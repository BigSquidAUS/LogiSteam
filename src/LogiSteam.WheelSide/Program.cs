using System.Threading;
using LogiSteam.Common.Diagnostics;

namespace LogiSteam.WheelSide;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        CrashLogger.Install("LogiSteam.WheelSide");

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        // TrayApp's constructor runs before Application.Run does, so a WindowsFormsSynchronizationContext
        // is not yet guaranteed to be installed on this thread (it's normally installed lazily on
        // first Control handle creation or by Application.Run itself). Install it explicitly so
        // TrayApp can rely on SynchronizationContext.Current from its very first line.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        Application.Run(new TrayApp());
    }
}