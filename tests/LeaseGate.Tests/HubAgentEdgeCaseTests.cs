using LeaseGate.Agent;
using LeaseGate.Audit;
using LeaseGate.Hub;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class HubAgentEdgeCaseTests
{
    private static string WritePolicyFile()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "hub-agent-test",
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            OrgDailyBudgetCents = 200,
            AllowedModels = new() { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });
        var path = Path.Combine(Path.GetTempPath(), $"hub-agent-edge-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        return path;
    }

    private static AcquireLeaseRequest BaseAcquire(string key) => new()
    {
        ActorId = "demo",
        OrgId = "org-test",
        WorkspaceId = "ws-test",
        ActionType = ActionType.ChatCompletion,
        ModelId = "gpt-4o-mini",
        ProviderId = "fake",
        EstimatedPromptTokens = 30,
        MaxOutputTokens = 20,
        EstimatedCostCents = 1,
        EstimatedComputeUnits = 1,
        RequestedCapabilities = new() { "chat" },
        RequestedTools = new(),
        IdempotencyKey = key
    };

    [Fact]
    public void Hub_DailyReport_EmptyState_ReturnsEmptyTotals()
    {
        var policyPath = WritePolicyFile();
        try
        {
            using var hub = new HubControlPlane(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policyPath);

            var report = hub.GetDailyReport();
            Assert.Equal(0, report.TotalSpendCents);
            Assert.Empty(report.TopSpenders);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void Hub_PrintDailyReport_FormatsReadableText()
    {
        var policyPath = WritePolicyFile();
        try
        {
            using var hub = new HubControlPlane(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policyPath);

            var text = hub.PrintDailyReport();
            Assert.NotEmpty(text);
            Assert.Contains("daily report", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("total spend", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task Agent_RecoverFromDegradedMode_WhenHubReturns()
    {
        var policyPath = WritePolicyFile();
        try
        {
            // Start with a hub that will be swapped
            var realHub = new HubControlPlane(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policyPath);

            // Use failing hub first to trigger degraded mode
            var failingClient = new FailingHubClient();
            var audit = new NoopAuditWriter();
            using var agent = new LeaseGateAgent(failingClient, new LeaseGovernorOptions
            {
                MaxInFlight = 2, DailyBudgetCents = 50,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 50, MaxTokensPerMinute = 5000,
                MaxContextTokens = 200, MaxRetrievedChunks = 4,
                MaxToolOutputTokens = 100, MaxToolCallsPerLease = 2, MaxComputeUnits = 1,
                EnableDurableState = false
            }, policyPath, audit);

            var r1 = await agent.AcquireAsync(BaseAcquire("degrade-1"), CancellationToken.None);
            Assert.True(agent.IsDegradedMode);
            Assert.Equal(LeaseLocality.LocalIssued, r1.LeaseLocality);
            Assert.True(r1.DegradedMode);

            realHub.Dispose();
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task Agent_DegradedMode_SetsLocality()
    {
        var policyPath = WritePolicyFile();
        try
        {
            var audit = new NoopAuditWriter();
            using var agent = new LeaseGateAgent(null, new LeaseGovernorOptions
            {
                MaxInFlight = 2, DailyBudgetCents = 50,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 50, MaxTokensPerMinute = 5000,
                MaxContextTokens = 200, MaxRetrievedChunks = 4,
                MaxToolOutputTokens = 100, MaxToolCallsPerLease = 2, MaxComputeUnits = 1,
                EnableDurableState = false
            }, policyPath, audit);

            var r1 = await agent.AcquireAsync(BaseAcquire("locality-1"), CancellationToken.None);
            Assert.Equal(LeaseLocality.LocalIssued, r1.LeaseLocality);
            Assert.True(r1.DegradedMode);
            Assert.True(agent.IsDegradedMode);
        }
        finally { File.Delete(policyPath); }
    }

    private sealed class FailingHubClient : ILeaseGateHubClient
    {
        public Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Hub unavailable");

        public Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Hub unavailable");
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.FromResult(new AuditWriteResult());
    }
}
