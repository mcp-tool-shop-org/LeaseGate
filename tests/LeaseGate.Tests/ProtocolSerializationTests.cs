using LeaseGate.Protocol;

namespace LeaseGate.Tests;

public class ProtocolSerializationTests
{
    [Fact]
    public void ProtocolJson_AllEnums_SerializeAsStrings()
    {
        var request = new AcquireLeaseRequest
        {
            ActionType = ActionType.ToolCall,
            PrincipalType = PrincipalType.Service,
            Role = Role.Admin,
            IntentClass = IntentClass.Codegen
        };

        var json = ProtocolJson.Serialize(request);
        Assert.Contains("\"toolCall\"", json);
        Assert.Contains("\"service\"", json);
        Assert.Contains("\"admin\"", json);
        Assert.Contains("\"codegen\"", json);

        // Should NOT contain numeric enum values
        Assert.DoesNotContain(":2", json);
    }

    [Fact]
    public void ProtocolJson_NullFields_Omitted()
    {
        var response = new AcquireLeaseResponse
        {
            Granted = true,
            LeaseId = "lease-1",
            RetryAfterMs = null
        };

        var json = ProtocolJson.Serialize(response);
        Assert.DoesNotContain("retryAfterMs", json);
    }

    [Fact]
    public void ProtocolJson_CamelCase_FieldNaming()
    {
        var request = new AcquireLeaseRequest
        {
            SessionId = "s1",
            ClientInstanceId = "c1",
            ActorId = "actor",
            WorkspaceId = "ws",
            EstimatedPromptTokens = 100,
            MaxOutputTokens = 50,
            IdempotencyKey = "key-1"
        };

        var json = ProtocolJson.Serialize(request);
        Assert.Contains("\"sessionId\"", json);
        Assert.Contains("\"clientInstanceId\"", json);
        Assert.Contains("\"actorId\"", json);
        Assert.Contains("\"workspaceId\"", json);
        Assert.Contains("\"estimatedPromptTokens\"", json);
        Assert.Contains("\"maxOutputTokens\"", json);
        Assert.Contains("\"idempotencyKey\"", json);

        // PascalCase should not appear
        Assert.DoesNotContain("\"SessionId\"", json);
        Assert.DoesNotContain("\"ActorId\"", json);
    }

    [Fact]
    public void ProtocolJson_RoundTrip_ReleaseLeaseRequest()
    {
        var original = new ReleaseLeaseRequest
        {
            LeaseId = "lease-rt-1",
            ActualPromptTokens = 100,
            ActualOutputTokens = 50,
            ActualCostCents = 3,
            ToolCallsCount = 2,
            BytesIn = 1024,
            BytesOut = 2048,
            LatencyMs = 150,
            ProviderErrorClassification = ProviderErrorClassification.None,
            Outcome = LeaseOutcome.Success,
            IdempotencyKey = "rt-key-1",
            ToolCalls = new()
            {
                new ToolCallUsage
                {
                    ToolId = "net.fetch",
                    Category = ToolCategory.NetworkRead,
                    DurationMs = 50,
                    Outcome = LeaseOutcome.Success
                }
            }
        };

        var json = ProtocolJson.Serialize(original);
        var deserialized = ProtocolJson.Deserialize<ReleaseLeaseRequest>(json);

        Assert.Equal(original.LeaseId, deserialized.LeaseId);
        Assert.Equal(original.ActualPromptTokens, deserialized.ActualPromptTokens);
        Assert.Equal(original.ActualOutputTokens, deserialized.ActualOutputTokens);
        Assert.Equal(original.ActualCostCents, deserialized.ActualCostCents);
        Assert.Equal(original.Outcome, deserialized.Outcome);
        Assert.Equal(original.IdempotencyKey, deserialized.IdempotencyKey);
        Assert.Single(deserialized.ToolCalls);
        Assert.Equal("net.fetch", deserialized.ToolCalls[0].ToolId);
    }

    [Fact]
    public void ProtocolJson_RoundTrip_MetricsSnapshot()
    {
        var original = new MetricsSnapshot
        {
            ActiveLeases = 3,
            SpendTodayCents = 42,
            RatePoolUtilization = 0.75,
            ContextPoolUtilization = 0.5,
            ComputePoolUtilization = 0.25,
            FailedAuditWrites = 7,
            GrantsByReason = new() { ["model_allowed"] = 10 },
            DeniesByReason = new() { ["budget_exceeded"] = 2 }
        };

        var json = ProtocolJson.Serialize(original);
        var deserialized = ProtocolJson.Deserialize<MetricsSnapshot>(json);

        Assert.Equal(3, deserialized.ActiveLeases);
        Assert.Equal(42, deserialized.SpendTodayCents);
        Assert.Equal(7, deserialized.FailedAuditWrites);
        Assert.Equal(0.75, deserialized.RatePoolUtilization);
        Assert.Equal(10, deserialized.GrantsByReason["model_allowed"]);
        Assert.Equal(2, deserialized.DeniesByReason["budget_exceeded"]);
    }
}
