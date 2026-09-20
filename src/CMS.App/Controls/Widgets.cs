using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// A dashboard statistic: coloured glyph chip, caption and a large value.
/// </summary>
public class MetricTile : Control
{
    private string _value = "0";

    public MetricTile()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Font = Theme.Body;
        Size = new Size(200, 78);
    }

    public string Caption { get; set; } = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value ?? string.Empty;
            Invalidate();
        }
    }

    public string Icon { get; set; } = Icons.Camera;

    public Color IconColor { get; set; } = Theme.Accent;

    /// <summary>Optional second line, e.g. "1.2 TB free".</summary>
    public string Detail { get; set; } = string.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var bounds = new Rectangle(0, 0, Width, Height);
        Theme.DrawCard(g, bounds, Theme.Card, Theme.Border);

        var chip = new Rectangle(14, (Height - 38) / 2, 38, 38);
        Theme.FillRounded(g, chip, 8, Color.FromArgb(40, IconColor));
        Theme.DrawText(g, Icon, Theme.IconFont(14f), IconColor, chip,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var textX = chip.Right + 12;
        var textWidth = Width - textX - 12;
        var hasDetail = !string.IsNullOrEmpty(Detail);

        var captionY = hasDetail ? 12 : (Height / 2) - 20;

        Theme.DrawText(g, Caption, Theme.Small, Theme.TextSecondary,
            new Rectangle(textX, captionY, textWidth, 16));

        Theme.DrawText(g, _value, Theme.Headline, Theme.TextPrimary,
            new Rectangle(textX, captionY + 16, textWidth, 26));

        if (hasDetail)
        {
            Theme.DrawText(g, Detail, Theme.Caption, Theme.TextMuted,
                new Rectangle(textX, captionY + 42, textWidth, 14));
        }
    }
}

/// <summary>
/// A labelled horizontal meter with a percentage on the right, used for the
/// CPU / memory / network rows on the dashboard.
/// </summary>
public class LinearMeter : Control
{
    private double _percent;

    public LinearMeter()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Font = Theme.Small;
        Height = 34;
    }

    public string Caption { get; set; } = string.Empty;

    public double Percent
    {
        get => _percent;
        set
        {
            var clamped = MathEx.Clamp(value, 0, 100);
            if (Math.Abs(_percent - clamped) < 0.05)
            {
                return;
            }

            _percent = clamped;
            Invalidate();
        }
    }

    /// <summary>Overrides the right-hand text; defaults to the percentage.</summary>
    public string ValueText { get; set; } = string.Empty;

    public Color BarColor { get; set; } = Theme.Accent;

    /// <summary>Turns the bar amber above 75% and red above 90%.</summary>
    public bool ColorByLoad { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var color = BarColor;
        if (ColorByLoad)
        {
            color = _percent >= 90 ? Theme.Offline : _percent >= 75 ? Theme.Warning : BarColor;
        }

        var text = string.IsNullOrEmpty(ValueText) ? _percent.ToString("0") + "%" : ValueText;
        var textWidth = Math.Max(44, Theme.MeasureText(text, Theme.Small).Width + 6);

        Theme.DrawText(g, Caption, Theme.Small, Theme.TextSecondary,
            new Rectangle(0, 0, Width - textWidth, 16));

        Theme.DrawText(g, text, Theme.SmallBold, Theme.TextPrimary,
            new Rectangle(Width - textWidth, 0, textWidth, 16),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        var track = new Rectangle(0, 20, Math.Max(1, Width), 6);
        Theme.FillRounded(g, track, 3, Theme.Input);

        var fillWidth = (int)(track.Width * _percent / 100.0);
        if (fillWidth > 0)
        {
            Theme.FillRounded(g, new Rectangle(track.X, track.Y, Math.Max(4, fillWidth), track.Height), 3, color);
        }
    }
}

/// <summary>
/// A circular percentage gauge, used by the System Information screen for CPU,
/// memory and disk.
/// </summary>
public class RingGauge : Control
{
    private double _percent;

    public RingGauge()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Size = new Size(96, 96);
    }

    public double Percent
    {
        get => _percent;
        set
        {
            var clamped = MathEx.Clamp(value, 0, 100);
            if (Math.Abs(_percent - clamped) < 0.05)
            {
                return;
            }

            _percent = clamped;
            Invalidate();
        }
    }

    public string Caption { get; set; } = string.Empty;

    public Color RingColor { get; set; } = Theme.Accent;

    public int Thickness { get; set; } = 8;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var size = Math.Min(Width, Height - (string.IsNullOrEmpty(Caption) ? 0 : 18));
        var inset = Thickness / 2;
        var ring = new Rectangle(
            ((Width - size) / 2) + inset,
            inset,
            Math.Max(1, size - Thickness),
            Math.Max(1, size - Thickness));

        using (var pen = new Pen(Theme.Input, Thickness))
        {
            g.DrawEllipse(pen, ring);
        }

        var sweep = (float)(360.0 * _percent / 100.0);
        if (sweep > 0)
        {
            using var pen = new Pen(RingColor, Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // Start at 12 o'clock so the gauge fills clockwise from the top.
            g.DrawArc(pen, ring, -90, sweep);
        }

        Theme.DrawText(g, _percent.ToString("0") + "%", Theme.MediumBold, Theme.TextPrimary,
            new Rectangle(0, ring.Y, Width, ring.Height),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (!string.IsNullOrEmpty(Caption))
        {
            Theme.DrawText(g, Caption, Theme.Small, Theme.TextSecondary,
                new Rectangle(0, ring.Bottom + 4, Width, 16),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>A themed check box drawn to match the rest of the controls.</summary>
public class DarkCheckBox : CheckBox
{
    public DarkCheckBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        ForeColor = Theme.TextSecondary;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Height = 22;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var box = new Rectangle(0, (Height - 16) / 2, 16, 16);

        if (Checked)
        {
            Theme.FillRounded(g, box, 4, Theme.Accent);
            Theme.DrawRounded(g, box, 4, Theme.Accent);
            Theme.DrawText(g, Icons.Accept, Theme.IconFont(8f), Theme.TextOnAccent, box,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            Theme.FillRounded(g, box, 4, Theme.Input);
            Theme.DrawRounded(g, box, 4, Enabled ? Theme.BorderStrong : Theme.Border);
        }

        var color = !Enabled ? Theme.TextMuted : Checked ? Theme.TextPrimary : ForeColor;

        Theme.DrawText(g, Text, Font, color,
            new Rectangle(box.Right + 8, 0, Width - box.Right - 8, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A themed slider for confidence and similarity thresholds.</summary>
public class DarkSlider : Control
{
    private double _value = 0.5;
    private bool _dragging;

    public DarkSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Height = 22;
        Cursor = Cursors.Hand;
    }

    public double Minimum { get; set; }

    public double Maximum { get; set; } = 1.0;

    public double Value
    {
        get => _value;
        set
        {
            var clamped = MathEx.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - clamped) < 0.0001)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    public Color BarColor { get; set; } = Theme.Accent;

    private void SetFromMouse(int x)
    {
        var track = Width - 14;
        if (track <= 0)
        {
            return;
        }

        var ratio = MathEx.Clamp((x - 7.0) / track, 0, 1);
        Value = Minimum + (ratio * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        SetFromMouse(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            SetFromMouse(e.X);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var track = new Rectangle(7, (Height - 4) / 2, Math.Max(1, Width - 14), 4);
        Theme.FillRounded(g, track, 2, Theme.Input);

        var range = Maximum - Minimum;
        var ratio = range <= 0 ? 0 : (_value - Minimum) / range;
        var fill = (int)(track.Width * ratio);

        if (fill > 0)
        {
            Theme.FillRounded(g, new Rectangle(track.X, track.Y, fill, track.Height), 2, BarColor);
        }

        var knobX = track.X + fill - 7;
        var knob = new Rectangle(MathEx.Clamp(knobX, 0, Width - 14), (Height - 14) / 2, 14, 14);

        using (var brush = new SolidBrush(Theme.TextOnAccent))
        {
            g.FillEllipse(brush, knob);
        }

        using (var pen = new Pen(BarColor, 3f))
        {
            g.DrawEllipse(pen, knob);
        }
    }
}

/// <summary>
/// A plain label that paints itself on the dark background without the flicker
/// of the stock control.
/// </summary>
public class DarkLabel : Control
{
    public DarkLabel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        ForeColor = Theme.TextSecondary;
        Font = Theme.Body;
        Height = 20;
    }

    public ContentAlignment Alignment { get; set; } = ContentAlignment.MiddleLeft;

    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var flags = Alignment switch
        {
            ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
            ContentAlignment.TopLeft => TextFormatFlags.Left | TextFormatFlags.Top,
            _ => TextFormatFlags.Left | TextFormatFlags.VerticalCenter
        };

        Theme.DrawText(e.Graphics, Text, Font, ForeColor, new Rectangle(0, 0, Width, Height),
            flags | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A small rounded status pill, e.g. Online / Recognized.</summary>
public class StatusPill : Control
{
    public StatusPill()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Font = Theme.Small;
        Size = new Size(80, 22);
    }

    public Color PillColor { get; set; } = Theme.Online;

    public bool ShowDot { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, bounds, Height / 2, Color.FromArgb(38, PillColor));
        Theme.DrawRounded(g, bounds, Height / 2, Color.FromArgb(120, PillColor));

        var textX = 10;
        if (ShowDot)
        {
            Theme.DrawStatusDot(g, 9, (Height - 7) / 2, 7, PillColor);
            textX = 21;
        }

        Theme.DrawText(g, Text, Font, PillColor,
            new Rectangle(textX, 0, Width - textX - 8, Height));
    }
}
