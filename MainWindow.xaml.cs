#nullable enable

namespace PingTestTool
{
    public sealed class InputValidator
    {
        private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
        private const int MIN_TIMEOUT = 100;

        public ValidationResult ValidateInput(string url, string pingCount, string timeout)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(url))
                errors.Add("URL не может быть пустым.");
            else if (CyrillicRegex.IsMatch(url))
                errors.Add("URL не может иметь кириллицу.");

            if (!int.TryParse(pingCount, out int count) || count <= 0)
                errors.Add("Количество пакетов должно быть положительным числом.");

            if (!int.TryParse(timeout, out int time) || time < MIN_TIMEOUT)
                errors.Add($"Таймаут должен быть числом не менее {MIN_TIMEOUT} мс.");

            return new(errors);
        }
    }

    public sealed record ValidationResult(List<string> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public sealed class WarningManager
    {
        private readonly Image[] _warningImages;

        public WarningManager(params Image[] warningImages)
        {
            _warningImages = warningImages ?? throw new ArgumentNullException(nameof(warningImages));
        }

        public void HideAllWarnings()
        {
            Array.ForEach(_warningImages, img => img.Visibility = Visibility.Collapsed);
        }

        public void ShowWarnings(ValidationResult result)
        {
            if (!result.IsValid && _warningImages.FirstOrDefault() is Image warning)
            {
                warning.Visibility = Visibility.Visible;
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors),
                    "Ошибка ввода",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }
    }

    public sealed class MainWindowEventHandler
    {
        private readonly MainWindow _mainWindow;
        private readonly PingService _pingService;
        private readonly InputValidator _inputValidator;
        private readonly WarningManager _warningManager;
        private CancellationTokenSource? _cancellationTokenSource;
        private GraphWindow? _graphWindow;
        private TraceWindow? _traceWindow;

        public MainWindowEventHandler(
            MainWindow mainWindow,
            PingService pingService,
            InputValidator inputValidator,
            WarningManager warningManager)
        {
            _mainWindow = mainWindow;
            _pingService = pingService;
            _inputValidator = inputValidator;
            _warningManager = warningManager;

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

                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = null;
            }
            else if (sender is GraphWindow)
            {
                _graphWindow = null; 
            }
        }

        public async Task HandlePingButtonClickAsync()
        {
            try
            {
                if (_mainWindow.btnPing.Content.ToString() == MainWindow.BTN_START_TEXT)
                {
                    Log.Information("[MainWindow] Нажата кнопка 'Запустить тест'.");
                    _warningManager.HideAllWarnings();

                    var validationResult = _inputValidator.ValidateInput(
                        _mainWindow.txtURL.Text,
                        _mainWindow.txtPingCount.Text,
                        _mainWindow.txtTimeout.Text
                    );

                    if (validationResult.IsValid)
                    {
                        Log.Information("[MainWindow] Валидация пройдена успешно. Начинаем пинг-тест.");
                        await ExecutePingTest();
                    }
                    else
                    {
                        Log.Warning("[MainWindow] Валидация не пройдена. Показываем ошибки.");
                        _warningManager.ShowWarnings(validationResult);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Information("[MainWindow] Пинг-тест был отменен.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Произошла ошибка при выполнении пинг-теста");
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExecutePingTest()
        {
            UpdateUIForTestStart();

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                Log.Information("[MainWindow] Начинаем выполнение пинг-теста.");
                await ExecutePingTestCore();
            }
            catch (OperationCanceledException)
            {
                Log.Information("[MainWindow] Пинг-тест был отменен.");
            }
            finally
            {
                Log.Information("[MainWindow] Завершаем выполнение пинг-теста.");
                ResetUIAfterTest();
            }
        }

        private async Task ExecutePingTestCore()
        {
            if (_mainWindow == null || _pingService == null || _cancellationTokenSource == null)
            {
                Log.Error("[MainWindow] Один или несколько необходимых компонентов недействительны.");
                return;
            }

            if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingCount) &&
                int.TryParse(_mainWindow.txtTimeout.Text, out int timeout))
            {
                await _pingService.ClearRoundtripTimesAsync().ConfigureAwait(false);
                var config = new PingService.PingConfiguration(_mainWindow.txtURL.Text, pingCount, timeout);
                try
                {
                    await _pingService.StartPingTestAsync(config, _cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("[MainWindow] Пинг-тест был отменен.");
                }
            }
        }

        private void UpdateUIForTestStart()
        {
            _mainWindow.btnPing.IsEnabled = false;
            _mainWindow.btnStop.IsEnabled = true;
            _mainWindow.btnPing.Content = MainWindow.BTN_WAIT_TEXT;
            _mainWindow.progressBar.Value = 0;
            Log.Information("[MainWindow] Обновляем UI для начала теста.");
        }

        private void ResetUIAfterTest()
        {
            _mainWindow.btnPing.IsEnabled = true;
            _mainWindow.btnPing.Content = MainWindow.BTN_START_TEXT;
            _mainWindow.btnStop.IsEnabled = false;
            _mainWindow.progressBar.Value = 0;
            Log.Information("[MainWindow] Сбрасываем UI после завершения теста.");
        }

        private void UpdateResults(string result)
        {
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.txtResults.AppendText(result));
        }

        private void UpdateGraph(int roundtripTime)
        {
            _mainWindow.Dispatcher.Invoke(async () =>
            {
                if (_graphWindow != null && await _pingService.GetRoundtripTimesAsync() is List<int> roundtripTimes)
                {
                    _graphWindow.SetPingData(roundtripTimes);
                }
            });
        }

        private void UpdateProgressBar(int current, int total)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                double progress = (current * 100.0) / total;
                _mainWindow.progressBar.Value = progress;
                Log.Information("[MainWindow] Обновляем прогресс бар: {Current}/{Total}", current, total);
            });
        }

        public void HandleStopButtonClick()
        {
            _cancellationTokenSource?.Cancel();
            ResetUIAfterTest();
            Log.Information("[MainWindow] Останавливаем пинг-тест по запросу пользователя.");
        }

        public async Task HandleShowGraphButtonClickAsync()
        {
            var roundtripTimes = await _pingService.GetRoundtripTimesAsync();
            if (roundtripTimes?.Count > 0)
            {
                HandleGraphWindow(roundtripTimes.ToList());
                Log.Information("[MainWindow] Показываем график с данными: {RoundtripTimes}", roundtripTimes);
            }
            else
            {
                MessageBox.Show(MainWindow.ERROR_NO_DATA, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Warning("[MainWindow] Нет данных для отображения на графике.");
            }
        }

        private void HandleGraphWindow(List<int> roundtripTimes)
        {
            if (_graphWindow == null || !_graphWindow.IsLoaded)
            {
                CreateNewGraphWindow(roundtripTimes);
                Log.Information("[MainWindow] Создаем новое окно графика.");
            }
            else
            {
                UpdateExistingGraphWindow(roundtripTimes);
                Log.Information("[MainWindow] Обновляем существующее окно графика.");
            }
        }

        private void CreateNewGraphWindow(List<int> roundtripTimes)
        {
            if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingInterval))
            {
                _graphWindow = new GraphWindow(pingInterval);
                if (_graphWindow != null)
                {
                    _graphWindow.SetPingData(roundtripTimes);
                    _graphWindow.Closed += HandleWindowClosed;
                    _graphWindow.Show();
                    Log.Information("[MainWindow] Создано новое окно графика с интервалом: {PingInterval}", pingInterval);
                }
                else
                {
                    Log.Error("[MainWindow] Не удалось создать окно графика.");
                }
            }
        }

        private void UpdateExistingGraphWindow(List<int> roundtripTimes)
        {
            if (_graphWindow == null)
            {
                Log.Warning("[MainWindow] Графическое окно null. Невозможно обновить состояние окна или установить данные пинга.");
                return;
            }

            if (_graphWindow.WindowState == WindowState.Minimized)
            {
                _graphWindow.WindowState = WindowState.Normal;
                _graphWindow.Activate();
                Log.Information("[MainWindow] Восстанавливаем окно графика из минимизированного состояния.");
            }
            else
            {
                _graphWindow.WindowState = WindowState.Minimized;
                Log.Information("[MainWindow] Минимизируем окно графика.");
            }

            _graphWindow.SetPingData(roundtripTimes);
        }

        public void HandleTraceRouteButtonClick()
        {
            if (string.IsNullOrWhiteSpace(_mainWindow.txtURL.Text))
            {
                MessageBox.Show(MainWindow.ERROR_NO_URL, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Warning("[MainWindow] Не указан URL для трассировки.");
                return;
            }

            HandleTraceWindow();
            Log.Information("[MainWindow] Начинаем трассировку маршрута.");
        }

        private void HandleTraceWindow()
        {
            if (_traceWindow == null || !_traceWindow.IsLoaded)
            {
                _traceWindow = new TraceWindow(_mainWindow.txtURL.Text);
                _traceWindow.Closed += HandleWindowClosed;
                _traceWindow.Show();
                Log.Information("[MainWindow] Создано новое окно трассировки.");
            }
            else
            {
                _traceWindow.Visibility = _traceWindow.IsVisible ? Visibility.Collapsed : Visibility.Visible;
                Log.Information("[MainWindow] Переключаем видимость окна трассировки.");
            }
        }
    }

    public partial class MainWindow : Window
    {
        private readonly MainWindowEventHandler _eventHandler;
        private readonly InputValidator _inputValidator;
        private readonly WarningManager _warningManager;
        private readonly PingService _pingService;

        public const string DEFAULT_URL = "google.com";
        public const string DEFAULT_PING_COUNT = "4";
        public const string DEFAULT_TIMEOUT = "1000";

        public const string BTN_START_TEXT = "Запустить тест";
        public const string BTN_WAIT_TEXT = "Ожидаем...";
        public const string ERROR_NO_DATA = "Нет данных пинга для отображения.";
        public const string ERROR_NO_URL = "Пожалуйста, укажите URL для трассировки.";

        public MainWindow()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                var exception = eventArgs.ExceptionObject as Exception;
                Log.Fatal(exception, "Unhandled exception");
                MessageBox.Show($"Критическая ошибка: {exception?.Message}", 
                      "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            _warningManager = new WarningManager(
                imgWarning, imgWarning_1, imgWarning_3
            );
            _inputValidator = new InputValidator();
            _pingService = new PingService();

            _eventHandler = new MainWindowEventHandler(
                this,
                _pingService,
                _inputValidator,
                _warningManager
            );

            this.Closed += _eventHandler.HandleWindowClosed;
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            await _eventHandler.HandlePingButtonClickAsync();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler.HandleStopButtonClick();
        }

        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e)
        {
            await _eventHandler.HandleShowGraphButtonClickAsync();
        }

        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler.HandleTraceRouteButtonClick();
        }
    }
}