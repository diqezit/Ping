#nullable enable

namespace PingTestTool;

public interface IDnsManager
{
    Task<string> GetDomainNameAsync(string ipAddress, CancellationToken token);
}

