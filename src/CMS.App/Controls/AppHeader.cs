using System.Drawing;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// The shell header: product mark on the left, then the signed-in user, the
/// notification bell and the clock, then the window buttons. It doubles as the
/// caption bar, since the window is borderless.
/// </summary>
public class AppHeader : Control
{
    private readonly FlatButton _minimize;
    private readonly FlatButton _maximize;
    private readonly FlatButton _close;
    private readonly FlatButton _bell;
    private readonly FlatButton _user;
    private readonly System.Windows.Forms.Timer _clock;

    private Form? _host;
    private int _alertCount;

    public AppHeader()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Shell;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;

        // Every child must exist before anything resizes this control: setting
        // Height raises OnSizeChanged, which lays them out.
        _minimize = CaptionButton(Icons.Minimize);
        _maximize = CaptionButton(Icons.Maximize);
        _close = CaptionButton(Icons.Close);
        _close.AccentOverride = Theme.Offline;

        _minimize.Click += (s, e) => { if (_host != null) _host.WindowState = FormWindowState.Minimized; };
        _maximize.Click += (s, e) => ToggleMaximize();
        _close.Click += (s, e) => _host?.Close();

        _bell = new FlatButton
        {
            Variant = ButtonVariant.Icon,
            Icon = Icons.Bell,
            IconSize = 11f,
            Size = new Size(32, 32),
            TabStop = false
        };
        _bell.Click += (s, e) => BellClicked?.Invoke(this, EventArgs.Empty);

        _user = new FlatButton
        {
            Variant = ButtonVariant.Ghost,
            Icon = Icons.User,
            IconSize = 10f,
            Text = "admin",
            Size = new Size(96, 32),
            TabStop = false
        };
        _user.Click += (s, e) => UserClicked?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { _minimize, _maximize, _close, _bell, _user });

        Height = Theme.HeaderHeight;
        Dock = DockStyle.Top;

        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (s, e) => Invalidate(new Rectangle(Width - 420, 0, 260, Height));
        _clock.Start();
    }

    public string BrandPrefix { get; set; } = "CAMERA MANAGEMENT ";

    public string BrandAccent { get; set; } = "SYSTEM";

    public event EventHandler? BellClicked;

    public event EventHandler? UserClicked;

    public string UserName
    {
        get => _user.Text;
        set
        {
            _user.Text = value;
            _user.Width = Math.Max(90, Theme.MeasureText(value, Theme.Body).Width + 44);
            LayoutChildren();
        }
    }

    /// <summary>Unacknowledged alerts, drawn as a red dot on the bell.</summary>
    public int AlertCount
    {
        get => _alertCount;
        set
        {
            if (_alertCount == value)
            {
                return;
            }

            _alertCount = Math.Max(0, value);
            Invalidate();
        }
    }

    private static FlatButton CaptionButton(string glyph) => new FlatButton
    {
        Variant = ButtonVariant.Icon,
        Icon = glyph,
        IconSize = 8f,
        Size = new Size(42, 30),
        Radius = 4,
        TabStop = false
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        _host = FindForm();
        UpdateMaximizeGlyph();
        LayoutChildren();
        base.OnHandleCreated(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        LayoutChildren();
        base.OnSizeChanged(e);
    }

    private void LayoutChildren()
    {
        var y = (Height - 30) / 2;
        var x = Width - 46;

        _close.SetBounds(x, y, 42, 30);
        x -= 44;
        _maximize.SetBounds(x, y, 42, 30);
        x -= 44;
        _minimize.SetBounds(x, y, 42, 30);

        // Clock text is painted, so reserve room for it before the widgets.
        x -= 170;

        _bell.SetBounds(x, (Height - 32) / 2, 32, 32);
        x -= _user.Width + 6;
        _user.SetBounds(x, (Height - 32) / 2, _user.Width, 32);
    }

    public void UpdateMaximizeGlyph()
        => _maximize.Icon = _host != null && _host.WindowState == FormWindowState.Maximized
            ? Icons.Restore
            : Icons.Maximize;

    private void ToggleMaximize()
    {
        if (_host == null)
        {
            return;
        }

        _host.WindowState = _host.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

        UpdateMaximizeGlyph();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _host != null)
        {
            NativeMethods.DragWindow(_host.Handle);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleMaximize();
        }

        base.OnMouseDoubleClick(e);
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
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        var x = 16;

        Theme.DrawText(g, Icons.Camera, Theme.IconFont(15f), Theme.Accent,
            new Rectangle(x, 0, 24, Height));
        x += 30;

        var prefixWidth = Theme.MeasureText(BrandPrefix, Theme.MediumBold).Width;
        Theme.DrawText(g, BrandPrefix, Theme.MediumBold, Theme.TextPrimary,
            new Rectangle(x, 0, prefixWidth + 4, Height));
        x += prefixWidth;

        var accentWidth = Theme.MeasureText(BrandAccent, Theme.MediumBold).Width;
        Theme.DrawText(g, BrandAccent, Theme.MediumBold, Theme.Accent,
            new Rectangle(x, 0, accentWidth + 4, Height));

        // A red dot on the bell is the standard unacknowledged-alert cue.
        if (_alertCount > 0)
        {
            Theme.DrawStatusDot(g, _bell.Right - 11, _bell.Top + 6, 8, Theme.Accent);
        }

        var clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Theme.DrawText(g, clock, Theme.Small, Theme.TextSecondary,
            new Rectangle(_minimize.Left - 168, 0, 160, Height),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Stop();
            _clock.Dispose();
        }

        base.Dispose(disposing);
    }
}
