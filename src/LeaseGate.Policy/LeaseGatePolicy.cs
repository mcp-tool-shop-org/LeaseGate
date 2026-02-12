using LeaseGate.Protocol;

namespace LeaseGate.Policy;

public sealed class LeaseGatePolicy
{
    public string PolicyVersion { get; set; } = "local";
    public int MaxInFlight { get; set; } = 4;
    public int DailyBudgetCents { get; set; } = 500;
    public int MaxRequestsPerMinute { get; set; } = 120;
    public int MaxTokensPerMinute { get; set; } = 250_000;
    public int MaxContextTokens { get; set; } = 16_000;
    public int MaxRetrievedChunks { get; set; } = 40;
    public int MaxToolOutputTokens { get; set; } = 4_000;
    public int MaxToolCallsPerLease { get; set; } = 6;
    public int MaxComputeUnits { get; set; } = 8;
    public List<string> AllowedModels { get; set; } = new();
    public Dictionary<ActionType, List<string>> AllowedCapabilities { get; set; } = new();
    public List<string> RiskRequiresApproval { get; set; } = new();
    public Dictionary<string, List<string>> AllowedToolsByActorWorkspace { get; set; } = new();
    public List<ToolCategory> DeniedToolCategories { get; set; } = new();
    public List<ToolCategory> ApprovalRequiredToolCategories { get; set; } = new();
    public List<string> AllowedFileRoots { get; set; } = new();
    public List<string> AllowedNetworkHosts { get; set; } = new();
    public int DefaultToolTimeoutMs { get; set; } = 2_000;
    public long MaxToolOutputBytes { get; set; } = 16_384;
}

public sealed class PolicyDecision
{
    public bool Allowed { get; init; }
    public string DeniedReason { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;

    public static PolicyDecision Allow() => new() { Allowed = true };

    public static PolicyDecision Deny(string reason, string recommendation) =>
        new()
        {
            Allowed = false,
            DeniedReason = reason,
            Recommendation = recommendation
        };
}
