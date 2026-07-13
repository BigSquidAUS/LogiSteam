using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Built-in catalog of wheel profiles LogiSteam can auto-detect on first run, before ever
/// falling back to the manual "Select USB Device..." picker. Add an entry here only once a
/// persona has been confirmed directly against real hardware (Device Manager / WMI/`usbipd
/// state`), never from a third-party VID/PID lookup site -- that mistake has already cost real
/// debugging time once (see <see cref="UsbHardwareId.G29ProductId"/>'s remarks).
/// </summary>
public static class KnownWheelProfiles
{
    /// <summary>
    /// Logitech G29. Three personas confirmed directly on one real unit (same physical USB port
    /// -- identical InstanceId suffix -- across all three, only the VID:PID prefix differs):
    /// PS3 mode, PS4 mode, and a third "native"/generic mode seen before any mode switch.
    /// </summary>
    public static readonly KnownWheelProfile G29 = new()
    {
        DisplayName = "Logitech G29",
        HardwareIds =
        [
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC24F), // PS3 mode
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC260), // PS4 mode
            new UsbHardwareId(UsbHardwareId.LogitechVendorId, 0xC52B), // native/generic mode
        ],
    };

    public static readonly IReadOnlyList<KnownWheelProfile> All = [G29];

    /// <summary>
    /// Looks up a known model's clean display name ("Logitech G29") for a hardware id, so UI that
    /// only has raw Windows device info (like the manual device picker) can still show a
    /// recognizable name instead of whatever confusing string Windows reports for that persona.
    /// </summary>
    public static bool TryGetDisplayName(UsbHardwareId id, out string? displayName)
    {
        var profile = All.FirstOrDefault(p => p.HardwareIds.Contains(id));
        displayName = profile?.DisplayName;
        return profile is not null;
    }
}
