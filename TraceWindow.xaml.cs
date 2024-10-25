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
    /// <summary>
    /// Окно для трассировки маршрута до указанного URL.
    /// </summary>
    public partial class TraceWindow : Window
    {
        #region Поля

        private readonly Logger logger;
        private CancellationTokenSource _cts;
        private bool isTracing;
        private readonly string traceUrl;
        private ObservableCollection<TraceResult> traceResultsInternal = new ObservableCollection<TraceResult>();
        private readonly IMemoryCache memoryCache;
        private readonly DnsManager dnsManager;
        private readonly PingManager pingManager;

        #endregion

        #region Свойства

        public ICollectionView TraceResults { get; private set; }
        public bool CombinedLogEnabled { get; set; }

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="TraceWindow"/>.
        /// </summary>
        /// <param name="url">URL для трассировки.</param>
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

        #endregion

        #region Приватные методы

        /// <summary>
        /// Отображает сообщение пользователю.
        /// </summary>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="title">Заголовок окна сообщения.</param>
        /// <param name="button">Кнопки для отображения.</param>
        /// <param name="icon">Иконка для отображения.</param>
        private void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
        {
            MessageBox.Show(message, title, button, icon);
        }

        /// <summary>
        /// Обновляет статистику для указанного IP-адреса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес.</param>
        /// <param name="ttl">TTL (Time to Live).</param>
        /// <param name="domainName">Доменное имя.</param>
        /// <param name="hop">Данные о хопе.</param>
        private void UpdateHopStatistics(string ipAddress, int ttl, string domainName, HopData hop)
        {
            if (ipAddress is null)
            {
                throw new ArgumentNullException(nameof(ipAddress), "IP-адрес не может быть null.");
            }

            Dispatcher.Invoke(() =>
            {
                var existingResult = traceResultsInternal.FirstOrDefault(tr => tr.IPAddress == ipAddress);

                if (existingResult is null)
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

        #endregion

        #region Обработчики событий

        /// <summary>
        /// Обработчик события нажатия на кнопку "Очистить результаты".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            traceResultsInternal.Clear();
            pingManager.ClearHopData();
        }

        /// <summary>
        /// Обработчик события нажатия на кнопку "Запустить трассировку".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
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
                    await logger.LogAsync(LogLevel.INFO, "Начинаем трассировку хоста");
                }
                catch (OperationCanceledException)
                {
                    await logger.LogAsync(LogLevel.WARNING, "Трассировка отменена.");
                }
                catch (Exception ex)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Ошибка: {ex.Message}\n{ex.StackTrace}");
                    ShowMessage($"Ошибка: {ex.Message}\n{ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnStartTrace.IsEnabled = true;
                    btnStopTrace.IsEnabled = false;
                    isTracing = false;

                    StatusTextBlock.Text = "Трассировка остановлена.";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    await logger.LogAsync(LogLevel.INFO, "Конец трассировки\n");
                }
            }
        }

        /// <summary>
        /// Обработчик события нажатия на кнопку "Остановить трассировку".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void BtnStopTrace_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            StatusTextBlock.Text = "Остановка трассировки...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }

        /// <summary>
        /// Обработчик события нажатия на кнопку "Сохранить результаты".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void BtnSaveResults_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(saveFileDialog.FileName))
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

        /// <summary>
        /// Обработчик события проверки флажка "Combined Log".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void OnCombinedLogChecked(object sender, RoutedEventArgs e) => CombinedLogEnabled = true;

        /// <summary>
        /// Обработчик события снятия флажка "Combined Log".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void OnCombinedLogUnchecked(object sender, RoutedEventArgs e) => CombinedLogEnabled = false;

        #endregion
    }
}