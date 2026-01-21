namespace PingTestTool.Visual;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GraphRenderer(
    Canvas canvas,
    Func<string, Brush?> findBrush,
    Action<string, string, string, string> onStats)
{
    const double ML = 45, MR = 15, MT = 15, MB = 25, R = 3;
    const int GridCount = 5;

    readonly Queue<int> _q = new(256);
    readonly List<Ellipse> _dots = new(256);
    readonly Line[] _grid = new Line[GridCount];
    readonly TextBlock[] _yAxis = new TextBlock[GridCount];

    Polyline? _line;
    int _max = 100;

    public void SetMaxPoints(int max)
    {
        _max = Math.Max(max, 10);
        Trim();
        Draw();
    }

    public void Clear()
    {
        _q.Clear();
        Draw();
    }

    public void SetData(IReadOnlyList<(DateTime, int)>? data)
    {
        _q.Clear();
        if (data is { Count: > 0 })
            foreach (var (_, v) in data)
                if (v > 0)
                    _q.Enqueue(v);
        Trim();
        Draw();
    }

    public void AddPoint(int v)
    {
        if (v <= 0)
            return;
        _q.Enqueue(v);
        Trim();
        Draw();
    }

    void Trim()
    {
        while (_q.Count > _max)
            _q.Dequeue();
    }

    void Draw()
    {
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 80 || h < 50)
        {
            onStats("0", "0", "0", "0");
            SetVisibility(false);
            return;
        }

        var (lineBrush, textBrush, gridBrush) = (
            findBrush("BgAccent") ?? Brushes.DodgerBlue,
            findBrush("FgPrimary") ?? Brushes.White,
            findBrush("Border") ?? Brushes.Gray);

        double pw = w - ML - MR, ph = h - MT - MB;

        EnsureGrid(gridBrush);
        UpdateGrid(pw, ph);

        if (_q.Count == 0)
        {
            onStats("0", "0", "0", "0");
            HideChart();
            return;
        }

        var arr = _q.ToArray();
        var (min, max, avg, cur) = CalcStats(arr);
        onStats(min.ToString(), $"{avg:F1}", max.ToString(), cur.ToString());

        int yMax = Math.Max(max + 10, 50);
        EnsureYAxis(textBrush);
        UpdateYAxis(yMax, ph);
        UpdateChart(arr, pw, ph, yMax, lineBrush);
    }

    static (int Min, int Max, double Avg, int Cur) CalcStats(int[] arr)
    {
        int min = arr[0], max = arr[0], cur = arr[^1];
        long sum = 0;
        foreach (int v in arr)
        {
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
        }
        return (min, max, (double)sum / arr.Length, cur);
    }

    void EnsureGrid(Brush b)
    {
        if (_grid[0] is not null)
            return;

        for (int i = 0; i < GridCount; i++)
        {
            _grid[i] = new Line { Stroke = b, StrokeThickness = 0.5, Opacity = 0.3 };
            canvas.Children.Add(_grid[i]);
        }
    }

    void UpdateGrid(double pw, double ph)
    {
        for (int i = 0; i < GridCount; i++)
        {
            double y = MT + ph * i / (GridCount - 1);
            var line = _grid[i];
            (line.X1, line.Y1, line.X2, line.Y2) = (ML, y, ML + pw, y);
            line.Visibility = Visibility.Visible;
        }
    }

    void EnsureYAxis(Brush b)
    {
        if (_yAxis[0] is not null)
            return;

        for (int i = 0; i < GridCount; i++)
        {
            _yAxis[i] = new TextBlock { Foreground = b, FontSize = 10 };
            canvas.Children.Add(_yAxis[i]);
        }
    }

    void UpdateYAxis(int yMax, double ph)
    {
        for (int i = 0; i < GridCount; i++)
        {
            int val = yMax * (GridCount - 1 - i) / (GridCount - 1);
            double y = MT + ph * i / (GridCount - 1);

            var t = _yAxis[i];
            t.Text = $"{val}";
            t.Visibility = Visibility.Visible;
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, ML - t.DesiredSize.Width - 4);
            Canvas.SetTop(t, y - t.DesiredSize.Height / 2);
        }
    }

    void UpdateChart(int[] arr, double pw, double ph, int yMax, Brush b)
    {
        if (_line is null)
        {
            _line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            canvas.Children.Add(_line);
        }

        _line.Stroke = b;
        _line.Visibility = Visibility.Visible;
        _line.Points.Clear();

        int last = arr.Length - 1;
        for (int i = 0; i < arr.Length; i++)
        {
            double x = ML + (last > 0 ? (double)i / last * pw : pw / 2);
            double y = MT + ph * (1 - (double)arr[i] / yMax);
            _line.Points.Add(new Point(x, y));
        }

        while (_dots.Count > arr.Length)
        {
            canvas.Children.Remove(_dots[^1]);
            _dots.RemoveAt(_dots.Count - 1);
        }

        while (_dots.Count < arr.Length)
        {
            var dot = new Ellipse { Width = R * 2, Height = R * 2 };
            _dots.Add(dot);
            canvas.Children.Add(dot);
        }

        for (int i = 0; i < arr.Length; i++)
        {
            var dot = _dots[i];
            var pt = _line.Points[i];
            dot.Fill = b;
            dot.Visibility = Visibility.Visible;
            Canvas.SetLeft(dot, pt.X - R);
            Canvas.SetTop(dot, pt.Y - R);
        }
    }

    void HideChart()
    {
        _line?.Visibility = Visibility.Collapsed;

        foreach (var dot in _dots)
            dot.Visibility = Visibility.Collapsed;

        for (int i = 0; i < GridCount; i++)
            _yAxis[i]?.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
    }

    void SetVisibility(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;

        _line?.SetValue(UIElement.VisibilityProperty, v);

        foreach (var dot in _dots)
            dot.Visibility = v;

        for (int i = 0; i < GridCount; i++)
        {
            _grid[i]?.SetValue(UIElement.VisibilityProperty, v);
            _yAxis[i]?.SetValue(UIElement.VisibilityProperty, v);
        }
    }
}