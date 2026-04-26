using System.Text.RegularExpressions;
using System.Globalization;

namespace LocalNetViewer.Core.Networking;

public static partial class ArpParser
{
    public static IReadOnlyList<ArpEntry> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<ArpEntry>();
        }

        var entries = new List<ArpEntry>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var unix = UnixArpRegex().Match(rawLine);
            if (unix.Success)
            {
                var hostName = unix.Groups["name"].Value == "?" ? "" : unix.Groups["name"].Value;
                entries.Add(new ArpEntry(
                    unix.Groups["ip"].Value,
                    NormalizeMac(unix.Groups["mac"].Value),
                    unix.Groups["if"].Value,
                    "ARP")
                {
                    HostName = hostName,
                });
                continue;
            }

            var windows = WindowsArpRegex().Match(rawLine);
            if (windows.Success)
            {
                entries.Add(new ArpEntry(
                    windows.Groups["ip"].Value,
                    NormalizeMac(windows.Groups["mac"].Value),
                    "",
                    "ARP"));
            }
        }

        return entries;
    }

    private static string NormalizeMac(string value)
    {
        var parts = value.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 6)
        {
            return string.Join(':', parts.Select(part =>
                byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var octet)
                    ? octet.ToString("X2", CultureInfo.InvariantCulture)
                    : part.ToUpperInvariant()));
        }

        return value.Replace('-', ':').ToUpperInvariant();
    }

    [GeneratedRegex(@"^(?<name>\S+)\s+\((?<ip>\d{1,3}(?:\.\d{1,3}){3})\)\s+at\s+(?<mac>[0-9a-fA-F:-]{11,17})\s+on\s+(?<if>\S+)", RegexOptions.Compiled)]
    private static partial Regex UnixArpRegex();

    [GeneratedRegex(@"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F-]{17})\s+\S+", RegexOptions.Compiled)]
    private static partial Regex WindowsArpRegex();
}

public static partial class TraceRouteParser
{
    public static IReadOnlyList<string> ParseHops(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => HopRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["host"].Value)
            .Distinct()
            .ToArray();
    }

    [GeneratedRegex(@"^\s*\d+\s+(?:\S+\s+){1,3}(?<host>(?:\d{1,3}\.){3}\d{1,3}|[a-zA-Z0-9_.-]+)", RegexOptions.Compiled)]
    private static partial Regex HopRegex();
}

public static partial class MdnsParser
{
    public static IReadOnlyList<MdnsBrowseEntry> ParseBrowse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<MdnsBrowseEntry>();
        }

        var entries = new List<MdnsBrowseEntry>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = BrowseRegex().Match(rawLine);
            if (match.Success)
            {
                entries.Add(new MdnsBrowseEntry(
                    match.Groups["type"].Value.TrimEnd('.'),
                    match.Groups["name"].Value.Trim()));
            }
        }

        return entries
            .DistinctBy(entry => $"{entry.ServiceType}\n{entry.InstanceName}")
            .ToArray();
    }

    public static string ParseResolvedHostName(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ResolvedHostRegex().Match(rawLine);
            if (match.Success)
            {
                return match.Groups["host"].Value.TrimEnd('.');
            }
        }

        return "";
    }

    public static IReadOnlyList<MdnsAddressEntry> ParseAddresses(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<MdnsAddressEntry>();
        }

        var entries = new List<MdnsAddressEntry>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = AddressRegex().Match(rawLine);
            if (match.Success)
            {
                entries.Add(new MdnsAddressEntry(
                    match.Groups["host"].Value.TrimEnd('.'),
                    match.Groups["ip"].Value));
            }
        }

        return entries
            .DistinctBy(entry => entry.IpAddress)
            .ToArray();
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}:\d{2}\.\d{3}\s+Add\s+\d+\s+\d+\s+\S+\s+(?<type>_[^\s]+\._(?:tcp|udp)\.)\s+(?<name>.+)$", RegexOptions.Compiled)]
    private static partial Regex BrowseRegex();

    [GeneratedRegex(@"\bcan be reached at\s+(?<host>.+?):\d+\s+\(", RegexOptions.Compiled)]
    private static partial Regex ResolvedHostRegex();

    [GeneratedRegex(@"^\d{1,2}:\d{2}:\d{2}\.\d{3}\s+Add\s+\S+\s+\d+\s+(?<host>\S+)\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+\d+\s*$", RegexOptions.Compiled)]
    private static partial Regex AddressRegex();
}
