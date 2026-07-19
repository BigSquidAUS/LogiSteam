using Microsoft.Win32;
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

    private GameSideDiagnosticsForm? _diagnosticsForm;
    private TrayStatus _currentStatus = new("Starting...", TrayStatusLevel.Idle);

    public TrayApp()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayApp must be constructed on the UI thread.");

        _attachManager = new AttachManager(_listener, new UsbipCliWrapper());
        _diagnosticsServer = new RemoteDiagnosticsServer(DiagnosticsPort.GameSide, BuildDiagnosticsSnapshotJson);

        _statusMenuItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        var detachMenuItem = new ToolStripMenuItem("Detach now", null, async (_, _) => await _attachManager.DetachForShutdownAsync());
        var openDiagnosticsMenuItem = new ToolStripMenuItem("Open Diagnostics...", null, (_, _) => OpenDiagnostics());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(detachMenuItem);
        menu.Items.Add(openDiagnosticsMenuItem);
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

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>
    /// A sleep/resume cycle can leave the beacon listener's UdpClient silently dead -- Windows
    /// often power-cycles the NIC during suspend, even though this process (and everything
    /// subscribed to the listener, like AttachManager) survives untouched. Restarting the
    /// listener's socket in place on resume is enough: AttachManager's existing attach-liveness
    /// check (see AttachManager.IsAttachmentAliveAsync) already self-heals a dead local
    /// attachment on the next beacon it receives, so simply getting beacons flowing again closes
    /// the loop without needing any additional resume-specific recovery logic here.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _uiContext.Post(_ => _listener.Restart(), null);
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsForm is { IsDisposed: false })
        {
            _diagnosticsForm.Activate();
            return;
        }

        _diagnosticsForm = new GameSideDiagnosticsForm(BuildDiagnosticsSnapshotJson, () => _listener.CurrentlyVisible.FirstOrDefault()?.Address.ToString());
        _diagnosticsForm.Show();
    }

    private void SetStatus(TrayStatus status)
    {
        _currentStatus = status;
        _statusMenuItem.Text = status.Text;
        _notifyIcon.Text = Truncate($"LogiSteam: {status.Text}", 127);
        _notifyIcon.Icon = TrayIconPalette.GetIcon(status.Level);
    }

    internal string BuildDiagnosticsSnapshotJson()
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
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        await _attachManager.DetachForShutdownAsync();
        _listener.Dispose();
        _diagnosticsForm?.Dispose();
        _diagnosticsServer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
