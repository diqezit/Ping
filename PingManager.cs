using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    /// <summary>
    /// Класс для управления пингами и трассировкой маршрута.
    /// </summary>
    public class PingManager
    {
        #region Константы

        private const int BufferSize = 32;
        private const int MaxTtl = 12;
        private const int Timeout = 5000;
        private const int ParallelRequests = 1;
        private const int AdaptiveDelay = 1000;

        #endregion

        #region Приватные поля

        private readonly ILogger logger;
        private readonly IDnsManager dnsManager;
        private readonly ConcurrentDictionary<string, HopData> hopData = new ConcurrentDictionary<string, HopData>();

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса PingManager.
        /// </summary>
        /// <param name="logger">Логгер для записи событий.</param>
        /// <param name="dnsManager">Менеджер DNS для разрешения доменных имен.</param>
        public PingManager(ILogger logger, IDnsManager dnsManager)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.dnsManager = dnsManager ?? throw new ArgumentNullException(nameof(dnsManager));
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Начинает трассировку маршрута для заданного хоста.
        /// </summary>
        /// <param name="host">Хост для трассировки.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <param name="updateUiCallback">Callback для обновления UI.</param>
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

        /// <summary>
        /// Очищает данные о хопах.
        /// </summary>
        public void ClearHopData() => hopData.Clear();

        #endregion

        #region Приватные методы

        /// <summary>
        /// Настраивает TTL и задержку для следующего цикла пингов.
        /// </summary>
        /// <returns>Кортеж с текущим максимальным TTL и задержкой.</returns>
        private (int, int) AdjustTtlAndDelay()
        {
            int totalSent = hopData.Values.Sum(h => h.Sent);
            int totalReceived = hopData.Values.Sum(h => h.Received);
            double lossPercentage = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;

            int currentMaxTtl = MaxTtl;
            int delay = AdaptiveDelay;

            if (lossPercentage > 50)
                delay = Math.Min(Timeout, (int)(AdaptiveDelay * 1.5));
            else if (lossPercentage < 10)
                delay = Math.Max(100, (int)(AdaptiveDelay * 0.75));

            return (currentMaxTtl, delay);
        }

        /// <summary>
        /// Выполняет пинги для заданного TTL.
        /// </summary>
        /// <param name="host">Хост для пингов.</param>
        /// <param name="ttl">Значение TTL.</param>
        /// <param name="buffer">Буфер для пинга.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <param name="updateUiCallback">Callback для обновления UI.</param>
        private async Task PingForTtlAsync(string host, int ttl, byte[] buffer, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            var pingTasks = Enumerable.Range(0, ParallelRequests)
                .Select(_ => PingAsync(host, ttl, buffer, token, updateUiCallback));
            await Task.WhenAll(pingTasks);
        }

        /// <summary>
        /// Выполняет одиночный пинг.
        /// </summary>
        /// <param name="host">Хост для пингов.</param>
        /// <param name="ttl">Значение TTL.</param>
        /// <param name="buffer">Буфер для пинга.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <param name="updateUiCallback">Callback для обновления UI.</param>
        private async Task PingAsync(string host, int ttl, byte[] buffer, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            using var pingSender = new Ping();
            try
            {
                token.ThrowIfCancellationRequested();

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await logger.LogAsync(LogLevel.INFO, $"Отправка пинга на {host} (TTL: {ttl})");

                var reply = await pingSender.SendPingAsync(host, Timeout, buffer, new PingOptions { Ttl = ttl });
                stopwatch.Stop();

                token.ThrowIfCancellationRequested();

                string ipAddress = reply?.Address?.ToString() ?? "Неизвестный адрес";
                if (reply?.Status == IPStatus.Success)
                {
                    if (reply.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        await logger.LogAsync(LogLevel.INFO, $"Обработка IPv6 адреса: {ipAddress}");
                    else
                        await logger.LogAsync(LogLevel.INFO, $"Обработка IPv4 адреса: {ipAddress}");
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

        /// <summary>
        /// Обрабатывает результат пинга и обновляет данные о хопе.
        /// </summary>
        /// <param name="ipAddress">IP-адрес.</param>
        /// <param name="ttl">Значение TTL.</param>
        /// <param name="reply">Ответ на пинг.</param>
        /// <param name="responseTime">Время отклика.</param>
        /// <param name="updateUiCallback">Callback для обновления UI.</param>
        /// <param name="token">Токен отмены операции.</param>
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

            if (reply?.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded)
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

        #endregion
    }
}