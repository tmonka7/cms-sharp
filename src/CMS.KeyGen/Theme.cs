using System.Drawing;
using System.Drawing.Drawing2D;

namespace CMS.App;

/// <summary>
/// The generator's copy of the product palette.
///
/// It deliberately duplicates the values in CMS.App/Theme.cs rather than
/// sharing that file, which also colours camera status and event severity and
/// therefore depends on CMS.Core. Keeping this copy costs a few constants;
/// sharing the original would cost this tool a dependency on the whole video
/// and analytics stack. If the product palette changes, change it here too.
///
/// The namespace matches the application so the form code reads the same as the
/// rest of the product.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x05, 0x07, 0x0A);
    public static readonly Color Panel = Color.FromArgb(0x10, 0x14, 0x1B);
    public static readonly Color Card = Color.FromArgb(0x15, 0x1A, 0x23);
    public static readonly Color CardHover = Color.FromArgb(0x1B, 0x20, 0x29);
    public static readonly Color Input = Color.FromArgb(0x0D, 0x11, 0x18);

    public static readonly Color Border = Color.FromArgb(0x1E, 0x25, 0x31);
    public static readonly Color BorderStrong = Color.FromArgb(0x2A, 0x33, 0x42);

    public static readonly Color Accent = Color.FromArgb(0xE0, 0x14, 0x2B);
    public static readonly Color AccentHover = Color.FromArgb(0xFF, 0x2A, 0x42);

    public static readonly Color TextPrimary = Color.FromArgb(0xE9, 0xED, 0xF4);
    public static readonly Color TextSecondary = Color.FromArgb(0x98, 0xA2, 0xB3);
    public static readonly Color TextMuted = Color.FromArgb(0x66, 0x70, 0x85);
    public static readonly Color TextOnAccent = Color.White;

    public static readonly Color Online = Color.FromArgb(0x22, 0xC5, 0x5E);
    public static readonly Color Offline = Color.FromArgb(0xEF, 0x44, 0x44);
    public static readonly Color Warning = Color.FromArgb(0xF5, 0x9E, 0x0B);

    public const int CardRadius = 8;

    public static readonly Font Body = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyBold = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Small = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Caption = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font MediumBold = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    /// <summary>Outlines a rounded rectangle, used for the card borders.</summary>
    public static void DrawRounded(Graphics g, Rectangle bounds, int radius, Color color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedPath(bounds, radius);
        using var pen = new Pen(color);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
