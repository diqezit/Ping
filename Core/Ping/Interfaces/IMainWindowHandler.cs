// IMainWindowHandler.cs

#nullable enable

namespace PingTestTool;

public interface IMainWindowHandler
{
    void HandleWindowClosed(object? sender, EventArgs e);
    Task HandlePingButtonClickAsync();
    void HandleStopButtonClick();
    Task HandleShowGraphButtonClickAsync();
    void HandleTraceRouteButtonClick();
    void HideWarningsOnStartup();
}