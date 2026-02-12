using LeaseGate.Audit;
using LeaseGate.Hub;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class PathTraversalTests
{
    private static string WritePolicyFile()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "path-test",
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

        var path = Path.Combine(Path.GetTempPath(), $"path-traversal-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        return path;
    }

    private static LeaseGovernor BuildGovernor(string policyPath)
    {
        var policy = new PolicyEngine(policyPath, hotReload: false);
        return new LeaseGovernor(new LeaseGovernorOptions
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
            EnableDurableState = false
        }, policy, new NoopAuditWriter());
    }

    [Fact]
    public void ExportDiagnostics_RejectsDotDotTraversal()
    {
        var policyPath = WritePolicyFile();
        try
        {
            using var governor = BuildGovernor(policyPath);
            var traversalPath = Path.Combine(Path.GetTempPath(), "..", "..", "etc", "diag.json");
            var result = governor.ExportDiagnostics(new ExportDiagnosticsRequest { OutputPath = traversalPath });
            Assert.False(result.Exported);
            // Path.GetFullPath resolves ".." on Windows, so it hits the "not under temp/appdata" check
            Assert.Contains("path must", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void ExportDiagnostics_RejectsPathOutsideTempAndAppData()
    {
        var policyPath = WritePolicyFile();
        try
        {
            using var governor = BuildGovernor(policyPath);
            var result = governor.ExportDiagnostics(new ExportDiagnosticsRequest { OutputPath = @"C:\Windows\diag.json" });
            Assert.False(result.Exported);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void ExportDiagnostics_AcceptsTempPath()
    {
        var policyPath = WritePolicyFile();
        var outputPath = Path.Combine(Path.GetTempPath(), $"diag-test-{Guid.NewGuid():N}.json");
        try
        {
            using var governor = BuildGovernor(policyPath);
            var result = governor.ExportDiagnostics(new ExportDiagnosticsRequest { OutputPath = outputPath });
            Assert.True(result.Exported);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(policyPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportRunawayReport_RejectsDotDotTraversal()
    {
        var policyPath = WritePolicyFile();
        try
        {
            using var governor = BuildGovernor(policyPath);
            var traversalPath = Path.Combine(Path.GetTempPath(), "..", "..", "etc", "runaway.json");
            var result = governor.ExportRunawayReport(new ExportRunawayReportRequest { OutputPath = traversalPath });
            Assert.False(result.Exported);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void ExportRunawayReport_AcceptsTempPath()
    {
        var policyPath = WritePolicyFile();
        var outputPath = Path.Combine(Path.GetTempPath(), $"runaway-test-{Guid.NewGuid():N}.json");
        try
        {
            using var governor = BuildGovernor(policyPath);
            var result = governor.ExportRunawayReport(new ExportRunawayReportRequest { OutputPath = outputPath });
            Assert.True(result.Exported);
        }
        finally
        {
            File.Delete(policyPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportDailySummary_RejectsDotDotTraversal()
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

            var traversalPath = Path.Combine(Path.GetTempPath(), "..", "..", "etc", "daily.csv");
            var result = hub.ExportDailySummary(new ExportSummaryRequest { OutputPath = traversalPath, Format = "csv" });
            Assert.False(result.Exported);
        }
        finally { File.Delete(policyPath); }
    }

    [Fact]
    public void ExportDailySummary_AcceptsTempPath()
    {
        var policyPath = WritePolicyFile();
        var outputPath = Path.Combine(Path.GetTempPath(), $"daily-test-{Guid.NewGuid():N}.csv");
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

            var result = hub.ExportDailySummary(new ExportSummaryRequest { OutputPath = outputPath, Format = "csv" });
            Assert.True(result.Exported);
        }
        finally
        {
            File.Delete(policyPath);
            File.Delete(outputPath);
        }
    }

    private sealed class NoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.FromResult(new AuditWriteResult());
    }
}
