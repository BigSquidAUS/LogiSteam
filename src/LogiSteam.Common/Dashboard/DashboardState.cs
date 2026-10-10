using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Dashboard;

public enum StepState
{
    /// <summary>Can't start until an earlier step is done.</summary>
    Pending,

    /// <summary>Being worked on right now (searching, connecting).</summary>
    Active,

    Done,

    /// <summary>Needs the user's attention.</summary>
    Problem,
}

/// <summary>One line of the "how far along is the connection" checklist.</summary>
public sealed record DashboardStep(string Title, string Detail, StepState State);

/// <summary>
/// Something the user should fix. <see cref="Id"/> is stable (see <see cref="GHubWarnings"/>) so
/// the app can tell a new warning from one it has already announced, and knows what the
/// warning's button should do.
/// </summary>
public sealed record DashboardWarning(string Id, string Title, string Detail, string? ActionLabel = null);

/// <summary>
/// Everything the main window shows, in plain words, for whichever role this PC is playing.
/// Built by the pure <see cref="WheelSideDashboard"/>/<see cref="GameSideDashboard"/> functions
/// so the wording and the state logic are unit-testable without any UI.
/// </summary>
public sealed record DashboardState(
    string Headline,
    string Detail,
    TrayStatusLevel Level,
    IReadOnlyList<DashboardStep> Steps,
    IReadOnlyList<DashboardWarning> Warnings);

/// <summary>
/// What's known about Logitech G HUB on this PC and on the PC at the other end, and where it is
/// required. G HUB is always required on the Game PC (that's where the game talks to the wheel);
/// whether the Wheel PC needs it too is a user setting, off by default.
/// </summary>
/// <param name="RunningOnPeer">Null when there is no peer yet, or it runs a LogiSteam too old to report it.</param>
/// <param name="RequiredHere">False on a Wheel PC unless "require G HUB on the Wheel PC too" is on.</param>
/// <param name="RequiredOnPeer">False on a Game PC (whose peer is the Wheel PC) unless that setting is on.</param>
public sealed record GHubStatus(
    bool RunningHere,
    bool InstalledHere,
    bool? RunningOnPeer = null,
    string? PeerName = null,
    bool RequiredHere = true,
    bool RequiredOnPeer = true);

public static class GHubWarnings
{
    public const string LocalId = "ghub-local";
    public const string PeerId = "ghub-peer";

    public static IReadOnlyList<DashboardWarning> For(GHubStatus gHub)
    {
        var warnings = new List<DashboardWarning>();
        if (gHub.RequiredHere && !gHub.RunningHere)
        {
            warnings.Add(new DashboardWarning(
                LocalId,
                "G HUB isn't running on this PC",
                "Logitech G HUB needs to be open here for the wheel to work properly.",
                gHub.InstalledHere ? "Open G HUB" : "Get G HUB"));
        }

        if (gHub.RequiredOnPeer && gHub.RunningOnPeer == false)
        {
            warnings.Add(new DashboardWarning(
                PeerId,
                $"G HUB isn't running on {PeerLabel(gHub)}",
                "Open Logitech G HUB on that PC for the wheel to work properly."));
        }

        return warnings;
    }

    public static DashboardStep Step(GHubStatus gHub)
    {
        const string title = "Logitech G HUB";
        if (gHub.RequiredHere && !gHub.RunningHere)
        {
            return new DashboardStep(title, "Not running on this PC", StepState.Problem);
        }

        if (gHub.RequiredOnPeer && gHub.RunningOnPeer == false)
        {
            return new DashboardStep(title, $"Not running on {PeerLabel(gHub)}", StepState.Problem);
        }

        var confirmedOnPeer = gHub.RequiredOnPeer && gHub.RunningOnPeer == true;
        return (gHub.RequiredHere, confirmedOnPeer) switch
        {
            (true, true) => new DashboardStep(title, "Running on both PCs", StepState.Done),
            (true, false) => new DashboardStep(title, "Running on this PC", StepState.Done),
            (false, true) => new DashboardStep(title, $"Running on {PeerLabel(gHub)}", StepState.Done),
            (false, false) => new DashboardStep(title, "Needed on the Game PC", StepState.Pending),
        };
    }

    private static string PeerLabel(GHubStatus gHub) => gHub.PeerName ?? "the other PC";

    /// <summary>
    /// A connection that is otherwise complete still isn't "all good" without G HUB, so it is
    /// shown (and coloured) as needing attention rather than as connected.
    /// </summary>
    public static DashboardState Apply(DashboardState state, GHubStatus gHub)
    {
        var gHubWarnings = For(gHub);
        var warnings = state.Warnings.Concat(gHubWarnings).ToList();
        var steps = state.Steps.Append(Step(gHub)).ToList();
        if (gHubWarnings.Count == 0 || state.Level != TrayStatusLevel.Connected)
        {
            return state with { Steps = steps, Warnings = warnings };
        }

        return state with
        {
            Level = TrayStatusLevel.Warning,
            Detail = "The wheel is connected, but it needs Logitech G HUB open to work properly.",
            Steps = steps,
            Warnings = warnings,
        };
    }
}
