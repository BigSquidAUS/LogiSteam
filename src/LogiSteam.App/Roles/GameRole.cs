using System.Windows.Threading;
using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.GHub;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Roles;

/// <summary>
/// The Game PC end of LogiSteam: listens for a Wheel PC's beacon and lets
/// <see cref="AttachManager"/> attach/detach the shared wheel automatically.
/// </summary>
internal sealed class GameRole : IRole
{
    private readonly GHubMonitor _gHub;
    private readonly Func<bool> _requireGHubOnWheelPc;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly BeaconListener _listener = new();
    private readonly AttachManager _attachManager;
    private readonly RemoteDiagnosticsServer _diagnosticsServer;

    private GameSideDiagnosticsForm? _diagnosticsForm;
    private TrayStatus _currentStatus = new("Starting...", TrayStatusLevel.Idle);
    private bool _stopped;

    /// <param name="requireGHubOnWheelPc">The "require G HUB on the Wheel PC too" setting, read live.</param>
    public GameRole(GHubMonitor gHub, Func<bool> requireGHubOnWheelPc)
    {
        _gHub = gHub;
        _requireGHubOnWheelPc = requireGHubOnWheelPc;
        _attachManager = new AttachManager(_listener, new UsbipCliWrapper(), gHub);
        _diagnosticsServer = new RemoteDiagnosticsServer(DiagnosticsPort.GameSide, BuildDiagnosticsSnapshotJson);
    }

    public AppRole Kind => AppRole.Game;

    public event Action? Changed;

    public void Start()
    {
        _attachManager.StatusChanged += status => _dispatcher.BeginInvoke(() =>
        {
            _currentStatus = status;
            if (!_stopped)
            {
                Changed?.Invoke();
            }
        });
        _attachManager.Start();
        _diagnosticsServer.Start();
    }

    public async Task StopAsync()
    {
        _stopped = true;
        await _attachManager.DetachForShutdownAsync();
        _listener.Dispose();
        _diagnosticsForm?.Dispose();
        _diagnosticsServer.Dispose();
    }

    public IReadOnlyList<RoleAction> AdvancedActions
    {
        get
        {
            var actions = new List<RoleAction>();
            if (_attachManager.CurrentAttachment is not null)
            {
                actions.Add(new("Reconnect the wheel", "Disconnect it and connect again. Try this if the wheel stops responding.", "", Reconnect));
            }

            actions.Add(new("Diagnostics", "Connection details and a live wheel input test.", "", OpenDiagnostics));
            return actions;
        }
    }

    public DashboardState BuildDashboard()
    {
        var wheelPc = CurrentWheelPc();
        var attached = _attachManager.CurrentAttachment is not null;

        return GameSideDashboard.Build(
            new GameSideInputs(
                WheelPcName: wheelPc?.Beacon.HostName,
                WheelName: wheelPc?.Beacon.DeviceName,
                Attached: attached,
                Problem: !attached && _currentStatus.Level == TrayStatusLevel.Error ? _currentStatus.Text : null),
            new GHubStatus(
                RunningHere: _gHub.IsRunning,
                InstalledHere: GHubMonitor.FindInstalledExecutable() is not null,
                RunningOnPeer: wheelPc?.Beacon.GHubRunning,
                PeerName: wheelPc?.Beacon.HostName,
                RequiredOnPeer: _requireGHubOnWheelPc()));
    }

    public (string Name, string? Version)? Peer =>
        CurrentWheelPc() is { } pc ? (pc.Beacon.HostName, pc.Beacon.AppVersion) : null;

    // The Wheel PC we're attached to if any, otherwise whichever one is currently visible.
    private DiscoveredWheelPc? CurrentWheelPc()
    {
        var visible = _listener.CurrentlyVisible;
        return visible.FirstOrDefault(pc => pc.Address.ToString() == _attachManager.AttachedHost) ?? visible.FirstOrDefault();
    }

    public void OnWarningAction(string warningId)
    {
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
    public void OnResume() => _listener.Restart();

    // Detaching is enough: the next beacon (every couple of seconds) attaches it again.
    private async void Reconnect() => await _attachManager.DetachForShutdownAsync();

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
}
