using LogiSteam.Common.Discovery;

namespace LogiSteam.Common.Tests;

public class AckMessageTests
{
    [Fact]
    public void ToJson_ThenTryParse_RoundTrips()
    {
        var message = new AckMessage { HostName = "GAME-PC", Attached = true };

        var json = message.ToJson();

        Assert.True(AckMessage.TryParse(json, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(message.HostName, parsed!.HostName);
        Assert.Equal(message.Attached, parsed.Attached);
        Assert.Equal(AckMessage.ProtocolVersion, parsed.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"unrelated\":true}")]
    public void TryParse_RejectsGarbageOrIncompleteInput(string json)
    {
        Assert.False(AckMessage.TryParse(json, out var parsed));
        Assert.Null(parsed);
    }
}
