#nullable enable

namespace PingTestTool
{
    // -------------------- Interfaces --------------------

    public interface IMainWindowHandler
    {
        void HandleWindowClosed(object? sender, EventArgs e);
        Task HandlePingButtonClickAsync();
        void HandleStopButtonClick();
        Task HandleShowGraphButtonClickAsync();
        void HandleTraceRouteButtonClick();
    }

    public interface ILoggingService
    {
        void Information(string messageTemplate, params object[] propertyValues);
        void Warning(string messageTemplate, params object[] propertyValues);
        void Error(Exception ex, string messageTemplate, params object[] propertyValues);
        void Error(string messageTemplate, params object[] propertyValues);
        void Fatal(Exception ex, string messageTemplate, params object[] propertyValues);
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

    // -------------------- Models --------------------

    public sealed class ValidationResult
    {
        public List<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;

        public ValidationResult(List<string> errors)
        {
            Errors = errors ?? new List<string>();
        }
    }

    // -------------------- Implementations --------------------

    public class SerilogLoggingService : ILoggingService
    {
        public void Information(string messageTemplate, params object[] propertyValues)
            => Log.Information(messageTemplate, propertyValues);

        public void Warning(string messageTemplate, params object[] propertyValues)
            => Log.Warning(messageTemplate, propertyValues);

        public void Error(Exception ex, string messageTemplate, params object[] propertyValues)
            => Log.Error(ex, messageTemplate, propertyValues);

        public void Error(string messageTemplate, params object[] propertyValues)
            => Log.Error(messageTemplate, propertyValues);

        public void Fatal(Exception ex, string messageTemplate, params object[] propertyValues)
            => Log.Fatal(ex, messageTemplate, propertyValues);
    }

    public class InputValidator : IInputValidator
    {
        private readonly ILoggingService _logger;

        public InputValidator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValidationResult ValidateInput(string url, string pingCount, string timeout)
        {
            var errors = new List<string>();

            errors.AddRange(ValidationHelper.ValidateUrl(url, _logger));
            errors.AddRange(ValidationHelper.ValidatePingCount(pingCount, _logger));
            errors.AddRange(ValidationHelper.ValidateTimeout(timeout, _logger));

            return new ValidationResult(errors);
        }
    }

    public class WarningPresenter : IWarningPresenter
    {
        private readonly Image[] _warningImages;

        public WarningPresenter(params Image[] warningImages)
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

    // -------------------- Helpers --------------------

    public static class ValidationHelper
    {
        private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
        private const int MIN_TIMEOUT = 100;
        private const int MIN_PING_COUNT = 1;
        private const int MAX_PING_COUNT = 1000;

        public static List<string> ValidateUrl(string url, ILoggingService logger)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(url))
            {
                errors.Add("URL не может быть пустым.");
                logger.Warning("Пустой URL при валидации");
            }
            else if (CyrillicRegex.IsMatch(url))
            {
                errors.Add("URL не может иметь кириллицу.");
                logger.Warning("URL содержит кириллические символы");
            }

            return errors;
        }

        public static List<string> ValidatePingCount(string pingCount, ILoggingService logger)
        {
            var errors = new List<string>();

            if (!int.TryParse(pingCount, out int count) || count < MIN_PING_COUNT || count > MAX_PING_COUNT)
            {
                errors.Add($"Количество пакетов должно быть целым числом между {MIN_PING_COUNT} и {MAX_PING_COUNT}.");
                logger.Warning("Некорректное количество пакетов");
            }

            return errors;
        }

        public static List<string> ValidateTimeout(string timeout, ILoggingService logger)
        {
            var errors = new List<string>();

            if (!int.TryParse(timeout, out int time) || time < MIN_TIMEOUT)
            {
                errors.Add($"Таймаут должен быть целым числом не менее {MIN_TIMEOUT} мс.");
                logger.Warning($"Некорректный таймаут: {timeout}");
            }

            return errors;
        }
    }

    // -------------------- Event Handlers --------------------

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
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
            _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
            _warningPresenter = warningPresenter ?? throw new ArgumentNullException(nameof(warningPresenter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
                if (_graphWindow != null)
                {
                    var roundtripTimes = await _pingService.GetRoundtripTimesAsync();
                    _graphWindow.SetPingData(roundtripTimes.ToList());
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
            }
            else
            {
                UpdateExistingGraphWindow(roundtripTimes);
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
                _traceWindow = new TraceWindow(_mainWindow.txtURL.Text, _logger);
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

    // -------------------- Main Window --------------------

    public partial class MainWindow : Window
    {
        public const string DEFAULT_URL = "8.8.8.8";
        public const int DEFAULT_PING_COUNT = 10;
        public const int DEFAULT_TIMEOUT = 1000;

        internal MainWindowEventHandler? _eventHandler;
        private readonly ILoggingService _logger;

        public MainWindow()
            : this(new PingService())
        {
        }

        public MainWindow(IPingTestService pingService)
        {
            InitializeComponent();
            _logger = new SerilogLoggingService();
            InitializeComponents(pingService);
            SetupExceptionHandling();
        }

        private void InitializeComponents(IPingTestService pingService)
        {
            var warningManager = new WarningPresenter(
                imgWarning, imgWarning_1, imgWarning_3
            );

            var inputValidator = new InputValidator(_logger);
            _eventHandler = new MainWindowEventHandler(
                this,
                pingService,
                inputValidator,
                warningManager,
                _logger
            );
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                var exception = eventArgs.ExceptionObject as Exception;
                if (exception != null)
                {
                    _logger.Fatal(exception, exception.Message);
                    MessageBox.Show($"Критическая ошибка: {exception.Message}",
                          "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            if (_eventHandler != null)
            {
                this.Closed += _eventHandler.HandleWindowClosed;
            }
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            if (_eventHandler != null)
            {
                await _eventHandler.HandlePingButtonClickAsync();
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler?.HandleStopButtonClick();
        }

        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e)
        {
            if (_eventHandler != null)
            {
                await _eventHandler.HandleShowGraphButtonClickAsync();
            }
        }

        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler?.HandleTraceRouteButtonClick();
        }
    }
}