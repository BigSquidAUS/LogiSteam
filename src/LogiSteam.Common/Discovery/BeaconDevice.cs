using System.Text.Json.Serialization;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// One USB device a Wheel PC is offering: the racing wheel, or an additional device the user
/// added (pedals, a shifter, a button box, a Stream Deck, a HOTAS...).
/// </summary>
public sealed class BeaconDevice
{
    [JsonPropertyName("busId")]
    public required string BusId { get; init; }

    [JsonPropertyName("vendorId")]
    public required string VendorId { get; init; }

    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>True for the identified racing wheel, false for a device the user added by hand.</summary>
    [JsonPropertyName("isWheel")]
    public bool IsWheel { get; init; }

    /// <summary>"vvvv:pppp" -- how a device is remembered across reconnects, when its bus id can change.</summary>
    [JsonIgnore]
    public string HardwareIdText => $"{VendorId}:{ProductId}".ToLowerInvariant();
}
