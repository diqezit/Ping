#nullable enable

namespace PingTestTool
{
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
}
