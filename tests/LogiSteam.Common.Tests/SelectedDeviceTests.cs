using LogiSteam.Common.Config;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class SelectedDeviceTests
{
    [Fact]
    public void From_UpgradesToFullKnownProfile_WhenPickedDeviceMatchesTheCatalog()
    {
        var g29Persona = new UsbDeviceInfo(
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC52B), "Logitech USB Input Device", "USB\\VID_046D&PID_C52B\\1");

        var selected = SelectedDevice.From(g29Persona);

        Assert.Equal(KnownWheelProfiles.G29.DisplayName, selected.DisplayName);
        Assert.Equal(KnownWheelProfiles.G29.HardwareIds, selected.HardwareIds);
    }

    [Fact]
    public void From_StaysASingleId_ForADeviceTheCatalogDoesNotRecognize()
    {
        var unknownDevice = new UsbDeviceInfo(new UsbHardwareId(0x1234, 0x5678), "Some Other Device", "USB\\VID_1234&PID_5678\\1");

        var selected = SelectedDevice.From(unknownDevice);

        Assert.Equal("Some Other Device", selected.DisplayName);
        Assert.Equal([unknownDevice.HardwareId], selected.HardwareIds);
    }
}
