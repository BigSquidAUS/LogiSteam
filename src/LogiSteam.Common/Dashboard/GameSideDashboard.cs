using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Dashboard;

public enum OfferedDeviceState
{
    Connecting,
    Connected,

    /// <summary>The last attempt to attach it failed; it is retried.</summary>
    Failed,

    /// <summary>The user chose not to use it on this PC, so it stays with the Wheel PC.</summary>
    Released,
}

/// <summary>One device the Wheel PC is offering, as the Game PC's dashboard sees it.</summary>
/// <param name="IsWheel">The racing wheel, as opposed to an additional device the user added.</param>
/// <param name="Error">Why the last attach failed, when <see cref="OfferedDeviceState.Failed"/>.</param>
public sealed record OfferedDeviceStatus(string Name, bool IsWheel, OfferedDeviceState State, string? Error = null);

/// <summary>What the Game PC role currently knows, reduced to what the main window needs.</summary>
/// <param name="WheelPcName">The Wheel PC whose beacon is currently visible, or null.</param>
/// <param name="WheelName">The wheel that PC is offering ("Logitech G29"), if it said.</param>
/// <param name="Attached">The wheel is attached to this PC right now.</param>
/// <param name="Problem">The most recent attach error in plain words, or null.</param>
public sealed record GameSideInputs(string? WheelPcName, string? WheelName, bool Attached, string? Problem)
{
    /// <summary>
    /// Every device on offer. When left empty (a caller that only knows about one wheel), the
    /// single wheel described by the parameters above is used.
    /// </summary>
    public IReadOnlyList<OfferedDeviceStatus> Devices { get; init; } = [];
}

public static class GameSideDashboard
{
    public const string DeviceFailedWarningPrefix = "device-failed:";

    /// <param name="gHub">Null when no Logitech wheel is on offer (only additional devices), so G HUB is irrelevant.</param>
    public static DashboardState Build(GameSideInputs i, GHubStatus? gHub)
    {
        var found = i.WheelPcName is not null;
        var devices = i.Devices.Count > 0 || !found
            ? i.Devices
            : [new OfferedDeviceStatus(
                i.WheelName ?? "Racing wheel", IsWheel: true,
                i.Attached ? OfferedDeviceState.Connected : i.Problem is not null ? OfferedDeviceState.Failed : OfferedDeviceState.Connecting,
                i.Problem)];

        var steps = new List<DashboardStep>
        {
            found
                ? new DashboardStep("Wheel PC", i.WheelPcName!, StepState.Done)
                : new DashboardStep("Wheel PC", "Searching your network...", StepState.Active),
        };

        if (devices.Count == 0)
        {
            steps.Add(new DashboardStep("Racing wheel", "Waiting", StepState.Pending));
        }

        foreach (var device in devices)
        {
            steps.Add(device.State switch
            {
                OfferedDeviceState.Connected => new DashboardStep(device.Name, "Connected", StepState.Done),
                OfferedDeviceState.Failed => new DashboardStep(device.Name, "Couldn't connect", StepState.Problem),
                OfferedDeviceState.Released => new DashboardStep(device.Name, "Not used on this PC", StepState.Pending),
                _ => new DashboardStep(device.Name, "Connecting...", StepState.Active),
            });
        }

        var wanted = devices.Where(d => d.State != OfferedDeviceState.Released).ToList();
        var connected = wanted.Where(d => d.State == OfferedDeviceState.Connected).ToList();
        var wheel = devices.FirstOrDefault(d => d.IsWheel);
        var wheelConnected = wheel?.State == OfferedDeviceState.Connected;

        // The device the headline is about: the wheel if one is on offer, otherwise the first device.
        var primary = wheel is { State: not OfferedDeviceState.Released } ? wheel : wanted.FirstOrDefault();
        var from = i.WheelPcName ?? "the Wheel PC";

        DashboardState state;
        if (!found)
        {
            state = new(
                "Looking for your Wheel PC",
                "Open LogiSteam on the PC your wheel is plugged into and switch it to Wheel PC. Both PCs need to be on the same network.",
                TrayStatusLevel.Idle, steps, []);
        }
        else if (primary is null)
        {
            state = new(
                "Nothing to connect",
                $"Every device {from} offers is switched off for this PC. Turn one back on under Advanced.",
                TrayStatusLevel.Idle, steps, []);
        }
        else if (primary.State == OfferedDeviceState.Connected)
        {
            var others = connected.Count - 1;
            var headline = wheelConnected ? "Wheel connected" : connected.Count == 1 ? $"{primary.Name} connected" : "Devices connected";
            var what = $"Your {primary.Name}{(others > 0 ? $" and {others} other device{(others == 1 ? "" : "s")}" : "")}";
            state = new(
                headline,
                $"{what} from {from} {(others > 0 ? "are" : "is")} ready. Windows and your games see {(others > 0 ? "them" : "it")} as plugged in here.",
                TrayStatusLevel.Connected, steps, []);
        }
        else if (primary.State == OfferedDeviceState.Failed)
        {
            state = new(
                wheel is not null ? "Couldn't connect the wheel" : $"Couldn't connect {primary.Name}",
                primary.Error ?? "The device couldn't be attached to this PC. Trying again shortly.",
                TrayStatusLevel.Error, steps, []);
        }
        else
        {
            state = new($"Connecting your {primary.Name}", $"Found it on {from} - this takes a few seconds.", TrayStatusLevel.Idle, steps, []);
        }

        // An additional device that won't connect is reported without spoiling the wheel's status.
        var failedOthers = wanted
            .Where(d => d.State == OfferedDeviceState.Failed && !ReferenceEquals(d, primary))
            .Select(d => new DashboardWarning(
                DeviceFailedWarningPrefix + d.Name,
                $"{d.Name} couldn't connect",
                d.Error ?? "LogiSteam will keep trying."))
            .ToList();
        if (failedOthers.Count > 0)
        {
            state = state with { Warnings = [.. state.Warnings, .. failedOthers] };
        }

        return gHub is null ? state : GHubWarnings.Apply(state, gHub);
    }
}
