using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Core.Models;

namespace CMS.App.Pages;

/// <summary>
/// Application configuration, grouped into categories on the left. Everything
/// is stored in the local database; Save applies changes to the running
/// services immediately.
/// </summary>
public sealed class SettingsPage : PageBase
{
    private readonly ListBox _categories = new ListBox();
    private readonly CardPanel _card = new CardPanel();

    private readonly FlatButton _save = new FlatButton { Variant = ButtonVariant.Primary, Text = "Save", Size = new Size(104, 32) };
    private readonly FlatButton _defaults = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Restore Defaults", Size = new Size(146, 32) };

    // General
    private readonly DarkTextBox _systemName = new DarkTextBox();
    private readonly DarkComboBox _language = new DarkComboBox();
    private readonly DarkComboBox _timeZone = new DarkComboBox();
    private readonly DarkComboBox _dateFormat = new DarkComboBox();
    private readonly DarkComboBox _autoLogout = new DarkComboBox();
    private readonly DarkCheckBox _startWithSystem = new DarkCheckBox { Text = "Start automatically with Windows" };

    // Network
    private readonly DarkTextBox _discoveryTimeout = new DarkTextBox { Suffix = "sec" };
    private readonly DarkTextBox _rtspTimeout = new DarkTextBox { Suffix = "sec" };
    private readonly DarkCheckBox _preferTcp = new DarkCheckBox { Text = "Prefer TCP transport for RTSP" };

    // Storage
    private readonly DarkTextBox _storageRoot = new DarkTextBox();
    private readonly FlatButton _browseStorage = new FlatButton { Variant = ButtonVariant.Secondary, Icon = Icons.Folder, Size = new Size(38, 32) };
    private readonly DarkTextBox _retentionDays = new DarkTextBox { Suffix = "days" };
    private readonly DarkTextBox _maxStorage = new DarkTextBox { Suffix = "GB" };
    private readonly DarkCheckBox _overwrite = new DarkCheckBox { Text = "Overwrite oldest footage when full" };

    // AI
    private readonly DarkTextBox _objectModelPath = new DarkTextBox();
    private readonly DarkTextBox _faceDetectorPath = new DarkTextBox();
    private readonly DarkTextBox _faceEmbedderPath = new DarkTextBox();
    private readonly DarkTextBox _detectionInterval = new DarkTextBox { Suffix = "ms" };
    private readonly DarkSlider _faceThreshold = new DarkSlider { Minimum = 0.3, Maximum = 0.95 };
    private readonly DarkLabel _faceThresholdValue = new DarkLabel { Font = Theme.SmallBold, ForeColor = Theme.TextPrimary, Alignment = ContentAlignment.MiddleRight };
    private readonly DarkCheckBox _useGpu = new DarkCheckBox { Text = "Use GPU acceleration when available" };
    private readonly FlatButton _reloadModels = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Reload Models", Size = new Size(132, 32) };

    private readonly Dictionary<string, List<Control>> _groups = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Label, Control Field)>> _rows =
        new Dictionary<string, List<(string, Control)>>(StringComparer.Ordinal);

    private string _category = "General";
    private bool _loading;

    public SettingsPage()
    {
        PageTitle = "Settings";

        ConfigureCategoryList();
        ConfigureFields();
        BuildGroups();

        _save.Click += (s, e) => Save();
        _defaults.Click += (s, e) => RestoreDefaults();
        _browseStorage.Click += (s, e) => BrowseStorage();
        _reloadModels.Click += (s, e) => ReloadModels();
        _faceThreshold.ValueChanged += (s, e) =>
        {
            _faceThresholdValue.Text = _faceThreshold.Value.ToString("0.00");
            _faceThresholdValue.Invalidate();
        };

        Controls.Add(_categories);
        Controls.Add(_card);

        PlaceTitleBarControls(_save, _defaults);
    }

    private void ConfigureCategoryList()
    {
        _categories.BackColor = Theme.Panel;
        _categories.ForeColor = Theme.TextSecondary;
        _categories.BorderStyle = BorderStyle.None;
        _categories.Font = Theme.Body;
        _categories.ItemHeight = 34;
        _categories.DrawMode = DrawMode.OwnerDrawFixed;
        _categories.IntegralHeight = false;

        _categories.Items.AddRange(new object[] { "General", "Network", "Storage", "AI Models" });
        _categories.SelectedIndex = 0;

        _categories.DrawItem += OnDrawCategory;
        _categories.SelectedIndexChanged += (s, e) =>
        {
            _category = _categories.SelectedItem as string ?? "General";
            _card.Title = _category + " Settings";
            ShowGroup();
        };
    }

    private void OnDrawCategory(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        var g = e.Graphics;
        Theme.Smooth(g);

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var brush = new SolidBrush(Theme.Panel))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        var row = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 2, e.Bounds.Width - 12, e.Bounds.Height - 4);

        if (selected)
        {
            Theme.FillRounded(g, row, 6, Theme.Accent);
        }

        Theme.DrawText(
            g,
            _categories.Items[e.Index].ToString() ?? string.Empty,
            selected ? Theme.BodyBold : Theme.Body,
            selected ? Theme.TextOnAccent : Theme.TextSecondary,
            new Rectangle(row.X + 12, row.Y, row.Width - 16, row.Height));
    }

    private void ConfigureFields()
    {
        _language.Items.AddRange(new object[] { "English", "한국어", "日本語", "中文" });
        _dateFormat.Items.AddRange(new object[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy" });
        _autoLogout.Items.AddRange(new object[] { "Never", "15 minutes", "30 minutes", "60 minutes" });

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            _timeZone.Items.Add(zone.DisplayName);
        }
    }

    private void BuildGroups()
    {
        _rows["General"] = new List<(string, Control)>
        {
            ("System Name", _systemName),
            ("Language", _language),
            ("Time Zone", _timeZone),
            ("Date Format", _dateFormat),
            ("Auto Logout", _autoLogout)
        };

        _rows["Network"] = new List<(string, Control)>
        {
            ("ONVIF Discovery Timeout", _discoveryTimeout),
            ("RTSP Connect Timeout", _rtspTimeout)
        };

        _rows["Storage"] = new List<(string, Control)>
        {
            ("Recording Folder", _storageRoot),
            ("Retention", _retentionDays),
            ("Maximum Size", _maxStorage)
        };

        _rows["AI Models"] = new List<(string, Control)>
        {
            ("Object Model File", _objectModelPath),
            ("Face Detector File", _faceDetectorPath),
            ("Face Recognition File", _faceEmbedderPath),
            ("Detection Interval", _detectionInterval)
        };

        _groups["General"] = _rows["General"].Select(r => r.Field).Concat(new Control[] { _startWithSystem }).ToList();
        _groups["Network"] = _rows["Network"].Select(r => r.Field).Concat(new Control[] { _preferTcp }).ToList();
        _groups["Storage"] = _rows["Storage"].Select(r => r.Field).Concat(new Control[] { _browseStorage, _overwrite }).ToList();
        _groups["AI Models"] = _rows["AI Models"].Select(r => r.Field)
            .Concat(new Control[] { _faceThreshold, _faceThresholdValue, _useGpu, _reloadModels }).ToList();

        foreach (var group in _groups.Values)
        {
            foreach (var control in group)
            {
                control.Visible = false;
                _card.Controls.Add(control);
            }
        }

        _card.Title = "General Settings";
        _card.TitleIcon = Icons.Settings;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        if (area.Width <= 0)
        {
            return;
        }

        const int gap = 12;
        const int categoryWidth = 170;

        _categories.SetBounds(area.X, area.Y, categoryWidth, area.Height);
        _card.SetBounds(area.X + categoryWidth + gap, area.Y, area.Width - categoryWidth - gap, area.Height);

        LayoutGroup();
        PlaceTitleBarControls(_save, _defaults);
    }

    private void ShowGroup()
    {
        foreach (var pair in _groups)
        {
            var visible = pair.Key == _category;

            foreach (var control in pair.Value)
            {
                control.Visible = visible;
            }
        }

        LayoutGroup();
        _card.Invalidate();
        Invalidate();
    }

    private void LayoutGroup()
    {
        var content = _card.ContentBounds;
        if (content.Width <= 0)
        {
            return;
        }

        const int labelWidth = 170;
        const int rowGap = 46;

        var fieldX = content.X + labelWidth;
        var fieldWidth = Math.Max(120, Math.Min(420, content.Width - labelWidth - 60));
        var y = content.Y;

        List<(string Label, Control Field)> rows;
        if (!_rows.TryGetValue(_category, out rows))
        {
            return;
        }

        foreach (var row in rows)
        {
            var width = fieldWidth;

            // The storage path leaves room for its browse button.
            if (ReferenceEquals(row.Field, _storageRoot))
            {
                width -= 44;
            }

            row.Field.SetBounds(fieldX, y, width, 32);

            if (ReferenceEquals(row.Field, _storageRoot))
            {
                _browseStorage.SetBounds(fieldX + width + 6, y, 38, 32);
            }

            y += rowGap;
        }

        switch (_category)
        {
            case "General":
                _startWithSystem.SetBounds(fieldX, y, fieldWidth, 24);
                break;

            case "Network":
                _preferTcp.SetBounds(fieldX, y, fieldWidth, 24);
                break;

            case "Storage":
                _overwrite.SetBounds(fieldX, y, fieldWidth, 24);
                break;

            case "AI Models":
                y += 12;
                _faceThresholdValue.SetBounds(fieldX + fieldWidth - 50, y - 20, 50, 18);
                _faceThreshold.SetBounds(fieldX, y, fieldWidth, 22);
                y += 36;
                _useGpu.SetBounds(fieldX, y, fieldWidth, 24);
                y += 34;
                _reloadModels.SetBounds(fieldX, y, 132, 32);
                break;
        }
    }

    public override void OnActivated()
    {
        LoadSettings();
        ShowGroup();
    }

    private void LoadSettings()
    {
        _loading = true;

        var settings = Services.Settings;

        _systemName.Text = settings.SystemName;
        SelectOrAdd(_language, settings.Language);
        SelectTimeZone(settings.TimeZoneId);
        SelectOrAdd(_dateFormat, settings.DateFormat);

        _autoLogout.SelectedIndex = settings.AutoLogoutMinutes switch
        {
            0 => 0,
            15 => 1,
            60 => 3,
            _ => 2
        };

        _startWithSystem.Checked = settings.StartWithSystem;

        _discoveryTimeout.Text = settings.OnvifDiscoveryTimeoutSeconds.ToString();
        _rtspTimeout.Text = settings.RtspConnectTimeoutSeconds.ToString();
        _preferTcp.Checked = settings.PreferTcpTransport;

        _storageRoot.Text = settings.StorageRoot;
        _retentionDays.Text = settings.RetentionDays.ToString();
        _maxStorage.Text = settings.MaxStorageGb.ToString();
        _overwrite.Checked = settings.OverwriteWhenFull;

        _objectModelPath.Text = settings.ObjectModelPath;
        _faceDetectorPath.Text = settings.FaceDetectorPath;
        _faceEmbedderPath.Text = settings.FaceEmbedderPath;
        _detectionInterval.Text = settings.DetectionIntervalMs.ToString();
        _faceThreshold.Value = settings.FaceMatchThreshold;
        _faceThresholdValue.Text = settings.FaceMatchThreshold.ToString("0.00");
        _useGpu.Checked = settings.UseGpu;

        _loading = false;
    }

    private static void SelectOrAdd(DarkComboBox combo, string value)
    {
        var index = combo.Items.IndexOf(value);

        if (index < 0 && !string.IsNullOrEmpty(value))
        {
            index = combo.Items.Add(value);
        }

        combo.SelectedIndex = Math.Max(0, index);
    }

    private void SelectTimeZone(string id)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            var index = _timeZone.Items.IndexOf(zone.DisplayName);
            _timeZone.SelectedIndex = index >= 0 ? index : 0;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _timeZone.SelectedIndex = 0;
        }
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        var settings = Services.Settings.Clone();

        settings.SystemName = _systemName.Text.Trim();
        settings.Language = _language.SelectedItem as string ?? settings.Language;
        settings.DateFormat = _dateFormat.SelectedItem as string ?? settings.DateFormat;

        settings.AutoLogoutMinutes = _autoLogout.SelectedIndex switch
        {
            0 => 0,
            1 => 15,
            3 => 60,
            _ => 30
        };

        settings.StartWithSystem = _startWithSystem.Checked;

        var displayName = _timeZone.SelectedItem as string;
        if (!string.IsNullOrEmpty(displayName))
        {
            var zone = TimeZoneInfo.GetSystemTimeZones()
                .FirstOrDefault(z => z.DisplayName == displayName);

            if (zone != null)
            {
                settings.TimeZoneId = zone.Id;
            }
        }

        settings.OnvifDiscoveryTimeoutSeconds = ParseInt(_discoveryTimeout.Text, settings.OnvifDiscoveryTimeoutSeconds);
        settings.RtspConnectTimeoutSeconds = ParseInt(_rtspTimeout.Text, settings.RtspConnectTimeoutSeconds);
        settings.PreferTcpTransport = _preferTcp.Checked;

        var root = _storageRoot.Text.Trim();

        if (!string.IsNullOrEmpty(root))
        {
            try
            {
                Directory.CreateDirectory(root);
                settings.StorageRoot = root;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowError("The recording folder could not be created:\n" + ex.Message);
                return;
            }
        }

        settings.RetentionDays = ParseInt(_retentionDays.Text, settings.RetentionDays);
        settings.MaxStorageGb = ParseInt(_maxStorage.Text, settings.MaxStorageGb);
        settings.OverwriteWhenFull = _overwrite.Checked;

        settings.ObjectModelPath = _objectModelPath.Text.Trim();
        settings.FaceDetectorPath = _faceDetectorPath.Text.Trim();
        settings.FaceEmbedderPath = _faceEmbedderPath.Text.Trim();
        settings.DetectionIntervalMs = Math.Max(50, ParseInt(_detectionInterval.Text, settings.DetectionIntervalMs));
        settings.FaceMatchThreshold = (float)_faceThreshold.Value;

        var gpuChanged = settings.UseGpu != _useGpu.Checked;
        settings.UseGpu = _useGpu.Checked;

        Services.SaveSettings(settings);
        Services.LogEvent(EventKind.System, "Settings updated.");

        if (gpuChanged)
        {
            Services.Analytics.LoadModels(settings);
        }

        MessageBox.Show(this, "Settings saved.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreDefaults()
    {
        if (!Confirm("Restore all settings to their defaults?", "Restore Defaults"))
        {
            return;
        }

        Services.SaveSettings(new AppSettings());
        Services.LogEvent(EventKind.System, "Settings restored to defaults.");

        LoadSettings();
        ShowGroup();
    }

    private void BrowseStorage()
    {
        using var picker = new FolderBrowserDialog { Description = "Select the recording folder" };

        if (Directory.Exists(_storageRoot.Text))
        {
            picker.SelectedPath = _storageRoot.Text;
        }

        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            _storageRoot.Text = picker.SelectedPath;
        }
    }

    private void ReloadModels()
    {
        Save();
        Services.Analytics.LoadModels(Services.Settings);

        var detector = Services.Analytics.Detector;
        var faces = Services.FaceRecognition;

        var message =
            "Object detection: " + (detector.IsReady ? "loaded" : "not loaded - " + detector.LastError) +
            "\nFace detection: " + (faces.DetectorReady ? "loaded" : "not loaded") +
            "\nFace recognition: " + (faces.EmbedderReady ? "loaded" : "not loaded");

        MessageBox.Show(this, message, "AI Models", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, out var value) && value >= 0 ? value : fallback;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        List<(string Label, Control Field)> rows;
        if (!_rows.TryGetValue(_category, out rows) || !_card.Visible)
        {
            return;
        }

        var g = e.Graphics;
        var origin = _card.Location;
        var content = _card.ContentBounds;

        foreach (var row in rows)
        {
            Theme.DrawText(g, row.Label, Theme.Small, Theme.TextSecondary,
                new Rectangle(origin.X + content.X, origin.Y + row.Field.Top, 160, 32));
        }

        if (_category == "AI Models")
        {
            Theme.DrawText(g, "Face Match Threshold", Theme.Small, Theme.TextSecondary,
                new Rectangle(origin.X + content.X, origin.Y + _faceThreshold.Top - 4, 160, 32));
        }
    }
}
