#nullable enable

namespace PingTestTool;

public class DnsManager : ValidationBase, IDnsManager
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _dnsTimeout;
    private readonly MemoryCacheEntryOptions _cacheOptions;

    public DnsManager(IMemoryCache memoryCache, TimeSpan? dnsTimeout = null)
    {
        ValidateNotNull(memoryCache, nameof(memoryCache));
        _cache = memoryCache;
        _dnsTimeout = dnsTimeout ?? TimeSpan.FromSeconds(5);
        _cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
            .SetSlidingExpiration(TimeSpan.FromMinutes(10));
    }

    public DnsManager(IMemoryCache memoryCache, NetworkSettings settings)
    {
        ValidateNotNull(memoryCache, nameof(memoryCache));
        ValidateNotNull(settings, nameof(settings));
        _cache = memoryCache;
        _dnsTimeout = settings.DnsTimeout;
        _cacheOptions = settings.CacheOptions;
    }

    public async Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token)
    {
        ValidateNotNullOrEmpty(ipAddress, nameof(ipAddress));

        if (!IPAddress.TryParse(ipAddress, out IPAddress parsed))
            throw new ArgumentException("Incorrect IP address", nameof(ipAddress));

        if (_cache.TryGetValue(ipAddress, out string? cached))
            return cached ?? Constants.DefaultUnresolvedValue;

        return await ResolveAsync(ipAddress, parsed, token).ConfigureAwait(false);
    }

    private async Task<string> ResolveAsync(string ipAddress, IPAddress parsed, CancellationToken token)
    {
        try
        {
            var dnsTask = Dns.GetHostEntryAsync(parsed);
            var timeoutTask = Task.Delay(_dnsTimeout, token);
            var completed = await Task.WhenAny(dnsTask, timeoutTask).ConfigureAwait(false);

            string result;
            if (completed == dnsTask && dnsTask.Status == TaskStatus.RanToCompletion)
                result = dnsTask.Result.HostName;
            else
                result = Constants.DefaultUnresolvedValue;

            _cache.Set(ipAddress, result, _cacheOptions);
            return result;
        }
        catch
        {
            _cache.Set(ipAddress, Constants.DefaultUnresolvedValue, _cacheOptions);
            return Constants.DefaultUnresolvedValue;
        }
    }
}