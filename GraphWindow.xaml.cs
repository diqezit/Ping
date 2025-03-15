#nullable enable

namespace PingTestTool
{
    #region GraphConstants
    public static class GraphConstants
    {
        public const int DefaultMaxVisiblePoints = 100, MinPingIntervalMilliseconds = 100;
        public const string GraphTitle = "Ping Response Time Graph",
                            TimeAxisTitle = "Time",
                            TimeAxisFormat = "HH:mm:ss",
                            ResponseAxisTitle = "Response Time (ms)";
        public const OxyPlot.MarkerType MarkerType = OxyPlot.MarkerType.Circle;
        public const double MarkerSize = 3.0;

        public static readonly OxyColor GraphBackgroundColor = OxyColors.White,
                                        LineColor = OxyColor.FromRgb(0, 114, 189),
                                        MarkerStrokeColor = OxyColor.FromRgb(0, 114, 189),
                                        MarkerFillColor = OxyColors.White;
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
            _plotModel = plotModel ?? throw new ArgumentNullException(nameof(plotModel));
            _updateTextFields = updateTextFields ?? throw new ArgumentNullException(nameof(updateTextFields));
            _statisticsManager = statisticsManager ?? throw new ArgumentNullException(nameof(statisticsManager));

            _lineSeries = CreateLineSeries();
            InitializeGraphComponents();
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
        #endregion

        #region Public Methods
        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock) { _dataPoints.Clear(); }

            _statisticsManager.Dispose();
            _disposed = true;
        }

        public void SetData(IEnumerable<(DateTime Time, int Value)> data)
        {
            if (data is null) return;

            lock (_lock)
            {
                foreach (var (time, value) in data.Where(d => d.Value > 0))
                {
                    _dataPoints.Add(new PingData(time, value));
                    if (_dataPoints.Count > _maxVisiblePoints)
                        _dataPoints.RemoveAt(0);
                }
            }
        }

        public void UpdateGraph()
        {
            List<PingData> currentData;

            lock (_lock) { currentData = _dataPoints.ToList(); }

            if (currentData.Count == 0)
            {
                _lineSeries.Points.Clear();
                _plotModel.InvalidatePlot(true);
                return;
            }

            currentData.Sort((a, b) => a.Time.CompareTo(b.Time));

            var points = currentData
                .Select(dp => new DataPoint(DateTimeAxis.ToDouble(dp.Time), dp.Value))
                .OrderBy(p => p.X)
                .ToList();

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
        private bool _disposed;
        private readonly CancellationTokenSource _cts = new();
        #endregion

        #region Properties
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
                _statisticsManager.UpdateMaxVisiblePoints(value);
                _graphManager.UpdateGraph();
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

            PingPlotModel = CreatePlotModel();
            _statisticsManager = new StatisticsManager();
            _graphManager = new GraphManager(PingPlotModel, UpdateTextFields, _statisticsManager);

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(pingInterval, GraphConstants.MinPingIntervalMilliseconds))
            };

            InitializeEvents();
        }

        private static PlotModel CreatePlotModel() =>
            new()
            {
                Title = GraphConstants.GraphTitle,
                Background = GraphConstants.GraphBackgroundColor
            };

        private void InitializeEvents()
        {
            _updateTimer.Tick += UpdateTimer_Tick;
            StateChanged += GraphWindow_StateChanged;

            _updateTimer.Start();
            ApplyThemeToPlot();
        }
        #endregion

        #region Event Handlers
        private void UpdateTimer_Tick(object? sender, EventArgs e) =>
            _graphManager.UpdateGraph();

        private void GraphWindow_StateChanged(object? sender, EventArgs e) =>
            AdjustWindowCorners();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) =>
            Close();
        #endregion

        #region UI Helpers
        private void AdjustWindowCorners()
        {
            var isMaximized = WindowState == WindowState.Maximized;
            BorderThickness = new Thickness(isMaximized ? 0 : 1);

            if (Content is Border mainBorder)
            {
                mainBorder.CornerRadius = new CornerRadius(isMaximized ? 0 : 12);

                if (mainBorder.Child is Grid grid &&
                    grid.Children.Count > 0 &&
                    grid.Children[0] is Border titleBar)
                {
                    titleBar.CornerRadius = isMaximized
                        ? new CornerRadius(0)
                        : new CornerRadius(12, 12, 0, 0);
                }
            }
        }

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
                // Fallback to default theme if there's an error
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

        private bool TryDetectDarkTheme() =>
            Application.Current.Resources.Contains("WindowBackground") &&
            Application.Current.Resources["WindowBackground"] is System.Windows.Media.SolidColorBrush brush &&
            brush.Color.R < 128 && brush.Color.G < 128 && brush.Color.B < 128;

        private void UpdateTextFields(string min, string avg, string max, string cur) =>
            Dispatcher.Invoke(() =>
            {
                txtMin.Text = min;
                txtAvg.Text = avg;
                txtMax.Text = max;
                txtCur.Text = cur;
            });
        #endregion

        #region IGraphWindow Implementation
        public void SetPingData(List<(DateTime Time, int RoundtripTime)> data)
        {
            if (data?.Count > 0)
            {
                _statisticsManager.SetPingData(data);
                _graphManager.SetData(data);
                _graphManager.UpdateGraph();
            }
        }

        public new void Show() =>
            Dispatcher.Invoke(() => base.Show());

        public new void Close() =>
            Dispatcher.Invoke(() => base.Close());
        #endregion

        #region IDisposable Implementation
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimer_Tick;
            StateChanged -= GraphWindow_StateChanged;

            _graphManager.Dispose();
            _statisticsManager.Dispose();

            try { _cts.Cancel(); } catch { /* Ignore cancellation errors */ }
            _cts.Dispose();

            _disposed = true;
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

        public PingStatistics GetStatistics(IReadOnlyList<int> data) =>
            data.Count == 0
                ? new PingStatistics(0, 0, 0, 0)
                : new PingStatistics(data.Min(), data.Average(), data.Max(), data.Last());

        public void SetPingData(IEnumerable<(DateTime Time, int Value)> data)
        {
            if (data is null) return;

            foreach (var (_, value) in data.Where(d => d.Value > 0))
            {
                _pingData.Enqueue(value);
                if (_pingData.Count > _maxDataPoints)
                    _pingData.Dequeue();
            }
        }

        public void UpdateMaxVisiblePoints(int maxVisiblePoints)
        {
            _maxDataPoints = maxVisiblePoints;

            while (_pingData.Count > _maxDataPoints)
                _pingData.Dequeue();
        }
        #endregion
    }
    #endregion
}