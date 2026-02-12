using LeaseGate.Protocol;

namespace LeaseGate.Hub;

internal sealed class CostAttributionTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CostAttributionRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _deniedReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> _recentDenials = new();

    public int SurgeDenialsThreshold { get; set; } = 8;
    public int RepeatedPolicyDenialThreshold { get; set; } = 5;

    public void RecordDenied(AcquireLeaseRequest request, string deniedReason)
    {
        lock (_lock)
        {
            _deniedReasons[deniedReason] = _deniedReasons.GetValueOrDefault(deniedReason) + 1;
            _recentDenials.Enqueue(DateTimeOffset.UtcNow);
            TrimRecentDenials();

            var key = Key(request.OrgId, request.WorkspaceId, request.ActorId, request.ModelId, "-");
            if (!_rows.TryGetValue(key, out var row))
            {
                row = new CostAttributionRow
                {
                    OrgId = request.OrgId,
                    WorkspaceId = request.WorkspaceId,
                    ActorId = request.ActorId,
                    ModelId = request.ModelId,
                    ToolId = "-"
                };
                _rows[key] = row;
            }

            row.Count += 1;
        }
    }

    public void RecordRelease(AcquireLeaseRequest request, ReleaseLeaseRequest release)
    {
        lock (_lock)
        {
            var modelKey = Key(request.OrgId, request.WorkspaceId, request.ActorId, request.ModelId, "-");
            var modelRow = GetOrCreate(modelKey, request.OrgId, request.WorkspaceId, request.ActorId, request.ModelId, "-");
            modelRow.SpendCents += release.ActualCostCents;
            modelRow.Count += 1;

            foreach (var tool in release.ToolCalls)
            {
                var toolKey = Key(request.OrgId, request.WorkspaceId, request.ActorId, request.ModelId, tool.ToolId);
                var toolRow = GetOrCreate(toolKey, request.OrgId, request.WorkspaceId, request.ActorId, request.ModelId, tool.ToolId);
                toolRow.SpendCents += release.ActualCostCents;
                toolRow.Count += 1;
            }
        }
    }

    public DailyReportResponse BuildDailyReport(int budgetLimitCents)
    {
        lock (_lock)
        {
            var spend = _rows.Values.Sum(r => r.SpendCents);
            var topSpenders = _rows.Values
                .OrderByDescending(r => r.SpendCents)
                .ThenByDescending(r => r.Count)
                .Take(10)
                .Select(r => new CostAttributionRow
                {
                    OrgId = r.OrgId,
                    WorkspaceId = r.WorkspaceId,
                    ActorId = r.ActorId,
                    ModelId = r.ModelId,
                    ToolId = r.ToolId,
                    SpendCents = r.SpendCents,
                    Count = r.Count
                })
                .ToList();

            var denied = _deniedReasons
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var alerts = EvaluateAlerts(spend, budgetLimitCents);

            return new DailyReportResponse
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                TotalSpendCents = spend,
                TopSpenders = topSpenders,
                TopDeniedReasons = denied,
                Alerts = alerts
            };
        }
    }

    private List<AlertSignal> EvaluateAlerts(int spendCents, int budgetLimitCents)
    {
        var alerts = new List<AlertSignal>();
        var now = DateTimeOffset.UtcNow;

        if (budgetLimitCents > 0)
        {
            var percent = (double)spendCents / budgetLimitCents * 100.0;
            if (percent >= 100)
            {
                alerts.Add(new AlertSignal { Code = "budget_100", Message = "budget reached 100%", TriggeredAtUtc = now });
            }
            else if (percent >= 90)
            {
                alerts.Add(new AlertSignal { Code = "budget_90", Message = "budget reached 90%", TriggeredAtUtc = now });
            }
            else if (percent >= 80)
            {
                alerts.Add(new AlertSignal { Code = "budget_80", Message = "budget reached 80%", TriggeredAtUtc = now });
            }
        }

        TrimRecentDenials();
        if (_recentDenials.Count >= SurgeDenialsThreshold)
        {
            alerts.Add(new AlertSignal
            {
                Code = "deny_surge",
                Message = $"denials surged to {_recentDenials.Count} within one minute",
                TriggeredAtUtc = now
            });
        }

        var policyDenials = _deniedReasons
            .Where(kv => kv.Key.Contains("policy", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("not_allowed", StringComparison.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);
        if (policyDenials >= RepeatedPolicyDenialThreshold)
        {
            alerts.Add(new AlertSignal
            {
                Code = "repeated_policy_denials",
                Message = $"policy denials repeated {policyDenials} times",
                TriggeredAtUtc = now
            });
        }

        return alerts;
    }

    private void TrimRecentDenials()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        while (_recentDenials.Count > 0 && _recentDenials.Peek() < cutoff)
        {
            _recentDenials.Dequeue();
        }
    }

    private CostAttributionRow GetOrCreate(string key, string orgId, string workspaceId, string actorId, string modelId, string toolId)
    {
        if (_rows.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new CostAttributionRow
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            ActorId = actorId,
            ModelId = modelId,
            ToolId = toolId
        };
        _rows[key] = created;
        return created;
    }

    private static string Key(string orgId, string workspaceId, string actorId, string modelId, string toolId)
    {
        return $"{orgId}|{workspaceId}|{actorId}|{modelId}|{toolId}";
    }
}
