using System.Text.Json;

namespace LogiSteam.Common.Config;

/// <summary>
/// Loads/saves the user's chosen wheel device so the picker only has to be used once -- picking
/// a device is a one-time setup step, not something to repeat on every launch.
/// </summary>
public sealed class SelectedDeviceStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiSteam", "wheelside-device.json");

    public SelectedDevice? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SelectedDevice>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(SelectedDevice device)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(device, JsonOptions));
    }
}
