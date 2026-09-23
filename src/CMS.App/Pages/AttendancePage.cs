using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Attendance;
using CMS.Core.Models;
using CMS.Core.Onvif;
using CMS.Core.Streaming;
using Size = System.Drawing.Size;

namespace CMS.App.Pages;

/// <summary>
/// Automatic attendance. The operator frames the room, presses one button, and
/// the camera sweeps an arc on its own, registering everyone it finds.
///
/// The screen deliberately shows what the sweep is doing at each moment. A
/// twenty to forty second operation that moves a camera by itself is alarming
/// when it gives no account of itself, and the running commentary is also what
/// makes a disappointing result diagnosable.
/// </summary>
public sealed class AttendancePage : PageBase
{
    private readonly CardPanel _previewCard = new CardPanel();
    private readonly CardPanel _resultsCard = new CardPanel();
    private readonly CardPanel _statusCard = new CardPanel();

    private readonly VideoView _preview = new VideoView
    {
        Dock = DockStyle.Fill,
        ShowOverlayHeader = true,
        ShowFaces = true,
        ShowDetections = false
    };

    private readonly DataTable _results = new DataTable { Dock = DockStyle.Fill };

    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 190 };

    private readonly FlatButton _start = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Face,
        Text = "Automatic Attendance",
        Size = new Size(184, 32)
    };

    private readonly FlatButton _cancel = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = "Stop",
        Size = new Size(84, 32),
        Enabled = false
    };

    private readonly LinearMeter _progress = new LinearMeter { Caption = "Sweep", ColorByLoad = false };
    private readonly DarkLabel _stage = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary };
    private readonly DarkLabel _counts = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };
    private readonly DarkLabel _readiness = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextMuted };

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private CameraDevice? _camera;

    private OnvifDeviceClient? _device;
    private OnvifPtzClient? _ptz;
    private PtzNodeInfo? _node;
    private CancellationTokenSource? _cancellation;

    private AttendanceSession? _session;

    public AttendancePage()
    {
        PageTitle = "Attendance";

        _previewCard.Title = "Camera";
        _previewCard.TitleIcon = Icons.Camera;
        _previewCard.Controls.Add(_preview);

        _resultsCard.Title = "Attendance";
        _resultsCard.TitleIcon = Icons.People;
        _resultsCard.Controls.Add(_results);

        _statusCard.Title = "Sweep";
        _statusCard.TitleIcon = Icons.Ptz;
        _statusCard.Controls.Add(_progress);
        _statusCard.Controls.Add(_stage);
        _statusCard.Controls.Add(_counts);
        _statusCard.Controls.Add(_readiness);

        BuildColumns();

        _cameraPicker.SelectedIndexChanged += (s, e) => SelectCamera();
        _start.Click += (s, e) => StartSweep();
        _cancel.Click += (s, e) => _cancellation?.Cancel();

        Controls.Add(_previewCard);
        Controls.Add(_resultsCard);
        Controls.Add(_statusCard);

        PlaceTitleBarControls(_start, _cancel, _cameraPicker);
    }

    private void BuildColumns()
    {
        _results.EmptyText = "No sweep has been run yet. Choose a camera and press Automatic Attendance.";
        _results.RowHeight = 46;

        _results.AddColumn(new TableColumn("Photo", 44, _ => string.Empty)
        {
            Image = item => ((AttendanceEntry)item).Thumbnail
        });

        _results.AddColumn(new TableColumn("Name", 150, item => ((AttendanceEntry)item).Name)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.5f,
            Font = Theme.BodyBold
        });

        // The review state rides on this pill rather than occupying a column of
        // its own: an entry worth checking is what the amber is for.
        _results.AddColumn(new TableColumn("Result", 128, item =>
        {
            var entry = (AttendanceEntry)item;
            return entry.NeedsReview ? entry.OutcomeText + " ?" : entry.OutcomeText;
        })
        {
            Pill = true,
            Color = item =>
            {
                var entry = (AttendanceEntry)item;
                if (entry.NeedsReview)
                {
                    return Theme.Warning;
                }

                return entry.Outcome switch
                {
                    AttendanceOutcome.Recognised => Theme.Online,
                    AttendanceOutcome.RecognisedProvisional => Theme.Info,
                    _ => Theme.Violet
                };
            }
        });

        _results.AddColumn(new TableColumn("Match", 64, item =>
        {
            var entry = (AttendanceEntry)item;
            return entry.Outcome == AttendanceOutcome.Enrolled
                ? "-"
                : (entry.Similarity * 100f).ToString("0.0") + "%";
        })
        {
            Color = _ => Theme.TextSecondary
        });

        _results.AddColumn(new TableColumn("Time", 68,
            item => ((AttendanceEntry)item).SeenUtc.ToLocalTime().ToString("HH:mm:ss"))
        {
            Color = _ => Theme.TextSecondary
        });

        _results.AddAction(new TableAction(Icons.Edit, "Name this person",
            item => NameEntry((AttendanceEntry)item))
        {
            IsVisible = item => ((AttendanceEntry)item).Outcome != AttendanceOutcome.Recognised
        });
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
        const int statusHeight = 182;

        var leftWidth = (int)(area.Width * 0.47);

        _previewCard.SetBounds(area.X, area.Y, leftWidth, area.Height - statusHeight - gap);
        _statusCard.SetBounds(area.X, area.Bottom - statusHeight, leftWidth, statusHeight);
        _resultsCard.SetBounds(area.X + leftWidth + gap, area.Y, area.Width - leftWidth - gap, area.Height);

        LayoutStatus();
        PlaceTitleBarControls(_start, _cancel, _cameraPicker);
    }

    private void LayoutStatus()
    {
        var content = _statusCard.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var y = content.Y;

        _progress.SetBounds(content.X, y, content.Width, 30);
        y += 36;

        _stage.SetBounds(content.X, y, content.Width, 18);
        y += 22;

        _counts.SetBounds(content.X, y, content.Width, 18);
        y += 22;

        _readiness.SetBounds(content.X, y, content.Width, 32);
    }

    public override void OnActivated()
    {
        LoadCameras();
        Services.Streams.FrameReady += OnFrameReady;
        UpdateReadiness();
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;

        // A sweep drives the camera, so leaving the screen stops it rather than
        // letting it run unattended with nothing reporting on it.
        _cancellation?.Cancel();
        DisposeDevice();
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll()
            .Where(c => c.Enabled && c.PtzSupported)
            .ToList();

        _cameraPicker.Items.Clear();

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
            _camera = null;
            _preview.Camera = null;
        }
    }

    private void SelectCamera()
    {
        var index = _cameraPicker.SelectedIndex;
        if (index < 0 || index >= _cameras.Count)
        {
            return;
        }

        _camera = _cameras[index];
        _preview.Camera = _camera;
        _preview.Status = _camera.Status;
        _preview.Invalidate();

        ConnectAsync(_camera);
    }

    private async void ConnectAsync(CameraDevice camera)
    {
        DisposeDevice();
        SetStage("Connecting to " + camera.IpAddress + "...");

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
                SetStage("The camera returned no media profiles.");
                return;
            }

            _device = device;
            _ptz = new OnvifPtzClient(device)
            {
                ProfileToken = profile.Token,
                VideoSourceToken = profile.VideoSourceToken
            };

            _node = await _ptz.GetNodeAsync(profile.PtzNodeToken).ConfigureAwait(true);

            SetStage(_node.SupportsAbsoluteMove
                ? "Ready. The sweep will cover " + Services.Settings.SweepArcDegrees.ToString("0") + " degrees."
                : "This camera does not support absolute positioning.");
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            SetStage("Camera control unavailable: " + ex.Message);
        }

        UpdateReadiness();
    }

    private async void StartSweep()
    {
        if (_camera == null || _ptz == null)
        {
            ShowError("Choose a camera first.");
            return;
        }

        var problem = Services.AttendanceSweeps.Validate(_camera, _node);
        if (problem != null)
        {
            ShowError(problem);
            return;
        }

        if (!Confirm(
                "The camera will move by itself for up to a minute and register everyone it finds.\n\n" +
                "Start the attendance sweep on " + _camera.DisplayName + "?",
                "Automatic Attendance"))
        {
            return;
        }

        var options = BuildOptions();

        _cancellation = new CancellationTokenSource();
        _start.Enabled = false;
        _cancel.Enabled = true;
        _results.SetRows(Enumerable.Empty<object>());

        Services.AttendanceSweeps.Progress += OnSweepProgress;

        try
        {
            _session = await Services.AttendanceSweeps.RunAsync(
                _camera,
                _ptz,
                _node!,
                options,
                Services.Auth.CurrentUser?.Username ?? "unknown",
                _cancellation.Token).ConfigureAwait(true);

            ShowResults(_session);
        }
        catch (Exception ex)
        {
            ShowError("The sweep could not be completed: " + ex.Message);
        }
        finally
        {
            Services.AttendanceSweeps.Progress -= OnSweepProgress;
            _cancellation?.Dispose();
            _cancellation = null;

            _start.Enabled = true;
            _cancel.Enabled = false;
        }
    }

    private SweepOptions BuildOptions()
    {
        var settings = Services.Settings;

        return new SweepOptions
        {
            ArcDegrees = settings.SweepArcDegrees,
            FieldOfViewDegrees = settings.SweepFieldOfViewDegrees,
            OverlapFraction = settings.SweepOverlapFraction,
            Zoom = settings.SweepZoom,
            SettleMilliseconds = settings.SweepSettleMs,
            FramesPerStop = settings.SweepFramesPerStop,
            ClusterCosine = settings.SweepClusterCosine,
            EnrolUnknownFaces = settings.SweepEnrolUnknown
        };
    }

    private void OnSweepProgress(object? sender, SweepProgress progress)
    {
        RunOnUi(() =>
        {
            _progress.Percent = progress.Percent;
            _progress.ValueText = progress.StopIndex + " / " + progress.StopCount;

            _stage.Text = progress.Stage;
            _counts.Text = progress.FacesAccepted + " face(s) kept, " +
                           progress.FacesRejected + " rejected";

            _progress.Invalidate();
            _stage.Invalidate();
            _counts.Invalidate();
        });
    }

    private void ShowResults(AttendanceSession session)
    {
        _results.SetRows(session.Entries.Cast<object>());

        _resultsCard.TitleSuffix = session.PeopleFound + " present, " +
                                   session.NewlyEnrolled + " new";
        _resultsCard.Invalidate();

        _progress.Percent = 100f;
        _stage.Text = session.StatusText + " in " + session.Duration.TotalSeconds.ToString("0") + " s";

        if (!string.IsNullOrEmpty(session.Error))
        {
            ShowError(session.Error!);
        }
        else if (session.Status == SweepStatus.Completed)
        {
            Services.LogEvent(
                EventKind.System,
                "Attendance sweep on " + session.CameraName + ": " +
                session.PeopleFound + " present, " + session.NewlyEnrolled + " newly registered.");
        }

        UpdateReadiness();
    }

    /// <summary>
    /// Names a provisional identity created by the sweep, turning it into an
    /// ordinary enrolment. This is the step that makes automatic registration
    /// useful rather than a pile of anonymous records.
    /// </summary>
    private void NameEntry(AttendanceEntry entry)
    {
        if (string.IsNullOrEmpty(entry.PersonId))
        {
            ShowError("This person was not registered, so there is nothing to name.");
            return;
        }

        var typed = Forms.Prompt.Show(this, "Name", "Name this person", entry.Name);
        var name = typed ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var record = Services.MongoFaces.GetAll()
            .FirstOrDefault(r => r.DocumentId == entry.PersonId);

        if (record == null)
        {
            ShowError("That record is no longer in the database.");
            return;
        }

        try
        {
            Services.MongoFaces.Confirm(record, name.Trim(), FaceGroup.Employee);
            Services.FaceRecognition.ReloadGallery();

            entry.Name = name.Trim();
            entry.Group = FaceGroup.Employee;
            entry.Outcome = AttendanceOutcome.Recognised;

            if (_session != null)
            {
                Services.AttendanceStore.Save(_session);
            }

            _results.Invalidate();
        }
        catch (Exception ex)
        {
            ShowError("The name could not be saved: " + ex.Message);
        }
    }

    private void UpdateReadiness()
    {
        var parts = new List<string>();

        if (!Services.Settings.AttendanceEnabled)
        {
            parts.Add("Attendance is switched off in Settings.");
        }
        else if (!Services.Mongo.IsConnected)
        {
            parts.Add("Database: " + Services.Mongo.Describe());
        }
        else
        {
            parts.Add("Database: " + Services.Mongo.DatabaseName);
        }

        if (!Services.FaceRecognition.IsReady)
        {
            parts.Add("Face models not loaded.");
        }

        var incompatible = Services.FaceRecognition.IncompatibleRecords;
        if (incompatible > 0)
        {
            parts.Add(incompatible + " enrolment(s) were made with a different alignment setting and will not match.");
        }

        _readiness.Text = string.Join("  ", parts);
        _readiness.ForeColor = Services.Mongo.IsConnected && Services.FaceRecognition.IsReady
            ? Theme.TextMuted
            : Theme.Warning;

        _readiness.Invalidate();
    }

    private void SetStage(string text)
    {
        _stage.Text = text;
        _stage.Invalidate();
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (_camera != null && frame.CameraId == _camera.Id)
        {
            _preview.SetFrame(frame.Image);
        }
    }

    private void DisposeDevice()
    {
        _ptz = null;
        _node = null;
        _device?.Dispose();
        _device = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            DisposeDevice();
        }

        base.Dispose(disposing);
    }
}
