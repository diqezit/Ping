using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    public class PingService
    {
        #region События

        public event Action<string> OnPingResult;
        public event Action<int, int> OnProgressUpdate;
        public event Action<int> OnRoundtripTimeAdded;

        #endregion

        #region Приватные поля

        public List<int> roundtripTimes = new();

        #endregion

        #region Публичные методы

        public async Task<string> StartPingTestAsync(string url, int pingCount, int timeout, CancellationToken cancellationToken)
        {
            #region Проверка входных параметров

            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL не может быть пустым.", nameof(url));
            if (pingCount <= 0)
                throw new ArgumentException("Количество пингов должно быть больше нуля.", nameof(pingCount));
            if (timeout <= 0)
                throw new ArgumentException("Таймаут должен быть больше нуля.", nameof(timeout));

            #endregion

            var options = new PingOptions { DontFragment = true };
            var responseTimes = new StringBuilder();
            var logBuilder = new StringBuilder();
            DateTime startTime = DateTime.Now;

            logBuilder.AppendLine($"Тест Ping для {url} начат в: {startTime:dd.MM.yyyy HH:mm:ss}")
                      .AppendLine(new string('-', 50));
            OnPingResult?.Invoke(logBuilder.ToString());

            int successfulPings = 0;
            int failedPings = 0;

            using var ping = new Ping();
            for (int i = 0; i < pingCount; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    OnPingResult?.Invoke("Тест был остановлен." + Environment.NewLine);
                    return "Тест был остановлен.";
                }

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

                    stopwatch.Stop();
                    int delay = CalculateDelay(timeout, pingResult.ElapsedMilliseconds);
                    if (delay > 0)
                        await Task.Delay(delay).ConfigureAwait(false);
                }
                catch (PingException pingEx)
                {
                    stopwatch.Stop();
                    string errorLine = $"Ошибка пинга: {pingEx.Message}";
                    OnPingResult?.Invoke(errorLine + Environment.NewLine);
                    return errorLine; // Возвращаем ошибку
                }
            }

            #region Логирование по завершению теста

            DateTime endTime = DateTime.Now;
            logBuilder.AppendLine(responseTimes.ToString())
                      .AppendLine(new string('-', 50))
                      .AppendLine($"Тест Ping завершен в: {endTime:dd.MM.yyyy HH:mm:ss}");

            string summary = GenerateSummary(startTime, endTime, successfulPings, failedPings, pingCount);
            logBuilder.AppendLine(summary);
            OnPingResult?.Invoke(summary);

            return logBuilder.ToString();

            #endregion
        }

        #endregion

        #region Приватные методы

        private async Task<(bool IsSuccess, int RoundtripTime, string Message, long ElapsedMilliseconds)> PerformSinglePingAsync(Ping ping, string url, int timeout, int currentPing, int totalPings)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                PingReply reply = await ping.SendPingAsync(url, timeout).ConfigureAwait(false);
                stopwatch.Stop();

                string resultLine = reply.Status == IPStatus.Success
                    ? $"{DateTime.Now:HH:mm:ss} - Ответ от {url}: {reply.RoundtripTime} мс, TTL={reply.Options.Ttl}"
                    : $"{DateTime.Now:HH:mm:ss} - Ошибка: {reply.Status}";

                // Вызываем события независимо от результата
                OnPingResult?.Invoke(resultLine + Environment.NewLine);
                OnProgressUpdate?.Invoke(currentPing, totalPings);

                return (reply.Status == IPStatus.Success, (int)reply.RoundtripTime, resultLine, stopwatch.ElapsedMilliseconds);
            }
            catch (PingException pingEx)
            {
                stopwatch.Stop();
                throw new PingException($"Ошибка пинга: {pingEx.Message}. Проверьте правильность адреса.");
            }
        }

        private int CalculateDelay(int timeout, long elapsedMilliseconds) => Math.Max(0, timeout - (int)elapsedMilliseconds);

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

        private double CalculateAverageJitter(List<int> roundtripTimes)
        {
            if (roundtripTimes.Count <= 1) return 0;

            double totalJitter = 0;
            for (int i = 1; i < roundtripTimes.Count; i++)
            {
                totalJitter += Math.Abs(roundtripTimes[i] - roundtripTimes[i - 1]);
            }

            return Math.Round(totalJitter / (roundtripTimes.Count - 1), 2);
        }

        #endregion

        #region Публичные методы для работы с roundtripTimes

        public List<int> GetRoundtripTimes() => new(roundtripTimes);

        public void ClearRoundtripTimes() => roundtripTimes.Clear();

        #endregion
    }
}