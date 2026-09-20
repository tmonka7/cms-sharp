using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CMS.Core.Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CMS.App.Controls;

/// <summary>
/// Renders one camera: the decoded frame, letterboxed to preserve aspect ratio,
/// plus the detection and recognition overlays drawn on top.
///
/// Frames arrive on a decoder thread. They are converted once there and swapped
/// under a lock, and only a single repaint is ever queued, so a fast camera
/// cannot flood the UI thread.
/// </summary>
public class VideoView : Control
{
    private readonly object _frameGate = new object();
    private Bitmap? _current;
    private Bitmap? _pending;
    private volatile bool _repaintQueued;

    private IList<Detection> _detections = new List<Detection>();
    private IList<FaceMatch> _faces = new List<FaceMatch>();

    private Rectangle _videoBounds;
    private int _frameWidth;
    private int _frameHeight;
    private bool _hovered;

    public VideoView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.VideoBackground;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
    }

    /// <summary>The camera shown here, used for the overlay caption.</summary>
    public CameraDevice? Camera { get; set; }

    /// <summary>Draws the channel name, status and clock over the video.</summary>
    public bool ShowOverlayHeader { get; set; } = true;

    /// <summary>Draws object-detection boxes.</summary>
    public bool ShowDetections { get; set; } = true;

    /// <summary>Draws face-recognition boxes.</summary>
    public bool ShowFaces { get; set; } = true;

    /// <summary>Shows the pulsing REC badge.</summary>
    public bool IsRecording { get; set; }

    /// <summary>Highlights the tile as the selected camera in a grid.</summary>
    public bool IsSelected { get; set; }

    public bool ShowBorder { get; set; } = true;

    public int Radius { get; set; } = 6;

    /// <summary>Text shown when there is no video, e.g. the offline reason.</summary>
    public string PlaceholderText { get; set; } = "No Signal";

    public CameraStatus Status { get; set; } = CameraStatus.Offline;

    public bool HasFrame
    {
        get
        {
            lock (_frameGate)
            {
                return _current != null;
            }
        }
    }

    /// <summary>
    /// Accepts a decoded frame from any thread. The Mat is not retained.
    /// </summary>
    public void SetFrame(Mat frame)
    {
        if (frame == null || frame.Empty() || IsDisposed)
        {
            return;
        }

        Bitmap bitmap;
        try
        {
            bitmap = BitmapConverter.ToBitmap(frame);
        }
        catch (Exception ex) when (ex is OpenCVException or ArgumentException)
        {
            return;
        }

        _frameWidth = frame.Width;
        _frameHeight = frame.Height;

        lock (_frameGate)
        {
            _pending?.Dispose();
            _pending = bitmap;
        }

        QueueRepaint();
    }

    public void SetDetections(IList<Detection> detections)
    {
        _detections = detections ?? new List<Detection>();
        QueueRepaint();
    }

    public void SetFaces(IList<FaceMatch> faces)
    {
        _faces = faces ?? new List<FaceMatch>();
        QueueRepaint();
    }

    /// <summary>Drops the current image, e.g. when the camera goes offline.</summary>
    public void ClearFrame()
    {
        lock (_frameGate)
        {
            _current?.Dispose();
            _current = null;
            _pending?.Dispose();
            _pending = null;
        }

        _detections = new List<Detection>();
        _faces = new List<FaceMatch>();
        QueueRepaint();
    }

    /// <summary>A copy of the displayed image, for the snapshot action.</summary>
    public Bitmap? CloneCurrentFrame()
    {
        lock (_frameGate)
        {
            return _current == null ? null : new Bitmap(_current);
        }
    }

    private void QueueRepaint()
    {
        if (_repaintQueued || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _repaintQueued = true;

        try
        {
            BeginInvoke(new Action(() =>
            {
                _repaintQueued = false;
                if (!IsDisposed)
                {
                    Invalidate();
                }
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _repaintQueued = false;
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        var bounds = new Rectangle(0, 0, Width, Height);
        Theme.FillRounded(g, bounds, Radius, Theme.VideoBackground);

        // Swap in the newest frame at paint time so the decoder never blocks.
        lock (_frameGate)
        {
            if (_pending != null)
            {
                _current?.Dispose();
                _current = _pending;
                _pending = null;
            }
        }

        Bitmap? image;
        lock (_frameGate)
        {
            image = _current;
        }

        if (image != null)
        {
            DrawImage(g, image, bounds);
            DrawOverlays(g);
        }
        else
        {
            DrawPlaceholder(g, bounds);
        }

        if (ShowOverlayHeader)
        {
            DrawHeader(g);
        }

        if (ShowBorder)
        {
            var border = IsSelected ? Theme.Accent : _hovered ? Theme.BorderStrong : Theme.Border;
            Theme.DrawRounded(g, bounds, Radius, border, IsSelected ? 2 : 1);
        }
    }

    /// <summary>Letterboxes the frame so it never stretches.</summary>
    private void DrawImage(Graphics g, Bitmap image, Rectangle bounds)
    {
        var scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));

        _videoBounds = new Rectangle(
            bounds.X + ((bounds.Width - width) / 2),
            bounds.Y + ((bounds.Height - height) / 2),
            width,
            height);

        var previous = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;

        using (var clip = Theme.RoundedRect(bounds, Radius))
        {
            g.SetClip(clip);
            g.DrawImage(image, _videoBounds);
            g.ResetClip();
        }

        g.InterpolationMode = previous;
    }

    private void DrawPlaceholder(Graphics g, Rectangle bounds)
    {
        var glyph = Status == CameraStatus.Connecting ? Icons.Refresh : Icons.Video;
        var color = Status == CameraStatus.Connecting ? Theme.Warning : Theme.TextMuted;

        var iconBounds = new Rectangle(0, (Height / 2) - 26, Width, 28);
        Theme.DrawText(g, glyph, Theme.IconFont(20f), color, iconBounds,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var label = Status switch
        {
            CameraStatus.Connecting => "Connecting...",
            CameraStatus.Error => "Connection Failed",
            _ => PlaceholderText
        };

        Theme.DrawText(g, label, Theme.Small, Theme.TextMuted,
            new Rectangle(0, (Height / 2) + 4, Width, 18),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Maps a frame-space box onto the letterboxed display rectangle.</summary>
    private RectangleF MapBox(Detection detection)
    {
        if (_frameWidth <= 0 || _frameHeight <= 0)
        {
            return RectangleF.Empty;
        }

        var scaleX = (float)_videoBounds.Width / _frameWidth;
        var scaleY = (float)_videoBounds.Height / _frameHeight;

        return new RectangleF(
            _videoBounds.X + (detection.X * scaleX),
            _videoBounds.Y + (detection.Y * scaleY),
            detection.Width * scaleX,
            detection.Height * scaleY);
    }

    private void DrawOverlays(Graphics g)
    {
        if (ShowDetections)
        {
            foreach (var detection in _detections)
            {
                DrawBox(g, MapBox(detection), detection.Caption, Theme.Accent);
            }
        }

        if (!ShowFaces)
        {
            return;
        }

        foreach (var face in _faces)
        {
            // Green for a known face, amber for one that did not match: an
            // operator can read the state without stopping to read the label.
            var color = face.IsRecognized
                ? (face.Record != null && face.Record.Group == FaceGroup.Blacklist ? Theme.Offline : Theme.Online)
                : Theme.Warning;

            var caption = face.IsRecognized
                ? face.DisplayName + " " + face.SimilarityText
                : "Unknown";

            DrawBox(g, MapBox(face.Box), caption, color);
        }
    }

    private static void DrawBox(Graphics g, RectangleF box, string caption, Color color)
    {
        if (box.Width < 2 || box.Height < 2)
        {
            return;
        }

        using (var pen = new Pen(color, 2f))
        {
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        }

        if (string.IsNullOrEmpty(caption))
        {
            return;
        }

        var font = Theme.Caption;
        var size = Theme.MeasureText(caption, font);
        var labelWidth = size.Width + 8;
        var labelHeight = size.Height + 2;

        // Keep the label inside the frame when the box touches the top edge.
        var labelY = box.Y - labelHeight;
        if (labelY < 0)
        {
            labelY = box.Y;
        }

        var label = new Rectangle((int)box.X, (int)labelY, labelWidth, labelHeight);

        using (var brush = new SolidBrush(color))
        {
            g.FillRectangle(brush, label);
        }

        Theme.DrawText(g, caption, font, Theme.TextOnAccent, label,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawHeader(Graphics g)
    {
        if (Camera == null)
        {
            return;
        }

        // A soft scrim keeps the caption readable over bright scenes.
        using (var brush = new LinearGradientBrush(
            new Rectangle(0, 0, Math.Max(1, Width), 28),
            Color.FromArgb(150, 0, 0, 0),
            Color.FromArgb(0, 0, 0, 0),
            LinearGradientMode.Vertical))
        {
            using var clip = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
            g.SetClip(clip);
            g.FillRectangle(brush, 0, 0, Width, 28);
            g.ResetClip();
        }

        Theme.DrawStatusDot(g, 9, 11, 7, Theme.StatusColor(Status));

        Theme.DrawText(g, Camera.DisplayName, Theme.SmallBold, Theme.TextPrimary,
            new Rectangle(22, 4, Width - 120, 18));

        var right = Width - 8;

        if (IsRecording)
        {
            const string rec = "REC";
            var recWidth = Theme.MeasureText(rec, Theme.Caption).Width;
            Theme.DrawStatusDot(g, right - recWidth - 12, 10, 7, Theme.Offline);
            Theme.DrawText(g, rec, Theme.Caption, Theme.Offline,
                new Rectangle(right - recWidth, 4, recWidth, 16));
            right -= recWidth + 20;
        }

        var clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var clockWidth = Theme.MeasureText(clock, Theme.Caption).Width;
        Theme.DrawText(g, clock, Theme.Caption, Theme.TextSecondary,
            new Rectangle(right - clockWidth, 4, clockWidth, 16));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_frameGate)
            {
                _current?.Dispose();
                _current = null;
                _pending?.Dispose();
                _pending = null;
            }
        }

        base.Dispose(disposing);
    }
}
