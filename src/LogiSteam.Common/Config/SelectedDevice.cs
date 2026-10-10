using System.Text.Json.Serialization;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Config;

/// <summary>How a <see cref="SelectedDevice"/> came to be the wheel LogiSteam shares.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WheelSelectionSource>))]
public enum WheelSelectionSource
{
    /// <summary>Saved by a version that predates this field (including its manual device picker).</summary>
    Legacy,

    /// <summary>Identified automatically from <see cref="KnownWheelProfiles"/>.</summary>
    Catalog,

    /// <summary>Identified automatically from its product name (a wheel the catalog doesn't list yet).</summary>
    NameMatch,

    /// <summary>Chosen by hand under Advanced -- never overridden by automatic detection.</summary>
    Manual,
}

/// <summary>
/// The wheel LogiSteam is sharing, persisted across restarts -- normally identified
/// automatically (every known persona for that model, e.g. the G29's PS3/PS4-mode ids, so a mode
/// switch or usbipd-win's bind-time driver swap never leaves the saved selection matching
/// nothing live), or exactly one hardware id when chosen by hand under Advanced.
/// </summary>
public sealed record SelectedDevice
{
    /// <summary>Stable label for status text -- a profile's clean name ("Logitech G29") or,
    /// for a manual choice, whatever raw Windows description that device reported at pick time.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Every hardware id that counts as "this wheel".</summary>
    public required IReadOnlyList<UsbHardwareId> HardwareIds { get; init; }

    public WheelSelectionSource Source { get; init; }

    public bool Matches(UsbHardwareId hardwareId) => HardwareIds.Contains(hardwareId);

    /// <summary>
    /// Builds a selection from a manual choice (Advanced). If the chosen device turns out to be
    /// a wheel the catalog knows, it becomes that ordinary auto-detected profile instead -- the
    /// full multi-persona id set, so a future mode switch never strands the connection the way a
    /// bare single-id manual pick would.
    /// </summary>
    public static SelectedDevice From(UsbDeviceInfo device)
    {
        var knownProfile = KnownWheelProfiles.Find(device.HardwareId);
        if (knownProfile is not null)
        {
            return FromProfile(knownProfile);
        }

        return new()
        {
            DisplayName = device.Description,
            HardwareIds = [device.HardwareId],
            Source = WheelSelectionSource.Manual,
        };
    }

    public static SelectedDevice FromProfile(KnownWheelProfile profile) => new()
    {
        DisplayName = profile.DisplayName,
        HardwareIds = profile.HardwareIds,
        Source = WheelSelectionSource.Catalog,
    };

    /// <summary>Value comparison -- the record's own equality compares the id list by reference.</summary>
    public bool IsSameSelectionAs(SelectedDevice other) =>
        DisplayName == other.DisplayName && Source == other.Source && HardwareIds.SequenceEqual(other.HardwareIds);
}
