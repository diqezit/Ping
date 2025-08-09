// IPingTestService.cs

#nullable enable

namespace PingTestTool;

public interface IPingTestService : IDisposable
{
    event Action<string>? OnPingResult;
    event Action<int, int>? OnProgressUpdate;
    event Action<int>? OnRoundtripTimeAdded;
    Task<IPingTestResult> StartPingTestAsync(IPingConfiguration config, CancellationToken cancellationToken = default);
    Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(DateTime Time, int RoundtripTime)>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default);
}

