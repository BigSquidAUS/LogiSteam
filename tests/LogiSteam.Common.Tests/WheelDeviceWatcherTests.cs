using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class WheelDeviceWatcherTests
{
    // A real Win32_PnPEntity DeviceID for the G29, e.g. as seen in Windows Device Manager.
    private const string RealG29DeviceId = @"USB\VID_046D&PID_C52B&REV_2411&MI_00";

    [Fact]
    public void BuildDeviceIdNeedle_ContainsNoBackslash()
    {
        var needle = WheelDeviceWatcher.BuildDeviceIdNeedle(UsbHardwareId.G29);

        // Regression test: WQL LIKE clauses embedding a backslash next to "%" have broken twice
        // now against the real WMI provider -- confirmed directly, not assumed -- first via
        // over-escaping ("USB\\\\VID_..." never matching anything), then via a reproducible
        // ManagementException "Invalid query" for a pattern ending "...USB\%". The fix is to
        // never put a backslash in the WQL text at all: match this needle against DeviceID in
        // .NET instead. This test guards that invariant going forward.
        Assert.DoesNotContain('\\', needle);
    }

    [Fact]
    public void BuildDeviceIdNeedle_MatchesARealG29DeviceId()
    {
        var needle = WheelDeviceWatcher.BuildDeviceIdNeedle(UsbHardwareId.G29);

        Assert.Contains(needle, RealG29DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeviceIdNeedle_ContainsHardwareIdInWindowsVidPidFormat()
    {
        var needle = WheelDeviceWatcher.BuildDeviceIdNeedle(UsbHardwareId.G29);

        Assert.Contains("VID_046D", needle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PID_C52B", needle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeviceIdNeedle_DoesNotMatchADifferentDevice()
    {
        var needle = WheelDeviceWatcher.BuildDeviceIdNeedle(UsbHardwareId.G29);
        const string unrelatedDeviceId = @"USB\VID_1005&PID_B113\7&2A2B3C4D&0&1";

        Assert.DoesNotContain(needle, unrelatedDeviceId, StringComparison.OrdinalIgnoreCase);
    }
}
