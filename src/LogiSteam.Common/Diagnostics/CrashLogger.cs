namespace LogiSteam.Common.Diagnostics;

/// <summary>
/// LogiSteam's tray apps have no console and no visible window, so an unhandled exception during
/// startup previously just vanished -- the process would exit a few seconds later with no
/// feedback at all (Windows Error Reporting handles it out of view, and the tray icon never gets
/// created). This wires up a last-resort handler that logs the failure to disk and shows a
/// message box, so "nothing happens" becomes a readable error instead of silence.
/// </summary>
public static class CrashLogger
{
    public static void Install(string appName)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(appName, e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

        Application.ThreadException += (_, e) => Report(appName, e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(appName, e.Exception);
            e.SetObserved();
        };
    }

    public static void Report(string appName, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiSteam", "logs");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, $"{appName}.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n\n");

            MessageBox.Show(
                $"{appName} hit an unexpected error and needs to close.\n\n{ex.Message}\n\nDetails were written to:\n{logPath}",
                appName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Logging/reporting itself must never throw -- there is nothing further to fall back to.
        }
    }
}
