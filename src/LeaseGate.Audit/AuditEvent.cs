using LeaseGate.Protocol;

namespace LeaseGate.Audit;

public sealed class AuditEvent
{
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ProtocolVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public PrincipalType PrincipalType { get; set; } = PrincipalType.Human;
    public Role Role { get; set; } = Role.Member;
    public string ActionType { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int EstimatedCostCents { get; set; }
    public int ActualCostCents { get; set; }
    public List<string> RequestedTools { get; set; } = new();
    public List<string> ToolUsageSummary { get; set; } = new();
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
}
