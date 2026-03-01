namespace PingTestTool;

public partial class GraphWindow : Window
{
    readonly GraphRenderer _renderer;

    public GraphWindow() : this(100) { }

    public GraphWindow(int maxPoints)
    {
        InitializeComponent();
        _renderer = new(GraphCanvas, BrushOf, OnStats);

        int pts = Math.Max(maxPoints, 25);
        _renderer.SetMaxPoints(pts);
        PointsSlider.Value = pts;
        PointsVal.Text = pts.ToString();

        PointsSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            int p = (int)PointsSlider.Value;
            PointsVal.Text = p.ToString();
            _renderer.SetMaxPoints(p);
        };

        GraphCanvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Visual.BoundsProperty)
                _renderer.SetMaxPoints((int)PointsSlider.Value);
        };
    }

    public void SetPingData(IReadOnlyList<(DateTime, int)> data) =>
        Post(() => _renderer.SetData(data));

    public void AddPingPoint(int rtt) =>
        Post(() => _renderer.AddPoint(rtt));

    void OnStats(string min, string avg, string max, string cur) =>
        (MinVal.Text, AvgVal.Text, MaxVal.Text, CurVal.Text) = (min, avg, max, cur);

    IBrush? BrushOf(string key)
    {
        try { return this.FindResource(key) as IBrush; }
        catch { return null; }
    }

    void Post(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}