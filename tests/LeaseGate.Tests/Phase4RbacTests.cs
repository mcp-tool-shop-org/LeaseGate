using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase4RbacTests
{
    [Fact]
    public async Task SameRequest_DiffersByRole()
    {
        var audit = new RecordingAuditWriter();
        using var governor = BuildGovernor(CreatePolicyJson(), audit);

        var member = await governor.AcquireAsync(BaseAcquire("same-role-member", Role.Member), CancellationToken.None);
        var viewer = await governor.AcquireAsync(BaseAcquire("same-role-viewer", Role.Viewer), CancellationToken.None);

        Assert.True(member.Granted);
        Assert.False(viewer.Granted);
        Assert.Equal("capability_not_allowed_for_role", viewer.DeniedReason);
    }

    [Fact]
    public async Task ServiceAccount_RequiresScopedToken_AndAuditsPrincipal()
    {
        var audit = new RecordingAuditWriter();
        using var governor = BuildGovernor(CreatePolicyJson(), audit);

        var denied = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "svc-bot",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Service,
            Role = Role.ServiceAccount,
            AuthToken = "bad-token",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = "svc-bad"
        }, CancellationToken.None);

        Assert.False(denied.Granted);
        Assert.Equal("service_account_unauthorized", denied.DeniedReason);

        var granted = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "svc-bot",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Service,
            Role = Role.ServiceAccount,
            AuthToken = "svc-alpha-token",
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = "svc-good"
        }, CancellationToken.None);

        Assert.True(granted.Granted);

        var leaseAcquiredEvent = audit.Events.Last(e => e.EventType == "lease_acquired");
        Assert.Equal(PrincipalType.Service, leaseAcquiredEvent.PrincipalType);
        Assert.Equal(Role.ServiceAccount, leaseAcquiredEvent.Role);
        Assert.Equal("org-acme", leaseAcquiredEvent.OrgId);
    }

    private static AcquireLeaseRequest BaseAcquire(string key, Role role)
    {
        return new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = role,
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 20,
            MaxOutputTokens = 20,
            EstimatedCostCents = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = key
        };
    }

    private static LeaseGovernor BuildGovernor(string policyJson, IAuditWriter audit)
    {
        var policyFile = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase4-rbac-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyFile, policyJson);

        var policy = new PolicyEngine(policyFile, hotReload: false);
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead },
            new ToolDefinition { ToolId = "net.post", Category = ToolCategory.NetworkWrite }
        });

        return new LeaseGovernor(
            new LeaseGovernorOptions
            {
                MaxInFlight = 10,
                DailyBudgetCents = 500,
                LeaseTtl = TimeSpan.FromSeconds(15),
                MaxRequestsPerMinute = 200,
                MaxTokensPerMinute = 20000,
                MaxContextTokens = 2000,
                MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 300,
                MaxToolCallsPerLease = 4,
                MaxComputeUnits = 5,
                EnableDurableState = false
            },
            policy,
            audit,
            tools);
    }

    private static string CreatePolicyJson()
    {
        return """
               {
                 "policyVersion": "v4-commit1",
                 "maxInFlight": 6,
                 "dailyBudgetCents": 1000,
                 "maxRequestsPerMinute": 120,
                 "maxTokensPerMinute": 10000,
                 "maxContextTokens": 2000,
                 "maxRetrievedChunks": 8,
                 "maxToolOutputTokens": 300,
                 "maxToolCallsPerLease": 4,
                 "maxComputeUnits": 4,
                 "allowedModels": ["gpt-4o-mini"],
                 "allowedModelsByWorkspace": {
                   "ws-alpha": ["gpt-4o-mini"]
                 },
                 "allowedCapabilitiesByRole": {
                   "viewer": {
                     "chatCompletion": ["read"]
                   },
                   "member": {
                     "chatCompletion": ["chat", "read"]
                   },
                   "serviceAccount": {
                     "chatCompletion": ["chat"]
                   }
                 },
                 "allowedCapabilities": {
                   "chatCompletion": ["chat", "read"]
                 },
                 "allowedToolsByWorkspaceRole": {
                   "ws-alpha|Member": ["net.fetch"],
                   "ws-alpha|ServiceAccount": ["net.fetch"]
                 },
                 "serviceAccounts": [
                   {
                     "name": "svc-bot",
                     "token": "svc-alpha-token",
                     "orgId": "org-acme",
                     "workspaceId": "ws-alpha",
                     "role": "serviceAccount",
                     "allowedCapabilities": ["chat"],
                     "allowedModels": ["gpt-4o-mini"],
                     "allowedTools": ["net.fetch"]
                   }
                 ],
                 "riskRequiresApproval": []
               }
               """;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.FromResult(new AuditWriteResult
            {
                PrevHash = auditEvent.PrevHash,
                EntryHash = auditEvent.EntryHash,
                LineNumber = Events.Count
            });
        }
    }
}
