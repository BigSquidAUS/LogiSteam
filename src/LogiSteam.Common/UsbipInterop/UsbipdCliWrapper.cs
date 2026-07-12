using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogiSteam.Common.UsbipInterop;

/// <summary>
/// Wraps usbipd.exe (dorssel/usbipd-win), which runs on the Wheel PC and shares the physically
/// attached G29 over the LAN. LogiSteam never calls `bind` directly -- it registers a persistent
/// AutoBind policy once, and usbipd-win shares the device automatically from then on whenever a
/// remote machine asks for it.
/// </summary>
public sealed class UsbipdCliWrapper(string executablePath = "usbipd")
{
    private static readonly Regex PolicyAutoBindLine = new(
        @"AutoBind", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<UsbipdState> GetStateAsync(CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(executablePath, ["state"], ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UsbipdCommandException("usbipd state", result);
        }

        return JsonSerializer.Deserialize<UsbipdState>(result.StdOut)
            ?? new UsbipdState();
    }

    public async Task<UsbipdDevice?> FindDeviceAsync(UsbHardwareId hardwareId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct).ConfigureAwait(false);
        return state.Devices.FirstOrDefault(d => d.HardwareId == hardwareId);
    }

    /// <summary>
    /// Ensures a persistent "Allow AutoBind" policy exists for the given hardware id, so the
    /// device is shared automatically the moment a remote machine attaches -- with no ongoing
    /// action required from LogiSteam. Idempotent: safe to call on every startup.
    /// </summary>
    public async Task<bool> EnsureAutoBindPolicyAsync(UsbHardwareId hardwareId, CancellationToken ct = default)
    {
        var listResult = await ProcessRunner.RunAsync(executablePath, ["policy", "list"], ct).ConfigureAwait(false);
        if (listResult.Succeeded && PolicyAlreadyExists(listResult.StdOut, hardwareId))
        {
            return true;
        }

        string[] addArgs = ["policy", "add", "--effect", "Allow", "--operation", "AutoBind", "--hardware-id", hardwareId.ToString()];

        var addResult = await ProcessRunner.RunAsync(executablePath, addArgs, ct).ConfigureAwait(false);
        if (addResult.Succeeded)
        {
            return true;
        }

        // `policy add` requires Administrator rights; this is the one operation LogiSteam ever
        // needs elevated, and only the first time it's missing -- one UAC prompt, not a
        // requirement to run the whole tray app elevated on every launch.
        var elevatedExitCode = await ProcessRunner.RunElevatedAsync(executablePath, addArgs, ct).ConfigureAwait(false);
        return elevatedExitCode == 0;
    }

    /// <summary>
    /// Best-effort text scan of `usbipd policy list` output for an existing AutoBind rule
    /// matching this hardware id. usbipd-win does not expose a JSON/typed form of this command
    /// (unlike `state`), so this is a pragmatic substring match rather than a structured parse --
    /// worst case on a format change is a harmless duplicate policy, not a functional failure.
    /// Public (rather than private) so this parsing logic is unit-testable without spawning a
    /// real usbipd.exe process.
    /// </summary>
    public static bool PolicyAlreadyExists(string policyListOutput, UsbHardwareId hardwareId)
    {
        var needle = hardwareId.ToString();
        return policyListOutput
            .Split('\n')
            .Any(line => PolicyAutoBindLine.IsMatch(line) && line.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class UsbipdCommandException(string command, ProcessResult result)
    : Exception($"'{command}' exited with code {result.ExitCode}: {result.StdErr}");
