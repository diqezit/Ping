#nullable enable

namespace PingTestTool;

public abstract class ValidationBase
{
    protected static void ValidateNotNull<T>(T value, string paramName) where T : class =>
        _ = value ?? throw new ArgumentNullException(paramName);

    protected static void ValidateNotNullOrEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}