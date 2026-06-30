using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

// Draws 5 numbered boxes (BLE profiles 1-5).
// Active profile: filled white with black number (inverted).
// Inactive profiles: white outline + white number.
// Hidden when USB is active.
sealed class ProfileBarWidget : IWidget
{
    public Rectangle    Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);
    public event Action? Invalidated;

    public ProfileBarConfig Config { get; set; } = new();

    private readonly LiveState _state;

    public ProfileBarWidget(LiveState state)
    {
        _state = state;
        _state.Changed += OnStateChanged;
    }

    private void OnStateChanged() => Invalidated?.Invoke();

    public void Start() => Invalidated?.Invoke();
    public void Stop()  { }

    public void Render(Graphics g)
    {
        if (_state.UsbActive) return;

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        float scale   = Math.Clamp(Config.Scale, 0.4f, 2.0f);
        float boxSize = 12f * scale;
        float gap     = 2f  * scale;
        float totalW  = 5 * boxSize + 4 * gap;

        float cx     = Bounds.X + Bounds.Width  / 2f;
        float cy     = Bounds.Y + Bounds.Height / 2f;
        float startX = cx - totalW / 2f;
        float y      = cy - boxSize / 2f;

        float fontSize = Math.Max(6f, boxSize * 0.65f);
        using var font = new Font("Consolas", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        int active = _state.BleProfile;  // 0-4

        for (int i = 0; i < 5; i++)
        {
            float bx  = startX + i * (boxSize + gap);
            bool  sel = i == active;

            string n  = $"{i + 1}";
            SizeF  sz = g.MeasureString(n, font);
            float  tx = bx + (boxSize - sz.Width)  / 2f;
            float  ty = y  + (boxSize - sz.Height) / 2f;

            if (sel)
            {
                g.FillRectangle(Brushes.White, bx, y, boxSize, boxSize);
                g.DrawString(n, font, Brushes.Black, tx, ty);
            }
            else
            {
                g.DrawRectangle(Pens.White, bx, y, boxSize - 1f, boxSize - 1f);
                g.DrawString(n, font, Brushes.White, tx, ty);
            }
        }
    }

    public void Dispose() => _state.Changed -= OnStateChanged;
}
