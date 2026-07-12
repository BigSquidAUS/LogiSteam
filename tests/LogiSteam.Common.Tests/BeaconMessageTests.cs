using LogiSteam.Common.Discovery;

namespace LogiSteam.Common.Tests;

public class BeaconMessageTests
{
    [Fact]
    public void ToJson_ThenTryParse_RoundTrips()
    {
        var message = new BeaconMessage
        {
            HostName = "WHEEL-PC",
            BusId = "1-4",
            VendorId = "046d",
            ProductId = "c52b",
            Shared = true,
        };

        var json = message.ToJson();

        Assert.True(BeaconMessage.TryParse(json, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(message.HostName, parsed!.HostName);
        Assert.Equal(message.BusId, parsed.BusId);
        Assert.Equal(message.VendorId, parsed.VendorId);
        Assert.Equal(message.ProductId, parsed.ProductId);
        Assert.Equal(message.Shared, parsed.Shared);
        Assert.Equal(BeaconMessage.ProtocolVersion, parsed.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{not valid json")]
    [InlineData("{\"unrelated\":true}")] // valid JSON, but missing required fields
    [InlineData("{\"hostName\":\"X\"")] // truncated
    public void TryParse_RejectsGarbageOrIncompleteInput(string json)
    {
        Assert.False(BeaconMessage.TryParse(json, out var parsed));
        Assert.Null(parsed);
    }
}
