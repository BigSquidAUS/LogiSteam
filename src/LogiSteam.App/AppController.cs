using System.Windows;
using System.Windows.Threading;
using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.GHub;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;
using LogiSteam.Roles;
using LogiSteam.UI;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace LogiSteam;

/// <summary>
/// The app shell: owns the main window, the tray icon and whichever role (Wheel PC or Game PC)
/// this PC is currently playing, and switches between the two when the user flips the switch.
/// </summary>
internal sealed class AppController
{
    private readonly AppSettingsStore _settingsStore = new();
    private readonly GHubMonitor _gHub = new();
    private readonly MainViewModel _viewModel = new();
    private readonly MainWindow _window;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _statusMenuItem;
    private readonly DispatcherTimer _refreshTimer;

    private IRole? _role;
    private AppSettings _settings = new();
    private HashSet<string> _announcedWarnings = [];
    private bool _hideHintShown;
    private bool _exiting;

    public AppController()
    {
        _window = new MainWindow(_viewModel);
        _window.HiddenToTray += OnHiddenToTray;
        _viewModel.RoleRequested += role => _ = SwitchRoleAsync(role);
        _viewModel.WarningActionRequested += OnWarningAction;

        var openMenuItem = new WinForms.ToolStripMenuItem("Open LogiSteam", null, (_, _) => ShowWindow());
        openMenuItem.Font = new System.Drawing.Font(openMenuItem.Font, System.Drawing.FontStyle.Bold);
        _statusMenuItem = new WinForms.ToolStripMenuItem("Starting...") { Enabled = false };
        var exitMenuItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => _ = ExitAsync());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(openMenuItem);
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = TrayIconPalette.GetIcon(TrayStatusLevel.Idle),
            Text = "LogiSteam",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };
        _notifyIcon.BalloonTipClicked += (_, _) => ShowWindow();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RefreshDashboard();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start(bool startMinimized)
    {
        // A PC that ran the old, separate Wheel Side app has a saved wheel but no role setting.
        _settings = _settingsStore.Load();
        var role = _settings.Role
            ?? (new SelectedDeviceStore().Load() is not null ? AppRole.Wheel : null);

        if (role is { } known)
        {
            StartRole(known);
        }
        else
        {
            _viewModel.IsChoosingRole = true;
            _ = SuggestRoleAsync();
        }

        _refreshTimer.Start();

        // First run always shows the window: there's a question to answer.
        if (!startMinimized || role is null)
        {
            ShowWindow();
        }
    }

    public void ShowWindow()
    {
        if (_exiting)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>First run only: if a racing wheel is plugged into this PC, say so on the Wheel PC choice.</summary>
    private async Task SuggestRoleAsync()
    {
        var detected = await Task.Run(() =>
        {
            try
            {
                var devices = UsbDeviceEnumerator.EnumeratePresentDevices();
                return WheelAutoDetector.Detect(devices, HidProductNameProbe.GetProductNames(UsbHardwareId.LogitechVendorId));
            }
            catch (System.Management.ManagementException)
            {
                return null;
            }
        });

        if (detected is not null)
        {
            _viewModel.DetectedWheelHint = $"{detected.DisplayName} detected on this PC";
        }
    }

    private void StartRole(AppRole kind)
    {
        _role = kind == AppRole.Wheel
            ? new WheelRole(_gHub, () => _settings.RequireGHubOnWheelPc)
            : new GameRole(_gHub, () => _settings.RequireGHubOnWheelPc);
        _role.Changed += RefreshDashboard;
        _viewModel.Role = kind;
        _viewModel.IsChoosingRole = false;
        _announcedWarnings = [];
        _role.Start();
        RefreshDashboard();
    }

    private async Task SwitchRoleAsync(AppRole kind)
    {
        if (_viewModel.IsSwitching || _role?.Kind == kind)
        {
            return;
        }

        _viewModel.IsSwitching = true;
        try
        {
            if (_role is { } previous)
            {
                _role = null;
                previous.Changed -= RefreshDashboard;
                _viewModel.Role = kind;
                _viewModel.Apply(
                    new DashboardState("Switching...", "One moment.", TrayStatusLevel.Idle, [], []), []);
                await previous.StopAsync();
            }

            SaveSettings(_settings with { Role = kind });
            StartRole(kind);
        }
        finally
        {
            _viewModel.IsSwitching = false;
        }
    }

    private void RefreshDashboard()
    {
        if (_role is null || _exiting)
        {
            return;
        }

        var state = _role.BuildDashboard();
        _viewModel.Apply(state, [.. _role.AdvancedActions, GHubRequirementAction()]);

        _statusMenuItem.Text = state.Headline;
        _notifyIcon.Text = Truncate($"LogiSteam - {state.Headline}", 127);
        _notifyIcon.Icon = TrayIconPalette.GetIcon(state.Level);

        AnnounceNewWarnings(state.Warnings);
    }

    /// <summary>
    /// A warning that appears while the window is tucked away in the tray would otherwise go
    /// unseen, so each new one gets a notification -- once, not on every refresh.
    /// </summary>
    private void AnnounceNewWarnings(IReadOnlyList<DashboardWarning> warnings)
    {
        var current = warnings.Select(w => w.Id).ToHashSet();
        var fresh = warnings.FirstOrDefault(w => !_announcedWarnings.Contains(w.Id));
        _announcedWarnings = current;

        if (fresh is not null && !_window.IsVisible)
        {
            _notifyIcon.ShowBalloonTip(8000, fresh.Title, fresh.Detail, WinForms.ToolTipIcon.Warning);
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
    }

    /// <summary>
    /// G HUB is only required on the Game PC by default. This Advanced switch is the fallback for
    /// a setup where the wheel turns out to need it on the Wheel PC as well. Each PC decides for
    /// itself what to warn about, so it should be turned on on both.
    /// </summary>
    private RoleAction GHubRequirementAction()
    {
        var on = _settings.RequireGHubOnWheelPc;
        return new RoleAction(
            on ? "G HUB on the Wheel PC: required" : "G HUB on the Wheel PC: not required",
            on
                ? "Both PCs warn when G HUB isn't open on the Wheel PC. Click to only require it on the Game PC."
                : "G HUB is only checked on the Game PC. Click to also require it on the Wheel PC, if the wheel misbehaves without it.",
            "\uE7BA",
            () =>
            {
                SaveSettings(_settings with { RequireGHubOnWheelPc = !_settings.RequireGHubOnWheelPc });
                RefreshDashboard();
            });
    }

    private void OnWarningAction(string warningId)
    {
        if (warningId == GHubWarnings.LocalId)
        {
            if (!GHubMonitor.OpenOrDownload())
            {
                MessageBox.Show(_window, $"LogiSteam couldn't open Logitech G HUB. You can get it from:\n{GHubMonitor.DownloadUrl}",
                    "LogiSteam", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        _role?.OnWarningAction(warningId);
    }

    private void OnHiddenToTray()
    {
        if (_hideHintShown || _exiting)
        {
            return;
        }

        _hideHintShown = true;
        _notifyIcon.ShowBalloonTip(5000, "LogiSteam is still running",
            "It stays in the tray so your wheel stays connected. Right-click the icon to exit.", WinForms.ToolTipIcon.Info);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _window.Dispatcher.BeginInvoke(() => _role?.OnResume());
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _refreshTimer.Stop();

        if (_role is { } role)
        {
            _role = null;
            await role.StopAsync();
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _window.AllowClose = true;
        _window.Close();
        Application.Current.Shutdown();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
