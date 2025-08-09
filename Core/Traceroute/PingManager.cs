#nullable enable

namespace PingTestTool;

public class PingManager : ValidationBase, IPingManager
{
    private readonly IDnsManager _dnsManager;
    private readonly ConcurrentDictionary<string, HopData> _hops = new();
    private static readonly byte[] SharedBuffer = new byte[Constants.Ping.BufferSize];

    public PingManager(IDnsManager dnsManager)
    {
        ValidateNotNull(dnsManager, nameof(dnsManager));
        _dnsManager = dnsManager;
    }

    public async Task StartTraceAsync(
        string host,
        CancellationToken token,
        Action<string, int, string, HopData> updateUiCallback)
    {
        ValidateNotNullOrEmpty(host, nameof(host));
        ValidateNotNull(updateUiCallback, nameof(updateUiCallback));

        while (!token.IsCancellationRequested)
        {
            var (MaxTtl, Delay) = GetParameters();
            await ExecuteRoundAsync(host, MaxTtl, updateUiCallback, token).ConfigureAwait(false);
            await Task.Delay(Delay, token).ConfigureAwait(false);
        }
    }

    public void ClearHopData() => _hops.Clear();

    private (int MaxTtl, int Delay) GetParameters()
    {
        int totalSent = _hops.Values.Sum(h => h.Sent);
        int totalReceived = _hops.Values.Sum(h => h.Received);
        double loss = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;

        int delay =
            loss > Constants.Ping.HighLossThreshold ? Math.Min(Constants.Ping.Timeout, (int)(Constants.Ping.BaseDelay * 1.5)) :
            loss < Constants.Ping.LowLossThreshold ? Math.Max(Constants.Ping.MinDelay, (int)(Constants.Ping.BaseDelay * 0.75)) :
            Constants.Ping.BaseDelay;

        return (Constants.Ping.MaxTtl, delay);
    }

    private Task ExecuteRoundAsync(
        string host,
        int maxTtl,
        Action<string, int, string, HopData> updateUiCallback,
        CancellationToken token) =>
        ExecuteParallelAsync(Enumerable.Range(1, maxTtl).ToArray(), ttl =>
            ExecuteForTtlAsync(host, ttl, updateUiCallback, token));

    private Task ExecuteForTtlAsync(
        string host,
        int ttl,
        Action<string, int, string, HopData> updateUiCallback,
        CancellationToken token) =>
        ExecuteParallelAsync(Enumerable.Range(0, Constants.Ping.ParallelRequests).ToArray(), _ =>
            ExecuteSingleAsync(host, ttl, updateUiCallback, token));

    private static async Task ExecuteParallelAsync(int[] range, Func<int, Task> action)
    {
        var tasks = new Task[range.Length];
        for (int i = 0; i < range.Length; i++)
            tasks[i] = action(range[i]);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ExecuteSingleAsync(
        string host,
        int ttl,
        Action<string, int, string, HopData> updateUiCallback,
        CancellationToken token)
    {
        using var ping = new Ping();
        try
        {
            var (Reply, Elapsed) = await SendAsync(ping, host, ttl, token).ConfigureAwait(false);
            if (Reply != null)
                await ProcessReplyAsync(Reply, ttl, Elapsed, updateUiCallback, token).ConfigureAwait(false);
        }
        catch (PingException) { }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    private static async Task<(PingReply? Reply, long Elapsed)> SendAsync(Ping ping, string host, int ttl, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reply = await ping.SendPingAsync(
            host,
            Constants.Ping.Timeout,
            SharedBuffer,
            new PingOptions { Ttl = ttl }
        ).ConfigureAwait(false);
        sw.Stop();

        return (reply, sw.ElapsedMilliseconds);
    }

    private async Task ProcessReplyAsync(
        PingReply reply,
        int ttl,
        long elapsed,
        Action<string, int, string, HopData> updateUiCallback,
        CancellationToken token)
    {
        var ip = reply.Address != null ? reply.Address.ToString() : "Unknown address";
        if (string.IsNullOrWhiteSpace(ip) || ip.Trim() == "0.0.0.0")
            return;

        var hop = _hops.GetOrAdd(ip, _ => new HopData());
        UpdateHopStatistics(reply, hop, elapsed);

        string domain = await ResolveDomainAsync(ip, token).ConfigureAwait(false);
        updateUiCallback(ip, ttl, domain, hop);
    }

    private static void UpdateHopStatistics(PingReply reply, HopData hop, long elapsed)
    {
        lock (hop)
        {
            hop.Sent++;
            if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.TimeExceeded)
            {
                hop.Received++;
                hop.AddResponseTime(elapsed);
            }
        }
    }

    private async Task<string> ResolveDomainAsync(string ip, CancellationToken token)
    {
        try { return await _dnsManager.GetDomainNameAsync(ip, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return ip; }
        catch { return ip;}
    }
}