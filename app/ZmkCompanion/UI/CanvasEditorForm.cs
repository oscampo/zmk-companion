using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features.Widgets;

namespace ZmkCompanion.UI;

// Visual canvas editor: shows the 68×160 display at 3× scale.
// CX/CY coordinates represent the CENTER of each widget's bounding box.
sealed class CanvasEditorForm : Form
{
    private const int Zoom    = 3;
    private const int CanvasW = BitmapFrame.Width  * Zoom;  // 204
    private const int CanvasH = BitmapFrame.Height * Zoom;  // 480

    private readonly AppSettings                   _settings;
    private readonly LiveState                     _liveState;
    private readonly Action<List<WidgetPlacement>> _onApply;

    private readonly List<WidgetPlacement> _placements = [];
    private readonly List<IWidget>         _previews   = [];

    private int   _sel        = -1;
    private bool  _dragging;
    private Point _dragOrigin;
    private int   _dragInitCX, _dragInitCY;

    private readonly Panel         _canvas;
    private readonly ListBox       _listWidgets;
    private readonly NumericUpDown _nudCX, _nudCY, _nudW, _nudH;
    private readonly CheckBox      _chkFull;
    private readonly Panel         _propsPanel;
    private readonly GroupBox      _grpProps;
    private          bool          _suppressNud;
    private          int           _propsY;

    // ── Construction ─────────────────────────────────────────────────────────

    public CanvasEditorForm(AppSettings settings, LiveState liveState, Action<List<WidgetPlacement>> onApply)
    {
        _settings  = settings;
        _liveState = liveState;
        _onApply   = onApply;

        Text            = "ZMK Companion — Canvas Editor";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        ClientSize      = new Size(532, 630);

        // ── Left: canvas preview ─────────────────────────────────────────────
        _canvas = new Panel
        {
            Location    = new Point(8, 8),
            Size        = new Size(CanvasW, CanvasH),
            BackColor   = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _canvas.Paint     += OnCanvasPaint;
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseUp   += (_, _) => _dragging = false;
        Controls.Add(_canvas);

        int rx = CanvasW + 20;  // 224

        // ── Widgets group ────────────────────────────────────────────────────
        var grpWidgets = new GroupBox { Text = "Widgets", Location = new Point(rx, 8), Size = new Size(298, 218) };

        _listWidgets = new ListBox { Location = new Point(8, 20), Size = new Size(282, 138) };
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
        grpWidgets.Controls.Add(_listWidgets);

        var btnAdd = new Button { Text = "Add ▾", Location = new Point(8, 166), Size = new Size(135, 26) };
        btnAdd.Click += OnAddClick;
        grpWidgets.Controls.Add(btnAdd);

        var btnRemove = new Button { Text = "Remove", Location = new Point(151, 166), Size = new Size(131, 26) };
        btnRemove.Click += OnRemoveClick;
        grpWidgets.Controls.Add(btnRemove);

        Controls.Add(grpWidgets);

        // ── Position & size group ─────────────────────────────────────────────
        var grpBounds = new GroupBox { Text = "Position && Size  (CX / CY = center)", Location = new Point(rx, 234), Size = new Size(298, 130) };

        grpBounds.Controls.Add(MakeLbl("CX:", 4,  24)); _nudCX = MakeNud(30,  20, 0, BitmapFrame.Width);
        grpBounds.Controls.Add(MakeLbl("CY:", 98, 24)); _nudCY = MakeNud(124, 20, 0, BitmapFrame.Height);
        grpBounds.Controls.Add(MakeLbl("W:",  4,  58)); _nudW  = MakeNud(30,  54, 1, BitmapFrame.Width);
        grpBounds.Controls.Add(MakeLbl("H:",  98, 58)); _nudH  = MakeNud(124, 54, 1, BitmapFrame.Height);

        _nudCX.ValueChanged += (_, _) => ApplyNudToSelected();
        _nudCY.ValueChanged += (_, _) => ApplyNudToSelected();
        _nudW .ValueChanged += (_, _) => ApplyNudToSelected();
        _nudH .ValueChanged += (_, _) => ApplyNudToSelected();

        grpBounds.Controls.AddRange([_nudCX, _nudCY, _nudW, _nudH]);

        _chkFull = new CheckBox { Text = "Full canvas (68×160)", Location = new Point(8, 94), Size = new Size(200, 23) };
        _chkFull.CheckedChanged += OnFullCanvasChecked;
        grpBounds.Controls.Add(_chkFull);

        Controls.Add(grpBounds);

        // ── Properties group (scrollable, rebuilt per widget type) ────────────
        _grpProps = new GroupBox { Text = "Properties", Location = new Point(rx, 372), Size = new Size(298, 210) };
        _propsPanel = new Panel
        {
            Location    = new Point(4, 18),
            Size        = new Size(288, 188),
            AutoScroll  = true,
        };
        _grpProps.Controls.Add(_propsPanel);
        Controls.Add(_grpProps);

        // ── Bottom buttons ────────────────────────────────────────────────────
        var btnApply = new Button { Text = "Apply && Send", Location = new Point(rx, 594), Size = new Size(138, 30) };
        btnApply.Click += (_, _) => Apply();
        Controls.Add(btnApply);

        var btnClose = new Button { Text = "Close", Location = new Point(rx + 148, 594), Size = new Size(110, 30) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        // ── Load + initial selection ──────────────────────────────────────────
        LoadFromSettings();
        if (_placements.Count > 0) _sel = 0;
        RefreshWidgetList();
        RefreshBoundsPanel();
        RefreshPropsPanel();

        var refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        refreshTimer.Tick    += (_, _) => _canvas.Invalidate();
        refreshTimer.Start();
        FormClosed += (_, _) => { refreshTimer.Stop(); refreshTimer.Dispose(); };
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        foreach (var p in _settings.Canvas)
        {
            var clone = p.Clone();
            _placements.Add(clone);
            _previews.Add(MakePreview(clone));
        }
    }

    internal IWidget MakePreview(WidgetPlacement p)
    {
        var bounds = p.ToRectangle();
        return p.Type switch
        {
            "label"      => new LabelWidget(_liveState)      { Bounds = bounds, Config = p.GetConfig<LabelConfig>() },
            "profilebar" => new ProfileBarWidget(_liveState) { Bounds = bounds, Config = p.GetConfig<ProfileBarConfig>() },
            "battery"    => new BatteryWidget    { Bounds = bounds, Config = p.GetConfig<BatteryConfig>() },
            "connection" => new ConnectionWidget { Bounds = bounds, Config = p.GetConfig<ConnectionConfig>() },
            _            => new ClockWidget      { Bounds = bounds, Config = p.GetConfig<ClockConfig>() },
        };
    }

    private void Apply() => _onApply(_placements.Select(p => p.Clone()).ToList());

    // ── Canvas paint ──────────────────────────────────────────────────────────

    private static readonly Color[] _overlays =
    [
        Color.FromArgb(70,   0, 120, 215),
        Color.FromArgb(70,   0, 180,  80),
        Color.FromArgb(70, 215, 120,   0),
        Color.FromArgb(70, 180,   0, 215),
    ];

    private void OnCanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        using var bmp = BitmapFrame.CreateCanvas();
        using var bg  = Graphics.FromImage(bmp);
        bg.Clear(Color.Black);
        bg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        for (int i = 0; i < _previews.Count; i++)
        {
            var saved = bg.Clip;
            bg.SetClip(_previews[i].Bounds);
            _previews[i].Render(bg);
            bg.Clip = saved;
        }

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode   = PixelOffsetMode.Half;
        g.DrawImage(bmp, 0, 0, CanvasW, CanvasH);

        for (int i = 0; i < _placements.Count; i++)
        {
            bool sel = i == _sel;
            var  b   = ZoomRect(_previews[i].Bounds);
            using var brush = new SolidBrush(sel ? Color.FromArgb(50, 255, 255, 255) : _overlays[i % _overlays.Length]);
            g.FillRectangle(brush, b);
            using var pen = new Pen(sel ? Color.White : Color.FromArgb(200, 100, 160, 255), sel ? 2f : 1f);
            g.DrawRectangle(pen, b.X, b.Y, b.Width - 1, b.Height - 1);
        }
    }

    private static Rectangle ZoomRect(Rectangle r) =>
        new(r.X * Zoom, r.Y * Zoom, r.Width * Zoom, r.Height * Zoom);

    // ── Canvas mouse ──────────────────────────────────────────────────────────

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        var pos = new Point(e.X / Zoom, e.Y / Zoom);
        _sel = -1;
        for (int i = _previews.Count - 1; i >= 0; i--)
        {
            if (!_previews[i].Bounds.Contains(pos)) continue;
            _sel        = i;
            _dragging   = true;
            _dragOrigin = pos;
            _dragInitCX = _placements[i].CX;
            _dragInitCY = _placements[i].CY;
            break;
        }
        SyncListSelection();
        RefreshBoundsPanel();
        RefreshPropsPanel();
        _canvas.Invalidate();
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || _sel < 0) return;
        var pos = new Point(e.X / Zoom, e.Y / Zoom);
        var p   = _placements[_sel];
        p.CX = Math.Clamp(_dragInitCX + (pos.X - _dragOrigin.X), p.W / 2, BitmapFrame.Width  - p.W / 2);
        p.CY = Math.Clamp(_dragInitCY + (pos.Y - _dragOrigin.Y), p.H / 2, BitmapFrame.Height - p.H / 2);
        _previews[_sel].Bounds = p.ToRectangle();
        RefreshBoundsPanel();
        _canvas.Invalidate();
    }

    // ── Widget list ───────────────────────────────────────────────────────────

    private void RefreshWidgetList()
    {
        _listWidgets.SelectedIndexChanged -= OnListSelectionChanged;
        _listWidgets.BeginUpdate();
        _listWidgets.Items.Clear();
        foreach (var p in _placements) _listWidgets.Items.Add(WidgetListLabel(p));
        if (_sel >= 0 && _sel < _listWidgets.Items.Count) _listWidgets.SelectedIndex = _sel;
        _listWidgets.EndUpdate();
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
    }

    private void SyncListSelection()
    {
        _listWidgets.SelectedIndexChanged -= OnListSelectionChanged;
        if (_sel >= 0 && _sel < _listWidgets.Items.Count) _listWidgets.SelectedIndex = _sel;
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
    }

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        _sel = _listWidgets.SelectedIndex;
        RefreshBoundsPanel();
        RefreshPropsPanel();
        _canvas.Invalidate();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Label (generic)",  null, (_, _) => AddWidget("label")));
        menu.Items.Add(new ToolStripMenuItem("Profile Bar (BLE 1-5)", null, (_, _) => AddWidget("profilebar")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Clock (legacy)",   null, (_, _) => AddWidget("clock")));
        menu.Items.Add(new ToolStripMenuItem("Battery (legacy)", null, (_, _) => AddWidget("battery")));
        menu.Items.Add(new ToolStripMenuItem("Connection (legacy)", null, (_, _) => AddWidget("connection")));
        var btn = (Button)sender!;
        menu.Show(btn, new Point(0, btn.Height));
    }

    private void AddWidget(string type)
    {
        var p = new WidgetPlacement { Type = type };
        // Default label: a centered clock, big font
        if (type == "label")
            p.SetConfig(new LabelConfig { Template = "{time}", Size = 24f, Bold = true });
        _placements.Add(p);
        _previews.Add(MakePreview(p));
        _sel = _placements.Count - 1;
        RefreshWidgetList();
        RefreshBoundsPanel();
        RefreshPropsPanel();
        _canvas.Invalidate();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_sel < 0 || _sel >= _placements.Count) return;
        _previews[_sel].Dispose();
        _previews.RemoveAt(_sel);
        _placements.RemoveAt(_sel);
        _sel = Math.Min(_sel, _placements.Count - 1);
        RefreshWidgetList();
        RefreshBoundsPanel();
        RefreshPropsPanel();
        _canvas.Invalidate();
    }

    private static string WidgetListLabel(WidgetPlacement p)
    {
        if (p.Type == "label")
        {
            var cfg = p.GetConfig<LabelConfig>();
            string preview = cfg.Template.Length > 22 ? cfg.Template[..22] + "…" : cfg.Template;
            return $"Label: {preview}";
        }
        return p.Type switch
        {
            "profilebar" => "Profile Bar",
            "clock"      => "Clock (legacy)",
            "battery"    => "Battery (legacy)",
            "connection" => "Connection (legacy)",
            _            => p.Type,
        };
    }

    // ── Bounds panel ──────────────────────────────────────────────────────────

    private void RefreshBoundsPanel()
    {
        bool active = _sel >= 0 && _sel < _placements.Count;
        _nudCX.Enabled = _nudCY.Enabled = _nudW.Enabled = _nudH.Enabled = _chkFull.Enabled = active;
        if (!active) return;

        var p = _placements[_sel];
        _suppressNud     = true;
        _nudCX.Value     = p.CX;
        _nudCY.Value     = p.CY;
        _nudW .Value     = p.W;
        _nudH .Value     = p.H;
        _chkFull.Checked = p.CX == BitmapFrame.Width / 2 && p.CY == BitmapFrame.Height / 2
                        && p.W  == BitmapFrame.Width      && p.H  == BitmapFrame.Height;
        _suppressNud = false;
    }

    private void ApplyNudToSelected()
    {
        if (_suppressNud || _sel < 0 || _sel >= _placements.Count) return;
        var p = _placements[_sel];
        p.CX = (int)_nudCX.Value;
        p.CY = (int)_nudCY.Value;
        p.W  = (int)_nudW .Value;
        p.H  = (int)_nudH .Value;
        _previews[_sel].Bounds = p.ToRectangle();
        _chkFull.Checked = p.CX == BitmapFrame.Width / 2 && p.CY == BitmapFrame.Height / 2
                        && p.W  == BitmapFrame.Width      && p.H  == BitmapFrame.Height;
        _canvas.Invalidate();
    }

    private void OnFullCanvasChecked(object? sender, EventArgs e)
    {
        if (_suppressNud || _sel < 0 || !_chkFull.Checked) return;
        _suppressNud = true;
        var p = _placements[_sel];
        p.CX = BitmapFrame.Width  / 2;
        p.CY = BitmapFrame.Height / 2;
        p.W  = BitmapFrame.Width;
        p.H  = BitmapFrame.Height;
        _nudCX.Value = p.CX; _nudCY.Value = p.CY;
        _nudW .Value = p.W;  _nudH .Value = p.H;
        _previews[_sel].Bounds = p.ToRectangle();
        _suppressNud = false;
        _canvas.Invalidate();
    }

    // ── Properties panel ─────────────────────────────────────────────────────

    private void RefreshPropsPanel()
    {
        foreach (Control c in _propsPanel.Controls) c.Dispose();
        _propsPanel.Controls.Clear();
        _propsY = 0;

        if (_sel < 0 || _sel >= _placements.Count) return;
        var p = _placements[_sel];

        switch (p.Type)
        {
            case "label":      BuildLabelProps(p);      break;
            case "profilebar": BuildProfileBarProps(p); break;
            case "clock":      BuildClockProps(p);      break;
            case "battery":    BuildBatteryProps(p);    break;
            case "connection": BuildConnectionProps(p); break;
        }

        // Expand scrollable area height to content
        _propsPanel.AutoScrollMinSize = new Size(0, _propsY + 4);
    }

    // ── Label properties ──────────────────────────────────────────────────────

    private void BuildLabelProps(WidgetPlacement p)
    {
        var cfg = p.GetConfig<LabelConfig>();

        // Template row
        AddLabel("Template:");
        var txtTemplate = new TextBox
        {
            Text      = cfg.Template,
            Location  = new Point(0, _propsY),
            Size      = new Size(284, 23),
        };
        txtTemplate.TextChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.Template = txtTemplate.Text; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) { w.Config = c; w.Start(); }
            RefreshListEntry();
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(txtTemplate);
        _propsY += 27;

        // Binding quick-insert buttons — row 1
        AddBindingButtons(p, txtTemplate,
        [
            ("{battery.icon}",    "bat icon"),
            ("{battery.percent}", "bat %"),
            ("{conn.icon}",       "conn icon"),
            ("{conn.type}",       "type"),
            ("{conn.profile}",    "profile#"),
        ]);
        // Row 2
        AddBindingButtons(p, txtTemplate,
        [
            ("{time}",   "time"),
            ("{time24}", "time24"),
            ("{ampm}",   "AM/PM"),
            ("{date}",   "date"),
            ("{date.month}", "month"),
            ("{date.day}",   "day"),
        ]);

        // Glyph picker button
        var btnGlyph = new Button
        {
            Text     = "Insert Glyph…",
            Location = new Point(0, _propsY),
            Size     = new Size(120, 24),
        };
        btnGlyph.Click += (_, _) =>
        {
            using var dlg = new GlyphPickerDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedGlyph is { } g)
            {
                int pos = txtTemplate.SelectionStart;
                txtTemplate.Text = txtTemplate.Text.Insert(pos, g);
                txtTemplate.SelectionStart = pos + g.Length;
            }
        };
        _propsPanel.Controls.Add(btnGlyph);
        _propsY += 30;

        // Font size
        AddLabel("Size (px):");
        var nudSize = new NumericUpDown
        {
            Location  = new Point(68, _propsY),
            Size      = new Size(60, 23),
            Minimum   = 6,
            Maximum   = 60,
            Value     = (decimal)Math.Clamp(cfg.Size, 6, 60),
        };
        nudSize.ValueChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.Size = (float)nudSize.Value; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) w.Config = c;
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(nudSize);
        _propsY += 28;

        // Bold + NerdFont checkboxes on same row
        var chkBold = new CheckBox { Text = "Bold", Checked = cfg.Bold, Location = new Point(0, _propsY), Size = new Size(70, 22) };
        chkBold.CheckedChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.Bold = chkBold.Checked; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) w.Config = c;
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(chkBold);

        var chkNerd = new CheckBox { Text = "Nerd Font", Checked = cfg.UseNerdFont, Location = new Point(78, _propsY), Size = new Size(100, 22) };
        chkNerd.CheckedChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.UseNerdFont = chkNerd.Checked; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) w.Config = c;
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(chkNerd);
        _propsY += 26;

        // 24h checkbox
        var chk24 = new CheckBox { Text = "Force 24h", Checked = cfg.Use24h, Location = new Point(0, _propsY), Size = new Size(130, 22) };
        chk24.CheckedChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.Use24h = chk24.Checked; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) w.Config = c;
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(chk24);
        _propsY += 26;

        // Alignment
        AddLabel("Align:");
        var cmbAlign = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(46, _propsY),
            Size          = new Size(90, 23),
        };
        cmbAlign.Items.AddRange(["center", "left", "right"]);
        cmbAlign.SelectedItem = cfg.Align is "left" or "right" ? cfg.Align : "center";
        cmbAlign.SelectedIndexChanged += (_, _) =>
        {
            var c = p.GetConfig<LabelConfig>(); c.Align = (string)cmbAlign.SelectedItem!; p.SetConfig(c);
            if (_previews[_sel] is LabelWidget w) w.Config = c;
            _canvas.Invalidate();
        };
        _propsPanel.Controls.Add(cmbAlign);
        _propsY += 30;

        // ── Glyph styles ──────────────────────────────────────────────────────
        AddLabel("─ Glyph styles ─");

        // Numeric style (digits in {time} / {date} / {conn.profile})
        AddPropComboRow("Digit style:", NerdFont.NumericStyleLabel,
            ["text", "box", "box_outline", "box_multiple", "box_multiple_outline", "plain", "circle", "circle_outline"],
            cfg.NumericStyle, v =>
            {
                var c = p.GetConfig<LabelConfig>(); c.NumericStyle = v; p.SetConfig(c);
                if (_previews[_sel] is LabelWidget w) w.Config = c;
                _canvas.Invalidate();
            });

        // Alpha style (letters in {date} / {date.month})
        AddPropComboRow("Letter style:", NerdFont.AlphaStyleLabel,
            ["text", "plain", "box", "box_outline", "circle", "circle_outline"],
            cfg.AlphaStyle, v =>
            {
                var c = p.GetConfig<LabelConfig>(); c.AlphaStyle = v; p.SetConfig(c);
                if (_previews[_sel] is LabelWidget w) w.Config = c;
                _canvas.Invalidate();
            });

        // Battery icon style
        AddPropComboRow("Battery icon:", s => s switch
            {
                "md_level"     => "MD level (10-90%)",
                "md_threshold" => "MD threshold",
                _              => "NF classic",
            },
            ["nf", "md_level", "md_threshold"],
            cfg.BatteryGlyphStyle, v =>
            {
                var c = p.GetConfig<LabelConfig>(); c.BatteryGlyphStyle = v; p.SetConfig(c);
                if (_previews[_sel] is LabelWidget w) w.Config = c;
                _canvas.Invalidate();
            });

        // BLE / USB paired glyph pickers for {conn.icon}
        AddLabel("Conn icon glyphs ({conn.icon}):");
        AddConnGlyphRow(p, "BLE:", cfg.ConnBleGlyph, NerdFont.Bluetooth,
            v => { var c = p.GetConfig<LabelConfig>(); c.ConnBleGlyph = v; p.SetConfig(c);
                   if (_previews[_sel] is LabelWidget w) w.Config = c; _canvas.Invalidate(); });
        AddConnGlyphRow(p, "USB:", cfg.ConnUsbGlyph, NerdFont.Usb,
            v => { var c = p.GetConfig<LabelConfig>(); c.ConnUsbGlyph = v; p.SetConfig(c);
                   if (_previews[_sel] is LabelWidget w) w.Config = c; _canvas.Invalidate(); });
    }

    // Row with a label + dropdown mapped through a label-lookup function.
    private void AddPropComboRow(string label, Func<string, string> labelFn,
        string[] keys, string selected, Action<string> onChange)
    {
        _propsPanel.Controls.Add(new Label { Text = label, Location = new Point(0, _propsY + 3), Size = new Size(78, 18) });
        var cmb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(80, _propsY),
            Size          = new Size(200, 23),
        };
        foreach (var k in keys) cmb.Items.Add(labelFn(k));
        int idx = Array.IndexOf(keys, selected);
        cmb.SelectedIndex = idx >= 0 ? idx : 0;
        cmb.SelectedIndexChanged += (_, _) =>
        {
            int i = cmb.SelectedIndex;
            if (i >= 0 && i < keys.Length) onChange(keys[i]);
        };
        _propsPanel.Controls.Add(cmb);
        _propsY += 28;
    }

    // Row with a label + button that shows the current glyph and opens GlyphPickerDialog on click.
    private void AddConnGlyphRow(WidgetPlacement p, string label, string current, string fallback, Action<string> onChange)
    {
        string displayGlyph = current is { Length: > 0 } ? current : fallback;

        _propsPanel.Controls.Add(new Label { Text = label, Location = new Point(0, _propsY + 3), Size = new Size(34, 18) });
        var btn = new Button
        {
            Text      = displayGlyph,
            Font      = NerdFont.CreateFont(14f),
            Location  = new Point(36, _propsY),
            Size      = new Size(36, 26),
            FlatStyle = FlatStyle.Flat,
        };
        btn.Click += (_, _) =>
        {
            using var dlg = new GlyphPickerDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedGlyph is { } g)
            {
                btn.Text = g;
                onChange(g);
            }
        };
        _propsPanel.Controls.Add(btn);
        _propsY += 30;
    }

    private void AddBindingButtons(WidgetPlacement p, TextBox target, (string Token, string Label)[] items)
    {
        int x = 0;
        foreach (var (token, label) in items)
        {
            int w = Math.Max(40, label.Length * 7 + 8);
            if (x + w > 284) { x = 0; _propsY += 24; }
            var btn = new Button
            {
                Text      = label,
                Tag       = token,
                Location  = new Point(x, _propsY),
                Size      = new Size(w, 22),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 7.5f),
            };
            btn.Click += (_, _) =>
            {
                int pos = target.SelectionStart;
                target.Text = target.Text.Insert(pos, token);
                target.SelectionStart = pos + token.Length;
                target.Focus();
            };
            _propsPanel.Controls.Add(btn);
            x += w + 2;
        }
        _propsY += 26;
    }

    // ── ProfileBar properties ─────────────────────────────────────────────────

    private void BuildProfileBarProps(WidgetPlacement p)
    {
        var cfg = p.GetConfig<ProfileBarConfig>();

        // Active profile glyph style
        AddPropComboRow("Active style:", NerdFont.NumericStyleLabel,
            ["gdi", "box", "box_outline", "box_multiple", "box_multiple_outline", "plain", "circle", "circle_outline"],
            cfg.ActiveStyle, v =>
            {
                var c = p.GetConfig<ProfileBarConfig>(); c.ActiveStyle = v; p.SetConfig(c);
                if (_previews[_sel] is ProfileBarWidget w) w.Config = c;
                _canvas.Invalidate();
            });

        // Inactive profile glyph style
        AddPropComboRow("Inactive style:", NerdFont.NumericStyleLabel,
            ["gdi", "box", "box_outline", "box_multiple", "box_multiple_outline", "plain", "circle", "circle_outline"],
            cfg.InactiveStyle, v =>
            {
                var c = p.GetConfig<ProfileBarConfig>(); c.InactiveStyle = v; p.SetConfig(c);
                if (_previews[_sel] is ProfileBarWidget w) w.Config = c;
                _canvas.Invalidate();
            });

        AddPropScale(cfg.Scale, v =>
        {
            var c = p.GetConfig<ProfileBarConfig>(); c.Scale = v; p.SetConfig(c);
            if (_previews[_sel] is ProfileBarWidget w) w.Config = c;
            _canvas.Invalidate();
        });
    }

    // ── Legacy widget properties ──────────────────────────────────────────────

    private void BuildClockProps(WidgetPlacement p)
    {
        var cfg = p.GetConfig<ClockConfig>();
        AddPropBool("24-hour format", cfg.Use24h, v =>
        {
            var c = p.GetConfig<ClockConfig>(); c.Use24h = v; p.SetConfig(c);
            if (_previews[_sel] is ClockWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropBool("Show AM/PM", cfg.ShowAmPm, v =>
        {
            var c = p.GetConfig<ClockConfig>(); c.ShowAmPm = v; p.SetConfig(c);
            if (_previews[_sel] is ClockWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropBool("Show date", cfg.ShowDate, v =>
        {
            var c = p.GetConfig<ClockConfig>(); c.ShowDate = v; p.SetConfig(c);
            if (_previews[_sel] is ClockWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropScale(cfg.Scale, v =>
        {
            var c = p.GetConfig<ClockConfig>(); c.Scale = v; p.SetConfig(c);
            if (_previews[_sel] is ClockWidget w) w.Config = c;
            _canvas.Invalidate();
        });
    }

    private void BuildBatteryProps(WidgetPlacement p)
    {
        var cfg = p.GetConfig<BatteryConfig>();
        AddPropBool("Show icon", cfg.ShowIcon, v =>
        {
            var c = p.GetConfig<BatteryConfig>(); c.ShowIcon = v; p.SetConfig(c);
            if (_previews[_sel] is BatteryWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropBool("Show percentage", cfg.ShowPercent, v =>
        {
            var c = p.GetConfig<BatteryConfig>(); c.ShowPercent = v; p.SetConfig(c);
            if (_previews[_sel] is BatteryWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropScale(cfg.Scale, v =>
        {
            var c = p.GetConfig<BatteryConfig>(); c.Scale = v; p.SetConfig(c);
            if (_previews[_sel] is BatteryWidget w) w.Config = c;
            _canvas.Invalidate();
        });
    }

    private void BuildConnectionProps(WidgetPlacement p)
    {
        var cfg = p.GetConfig<ConnectionConfig>();
        AddPropBool("Show icon", cfg.ShowIcon, v =>
        {
            var c = p.GetConfig<ConnectionConfig>(); c.ShowIcon = v; p.SetConfig(c);
            if (_previews[_sel] is ConnectionWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropBool("Show type (USB / BLE)", cfg.ShowType, v =>
        {
            var c = p.GetConfig<ConnectionConfig>(); c.ShowType = v; p.SetConfig(c);
            if (_previews[_sel] is ConnectionWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropBool("Show BLE profile number", cfg.ShowProfile, v =>
        {
            var c = p.GetConfig<ConnectionConfig>(); c.ShowProfile = v; p.SetConfig(c);
            if (_previews[_sel] is ConnectionWidget w) w.Config = c;
            _canvas.Invalidate();
        });
        AddPropScale(cfg.Scale, v =>
        {
            var c = p.GetConfig<ConnectionConfig>(); c.Scale = v; p.SetConfig(c);
            if (_previews[_sel] is ConnectionWidget w) w.Config = c;
            _canvas.Invalidate();
        });
    }

    // ── Property helpers ──────────────────────────────────────────────────────

    private void AddLabel(string text)
    {
        _propsPanel.Controls.Add(new Label
        {
            Text     = text,
            Location = new Point(0, _propsY),
            Size     = new Size(280, 16),
            AutoSize = false,
        });
        _propsY += 18;
    }

    private void AddPropBool(string label, bool value, Action<bool> onChange)
    {
        var chk = new CheckBox
        {
            Text     = label,
            Checked  = value,
            Location = new Point(0, _propsY),
            Size     = new Size(284, 22),
            AutoSize = false,
        };
        chk.CheckedChanged += (_, _) => onChange(chk.Checked);
        _propsPanel.Controls.Add(chk);
        _propsY += 26;
    }

    private void AddPropScale(float value, Action<float> onChange)
    {
        _propsPanel.Controls.Add(new Label { Text = "Size:", Location = new Point(0, _propsY + 2), Size = new Size(40, 20) });
        var nud = new NumericUpDown
        {
            Location  = new Point(42, _propsY),
            Size      = new Size(60, 23),
            Minimum   = 40, Maximum = 200, Increment = 10,
            Value     = (decimal)Math.Clamp(value * 100, 40, 200),
        };
        _propsPanel.Controls.Add(new Label { Text = "%", Location = new Point(106, _propsY + 2), Size = new Size(20, 20) });
        nud.ValueChanged += (_, _) => onChange((float)nud.Value / 100f);
        _propsPanel.Controls.Add(nud);
        _propsY += 30;
    }

    private void RefreshListEntry()
    {
        if (_sel < 0 || _sel >= _placements.Count) return;
        _listWidgets.SelectedIndexChanged -= OnListSelectionChanged;
        _listWidgets.Items[_sel] = WidgetListLabel(_placements[_sel]);
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var w in _previews) w.Dispose();
        base.Dispose(disposing);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Label MakeLbl(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), Size = new Size(26, 20), AutoSize = false };

    private static NumericUpDown MakeNud(int x, int y, int min, int max) =>
        new() { Location = new Point(x, y), Size = new Size(58, 23), Minimum = min, Maximum = max };
}
