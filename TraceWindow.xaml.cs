using Microsoft.Win32;
using Serilog;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

#nullable enable

namespace PingTestTool
{
    public partial class TraceWindow : Window
    {
        private readonly TraceManager _traceManager;
        public ICollectionView TraceResults { get; }

        public TraceWindow(string url)
        {
            InitializeComponent();
            _traceManager = new TraceManager(url);

            TraceResults = ConfigureTraceResults();
            Log.Information("[TraceWindow] Инициализирован с URL: {Url}", url);
        }

        private ICollectionView ConfigureTraceResults()
        {
            var view = CollectionViewSource.GetDefaultView(_traceManager.TraceResults);
            ResultsList.ItemsSource = view;
            ((CollectionView)view).SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
            return view;
        }

        private async Task HandleTraceStartAsync()
        {
            if (!ValidateTraceStart()) return;

            SetTraceControlsState(isStarting: true);
            UpdateStatus("Трассировка запущена...", Colors.Green);
            await _traceManager.StartTraceAsync(UpdateStatus, ShowMessage);
        }

        private bool ValidateTraceStart()
        {
            if (_traceManager.IsTracing)
            {
                ShowMessage("Трассировка уже запущена.", "Предупреждение");
                Log.Warning("[TraceWindow] Попытка запустить уже запущенную трассировку");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_traceManager.TraceUrl))
            {
                ShowMessage("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Error("[TraceWindow] URL для трассировки не указан");
                return false;
            }

            return true;
        }

        private void SaveResults(string fileName)
        {
            try
            {
                File.WriteAllLines(fileName, _traceManager.TraceResults.Select(result => result?.ToString() ?? "Пустой результат"));
                ShowMessage("Результаты успешно сохранены.", "Успех");
                Log.Information("[TraceWindow] Результаты сохранены в файл: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                ShowMessage($"Ошибка при сохранении результатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Error(ex, "[TraceWindow] Ошибка при сохранении результатов: {Message}", ex.Message);
            }
        }

        private void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
            => MessageBox.Show(message, title, button, icon);

        private void UpdateStatus(string message, Color color)
            => StatusTextBlock.Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(color);
            });

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            _traceManager.ClearResults();
            Log.Information("[TraceWindow] Результаты очищены");
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            SetTraceControlsState(isStarting: true);
            await HandleTraceStartAsync();
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _traceManager.StopTrace();
            UpdateStatus("Остановка трассировки...", Colors.Orange);
            Log.Information("[TraceWindow] Трассировка остановлена");
            SetTraceControlsState(isStarting: false);
        }

        private void SetTraceControlsState(bool isStarting)
        {
            btnStartTrace.IsEnabled = !isStarting;
            btnStopTrace.IsEnabled = isStarting;
        }

        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SaveResults(saveFileDialog.FileName);
            }
        }
    }
}