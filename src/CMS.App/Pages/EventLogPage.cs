using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;

namespace CMS.App.Pages;

/// <summary>
/// The searchable event history, with filters by kind, camera and date range,
/// and a paged table. Rows carry the snapshot captured at the time.
/// </summary>
public sealed class EventLogPage : PageBase
{
    private readonly CardPanel _card = new CardPanel();
    private readonly DataTable _table = new DataTable { Dock = DockStyle.Fill };

    private readonly DarkComboBox _kindFilter = new DarkComboBox { Width = 160 };
    private readonly DarkComboBox _cameraFilter = new DarkComboBox { Width = 170 };
    private readonly DateTimePicker _fromDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _toDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };

    private readonly FlatButton _search = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Search,
        Text = "Search",
        Size = new Size(100, 32)
    };

    private readonly FlatButton _export = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Icon = Icons.Export,
        Text = "Export",
        Size = new Size(96, 32)
    };

    private readonly Panel _pager = new Panel { Height = 40, BackColor = Theme.Card };
    private readonly FlatButton _prev = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.ChevronLeft, Size = new Size(30, 28) };
    private readonly FlatButton _next = new FlatButton { Variant = ButtonVariant.Icon, Icon = Icons.ChevronRight, Size = new Size(30, 28) };
    private readonly DarkLabel _pageLabel = new DarkLabel { Font = Theme.Small, ForeColor = Theme.TextSecondary, Alignment = ContentAlignment.MiddleCenter };

    private const int PageSize = 100;

    private List<CameraDevice> _cameras = new List<CameraDevice>();
    private int _page;
    private int _totalCount;

    public EventLogPage()
    {
        PageTitle = "Event Log";

        _card.Title = "Events";
        _card.TitleIcon = Icons.EventLog;
        _card.Controls.Add(_table);
        _card.Controls.Add(_pager);

        _pager.Dock = DockStyle.Bottom;
        _pager.Controls.AddRange(new Control[] { _prev, _pageLabel, _next });
        _pager.Resize += (s, e) => LayoutPager();

        BuildColumns();

        _kindFilter.Items.AddRange(new object[]
        {
            "All Events", "Face Recognized", "Object Detected",
            "Camera Offline", "Camera Online", "Motion Detected", "Recording", "System"
        });
        _kindFilter.SelectedIndex = 0;

        StyleDatePicker(_fromDate);
        StyleDatePicker(_toDate);
        _fromDate.Value = DateTime.Today;
        _toDate.Value = DateTime.Today;

        _search.Click += (s, e) => { _page = 0; Reload(); };
        _export.Click += (s, e) => Export();
        _prev.Click += (s, e) => { if (_page > 0) { _page--; Reload(); } };
        _next.Click += (s, e) => { if ((_page + 1) * PageSize < _totalCount) { _page++; Reload(); } };
        _table.RowDoubleClicked += (s, item) => ShowSnapshot((EventEntry)item);

        Controls.Add(_card);
        PlaceTitleBarControls(_search, _export, _toDate, _fromDate, _cameraFilter, _kindFilter);
    }

    private static void StyleDatePicker(DateTimePicker picker)
    {
        // The native picker cannot be fully themed; a flat calendar keeps it
        // from looking out of place.
        picker.CalendarMonthBackground = Theme.Panel;
        picker.CalendarForeColor = Theme.TextPrimary;
        picker.CalendarTitleBackColor = Theme.Accent;
        picker.CalendarTitleForeColor = Color.White;
        picker.CalendarTrailingForeColor = Theme.TextMuted;
    }

    private void BuildColumns()
    {
        _table.EmptyText = "No events match these filters";
        _table.RowHeight = 40;

        _table.AddColumn(new TableColumn("Time", 150, item => ((EventEntry)item).DateTimeText)
        {
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Type", 150, item => ((EventEntry)item).TypeText)
        {
            Glyph = item => EventGlyph(((EventEntry)item).Kind),
            Color = item => Theme.SeverityColor(((EventEntry)item).Severity)
        });

        _table.AddColumn(new TableColumn("Message", 240, item => ((EventEntry)item).Message)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 2f
        });

        _table.AddColumn(new TableColumn("Score", 74, item => ((EventEntry)item).ScoreText)
        {
            Color = _ => Theme.TextMuted
        });

        _table.AddColumn(new TableColumn("Camera", 160, item => ((EventEntry)item).CameraName)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.2f,
            Color = _ => Theme.TextSecondary
        });
    }

    private static string EventGlyph(EventKind kind) => kind switch
    {
        EventKind.FaceRecognized => Icons.Face,
        EventKind.ObjectDetected => Icons.Detection,
        EventKind.CameraOffline => Icons.Cancel,
        EventKind.CameraOnline => Icons.Accept,
        EventKind.MotionDetected => Icons.Warning,
        EventKind.Recording => Icons.Video,
        _ => Icons.Info
    };

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        _card.SetBounds(area.X, area.Y, area.Width, area.Height);

        PlaceTitleBarControls(_search, _export, _toDate, _fromDate, _cameraFilter, _kindFilter);
        LayoutPager();
    }

    private void LayoutPager()
    {
        var y = (_pager.Height - 28) / 2;

        _prev.SetBounds(_pager.Width - 200, y, 30, 28);
        _pageLabel.SetBounds(_pager.Width - 166, y, 120, 28);
        _next.SetBounds(_pager.Width - 42, y, 30, 28);
    }

    public override void OnActivated()
    {
        LoadCameras();
        Reload();
    }

    private void LoadCameras()
    {
        _cameras = Services.Cameras.GetAll();

        var selected = _cameraFilter.SelectedIndex;

        _cameraFilter.Items.Clear();
        _cameraFilter.Items.Add("All Cameras");

        foreach (var camera in _cameras)
        {
            _cameraFilter.Items.Add(camera.DisplayName);
        }

        _cameraFilter.SelectedIndex = selected >= 0 && selected < _cameraFilter.Items.Count ? selected : 0;
    }

    private void Reload()
    {
        var fromUtc = _fromDate.Value.Date.ToUniversalTime();
        var toUtc = _toDate.Value.Date.AddDays(1).AddSeconds(-1).ToUniversalTime();

        EventKind? kind = _kindFilter.SelectedIndex <= 0
            ? (EventKind?)null
            : _kindFilter.SelectedIndex switch
            {
                1 => EventKind.FaceRecognized,
                2 => EventKind.ObjectDetected,
                3 => EventKind.CameraOffline,
                4 => EventKind.CameraOnline,
                5 => EventKind.MotionDetected,
                6 => EventKind.Recording,
                _ => EventKind.System
            };

        int? cameraId = null;
        var index = _cameraFilter.SelectedIndex;
        if (index > 0 && index - 1 < _cameras.Count)
        {
            cameraId = _cameras[index - 1].Id;
        }

        _totalCount = Services.Events.Count(fromUtc, toUtc, kind, cameraId);

        var events = Services.Events.Query(
            fromUtc: fromUtc,
            toUtc: toUtc,
            kind: kind,
            cameraId: cameraId,
            limit: PageSize,
            offset: _page * PageSize);

        _table.SetRows(events.Cast<object>());

        var pages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));

        _pageLabel.Text = "Page " + (_page + 1) + " of " + pages;
        _pageLabel.Invalidate();

        _prev.Enabled = _page > 0;
        _next.Enabled = _page + 1 < pages;

        _card.TitleSuffix = "Total: " + _totalCount;
        _card.Invalidate();
    }

    private void ShowSnapshot(EventEntry entry)
    {
        var snapshot = entry.Snapshot ?? Services.Events.GetSnapshot(entry.Id);

        if (snapshot == null || snapshot.Length == 0)
        {
            ShowError("This event has no stored snapshot.");
            return;
        }

        using var viewer = new SnapshotViewer(entry, snapshot);
        viewer.ShowDialog(this);
    }

    /// <summary>Writes the current filter result to CSV for offline review.</summary>
    private void Export()
    {
        using var picker = new SaveFileDialog
        {
            Filter = "CSV file|*.csv",
            FileName = "events_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(picker.FileName, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Time,Type,Severity,Camera,Message,Score");

            foreach (var item in _table.Rows.Cast<EventEntry>())
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Quote(item.DateTimeText),
                    Quote(item.TypeText),
                    Quote(item.Severity.ToString()),
                    Quote(item.CameraName),
                    Quote(item.Message),
                    Quote(item.ScoreText)
                }));
            }

            MessageBox.Show(this, "Exported to:\n" + picker.FileName, "Export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("The file could not be written: " + ex.Message);
        }
    }

    private static string Quote(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}

/// <summary>A simple dark viewer for an event snapshot.</summary>
internal sealed class SnapshotViewer : Form
{
    private readonly PictureBox _picture = new PictureBox
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Theme.VideoBackground
    };

    public SnapshotViewer(EventEntry entry, byte[] snapshot)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        Size = new Size(640, 520);
        KeyPreview = true;

        var titleBar = new TitleBar
        {
            BrandPrefix = entry.TypeText.ToUpperInvariant() + " ",
            BrandAccent = string.Empty,
            Subtitle = entry.DateTimeText + "   " + entry.CameraName,
            ShowMaximize = false,
            ShowMinimize = false
        };

        try
        {
            using var stream = new MemoryStream(snapshot);
            _picture.Image = Image.FromStream(stream);
        }
        catch (ArgumentException)
        {
            // Leave the box empty rather than failing to open.
        }

        Controls.Add(_picture);
        Controls.Add(titleBar);

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _picture.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
