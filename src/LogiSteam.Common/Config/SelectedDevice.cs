using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Config;

/// <summary>The USB device the user picked as "this is my wheel", persisted across restarts.</summary>
public sealed record SelectedDevice
{
    public required ushort Vendor { get; init; }
    public required ushort Product { get; init; }
    public required string Description { get; init; }

    public UsbHardwareId HardwareId => new(Vendor, Product);

    public static SelectedDevice From(UsbDeviceInfo device) => new()
    {
        Vendor = device.HardwareId.Vendor,
        Product = device.HardwareId.Product,
        Description = device.Description,
    };
}
