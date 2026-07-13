using LogiSteam.Common.Config;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class SelectedDeviceStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"logisteam-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_ReturnsNull_WhenFileDoesNotExist()
    {
        var store = new SelectedDeviceStore(_tempPath);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SelectedDeviceStore(_tempPath);
        var device = new SelectedDevice
        {
            DisplayName = "Logitech G29",
            HardwareIds = [new UsbHardwareId(0x046D, 0xC24F), new UsbHardwareId(0x046D, 0xC260)],
        };

        store.Save(device);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(device.DisplayName, loaded!.DisplayName);
        Assert.Equal(device.HardwareIds, loaded.HardwareIds);
    }

    [Fact]
    public void Load_ReturnsNull_ForCorruptFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
        File.WriteAllText(_tempPath, "{not valid json");

        var store = new SelectedDeviceStore(_tempPath);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_OverwritesPreviousSelection()
    {
        var store = new SelectedDeviceStore(_tempPath);
        store.Save(new SelectedDevice { DisplayName = "First", HardwareIds = [new UsbHardwareId(1, 1)] });
        store.Save(new SelectedDevice { DisplayName = "Second", HardwareIds = [new UsbHardwareId(2, 2)] });

        var loaded = store.Load();

        Assert.Equal("Second", loaded!.DisplayName);
    }

    [Fact]
    public void Load_ReturnsNull_ForOldSingleHardwareIdShapeJson()
    {
        // Proves no migration code is needed: the pre-multi-persona on-disk shape
        // ({"Vendor":...,"Product":...,"Description":...}) has none of the new required
        // members, so deserialization throws and Load()'s existing catch degrades to null --
        // which gracefully re-triggers auto-detection/the picker rather than crashing.
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
        File.WriteAllText(_tempPath, """{"Vendor":1133,"Product":49739,"Description":"Old Shape Device"}""");

        var store = new SelectedDeviceStore(_tempPath);
        Assert.Null(store.Load());
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }
}
