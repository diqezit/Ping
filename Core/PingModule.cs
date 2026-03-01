namespace PingTestTool.Core;

public readonly record struct PingConfiguration(
    string Url,
    int PingCount,
    int Timeout,
    bool DontFragment = true);

public sealed class PingService : IDisposable
{
    const int BufSz = 32;
    const string DtFmt = "dd.MM.yyyy HH:mm:ss",
                 Sep = "══════════════════════════════════════════════════════════",
                 SepMini = "──────────────────────────────────────";

    readonly Lock _lk = new();
    readonly List<(DateTime T, int Rtt)> _data = new(256);
    bool _disposed;

    public event Action<string>? OnPingResult;
    public event Action<int, int>? OnProgressUpdate;
    public event Action<DateTime, int>? OnRoundtripTimeAdded;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public Task ClearRoundtripTimesAsync(CancellationToken _ = default)
    {
        lock (_lk) _data.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(DateTime, int)>> GetRoundtripTimesAsync(CancellationToken _ = default)
    {
        lock (_lk) return Task.FromResult<IReadOnlyList<(DateTime, int)>>([.. _data]);
    }

    public async Task StartPingTestAsync(PingConfiguration cfg, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var t0 = DateTime.Now;
        var opts = new PingOptions { DontFragment = cfg.DontFragment };
        var buf = new byte[BufSz];
        using var ping = new SysPing();

        int ok = 0, fail = 0;
        EmitHeader(cfg, t0);

        for (int i = 0; i < cfg.PingCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            long sw = Stopwatch.GetTimestamp();

            try
            {
                var rep = await ping.SendPingAsync(cfg.Url, cfg.Timeout, buf, opts).ConfigureAwait(false);
                int ms = ElapsedMs(sw);

                if (rep.Status == IPStatus.Success)
                {
                    int rtt = (int)rep.RoundtripTime;
                    var now = DateTime.Now;

                    lock (_lk) _data.Add((now, rtt));
                    ok++;

                    OnRoundtripTimeAdded?.Invoke(now, rtt);
                    OnPingResult?.Invoke(
                        $"[{now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {S("ReplyFrom")} {cfg.Url}:\n" +
                        $"  {S("Time")}: {rep.RoundtripTime,4} {S("Ms")}\n" +
                        $"  TTL:  {rep.Options?.Ttl ?? 0}\n" +
                        $"  {S("Size")}: {rep.Buffer?.Length ?? 0} {S("Bytes")}\n");
                }
                else
                {
                    fail++;
                    OnPingResult?.Invoke(
                        $"[{DateTime.Now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {S("PingError")} {cfg.Url}:\n" +
                        $"  {S("Status")}: {rep.Status}\n");
                }

                OnProgressUpdate?.Invoke(i + 1, cfg.PingCount);

                int delay = cfg.Timeout - ms;
                if (delay > 0)
                    await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (PingException ex)
            {
                fail++;
                OnPingResult?.Invoke(
                    $"[{DateTime.Now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {S("CriticalPingError")}: {ex.Message}\n");
                OnProgressUpdate?.Invoke(i + 1, cfg.PingCount);
            }
        }

        EmitFooter(t0, ok, fail);
    }

    void EmitHeader(PingConfiguration cfg, DateTime t0) =>
        OnPingResult?.Invoke(
            $"{Sep}\n  {S("PingTest")}\n{Sep}\n" +
            $"{S("StartTime")}:    {t0.ToString(DtFmt)}\n" +
            $"{S("Host")}:      {cfg.Url}\n" +
            $"{S("PingCount")}:   {cfg.PingCount}\n" +
            $"{S("Timeout")}:     {cfg.Timeout} {S("Ms")}\n" +
            $"{S("DontFragment")}: {(cfg.DontFragment ? S("Yes") : S("No"))}\n{Sep}\n");

    void EmitFooter(DateTime t0, int ok, int fail)
    {
        var t1 = DateTime.Now;
        var dur = t1 - t0;
        var (min, max, avg) = CalcStats();
        double jitter = CalcJitter();
        int total = ok + fail;
        string loss = total > 0 ? $"{fail * 100.0 / total:F2}" : "0.00";

        OnPingResult?.Invoke(
            $"\n{Sep}\n  {S("TestingResults")}\n{Sep}\n" +
            $"{S("StartTime")}:    {t0.ToString(DtFmt)}\n" +
            $"{S("EndTime")}:      {t1.ToString(DtFmt)}\n" +
            $"{S("Duration")}:      {FmtDur(dur)}\n" +
            $"{SepMini}\n{S("PacketStatistics")}:\n" +
            $"    {S("PacketsSent")}: {total}\n" +
            $"    {S("Successful")}:     {ok}\n" +
            $"    {S("Lost")}:       {fail} ({loss}%)\n" +
            $"{SepMini}\n{S("TimeStatistics")}:\n" +
            $"    {S("Minimum")}:      {min} {S("Ms")}\n" +
            $"    {S("Maximum")}:      {max} {S("Ms")}\n" +
            $"    {S("Average")}:        {avg:F2} {S("Ms")}\n" +
            $"    {S("Jitter")}:         {jitter:F2} {S("Ms")}\n{Sep}\n");
    }

    static string FmtDur(TimeSpan t) => t.TotalHours >= 1
        ? $"{t.TotalHours:F2} {S("Hours")}"
        : t.TotalMinutes >= 1
            ? $"{t.TotalMinutes:F2} {S("Minutes")}"
            : $"{t.TotalSeconds:F2} {S("Seconds")}";

    (int Min, int Max, double Avg) CalcStats()
    {
        lock (_lk)
        {
            if (_data.Count == 0) return (0, 0, 0);
            int min = int.MaxValue, max = 0;
            long sum = 0;
            foreach (var (_, rtt) in _data)
            {
                min = Math.Min(min, rtt);
                max = Math.Max(max, rtt);
                sum += rtt;
            }
            return (min, max, (double)sum / _data.Count);
        }
    }

    double CalcJitter()
    {
        lock (_lk)
        {
            if (_data.Count <= 1) return 0;
            double sum = 0;
            for (int i = 1; i < _data.Count; i++)
                sum += Math.Abs(_data[i].Rtt - _data[i - 1].Rtt);
            return Math.Round(sum / (_data.Count - 1), 2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ElapsedMs(long t0) =>
        (int)((Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string S(string k) => Strings.Get(k);
}