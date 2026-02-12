using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service.Approvals;
using LeaseGate.Service.Leases;
using LeaseGate.Service.Telemetry;
using LeaseGate.Service.TokenPools;
using LeaseGate.Service.Tools;

namespace LeaseGate.Service;

public sealed class LeaseGovernor : IDisposable
{
    private readonly LeaseGovernorOptions _options;
    private readonly IPolicyEngine _policy;
    private readonly IAuditWriter _audit;
    private readonly ConcurrencyPool _concurrency;
    private readonly DailyBudgetPool _budget;
    private readonly RatePool _rate;
    private readonly ContextPool _context;
    private readonly ComputePool _compute;
    private readonly ToolRegistry _toolRegistry;
    private readonly ApprovalStore _approvals;
    private readonly MetricsRegistry _metrics;
    private readonly LeaseStore _leases = new();
    private readonly Timer _expiryTimer;
    private double _lastContextUtilization;

    public LeaseGovernor(LeaseGovernorOptions options, IPolicyEngine policy, IAuditWriter audit, ToolRegistry? toolRegistry = null)
    {
        _options = options;
        _policy = policy;
        _audit = audit;
        _concurrency = new ConcurrencyPool(options.MaxInFlight);
        _budget = new DailyBudgetPool(options.DailyBudgetCents);
        _rate = new RatePool(options.MaxRequestsPerMinute, options.MaxTokensPerMinute, options.RateWindow);
        _context = new ContextPool(options.MaxContextTokens, options.MaxRetrievedChunks, options.MaxToolOutputTokens);
        _compute = new ComputePool(options.MaxComputeUnits);
        _toolRegistry = toolRegistry ?? new ToolRegistry();
        _approvals = new ApprovalStore();
        _metrics = new MetricsRegistry();
        _expiryTimer = new Timer(_ => _ = ExpireLeasesAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ApprovalRequestResponse RequestApproval(ApprovalRequest request)
    {
        return _approvals.Create(request);
    }

    public GrantApprovalResponse GrantApproval(GrantApprovalRequest request)
    {
        return _approvals.Grant(request);
    }

    public DenyApprovalResponse DenyApproval(DenyApprovalRequest request)
    {
        return _approvals.Deny(request);
    }

    public MetricsSnapshot GetMetricsSnapshot()
    {
        return new MetricsSnapshot
        {
            ActiveLeases = _concurrency.Active,
            SpendTodayCents = _budget.ReservedCents,
            RatePoolUtilization = _rate.Utilization,
            ContextPoolUtilization = _lastContextUtilization,
            ComputePoolUtilization = _compute.Utilization,
            GrantsByReason = _metrics.SnapshotGrants(),
            DeniesByReason = _metrics.SnapshotDenies()
        };
    }

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        var existing = _leases.GetByIdempotency(request.IdempotencyKey);
        if (existing is not null)
        {
            return new AcquireLeaseResponse
            {
                Granted = true,
                LeaseId = existing.LeaseId,
                ExpiresAtUtc = existing.ExpiresAtUtc,
                Constraints = existing.Constraints,
                IdempotencyKey = request.IdempotencyKey
            };
        }

        if (!ValidateRequestedTools(request, out var toolValidationReason, out var toolValidationRecommendation))
        {
            var denied = Denied(request, toolValidationReason, null, toolValidationRecommendation);
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny(toolValidationReason);
            return denied;
        }

        var policyDecision = _policy.Evaluate(request);
        if (!policyDecision.Allowed)
        {
            var denied = Denied(request, policyDecision.DeniedReason, null, policyDecision.Recommendation);
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny(policyDecision.DeniedReason);
            return denied;
        }

        if (RequiresApproval(request) && !_approvals.ValidateToken(request.ApprovalToken, request.ActorId, request.WorkspaceId, request.RequestedTools))
        {
            var denied = Denied(
                request,
                "approval_required",
                0,
                "request approval and include approval token scoped to actor/workspace/tool");
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny("approval_required");
            return denied;
        }

        if (!_concurrency.TryAcquire(out var concurrencyRetryMs))
        {
            var denied = Denied(request, "concurrency_limit_reached", concurrencyRetryMs, "retry after active leases complete");
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny("concurrency_limit_reached");
            return denied;
        }

        if (!_compute.TryAcquire(request.EstimatedComputeUnits, out var computeRetryMs))
        {
            _concurrency.Release();
            var denied = Denied(request, "compute_capacity_reached", computeRetryMs, "retry or reduce compute requirement");
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny("compute_capacity_reached");
            return denied;
        }

        var estimatedTotalTokens = request.EstimatedPromptTokens + request.MaxOutputTokens;
        if (!_rate.TryAcquire(estimatedTotalTokens, out var rateRetryMs))
        {
            _compute.Release(request.EstimatedComputeUnits);
            _concurrency.Release();
            var denied = Denied(request, "rate_limit_reached", rateRetryMs, "backoff and retry with lower throughput");
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny("rate_limit_reached");
            return denied;
        }

        if (!_context.TryEvaluate(request, out var contextReason, out var contextRecommendation))
        {
            _compute.Release(request.EstimatedComputeUnits);
            _concurrency.Release();
            var denied = Denied(request, contextReason, 200, contextRecommendation);
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny(contextReason);
            return denied;
        }
        _lastContextUtilization = _context.Utilization(request);

        if (!_budget.TryReserve(request.EstimatedCostCents, out var budgetRetryMs))
        {
            _compute.Release(request.EstimatedComputeUnits);
            _concurrency.Release();
            var denied = Denied(request, "daily_budget_exceeded", budgetRetryMs, "switch model / reduce output tokens");
            await AuditDeniedAsync(request, denied, cancellationToken);
            _metrics.RecordDeny("daily_budget_exceeded");
            return denied;
        }

        var constraints = BuildConstraints(request);

        var lease = new LeaseRecord
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = request.IdempotencyKey,
            Request = request,
            Constraints = constraints,
            ReservedComputeUnits = Math.Max(1, request.EstimatedComputeUnits),
            AcquiredAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_options.LeaseTtl)
        };
        _leases.Add(lease);

        var response = new AcquireLeaseResponse
        {
            Granted = true,
            LeaseId = lease.LeaseId,
            ExpiresAtUtc = lease.ExpiresAtUtc,
            Constraints = constraints,
            IdempotencyKey = request.IdempotencyKey
        };

        await _audit.WriteAsync(new AuditEvent
        {
            EventType = "lease_acquired",
            TimestampUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash,
            LeaseId = lease.LeaseId,
            ActorId = request.ActorId,
            WorkspaceId = request.WorkspaceId,
            ActionType = request.ActionType.ToString(),
            ModelId = request.ModelId,
            EstimatedCostCents = request.EstimatedCostCents,
            RequestedTools = request.RequestedTools.Select(t => $"{t.ToolId}:{t.Category}").ToList(),
            Decision = "granted"
        }, cancellationToken);

        _metrics.RecordGrant("granted");

        return response;
    }

    public async Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        var lease = _leases.Remove(request.LeaseId);
        if (lease is null)
        {
            return new ReleaseLeaseResponse
            {
                Classification = ReleaseClassification.LeaseNotFound,
                Recommendation = "lease missing or already expired",
                IdempotencyKey = request.IdempotencyKey
            };
        }

        _concurrency.Release();
        _compute.Release(lease.ReservedComputeUnits);
        _budget.Settle(lease.Request.EstimatedCostCents, request.ActualCostCents);

        var recommendation = BuildReleaseRecommendation(request);

        await _audit.WriteAsync(new AuditEvent
        {
            EventType = "lease_released",
            TimestampUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash,
            LeaseId = lease.LeaseId,
            ActorId = lease.Request.ActorId,
            WorkspaceId = lease.Request.WorkspaceId,
            ActionType = lease.Request.ActionType.ToString(),
            ModelId = lease.Request.ModelId,
            EstimatedCostCents = lease.Request.EstimatedCostCents,
            ActualCostCents = request.ActualCostCents,
            RequestedTools = lease.Request.RequestedTools.Select(t => $"{t.ToolId}:{t.Category}").ToList(),
            ToolUsageSummary = request.ToolCalls.Select(t => $"{t.ToolId}:{t.Outcome}:{t.DurationMs}ms").ToList(),
            Decision = request.Outcome.ToString(),
            Recommendation = recommendation
        }, cancellationToken);

        return new ReleaseLeaseResponse
        {
            Classification = ReleaseClassification.Recorded,
            Recommendation = recommendation,
            IdempotencyKey = request.IdempotencyKey
        };
    }

    private LeaseConstraints BuildConstraints(AcquireLeaseRequest request)
    {
        return new LeaseConstraints
        {
            MaxOutputTokensOverride = Math.Min(request.MaxOutputTokens, _options.MaxToolOutputTokens),
            MaxToolCalls = _options.MaxToolCallsPerLease,
            MaxContextTokens = Math.Min(_options.MaxContextTokens, Math.Max(request.RequestedContextTokens, 0)),
            CooldownMs = null
        };
    }

    private bool RequiresApproval(AcquireLeaseRequest request)
    {
        var approvalCategories = _policy.CurrentSnapshot.Policy.ApprovalRequiredToolCategories.ToHashSet();
        return request.RequestedTools.Any(tool => approvalCategories.Contains(tool.Category));
    }

    private bool ValidateRequestedTools(AcquireLeaseRequest request, out string reason, out string recommendation)
    {
        foreach (var tool in request.RequestedTools)
        {
            if (_toolRegistry.TryGet(tool.ToolId, out var definition) && definition is not null)
            {
                if (definition.Category != tool.Category)
                {
                    reason = $"tool_category_mismatch:{tool.ToolId}";
                    recommendation = "fix requested tool category";
                    return false;
                }

                continue;
            }

            reason = $"tool_not_registered:{tool.ToolId}";
            recommendation = "register tool before use";
            return false;
        }

        reason = string.Empty;
        recommendation = string.Empty;
        return true;
    }

    private static string BuildReleaseRecommendation(ReleaseLeaseRequest request)
    {
        return request.ProviderErrorClassification switch
        {
            ProviderErrorClassification.RateLimited => "backoff and retry",
            ProviderErrorClassification.Timeout => "reduce context or increase provider timeout",
            ProviderErrorClassification.ContextTooLarge => "reduce context tokens or chunks",
            ProviderErrorClassification.ModelUnavailable => "switch model",
            ProviderErrorClassification.Unauthorized => "check provider credentials",
            _ when request.Outcome == LeaseOutcome.PolicyDenied => "request approval or update policy",
            _ => "continue"
        };
    }

    private static AcquireLeaseResponse Denied(
        AcquireLeaseRequest request,
        string reason,
        int? retryAfterMs,
        string recommendation)
    {
        return new AcquireLeaseResponse
        {
            Granted = false,
            LeaseId = string.Empty,
            ExpiresAtUtc = DateTimeOffset.MinValue,
            Constraints = new LeaseConstraints(),
            DeniedReason = reason,
            RetryAfterMs = retryAfterMs,
            Recommendation = recommendation,
            IdempotencyKey = request.IdempotencyKey
        };
    }

    private async Task AuditDeniedAsync(AcquireLeaseRequest request, AcquireLeaseResponse denied, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(new AuditEvent
        {
            EventType = "lease_denied",
            TimestampUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash,
            LeaseId = string.Empty,
            ActorId = request.ActorId,
            WorkspaceId = request.WorkspaceId,
            ActionType = request.ActionType.ToString(),
            ModelId = request.ModelId,
            EstimatedCostCents = request.EstimatedCostCents,
            RequestedTools = request.RequestedTools.Select(t => $"{t.ToolId}:{t.Category}").ToList(),
            Decision = "denied",
            Reason = denied.DeniedReason,
            Recommendation = denied.Recommendation
        }, cancellationToken);
    }

    private async Task ExpireLeasesAsync()
    {
        var expired = _leases.RemoveExpired(DateTimeOffset.UtcNow);
        if (expired.Count == 0)
        {
            return;
        }

        foreach (var lease in expired)
        {
            _concurrency.Release();
            _compute.Release(lease.ReservedComputeUnits);
            _budget.ReleaseReservation(lease.Request.EstimatedCostCents);

            await _audit.WriteAsync(new AuditEvent
            {
                EventType = "lease_expired",
                TimestampUtc = DateTimeOffset.UtcNow,
                ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
                PolicyHash = _policy.CurrentSnapshot.PolicyHash,
                LeaseId = lease.LeaseId,
                ActorId = lease.Request.ActorId,
                WorkspaceId = lease.Request.WorkspaceId,
                ActionType = lease.Request.ActionType.ToString(),
                ModelId = lease.Request.ModelId,
                EstimatedCostCents = lease.Request.EstimatedCostCents,
                RequestedTools = lease.Request.RequestedTools.Select(t => $"{t.ToolId}:{t.Category}").ToList(),
                Decision = "expired"
            }, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        _expiryTimer.Dispose();
    }
}
