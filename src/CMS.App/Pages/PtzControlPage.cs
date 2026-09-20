using System.Net.Http;
using CMS.App.Forms;
using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;
using CMS.Core.Onvif;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// PTZ operation over ONVIF: continuous pan and tilt from the pad, zoom, focus
/// and iris, plus preset storage and the auto scan / pattern / cruise commands.
/// </summary>
public sealed class PtzControlPage : PageBase
{
    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 210 };
    private readonly VideoView _video = new VideoView { ShowDetections = false, ShowFaces = false };

    private readonly CardPanel _controls = new CardPanel();
    private readonly PtzPad _pad = new PtzPad { Size = new Size(140, 140) };

    private readonly FlatButton _zoomIn = Pill("Zoom +");
    private readonly FlatButton _zoomOut = Pill("Zoom -");
    private readonly FlatButton _focusNear = Pill("Focus +");
    private readonly FlatButton _focusFar = Pill("Focus -");
    private readonly FlatButton _irisOpen = Pill("Iris +");
    private readonly FlatButton _irisClose = Pill("Iris -");

    private readonly DarkComboBox _presets = new DarkComboBox();
    private readonly FlatButton _setPreset = Pill("Set");
    private readonly FlatButton _goPreset = Pill("Go");

    private readonly FlatButton _autoScan = Tool("Auto Scan", Icons.Refresh);
    private readonly FlatButton _autoPan = Tool("Auto Pan", Icons.Right);
    private readonly FlatButton _pattern = Tool("Pattern", Icons.Ptz);
    private readonly FlatButton _cruise = Tool("Cruise", Icons.Play);

    private readonly DarkLabel _status = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };
    private readonly DarkSlider _speed = new DarkSlider { Minimum = 0.1, Maximum = 1.0, Value = 0.5 };

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private CameraDevice? _camera;
    private OnvifDeviceClient? _device;
    private OnvifPtzClient? _ptz;
    private List<OnvifPreset> _presetList = new List<OnvifPreset>();
    private bool _suppressPicker;

    public PtzControlPage()
    {
        PageTitle = "PTZ Control";

        _controls.Title = "PTZ";
        _controls.TitleIcon = Icons.Ptz;

        _pad.MoveStarted += OnPadMove;
        _pad.MoveStopped += (s, e) => Run(() => _ptz?.StopAsync() ?? Task.CompletedTask, "Stop");
        _pad.HomeRequested += (s, e) => Run(() => _ptz?.GotoHomeAsync() ?? Task.CompletedTask, "Home");

        _zoomIn.Click += (s, e) => Pulse(() => _ptz?.ZoomAsync(Speed) ?? Task.CompletedTask, "Zoom in");
        _zoomOut.Click += (s, e) => Pulse(() => _ptz?.ZoomAsync(-Speed) ?? Task.CompletedTask, "Zoom out");
        _focusNear.Click += (s, e) => Pulse(() => _ptz?.FocusAsync(Speed) ?? Task.CompletedTask, "Focus near");
        _focusFar.Click += (s, e) => Pulse(() => _ptz?.FocusAsync(-Speed) ?? Task.CompletedTask, "Focus far");
        _irisOpen.Click += (s, e) => Run(() => _ptz?.IrisAsync(0.2f) ?? Task.CompletedTask, "Iris open");
        _irisClose.Click += (s, e) => Run(() => _ptz?.IrisAsync(-0.2f) ?? Task.CompletedTask, "Iris close");

        _setPreset.Click += OnSetPreset;
        _goPreset.Click += OnGoPreset;

        _autoScan.Click += (s, e) => Run(() => _ptz?.AutoScanAsync() ?? Task.CompletedTask, "Auto scan");
        _autoPan.Click += (s, e) => Run(() => _ptz?.AutoPanAsync() ?? Task.CompletedTask, "Auto pan");
        _pattern.Click += (s, e) => Run(() => _ptz?.PatternAsync() ?? Task.CompletedTask, "Pattern");
        _cruise.Click += (s, e) => Run(() => _ptz?.CruiseAsync() ?? Task.CompletedTask, "Cruise");

        _cameraPicker.SelectedIndexChanged += OnPickerChanged;

        _controls.Controls.AddRange(new Control[]
        {
            _pad, _speed,
            _zoomIn, _zoomOut, _focusNear, _focusFar, _irisOpen, _irisClose,
            _presets, _setPreset, _goPreset,
            _autoScan, _autoPan, _pattern, _cruise,
            _status
        });

        Controls.Add(_video);
        Controls.Add(_controls);
        PlaceTitleBarControls(_cameraPicker);
    }

    private float Speed => (float)_speed.Value;

    private static FlatButton Pill(string text) => new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = text,
        Size = new Size(74, 30)
    };

    private static FlatButton Tool(string text, string icon) => new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = text,
        Icon = icon,
        IconSize = 10f,
        Size = new Size(112, 34)
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

        const int gap = 12;
        var panelWidth = Math.Min(320, Math.Max(260, area.Width / 3));
        var videoWidth = area.Width - panelWidth - gap;

        _video.SetBounds(area.X, area.Y, videoWidth, area.Height);
        _controls.SetBounds(area.X + videoWidth + gap, area.Y, panelWidth, area.Height);

        LayoutControls();
        PlaceTitleBarControls(_cameraPicker);
    }

    private void LayoutControls()
    {
        var content = _controls.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var centerX = content.X + (content.Width / 2);
        var y = content.Y;

        _pad.SetBounds(centerX - 70, y, 140, 140);
        y += 148;

        _speed.SetBounds(content.X, y, content.Width, 22);
        y += 32;

        // Two-column rows of paired controls.
        var half = (content.Width - 8) / 2;

        var pairs = new[]
        {
            (_zoomIn, _zoomOut),
            (_focusNear, _focusFar),
            (_irisOpen, _irisClose)
        };

        foreach (var pair in pairs)
        {
            pair.Item1.SetBounds(content.X, y, half, 30);
            pair.Item2.SetBounds(content.X + half + 8, y, half, 30);
            y += 36;
        }

        y += 8;

        _presets.SetBounds(content.X, y, content.Width, 32);
        y += 38;

        _setPreset.SetBounds(content.X, y, half, 30);
        _goPreset.SetBounds(content.X + half + 8, y, half, 30);
        y += 44;

        var quarter = (content.Width - 8) / 2;
        _autoScan.SetBounds(content.X, y, quarter, 34);
        _autoPan.SetBounds(content.X + quarter + 8, y, quarter, 34);
        y += 40;
        _pattern.SetBounds(content.X, y, quarter, 34);
        _cruise.SetBounds(content.X + quarter + 8, y, quarter, 34);
        y += 44;

        _status.SetBounds(content.X, y, content.Width, 36);
    }

    public override void OnActivated()
    {
        LoadCameras();
        DoLayout();
        Services.Streams.FrameReady += OnFrameReady;
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;
        DisposeDevice();
    }

    private void LoadCameras()
    {
        // Only cameras that reported PTZ can be driven.
        _cameras = Services.Cameras.GetAll()
            .Where(c => c.PtzSupported || c.Kind == CameraKind.PtzCamera)
            .ToList();

        _suppressPicker = true;
        _cameraPicker.Items.Clear();

        foreach (var camera in _cameras)
        {
            _cameraPicker.Items.Add(camera.DisplayName);
        }

        _suppressPicker = false;

        if (_cameras.Count == 0)
        {
            _camera = null;
            _video.Camera = null;
            _video.ClearFrame();
            PageSubtitle = "No PTZ-capable cameras configured";
            SetStatus("Mark a camera as PTZ-capable in Device Management to use this screen.", Theme.TextMuted);
            SetControlsEnabled(false);
            return;
        }

        SetControlsEnabled(true);

        if (_camera == null || !_cameras.Any(c => c.Id == _camera.Id))
        {
            SelectCamera(_cameras[0].Id);
        }
        else
        {
            SelectCamera(_camera.Id);
        }
    }

    /// <summary>Selects a camera and opens its ONVIF PTZ session.</summary>
    public void SelectCamera(int cameraId)
    {
        var camera = _cameras.FirstOrDefault(c => c.Id == cameraId);
        if (camera == null)
        {
            return;
        }

        _camera = camera;
        _video.Camera = camera;
        _video.Status = camera.Status;
        _video.ClearFrame();

        PageSubtitle = camera.DisplayName;

        _suppressPicker = true;
        _cameraPicker.SelectedIndex = _cameras.FindIndex(c => c.Id == cameraId);
        _suppressPicker = false;

        var frame = Services.Streams.Snapshot(cameraId);
        if (frame != null)
        {
            using (frame)
            {
                _video.SetFrame(frame);
            }
        }

        ConnectAsync(camera);
    }

    private async void ConnectAsync(CameraDevice camera)
    {
        DisposeDevice();
        SetStatus("Connecting to " + camera.IpAddress + "...", Theme.TextSecondary);

        try
        {
            var device = new OnvifDeviceClient(
                camera.IpAddress,
                camera.OnvifPort,
                camera.Username,
                camera.Password,
                TimeSpan.FromSeconds(8));

            var profiles = await device.GetProfilesAsync().ConfigureAwait(true);
            var profile = profiles.FirstOrDefault();

            if (profile == null)
            {
                device.Dispose();
                SetStatus("The device returned no media profiles.", Theme.Offline);
                return;
            }

            _device = device;
            _ptz = new OnvifPtzClient(device)
            {
                ProfileToken = profile.Token,
                VideoSourceToken = profile.VideoSourceToken
            };

            await LoadPresetsAsync().ConfigureAwait(true);
            SetStatus("Connected  -  profile " + profile.DisplayName, Theme.Online);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            SetStatus("PTZ unavailable: " + ex.Message, Theme.Offline);
        }
    }

    private async Task LoadPresetsAsync()
    {
        if (_ptz == null)
        {
            return;
        }

        try
        {
            _presetList = await _ptz.GetPresetsAsync().ConfigureAwait(true);

            _presets.Items.Clear();
            foreach (var preset in _presetList)
            {
                _presets.Items.Add(preset.DisplayName);
            }

            if (_presets.Items.Count > 0)
            {
                _presets.SelectedIndex = 0;
            }
        }
        catch (OnvifFaultException)
        {
            // Presets are optional on some devices.
            _presetList = new List<OnvifPreset>();
        }
    }

    private void OnPickerChanged(object? sender, EventArgs e)
    {
        if (_suppressPicker || _cameraPicker.SelectedIndex < 0)
        {
            return;
        }

        SelectCamera(_cameras[_cameraPicker.SelectedIndex].Id);
    }

    private void OnPadMove(object? sender, PtzMove direction)
        => Run(() => _ptz?.MoveAsync(direction, Speed) ?? Task.CompletedTask, direction.ToString());

    /// <summary>
    /// Runs a continuous command briefly and then stops, which is what a button
    /// press should do for zoom and focus.
    /// </summary>
    private async void Pulse(Func<Task> action, string label)
    {
        if (_ptz == null)
        {
            SetStatus("Not connected to a PTZ service.", Theme.Warning);
            return;
        }

        try
        {
            await action().ConfigureAwait(true);
            await Task.Delay(420).ConfigureAwait(true);
            await _ptz.StopAsync().ConfigureAwait(true);
            SetStatus(label, Theme.TextSecondary);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException)
        {
            SetStatus(label + " failed: " + ex.Message, Theme.Offline);
        }
    }

    private async void Run(Func<Task> action, string label)
    {
        if (_ptz == null)
        {
            SetStatus("Not connected to a PTZ service.", Theme.Warning);
            return;
        }

        try
        {
            await action().ConfigureAwait(true);
            SetStatus(label, Theme.TextSecondary);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException)
        {
            SetStatus(label + " failed: " + ex.Message, Theme.Offline);
        }
    }

    private async void OnSetPreset(object? sender, EventArgs e)
    {
        if (_ptz == null)
        {
            SetStatus("Not connected to a PTZ service.", Theme.Warning);
            return;
        }

        var name = Prompt.Show(this, "Preset name", "Save PTZ Preset", "Preset " + (_presetList.Count + 1));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await _ptz.SetPresetAsync(name!).ConfigureAwait(true);
            await LoadPresetsAsync().ConfigureAwait(true);
            SetStatus("Preset saved: " + name, Theme.Online);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException)
        {
            SetStatus("Could not save the preset: " + ex.Message, Theme.Offline);
        }
    }

    private void OnGoPreset(object? sender, EventArgs e)
    {
        var index = _presets.SelectedIndex;
        if (index < 0 || index >= _presetList.Count)
        {
            SetStatus("Select a preset first.", Theme.Warning);
            return;
        }

        var preset = _presetList[index];
        Run(() => _ptz?.GotoPresetAsync(preset.Token, Speed) ?? Task.CompletedTask, "Go to " + preset.DisplayName);
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (Control control in _controls.Controls)
        {
            control.Enabled = enabled || control == _status;
        }
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
        _status.Invalidate();
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (_camera != null && frame.CameraId == _camera.Id)
        {
            _video.SetFrame(frame.Image);
        }
    }

    private void DisposeDevice()
    {
        _ptz = null;
        _device?.Dispose();
        _device = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeDevice();
        }

        base.Dispose(disposing);
    }
}
