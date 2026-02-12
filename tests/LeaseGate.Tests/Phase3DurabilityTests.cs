using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;
using Microsoft.Data.Sqlite;

namespace LeaseGate.Tests;

public class Phase3DurabilityTests
{
    [Fact]
    public void Status_ReportsHealthyRuntimeState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var options = BuildOptions(dbPath, leaseTtlMs: 2_000);

        using var governor = BuildGovernor(options, policyPath, new RecordingAuditWriter());
        var status = governor.GetStatus();

        Assert.True(status.Healthy);
        Assert.True(status.DurableStateEnabled);
        Assert.Equal("v1", status.PolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(status.PolicyHash));
    }

    [Fact]
    public void Diagnostics_Export_WritesSnapshotBundle()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var diagnosticsPath = Path.Combine(Path.GetTempPath(), $"leasegate-diagnostics-{Guid.NewGuid():N}.json");
        var options = BuildOptions(dbPath, leaseTtlMs: 2_000);

        using var governor = BuildGovernor(options, policyPath, new RecordingAuditWriter());
        var exported = governor.ExportDiagnostics(new ExportDiagnosticsRequest
        {
            OutputPath = diagnosticsPath,
            IdempotencyKey = "diag-export"
        });

        Assert.True(exported.Exported);
        Assert.True(File.Exists(exported.OutputPath));
        var json = File.ReadAllText(exported.OutputPath);
        Assert.Contains("\"status\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"metrics\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restart_RehydratesActiveLeases_WithoutPhantomCapacity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var audit = new RecordingAuditWriter();

        var options = BuildOptions(dbPath, leaseTtlMs: 10_000);
        using (var governor = BuildGovernor(options, policyPath, audit))
        {
            var acquired = await governor.AcquireAsync(BaseAcquire("rehydrate-1"), CancellationToken.None);
            Assert.True(acquired.Granted);
        }

        using (var restarted = BuildGovernor(options, policyPath, audit))
        {
            var metrics = restarted.GetMetricsSnapshot();
            Assert.Equal(1, metrics.ActiveLeases);

            var second = await restarted.AcquireAsync(BaseAcquire("rehydrate-2"), CancellationToken.None);
            Assert.False(second.Granted);
            Assert.Equal("concurrency_limit_reached", second.DeniedReason);
        }
    }

    [Fact]
    public async Task Restart_ExpiresStaleLeases_AndAuditsRestartExpiry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var audit = new RecordingAuditWriter();

        var options = BuildOptions(dbPath, leaseTtlMs: 150);
        using (var governor = BuildGovernor(options, policyPath, audit))
        {
            var acquired = await governor.AcquireAsync(BaseAcquire("stale-lease"), CancellationToken.None);
            Assert.True(acquired.Granted);
        }

        await Task.Delay(350);

        using var restarted = BuildGovernor(options, policyPath, audit);
        await Task.Delay(80);
        Assert.Contains(audit.Events, e => e.EventType == "lease_expired_by_restart");
        Assert.Equal(0, restarted.GetMetricsSnapshot().ActiveLeases);
    }

    [Fact]
    public async Task Chaos_BurstAcquireRelease_DoesNotLeakCapacity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var options = BuildOptions(dbPath, leaseTtlMs: 5_000);
        options.MaxInFlight = 2;
        options.MaxRequestsPerMinute = 1_000;
        options.MaxTokensPerMinute = 1_000_000;

        using var governor = BuildGovernor(options, policyPath, new RecordingAuditWriter());
        for (var index = 0; index < 40; index++)
        {
            var acquired = await governor.AcquireAsync(BaseAcquire($"burst-{index}"), CancellationToken.None);
            Assert.True(acquired.Granted);

            var released = await governor.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acquired.LeaseId,
                ActualPromptTokens = 20,
                ActualOutputTokens = 10,
                ActualCostCents = 1,
                Outcome = LeaseOutcome.Success,
                IdempotencyKey = $"release-{index}"
            }, CancellationToken.None);

            Assert.Equal(ReleaseClassification.Recorded, released.Classification);
        }

        Assert.Equal(0, governor.GetMetricsSnapshot().ActiveLeases);
    }

    [Fact]
    public async Task ClockSkew_PastExpiryInState_RecoversWithoutBlock()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"leasegate-state-{Guid.NewGuid():N}.db");
        var policyPath = WritePolicyFile();
        var options = BuildOptions(dbPath, leaseTtlMs: 30_000);

        using (var governor = BuildGovernor(options, policyPath, new RecordingAuditWriter()))
        {
            var acquired = await governor.AcquireAsync(BaseAcquire("clock-skew"), CancellationToken.None);
            Assert.True(acquired.Granted);
        }

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE leases SET expires_at_utc = $expiresAt";
            cmd.Parameters.AddWithValue("$expiresAt", DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
            cmd.ExecuteNonQuery();
        }

        using var restarted = BuildGovernor(options, policyPath, new RecordingAuditWriter());
        var status = restarted.GetStatus();
        Assert.Equal(0, status.ActiveLeases);

        var afterSkew = await restarted.AcquireAsync(BaseAcquire("clock-skew-recover"), CancellationToken.None);
        Assert.True(afterSkew.Granted);
    }

    private static LeaseGovernor BuildGovernor(LeaseGovernorOptions options, string policyPath, IAuditWriter audit)
    {
        var policy = new PolicyEngine(policyPath, hotReload: false);
        var tools = new ToolRegistry(new[]
        {
            new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead }
        });

        return new LeaseGovernor(options, policy, audit, tools);
    }

    private static LeaseGovernorOptions BuildOptions(string dbPath, int leaseTtlMs)
    {
        return new LeaseGovernorOptions
        {
            MaxInFlight = 1,
            DailyBudgetCents = 100,
            LeaseTtl = TimeSpan.FromMilliseconds(leaseTtlMs),
            MaxRequestsPerMinute = 20,
            MaxTokensPerMinute = 10000,
            MaxContextTokens = 400,
            MaxRetrievedChunks = 8,
            MaxToolOutputTokens = 200,
            MaxToolCallsPerLease = 3,
            MaxComputeUnits = 2,
            EnableDurableState = true,
            StateDatabasePath = dbPath
        };
    }

    private static AcquireLeaseRequest BaseAcquire(string key)
    {
        return new AcquireLeaseRequest
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
            EstimatedToolOutputTokens = 0,
            EstimatedComputeUnits = 1,
            RequestedCapabilities = new List<string> { "chat" },
            RequestedTools = new List<ToolIntent>(),
            IdempotencyKey = key
        };
    }

    private static string WritePolicyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leasegate-policy-phase3-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "policyVersion": "v1",
              "maxInFlight": 4,
              "dailyBudgetCents": 200,
              "maxRequestsPerMinute": 90,
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
              "allowedToolsByActorWorkspace": {
                "demo|sample": []
              },
              "deniedToolCategories": [],
              "approvalRequiredToolCategories": [],
              "riskRequiresApproval": []
            }
            """);
        return path;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            lock (Events)
            {
                Events.Add(auditEvent);
            }

            return Task.FromResult(new AuditWriteResult
            {
                EntryHash = auditEvent.EntryHash,
                PrevHash = auditEvent.PrevHash,
                LineNumber = Events.Count
            });
        }
    }
}
