using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PingTestTool
{
    public partial class GraphWindow : Window, IDisposable, INotifyPropertyChanged
    {
        private readonly GraphDataManager _dataManager = new();
        private readonly DispatcherTimer _updateTimer;
        private readonly LinearAxis _pingAxis;
        private readonly DateTimeAxis _timeAxis;
        private readonly LineSeries _errorSeries;
        private readonly LineSeries _normalSeries;
        private readonly AreaSeries _thresholdSeries;
        private readonly Queue<TimestampedData> _realtimeData = new();

        private int _maxVisiblePoints = GraphTools.Defaults.MAX_VISIBLE_POINTS;
        private bool _isSmoothingEnabled;
        private bool _isAutoScrollEnabled = true;
        private bool _isPaused;
        private bool _isLogarithmicScale;
        private double _zoomLevel = 1.0;
        private double _warningThreshold = 100;
        private double _criticalThreshold = 200;

        public PlotModel PingPlotModel { get; } = new()
        {
            Title = GraphTools.Defaults.GRAPH_TITLE,
            Background = OxyColors.White
        };

        public bool IsAutoScrollEnabled
        {
            get => _isAutoScrollEnabled;
            set => SetProperty(ref _isAutoScrollEnabled, value);
        }

        public bool IsSmoothingEnabled
        {
            get => _isSmoothingEnabled;
            set => SetProperty(ref _isSmoothingEnabled, value);
        }

        public double WarningThreshold
        {
            get => _warningThreshold;
            set => SetProperty(ref _warningThreshold, value, UpdateThresholdLines);
        }

        public double CriticalThreshold
        {
            get => _criticalThreshold;
            set => SetProperty(ref _criticalThreshold, value, UpdateThresholdLines);
        }

        public TimeSpan DataRetentionPeriod { get; set; } = TimeSpan.FromHours(1);

        public List<int> MaxVisiblePointsOptions { get; } = new() { 100, 200, 500, 1000 };

        public double ZoomLevel
        {
            get => _zoomLevel;
            set => SetProperty(ref _zoomLevel, value, () => UpdateGraph(null, EventArgs.Empty));
        }

        public int MaxVisiblePoints
        {
            get => _maxVisiblePoints;
            set => SetProperty(ref _maxVisiblePoints, value, () => UpdateGraph(null, EventArgs.Empty));
        }

        public GraphWindow(int pingInterval)
        {
            InitializeComponent();
            DataContext = this;

            (_timeAxis, _pingAxis) = GraphSettings.InitializeAxes();
            (_normalSeries, _errorSeries, _thresholdSeries) = GraphSettings.InitializeSeries();
            _updateTimer = GraphSettings.InitializeTimer(pingInterval, UpdateGraph);
            InitializePlotModel();
        }

        private void InitializePlotModel()
        {
            PingPlotModel.Axes.Add(_timeAxis);
            PingPlotModel.Axes.Add(_pingAxis);
            PingPlotModel.Series.Add(_thresholdSeries);
            PingPlotModel.Series.Add(_normalSeries);
            PingPlotModel.Series.Add(_errorSeries);
            PingPlotModel.Legends.Add(new OxyPlot.Legends.Legend
            {
                LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
                LegendPlacement = OxyPlot.Legends.LegendPlacement.Inside
            });

            var plotView = this.plotView;
            if (plotView == null)
            {
                Log.Error("plotView is null during SetupEventHandlers.");
                return;
            }

            var controller = new PlotController();
            controller.BindMouseDown(OxyMouseButton.Right, new DelegatePlotCommand<OxyMouseDownEventArgs>((model, view, args) =>
            {
                if (args.ChangedButton == OxyMouseButton.Right)
                {
                    args.Handled = true;
                    ShowContextMenu(args);
                }
            }));
            plotView.Controller = controller;
        }

        private async void UpdateGraph(object? sender, EventArgs e)
        {
            if (_isPaused) return;
            try
            {
                var now = DateTime.Now;
                CleanupOldData(now);
                var dataToPlot = await Task.Run(() => _dataManager.GetDataToPlot(_isSmoothingEnabled));
                UpdateSeriesData(dataToPlot, now);
                UpdateAxesRanges(now);
                UpdatePingStatistics();
                UpdateThresholdLines();
                if (_isAutoScrollEnabled) AutoScrollGraph();
                PingPlotModel.InvalidatePlot(true);
            }
            catch (Exception ex) { Log.Error(ex, "Ошибка обновления графика"); }
        }

        private void CleanupOldData(DateTime now) => GraphTools.CleanupOldData(_realtimeData, now - DataRetentionPeriod);

        private void UpdatePingStatistics()
        {
            var stats = _dataManager.GetStatistics();
            Dispatcher.Invoke(() =>
            {
                txtMin.Text = $"{stats.Min:F1}";
                txtAvg.Text = $"{stats.Avg:F1}";
                txtMax.Text = $"{stats.Max:F1}";
                txtCur.Text = $"{stats.Cur:F1}";
            });
        }

        private void UpdateSeriesData(IReadOnlyList<double> data, DateTime now)
        {
            _normalSeries.Points.Clear();
            _errorSeries.Points.Clear();
            PingPlotModel.Annotations.Clear();

            int startIndex = Math.Max(0, _realtimeData.Count - MaxVisiblePoints);
            var visibleData = _realtimeData.Skip(startIndex).ToList();

            foreach (var point in visibleData)
            {
                var oxyTime = DateTimeAxis.ToDouble(point.Timestamp);
                if (point.Value <= 0) AddErrorPoint(new DataPoint(oxyTime, point.Value));
                else _normalSeries.Points.Add(new DataPoint(oxyTime, point.Value));
            }
        }

        private void AddErrorPoint(DataPoint point)
        {
            _errorSeries.Points.Add(point);
            PingPlotModel.Annotations.Add(new TextAnnotation
            {
                Text = "Ошибка",
                TextPosition = point,
                TextColor = OxyColors.Red,
                FontSize = 10,
                FontWeight = OxyPlot.FontWeights.Bold
            });
        }

        private void UpdateAxesRanges(DateTime now)
        {
            if (_realtimeData.Count == 0) return;
            var visibleDuration = TimeSpan.FromSeconds(30 * ZoomLevel);
            _timeAxis.Minimum = DateTimeAxis.ToDouble(now - visibleDuration);
            _timeAxis.Maximum = DateTimeAxis.ToDouble(now);
            _pingAxis.StringFormat = _isLogarithmicScale ? "0.##E0" : "0.##";
            _pingAxis.UseSuperExponentialFormat = _isLogarithmicScale;
        }

        private void UpdateThresholdLines() => GraphTools.UpdateThresholdSeries(_thresholdSeries, _warningThreshold, _criticalThreshold, DateTime.Now - DataRetentionPeriod, DateTime.Now);

        private void AutoScrollGraph()
        {
            if (_realtimeData.Count > GraphTools.Defaults.AUTOSCROLL_THRESHOLD)
            {
                var latest = _realtimeData.Last().Timestamp;
                _timeAxis.Minimum = DateTimeAxis.ToDouble(latest.AddSeconds(-30));
                _timeAxis.Maximum = DateTimeAxis.ToDouble(latest.AddSeconds(1));
            }
        }

        public void SetPingData(List<int> pingData)
        {
            try
            {
                if (pingData == null)
                {
                    Log.Warning("Received null pingData in SetPingData.");
                    return;
                }

                foreach (var value in pingData) _realtimeData.Enqueue(new TimestampedData(DateTime.Now, value));
                _dataManager.SetPingData(pingData);
                UpdateGraph(null, EventArgs.Empty);
            }
            catch (Exception ex) { Log.Error(ex, "Ошибка инициализации"); }
        }

        public void TogglePause()
        {
            _isPaused = !_isPaused;
            Log.Information("TogglePause called. IsPaused: {IsPaused}", _isPaused);
        }

        public void SetRefreshRate(int milliseconds)
        {
            if (milliseconds < 100)
            {
                Log.Error("Частота обновления не может быть меньше 100 мс. Получено: {Milliseconds}", milliseconds);
                throw new ArgumentException("Частота обновления не может быть меньше 100 мс", nameof(milliseconds));
            }

            _updateTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            Log.Information("Refresh rate set to {Milliseconds} ms", milliseconds);
        }

        private void ShowContextMenu(OxyMouseDownEventArgs e)
        {
            if (e == null)
            {
                Log.Warning("ShowContextMenu called with null OxyMouseDownEventArgs.");
                return;
            }

            var menu = new ContextMenu();
            var logScaleItem = new MenuItem
            {
                Header = "Логарифмическая шкала",
                IsCheckable = true,
                IsChecked = _isLogarithmicScale
            };

            logScaleItem.Click += (s, args) =>
            {
                _isLogarithmicScale = !_isLogarithmicScale;
                Log.Information("Logarithmic scale toggled. IsLogarithmicScale: {IsLogarithmicScale}", _isLogarithmicScale);
                UpdateGraph(null, EventArgs.Empty);
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(logScaleItem);
            menu.IsOpen = true;
        }

        public string PauseButtonContent => _isPaused ? "Продолжить" : "Пауза";

        private void OnTogglePauseClick(object sender, RoutedEventArgs e)
        {
            TogglePause();
            OnPropertyChanged(nameof(PauseButtonContent));
            Log.Information("Pause button clicked. New state: {PauseButtonContent}", PauseButtonContent);
        }

        public void Dispose()
        {
            try
            {
                _updateTimer?.Stop();
                _dataManager?.Dispose();
                Log.Information("GraphWindow resources have been disposed.");
            }
            catch (Exception ex) { Log.Error(ex, "Error during disposing of GraphWindow resources."); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Log.Debug("Property changed: {PropertyName}", propertyName);
        }

        private void SetProperty<T>(ref T field, T value, Action? onChanged = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                onChanged?.Invoke();
                OnPropertyChanged(nameof(field));
            }
        }
    }
}