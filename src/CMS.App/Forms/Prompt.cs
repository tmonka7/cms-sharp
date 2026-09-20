using System.Drawing;
using System.Windows.Forms;
using CMS.App.Controls;

namespace CMS.App.Forms;

/// <summary>
/// A themed single-line input dialog, used where WinForms would otherwise need
/// the light-coloured VB InputBox.
/// </summary>
public sealed class Prompt : Form
{
    private readonly DarkTextBox _input = new DarkTextBox();
    private readonly FlatButton _ok = new FlatButton { Variant = ButtonVariant.Primary, Text = "OK", Size = new Size(94, 32) };
    private readonly FlatButton _cancel = new FlatButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Size = new Size(94, 32) };

    private readonly string _label;

    private Prompt(string label, string caption, string initialValue)
    {
        _label = label;

        Text = caption;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Size = new Size(400, 168);
        DoubleBuffered = true;
        KeyPreview = true;

        var titleBar = new TitleBar
        {
            BrandPrefix = caption.ToUpperInvariant() + " ",
            BrandAccent = string.Empty,
            ShowMaximize = false,
            ShowMinimize = false
        };

        _input.Text = initialValue;
        _input.SetBounds(24, titleBar.Height + 40, Width - 48, 32);

        _ok.Location = new Point(Width - 24 - 94, Height - 48);
        _cancel.Location = new Point(_ok.Left - 102, Height - 48);

        _ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        _input.Input.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                DialogResult = DialogResult.OK;
                Close();
                e.SuppressKeyPress = true;
            }
        };

        Controls.Add(titleBar);
        Controls.AddRange(new Control[] { _input, _ok, _cancel });
    }

    public string Value => _input.Text;

    /// <summary>Shows the prompt and returns the entered text, or null if cancelled.</summary>
    public static string? Show(IWin32Window owner, string label, string caption, string initialValue = "")
    {
        using var prompt = new Prompt(label, caption, initialValue);

        prompt.Shown += (s, e) =>
        {
            prompt._input.Focus();
            prompt._input.SelectAll();
        };

        return prompt.ShowDialog(owner) == DialogResult.OK ? prompt.Value : null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);

        using (var brush = new SolidBrush(Theme.Panel))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        Theme.DrawText(g, _label, Theme.Small, Theme.TextSecondary,
            new Rectangle(24, Theme.TitleBarHeight + 16, Width - 48, 20));

        Theme.DrawRounded(g, new Rectangle(0, 0, Width, Height), 0, Theme.BorderStrong);
    }
}
