using System.Text.RegularExpressions;

namespace LogiSteam.Common.UsbipInterop;

/// <summary>A USB Vendor ID / Product ID pair, formatted the same way usbipd-win and usbip-win2 do: "vvvv:pppp" lowercase.</summary>
public readonly record struct UsbHardwareId(ushort Vendor, ushort Product)
{
    /// <summary>Logitech's USB Vendor ID.</summary>
    public const ushort LogitechVendorId = 0x046D;

    /// <summary>
    /// One confirmed Product ID for the G29 wheel base -- its "native"/generic mode, where it
    /// shows up as "Logitech USB Input Device" (VID_046D&amp;PID_C52B) in Device Manager. This is
    /// NOT the only valid G29 persona: the wheel has a physical PS3/PS4 mode switch, and each
    /// position makes the exact same physical unit enumerate under a completely different
    /// PID (0xC24F for PS3 mode, 0xC260 for PS4 mode -- both independently confirmed on a real
    /// unit, same physical USB port). See <see cref="DeviceDetection.KnownWheelProfiles.G29"/>
    /// for the full, authoritative multi-persona list used for auto-detection; this single
    /// constant is kept only as a convenient one-id fixture (e.g. for tests). The one real
    /// mistake in this area's history was trusting a PID from a third-party lookup site instead
    /// of a real device -- always confirm a new persona against Device Manager/`usbipd state`
    /// before adding it anywhere, never from an online database.
    /// </summary>
    public const ushort G29ProductId = 0xC52B;

    public static readonly UsbHardwareId G29 = new(LogitechVendorId, G29ProductId);

    private static readonly Regex InstanceIdPattern = new(
        @"VID_(?<vid>[0-9a-fA-F]{4})&PID_(?<pid>[0-9a-fA-F]{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColonPairPattern = new(
        @"^(?<vid>[0-9a-fA-F]{4}):(?<pid>[0-9a-fA-F]{4})$",
        RegexOptions.Compiled);

    public override string ToString() => $"{Vendor:x4}:{Product:x4}";

    /// <summary>Parses a Windows PnP instance id, e.g. "USB\VID_046D&PID_C52B&REV_2411&MI_00".</summary>
    public static bool TryParseFromInstanceId(string instanceId, out UsbHardwareId hardwareId)
    {
        var match = InstanceIdPattern.Match(instanceId);
        if (!match.Success)
        {
            hardwareId = default;
            return false;
        }

        hardwareId = new UsbHardwareId(
            Convert.ToUInt16(match.Groups["vid"].Value, 16),
            Convert.ToUInt16(match.Groups["pid"].Value, 16));
        return true;
    }

    /// <summary>Parses the "vvvv:pppp" form used in usbip.exe/usbipd.exe CLI output and arguments.</summary>
    public static bool TryParse(string text, out UsbHardwareId hardwareId)
    {
        var match = ColonPairPattern.Match(text.Trim());
        if (!match.Success)
        {
            hardwareId = default;
            return false;
        }

        hardwareId = new UsbHardwareId(
            Convert.ToUInt16(match.Groups["vid"].Value, 16),
            Convert.ToUInt16(match.Groups["pid"].Value, 16));
        return true;
    }
}
