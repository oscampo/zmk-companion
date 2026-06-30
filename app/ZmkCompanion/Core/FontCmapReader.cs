namespace ZmkCompanion.Core;

// Parses the cmap table of the embedded Fira Code NF font and returns
// the set of all Unicode codepoints that have a mapped glyph.
// Handles format-4 (BMP) and format-12 (full Unicode) subtables.
// Result is cached after the first call.
static class FontCmapReader
{
    private static int[]? _cached;

    public static int[] GetAllCodepoints()
    {
        if (_cached != null) return _cached;
        _cached = Parse(NerdFont.GetFontData());
        return _cached;
    }

    private static int[] Parse(byte[] data)
    {
        var cps = new SortedSet<int>();

        // Offset table: numTables at byte 4
        int numTables = U16(data, 4);

        // Find 'cmap' table in the directory (each record is 16 bytes, starting at 12)
        int cmapBase = -1;
        for (int i = 0; i < numTables; i++)
        {
            int off = 12 + i * 16;
            if (data[off] == 'c' && data[off+1] == 'm' && data[off+2] == 'a' && data[off+3] == 'p')
            {
                cmapBase = (int)U32(data, off + 8);
                break;
            }
        }
        if (cmapBase < 0) return [];

        // cmap header: version(2), numSubTables(2), then records of 8 bytes each
        int numSub = U16(data, cmapBase + 2);
        for (int i = 0; i < numSub; i++)
        {
            int rec = cmapBase + 4 + i * 8;
            int subOff = cmapBase + (int)U32(data, rec + 4);
            int fmt = U16(data, subOff);

            if (fmt == 4)  ParseFmt4(data, subOff, cps);
            if (fmt == 12) ParseFmt12(data, subOff, cps);
        }

        return [.. cps];
    }

    // Format 4: BMP Unicode (codepoints 0–65535, segments of [start,end] pairs)
    private static void ParseFmt4(byte[] data, int off, SortedSet<int> cps)
    {
        int segCount   = U16(data, off + 6) / 2;
        int endOff     = off + 14;
        int startOff   = endOff + segCount * 2 + 2;  // +2 for reservedPad

        for (int i = 0; i < segCount; i++)
        {
            int end   = U16(data, endOff   + i * 2);
            int start = U16(data, startOff + i * 2);
            if (end == 0xFFFF) continue;  // sentinel segment
            for (int cp = start; cp <= end; cp++) cps.Add(cp);
        }
    }

    // Format 12: Full Unicode (groups of [startCode, endCode, startGlyphID])
    private static void ParseFmt12(byte[] data, int off, SortedSet<int> cps)
    {
        int numGroups = (int)U32(data, off + 12);
        for (int i = 0; i < numGroups; i++)
        {
            int gOff  = off + 16 + i * 12;
            int start = (int)U32(data, gOff);
            int end   = (int)U32(data, gOff + 4);
            for (int cp = start; cp <= end && cp <= 0x10FFFF; cp++) cps.Add(cp);
        }
    }

    private static int  U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static uint U32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o+1] << 16) | ((uint)d[o+2] << 8) | d[o+3];
}
