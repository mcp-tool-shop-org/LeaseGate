using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service.Approvals;
using LeaseGate.Service.Leases;
using LeaseGate.Service.Telemetry;
using LeaseGate.Service.TokenPools;
using LeaseGate.Service.ToolIsolation;
using LeaseGate.Service.Tools;
using LeaseGate.Storage;
using System.Text;

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
    private readonly ToolSubLeaseStore _toolSubLeases;
    private readonly IsolatedToolRunner _toolRunner;
    private readonly ApprovalStore _approvals;
    private readonly MetricsRegistry _metrics;
    private readonly LeaseStore _leases = new();
    private readonly Timer _expiryTimer;
    private readonly ILeaseGateStateStore? _stateStore;
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
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
        _toolSubLeases = new ToolSubLeaseStore();
        _toolRunner = new IsolatedToolRunner();
        _approvals = new ApprovalStore();
        _metrics = new MetricsRegistry();
        _stateStore = options.EnableDurableState ? new SqliteLeaseGateStateStore(options.StateDatabasePath) : null;
        RecoverDurableState();
        _expiryTimer = new Timer(_ => _ = ExpireLeasesAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ApprovalRequestResponse RequestApproval(ApprovalRequest request)
    {
        var response = _approvals.Create(request);
        PersistApprovals();
        return response;
    }

    public GrantApprovalResponse GrantApproval(GrantApprovalRequest request)
    {
        var response = _approvals.Grant(request);
        PersistApprovals();
        return response;
    }

    public DenyApprovalResponse DenyApproval(DenyApprovalRequest request)
    {
        var response = _approvals.Deny(request);
        PersistApprovals();
        return response;
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

    public GovernorStatusResponse GetStatus()
    {
        var approvals = _approvals.Snapshot();
        var pendingApprovals = approvals.Count(a => a.Status == ApprovalDecisionStatus.Pending && a.ExpiresAtUtc > DateTimeOffset.UtcNow);

        return new GovernorStatusResponse
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = _startedAtUtc,
            Healthy = true,
            DurableStateEnabled = _options.EnableDurableState,
            StateDatabasePath = _options.StateDatabasePath,
            ActiveLeases = _concurrency.Active,
            PendingApprovals = pendingApprovals,
            SpendTodayCents = _budget.ReservedCents,
            PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash
        };
    }

    public ExportDiagnosticsResponse ExportDiagnostics(ExportDiagnosticsRequest request)
    {
        try
        {
            var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
                ? Path.Combine(Path.GetTempPath(), $"leasegate-diagnostics-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json")
                : request.OutputPath;

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new
            {
                Status = GetStatus(),
                Metrics = GetMetricsSnapshot(),
                Pools = new
                {
                    Concurrency = new { Active = _concurrency.Active, Capacity = _options.MaxInFlight },
                    Compute = new { Utilization = _compute.Utilization, Capacity = _options.MaxComputeUnits },
                    Rate = new
                    {
                        Utilization = _rate.Utilization,
                        MaxRequestsPerMinute = _options.MaxRequestsPerMinute,
                        MaxTokensPerMinute = _options.MaxTokensPerMinute,
                        WindowSeconds = _options.RateWindow.TotalSeconds
                    },
                    Context = new
                    {
                        Utilization = _lastContextUtilization,
                        MaxContextTokens = _options.MaxContextTokens,
                        MaxRetrievedChunks = _options.MaxRetrievedChunks,
                        MaxToolOutputTokens = _options.MaxToolOutputTokens
                    },
                    Budget = new { ReservedCents = _budget.ReservedCents, LimitCents = _options.DailyBudgetCents }
                }
            };

            File.WriteAllText(outputPath, ProtocolJson.Serialize(payload), Encoding.UTF8);

            return new ExportDiagnosticsResponse
            {
                Exported = true,
                OutputPath = outputPath,
                Message = "diagnostics exported",
                IdempotencyKey = request.IdempotencyKey
            };
        }
        catch (Exception ex)
        {
            return new ExportDiagnosticsResponse
            {
                Exported = false,
                OutputPath = request.OutputPath,
                Message = ex.Message,
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    public StagePolicyBundleResponse StagePolicyBundle(PolicyBundle bundle)
    {
        return _policy.StageBundle(bundle);
    }

    public ToolSubLeaseResponse RequestToolSubLease(ToolSubLeaseRequest request)
    {
        var lease = _leases.GetByLeaseId(request.LeaseId);
        if (lease is null)
        {
            return new ToolSubLeaseResponse
            {
                Granted = false,
                DeniedReason = "lease_not_found",
                Recommendation = "acquire a valid model lease first",
                IdempotencyKey = request.IdempotencyKey
            };
        }

        var allowedCalls = Math.Min(Math.Max(1, request.RequestedCalls), lease.Constraints.MaxToolCalls ?? _options.MaxToolCallsPerLease);
        var timeoutMs = Math.Min(Math.Max(100, request.TimeoutMs), _policy.CurrentSnapshot.Policy.DefaultToolTimeoutMs);
        var maxBytes = Math.Min(Math.Max(256, request.MaxOutputBytes), _policy.CurrentSnapshot.Policy.MaxToolOutputBytes);

        var subLease = _toolSubLeases.Add(
            request.LeaseId,
            request.ToolId,
            request.Category,
            allowedCalls,
            timeoutMs,
            maxBytes,
            lease.ExpiresAtUtc);

        return new ToolSubLeaseResponse
        {
            Granted = true,
            ToolSubLeaseId = subLease.ToolSubLeaseId,
            ExpiresAtUtc = subLease.ExpiresAtUtc,
            AllowedCalls = subLease.RemainingCalls,
            TimeoutMs = subLease.TimeoutMs,
            MaxOutputBytes = subLease.MaxOutputBytes,
            Recommendation = "sub-lease granted",
            IdempotencyKey = request.IdempotencyKey
        };
    }

    public async Task<ToolExecutionResponse> ExecuteToolCallAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!_toolSubLeases.TryConsume(request.ToolSubLeaseId, request.LeaseId, request.ToolId, request.Category, out var subLease, out var denyReason) || subLease is null)
        {
            var denied = new ToolExecutionResponse
            {
                Allowed = false,
                Outcome = LeaseOutcome.PolicyDenied,
                DeniedReason = denyReason,
                Recommendation = "request a valid scoped tool sub-lease",
                IdempotencyKey = request.IdempotencyKey
            };

            await _audit.WriteAsync(new AuditEvent
            {
                EventType = "tool_execution_denied",
                TimestampUtc = DateTimeOffset.UtcNow,
                ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
                PolicyHash = _policy.CurrentSnapshot.PolicyHash,
                LeaseId = request.LeaseId,
                ActorId = "tool",
                WorkspaceId = "tool",
                ActionType = ActionType.ToolCall.ToString(),
                ModelId = string.Empty,
                RequestedTools = new List<string> { $"{request.ToolId}:{request.Category}" },
                Decision = "denied",
                Reason = denied.DeniedReason,
                Recommendation = denied.Recommendation
            }, cancellationToken);

            return denied;
        }

        var result = await _toolRunner.ExecuteAsync(request, subLease, _policy.CurrentSnapshot.Policy, cancellationToken);
        await _audit.WriteAsync(new AuditEvent
        {
            EventType = result.Allowed ? "tool_execution_completed" : "tool_execution_blocked",
            TimestampUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash,
            LeaseId = request.LeaseId,
            ActorId = "tool",
            WorkspaceId = "tool",
            ActionType = ActionType.ToolCall.ToString(),
            ModelId = string.Empty,
            RequestedTools = new List<string> { $"{request.ToolId}:{request.Category}" },
            ToolUsageSummary = new List<string> { $"bytes={result.OutputBytes};ms={result.DurationMs}" },
            Decision = result.Outcome.ToString(),
            Reason = result.DeniedReason,
            Recommendation = result.Recommendation
        }, cancellationToken);

        return result;
    }

    public async Task<ActivatePolicyResponse> ActivatePolicyAsync(ActivatePolicyRequest request, CancellationToken cancellationToken)
    {
        var activation = _policy.ActivateStaged(request);
        if (activation.Activated)
        {
            PersistPolicyState();
            await _audit.WriteAsync(new AuditEvent
            {
                EventType = "policy_activated",
                TimestampUtc = DateTimeOffset.UtcNow,
                ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
                PolicyHash = activation.ActivePolicyHash,
                LeaseId = string.Empty,
                ActorId = "system",
                WorkspaceId = "system",
                ActionType = ActionType.WorkflowStep.ToString(),
                ModelId = string.Empty,
                Decision = "activated",
                Recommendation = activation.ActivePolicyVersion
            }, cancellationToken);
        }

        return activation;
    }

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        await ExpireLeasesAsync();

        var existing = _leases.GetByIdempotency(request.IdempotencyKey);
        if (existing is not null)
        {
            return new AcquireLeaseResponse
            {
                Granted = true,
                LeaseId = existing.LeaseId,
                ExpiresAtUtc = existing.ExpiresAtUtc,
                Constraints = existing.Constraints,
                IdempotencyKey = request.IdempotencyKey,
                PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
                PolicyHash = _policy.CurrentSnapshot.PolicyHash
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
        PersistLease(lease);
        PersistBudgetAndRate();
        PersistPolicyState();

        var response = new AcquireLeaseResponse
        {
            Granted = true,
            LeaseId = lease.LeaseId,
            ExpiresAtUtc = lease.ExpiresAtUtc,
            Constraints = constraints,
            IdempotencyKey = request.IdempotencyKey,
            PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash
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
        _toolSubLeases.RemoveByLease(request.LeaseId);
        _stateStore?.RemoveLease(request.LeaseId);
        PersistBudgetAndRate();

        var recommendation = BuildReleaseRecommendation(request);

        var auditResult = await _audit.WriteAsync(new AuditEvent
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
            IdempotencyKey = request.IdempotencyKey,
            PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash,
            Receipt = request.ActualCostCents >= _options.ReceiptThresholdCostCents
                ? new LeaseReceipt
                {
                    LeaseId = lease.LeaseId,
                    PolicyHash = _policy.CurrentSnapshot.PolicyHash,
                    ActualPromptTokens = request.ActualPromptTokens,
                    ActualOutputTokens = request.ActualOutputTokens,
                    ActualCostCents = request.ActualCostCents,
                    Outcome = request.Outcome,
                    AuditEntryHash = auditResult.EntryHash,
                    TimestampUtc = DateTimeOffset.UtcNow
                }
                : null
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

    private AcquireLeaseResponse Denied(
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
            IdempotencyKey = request.IdempotencyKey,
            PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash
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
            _toolSubLeases.RemoveByLease(lease.LeaseId);
            _stateStore?.RemoveLease(lease.LeaseId);
            PersistBudgetAndRate();

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

    private void RecoverDurableState()
    {
        if (_stateStore is null)
        {
            return;
        }

        var snapshot = _stateStore.Load();

        if (snapshot.BudgetState is not null)
        {
            _budget.RestoreState(snapshot.BudgetState.DateUtc, snapshot.BudgetState.ReservedCents);
        }

        if (snapshot.RateEvents.Count > 0)
        {
            _rate.RestoreEvents(snapshot.RateEvents.Select(e => (e.TimestampUtc, e.TokenCost)));
        }

        var restoredApprovals = snapshot.Approvals.Select(a => new ApprovalRecord
        {
            ApprovalId = a.ApprovalId,
            Status = Enum.TryParse<ApprovalDecisionStatus>(a.Status, true, out var parsedStatus)
                ? parsedStatus
                : ApprovalDecisionStatus.Pending,
            ExpiresAtUtc = a.ExpiresAtUtc,
            Token = a.Token,
            Used = a.Used,
            Request = ProtocolJson.Deserialize<ApprovalRequest>(a.RequestJson)
        }).ToList();
        _approvals.Restore(restoredApprovals);

        var now = DateTimeOffset.UtcNow;
        foreach (var persistedLease in snapshot.ActiveLeases)
        {
            var request = ProtocolJson.Deserialize<AcquireLeaseRequest>(persistedLease.RequestJson);
            var constraints = ProtocolJson.Deserialize<LeaseConstraints>(persistedLease.ConstraintsJson);

            if (persistedLease.ExpiresAtUtc <= now)
            {
                _ = _audit.WriteAsync(new AuditEvent
                {
                    EventType = "lease_expired_by_restart",
                    TimestampUtc = now,
                    ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
                    PolicyHash = _policy.CurrentSnapshot.PolicyHash,
                    LeaseId = persistedLease.LeaseId,
                    ActorId = request.ActorId,
                    WorkspaceId = request.WorkspaceId,
                    ActionType = request.ActionType.ToString(),
                    ModelId = request.ModelId,
                    EstimatedCostCents = request.EstimatedCostCents,
                    Decision = "expired"
                }, CancellationToken.None);
                _stateStore.RemoveLease(persistedLease.LeaseId);
                continue;
            }

            _concurrency.TryAcquire(out _);
            _compute.TryAcquire(persistedLease.ReservedComputeUnits, out _);
            _budget.TryReserve(request.EstimatedCostCents, out _);

            _leases.Add(new LeaseRecord
            {
                LeaseId = persistedLease.LeaseId,
                IdempotencyKey = persistedLease.IdempotencyKey,
                Request = request,
                Constraints = constraints,
                ReservedComputeUnits = persistedLease.ReservedComputeUnits,
                AcquiredAtUtc = persistedLease.AcquiredAtUtc,
                ExpiresAtUtc = persistedLease.ExpiresAtUtc
            });
        }

        PersistBudgetAndRate();
        PersistApprovals();
        PersistPolicyState();
    }

    private void PersistLease(LeaseRecord lease)
    {
        if (_stateStore is null)
        {
            return;
        }

        _stateStore.UpsertLease(new StoredLease
        {
            LeaseId = lease.LeaseId,
            IdempotencyKey = lease.IdempotencyKey,
            AcquiredAtUtc = lease.AcquiredAtUtc,
            ExpiresAtUtc = lease.ExpiresAtUtc,
            ReservedComputeUnits = lease.ReservedComputeUnits,
            RequestJson = ProtocolJson.Serialize(lease.Request),
            ConstraintsJson = ProtocolJson.Serialize(lease.Constraints)
        });
    }

    private void PersistApprovals()
    {
        if (_stateStore is null)
        {
            return;
        }

        var approvals = _approvals.Snapshot().Select(a => new StoredApproval
        {
            ApprovalId = a.ApprovalId,
            Status = a.Status.ToString(),
            ExpiresAtUtc = a.ExpiresAtUtc,
            Token = a.Token,
            Used = a.Used,
            RequestJson = ProtocolJson.Serialize(a.Request)
        }).ToList();

        _stateStore.ReplaceApprovals(approvals);
    }

    private void PersistBudgetAndRate()
    {
        if (_stateStore is null)
        {
            return;
        }

        _stateStore.SaveBudgetState(new StoredBudgetState
        {
            DateUtc = _budget.CurrentDateUtc,
            ReservedCents = _budget.ReservedCents
        });

        var events = _rate.SnapshotEvents().Select(e => new StoredRateEvent
        {
            TimestampUtc = e.TimestampUtc,
            TokenCost = e.TokenCost
        }).ToList();
        _stateStore.ReplaceRateEvents(events);
    }

    private void PersistPolicyState()
    {
        if (_stateStore is null)
        {
            return;
        }

        _stateStore.SavePolicyState(new StoredPolicyState
        {
            PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
            PolicyHash = _policy.CurrentSnapshot.PolicyHash
        });
    }

    public void Dispose()
    {
        _expiryTimer.Dispose();
    }
}
