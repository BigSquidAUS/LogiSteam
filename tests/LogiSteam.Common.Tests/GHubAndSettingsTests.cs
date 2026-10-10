using LogiSteam.Common.Config;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.GHub;

namespace LogiSteam.Common.Tests;

public class GHubMonitorTests
{
    [Theory]
    [InlineData("lghub", true)]
    [InlineData("lghub_agent", true)]
    [InlineData("LGHUB_Agent", true)]
    [InlineData("lghub_updater", false)] // a Windows service that keeps running after G HUB is quit
    [InlineData("lghub_system_tray", false)]
    [InlineData("LogiSteam", false)]
    public void IsGHubProcessName_CountsOnlyTheAppAndItsAgent(string processName, bool expected)
    {
        Assert.Equal(expected, GHubMonitor.IsGHubProcessName(processName));
    }

    [Fact]
    public void IsRunning_ReflectsTheProcessList()
    {
        Assert.True(new GHubMonitor(() => ["explorer", "lghub_agent"]).IsRunning);
        Assert.False(new GHubMonitor(() => ["explorer", "lghub_updater"]).IsRunning);
    }

    [Fact]
    public void IsRunning_IsCached_BetweenChecks()
    {
        var calls = 0;
        var monitor = new GHubMonitor(() => { calls++; return ["lghub"]; }, cacheFor: TimeSpan.FromMinutes(1));

        _ = monitor.IsRunning;
        _ = monitor.IsRunning;

        Assert.Equal(1, calls);
    }
}

public class GHubDiscoveryFieldTests
{
    [Fact]
    public void Beacon_RoundTrips_DeviceNameAndGHubState()
    {
        var json = new BeaconMessage
        {
            HostName = "WHEEL-PC", BusId = "1-4", VendorId = "046d", ProductId = "c24f",
            DeviceName = "Logitech G29", GHubRunning = false,
        }.ToJson();

        Assert.True(BeaconMessage.TryParse(json, out var parsed));
        Assert.Equal("Logitech G29", parsed!.DeviceName);
        Assert.False(parsed.GHubRunning);
    }

    [Fact]
    public void Beacon_FromAnOlderVersion_ParsesWithUnknownGHubState()
    {
        const string oldJson = """{"protocolVersion":1,"hostName":"WHEEL-PC","busId":"1-4","vendorId":"046d","productId":"c24f","shared":true}""";

        Assert.True(BeaconMessage.TryParse(oldJson, out var parsed));
        Assert.Null(parsed!.GHubRunning);
        Assert.Null(parsed.DeviceName);
    }

    [Fact]
    public void Ack_RoundTrips_GHubState_AndToleratesItsAbsence()
    {
        var json = new AckMessage { HostName = "GAME-PC", Attached = true, GHubRunning = true }.ToJson();
        Assert.True(AckMessage.TryParse(json, out var parsed));
        Assert.True(parsed!.GHubRunning);

        Assert.True(AckMessage.TryParse("""{"protocolVersion":1,"hostName":"GAME-PC","attached":true}""", out var old));
        Assert.Null(old!.GHubRunning);
    }
}

public class AppSettingsStoreTests
{
    [Fact]
    public void Load_ReturnsNoRole_WhenNothingHasBeenSaved()
    {
        var store = new AppSettingsStore(Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}.json"));

        Assert.Null(store.Load().Role);
    }

    [Fact]
    public void RequireGHubOnWheelPc_IsOffByDefault_AndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppSettingsStore(path);
            Assert.False(store.Load().RequireGHubOnWheelPc);

            store.Save(new AppSettings { Role = AppRole.Game, RequireGHubOnWheelPc = true });

            Assert.True(store.Load().RequireGHubOnWheelPc);
            Assert.Equal(AppRole.Game, store.Load().Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(AppRole.Wheel)]
    [InlineData(AppRole.Game)]
    public void Save_ThenLoad_RoundTripsTheRole(AppRole role)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings { Role = role });

            Assert.Equal(role, store.Load().Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SelectedDevice_RoundTripsItsSource_AndOldFilesLoadAsLegacy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SelectedDeviceStore(path);
            store.Save(SelectedDevice.FromProfile(DeviceDetection.KnownWheelProfiles.G29));
            Assert.Equal(WheelSelectionSource.Catalog, store.Load()!.Source);

            File.WriteAllText(path, """{"DisplayName":"Logitech G29","HardwareIds":[{"Vendor":1133,"Product":49743}]}""");
            Assert.Equal(WheelSelectionSource.Legacy, store.Load()!.Source);

            store.Clear();
            Assert.Null(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
