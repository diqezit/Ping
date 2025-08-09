// StatisticsCalculator.cs

#nullable enable

namespace PingTestTool;

public class StatisticsCalculator : IStatisticsCalculator
{
    public Task<double> CalculateAverageJitterAsync(IReadOnlyList<int> times) =>
        Task.FromResult(times.Count <= 1
            ? 0.0
            : Math.Round(Enumerable.Range(1, times.Count - 1)
                .Select(i => Math.Abs(times[i] - times[i - 1]))
                .Average(), 2));

    public Task<(int Min, int Max, double Average)> CalculateStatisticsAsync(IReadOnlyList<int> times) =>
        Task.FromResult(times.Count == 0
            ? (0, 0, 0.0)
            : (times.Min(), times.Max(), times.Average()));
}

