using System.Drawing;
using System.Drawing.Text;
using System.Text.Json;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

// Editor for cell-grid display pages.
// Left panel: page list, row editor, data-source tabs (Sports/Weather/CLI/Library).
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
    private readonly ComboBox      _cmbSplit;
    private readonly TextBox       _txtTemplate;
    private readonly RadioButton   _radLeft, _radCenter, _radRight;
    private readonly CheckBox      _chkBold;
    private readonly CheckBox      _chkAntiAlias;
    private readonly ComboBox      _cmbNumericStyle;
    private readonly ComboBox      _cmbAlphaStyle;
    private readonly Panel         _rowEditorPanel;
    private          bool          _suppressRowUi;

    // Binding picker
    private readonly ComboBox      _cmbBindCategory;
    private readonly ComboBox      _cmbBind;

    // Data-source tabs
    private readonly TextBox       _txtCity;
    private readonly Label         _lblWeatherStatus;
    private readonly RadioButton   _radTempC, _radTempF;
    private readonly Panel         _teamsPanel;
    private readonly Dictionary<string, TextBox> _teamBoxes = new();
    private          List<string>  _editLeagues;

    // Distinct categories from settings.CustomTokens, appended to the binding
    // picker after BindingCatalog. Computed once: this dialog doesn't need to
    // react to CustomTokensForm changes made while it's already open.
    private readonly List<string> _customCategories;

    // Library tab
    private readonly TextBox  _txtLibName;
    private readonly ListBox  _lstLibFiles;

    private static string LibraryDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZmkCompanion", "library");

    private const int DeportesCategory = 2;

    // Binding catalog — category → list of (label, token)
    private static readonly (string Category, (string Label, string Token)[] Items)[] BindingCatalog =
    [
        ("Hora", [
            ("Hora",                 "{time}"),
            ("Hora 24h",             "{time24}"),
            ("Hora 12h",             "{time12}"),
            ("Hora (HH)",            "{time.hh}"),
            ("Minutos (MM)",         "{time.mm}"),
            ("AM/PM",                "{ampm}"),
            ("Fecha",                "{date}"),
            ("Día del mes",          "{date.day}"),
            ("Día del mes (DD)",     "{time.dd}"),
            ("Mes",                  "{date.month}"),
        ]),
        ("Clima", [
            ("Clima (resumen)",      "{weather}"),
            ("Icono clima",          "{weather.icon}"),
            ("Temperatura",          "{weather.temp}"),
            ("Ciudad clima",         "{weather.city}"),
        ]),
        // Index 2 = DeportesCategory — populated dynamically
        ("Deportes", []),
        ("Pomodoro", [
            ("Tiempo pomodoro",      "{pomodoro.time}"),
            ("Fase",                 "{pomodoro.phase}"),
            ("Barra progreso",       "{pomodoro.bar}"),
            ("Icono fase",           "{pomodoro.icon}"),
            ("Ciclo #",              "{pomodoro.cycle}"),
        ]),
        ("Sistema", [
            ("Batería (icono)",      "{battery.icon}"),
            ("Batería (%)",          "{battery.percent}"),
            ("Batería (nivel)",      "{battery.level}"),
            ("Conexión (icono)",     "{conn.icon}"),
            ("Tipo conexión",        "{conn.type}"),
            ("Perfil BLE",           "{conn.profile}"),
            ("Barra perfiles (5)",   "{conn.profilebar}"),
            ("Layer activo",         "{layer}"),
            ("Texto ext. (completo)","{ext.text}"),
            ("Texto ext. línea 1",   "{ext.text.0}"),
            ("Texto ext. línea 2",   "{ext.text.1}"),
            ("Texto ext. línea 3",   "{ext.text.2}"),
            ("Texto ext. línea 4",   "{ext.text.3}"),
        ]),
    ];

    // ── Construction ─────────────────────────────────────────────────────────

    public CellGridEditorForm(AppSettings settings, LiveState liveState,
                              Action<List<CellGridPage>, bool> onApply)
    {
        _settings    = settings;
        _liveState   = liveState;
        _onApply     = onApply;
        _pages       = settings.DisplayPages.Select(p => p.Clone()).ToList();
        _editLeagues = settings.SelectedLeagues.ToList();
        _customCategories = settings.CustomTokens
            .Select(t => t.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        liveState.Changed += OnLiveStateChanged;

        Text            = "ZMK Companion — Display Editor";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        ClientSize      = new Size(640, 657);

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
        var grpRows = new GroupBox { Text = "Filas", Location = new Point(6, 100), Size = new Size(406, 375) };

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

        var btnIconPair = new Button { Text = "+ Par íconos", Location = new Point(248, 122), Size = new Size(82, 24) };
        btnIconPair.Click += OnAddIconPair;
        grpRows.Controls.Add(btnIconPair);

        var btnTextBlock = new Button { Text = "+ Texto", Location = new Point(334, 122), Size = new Size(58, 24) };
        btnTextBlock.Click += OnAddTextBlock;
        grpRows.Controls.Add(btnTextBlock);

        // ── Row editor sub-panel ──────────────────────────────────────────────
        _rowEditorPanel = new Panel { Location = new Point(6, 152), Size = new Size(390, 215), Enabled = false };

        _rowEditorPanel.Controls.Add(new Label { Text = "Tier:", Location = new Point(0, 4), Size = new Size(30, 18) });
        _cmbTier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(34, 1), Size = new Size(160, 23) };
        foreach (var t in CellGridProtocol.Tiers)
            _cmbTier.Items.Add($"{t.Name}  {t.W}×{t.H}px  ({t.Cols} {(t.Cols == 1 ? "col" : "cols")})");
        _cmbTier.SelectedIndexChanged += OnTierChanged;
        _rowEditorPanel.Controls.Add(_cmbTier);

        _rowEditorPanel.Controls.Add(new Label { Text = "Mitad:", Location = new Point(198, 4), Size = new Size(38, 18) });
        _cmbSplit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(238, 1), Size = new Size(148, 23) };
        _cmbSplit.Items.AddRange(["Normal", "↑ Mitad superior", "↓ Mitad inferior"]);
        _cmbSplit.SelectedIndex = 0;
        _cmbSplit.SelectedIndexChanged += OnSplitChanged;
        _rowEditorPanel.Controls.Add(_cmbSplit);

        _rowEditorPanel.Controls.Add(new Label { Text = "Template:", Location = new Point(0, 32), Size = new Size(62, 18) });
        // NerdFont, not the inherited UI font: without this, any inserted glyph
        // (from the "NF..." picker or a {token}'s icon) has no matching glyph in
        // the default font, so Windows silently font-fallbacks to some other
        // installed font that happens to have *something* mapped to that same
        // codepoint, showing an unrelated character instead of the one picked.
        _txtTemplate = new TextBox
        {
            Location  = new Point(0, 50),
            Size      = new Size(385, 60),
            Multiline = true,
            Font      = NerdFont.CreateFont(12f),
        };
        _txtTemplate.TextChanged += OnTemplateChanged;
        _rowEditorPanel.Controls.Add(_txtTemplate);

        _rowEditorPanel.Controls.Add(new Label { Text = "Align:", Location = new Point(0, 117), Size = new Size(40, 18) });
        _radLeft   = new RadioButton { Text = "Izq",    Location = new Point(44,  115), Size = new Size(50, 20) };
        _radCenter = new RadioButton { Text = "Centro", Location = new Point(96,  115), Size = new Size(62, 20), Checked = true };
        _radRight  = new RadioButton { Text = "Der",    Location = new Point(162, 115), Size = new Size(50, 20) };
        _radLeft.CheckedChanged   += OnAlignChanged;
        _radCenter.CheckedChanged += OnAlignChanged;
        _radRight.CheckedChanged  += OnAlignChanged;
        _rowEditorPanel.Controls.Add(_radLeft);
        _rowEditorPanel.Controls.Add(_radCenter);
        _rowEditorPanel.Controls.Add(_radRight);

        // ── Style row (Bold + AntiAlias + glyph styles) ──────────────────────
        _chkBold = new CheckBox { Text = "Bold", Location = new Point(0, 141), Size = new Size(52, 20) };
        _chkBold.CheckedChanged += OnBoldChanged;
        _rowEditorPanel.Controls.Add(_chkBold);

        _chkAntiAlias = new CheckBox { Text = "AA", Location = new Point(54, 141), Size = new Size(42, 20) };
        _chkAntiAlias.CheckedChanged += OnAntiAliasChanged;
        _rowEditorPanel.Controls.Add(_chkAntiAlias);

        _rowEditorPanel.Controls.Add(new Label { Text = "Núm:", Location = new Point(100, 144), Size = new Size(32, 18) });
        _cmbNumericStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(134, 141), Size = new Size(116, 23) };
        foreach (var s in new[] { "text", "box", "box_outline", "box_multiple", "plain", "circle", "circle_outline" })
            _cmbNumericStyle.Items.Add(NerdFont.NumericStyleLabel(s));
        _cmbNumericStyle.SelectedIndex = 0;
        _cmbNumericStyle.SelectedIndexChanged += OnNumericStyleChanged;
        _rowEditorPanel.Controls.Add(_cmbNumericStyle);

        _rowEditorPanel.Controls.Add(new Label { Text = "Alfa:", Location = new Point(254, 144), Size = new Size(30, 18) });
        _cmbAlphaStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(284, 141), Size = new Size(100, 23) };
        foreach (var s in new[] { "text", "plain", "box", "box_outline", "circle", "circle_outline" })
            _cmbAlphaStyle.Items.Add(NerdFont.AlphaStyleLabel(s));
        _cmbAlphaStyle.SelectedIndex = 0;
        _cmbAlphaStyle.SelectedIndexChanged += OnAlphaStyleChanged;
        _rowEditorPanel.Controls.Add(_cmbAlphaStyle);

        // ── Binding picker ────────────────────────────────────────────────────
        _rowEditorPanel.Controls.Add(new Label { Text = "Insertar:", Location = new Point(0, 183), Size = new Size(52, 18) });

        _cmbBindCategory = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(56, 180),
            Size          = new Size(90, 23),
        };
        foreach (var cat in BindingCatalog) _cmbBindCategory.Items.Add(cat.Category);
        foreach (var cat in _customCategories) _cmbBindCategory.Items.Add(cat);
        _cmbBindCategory.SelectedIndex = 0;
        _cmbBindCategory.SelectedIndexChanged += OnBindCategoryChanged;
        _rowEditorPanel.Controls.Add(_cmbBindCategory);

        _cmbBind = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(150, 180),
            Size          = new Size(118, 23), // narrowed 160->118 to fit btnGlyph on the same row
        };
        _cmbBind.SelectedIndexChanged += (_, _) => { };
        _rowEditorPanel.Controls.Add(_cmbBind);
        PopulateBindings(0);

        var btnInsert = new Button { Text = "↵ Insertar", Location = new Point(272, 180), Size = new Size(70, 23) };
        btnInsert.Click += OnInsertBinding;
        _rowEditorPanel.Controls.Add(btnInsert);

        // Full Nerd Font glyph picker (GlyphPickerDialog/FontCmapReader already
        // existed, previously wired only to the unreachable legacy
        // CanvasEditorForm - nothing new to build, just reconnecting it here).
        var btnGlyph = new Button { Text = "NF…", Location = new Point(346, 180), Size = new Size(40, 23) };
        btnGlyph.Click += OnInsertGlyph;
        _rowEditorPanel.Controls.Add(btnGlyph);

        grpRows.Controls.Add(_rowEditorPanel);
        Controls.Add(grpRows);

        // ── LEFT: Data Sources — TabControl ───────────────────────────────────
        var tabData = new TabControl
        {
            Location = new Point(6, 481),
            Size     = new Size(406, 138),
        };

        // ── Tab: Deportes ─────────────────────────────────────────────────────
        var tabSports = new TabPage("Deportes");
        tabSports.Controls.Add(new Label { Text = "Ligas y equipos:", Location = new Point(6, 6), AutoSize = true });
        var btnLeagues = new Button { Text = "Editar ligas…", Location = new Point(280, 3), Size = new Size(110, 23) };
        btnLeagues.Click += (_, _) =>
        {
            using var dlg = new LeaguePickerDialog(_editLeagues);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _editLeagues = dlg.SelectedPaths.ToList();
                RebuildTeamInputs();
                PopulateBindings(_cmbBindCategory.SelectedIndex);
            }
        };
        tabSports.Controls.Add(btnLeagues);

        _teamsPanel = new Panel
        {
            Location   = new Point(4, 28),
            Size       = new Size(390, 68),
            AutoScroll = true,
        };
        tabSports.Controls.Add(_teamsPanel);
        tabData.TabPages.Add(tabSports);

        // ── Tab: Clima ────────────────────────────────────────────────────────
        var tabWeather = new TabPage("Clima");
        tabWeather.Controls.Add(new Label { Text = "Ciudad:", Location = new Point(6, 8), AutoSize = true });
        _txtCity = new TextBox { Text = settings.City, Location = new Point(58, 5), Size = new Size(100, 22) };
        tabWeather.Controls.Add(_txtCity);

        var btnRefreshWeather = new Button { Text = "↺", Location = new Point(162, 5), Size = new Size(26, 22) };
        btnRefreshWeather.Click += (_, _) => _ = RefreshWeatherPreviewAsync(_txtCity.Text.Trim());
        tabWeather.Controls.Add(btnRefreshWeather);

        _lblWeatherStatus = new Label
        {
            Text      = "…",
            Location  = new Point(194, 8),
            Size      = new Size(196, 18),
            ForeColor = Color.Gray,
            AutoSize  = false,
        };
        tabWeather.Controls.Add(_lblWeatherStatus);

        tabWeather.Controls.Add(new Label { Text = "Temperatura:", Location = new Point(6, 36), AutoSize = true });
        bool isFahrenheit = settings.WeatherUnit == "fahrenheit";
        _radTempC = new RadioButton { Text = "°C", Location = new Point(90, 34), Size = new Size(42, 20), Checked = !isFahrenheit };
        _radTempF = new RadioButton { Text = "°F", Location = new Point(136, 34), Size = new Size(42, 20), Checked =  isFahrenheit };
        tabWeather.Controls.AddRange([_radTempC, _radTempF]);
        tabData.TabPages.Add(tabWeather);

        // ── Tab: CLI ──────────────────────────────────────────────────────────
        var tabCli = new TabPage("CLI");
        string exeDir   = AppContext.BaseDirectory;
        string zkcPath  = Path.Combine(exeDir, "zkc.exe");
        var txtCliPath = new TextBox
        {
            Text      = zkcPath,
            ReadOnly  = true,
            Location  = new Point(4, 5),
            Size      = new Size(312, 22),
            BackColor = SystemColors.Control,
        };
        tabCli.Controls.Add(txtCliPath);

        var btnCopyPath = new Button { Text = "Copiar", Location = new Point(320, 5), Size = new Size(60, 22) };
        btnCopyPath.Click += (_, _) => Clipboard.SetText(zkcPath);
        tabCli.Controls.Add(btnCopyPath);

        var btnOpenCli = new Button { Text = "Abrir terminal con zkc -h", Location = new Point(4, 33), Size = new Size(180, 26) };
        btnOpenCli.Click += OnOpenCli;
        tabCli.Controls.Add(btnOpenCli);

        var lblCliHint = new Label
        {
            Text      = "Uso: zkc \"mensaje\"  |  zkc \"línea1\\nlínea2\"",
            Location  = new Point(4, 66),
            Size      = new Size(390, 18),
            ForeColor = Color.Gray,
            Font      = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f),
        };
        tabCli.Controls.Add(lblCliHint);
        tabData.TabPages.Add(tabCli);

        // ── Tab: Biblioteca ───────────────────────────────────────────────────
        var tabLib = new TabPage("Biblioteca");
        tabLib.Controls.Add(new Label { Text = "Nombre:", Location = new Point(4, 8), AutoSize = true });
        _txtLibName = new TextBox { Location = new Point(58, 5), Size = new Size(200, 22) };
        tabLib.Controls.Add(_txtLibName);

        var btnLibSave = new Button { Text = "Guardar", Location = new Point(264, 5), Size = new Size(70, 22) };
        btnLibSave.Click += OnLibSave;
        tabLib.Controls.Add(btnLibSave);

        _lstLibFiles = new ListBox
        {
            Location       = new Point(4, 32),
            Size           = new Size(274, 66),
            IntegralHeight = false,
        };
        tabLib.Controls.Add(_lstLibFiles);

        var btnLibLoad = new Button { Text = "Cargar", Location = new Point(284, 32), Size = new Size(106, 24) };
        btnLibLoad.Click += OnLibLoad;
        tabLib.Controls.Add(btnLibLoad);

        var btnLibDel = new Button { Text = "Eliminar", Location = new Point(284, 60), Size = new Size(106, 24) };
        btnLibDel.Click += OnLibDelete;
        tabLib.Controls.Add(btnLibDel);

        tabData.TabPages.Add(tabLib);
        Controls.Add(tabData);

        // ── Bottom buttons ────────────────────────────────────────────────────
        var btnApply = new Button { Text = "Aplicar", Location = new Point(270, 625), Size = new Size(74, 28), DialogResult = DialogResult.OK };
        btnApply.Click += OnApply;
        var btnClose = new Button { Text = "Cerrar", Location = new Point(350, 625), Size = new Size(74, 28) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnApply);
        Controls.Add(btnClose);

        FormClosed += (_, _) => _liveState.Changed -= OnLiveStateChanged;

        // ── Initialize ───────────────────────────────────────────────────────
        if (_pages.Count == 0) _pages.Add(new CellGridPage());
        LoadPage(0);
        RebuildTeamInputs();
        RefreshLibraryList();

        _ = RefreshWeatherPreviewAsync(settings.City);
    }

    // ── Binding picker ────────────────────────────────────────────────────────

    private void OnBindCategoryChanged(object? sender, EventArgs e)
        => PopulateBindings(_cmbBindCategory.SelectedIndex);

    private void PopulateBindings(int catIndex)
    {
        _cmbBind.Items.Clear();
        int totalCategories = BindingCatalog.Length + _customCategories.Count;
        if (catIndex < 0 || catIndex >= totalCategories) return;
        var items = GetCategoryItems(catIndex);
        foreach (var (label, token) in items)
            _cmbBind.Items.Add($"{label}  {token}");
        if (_cmbBind.Items.Count > 0) _cmbBind.SelectedIndex = 0;
    }

    // catIndex: 0..BindingCatalog.Length-1 are the fixed built-in categories
    // (DeportesCategory dynamic among them); beyond that, indices map to
    // _customCategories, filtering settings.CustomTokens by category name.
    private (string Label, string Token)[] GetCategoryItems(int catIndex)
    {
        if (catIndex == DeportesCategory) return BuildDeportesItems();
        if (catIndex >= 0 && catIndex < BindingCatalog.Length) return BindingCatalog[catIndex].Items;

        int customIdx = catIndex - BindingCatalog.Length;
        if (customIdx < 0 || customIdx >= _customCategories.Count) return [];
        string category = _customCategories[customIdx];
        return _settings.CustomTokens
            .Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(t => (t.Name, $"{{custom.{t.Name}}}"))
            .ToArray();
    }

    private (string Label, string Token)[] BuildDeportesItems()
    {
        var items = new List<(string, string)>
        {
            ("Partido (resumen)",    "{sports}"),
            ("Equipo",               "{sports.team}"),
            ("Equipos en vivo",      "{sports.live_game}"),
            ("Marcador en vivo",     "{sports.live_marker}"),
            ("Estado (icono)",       "{sports.marker}"),
            ("Tiempo en vivo",       "{sports.live_time}"),
            ("Liga",                 "{sports.league}"),
            ("Últ. partido",         "{sports.last_game}"),
            ("Últ. marcador",        "{sports.last_marker}"),
            ("Próx. partido",        "{sports.next_game}"),
            ("Próx. fecha",          "{sports.next_date}"),
            ("Próx. hora",           "{sports.next_gametime}"),
        };

        if (_editLeagues.Count > 1)
        {
            foreach (var path in _editLeagues)
            {
                var lg = SportsFeature.FindOrCreate(path);
                string q = ":" + lg.ShortName;
                items.Add(($"[{lg.ShortName}] Últ. partido",   $"{{sports.last_game{q}}}"));
                items.Add(($"[{lg.ShortName}] Últ. marcador",  $"{{sports.last_marker{q}}}"));
                items.Add(($"[{lg.ShortName}] Próx. partido",  $"{{sports.next_game{q}}}"));
                items.Add(($"[{lg.ShortName}] Próx. fecha",    $"{{sports.next_date{q}}}"));
                items.Add(($"[{lg.ShortName}] Próx. hora",     $"{{sports.next_gametime{q}}}"));
                items.Add(($"[{lg.ShortName}] Liga",           $"{{sports.league{q}}}"));
                items.Add(($"[{lg.ShortName}] Equipo",         $"{{sports.team{q}}}"));
            }
        }
        return items.ToArray();
    }

    private void OnInsertBinding(object? sender, EventArgs e)
    {
        int catIdx  = _cmbBindCategory.SelectedIndex;
        int bindIdx = _cmbBind.SelectedIndex;
        if (catIdx < 0 || bindIdx < 0) return;
        var items = GetCategoryItems(catIdx);
        if (bindIdx >= items.Length) return;
        string token = items[bindIdx].Token;
        int sel = _txtTemplate.SelectionStart;
        _txtTemplate.Text = _txtTemplate.Text.Insert(sel, token);
        _txtTemplate.SelectionStart = sel + token.Length;
        _txtTemplate.Focus();
    }

    // Inserts a literal glyph character (not a {token}) at the cursor, e.g. a
    // static status/battery/traffic-light icon to sit next to a live
    // {custom.NAME} value in the same row template.
    private void OnInsertGlyph(object? sender, EventArgs e)
    {
        using var dlg = new GlyphPickerDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedGlyph is not { } glyph) return;
        int sel = _txtTemplate.SelectionStart;
        _txtTemplate.Text = _txtTemplate.Text.Insert(sel, glyph);
        _txtTemplate.SelectionStart = sel + glyph.Length;
        _txtTemplate.Focus();
    }

    // ── Per-league team inputs ────────────────────────────────────────────────

    private void RebuildTeamInputs()
    {
        var saved = _teamBoxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim());
        _teamBoxes.Clear();
        _teamsPanel.Controls.Clear();

        int col = 0, row = 0;
        const int slotW = 130, rowH = 28;
        const int colsPerRow = 3;

        foreach (var path in _editLeagues)
        {
            var lg = SportsFeature.FindOrCreate(path);
            string team = saved.TryGetValue(path, out var e1) ? e1
                        : _settings.SportsTeams.TryGetValue(path, out var e2) ? e2 : "";

            int px = col * slotW;
            int py = row * rowH;

            var lbl = new Label { Text = lg.ShortName + ":", Location = new Point(px, py + 4), Size = new Size(54, 18), AutoSize = false };
            var tb  = new TextBox { Text = team, Location = new Point(px + 56, py), Size = new Size(68, 22), PlaceholderText = "equipo" };
            _teamBoxes[path] = tb;
            _teamsPanel.Controls.AddRange([lbl, tb]);

            col++;
            if (col >= colsPerRow) { col = 0; row++; }
        }
    }

    // ── Weather preview refresh ───────────────────────────────────────────────

    private async Task RefreshWeatherPreviewAsync(string city)
    {
        _lblWeatherStatus.Text      = "consultando…";
        _lblWeatherStatus.ForeColor = Color.Gray;
        try
        {
            var data = await WeatherFeature.FetchWeatherAsync(city);
            bool fahrenheit = _radTempF.Checked;
            string tempStr  = fahrenheit
                ? $"{data.TempC * 9 / 5 + 32:F0}°F"
                : $"{data.TempC:F0}°";
            _liveState.UpdateWeather(data.Icon.ToString(), tempStr, data.City);
            _lblWeatherStatus.Text      = $"{data.City} {tempStr}";
            _lblWeatherStatus.ForeColor = Color.LimeGreen;
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            _lblWeatherStatus.Text      = $"HTTP {(int)ex.StatusCode.Value} — red/proxy";
            _lblWeatherStatus.ForeColor = Color.OrangeRed;
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            _lblWeatherStatus.Text      = msg.Length > 34 ? msg[..34] + "…" : msg;
            _lblWeatherStatus.ForeColor = Color.OrangeRed;
        }
    }

    private void OnLiveStateChanged() => RefreshPreview();

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

    private void SyncCurrentPage() { }

    // ── Row management ────────────────────────────────────────────────────────

    private void RefreshRowList()
    {
        _lstRows.SelectedIndexChanged -= OnRowSelected;
        _lstRows.Items.Clear();
        if (_pageIndex >= 0 && _pageIndex < _pages.Count)
            foreach (var row in _pages[_pageIndex].Rows)
            {
                var tier = CellGridProtocol.Tiers[row.TierId];
                string splitMark = row.SplitHalf switch
                {
                    SplitHalf.Top    => "↑ ",
                    SplitHalf.Bottom => "↓ ",
                    _                => "",
                };
                _lstRows.Items.Add($"{splitMark}{tier.Name}  {tier.W}×{tier.H}   \"{Truncate(row.Template, 28)}\"");
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
        _cmbTier.SelectedIndex         = row.TierId;
        _cmbSplit.SelectedIndex        = (int)row.SplitHalf;
        _txtTemplate.Text              = row.Template;
        _chkBold.Checked               = row.Bold;
        _chkAntiAlias.Checked          = row.AntiAlias;
        _cmbNumericStyle.SelectedIndex = NumericStyleIndex(row.NumericStyle);
        _cmbAlphaStyle.SelectedIndex   = AlphaStyleIndex(row.AlphaStyle);
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
        byte tierId = 4;
        if (remaining < CellGridProtocol.Tiers[4].H)
            tierId = (byte)(CellGridProtocol.Tiers
                .Select((t, i) => (t, i))
                .Where(x => x.t.H <= remaining)
                .OrderByDescending(x => x.t.H)
                .Select(x => x.i)
                .DefaultIfEmpty(6)
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

    private void OnSplitChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].SplitHalf = (SplitHalf)_cmbSplit.SelectedIndex;
        RefreshRowList();
        SelectRow(_rowIndex);
    }

    private static readonly string[] _numericStyles = ["text", "box", "box_outline", "box_multiple", "plain", "circle", "circle_outline"];
    private static readonly string[] _alphaStyles   = ["text", "plain", "box", "box_outline", "circle", "circle_outline"];

    private static int NumericStyleIndex(string s) => Math.Max(0, Array.IndexOf(_numericStyles, s));
    private static int AlphaStyleIndex  (string s) => Math.Max(0, Array.IndexOf(_alphaStyles,   s));

    private void OnBoldChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].Bold = _chkBold.Checked;
        RefreshPreview();
    }

    private void OnAntiAliasChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].AntiAlias = _chkAntiAlias.Checked;
        RefreshPreview();
    }

    private void OnNumericStyleChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        int idx = _cmbNumericStyle.SelectedIndex;
        _pages[_pageIndex].Rows[_rowIndex].NumericStyle =
            idx >= 0 && idx < _numericStyles.Length ? _numericStyles[idx] : "text";
        RefreshPreview();
    }

    private void OnAlphaStyleChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        int idx = _cmbAlphaStyle.SelectedIndex;
        _pages[_pageIndex].Rows[_rowIndex].AlphaStyle =
            idx >= 0 && idx < _alphaStyles.Length ? _alphaStyles[idx] : "text";
        RefreshPreview();
    }

    private void OnAddIconPair(object? sender, EventArgs e)
    {
        if (_pageIndex < 0) return;
        var page = _pages[_pageIndex];
        const byte iconHalfId = 14;
        var tier = CellGridProtocol.Tiers[iconHalfId];
        int remaining = BitmapFrame.Height - page.TotalHeight;
        if (remaining < tier.H * 2)
        {
            MessageBox.Show(
                $"No hay espacio suficiente para un par de íconos ({tier.H * 2}px necesarios, {remaining}px libres).",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int insertAt = _rowIndex >= 0 ? _rowIndex + 1 : page.Rows.Count;
        page.Rows.Insert(insertAt, new CellGridRow { TierId = iconHalfId, Template = "", Align = "center", SplitHalf = SplitHalf.Bottom });
        page.Rows.Insert(insertAt, new CellGridRow { TierId = iconHalfId, Template = "", Align = "center", SplitHalf = SplitHalf.Top });
        RefreshRowList();
        SelectRow(insertAt);
    }

    private void OnAddTextBlock(object? sender, EventArgs e)
    {
        if (_pageIndex < 0) return;

        // Inline dialog: choose number of lines
        using var dlg = new Form
        {
            Text            = "Bloque de texto",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MinimizeBox     = false,
            MaximizeBox     = false,
            ClientSize      = new Size(240, 88),
        };
        dlg.Controls.Add(new Label { Text = "Número de líneas:", Location = new Point(8, 16), AutoSize = true });
        var nud = new NumericUpDown { Location = new Point(134, 13), Size = new Size(50, 22), Minimum = 1, Maximum = 8, Value = 2 };
        dlg.Controls.Add(nud);
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(60, 52), Width = 70 };
        var btnCx = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(138, 52), Width = 76 };
        dlg.Controls.AddRange([btnOk, btnCx]);
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCx;

        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int lines = (int)nud.Value;

        var page = _pages[_pageIndex];
        byte tierId = _rowIndex >= 0 ? _pages[_pageIndex].Rows[_rowIndex].TierId : (byte)0;
        var tier = CellGridProtocol.Tiers[tierId];
        int remaining = BitmapFrame.Height - page.TotalHeight;
        if (remaining < tier.H * lines)
        {
            MessageBox.Show(
                $"No hay espacio suficiente ({tier.H * lines}px necesarios, {remaining}px libres).",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int insertAt = _rowIndex >= 0 ? _rowIndex + 1 : page.Rows.Count;
        for (int i = lines - 1; i >= 0; i--)
            page.Rows.Insert(insertAt,
                new CellGridRow { TierId = tierId, Template = $"{{ext.text.{i}}}", Align = "left" });
        RefreshRowList();
        SelectRow(insertAt);
    }

    // ── CLI ───────────────────────────────────────────────────────────────────

    private void OnOpenCli(object? sender, EventArgs e)
    {
        string zkcPath = Path.Combine(AppContext.BaseDirectory, "zkc.exe");
        if (!File.Exists(zkcPath))
        {
            MessageBox.Show($"No se encontró zkc.exe en:\n{zkcPath}",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName  = "cmd.exe",
                Arguments = $"/K \"{zkcPath}\" -h",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la terminal: {ex.Message}",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Library ───────────────────────────────────────────────────────────────

    private void RefreshLibraryList()
    {
        _lstLibFiles.Items.Clear();
        if (!Directory.Exists(LibraryDir)) return;
        foreach (var f in Directory.GetFiles(LibraryDir, "*.json").OrderBy(f => f))
            _lstLibFiles.Items.Add(Path.GetFileNameWithoutExtension(f));
    }

    private void OnLibSave(object? sender, EventArgs e)
    {
        string name = _txtLibName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Ingresa un nombre para la configuración.",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        Directory.CreateDirectory(LibraryDir);
        var snap = BuildSnapshot();
        var opts = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(Path.Combine(LibraryDir, name + ".json"),
            JsonSerializer.Serialize(snap, opts));
        RefreshLibraryList();
        int idx = _lstLibFiles.Items.IndexOf(name);
        if (idx >= 0) _lstLibFiles.SelectedIndex = idx;
    }

    private void OnLibLoad(object? sender, EventArgs e)
    {
        if (_lstLibFiles.SelectedItem is not string name) return;
        string path = Path.Combine(LibraryDir, name + ".json");
        if (!File.Exists(path)) { RefreshLibraryList(); return; }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var snap = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), opts) ?? new AppSettings();

            _pages.Clear();
            foreach (var p in snap.DisplayPages) _pages.Add(p.Clone());
            if (_pages.Count == 0) _pages.Add(new CellGridPage());

            _editLeagues  = snap.SelectedLeagues.ToList();
            _chkCycle.Checked = snap.CycleDisplayPages;
            _txtCity.Text = snap.City;
            bool f = snap.WeatherUnit == "fahrenheit";
            _radTempC.Checked = !f;
            _radTempF.Checked =  f;

            RebuildTeamInputs();
            LoadPage(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar la configuración: {ex.Message}",
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLibDelete(object? sender, EventArgs e)
    {
        if (_lstLibFiles.SelectedItem is not string name) return;
        if (MessageBox.Show($"¿Eliminar '{name}' de la biblioteca?", "ZMK Companion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string path = Path.Combine(LibraryDir, name + ".json");
        if (File.Exists(path)) File.Delete(path);
        RefreshLibraryList();
    }

    private AppSettings BuildSnapshot() => new()
    {
        DisplayPages      = _pages.Select(p => p.Clone()).ToList(),
        CycleDisplayPages = _chkCycle.Checked,
        City              = _txtCity.Text.Trim(),
        WeatherUnit       = _radTempF.Checked ? "fahrenheit" : "celsius",
        SelectedLeagues   = _editLeagues.Count > 0 ? _editLeagues : ["football/nfl"],
        SportsTeams       = _teamBoxes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Text))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim().ToUpper()),
        PomodoroWorkMin      = _settings.PomodoroWorkMin,
        PomodoroBreakMin     = _settings.PomodoroBreakMin,
        PomodoroCycles       = _settings.PomodoroCycles,
        PomodoroLongBreakMin = _settings.PomodoroLongBreakMin,
    };

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

        _settings.City            = _txtCity.Text.Trim();
        _settings.WeatherUnit     = _radTempF.Checked ? "fahrenheit" : "celsius";
        _settings.SelectedLeagues = _editLeagues.Count > 0 ? _editLeagues : ["football/nfl"];
        _settings.SportsTeams     = _teamBoxes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Text))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim().ToUpper());
        _settings.SportsTeam      = null;

        _onApply(_pages.Select(p => p.Clone()).ToList(), _chkCycle.Checked);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
