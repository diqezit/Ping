#nullable enable

namespace PingTestTool;

public class TraceManager : ValidationBase, IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private readonly IPingManager _pingManager;
    private readonly ObservableCollection<TraceResult> _results;
    private readonly IMemoryCache _memoryCache;
    private bool _isTracing;

    public ObservableCollection<TraceResult> TraceResults => _results;
    public string TraceUrl { get; }
    public bool IsTracing { get => _isTracing; private set => _isTracing = value; }

    public TraceManager(string url)
    {
        ValidateNotNullOrEmpty(url, nameof(url));
        TraceUrl = url;
        _results = new ObservableCollection<TraceResult>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _pingManager = new PingManager(new DnsManager(_memoryCache));
    }

    public async Task StartTraceAsync(
        Action<string, Color> updateStatus,
        Action<string, string, MessageBoxButton, MessageBoxImage> showMessage)
    {
        if (IsTracing)
        {
            showMessage(
                ResourceHelper.FindResourceString("TraceAlreadyRunning"),
                ResourceHelper.FindResourceString("WarningCaption"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        IsTracing = true;
        updateStatus(ResourceHelper.FindResourceString("TraceStarted"), Colors.Green);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try { await _pingManager.StartTraceAsync(TraceUrl, _cts.Token, UpdateResult); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            showMessage(
                string.Format(ResourceHelper.FindResourceString("TraceError"), ex.Message),
                ResourceHelper.FindResourceString("ErrorCaption"),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            IsTracing = false;
            updateStatus(ResourceHelper.FindResourceString("TraceStopped"), Colors.Red);
        }
    }

    public void StopTrace() => _cts?.Cancel();

    public void ClearResults()
    {
        _results.Clear();
        _pingManager.ClearHopData();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _cts?.Dispose();
            _memoryCache.Dispose();
            _disposed = true;
        }
    }

    ~TraceManager() => Dispose(false);

    private void UpdateResult(string ip, int ttl, string domain, HopData hop)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                var existing = _results.FirstOrDefault(r => r.IPAddress == ip);
                if (existing == null)
                    _results.Add(new TraceResult(ttl, ip, domain, hop));
                else
                    existing.UpdateStatistics(hop);
            }),
            DispatcherPriority.Background
        );
    }
}