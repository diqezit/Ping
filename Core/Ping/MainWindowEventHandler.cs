// MainWindowEventHandler.cs

#nullable enable

namespace PingTestTool;

public class MainWindowEventHandler : IMainWindowHandler
{
    private const string BTN_START_TEXT_KEY = "BtnStartText";
    private const string BTN_WAIT_TEXT_KEY = "BtnWaitText";
    private const string ERROR_NO_DATA_KEY = "ErrorNoGraphData";
    private const string ERROR_NO_URL_KEY = "ErrorNoUrlForTrace";

    private readonly MainWindow _window;
    private readonly IPingTestService _pingService;
    private readonly IInputValidator _validator;
    private readonly IWarningPresenter _warningPresenter;
    private CancellationTokenSource? _cts;
    private IGraphWindow? _graphWindow;

    public MainWindowEventHandler(
        MainWindow window,
        IPingTestService pingService,
        IInputValidator validator,
        IWarningPresenter warningPresenter)
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

    private void UnsubscribeFromEvents()
    {
        _pingService.OnPingResult -= HandlePingResult;
        _pingService.OnProgressUpdate -= HandleProgressUpdate;
        _pingService.OnRoundtripTimeAdded -= HandleRoundtripTimeAdded;
    }

    private void HandlePingResult(string result) =>
        _window.Dispatcher.Invoke(() => _window.txtResults.AppendText(result));

    private void HandleProgressUpdate(int current, int total) =>
        _window.Dispatcher.Invoke(() => _window.progressBar.Value = (current * 100.0) / total);

    private async void HandleRoundtripTimeAdded(int roundtripTime)
    {
        if (_graphWindow == null) return;

        var times = await _pingService.GetRoundtripTimesAsync();
        _window.Dispatcher.Invoke(() => _graphWindow?.SetPingData(times.ToList()));
    }

    public void HandleWindowClosed(object? sender, EventArgs e)
    {
        if (sender == _window)
        {
            UnsubscribeFromEvents();
            _graphWindow?.Close();
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
            if (_window.btnPing.Content?.ToString() == ResourceHelper.FindResourceString(BTN_START_TEXT_KEY))
            {
                _warningPresenter.HideAllWarnings();
                var validation = _validator.ValidateInput(_window.txtURL.Text, _window.txtPingCount.Text, _window.txtTimeout.Text);

                if (validation.IsValid)
                    await ExecutePingTest();
                else
                    _warningPresenter.ShowWarnings(validation);
            }
        }
        catch (OperationCanceledException)
        {
            // No-op on cancellation
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{ResourceHelper.FindResourceString("GenericError")}: {ex.Message}",
                ResourceHelper.FindResourceString("ErrorCaption"),
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
                ResourceHelper.FindResourceString(ERROR_NO_DATA_KEY),
                ResourceHelper.FindResourceString("ErrorCaption"),
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

    private void HandleGraphWindow(List<(DateTime Time, int RoundtripTime)> pingData)
    {
        if (_graphWindow == null || !_graphWindow.IsLoaded)
        {
            CreateNewGraphWindow(pingData);
        }
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

    private async Task ExecutePingTest()
    {
        UpdateUIForTestStart();
        _cts = new CancellationTokenSource();

        try
        {
            await ExecutePingTestCore();
        }
        catch (OperationCanceledException)
        {
            // No-op on cancellation
        }
        finally
        {
            ResetUIAfterTest();
        }
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

    private void UpdateUIForTestStart() =>
        _window.Dispatcher.Invoke(() =>
        {
            _window.btnPing.IsEnabled = false;
            _window.btnStop.IsEnabled = true;
            _window.btnPing.Content = FindResourceString(BTN_WAIT_TEXT_KEY);
            _window.progressBar.Value = 0;
        });

    private void ResetUIAfterTest() =>
        _window.Dispatcher.Invoke(() =>
        {
            _window.btnPing.IsEnabled = true;
            _window.btnPing.Content = FindResourceString(BTN_START_TEXT_KEY);
            _window.btnStop.IsEnabled = false;
            _window.progressBar.Value = 0;
        });

    public void HideWarningsOnStartup() => _warningPresenter.HideAllWarnings();

    private static string FindResourceString(string resourceKey) =>
        Application.Current?.TryFindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
}
