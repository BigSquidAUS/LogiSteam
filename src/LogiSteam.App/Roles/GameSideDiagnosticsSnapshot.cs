using System.Text.Json;
using System.Text.Json.Serialization;
using LogiSteam.Common.Discovery;

namespace LogiSteam.Roles;

/// <summary>Everything a remote troubleshooter would want to know about GameSide's live state, in one query.</summary>
public sealed class GameSideDiagnosticsSnapshot
{
    [JsonPropertyName("snapshotUtc")]
    public DateTime SnapshotUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("statusText")]
    public required string StatusText { get; init; }

    [JsonPropertyName("statusLevel")]
    public required string StatusLevel { get; init; }

    [JsonPropertyName("attachedFromHost")]
    public string? AttachedFromHost { get; init; }

    [JsonPropertyName("attachedBusId")]
    public string? AttachedBusId { get; init; }

    [JsonPropertyName("attachedLocalPort")]
    public int? AttachedLocalPort { get; init; }

    /// <summary>Every device the current Wheel PC offers and what this PC has done with it.</summary>
    [JsonPropertyName("devices")]
    public IReadOnlyList<OfferedDeviceSummary> Devices { get; init; } = [];

    [JsonPropertyName("discoveredWheelPcs")]
    public IReadOnlyList<DiscoveredWheelPcSummary> DiscoveredWheelPcs { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

public sealed record OfferedDeviceSummary(
    string Name, string HardwareId, string BusId, bool IsWheel, string State, int? LocalPort, string? Error);

public sealed record DiscoveredWheelPcSummary(
    string Address, string HostName, string BusId, string VendorId, string ProductId, bool Shared, double SecondsSinceLastSeen)
{
    public static DiscoveredWheelPcSummary From(DiscoveredWheelPc pc) => new(
        pc.Address.ToString(), pc.Beacon.HostName, pc.Beacon.BusId, pc.Beacon.VendorId, pc.Beacon.ProductId,
        pc.Beacon.Shared, (DateTime.UtcNow - pc.LastSeenUtc).TotalSeconds);
}
