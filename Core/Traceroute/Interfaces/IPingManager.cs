#nullable enable

namespace PingTestTool;

public interface IPingManager
{
    Task StartTraceAsync(
        string host,
        CancellationToken token,
        Action<string, int, string, HopData> updateUiCallback);
    void ClearHopData();
}

