using LeaseGate.Hub;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class Phase4DistributedQuotaTests
{
    [Fact]
    public async Task NoisyActor_CannotStarveOtherActors()
    {
        var policyPath = WritePolicy("""
            {
              "policyVersion": "v4-commit3",
              "allowedModels": ["gpt-4o-mini"],
              "allowedCapabilities": { "chatCompletion": ["chat"] },
              "orgDailyBudgetCents": 100,
              "workspaceDailyBudgetCents": { "ws-alpha": 100 },
              "actorDailyBudgetCents": { "noisy": 50, "quiet": 50 },
              "maxInFlightPerActor": 1,
              "roleMaxInFlightOverrides": { "admin": 2 }
            }
            """);

        using var hub = new HubControlPlane(new LeaseGovernorOptions
        {
            MaxInFlight = 3,
            DailyBudgetCents = 100,
            MaxRequestsPerMinute = 500,
            MaxTokensPerMinute = 50000,
            MaxContextTokens = 2000,
            MaxToolCallsPerLease = 4,
            MaxToolOutputTokens = 300,
            MaxComputeUnits = 4,
            EnableDurableState = false
        }, policyPath);

        var noisyFirst = await hub.AcquireAsync(BaseAcquire("a-1", "noisy", 1), CancellationToken.None);
        var noisySecond = await hub.AcquireAsync(BaseAcquire("a-2", "noisy", 1), CancellationToken.None);
        var quiet = await hub.AcquireAsync(BaseAcquire("b-1", "quiet", 1), CancellationToken.None);

        Assert.True(noisyFirst.Granted);
        Assert.False(noisySecond.Granted);
        Assert.Equal("actor_throttled", noisySecond.DeniedReason);
        Assert.True(quiet.Granted);
    }

    [Fact]
    public async Task DenyReasons_ArePrecise_AndIncludeRefillHint()
    {
        var policyPath = WritePolicy("""
            {
              "policyVersion": "v4-commit3-deny",
              "allowedModels": ["gpt-4o-mini"],
              "allowedCapabilities": { "chatCompletion": ["chat"] },
              "orgDailyBudgetCents": 2,
              "workspaceDailyBudgetCents": { "ws-alpha": 1 },
              "actorDailyBudgetCents": { "alice": 1 }
            }
            """);

        using var hub = new HubControlPlane(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = 100,
            MaxRequestsPerMinute = 500,
            MaxTokensPerMinute = 50000,
            EnableDurableState = false
        }, policyPath);

        var first = await hub.AcquireAsync(BaseAcquire("org-1", "alice", 1), CancellationToken.None);
        var second = await hub.AcquireAsync(BaseAcquire("org-2", "alice", 1), CancellationToken.None);

        Assert.True(first.Granted);
        Assert.False(second.Granted);
        Assert.Equal("workspace_exhausted", second.DeniedReason);
        Assert.True(second.RetryAfterMs > 0);
        Assert.Contains("next refill", second.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    private static AcquireLeaseRequest BaseAcquire(string key, string actorId, int estimatedCost)
    {
        return new AcquireLeaseRequest
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientInstanceId = "node-a",
            OrgId = "org-acme",
            ActorId = actorId,
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 20,
            MaxOutputTokens = 10,
            EstimatedCostCents = estimatedCost,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = key
        };
    }

    private static string WritePolicy(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase4-quotas-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
