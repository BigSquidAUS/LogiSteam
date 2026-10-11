using System.Text.Json;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Config;

/// <summary>
/// A USB device the user added on the Wheel PC to be shared alongside the wheel -- pedals, a
/// shifter, a handbrake, a button box, a Stream Deck, a HOTAS. Remembered by hardware id, so it
/// is shared again whenever it's plugged in.
/// </summary>
public sealed record ExtraDevice
{
    public required string DisplayName { get; init; }

    public required UsbHardwareId HardwareId { get; init; }
}

/// <summary>A small JSON file holding a list, under %LOCALAPPDATA%\LogiSteam.</summary>
public sealed class JsonListStore<T>(string fileName, string? directory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiSteam"),
        fileName);

    public IReadOnlyList<T> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path), JsonOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<T> items)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(items.ToList(), JsonOptions));
    }
}

public static class DeviceStores
{
    /// <summary>How many additional devices can be shared alongside the wheel.</summary>
    public const int MaxExtraDevices = 8;

    /// <summary>Wheel PC: the additional devices the user has added.</summary>
    public static JsonListStore<ExtraDevice> ExtraDevices(string? directory = null) => new("wheelside-extra-devices.json", directory);

    /// <summary>
    /// Game PC: hardware ids ("vvvv:pppp") of offered devices the user chose not to connect
    /// here, so they stay with the Wheel PC until re-enabled.
    /// </summary>
    public static JsonListStore<string> ReleasedDevices(string? directory = null) => new("gameside-released-devices.json", directory);
}
