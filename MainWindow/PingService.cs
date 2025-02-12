#nullable enable

namespace PingTestTool
{
    public static class PingServiceConstants
    {
        public const int BUFFER_SIZE = 32;
        public const string TIME_FORMAT = "HH:mm:ss";
        public const string DATE_TIME_FORMAT = "dd.MM.yyyy HH:mm:ss";
        public const string LOG_SEPARATOR = "══════════════════════════════════════════════════════════";
        public const string LOG_MINI_SEPARATOR = "──────────────────────────────────────";
    }

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

    public sealed class PingService : IPingTestService
    {
        private readonly List<int> _roundtripTimes = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly IPingExecutor _pingExecutor;
        private readonly IStatisticsCalculator _statisticsCalculator;
        private readonly IReportGenerator _reportGenerator;
        private bool _isDisposed;

        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;

        public PingService(IPingExecutor? pingExecutor = null, IStatisticsCalculator? statisticsCalculator = null, IReportGenerator? reportGenerator = null)
        {
            _pingExecutor = pingExecutor ?? new PingExecutor();
            _statisticsCalculator = statisticsCalculator ?? new StatisticsCalculator();
            _reportGenerator = reportGenerator ?? new ReportGenerator();
        }

        public async Task<IPingTestResult> StartPingTestAsync(IPingConfiguration config, CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            var startTime = DateTime.Now;
            var logBuilder = new StringBuilder();
            _reportGenerator.InitializeLogBuilder(logBuilder, config, startTime);
            OnPingResult?.Invoke(logBuilder.ToString());

            var (successfulPings, failedPings, responseTimes) = await ExecutePingTestsAsync(config, cancellationToken);
            var endTime = DateTime.Now;
            var executionTime = endTime - startTime;
            var roundtripTimes = await GetRoundtripTimesAsync(cancellationToken);
            var avgJitter = await _statisticsCalculator.CalculateAverageJitterAsync(roundtripTimes);
            var finalLog = await _reportGenerator.GenerateFinalReport(new StringBuilder(), responseTimes, startTime, endTime, executionTime, avgJitter, successfulPings, failedPings, config.PingCount, roundtripTimes);
            OnPingResult?.Invoke(Environment.NewLine + finalLog);

            return new PingTestResult(successfulPings, failedPings, executionTime, avgJitter, roundtripTimes, logBuilder.ToString() + Environment.NewLine + finalLog);
        }

        private async Task<(int SuccessfulPings, int FailedPings, StringBuilder ResponseTimes)> ExecutePingTestsAsync(IPingConfiguration config, CancellationToken cancellationToken)
        {
            int successfulPings = 0, failedPings = 0;
            var responseTimes = new StringBuilder();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[PingServiceConstants.BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _pingExecutor.ExecuteSinglePingAsync(config.Url, config.Timeout, options, buffer, i + 1, config.PingCount, cancellationToken);
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
                var delay = Math.Max(0, config.Timeout - (int)result.ElapsedMilliseconds);
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken);
            }
            return (successfulPings, failedPings, responseTimes);
        }

        private async Task AddRoundtripTimeAsync(int roundtripTime, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _roundtripTimes.Add(roundtripTime);
                OnRoundtripTimeAdded?.Invoke(roundtripTime);
            }
            finally { _lock.Release(); }
        }

        public async Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try { _roundtripTimes.Clear(); }
            finally { _lock.Release(); }
        }

        public async Task<IReadOnlyList<int>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try { return _roundtripTimes.ToList().AsReadOnly(); }
            finally { _lock.Release(); }
        }

        private async Task ThrowIfDisposedAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(PingService));
            }
            finally { _lock.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed)
            {
                await ClearRoundtripTimesAsync();
                _lock.Dispose();
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    public class PingExecutor : IPingExecutor
    {
        public async Task<PingExecutionResult> ExecuteSinglePingAsync(string url, int timeout, PingOptions options, byte[] buffer, int currentPing, int totalPings, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(url, timeout, buffer, options);
                stopwatch.Stop();
                string resultLine = reply.Status == IPStatus.Success
                    ? $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ответ от {url}:\n    Время: {reply.RoundtripTime,4} мс\n    TTL:   {reply.Options?.Ttl ?? 0}\n    Размер:{reply.Buffer?.Length ?? 0} байт"
                    : $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ошибка пинга {url}:\n    Статус: {reply.Status}";
                return new PingExecutionResult(reply.Status == IPStatus.Success, (int)reply.RoundtripTime, resultLine, stopwatch.ElapsedMilliseconds);
            }
            catch (PingException ex)
            {
                stopwatch.Stop();
                string errorMessage = $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Критическая ошибка пинга {url}:\n    {ex.Message}";
                return new PingExecutionResult(false, 0, errorMessage, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    public class StatisticsCalculator : IStatisticsCalculator
    {
        public Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> roundtripTimes)
        {
            if (roundtripTimes.Count <= 1)
                return Task.FromResult(0.0);
            double avgJitter = Math.Round(
                Enumerable.Range(1, roundtripTimes.Count - 1)
                          .Select(i => Math.Abs(roundtripTimes[i] - roundtripTimes[i - 1]))
                          .Average(), 2);
            return Task.FromResult(avgJitter);
        }

        public Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> roundtripTimes)
        {
            if (roundtripTimes.Count == 0)
                return Task.FromResult((0, 0, 0.0));
            int min = roundtripTimes.Min();
            int max = roundtripTimes.Max();
            double avg = roundtripTimes.Average();
            return Task.FromResult((min, max, avg));
        }
    }

    public class ReportGenerator : IReportGenerator
    {
        public void InitializeLogBuilder(StringBuilder logBuilder, IPingConfiguration config, DateTime startTime)
        {
            logBuilder.AppendLine(PingServiceConstants.LOG_SEPARATOR)
                      .AppendLine("  ТЕСТ PING")
                      .AppendLine(PingServiceConstants.LOG_SEPARATOR)
                      .AppendLine($"Время начала:    {startTime:dd.MM.yyyy HH:mm:ss}")
                      .AppendLine($"Хост:           {config.Url}")
                      .AppendLine($"Кол-во пингов:  {config.PingCount}")
                      .AppendLine($"Таймаут:        {config.Timeout} мс")
                      .AppendLine($"Не фрагментировать: {(config.DontFragment ? "Да" : "Нет")}")
                      .AppendLine(PingServiceConstants.LOG_SEPARATOR)
                      .AppendLine();
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
            var stats = await new StatisticsCalculator().CalculateStatisticsAsync(roundtripTimes);
            var lossPercentage = totalPings > 0 ? (failedPings * 100.0 / totalPings).ToString("F2") : "0.00";

            logBuilder
                .AppendLine(PingServiceConstants.LOG_SEPARATOR)
                .AppendLine("  ИТОГИ ТЕСТИРОВАНИЯ")
                .AppendLine(PingServiceConstants.LOG_SEPARATOR)
                .AppendLine($"Время начала:   {startTime:dd.MM.yyyy HH:mm:ss}")
                .AppendLine($"Время конца:    {endTime:dd.MM.yyyy HH:mm:ss}")
                .AppendLine($"Длительность:   {FormatExecutionTime(executionTime)}")
                .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
                .AppendLine("Статистика пакетов:")
                .AppendLine($"    Всего отправлено: {totalPings}")
                .AppendLine($"    Успешно:         {successfulPings}")
                .AppendLine($"    Потеряно:        {failedPings} ({lossPercentage}%)")
                .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
                .AppendLine("Статистика времени:")
                .AppendLine($"    Минимальное:     {stats.Min} мс")
                .AppendLine($"    Максимальное:    {stats.Max} мс")
                .AppendLine($"    Среднее:         {stats.Average:F2} мс")
                .AppendLine($"    Джиттер:         {avgJitter:F2} мс")
                .AppendLine(PingServiceConstants.LOG_SEPARATOR);
            return logBuilder.ToString();
        }

        private static string FormatExecutionTime(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{time.TotalHours:F2} часов" :
            time.TotalMinutes >= 1 ? $"{time.TotalMinutes:F2} минут" :
            $"{time.TotalSeconds:F2} секунд";
    }

    public class PingConfiguration : IPingConfiguration
    {
        public string Url { get; }
        public int PingCount { get; }
        public int Timeout { get; }
        public bool DontFragment { get; }

        public PingConfiguration(string url, int pingCount, int timeout, bool dontFragment = true)
        {
            var errors = ValidateParameters(url, pingCount, timeout);
            if (errors.Any())
                throw new ArgumentException("Invalid configuration parameters: " + string.Join(", ", errors));
            Url = url;
            PingCount = pingCount;
            Timeout = timeout;
            DontFragment = dontFragment;
        }

        public void Validate() { }

        private static IEnumerable<string> ValidateParameters(string url, int pingCount, int timeout)
        {
            var errors = new List<string>();
            errors.AddRange(ValidationHelper.ValidateUrl(url));
            errors.AddRange(ValidationHelper.ValidatePingCount(pingCount.ToString()));
            errors.AddRange(ValidationHelper.ValidateTimeout(timeout.ToString()));
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

        public PingTestResult(int successfulPings, int failedPings, TimeSpan executionTime, double averageJitter, IReadOnlyList<int> roundtripTimes, string detailedLog)
        {
            SuccessfulPings = successfulPings;
            FailedPings = failedPings;
            ExecutionTime = executionTime;
            AverageJitter = averageJitter;
            RoundtripTimes = roundtripTimes;
            DetailedLog = detailedLog;
        }
    }

    public readonly record struct PingExecutionResult(bool IsSuccess, int RoundtripTime, string Message, long ElapsedMilliseconds);
}

public static class ValidationHelper
{
    public static IEnumerable<string> ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            yield return "URL is empty";
        else if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            yield return "URL is not valid";
    }

    public static IEnumerable<string> ValidatePingCount(string pingCount)
    {
        if (!int.TryParse(pingCount, out var count) || count <= 0)
            yield return "PingCount must be a positive integer";
    }

    public static IEnumerable<string> ValidateTimeout(string timeout)
    {
        if (!int.TryParse(timeout, out var t) || t <= 0)
            yield return "Timeout must be a positive integer";
    }
}
