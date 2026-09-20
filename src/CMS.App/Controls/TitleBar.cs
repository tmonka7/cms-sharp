using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CMS.App.Controls;

/// <summary>
/// Custom window chrome. The app uses borderless forms so the header can carry
/// the product mark and the dark palette, so drag, double-click-to-maximise and
/// the caption buttons are implemented here.
/// </summary>
public class TitleBar : Control
{
    private const int HtCaption = 0x2;
    private const int WmNcLButtonDown = 0xA1;

    private readonly FlatButton _minimize;
    private readonly FlatButton _maximize;
    private readonly FlatButton _close;

    private Form? _host;

    public TitleBar()
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
        Height = Theme.TitleBarHeight;
        Dock = DockStyle.Top;

        _minimize = MakeCaptionButton(Icons.Minimize);
        _maximize = MakeCaptionButton(Icons.Maximize);
        _close = MakeCaptionButton(Icons.Close);
        _close.AccentOverride = Theme.Offline;

        _minimize.Click += (s, e) => SetState(FormWindowState.Minimized);
        _maximize.Click += (s, e) => ToggleMaximize();
        _close.Click += (s, e) => _host?.Close();

        Controls.Add(_minimize);
        Controls.Add(_maximize);
        Controls.Add(_close);
    }

    /// <summary>Product name drawn in bold before the accented word.</summary>
    public string BrandPrefix { get; set; } = "CAMERA MANAGEMENT ";

    /// <summary>Trailing word drawn in the accent colour.</summary>
    public string BrandAccent { get; set; } = "SYSTEM";

    public bool ShowMaximize { get; set; } = true;

    public bool ShowMinimize { get; set; } = true;

    /// <summary>Extra text drawn after the brand, e.g. a page name.</summary>
    public string Subtitle { get; set; } = string.Empty;

    private FlatButton MakeCaptionButton(string glyph) => new FlatButton
    {
        Variant = ButtonVariant.Icon,
        Icon = glyph,
        IconSize = 8f,
        Size = new Size(42, Theme.TitleBarHeight - 6),
        Radius = 4,
        TabStop = false
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        _host = FindForm();
        UpdateMaximizeGlyph();
        base.OnHandleCreated(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        LayoutButtons();
        base.OnSizeChanged(e);
    }

    private void LayoutButtons()
    {
        var y = 3;
        var x = Width - 46;

        _close.SetBounds(x, y, 42, Height - 6);
        x -= 44;

        _maximize.Visible = ShowMaximize;
        if (ShowMaximize)
        {
            _maximize.SetBounds(x, y, 42, Height - 6);
            x -= 44;
        }

        _minimize.Visible = ShowMinimize;
        if (ShowMinimize)
        {
            _minimize.SetBounds(x, y, 42, Height - 6);
        }
    }

    private void SetState(FormWindowState state)
    {
        if (_host != null)
        {
            _host.WindowState = state;
        }
    }

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

    /// <summary>Call after the host window state changes.</summary>
    public void UpdateMaximizeGlyph()
    {
        _maximize.Icon = _host != null && _host.WindowState == FormWindowState.Maximized
            ? Icons.Restore
            : Icons.Maximize;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Hand the drag to the window manager so snapping and Aero work.
        if (e.Button == MouseButtons.Left && _host != null)
        {
            ReleaseCapture();
            SendMessage(_host.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ShowMaximize)
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

        var x = 14;

        Theme.DrawText(g, Icons.Camera, Theme.IconFont(12f), Theme.Accent,
            new Rectangle(x, 0, 20, Height));
        x += 26;

        var prefixWidth = Theme.MeasureText(BrandPrefix, Theme.SmallBold).Width;
        Theme.DrawText(g, BrandPrefix, Theme.SmallBold, Theme.TextPrimary,
            new Rectangle(x, 0, prefixWidth + 4, Height));
        x += prefixWidth;

        var accentWidth = Theme.MeasureText(BrandAccent, Theme.SmallBold).Width;
        Theme.DrawText(g, BrandAccent, Theme.SmallBold, Theme.Accent,
            new Rectangle(x, 0, accentWidth + 4, Height));
        x += accentWidth + 14;

        if (!string.IsNullOrEmpty(Subtitle))
        {
            Theme.DrawText(g, Subtitle, Theme.Small, Theme.TextMuted,
                new Rectangle(x, 0, Math.Max(0, Width - x - 160), Height));
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
