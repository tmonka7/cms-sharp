using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Ai;
using CMS.Core.Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Size = System.Drawing.Size;

namespace CMS.App.Forms;

/// <summary>
/// Enrols or edits one identity. A photo is loaded from disk or grabbed from a
/// live camera, the largest face in it is embedded, and the vector is stored
/// locally alongside a thumbnail.
/// </summary>
public sealed class AddFaceForm : Form
{
    private readonly TitleBar _titleBar = new TitleBar
    {
        BrandPrefix = "ADD ",
        BrandAccent = "FACE",
        ShowMaximize = false,
        ShowMinimize = false
    };

    private readonly PictureBox _photo = new PictureBox
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Theme.Input
    };

    private readonly DarkTextBox _name = new DarkTextBox { Placeholder = "Full name" };
    private readonly DarkComboBox _group = new DarkComboBox();
    private readonly DarkTextBox _note = new DarkTextBox { Placeholder = "Optional note" };
    private readonly DarkCheckBox _enabled = new DarkCheckBox { Text = "Active", Checked = true };

    private readonly FlatButton _browse = new FlatButton { Variant = ButtonVariant.Secondary, Icon = Icons.Folder, Text = "Photo", Size = new Size(104, 32) };
    private readonly FlatButton _capture = new FlatButton { Variant = ButtonVariant.Secondary, Icon = Icons.Camera, Text = "Capture", Size = new Size(104, 32) };
    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 150 };

    private readonly FlatButton _save = new FlatButton { Variant = ButtonVariant.Primary, Text = "Save", Size = new Size(104, 34) };
    private readonly FlatButton _cancel = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Size = new Size(104, 34) };

    private readonly DarkLabel _status = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };

    private readonly FaceRecord? _existing;
    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private Mat? _sourceImage;

    public AddFaceForm(FaceRecord? existing = null)
    {
        _existing = existing;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(620, 460);
        DoubleBuffered = true;
        KeyPreview = true;

        _titleBar.BrandPrefix = existing == null ? "ADD " : "EDIT ";

        _group.Items.AddRange(new object[] { "Employee", "Visitor", "Blacklist", "Unknown" });
        _group.SelectedIndex = 1;

        _browse.Click += OnBrowse;
        _capture.Click += OnCapture;
        _save.Click += OnSave;
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
        Controls.AddRange(new Control[]
        {
            _photo, _name, _group, _note, _enabled,
            _browse, _capture, _cameraPicker, _status, _save, _cancel
        });

        LoadCameras();
        LoadExisting();

        Resize += (s, e) => DoLayout();
    }

    private void LoadCameras()
    {
        _cameras = Program.Services.Cameras.GetAll().Where(c => c.Enabled).ToList();

        foreach (var camera in _cameras)
        {
            _cameraPicker.Items.Add(camera.DisplayName);
        }

        if (_cameraPicker.Items.Count > 0)
        {
            _cameraPicker.SelectedIndex = 0;
        }
        else
        {
            _capture.Enabled = false;
            _cameraPicker.Enabled = false;
        }
    }

    private void LoadExisting()
    {
        if (_existing == null)
        {
            return;
        }

        _name.Text = _existing.Name;
        _note.Text = _existing.Note;
        _enabled.Checked = _existing.Enabled;
        _group.SelectedIndex = (int)_existing.Group;

        if (_existing.Thumbnail != null && _existing.Thumbnail.Length > 0)
        {
            try
            {
                // The bitmap has to own its bytes: GDI+ keeps reading the stream
                // for the lifetime of an Image created from one, so the copy is
                // what makes closing the stream here safe.
                using var stream = new MemoryStream(_existing.Thumbnail);
                using var stored = Image.FromStream(stream);
                _photo.Image = new Bitmap(stored);
            }
            catch (ArgumentException)
            {
                // Leave the placeholder.
            }
        }

        SetStatus("Choose a new photo to replace the stored face vector.", Theme.TextMuted);
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
        var top = _titleBar.Bottom + 18;

        var photoSize = 200;
        _photo.SetBounds(left, top, photoSize, photoSize);

        _browse.SetBounds(left, _photo.Bottom + 10, 96, 32);
        _capture.SetBounds(left + 104, _photo.Bottom + 10, 96, 32);
        _cameraPicker.SetBounds(left, _photo.Bottom + 48, photoSize, 30);

        var fieldX = left + photoSize + 28;
        var fieldWidth = Width - fieldX - 24;
        var y = top + 18;

        _name.SetBounds(fieldX, y, fieldWidth, 32);
        y += 56;

        _group.SetBounds(fieldX, y, fieldWidth, 32);
        y += 56;

        _note.SetBounds(fieldX, y, fieldWidth, 32);
        y += 48;

        _enabled.SetBounds(fieldX, y, 140, 22);

        _status.SetBounds(left, Height - 84, Width - 48, 20);

        _cancel.SetBounds(Width - 24 - 104, Height - 54, 104, 34);
        _save.SetBounds(_cancel.Left - 112, Height - 54, 104, 34);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
            Title = "Select a face photo"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var image = Cv2.ImRead(picker.FileName, ImreadModes.Color);

            if (image.Empty())
            {
                image.Dispose();
                SetStatus("That file could not be decoded as an image.", Theme.Offline);
                return;
            }

            SetSource(image);
        }
        catch (Exception ex) when (ex is OpenCVException or IOException)
        {
            SetStatus("Could not open the file: " + ex.Message, Theme.Offline);
        }
    }

    private void OnCapture(object? sender, EventArgs e)
    {
        var index = _cameraPicker.SelectedIndex;
        if (index < 0 || index >= _cameras.Count)
        {
            return;
        }

        var frame = Program.Services.Streams.Snapshot(_cameras[index].Id);

        if (frame == null || frame.Empty())
        {
            frame?.Dispose();
            SetStatus("That camera has no live frame to capture.", Theme.Warning);
            return;
        }

        SetSource(frame);
    }

    /// <summary>
    /// Replaces the working image and previews the detected face, so the
    /// operator sees what will actually be enrolled.
    /// </summary>
    private void SetSource(Mat image)
    {
        _sourceImage?.Dispose();
        _sourceImage = image;

        _photo.Image?.Dispose();
        _photo.Image = null;

        var faces = Program.Services.FaceRecognition;

        if (!faces.IsReady)
        {
            _photo.Image = BitmapConverter.ToBitmap(image);
            SetStatus("Face models are not loaded; the photo cannot be embedded.", Theme.Warning);
            return;
        }

        try
        {
            _photo.Image = BitmapConverter.ToBitmap(image);
            SetStatus("Photo loaded. Press Save to enrol.", Theme.Online);
        }
        catch (Exception ex) when (ex is OpenCVException or ArgumentException)
        {
            SetStatus("The image could not be displayed: " + ex.Message, Theme.Offline);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            SetStatus("Enter a name.", Theme.Warning);
            _name.Focus();
            return;
        }

        var group = (FaceGroup)Math.Max(0, _group.SelectedIndex);

        // Editing without a new photo only updates the metadata.
        if (_existing != null && _sourceImage == null)
        {
            _existing.Name = _name.Text.Trim();
            _existing.Group = group;
            _existing.Note = _note.Text.Trim();
            _existing.Enabled = _enabled.Checked;

            Program.Services.Faces.Update(_existing);
            Program.Services.LogEvent(EventKind.System, "Face record updated: " + _existing.Name);

            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_sourceImage == null)
        {
            SetStatus("Choose a photo or capture one from a camera.", Theme.Warning);
            return;
        }

        var faces = Program.Services.FaceRecognition;

        if (!faces.IsReady)
        {
            SetStatus("The face models are not loaded.", Theme.Offline);
            return;
        }

        FaceRecord? record;

        try
        {
            record = faces.Enroll(_sourceImage, _name.Text.Trim(), group, _note.Text.Trim());
        }
        catch (Exception ex)
        {
            // A bad model file or an unexpected tensor layout should fail this
            // one enrolment, not take the whole station down.
            SetStatus("Enrolment failed: " + ex.Message, Theme.Offline);
            Program.Services.LogEvent(EventKind.System, "Face enrolment failed: " + ex, EventSeverity.Warning);
            return;
        }

        if (record == null)
        {
            SetStatus("No face was found in that photo. Try a clearer, front-facing image.", Theme.Offline);
            return;
        }

        record.Enabled = _enabled.Checked;

        if (!record.Enabled)
        {
            Program.Services.Faces.Update(record);
        }

        // Replacing an existing identity drops the previous vector.
        if (_existing != null)
        {
            Program.Services.Faces.Delete(_existing.Id);
        }

        Program.Services.LogEvent(EventKind.System, "Face enrolled: " + record.Name);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
        _status.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        var fieldX = 24 + 200 + 28;
        var top = _titleBar.Bottom + 18;

        Theme.DrawText(g, "Name", Theme.Small, Theme.TextSecondary,
            new Rectangle(fieldX, top, 200, 18));

        Theme.DrawText(g, "Group", Theme.Small, Theme.TextSecondary,
            new Rectangle(fieldX, top + 56, 200, 18));

        Theme.DrawText(g, "Note", Theme.Small, Theme.TextSecondary,
            new Rectangle(fieldX, top + 112, 200, 18));

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sourceImage?.Dispose();
            _photo.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
