namespace PingTestTool
{
    public class DnsManager : IDnsManager
    {
        private const string DefaultUnresolvedValue = "---";
        private readonly IMemoryCache _dnsCache;
        private readonly TimeSpan _dnsTimeout;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public DnsManager(IMemoryCache memoryCache, TimeSpan? dnsTimeout = null)
        {
            _dnsCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                .SetSlidingExpiration(TimeSpan.FromMinutes(10));

            Log.Information("[DnsManager] DnsManager инициализирован");
        }

        public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
        {
            if (!IPAddress.TryParse(ipAddress, out var parsedIp))
            {
                Log.Error("[DnsManager] Некорректный IP-адрес: {IpAddress}", ipAddress);
                throw new ArgumentException("Некорректный IP-адрес", nameof(ipAddress));
            }

            if (_dnsCache.TryGetValue(ipAddress, out string? cachedResult))
            {
                return cachedResult;
            }

            try
            {
                if (parsedIp.IsPrivate())
                {
                    var localName = GetLocalNetworkName(parsedIp);
                    _dnsCache.Set(ipAddress, localName, _cacheOptions);
                    return localName;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(_dnsTimeout);

                var hostEntry = await Dns.GetHostEntryAsync(parsedIp);
                var domainName = hostEntry.HostName;
                _dnsCache.Set(ipAddress, domainName, _cacheOptions);

                return domainName;
            }
            catch (Exception ex)
            {
                _dnsCache.Set(ipAddress, DefaultUnresolvedValue, _cacheOptions);
                Log.Warning("[DnsManager] Возвращен неразрешенный результат для IP: {IpAddress}", ipAddress, ex);
                return DefaultUnresolvedValue;
            }
        }

        private string GetLocalNetworkName(IPAddress ip) => ip.AddressFamily switch
        {
            AddressFamily.InterNetworkV6 => GetLocalIpv6Name(ip),
            AddressFamily.InterNetwork => GetLocalIpv4Name(ip),
            _ => "Неизвестный локальный адрес"
        };

        private string GetLocalIpv6Name(IPAddress ip) => ip switch
        {
            { IsIPv6LinkLocal: true } => "IPv6 Link-Local",
            { IsIPv6SiteLocal: true } => "IPv6 Site-Local",
            { IsIPv6Multicast: true } => "IPv6 Multicast",
            _ => "Прочий IPv6 адрес"
        };

        private string GetLocalIpv4Name(IPAddress ip) => ip switch
        {
            var addr when addr.IsInSubnet(IPAddress.Parse("192.168.0.0"), 16) => "Локальная сеть (Router)",
            var addr when addr.IsInSubnet(IPAddress.Parse("10.0.0.0"), 8) => "DNS провайдера",
            var addr when addr.IsInSubnet(IPAddress.Parse("172.16.0.0"), 12) => "Локальная сеть",
            _ => "Прочий IPv4 адрес"
        };
    }

    public interface IDnsManager
    {
        Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token);
    }

    public static class DnsManagerExtensions
    {
        private const byte PrivateNetworkAFirstByte = 10;
        private const byte PrivateNetworkBFirstByte = 172;
        private const byte PrivateNetworkBSecondByteStart = 16;
        private const byte PrivateNetworkBSecondByteEnd = 31;
        private const byte PrivateNetworkCFirstByte = 192;
        private const byte PrivateNetworkCSecondByte = 168;

        public static bool IsPrivate(this IPAddress ipAddress)
        {
            if (ipAddress == null) return false;

            var bytes = ipAddress.GetAddressBytes();
            return ipAddress.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsPrivateIPv4(bytes),
                AddressFamily.InterNetworkV6 => IsPrivateIPv6(bytes),
                _ => false
            };
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

        private static bool IsPrivateIPv4(byte[] bytes) =>
            bytes[0] == PrivateNetworkAFirstByte ||
            (bytes[0] == PrivateNetworkBFirstByte && bytes[1] >= PrivateNetworkBSecondByteStart && bytes[1] <= PrivateNetworkBSecondByteEnd) ||
            (bytes[0] == PrivateNetworkCFirstByte && bytes[1] == PrivateNetworkCSecondByte);

        private static bool IsPrivateIPv6(byte[] bytes) => bytes[0] == 0xfc || bytes[0] == 0xfd;
    }
}