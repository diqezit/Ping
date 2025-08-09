#nullable enable

namespace PingTestTool;

public record NetworkSettings(TimeSpan DnsTimeout, MemoryCacheEntryOptions CacheOptions);