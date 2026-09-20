using System.Drawing;
using System.Windows.Forms;
using CMS.Core.Models;

namespace CMS.App.Controls;

/// <summary>
/// The compact scrolling event feed used on the dashboard and beside live view:
/// severity dot, time, message and camera on one line.
/// </summary>
public class EventListView : Control
{
    private readonly List<EventEntry> _items = new List<EventEntry>();
    private readonly VScrollBar _scrollBar;
    private int _hoverIndex = -1;

    public EventListView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Card;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Small;

        _scrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            Width = 10,
            Visible = false,
            SmallChange = 1,
            LargeChange = 4
        };

        _scrollBar.ValueChanged += (s, e) => Invalidate();
        Controls.Add(_scrollBar);
    }

    public int ItemHeight { get; set; } = 34;

    /// <summary>Most recent entries are kept and older ones discarded.</summary>
    public int Capacity { get; set; } = 300;

    public string EmptyText { get; set; } = "No recent events";

    /// <summary>Shows the camera name on a second line.</summary>
    public bool ShowCamera { get; set; } = true;

    public event EventHandler<EventEntry>? ItemActivated;

    public void SetItems(IEnumerable<EventEntry> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Trim();
        UpdateScrollBar();
        Invalidate();
    }

    /// <summary>Inserts a new event at the top, as the live feed does.</summary>
    public void Prepend(EventEntry entry)
    {
        _items.Insert(0, entry);
        Trim();
        UpdateScrollBar();
        Invalidate();
    }

    public void Clear()
    {
        _items.Clear();
        UpdateScrollBar();
        Invalidate();
    }

    private void Trim()
    {
        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
    }

    private int VisibleCount => Math.Max(1, Height / ItemHeight);

    private void UpdateScrollBar()
    {
        var visible = VisibleCount;

        if (_items.Count > visible)
        {
            _scrollBar.Visible = true;
            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = Math.Max(0, _items.Count - 1);
            _scrollBar.LargeChange = visible;
            _scrollBar.Value = Math.Min(_scrollBar.Value, Math.Max(0, _items.Count - visible));
        }
        else
        {
            _scrollBar.Visible = false;
            _scrollBar.Value = 0;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        UpdateScrollBar();
        base.OnSizeChanged(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_scrollBar.Visible)
        {
            var delta = e.Delta > 0 ? -2 : 2;
            _scrollBar.Value = MathEx.Clamp(
                _scrollBar.Value + delta,
                _scrollBar.Minimum,
                Math.Max(_scrollBar.Minimum, _scrollBar.Maximum - _scrollBar.LargeChange + 1));
        }

        base.OnMouseWheel(e);
    }

    private int IndexAt(int y)
    {
        var index = (y / ItemHeight) + _scrollBar.Value;
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
        if (index >= 0)
        {
            ItemActivated?.Invoke(this, _items[index]);
        }

        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(BackColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        if (_items.Count == 0)
        {
            Theme.DrawText(g, EmptyText, Theme.Small, Theme.TextMuted,
                ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var width = Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        var first = _scrollBar.Value;
        var last = Math.Min(_items.Count, first + VisibleCount + 1);
        var y = 0;

        for (var i = first; i < last; i++)
        {
            var item = _items[i];
            var bounds = new Rectangle(0, y, width, ItemHeight);

            if (i == _hoverIndex)
            {
                Theme.FillRounded(g, new Rectangle(2, y + 1, width - 4, ItemHeight - 2), 5, Theme.RowHover);
            }

            var color = Theme.SeverityColor(item.Severity);
            Theme.DrawStatusDot(g, 8, y + ((ItemHeight - 7) / 2), 7, color);

            var timeWidth = Theme.MeasureText(item.TimeText, Theme.Caption).Width + 6;
            Theme.DrawText(g, item.TimeText, Theme.Caption, Theme.TextMuted,
                new Rectangle(21, y, timeWidth, ItemHeight));

            var textX = 21 + timeWidth + 4;
            var cameraWidth = 0;

            if (ShowCamera && !string.IsNullOrEmpty(item.CameraName))
            {
                cameraWidth = Math.Min(96, Theme.MeasureText(item.CameraName, Theme.Caption).Width + 8);
                Theme.DrawText(g, item.CameraName, Theme.Caption, Theme.TextMuted,
                    new Rectangle(width - cameraWidth - 8, y, cameraWidth, ItemHeight),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            var message = string.IsNullOrEmpty(item.Message) ? item.TypeText : item.TypeText + ": " + item.Message;

            Theme.DrawText(g, message, Theme.Small, Theme.TextPrimary,
                new Rectangle(textX, y, Math.Max(20, width - textX - cameraWidth - 16), ItemHeight),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            y += ItemHeight;
        }
    }
}
