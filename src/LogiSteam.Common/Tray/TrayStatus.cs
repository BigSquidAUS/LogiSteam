namespace LogiSteam.Common.Tray;

/// <summary>
/// A deterministic status level for tray icon/text, shared by both apps so icon selection isn't
/// based on fragile substring-matching of human-readable status text.
/// </summary>
public enum TrayStatusLevel
{
    /// <summary>Nothing to report yet / not doing anything (e.g. wheel unplugged, no peer found).</summary>
    Idle,

    /// <summary>Actively working but incomplete (e.g. sharing the wheel but no Game PC has been seen yet).</summary>
    Warning,

    /// <summary>Fully connected end-to-end (e.g. a Game PC can see this Wheel PC, or the wheel is attached).</summary>
    Connected,

    /// <summary>Something is actually broken (e.g. a prerequisite is missing, a command failed).</summary>
    Error,
}

public sealed record TrayStatus(string Text, TrayStatusLevel Level);
