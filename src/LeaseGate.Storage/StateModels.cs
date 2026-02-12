namespace LeaseGate.Storage;

public sealed class StoredLease
{
    public string LeaseId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset AcquiredAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int ReservedComputeUnits { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string ConstraintsJson { get; set; } = string.Empty;
}

public sealed class StoredApproval
{
    public string ApprovalId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Token { get; set; } = string.Empty;
    public bool Used { get; set; }
    public string RequestJson { get; set; } = string.Empty;
}

public sealed class StoredRateEvent
{
    public DateTimeOffset TimestampUtc { get; set; }
    public int TokenCost { get; set; }
}

public sealed class StoredBudgetState
{
    public DateTime DateUtc { get; set; }
    public int ReservedCents { get; set; }
}

public sealed class StoredPolicyState
{
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
}

public sealed class DurableStateSnapshot
{
    public List<StoredLease> ActiveLeases { get; set; } = new();
    public List<StoredApproval> Approvals { get; set; } = new();
    public List<StoredRateEvent> RateEvents { get; set; } = new();
    public StoredBudgetState? BudgetState { get; set; }
    public StoredPolicyState? PolicyState { get; set; }
}
