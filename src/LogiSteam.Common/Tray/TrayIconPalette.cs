using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LogiSteam.Common.Tray;

/// <summary>
/// Draws LogiSteam's tray icon -- a simple steering-wheel glyph, colored per
/// <see cref="TrayStatusLevel"/> -- instead of borrowing generic system stock icons. Icons are
/// generated once and cached: NotifyIcon does not take ownership of the GDI icon handle, so
/// regenerating one on every status tick would leak a handle each time.
/// </summary>
public static class TrayIconPalette
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private const int CanvasSize = 32;

    private static readonly Dictionary<TrayStatusLevel, Icon> Cache = [];
    private static readonly object CacheLock = new();

    public static Icon GetIcon(TrayStatusLevel level)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(level, out var cached))
            {
                return cached;
            }

            var icon = Draw(level);
            Cache[level] = icon;
            return icon;
        }
    }

    private static Icon Draw(TrayStatusLevel level)
    {
        var color = level switch
        {
            TrayStatusLevel.Error => Color.FromArgb(220, 68, 60),
            TrayStatusLevel.Warning => Color.FromArgb(242, 169, 59),
            TrayStatusLevel.Connected => Color.FromArgb(59, 178, 115),
            _ => Color.FromArgb(90, 140, 214),
        };

        using var bitmap = new Bitmap(CanvasSize, CanvasSize);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawSteeringWheel(g, color);
        }

        // Bitmap.GetHicon() creates a native GDI icon handle that Icon.FromHandle() does not own;
        // it must be released explicitly once the managed Icon copy is made, or it leaks for the
        // life of the process (these are cached and only created once per status level, so this
        // runs at most 4 times total, but the handle would still outlive its usefulness otherwise).
        var hIcon = bitmap.GetHicon();
        try
        {
            using var handleIcon = Icon.FromHandle(hIcon);
            return (Icon)handleIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void DrawSteeringWheel(Graphics g, Color color)
    {
        const float margin = 3f;
        const float rimThickness = 3.2f;
        var rimRect = new RectangleF(margin, margin, CanvasSize - 2 * margin, CanvasSize - 2 * margin);
        var center = new PointF(CanvasSize / 2f, CanvasSize / 2f);
        var rimRadius = rimRect.Width / 2f;

        using var rimPen = new Pen(color, rimThickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(rimPen, rimRect);

        // Three spokes in a "Y" -- up, lower-left, lower-right -- meeting at a central hub, the
        // classic steering-wheel silhouette.
        var hubRadius = CanvasSize * 0.11f;
        float[] spokeAnglesDegrees = [-90f, 30f, 150f];
        using var spokePen = new Pen(color, rimThickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var angleDeg in spokeAnglesDegrees)
        {
            var angleRad = angleDeg * Math.PI / 180.0;
            var outer = new PointF(
                center.X + (float)(Math.Cos(angleRad) * (rimRadius - rimThickness / 2f)),
                center.Y + (float)(Math.Sin(angleRad) * (rimRadius - rimThickness / 2f)));
            var inner = new PointF(
                center.X + (float)(Math.Cos(angleRad) * hubRadius),
                center.Y + (float)(Math.Sin(angleRad) * hubRadius));
            g.DrawLine(spokePen, inner, outer);
        }

        using var hubBrush = new SolidBrush(color);
        g.FillEllipse(hubBrush, center.X - hubRadius, center.Y - hubRadius, hubRadius * 2, hubRadius * 2);
    }
}
