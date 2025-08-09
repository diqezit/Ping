#nullable enable

namespace PingTestTool;

public interface IGraphManager : IDisposable
{
    void UpdateMaxVisiblePoints(int maxVisiblePoints);
    void UpdateGraph();
    void SetData(IEnumerable<(DateTime Time, int Value)> data);
}