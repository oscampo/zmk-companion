using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

// Draws 5 numbered slots for BLE profiles 1-5, each in one of three states:
//   Connected  — the currently active BLE profile
//   Assigned   — has a paired device but is not currently connected
//   Free       — no paired device
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

        var cfg = Config;
        if (cfg.ConnectedStyle == "gdi" && cfg.AssignedStyle == "gdi" && cfg.FreeStyle == "gdi")
            RenderGdi(g, cfg);
        else
            RenderGlyphs(g, cfg);
    }

    private SlotState GetSlotState(int i, ProfileBarConfig cfg)
    {
        if (i == _state.BleProfile) return SlotState.Connected;
        return ((cfg.AssignedMask >> i) & 1) == 1 ? SlotState.Assigned : SlotState.Free;
    }

    private string StyleForState(SlotState s, ProfileBarConfig cfg) => s switch
    {
        SlotState.Connected => cfg.ConnectedStyle == "gdi" ? "box"         : cfg.ConnectedStyle,
        SlotState.Assigned  => cfg.AssignedStyle  == "gdi" ? "plain"       : cfg.AssignedStyle,
        _                   => cfg.FreeStyle       == "gdi" ? "box_outline" : cfg.FreeStyle,
    };

    // Classic GDI+ rendering (all styles == "gdi").
    // Connected → white fill + black digit; Assigned → white outline; Free → dim outline.
    private void RenderGdi(Graphics g, ProfileBarConfig cfg)
    {
        float scale   = Math.Clamp(cfg.Scale, 0.4f, 2.0f);
        float boxSize = 12f * scale;
        float gap     = 2f  * scale;
        float totalW  = 5 * boxSize + 4 * gap;

        float cx     = Bounds.X + Bounds.Width  / 2f;
        float cy     = Bounds.Y + Bounds.Height / 2f;
        float startX = cx - totalW / 2f;
        float y      = cy - boxSize / 2f;

        float fontSize = Math.Max(6f, boxSize * 0.65f);
        using var font    = new Font("Consolas", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var dimPen  = new Pen(Color.FromArgb(100, 255, 255, 255));
        using var dimBrush = new SolidBrush(Color.FromArgb(100, 255, 255, 255));

        for (int i = 0; i < 5; i++)
        {
            float bx    = startX + i * (boxSize + gap);
            string n    = $"{i + 1}";
            SizeF  sz   = g.MeasureString(n, font);
            float  tx   = bx + (boxSize - sz.Width)  / 2f;
            float  ty   = y  + (boxSize - sz.Height) / 2f;

            switch (GetSlotState(i, cfg))
            {
                case SlotState.Connected:
                    g.FillRectangle(Brushes.White, bx, y, boxSize, boxSize);
                    g.DrawString(n, font, Brushes.Black, tx, ty);
                    break;
                case SlotState.Assigned:
                    g.DrawRectangle(Pens.White, bx, y, boxSize - 1f, boxSize - 1f);
                    g.DrawString(n, font, Brushes.White, tx, ty);
                    break;
                case SlotState.Free:
                    g.DrawRectangle(dimPen, bx, y, boxSize - 1f, boxSize - 1f);
                    g.DrawString(n, font, dimBrush, tx, ty);
                    break;
            }
        }
    }

    // NF glyph rendering.
    private void RenderGlyphs(Graphics g, ProfileBarConfig cfg)
    {
        float scale    = Math.Clamp(cfg.Scale, 0.4f, 2.0f);
        float fontSize = Math.Max(8f, 16f * scale);
        float gap      = 2f * scale + cfg.LetterSpacing;
        using var font = NerdFont.CreateFont(fontSize);

        var    glyphs = new string[5];
        float  totalW = 0f;

        for (int i = 0; i < 5; i++)
        {
            string style = StyleForState(GetSlotState(i, cfg), cfg);
            glyphs[i]    = NerdFont.NumericGlyph(i + 1, style) ?? $"{i + 1}";
            totalW      += g.MeasureString(glyphs[i], font).Width + (i < 4 ? gap : 0);
        }

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;
        float x  = cx - totalW / 2f;

        for (int i = 0; i < 5; i++)
        {
            SizeF sz = g.MeasureString(glyphs[i], font);
            g.DrawString(glyphs[i], font, Brushes.White, x, cy - sz.Height / 2f);
            x += sz.Width + gap;
        }
    }

    public void Dispose() => _state.Changed -= OnStateChanged;

    private enum SlotState { Connected, Assigned, Free }
}
