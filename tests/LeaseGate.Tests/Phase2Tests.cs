using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.TokenPools;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase2Tests
{
    [Fact]
    public async Task RatePool_EnforcesRollingWindow()
    {
        var pool = new RatePool(maxRequestsPerWindow: 2, maxTokensPerWindow: 20, window: TimeSpan.FromMilliseconds(150));

        Assert.True(pool.TryAcquire(5, out _));
        Assert.True(pool.TryAcquire(5, out _));
        Assert.False(pool.TryAcquire(5, out var retry));
        Assert.True(retry > 0);

        await Task.Delay(220);
        Assert.True(pool.TryAcquire(5, out _));
    }

    [Fact]
    public void ContextPool_DeniesOnExceededTokens()
    {
        var pool = new ContextPool(maxPromptTokens: 100, maxRetrievedChunks: 5, maxToolOutputTokens: 80);
        var request = new AcquireLeaseRequest
        {
            RequestedContextTokens = 150,
            RequestedRetrievedChunks = 1,
            EstimatedToolOutputTokens = 10
        };

        Assert.False(pool.TryEvaluate(request, out var reason, out _));
        Assert.Equal("context_prompt_tokens_exceeded", reason);
    }

    [Fact]
    public void ComputePool_EnforcesWeightedCapacity()
    {
        var pool = new ComputePool(maxUnits: 3);
        Assert.True(pool.TryAcquire(2, out _));
        Assert.False(pool.TryAcquire(2, out _));
        pool.Release(2);
        Assert.True(pool.TryAcquire(3, out _));
    }

    [Fact]
    public async Task Governor_ReturnsRealConstraintsOnGrant()
    {
        using var governor = BuildGovernor(CreatePolicyJson(), maxContextTokens: 2000, maxToolCalls: 4);

        var response = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            ActorId = "demo",
            WorkspaceId = "sample",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 40,
            MaxOutputTokens = 100,
            EstimatedCostCents = 2,
            RequestedContextTokens = 120,
            RequestedRetrievedChunks = 2,
            EstimatedToolOutputTokens = 10,
            EstimatedComputeUnits = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = "constraints-1"
        }, CancellationToken.None);

        Assert.True(response.Granted);
        Assert.NotNull(response.Constraints);
        Assert.True(response.Constraints.MaxOutputTokensOverride > 0);
        Assert.Equal(4, response.Constraints.MaxToolCalls);
        Assert.True(response.Constraints.MaxContextTokens <= 2000);
    }

    [Fact]
    public async Task ApprovalToken_RequiredScopedAndSingleUse()
    {
        using var governor = BuildGovernor(CreatePolicyJson(), maxContextTokens: 2000, maxToolCalls: 4);

        var request = new AcquireLeaseRequest
        {
            ActorId = "demo",
            WorkspaceId = "sample",
            ActionType = ActionType.ToolCall,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 40,
            MaxOutputTokens = 80,
            EstimatedCostCents = 2,
            RequestedContextTokens = 100,
            RequestedRetrievedChunks = 1,
            EstimatedToolOutputTokens = 20,
            EstimatedComputeUnits = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>
            {
                new() { ToolId = "net.post", Category = ToolCategory.NetworkWrite }
            },
            IdempotencyKey = "approval-block"
        };

        var blocked = await governor.AcquireAsync(request, CancellationToken.None);
        Assert.False(blocked.Granted);
        Assert.Equal("approval_required", blocked.DeniedReason);

        var approval = governor.RequestApproval(new ApprovalRequest
        {
            ActorId = "demo",
            WorkspaceId = "sample",
            Reason = "need network write",
            RequestedBy = "tester",
            ToolCategory = ToolCategory.NetworkWrite,
            TtlSeconds = 120,
            SingleUse = true,
            IdempotencyKey = "approval-req"
        });

        var grant = governor.GrantApproval(new GrantApprovalRequest
        {
            ApprovalId = approval.ApprovalId,
            GrantedBy = "admin",
            IdempotencyKey = "approval-grant"
        });

        Assert.True(grant.Granted);

        request.IdempotencyKey = "approval-allow";
        request.ApprovalToken = grant.ApprovalToken;
        var allowed = await governor.AcquireAsync(request, CancellationToken.None);
        Assert.True(allowed.Granted);

        request.IdempotencyKey = "approval-reuse";
        var reuseDenied = await governor.AcquireAsync(request, CancellationToken.None);
        Assert.False(reuseDenied.Granted);
        Assert.Equal("approval_required", reuseDenied.DeniedReason);
    }

    [Fact]
    public async Task MetricsSnapshot_TracksDeniesByReason()
    {
        using var governor = BuildGovernor(CreatePolicyJson(), maxContextTokens: 100, maxToolCalls: 4);

        var denied = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            ActorId = "demo",
            WorkspaceId = "sample",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 1,
            RequestedContextTokens = 1000,
            RequestedRetrievedChunks = 1,
            EstimatedToolOutputTokens = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = "metrics-deny"
        }, CancellationToken.None);

        Assert.False(denied.Granted);

        var metrics = governor.GetMetricsSnapshot();
        Assert.True(metrics.DeniesByReason.Count > 0);
        Assert.True(metrics.DeniesByReason.ContainsKey("context_prompt_tokens_exceeded"));
    }

    private static LeaseGovernor BuildGovernor(string policyJson, int maxContextTokens, int maxToolCalls)
    {
        var policyFile = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase2-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyFile, policyJson);

        var policy = new PolicyEngine(policyFile, hotReload: false);
        var audit = new NoopAuditWriter();
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.post", Category = ToolCategory.NetworkWrite },
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead },
            new ToolDefinition { ToolId = "shell.exec", Category = ToolCategory.Exec }
        });

        return new LeaseGovernor(
            new LeaseGovernorOptions
            {
                MaxInFlight = 5,
                DailyBudgetCents = 10000,
                LeaseTtl = TimeSpan.FromSeconds(10),
                MaxRequestsPerMinute = 100,
                MaxTokensPerMinute = 5000,
                MaxContextTokens = maxContextTokens,
                MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 400,
                MaxToolCallsPerLease = maxToolCalls,
                MaxComputeUnits = 5
            },
            policy,
            audit,
            tools);
    }

    private static string CreatePolicyJson()
    {
        return """
               {
                 "maxInFlight": 4,
                 "dailyBudgetCents": 120,
                 "maxRequestsPerMinute": 90,
                 "maxTokensPerMinute": 10000,
                 "maxContextTokens": 2000,
                 "maxRetrievedChunks": 8,
                 "maxToolOutputTokens": 300,
                 "maxToolCallsPerLease": 4,
                 "maxComputeUnits": 4,
                 "allowedModels": ["gpt-4o-mini"],
                 "allowedCapabilities": {
                   "chatCompletion": ["chat"],
                   "toolCall": ["chat", "read"]
                 },
                 "allowedToolsByActorWorkspace": {
                   "demo|sample": ["net.post", "net.fetch", "shell.exec"]
                 },
                 "deniedToolCategories": ["exec"],
                 "approvalRequiredToolCategories": ["networkWrite"],
                 "riskRequiresApproval": []
               }
               """;
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
