using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class KnownWheelProfilesTests
{
    private static UsbHardwareId Logitech(ushort productId) => new(UsbHardwareId.LogitechVendorId, productId);

    [Fact]
    public void G29Profile_ContainsExactlyItsTwoConfirmedPersonas()
    {
        // PS3 mode and PS4 mode, both confirmed on a real unit.
        Assert.Equal([Logitech(0xC24F), Logitech(0xC260)], KnownWheelProfiles.G29.HardwareIds);
    }

    [Fact]
    public void Catalog_DoesNotTreatTheUnifyingReceiverAsAWheel()
    {
        // Regression guard: 046d:c52b is Logitech's Unifying wireless receiver (a mouse/keyboard
        // dongle). It was once listed as a G29 "native mode" persona, which let LogiSteam pick
        // and share somebody's mouse dongle as "the wheel". Do not add it back.
        Assert.Null(KnownWheelProfiles.Find(Logitech(0xC52B)));
        Assert.Null(KnownWheelProfiles.Find(Logitech(0xC532)));
    }

    [Theory]
    [InlineData(0xC24F, "Logitech G29")]
    [InlineData(0xC260, "Logitech G29")]
    [InlineData(0xC262, "Logitech G920")]
    [InlineData(0xC26E, "Logitech G923")]
    [InlineData(0xC29B, "Logitech G27")]
    [InlineData(0xC299, "Logitech G25")]
    [InlineData(0xC29A, "Logitech Driving Force GT")]
    public void TryGetDisplayName_ReturnsTheModelName_ForAKnownPersona(int productId, string expectedName)
    {
        var found = KnownWheelProfiles.TryGetDisplayName(Logitech((ushort)productId), out var displayName);

        Assert.True(found);
        Assert.Equal(expectedName, displayName);
    }

    [Fact]
    public void TryGetDisplayName_ReturnsFalseAndNull_ForAnUnrecognizedDevice()
    {
        var found = KnownWheelProfiles.TryGetDisplayName(new UsbHardwareId(0x1234, 0x5678), out var displayName);

        Assert.False(found);
        Assert.Null(displayName);
    }

    [Fact]
    public void All_ProfilesHaveANameAndAtLeastOneHardwareId()
    {
        Assert.All(KnownWheelProfiles.All, profile =>
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.DisplayName));
            Assert.NotEmpty(profile.HardwareIds);
        });
    }

    [Fact]
    public void All_ContainsNoDuplicateHardwareIdsAcrossProfiles()
    {
        // A hardware id claimed by two profiles would make WheelAutoDetector's first-match
        // result depend on catalog order.
        var allIds = KnownWheelProfiles.All.SelectMany(p => p.HardwareIds).ToList();

        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }
}
