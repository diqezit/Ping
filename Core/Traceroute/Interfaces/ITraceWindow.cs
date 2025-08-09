#nullable enable

namespace PingTestTool;

public interface ITraceWindow
{
    bool IsLoaded { get; }
    bool IsVisible { get; }
    Visibility Visibility { get; set; }
    void Show();
    void Close();
    event EventHandler Closed;
}

