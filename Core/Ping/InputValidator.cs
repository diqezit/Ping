// InputValidator.cs

#nullable enable

namespace PingTestTool;

public class InputValidator : IInputValidator
{
    public ValidationResult ValidateInput(string url, string pingCount, string timeout) =>
        new(ValidationHelper.ValidateUrl(url)
                .Concat(ValidationHelper.ValidatePingCount(pingCount))
                .Concat(ValidationHelper.ValidateTimeout(timeout))
                .ToList());
}

