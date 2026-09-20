using System.Drawing;
using System.Windows.Forms;
using CMS.Core.Models;

namespace CMS.App.Controls;

/// <summary>
/// The playback scrubber: a 24-hour strip showing recorded segments, event
/// markers and the playhead. Clicking anywhere seeks to that moment.
/// </summary>
public class TimelineBar : Control
{
    private readonly List<RecordingSegment> _segments = new List<RecordingSegment>();
    private readonly List<PlaybackMarker> _markers = new List<PlaybackMarker>();

    private DateTime _windowStart = DateTime.Today;
    private TimeSpan _windowLength = TimeSpan.FromHours(24);
    private DateTime _position = DateTime.Now;
    private bool _dragging;
    private int _hoverX = -1;

    public TimelineBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Card;
        Font = Theme.Caption;
        Height = 64;
        Cursor = Cursors.Hand;
    }

    /// <summary>Local time at the left edge of the strip.</summary>
    public DateTime WindowStart
    {
        get => _windowStart;
        set
        {
            _windowStart = value;
            Invalidate();
        }
    }

    public TimeSpan WindowLength
    {
        get => _windowLength;
        set
        {
            _windowLength = value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;
            Invalidate();
        }
    }

    /// <summary>The playhead position, in local time.</summary>
    public DateTime Position
    {
        get => _position;
        set
        {
            _position = value;
            Invalidate();
        }
    }

    /// <summary>Raised when the operator clicks or drags to a new time.</summary>
    public event EventHandler<DateTime>? Seeked;

    public void SetSegments(IEnumerable<RecordingSegment> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments);
        Invalidate();
    }

    public void SetMarkers(IEnumerable<PlaybackMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        Invalidate();
    }

    private int TrackLeft => 8;

    private int TrackWidth => Math.Max(1, Width - 16);

    private int TimeToX(DateTime time)
    {
        var offset = (time - _windowStart).TotalSeconds / _windowLength.TotalSeconds;
        return TrackLeft + (int)(MathEx.Clamp(offset, 0, 1) * TrackWidth);
    }

    private DateTime XToTime(int x)
    {
        var ratio = MathEx.Clamp((x - TrackLeft) / (double)TrackWidth, 0, 1);
        return _windowStart.AddSeconds(ratio * _windowLength.TotalSeconds);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Position = XToTime(e.X);
            Seeked?.Invoke(this, Position);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hoverX = e.X;

        if (_dragging)
        {
            Position = XToTime(e.X);
            Seeked?.Invoke(this, Position);
        }
        else
        {
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverX = -1;
        _dragging = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(BackColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        const int trackTop = 10;
        const int trackHeight = 22;
        var track = new Rectangle(TrackLeft, trackTop, TrackWidth, trackHeight);

        Theme.FillRounded(g, track, 4, Theme.Input);

        // Recorded footage.
        foreach (var segment in _segments)
        {
            var x1 = TimeToX(segment.Start);
            var x2 = TimeToX(segment.End);
            var width = Math.Max(2, x2 - x1);

            var color = segment.HasEvents ? Theme.Accent : Theme.Info;
            using var brush = new SolidBrush(Color.FromArgb(190, color));
            g.FillRectangle(brush, x1, trackTop, width, trackHeight);
        }

        // Event markers sit just under the track.
        foreach (var marker in _markers)
        {
            var x = TimeToX(marker.Timestamp);
            var color = marker.Kind == EventKind.FaceRecognized ? Theme.Online : Theme.Warning;

            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x - 1, trackTop + trackHeight + 2, 2, 6);
        }

        DrawHourTicks(g, track);

        // Playhead.
        var playX = TimeToX(_position);
        using (var pen = new Pen(Theme.Accent, 2f))
        {
            g.DrawLine(pen, playX, trackTop - 4, playX, trackTop + trackHeight + 4);
        }

        using (var brush = new SolidBrush(Theme.Accent))
        {
            g.FillPolygon(brush, new[]
            {
                new Point(playX - 5, trackTop - 9),
                new Point(playX + 5, trackTop - 9),
                new Point(playX, trackTop - 3)
            });
        }

        if (_hoverX >= TrackLeft && _hoverX <= TrackLeft + TrackWidth)
        {
            var hoverTime = XToTime(_hoverX);
            var text = hoverTime.ToString("HH:mm:ss");
            var size = Theme.MeasureText(text, Theme.Caption);
            var box = new Rectangle(
                MathEx.Clamp(_hoverX - (size.Width / 2) - 5, 0, Math.Max(0, Width - size.Width - 10)),
                Height - 18,
                size.Width + 10,
                16);

            Theme.FillRounded(g, box, 4, Theme.Panel);
            Theme.DrawRounded(g, box, 4, Theme.BorderStrong);
            Theme.DrawText(g, text, Theme.Caption, Theme.TextPrimary, box,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void DrawHourTicks(Graphics g, Rectangle track)
    {
        var hours = (int)Math.Ceiling(_windowLength.TotalHours);
        if (hours <= 0)
        {
            return;
        }

        // Thin out labels so they never collide on a narrow window.
        var step = Math.Max(1, hours / Math.Max(1, TrackWidth / 56));

        using var pen = new Pen(Color.FromArgb(70, Theme.TextMuted));

        for (var hour = 0; hour <= hours; hour += step)
        {
            var time = _windowStart.AddHours(hour);
            if (time > _windowStart + _windowLength)
            {
                break;
            }

            var x = TimeToX(time);
            g.DrawLine(pen, x, track.Top, x, track.Bottom);

            Theme.DrawText(g, time.ToString("HH:mm"), Theme.Caption, Theme.TextMuted,
                new Rectangle(x - 22, track.Bottom + 8, 44, 14),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
