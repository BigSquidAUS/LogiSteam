using System.Text.RegularExpressions;

namespace LogiSteam.Common.UsbipInterop;

/// <summary>A USB Vendor ID / Product ID pair, formatted the same way usbipd-win and usbip-win2 do: "vvvv:pppp" lowercase.</summary>
public readonly record struct UsbHardwareId(ushort Vendor, ushort Product)
{
    /// <summary>Logitech's USB Vendor ID.</summary>
    public const ushort LogitechVendorId = 0x046D;

    /// <summary>
    /// Kept only as a one-id fixture for tests. Despite the name, 0xC52B is not a G29: it is
    /// Logitech's Unifying wireless receiver ("Logitech USB Input Device" in Device Manager),
    /// which an early version mistook for the wheel. The G29's real personas (0xC24F in PS3
    /// mode, 0xC260 in PS4 mode) live in <see cref="DeviceDetection.KnownWheelProfiles.G29"/>,
    /// and nothing uses this constant to identify a wheel.
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
