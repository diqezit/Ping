#nullable enable

namespace PingTestTool;

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

public class WarningPresenter : IWarningPresenter
{
    private readonly Image[] _warningImages;
    public WarningPresenter(params Image[] warningImages) =>
        _warningImages = warningImages ?? throw new ArgumentNullException(nameof(warningImages));
    public void HideAllWarnings() =>
        Array.ForEach(_warningImages, img => img.Visibility = Visibility.Collapsed);
    public void ShowWarnings(ValidationResult result)
    {
        if (!result.IsValid && _warningImages.FirstOrDefault() is Image warning)
        {
            warning.Visibility = Visibility.Visible;
            MessageBox.Show(string.Join(Environment.NewLine, result.Errors),
                "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public static class ValidationHelper
{
    private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
    private const int MIN_TIMEOUT = 100, MIN_PING_COUNT = 1, MAX_PING_COUNT = 1000;
    public static List<string> ValidateUrl(string url)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(url))
            errors.Add("URL не может быть пустым.");
        else if (CyrillicRegex.IsMatch(url))
            errors.Add("URL не может иметь кириллицу.");
        return errors;
    }
    public static List<string> ValidatePingCount(string pingCount)
    {
        var errors = new List<string>();
        if (!int.TryParse(pingCount, out int count) || count < MIN_PING_COUNT || count > MAX_PING_COUNT)
            errors.Add($"Количество пакетов должно быть целым числом между {MIN_PING_COUNT} и {MAX_PING_COUNT}.");
        return errors;
    }
    public static List<string> ValidateTimeout(string timeout)
    {
        var errors = new List<string>();
        if (!int.TryParse(timeout, out int time) || time < MIN_TIMEOUT)
            errors.Add($"Таймаут должен быть целым числом не менее {MIN_TIMEOUT} мс.");
        return errors;
    }
}

public class MainWindowEventHandler : IMainWindowHandler
{
    private readonly MainWindow _mainWindow;
    private readonly IPingTestService _pingService;
    private readonly IInputValidator _inputValidator;
    private readonly IWarningPresenter _warningPresenter;
    private CancellationTokenSource? _cts;
    private IGraphWindow? _graphWindow;
    private ITraceWindow? _traceWindow;
    public const string BTN_START_TEXT = "Запустить тест";
    public const string BTN_WAIT_TEXT = "Ожидаем...";
    public const string ERROR_NO_DATA = "Нет данных пинга для отображения.";
    public const string ERROR_NO_URL = "Пожалуйста, укажите URL для трассировки.";

    public MainWindowEventHandler(MainWindow mainWindow, IPingTestService pingService, IInputValidator inputValidator, IWarningPresenter warningPresenter)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
        _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
        _warningPresenter = warningPresenter ?? throw new ArgumentNullException(nameof(warningPresenter));
        InitializePingService();
    }

    private void InitializePingService()
    {
        _pingService.OnPingResult += UpdateResults;
        _pingService.OnProgressUpdate += UpdateProgressBar;
        _pingService.OnRoundtripTimeAdded += UpdateGraph;
    }

    public void HandleWindowClosed(object? sender, EventArgs e)
    {
        if (sender == _mainWindow)
        {
            _graphWindow?.Close();
            _traceWindow?.Close();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        else if (sender is IGraphWindow)
        {
            _graphWindow = null;
        }
    }

    public async Task HandlePingButtonClickAsync()
    {
        try
        {
            if (_mainWindow.btnPing.Content.ToString() == BTN_START_TEXT)
            {
                _warningPresenter.HideAllWarnings();
                var validationResult = _inputValidator.ValidateInput(
                    _mainWindow.txtURL.Text,
                    _mainWindow.txtPingCount.Text,
                    _mainWindow.txtTimeout.Text);
                if (validationResult.IsValid)
                    await ExecutePingTest();
                else
                    _warningPresenter.ShowWarnings(validationResult);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingCount) &&
            int.TryParse(_mainWindow.txtTimeout.Text, out int timeout))
        {
            await _pingService.ClearRoundtripTimesAsync().ConfigureAwait(false);
            var config = new PingConfiguration(_mainWindow.txtURL.Text, pingCount, timeout);
            await _pingService.StartPingTestAsync(config, _cts!.Token).ConfigureAwait(false);
        }
    }

    private void UpdateUIForTestStart()
    {
        _mainWindow.btnPing.IsEnabled = false;
        _mainWindow.btnStop.IsEnabled = true;
        _mainWindow.btnPing.Content = BTN_WAIT_TEXT;
        _mainWindow.progressBar.Value = 0;
    }

    private void ResetUIAfterTest()
    {
        _mainWindow.btnPing.IsEnabled = true;
        _mainWindow.btnPing.Content = BTN_START_TEXT;
        _mainWindow.btnStop.IsEnabled = false;
        _mainWindow.progressBar.Value = 0;
    }

    private void UpdateResults(string result) =>
        _mainWindow.Dispatcher.Invoke(() => _mainWindow.txtResults.AppendText(result));

    private void UpdateGraph(int roundtripTime)
    {
        _mainWindow.Dispatcher.Invoke(async () =>
        {
            if (_graphWindow != null)
            {
                var times = await _pingService.GetRoundtripTimesAsync();
                _graphWindow.SetPingData(times.ToList());
            }
        });
    }

    private void UpdateProgressBar(int current, int total) =>
        _mainWindow.Dispatcher.Invoke(() =>
            _mainWindow.progressBar.Value = (current * 100.0) / total);

    public void HandleStopButtonClick()
    {
        _cts?.Cancel();
        ResetUIAfterTest();
    }

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
            UpdateExistingGraphWindow(times);
    }

    private void CreateNewGraphWindow(List<int> times)
    {
        if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingInterval))
        {
            _graphWindow = new GraphWindow(pingInterval);
            _graphWindow.SetPingData(times);
            _graphWindow.Closed += HandleWindowClosed;
            _graphWindow.Show();
        }
    }

    private void UpdateExistingGraphWindow(List<int> times)
    {
        if (_graphWindow is null)
            return;
        _graphWindow.WindowState = _graphWindow.WindowState == WindowState.Minimized ? WindowState.Normal : WindowState.Minimized;
        _graphWindow.SetPingData(times);
    }

    public void HandleTraceRouteButtonClick()
    {
        if (string.IsNullOrWhiteSpace(_mainWindow.txtURL.Text))
            MessageBox.Show(ERROR_NO_URL, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            HandleTraceWindow();
    }

    private void HandleTraceWindow()
    {
        if (_traceWindow == null || !_traceWindow.IsLoaded)
        {
            _traceWindow = new TraceWindow(_mainWindow.txtURL.Text);
            _traceWindow.Closed += HandleWindowClosed;
            _traceWindow.Show();
        }
        else
        {
            _traceWindow.Visibility = _traceWindow.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}

public partial class MainWindow : Window
{
    public const string DEFAULT_URL = "8.8.8.8";
    public const int DEFAULT_PING_COUNT = 10;
    public const int DEFAULT_TIMEOUT = 1000;
    internal MainWindowEventHandler? _eventHandler;

    public MainWindow() : this(new PingService()) { }

    public MainWindow(IPingTestService pingService)
    {
        InitializeComponent();
        InitializeComponents(pingService);
        SetupExceptionHandling();
    }

    private void InitializeComponents(IPingTestService pingService)
    {
        var warningPresenter = new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3);
        var inputValidator = new InputValidator();
        _eventHandler = new MainWindowEventHandler(this, pingService, inputValidator, warningPresenter);
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                MessageBox.Show($"Критическая ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        Closed += _eventHandler!.HandleWindowClosed;
    }

    private async void BtnPing_Click(object sender, RoutedEventArgs e) =>
        await _eventHandler!.HandlePingButtonClickAsync();

    private void BtnStop_Click(object sender, RoutedEventArgs e) =>
        _eventHandler?.HandleStopButtonClick();

    private async void BtnShowGraph_Click(object sender, RoutedEventArgs e) =>
        await _eventHandler!.HandleShowGraphButtonClickAsync();

    private void BtnTraceRoute_Click(object sender, RoutedEventArgs e) =>
        _eventHandler?.HandleTraceRouteButtonClick();
}
