using System.Collections.ObjectModel;
using System.Net;

namespace LocalNetViewer.Core.Networking;

public sealed record HostRecord
{
    public string Kind { get; init; } = "不明";
    public string HostName { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Response { get; init; } = "";
    public string LastSeen { get; init; } = "";
    public int LatencyMs { get; init; }
}

public sealed record ArpEntry(string IpAddress, string MacAddress, string InterfaceName, string Source)
{
    public string HostName { get; init; } = "";
}

public sealed record MdnsBrowseEntry(string ServiceType, string InstanceName);

public sealed record MdnsAddressEntry(string HostName, string IpAddress);

public sealed record PortScanResult
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string Service { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
}

public sealed record CommandDefinition(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    bool IsAvailable,
    bool RequiresAdministrator,
    string UnavailableReason);

public sealed record CommandResult(
    string CommandLine,
    string Output,
    string Error,
    int ExitCode,
    TimeSpan Elapsed,
    DateTimeOffset StartedAt);

public sealed record CommandHistoryRecord
{
    public string Command { get; init; } = "";
    public string Target { get; init; } = "";
    public string Status { get; init; } = "";
    public string Elapsed { get; init; } = "";
    public string ExecutedAt { get; init; } = "";
}

public sealed record PacketRecord
{
    public int Number { get; init; }
    public string Time { get; init; } = "";
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Protocol { get; init; } = "";
    public int Length { get; init; }
    public string Summary { get; init; } = "";
}

public sealed record CapabilityStatus
{
    public string Feature { get; init; } = "";
    public string Platform { get; init; } = "";
    public string Status { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed record ScanSettings
{
    public string InterfaceName { get; init; } = "";
    public string AddressOrCidr { get; init; } = "192.168.1.0/24";
    public int TimeoutMs { get; init; } = 400;
    public int TimeToLive { get; init; } = 64;
    public int Parallelism { get; init; } = 64;
    public bool IncludeArp { get; init; } = true;
    public bool ResolveNames { get; init; } = true;
}

public sealed record NetworkInterfaceInfo(string Name, string Description, string Address, string Cidr);

public sealed record IpCalculationResult
{
    public string Input { get; init; } = "";
    public int PrefixLength { get; init; }
    public IReadOnlyList<IpConversionRow> Rows { get; init; } = Array.Empty<IpConversionRow>();
}

public sealed record IpConversionRow(
    string Label,
    string Address,
    string Hexadecimal,
    string Decimal,
    string ReversedDecimal,
    string Binary);

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<HostRecord>> DiscoverAsync(ScanSettings settings, CancellationToken cancellationToken);
    IReadOnlyList<NetworkInterfaceInfo> GetInterfaces();
}

public interface IPortScanService
{
    Task<IReadOnlyList<PortScanResult>> ScanTcpAsync(
        IEnumerable<string> hosts,
        IEnumerable<int> ports,
        int timeoutMs,
        int parallelism,
        CancellationToken cancellationToken);
}

public interface ICommandService
{
    IReadOnlyList<CommandDefinition> GetDefinitions();
    Task<CommandResult> RunAsync(string commandId, string target, string extraArguments, CancellationToken cancellationToken);
}

public interface ICaptureInventoryService
{
    IReadOnlyList<PacketRecord> GetInitialPackets();
    CapabilityStatus GetCaptureStatus();
}

public interface IPlatformStatusService
{
    string PlatformName { get; }
    IReadOnlyList<CapabilityStatus> GetCapabilities();
}

public sealed class HostCollection : ObservableCollection<HostRecord>
{
    public HostCollection()
    {
    }

    public HostCollection(IEnumerable<HostRecord> hosts)
        : base(hosts)
    {
    }
}
