using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

sealed class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox  _cityBox;
    private readonly ComboBox _sportBox;
    private readonly ComboBox _leagueBox;
    private readonly TextBox  _teamBox;
    private readonly ComboBox _pomodoroBox;

    // Parallel list that tracks which SportsLeague each _leagueBox item represents.
    private readonly List<SportsLeague> _leagueItems = [];

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;

        Text            = "ZMK Companion - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(340, 330);

        int y = 12;
        Controls.Add(MakeLabel("Weather city (blank = auto):", y));
        _cityBox = MakeTextBox(settings.City, y + 20);
        Controls.Add(_cityBox);

        y += 54;
        Controls.Add(MakeLabel("Sport:", y));
        _sportBox = new ComboBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(316, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _sportBox.Items.AddRange(["Football (NFL)", "Soccer", "Basketball (NBA)", "Hockey (NHL)"]);
        Controls.Add(_sportBox);

        y += 54;
        Controls.Add(MakeLabel("League:", y));
        _leagueBox = new ComboBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(316, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        Controls.Add(_leagueBox);

        y += 54;
        Controls.Add(MakeLabel("Team filter (abbreviation, e.g. KC, FRA):", y));
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

        _sportBox.SelectedIndexChanged += (_, _) => PopulateLeagues();
        InitSportSelection(settings.SportEspnPath);

        FormClosing += OnClosing;
    }

    private void InitSportSelection(string espnPath)
    {
        var league = SportsFeature.FindLeague(espnPath) ?? SportsFeature.DefaultLeague;
        _sportBox.SelectedIndex = league.Sport switch
        {
            SportKind.Soccer     => 1,
            SportKind.Basketball => 2,
            SportKind.Hockey     => 3,
            _                    => 0,
        };
        // SelectedIndexChanged fires synchronously above and populates _leagueItems.
        int idx = _leagueItems.FindIndex(l => l.EspnPath == espnPath);
        if (idx >= 0) _leagueBox.SelectedIndex = idx;
    }

    private void PopulateLeagues()
    {
        _leagueItems.Clear();
        _leagueBox.Items.Clear();

        var sport = _sportBox.SelectedIndex switch
        {
            1 => SportKind.Soccer,
            2 => SportKind.Basketball,
            3 => SportKind.Hockey,
            _ => SportKind.Football,
        };

        foreach (var l in SportsFeature.AllLeagues.Where(l => l.Sport == sport))
        {
            _leagueItems.Add(l);
            _leagueBox.Items.Add(l.DisplayName);
        }

        if (_leagueBox.Items.Count > 0)
            _leagueBox.SelectedIndex = 0;

        // Disable when only one league exists for the sport (e.g. NFL, NBA, NHL).
        _leagueBox.Enabled = _leagueBox.Items.Count > 1;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        _settings.City           = _cityBox.Text.Trim();
        _settings.PomodoroPreset = _pomodoroBox.Text.Trim();
        _settings.SportsTeam     = _teamBox.Text.Trim().ToUpper();
        if (_leagueBox.SelectedIndex >= 0 && _leagueBox.SelectedIndex < _leagueItems.Count)
            _settings.SportEspnPath = _leagueItems[_leagueBox.SelectedIndex].EspnPath;
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
