using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Ai;
using CMS.Core.Services;

namespace CMS.App.Pages;

/// <summary>
/// Build identity, machine load and host networking. Everything shown here is
/// read from the local machine; there is no update service to call.
/// </summary>
public sealed class SystemInfoPage : PageBase
{
    private readonly CardPanel _versionCard = new CardPanel();
    private readonly CardPanel _resourcesCard = new CardPanel();
    private readonly CardPanel _networkCard = new CardPanel();
    private readonly CardPanel _modelsCard = new CardPanel();

    private readonly RingGauge _cpu = new RingGauge { Caption = "CPU", RingColor = Theme.Accent };
    private readonly RingGauge _memory = new RingGauge { Caption = "Memory", RingColor = Theme.Info };
    private readonly RingGauge _disk = new RingGauge { Caption = "Disk", RingColor = Theme.Violet };

    private readonly FlatButton _checkUpdate = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = "Check for Update",
        Size = new Size(150, 32)
    };

    private readonly FlatButton _openFolder = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Icon = Icons.Folder,
        Text = "Open Data Folder",
        Size = new Size(158, 32)
    };

    private SystemSnapshot _snapshot = new SystemSnapshot();
    private NetworkSummary _network = new NetworkSummary();

    public SystemInfoPage()
    {
        PageTitle = "System Information";

        _versionCard.Title = "Software";
        _versionCard.TitleIcon = Icons.Info;

        _resourcesCard.Title = "System Resources";
        _resourcesCard.TitleIcon = Icons.Cpu;
        _resourcesCard.Controls.AddRange(new Control[] { _cpu, _memory, _disk });

        _networkCard.Title = "Network";
        _networkCard.TitleIcon = Icons.Network;

        _modelsCard.Title = "AI Models";
        _modelsCard.TitleIcon = Icons.Detection;

        _checkUpdate.Click += (s, e) => MessageBox.Show(
            this,
            "This installation is designed to run offline and does not contact an update server.\n\n" +
            "Version " + Program.Version + " is the installed build.",
            "Check for Update",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        _openFolder.Click += (s, e) => OpenDataFolder();

        _versionCard.Controls.Add(_checkUpdate);
        _versionCard.Controls.Add(_openFolder);

        Controls.AddRange(new Control[] { _versionCard, _resourcesCard, _networkCard, _modelsCard });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        const int gap = 12;
        var columnWidth = (area.Width - gap) / 2;
        var topHeight = (int)(area.Height * 0.52);

        _versionCard.SetBounds(area.X, area.Y, columnWidth, topHeight);
        _resourcesCard.SetBounds(area.X + columnWidth + gap, area.Y, area.Width - columnWidth - gap, topHeight);

        var bottomY = area.Y + topHeight + gap;
        var bottomHeight = area.Bottom - bottomY;

        _networkCard.SetBounds(area.X, bottomY, columnWidth, bottomHeight);
        _modelsCard.SetBounds(area.X + columnWidth + gap, bottomY, area.Width - columnWidth - gap, bottomHeight);

        LayoutGauges();

        var versionContent = _versionCard.ContentBounds;
        _checkUpdate.SetBounds(
            _versionCard.Left + versionContent.X,
            _versionCard.Top + versionContent.Bottom - 32,
            150, 32);

        _openFolder.SetBounds(
            _checkUpdate.Left + 158,
            _checkUpdate.Top,
            158, 32);

        // The buttons are children of the card, so use card-relative bounds.
        _checkUpdate.Location = new Point(versionContent.X, versionContent.Bottom - 32);
        _openFolder.Location = new Point(versionContent.X + 158, versionContent.Bottom - 32);
    }

    private void LayoutGauges()
    {
        var content = _resourcesCard.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var size = Math.Min(120, Math.Max(80, (content.Width - 32) / 3));
        var y = content.Y + Math.Max(0, (content.Height - size - 20) / 2);
        var spacing = (content.Width - (size * 3)) / 4;

        var x = content.X + spacing;

        foreach (var gauge in new[] { _cpu, _memory, _disk })
        {
            gauge.SetBounds(x, y, size, size + 20);
            x += size + spacing;
        }
    }

    public override void OnActivated()
    {
        _network = SystemMonitor.DescribeNetwork();
        OnTick();
        Invalidate();
    }

    public override void OnTick()
    {
        _snapshot = Services.Monitor.Sample();

        _cpu.Percent = _snapshot.CpuPercent;
        _memory.Percent = _snapshot.MemoryPercent;
        _disk.Percent = _snapshot.DiskPercent;

        _versionCard.Invalidate();
        _networkCard.Invalidate();
    }

    private void OpenDataFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(Services.Database.DatabasePath);

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ShowError("The folder could not be opened: " + ex.Message);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;

        DrawRows(g, _versionCard, new[]
        {
            ("Software Version", "v" + Program.Version),
            ("Build Date", File.GetLastWriteTime(typeof(Program).Assembly.Location).ToString("yyyy-MM-dd HH:mm")),
            ("Runtime", ".NET Framework " + Environment.Version),
            ("Operating System", Environment.OSVersion.VersionString),
            ("Machine", Environment.MachineName),
            ("Database", Services.Database.DatabasePath),
            ("Uptime", _snapshot.UptimeText)
        });

        DrawRows(g, _networkCard, new[]
        {
            ("Adapter", _network.Adapter),
            ("IP Address", _network.IpAddress),
            ("Subnet Mask", _network.SubnetMask),
            ("Gateway", _network.Gateway),
            ("DNS", _network.Dns),
            ("Throughput", _snapshot.NetworkMbps.ToString("0.0") + " Mbps")
        });

        var detector = Services.Analytics.Detector;
        var faces = Services.FaceRecognition;

        DrawRows(g, _modelsCard, new[]
        {
            ("Object Model", Services.Settings.ObjectModelName),
            ("Object Model File", detector.IsReady ? detector.ModelPath : "not loaded"),
            ("Input Size", detector.IsReady ? detector.InputWidth + " x " + detector.InputHeight : "-"),
            ("Face Detector", faces.DetectorReady ? faces.DetectorModelPath : "not loaded"),
            ("Face Recognition", faces.EmbedderReady ? faces.EmbedderModelPath : "not loaded"),
            ("Enrolled Identities", faces.GalleryCount.ToString()),
            ("Acceleration", Services.Settings.UseGpu ? "GPU requested" : "CPU")
        });

        DrawResourceDetail(g);
    }

    /// <summary>Draws a key/value list inside a card.</summary>
    private static void DrawRows(Graphics g, CardPanel card, (string Label, string Value)[] rows)
    {
        if (!card.Visible)
        {
            return;
        }

        var content = card.ContentBounds;
        var origin = card.Location;

        if (content.Width <= 0)
        {
            return;
        }

        var labelWidth = Math.Min(150, content.Width / 2);
        var y = origin.Y + content.Y;

        foreach (var row in rows)
        {
            Theme.DrawText(g, row.Label, Theme.Small, Theme.TextSecondary,
                new Rectangle(origin.X + content.X, y, labelWidth, 22));

            Theme.DrawText(g, string.IsNullOrEmpty(row.Value) ? "-" : row.Value, Theme.Small, Theme.TextPrimary,
                new Rectangle(origin.X + content.X + labelWidth, y, content.Width - labelWidth, 22),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);

            y += 24;
        }
    }

    private void DrawResourceDetail(Graphics g)
    {
        if (!_resourcesCard.Visible)
        {
            return;
        }

        var content = _resourcesCard.ContentBounds;
        var origin = _resourcesCard.Location;

        var text = "Memory " + _snapshot.MemoryText + "      Disk " + _snapshot.StorageText;

        Theme.DrawText(g, text, Theme.Caption, Theme.TextMuted,
            new Rectangle(origin.X + content.X, origin.Y + content.Bottom - 18, content.Width, 16),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
