using System.Management;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Diagnostics;

/// <summary>
/// Runs on the Wheel PC and keeps a durable, timestamped log of every USB device Windows sees
/// and how that list changes over time -- turning the one-off diagnostic scripts used to track
/// down the G29's bind/rebind/bus-move flakiness into a permanent, built-in feature. Every
/// Logitech-vendor device is tagged so it's easy to spot the wheel even as it moves between bus
/// locations or gets a driver swapped out from under it (e.g. usbipd-win's "USBIP Shared Device"
/// stub), and the currently-selected device (see SelectedDeviceStore) is tagged distinctly.
/// </summary>
public sealed class UsbDeviceInventoryLogger(
    Func<UsbHardwareId?> getSelectedHardwareId,
    string? logPath = null,
    TimeSpan? pollInterval = null) : IDisposable
{
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB, then rotate

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    public readonly string LogPath = logPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiSteam", "logs", "usb-devices.log");

    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;
    private readonly CancellationTokenSource _cts = new();
    private IReadOnlyList<UsbDeviceInfo>? _lastSnapshot;
    private Task? _loop;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Poll();
            }
            catch (ManagementException ex)
            {
                WriteLine($"WMI ERROR: {ex.Message}");
            }

            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void Poll()
    {
        var current = UsbDeviceEnumerator.EnumeratePresentDevices();

        if (_lastSnapshot is null)
        {
            WriteLine($"BASELINE -- {current.Count} device(s) present:");
            foreach (var device in current)
            {
                WriteLine($"  {DescribeDevice(device)}");
            }

            _lastSnapshot = current;
            return;
        }

        var changes = UsbInventoryDiff.Compute(_lastSnapshot, current);
        if (changes.Count > 0)
        {
            foreach (var change in changes)
            {
                WriteLine(DescribeChange(change));
            }
        }

        _lastSnapshot = current;
    }

    private string DescribeDevice(UsbDeviceInfo device)
    {
        var tags = BuildTags(device.HardwareId);
        return $"{device.HardwareId} \"{device.Description}\" [{device.DeviceId}]{tags}";
    }

    private string DescribeChange(UsbInventoryChange change)
    {
        var tags = BuildTags(change.HardwareId);
        return change.Kind switch
        {
            UsbInventoryChangeKind.Arrived =>
                $"ARRIVED  {change.HardwareId} \"{change.Description}\" [{change.NewDeviceId}]{tags}",
            UsbInventoryChangeKind.Left =>
                $"LEFT     {change.HardwareId} \"{change.Description}\" [{change.OldDeviceId}]{tags}",
            UsbInventoryChangeKind.DeviceIdChanged =>
                $"MOVED    {change.HardwareId} \"{change.Description}\" [{change.OldDeviceId}] -> [{change.NewDeviceId}]{tags}",
            _ => $"UNKNOWN CHANGE {change}",
        };
    }

    private string BuildTags(UsbHardwareId hardwareId)
    {
        var tags = string.Empty;
        if (hardwareId.Vendor == UsbHardwareId.LogitechVendorId)
        {
            tags += " [LOGITECH]";
        }

        if (getSelectedHardwareId() == hardwareId)
        {
            tags += " [SELECTED]";
        }

        return tags;
    }

    private void WriteLine(string text)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RotateIfNeeded();
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}\n");
        }
        catch (IOException)
        {
            // Best-effort logging; a transient file-lock hiccup shouldn't crash the watcher.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxLogSizeBytes)
        {
            return;
        }

        File.Move(LogPath, LogPath + ".1", overwrite: true);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Expected on cancellation.
        }

        _cts.Dispose();
    }
}
