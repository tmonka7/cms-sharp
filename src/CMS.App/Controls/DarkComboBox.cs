using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// A flat, dark drop-down. The native combo paints a light border and arrow, so
/// the whole control is owner-drawn on top of a borderless base.
/// </summary>
public class DarkComboBox : ComboBox
{
    private bool _hovered;

    public DarkComboBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Input;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ItemHeight = 22;
        Height = 32;
        Cursor = Cursors.Hand;
    }

    /// <summary>Placeholder shown when nothing is selected.</summary>
    public string Placeholder { get; set; } = string.Empty;

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

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Panel))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        var text = GetItemText(Items[e.Index]);
        Theme.DrawText(
            g, text, Font,
            selected ? Theme.TextOnAccent : Theme.TextPrimary,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        Theme.PaintSurface(g, this);

        var bounds = new Rectangle(0, 0, Width, Height);
        var border = DroppedDown ? Theme.Accent : _hovered ? Theme.BorderStrong : Theme.Border;

        Theme.FillRounded(g, bounds, Theme.ControlRadius, Enabled ? Theme.Input : Theme.Card);
        Theme.DrawRounded(g, bounds, Theme.ControlRadius, border);

        var hasSelection = SelectedIndex >= 0;
        var text = hasSelection ? GetItemText(SelectedItem) : Placeholder;
        var color = !Enabled
            ? Theme.TextMuted
            : hasSelection ? Theme.TextPrimary : Theme.TextMuted;

        Theme.DrawText(
            g, text, Font, color,
            new Rectangle(10, 0, Width - 34, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        Theme.DrawText(
            g, Icons.ChevronDown, Theme.IconFont(7.5f), Theme.TextMuted,
            new Rectangle(Width - 26, 0, 20, Height),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Repaints the closed box when the drop-down opens or closes.</summary>
    protected override void OnDropDown(EventArgs e)
    {
        Invalidate();
        base.OnDropDown(e);
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        Invalidate();
        base.OnDropDownClosed(e);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        Invalidate();
        base.OnSelectedIndexChanged(e);
    }
}
