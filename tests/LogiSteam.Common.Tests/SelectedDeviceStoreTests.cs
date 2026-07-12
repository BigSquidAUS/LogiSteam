using LogiSteam.Common.Config;

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
        var device = new SelectedDevice { Vendor = 0x046D, Product = 0xC52B, Description = "Logitech G29 Driving Force Racing Wheel" };

        store.Save(device);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(device.Vendor, loaded!.Vendor);
        Assert.Equal(device.Product, loaded.Product);
        Assert.Equal(device.Description, loaded.Description);
        Assert.Equal(device.HardwareId, loaded.HardwareId);
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
        store.Save(new SelectedDevice { Vendor = 1, Product = 1, Description = "First" });
        store.Save(new SelectedDevice { Vendor = 2, Product = 2, Description = "Second" });

        var loaded = store.Load();

        Assert.Equal("Second", loaded!.Description);
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }
}
