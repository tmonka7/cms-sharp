using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Models;

namespace CMS.App.Pages;

/// <summary>
/// Local accounts and what each one may do. The permission checklist on the
/// right applies to the selected user and saves in place.
/// </summary>
public sealed class UserManagementPage : PageBase
{
    private readonly CardPanel _usersCard = new CardPanel();
    private readonly CardPanel _permissionsCard = new CardPanel();
    private readonly DataTable _table = new DataTable { Dock = DockStyle.Fill };

    private readonly FlatButton _add = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Add,
        Text = "Add User",
        Size = new Size(110, 32)
    };

    private readonly FlatButton _save = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Text = "Save",
        Size = new Size(104, 32)
    };

    private readonly List<DarkCheckBox> _permissionBoxes = new List<DarkCheckBox>();
    private readonly DarkLabel _selectedUser = new DarkLabel { Font = Theme.MediumBold, ForeColor = Theme.TextPrimary };
    private readonly DarkLabel _hint = new DarkLabel { Font = Theme.Caption, ForeColor = Theme.TextMuted };

    private AppUser? _selected;
    private bool _loading;

    public UserManagementPage()
    {
        PageTitle = "User Management";

        _usersCard.Title = "User List";
        _usersCard.TitleIcon = Icons.Permissions;
        _usersCard.Controls.Add(_table);

        _permissionsCard.Title = "Permissions";
        _permissionsCard.TitleIcon = Icons.Lock;
        _permissionsCard.Controls.Add(_selectedUser);
        _permissionsCard.Controls.Add(_hint);
        _permissionsCard.Controls.Add(_save);

        foreach (var permission in Permissions.All)
        {
            var box = new DarkCheckBox { Text = permission.Value, Tag = permission.Key };
            _permissionBoxes.Add(box);
            _permissionsCard.Controls.Add(box);
        }

        BuildColumns();

        _add.Click += (s, e) => AddUser();
        _save.Click += (s, e) => SavePermissions();
        _table.SelectionChanged += (s, e) => OnUserSelected();

        Controls.Add(_usersCard);
        Controls.Add(_permissionsCard);

        PlaceTitleBarControls(_add);
    }

    private void BuildColumns()
    {
        _table.EmptyText = "No accounts";
        _table.RowHeight = 44;

        _table.AddColumn(new TableColumn("No.", 44, item => ((AppUser)item).Id.ToString())
        {
            Color = _ => Theme.TextMuted
        });

        _table.AddColumn(new TableColumn("Username", 150, item => ((AppUser)item).Username)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.4f,
            Font = Theme.BodyBold
        });

        _table.AddColumn(new TableColumn("Role", 130, item => ((AppUser)item).RoleText)
        {
            Pill = true,
            Color = item => ((AppUser)item).Role switch
            {
                UserRole.Administrator => Theme.Accent,
                UserRole.Operator => Theme.Info,
                _ => Theme.TextMuted
            }
        });

        _table.AddColumn(new TableColumn("Last Login", 150, item =>
        {
            var user = (AppUser)item;
            return user.LastLoginUtc.HasValue
                ? user.LastLoginUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "Never";
        })
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.1f,
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Status", 90, item => ((AppUser)item).StatusText)
        {
            Dot = item => ((AppUser)item).Enabled ? Theme.Online : Theme.Offline,
            Color = item => ((AppUser)item).Enabled ? Theme.Online : Theme.Offline
        });

        _table.AddAction(new TableAction(Icons.Edit, "Edit", item => EditUser((AppUser)item)));

        _table.AddAction(new TableAction(Icons.Delete, "Delete", item => DeleteUser((AppUser)item))
        {
            HoverColor = Theme.Offline,
            // The signed-in account must never delete itself out of the system.
            IsVisible = item => ((AppUser)item).Id != Services.Auth.CurrentUser?.Id
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        if (area.Width <= 0)
        {
            return;
        }

        const int gap = 12;
        var rightWidth = Math.Min(300, Math.Max(240, area.Width / 4));

        _usersCard.SetBounds(area.X, area.Y, area.Width - rightWidth - gap, area.Height);
        _permissionsCard.SetBounds(area.Right - rightWidth, area.Y, rightWidth, area.Height);

        LayoutPermissions();
        PlaceTitleBarControls(_add);
    }

    private void LayoutPermissions()
    {
        var content = _permissionsCard.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var y = content.Y;

        _selectedUser.SetBounds(content.X, y, content.Width, 22);
        y += 28;

        foreach (var box in _permissionBoxes)
        {
            box.SetBounds(content.X, y, content.Width, 24);
            y += 28;
        }

        y += 8;
        _hint.SetBounds(content.X, y, content.Width, 32);

        _save.SetBounds(content.X, content.Bottom - 34, content.Width, 32);
    }

    public override void OnActivated() => Reload();

    private void Reload()
    {
        var users = Services.Users.GetAll();
        _table.SetRows(users.Cast<object>());

        _usersCard.TitleSuffix = users.Count + " accounts";
        _usersCard.Invalidate();

        OnUserSelected();
    }

    private void OnUserSelected()
    {
        _selected = _table.SelectedItem as AppUser;
        _loading = true;

        if (_selected == null)
        {
            _selectedUser.Text = "Select an account";
            _hint.Text = string.Empty;

            foreach (var box in _permissionBoxes)
            {
                box.Checked = false;
                box.Enabled = false;
            }

            _save.Enabled = false;
            _loading = false;
            Invalidate();
            return;
        }

        _selectedUser.Text = _selected.Username;

        var isAdministrator = _selected.Role == UserRole.Administrator;

        foreach (var box in _permissionBoxes)
        {
            var key = (string)box.Tag!;
            box.Checked = isAdministrator || _selected.Permissions.Contains(key);

            // An administrator implicitly holds everything, so the checklist is
            // shown but locked rather than silently editable and ignored.
            box.Enabled = !isAdministrator;
        }

        _hint.Text = isAdministrator
            ? "Administrators always hold every permission."
            : "Changes apply the next time this user signs in.";

        _save.Enabled = !isAdministrator;
        _loading = false;

        _selectedUser.Invalidate();
        _hint.Invalidate();
    }

    private void SavePermissions()
    {
        if (_selected == null || _loading)
        {
            return;
        }

        _selected.Permissions = new HashSet<string>(
            _permissionBoxes.Where(b => b.Checked).Select(b => (string)b.Tag!),
            StringComparer.OrdinalIgnoreCase);

        Services.Users.Update(_selected);
        Services.LogEvent(EventKind.System, "Permissions updated for " + _selected.Username);

        MessageBox.Show(this, "Permissions saved for " + _selected.Username + ".",
            "User Management", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AddUser()
    {
        using var dialog = new UserEditForm();

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Reload();
        }
    }

    private void EditUser(AppUser user)
    {
        using var dialog = new UserEditForm(user);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Reload();
        }
    }

    private void DeleteUser(AppUser user)
    {
        if (user.Id == Services.Auth.CurrentUser?.Id)
        {
            ShowError("You cannot delete the account you are signed in with.");
            return;
        }

        var administrators = Services.Users.GetAll().Count(u => u.Role == UserRole.Administrator && u.Enabled);

        if (user.Role == UserRole.Administrator && administrators <= 1)
        {
            ShowError("This is the last active administrator. Create another one before removing it.");
            return;
        }

        if (!Confirm("Delete the account " + user.Username + "?", "Delete User"))
        {
            return;
        }

        Services.Users.Delete(user.Id);
        Services.LogEvent(EventKind.System, "User account removed: " + user.Username);

        Reload();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!_permissionsCard.Visible || _permissionBoxes.Count == 0)
        {
            return;
        }

        var origin = _permissionsCard.Location;
        var content = _permissionsCard.ContentBounds;

        Theme.DrawText(e.Graphics, "Allowed screens", Theme.Caption, Theme.TextMuted,
            new Rectangle(origin.X + content.X, origin.Y + _permissionBoxes[0].Top - 18, content.Width, 16));
    }
}
