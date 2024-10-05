using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    public class DnsManager
    {
        private readonly IMemoryCache dnsCache;
        private readonly Logger logger;
        private readonly TimeSpan dnsTimeout;
        private readonly TimeSpan cacheDuration;

        public DnsManager(IMemoryCache memoryCache, Logger logger, TimeSpan? dnsTimeout = null, TimeSpan? cacheDuration = null)
        {
            this.dnsCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
            this.cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(30);
        }

        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("IP-адрес не может быть пустым или null.", nameof(ipAddress));
            }

            if (!IPAddress.TryParse(ipAddress, out var parsedIp))
            {
                throw new ArgumentException("Недопустимый формат IP-адреса.", nameof(ipAddress));
            }

            token.ThrowIfCancellationRequested();

            if (dnsCache.TryGetValue(ipAddress, out string cachedDomain))
            {
                await logger.LogAsync(LogLevel.DEBUG, $"Используем кэш для {ipAddress}: {cachedDomain ?? "---"}").ConfigureAwait(false);
                return cachedDomain ?? "---";
            }

            string domainName;
            if (IsLocalIpAddress(parsedIp))
            {
                domainName = GetLocalName(ipAddress);
                await logger.LogAsync(LogLevel.INFO, $"Локальный IP-адрес для {ipAddress}: {domainName}").ConfigureAwait(false);
            }
            else
            {
                domainName = await GetDomainNameFromDnsAsync(ipAddress, token);
            }

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(cacheDuration);

            dnsCache.Set(ipAddress, domainName, cacheEntryOptions);
            return domainName;
        }

        private async Task<string> GetDomainNameFromDnsAsync(string ipAddress, CancellationToken token)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                linkedCts.CancelAfter(dnsTimeout);
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                    await logger.LogAsync(LogLevel.INFO, $"DNS-поиск для {ipAddress} возвращено {hostEntry.HostName}").ConfigureAwait(false);
                    return hostEntry.HostName;
                }
                catch (OperationCanceledException)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"DNS-запрос для {ipAddress} был отменён по истечению времени ({dnsTimeout.TotalSeconds} секунд).").ConfigureAwait(false);
                    return "---";
                }
                catch (SocketException ex)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Ошибка при разрешении {ipAddress}: {ex.GetType().Name} - {ex.Message}").ConfigureAwait(false);
                    return "---";
                }
                catch (ArgumentException ex)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Ошибка при разрешении {ipAddress}: {ex.GetType().Name} - {ex.Message}").ConfigureAwait(false);
                    return "---";
                }
                catch (Exception ex)
                {
                    await logger.LogAsync(LogLevel.WARNING, $"Неизвестная ошибка при разрешении {ipAddress}: {ex.Message}").ConfigureAwait(false);
                    return "---";
                }
            }
        }

        private static bool IsLocalIpAddress(IPAddress ip)
        {
            return ip != null && (ip.IsPrivate() || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal);
        }

        private static string GetLocalName(string ipAddress)
        {
            if (IPAddress.TryParse(ipAddress, out IPAddress ip))
            {
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (ip.IsIPv6LinkLocal)
                        return "Локальная сеть (IPv6 Link-Local)";
                    if (ip.IsIPv6SiteLocal)
                        return "Локальная сеть (IPv6 Site-Local)";
                    if (ip.IsIPv6Multicast)
                        return "Мультикаст (IPv6)";
                }
                else if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (ip.IsInSubnet(IPAddress.Parse("192.168.0.0"), 16))
                        return "Локальная сеть (Router)";
                    if (ip.IsInSubnet(IPAddress.Parse("10.0.0.0"), 8))
                        return "DNS сервер провайдеров";
                    if (ip.IsInSubnet(IPAddress.Parse("172.16.0.0"), 12))
                        return "Локальная сеть";
                }
            }
            return "Неизвестный локальный IP-адрес";
        }
    }


    public static class IPAddressExtensions
    {
        public static bool IsPrivate(this IPAddress ipAddress)
        {
            if (ipAddress == null)
                return false;

            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ipAddress.GetAddressBytes();
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168);
            }
            else if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = ipAddress.GetAddressBytes();
                // Проверяем для частных адресов IPv6 (fc00::/7)
                return (bytes[0] == 0xfc || bytes[0] == 0xfd);
            }

            return false;
        }

        public static bool IsInSubnet(this IPAddress ipAddress, IPAddress subnetMask, int prefixLength)
        {
            if (ipAddress == null || subnetMask == null || ipAddress.AddressFamily != subnetMask.AddressFamily)
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
    }
}