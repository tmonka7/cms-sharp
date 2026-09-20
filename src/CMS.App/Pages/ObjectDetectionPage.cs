using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Ai;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// Tuning and monitoring for the YOLO detector: which classes to report, the
/// confidence floor, and a live preview with the resulting boxes and tallies.
/// Changes apply immediately and are persisted to settings.
/// </summary>
public sealed class ObjectDetectionPage : PageBase
{
    private readonly CardPanel _settingsCard = new CardPanel();
    private readonly CardPanel _countsCard = new CardPanel();
    private readonly VideoView _video = new VideoView { ShowFaces = false };

    private readonly ToggleSwitch _enabled = new ToggleSwitch();
    private readonly DarkComboBox _model = new DarkComboBox();
    private readonly FlatButton _modelInfo = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = "Info",
        Size = new Size(58, 30)
    };

    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 210 };
    private readonly DarkSlider _confidence = new DarkSlider { Minimum = 0.05, Maximum = 0.95 };
    private readonly DarkLabel _confidenceValue = new DarkLabel
    {
        Font = Theme.SmallBold,
        ForeColor = Theme.TextPrimary,
        Alignment = ContentAlignment.MiddleRight
    };

    private readonly DarkLabel _modelState = new DarkLabel { Font = Theme.Caption, ForeColor = Theme.TextMuted };
    private readonly DetectionCountPanel _counts = new DetectionCountPanel { Dock = DockStyle.Fill };
    private readonly List<DarkCheckBox> _classBoxes = new List<DarkCheckBox>();

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private CameraDevice? _camera;
    private bool _loading;

    public ObjectDetectionPage()
    {
        PageTitle = "Object Detection";

        _settingsCard.Title = "Detection Settings";
        _settingsCard.TitleIcon = Icons.Settings;

        _countsCard.Title = "Detection Count";
        _countsCard.TitleIcon = Icons.Detection;
        _countsCard.Controls.Add(_counts);

        _model.Items.AddRange(new object[]
        {
            "YOLOv26n (Fast & Lightweight)",
            "YOLOv26s (Balanced)",
            "YOLOv26m (Accurate)"
        });

        foreach (var label in CocoLabels.Featured)
        {
            var box = new DarkCheckBox { Text = label, Tag = label };
            box.CheckedChanged += OnClassToggled;
            _classBoxes.Add(box);
            _settingsCard.Controls.Add(box);
        }

        _enabled.CheckedChanged += OnEnabledChanged;
        _confidence.ValueChanged += OnConfidenceChanged;
        _model.SelectedIndexChanged += OnModelChanged;
        _modelInfo.Click += OnModelInfo;
        _cameraPicker.SelectedIndexChanged += OnPickerChanged;

        _settingsCard.Controls.AddRange(new Control[]
        {
            _enabled, _model, _modelInfo, _confidence, _confidenceValue, _modelState
        });

        Controls.Add(_video);
        Controls.Add(_settingsCard);
        Controls.Add(_countsCard);

        PlaceTitleBarControls(_cameraPicker);
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
        var settingsWidth = Math.Min(300, Math.Max(240, area.Width / 4));
        var countsWidth = Math.Min(210, Math.Max(160, area.Width / 6));
        var videoWidth = area.Width - settingsWidth - countsWidth - (gap * 2);

        _settingsCard.SetBounds(area.X, area.Y, settingsWidth, area.Height);
        _video.SetBounds(area.X + settingsWidth + gap, area.Y, Math.Max(120, videoWidth), area.Height);
        _countsCard.SetBounds(area.Right - countsWidth, area.Y, countsWidth, area.Height);

        LayoutSettings();
        PlaceTitleBarControls(_cameraPicker);
    }

    private void LayoutSettings()
    {
        var content = _settingsCard.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var y = content.Y;

        // "Enable Object Detection" row: label painted, switch on the right.
        _enabled.SetBounds(content.Right - 42, y, 42, 22);
        y += 36;

        _model.SetBounds(content.X, y, content.Width - 64, 30);
        _modelInfo.SetBounds(content.Right - 58, y, 58, 30);
        y += 34;

        _modelState.SetBounds(content.X, y, content.Width, 16);
        y += 30;

        // Class checklist in two columns.
        var half = (content.Width - 8) / 2;
        for (var i = 0; i < _classBoxes.Count; i++)
        {
            _classBoxes[i].SetBounds(
                content.X + ((i % 2) * (half + 8)),
                y + ((i / 2) * 26),
                half,
                22);
        }

        y += (int)Math.Ceiling(_classBoxes.Count / 2.0) * 26;
        y += 22;

        _confidenceValue.SetBounds(content.Right - 50, y - 20, 50, 18);
        _confidence.SetBounds(content.X, y, content.Width, 22);
    }

    public override void OnActivated()
    {
        LoadSettings();
        LoadCameras();
        DoLayout();

        Services.Streams.FrameReady += OnFrameReady;
        Services.Analytics.ObjectsDetected += OnObjectsDetected;
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;
        Services.Analytics.ObjectsDetected -= OnObjectsDetected;
    }

    private void LoadSettings()
    {
        _loading = true;

        var settings = Services.Settings;

        _enabled.Checked = settings.ObjectDetectionEnabled;
        _confidence.Value = settings.ObjectConfidenceThreshold;
        _confidenceValue.Text = settings.ObjectConfidenceThreshold.ToString("0.00");

        var index = _model.Items.IndexOf(settings.ObjectModelName);
        _model.SelectedIndex = index >= 0 ? index : 0;

        foreach (var box in _classBoxes)
        {
            box.Checked = settings.EnabledClasses.Contains((string)box.Tag!);
        }

        UpdateModelState();
        _loading = false;
    }

    private void UpdateModelState()
    {
        var ready = Services.Analytics.ObjectModelReady;

        _modelState.Text = ready
            ? "Model loaded: " + Path.GetFileName(Services.Analytics.Detector.ModelPath)
            : "Model not loaded - " + (Services.Analytics.Detector.LastError ?? "file missing");

        _modelState.ForeColor = ready ? Theme.Online : Theme.Warning;
        _modelState.Invalidate();

        _counts.SetModelState(ready);
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll().Where(c => c.Enabled).ToList();

        _cameraPicker.Items.Clear();
        foreach (var camera in _cameras)
        {
            _cameraPicker.Items.Add(camera.DisplayName);
        }

        if (_camera == null || !_cameras.Any(c => c.Id == _camera.Id))
        {
            _camera = _cameras.FirstOrDefault();
        }

        if (_camera != null)
        {
            _cameraPicker.SelectedIndex = _cameras.FindIndex(c => c.Id == _camera.Id);
            AttachCamera(_camera);
        }
    }

    private void AttachCamera(CameraDevice camera)
    {
        _camera = camera;
        _video.Camera = camera;
        _video.Status = camera.Status;
        PageSubtitle = camera.DisplayName;

        var frame = Services.Streams.Snapshot(camera.Id);
        if (frame != null)
        {
            using (frame)
            {
                _video.SetFrame(frame);
            }
        }

        var snapshot = Services.Analytics.LatestFor(camera.Id);
        if (snapshot.Objects != null)
        {
            _video.SetDetections(snapshot.Objects.Detections);
            _counts.Update(snapshot.Objects);
        }
    }

    private void OnPickerChanged(object? sender, EventArgs e)
    {
        if (_cameraPicker.SelectedIndex < 0)
        {
            return;
        }

        AttachCamera(_cameras[_cameraPicker.SelectedIndex]);
    }

    private void OnEnabledChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = Services.Settings.Clone();
        settings.ObjectDetectionEnabled = _enabled.Checked;
        Services.SaveSettings(settings);

        _video.ShowDetections = _enabled.Checked;
        _video.Invalidate();
    }

    private void OnConfidenceChanged(object? sender, EventArgs e)
    {
        _confidenceValue.Text = _confidence.Value.ToString("0.00");
        _confidenceValue.Invalidate();

        if (_loading)
        {
            return;
        }

        var settings = Services.Settings.Clone();
        settings.ObjectConfidenceThreshold = (float)_confidence.Value;
        Services.SaveSettings(settings);
    }

    private void OnClassToggled(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = Services.Settings.Clone();
        settings.EnabledClasses = new HashSet<string>(
            _classBoxes.Where(b => b.Checked).Select(b => (string)b.Tag!),
            StringComparer.OrdinalIgnoreCase);

        Services.SaveSettings(settings);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_loading || _model.SelectedIndex < 0)
        {
            return;
        }

        var name = (string)_model.Items[_model.SelectedIndex];

        // The variant name maps onto a file next to the executable.
        var fileName = name.StartsWith("YOLOv26s", StringComparison.OrdinalIgnoreCase) ? "yolov26s.onnx"
            : name.StartsWith("YOLOv26m", StringComparison.OrdinalIgnoreCase) ? "yolov26m.onnx"
            : "yolov26n.onnx";

        var settings = Services.Settings.Clone();
        settings.ObjectModelName = name;
        settings.ObjectModelPath = Path.Combine("Models", fileName);
        Services.SaveSettings(settings);

        Services.Analytics.LoadModels(settings);
        UpdateModelState();
    }

    private void OnModelInfo(object? sender, EventArgs e)
    {
        var detector = Services.Analytics.Detector;

        var message = detector.IsReady
            ? "Model: " + Services.Settings.ObjectModelName +
              "\nFile: " + detector.ModelPath +
              "\nInput: " + detector.InputWidth + " x " + detector.InputHeight +
              "\nConfidence: " + detector.ConfidenceThreshold.ToString("0.00") +
              "\nNMS IoU: " + detector.NmsThreshold.ToString("0.00") +
              "\nClasses reported: " + (detector.ClassFilter.Count == 0 ? "all" : string.Join(", ", detector.ClassFilter))
            : "No detection model is loaded.\n\nExpected file:\n" +
              OnnxSessionFactory.ResolvePath(Services.Settings.ObjectModelPath) +
              "\n\nPlace a YOLO ONNX export at that path and reopen this screen. " +
              "Everything else in the application keeps working without it.";

        MessageBox.Show(this, message, "Detection Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (_camera != null && frame.CameraId == _camera.Id)
        {
            _video.SetFrame(frame.Image);
        }
    }

    private void OnObjectsDetected(object? sender, DetectionFrameResult result)
    {
        if (_camera == null || result.CameraId != _camera.Id)
        {
            return;
        }

        RunOnUi(() =>
        {
            _video.SetDetections(result.Detections);
            _counts.Update(result);
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var content = _settingsCard.ContentBounds;
        if (content.Width <= 0 || !_settingsCard.Visible)
        {
            return;
        }

        var g = e.Graphics;
        var origin = _settingsCard.Location;

        Theme.DrawText(g, "Enable Object Detection", Theme.Small, Theme.TextSecondary,
            new Rectangle(origin.X + content.X, origin.Y + content.Y, content.Width - 50, 22));

        var classesY = origin.Y + _classBoxes[0].Top - 20;
        Theme.DrawText(g, "Detection Classes", Theme.SmallBold, Theme.TextSecondary,
            new Rectangle(origin.X + content.X, classesY, content.Width, 18));

        Theme.DrawText(g, "Confidence Threshold", Theme.SmallBold, Theme.TextSecondary,
            new Rectangle(origin.X + content.X, origin.Y + _confidence.Top - 20, content.Width - 56, 18));
    }
}
