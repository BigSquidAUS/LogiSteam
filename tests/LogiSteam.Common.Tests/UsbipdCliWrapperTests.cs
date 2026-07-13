using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbipdCliWrapperTests
{
    private static UsbipdDevice Device(string? busId, ushort vendor, ushort product, Guid? persistedGuid = null) => new()
    {
        BusId = busId,
        InstanceId = $"USB\\VID_{vendor:X4}&PID_{product:X4}\\1",
        PersistedGuid = persistedGuid,
    };

    private static readonly UsbHardwareId Ps3Mode = new(0x046D, 0xC24F);
    private static readonly UsbHardwareId Ps4Mode = new(0x046D, 0xC260);
    private static readonly UsbHardwareId Unrelated = new(0x1234, 0x5678);

    [Fact]
    public void SelectBestMatch_ReturnsNull_WhenNoDeviceMatchesAnyCandidate()
    {
        var devices = new[] { Device("1-1", Unrelated.Vendor, Unrelated.Product) };

        Assert.Null(UsbipdCliWrapper.SelectBestMatch(devices, [Ps3Mode, Ps4Mode]));
    }

    [Fact]
    public void SelectBestMatch_ReturnsTheMatchingDevice_WhenExactlyOneCandidateMatches()
    {
        var target = Device("3-1", Ps3Mode.Vendor, Ps3Mode.Product);
        var devices = new[] { Device("1-1", Unrelated.Vendor, Unrelated.Product), target };

        var result = UsbipdCliWrapper.SelectBestMatch(devices, [Ps3Mode, Ps4Mode]);

        Assert.Equal(target, result);
    }

    [Fact]
    public void SelectBestMatch_PrefersLiveBusId_WhenTwoCandidatesBothMatchButOnlyOneHasABusId()
    {
        // Simulates exactly the scenario observed with the real G29: after a mode switch, the
        // previous persona's usbipd-win record lingers with a PersistedGuid but no live BusId,
        // while the new persona is the one actually connected right now.
        var stale = Device(busId: null, Ps4Mode.Vendor, Ps4Mode.Product, persistedGuid: Guid.NewGuid());
        var live = Device("3-1", Ps3Mode.Vendor, Ps3Mode.Product, persistedGuid: Guid.NewGuid());
        var devices = new[] { stale, live };

        var result = UsbipdCliWrapper.SelectBestMatch(devices, [Ps3Mode, Ps4Mode]);

        Assert.Equal(live, result);
        Assert.NotNull(result!.BusId);
    }

    [Fact]
    public void SelectBestMatch_ReturnsNull_ForEmptyCandidateList()
    {
        var devices = new[] { Device("3-1", Ps3Mode.Vendor, Ps3Mode.Product) };

        Assert.Null(UsbipdCliWrapper.SelectBestMatch(devices, []));
    }

    [Fact]
    public void SelectBestMatch_ReturnsNull_ForEmptyDeviceList()
    {
        Assert.Null(UsbipdCliWrapper.SelectBestMatch([], [Ps3Mode]));
    }
}
