// ValidationResult.cs

#nullable enable

namespace PingTestTool;

public sealed class ValidationResult
{
    public List<string> Errors { get; }
    public bool IsValid => Errors.Count == 0;
    public ValidationResult(List<string> errors) => Errors = errors ?? new List<string>();
}

