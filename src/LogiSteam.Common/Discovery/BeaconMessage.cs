using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Payload broadcast by LogiSteam.WheelSide so LogiSteam.GameSide can find it on the LAN
/// with no manual IP/bus-id entry.
/// </summary>
public sealed class BeaconMessage
{
    public const int ProtocolVersion = 1;

    [JsonPropertyName("protocolVersion")]
    public int Version { get; init; } = ProtocolVersion;

    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("busId")]
    public required string BusId { get; init; }

    [JsonPropertyName("vendorId")]
    public required string VendorId { get; init; }

    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }

    [JsonPropertyName("shared")]
    public bool Shared { get; init; }

    /// <summary>The wheel's display name ("Logitech G29"). Null from a Wheel PC running an older LogiSteam.</summary>
    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; init; }

    /// <summary>Whether Logitech G HUB is running on the Wheel PC. Null from an older LogiSteam.</summary>
    [JsonPropertyName("gHubRunning")]
    public bool? GHubRunning { get; init; }

    /// <summary>The Wheel PC's LogiSteam version ("2.1.0"). Null from a version before 2.1.</summary>
    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; init; }

    /// <summary>
    /// Every device on offer, the wheel first if there is one. Null from a version before 2.2,
    /// which only ever offered the single device described by the fields above. Those fields
    /// are still filled in (with the first device here) so an older Game PC keeps working.
    /// </summary>
    [JsonPropertyName("devices")]
    public IReadOnlyList<BeaconDevice>? Devices { get; init; }

    /// <summary>The devices on offer whichever version sent the beacon.</summary>
    public IReadOnlyList<BeaconDevice> AllDevices()
    {
        var devices = Devices is { Count: > 0 }
            ? Devices
            : [new BeaconDevice { BusId = BusId, VendorId = VendorId, ProductId = ProductId, Name = DeviceName, IsWheel = true }];
        return devices.Where(d => !string.IsNullOrWhiteSpace(d.BusId)).ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string json, out BeaconMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<BeaconMessage>(json, JsonOptions);
            return message is not null;
        }
        catch (JsonException)
        {
            message = null;
            return false;
        }
    }
}
