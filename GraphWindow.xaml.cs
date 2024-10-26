using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PingTestTool
{
    /// <summary>
    /// Окно для отображения графика времени отклика для Ping.
    /// </summary>
    public partial class GraphWindow : Window
    {
        #region Константы

        private const int DEFAULT_MAX_VISIBLE_POINTS = 500;
        private const string GRAPH_TITLE = "График по времени отклика для Ping";

        #endregion

        #region Приватные поля

        private readonly GraphDataManager _dataManager;
        private readonly int _maxVisiblePoints;
        private bool _isSmoothingEnabled;

        private DispatcherTimer _updateTimer;
        private readonly LinearAxis _pingAxis;
        private readonly LinearAxis _timeAxis;
        private readonly LineSeries _errorSeries;
        private readonly LineSeries _normalSeries;
        private ILogger _logger;

        #endregion

        #region Публичные свойства

        public PlotModel PingPlotModel { get; }

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="GraphWindow"/>.
        /// </summary>
        /// <param name="pingInterval">Интервал пинга в миллисекундах.</param>
        public GraphWindow(int pingInterval)
        {
            try
            {
                InitializeComponent();

                _maxVisiblePoints = DEFAULT_MAX_VISIBLE_POINTS;
                _dataManager = new GraphDataManager();

                PingPlotModel = new PlotModel { Title = GRAPH_TITLE };
                DataContext = this;

                (_timeAxis, _pingAxis) = InitializeAxes();
                (_normalSeries, _errorSeries) = InitializeSeries();
                _updateTimer = InitializeTimer(pingInterval);

                ConfigurePlotModel();

                // Инициализируем Logger
                _logger = new Logger("graph_window_log.txt", true);
            }
            catch (Exception ex)
            {
                HandleInitializationErrorAsync(ex).GetAwaiter().GetResult();
                throw;
            }
        }

        #endregion

        #region Инициализация компонентов

        /// <summary>
        /// Инициализирует оси графика.
        /// </summary>
        /// <returns>Кортеж с осями времени и пинга.</returns>
        private (LinearAxis TimeAxis, LinearAxis PingAxis) InitializeAxes()
        {
            var timeAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Пакет",
                FontSize = 12,
                Font = "Segoe UI",
                Minimum = 0,
                MaximumPadding = 0,
                MinimumPadding = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                IsPanEnabled = true,
                IsZoomEnabled = true,
                MinimumRange = 1,
                TextColor = OxyColors.Black,
                AxislineColor = OxyColors.LightGray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkSlateGray
            };

            var pingAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Время отклика (мс)",
                FontSize = 12,
                Font = "Segoe UI",
                MinimumRange = 10,
                MaximumPadding = 0,
                MinimumPadding = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                IsPanEnabled = true,
                IsZoomEnabled = true,
                TextColor = OxyColors.Black,
                AxislineColor = OxyColors.LightGray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkSlateGray
            };

            return (timeAxis, pingAxis);
        }

        /// <summary>
        /// Инициализирует серии данных для графика.
        /// </summary>
        /// <returns>Кортеж с сериями нормальных и ошибочных данных.</returns>
        private (LineSeries NormalSeries, LineSeries ErrorSeries) InitializeSeries() =>
            (
                new LineSeries { Title = "Ping", MarkerType = MarkerType.None, Color = OxyColors.Blue },
                new LineSeries { Title = "Error", MarkerType = MarkerType.None, Color = OxyColors.Red }
            );

        /// <summary>
        /// Инициализирует таймер обновления графика.
        /// </summary>
        /// <param name="pingInterval">Интервал пинга в миллисекундах.</param>
        /// <returns>Инициализированный таймер.</returns>
        private DispatcherTimer InitializeTimer(int pingInterval)
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(pingInterval)
            };
            timer.Tick += UpdateGraph;
            return timer;
        }

        /// <summary>
        /// Настраивает модель графика.
        /// </summary>
        private void ConfigurePlotModel()
        {
            PingPlotModel.Axes.Add(_timeAxis);
            PingPlotModel.Axes.Add(_pingAxis);
            PingPlotModel.Series.Add(_normalSeries);
            PingPlotModel.Series.Add(_errorSeries);
        }

        #endregion

        #region Обновление графика

        /// <summary>
        /// Обновляет график на основе новых данных.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private async void UpdateGraph(object? sender, EventArgs e)
        {
            try
            {
                var dataToPlot = _dataManager.GetDataToPlot(_isSmoothingEnabled);
                UpdateSeriesData(dataToPlot);
                UpdateAxesRanges(dataToPlot);
                UpdateStatistics();
                PingPlotModel.InvalidatePlot(true);
            }
            catch (Exception ex)
            {
                await HandleGraphUpdateErrorAsync(ex).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Обновляет данные серий на графике.
        /// </summary>
        /// <param name="data">Данные для обновления.</param>
        private void UpdateSeriesData(IReadOnlyList<double> data)
        {
            _normalSeries.Points.Clear();
            _errorSeries.Points.Clear();
            PingPlotModel.Annotations.Clear();

            var startIndex = Math.Max(0, data.Count - _maxVisiblePoints);
            for (var i = startIndex; i < data.Count; i++)
            {
                var point = new DataPoint(i, data[i]);
                if (data[i] <= 0)
                {
                    AddErrorPoint(point);
                }
                else
                {
                    _normalSeries.Points.Add(point);
                }
            }
        }

        /// <summary>
        /// Добавляет точку ошибки на график.
        /// </summary>
        /// <param name="point">Точка данных.</param>
        private void AddErrorPoint(DataPoint point)
        {
            _errorSeries.Points.Add(point);
            PingPlotModel.Annotations.Add(new TextAnnotation
            {
                Text = "Error",
                TextPosition = point,
                TextColor = OxyColors.Red,
                FontSize = 10,
                FontWeight = OxyPlot.FontWeights.Bold
            });
        }

        /// <summary>
        /// Обновляет диапазоны осей на графике.
        /// </summary>
        /// <param name="data">Данные для обновления.</param>
        private void UpdateAxesRanges(IReadOnlyList<double> data)
        {
            if (data.Count == 0)
            {
                SetDefaultAxesRanges();
                return;
            }

            var startIndex = Math.Max(0, data.Count - _maxVisiblePoints);
            var visibleData = data.Skip(startIndex).ToList();

            _timeAxis.Minimum = startIndex;
            _timeAxis.Maximum = data.Count - 1;
            _pingAxis.Minimum = visibleData.Min();
            _pingAxis.Maximum = visibleData.Max() + 10;
        }

        /// <summary>
        /// Устанавливает диапазоны осей по умолчанию.
        /// </summary>
        private void SetDefaultAxesRanges()
        {
            _timeAxis.Minimum = 0;
            _timeAxis.Maximum = 100;
            _pingAxis.Minimum = 0;
            _pingAxis.Maximum = 100;
        }

        #endregion

        #region Обработка статистики

        /// <summary>
        /// Обновляет статистику на графике.
        /// </summary>
        private void UpdateStatistics()
        {
            var stats = _dataManager.GetStatistics();

            SetStatisticsText(txtMin, stats.Min);
            SetStatisticsText(txtAvg, stats.Avg);
            SetStatisticsText(txtMax, stats.Max);
            SetStatisticsText(txtCur, stats.Cur);
        }

        /// <summary>
        /// Устанавливает текст статистики в указанный TextBlock.
        /// </summary>
        /// <param name="textBlock">TextBlock для отображения статистики.</param>
        /// <param name="value">Значение статистики.</param>
        private static void SetStatisticsText(TextBlock textBlock, double value) =>
            textBlock.Text = double.IsNaN(value) ? "-" : value.ToString();

        #endregion

        #region Обработка ошибок

        /// <summary>
        /// Обрабатывает ошибки инициализации.
        /// </summary>
        /// <param name="ex">Исключение, возникшее при инициализации.</param>
        private async Task HandleInitializationErrorAsync(Exception ex)
        {
            await _logger.LogAsync(LogLevel.ERROR, $"Ошибка инициализации графика: {ex.Message}").ConfigureAwait(false);
            MessageBox.Show(
                $"Ошибка инициализации графика: {ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// Обрабатывает ошибки обновления графика.
        /// </summary>
        /// <param name="ex">Исключение, возникшее при обновлении.</param>
        private async Task HandleGraphUpdateErrorAsync(Exception ex)
        {
            await _logger.LogAsync(LogLevel.ERROR, $"Произошла ошибка при обновлении графика. Проверьте данные и повторите попытку. {ex.Message}").ConfigureAwait(false);
            MessageBox.Show(
                "Произошла ошибка при обновлении графика. Проверьте данные и повторите попытку.",
                "Ошибка обновления",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        #endregion

        #region Обработчики событий

        /// <summary>
        /// Переключает режим сглаживания данных на графике.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void ToggleSmoothing(object sender, RoutedEventArgs e)
        {
            _isSmoothingEnabled = !_isSmoothingEnabled;
            UpdateGraph(null, EventArgs.Empty);
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Устанавливает данные пинга для отображения на графике.
        /// </summary>
        /// <param name="pingData">Список данных пинга.</param>
        public void SetPingData(List<int> pingData)
        {
            try
            {
                _dataManager.SetPingData(pingData);
                UpdateGraph(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                HandleGraphUpdateErrorAsync(ex).GetAwaiter().GetResult();
            }
        }

        #endregion
    }
}