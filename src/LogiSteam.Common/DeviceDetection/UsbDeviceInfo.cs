using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>A USB device currently visible to Windows, as shown to the user in the device picker.</summary>
public sealed record UsbDeviceInfo(UsbHardwareId HardwareId, string Description, string DeviceId);
