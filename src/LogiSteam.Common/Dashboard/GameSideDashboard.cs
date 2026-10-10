using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Dashboard;

/// <summary>What the Game PC role currently knows, reduced to what the main window needs.</summary>
/// <param name="WheelPcName">The Wheel PC whose beacon is currently visible, or null.</param>
/// <param name="WheelName">The wheel that PC is offering ("Logitech G29"), if it said.</param>
/// <param name="Attached">The wheel is attached to this PC right now.</param>
/// <param name="Problem">The most recent attach error in plain words, or null.</param>
public sealed record GameSideInputs(string? WheelPcName, string? WheelName, bool Attached, string? Problem);

public static class GameSideDashboard
{
    public static DashboardState Build(GameSideInputs i, GHubStatus gHub)
    {
        var wheel = i.WheelName ?? "racing wheel";
        var found = i.WheelPcName is not null;

        var wheelPcStep = found
            ? new DashboardStep("Wheel PC", i.WheelPcName!, StepState.Done)
            : new DashboardStep("Wheel PC", "Searching your network...", StepState.Active);

        var wheelStep = found
            ? new DashboardStep("Racing wheel", i.WheelName ?? "Detected", StepState.Done)
            : new DashboardStep("Racing wheel", "Waiting", StepState.Pending);

        var attachedStep = i.Attached ? new DashboardStep("Connected to this PC", "Ready to use", StepState.Done)
            : i.Problem is not null ? new DashboardStep("Connected to this PC", "Couldn't connect", StepState.Problem)
            : found ? new DashboardStep("Connected to this PC", "Connecting...", StepState.Active)
            : new DashboardStep("Connected to this PC", "Waiting", StepState.Pending);

        IReadOnlyList<DashboardStep> steps = [wheelPcStep, wheelStep, attachedStep];

        DashboardState state;
        if (i.Attached)
        {
            state = new(
                "Wheel connected",
                $"Your {wheel} from {i.WheelPcName ?? "the Wheel PC"} is ready. Windows and your games see it as plugged in here.",
                TrayStatusLevel.Connected, steps, []);
        }
        else if (i.Problem is not null)
        {
            state = new("Couldn't connect the wheel", i.Problem, TrayStatusLevel.Error, steps, []);
        }
        else if (found)
        {
            state = new($"Connecting your {wheel}", $"Found it on {i.WheelPcName} - this takes a few seconds.", TrayStatusLevel.Idle, steps, []);
        }
        else
        {
            state = new(
                "Looking for your Wheel PC",
                "Open LogiSteam on the PC your wheel is plugged into and switch it to Wheel PC. Both PCs need to be on the same network.",
                TrayStatusLevel.Idle, steps, []);
        }

        return GHubWarnings.Apply(state, gHub);
    }
}
