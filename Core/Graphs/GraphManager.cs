using PingTestTool;

public class GraphManager : IGraphManager
{
    private const string ValueFormat = "F1";

    private readonly PlotModel _plotModel;
    private readonly Action<string, string, string, string> _updateTextFields;

    private readonly List<PingData> _dataPoints = new(GraphConstants.DefaultMaxVisiblePoints);
    private readonly object _lock = new();
    private readonly LineSeries _lineSeries;

    private int _maxVisiblePoints = GraphConstants.DefaultMaxVisiblePoints;
    private bool _disposed;

    private struct PingData
    {
        public DateTime Time;
        public int Value;
        public PingData(DateTime t, int v) { Time = t; Value = v; }
    }

    public GraphManager(PlotModel plotModel, Action<string, string, string, string> updateTextFields)
    {
        _plotModel = plotModel ?? throw new ArgumentNullException(nameof(plotModel));
        _updateTextFields = updateTextFields ?? throw new ArgumentNullException(nameof(updateTextFields));

        _lineSeries = CreateLineSeries();
        InitializeGraphComponents();
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock) { _dataPoints.Clear(); }
        _disposed = true;
    }

    public void SetData(IEnumerable<(DateTime Time, int Value)> data)
    {
        if (data is null) return;

        lock (_lock)
        {
            _dataPoints.Clear();
            if (data is IList<(DateTime Time, int Value)> list)
                SetDataFromList(list);
            else
                SetDataFromEnumerable(data);
        }
    }

    public void UpdateGraph()
    {
        if (!TryGetSnapshot(out var snapshot))
        {
            ClearSeriesAndRefresh();
            return;
        }

        var pts = _lineSeries.Points;
        PrepareSeriesPoints(pts, snapshot.Count);

        var (min, max, sum, cur, count) = FillSeriesPointsAndCompute(snapshot, pts);
        var avg = CalculateAverage(sum, count);

        UpdateLabels(min, avg, max, cur);
        _plotModel.InvalidatePlot(true);
    }

    public void UpdateMaxVisiblePoints(int maxVisiblePoints)
    {
        _maxVisiblePoints = maxVisiblePoints > 0 ? maxVisiblePoints : GraphConstants.DefaultMaxVisiblePoints;
        lock (_lock) { TrimFrontIfExceedsCapacity(); }
    }

    private void SetDataFromList(IList<(DateTime Time, int Value)> list)
    {
        int start = Math.Max(0, list.Count - _maxVisiblePoints);
        for (int i = start; i < list.Count; i++)
        {
            var (time, value) = list[i];
            if (value > 0)
                _dataPoints.Add(new(time, value));
        }
    }

    private void SetDataFromEnumerable(IEnumerable<(DateTime Time, int Value)> data)
    {
        var buffer = new Queue<PingData>(_maxVisiblePoints);
        foreach (var (time, value) in data)
        {
            if (value <= 0) continue;
            if (buffer.Count == _maxVisiblePoints) buffer.Dequeue();
            buffer.Enqueue(new(time, value));
        }
        _dataPoints.AddRange(buffer);
    }

    private bool TryGetSnapshot(out List<PingData> snapshot)
    {
        lock (_lock)
        {
            if (_dataPoints.Count == 0)
            {
                snapshot = null!;
                return false;
            }
            snapshot = new(_dataPoints);
            return true;
        }
    }

    private void ClearSeriesAndRefresh()
    {
        _lineSeries.Points.Clear();
        _plotModel.InvalidatePlot(true);
    }

    private static void PrepareSeriesPoints(IList<DataPoint> pts, int required)
    {
        pts.Clear();
        if (pts is List<DataPoint> list && list.Capacity < required)
            list.Capacity = required;
    }

    private static (int min, int max, long sum, int cur, int count) FillSeriesPointsAndCompute(
        List<PingData> snapshot,
        IList<DataPoint> pts)
    {
        int min = int.MaxValue;
        int max = int.MinValue;
        long sum = 0;
        int cur = snapshot[snapshot.Count - 1].Value;

        for (int i = 0; i < snapshot.Count; i++)
        {
            var (time, value) = (snapshot[i].Time, snapshot[i].Value);
            pts.Add(new DataPoint(DateTimeAxis.ToDouble(time), value));

            if (value < min) min = value;
            if (value > max) max = value;
            sum += value;
        }

        if (min == int.MaxValue) min = 0;
        if (max == int.MinValue) max = 0;

        return (min, max, sum, cur, snapshot.Count);
    }

    private static double CalculateAverage(long sum, int count) =>
        count > 0 ? (double)sum / count : 0d;

    private void UpdateLabels(int min, double avg, int max, int cur) =>
        _updateTextFields(
            FormatValue(min),
            FormatValue(avg),
            FormatValue(max),
            FormatValue(cur)
        );

    private static string FormatValue(double v) => v.ToString(ValueFormat);

    private void TrimFrontIfExceedsCapacity()
    {
        if (_dataPoints.Count > _maxVisiblePoints)
            _dataPoints.RemoveRange(0, _dataPoints.Count - _maxVisiblePoints);
    }

    private static LineSeries CreateLineSeries() =>
        new()
        {
            Title = "Ping",
            Color = GraphConstants.LineColor,
            MarkerType = GraphConstants.MarkerType,
            MarkerSize = GraphConstants.MarkerSize,
            MarkerStroke = GraphConstants.MarkerStrokeColor,
            MarkerFill = GraphConstants.MarkerFillColor
        };

    private void InitializeGraphComponents()
    {
        _plotModel.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Title = GraphConstants.TimeAxisTitle,
            StringFormat = GraphConstants.TimeAxisFormat
        });

        _plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = GraphConstants.ResponseAxisTitle,
            AbsoluteMinimum = 0
        });

        _plotModel.Series.Add(_lineSeries);
    }
}