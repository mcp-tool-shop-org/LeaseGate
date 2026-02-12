namespace LeaseGate.Service.TokenPools;

public sealed class ConcurrencyPool
{
    private readonly int _maxInFlight;
    private int _active;
    private readonly object _lock = new();

    public ConcurrencyPool(int maxInFlight)
    {
        _maxInFlight = maxInFlight;
    }

    public bool TryAcquire(out int retryAfterMs)
    {
        lock (_lock)
        {
            if (_active >= _maxInFlight)
            {
                retryAfterMs = 500;
                return false;
            }

            _active++;
            retryAfterMs = 0;
            return true;
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            if (_active > 0)
            {
                _active--;
            }
        }
    }

    public int Active
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }
}
