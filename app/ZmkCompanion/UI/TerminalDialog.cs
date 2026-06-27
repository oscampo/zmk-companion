using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Small terminal-style window for sending text to the keyboard display.
// Runs on the UI thread — calls BleService.SendAsync directly, no pipes.
internal sealed class TerminalDialog : Form
{
    private readonly BleService _ble;
    private readonly Action? _onSend;
    private readonly RichTextBox _output;
    private readonly TextBox _input;
    private readonly List<string> _history = [];
    private int _histIdx = -1;

    private static readonly Color BgDark    = Color.FromArgb(22, 22, 22);
    private static readonly Color BgInput   = Color.FromArgb(36, 36, 36);
    private static readonly Color FgNormal  = Color.FromArgb(212, 212, 212);
    private static readonly Color FgPrompt  = Color.FromArgb(87, 166, 74);
    private static readonly Color FgOk      = Color.FromArgb(87, 166, 74);
    private static readonly Color FgErr     = Color.FromArgb(220, 80, 80);
    private static readonly Color FgHint    = Color.FromArgb(120, 120, 120);

    internal TerminalDialog(BleService ble, Action? onSend = null)
    {
        _ble    = ble;
        _onSend = onSend;

        var font = new Font("Consolas", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

        Text            = "ZMK Companion — Send text";
        ClientSize      = new Size(520, 300);
        MinimumSize     = new Size(380, 220);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgDark;

        _output = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BackColor   = BgDark,
            ForeColor   = FgNormal,
            Font        = font,
            BorderStyle = BorderStyle.None,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            Padding     = new Padding(6, 4, 0, 0),
        };

        // Separator line between output and input
        var sep = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 1,
            BackColor = Color.FromArgb(60, 60, 60),
        };

        // Input row: "> " prompt label + TextBox
        var inputPanel = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 36,
            BackColor = BgInput,
            Padding   = new Padding(8, 0, 8, 0),
        };

        var promptLabel = new Label
        {
            Text      = ">",
            ForeColor = FgPrompt,
            Font      = font,
            AutoSize  = true,
            Dock      = DockStyle.Left,
            Padding   = new Padding(0, 8, 4, 0),
        };

        _input = new TextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = BgInput,
            ForeColor   = FgNormal,
            Font        = font,
            BorderStyle = BorderStyle.None,
        };
        _input.KeyDown += OnInputKeyDown;

        inputPanel.Controls.Add(_input);
        inputPanel.Controls.Add(promptLabel);

        Controls.Add(_output);
        Controls.Add(sep);
        Controls.Add(inputPanel);

        Shown += (_, _) =>
        {
            Append("Type text and press Enter to send.  Use \\n for line breaks.\n", FgHint);
            _input.Focus();
        };
    }

    // ── Input handling ────────────────────────────────────────────────────────

    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.SuppressKeyPress = true;
                await SendAsync(_input.Text.Trim());
                break;
            case Keys.Up:
                e.SuppressKeyPress = true;
                NavigateHistory(-1);
                break;
            case Keys.Down:
                e.SuppressKeyPress = true;
                NavigateHistory(1);
                break;
        }
    }

    private async Task SendAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        _history.Insert(0, text);
        _histIdx = -1;
        _input.Clear();

        Append($"> {text}\n", FgPrompt);

        _onSend?.Invoke();

        string message = text.Replace("\\n", "\n");
        try
        {
            bool ok = await _ble.SendAsync(message);
            Append(ok ? "  Sent.\n" : "  ERR: not connected or send failed\n", ok ? FgOk : FgErr);
        }
        catch (Exception ex)
        {
            Append($"  ERR: {ex.Message}\n", FgErr);
        }
    }

    private void NavigateHistory(int dir)
    {
        if (_history.Count == 0) return;
        _histIdx = Math.Clamp(_histIdx + dir, -1, _history.Count - 1);
        _input.Text = _histIdx >= 0 ? _history[_histIdx] : "";
        _input.SelectionStart = _input.Text.Length;
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private void Append(string text, Color color)
    {
        _output.SelectionStart  = _output.TextLength;
        _output.SelectionLength = 0;
        _output.SelectionColor  = color;
        _output.AppendText(text);
        _output.ScrollToCaret();
    }
}
