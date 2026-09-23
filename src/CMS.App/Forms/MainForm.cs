using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Pages;
using CMS.Core.Models;

namespace CMS.App.Forms;

/// <summary>
/// The application shell: header, navigation rail and the page host. It owns the
/// page instances so navigating back to a screen keeps its state.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppHeader _header;
    private readonly SidebarNav _nav;
    private readonly Panel _host;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>(StringComparer.OrdinalIgnoreCase);

    private PageBase? _current;
    private int _unreadAlerts;

    public MainForm()
    {
        Text = "Camera Management System";

        // The header draws its own caption bar and window buttons, so the OS
        // chrome is removed; resizing is handled in WndProc below.
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1440, 900);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        DoubleBuffered = true;
        KeyPreview = true;

        _host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };

        _nav = new SidebarNav { Dock = DockStyle.Left, Width = Theme.SidebarWidth };
        _nav.ItemSelected += (s, item) => Navigate(item.Key);

        _header = new AppHeader();
        _header.BellClicked += (s, e) => { _unreadAlerts = 0; _header.AlertCount = 0; Navigate("events"); };
        _header.UserClicked += OnUserClicked;

        Controls.Add(_host);
        Controls.Add(_nav);
        Controls.Add(_header);

        BuildNavigation();

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (s, e) => _current?.OnTick();

        Services.EventLogged += OnEventLogged;
        Services.Analytics.EventRaised += OnEventLogged;

        Load += OnLoaded;
        FormClosing += OnClosing;
        KeyDown += OnKeyDown;
        Resize += (s, e) => _header.UpdateMaximizeGlyph();
    }

    private CMS.Core.Services.AppServices Services => Program.Services;

    private void BuildNavigation()
    {
        var user = Services.Auth.CurrentUser;
        _header.UserName = user?.Username ?? "user";

        var items = new List<NavItem>
        {
            new NavItem("dashboard", "Dashboard", Icons.Dashboard),
            new NavItem("live", "Live View", Icons.LiveView, Permissions.LiveView),
            new NavItem("playback", "Playback", Icons.Playback, Permissions.Playback),
            new NavItem("cameras", "Camera List", Icons.Grid, Permissions.LiveView),
            new NavItem("devices", "Device Management", Icons.Devices, Permissions.DeviceManagement),
            new NavItem("ptz", "PTZ Control", Icons.Ptz, Permissions.PtzControl),
            new NavItem("detection", "Object Detection", Icons.Detection, Permissions.ObjectDetection),
            new NavItem("faces", "Face Recognition", Icons.Face, Permissions.FaceRecognition),
            new NavItem("facedb", "Face Database", Icons.People, Permissions.FaceRecognition),
            new NavItem("attendance", "Attendance", Icons.People, Permissions.Attendance),
            new NavItem("events", "Event Log", Icons.EventLog),
            new NavItem("users", "User Management", Icons.Permissions, Permissions.UserManagement),
            new NavItem("system", "System Info", Icons.Info),
            new NavItem("settings", "Settings", Icons.Settings, Permissions.SystemSettings)
        };

        // Hide anything this account is not entitled to use.
        var visible = items
            .Where(i => i.Permission == null || Services.Auth.HasPermission(i.Permission))
            .ToList();

        _nav.SetItems(visible);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Navigate("dashboard");
        _tick.Start();

        // Bring the cameras up without blocking the first paint.
        Task.Run(async () =>
        {
            try
            {
                await Services.StartAllCamerasAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Services.LogEvent(EventKind.System, "Camera start-up: " + ex.Message, EventSeverity.Warning);
            }
        });
    }

    /// <summary>Shows a page, creating it the first time it is requested.</summary>
    public void Navigate(string key)
    {
        PageBase page;

        if (!_pages.TryGetValue(key, out page))
        {
            page = CreatePage(key);
            _pages[key] = page;
            page.Visible = false;
            _host.Controls.Add(page);
        }

        if (ReferenceEquals(_current, page))
        {
            return;
        }

        if (_current != null)
        {
            _current.OnDeactivated();
            _current.Visible = false;
        }

        _current = page;
        page.Visible = true;
        page.BringToFront();
        page.OnActivated();

        _nav.SelectKey(key, raiseEvent: false);
    }

    /// <summary>Navigates to Live View and focuses one camera.</summary>
    public void ShowCamera(CameraDevice camera)
    {
        Navigate("live");

        if (_current is LiveViewPage live)
        {
            live.FocusCamera(camera.Id);
        }
    }

    /// <summary>Navigates to PTZ Control for a specific camera.</summary>
    public void ShowPtz(CameraDevice camera)
    {
        Navigate("ptz");

        if (_current is PtzControlPage ptz)
        {
            ptz.SelectCamera(camera.Id);
        }
    }

    private PageBase CreatePage(string key) => key switch
    {
        "dashboard" => new DashboardPage(),
        "live" => new LiveViewPage(),
        "playback" => new PlaybackPage(),
        "cameras" => new CameraListPage(),
        "devices" => new DeviceManagementPage(),
        "ptz" => new PtzControlPage(),
        "detection" => new ObjectDetectionPage(),
        "faces" => new FaceRecognitionPage(),
        "facedb" => new FaceDatabasePage(),
        "attendance" => new AttendancePage(),
        "events" => new EventLogPage(),
        "users" => new UserManagementPage(),
        "system" => new SystemInfoPage(),
        "settings" => new SettingsPage(),
        _ => new DashboardPage()
    };

    private void OnEventLogged(object? sender, EventEntry entry)
    {
        if (entry.Severity == EventSeverity.Info)
        {
            return;
        }

        // Only warnings and alarms deserve the bell.
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                _unreadAlerts++;
                _header.AlertCount = _unreadAlerts;
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private void OnUserClicked(object? sender, EventArgs e)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Panel,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System
        };

        var user = Services.Auth.CurrentUser;

        var whoAmI = new ToolStripMenuItem(
            (user?.Username ?? "user") + "  -  " + (user?.RoleText ?? string.Empty))
        {
            Enabled = false,
            ForeColor = Theme.TextMuted
        };

        var systemInfo = new ToolStripMenuItem("System Information", null, (s, args) => Navigate("system"))
        {
            ForeColor = Theme.TextPrimary
        };

        var signOut = new ToolStripMenuItem("Sign Out", null, (s, args) => SignOut())
        {
            ForeColor = Theme.TextPrimary
        };

        var exit = new ToolStripMenuItem("Exit", null, (s, args) => Close())
        {
            ForeColor = Theme.TextPrimary
        };

        menu.Items.Add(whoAmI);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(systemInfo);
        menu.Items.Add(signOut);
        menu.Items.Add(exit);

        menu.Show(_header, new Point(_header.Width - 260, _header.Height - 4));
    }

    private void SignOut()
    {
        if (!Confirm("Sign out of the current session?"))
        {
            return;
        }

        Services.LogEvent(
            EventKind.System,
            "User " + (Services.Auth.CurrentUser?.Username ?? "unknown") + " signed out.");

        Services.Streams.StopAll();
        Services.Recording.StopAll();
        Services.Auth.Logout();

        // Restarting keeps the shell state clean for the next operator.
        Application.Restart();
        Environment.Exit(0);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F5 refreshes, Escape leaves full screen, F11 toggles it.
        if (e.KeyCode == Keys.F5)
        {
            _current?.OnActivated();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F11)
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !Confirm("Close Camera Management System?"))
        {
            e.Cancel = true;
            return;
        }

        _tick.Stop();

        Services.EventLogged -= OnEventLogged;
        Services.Analytics.EventRaised -= OnEventLogged;

        if (_current != null)
        {
            _current.OnDeactivated();
        }

        Services.Recording.StopAll();
        Services.Streams.StopAll();
    }

    private bool Confirm(string message)
        => MessageBox.Show(this, message, "Camera Management System",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    // ---- Borderless window resizing ----

    private const int WmNcHitTest = 0x0084;
    private const int ResizeBorder = 6;

    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    /// <summary>
    /// A borderless form has no resize frame, so the edges are reported back to
    /// Windows here. The window manager then does the resize itself, which keeps
    /// Aero Snap and multi-monitor behaviour working.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            var lparam = unchecked((int)(long)m.LParam);
            var screenPoint = new Point((short)(lparam & 0xFFFF), (short)((lparam >> 16) & 0xFFFF));
            var point = PointToClient(screenPoint);

            var onLeft = point.X <= ResizeBorder;
            var onRight = point.X >= ClientSize.Width - ResizeBorder;
            var onTop = point.Y <= ResizeBorder;
            var onBottom = point.Y >= ClientSize.Height - ResizeBorder;

            var hit = 0;

            if (onTop && onLeft) hit = HtTopLeft;
            else if (onTop && onRight) hit = HtTopRight;
            else if (onBottom && onLeft) hit = HtBottomLeft;
            else if (onBottom && onRight) hit = HtBottomRight;
            else if (onLeft) hit = HtLeft;
            else if (onRight) hit = HtRight;
            else if (onTop) hit = HtTop;
            else if (onBottom) hit = HtBottom;

            if (hit != 0)
            {
                m.Result = (IntPtr)hit;
                return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }
}
