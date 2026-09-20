using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// The compact wall: every channel at a glance with its live thumbnail and
/// connection state, plus a footer tally. Clicking a tile opens Live View.
/// </summary>
public sealed class CameraListPage : PageBase
{
    private readonly Panel _host = new Panel { BackColor = Theme.Background, AutoScroll = true };
    private readonly Panel _footer = new Panel { BackColor = Theme.Card, Height = 36 };

    private readonly FlatButton _twoUp = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Video, Size = new Size(32, 32) };
    private readonly FlatButton _fourUp = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Grid, Size = new Size(32, 32) };
    private readonly FlatButton _sixUp = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.List, Size = new Size(32, 32) };
    private readonly FlatButton _fullScreen = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.FullScreen,
        Text = "Full Screen",
        Size = new Size(122, 32)
    };

    private readonly List<VideoView> _views = new List<VideoView>();
    private readonly Dictionary<int, VideoView> _byCamera = new Dictionary<int, VideoView>();

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private int _columns = 4;
    private CameraDevice? _selected;

    public CameraListPage()
    {
        PageTitle = "Camera List";

        _twoUp.Click += (s, e) => SetColumns(2);
        _fourUp.Click += (s, e) => SetColumns(4);
        _sixUp.Click += (s, e) => SetColumns(6);
        _fullScreen.Click += OnFullScreen;

        _fourUp.Active = true;

        Controls.Add(_host);
        Controls.Add(_footer);

        PlaceTitleBarControls(_fullScreen, _sixUp, _fourUp, _twoUp);

        _host.Resize += (s, e) => LayoutTiles();
        _footer.Paint += OnFooterPaint;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        if (area.Width <= 0)
        {
            return;
        }

        _footer.SetBounds(area.X, area.Bottom - 36, area.Width, 36);
        _host.SetBounds(area.X, area.Y, area.Width, Math.Max(40, area.Height - 44));

        PlaceTitleBarControls(_fullScreen, _sixUp, _fourUp, _twoUp);
    }

    private void SetColumns(int columns)
    {
        _columns = columns;
        _twoUp.Active = columns == 2;
        _fourUp.Active = columns == 4;
        _sixUp.Active = columns == 6;
        LayoutTiles();
    }

    private void LayoutTiles()
    {
        if (_views.Count == 0 || _host.ClientSize.Width <= 0)
        {
            return;
        }

        const int gap = 8;
        var columns = Math.Max(1, Math.Min(_columns, _views.Count));
        var width = _host.ClientSize.Width - 4;
        var cellWidth = (width - (gap * (columns - 1))) / columns;

        // 16:9 tiles keep the wall regular regardless of source aspect.
        var cellHeight = (int)(cellWidth * 9.0 / 16.0);

        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].SetBounds(
                (i % columns) * (cellWidth + gap),
                (i / columns) * (cellHeight + gap),
                Math.Max(60, cellWidth),
                Math.Max(40, cellHeight));
        }
    }

    public override void OnActivated()
    {
        LoadCameras();
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

        _host.Controls.Clear();
        foreach (var view in _views)
        {
            view.Dispose();
        }

        _views.Clear();
        _byCamera.Clear();

        foreach (var camera in _cameras)
        {
            var view = new VideoView
            {
                Camera = camera,
                Status = camera.Status,
                ShowDetections = false,
                ShowFaces = false,
                Cursor = Cursors.Hand
            };

            var captured = camera;
            view.Click += (s, e) => Select(captured);
            view.DoubleClick += (s, e) => OpenLive(captured);

            _views.Add(view);
            _byCamera[camera.Id] = view;
            _host.Controls.Add(view);
        }

        _selected = _cameras.FirstOrDefault();
        if (_selected != null)
        {
            Select(_selected);
        }

        LayoutTiles();
        RestoreFrames();
        _footer.Invalidate();
    }

    private void RestoreFrames()
    {
        foreach (var pair in _byCamera)
        {
            var frame = Services.Streams.Snapshot(pair.Key);
            if (frame != null)
            {
                using (frame)
                {
                    pair.Value.SetFrame(frame);
                }
            }
        }
    }

    private void Select(CameraDevice camera)
    {
        _selected = camera;
        PageSubtitle = camera.DisplayName;

        foreach (var pair in _byCamera)
        {
            pair.Value.IsSelected = pair.Key == camera.Id;
            pair.Value.Invalidate();
        }
    }

    private void OpenLive(CameraDevice camera)
    {
        if (FindForm() is MainForm shell)
        {
            shell.ShowCamera(camera);
        }
    }

    private void OnFullScreen(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
            ShowError("Select a camera first.");
            return;
        }

        using var viewer = new FullScreenViewer(_selected);
        viewer.ShowDialog(this);
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

            _footer.Invalidate();
        });
    }

    private void OnFooterPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Card))
        {
            g.FillRectangle(brush, _footer.ClientRectangle);
        }

        Theme.DrawRounded(g, new Rectangle(0, 0, _footer.Width, _footer.Height), 6, Theme.Border);

        var online = _cameras.Count(c => c.IsOnline);
        var x = 14;

        x = DrawStat(g, x, Icons.Camera, _cameras.Count + " Cameras", Theme.TextSecondary);
        x = DrawStat(g, x, null, "Online " + online, Theme.Online);
        DrawStat(g, x, null, "Offline " + (_cameras.Count - online), Theme.Offline);
    }

    private int DrawStat(Graphics g, int x, string? glyph, string text, Color color)
    {
        if (glyph != null)
        {
            Theme.DrawText(g, glyph, Theme.IconFont(10f), color, new Rectangle(x, 0, 18, _footer.Height));
            x += 20;
        }
        else
        {
            Theme.DrawStatusDot(g, x, (_footer.Height - 7) / 2, 7, color);
            x += 13;
        }

        var width = Theme.MeasureText(text, Theme.Small).Width;
        Theme.DrawText(g, text, Theme.Small, color, new Rectangle(x, 0, width + 4, _footer.Height));

        return x + width + 22;
    }
}
