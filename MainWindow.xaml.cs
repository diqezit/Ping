#nullable enable

namespace PingTestTool;

public partial class MainWindow : Window, IDisposable
{
    public const string DEFAULT_URL = "8.8.8.8";
    public const int DEFAULT_PING_COUNT = 10;
    public const int DEFAULT_TIMEOUT = 1000;

    private static readonly string _themeBaseDir = "Themes";
    private static readonly string _languageBaseDir = "Resources";

    private readonly MainWindowEventHandler _handler;
    private TraceManager? _traceManager;
    private bool _disposed;

    public MainWindow() : this(new PingService()) { }

    public MainWindow(IPingTestService pingService)
    {
        InitializeComponent();
        _handler = CreateHandler(pingService);
        InitializeWindow();
    }

    private MainWindowEventHandler CreateHandler(IPingTestService pingService) =>
        new(this,
            pingService,
            new InputValidator(),
            new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3));

    private void InitializeWindow()
    {
        HookGlobalHandlers();
        HookWindowEvents();
        InitializeDefaults();
        ApplyStartupLanguage();
        UpdateMaximizeRestoreButton();
        _handler.HideWarningsOnStartup();
    }

    private void HookGlobalHandlers() =>
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

    private void HookWindowEvents()
    {
        Closed += HandleWindowClosed;
        StateChanged += MainWindow_StateChanged;
    }

    private void InitializeDefaults()
    {
        txtURL.Text = DEFAULT_URL;
        txtPingCount.Text = DEFAULT_PING_COUNT.ToString();
        txtTimeout.Text = DEFAULT_TIMEOUT.ToString();
    }

    private void ApplyStartupLanguage() =>
        ApplyLanguage("StringResources.en.xaml");

    // Window controls
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        WindowHelper.HandleTitleBarMouseLeftButtonDown(this, e);

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButton();
        WindowHelper.AdjustWindowCorners(this);
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (MaximizeIcon == null) return;

        MaximizeIcon.Data = WindowState == WindowState.Maximized
            ? Geometry.Parse("M2,2 L10,2 L10,10 L2,10 Z M0,4 L8,4 L8,12 L0,12 Z")
            : Geometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
    }

    // Ping UI
    private async void BtnPing_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_handler.HandlePingButtonClickAsync, "Error during ping operation");

    private void BtnStop_Click(object sender, RoutedEventArgs e) =>
        RunSafe(_handler.HandleStopButtonClick, "Error stopping operation");

    private async void BtnShowGraph_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(_handler.HandleShowGraphButtonClickAsync, "Error displaying graph");

    private void BtnClearResultsPing_Click(object sender, RoutedEventArgs e) =>
        RunSafe(ClearPingResults, "Error clearing ping results");

    private void ClearPingResults()
    {
        var confirmMessage = ResourceHelper.FindResourceString("ClearPingResultsConfirmation");
        var confirmCaption = ResourceHelper.FindResourceString("ConfirmationCaption");

        if (MessageBox.Show(confirmMessage, confirmCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            txtResults.Clear();
    }

    // Traceroute
    private bool ValidateStartTrace()
    {
        if (_traceManager?.IsTracing == true)
        {
            ShowMessage("Trace is already running.", "Warning");
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtURL.Text))
        {
            ShowMessage("Please specify URL for tracing.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStartTrace()) return;

        await RunSafeAsync(InitializeAndStartTrace, "Trace error", onError: () => SetTraceControlsState(false));
    }

    private async Task InitializeAndStartTrace()
    {
        SetTraceControlsState(true);

        using (_traceManager = new TraceManager(txtURL.Text))
        {
            BindTraceResults(_traceManager);
            await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
        }
    }

    private void BindTraceResults(TraceManager manager)
    {
        ResultsList.ItemsSource = CollectionViewSource.GetDefaultView(manager.TraceResults);

        if (ResultsList.ItemsSource is ICollectionView view)
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
        }
    }

    private void BtnStopTrace_Click(object sender, RoutedEventArgs e) =>
        RunSafe(() =>
        {
            _traceManager?.StopTrace();
            UpdateStatus("Stopping trace...", Colors.Orange);
            SetTraceControlsState(false);
        }, "Error stopping trace");

    private void BtnClearResults_Click(object sender, RoutedEventArgs e) =>
        RunSafe(() => _traceManager?.ClearResults(), "Error clearing results");

    private void BtnSaveResults_Click(object sender, RoutedEventArgs e) =>
        RunSafe(SaveResultsViaDialog, "Error in save dialog");

    private void SaveResultsViaDialog()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
            SaveResultsToFile(dlg.FileName);
    }

    private void SaveResultsToFile(string fileName)
    {
        try
        {
            if (_traceManager?.TraceResults == null)
            {
                ShowMessage("No results to save.", "Warning");
                return;
            }

            var lines = _traceManager.TraceResults.Select(r => r?.ToString() ?? "Empty result");
            File.WriteAllLines(fileName, lines);
            ShowMessage("Results saved successfully.", "Success");
        }
        catch (Exception ex)
        {
            ShowMessage($"Error saving results: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetTraceControlsState(bool tracing) =>
        Dispatcher.Invoke(() =>
        {
            btnStartTrace.IsEnabled = !tracing;
            btnStopTrace.IsEnabled = tracing;
        });

    private void UpdateStatus(string msg, Color color) =>
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(color);
        });

    // Theme & Language
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => ApplyThemeSafely("DarkTheme.xaml");
    private void LightTheme_Click(object sender, RoutedEventArgs e) => ApplyThemeSafely("LightTheme.xaml");
    private void RussianLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguageSafely("StringResources.ru.xaml");
    private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguageSafely("StringResources.en.xaml");

    private void ApplyThemeSafely(string themeName) =>
        RunSafe(() => ApplyTheme(themeName), "Error applying theme");

    private void ApplyLanguageSafely(string languageName) =>
        RunSafe(() => ApplyLanguage(languageName), "Error applying language");

    private void ApplyTheme(string themeFileName) =>
        ResourceHelper.ApplyResourceDictionary($"{_themeBaseDir}/{themeFileName}", _themeBaseDir, this);

    private void ApplyLanguage(string languageFileName) =>
        ResourceHelper.ApplyResourceDictionary($"{_languageBaseDir}/{languageFileName}", _languageBaseDir, this);

    // Cleanup & errors
    private void HandleWindowClosed(object? sender, EventArgs e) =>
        RunSafe(Dispose, "Error during window cleanup");

    public void Dispose()
    {
        if (_disposed) return;

        _traceManager?.Dispose();

        if (_handler is IDisposable disposableHandler)
            disposableHandler.Dispose();

        AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
        Closed -= HandleWindowClosed;
        StateChanged -= MainWindow_StateChanged;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void HandleUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"{ResourceHelper.FindResourceString("CriticalError")}: {ex.Message}",
                ResourceHelper.FindResourceString("ErrorCaption"),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    // UI helpers
    public void ResetPingTestUI() =>
        Dispatcher.Invoke(() =>
        {
            btnPing.Content = ResourceHelper.FindResourceString("StartTestButton");
            btnPing.IsEnabled = true;
        });

    private void ShowMessage(
        string msg,
        string title,
        MessageBoxButton btn = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.Information
    ) => Dispatcher.Invoke(() => MessageBox.Show(msg, title, btn, icon));

    // Safe execution wrappers
    private void RunSafe(Action action, string errorPrefix)
    {
        try { action(); }
        catch (Exception ex) { ShowMessage($"{errorPrefix}: {ex.Message}", "Error"); }
    }

    private async Task RunSafeAsync(Func<Task> action, string errorPrefix, Action? onError = null)
    {
        try { await action(); }
        catch (Exception ex)
        {
            onError?.Invoke();
            ShowMessage($"{errorPrefix}: {ex.Message}", "Error");
        }
    }

    // Window helper class
    public static class WindowHelper
    {
        public static void AdjustWindowCorners(Window window)
        {
            bool isMaximized = window.WindowState == WindowState.Maximized;
            window.BorderThickness = new Thickness(isMaximized ? 0 : 1);

            if (window.Content is Border mainBorder)
            {
                mainBorder.CornerRadius = new CornerRadius(isMaximized ? 0 : 12);

                if (mainBorder.Child is Grid grid &&
                    grid.Children.Count > 0 &&
                    grid.Children[0] is Border titleBar)
                {
                    titleBar.CornerRadius = isMaximized
                        ? new CornerRadius(0)
                        : new CornerRadius(12, 12, 0, 0);
                }
            }
        }

        public static void HandleTitleBarMouseLeftButtonDown(Window window, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                window.DragMove();
            }
        }
    }
}