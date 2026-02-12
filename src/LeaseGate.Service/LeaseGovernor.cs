using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service.Leases;
using LeaseGate.Service.TokenPools;

namespace LeaseGate.Service;

public sealed class LeaseGovernor : IDisposable
{
    private readonly LeaseGovernorOptions _options;
    private readonly IPolicyEngine _policy;
    private readonly IAuditWriter _audit;
    private readonly ConcurrencyPool _concurrency;
    private readonly DailyBudgetPool _budget;
    private readonly LeaseStore _leases = new();
    private readonly Timer _expiryTimer;

    public LeaseGovernor(LeaseGovernorOptions options, IPolicyEngine policy, IAuditWriter audit)
    {
        _options = options;
        _policy = policy;
        _audit = audit;
        _concurrency = new ConcurrencyPool(options.MaxInFlight);
        _budget = new DailyBudgetPool(options.DailyBudgetCents);
        _expiryTimer = new Timer(_ => _ = ExpireLeasesAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
                Constraints = new LeaseConstraints(),
                IdempotencyKey = request.IdempotencyKey
            };
        }

        var policyDecision = _policy.Evaluate(request);
        if (!policyDecision.Allowed)
        {
            var denied = Denied(request, policyDecision.DeniedReason, null, policyDecision.Recommendation);
            await AuditDeniedAsync(request, denied, cancellationToken);
            return denied;
        }

        if (!_concurrency.TryAcquire(out var concurrencyRetryMs))
        {
            var denied = Denied(request, "concurrency_limit_reached", concurrencyRetryMs, "retry after active leases complete");
            await AuditDeniedAsync(request, denied, cancellationToken);
            return denied;
        }

        if (!_budget.TryReserve(request.EstimatedCostCents, out var budgetRetryMs))
        {
            _concurrency.Release();
            var denied = Denied(request, "daily_budget_exceeded", budgetRetryMs, "switch model / reduce output tokens");
            await AuditDeniedAsync(request, denied, cancellationToken);
            return denied;
        }

        var lease = new LeaseRecord
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = request.IdempotencyKey,
            Request = request,
            AcquiredAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_options.LeaseTtl)
        };
        _leases.Add(lease);

        var response = new AcquireLeaseResponse
        {
            Granted = true,
            LeaseId = lease.LeaseId,
            ExpiresAtUtc = lease.ExpiresAtUtc,
            Constraints = new LeaseConstraints(),
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
            Decision = "granted"
        }, cancellationToken);

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
        _budget.Settle(lease.Request.EstimatedCostCents, request.ActualCostCents);

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
            Decision = request.Outcome.ToString(),
            Recommendation = "continue"
        }, cancellationToken);

        return new ReleaseLeaseResponse
        {
            Classification = ReleaseClassification.Recorded,
            Recommendation = "continue",
            IdempotencyKey = request.IdempotencyKey
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
                Decision = "expired"
            }, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        _expiryTimer.Dispose();
    }
}
