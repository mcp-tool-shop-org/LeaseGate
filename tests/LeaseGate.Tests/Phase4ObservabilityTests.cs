using LeaseGate.Hub;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class Phase4ObservabilityTests
{
    [Fact]
    public async Task DailyReport_ShowsTopCosts_AndThrottleCauses()
    {
        var policyPath = WritePolicy();
        using var hub = new HubControlPlane(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = 100,
            MaxRequestsPerMinute = 200,
            MaxTokensPerMinute = 50000,
            EnableDurableState = false
        }, policyPath);

        var acquire = await hub.AcquireAsync(BaseAcquire("cost-1", "gpt-4o-mini"), CancellationToken.None);
        Assert.True(acquire.Granted);

        await hub.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = acquire.LeaseId,
            ActualPromptTokens = 20,
            ActualOutputTokens = 20,
            ActualCostCents = 9,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "release-cost-1"
        }, CancellationToken.None);

        for (var i = 0; i < 8; i++)
        {
            var denied = await hub.AcquireAsync(BaseAcquire($"deny-{i}", "blocked-model"), CancellationToken.None);
            Assert.False(denied.Granted);
        }

        var report = hub.GetDailyReport();
        Assert.True(report.TotalSpendCents >= 9);
        Assert.True(report.TopSpenders.Count > 0);
        Assert.True(report.TopDeniedReasons.Count > 0);
        Assert.Contains(report.Alerts, a => a.Code == "budget_80" || a.Code == "budget_90" || a.Code == "budget_100");
        Assert.Contains(report.Alerts, a => a.Code == "deny_surge");
        Assert.Contains(report.Alerts, a => a.Code == "repeated_policy_denials");

        var output = hub.PrintDailyReport();
        Assert.Contains("top costs", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("top throttle causes", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportSummary_JsonAndCsv_WritesFiles()
    {
        var policyPath = WritePolicy();
        using var hub = new HubControlPlane(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = 100,
            MaxRequestsPerMinute = 200,
            MaxTokensPerMinute = 50000,
            EnableDurableState = false
        }, policyPath);

        var acquire = await hub.AcquireAsync(BaseAcquire("export-1", "gpt-4o-mini"), CancellationToken.None);
        Assert.True(acquire.Granted);
        await hub.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = acquire.LeaseId,
            ActualPromptTokens = 10,
            ActualOutputTokens = 10,
            ActualCostCents = 4,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "export-release"
        }, CancellationToken.None);

        var jsonPath = Path.Combine(Path.GetTempPath(), $"leasegate-report-{Guid.NewGuid():N}.json");
        var csvPath = Path.Combine(Path.GetTempPath(), $"leasegate-report-{Guid.NewGuid():N}.csv");

        var json = hub.ExportDailySummary(new ExportSummaryRequest
        {
            OutputPath = jsonPath,
            Format = "json",
            IdempotencyKey = "export-json"
        });
        var csv = hub.ExportDailySummary(new ExportSummaryRequest
        {
            OutputPath = csvPath,
            Format = "csv",
            IdempotencyKey = "export-csv"
        });

        Assert.True(json.Exported);
        Assert.True(csv.Exported);
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(csvPath));
    }

    private static AcquireLeaseRequest BaseAcquire(string idempotencyKey, string model)
    {
        return new AcquireLeaseRequest
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientInstanceId = "obs-node",
            OrgId = "org-acme",
            ActorId = "observer",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            ModelId = model,
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = idempotencyKey
        };
    }

    private static string WritePolicy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase4-observability-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "policyVersion": "v4-commit5",
              "allowedModels": ["gpt-4o-mini"],
              "allowedCapabilities": { "chatCompletion": ["chat"] },
              "orgDailyBudgetCents": 10,
              "maxInFlightPerActor": 2
            }
            """);
        return path;
    }
}
