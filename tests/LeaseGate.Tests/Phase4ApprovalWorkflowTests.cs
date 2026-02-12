using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class Phase4ApprovalWorkflowTests
{
    [Fact]
    public async Task ApprovalQueue_RequiresTwoReviewers_AndReceiptHasChain()
    {
        var audit = new RecordingAuditWriter();
        using var governor = BuildGovernor(audit);

        var request = governor.RequestApproval(new ApprovalRequest
        {
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            Reason = "need network write for deploy",
            RequestedBy = "alice",
            ToolCategory = ToolCategory.NetworkWrite,
            TtlSeconds = 300,
            SingleUse = true,
            IdempotencyKey = "approval-q-1"
        });

        Assert.Equal(2, request.RequiredReviewers);

        var queue = governor.ListPendingApprovals(new ApprovalQueueRequest
        {
            WorkspaceId = "ws-alpha",
            ToolCategory = ToolCategory.NetworkWrite
        });
        Assert.Single(queue.Items);

        var first = governor.ReviewApproval(new ReviewApprovalRequest
        {
            ApprovalId = request.ApprovalId,
            ReviewerId = "reviewer-1",
            Approve = true,
            Comment = "looks good",
            IdempotencyKey = "review-1"
        });
        Assert.Equal(ApprovalDecisionStatus.Pending, first.Status);

        var second = governor.ReviewApproval(new ReviewApprovalRequest
        {
            ApprovalId = request.ApprovalId,
            ReviewerId = "reviewer-2",
            Approve = true,
            Comment = "approved for deploy window",
            IdempotencyKey = "review-2"
        });
        Assert.Equal(ApprovalDecisionStatus.Granted, second.Status);
        Assert.False(string.IsNullOrWhiteSpace(second.ApprovalToken));

        var acquire = await governor.AcquireAsync(new AcquireLeaseRequest
        {
            OrgId = "org-acme",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            PrincipalType = PrincipalType.Human,
            Role = Role.Member,
            ActionType = ActionType.ToolCall,
            ModelId = "gpt-4o-mini",
            ProviderId = "fake",
            EstimatedPromptTokens = 10,
            MaxOutputTokens = 10,
            EstimatedCostCents = 2,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>
            {
                new() { ToolId = "net.post", Category = ToolCategory.NetworkWrite }
            },
            ApprovalToken = second.ApprovalToken,
            IdempotencyKey = "approval-acquire"
        }, CancellationToken.None);

        Assert.True(acquire.Granted);

        var release = await governor.ReleaseAsync(new ReleaseLeaseRequest
        {
            LeaseId = acquire.LeaseId,
            ActualPromptTokens = 5,
            ActualOutputTokens = 5,
            ActualCostCents = 3,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "approval-release"
        }, CancellationToken.None);

        Assert.NotNull(release.Receipt);
        Assert.Equal(2, release.Receipt!.ApprovalChain.Count);
        Assert.Contains(release.Receipt.ApprovalChain, r => r.ReviewerId == "reviewer-1");
        Assert.Contains(release.Receipt.ApprovalChain, r => r.ReviewerId == "reviewer-2");

        Assert.Contains(audit.Events, e => e.EventType == "approval_reviewed");
    }

    private static LeaseGovernor BuildGovernor(IAuditWriter audit)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase4-approvals-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "policyVersion": "v4-commit4",
              "maxInFlight": 4,
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
                "toolCall": ["chat"]
              },
              "allowedToolsByActorWorkspace": {
                "alice|ws-alpha": ["net.post"]
              },
              "approvalRequiredToolCategories": ["networkWrite"],
              "approvalReviewersByToolCategory": {
                "networkWrite": 2
              },
              "riskRequiresApproval": []
            }
            """);

        var policy = new PolicyEngine(path, hotReload: false);
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.post", Category = ToolCategory.NetworkWrite }
        });

        return new LeaseGovernor(new LeaseGovernorOptions
        {
            MaxInFlight = 3,
            DailyBudgetCents = 100,
            MaxRequestsPerMinute = 200,
            MaxTokensPerMinute = 20000,
            MaxToolCallsPerLease = 3,
            MaxToolOutputTokens = 200,
            MaxComputeUnits = 3,
            ReceiptThresholdCostCents = 1,
            EnableDurableState = false
        }, policy, audit, tools);
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
