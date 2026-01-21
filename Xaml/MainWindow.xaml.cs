namespace PingTestTool;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class MainWindow : Window, IDisposable
{
    public static readonly RoutedCommand StartPingCommand = new();
    public static readonly RoutedCommand StopPingCommand = new();

    const string DefUrl = "8.8.8.8", ThemeDir = "Themes", LangDir = "Resources";
    const int DefPingCnt = 10, DefTimeout = 1000;

    readonly PingService _ping = new();
    readonly DispatcherTimer _routeTimer;
    RouteRenderer? _routeRenderer;
    TraceManager? _trace;
    CancellationTokenSource? _pingCts;
    GraphWindow? _graph;
    bool _isPinging, _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _routeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        _routeTimer.Tick += (_, _) => DrawRoute();
        Init();
    }

    void Init()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        Closed += OnClosed;
        RouteCanvas.SizeChanged += (_, _) => DrawRoute();

        (txtURL.Text, txtPingCount.Text, txtTimeout.Text) = (DefUrl, DefPingCnt.ToString(), DefTimeout.ToString());
        ApplyLang("StringResources.en.xaml");

        _routeRenderer = new(RouteCanvas, TryFindBrush, TryFindColor, OnRouteHopClick);

        _ping.OnPingResult += s => RunOnUI(() => txtResults.AppendText(s));
        _ping.OnProgressUpdate += (c, t) => RunOnUI(() => progressBar.Value = t > 0 ? c * 100.0 / t : 0);
        _ping.OnRoundtripTimeAdded += OnRttAdded;
    }

    void StartPingCommand_Executed(object sender, ExecutedRoutedEventArgs e) => BtnPing_Click(sender, e);
    void StartPingCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_isPinging;
    void StopPingCommand_Executed(object sender, ExecutedRoutedEventArgs e) => BtnStop_Click(sender, e);
    void StopPingCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _isPinging;

    void OnRouteHopClick(TraceResult hop)
    {
        ResultsList.SelectedItem = hop;
        ResultsList.ScrollIntoView(hop);
        ResultsList.Focus();
    }

    void OnRttAdded(DateTime _, int rtt) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_graph is { IsLoaded: true })
                _graph.AddPingPoint(rtt);
        });

    void DrawRoute() => _routeRenderer?.Draw(_trace?.TraceResults);

    Brush? TryFindBrush(string key)
    {
        try { return FindResource(key) as Brush; }
        catch { return null; }
    }

    Color? TryFindColor(string key)
    {
        try { return FindResource(key) is Color c ? c : null; }
        catch { return null; }
    }

    async void BtnPing_Click(object s, RoutedEventArgs e)
    {
        if (_isPinging)
            return;

        if (!ValidateInput(out string url, out int cnt, out int timeout))
            return;

        _isPinging = true;
        SetPingState(true);

        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _pingCts = new();

        try
        {
            await _ping.ClearRoundtripTimesAsync(_pingCts.Token);
            await _ping.StartPingTestAsync(new(url, cnt, timeout), _pingCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Msg($"{Res("PingError")}: {ex.Message}", Res("ErrorCaption")); }
        finally
        {
            _isPinging = false;
            SetPingState(false);
        }
    }

    void BtnStop_Click(object s, RoutedEventArgs e) => _pingCts?.Cancel();

    async void BtnShowGraph_Click(object s, RoutedEventArgs e)
    {
        var data = await _ping.GetRoundtripTimesAsync();
        if (data.Count == 0)
        {
            Msg(Res("ErrorNoGraphData"), Res("WarningCaption"));
            return;
        }

        if (_graph is not { IsLoaded: true })
        {
            _graph = new(int.TryParse(txtPingCount.Text, out int v) ? v : 100);
            _graph.Closed += (_, _) => _graph = null;
            _graph.Show();
        }

        _graph.SetPingData(data);
    }

    void BtnClearResultsPing_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(Res("ClearPingResultsConfirmation"), Res("ConfirmationCaption"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            txtResults.Clear();
    }

    async void BtnExportPing_Click(object s, RoutedEventArgs e)
    {
        string text = txtResults.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Msg(Res("NoResults"), Res("WarningCaption"));
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text (*.txt)|*.txt|All (*.*)|*.*" };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            await File.WriteAllTextAsync(dlg.FileName, text);
            Msg(Res("Saved"), Res("SuccessCaption"));
        }
        catch (Exception ex) { Msg($"{Res("SaveError")}: {ex.Message}", Res("ErrorCaption")); }
    }

    async void BtnStartTrace_Click(object s, RoutedEventArgs e)
    {
        if (_trace is { IsTracing: true })
        {
            Msg(Res("TraceAlreadyRunning"), Res("WarningCaption"));
            return;
        }

        string url = txtURL.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            Msg(Res("SpecifyUrl"), Res("WarningCaption"));
            return;
        }

        if (!IsValidHost(url))
        {
            Msg(Res("UrlInvalidError"), Res("InputErrorCaption"));
            return;
        }

        SetTraceState(true);

        try
        {
            _trace?.Dispose();
            _trace = new(url);
            BindTrace(_trace);
            _routeTimer.Start();
            await _trace.StartTraceAsync(UpdStatusSafe, MsgSafe);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Msg(string.Format(Res("TraceError"), ex.Message), Res("ErrorCaption")); }
        finally
        {
            _routeTimer.Stop();
            DrawRoute();
            SetTraceState(false);
        }
    }

    void BtnStopTrace_Click(object s, RoutedEventArgs e)
    {
        if (_trace is not { IsTracing: true })
            return;

        _trace.StopTrace();
        UpdStatusSafe(Res("Stopping"), Colors.Orange);
    }

    void BtnClearResults_Click(object s, RoutedEventArgs e)
    {
        _trace?.ClearResults();
        _routeRenderer?.Clear();
    }

    async void BtnSaveResults_Click(object s, RoutedEventArgs e)
    {
        if (_trace?.TraceResults is not { Count: > 0 } results)
        {
            Msg(Res("NoResults"), Res("WarningCaption"));
            return;
        }

        var snapshot = results.ToArray();

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text (*.txt)|*.txt|All (*.*)|*.*" };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var sb = new StringBuilder();
            foreach (var r in snapshot)
                sb.AppendLine(r?.ToString() ?? "Empty");

            await File.WriteAllTextAsync(dlg.FileName, sb.ToString());
            Msg(Res("Saved"), Res("SuccessCaption"));
        }
        catch (Exception ex) { Msg($"{Res("SaveError")}: {ex.Message}", Res("ErrorCaption")); }
    }

    bool ValidateInput(out string url, out int cnt, out int timeout)
    {
        url = txtURL.Text?.Trim() ?? "";
        cnt = timeout = 0;

        if (string.IsNullOrWhiteSpace(url))
        {
            Msg(Res("UrlEmptyError"), Res("InputErrorCaption"));
            return false;
        }

        if (!IsValidHost(url))
        {
            Msg(Res("UrlInvalidError"), Res("InputErrorCaption"));
            return false;
        }

        if (!int.TryParse(txtPingCount.Text, out cnt) || cnt < 1 || cnt > 1000)
        {
            Msg(string.Format(Res("PingCountRangeError"), 1, 1000), Res("InputErrorCaption"));
            return false;
        }

        if (!int.TryParse(txtTimeout.Text, out timeout) || timeout < 100)
        {
            Msg(string.Format(Res("TimeoutMinimumError"), 100), Res("InputErrorCaption"));
            return false;
        }

        return true;
    }

    static bool IsValidHost(string host) =>
        IPAddress.TryParse(host, out _) ||
        (Uri.TryCreate($"http://{host}", UriKind.Absolute, out var uri) &&
         string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));

    void BindTrace(TraceManager mgr)
    {
        ResultsList.ItemsSource = CollectionViewSource.GetDefaultView(mgr.TraceResults);
        if (ResultsList.ItemsSource is ICollectionView v)
        {
            v.SortDescriptions.Clear();
            v.SortDescriptions.Add(new SortDescription("Nr", ListSortDirection.Ascending));
        }
    }

    void SetPingState(bool running)
    {
        (btnPing.IsEnabled, btnStop.IsEnabled) = (!running, running);
        btnPing.Content = Res(running ? "BtnWaitText" : "BtnStartText");
        if (!running)
            progressBar.Value = 0;
        CommandManager.InvalidateRequerySuggested();
    }

    void SetTraceState(bool tracing) =>
        (btnStartTrace.IsEnabled, btnStopTrace.IsEnabled) = (!tracing, tracing);

    void UpdStatusSafe(string msg, Color col) =>
        RunOnUI(() => (StatusTextBlock.Text, StatusTextBlock.Foreground) = (msg, new SolidColorBrush(col)));

    void MsgSafe(string msg, string title, MessageBoxButton btn, MessageBoxImage icon) =>
        RunOnUI(() => MessageBox.Show(msg, title, btn, icon));

    void RunOnUI(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    void DarkTheme_Click(object s, RoutedEventArgs e) => ApplyTheme("DarkTheme.xaml");
    void LightTheme_Click(object s, RoutedEventArgs e) => ApplyTheme("LightTheme.xaml");
    void RussianLanguage_Click(object s, RoutedEventArgs e) => ApplyLang("StringResources.ru.xaml");
    void EnglishLanguage_Click(object s, RoutedEventArgs e) => ApplyLang("StringResources.en.xaml");

    void ApplyTheme(string f)
    {
        ResourceHelper.ApplyResourceDictionary($"{ThemeDir}/{f}", ThemeDir, this);
        DrawRoute();
    }

    void ApplyLang(string f) => ResourceHelper.ApplyResourceDictionary($"{LangDir}/{f}", LangDir, this);

    void OnClosed(object? s, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _routeTimer.Stop();
        _ping.OnRoundtripTimeAdded -= OnRttAdded;
        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _trace?.Dispose();
        _ping.Dispose();

        try { if (_graph is { IsLoaded: true }) _graph.Close(); }
        catch (InvalidOperationException) { }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        Closed -= OnClosed;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    static void OnUnhandled(object? s, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            MessageBox.Show($"{Res("CriticalError")}: {ex.Message}", Res("ErrorCaption"),
                MessageBoxButton.OK, MessageBoxImage.Error);
    }

    static void Msg(string msg, string title,
        MessageBoxButton btn = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.Information) =>
        MessageBox.Show(msg, title, btn, icon);

    static string Res(string k) => ResourceHelper.FindResourceString(k);
}