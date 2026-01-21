using SysPing = System.Net.NetworkInformation.Ping;

namespace PingTestTool.Core;

public readonly record struct HopStats(long Min, long Max, double Avg, long Last, double Loss);

public sealed class HopData
{
    readonly Lock _lk = new();
    int _sent, _recv, _cnt;
    long _min = long.MaxValue, _max, _sum, _last;

    public int Sent { get { lock (_lk) return _sent; } }
    public int Received { get { lock (_lk) return _recv; } }

    public void Record(bool ok, int ms)
    {
        lock (_lk)
        {
            _sent++;
            if (!ok)
                return;

            _recv++;
            ms = Math.Max(ms, 0);
            _last = ms;
            _min = Math.Min(_min, ms);
            _max = Math.Max(_max, ms);
            _sum += ms;
            _cnt++;
        }
    }

    public HopStats GetStats()
    {
        lock (_lk)
        {
            if (_cnt == 0)
                return new(0, 0, 0, _last, _sent > 0 ? 100.0 : 0);

            double avg = (double)_sum / _cnt,
                   loss = _sent > 0 ? (_sent - _recv) * 100.0 / _sent : 0;
            return new(_min, _max, avg, _last, loss);
        }
    }

    public void Reset()
    {
        lock (_lk)
        {
            (_sent, _recv, _cnt) = (0, 0, 0);
            (_min, _max, _sum, _last) = (long.MaxValue, 0, 0, 0);
        }
    }
}

public sealed class TraceResult : INotifyPropertyChanged
{
    const string MsSuf = " ms", PctSuf = "%";

    int _nr;
    string _ip = "", _dom = "", _loss = "", _sent = "", _recv = "",
           _best = "", _avrg = "", _wrst = "", _last = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Nr { get => _nr; set => Set(ref _nr, value); }
    public string IPAddress { get => _ip; set => Set(ref _ip, value ?? ""); }
    public string DomainName { get => _dom; set => Set(ref _dom, value ?? ""); }
    public string Loss { get => _loss; set => Set(ref _loss, value ?? ""); }
    public string Sent { get => _sent; set => Set(ref _sent, value ?? ""); }
    public string Received { get => _recv; set => Set(ref _recv, value ?? ""); }
    public string Best { get => _best; set => Set(ref _best, value ?? ""); }
    public string Avrg { get => _avrg; set => Set(ref _avrg, value ?? ""); }
    public string Wrst { get => _wrst; set => Set(ref _wrst, value ?? ""); }
    public string Last { get => _last; set => Set(ref _last, value ?? ""); }

    public TraceResult(int ttl, string ip, string dom, HopData hop)
    {
        (_nr, _ip, _dom) = (ttl, ip ?? "", dom ?? "");
        UpdateStatistics(hop ?? throw new ArgumentNullException(nameof(hop)));
    }

    public void UpdateStatistics(HopData hop)
    {
        ArgumentNullException.ThrowIfNull(hop);
        var st = hop.GetStats();

        (Sent, Received) = (hop.Sent.ToString(), hop.Received.ToString());
        (Loss, Best, Wrst, Avrg, Last) = (
            $"{st.Loss:F0}{PctSuf}",
            $"{st.Min}{MsSuf}",
            $"{st.Max}{MsSuf}",
            $"{(long)st.Avg}{MsSuf}",
            $"{st.Last}{MsSuf}");
    }

    void Set<T>(ref T dst, T val, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(dst, val))
            return;

        dst = val;
        PropertyChanged?.Invoke(this, new(name));
    }

    public override string ToString() =>
        $"TTL: {Nr}, IP: {IPAddress}, Domain: {DomainName}, Loss: {Loss}, " +
        $"Sent: {Sent}, Recv: {Received}, Best: {Best}, Avg: {Avrg}, Wrst: {Wrst}, Last: {Last}";
}

public sealed class TraceManager : IDisposable
{
    const int BufSz = 32, MaxTtl = 30, TimeoutMs = 3000,
              InitDelayMs = 50, UpdDelayMs = 500, UiUpdMs = 1000;
    const string Unresolved = "---", UnknownIp = "*";

    static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(3);
    static readonly long UiUpdTicks = Stopwatch.Frequency * UiUpdMs / 1000;
    static readonly MemoryCacheEntryOptions DnsOkOpt = new()
    { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) };
    static readonly MemoryCacheEntryOptions DnsBadOpt = new()
    { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    readonly string?[] _route = new string?[MaxTtl + 1];
    readonly HopData?[] _hop = new HopData?[MaxTtl + 1];
    readonly SysPing?[] _ping = new SysPing?[MaxTtl + 1];
    readonly byte[]?[] _buf = new byte[]?[MaxTtl + 1];
    readonly PingOptions?[] _opt = new PingOptions?[MaxTtl + 1];
    readonly TraceResult?[] _res = new TraceResult?[MaxTtl + 1];
    readonly long[] _uiNext = new long[MaxTtl + 1];
    readonly Task[] _run = new Task[MaxTtl];
    readonly Lock _lock = new();

    readonly MemoryCache _dnsCache = new(new MemoryCacheOptions());
    readonly ConcurrentDictionary<string, byte> _dnsRun = new(StringComparer.OrdinalIgnoreCase);
    readonly LookupClient _dnsCli;
    readonly SemaphoreSlim _dnsSem = new(4, 4);

    CancellationTokenSource? _cts;
    volatile bool _disposed;
    int _maxTtl;

    public ObservableCollection<TraceResult> TraceResults { get; } = [];
    public string TraceUrl { get; }
    public bool IsTracing { get; private set; }

    public TraceManager(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        TraceUrl = url;
        _dnsCli = new(new LookupClientOptions
        {
            Timeout = DnsTimeout,
            Retries = 1,
            UseCache = false,
            ThrowDnsErrors = false
        });
    }

    public async Task StartTraceAsync(
        Action<string, Color> updStatus,
        Action<string, string, MessageBoxButton, MessageBoxImage> showMsg)
    {
        if (IsTracing)
        {
            showMsg(Res("TraceAlreadyRunning"), Res("WarningCaption"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsTracing = true;
        updStatus(Res("TraceStarted"), Colors.Green);
        _cts?.Dispose();
        _cts = new();

        try
        {
            await RunTrace(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            showMsg(string.Format(Res("TraceError"), ex.Message), Res("ErrorCaption"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTracing = false;
            updStatus(Res("TraceStopped"), Colors.Red);
        }
    }

    public void StopTrace() => _cts?.Cancel();

    public void ClearResults()
    {
        TraceResults.Clear();
        lock (_lock)
        {
            Array.Clear(_route);
            Array.Clear(_res);
            Array.Clear(_uiNext);
            _maxTtl = 0;
            for (int i = 1; i <= MaxTtl; i++)
                _hop[i]?.Reset();
        }
        _dnsCache.Compact(1.0);
        _dnsRun.Clear();
    }

    async Task RunTrace(CancellationToken ct)
    {
        byte[] buf = new byte[BufSz];
        using SysPing ping = new();
        await BuildRoute(ping, buf, ct).ConfigureAwait(false);
        await RunLoops(ct).ConfigureAwait(false);
    }

    async Task BuildRoute(SysPing ping, byte[] buf, CancellationToken ct)
    {
        PingOptions opts = new();
        for (int ttl = 1; ttl <= MaxTtl; ttl++)
        {
            ct.ThrowIfCancellationRequested();
            opts.Ttl = ttl;

            var (rep, ms) = await SendPing(ping, opts, buf, ct).ConfigureAwait(false);
            bool ok = rep is { Status: not IPStatus.TimedOut };
            var hop = GetHop(ttl);
            hop.Record(ok, ok ? ms : 0);

            string ip = ExtractIp(rep) ?? UnknownIp;
            lock (_lock)
            {
                _maxTtl = ttl;
                if (ip != UnknownIp)
                    _route[ttl] = ip;
            }

            string dom = ip != UnknownIp ? GetDomFast(ip, ct) : Unresolved;
            UpdUi(ttl, ip, dom, hop, true);

            if (rep?.Status == IPStatus.Success)
                break;

            await Task.Delay(InitDelayMs, ct).ConfigureAwait(false);
        }
    }

    async Task RunLoops(CancellationToken ct)
    {
        int max;
        lock (_lock) max = _maxTtl;

        if (max <= 0)
        {
            while (!ct.IsCancellationRequested)
                await Task.Delay(UpdDelayMs, ct).ConfigureAwait(false);
            return;
        }

        int cnt = Math.Min(max, MaxTtl);
        for (int i = 0; i < cnt; i++)
            _run[i] = RunTtl(i + 1, ct);
        for (int i = cnt; i < MaxTtl; i++)
            _run[i] = Task.CompletedTask;

        await Task.WhenAll(_run).ConfigureAwait(false);
    }

    async Task RunTtl(int ttl, CancellationToken ct)
    {
        SysPing ping;
        byte[] buf;
        PingOptions opt;
        HopData hop;
        string ipCur;

        lock (_lock)
        {
            InitTtl(ttl);
            (ping, buf, opt, hop) = (_ping[ttl]!, _buf[ttl]!, _opt[ttl]!, _hop[ttl]!);
            ipCur = _route[ttl] ?? UnknownIp;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (rep, ms) = await SendPing(ping, opt, buf, ct).ConfigureAwait(false);
                bool ok = rep is { Status: not IPStatus.TimedOut };
                hop.Record(ok, ok ? ms : 0);

                string ipNow = ExtractIp(rep) ?? ipCur;
                if (ipNow != UnknownIp && ipNow != ipCur)
                {
                    lock (_lock) _route[ttl] = ipNow;
                    ipCur = ipNow;
                    UpdUi(ttl, ipNow, Unresolved, hop, true);
                }

                string dom = ipCur != UnknownIp ? GetDomFast(ipCur, ct) : Unresolved;
                UpdUi(ttl, ipCur, dom, hop, false);
                await Task.Delay(UpdDelayMs, ct).ConfigureAwait(false);
            }
        }
        catch { }
    }

    void UpdUi(int ttl, string ip, string dom, HopData hop, bool force)
    {
        if ((uint)ttl > MaxTtl)
            return;

        if (!force)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _uiNext[ttl])
                return;
            _uiNext[ttl] = now + UiUpdTicks;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;

            var r = _res[ttl];
            if (r is null)
            {
                r = new(ttl, ip, dom, hop);
                _res[ttl] = r;
                TraceResults.Add(r);
                return;
            }

            if (r.IPAddress != ip)
                (r.IPAddress, r.DomainName) = (ip, Unresolved);

            if (dom != Unresolved && r.DomainName != dom)
                r.DomainName = dom;

            r.UpdateStatistics(hop);
        }, DispatcherPriority.Background);
    }

    HopData GetHop(int ttl)
    {
        ttl = Math.Clamp(ttl, 1, MaxTtl);
        lock (_lock) return _hop[ttl] ??= new();
    }

    void InitTtl(int ttl)
    {
        ttl = Math.Clamp(ttl, 1, MaxTtl);
        _hop[ttl] ??= new();
        _buf[ttl] ??= new byte[BufSz];
        _opt[ttl] ??= new() { Ttl = ttl };
        _ping[ttl] ??= new();
    }

    string GetDomFast(string ip, CancellationToken ct)
    {
        if (_dnsCache.TryGetValue(ip, out string? dom) && dom is not null)
            return dom;

        _dnsCache.Set(ip, Unresolved, DnsBadOpt);
        StartDns(ip, ct);
        return Unresolved;
    }

    void StartDns(string ip, CancellationToken ct)
    {
        if (_disposed || !_dnsRun.TryAdd(ip, 0))
            return;

        _ = ResolveDnsAsync(ip, ct);
    }

    async Task ResolveDnsAsync(string ip, CancellationToken ct)
    {
        string dom = Unresolved;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DnsTimeout);

            if (IPAddress.TryParse(ip, out var addr))
            {
                await _dnsSem.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    var resp = await _dnsCli.QueryReverseAsync(addr, cts.Token).ConfigureAwait(false);
                    foreach (var rr in resp.Answers)
                    {
                        if (rr is PtrRecord { PtrDomainName.Value: { } host } &&
                            !string.IsNullOrWhiteSpace(host))
                        {
                            dom = host.TrimEnd('.');
                            break;
                        }
                    }
                }
                finally
                {
                    _dnsSem.Release();
                }
            }
        }
        catch { }
        finally
        {
            _dnsRun.TryRemove(ip, out _);
        }

        _dnsCache.Set(ip, dom, dom == Unresolved ? DnsBadOpt : DnsOkOpt);

        if (_disposed)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_disposed)
                return;

            foreach (var r in TraceResults)
                if (r.IPAddress == ip)
                    r.DomainName = dom;
        }, DispatcherPriority.Background, ct);
    }

    async Task<(PingReply? Rep, int Ms)> SendPing(
        SysPing ping, PingOptions opts, byte[] buf, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var rep = await ping.SendPingAsync(TraceUrl, TimeoutMs, buf, opts).ConfigureAwait(false);
            return (rep, ElapsedMs(t0));
        }
        catch (PingException) { return (null, ElapsedMs(t0)); }
        catch (ObjectDisposedException) { return (null, ElapsedMs(t0)); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string? ExtractIp(PingReply? rep) =>
        rep?.Address?.ToString() is { Length: > 0 } ip && ip != "0.0.0.0" ? ip : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ElapsedMs(long t0) =>
        (int)((Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string Res(string k) => ResourceHelper.FindResourceString(k);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        for (int i = 1; i <= MaxTtl; i++)
            try { _ping[i]?.Dispose(); } catch { }

        _dnsCache.Dispose();
        _dnsSem.Dispose();
        GC.SuppressFinalize(this);
    }
}