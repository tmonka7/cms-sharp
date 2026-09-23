using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;
using CMS.Licensing;
using Size = System.Drawing.Size;

namespace CMS.App.Forms;

/// <summary>
/// Shown when the product has no usable licence. It states why, shows the
/// installation code the supplier needs, and takes a licence by paste or by
/// file.
///
/// Everything an operator needs to get unstuck is on this one window, because
/// it is the only window they can reach until a licence is accepted.
/// </summary>
public sealed class LicenseForm : Form
{
    private readonly TitleBar _titleBar = new TitleBar
    {
        BrandPrefix = "LICENCE ",
        BrandAccent = "REQUIRED",
        ShowMaximize = false,
        ShowMinimize = false
    };

    private readonly DarkLabel _headline = new DarkLabel
    {
        Font = Theme.Headline,
        ForeColor = Theme.TextPrimary
    };

    private readonly DarkLabel _advice = new DarkLabel
    {
        Font = Theme.Body,
        ForeColor = Theme.TextSecondary
    };

    private readonly DarkLabel _codeCaption = new DarkLabel
    {
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        Text = "Installation code - send this to your supplier"
    };

    private readonly TextBox _code = new TextBox
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Center
    };

    private readonly FlatButton _copyCode = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = "Copy",
        Size = new Size(84, 30)
    };

    private readonly DarkLabel _pasteCaption = new DarkLabel
    {
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        Text = "Paste your licence key here, or load a licence file"
    };

    private readonly TextBox _licenseText = new TextBox
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly FlatButton _load = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Icon = Icons.Folder,
        Text = "Load File",
        Size = new Size(118, 34)
    };

    private readonly FlatButton _activate = new FlatButton
    {
        Variant = ButtonVariant.Primary,
        Text = "Activate",
        Size = new Size(118, 34)
    };

    private readonly FlatButton _quit = new FlatButton
    {
        Variant = ButtonVariant.Secondary,
        Text = "Exit",
        Size = new Size(96, 34)
    };

    private readonly DarkLabel _status = new DarkLabel
    {
        Font = Theme.Small,
        ForeColor = Theme.Warning
    };

    public LicenseForm(LicenseStatus status)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(620, 560);
        DoubleBuffered = true;
        KeyPreview = true;

        _headline.Text = status.Verdict == LicenseVerdict.Missing
            ? "Activate this installation"
            : "This licence cannot be used";

        _advice.Text = status.Advice;
        _code.Text = MachineFingerprint.Current;

        _code.BackColor = Theme.Input;
        _code.ForeColor = Theme.TextPrimary;
        _code.Font = new Font("Consolas", 11f, FontStyle.Bold);

        _licenseText.BackColor = Theme.Input;
        _licenseText.ForeColor = Theme.TextPrimary;
        _licenseText.Font = new Font("Consolas", 8.5f);

        _copyCode.Click += (s, e) => CopyCode();
        _load.Click += (s, e) => LoadFile();
        _activate.Click += (s, e) => ApplyLicense();
        _quit.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

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
            _headline, _advice, _codeCaption, _code, _copyCode,
            _pasteCaption, _licenseText, _load, _activate, _quit, _status
        });

        Resize += (s, e) => DoLayout();
    }

    /// <summary>The licence accepted here, for the caller to use.</summary>
    public LicenseStatus? Accepted { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DoLayout();
        _licenseText.Focus();
    }

    private void DoLayout()
    {
        const int left = 28;
        var width = Width - (left * 2);
        var y = _titleBar.Bottom + 20;

        _headline.SetBounds(left, y, width, 28);
        y += 34;

        _advice.SetBounds(left, y, width, 52);
        y += 60;

        _codeCaption.SetBounds(left, y, width, 16);
        y += 20;

        _code.SetBounds(left, y, width - 92, 30);
        _copyCode.SetBounds(left + width - 84, y, 84, 30);
        y += 44;

        _pasteCaption.SetBounds(left, y, width, 16);
        y += 20;

        var textHeight = Math.Max(90, Height - y - 110);
        _licenseText.SetBounds(left, y, width, textHeight);
        y += textHeight + 12;

        _status.SetBounds(left, y, width, 20);

        _load.SetBounds(left, Height - 56, 118, 34);
        _quit.SetBounds(Width - left - 96, Height - 56, 96, 34);
        _activate.SetBounds(_quit.Left - 128, Height - 56, 118, 34);
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(_code.Text);
            SetStatus("Installation code copied.", Theme.Online);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("The clipboard was not available.", Theme.Warning);
        }
    }

    private void LoadFile()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Licence file|*.lic;*.txt|All files|*.*",
            Title = "Load licence"
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _licenseText.Text = File.ReadAllText(picker.FileName);
            ApplyLicense();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            SetStatus("The file could not be read: " + ex.Message, Theme.Offline);
        }
    }

    private void ApplyLicense()
    {
        var text = _licenseText.Text.Trim();

        if (text.Length == 0)
        {
            SetStatus("Paste a licence key, or load a licence file.", Theme.Warning);
            return;
        }

        var publicKey = LicensePublicKey.Resolve();
        var status = LicenseVerifier.VerifyText(text, publicKey);

        if (!status.IsValid)
        {
            SetStatus(status.Message + "  " + status.Advice, Theme.Offline);
            return;
        }

        // Only stored once it has been proven to verify on this machine, so a
        // licence that cannot work here is never left behind to fail at the
        // next start.
        try
        {
            LicenseStore.Write(text);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            SetStatus("The licence is valid but could not be saved: " + ex.Message, Theme.Offline);
            return;
        }

        Accepted = status;
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

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }
}
