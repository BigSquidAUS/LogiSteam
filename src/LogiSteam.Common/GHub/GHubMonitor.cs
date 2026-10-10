using System.ComponentModel;
using System.Diagnostics;

namespace LogiSteam.Common.GHub;

/// <summary>
/// Tells whether Logitech G HUB is running on this PC. The wheel needs G HUB open on both the
/// Wheel PC and the Game PC, so each side checks locally and tells the other over the
/// beacon/ack messages. The answer is cached briefly: it's asked for on every beacon and every
/// UI refresh, and enumerating processes that often would be wasteful.
/// </summary>
public sealed class GHubMonitor(Func<IEnumerable<string>>? processNames = null, TimeSpan? cacheFor = null)
{
    public const string DownloadUrl = "https://www.logitechg.com/innovation/g-hub.html";

    private readonly Func<IEnumerable<string>> _processNames = processNames ?? RunningProcessNames;
    private readonly TimeSpan _cacheFor = cacheFor ?? TimeSpan.FromSeconds(3);
    private readonly object _lock = new();
    private DateTime _checkedAtUtc = DateTime.MinValue;
    private bool _isRunning;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedAtUtc >= _cacheFor)
                {
                    _isRunning = _processNames().Any(IsGHubProcessName);
                    _checkedAtUtc = DateTime.UtcNow;
                }

                return _isRunning;
            }
        }
    }

    /// <summary>
    /// G HUB counts as running when its window (lghub) or its background agent (lghub_agent, what
    /// stays alive in the tray after the window is closed and actually talks to devices) exists.
    /// lghub_updater is deliberately excluded: it's a Windows service that keeps running after
    /// G HUB itself has been quit.
    /// </summary>
    public static bool IsGHubProcessName(string processName) =>
        processName.Equals("lghub", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("lghub_agent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Full path of the installed G HUB app, or null when G HUB isn't installed.</summary>
    public static string? FindInstalledExecutable()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "LGHUB", "lghub.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Opens G HUB if it's installed, otherwise its download page. Returns false if neither could be opened.</summary>
    public static bool OpenOrDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo(FindInstalledExecutable() ?? DownloadUrl) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static IEnumerable<string> RunningProcessNames()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(p => p.ProcessName).ToList();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
