using System.Reflection;
using LeaseGate.Hub;
using LeaseGate.Protocol;
using LeaseGate.Policy;
using LeaseGate.Service;

namespace LeaseGate.Tests;

public class CsvInjectionTests
{
    private static string InvokeCsvEscape(string value)
    {
        var method = typeof(HubControlPlane).GetMethod("CsvEscape",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object[] { value })!;
    }

    [Fact]
    public void CsvEscape_PrefixesEqualsSign()
    {
        var result = InvokeCsvEscape("=SUM(A1:A10)");
        Assert.StartsWith("'", result);
    }

    [Fact]
    public void CsvEscape_PrefixesPlusSign()
    {
        var result = InvokeCsvEscape("+cmd");
        Assert.StartsWith("'", result);
    }

    [Fact]
    public void CsvEscape_PrefixesMinusSign()
    {
        var result = InvokeCsvEscape("-cmd");
        Assert.StartsWith("'", result);
    }

    [Fact]
    public void CsvEscape_PrefixesAtSign()
    {
        var result = InvokeCsvEscape("@SUM");
        Assert.StartsWith("'", result);
    }

    [Fact]
    public void CsvEscape_PassesThroughSafeValues()
    {
        Assert.Equal("hello", InvokeCsvEscape("hello"));
        Assert.Equal("org-acme", InvokeCsvEscape("org-acme"));
        Assert.Equal("gpt-4o-mini", InvokeCsvEscape("gpt-4o-mini"));
    }

    [Fact]
    public void CsvEscape_EscapesCommasWithQuotes()
    {
        var result = InvokeCsvEscape("a,b");
        Assert.Contains("\"", result);
    }

    [Fact]
    public async Task ExportDailySummary_CsvContainsEscapedFields()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "csv-test",
            MaxInFlight = 4,
            DailyBudgetCents = 200,
            OrgDailyBudgetCents = 200,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });

        var policyPath = Path.Combine(Path.GetTempPath(), $"csv-inject-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, policyJson);
        var csvPath = Path.Combine(Path.GetTempPath(), $"csv-inject-{Guid.NewGuid():N}.csv");

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

            // Acquire + release with an actor name that starts with =
            var acq = await hub.AcquireAsync(new AcquireLeaseRequest
            {
                ActorId = "=SUM(evil)",
                OrgId = "org-csv",
                WorkspaceId = "ws-csv",
                ActionType = ActionType.ChatCompletion,
                ModelId = "gpt-4o-mini",
                ProviderId = "fake",
                EstimatedPromptTokens = 10,
                MaxOutputTokens = 10,
                EstimatedCostCents = 1,
                EstimatedComputeUnits = 1,
                RequestedCapabilities = new() { "chat" },
                IdempotencyKey = "csv-1"
            }, CancellationToken.None);

            if (acq.Granted)
            {
                await hub.ReleaseAsync(new ReleaseLeaseRequest
                {
                    LeaseId = acq.LeaseId,
                    ActualCostCents = 1,
                    Outcome = LeaseOutcome.Success,
                    IdempotencyKey = "csv-rel-1"
                }, CancellationToken.None);
            }

            var result = hub.ExportDailySummary(new ExportSummaryRequest { OutputPath = csvPath, Format = "csv" });
            Assert.True(result.Exported);

            var csv = File.ReadAllText(csvPath);
            // The actor name =SUM(evil) should be escaped with a leading apostrophe
            Assert.DoesNotContain(",=SUM", csv);
            if (csv.Contains("SUM"))
            {
                Assert.Contains("'=SUM", csv);
            }
        }
        finally
        {
            File.Delete(policyPath);
            File.Delete(csvPath);
        }
    }
}
