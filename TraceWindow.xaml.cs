//#nullable enable

//namespace PingTestTool
//{
//    public partial class TraceWindow : Window, ITraceWindow
//    {
//        private readonly TraceManager _traceManager;
//        public ICollectionView TraceResults { get; }

//        public TraceWindow(string url)
//        {
//            InitializeComponent();
//            _traceManager = new TraceManager(url);
//            TraceResults = CollectionViewSource.GetDefaultView(_traceManager.TraceResults);
//            ResultsList.ItemsSource = TraceResults;
//            ((CollectionView)TraceResults).SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
//        }

//        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
//        {
//            if (!ValidateStart()) return;
//            SetControlsState(true);
//            await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
//        }

//        private bool ValidateStart()
//        {
//            if (_traceManager.IsTracing)
//            {
//                ShowMessage("Трассировка уже запущена.", "Предупреждение");
//                return false;
//            }
//            if (string.IsNullOrWhiteSpace(_traceManager.TraceUrl))
//            {
//                ShowMessage("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
//                return false;
//            }
//            return true;
//        }

//        private void SaveResults(string fileName)
//        {
//            try
//            {
//                System.IO.File.WriteAllLines(fileName, _traceManager.TraceResults.Select(r => r?.ToString() ?? "Пустой результат"));
//                ShowMessage("Результаты успешно сохранены.", "Успех");
//            }
//            catch (Exception ex)
//            {
//                ShowMessage($"Ошибка при сохранении результатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
//            }
//        }

//        private void ShowMessage(string msg, string title, MessageBoxButton btn = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information) =>
//            MessageBox.Show(msg, title, btn, icon);

//        private void UpdateStatus(string msg, Color color) =>
//            StatusTextBlock.Dispatcher.Invoke(() =>
//            {
//                StatusTextBlock.Text = msg;
//                StatusTextBlock.Foreground = new SolidColorBrush(color);
//            });

//        private void BtnClearResults_Click(object sender, RoutedEventArgs e) => _traceManager.ClearResults();
//        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
//        {
//            _traceManager.StopTrace();
//            UpdateStatus("Остановка трассировки...", Colors.Orange);
//            SetControlsState(false);
//        }

//        private void SetControlsState(bool tracing)
//        {
//            btnStartTrace.IsEnabled = !tracing;
//            btnStopTrace.IsEnabled = tracing;
//        }

//        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
//        {
//            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*" };
//            if (dlg.ShowDialog() == true)
//                SaveResults(dlg.FileName);
//        }

//        private void DarkTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("Themes/DarkTheme.xaml");
//        private void LightTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("Themes/LightTheme.xaml");
//        private void RussianLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguage("Resources/StringResources.ru.xaml");
//        private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => ApplyLanguage("Resources/StringResources.en.xaml");

//        private void ApplyTheme(string themePath) => ApplyResourceDictionary(themePath, "Themes/");
//        private void ApplyLanguage(string languagePath) => ApplyResourceDictionary(languagePath, "Resources/");


//        private void ApplyResourceDictionary(string resourcePath, string baseDir)
//        {
//            var uri = new Uri(resourcePath, UriKind.Relative);
//            UpdateResourceDictionaries(Application.Current.Resources.MergedDictionaries, uri);
//            UpdateResourceDictionaries(Resources.MergedDictionaries, uri);
//        }

//        private static void UpdateResourceDictionaries(Collection<ResourceDictionary> dictionaries, Uri newUri)
//        {
//            for (int i = dictionaries.Count - 1; i >= 0; i--)
//            {
//                if (dictionaries[i].Source != null && dictionaries[i].Source.ToString().StartsWith(newUri.ToString().StartsWith("Themes/") ? "Themes/" : "Resources/"))
//                {
//                    dictionaries.RemoveAt(i);
//                }
//            }
//            dictionaries.Add(new ResourceDictionary { Source = newUri });
//        }
//    }
//}