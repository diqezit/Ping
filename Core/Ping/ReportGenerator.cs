// ReportGenerator.cs

#nullable enable

namespace PingTestTool;

public class ReportGenerator : IReportGenerator
{
    public void InitializeLogBuilder(StringBuilder sb, IPingConfiguration config, DateTime startTime) =>
        sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
          .AppendLine($"  {ResourceHelper.FindResourceString("PingTest").ToUpper()}")
          .AppendLine(PingServiceConstants.LOG_SEPARATOR)
          .AppendLine($"{ResourceHelper.FindResourceString("StartTime")}:    {startTime:dd.MM.yyyy HH:mm:ss}")
          .AppendLine($"{ResourceHelper.FindResourceString("Host")}:      {config.Url}")
          .AppendLine($"{ResourceHelper.FindResourceString("PingCount")}:   {config.PingCount}")
          .AppendLine($"{ResourceHelper.FindResourceString("Timeout")}:     {config.Timeout} {ResourceHelper.FindResourceString("Ms")}")
          .AppendLine($"{ResourceHelper.FindResourceString("DontFragment")}: {(config.DontFragment ? ResourceHelper.FindResourceString("Yes") : ResourceHelper.FindResourceString("No"))}")
          .AppendLine(PingServiceConstants.LOG_SEPARATOR);

    public async Task<string> GenerateFinalReport(StringBuilder sb, StringBuilder responseTimes,
        DateTime startTime, DateTime endTime, TimeSpan execTime, double avgJitter,
        int success, int fail, int total, IReadOnlyList<int> times)
    {
        var (Min, Max, Average) = await new StatisticsCalculator().CalculateStatisticsAsync(times).ConfigureAwait(false);
        var loss = total > 0 ? (fail * 100.0 / total).ToString("F2") : "0.00";

        sb.AppendLine(PingServiceConstants.LOG_SEPARATOR)
          .AppendLine($"  {ResourceHelper.FindResourceString("TestingResults").ToUpper()}")
          .AppendLine(PingServiceConstants.LOG_SEPARATOR)
          .AppendLine($"{ResourceHelper.FindResourceString("StartTime")}:    {startTime:dd.MM.yyyy HH:mm:ss}")
          .AppendLine($"{ResourceHelper.FindResourceString("EndTime")}:      {endTime:dd.MM.yyyy HH:mm:ss}")
          .AppendLine($"{ResourceHelper.FindResourceString("Duration")}:      {FormatExecutionTime(execTime)}")
          .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
          .AppendLine($"{ResourceHelper.FindResourceString("PacketStatistics")}:")
          .AppendLine($"    {ResourceHelper.FindResourceString("PacketsSent")}: {total}")
          .AppendLine($"    {ResourceHelper.FindResourceString("Successful")}:     {success}")
          .AppendLine($"    {ResourceHelper.FindResourceString("Lost")}:       {fail} ({loss}%)")
          .AppendLine(PingServiceConstants.LOG_MINI_SEPARATOR)
          .AppendLine($"{ResourceHelper.FindResourceString("TimeStatistics")}:")
          .AppendLine($"    {ResourceHelper.FindResourceString("Minimum")}:      {Min} {ResourceHelper.FindResourceString("Ms")}")
          .AppendLine($"    {ResourceHelper.FindResourceString("Maximum")}:      {Max} {ResourceHelper.FindResourceString("Ms")}")
          .AppendLine($"    {ResourceHelper.FindResourceString("Average")}:        {Average:F2} {ResourceHelper.FindResourceString("Ms")}")
          .AppendLine($"    {ResourceHelper.FindResourceString("Jitter")}:         {avgJitter:F2} {ResourceHelper.FindResourceString("Ms")}")
          .AppendLine(PingServiceConstants.LOG_SEPARATOR);

        return sb.ToString();
    }

    private static string FormatExecutionTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{t.TotalHours:F2} {ResourceHelper.FindResourceString("Hours")}" :
        t.TotalMinutes >= 1 ? $"{t.TotalMinutes:F2} {ResourceHelper.FindResourceString("Minutes")}" :
        $"{t.TotalSeconds:F2} {ResourceHelper.FindResourceString("Seconds")}";
}
