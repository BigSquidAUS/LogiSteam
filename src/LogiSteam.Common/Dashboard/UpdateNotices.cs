using LogiSteam.Common.Updates;

namespace LogiSteam.Common.Dashboard;

public enum UpdatePhase
{
    /// <summary>Nothing in progress.</summary>
    Idle,

    Downloading,

    /// <summary>Downloaded; the installer is starting.</summary>
    Installing,
}

/// <summary>What the app knows about its own version, the newest published one, and the other PC's.</summary>
/// <param name="CurrentVersion">This PC's version.</param>
/// <param name="LatestVersion">The newest published version, if it has been looked up.</param>
/// <param name="PeerName">The other PC, if one is connected.</param>
/// <param name="PeerVersion">Its version; null if it's too old to report one (before 2.1).</param>
/// <param name="Progress">0..1 while <see cref="UpdatePhase.Downloading"/>.</param>
/// <param name="Error">Why the last update attempt failed, if it did.</param>
public sealed record UpdateStatus(
    string CurrentVersion,
    string? LatestVersion = null,
    string? PeerName = null,
    string? PeerVersion = null,
    UpdatePhase Phase = UpdatePhase.Idle,
    double Progress = 0,
    string? Error = null);

/// <summary>
/// Adds the update-related cards to a dashboard: "an update is available for this PC" (with an
/// Update button) and "the other PC is behind". Keeping both PCs on the same version matters
/// because they talk to each other; these cards are how installing a new version on one PC
/// leads to the other being updated without anyone carrying an installer across.
/// </summary>
public static class UpdateNotices
{
    public const string UpdateId = "update";
    public const string PeerOutdatedId = "peer-outdated";

    public static bool UpdateAvailable(UpdateStatus status) => AppVersion.IsNewer(status.LatestVersion, status.CurrentVersion);

    public static DashboardState Apply(DashboardState state, UpdateStatus status)
    {
        var notices = new List<DashboardWarning>();

        if (status.Phase == UpdatePhase.Downloading)
        {
            notices.Add(new DashboardWarning(
                UpdateId,
                $"Downloading LogiSteam {status.LatestVersion}... {Math.Clamp(status.Progress, 0, 1):P0}",
                "The wheel stays connected while this downloads."));
        }
        else if (status.Phase == UpdatePhase.Installing)
        {
            notices.Add(new DashboardWarning(
                UpdateId,
                $"Installing LogiSteam {status.LatestVersion}...",
                "Choose Yes on the Windows prompt. LogiSteam will restart by itself."));
        }
        else if (UpdateAvailable(status))
        {
            var behindPeer = AppVersion.IsNewer(status.PeerVersion, status.CurrentVersion);
            notices.Add(new DashboardWarning(
                UpdateId,
                $"LogiSteam {status.LatestVersion} is available",
                status.Error
                    ?? (behindPeer
                        ? $"{status.PeerName ?? "Your other PC"} is already on {status.PeerVersion}. Update this PC to match (the wheel disconnects for a few seconds)."
                        : $"This PC has {status.CurrentVersion}. Updating takes a moment; the wheel disconnects for a few seconds."),
                status.Error is null ? "Update" : "Try again"));
        }

        if (status.PeerName is not null && status.Phase == UpdatePhase.Idle)
        {
            if (status.PeerVersion is null)
            {
                notices.Add(new DashboardWarning(
                    PeerOutdatedId,
                    $"LogiSteam on {status.PeerName} is out of date",
                    "Install the latest version there once. After that it can update itself from this window."));
            }
            else if (AppVersion.IsNewer(status.CurrentVersion, status.PeerVersion))
            {
                notices.Add(new DashboardWarning(
                    PeerOutdatedId,
                    $"LogiSteam on {status.PeerName} is out of date",
                    $"It has {status.PeerVersion}; this PC has {status.CurrentVersion}. Open LogiSteam there and choose Update."));
            }
        }

        return notices.Count == 0 ? state : state with { Warnings = [.. state.Warnings, .. notices] };
    }
}
