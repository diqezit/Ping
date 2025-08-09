// PingConfiguration.cs

#nullable enable

namespace PingTestTool;

public record PingConfiguration : IPingConfiguration
{
    public string Url { get; }
    public int PingCount { get; }
    public int Timeout { get; }
    public bool DontFragment { get; }

    public PingConfiguration(string url, int pingCount, int timeout, bool dontFragment = true)
    {
        var errs = ValidateParameters(url, pingCount, timeout);
        if (errs.Any())
            throw new ArgumentException("Invalid configuration parameters: " + string.Join(", ", errs));

        Url = url;
        PingCount = pingCount;
        Timeout = timeout;
        DontFragment = dontFragment;
    }

    public void Validate() { }

    private static IEnumerable<string> ValidateParameters(string url, int pingCount, int timeout) =>
        ValidationHelper.ValidateUrl(url)
            .Concat(ValidationHelper.ValidatePingCount(pingCount.ToString()))
            .Concat(ValidationHelper.ValidateTimeout(timeout.ToString()));
}

public record PingTestResult(
int SuccessfulPings,
int FailedPings,
TimeSpan ExecutionTime,
double AverageJitter,
IReadOnlyList<int> RoundtripTimes,
string DetailedLog) : IPingTestResult;

public readonly record struct PingExecutionResult(
    bool IsSuccess,
    int RoundtripTime,
    string Message,
    long ElapsedMilliseconds);