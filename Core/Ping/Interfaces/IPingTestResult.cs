// IPingTestResult.cs

#nullable enable

namespace PingTestTool;

public interface IPingTestResult
{
    int SuccessfulPings { get; }
    int FailedPings { get; }
    TimeSpan ExecutionTime { get; }
    double AverageJitter { get; }
    IReadOnlyList<int> RoundtripTimes { get; }
    string DetailedLog { get; }
}
