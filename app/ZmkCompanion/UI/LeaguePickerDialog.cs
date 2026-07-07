using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

// Chip-style multi-league picker.
// Shows a sport selector, live-filter search, scrollable list, and selected chips.
sealed class LeaguePickerDialog : Form
{
    private readonly ComboBox         _sportBox;
    private readonly TextBox          _searchBox;
    private readonly ListBox          _listBox;
    private readonly FlowLayoutPanel  _chipsPanel;
    private readonly ProgressBar      _progressBar;
    private readonly Label            _statusLabel;
    private readonly Button           _btnOk;

    private List<SportsLeague> _available = [];
    private List<SportsLeague> _filtered  = [];
    private readonly List<SportsLeague> _selected;
    private CancellationTokenSource? _loadCts;

    public IReadOnlyList<string> SelectedPaths => _selected.Select(l => l.EspnPath).ToList();

    public LeaguePickerDialog(List<string> currentPaths)
    {
        Text            = Strings.LeaguePickerTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(400, 430);

        int y = 12;
        Controls.Add(MakeLabel(Strings.SportLabel, y));
        _sportBox = new ComboBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(376, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _sportBox.Items.AddRange(Strings.SportNames);
        _sportBox.SelectedIndex = 0;
        Controls.Add(_sportBox);

        y += 54;
        Controls.Add(MakeLabel(Strings.SearchLeaguesLabel, y));
        _searchBox = new TextBox
        {
            Location        = new Point(12, y + 20),
            Size            = new Size(376, 23),
            PlaceholderText = Strings.SearchLeaguesPlaceholder,
        };
        Controls.Add(_searchBox);

        y += 54;
        Controls.Add(MakeLabel(Strings.AvailableLeaguesLabel, y));
        _listBox = new ListBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(376, 120),
            SelectionMode = SelectionMode.One,
        };
        Controls.Add(_listBox);

        y += 140;
        Controls.Add(MakeLabel(Strings.SelectedLeaguesLabel, y));
        _chipsPanel = new FlowLayoutPanel
        {
            Location     = new Point(12, y + 20),
            Size         = new Size(376, 72),
            AutoScroll   = true,
            BorderStyle  = BorderStyle.FixedSingle,
            WrapContents = true,
        };
        Controls.Add(_chipsPanel);

        y += 100;
        _statusLabel = new Label
        {
            Location  = new Point(12, y + 4),
            Size      = new Size(200, 18),
            Text      = Strings.Loading,
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(12, y + 24),
            Size     = new Size(376, 8),
            Style    = ProgressBarStyle.Continuous,
        };
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);

        y += 38;
        _btnOk = new Button
        {
            Text         = Strings.Ok,
            DialogResult = DialogResult.OK,
            Location     = new Point(208, y),
            Size         = new Size(80, 26),
            Enabled      = false,
        };
        var btnCancel = new Button
        {
            Text         = Strings.Cancel,
            DialogResult = DialogResult.Cancel,
            Location     = new Point(296, y),
            Size         = new Size(92, 26),
        };
        AcceptButton = _btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([_btnOk, btnCancel]);

        // Seed selected list from current paths (use known leagues where possible)
        _selected = currentPaths
            .Select(p => SportsFeature.FindOrCreate(p))
            .ToList();
        RebuildChips();

        _sportBox.SelectedIndexChanged += (_, _) => StartLoad();
        _searchBox.TextChanged         += (_, _) => ApplyFilter();
        _listBox.DoubleClick           += (_, _) => AddFocused();
        _listBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) AddFocused(); };

        FormClosed += (_, _) => _loadCts?.Cancel();
        Load       += (_, _) => StartLoad();
    }

    // ── Sport / load ──────────────────────────────────────────────────────────

    private SportKind CurrentSport => _sportBox.SelectedIndex switch
    {
        1 => SportKind.Soccer,
        2 => SportKind.Basketball,
        3 => SportKind.Hockey,
        _ => SportKind.Football,
    };

    private void StartLoad()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct    = _loadCts.Token;
        var sport = CurrentSport;

        _available        = [];
        _filtered         = [];
        _listBox.Items.Clear();
        _listBox.Enabled  = false;
        _btnOk.Enabled    = false;
        _progressBar.Value = 0;
        _statusLabel.Text = Strings.Loading;

        var prog = new Progress<int>(v =>
        {
            if (IsDisposed) return;
            _progressBar.Value = Math.Min(100, v);
            _statusLabel.Text  = v >= 100 ? "" : Strings.LoadingPercent(v);
        });

        _ = Task.Run(async () =>
        {
            var leagues = await LeagueCatalog.GetLeaguesAsync(sport, prog, ct);
            if (ct.IsCancellationRequested || IsDisposed) return;
            Invoke(() =>
            {
                _available       = leagues;
                _listBox.Enabled = true;
                _btnOk.Enabled   = true;
                _statusLabel.Text = Strings.LeaguesAvailable(leagues.Count);
                ApplyFilter();
            });
        }, ct);
    }

    // ── List ──────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        string q = _searchBox.Text.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _available.ToList()
            : _available.Where(l =>
                l.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.EspnPath.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var l in _filtered)
            _listBox.Items.Add(l.DisplayName);
        _listBox.EndUpdate();
    }

    private void AddFocused()
    {
        if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _filtered.Count) return;
        var league = _filtered[_listBox.SelectedIndex];
        if (_selected.Any(s => s.EspnPath == league.EspnPath)) return;
        _selected.Add(league);
        RebuildChips();
    }

    // ── Chips ─────────────────────────────────────────────────────────────────

    private void RebuildChips()
    {
        _chipsPanel.Controls.Clear();
        foreach (var l in _selected.ToList())
        {
            var chip = new Button
            {
                Text      = $"{l.ShortName}  ×",
                AutoSize  = true,
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(2),
                Tag       = l,
            };
            chip.FlatAppearance.BorderSize = 1;
            chip.Click += (_, _) =>
            {
                _selected.RemoveAll(s => s.EspnPath == ((SportsLeague)chip.Tag!).EspnPath);
                RebuildChips();
            };
            _chipsPanel.Controls.Add(chip);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Label MakeLabel(string text, int y) =>
        new()
        {
            Text     = text,
            Location = new Point(12, y),
            Size     = new Size(376, 18),
            AutoSize = false,
        };
}
