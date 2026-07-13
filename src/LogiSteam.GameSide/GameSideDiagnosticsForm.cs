using System.Management;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.GameSide;

/// <summary>
/// Game PC counterpart to WheelSideDiagnosticsForm. Same two-section shape (Connectivity, Input
/// Test), but Connectivity also lets a user fetch the Wheel PC's own live snapshot in-app (the
/// built-in replacement for running tools/query-diagnostics.ps1 by hand), and Input Test is
/// sourced from whatever USB devices are currently present on this machine rather than a fixed
/// hardware-id list, since GameSide tracks the attachment by bus id/port, not hardware id.
/// </summary>
public sealed class GameSideDiagnosticsForm : Form
{
    private readonly Func<string> _buildSnapshotJson;
    private readonly Func<string?> _getDefaultWheelPcAddress;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly HidInputTester _hidTester = new();
    private readonly SynchronizationContext _uiContext;

    private readonly TextBox _connectivityBox;
    private readonly TextBox _wheelPcAddressBox;
    private readonly Button _queryWheelPcButton;
    private readonly ComboBox _deviceDropdown;
    private readonly Button _refreshDevicesButton;
    private readonly Button _startTestButton;
    private readonly Button _stopTestButton;
    private readonly TextBox _inputLogBox;

    private const int MaxInputLogLines = 500;

    public GameSideDiagnosticsForm(Func<string> buildSnapshotJson, Func<string?> getDefaultWheelPcAddress)
    {
        _buildSnapshotJson = buildSnapshotJson;
        _getDefaultWheelPcAddress = getDefaultWheelPcAddress;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("GameSideDiagnosticsForm must be constructed on the UI thread.");

        Text = "LogiSteam - Diagnostics (Game Side)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 700);
        MinimumSize = new Size(520, 460);
        ShowIcon = false;

        var connectivityLabel = new Label { Text = "Connectivity", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };

        _wheelPcAddressBox = new TextBox { Text = getDefaultWheelPcAddress() ?? string.Empty, Width = 140 };
        _queryWheelPcButton = new Button { Text = "Query Wheel PC", AutoSize = true };
        _queryWheelPcButton.Click += async (_, _) => await QueryWheelPcAsync();

        var wheelPcPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 32,
            Padding = new Padding(8, 0, 0, 4),
            WrapContents = false,
        };
        wheelPcPanel.Controls.Add(new Label { Text = "Wheel PC IP:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) });
        wheelPcPanel.Controls.Add(_wheelPcAddressBox);
        wheelPcPanel.Controls.Add(_queryWheelPcButton);

        _connectivityBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
        };

        var refreshNowButton = new Button { Text = "Refresh Now", AutoSize = true };
        refreshNowButton.Click += (_, _) => RefreshConnectivity();

        var copyButton = new Button { Text = "Copy to Clipboard", AutoSize = true };
        copyButton.Click += (_, _) => Clipboard.SetText(_connectivityBox.Text);

        var connectivityButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 36,
            Padding = new Padding(4),
        };
        connectivityButtonPanel.Controls.Add(copyButton);
        connectivityButtonPanel.Controls.Add(refreshNowButton);

        var connectivityPanel = new Panel { Dock = DockStyle.Top, Height = 340 };
        connectivityPanel.Controls.Add(_connectivityBox);
        connectivityPanel.Controls.Add(connectivityButtonPanel);
        connectivityPanel.Controls.Add(wheelPcPanel);
        connectivityPanel.Controls.Add(connectivityLabel);

        var inputLabel = new Label { Text = "Input Test", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };
        var inputHint = new Label
        {
            Text = "Pick the attached device, then move the wheel / press pedals and buttons.",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 0, 0, 4),
        };

        _deviceDropdown = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
        _refreshDevicesButton = new Button { Text = "Refresh Devices", AutoSize = true };
        _refreshDevicesButton.Click += (_, _) => PopulateDeviceDropdown();

        var devicePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 32,
            Padding = new Padding(8, 0, 0, 4),
            WrapContents = false,
        };
        devicePanel.Controls.Add(_deviceDropdown);
        devicePanel.Controls.Add(_refreshDevicesButton);

        _startTestButton = new Button { Text = "Start Test", AutoSize = true };
        _startTestButton.Click += (_, _) => StartInputTest();

        _stopTestButton = new Button { Text = "Stop Test", AutoSize = true, Enabled = false };
        _stopTestButton.Click += (_, _) => StopInputTest();

        var inputButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 36,
            Padding = new Padding(4),
        };
        inputButtonPanel.Controls.Add(_startTestButton);
        inputButtonPanel.Controls.Add(_stopTestButton);

        _inputLogBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
        };

        var inputPanel = new Panel { Dock = DockStyle.Fill };
        inputPanel.Controls.Add(_inputLogBox);
        inputPanel.Controls.Add(inputButtonPanel);
        inputPanel.Controls.Add(devicePanel);
        inputPanel.Controls.Add(inputHint);
        inputPanel.Controls.Add(inputLabel);

        Controls.Add(inputPanel);
        Controls.Add(connectivityPanel);

        _hidTester.ReportReceived += OnReportReceived;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshConnectivity();
        _refreshTimer.Start();

        RefreshConnectivity();
        PopulateDeviceDropdown();
    }

    private void RefreshConnectivity()
    {
        try
        {
            _connectivityBox.Text = _buildSnapshotJson();
        }
        catch (Exception ex)
        {
            _connectivityBox.Text = $"<failed to build snapshot: {ex.Message}>";
        }
    }

    private async Task QueryWheelPcAsync()
    {
        var address = _wheelPcAddressBox.Text.Trim();
        if (address.Length == 0)
        {
            MessageBox.Show(this, "Enter the Wheel PC's IP address first.", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _queryWheelPcButton.Enabled = false;
        try
        {
            var snapshot = await RemoteDiagnosticsClient.FetchAsync(address, DiagnosticsPort.WheelSide);
            _connectivityBox.Text = $"--- Wheel PC ({address}) ---{Environment.NewLine}{snapshot}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not query the Wheel PC: {ex.Message}", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _queryWheelPcButton.Enabled = true;
        }
    }

    private void PopulateDeviceDropdown()
    {
        _deviceDropdown.Items.Clear();

        IReadOnlyList<UsbDeviceInfo> devices;
        try
        {
            devices = UsbDeviceEnumerator.EnumeratePresentDevices();
        }
        catch (ManagementException ex)
        {
            MessageBox.Show(this, $"Could not list USB devices: {ex.Message}", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var device in devices)
        {
            _deviceDropdown.Items.Add(new DeviceDropdownItem(device));
        }

        if (_deviceDropdown.Items.Count == 0)
        {
            return;
        }

        var defaultIndex = Enumerable.Range(0, _deviceDropdown.Items.Count)
            .FirstOrDefault(i => ((DeviceDropdownItem)_deviceDropdown.Items[i]!).Device.HardwareId.Vendor == UsbHardwareId.LogitechVendorId, -1);

        _deviceDropdown.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
    }

    private void StartInputTest()
    {
        if (_deviceDropdown.SelectedItem is not DeviceDropdownItem selected)
        {
            MessageBox.Show(this, "No device selected - refresh the device list first.", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _inputLogBox.Clear();
        _hidTester.Start([selected.Device.HardwareId]);
        _startTestButton.Enabled = false;
        _stopTestButton.Enabled = true;
        _deviceDropdown.Enabled = false;
        _refreshDevicesButton.Enabled = false;
    }

    private void StopInputTest()
    {
        _hidTester.Stop();
        _startTestButton.Enabled = true;
        _stopTestButton.Enabled = false;
        _deviceDropdown.Enabled = true;
        _refreshDevicesButton.Enabled = true;
    }

    private void OnReportReceived(string devicePath, byte[] report, DateTime timestamp)
    {
        _uiContext.Post(_ =>
        {
            AppendInputLogLine($"{timestamp:HH:mm:ss.fff}  {Convert.ToHexString(report)}");
        }, null);
    }

    private void AppendInputLogLine(string line)
    {
        _inputLogBox.AppendText(line + Environment.NewLine);

        if (_inputLogBox.Lines.Length > MaxInputLogLines)
        {
            _inputLogBox.Lines = _inputLogBox.Lines[^MaxInputLogLines..];
            _inputLogBox.SelectionStart = _inputLogBox.Text.Length;
            _inputLogBox.ScrollToCaret();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _hidTester.Dispose();
        base.OnFormClosed(e);
    }

    private sealed record DeviceDropdownItem(UsbDeviceInfo Device)
    {
        public override string ToString() => $"{Device.Description} ({Device.HardwareId})";
    }
}
