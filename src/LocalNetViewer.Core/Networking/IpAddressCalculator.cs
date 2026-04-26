using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LocalNetViewer.Core.Networking;

public static class IpAddressCalculator
{
    public static IpCalculationResult CalculateIPv4(string input, int prefixLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("IPアドレスを入力してください。", nameof(input));
        }

        var addressText = input.Trim();
        if (addressText.Contains('/', StringComparison.Ordinal))
        {
            var parts = addressText.Split('/', 2);
            addressText = parts[0];
            if (!int.TryParse(parts[1], out prefixLength))
            {
                throw new ArgumentException("CIDRのprefixが不正です。", nameof(input));
            }
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "IPv4 prefixは0から32で指定してください。");
        }

        if (!IPAddress.TryParse(addressText, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("IPv4アドレスを入力してください。", nameof(input));
        }

        var value = ToUInt32(ip);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        var network = value & mask;
        var broadcast = network | ~mask;
        var firstHost = prefixLength >= 31 ? network : network + 1;
        var lastHost = prefixLength >= 31 ? broadcast : broadcast - 1;
        var hostCount = prefixLength >= 31 ? (1UL << (32 - prefixLength)) : (1UL << (32 - prefixLength)) - 2;

        var rows = new[]
        {
            Row("入力IP", value),
            Row("ネットワーク", network),
            Row("ブロードキャスト", broadcast),
            Row("最初のホスト", firstHost),
            Row("最後のホスト", lastHost),
            Row("サブネットマスク", mask),
            Row("ワイルドカード", ~mask),
            new IpConversionRow("ホスト数", hostCount.ToString("N0"), "", hostCount.ToString(), "", ""),
        };

        return new IpCalculationResult
        {
            Input = $"{ip}/{prefixLength}",
            PrefixLength = prefixLength,
            Rows = rows,
        };
    }

    public static IReadOnlyList<IPAddress> ExpandIPv4Range(string input, int maxAddresses = 256)
    {
        var result = CalculateIPv4(input, 24);
        var network = ToUInt32(IPAddress.Parse(result.Rows.Single(row => row.Label == "ネットワーク").Address));
        var broadcast = ToUInt32(IPAddress.Parse(result.Rows.Single(row => row.Label == "ブロードキャスト").Address));
        var first = result.PrefixLength >= 31 ? network : network + 1;
        var last = result.PrefixLength >= 31 ? broadcast : broadcast - 1;
        var count = Math.Min((long)maxAddresses, (long)last - first + 1);
        var addresses = new List<IPAddress>((int)Math.Max(count, 0));

        for (var offset = 0u; offset < count; offset++)
        {
            addresses.Add(FromUInt32(first + offset));
        }

        return addresses;
    }

    public static IpConversionRow Convert(string label, IPAddress address) => Row(label, ToUInt32(address));

    private static IpConversionRow Row(string label, uint value)
    {
        var address = FromUInt32(value);
        var bytes = address.GetAddressBytes();
        return new IpConversionRow(
            label,
            address.ToString(),
            $"0x{value:X8}",
            value.ToString(),
            string.Join('.', bytes.Reverse()),
            string.Join('.', bytes.Select(part => System.Convert.ToString(part, 2).PadLeft(8, '0'))));
    }

    private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress FromUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
