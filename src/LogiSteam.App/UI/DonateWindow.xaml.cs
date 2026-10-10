using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace LogiSteam.UI;

/// <summary>
/// The occasional "would you like to donate?" dialog (see <c>DonationPrompt</c> for how rarely it
/// appears). The Donate button in the main window's footer opens the same page directly.
/// </summary>
public partial class DonateWindow : Window
{
    /// <summary>Where donations go. LogiSteam is free; this is the only ask.</summary>
    public const string DonateUrl = "https://www.paypal.com/donate/?hosted_button_id=NZJ7CCVL7Y5GS";

    public DonateWindow()
    {
        InitializeComponent();
    }

    /// <summary>Opens the donation page in the user's browser.</summary>
    public static void OpenDonationPage(Window? owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DonateUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            var message = $"LogiSteam couldn't open your browser. You can donate at:\n{DonateUrl}";
            if (owner is null)
            {
                MessageBox.Show(message, "LogiSteam", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(owner, message, "LogiSteam", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void OnDonate(object sender, RoutedEventArgs e)
    {
        OpenDonationPage(this);
        DialogResult = true;
    }
}
