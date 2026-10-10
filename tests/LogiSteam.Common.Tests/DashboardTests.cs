using LogiSteam.Common.Dashboard;
using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Tests;

public class DashboardTests
{
    private static readonly GHubStatus GHubFine = new(RunningHere: true, InstalledHere: true, RunningOnPeer: true, PeerName: "OTHER-PC");

    private static WheelSideInputs WheelConnected => new(
        WheelName: "Logitech G29", WheelPresent: true, Shared: true, SharingAllowed: true,
        Problem: null, GamePcName: "GAME-PC", GamePcAttached: true);

    private static GameSideInputs GameConnected => new(WheelPcName: "WHEEL-PC", WheelName: "Logitech G29", Attached: true, Problem: null);

    [Fact]
    public void WheelSide_IsConnected_WhenEverythingIsInPlace()
    {
        var state = WheelSideDashboard.Build(WheelConnected, GHubFine);

        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Equal("Wheel connected", state.Headline);
        Assert.Empty(state.Warnings);
        Assert.All(state.Steps, step => Assert.Equal(StepState.Done, step.State));
    }

    [Fact]
    public void WheelSide_AsksForTheWheel_WhenNoneHasEverBeenIdentified()
    {
        var state = WheelSideDashboard.Build(
            WheelConnected with { WheelName = null, WheelPresent = false, GamePcName = null, GamePcAttached = false }, GHubFine);

        Assert.Equal(TrayStatusLevel.Idle, state.Level);
        Assert.Equal("Plug in your racing wheel", state.Headline);
        Assert.Equal(StepState.Active, state.Steps[0].State);
    }

    [Fact]
    public void WheelSide_OffersAnAllowButton_WhenSharingPermissionIsMissing()
    {
        var state = WheelSideDashboard.Build(WheelConnected with { SharingAllowed = false, GamePcAttached = false }, GHubFine);

        Assert.Equal(TrayStatusLevel.Warning, state.Level);
        var warning = Assert.Single(state.Warnings);
        Assert.Equal(WheelSideDashboard.SharePermissionWarningId, warning.Id);
        Assert.Equal("Allow", warning.ActionLabel);
    }

    [Fact]
    public void WheelSide_ShowsSearching_UntilAGamePcAnswers()
    {
        var state = WheelSideDashboard.Build(WheelConnected with { GamePcName = null, GamePcAttached = false }, GHubFine with { RunningOnPeer = null });

        Assert.Equal("Looking for your Game PC", state.Headline);
        Assert.Equal(TrayStatusLevel.Idle, state.Level);
    }

    [Fact]
    public void WheelSide_ReportsAProblem_AsAnError()
    {
        var state = WheelSideDashboard.Build(WheelConnected with { Problem = "usbipd-win isn't installed." }, GHubFine);

        Assert.Equal(TrayStatusLevel.Error, state.Level);
        Assert.Equal("usbipd-win isn't installed.", state.Detail);
    }

    [Fact]
    public void GameSide_IsConnected_WhenAttached()
    {
        var state = GameSideDashboard.Build(GameConnected, GHubFine);

        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Contains("Logitech G29", state.Detail);
        Assert.Contains("WHEEL-PC", state.Detail);
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void GameSide_ShowsSearching_WhenNoWheelPcIsVisible()
    {
        var state = GameSideDashboard.Build(new GameSideInputs(null, null, Attached: false, Problem: null), GHubFine with { RunningOnPeer = null });

        Assert.Equal("Looking for your Wheel PC", state.Headline);
        Assert.Equal(TrayStatusLevel.Idle, state.Level);
    }

    [Fact]
    public void GameSide_ReportsAnAttachFailure_AsAnError()
    {
        var state = GameSideDashboard.Build(GameConnected with { Attached = false, Problem = "Attach failed" }, GHubFine);

        Assert.Equal(TrayStatusLevel.Error, state.Level);
        Assert.Equal("Attach failed", state.Detail);
    }

    [Fact]
    public void GHub_NotRunningHere_WarnsWithAnOpenButton_OnBothRoles()
    {
        var gHub = GHubFine with { RunningHere = false };

        foreach (var state in new[] { WheelSideDashboard.Build(WheelConnected, gHub), GameSideDashboard.Build(GameConnected, gHub) })
        {
            var warning = Assert.Single(state.Warnings);
            Assert.Equal(GHubWarnings.LocalId, warning.Id);
            Assert.Equal("Open G HUB", warning.ActionLabel);
            Assert.Equal(TrayStatusLevel.Warning, state.Level); // connected, but not "all good"
            Assert.Equal(StepState.Problem, state.Steps[^1].State);
        }
    }

    [Fact]
    public void GHub_NotInstalledHere_OffersToGetIt()
    {
        var state = GameSideDashboard.Build(GameConnected, GHubFine with { RunningHere = false, InstalledHere = false });

        Assert.Equal("Get G HUB", Assert.Single(state.Warnings).ActionLabel);
    }

    [Fact]
    public void GHub_NotRunningOnTheOtherPc_WarnsAndNamesThatPc()
    {
        var state = GameSideDashboard.Build(GameConnected, GHubFine with { RunningOnPeer = false, PeerName = "WHEEL-PC" });

        var warning = Assert.Single(state.Warnings);
        Assert.Equal(GHubWarnings.PeerId, warning.Id);
        Assert.Contains("WHEEL-PC", warning.Title);
        Assert.Null(warning.ActionLabel);
        Assert.Equal(TrayStatusLevel.Warning, state.Level);
    }

    [Fact]
    public void GHub_MissingOnBothPcs_RaisesBothWarnings()
    {
        var state = WheelSideDashboard.Build(WheelConnected, GHubFine with { RunningHere = false, RunningOnPeer = false });

        Assert.Equal([GHubWarnings.LocalId, GHubWarnings.PeerId], state.Warnings.Select(w => w.Id));
    }

    [Fact]
    public void GHub_UnknownOnTheOtherPc_IsNotAWarning()
    {
        // No peer yet, or a peer running an older LogiSteam that doesn't report it.
        var state = GameSideDashboard.Build(GameConnected, GHubFine with { RunningOnPeer = null });

        Assert.Empty(state.Warnings);
        Assert.Equal(TrayStatusLevel.Connected, state.Level);
    }

    // ---- G HUB is only required on the Game PC unless the Advanced fallback is switched on ----

    private static readonly GHubStatus NotRequiredOnWheelPc_SeenFromWheelPc =
        new(RunningHere: false, InstalledHere: true, RunningOnPeer: true, PeerName: "GAME-PC", RequiredHere: false);

    private static readonly GHubStatus NotRequiredOnWheelPc_SeenFromGamePc =
        new(RunningHere: true, InstalledHere: true, RunningOnPeer: false, PeerName: "WHEEL-PC", RequiredOnPeer: false);

    [Fact]
    public void WheelPc_DoesNotWarnAboutItsOwnGHub_ByDefault()
    {
        var state = WheelSideDashboard.Build(WheelConnected, NotRequiredOnWheelPc_SeenFromWheelPc);

        Assert.Empty(state.Warnings);
        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Equal("Running on GAME-PC", state.Steps[^1].Detail);
        Assert.Equal(StepState.Done, state.Steps[^1].State);
    }

    [Fact]
    public void WheelPc_StillWarns_WhenGHubIsNotRunningOnTheGamePc()
    {
        var state = WheelSideDashboard.Build(WheelConnected, NotRequiredOnWheelPc_SeenFromWheelPc with { RunningOnPeer = false });

        Assert.Equal(GHubWarnings.PeerId, Assert.Single(state.Warnings).Id);
        Assert.Equal(TrayStatusLevel.Warning, state.Level);
    }

    [Fact]
    public void GamePc_DoesNotWarnAboutTheWheelPcsGHub_ByDefault()
    {
        var state = GameSideDashboard.Build(GameConnected, NotRequiredOnWheelPc_SeenFromGamePc);

        Assert.Empty(state.Warnings);
        Assert.Equal(TrayStatusLevel.Connected, state.Level);
        Assert.Equal("Running on this PC", state.Steps[^1].Detail);
    }

    [Fact]
    public void GamePc_AlwaysWarnsAboutItsOwnGHub()
    {
        var state = GameSideDashboard.Build(GameConnected, NotRequiredOnWheelPc_SeenFromGamePc with { RunningHere = false });

        Assert.Equal(GHubWarnings.LocalId, Assert.Single(state.Warnings).Id);
    }

    [Fact]
    public void Fallback_RequiringGHubOnTheWheelPc_WarnsOnBothSides()
    {
        var fromWheelPc = WheelSideDashboard.Build(WheelConnected, NotRequiredOnWheelPc_SeenFromWheelPc with { RequiredHere = true });
        var fromGamePc = GameSideDashboard.Build(GameConnected, NotRequiredOnWheelPc_SeenFromGamePc with { RequiredOnPeer = true });

        Assert.Equal(GHubWarnings.LocalId, Assert.Single(fromWheelPc.Warnings).Id);
        Assert.Equal(GHubWarnings.PeerId, Assert.Single(fromGamePc.Warnings).Id);
    }

    [Fact]
    public void WheelPc_WithNoGamePcYet_ShowsGHubAsNotYetChecked()
    {
        var state = WheelSideDashboard.Build(
            WheelConnected with { GamePcName = null, GamePcAttached = false },
            new GHubStatus(RunningHere: false, InstalledHere: true, RequiredHere: false));

        Assert.Empty(state.Warnings);
        Assert.Equal(StepState.Pending, state.Steps[^1].State);
    }

    [Fact]
    public void GHub_Warning_IsShownEvenBeforeTheWheelIsConnected()
    {
        var state = GameSideDashboard.Build(new GameSideInputs(null, null, Attached: false, Problem: null), new GHubStatus(RunningHere: false, InstalledHere: true));

        Assert.Equal(GHubWarnings.LocalId, Assert.Single(state.Warnings).Id);
        Assert.Equal(TrayStatusLevel.Idle, state.Level); // still just searching
    }
}
