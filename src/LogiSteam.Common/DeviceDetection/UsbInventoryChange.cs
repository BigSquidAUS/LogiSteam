using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

public enum UsbInventoryChangeKind
{
    /// <summary>A hardware id that wasn't present in the previous snapshot showed up.</summary>
    Arrived,

    /// <summary>A hardware id that was present in the previous snapshot is no longer there.</summary>
    Left,

    /// <summary>
    /// The same hardware id is still present, but its underlying Windows DeviceID changed --
    /// e.g. a different bus/port location, or a driver rebind (such as usbipd-win swapping in
    /// its "USBIP Shared Device" stub while sharing it). Distinct from Arrived/Left because the
    /// VID:PID never disappeared from view; only its instance identity moved.
    /// </summary>
    DeviceIdChanged,
}

/// <summary>One observed change between two consecutive USB device inventory snapshots.</summary>
public sealed record UsbInventoryChange(
    UsbInventoryChangeKind Kind,
    UsbHardwareId HardwareId,
    string Description,
    string? OldDeviceId,
    string? NewDeviceId);
