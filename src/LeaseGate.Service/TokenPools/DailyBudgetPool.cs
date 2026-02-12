namespace LeaseGate.Service.TokenPools;

public sealed class DailyBudgetPool
{
    private readonly int _centsPerDay;
    private DateTime _currentDateUtc;
    private int _reservedCents;
    private readonly object _lock = new();

    public DailyBudgetPool(int centsPerDay)
    {
        _centsPerDay = centsPerDay;
        _currentDateUtc = DateTime.UtcNow.Date;
    }

    public bool TryReserve(int estimatedCostCents, out int retryAfterMs)
    {
        lock (_lock)
        {
            RollDateIfNeeded();

            if (_reservedCents + estimatedCostCents > _centsPerDay)
            {
                retryAfterMs = GetRetryAfterMs();
                return false;
            }

            _reservedCents += estimatedCostCents;
            retryAfterMs = 0;
            return true;
        }
    }

    public void Settle(int estimatedCostCents, int actualCostCents)
    {
        lock (_lock)
        {
            RollDateIfNeeded();
            _reservedCents -= estimatedCostCents;
            if (_reservedCents < 0)
            {
                _reservedCents = 0;
            }

            _reservedCents += actualCostCents;
            if (_reservedCents < 0)
            {
                _reservedCents = 0;
            }
        }
    }

    public void ReleaseReservation(int estimatedCostCents)
    {
        lock (_lock)
        {
            RollDateIfNeeded();
            _reservedCents -= estimatedCostCents;
            if (_reservedCents < 0)
            {
                _reservedCents = 0;
            }
        }
    }

    public int ReservedCents
    {
        get
        {
            lock (_lock)
            {
                RollDateIfNeeded();
                return _reservedCents;
            }
        }
    }

    public DateTime CurrentDateUtc
    {
        get
        {
            lock (_lock)
            {
                RollDateIfNeeded();
                return _currentDateUtc;
            }
        }
    }

    public void RestoreState(DateTime dateUtc, int reservedCents)
    {
        lock (_lock)
        {
            _currentDateUtc = dateUtc.Date;
            _reservedCents = Math.Max(0, reservedCents);
            RollDateIfNeeded();
        }
    }

    private void RollDateIfNeeded()
    {
        var nowDate = DateTime.UtcNow.Date;
        if (nowDate != _currentDateUtc)
        {
            _currentDateUtc = nowDate;
            _reservedCents = 0;
        }
    }

    private static int GetRetryAfterMs()
    {
        var nextUtcMidnight = DateTime.UtcNow.Date.AddDays(1);
        var delay = nextUtcMidnight - DateTime.UtcNow;
        return Math.Max(1000, (int)delay.TotalMilliseconds);
    }
}
