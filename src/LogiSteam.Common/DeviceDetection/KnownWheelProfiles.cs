using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Built-in catalog of racing wheels LogiSteam identifies automatically, with no device list
/// ever shown to the user. Each profile lists every USB persona the model can enumerate as --
/// the G29, for one, changes VID:PID with its PS3/PS4 switch, so a single id per model is not
/// always enough.
///
/// Only add an id that is confirmed: either observed on a real unit (the G29's two personas)
/// or listed in the Linux kernel's Logitech wheel drivers (drivers/hid/hid-ids.h, used by
/// hid-lg4ff and hid-logitech-hidpp), which is where every other id here comes from. Never use
/// a VID/PID lookup site. A wheel that isn't listed still has two ways in: the name-based
/// fallback in <see cref="WheelAutoDetector"/>, and the manual choice under Advanced.
/// </summary>
public static class KnownWheelProfiles
{
    private static KnownWheelProfile Profile(string displayName, params ushort[] productIds) => new()
    {
        DisplayName = displayName,
        HardwareIds = productIds.Select(id => new UsbHardwareId(UsbHardwareId.LogitechVendorId, id)).ToArray(),
    };

    /// <summary>
    /// Logitech G29: PS3 mode (0xC24F) and PS4 mode (0xC260), both confirmed on one real unit.
    /// 0xC52B used to be listed here as a third "native" persona, but that id is Logitech's
    /// Unifying wireless receiver (USB_DEVICE_ID_LOGITECH_UNIFYING_RECEIVER in the kernel's
    /// hid-ids.h) -- with it in the catalog, any PC with a Logitech mouse/keyboard dongle
    /// plugged in could have that dongle identified, and shared, as "the G29".
    /// </summary>
    public static readonly KnownWheelProfile G29 = Profile("Logitech G29", 0xC24F, 0xC260);

    public static readonly KnownWheelProfile G920 = Profile("Logitech G920", 0xC262);
    public static readonly KnownWheelProfile G923 = Profile("Logitech G923", 0xC26E);
    public static readonly KnownWheelProfile G27 = Profile("Logitech G27", 0xC29B);
    public static readonly KnownWheelProfile G25 = Profile("Logitech G25", 0xC299);
    public static readonly KnownWheelProfile DrivingForceGt = Profile("Logitech Driving Force GT", 0xC29A);
    public static readonly KnownWheelProfile DrivingForcePro = Profile("Logitech Driving Force Pro", 0xC298);
    public static readonly KnownWheelProfile DrivingForce = Profile("Logitech Driving Force", 0xC294);
    public static readonly KnownWheelProfile MomoRacing = Profile("Logitech MOMO Racing", 0xC295, 0xCA03);

    public static readonly IReadOnlyList<KnownWheelProfile> All =
    [
        G29, G920, G923, G27, G25, DrivingForceGt, DrivingForcePro, DrivingForce, MomoRacing,
    ];

    /// <summary>The catalog profile that lists this hardware id as one of its personas, if any.</summary>
    public static KnownWheelProfile? Find(UsbHardwareId id) =>
        All.FirstOrDefault(p => p.HardwareIds.Contains(id));

    /// <summary>
    /// Looks up a known model's clean display name ("Logitech G29") for a hardware id, so UI that
    /// only has raw Windows device info can still show a recognizable name instead of whatever
    /// confusing string Windows reports for that persona.
    /// </summary>
    public static bool TryGetDisplayName(UsbHardwareId id, out string? displayName)
    {
        var profile = Find(id);
        displayName = profile?.DisplayName;
        return profile is not null;
    }
}
