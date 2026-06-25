using System.Text;

namespace ZmkCompanion.Core;

// Port of to_display() from keyboard_display.py.
// Strips characters the firmware fonts can't render, keeping Basic Latin,
// Latin-1 Supplement, and the Nerd Font private-use ranges used by the protocol.
static class TextConverter
{
    private static readonly (int Lo, int Hi)[] FontRanges =
    [
        (0x0020, 0x007F), // Basic Latin
        (0x00A1, 0x00FF), // Latin-1 Supplement
        (0xE0A0, 0xE0D4), // Powerline
        (0xE200, 0xE2A9), // FA Extension
        (0xE300, 0xE3FF), // Weather icons
        (0xEE00, 0xEE0F), // FiraCode progress bar
        (0xF000, 0xF2E0), // Font Awesome
    ];

    // Control chars the protocol uses as delimiters — always pass through.
    private static readonly HashSet<char> ProtocolChars = ['\n', '\r', '\x01'];

    // Multi-char expansions for common non-Latin-1 letters.
    private static readonly Dictionary<char, string> ExtraMap = new()
    {
        ['ß'] = "ss",
        ['æ'] = "ae", ['Æ'] = "AE",
        ['œ'] = "oe", ['Œ'] = "OE",
    };

    public static string Convert(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ProtocolChars.Contains(ch))
            {
                sb.Append(ch);
                continue;
            }
            if (ExtraMap.TryGetValue(ch, out var expansion))
            {
                sb.Append(expansion);
                continue;
            }
            int cp = ch;
            if (InFontRange(cp))
            {
                sb.Append(ch);
                continue;
            }
            // NFD decomposition: keep base character if it's in range, drop combining marks.
            string nfd = ch.ToString().Normalize(NormalizationForm.FormD);
            char baseChar = nfd[0];
            if (InFontRange(baseChar))
                sb.Append(baseChar);
            // else: drop the character
        }
        return sb.ToString();
    }

    // Encode to UTF-8, capped at 64 bytes as per protocol spec.
    public static byte[] ToBytes(string text) =>
        Encoding.UTF8.GetBytes(Convert(text)).Take(64).ToArray();

    private static bool InFontRange(int cp)
    {
        foreach (var (lo, hi) in FontRanges)
            if (cp >= lo && cp <= hi) return true;
        return false;
    }
}
