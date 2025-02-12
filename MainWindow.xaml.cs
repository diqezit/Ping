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

    public interface IMainWindowHandler
    {
        void HandleWindowClosed(object? sender, EventArgs e);
        Task HandlePingButtonClickAsync();
        void HandleStopButtonClick();
        Task HandleShowGraphButtonClickAsync();
        void HandleTraceRouteButtonClick();
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
        Task<IReadOnlyList<int>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default);
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

    public sealed class ValidationResult
    {
        public List<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
        public ValidationResult(List<string> errors) => Errors = errors ?? new();
    }

    public class InputValidator : IInputValidator
    {
        public ValidationResult ValidateInput(string url, string pingCount, string timeout)
        {
            var errors = ValidationHelper.ValidateUrl(url)
                .Concat(ValidationHelper.ValidatePingCount(pingCount))
                .Concat(ValidationHelper.ValidateTimeout(timeout))
                .ToList();
            return new ValidationResult(errors);
        }
    }

    public class WarningPresenter : IWarningPresenter
    {
        private readonly Image[] _warnings;
        public WarningPresenter(params Image[] warnings) => _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        public void HideAllWarnings() => Array.ForEach(_warnings, img => img.Visibility = Visibility.Collapsed);
        public void ShowWarnings(ValidationResult result)
        {
            if (!result.IsValid && _warnings.FirstOrDefault() is Image warning)
            {
                warning.Visibility = Visibility.Visible;
                MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public static class ValidationHelper
    {
        private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
        private const int MIN_TIMEOUT = 100, MIN_PING_COUNT = 1, MAX_PING_COUNT = 1000;
        public static List<string> ValidateUrl(string url) =>
            string.IsNullOrWhiteSpace(url) ? new() { "URL не может быть пустым." } :
            CyrillicRegex.IsMatch(url) ? new() { "URL не может иметь кириллицу." } : new();
        public static List<string> ValidatePingCount(string pingCount) =>
            !int.TryParse(pingCount, out int count) || count < MIN_PING_COUNT || count > MAX_PING_COUNT ?
                new() { $"Количество пакетов должно быть целым числом между {MIN_PING_COUNT} и {MAX_PING_COUNT}." } : new();
        public static List<string> ValidateTimeout(string timeout) =>
            !int.TryParse(timeout, out int time) || time < MIN_TIMEOUT ?
                new() { $"Таймаут должен быть целым числом не менее {MIN_TIMEOUT} мс." } : new();
    }

    public class MainWindowEventHandler : IMainWindowHandler
    {
        private readonly MainWindow _window;
        private readonly IPingTestService _pingService;
        private readonly IInputValidator _validator;
        private readonly IWarningPresenter _warningPresenter;
        private CancellationTokenSource? _cts;
        private IGraphWindow? _graphWindow;
        private ITraceWindow? _traceWindow;
        public const string BTN_START_TEXT = "Запустить тест",
                           BTN_WAIT_TEXT = "Ожидаем...",
                           ERROR_NO_DATA = "Нет данных пинга для отображения.",
                           ERROR_NO_URL = "Пожалуйста, укажите URL для трассировки.";

        public MainWindowEventHandler(MainWindow window, IPingTestService pingService, IInputValidator validator, IWarningPresenter warningPresenter)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _warningPresenter = warningPresenter ?? throw new ArgumentNullException(nameof(warningPresenter));

            _pingService.OnPingResult += s => _window.Dispatcher.Invoke(() => _window.txtResults.AppendText(s));
            _pingService.OnProgressUpdate += (current, total) => _window.Dispatcher.Invoke(() => _window.progressBar.Value = (current * 100.0) / total);
            _pingService.OnRoundtripTimeAdded += rt => _window.Dispatcher.Invoke(async () =>
            {
                if (_graphWindow != null)
                {
                    var times = await _pingService.GetRoundtripTimesAsync();
                    _graphWindow.SetPingData(times.ToList());
                }
            });
        }

        public void HandleWindowClosed(object? sender, EventArgs e)
        {
            if (sender == _window)
            {
                _graphWindow?.Close();
                _traceWindow?.Close();
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            else if (sender is IGraphWindow)
                _graphWindow = null;
        }

        public async Task HandlePingButtonClickAsync()
        {
            try
            {
                if (_window.btnPing.Content.ToString() == BTN_START_TEXT)
                {
                    _warningPresenter.HideAllWarnings();
                    var result = _validator.ValidateInput(_window.txtURL.Text, _window.txtPingCount.Text, _window.txtTimeout.Text);
                    if (result.IsValid)
                        await ExecutePingTest();
                    else
                        _warningPresenter.ShowWarnings(result);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task ExecutePingTest()
        {
            UpdateUIForTestStart();
            _cts = new CancellationTokenSource();
            try { await ExecutePingTestCore(); }
            catch (OperationCanceledException) { }
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

        private void UpdateUIForTestStart()
        {
            _window.btnPing.IsEnabled = false;
            _window.btnStop.IsEnabled = true;
            _window.btnPing.Content = BTN_WAIT_TEXT;
            _window.progressBar.Value = 0;
        }

        private void ResetUIAfterTest()
        {
            _window.btnPing.IsEnabled = true;
            _window.btnPing.Content = BTN_START_TEXT;
            _window.btnStop.IsEnabled = false;
            _window.progressBar.Value = 0;
        }

        public void HandleStopButtonClick() { _cts?.Cancel(); ResetUIAfterTest(); }

        public async Task HandleShowGraphButtonClickAsync()
        {
            var times = await _pingService.GetRoundtripTimesAsync();
            if (times.Count > 0)
                HandleGraphWindow(times.ToList());
            else
                MessageBox.Show(ERROR_NO_DATA, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void HandleGraphWindow(List<int> times)
        {
            if (_graphWindow == null || !_graphWindow.IsLoaded)
                CreateNewGraphWindow(times);
            else
            {
                _graphWindow.WindowState = _graphWindow.WindowState == WindowState.Minimized ? WindowState.Normal : WindowState.Minimized;
                _graphWindow.SetPingData(times);
            }
        }

        private void CreateNewGraphWindow(List<int> times)
        {
            if (int.TryParse(_window.txtPingCount.Text, out int pingInterval))
            {
                _graphWindow = new GraphWindow(pingInterval);
                _graphWindow.SetPingData(times);
                _graphWindow.Closed += HandleWindowClosed;
                _graphWindow.Show();
            }
        }

        public void HandleTraceRouteButtonClick()
        {
            if (string.IsNullOrWhiteSpace(_window.txtURL.Text))
                MessageBox.Show(ERROR_NO_URL, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                HandleTraceWindow();
        }

        private void HandleTraceWindow()
        {
            if (_traceWindow == null || !_traceWindow.IsLoaded)
            {
                _traceWindow = new TraceWindow(_window.txtURL.Text);
                _traceWindow.Closed += HandleWindowClosed;
                _traceWindow.Show();
            }
            else
                _traceWindow.Visibility = _traceWindow.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public class PingService : IPingTestService
    {
        private readonly List<int> _roundtripTimes = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly IPingExecutor _executor;
        private readonly IStatisticsCalculator _statsCalc;
        private readonly IReportGenerator _reportGen;
        private bool _disposed;
        public event Action<string>? OnPingResult;
        public event Action<int, int>? OnProgressUpdate;
        public event Action<int>? OnRoundtripTimeAdded;

        public PingService(IPingExecutor? executor = null, IStatisticsCalculator? statsCalc = null, IReportGenerator? reportGen = null)
        {
            _executor = executor ?? new PingExecutor();
            _statsCalc = statsCalc ?? new StatisticsCalculator();
            _reportGen = reportGen ?? new ReportGenerator();
        }

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
            var avgJitter = await _statsCalc.CalculateAverageJitterAsync(roundtripTimes);
            var finalLog = await _reportGen.GenerateFinalReport(new StringBuilder(), responseTimes, startTime, endTime, execTime, avgJitter, success, fail, config.PingCount, roundtripTimes);
            OnPingResult?.Invoke(Environment.NewLine + finalLog);

            return new PingTestResult(success, fail, execTime, avgJitter, roundtripTimes, logBuilder.ToString() + Environment.NewLine + finalLog);
        }

        private async Task<(int success, int fail, StringBuilder responseTimes)> ExecutePingTestsAsync(IPingConfiguration config, CancellationToken cancellationToken)
        {
            int success = 0, fail = 0;
            var responseTimes = new StringBuilder();
            var options = new PingOptions { DontFragment = config.DontFragment };
            var buffer = new byte[PingServiceConstants.BUFFER_SIZE];

            for (var i = 0; i < config.PingCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _executor.ExecuteSinglePingAsync(config.Url, config.Timeout, options, buffer, i + 1, config.PingCount, cancellationToken);
                responseTimes.AppendLine(result.Message);
                OnPingResult?.Invoke(result.Message + Environment.NewLine);
                if (result.IsSuccess)
                {
                    await AddRoundtripTimeAsync(result.RoundtripTime, cancellationToken);
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

        private async Task AddRoundtripTimeAsync(int time, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _roundtripTimes.Add(time);
                OnRoundtripTimeAdded?.Invoke(time);
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
            try { if (_disposed) throw new ObjectDisposedException(nameof(PingService)); }
            finally { _lock.Release(); }
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
                string message = reply.Status == IPStatus.Success
                    ? $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ответ от {url}:\n    Время: {reply.RoundtripTime,4} мс\n    TTL:   {reply.Options?.Ttl ?? 0}\n    Размер:{reply.Buffer?.Length ?? 0} байт"
                    : $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Ошибка пинга {url}:\n    Статус: {reply.Status}";
                return new PingExecutionResult(reply.Status == IPStatus.Success, (int)reply.RoundtripTime, message, stopwatch.ElapsedMilliseconds);
            }
            catch (PingException ex)
            {
                stopwatch.Stop();
                string errMsg = $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] Критическая ошибка пинга {url}:\n    {ex.Message}";
                return new PingExecutionResult(false, 0, errMsg, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    public class StatisticsCalculator : IStatisticsCalculator
    {
        public Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> times)
        {
            if (times.Count <= 1) return Task.FromResult(0.0);
            double jitter = Math.Round(Enumerable.Range(1, times.Count - 1)
                .Select(i => Math.Abs(times[i] - times[i - 1])).Average(), 2);
            return Task.FromResult(jitter);
        }

        public Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> times)
        {
            if (times.Count == 0) return Task.FromResult((0, 0, 0.0));
            return Task.FromResult((times.Min(), times.Max(), times.Average()));
        }
    }

    public class ReportGenerator : IReportGenerator
    {
        public void InitializeLogBuilder(StringBuilder sb, IPingConfiguration config, DateTime startTime) =>
            sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine("  ТЕСТ PING")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"Время начала:    {startTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"Хост:           {config.Url}")
              .AppendLine($"Кол-во пингов:  {config.PingCount}")
              .AppendLine($"Таймаут:        {config.Timeout} мс")
              .AppendLine($"Не фрагментировать: {(config.DontFragment ? "Да" : "Нет")}")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine();

        public async Task<string> GenerateFinalReport(StringBuilder sb, StringBuilder responseTimes, DateTime startTime, DateTime endTime, TimeSpan execTime, double avgJitter, int success, int fail, int total, IReadOnlyList<int> times)
        {
            var stats = await new StatisticsCalculator().CalculateStatisticsAsync(times);
            var loss = total > 0 ? (fail * 100.0 / total).ToString("F2") : "0.00";
            sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine("  ИТОГИ ТЕСТИРОВАНИЯ")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR)
              .AppendLine($"Время начала:   {startTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"Время конца:    {endTime:dd.MM.yyyy HH:mm:ss}")
              .AppendLine($"Длительность:   {FormatExecutionTime(execTime)}")
              .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
              .AppendLine("Статистика пакетов:")
              .AppendLine($"    Всего отправлено: {total}")
              .AppendLine($"    Успешно:         {success}")
              .AppendLine($"    Потеряно:        {fail} ({loss}%)")
              .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
              .AppendLine("Статистика времени:")
              .AppendLine($"    Минимальное:     {stats.Min} мс")
              .AppendLine($"    Максимальное:    {stats.Max} мс")
              .AppendLine($"    Среднее:         {stats.Average:F2} мс")
              .AppendLine($"    Джиттер:         {avgJitter:F2} мс")
              .AppendLine(PingServiceConstants.LOG_SEPARATOR);
            return sb.ToString();
        }

        private static string FormatExecutionTime(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{t.TotalHours:F2} часов" :
            t.TotalMinutes >= 1 ? $"{t.TotalMinutes:F2} минут" :
            $"{t.TotalSeconds:F2} секунд";
    }

    public class PingConfiguration : IPingConfiguration
    {
        public string Url { get; }
        public int PingCount { get; }
        public int Timeout { get; }
        public bool DontFragment { get; }
        public PingConfiguration(string url, int pingCount, int timeout, bool dontFragment = true)
        {
            var errs = ValidateParameters(url, pingCount, timeout);
            if (errs.Any()) throw new ArgumentException("Invalid configuration parameters: " + string.Join(", ", errs));
            Url = url; PingCount = pingCount; Timeout = timeout; DontFragment = dontFragment;
        }
        public void Validate() { }
        private static IEnumerable<string> ValidateParameters(string url, int pingCount, int timeout) =>
            ValidationHelper.ValidateUrl(url)
                .Concat(ValidationHelper.ValidatePingCount(pingCount.ToString()))
                .Concat(ValidationHelper.ValidateTimeout(timeout.ToString()));
    }

    public class PingTestResult : IPingTestResult
    {
        public int SuccessfulPings { get; }
        public int FailedPings { get; }
        public TimeSpan ExecutionTime { get; }
        public double AverageJitter { get; }
        public IReadOnlyList<int> RoundtripTimes { get; }
        public string DetailedLog { get; }
        public PingTestResult(int success, int fail, TimeSpan execTime, double avgJitter, IReadOnlyList<int> times, string log)
        {
            SuccessfulPings = success; FailedPings = fail; ExecutionTime = execTime; AverageJitter = avgJitter;
            RoundtripTimes = times; DetailedLog = log;
        }
    }

    public readonly record struct PingExecutionResult(bool IsSuccess, int RoundtripTime, string Message, long ElapsedMilliseconds);

    public partial class MainWindow : Window
    {
        public const string DEFAULT_URL = "8.8.8.8";
        public const int DEFAULT_PING_COUNT = 10, DEFAULT_TIMEOUT = 1000;
        internal MainWindowEventHandler? _handler;
        public MainWindow() : this(new PingService()) { }
        public MainWindow(IPingTestService pingService)
        {
            InitializeComponent();
            var warningPresenter = new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3);
            var inputValidator = new InputValidator();
            _handler = new MainWindowEventHandler(this, pingService, inputValidator, warningPresenter);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    MessageBox.Show($"Критическая ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            Closed += _handler.HandleWindowClosed;
        }
        private async void BtnPing_Click(object sender, RoutedEventArgs e) => await _handler!.HandlePingButtonClickAsync();
        private void BtnStop_Click(object sender, RoutedEventArgs e) => _handler?.HandleStopButtonClick();
        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e) => await _handler!.HandleShowGraphButtonClickAsync();
        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e) => _handler?.HandleTraceRouteButtonClick();
    }
}
