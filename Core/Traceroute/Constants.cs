#nullable enable

namespace PingTestTool;

public static class Constants
{
    public const string DefaultUnresolvedValue = "---",
                        MsUnitSuffix = " ms",
                        PercentageSuffix = "%",
                        DefaultFormat = "F0";

    public static class Ping
    {
        public const int BufferSize = 32,
                         MaxTtl = 12,
                         Timeout = 5000,
                         ParallelRequests = 1,
                         BaseDelay = 1000,
                         MinDelay = 100;
        public const double HighLossThreshold = 50,
                            LowLossThreshold = 10;
    }
}