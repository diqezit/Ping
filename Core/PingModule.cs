using SysPing = System.Net.NetworkInformation.Ping;

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
        if (_disposed)
            return;
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
                        $"[{now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {Res("ReplyFrom")} {cfg.Url}:\n" +
                        $"  {Res("Time")}: {rep.RoundtripTime,4} {Res("Ms")}\n" +
                        $"  TTL:  {rep.Options?.Ttl ?? 0}\n" +
                        $"  {Res("Size")}: {rep.Buffer?.Length ?? 0} {Res("Bytes")}\n");
                }
                else
                {
                    fail++;
                    OnPingResult?.Invoke(
                        $"[{DateTime.Now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {Res("PingError")} {cfg.Url}:\n" +
                        $"  {Res("Status")}: {rep.Status}\n");
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
                    $"[{DateTime.Now:HH:mm:ss}] [{i + 1}/{cfg.PingCount}] {Res("CriticalPingError")}: {ex.Message}\n");
                OnProgressUpdate?.Invoke(i + 1, cfg.PingCount);
            }
        }

        EmitFooter(t0, ok, fail);
    }

    void EmitHeader(PingConfiguration cfg, DateTime t0) =>
        OnPingResult?.Invoke(
            $"{Sep}\n  {Res("PingTest")}\n{Sep}\n" +
            $"{Res("StartTime")}:    {t0.ToString(DtFmt)}\n" +
            $"{Res("Host")}:      {cfg.Url}\n" +
            $"{Res("PingCount")}:   {cfg.PingCount}\n" +
            $"{Res("Timeout")}:     {cfg.Timeout} {Res("Ms")}\n" +
            $"{Res("DontFragment")}: {(cfg.DontFragment ? Res("Yes") : Res("No"))}\n{Sep}\n");

    void EmitFooter(DateTime t0, int ok, int fail)
    {
        var t1 = DateTime.Now;
        var dur = t1 - t0;
        var (min, max, avg) = CalcStats();
        double jitter = CalcJitter();
        int total = ok + fail;
        string loss = total > 0 ? $"{fail * 100.0 / total:F2}" : "0.00";

        OnPingResult?.Invoke(
            $"\n{Sep}\n  {Res("TestingResults")}\n{Sep}\n" +
            $"{Res("StartTime")}:    {t0.ToString(DtFmt)}\n" +
            $"{Res("EndTime")}:      {t1.ToString(DtFmt)}\n" +
            $"{Res("Duration")}:      {FmtDur(dur)}\n" +
            $"{SepMini}\n{Res("PacketStatistics")}:\n" +
            $"    {Res("PacketsSent")}: {total}\n" +
            $"    {Res("Successful")}:     {ok}\n" +
            $"    {Res("Lost")}:       {fail} ({loss}%)\n" +
            $"{SepMini}\n{Res("TimeStatistics")}:\n" +
            $"    {Res("Minimum")}:      {min} {Res("Ms")}\n" +
            $"    {Res("Maximum")}:      {max} {Res("Ms")}\n" +
            $"    {Res("Average")}:        {avg:F2} {Res("Ms")}\n" +
            $"    {Res("Jitter")}:         {jitter:F2} {Res("Ms")}\n{Sep}\n");
    }

    static string FmtDur(TimeSpan t) => t.TotalHours >= 1
        ? $"{t.TotalHours:F2} {Res("Hours")}"
        : t.TotalMinutes >= 1
            ? $"{t.TotalMinutes:F2} {Res("Minutes")}"
            : $"{t.TotalSeconds:F2} {Res("Seconds")}";

    (int Min, int Max, double Avg) CalcStats()
    {
        lock (_lk)
        {
            if (_data.Count == 0)
                return (0, 0, 0);

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
            if (_data.Count <= 1)
                return 0;

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
    static string Res(string k) => ResourceHelper.FindResourceString(k);
}