namespace PingTestTool;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class GraphWindow : Window
{
    readonly GraphRenderer _renderer;

    public GraphWindow(int maxPoints = 100)
    {
        InitializeComponent();

        _renderer = new(GraphCanvas, TryFindBrush, UpdateStats);

        int pts = Math.Max(maxPoints, 25);
        _renderer.SetMaxPoints(pts);
        PointsSlider.Value = pts;
        PointsValue.Text = pts.ToString();
    }

    public void SetPingData(IReadOnlyList<(DateTime, int)> data)
    {
        if (Dispatcher.CheckAccess())
            _renderer.SetData(data);
        else
            Dispatcher.Invoke(() => _renderer.SetData(data));
    }

    public void AddPingPoint(int rtt)
    {
        if (Dispatcher.CheckAccess())
            _renderer.AddPoint(rtt);
        else
            Dispatcher.Invoke(() => _renderer.AddPoint(rtt));
    }

    void UpdateStats(string min, string avg, string max, string cur) =>
        (MinValue.Text, AvgValue.Text, MaxValue.Text, CurrentValue.Text) = (min, avg, max, cur);

    void PointsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PointsValue is null)
            return;

        int pts = (int)e.NewValue;
        PointsValue.Text = pts.ToString();
        _renderer?.SetMaxPoints(pts);
    }

    Brush? TryFindBrush(string key)
    {
        try { return FindResource(key) as Brush; }
        catch { return null; }
    }
}