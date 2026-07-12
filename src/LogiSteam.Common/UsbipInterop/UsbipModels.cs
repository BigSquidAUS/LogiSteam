namespace LogiSteam.Common.UsbipInterop;

/// <summary>A device line parsed from `usbip.exe list -r &lt;host&gt;` output.</summary>
public sealed record RemoteUsbDevice(string BusId, string VendorProductName, UsbHardwareId HardwareId);

/// <summary>The result of a successful `usbip.exe attach`.</summary>
public sealed record AttachedDevice(int Port, string RemoteHost, string BusId);
