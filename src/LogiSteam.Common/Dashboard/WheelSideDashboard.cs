using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Dashboard;

/// <summary>What the Wheel PC role currently knows, reduced to what the main window needs.</summary>
/// <param name="WheelName">The identified wheel's name, even while it's unplugged; null if none has ever been identified.</param>
/// <param name="WheelPresent">usbipd-win currently sees the wheel plugged in.</param>
/// <param name="Shared">usbipd-win has bound the wheel for sharing.</param>
/// <param name="SharingAllowed">The one-time usbipd-win AutoBind permission is in place.</param>
/// <param name="Problem">A blocking error in plain words (e.g. usbipd-win missing), or null.</param>
/// <param name="GamePcName">The Game PC currently answering this PC's beacon, or null.</param>
/// <param name="GamePcAttached">That Game PC reports it has attached the wheel.</param>
public sealed record WheelSideInputs(
    string? WheelName,
    bool WheelPresent,
    bool Shared,
    bool SharingAllowed,
    string? Problem,
    string? GamePcName,
    bool GamePcAttached);

public static class WheelSideDashboard
{
    public const string SharePermissionWarningId = "share-permission";

    public static DashboardState Build(WheelSideInputs i, GHubStatus gHub)
    {
        var wheel = i.WheelName ?? "racing wheel";
        var connected = i.WheelPresent && i.SharingAllowed && i.GamePcName is not null && i.GamePcAttached && i.Shared;

        var wheelStep = i.Problem is not null ? new DashboardStep("Racing wheel", "Can't check right now", StepState.Problem)
            : i.WheelPresent ? new DashboardStep("Racing wheel", i.WheelName ?? "Detected", StepState.Done)
            : new DashboardStep("Racing wheel", i.WheelName is null ? "Searching..." : $"Waiting for {i.WheelName}", StepState.Active);

        var sharingStep = !i.WheelPresent ? new DashboardStep("Shared on your network", "Waiting for the wheel", StepState.Pending)
            : i.SharingAllowed ? new DashboardStep("Shared on your network", "Ready", StepState.Done)
            : new DashboardStep("Shared on your network", "Needs your permission", StepState.Problem);

        var gamePcStep = !i.WheelPresent || !i.SharingAllowed ? new DashboardStep("Game PC", "Waiting", StepState.Pending)
            : i.GamePcName is null ? new DashboardStep("Game PC", "Searching...", StepState.Active)
            : new DashboardStep("Game PC", i.GamePcName, StepState.Done);

        var attachedStep = gamePcStep.State != StepState.Done ? new DashboardStep("Wheel in use by Game PC", "Waiting", StepState.Pending)
            : connected ? new DashboardStep("Wheel in use by Game PC", "Connected", StepState.Done)
            : new DashboardStep("Wheel in use by Game PC", "Connecting...", StepState.Active);

        IReadOnlyList<DashboardStep> steps = [wheelStep, sharingStep, gamePcStep, attachedStep];

        DashboardState state;
        if (i.Problem is not null)
        {
            state = new("Something needs attention", i.Problem, TrayStatusLevel.Error, steps, []);
        }
        else if (!i.WheelPresent)
        {
            state = i.WheelName is null
                ? new("Plug in your racing wheel", "LogiSteam will find it automatically - there's nothing to pick or set up.", TrayStatusLevel.Idle, steps, [])
                : new($"Waiting for your {i.WheelName}", "Plug it into this PC and make sure it's powered on.", TrayStatusLevel.Idle, steps, []);
        }
        else if (!i.SharingAllowed)
        {
            state = new(
                "One-time permission needed",
                $"Windows needs your OK before LogiSteam can share your {wheel} with your Game PC.",
                TrayStatusLevel.Warning, steps,
                [new DashboardWarning(
                    SharePermissionWarningId,
                    "Allow LogiSteam to share the wheel",
                    "Click Allow, then choose Yes on the Windows prompt. You only need to do this once.",
                    "Allow")]);
        }
        else if (i.GamePcName is null)
        {
            state = new(
                "Looking for your Game PC",
                $"Your {wheel} is ready. Open LogiSteam on the PC that runs your games and switch it to Game PC.",
                TrayStatusLevel.Idle, steps, []);
        }
        else if (!connected)
        {
            state = new($"Connecting to {i.GamePcName}", $"Handing your {wheel} over - this takes a few seconds.", TrayStatusLevel.Idle, steps, []);
        }
        else
        {
            state = new("Wheel connected", $"Your {wheel} is live on {i.GamePcName}. Start your game and drive.", TrayStatusLevel.Connected, steps, []);
        }

        return GHubWarnings.Apply(state, gHub);
    }
}
