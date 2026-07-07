using System.Globalization;
using System.Text;

namespace ZmkCompanion.Core;

// Loads the bundled name -> codepoint table (Resources/glyphnames.tsv,
// generated from the official nerd-fonts glyphnames.json, name+hex only,
// metadata stripped) for GlyphPickerDialog's search-by-name.
//
// Independent of FontCmapReader: a name here can point at a codepoint the
// embedded FiraCode NF build doesn't actually contain (the source list
// covers every Nerd Font icon set variant, not just this one font).
// GlyphPickerDialog cross-checks against FontCmapReader.GetAllCodepoints()
// before using a match, so a name with no renderable glyph in this font
// just never surfaces, rather than showing a tofu box.
static class GlyphNames
{
    private static (string Name, int Codepoint)[]? _cached;

    public static (string Name, int Codepoint)[] GetAll()
    {
        _cached ??= Load();
        return _cached;
    }

    private static (string Name, int Codepoint)[] Load()
    {
        var asm  = typeof(GlyphNames).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("glyphnames.tsv", StringComparison.OrdinalIgnoreCase));

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var list = new List<(string, int)>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int tab = line.IndexOf('\t');
            if (tab < 0) continue;
            string glyphName = line[..tab];
            string hex       = line[(tab + 1)..];
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                list.Add((glyphName, cp));
        }
        return [.. list];
    }
}
