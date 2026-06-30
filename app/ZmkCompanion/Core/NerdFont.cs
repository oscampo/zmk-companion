using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ZmkCompanion.Core;

// Loads FiraCode Nerd Font from the embedded resource and exposes it for GDI+.
// The PrivateFontCollection is kept alive for the process lifetime.
static class NerdFont
{
    private static readonly PrivateFontCollection _pfc;
    public  static readonly FontFamily            Family;

    static NerdFont()
    {
        _pfc   = Load();
        Family = _pfc.Families[0];
    }

    private static PrivateFontCollection Load()
    {
        var pfc  = new PrivateFontCollection();
        var asm  = typeof(NerdFont).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("FiraCodeNerdFont-Regular.ttf", StringComparison.OrdinalIgnoreCase));

        using var stream = asm.GetManifestResourceStream(name)!;
        int    len = (int)stream.Length;
        byte[] buf = new byte[len];
        stream.ReadExactly(buf);

        // GDI+ requires a pinned native buffer for AddMemoryFont.
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try   { pfc.AddMemoryFont(handle.AddrOfPinnedObject(), len); }
        finally { handle.Free(); }

        return pfc;
    }

    // Convenience: create a NerdFont Font at the given pixel size.
    public static Font CreateFont(float sizePx, FontStyle style = FontStyle.Regular) =>
        new(Family, sizePx, style, GraphicsUnit.Pixel);

    // ── Glyph codepoints ─────────────────────────────────────────────────────
    // Font Awesome 4 battery (U+F240-F244) and bolt (U+F0E7)
    public const string BatteryFull     = "";  // nf-fa-battery_4
    public const string Battery3Q       = "";  // nf-fa-battery_3
    public const string BatteryHalf     = "";  // nf-fa-battery_2
    public const string Battery1Q       = "";  // nf-fa-battery_1
    public const string BatteryEmpty    = "";  // nf-fa-battery_0
    public const string BatteryCharging = "";  // nf-fa-bolt

    // Connection
    public const string Bluetooth = "";  // nf-fa-bluetooth
    public const string Usb       = "";  // nf-fa-usb

    // Returns the right battery glyph for a given charge level.
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
}
