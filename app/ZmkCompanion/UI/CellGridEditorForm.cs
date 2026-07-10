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

    // Tier picker display order: CellGridProtocol.Tiers stays indexed by Id
    // (row.TierId is a raw array index everywhere else, e.g. Tiers[row.TierId]
    // in CellGridCompositor), so this is a presentation-only reordering, it
    // maps combo-box position -> actual Id, never touches the array itself.
    // Widest-text-capacity first: large_par(4 cols) ascending to
    // small_impar(11 cols), then icon_half(3 cols, special-cased on its own),
    // then the *_sq_* square tiers ascending from xlarge_sq_par(4 cols) to
    // micro(34 cols).
    private static readonly byte[] TierDisplayOrder =
        [5, 4, 3, 2, 1, 15, 0, 14, 13, 12, 11, 10, 9, 8, 7, 6];

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
    private readonly ComboBox      _cmbFontVariant;
    private readonly Panel         _rowEditorPanel;
    private          bool          _suppressRowUi;

    // Binding picker
    private readonly ComboBox      _cmbBindCategory;
    private readonly ComboBox      _cmbBind;

    // Data-source tabs
    private readonly TextBox       _txtCity;
    private readonly Label         _lblWeatherStatus;
    private readonly RadioButton   _radTempC, _radTempF;
    private readonly FlowLayoutPanel _weatherCitiesPanel;
    private          List<string>  _editWeatherCities;
    private const int MaxWeatherCities = 4;
    private readonly Panel         _teamsPanel;
    private readonly Dictionary<string, TextBox> _teamBoxes = new();
    private          List<string>  _editLeagues;
    private readonly Panel         _timeZonesPanel;
    private          List<string>  _editTimeZones;

    // Distinct categories from settings.CustomTokens, appended to the binding
    // picker after BindingCatalog. Computed once: this dialog doesn't need to
    // react to CustomTokensForm changes made while it's already open.
    private readonly List<string> _customCategories;

    // Library tab
    private readonly TextBox  _txtLibName;
    private readonly ListBox  _lstLibFiles;

    // CLI tab: raw command line the "Launch zkc" button runs verbatim in cmd
    // (may pipe another program into zkc, e.g. "python reloj.py | zkc -w",
    // so it's a shell command line, not just arguments to zkc.exe itself).
    private readonly TextBox _txtCliCommand;

    private static string LibraryDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZmkCompanion", "library");

    private const int DeportesCategory = 2;

    // Binding catalog — category → list of (label, token). Built per-instance
    // (not static readonly) so it reflects the language active when the
    // editor is opened, same reasoning as GlyphPickerDialog.Categories.
    private readonly (string Category, (string Label, string Token)[] Items)[] BindingCatalog = BuildBindingCatalog();

    private static (string Category, (string Label, string Token)[] Items)[] BuildBindingCatalog()
    {
        bool es = Strings.Current == AppLanguage.Es;
        return
        [
            // Merged with the old separate dynamic "Zona Horaria" category: every
            // token here is local time bare, and accepts an optional ":<IANA id>"
            // suffix for a foreign zone (see LiveState.TimeZoneAwareKeys) — the
            // Time Zone tab shows the full id to copy for that suffix, so this
            // list doesn't enumerate a token per selected city (would multiply
            // by however many cities the user has picked).
            (es ? "Zona Horaria" : "Time Zone", es ? [
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
            ] : [
                ("Time",                 "{time}"),
                ("Time 24h",             "{time24}"),
                ("Time 12h",             "{time12}"),
                ("Hour (HH)",            "{time.hh}"),
                ("Minutes (MM)",         "{time.mm}"),
                ("AM/PM",                "{ampm}"),
                ("Date",                 "{date}"),
                ("Day of month",         "{date.day}"),
                ("Day of month (DD)",    "{time.dd}"),
                ("Month",                "{date.month}"),
            ]),
            (es ? "Clima" : "Weather", es ? [
                ("Clima (resumen)",      "{weather}"),
                ("Icono clima",          "{weather.icon}"),
                ("Temperatura",          "{weather.temp}"),
                ("Ciudad clima",         "{weather.city}"),
            ] : [
                ("Weather (summary)",    "{weather}"),
                ("Weather icon",         "{weather.icon}"),
                ("Temperature",          "{weather.temp}"),
                ("Weather city",         "{weather.city}"),
            ]),
            // Index 2 = DeportesCategory — populated dynamically
            (es ? "Deportes" : "Sports", []),
            ("Pomodoro", es ? [
                ("Tiempo pomodoro",      "{pomodoro.time}"),
                ("Fase",                 "{pomodoro.phase}"),
                ("Barra progreso",       "{pomodoro.bar}"),
                ("Icono fase",           "{pomodoro.icon}"),
                ("Ciclo #",              "{pomodoro.cycle}"),
            ] : [
                ("Pomodoro time",        "{pomodoro.time}"),
                ("Phase",                "{pomodoro.phase}"),
                ("Progress bar",         "{pomodoro.bar}"),
                ("Phase icon",           "{pomodoro.icon}"),
                ("Cycle #",              "{pomodoro.cycle}"),
            ]),
            (es ? "Sistema" : "System", es ? [
                ("Batería (icono)",      "{battery.icon}"),
                ("Batería (%)",          "{battery.percent}"),
                ("Batería (nivel)",      "{battery.level}"),
                ("Conexión (icono)",     "{conn.icon}"),
                ("Tipo conexión",        "{conn.type}"),
                ("Perfil BLE",           "{conn.profile}"),
                ("Barra perfiles (5)",   "{conn.profilebar}"),
                ("Layer (número)",       "{layer.number}"),
                ("Layer (nombre)",       "{layer.name}"),
                ("WPM",                  "{wpm}"),
                ("Texto ext. (completo)","{ext.text}"),
                ("Texto ext. línea 1",   "{ext.text.0}"),
                ("Texto ext. línea 2",   "{ext.text.1}"),
                ("Texto ext. línea 3",   "{ext.text.2}"),
                ("Texto ext. línea 4",   "{ext.text.3}"),
            ] : [
                ("Battery (icon)",       "{battery.icon}"),
                ("Battery (%)",          "{battery.percent}"),
                ("Battery (level)",      "{battery.level}"),
                ("Connection (icon)",    "{conn.icon}"),
                ("Connection type",      "{conn.type}"),
                ("BLE profile",          "{conn.profile}"),
                ("Profile bar (5)",      "{conn.profilebar}"),
                ("Layer (number)",       "{layer.number}"),
                ("Layer (name)",         "{layer.name}"),
                ("WPM",                  "{wpm}"),
                ("Ext. text (full)",     "{ext.text}"),
                ("Ext. text line 1",     "{ext.text.0}"),
                ("Ext. text line 2",     "{ext.text.1}"),
                ("Ext. text line 3",     "{ext.text.2}"),
                ("Ext. text line 4",     "{ext.text.3}"),
            ]),
        ];
    }

    // ── Construction ─────────────────────────────────────────────────────────

    public CellGridEditorForm(AppSettings settings, LiveState liveState,
                              Action<List<CellGridPage>, bool> onApply)
    {
        _settings    = settings;
        _liveState   = liveState;
        _onApply     = onApply;
        _pages       = settings.DisplayPages.Select(p => p.Clone()).ToList();
        _editLeagues = settings.SelectedLeagues.ToList();
        _editTimeZones = settings.SelectedTimeZones.ToList();
        _editWeatherCities = settings.WeatherCities.ToList();
        _customCategories = settings.CustomTokens
            .Select(t => t.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        liveState.Changed += OnLiveStateChanged;

        Text            = Strings.EditorTitle;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        ClientSize      = new Size(640, 711);

        // ── RIGHT: preview ────────────────────────────────────────────────────
        var previewBox = new GroupBox
        {
            Text     = Strings.PreviewGroupTitle,
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
        var grpPages = new GroupBox { Text = Strings.PagesGroupTitle, Location = new Point(6, 6), Size = new Size(406, 90) };

        _cmbPages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(6, 18), Size = new Size(130, 23) };
        _cmbPages.SelectedIndexChanged += OnPageComboChanged;
        grpPages.Controls.Add(_cmbPages);

        var btnAddPage = new Button { Text = "+", Location = new Point(140, 18), Size = new Size(24, 23) };
        btnAddPage.Click += (_, _) =>
        {
            SyncCurrentPage();
            _pages.Add(new CellGridPage { Name = Strings.DefaultPageName(_pages.Count + 1) });
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

        grpPages.Controls.Add(new Label { Text = Strings.NameLabel, Location = new Point(6, 48), Size = new Size(52, 18) });
        _txtPageName = new TextBox { Location = new Point(62, 45), Size = new Size(120, 22) };
        _txtPageName.TextChanged += (_, _) =>
        {
            if (_suppressPageUi || _pageIndex < 0) return;
            _pages[_pageIndex].Name = _txtPageName.Text;
            RefreshPageCombo();
        };
        grpPages.Controls.Add(_txtPageName);

        _chkCycle = new CheckBox { Text = Strings.CyclePagesCheck, Checked = settings.CycleDisplayPages, Location = new Point(196, 18), Size = new Size(110, 22) };
        grpPages.Controls.Add(_chkCycle);

        grpPages.Controls.Add(new Label { Text = Strings.DurationLabel, Location = new Point(196, 48), Size = new Size(34, 18) });
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
        var grpRows = new GroupBox { Text = Strings.RowsGroupTitle, Location = new Point(6, 100), Size = new Size(406, 405) };

        _lstRows = new ListBox { Location = new Point(6, 18), Size = new Size(390, 100), IntegralHeight = false };
        _lstRows.SelectedIndexChanged += OnRowSelected;
        grpRows.Controls.Add(_lstRows);

        // Row action buttons
        var btnAddRow = new Button { Text = Strings.AddRowButton, Location = new Point(6, 122), Size = new Size(90, 24) };
        btnAddRow.Click += OnAddRow;
        grpRows.Controls.Add(btnAddRow);

        var btnRemRow = new Button { Text = Strings.DeleteButton, Location = new Point(100, 122), Size = new Size(70, 24) };
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

        var btnIconPair = new Button { Text = Strings.AddIconPairButton, Location = new Point(248, 122), Size = new Size(82, 24) };
        btnIconPair.Click += OnAddIconPair;
        grpRows.Controls.Add(btnIconPair);

        var btnTextBlock = new Button { Text = Strings.AddTextBlockButton, Location = new Point(334, 122), Size = new Size(58, 24) };
        btnTextBlock.Click += OnAddTextBlock;
        grpRows.Controls.Add(btnTextBlock);

        // ── Row editor sub-panel ──────────────────────────────────────────────
        _rowEditorPanel = new Panel { Location = new Point(6, 152), Size = new Size(390, 241), Enabled = false };

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.TierLabel, Location = new Point(0, 4), Size = new Size(30, 18) });
        _cmbTier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(34, 1), Size = new Size(160, 23) };
        foreach (byte id in TierDisplayOrder)
        {
            var t = CellGridProtocol.Tiers[id];
            _cmbTier.Items.Add($"{t.Name}  {t.W}×{t.H}px  ({t.Cols} {(t.Cols == 1 ? "col" : "cols")})");
        }
        _cmbTier.SelectedIndexChanged += OnTierChanged;
        _rowEditorPanel.Controls.Add(_cmbTier);

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.HalfLabel, Location = new Point(198, 4), Size = new Size(38, 18) });
        _cmbSplit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(238, 1), Size = new Size(148, 23) };
        _cmbSplit.Items.AddRange(Strings.SplitOptions);
        _cmbSplit.SelectedIndex = 0;
        _cmbSplit.SelectedIndexChanged += OnSplitChanged;
        _rowEditorPanel.Controls.Add(_cmbSplit);

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.TemplateLabel, Location = new Point(0, 32), Size = new Size(62, 18) });
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

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.AlignLabel, Location = new Point(0, 117), Size = new Size(40, 18) });
        _radLeft   = new RadioButton { Text = Strings.AlignLeft,   Location = new Point(44,  115), Size = new Size(50, 20) };
        _radCenter = new RadioButton { Text = Strings.AlignCenter, Location = new Point(96,  115), Size = new Size(62, 20), Checked = true };
        _radRight  = new RadioButton { Text = Strings.AlignRight,  Location = new Point(162, 115), Size = new Size(50, 20) };
        _radLeft.CheckedChanged   += OnAlignChanged;
        _radCenter.CheckedChanged += OnAlignChanged;
        _radRight.CheckedChanged  += OnAlignChanged;
        _rowEditorPanel.Controls.Add(_radLeft);
        _rowEditorPanel.Controls.Add(_radCenter);
        _rowEditorPanel.Controls.Add(_radRight);

        // ── Style row (Bold + AntiAlias + glyph styles) ──────────────────────
        _chkBold = new CheckBox { Text = Strings.BoldCheck, Location = new Point(0, 141), Size = new Size(52, 20) };
        _chkBold.CheckedChanged += OnBoldChanged;
        _rowEditorPanel.Controls.Add(_chkBold);

        _chkAntiAlias = new CheckBox { Text = "AA", Location = new Point(54, 141), Size = new Size(42, 20) };
        _chkAntiAlias.CheckedChanged += OnAntiAliasChanged;
        _rowEditorPanel.Controls.Add(_chkAntiAlias);

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.NumericLabel, Location = new Point(100, 144), Size = new Size(32, 18) });
        _cmbNumericStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(134, 141), Size = new Size(116, 23) };
        foreach (var s in new[] { "text", "box", "box_outline", "box_multiple", "plain", "circle", "circle_outline" })
            _cmbNumericStyle.Items.Add(NerdFont.NumericStyleLabel(s));
        _cmbNumericStyle.SelectedIndex = 0;
        _cmbNumericStyle.SelectedIndexChanged += OnNumericStyleChanged;
        _rowEditorPanel.Controls.Add(_cmbNumericStyle);

        _rowEditorPanel.Controls.Add(new Label { Text = Strings.AlphaLabel, Location = new Point(254, 144), Size = new Size(30, 18) });
        _cmbAlphaStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(284, 141), Size = new Size(100, 23) };
        foreach (var s in new[] { "text", "plain", "box", "box_outline", "circle", "circle_outline" })
            _cmbAlphaStyle.Items.Add(NerdFont.AlphaStyleLabel(s));
        _cmbAlphaStyle.SelectedIndex = 0;
        _cmbAlphaStyle.SelectedIndexChanged += OnAlphaStyleChanged;
        _rowEditorPanel.Controls.Add(_cmbAlphaStyle);

        // ── Font variant picker ───────────────────────────────────────────────
        // Mono (the app default) shrinks icon/box glyph ink to force a uniform
        // monospace advance width — that's what made weather icons and
        // {conn.profilebar} boxes look undersized/inconsistent. Regular/Propo
        // keep each glyph's natural size; pick per row where it matters.
        _rowEditorPanel.Controls.Add(new Label { Text = Strings.FontVariantLabel, Location = new Point(0, 183), Size = new Size(46, 18) });
        _cmbFontVariant = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(50, 180), Size = new Size(90, 23) };
        foreach (var v in Enum.GetValues<FontVariant>())
            _cmbFontVariant.Items.Add(NerdFont.VariantLabel(v));
        _cmbFontVariant.SelectedIndex = 0;
        _cmbFontVariant.SelectedIndexChanged += OnFontVariantChanged;
        _rowEditorPanel.Controls.Add(_cmbFontVariant);

        // ── Binding picker ────────────────────────────────────────────────────
        _rowEditorPanel.Controls.Add(new Label { Text = Strings.InsertLabel, Location = new Point(0, 209), Size = new Size(52, 18) });

        _cmbBindCategory = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(56, 206),
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
            Location      = new Point(150, 206),
            Size          = new Size(118, 23), // narrowed 160->118 to fit btnGlyph on the same row
        };
        _cmbBind.SelectedIndexChanged += (_, _) => { };
        _rowEditorPanel.Controls.Add(_cmbBind);
        PopulateBindings(0);

        var btnInsert = new Button { Text = Strings.InsertButton, Location = new Point(272, 206), Size = new Size(70, 23) };
        btnInsert.Click += OnInsertBinding;
        _rowEditorPanel.Controls.Add(btnInsert);

        // Full Nerd Font glyph picker (GlyphPickerDialog/FontCmapReader).
        var btnGlyph = new Button { Text = "NF…", Location = new Point(346, 206), Size = new Size(40, 23) };
        btnGlyph.Click += OnInsertGlyph;
        _rowEditorPanel.Controls.Add(btnGlyph);

        grpRows.Controls.Add(_rowEditorPanel);
        Controls.Add(grpRows);

        // ── LEFT: Data Sources — TabControl ───────────────────────────────────
        var tabData = new TabControl
        {
            Location = new Point(6, 511),
            Size     = new Size(406, 162),
        };

        // ── Tab: Deportes ─────────────────────────────────────────────────────
        var tabSports = new TabPage(Strings.SportsTab);
        tabSports.Controls.Add(new Label { Text = Strings.LeaguesTeamsLabel, Location = new Point(6, 6), AutoSize = true });
        var btnLeagues = new Button { Text = Strings.EditLeaguesButton, Location = new Point(280, 3), Size = new Size(110, 23) };
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

        // ── Tab: Zona Horaria ─────────────────────────────────────────────────
        var tabTimeZone = new TabPage(Strings.TimeZoneTab);
        tabTimeZone.Controls.Add(new Label { Text = Strings.TimeZonesLabel, Location = new Point(6, 6), AutoSize = true });
        var btnTimeZones = new Button { Text = Strings.EditTimeZonesButton, Location = new Point(280, 3), Size = new Size(110, 23) };
        btnTimeZones.Click += (_, _) =>
        {
            using var dlg = new TimeZonePickerDialog(_editTimeZones);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _editTimeZones = dlg.SelectedIds.ToList();
                RebuildTimeZoneLabels();
                PopulateBindings(_cmbBindCategory.SelectedIndex);
            }
        };
        tabTimeZone.Controls.Add(btnTimeZones);

        _timeZonesPanel = new Panel
        {
            Location   = new Point(4, 28),
            Size       = new Size(390, 54),
            AutoScroll = true,
        };
        tabTimeZone.Controls.Add(_timeZonesPanel);

        // Convention hint: the "Zona Horaria"/"Time Zone" binding-picker
        // category only lists the bare local tokens ({time}, {date}, ...) to
        // avoid one entry per token × selected city; a foreign city is instead
        // targeted by hand-appending ":<id>", using the full id shown above,
        // not the short code (LiveState.Resolve rejects "BOG", it needs the
        // real IANA id, e.g. "America/Bogota").
        tabTimeZone.Controls.Add(new Label
        {
            Text      = Strings.TimeZoneSuffixHint,
            Location  = new Point(4, 84),
            Size      = new Size(390, 30),
            ForeColor = Color.Gray,
            AutoSize  = false,
            Font      = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f),
        });

        // ── Tab: Clima ────────────────────────────────────────────────────────
        // Up to MaxWeatherCities cities: unlike Sports/Time Zone there's no
        // finite catalog to browse (any place name is technically valid), so
        // instead of a modal picker dialog, "add" happens inline here — type a
        // name, it's validated against the geocoding API (same one
        // WeatherFeature already calls) before being accepted into the list.
        var tabWeather = new TabPage(Strings.WeatherTab);
        tabWeather.Controls.Add(new Label { Text = Strings.CityLabel, Location = new Point(6, 6), AutoSize = true });
        _txtCity = new TextBox
        {
            Location        = new Point(58, 3),
            Size            = new Size(150, 22),
            PlaceholderText = "Madrid",
        };
        tabWeather.Controls.Add(_txtCity);

        var btnAddWeatherCity = new Button { Text = Strings.Add, Location = new Point(212, 3), Size = new Size(80, 22) };
        btnAddWeatherCity.Click += (_, _) => _ = OnAddWeatherCityAsync();
        tabWeather.Controls.Add(btnAddWeatherCity);
        _txtCity.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { _ = OnAddWeatherCityAsync(); e.SuppressKeyPress = true; } };

        _lblWeatherStatus = new Label
        {
            Text      = "…",
            Location  = new Point(6, 27),
            Size      = new Size(378, 16),
            ForeColor = Color.Gray,
            AutoSize  = false,
        };
        tabWeather.Controls.Add(_lblWeatherStatus);

        _weatherCitiesPanel = new FlowLayoutPanel
        {
            Location     = new Point(4, 45),
            Size         = new Size(390, 30),
            AutoScroll   = true,
            WrapContents = true,
        };
        tabWeather.Controls.Add(_weatherCitiesPanel);

        // Convention hint: {weather.*} accepts an optional ":<city>" suffix for
        // any additional configured city besides the first/default one, using
        // the exact text shown on its chip above (no separate id to look up,
        // unlike Time Zone's IANA ids).
        tabWeather.Controls.Add(new Label
        {
            Text      = Strings.WeatherSuffixHint,
            Location  = new Point(4, 77),
            Size      = new Size(390, 14),
            ForeColor = Color.Gray,
            AutoSize  = false,
            Font      = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f),
        });

        tabWeather.Controls.Add(new Label { Text = Strings.TemperatureLabel, Location = new Point(6, 94), AutoSize = true });
        bool isFahrenheit = settings.WeatherUnit == "fahrenheit";
        _radTempC = new RadioButton { Text = "°C", Location = new Point(90, 92), Size = new Size(42, 20), Checked = !isFahrenheit };
        _radTempF = new RadioButton { Text = "°F", Location = new Point(136, 92), Size = new Size(42, 20), Checked =  isFahrenheit };
        tabWeather.Controls.AddRange([_radTempC, _radTempF]);

        // ── Tab: CLI ──────────────────────────────────────────────────────────
        var tabCli = new TabPage("CLI"); // "CLI" is an acronym, invariant across languages
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

        var btnCopyPath = new Button { Text = Strings.CopyButton, Location = new Point(320, 5), Size = new Size(60, 22) };
        btnCopyPath.Click += (_, _) => Clipboard.SetText(zkcPath);
        tabCli.Controls.Add(btnCopyPath);

        tabCli.Controls.Add(new Label { Text = Strings.CliCommandLabel, Location = new Point(4, 30), Size = new Size(390, 14) });
        _txtCliCommand = new TextBox
        {
            Location        = new Point(4, 45),
            Size            = new Size(390, 20),
            PlaceholderText = "python reloj.py | zkc -w",
            Text            = settings.CliLastCommand,
        };
        tabCli.Controls.Add(_txtCliCommand);

        var btnOpenCli = new Button { Text = Strings.LaunchCliButton, Location = new Point(4, 68), Size = new Size(140, 24) };
        btnOpenCli.Click += OnOpenCli;
        tabCli.Controls.Add(btnOpenCli);

        var lblCliHint = new Label
        {
            Text      = Strings.CliHint,
            Location  = new Point(4, 94),
            Size      = new Size(390, 28),
            ForeColor = Color.Gray,
            Font      = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f),
        };
        tabCli.Controls.Add(lblCliHint);

        // ── Tab: Biblioteca ───────────────────────────────────────────────────
        var tabLib = new TabPage(Strings.LibraryTab);
        tabLib.Controls.Add(new Label { Text = Strings.NameLabel, Location = new Point(4, 8), AutoSize = true });
        _txtLibName = new TextBox { Location = new Point(58, 5), Size = new Size(200, 22) };
        tabLib.Controls.Add(_txtLibName);

        var btnLibSave = new Button { Text = Strings.SaveButton, Location = new Point(264, 5), Size = new Size(70, 22) };
        btnLibSave.Click += OnLibSave;
        tabLib.Controls.Add(btnLibSave);

        _lstLibFiles = new ListBox
        {
            Location       = new Point(4, 32),
            Size           = new Size(274, 66),
            IntegralHeight = false,
        };
        tabLib.Controls.Add(_lstLibFiles);

        var btnLibLoad = new Button { Text = Strings.LoadButton, Location = new Point(284, 32), Size = new Size(106, 24) };
        btnLibLoad.Click += OnLibLoad;
        tabLib.Controls.Add(btnLibLoad);

        var btnLibDel = new Button { Text = Strings.DeleteButton, Location = new Point(284, 60), Size = new Size(106, 24) };
        btnLibDel.Click += OnLibDelete;
        tabLib.Controls.Add(btnLibDel);

        // Add order determines both left-to-right tab order and which tab is
        // selected by default (TabControl.SelectedIndex defaults to 0, the
        // first one added) — Library first so the editor opens there.
        tabData.TabPages.Add(tabLib);
        tabData.TabPages.Add(tabTimeZone);
        tabData.TabPages.Add(tabWeather);
        tabData.TabPages.Add(tabSports);
        tabData.TabPages.Add(tabCli);
        Controls.Add(tabData);

        // ── Bottom buttons ────────────────────────────────────────────────────
        var btnApply = new Button { Text = Strings.ApplyButton, Location = new Point(270, 679), Size = new Size(74, 28), DialogResult = DialogResult.OK };
        btnApply.Click += OnApply;
        var btnClose = new Button { Text = Strings.Close, Location = new Point(350, 679), Size = new Size(74, 28) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnApply);
        Controls.Add(btnClose);

        FormClosed += (_, _) => _liveState.Changed -= OnLiveStateChanged;

        // ── Initialize ───────────────────────────────────────────────────────
        if (_pages.Count == 0) _pages.Add(new CellGridPage());
        LoadPage(0);
        RebuildTeamInputs();
        RebuildTimeZoneLabels();
        RebuildWeatherCityLabels();
        RefreshLibraryList();

        _ = RefreshWeatherPreviewAsync(_editWeatherCities.FirstOrDefault() ?? "", "default");
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
        bool es = Strings.Current == AppLanguage.Es;
        var items = es ? new List<(string, string)>
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
        } : new List<(string, string)>
        {
            ("Game (summary)",       "{sports}"),
            ("Team",                 "{sports.team}"),
            ("Live teams",           "{sports.live_game}"),
            ("Live score",           "{sports.live_marker}"),
            ("Status (icon)",        "{sports.marker}"),
            ("Live time",            "{sports.live_time}"),
            ("League",               "{sports.league}"),
            ("Last game",            "{sports.last_game}"),
            ("Last score",           "{sports.last_marker}"),
            ("Next game",            "{sports.next_game}"),
            ("Next date",            "{sports.next_date}"),
            ("Next time",            "{sports.next_gametime}"),
        };

        if (_editLeagues.Count > 1)
        {
            foreach (var path in _editLeagues)
            {
                var lg = SportsFeature.FindOrCreate(path);
                string q = ":" + lg.ShortName;
                if (es)
                {
                    items.Add(($"[{lg.ShortName}] Últ. partido",   $"{{sports.last_game{q}}}"));
                    items.Add(($"[{lg.ShortName}] Últ. marcador",  $"{{sports.last_marker{q}}}"));
                    items.Add(($"[{lg.ShortName}] Próx. partido",  $"{{sports.next_game{q}}}"));
                    items.Add(($"[{lg.ShortName}] Próx. fecha",    $"{{sports.next_date{q}}}"));
                    items.Add(($"[{lg.ShortName}] Próx. hora",     $"{{sports.next_gametime{q}}}"));
                    items.Add(($"[{lg.ShortName}] Liga",           $"{{sports.league{q}}}"));
                    items.Add(($"[{lg.ShortName}] Equipo",         $"{{sports.team{q}}}"));
                }
                else
                {
                    items.Add(($"[{lg.ShortName}] Last game",   $"{{sports.last_game{q}}}"));
                    items.Add(($"[{lg.ShortName}] Last score",  $"{{sports.last_marker{q}}}"));
                    items.Add(($"[{lg.ShortName}] Next game",   $"{{sports.next_game{q}}}"));
                    items.Add(($"[{lg.ShortName}] Next date",   $"{{sports.next_date{q}}}"));
                    items.Add(($"[{lg.ShortName}] Next time",   $"{{sports.next_gametime{q}}}"));
                    items.Add(($"[{lg.ShortName}] League",      $"{{sports.league{q}}}"));
                    items.Add(($"[{lg.ShortName}] Team",        $"{{sports.team{q}}}"));
                }
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

        // {conn.profilebar} is Material Design numeric-box glyphs — Mono's
        // shrunk-ink sizing (see NerdFont/FontVariant) makes its 5 boxes look
        // mismatched in size; Propo keeps their natural size. Only switches
        // the row that receives the insert, not existing rows.
        if (token == "{conn.profilebar}" && _rowIndex >= 0 && _pageIndex >= 0)
            _cmbFontVariant.SelectedIndex = (int)FontVariant.Propo; // triggers OnFontVariantChanged
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
            var tb  = new TextBox { Text = team, Location = new Point(px + 56, py), Size = new Size(68, 22), PlaceholderText = Strings.TeamPlaceholder };
            _teamBoxes[path] = tb;
            _teamsPanel.Controls.AddRange([lbl, tb]);

            col++;
            if (col >= colsPerRow) { col = 0; row++; }
        }
    }

    // Read-only display of the zones picked in TimeZonePickerDialog. No
    // per-zone input here (unlike RebuildTeamInputs' team-filter textboxes),
    // there's nothing to configure per zone besides its id, which the picker
    // already owns, removing a zone happens there, not in this tab.
    //
    // Shows the full IANA id (not just the short code) so the user has
    // something to literally copy into a template's ":<id>" suffix, e.g.
    // "{time:America/Bogota}" — see the hint label built in the constructor.
    private void RebuildTimeZoneLabels()
    {
        _timeZonesPanel.Controls.Clear();

        int row = 0;
        const int rowH = 18;

        foreach (var id in _editTimeZones)
        {
            var tz = TimeZoneCatalog.FindOrCreate(id);
            var lbl = new Label
            {
                Text     = $"{tz.DisplayName} ({tz.ShortName})  →  {tz.Id}",
                Location = new Point(0, row * rowH),
                Size     = new Size(374, 18),
                AutoSize = false,
            };
            _timeZonesPanel.Controls.Add(lbl);
            row++;
        }
    }

    // ── Weather cities ─────────────────────────────────────────────────────────

    // No modal picker here (unlike Sports/Time Zone): there's no finite catalog
    // to browse, any place name is potentially valid, so "add" means validating
    // the typed name against the same geocoding call WeatherFeature already
    // makes, then accepting it into the list on success.
    private async Task OnAddWeatherCityAsync()
    {
        string city = _txtCity.Text.Trim();
        if (city.Length == 0) return;
        if (_editWeatherCities.Count >= MaxWeatherCities)
        {
            MessageBox.Show(this, Strings.WeatherCityLimitBody(MaxWeatherCities),
                Strings.WeatherCityLimitTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_editWeatherCities.Any(c => c.Equals(city, StringComparison.OrdinalIgnoreCase)))
            return; // already added

        bool isFirst = _editWeatherCities.Count == 0;
        bool ok = await RefreshWeatherPreviewAsync(city, city, alsoDefault: isFirst);
        if (!ok) return; // _lblWeatherStatus already shows the error

        _editWeatherCities.Add(city);
        _txtCity.Text = "";
        RebuildWeatherCityLabels();
        PopulateBindings(_cmbBindCategory.SelectedIndex);
    }

    private void RebuildWeatherCityLabels()
    {
        _weatherCitiesPanel.Controls.Clear();
        if (_editWeatherCities.Count == 0)
        {
            _weatherCitiesPanel.Controls.Add(new Label
            {
                Text     = Strings.WeatherAutoDetectHint,
                AutoSize = true,
                ForeColor = Color.Gray,
            });
            return;
        }

        foreach (var city in _editWeatherCities.ToList())
        {
            var chip = new Button
            {
                Text      = $"{city}  ×",
                AutoSize  = true,
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(2),
            };
            chip.FlatAppearance.BorderSize = 1;
            chip.Click += (_, _) =>
            {
                _editWeatherCities.Remove(city);
                RebuildWeatherCityLabels();
                PopulateBindings(_cmbBindCategory.SelectedIndex);
            };
            _weatherCitiesPanel.Controls.Add(chip);
        }
    }

    // ── Weather preview refresh ───────────────────────────────────────────────

    // Validates `city` against the live API and, on success, updates the
    // preview's LiveState under `cityKey` (and also "default" when
    // alsoDefault, for whichever city is first/bare-token-backed). Returns
    // whether the fetch succeeded, callers use this to gate adding the city.
    private async Task<bool> RefreshWeatherPreviewAsync(string city, string cityKey, bool alsoDefault = false)
    {
        _lblWeatherStatus.Text      = Strings.QueryingWeather;
        _lblWeatherStatus.ForeColor = Color.Gray;
        try
        {
            var data = await WeatherFeature.FetchWeatherAsync(city);
            bool fahrenheit = _radTempF.Checked;
            string tempStr  = fahrenheit
                ? $"{data.TempC * 9 / 5 + 32:F0}°F"
                : $"{data.TempC:F0}°";
            _liveState.UpdateWeather(cityKey, data.Icon.ToString(), tempStr, data.City);
            if (alsoDefault && cityKey != "default")
                _liveState.UpdateWeather("default", data.Icon.ToString(), tempStr, data.City);
            _lblWeatherStatus.Text      = $"{data.City} {tempStr}";
            _lblWeatherStatus.ForeColor = Color.LimeGreen;
            return true;
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            _lblWeatherStatus.Text      = Strings.WeatherHttpError((int)ex.StatusCode.Value);
            _lblWeatherStatus.ForeColor = Color.OrangeRed;
            return false;
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            _lblWeatherStatus.Text      = msg.Length > 34 ? msg[..34] + "…" : msg;
            _lblWeatherStatus.ForeColor = Color.OrangeRed;
            return false;
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
        _cmbTier.SelectedIndex         = Array.IndexOf(TierDisplayOrder, row.TierId);
        _cmbSplit.SelectedIndex        = (int)row.SplitHalf;
        _txtTemplate.Text              = row.Template;
        _chkBold.Checked               = row.Bold;
        _chkAntiAlias.Checked          = row.AntiAlias;
        _cmbNumericStyle.SelectedIndex = NumericStyleIndex(row.NumericStyle);
        _cmbAlphaStyle.SelectedIndex   = AlphaStyleIndex(row.AlphaStyle);
        _cmbFontVariant.SelectedIndex  = (int)row.FontVariant;
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
        _pages[_pageIndex].Rows[_rowIndex].TierId = TierDisplayOrder[_cmbTier.SelectedIndex];
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

    private void OnFontVariantChanged(object? sender, EventArgs e)
    {
        if (_suppressRowUi || _rowIndex < 0 || _pageIndex < 0) return;
        _pages[_pageIndex].Rows[_rowIndex].FontVariant = (FontVariant)_cmbFontVariant.SelectedIndex;
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
                Strings.NotEnoughSpaceIconPair(tier.H * 2, remaining),
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
            Text            = Strings.TextBlockDialogTitle,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MinimizeBox     = false,
            MaximizeBox     = false,
            ClientSize      = new Size(240, 88),
        };
        dlg.Controls.Add(new Label { Text = Strings.NumberOfLinesLabel, Location = new Point(8, 16), AutoSize = true });
        var nud = new NumericUpDown { Location = new Point(134, 13), Size = new Size(50, 22), Minimum = 1, Maximum = 8, Value = 2 };
        dlg.Controls.Add(nud);
        var btnOk = new Button { Text = Strings.Ok, DialogResult = DialogResult.OK, Location = new Point(60, 52), Width = 70 };
        var btnCx = new Button { Text = Strings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(138, 52), Width = 76 };
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
                Strings.NotEnoughSpace(tier.H * lines, remaining),
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
            MessageBox.Show(Strings.ZkcNotFound(zkcPath),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // The command field holds a full shell command line, not just zkc
        // arguments (the documented example pipes another program into zkc:
        // "python reloj.py | zkc -w"), so run it verbatim via cmd /K rather
        // than trying to parse/append it as zkc's argv. Blank falls back to
        // the previous fixed "zkc -h" behavior.
        string command  = _txtCliCommand.Text.Trim();
        string cmdArgs  = command.Length > 0 ? command : $"\"{zkcPath}\" -h";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName  = "cmd.exe",
                Arguments = $"/K {cmdArgs}",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.CouldNotOpenTerminal(ex.Message),
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
            MessageBox.Show(Strings.EnterConfigName,
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

            _editLeagues       = snap.SelectedLeagues.ToList();
            _editTimeZones     = snap.SelectedTimeZones.ToList();
            _editWeatherCities = snap.WeatherCities.ToList();
            _chkCycle.Checked = snap.CycleDisplayPages;
            bool f = snap.WeatherUnit == "fahrenheit";
            _radTempC.Checked = !f;
            _radTempF.Checked =  f;

            RebuildTeamInputs();
            RebuildTimeZoneLabels();
            RebuildWeatherCityLabels();
            LoadPage(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.ErrorLoadingConfig(ex.Message),
                "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLibDelete(object? sender, EventArgs e)
    {
        if (_lstLibFiles.SelectedItem is not string name) return;
        if (MessageBox.Show(Strings.ConfirmDeleteLibraryItem(name), "ZMK Companion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string path = Path.Combine(LibraryDir, name + ".json");
        if (File.Exists(path)) File.Delete(path);
        RefreshLibraryList();
    }

    private AppSettings BuildSnapshot() => new()
    {
        DisplayPages      = _pages.Select(p => p.Clone()).ToList(),
        CycleDisplayPages = _chkCycle.Checked,
        WeatherCities     = _editWeatherCities,
        WeatherUnit       = _radTempF.Checked ? "fahrenheit" : "celsius",
        SelectedLeagues   = _editLeagues.Count > 0 ? _editLeagues : ["football/nfl"],
        SelectedTimeZones = _editTimeZones,
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
                    Strings.PageExceedsHeight(page.Name, page.TotalHeight, BitmapFrame.Height),
                    "ZMK Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _settings.WeatherCities     = _editWeatherCities;
        _settings.WeatherUnit       = _radTempF.Checked ? "fahrenheit" : "celsius";
        _settings.SelectedLeagues   = _editLeagues.Count > 0 ? _editLeagues : ["football/nfl"];
        _settings.SelectedTimeZones = _editTimeZones;
        _settings.SportsTeams       = _teamBoxes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Text))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim().ToUpper());
        _settings.SportsTeam      = null;
        _settings.CliLastCommand  = _txtCliCommand.Text;

        _onApply(_pages.Select(p => p.Clone()).ToList(), _chkCycle.Checked);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
