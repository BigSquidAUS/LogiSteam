using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.GameSide;

/// <summary>
/// Composition root for the Game PC side of LogiSteam. No visible window: a tray icon shows
/// status, and the AttachManager handles discovery/attach/detach automatically.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly BeaconListener _listener = new();
    private readonly AttachManager _attachManager;
    private readonly RemoteDiagnosticsServer _diagnosticsServer;
    private readonly SynchronizationContext _uiContext;

    private TrayStatus _currentStatus = new("Starting...", TrayStatusLevel.Idle);

    public TrayApp()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayApp must be constructed on the UI thread.");

        _attachManager = new AttachManager(_listener, new UsbipCliWrapper());
        _diagnosticsServer = new RemoteDiagnosticsServer(DiagnosticsPort.GameSide, BuildDiagnosticsSnapshotJson);

        _statusMenuItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        var detachMenuItem = new ToolStripMenuItem("Detach now", null, async (_, _) => await _attachManager.DetachForShutdownAsync());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(detachMenuItem);
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconPalette.GetIcon(TrayStatusLevel.Idle),
            Text = "LogiSteam Game Side - starting",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _attachManager.StatusChanged += status => _uiContext.Post(_ => SetStatus(status), null);
        _attachManager.Start();
        _diagnosticsServer.Start();
    }

    private void SetStatus(TrayStatus status)
    {
        _currentStatus = status;
        _statusMenuItem.Text = status.Text;
        _notifyIcon.Text = Truncate($"LogiSteam: {status.Text}", 127);
        _notifyIcon.Icon = TrayIconPalette.GetIcon(status.Level);
    }

    private string BuildDiagnosticsSnapshotJson()
    {
        var snapshot = new GameSideDiagnosticsSnapshot
        {
            HostName = Environment.MachineName,
            StatusText = _currentStatus.Text,
            StatusLevel = _currentStatus.Level.ToString(),
            AttachedFromHost = _attachManager.AttachedHost,
            AttachedBusId = _attachManager.CurrentAttachment?.BusId,
            AttachedLocalPort = _attachManager.CurrentAttachment?.Port,
            DiscoveredWheelPcs = _listener.CurrentlyVisible.Select(DiscoveredWheelPcSummary.From).ToList(),
        };

        return snapshot.ToJson();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private async void ExitApp()
    {
        await _attachManager.DetachForShutdownAsync();
        _listener.Dispose();
        _diagnosticsServer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
