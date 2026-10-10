using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Windows.Threading;
using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.GHub;
using LogiSteam.Common.Updates;
using LogiSteam.Common.UsbipInterop;
using LogiSteam.UI;

namespace LogiSteam.Roles;

/// <summary>
/// The Wheel PC end of LogiSteam: identifies the racing wheel plugged into this PC with no
/// device list shown (see <see cref="WheelAutoDetector"/>), registers usbipd-win's AutoBind
/// policy for it once, and beacons its bus id on the LAN whenever it's present so a Game PC can
/// attach it. Choosing the device by hand exists only as an Advanced fallback.
/// </summary>
internal sealed class WheelRole : IRole
{
    private readonly GHubMonitor _gHub;
    private readonly Func<bool> _requireGHubHere;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly UsbipdCliWrapper _usbipd = new();
    private readonly AckListener _ackListener = new();
    private readonly SelectedDeviceStore _deviceStore = new();
    private readonly UsbDeviceInventoryLogger _inventoryLogger;
    private readonly RemoteDiagnosticsServer _diagnosticsServer;

    private WheelDeviceWatcher? _watcher;
    private BeaconBroadcaster? _broadcaster;
    private WheelSideDiagnosticsForm? _diagnosticsForm;
    private SelectedDevice? _selectedDevice;
    private UsbipdDevice? _currentDevice;
    private DiscoveredGamePc? _lastAck;
    private DateTime? _lastAckAtUtc;
    private string? _problem;
    private bool _policyEnsured;
    private bool _policyPromptShown;
    private bool _refreshInProgress;
    private bool _stopped;

    /// <param name="requireGHubOnWheelPc">The "require G HUB on the Wheel PC too" setting, read live.</param>
    public WheelRole(GHubMonitor gHub, Func<bool> requireGHubOnWheelPc)
    {
        _gHub = gHub;
        _requireGHubHere = requireGHubOnWheelPc;
        _inventoryLogger = new UsbDeviceInventoryLogger(id => _selectedDevice?.Matches(id) ?? false);
        _diagnosticsServer = new RemoteDiagnosticsServer(DiagnosticsPort.WheelSide, BuildDiagnosticsSnapshotJson);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshNow();
    }

    public AppRole Kind => AppRole.Wheel;

    public event Action? Changed;

    public void Start()
    {
        _ackListener.GamePcSeenOrUpdated += pc => _dispatcher.BeginInvoke(() => { _lastAck = pc; _lastAckAtUtc = DateTime.UtcNow; RefreshNow(); });
        _ackListener.GamePcLost += _ => _dispatcher.BeginInvoke(() => { _lastAck = null; _lastAckAtUtc = null; RefreshNow(); });
        _ackListener.Start();

        _inventoryLogger.Start();
        _diagnosticsServer.Start();

        _selectedDevice = _deviceStore.Load();
        if (_selectedDevice is not null)
        {
            StartWatchingSelectedDevice();
        }

        _refreshTimer.Start();
        RefreshNow();
    }

    public Task StopAsync()
    {
        _stopped = true;
        _refreshTimer.Stop();
        _watcher?.Dispose();
        _broadcaster?.Dispose();
        _diagnosticsForm?.Dispose();
        _ackListener.Dispose();
        _inventoryLogger.Dispose();
        _diagnosticsServer.Dispose();
        return Task.CompletedTask;
    }

    public IReadOnlyList<RoleAction> AdvancedActions
    {
        get
        {
            var actions = new List<RoleAction>
            {
                new("Choose the wheel manually", "Only needed if your wheel isn't found automatically.", "", ChooseWheelManually),
            };

            if (_selectedDevice?.Source == WheelSelectionSource.Manual)
            {
                actions.Add(new("Go back to automatic detection", $"Stop using the manual choice ({_selectedDevice.DisplayName}).", "", UseAutomaticDetection));
            }

            actions.Add(new("Diagnostics", "Connection details and a live wheel input test.", "", OpenDiagnostics));
            actions.Add(new("USB device log", "A record of USB devices arriving and leaving on this PC.", "", OpenUsbLog));
            return actions;
        }
    }

    public DashboardState BuildDashboard() => WheelSideDashboard.Build(
        new WheelSideInputs(
            WheelName: _selectedDevice?.DisplayName,
            WheelPresent: _currentDevice?.BusId is not null,
            Shared: _currentDevice?.IsShared ?? false,
            SharingAllowed: _policyEnsured,
            Problem: _problem,
            GamePcName: _lastAck?.Ack.HostName,
            GamePcAttached: _lastAck?.Ack.Attached ?? false),
        new GHubStatus(
            RunningHere: _gHub.IsRunning,
            InstalledHere: GHubMonitor.FindInstalledExecutable() is not null,
            RunningOnPeer: _lastAck?.Ack.GHubRunning,
            PeerName: _lastAck?.Ack.HostName,
            RequiredHere: _requireGHubHere()));

    public (string Name, string? Version)? Peer => _lastAck is { } ack ? (ack.Ack.HostName, ack.Ack.AppVersion) : null;

    public void OnWarningAction(string warningId)
    {
        if (warningId == WheelSideDashboard.SharePermissionWarningId)
        {
            _policyPromptShown = false;
            RefreshNow();
        }
    }

    /// <summary>
    /// A sleep/resume cycle can leave the beacon broadcaster's and ack listener's UdpClients
    /// silently dead -- Windows often power-cycles the NIC during suspend, even though this
    /// process survives untouched. Restarting them in place on resume is enough to get the whole
    /// discovery/ack loop flowing again with no further resume-specific recovery logic needed.
    /// </summary>
    public void OnResume()
    {
        _ackListener.Restart();
        _broadcaster?.Restart();
    }

    private static (IReadOnlyList<UsbDeviceInfo> Devices, IReadOnlyDictionary<UsbHardwareId, string> ProductNames) ScanUsbDevices()
    {
        IReadOnlyList<UsbDeviceInfo> devices;
        try
        {
            devices = UsbDeviceEnumerator.EnumeratePresentDevices();
        }
        catch (ManagementException)
        {
            devices = [];
        }

        // The USB product string is only needed to recognize a Logitech wheel the catalog
        // doesn't list, so it's only read when there is such a candidate device.
        var needsNames = devices.Any(d =>
            d.HardwareId.Vendor == UsbHardwareId.LogitechVendorId && KnownWheelProfiles.Find(d.HardwareId) is null);
        return (devices, needsNames ? HidProductNameProbe.GetProductNames(UsbHardwareId.LogitechVendorId) : new Dictionary<UsbHardwareId, string>());
    }

    private void OpenUsbLog()
    {
        try
        {
            var dir = Path.GetDirectoryName(_inventoryLogger.LogPath);
            if (!File.Exists(_inventoryLogger.LogPath) && !string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_inventoryLogger.LogPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show($"Could not open the log: {ex.Message}\n\nLog path: {_inventoryLogger.LogPath}",
                "LogiSteam", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsForm is { IsDisposed: false })
        {
            _diagnosticsForm.Activate();
            return;
        }

        _diagnosticsForm = new WheelSideDiagnosticsForm(BuildDiagnosticsSnapshotJson, () => _selectedDevice?.HardwareIds ?? []);
        _diagnosticsForm.Show();
    }

    private void ChooseWheelManually()
    {
        var picker = new ManualWheelWindow(_selectedDevice?.HardwareIds.FirstOrDefault()) { Owner = System.Windows.Application.Current?.MainWindow };
        if (picker.ShowDialog() != true || picker.ChosenDevice is null)
        {
            return;
        }

        ApplySelection(SelectedDevice.From(picker.ChosenDevice));
        RefreshNow();
    }

    private void UseAutomaticDetection()
    {
        ApplySelection(null);
        RefreshNow();
    }

    private void ApplySelection(SelectedDevice? selection)
    {
        _selectedDevice = selection;
        if (selection is null)
        {
            _deviceStore.Clear();
        }
        else
        {
            _deviceStore.Save(selection);
        }

        _policyEnsured = false;
        _policyPromptShown = false;
        _currentDevice = null;
        StartWatchingSelectedDevice();
    }

    private void StartWatchingSelectedDevice()
    {
        _watcher?.Dispose();
        _broadcaster?.Dispose();
        _watcher = null;
        _broadcaster = null;

        if (_selectedDevice is null)
        {
            return;
        }

        _watcher = new WheelDeviceWatcher(_selectedDevice.HardwareIds);
        _watcher.DeviceArrived += () => _dispatcher.BeginInvoke(RefreshNow);
        _watcher.DeviceRemoved += () => _dispatcher.BeginInvoke(RefreshNow);
        _watcher.Start();

        _broadcaster = new BeaconBroadcaster(BuildBeaconMessage);
        _broadcaster.Start();
    }

    private void RefreshNow() => _ = RefreshAsync();

    private BeaconMessage? BuildBeaconMessage()
    {
        var device = _currentDevice;
        if (device?.BusId is null || device.HardwareId is not { } liveHardwareId)
        {
            return null;
        }

        return new BeaconMessage
        {
            HostName = Environment.MachineName,
            BusId = device.BusId,
            // Derived from the device that's actually live right now (device.HardwareId, parsed
            // from usbipd state's InstanceId), not the originally-selected id -- a mode switch
            // can mean a different persona than the one first selected is the one that's live.
            VendorId = $"{liveHardwareId.Vendor:x4}",
            ProductId = $"{liveHardwareId.Product:x4}",
            Shared = device.IsShared,
            DeviceName = _selectedDevice?.DisplayName,
            GHubRunning = _gHub.IsRunning,
            AppVersion = AppVersion.Current,
        };
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress || _stopped)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            var (liveDevices, productNames) = await Task.Run(ScanUsbDevices);
            if (_stopped)
            {
                return;
            }

            // Silent, no-dialog identification: first detection, a mode switch, a swapped wheel,
            // or a leftover selection from an older version that isn't a wheel at all.
            var reconciled = WheelAutoDetector.ReconcileSelection(_selectedDevice, liveDevices, productNames);
            if (!ReferenceEquals(reconciled, _selectedDevice))
            {
                ApplySelection(reconciled);
            }

            var selected = _selectedDevice;
            if (selected is null)
            {
                _currentDevice = null;
                _problem = null;
                return;
            }

            // Deliberately NOT gated on our own WheelDeviceWatcher/WMI presence check here.
            // usbipd-win replaces a shared device's normal driver with a stub ("USBIP Shared
            // Device" in Device Manager) while it's bound -- which is itself a driver rebind the
            // physical device sees as a reset (hence the G29's autocentering calibration on
            // bind). That means the *original* VID:PID PnP entry our watcher looks for can
            // legitimately disappear from WMI while the device is being actively, successfully
            // shared. Gating on the watcher here meant LogiSteam would see a healthy share as
            // "unplugged," stop beaconing, cause the Game PC to detach (unbinding the device,
            // restoring the original entry), which the watcher would then see as "back," starting
            // an attach that binds it again -- LogiSteam driving its own bind/unbind feedback
            // loop against a wheel that was never actually disconnected.
            // `usbipd state` is usbipd-win's own authoritative view and already correctly tracks
            // the device through binding/stub-driver changes, so it alone is the presence signal.
            try
            {
                _currentDevice = await _usbipd.FindDeviceAsync(selected.HardwareIds);
                _problem = null;
            }
            catch (UsbipdCommandException ex)
            {
                _currentDevice = null;
                _problem = $"usbipd-win, the tool LogiSteam uses to share the wheel, reported an error: {ex.Message}";
                return;
            }
            catch (Win32Exception)
            {
                _currentDevice = null;
                _problem = "usbipd-win, the tool LogiSteam uses to share the wheel, isn't installed on this PC. Run the LogiSteam installer again to add it.";
                return;
            }

            // usbipd-win's AutoBind policy only actually binds the device once a Game PC
            // requests to attach it, so the policy is all that has to exist up front. Adding it
            // needs Administrator rights once: Windows is only allowed to prompt for that the
            // first time the wheel shows up and when the user clicks Allow -- never on a timer.
            if (!_policyEnsured && _currentDevice?.BusId is not null)
            {
                var mayPrompt = !_policyPromptShown;
                _policyPromptShown = true;
                _policyEnsured = await TryEnsurePolicyAsync(selected.HardwareIds, mayPrompt);
            }
        }
        finally
        {
            _refreshInProgress = false;
            if (!_stopped)
            {
                Changed?.Invoke();
            }
        }
    }

    private async Task<bool> TryEnsurePolicyAsync(IReadOnlyList<UsbHardwareId> hardwareIds, bool allowElevationPrompt)
    {
        try
        {
            return await _usbipd.EnsureAutoBindPoliciesAsync(hardwareIds, allowElevationPrompt);
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (UsbipdCommandException)
        {
            return false;
        }
    }

    /// <summary>
    /// Built fresh on every remote diagnostics query (see RemoteDiagnosticsServer) -- includes a
    /// live re-enumeration of all USB devices at query time, not just the cached state from the
    /// last 2-second refresh tick, so a remote troubleshooter always sees current reality.
    /// </summary>
    internal string BuildDiagnosticsSnapshotJson()
    {
        var liveDevices = ScanUsbDevices().Devices;
        var dashboard = BuildDashboard();

        var snapshot = new WheelSideDiagnosticsSnapshot
        {
            HostName = Environment.MachineName,
            SelectedDisplayName = _selectedDevice?.DisplayName,
            SelectedHardwareIds = _selectedDevice?.HardwareIds.Select(id => id.ToString()).ToList() ?? [],
            CurrentlyLiveHardwareId = _currentDevice?.HardwareId?.ToString(),
            StatusText = $"{dashboard.Headline} - {dashboard.Detail}",
            StatusLevel = dashboard.Level.ToString(),
            PolicyEnsured = _policyEnsured,
            CurrentBusId = _currentDevice?.BusId,
            CurrentlyShared = _currentDevice?.IsShared,
            LastAckFromHost = _lastAck?.Ack.HostName,
            LastAckAttached = _lastAck?.Ack.Attached,
            LastAckAgoSeconds = _lastAckAtUtc is { } at ? (DateTime.UtcNow - at).TotalSeconds : null,
            RecentUsbLogLines = ReadLogTail(_inventoryLogger.LogPath, 40),
            AllUsbDevicesNow = liveDevices.Select(UsbDeviceSummary.From).ToList(),
        };

        return snapshot.ToJson();
    }

    private static IReadOnlyList<string> ReadLogTail(string path, int maxLines)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).TakeLast(maxLines).ToList() : [];
        }
        catch (IOException)
        {
            return ["<log unavailable: file in use>"];
        }
    }
}
