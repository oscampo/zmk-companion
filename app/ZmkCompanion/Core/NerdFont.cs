using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ZmkCompanion.Core;

// Loads FiraCode Nerd Font Mono from the embedded resource and exposes it for
// GDI+. Mono specifically (not the plain "FiraCode Nerd Font" variant): the
// non-Mono variant lets icon glyphs draw wider than one monospace advance
// width by design, which was the actual cause of several icons (soccer ball,
// umbrella, coffee cup) visually overlapping/clipping their neighbor in the
// cell-grid — not a centering bug, see CellGridRenderer.
// The PrivateFontCollection is kept alive for the process lifetime.
static class NerdFont
{
    private static readonly PrivateFontCollection _pfc;
    private static readonly byte[]               _fontData;
    public  static readonly FontFamily            Family;

    static NerdFont()
    {
        (_pfc, _fontData) = Load();
        Family = _pfc.Families[0];
    }

    // Raw bytes of the embedded Fira Code NF Mono font (used by FontCmapReader).
    internal static byte[] GetFontData() => _fontData;

    private static (PrivateFontCollection pfc, byte[] data) Load()
    {
        var pfc  = new PrivateFontCollection();
        var asm  = typeof(NerdFont).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("FIRACODENERDFONTMONO-REGULAR.TTF", StringComparison.OrdinalIgnoreCase));

        using var stream = asm.GetManifestResourceStream(name)!;
        int    len = (int)stream.Length;
        byte[] buf = new byte[len];
        stream.ReadExactly(buf);

        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try   { pfc.AddMemoryFont(handle.AddrOfPinnedObject(), len); }
        finally { handle.Free(); }

        return (pfc, buf);
    }

    public static Font CreateFont(float sizePx, FontStyle style = FontStyle.Regular) =>
        new(Family, sizePx, style, GraphicsUnit.Pixel);

    // ── Font Awesome glyphs (BMP, U+F000-F2FF) ──────────────────────────────
    public const string BatteryFull     = "";  // nf-fa-battery_4  U+F240
    public const string Battery3Q       = "";  // nf-fa-battery_3  U+F241
    public const string BatteryHalf     = "";  // nf-fa-battery_2  U+F242
    public const string Battery1Q       = "";  // nf-fa-battery_1  U+F243
    public const string BatteryEmpty    = "";  // nf-fa-battery_0  U+F244
    public const string BatteryCharging = "";  // nf-fa-bolt       U+F0E7
    public const string Bluetooth       = "";  // nf-fa-bluetooth  U+F293
    public const string BluetoothB      = "";  // nf-fa-bluetooth_b U+F294
    public const string Usb             = "";  // nf-fa-usb        U+F287
    public const string Wifi            = "";  // nf-fa-wifi       U+F1EB

    // ── Material Design glyphs (Supplementary PUA; use char.ConvertFromUtf32) ──
    public static string MdBluetooth        => Md(0xF00AF);  // md-bluetooth
    public static string MdBluetoothConnect => Md(0xF00B1);  // md-bluetooth_connect
    public static string MdUsb              => Md(0xF0553);  // md-usb

    // ── FA battery glyph — classic 5-tier style ──────────────────────────────
    public static string BatteryGlyph(int level, bool charging)
    {
        if (charging) return BatteryCharging;
        return level switch
        {
            >= 90 => BatteryFull,
            >= 65 => Battery3Q,
            >= 35 => BatteryHalf,
            >= 10 => Battery1Q,
            _     => BatteryEmpty,
        };
    }

    // ── MD battery glyphs ─────────────────────────────────────────────────────

    // MD 10-step level: battery_10 (F007A) … battery_90 (F0082), battery (F0079) = full.
    public static string MdBatteryLevelGlyph(int level, bool charging)
    {
        if (charging) return Md(0xF0084);  // md-battery_charging
        if (level >= 95) return Md(0xF0079);  // md-battery (full)
        int decade = Math.Max(10, (int)Math.Round(level / 10.0) * 10);
        decade     = Math.Min(decade, 90);
        return Md(0xF007A + decade / 10 - 1);   // 10→F007A … 90→F0082
    }

    // MD 3-tier threshold: battery_low / medium / high.
    public static string MdBatteryThresholdGlyph(int level, bool charging)
    {
        if (charging)     return Md(0xF0084);  // md-battery_charging
        if (level >= 100) return Md(0xF0079);  // md-battery (full)
        if (level >= 70)  return Md(0xF12A3);  // md-battery_high
        if (level >= 30)  return Md(0xF12A2);  // md-battery_medium
        return Md(0xF12A1);                     // md-battery_low
    }

    // ── Numeric glyph tables (MD, Supplementary PUA) ─────────────────────────
    // All indexed by digit 0–9.

    // md-numeric_X_box — filled box with digit (0–9 all supported)
    private static readonly int[] _numBox =
        [0xF03A1, 0xF03A4, 0xF03A7, 0xF03AA, 0xF03AD, 0xF03B1, 0xF03B3, 0xF03B6, 0xF03B9, 0xF03BC];

    // md-numeric_X_box_outline — outlined box with digit (0–9 all supported)
    private static readonly int[] _numBoxOutline =
        [0xF03A3, 0xF03A6, 0xF03A9, 0xF03AC, 0xF03AE, 0xF03B0, 0xF03B5, 0xF03B8, 0xF03BB, 0xF03BE];

    // md-numeric_X_box_multiple — double-filled box (0–9 all supported)
    private static readonly int[] _numBoxMultiple =
        [0xF0F0E, 0xF0F0F, 0xF0F10, 0xF0F11, 0xF0F12, 0xF0F13, 0xF0F14, 0xF0F15, 0xF0F16, 0xF0F17];

    // md-numeric_X_box_multiple_outline — older outlined double-box; ordering is non-sequential in font
    private static readonly int[] _numBoxMultipleOutline =
        [0xF03A2, 0xF03A5, 0xF03A8, 0xF03AB, 0xF03B2, 0xF03AF, 0xF03B4, 0xF03B7, 0xF03BA, 0xF03BD];

    // Returns the Nerd Font glyph string for digit 0–9 in the requested style,
    // or null when that style has no glyph for the given digit.
    // Styles: box | box_outline | box_multiple | box_multiple_outline
    //         plain | circle | circle_outline  (digits 1–9 only)
    public static string? NumericGlyph(int digit, string style)
    {
        if (digit is < 0 or > 9) return null;
        return style switch
        {
            "box"                  => Md(_numBox[digit]),
            "box_outline"          => Md(_numBoxOutline[digit]),
            "box_multiple"         => Md(_numBoxMultiple[digit]),
            "box_multiple_outline" => Md(_numBoxMultipleOutline[digit]),
            // plain: md-numeric_1…9; no '0' — use alpha_o as visual substitute
            "plain" when digit == 0         => AlphaGlyph('o', "plain"),
            "plain"                         => Md(0xF0B3A + digit - 1),
            // circle: no '0' — use alpha_o circle as substitute
            "circle" when digit == 0        => AlphaGlyph('o', "circle"),
            "circle"                        => Md(0xF0CA0 + (digit - 1) * 2),
            "circle_outline" when digit == 0 => AlphaGlyph('o', "circle_outline"),
            "circle_outline"                => Md(0xF0CA1 + (digit - 1) * 2),
            _ => null,
        };
    }

    // Returns the Nerd Font glyph string for letter a–z (case-insensitive) in
    // the requested style, or null if c is not an ASCII letter.
    // Accented characters are normalized to their base letter (ñ→n, á→a, etc.).
    // Styles: plain | box | box_outline | circle | circle_outline
    public static string? AlphaGlyph(char c, string style)
    {
        c = NormalizeAlpha(c);
        int i = char.ToLower(c) - 'a';
        if (i is < 0 or > 25) return null;
        return style switch
        {
            "plain"          => Md(0xF0AEE + i),
            "box"            => Md(0xF0B08 + i),
            "box_outline"    => Md(0xF0BEB + i * 3),
            "circle"         => Md(0xF0BEC + i * 3),
            "circle_outline" => Md(0xF0BED + i * 3),
            _ => null,
        };
    }

    // Human-readable labels for style option lists in the editor UI.
    public static string NumericStyleLabel(string style) => style switch
    {
        "gdi"                  => "Classic (GDI)",
        "box"                  => "Box (filled)",
        "box_outline"          => "Box outline",
        "box_multiple"         => "Box multiple",
        "box_multiple_outline" => "Box multi outline",
        "plain"                => "Plain (1–9)",
        "circle"               => "Circle (1–9)",
        "circle_outline"       => "Circle outline (1–9)",
        _                      => "Text (default)",
    };

    public static string AlphaStyleLabel(string style) => style switch
    {
        "plain"          => "Plain",
        "box"            => "Box (filled)",
        "box_outline"    => "Box outline",
        "circle"         => "Circle",
        "circle_outline" => "Circle outline",
        _                => "Text (default)",
    };

    // Decomposes an accented character to its ASCII base letter via Unicode NFD.
    // ñ→n, á→a, ü→u, ç→c, etc. Returns c unchanged if no ASCII base is found.
    private static char NormalizeAlpha(char c)
    {
        if (char.IsAsciiLetter(c)) return c;
        string nfd = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (char b in nfd)
            if (char.IsAsciiLetter(b)) return b;
        return c;
    }

    // Shorthand: convert a supplementary Unicode code point to a C# string.
    private static string Md(int cp) => char.ConvertFromUtf32(cp);
}
