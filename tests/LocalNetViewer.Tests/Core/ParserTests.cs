using LocalNetViewer.Core.Networking;

namespace LocalNetViewer.Tests.Core;

public sealed class ParserTests
{
    [Fact]
    public void ArpParser_ParsesUnixRows()
    {
        const string output = "? (192.168.1.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]";

        var entry = Assert.Single(ArpParser.Parse(output));

        Assert.Equal("192.168.1.1", entry.IpAddress);
        Assert.Equal("AA:BB:CC:DD:EE:FF", entry.MacAddress);
        Assert.Equal("en0", entry.InterfaceName);
        Assert.Equal("", entry.HostName);
    }

    [Fact]
    public void ArpParser_ParsesUnixHostNames()
    {
        const string output = "router.local (192.168.1.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]";

        var entry = Assert.Single(ArpParser.Parse(output));

        Assert.Equal("router.local", entry.HostName);
        Assert.Equal("192.168.1.1", entry.IpAddress);
    }

    [Fact]
    public void ArpParser_PadsSingleDigitMacOctets()
    {
        const string output = "? (192.168.1.1) at d8:43:ae:67:97:c on en0 ifscope [ethernet]";

        var entry = Assert.Single(ArpParser.Parse(output));

        Assert.Equal("D8:43:AE:67:97:0C", entry.MacAddress);
    }

    [Fact]
    public void ArpParser_ParsesWindowsRows()
    {
        const string output = "  192.168.1.33          11-22-33-44-55-66     dynamic";

        var entry = Assert.Single(ArpParser.Parse(output));

        Assert.Equal("192.168.1.33", entry.IpAddress);
        Assert.Equal("11:22:33:44:55:66", entry.MacAddress);
    }

    [Fact]
    public void MdnsParser_ParsesBrowseRowsWithSpaces()
    {
        const string output = "23:02:14.908  Add        2  15 local.               _ssh._tcp.           Shogo's MacBook Pro";

        var entry = Assert.Single(MdnsParser.ParseBrowse(output));

        Assert.Equal("_ssh._tcp", entry.ServiceType);
        Assert.Equal("Shogo's MacBook Pro", entry.InstanceName);
    }

    [Fact]
    public void MdnsParser_ParsesResolvedHostNames()
    {
        const string output = "23:02:33.168  kvm-d778._ssh._tcp.local. can be reached at kvm-d778.local.:22 (interface 15)";

        var hostName = MdnsParser.ParseResolvedHostName(output);

        Assert.Equal("kvm-d778.local", hostName);
    }

    [Fact]
    public void MdnsParser_ParsesAddressRows()
    {
        const string output = "23:02:46.020  Add  40000002      15  kvm-d778.local.                        192.168.0.27                                 120";

        var entry = Assert.Single(MdnsParser.ParseAddresses(output));

        Assert.Equal("kvm-d778.local", entry.HostName);
        Assert.Equal("192.168.0.27", entry.IpAddress);
    }
}
