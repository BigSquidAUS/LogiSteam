using System.Reflection;

namespace LogiSteam.Common.Updates;

/// <summary>
/// Version strings as LogiSteam exchanges them: "2.1.0" in beacon/ack messages, "v2.1.0" as a
/// GitHub release tag. Both PCs tell each other their version so that updating one of them
/// prompts the other to update too.
/// </summary>
public static class AppVersion
{
    /// <summary>This running app's version ("2.1.0"), taken from the executable.</summary>
    public static string Current { get; } = Format(Assembly.GetEntryAssembly()?.GetName().Version) ?? "0.0.0";

    /// <summary>Parses "2.1.0", "v2.1.0" or "2.1" -- anything else (including null) gives null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        if (!Version.TryParse(text, out var version))
        {
            return null;
        }

        // Normalize so "2.1" and "2.1.0" compare equal (Version treats a missing part as -1).
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>True only when both parse and <paramref name="candidate"/> is strictly newer.</summary>
    public static bool IsNewer(string? candidate, string? than) =>
        Parse(candidate) is { } a && Parse(than) is { } b && a > b;

    private static string? Format(Version? version) =>
        version is null ? null : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
