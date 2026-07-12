using System.Net;
using System.Text.Json.Serialization;

namespace LogiSteam.Common.UsbipInterop;

// Mirrors the JSON shape of Usbipd.Automation.Device / State (dorssel/usbipd-win), as emitted
// by `usbipd state`. usbipd-win's own IsBound/IsConnected/IsAttached properties are [JsonIgnore]
// on that type (computed at runtime, not serialized), so this wrapper re-derives equivalent
// flags from the fields that ARE present in the JSON (PersistedGuid / ClientIPAddress).
// Verify this shape against a live `usbipd state` during the Phase 0 spike (see plan) --
// usbipd-win does not publish a formal JSON schema for this command.

public sealed class UsbipdState
{
    [JsonPropertyName("Devices")]
    public List<UsbipdDevice> Devices { get; init; } = [];
}

public sealed class UsbipdDevice
{
    [JsonPropertyName("BusId")]
    public string? BusId { get; init; }

    [JsonPropertyName("ClientIPAddress")]
    [JsonConverter(typeof(IPAddressJsonConverter))]
    public IPAddress? ClientIPAddress { get; init; }

    [JsonPropertyName("Description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("InstanceId")]
    public string InstanceId { get; init; } = string.Empty;

    [JsonPropertyName("IsForced")]
    public bool IsForced { get; init; }

    [JsonPropertyName("PersistedGuid")]
    public Guid? PersistedGuid { get; init; }

    [JsonPropertyName("StubInstanceId")]
    public string? StubInstanceId { get; init; }

    /// <summary>Derived from <see cref="InstanceId"/>; not present in the raw JSON.</summary>
    public UsbHardwareId? HardwareId =>
        UsbHardwareId.TryParseFromInstanceId(InstanceId, out var id) ? id : null;

    /// <summary>A non-null PersistedGuid indicates the device has been bound/shared at least once.</summary>
    public bool IsShared => PersistedGuid.HasValue;

    /// <summary>A non-null ClientIPAddress indicates a remote machine currently has this device attached.</summary>
    public bool IsAttachedRemotely => ClientIPAddress is not null;
}
