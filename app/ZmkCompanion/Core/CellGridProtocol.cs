namespace ZmkCompanion.Core;

// Cell-grid display protocol (docs/cell_grid_protocol.md v1.2).
//
// v1.1 — MsgLayout (0x01): (tier_id, repeat) entries; firmware uses a
//         hardcoded tier table.  Still supported for backward compat.
// v1.2 — MsgLayoutV2 (0x04): (W, H, repeat) entries; firmware is
//         dimension-agnostic — no need to keep tier tables in sync.
//         New tiers can be added app-side without a firmware change.
sealed record CellTier(byte Id, string Name, int W, int H)
{
    public int Cols  => BitmapFrame.Width / W;
    public int Bytes => ((W + 7) / 8) * H;   // 1bpp, rows padded to byte boundary
}

static class CellGridProtocol
{
    public const byte MsgLayout   = 0x01;
    public const byte MsgCell     = 0x02;
    public const byte MsgClear    = 0x03;
    public const byte MsgLayoutV2 = 0x04;  // sends (W, H, repeat) — no tier table needed

    // Rectangular tiers (IDs 0-6) — shared with firmware tier table (v1.1 compat).
    // Square tiers (IDs 7-16) — self-describing via LAYOUT_v2; no firmware table entry needed.
    public static readonly CellTier[] Tiers =
    [
        // ── Rectangular ───────────────────────────────────────────────────────
        new(0,  "small_impar",    6,  10),
        new(1,  "small_par",      8,  13),
        new(2,  "medium_impar",   9,  15),
        new(3,  "medium_par",     11, 20),
        new(4,  "large_impar",    13, 22),
        new(5,  "large_par",      16, 28),
        new(6,  "micro",          2,  2),
        // ── Square (require LAYOUT_v2 / firmware ≥ v1.2) ─────────────────────
        new(7,  "small_sq_impar",  6,  6),
        new(8,  "small_sq_par",    8,  8),
        new(9,  "medium_sq_impar", 9,  9),
        new(10, "medium_sq_par",   11, 11),
        new(11, "large_sq_impar",  13, 13),
        new(12, "large_sq_par",    16, 16),
        new(13, "huge_sq_impar",   22, 22),
        new(14, "huge_sq_par",     34, 34),
        new(15, "half_sq_single",  34, 34),  // semantic alias: use with 1 glyph
        new(16, "full_sq_single",  68, 68),  // full-width square; 1 col
    ];

    // LAYOUT v1.1: run-length entries (tier_id, repeat). Max 16 entries.
    // Use only for rectangular tiers (IDs 0-6) with firmware that has the tier table.
    public static byte[] BuildLayout(params (byte TierId, byte Repeat)[] entries)
    {
        if (entries.Length > 16)
            throw new ArgumentException("LAYOUT allows at most 16 entries");
        var msg = new byte[2 + entries.Length * 2];
        msg[0] = MsgLayout;
        msg[1] = (byte)entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            msg[2 + i * 2] = entries[i].TierId;
            msg[3 + i * 2] = entries[i].Repeat;
        }
        return msg;
    }

    // LAYOUT v1.2: explicit (W, H, repeat) entries. Max 16 entries.
    // Firmware receives exact cell dimensions — no tier table lookup needed.
    public static byte[] BuildLayoutV2(params (byte W, byte H, byte Repeat)[] entries)
    {
        if (entries.Length > 16)
            throw new ArgumentException("LAYOUT_v2 allows at most 16 entries");
        var msg = new byte[2 + entries.Length * 3];
        msg[0] = MsgLayoutV2;
        msg[1] = (byte)entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            msg[2 + i * 3] = entries[i].W;
            msg[3 + i * 3] = entries[i].H;
            msg[4 + i * 3] = entries[i].Repeat;
        }
        return msg;
    }

    public static byte[] BuildCell(int rowIndex, int colIndex, byte[] bitmap)
    {
        var msg = new byte[4 + bitmap.Length];
        msg[0] = MsgCell;
        msg[1] = (byte)rowIndex;
        msg[2] = (byte)colIndex;
        msg[3] = (byte)bitmap.Length;
        bitmap.CopyTo(msg, 4);
        return msg;
    }

    public static byte[] BuildClear() => [MsgClear];
}
