using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CMS.App;
using CMS.Licensing;

namespace CMS.KeyGen;

/// <summary>
/// The vendor tool that issues licences.
///
/// This holds the private signing key and is never distributed to customers.
/// The application it licenses carries only the matching public key, so it can
/// check a licence but cannot create one.
/// </summary>
public sealed class KeyGenForm : Form
{
    private LicenseKeyPair? _keyPair;

    // Signing key
    private readonly Label _keyState = new Label();
    private readonly TextBox _publicKey = new TextBox();
    private readonly Button _createKey = new Button { Text = "Create Signing Key" };
    private readonly Button _importKey = new Button { Text = "Import..." };
    private readonly Button _backupKey = new Button { Text = "Back Up..." };
    private readonly Button _installKey = new Button { Text = "Install Into CMS Folder..." };
    private readonly Button _copyPublic = new Button { Text = "Copy Public Key" };

    // Licence details
    private readonly TextBox _customer = new TextBox();
    private readonly TextBox _licenseId = new TextBox();
    private readonly ComboBox _edition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _maxCameras = new NumericUpDown { Minimum = 1, Maximum = 1024 };
    private readonly ComboBox _term = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _expiry = new DateTimePicker { Format = DateTimePickerFormat.Short };
    private readonly TextBox _machine = new TextBox();
    private readonly Button _useThisMachine = new Button { Text = "This PC" };
    private readonly TextBox _notes = new TextBox();
    private readonly CheckedListBox _features = new CheckedListBox { CheckOnClick = true };

    // Output
    private readonly Button _generate = new Button { Text = "Generate Licence" };
    private readonly Button _saveLicense = new Button { Text = "Save .lic..." };
    private readonly Button _copyLicense = new Button { Text = "Copy Key" };
    private readonly Button _verify = new Button { Text = "Verify" };
    private readonly TextBox _output = new TextBox();
    private readonly Label _status = new Label();

    private License? _generated;
    private bool _loading;

    public KeyGenForm()
    {
        Text = "Camera Management System - Licence Generator";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 720);
        Size = new Size(1120, 780);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        DoubleBuffered = true;

        BuildLayout();
        WireEvents();
        LoadSigningKey();
        ResetForm();
    }

    // ---- Layout ----

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = Theme.Background
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 208f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        root.Controls.Add(BuildKeyPanel(), 0, 0);
        root.SetColumnSpan(root.GetControlFromPosition(0, 0), 2);

        root.Controls.Add(BuildDetailsPanel(), 0, 1);
        root.Controls.Add(BuildOutputPanel(), 1, 1);

        Controls.Add(root);
    }

    private Control BuildKeyPanel()
    {
        var panel = NewCard("1.  Signing Key");

        _keyState.SetBounds(16, 44, 700, 20);
        _keyState.ForeColor = Theme.TextSecondary;
        _keyState.Font = Theme.Small;

        var caption = new Label
        {
            Text = "Public key - this is what the application embeds",
            ForeColor = Theme.TextMuted,
            Font = Theme.Caption
        };
        caption.SetBounds(16, 70, 500, 16);

        StyleField(_publicKey);
        _publicKey.SetBounds(16, 90, 660, 46);
        _publicKey.Multiline = true;
        _publicKey.ReadOnly = true;
        _publicKey.ScrollBars = ScrollBars.Vertical;

        foreach (var button in new[] { _createKey, _importKey, _backupKey, _copyPublic, _installKey })
        {
            StyleButton(button, button == _createKey);
        }

        panel.Controls.AddRange(new Control[]
        {
            _keyState, caption, _publicKey, _createKey, _importKey, _backupKey, _copyPublic, _installKey
        });

        // Laid out from the right edge on every resize. Fixed coordinates
        // assumed a panel width the table layout does not guarantee, and the
        // last button in each row fell off the card.
        panel.Resize += (s, e) =>
        {
            const int buttonWidth = 130;
            const int gap = 8;
            var right = panel.Width - 16;

            var x = right - buttonWidth;
            foreach (var button in new[] { _backupKey, _importKey, _createKey })
            {
                button.SetBounds(x, 44, buttonWidth, 30);
                x -= buttonWidth + gap;
            }

            var rowLeft = _createKey.Left;
            _copyPublic.SetBounds(rowLeft, 82, buttonWidth, 30);
            _installKey.SetBounds(rowLeft + buttonWidth + gap, 82,
                right - rowLeft - buttonWidth - gap, 30);

            _publicKey.SetBounds(16, 90, Math.Max(120, rowLeft - 32), 46);
            _keyState.Width = Math.Max(200, rowLeft - 32);
        };

        return panel;
    }

    private Control BuildDetailsPanel()
    {
        var panel = NewCard("2.  Licence Details");
        panel.Margin = new Padding(0, 12, 8, 0);

        var y = 48;

        AddRow(panel, "Customer", _customer, ref y);
        AddRow(panel, "Licence ID", _licenseId, ref y);

        _edition.Items.AddRange(LicenseEditions.All.Cast<object>().ToArray());
        AddRow(panel, "Edition", _edition, ref y);

        AddRow(panel, "Maximum Cameras", _maxCameras, ref y);

        _term.Items.AddRange(new object[] { "30 days", "90 days", "1 year", "3 years", "Perpetual", "Custom date" });
        AddRow(panel, "Term", _term, ref y);
        AddRow(panel, "Expires", _expiry, ref y);

        AddRow(panel, "Locked To", _machine, ref y, _useThisMachine);

        var hint = new Label
        {
            Text = "Leave blank for a licence that runs on any computer.",
            ForeColor = Theme.TextMuted,
            Font = Theme.Caption
        };
        hint.SetBounds(150, y - 4, 400, 16);
        panel.Controls.Add(hint);
        y += 18;

        AddRow(panel, "Notes", _notes, ref y);

        var featuresCaption = new Label
        {
            Text = "Features",
            ForeColor = Theme.TextSecondary,
            Font = Theme.Small
        };
        featuresCaption.SetBounds(16, y + 6, 130, 20);

        _features.SetBounds(150, y, 330, 132);
        _features.BackColor = Theme.Input;
        _features.ForeColor = Theme.TextPrimary;
        _features.BorderStyle = BorderStyle.FixedSingle;
        _features.Font = Theme.Body;

        foreach (var feature in LicenseFeatures.All)
        {
            _features.Items.Add(feature.Value);
        }

        panel.Controls.Add(featuresCaption);
        panel.Controls.Add(_features);

        return panel;
    }

    private Control BuildOutputPanel()
    {
        var panel = NewCard("3.  Issued Licence");
        panel.Margin = new Padding(8, 12, 0, 0);

        StyleButton(_generate, true);

        foreach (var button in new[] { _saveLicense, _copyLicense, _verify })
        {
            StyleButton(button, false);
        }

        StyleField(_output);
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Font = new Font("Consolas", 8.5f);

        _status.ForeColor = Theme.TextMuted;
        _status.Font = Theme.Small;

        panel.Controls.AddRange(new Control[] { _generate, _saveLicense, _copyLicense, _verify, _output, _status });

        // Two rows: the one primary action, then the three that act on its
        // result. Four across did not fit the column at any sensible width.
        panel.Resize += (s, e) =>
        {
            var width = Math.Max(240, panel.Width - 32);

            _generate.SetBounds(16, 44, width, 32);

            var third = (width - 16) / 3;
            var x = 16;

            foreach (var button in new[] { _saveLicense, _copyLicense, _verify })
            {
                button.SetBounds(x, 84, third, 30);
                x += third + 8;
            }

            _output.SetBounds(16, 126, width, Math.Max(120, panel.Height - 186));
            _status.SetBounds(16, _output.Bottom + 8, width, 44);
        };

        return panel;
    }

    private static Panel NewCard(string title)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(1) };

        var heading = new Label
        {
            Text = title,
            ForeColor = Theme.TextPrimary,
            Font = Theme.MediumBold,
            AutoSize = true
        };
        heading.SetBounds(16, 14, 300, 22);

        panel.Controls.Add(heading);
        panel.Paint += (s, e) =>
        {
            Theme.Smooth(e.Graphics);
            Theme.DrawRounded(e.Graphics, new Rectangle(0, 0, panel.Width - 1, panel.Height - 1),
                Theme.CardRadius, Theme.Border);
        };

        return panel;
    }

    private static void AddRow(Control parent, string label, Control field, ref int y, Control? trailing = null)
    {
        var caption = new Label
        {
            Text = label,
            ForeColor = Theme.TextSecondary,
            Font = Theme.Small
        };
        caption.SetBounds(16, y + 5, 130, 20);

        var width = trailing == null ? 330 : 242;
        field.SetBounds(150, y, width, 26);

        if (field is TextBox box)
        {
            StyleField(box);
        }
        else if (field is ComboBox combo)
        {
            combo.BackColor = Theme.Input;
            combo.ForeColor = Theme.TextPrimary;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = Theme.Body;
        }
        else if (field is NumericUpDown numeric)
        {
            numeric.BackColor = Theme.Input;
            numeric.ForeColor = Theme.TextPrimary;
            numeric.BorderStyle = BorderStyle.FixedSingle;
            numeric.Font = Theme.Body;
        }
        else if (field is DateTimePicker picker)
        {
            picker.CalendarMonthBackground = Theme.Panel;
            picker.CalendarForeColor = Theme.TextPrimary;
            picker.Font = Theme.Body;
        }

        parent.Controls.Add(caption);
        parent.Controls.Add(field);

        if (trailing != null)
        {
            StyleButton(trailing, false);
            trailing.SetBounds(400, y - 1, 80, 28);
            parent.Controls.Add(trailing);
        }

        y += 34;
    }

    private static void StyleField(TextBox box)
    {
        box.BackColor = Theme.Input;
        box.ForeColor = Theme.TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = Theme.Body;
    }

    private static void StyleButton(Control control, bool primary)
    {
        if (control is not Button button)
        {
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Theme.Accent : Theme.Panel;
        button.ForeColor = primary ? Theme.TextOnAccent : Theme.TextPrimary;
        button.Font = primary ? Theme.BodyBold : Theme.Body;
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.CardHover;
        button.Cursor = Cursors.Hand;
    }

    // ---- Behaviour ----

    private void WireEvents()
    {
        _createKey.Click += (s, e) => CreateSigningKey();
        _importKey.Click += (s, e) => ImportSigningKey();
        _backupKey.Click += (s, e) => BackUpSigningKey();
        _installKey.Click += (s, e) => InstallPublicKey();
        _copyPublic.Click += (s, e) => CopyToClipboard(_publicKey.Text, "Public key copied.");

        _edition.SelectedIndexChanged += (s, e) => ApplyEdition();
        _term.SelectedIndexChanged += (s, e) => ApplyTerm();
        _useThisMachine.Click += (s, e) => _machine.Text = MachineFingerprint.Current;

        _generate.Click += (s, e) => Generate();
        _saveLicense.Click += (s, e) => SaveLicense();
        _copyLicense.Click += (s, e) => CopyToClipboard(_output.Text, "Licence copied.");
        _verify.Click += (s, e) => VerifyCurrent();
    }

    private void ResetForm()
    {
        _loading = true;

        _licenseId.Text = "CMS-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                          Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        _edition.SelectedItem = LicenseEditions.Standard;
        _term.SelectedItem = "1 year";
        _expiry.Value = DateTime.Today.AddYears(1);

        _loading = false;

        ApplyEdition();
        ApplyTerm();
        UpdateButtons();
    }

    private void LoadSigningKey()
    {
        if (SigningKeyStore.TryLoad(out var keyPair, out var error))
        {
            _keyPair = keyPair;
            _publicKey.Text = keyPair!.PublicKeyCompact;
            _keyState.Text = "Signing key loaded from " + SigningKeyStore.Path_;
            _keyState.ForeColor = Theme.Online;
        }
        else
        {
            _keyPair = null;
            _publicKey.Text = string.Empty;
            _keyState.Text = error + "  Create one, or import an existing key.";
            _keyState.ForeColor = Theme.Warning;
        }

        UpdateButtons();
    }

    private void CreateSigningKey()
    {
        if (SigningKeyStore.Exists &&
            MessageBox.Show(
                this,
                "A signing key already exists." + Environment.NewLine + Environment.NewLine +
                "Replacing it means every licence already issued with the old key will stop working, " +
                "and every installation will need a new public key and a new licence." +
                Environment.NewLine + Environment.NewLine + "Replace it?",
                "Replace Signing Key",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var keyPair = LicenseKeyPair.Create();
        SigningKeyStore.Save(keyPair);

        _keyPair = keyPair;
        _publicKey.Text = keyPair.PublicKeyCompact;
        _keyState.Text = "New signing key created and stored at " + SigningKeyStore.Path_;
        _keyState.ForeColor = Theme.Online;

        UpdateButtons();

        MessageBox.Show(
            this,
            "A signing key has been created." + Environment.NewLine + Environment.NewLine +
            "Back it up now and keep the backup safe. If it is lost you cannot issue any further " +
            "licences for installations already using this public key." + Environment.NewLine + Environment.NewLine +
            "The key is protected for your Windows account on this computer only.",
            "Signing Key Created",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ImportSigningKey()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Signing key|*.key;*.xml|All files|*.*",
            Title = "Import signing key"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var keyPair = SigningKeyStore.ImportPrivateKey(picker.FileName);
            SigningKeyStore.Save(keyPair);

            _keyPair = keyPair;
            _publicKey.Text = keyPair.PublicKeyCompact;
            _keyState.Text = "Signing key imported from " + picker.FileName;
            _keyState.ForeColor = Theme.Online;

            UpdateButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "That file is not a usable signing key: " + ex.Message,
                "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BackUpSigningKey()
    {
        if (_keyPair == null)
        {
            return;
        }

        using var picker = new SaveFileDialog
        {
            Filter = "Signing key|*.key",
            FileName = "cms-signing-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".key",
            Title = "Back up signing key"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SigningKeyStore.ExportPrivateKey(_keyPair, picker.FileName);

        MessageBox.Show(
            this,
            "The backup is NOT encrypted. Store it somewhere only you can reach." + Environment.NewLine +
            Environment.NewLine + "Anyone holding this file can issue licences for your product.",
            "Backup Written",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Writes license.pub next to a CMS installation, which is how an
    /// unconfigured build learns which key to trust.
    /// </summary>
    private void InstallPublicKey()
    {
        if (_keyPair == null)
        {
            return;
        }

        using var picker = new OpenFileDialog
        {
            Filter = "CMS executable|CameraManagementSystem.exe|All files|*.*",
            Title = "Select the installed CameraManagementSystem.exe"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(picker.FileName);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            var target = Path.Combine(folder, "license.pub");
            File.WriteAllText(target, _keyPair.PublicKeyCompact, new System.Text.UTF8Encoding(false));

            MessageBox.Show(
                this,
                "Public key written to:" + Environment.NewLine + target + Environment.NewLine + Environment.NewLine +
                "This is fine for setting the product up. For a release build, paste the key into " +
                "LicensePublicKey.Embedded and rebuild, so it cannot be swapped by replacing a file.",
                "Public Key Installed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The key could not be written: " + ex.Message,
                "Install", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyEdition()
    {
        if (_loading || _edition.SelectedItem is not string edition)
        {
            return;
        }

        var granted = new HashSet<string>(LicenseEditions.FeaturesFor(edition), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < LicenseFeatures.All.Length; i++)
        {
            _features.SetItemChecked(i, granted.Contains(LicenseFeatures.All[i].Key));
        }

        _maxCameras.Value = Math.Min(_maxCameras.Maximum, LicenseEditions.CamerasFor(edition));

        if (edition == LicenseEditions.Trial)
        {
            _term.SelectedItem = "30 days";
        }
    }

    private void ApplyTerm()
    {
        if (_loading || _term.SelectedItem is not string term)
        {
            return;
        }

        _expiry.Enabled = term == "Custom date";

        switch (term)
        {
            case "30 days": _expiry.Value = DateTime.Today.AddDays(30); break;
            case "90 days": _expiry.Value = DateTime.Today.AddDays(90); break;
            case "1 year": _expiry.Value = DateTime.Today.AddYears(1); break;
            case "3 years": _expiry.Value = DateTime.Today.AddYears(3); break;
            case "Perpetual": break;
        }
    }

    private void Generate()
    {
        if (_keyPair == null)
        {
            SetStatus("Create or import a signing key first.", Theme.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_customer.Text))
        {
            SetStatus("Enter a customer name.", Theme.Warning);
            _customer.Focus();
            return;
        }

        var perpetual = (_term.SelectedItem as string) == "Perpetual";

        var license = new License
        {
            Id = _licenseId.Text.Trim(),
            Customer = _customer.Text.Trim(),
            Edition = _edition.SelectedItem as string ?? LicenseEditions.Standard,
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = perpetual ? null : _expiry.Value.Date.ToUniversalTime().AddDays(1).AddSeconds(-1),
            MachineFingerprint = MachineFingerprint.Normalise(_machine.Text).Length == 0
                ? string.Empty
                : _machine.Text.Trim(),
            MaxCameras = (int)_maxCameras.Value,
            Notes = _notes.Text.Trim(),
            Features = new HashSet<string>(SelectedFeatures(), StringComparer.OrdinalIgnoreCase)
        };

        LicenseSigner.Sign(license, _keyPair.PrivateKeyXml);

        _generated = license;
        _output.Text = LicenseCodec.Write(license).Replace("\n", Environment.NewLine);

        SetStatus(
            "Issued " + license.Id + " for " + license.Customer + ".  " +
            (license.IsPerpetual ? "Perpetual." : "Expires " + license.ExpiryText + ".") + "  " +
            (license.IsNodeLocked ? "Locked to one computer." : "Runs on any computer."),
            Theme.Online);

        UpdateButtons();
    }

    private IEnumerable<string> SelectedFeatures()
    {
        for (var i = 0; i < LicenseFeatures.All.Length; i++)
        {
            if (_features.GetItemChecked(i))
            {
                yield return LicenseFeatures.All[i].Key;
            }
        }
    }

    private void SaveLicense()
    {
        if (_generated == null)
        {
            return;
        }

        using var picker = new SaveFileDialog
        {
            Filter = "Licence file|*.lic",
            FileName = _generated.Id + ".lic",
            Title = "Save licence"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(picker.FileName, LicenseCodec.Write(_generated), new System.Text.UTF8Encoding(false));
        SetStatus("Saved to " + picker.FileName, Theme.Online);
    }

    /// <summary>
    /// Verifies the issued licence against the public key alone, which is
    /// exactly what the application will do. Machine binding is not enforced
    /// here, because a licence is usually issued for a different computer.
    /// </summary>
    private void VerifyCurrent()
    {
        if (_keyPair == null || _output.TextLength == 0)
        {
            return;
        }

        var status = LicenseVerifier.VerifyText(_output.Text, _keyPair.PublicKeyXml, checkMachine: false);

        if (status.IsValid)
        {
            var license = status.License!;
            SetStatus(
                "Verified against the public key.  " + license.Customer + ", " + license.Edition + ", " +
                license.MaxCameras + " cameras, " + license.Features.Count + " feature(s), " +
                (license.IsPerpetual ? "perpetual" : "expires " + license.ExpiryText) + ".",
                Theme.Online);
        }
        else
        {
            SetStatus("Verification failed: " + status.Message, Theme.Offline);
        }
    }

    private void CopyToClipboard(string text, string confirmation)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus(confirmation, Theme.Online);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("The clipboard was not available.", Theme.Warning);
        }
    }

    private void UpdateButtons()
    {
        var hasKey = _keyPair != null;

        _backupKey.Enabled = hasKey;
        _installKey.Enabled = hasKey;
        _copyPublic.Enabled = hasKey;
        _generate.Enabled = hasKey;

        _saveLicense.Enabled = _generated != null;
        _copyLicense.Enabled = _generated != null;
        _verify.Enabled = _generated != null && hasKey;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
