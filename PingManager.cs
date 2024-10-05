using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    public class PingManager
    {
        private const int BufferSize = 32;
        private const int MaxTtl = 12;
        private const int Timeout = 5000;
        private const int ParallelRequests = 1;
        private const int adaptiveDelay = 1000;
        private readonly Logger logger;
        private readonly DnsManager dnsManager;
        private readonly ConcurrentDictionary<string, HopData> hopData = new ConcurrentDictionary<string, HopData>();

        public PingManager(Logger logger, DnsManager dnsManager)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.dnsManager = dnsManager ?? throw new ArgumentNullException(nameof(dnsManager));
        }

        public async Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentNullException(nameof(host), "Хост не может быть null или пустым.");
            }

            var options = new PingOptions { DontFragment = true };
            var buffer = new byte[BufferSize];

            await logger.LogAsync(LogLevel.INFO, $"Начинаем трассировку для хоста: {host}");

            while (!token.IsCancellationRequested)
            {
                var (currentMaxTtl, delay) = AdjustTtlAndDelay();

                var pingTasks = Enumerable.Range(1, currentMaxTtl)
                    .Select(ttl => PingForTtlAsync(host, ttl, buffer, token, updateUiCallback));
                await Task.WhenAll(pingTasks);

                await logger.LogAsync(LogLevel.INFO, $"Цикл пингов завершен для хоста: {host}");

                await Task.Delay(delay, token);
            }

            await logger.LogAsync(LogLevel.INFO, $"Завершение трассировки для хоста: {host}");
        }

        private (int, int) AdjustTtlAndDelay()
        {
            // Анализ потерь пакетов
            int totalSent = hopData.Values.Sum(h => h.Sent);
            int totalReceived = hopData.Values.Sum(h => h.Received);
            double lossPercentage = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;

            // Оставим TTL неизменным для одного цикла трассировки
            int currentMaxTtl = MaxTtl;

            // Адаптивная задержка
            int delay = adaptiveDelay;
            if (lossPercentage > 50)
                delay = Math.Min(Timeout, (int)(adaptiveDelay * 1.5));  // Ограничим максимальной задержкой
            else if (lossPercentage < 10)
                delay = Math.Max(100, (int)(adaptiveDelay * 0.75));  // Не уменьшаем слишком сильно

            return (currentMaxTtl, delay);
        }

        private async Task PingForTtlAsync(string host, int ttl, byte[] buffer, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            var pingTasks = Enumerable.Range(0, ParallelRequests)
                .Select(_ => PingAsync(host, ttl, buffer, token, updateUiCallback));
            await Task.WhenAll(pingTasks);
        }

        private async Task PingAsync(string host, int ttl, byte[] buffer, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            using (var pingSender = new Ping())
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await logger.LogAsync(LogLevel.INFO, $"Отправка пинга на {host} (TTL: {ttl})");

                    // Отправка пинга
                    var reply = await pingSender.SendPingAsync(host, Timeout, buffer, new PingOptions { Ttl = ttl });
                    stopwatch.Stop();

                    token.ThrowIfCancellationRequested();

                    // Получаем IP-адрес и обрабатываем его
                    string ipAddress = reply?.Address?.ToString() ?? "Неизвестный адрес";
                    if (reply?.Status == IPStatus.Success)
                    {
                        // Дополнительная проверка для IPv6
                        if (reply.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        {
                            await logger.LogAsync(LogLevel.INFO, $"Обработка IPv6 адреса: {ipAddress}");
                        }
                        else
                        {
                            await logger.LogAsync(LogLevel.INFO, $"Обработка IPv4 адреса: {ipAddress}");
                        }
                    }

                    await ProcessTraceLineAsync(ipAddress, ttl, reply, stopwatch.ElapsedMilliseconds, updateUiCallback, token);
                }
                catch (OperationCanceledException)
                {
                    // Игнорируем исключение отмены
                }
                catch (PingException ex)
                {
                    await logger.LogAsync(LogLevel.ERROR, $"Ошибка при пинге {host} с TTL {ttl}: {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    await logger.LogAsync(LogLevel.ERROR, $"Непредвиденная ошибка при пинге {host} с TTL {ttl}: {ex.Message}", ex);
                }
            }
        }


        private async Task ProcessTraceLineAsync(string ipAddress, int ttl, PingReply reply, long responseTime, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ipAddress) || ipAddress.Trim() == "0.0.0.0")
                return;

            await logger.LogAsync(LogLevel.INFO, $"Обработка IP-адреса: {ipAddress} (TTL: {ttl}).");

            string domainName = await dnsManager.GetDomainNameAsync(ipAddress, token);

            if (!hopData.TryGetValue(ipAddress, out var hop))
            {
                hop = new HopData();
                hopData[ipAddress] = hop;
            }

            hop.Sent++;

            if (reply?.Status == IPStatus.Success || reply?.Status == IPStatus.TtlExpired || reply?.Status == IPStatus.TimeExceeded)
            {
                hop.Received++;
                hop.AddResponseTime(responseTime);
            }
            else
            {
                await logger.LogAsync(LogLevel.WARNING, $"Пакет не был получен от IP: {ipAddress} (TTL: {ttl})");
            }

            updateUiCallback(ipAddress, ttl, domainName, hop);
        }

        public void ClearHopData()
        {
            hopData.Clear();
        }
    }
}