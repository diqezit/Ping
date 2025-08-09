// IReportGenerator.cs

#nullable enable

namespace PingTestTool;

public interface IReportGenerator
{
    void InitializeLogBuilder(
        StringBuilder logBuilder,
        IPingConfiguration config,
        DateTime startTime);

    Task<string> GenerateFinalReport(
        StringBuilder logBuilder,
        StringBuilder responseTimes,
        DateTime startTime,
        DateTime endTime,
        TimeSpan executionTime,
        double avgJitter,
        int successfulPings,
        int failedPings,
        int totalPings,
        IReadOnlyList<int> roundtripTimes);
}

