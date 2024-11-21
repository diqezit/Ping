#nullable enable

namespace PingTestTool
{
    public interface IMainWindowHandler
    {
        void HandleWindowClosed(object? sender, EventArgs e);
        Task HandlePingButtonClickAsync();
        void HandleStopButtonClick();
        Task HandleShowGraphButtonClickAsync();
        void HandleTraceRouteButtonClick();
    }

    public class MainWindowEventHandler : IMainWindowHandler
    {
        private readonly MainWindow _mainWindow;
        private readonly IPingTestService _pingService;
        private readonly IInputValidator _inputValidator;
        private readonly IWarningPresenter _warningPresenter;
        private readonly ILoggingService _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private IGraphWindow? _graphWindow;
        private ITraceWindow? _traceWindow;

        public const string BTN_START_TEXT = "Запустить тест";
        public const string BTN_WAIT_TEXT = "Ожидаем...";
        public const string ERROR_NO_DATA = "Нет данных пинга для отображения.";
        public const string ERROR_NO_URL = "Пожалуйста, укажите URL для трассировки.";

        public MainWindowEventHandler(
            MainWindow mainWindow,
            IPingTestService pingService,
            IInputValidator inputValidator,
            IWarningPresenter warningPresenter,
            ILoggingService logger)
        {
            _mainWindow = mainWindow ?? 
                throw new ArgumentNullException(nameof(mainWindow));
            _pingService = pingService ?? 
                throw new ArgumentNullException(nameof(pingService));
            _inputValidator = inputValidator ?? 
                throw new ArgumentNullException(nameof(inputValidator));
            _warningPresenter = warningPresenter ?? 
                throw new ArgumentNullException(nameof(warningPresenter));
            _logger = logger ?? 
                throw new ArgumentNullException(nameof(logger));

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
                    _logger.Information("[MainWindow] Нажата кнопка 'Запустить тест'.");
                    _warningPresenter.HideAllWarnings();

                    var validationResult = _inputValidator.ValidateInput(
                        _mainWindow.txtURL.Text,
                        _mainWindow.txtPingCount.Text,
                        _mainWindow.txtTimeout.Text
                    );

                    if (validationResult.IsValid)
                    {
                        _logger.Information("[MainWindow] Валидация пройдена успешно. Начинаем пинг-тест.");
                        await ExecutePingTest();
                    }
                    else
                    {
                        _logger.Warning("[MainWindow] Валидация не пройдена. Показываем ошибки.");
                        _warningPresenter.ShowWarnings(validationResult);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Information("[MainWindow] Пинг-тест был отменен.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[MainWindow] Произошла ошибка при выполнении пинг-теста: {ex.Message}");
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExecutePingTest()
        {
            UpdateUIForTestStart();

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _logger.Information("[MainWindow] Начинаем выполнение пинг-теста.");
                await ExecutePingTestCore();
            }
            catch (OperationCanceledException)
            {
                _logger.Information("[MainWindow] Пинг-тест был отменен.");
            }
            finally
            {
                _logger.Information("[MainWindow] Завершаем выполнение пинг-теста.");
                ResetUIAfterTest();
            }
        }

        private async Task ExecutePingTestCore()
        {
            if (_mainWindow == null || _pingService == null || _cancellationTokenSource == null)
            {
                _logger.Error("[MainWindow] Один или несколько необходимых компонентов недействительны.");
                return;
            }

            if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingCount) &&
                int.TryParse(_mainWindow.txtTimeout.Text, out int timeout))
            {
                await _pingService.ClearRoundtripTimesAsync().ConfigureAwait(false);
                var config = new PingConfiguration(_mainWindow.txtURL.Text, pingCount, timeout);
                try
                {
                    await _pingService.StartPingTestAsync(config, _cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.Information("[MainWindow] Пинг-тест был отменен.");
                }
            }
        }

        private void UpdateUIForTestStart()
        {
            _mainWindow.btnPing.IsEnabled = false;
            _mainWindow.btnStop.IsEnabled = true;
            _mainWindow.btnPing.Content = BTN_WAIT_TEXT;
            _mainWindow.progressBar.Value = 0;
            _logger.Information("[MainWindow] Обновляем UI для начала теста.");
        }

        private void ResetUIAfterTest()
        {
            _mainWindow.btnPing.IsEnabled = true;
            _mainWindow.btnPing.Content = BTN_START_TEXT;
            _mainWindow.btnStop.IsEnabled = false;
            _mainWindow.progressBar.Value = 0;
            _logger.Information("[MainWindow] Сбрасываем UI после завершения теста.");
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
                _logger.Information($"[MainWindow] Обновляем прогресс бар: {current}/{total}");
            });
        }

        public void HandleStopButtonClick()
        {
            _cancellationTokenSource?.Cancel();
            ResetUIAfterTest();
            _logger.Information("[MainWindow] Останавливаем пинг-тест по запросу пользователя.");
        }

        public async Task HandleShowGraphButtonClickAsync()
        {
            var roundtripTimes = await _pingService.GetRoundtripTimesAsync();
            if (roundtripTimes?.Count > 0)
            {
                HandleGraphWindow(roundtripTimes.ToList());
                _logger.Information($"[MainWindow] Показываем график с данными: {string.Join(", ", roundtripTimes)}");
            }
            else
            {
                MessageBox.Show(ERROR_NO_DATA, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.Warning("[MainWindow] Нет данных для отображения на графике.");
            }
        }

        private void HandleGraphWindow(List<int> roundtripTimes)
        {
            if (_graphWindow == null || !_graphWindow.IsLoaded)
            {
                CreateNewGraphWindow(roundtripTimes);
                _logger.Information("[MainWindow] Создаем новое окно графика.");
            }
            else
            {
                UpdateExistingGraphWindow(roundtripTimes);
                _logger.Information("[MainWindow] Обновляем существующее окно графика.");
            }
        }

        private void CreateNewGraphWindow(List<int> roundtripTimes)
        {
            if (int.TryParse(_mainWindow.txtPingCount.Text, out int pingInterval))
            {
                var logger = new SerilogLoggingService();
                _graphWindow = new GraphWindow(pingInterval, logger);
                if (_graphWindow != null)
                {
                    _graphWindow.SetPingData(roundtripTimes);
                    _graphWindow.Closed += HandleWindowClosed;
                    _graphWindow.Show();
                    _logger.Information($"[MainWindow] Создано новое окно графика с интервалом: {pingInterval}");
                }
                else
                {
                    _logger.Error("[MainWindow] Не удалось создать окно графика.");
                }
            }
        }

        private void UpdateExistingGraphWindow(List<int> roundtripTimes)
        {
            if (_graphWindow == null)
            {
                _logger.Warning("[MainWindow] Графическое окно null. Невозможно обновить состояние окна или установить данные пинга.");
                return;
            }

            if (_graphWindow.WindowState == WindowState.Minimized)
            {
                _graphWindow.WindowState = WindowState.Normal;
                // Удаляем вызов Activate
                _logger.Information("[MainWindow] Восстанавливаем окно графика из минимизированного состояния.");
            }
            else
            {
                _graphWindow.WindowState = WindowState.Minimized;
                _logger.Information("[MainWindow] Минимизируем окно графика.");
            }

            _graphWindow.SetPingData(roundtripTimes);
        }

        public void HandleTraceRouteButtonClick()
        {
            if (string.IsNullOrWhiteSpace(_mainWindow.txtURL.Text))
            {
                MessageBox.Show(ERROR_NO_URL, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                _logger.Warning("[MainWindow] Не указан URL для трассировки.");
                return;
            }

            HandleTraceWindow();
            _logger.Information("[MainWindow] Начинаем трассировку маршрута.");
        }

        private void HandleTraceWindow()
        {
            if (_traceWindow == null || !_traceWindow.IsLoaded)
            {
                _traceWindow = new TraceWindow(_mainWindow.txtURL.Text);
                _traceWindow.Closed += HandleWindowClosed;
                _traceWindow.Show();
                _logger.Information("[MainWindow] Создано новое окно трассировки.");
            }
            else
            {
                _traceWindow.Visibility = _traceWindow.IsVisible ? Visibility.Collapsed : Visibility.Visible;
                _logger.Information("[MainWindow] Переключаем видимость окна трассировки.");
            }
        }
    }
}