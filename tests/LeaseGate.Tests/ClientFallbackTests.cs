using LeaseGate.Client;
using LeaseGate.Protocol;

namespace LeaseGate.Tests;

public class ClientFallbackTests
{
    private static LeaseGateClient MakeClient(FallbackMode mode) => new(new LeaseGateClientOptions
    {
        PipeName = $"nonexistent-{Guid.NewGuid():N}",
        FallbackMode = mode,
        DevMaxOutputTokens = 200,
        ProdReadOnlyMaxOutputTokens = 100
    });

    private static AcquireLeaseRequest BaseRequest(string key, ActionType action = ActionType.ChatCompletion, int maxOutputTokens = 50) => new()
    {
        ActorId = "demo",
        WorkspaceId = "ws",
        ActionType = action,
        ModelId = "gpt-4o-mini",
        MaxOutputTokens = maxOutputTokens,
        EstimatedCostCents = 1,
        RequestedCapabilities = new() { "chat" },
        RequestedTools = new(),
        IdempotencyKey = key
    };

    [Fact]
    public async Task DevFallback_RejectsHighOutputTokens()
    {
        var client = MakeClient(FallbackMode.Dev);
        var response = await client.AcquireAsync(BaseRequest("dev-high", maxOutputTokens: 500), CancellationToken.None);
        Assert.False(response.Granted);
        Assert.Contains("dev", response.DeniedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProdFallback_AllowsReadOnlyChat()
    {
        var client = MakeClient(FallbackMode.Prod);
        var response = await client.AcquireAsync(BaseRequest("prod-chat", maxOutputTokens: 50), CancellationToken.None);
        Assert.True(response.Granted);
        Assert.Contains("local-", response.LeaseId);
    }

    [Fact]
    public async Task ProdFallback_RejectsEmbedding()
    {
        var client = MakeClient(FallbackMode.Prod);
        var response = await client.AcquireAsync(BaseRequest("prod-embed", action: ActionType.Embedding), CancellationToken.None);
        Assert.False(response.Granted);
        Assert.Contains("readonly", response.DeniedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProdFallback_RejectsToolCall()
    {
        var client = MakeClient(FallbackMode.Prod);
        var request = BaseRequest("prod-tool", action: ActionType.ToolCall);
        request.RequestedTools = new() { new ToolIntent { ToolId = "net.post", Category = ToolCategory.NetworkWrite } };
        var response = await client.AcquireAsync(request, CancellationToken.None);
        Assert.False(response.Granted);
    }

    [Fact]
    public async Task LocalRelease_KnownLocalLease_ReturnsRecorded()
    {
        var client = MakeClient(FallbackMode.Dev);
        var acq = await client.AcquireAsync(BaseRequest("local-rel-1"), CancellationToken.None);
        Assert.True(acq.Granted);

        var rel = await client.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = acq.LeaseId,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "local-rel-rel-1"
        }, CancellationToken.None);

        Assert.Equal(ReleaseClassification.Recorded, rel.Classification);
    }

    [Fact]
    public async Task LocalRelease_UnknownLease_AttemptsServer()
    {
        var client = MakeClient(FallbackMode.Dev);
        var rel = await client.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = "unknown-lease-id",
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "unknown-rel-1"
        }, CancellationToken.None);

        // Server is unavailable, so it should return LeaseNotFound
        Assert.Equal(ReleaseClassification.LeaseNotFound, rel.Classification);
    }
}
