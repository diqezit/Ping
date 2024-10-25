#nullable enable
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    /// <summary>
    /// Класс для управления DNS-запросами и кэширования результатов.
    /// </summary>
    public class DnsManager : IDnsManager
    {
        #region Константы

        private const string DefaultUnresolvedValue = "---";
        private const int DefaultDnsTimeoutSeconds = 5;
        private const int DefaultCacheDurationMinutes = 30;

        #endregion

        #region Приватные поля

        private readonly IMemoryCache _dnsCache;
        private readonly ILogger _logger;
        private readonly TimeSpan _dnsTimeout;
        private readonly TimeSpan _cacheDuration;

        private static readonly IPAddress PrivateNetworkA = IPAddress.Parse("10.0.0.0");
        private static readonly IPAddress PrivateNetworkB = IPAddress.Parse("172.16.0.0");
        private static readonly IPAddress PrivateNetworkC = IPAddress.Parse("192.168.0.0");

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса DnsManager.
        /// </summary>
        /// <param name="memoryCache">Кэш для хранения результатов DNS-запросов.</param>
        /// <param name="logger">Логгер для записи событий.</param>
        /// <param name="dnsTimeout">Тайм-аут для DNS-запросов.</param>
        /// <param name="cacheDuration">Длительность кэширования результатов.</param>
        public DnsManager(IMemoryCache memoryCache, ILogger logger, TimeSpan? dnsTimeout = null, TimeSpan? cacheDuration = null)
        {
            _dnsCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(DefaultDnsTimeoutSeconds);
            _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(DefaultCacheDurationMinutes);
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Получает доменное имя для заданного IP-адреса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для разрешения.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <returns>Доменное имя или значение по умолчанию, если разрешение не удалось.</returns>
        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            ValidateIpAddress(ipAddress, out IPAddress parsedIp);
            token.ThrowIfCancellationRequested();

            if (await GetFromCacheAsync(ipAddress) is { } cachedResult)
            {
                return cachedResult;
            }

            var result = await ResolveDomainNameAsync(ipAddress, parsedIp, token);
            await CacheResultAsync(ipAddress, result);

            return result;
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Проверяет и парсит IP-адрес.
        /// </summary>
        /// <param name="ipAddress">Строка с IP-адресом.</param>
        /// <param name="parsedIp">Выходной параметр для распарсенного IP-адреса.</param>
        private static void ValidateIpAddress(string ipAddress, out IPAddress parsedIp)
        {
            parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
                ? ip
                : throw new ArgumentException("Недопустимый формат IP-адреса.", nameof(ipAddress));
        }

        /// <summary>
        /// Получает результат из кэша.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для поиска в кэше.</param>
        /// <returns>Кэшированное доменное имя или null, если результат не найден.</returns>
        private async Task<string?> GetFromCacheAsync(string ipAddress)
        {
            if (_dnsCache.TryGetValue(ipAddress, out var cachedDomain))
            {
                var domain = cachedDomain as string;
                await _logger.LogAsync(LogLevel.DEBUG, $"Используем кэш для {ipAddress}: {domain ?? DefaultUnresolvedValue}")
                    .ConfigureAwait(false);
                return domain;
            }
            return null;
        }

        /// <summary>
        /// Разрешает доменное имя для заданного IP-адреса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для разрешения.</param>
        /// <param name="parsedIp">Распарсенный IP-адрес.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <returns>Доменное имя или значение по умолчанию, если разрешение не удалось.</returns>
        private async Task<string> ResolveDomainNameAsync(string ipAddress, IPAddress parsedIp, CancellationToken token) =>
            IsLocalIpAddress(parsedIp)
                ? await ResolveLocalIpAddressAsync(ipAddress, parsedIp).ConfigureAwait(false)
                : await GetDomainNameFromDnsAsync(ipAddress, token).ConfigureAwait(false);

        private async Task<string> ResolveLocalIpAddressAsync(string ipAddress, IPAddress parsedIp)
        {
            var domainName = GetLocalName(parsedIp);
            await _logger.LogAsync(LogLevel.INFO, $"Локальный IP-адрес для {ipAddress}: {domainName}")
                .ConfigureAwait(false);
            return domainName;
        }

        /// <summary>
        /// Кэширует результат DNS-запроса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес.</param>
        /// <param name="result">Результат DNS-запроса.</param>
        private Task CacheResultAsync(string ipAddress, string result) =>
            Task.Run(() => _dnsCache.Set(ipAddress, result, new MemoryCacheEntryOptions().SetSlidingExpiration(_cacheDuration)));

        /// <summary>
        /// Выполняет DNS-запрос с учетом тайм-аута.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для разрешения.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <returns>Доменное имя или значение по умолчанию, если разрешение не удалось.</returns>
        private async Task<string> GetDomainNameFromDnsAsync(string ipAddress, CancellationToken token)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            linkedCts.CancelAfter(_dnsTimeout);
            return await ExecuteDnsLookupAsync(ipAddress, linkedCts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Выполняет DNS-запрос и обрабатывает возможные ошибки.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для разрешения.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <returns>Доменное имя или значение по умолчанию, если разрешение не удалось.</returns>
        private async Task<string> ExecuteDnsLookupAsync(string ipAddress, CancellationToken token)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                await _logger.LogAsync(LogLevel.INFO, $"DNS-поиск для {ipAddress} возвращено {hostEntry.HostName}")
                    .ConfigureAwait(false);
                return hostEntry.HostName;
            }
            catch (Exception ex)
            {
                await LogDnsError(ipAddress, ex).ConfigureAwait(false);
                return DefaultUnresolvedValue;
            }
        }

        /// <summary>
        /// Логирует ошибки DNS-запроса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес, для которого выполнялся запрос.</param>
        /// <param name="ex">Исключение, возникшее при выполнении запроса.</param>
        private async Task LogDnsError(string ipAddress, Exception ex)
        {
            var logMessage = ex switch
            {
                OperationCanceledException => $"DNS-запрос для {ipAddress} был отменён по истечению времени ({_dnsTimeout.TotalSeconds} секунд).",
                SocketException or ArgumentException => $"Ошибка при разрешении {ipAddress}: {ex.GetType().Name} - {ex.Message}",
                _ => $"Неизвестная ошибка при разрешении {ipAddress}: {ex.Message}"
            };

            await _logger.LogAsync(LogLevel.WARNING, logMessage).ConfigureAwait(false);
        }

        /// <summary>
        /// Проверяет, является ли IP-адрес локальным.
        /// </summary>
        /// <param name="ip">IP-адрес для проверки.</param>
        /// <returns>True, если IP-адрес локальный, иначе False.</returns>
        private static bool IsLocalIpAddress(IPAddress? ip) => ip?.IsPrivate() ?? false;

        /// <summary>
        /// Возвращает описание для локального IP-адреса.
        /// </summary>
        /// <param name="ip">IP-адрес для определения типа.</param>
        /// <returns>Описание типа локального IP-адреса.</returns>
        private static string GetLocalName(IPAddress ip) => ip.AddressFamily switch
        {
            AddressFamily.InterNetworkV6 => GetIpv6LocalName(ip),
            AddressFamily.InterNetwork => GetIpv4LocalName(ip),
            _ => "Неизвестный локальный IP-адрес"
        };

        /// <summary>
        /// Возвращает описание для локального IPv6-адреса.
        /// </summary>
        /// <param name="ip">IPv6-адрес для определения типа.</param>
        /// <returns>Описание типа локального IPv6-адреса.</returns>
        private static string GetIpv6LocalName(IPAddress ip) => ip switch
        {
            { IsIPv6LinkLocal: true } => "Локальная сеть (IPv6 Link-Local)",
            { IsIPv6SiteLocal: true } => "Локальная сеть (IPv6 Site-Local)",
            { IsIPv6Multicast: true } => "Мультикаст (IPv6)",
            _ => "Неизвестный локальный IPv6-адрес"
        };

        /// <summary>
        /// Возвращает описание для локального IPv4-адреса.
        /// </summary>
        /// <param name="ip">IPv4-адрес для определения типа.</param>
        /// <returns>Описание типа локального IPv4-адреса.</returns>
        private static string GetIpv4LocalName(IPAddress ip) => ip switch
        {
            var addr when addr.IsInSubnet(PrivateNetworkC, 16) => "Локальная сеть (Router)",
            var addr when addr.IsInSubnet(PrivateNetworkA, 8) => "DNS сервер провайдеров",
            var addr when addr.IsInSubnet(PrivateNetworkB, 12) => "Локальная сеть",
            _ => "Неизвестный локальный IPv4-адрес"
        };

        #endregion

    }

    #region Интерфейс 

    /// <summary>
    /// Интерфейс для управления DNS-запросами.
    /// </summary>
    public interface IDnsManager
    {
        /// <summary>
        /// Получает доменное имя для заданного IP-адреса.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для разрешения.</param>
        /// <param name="token">Токен отмены операции.</param>
        /// <returns>Доменное имя или значение по умолчанию, если разрешение не удалось.</returns>
        Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token);
    }
    #endregion

    /// <summary>
    /// Расширения для класса IPAddress.
    /// </summary>
    public static class IPAddressExtensions
    {
        #region Константы

        private const byte PrivateNetworkAFirstByte = 10;
        private const byte PrivateNetworkBFirstByte = 172;
        private const byte PrivateNetworkBSecondByteStart = 16;
        private const byte PrivateNetworkBSecondByteEnd = 31;
        private const byte PrivateNetworkCFirstByte = 192;
        private const byte PrivateNetworkCSecondByte = 168;

        #endregion

        #region Публичные методы

        /// <summary>
        /// Проверяет, является ли IP-адрес частным.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для проверки.</param>
        /// <returns>True, если IP-адрес частный, иначе False.</returns>
        public static bool IsPrivate(this IPAddress ipAddress)
        {
            if (ipAddress == null)
                return false;

            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                return IsPrivateIPv4(ipAddress.GetAddressBytes());
            }

            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return IsPrivateIPv6(ipAddress.GetAddressBytes());
            }

            return false;
        }

        /// <summary>
        /// Проверяет, находится ли IP-адрес в заданной подсети.
        /// </summary>
        /// <param name="ipAddress">IP-адрес для проверки.</param>
        /// <param name="subnetMask">Маска подсети.</param>
        /// <param name="prefixLength">Длина префикса подсети.</param>
        /// <returns>True, если IP-адрес находится в подсети, иначе False.</returns>
        public static bool IsInSubnet(this IPAddress ipAddress, IPAddress subnetMask, int prefixLength)
        {
            if (ipAddress == null || subnetMask == null ||
                ipAddress.AddressFamily != subnetMask.AddressFamily)
                return false;

            var ipBytes = ipAddress.GetAddressBytes();
            var subnetBytes = subnetMask.GetAddressBytes();

            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != subnetBytes[i])
                    return false;
            }

            if (remainingBits > 0)
            {
                int mask = 0xFF << (8 - remainingBits);
                return (ipBytes[fullBytes] & mask) == (subnetBytes[fullBytes] & mask);
            }

            return true;
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Проверяет, является ли IPv4-адрес частным.
        /// </summary>
        /// <param name="bytes">Байты IPv4-адреса.</param>
        /// <returns>True, если IPv4-адрес частный, иначе False.</returns>
        private static bool IsPrivateIPv4(byte[] bytes)
        {
            return bytes[0] == PrivateNetworkAFirstByte ||
                   (bytes[0] == PrivateNetworkBFirstByte &&
                    bytes[1] >= PrivateNetworkBSecondByteStart &&
                    bytes[1] <= PrivateNetworkBSecondByteEnd) ||
                   (bytes[0] == PrivateNetworkCFirstByte &&
                    bytes[1] == PrivateNetworkCSecondByte);
        }

        /// <summary>
        /// Проверяет, является ли IPv6-адрес частным.
        /// </summary>
        /// <param name="bytes">Байты IPv6-адреса.</param>
        /// <returns>True, если IPv6-адрес частный, иначе False.</returns>
        private static bool IsPrivateIPv6(byte[] bytes)
        {
            return (bytes[0] == 0xfc || bytes[0] == 0xfd);
        }

        #endregion
    }
}