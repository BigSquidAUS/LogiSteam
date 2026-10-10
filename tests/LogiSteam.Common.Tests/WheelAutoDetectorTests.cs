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

    // ---- Identification with no device list (ids are what one real PC actually had plugged in) ----

    private static readonly UsbHardwareId GamingMouse = new(UsbHardwareId.LogitechVendorId, 0xC246);
    private static readonly UsbHardwareId Extreme3DPro = new(UsbHardwareId.LogitechVendorId, 0xC215);
    private static readonly UsbHardwareId UnifyingReceiver = new(UsbHardwareId.LogitechVendorId, 0xC52B);
    private static readonly UsbHardwareId UnlistedWheel = new(UsbHardwareId.LogitechVendorId, 0xC2FF);

    [Fact]
    public void Detect_PicksTheWheel_AmongOtherLogitechDevices()
    {
        // Exactly what a real PC reported with a G29, a Logitech mouse and a Logitech joystick
        // plugged in at once.
        var devices = new[]
        {
            Device(GamingMouse, "USB Composite Device"),
            Device(Extreme3DPro, "Logitech Extreme 3D Pro USB"),
            Device(Ps3Mode, "USB Composite Device"),
        };

        var result = WheelAutoDetector.Detect(devices);

        Assert.NotNull(result);
        Assert.Equal("Logitech G29", result!.DisplayName);
        Assert.Equal(WheelSelectionSource.Catalog, result.Source);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenOnlyNonWheelLogitechDevicesArePresent()
    {
        var devices = new[]
        {
            Device(GamingMouse, "USB Composite Device"),
            Device(Extreme3DPro, "Logitech Extreme 3D Pro USB"),
            Device(UnifyingReceiver, "Logitech USB Input Device"),
        };
        var productNames = new Dictionary<UsbHardwareId, string>
        {
            [GamingMouse] = "Gaming Mouse G300",
            [Extreme3DPro] = "Extreme 3D pro",
            [UnifyingReceiver] = "USB Receiver",
        };

        Assert.Null(WheelAutoDetector.Detect(devices, productNames));
    }

    [Fact]
    public void Detect_RecognizesAnUnlistedLogitechWheel_ByItsUsbProductName()
    {
        var devices = new[] { Device(UnlistedWheel, "USB Input Device") };
        var productNames = new Dictionary<UsbHardwareId, string> { [UnlistedWheel] = "RS50 Racing Wheel" };

        var result = WheelAutoDetector.Detect(devices, productNames);

        Assert.NotNull(result);
        Assert.Equal("Logitech RS50 Racing Wheel", result!.DisplayName);
        Assert.Equal([UnlistedWheel], result.HardwareIds);
        Assert.Equal(WheelSelectionSource.NameMatch, result.Source);
    }

    [Fact]
    public void Detect_DoesNotTrustAWheelLikeName_FromAnotherVendor()
    {
        var other = new UsbHardwareId(0x1234, 0x0001);

        Assert.Null(WheelAutoDetector.Detect([Device(other, "Super Racing Wheel")]));
    }

    [Theory]
    [InlineData("G29 Driving Force Racing Wheel", true)]
    [InlineData("Logitech G HUB G29 Driving Force Racing Wheel USB", true)]
    [InlineData("Driving Force GT", true)]
    [InlineData("USB Wheel Mouse", false)]
    [InlineData("Logitech Extreme 3D Pro USB", false)]
    [InlineData("USB Receiver", false)]
    [InlineData("USB Input Device", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeWheelName_MatchesWheelsOnly(string? name, bool expected)
    {
        Assert.Equal(expected, WheelAutoDetector.LooksLikeWheelName(name));
    }

    [Fact]
    public void ReconcileSelection_DropsALeftoverNonWheelPick_FromTheOldDeviceList()
    {
        // Saved by a version whose device list let the user pick anything -- here, a mouse.
        var leftover = new SelectedDevice { DisplayName = "USB Composite Device", HardwareIds = [GamingMouse] };

        Assert.Null(WheelAutoDetector.ReconcileSelection(leftover, [Device(GamingMouse)]));
    }

    [Fact]
    public void ReconcileSelection_ReplacesALeftoverNonWheelPick_WithTheRealWheel()
    {
        var leftover = new SelectedDevice { DisplayName = "Logitech USB Input Device", HardwareIds = [UnifyingReceiver] };

        var result = WheelAutoDetector.ReconcileSelection(leftover, [Device(UnifyingReceiver), Device(Ps3Mode)]);

        Assert.Equal("Logitech G29", result?.DisplayName);
    }

    [Fact]
    public void ReconcileSelection_StripsTheUnifyingReceiver_FromAnOlderSavedG29Selection()
    {
        var saved = new SelectedDevice { DisplayName = "Logitech G29", HardwareIds = [Ps3Mode, Ps4Mode, UnifyingReceiver] };

        var result = WheelAutoDetector.ReconcileSelection(saved, [Device(UnifyingReceiver)]);

        Assert.Equal(KnownWheelProfiles.G29.HardwareIds, result?.HardwareIds);
    }

    [Fact]
    public void ReconcileSelection_KeepsARecognizedWheel_WhileItIsNotVisible()
    {
        // Unplugged, or bound to usbipd-win's stub driver (which hides its normal PnP entry).
        var current = SelectedDevice.FromProfile(KnownWheelProfiles.G29);

        Assert.Same(current, WheelAutoDetector.ReconcileSelection(current, [Device(GamingMouse)]));
    }

    [Fact]
    public void ReconcileSelection_SwitchesToADifferentKnownWheel_WhenThatIsWhatIsPluggedIn()
    {
        var current = SelectedDevice.FromProfile(KnownWheelProfiles.G29);
        var g920 = KnownWheelProfiles.G920.HardwareIds[0];

        var result = WheelAutoDetector.ReconcileSelection(current, [Device(g920)]);

        Assert.Equal("Logitech G920", result?.DisplayName);
    }

    [Fact]
    public void ReconcileSelection_NeverOverridesAManualChoice()
    {
        var manual = SelectedDevice.From(Device(UnrelatedMouse, "My Unrecognized Wheel"));

        Assert.Same(manual, WheelAutoDetector.ReconcileSelection(manual, [Device(Ps3Mode)]));
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
