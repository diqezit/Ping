// IWarningPresenter.cs

#nullable enable

namespace PingTestTool;

public interface IWarningPresenter
{
    void HideAllWarnings();
    void ShowWarnings(ValidationResult result);
}

