using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Serilog;
using static PingTestTool.GraphTools;

#nullable enable

namespace PingTestTool
{
    #region GraphTools
    public static class GraphTools
    {
        public static class Defaults
        {
            public const int SMOOTHING_WINDOW = 5;
            public const int MAX_VISIBLE_POINTS = 500;
            public const int REFRESH_RATE = 1000;
            public const int AUTOSCROLL_THRESHOLD = 50;
            public const string GRAPH_TITLE = "График по времени отклика для Ping";
        }

        public static PingStatistics CalculateStatistics(IReadOnlyCollection<int> data)
        {
            if (data?.Any() != true) return PingStatistics.Empty;
            var validData = data.Where(x => x > 0).ToList();
            if (!validData.Any()) return PingStatistics.Empty;

            return new PingStatistics(
                validData.Min(),
                validData.Average(),
                validData.Max(),
                data.Last(),
                CalculateJitter(validData),
                CalculatePacketLoss(data),
                CalculateStandardDeviation(validData));
        }

        private static double CalculateJitter(IReadOnlyList<int> data) =>
            data.Count < 2 ? 0 : data.Zip(data.Skip(1), (a, b) => Math.Abs(a - b)).Average();

        private static double CalculatePacketLoss(IEnumerable<int> data)
        {
            var (total, lost) = data.Aggregate((0, 0), (acc, value) => value <= 0 ? (acc.Item1 + 1, acc.Item2 + 1) : (acc.Item1 + 1, acc.Item2));
            return total == 0 ? 0 : (double)lost / total * 100;
        }

        private static double CalculateStandardDeviation(IReadOnlyList<int> data)
        {
            if (data.Count < 2) return 0;
            var average = data.Average();
            return Math.Sqrt(data.Sum(value => Math.Pow(value - average, 2)) / (data.Count - 1));
        }

        public static IReadOnlyList<double> ApplyMovingAverage(IReadOnlyList<int> data, int windowSize)
        {
            if (data == null || data.Count == 0) return Array.Empty<double>();

            var result = new double[data.Count];
            double sum = 0;
            int count = 0;

            for (int i = 0; i < data.Count; i++)
            {
                sum += data[i];
                count++;
                if (i >= windowSize) sum -= data[i - windowSize];
                result[i] = sum / Math.Min(count, windowSize);
            }

            return result;
        }

        public static void UpdateThresholdSeries(AreaSeries thresholdSeries, double warningThreshold, double criticalThreshold, DateTime startTime, DateTime endTime)
        {
            var timeMin = DateTimeAxis.ToDouble(startTime);
            var timeMax = DateTimeAxis.ToDouble(endTime);

            thresholdSeries.Points.Clear();
            thresholdSeries.Points2.Clear();

            thresholdSeries.Points.AddRange(new[] { new DataPoint(timeMin, warningThreshold), new DataPoint(timeMax, warningThreshold) });
            thresholdSeries.Points2.AddRange(new[] { new DataPoint(timeMin, criticalThreshold), new DataPoint(timeMax, criticalThreshold) });
        }

        public static void CleanupOldData<T>(Queue<T> dataQueue, DateTime cutoffTime) where T : ITimestampedData
        {
            while (dataQueue.Count > 0 && dataQueue.Peek().Timestamp < cutoffTime) dataQueue.Dequeue();
        }

        public static void ValidateGraphParameters(int refreshRate, int smoothingWindow)
        {
            if (refreshRate < 100) throw new ArgumentException("Частота обновления не может быть меньше 100 мс");
            if (smoothingWindow < 2) throw new ArgumentException("Сглаживающее окно должно быть не менее 2");
        }
    }
    #endregion

    #region Interfaces and Records
    public interface ITimestampedData
    {
        DateTime Timestamp { get; }
    }

    public record PingStatistics(double Min, double Avg, double Max, double Cur, double Jitter = 0, double PacketLoss = 0, double StandardDeviation = 0)
    {
        public static PingStatistics Empty { get; } = new PingStatistics(0, 0, 0, 0);
        public override string ToString() => $"Min: {Min:F2}мс, Avg: {Avg:F2}мс, Max: {Max:F2}мс, Current: {Cur:F2}мс, Jitter: {Jitter:F2}мс, Packet Loss: {PacketLoss:F2}%, StdDev: {StandardDeviation:F2}мс";
    }

    public readonly record struct TimestampedData(DateTime Timestamp, int Value) : ITimestampedData;
    #endregion

    #region GraphSettings
    public static class GraphSettings
    {
        private static readonly OxyColor WarningZoneColor = OxyColor.FromAColor(80, OxyColors.Orange);
        private static readonly OxyColor CriticalZoneColor = OxyColor.FromAColor(60, OxyColors.Red);

        public static (DateTimeAxis TimeAxis, LinearAxis PingAxis) InitializeAxes() =>
            (CreateTimeAxis(), CreatePingAxis());

        public static (LineSeries Normal, LineSeries Error, AreaSeries Threshold) InitializeSeries() =>
            (CreateNormalSeries(), CreateErrorSeries(), CreateThresholdSeries());

        public static DispatcherTimer InitializeTimer(int pingInterval, EventHandler tickHandler)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(pingInterval, Defaults.REFRESH_RATE)) };
            timer.Tick += (s, e) => SafeInvoke(tickHandler, s, e);
            timer.Start();
            return timer;
        }

        private static DateTimeAxis CreateTimeAxis() =>
            new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Время",
                FontSize = 12,
                StringFormat = "HH:mm:ss",
                IntervalType = DateTimeIntervalType.Seconds,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MinimumRange = TimeSpan.FromSeconds(10).TotalDays,
                MajorGridlineColor = OxyColor.FromArgb(40, 128, 128, 128),
                MinorGridlineColor = OxyColor.FromArgb(20, 128, 128, 128),
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.DarkGray
            };

        private static LinearAxis CreatePingAxis() =>
            new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Время отклика (мс)",
                FontSize = 12,
                MinimumRange = 10,
                MaximumPadding = 0.1,
                MinimumPadding = 0.1,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 128, 128, 128),
                MinorGridlineColor = OxyColor.FromArgb(20, 128, 128, 128),
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.DarkGray,
                AbsoluteMinimum = 0,
                StartPosition = 0,
                EndPosition = 1
            };

        private static LineSeries CreateNormalSeries() =>
            new LineSeries
            {
                Title = "Ping",
                Color = OxyColor.FromRgb(0, 114, 189),
                StrokeThickness = 1.5,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerStroke = OxyColor.FromRgb(0, 114, 189),
                MarkerFill = OxyColors.White,
                InterpolationAlgorithm = InterpolationAlgorithms.CanonicalSpline,
                MinimumSegmentLength = 2
            };

        private static LineSeries CreateErrorSeries() =>
            new LineSeries
            {
                Title = "Ошибка",
                Color = OxyColor.FromRgb(217, 83, 79),
                StrokeThickness = 2,
                MarkerType = MarkerType.Diamond,
                MarkerSize = 5,
                MarkerStroke = OxyColor.FromRgb(217, 83, 79),
                MarkerFill = OxyColor.FromRgb(217, 83, 79),
                RenderInLegend = true
            };

        private static AreaSeries CreateThresholdSeries() =>
            new AreaSeries
            {
                Color = OxyColors.Transparent,
                Fill = WarningZoneColor,
                Title = "Пороговые значения",
                RenderInLegend = true,
                StrokeThickness = 1,
                Points2 = { new DataPoint(0, 0), new DataPoint(1, 0), new DataPoint(1, 1), new DataPoint(0, 1) }
            };

        private static void SafeInvoke(EventHandler handler, object sender, EventArgs e)
        {
            try { handler?.Invoke(sender, e); }
            catch (Exception ex) { Log.Error($"Ошибка тика таймера: {ex.Message}"); }
        }
    }
    #endregion

    #region GraphDataManager
    public class GraphDataManager : IDisposable
    {
        private readonly List<int> _pingData = new();
        private CacheData _cache = new(Array.Empty<double>(), false);
        private int _smoothingWindow = Defaults.SMOOTHING_WINDOW;
        private readonly object _lockObject = new();
        private bool _disposed;

        public int SmoothingWindow
        {
            get => _smoothingWindow;
            set
            {
                GraphTools.ValidateGraphParameters(Defaults.REFRESH_RATE, value);
                _smoothingWindow = value;
                InvalidateCache();
            }
        }

        public TimeSpan CacheTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public GraphDataManager() => SmoothingWindow = Defaults.SMOOTHING_WINDOW;

        public void SetPingData(IEnumerable<int> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            lock (_lockObject)
            {
                _pingData.Clear();
                _pingData.AddRange(data);
                InvalidateCache();
            }
        }

        public void AddPingData(int value)
        {
            lock (_lockObject)
            {
                _pingData.Add(value);
                InvalidateCache();
            }
        }

        public IReadOnlyList<double> GetDataToPlot(bool isSmoothingEnabled)
        {
            lock (_lockObject)
            {
                if (isSmoothingEnabled)
                {
                    if (_cache.IsValid && DateTime.Now - _cache.Timestamp <= CacheTimeout) return _cache.SmoothedData;
                    var smoothedData = GraphTools.ApplyMovingAverage(_pingData, _smoothingWindow).ToArray();
                    _cache = new CacheData(smoothedData, true);
                    return smoothedData;
                }
                return _pingData.ConvertAll(x => (double)x);
            }
        }

        public PingStatistics GetStatistics()
        {
            lock (_lockObject) return GraphTools.CalculateStatistics(_pingData);
        }

        public (IReadOnlyList<double> Data, IReadOnlyList<DateTime> Timestamps) GetTimeSeriesData(bool isSmoothingEnabled, DateTime startTime, TimeSpan interval)
        {
            var data = GetDataToPlot(isSmoothingEnabled);
            var timestamps = data.Select((_, i) => startTime.Add(TimeSpan.FromTicks(interval.Ticks * i))).ToArray();
            return (data, timestamps);
        }

        private void InvalidateCache() => _cache = new CacheData(Array.Empty<double>(), false);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _pingData.Clear();
                    _cache = new CacheData(Array.Empty<double>(), false);
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private readonly record struct CacheData(IReadOnlyList<double> SmoothedData, bool IsValid)
        {
            public DateTime Timestamp { get; } = DateTime.Now;
        }
    }
    #endregion
}