#nullable enable

namespace PingTestTool
{
    #region GraphConstants

    public static class GraphConstants
    {
        public const int DefaultMaxVisiblePoints = 100;
        public const int MinPingIntervalMilliseconds = 100;
        public const string GraphTitle = "Ping Response Time Graph";
        public static readonly OxyColor GraphBackgroundColor = OxyColors.White;
        public const string TimeAxisTitle = "Time";
        public const string TimeAxisFormat = "HH:mm:ss";
        public const string ResponseAxisTitle = "Response Time (ms)";
        public static readonly OxyColor LineColor = OxyColor.FromRgb(0, 114, 189);
        public const OxyPlot.MarkerType MarkerType = OxyPlot.MarkerType.Circle;
        public const double MarkerSize = 3.0;
        public static readonly OxyColor MarkerStrokeColor = OxyColor.FromRgb(0, 114, 189);
        public static readonly OxyColor MarkerFillColor = OxyColors.White;
    }

    #endregion

    #region Interfaces

    public interface IGraphManager : IDisposable
    {
        void UpdateMaxVisiblePoints(int maxVisiblePoints);
        void UpdateGraph();
        void SetData(IEnumerable<(DateTime Time, int Value)> data);
    }

    public interface IGraphWindow
    {
        bool IsLoaded { get; }
        WindowState WindowState { get; set; }
        bool IsVisible { get; }
        Visibility Visibility { get; set; }
        void SetPingData(List<(DateTime Time, int RoundtripTime)> roundtripTimes);
        void Show();
        void Close();
        event EventHandler? Closed;
    }

    public interface IStatisticsManager : IDisposable
    {
        void UpdateMaxVisiblePoints(int maxVisiblePoints);
        void SetPingData(IEnumerable<(DateTime Time, int Value)> data);
        PingStatistics GetStatistics(IReadOnlyList<int> data);
    }

    #endregion

    #region Structs

    public readonly record struct PingStatistics(double Min, double Avg, double Max, double Cur);

    #endregion

    #region GraphManager Class

    public class GraphManager : IGraphManager
    {
        #region Fields

        private readonly PlotModel _plotModel;
        private readonly Action<string, string, string, string> _updateTextFields;
        private readonly IStatisticsManager _statisticsManager;
        private readonly List<PingData> _dataPoints = new();
        private readonly object _lock = new();
        private int _maxVisiblePoints = GraphConstants.DefaultMaxVisiblePoints;
        private bool _disposed;
        private readonly LineSeries _lineSeries;

        #endregion

        #region Nested Types

        private record PingData(DateTime Time, int Value);

        #endregion

        #region Constructor

        public GraphManager(
            PlotModel plotModel,
            Action<string, string, string, string> updateTextFields,
            IStatisticsManager statisticsManager)
        {
            _plotModel = plotModel;
            _updateTextFields = updateTextFields;
            _statisticsManager = statisticsManager;
            _lineSeries = new LineSeries
            {
                Title = "Ping",
                Color = GraphConstants.LineColor,
                MarkerType = GraphConstants.MarkerType,
                MarkerSize = GraphConstants.MarkerSize,
                MarkerStroke = GraphConstants.MarkerStrokeColor,
                MarkerFill = GraphConstants.MarkerFillColor
            };
            InitializeGraphComponents();
        }

        #endregion

        #region Public Methods

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                _dataPoints.Clear();
            }
            _statisticsManager.Dispose();
            _disposed = true;
        }

        public void SetData(IEnumerable<(DateTime Time, int Value)> data)
        {
            lock (_lock)
            {
                foreach (var (time, value) in data)
                {
                    if (value > 0)
                    {
                        _dataPoints.Add(new PingData(time, value));
                        if (_dataPoints.Count > _maxVisiblePoints)
                            _dataPoints.RemoveAt(0);
                    }
                }
            }
        }

        public void UpdateGraph()
        {
            List<PingData> currentData;
            lock (_lock)
            {
                currentData = _dataPoints.ToList();
            }
            if (currentData.Count == 0)
            {
                _lineSeries.Points.Clear();
                _plotModel.InvalidatePlot(true);
                return;
            }

            currentData.Sort((a, b) => a.Time.CompareTo(b.Time));

            var points = currentData
                .Select(dp => new DataPoint(DateTimeAxis.ToDouble(dp.Time), dp.Value))
                .ToList();
            points.Sort((a, b) => a.X.CompareTo(b.X));


            _lineSeries.Points.Clear();
            _lineSeries.Points.AddRange(points);

            var stats = _statisticsManager.GetStatistics(points.Select(p => (int)p.Y).ToList());
            _updateTextFields(
                $"{stats.Min:F1}",
                $"{stats.Avg:F1}",
                $"{stats.Max:F1}",
                $"{stats.Cur:F1}"
            );
            _plotModel.InvalidatePlot(true);
        }

        public void UpdateMaxVisiblePoints(int maxVisiblePoints)
        {
            _maxVisiblePoints = maxVisiblePoints;
            lock (_lock)
            {
                if (_dataPoints.Count > _maxVisiblePoints)
                    _dataPoints.RemoveRange(0, _dataPoints.Count - _maxVisiblePoints);
            }
        }

        #endregion

        #region Private Methods

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

        #endregion
    }

    #endregion

    #region GraphWindow Class

    public partial class GraphWindow : Window, INotifyPropertyChanged, IDisposable, IGraphWindow
    {
        #region Fields

        private readonly IGraphManager _graphManager;
        private readonly IStatisticsManager _statisticsManager;
        private readonly DispatcherTimer _updateTimer;
        private int _maxVisiblePoints = GraphConstants.DefaultMaxVisiblePoints;

        #endregion

        #region Properties

        public PlotModel PingPlotModel { get; }

        public int MaxVisiblePoints
        {
            get => _maxVisiblePoints;
            set
            {
                if (_maxVisiblePoints != value)
                {
                    _maxVisiblePoints = value;
                    OnPropertyChanged();
                    _graphManager.UpdateMaxVisiblePoints(value);
                    _statisticsManager.UpdateMaxVisiblePoints(value);
                    _graphManager.UpdateGraph();
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion

        #region Constructor

        public GraphWindow(int pingInterval)
        {
            InitializeComponent();
            DataContext = this;
            PingPlotModel = new PlotModel
            {
                Title = GraphConstants.GraphTitle,
                Background = GraphConstants.GraphBackgroundColor
            };

            _statisticsManager = new StatisticsManager();
            _graphManager = new GraphManager(PingPlotModel, UpdateTextFields, _statisticsManager);

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(pingInterval, GraphConstants.MinPingIntervalMilliseconds))
            };
            _updateTimer.Tick += (s, e) => _graphManager.UpdateGraph();
            _updateTimer.Start();
        }

        #endregion

        #region IGraphWindow Implementation

        public new void Close()
        {
            Dispatcher.Invoke(() => base.Close());
        }

        public void Dispose()
        {
            _updateTimer.Stop();
            _graphManager.Dispose();
            _statisticsManager.Dispose();
        }

        public void SetPingData(List<(DateTime Time, int RoundtripTime)> data)
        {
            if (data is null || data.Count == 0) return;
            _statisticsManager.SetPingData(data);
            _graphManager.SetData(data);
            _graphManager.UpdateGraph();
        }

        public new void Show()
        {
            Dispatcher.Invoke(() => base.Show());
        }

        #endregion

        #region Private Methods

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

        #endregion
    }

    #endregion

    #region StatisticsManager Class

    public class StatisticsManager : IStatisticsManager
    {
        #region Fields

        private readonly Queue<int> _pingData = new();
        private int _maxDataPoints = GraphConstants.DefaultMaxVisiblePoints;
        private bool _disposed;

        #endregion

        #region Public Methods

        public void Dispose()
        {
            if (_disposed) return;
            _pingData.Clear();
            _disposed = true;
        }

        public PingStatistics GetStatistics(IReadOnlyList<int> data)
        {
            if (data.Count == 0) return new PingStatistics(0, 0, 0, 0);
            return new PingStatistics(data.Min(), data.Average(), data.Max(), data.Last());
        }

        public void SetPingData(IEnumerable<(DateTime Time, int Value)> data)
        {
            foreach (var (_, value) in data)
            {
                if (value > 0)
                {
                    _pingData.Enqueue(value);
                    if (_pingData.Count > _maxDataPoints)
                        _pingData.Dequeue();
                }
            }
        }

        public void UpdateMaxVisiblePoints(int maxVisiblePoints)
        {
            _maxDataPoints = maxVisiblePoints;
            while (_pingData.Count > _maxDataPoints)
            {
                _pingData.Dequeue();
            }
        }

        #endregion
    }

    #endregion
}