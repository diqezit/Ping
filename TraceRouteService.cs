﻿#nullable enable

namespace PingTestTool
{
    #region Constants

    public static class Constants
    {
        public const string DefaultUnresolvedValue = "---",
                            MsUnitSuffix = " ms",
                            PercentageSuffix = "%",
                            DefaultFormat = "F0";

        public static class Ping
        {
            public const int BufferSize = 32,
                             MaxTtl = 12,
                             Timeout = 5000,
                             ParallelRequests = 1,
                             BaseDelay = 1000,
                             MinDelay = 100;
            public const double HighLossThreshold = 50,
                                LowLossThreshold = 10;
        }
    }

    #endregion

    #region Interfaces

    public interface IDnsManager
    {
        Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token);
    }

    public interface IPingManager
    {
        Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback);
        void ClearHopData();
    }

    public interface ITraceWindow
    {
        bool IsLoaded { get; }
        bool IsVisible { get; }
        Visibility Visibility { get; set; }
        void Show();
        void Close();
        event EventHandler Closed;
    }

    #endregion

    #region Base Classes

    public abstract class ObservableBase : INotifyPropertyChanged
    {
        private readonly ConcurrentDictionary<string, object> _values = new();
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(T value, [CallerMemberName] string? name = null)
        {
            if (name == null) return false;

            var old = _values.GetOrAdd(name, default(T)!);
            if (EqualityComparer<T>.Default.Equals((T)old, value))
                return false;

            _values[name] = value!;
            OnPropertyChanged(name);
            return true;
        }

        protected T GetProperty<T>(T defaultValue = default!, [CallerMemberName] string? name = null) =>
            name == null ? defaultValue : (T)_values.GetOrAdd(name, defaultValue!);
    }

    public abstract class ValidationBase
    {
        protected static void ValidateNotNull<T>(T value, string paramName) where T : class =>
            _ = value ?? throw new ArgumentNullException(paramName);

        protected static void ValidateNotNullOrEmpty(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }
    }

    #endregion

    #region Data Models

    public record NetworkSettings(TimeSpan DnsTimeout, MemoryCacheEntryOptions CacheOptions);

    public class TraceResult : ObservableBase
    {
        public int Nr { get => GetProperty(0); set => SetProperty(value); }
        public string IPAddress { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string DomainName { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Loss { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Sent { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Received { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Best { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Avrg { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Wrst { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
        public string Last { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }

        public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
        {
            ValidateHop(hop);
            Nr = ttl;
            IPAddress = ipAddress;
            DomainName = domainName;
            UpdateStatistics(hop);
        }

        public void UpdateStatistics(HopData hop)
        {
            ValidateHop(hop);
            var stats = hop.GetStatistics();
            Sent = hop.Sent.ToString();
            Received = hop.Received.ToString();
            Loss = $"{stats.LossPercentage.ToString(Constants.DefaultFormat)}{Constants.PercentageSuffix}";
            Best = FormatMs(stats.Min);
            Wrst = FormatMs(stats.Max);
            Avrg = FormatMs((long)stats.Avg);
            Last = FormatMs(stats.Last);
        }

        private static string FormatMs(long ms) => $"{ms}{Constants.MsUnitSuffix}";

        private static void ValidateHop(HopData hop) =>
            _ = hop ?? throw new ArgumentNullException(nameof(hop));

        public override string ToString() =>
            $"TTL: {Nr}, IP: {IPAddress}, Domain: {DomainName}, Loss: {Loss}, Sent: {Sent}, Received: {Received}, " +
            $"Best: {Best}, Avg: {Avrg}, Worst: {Wrst}, Last: {Last}";
    }

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
                _cached = (
                    arr.Min(),
                    arr.Max(),
                    arr.Average(),
                    _last,
                    CalculateLossPercentage()
                );
                _needUpdate = false;
                return _cached;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                // ConcurrentQueue does not have a Clear method.
                while (_times.TryDequeue(out _)) { }
                _last = 0;
                _needUpdate = true;
                _cached = default;
                Sent = 0;
                Received = 0;
            }
        }
    }

    #endregion

    #region Managers

    public class DnsManager : ValidationBase, IDnsManager
    {
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _dnsTimeout;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public DnsManager(IMemoryCache memoryCache, TimeSpan? dnsTimeout = null)
        {
            ValidateNotNull(memoryCache, nameof(memoryCache));
            _cache = memoryCache;
            _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                .SetSlidingExpiration(TimeSpan.FromMinutes(10));
        }

        public DnsManager(IMemoryCache memoryCache, NetworkSettings settings)
        {
            ValidateNotNull(memoryCache, nameof(memoryCache));
            ValidateNotNull(settings, nameof(settings));
            _cache = memoryCache;
            _dnsTimeout = settings.DnsTimeout;
            _cacheOptions = settings.CacheOptions;
        }

        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            ValidateNotNullOrEmpty(ipAddress, nameof(ipAddress));

            if (!IPAddress.TryParse(ipAddress, out var parsed))
                throw new ArgumentException("Incorrect IP address", nameof(ipAddress));

            return _cache.TryGetValue(ipAddress, out string? cached)
                ? cached ?? Constants.DefaultUnresolvedValue
                : await ResolveAsync(ipAddress, parsed, token);
        }

        private async Task<string> ResolveAsync(string ipAddress, IPAddress parsed, CancellationToken token)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(_dnsTimeout);

                var entry = await Dns.GetHostEntryAsync(parsed);
                string result = entry.HostName;
                _cache.Set(ipAddress, result, _cacheOptions);
                return result;
            }
            catch (Exception)
            {
                _cache.Set(ipAddress, Constants.DefaultUnresolvedValue, _cacheOptions);
                return Constants.DefaultUnresolvedValue;
            }
        }
    }

    public class PingManager : ValidationBase, IPingManager
    {
        private readonly IDnsManager _dnsManager;
        private readonly ConcurrentDictionary<string, HopData> _hops = new();
        private readonly byte[] _buffer = new byte[Constants.Ping.BufferSize];

        public PingManager(IDnsManager dnsManager)
        {
            ValidateNotNull(dnsManager, nameof(dnsManager));
            _dnsManager = dnsManager;
        }

        public async Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            ValidateNotNullOrEmpty(host, nameof(host));
            ValidateNotNull(updateUiCallback, nameof(updateUiCallback));

            while (!token.IsCancellationRequested)
            {
                var (maxTtl, delay) = GetParameters();
                await ExecuteRoundAsync(host, maxTtl, updateUiCallback, token);
                await Task.Delay(delay, token);
            }
        }

        public void ClearHopData() => _hops.Clear();

        private (int MaxTtl, int Delay) GetParameters()
        {
            int totalSent = _hops.Values.Sum(h => h.Sent);
            int totalReceived = _hops.Values.Sum(h => h.Received);
            double loss = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;

            int delay = loss switch
            {
                > Constants.Ping.HighLossThreshold => Math.Min(Constants.Ping.Timeout, (int)(Constants.Ping.BaseDelay * 1.5)),
                < Constants.Ping.LowLossThreshold => Math.Max(Constants.Ping.MinDelay, (int)(Constants.Ping.BaseDelay * 0.75)),
                _ => Constants.Ping.BaseDelay
            };

            return (Constants.Ping.MaxTtl, delay);
        }
        
        private async Task ExecuteRoundAsync(string host, int maxTtl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            await ExecuteParallelAsync(Enumerable.Range(1, maxTtl),
                ttl => ExecuteForTtlAsync(host, ttl, updateUiCallback, token));
        }

        private async Task ExecuteForTtlAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            await ExecuteParallelAsync(Enumerable.Range(0, Constants.Ping.ParallelRequests),
                _ => ExecuteSingleAsync(host, ttl, updateUiCallback, token));
        }

        private async Task ExecuteParallelAsync(IEnumerable<int> range, Func<int, Task> action)
        {
            var tasks = range.Select(action);
            await Task.WhenAll(tasks);
        }

        private async Task ExecuteSingleAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            using var ping = new Ping();
            try
            {
                var (reply, elapsed) = await SendAsync(ping, host, ttl, token);
                if (reply != null)
                    await ProcessReplyAsync(reply, ttl, elapsed, updateUiCallback, token);
            }
            catch (PingException) { /* Ignored - common ping errors */ }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { /* Ignored - other non-cancellation errors */ }
        }

        private static async Task<(PingReply? Reply, long Elapsed)> SendAsync(Ping ping, string host, int ttl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(
                host,
                Constants.Ping.Timeout,
                new byte[Constants.Ping.BufferSize],
                new PingOptions { Ttl = ttl }
            );
            sw.Stop();

            return (reply, sw.ElapsedMilliseconds);
        }

        private async Task ProcessReplyAsync(PingReply reply, int ttl, long elapsed, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var ip = reply.Address?.ToString() ?? "Unknown address";
            if (string.IsNullOrWhiteSpace(ip) || ip.Trim() == "0.0.0.0")
                return;

            var hop = _hops.GetOrAdd(ip, _ => new HopData());
            UpdateHopStatistics(reply, hop, elapsed);

            string domain = await ResolveDomainAsync(ip, token);
            updateUiCallback(ip, ttl, domain, hop);
        }

        private static void UpdateHopStatistics(PingReply reply, HopData hop, long elapsed)
        {
            lock (hop)
            {
                hop.Sent++;
                if (reply.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded)
                {
                    hop.Received++;
                    hop.AddResponseTime(elapsed);
                }
            }
        }

        private async Task<string> ResolveDomainAsync(string ip, CancellationToken token)
        {
            try
            {
                return await _dnsManager.GetDomainNameAsync(ip, token);
            }
            catch (OperationCanceledException)
            {
                return ip;
            }
            catch
            {
                return ip;
            }
        }
    }

    public class TraceManager : ValidationBase, IDisposable
    {
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly IPingManager _pingManager;
        private readonly ObservableCollection<TraceResult> _results;
        private readonly IMemoryCache _memoryCache;
        private bool _isTracing;

        public ObservableCollection<TraceResult> TraceResults => _results;
        public string TraceUrl { get; }
        public bool IsTracing { get => _isTracing; private set => _isTracing = value; }

        public TraceManager(string url)
        {
            ValidateNotNullOrEmpty(url, nameof(url));
            TraceUrl = url;
            _results = new ObservableCollection<TraceResult>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _pingManager = new PingManager(new DnsManager(_memoryCache));
        }

        public async Task StartTraceAsync(Action<string, Color> updateStatus, Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
        {
            if (IsTracing)
            {
                showMessage(
                    ResourceHelper.FindResourceString("TraceAlreadyRunning"),
                    ResourceHelper.FindResourceString("WarningCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            IsTracing = true;
            updateStatus(ResourceHelper.FindResourceString("TraceStarted"), Colors.Green);

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                await _pingManager.StartTraceAsync(TraceUrl, _cts.Token, UpdateResult);
            }
            catch (OperationCanceledException)
            {
                /* Ignored cancellation */
            }
            catch (Exception ex)
            {
                showMessage(
                    string.Format(ResourceHelper.FindResourceString("TraceError"), ex.Message),
                    ResourceHelper.FindResourceString("ErrorCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsTracing = false;
                updateStatus(ResourceHelper.FindResourceString("TraceStopped"), Colors.Red);
            }
        }

        public void StopTrace() => _cts?.Cancel();

        public void ClearResults()
        {
            _results.Clear();
            _pingManager.ClearHopData();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _cts?.Dispose();
                _memoryCache.Dispose();
                _disposed = true;
            }
        }

        ~TraceManager() => Dispose(false);

        private void UpdateResult(string ip, int ttl, string domain, HopData hop)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = _results.FirstOrDefault(r => r.IPAddress == ip);
                if (existing == null)
                    _results.Add(new TraceResult(ttl, ip, domain, hop));
                else
                    existing.UpdateStatistics(hop);
            });
        }

        private static string FindResourceStringStatic(string resourceKey) =>
            Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }

    #endregion
}