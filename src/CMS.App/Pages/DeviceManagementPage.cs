using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Models;

namespace CMS.App.Pages;

/// <summary>
/// The camera inventory: add, edit, remove and test devices. Adding a camera is
/// the one place ONVIF discovery is offered.
/// </summary>
public sealed class DeviceManagementPage : PageBase
{
    private readonly CardPanel _card = new CardPanel();
    private readonly DataTable _table = new DataTable { Dock = DockStyle.Fill };
    private readonly FlatButton _add = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Add,
        Text = "Add Device",
        Size = new Size(126, 32)
    };

    private readonly FlatButton _scan = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Icon = Icons.Search,
        Text = "Scan Network",
        Size = new Size(134, 32)
    };

    private readonly FlatButton _refresh = new FlatButton
    {
        Variant = ButtonVariant.Icon,
        Icon = Icons.Refresh,
        Size = new Size(32, 32)
    };

    public DeviceManagementPage()
    {
        PageTitle = "Device Management";

        _card.Title = "Camera Management";
        _card.TitleIcon = Icons.Devices;
        _card.Controls.Add(_table);

        BuildColumns();

        _add.Click += (s, e) => AddDevice();
        _scan.Click += (s, e) => ScanNetwork();
        _refresh.Click += (s, e) => Reload();
        _table.RowDoubleClicked += (s, item) => EditDevice((CameraDevice)item);

        Controls.Add(_card);
        PlaceTitleBarControls(_add, _scan, _refresh);
    }

    private void BuildColumns()
    {
        _table.EmptyText = "No cameras configured. Use Add Device or Scan Network to begin.";
        _table.RowHeight = 44;

        _table.AddColumn(new TableColumn("No.", 46, item => ((CameraDevice)item).Channel.ToString())
        {
            Color = _ => Theme.TextMuted
        });

        _table.AddColumn(new TableColumn("Camera Name", 180, item => ((CameraDevice)item).DisplayName)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.6f,
            Font = Theme.BodyBold
        });

        _table.AddColumn(new TableColumn("IP Address", 130, item => ((CameraDevice)item).IpAddress)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.1f,
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Protocol", 90, item => ((CameraDevice)item).ProtocolText)
        {
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Type", 110, item => ((CameraDevice)item).KindText)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1f,
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Status", 100, item => ((CameraDevice)item).StatusText)
        {
            Dot = item => Theme.StatusColor(((CameraDevice)item).Status),
            Color = item => Theme.StatusColor(((CameraDevice)item).Status)
        });

        _table.AddAction(new TableAction(Icons.Ptz, "PTZ control", item => OpenPtz((CameraDevice)item))
        {
            IsVisible = item => ((CameraDevice)item).PtzSupported
        });

        _table.AddAction(new TableAction(Icons.Edit, "Edit", item => EditDevice((CameraDevice)item)));

        _table.AddAction(new TableAction(Icons.Delete, "Delete", item => DeleteDevice((CameraDevice)item))
        {
            Color = Theme.TextSecondary,
            HoverColor = Theme.Offline
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        _card.SetBounds(area.X, area.Y, area.Width, area.Height);
        PlaceTitleBarControls(_add, _scan, _refresh);
    }

    public override void OnActivated() => Reload();

    private void Reload()
    {
        var cameras = Services.Cameras.GetAll();
        _table.SetRows(cameras.Cast<object>());
        _card.TitleSuffix = cameras.Count + " devices  -  " + cameras.Count(c => c.IsOnline) + " online";
        _card.Invalidate();
    }

    /// <summary>
    /// True when another camera would exceed the licensed ceiling. Checked
    /// before the dialog opens, so the operator is not asked to fill in details
    /// for a camera that cannot be saved.
    /// </summary>
    private bool AtCameraLimit(int adding = 1)
    {
        var limit = LicenseGate.MaxCameras;
        var existing = Services.Cameras.GetAll().Count;

        if (existing + adding <= limit)
        {
            return false;
        }

        ShowError(
            "This licence covers " + limit + " camera" + (limit == 1 ? string.Empty : "s") +
            " and " + existing + " are already configured.\n\n" +
            "Remove a camera, or contact your supplier to extend the licence.");

        return true;
    }

    private void AddDevice()
    {
        if (AtCameraLimit())
        {
            return;
        }

        using var dialog = new AddDeviceForm();

        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Camera != null)
        {
            Services.Cameras.Insert(dialog.Camera);
            Services.LogEvent(EventKind.System, "Camera added.", EventSeverity.Info, dialog.Camera);
            StartCamera(dialog.Camera);
            Reload();
        }
    }

    private void EditDevice(CameraDevice camera)
    {
        using var dialog = new AddDeviceForm(camera);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Camera == null)
        {
            return;
        }

        Services.Cameras.Update(dialog.Camera);
        Services.LogEvent(EventKind.System, "Camera settings updated.", EventSeverity.Info, dialog.Camera);

        // Restart the decoder so a changed URL or credential takes effect.
        Services.Streams.Remove(camera.Id);
        StartCamera(dialog.Camera);
        Reload();
    }

    private void DeleteDevice(CameraDevice camera)
    {
        if (!Confirm("Remove " + camera.DisplayName + "?\n\nRecorded footage is kept on disk.", "Delete Camera"))
        {
            return;
        }

        Services.Recording.Stop(camera.Id);
        Services.Streams.Remove(camera.Id);
        Services.Analytics.Forget(camera.Id);
        Services.Cameras.Delete(camera.Id);
        Services.LogEvent(EventKind.System, "Camera " + camera.DisplayName + " removed.");

        Reload();
    }

    private void OpenPtz(CameraDevice camera)
    {
        if (FindForm() is MainForm shell)
        {
            shell.ShowPtz(camera);
        }
    }

    private void StartCamera(CameraDevice camera)
    {
        if (!camera.Enabled)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Services.Streams.StartAsync(camera).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Services.LogEvent(EventKind.CameraOffline, ex.Message, EventSeverity.Warning, camera);
            }
        });
    }

    private void ScanNetwork()
    {
        using var dialog = new DiscoveryForm();

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedDevices.Count == 0)
        {
            return;
        }

        // A network scan can select many devices at once, so the whole batch is
        // checked before any of it is written rather than stopping part way.
        if (AtCameraLimit(dialog.SelectedDevices.Count))
        {
            return;
        }

        var added = 0;

        foreach (var discovered in dialog.SelectedDevices)
        {
            var camera = new CameraDevice
            {
                Channel = Services.Cameras.NextChannel(),
                Name = string.IsNullOrWhiteSpace(discovered.DisplayName) ? discovered.Address : discovered.DisplayName,
                IpAddress = discovered.Address,
                OnvifPort = discovered.Port,
                Port = 554,
                Protocol = CameraProtocol.Onvif,
                Username = dialog.Username,
                Password = dialog.Password
            };

            Services.Cameras.Insert(camera);
            StartCamera(camera);
            added++;
        }

        Services.LogEvent(EventKind.System, added + " camera(s) added from network scan.");
        Reload();
    }
}
