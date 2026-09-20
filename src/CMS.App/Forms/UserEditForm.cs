using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;

namespace CMS.App.Forms;

/// <summary>
/// Create an account or change an existing one. Passwords are hashed with
/// PBKDF2 before they reach the database and are never stored in clear text.
/// </summary>
public sealed class UserEditForm : Form
{
    private readonly TitleBar _titleBar = new TitleBar
    {
        BrandPrefix = "ADD ",
        BrandAccent = "USER",
        ShowMaximize = false,
        ShowMinimize = false
    };

    private readonly DarkTextBox _username = new DarkTextBox { Placeholder = "Username" };
    private readonly DarkTextBox _password = new DarkTextBox { UseSystemPasswordChar = true, Placeholder = "Password" };
    private readonly DarkTextBox _confirm = new DarkTextBox { UseSystemPasswordChar = true, Placeholder = "Confirm password" };
    private readonly DarkComboBox _role = new DarkComboBox();
    private readonly DarkCheckBox _enabled = new DarkCheckBox { Text = "Account is active", Checked = true };

    private readonly FlatButton _save = new FlatButton { Variant = ButtonVariant.Primary, Text = "Save", Size = new Size(104, 34) };
    private readonly FlatButton _cancel = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Size = new Size(104, 34) };
    private readonly DarkLabel _status = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };

    private readonly AppUser? _existing;

    public UserEditForm(AppUser? existing = null)
    {
        _existing = existing;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(430, 400);
        DoubleBuffered = true;
        KeyPreview = true;

        _titleBar.BrandPrefix = existing == null ? "ADD " : "EDIT ";

        _role.Items.AddRange(new object[] { "Viewer", "Operator", "Administrator" });
        _role.SelectedIndex = 0;

        _save.Click += OnSave;
        _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Controls.Add(_titleBar);
        Controls.AddRange(new Control[] { _username, _password, _confirm, _role, _enabled, _status, _save, _cancel });

        if (existing != null)
        {
            _username.Text = existing.Username;
            _role.SelectedIndex = (int)existing.Role;
            _enabled.Checked = existing.Enabled;
            _password.Placeholder = "Leave blank to keep the current password";
            _confirm.Placeholder = "Leave blank to keep the current password";
        }

        Resize += (s, e) => DoLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DoLayout();
        _username.Focus();
    }

    private void DoLayout()
    {
        const int left = 24;
        var width = Width - 48;
        var y = _titleBar.Bottom + 34;

        foreach (var field in new Control[] { _username, _password, _confirm, _role })
        {
            field.SetBounds(left, y, width, 32);
            y += 54;
        }

        _enabled.SetBounds(left, y, width, 22);
        _status.SetBounds(left, y + 30, width, 20);

        _cancel.SetBounds(Width - 24 - 104, Height - 54, 104, 34);
        _save.SetBounds(_cancel.Left - 112, Height - 54, 104, 34);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var username = _username.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Enter a username.", Theme.Warning);
            return;
        }

        var duplicate = Program.Services.Users.GetByUsername(username);

        if (duplicate != null && (_existing == null || duplicate.Id != _existing.Id))
        {
            SetStatus("That username is already taken.", Theme.Offline);
            return;
        }

        var changingPassword = !string.IsNullOrEmpty(_password.Text);

        if (_existing == null && !changingPassword)
        {
            SetStatus("Set a password for the new account.", Theme.Warning);
            return;
        }

        if (changingPassword)
        {
            if (_password.Text.Length < 6)
            {
                SetStatus("Use at least six characters.", Theme.Warning);
                return;
            }

            if (_password.Text != _confirm.Text)
            {
                SetStatus("The two passwords do not match.", Theme.Offline);
                return;
            }
        }

        var role = (UserRole)Math.Max(0, _role.SelectedIndex);

        if (_existing == null)
        {
            var user = new AppUser
            {
                Username = username,
                Role = role,
                Enabled = _enabled.Checked,
                Permissions = new HashSet<string>(Permissions.ForRole(role), StringComparer.OrdinalIgnoreCase)
            };

            Program.Services.Auth.SetPassword(user, _password.Text);
            Program.Services.Users.Insert(user);
            Program.Services.LogEvent(EventKind.System, "User account created: " + username);
        }
        else
        {
            // Demoting the last administrator would lock everyone out.
            if (_existing.Role == UserRole.Administrator && role != UserRole.Administrator)
            {
                var administrators = Program.Services.Users.GetAll()
                    .Count(u => u.Role == UserRole.Administrator && u.Enabled);

                if (administrators <= 1)
                {
                    SetStatus("This is the last administrator; its role cannot be changed.", Theme.Offline);
                    return;
                }
            }

            _existing.Username = username;
            _existing.Enabled = _enabled.Checked;

            if (_existing.Role != role)
            {
                _existing.Role = role;
                _existing.Permissions = new HashSet<string>(
                    Permissions.ForRole(role),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (changingPassword)
            {
                Program.Services.Auth.SetPassword(_existing, _password.Text);
            }

            Program.Services.Users.Update(_existing);
            Program.Services.LogEvent(EventKind.System, "User account updated: " + username);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
        _status.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        var labels = new[] { "Username", "Password", "Confirm Password", "Role" };
        var y = _titleBar.Bottom + 14;

        foreach (var label in labels)
        {
            Theme.DrawText(g, label, Theme.Small, Theme.TextSecondary,
                new Rectangle(24, y, Width - 48, 18));
            y += 54;
        }

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }
}
