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
    public static byte[] RenderCell(CellTier tier, string element)
    {
        using var bmp = new Bitmap(tier.W, tier.H);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // For rectangular tiers (H > W): size by H×0.78 — the scale-down check
        // below then caps width-overflow, naturally filling the cell width.
        // For square tiers (W == H): H×0.78 leaves 22% unused; use W×0.92
        // instead so icons fill the square rather than leaving an empty border.
        var sf       = StringFormat.GenericTypographic;
        float sizePx = (tier.W == tier.H) ? tier.W * 0.92f : tier.H * 0.78f;

        using var probe = NerdFont.CreateFont(sizePx);
        SizeF sz = g.MeasureString(element, probe, PointF.Empty, sf);
        if (sz.Width > tier.W) sizePx *= tier.W / sz.Width;

        using var font = NerdFont.CreateFont(sizePx);
        sz = g.MeasureString(element, font, PointF.Empty, sf);
        g.DrawString(element, font, Brushes.White,
            (tier.W - sz.Width) / 2f, (tier.H - sz.Height) / 2f, sf);

        return Pack1bpp(bmp);
    }

    // Renders element into a W×(H*2) square then crops the top or bottom H rows.
    // Use with the icon_half tier (22×11): two stacked rows display a full 22×22 glyph.
    public static byte[] RenderCellSplit(CellTier tier, string element, SplitHalf half)
    {
        int fullH = tier.H * 2;
        using var bmp = new Bitmap(tier.W, fullH);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var sf       = StringFormat.GenericTypographic;
        float sizePx = tier.W * 0.92f; // square canvas: fill W×W

        using var probe = NerdFont.CreateFont(sizePx);
        SizeF sz = g.MeasureString(element, probe, PointF.Empty, sf);
        if (sz.Width > tier.W) sizePx *= tier.W / sz.Width;

        using var font = NerdFont.CreateFont(sizePx);
        sz = g.MeasureString(element, font, PointF.Empty, sf);
        g.DrawString(element, font, Brushes.White,
            (tier.W - sz.Width) / 2f, (fullH - sz.Height) / 2f, sf);

        int startY = half == SplitHalf.Top ? 0 : tier.H;
        return PackCrop1bpp(bmp, startY, tier.H);
    }

    // Packs a bitmap to 1bpp: row-major, MSB-first, rows padded to a byte
    // boundary (per the protocol spec; must match firmware's decoder).
    public static byte[] Pack1bpp(Bitmap bmp)
    {
        int rowBytes = (bmp.Width + 7) / 8;
        var packed = new byte[rowBytes * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).R > 127)
                    packed[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
        return packed;
    }

    // Packs [startY, startY+height) rows of bmp to 1bpp.
    private static byte[] PackCrop1bpp(Bitmap bmp, int startY, int height)
    {
        int rowBytes = (bmp.Width + 7) / 8;
        var packed = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, startY + y).R > 127)
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
