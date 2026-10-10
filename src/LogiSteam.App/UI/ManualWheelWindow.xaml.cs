using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LogiSteam.Common.DeviceDetection;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.UI;

/// <summary>
/// The Advanced fallback for a wheel automatic detection doesn't recognize: lists the USB
/// devices on this PC so one can be chosen by hand. Never shown unless the user asks for it.
/// </summary>
public partial class ManualWheelWindow : Window
{
    private readonly UsbHardwareId? _currentSelection;

    public ManualWheelWindow(UsbHardwareId? currentSelection)
    {
        _currentSelection = currentSelection;
        InitializeComponent();
        Populate();
    }

    public UsbDeviceInfo? ChosenDevice { get; private set; }

    private sealed record DeviceRow(UsbDeviceInfo Device, bool LooksLikeWheel)
    {
        // A device Windows can only describe with something generic ("USB Input Device") gets its
        // known model name shown first, with the raw description kept alongside for cross-checking.
        public string Name => KnownWheelProfiles.TryGetDisplayName(Device.HardwareId, out var known)
            ? $"{known} ({Device.Description})"
            : Device.Description;

        public string HardwareIdText => $"USB ID {Device.HardwareId}";

        public Visibility BadgeVisibility => LooksLikeWheel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Populate()
    {
        IReadOnlyList<UsbDeviceInfo> devices;
        try
        {
            devices = UsbDeviceEnumerator.EnumeratePresentDevices();
        }
        catch (ManagementException ex)
        {
            DeviceList.ItemsSource = null;
            EmptyText.Text = $"Couldn't list USB devices: {ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        // Likely wheels first, then Logitech devices, then everything else.
        var rows = devices
            .Select(d => new DeviceRow(d, KnownWheelProfiles.Find(d.HardwareId) is not null || WheelAutoDetector.LooksLikeWheelName(d.Description)))
            .OrderBy(r => r.LooksLikeWheel ? 0 : r.Device.HardwareId.Vendor == UsbHardwareId.LogitechVendorId ? 1 : 2)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DeviceList.ItemsSource = rows;
        DeviceList.SelectedItem = rows.FirstOrDefault(r => r.Device.HardwareId == _currentSelection);
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Populate();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UseButton.IsEnabled = DeviceList.SelectedItem is DeviceRow;

    private void OnDeviceDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void OnUse(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (DeviceList.SelectedItem is DeviceRow row)
        {
            ChosenDevice = row.Device;
            DialogResult = true;
        }
    }
}
