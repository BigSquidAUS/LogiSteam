using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogiSteam.Common.UsbipInterop;

/// <summary>
/// Wraps usbipd.exe (dorssel/usbipd-win), which runs on the Wheel PC and shares the physically
/// attached wheel over the LAN. LogiSteam never calls `bind` directly -- it registers a persistent
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

    /// <summary>
    /// Finds the best match for a wheel's candidate hardware ids (e.g. a multi-persona profile's
    /// known personas). See <see cref="SelectBestMatch"/> for the "prefer a live bus id" logic --
    /// a mode switch or usbipd-win's bind-time driver swap can leave more than one candidate in
    /// `usbipd state` simultaneously, only one of which is actually live right now.
    /// </summary>
    public async Task<UsbipdDevice?> FindDeviceAsync(IReadOnlyList<UsbHardwareId> candidateHardwareIds, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct).ConfigureAwait(false);
        return SelectBestMatch(state.Devices, candidateHardwareIds);
    }

    /// <summary>
    /// Picks the best match for a set of candidate hardware ids from `usbipd state`'s device
    /// list -- preferring a device with a live (non-null) BusId over one that only matches
    /// historically (a stale PersistedGuid from a previous bind, BusId null). Public static and
    /// pure (no process spawn), same pattern as <see cref="ParseRemoteDeviceList"/>-style parsers
    /// elsewhere in this codebase, so it's unit-testable without a real usbipd.exe.
    /// </summary>
    public static UsbipdDevice? SelectBestMatch(IReadOnlyList<UsbipdDevice> devices, IReadOnlyList<UsbHardwareId> candidateHardwareIds)
    {
        var candidateSet = candidateHardwareIds.ToHashSet();
        return devices
            .Where(d => d.HardwareId is { } id && candidateSet.Contains(id))
            .OrderByDescending(d => d.BusId is not null)
            .FirstOrDefault();
    }

    /// <summary>
    /// Ensures a persistent "Allow AutoBind" policy exists for every given hardware id, so each
    /// persona of a wheel profile is shared automatically the moment a remote machine attaches --
    /// with no ongoing action required from LogiSteam. Idempotent: safe to call on every startup.
    /// Batches every still-missing policy into a single elevated invocation, so a multi-persona
    /// profile only ever produces one UAC prompt (the first time it's needed), not one per persona.
    /// </summary>
    public async Task<bool> EnsureAutoBindPoliciesAsync(IReadOnlyList<UsbHardwareId> hardwareIds, CancellationToken ct = default)
    {
        var listResult = await ProcessRunner.RunAsync(executablePath, ["policy", "list"], ct).ConfigureAwait(false);
        var existingPolicyText = listResult.Succeeded ? listResult.StdOut : string.Empty;

        var missing = hardwareIds.Where(id => !PolicyAlreadyExists(existingPolicyText, id)).ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        var stillMissing = new List<UsbHardwareId>();
        foreach (var id in missing)
        {
            var addResult = await ProcessRunner.RunAsync(executablePath, PolicyAddArgs(id), ct).ConfigureAwait(false);
            if (!addResult.Succeeded)
            {
                stillMissing.Add(id);
            }
        }

        if (stillMissing.Count == 0)
        {
            return true;
        }

        // `policy add` requires Administrator rights; this is the one operation LogiSteam ever
        // needs elevated, and only the first time each persona is missing -- batched into one UAC
        // prompt covering every still-missing persona at once, not a requirement to run the whole
        // tray app elevated on every launch.
        var elevatedExitCode = await ProcessRunner.RunElevatedBatchAsync(
            executablePath, stillMissing.Select(PolicyAddArgs), ct).ConfigureAwait(false);
        return elevatedExitCode == 0;
    }

    private static string[] PolicyAddArgs(UsbHardwareId hardwareId) =>
        ["policy", "add", "--effect", "Allow", "--operation", "AutoBind", "--hardware-id", hardwareId.ToString()];

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
