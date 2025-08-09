#nullable enable

namespace PingTestTool;

public class PingService : IPingTestService
{
    private readonly List<(DateTime Time, int RoundtripTime)> _pingData = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IPingExecutor _executor;
    private readonly IStatisticsCalculator _statsCalc;
    private readonly IReportGenerator _reportGen;
    private bool _disposed;

    public event Action<string>? OnPingResult;
    public event Action<int, int>? OnProgressUpdate;
    public event Action<int>? OnRoundtripTimeAdded;

    public PingService(IPingExecutor? executor = null, IStatisticsCalculator? statsCalc = null, IReportGenerator? reportGen = null)
    {
        _executor = executor ?? new PingExecutor();
        _statsCalc = statsCalc ?? new StatisticsCalculator();
        _reportGen = reportGen ?? new ReportGenerator();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PingService));
    }

    public async Task<IPingTestResult> StartPingTestAsync(IPingConfiguration config, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var startTime = DateTime.Now;
        var headerBuilder = new StringBuilder();

        _reportGen.InitializeLogBuilder(headerBuilder, config, startTime);
        OnPingResult?.Invoke(headerBuilder.ToString());

        var (success, fail, responseTimes) = await ExecutePingTestsAsync(config, cancellationToken).ConfigureAwait(false);

        var endTime = DateTime.Now;
        var execTime = endTime - startTime;
        var roundtripTimes = await GetRoundtripTimesAsync(cancellationToken).ConfigureAwait(false);
        var rtTimes = roundtripTimes.Select(x => x.RoundtripTime).ToList();

        var avgJitter = await _statsCalc.CalculateAverageJitterAsync(rtTimes).ConfigureAwait(false);

        var finalLog = await _reportGen.GenerateFinalReport(
            new StringBuilder(), responseTimes, startTime, endTime,
            execTime, avgJitter, success, fail, config.PingCount, rtTimes).ConfigureAwait(false);

        OnPingResult?.Invoke(Environment.NewLine + finalLog);

        var detailed = new StringBuilder(headerBuilder.ToString())
            .AppendLine()
            .Append(responseTimes)
            .AppendLine()
            .Append(finalLog)
            .ToString();

        return new PingTestResult(
            success, fail, execTime, avgJitter, rtTimes, detailed);
    }

    private async Task<(int success, int fail, StringBuilder responseTimes)> ExecutePingTestsAsync(
        IPingConfiguration config, CancellationToken cancellationToken)
    {
        int success = 0, fail = 0;
        var responseTimes = new StringBuilder();
        var options = new PingOptions { DontFragment = config.DontFragment };
        var buffer = new byte[PingServiceConstants.BUFFER_SIZE];

        for (var i = 0; i < config.PingCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _executor.ExecuteSinglePingAsync(
                config.Url, config.Timeout, options, buffer,
                i + 1, config.PingCount, cancellationToken).ConfigureAwait(false);

            responseTimes.AppendLine(result.Message);
            OnPingResult?.Invoke(result.Message + Environment.NewLine);

            if (result.IsSuccess)
            {
                await AddRoundtripTimeAsync((DateTime.Now, result.RoundtripTime), cancellationToken).ConfigureAwait(false);
                success++;
            }
            else
            {
                fail++;
            }

            OnProgressUpdate?.Invoke(i + 1, config.PingCount);

            var delay = Math.Max(0, config.Timeout - (int)result.ElapsedMilliseconds);
            if (delay > 0)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return (success, fail, responseTimes);
    }

    private async Task AddRoundtripTimeAsync((DateTime Time, int RoundtripTime) pingData, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pingData.Add(pingData);
            OnRoundtripTimeAdded?.Invoke(pingData.RoundtripTime);
        }
        finally { _lock.Release(); }
    }

    public async Task ClearRoundtripTimesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _pingData.Clear(); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<(DateTime Time, int RoundtripTime)>> GetRoundtripTimesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _pingData.ToList().AsReadOnly(); }
        finally { _lock.Release(); }
    }
}