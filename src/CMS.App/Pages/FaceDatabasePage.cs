using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.App.Forms;
using CMS.Core.Models;

namespace CMS.App.Pages;

/// <summary>
/// The enrolled-identity register. Faces are enrolled from a photo, embedded
/// once and stored locally; matching later compares against these vectors.
/// </summary>
public sealed class FaceDatabasePage : PageBase
{
    private readonly CardPanel _card = new CardPanel();
    private readonly DataTable _table = new DataTable { Dock = DockStyle.Fill };

    private readonly FlatButton _add = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Icon = Icons.Add,
        Text = "Add Face",
        Size = new Size(112, 32)
    };

    private readonly FlatButton _import = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Icon = Icons.Import,
        Text = "Import Folder",
        Size = new Size(130, 32)
    };

    private readonly FlatButton _refresh = new FlatButton
    {
        Variant = ButtonVariant.Icon,
        Icon = Icons.Refresh,
        Size = new Size(32, 32)
    };

    public FaceDatabasePage()
    {
        PageTitle = "Face Database";

        _card.Title = "Registered Faces";
        _card.TitleIcon = Icons.People;
        _card.Controls.Add(_table);

        BuildColumns();

        _add.Click += (s, e) => AddFace();
        _import.Click += (s, e) => ImportFolder();
        _refresh.Click += (s, e) => Reload();
        _table.RowDoubleClicked += (s, item) => EditFace((FaceRecord)item);

        Controls.Add(_card);
        PlaceTitleBarControls(_add, _import, _refresh);
    }

    private void BuildColumns()
    {
        _table.EmptyText = "No faces enrolled. Use Add Face to register someone.";
        _table.RowHeight = 46;

        _table.AddColumn(new TableColumn("No.", 44, item => ((FaceRecord)item).Id.ToString())
        {
            Color = _ => Theme.TextMuted
        });

        _table.AddColumn(new TableColumn("Photo", 44, _ => string.Empty)
        {
            Image = item => ((FaceRecord)item).Thumbnail
        });

        _table.AddColumn(new TableColumn("Name", 170, item => ((FaceRecord)item).Name)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.5f,
            Font = Theme.BodyBold
        });

        _table.AddColumn(new TableColumn("Group", 110, item => ((FaceRecord)item).GroupText)
        {
            Pill = true,
            Color = item => GroupColor(((FaceRecord)item).Group)
        });

        _table.AddColumn(new TableColumn("Registered Date", 130, item => ((FaceRecord)item).RegisteredText)
        {
            Color = _ => Theme.TextSecondary
        });

        _table.AddColumn(new TableColumn("Note", 160, item => ((FaceRecord)item).Note)
        {
            Sizing = ColumnSizing.Fill,
            Weight = 1.2f,
            Color = _ => Theme.TextMuted
        });

        _table.AddColumn(new TableColumn("Status", 84, item => ((FaceRecord)item).Enabled ? "Active" : "Disabled")
        {
            Dot = item => ((FaceRecord)item).Enabled ? Theme.Online : Theme.TextMuted,
            Color = item => ((FaceRecord)item).Enabled ? Theme.Online : Theme.TextMuted
        });

        _table.AddAction(new TableAction(Icons.Edit, "Edit", item => EditFace((FaceRecord)item)));

        _table.AddAction(new TableAction(Icons.Delete, "Delete", item => DeleteFace((FaceRecord)item))
        {
            HoverColor = Theme.Offline
        });
    }

    private static Color GroupColor(FaceGroup group) => group switch
    {
        FaceGroup.Employee => Theme.Info,
        FaceGroup.Blacklist => Theme.Offline,
        FaceGroup.Unknown => Theme.TextMuted,
        _ => Theme.Violet
    };

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var area = ContentArea;
        _card.SetBounds(area.X, area.Y, area.Width, area.Height);
        PlaceTitleBarControls(_add, _import, _refresh);
    }

    public override void OnActivated() => Reload();

    private void Reload()
    {
        var faces = Services.Faces.GetAll();
        _table.SetRows(faces.Cast<object>());

        _card.TitleSuffix = faces.Count + " identities";
        _card.Invalidate();
    }

    private void AddFace()
    {
        if (!Services.FaceRecognition.IsReady)
        {
            ShowError(
                "The face models are not loaded, so a photo cannot be enrolled.\n\n" +
                "Place the detector and recognition ONNX files in the Models folder, " +
                "then restart the application.");
            return;
        }

        using var dialog = new AddFaceForm();

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Services.FaceRecognition.ReloadGallery();
            Reload();
        }
    }

    private void EditFace(FaceRecord record)
    {
        using var dialog = new AddFaceForm(record);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        Services.FaceRecognition.ReloadGallery();
        Reload();
    }

    private void DeleteFace(FaceRecord record)
    {
        if (!Confirm("Remove " + record.Name + " from the face database?", "Delete Face"))
        {
            return;
        }

        Services.Faces.Delete(record.Id);
        Services.FaceRecognition.ReloadGallery();
        Services.LogEvent(EventKind.System, "Face record removed: " + record.Name);

        Reload();
    }

    /// <summary>
    /// Bulk enrolment: every image in a folder becomes an identity named after
    /// its file, which is how an existing badge-photo export is loaded.
    /// </summary>
    private void ImportFolder()
    {
        if (!Services.FaceRecognition.IsReady)
        {
            ShowError("The face models are not loaded, so photos cannot be enrolled.");
            return;
        }

        using var picker = new FolderBrowserDialog
        {
            Description = "Select a folder of face photos. Each file name becomes the person's name."
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };

        var files = Directory
            .GetFiles(picker.SelectedPath)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            ShowError("That folder contains no supported image files.");
            return;
        }

        var imported = 0;
        var skipped = new List<string>();

        foreach (var file in files)
        {
            try
            {
                using var image = OpenCvSharp.Cv2.ImRead(file, OpenCvSharp.ImreadModes.Color);

                if (image.Empty())
                {
                    skipped.Add(Path.GetFileName(file));
                    continue;
                }

                var record = Services.FaceRecognition.Enroll(
                    image,
                    Path.GetFileNameWithoutExtension(file),
                    FaceGroup.Employee,
                    "Imported from " + Path.GetFileName(file));

                if (record == null)
                {
                    skipped.Add(Path.GetFileName(file));
                }
                else
                {
                    imported++;
                }
            }
            catch (Exception ex) when (ex is OpenCvSharp.OpenCVException or IOException)
            {
                skipped.Add(Path.GetFileName(file));
            }
        }

        Services.FaceRecognition.ReloadGallery();
        Services.LogEvent(EventKind.System, imported + " face(s) imported.");
        Reload();

        var message = imported + " of " + files.Count + " photo(s) enrolled.";
        if (skipped.Count > 0)
        {
            message += "\n\nNo usable face was found in:\n" + string.Join("\n", skipped.Take(10));

            if (skipped.Count > 10)
            {
                message += "\n... and " + (skipped.Count - 10) + " more.";
            }
        }

        MessageBox.Show(this, message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
