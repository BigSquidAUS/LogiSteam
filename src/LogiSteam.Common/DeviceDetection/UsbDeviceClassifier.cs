using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>Whether a USB device is a sensible thing to add as an additional shared device.</summary>
public enum ShareAdvice
{
    /// <summary>Fine to share: game controllers, button boxes, and anything not flagged below.</summary>
    Ok,

    /// <summary>
    /// A keyboard or mouse. Sharing it takes it away from the Wheel PC -- usually the PC the
    /// user is sitting at -- so it needs an explicit "yes, I mean it". Not blocked outright:
    /// plenty of button boxes and macro pads present themselves as keyboards.
    /// </summary>
    ConfirmInput,

    /// <summary>Audio or video. Works over USB/IP in principle but copes badly with network delay.</summary>
    CautionMedia,

    /// <summary>Never offered: sharing it would break the Wheel PC or the connection itself.</summary>
    Blocked,
}

/// <summary>A plugged-in USB device as shown in the "add another USB device" list.</summary>
/// <param name="DisplayName">The best available name: the device's own product name where Windows only has a generic one.</param>
/// <param name="AdviceText">Why it is flagged, in plain words; null for <see cref="ShareAdvice.Ok"/>.</param>
public sealed record UsbDeviceDetails(UsbDeviceInfo Info, string DisplayName, ShareAdvice Advice, string? AdviceText)
{
    public UsbHardwareId HardwareId => Info.HardwareId;
}

/// <summary>
/// Pure rules for <see cref="ShareAdvice"/>, from the Windows device classes and driver services
/// found on a physical USB device (its own PnP node plus the HID/function nodes beneath it).
/// </summary>
public static class UsbDeviceClassifier
{
    public static (ShareAdvice Advice, string? Reason) Classify(IEnumerable<string?> pnpClasses, IEnumerable<string?> services)
    {
        var classes = pnpClasses.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drivers = services.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();

        if (drivers.Any(s => s.StartsWith("usbhub", StringComparison.OrdinalIgnoreCase)))
        {
            return (ShareAdvice.Blocked, "USB hubs can't be shared. Add the device plugged into the hub instead.");
        }

        if (drivers.Any(s => s.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) || s.Equals("UASPStor", StringComparison.OrdinalIgnoreCase))
            || classes.Overlaps(["DiskDrive", "CDROM", "SCSIAdapter", "WPD"]))
        {
            return (ShareAdvice.Blocked, "Storage drives aren't shared: pulling one away from Windows mid-use risks losing data.");
        }

        if (classes.Contains("Net"))
        {
            return (ShareAdvice.Blocked, "Network adapters aren't shared: it could cut the very connection LogiSteam uses.");
        }

        if (classes.Contains("Bluetooth"))
        {
            return (ShareAdvice.Blocked, "Bluetooth adapters aren't shared: every Bluetooth device on this PC would go with it.");
        }

        if (classes.Overlaps(["Keyboard", "Mouse"]))
        {
            return (ShareAdvice.ConfirmInput, "This acts as a keyboard or mouse. While it's connected to the Game PC it will not work on this PC.");
        }

        if (classes.Overlaps(["MEDIA", "AudioEndpoint", "Camera", "Image"]))
        {
            return (ShareAdvice.CautionMedia, "Audio and video devices often stutter or fail when shared over the network.");
        }

        return (ShareAdvice.Ok, null);
    }

    /// <summary>
    /// Which devices the "add another USB device" list offers, and in what order: blocked ones
    /// and anything already shared are left out; ordinary devices come first, flagged ones last.
    /// </summary>
    public static IReadOnlyList<UsbDeviceDetails> Offerable(IEnumerable<UsbDeviceDetails> devices, IReadOnlySet<UsbHardwareId> alreadyShared) =>
        devices
            .Where(d => d.Advice != ShareAdvice.Blocked && !alreadyShared.Contains(d.HardwareId))
            .OrderBy(d => d.Advice)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Windows' own name where it's descriptive, the device's USB product name where Windows
    /// only offers something generic ("USB Input Device", "USB Composite Device").
    /// </summary>
    /// <param name="otherDescriptions">Windows' names for the device's other PnP nodes; one of them is often the only descriptive one.</param>
    public static string ChooseDisplayName(
        UsbHardwareId id, string windowsDescription, string? usbProductName, IEnumerable<string?>? otherDescriptions = null)
    {
        if (KnownWheelProfiles.TryGetDisplayName(id, out var known) && known is not null)
        {
            return known;
        }

        if (!IsGenericName(windowsDescription))
        {
            return windowsDescription;
        }

        if (!string.IsNullOrWhiteSpace(usbProductName))
        {
            return usbProductName.Trim();
        }

        return (otherDescriptions ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name) && !IsGenericName(name!))
            .OrderByDescending(name => name!.Length)
            .FirstOrDefault() ?? windowsDescription;
    }

    /// <summary>Names Windows gives a device when it knows nothing specific about it.</summary>
    private static bool IsGenericName(string name) =>
        name.StartsWith("USB ", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("HID", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase);
}
