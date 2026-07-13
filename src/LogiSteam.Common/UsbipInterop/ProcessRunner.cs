using System.Diagnostics;

namespace LogiSteam.Common.UsbipInterop;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs an external CLI (usbipd.exe / usbip.exe) and captures its output.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    /// <summary>
    /// Runs a command elevated via a UAC prompt (ShellExecute "runas"). Output cannot be
    /// redirected in this mode, so only the exit code is available -- use this only for the rare
    /// one-time operations that need Administrator rights (e.g. registering usbipd-win's AutoBind
    /// policy), not for routine status queries.
    /// </summary>
    public static async Task<int?> RunElevatedAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: user declined the UAC prompt.
            return null;
        }
    }

    /// <summary>
    /// Runs several commands under a single elevation prompt (e.g. registering AutoBind policies
    /// for every persona of a multi-persona wheel profile) -- without this, a naive per-command
    /// loop over RunElevatedAsync would trigger one UAC prompt per command, turning a 3-persona
    /// profile's first run into three separate "Yes" clicks instead of one.
    /// </summary>
    public static async Task<int?> RunElevatedBatchAsync(
        string fileName, IEnumerable<IEnumerable<string>> argumentSets, CancellationToken ct = default)
    {
        var (batchFileName, batchArgs) = BuildElevatedBatchCommand(fileName, argumentSets);
        return await RunElevatedAsync(batchFileName, batchArgs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure: builds the `cmd.exe /c "cmd1 && cmd2 && ..."` invocation used by
    /// <see cref="RunElevatedBatchAsync"/>. Exposed separately (rather than inlined) so the
    /// quoting/joining logic is unit-testable without actually spawning a process or triggering UAC.
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments) BuildElevatedBatchCommand(
        string fileName, IEnumerable<IEnumerable<string>> argumentSets)
    {
        var commands = argumentSets
            .Select(args => string.Join(' ', new[] { Quote(fileName) }.Concat(args.Select(Quote))))
            .ToList();

        return ("cmd.exe", ["/c", string.Join(" && ", commands)]);
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
