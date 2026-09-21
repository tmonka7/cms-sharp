using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

public enum ButtonVariant
{
    /// <summary>Solid red: the one primary action on a screen.</summary>
    Primary,

    /// <summary>Outlined card colour: secondary actions.</summary>
    Secondary,

    /// <summary>Transparent until hovered: toolbars and row actions.</summary>
    Ghost,

    /// <summary>Solid red used for destructive confirmation.</summary>
    Danger,

    /// <summary>Square glyph-only button.</summary>
    Icon
}

/// <summary>
/// The app-wide button. Owner-drawn so it matches the dark theme exactly,
/// including hover, press and disabled states.
/// </summary>
public class FlatButton : Control, IButtonControl
{
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private bool _hovered;
    private bool _pressed;
    private string _icon = string.Empty;
    private float _iconSize = 10f;
    private int _radius = Theme.ControlRadius;
    private bool _active;

    public FlatButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Size = new Size(96, 32);
        TabStop = true;
    }

    [DefaultValue(ButtonVariant.Secondary)]
    public ButtonVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;

            if (value == ButtonVariant.Icon)
            {
                _radius = 5;
            }

            Invalidate();
        }
    }

    /// <summary>Glyph drawn before the text, or alone for an icon button.</summary>
    [DefaultValue("")]
    public string Icon
    {
        get => _icon;
        set
        {
            _icon = value ?? string.Empty;
            Invalidate();
        }
    }

    [DefaultValue(10f)]
    public float IconSize
    {
        get => _iconSize;
        set
        {
            _iconSize = value;
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

    /// <summary>
    /// Latches the button into its hover appearance, used for toggle-style
    /// toolbar buttons such as the grid/list switch.
    /// </summary>
    [DefaultValue(false)]
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Invalidate();
        }
    }

    /// <summary>Overrides the accent colour for this button only.</summary>
    public Color? AccentOverride { get; set; }

    public DialogResult DialogResult { get; set; } = DialogResult.None;

    public void NotifyDefault(bool value)
    {
    }

    public void PerformClick()
    {
        if (Enabled)
        {
            OnClick(EventArgs.Empty);
        }
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            PerformClick();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        Theme.PaintSurface(g, this);

        var accent = AccentOverride ?? Theme.Accent;
        var bounds = new Rectangle(0, 0, Width, Height);

        Color fill;
        Color text;
        Color? border = null;

        switch (_variant)
        {
            case ButtonVariant.Primary:
            case ButtonVariant.Danger:
                fill = _pressed ? Theme.AccentPressed : _hovered ? Theme.AccentHover : accent;
                if (_variant == ButtonVariant.Danger)
                {
                    fill = _pressed ? Theme.AccentPressed : _hovered ? Theme.Offline : Theme.Offline;
                }

                text = Theme.TextOnAccent;
                break;

            case ButtonVariant.Secondary:
                fill = _pressed ? Theme.Input : _hovered || _active ? Theme.CardHover : Theme.Card;
                text = Theme.TextPrimary;
                border = _hovered || _active ? accent : Theme.BorderStrong;
                break;

            case ButtonVariant.Icon:
            case ButtonVariant.Ghost:
            default:
                if (_pressed)
                {
                    fill = accent;
                    text = Theme.TextOnAccent;
                }
                else if (_hovered || _active)
                {
                    fill = Theme.CardHover;
                    text = Theme.TextPrimary;
                }
                else
                {
                    fill = Color.Transparent;
                    text = ForeColor == Theme.TextPrimary ? Theme.TextSecondary : ForeColor;
                }

                break;
        }

        if (!Enabled)
        {
            fill = _variant is ButtonVariant.Primary or ButtonVariant.Danger
                ? Color.FromArgb(70, fill)
                : Theme.Card;

            text = Theme.TextMuted;
            border = Theme.Border;
        }

        if (fill != Color.Transparent)
        {
            Theme.FillRounded(g, bounds, _radius, fill);
        }

        if (border.HasValue)
        {
            Theme.DrawRounded(g, bounds, _radius, border.Value);
        }

        if (Focused && Enabled && _variant != ButtonVariant.Icon)
        {
            Theme.DrawRounded(g, bounds, _radius, Color.FromArgb(110, accent));
        }

        DrawContent(g, text);
    }

    private void DrawContent(Graphics g, Color text)
    {
        var hasIcon = !string.IsNullOrEmpty(_icon);
        var hasText = !string.IsNullOrEmpty(Text);

        if (hasIcon && !hasText)
        {
            Theme.DrawText(
                g, _icon, Theme.IconFont(_iconSize), text,
                new Rectangle(0, 0, Width, Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (!hasIcon && hasText)
        {
            Theme.DrawText(
                g, Text, Font, text,
                new Rectangle(4, 0, Width - 8, Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        if (!hasIcon)
        {
            return;
        }

        // Icon plus label: measure both and centre the pair as a unit.
        var iconFont = Theme.IconFont(_iconSize);
        var iconWidth = Theme.MeasureText(_icon, iconFont).Width;
        var textWidth = Theme.MeasureText(Text, Font).Width;
        const int gap = 6;

        var totalWidth = iconWidth + gap + textWidth;
        var startX = Math.Max(4, (Width - totalWidth) / 2);

        Theme.DrawText(g, _icon, iconFont, text,
            new Rectangle(startX, 0, iconWidth + 2, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        Theme.DrawText(g, Text, Font, text,
            new Rectangle(startX + iconWidth + gap, 0, Width - startX - iconWidth - gap - 4, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
