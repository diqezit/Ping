#nullable enable

namespace PingTestTool
{

    public class DnsManager : IDnsManager
    {
        private const string DefaultUnresolvedValue = "---";
        private readonly IMemoryCache _dnsCache;
        private readonly TimeSpan _dnsTimeout;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public DnsManager(IMemoryCache memoryCache, TimeSpan? dnsTimeout = null)
        {
            _dnsCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                .SetSlidingExpiration(TimeSpan.FromMinutes(10));

            Log.Information("[DnsManager] DnsManager инициализирован");
        }

        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            if (!IPAddress.TryParse(ipAddress, out var parsedIp))
            {
                Log.Error("[DnsManager] Некорректный IP-адрес: {IpAddress}", ipAddress);
                throw new ArgumentException("Некорректный IP-адрес", nameof(ipAddress));
            }

            if (_dnsCache.TryGetValue(ipAddress, out string? cachedResult))
            {
                return cachedResult;
            }

            try
            {
                if (parsedIp.IsPrivate())
                {
                    var localName = GetLocalNetworkName(parsedIp);
                    _dnsCache.Set(ipAddress, localName, _cacheOptions);
                    return localName;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(_dnsTimeout);

                var hostEntry = await Dns.GetHostEntryAsync(parsedIp);
                var domainName = hostEntry.HostName;
                _dnsCache.Set(ipAddress, domainName, _cacheOptions);

                return domainName;
            }
            catch (Exception ex)
            {
                _dnsCache.Set(ipAddress, DefaultUnresolvedValue, _cacheOptions);
                Log.Warning("[DnsManager] Возвращен неразрешенный результат для IP: {IpAddress}", ipAddress, ex);
                return DefaultUnresolvedValue;
            }
        }

        private string GetLocalNetworkName(IPAddress ip) => ip.AddressFamily switch
        {
            AddressFamily.InterNetworkV6 => GetLocalIpv6Name(ip),
            AddressFamily.InterNetwork => GetLocalIpv4Name(ip),
            _ => "Неизвестный локальный адрес"
        };

        private string GetLocalIpv6Name(IPAddress ip) => ip switch
        {
            { IsIPv6LinkLocal: true } => "IPv6 Link-Local",
            { IsIPv6SiteLocal: true } => "IPv6 Site-Local",
            { IsIPv6Multicast: true } => "IPv6 Multicast",
            _ => "Прочий IPv6 адрес"
        };

        private string GetLocalIpv4Name(IPAddress ip) => ip switch
        {
            var addr when addr.IsInSubnet(IPAddress.Parse("192.168.0.0"), 16) => "Локальная сеть (Router)",
            var addr when addr.IsInSubnet(IPAddress.Parse("10.0.0.0"), 8) => "DNS провайдера",
            var addr when addr.IsInSubnet(IPAddress.Parse("172.16.0.0"), 12) => "Локальная сеть",
            _ => "Прочий IPv4 адрес"
        };
    }

    public interface IDnsManager
    {
        Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token);
    }

    public static class DnsManagerExtensions
    {
        private const byte PrivateNetworkAFirstByte = 10;
        private const byte PrivateNetworkBFirstByte = 172;
        private const byte PrivateNetworkBSecondByteStart = 16;
        private const byte PrivateNetworkBSecondByteEnd = 31;
        private const byte PrivateNetworkCFirstByte = 192;
        private const byte PrivateNetworkCSecondByte = 168;

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
            bytes[0] == PrivateNetworkAFirstByte ||
            (bytes[0] == PrivateNetworkBFirstByte && bytes[1] >= PrivateNetworkBSecondByteStart && bytes[1] <= PrivateNetworkBSecondByteEnd) ||
            (bytes[0] == PrivateNetworkCFirstByte && bytes[1] == PrivateNetworkCSecondByte);

        private static bool IsPrivateIPv6(byte[] bytes) => bytes[0] == 0xfc || bytes[0] == 0xfd;
    }

    public class TraceResult : INotifyPropertyChanged
    {
        private const string MsUnitSuffix = " ms";
        private const string PercentageSuffix = "%";
        private const string DefaultFormat = "F0";

        private readonly Dictionary<string, object> _propertyValues = new();

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
        {
            if (hop == null)
            {
                Log.Error("[TraceResult] HopData не может быть null.");
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
                Log.Error("[TraceResult] HopData не может быть null.");
                throw new ArgumentNullException(nameof(hop));
            }

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
            SetProperty(sent.ToString(), nameof(Sent));
            SetProperty(received.ToString(), nameof(Received));
            SetProperty($"{lossPercentage.ToString(DefaultFormat)}{PercentageSuffix}", nameof(Loss));
            SetProperty(FormatMilliseconds(bestTime), nameof(Best));
            SetProperty(FormatMilliseconds(worstTime), nameof(Wrst));
            SetProperty(FormatMilliseconds((long)averageTime), nameof(Avrg));
            SetProperty(FormatMilliseconds(lastTime), nameof(Last));
        }

        private static string FormatMilliseconds(long milliseconds) =>
            $"{milliseconds}{MsUnitSuffix}";

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetProperty<T>(T value, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return false;

            if (!_propertyValues.TryGetValue(propertyName, out var currentValue) ||
                !EqualityComparer<T>.Default.Equals((T)currentValue, value))
            {
                _propertyValues[propertyName] = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }

        private T GetProperty<T>(T defaultValue = default!, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return defaultValue;
            return _propertyValues.TryGetValue(propertyName, out var value)
                ? (T)value
                : defaultValue;
        }
    }

    public class HopData
    {
        private readonly ConcurrentBag<long> _responseTimes = new();
        private readonly object _statsLock = new();

        private volatile int _sent;
        private volatile int _received;

        private long? _lastResponseTime;
        private (long Min, long Max, double Avg, long Last) _cachedStats;
        private bool _statsNeedUpdate = true;

        public int Sent
        {
            get => _sent;
            set
            {
                Interlocked.Exchange(ref _sent, value);
                _statsNeedUpdate = true;
            }
        }

        public int Received
        {
            get => _received;
            set
            {
                Interlocked.Exchange(ref _received, value);
                _statsNeedUpdate = true;
            }
        }

        private readonly object _lock = new();

        public void AddResponseTime(long time)
        {
            if (time < 0)
            {
                Log.Error("[HopData] Ответное время не может быть отрицательным");
                throw new ArgumentOutOfRangeException(nameof(time), "Response time cannot be negative");
            }

            _responseTimes.Add(time);
            lock (_lock)
            {
                _lastResponseTime = time;
            }
            _statsNeedUpdate = true;
        }

        public double CalculateLossPercentage()
        {
            var lossPercentage = Sent == 0 ? 0 : (double)(Sent - Received) / Sent * 100;
            return lossPercentage;
        }

        public (long Min, long Max, double Avg, long Last) GetStatistics()
        {
            if (_responseTimes.IsEmpty)
            {
                return (0, 0, 0, 0);
            }

            if (!_statsNeedUpdate)
            {
                return _cachedStats;
            }

            lock (_statsLock)
            {
                if (!_statsNeedUpdate)
                {
                    return _cachedStats;
                }

                var responseArray = _responseTimes.ToArray();
                _cachedStats = CalculateStatistics(responseArray);
                _statsNeedUpdate = false;
                return _cachedStats;
            }
        }

        public void Clear()
        {
            lock (_statsLock)
            {
                while (!_responseTimes.IsEmpty)
                {
                    _responseTimes.TryTake(out _);
                }

                _lastResponseTime = null;
                _statsNeedUpdate = true;
                _cachedStats = default;
                Sent = 0;
                Received = 0;

                Log.Debug("[HopData] Очищена статистика");
            }
        }

        private (long Min, long Max, double Avg, long Last) CalculateStatistics(long[] times)
        {
            if (times == null || times.Length == 0)
            {
                return (0, 0, 0, 0);
            }

            var min = times.Min();
            var max = times.Max();
            var avg = times.Average();
            var last = _lastResponseTime ?? times[times.Length - 1];
            return (min, max, avg, last);
        }
    }

    public class PingManager : IPingManager
    {
        private const int BufferSize = 32;
        private const int MaxTtl = 12;
        private const int Timeout = 5000;
        private const int ParallelRequests = 1;
        private const int BaseDelay = 1000;
        private const int MinDelay = 100;
        private const double HighLossThreshold = 50;
        private const double LowLossThreshold = 10;

        private readonly IDnsManager _dnsManager;
        private readonly ConcurrentDictionary<string, HopData> _hopData;
        private readonly byte[] _buffer;

        public PingManager(IDnsManager dnsManager)
        {
            _dnsManager = dnsManager ?? throw new ArgumentNullException(nameof(dnsManager));
            _hopData = new ConcurrentDictionary<string, HopData>();
            _buffer = new byte[BufferSize];
        }

        public async Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            ValidateHost(host);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var (currentMaxTtl, delay) = CalculateTraceParameters();
                    await ExecuteTraceRoundAsync(host, currentMaxTtl, updateUiCallback, token);
                    await Task.Delay(delay, token);
                }
            }
            finally
            {
                Log.Information("[PingManager] Завершение трассировки для хоста: {Host}", host);
            }
        }

        public void ClearHopData()
        {
            _hopData.Clear();
            Log.Debug("[PingManager] Очищена статистика по хопам");
        }

        private static void ValidateHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                const string message = "Хост не может быть null или пустым.";
                Log.Error("[PingManager] {Message}", message);
                throw new ArgumentNullException(nameof(host), message);
            }
        }

        private (int MaxTtl, int Delay) CalculateTraceParameters()
        {
            var stats = CalculateLossStatistics();
            int delay = CalculateAdaptiveDelay(stats.LossPercentage);
            return (MaxTtl, delay);
        }

        private (int TotalSent, int TotalReceived, double LossPercentage) CalculateLossStatistics()
        {
            int totalSent = _hopData.Values.Sum(h => h.Sent);
            int totalReceived = _hopData.Values.Sum(h => h.Received);
            double lossPercentage = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;
            return (totalSent, totalReceived, lossPercentage);
        }

        private static int CalculateAdaptiveDelay(double lossPercentage)
        {
            if (lossPercentage > HighLossThreshold)
                return Math.Min(Timeout, (int)(BaseDelay * 1.5));
            if (lossPercentage < LowLossThreshold)
                return Math.Max(MinDelay, (int)(BaseDelay * 0.75));
            return BaseDelay;
        }

        private async Task ExecuteTraceRoundAsync(string host, int maxTtl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(1, maxTtl)
                .Select(ttl => ExecutePingForTtlAsync(host, ttl, updateUiCallback, token));
            await Task.WhenAll(pingTasks);
        }

        private async Task ExecutePingForTtlAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(0, ParallelRequests)
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
                LogPingError(ex, host, ttl);
            }
        }

        private async Task<(PingReply? Reply, long ResponseTime)> SendPingAsync(Ping pingSender, string host, int ttl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var reply = await pingSender.SendPingAsync(host, Timeout, _buffer, new PingOptions { Ttl = ttl });
            stopwatch.Stop();

            return (reply, stopwatch.ElapsedMilliseconds);
        }

        private async Task ProcessPingReplyAsync(PingReply reply, int ttl, long responseTime, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            string ipAddress = reply.Address?.ToString() ?? "Неизвестный адрес";
            if (IsValidIpAddress(ipAddress))
            {
                var hop = _hopData.GetOrAdd(ipAddress, _ => new HopData());
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

        private static void LogPingError(Exception ex, string host, int ttl)
        {
            string errorType = ex is PingException ? "Ошибка пинга" : "Непредвиденная ошибка";
            Log.Error(ex, "[PingManager] {ErrorType} при пинге {Host} с TTL {Ttl}: {Message}", errorType, host, ttl, ex.Message);
        }
    }

    public interface IPingManager
    {
        Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback);
        void ClearHopData();
    }

    public class TraceManager : IDisposable
    {
        private CancellationTokenSource? _cts;
        private bool _isTracing;
        private bool _disposed;
        private readonly string _traceUrl;
        private readonly ObservableCollection<TraceResult> _traceResults;
        private readonly IMemoryCache _memoryCache;
        private readonly IPingManager _pingManager;
        private readonly IDnsManager _dnsManager;

        public ObservableCollection<TraceResult> TraceResults => _traceResults;
        public string TraceUrl => _traceUrl;
        public bool IsTracing => _isTracing;

        public TraceManager(string url)
        {
            _traceUrl = url ?? throw new ArgumentNullException(nameof(url), "URL не может быть null.");
            _traceResults = new ObservableCollection<TraceResult>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _dnsManager = new DnsManager(_memoryCache);
            _pingManager = new PingManager(_dnsManager);

            Log.Information("[TraceManager] Инициализирован с URL: {Url}", url);
        }

        public async Task StartTraceAsync(Action<string, Color> updateStatus, Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
        {
            if (_isTracing)
            {
                showMessage("Трассировка уже запущена.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Warning("[TraceManager] Попытка запустить уже запущенную трассировку");
                return;
            }

            _isTracing = true;
            updateStatus("Трассировка запущена...", Colors.Green);
            Log.Information("[TraceManager] Запуск трассировки для URL: {Url}", _traceUrl);

            using (_cts = new CancellationTokenSource())
            {
                try
                {
                    await _pingManager.StartTraceAsync(_traceUrl, _cts.Token, UpdateHopStatistics);
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("[TraceManager] Трассировка отменена");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[TraceManager] Ошибка: {Message}", ex.Message);
                    showMessage($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ResetTraceStatus(updateStatus);
                }
            }
        }

        public void StopTrace()
        {
            try
            {
                _cts?.Cancel();
                Log.Information("[TraceManager] Трассировка остановлена");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TraceManager] Ошибка при остановке трассировки");
            }
        }

        public void ClearResults()
        {
            _traceResults.Clear();
            _pingManager.ClearHopData();
            Log.Information("[TraceManager] Результаты очищены");
        }

        private void UpdateHopStatistics(string ipAddress, int ttl, string domainName, HopData hop)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                Log.Warning("[TraceManager] Получен пустой IP-адрес");
                return;
            }

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var existingResult = _traceResults.FirstOrDefault(tr => tr.IPAddress == ipAddress);

                    if (existingResult is null)
                    {
                        _traceResults.Add(new TraceResult(ttl, ipAddress, domainName, hop));
                        Log.Debug("[TraceManager] Добавлен новый результат для IP: {IpAddress}", ipAddress);
                    }
                    else
                    {
                        existingResult.UpdateStatistics(hop);
                        Log.Debug("[TraceManager] Обновлена статистика для IP: {IpAddress}", ipAddress);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TraceManager] Ошибка при обновлении статистики хопа");
            }
        }

        private void ResetTraceStatus(Action<string, Color> updateStatus)
        {
            _isTracing = false;
            updateStatus("Трассировка остановлена.", Colors.Red);
            Log.Information("[TraceManager] Статус трассировки сброшен");
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

    public interface ITraceWindow
    {
        bool IsLoaded { get; }
        bool IsVisible { get; }
        Visibility Visibility { get; set; }
        void Show();
        void Close();
        event EventHandler Closed;
    }

    public partial class TraceWindow : Window, ITraceWindow
    {
        private readonly TraceManager _traceManager;
        public ICollectionView TraceResults { get; }

        public TraceWindow(string url)
        {
            InitializeComponent();
            _traceManager = new TraceManager(url);

            TraceResults = ConfigureTraceResults();
            Log.Information("[TraceWindow] Инициализирован с URL: {Url}", url);
        }

        private ICollectionView ConfigureTraceResults()
        {
            var view = CollectionViewSource.GetDefaultView(_traceManager.TraceResults);
            ResultsList.ItemsSource = view;
            ((CollectionView)view).SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
            return view;
        }

        private async Task HandleTraceStartAsync()
        {
            if (!ValidateTraceStart()) return;

            SetTraceControlsState(isStarting: true);
            UpdateStatus("Трассировка запущена...", Colors.Green);
            await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
        }

        private bool ValidateTraceStart()
        {
            if (_traceManager.IsTracing)
            {
                ShowMessage("Трассировка уже запущена.", "Предупреждение");
                Log.Warning("[TraceWindow] Попытка запустить уже запущенную трассировку");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_traceManager.TraceUrl))
            {
                ShowMessage("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Error("[TraceWindow] URL для трассировки не указан");
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
                Log.Information("[TraceWindow] Результаты сохранены в файл: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                ShowMessage($"Ошибка при сохранении результатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Error(ex, "[TraceWindow] Ошибка при сохранении результатов: {Message}", ex.Message);
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
            Log.Information("[TraceWindow] Результаты очищены");
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            SetTraceControlsState(isStarting: true);
            await HandleTraceStartAsync();
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _traceManager.StopTrace();
            UpdateStatus("Остановка трассировки...", Colors.Orange);
            Log.Information("[TraceWindow] Трассировка остановлена");
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