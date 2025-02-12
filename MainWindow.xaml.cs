#nullable enable

namespace PingTestTool
{
    public partial class MainWindow : Window, IDisposable
    {
        #region Constants
        public const string DEFAULT_URL = "8.8.8.8";
        public const int DEFAULT_PING_COUNT = 10;
        public const int DEFAULT_TIMEOUT = 1000;
        private static readonly string _themeBaseDir = "Themes";
        private static readonly string _languageBaseDir = "Resources";
        #endregion

        #region Fields
        private readonly MainWindowEventHandler _handler;
        private TraceManager? _traceManager;
        private bool _disposed;
        #endregion

        public MainWindow() : this(new PingService()) { }

        public MainWindow(IPingTestService pingService)
        {
            InitializeComponent();
            _handler = new MainWindowEventHandler(this, pingService,
                new InputValidator(),
                new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3));

            InitializeWindow();
        }

        private void InitializeWindow()
        {
            _handler.HideWarningsOnStartup();
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            Closed += HandleWindowClosed;

            txtURL.Text = DEFAULT_URL;
            txtPingCount.Text = DEFAULT_PING_COUNT.ToString();
            txtTimeout.Text = DEFAULT_TIMEOUT.ToString();

            ApplyLanguage("StringResources.en.xaml");
        }

        #region Button Click Events
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
                MessageBoxResult result = MessageBox.Show(FindResourceStringStatic("ClearPingResultsConfirmation"),
                                                     FindResourceStringStatic("ConfirmationCaption"),
                                                     MessageBoxButton.YesNo,
                                                     MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
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
            if (_traceManager != null && _traceManager.IsTracing)
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
                {
                    view.SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
                }

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
                {
                    SaveResults(dlg.FileName);
                }
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

                System.IO.File.WriteAllLines(fileName,
                    _traceManager.TraceResults.Select(r => r?.ToString() ?? "Empty result"));
                ShowMessage("Results saved successfully.", "Success");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error saving results: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetTraceControlsState(bool tracing)
        {
            Dispatcher.Invoke(() =>
            {
                btnStartTrace.IsEnabled = !tracing;
                btnStopTrace.IsEnabled = tracing;
            });
        }

        private void UpdateStatus(string msg, Color color)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = msg;
                StatusTextBlock.Foreground = new SolidColorBrush(color);
            });
        }
        #endregion

        #region Theme & Language Application
        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyTheme("DarkTheme.xaml");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying dark theme: {ex.Message}", "Theme Error");
            }
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyTheme("LightTheme.xaml");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying light theme: {ex.Message}", "Theme Error");
            }
        }

        private void RussianLanguage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyLanguage("StringResources.ru.xaml");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying Russian language: {ex.Message}", "Language Error");
            }
        }

        private void EnglishLanguage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyLanguage("StringResources.en.xaml");
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying English language: {ex.Message}", "Language Error");
            }
        }

        private void ApplyTheme(string themeFileName) =>
            ApplyResourceDictionary($"{_themeBaseDir}/{themeFileName}", _themeBaseDir);

        private void ApplyLanguage(string languageFileName) =>
            ApplyResourceDictionary($"{_languageBaseDir}/{languageFileName}", _languageBaseDir);

        private void ApplyResourceDictionary(string resourcePath, string baseDir)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{GetType().Assembly.GetName().Name};component/{resourcePath}", UriKind.Absolute);
                var newResourceDictionary = new ResourceDictionary { Source = uri };

                UpdateResourceDictionaries(Application.Current.Resources.MergedDictionaries, newResourceDictionary, baseDir);
                UpdateResourceDictionaries(Resources.MergedDictionaries, newResourceDictionary, baseDir);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error applying resources: {ex.Message}", "Resource Error");
            }
        }

        private static void UpdateResourceDictionaries(Collection<ResourceDictionary> dictionaries,
            ResourceDictionary newResourceDictionary, string baseDir)
        {
            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.ToString();
                if (source != null && source.Contains($"/{baseDir}/"))
                {
                    if (baseDir == "Themes" &&
                        source.EndsWith("CommonStyles.xaml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    dictionaries.RemoveAt(i);
                }
            }
            dictionaries.Add(newResourceDictionary);
        }
        #endregion

        #region Resource Management
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
            {
                disposableHandler.Dispose();
            }

            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
            Closed -= HandleWindowClosed;

            _disposed = true;
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Error Handling
        private static void HandleUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"{FindResourceStringStatic("CriticalError")}: {ex.Message}",
                    FindResourceStringStatic("ErrorCaption"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FindResourceStringStatic(string resourceKey)
            => Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";

        private void ShowMessage(string msg, string title,
            MessageBoxButton btn = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information)
        {
            Dispatcher.Invoke(() => MessageBox.Show(msg, title, btn, icon));
        }
        #endregion

        #region UI Updates
        public void ResetPingTestUI()
        {
            Dispatcher.Invoke(() =>
            {
                btnPing.Content = FindResource("StartTestButton");
                btnPing.IsEnabled = true;
            });
        }
        #endregion
    }
}