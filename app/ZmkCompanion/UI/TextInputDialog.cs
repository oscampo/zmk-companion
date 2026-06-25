using System.Drawing;
using System.Windows.Forms;

namespace ZmkCompanion.UI;

// Minimal single-field input dialog used for "Send text" and custom pomodoro.
sealed class TextInputDialog : Form
{
    private readonly TextBox _input;
    public string Value => _input.Text;

    public TextInputDialog(string title, string prompt, string initial)
    {
        Text            = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(380, 120);

        var label = new Label
        {
            Text     = prompt,
            Location = new Point(12, 12),
            Size     = new Size(356, 32),
            AutoSize = false,
        };

        _input = new TextBox
        {
            Text     = initial,
            Location = new Point(12, 48),
            Size     = new Size(356, 23),
            Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var ok = new Button
        {
            Text         = "Send",
            DialogResult = DialogResult.OK,
            Location     = new Point(216, 82),
            Size         = new Size(72, 26),
        };
        var cancel = new Button
        {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location     = new Point(296, 82),
            Size         = new Size(72, 26),
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([label, _input, ok, cancel]);

        Shown += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }
}
