using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class Phase5ContextGovernanceTests
{
    [Fact]
    public async Task OversizedRetrieval_AutoCompression_GrantsWithSummaryTrace()
    {
        var audit = new RecordingAuditWriter();
        using var governor = BuildGovernor(audit, dailyBudget: 20);

        var acquire = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.LongContext,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 100,
            MaxOutputTokens = 80,
            EstimatedCostCents = 2,
            RequestedContextTokens = 100,
            RequestedRetrievedChunks = 2,
            RequestedRetrievedBytes = 10000,
            RequestedRetrievedTokens = 2000,
            RequestedCapabilities = new List<string> { "chat" },
            ContextContributions = new List<ContextContribution>
            {
                new() { SourceId = "doc:alpha", Chunks = 2, Bytes = 9000, Tokens = 1800 }
            },
            AutoApplyConstraints = true,
            IdempotencyKey = "ctx-auto"
        }, CancellationToken.None);

        Assert.True(acquire.Granted);
        Assert.Equal("retrieval_auto_compression_applied", acquire.Recommendation);
        Assert.Contains(audit.Events, e => e.EventType == "context_summarization_required");

        var release = await governor.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = acquire.LeaseId,
            ActualPromptTokens = 60,
            ActualOutputTokens = 40,
            ActualCostCents = 3,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "ctx-release"
        }, CancellationToken.None);

        Assert.NotNull(release.Receipt);
        Assert.NotEmpty(release.Receipt!.ContextSummaries);
        Assert.Equal("doc:alpha", release.Receipt.ContextSummaries[0].SourceId);
    }

    [Fact]
    public async Task OversizedRetrieval_WithoutAutoApply_DeniesWithReason()
    {
        using var governor = BuildGovernor(new RecordingAuditWriter(), dailyBudget: 20);

        var denied = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            IntentClass = IntentClass.LongContext,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 100,
            MaxOutputTokens = 80,
            EstimatedCostCents = 2,
            RequestedContextTokens = 100,
            RequestedRetrievedChunks = 2,
            RequestedRetrievedBytes = 10000,
            RequestedRetrievedTokens = 2000,
            RequestedCapabilities = new List<string> { "chat" },
            AutoApplyConstraints = false,
            IdempotencyKey = "ctx-deny"
        }, CancellationToken.None);

        Assert.False(denied.Granted);
        Assert.Equal("retrieval_bytes_exceeded", denied.DeniedReason);
    }

    private static LeaseGovernor BuildGovernor(IAuditWriter audit, int dailyBudget)
    {
        var policyPath = Path.Combine(Path.GetTempPath(), $"leasegate-phase5-context-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, """
            {
              "policyVersion": "v5-context",
              "maxInFlight": 4,
              "dailyBudgetCents": 100,
              "maxRequestsPerMinute": 120,
              "maxTokensPerMinute": 10000,
              "maxContextTokens": 2000,
              "maxRetrievedChunks": 8,
              "maxRetrievedBytes": 4000,
              "maxRetrievedTokens": 800,
              "summarizationTargetTokens": 300,
              "maxToolOutputTokens": 300,
              "maxToolCallsPerLease": 4,
              "maxComputeUnits": 4,
              "allowedModels": ["gpt-4o-mini"],
              "allowedCapabilities": {
                "chatCompletion": ["chat"]
              },
              "riskRequiresApproval": []
            }
            """);

        return new LeaseGovernor(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = dailyBudget,
            MaxRequestsPerMinute = 500,
            MaxTokensPerMinute = 50000,
            MaxToolCallsPerLease = 3,
            MaxToolOutputTokens = 200,
            MaxComputeUnits = 4,
            ReceiptThresholdCostCents = 1,
            EnableDurableState = false
        }, new PolicyEngine(policyPath), audit);
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.FromResult(new AuditWriteResult());
        }
    }
}
