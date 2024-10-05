using Microsoft.Extensions.Caching.Memory;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PingTestTool
{
    public partial class TraceWindow : Window
    {
        private readonly Logger logger;
        private CancellationTokenSource _cts;
        private bool isTracing;
        private readonly string traceUrl;
        private ObservableCollection<TraceResult> traceResultsInternal = new ObservableCollection<TraceResult>();
        public ICollectionView TraceResults { get; private set; }
        private readonly IMemoryCache memoryCache;
        private readonly DnsManager dnsManager;
        private readonly PingManager pingManager;

        // Свойство для включения/отключения комбинированного лога
        public bool CombinedLogEnabled { get; set; }

        public TraceWindow(string url)
        {
            InitializeComponent();
            CombinedLogEnabled = chkCombinedLog.IsChecked ?? false;
            logger = new Logger("combined_log.txt", CombinedLogEnabled);
            memoryCache = new MemoryCache(new MemoryCacheOptions());

            TraceResults = CollectionViewSource.GetDefaultView(traceResultsInternal);
            ResultsList.ItemsSource = TraceResults;

            var sortedView = (CollectionView)TraceResults;
            sortedView.SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));

            dnsManager = new DnsManager(memoryCache, logger);
            pingManager = new PingManager(logger, dnsManager);
            traceUrl = url ?? throw new ArgumentNullException(nameof(url), "URL не может быть null.");
        }

        private void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
        {
            MessageBox.Show(message, title, button, icon);
        }

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            traceResultsInternal.Clear();
            pingManager.ClearHopData();
        }

        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            if (isTracing)
            {
                ShowMessage("Трассировка уже запущена.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(traceUrl))
            {
                ShowMessage("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnStartTrace.IsEnabled = false;
            btnStopTrace.IsEnabled = true;
            isTracing = true;

            StatusTextBlock.Text = "Трассировка запущена...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);

            await logger.LogAsync(LogLevel.INFO, $"Запуск трассировки для URL: {traceUrl}");

            using (_cts = new CancellationTokenSource())
            {
                try
                {
                    await pingManager.StartTraceAsync(traceUrl, _cts.Token, UpdateHopStatistics);
                    await logger.LogAsync(LogLevel.INFO, $"Начинаем трассировку хоста");
                }
                catch (OperationCanceledException)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Трассировка отменена.");
                }
                catch (Exception ex)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Ошибка: {ex.Message} \n{ex.StackTrace}");
                    ShowMessage($"Ошибка: {ex.Message} \n{ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnStartTrace.IsEnabled = true;
                    btnStopTrace.IsEnabled = false;
                    isTracing = false;

                    StatusTextBlock.Text = "Трассировка остановлена.";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    await logger.LogAsync(LogLevel.INFO, $"Конец трассировки \n");
                }
            }
        }

        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            StatusTextBlock.Text = "Остановка трассировки...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }

        private void UpdateHopStatistics(string ipAddress, int ttl, string domainName, HopData hop)
        {
            if (ipAddress == null)
            {
                throw new ArgumentNullException(nameof(ipAddress), "IP-адрес не может быть null.");
            }

            Dispatcher.Invoke(() =>
            {
                var existingResult = traceResultsInternal.FirstOrDefault(tr => tr.IPAddress == ipAddress);

                if (existingResult == null)
                {
                    var result = new TraceResult(ttl, ipAddress, domainName, hop);
                    traceResultsInternal.Add(result);
                }
                else
                {
                    existingResult.UpdateStatistics(hop);
                }
            });
        }

        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                    {
                        foreach (var result in traceResultsInternal)
                        {
                            writer.WriteLine(result?.ToString() ?? "Пустой результат");
                        }
                    }

                    ShowMessage("Результаты успешно сохранены.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowMessage($"Ошибка при сохранении результатов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void OnCombinedLogChecked(object sender, RoutedEventArgs e)
        {
            CombinedLogEnabled = true;
        }

        private void OnCombinedLogUnchecked(object sender, RoutedEventArgs e)
        {
            CombinedLogEnabled = false;
        }
    }
}



