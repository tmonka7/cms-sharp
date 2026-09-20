using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;
using CMS.Core.Streaming;
using OpenCvSharp;
using Size = System.Drawing.Size;

namespace CMS.App.Pages;

/// <summary>
/// Reviews recorded footage from local disk: pick a camera and a date, scrub the
/// timeline, and jump straight to the events captured during the recording.
/// </summary>
public sealed class PlaybackPage : PageBase
{
    private readonly DarkComboBox _cameraPicker = new DarkComboBox { Width = 200 };
    private readonly DateTimePicker _datePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly FlatButton _search = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Search,
        Text = "Search",
        Size = new Size(100, 32)
    };

    private readonly VideoView _video = new VideoView { ShowOverlayHeader = false, ShowDetections = false, ShowFaces = false };
    private readonly TimelineBar _timeline = new TimelineBar();
    private readonly CardPanel _eventsCard = new CardPanel();
    private readonly DataTable _events = new DataTable { Dock = DockStyle.Fill };

    private readonly Panel _transport = new Panel { Height = 44, BackColor = Theme.Card };
    private readonly FlatButton _playPause = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Play, IconSize = 11f, Size = new Size(34, 34) };
    private readonly FlatButton _stop = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Stop, Size = new Size(34, 34) };
    private readonly FlatButton _stepBack = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Previous, Size = new Size(34, 34) };
    private readonly FlatButton _stepForward = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.Next, Size = new Size(34, 34) };
    private readonly DarkComboBox _speed = new DarkComboBox { Width = 84 };
    private readonly DarkLabel _position = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary };

    private readonly PlaybackPlayer _player = new PlaybackPlayer();

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private List<RecordingSegment> _segments = new List<RecordingSegment>();
    private RecordingSegment? _currentSegment;
    private CameraDevice? _camera;

    public PlaybackPage()
    {
        PageTitle = "Playback";

        _eventsCard.Title = "Events";
        _eventsCard.TitleIcon = Icons.EventLog;
        _eventsCard.Controls.Add(_events);

        BuildEventColumns();

        _speed.Items.AddRange(new object[] { "0.5x", "1x", "2x", "4x" });
        _speed.SelectedIndex = 1;
        _speed.SelectedIndexChanged += (s, e) => ApplySpeed();

        _playPause.Click += (s, e) => TogglePlay();
        _stop.Click += (s, e) => StopPlayback();
        _stepForward.Click += (s, e) => _player.StepFrame();
        _stepBack.Click += (s, e) => _player.Seek(Math.Max(0, _player.PositionSeconds - 5));

        _search.Click += (s, e) => LoadDay();
        _cameraPicker.SelectedIndexChanged += (s, e) => LoadDay();
        _datePicker.ValueChanged += (s, e) => LoadDay();

        _timeline.Seeked += OnTimelineSeeked;
        _events.RowDoubleClicked += (s, item) => JumpToEvent((EventEntry)item);

        _player.FrameReady += OnPlayerFrame;
        _player.PositionChanged += OnPlayerPosition;
        _player.PlaybackEnded += OnPlaybackEnded;

        StyleDatePicker(_datePicker);

        _transport.Controls.AddRange(new Control[]
        {
            _playPause, _stop, _stepBack, _stepForward, _speed, _position
        });
        _transport.Resize += (s, e) => LayoutTransport();

        Controls.AddRange(new Control[] { _video, _transport, _timeline, _eventsCard });
        PlaceTitleBarControls(_search, _datePicker, _cameraPicker);
    }

    private static void StyleDatePicker(DateTimePicker picker)
    {
        picker.CalendarMonthBackground = Theme.Panel;
        picker.CalendarForeColor = Theme.TextPrimary;
        picker.CalendarTitleBackColor = Theme.Accent;
        picker.CalendarTitleForeColor = Color.White;
        picker.CalendarTrailingForeColor = Theme.TextMuted;
    }

    private void BuildEventColumns()
    {
        _events.EmptyText = "No events for this day";
        _events.RowHeight = 42;

        _events.AddColumn(new TableColumn("Time", 70, item => ((EventEntry)item).TimeText)
        {
            Color = _ => Theme.TextMuted
        });

        _events.AddColumn(new TableColumn("Snapshot", 42, _ => string.Empty)
        {
            Image = item => ((EventEntry)item).Snapshot
        });

        _events.AddColumn(new TableColumn("Event", 150, item => ((EventEntry)item).Message)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.4f,
            Glyph = item => ((EventEntry)item).Kind == EventKind.FaceRecognized ? Icons.Face : Icons.Detection,
            Color = item => Theme.SeverityColor(((EventEntry)item).Severity)
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
        const int transportHeight = 44;
        const int timelineHeight = 64;

        var eventsWidth = Math.Min(300, Math.Max(220, area.Width / 4));
        var leftWidth = area.Width - eventsWidth - gap;
        var videoHeight = area.Height - transportHeight - timelineHeight - (gap * 2);

        _video.SetBounds(area.X, area.Y, leftWidth, Math.Max(80, videoHeight));
        _transport.SetBounds(area.X, _video.Bottom + gap, leftWidth, transportHeight);
        _timeline.SetBounds(area.X, _transport.Bottom + gap, leftWidth, timelineHeight);
        _eventsCard.SetBounds(area.X + leftWidth + gap, area.Y, eventsWidth, area.Height);

        LayoutTransport();
        PlaceTitleBarControls(_search, _datePicker, _cameraPicker);
    }

    private void LayoutTransport()
    {
        var y = (_transport.Height - 34) / 2;
        var x = 12;

        foreach (var button in new[] { _playPause, _stop, _stepBack, _stepForward })
        {
            button.SetBounds(x, y, 34, 34);
            x += 38;
        }

        _speed.SetBounds(x + 8, y + 2, 84, 30);
        _position.SetBounds(_transport.Width - 220, 0, 208, _transport.Height);
        _position.Alignment = ContentAlignment.MiddleRight;
    }

    public override void OnActivated()
    {
        LoadCameras();
        DoLayout();
    }

    public override void OnDeactivated() => StopPlayback();

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll();

        var selected = _cameraPicker.SelectedIndex;

        _cameraPicker.Items.Clear();
        foreach (var camera in _cameras)
        {
            _cameraPicker.Items.Add(camera.DisplayName);
        }

        if (_cameraPicker.Items.Count > 0)
        {
            _cameraPicker.SelectedIndex = selected >= 0 && selected < _cameraPicker.Items.Count ? selected : 0;
        }
    }

    private void LoadDay()
    {
        var index = _cameraPicker.SelectedIndex;
        if (index < 0 || index >= _cameras.Count)
        {
            return;
        }

        _camera = _cameras[index];
        _video.Camera = _camera;
        PageSubtitle = _camera.DisplayName + "  -  " + _datePicker.Value.ToString("yyyy-MM-dd");

        var dayStart = _datePicker.Value.Date;
        var dayEnd = dayStart.AddDays(1);

        _segments = Services.Recordings.Query(_camera.Id, dayStart.ToUniversalTime(), dayEnd.ToUniversalTime());

        _timeline.WindowStart = dayStart;
        _timeline.WindowLength = TimeSpan.FromHours(24);
        _timeline.SetSegments(_segments);
        _timeline.Position = _segments.Count > 0 ? _segments[0].Start : dayStart;

        var events = Services.Events.Query(
            fromUtc: dayStart.ToUniversalTime(),
            toUtc: dayEnd.ToUniversalTime(),
            cameraId: _camera.Id,
            limit: 300,
            includeSnapshots: true);

        _events.SetRows(events.Cast<object>());
        _eventsCard.TitleSuffix = events.Count.ToString();
        _eventsCard.Invalidate();

        _timeline.SetMarkers(events.Select(item => new PlaybackMarker
        {
            TimestampUtc = item.TimestampUtc,
            Kind = item.Kind,
            Label = item.Message,
            CameraName = item.CameraName,
            Score = item.Score ?? 0
        }));

        StopPlayback();

        if (_segments.Count == 0)
        {
            _video.PlaceholderText = "No footage recorded for this day";
            _video.ClearFrame();
            SetPositionText(TimeSpan.Zero, TimeSpan.Zero);
        }
        else
        {
            OpenSegment(_segments[0], 0);
        }
    }

    /// <summary>Opens a clip and seeks to an offset within it.</summary>
    private bool OpenSegment(RecordingSegment segment, double offsetSeconds)
    {
        if (!_player.Open(segment.FilePath))
        {
            _video.PlaceholderText = "The recording file is missing or unreadable";
            _video.ClearFrame();
            return false;
        }

        _currentSegment = segment;
        _player.Seek(offsetSeconds);
        _player.StepFrame();

        ApplySpeed();
        return true;
    }

    private void OnTimelineSeeked(object? sender, DateTime time)
    {
        var segment = _segments.FirstOrDefault(s => time >= s.Start && time <= s.End);

        if (segment == null)
        {
            return;
        }

        var offset = (time - segment.Start).TotalSeconds;

        if (_currentSegment != null && ReferenceEquals(segment, _currentSegment))
        {
            _player.Seek(offset);

            if (!_player.IsPlaying)
            {
                _player.StepFrame();
            }

            return;
        }

        var wasPlaying = _player.IsPlaying;
        _player.Stop();

        if (OpenSegment(segment, offset) && wasPlaying)
        {
            _player.Play();
            _playPause.Icon = Icons.Pause;
        }
    }

    private void JumpToEvent(EventEntry entry)
    {
        _timeline.Position = entry.Timestamp;
        OnTimelineSeeked(this, entry.Timestamp);
    }

    private void TogglePlay()
    {
        if (_currentSegment == null)
        {
            if (_segments.Count == 0)
            {
                ShowError("There is no footage to play for this day.");
                return;
            }

            OpenSegment(_segments[0], 0);
        }

        if (_player.IsPlaying && !_player.IsPaused)
        {
            _player.Pause();
            _playPause.Icon = Icons.Play;
        }
        else
        {
            _player.Play();
            _playPause.Icon = Icons.Pause;
        }
    }

    private void StopPlayback()
    {
        _player.Stop();
        _playPause.Icon = Icons.Play;
    }

    private void ApplySpeed()
    {
        _player.Speed = _speed.SelectedIndex switch
        {
            0 => 0.5,
            2 => 2.0,
            3 => 4.0,
            _ => 1.0
        };
    }

    private void OnPlayerFrame(object? sender, Mat frame) => _video.SetFrame(frame);

    private void OnPlayerPosition(object? sender, double seconds)
    {
        RunOnUi(() =>
        {
            if (_currentSegment != null)
            {
                _timeline.Position = _currentSegment.Start.AddSeconds(seconds);
            }

            SetPositionText(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(_player.DurationSeconds));
        });
    }

    private void SetPositionText(TimeSpan position, TimeSpan duration)
    {
        _position.Text = Format(position) + " / " + Format(duration);
        _position.Invalidate();
    }

    private static string Format(TimeSpan value)
        => ((int)value.TotalHours).ToString("00") + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00");

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            // Roll on to the next clip so a day plays continuously.
            if (_currentSegment == null)
            {
                return;
            }

            var index = _segments.IndexOf(_currentSegment);

            if (index >= 0 && index + 1 < _segments.Count)
            {
                if (OpenSegment(_segments[index + 1], 0))
                {
                    _player.Play();
                    return;
                }
            }

            _playPause.Icon = Icons.Play;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _player.Dispose();
        }

        base.Dispose(disposing);
    }
}
