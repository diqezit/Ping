namespace PingTestTool.Visual;

public sealed class GraphRenderer(
    Canvas canvas,
    Func<string, IBrush?> findBrush,
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
        if (v <= 0) return;
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
        double w = canvas.Bounds.Width, h = canvas.Bounds.Height;
        if (w < 80 || h < 50)
        {
            onStats("0", "0", "0", "0");
            SetVisibility(false);
            return;
        }

        var lineBrush = findBrush("BgAccent") ?? Brushes.DodgerBlue;
        var textBrush = findBrush("FgPrimary") ?? Brushes.White;
        var gridBrush = findBrush("Border") ?? Brushes.Gray;

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

    void EnsureGrid(IBrush b)
    {
        if (_grid[0] is not null) return;
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
            line.StartPoint = new Point(ML, y);
            line.EndPoint = new Point(ML + pw, y);
            line.IsVisible = true;
        }
    }

    void EnsureYAxis(IBrush b)
    {
        if (_yAxis[0] is not null) return;
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
            t.IsVisible = true;
            t.Measure(Size.Infinity);
            Canvas.SetLeft(t, ML - t.DesiredSize.Width - 4);
            Canvas.SetTop(t, y - t.DesiredSize.Height / 2);
        }
    }

    void UpdateChart(int[] arr, double pw, double ph, int yMax, IBrush b)
    {
        if (_line is null)
        {
            _line = new Polyline { StrokeThickness = 2, StrokeJoin = PenLineJoin.Round };
            canvas.Children.Add(_line);
        }

        _line.Stroke = b;
        _line.IsVisible = true;
        _line.Points = [];

        int last = arr.Length - 1;
        var points = new List<Point>(arr.Length);
        for (int i = 0; i < arr.Length; i++)
        {
            double x = ML + (last > 0 ? (double)i / last * pw : pw / 2);
            double y = MT + ph * (1 - (double)arr[i] / yMax);
            points.Add(new Point(x, y));
        }
        _line.Points = [.. points];

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
            var pt = points[i];
            dot.Fill = b;
            dot.IsVisible = true;
            Canvas.SetLeft(dot, pt.X - R);
            Canvas.SetTop(dot, pt.Y - R);
        }
    }

    void HideChart()
    {
        _line?.IsVisible = false;
        foreach (var dot in _dots) dot.IsVisible = false;
        for (int i = 0; i < GridCount; i++)
            _yAxis[i]?.IsVisible = false;
    }

    void SetVisibility(bool visible)
    {
        _line?.IsVisible = visible;
        foreach (var dot in _dots) dot.IsVisible = visible;
        for (int i = 0; i < GridCount; i++)
        {
            _grid[i]?.IsVisible = visible;
            _yAxis[i]?.IsVisible = visible;
        }
    }
}