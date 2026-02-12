using LeaseGate.Protocol;

namespace LeaseGate.Service.ToolIsolation;

public sealed class ToolSubLeaseRecord
{
    public string ToolSubLeaseId { get; init; } = string.Empty;
    public string LeaseId { get; init; } = string.Empty;
    public string ToolId { get; init; } = string.Empty;
    public ToolCategory Category { get; init; }
    public int RemainingCalls { get; set; }
    public int TimeoutMs { get; init; }
    public long MaxOutputBytes { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed class ToolSubLeaseStore
{
    private readonly Dictionary<string, ToolSubLeaseRecord> _records = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ToolSubLeaseRecord Add(string leaseId, string toolId, ToolCategory category, int allowedCalls, int timeoutMs, long maxOutputBytes, DateTimeOffset expiresAtUtc)
    {
        var record = new ToolSubLeaseRecord
        {
            ToolSubLeaseId = Guid.NewGuid().ToString("N"),
            LeaseId = leaseId,
            ToolId = toolId,
            Category = category,
            RemainingCalls = Math.Max(1, allowedCalls),
            TimeoutMs = timeoutMs,
            MaxOutputBytes = maxOutputBytes,
            ExpiresAtUtc = expiresAtUtc
        };

        lock (_lock)
        {
            _records[record.ToolSubLeaseId] = record;
        }

        return record;
    }

    public bool TryConsume(string subLeaseId, string leaseId, string toolId, ToolCategory category, out ToolSubLeaseRecord? record, out string reason)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(subLeaseId, out record))
            {
                reason = "tool_sublease_not_found";
                return false;
            }

            if (!record.LeaseId.Equals(leaseId, StringComparison.Ordinal))
            {
                reason = "tool_sublease_lease_mismatch";
                return false;
            }

            if (!record.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase) || record.Category != category)
            {
                reason = "tool_sublease_scope_mismatch";
                return false;
            }

            if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                _records.Remove(subLeaseId);
                reason = "tool_sublease_expired";
                return false;
            }

            if (record.RemainingCalls <= 0)
            {
                reason = "tool_sublease_depleted";
                return false;
            }

            record.RemainingCalls--;
            if (record.RemainingCalls == 0)
            {
                _records.Remove(subLeaseId);
            }

            reason = string.Empty;
            return true;
        }
    }

    public void RemoveByLease(string leaseId)
    {
        lock (_lock)
        {
            var keys = _records.Where(p => p.Value.LeaseId.Equals(leaseId, StringComparison.Ordinal)).Select(p => p.Key).ToList();
            foreach (var key in keys)
            {
                _records.Remove(key);
            }
        }
    }
}
