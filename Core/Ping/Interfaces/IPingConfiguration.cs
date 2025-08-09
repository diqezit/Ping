// IPingConfiguration.cs

#nullable enable

namespace PingTestTool;

public interface IPingConfiguration
{
    string Url { get; }
    int PingCount { get; }
    int Timeout { get; }
    bool DontFragment { get; }
    void Validate();
}
