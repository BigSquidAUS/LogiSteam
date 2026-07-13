using HidSharp;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Diagnostics;

/// <summary>
/// Reads raw HID input reports live from every currently-present device matching any of a set of
/// hardware ids, for the Diagnostics window's "Input Test" panel -- productizes the ad-hoc
/// HidSharp scratch script used to directly confirm the G29's input was flowing end-to-end over
/// the USB/IP tunnel during this project's troubleshooting. UI-agnostic: raises an event with raw
/// bytes: callers (a Form) marshal it to their own UI thread, the same
/// <see cref="SynchronizationContext"/>-based pattern used everywhere else in this codebase.
/// </summary>
public sealed class HidInputTester : IDisposable
{
    private readonly List<HidStream> _openStreams = [];
    private CancellationTokenSource? _cts;

    /// <summary>Raised once per HID input report received, from a background thread. Parameters: device path, raw report bytes, timestamp.</summary>
    public event Action<string, byte[], DateTime>? ReportReceived;

    public bool IsRunning => _cts is not null;

    /// <summary>Opens every currently-present HID device matching any of the given hardware ids and starts reading.</summary>
    public void Start(IReadOnlyList<UsbHardwareId> hardwareIds)
    {
        Stop();

        var cts = new CancellationTokenSource();
        _cts = cts;

        foreach (var hardwareId in hardwareIds)
        {
            foreach (var device in DeviceList.Local.GetHidDevices(hardwareId.Vendor, hardwareId.Product))
            {
                if (!device.TryOpen(out var stream))
                {
                    continue;
                }

                stream.ReadTimeout = 500;
                _openStreams.Add(stream);
                var devicePath = device.DevicePath;
                var reportLength = SafeGetMaxInputReportLength(device);
                _ = Task.Run(() => ReadLoop(stream, devicePath, reportLength, cts.Token), cts.Token);
            }
        }
    }

    private static int SafeGetMaxInputReportLength(HidDevice device)
    {
        try
        {
            return device.GetMaxInputReportLength();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 64; // reasonable fallback buffer size if the descriptor can't be read for some reason
        }
    }

    private void ReadLoop(HidStream stream, string devicePath, int reportLength, CancellationToken ct)
    {
        var buffer = new byte[reportLength];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                break;
            }

            if (read > 0)
            {
                ReportReceived?.Invoke(devicePath, buffer[..read], DateTime.Now);
            }
        }
    }

    /// <summary>Closes every open device stream. Safe to call even if never started.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        foreach (var stream in _openStreams)
        {
            try
            {
                stream.Close();
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }

        _openStreams.Clear();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
