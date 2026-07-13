using LogiSteam.Common.Config;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Pure matcher: given what's currently plugged in and the known-profile catalog, finds the
/// first profile with at least one currently-live hardware id. Takes an already-enumerated
/// device list rather than calling WMI itself, so it's trivially unit-testable.
/// </summary>
public static class WheelAutoDetector
{
    public static KnownWheelProfile? DetectFirstMatch(
        IReadOnlyList<UsbDeviceInfo> liveDevices, IReadOnlyList<KnownWheelProfile>? catalog = null)
    {
        catalog ??= KnownWheelProfiles.All;
        var liveIds = liveDevices.Select(d => d.HardwareId).ToHashSet();
        return catalog.FirstOrDefault(profile => profile.HardwareIds.Any(liveIds.Contains));
    }

    /// <summary>
    /// Keeps a persisted <see cref="SelectedDevice"/> honest against reality, called on every
    /// refresh tick rather than only at first run. Fixes two real failure modes: (1) a legacy or
    /// manual single-persona pick that happens to be a known wheel model never gets upgraded to
    /// the full multi-persona set, so a later mode switch strands it; (2) a selection whose ids
    /// have all gone stale (mode switch, wheel swapped) never re-triggers auto-detection even
    /// when a known model is plugged in right now. Never wipes a selection outright -- an
    /// unrecognized manual pick that's just temporarily unplugged is left alone rather than
    /// cleared, since the user chose it deliberately and it may come back.
    /// </summary>
    public static SelectedDevice? ReconcileSelection(
        SelectedDevice? current, IReadOnlyList<UsbDeviceInfo> liveDevices, IReadOnlyList<KnownWheelProfile>? catalog = null)
    {
        if (current is null)
        {
            var detected = DetectFirstMatch(liveDevices, catalog);
            return detected is null ? null : SelectedDevice.FromProfile(detected);
        }

        catalog ??= KnownWheelProfiles.All;

        if (current.HardwareIds.Count == 1)
        {
            var knownProfile = catalog.FirstOrDefault(profile => profile.HardwareIds.Contains(current.HardwareIds[0]));
            if (knownProfile is not null)
            {
                return SelectedDevice.FromProfile(knownProfile);
            }
        }

        var liveIds = liveDevices.Select(d => d.HardwareId).ToHashSet();
        if (current.HardwareIds.Any(liveIds.Contains))
        {
            return current; // already matches something live -- don't thrash a working selection
        }

        var replacement = DetectFirstMatch(liveDevices, catalog);
        return replacement is null ? current : SelectedDevice.FromProfile(replacement);
    }
}
