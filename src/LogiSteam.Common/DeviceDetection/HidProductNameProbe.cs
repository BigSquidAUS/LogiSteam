using HidSharp;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Reads each present HID device's own USB product string ("G29 Driving Force Racing Wheel"),
/// for <see cref="WheelAutoDetector"/>'s name-based fallback -- Windows' PnP description for the
/// same device is often just "USB Input Device".
/// </summary>
public static class HidProductNameProbe
{
    public static IReadOnlyDictionary<UsbHardwareId, string> GetProductNames(ushort vendorId)
    {
        var names = new Dictionary<UsbHardwareId, string>();
        try
        {
            foreach (var device in DeviceList.Local.GetHidDevices(vendorId))
            {
                var id = new UsbHardwareId((ushort)device.VendorID, (ushort)device.ProductID);
                if (names.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    var name = device.GetProductName();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names[id] = name;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Some HID collections refuse a string read; the next one for this id may not.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Best-effort enrichment only -- detection still works from the catalog without it.
        }

        return names;
    }
}
