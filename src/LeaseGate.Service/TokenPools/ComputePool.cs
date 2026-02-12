namespace LeaseGate.Service.TokenPools;

public sealed class ComputePool
{
    private readonly int _maxUnits;
    private int _activeUnits;
    private readonly object _lock = new();

    public ComputePool(int maxUnits)
    {
        _maxUnits = maxUnits;
    }

    public bool TryAcquire(int units, out int retryAfterMs)
    {
        lock (_lock)
        {
            var requested = Math.Max(1, units);
            if (_activeUnits + requested > _maxUnits)
            {
                retryAfterMs = 300;
                return false;
            }

            _activeUnits += requested;
            retryAfterMs = 0;
            return true;
        }
    }

    public void Release(int units)
    {
        lock (_lock)
        {
            _activeUnits -= Math.Max(1, units);
            if (_activeUnits < 0)
            {
                _activeUnits = 0;
            }
        }
    }

    public double Utilization
    {
        get
        {
            lock (_lock)
            {
                if (_maxUnits == 0)
                {
                    return 0;
                }

                return Math.Clamp((double)_activeUnits / _maxUnits, 0, 1);
            }
        }
    }
}
