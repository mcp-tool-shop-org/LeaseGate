using System.Security.Cryptography;
using System.Text;
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
    public int MaxRetrievedBytes { get; set; } = 1_000_000;
    public int MaxRetrievedTokens { get; set; } = 12_000;
    public int SummarizationTargetTokens { get; set; } = 1_000;
    public int MaxToolOutputTokens { get; set; } = 4_000;
    public int MaxToolCallsPerLease { get; set; } = 6;
    public int MaxComputeUnits { get; set; } = 8;
    public int RetryThresholdPerLease { get; set; } = 3;
    public int ToolLoopThreshold { get; set; } = 4;
    public int PolicyDenyCircuitBreakerThreshold { get; set; } = 5;
    public int SpendSpikeCents { get; set; } = 20;
    public int SafetyCooldownMs { get; set; } = 2_000;
    public int ClampedMaxOutputTokens { get; set; } = 64;
    public int OrgDailyBudgetCents { get; set; } = 0;
    public int OrgMaxRequestsPerMinute { get; set; } = 0;
    public int OrgMaxTokensPerMinute { get; set; } = 0;
    public Dictionary<string, int> WorkspaceDailyBudgetCents { get; set; } = new();
    public Dictionary<string, int> WorkspaceMaxRequestsPerMinute { get; set; } = new();
    public Dictionary<string, int> WorkspaceMaxTokensPerMinute { get; set; } = new();
    public Dictionary<string, int> ActorDailyBudgetCents { get; set; } = new();
    public int ActorMaxRequestsPerMinute { get; set; } = 0;
    public int ActorMaxTokensPerMinute { get; set; } = 0;
    public int MaxInFlightPerActor { get; set; } = 0;
    public Dictionary<Role, int> RoleMaxInFlightOverrides { get; set; } = new();
    public Dictionary<IntentClass, List<string>> IntentModelTiers { get; set; } = new();
    public Dictionary<IntentClass, int> IntentMaxCostCents { get; set; } = new();
    public List<string> AllowedModels { get; set; } = new();
    public Dictionary<ActionType, List<string>> AllowedCapabilities { get; set; } = new();
    public Dictionary<Role, Dictionary<ActionType, List<string>>> AllowedCapabilitiesByRole { get; set; } = new();
    public List<string> RiskRequiresApproval { get; set; } = new();
    public Dictionary<string, List<string>> AllowedToolsByActorWorkspace { get; set; } = new();
    public Dictionary<string, List<string>> AllowedModelsByWorkspace { get; set; } = new();
    public Dictionary<string, List<string>> AllowedToolsByWorkspaceRole { get; set; } = new();
    public List<ServiceAccountPolicy> ServiceAccounts { get; set; } = new();
    public List<ToolCategory> DeniedToolCategories { get; set; } = new();
    public List<ToolCategory> ApprovalRequiredToolCategories { get; set; } = new();
    public Dictionary<ToolCategory, int> ApprovalReviewersByToolCategory { get; set; } = new();
    public List<string> AllowedFileRoots { get; set; } = new();
    public List<string> AllowedNetworkHosts { get; set; } = new();
    public int DefaultToolTimeoutMs { get; set; } = 2_000;
    public long MaxToolOutputBytes { get; set; } = 16_384;
}

public sealed class ServiceAccountPolicy
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Plaintext token — only used for backward compatibility. Prefer <see cref="TokenHash"/>.</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>SHA-256 hex hash of the token. Use <see cref="HashToken"/> to generate.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Role Role { get; set; } = Role.ServiceAccount;
    public List<string> AllowedCapabilities { get; set; } = new();
    public List<string> AllowedModels { get; set; } = new();
    public List<string> AllowedTools { get; set; } = new();

    public static string HashToken(string plainToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken))).ToLowerInvariant();
    }
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
