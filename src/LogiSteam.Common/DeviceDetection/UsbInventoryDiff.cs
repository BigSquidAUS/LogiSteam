namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Compares two USB device inventory snapshots (as produced by
/// <see cref="UsbDeviceEnumerator.EnumeratePresentDevices"/>) and reports what changed. Pure and
/// side-effect-free so it's unit-testable without WMI/real hardware.
/// </summary>
public static class UsbInventoryDiff
{
    public static IReadOnlyList<UsbInventoryChange> Compute(
        IReadOnlyList<UsbDeviceInfo> previous, IReadOnlyList<UsbDeviceInfo> current)
    {
        var previousByHardwareId = previous.ToDictionary(d => d.HardwareId);
        var currentByHardwareId = current.ToDictionary(d => d.HardwareId);
        var changes = new List<UsbInventoryChange>();

        foreach (var currentDevice in current)
        {
            if (!previousByHardwareId.TryGetValue(currentDevice.HardwareId, out var previousDevice))
            {
                changes.Add(new UsbInventoryChange(
                    UsbInventoryChangeKind.Arrived, currentDevice.HardwareId, currentDevice.Description,
                    OldDeviceId: null, NewDeviceId: currentDevice.DeviceId));
            }
            else if (!string.Equals(previousDevice.DeviceId, currentDevice.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new UsbInventoryChange(
                    UsbInventoryChangeKind.DeviceIdChanged, currentDevice.HardwareId, currentDevice.Description,
                    previousDevice.DeviceId, currentDevice.DeviceId));
            }
        }

        foreach (var previousDevice in previous)
        {
            if (!currentByHardwareId.ContainsKey(previousDevice.HardwareId))
            {
                changes.Add(new UsbInventoryChange(
                    UsbInventoryChangeKind.Left, previousDevice.HardwareId, previousDevice.Description,
                    previousDevice.DeviceId, NewDeviceId: null));
            }
        }

        return changes;
    }
}
