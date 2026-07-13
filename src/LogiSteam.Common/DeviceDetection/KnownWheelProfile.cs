using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// A wheel model's full set of confirmed USB personas -- e.g. the G29 enumerates as a completely
/// different VID:PID depending on its physical PS3/PS4/native mode switch position, for the
/// exact same physical unit. Only add profiles backed by a directly-observed real device (see
/// UsbHardwareId's remarks on why a guessed/looked-up PID has been wrong before).
/// </summary>
public sealed record KnownWheelProfile
{
    public required string DisplayName { get; init; }
    public required IReadOnlyList<UsbHardwareId> HardwareIds { get; init; }
}
