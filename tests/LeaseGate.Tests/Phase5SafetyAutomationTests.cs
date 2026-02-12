using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase5SafetyAutomationTests
{
    [Fact]
    public async Task RepeatedPolicyDenials_TripWorkspaceCircuitBreaker()
    {
        using var governor = BuildGovernor(new RecordingAuditWriter());

        for (var i = 0; i < 3; i++)
        {
            var denied = await governor.AcquireAsync(new AcquireLeaseRequest
            {
                OrgId = "org-acme",
                ActorId = "alice",
                WorkspaceId = "ws-alpha",
                PrincipalType = PrincipalType.Human,
                Role = Role.Member,
                ActionType = ActionType.ChatCompletion,
                IntentClass = IntentClass.Draft,
                ModelId = "blocked-model",
                ProviderId = "fake",
                EstimatedPromptTokens = 10,
                MaxOutputTokens = 10,
                EstimatedCostCents = 1,
                RequestedCapabilities = new List<string> { "chat" },
                IdempotencyKey = $"deny-{i}"
            }, CancellationToken.None);

            Assert.False(denied.Granted);
        }

        var breaker = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.Draft,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            IdempotencyKey = "breaker"
        }, CancellationToken.None);

        Assert.False(breaker.Granted);
        Assert.Equal("workspace_circuit_breaker", breaker.DeniedReason);
    }

    [Fact]
    public async Task SpendSpike_TriggersClampAndCooldown_AndExportsRunawayReport()
    {
        using var governor = BuildGovernor(new RecordingAuditWriter());

        var granted = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "spendy",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.Draft,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 100,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            IdempotencyKey = "spike-1"
        }, CancellationToken.None);

        Assert.True(granted.Granted);

        var released = await governor.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = granted.LeaseId,
            ActualPromptTokens = 50,
            ActualOutputTokens = 50,
            ActualCostCents = 25,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "spike-release"
        }, CancellationToken.None);
        Assert.Equal(ReleaseClassification.Recorded, released.Classification);

        var cooled = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "spendy",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.Draft,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 100,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            IdempotencyKey = "spike-2"
        }, CancellationToken.None);

        Assert.False(cooled.Granted);
        Assert.Equal("actor_cooldown_active", cooled.DeniedReason);

        var reportPath = Path.Combine(Path.GetTempPath(), $"leasegate-runaway-{Guid.NewGuid():N}.json");
        var exported = governor.ExportRunawayReport(new ExportRunawayReportRequest
        {
            OutputPath = reportPath,
            IdempotencyKey = "report"
        });

        Assert.True(exported.Exported);
        Assert.True(File.Exists(exported.OutputPath));
        var reportJson = File.ReadAllText(exported.OutputPath);
        Assert.Contains("spend_spike", reportJson, StringComparison.OrdinalIgnoreCase);
    }

    private static LeaseGovernor BuildGovernor(IAuditWriter audit)
    {
        var policyPath = Path.Combine(Path.GetTempPath(), $"leasegate-phase5-safety-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, """
            {
              "policyVersion": "v5-safety",
              "maxInFlight": 4,
              "dailyBudgetCents": 200,
              "maxRequestsPerMinute": 120,
              "maxTokensPerMinute": 10000,
              "maxContextTokens": 2000,
              "maxRetrievedChunks": 8,
              "maxToolOutputTokens": 300,
              "maxToolCallsPerLease": 4,
              "maxComputeUnits": 4,
              "allowedModels": ["gpt-4o-mini"],
              "allowedCapabilities": {
                "chatCompletion": ["chat"]
              },
              "retryThresholdPerLease": 3,
              "toolLoopThreshold": 2,
              "policyDenyCircuitBreakerThreshold": 3,
              "spendSpikeCents": 20,
              "safetyCooldownMs": 60000,
              "clampedMaxOutputTokens": 32,
              "riskRequiresApproval": []
            }
            """);

        return new LeaseGovernor(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            MaxRequestsPerMinute = 500,
            MaxTokensPerMinute = 50000,
            MaxToolCallsPerLease = 3,
            MaxToolOutputTokens = 200,
            MaxComputeUnits = 4,
            EnableDurableState = false
        }, new PolicyEngine(policyPath), audit, new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead }
        }));
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuditWriteResult());
        }
    }
}
