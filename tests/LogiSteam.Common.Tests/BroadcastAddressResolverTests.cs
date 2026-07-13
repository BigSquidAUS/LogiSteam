using System.Net;
using LogiSteam.Common.Discovery;

namespace LogiSteam.Common.Tests;

public class BroadcastAddressResolverTests
{
    [Theory]
    [InlineData("192.168.1.50", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.0.5", "255.0.0.0", "10.255.255.255")]
    [InlineData("172.16.4.100", "255.255.254.0", "172.16.5.255")]
    [InlineData("192.168.1.1", "255.255.255.255", "192.168.1.1")]
    public void GetSubnetBroadcast_ComputesCorrectAddress(string address, string mask, string expectedBroadcast)
    {
        var result = BroadcastAddressResolver.GetSubnetBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask));

        Assert.Equal(IPAddress.Parse(expectedBroadcast), result);
    }

    [Fact]
    public void GetSubnetBroadcastAddresses_ComputesOneAddressPerInterface()
    {
        var interfaces = new[]
        {
            (Address: IPAddress.Parse("192.168.1.50"), Mask: IPAddress.Parse("255.255.255.0")),
            (Address: IPAddress.Parse("10.0.0.5"), Mask: IPAddress.Parse("255.0.0.0")),
        };

        var result = BroadcastAddressResolver.GetSubnetBroadcastAddresses(interfaces);

        Assert.Equal([IPAddress.Parse("192.168.1.255"), IPAddress.Parse("10.255.255.255")], result);
    }

    [Fact]
    public void GetSubnetBroadcastAddresses_ReturnsEmpty_ForNoInterfaces()
    {
        var result = BroadcastAddressResolver.GetSubnetBroadcastAddresses([]);

        Assert.Empty(result);
    }
}
