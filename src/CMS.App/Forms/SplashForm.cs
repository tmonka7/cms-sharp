using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CMS.App.Forms;

/// <summary>
/// The start-up screen. It also does the real work of warming the system up:
/// loading the ONNX models and priming the camera list happen here, on a worker
/// thread, so the main window opens ready to use.
/// </summary>
public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _animation;
    private readonly BackgroundWorker _worker;

    private double _progress;
    private double _targetProgress;
    private string _status = "Initializing system...";
    private float _ringAngle;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Size = new Size(720, 460);
        DoubleBuffered = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _animation = new System.Windows.Forms.Timer { Interval = 33 };
        _animation.Tick += OnAnimationTick;

        _worker = new BackgroundWorker { WorkerReportsProgress = true };
        _worker.DoWork += OnWork;
        _worker.ProgressChanged += OnProgress;
        _worker.RunWorkerCompleted += OnCompleted;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _animation.Start();
        _worker.RunWorkerAsync();
    }

    /// <summary>
    /// The warm-up steps. Model loading is the slow one, and it is deliberately
    /// tolerant: a missing model file leaves the app usable.
    /// </summary>
    private void OnWork(object? sender, DoWorkEventArgs e)
    {
        var steps = new (int Percent, string Message, Action Work)[]
        {
            (15, "Opening local database...", () => Program.Services.Cameras.GetAll()),
            (35, "Loading detection model...", () => Program.Services.LoadModels()),
            (70, "Loading face database...", () => Program.Services.FaceRecognition.ReloadGallery()),
            (85, "Preparing video pipeline...", () => Thread.Sleep(150)),
            (100, "Ready", () => Thread.Sleep(120))
        };

        foreach (var step in steps)
        {
            _worker.ReportProgress(step.Percent, step.Message);

            try
            {
                step.Work();
            }
            catch (Exception ex)
            {
                // Report and continue: start-up must not be blocked by an
                // optional component such as a missing model file.
                _worker.ReportProgress(step.Percent, "Warning: " + ex.Message);
                Thread.Sleep(400);
            }

            Thread.Sleep(180);
        }
    }

    private void OnProgress(object? sender, ProgressChangedEventArgs e)
    {
        _targetProgress = e.ProgressPercentage;
        _status = e.UserState as string ?? _status;
    }

    private void OnCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        _targetProgress = 100;
        _status = "Ready";

        // Let the bar visibly reach the end before the window closes.
        var closeTimer = new System.Windows.Forms.Timer { Interval = 450 };
        closeTimer.Tick += (s, args) =>
        {
            closeTimer.Stop();
            closeTimer.Dispose();
            _animation.Stop();
            Close();
        };

        closeTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        // Ease toward the reported percentage so the bar never jumps.
        _progress += (_targetProgress - _progress) * 0.12;
        _ringAngle = (_ringAngle + 3f) % 360f;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        DrawGlow(g);
        DrawLens(g, new Point(Width / 2, 128), 62);
        DrawBranding(g);
        DrawFeatureChips(g);
        DrawProgress(g);

        Theme.DrawText(g, "v" + Program.Version, Theme.Caption, Theme.TextMuted,
            new Rectangle(0, Height - 26, Width, 16),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }

    /// <summary>The red halo behind the lens.</summary>
    private void DrawGlow(Graphics g)
    {
        var center = new Point(Width / 2, 128);
        var radius = 220;
        var bounds = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using var path = new GraphicsPath();
        path.AddEllipse(bounds);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(70, Theme.Accent),
            SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
            CenterPoint = center
        };

        g.FillEllipse(brush, bounds);
    }

    /// <summary>A stylised camera lens: concentric rings plus a rotating arc.</summary>
    private void DrawLens(Graphics g, Point center, int radius)
    {
        var outer = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using (var brush = new LinearGradientBrush(outer, Color.FromArgb(0x1A, 0x1F, 0x29), Color.Black, 60f))
        {
            g.FillEllipse(brush, outer);
        }

        using (var pen = new Pen(Theme.Accent, 3f))
        {
            g.DrawEllipse(pen, outer);
        }

        // The sweeping arc reads as activity while the app loads.
        using (var pen = new Pen(Theme.AccentHover, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var arc = new Rectangle(outer.X - 10, outer.Y - 10, outer.Width + 20, outer.Height + 20);
            g.DrawArc(pen, arc, _ringAngle, 70);
        }

        for (var i = 1; i <= 3; i++)
        {
            var inset = i * 11;
            var ring = new Rectangle(outer.X + inset, outer.Y + inset, outer.Width - (inset * 2), outer.Height - (inset * 2));

            using var pen = new Pen(Color.FromArgb(120 - (i * 25), Theme.BorderStrong));
            g.DrawEllipse(pen, ring);
        }

        var pupilRadius = radius / 3;
        var pupil = new Rectangle(center.X - pupilRadius, center.Y - pupilRadius, pupilRadius * 2, pupilRadius * 2);

        using (var brush = new SolidBrush(Color.FromArgb(0x05, 0x07, 0x0A)))
        {
            g.FillEllipse(brush, pupil);
        }

        using (var pen = new Pen(Color.FromArgb(160, Theme.Accent), 2f))
        {
            g.DrawEllipse(pen, pupil);
        }

        // A small specular highlight to sell the glass.
        using (var brush = new SolidBrush(Color.FromArgb(60, Color.White)))
        {
            g.FillEllipse(brush, center.X - (radius / 2), center.Y - (radius / 2), radius / 3, radius / 4);
        }
    }

    private void DrawBranding(Graphics g)
    {
        const string prefix = "CAMERA MANAGEMENT ";
        const string accent = "SYSTEM";

        var prefixSize = Theme.MeasureText(prefix, Theme.Display);
        var accentSize = Theme.MeasureText(accent, Theme.Display);
        var startX = (Width - prefixSize.Width - accentSize.Width) / 2;
        var y = 224;

        Theme.DrawText(g, prefix, Theme.Display, Theme.TextPrimary,
            new Rectangle(startX, y, prefixSize.Width + 4, 40));

        Theme.DrawText(g, accent, Theme.Display, Theme.Accent,
            new Rectangle(startX + prefixSize.Width, y, accentSize.Width + 4, 40));

        Theme.DrawText(g, "AI Powered    •    Face Recognition    •    Object Detection",
            Theme.Small, Theme.TextSecondary,
            new Rectangle(0, y + 42, Width, 18),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawFeatureChips(Graphics g)
    {
        var chips = new[]
        {
            (Icons.Detection, "YOLOv26n", "Object Detection"),
            (Icons.Face, "Face Recognition", "Identity Matching"),
            (Icons.Devices, "ONVIF", "Camera Management")
        };

        const int chipWidth = 176;
        const int chipHeight = 48;
        const int gap = 14;

        var totalWidth = (chipWidth * chips.Length) + (gap * (chips.Length - 1));
        var x = (Width - totalWidth) / 2;
        var y = 306;

        foreach (var chip in chips)
        {
            var bounds = new Rectangle(x, y, chipWidth, chipHeight);
            Theme.DrawCard(g, bounds, Theme.Card, Theme.Border);

            Theme.DrawText(g, chip.Item1, Theme.IconFont(13f), Theme.Accent,
                new Rectangle(bounds.X + 12, bounds.Y, 22, bounds.Height));

            Theme.DrawText(g, chip.Item2, Theme.SmallBold, Theme.TextPrimary,
                new Rectangle(bounds.X + 40, bounds.Y + 9, bounds.Width - 48, 16));

            Theme.DrawText(g, chip.Item3, Theme.Caption, Theme.TextMuted,
                new Rectangle(bounds.X + 40, bounds.Y + 25, bounds.Width - 48, 14));

            x += chipWidth + gap;
        }
    }

    private void DrawProgress(Graphics g)
    {
        var track = new Rectangle((Width - 420) / 2, 384, 420, 5);
        Theme.FillRounded(g, track, 3, Theme.Input);

        var fill = (int)(track.Width * MathEx.Clamp(_progress, 0, 100) / 100.0);
        if (fill > 0)
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(track.X, track.Y, Math.Max(2, fill), track.Height),
                Theme.AccentHover,
                Theme.AccentDeep,
                LinearGradientMode.Horizontal);

            using var path = Theme.RoundedRect(new Rectangle(track.X, track.Y, Math.Max(4, fill), track.Height), 3);
            g.FillPath(brush, path);
        }

        Theme.DrawText(g, _status, Theme.Small, Theme.TextSecondary,
            new Rectangle(0, track.Bottom + 8, Width, 18),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Stop();
            _animation.Dispose();
            _worker.Dispose();
        }

        base.Dispose(disposing);
    }
}
