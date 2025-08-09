// PingExecutor.cs

#nullable enable

namespace PingTestTool;

public class PingExecutor : IPingExecutor
{
    public async Task<PingExecutionResult> ExecuteSinglePingAsync(
        string url, int timeout, PingOptions options, byte[] buffer,
        int currentPing, int totalPings, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(url, timeout, buffer, options).ConfigureAwait(false);
            stopwatch.Stop();

            return reply.Status == IPStatus.Success
                ? CreateSuccessResult(url, reply, currentPing, totalPings, stopwatch.ElapsedMilliseconds)
                : CreateFailureResult(url, reply, currentPing, totalPings, stopwatch.ElapsedMilliseconds);
        }
        catch (PingException ex)
        {
            stopwatch.Stop();
            return CreateExceptionResult(url, ex, currentPing, totalPings, stopwatch.ElapsedMilliseconds);
        }
    }

    private PingExecutionResult CreateSuccessResult(string url, PingReply reply, int currentPing, int totalPings, long elapsed) =>
        new(true, (int)reply.RoundtripTime,
            $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {ResourceHelper.FindResourceString("ReplyFrom")} {url}:\n" +
            $"  {ResourceHelper.FindResourceString("Time")}: {reply.RoundtripTime,4} {ResourceHelper.FindResourceString("Ms")}\n" +
            $"  TTL:  {reply.Options?.Ttl ?? 0}\n" +
            $"  {ResourceHelper.FindResourceString("Size")}:{reply.Buffer?.Length ?? 0} {ResourceHelper.FindResourceString("Bytes")}",
            elapsed);

    private PingExecutionResult CreateFailureResult(string url, PingReply reply, int currentPing, int totalPings, long elapsed) =>
        new(false, 0,
            $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {ResourceHelper.FindResourceString("PingError")} {url}:\n" +
            $"  {ResourceHelper.FindResourceString("Status")}: {reply.Status}",
            elapsed);

    private PingExecutionResult CreateExceptionResult(string url, PingException ex, int currentPing, int totalPings, long elapsed) =>
        new(false, 0,
            $"[{DateTime.Now:HH:mm:ss}] [{currentPing}/{totalPings}] {ResourceHelper.FindResourceString("CriticalPingError")} {url}:\n" +
            $"  {ex.Message}",
            elapsed);
}

