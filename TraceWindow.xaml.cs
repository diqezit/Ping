#nullable enable

namespace PingTestTool
{
    // -------------------- Constants --------------------
    public static class Constants
    {
        public const string DefaultUnresolvedValue = "---";
        public const string MsUnitSuffix = " ms";
        public const string PercentageSuffix = "%";
        public const string DefaultFormat = "F0";

        public static class Ping
        {
            public const int BufferSize = 32;
            public const int MaxTtl = 12;
            public const int Timeout = 5000;
            public const int ParallelRequests = 1;
            public const int BaseDelay = 1000;
            public const int MinDelay = 100;
            public const double HighLossThreshold = 50;
            public const double LowLossThreshold = 10;
        }

        public static class Network
        {
            public const byte PrivateNetworkAFirstByte = 10;
            public const byte PrivateNetworkBFirstByte = 172;
            public const byte PrivateNetworkBSecondByteStart = 16;
            public const byte PrivateNetworkBSecondByteEnd = 31;
            public const byte PrivateNetworkCFirstByte = 192;
            public const byte PrivateNetworkCSecondByte = 168;
        }
    }

    // -------------------- Interfaces --------------------
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

    // -------------------- Models --------------------
    public abstract class ObservableBase : INotifyPropertyChanged
    {
        private readonly ConcurrentDictionary<string, object> _propertyValues = new();
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(T value, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return false;

            var oldValue = _propertyValues.GetOrAdd(propertyName, default(T)!);
            if (!EqualityComparer<T>.Default.Equals((T)oldValue, value))
            {
                _propertyValues[propertyName] = value!;
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }

        protected T GetProperty<T>(T defaultValue = default!, [CallerMemberName] string? propertyName = null) =>
            propertyName == null ? defaultValue :
            (T)_propertyValues.GetOrAdd(propertyName, defaultValue!);
    }

    public abstract class ValidationBase
    {
        protected static void ValidateNotNull<T>(T value, string paramName, ILoggingService logger) where T : class
        {
            if (value == null)
            {
                logger.Error($"[{typeof(T).Name}] {paramName} не может быть null.");
                throw new ArgumentNullException(paramName);
            }
        }

        protected static void ValidateNotNullOrEmpty(string value, string paramName, ILoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                logger.Error($"[{typeof(ValidationBase).Name}] {paramName} не может быть пустым.");
                throw new ArgumentException($"{paramName} не может быть пустым.", paramName);
            }
        }
    }

    public class TraceResult : ObservableBase
    {

        private readonly ILoggingService _logger;

        public int Nr
        {
            get => GetProperty(0);
            set => SetProperty(value);
        }

        public string IPAddress
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string DomainName
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Loss
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Sent
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Received
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Best
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Avrg
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Wrst
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Last
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public TraceResult(ILoggingService logger, int ttl, string ipAddress, string domainName, HopData hop)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (hop == null)
            {
                _logger.Error("[TraceResult] HopData не может быть null.");
                throw new ArgumentNullException(nameof(hop));
            }

            Nr = ttl;
            IPAddress = ipAddress;
            DomainName = domainName;
            UpdateStatistics(hop);
        }

        public void UpdateStatistics(HopData hop)
        {
            if (hop == null)
            {
                _logger.Error("[TraceResult] HopData не может быть null.");
                throw new ArgumentNullException(nameof(hop));
            }

            _logger.Information($"[TraceResult] Обновление статистики для {DomainName}.");
            var stats = hop.GetStatistics();
            UpdateStatisticsValues(
                hop.Sent,
                hop.Received,
                hop.CalculateLossPercentage(),
                stats.Min,
                stats.Max,
                stats.Avg,
                stats.Last
            );
        }

        private void UpdateStatisticsValues(
            int sent, int received, double lossPercentage,
            long bestTime, long worstTime, double averageTime, long lastTime)
        {
            _logger.Information($"[TraceResult] Обновление значений статистики: Sent={sent}, Received={received}.");
            SetProperty(sent.ToString(), nameof(Sent));
            SetProperty(received.ToString(), nameof(Received));
            SetProperty($"{lossPercentage.ToString(Constants.DefaultFormat)}{Constants.PercentageSuffix}", nameof(Loss));
            SetProperty(FormatMilliseconds(bestTime), nameof(Best));
            SetProperty(FormatMilliseconds(worstTime), nameof(Wrst));
            SetProperty(FormatMilliseconds((long)averageTime), nameof(Avrg));
            SetProperty(FormatMilliseconds(lastTime), nameof(Last));
        }

        private static string FormatMilliseconds(long milliseconds) =>
            $"{milliseconds}{Constants.MsUnitSuffix}";
    }

    public sealed class HopData
    {
        private readonly ConcurrentQueue<long> _responseTimes = new();
        private readonly ILoggingService _logger;
        private readonly object _statsLock = new();

        private volatile int _sent;
        private volatile int _received;
        private long _lastResponseTime;
        private (long Min, long Max, double Avg, long Last) _cachedStats;
        private volatile bool _statsNeedUpdate = true;

        public HopData(ILoggingService logger) =>
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public int Sent
        {
            get => _sent;
            set => Interlocked.Exchange(ref _sent, value);
        }

        public int Received
        {
            get => _received;
            set => Interlocked.Exchange(ref _received, value);
        }

        public void AddResponseTime(long time)
        {
            if (time < 0)
            {
                _logger.Error("[HopData] Отрицательное время отклика");
                throw new ArgumentOutOfRangeException(nameof(time));
            }

            _responseTimes.Enqueue(time);
            Interlocked.Exchange(ref _lastResponseTime, time);
            _statsNeedUpdate = true;
        }

        public double CalculateLossPercentage() =>
            Sent == 0 ? 0 : (double)(Sent - Received) / Sent * 100;

        public (long Min, long Max, double Avg, long Last) GetStatistics()
        {
            if (_responseTimes.IsEmpty)
                return (0, 0, 0, 0);

            if (!_statsNeedUpdate)
                return _cachedStats;

            lock (_statsLock)
            {
                if (!_statsNeedUpdate)
                    return _cachedStats;

                var times = _responseTimes.ToArray();
                if (times.Length == 0)
                    return (0, 0, 0, 0);

                _cachedStats = (
                    times.Min(),
                    times.Max(),
                    times.Average(),
                    _lastResponseTime
                );
                _statsNeedUpdate = false;
                return _cachedStats;
            }
        }

        public void Clear()
        {
            lock (_statsLock)
            {
                while (_responseTimes.TryDequeue(out _)) { }
                _lastResponseTime = 0;
                _statsNeedUpdate = true;
                _cachedStats = default;
                Sent = Received = 0;
            }
        }
    }

    // -------------------- Implementations --------------------
    public class DnsManager : ValidationBase, IDnsManager
    {
        private readonly IMemoryCache _dnsCache;
        private readonly TimeSpan _dnsTimeout;
        private readonly MemoryCacheEntryOptions _cacheOptions;
        private readonly ILoggingService _logger;

        public DnsManager(IMemoryCache memoryCache, ILoggingService logger, TimeSpan? dnsTimeout = null)
        {
            ValidateNotNull(memoryCache, nameof(memoryCache), logger);
            ValidateNotNull(logger, nameof(logger), logger);

            _dnsCache = memoryCache;
            _logger = logger;
            _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                .SetSlidingExpiration(TimeSpan.FromMinutes(10));
        }

        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            ValidateNotNullOrEmpty(ipAddress, nameof(ipAddress), _logger);

            if (!IPAddress.TryParse(ipAddress, out var parsedIp))
            {
                _logger.Error($"[DnsManager] Некорректный IP-адрес: {ipAddress}");
                throw new ArgumentException("Некорректный IP-адрес", nameof(ipAddress));
            }

            return await ResolveDomainNameAsync(ipAddress, parsedIp, token);
        }

        private async Task<string> ResolveDomainNameAsync(string ipAddress, IPAddress parsedIp, CancellationToken token)
        {
            if (_dnsCache.TryGetValue(ipAddress, out string? cachedResult))
            {
                return cachedResult ?? Constants.DefaultUnresolvedValue;
            }

            try
            {
                var result = parsedIp.IsPrivate()
                    ? GetLocalNetworkName(parsedIp)
                    : await ResolveRemoteDomainNameAsync(parsedIp, token);

                _dnsCache.Set(ipAddress, result, _cacheOptions);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warning($"[DnsManager] Возвращен неразрешенный результат для IP: {ipAddress}", ex);
                _dnsCache.Set(ipAddress, Constants.DefaultUnresolvedValue, _cacheOptions);
                return Constants.DefaultUnresolvedValue;
            }
        }

        private async Task<string> ResolveRemoteDomainNameAsync(IPAddress ip, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(_dnsTimeout);
            var hostEntry = await Dns.GetHostEntryAsync(ip);
            return hostEntry.HostName;
        }

        private static string GetLocalNetworkName(IPAddress ip) => ip.AddressFamily switch
        {
            AddressFamily.InterNetworkV6 => GetLocalIpv6Name(ip),
            AddressFamily.InterNetwork => GetLocalIpv4Name(ip),
            _ => "Неизвестный локальный адрес"
        };

        private static string GetLocalIpv6Name(IPAddress ip) => ip switch
        {
            { IsIPv6LinkLocal: true } => "IPv6 Link-Local",
            { IsIPv6SiteLocal: true } => "IPv6 Site-Local",
            { IsIPv6Multicast: true } => "IPv6 Multicast",
            _ => "Прочий IPv6 адрес"
        };

        private static string GetLocalIpv4Name(IPAddress ip) => ip switch
        {
            var addr when addr.IsInSubnet(IPAddress.Parse("192.168.0.0"), 16) => "Локальная сеть (Router)",
            var addr when addr.IsInSubnet(IPAddress.Parse("10.0.0.0"), 8) => "DNS провайдера",
            var addr when addr.IsInSubnet(IPAddress.Parse("172.16.0.0"), 12) => "Локальная сеть",
            _ => "Прочий IPv4 адрес"
        };
    }

    public static class NetworkExtensions
    {
        public static bool IsPrivate(this IPAddress ipAddress)
        {
            if (ipAddress == null) return false;

            var bytes = ipAddress.GetAddressBytes();
            return ipAddress.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsPrivateIPv4(bytes),
                AddressFamily.InterNetworkV6 => IsPrivateIPv6(bytes),
                _ => false
            };
        }

        public static bool IsInSubnet(this IPAddress ipAddress, IPAddress subnetMask, int prefixLength)
        {
            if (ipAddress == null || subnetMask == null || ipAddress.AddressFamily != subnetMask.AddressFamily)
                return false;

            var ipBytes = ipAddress.GetAddressBytes();
            var subnetBytes = subnetMask.GetAddressBytes();

            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != subnetBytes[i])
                    return false;
            }

            if (remainingBits > 0)
            {
                int mask = 0xFF << (8 - remainingBits);
                return (ipBytes[fullBytes] & mask) == (subnetBytes[fullBytes] & mask);
            }

            return true;
        }

        private static bool IsPrivateIPv4(byte[] bytes) =>
            bytes[0] == Constants.Network.PrivateNetworkAFirstByte ||
            (bytes[0] == Constants.Network.PrivateNetworkBFirstByte &&
             bytes[1] >= Constants.Network.PrivateNetworkBSecondByteStart &&
             bytes[1] <= Constants.Network.PrivateNetworkBSecondByteEnd) ||
            (bytes[0] == Constants.Network.PrivateNetworkCFirstByte &&
             bytes[1] == Constants.Network.PrivateNetworkCSecondByte);

        private static bool IsPrivateIPv6(byte[] bytes) =>
            bytes[0] == 0xfc || bytes[0] == 0xfd;
    }

    public class PingManager : ValidationBase, IPingManager
    {
        private readonly IDnsManager _dnsManager;
        private readonly ConcurrentDictionary<string, HopData> _hopData;
        private readonly byte[] _buffer;
        private readonly ILoggingService _logger;

        public PingManager(IDnsManager dnsManager, ILoggingService logger)
        {
            ValidateNotNull(dnsManager, nameof(dnsManager), logger);
            ValidateNotNull(logger, nameof(logger), logger);

            _dnsManager = dnsManager;
            _logger = logger;
            _hopData = new ConcurrentDictionary<string, HopData>();
            _buffer = new byte[Constants.Ping.BufferSize];
        }

        public async Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            ValidateNotNullOrEmpty(host, nameof(host), _logger);
            ValidateNotNull(updateUiCallback, nameof(updateUiCallback), _logger);

            try
            {
                await ExecuteTracingLoopAsync(host, token, updateUiCallback);
            }
            finally
            {
                _logger.Information($"[PingManager] Завершение трассировки для хоста: {host}");
            }
        }

        public void ClearHopData()
        {
            _hopData.Clear();
            _logger.Information("[PingManager] Очищена статистика по хопам");
        }

        private async Task ExecuteTracingLoopAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            while (!token.IsCancellationRequested)
            {
                var (maxTtl, delay) = GetTraceParameters();
                await ExecuteTraceRoundAsync(host, maxTtl, updateUiCallback, token);
                await Task.Delay(delay, token);
            }
        }

        private (int MaxTtl, int Delay) GetTraceParameters()
        {
            var stats = CalculateLossStatistics();
            return (Constants.Ping.MaxTtl, CalculateAdaptiveDelay(stats.LossPercentage));
        }

        private (int TotalSent, int TotalReceived, double LossPercentage) CalculateLossStatistics()
        {
            int totalSent = _hopData.Values.Sum(h => h.Sent);
            int totalReceived = _hopData.Values.Sum(h => h.Received);
            double lossPercentage = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;
            return (totalSent, totalReceived, lossPercentage);
        }

        private static int CalculateAdaptiveDelay(double lossPercentage) => lossPercentage switch
        {
            > Constants.Ping.HighLossThreshold => Math.Min(Constants.Ping.Timeout, (int)(Constants.Ping.BaseDelay * 1.5)),
            < Constants.Ping.LowLossThreshold => Math.Max(Constants.Ping.MinDelay, (int)(Constants.Ping.BaseDelay * 0.75)),
            _ => Constants.Ping.BaseDelay
        };

        private async Task ExecuteTraceRoundAsync(string host, int maxTtl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(1, maxTtl)
                .Select(ttl => ExecutePingForTtlAsync(host, ttl, updateUiCallback, token));
            await Task.WhenAll(pingTasks);
        }

        private async Task ExecutePingForTtlAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(0, Constants.Ping.ParallelRequests)
                .Select(_ => ExecuteSinglePingAsync(host, ttl, updateUiCallback, token));
            await Task.WhenAll(pingTasks);
        }

        private async Task ExecuteSinglePingAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            using var pingSender = new Ping();
            try
            {
                var (reply, responseTime) = await SendPingAsync(pingSender, host, ttl, token);
                if (reply != null)
                {
                    await ProcessPingReplyAsync(reply, ttl, responseTime, updateUiCallback, token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, $"[PingManager] Ошибка при пинге {host} с TTL {ttl}: {ex.Message}");
            }
        }

        private async Task<(PingReply? Reply, long ResponseTime)> SendPingAsync(Ping pingSender, string host, int ttl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var reply = await pingSender.SendPingAsync(host, Constants.Ping.Timeout, _buffer, new PingOptions { Ttl = ttl });
            stopwatch.Stop();

            return (reply, stopwatch.ElapsedMilliseconds);
        }

        private async Task ProcessPingReplyAsync(PingReply reply, int ttl, long responseTime, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            string ipAddress = reply.Address?.ToString() ?? "Неизвестный адрес";
            if (IsValidIpAddress(ipAddress))
            {
                var hop = _hopData.GetOrAdd(ipAddress, _ => new HopData(_logger));
                UpdateHopStatistics(hop, reply, responseTime);

                string domainName = await _dnsManager.GetDomainNameAsync(ipAddress, token);
                updateUiCallback(ipAddress, ttl, domainName, hop);
            }
        }

        private static bool IsValidIpAddress(string ipAddress)
            => !string.IsNullOrEmpty(ipAddress) && ipAddress.Trim() != "0.0.0.0";

        private static void UpdateHopStatistics(HopData hop, PingReply reply, long responseTime)
        {
            hop.Sent++;
            if (reply.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded)
            {
                hop.Received++;
                hop.AddResponseTime(responseTime);
            }
        }
    }

    public class TraceManager : ValidationBase, IDisposable
    {
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly IPingManager _pingManager;
        private readonly IDnsManager _dnsManager;
        private readonly ILoggingService _logger;
        private readonly ObservableCollection<TraceResult> _traceResults;
        private readonly IMemoryCache _memoryCache;
        private bool _isTracing;

        public ObservableCollection<TraceResult> TraceResults => _traceResults;
        public string TraceUrl { get; }
        public bool IsTracing
        {
            get => _isTracing;
            private set => _isTracing = value;
        }

        public TraceManager(string url, ILoggingService logger)
        {
            ValidateNotNullOrEmpty(url, nameof(url), logger);
            ValidateNotNull(logger, nameof(logger), logger);

            TraceUrl = url;
            _logger = logger;
            _traceResults = new ObservableCollection<TraceResult>();

            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _dnsManager = new DnsManager(_memoryCache, logger);
            _pingManager = new PingManager(_dnsManager, logger);

            _logger.Information($"[TraceManager] Инициализирован с URL: {url}");
        }

        public async Task StartTraceAsync(Action<string, Color> updateStatus, Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
        {
            if (!ValidateTraceStart(showMessage)) return;

            IsTracing = true;
            updateStatus("Трассировка запущена...", Colors.Green);
            _logger.Information($"[TraceManager] Запуск трассировки для URL: {TraceUrl}");

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                await _pingManager.StartTraceAsync(TraceUrl, _cts.Token, UpdateHopStatistics);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("[TraceManager] Трассировка отменена");
            }
            catch (Exception ex)
            {
                HandleTraceError(ex, showMessage);
            }
            finally
            {
                ResetTraceStatus(updateStatus);
            }
        }

        private bool ValidateTraceStart(Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
        {
            if (IsTracing)
            {
                showMessage("Трассировка уже запущена.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.Warning("[TraceManager] Попытка запустить уже запущенную трассировку");
                return false;
            }
            return true;
        }

        private void HandleTraceError(Exception ex, Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
        {
            _logger.Error(ex, $"[TraceManager] Ошибка: {ex.Message}");
            showMessage($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void StopTrace()
        {
            try
            {
                _cts?.Cancel();
                _logger.Information("[TraceManager] Трассировка остановлена");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TraceManager] Ошибка при остановке трассировки");
            }
        }

        public void ClearResults()
        {
            _traceResults.Clear();
            _pingManager.ClearHopData();
            _logger.Information("[TraceManager] Результаты очищены");
        }

        private void UpdateHopStatistics(string ipAddress, int ttl, string domainName, HopData hop)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                _logger.Warning("[TraceManager] Получен пустой IP-адрес");
                return;
            }

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var existingResult = _traceResults.FirstOrDefault(tr => tr.IPAddress == ipAddress);

                    if (existingResult is null)
                    {
                        _traceResults.Add(new TraceResult(_logger, ttl, ipAddress, domainName, hop));
                        _logger.Information($"[TraceManager] Добавлен новый результат для IP: {ipAddress}");
                    }
                    else
                    {
                        existingResult.UpdateStatistics(hop);
                        _logger.Information($"[TraceManager] Обновлена статистика для IP: {ipAddress}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TraceManager] Ошибка при обновлении статистики хопа");
            }
        }

        private void ResetTraceStatus(Action<string, Color> updateStatus)
        {
            IsTracing = false;
            updateStatus("Трассировка остановлена.", Colors.Red);
            _logger.Information("[TraceManager] Статус трассировки сброшен");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _cts?.Dispose();
                _memoryCache?.Dispose();
            }

            _disposed = true;
        }

        ~TraceManager()
        {
            Dispose(false);
        }
    }

    public partial class TraceWindow : Window, ITraceWindow
    {
        private readonly TraceManager _traceManager;
        private readonly ILoggingService _logger;

        public ICollectionView TraceResults { get; }

        public TraceWindow(string url, ILoggingService logger)
        {
            InitializeComponent();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _traceManager = new TraceManager(url, _logger);

            TraceResults = ConfigureTraceResults();
            _logger.Information($"[TraceWindow] Инициализирован с URL: {url}");
        }

        private ICollectionView ConfigureTraceResults()
        {
            var view = CollectionViewSource.GetDefaultView(_traceManager.TraceResults);
            ResultsList.ItemsSource = view;
            ((CollectionView)view).SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
            return view;
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            await HandleTraceStartAsync();
        }

        private async Task HandleTraceStartAsync()
        {
            if (!ValidateTraceStart()) return;

            SetTraceControlsState(isStarting: true);
            await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
        }

        private bool ValidateTraceStart()
        {
            if (_traceManager.IsTracing)
            {
                ShowMessage("Трассировка уже запущена.", "Предупреждение");
                _logger.Warning("[TraceWindow] Попытка запустить уже запущенную трассировку");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_traceManager.TraceUrl))
            {
                ShowMessage("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.Error("[TraceWindow] URL для трассировки не указан");
                return false;
            }

            return true;
        }

        private void SaveResults(string fileName)
        {
            try
            {
                File.WriteAllLines(fileName, _traceManager.TraceResults.Select(result => result?.ToString() ?? "Пустой результат"));
                ShowMessage("Результаты успешно сохранены.", "Успех");
                _logger.Information($"[TraceWindow] Результаты сохранены в файл: {fileName}");
            }
            catch (Exception ex)
            {
                ShowMessage($"Ошибка при сохранении результатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.Error(ex, $"[TraceWindow] Ошибка при сохранении результатов: {ex.Message}");
            }
        }

        private void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
            => MessageBox.Show(message, title, button, icon);

        private void UpdateStatus(string message, Color color)
            => StatusTextBlock.Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(color);
            });

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            _traceManager.ClearResults();
            _logger.Information("[TraceWindow] Результаты очищены");
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _traceManager.StopTrace();
            UpdateStatus("Остановка трассировки...", Colors.Orange);
            _logger.Information("[TraceWindow] Трассировка остановлена");
            SetTraceControlsState(isStarting: false);
        }

        private void SetTraceControlsState(bool isStarting)
        {
            btnStartTrace.IsEnabled = !isStarting;
            btnStopTrace.IsEnabled = isStarting;
        }

        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SaveResults(saveFileDialog.FileName);
            }
        }
    }
}