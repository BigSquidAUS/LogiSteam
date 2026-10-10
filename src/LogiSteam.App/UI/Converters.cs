using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LogiSteam.Common.Tray;

namespace LogiSteam.UI;

/// <summary>Status level -> its colour (blue = working on it, green = connected, amber = needs attention, red = broken).</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            TrayStatusLevel.Connected => "Good",
            TrayStatusLevel.Warning => "Warn",
            TrayStatusLevel.Error => "Bad",
            _ => "Accent",
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Status level -> the glyph shown in the badge on the wheel emblem.</summary>
public sealed class LevelToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TrayStatusLevel.Connected => "",
        TrayStatusLevel.Warning => "",
        TrayStatusLevel.Error => "",
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Collapsed for null/empty/false/zero, Visible otherwise. Pass "invert" to flip it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i > 0,
            _ => true,
        };

        if (parameter as string == "invert")
        {
            has = !has;
        }

        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
