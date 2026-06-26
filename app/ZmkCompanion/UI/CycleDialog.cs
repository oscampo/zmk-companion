using System.Drawing;
using System.Windows.Forms;

namespace ZmkCompanion.UI;

sealed class CycleDialog : Form
{
    private readonly AppSettings _settings;
    private readonly CheckBox _chkClock;
    private readonly CheckBox _chkWeather;
    private readonly CheckBox _chkPomodoro;
    private readonly CheckBox _chkSports;
    private readonly CheckBox _chkText;
    private readonly TextBox  _txtText;
    private readonly ComboBox _cboInterval;

    private static readonly (string Label, int Seconds)[] Intervals =
    [
        ("5 s",  5),
        ("10 s", 10),
        ("30 s", 30),
        ("60 s", 60),
    ];

    public CycleDialog(AppSettings settings)
    {
        _settings = settings;

        Text            = "ZMK Companion - Cycle";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(340, 240);

        Controls.Add(MakeLabel("Items to include:", 12));

        _chkClock    = MakeCheck("Clock",               settings.CycleClock,    32);
        _chkWeather  = MakeCheck("Weather",             settings.CycleWeather,  55);
        _chkPomodoro = MakeCheck("Pomodoro (if active)",settings.CyclePomodoro, 78);
        _chkSports   = MakeCheck("Sports",              settings.CycleSports,  101);

        _chkText = new CheckBox
        {
            Text     = "Text:",
            Checked  = !string.IsNullOrEmpty(settings.CycleCustomText),
            Location = new Point(12, 124),
            Size     = new Size(62, 23),
            AutoSize = false,
        };
        _txtText = new TextBox
        {
            Text     = settings.CycleCustomText,
            Location = new Point(78, 124),
            Size     = new Size(250, 23),
            Enabled  = !string.IsNullOrEmpty(settings.CycleCustomText),
        };
        _chkText.CheckedChanged += (_, _) => _txtText.Enabled = _chkText.Checked;
        Controls.AddRange([_chkClock, _chkWeather, _chkPomodoro, _chkSports, _chkText, _txtText]);

        Controls.Add(MakeLabel("Interval per item:", 157));
        _cboInterval = new ComboBox
        {
            Location      = new Point(12, 177),
            Size          = new Size(120, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var (label, _) in Intervals)
            _cboInterval.Items.Add(label);
        int sel = Array.FindIndex(Intervals, t => t.Seconds == settings.CycleIntervalSeconds);
        _cboInterval.SelectedIndex = sel >= 0 ? sel : 1; // default 10 s
        Controls.Add(_cboInterval);

        var btnStart = new Button
        {
            Text         = "Start",
            DialogResult = DialogResult.OK,
            Location     = new Point(148, 206),
            Size         = new Size(80, 26),
        };
        var btnCancel = new Button
        {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location     = new Point(236, 206),
            Size         = new Size(80, 26),
        };
        AcceptButton = btnStart;
        CancelButton = btnCancel;
        Controls.AddRange([btnStart, btnCancel]);

        FormClosing += OnClosing;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        _settings.CycleClock           = _chkClock.Checked;
        _settings.CycleWeather         = _chkWeather.Checked;
        _settings.CyclePomodoro        = _chkPomodoro.Checked;
        _settings.CycleSports          = _chkSports.Checked;
        _settings.CycleCustomText      = _chkText.Checked ? _txtText.Text.Trim() : "";
        _settings.CycleIntervalSeconds = Intervals[_cboInterval.SelectedIndex].Seconds;
    }

    private CheckBox MakeCheck(string text, bool @checked, int y) =>
        new()
        {
            Text     = text,
            Checked  = @checked,
            Location = new Point(12, y),
            Size     = new Size(316, 20),
            AutoSize = false,
        };

    private static Label MakeLabel(string text, int y) =>
        new()
        {
            Text     = text,
            Location = new Point(12, y),
            Size     = new Size(316, 18),
            AutoSize = false,
        };
}
