using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Ai;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// Live monitoring. A single focused camera or a tiled wall, with the detection
/// and recognition overlays drawn on the video and a per-class tally beside it.
/// </summary>
public sealed class LiveViewPage : PageBase
{
    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 210 };
    private readonly FlatButton _singleButton = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Video, Size = new Size(32, 32) };
    private readonly FlatButton _gridButton = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Grid, Size = new Size(32, 32) };

    private readonly Panel _videoHost = new Panel { BackColor = Theme.Background };
    private readonly CardPanel _sidePanel = new CardPanel();
    private readonly DetectionCountPanel _counts = new DetectionCountPanel { Dock = DockStyle.Fill };
    private readonly Panel _toolbar = new Panel { BackColor = Theme.Card, Height = 52 };

    private readonly FlatButton _snapshot = ToolButton(Icons.Camera, "Snapshot");
    private readonly FlatButton _record = ToolButton(Icons.Record, "Record");
    private readonly FlatButton _audio = ToolButton(Icons.Audio, "Audio");
    private readonly FlatButton _ptz = ToolButton(Icons.Ptz, "PTZ");
    private readonly FlatButton _fullScreen = ToolButton(Icons.FullScreen, "Full Screen");

    private readonly List<VideoView> _views = new List<VideoView>();
    private readonly Dictionary<int, VideoView> _byCamera = new Dictionary<int, VideoView>();

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private CameraDevice? _focused;
    private bool _gridMode;
    private bool _suppressPickerEvent;

    public LiveViewPage()
    {
        PageTitle = "Live View";

        _sidePanel.Title = "Object Detection";
        _sidePanel.TitleIcon = Icons.Detection;
        _sidePanel.TitleSuffix = Services.Settings.ObjectModelName;
        _sidePanel.Controls.Add(_counts);

        _singleButton.Active = true;
        _singleButton.Click += (s, e) => SetGridMode(false);
        _gridButton.Click += (s, e) => SetGridMode(true);

        _cameraPicker.SelectedIndexChanged += OnPickerChanged;

        _snapshot.Click += OnSnapshot;
        _record.Click += OnToggleRecord;
        _audio.Click += OnAudio;
        _ptz.Click += OnOpenPtz;
        _fullScreen.Click += OnFullScreen;

        _toolbar.Controls.AddRange(new Control[] { _snapshot, _record, _audio, _ptz, _fullScreen });
        _toolbar.Resize += (s, e) => LayoutToolbar();

        Controls.AddRange(new Control[] { _videoHost, _sidePanel, _toolbar });
        PlaceTitleBarControls(_gridButton, _singleButton, _cameraPicker);

        _videoHost.Resize += (s, e) => LayoutVideo();
    }

    private static FlatButton ToolButton(string icon, string text) => new FlatButton
    {
        Variant = ButtonVariant.Ghost,
        Icon = icon,
        IconSize = 11f,
        Text = text,
        Size = new Size(104, 36)
    };

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

        PlaceTitleBarControls(_gridButton, _singleButton, _cameraPicker);

        const int gap = 12;
        const int toolbarHeight = 52;

        var sideWidth = Math.Min(260, Math.Max(180, area.Width / 5));
        var videoWidth = area.Width - sideWidth - gap;
        var videoHeight = area.Height - toolbarHeight - gap;

        _videoHost.SetBounds(area.X, area.Y, videoWidth, videoHeight);
        _toolbar.SetBounds(area.X, area.Y + videoHeight + gap, videoWidth, toolbarHeight);
        _sidePanel.SetBounds(area.X + videoWidth + gap, area.Y, sideWidth, area.Height);

        LayoutVideo();
        LayoutToolbar();
    }

    private void LayoutToolbar()
    {
        var buttons = new[] { _snapshot, _record, _audio, _ptz, _fullScreen };
        var total = buttons.Sum(b => b.Width) + (8 * (buttons.Length - 1));
        var x = Math.Max(8, (_toolbar.Width - total) / 2);
        var y = (_toolbar.Height - 36) / 2;

        foreach (var button in buttons)
        {
            button.SetBounds(x, y, button.Width, 36);
            x += button.Width + 8;
        }
    }

    private void LayoutVideo()
    {
        if (_views.Count == 0 || _videoHost.Width <= 0)
        {
            return;
        }

        if (!_gridMode)
        {
            _views[0].SetBounds(0, 0, _videoHost.Width, _videoHost.Height);
            return;
        }

        const int gap = 6;
        var columns = _views.Count <= 1 ? 1
            : _views.Count <= 4 ? 2
            : _views.Count <= 9 ? 3
            : 4;

        var rows = (int)Math.Ceiling(_views.Count / (double)columns);
        var cellWidth = (_videoHost.Width - (gap * (columns - 1))) / columns;
        var cellHeight = (_videoHost.Height - (gap * (rows - 1))) / Math.Max(1, rows);

        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].SetBounds(
                (i % columns) * (cellWidth + gap),
                (i / columns) * (cellHeight + gap),
                Math.Max(40, cellWidth),
                Math.Max(40, cellHeight));
        }
    }

    public override void OnActivated()
    {
        LoadCameras();
        DoLayout();

        Services.Streams.FrameReady += OnFrameReady;
        Services.Streams.StatusChanged += OnStatusChanged;
        Services.Analytics.ObjectsDetected += OnObjectsDetected;
        Services.Analytics.FacesRecognized += OnFacesRecognized;
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;
        Services.Streams.StatusChanged -= OnStatusChanged;
        Services.Analytics.ObjectsDetected -= OnObjectsDetected;
        Services.Analytics.FacesRecognized -= OnFacesRecognized;
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll().Where(c => c.Enabled).ToList();

        _suppressPickerEvent = true;
        _cameraPicker.Items.Clear();
        foreach (var camera in _cameras)
        {
            _cameraPicker.Items.Add(camera.DisplayName);
        }

        if (_focused == null || !_cameras.Any(c => c.Id == _focused.Id))
        {
            _focused = _cameras.FirstOrDefault();
        }

        if (_focused != null)
        {
            _cameraPicker.SelectedIndex = _cameras.FindIndex(c => c.Id == _focused.Id);
        }

        _suppressPickerEvent = false;

        RebuildViews();
    }

    private void RebuildViews()
    {
        _videoHost.Controls.Clear();
        foreach (var view in _views)
        {
            view.Dispose();
        }

        _views.Clear();
        _byCamera.Clear();

        var shown = _gridMode
            ? _cameras
            : _focused == null ? new List<CameraDevice>() : new List<CameraDevice> { _focused };

        foreach (var camera in shown)
        {
            var view = new VideoView
            {
                Camera = camera,
                Status = camera.Status,
                ShowDetections = true,
                ShowFaces = true,
                IsRecording = Services.Recording.IsRecording(camera.Id),
                IsSelected = _gridMode && _focused != null && camera.Id == _focused.Id
            };

            var captured = camera;
            view.Click += (s, e) => FocusCamera(captured.Id);
            view.DoubleClick += (s, e) => { SetGridMode(false); FocusCamera(captured.Id); };

            _views.Add(view);
            _byCamera[camera.Id] = view;
            _videoHost.Controls.Add(view);
        }

        LayoutVideo();
        UpdateToolbarState();
        RestoreLatestResults();
    }

    /// <summary>Repopulates overlays from the last analysis so a freshly opened
    /// view is not blank until the next inference completes.</summary>
    private void RestoreLatestResults()
    {
        foreach (var pair in _byCamera)
        {
            var snapshot = Services.Analytics.LatestFor(pair.Key);

            if (snapshot.Objects != null)
            {
                pair.Value.SetDetections(snapshot.Objects.Detections);

                if (_focused != null && pair.Key == _focused.Id)
                {
                    _counts.Update(snapshot.Objects);
                }
            }

            if (snapshot.Faces != null)
            {
                pair.Value.SetFaces(snapshot.Faces.Matches);
            }

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

    private void SetGridMode(bool grid)
    {
        if (_gridMode == grid)
        {
            return;
        }

        _gridMode = grid;
        _gridButton.Active = grid;
        _singleButton.Active = !grid;
        RebuildViews();
    }

    /// <summary>Selects a camera, used by the dashboard and the camera list.</summary>
    public void FocusCamera(int cameraId)
    {
        var camera = _cameras.FirstOrDefault(c => c.Id == cameraId);
        if (camera == null)
        {
            return;
        }

        _focused = camera;

        _suppressPickerEvent = true;
        _cameraPicker.SelectedIndex = _cameras.FindIndex(c => c.Id == cameraId);
        _suppressPickerEvent = false;

        PageSubtitle = camera.DisplayName;

        if (_gridMode)
        {
            foreach (var pair in _byCamera)
            {
                pair.Value.IsSelected = pair.Key == cameraId;
                pair.Value.Invalidate();
            }

            UpdateToolbarState();
        }
        else
        {
            RebuildViews();
        }
    }

    private void OnPickerChanged(object? sender, EventArgs e)
    {
        if (_suppressPickerEvent || _cameraPicker.SelectedIndex < 0)
        {
            return;
        }

        FocusCamera(_cameras[_cameraPicker.SelectedIndex].Id);
    }

    private void UpdateToolbarState()
    {
        var hasCamera = _focused != null;

        _snapshot.Enabled = hasCamera;
        _record.Enabled = hasCamera;
        _audio.Enabled = hasCamera;
        _ptz.Enabled = hasCamera && _focused!.PtzSupported;
        _fullScreen.Enabled = hasCamera;

        if (hasCamera)
        {
            var recording = Services.Recording.IsRecording(_focused!.Id);
            _record.Text = recording ? "Stop" : "Record";
            _record.AccentOverride = recording ? Theme.Offline : null;
            _record.ForeColor = recording ? Theme.Offline : Theme.TextSecondary;
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
        });
    }

    private void OnObjectsDetected(object? sender, DetectionFrameResult result)
    {
        RunOnUi(() =>
        {
            VideoView view;
            if (_byCamera.TryGetValue(result.CameraId, out view))
            {
                view.SetDetections(result.Detections);
            }

            if (_focused != null && result.CameraId == _focused.Id)
            {
                _counts.Update(result);
            }
        });
    }

    private void OnFacesRecognized(object? sender, FaceFrameResult result)
    {
        RunOnUi(() =>
        {
            VideoView view;
            if (_byCamera.TryGetValue(result.CameraId, out view))
            {
                view.SetFaces(result.Matches);
            }
        });
    }

    private void OnSnapshot(object? sender, EventArgs e)
    {
        if (_focused == null)
        {
            return;
        }

        VideoView view;
        if (!_byCamera.TryGetValue(_focused.Id, out view))
        {
            return;
        }

        using var bitmap = view.CloneCurrentFrame();
        if (bitmap == null)
        {
            ShowError("There is no live frame to capture yet.");
            return;
        }

        try
        {
            var folder = Path.Combine(Services.Settings.StorageRoot, "Snapshots");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(
                folder,
                _focused.ChannelLabel + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg");

            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);

            Services.LogEvent(EventKind.System, "Snapshot saved: " + Path.GetFileName(path), EventSeverity.Info, _focused);

            MessageBox.Show(this, "Snapshot saved to:\n" + path, "Snapshot",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            ShowError("The snapshot could not be saved: " + ex.Message);
        }
    }

    private void OnToggleRecord(object? sender, EventArgs e)
    {
        if (_focused == null)
        {
            return;
        }

        var started = Services.Recording.Toggle(_focused);

        VideoView view;
        if (_byCamera.TryGetValue(_focused.Id, out view))
        {
            view.IsRecording = started;
            view.Invalidate();
        }

        Services.LogEvent(
            EventKind.Recording,
            started ? "Manual recording started." : "Manual recording stopped.",
            EventSeverity.Info,
            _focused);

        UpdateToolbarState();
    }

    private void OnAudio(object? sender, EventArgs e)
    {
        // Two-way audio needs the device backchannel, which varies by vendor.
        MessageBox.Show(
            this,
            "Audio output is not enabled for this camera profile.\n\n" +
            "Enable an audio-bearing ONVIF profile on the device to use this.",
            "Audio",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnOpenPtz(object? sender, EventArgs e)
    {
        if (_focused != null && FindForm() is MainForm shell)
        {
            shell.ShowPtz(_focused);
        }
    }

    private void OnFullScreen(object? sender, EventArgs e)
    {
        if (_focused == null)
        {
            return;
        }

        using var full = new FullScreenViewer(_focused);
        full.ShowDialog(this);
    }
}

/// <summary>
/// The per-class tally beside the live video: one row per detection class with
/// its current count.
/// </summary>
public sealed class DetectionCountPanel : Control
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private double _inferenceMs;
    private bool _modelReady = true;

    public DetectionCountPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Card;
        Font = Theme.Small;
    }

    public void Update(DetectionFrameResult result)
    {
        _counts.Clear();

        foreach (var pair in result.CountByLabel())
        {
            _counts[pair.Key] = pair.Value;
        }

        _inferenceMs = result.InferenceMs;
        Invalidate();
    }

    public void SetModelState(bool ready)
    {
        _modelReady = ready;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(BackColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        if (!_modelReady)
        {
            Theme.DrawText(g, "Detection model not loaded", Theme.Small, Theme.TextMuted,
                new Rectangle(0, 0, Width, 40));
            return;
        }

        var y = 0;
        var others = 0;

        foreach (var label in CocoLabels.Featured)
        {
            int count;
            _counts.TryGetValue(label, out count);
            DrawRow(g, y, label, count);
            y += 26;
        }

        foreach (var pair in _counts)
        {
            if (!CocoLabels.IsFeatured(pair.Key))
            {
                others += pair.Value;
            }
        }

        DrawRow(g, y, "others", others);
        y += 34;

        if (_inferenceMs > 0)
        {
            Theme.DrawText(g, "Inference " + _inferenceMs.ToString("0") + " ms",
                Theme.Caption, Theme.TextMuted, new Rectangle(0, y, Width, 16));
        }
    }

    private void DrawRow(Graphics g, int y, string label, int count)
    {
        var color = count > 0 ? Theme.Accent : Theme.TextMuted;

        Theme.DrawStatusDot(g, 2, y + 8, 6, color);

        Theme.DrawText(g, label, Theme.Small, count > 0 ? Theme.TextPrimary : Theme.TextSecondary,
            new Rectangle(16, y, Width - 50, 20));

        Theme.DrawText(g, count.ToString(), count > 0 ? Theme.SmallBold : Theme.Small,
            count > 0 ? Theme.Accent : Theme.TextMuted,
            new Rectangle(Width - 34, y, 30, 20),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
