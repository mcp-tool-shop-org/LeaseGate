using LeaseGate.Protocol;

namespace LeaseGate.Service.Leases;

public sealed class LeaseRecord
{
    public string LeaseId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public AcquireLeaseRequest Request { get; init; } = new();
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset AcquiredAtUtc { get; init; }
}
