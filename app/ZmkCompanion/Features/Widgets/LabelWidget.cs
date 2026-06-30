using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

// Generic widget: renders one line of text from a template with {binding} tokens.
// Any literal Unicode (including Nerd Font glyphs) can appear in the template.
// If UseNerdFont=true, the entire string renders in Fira Code NF (covers both
// ASCII text and private-use-area glyph codepoints).
sealed class LabelWidget : IWidget
{
    public Rectangle   Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);
    public event Action? Invalidated;

    public LabelConfig Config { get; set; } = new();

    private readonly LiveState _state;
    private System.Windows.Forms.Timer? _clockTimer;

    public LabelWidget(LiveState state)
    {
        _state = state;
        _state.Changed += OnStateChanged;
    }

    private void OnStateChanged() => Invalidated?.Invoke();

    public void Start()
    {
        Stop();
        Invalidated?.Invoke();
        if (LiveState.HasTimeBind(Config.Template))
            StartClockTimer();
    }

    private void StartClockTimer()
    {
        int msUntilNext = (60 - DateTime.Now.Second) * 1000 - DateTime.Now.Millisecond;
        _clockTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, msUntilNext) };
        _clockTimer.Tick += (_, _) =>
        {
            _clockTimer!.Interval = 60_000;
            Invalidated?.Invoke();
        };
        _clockTimer.Start();
    }

    public void Stop()
    {
        _clockTimer?.Stop(); _clockTimer?.Dispose(); _clockTimer = null;
    }

    public void Render(Graphics g)
    {
        var    cfg  = Config;
        string text = _state.Expand(cfg.Template, cfg.Use24h, cfg);
        if (text.Length == 0) return;

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var style = cfg.Bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = cfg.UseNerdFont
            ? NerdFont.CreateFont(cfg.Size, style)
            : new Font("Consolas", cfg.Size, style, GraphicsUnit.Pixel);

        SizeF sz = g.MeasureString(text, font);
        float cy  = Bounds.Y + Bounds.Height / 2f;
        float y   = cy - sz.Height / 2f;

        float x = cfg.Align switch
        {
            "left"  => Bounds.X,
            "right" => Bounds.Right - sz.Width,
            _       => Bounds.X + Bounds.Width / 2f - sz.Width / 2f,
        };

        g.DrawString(text, font, Brushes.White, x, y);
    }

    public void Dispose()
    {
        Stop();
        _state.Changed -= OnStateChanged;
    }
}
