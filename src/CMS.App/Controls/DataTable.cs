using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>How a column sizes itself.</summary>
public enum ColumnSizing
{
    /// <summary>Fixed pixel width.</summary>
    Fixed,

    /// <summary>Shares the leftover width in proportion to <see cref="TableColumn.Weight"/>.</summary>
    Fill
}

/// <summary>One column of a <see cref="DataTable"/>.</summary>
public sealed class TableColumn
{
    public TableColumn(string header, int width, Func<object, string> value)
    {
        Header = header;
        Width = width;
        Value = value;
    }

    public string Header { get; set; }

    public int Width { get; set; }

    public float Weight { get; set; } = 1f;

    public ColumnSizing Sizing { get; set; } = ColumnSizing.Fixed;

    public ContentAlignment Alignment { get; set; } = ContentAlignment.MiddleLeft;

    /// <summary>Renders the cell text for a row item.</summary>
    public Func<object, string> Value { get; }

    /// <summary>Optional per-row colour, e.g. green for Online.</summary>
    public Func<object, Color>? Color { get; set; }

    /// <summary>When set, a coloured dot is drawn before the text.</summary>
    public Func<object, Color?>? Dot { get; set; }

    /// <summary>When set, the cell is drawn as a rounded status pill.</summary>
    public bool Pill { get; set; }

    /// <summary>When set, the cell shows this glyph instead of text.</summary>
    public Func<object, string>? Glyph { get; set; }

    /// <summary>Thumbnail image bytes for a photo column.</summary>
    public Func<object, byte[]?>? Image { get; set; }

    public Font? Font { get; set; }
}

/// <summary>A clickable glyph shown in the right-hand actions column.</summary>
public sealed class TableAction
{
    public TableAction(string glyph, string tooltip, Action<object> handler)
    {
        Glyph = glyph;
        Tooltip = tooltip;
        Handler = handler;
    }

    public string Glyph { get; }

    public string Tooltip { get; }

    public Action<object> Handler { get; }

    public Color Color { get; set; } = Theme.TextSecondary;

    public Color HoverColor { get; set; } = Theme.Accent;

    /// <summary>Hides the action for rows where it does not apply.</summary>
    public Func<object, bool>? IsVisible { get; set; }
}

/// <summary>
/// An owner-drawn table. The stock DataGridView cannot be themed to this design
/// without fighting it, and this control draws exactly the row layout the
/// mockups use: header, hover, status dots, pills, thumbnails and row actions.
/// </summary>
public class DataTable : Control
{
    private readonly List<TableColumn> _columns = new List<TableColumn>();
    private readonly List<TableAction> _actions = new List<TableAction>();
    private readonly List<object> _rows = new List<object>();
    private readonly Dictionary<object, Image> _imageCache = new Dictionary<object, Image>();

    private readonly VScrollBar _scrollBar;

    private int _hoverRow = -1;
    private int _hoverAction = -1;
    private int _selectedRow = -1;

    public DataTable()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Card;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;

        _scrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            Width = 12,
            Visible = false,
            SmallChange = 1,
            LargeChange = 5
        };

        _scrollBar.ValueChanged += (s, e) => Invalidate();
        Controls.Add(_scrollBar);
    }

    public int RowHeight { get; set; } = 42;

    public int HeaderHeight { get; set; } = 34;

    /// <summary>Message drawn in the body when there are no rows.</summary>
    public string EmptyText { get; set; } = "No records";

    public bool ShowHeader { get; set; } = true;

    public bool ShowRowNumbers { get; set; }

    /// <summary>
    /// Width reserved for the actions column.
    ///
    /// The icons need 30px each, but the header above them reads "Operation",
    /// which is wider than a single icon. Reserving only the icon width clips
    /// the header on any table that offers just one action.
    /// </summary>
    public int ActionsWidth => _actions.Count == 0
        ? 0
        : Math.Max((_actions.Count * 30) + 10, MinimumActionsWidth);

    /// <summary>Enough for the "Operation" header at the header font.</summary>
    private const int MinimumActionsWidth = 68;

    public IReadOnlyList<object> Rows => _rows;

    public object? SelectedItem
        => _selectedRow >= 0 && _selectedRow < _rows.Count ? _rows[_selectedRow] : null;

    public event EventHandler? SelectionChanged;

    public event EventHandler<object>? RowDoubleClicked;

    public void AddColumn(TableColumn column)
    {
        _columns.Add(column);
        Invalidate();
    }

    public void AddAction(TableAction action)
    {
        _actions.Add(action);
        Invalidate();
    }

    /// <summary>Replaces every row and keeps the selection when possible.</summary>
    public void SetRows(IEnumerable<object> rows)
    {
        var previous = SelectedItem;

        ClearImageCache();
        _rows.Clear();
        _rows.AddRange(rows);

        _selectedRow = previous == null ? -1 : _rows.IndexOf(previous);
        _hoverRow = -1;

        UpdateScrollBar();
        Invalidate();
    }

    public void ClearSelection()
    {
        _selectedRow = -1;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearImageCache()
    {
        foreach (var image in _imageCache.Values)
        {
            image.Dispose();
        }

        _imageCache.Clear();
    }

    private int VisibleRowCount
        => Math.Max(1, (Height - (ShowHeader ? HeaderHeight : 0)) / RowHeight);

    private void UpdateScrollBar()
    {
        var visible = VisibleRowCount;

        if (_rows.Count > visible)
        {
            _scrollBar.Visible = true;
            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = Math.Max(0, _rows.Count - 1);
            _scrollBar.LargeChange = visible;
            _scrollBar.Value = Math.Min(_scrollBar.Value, Math.Max(0, _rows.Count - visible));
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

    private int RowAt(int y)
    {
        var top = ShowHeader ? HeaderHeight : 0;
        if (y < top)
        {
            return -1;
        }

        var index = ((y - top) / RowHeight) + _scrollBar.Value;
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = RowAt(e.Y);
        var action = HitTestAction(e.X, row);

        if (row != _hoverRow || action != _hoverAction)
        {
            _hoverRow = row;
            _hoverAction = action;
            Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverRow = -1;
        _hoverAction = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private int HitTestAction(int x, int row)
    {
        if (row < 0 || _actions.Count == 0)
        {
            return -1;
        }

        var right = Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - 8;
        var start = right - (_actions.Count * 30);

        if (x < start || x > right)
        {
            return -1;
        }

        var index = (x - start) / 30;
        return index >= 0 && index < _actions.Count ? index : -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var row = RowAt(e.Y);

        if (row >= 0)
        {
            var actionIndex = HitTestAction(e.X, row);

            if (actionIndex >= 0)
            {
                var item = _rows[row];
                var action = _actions[actionIndex];

                if (action.IsVisible == null || action.IsVisible(item))
                {
                    action.Handler(item);
                    return;
                }
            }

            if (_selectedRow != row)
            {
                _selectedRow = row;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        var row = RowAt(e.Y);
        if (row >= 0 && HitTestAction(e.X, row) < 0)
        {
            RowDoubleClicked?.Invoke(this, _rows[row]);
        }

        base.OnMouseDoubleClick(e);
    }

    /// <summary>Resolves each column to an on-screen x position and width.</summary>
    private List<Rectangle> LayoutColumns()
    {
        var available = Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - ActionsWidth - 24;

        var fixedWidth = _columns.Where(c => c.Sizing == ColumnSizing.Fixed).Sum(c => c.Width);
        var totalWeight = _columns.Where(c => c.Sizing == ColumnSizing.Fill).Sum(c => c.Weight);
        var leftover = Math.Max(0, available - fixedWidth);

        var layout = new List<Rectangle>(_columns.Count);
        var x = 12;

        foreach (var column in _columns)
        {
            var width = column.Sizing == ColumnSizing.Fixed
                ? column.Width
                : totalWeight <= 0 ? column.Width : (int)(leftover * (column.Weight / totalWeight));

            layout.Add(new Rectangle(x, 0, Math.Max(10, width), RowHeight));
            x += width + 8;
        }

        return layout;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(BackColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        var layout = LayoutColumns();
        var y = 0;

        if (ShowHeader)
        {
            DrawHeaderRow(g, layout);
            y = HeaderHeight;
        }

        if (_rows.Count == 0)
        {
            Theme.DrawText(g, EmptyText, Theme.Body, Theme.TextMuted,
                new Rectangle(0, y, Width, Math.Max(40, Height - y)),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var first = _scrollBar.Value;
        var last = Math.Min(_rows.Count, first + VisibleRowCount + 1);

        for (var i = first; i < last; i++)
        {
            var rowBounds = new Rectangle(0, y, Width - (_scrollBar.Visible ? _scrollBar.Width : 0), RowHeight);
            DrawRow(g, _rows[i], i, rowBounds, layout);
            y += RowHeight;

            if (y > Height)
            {
                break;
            }
        }
    }

    private void DrawHeaderRow(Graphics g, List<Rectangle> layout)
    {
        var bounds = new Rectangle(0, 0, Width, HeaderHeight);

        using (var brush = new SolidBrush(Theme.Panel))
        {
            g.FillRectangle(brush, bounds);
        }

        using (var pen = new Pen(Theme.Border))
        {
            g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
        }

        for (var i = 0; i < _columns.Count; i++)
        {
            var cell = new Rectangle(layout[i].X, 0, layout[i].Width, HeaderHeight);
            Theme.DrawText(g, _columns[i].Header, Theme.SmallBold, Theme.TextSecondary, cell,
                ToFlags(_columns[i].Alignment) | TextFormatFlags.EndEllipsis);
        }

        if (_actions.Count > 0)
        {
            var right = Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - 8;

            // Drawn across the whole reserved width, not just the icon strip,
            // so the header is not clipped when there is a single action.
            Theme.DrawText(g, "Operation", Theme.SmallBold, Theme.TextSecondary,
                new Rectangle(right - ActionsWidth, 0, ActionsWidth, HeaderHeight),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void DrawRow(Graphics g, object item, int index, Rectangle bounds, List<Rectangle> layout)
    {
        if (index == _selectedRow)
        {
            using var brush = new SolidBrush(Color.FromArgb(38, Theme.Accent));
            g.FillRectangle(brush, bounds);
        }
        else if (index == _hoverRow)
        {
            using var brush = new SolidBrush(Theme.RowHover);
            g.FillRectangle(brush, bounds);
        }

        using (var pen = new Pen(Theme.Divider))
        {
            g.DrawLine(pen, 8, bounds.Bottom - 1, bounds.Right - 8, bounds.Bottom - 1);
        }

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var cell = new Rectangle(layout[i].X, bounds.Y, layout[i].Width, bounds.Height);
            DrawCell(g, column, item, cell);
        }

        DrawActions(g, item, bounds, index);
    }

    private void DrawCell(Graphics g, TableColumn column, object item, Rectangle cell)
    {
        if (column.Image != null)
        {
            DrawThumbnail(g, column, item, cell);
            return;
        }

        var color = column.Color != null ? column.Color(item) : Theme.TextPrimary;
        var font = column.Font ?? Font;
        var text = column.Value(item) ?? string.Empty;
        var flags = ToFlags(column.Alignment) | TextFormatFlags.EndEllipsis;

        if (column.Glyph != null)
        {
            var glyph = column.Glyph(item);
            Theme.DrawText(g, glyph, Theme.IconFont(10f), color,
                new Rectangle(cell.X, cell.Y, 18, cell.Height));

            Theme.DrawText(g, text, font, Theme.TextPrimary,
                new Rectangle(cell.X + 22, cell.Y, cell.Width - 22, cell.Height), flags);
            return;
        }

        if (column.Pill)
        {
            var size = Theme.MeasureText(text, Theme.Small);
            var pill = new Rectangle(
                cell.X,
                cell.Y + ((cell.Height - 20) / 2),
                size.Width + 18,
                20);

            Theme.FillRounded(g, pill, 10, Color.FromArgb(38, color));
            Theme.DrawRounded(g, pill, 10, Color.FromArgb(120, color));
            Theme.DrawText(g, text, Theme.Small, color, pill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (column.Dot != null)
        {
            var dot = column.Dot(item);
            if (dot.HasValue)
            {
                Theme.DrawStatusDot(g, cell.X, cell.Y + ((cell.Height - 7) / 2), 7, dot.Value);
                Theme.DrawText(g, text, font, color,
                    new Rectangle(cell.X + 13, cell.Y, cell.Width - 13, cell.Height), flags);
                return;
            }
        }

        Theme.DrawText(g, text, font, color, cell, flags);
    }

    private void DrawThumbnail(Graphics g, TableColumn column, object item, Rectangle cell)
    {
        var size = Math.Min(cell.Height - 8, 30);
        var target = new Rectangle(cell.X, cell.Y + ((cell.Height - size) / 2), size, size);

        Image? image;
        if (!_imageCache.TryGetValue(item, out image))
        {
            image = null;
            var bytes = column.Image!(item);

            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    image = System.Drawing.Image.FromStream(stream);
                }
                catch (ArgumentException)
                {
                    image = null;
                }
            }

            if (image != null)
            {
                _imageCache[item] = image;
            }
        }

        if (image == null)
        {
            Theme.FillRounded(g, target, 4, Theme.Input);
            Theme.DrawText(g, Icons.User, Theme.IconFont(11f), Theme.TextMuted, target,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using var clip = Theme.RoundedRect(target, 4);
        g.SetClip(clip);
        g.DrawImage(image, target);
        g.ResetClip();
    }

    private void DrawActions(Graphics g, object item, Rectangle bounds, int rowIndex)
    {
        if (_actions.Count == 0)
        {
            return;
        }

        var right = Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - 8;
        var x = right - (_actions.Count * 30);

        for (var i = 0; i < _actions.Count; i++)
        {
            var action = _actions[i];

            if (action.IsVisible != null && !action.IsVisible(item))
            {
                x += 30;
                continue;
            }

            var isHot = rowIndex == _hoverRow && i == _hoverAction;
            var cell = new Rectangle(x, bounds.Y, 30, bounds.Height);

            if (isHot)
            {
                var chip = new Rectangle(x + 3, bounds.Y + ((bounds.Height - 24) / 2), 24, 24);
                Theme.FillRounded(g, chip, 5, Theme.CardHover);
            }

            Theme.DrawText(g, action.Glyph, Theme.IconFont(10f),
                isHot ? action.HoverColor : action.Color, cell,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            x += 30;
        }
    }

    private static TextFormatFlags ToFlags(ContentAlignment alignment) => alignment switch
    {
        ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
        ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
        _ => TextFormatFlags.Left | TextFormatFlags.VerticalCenter
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearImageCache();
        }

        base.Dispose(disposing);
    }
}
