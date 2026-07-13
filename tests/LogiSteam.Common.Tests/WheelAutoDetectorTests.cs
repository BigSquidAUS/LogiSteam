using LogiSteam.Common.Config;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class WheelAutoDetectorTests
{
    private static UsbDeviceInfo Device(UsbHardwareId id, string description = "Some Device") =>
        new(id, description, $"USB\\VID_{id.Vendor:X4}&PID_{id.Product:X4}\\1");

    private static readonly UsbHardwareId Ps3Mode = new(UsbHardwareId.LogitechVendorId, 0xC24F);
    private static readonly UsbHardwareId Ps4Mode = new(UsbHardwareId.LogitechVendorId, 0xC260);
    private static readonly UsbHardwareId UnrelatedMouse = new(0x1234, 0x5678);

    [Fact]
    public void DetectFirstMatch_ReturnsProfile_WhenOneOfItsPersonasIsLive()
    {
        // Proves it doesn't require ALL personas to be present -- just one.
        var devices = new[] { Device(Ps4Mode), Device(UnrelatedMouse) };

        var result = WheelAutoDetector.DetectFirstMatch(devices);

        Assert.Equal(KnownWheelProfiles.G29, result);
    }

    [Fact]
    public void DetectFirstMatch_ReturnsNull_WhenNoLiveDeviceMatchesAnyProfile()
    {
        var devices = new[] { Device(UnrelatedMouse), Device(new UsbHardwareId(0x8087, 0x0024)) };

        Assert.Null(WheelAutoDetector.DetectFirstMatch(devices));
    }

    [Fact]
    public void DetectFirstMatch_ReturnsNull_WhenNoDevicesArePresent()
    {
        Assert.Null(WheelAutoDetector.DetectFirstMatch([]));
    }

    [Fact]
    public void DetectFirstMatch_IsIndependentOfDeviceOrder()
    {
        var forward = new[] { Device(UnrelatedMouse), Device(Ps3Mode) };
        var reversed = new[] { Device(Ps3Mode), Device(UnrelatedMouse) };

        Assert.Equal(WheelAutoDetector.DetectFirstMatch(forward), WheelAutoDetector.DetectFirstMatch(reversed));
    }

    [Fact]
    public void DetectFirstMatch_ReturnsFirstMatchingProfileInCatalogOrder_WhenMultipleProfilesCouldMatch()
    {
        var sharedId = new UsbHardwareId(0x0AAA, 0x0001);
        var profileA = new KnownWheelProfile { DisplayName = "Profile A", HardwareIds = [sharedId] };
        var profileB = new KnownWheelProfile { DisplayName = "Profile B", HardwareIds = [sharedId] };
        var customCatalog = new[] { profileA, profileB };

        var result = WheelAutoDetector.DetectFirstMatch([Device(sharedId)], customCatalog);

        Assert.Equal(profileA, result);
    }

    [Fact]
    public void DetectFirstMatch_IgnoresProfileHardwareIds_NotCurrentlyLive()
    {
        var devices = new[] { Device(UnrelatedMouse) };

        var result = WheelAutoDetector.DetectFirstMatch(devices, [KnownWheelProfiles.G29]);

        Assert.Null(result);
    }

    [Fact]
    public void ReconcileSelection_DetectsFromScratch_WhenNothingIsCurrentlySelected()
    {
        var result = WheelAutoDetector.ReconcileSelection(null, [Device(Ps3Mode)]);

        Assert.Equal(SelectedDevice.FromProfile(KnownWheelProfiles.G29), result);
    }

    [Fact]
    public void ReconcileSelection_ReturnsNull_WhenNothingSelectedAndNothingDetected()
    {
        var result = WheelAutoDetector.ReconcileSelection(null, [Device(UnrelatedMouse)]);

        Assert.Null(result);
    }

    [Fact]
    public void ReconcileSelection_UpgradesLegacySingleIdSelection_ToFullKnownProfile()
    {
        // Simulates a manual pick made before auto-detect existed, or while the wheel happened to
        // be in one specific mode -- even though it's currently live and matching fine, it should
        // still get upgraded so a FUTURE mode switch won't strand it.
        var singleIdSelection = new SelectedDevice { DisplayName = "USB Composite Device", HardwareIds = [Ps3Mode] };

        var result = WheelAutoDetector.ReconcileSelection(singleIdSelection, [Device(Ps3Mode)]);

        Assert.Equal(SelectedDevice.FromProfile(KnownWheelProfiles.G29), result);
    }

    [Fact]
    public void ReconcileSelection_SelfHeals_WhenSelectionGoesStaleButAKnownModelIsLive()
    {
        // The "mode switched, old persisted id is dead" bug: selection is a stale single id
        // (PS3 mode) but the wheel is now live in PS4 mode.
        var staleSelection = new SelectedDevice { DisplayName = "Stale", HardwareIds = [Ps3Mode] };

        var result = WheelAutoDetector.ReconcileSelection(staleSelection, [Device(Ps4Mode)]);

        Assert.Equal(SelectedDevice.FromProfile(KnownWheelProfiles.G29), result);
    }

    [Fact]
    public void ReconcileSelection_LeavesWorkingSelectionUnchanged()
    {
        var current = SelectedDevice.FromProfile(KnownWheelProfiles.G29);

        var result = WheelAutoDetector.ReconcileSelection(current, [Device(Ps4Mode)]);

        Assert.Same(current, result);
    }

    [Fact]
    public void ReconcileSelection_LeavesUnrecognizedManualPickUnchanged_WhenTemporarilyUnplugged()
    {
        // A deliberate manual pick of a device the catalog doesn't know about shouldn't be wiped
        // just because it's not currently plugged in -- the user chose it, it may come back.
        var manualPick = SelectedDevice.From(Device(UnrelatedMouse, "My Unrecognized Wheel"));

        var result = WheelAutoDetector.ReconcileSelection(manualPick, []);

        Assert.Same(manualPick, result);
    }
}
