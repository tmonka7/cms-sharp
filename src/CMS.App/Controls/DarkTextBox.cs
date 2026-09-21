using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// A themed text field. WinForms cannot restyle the native edit control border,
/// so a borderless TextBox is hosted inside an owner-drawn rounded frame, with
/// optional leading glyph and placeholder text.
/// </summary>
public class DarkTextBox : Control
{
    private readonly TextBox _input;
    private string _placeholder = string.Empty;
    private string _icon = string.Empty;
    private bool _focused;
    private bool _hovered;

    public DarkTextBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        // The hosted edit control must exist first: setting Font or Size on this
        // control raises OnFontChanged / OnSizeChanged, both of which lay it out.
        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Input,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body
        };

        BackColor = Theme.Input;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(200, 32);

        _input.GotFocus += (s, e) => { _focused = true; Invalidate(); };
        _input.LostFocus += (s, e) => { _focused = false; Invalidate(); };
        _input.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
        _input.KeyDown += (s, e) => OnKeyDown(e);
        _input.KeyPress += (s, e) => OnKeyPress(e);
        _input.MouseEnter += (s, e) => { _hovered = true; Invalidate(); };
        _input.MouseLeave += (s, e) => { _hovered = false; Invalidate(); };

        Controls.Add(_input);
        LayoutInput();
    }

    /// <summary>The hosted edit control, for callers that need the raw box.</summary>
    [Browsable(false)]
    public TextBox Input => _input;

    [DefaultValue("")]
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Optional glyph drawn on the left inside the frame.</summary>
    [DefaultValue("")]
    public string Icon
    {
        get => _icon;
        set
        {
            _icon = value ?? string.Empty;
            LayoutInput();
            Invalidate();
        }
    }

    public override string Text
    {
        get => _input.Text;
        set
        {
            _input.Text = value ?? string.Empty;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool UseSystemPasswordChar
    {
        get => _input.UseSystemPasswordChar;
        set => _input.UseSystemPasswordChar = value;
    }

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _input.ReadOnly;
        set
        {
            _input.ReadOnly = value;
            _input.ForeColor = value ? Theme.TextSecondary : Theme.TextPrimary;
            Invalidate();
        }
    }

    public int MaxLength
    {
        get => _input.MaxLength;
        set => _input.MaxLength = value;
    }

    /// <summary>Right-aligned suffix such as a unit, drawn inside the frame.</summary>
    public string Suffix { get; set; } = string.Empty;

    public void SelectAll() => _input.SelectAll();

    public new void Focus() => _input.Focus();

    protected override void OnSizeChanged(EventArgs e)
    {
        LayoutInput();
        base.OnSizeChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _input.Font = Font;
        LayoutInput();
        base.OnFontChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _input.Enabled = Enabled;
        _input.BackColor = Theme.Input;
        _input.ForeColor = Enabled ? Theme.TextPrimary : Theme.TextMuted;
        Invalidate();
        base.OnEnabledChanged(e);
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _input.Focus();
        base.OnMouseDown(e);
    }

    private void LayoutInput()
    {
        var left = string.IsNullOrEmpty(_icon) ? 10 : 30;
        var right = string.IsNullOrEmpty(Suffix) ? 10 : 10 + Theme.MeasureText(Suffix, Theme.Small).Width + 6;
        var height = _input.PreferredHeight;

        _input.SetBounds(
            left,
            Math.Max(1, (Height - height) / 2),
            Math.Max(10, Width - left - right),
            height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        Theme.PaintSurface(g, this);

        var bounds = new Rectangle(0, 0, Width, Height);
        var border = !Enabled
            ? Theme.Border
            : _focused
                ? Theme.Accent
                : _hovered
                    ? Theme.BorderStrong
                    : Theme.Border;

        Theme.FillRounded(g, bounds, Theme.ControlRadius, Theme.Input);
        Theme.DrawRounded(g, bounds, Theme.ControlRadius, border);

        if (!string.IsNullOrEmpty(_icon))
        {
            Theme.DrawText(g, _icon, Theme.IconFont(10f), Theme.TextMuted,
                new Rectangle(9, 0, 20, Height));
        }

        if (string.IsNullOrEmpty(_input.Text) && !string.IsNullOrEmpty(_placeholder) && !_focused)
        {
            Theme.DrawText(g, _placeholder, Font, Theme.TextMuted,
                new Rectangle(_input.Left, 0, _input.Width, Height));
        }

        if (!string.IsNullOrEmpty(Suffix))
        {
            Theme.DrawText(g, Suffix, Theme.Small, Theme.TextMuted,
                new Rectangle(0, 0, Width - 10, Height),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
