#nullable enable

namespace PingTestTool
{
    public static class Constants
    {
        public const int MaxVisiblePoints = 100;
        public const int MinPingInterval = 100;
    }

    public interface IGraphWindow
    {
        bool IsLoaded { get; }
        WindowState WindowState { get; set; }
        bool IsVisible { get; }
        Visibility Visibility { get; set; }
        void SetPingData(List<int> roundtripTimes);
        void Show();
        void Close();
        event EventHandler Closed;
    }

    public partial class GraphWindow : Window, INotifyPropertyChanged, IDisposable, IGraphWindow
    {
        private readonly ILoggingService _logger;
        private readonly GraphManager _graphManager;
        private readonly StatisticsManager _statisticsManager;
        private readonly DispatcherTimer _updateTimer;
        private int _maxVisiblePoints = Constants.MaxVisiblePoints;

        public PlotModel PingPlotModel { get; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public int MaxVisiblePoints
        {
            get => _maxVisiblePoints;
            set
            {
                if (_maxVisiblePoints != value)
                {
                    _maxVisiblePoints = value;
                    OnPropertyChanged(nameof(MaxVisiblePoints));
                    _graphManager.UpdateMaxVisiblePoints(value);
                    _statisticsManager.UpdateMaxVisiblePoints(value);
                    _graphManager.UpdateGraph(value);
                }
            }
        }

        public GraphWindow(int pingInterval, ILoggingService logger)
        {
            InitializeComponent();
            DataContext = this;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            PingPlotModel = new PlotModel
            {
                Title = "График по времени отклика для Ping",
                Background = OxyColors.White
            };

            _graphManager = new GraphManager(logger, PingPlotModel, UpdateTextFields);
            _statisticsManager = new StatisticsManager();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(pingInterval, Constants.MinPingInterval))
            };

            _updateTimer.Tick += (_, _) => Application.Current.Dispatcher.Invoke(() => _graphManager.UpdateGraph(_maxVisiblePoints));
            _updateTimer.Start();
        }

        public void SetPingData(List<int>? data)
        {
            if (data is null || !data.Any())
            {
                Log.Warning("[GraphWindow] Пустые или нулевые данные получены для пинга.");
                return;
            }

            _statisticsManager.SetPingData(data);
            _graphManager.SetData(data);
            _graphManager.UpdateGraph(MaxVisiblePoints);
        }

        private void UpdateTextFields(string min, string avg, string max, string cur)
        {
            Dispatcher.Invoke(() =>
            {
                txtMin.Text = min;
                txtAvg.Text = avg;
                txtMax.Text = max;
                txtCur.Text = cur;
            });
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _updateTimer.Stop();
            _graphManager.Dispose();
            _statisticsManager.Dispose();
        }
    }

    public class GraphManager : IDisposable
    {
        private readonly PlotModel _plotModel;
        private readonly Action<string, string, string, string> _updateTextFields;
        private readonly ConcurrentDictionary<DateTime, double> _realtimeData = new();
        private readonly StatisticsManager _statisticsManager;
        private readonly LineSeries _normalSeries;
        private readonly ILoggingService _logger;
        private readonly object _lock = new();
        private bool _disposed;
        private int _maxVisiblePoints = Constants.MaxVisiblePoints;

        public GraphManager(ILoggingService logger, PlotModel plotModel, Action<string, string, string, string> updateTextFields)
        {
            _plotModel = plotModel;
            _updateTextFields = updateTextFields;
            _statisticsManager = new StatisticsManager();
            _logger = logger ?? new SerilogLoggingService();

            _normalSeries = new LineSeries
            {
                Title = "Ping",
                Color = OxyColor.FromRgb(0, 114, 189),
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerStroke = OxyColor.FromRgb(0, 114, 189),
                MarkerFill = OxyColors.White,
            };

            InitializeGraphComponents();
        }

        private void InitializeGraphComponents()
        {
            _plotModel.Axes.Add(new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Время",
                StringFormat = "HH:mm:ss",
            });

            _plotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Время отклика (мс)",
                AbsoluteMinimum = 0,
            });

            _plotModel.Series.Add(_normalSeries);
        }

        public void UpdateMaxVisiblePoints(int maxVisiblePoints)
        {
            _maxVisiblePoints = maxVisiblePoints;
            TrimDataToMaxVisiblePoints();
        }

        private void TrimDataToMaxVisiblePoints()
        {
            lock (_lock)
            {
                while (_realtimeData.Count > _maxVisiblePoints)
                {
                    var oldestKey = _realtimeData.Keys.Min();
                    _realtimeData.TryRemove(oldestKey, out _);
                }
            }
        }

        public void UpdateGraph(int maxVisiblePoints)
        {
            if (_realtimeData.IsEmpty)
            {
                Log.Warning("No data to update graph.");
                return;
            }

            var sortedData = FilterAndSortData(maxVisiblePoints);

            _normalSeries.Points.Clear();
            _normalSeries.Points.AddRange(sortedData);

            var dataValues = _realtimeData.Values.Select(d => (int)d).ToList();
            var stats = _statisticsManager.GetStatistics(dataValues);
            _logger.Information($"Min: {stats.Min}, Avg: {stats.Avg}, Max: {stats.Max}, Cur: {stats.Cur}");
            _updateTextFields(
                $"{stats.Min:F1}",
                $"{stats.Avg:F1}",
                $"{stats.Max:F1}",
                $"{stats.Cur:F1}"
            );

            _plotModel.InvalidatePlot(true);
        }

        private List<DataPoint> FilterAndSortData(int maxVisiblePoints)
        {
            List<DataPoint> sortedData;

            lock (_lock)
            {
                sortedData = _realtimeData
                    .OrderBy(kvp => kvp.Key)
                    .Skip(Math.Max(0, _realtimeData.Count - maxVisiblePoints))
                    .Select(kvp => new DataPoint(DateTimeAxis.ToDouble(kvp.Key), kvp.Value))
                    .Where(dp => dp.Y > 0)
                    .ToList();
            }

            return sortedData;
        }

        public void SetData(IEnumerable<int> data)
        {
            var timestamp = DateTime.Now;

            lock (_lock)
            {
                foreach (var value in data.Where(x => x > 0))
                {
                    _realtimeData[timestamp] = value;
                    TrimDataToMaxVisiblePoints();
                }
            }

            Log.Information("[GraphWindow] Graph data updated. Current data count: {Count}", _realtimeData.Count);
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                _realtimeData.Clear();
            }

            _statisticsManager.Dispose();
            _disposed = true;
        }
    }

    public class StatisticsManager : IDisposable
    {
        private ConcurrentQueue<int> _pingData = new();
        private int _maxDataPoints = Constants.MaxVisiblePoints;
        private bool _disposed;

        public void UpdateMaxVisiblePoints(int maxVisiblePoints)
        {
            _maxDataPoints = maxVisiblePoints;
            TrimDataToMaxVisiblePoints();
        }

        private void TrimDataToMaxVisiblePoints()
        {
            while (_pingData.Count > _maxDataPoints)
            {
                _pingData.TryDequeue(out _);
            }
        }

        public void SetPingData(IEnumerable<int> data)
        {
            foreach (var value in data.Where(x => x > 0))
            {
                _pingData.Enqueue(value);
                TrimDataToMaxVisiblePoints();
            }
        }

        public PingStatistics GetStatistics(List<int> data)
        {
            if (data == null || data.Count == 0)
                return new PingStatistics(0, 0, 0, 0);

            return new PingStatistics(
                data.Min(),
                data.Average(),
                data.Max(),
                data.Last()
            );
        }

        public void Dispose()
        {
            if (_disposed) return;

            while (_pingData.TryDequeue(out _)) { }
            _disposed = true;
        }
    }

    public readonly record struct PingStatistics(double Min, double Avg, double Max, double Cur);
}
