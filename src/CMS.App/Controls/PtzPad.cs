using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CMS.Core.Models;

namespace CMS.App.Controls;

/// <summary>
/// The circular pan/tilt pad. Pressing a segment starts a continuous move and
/// releasing stops it, which is how ONVIF PTZ is meant to be driven.
/// </summary>
public class PtzPad : Control
{
    private PtzMove _hover = PtzMove.Stop;
    private PtzMove _pressed = PtzMove.Stop;

    public PtzPad()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Size = new Size(128, 128);
        Cursor = Cursors.Hand;
    }

    /// <summary>Raised when a direction is pressed; Stop means the centre.</summary>
    public event EventHandler<PtzMove>? MoveStarted;

    /// <summary>Raised when the pointer is released and motion should stop.</summary>
    public event EventHandler? MoveStopped;

    /// <summary>Raised when the centre button is clicked.</summary>
    public event EventHandler? HomeRequested;

    /// <summary>Includes the four diagonal segments.</summary>
    public bool EnableDiagonals { get; set; } = true;

    private int Radius => (Math.Min(Width, Height) / 2) - 2;

    private Point Center => new Point(Width / 2, Height / 2);

    /// <summary>Maps a point to the segment under it.</summary>
    private PtzMove HitTest(Point point)
    {
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance > Radius)
        {
            return PtzMove.Stop;
        }

        // The centre third of the pad is the home button.
        if (distance < Radius * 0.36)
        {
            return PtzMove.Stop;
        }

        // Screen y grows downward, so negate it to get a normal angle.
        var angle = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360;
        }

        if (EnableDiagonals)
        {
            // Eight 45 degree sectors, offset so each direction is centred.
            var sector = (int)Math.Floor(((angle + 22.5) % 360) / 45.0);
            return sector switch
            {
                0 => PtzMove.Right,
                1 => PtzMove.UpRight,
                2 => PtzMove.Up,
                3 => PtzMove.UpLeft,
                4 => PtzMove.Left,
                5 => PtzMove.DownLeft,
                6 => PtzMove.Down,
                _ => PtzMove.DownRight
            };
        }

        var quadrant = (int)Math.Floor(((angle + 45) % 360) / 90.0);
        return quadrant switch
        {
            0 => PtzMove.Right,
            1 => PtzMove.Up,
            2 => PtzMove.Left,
            _ => PtzMove.Down
        };
    }

    private bool IsCenter(Point point)
    {
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) < Radius * 0.36;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_pressed != PtzMove.Stop)
        {
            _pressed = PtzMove.Stop;
            MoveStopped?.Invoke(this, EventArgs.Empty);
        }

        _hover = PtzMove.Stop;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        if (IsCenter(e.Location))
        {
            HomeRequested?.Invoke(this, EventArgs.Empty);
            base.OnMouseDown(e);
            return;
        }

        var hit = HitTest(e.Location);
        if (hit != PtzMove.Stop)
        {
            _pressed = hit;
            Invalidate();
            MoveStarted?.Invoke(this, hit);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed != PtzMove.Stop)
        {
            _pressed = PtzMove.Stop;
            Invalidate();
            MoveStopped?.Invoke(this, EventArgs.Empty);
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var size = Radius * 2;
        var outer = new Rectangle(Center.X - Radius, Center.Y - Radius, size, size);

        using (var brush = new SolidBrush(Theme.Input))
        {
            g.FillEllipse(brush, outer);
        }

        using (var pen = new Pen(Theme.Border))
        {
            g.DrawEllipse(pen, outer);
        }

        var directions = EnableDiagonals
            ? new[]
            {
                PtzMove.Up, PtzMove.UpRight, PtzMove.Right, PtzMove.DownRight,
                PtzMove.Down, PtzMove.DownLeft, PtzMove.Left, PtzMove.UpLeft
            }
            : new[] { PtzMove.Up, PtzMove.Right, PtzMove.Down, PtzMove.Left };

        var sweep = 360f / directions.Length;

        foreach (var direction in directions)
        {
            if (direction != _hover && direction != _pressed)
            {
                continue;
            }

            var index = Array.IndexOf(directions, direction);

            // GDI+ angles start at 3 o'clock and go clockwise; Up is index 0.
            var start = (index * sweep) - 90 - (sweep / 2);

            using var path = new GraphicsPath();
            path.AddArc(outer, start, sweep);
            path.AddLine(
                Center.X + (float)(Radius * 0.36 * Math.Cos((start + sweep) * Math.PI / 180)),
                Center.Y + (float)(Radius * 0.36 * Math.Sin((start + sweep) * Math.PI / 180)),
                Center.X + (float)(Radius * 0.36 * Math.Cos(start * Math.PI / 180)),
                Center.Y + (float)(Radius * 0.36 * Math.Sin(start * Math.PI / 180)));
            path.CloseFigure();

            var color = direction == _pressed
                ? Theme.Accent
                : Color.FromArgb(60, Theme.Accent);

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        DrawArrow(g, Icons.ChevronUp, Center.X, Center.Y - (int)(Radius * 0.72), PtzMove.Up);
        DrawArrow(g, Icons.ChevronDown, Center.X, Center.Y + (int)(Radius * 0.72), PtzMove.Down);
        DrawArrow(g, Icons.ChevronLeft, Center.X - (int)(Radius * 0.72), Center.Y, PtzMove.Left);
        DrawArrow(g, Icons.ChevronRight, Center.X + (int)(Radius * 0.72), Center.Y, PtzMove.Right);

        var innerRadius = (int)(Radius * 0.36);
        var inner = new Rectangle(
            Center.X - innerRadius,
            Center.Y - innerRadius,
            innerRadius * 2,
            innerRadius * 2);

        using (var brush = new SolidBrush(Theme.Card))
        {
            g.FillEllipse(brush, inner);
        }

        using (var pen = new Pen(Theme.Accent, 2f))
        {
            g.DrawEllipse(pen, inner);
        }

        Theme.DrawText(g, Icons.Target, Theme.IconFont(11f), Theme.Accent, inner,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawArrow(Graphics g, string glyph, int x, int y, PtzMove direction)
    {
        var color = direction == _pressed
            ? Theme.TextOnAccent
            : direction == _hover ? Theme.TextPrimary : Theme.TextSecondary;

        Theme.DrawText(g, glyph, Theme.IconFont(9f), color,
            new Rectangle(x - 10, y - 10, 20, 20),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
