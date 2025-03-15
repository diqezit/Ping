#nullable enable

namespace PingTestTool
{
    #region Constants
    public static class PingServiceConstants
    {
        public const int BUFFER_SIZE = 32;
        public const string TIME_FORMAT = "HH:mm:ss",
                          DATE_TIME_FORMAT = "dd.MM.yyyy HH:mm:ss",
                          LOG_SEPARATOR = "══════════════════════════════════════════════════════════",
                          LOG_MINI_SEPARATOR = "──────────────────────────────────────";
    }
    #endregion

    #region Interfaces
    public interface IMainWindowHandler
    {
        void HandleWindowClosed(object? sender, EventArgs e);
        Task HandlePingButtonClickAsync();
        void HandleStopButtonClick();
        Task HandleShowGraphButtonClickAsync();
        void HandleTraceRouteButtonClick();
        void HideWarningsOnStartup();
    }

    public interface IInputValidator
    {
        ValidationResult ValidateInput(string url, string pingCount, string timeout);
    }

    public interface IWarningPresenter
    {
        void HideAllWarnings();
        void ShowWarnings(ValidationResult result);
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
        Task<IReadOnlyList<(DateTime Time, int RoundtripTime)>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default);
    }

    public interface IPingExecutor
    {
        Task<PingExecutionResult> ExecuteSinglePingAsync(string url, int timeout, PingOptions options, byte[] buffer, int currentPing, int totalPings, CancellationToken cancellationToken);
    }

    public interface IStatisticsCalculator
    {
        Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> roundtripTimes);
        Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> roundtripTimes);
    }

    public interface IReportGenerator
    {
        void InitializeLogBuilder(StringBuilder logBuilder, IPingConfiguration config, DateTime startTime);
        Task<string> GenerateFinalReport(StringBuilder logBuilder, StringBuilder responseTimes, DateTime startTime, DateTime endTime, TimeSpan executionTime, double avgJitter, int successfulPings, int failedPings, int totalPings, IReadOnlyList<int> roundtripTimes);
    }
    #endregion

    #region Validation
    public sealed class ValidationResult
    {
        public List<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
        public ValidationResult(List<string> errors) => Errors = errors ?? new();
    }

    public class InputValidator : IInputValidator
    {
        public ValidationResult ValidateInput(string url, string pingCount, string timeout) =>
            new(ValidationHelper.ValidateUrl(url)
                .Concat(ValidationHelper.ValidatePingCount(pingCount))
                .Concat(ValidationHelper.ValidateTimeout(timeout))
                .ToList());
    }

    public static class ValidationHelper
    {
        private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
        private const int MIN_TIMEOUT = 100, MIN_PING_COUNT = 1, MAX_PING_COUNT = 1000;

        public static List<string> ValidateUrl(string url) =>
            string.IsNullOrWhiteSpace(url) ? new() { FindResourceString("UrlEmptyError") } :
            CyrillicRegex.IsMatch(url) ? new() { FindResourceString("UrlCyrillicError") } : new();

        public static List<string> ValidatePingCount(string pingCount) =>
            !int.TryParse(pingCount, out int count) || count < MIN_PING_COUNT || count > MAX_PING_COUNT ?
                new() { string.Format(FindResourceString("PingCountRangeError"), MIN_PING_COUNT, MAX_PING_COUNT) } : new();

        public static List<string> ValidateTimeout(string timeout) =>
            !int.TryParse(timeout, out int time) || time < MIN_TIMEOUT ?
                new() { string.Format(FindResourceString("TimeoutMinimumError"), MIN_TIMEOUT) } : new();

        private static string FindResourceString(string resourceKey) =>
             Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
    #endregion

    #region Warning Presentation
    public class WarningPresenter : IWarningPresenter
    {
        private readonly Image[] _warnings;

        public WarningPresenter(params Image[] warnings) =>
            _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));

        public void HideAllWarnings() =>
            Array.ForEach(_warnings, img => img.Visibility = Visibility.Collapsed);

        public void ShowWarnings(ValidationResult result)
        {
            if (!result.IsValid && _warnings.FirstOrDefault() is Image warning)
            {
                warning.Visibility = Visibility.Visible;
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors),
                    FindResourceString("InputErrorCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static string FindResourceString(string resourceKey) =>
            Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
    #endregion

    #region Main Window Event Handler
    public class MainWindowEventHandler : IMainWindowHandler
    {
        #region Constants
        private const string BTN_START_TEXT_KEY = "BtnStartText",
                             BTN_WAIT_TEXT_KEY = "BtnWaitText",
                             ERROR_NO_DATA_KEY = "ErrorNoGraphData",
                             ERROR_NO_URL_KEY = "ErrorNoUrlForTrace";
        #endregion

        #region Fields
        private readonly MainWindow _window;
        private readonly IPingTestService _pingService;
        private readonly IInputValidator _validator;
        private readonly IWarningPresenter _warningPresenter;
        private CancellationTokenSource? _cts;
        private IGraphWindow? _graphWindow;
        #endregion

        #region Constructor
        public MainWindowEventHandler(MainWindow window, IPingTestService pingService, IInputValidator validator, IWarningPresenter warningPresenter)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _warningPresenter = warningPresenter ?? throw new ArgumentNullException(nameof(warningPresenter));

            HideWarningsOnStartup();
            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            _pingService.OnPingResult += HandlePingResult;
            _pingService.OnProgressUpdate += HandleProgressUpdate;
            _pingService.OnRoundtripTimeAdded += HandleRoundtripTimeAdded;
        }

        private void HandlePingResult(string result) =>
            _window.Dispatcher.Invoke(() => _window.txtResults.AppendText(result));

        private void HandleProgressUpdate(int current, int total) =>
            _window.Dispatcher.Invoke(() => _window.progressBar.Value = (current * 100.0) / total);

        private void HandleRoundtripTimeAdded(int roundtripTime) =>
            _window.Dispatcher.Invoke(async () =>
            {
                if (_graphWindow != null)
                {
                    var times = await _pingService.GetRoundtripTimesAsync();
                    _graphWindow.SetPingData(times.ToList());
                }
            });
        #endregion

        #region Window Event Handlers
        public void HandleWindowClosed(object? sender, EventArgs e)
        {
            if (sender == _window)
            {
                _graphWindow?.Close();
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            else if (sender is IGraphWindow)
                _graphWindow = null;
        }
        #endregion

        #region Button Click Handlers
        public async Task HandlePingButtonClickAsync()
        {
            try
            {
                if (_window.btnPing.Content.ToString() == FindResourceString(BTN_START_TEXT_KEY))
                {
                    _warningPresenter.HideAllWarnings();
                    var result = _validator.ValidateInput(_window.txtURL.Text, _window.txtPingCount.Text, _window.txtTimeout.Text);

                    if (result.IsValid)
                        await ExecutePingTest();
                    else
                        _warningPresenter.ShowWarnings(result);
                }
            }
            catch (OperationCanceledException) { /* Cancelled operation - no action needed */ }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{FindResourceString("GenericError")}: {ex.Message}",
                    FindResourceString("ErrorCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void HandleStopButtonClick() => _cts?.Cancel();

        public async Task HandleShowGraphButtonClickAsync()
        {
            var pingData = await _pingService.GetRoundtripTimesAsync();

            if (pingData.Count > 0)
                HandleGraphWindow(pingData.ToList());
            else
                MessageBox.Show(
                    FindResourceString(ERROR_NO_DATA_KEY),
                    FindResourceString("ErrorCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
        }

        public void HandleTraceRouteButtonClick()
        {
            if (string.IsNullOrWhiteSpace(_window.txtURL.Text))
            {
                MessageBox.Show(
                    FindResourceString(ERROR_NO_URL_KEY),
                    FindResourceString("ErrorCaption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                _window.tabControlMain.SelectedIndex = 1;
            }
        }
        #endregion

        #region Graph Window Management
        private void HandleGraphWindow(List<(DateTime Time, int RoundtripTime)> pingData)
        {
            if (_graphWindow == null || !_graphWindow.IsLoaded)
                CreateNewGraphWindow(pingData);
            else
            {
                _graphWindow.WindowState = _graphWindow.WindowState == WindowState.Minimized
                    ? WindowState.Normal
                    : WindowState.Minimized;
                _graphWindow.SetPingData(pingData);
            }
        }

        private void CreateNewGraphWindow(List<(DateTime Time, int RoundtripTime)> pingData)
        {
            if (int.TryParse(_window.txtPingCount.Text, out int pingInterval))
            {
                _graphWindow = new GraphWindow(pingInterval);
                _graphWindow.SetPingData(pingData);
                _graphWindow.Closed += HandleWindowClosed;
                _graphWindow.Show();
            }
        }
        #endregion

        #region Ping Test Execution
        private async Task ExecutePingTest()
        {
            UpdateUIForTestStart();
            _cts = new CancellationTokenSource();

            try { await ExecutePingTestCore(); }
            catch (OperationCanceledException) { /* Cancelled operation - no action needed */ }
            finally { ResetUIAfterTest(); }
        }

        private async Task ExecutePingTestCore()
        {
            if (int.TryParse(_window.txtPingCount.Text, out int pingCount) &&
                int.TryParse(_window.txtTimeout.Text, out int timeout))
            {
                await _pingService.ClearRoundtripTimesAsync();
                var config = new PingConfiguration(_window.txtURL.Text, pingCount, timeout);
                await _pingService.StartPingTestAsync(config, _cts!.Token);
            }
        }
        #endregion

        #region UI Updates
        private void UpdateUIForTestStart()
        {
            _window.btnPing.IsEnabled = false;
            _window.btnStop.IsEnabled = true;
            _window.btnPing.Content = FindResourceString(BTN_WAIT_TEXT_KEY);
            _window.progressBar.Value = 0;
        }

        private void ResetUIAfterTest()
        {
            _window.btnPing.IsEnabled = true;
            _window.btnPing.Content = FindResourceString(BTN_START_TEXT_KEY);
            _window.btnStop.IsEnabled = false;
            _window.progressBar.Value = 0;
        }

        public void HideWarningsOnStartup() => _warningPresenter.HideAllWarnings();
        #endregion

        private static string FindResourceString(string resourceKey) =>
            Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
    #endregion

    #region Ping Service
    public class PingService : IPingTestService
    {
        #region Fields
        private readonly List<(DateTime Time, int RoundtripTime)> _pingData = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly IPingExecutor _executor;
        private readonly IStatisticsCalculator _statsCalc;
        private readonly IReportGenerator _reportGen;
        private bool _disposed;
        #endregion

        #region Events
        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;
        #endregion

        #region Constructor & Dispose
        public PingService(IPingExecutor? executor = null, IStatisticsCalculator? statsCalc = null, IReportGenerator? reportGen = null)
        {
            _executor = executor ?? new PingExecutor();
            _statsCalc = statsCalc ?? new StatisticsCalculator();
            _reportGen = reportGen ?? new ReportGenerator();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await ClearRoundtripTimesAsync();
                _lock.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
        #endregion

        #region Ping Test Execution
        public async Task<IPingTestResult> StartPingTestAsync(IPingConfiguration config, CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            var startTime = DateTime.Now;
            var logBuilder = new StringBuilder();

            _reportGen.InitializeLogBuilder(logBuilder, config, startTime);
            OnPingResult?.Invoke(logBuilder.ToString());

            var (success, fail, responseTimes) = await ExecutePingTestsAsync(config, cancellationToken);
            var endTime = DateTime.Now;
            var execTime = endTime - startTime;
            var roundtripTimes = await GetRoundtripTimesAsync(cancellationToken);
            var rtTimes = roundtripTimes.Select(x => x.RoundtripTime).ToList();

            var avgJitter = await _statsCalc.CalculateAverageJitterAsync(rtTimes);
            var finalLog = await _reportGen.GenerateFinalReport(
                new StringBuilder(), responseTimes, startTime, endTime,
                execTime, avgJitter, success, fail, config.PingCount, rtTimes);

            OnPingResult?.Invoke(Environment.NewLine + finalLog);

            return new PingTestResult(
                success, fail, execTime, avgJitter, rtTimes,
                logBuilder.ToString() + Environment.NewLine + finalLog);
        }

        private async Task<(int success, int fail, StringBuilder responseTimes)> ExecutePingTestsAsync(
            IPingConfiguration config, CancellationToken cancellationToken)
        {
            int success = 0, fail = 0;
            var responseTimes = new StringBuilder();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[PingServiceConstants.BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _executor.ExecuteSinglePingAsync(
                    config.Url, config.Timeout, options, buffer,
                    i + 1, config.PingCount, cancellationToken);

                responseTimes.AppendLine(result.Message);
                OnPingResult?.Invoke(result.Message + Environment.NewLine);

                if (result.IsSuccess)
                {
                    await AddRoundtripTimeAsync((DateTime.Now, result.RoundtripTime), cancellationToken);
                    success++;
                }
                else
                    fail++;

                OnProgressUpdate?.Invoke(i + 1, config.PingCount);

                var delay = Math.Max(0, config.Timeout - (int)result.ElapsedMilliseconds);
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken);
            }

            return (success, fail, responseTimes);
        }
        #endregion

        #region Roundtrip Time Management
        private async Task AddRoundtripTimeAsync((DateTime Time, int RoundtripTime) pingData, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _pingData.Add(pingData);
                OnRoundtripTimeAdded?.Invoke(pingData.RoundtripTime);
            }
            finally { _lock.Release(); }
        }

        public async Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try { _pingData.Clear(); }
            finally { _lock.Release(); }
        }

        public async Task<IReadOnlyList<(DateTime Time, int RoundtripTime)>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default)
        {
            await ThrowIfDisposedAsync(cancellationToken);
            await _lock.WaitAsync(cancellationToken);
            try { return _pingData.ToList().AsReadOnly(); }
            finally { _lock.Release(); }
        }
        #endregion

        #region Disposed Check
        private async Task ThrowIfDisposedAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try { if (_disposed) throw new ObjectDisposedException(nameof(PingService)); }
            finally { _lock.Release(); }
        }
        #endregion
    }
    #endregion

    #region Ping Executor
    public class PingExecutor : IPingExecutor
    {
        public async Task<PingExecutionResult> ExecuteSinglePingAsync(
            string url, int timeout, PingOptions options, byte[] buffer,
            int currentPing, int totalPings, CancellationToken cancellationToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var ping = new Ping();

            try
            {
                var reply = await ping.SendPingAsync(url, timeout, buffer, options);
                stopwatch.Stop();

                return reply.Status == IPStatus.Success
                    ? CreateSuccessResult(url, reply, currentPing, totalPings, stopwatch.ElapsedMilliseconds)
                    : CreateFailureResult(url, reply, currentPing, totalPings, stopwatch.ElapsedMilliseconds);
            }
            catch (PingException ex)
            {
                stopwatch.Stop();
                return CreateExceptionResult(url, ex, currentPing, totalPings, stopwatch.ElapsedMilliseconds);
            }
        }

        private PingExecutionResult CreateSuccessResult(string url, PingReply reply, int currentPing, int totalPings, long elapsed) =>
            new(true, (int)reply.RoundtripTime,
                $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {FindResourceString("ReplyFrom")} {url}:\n" +
                $"  {FindResourceString("Time")}: {reply.RoundtripTime,4} {FindResourceString("Ms")}\n" +
                $"  TTL:  {reply.Options?.Ttl ?? 0}\n" +
                $"  {FindResourceString("Size")}:{reply.Buffer?.Length ?? 0} {FindResourceString("Bytes")}",
                elapsed);

        private PingExecutionResult CreateFailureResult(string url, PingReply reply, int currentPing, int totalPings, long elapsed) =>
            new(false, 0,
                $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {FindResourceString("PingError")} {url}:\n" +
                $"  {FindResourceString("Status")}: {reply.Status}",
                elapsed);

        private PingExecutionResult CreateExceptionResult(string url, PingException ex, int currentPing, int totalPings, long elapsed) =>
            new(false, 0,
                $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {FindResourceString("CriticalPingError")} {url}:\n" +
                $"  {ex.Message}",
                elapsed);

        private static string FindResourceString(string resourceKey) =>
            Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
    #endregion

    #region Statistics Calculation
    public class StatisticsCalculator : IStatisticsCalculator
    {
        public Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> times) =>
            Task.FromResult(times.Count <= 1
                ? 0.0
                : Math.Round(Enumerable.Range(1, times.Count - 1)
                    .Select(i => Math.Abs(times[i] - times[i - 1]))
                    .Average(), 2));

        public Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> times) =>
            Task.FromResult(times.Count == 0
                ? (0, 0, 0.0)
                : (times.Min(), times.Max(), times.Average()));
    }
    #endregion

    #region Report Generation
    public class ReportGenerator : IReportGenerator
    {
        public void InitializeLogBuilder(StringBuilder sb, IPingConfiguration config, DateTime startTime) =>
            sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"  {FindResourceString("PingTest").ToUpper()}")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"{FindResourceString("StartTime")}:    {startTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"{FindResourceString("Host")}:      {config.Url}")
              .AppendLine($"{FindResourceString("PingCount")}:   {config.PingCount}")
              .AppendLine($"{FindResourceString("Timeout")}:     {config.Timeout} {FindResourceString("Ms")}")
              .AppendLine($"{FindResourceString("DontFragment")}: {(config.DontFragment ? FindResourceString("Yes") : FindResourceString("No"))}")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine();

        public async Task<string> GenerateFinalReport(StringBuilder sb, StringBuilder responseTimes,
            DateTime startTime, DateTime endTime, TimeSpan execTime, double avgJitter,
            int success, int fail, int total, IReadOnlyList<int> times)
        {
            var stats = await new StatisticsCalculator().CalculateStatisticsAsync(times);
            var loss = total > 0 ? (fail * 100.0 / total).ToString("F2") : "0.00";

            sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"  {FindResourceString("TestingResults").ToUpper()}")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"{FindResourceString("StartTime")}:    {startTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"{FindResourceString("EndTime")}:      {endTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"{FindResourceString("Duration")}:      {FormatExecutionTime(execTime)}")
              .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
              .AppendLine($"{FindResourceString("PacketStatistics")}:")
              .AppendLine($"    {FindResourceString("PacketsSent")}: {total}")
              .AppendLine($"    {FindResourceString("Successful")}:     {success}")
              .AppendLine($"    {FindResourceString("Lost")}:       {fail} ({loss}%)")
              .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
              .AppendLine($"{FindResourceString("TimeStatistics")}:")
              .AppendLine($"    {FindResourceString("Minimum")}:      {stats.Min} {FindResourceString("Ms")}")
              .AppendLine($"    {FindResourceString("Maximum")}:      {stats.Max} {FindResourceString("Ms")}")
              .AppendLine($"    {FindResourceString("Average")}:        {stats.Average:F2} {FindResourceString("Ms")}")
              .AppendLine($"    {FindResourceString("Jitter")}:         {avgJitter:F2} {FindResourceString("Ms")}")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR);

            return sb.ToString();
        }

        private static string FormatExecutionTime(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{t.TotalHours:F2} {FindResourceString("Hours")}" :
            t.TotalMinutes >= 1 ? $"{t.TotalMinutes:F2} {FindResourceString("Minutes")}" :
            $"{t.TotalSeconds:F2} {FindResourceString("Seconds")}";

        private static string FindResourceString(string resourceKey) =>
            Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
    #endregion

    #region Configuration & Results
    public record PingConfiguration : IPingConfiguration
    {
        public string Url { get; }
        public int PingCount { get; }
        public int Timeout { get; }
        public bool DontFragment { get; }

        public PingConfiguration(string url, int pingCount, int timeout, bool dontFragment = true)
        {
            var errs = ValidateParameters(url, pingCount, timeout);
            if (errs.Any())
                throw new ArgumentException("Invalid configuration parameters: " + string.Join(", ", errs));

            Url = url;
            PingCount = pingCount;
            Timeout = timeout;
            DontFragment = dontFragment;
        }

        public void Validate() { }

        private static IEnumerable<string> ValidateParameters(string url, int pingCount, int timeout) =>
            ValidationHelper.ValidateUrl(url)
                .Concat(ValidationHelper.ValidatePingCount(pingCount.ToString()))
                .Concat(ValidationHelper.ValidateTimeout(timeout.ToString()));
    }

    public record PingTestResult(
        int SuccessfulPings,
        int FailedPings,
        TimeSpan ExecutionTime,
        double AverageJitter,
        IReadOnlyList<int> RoundtripTimes,
        string DetailedLog) : IPingTestResult;

    public readonly record struct PingExecutionResult(
        bool IsSuccess,
        int RoundtripTime,
        string Message,
        long ElapsedMilliseconds);
    #endregion
}