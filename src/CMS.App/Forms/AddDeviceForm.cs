using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;
using CMS.Core.Onvif;
using OpenCvSharp;
using Size = System.Drawing.Size;
using OpenCvSharp.Extensions;

namespace CMS.App.Forms;

/// <summary>
/// Add or edit a camera. Test Connection performs a real ONVIF round trip and
/// shows the returned identity, profiles and preview frame, so a device is
/// verified before it is saved.
/// </summary>
public sealed class AddDeviceForm : Form
{
    private readonly TitleBar _titleBar = new TitleBar
    {
        BrandPrefix = "ADD CAMERA ",
        BrandAccent = "DEVICE",
        ShowMaximize = false,
        ShowMinimize = false
    };

    private readonly DarkTextBox _name = new DarkTextBox { Placeholder = "CH9 - New Camera" };
    private readonly DarkTextBox _ip = new DarkTextBox { Placeholder = "192.168.1.100" };
    private readonly DarkTextBox _onvifPort = new DarkTextBox { Placeholder = "80" };
    private readonly DarkTextBox _rtspPort = new DarkTextBox { Placeholder = "554" };
    private readonly DarkTextBox _username = new DarkTextBox { Placeholder = "admin" };
    private readonly DarkTextBox _password = new DarkTextBox { UseSystemPasswordChar = true, Placeholder = "Password" };
    private readonly DarkTextBox _streamUrl = new DarkTextBox { Placeholder = "rtsp://192.168.1.100:554/stream1" };

    private readonly DarkComboBox _protocol = new DarkComboBox();
    private readonly DarkComboBox _kind = new DarkComboBox();
    private readonly DarkComboBox _profile = new DarkComboBox { Placeholder = "Resolved after a connection test" };

    private readonly DarkCheckBox _objectDetection = new DarkCheckBox { Text = "Object detection", Checked = true };
    private readonly DarkCheckBox _faceRecognition = new DarkCheckBox { Text = "Face recognition", Checked = true };
    private readonly DarkCheckBox _ptzSupported = new DarkCheckBox { Text = "PTZ supported" };
    private readonly DarkCheckBox _enabled = new DarkCheckBox { Text = "Enabled", Checked = true };

    private readonly FlatButton _test = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Test Connection", Size = new Size(140, 34) };
    private readonly FlatButton _save = new FlatButton { Variant = ButtonVariant.Primary, Text = "Save", Size = new Size(104, 34) };
    private readonly FlatButton _cancel = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Size = new Size(104, 34) };

    private readonly PictureBox _preview = new PictureBox
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Theme.VideoBackground
    };

    private readonly DarkLabel _testResult = new DarkLabel
    {
        Font = Theme.Small,
        Alignment = ContentAlignment.MiddleLeft
    };

    private readonly CameraDevice _camera;
    private readonly bool _isEdit;
    private List<OnvifProfile> _profiles = new List<OnvifProfile>();
    private CancellationTokenSource? _testCancellation;

    public AddDeviceForm(CameraDevice? existing = null)
    {
        _isEdit = existing != null;
        _camera = existing != null ? existing.Clone() : new CameraDevice
        {
            Channel = Program.Services.Cameras.NextChannel()
        };

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(760, 560);
        DoubleBuffered = true;
        KeyPreview = true;

        _titleBar.BrandPrefix = _isEdit ? "EDIT CAMERA " : "ADD CAMERA ";

        _protocol.Items.AddRange(new object[] { "ONVIF", "RTSP", "HTTP" });
        _kind.Items.AddRange(new object[] { "IP Camera", "PTZ Camera", "NVR", "USB Camera" });

        _test.Click += OnTestConnection;
        _save.Click += OnSave;
        _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        _profile.SelectedIndexChanged += OnProfileChanged;

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Controls.Add(_titleBar);
        Controls.AddRange(new Control[]
        {
            _name, _ip, _onvifPort, _rtspPort, _username, _password, _streamUrl,
            _protocol, _kind, _profile,
            _objectDetection, _faceRecognition, _ptzSupported, _enabled,
            _preview, _testResult, _test, _save, _cancel
        });

        LoadCamera();
        Resize += (s, e) => DoLayout();
    }

    /// <summary>The configured camera when the dialog is accepted.</summary>
    public CameraDevice? Camera { get; private set; }

    private void LoadCamera()
    {
        _name.Text = _camera.Name;
        _ip.Text = _camera.IpAddress;
        _onvifPort.Text = _camera.OnvifPort.ToString();
        _rtspPort.Text = _camera.Port.ToString();
        _username.Text = _camera.Username;
        _password.Text = _camera.Password;
        _streamUrl.Text = _camera.StreamUrl;

        _protocol.SelectedIndex = (int)_camera.Protocol switch
        {
            (int)CameraProtocol.Onvif => 0,
            (int)CameraProtocol.Rtsp => 1,
            _ => 2
        };

        _kind.SelectedIndex = (int)_camera.Kind;

        _objectDetection.Checked = _camera.ObjectDetectionEnabled;
        _faceRecognition.Checked = _camera.FaceRecognitionEnabled;
        _ptzSupported.Checked = _camera.PtzSupported;
        _enabled.Checked = _camera.Enabled;

        if (string.IsNullOrEmpty(_name.Text))
        {
            _name.Text = "CH" + _camera.Channel + " - New Camera";
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DoLayout();
        _name.Focus();
    }

    private void DoLayout()
    {
        const int left = 24;
        const int labelWidth = 104;
        const int fieldHeight = 32;
        const int rowGap = 42;

        var top = _titleBar.Bottom + 18;
        var formWidth = 360;
        var fieldWidth = formWidth - labelWidth;

        var rows = new (Control Control, int Height)[]
        {
            (_name, fieldHeight),
            (_ip, fieldHeight),
            (_onvifPort, fieldHeight),
            (_rtspPort, fieldHeight),
            (_username, fieldHeight),
            (_password, fieldHeight),
            (_protocol, fieldHeight),
            (_kind, fieldHeight),
            (_profile, fieldHeight)
        };

        var y = top;
        foreach (var row in rows)
        {
            row.Control.SetBounds(left + labelWidth, y, fieldWidth, row.Height);
            y += rowGap;
        }

        // The stream URL spans the full dialog width.
        _streamUrl.SetBounds(left + labelWidth, y, Width - left - labelWidth - 24, fieldHeight);
        y += rowGap + 4;

        var checkX = left + labelWidth;
        _objectDetection.SetBounds(checkX, y, 160, 22);
        _faceRecognition.SetBounds(checkX + 168, y, 160, 22);
        y += 28;
        _ptzSupported.SetBounds(checkX, y, 160, 22);
        _enabled.SetBounds(checkX + 168, y, 160, 22);

        // Preview pane on the right of the form fields.
        var previewX = left + formWidth + 24;
        var previewWidth = Width - previewX - 24;
        _preview.SetBounds(previewX, top, Math.Max(120, previewWidth), 210);

        _test.SetBounds(previewX, _preview.Bottom + 12, 140, 34);
        _testResult.SetBounds(previewX, _test.Bottom + 10, Math.Max(120, previewWidth), 40);

        _cancel.SetBounds(Width - 24 - 104, Height - 56, 104, 34);
        _save.SetBounds(_cancel.Left - 112, Height - 56, 104, 34);
    }

    private async void OnTestConnection(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_ip.Text))
        {
            SetResult("Enter an IP address first.", Theme.Warning);
            return;
        }

        _test.Enabled = false;
        _test.Text = "Testing...";
        SetResult("Contacting device...", Theme.TextSecondary);

        _testCancellation?.Cancel();
        _testCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = _testCancellation.Token;

        try
        {
            var host = _ip.Text.Trim();
            var onvifPort = ParseInt(_onvifPort.Text, 80);
            var user = _username.Text;
            var pass = _password.Text;

            OnvifDeviceProbeResult probe;

            using (var client = new OnvifDeviceClient(host, onvifPort, user, pass, TimeSpan.FromSeconds(10)))
            {
                probe = await client.ProbeAsync(token).ConfigureAwait(true);
            }

            if (!probe.Success)
            {
                SetResult("Failed: " + probe.Error, Theme.Offline);
                return;
            }

            _profiles = probe.Profiles;
            _profile.Items.Clear();

            foreach (var profile in _profiles)
            {
                _profile.Items.Add(profile.DisplayName);
            }

            if (_profile.Items.Count > 0)
            {
                _profile.SelectedIndex = 0;
            }

            if (!string.IsNullOrEmpty(probe.StreamUri))
            {
                _streamUrl.Text = probe.StreamUri;
            }

            if (probe.PtzSupported)
            {
                _ptzSupported.Checked = true;
            }

            var identity = probe.Information == null ? host : probe.Information.ToString();
            var firmware = probe.Information?.FirmwareVersion ?? string.Empty;

            SetResult(
                "Success  -  " + identity +
                (string.IsNullOrEmpty(firmware) ? string.Empty : "  (fw " + firmware + ")") +
                "\n" + _profiles.Count + " profile(s)" + (probe.PtzSupported ? ", PTZ available" : string.Empty),
                Theme.Online);

            if (string.IsNullOrWhiteSpace(_name.Text) || _name.Text.EndsWith("New Camera", StringComparison.OrdinalIgnoreCase))
            {
                if (probe.Information != null && !string.IsNullOrWhiteSpace(probe.Information.Model))
                {
                    _name.Text = "CH" + _camera.Channel + " - " + probe.Information.Model;
                }
            }

            await LoadPreviewAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetResult("The connection test timed out.", Theme.Offline);
        }
        catch (Exception ex)
        {
            SetResult("Failed: " + ex.Message, Theme.Offline);
        }
        finally
        {
            _test.Enabled = true;
            _test.Text = "Test Connection";
        }
    }

    /// <summary>Grabs a single frame so the operator can confirm the feed.</summary>
    private async Task LoadPreviewAsync(CancellationToken cancellationToken)
    {
        var url = BuildStreamUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var bitmap = await Task.Run(() =>
        {
            try
            {
                using var capture = new VideoCapture(url, VideoCaptureAPIs.FFMPEG);
                if (!capture.IsOpened())
                {
                    return null;
                }

                using var frame = new Mat();

                // The first packets after a connect are often empty.
                for (var attempt = 0; attempt < 30 && !cancellationToken.IsCancellationRequested; attempt++)
                {
                    if (capture.Read(frame) && !frame.Empty())
                    {
                        return BitmapConverter.ToBitmap(frame);
                    }

                    Thread.Sleep(60);
                }

                return null;
            }
            catch (Exception ex) when (ex is OpenCVException or ArgumentException)
            {
                return null;
            }
        }, cancellationToken).ConfigureAwait(true);

        if (bitmap == null)
        {
            return;
        }

        _preview.Image?.Dispose();
        _preview.Image = bitmap;
    }

    private string BuildStreamUrl()
    {
        var probe = new CameraDevice
        {
            IpAddress = _ip.Text.Trim(),
            Port = ParseInt(_rtspPort.Text, 554),
            Username = _username.Text,
            Password = _password.Text,
            StreamUrl = _streamUrl.Text.Trim()
        };

        return probe.BuildEffectiveStreamUrl();
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        var index = _profile.SelectedIndex;
        if (index < 0 || index >= _profiles.Count)
        {
            return;
        }

        var selected = _profiles[index];
        if (!string.IsNullOrEmpty(selected.PtzNodeToken))
        {
            _ptzSupported.Checked = true;
        }
    }

    private void SetResult(string message, Color color)
    {
        _testResult.Text = message;
        _testResult.ForeColor = color;
        _testResult.Invalidate();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            ShowValidation("Enter a camera name.");
            _name.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_ip.Text) && string.IsNullOrWhiteSpace(_streamUrl.Text))
        {
            ShowValidation("Enter an IP address or a full stream URL.");
            _ip.Focus();
            return;
        }

        _camera.Name = StripChannelPrefix(_name.Text.Trim());
        _camera.IpAddress = _ip.Text.Trim();
        _camera.OnvifPort = ParseInt(_onvifPort.Text, 80);
        _camera.Port = ParseInt(_rtspPort.Text, 554);
        _camera.Username = _username.Text;
        _camera.Password = _password.Text;
        _camera.StreamUrl = _streamUrl.Text.Trim();

        _camera.Protocol = _protocol.SelectedIndex switch
        {
            0 => CameraProtocol.Onvif,
            1 => CameraProtocol.Rtsp,
            _ => CameraProtocol.Http
        };

        _camera.Kind = (CameraKind)Math.Max(0, _kind.SelectedIndex);
        _camera.ObjectDetectionEnabled = _objectDetection.Checked;
        _camera.FaceRecognitionEnabled = _faceRecognition.Checked;
        _camera.PtzSupported = _ptzSupported.Checked || _camera.Kind == CameraKind.PtzCamera;
        _camera.Enabled = _enabled.Checked;

        Camera = _camera;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// The channel prefix is rendered from the channel number, so a pasted
    /// "CH3 - Lobby" is stored as just "Lobby".
    /// </summary>
    private string StripChannelPrefix(string name)
    {
        var marker = " - ";
        if (name.StartsWith("CH", StringComparison.OrdinalIgnoreCase))
        {
            var index = name.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && index < 6)
            {
                return name.Substring(index + marker.Length);
            }
        }

        return name;
    }

    private void ShowValidation(string message)
        => MessageBox.Show(this, message, "Add Device", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, out var value) && value > 0 ? value : fallback;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        const int left = 24;
        const int labelWidth = 104;
        const int rowGap = 42;

        var labels = new[]
        {
            "Camera Name", "IP Address", "ONVIF Port", "RTSP Port",
            "Username", "Password", "Protocol", "Camera Type", "Profile"
        };

        var y = _titleBar.Bottom + 18;

        foreach (var label in labels)
        {
            Theme.DrawText(g, label, Theme.Small, Theme.TextSecondary,
                new Rectangle(left, y, labelWidth - 8, 32));
            y += rowGap;
        }

        Theme.DrawText(g, "Stream URL", Theme.Small, Theme.TextSecondary,
            new Rectangle(left, y, labelWidth - 8, 32));
        y += rowGap + 4;

        Theme.DrawText(g, "Analytics", Theme.Small, Theme.TextSecondary,
            new Rectangle(left, y, labelWidth - 8, 22));

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _testCancellation?.Cancel();
            _testCancellation?.Dispose();
            _preview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
