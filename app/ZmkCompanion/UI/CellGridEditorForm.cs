using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Editor for cell-grid display pages.
// Left panel: page list, row editor, data sources.
// Right panel: scaled live preview of the display (68×160px @ 3×).
sealed class CellGridEditorForm : Form
{
    private const int PreviewScale = 3;
    private const int PreviewW     = BitmapFrame.Width  * PreviewScale; // 204
    private const int PreviewH     = BitmapFrame.Height * PreviewScale; // 480

    private readonly AppSettings                            _settings;
    private readonly LiveState                              _liveState;
    private readonly Action<List<CellGridPage>, bool>       _onApply;

    // Working copies
    private readonly List<CellGridPage> _pages;
    private int                         _pageIndex;
    private int                         _rowIndex = -1;

    // Right panel: preview
    private readonly Panel         _previewPanel;

    // Page controls
    private readonly ComboBox      _cmbPages;
    private readonly TextBox       _txtPageName;
    private readonly CheckBox      _chkCycle;
    private readonly NumericUpDown _nudDuration;
    private          bool          _suppressPageUi;

    // Row list
    private readonly ListBox       _lstRows;

    // Row editor controls
    private readonly ComboBox      _cmbTier;
    private readonly TextBox       _txtTemplate;
    private readonly RadioButton   _radLeft, _radCenter, _radRight;
    private readonly Panel         _rowEditorPanel;
    private          bool          _suppressRowUi;

    // Data sources
    private readonly TextBox       _txtCity;
    private readonly TextBox       _txtTeam;
    private readonly Label         _lblLeagues;
    private          List<string>  _editLeagues;

    // ── Construction ─────────────────────────────────────────────────────────

    public CellGridEditorForm(AppSettings settings, LiveState liveState,
                              Action<List<CellGridPage>, bool> onApply)
    {
        _settings    = settings;
        _liveState   = liveState;
        _onApply     = onApply;
        _pages       = settings.DisplayPages.Select(p => p.Clone()).ToList();
        _editLeagues = settings.SelectedLeagues.ToList();

        Text            = "ZMK Companion — Display Editor";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        ClientSize      = new Size(640, 580);

        // ── RIGHT: preview ────────────────────────────────────────────────────
        var previewBox = new GroupBox
        {
            Text     = "Vista previa  (3×)",
            Location = new Point(420, 6),
            Size     = new Size(PreviewW + 18, PreviewH + 22),
        };
        _previewPanel = new Panel
        {
            Location  = new Point(6, 18),
            Size      = new Size(PreviewW, PreviewH),
            BackColor = Color.Black,
        };
        _previewPanel.Paint += OnPreviewPaint;
        previewBox.Controls.Add(_previewPanel);
        Controls.Add(previewBox);

        // ── LEFT: Pages group (top) ───────────────────────────────────────────
        var grpPages = new GroupBox { Text = "Páginas", Location = new Point(6, 6), Size = new Size(406, 90) };

        _cmbPages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(6, 18), Size = new Size(130, 23) };
        _cmbPages.SelectedIndexChanged += OnPageComboChanged;
        grpPages.Controls.Add(_cmbPages);

        var btnAddPage = new Button { Text = "+", Location = new Point(140, 18), Size = new Size(24, 23) };
        btnAddPage.Click += (_, _) =>
        {
            SyncCurrentPage();
            _pages.Add(new CellGridPage { Name = $"Page {_pages.Count + 1}" });
            LoadPage(_pages.Count - 1);
        };
        grpPages.Controls.Add(btnAddPage);

        var btnRemPage = new Button { Text = "−", Location = new Point(166, 18), Size = new Size(24, 23) };
        btnRemPage.Click += (_, _) =>
        {
            if (_pages.Count <= 1) return;
            _pages.RemoveAt(_pageIndex);
            LoadPage(Math.Min(_pageIndex, _pages.Count - 1));
        };
        grpPages.Controls.Add(btnRemPage);

        grpPages.Controls.Add(new Label { Text = "Nombre:", Location = new Point(6, 48), Size = new Size(52, 18) });
        _txtPageName = new TextBox { Location = new Point(62, 45), Size = new Size(120, 22) };
        _txtPageName.TextChanged += (_, _) =>
        {
            if (_suppressPageUi || _pageIndex < 0) return;
            _pages[_pageIndex].Name = _txtPageName.Text;
            RefreshPageCombo();
        };
        grpPages.Controls.Add(_txtPageName);

        _chkCycle = new CheckBox { Text = "Ciclar páginas", Checked = settings.CycleDisplayPages, Location = new Point(196, 18), Size = new Size(110, 22) };
        grpPages.Controls.Add(_chkCycle);

        grpPages.Controls.Add(new Label { Text = "Dur.:", Location = new Point(196, 48), Size = new Size(34, 18) });
        _nudDuration = new NumericUpDown { Location = new Point(232, 45), Size = new Size(50, 22), Minimum = 2, Maximum = 3600, Value = 10 };
        _nudDuration.ValueChanged += (_, _) =>
        {
            if (_suppressPageUi || _pageIndex < 0) return;
            _pages[_pageIndex].DurationSeconds = (int)_nudDuration.Value;
        };
        grpPages.Controls.Add(_nudDuration);
        grpPages.Controls.Add(new Label { Text = "s", Location = new Point(284, 48), Size = new Size(12, 18) });
        Controls.Add(grpPages);

        // ── LEFT: Rows group ──────────────────────────────────────────────────
        var grpRows = new GroupBox { Text = "Filas", Location = new Point(6, 100), Size = new Size(406, 330) };

        _lstRows = new ListBox { Location = new Point(6, 18), Size = new Size(390, 100), IntegralHeight = false };
        _lstRows.SelectedIndexChanged += OnRowSelected;
        grpRows.Controls.Add(_lstRows);

        // Row action buttons
        var btnAddRow = new Button { Text = "+ Agregar fila", Location = new Point(6, 122), Size = new Size(90, 24) };
        btnAddRow.Click += OnAddRow;
        grpRows.Controls.Add(btnAddRow);

        var btnRemRow = new Button { Text = "Eliminar", Location = new Point(100, 122), Size = new Size(70, 24) };
        btnRemRow.Click += (_, _) =>
        {
            if (_rowIndex < 0 || _pageIndex < 0) return;
            _pages[_pageIndex].Rows.RemoveAt(_rowIndex);
            RefreshRowList();
            SelectRow(Math.Min(_rowIndex, _pages[_pageIndex].Rows.Count - 1));
        };
        grpRows.Controls.Add(btnRemRow);

        var btnUp = new Button { Text = "▲", Location = new Point(176, 122), Size = new Size(30, 24) };
        btnUp.Click += (_, _) =>
        {
            if (_rowIndex <= 0 || _pageIndex < 0) return;
            var rows = _pages[_pageIndex].Rows;
            (rows[_rowIndex], rows[_rowIndex - 1]) = (rows[_rowIndex - 1], rows[_rowIndex]);
            int newIdx = _rowIndex - 1;
            RefreshRowList(); SelectRow(newIdx);
        };
        grpRows.Controls.Add(btnUp);

        var btnDown = new Button { Text = "▼", Location = new Point(210, 122), Size = new Size(30, 24) };
        btnDown.Click += (_, _) =>
        {
            if (_pageIndex < 0) return;
            var rows = _pages[_pageIndex].Rows;
            if (_rowIndex < 0 || _rowIndex >= rows.Count - 1) return;
            (rows[_rowIndex], rows[_rowIndex + 1]) = (rows[_rowIndex + 1], rows[_rowIndex]);
            int newIdx = _rowIndex + 1;
            RefreshRowList(); SelectRow(newIdx);
        };
        grpRows.Controls.Add(btnDown);

        // ── Row editor sub-panel ──────────────────────────────────────────────
        _rowEditorPanel = new Panel { Location = new Point(6, 152), Size = new Size(390, 170), Enabled = false };

        _rowEditorPanel.Controls.Add(new Label { Text = "Tier:", Location = new Point(0, 4), Size = new Size(30, 18) });
        _cmbTier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(34, 1), Size = new Size(200, 23) };
        foreach (var t in CellGridProtocol.Tiers)
            _cmbTier.Items.Add($"{t.Name}  {t.W}×{t.H}px  ({t.Cols} cols)");
        _cmbTier.SelectedIndexChanged += OnTierChanged;
        _rowEditorPanel.Controls.Add(_cmbTier);

        _rowEditorPanel.Controls.Add(new Label { Text = "Template:", Location = new Point(0, 32), Size = new Size(62, 18) });
        _txtTemplate = new TextBox { Location = new Point(0, 50), Size = new Size(385, 50), Multiline = true };
        _txtTemplate.TextChanged += OnTemplateChanged;
        _rowEditorPanel.Controls.Add(_txtTemplate);

        _rowEditorPanel.Controls.Add(new Label { Text = "Align:", Location = new Point(0, 106), Size = new Size(40, 18) });
        _radLeft   = new RadioButton { Text = "Izq",   Location = new Point(44,  104), Size = new Size(50, 20) };
        _radCenter = new RadioButton { Text = "Centro", Location = new Point(96,  104), Size = new Size(62, 20), Checked = true };
        _radRight  = new RadioButton { Text = "Der",   Location = new Point(162, 104), Size = new Size(50, 20) };
        _radLeft.CheckedChanged   += OnAlignChanged;
        _radCenter.CheckedChanged += OnAlignChanged;
        _radRight.CheckedChanged  += OnAlignChanged;
        _rowEditorPanel.Controls.Add(_radLeft);
        _rowEditorPanel.Controls.Add(_radCenter);
        _rowEditorPanel.Controls.Add(_radRight);

        // Binding buttons
        _rowEditorPanel.Controls.Add(new Label { Text = "Bindings:", Location = new Point(0, 132), Size = new Size(58, 18) });
        int bx = 62, by = 130;
        foreach (string binding in new[] { "{time}", "{date}", "{ampm}", "{weather}", "{weather.icon}",
                                            "{weather.temp}", "{sports}", "{pomodoro.time}", "{ext.text}",
                                            "{battery.icon}", "{conn.icon}" })
        {
            var btn = new Button { Text = binding, Location = new Point(bx, by), AutoSize = true, Height = 22,
                                   Font = new Font("Consolas", 7f, GraphicsUnit.Point), Padding = new Padding(2, 0, 2, 0) };
            string b = binding;
            btn.Click += (_, _) =>
            {
                int sel = _txtTemplate.SelectionStart;
                _txtTemplate.Text = _txtTemplate.Text.Insert(sel, b);
                _txtTemplate.SelectionStart = sel + b.Length;
                _txtTemplate.Focus();
            };
            _rowEditorPanel.Controls.Add(btn);
            bx += btn.PreferredSize.Width + 4;
            if (bx > 340) { bx = 62; by += 26; }
        }

        grpRows.Controls.Add(_rowEditorPanel);
        Controls.Add(grpRows);

        // ── LEFT: Data Sources group ──────────────────────────────────────────
        var grpData = new GroupBox { Text = "Fuentes de datos", Location = new Point(6, 436), Size = new Size(406, 100) };

        grpData.Controls.Add(new Label { Text = "Ciudad (clima):", Location = new Point(6, 20), Size = new Size(90, 18) });
        _txtCity = new TextBox { Text = settings.City, Location = new Point(100, 17), Size = new Size(90, 23) };
        grpData.Controls.Add(_txtCity);

        grpData.Controls.Add(new Label { Text = "Equipo:", Location = new Point(6, 50), Size = new Size(50, 18) });
        _txtTeam = new TextBox { Text = settings.SportsTeam, Location = new Point(60, 47), Size = new Size(60, 23) };
        grpData.Controls.Add(_txtTeam);

        grpData.Controls.Add(new Label { Text = "Ligas:", Location = new Point(134, 50), Size = new Size(40, 18) });
        _lblLeagues = new Label { Text = FormatLeagueLabel(_editLeagues), Location = new Point(176, 50),
                                  Size = new Size(160, 18), AutoSize = false };
        grpData.Controls.Add(_lblLeagues);

        var btnLeagues = new Button { Text = "Editar…", Location = new Point(340, 47), Size = new Size(56, 24) };
        btnLeagues.Click += (_, _) =>
        {
            using var dlg = new LeaguePickerDialog(_editLeagues);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _editLeagues        = dlg.SelectedPaths.ToList();
                _lblLeagues.Text    = FormatLeagueLabel(_editLeagues);
            }
        };
        grpData.Controls.Add(btnLeagues);
        Controls.Add(grpData);

        // ── Bottom buttons ────────────────────────────────────────────────────
        var btnApply = new Button { Text = "Aplicar", Location = new Point(270, 544), Size = new Size(74, 28), DialogResult = DialogResult.OK };
        btnApply.Click += OnApply;
        var btnClose = new Button { Text = "Cerrar", Location = new Point(350, 544), Size = new Size(74, 28) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnApply);
        Controls.Add(btnClose);

        // ── Initialize ───────────────────────────────────────────────────────
        if (_pages.Count == 0) _pages.Add(new CellGridPage());
        LoadPage(0);
    }

    // ── Page management ───────────────────────────────────────────────────────

    private void LoadPage(int index)
    {
        index = Math.Clamp(index, 0, _pages.Count - 1);
        _pageIndex = index;
        _rowIndex  = -1;

        _suppressPageUi = true;
        RefreshPageCombo();
        _txtPageName.Text    = _pages[index].Name;
        _nudDuration.Value   = Math.Clamp(_pages[index].DurationSeconds, 2, 3600);
        _suppressPageUi      = false;

        RefreshRowList();
        SelectRow(_pages[index].Rows.Count > 0 ? 0 : -1);
    }

    private void RefreshPageCombo()
    {
        _cmbPages.SelectedIndexChanged -= OnPageComboChanged;
        _cmbPages.Items.Clear();
        for (int i = 0; i < _pages.Count; i++) _cmbPages.Items.Add($"{i + 1}. {_pages[i].Name}");
        _cmbPages.SelectedIndex = Math.Clamp(_pageIndex, 0, _pages.Count - 1);
        _cmbPages.SelectedIndexChanged += OnPageComboChanged;
    }

    private void OnPageComboChanged(object? sender, EventArgs e)
    {
        if (_suppressPageUi) return;
        SyncCurrentPage();
        LoadPage(_cmbPages.SelectedIndex);
    }

    // Write widget-list edits back to _pages[_pageIndex] before navigating away.
    private void SyncCurrentPage()
    {
        // Page-level fields (name, duration) are synced immediately via their event handlers.
        // Row list is already kept in sync via Add/Remove/Up/Down operations.
    }

    // ── Row management ────────────────────────────────────────────────────────

    private void RefreshRowList()
    {
        _lstRows.SelectedIndexChanged -= OnRowSelected;
        _lstRows.Items.Clear();
        if (_pageIndex >= 0 && _pageIndex < _pages.Count)
            foreach (var row in _pages[_pageIndex].Rows)
            {
                var tier = CellGridProtocol.Tiers[row.TierId];
                _lstRows.Items.Add($"{tier.Name}  {tier.W}×{tier.H}   \"{Truncate(row.Template, 28)}\"");
            }
        _lstRows.SelectedIndexChanged += OnRowSelected;
        RefreshPreview();
    }

    private void SelectRow(int index)
    {
        _rowIndex = index;
        _lstRows.SelectedIndexChanged -= OnRowSelected;
        _lstRows.SelectedIndex = index;
        _lstRows.SelectedIndexChanged += OnRowSelected;

        _rowEditorPanel.Enabled = index >= 0;
        if (index < 0) return;

        var row = _pages[_pageIndex].Rows[index];
        _suppressRowUi = true;
        _cmbTier.SelectedIndex = row.TierId;
        _txtTemplate.Text      = row.Template;
        (_radLeft.Checked, _radCenter.Checked, _radRight.Checked) = row.Align switch
        {
            "left"  => (true,  false, false),
            "right" => (false, false, true),
            _       => (false, true,  false),
        };
        _suppressRowUi = false;
    }

    private void OnRowSelected(object? sender, EventArgs e)
        => SelectRow(_lstRows.SelectedIndex);

    private void OnAddRow(object? sender, EventArgs e)
    {
        if (_pageIndex < 0) return;
        var page = _pages[_pageIndex];
        int remaining = BitmapFrame.Height - page.TotalHeight;
        // Pick the smallest tier that still fits, default large_impar (id=4).
        byte tierId = 4;
        if (remaining < CellGridProtocol.Tiers[4].H)
            tierId = (byte)(CellGridProtocol.Tiers
                .Select((t, i) => (t, i))
                .Where(x => x.t.H <= remaining)
                .OrderByDescending(x => x.t.H)
                .Select(x => x.i)
                .DefaultIfEmpty(6) // micro if nothing fits
                .First());
        page.Rows.Add(new CellGridRow { TierId = tierId, Template = "", Align = "center" });
        RefreshRowList();
        SelectRow(page.Rows.Count - 1);
    }

    private void OnTierChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].TierId = (byte)_cmbTier.SelectedIndex;
        RefreshRowList();
        SelectRow(_rowIndex);
    }

    private void OnTemplateChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].Template = _txtTemplate.Text;
        RefreshPreview();
    }

    private void OnAlignChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        string align = _radLeft.Checked ? "left" : _radRight.Checked ? "right" : "center";
        _pages[_pageIndex].Rows[_rowIndex].Align = align;
        RefreshPreview();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void RefreshPreview() => _previewPanel.Invalidate();

    private void OnPreviewPaint(object? sender, PaintEventArgs e)
    {
        if (_pageIndex < 0 || _pageIndex >= _pages.Count) return;
        using var bmp = CellGridCompositor.RenderPreview(_pages[_pageIndex].Rows, _liveState, PreviewScale);
        e.Graphics.DrawImage(bmp, 0, 0);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    private void OnApply(object? sender, EventArgs e)
    {
        // Validate: no page exceeds display height.
        foreach (var page in _pages)
        {
            if (page.TotalHeight > BitmapFrame.Height)
            {
                MessageBox.Show(
                    $"La página '{page.Name}' excede la altura del display " +
                    $"({page.TotalHeight}px > {BitmapFrame.Height}px).\n" +
                    "Elimina o reduce filas antes de aplicar.",
                    "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _settings.City           = _txtCity.Text.Trim();
        _settings.SportsTeam     = _txtTeam.Text.Trim();
        _settings.SelectedLeagues = _editLeagues.Count > 0 ? _editLeagues : ["football/nfl"];

        _onApply(_pages.Select(p => p.Clone()).ToList(), _chkCycle.Checked);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string FormatLeagueLabel(List<string> leagues) =>
        leagues.Count == 0 ? "(ninguna)" :
        leagues.Count == 1 ? leagues[0] :
        $"{leagues[0]} +{leagues.Count - 1}";
}
