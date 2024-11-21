#nullable enable

namespace PingTestTool
{

    public interface IInputValidator
    {
        ValidationResult ValidateInput(string url, string pingCount, string timeout);
    }

    public interface IWarningPresenter
    {
        void HideAllWarnings();
        void ShowWarnings(ValidationResult result);
    }

    public interface IPingTestService
    {
        Task StartPingTestAsync(PingConfiguration config, CancellationToken cancellationToken);
        Task<List<int>?> GetRoundtripTimesAsync();
        Task ClearRoundtripTimesAsync();
        event Action<string> OnPingResult;
        event Action<int, int> OnProgressUpdate;
        event Action<int> OnRoundtripTimeAdded;
    }

    public sealed class ValidationResult
    {
        public List<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;

        public ValidationResult(List<string> errors)
        {
            Errors = errors ?? new List<string>();
        }
    }

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
            await _eventHandler?.HandlePingButtonClickAsync();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler?.HandleStopButtonClick();
        }

        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e)
        {
            await _eventHandler?.HandleShowGraphButtonClickAsync();
        }

        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e)
        {
            _eventHandler?.HandleTraceRouteButtonClick();
        }
    }
}