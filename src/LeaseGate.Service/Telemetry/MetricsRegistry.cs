namespace LeaseGate.Service.Telemetry;

public sealed class MetricsRegistry
{
    private readonly Dictionary<string, long> _grants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _denies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void RecordGrant(string reason)
    {
        lock (_lock)
        {
            _grants.TryGetValue(reason, out var value);
            _grants[reason] = value + 1;
        }
    }

    public void RecordDeny(string reason)
    {
        lock (_lock)
        {
            _denies.TryGetValue(reason, out var value);
            _denies[reason] = value + 1;
        }
    }

    public Dictionary<string, long> SnapshotGrants()
    {
        lock (_lock)
        {
            return _grants.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    public Dictionary<string, long> SnapshotDenies()
    {
        lock (_lock)
        {
            return _denies.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
