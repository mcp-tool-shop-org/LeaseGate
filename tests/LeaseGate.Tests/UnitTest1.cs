using LeaseGate.Audit;
using LeaseGate.Client;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class UnitTest1
{
    [Fact]
    public void ProtocolJson_AcquireRequest_SerializesStableShape()
    {
        var request = new AcquireLeaseRequest
        {
            ActorId = "actor-1",
            WorkspaceId = "ws-1",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "stub",
            EstimatedPromptTokens = 120,
            MaxOutputTokens = 80,
            EstimatedCostCents = 7,
            RequestedCapabilities = new List<string> { "chat" },
            RiskFlags = new List<string> { "none" },
            IdempotencyKey = "idem-1"
        };

        var json = ProtocolJson.Serialize(request);
        Assert.Equal("{\"actorId\":\"actor-1\",\"workspaceId\":\"ws-1\",\"actionType\":\"chatCompletion\",\"modelId\":\"gpt-4o-mini\",\"providerId\":\"stub\",\"estimatedPromptTokens\":120,\"maxOutputTokens\":80,\"estimatedCostCents\":7,\"requestedCapabilities\":[\"chat\"],\"riskFlags\":[\"none\"],\"idempotencyKey\":\"idem-1\"}", json);

        var roundTrip = ProtocolJson.Deserialize<AcquireLeaseRequest>(json);
        Assert.Equal(request.IdempotencyKey, roundTrip.IdempotencyKey);
        Assert.Equal(ActionType.ChatCompletion, roundTrip.ActionType);
    }

    [Fact]
    public async Task Governor_Denies_OnConcurrencyLimit()
    {
        var governor = BuildGovernor(maxInFlight: 1, budget: 1000, ttlMs: 5000);

        var first = await governor.AcquireAsync(BaseAcquire("k1", 10), CancellationToken.None);
        var second = await governor.AcquireAsync(BaseAcquire("k2", 10), CancellationToken.None);

        Assert.True(first.Granted);
        Assert.False(second.Granted);
        Assert.Equal("concurrency_limit_reached", second.DeniedReason);
    }

    [Fact]
    public async Task Governor_Denies_OnBudgetLimit()
    {
        var governor = BuildGovernor(maxInFlight: 5, budget: 10, ttlMs: 5000);
        var response = await governor.AcquireAsync(BaseAcquire("budget", 11), CancellationToken.None);

        Assert.False(response.Granted);
        Assert.Equal("daily_budget_exceeded", response.DeniedReason);
        Assert.Contains("switch model", response.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Governor_ExpiresLease_AndReturnsCapacity()
    {
        var governor = BuildGovernor(maxInFlight: 1, budget: 1000, ttlMs: 300);

        var first = await governor.AcquireAsync(BaseAcquire("ttl1", 5), CancellationToken.None);
        Assert.True(first.Granted);

        await Task.Delay(1400);
        var second = await governor.AcquireAsync(BaseAcquire("ttl2", 5), CancellationToken.None);
        Assert.True(second.Granted);
    }

    [Fact]
    public async Task Client_DevFallback_AllowsBoundedRequest_ButBlocksRisky()
    {
        var client = new LeaseGateClient(new LeaseGateClientOptions
        {
            PipeName = "missing-pipe-dev",
            FallbackMode = FallbackMode.Dev,
            DevMaxOutputTokens = 200
        });

        var allowed = await client.AcquireAsync(BaseAcquire("dev-ok", 2), CancellationToken.None);
        var risky = await client.AcquireAsync(new AcquireLeaseRequest
        {
            ActorId = "a",
            WorkspaceId = "w",
            ActionType = ActionType.ToolCall,
            ModelId = "gpt-4o-mini",
            ProviderId = "stub",
            EstimatedPromptTokens = 1,
            MaxOutputTokens = 100,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "exec" },
            RiskFlags = new List<string>(),
            IdempotencyKey = "dev-risk"
        }, CancellationToken.None);

        Assert.True(allowed.Granted);
        Assert.False(risky.Granted);
    }

    [Fact]
    public async Task Client_ProdFallback_ReadOnlyChatOnly()
    {
        var client = new LeaseGateClient(new LeaseGateClientOptions
        {
            PipeName = "missing-pipe-prod",
            FallbackMode = FallbackMode.Prod,
            ProdReadOnlyMaxOutputTokens = 100
        });

        var allowedChat = await client.AcquireAsync(BaseAcquire("prod-chat", 2), CancellationToken.None);
        var deniedTool = await client.AcquireAsync(new AcquireLeaseRequest
        {
            ActorId = "a",
            WorkspaceId = "w",
            ActionType = ActionType.ToolCall,
            ModelId = "gpt-4o-mini",
            ProviderId = "stub",
            EstimatedPromptTokens = 1,
            MaxOutputTokens = 50,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RiskFlags = new List<string>(),
            IdempotencyKey = "prod-tool"
        }, CancellationToken.None);

        Assert.True(allowedChat.Granted);
        Assert.False(deniedTool.Granted);
    }

    private static AcquireLeaseRequest BaseAcquire(string key, int estimatedCostCents)
    {
        return new AcquireLeaseRequest
        {
            ActorId = "actor",
            WorkspaceId = "workspace",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "stub",
            EstimatedPromptTokens = 50,
            MaxOutputTokens = 50,
            EstimatedCostCents = estimatedCostCents,
            RequestedCapabilities = new List<string> { "chat" },
            RiskFlags = new List<string>(),
            IdempotencyKey = key
        };
    }

    private static LeaseGovernor BuildGovernor(int maxInFlight, int budget, int ttlMs)
    {
        var policyFile = Path.Combine(Path.GetTempPath(), $"leasegate-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyFile, "{\"maxInFlight\":10,\"dailyBudgetCents\":100000,\"allowedModels\":[\"gpt-4o-mini\"],\"allowedCapabilities\":{},\"riskRequiresApproval\":[]}");

        var policy = new PolicyEngine(policyFile, hotReload: false);
        var audit = new NoopAuditWriter();
        return new LeaseGovernor(
            new LeaseGovernorOptions
            {
                MaxInFlight = maxInFlight,
                DailyBudgetCents = budget,
                LeaseTtl = TimeSpan.FromMilliseconds(ttlMs)
            },
            policy,
            audit);
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}