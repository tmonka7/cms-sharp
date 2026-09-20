using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// A rounded, bordered surface with an optional title row. Every grouped block
/// in the app sits on one of these.
/// </summary>
public class CardPanel : Panel
{
    private string _title = string.Empty;
    private string _titleIcon = string.Empty;
    private Color _fill = Theme.Card;
    private Color _borderColor = Theme.Border;
    private int _radius = Theme.CardRadius;
    private int _titleHeight = 34;

    public CardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Padding = new Padding(14);
    }

    [DefaultValue("")]
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            UpdatePadding();
            Invalidate();
        }
    }

    /// <summary>Optional glyph drawn to the left of the title.</summary>
    [DefaultValue("")]
    public string TitleIcon
    {
        get => _titleIcon;
        set
        {
            _titleIcon = value ?? string.Empty;
            Invalidate();
        }
    }

    public Color Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    public int Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Invalidate();
        }
    }

    /// <summary>Right-aligned text in the title row, e.g. a count or a status.</summary>
    public string TitleSuffix { get; set; } = string.Empty;

    public Color TitleSuffixColor { get; set; } = Theme.TextMuted;

    /// <summary>Content area inside the border and below the title.</summary>
    public Rectangle ContentBounds
    {
        get
        {
            var top = string.IsNullOrEmpty(_title) ? Padding.Top : _titleHeight + Padding.Top;
            return new Rectangle(
                Padding.Left,
                top,
                Math.Max(0, Width - Padding.Left - Padding.Right),
                Math.Max(0, Height - top - Padding.Bottom));
        }
    }

    private void UpdatePadding()
    {
        // Callers position children with DockStyle, so the title row is carved
        // out of the padding rather than by a separate child control.
        var top = string.IsNullOrEmpty(_title) ? 14 : _titleHeight + 6;
        Padding = new Padding(14, top, 14, 14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var bounds = new Rectangle(0, 0, Width, Height);
        Theme.DrawCard(g, bounds, _fill, _borderColor, _radius);

        if (string.IsNullOrEmpty(_title))
        {
            base.OnPaint(e);
            return;
        }

        var x = 14;
        var row = new Rectangle(x, 10, Width - 28, 20);

        if (!string.IsNullOrEmpty(_titleIcon))
        {
            Theme.DrawText(g, _titleIcon, Theme.IconFont(11f), Theme.Accent, new Rectangle(x, row.Y, 20, row.Height));
            x += 24;
        }

        var titleWidth = Theme.MeasureText(_title, Theme.MediumBold).Width;
        Theme.DrawText(g, _title, Theme.MediumBold, Theme.TextPrimary,
            new Rectangle(x, row.Y, titleWidth + 4, row.Height));

        if (!string.IsNullOrEmpty(TitleSuffix))
        {
            Theme.DrawText(g, TitleSuffix, Theme.Small, TitleSuffixColor,
                new Rectangle(x, row.Y, Width - x - 14, row.Height),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        base.OnPaint(e);
    }
}
