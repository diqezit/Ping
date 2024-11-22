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

    public interface IPingTestService : IAsyncDisposable
    {
        event Action<string>? OnPingResult;
        event Action<int, int>? OnProgressUpdate;
        event Action<int>? OnRoundtripTimeAdded;

        Task<IPingTestResult> StartPingTestAsync(IPingConfiguration config, CancellationToken cancellationToken = default);
        Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<int>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default);
    }

    public sealed class PingService : IPingTestService
    {
        private const int BUFFER_SIZE = 32;
        private const string TIME_FORMAT = "HH:mm:ss";
        private const string DATE_TIME_FORMAT = "dd.MM.yyyy HH:mm:ss";
        private const string LOG_SEPARATOR = "══════════════════════════════════════════════════════════";
        private const string LOG_MINI_SEPARATOR = "──────────────────────────────────────";

        private readonly List<int> _roundtripTimes = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ILoggingService _logger;
        private bool _isDisposed;

        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;

        public PingService(ILoggingService? logger = null)
        {
            _logger = logger ?? new SerilogLoggingService();
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
                var testResults = await ExecutePingTestsAsync(config, responseTimes, cancellationToken);
                var endTime = DateTime.Now;
                var executionTime = endTime - startTime;
                var avgJitter = await CalculateAverageJitterAsync(cancellationToken);

                var finalLog = await GenerateFinalReport(
                    logBuilder,
                    responseTimes,
                    startTime,
                    endTime,
                    executionTime,
                    avgJitter,
                    testResults.SuccessfulPings,
                    testResults.FailedPings,
                    config.PingCount);

                return new PingTestResult(
                    testResults.SuccessfulPings,
                    testResults.FailedPings,
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

        public async Task<IReadOnlyList<int>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try
            {
                return _roundtripTimes.ToList().AsReadOnly();
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

        private void InitializeLogBuilder(
            StringBuilder logBuilder,
            IPingConfiguration config,
            DateTime startTime)
        {
            var header = $"""
                {LOG_SEPARATOR}
                  ТЕСТ PING
                {LOG_SEPARATOR}
                Время начала:    {startTime:dd.MM.yyyy HH:mm:ss}
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
            var (successfulPings, failedPings) = (0, 0);
            using var ping = new Ping();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ExecuteSinglePingAsync(
                    ping, config, options, buffer, i + 1, cancellationToken);

                responseTimes.AppendLine(result.Message);

                if (result.IsSuccess)
                {
                    await AddRoundtripTimeAsync(result.RoundtripTime, cancellationToken);
                    successfulPings++;
                }
                else
                {
                    failedPings++;
                }

                OnProgressUpdate?.Invoke(i + 1, config.PingCount);

                var delay = CalculateDelay(config.Timeout, result.ElapsedMilliseconds);
                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            _logger.Information(
                "[PingService] Выполнение пинг-тестов завершено. Успешных: {SuccessfulPings}, Неудачных: {FailedPings}",
                successfulPings, failedPings);

            return (successfulPings, failedPings);
        }

        private async Task<PingExecutionResult> ExecuteSinglePingAsync(
            Ping ping,
            IPingConfiguration config,
            PingOptions options,
            byte[] buffer,
            int currentPing,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await ping.SendPingAsync(config.Url, config.Timeout, buffer, options);
                stopwatch.Stop();

                var resultLine = FormatPingResult(reply, config.Url, currentPing, config.PingCount);
                OnPingResult?.Invoke(resultLine + Environment.NewLine);

                _logger.Information(
                    "[PingService] Пинг {CurrentPing}/{TotalPings} к {Url} завершен. Статус: {Status}, Время: {RoundtripTime} мс",
                    currentPing, config.PingCount, config.Url, reply.Status, reply.RoundtripTime);

                return new PingExecutionResult(
                    reply.Status == IPStatus.Success,
                    (int)reply.RoundtripTime,
                    resultLine,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (PingException ex)
            {
                stopwatch.Stop();
                _logger.Error(ex,
                    "[PingService] Ошибка пинга {CurrentPing}/{TotalPings} к {Url}",
                    currentPing, config.PingCount, config.Url);

                var errorMessage = FormatPingError(config.Url, currentPing, config.PingCount, ex.Message);
                OnPingResult?.Invoke(errorMessage + Environment.NewLine);
                return new PingExecutionResult(false, 0, errorMessage, stopwatch.ElapsedMilliseconds);
            }
        }

        private static string FormatPingResult(PingReply reply, string url, int currentPing, int totalPings)
        {
            if (reply.Status == IPStatus.Success)
            {
                return $"""
                    [{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ответ от {url}:
                        Время: {reply.RoundtripTime,4} мс
                        TTL:   {reply.Options?.Ttl ?? 0}
                        Размер:{reply.Buffer?.Length ?? 0} байт
                    """;
            }

            return $"""
                [{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ошибка пинга {url}:
                    Статус: {reply.Status}
                """;
        }

        private static string FormatPingError(string url, int currentPing, int totalPings, string error) =>
            $"""
            [{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Критическая ошибка пинга {url}:
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

                var jitters = new List<double>(_roundtripTimes.Count - 1);
                for (var i = 1; i < _roundtripTimes.Count; i++)
                {
                    jitters.Add(Math.Abs(_roundtripTimes[i] - _roundtripTimes[i - 1]));
                }

                var avgJitter = Math.Round(jitters.Average(), 2);
                _logger.Information("[PingService] Рассчитан средний джиттер: {AvgJitter} мс", avgJitter);
                return avgJitter;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_roundtripTimes.Count == 0)
                    return (0, 0, 0);

                var min = _roundtripTimes.Min();
                var max = _roundtripTimes.Max();
                var avg = _roundtripTimes.Average();

                _logger.Information(
                    "[PingService] Рассчитана статистика времени: Минимальное: {Min} мс, Максимальное: {Max} мс, Среднее: {Avg:F2} мс",
                    min, max, avg);

                return (min, max, avg);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<string> GenerateFinalReport(
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
            var stats = await CalculateStatisticsAsync(CancellationToken.None);
            var lossPercentage = totalPings > 0
                ? (failedPings * 100.0 / totalPings).ToString("F2")
                : "0.00";

            var summary = $"""
                {LOG_SEPARATOR}
                  ИТОГИ ТЕСТИРОВАНИЯ
                {LOG_SEPARATOR}
                Время начала:   {startTime:dd.MM.yyyy HH:mm:ss}
                Время конца:    {endTime:dd.MM.yyyy HH:mm:ss}
                Длительность:   {FormatExecutionTime(executionTime)}

                {LOG_MINI_SEPARATOR}
                Статистика пакетов:
                    Всего отправлено: {totalPings}
                    Успешно:         {successfulPings}
                    Потеряно:        {failedPings} ({lossPercentage}%)

                {LOG_MINI_SEPARATOR}
                Статистика времени:
                    Минимальное:     {stats.Min} мс
                    Максимальное:    {stats.Max} мс
                    Среднее:         {stats.Average:F2} мс
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
    }

    public class PingConfiguration : IPingConfiguration
    {
        public string Url { get; }
        public int PingCount { get; }
        public int Timeout { get; }
        public bool DontFragment { get; }

        public PingConfiguration(
            string url,
            int pingCount,
            int timeout,
            bool dontFragment = true)
        {
            var logger = new SerilogLoggingService();
            var errors = ValidateParameters(url, pingCount, timeout, logger);

            if (errors.Any())
            {
                throw new ArgumentException(
                    "Invalid configuration parameters: " +
                    string.Join(", ", errors));
            }

            Url = url;
            PingCount = pingCount;
            Timeout = timeout;
            DontFragment = dontFragment;
        }

        public void Validate() { } // Validation is performed in constructor

        private static IEnumerable<string> ValidateParameters(
            string url,
            int pingCount,
            int timeout,
            ILoggingService logger)
        {
            var errors = new List<string>();

            errors.AddRange(ValidationHelper.ValidateUrl(url, logger));
            errors.AddRange(ValidationHelper.ValidatePingCount(pingCount.ToString(), logger));
            errors.AddRange(ValidationHelper.ValidateTimeout(timeout.ToString(), logger));

            return errors;
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

    internal readonly record struct PingExecutionResult(
        bool IsSuccess,
        int RoundtripTime,
        string Message,
        long ElapsedMilliseconds);
}