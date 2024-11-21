#nullable enable

namespace PingTestTool
{

    public interface IPingConfiguration
    {
        string Url { get; }
        int PingCount { get; }
        int Timeout { get; }
        bool DontFragment { get; }
        void Validate();
    }

    public interface IPingTestResult
    {
        int SuccessfulPings { get; }
        int FailedPings { get; }
        TimeSpan ExecutionTime { get; }
        double AverageJitter { get; }
        IReadOnlyList<int> RoundtripTimes { get; }
        string DetailedLog { get; }
    }

    public class PingConfiguration : IPingConfiguration
    {
        private const int MIN_TIMEOUT = 100;
        private const int MAX_TIMEOUT = 60000;
        private const int MIN_PING_COUNT = 1;
        private const int MAX_PING_COUNT = 1000;

        public string Url { get; }
        public int PingCount { get; }
        public int Timeout { get; }
        public bool DontFragment { get; }

        public PingConfiguration(string url, int pingCount, int timeout, bool dontFragment = true)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            PingCount = pingCount;
            Timeout = timeout;
            DontFragment = dontFragment;
            Validate();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                throw new ArgumentException("Url не может быть null или пустым.", nameof(Url));
            }

            if (PingCount is < MIN_PING_COUNT or > MAX_PING_COUNT)
            {
                throw new ArgumentException(
                    $"Количество пингов должно быть между {MIN_PING_COUNT} и {MAX_PING_COUNT}.");
            }

            if (Timeout is < MIN_TIMEOUT or > MAX_TIMEOUT)
            {
                throw new ArgumentException(
                    $"Таймаут должен быть между {MIN_TIMEOUT} и {MAX_TIMEOUT} мс.");
            }
        }
    }

    public class PingTestResult : IPingTestResult
    {
        public int SuccessfulPings { get; }
        public int FailedPings { get; }
        public TimeSpan ExecutionTime { get; }
        public double AverageJitter { get; }
        public IReadOnlyList<int> RoundtripTimes { get; }
        public string DetailedLog { get; }

        public PingTestResult(
            int successfulPings,
            int failedPings,
            TimeSpan executionTime,
            double averageJitter,
            IReadOnlyList<int> roundtripTimes,
            string detailedLog)
        {
            SuccessfulPings = successfulPings;
            FailedPings = failedPings;
            ExecutionTime = executionTime;
            AverageJitter = averageJitter;
            RoundtripTimes = roundtripTimes;
            DetailedLog = detailedLog;
        }
    }

    public sealed class PingService : IAsyncDisposable, IPingTestService
    {
        #region Constants

        private const int BUFFER_SIZE = 32;
        private const string LOG_SEPARATOR = "══════════════════════════════════════════════════════════";
        private const string LOG_MINI_SEPARATOR = "──────────────────────────────────────";
        private const string TIME_FORMAT = "HH:mm:ss";
        private const string DATE_TIME_FORMAT = "dd.MM.yyyy HH:mm:ss";

        #endregion

        #region Fields

        private readonly List<int> _roundtripTimes = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ILoggingService _logger;
        private bool _isDisposed;

        #endregion

        #region Events

        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;

        #endregion

        #region Constructor

        public PingService(ILoggingService? logger = null)
        {
            _logger = logger ?? new SerilogLoggingService();
        }

        #endregion

        #region Public Methods

        public async Task StartPingTestAsync(PingConfiguration config, CancellationToken cancellationToken = default)
        {
            await StartPingTestAsync((IPingConfiguration)config, cancellationToken);
        }

        public async Task<IPingTestResult> StartPingTestAsync(
            IPingConfiguration config,
            CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);

            var startTime = DateTime.Now;
            var logBuilder = new StringBuilder();
            var responseTimes = new StringBuilder();

            InitializeLogBuilder(logBuilder, config, startTime);

            try
            {
                var (successfulPings, failedPings) = await ExecutePingTestsAsync(
                    config, responseTimes, cancellationToken);

                var endTime = DateTime.Now;
                var executionTime = endTime - startTime;
                var avgJitter = await CalculateAverageJitterAsync(cancellationToken);

                var finalLog = GenerateFinalReport(
                    logBuilder,
                    responseTimes,
                    startTime,
                    endTime,
                    executionTime,
                    avgJitter,
                    successfulPings,
                    failedPings,
                    config.PingCount);

                return new PingTestResult(
                    successfulPings,
                    failedPings,
                    executionTime,
                    avgJitter,
                    await GetRoundtripTimesAsync(cancellationToken),
                    finalLog);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Ping test was cancelled");
                OnPingResult?.Invoke("Тест был остановлен." + Environment.NewLine);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during ping test execution");
                throw;
            }
        }

        public Task ClearRoundtripTimesAsync() => ClearRoundtripTimesAsync(CancellationToken.None);

        public async Task<List<int>?> GetRoundtripTimesAsync()
        {
            var times = await GetRoundtripTimesAsync(CancellationToken.None);
            return times.ToList();
        }

        public async Task<IReadOnlyList<int>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try
            {
                return _roundtripTimes.ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _roundtripTimes.Clear();
            }
            finally
            {
                _lock.Release();
            }
        }

        #endregion

        #region Private Methods

        private void InitializeLogBuilder(
            StringBuilder logBuilder,
            IPingConfiguration config,
            DateTime startTime)
        {
            var header = $"""
                {LOG_SEPARATOR}
                  ТЕСТ PING
                {LOG_SEPARATOR}
                Время начала:    {startTime.ToString(DATE_TIME_FORMAT)}
                Хост:           {config.Url}
                Кол-во пингов:  {config.PingCount}
                Таймаут:        {config.Timeout} мс
                Не фрагментировать: {(config.DontFragment ? "Да" : "Нет")}
                {LOG_SEPARATOR}

                """;

            logBuilder.Append(header);
            OnPingResult?.Invoke(header);
            _logger.Information("[PingService] Инициализация логгера для теста PING.");
        }

        private async Task<(int SuccessfulPings, int FailedPings)> ExecutePingTestsAsync(
            IPingConfiguration config,
            StringBuilder responseTimes,
            CancellationToken cancellationToken)
        {
            var successfulPings = 0;
            var failedPings = 0;

            using var ping = new Ping();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (isSuccess, roundtripTime, message, elapsedMs) = await PerformSinglePingAsync(
                    ping, config.Url, config.Timeout, options, buffer, i + 1, config.PingCount, cancellationToken);

                responseTimes.AppendLine(message);

                if (isSuccess)
                {
                    await AddRoundtripTimeAsync(roundtripTime, cancellationToken);
                    successfulPings++;
                }
                else
                {
                    failedPings++;
                }

                OnProgressUpdate?.Invoke(i + 1, config.PingCount);

                var delay = CalculateDelay(config.Timeout, elapsedMs);
                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            _logger.Information("[PingService] Выполнение пинг-тестов завершено. Успешных: {SuccessfulPings}, Неудачных: {FailedPings}",
                successfulPings, failedPings);
            return (successfulPings, failedPings);
        }

        private async Task<(bool IsSuccess, int RoundtripTime, string Message, long ElapsedMilliseconds)>
            PerformSinglePingAsync(
                Ping ping,
                string url,
                int timeout,
                PingOptions options,
                byte[] buffer,
                int currentPing,
                int totalPings,
                CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await ping.SendPingAsync(url, timeout, buffer, options);
                stopwatch.Stop();

                var resultLine = FormatPingResult(reply, url, currentPing, totalPings);
                OnPingResult?.Invoke(resultLine + Environment.NewLine);

                _logger.Information(
                    "[PingService] Пинг {CurrentPing}/{TotalPings} к {Url} завершен. Статус: {Status}, Время: {RoundtripTime} мс",
                    currentPing, totalPings, url, reply.Status, reply.RoundtripTime);

                return (reply.Status == IPStatus.Success,
                       (int)reply.RoundtripTime,
                       resultLine,
                       stopwatch.ElapsedMilliseconds);
            }
            catch (PingException ex)
            {
                stopwatch.Stop();

                _logger.Error(ex,
                    "[PingService] Ошибка пинга {CurrentPing}/{TotalPings} к {Url}",
                    currentPing, totalPings, url);

                var errorMessage = FormatPingError(url, currentPing, totalPings, ex.Message);
                OnPingResult?.Invoke(errorMessage + Environment.NewLine);
                throw;
            }
        }

        private static string FormatPingResult(PingReply reply, string url, int currentPing, int totalPings)
        {
            if (reply.Status == IPStatus.Success)
            {
                return $"""
                    [{DateTime.Now.ToString(TIME_FORMAT)}] [{currentPing}/{totalPings}] Ответ от {url}:
                        Время: {reply.RoundtripTime,4} мс
                        TTL:   {reply.Options?.Ttl ?? 0}
                        Размер:{reply.Buffer?.Length ?? 0} байт
                    """;
            }

            return $"""
                [{DateTime.Now.ToString(TIME_FORMAT)}] [{currentPing}/{totalPings}] Ошибка пинга {url}:
                    Статус: {reply.Status}
                """;
        }

        private static string FormatPingError(string url, int currentPing, int totalPings, string error) =>
            $"""
            [{DateTime.Now.ToString(TIME_FORMAT)}] [{currentPing}/{totalPings}] Критическая ошибка пинга {url}:
                {error}
            """;

        private async Task AddRoundtripTimeAsync(int roundtripTime, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _roundtripTimes.Add(roundtripTime);
                OnRoundtripTimeAdded?.Invoke(roundtripTime);
                _logger.Information("[PingService] Добавлено время кругового пути: {RoundtripTime} мс", roundtripTime);
            }
            finally
            {
                _lock.Release();
            }
        }

        private static int CalculateDelay(int timeout, long elapsedMilliseconds) =>
            Math.Max(0, timeout - (int)elapsedMilliseconds);

        private async Task<double> CalculateAverageJitterAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_roundtripTimes.Count <= 1) return 0;

                var totalJitter = 0.0;
                for (var i = 1; i < _roundtripTimes.Count; i++)
                {
                    totalJitter += Math.Abs(_roundtripTimes[i] - _roundtripTimes[i - 1]);
                }

                var avgJitter = Math.Round(totalJitter / (_roundtripTimes.Count - 1), 2);
                _logger.Information("[PingService] Рассчитан средний джиттер: {AvgJitter} мс", avgJitter);
                return avgJitter;
            }
            finally
            {
                _lock.Release();
            }
        }

        private string GenerateFinalReport(
                    StringBuilder logBuilder,
                    StringBuilder responseTimes,
                    DateTime startTime,
                    DateTime endTime,
                    TimeSpan executionTime,
                    double avgJitter,
                    int successfulPings,
                    int failedPings,
                    int totalPings)
        {
            var stats = CalculateStatistics();
            var lossPercentage = totalPings > 0
                ? (failedPings * 100.0 / totalPings).ToString("F2")
                : "0.00";

            var summary = $"""
                {LOG_SEPARATOR}
                  ИТОГИ ТЕСТИРОВАНИЯ
                {LOG_SEPARATOR}
                Время начала:   {startTime.ToString(DATE_TIME_FORMAT)}
                Время конца:    {endTime.ToString(DATE_TIME_FORMAT)}
                Длительность:   {FormatExecutionTime(executionTime)}

                {LOG_MINI_SEPARATOR}
                Статистика пакетов:
                    Всего отправлено: {totalPings}
                    Успешно:         {successfulPings}
                    Потеряно:        {failedPings} ({lossPercentage}%)

                {LOG_MINI_SEPARATOR}
                Статистика времени:
                    Минимальное:     {stats.min} мс
                    Максимальное:    {stats.max} мс
                    Среднее:         {stats.avg:F2} мс
                    Джиттер:         {avgJitter:F2} мс
                {LOG_SEPARATOR}
                """;

            logBuilder.AppendLine(responseTimes.ToString())
                     .AppendLine(summary);

            OnPingResult?.Invoke(summary);
            _logger.Information("[PingService] Сгенерирован итоговый отчет.");
            return logBuilder.ToString();
        }

        private static string FormatExecutionTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return $"{time.TotalHours:F2} часов";
            if (time.TotalMinutes >= 1)
                return $"{time.TotalMinutes:F2} минут";
            return $"{time.TotalSeconds:F2} секунд";
        }

        private (int min, int max, double avg) CalculateStatistics()
        {
            if (_roundtripTimes.Count == 0)
                return (0, 0, 0);

            var min = int.MaxValue;
            var max = int.MinValue;
            var sum = 0.0;

            foreach (var time in _roundtripTimes)
            {
                min = Math.Min(min, time);
                max = Math.Max(max, time);
                sum += time;
            }

            var avg = sum / _roundtripTimes.Count;
            _logger.Information("[PingService] Рассчитана статистика времени: Минимальное: {Min} мс, Максимальное: {Max} мс, Среднее: {Avg:F2} мс", min, max, avg);
            return (min, max, avg);
        }

        private async Task ThrowIfDisposedAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(PingService));
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed)
            {
                await ClearRoundtripTimesAsync();
                _lock.Dispose();
                _isDisposed = true;

                GC.SuppressFinalize(this);
                _logger.Information("[PingService] Сервис пинга успешно освобожден.");
            }
        }
        #endregion
    }
}