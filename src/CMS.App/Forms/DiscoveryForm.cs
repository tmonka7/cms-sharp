using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Onvif;

namespace CMS.App.Forms;

/// <summary>
/// ONVIF WS-Discovery scan. Probes every local interface over UDP multicast and
/// lists the cameras that answer, so devices can be added without knowing their
/// addresses. Nothing leaves the local network.
/// </summary>
public sealed class DiscoveryForm : Form
{
    private readonly TitleBar _titleBar = new TitleBar
    {
        BrandPrefix = "NETWORK ",
        BrandAccent = "SCAN",
        ShowMaximize = false,
        ShowMinimize = false
    };

    private readonly DataTable _table = new DataTable();
    private readonly DarkTextBox _username = new DarkTextBox { Placeholder = "admin" };
    private readonly DarkTextBox _password = new DarkTextBox { UseSystemPasswordChar = true, Placeholder = "Password" };
    private readonly FlatButton _scan = new FlatButton { Variant = ButtonVariant.Secondary, Icon = Icons.Search, Text = "Scan", Size = new Size(110, 34) };
    private readonly FlatButton _add = new FlatButton { Variant = ButtonVariant.Primary, Text = "Add Selected", Size = new Size(126, 34) };
    private readonly FlatButton _cancel = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Size = new Size(104, 34) };
    private readonly DarkLabel _status = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };

    private readonly List<DiscoveredDevice> _found = new List<DiscoveredDevice>();
    private CancellationTokenSource? _cancellation;

    public DiscoveryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(720, 480);
        DoubleBuffered = true;
        KeyPreview = true;

        _table.EmptyText = "No cameras found yet. Press Scan to probe the local network.";
        _table.RowHeight = 40;

        _table.AddColumn(new TableColumn("Device", 200, item => ((DiscoveredDevice)item).DisplayName)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.4f,
            Font = Theme.BodyBold
        });

        _table.AddColumn(new TableColumn("IP Address", 140, item => ((DiscoveredDevice)item).Address)
        {
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Port", 60, item => ((DiscoveredDevice)item).Port.ToString())
        {
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Service URL", 220, item => ((DiscoveredDevice)item).ServiceUrl)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.6f,
            Color = _ => Theme.TextMuted
        });

        _scan.Click += OnScan;
        _add.Click += OnAdd;
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
        Controls.AddRange(new Control[] { _table, _username, _password, _scan, _add, _cancel, _status });

        Resize += (s, e) => DoLayout();
    }

    /// <summary>Devices the operator chose to add.</summary>
    public List<DiscoveredDevice> SelectedDevices { get; } = new List<DiscoveredDevice>();

    public string Username => _username.Text;

    public string Password => _password.Text;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DoLayout();
        OnScan(this, EventArgs.Empty);
    }

    private void DoLayout()
    {
        var top = _titleBar.Bottom + 14;

        _username.SetBounds(96, top, 160, 32);
        _password.SetBounds(96 + 168 + 64, top, 160, 32);
        _scan.SetBounds(Width - 24 - 110, top - 1, 110, 34);

        var tableTop = top + 48;
        _table.SetBounds(24, tableTop, Width - 48, Height - tableTop - 72);

        _status.SetBounds(24, Height - 56, 300, 34);
        _cancel.SetBounds(Width - 24 - 104, Height - 56, 104, 34);
        _add.SetBounds(_cancel.Left - 134, Height - 56, 126, 34);
    }

    private async void OnScan(object? sender, EventArgs e)
    {
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        _found.Clear();
        _table.SetRows(Array.Empty<object>());

        _scan.Enabled = false;
        _scan.Text = "Scanning...";
        _status.Text = "Probing the local network...";
        _status.ForeColor = Theme.TextSecondary;
        _status.Invalidate();

        var timeout = TimeSpan.FromSeconds(Math.Max(2, Program.Services.Settings.OnvifDiscoveryTimeoutSeconds));

        var progress = new Progress<DiscoveredDevice>(device =>
        {
            _found.Add(device);
            _table.SetRows(_found.Cast<object>());
            _status.Text = _found.Count + " device(s) found";
            _status.Invalidate();
        });

        try
        {
            var discovery = new OnvifDiscovery();
            var devices = await discovery
                .DiscoverAsync(timeout, progress, _cancellation.Token)
                .ConfigureAwait(true);

            _found.Clear();
            _found.AddRange(devices);
            _table.SetRows(_found.Cast<object>());

            _status.Text = devices.Count == 0
                ? "No ONVIF devices responded."
                : devices.Count + " device(s) found";

            _status.ForeColor = devices.Count == 0 ? Theme.Warning : Theme.Online;
        }
        catch (Exception ex)
        {
            _status.Text = "Scan failed: " + ex.Message;
            _status.ForeColor = Theme.Offline;
        }
        finally
        {
            _status.Invalidate();
            _scan.Enabled = true;
            _scan.Text = "Scan";
        }
    }

    private void OnAdd(object? sender, EventArgs e)
    {
        if (_table.SelectedItem is not DiscoveredDevice selected)
        {
            MessageBox.Show(this, "Select a device from the list first.", "Network Scan",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedDevices.Clear();
        SelectedDevices.Add(selected);

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        var top = _titleBar.Bottom + 14;

        Theme.DrawText(g, "Username", Theme.Small, Theme.TextSecondary,
            new Rectangle(24, top, 68, 32));

        Theme.DrawText(g, "Password", Theme.Small, Theme.TextSecondary,
            new Rectangle(96 + 168, top, 68, 32));

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        base.OnFormClosed(e);
    }
}
