#nullable enable

namespace PingTestTool;

public class TraceResult : ObservableBase
{
    public int Nr { get => GetProperty(0); set => SetProperty(value); }
    public string IPAddress { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string DomainName { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Loss { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Sent { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Received { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Best { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Avrg { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Wrst { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }
    public string Last { get => GetProperty(string.Empty); set => SetProperty(value ?? string.Empty); }

    public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
    {
        if (hop == null) throw new ArgumentNullException(nameof(hop));
        Nr = ttl;
        IPAddress = ipAddress;
        DomainName = domainName;
        UpdateStatistics(hop);
    }

    public void UpdateStatistics(HopData hop)
    {
        if (hop == null) throw new ArgumentNullException(nameof(hop));
        var stats = hop.GetStatistics();
        Sent = hop.Sent.ToString();
        Received = hop.Received.ToString();
        Loss = $"{stats.LossPercentage.ToString(Constants.DefaultFormat)}{Constants.PercentageSuffix}";
        Best = FormatMs(stats.Min);
        Wrst = FormatMs(stats.Max);
        Avrg = FormatMs((long)stats.Avg);
        Last = FormatMs(stats.Last);
    }

    private static string FormatMs(long ms) => $"{ms}{Constants.MsUnitSuffix}";

    public override string ToString() =>
        $"TTL: {Nr}, IP: {IPAddress}, Domain: {DomainName}, Loss: {Loss}, Sent: {Sent}, Received: {Received}, " +
        $"Best: {Best}, Avg: {Avrg}, Worst: {Wrst}, Last: {Last}";
}