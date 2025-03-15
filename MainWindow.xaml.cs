#nullable enable

namespace PingTestTool
{
    public partial class MainWindow : Window, IDisposable
    {
        #region Constants and Fields
        public const string DEFAULT_URL = "8.8.8.8";
        public const int DEFAULT_PING_COUNT = 10, DEFAULT_TIMEOUT = 1000;
        private static readonly string _themeBaseDir = "Themes", _languageBaseDir = "Resources";

        private readonly MainWindowEventHandler _handler;
        private TraceManager? _traceManager;
        private bool _disposed;
        #endregion

        #region Initialization
        public MainWindow() : this(new PingService()) { }

        public MainWindow(IPingTestService pingService)
        {
            InitializeComponent();

            _handler = new MainWindowEventHandler(
                this,
                pingService,
                new InputValidator(),
                new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3)
            );

            InitializeWindow();
            StateChanged += MainWindow_StateChanged;
        }

        private void InitializeWindow()
        {
            _handler.HideWarningsOnStartup();
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            Closed += HandleWindowClosed;

            txtURL.Text = DEFAULT_URL;
            txtPingCount.Text = DEFAULT_PING_COUNT.ToString();
            txtTimeout.Text = DEFAULT_TIMEOUT.ToString();

            UpdateMaximizeRestoreButton();
            ApplyLanguage("StringResources.en.xaml");
        }
        #endregion

        #region Window Control Handlers
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowHelper.HandleTitleBarMouseLeftButtonDown(this, e);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
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
        #endregion

        #region Ping UI Events
        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _handler.HandlePingButtonClickAsync();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error during ping operation: {ex.Message}", "Error");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler.HandleStopButtonClick();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error stopping operation: {ex.Message}", "Error");
            }
        }

        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _handler.HandleShowGraphButtonClickAsync();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error displaying graph: {ex.Message}", "Error");
            }
        }

        private void BtnClearResultsPing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (confirmMessage, confirmCaption) = (
                    ResourceHelper.FindResourceString("ClearPingResultsConfirmation"),
                    ResourceHelper.FindResourceString("ConfirmationCaption")
                );

                if (MessageBox.Show(confirmMessage, confirmCaption,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    txtResults.Clear();
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"Error clearing ping results: {ex.Message}", "Error");
            }
        }
        #endregion

        #region Traceroute Functionality
        private bool ValidateStartTrace()
        {
            if (_traceManager?.IsTracing == true)
            {
                ShowMessage("Trace is already running.", "Warning");
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtURL.Text))
            {
                ShowMessage("Please specify URL for tracing.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateStartTrace()) return;

            try
            {
                await InitializeAndStartTrace();
            }
            catch (Exception ex)
            {
                ShowMessage($"Trace error: {ex.Message}", "Error");
                SetTraceControlsState(false);
            }
        }

        private async Task InitializeAndStartTrace()
        {
            SetTraceControlsState(true);

            using (_traceManager = new TraceManager(txtURL.Text))
            {
                ResultsList.ItemsSource = CollectionViewSource.GetDefaultView(_traceManager.TraceResults);

                if (ResultsList.ItemsSource is ICollectionView view)
                    view.SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));

                await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
            }
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _traceManager?.StopTrace();
                UpdateStatus("Stopping trace...", Colors.Orange);
                SetTraceControlsState(false);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error stopping trace: {ex.Message}", "Error");
            }
        }

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _traceManager?.ClearResults();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error clearing results: {ex.Message}", "Error");
            }
        }

        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
                };

                if (dlg.ShowDialog() == true)
                    SaveResults(dlg.FileName);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error in save dialog: {ex.Message}", "Error");
            }
        }

        private void SaveResults(string fileName)
        {
            try
            {
                if (_traceManager?.TraceResults == null)
                {
                    ShowMessage("No results to save.", "Warning");
                    return;
                }

                File.WriteAllLines(fileName,
                    _traceManager.TraceResults.Select(r => r?.ToString() ?? "Empty result"));
                ShowMessage("Results saved successfully.", "Success");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error saving results: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
        #endregion

        #region Theme & Language Handling
        private void DarkTheme_Click(object sender, RoutedEventArgs e) =>
            ApplyThemeWithErrorHandling("DarkTheme.xaml");

        private void LightTheme_Click(object sender, RoutedEventArgs e) =>
            ApplyThemeWithErrorHandling("LightTheme.xaml");

        private void RussianLanguage_Click(object sender, RoutedEventArgs e) =>
            ApplyLanguageWithErrorHandling("StringResources.ru.xaml");

        private void EnglishLanguage_Click(object sender, RoutedEventArgs e) =>
            ApplyLanguageWithErrorHandling("StringResources.en.xaml");

        private void ApplyThemeWithErrorHandling(string themeName)
        {
            try
            {
                ApplyTheme(themeName);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying theme: {ex.Message}", "Theme Error");
            }
        }

        private void ApplyLanguageWithErrorHandling(string languageName)
        {
            try
            {
                ApplyLanguage(languageName);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying language: {ex.Message}", "Language Error");
            }
        }

        private void ApplyTheme(string themeFileName) =>
            ResourceHelper.ApplyResourceDictionary($"{_themeBaseDir}/{themeFileName}", _themeBaseDir, this);

        private void ApplyLanguage(string languageFileName) =>
            ResourceHelper.ApplyResourceDictionary($"{_languageBaseDir}/{languageFileName}", _languageBaseDir, this);

        #endregion

        #region Resource Management & Disposal
        private void HandleWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                _handler.HandleWindowClosed(sender, e);
                Dispose();
            }
            catch (Exception ex)
            {
                ShowMessage($"Error during window cleanup: {ex.Message}", "Error");
            }
        }

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
        #endregion

        #region Error Handling & Message Display
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

        private void ShowMessage(
            string msg,
            string title,
            MessageBoxButton btn = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information
        ) => Dispatcher.Invoke(() => MessageBox.Show(msg, title, btn, icon));
        #endregion

        #region UI Updates
        public void ResetPingTestUI() =>
            Dispatcher.Invoke(() =>
            {
                btnPing.Content = FindResource("StartTestButton");
                btnPing.IsEnabled = true;
            });
        #endregion

        #region логика управления окнами

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

        #endregion
    }
}