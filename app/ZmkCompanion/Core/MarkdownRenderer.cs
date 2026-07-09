using System.Drawing;
using System.Windows.Forms;

namespace ZmkCompanion.Core;

// Renders a small, deliberate subset of Markdown into a RichTextBox: level 1/2
// headings, **bold** spans, "- " bullet lists, blank-line paragraph breaks, and
// plain-text URLs (via RichTextBox.DetectUrls — no explicit [text](url) syntax).
// Not a general Markdown parser: no tables, code fences, images, or nested lists.
// Good enough for WelcomeForm's static content; reach for a real engine (and
// probably WebView2) if the content grows beyond that.
static class MarkdownRenderer
{
    public static void Render(RichTextBox target, string markdown, Font baseFont)
    {
        target.Clear();
        target.SelectionFont = baseFont;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();

            if (line.Length == 0)
            {
                AppendLine(target, "", baseFont);
                continue;
            }

            if (line.StartsWith("## "))
            {
                AppendHeading(target, line[3..], baseFont, 1.15f);
                continue;
            }
            if (line.StartsWith("# "))
            {
                AppendHeading(target, line[2..], baseFont, 1.35f);
                continue;
            }
            if (line.StartsWith("- "))
            {
                AppendBullet(target, line[2..], baseFont);
                continue;
            }
            // "1. text" / "2. text" ordered list items — rendered as plain
            // paragraphs (the numbers already come through as normal text).
            AppendParagraph(target, line, baseFont);
        }

        target.SelectionStart = 0;
        target.ScrollToCaret();
    }

    private static void AppendHeading(RichTextBox rtb, string text, Font baseFont, float scale)
    {
        int start = rtb.TextLength;
        rtb.AppendText(text + "\n");
        rtb.Select(start, text.Length);
        rtb.SelectionFont = new Font(baseFont.FontFamily, baseFont.Size * scale, FontStyle.Bold);
        rtb.SelectionColor = Color.White;
        rtb.Select(rtb.TextLength, 0);
        rtb.SelectionFont = baseFont;
    }

    private static void AppendBullet(RichTextBox rtb, string text, Font baseFont)
    {
        int start = rtb.TextLength;
        rtb.SelectionBullet = true;
        AppendInlineBold(rtb, text, baseFont);
        rtb.AppendText("\n");
        rtb.Select(start, rtb.TextLength - start);
        rtb.SelectionBullet = true;
        rtb.Select(rtb.TextLength, 0);
        rtb.SelectionBullet = false;
    }

    private static void AppendParagraph(RichTextBox rtb, string text, Font baseFont)
    {
        AppendInlineBold(rtb, text, baseFont);
        rtb.AppendText("\n");
    }

    private static void AppendLine(RichTextBox rtb, string text, Font baseFont) =>
        rtb.AppendText(text + "\n");

    // Splits on "**bold**" and "`code`" spans, styling each accordingly.
    // Doesn't handle the two nested inside each other or overlapping.
    private static void AppendInlineBold(RichTextBox rtb, string text, Font baseFont)
    {
        int i = 0;
        bool bold = false;
        while (i < text.Length)
        {
            int nextBold = text.IndexOf("**", i, StringComparison.Ordinal);
            int nextCode = text.IndexOf('`', i);
            bool codeIsNext = nextCode >= 0 && (nextBold < 0 || nextCode < nextBold);

            if (codeIsNext)
            {
                AppendStyled(rtb, text[i..nextCode], bold ? new Font(baseFont, FontStyle.Bold) : baseFont);
                int close = text.IndexOf('`', nextCode + 1);
                int codeEnd = close < 0 ? text.Length : close;
                AppendStyled(rtb, text[(nextCode + 1)..codeEnd],
                    new Font(FontFamily.GenericMonospace, baseFont.Size * 0.95f));
                i = close < 0 ? text.Length : close + 1;
                continue;
            }

            string segment = nextBold < 0 ? text[i..] : text[i..nextBold];
            AppendStyled(rtb, segment, bold ? new Font(baseFont, FontStyle.Bold) : baseFont);
            if (nextBold < 0) break;
            bold = !bold;
            i = nextBold + 2;
        }
    }

    private static void AppendStyled(RichTextBox rtb, string segment, Font font)
    {
        if (segment.Length == 0) return;
        int start = rtb.TextLength;
        rtb.AppendText(segment);
        rtb.Select(start, segment.Length);
        rtb.SelectionFont = font;
        rtb.Select(rtb.TextLength, 0);
    }
}
