namespace ZmkCompanion.Core;

// Cell-grid display protocol (docs/cell_grid_protocol.md v1.1).
// The tier table is a compile-time constant shared with firmware — any
// change here is a protocol version bump requiring a coordinated firmware
// change, so do not edit casually.
sealed record CellTier(byte Id, string Name, int W, int H)
{
    public int Cols  => BitmapFrame.Width / W;
    public int Bytes => ((W + 7) / 8) * H;   // 1bpp, rows padded to byte boundary
}

static class CellGridProtocol
{
    public const byte MsgLayout = 0x01;
    public const byte MsgCell   = 0x02;
    public const byte MsgClear  = 0x03;

    public static readonly CellTier[] Tiers =
    [
        new(0, "small_impar",  6,  10),
        new(1, "small_par",    8,  13),
        new(2, "medium_impar", 9,  15),
        new(3, "medium_par",   11, 20),
        new(4, "large_impar",  13, 22),
        new(5, "large_par",    16, 28),
        new(6, "micro",        2,  2),
    ];

    // LAYOUT: run-length entries (tier_id, repeat). Max 16 entries.
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
