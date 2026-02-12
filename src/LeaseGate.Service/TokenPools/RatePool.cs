namespace LeaseGate.Service.TokenPools;

public sealed class RatePool
{
    private readonly int _maxRequestsPerWindow;
    private readonly int _maxTokensPerWindow;
    private readonly TimeSpan _window;
    private readonly Queue<(DateTimeOffset TimestampUtc, int TokenCost)> _events = new();
    private readonly object _lock = new();

    public RatePool(int maxRequestsPerWindow, int maxTokensPerWindow, TimeSpan window)
    {
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _maxTokensPerWindow = maxTokensPerWindow;
        _window = window;
    }

    public bool TryAcquire(int tokenCost, out int retryAfterMs)
    {
        lock (_lock)
        {
            Prune(DateTimeOffset.UtcNow);
            var requestCount = _events.Count;
            var tokenSum = _events.Sum(x => x.TokenCost);

            if (requestCount + 1 > _maxRequestsPerWindow || tokenSum + tokenCost > _maxTokensPerWindow)
            {
                retryAfterMs = EstimateRetryAfterMs();
                return false;
            }

            _events.Enqueue((DateTimeOffset.UtcNow, Math.Max(0, tokenCost)));
            retryAfterMs = 0;
            return true;
        }
    }

    public double Utilization
    {
        get
        {
            lock (_lock)
            {
                Prune(DateTimeOffset.UtcNow);
                var requestUtil = _maxRequestsPerWindow == 0 ? 0 : (double)_events.Count / _maxRequestsPerWindow;
                var tokenUtil = _maxTokensPerWindow == 0 ? 0 : (double)_events.Sum(x => x.TokenCost) / _maxTokensPerWindow;
                return Math.Max(requestUtil, tokenUtil);
            }
        }
    }

    private int EstimateRetryAfterMs()
    {
        if (_events.Count == 0)
        {
            return 1000;
        }

        var oldest = _events.Peek().TimestampUtc;
        var wait = oldest.Add(_window) - DateTimeOffset.UtcNow;
        return Math.Max(200, (int)wait.TotalMilliseconds);
    }

    private void Prune(DateTimeOffset now)
    {
        while (_events.Count > 0 && now - _events.Peek().TimestampUtc >= _window)
        {
            _events.Dequeue();
        }
    }
}
