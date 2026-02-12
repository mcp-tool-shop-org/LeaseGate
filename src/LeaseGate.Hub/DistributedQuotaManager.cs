using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service.TokenPools;

namespace LeaseGate.Hub;

internal sealed class DistributedQuotaManager
{
    private readonly object _lock = new();
    private DateOnly _currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _orgSpendCents;
    private readonly Dictionary<string, int> _workspaceSpend = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _actorSpend = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _actorInFlight = new(StringComparer.OrdinalIgnoreCase);

    private RatePool? _orgRatePool;
    private readonly Dictionary<string, RatePool> _workspaceRatePools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RatePool> _actorRatePools = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(AcquireLeaseRequest request, LeaseGatePolicy policy, out string denyReason, out int? retryAfterMs, out DateTimeOffset? nextRefillUtc)
    {
        lock (_lock)
        {
            RollDayIfNeeded();

            denyReason = string.Empty;
            retryAfterMs = null;
            nextRefillUtc = null;
            var estimatedTokens = request.EstimatedPromptTokens + request.MaxOutputTokens;

            if (policy.OrgDailyBudgetCents > 0 && _orgSpendCents + request.EstimatedCostCents > policy.OrgDailyBudgetCents)
            {
                denyReason = "org_exhausted";
                nextRefillUtc = NextDayRefillUtc();
                retryAfterMs = (int)Math.Max(250, (nextRefillUtc.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                return false;
            }

            if (policy.WorkspaceDailyBudgetCents.TryGetValue(request.WorkspaceId, out var workspaceBudget) &&
                workspaceBudget > 0 &&
                _workspaceSpend.GetValueOrDefault(request.WorkspaceId) + request.EstimatedCostCents > workspaceBudget)
            {
                denyReason = "workspace_exhausted";
                nextRefillUtc = NextDayRefillUtc();
                retryAfterMs = (int)Math.Max(250, (nextRefillUtc.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                return false;
            }

            if (policy.ActorDailyBudgetCents.TryGetValue(request.ActorId, out var actorBudget) &&
                actorBudget > 0 &&
                _actorSpend.GetValueOrDefault(request.ActorId) + request.EstimatedCostCents > actorBudget)
            {
                denyReason = "actor_exhausted";
                nextRefillUtc = NextDayRefillUtc();
                retryAfterMs = (int)Math.Max(250, (nextRefillUtc.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                return false;
            }

            var maxInFlight = policy.MaxInFlightPerActor > 0 ? policy.MaxInFlightPerActor : int.MaxValue;
            if (policy.RoleMaxInFlightOverrides.TryGetValue(request.Role, out var roleOverride) && roleOverride > 0)
            {
                maxInFlight = roleOverride;
            }

            if (_actorInFlight.GetValueOrDefault(request.ActorId) >= maxInFlight)
            {
                denyReason = "actor_throttled";
                retryAfterMs = 1000;
                nextRefillUtc = DateTimeOffset.UtcNow.AddMilliseconds(retryAfterMs.Value);
                return false;
            }

            if (policy.OrgMaxRequestsPerMinute > 0 || policy.OrgMaxTokensPerMinute > 0)
            {
                _orgRatePool ??= new RatePool(
                    policy.OrgMaxRequestsPerMinute > 0 ? policy.OrgMaxRequestsPerMinute : int.MaxValue,
                    policy.OrgMaxTokensPerMinute > 0 ? policy.OrgMaxTokensPerMinute : int.MaxValue,
                    TimeSpan.FromMinutes(1));

                if (!_orgRatePool.TryAcquire(estimatedTokens, out var retry))
                {
                    denyReason = "org_exhausted";
                    retryAfterMs = retry;
                    nextRefillUtc = DateTimeOffset.UtcNow.AddMilliseconds(retry);
                    return false;
                }
            }

            var hasWorkspaceReq = policy.WorkspaceMaxRequestsPerMinute.TryGetValue(request.WorkspaceId, out var wsReq);
            var hasWorkspaceTok = policy.WorkspaceMaxTokensPerMinute.TryGetValue(request.WorkspaceId, out var wsTok);
            if (hasWorkspaceReq || hasWorkspaceTok)
            {
                var workspacePool = GetWorkspacePool(request.WorkspaceId, wsReq, wsTok);
                if (!workspacePool.TryAcquire(estimatedTokens, out var retry))
                {
                    denyReason = "workspace_exhausted";
                    retryAfterMs = retry;
                    nextRefillUtc = DateTimeOffset.UtcNow.AddMilliseconds(retry);
                    return false;
                }
            }

            if (policy.ActorMaxRequestsPerMinute > 0 || policy.ActorMaxTokensPerMinute > 0)
            {
                var actorPool = GetActorPool(request.ActorId, policy.ActorMaxRequestsPerMinute, policy.ActorMaxTokensPerMinute);
                if (!actorPool.TryAcquire(estimatedTokens, out var retry))
                {
                    denyReason = "actor_throttled";
                    retryAfterMs = retry;
                    nextRefillUtc = DateTimeOffset.UtcNow.AddMilliseconds(retry);
                    return false;
                }
            }

            _orgSpendCents += request.EstimatedCostCents;
            _workspaceSpend[request.WorkspaceId] = _workspaceSpend.GetValueOrDefault(request.WorkspaceId) + request.EstimatedCostCents;
            _actorSpend[request.ActorId] = _actorSpend.GetValueOrDefault(request.ActorId) + request.EstimatedCostCents;
            _actorInFlight[request.ActorId] = _actorInFlight.GetValueOrDefault(request.ActorId) + 1;
            return true;
        }
    }

    public void Release(AcquireLeaseRequest request)
    {
        lock (_lock)
        {
            if (_actorInFlight.TryGetValue(request.ActorId, out var active) && active > 0)
            {
                _actorInFlight[request.ActorId] = active - 1;
            }
        }
    }

    public void Rollback(AcquireLeaseRequest request)
    {
        lock (_lock)
        {
            _orgSpendCents = Math.Max(0, _orgSpendCents - request.EstimatedCostCents);

            if (_workspaceSpend.TryGetValue(request.WorkspaceId, out var workspaceSpent))
            {
                _workspaceSpend[request.WorkspaceId] = Math.Max(0, workspaceSpent - request.EstimatedCostCents);
            }

            if (_actorSpend.TryGetValue(request.ActorId, out var actorSpent))
            {
                _actorSpend[request.ActorId] = Math.Max(0, actorSpent - request.EstimatedCostCents);
            }

            if (_actorInFlight.TryGetValue(request.ActorId, out var active) && active > 0)
            {
                _actorInFlight[request.ActorId] = active - 1;
            }
        }
    }

    private RatePool GetWorkspacePool(string workspaceId, int req, int tok)
    {
        if (_workspaceRatePools.TryGetValue(workspaceId, out var existing))
        {
            return existing;
        }

        var created = new RatePool(req > 0 ? req : int.MaxValue, tok > 0 ? tok : int.MaxValue, TimeSpan.FromMinutes(1));
        _workspaceRatePools[workspaceId] = created;
        return created;
    }

    private RatePool GetActorPool(string actorId, int req, int tok)
    {
        if (_actorRatePools.TryGetValue(actorId, out var existing))
        {
            return existing;
        }

        var created = new RatePool(req > 0 ? req : int.MaxValue, tok > 0 ? tok : int.MaxValue, TimeSpan.FromMinutes(1));
        _actorRatePools[actorId] = created;
        return created;
    }

    private void RollDayIfNeeded()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        if (now == _currentDate)
        {
            return;
        }

        _currentDate = now;
        _orgSpendCents = 0;
        _workspaceSpend.Clear();
        _actorSpend.Clear();
        _actorInFlight.Clear();
    }

    private static DateTimeOffset NextDayRefillUtc()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        return new DateTimeOffset(tomorrow.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}
