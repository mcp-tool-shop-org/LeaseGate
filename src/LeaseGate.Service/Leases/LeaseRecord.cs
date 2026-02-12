using LeaseGate.Protocol;

namespace LeaseGate.Service.Leases;

public sealed class LeaseRecord
{
    public string LeaseId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public AcquireLeaseRequest Request { get; init; } = new();
    public LeaseConstraints Constraints { get; init; } = new();
    public List<ApprovalDecisionTrace> ApprovalChain { get; init; } = new();
    public int ReservedComputeUnits { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset AcquiredAtUtc { get; init; }
}
