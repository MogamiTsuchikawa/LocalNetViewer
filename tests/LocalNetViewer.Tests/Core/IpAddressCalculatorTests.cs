using LocalNetViewer.Core.Networking;

namespace LocalNetViewer.Tests.Core;

public sealed class IpAddressCalculatorTests
{
    [Fact]
    public void CalculateIPv4_ReturnsExpectedNetworkRows()
    {
        var result = IpAddressCalculator.CalculateIPv4("192.168.1.42", 24);

        Assert.Equal("192.168.1.42/24", result.Input);
        Assert.Contains(result.Rows, row => row.Label == "ネットワーク" && row.Address == "192.168.1.0");
        Assert.Contains(result.Rows, row => row.Label == "ブロードキャスト" && row.Address == "192.168.1.255");
        Assert.Contains(result.Rows, row => row.Label == "サブネットマスク" && row.Address == "255.255.255.0");
    }

    [Fact]
    public void ExpandIPv4Range_RespectsMaximum()
    {
        var addresses = IpAddressCalculator.ExpandIPv4Range("192.168.1.0/24", 4);

        Assert.Equal(["192.168.1.1", "192.168.1.2", "192.168.1.3", "192.168.1.4"], addresses.Select(item => item.ToString()));
    }

    [Fact]
    public void ExpandIPv4Range_IncludesEntireIpv4ClassCSubnet()
    {
        var addresses = IpAddressCalculator.ExpandIPv4Range("192.168.1.42/24");

        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.1.1", addresses.First().ToString());
        Assert.Equal("192.168.1.254", addresses.Last().ToString());
    }
}
