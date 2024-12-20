#nullable enable

namespace PingTestTool
{
    #region Constants
    public static class PingServiceConstants
    {
        public const int BUFFER_SIZE = 32;
        public const string TIME_FORMAT = "HH:mm:ss";
        public const string DATE_TIME_FORMAT = "dd.MM.yyyy HH:mm:ss";
        public const string LOG_SEPARATOR = "══════════════════════════════════════════════════════════";
        public const string LOG_MINI_SEPARATOR = "──────────────────────────────────────";
    }
    #endregion

    #region Interfaces
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

    public interface IPingExecutor
    {
        Task<PingExecutionResult> ExecuteSinglePingAsync(
            string url,
            int timeout,
            PingOptions options,
            byte[] buffer,
            int currentPing,
            int totalPings,
            CancellationToken cancellationToken);
    }

    public interface IStatisticsCalculator
    {
        Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> roundtripTimes);
        Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> roundtripTimes);
    }

    public interface IReportGenerator
    {
        void InitializeLogBuilder(StringBuilder logBuilder, IPingConfiguration config, DateTime startTime);
        Task<string> GenerateFinalReport(
            StringBuilder logBuilder,
            StringBuilder responseTimes,
            DateTime startTime,
            DateTime endTime,
            TimeSpan executionTime,
            double avgJitter,
            int successfulPings,
            int failedPings,
            int totalPings,
            IReadOnlyList<int> roundtripTimes);
    }
    #endregion

    #region Implementation Classes
    public sealed class PingService : IPingTestService
    {
        private readonly List<int> _roundtripTimes = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ILoggingService _logger;
        private readonly IPingExecutor _pingExecutor;
        private readonly IStatisticsCalculator _statisticsCalculator;
        private readonly IReportGenerator _reportGenerator;
        private bool _isDisposed;

        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;

        public PingService(
            ILoggingService? logger = null,
            IPingExecutor? pingExecutor = null,
            IStatisticsCalculator? statisticsCalculator = null,
            IReportGenerator? reportGenerator = null)
        {
            _logger = logger ?? new SerilogLoggingService();
            _pingExecutor = pingExecutor ?? new PingExecutor(_logger);
            _statisticsCalculator = statisticsCalculator ?? new StatisticsCalculator(_logger);
            _reportGenerator = reportGenerator ?? new ReportGenerator(_logger);
        }

        public async Task<IPingTestResult> StartPingTestAsync(
            IPingConfiguration config,
            CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);

            var startTime = DateTime.Now;
            var logBuilder = new StringBuilder();

            _reportGenerator.InitializeLogBuilder(logBuilder, config, startTime);
            OnPingResult?.Invoke(logBuilder.ToString());

            try
            {
                var testResults = await ExecutePingTestsAsync(config, cancellationToken);
                var endTime = DateTime.Now;
                var executionTime = endTime - startTime;

                var roundtripTimes = await GetRoundtripTimesAsync(cancellationToken);
                var avgJitter = await _statisticsCalculator.CalculateAverageJitterAsync(roundtripTimes);

                var finalLog = await _reportGenerator.GenerateFinalReport(
                    new StringBuilder(), // Используем новый StringBuilder для финального отчета
                    testResults.ResponseTimes,
                    startTime,
                    endTime,
                    executionTime,
                    avgJitter,
                    testResults.SuccessfulPings,
                    testResults.FailedPings,
                    config.PingCount,
                    roundtripTimes);

                // Отправляем финальный отчет через событие
                OnPingResult?.Invoke(Environment.NewLine + finalLog);

                return new PingTestResult(
                    testResults.SuccessfulPings,
                    testResults.FailedPings,
                    executionTime,
                    avgJitter,
                    roundtripTimes,
                    logBuilder.ToString() + Environment.NewLine + finalLog);
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

        private async Task<(int SuccessfulPings, int FailedPings, StringBuilder ResponseTimes)> ExecutePingTestsAsync(
            IPingConfiguration config,
            CancellationToken cancellationToken)
        {
            var (successfulPings, failedPings) = (0, 0);
            var responseTimes = new StringBuilder();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[PingServiceConstants.BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _pingExecutor.ExecuteSinglePingAsync(
                    config.Url,
                    config.Timeout,
                    options,
                    buffer,
                    i + 1,
                    config.PingCount,
                    cancellationToken);

                responseTimes.AppendLine(result.Message);
                OnPingResult?.Invoke(result.Message + Environment.NewLine);

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

            return (successfulPings, failedPings, responseTimes);
        }

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

        private static int CalculateDelay(int timeout, long elapsedMilliseconds) =>
            Math.Max(0, timeout - (int)elapsedMilliseconds);

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
    }

    public class PingExecutor : IPingExecutor
    {
        private readonly ILoggingService _logger;

        public PingExecutor(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<PingExecutionResult> ExecuteSinglePingAsync(
            string url,
            int timeout,
            PingOptions options,
            byte[] buffer,
            int currentPing,
            int totalPings,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var ping = new Ping();

            try
            {
                var reply = await ping.SendPingAsync(url, timeout, buffer, options);
                stopwatch.Stop();

                var resultLine = FormatPingResult(reply, url, currentPing, totalPings);

                _logger.Information(
                    "[PingService] Пинг {CurrentPing}/{TotalPings} к {Url} завершен. Статус: {Status}, Время: {RoundtripTime} мс",
                    currentPing, totalPings, url, reply.Status, reply.RoundtripTime);

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
                    currentPing, totalPings, url);

                var errorMessage = FormatPingError(url, currentPing, totalPings, ex.Message);
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
    }

    public class StatisticsCalculator : IStatisticsCalculator
    {
        private readonly ILoggingService _logger;

        public StatisticsCalculator(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> roundtripTimes)
        {
            if (roundtripTimes.Count <= 1) return Task.FromResult(0.0);

            var jitters = new List<double>(roundtripTimes.Count - 1);
            for (var i = 1; i < roundtripTimes.Count; i++)
            {
                jitters.Add(Math.Abs(roundtripTimes[i] - roundtripTimes[i - 1]));
            }

            var avgJitter = Math.Round(jitters.Average(), 2);
            _logger.Information("[PingService] Рассчитан средний джиттер: {AvgJitter} мс", avgJitter);
            return Task.FromResult(avgJitter);
        }

        public Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> roundtripTimes)
        {
            if (roundtripTimes.Count == 0)
                return Task.FromResult((0, 0, 0.0));

            var min = roundtripTimes.Min();
            var max = roundtripTimes.Max();
            var avg = roundtripTimes.Average();

            _logger.Information(
                "[PingService] Рассчитана статистика времени: Минимальное: {Min} мс, Максимальное: {Max} мс, Среднее: {Avg:F2} мс",
                min, max, avg);

            return Task.FromResult((min, max, avg));
        }
    }

    public class ReportGenerator : IReportGenerator
    {
        private readonly ILoggingService _logger;
        private readonly IStatisticsCalculator _statisticsCalculator;

        public ReportGenerator(
            ILoggingService logger,
            IStatisticsCalculator? statisticsCalculator = null)
        {
            _logger = logger;
            _statisticsCalculator = statisticsCalculator ?? new StatisticsCalculator(logger);
        }

        public void InitializeLogBuilder(StringBuilder logBuilder, IPingConfiguration config, DateTime startTime)
        {
            var header = $"""
                {PingServiceConstants.LOG_SEPARATOR}
                  ТЕСТ PING
                {PingServiceConstants.LOG_SEPARATOR}
                Время начала:    {startTime:dd.MM.yyyy HH:mm:ss}
                Хост:           {config.Url}
                Кол-во пингов:  {config.PingCount}
                Таймаут:        {config.Timeout} мс
                Не фрагментировать: {(config.DontFragment ? "Да" : "Нет")}
                {PingServiceConstants.LOG_SEPARATOR}

                """;

            logBuilder.Append(header);
            _logger.Information("[PingService] Инициализация логгера для теста PING.");
        }

        public async Task<string> GenerateFinalReport(
            StringBuilder logBuilder,
            StringBuilder responseTimes,
            DateTime startTime,
            DateTime endTime,
            TimeSpan executionTime,
            double avgJitter,
            int successfulPings,
            int failedPings,
            int totalPings,
            IReadOnlyList<int> roundtripTimes)
        {
            var stats = await _statisticsCalculator.CalculateStatisticsAsync(roundtripTimes);
            var lossPercentage = totalPings > 0
                ? (failedPings * 100.0 / totalPings).ToString("F2")
                : "0.00";

            var summary = $"""
                {PingServiceConstants.LOG_SEPARATOR}
                  ИТОГИ ТЕСТИРОВАНИЯ
                {PingServiceConstants.LOG_SEPARATOR}
                Время начала:   {startTime:dd.MM.yyyy HH:mm:ss}
                Время конца:    {endTime:dd.MM.yyyy HH:mm:ss}
                Длительность:   {FormatExecutionTime(executionTime)}

                {PingServiceConstants.LOG_MINI_SEPARATOR}
                Статистика пакетов:
                    Всего отправлено: {totalPings}
                    Успешно:         {successfulPings}
                    Потеряно:        {failedPings} ({lossPercentage}%)

                {PingServiceConstants.LOG_MINI_SEPARATOR}
                Статистика времени:
                    Минимальное:     {stats.Min} мс
                    Максимальное:    {stats.Max} мс
                    Среднее:         {stats.Average:F2} мс
                    Джиттер:         {avgJitter:F2} мс
                {PingServiceConstants.LOG_SEPARATOR}
                """;

            logBuilder.AppendLine(responseTimes.ToString())
                .AppendLine(summary);

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
    }
    #endregion

    #region Model Classes
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

    public readonly record struct PingExecutionResult(
        bool IsSuccess,
        int RoundtripTime,
        string Message,
        long ElapsedMilliseconds);
    #endregion
}