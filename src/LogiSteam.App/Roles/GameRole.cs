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
/// <see cref="AttachManager"/> attach/detach the devices it offers automatically.
/// </summary>
internal sealed class GameRole : IRole
{
    private readonly GHubMonitor _gHub;
    private readonly Func<bool> _requireGHubOnWheelPc;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly BeaconListener _listener = new();
    private readonly AttachManager _attachManager;
    private readonly RemoteDiagnosticsServer _diagnosticsServer;
    private readonly JsonListStore<string> _releasedStore = DeviceStores.ReleasedDevices();

    // Read from AttachManager's thread, replaced (never mutated) from the UI thread.
    private volatile IReadOnlySet<string> _released;

    private GameSideDiagnosticsForm? _diagnosticsForm;
    private TrayStatus _currentStatus = new("Starting...", TrayStatusLevel.Idle);
    private bool _stopped;

    /// <param name="requireGHubOnWheelPc">The "require G HUB on the Wheel PC too" setting, read live.</param>
    public GameRole(GHubMonitor gHub, Func<bool> requireGHubOnWheelPc)
    {
        _gHub = gHub;
        _requireGHubOnWheelPc = requireGHubOnWheelPc;
        _released = _releasedStore.Load().ToHashSet(StringComparer.OrdinalIgnoreCase);
        _attachManager = new AttachManager(_listener, new UsbipCliWrapper(), gHub, hardwareId => _released.Contains(hardwareId));
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

    public RoleAction? PrimaryAction => null;

    public IReadOnlyList<RoleAction> AdvancedActions
    {
        get
        {
            var actions = new List<RoleAction>();
            var links = _attachManager.Links;
            var from = CurrentWheelPc()?.Beacon.HostName ?? "the Wheel PC";

            if (links.Any(l => l.State == OfferedDeviceState.Connected))
            {
                actions.Add(new("Reconnect", "Disconnect and connect again. Try this if a device stops responding.", "", Reconnect));
            }

            // Every offered device can be handed back from here -- the way out if something was
            // added on the Wheel PC by mistake and is now out of reach there.
            foreach (var link in links)
            {
                var hardwareId = link.Device.HardwareIdText;
                actions.Add(link.State == OfferedDeviceState.Released
                    ? new($"Use {link.Name} on this PC", $"It's currently left with {from}.", "", () => SetReleased(hardwareId, false))
                    : new($"Stop using {link.Name} on this PC", $"Hands it back to {from}, where it works normally again.", "", () => SetReleased(hardwareId, true)));
            }

            actions.Add(new("Diagnostics", "Connection details and a live input test.", "", OpenDiagnostics));
            return actions;
        }
    }

    public DashboardState BuildDashboard()
    {
        var wheelPc = CurrentWheelPc();
        var links = wheelPc is null ? [] : _attachManager.Links;
        var wheel = links.FirstOrDefault(l => l.Device.IsWheel);

        var inputs = new GameSideInputs(
            WheelPcName: wheelPc?.Beacon.HostName,
            WheelName: wheel?.Name ?? wheelPc?.Beacon.DeviceName,
            Attached: wheel?.State == OfferedDeviceState.Connected,
            Problem: wheel?.Error)
        {
            Devices = links.Select(l => new OfferedDeviceStatus(l.Name, l.Device.IsWheel, l.State, l.Error)).ToList(),
        };

        // G HUB only matters when a Logitech wheel is on offer, not for a lone HOTAS or Stream Deck.
        var wheelInvolved = links.Count == 0 || wheel is not null;
        return GameSideDashboard.Build(inputs, wheelInvolved
            ? new GHubStatus(
                RunningHere: _gHub.IsRunning,
                InstalledHere: GHubMonitor.FindInstalledExecutable() is not null,
                RunningOnPeer: wheelPc?.Beacon.GHubRunning,
                PeerName: wheelPc?.Beacon.HostName,
                RequiredOnPeer: _requireGHubOnWheelPc())
            : null);
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
    /// listener's socket in place on resume is enough: AttachManager re-checks its attachments
    /// against `usbip port` on every beacon, so simply getting beacons flowing again closes the
    /// loop without needing any additional resume-specific recovery logic here.
    /// </summary>
    public void OnResume() => _listener.Restart();

    // Detaching is enough: the next beacon (every couple of seconds) attaches everything again.
    private async void Reconnect() => await _attachManager.DetachForShutdownAsync();

    /// <summary>Switches an offered device off (or back on) for this PC. Takes effect on the next beacon.</summary>
    private void SetReleased(string hardwareId, bool released)
    {
        var updated = _released.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (released ? updated.Add(hardwareId) : updated.Remove(hardwareId))
        {
            _released = updated;
            _releasedStore.Save(updated);
            Changed?.Invoke();
        }
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

    internal string BuildDiagnosticsSnapshotJson()
    {
        var links = _attachManager.Links;
        var primary = links.FirstOrDefault(l => l.Device.IsWheel && l.State == OfferedDeviceState.Connected)
            ?? links.FirstOrDefault(l => l.State == OfferedDeviceState.Connected);

        var snapshot = new GameSideDiagnosticsSnapshot
        {
            HostName = Environment.MachineName,
            StatusText = _currentStatus.Text,
            StatusLevel = _currentStatus.Level.ToString(),
            AttachedFromHost = _attachManager.AttachedHost,
            AttachedBusId = primary?.Device.BusId,
            AttachedLocalPort = primary?.Port,
            Devices = links.Select(l => new OfferedDeviceSummary(
                l.Name, l.Device.HardwareIdText, l.Device.BusId, l.Device.IsWheel, l.State.ToString(), l.Port, l.Error)).ToList(),
            DiscoveredWheelPcs = _listener.CurrentlyVisible.Select(DiscoveredWheelPcSummary.From).ToList(),
        };

        return snapshot.ToJson();
    }
}
