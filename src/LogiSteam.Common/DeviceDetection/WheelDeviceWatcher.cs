using System.Management;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Polls Windows PnP device presence for any of a wheel's known hardware ids (a manual pick is
/// one id; an auto-detected profile is every known persona for that model) so
/// LogiSteam.WheelSide can drive its tray status, without requiring the wheel to already be
/// plugged in when the app starts. "Present" means any one of the given ids currently matches.
/// </summary>
public sealed class WheelDeviceWatcher(IReadOnlyList<UsbHardwareId> hardwareIds, TimeSpan? pollInterval = null, TimeSpan? disappearanceTolerance = null) : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1.5);

    // Logitech wheels (confirmed for the G29) run an autocentering calibration routine on
    // connect that takes roughly 10 seconds, during which the device can be hard to detect or
    // unresponsive. The original 3-poll debounce (~4.5s at the default interval) wasn't enough to
    // ride that out, so a fresh calibration could itself look like a disconnect, triggering a
    // detach that immediately causes another calibration on reattach -- a self-sustaining bounce.
    // Default tolerance is set comfortably above the observed calibration duration.
    private static readonly TimeSpan DefaultDisappearanceTolerance = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly PresenceDebouncer _debouncer = new(ComputeDebounceThreshold(pollInterval ?? DefaultPollInterval, disappearanceTolerance ?? DefaultDisappearanceTolerance));
    private Task? _loop;

    private static int ComputeDebounceThreshold(TimeSpan pollInterval, TimeSpan tolerance) =>
        Math.Max(1, (int)Math.Ceiling(tolerance / pollInterval));

    public bool IsPresent { get; private set; }

    public event Action? DeviceArrived;
    public event Action? DeviceRemoved;

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
        // Deliberately no backslash in this WQL text -- see UsbDeviceEnumerator's remarks for the
        // confirmed repro ("USB\%" throws ManagementException "Invalid query" against the real
        // WMI provider). Match the actual hardware id in .NET instead of in the WHERE clause.
        var query = new SelectQuery("Win32_PnPEntity", "DeviceID LIKE 'USB%'");
        var needles = hardwareIds.Select(BuildDeviceIdNeedle).ToList();

        while (!ct.IsCancellationRequested)
        {
            bool present;
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                using var results = searcher.Get();
                present = false;
                foreach (var item in results)
                {
                    using (item)
                    {
                        var deviceId = item["DeviceID"] as string;
                        if (needles.Any(needle => MatchesNeedle(deviceId, needle)))
                        {
                            present = true;
                            break;
                        }
                    }
                }
            }
            catch (ManagementException)
            {
                // WMI hiccup (e.g. service restarting); assume no change this tick.
                present = _debouncer.CurrentState;
            }

            var changedTo = _debouncer.Observe(present);
            if (changedTo is { } newState)
            {
                IsPresent = newState;
                if (newState)
                {
                    DeviceArrived?.Invoke();
                }
                else
                {
                    DeviceRemoved?.Invoke();
                }
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

    /// <summary>
    /// The substring a real DeviceID must contain to match this hardware id, e.g.
    /// "VID_046D&amp;PID_C52B" (a real DeviceID looks like "USB\VID_046D&PID_C52B&REV_2411&MI_00").
    /// Matched in .NET rather than in WQL -- see the remarks on <see cref="RunAsync"/> for why.
    /// Exposed statically so this is unit-testable without WMI.
    /// </summary>
    public static string BuildDeviceIdNeedle(UsbHardwareId hardwareId) =>
        $"VID_{hardwareId.Vendor:X4}&PID_{hardwareId.Product:X4}";

    private static bool MatchesNeedle(string? deviceId, string needle) =>
        deviceId is not null && deviceId.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
