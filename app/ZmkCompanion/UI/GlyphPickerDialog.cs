using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Full glyph picker: enumerates every codepoint in the embedded Nerd Font via
// its cmap table, groups them into NF categories, and lets the user filter by
// category or hex search.  Click a cell to select; Escape to cancel.
sealed class GlyphPickerDialog : Form
{
    public string? SelectedGlyph { get; private set; }

    // NF range categories (predicates are inclusive, can overlap for "All")
    private static readonly (string Name, Func<int, bool> Match)[] Categories =
    [
        ("All icons",       cp => cp >= 0xE000),
        ("Material Design", cp => cp is >= 0xF0001 and <= 0xF1AF0),
        ("Font Awesome",    cp => cp is >= 0xF000  and <= 0xF2FF),
        ("Octicons",        cp => cp is >= 0xF400  and <= 0xF67F),
        ("Devicons",        cp => cp is >= 0xE700  and <= 0xE8EF),
        ("Powerline",       cp => cp is >= 0xE0A0  and <= 0xE0FF),
        ("BMP other",       cp => cp is >= 0xE000  and <= 0xEFFF
                                  && !(cp is >= 0xE0A0 and <= 0xE0FF)
                                  && !(cp is >= 0xE700 and <= 0xE8EF)),
    ];

    private readonly GlyphGrid _grid;
    private readonly Label     _status;
    private int[]              _allNfCps;   // codepoints >= U+E000 from the font

    public GlyphPickerDialog()
    {
        Text            = "Glyph Picker — click to select";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MinimumSize     = new Size(420, 360);
        Size            = new Size(460, 520);
        MaximizeBox     = false;
        BackColor       = Color.FromArgb(28, 28, 28);
        ForeColor       = Color.White;
        KeyPreview      = true;

        // Seed codepoints (all icon-range codepoints from the cmap)
        _allNfCps = FontCmapReader.GetAllCodepoints()
                        .Where(cp => cp >= 0xE000)
                        .ToArray();

        // ── Top toolbar ─────────────────────────────────────────────────────
        var toolbar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 34,
            BackColor = Color.FromArgb(40, 40, 40),
            Padding   = new Padding(4, 4, 4, 0),
        };

        var lblCat = new Label
        {
            Text      = "Category:",
            Location  = new Point(6, 8),
            Size      = new Size(64, 18),
            ForeColor = Color.White,
        };

        var cmbCat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(72, 4),
            Size          = new Size(160, 23),
        };
        foreach (var (name, _) in Categories) cmbCat.Items.Add(name);
        cmbCat.SelectedIndex = 0;

        var lblSrc = new Label
        {
            Text      = "Search:",
            Location  = new Point(240, 8),
            Size      = new Size(52, 18),
            ForeColor = Color.White,
        };

        var txtSearch = new TextBox
        {
            Location    = new Point(294, 4),
            Size        = new Size(120, 23),
            BackColor   = Color.FromArgb(50, 50, 50),
            ForeColor   = Color.White,
            PlaceholderText = "hex…",
        };

        toolbar.Controls.AddRange([lblCat, cmbCat, lblSrc, txtSearch]);
        Controls.Add(toolbar);

        // ── Status bar ───────────────────────────────────────────────────────
        _status = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 20,
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.Gray,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Padding   = new Padding(6, 0, 0, 0),
            Text      = $"{_allNfCps.Length} glyphs loaded",
        };
        Controls.Add(_status);

        // ── Glyph grid ───────────────────────────────────────────────────────
        _grid = new GlyphGrid { Dock = DockStyle.Fill };
        Controls.Add(_grid);

        // Wire events
        _grid.GlyphSelected += glyph =>
        {
            SelectedGlyph = glyph;
            DialogResult  = DialogResult.OK;
            Close();
        };

        _grid.HoveredCodepointChanged += cp =>
        {
            _status.Text = cp >= 0
                ? $"U+{cp:X5}  ({char.ConvertFromUtf32(cp)})"
                : $"{_grid.Count} glyphs";
            _status.ForeColor = cp >= 0 ? Color.Silver : Color.Gray;
        };

        void Refilter()
        {
            int catIdx = cmbCat.SelectedIndex;
            var catMatch = Categories[catIdx < 0 ? 0 : catIdx].Match;
            string q = txtSearch.Text.Trim().ToUpperInvariant();

            var filtered = _allNfCps
                .Where(cp => catMatch(cp))
                .Where(cp => q.Length == 0 || $"{cp:X5}".Contains(q))
                .ToArray();
            _grid.SetCodepoints(filtered);
            _status.Text      = $"{filtered.Length} glyphs";
            _status.ForeColor = Color.Gray;
        }

        cmbCat.SelectedIndexChanged += (_, _) => Refilter();
        txtSearch.TextChanged       += (_, _) => Refilter();

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        Refilter();
    }
}

// Owner-drawn scrollable grid of glyph cells.
sealed class GlyphGrid : Panel
{
    private const int CellSize = 36;
    private const int Cols     = 10;

    private int[] _cps     = [];
    private int   _hovered = -1;

    public event Action<string>? GlyphSelected;
    public event Action<int>?    HoveredCodepointChanged;  // -1 = none

    public int Count => _cps.Length;

    public GlyphGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint            |
                 ControlStyles.OptimizedDoubleBuffer, true);
        AutoScroll = true;
        BackColor  = Color.FromArgb(28, 28, 28);
    }

    public void SetCodepoints(int[] cps)
    {
        _cps    = cps;
        _hovered = -1;
        int rows = (cps.Length + Cols - 1) / Cols;
        AutoScrollMinSize = new Size(Cols * CellSize, rows * CellSize);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g   = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        int ap0 = AutoScrollPosition.Y;  // ≤ 0 when scrolled down

        int firstRow = Math.Max(0, (-ap0) / CellSize);
        int lastRow  = Math.Min((_cps.Length + Cols - 1) / Cols,
                                (-ap0 + ClientSize.Height) / CellSize + 1);

        using var nfFont = NerdFont.CreateFont(18f);
        using var sf     = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                int idx = row * Cols + col;
                if (idx >= _cps.Length) break;

                int cellX = col * CellSize + AutoScrollPosition.X;
                int cellY = row * CellSize + ap0;

                if (idx == _hovered)
                    g.FillRectangle(Brushes.DimGray, cellX, cellY, CellSize - 1, CellSize - 1);

                string glyph = char.ConvertFromUtf32(_cps[idx]);
                g.DrawString(glyph, nfFont, Brushes.White,
                    new RectangleF(cellX, cellY, CellSize - 1, CellSize - 1), sf);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitTest(e.X, e.Y);
        if (idx != _hovered)
        {
            _hovered = idx;
            Invalidate();
            HoveredCodepointChanged?.Invoke(idx >= 0 ? _cps[idx] : -1);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hovered >= 0) { _hovered = -1; Invalidate(); HoveredCodepointChanged?.Invoke(-1); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int idx = HitTest(e.X, e.Y);
            if (idx >= 0 && idx < _cps.Length)
                GlyphSelected?.Invoke(char.ConvertFromUtf32(_cps[idx]));
        }
        base.OnMouseClick(e);
    }

    private int HitTest(int mx, int my)
    {
        int vx  = mx - AutoScrollPosition.X;
        int vy  = my - AutoScrollPosition.Y;
        int col = vx / CellSize;
        int row = vy / CellSize;
        if (col < 0 || col >= Cols || row < 0) return -1;
        int idx = row * Cols + col;
        return idx < _cps.Length ? idx : -1;
    }
}
