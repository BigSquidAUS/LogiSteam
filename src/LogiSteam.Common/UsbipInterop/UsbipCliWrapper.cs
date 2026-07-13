using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LogiSteam.Common.UsbipInterop;

/// <summary>
/// Wraps usbip.exe (vadimgrn/usbip-win2), which runs on the Game PC and attaches the G29 shared
/// by the Wheel PC as if it were physically local (via the VHCI virtual bus driver). Unlike
/// usbipd-win, usbip-win2 has no auto-attach/policy engine -- that gap is exactly what
/// LogiSteam.GameSide's AttachManager fills, driven by BeaconListener discovery.
/// </summary>
public sealed partial class UsbipCliWrapper(string? executablePath = null)
{
    // usbip-win2's own Inno Setup installer script (confirmed by inspecting its source) never
    // adds its install directory to PATH, unlike usbipd-win -- so a bare "usbip" command only
    // works if the user happened to add it manually. Resolve the real install location from the
    // registry instead of assuming PATH resolution.
    private readonly string _executablePath = executablePath ?? ResolveExecutablePath();

    // usbip-win2's AppId, from its own installer script (userspace/innosetup/setup.iss).
    private const string UsbipWin2AppGuid = "{199505B0-B93D-4521-A8C7-897818E0205A}";

    // Matches a device line from `usbip list -r <host>`, e.g.:
    //         3-2: unknown vendor : unknown product (1005:b113)
    [GeneratedRegex(@"^\s*(?<busid>[0-9]+-[0-9.]+):\s*(?<desc>.+?)\s*\((?<vidpid>[0-9a-fA-F]{4}:[0-9a-fA-F]{4})\)\s*$")]
    private static partial Regex RemoteDeviceLine();

    // Matches the success line from `usbip attach`, e.g.: "successfully attached to port 1"
    [GeneratedRegex(@"port\s+(?<port>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AttachedPort();

    // Matches a "Port NN: ..." block header from `usbip port`. usbip-win2 mirrors the Linux usbip
    // client's CLI (confirmed empirically on a real install: `usbip port` prints nothing at all
    // when no ports are imported -- empty stdout, exit 0), and Linux usbip's well-documented
    // "port" output uses this same "Port NN: <state>" block-header format, which this only needs
    // to detect the presence of a port number for, not parse further.
    [GeneratedRegex(@"^\s*Port\s+(?<port>\d+):", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PortLine();

    private static string ResolveExecutablePath() => TryFindInstalledExecutable() ?? "usbip";

    /// <summary>
    /// Looks up usbip-win2's install location via its Inno Setup uninstall registry entry
    /// (checking both the native and WOW6432Node views, since we can't be sure which one a given
    /// Windows/.NET bitness combination resolves to) and returns the full path to usbip.exe if
    /// found there. Returns null if it can't be found this way, in which case the caller falls
    /// back to a bare "usbip" (PATH-based) invocation.
    /// </summary>
    private static string? TryFindInstalledExecutable()
    {
        string[] uninstallKeyPaths =
        [
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{UsbipWin2AppGuid}_is1",
            $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{UsbipWin2AppGuid}_is1",
        ];

        foreach (var keyPath in uninstallKeyPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                var installLocation = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(installLocation))
                {
                    continue;
                }

                var candidate = Path.Combine(installLocation, "usbip.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (System.Security.SecurityException)
            {
                // No permission to read this key; try the next candidate.
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<RemoteUsbDevice>> ListRemoteAsync(string host, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(_executablePath, ["list", "-r", host], ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UsbipCommandException("usbip list", result);
        }

        return ParseRemoteDeviceList(result.StdOut);
    }

    /// <summary>Parses `usbip list -r &lt;host&gt;` output. Exposed statically so parsing logic is unit-testable without spawning a real process.</summary>
    public static IReadOnlyList<RemoteUsbDevice> ParseRemoteDeviceList(string stdOut)
    {
        var devices = new List<RemoteUsbDevice>();
        foreach (var line in stdOut.Split('\n'))
        {
            var match = RemoteDeviceLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (!UsbHardwareId.TryParse(match.Groups["vidpid"].Value, out var hardwareId))
            {
                continue;
            }

            devices.Add(new RemoteUsbDevice(match.Groups["busid"].Value, match.Groups["desc"].Value, hardwareId));
        }

        return devices;
    }

    public async Task<RemoteUsbDevice?> FindRemoteDeviceAsync(string host, UsbHardwareId hardwareId, CancellationToken ct = default)
    {
        var devices = await ListRemoteAsync(host, ct).ConfigureAwait(false);
        return devices.FirstOrDefault(d => d.HardwareId == hardwareId);
    }

    public async Task<AttachedDevice> AttachAsync(string host, string busId, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(_executablePath, ["attach", "-r", host, "-b", busId], ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UsbipCommandException("usbip attach", result);
        }

        var port = ParseAttachedPort(result.StdOut);
        if (port is null)
        {
            throw new UsbipCommandException("usbip attach", result with { StdErr = "Could not parse attached port from output: " + result.StdOut });
        }

        return new AttachedDevice(port.Value, host, busId);
    }

    /// <summary>Parses the port number out of `usbip attach` success output (e.g. "successfully attached to port 1").</summary>
    public static int? ParseAttachedPort(string stdOut)
    {
        var match = AttachedPort().Match(stdOut);
        return match.Success && int.TryParse(match.Groups["port"].Value, out var port) ? port : null;
    }

    /// <summary>
    /// Lists locally-imported (attached) VHCI port numbers, for verifying a previously-successful
    /// attach is still genuinely alive rather than trusting the in-memory "attached" flag forever
    /// -- the local attachment can die silently (sleep/resume, a driver hiccup) without the Wheel
    /// PC's beacon ever indicating anything is wrong.
    /// </summary>
    public async Task<IReadOnlyList<int>> ListPortsAsync(CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(_executablePath, ["port"], ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UsbipCommandException("usbip port", result);
        }

        return ParsePortList(result.StdOut);
    }

    /// <summary>Parses `usbip port` output into the set of currently-imported port numbers. Exposed statically so parsing logic is unit-testable without spawning a real process.</summary>
    public static IReadOnlyList<int> ParsePortList(string stdOut) =>
        PortLine().Matches(stdOut)
            .Select(m => int.Parse(m.Groups["port"].Value))
            .ToList();

    public async Task<bool> DetachAsync(int port, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(_executablePath, ["detach", "-p", port.ToString()], ct).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<bool> DetachAllAsync(CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(_executablePath, ["detach", "-all"], ct).ConfigureAwait(false);
        return result.Succeeded;
    }
}

public sealed class UsbipCommandException(string command, ProcessResult result)
    : Exception($"'{command}' exited with code {result.ExitCode}: {result.StdErr}");
