namespace PingTestTool;

public partial class MainWindow : Window, IDisposable
{
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
        Closed += OnClosed;

        RouteCanvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Visual.BoundsProperty)
                DrawRoute();
        };

        _routeRenderer = new(RouteCanvas, TryFindBrush, TryFindColor, OnRouteHopClick);

        _ping.OnPingResult += s => RunOnUI(() =>
        {
            txtResults.Text += s;
            txtResults.CaretIndex = txtResults.Text?.Length ?? 0;
        });

        _ping.OnProgressUpdate += (c, t) =>
            RunOnUI(() => progress.Value = t > 0 ? c * 100.0 / t : 0);

        _ping.OnRoundtripTimeAdded += OnRttAdded;
    }

    void OnRouteHopClick(TraceResult hop)
    {
        ResultsList.SelectedItem = hop;
        ResultsList.ScrollIntoView(hop, null);
        ResultsList.Focus();
    }

    void OnRttAdded(DateTime _, int rtt) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_graph is not null)
                _graph.AddPingPoint(rtt);
        });

    void DrawRoute() => _routeRenderer?.Draw(_trace?.TraceResults);

    IBrush? TryFindBrush(string key)
    {
        try { return this.FindResource(key) as IBrush; }
        catch { return null; }
    }

    Color? TryFindColor(string key)
    {
        try { return this.FindResource(key) is Color c ? c : null; }
        catch { return null; }
    }

    async void Ping_Click(object? s, RoutedEventArgs e)
    {
        if (_isPinging) return;
        if (!ValidateInput(out string url, out int cnt, out int timeout)) return;

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
        catch (Exception ex) { await ShowMsg($"{S("PingError")}: {ex.Message}", S("ErrorCaption")); }
        finally
        {
            _isPinging = false;
            SetPingState(false);
        }
    }

    void Stop_Click(object? s, RoutedEventArgs e) => _pingCts?.Cancel();

    async void Graph_Click(object? s, RoutedEventArgs e)
    {
        if (_graph is null)
        {
            _graph = new(int.TryParse(txtCount.Text, out int v) ? v : 100);
            _graph.Closed += (_, _) => _graph = null;
        }

        var data = await _ping.GetRoundtripTimesAsync();
        if (data.Count > 0)
            _graph.SetPingData(data);

        if (!_graph.IsVisible)
            _graph.Show();

        _graph.Activate();
    }

    void ClearPing_Click(object? s, RoutedEventArgs e) => txtResults.Text = "";

    async void Export_Click(object? s, RoutedEventArgs e)
    {
        string text = txtResults.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowMsg(S("NoResults"), S("WarningCaption"));
            return;
        }

        var file = await PickSaveFile("txt");
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
            await ShowMsg(S("Saved"), S("SuccessCaption"));
        }
        catch (Exception ex)
        {
            await ShowMsg($"{S("SaveError")}: {ex.Message}", S("ErrorCaption"));
        }
    }

    async void BtnStartTrace_Click(object? s, RoutedEventArgs e)
    {
        if (_trace is { IsTracing: true })
        {
            await ShowMsg(S("TraceAlreadyRunning"), S("WarningCaption"));
            return;
        }

        string url = txtUrl.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            await ShowMsg(S("SpecifyUrl"), S("WarningCaption"));
            return;
        }

        if (!IsValidHost(url))
        {
            await ShowMsg(S("UrlInvalidError"), S("InputErrorCaption"));
            return;
        }

        SetTraceState(true);

        try
        {
            _trace?.Dispose();
            _trace = new(url);
            ResultsList.ItemsSource = _trace.TraceResults;
            _routeTimer.Start();

            await _trace.StartTraceAsync(
                (msg, color) => RunOnUI(() =>
                {
                    StatusText.Text = msg;
                    StatusText.Foreground = new SolidColorBrush(color);
                }),
                (msg, title) => RunOnUI(async () => await ShowMsg(msg, title)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await ShowMsg(string.Format(S("TraceError"), ex.Message), S("ErrorCaption"));
        }
        finally
        {
            _routeTimer.Stop();
            DrawRoute();
            SetTraceState(false);
        }
    }

    void BtnStopTrace_Click(object? s, RoutedEventArgs e)
    {
        if (_trace is not { IsTracing: true }) return;
        _trace.StopTrace();
        StatusText.Text = S("Stopping");
        StatusText.Foreground = new SolidColorBrush(Colors.Orange);
    }

    void BtnClearResults_Click(object? s, RoutedEventArgs e)
    {
        _trace?.ClearResults();
        _routeRenderer?.Clear();
    }

    async void BtnSaveResults_Click(object? s, RoutedEventArgs e)
    {
        if (_trace?.TraceResults is not { Count: > 0 } results)
        {
            await ShowMsg(S("NoResults"), S("WarningCaption"));
            return;
        }

        var snapshot = results.ToArray();
        var file = await PickSaveFile("txt");
        if (file is null) return;

        try
        {
            var sb = new StringBuilder();
            foreach (var r in snapshot)
                sb.AppendLine(r?.ToString() ?? "Empty");

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(sb.ToString());
            await ShowMsg(S("Saved"), S("SuccessCaption"));
        }
        catch (Exception ex)
        {
            await ShowMsg($"{S("SaveError")}: {ex.Message}", S("ErrorCaption"));
        }
    }

    bool ValidateInput(out string url, out int cnt, out int timeout)
    {
        url = txtUrl.Text?.Trim() ?? "";
        cnt = timeout = 0;

        if (string.IsNullOrWhiteSpace(url))
        {
            _ = ShowMsg(S("UrlEmptyError"), S("InputErrorCaption"));
            return false;
        }

        if (!IsValidHost(url))
        {
            _ = ShowMsg(S("UrlInvalidError"), S("InputErrorCaption"));
            return false;
        }

        if (!int.TryParse(txtCount.Text, out cnt) || cnt < 1 || cnt > 1000)
        {
            _ = ShowMsg(string.Format(S("PingCountRangeError"), 1, 1000), S("InputErrorCaption"));
            return false;
        }

        if (!int.TryParse(txtTimeout.Text, out timeout) || timeout < 100)
        {
            _ = ShowMsg(string.Format(S("TimeoutMinimumError"), 100), S("InputErrorCaption"));
            return false;
        }

        return true;
    }

    static bool IsValidHost(string host) =>
        IPAddress.TryParse(host, out _) ||
        (Uri.TryCreate($"http://{host}", UriKind.Absolute, out var uri) &&
         string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));

    void SetPingState(bool running)
    {
        btnPing.IsEnabled = !running;
        btnStop.IsEnabled = running;
        btnPing.Content = S(running ? "BtnWaitText" : "BtnStartText");
        if (!running) progress.Value = 0;
    }

    void SetTraceState(bool tracing)
    {
        btnStartTrace.IsEnabled = !tracing;
        btnStopTrace.IsEnabled = tracing;
    }

    void RunOnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    void Dark_Click(object? s, RoutedEventArgs e) { ApplyTheme("DarkTheme.axaml"); DrawRoute(); }
    void Light_Click(object? s, RoutedEventArgs e) { ApplyTheme("LightTheme.axaml"); DrawRoute(); }

    void Ru_Click(object? s, RoutedEventArgs e)
    {
        Strings.SetLanguage(Lang.Ru);
        RefreshLabels();
    }

    void En_Click(object? s, RoutedEventArgs e)
    {
        Strings.SetLanguage(Lang.En);
        RefreshLabels();
    }

    void RefreshLabels()
    {
        Title = S("WindowTitle");
        tabPing.Header = S("PingTabHeader");
        tabTrace.Header = S("TraceRouteTabHeader");
        btnPing.Content = S(_isPinging ? "BtnWaitText" : "BtnStartText");
        btnStop.Content = S("StopButton");
        btnClear.Content = S("ClearResultsButton");
        btnGraph.Content = S("GraphButton");
        btnExport.Content = S("ExportButton");
        btnStartTrace.Content = S("StartTraceButton");
        btnStopTrace.Content = S("StopTraceButton");
        btnClearResults.Content = S("ClearResultsButton");
        btnSaveResults.Content = S("SaveResultsButton");
    }

    static void ApplyTheme(string fileName)
    {
        var app = Application.Current;
        if (app is null) return;

        for (int i = app.Styles.Count - 1; i >= 0; i--)
            if (app.Styles[i] is StyleInclude si
                && si.Source?.ToString().Contains("Themes/") == true)
                app.Styles.RemoveAt(i);

        app.Styles.Add(new StyleInclude(new Uri("avares://PingTestTool"))
        {
            Source = new Uri($"avares://PingTestTool/Themes/{fileName}")
        });
    }

    async Task ShowMsg(string msg, string title = "PingTestTool")
    {
        var okBtn = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24, 6)
        };

        var dlg = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap },
                    okBtn
                }
            }
        };

        okBtn.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async Task<Avalonia.Platform.Storage.IStorageFile?> PickSaveFile(string ext)
    {
        var provider = GetTopLevel(this)?.StorageProvider;
        if (provider is null) return null;

        return await provider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            DefaultExtension = ext,
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType($"{ext} files") { Patterns = [$"*.{ext}"] }]
        });
    }

    void OnClosed(object? s, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _routeTimer.Stop();
        _ping.OnRoundtripTimeAdded -= OnRttAdded;
        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _trace?.Dispose();
        _ping.Dispose();
        try { _graph?.Close(); } catch { }
        Closed -= OnClosed;
        GC.SuppressFinalize(this);
    }

    static string S(string k) => Strings.Get(k);
}