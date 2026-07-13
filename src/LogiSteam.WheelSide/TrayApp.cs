using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using LogiSteam.Common.Config;
using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.WheelSide;

/// <summary>
/// Composition root for the Wheel PC side of LogiSteam. Runs with no visible window: a tray
/// icon shows status, and everything else happens automatically once a device is selected --
/// register the AutoBind policy once, watch for the chosen device, and beacon its bus id on the
/// LAN whenever it's present and shared. The device itself is picked by the user (see
/// DevicePickerForm) rather than assumed from a hardcoded VID/PID, since that assumption has
/// been wrong twice already for this exact wheel.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _selectDeviceMenuItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly UsbipdCliWrapper _usbipd = new();
    private readonly AckListener _ackListener = new();
    private readonly SelectedDeviceStore _deviceStore = new();
    private readonly UsbDeviceInventoryLogger _inventoryLogger;
    private readonly RemoteDiagnosticsServer _diagnosticsServer;
    private readonly SynchronizationContext _uiContext;

    private WheelDeviceWatcher? _watcher;
    private BeaconBroadcaster? _broadcaster;
    private WheelSideDiagnosticsForm? _diagnosticsForm;
    private SelectedDevice? _selectedDevice;
    private UsbipdDevice? _currentDevice;
    private DiscoveredGamePc? _lastAck;
    private DateTime? _lastAckAtUtc;
    private TrayStatus _currentStatus = new("Starting...", TrayStatusLevel.Idle);
    private bool _policyEnsured;
    private bool _refreshInProgress;

    public TrayApp()
    {
        // Captured while still on the UI thread; used to safely marshal calls from the
        // background watcher/broadcaster/ack-listener threads back to the tray icon/menu.
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayApp must be constructed on the UI thread.");

        _inventoryLogger = new UsbDeviceInventoryLogger(id => _selectedDevice?.Matches(id) ?? false);
        _diagnosticsServer = new RemoteDiagnosticsServer(DiagnosticsPort.WheelSide, BuildDiagnosticsSnapshotJson);

        _statusMenuItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        _selectDeviceMenuItem = new ToolStripMenuItem("Select USB Device...", null, (_, _) => OpenDevicePicker());
        var openUsbLogMenuItem = new ToolStripMenuItem("Open USB Device Log", null, (_, _) => OpenUsbLog());
        var openDiagnosticsMenuItem = new ToolStripMenuItem("Open Diagnostics...", null, (_, _) => OpenDiagnostics());
        var exitMenuItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_selectDeviceMenuItem);
        menu.Items.Add(openUsbLogMenuItem);
        menu.Items.Add(openDiagnosticsMenuItem);
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconPalette.GetIcon(TrayStatusLevel.Idle),
            Text = "LogiSteam Wheel Side - starting",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _ackListener.GamePcSeenOrUpdated += pc => _uiContext.Post(_ => { _lastAck = pc; _lastAckAtUtc = DateTime.UtcNow; RefreshNow(); }, null);
        _ackListener.GamePcLost += _ => _uiContext.Post(_ => { _lastAck = null; _lastAckAtUtc = null; RefreshNow(); }, null);
        _ackListener.Start();

        _inventoryLogger.Start();
        _diagnosticsServer.Start();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _selectedDevice = _deviceStore.Load();
        if (_selectedDevice is not null)
        {
            StartWatchingSelectedDevice();
        }
        else
        {
            var profile = WheelAutoDetector.DetectFirstMatch(SafeEnumerateUsbDevices());
            if (profile is not null)
            {
                // Auto-detected a known wheel model -- select and persist it silently, exactly as
                // if the user had picked it manually, with zero dialogs shown.
                _selectedDevice = SelectedDevice.FromProfile(profile);
                _deviceStore.Save(_selectedDevice);
                StartWatchingSelectedDevice();
            }
            else
            {
                SetStatus(new TrayStatus("No device selected - pick your wheel now", TrayStatusLevel.Warning));
                OpenDevicePicker();
            }
        }

        _ = RefreshAsync();
    }

    private static IReadOnlyList<UsbDeviceInfo> SafeEnumerateUsbDevices()
    {
        try
        {
            return UsbDeviceEnumerator.EnumeratePresentDevices();
        }
        catch (ManagementException)
        {
            return [];
        }
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
            MessageBox.Show($"Could not open the log: {ex.Message}\n\nLog path: {_inventoryLogger.LogPath}",
                "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void OpenDevicePicker()
    {
        using var picker = new DevicePickerForm(_selectedDevice?.HardwareIds.FirstOrDefault());
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedDevice is null)
        {
            return;
        }

        _selectedDevice = SelectedDevice.From(picker.SelectedDevice);
        _deviceStore.Save(_selectedDevice);
        _policyEnsured = false;
        _currentDevice = null;
        StartWatchingSelectedDevice();
        RefreshNow();
    }

    private void StartWatchingSelectedDevice()
    {
        _watcher?.Dispose();
        _broadcaster?.Dispose();

        if (_selectedDevice is null)
        {
            _watcher = null;
            _broadcaster = null;
            return;
        }

        _watcher = new WheelDeviceWatcher(_selectedDevice.HardwareIds);
        _watcher.DeviceArrived += () => _uiContext.Post(_ => RefreshNow(), null);
        _watcher.DeviceRemoved += () => _uiContext.Post(_ => RefreshNow(), null);
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
        };
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            var reconciled = WheelAutoDetector.ReconcileSelection(_selectedDevice, SafeEnumerateUsbDevices());
            if (reconciled is not null && reconciled != _selectedDevice)
            {
                // Silent, no-dialog self-heal -- same UX as first-run auto-detect: either a stale
                // selection's ids stopped matching anything live and a known model was found to
                // replace it, or a legacy single-persona pick got upgraded to its full known set.
                _selectedDevice = reconciled;
                _deviceStore.Save(_selectedDevice);
                _policyEnsured = false;
                _currentDevice = null;
                StartWatchingSelectedDevice();
            }

            var selected = _selectedDevice;
            if (selected is null)
            {
                SetStatus(new TrayStatus("No device selected - right-click and choose your wheel", TrayStatusLevel.Warning));
                return;
            }

            if (!_policyEnsured)
            {
                _policyEnsured = await TryEnsurePolicyAsync(selected.HardwareIds);
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
            }
            catch (UsbipdCommandException ex)
            {
                _currentDevice = null;
                SetStatus(new TrayStatus($"usbipd-win error: {ex.Message}", TrayStatusLevel.Error));
                return;
            }
            catch (Win32Exception)
            {
                _currentDevice = null;
                SetStatus(new TrayStatus("usbipd-win not found - install it, then restart LogiSteam", TrayStatusLevel.Error));
                return;
            }

            if (_currentDevice is null)
            {
                SetStatus(new TrayStatus(
                    $"Waiting for {selected.DisplayName} to be plugged in... - not your wheel? Right-click > Select USB Device...",
                    TrayStatusLevel.Idle));
            }
            else if (!_policyEnsured)
            {
                SetStatus(new TrayStatus($"{selected.DisplayName} detected - AutoBind policy could not be registered (run as Administrator once)", TrayStatusLevel.Warning));
            }
            else
            {
                // usbipd-win's AutoBind policy only actually binds the device once a Game PC
                // requests to attach it -- IsShared can legitimately stay false right up until
                // that happens, so "ready to share" (not yet bound) is a real, expected state,
                // not an error. Only report "Sharing"/Connected once IsShared AND an ack saying
                // "attached" both confirm the whole path actually worked end to end.
                var baseText = _currentDevice.IsShared
                    ? $"Sharing {selected.DisplayName} (bus {_currentDevice.BusId})"
                    : $"{selected.DisplayName} ready (bus {_currentDevice.BusId}) - not yet bound";

                if (_lastAck is null)
                {
                    SetStatus(new TrayStatus($"{baseText} - no Game PC connected yet", TrayStatusLevel.Warning));
                }
                else
                {
                    var attachedNote = _lastAck.Ack.Attached ? "attached" : "visible, not attached yet";
                    var level = _currentDevice.IsShared && _lastAck.Ack.Attached ? TrayStatusLevel.Connected : TrayStatusLevel.Warning;
                    SetStatus(new TrayStatus($"{baseText} - connected to {_lastAck.Ack.HostName} ({attachedNote})", level));
                }
            }
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async Task<bool> TryEnsurePolicyAsync(IReadOnlyList<UsbHardwareId> hardwareIds)
    {
        try
        {
            return await _usbipd.EnsureAutoBindPoliciesAsync(hardwareIds);
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

    private void SetStatus(TrayStatus status)
    {
        _currentStatus = status;
        _statusMenuItem.Text = status.Text;
        _notifyIcon.Text = Truncate($"LogiSteam: {status.Text}", 127);
        _notifyIcon.Icon = TrayIconPalette.GetIcon(status.Level);
    }

    /// <summary>
    /// Built fresh on every remote diagnostics query (see RemoteDiagnosticsServer) -- includes a
    /// live re-enumeration of all USB devices at query time, not just the cached state from the
    /// last 2-second refresh tick, so a remote troubleshooter always sees current reality.
    /// </summary>
    internal string BuildDiagnosticsSnapshotJson()
    {
        var liveDevices = SafeEnumerateUsbDevices();

        var snapshot = new WheelSideDiagnosticsSnapshot
        {
            HostName = Environment.MachineName,
            SelectedDisplayName = _selectedDevice?.DisplayName,
            SelectedHardwareIds = _selectedDevice?.HardwareIds.Select(id => id.ToString()).ToList() ?? [],
            CurrentlyLiveHardwareId = _currentDevice?.HardwareId?.ToString(),
            StatusText = _currentStatus.Text,
            StatusLevel = _currentStatus.Level.ToString(),
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

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private void ExitApp()
    {
        _refreshTimer.Stop();
        _watcher?.Dispose();
        _broadcaster?.Dispose();
        _diagnosticsForm?.Dispose();
        _ackListener.Dispose();
        _inventoryLogger.Dispose();
        _diagnosticsServer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
