namespace LeaseGate.Protocol;

public sealed class AcquireLeaseRequest
{
    public string OrgId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public PrincipalType PrincipalType { get; set; } = PrincipalType.Human;
    public Role Role { get; set; } = Role.Member;
    public string AuthToken { get; set; } = string.Empty;
    public ActionType ActionType { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public int EstimatedPromptTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public int EstimatedCostCents { get; set; }
    public int RequestedContextTokens { get; set; }
    public int RequestedRetrievedChunks { get; set; }
    public int EstimatedToolOutputTokens { get; set; }
    public int EstimatedComputeUnits { get; set; } = 1;
    public List<string> RequestedCapabilities { get; set; } = new();
    public List<ToolIntent> RequestedTools { get; set; } = new();
    public List<string> RiskFlags { get; set; } = new();
    public string ApprovalToken { get; set; } = string.Empty;
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
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public PrincipalType PrincipalType { get; set; } = PrincipalType.Human;
    public Role Role { get; set; } = Role.Member;
}

public sealed class LeaseConstraints
{
    public int? MaxOutputTokensOverride { get; set; }
    public string ForcedModelId { get; set; } = string.Empty;
    public int? MaxToolCalls { get; set; }
    public int? MaxContextTokens { get; set; }
    public int? CooldownMs { get; set; }
}

public sealed class ToolIntent
{
    public string ToolId { get; set; } = string.Empty;
    public ToolCategory Category { get; set; }
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
    public long LatencyMs { get; set; }
    public ProviderErrorClassification ProviderErrorClassification { get; set; }
    public List<ToolCallUsage> ToolCalls { get; set; } = new();
    public LeaseOutcome Outcome { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ToolCallUsage
{
    public string ToolId { get; set; } = string.Empty;
    public string ToolSubLeaseId { get; set; } = string.Empty;
    public ToolCategory Category { get; set; }
    public long DurationMs { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public LeaseOutcome Outcome { get; set; } = LeaseOutcome.Success;
}

public sealed class ToolSubLeaseRequest
{
    public string LeaseId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public ToolCategory Category { get; set; }
    public int RequestedCalls { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2_000;
    public long MaxOutputBytes { get; set; } = 16_384;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ToolSubLeaseResponse
{
    public bool Granted { get; set; }
    public string ToolSubLeaseId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int AllowedCalls { get; set; }
    public int TimeoutMs { get; set; }
    public long MaxOutputBytes { get; set; }
    public string DeniedReason { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ToolExecutionRequest
{
    public string LeaseId { get; set; } = string.Empty;
    public string ToolSubLeaseId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public ToolCategory Category { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string TargetHost { get; set; } = string.Empty;
    public string CommandText { get; set; } = string.Empty;
    public int TimeoutMs { get; set; } = 1_000;
    public long MaxOutputBytes { get; set; } = 8_192;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ToolExecutionResponse
{
    public bool Allowed { get; set; }
    public LeaseOutcome Outcome { get; set; }
    public string DeniedReason { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public long OutputBytes { get; set; }
    public long DurationMs { get; set; }
    public string OutputPreview { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ReleaseLeaseResponse
{
    public ReleaseClassification Classification { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public LeaseReceipt? Receipt { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
}

public sealed class LeaseReceipt
{
    public string LeaseId { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public int ActualPromptTokens { get; set; }
    public int ActualOutputTokens { get; set; }
    public int ActualCostCents { get; set; }
    public LeaseOutcome Outcome { get; set; }
    public string AuditEntryHash { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class ApprovalRequest
{
    public string ActorId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public ToolCategory? ToolCategory { get; set; }
    public int TtlSeconds { get; set; } = 300;
    public bool SingleUse { get; set; } = true;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ApprovalRequestResponse
{
    public string ApprovalId { get; set; } = string.Empty;
    public ApprovalDecisionStatus Status { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GrantApprovalRequest
{
    public string ApprovalId { get; set; } = string.Empty;
    public string GrantedBy { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GrantApprovalResponse
{
    public bool Granted { get; set; }
    public string ApprovalToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DenyApprovalRequest
{
    public string ApprovalId { get; set; } = string.Empty;
    public string DeniedBy { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class DenyApprovalResponse
{
    public bool Denied { get; set; }
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class MetricsSnapshot
{
    public int ActiveLeases { get; set; }
    public int SpendTodayCents { get; set; }
    public double RatePoolUtilization { get; set; }
    public double ContextPoolUtilization { get; set; }
    public double ComputePoolUtilization { get; set; }
    public Dictionary<string, long> GrantsByReason { get; set; } = new();
    public Dictionary<string, long> DeniesByReason { get; set; } = new();
}

public sealed class GovernorStatusResponse
{
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public bool Healthy { get; set; }
    public bool DurableStateEnabled { get; set; }
    public string StateDatabasePath { get; set; } = string.Empty;
    public int ActiveLeases { get; set; }
    public int PendingApprovals { get; set; }
    public int SpendTodayCents { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
}

public sealed class ExportDiagnosticsRequest
{
    public string OutputPath { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ExportDiagnosticsResponse
{
    public bool Exported { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class PolicyBundle
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Author { get; set; } = string.Empty;
    public string PolicyContentJson { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
}

public sealed class StagePolicyBundleResponse
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
    public string StagedPolicyHash { get; set; } = string.Empty;
    public string StagedPolicyVersion { get; set; } = string.Empty;
}

public sealed class ActivatePolicyRequest
{
    public string Version { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ActivatePolicyResponse
{
    public bool Activated { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ActivePolicyHash { get; set; } = string.Empty;
    public string ActivePolicyVersion { get; set; } = string.Empty;
}
