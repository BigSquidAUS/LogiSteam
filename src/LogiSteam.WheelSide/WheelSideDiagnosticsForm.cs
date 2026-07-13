using LogiSteam.Common.Diagnostics;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.WheelSide;

/// <summary>
/// Built-in replacement for the ad-hoc scratch scripts used throughout this project's
/// troubleshooting (raw HID report capture, `tools/query-diagnostics.ps1`) -- a permanent,
/// non-modal window reachable from the tray menu so a user (or a remote helper walking them
/// through it) can see connectivity state and confirm raw wheel input is flowing without needing
/// a terminal.
/// </summary>
public sealed class WheelSideDiagnosticsForm : Form
{
    private readonly Func<string> _buildSnapshotJson;
    private readonly Func<IReadOnlyList<UsbHardwareId>> _getSelectedHardwareIds;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly HidInputTester _hidTester = new();
    private readonly SynchronizationContext _uiContext;

    private readonly TextBox _connectivityBox;
    private readonly Button _startTestButton;
    private readonly Button _stopTestButton;
    private readonly TextBox _inputLogBox;

    private const int MaxInputLogLines = 500;

    public WheelSideDiagnosticsForm(Func<string> buildSnapshotJson, Func<IReadOnlyList<UsbHardwareId>> getSelectedHardwareIds)
    {
        _buildSnapshotJson = buildSnapshotJson;
        _getSelectedHardwareIds = getSelectedHardwareIds;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("WheelSideDiagnosticsForm must be constructed on the UI thread.");

        Text = "LogiSteam - Diagnostics (Wheel Side)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 640);
        MinimumSize = new Size(500, 420);
        ShowIcon = false;

        var connectivityLabel = new Label { Text = "Connectivity", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };
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

        var connectivityPanel = new Panel { Dock = DockStyle.Top, Height = 300 };
        connectivityPanel.Controls.Add(_connectivityBox);
        connectivityPanel.Controls.Add(connectivityButtonPanel);
        connectivityPanel.Controls.Add(connectivityLabel);

        var inputLabel = new Label { Text = "Input Test", Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };
        var inputHint = new Label
        {
            Text = "Move the wheel / press pedals and buttons - new reports should appear below.",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 0, 0, 4),
        };

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
        inputPanel.Controls.Add(inputHint);
        inputPanel.Controls.Add(inputLabel);

        Controls.Add(inputPanel);
        Controls.Add(connectivityPanel);

        _hidTester.ReportReceived += OnReportReceived;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshConnectivity();
        _refreshTimer.Start();

        RefreshConnectivity();
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

    private void StartInputTest()
    {
        var hardwareIds = _getSelectedHardwareIds();
        if (hardwareIds.Count == 0)
        {
            MessageBox.Show(this, "No device is selected yet - pick your wheel first.", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _inputLogBox.Clear();
        _hidTester.Start(hardwareIds);
        _startTestButton.Enabled = false;
        _stopTestButton.Enabled = true;
    }

    private void StopInputTest()
    {
        _hidTester.Stop();
        _startTestButton.Enabled = true;
        _stopTestButton.Enabled = false;
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
}
