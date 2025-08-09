#nullable enable

using static PingTestTool.MainWindow;

namespace PingTestTool;

public partial class GraphWindow : Window, INotifyPropertyChanged, IDisposable, IGraphWindow
{
    private readonly IGraphManager _graphManager;
    private readonly DispatcherTimer _updateTimer;
    private int _maxVisiblePoints = GraphConstants.DefaultMaxVisiblePoints;
    private bool _disposed;

    public PlotModel PingPlotModel { get; }

    public int MaxVisiblePoints
    {
        get => _maxVisiblePoints;
        set
        {
            if (_maxVisiblePoints == value) return;

            _maxVisiblePoints = value;
            OnPropertyChanged();
            _graphManager.UpdateMaxVisiblePoints(value);
            _graphManager.UpdateGraph();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public GraphWindow(int pingInterval)
    {
        InitializeComponent();
        DataContext = this;

        PingPlotModel = CreatePlotModel();
        _graphManager = new GraphManager(PingPlotModel, UpdateTextFields);

        _updateTimer = CreateTimer(NormalizeInterval(pingInterval));

        HookEvents();
        StartAndApplyTheme();
    }

    private static int NormalizeInterval(int ms) =>
        Math.Max(ms, GraphConstants.MinPingIntervalMilliseconds);

    private static PlotModel CreatePlotModel() =>
        new()
        {
            Title = GraphConstants.GraphTitle,
            Background = GraphConstants.GraphBackgroundColor
        };

    private static DispatcherTimer CreateTimer(int intervalMs) =>
        new()
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };

    private void HookEvents()
    {
        _updateTimer.Tick += UpdateTimer_Tick;
        StateChanged += GraphWindow_StateChanged;
    }

    private void UnhookEvents()
    {
        _updateTimer.Tick -= UpdateTimer_Tick;
        StateChanged -= GraphWindow_StateChanged;
    }

    private void StartAndApplyTheme()
    {
        _updateTimer.Start();
        ApplyThemeToPlot();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e) =>
        _graphManager.UpdateGraph();

    private void GraphWindow_StateChanged(object? sender, EventArgs e) =>
        WindowHelper.AdjustWindowCorners(this);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        WindowHelper.HandleTitleBarMouseLeftButtonDown(this, e);

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ApplyThemeToPlot()
    {
        try
        {
            if (TryDetectDarkTheme())
                ApplyDarkThemeToPlot();

            PingPlotModel.InvalidatePlot(true);
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyDarkThemeToPlot()
    {
        PingPlotModel.TextColor = OxyColors.LightGray;
        PingPlotModel.PlotAreaBorderColor = OxyColors.Gray;
        PingPlotModel.Background = OxyColor.FromRgb(45, 45, 48);

        foreach (var axis in PingPlotModel.Axes)
        {
            axis.TextColor = OxyColors.LightGray;
            axis.TitleColor = OxyColors.White;
            axis.AxislineColor = OxyColors.Gray;
            axis.MajorGridlineColor = OxyColor.FromRgb(70, 70, 70);
            axis.MinorGridlineColor = OxyColor.FromRgb(50, 50, 50);
        }
    }

    private static bool TryDetectDarkTheme() =>
        Application.Current.Resources.Contains("WindowBackground") &&
        Application.Current.Resources["WindowBackground"] is System.Windows.Media.SolidColorBrush brush &&
        brush.Color.R < 128 && brush.Color.G < 128 && brush.Color.B < 128;

    private void UpdateTextFields(string min, string avg, string max, string cur) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            txtMin.Text = min;
            txtAvg.Text = avg;
            txtMax.Text = max;
            txtCur.Text = cur;
        }), DispatcherPriority.Background);

    public void SetPingData(List<(DateTime Time, int RoundtripTime)> data)
    {
        if (data is null || data.Count == 0) return;

        var transformed = ToValueArray(data);
        _graphManager.SetData(transformed);
        _graphManager.UpdateGraph();
    }

    private static (DateTime Time, int Value)[] ToValueArray(List<(DateTime Time, int RoundtripTime)> data)
    {
        var arr = new (DateTime Time, int Value)[data.Count];
        for (int i = 0; i < data.Count; i++)
            arr[i] = (data[i].Time, data[i].RoundtripTime);
        return arr;
    }

    public new void Show() =>
        Dispatcher.Invoke(() => base.Show());

    public new void Close() =>
        Dispatcher.Invoke(() => base.Close());

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        try { _updateTimer.Stop(); } catch { /* ignore */ }
        UnhookEvents();
        _graphManager.Dispose();

        _disposed = true;
    }
}