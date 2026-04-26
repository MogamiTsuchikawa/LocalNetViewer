using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using LocalNetViewer.Core.Networking;

namespace LocalNetViewer.Platform.Services;

public sealed class PlatformStatusService : IPlatformStatusService
{
    public string PlatformName
    {
        get
        {
            if (OperatingSystem.IsMacOS()) return "macOS";
            if (OperatingSystem.IsWindows()) return "Windows";
            if (OperatingSystem.IsLinux()) return "Linux";
            return RuntimeInformation.OSDescription;
        }
    }

    public IReadOnlyList<CapabilityStatus> GetCapabilities()
    {
        var commandRunner = new CommandService();
        var capture = new CaptureInventoryService();
        return new[]
        {
            new CapabilityStatus { Feature = "ネットワーク探索", Platform = PlatformName, Status = "利用可", Reason = "ICMP/DNS/ARPを組み合わせて実行します。" },
            new CapabilityStatus { Feature = "TCPポート確認", Platform = PlatformName, Status = "利用可", Reason = "TCP接続で開放状態を確認します。" },
            capture.GetCaptureStatus(),
            new CapabilityStatus
            {
                Feature = "Windows管理操作",
                Platform = PlatformName,
                Status = OperatingSystem.IsWindows() ? "条件付き利用可" : "利用不可",
                Reason = OperatingSystem.IsWindows() ? "管理権限と対象ホスト側の設定が必要です。" : "Windows固有APIが必要なため、このOSでは無効です。"
            },
            new CapabilityStatus
            {
                Feature = "OS別コマンド",
                Platform = PlatformName,
                Status = "検出済み",
                Reason = $"{commandRunner.GetDefinitions().Count(command => command.IsAvailable)}個のコマンドを利用できます。"
            },
        };
    }
}

public sealed class NetworkDiscoveryService : INetworkDiscoveryService
{
    public IReadOnlyList<NetworkInterfaceInfo> GetInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ToInterfaceInfo)
            .DefaultIfEmpty(new NetworkInterfaceInfo("自動", "既定のインターフェース", "127.0.0.1", "127.0.0.1/32"))
            .ToArray();
    }

    public async Task<IReadOnlyList<HostRecord>> DiscoverAsync(ScanSettings settings, CancellationToken cancellationToken)
    {
        var discovered = new ConcurrentDictionary<string, HostRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CreateLocalRows())
        {
            AddOrMerge(discovered, item);
        }

        if (settings.IncludeArp)
        {
            await MergeArpRowsAsync(discovered, cancellationToken).ConfigureAwait(false);
        }

        var addresses = SafeExpand(settings.AddressOrCidr);
        await Parallel.ForEachAsync(
            addresses,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(settings.Parallelism, 1, 256), CancellationToken = cancellationToken },
            async (address, token) =>
            {
                var row = await ProbeAsync(address, settings, token).ConfigureAwait(false);
                if (row is not null)
                {
                    AddOrMerge(discovered, row);
                }
            }).ConfigureAwait(false);

        if (settings.IncludeArp)
        {
            await MergeArpRowsAsync(discovered, cancellationToken).ConfigureAwait(false);
        }

        if (settings.ResolveNames)
        {
            await EnrichMdnsNamesAsync(discovered, cancellationToken).ConfigureAwait(false);
            await EnrichHostNamesAsync(discovered, cancellationToken).ConfigureAwait(false);
        }

        return discovered.Values
            .OrderBy(row => Version.TryParse(row.IpAddress, out var version) ? version : new Version(999, 999))
            .ToArray();
    }

    private static async Task MergeArpRowsAsync(
        ConcurrentDictionary<string, HostRecord> discovered,
        CancellationToken cancellationToken)
    {
        foreach (var item in await ReadArpRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            AddOrMerge(discovered, item);
        }
    }

    private static void AddOrMerge(ConcurrentDictionary<string, HostRecord> discovered, HostRecord incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.IpAddress))
        {
            return;
        }

        discovered.AddOrUpdate(incoming.IpAddress, incoming, (_, existing) => MergeHostRecords(existing, incoming));
    }

    private static HostRecord MergeHostRecords(HostRecord existing, HostRecord incoming)
    {
        var macAddress = PreferNonEmpty(incoming.MacAddress, existing.MacAddress);
        var vendor = PreferNonEmpty(incoming.Vendor, existing.Vendor);
        if (string.IsNullOrWhiteSpace(vendor))
        {
            vendor = VendorLookup.Guess(macAddress);
        }

        return new HostRecord
        {
            Kind = MergeKind(existing, incoming, macAddress),
            HostName = PreferNonEmpty(incoming.HostName, existing.HostName),
            IpAddress = PreferNonEmpty(incoming.IpAddress, existing.IpAddress),
            MacAddress = macAddress,
            Vendor = vendor,
            OperatingSystem = PreferNonEmpty(incoming.OperatingSystem, existing.OperatingSystem),
            UserName = PreferNonEmpty(incoming.UserName, existing.UserName),
            Response = PreferResponse(existing.Response, incoming.Response),
            LastSeen = PreferNonEmpty(incoming.LastSeen, existing.LastSeen),
            LatencyMs = incoming.LatencyMs > 0 ? incoming.LatencyMs : existing.LatencyMs,
        };
    }

    private static string MergeKind(HostRecord existing, HostRecord incoming, string macAddress)
    {
        if (existing.Kind == "自機" || incoming.Kind == "自機")
        {
            return "自機";
        }

        var hasResponse = existing.Kind.Contains("応答", StringComparison.Ordinal)
            || incoming.Kind.Contains("応答", StringComparison.Ordinal)
            || existing.Response == "応答あり"
            || incoming.Response == "応答あり";
        var hasArp = existing.Kind.Contains("ARP", StringComparison.Ordinal)
            || incoming.Kind.Contains("ARP", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(macAddress);
        var hasMdns = existing.Kind.Contains("mDNS", StringComparison.Ordinal)
            || incoming.Kind.Contains("mDNS", StringComparison.Ordinal);

        if (hasResponse && hasArp)
        {
            return "応答+ARP";
        }

        if (hasResponse && hasMdns)
        {
            return "応答+mDNS";
        }

        if (hasArp && hasMdns)
        {
            return "mDNS+ARP";
        }

        return PreferNonEmpty(incoming.Kind, existing.Kind);
    }

    private static string PreferResponse(string existing, string incoming)
    {
        if (incoming == "応答あり" || existing == "応答あり")
        {
            return "応答あり";
        }

        if (incoming == "mDNS" || existing == "mDNS")
        {
            return "mDNS";
        }

        return PreferNonEmpty(incoming, existing);
    }

    private static string PreferNonEmpty(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static IEnumerable<NetworkInterfaceInfo> ToInterfaceInfo(NetworkInterface networkInterface)
    {
        foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
        {
            if (address.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var prefix = address.PrefixLength;
            yield return new NetworkInterfaceInfo(
                networkInterface.Name,
                networkInterface.Description,
                address.Address.ToString(),
                $"{address.Address}/{prefix}");
        }
    }

    private static IEnumerable<HostRecord> CreateLocalRows()
    {
        var hostName = Dns.GetHostName();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces().Where(item => item.OperationalStatus == OperationalStatus.Up))
        {
            var mac = string.Join(':', networkInterface.GetPhysicalAddress().GetAddressBytes().Select(part => part.ToString("X2")));
            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses.Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                yield return new HostRecord
                {
                    Kind = "自機",
                    HostName = hostName,
                    IpAddress = address.Address.ToString(),
                    MacAddress = mac,
                    Vendor = mac.Length == 17 ? "ローカルNIC" : "",
                    OperatingSystem = RuntimeInformation.OSDescription,
                    UserName = Environment.UserName,
                    Response = "自機",
                    LastSeen = DateTime.Now.ToString("HH:mm:ss"),
                };
            }
        }
    }

    private static async Task<IEnumerable<HostRecord>> ReadArpRowsAsync(CancellationToken cancellationToken)
    {
        var command = OperatingSystem.IsWindows() ? "arp" : "/usr/sbin/arp";
        var args = OperatingSystem.IsWindows() ? "-a" : "-an";
        var output = await ProcessRunner.RunAsync(command, args, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        return ArpParser.Parse(output.Output)
            .Select(entry => new HostRecord
            {
                Kind = "ARP",
                HostName = entry.HostName,
                IpAddress = entry.IpAddress,
                MacAddress = entry.MacAddress,
                Vendor = VendorLookup.Guess(entry.MacAddress),
                Response = "ARP",
                LastSeen = DateTime.Now.ToString("HH:mm:ss"),
            });
    }

    private static IReadOnlyList<IPAddress> SafeExpand(string cidr)
    {
        try
        {
            return IpAddressCalculator.ExpandIPv4Range(cidr, 4096);
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static async Task<HostRecord?> ProbeAsync(IPAddress address, ScanSettings settings, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        try
        {
            var started = Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(address, settings.TimeoutMs).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (reply.Status != IPStatus.Success)
            {
                return null;
            }

            string hostName = "";
            if (settings.ResolveNames)
            {
                hostName = await ResolveHostNameAsync(address, cancellationToken).ConfigureAwait(false);
            }

            return new HostRecord
            {
                Kind = "応答",
                HostName = hostName,
                IpAddress = address.ToString(),
                Response = "応答あり",
                LatencyMs = (int)Math.Max(reply.RoundtripTime, started.ElapsedMilliseconds),
                LastSeen = DateTime.Now.ToString("HH:mm:ss"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task EnrichHostNamesAsync(
        ConcurrentDictionary<string, HostRecord> discovered,
        CancellationToken cancellationToken)
    {
        var unresolved = discovered.Values
            .Where(row => string.IsNullOrWhiteSpace(row.HostName) && IPAddress.TryParse(row.IpAddress, out _))
            .ToArray();

        await Parallel.ForEachAsync(
            unresolved,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            async (row, token) =>
            {
                if (!IPAddress.TryParse(row.IpAddress, out var address))
                {
                    return;
                }

                var hostName = await ResolveHostNameAsync(address, token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(hostName))
                {
                    AddOrMerge(discovered, row with { HostName = hostName });
                }
            }).ConfigureAwait(false);
    }

    private static async Task EnrichMdnsNamesAsync(
        ConcurrentDictionary<string, HostRecord> discovered,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("/usr/bin/dns-sd"))
        {
            return;
        }

        var serviceTypes = new[]
        {
            "_workstation._tcp",
            "_ssh._tcp",
            "_sftp-ssh._tcp",
            "_smb._tcp",
            "_http._tcp",
            "_ipp._tcp",
            "_printer._tcp",
            "_airplay._tcp",
            "_raop._tcp",
            "_companion-link._tcp",
            "_googlecast._tcp",
            "_device-info._tcp",
            "_apple-mobdev2._tcp",
            "_remotepairing._tcp",
        };

        var browseEntries = new ConcurrentBag<MdnsBrowseEntry>();
        await Parallel.ForEachAsync(
            serviceTypes,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (serviceType, token) =>
            {
                var result = await ProcessRunner.RunCollectingAsync(
                    "/usr/bin/dns-sd",
                    ["-B", serviceType, "local"],
                    TimeSpan.FromMilliseconds(1_600),
                    token).ConfigureAwait(false);
                foreach (var entry in MdnsParser.ParseBrowse(result.Output))
                {
                    browseEntries.Add(entry);
                    MergeMdnsInstanceByMac(discovered, entry.InstanceName);
                }
            }).ConfigureAwait(false);

        var entries = browseEntries
            .DistinctBy(entry => $"{entry.ServiceType}\n{entry.InstanceName}")
            .Take(64)
            .ToArray();

        await Parallel.ForEachAsync(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (entry, token) =>
            {
                var resolved = await ProcessRunner.RunCollectingAsync(
                    "/usr/bin/dns-sd",
                    ["-L", entry.InstanceName, entry.ServiceType, "local"],
                    TimeSpan.FromMilliseconds(1_200),
                    token).ConfigureAwait(false);
                var hostName = MdnsParser.ParseResolvedHostName(resolved.Output);
                if (string.IsNullOrWhiteSpace(hostName))
                {
                    return;
                }

                var addresses = await ProcessRunner.RunCollectingAsync(
                    "/usr/bin/dns-sd",
                    ["-G", "v4", hostName],
                    TimeSpan.FromMilliseconds(1_200),
                    token).ConfigureAwait(false);

                var displayName = CleanMdnsInstanceName(entry.InstanceName);
                foreach (var address in MdnsParser.ParseAddresses(addresses.Output))
                {
                    var displayHostName = ChooseMdnsHostName(displayName, address.HostName);
                    if (string.IsNullOrWhiteSpace(displayHostName) && !discovered.ContainsKey(address.IpAddress))
                    {
                        continue;
                    }

                    AddOrMerge(discovered, new HostRecord
                    {
                        Kind = "mDNS",
                        HostName = displayHostName,
                        IpAddress = address.IpAddress,
                        Response = "mDNS",
                        LastSeen = DateTime.Now.ToString("HH:mm:ss"),
                    });
                }
            }).ConfigureAwait(false);
    }

    private static void MergeMdnsInstanceByMac(
        ConcurrentDictionary<string, HostRecord> discovered,
        string instanceName)
    {
        var (name, macAddress) = SplitMdnsWorkstationName(instanceName);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(macAddress))
        {
            return;
        }

        var matches = discovered.Values
            .Where(row => string.Equals(NormalizeMac(row.MacAddress), macAddress, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var match in matches)
        {
            AddOrMerge(discovered, match with { HostName = name, Kind = "mDNS+ARP" });
        }
    }

    private static (string Name, string MacAddress) SplitMdnsWorkstationName(string instanceName)
    {
        var marker = instanceName.LastIndexOf('[');
        if (marker < 0 || !instanceName.EndsWith(']'))
        {
            return ("", "");
        }

        var name = instanceName[..marker].Trim();
        var macAddress = NormalizeMac(instanceName[(marker + 1)..^1]);
        return (IsUsefulMdnsName(name) ? name : "", macAddress);
    }

    private static string CleanMdnsInstanceName(string instanceName)
    {
        var (name, _) = SplitMdnsWorkstationName(instanceName);
        var cleaned = CleanMdnsDisplayName(string.IsNullOrWhiteSpace(name) ? instanceName.Trim() : name);
        return IsUsefulMdnsName(cleaned) ? cleaned : "";
    }

    private static string ChooseMdnsHostName(string instanceName, string hostName)
    {
        if (IsUsefulMdnsName(instanceName))
        {
            return instanceName;
        }

        var cleanedHostName = hostName.Trim().TrimEnd('.');
        if (cleanedHostName.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            cleanedHostName = cleanedHostName[..^6];
        }

        cleanedHostName = CleanMdnsDisplayName(cleanedHostName);
        return IsUsefulMdnsName(cleanedHostName) ? cleanedHostName : "";
    }

    private static string CleanMdnsDisplayName(string value)
    {
        var name = value.Trim();
        var atMarker = name.IndexOf('@', StringComparison.Ordinal);
        if (atMarker >= 0 && atMarker < name.Length - 1)
        {
            name = name[(atMarker + 1)..].Trim();
        }

        return name;
    }

    private static bool IsUsefulMdnsName(string value)
    {
        var name = CleanMdnsDisplayName(value);

        if (name.Length < 2)
        {
            return false;
        }

        if (Guid.TryParse(name, out _))
        {
            return false;
        }

        var identifierChars = name.Count(c => Uri.IsHexDigit(c) || c == '-');
        return identifierChars < name.Length || name.Length <= 16;
    }

    private static async Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            return (await Dns.GetHostEntryAsync(address.ToString(), cancellationToken).ConfigureAwait(false)).HostName;
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeMac(string value)
    {
        var parts = value.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 6)
        {
            return "";
        }

        return string.Join(':', parts.Select(part =>
            byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var octet)
                ? octet.ToString("X2", CultureInfo.InvariantCulture)
                : ""));
    }

    private static bool CommandExists(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            return File.Exists(fileName);
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(path => File.Exists(Path.Combine(path, fileName)));
    }
}

public sealed class PortScanService : IPortScanService
{
    public async Task<IReadOnlyList<PortScanResult>> ScanTcpAsync(
        IEnumerable<string> hosts,
        IEnumerable<int> ports,
        int timeoutMs,
        int parallelism,
        CancellationToken cancellationToken)
    {
        var targets = hosts.SelectMany(host => ports.Select(port => (host, port))).ToArray();
        var results = new ConcurrentBag<PortScanResult>();

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(parallelism, 1, 256), CancellationToken = cancellationToken },
            async (target, token) =>
            {
                results.Add(await ScanOneAsync(target.host, target.port, timeoutMs, token).ConfigureAwait(false));
            }).ConfigureAwait(false);

        return results.OrderBy(row => row.Host).ThenBy(row => row.Port).ToArray();
    }

    private static async Task<PortScanResult> ScanOneAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        var started = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken).ConfigureAwait(false);
            return new PortScanResult
            {
                Host = host,
                Port = port,
                Service = KnownPorts.Name(port),
                Status = "開放",
                Detail = $"TCP接続成功 ({started.ElapsedMilliseconds} ms)",
            };
        }
        catch (TimeoutException)
        {
            return Closed(host, port, "応答なし", $"timeout {timeoutMs} ms");
        }
        catch (Exception ex)
        {
            return Closed(host, port, "閉鎖", ex.Message);
        }
    }

    private static PortScanResult Closed(string host, int port, string status, string detail) => new()
    {
        Host = host,
        Port = port,
        Service = KnownPorts.Name(port),
        Status = status,
        Detail = detail,
    };
}

public sealed class CommandService : ICommandService
{
    public IReadOnlyList<CommandDefinition> GetDefinitions()
    {
        var isWindows = OperatingSystem.IsWindows();
        return new[]
        {
            Definition("ping", "Ping", "基本", "ICMP到達確認", true),
            Definition("arp", "ARP一覧", "基本", "ARPキャッシュを表示", CommandExists(isWindows ? "arp.exe" : "/usr/sbin/arp")),
            Definition("netstat", "Netstat", "基本", "接続状態を表示", CommandExists(isWindows ? "netstat.exe" : "/usr/sbin/netstat")),
            Definition("dns", "DNS Lookup", "名前解決", "名前解決を実行", CommandExists(isWindows ? "nslookup.exe" : "/usr/bin/nslookup") || CommandExists("/usr/bin/dig")),
            Definition("route", "Route", "経路", "ルーティングテーブルを表示", CommandExists(isWindows ? "route.exe" : "/usr/sbin/route")),
            Definition("trace", "Trace Route", "経路", "経路探索を実行", CommandExists(isWindows ? "tracert.exe" : "/usr/sbin/traceroute")),
            Definition("hostname", "Hostname", "基本", "ローカルホスト名を表示", true),
            Definition("nbtstat", "NBTStat", "Windows", "NetBIOS情報を表示", isWindows && CommandExists("nbtstat.exe"), false, isWindows ? "" : "Windows専用です。"),
            Definition("netsh", "Netsh", "Windows", "Windowsネットワーク設定を表示", isWindows && CommandExists("netsh.exe"), true, isWindows ? "" : "Windows専用です。"),
        };
    }

    public async Task<CommandResult> RunAsync(string commandId, string target, string extraArguments, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = BuildCommand(commandId, target, extraArguments);
        var started = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(fileName, arguments, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        return new CommandResult(
            $"{fileName} {arguments}".Trim(),
            result.Output,
            result.Error,
            result.ExitCode,
            stopwatch.Elapsed,
            started);
    }

    private static CommandDefinition Definition(
        string id,
        string displayName,
        string category,
        string description,
        bool available,
        bool requiresAdministrator = false,
        string unavailableReason = "") =>
        new(id, displayName, category, description, available, requiresAdministrator, available ? "" : unavailableReason);

    private static (string FileName, string Arguments) BuildCommand(string id, string target, string extra)
    {
        var safeTarget = string.IsNullOrWhiteSpace(target) ? "127.0.0.1" : target.Trim();
        var safeExtra = extra?.Trim() ?? "";
        var isWindows = OperatingSystem.IsWindows();
        return id switch
        {
            "ping" => (isWindows ? "ping.exe" : "/sbin/ping", isWindows ? $"-n 4 {safeTarget}" : $"-c 4 {safeTarget}"),
            "arp" => (isWindows ? "arp.exe" : "/usr/sbin/arp", "-a"),
            "netstat" => (isWindows ? "netstat.exe" : "/usr/sbin/netstat", isWindows ? "-ano" : "-an"),
            "dns" => (CommandExists(isWindows ? "nslookup.exe" : "/usr/bin/nslookup") ? (isWindows ? "nslookup.exe" : "/usr/bin/nslookup") : "/usr/bin/dig", safeTarget),
            "route" => (isWindows ? "route.exe" : "/usr/sbin/route", isWindows ? "print" : "-n get default"),
            "trace" => (isWindows ? "tracert.exe" : "/usr/sbin/traceroute", safeTarget),
            "hostname" => (isWindows ? "hostname.exe" : "/bin/hostname", ""),
            "nbtstat" => ("nbtstat.exe", $"-A {safeTarget}"),
            "netsh" => ("netsh.exe", string.IsNullOrWhiteSpace(safeExtra) ? "interface ip show config" : safeExtra),
            _ => throw new InvalidOperationException($"未対応のコマンドです: {id}"),
        };
    }

    private static bool CommandExists(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            return File.Exists(fileName);
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(path => File.Exists(Path.Combine(path, fileName)));
    }
}

public sealed class CaptureInventoryService : ICaptureInventoryService
{
    public IReadOnlyList<PacketRecord> GetInitialPackets() => Array.Empty<PacketRecord>();

    public CapabilityStatus GetCaptureStatus()
    {
        if (OperatingSystem.IsWindows())
        {
            var npcap = Directory.Exists(@"C:\Windows\System32\Npcap") || Directory.Exists(@"C:\Program Files\Npcap");
            return new CapabilityStatus
            {
                Feature = "パケットキャプチャ",
                Platform = "Windows",
                Status = npcap ? "条件付き利用可" : "追加設定が必要",
                Reason = npcap ? "Npcapを検出しました。管理権限が必要な場合があります。" : "Npcapのインストールが必要です。",
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            var bpfCount = Directory.GetFiles("/dev", "bpf*", SearchOption.TopDirectoryOnly).Length;
            return new CapabilityStatus
            {
                Feature = "パケットキャプチャ",
                Platform = "macOS",
                Status = bpfCount > 0 ? "条件付き利用可" : "権限確認が必要",
                Reason = bpfCount > 0 ? $"BPFデバイスを{bpfCount}個検出しました。実行権限が必要です。" : "BPFデバイスにアクセスできません。",
            };
        }

        return new CapabilityStatus
        {
            Feature = "パケットキャプチャ",
            Platform = RuntimeInformation.OSDescription,
            Status = "未対応",
            Reason = "この画面はmacOS/Windowsを対象にしています。",
        };
    }
}

internal static class ProcessRunner
{
    public static async Task<(string Output, string Error, int ExitCode)> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return (await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false), process.ExitCode);
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return ("", ex.Message, -1);
        }
    }

    public static async Task<(string Output, string Error, int ExitCode)> RunCollectingAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return (await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false), process.ExitCode);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                return (await CompleteReadAsync(outputTask).ConfigureAwait(false), await CompleteReadAsync(errorTask).ConfigureAwait(false), -1);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw;
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return ("", ex.Message, -1);
        }
    }

    private static async Task<string> CompleteReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
    }
}

internal static class VendorLookup
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Vendors = new(LoadVendors);

    public static string Guess(string macAddress)
    {
        var normalized = NormalizeHex(macAddress);
        if (normalized.Length < 6)
        {
            return "";
        }

        if (!byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var firstOctet))
        {
            return "";
        }

        if ((firstOctet & 0x01) != 0)
        {
            return "マルチキャスト/特殊MAC";
        }

        if ((firstOctet & 0x02) != 0)
        {
            return "ローカル管理アドレス";
        }

        foreach (var length in new[] { 9, 7, 6 })
        {
            if (normalized.Length >= length && Vendors.Value.TryGetValue(normalized[..length], out var vendor))
            {
                return vendor;
            }
        }

        return "不明";
    }

    private static IReadOnlyDictionary<string, string> LoadVendors()
    {
        var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001A2B"] = "Example Networks",
            ["3C22FB"] = "Apple",
            ["F45C89"] = "Intel",
            ["B827EB"] = "Raspberry Pi",
            ["DCA632"] = "Raspberry Pi",
            ["000C29"] = "VMware",
            ["005056"] = "VMware",
            ["080027"] = "Oracle VirtualBox",
            ["00155D"] = "Microsoft",
        };

        LoadEmbeddedVendorFile(vendors);
        foreach (var path in VendorFileCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                LoadVendorFile(path, vendors);
            }
        }

        return vendors;
    }

    private static void LoadEmbeddedVendorFile(IDictionary<string, string> vendors)
    {
        var assembly = typeof(VendorLookup).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".Data.oui.tsv", StringComparison.Ordinal));
        if (resourceName is null)
        {
            return;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        LoadVendorTsv(reader.ReadToEnd().Split('\n'), vendors);
    }

    private static IEnumerable<string> VendorFileCandidates()
    {
        var configured = Environment.GetEnvironmentVariable("LOCALNETVIEWER_OUI_FILE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        yield return "/opt/homebrew/share/nmap/nmap-mac-prefixes";
        yield return "/usr/local/share/nmap/nmap-mac-prefixes";
        yield return "/usr/share/nmap/nmap-mac-prefixes";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Nmap", "nmap-mac-prefixes");
        }
    }

    private static void LoadVendorFile(string path, IDictionary<string, string> vendors)
    {
        try
        {
            if (path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
            {
                LoadVendorTsv(File.ReadLines(path), vendors);
                return;
            }

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var prefix = NormalizePrefix(parts[0]);
                    if (prefix.Length == 6)
                    {
                        vendors[prefix] = parts[1];
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static void LoadVendorTsv(IEnumerable<string> lines, IDictionary<string, string> vendors)
    {
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var prefix = NormalizeHex(parts[0]);
                if (prefix.Length is 6 or 7 or 9)
                {
                    vendors[prefix] = parts[1];
                }
            }
        }
    }

    private static string NormalizePrefix(string value) => NormalizeHex(value);

    private static string NormalizeHex(string value)
    {
        var builder = new StringBuilder(12);
        foreach (var c in value)
        {
            if (Uri.IsHexDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}

internal static class KnownPorts
{
    public static string Name(int port) => port switch
    {
        22 => "SSH",
        53 => "DNS",
        80 => "HTTP",
        135 => "RPC",
        139 => "NetBIOS",
        443 => "HTTPS",
        445 => "SMB",
        3389 => "RDP",
        8080 => "HTTP-Alt",
        _ => "",
    };
}
