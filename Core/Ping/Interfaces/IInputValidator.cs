// IInputValidator.cs

#nullable enable

namespace PingTestTool;

public interface IInputValidator
{
    ValidationResult ValidateInput(string url, string pingCount, string timeout);
}