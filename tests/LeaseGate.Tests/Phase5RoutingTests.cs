using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase5RoutingTests
{
    [Fact]
    public async Task BudgetPressure_AutoApply_DowngradesPredictably()
    {
        using var governor = BuildGovernor(5);

        var first = await governor.AcquireAsync(BaseAcquire("seed", IntentClass.Draft, estimatedCost: 3), CancellationToken.None);
        Assert.True(first.Granted);

        var pressured = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.Draft,
            ModelId = "gpt-4.1-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 100,
            MaxOutputTokens = 100,
            EstimatedCostCents = 3,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            AutoApplyConstraints = true,
            IdempotencyKey = "pressured"
        }, CancellationToken.None);

        Assert.True(pressured.Granted);
        Assert.Equal("budget_auto_downgrade_applied", pressured.Recommendation);
        Assert.True(pressured.Constraints.MaxOutputTokensOverride < 100);
        Assert.Equal("gpt-4o-mini", pressured.Constraints.ForcedModelId);
    }

    [Fact]
    public async Task DeniedRequest_ReturnsDeterministicFallbackPlan()
    {
        using var governor = BuildGovernor(2);

        var denied = await governor.AcquireAsync(BaseAcquire("deny-1", IntentClass.Draft, estimatedCost: 4), CancellationToken.None);

        Assert.False(denied.Granted);
        Assert.Equal("daily_budget_exceeded", denied.DeniedReason);
        Assert.Equal(4, denied.FallbackPlan.Count);
        Assert.Equal("reduce_output_tokens", denied.FallbackPlan[0].Action);
        Assert.Equal("compress_context", denied.FallbackPlan[1].Action);
        Assert.Equal("switch_model", denied.FallbackPlan[2].Action);
        Assert.Equal("delay_backoff", denied.FallbackPlan[3].Action);
    }

    [Fact]
    public async Task IntentRouting_EnforcesTierAndCost()
    {
        using var governor = BuildGovernor(100);

        var badModel = await governor.AcquireAsync(BaseAcquire("intent-model", IntentClass.Summarize, estimatedCost: 1, modelId: "gpt-4.1-mini"), CancellationToken.None);
        var badCost = await governor.AcquireAsync(BaseAcquire("intent-cost", IntentClass.Summarize, estimatedCost: 5, modelId: "gpt-4o-mini"), CancellationToken.None);

        Assert.False(badModel.Granted);
        Assert.Equal("intent_model_not_allowed", badModel.DeniedReason);
        Assert.False(badCost.Granted);
        Assert.Equal("intent_cost_exceeded", badCost.DeniedReason);
    }

    private static AcquireLeaseRequest BaseAcquire(string key, IntentClass intent, int estimatedCost, string modelId = "gpt-4o-mini")
    {
        return new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = intent,
            ModelId = modelId,
            ProviderId = "fake",
            EstimatedPromptTokens = 60,
            MaxOutputTokens = 60,
            EstimatedCostCents = estimatedCost,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = key
        };
    }

    private static LeaseGovernor BuildGovernor(int budget)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase5-routing-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "policyVersion": "v5-routing",
              "maxInFlight": 4,
              "dailyBudgetCents": 100,
              "maxRequestsPerMinute": 120,
              "maxTokensPerMinute": 10000,
              "maxContextTokens": 2000,
              "maxRetrievedChunks": 8,
              "maxToolOutputTokens": 300,
              "maxToolCallsPerLease": 4,
              "maxComputeUnits": 4,
              "allowedModels": ["gpt-4o-mini", "gpt-4.1-mini"],
              "intentModelTiers": {
                "draft": ["gpt-4o-mini", "gpt-4.1-mini"],
                "summarize": ["gpt-4o-mini"]
              },
              "intentMaxCostCents": {
                "draft": 10,
                "summarize": 2
              },
              "allowedCapabilities": {
                "chatCompletion": ["chat"]
              },
              "riskRequiresApproval": []
            }
            """);

        var policy = new PolicyEngine(path, hotReload: false);
        var tools = new ToolRegistry();

        return new LeaseGovernor(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = budget,
            MaxRequestsPerMinute = 500,
            MaxTokensPerMinute = 50000,
            MaxToolCallsPerLease = 3,
            MaxToolOutputTokens = 200,
            MaxComputeUnits = 4,
            EnableDurableState = false
        }, policy, new NoopAuditWriter(), tools);
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuditWriteResult());
        }
    }
}
