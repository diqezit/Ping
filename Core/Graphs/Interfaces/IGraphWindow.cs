#nullable enable

namespace PingTestTool;

public interface IGraphWindow
{
    bool IsLoaded { get; }
    WindowState WindowState { get; set; }
    void SetPingData(List<(DateTime Time, int RoundtripTime)> roundtripTimes);
    void Show();
    void Close();
    event EventHandler? Closed;
}