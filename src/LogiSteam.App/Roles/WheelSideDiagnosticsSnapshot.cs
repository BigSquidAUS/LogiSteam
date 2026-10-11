using System.Text.Json;
using System.Text.Json.Serialization;
using LogiSteam.Common.DeviceDetection;

namespace LogiSteam.Roles;

/// <summary>Everything a remote troubleshooter would want to know about WheelSide's live state, in one query.</summary>
public sealed class WheelSideDiagnosticsSnapshot
{
    [JsonPropertyName("snapshotUtc")]
    public DateTime SnapshotUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("selectedDisplayName")]
    public string? SelectedDisplayName { get; init; }

    [JsonPropertyName("selectedHardwareIds")]
    public IReadOnlyList<string> SelectedHardwareIds { get; init; } = [];

    [JsonPropertyName("currentlyLiveHardwareId")]
    public string? CurrentlyLiveHardwareId { get; init; }

    [JsonPropertyName("statusText")]
    public required string StatusText { get; init; }

    [JsonPropertyName("statusLevel")]
    public required string StatusLevel { get; init; }

    [JsonPropertyName("policyEnsured")]
    public bool PolicyEnsured { get; init; }

    [JsonPropertyName("currentBusId")]
    public string? CurrentBusId { get; init; }

    [JsonPropertyName("currentlyShared")]
    public bool? CurrentlyShared { get; init; }

    /// <summary>Additional devices the user added, and what usbipd-win currently sees of each.</summary>
    [JsonPropertyName("extraDevices")]
    public IReadOnlyList<ExtraDeviceSummary> ExtraDevices { get; init; } = [];

    [JsonPropertyName("lastAckAttachedBusIds")]
    public IReadOnlyList<string>? LastAckAttachedBusIds { get; init; }

    [JsonPropertyName("lastAckFromHost")]
    public string? LastAckFromHost { get; init; }

    [JsonPropertyName("lastAckAttached")]
    public bool? LastAckAttached { get; init; }

    [JsonPropertyName("lastAckAgoSeconds")]
    public double? LastAckAgoSeconds { get; init; }

    [JsonPropertyName("recentUsbLogLines")]
    public IReadOnlyList<string> RecentUsbLogLines { get; init; } = [];

    [JsonPropertyName("allUsbDevicesNow")]
    public IReadOnlyList<UsbDeviceSummary> AllUsbDevicesNow { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

public sealed record ExtraDeviceSummary(string Name, string HardwareId, string? BusId, bool? Shared);

public sealed record UsbDeviceSummary(string HardwareId, string Description, string DeviceId)
{
    public static UsbDeviceSummary From(UsbDeviceInfo device) =>
        new(device.HardwareId.ToString(), device.Description, device.DeviceId);
}
