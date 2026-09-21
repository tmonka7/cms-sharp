using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// The pill switch used for "Enable Object Detection" and similar on/off rows.
/// </summary>
public class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hovered;

    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Size = new Size(42, 22);
        Cursor = Cursors.Hand;
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    /// <summary>Colour of the track when on; defaults to the app accent.</summary>
    public Color OnColor { get; set; } = Theme.Accent;

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        Theme.PaintSurface(g, this);

        var height = Math.Min(Height, 22);
        var top = (Height - height) / 2;
        var track = new Rectangle(0, top, Width - 1, height - 1);
        var radius = height / 2;

        var trackColor = !Enabled
            ? Theme.Card
            : _checked ? OnColor : Theme.Input;

        var borderColor = !Enabled
            ? Theme.Border
            : _checked ? OnColor : _hovered ? Theme.BorderStrong : Theme.Border;

        Theme.FillRounded(g, track, radius, trackColor);
        Theme.DrawRounded(g, track, radius, borderColor);

        var knobSize = height - 7;
        var knobX = _checked ? track.Right - knobSize - 3 : track.Left + 4;
        var knobColor = !Enabled
            ? Theme.TextMuted
            : _checked ? Theme.TextOnAccent : Theme.TextMuted;

        using var brush = new SolidBrush(knobColor);
        g.FillEllipse(brush, knobX, top + 3, knobSize, knobSize);
    }
}
