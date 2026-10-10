using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Sent by the Game PC back to a Wheel PC every time it processes that Wheel PC's beacon, so the
/// Wheel PC can tell whether a Game PC currently sees it (not just that it's broadcasting into
/// the void) -- and whether that Game PC has actually attached the wheel.
/// </summary>
public sealed class AckMessage
{
    public const int ProtocolVersion = 1;

    [JsonPropertyName("protocolVersion")]
    public int Version { get; init; } = ProtocolVersion;

    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("attached")]
    public bool Attached { get; init; }

    /// <summary>Whether Logitech G HUB is running on the Game PC. Null from an older LogiSteam.</summary>
    [JsonPropertyName("gHubRunning")]
    public bool? GHubRunning { get; init; }

    /// <summary>The Game PC's LogiSteam version ("2.1.0"). Null from a version before 2.1.</summary>
    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string json, out AckMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<AckMessage>(json, JsonOptions);
            return message is not null;
        }
        catch (JsonException)
        {
            message = null;
            return false;
        }
    }
}
