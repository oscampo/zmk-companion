using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Renders a plain-text string (with \n line breaks) onto display-sized Bitmap
// "pages". Used to convert CLI pipe text (zkc "line1\nline2") to the bitmap
// wire format.
static class BitmapTextRenderer
{
    private const float FontSizePx = 12f;
    // Only vertical padding: any horizontal inset eats into the ~9-10 chars
    // that already barely fit a 68px-wide line at this font size, so text
    // starts flush against the left edge (x=0).
    private const float PaddingY   = 1f;

    // Splits `text` into as many display-sized "pages" as needed: hard line
    // breaks (\n) first, then each resulting line is soft-wrapped by measured
    // pixel width (not an assumed char count — DrawString's width for a given
    // string is font/hinting dependent, measuring is the only reliable way
    // to know exactly how many characters fit before the canvas edge), and
    // finally the wrapped lines are chunked into pages of however many rows
    // fit in the canvas height.
    public static List<Bitmap> RenderPages(string text)
    {
        using var measureBmp = BitmapFrame.CreateCanvas();
        using var g          = Graphics.FromImage(measureBmp);
        g.TextRenderingHint   = TextRenderingHint.SingleBitPerPixelGridFit;
        using var font        = new Font("Consolas", FontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);

        float lineHeight   = font.GetHeight(g);
        int   linesPerPage = Math.Max(1, (int)((BitmapFrame.Height - 2 * PaddingY) / lineHeight));

        var wrapped = new List<string>();
        foreach (string hardLine in text.Split('\n', StringSplitOptions.None))
            wrapped.AddRange(WrapByWidth(hardLine, font, g));

        var pages = new List<Bitmap>();
        for (int i = 0; i < wrapped.Count; i += linesPerPage)
        {
            var bmp = BitmapFrame.CreateCanvas();
            using var pg = Graphics.FromImage(bmp);
            pg.Clear(Color.Black);
            pg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            float y = PaddingY;
            for (int j = i; j < Math.Min(i + linesPerPage, wrapped.Count); j++)
            {
                if (wrapped[j].Length > 0)
                    pg.DrawString(wrapped[j], font, Brushes.White, 0f, y, StringFormat.GenericTypographic);
                y += lineHeight;
            }
            pages.Add(bmp);
        }

        if (pages.Count == 0) pages.Add(BitmapFrame.CreateCanvas()); // empty text → one blank page
        return pages;
    }

    // Greedy char-by-char wrap (not word-wrap: this renders short status
    // strings/sensor readouts via `zkc`, not prose, so breaking mid-word is
    // fine and keeps the logic simple/predictable).
    private static List<string> WrapByWidth(string line, Font font, Graphics g)
    {
        const float maxWidth = BitmapFrame.Width; // flush to both edges, see PaddingY comment
        var result = new List<string>();
        if (line.Length == 0) { result.Add(""); return result; }

        int start = 0;
        while (start < line.Length)
        {
            int end = start + 1;
            while (end < line.Length &&
                   g.MeasureString(line[start..(end + 1)], font, int.MaxValue, StringFormat.GenericTypographic).Width <= maxWidth)
                end++;
            result.Add(line[start..end]);
            start = end;
        }
        return result;
    }
}
