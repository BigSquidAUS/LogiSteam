using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.UI;

/// <summary>
/// Lets the user choose one more USB device on the Wheel PC to share alongside the wheel.
/// Devices that would break this PC or the connection are never listed, and a keyboard, mouse,
/// or audio/video device needs a second, explicit confirmation (see <see cref="ShareAdvice"/>).
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly IReadOnlySet<UsbHardwareId> _alreadyShared;

    /// <param name="alreadyShared">The wheel and devices already added, which aren't offered again.</param>
    public AddDeviceWindow(IReadOnlySet<UsbHardwareId> alreadyShared)
    {
        _alreadyShared = alreadyShared;
        InitializeComponent();
        Populate();
    }

    public UsbDeviceDetails? ChosenDevice { get; private set; }

    private sealed record DeviceRow(UsbDeviceDetails Device)
    {
        public string Name => Device.DisplayName;

        public string HardwareIdText => $"USB ID {Device.HardwareId}";

        public string? Badge => Device.Advice switch
        {
            ShareAdvice.ConfirmInput => "Keyboard / mouse",
            ShareAdvice.CautionMedia => "Audio / video",
            _ => null,
        };

        public Visibility BadgeVisibility => Badge is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Populate()
    {
        IReadOnlyList<UsbDeviceDetails> devices;
        try
        {
            devices = UsbDeviceEnumerator.EnumerateWithDetails();
        }
        catch (ManagementException ex)
        {
            DeviceList.ItemsSource = null;
            EmptyText.Text = $"Couldn't list USB devices: {ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        var rows = UsbDeviceClassifier.Offerable(devices, _alreadyShared).Select(d => new DeviceRow(d)).ToList();
        DeviceList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = false;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Populate();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => AddButton.IsEnabled = DeviceList.SelectedItem is DeviceRow;

    private void OnDeviceDoubleClick(object sender, MouseButtonEventArgs e) => Choose();

    private void OnAdd(object sender, RoutedEventArgs e) => Choose();

    private void Choose()
    {
        if (DeviceList.SelectedItem is not DeviceRow row)
        {
            return;
        }

        if (row.Device.Advice == ShareAdvice.Ok)
        {
            Accept(row.Device);
            return;
        }

        // Flagged: spell out the consequence and make the user say yes to it.
        ConfirmTitle.Text = $"Add {row.Device.DisplayName}?";
        ConfirmText.Text = row.Device.Advice == ShareAdvice.ConfirmInput
            ? "This acts as a keyboard or mouse. While it's connected to your Game PC it will stop working on this PC. If it's the keyboard or mouse you use to control this PC, don't add it."
            : row.Device.AdviceText ?? string.Empty;
        ConfirmEscape.Text = row.Device.Advice == ShareAdvice.ConfirmInput
            ? "If you add it by mistake, you can hand it back from the Game PC: open LogiSteam there, choose Advanced, and stop using it on that PC."
            : "You can remove it again at any time under Advanced.";
        ConfirmPanel.Visibility = Visibility.Visible;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is DeviceRow row)
        {
            Accept(row.Device);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => ConfirmPanel.Visibility = Visibility.Collapsed;

    private void Accept(UsbDeviceDetails device)
    {
        ChosenDevice = device;
        DialogResult = true;
    }
}
