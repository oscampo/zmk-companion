using System.Drawing;
using System.Text.Json;

namespace ZmkCompanion.Core;

// Serializable description of one widget on the 68×160 canvas.
// CX/CY are the CENTER of the widget region (not the top-left corner).
// ToRectangle() converts back to a top-left Rectangle for GDI+.
sealed class WidgetPlacement
{
    public string Type { get; set; } = "clock";
    public int    CX   { get; set; } = BitmapFrame.Width  / 2;  // 34
    public int    CY   { get; set; } = BitmapFrame.Height / 2;  // 80
    public int    W    { get; set; } = BitmapFrame.Width;        // 68
    public int    H    { get; set; } = BitmapFrame.Height;       // 160

    // Type-specific config (glyph overrides, formatting options, etc.)
    public JsonElement? Config { get; set; }

    public Rectangle ToRectangle() => new(CX - W / 2, CY - H / 2, W, H);

    public WidgetPlacement Clone() => new()
    {
        Type   = Type,
        CX     = CX, CY = CY,
        W      = W,  H  = H,
        Config = Config,
    };
}
