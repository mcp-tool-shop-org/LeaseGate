using LeaseGate.Protocol;

namespace LeaseGate.Service.Safety;

internal sealed class SafetyIntervention
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Trigger { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

internal sealed class SafetyAutomationState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _policyDenyByWorkspace = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retryByIdempotency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _toolLoopByLeaseTool = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _workspaceCircuitBreakerUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _actorCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _actorOutputClamp = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SafetyIntervention> _interventions = new();

    public bool IsWorkspaceCircuitBroken(string workspaceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_workspaceCircuitBreakerUntil.TryGetValue(workspaceId, out var until) && until > now)
            {
                return true;
            }

            return false;
        }
    }

    public bool IsActorOnCooldown(string actorId, DateTimeOffset now, out int retryAfterMs)
    {
        lock (_lock)
        {
            if (_actorCooldownUntil.TryGetValue(actorId, out var until) && until > now)
            {
                retryAfterMs = (int)Math.Max(250, (until - now).TotalMilliseconds);
                return true;
            }

            retryAfterMs = 0;
            return false;
        }
    }

    public int? GetActorOutputClamp(string actorId)
    {
        lock (_lock)
        {
            if (_actorOutputClamp.TryGetValue(actorId, out var clamp) && clamp > 0)
            {
                return clamp;
            }

            return null;
        }
    }

    public bool RegisterRetryAndCheckThreshold(string idempotencyKey, int threshold)
    {
        lock (_lock)
        {
            _retryByIdempotency[idempotencyKey] = _retryByIdempotency.GetValueOrDefault(idempotencyKey) + 1;
            return _retryByIdempotency[idempotencyKey] >= threshold;
        }
    }

    public bool RegisterPolicyDenyAndCheckThreshold(string workspaceId, int threshold)
    {
        lock (_lock)
        {
            _policyDenyByWorkspace[workspaceId] = _policyDenyByWorkspace.GetValueOrDefault(workspaceId) + 1;
            return _policyDenyByWorkspace[workspaceId] >= threshold;
        }
    }

    public bool RegisterToolLoopAndCheckThreshold(string leaseId, string toolId, int threshold)
    {
        lock (_lock)
        {
            var key = $"{leaseId}|{toolId}";
            _toolLoopByLeaseTool[key] = _toolLoopByLeaseTool.GetValueOrDefault(key) + 1;
            return _toolLoopByLeaseTool[key] >= threshold;
        }
    }

    public void ApplyWorkspaceCircuitBreaker(string workspaceId, TimeSpan duration, string trigger, string detail)
    {
        lock (_lock)
        {
            var until = DateTimeOffset.UtcNow.Add(duration);
            _workspaceCircuitBreakerUntil[workspaceId] = until;
            _interventions.Add(new SafetyIntervention
            {
                Trigger = trigger,
                Scope = $"workspace:{workspaceId}",
                Action = "circuit_breaker",
                Detail = detail
            });
        }
    }

    public void ApplyActorCooldown(string actorId, TimeSpan duration, string trigger, string detail)
    {
        lock (_lock)
        {
            var until = DateTimeOffset.UtcNow.Add(duration);
            _actorCooldownUntil[actorId] = until;
            _interventions.Add(new SafetyIntervention
            {
                Trigger = trigger,
                Scope = $"actor:{actorId}",
                Action = "cooldown",
                Detail = detail
            });
        }
    }

    public void ApplyActorClamp(string actorId, int maxOutputTokens, string trigger, string detail)
    {
        lock (_lock)
        {
            _actorOutputClamp[actorId] = maxOutputTokens;
            _interventions.Add(new SafetyIntervention
            {
                Trigger = trigger,
                Scope = $"actor:{actorId}",
                Action = "clamp_max_output_tokens",
                Detail = detail
            });
        }
    }

    public List<SafetyIntervention> SnapshotInterventions()
    {
        lock (_lock)
        {
            return _interventions.Select(i => new SafetyIntervention
            {
                TimestampUtc = i.TimestampUtc,
                Trigger = i.Trigger,
                Scope = i.Scope,
                Action = i.Action,
                Detail = i.Detail
            }).ToList();
        }
    }
}
