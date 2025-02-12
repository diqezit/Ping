#nullable enable
namespace PingTestTool
{
    public partial class MainWindow : Window
    {
        #region Constants
        public const string DEFAULT_URL = "8.8.8.8";
        public const int DEFAULT_PING_COUNT = 10;
        public const int DEFAULT_TIMEOUT = 1000;
        private static readonly string _themeBaseDir = "Themes/";
        private static readonly string _languageBaseDir = "Resources";
        #endregion

        #region Fields
        private readonly MainWindowEventHandler _handler;
        #endregion

        #region Constructor
        public MainWindow() : this(new PingService()) { }

        public MainWindow(IPingTestService pingService)
        {
            InitializeComponent();
            var warningPresenter = new WarningPresenter(imgWarning, imgWarning_1, imgWarning_3);
            var inputValidator = new InputValidator();
            _handler = new MainWindowEventHandler(this, pingService, inputValidator, warningPresenter);
            _handler.HideWarningsOnStartup();
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            Closed += _handler.HandleWindowClosed;
            InitializeDefaultValues();
            ApplyLanguage("StringResources.en.xaml");
        }

        private void InitializeDefaultValues()
        {
            txtURL.Text = DEFAULT_URL;
            txtPingCount.Text = DEFAULT_PING_COUNT.ToString();
            txtTimeout.Text = DEFAULT_TIMEOUT.ToString();
        }
        #endregion

        #region Event Handlers
        #region Button Click Events
        private async void BtnPing_Click(object sender, RoutedEventArgs e) => await _handler.HandlePingButtonClickAsync();
        private void BtnStop_Click(object sender, RoutedEventArgs e) => _handler.HandleStopButtonClick();
        private async void BtnShowGraph_Click(object sender, RoutedEventArgs e) => await _handler.HandleShowGraphButtonClickAsync();
        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e) => _handler.HandleTraceRouteButtonClick();
        #endregion

        #region Theme Switch Events
        private void DarkTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("DarkTheme.xaml");
        private void LightTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("LightTheme.xaml");
        #endregion

        #region Language Switch Events
        private void RussianLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguage("StringResources.ru.xaml");
        private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguage("StringResources.en.xaml");
        #endregion

        #region Unhandled Exception Event
        private static void HandleUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                MessageBox.Show($"{FindResourceStringStatic("CriticalError")}: {ex.Message}", FindResourceStringStatic("ErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        #endregion
        #endregion

        #region Theme & Language Application
        private void ApplyTheme(string themeFileName) => ApplyResourceDictionary($"{_themeBaseDir}{themeFileName}", _themeBaseDir);
        private void ApplyLanguage(string languageFileName) => ApplyResourceDictionary($"{_languageBaseDir}/{languageFileName}", _languageBaseDir);

        private void ApplyResourceDictionary(string resourcePath, string baseDir)
        {
            var uri = new Uri($"pack://application:,,,/{GetType().Assembly.GetName().Name};component/{resourcePath}", UriKind.Absolute);
            var newResourceDictionary = new ResourceDictionary { Source = uri };
            UpdateResourceDictionaries(Application.Current.Resources.MergedDictionaries, newResourceDictionary, baseDir);
            UpdateResourceDictionaries(Resources.MergedDictionaries, newResourceDictionary, baseDir);
        }
        #endregion

        #region Resource Dictionary Update
        private static void UpdateResourceDictionaries(Collection<ResourceDictionary> dictionaries, ResourceDictionary newResourceDictionary, string baseDir)
        {
            for (int i = dictionaries.Count - 1; i >= 0; i--)
                if (dictionaries[i].Source?.ToString().StartsWith(baseDir) == true)
                    dictionaries.RemoveAt(i);
            dictionaries.Add(newResourceDictionary);
        }
        #endregion

        private static string FindResourceStringStatic(string resourceKey) => Application.Current.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";
    }
}