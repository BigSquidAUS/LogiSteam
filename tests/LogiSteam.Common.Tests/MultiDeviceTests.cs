using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbDeviceClassifierTests
{
    private static ShareAdvice Advice(string[] classes, params string[] services) => UsbDeviceClassifier.Classify(classes, services).Advice;

    [Fact]
    public void GameControllersAndButtonBoxes_AreFineToShare()
    {
        // A joystick / pedals / Stream Deck: a USB node with HID children and nothing else.
        Assert.Equal(ShareAdvice.Ok, Advice(["USB", "HIDClass", "HIDClass"], "usbccgp", "HidUsb"));
        Assert.Equal(ShareAdvice.Ok, Advice(["HIDClass"], "HidUsb"));
    }

    [Theory]
    [InlineData("USBSTOR")]
    [InlineData("UASPStor")]
    [InlineData("usbstor")]
    public void StorageDrives_AreBlocked(string service)
    {
        Assert.Equal(ShareAdvice.Blocked, Advice(["USB"], service));
    }

    [Fact]
    public void NetworkAndBluetoothAdaptersAndHubs_AreBlocked()
    {
        Assert.Equal(ShareAdvice.Blocked, Advice(["Net"], "rtl8153"));
        Assert.Equal(ShareAdvice.Blocked, Advice(["Bluetooth"], "BTHUSB"));
        Assert.Equal(ShareAdvice.Blocked, Advice(["USB"], "USBHUB3"));
        Assert.Equal(ShareAdvice.Blocked, Advice(["USB"], "usbhub"));
    }

    [Fact]
    public void KeyboardsAndMice_NeedConfirmation_NotABlock()
    {
        // A gaming mouse: USB composite node, with Mouse and Keyboard HID nodes beneath it.
        // Not blocked, because macro pads and many button boxes look exactly like this.
        Assert.Equal(ShareAdvice.ConfirmInput, Advice(["USB", "HIDClass", "Mouse", "Keyboard"], "usbccgp", "HidUsb", "mouhid", "kbdhid"));
        Assert.Equal(ShareAdvice.ConfirmInput, Advice(["Keyboard"], "kbdhid"));
    }

    [Fact]
    public void AudioAndVideo_GetACaution()
    {
        Assert.Equal(ShareAdvice.CautionMedia, Advice(["MEDIA"], "usbaudio"));
        Assert.Equal(ShareAdvice.CautionMedia, Advice(["Camera"], "usbvideo"));
    }

    [Fact]
    public void ADangerousPart_OutweighsAHarmlessOne()
    {
        // e.g. a keyboard with a built-in card reader: the storage part decides.
        Assert.Equal(ShareAdvice.Blocked, Advice(["Keyboard", "HIDClass"], "kbdhid", "USBSTOR"));
    }

    [Fact]
    public void MissingClassInformation_IsTreatedAsOrdinary()
    {
        Assert.Equal(ShareAdvice.Ok, UsbDeviceClassifier.Classify([null, ""], [null]).Advice);
    }

    [Theory]
    [InlineData("USB Input Device", "Stream Deck XL", "Stream Deck XL")]
    [InlineData("USB Composite Device", "T.16000M Joystick", "T.16000M Joystick")]
    [InlineData("Logitech Extreme 3D Pro USB", "Logitech Extreme 3D", "Logitech Extreme 3D Pro USB")] // Windows' name is already good
    [InlineData("USB Input Device", null, "USB Input Device")] // nothing better available
    public void ChooseDisplayName_PrefersTheDevicesOwnName_OnlyWhenWindowsIsGeneric(string windows, string? product, string expected)
    {
        Assert.Equal(expected, UsbDeviceClassifier.ChooseDisplayName(new UsbHardwareId(0x1234, 0x0001), windows, product));
    }

    [Fact]
    public void ChooseDisplayName_FallsBackToADescriptiveSiblingName_WhenThereIsNoProductName()
    {
        // A webcam: the top-level node is "USB Composite Device", but a node beneath it is named properly.
        var name = UsbDeviceClassifier.ChooseDisplayName(
            new UsbHardwareId(0x1532, 0x0E03), "USB Composite Device", null, ["USB Composite Device", "Razer Kiyo", null, "USB Input Device"]);

        Assert.Equal("Razer Kiyo", name);
    }

    [Fact]
    public void ChooseDisplayName_UsesTheCatalogName_ForAKnownWheel()
    {
        Assert.Equal("Logitech G29", UsbDeviceClassifier.ChooseDisplayName(new UsbHardwareId(0x046D, 0xC24F), "USB Composite Device", "G29 Driving Force Racing Wheel"));
    }

    [Fact]
    public void Offerable_HidesBlockedAndAlreadySharedDevices_AndListsFlaggedOnesLast()
    {
        static UsbDeviceDetails Device(ushort pid, string name, ShareAdvice advice) =>
            new(new UsbDeviceInfo(new UsbHardwareId(0x1234, pid), name, $"USB\\VID_1234&PID_{pid:X4}\\1"), name, advice, null);

        var devices = new[]
        {
            Device(1, "Keyboard", ShareAdvice.ConfirmInput),
            Device(2, "USB Drive", ShareAdvice.Blocked),
            Device(3, "Pedals", ShareAdvice.Ok),
            Device(4, "Already added shifter", ShareAdvice.Ok),
            Device(5, "Button box", ShareAdvice.Ok),
        };

        var offered = UsbDeviceClassifier.Offerable(devices, new HashSet<UsbHardwareId> { new(0x1234, 4) });

        Assert.Equal(["Button box", "Pedals", "Keyboard"], offered.Select(d => d.DisplayName));
    }
}

public class MultiDeviceMessageTests
{
    private static BeaconDevice Device(string busId, string pid, string name, bool isWheel = false) =>
        new() { BusId = busId, VendorId = "046d", ProductId = pid, Name = name, IsWheel = isWheel };

    [Fact]
    public void Beacon_RoundTripsItsDeviceList()
    {
        var beacon = new BeaconMessage
        {
            HostName = "WHEEL-PC", BusId = "4-2", VendorId = "046d", ProductId = "c24f", DeviceName = "Logitech G29",
            Devices = [Device("4-2", "c24f", "Logitech G29", isWheel: true), Device("1-3", "c215", "Extreme 3D Pro")],
        };

        Assert.True(BeaconMessage.TryParse(beacon.ToJson(), out var parsed));
        var devices = parsed!.AllDevices();

        Assert.Equal(["4-2", "1-3"], devices.Select(d => d.BusId));
        Assert.True(devices[0].IsWheel);
        Assert.False(devices[1].IsWheel);
        Assert.Equal("046d:c215", devices[1].HardwareIdText);
    }

    [Fact]
    public void Beacon_FromAnOlderWheelPc_IsReadAsItsOneWheel()
    {
        const string oldJson = """{"protocolVersion":1,"hostName":"WHEEL-PC","busId":"4-2","vendorId":"046d","productId":"c24f","shared":true,"deviceName":"Logitech G29"}""";

        Assert.True(BeaconMessage.TryParse(oldJson, out var parsed));
        var device = Assert.Single(parsed!.AllDevices());

        Assert.Equal("4-2", device.BusId);
        Assert.Equal("Logitech G29", device.Name);
        Assert.True(device.IsWheel);
    }

    [Fact]
    public void Beacon_ForAnOlderGamePc_StillDescribesTheFirstDevice()
    {
        // An older Game PC ignores "devices" and reads only these fields.
        var beacon = new BeaconMessage
        {
            HostName = "WHEEL-PC", BusId = "4-2", VendorId = "046d", ProductId = "c24f",
            Devices = [Device("4-2", "c24f", "Logitech G29", isWheel: true), Device("1-3", "c215", "Extreme 3D Pro")],
        };

        Assert.Contains("\"busId\":\"4-2\"", beacon.ToJson());
    }

    [Fact]
    public void Ack_ReportsEachAttachedDevice()
    {
        var ack = new AckMessage { HostName = "GAME-PC", Attached = true, AttachedBusIds = ["4-2"] };
        Assert.True(AckMessage.TryParse(ack.ToJson(), out var parsed));

        Assert.True(parsed!.HasAttached("4-2", isPrimary: true));
        Assert.False(parsed.HasAttached("1-3", isPrimary: false));
    }

    [Fact]
    public void Ack_FromAnOlderGamePc_OnlySpeaksForTheFirstDevice()
    {
        Assert.True(AckMessage.TryParse("""{"protocolVersion":1,"hostName":"GAME-PC","attached":true}""", out var old));

        Assert.True(old!.HasAttached("4-2", isPrimary: true));
        Assert.False(old.HasAttached("1-3", isPrimary: false)); // it never attached the extra one
    }
}

public class AttachPlannerTests
{
    private static readonly BeaconDevice Wheel = new() { BusId = "4-2", VendorId = "046d", ProductId = "c24f", Name = "Logitech G29", IsWheel = true };
    private static readonly BeaconDevice Pedals = new() { BusId = "1-3", VendorId = "0eb7", ProductId = "183b", Name = "Pedals" };
    private static readonly BeaconDevice Keyboard = new() { BusId = "2-1", VendorId = "1234", ProductId = "0001", Name = "Keyboard" };

    private static AttachPlan Plan(
        IReadOnlyList<BeaconDevice> offered, Dictionary<string, int>? attached = null, int[]? livePorts = null,
        string[]? released = null, string[]? coolingDown = null) =>
        AttachPlanner.Plan(
            offered, attached ?? [], livePorts,
            id => (released ?? []).Contains(id), busId => (coolingDown ?? []).Contains(busId));

    [Fact]
    public void AttachesEverythingOffered_WhenNothingIsAttached()
    {
        var plan = Plan([Wheel, Pedals]);

        Assert.Equal([Wheel, Pedals], plan.Attach);
        Assert.Empty(plan.Detach);
        Assert.Empty(plan.Forget);
    }

    [Fact]
    public void DoesNothing_WhenEverythingOfferedIsAlreadyAttachedAndAlive()
    {
        var plan = Plan([Wheel, Pedals], new() { ["4-2"] = 1, ["1-3"] = 2 }, livePorts: [1, 2]);

        Assert.Empty(plan.Attach);
        Assert.Empty(plan.Detach);
        Assert.Empty(plan.Forget);
    }

    [Fact]
    public void AttachesOnlyTheNewDevice_WhenOneIsAddedOnTheWheelPc()
    {
        var plan = Plan([Wheel, Pedals], new() { ["4-2"] = 1 }, livePorts: [1]);

        Assert.Equal([Pedals], plan.Attach);
        Assert.Empty(plan.Detach);
    }

    [Fact]
    public void DetachesADevice_ThatIsNoLongerOffered()
    {
        // Removed on the Wheel PC, or unplugged.
        var plan = Plan([Wheel], new() { ["4-2"] = 1, ["1-3"] = 2 }, livePorts: [1, 2]);

        Assert.Equal(["1-3"], plan.Detach);
        Assert.Empty(plan.Attach);
    }

    [Fact]
    public void AReleasedDevice_IsDetachedAndNeverAttached()
    {
        // The escape hatch for a keyboard added by mistake: switched off on the Game PC.
        var whileAttached = Plan([Wheel, Keyboard], new() { ["4-2"] = 1, ["2-1"] = 2 }, livePorts: [1, 2], released: ["1234:0001"]);
        Assert.Equal(["2-1"], whileAttached.Detach);
        Assert.Empty(whileAttached.Attach);

        var afterwards = Plan([Wheel, Keyboard], new() { ["4-2"] = 1 }, livePorts: [1], released: ["1234:0001"]);
        Assert.Empty(afterwards.Detach);
        Assert.Empty(afterwards.Attach);
    }

    [Fact]
    public void AnAttachmentThatSilentlyDied_IsForgottenAndReattached()
    {
        var plan = Plan([Wheel, Pedals], new() { ["4-2"] = 1, ["1-3"] = 2 }, livePorts: [2]);

        Assert.Equal(["4-2"], plan.Forget);
        Assert.Equal([Wheel], plan.Attach);
        Assert.Empty(plan.Detach);
    }

    [Fact]
    public void NothingIsAssumedDead_WhenThePortListCouldNotBeRead()
    {
        var plan = Plan([Wheel], new() { ["4-2"] = 1 }, livePorts: null);

        Assert.Empty(plan.Forget);
        Assert.Empty(plan.Attach);
    }

    [Fact]
    public void ADeviceThatJustFailed_IsNotRetriedYet_ButOthersStillAttach()
    {
        var plan = Plan([Wheel, Pedals], coolingDown: ["1-3"]);

        Assert.Equal([Wheel], plan.Attach);
    }

    [Fact]
    public void ABusIdChange_DetachesTheOldAndAttachesTheNew()
    {
        var moved = new BeaconDevice { BusId = "5-1", VendorId = "046d", ProductId = "c24f", Name = "Logitech G29", IsWheel = true };

        var plan = Plan([moved], new() { ["4-2"] = 1 }, livePorts: [1]);

        Assert.Equal(["4-2"], plan.Detach);
        Assert.Equal([moved], plan.Attach);
    }
}

public class MultiDeviceDashboardTests
{
    private static readonly GHubStatus GHubFine = new(RunningHere: true, InstalledHere: true, RunningOnPeer: true, PeerName: "GAME-PC");

    private static WheelSideInputs WheelConnected => new(
        WheelName: "Logitech G29", WheelPresent: true, Shared: true, SharingAllowed: true,
        Problem: null, GamePcName: "GAME-PC", GamePcAttached: true);

    [Fact]
    public void WheelPc_ListsEachExtraDevice_WithItsOwnState()
    {
        var state = WheelSideDashboard.Build(
            WheelConnected with
            {
                Extras = [new("Pedals", Present: true, Attached: true), new("Stream Deck", Present: true, Attached: false), new("Handbrake", Present: false, Attached: false)],
            },
            GHubFine);

        Assert.Equal(StepState.Done, state.Steps.Single(s => s.Title == "Pedals").State);
        Assert.Equal(StepState.Active, state.Steps.Single(s => s.Title == "Stream Deck").State);
        Assert.Equal("Not plugged in", state.Steps.Single(s => s.Title == "Handbrake").Detail);
        Assert.Equal("Connected", state.Steps.Single(s => s.Title == "Logitech G29").Detail);
        Assert.Equal("Wheel connected", state.Headline); // an extra still connecting doesn't spoil the wheel
        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Contains("and 1 other device", state.Detail);
    }

    [Fact]
    public void WheelPc_WithOnlyExtraDevices_DoesNotAskForAWheelOrMentionGHub()
    {
        // A HOTAS or Stream Deck user with no racing wheel at all.
        var state = WheelSideDashboard.Build(
            new WheelSideInputs(null, WheelPresent: false, Shared: false, SharingAllowed: true, Problem: null, GamePcName: "GAME-PC", GamePcAttached: false)
            {
                Extras = [new("T.16000M Joystick", Present: true, Attached: true)],
            },
            gHub: null);

        Assert.Equal("T.16000M Joystick connected", state.Headline);
        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Equal(["T.16000M Joystick", "Shared on your network", "Game PC"], state.Steps.Select(s => s.Title));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void WheelPc_WaitsForAnExtraDevice_WhenNothingIsPluggedIn()
    {
        var state = WheelSideDashboard.Build(
            new WheelSideInputs(null, false, false, true, null, null, false) { Extras = [new("Stream Deck", Present: false, Attached: false)] },
            gHub: null);

        Assert.Equal("Waiting for your Stream Deck", state.Headline);
    }

    [Fact]
    public void GamePc_ListsEveryOfferedDevice()
    {
        var state = GameSideDashboard.Build(
            new GameSideInputs("WHEEL-PC", "Logitech G29", Attached: true, Problem: null)
            {
                Devices =
                [
                    new("Logitech G29", IsWheel: true, OfferedDeviceState.Connected),
                    new("Pedals", IsWheel: false, OfferedDeviceState.Connected),
                    new("Stream Deck", IsWheel: false, OfferedDeviceState.Connecting),
                ],
            },
            GHubFine);

        Assert.Equal("Wheel connected", state.Headline);
        Assert.Contains("and 1 other device", state.Detail);
        Assert.Equal(StepState.Done, state.Steps.Single(s => s.Title == "Pedals").State);
        Assert.Equal(StepState.Active, state.Steps.Single(s => s.Title == "Stream Deck").State);
    }

    [Fact]
    public void GamePc_AnExtraDeviceFailing_IsAWarning_NotAnError()
    {
        var state = GameSideDashboard.Build(
            new GameSideInputs("WHEEL-PC", "Logitech G29", Attached: true, Problem: null)
            {
                Devices =
                [
                    new("Logitech G29", IsWheel: true, OfferedDeviceState.Connected),
                    new("Stream Deck", IsWheel: false, OfferedDeviceState.Failed, "Stream Deck couldn't be attached."),
                ],
            },
            GHubFine);

        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        var warning = Assert.Single(state.Warnings);
        Assert.Equal("Stream Deck couldn't connect", warning.Title);
        Assert.Equal(StepState.Problem, state.Steps.Single(s => s.Title == "Stream Deck").State);
    }

    [Fact]
    public void GamePc_TheWheelFailing_IsStillAnError()
    {
        var state = GameSideDashboard.Build(
            new GameSideInputs("WHEEL-PC", "Logitech G29", Attached: false, Problem: "nope")
            {
                Devices = [new("Logitech G29", IsWheel: true, OfferedDeviceState.Failed, "nope"), new("Pedals", IsWheel: false, OfferedDeviceState.Connected)],
            },
            GHubFine);

        Assert.Equal(TrayStatusLevel.Error, state.Level);
        Assert.Equal("Couldn't connect the wheel", state.Headline);
    }

    [Fact]
    public void GamePc_AReleasedDevice_IsShownAsNotUsedHere_AndDoesNotCountAsMissing()
    {
        var state = GameSideDashboard.Build(
            new GameSideInputs("WHEEL-PC", "Logitech G29", Attached: true, Problem: null)
            {
                Devices = [new("Logitech G29", IsWheel: true, OfferedDeviceState.Connected), new("Keyboard", IsWheel: false, OfferedDeviceState.Released)],
            },
            GHubFine);

        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Equal("Not used on this PC", state.Steps.Single(s => s.Title == "Keyboard").Detail);
        Assert.DoesNotContain("other device", state.Detail);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void GamePc_WithOnlyExtraDevices_NamesTheDevice_AndSkipsGHub()
    {
        var state = GameSideDashboard.Build(
            new GameSideInputs("WHEEL-PC", null, Attached: false, Problem: null)
            {
                Devices = [new("Stream Deck XL", IsWheel: false, OfferedDeviceState.Connected)],
            },
            gHub: null);

        Assert.Equal("Stream Deck XL connected", state.Headline);
        Assert.DoesNotContain(state.Steps, s => s.Title == "Logitech G HUB");
    }

    [Fact]
    public void ExtraDeviceList_RoundTripsThroughItsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"logisteam-{Guid.NewGuid():N}");
        try
        {
            var store = DeviceStores.ExtraDevices(directory);
            Assert.Empty(store.Load());

            store.Save([new ExtraDevice { DisplayName = "Pedals", HardwareId = new UsbHardwareId(0x0EB7, 0x183B) }]);

            var loaded = Assert.Single(store.Load());
            Assert.Equal("Pedals", loaded.DisplayName);
            Assert.Equal(new UsbHardwareId(0x0EB7, 0x183B), loaded.HardwareId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
