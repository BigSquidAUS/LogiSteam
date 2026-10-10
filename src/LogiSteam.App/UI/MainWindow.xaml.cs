using System.ComponentModel;
using System.Windows;
using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Roles;

namespace LogiSteam.UI;

/// <summary>
/// The one LogiSteam window, shared by both roles. Closing it only hides it -- the app keeps
/// running in the tray so the wheel stays connected -- unless <see cref="AllowClose"/> is set.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    // The size the layout is designed at; the window is never larger than this.
    private const double DesignWidth = 460;
    private const double DesignHeight = 780;
    private const double DesignCaptionHeight = 44;
    private const double ScreenMargin = 16;
    private const double MinScale = 0.55;

    private string? _fittedScreen;

    /// <summary>
    /// How much to shrink the 460x780 design so the whole window fits in the given free screen
    /// area (in the same device-independent units). Never enlarges, and stops shrinking at a
    /// floor below which text would be unreadable -- past that the content scrolls instead.
    /// </summary>
    public static double ComputeScale(double availableWidth, double availableHeight) =>
        Math.Clamp(Math.Min(availableWidth / DesignWidth, availableHeight / DesignHeight), MinScale, 1.0);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FitToScreen(center: true);
        LocationChanged += (_, _) => FitToScreen(center: false);
        DpiChanged += (_, _) => { _fittedScreen = null; FitToScreen(center: false); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => { _fittedScreen = null; FitToScreen(center: true); });

    /// <summary>
    /// Sizes the window for the screen it is on, so on a low-resolution display (an old TV, a
    /// small laptop) it shrinks as a whole instead of running off the bottom of the screen.
    /// Re-run when the window lands on a different screen or the display settings change.
    /// </summary>
    private void FitToScreen(bool center)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var screenKey = $"{screen.DeviceName}|{screen.WorkingArea}";
        if (screenKey == _fittedScreen)
        {
            return;
        }

        _fittedScreen = screenKey;

        // Screen reports physical pixels; WPF sizes are in device-independent units.
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var areaLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var areaTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var areaWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
        var areaHeight = screen.WorkingArea.Height / dpi.DpiScaleY;

        var scale = ComputeScale(areaWidth - 2 * ScreenMargin, areaHeight - 2 * ScreenMargin);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
        Width = DesignWidth * scale;
        // At the minimum scale the design may still be taller than the screen; cap it and let it scroll.
        Height = Math.Min(DesignHeight * scale, areaHeight - 2 * ScreenMargin);

        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            // WindowChrome is frozen once applied, so swap in a resized copy for the drag area.
            var resized = (System.Windows.Shell.WindowChrome)chrome.CloneCurrentValue();
            resized.CaptionHeight = DesignCaptionHeight * scale;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, resized);
        }

        if (center)
        {
            Left = areaLeft + (areaWidth - Width) / 2;
            Top = areaTop + (areaHeight - Height) / 2;
        }
        else if (Top + Height > areaTop + areaHeight)
        {
            Top = Math.Max(areaTop, areaTop + areaHeight - Height - ScreenMargin);
        }
    }

    /// <summary>Set by the app when it is really exiting.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Raised when the user closes the window and it hides to the tray instead.</summary>
    public event Action? HiddenToTray;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }

        base.OnClosing(e);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWheelRoleClick(object sender, RoutedEventArgs e) => _viewModel.RequestRole(AppRole.Wheel);

    private void OnGameRoleClick(object sender, RoutedEventArgs e) => _viewModel.RequestRole(AppRole.Game);

    private void OnWarningAction(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DashboardWarning warning)
        {
            _viewModel.RequestWarningAction(warning.Id);
        }
    }

    /// <summary>Where the Donate button goes. LogiSteam is free; this is the only ask.</summary>
    public const string DonateUrl = "https://www.paypal.com/donate/?hosted_button_id=NZJ7CCVL7Y5GS";

    private void OnDonate(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DonateUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"LogiSteam couldn't open your browser. You can donate at:\n{DonateUrl}",
                "LogiSteam", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnAdvancedAction(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RoleAction action)
        {
            action.Invoke();
        }
    }
}
