using LogiSteam.Common.Config;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Pure matcher that decides which plugged-in USB device is the racing wheel, so the user is
/// never shown a list of devices to choose from. Takes already-enumerated device data rather
/// than calling WMI/HID itself, so it's trivially unit-testable.
/// </summary>
public static class WheelAutoDetector
{
    private static readonly string[] WheelWords = ["racing", "wheel", "driving force"];

    // "USB Wheel Mouse" is a real Logitech product name -- a wheel word alone isn't enough.
    private static readonly string[] NotAWheelWords = ["mouse", "keyboard", "receiver", "headset", "webcam", "camera"];

    public static KnownWheelProfile? DetectFirstMatch(
        IReadOnlyList<UsbDeviceInfo> liveDevices, IReadOnlyList<KnownWheelProfile>? catalog = null)
    {
        catalog ??= KnownWheelProfiles.All;
        var liveIds = liveDevices.Select(d => d.HardwareId).ToHashSet();
        return catalog.FirstOrDefault(profile => profile.HardwareIds.Any(liveIds.Contains));
    }

    /// <summary>True for a product name that reads like a racing wheel ("G29 Driving Force Racing Wheel").</summary>
    public static bool LooksLikeWheelName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && WheelWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
        && !NotAWheelWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Identifies the wheel among the currently-plugged-in devices: a catalog model first, then
    /// any Logitech device whose product name reads like a racing wheel (covers models the
    /// catalog doesn't list). <paramref name="productNames"/> is the device's own USB product
    /// string where known -- Windows' generic description ("USB Input Device") often says
    /// nothing useful.
    /// </summary>
    public static SelectedDevice? Detect(
        IReadOnlyList<UsbDeviceInfo> liveDevices,
        IReadOnlyDictionary<UsbHardwareId, string>? productNames = null,
        IReadOnlyList<KnownWheelProfile>? catalog = null)
    {
        var profile = DetectFirstMatch(liveDevices, catalog);
        if (profile is not null)
        {
            return SelectedDevice.FromProfile(profile);
        }

        foreach (var device in liveDevices.Where(d => d.HardwareId.Vendor == UsbHardwareId.LogitechVendorId))
        {
            string? productName = null;
            productNames?.TryGetValue(device.HardwareId, out productName);
            var name = LooksLikeWheelName(productName) ? productName
                : LooksLikeWheelName(device.Description) ? device.Description
                : null;
            if (name is not null)
            {
                name = name.Trim();
                return new SelectedDevice
                {
                    DisplayName = name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? name : $"Logitech {name}",
                    HardwareIds = [device.HardwareId],
                    Source = WheelSelectionSource.NameMatch,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the persisted <see cref="SelectedDevice"/> honest against reality on every refresh
    /// tick. A manual choice (Advanced) is never touched. Anything else is only ever a wheel
    /// LogiSteam itself recognizes: a saved selection is refreshed to its catalog profile's
    /// current id set, replaced when a different known wheel is plugged in instead, and dropped
    /// outright if it isn't a recognizable wheel at all (a leftover pick from the old manual
    /// device list). A recognized wheel that's merely not visible right now is kept -- it may be
    /// unplugged, or bound to usbipd-win's stub driver, which hides its normal PnP entry.
    /// Returns <paramref name="current"/> itself (same reference) when nothing changed.
    /// </summary>
    public static SelectedDevice? ReconcileSelection(
        SelectedDevice? current,
        IReadOnlyList<UsbDeviceInfo> liveDevices,
        IReadOnlyDictionary<UsbHardwareId, string>? productNames = null,
        IReadOnlyList<KnownWheelProfile>? catalog = null)
    {
        if (current?.Source == WheelSelectionSource.Manual)
        {
            return current;
        }

        catalog ??= KnownWheelProfiles.All;
        var detected = Detect(liveDevices, productNames, catalog);
        if (current is null)
        {
            return detected;
        }

        var currentProfile = catalog.FirstOrDefault(profile => profile.HardwareIds.Any(current.HardwareIds.Contains));
        if (currentProfile is null && current.Source != WheelSelectionSource.NameMatch)
        {
            return detected;
        }

        var kept = currentProfile is null ? current : SelectedDevice.FromProfile(currentProfile);
        if (kept.IsSameSelectionAs(current))
        {
            kept = current;
        }

        var liveIds = liveDevices.Select(d => d.HardwareId).ToHashSet();
        if (kept.HardwareIds.Any(liveIds.Contains))
        {
            return kept;
        }

        // Not visible right now. Only a positively-identified different catalog wheel replaces it.
        return detected is { Source: WheelSelectionSource.Catalog } && !detected.IsSameSelectionAs(kept) ? detected : kept;
    }
}
