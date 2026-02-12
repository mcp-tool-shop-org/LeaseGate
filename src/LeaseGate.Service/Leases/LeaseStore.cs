namespace LeaseGate.Service.Leases;

public sealed class LeaseStore
{
    private readonly Dictionary<string, LeaseRecord> _byLeaseId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _leaseIdByIdempotency = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void Add(LeaseRecord lease)
    {
        lock (_lock)
        {
            _byLeaseId[lease.LeaseId] = lease;
            _leaseIdByIdempotency[lease.IdempotencyKey] = lease.LeaseId;
        }
    }

    public LeaseRecord? GetByIdempotency(string idempotencyKey)
    {
        lock (_lock)
        {
            if (!_leaseIdByIdempotency.TryGetValue(idempotencyKey, out var leaseId))
            {
                return null;
            }

            _byLeaseId.TryGetValue(leaseId, out var lease);
            return lease;
        }
    }

    public LeaseRecord? Remove(string leaseId)
    {
        lock (_lock)
        {
            if (!_byLeaseId.Remove(leaseId, out var lease))
            {
                return null;
            }

            _leaseIdByIdempotency.Remove(lease.IdempotencyKey);
            return lease;
        }
    }

    public List<LeaseRecord> RemoveExpired(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            var expired = _byLeaseId.Values.Where(v => v.ExpiresAtUtc <= nowUtc).ToList();
            foreach (var lease in expired)
            {
                _byLeaseId.Remove(lease.LeaseId);
                _leaseIdByIdempotency.Remove(lease.IdempotencyKey);
            }

            return expired;
        }
    }
}
