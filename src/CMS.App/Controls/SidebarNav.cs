using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>One entry in the sidebar.</summary>
public sealed class NavItem
{
    public NavItem(string key, string label, string icon, string? permission = null)
    {
        Key = key;
        Label = label;
        Icon = icon;
        Permission = permission;
    }

    public string Key { get; }

    public string Label { get; }

    public string Icon { get; }

    /// <summary>Permission required to see this entry; null means always shown.</summary>
    public string? Permission { get; }

    /// <summary>Optional count badge, e.g. unread events.</summary>
    public int Badge { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// The left navigation rail. The selected row is a solid red block, matching the
/// product design.
/// </summary>
public class SidebarNav : Control
{
    private readonly List<NavItem> _items = new List<NavItem>();
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public SidebarNav()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Shell;
        ForeColor = Theme.TextSecondary;
        Font = Theme.Body;
        Width = Theme.SidebarWidth;
    }

    public int ItemHeight { get; set; } = 38;

    public int TopPadding { get; set; } = 8;

    public IReadOnlyList<NavItem> Items => _items;

    public string SelectedKey
        => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Key : string.Empty;

    public event EventHandler<NavItem>? ItemSelected;

    public void SetItems(IEnumerable<NavItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count > 0 ? 0 : -1;
        Invalidate();
    }

    /// <summary>Selects an entry by key without raising the event.</summary>
    public void SelectKey(string key, bool raiseEvent = true)
    {
        var index = _items.FindIndex(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index == _selectedIndex)
        {
            return;
        }

        _selectedIndex = index;
        Invalidate();

        if (raiseEvent)
        {
            ItemSelected?.Invoke(this, _items[index]);
        }
    }

    public void SetBadge(string key, int count)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item == null || item.Badge == count)
        {
            return;
        }

        item.Badge = count;
        Invalidate();
    }

    private int IndexAt(int y)
    {
        var index = (y - TopPadding) / ItemHeight;
        return index >= 0 && index < _items.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Y);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var index = IndexAt(e.Y);

        if (index >= 0 && _items[index].Enabled && index != _selectedIndex)
        {
            _selectedIndex = index;
            Invalidate();
            ItemSelected?.Invoke(this, _items[index]);
        }

        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Shell))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        using (var pen = new Pen(Theme.Border))
        {
            g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        var y = TopPadding;

        foreach (var item in _items)
        {
            var index = _items.IndexOf(item);
            var selected = index == _selectedIndex;
            var hovered = index == _hoverIndex;
            var bounds = new Rectangle(8, y, Width - 16, ItemHeight - 4);

            if (selected)
            {
                Theme.FillRounded(g, bounds, 6, Theme.Accent);
            }
            else if (hovered && item.Enabled)
            {
                Theme.FillRounded(g, bounds, 6, Theme.CardHover);
            }

            var foreground = !item.Enabled
                ? Theme.TextMuted
                : selected ? Theme.TextOnAccent : hovered ? Theme.TextPrimary : Theme.TextSecondary;

            Theme.DrawText(g, item.Icon, Theme.IconFont(11f), foreground,
                new Rectangle(bounds.X + 10, bounds.Y, 20, bounds.Height));

            var labelWidth = bounds.Width - 40 - (item.Badge > 0 ? 28 : 0);
            Theme.DrawText(g, item.Label, selected ? Theme.BodyBold : Font, foreground,
                new Rectangle(bounds.X + 36, bounds.Y, labelWidth, bounds.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (item.Badge > 0)
            {
                var text = item.Badge > 99 ? "99+" : item.Badge.ToString();
                var width = Theme.MeasureText(text, Theme.Caption).Width + 12;
                var badge = new Rectangle(bounds.Right - width - 8, bounds.Y + ((bounds.Height - 16) / 2), width, 16);

                Theme.FillRounded(g, badge, 8, selected ? Theme.TextOnAccent : Theme.Accent);
                Theme.DrawText(g, text, Theme.Caption,
                    selected ? Theme.Accent : Theme.TextOnAccent, badge,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            y += ItemHeight;
        }
    }
}
