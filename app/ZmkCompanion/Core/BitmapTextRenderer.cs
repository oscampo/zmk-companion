using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Renders a plain-text string (with \n line breaks) onto a display-sized Bitmap.
// Used to convert CLI pipe text (zkc "line1\nline2") to the bitmap wire format.
static class BitmapTextRenderer
{
    private const float FontSizePx = 12f;

    public static Bitmap Render(string text)
    {
        var bmp = BitmapFrame.CreateCanvas();
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        string[] lines = text.Split('\n', StringSplitOptions.None);
        using var font = new Font("Consolas", FontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);

        float lineHeight = font.GetHeight(g);
        float totalH     = lines.Length * lineHeight;
        float y          = (BitmapFrame.Height - totalH) / 2f;

        foreach (string line in lines)
        {
            if (line.Length > 0)
            {
                SizeF sz = g.MeasureString(line, font);
                g.DrawString(line, font, Brushes.White, (BitmapFrame.Width - sz.Width) / 2f, y);
            }
            y += lineHeight;
        }

        return bmp;
    }
}
