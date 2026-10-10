using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiSteam.Common.Config;

/// <summary>Which end of the connection this PC is. One LogiSteam app plays either role.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppRole>))]
public enum AppRole
{
    /// <summary>The wheel is physically plugged into this PC.</summary>
    Wheel,

    /// <summary>This PC runs Steam and the game.</summary>
    Game,
}

public sealed record AppSettings
{
    /// <summary>Null until the user has chosen on first run.</summary>
    public AppRole? Role { get; init; }

    /// <summary>
    /// Logitech G HUB is always required on the Game PC. This also requires it on the Wheel PC
    /// (and warns on both PCs when it isn't running there) -- off by default, for setups where
    /// the wheel misbehaves without it.
    /// </summary>
    public bool RequireGHubOnWheelPc { get; init; }
}

/// <summary>Loads/saves <see cref="AppSettings"/> (%LOCALAPPDATA%\LogiSteam\settings.json).</summary>
public sealed class AppSettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiSteam", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
