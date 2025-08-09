#nullable enable

namespace PingTestTool;

public sealed class HopData
{
    private readonly ConcurrentQueue<long> _times = new();
    private readonly object _lock = new();
    private long _last;
    private (long Min, long Max, double Avg, long Last, double LossPercentage) _cached;
    private volatile bool _needUpdate = true;

    public int Sent { get; set; }
    public int Received { get; set; }

    public void AddResponseTime(long time)
    {
        if (time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        _times.Enqueue(time);
        Interlocked.Exchange(ref _last, time);
        _needUpdate = true;
    }

    public double CalculateLossPercentage() =>
        Sent == 0 ? 0 : (double)(Sent - Received) / Sent * 100;

    public (long Min, long Max, double Avg, long Last, double LossPercentage) GetStatistics()
    {
        if (_times.IsEmpty) return (0, 0, 0, 0, 0);
        if (!_needUpdate) return _cached;

        lock (_lock)
        {
            if (!_needUpdate) return _cached;

            var arr = _times.ToArray();
            _cached = (arr.Min(), arr.Max(), arr.Average(), _last, CalculateLossPercentage());
            _needUpdate = false;
            return _cached;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            while (_times.TryDequeue(out _)) { }
            _last = 0;
            _needUpdate = true;
            _cached = default;
            Sent = 0;
            Received = 0;
        }
    }
}