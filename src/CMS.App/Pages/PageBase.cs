using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Services;

namespace CMS.App.Pages;

/// <summary>
/// Shared plumbing for every screen: the page title row, access to the service
/// container, and activate/deactivate hooks so a page can start and stop work
/// when the operator navigates to and away from it.
/// </summary>
public class PageBase : UserControl
{
    private string _pageTitle = string.Empty;
    private string _pageSubtitle = string.Empty;

    public PageBase()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Dock = DockStyle.Fill;
        Padding = new Padding(Theme.PagePadding);
    }

    protected AppServices Services => Program.Services;

    /// <summary>Height of the title row; zero hides it.</summary>
    public int TitleHeight { get; set; } = 38;

    public string PageTitle
    {
        get => _pageTitle;
        set
        {
            _pageTitle = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Muted text after the title, e.g. the selected channel.</summary>
    public string PageSubtitle
    {
        get => _pageSubtitle;
        set
        {
            _pageSubtitle = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Controls placed in the title row, right aligned.</summary>
    protected void PlaceTitleBarControls(params Control[] controls)
    {
        var right = Width - Padding.Right;

        for (var i = controls.Length - 1; i >= 0; i--)
        {
            var control = controls[i];
            control.Location = new Point(right - control.Width, Padding.Top + ((TitleHeight - 8 - control.Height) / 2));
            right -= control.Width + 8;

            if (!Controls.Contains(control))
            {
                Controls.Add(control);
            }

            control.BringToFront();
        }
    }

    /// <summary>The area below the title row that page content should fill.</summary>
    protected Rectangle ContentArea => new Rectangle(
        Padding.Left,
        Padding.Top + (string.IsNullOrEmpty(_pageTitle) ? 0 : TitleHeight),
        Math.Max(0, Width - Padding.Horizontal),
        Math.Max(0, Height - Padding.Vertical - (string.IsNullOrEmpty(_pageTitle) ? 0 : TitleHeight)));

    /// <summary>Called when the page becomes visible in the shell.</summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>Called when the operator navigates away.</summary>
    public virtual void OnDeactivated()
    {
    }

    /// <summary>Called on a one-second shell timer while the page is active.</summary>
    public virtual void OnTick()
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(BackColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        if (string.IsNullOrEmpty(_pageTitle))
        {
            base.OnPaint(e);
            return;
        }

        var titleSize = Theme.MeasureText(_pageTitle, Theme.Title);

        Theme.DrawText(g, _pageTitle, Theme.Title, Theme.TextPrimary,
            new Rectangle(Padding.Left, Padding.Top, titleSize.Width + 6, TitleHeight - 8));

        if (!string.IsNullOrEmpty(_pageSubtitle))
        {
            Theme.DrawText(g, _pageSubtitle, Theme.Small, Theme.TextMuted,
                new Rectangle(Padding.Left + titleSize.Width + 12, Padding.Top,
                    Math.Max(0, Width - Padding.Horizontal - titleSize.Width - 12), TitleHeight - 8));
        }

        base.OnPaint(e);
    }

    /// <summary>Marshals an action onto the UI thread, ignoring a closed page.</summary>
    protected void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The page was closed while a background result was arriving.
        }
    }

    /// <summary>Standard error presentation for page actions.</summary>
    protected void ShowError(string message, string caption = "Camera Management System")
        => MessageBox.Show(this, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    protected bool Confirm(string message, string caption = "Confirm")
        => MessageBox.Show(this, message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
