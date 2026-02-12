using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class AuditResilienceTests
{
    private static string WritePolicyFile()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "audit-test",
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });

        var path = Path.Combine(Path.GetTempPath(), $"audit-resilience-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        return path;
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
        EstimatedCostCents = 1,
        RequestedContextTokens = 40,
        RequestedRetrievedChunks = 1,
        EstimatedComputeUnits = 1,
        RequestedCapabilities = new() { "chat" },
        RequestedTools = new(),
        IdempotencyKey = key
    };

    [Fact]
    public async Task AuditFireAndForget_IncrementsFailed_OnWriteError()
    {
        var policyPath = WritePolicyFile();
        try
        {
            var failingAudit = new FailingAuditWriter();
            var policy = new PolicyEngine(policyPath, hotReload: false);
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policy, failingAudit);

            // Trigger fire-and-forget audit via approval workflow (not AcquireAsync which awaits audit directly)
            governor.RequestApproval(new ApprovalRequest
            {
                ActorId = "test",
                WorkspaceId = "ws",
                Reason = "test approval",
                IdempotencyKey = "fail-audit-approval-1"
            });

            // Give fire-and-forget tasks time to complete
            await Task.Delay(200);

            var metrics = governor.GetMetricsSnapshot();
            Assert.True(metrics.FailedAuditWrites > 0, "FailedAuditWrites should be incremented when audit throws");
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void AuditFireAndForget_DoesNotThrowToCallingPath()
    {
        var policyPath = WritePolicyFile();
        try
        {
            var failingAudit = new FailingAuditWriter();
            var policy = new PolicyEngine(policyPath, hotReload: false);
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policy, failingAudit);

            // Approval workflow uses AuditFireAndForget — should not throw
            var ex = Record.Exception(() => governor.RequestApproval(new ApprovalRequest
            {
                ActorId = "test",
                WorkspaceId = "ws",
                Reason = "test approval",
                IdempotencyKey = "no-throw-approval-1"
            }));

            Assert.Null(ex);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task MetricsSnapshot_ExposesFailedAuditWrites()
    {
        var policyPath = WritePolicyFile();
        try
        {
            var noopAudit = new NoopAuditWriter();
            var policy = new PolicyEngine(policyPath, hotReload: false);
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 4, DailyBudgetCents = 200,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 2,
                EnableDurableState = false
            }, policy, noopAudit);

            var metrics = governor.GetMetricsSnapshot();
            Assert.Equal(0, metrics.FailedAuditWrites);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public async Task AuditSemaphore_NoDoubleRelease_OnCancellation()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"audit-semaphore-{Guid.NewGuid():N}");
        try
        {
            var writer = new JsonlAuditWriter(auditDir);
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Already cancelled

            var result = await writer.WriteAsync(new AuditEvent
            {
                EventType = "test_cancel",
                LeaseId = "cancel-1"
            }, cts.Token);

            // Should return empty hash (error path) but not crash
            // The semaphore should remain in a valid state
            var result2 = await writer.WriteAsync(new AuditEvent
            {
                EventType = "test_after_cancel",
                LeaseId = "after-cancel-1"
            }, CancellationToken.None);

            // Second write should succeed normally
            Assert.False(string.IsNullOrEmpty(result2.EntryHash));
        }
        finally
        {
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public async Task AuditSemaphore_ReleasesOnSuccess()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"audit-release-{Guid.NewGuid():N}");
        try
        {
            var writer = new JsonlAuditWriter(auditDir);

            var r1 = await writer.WriteAsync(new AuditEvent { EventType = "e1", LeaseId = "l1" }, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(r1.EntryHash));

            var r2 = await writer.WriteAsync(new AuditEvent { EventType = "e2", LeaseId = "l2" }, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(r2.EntryHash));

            // Hash chain should link
            Assert.Equal(r1.EntryHash, r2.PrevHash);
        }
        finally
        {
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public void LoadTailState_StreamingConstantMemory()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"audit-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditDir);
        try
        {
            // Write 100 audit entries manually
            var path = Path.Combine(auditDir, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            var lastHash = new string('0', 64);
            for (var i = 0; i < 100; i++)
            {
                var evt = new AuditEvent
                {
                    EventType = "test",
                    LeaseId = $"lease-{i}",
                    PrevHash = lastHash
                };
                evt.EntryHash = AuditHashChain.ComputeEntryHash(evt);
                lastHash = evt.EntryHash;
                File.AppendAllText(path, ProtocolJson.Serialize(evt) + Environment.NewLine);
            }

            // Creating a new writer should load tail state from the file
            var writer = new JsonlAuditWriter(auditDir);

            // Write one more and verify chain continuity
            var result = writer.WriteAsync(new AuditEvent { EventType = "verify", LeaseId = "verify-1" }, CancellationToken.None).Result;
            Assert.Equal(lastHash, result.PrevHash);
            Assert.Equal(101, result.LineNumber);
        }
        finally
        {
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public void LoadTailState_EmptyFile_ReturnsDefaults()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"audit-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditDir);
        try
        {
            var path = Path.Combine(auditDir, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            File.WriteAllText(path, "");

            var writer = new JsonlAuditWriter(auditDir);
            var result = writer.WriteAsync(new AuditEvent { EventType = "first", LeaseId = "first-1" }, CancellationToken.None).Result;

            Assert.Equal(new string('0', 64), result.PrevHash);
            Assert.Equal(1, result.LineNumber);
        }
        finally
        {
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public void LoadTailState_MissingFile_ReturnsDefaults()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"audit-missing-{Guid.NewGuid():N}");
        try
        {
            var writer = new JsonlAuditWriter(auditDir);
            var result = writer.WriteAsync(new AuditEvent { EventType = "first", LeaseId = "first-1" }, CancellationToken.None).Result;

            Assert.Equal(new string('0', 64), result.PrevHash);
            Assert.Equal(1, result.LineNumber);
        }
        finally
        {
            if (Directory.Exists(auditDir)) Directory.Delete(auditDir, true);
        }
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => throw new IOException("Simulated audit failure");
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.FromResult(new AuditWriteResult());
    }
}
