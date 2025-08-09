// IPingExecutor.cs

#nullable enable

namespace PingTestTool;

public interface IPingExecutor
{
    Task<PingExecutionResult> ExecuteSinglePingAsync(
        string url,
        int timeout,
        PingOptions options,
        byte[] buffer,
        int currentPing,
        int totalPings,
        CancellationToken cancellationToken);
}
