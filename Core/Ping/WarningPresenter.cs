// WarningPresenter.cs

#nullable enable

namespace PingTestTool;

public class WarningPresenter : IWarningPresenter
{
    private readonly Image[] _warnings;

    public WarningPresenter(params Image[] warnings) =>
        _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));

    public void HideAllWarnings() =>
        Array.ForEach(_warnings, img => img.Visibility = Visibility.Collapsed);

    public void ShowWarnings(ValidationResult result)
    {
        if (!result.IsValid && _warnings.FirstOrDefault() is Image warning)
        {
            warning.Visibility = Visibility.Visible;
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Errors),
                ResourceHelper.FindResourceString("InputErrorCaption"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
