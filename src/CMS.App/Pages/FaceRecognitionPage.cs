using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.App.Pages;

/// <summary>
/// Live face recognition: the camera feed with identity boxes, the most recent
/// match shown large, and the recent-match history for the chosen filter.
/// </summary>
public sealed class FaceRecognitionPage : PageBase
{
    private readonly DarkComboBox _cameraFilter = new DarkComboBox { Width = 180 };
    private readonly DarkComboBox _rangeFilter = new DarkComboBox { Width = 130 };
    private readonly FlatButton _search = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Search,
        Text = "Search",
        Size = new Size(104, 32)
    };

    private readonly VideoView _video = new VideoView { ShowDetections = false };
    private readonly CardPanel _matchCard = new CardPanel();
    private readonly CardPanel _historyCard = new CardPanel();

    private readonly PictureBox _matchPhoto = new PictureBox
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Theme.Input
    };

    private readonly DarkLabel _matchName = new DarkLabel { Font = Theme.Title, ForeColor = Theme.TextPrimary };
    private readonly StatusPill _matchState = new StatusPill { Text = "Recognized", PillColor = Theme.Online, Size = new Size(104, 22) };
    private readonly DarkLabel _matchSimilarity = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary };
    private readonly DarkLabel _matchCamera = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary };
    private readonly DarkLabel _matchTime = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary };
    private readonly DarkLabel _modelState = new DarkLabel { Font = Theme.Caption, ForeColor = Theme.TextMuted };

    private readonly DataTable _history = new DataTable { Dock = DockStyle.Fill };

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private CameraDevice? _camera;
    private EventEntry? _lastMatch;

    public FaceRecognitionPage()
    {
        PageTitle = "Face Recognition";

        _matchCard.Title = "Latest Match";
        _matchCard.TitleIcon = Icons.Face;
        _matchCard.Controls.AddRange(new Control[]
        {
            _matchPhoto, _matchName, _matchState, _matchSimilarity, _matchCamera, _matchTime, _modelState
        });

        _historyCard.Title = "Recent Face Events";
        _historyCard.TitleIcon = Icons.EventLog;
        _historyCard.Controls.Add(_history);

        BuildHistoryColumns();

        _rangeFilter.Items.AddRange(new object[] { "Today", "Last 24 hours", "Last 7 days", "All time" });
        _rangeFilter.SelectedIndex = 0;

        _search.Click += (s, e) => LoadHistory();
        _cameraFilter.SelectedIndexChanged += OnCameraFilterChanged;
        _history.RowDoubleClicked += (s, item) => ShowMatch((EventEntry)item);

        Controls.Add(_video);
        Controls.Add(_matchCard);
        Controls.Add(_historyCard);

        PlaceTitleBarControls(_search, _rangeFilter, _cameraFilter);
    }

    private void BuildHistoryColumns()
    {
        _history.EmptyText = "No face events for this filter";
        _history.RowHeight = 40;

        _history.AddColumn(new TableColumn("Time", 74, item => ((EventEntry)item).TimeText)
        {
            Color = _ => Theme.TextMuted
        });

        _history.AddColumn(new TableColumn("Photo", 40, _ => string.Empty)
        {
            Image = item => ((EventEntry)item).Snapshot
        });

        _history.AddColumn(new TableColumn("Name", 140, item => ((EventEntry)item).Message)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.4f,
            Font = Theme.BodyBold
        });

        _history.AddColumn(new TableColumn("Similarity", 90, item => ((EventEntry)item).ScoreText)
        {
            Color = item =>
            {
                var score = ((EventEntry)item).Score ?? 0;
                return score >= 0.9 ? Theme.Online : score >= 0.75 ? Theme.Warning : Theme.TextSecondary;
            }
        });

        _history.AddColumn(new TableColumn("Camera", 140, item => ((EventEntry)item).CameraName)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.2f,
            Color = _ => Theme.TextSecondary
        });
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
        var rightWidth = Math.Min(340, Math.Max(280, area.Width / 3));
        var leftWidth = area.Width - rightWidth - gap;
        var videoHeight = (int)(area.Height * 0.55);

        _video.SetBounds(area.X, area.Y, leftWidth, videoHeight);
        _historyCard.SetBounds(area.X, area.Y + videoHeight + gap, leftWidth, area.Height - videoHeight - gap);
        _matchCard.SetBounds(area.X + leftWidth + gap, area.Y, rightWidth, area.Height);

        LayoutMatchCard();
        PlaceTitleBarControls(_search, _rangeFilter, _cameraFilter);
    }

    private void LayoutMatchCard()
    {
        var content = _matchCard.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        var photoSize = Math.Min(content.Width, 190);

        _matchPhoto.SetBounds(content.X + ((content.Width - photoSize) / 2), content.Y, photoSize, photoSize);

        var y = _matchPhoto.Bottom + 14;

        _matchName.SetBounds(content.X, y, content.Width, 26);
        y += 30;

        _matchState.SetBounds(content.X, y, 104, 22);
        y += 32;

        foreach (var label in new[] { _matchSimilarity, _matchCamera, _matchTime })
        {
            label.SetBounds(content.X, y, content.Width, 20);
            y += 24;
        }

        _modelState.SetBounds(content.X, content.Bottom - 18, content.Width, 16);
    }

    public override void OnActivated()
    {
        LoadCameras();
        LoadHistory();
        UpdateModelState();
        DoLayout();

        Services.Streams.FrameReady += OnFrameReady;
        Services.Analytics.FacesRecognized += OnFacesRecognized;
        Services.Analytics.EventRaised += OnEventRaised;
    }

    public override void OnDeactivated()
    {
        Services.Streams.FrameReady -= OnFrameReady;
        Services.Analytics.FacesRecognized -= OnFacesRecognized;
        Services.Analytics.EventRaised -= OnEventRaised;
    }

    private void UpdateModelState()
    {
        var faces = Services.FaceRecognition;

        _modelState.Text = faces.IsReady
            ? "Models loaded  -  " + faces.GalleryCount + " enrolled identities"
            : "Face models not loaded - " + (faces.LastError ?? "files missing");

        _modelState.ForeColor = faces.IsReady ? Theme.Online : Theme.Warning;
        _modelState.Invalidate();
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll().Where(c => c.Enabled).ToList();

        _cameraFilter.Items.Clear();
        _cameraFilter.Items.Add("All Cameras");

        foreach (var camera in _cameras)
        {
            _cameraFilter.Items.Add(camera.DisplayName);
        }

        _cameraFilter.SelectedIndex = 0;

        _camera = _cameras.FirstOrDefault();
        if (_camera != null)
        {
            AttachCamera(_camera);
        }
    }

    private void AttachCamera(CameraDevice camera)
    {
        _camera = camera;
        _video.Camera = camera;
        _video.Status = camera.Status;

        var frame = Services.Streams.Snapshot(camera.Id);
        if (frame != null)
        {
            using (frame)
            {
                _video.SetFrame(frame);
            }
        }

        var snapshot = Services.Analytics.LatestFor(camera.Id);
        if (snapshot.Faces != null)
        {
            _video.SetFaces(snapshot.Faces.Matches);
        }
    }

    private void OnCameraFilterChanged(object? sender, EventArgs e)
    {
        var index = _cameraFilter.SelectedIndex;

        if (index > 0 && index - 1 < _cameras.Count)
        {
            AttachCamera(_cameras[index - 1]);
        }

        LoadHistory();
    }

    private void LoadHistory()
    {
        DateTime? from = _rangeFilter.SelectedIndex switch
        {
            0 => DateTime.UtcNow.Date,
            1 => DateTime.UtcNow.AddDays(-1),
            2 => DateTime.UtcNow.AddDays(-7),
            _ => null
        };

        int? cameraId = null;
        var index = _cameraFilter.SelectedIndex;
        if (index > 0 && index - 1 < _cameras.Count)
        {
            cameraId = _cameras[index - 1].Id;
        }

        var events = Services.Events.Query(
            fromUtc: from,
            kind: EventKind.FaceRecognized,
            cameraId: cameraId,
            limit: 200,
            includeSnapshots: true);

        _history.SetRows(events.Cast<object>());
        _historyCard.TitleSuffix = events.Count + " matches";
        _historyCard.Invalidate();

        if (_lastMatch == null && events.Count > 0)
        {
            ShowMatch(events[0]);
        }
    }

    private void ShowMatch(EventEntry entry)
    {
        _lastMatch = entry;

        _matchName.Text = entry.Message;
        _matchSimilarity.Text = "Similarity: " + entry.ScoreText;
        _matchCamera.Text = "Camera: " + entry.CameraName;
        _matchTime.Text = "Time: " + entry.DateTimeText;

        var blacklisted = entry.Severity == EventSeverity.Critical;
        _matchState.Text = blacklisted ? "Blacklist" : "Recognized";
        _matchState.PillColor = blacklisted ? Theme.Offline : Theme.Online;

        var snapshot = entry.Snapshot ?? Services.Events.GetSnapshot(entry.Id);

        _matchPhoto.Image?.Dispose();
        _matchPhoto.Image = null;

        if (snapshot != null && snapshot.Length > 0)
        {
            try
            {
                using var stream = new MemoryStream(snapshot);
                _matchPhoto.Image = Image.FromStream(stream);
            }
            catch (ArgumentException)
            {
                // A truncated blob just leaves the placeholder showing.
            }
        }

        foreach (var control in new Control[] { _matchName, _matchState, _matchSimilarity, _matchCamera, _matchTime })
        {
            control.Invalidate();
        }
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (_camera != null && frame.CameraId == _camera.Id)
        {
            _video.SetFrame(frame.Image);
        }
    }

    private void OnFacesRecognized(object? sender, FaceFrameResult result)
    {
        if (_camera == null || result.CameraId != _camera.Id)
        {
            return;
        }

        RunOnUi(() => _video.SetFaces(result.Matches));
    }

    private void OnEventRaised(object? sender, EventEntry entry)
    {
        if (entry.Kind != EventKind.FaceRecognized)
        {
            return;
        }

        RunOnUi(() =>
        {
            ShowMatch(entry);
            LoadHistory();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _matchPhoto.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
