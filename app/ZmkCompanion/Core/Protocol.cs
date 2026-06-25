using Microsoft.Win32;

namespace ZmkCompanion.Core;

// Builds protocol messages as defined in docs/protocol.md.
// All public methods return a UTF-8-safe string ready for TextConverter.ToBytes().
static class Protocol
{
    // ── Clock ─────────────────────────────────────────────────────────────────

    // T:<local_unix>:A  (12h)  or  T:<local_unix>:H  (24h)
    // local_unix = UTC epoch + local offset; firmware does % 86400 to get tod.
    public static string BuildClock()
    {
        var now = DateTimeOffset.Now;
        long localUnix = now.ToUnixTimeSeconds();
        // Adjust: we want seconds since UTC midnight + local offset,
        // i.e. local wall-clock seconds since Unix epoch without UTC.
        long localEpochSec = (long)now.DateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        char mode = Detect12h() ? 'A' : 'H';
        return $"T:{localEpochSec}:{mode}";
    }

    // ── Weather ───────────────────────────────────────────────────────────────

    // W:<city>\n<temp>\n<label>\x01<icon>
    public static string BuildWeather(string city, string tempStr, string label, char icon) =>
        $"W:{city}\n{tempStr}\n{label}\x01{icon}";

    // ── Text + optional icon ──────────────────────────────────────────────────

    public static string BuildText(string line1, string? line2 = null, string? line3 = null, char icon = '\0')
    {
        var parts = new[] { line1, line2, line3 }
            .Where(s => s is not null)
            .Select(s => s!);
        string text = string.Join("\n", parts);
        return icon != '\0' ? $"{text}\x01{icon}" : text;
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    public static string BuildClear() => "";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool Detect12h()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International");
            if (key?.GetValue("iTime") is string itime)
                return itime == "0"; // 0 = 12h, 1 = 24h
        }
        catch { }
        // Fallback: format a known PM time and check for AM/PM markers.
        string formatted = new DateTime(2000, 1, 1, 13, 0, 0).ToString("T");
        return formatted.Contains("PM", StringComparison.OrdinalIgnoreCase)
            || formatted.Contains("AM", StringComparison.OrdinalIgnoreCase);
    }
}
