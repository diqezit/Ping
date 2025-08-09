// IStatisticsCalculator.cs

#nullable enable

namespace PingTestTool;

public interface IStatisticsCalculator
{
    Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> roundtripTimes);
    Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> roundtripTimes);
}