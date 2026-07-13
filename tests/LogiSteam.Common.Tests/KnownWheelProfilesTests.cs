using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class KnownWheelProfilesTests
{
    [Fact]
    public void G29Profile_ContainsExactlyTheThreeConfirmedPersonas()
    {
        // Regression guard: if someone "cleans up" this list later thinking a persona is a
        // duplicate/mistake, this test fails loudly and forces them to re-read why all three are
        // independently confirmed on one real unit (PS3 mode, PS4 mode, native/generic mode).
        var expected = new[]
        {
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC24F),
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC260),
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC52B),
        };

        Assert.Equal(expected, KnownWheelProfiles.G29.HardwareIds);
    }

    [Fact]
    public void G29Profile_HasNonEmptyDisplayName()
    {
        Assert.False(string.IsNullOrWhiteSpace(KnownWheelProfiles.G29.DisplayName));
    }

    [Fact]
    public void All_ContainsG29()
    {
        Assert.Contains(KnownWheelProfiles.G29, KnownWheelProfiles.All);
    }

    [Fact]
    public void All_ContainsNoDuplicateHardwareIdsAcrossProfiles()
    {
        // Future-proofing for when a second profile is added: a hardware id claimed by two
        // profiles would make WheelAutoDetector's first-match result ambiguous/non-deterministic.
        var allIds = KnownWheelProfiles.All.SelectMany(p => p.HardwareIds).ToList();

        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void TryGetDisplayName_ReturnsTrueAndName_ForAKnownPersona()
    {
        var found = KnownWheelProfiles.TryGetDisplayName(new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC52B), out var displayName);

        Assert.True(found);
        Assert.Equal("Logitech G29", displayName);
    }

    [Fact]
    public void TryGetDisplayName_ReturnsFalseAndNull_ForAnUnrecognizedDevice()
    {
        var found = KnownWheelProfiles.TryGetDisplayName(new UsbHardwareId(0x1234, 0x5678), out var displayName);

        Assert.False(found);
        Assert.Null(displayName);
    }
}
