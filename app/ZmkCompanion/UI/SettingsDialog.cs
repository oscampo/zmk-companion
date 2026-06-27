using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

sealed class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox  _cityBox;
    private readonly Button   _btnLeagues;
    private readonly TextBox  _teamBox;
    private readonly ComboBox _pomodoroBox;

    // Working copy; assigned to _settings only on OK.
    private List<string> _pendingLeagues;

    public SettingsDialog(AppSettings settings)
    {
        _settings       = settings;
        _pendingLeagues = settings.SelectedLeagues.ToList();

        Text            = "ZMK Companion - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(340, 266);

        int y = 12;
        Controls.Add(MakeLabel("Weather city (blank = auto):", y));
        _cityBox = MakeTextBox(settings.City, y + 20);
        Controls.Add(_cityBox);

        y += 54;
        Controls.Add(MakeLabel("Sports leagues:", y));
        _btnLeagues = new Button
        {
            Text      = LeaguesSummary(_pendingLeagues),
            Location  = new Point(12, y + 20),
            Size      = new Size(316, 26),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        };
        _btnLeagues.Click += OnConfigureLeagues;
        Controls.Add(_btnLeagues);

        y += 58;
        Controls.Add(MakeLabel("Team filter (abbreviation, e.g. KC, COL):", y));
        _teamBox = MakeTextBox(settings.SportsTeam, y + 20);
        Controls.Add(_teamBox);

        y += 54;
        Controls.Add(MakeLabel("Default Pomodoro preset:", y));
        _pomodoroBox = new ComboBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(316, 23),
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

    private void OnConfigureLeagues(object? sender, EventArgs e)
    {
        using var dlg = new LeaguePickerDialog(_pendingLeagues);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _pendingLeagues     = dlg.SelectedPaths.ToList();
        _btnLeagues.Text    = LeaguesSummary(_pendingLeagues);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        _settings.City            = _cityBox.Text.Trim();
        _settings.PomodoroPreset  = _pomodoroBox.Text.Trim();
        _settings.SportsTeam      = _teamBox.Text.Trim().ToUpper();
        _settings.SelectedLeagues = _pendingLeagues;
    }

    private static string LeaguesSummary(List<string> paths)
    {
        if (paths.Count == 0) return "No leagues selected — click to configure";
        if (paths.Count == 1) return SportsFeature.FindOrCreate(paths[0]).DisplayName;
        return $"{paths.Count} leagues selected";
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
