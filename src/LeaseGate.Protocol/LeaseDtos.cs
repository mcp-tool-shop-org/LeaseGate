namespace LeaseGate.Protocol;

public sealed class AcquireLeaseRequest
{
    public string ActorId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public ActionType ActionType { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public int EstimatedPromptTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public int EstimatedCostCents { get; set; }
    public List<string> RequestedCapabilities { get; set; } = new();
    public List<string> RiskFlags { get; set; } = new();
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class AcquireLeaseResponse
{
    public bool Granted { get; set; }
    public string LeaseId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public LeaseConstraints Constraints { get; set; } = new();
    public string DeniedReason { get; set; } = string.Empty;
    public int? RetryAfterMs { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class LeaseConstraints
{
    public int? MaxOutputTokensOverride { get; set; }
    public string ForcedModelId { get; set; } = string.Empty;
}

public sealed class ReleaseLeaseRequest
{
    public string LeaseId { get; set; } = string.Empty;
    public int ActualPromptTokens { get; set; }
    public int ActualOutputTokens { get; set; }
    public int ActualCostCents { get; set; }
    public int ToolCallsCount { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public LeaseOutcome Outcome { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ReleaseLeaseResponse
{
    public ReleaseClassification Classification { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
