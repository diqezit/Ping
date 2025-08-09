// ValidationHelper.cs

#nullable enable

namespace PingTestTool;

public static class ValidationHelper
{
    private static readonly Regex CyrillicRegex = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);
    private const int MIN_TIMEOUT = 100, MIN_PING_COUNT = 1, MAX_PING_COUNT = 1000;

    public static List<string> ValidateUrl(string url) =>
        string.IsNullOrWhiteSpace(url) ? new List<string> { ResourceHelper.FindResourceString("UrlEmptyError") } :
        CyrillicRegex.IsMatch(url) ? new List<string> { ResourceHelper.FindResourceString("UrlCyrillicError") } : new List<string>();

    public static List<string> ValidatePingCount(string pingCount) =>
        !int.TryParse(pingCount, out int count) || count < MIN_PING_COUNT || count > MAX_PING_COUNT
            ? new List<string> { string.Format(ResourceHelper.FindResourceString("PingCountRangeError"), MIN_PING_COUNT, MAX_PING_COUNT) }
            : new List<string>();

    public static List<string> ValidateTimeout(string timeout) =>
        !int.TryParse(timeout, out int time) || time < MIN_TIMEOUT
            ? new List<string> { string.Format(ResourceHelper.FindResourceString("TimeoutMinimumError"), MIN_TIMEOUT) }
            : new List<string>();
}

