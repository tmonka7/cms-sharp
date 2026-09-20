using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.App.Forms;

/// <summary>
/// Borderless full-screen view of one camera, with the overlays still drawn.
/// Escape or the on-screen button returns to the shell.
/// </summary>
public sealed class FullScreenViewer : Form
{
    private readonly VideoView _view;
    private readonly FlatButton _exit;
    private readonly CameraDevice _camera;

    public FullScreenViewer(CameraDevice camera)
    {
        _camera = camera;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.VideoBackground;
        KeyPreview = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        _view = new VideoView
        {
            Dock = DockStyle.Fill,
            Camera = camera,
            Status = camera.Status,
            ShowDetections = true,
            ShowFaces = true,
            ShowBorder = false,
            Radius = 0,
            IsRecording = Program.Services.Recording.IsRecording(camera.Id)
        };

        _exit = new FlatButton
        {
            Variant = ButtonVariant.Icon,
            Icon = Icons.ExitFullScreen,
            IconSize = 12f,
            Size = new Size(40, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _exit.Click += (s, e) => Close();

        Controls.Add(_exit);
        Controls.Add(_view);

        _exit.BringToFront();

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F11)
            {
                Close();
            }
        };

        _view.DoubleClick += (s, e) => Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _exit.Location = new Point(Width - 56, 16);

        var frame = Program.Services.Streams.Snapshot(_camera.Id);
        if (frame != null)
        {
            using (frame)
            {
                _view.SetFrame(frame);
            }
        }

        var snapshot = Program.Services.Analytics.LatestFor(_camera.Id);
        if (snapshot.Objects != null)
        {
            _view.SetDetections(snapshot.Objects.Detections);
        }

        if (snapshot.Faces != null)
        {
            _view.SetFaces(snapshot.Faces.Matches);
        }

        Program.Services.Streams.FrameReady += OnFrameReady;
        Program.Services.Analytics.ObjectsDetected += OnObjects;
        Program.Services.Analytics.FacesRecognized += OnFaces;
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (frame.CameraId == _camera.Id)
        {
            _view.SetFrame(frame.Image);
        }
    }

    private void OnObjects(object? sender, DetectionFrameResult result)
    {
        if (result.CameraId == _camera.Id)
        {
            _view.SetDetections(result.Detections);
        }
    }

    private void OnFaces(object? sender, FaceFrameResult result)
    {
        if (result.CameraId == _camera.Id)
        {
            _view.SetFaces(result.Matches);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Services.Streams.FrameReady -= OnFrameReady;
        Program.Services.Analytics.ObjectsDetected -= OnObjects;
        Program.Services.Analytics.FacesRecognized -= OnFaces;
        base.OnFormClosed(e);
    }
}
