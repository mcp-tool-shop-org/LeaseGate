using LeaseGate.Hub;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Safety;
using System.Reflection;

namespace LeaseGate.Tests;

public class ResourceBoundsTests
{
    [Fact]
    public void SafetyState_MapEvictsAt10001Entries()
    {
        var state = new SafetyAutomationState();
        for (var i = 0; i < 10_001; i++)
        {
            state.RegisterPolicyDenyAndCheckThreshold($"ws-{i}", 999_999);
        }

        // Use reflection to check dictionary count
        var field = typeof(SafetyAutomationState).GetField("_policyDenyByWorkspace", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var dict = (Dictionary<string, int>)field!.GetValue(state)!;
        Assert.True(dict.Count <= 10_000, $"Expected ≤ 10,000 entries but got {dict.Count}");
    }

    [Fact]
    public void SafetyState_InterventionsEvictAt1001()
    {
        var state = new SafetyAutomationState();
        for (var i = 0; i < 1_001; i++)
        {
            state.ApplyWorkspaceCircuitBreaker($"ws-{i}", TimeSpan.FromSeconds(1), "test", $"detail-{i}");
        }

        var interventions = state.SnapshotInterventions();
        Assert.True(interventions.Count <= 1_000, $"Expected ≤ 1,000 interventions but got {interventions.Count}");
    }

    [Fact]
    public void SafetyState_EvictionPreservesNewestEntries()
    {
        var state = new SafetyAutomationState();
        for (var i = 0; i < 1_001; i++)
        {
            state.ApplyActorCooldown($"actor-{i}", TimeSpan.FromMinutes(5), "test", $"detail-{i}");
        }

        var interventions = state.SnapshotInterventions();
        // Newest entry should be present
        Assert.Contains(interventions, i => i.Detail == "detail-1000");
    }

    [Fact]
    public async Task HubControlPlane_LeaseMapEvictsAt10001()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "bounds-test",
            MaxInFlight = 20_000,
            DailyBudgetCents = 100_000,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            OrgDailyBudgetCents = 100_000,
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });
        var policyPath = Path.Combine(Path.GetTempPath(), $"bounds-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, policyJson);

        try
        {
            using var hub = new HubControlPlane(new LeaseGovernorOptions
            {
                MaxInFlight = 20_000, DailyBudgetCents = 100_000,
                LeaseTtl = TimeSpan.FromMinutes(30),
                MaxRequestsPerMinute = 100_000, MaxTokensPerMinute = 1_000_000,
                MaxContextTokens = 4000, MaxRetrievedChunks = 80,
                MaxToolOutputTokens = 2000, MaxToolCallsPerLease = 30, MaxComputeUnits = 20_000,
                EnableDurableState = false
            }, policyPath);

            // Reflection to check _leaseRequests count
            var field = typeof(HubControlPlane).GetField("_leaseRequests", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);

            // Acquire many leases
            for (var i = 0; i < 100; i++)
            {
                await hub.AcquireAsync(new AcquireLeaseRequest
                {
                    ActorId = $"actor-{i}",
                    OrgId = "org",
                    WorkspaceId = "ws",
                    ActionType = ActionType.ChatCompletion,
                    ModelId = "gpt-4o-mini",
                    ProviderId = "fake",
                    EstimatedPromptTokens = 10,
                    MaxOutputTokens = 10,
                    EstimatedCostCents = 1,
                    EstimatedComputeUnits = 1,
                    RequestedCapabilities = new() { "chat" },
                    IdempotencyKey = $"bounds-{i}"
                }, CancellationToken.None);
            }

            var dict = (Dictionary<string, AcquireLeaseRequest>)field!.GetValue(hub)!;
            Assert.True(dict.Count <= 10_000);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task HubControlPlane_EvictionDoesNotBreakRelease()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "evict-release-test",
            MaxInFlight = 100,
            DailyBudgetCents = 10_000,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            OrgDailyBudgetCents = 10_000,
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });
        var policyPath = Path.Combine(Path.GetTempPath(), $"evict-rel-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, policyJson);

        try
        {
            using var hub = new HubControlPlane(new LeaseGovernorOptions
            {
                MaxInFlight = 100, DailyBudgetCents = 10_000,
                LeaseTtl = TimeSpan.FromMinutes(5),
                MaxRequestsPerMinute = 10_000, MaxTokensPerMinute = 100_000,
                MaxContextTokens = 4000, MaxRetrievedChunks = 80,
                MaxToolOutputTokens = 2000, MaxToolCallsPerLease = 30, MaxComputeUnits = 100,
                EnableDurableState = false
            }, policyPath);

            var acq = await hub.AcquireAsync(new AcquireLeaseRequest
            {
                ActorId = "release-actor",
                OrgId = "org",
                WorkspaceId = "ws",
                ActionType = ActionType.ChatCompletion,
                ModelId = "gpt-4o-mini",
                ProviderId = "fake",
                EstimatedPromptTokens = 10,
                MaxOutputTokens = 10,
                EstimatedCostCents = 1,
                EstimatedComputeUnits = 1,
                RequestedCapabilities = new() { "chat" },
                IdempotencyKey = "evict-rel-1"
            }, CancellationToken.None);

            Assert.True(acq.Granted);

            // Release with a potentially evicted ID should not throw
            var rel = await hub.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acq.LeaseId,
                ActualCostCents = 1,
                Outcome = LeaseOutcome.Success,
                IdempotencyKey = "evict-rel-rel-1"
            }, CancellationToken.None);

            // Should complete without crashing
            Assert.NotNull(rel);
        }
        finally { File.Delete(policyPath); }
    }
}
