using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.WheelSide;

/// <summary>
/// Lets the user pick which currently-plugged-in USB device is "the wheel" by hand, instead of
/// LogiSteam guessing a single hardcoded VID/PID -- that guess has been wrong twice already (a
/// stale value from a lookup site, then a real per-unit/firmware difference), so putting a
/// person in the loop is more reliable than another hardcoded assumption.
/// </summary>
public sealed class DevicePickerForm : Form
{
    private readonly ListView _list;
    private readonly Button _okButton;

    public UsbDeviceInfo? SelectedDevice { get; private set; }

    public DevicePickerForm(UsbHardwareId? currentSelection)
    {
        Text = "LogiSteam - Select your wheel";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 360);
        ShowIcon = false;
        ShowInTaskbar = false;

        var hint = new Label
        {
            Text = "Pick the USB device for your wheel. If you don't see it, unplug/replug it and click Refresh.",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        _list.Columns.Add("Description", 340);
        _list.Columns.Add("Hardware ID", 160);

        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        refreshButton.Click += (_, _) => Populate(currentSelection);

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
        _okButton.Click += (_, _) => AcceptSelection();

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        _list.SelectedIndexChanged += (_, _) => _okButton.Enabled = _list.SelectedItems.Count > 0;
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count > 0) { AcceptSelection(); } };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(refreshButton);

        Controls.Add(_list);
        Controls.Add(buttonPanel);
        Controls.Add(hint);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        Populate(currentSelection);
    }

    private void Populate(UsbHardwareId? currentSelection)
    {
        _list.Items.Clear();
        _okButton.Enabled = false;

        IReadOnlyList<UsbDeviceInfo> devices;
        try
        {
            // Known catalog matches sort first (these are the ones auto-detect would have picked
            // silently if it recognized the current state -- e.g. a legacy/stale selection or a
            // persona that arrived after this window opened), then the existing likely-wheel
            // heuristic for anything else.
            devices = UsbDeviceEnumerator.EnumeratePresentDevices()
                .OrderBy(d => KnownWheelProfiles.TryGetDisplayName(d.HardwareId, out _) ? 0 : IsLikelyWheel(d) ? 1 : 2)
                .ThenBy(d => d.Description, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not list USB devices: {ex.Message}", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var device in devices)
        {
            // A device Windows can only describe with something confusing ("Logitech USB Input
            // Device") gets its known model name shown first, with the raw description kept
            // alongside so it can still be cross-checked against Device Manager if needed.
            var label = KnownWheelProfiles.TryGetDisplayName(device.HardwareId, out var displayName)
                ? $"{displayName} ({device.Description})"
                : device.Description;

            var item = new ListViewItem(label) { Tag = device };
            item.SubItems.Add(device.HardwareId.ToString());
            _list.Items.Add(item);

            if (currentSelection is not null && device.HardwareId == currentSelection)
            {
                item.Selected = true;
                item.EnsureVisible();
            }
        }
    }

    private static readonly string[] WheelKeywords = ["wheel", "racing", "driving force", "g29", "g hub"];

    /// <summary>
    /// Heuristic used only to sort the manual-fallback picker's list, never to auto-select --
    /// Logitech vendor id, or a description containing a wheel-ish keyword (which usbipd-win's
    /// driver-bound friendly names often do, e.g. "Logitech G HUB G29 Driving Force Racing Wheel USB").
    /// </summary>
    private static bool IsLikelyWheel(UsbDeviceInfo device) =>
        device.HardwareId.Vendor == UsbHardwareId.LogitechVendorId
        || WheelKeywords.Any(keyword => device.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private void AcceptSelection()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        SelectedDevice = (UsbDeviceInfo)_list.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }
}
