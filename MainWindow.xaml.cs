using Microsoft.Win32;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PingTestTool
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource cancellationTokenSource;
        private GraphWindow graphWindow;
        private IniFileSettings iniSettings;
        private TraceWindow traceWindow;
        private PingService pingService;

        public MainWindow()
        {
            InitializeComponent();
            string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PingTestSettings.ini");
            iniSettings = new IniFileSettings(iniFilePath);
            if (File.Exists(iniFilePath))
            {
                iniSettings.LoadSettings(this);
            }
            else
            {
                MessageBox.Show("INI-файл не найден. Будут использованы значения по умолчанию.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Information);
                iniSettings.SetDefaultSettings(this);
            }

            pingService = new PingService();
            pingService.OnPingResult += UpdateResults;
            pingService.OnProgressUpdate += UpdateProgressBar;
            pingService.OnRoundtripTimeAdded += UpdateGraph;

            this.Closed += MainWindow_Closed;
        }

        public class IniFileSettings
        {
            private readonly IniFile iniFile;
            private const string SettingsSection = "PingTestSettings";

            public IniFileSettings(string filePath)
            {
                iniFile = new IniFile(filePath);
            }

            public void LoadSettings(MainWindow mainWindow)
            {
                mainWindow.txtURL.Text = iniFile.Read(SettingsSection, "URL") ?? "google.com";
                mainWindow.txtPingCount.Text = iniFile.Read(SettingsSection, "PingCount") ?? "10";
                mainWindow.txtTimeout.Text = iniFile.Read(SettingsSection, "Timeout") ?? "1000";
                mainWindow.txtLogFile.Text = iniFile.Read(SettingsSection, "LogFile") ?? "C:\\ping_log.txt";
            }

            public void SaveSettings(MainWindow mainWindow)
            {
                iniFile.Write(SettingsSection, "URL", mainWindow.txtURL.Text);
                iniFile.Write(SettingsSection, "PingCount", mainWindow.txtPingCount.Text);
                iniFile.Write(SettingsSection, "Timeout", mainWindow.txtTimeout.Text);
                iniFile.Write(SettingsSection, "LogFile", mainWindow.txtLogFile.Text);
            }

            public void SetDefaultSettings(MainWindow mainWindow)
            {
                mainWindow.txtURL.Text = "google.com";
                mainWindow.txtPingCount.Text = "10";
                mainWindow.txtTimeout.Text = "1000";
                mainWindow.txtLogFile.Text = "C:\\ping_log.txt";
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (graphWindow != null)
            {
                graphWindow.Close();
            }
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (btnPing.Content.ToString() == "Запустить тест")
                {
                    if (ValidateInput())
                    {
                        iniSettings.SaveSettings(this);
                        btnPing.IsEnabled = false;
                        btnStop.IsEnabled = true;
                        btnPing.Content = "Ожидаем...";

                        cancellationTokenSource = new CancellationTokenSource();

                        string url = txtURL.Text;
                        int pingCount = int.Parse(txtPingCount.Text);
                        int timeout = int.Parse(txtTimeout.Text);
                        string logFile = txtLogFile.Text;

                        txtResults.Clear();
                        pingService.ClearRoundtripTimes();

                        string result = await Task.Run(() => 
                        pingService.StartPingTestAsync(url, pingCount, timeout, cancellationTokenSource.Token));
                        await WriteLogFileAsync(logFile, result);

                        btnPing.IsEnabled = true;
                        btnPing.Content = "Запустить тест";
                        btnStop.IsEnabled = false;
                        progressBar.Value = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task WriteLogFileAsync(string iniFilePath, string content)
        {
            using (var writer = new StreamWriter(iniFilePath))
            {
                await writer.WriteAsync(content);
            }
        }

        private void UpdateResults(string result)
        {
            Dispatcher.Invoke(() =>
            {
                txtResults.AppendText(result);
            });
        }

        private void UpdateGraph(int roundtripTime)
        {
            Dispatcher.Invoke(() =>
            {
                if (graphWindow != null)
                {
                    var roundtripTimes = pingService.GetRoundtripTimes();
                    graphWindow.SetPingData(roundtripTimes);
                }
            });
        }

        private static readonly Regex CyrillicRegex = new Regex(@"[\u0400-\u04FF]", RegexOptions.Compiled);

        private bool ValidateInput()
        {
            bool isValid = true;
            HideAllWarnings();

            if (string.IsNullOrWhiteSpace(txtURL.Text))
            {
                ShowWarning(imgWarning_3, "URL не может быть пустым.");
                isValid = false;
            }

            string input = txtURL.Text;
            if (CyrillicRegex.IsMatch(input))
            {
                ShowWarning(imgWarning_3, "URL не может иметь кириллицу.");
                isValid = false;
            }

            if (!int.TryParse(txtPingCount.Text, out int pingCount))
            {
                ShowWarning(imgWarning_1, "Количество пакетов должно быть числом.");
                isValid = false;
            }
            else if (pingCount <= 0)
            {
                ShowWarning(imgWarning_1, "Количество пакетов должно быть больше нуля.");
                isValid = false;
            }

            if (!int.TryParse(txtTimeout.Text, out int timeout))
            {
                ShowWarning(imgWarning_2, "Таймаут должен быть числом.");
                isValid = false;
            }
            else if (timeout < 500)
            {
                ShowWarning(imgWarning, "Таймаут не может быть меньше 500 мс.");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(txtLogFile.Text))
            {
                ShowWarning(imgWarning_2, "Файл лога не может быть пустым.");
                isValid = false;
            }

            return isValid;
        }

        private void HideAllWarnings()
        {
            imgWarning.Visibility = Visibility.Collapsed;
            imgWarning_1.Visibility = Visibility.Collapsed;
            imgWarning_2.Visibility = Visibility.Collapsed;
            imgWarning_3.Visibility = Visibility.Collapsed;
        }

        private void ShowWarning(Image warningImage, string message)
        {
            warningImage.Visibility = Visibility.Visible;
            MessageBox.Show(message, "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning);
        }


        private void UpdateProgressBar(int current, int total)
        {
            Dispatcher.Invoke(() =>
            {
                double progress = (current * 100.0) / total;
                progressBar.Value = progress;
            });
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                txtLogFile.Text = saveFileDialog.FileName;
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                btnStop.IsEnabled = false;
                btnPing.IsEnabled = true;
                btnPing.Content = "Запустить тест";
            }
        }

        private void BtnShowGraph_Click(object sender, RoutedEventArgs e)
        {
            var roundtripTimes = pingService.GetRoundtripTimes();
            if (roundtripTimes.Count > 0)
            {
                if (graphWindow == null)
                {
                    int pingInterval = int.Parse(txtPingCount.Text);
                    graphWindow = new GraphWindow(pingInterval);
                    graphWindow.SetPingData(roundtripTimes);
                    graphWindow.Closed += GraphWindow_Closed;
                    graphWindow.Show();
                }
                else
                {
                    if (graphWindow.WindowState == WindowState.Minimized)
                    {
                        graphWindow.WindowState = WindowState.Normal;
                        graphWindow.Activate();
                    }
                    else
                    {
                        graphWindow.WindowState = WindowState.Minimized;
                    }

                    graphWindow.SetPingData(roundtripTimes);
                }
            }
            else
            {
                MessageBox.Show("Нет данных пинга для отображения.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void GraphWindow_Closed(object sender, EventArgs e)
        {
            graphWindow = null;
        }

        private void BtnTraceRoute_Click(object sender, RoutedEventArgs e)
        {
            string url = txtURL.Text;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Пожалуйста, укажите URL для трассировки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (traceWindow == null || !traceWindow.IsLoaded)
            {
                traceWindow = new TraceWindow(url);
                traceWindow.Closed += GraphWindow_Closed;
                traceWindow.Show();
            }
            else
            {
                if (traceWindow.IsVisible)
                {
                    traceWindow.Hide();
                }
                else
                {
                    traceWindow.Show();
                }
            }
        }
    }
}
