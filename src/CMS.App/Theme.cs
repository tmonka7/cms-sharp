using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CMS.App;

/// <summary>
/// The single source of truth for the dark surveillance look: near-black
/// surfaces, a hot red accent, and status colours that stay legible against
/// video.
/// </summary>
public static class Theme
{
    // ---- Surfaces ----
    public static readonly Color Background = Color.FromArgb(0x05, 0x07, 0x0A);
    public static readonly Color Shell = Color.FromArgb(0x0A, 0x0D, 0x13);
    public static readonly Color Panel = Color.FromArgb(0x10, 0x14, 0x1B);
    public static readonly Color Card = Color.FromArgb(0x15, 0x1A, 0x23);
    public static readonly Color CardHover = Color.FromArgb(0x1B, 0x20, 0x29);
    public static readonly Color Input = Color.FromArgb(0x0D, 0x11, 0x18);
    public static readonly Color VideoBackground = Color.FromArgb(0x07, 0x09, 0x0D);
    public static readonly Color RowHover = Color.FromArgb(0x14, 0x19, 0x22);

    // ---- Lines ----
    public static readonly Color Border = Color.FromArgb(0x1E, 0x25, 0x31);
    public static readonly Color BorderStrong = Color.FromArgb(0x2A, 0x33, 0x42);
    public static readonly Color Divider = Color.FromArgb(0x16, 0x1C, 0x25);

    // ---- Accent ----
    public static readonly Color Accent = Color.FromArgb(0xE0, 0x14, 0x2B);
    public static readonly Color AccentHover = Color.FromArgb(0xFF, 0x2A, 0x42);
    public static readonly Color AccentPressed = Color.FromArgb(0xB0, 0x0F, 0x21);
    public static readonly Color AccentDeep = Color.FromArgb(0x8A, 0x0A, 0x1A);

    // ---- Text ----
    public static readonly Color TextPrimary = Color.FromArgb(0xE9, 0xED, 0xF4);
    public static readonly Color TextSecondary = Color.FromArgb(0x98, 0xA2, 0xB3);
    public static readonly Color TextMuted = Color.FromArgb(0x66, 0x70, 0x85);
    public static readonly Color TextOnAccent = Color.White;

    // ---- Status ----
    public static readonly Color Online = Color.FromArgb(0x22, 0xC5, 0x5E);
    public static readonly Color Offline = Color.FromArgb(0xEF, 0x44, 0x44);
    public static readonly Color Warning = Color.FromArgb(0xF5, 0x9E, 0x0B);
    public static readonly Color Info = Color.FromArgb(0x3B, 0x82, 0xF6);
    public static readonly Color Cyan = Color.FromArgb(0x06, 0xB6, 0xD4);
    public static readonly Color Violet = Color.FromArgb(0x8B, 0x5C, 0xF6);

    // ---- Metrics ----
    public const int SidebarWidth = 190;
    public const int HeaderHeight = 52;
    public const int TitleBarHeight = 34;
    public const int CardRadius = 8;
    public const int ControlRadius = 6;
    public const int PagePadding = 16;

    // ---- Fonts ----
    public static readonly Font Body = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyBold = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Small = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font SmallBold = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Caption = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Medium = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font MediumBold = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Title = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Headline = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Display = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Point);

    private static string? _iconFamily;

    /// <summary>
    /// The system icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets
    /// on Windows 10. Using a system font means no icon assets to ship.
    /// </summary>
    public static string IconFamily
    {
        get
        {
            if (_iconFamily != null)
            {
                return _iconFamily;
            }

            var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToArray();

            foreach (var candidate in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                if (names.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    _iconFamily = candidate;
                    return _iconFamily;
                }
            }

            _iconFamily = "Segoe UI Symbol";
            return _iconFamily;
        }
    }

    public static Font Icon(float size) => new Font(IconFamily, size, FontStyle.Regular, GraphicsUnit.Point);

    private static readonly Dictionary<float, Font> IconCache = new Dictionary<float, Font>();

    /// <summary>Cached icon font, so repeated paints do not allocate.</summary>
    public static Font IconFont(float size)
    {
        lock (IconCache)
        {
            Font font;
            if (!IconCache.TryGetValue(size, out font))
            {
                font = Icon(size);
                IconCache[size] = font;
            }

            return font;
        }
    }

    /// <summary>Colour for a camera status dot or label.</summary>
    public static Color StatusColor(Core.Models.CameraStatus status) => status switch
    {
        Core.Models.CameraStatus.Online => Online,
        Core.Models.CameraStatus.Connecting => Warning,
        Core.Models.CameraStatus.Error => Offline,
        _ => Offline
    };

    public static Color SeverityColor(Core.Models.EventSeverity severity) => severity switch
    {
        Core.Models.EventSeverity.Critical => Offline,
        Core.Models.EventSeverity.Warning => Warning,
        _ => Info
    };

    // ---- Drawing helpers ----

    /// <summary>A rounded rectangle path; radius is clamped to the shorter side.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Rectangle bounds, int radius, Color color, int thickness = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Inset by half the pen width so the stroke lands inside the bounds.
        var rect = new Rectangle(
            bounds.X,
            bounds.Y,
            bounds.Width - thickness,
            bounds.Height - thickness);

        using var path = RoundedRect(rect, radius);
        using var pen = new Pen(color, thickness);
        g.DrawPath(pen, path);
    }

    /// <summary>Filled panel with a one-pixel border, the standard card look.</summary>
    public static void DrawCard(Graphics g, Rectangle bounds, Color fill, Color border, int radius = CardRadius)
    {
        FillRounded(g, bounds, radius, fill);
        DrawRounded(g, bounds, radius, border);
    }

    public static void DrawStatusDot(Graphics g, int x, int y, int size, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, y, size, size);
    }

    /// <summary>Crisp text with the standard no-prefix, no-clip flags.</summary>
    public static void DrawText(
        Graphics g,
        string text,
        Font font,
        Color color,
        Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPrefix);
    }

    public static Size MeasureText(string text, Font font)
        => TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);

    /// <summary>Turns on the antialiasing the custom controls assume.</summary>
    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
    }

    /// <summary>The red gradient used on the splash ring and primary surfaces.</summary>
    public static LinearGradientBrush AccentGradient(Rectangle bounds)
        => new LinearGradientBrush(bounds, AccentHover, AccentDeep, LinearGradientMode.ForwardDiagonal);

    /// <summary>
    /// Applies the base dark palette to a control and everything under it, so
    /// stock WinForms controls do not flash white.
    /// </summary>
    public static void Apply(Control control)
    {
        control.BackColor = Background;
        control.ForeColor = TextPrimary;
        control.Font = Body;
    }
}
