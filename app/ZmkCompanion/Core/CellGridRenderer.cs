using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Renders text into per-cell 1bpp bitmaps for the cell-grid protocol.
// All glyph rendering stays in the app (GDI+ with the embedded FiraCode NF),
// so firmware needs no font assets and any NF codepoint is usable.
static class CellGridRenderer
{
    // Renders each element (char or surrogate pair) of `text` into its own
    // cell bitmap for the given tier. Returns one packed 1bpp array per
    // element, each exactly tier.Bytes long.
    public static byte[][] RenderText(CellTier tier, string text)
    {
        var elements = SplitElements(text);
        var cells = new byte[elements.Length][];
        for (int i = 0; i < elements.Length; i++)
            cells[i] = RenderCell(tier, elements[i]);
        return cells;
    }

    // Renders a single text element centered in a tier-sized cell.
    // antiAlias=true: AntiAliasGridFit → gray pixels thresholded at 100 (softer, better outlines).
    // antiAlias=false: SingleBitPerPixelGridFit → pure 1bpp (hinted, crisp).
    public static byte[] RenderCell(CellTier tier, string element,
                                    FontStyle style     = FontStyle.Regular,
                                    bool      antiAlias = false)
    {
        using var bmp = new Bitmap(tier.W, tier.H);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = antiAlias
            ? TextRenderingHint.AntiAliasGridFit
            : TextRenderingHint.SingleBitPerPixelGridFit;

        // For rectangular tiers (H > W): size by H×0.78 — the scale-down check
        // below then caps width-overflow, naturally filling the cell width.
        // For square tiers (W == H): H×0.78 leaves 22% unused; use W×0.92
        // instead so icons fill the square rather than leaving an empty border.
        var sf       = StringFormat.GenericTypographic;
        float sizePx = (tier.W == tier.H) ? tier.W * 0.92f : tier.H * 0.78f;

        using var probe = NerdFont.CreateFont(sizePx, style);
        RectangleF ink = MeasureInk(g, element, probe, sf);
        if (ink.Width > tier.W) sizePx *= tier.W / ink.Width;

        using var font = NerdFont.CreateFont(sizePx, style);
        ink = MeasureInk(g, element, font, sf);
        // Center the glyph's actual rendered ink box, not its typographic
        // advance box — several Nerd Font icon glyphs draw wider than (and
        // offset from) what MeasureString reports, e.g. the soccer ball and
        // umbrella glyphs overlap their neighbor even in a plain WinForms
        // TextBox at the same font/size, confirming this is a font-metrics
        // mismatch, not a centering-math bug.
        g.DrawString(element, font, Brushes.White,
            (tier.W - ink.Width) / 2f - ink.X, (tier.H - ink.Height) / 2f - ink.Y, sf);

        return Pack1bpp(bmp, antiAlias);
    }

    // Renders element into a W×(H*2) square then crops the top or bottom H rows.
    // Use with the icon_half tier (22×11): two stacked rows display a full 22×22 glyph.
    public static byte[] RenderCellSplit(CellTier tier, string element, SplitHalf half,
                                         FontStyle style     = FontStyle.Regular,
                                         bool      antiAlias = false)
    {
        int fullH = tier.H * 2;
        using var bmp = new Bitmap(tier.W, fullH);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = antiAlias
            ? TextRenderingHint.AntiAliasGridFit
            : TextRenderingHint.SingleBitPerPixelGridFit;

        var sf       = StringFormat.GenericTypographic;
        float sizePx = tier.W * 0.92f; // square canvas: fill W×W

        using var probe = NerdFont.CreateFont(sizePx, style);
        RectangleF ink = MeasureInk(g, element, probe, sf);
        if (ink.Width > tier.W) sizePx *= tier.W / ink.Width;

        using var font = NerdFont.CreateFont(sizePx, style);
        ink = MeasureInk(g, element, font, sf);
        g.DrawString(element, font, Brushes.White,
            (tier.W - ink.Width) / 2f - ink.X, (fullH - ink.Height) / 2f - ink.Y, sf);

        int startY = half == SplitHalf.Top ? 0 : tier.H;
        return PackCrop1bpp(bmp, startY, tier.H, antiAlias);
    }

    // Measures the actual rendered ("ink") bounding box of `text`, not just
    // its typographic advance width. MeasureString's advance-width figure
    // under-reports how wide several Nerd Font icon glyphs actually draw
    // (by font design, these are meant to slightly overflow a single
    // character cell), so using it alone both fails the "is this too wide"
    // scale-down check and miscenters the glyph within the cell.
    private static RectangleF MeasureInk(Graphics g, string text, Font font, StringFormat baseFormat)
    {
        using var measureFormat = (StringFormat)baseFormat.Clone();
        measureFormat.SetMeasurableCharacterRanges([new CharacterRange(0, text.Length)]);
        var layoutRect = new RectangleF(0, 0, 2000, 2000); // generous — must not clip the glyph
        var regions = g.MeasureCharacterRanges(text, font, layoutRect, measureFormat);
        var bounds = regions[0].GetBounds(g);
        regions[0].Dispose();
        return bounds;
    }

    // Packs a bitmap to 1bpp: row-major, MSB-first, rows padded to a byte
    // boundary (per the protocol spec; must match firmware's decoder).
    // antiAlias=true uses a lower threshold (100) to retain more of the anti-aliased
    // stroke area, producing slightly thicker and smoother-looking outlines.
    public static byte[] Pack1bpp(Bitmap bmp, bool antiAlias = false)
    {
        int threshold = antiAlias ? 100 : 127;
        int rowBytes = (bmp.Width + 7) / 8;
        var packed = new byte[rowBytes * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).R > threshold)
                    packed[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
        return packed;
    }

    // Packs [startY, startY+height) rows of bmp to 1bpp.
    private static byte[] PackCrop1bpp(Bitmap bmp, int startY, int height, bool antiAlias = false)
    {
        int threshold = antiAlias ? 100 : 127;
        int rowBytes = (bmp.Width + 7) / 8;
        var packed = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, startY + y).R > threshold)
                    packed[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
        return packed;
    }

    private static string[] SplitElements(string s)
    {
        var list = new List<string>(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
            { list.Add(s.Substring(i, 2)); i += 2; }
            else
            { list.Add(s.Substring(i, 1)); i++; }
        }
        return list.ToArray();
    }
}
