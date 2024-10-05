using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PingTestTool
{
    public partial class GraphWindow : Window
    {
        private GraphDataManager dataManager;
        private int maxVisiblePoints = 500;
        private bool isSmoothingEnabled = false;

        private DispatcherTimer updateTimer;
        private LinearAxis pingAxis;
        private LinearAxis timeAxis;
        private LineSeries errorSeries;
        private LineSeries normalSeries;
        public PlotModel PingPlotModel { get; private set; }

        public GraphWindow(int pingInterval)
        {
            InitializeComponent();

            PingPlotModel = new PlotModel { Title = "График по времени отклика для Ping" };
            DataContext = this;

            dataManager = new GraphDataManager();

            SetupAxes(); // Настройка осей
            SetupSeries();

            // Настройка таймера
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(pingInterval)
            };
            updateTimer.Tick += UpdateGraph;
            updateTimer.Start();
        }

        private void SetupAxes()
        {
            timeAxis = new LinearAxis // Настройка оси времени
            {
                FontSize = 12,
                Font = "Segoe UI",
                Position = AxisPosition.Bottom,
                Title = "Пакет",
                Minimum = 0,
                MaximumPadding = 0,  // Отключаем отступы
                MinimumPadding = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                IsPanEnabled = true,
                IsZoomEnabled = true,  // Включаем масштабирование
                MinimumRange = 1,       // Ограничение zoom out — минимум 1 единица по времени
                TextColor = OxyColors.Black,  // Цвет текста оси
                AxislineColor = OxyColors.LightGray,  // Светло-серый цвет линии оси
                MajorGridlineColor = OxyColors.Gray,  // Серый цвет основной сетки
                MinorGridlineColor = OxyColors.DarkSlateGray  // Темно-серый цвет дополнительной сетки
            };

            PingPlotModel.Axes.Add(timeAxis);

            pingAxis = new LinearAxis
            {
                FontSize = 12,
                Font = "Segoe UI",
                Position = AxisPosition.Left,
                Title = "Время отклика (мс)",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                IsPanEnabled = true,
                IsZoomEnabled = true,  // Включаем масштабирование
                MinimumRange = 10,     // Ограничение zoom out — минимум 10 мс
                MaximumPadding = 0,    // Отключаем отступы
                MinimumPadding = 0,
                TextColor = OxyColors.Black,  // Цвет текста оси
                AxislineColor = OxyColors.LightGray,  // Светло-серый цвет линии оси
                MajorGridlineColor = OxyColors.Gray,  // Серый цвет основной сетки
                MinorGridlineColor = OxyColors.DarkSlateGray  // Темно-серый цвет дополнительной сетки
            };

            PingPlotModel.Axes.Add(pingAxis);
        }

        private void SetupSeries()
        {
            normalSeries = new LineSeries { Title = "Ping", MarkerType = MarkerType.None, Color = OxyColors.Blue };
            errorSeries = new LineSeries { Title = "Error", MarkerType = MarkerType.None, Color = OxyColors.Red };
            PingPlotModel.Series.Add(normalSeries);
            PingPlotModel.Series.Add(errorSeries);
        }

        public void SetPingData(List<int> pingData)
        {
            dataManager.SetPingData(pingData);
            UpdateGraph(null, null); // Обновление графика
        }

        private void UpdateGraph(object sender, EventArgs e)
        {
            var dataToPlot = dataManager.GetDataToPlot(isSmoothingEnabled);

            normalSeries.Points.Clear();
            errorSeries.Points.Clear();
            PingPlotModel.Annotations.Clear();

            for (int i = Math.Max(0, dataToPlot.Count - maxVisiblePoints); i < dataToPlot.Count; i++)
            {
                if (dataToPlot[i] <= 0)
                {
                    errorSeries.Points.Add(new DataPoint(i, dataToPlot[i]));
                    PingPlotModel.Annotations.Add(new TextAnnotation
                    {
                        Text = "Error",
                        TextPosition = new DataPoint(i, dataToPlot[i]),
                        TextColor = OxyColors.Red,
                        FontSize = 10,
                        FontWeight = OxyPlot.FontWeights.Bold
                    });
                }
                else
                {
                    normalSeries.Points.Add(new DataPoint(i, dataToPlot[i]));
                }
            }

            UpdateAxes();
            PingPlotModel.InvalidatePlot(true);
            UpdateStatistics();
        }

        private void UpdateAxes()
        {
            var pingData = dataManager.GetDataToPlot(isSmoothingEnabled);

            if (pingData.Count == 0)
            {
                timeAxis.Minimum = 0;
                timeAxis.Maximum = 100;
                pingAxis.Minimum = 0;
                pingAxis.Maximum = 100;
                return;
            }

            int startIndex = Math.Max(0, pingData.Count - maxVisiblePoints);
            timeAxis.Minimum = startIndex;
            timeAxis.Maximum = pingData.Count - 1;

            var visibleData = pingData.Skip(startIndex);
            pingAxis.Minimum = visibleData.Min();
            pingAxis.Maximum = visibleData.Max() + 10;
        }

        private void UpdateStatistics()
        {
            var stats = dataManager.GetStatistics();

            SetText(txtMin, stats.Min.ToString());
            SetText(txtAvg, stats.Avg.ToString("F2"));
            SetText(txtMax, stats.Max.ToString());
            SetText(txtCur, stats.Cur.ToString());
        }

        private void SetText(TextBlock textBlock, string value)
        {
            textBlock.Text = value;
        }

        private void ToggleSmoothing(object sender, RoutedEventArgs e) // Сглаживание
        {
            isSmoothingEnabled = !isSmoothingEnabled; // Переключение состояния
            UpdateGraph(null, null); // Перерисовка графика
        }
    }
}