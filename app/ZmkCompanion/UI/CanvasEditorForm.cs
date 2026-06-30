using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features.Widgets;

namespace ZmkCompanion.UI;

// Visual canvas editor: shows the 68×160 display at 3× scale.
// The user can add/remove/drag/resize widgets and hit "Apply & Send"
// to push the layout to the live compositor and keyboard.
sealed class CanvasEditorForm : Form
{
    private const int Zoom     = 3;
    private const int CanvasW  = BitmapFrame.Width  * Zoom;  // 204
    private const int CanvasH  = BitmapFrame.Height * Zoom;  // 480

    private readonly AppSettings                   _settings;
    private readonly Action<List<WidgetPlacement>> _onApply;

    // Working copies of the canvas layout; index-aligned with _previews.
    private readonly List<WidgetPlacement> _placements = [];
    private readonly List<IWidget>         _previews   = [];

    private int   _sel          = -1;
    private bool  _dragging;
    private Point _dragOriginPx;   // unscaled canvas point where drag started
    private Point _dragInitPos;    // placement (X,Y) at drag start

    // Controls
    private readonly Panel         _canvas;
    private readonly ListBox       _listWidgets;
    private readonly NumericUpDown _nudX, _nudY, _nudW, _nudH;
    private readonly CheckBox      _chkFull;
    private          bool          _suppressNud;

    // ── Construction ─────────────────────────────────────────────────────────

    public CanvasEditorForm(AppSettings settings, Action<List<WidgetPlacement>> onApply)
    {
        _settings = settings;
        _onApply  = onApply;

        Text            = "ZMK Companion — Canvas Editor";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        ClientSize      = new Size(492, 540);

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

        int rx = CanvasW + 20;   // right column x

        // ── Widgets group ────────────────────────────────────────────────────
        var grpWidgets = new GroupBox { Text = "Widgets", Location = new Point(rx, 8),   Size = new Size(258, 218) };

        _listWidgets = new ListBox { Location = new Point(8, 20), Size = new Size(242, 138) };
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
        grpWidgets.Controls.Add(_listWidgets);

        var btnAdd = new Button { Text = "Add ▾", Location = new Point(8, 166), Size = new Size(115, 26) };
        btnAdd.Click += OnAddClick;
        grpWidgets.Controls.Add(btnAdd);

        var btnRemove = new Button { Text = "Remove", Location = new Point(131, 166), Size = new Size(111, 26) };
        btnRemove.Click += OnRemoveClick;
        grpWidgets.Controls.Add(btnRemove);

        Controls.Add(grpWidgets);

        // ── Position & size group ────────────────────────────────────────────
        var grpBounds = new GroupBox { Text = "Position && Size", Location = new Point(rx, 234), Size = new Size(258, 152) };

        grpBounds.Controls.Add(MakeLbl("X:", 8,  24)); _nudX = MakeNud(28,  20, 0, BitmapFrame.Width  - 1);
        grpBounds.Controls.Add(MakeLbl("Y:", 98, 24)); _nudY = MakeNud(118, 20, 0, BitmapFrame.Height - 1);
        grpBounds.Controls.Add(MakeLbl("W:", 8,  58)); _nudW = MakeNud(28,  54, 1, BitmapFrame.Width);
        grpBounds.Controls.Add(MakeLbl("H:", 98, 58)); _nudH = MakeNud(118, 54, 1, BitmapFrame.Height);

        _nudX.ValueChanged += (_, _) => ApplyNudToSelected();
        _nudY.ValueChanged += (_, _) => ApplyNudToSelected();
        _nudW.ValueChanged += (_, _) => ApplyNudToSelected();
        _nudH.ValueChanged += (_, _) => ApplyNudToSelected();

        grpBounds.Controls.AddRange([_nudX, _nudY, _nudW, _nudH]);

        _chkFull = new CheckBox { Text = "Full canvas (68×160)", Location = new Point(8, 94), Size = new Size(200, 23) };
        _chkFull.CheckedChanged += OnFullCanvasChecked;
        grpBounds.Controls.Add(_chkFull);

        Controls.Add(grpBounds);

        // ── Bottom buttons ───────────────────────────────────────────────────
        var btnApply = new Button { Text = "Apply && Send", Location = new Point(rx, 496), Size = new Size(120, 32) };
        btnApply.Click += (_, _) => Apply();
        Controls.Add(btnApply);

        var btnClose = new Button { Text = "Close", Location = new Point(rx + 128, 496), Size = new Size(110, 32) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        // ── Load & start refresh ─────────────────────────────────────────────
        LoadFromSettings();
        RefreshWidgetList();
        RefreshBoundsPanel();

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

    private static IWidget MakePreview(WidgetPlacement p) => p.Type switch
    {
        _ => new ClockWidget { Bounds = p.ToRectangle() },
    };

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

        // Render widgets into 68×160 bitmap
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

        // Scale up (nearest-neighbor = pixel-perfect)
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode   = PixelOffsetMode.Half;
        g.DrawImage(bmp, 0, 0, CanvasW, CanvasH);

        // Widget overlays
        for (int i = 0; i < _placements.Count; i++)
        {
            bool sel = i == _sel;
            var  b   = ZoomRect(_placements[i].ToRectangle());
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
        for (int i = _placements.Count - 1; i >= 0; i--)
        {
            if (!_placements[i].ToRectangle().Contains(pos)) continue;
            _sel          = i;
            _dragging     = true;
            _dragOriginPx = pos;
            _dragInitPos  = new Point(_placements[i].X, _placements[i].Y);
            break;
        }
        _listWidgets.SelectedIndexChanged -= OnListSelectionChanged;
        if (_sel >= 0 && _sel < _listWidgets.Items.Count) _listWidgets.SelectedIndex = _sel;
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
        RefreshBoundsPanel();
        _canvas.Invalidate();
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || _sel < 0) return;
        var pos   = new Point(e.X / Zoom, e.Y / Zoom);
        var delta = new Point(pos.X - _dragOriginPx.X, pos.Y - _dragOriginPx.Y);
        var p     = _placements[_sel];
        p.X = Math.Clamp(_dragInitPos.X + delta.X, 0, BitmapFrame.Width  - p.W);
        p.Y = Math.Clamp(_dragInitPos.Y + delta.Y, 0, BitmapFrame.Height - p.H);
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
        foreach (var p in _placements)
            _listWidgets.Items.Add(WidgetLabel(p.Type));
        if (_sel >= 0 && _sel < _listWidgets.Items.Count)
            _listWidgets.SelectedIndex = _sel;
        _listWidgets.EndUpdate();
        _listWidgets.SelectedIndexChanged += OnListSelectionChanged;
    }

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        _sel = _listWidgets.SelectedIndex;
        RefreshBoundsPanel();
        _canvas.Invalidate();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Clock",    null, (_, _) => AddWidget("clock")));
        menu.Items.Add(new ToolStripMenuItem("Weather",  null, (_, _) => AddWidget("weather"))  { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Pomodoro", null, (_, _) => AddWidget("pomodoro")) { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Text",     null, (_, _) => AddWidget("text"))     { Enabled = false });
        menu.Show((Button)sender!, new Point(0, ((Button)sender!).Height));
    }

    private void AddWidget(string type)
    {
        var p = new WidgetPlacement { Type = type };
        _placements.Add(p);
        _previews.Add(MakePreview(p));
        _sel = _placements.Count - 1;
        RefreshWidgetList();
        RefreshBoundsPanel();
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
        _canvas.Invalidate();
    }

    private static string WidgetLabel(string type) => type switch
    {
        "clock"    => "Clock",
        "weather"  => "Weather",
        "pomodoro" => "Pomodoro",
        "text"     => "Text",
        _          => type,
    };

    // ── Bounds panel ──────────────────────────────────────────────────────────

    private void RefreshBoundsPanel()
    {
        bool active = _sel >= 0 && _sel < _placements.Count;
        _nudX.Enabled = _nudY.Enabled = _nudW.Enabled = _nudH.Enabled = _chkFull.Enabled = active;
        if (!active) return;

        var p = _placements[_sel];
        _suppressNud = true;
        _nudX.Value  = p.X;
        _nudY.Value  = p.Y;
        _nudW.Value  = p.W;
        _nudH.Value  = p.H;
        _chkFull.Checked = p.X == 0 && p.Y == 0 && p.W == BitmapFrame.Width && p.H == BitmapFrame.Height;
        _suppressNud = false;
    }

    private void ApplyNudToSelected()
    {
        if (_suppressNud || _sel < 0 || _sel >= _placements.Count) return;
        var p = _placements[_sel];
        p.X = (int)_nudX.Value;
        p.Y = (int)_nudY.Value;
        p.W = (int)_nudW.Value;
        p.H = (int)_nudH.Value;
        _previews[_sel].Bounds = p.ToRectangle();
        _chkFull.Checked = p.X == 0 && p.Y == 0 && p.W == BitmapFrame.Width && p.H == BitmapFrame.Height;
        _canvas.Invalidate();
    }

    private void OnFullCanvasChecked(object? sender, EventArgs e)
    {
        if (_suppressNud || _sel < 0 || !_chkFull.Checked) return;
        _suppressNud = true;
        var p = _placements[_sel];
        p.X = p.Y = 0;
        p.W = BitmapFrame.Width;
        p.H = BitmapFrame.Height;
        _nudX.Value = 0; _nudY.Value = 0;
        _nudW.Value = BitmapFrame.Width; _nudH.Value = BitmapFrame.Height;
        _previews[_sel].Bounds = p.ToRectangle();
        _suppressNud = false;
        _canvas.Invalidate();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var w in _previews) w.Dispose();
        base.Dispose(disposing);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Label MakeLbl(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), Size = new Size(20, 20), AutoSize = false };

    private static NumericUpDown MakeNud(int x, int y, int min, int max) =>
        new() { Location = new Point(x, y), Size = new Size(58, 23), Minimum = min, Maximum = max };
}
