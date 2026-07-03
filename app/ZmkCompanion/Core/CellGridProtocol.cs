namespace ZmkCompanion.Core;

// Cell-grid display protocol (docs/cell_grid_protocol.md v1.2).
//
// v1.1 — MsgLayout (0x01): (tier_id, repeat) entries; firmware uses a
//         hardcoded tier table.  Still supported for backward compat.
// v1.2 — MsgLayoutV2 (0x04): (W, H, repeat) entries; firmware is
//         dimension-agnostic — no need to keep tier tables in sync.
//         New tiers can be added app-side without a firmware change.
//
// BLE cell-size constraint: BuildCell encodes bitmap length as uint16 LE.
// CELL message = 5 + bitmap.Length bytes.  Practical BLE ATT limit ≈ 512 B,
// so max bitmap = 507 bytes.  Max practical tile: 34×34 = 170 B (safe).
// Tiles wider than 34px are not supported without chunked writes.
sealed record CellTier(byte Id, string Name, int W, int H, int? ColsOverride = null)
{
    // ColsOverride lets a tile behave as fewer logical columns than W implies
    // (e.g. half_sq_single has 2 physical cols in firmware but 1 logical col
    // in the compositor so only col 0 is ever written).
    public int Cols  => ColsOverride ?? BitmapFrame.Width / W;
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
        // Bitmap sizes (bytes) at each size: 6=6, 8=8, 9=18, 11=22, 13=26,
        // 16=32, 22=66, 34=170. All ≤ 255 and within typical BLE ATT budget.
        new(7,  "small_sq_impar",  6,  6),
        new(8,  "small_sq_par",    8,  8),
        new(9,  "medium_sq_impar", 9,  9),
        new(10, "medium_sq_par",   11, 11),
        new(11, "large_sq_impar",  13, 13),
        new(12, "large_sq_par",    16, 16),
        new(13, "huge_sq_impar",   22, 22),
        new(14, "huge_sq_par",     34, 34),
        // half_sq_single: same 34×34 cell as huge_sq_par but ColsOverride=1
        // so the compositor only renders/sends col 0 (icon left-aligned).
        // Firmware sees (W=34,H=34) → 2 physical cols; col 1 stays blank.
        new(15, "half_sq_single",  34, 34, ColsOverride: 1),
        // full_sq_single (68×68) removed: 612-byte bitmap exceeds uint8 limit
        // and BLE ATT budget.  Needs chunked-write protocol extension first.
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
        // bitmap_len is uint16 LE (bytes 3-4) to support tiles larger than 255 bytes.
        // Firmware must read 2 bytes for the length field.
        int len = bitmap.Length;
        var msg = new byte[5 + len];
        msg[0] = MsgCell;
        msg[1] = (byte)rowIndex;
        msg[2] = (byte)colIndex;
        msg[3] = (byte)(len & 0xFF);         // length low byte
        msg[4] = (byte)((len >> 8) & 0xFF);  // length high byte
        bitmap.CopyTo(msg, 5);
        return msg;
    }

    public static byte[] BuildClear() => [MsgClear];
}
