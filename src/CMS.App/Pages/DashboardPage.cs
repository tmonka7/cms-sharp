using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Models;
using CMS.Core.Services;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// The landing screen: fleet counters, a live thumbnail wall, the recent event
/// feed and machine load.
/// </summary>
public sealed class DashboardPage : PageBase
{
    private readonly MetricTile _totalTile = new MetricTile();
    private readonly MetricTile _onlineTile = new MetricTile();
    private readonly MetricTile _offlineTile = new MetricTile();
    private readonly MetricTile _storageTile = new MetricTile();

    private readonly CardPanel _overviewCard = new CardPanel();
    private readonly CardPanel _eventsCard = new CardPanel();
    private readonly CardPanel _statusCard = new CardPanel();

    private readonly EventListView _events = new EventListView();
    private readonly LinearMeter _cpu = new LinearMeter { Caption = "CPU" };
    private readonly LinearMeter _memory = new LinearMeter { Caption = "Memory" };
    private readonly LinearMeter _network = new LinearMeter { Caption = "Network", ColorByLoad = false };

    private readonly Panel _thumbnailHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
    private readonly List<VideoView> _thumbnails = new List<VideoView>();
    private readonly Dictionary<int, VideoView> _byCamera = new Dictionary<int, VideoView>();

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private int _tickCounter;

    public DashboardPage()
    {
        PageTitle = "Dashboard";

        ConfigureTiles();

        _overviewCard.Title = "Camera Overview";
        _overviewCard.TitleIcon = Icons.Camera;
        _overviewCard.Controls.Add(_thumbnailHost);

        _eventsCard.Title = "Recent Events";
        _eventsCard.TitleIcon = Icons.Bell;
        _events.Dock = DockStyle.Fill;
        _events.ItemActivated += OnEventActivated;
        _eventsCard.Controls.Add(_events);

        _statusCard.Title = "System Status";
        _statusCard.TitleIcon = Icons.Cpu;
        _statusCard.Controls.Add(_cpu);
        _statusCard.Controls.Add(_memory);
        _statusCard.Controls.Add(_network);

        Controls.AddRange(new Control[]
        {
            _totalTile, _onlineTile, _offlineTile, _storageTile,
            _overviewCard, _eventsCard, _statusCard
        });

        _thumbnailHost.Resize += (s, e) => LayoutThumbnails();
    }

    private void ConfigureTiles()
    {
        _totalTile.Caption = "Total Cameras";
        _totalTile.Icon = Icons.Camera;
        _totalTile.IconColor = Theme.Info;

        _onlineTile.Caption = "Online";
        _onlineTile.Icon = Icons.Accept;
        _onlineTile.IconColor = Theme.Online;

        _offlineTile.Caption = "Offline";
        _offlineTile.Icon = Icons.Cancel;
        _offlineTile.IconColor = Theme.Offline;

        _storageTile.Caption = "Storage Used";
        _storageTile.Icon = Icons.Storage;
        _storageTile.IconColor = Theme.Violet;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        DoLayout();
    }

    private void DoLayout()
    {
        var area = ContentArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        const int gap = 12;
        const int tileHeight = 78;

        var tileWidth = (area.Width - (gap * 3)) / 4;
        var x = area.X;

        foreach (var tile in new[] { _totalTile, _onlineTile, _offlineTile, _storageTile })
        {
            tile.SetBounds(x, area.Y, tileWidth, tileHeight);
            x += tileWidth + gap;
        }

        var bodyY = area.Y + tileHeight + gap;
        var bodyHeight = Math.Max(120, area.Bottom - bodyY);

        // Two thirds for the wall, one third for the event feed and load.
        var leftWidth = (int)(area.Width * 0.62);
        var rightWidth = area.Width - leftWidth - gap;

        _overviewCard.SetBounds(area.X, bodyY, leftWidth, bodyHeight);

        var statusHeight = 156;
        var eventsHeight = Math.Max(140, bodyHeight - statusHeight - gap);

        _eventsCard.SetBounds(area.X + leftWidth + gap, bodyY, rightWidth, eventsHeight);
        _statusCard.SetBounds(area.X + leftWidth + gap, bodyY + eventsHeight + gap, rightWidth, statusHeight);

        LayoutStatusMeters();
        LayoutThumbnails();
    }

    private void LayoutStatusMeters()
    {
        var content = _statusCard.ContentBounds;
        var y = content.Y;

        foreach (var meter in new[] { _cpu, _memory, _network })
        {
            meter.SetBounds(content.X, y, content.Width, 32);
            y += 36;
        }
    }

    /// <summary>Tiles the camera thumbnails into as square a grid as fits.</summary>
    private void LayoutThumbnails()
    {
        if (_thumbnails.Count == 0 || _thumbnailHost.Width <= 0)
        {
            return;
        }

        const int gap = 8;
        var columns = _thumbnails.Count <= 2 ? _thumbnails.Count
            : _thumbnails.Count <= 6 ? 3
            : _thumbnails.Count <= 12 ? 4
            : 5;

        var rows = (int)Math.Ceiling(_thumbnails.Count / (double)columns);

        var cellWidth = (_thumbnailHost.Width - (gap * (columns - 1))) / columns;
        var cellHeight = (_thumbnailHost.Height - (gap * (rows - 1))) / Math.Max(1, rows);

        for (var i = 0; i < _thumbnails.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            _thumbnails[i].SetBounds(
                column * (cellWidth + gap),
                row * (cellHeight + gap),
                Math.Max(40, cellWidth),
                Math.Max(40, cellHeight));
        }
    }

    public override void OnActivated()
    {
        LoadCameras();
        LoadEvents();
        DoLayout();

        Services.Streams.FrameReady += OnFrameReady;
        Services.Streams.StatusChanged += OnStatusChanged;
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;
        Services.Streams.StatusChanged -= OnStatusChanged;
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll();

        _thumbnailHost.Controls.Clear();
        foreach (var view in _thumbnails)
        {
            view.Dispose();
        }

        _thumbnails.Clear();
        _byCamera.Clear();

        // The wall shows the first eight channels; the full set lives on the
        // Camera List and Live View screens.
        foreach (var camera in _cameras.Take(8))
        {
            var view = new VideoView
            {
                Camera = camera,
                Status = camera.Status,
                ShowOverlayHeader = true,
                ShowDetections = false,
                ShowFaces = false,
                Radius = 6
            };

            view.Click += (s, e) => OpenCamera(camera);
            view.Cursor = Cursors.Hand;

            _thumbnails.Add(view);
            _byCamera[camera.Id] = view;
            _thumbnailHost.Controls.Add(view);
        }

        _overviewCard.TitleSuffix = _cameras.Count + " cameras";
        UpdateCounters();
        LayoutThumbnails();
    }

    private void UpdateCounters()
    {
        var online = _cameras.Count(c => c.IsOnline);

        _totalTile.Value = _cameras.Count.ToString();
        _onlineTile.Value = online.ToString();
        _offlineTile.Value = (_cameras.Count - online).ToString();

        var used = Services.Recordings.TotalSizeBytes();
        _storageTile.Value = SystemMonitor.FormatBytes(used);
        _storageTile.Detail = "of " + Services.Settings.MaxStorageGb + " GB allowance";
    }

    private void LoadEvents()
        => _events.SetItems(Services.Events.Query(limit: 60));

    private void OnEventActivated(object? sender, EventEntry entry)
    {
        if (entry.CameraId <= 0)
        {
            return;
        }

        var camera = _cameras.FirstOrDefault(c => c.Id == entry.CameraId);
        if (camera != null)
        {
            OpenCamera(camera);
        }
    }

    private void OpenCamera(CameraDevice camera)
    {
        if (FindForm() is MainForm shell)
        {
            shell.ShowCamera(camera);
        }
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        VideoView view;
        if (_byCamera.TryGetValue(frame.CameraId, out view))
        {
            view.SetFrame(frame.Image);
        }
    }

    private void OnStatusChanged(object? sender, CameraStream stream)
    {
        RunOnUi(() =>
        {
            VideoView view;
            if (_byCamera.TryGetValue(stream.CameraId, out view))
            {
                view.Status = stream.Camera.Status;
                view.Invalidate();
            }

            UpdateCounters();
        });
    }

    public override void OnTick()
    {
        var snapshot = Services.Monitor.Sample();

        _cpu.Percent = snapshot.CpuPercent;

        _memory.Percent = snapshot.MemoryPercent;
        _memory.ValueText = snapshot.MemoryPercent.ToString("0") + "%";

        _network.Percent = MathEx.Clamp(snapshot.NetworkMbps * 2, 0, 100);
        _network.ValueText = snapshot.NetworkMbps.ToString("0.0") + " Mbps";

        // The counters and feed move slowly; refreshing every five seconds
        // keeps the database out of the per-second path.
        if (++_tickCounter % 5 == 0)
        {
            UpdateCounters();
            LoadEvents();
        }
    }
}
