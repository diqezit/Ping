namespace PingTestTool.Visual;

public sealed class RouteRenderer(
    Canvas canvas,
    Func<string, IBrush?> findBrush,
    Func<string, Color?> findColor,
    Action<TraceResult>? onClick = null)
{
    const double Margin = 30, Radius = 12;
    const int MaxIpLen = 12;

    readonly List<(Ellipse E, TextBlock Nr, TextBlock Ip)> _hops = new(32);
    Line? _routeLine;

    public void Clear()
    {
        canvas.Children.Clear();
        _hops.Clear();
        _routeLine = null;
    }

    public void Draw(IReadOnlyList<TraceResult>? results)
    {
        if (results is not { Count: > 0 })
        {
            Clear();
            return;
        }

        double w = canvas.Bounds.Width, h = canvas.Bounds.Height;
        if (w < 50 || h < 20)
        {
            Clear();
            return;
        }

        int cnt = results.Count;
        double step = (w - Margin * 2) / Math.Max(cnt - 1, 1), cy = h / 2;

        var lineBrush = findBrush("RouteLine") as ISolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(100, 100, 100));
        var textBrush = findBrush("RouteText") ?? new SolidColorBrush(Color.FromRgb(200, 200, 200));
        var goodColor = findColor("RouteGoodColor") ?? Colors.DodgerBlue;
        var badColor = findColor("RouteBadColor") ?? Colors.Red;

        EnsureLine(Margin, cy, w - Margin, lineBrush);
        EnsureHops(cnt);

        for (int i = 0; i < cnt; i++)
        {
            var hop = results[i];
            double x = Margin + i * step;
            double loss = ParseLoss(hop.Loss);
            var col = LerpColor(goodColor, badColor, loss / 100.0);
            UpdateHop(i, x, cy, col, hop, textBrush);
        }
    }

    void EnsureLine(double x1, double y, double x2, IBrush brush)
    {
        if (_routeLine is null)
        {
            _routeLine = new Line
            {
                StrokeThickness = 3,
                StrokeDashArray = [4, 2]
            };
            canvas.Children.Add(_routeLine);
        }

        _routeLine.StartPoint = new Point(x1, y);
        _routeLine.EndPoint = new Point(x2, y);
        _routeLine.Stroke = brush;
    }

    void EnsureHops(int count)
    {
        while (_hops.Count > count)
        {
            int idx = _hops.Count - 1;
            var (e, nr, ip) = _hops[idx];
            canvas.Children.Remove(e);
            canvas.Children.Remove(nr);
            canvas.Children.Remove(ip);
            _hops.RemoveAt(idx);
        }

        while (_hops.Count < count)
        {
            var e = new Ellipse
            {
                Width = Radius * 2,
                Height = Radius * 2,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            e.PointerPressed += OnPointerPressed;

            var nr = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                IsHitTestVisible = false
            };

            var ip = new TextBlock
            {
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };

            _hops.Add((e, nr, ip));
            canvas.Children.Add(e);
            canvas.Children.Add(nr);
            canvas.Children.Add(ip);
        }
    }

    void UpdateHop(int idx, double x, double cy, Color fill, TraceResult hop, IBrush textBrush)
    {
        var (e, nr, ip) = _hops[idx];

        var brush = new SolidColorBrush(fill);
        e.Fill = brush;
        e.Tag = hop;
        ToolTip.SetTip(e, BuildTip(hop));
        Canvas.SetLeft(e, x - Radius);
        Canvas.SetTop(e, cy - Radius);

        nr.Text = hop.Nr.ToString();
        nr.Measure(Size.Infinity);
        Canvas.SetLeft(nr, x - nr.DesiredSize.Width / 2);
        Canvas.SetTop(nr, cy - nr.DesiredSize.Height / 2);

        string ipText = hop.IPAddress.Length > MaxIpLen
            ? $"{hop.IPAddress[..MaxIpLen]}…"
            : hop.IPAddress;

        ip.Text = ipText;
        ip.Foreground = textBrush;
        ip.Measure(Size.Infinity);
        Canvas.SetLeft(ip, x - ip.DesiredSize.Width / 2);
        Canvas.SetTop(ip, cy + Radius + 4);
    }

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Ellipse { Tag: TraceResult hop })
            onClick?.Invoke(hop);
    }

    static string BuildTip(TraceResult r) =>
        $"TTL: {r.Nr}\nIP: {r.IPAddress}\nDomain: {r.DomainName}\n" +
        $"Loss: {r.Loss}\nSent: {r.Sent}, Recv: {r.Received}\n" +
        $"Last: {r.Last}, Avg: {r.Avrg}\nBest: {r.Best}, Wrst: {r.Wrst}";

    static double ParseLoss(string s) =>
        string.IsNullOrEmpty(s) ? 0 :
        double.TryParse(s.TrimEnd('%', ' '), out double v) ? Math.Clamp(v, 0, 100) : 0;

    static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}