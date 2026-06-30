using System.Drawing;

namespace ZmkCompanion.Core;

// Serializable description of one widget on the 68×160 canvas.
sealed class WidgetPlacement
{
    public string Type { get; set; } = "clock";
    public int    X    { get; set; } = 0;
    public int    Y    { get; set; } = 0;
    public int    W    { get; set; } = BitmapFrame.Width;
    public int    H    { get; set; } = BitmapFrame.Height;

    public Rectangle  ToRectangle() => new(X, Y, W, H);
    public WidgetPlacement Clone()  => new() { Type = Type, X = X, Y = Y, W = W, H = H };
}
