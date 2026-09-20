using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CMS.App.Controls;

namespace CMS.App.Forms;

/// <summary>
/// Sign-in against the local account store. Credentials are verified in-process
/// against PBKDF2 hashes; nothing is sent anywhere.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly DarkTextBox _username;
    private readonly DarkTextBox _password;
    private readonly DarkCheckBox _remember;
    private readonly FlatButton _login;
    private readonly FlatButton _close;
    private readonly DarkLabel _error;
    private readonly LinkLabel _forgot;

    private const string RememberKey = "login.rememberedUser";

    public LoginForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Size = new Size(430, 520);
        DoubleBuffered = true;
        KeyPreview = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _close = new FlatButton
        {
            Variant = ButtonVariant.Icon,
            Icon = Icons.Close,
            IconSize = 9f,
            Size = new Size(30, 28),
            Location = new Point(Width - 40, 12),
            TabStop = false
        };
        _close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _username = new DarkTextBox
        {
            Icon = Icons.User,
            Placeholder = "Username",
            Bounds = new Rectangle(48, 276, Width - 96, 40)
        };

        _password = new DarkTextBox
        {
            Icon = Icons.Lock,
            Placeholder = "Password",
            UseSystemPasswordChar = true,
            Bounds = new Rectangle(48, 326, Width - 96, 40)
        };

        // Reveal toggle, positioned inside the password field.
        var reveal = new FlatButton
        {
            Variant = ButtonVariant.Icon,
            Icon = Icons.RedEye,
            IconSize = 10f,
            Size = new Size(28, 28),
            Location = new Point(_password.Right - 34, _password.Top + 6),
            TabStop = false
        };
        reveal.Click += (s, e) => _password.UseSystemPasswordChar = !_password.UseSystemPasswordChar;

        _remember = new DarkCheckBox
        {
            Text = "Remember me",
            Bounds = new Rectangle(50, 376, 140, 22)
        };

        _error = new DarkLabel
        {
            ForeColor = Theme.Offline,
            Font = Theme.Small,
            Alignment = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(48, 400, Width - 96, 18),
            Visible = false
        };

        _login = new FlatButton
        {
            Variant = ButtonVariant.Primary,
            Text = "Login",
            Bounds = new Rectangle(48, 424, Width - 96, 40)
        };
        _login.Click += OnLoginClicked;

        _forgot = new LinkLabel
        {
            Text = "Forgot Password?",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            LinkColor = Theme.TextMuted,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.TextMuted,
            LinkBehavior = LinkBehavior.NeverUnderline,
            Font = Theme.Small,
            Bounds = new Rectangle(48, 472, Width - 96, 20)
        };
        _forgot.LinkClicked += OnForgotClicked;

        Controls.AddRange(new Control[]
        {
            _close, _username, _password, reveal, _remember, _error, _login, _forgot
        });

        AcceptButton = null;
        KeyDown += OnKeyDown;
        _password.Input.KeyDown += OnKeyDown;
        _username.Input.KeyDown += OnKeyDown;

        RestoreRememberedUser();
    }

    private void RestoreRememberedUser()
    {
        var remembered = Program.Services.SettingsStore.GetValue(RememberKey);

        if (!string.IsNullOrEmpty(remembered))
        {
            _username.Text = remembered!;
            _remember.Checked = true;
        }
        else
        {
            _username.Text = CMS.Core.Services.AuthService.DefaultUsername;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (string.IsNullOrEmpty(_username.Text))
        {
            _username.Focus();
        }
        else
        {
            _password.Focus();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OnLoginClicked(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void OnLoginClicked(object? sender, EventArgs e)
    {
        _error.Visible = false;
        _login.Enabled = false;

        try
        {
            var result = Program.Services.Auth.Login(_username.Text, _password.Text);

            if (!result.Success)
            {
                ShowError(result.Error ?? "Sign-in failed.");
                _password.Text = string.Empty;
                _password.Focus();
                return;
            }

            // Only the username is stored, never the password.
            Program.Services.SettingsStore.SetValue(
                RememberKey,
                _remember.Checked ? _username.Text.Trim() : string.Empty);

            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            _login.Enabled = true;
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    private void OnForgotClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        MessageBox.Show(
            this,
            "This system stores accounts locally and cannot email a reset link.\n\n" +
            "Ask an administrator to set a new password from User Management.",
            "Forgot Password",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        DrawGlow(g, new Point(Width / 2, 120), 190);
        DrawLens(g, new Point(Width / 2, 108), 42);

        const string prefix = "CAMERA MANAGEMENT ";
        const string accent = "SYSTEM";

        var prefixSize = Theme.MeasureText(prefix, Theme.Title);
        var accentSize = Theme.MeasureText(accent, Theme.Title);
        var startX = (Width - prefixSize.Width - accentSize.Width) / 2;

        Theme.DrawText(g, prefix, Theme.Title, Theme.TextPrimary,
            new Rectangle(startX, 176, prefixSize.Width + 4, 24));

        Theme.DrawText(g, accent, Theme.Title, Theme.Accent,
            new Rectangle(startX + prefixSize.Width, 176, accentSize.Width + 4, 24));

        Theme.DrawText(g, "Secure Access", Theme.Small, Theme.TextMuted,
            new Rectangle(0, 204, Width, 18),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Shield mark beside the credential block.
        Theme.DrawText(g, Icons.Shield, Theme.IconFont(15f), Color.FromArgb(150, Theme.Accent),
            new Rectangle(Width - 44, 240, 28, 24),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }

    private static void DrawGlow(Graphics g, Point center, int radius)
    {
        var bounds = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using var path = new GraphicsPath();
        path.AddEllipse(bounds);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(56, Theme.Accent),
            SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
            CenterPoint = center
        };

        g.FillEllipse(brush, bounds);
    }

    private static void DrawLens(Graphics g, Point center, int radius)
    {
        var outer = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using (var brush = new LinearGradientBrush(outer, Color.FromArgb(0x1A, 0x1F, 0x29), Color.Black, 60f))
        {
            g.FillEllipse(brush, outer);
        }

        using (var pen = new Pen(Theme.Accent, 2.5f))
        {
            g.DrawEllipse(pen, outer);
        }

        var inset = 10;
        var ring = new Rectangle(outer.X + inset, outer.Y + inset, outer.Width - (inset * 2), outer.Height - (inset * 2));

        using (var pen = new Pen(Color.FromArgb(90, Theme.BorderStrong)))
        {
            g.DrawEllipse(pen, ring);
        }

        var pupilRadius = radius / 3;
        var pupil = new Rectangle(center.X - pupilRadius, center.Y - pupilRadius, pupilRadius * 2, pupilRadius * 2);

        using (var brush = new SolidBrush(Color.FromArgb(0x05, 0x07, 0x0A)))
        {
            g.FillEllipse(brush, pupil);
        }

        using (var pen = new Pen(Color.FromArgb(160, Theme.Accent), 1.5f))
        {
            g.DrawEllipse(pen, pupil);
        }
    }

    /// <summary>Lets the borderless login window be dragged by its background.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            NativeMethods.DragWindow(Handle);
        }

        base.OnMouseDown(e);
    }
}
