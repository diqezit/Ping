using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;

namespace PingTestTool
{
    public class PingService
    {
        public event Action<string> OnPingResult;
        public event Action<int, int> OnProgressUpdate;
        public event Action<int> OnRoundtripTimeAdded;
        private List<int> roundtripTimes = new List<int>();

        public async Task<string> StartPingTestAsync(string url, int pingCount, int timeout, CancellationToken cancellationToken)
        {
            // Проверка входных параметров
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL не может быть пустым.", nameof(url));
            if (pingCount <= 0)
                throw new ArgumentException("Количество пингов должно быть больше нуля.", nameof(pingCount));
            if (timeout <= 0)
                throw new ArgumentException("Таймаут должен быть больше нуля.", nameof(timeout));

            var options = new PingOptions { DontFragment = true };
            var responseTimes = new StringBuilder();
            var logBuilder = new StringBuilder();
            DateTime startTime = DateTime.Now;

            string startMessage = $"Тест Ping для {url} начат в: {startTime:dd.MM.yyyy HH:mm:ss}";
            string separator = new string('-', 50);
            logBuilder.AppendLine(startMessage);
            logBuilder.AppendLine(separator);
            OnPingResult?.Invoke(startMessage + Environment.NewLine + separator + Environment.NewLine);

            int successfulPings = 0;
            int failedPings = 0;

            using (var ping = new Ping())
            {
                for (int i = 0; i < pingCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        OnPingResult?.Invoke("Тест был остановлен." + Environment.NewLine);
                        return "Тест был остановлен.";
                    }

                    // Инициализация и старт Stopwatch перед выполнением пинга
                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        var pingResult = await PerformSinglePingAsync(ping, url, timeout, i + 1, pingCount).ConfigureAwait(false);
                        responseTimes.AppendLine(pingResult.Message);

                        if (pingResult.IsSuccess)
                        {
                            roundtripTimes.Add(pingResult.RoundtripTime);
                            successfulPings++;
                            OnRoundtripTimeAdded?.Invoke(pingResult.RoundtripTime);
                        }
                        else
                        {
                            failedPings++;
                        }

                        // Остановка Stopwatch после выполнения пинга
                        stopwatch.Stop();

                        // Рассчитываем задержку между пингами
                        int delay = CalculateDelay(timeout, pingResult.ElapsedMilliseconds);
                        if (delay > 0)
                            await Task.Delay(delay).ConfigureAwait(false);
                    }
                    catch (PingException pingEx)
                    {
                        // Остановка Stopwatch при возникновении исключения
                        stopwatch.Stop();

                        string errorLine = $"Ошибка пинга: {pingEx.Message}";
                        OnPingResult?.Invoke(errorLine + Environment.NewLine);

                        // Удаление использования неинициализированного stopwatch.ElapsedMilliseconds
                        return errorLine;
                    }
                }
            }

            // Логирование по завершению теста
            DateTime endTime = DateTime.Now;
            logBuilder.AppendLine(responseTimes.ToString());
            logBuilder.AppendLine(separator);
            logBuilder.AppendLine($"Тест Ping завершен в: {endTime:dd.MM.yyyy HH:mm:ss}");

            string summary = GenerateSummary(startTime, endTime, successfulPings, failedPings, pingCount);
            logBuilder.AppendLine(summary);

            OnPingResult?.Invoke(summary);
            return logBuilder.ToString();
        }


        private async Task<(bool IsSuccess, int RoundtripTime, string Message, long ElapsedMilliseconds)> PerformSinglePingAsync(Ping ping, string url, int timeout, int currentPing, int totalPings)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                PingReply reply = await ping.SendPingAsync(url, timeout).ConfigureAwait(false);
                stopwatch.Stop();

                if (reply.Status == IPStatus.Success)
                {
                    string resultLine = $"{DateTime.Now:HH:mm:ss} - Ответ от {url}: {reply.RoundtripTime} мс, TTL={reply.Options.Ttl}";
                    OnPingResult?.Invoke(resultLine + Environment.NewLine);
                    OnProgressUpdate?.Invoke(currentPing, totalPings);
                    return (true, (int)reply.RoundtripTime, resultLine, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    string errorLine = $"{DateTime.Now:HH:mm:ss} - Ошибка: {reply.Status}";
                    OnPingResult?.Invoke(errorLine + Environment.NewLine);
                    OnProgressUpdate?.Invoke(currentPing, totalPings);
                    return (false, 0, errorLine, stopwatch.ElapsedMilliseconds);
                }
            }
            catch (PingException pingEx)
            {
                stopwatch.Stop();
                throw new PingException($"Ошибка пинга: {pingEx.Message} Проверьте правильность адреса.");
            }
        }

        private int CalculateDelay(int timeout, long elapsedMilliseconds)
        {
            int delay = timeout - (int)elapsedMilliseconds;
            return delay > 0 ? delay : 0;
        }

        private string GenerateSummary(DateTime startTime, DateTime endTime, int successfulPings, int failedPings, int totalPings)
        {
            double totalSeconds = (endTime - startTime).TotalSeconds;
            string executionTime = totalSeconds > 59
                ? $"{Math.Round(totalSeconds / 60, 2)} минут"
                : $"{Math.Round(totalSeconds, 2)} секунд";

            double avgJitter = CalculateAverageJitter(roundtripTimes);
            string separator = new string('-', 50);

            return $"{separator}{Environment.NewLine}" +
                   $"Тест Ping завершен в: {endTime:dd.MM.yyyy HH:mm:ss}{Environment.NewLine}" +
                   $"Общее время выполнения: {executionTime}{Environment.NewLine}" +
                   $"Средний джиттер: {avgJitter} мс{Environment.NewLine}" +
                   $"Потеряно пакетов: {failedPings} из {totalPings}{Environment.NewLine}";
        }

        public List<int> GetRoundtripTimes()
        {
            return new List<int>(roundtripTimes);
        }

        public void ClearRoundtripTimes()
        {
            roundtripTimes.Clear();
        }

        private double CalculateAverageJitter(List<int> roundtripTimes)
        {
            if (roundtripTimes.Count <= 1) return 0;

            double totalJitter = 0;
            double lastTime = roundtripTimes[0];

            for (int i = 1; i < roundtripTimes.Count; i++)
            {
                totalJitter += Math.Abs(roundtripTimes[i] - lastTime);
                lastTime = roundtripTimes[i];
            }

            return Math.Round(totalJitter / (roundtripTimes.Count - 1), 2);
        }
    }
}