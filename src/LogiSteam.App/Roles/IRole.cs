using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;

namespace LogiSteam.Roles;

/// <summary>An entry in the main window's Advanced section.</summary>
public sealed record RoleAction(string Title, string Description, string Glyph, Action Invoke);

/// <summary>
/// One end of the connection. The single LogiSteam app runs exactly one role at a time
/// (<see cref="WheelRole"/> or <see cref="GameRole"/>), chosen with the switch in the main
/// window; switching stops one and starts the other. Everything here is called on the UI thread.
/// </summary>
internal interface IRole
{
    AppRole Kind { get; }

    /// <summary>Raised on the UI thread whenever <see cref="BuildDashboard"/> would return something new.</summary>
    event Action? Changed;

    void Start();

    /// <summary>Releases the network ports and any attached wheel so the other role can start cleanly.</summary>
    Task StopAsync();

    DashboardState BuildDashboard();

    /// <summary>The PC at the other end, if one is currently connected, and the LogiSteam version it reported (null before 2.1).</summary>
    (string Name, string? Version)? Peer { get; }

    /// <summary>The one action offered as a button on the dashboard itself (not tucked under Advanced), if the role has one.</summary>
    RoleAction? PrimaryAction { get; }

    IReadOnlyList<RoleAction> AdvancedActions { get; }

    /// <summary>Handles the button on a warning this role raised (see <see cref="DashboardWarning.Id"/>).</summary>
    void OnWarningAction(string warningId);

    /// <summary>The PC just woke from sleep; network sockets may be silently dead.</summary>
    void OnResume();
}
