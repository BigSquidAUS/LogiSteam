using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbDeviceEnumeratorTests
{
    [Fact]
    public void DeduplicateByHardwareId_CollapsesMultipleInterfacesOfTheSameDevice()
    {
        // Mirrors a real composite USB device: several PnP entries share one VID:PID but differ
        // by "&MI_xx" function suffix (as seen for the actual G29: PID_C52B&REV_2411&MI_00 and
        // PID_C52B&MI_00 both showed up in Device Manager for one physical wheel).
        var g29 = UsbHardwareId.G29;
        var devices = new[]
        {
            new UsbDeviceInfo(g29, "Logitech USB Input Device", @"USB\VID_046D&PID_C52B&MI_00\7&1&0"),
            new UsbDeviceInfo(g29, "Logitech USB Input Device", @"USB\VID_046D&PID_C52B&REV_2411&MI_00\7&1&1"),
            new UsbDeviceInfo(g29, "Logitech G29 Driving Force Racing Wheel", @"USB\VID_046D&PID_C52B\7&1&2"),
        };

        var result = UsbDeviceEnumerator.DeduplicateByHardwareId(devices);

        Assert.Single(result);
        Assert.Equal(g29, result[0].HardwareId);
        // Prefers the entry without an "&MI_" suffix -- more likely the friendly parent name.
        Assert.Equal("Logitech G29 Driving Force Racing Wheel", result[0].Description);
    }

    [Fact]
    public void DeduplicateByHardwareId_KeepsDistinctDevicesSeparate()
    {
        var devices = new[]
        {
            new UsbDeviceInfo(UsbHardwareId.G29, "Logitech G29 Driving Force Racing Wheel", @"USB\VID_046D&PID_C52B\1"),
            new UsbDeviceInfo(new UsbHardwareId(0x046D, 0xC246), "Logitech Mouse", @"USB\VID_046D&PID_C246\2"),
        };

        var result = UsbDeviceEnumerator.DeduplicateByHardwareId(devices);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.Description == "Logitech G29 Driving Force Racing Wheel");
        Assert.Contains(result, d => d.Description == "Logitech Mouse");
    }

    [Fact]
    public void DeduplicateByHardwareId_SortsByDescription()
    {
        var devices = new[]
        {
            new UsbDeviceInfo(new UsbHardwareId(1, 1), "Zebra Device", @"USB\VID_0001&PID_0001\1"),
            new UsbDeviceInfo(new UsbHardwareId(2, 2), "Alpha Device", @"USB\VID_0002&PID_0002\1"),
        };

        var result = UsbDeviceEnumerator.DeduplicateByHardwareId(devices);

        Assert.Equal("Alpha Device", result[0].Description);
        Assert.Equal("Zebra Device", result[1].Description);
    }

    [Fact]
    public void DeduplicateByHardwareId_ReturnsEmpty_ForNoDevices()
    {
        Assert.Empty(UsbDeviceEnumerator.DeduplicateByHardwareId([]));
    }
}
