using LeaseGate.Agent;
using LeaseGate.Audit;
using LeaseGate.Hub;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase4HubAgentTests
{
    [Fact]
    public async Task TwoAgents_SharedHubPool_UsesGlobalConcurrency()
    {
        var policyPath = WritePolicy();
        using var hub = new HubControlPlane(new LeaseGovernorOptions
        {
            MaxInFlight = 1,
            DailyBudgetCents = 50,
            MaxRequestsPerMinute = 200,
            MaxTokensPerMinute = 50000,
            MaxContextTokens = 1000,
            MaxToolCallsPerLease = 3,
            MaxToolOutputTokens = 200,
            MaxComputeUnits = 2,
            EnableDurableState = false
        }, policyPath);

        var hubClient = new InMemoryHubClient(hub);
        var audit = new RecordingAuditWriter();

        using var agent1 = BuildAgent(hubClient, policyPath, audit);
        using var agent2 = BuildAgent(hubClient, policyPath, audit);

        var req1 = BaseAcquire("m1");
        req1.ClientInstanceId = "machine-1";
        req1.SessionId = "session-a";

        var req2 = BaseAcquire("m2");
        req2.ClientInstanceId = "machine-2";
        req2.SessionId = "session-b";

        var first = await agent1.AcquireAsync(req1, CancellationToken.None);
        var second = await agent2.AcquireAsync(req2, CancellationToken.None);

        Assert.True(first.Granted);
        Assert.Equal(LeaseLocality.HubIssued, first.LeaseLocality);
        Assert.False(second.Granted);
        Assert.Equal("concurrency_limit_reached", second.DeniedReason);
        Assert.Equal(LeaseLocality.HubIssued, second.LeaseLocality);
    }

    [Fact]
    public async Task HubUnavailable_AgentEntersDegradedMode_AndUsesLocalCaps()
    {
        var policyPath = WritePolicy();
        var audit = new RecordingAuditWriter();
        using var agent = BuildAgent(new FailingHubClient(), policyPath, audit);

        var request = BaseAcquire("degraded");
        request.MaxOutputTokens = 5000;

        var response = await agent.AcquireAsync(request, CancellationToken.None);

        Assert.False(response.Granted);
        Assert.True(response.DegradedMode);
        Assert.Equal(LeaseLocality.LocalIssued, response.LeaseLocality);
        Assert.True(agent.IsDegradedMode);
        Assert.Contains(audit.Events, e => e.EventType == "agent_degraded_mode");
    }

    private static LeaseGateAgent BuildAgent(ILeaseGateHubClient hubClient, string policyPath, IAuditWriter audit)
    {
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead }
        });

        return new LeaseGateAgent(
            hubClient,
            new LeaseGovernorOptions
            {
                MaxInFlight = 1,
                DailyBudgetCents = 5,
                MaxRequestsPerMinute = 20,
                MaxTokensPerMinute = 200,
                MaxContextTokens = 400,
                MaxRetrievedChunks = 4,
                MaxToolOutputTokens = 100,
                MaxToolCallsPerLease = 2,
                MaxComputeUnits = 1,
                EnableDurableState = false
            },
            policyPath,
            audit,
            tools);
    }

    private static AcquireLeaseRequest BaseAcquire(string idempotencyKey)
    {
        return new AcquireLeaseRequest
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientInstanceId = "local",
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 40,
            MaxOutputTokens = 40,
            EstimatedCostCents = 1,
            RequestedContextTokens = 60,
            RequestedRetrievedChunks = 1,
            EstimatedToolOutputTokens = 10,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = idempotencyKey
        };
    }

    private static string WritePolicy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase4-hubagent-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "policyVersion": "v4-commit2",
              "maxInFlight": 2,
              "dailyBudgetCents": 100,
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
              "riskRequiresApproval": []
            }
            """);
        return path;
    }

    private sealed class InMemoryHubClient : ILeaseGateHubClient
    {
        private readonly HubControlPlane _hub;

        public InMemoryHubClient(HubControlPlane hub)
        {
            _hub = hub;
        }

        public Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
        {
            return _hub.AcquireAsync(request, cancellationToken);
        }

        public Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
        {
            return _hub.ReleaseAsync(request, cancellationToken);
        }
    }

    private sealed class FailingHubClient : ILeaseGateHubClient
    {
        public Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("hub unavailable");
        }

        public Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("hub unavailable");
        }
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
