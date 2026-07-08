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
        var ink = MeasureInkPixels(element, probe, antiAlias);
        if (ink.Width > tier.W) sizePx *= (float)tier.W / ink.Width;

        using var font = NerdFont.CreateFont(sizePx, style);
        ink = MeasureInkPixels(element, font, antiAlias);
        // Center the glyph's actually-painted pixels, not any font-reported
        // metric — MeasureCharacterRanges' region still didn't match for some
        // icon glyphs (soccer ball, umbrella stayed clipped after that first
        // attempt), so this measures real pixels the same way Pack1bpp itself
        // reads them, guaranteed consistent with what actually gets sent.
        g.DrawString(element, font, Brushes.White,
            (tier.W - ink.Width) / 2f - ink.BearingX, (tier.H - ink.Height) / 2f - ink.BearingY, sf);

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
        var ink = MeasureInkPixels(element, probe, antiAlias);
        if (ink.Width > tier.W) sizePx *= (float)tier.W / ink.Width;

        using var font = NerdFont.CreateFont(sizePx, style);
        ink = MeasureInkPixels(element, font, antiAlias);
        g.DrawString(element, font, Brushes.White,
            (tier.W - ink.Width) / 2f - ink.BearingX, (fullH - ink.Height) / 2f - ink.BearingY, sf);

        int startY = half == SplitHalf.Top ? 0 : tier.H;
        return PackCrop1bpp(bmp, startY, tier.H, antiAlias);
    }

    private readonly record struct InkBounds(int Width, int Height, float BearingX, float BearingY);

    // Renders `element` to an oversized scratch canvas and scans for the
    // actual lit pixels — no GDI+ font metric API (MeasureString's advance
    // width, MeasureCharacterRanges' region) matched what several Nerd Font
    // icon glyphs (soccer ball, umbrella) actually paint, so this measures
    // real pixels the same way Pack1bpp does, guaranteed self-consistent.
    // BearingX/Y is the offset from the (pad,pad) draw origin used here to
    // where the ink actually starts — GDI+ text rendering is translation
    // invariant, so that offset holds for any origin the caller draws at.
    private static InkBounds MeasureInkPixels(string element, Font font, bool antiAlias)
    {
        int pad  = (int)(font.Size * 2) + 2;
        int size = pad * 2 + (int)(font.Size * 2) + 4;
        using var bmp = new Bitmap(size, size);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = antiAlias
            ? TextRenderingHint.AntiAliasGridFit
            : TextRenderingHint.SingleBitPerPixelGridFit;
        g.DrawString(element, font, Brushes.White, pad, pad, StringFormat.GenericTypographic);

        int threshold = antiAlias ? 100 : 127;
        int minX = size, minY = size, maxX = -1, maxY = -1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (bmp.GetPixel(x, y).R > threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) return new InkBounds(0, 0, 0, 0); // blank element (e.g. a space)
        return new InkBounds(maxX - minX + 1, maxY - minY + 1, minX - pad, minY - pad);
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
