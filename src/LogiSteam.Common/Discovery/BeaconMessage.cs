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
