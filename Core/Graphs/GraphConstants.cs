#nullable enable

namespace PingTestTool;

public static class GraphConstants
{
    public const int DefaultMaxVisiblePoints = 100;
    public const int MinPingIntervalMilliseconds = 100;

    public const string GraphTitle = "Ping Response Time Graph";
    public const string TimeAxisTitle = "Time";
    public const string TimeAxisFormat = "HH:mm:ss";
    public const string ResponseAxisTitle = "Response Time (ms)";

    public const OxyPlot.MarkerType MarkerType = OxyPlot.MarkerType.Circle;
    public const double MarkerSize = 3.0;

    public static readonly OxyColor GraphBackgroundColor = OxyColors.White;
    public static readonly OxyColor LineColor = OxyColor.FromRgb(0, 114, 189);
    public static readonly OxyColor MarkerStrokeColor = OxyColor.FromRgb(0, 114, 189);
    public static readonly OxyColor MarkerFillColor = OxyColors.White;
}