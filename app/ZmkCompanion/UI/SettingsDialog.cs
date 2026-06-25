using System.Drawing;
using System.Windows.Forms;

namespace ZmkCompanion.UI;

sealed class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _cityBox;
    private readonly TextBox _nflTeamBox;
    private readonly ComboBox _pomodoroBox;

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;

        Text            = "ZMK Companion — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(340, 200);

        int y = 12;
        Controls.Add(MakeLabel("Weather city (blank = auto):", y));
        _cityBox = MakeTextBox(settings.City, y + 20);
        Controls.Add(_cityBox);

        y += 54;
        Controls.Add(MakeLabel("NFL team abbreviation (e.g. KC, SF):", y));
        _nflTeamBox = MakeTextBox(settings.NflTeam, y + 20);
        Controls.Add(_nflTeamBox);

        y += 54;
        Controls.Add(MakeLabel("Default Pomodoro preset:", y));
        _pomodoroBox = new ComboBox
        {
            Location     = new Point(12, y + 20),
            Size         = new Size(316, 23),
            DropDownStyle = ComboBoxStyle.DropDown,
        };
        _pomodoroBox.Items.AddRange(["classic", "short", "long"]);
        _pomodoroBox.Text = settings.PomodoroPreset;
        Controls.Add(_pomodoroBox);

        y += 56;
        var ok = new Button
        {
            Text         = "Save",
            DialogResult = DialogResult.OK,
            Location     = new Point(148, y),
            Size         = new Size(80, 26),
        };
        var cancel = new Button
        {
            Text         = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location     = new Point(236, y),
            Size         = new Size(80, 26),
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([ok, cancel]);

        FormClosing += OnClosing;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        _settings.City          = _cityBox.Text.Trim();
        _settings.NflTeam       = _nflTeamBox.Text.Trim().ToUpper();
        _settings.PomodoroPreset = _pomodoroBox.Text.Trim();
    }

    private static Label MakeLabel(string text, int y) =>
        new()
        {
            Text     = text,
            Location = new Point(12, y),
            Size     = new Size(316, 18),
            AutoSize = false,
        };

    private static TextBox MakeTextBox(string text, int y) =>
        new()
        {
            Text     = text,
            Location = new Point(12, y),
            Size     = new Size(316, 23),
        };
}
