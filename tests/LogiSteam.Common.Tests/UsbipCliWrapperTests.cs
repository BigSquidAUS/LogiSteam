using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbipCliWrapperTests
{
    // Sample shape taken from usbip-win2's documented `usbip list -r <host>` output.
    private const string SampleListOutput = """
        Exportable USB devices
        ======================
         - 192.168.1.9
                3-2: unknown vendor : unknown product (1005:b113)
                   : /sys/devices/pci0000:00/0000:00:14.0/usb3/3-2
                   : (Defined at Interface level) (00/00/00)
                1-4: Logitech G29 Driving Force Racing Wheel (046d:c52b)
                   : /sys/devices/pci0000:00/0000:00:14.0/usb1/1-4
                   : (Defined at Interface level) (00/00/00)

        """;

    [Fact]
    public void ParseRemoteDeviceList_ExtractsAllDeviceLines()
    {
        var devices = UsbipCliWrapper.ParseRemoteDeviceList(SampleListOutput);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d.BusId == "3-2" && d.HardwareId == new UsbHardwareId(0x1005, 0xB113));
        Assert.Contains(devices, d => d.BusId == "1-4" && d.HardwareId == UsbHardwareId.G29);
    }

    [Fact]
    public void ParseRemoteDeviceList_FindsG29ByHardwareId()
    {
        var devices = UsbipCliWrapper.ParseRemoteDeviceList(SampleListOutput);
        var g29 = devices.FirstOrDefault(d => d.HardwareId == UsbHardwareId.G29);

        Assert.NotNull(g29);
        Assert.Equal("1-4", g29!.BusId);
        Assert.Contains("Logitech", g29.VendorProductName);
    }

    [Fact]
    public void ParseRemoteDeviceList_ReturnsEmpty_ForNoDevices()
    {
        var devices = UsbipCliWrapper.ParseRemoteDeviceList("Exportable USB devices\n======================\n");
        Assert.Empty(devices);
    }

    [Theory]
    [InlineData("successfully attached to port 1", 1)]
    [InlineData("successfully attached to port 12", 12)]
    public void ParseAttachedPort_ParsesPortNumber(string output, int expectedPort)
    {
        Assert.Equal(expectedPort, UsbipCliWrapper.ParseAttachedPort(output));
    }

    [Fact]
    public void ParseAttachedPort_ReturnsNull_WhenNoPortMentioned()
    {
        Assert.Null(UsbipCliWrapper.ParseAttachedPort("attach failed: device busy"));
    }
}
