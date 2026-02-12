namespace LeaseGate.Storage;

public interface ILeaseGateStateStore
{
    DurableStateSnapshot Load();
    void UpsertLease(StoredLease lease);
    void RemoveLease(string leaseId);
    void ReplaceApprovals(IEnumerable<StoredApproval> approvals);
    void ReplaceRateEvents(IEnumerable<StoredRateEvent> events);
    void SaveBudgetState(StoredBudgetState state);
    void SavePolicyState(StoredPolicyState state);
}
