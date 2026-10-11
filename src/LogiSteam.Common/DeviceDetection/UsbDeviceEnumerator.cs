using System.Management;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Lists USB devices currently plugged in, for the "pick your wheel yourself" flow -- guessing a
/// single hardcoded VID/PID for the G29 has been wrong twice (a stale value from a third-party
/// lookup site, then a genuine per-unit/firmware difference), so LogiSteam no longer trusts a
/// single hardcoded id as the only path to detection.
/// </summary>
public static class UsbDeviceEnumerator
{
    public static IReadOnlyList<UsbDeviceInfo> EnumeratePresentDevices()
    {
        var devices = new List<UsbDeviceInfo>();

        // Deliberately no backslash anywhere in this WQL text. "USB\%" (backslash immediately
        // before a '%' wildcard) reproducibly throws ManagementException "Invalid query" against
        // the real WMI provider -- confirmed directly, not assumed -- likely because WQL treats
        // backslash as its own escape character and doesn't accept '%' as an escaped literal.
        // Filtering to "USB\..." is done in .NET below instead of in the WHERE clause.
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Name, Description FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'");
        using var results = searcher.Get();

        foreach (var item in results)
        {
            using (item)
            {
                var deviceId = item["DeviceID"] as string;
                if (deviceId is null || !UsbHardwareId.TryParseFromInstanceId(deviceId, out var hardwareId))
                {
                    continue;
                }

                var name = item["Name"] as string ?? item["Description"] as string ?? deviceId;
                devices.Add(new UsbDeviceInfo(hardwareId, name, deviceId));
            }
        }

        return DeduplicateByHardwareId(devices);
    }

    /// <summary>
    /// Lists plugged-in USB devices for the "add another USB device" list: each with the best
    /// available name and a verdict on whether it's sensible to share (see
    /// <see cref="UsbDeviceClassifier"/>). The verdict needs every PnP node belonging to the
    /// device, not just its top-level USB one -- a keyboard only says "Keyboard" on the HID node
    /// beneath it -- so this reads HID nodes too and groups everything by VID:PID.
    /// </summary>
    public static IReadOnlyList<UsbDeviceDetails> EnumerateWithDetails()
    {
        var usbNodes = new List<UsbDeviceInfo>();
        var classes = new Dictionary<UsbHardwareId, List<string?>>();
        var services = new Dictionary<UsbHardwareId, List<string?>>();
        var names = new Dictionary<UsbHardwareId, List<string?>>();

        using (var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Name, Description, PNPClass, Service FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%' OR DeviceID LIKE 'HID%'"))
        using (var results = searcher.Get())
        {
            foreach (var item in results)
            {
                using (item)
                {
                    var deviceId = item["DeviceID"] as string;
                    if (deviceId is null || !UsbHardwareId.TryParseFromInstanceId(deviceId, out var hardwareId))
                    {
                        continue;
                    }

                    if (!classes.TryGetValue(hardwareId, out var deviceClasses))
                    {
                        classes[hardwareId] = deviceClasses = [];
                        services[hardwareId] = [];
                        names[hardwareId] = [];
                    }

                    names[hardwareId].Add(item["Name"] as string);

                    deviceClasses.Add(item["PNPClass"] as string);
                    services[hardwareId].Add(item["Service"] as string);

                    if (deviceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
                    {
                        usbNodes.Add(new UsbDeviceInfo(hardwareId, item["Name"] as string ?? item["Description"] as string ?? deviceId, deviceId));
                    }
                }
            }
        }

        var productNames = HidProductNameProbe.GetProductNames(vendorId: null);
        return DeduplicateByHardwareId(usbNodes)
            .Select(info =>
            {
                productNames.TryGetValue(info.HardwareId, out var productName);
                var (advice, reason) = UsbDeviceClassifier.Classify(classes[info.HardwareId], services[info.HardwareId]);
                return new UsbDeviceDetails(
                    info, UsbDeviceClassifier.ChooseDisplayName(info.HardwareId, info.Description, productName, names[info.HardwareId]), advice, reason);
            })
            .ToList();
    }

    /// <summary>
    /// A single physical composite USB device (e.g. a wheel base with a keyboard-emulation
    /// interface, a joystick/FFB interface, etc.) usually shows up as several separate PnP
    /// entries sharing one VID:PID but with different "&amp;MI_xx" function suffixes on their
    /// DeviceID. Collapse those into one representative entry per hardware id, so the picker
    /// shows one line per physical device rather than one per internal USB interface. Exposed
    /// publicly (rather than kept as a private implementation detail) so this grouping logic is
    /// unit-testable without WMI/real hardware.
    /// </summary>
    public static IReadOnlyList<UsbDeviceInfo> DeduplicateByHardwareId(IEnumerable<UsbDeviceInfo> devices) =>
        devices
            .GroupBy(d => d.HardwareId)
            .Select(g => g
                .OrderBy(d => d.DeviceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(d => d.Description.Length)
                .First())
            .OrderBy(d => d.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
