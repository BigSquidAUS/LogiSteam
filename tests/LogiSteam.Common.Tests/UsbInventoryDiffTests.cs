using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbInventoryDiffTests
{
    private static readonly UsbHardwareId G29 = UsbHardwareId.G29;
    private static readonly UsbHardwareId Mouse = new(0x046D, 0xC246);

    [Fact]
    public void Compute_ReportsArrival_ForNewHardwareId()
    {
        var previous = Array.Empty<UsbDeviceInfo>();
        var current = new[] { new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\1") };

        var changes = UsbInventoryDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(UsbInventoryChangeKind.Arrived, change.Kind);
        Assert.Equal(G29, change.HardwareId);
        Assert.Null(change.OldDeviceId);
        Assert.Equal(@"USB\VID_046D&PID_C52B\1", change.NewDeviceId);
    }

    [Fact]
    public void Compute_ReportsDeparture_ForMissingHardwareId()
    {
        var previous = new[] { new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\1") };
        var current = Array.Empty<UsbDeviceInfo>();

        var changes = UsbInventoryDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(UsbInventoryChangeKind.Left, change.Kind);
        Assert.Equal(@"USB\VID_046D&PID_C52B\1", change.OldDeviceId);
        Assert.Null(change.NewDeviceId);
    }

    [Fact]
    public void Compute_ReportsDeviceIdChanged_WhenSameHardwareIdMovesLocation()
    {
        // Regression scenario: usbipd-win swaps in its "USBIP Shared Device" stub while sharing a
        // device, and the bus/port location has also been observed to shift (e.g. "3-2" -> "4-1")
        // -- same VID:PID, different underlying Windows DeviceID. This must be reported distinctly
        // from Arrived/Left so the log can show "it moved" rather than implying a real unplug.
        var previous = new[] { new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\3-2") };
        var current = new[] { new UsbDeviceInfo(G29, "USBIP Shared Device", @"USB\VID_046D&PID_C52B\4-1") };

        var changes = UsbInventoryDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(UsbInventoryChangeKind.DeviceIdChanged, change.Kind);
        Assert.Equal(@"USB\VID_046D&PID_C52B\3-2", change.OldDeviceId);
        Assert.Equal(@"USB\VID_046D&PID_C52B\4-1", change.NewDeviceId);
    }

    [Fact]
    public void Compute_ReportsNothing_WhenSnapshotsAreIdentical()
    {
        var snapshot = new[] { new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\1") };

        var changes = UsbInventoryDiff.Compute(snapshot, snapshot);

        Assert.Empty(changes);
    }

    [Fact]
    public void Compute_HandlesMultipleSimultaneousChanges()
    {
        var previous = new[]
        {
            new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\1"),
            new UsbDeviceInfo(Mouse, "Logitech Mouse", @"USB\VID_046D&PID_C246\2"),
        };
        var current = new[]
        {
            // G29 unchanged
            new UsbDeviceInfo(G29, "G29 Wheel", @"USB\VID_046D&PID_C52B\1"),
            // Mouse left, a new unrelated device arrived
            new UsbDeviceInfo(new UsbHardwareId(0x1234, 0x5678), "Some Other Device", @"USB\VID_1234&PID_5678\3"),
        };

        var changes = UsbInventoryDiff.Compute(previous, current);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Kind == UsbInventoryChangeKind.Left && c.HardwareId == Mouse);
        Assert.Contains(changes, c => c.Kind == UsbInventoryChangeKind.Arrived && c.HardwareId == new UsbHardwareId(0x1234, 0x5678));
    }
}
