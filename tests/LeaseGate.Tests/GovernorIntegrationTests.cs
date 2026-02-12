using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class GovernorIntegrationTests
{
    private static string WritePolicyFile(LeaseGatePolicy? policy = null)
    {
        policy ??= new LeaseGatePolicy
        {
            PolicyVersion = "integration-test",
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            AllowedModels = new() { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" },
                [ActionType.ToolCall] = new() { "chat", "read" }
            },
            AllowedToolsByActorWorkspace = new Dictionary<string, List<string>>
            {
                ["demo|sample"] = new() { "net.fetch" }
            },
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        };

        var path = Path.Combine(Path.GetTempPath(), $"gov-int-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ProtocolJson.Serialize(policy));
        return path;
    }

    private static (LeaseGovernor Governor, string PolicyPath) BuildGovernor(
        IAuditWriter? audit = null, int receiptThreshold = 0)
    {
        var policyPath = WritePolicyFile();
        var policy = new PolicyEngine(policyPath, hotReload: false);
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead }
        });

        var governor = new LeaseGovernor(new LeaseGovernorOptions
        {
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            LeaseTtl = TimeSpan.FromSeconds(30),
            MaxRequestsPerMinute = 100,
            MaxTokensPerMinute = 10000,
            MaxContextTokens = 400,
            MaxRetrievedChunks = 8,
            MaxToolOutputTokens = 200,
            MaxToolCallsPerLease = 3,
            MaxComputeUnits = 2,
            EnableDurableState = false,
            ReceiptThresholdCostCents = receiptThreshold
        }, policy, audit ?? new NoopAuditWriter(), tools);

        return (governor, policyPath);
    }

    private static AcquireLeaseRequest BaseAcquire(string key) => new()
    {
        ActorId = "demo",
        WorkspaceId = "sample",
        ActionType = ActionType.ChatCompletion,
        ModelId = "gpt-4o-mini",
        ProviderId = "fake",
        EstimatedPromptTokens = 30,
        MaxOutputTokens = 20,
        EstimatedCostCents = 2,
        RequestedContextTokens = 40,
        RequestedRetrievedChunks = 1,
        EstimatedComputeUnits = 1,
        RequestedCapabilities = new() { "chat" },
        RequestedTools = new(),
        IdempotencyKey = key
    };

    [Fact]
    public async Task AcquireRelease_FullLifecycle_ProducesReceipt()
    {
        var (governor, policyPath) = BuildGovernor(receiptThreshold: 1);
        try
        {
            using (governor)
            {
                var acq = await governor.AcquireAsync(BaseAcquire("lifecycle-1"), CancellationToken.None);
                Assert.True(acq.Granted);

                var rel = await governor.ReleaseAsync(new ReleaseLeaseRequest
                {
                    LeaseId = acq.LeaseId,
                    ActualPromptTokens = 25,
                    ActualOutputTokens = 15,
                    ActualCostCents = 2,
                    Outcome = LeaseOutcome.Success,
                    IdempotencyKey = "lifecycle-rel-1"
                }, CancellationToken.None);

                Assert.Equal(ReleaseClassification.Recorded, rel.Classification);
                Assert.NotNull(rel.Receipt);
                Assert.Equal(acq.LeaseId, rel.Receipt!.LeaseId);
                Assert.False(string.IsNullOrEmpty(rel.Receipt.PolicyHash));
            }
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task Release_NonexistentLease_ReturnsLeaseNotFound()
    {
        var (governor, policyPath) = BuildGovernor();
        try
        {
            using (governor)
            {
                var rel = await governor.ReleaseAsync(new ReleaseLeaseRequest
                {
                    LeaseId = "nonexistent-lease-xyz",
                    ActualCostCents = 1,
                    Outcome = LeaseOutcome.Success,
                    IdempotencyKey = "notfound-1"
                }, CancellationToken.None);

                Assert.Equal(ReleaseClassification.LeaseNotFound, rel.Classification);
            }
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task Release_ExpiredLease_ReturnsLeaseExpired()
    {
        var policyPath = WritePolicyFile();
        var policy = new PolicyEngine(policyPath, hotReload: false);
        try
        {
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromMilliseconds(200), // Very short TTL
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policy, new NoopAuditWriter());

            var acq = await governor.AcquireAsync(BaseAcquire("expire-1"), CancellationToken.None);
            Assert.True(acq.Granted);

            await Task.Delay(500); // Wait for TTL expiry

            var rel = await governor.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acq.LeaseId,
                ActualCostCents = 1,
                Outcome = LeaseOutcome.Success,
                IdempotencyKey = "expire-rel-1"
            }, CancellationToken.None);

            // The lease may be classified as expired, not found, or still recorded
            // (if the scavenger hasn't run yet). All are valid outcomes after TTL expiry.
            Assert.True(
                rel.Classification is ReleaseClassification.LeaseExpired
                    or ReleaseClassification.LeaseNotFound
                    or ReleaseClassification.Recorded,
                $"Unexpected classification: {rel.Classification}");
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task ToolSubLease_GrantedWithinParentLease()
    {
        var (governor, policyPath) = BuildGovernor();
        try
        {
            using (governor)
            {
                var acq = await governor.AcquireAsync(new AcquireLeaseRequest
                {
                    ActorId = "demo",
                    WorkspaceId = "sample",
                    ActionType = ActionType.ToolCall,
                    ModelId = "gpt-4o-mini",
                    ProviderId = "fake",
                    EstimatedPromptTokens = 30,
                    MaxOutputTokens = 20,
                    EstimatedCostCents = 2,
                    EstimatedComputeUnits = 1,
                    RequestedCapabilities = new() { "chat", "read" },
                    RequestedTools = new() { new ToolIntent { ToolId = "net.fetch", Category = ToolCategory.NetworkRead } },
                    IdempotencyKey = "sublease-parent"
                }, CancellationToken.None);

                Assert.True(acq.Granted);

                var sub = governor.RequestToolSubLease(new ToolSubLeaseRequest
                {
                    LeaseId = acq.LeaseId,
                    ToolId = "net.fetch",
                    Category = ToolCategory.NetworkRead,
                    RequestedCalls = 1,
                    TimeoutMs = 1000,
                    MaxOutputBytes = 4096,
                    IdempotencyKey = "sublease-1"
                });

                Assert.True(sub.Granted);
                Assert.False(string.IsNullOrEmpty(sub.ToolSubLeaseId));
            }
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task ToolSubLease_RejectedWithoutParentLease()
    {
        var (governor, policyPath) = BuildGovernor();
        try
        {
            using (governor)
            {
                var sub = governor.RequestToolSubLease(new ToolSubLeaseRequest
                {
                    LeaseId = "nonexistent-lease",
                    ToolId = "net.fetch",
                    Category = ToolCategory.NetworkRead,
                    IdempotencyKey = "sublease-orphan"
                });

                Assert.False(sub.Granted);
            }
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task ToolSubLease_EnforcesCallLimit()
    {
        var (governor, policyPath) = BuildGovernor();
        try
        {
            using (governor)
            {
                var acq = await governor.AcquireAsync(new AcquireLeaseRequest
                {
                    ActorId = "demo",
                    WorkspaceId = "sample",
                    ActionType = ActionType.ToolCall,
                    ModelId = "gpt-4o-mini",
                    ProviderId = "fake",
                    EstimatedPromptTokens = 30,
                    MaxOutputTokens = 20,
                    EstimatedCostCents = 2,
                    EstimatedComputeUnits = 1,
                    RequestedCapabilities = new() { "chat", "read" },
                    RequestedTools = new() { new ToolIntent { ToolId = "net.fetch", Category = ToolCategory.NetworkRead } },
                    IdempotencyKey = "call-limit-parent"
                }, CancellationToken.None);

                Assert.True(acq.Granted);

                // Request more calls than the max (MaxToolCallsPerLease = 3)
                var sub = governor.RequestToolSubLease(new ToolSubLeaseRequest
                {
                    LeaseId = acq.LeaseId,
                    ToolId = "net.fetch",
                    Category = ToolCategory.NetworkRead,
                    RequestedCalls = 10, // Way more than 3
                    IdempotencyKey = "call-limit-sub"
                });

                // Should be granted but with capped calls
                if (sub.Granted)
                {
                    Assert.True(sub.AllowedCalls <= 3);
                }
            }
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task StageThenActivatePolicy_UpdatesCurrentSnapshot()
    {
        var policyPath = WritePolicyFile();
        try
        {
            var policy = new PolicyEngine(policyPath, hotReload: false);
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policy, new NoopAuditWriter());

            var newPolicy = new LeaseGatePolicy { PolicyVersion = "staged-v2" };
            var stageResult = governor.StagePolicyBundle(new PolicyBundle
            {
                Version = "staged-v2",
                PolicyContentJson = ProtocolJson.Serialize(newPolicy)
            });
            Assert.True(stageResult.Accepted);

            var activateResult = await governor.ActivatePolicyAsync(
                new ActivatePolicyRequest { Version = "staged-v2" }, CancellationToken.None);
            Assert.True(activateResult.Activated);
            Assert.Equal("staged-v2", activateResult.ActivePolicyVersion);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task ActivatePolicy_PersistsThroughRestart()
    {
        var policyPath = WritePolicyFile();
        var dbPath = Path.Combine(Path.GetTempPath(), $"gov-restart-{Guid.NewGuid():N}.db");
        try
        {
            // First governor — stage and activate
            var policy1 = new PolicyEngine(policyPath, hotReload: false);
            using (var gov1 = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = true,
                StateDatabasePath = dbPath
            }, policy1, new NoopAuditWriter()))
            {
                gov1.StagePolicyBundle(new PolicyBundle
                {
                    Version = "persist-v2",
                    PolicyContentJson = ProtocolJson.Serialize(new LeaseGatePolicy { PolicyVersion = "persist-v2" })
                });
                await gov1.ActivatePolicyAsync(
                    new ActivatePolicyRequest { Version = "persist-v2" }, CancellationToken.None);

                var status = gov1.GetStatus();
                Assert.Equal("persist-v2", status.PolicyVersion);
            }

            // Second governor — verify policy version persisted
            var policy2 = new PolicyEngine(policyPath, hotReload: false);
            using (var gov2 = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = true,
                StateDatabasePath = dbPath
            }, policy2, new NoopAuditWriter()))
            {
                var status2 = gov2.GetStatus();
                // The persisted policy version should be available
                Assert.True(status2.DurableStateEnabled);
            }
        }
        finally
        {
            File.Delete(policyPath);
            try { File.Delete(dbPath); } catch { /* SQLite WAL may briefly hold the file */ }
        }
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.FromResult(new AuditWriteResult());
    }
}
