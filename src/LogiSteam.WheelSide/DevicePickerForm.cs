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
            devices = UsbDeviceEnumerator.EnumeratePresentDevices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not list USB devices: {ex.Message}", "LogiSteam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var device in devices)
        {
            var item = new ListViewItem(device.Description) { Tag = device };
            item.SubItems.Add(device.HardwareId.ToString());
            _list.Items.Add(item);

            if (currentSelection is not null && device.HardwareId == currentSelection)
            {
                item.Selected = true;
                item.EnsureVisible();
            }
        }
    }

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
