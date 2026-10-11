using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Dashboard;

/// <summary>An additional device the user added on the Wheel PC, as the dashboard sees it.</summary>
/// <param name="Present">It's plugged in right now.</param>
/// <param name="Attached">The Game PC reports it has attached it.</param>
public sealed record ExtraDeviceStatus(string Name, bool Present, bool Attached);

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
    bool GamePcAttached)
{
    /// <summary>Additional devices shared alongside (or instead of) a wheel.</summary>
    public IReadOnlyList<ExtraDeviceStatus> Extras { get; init; } = [];
}

public static class WheelSideDashboard
{
    public const string SharePermissionWarningId = "share-permission";

    /// <param name="gHub">Null when no Logitech wheel is involved (only additional devices), so G HUB is irrelevant.</param>
    public static DashboardState Build(WheelSideInputs i, GHubStatus? gHub)
    {
        var extras = i.Extras;

        // Someone sharing only a HOTAS or a Stream Deck has no wheel and shouldn't be told to plug one in.
        var showWheel = i.WheelName is not null || extras.Count == 0;
        var anyPresent = i.WheelPresent || extras.Any(e => e.Present);
        var wheelConnected = i.WheelPresent && i.SharingAllowed && i.GamePcName is not null && i.GamePcAttached && i.Shared;
        var readyForGamePc = anyPresent && i.SharingAllowed;
        var gamePcFound = readyForGamePc && i.GamePcName is not null;
        var anyConnected = wheelConnected || (gamePcFound && extras.Any(e => e.Present && e.Attached));

        var steps = new List<DashboardStep>();

        if (showWheel)
        {
            var wheelTitle = i.WheelName ?? "Racing wheel";
            steps.Add(i.Problem is not null ? new DashboardStep(wheelTitle, "Can't check right now", StepState.Problem)
                : !i.WheelPresent ? new DashboardStep(wheelTitle, i.WheelName is null ? "Searching..." : "Waiting for it to be plugged in", StepState.Active)
                : wheelConnected ? new DashboardStep(wheelTitle, "Connected", StepState.Done)
                : gamePcFound ? new DashboardStep(wheelTitle, "Connecting...", StepState.Active)
                : new DashboardStep(wheelTitle, i.SharingAllowed ? "Ready" : "Detected", StepState.Done));
        }

        foreach (var extra in extras)
        {
            steps.Add(!extra.Present ? new DashboardStep(extra.Name, "Not plugged in", StepState.Pending)
                : gamePcFound && extra.Attached ? new DashboardStep(extra.Name, "Connected", StepState.Done)
                : gamePcFound ? new DashboardStep(extra.Name, "Connecting...", StepState.Active)
                : new DashboardStep(extra.Name, "Ready", StepState.Done));
        }

        steps.Add(!anyPresent ? new DashboardStep("Shared on your network", "Waiting", StepState.Pending)
            : i.SharingAllowed ? new DashboardStep("Shared on your network", "Ready", StepState.Done)
            : new DashboardStep("Shared on your network", "Needs your permission", StepState.Problem));

        steps.Add(!readyForGamePc ? new DashboardStep("Game PC", "Waiting", StepState.Pending)
            : i.GamePcName is null ? new DashboardStep("Game PC", "Searching...", StepState.Active)
            : new DashboardStep("Game PC", i.GamePcName, StepState.Done));

        // What to call "the thing being shared" in sentences.
        var subject = i.WheelPresent || extras.Count == 0 ? i.WheelName ?? "racing wheel"
            : extras.Count(e => e.Present) == 1 ? extras.First(e => e.Present).Name
            : extras.Any(e => e.Present) ? "devices"
            : i.WheelName ?? extras[0].Name;

        DashboardState state;
        if (i.Problem is not null)
        {
            state = new("Something needs attention", i.Problem, TrayStatusLevel.Error, steps, []);
        }
        else if (!anyPresent)
        {
            state = i.WheelName is null && extras.Count == 0
                ? new("Plug in your racing wheel", "LogiSteam will find it automatically - there's nothing to pick or set up.", TrayStatusLevel.Idle, steps, [])
                : new($"Waiting for your {subject}", "Plug it into this PC and make sure it's powered on.", TrayStatusLevel.Idle, steps, []);
        }
        else if (!i.SharingAllowed)
        {
            state = new(
                "One-time permission needed",
                $"Windows needs your OK before LogiSteam can share your {subject} with your Game PC.",
                TrayStatusLevel.Warning, steps,
                [new DashboardWarning(
                    SharePermissionWarningId,
                    "Allow LogiSteam to share your devices",
                    "Click Allow, then choose Yes on the Windows prompt. This is needed once for each device you add.",
                    "Allow")]);
        }
        else if (i.GamePcName is null)
        {
            state = new(
                "Looking for your Game PC",
                $"Your {subject} {(subject == "devices" ? "are" : "is")} ready. Open LogiSteam on the PC that runs your games and switch it to Game PC.",
                TrayStatusLevel.Idle, steps, []);
        }
        else if (!anyConnected)
        {
            state = new($"Connecting to {i.GamePcName}", $"Handing your {subject} over - this takes a few seconds.", TrayStatusLevel.Idle, steps, []);
        }
        else
        {
            var others = extras.Count(e => e.Present && e.Attached);
            var headline = wheelConnected ? "Wheel connected" : others == 1 ? $"{extras.First(e => e.Present && e.Attached).Name} connected" : "Devices connected";
            var what = wheelConnected
                ? $"Your {i.WheelName ?? "racing wheel"}{(others > 0 ? $" and {others} other device{(others == 1 ? "" : "s")}" : "")}"
                : others == 1 ? $"Your {extras.First(e => e.Present && e.Attached).Name}" : $"{others} devices";
            var verb = (wheelConnected ? others > 0 : others > 1) ? "are" : "is";
            state = new(headline, $"{what} {verb} live on {i.GamePcName}. Start your game and drive.", TrayStatusLevel.Connected, steps, []);
        }

        return gHub is null ? state : GHubWarnings.Apply(state, gHub);
    }
}
