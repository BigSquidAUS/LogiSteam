using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Config;

/// <summary>
/// The wheel the user is using, persisted across restarts -- either a manual pick (exactly one
/// hardware id) or an auto-detected known profile (every confirmed persona for that model, e.g.
/// the G29's PS3/PS4/native-mode personas), so a mode switch or usbipd-win's bind-time driver
/// swap never leaves the saved selection matching nothing live.
/// </summary>
public sealed record SelectedDevice
{
    /// <summary>Stable label for tray/status text -- a profile's clean name ("Logitech G29") or,
    /// for a manual pick, whatever raw Windows description that device reported at pick time.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Every hardware id that counts as "this wheel". One entry for a manual pick, one
    /// per known persona for an auto-detected profile.</summary>
    public required IReadOnlyList<UsbHardwareId> HardwareIds { get; init; }

    public bool Matches(UsbHardwareId hardwareId) => HardwareIds.Contains(hardwareId);

    /// <summary>
    /// Builds a selection from a manual pick. If the picked device's hardware id matches a known
    /// wheel profile, upgrades immediately to that profile's full multi-persona id set rather than
    /// persisting just the one id picked today -- so a future mode switch on a recognized wheel
    /// never strands the connection the way a bare single-id manual pick used to.
    /// </summary>
    public static SelectedDevice From(UsbDeviceInfo device)
    {
        var knownProfile = KnownWheelProfiles.All.FirstOrDefault(p => p.HardwareIds.Contains(device.HardwareId));
        if (knownProfile is not null)
        {
            return FromProfile(knownProfile);
        }

        return new()
        {
            DisplayName = device.Description,
            HardwareIds = [device.HardwareId],
        };
    }

    public static SelectedDevice FromProfile(KnownWheelProfile profile) => new()
    {
        DisplayName = profile.DisplayName,
        HardwareIds = profile.HardwareIds,
    };
}
