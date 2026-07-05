using System.Drawing;
using System.Drawing.Text;

namespace ZmkCompanion.Core;

// Renders a plain-text string (with \n line breaks) onto a display-sized Bitmap.
// Used to convert CLI pipe text (zkc "line1\nline2") to the bitmap wire format.
static class BitmapTextRenderer
{
    private const float FontSizePx = 12f;
    private const float Padding    = 1f;

    public static Bitmap Render(string text)
    {
        var bmp = BitmapFrame.CreateCanvas();
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        string[] lines = text.Split('\n', StringSplitOptions.None);
        using var font = new Font("Consolas", FontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);

        float lineHeight = font.GetHeight(g);
        float y = Padding; // top-left, like a terminal

        foreach (string line in lines)
        {
            if (line.Length > 0)
                g.DrawString(line, font, Brushes.White, Padding, y);
            y += lineHeight;
        }

        return bmp;
    }
}
