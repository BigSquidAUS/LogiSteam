using System.Windows;
using LogiSteam.Common;
using LogiSteam.Common.Diagnostics;

namespace LogiSteam;

public partial class App : Application
{
    /// <summary>Passed by the "start with Windows" shortcut so LogiSteam starts quietly in the tray.</summary>
    private const string MinimizedArgument = "--minimized";

    private SingleInstance? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = SingleInstance.TryAcquire();
        if (_singleInstance is null)
        {
            // Already running: that copy has just been asked to show its window.
            Shutdown();
            return;
        }

        CrashLogger.Install("LogiSteam");
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLogger.Report("LogiSteam", args.Exception);
            args.Handled = true;
            Shutdown(1);
        };

        // The tray icon and the diagnostics window are Windows Forms, hosted by this WPF app.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();

        _controller = new AppController();
        _singleInstance.ListenForShowRequests(() => Dispatcher.BeginInvoke(_controller.ShowWindow));
        _controller.Start(startMinimized: e.Args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
