using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ZmkCompanion.Core;

// Wire format for the 68×160 monochrome display (characteristic 0x1525).
// 1 bit per pixel, MSB-first, row-major, portrait orientation.
// Each row is 68 px → 9 bytes (4 padding bits at end of each row).
// GDI+ LockBits stride for 1bpp is 12 bytes (4-byte aligned) — we copy only the first 9.
static class BitmapFrame
{
    public const int Width       = 68;
    public const int Height      = 160;
    public const int BytesPerRow = 9;                    // ceil(68 / 8)
    public const int FrameBytes  = Height * BytesPerRow; // 1,440

    // Packs a 32bpp ARGB Bitmap (68×160) into the 1,440-byte wire format.
    // White-ish pixels (average channel > 128) become 1; dark pixels become 0.
    public static byte[] Pack(Bitmap bmp)
    {
        if (bmp.Width != Width || bmp.Height != Height)
            throw new ArgumentException($"Bitmap must be {Width}×{Height}, got {bmp.Width}×{bmp.Height}");

        using var mono = bmp.Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format1bppIndexed);
        var bd = mono.LockBits(new Rectangle(0, 0, Width, Height),
            ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
        try
        {
            int gdiStride = Math.Abs(bd.Stride); // 12 bytes for 68-px wide 1bpp
            byte[] frame = new byte[FrameBytes];
            for (int row = 0; row < Height; row++)
                Marshal.Copy(IntPtr.Add(bd.Scan0, row * gdiStride), frame, row * BytesPerRow, BytesPerRow);
            return frame;
        }
        finally { mono.UnlockBits(bd); }
    }

    // Creates a blank black 32bpp canvas ready for GDI+ drawing.
    public static Bitmap CreateCanvas() => new(Width, Height, PixelFormat.Format32bppArgb);
}
