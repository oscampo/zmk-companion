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
        {
            DebugLog.Log($"LabelWidget.Start (clock) template='{Config.Template}' now={DateTime.Now:HH:mm:ss.fff}");
            StartClockTimer();
        }
    }

    private void StartClockTimer()
    {
        int msUntilNext = (60 - DateTime.Now.Second) * 1000 - DateTime.Now.Millisecond;
        DebugLog.Log($"StartClockTimer: msUntilNext={msUntilNext}");
        _clockTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, msUntilNext) };
        _clockTimer.Tick += (_, _) =>
        {
            DebugLog.Log($"clockTimer TICK now={DateTime.Now:HH:mm:ss.fff}");
            _clockTimer!.Interval = 60_000;
            Invalidated?.Invoke();
        };
        _clockTimer.Start();
    }

    public void Stop()
    {
        if (_clockTimer != null) DebugLog.Log($"LabelWidget.Stop (clock timer disposed) now={DateTime.Now:HH:mm:ss.fff}");
        _clockTimer?.Stop(); _clockTimer?.Dispose(); _clockTimer = null;
    }

    public void Render(Graphics g)
    {
        var    cfg  = Config;
        string text = _state.Expand(cfg.Template, cfg.Use24h, cfg);
        if (LiveState.HasTimeBind(cfg.Template))
            DebugLog.Log($"LabelWidget.Render clock template='{cfg.Template}' resolved='{text.Replace('\n', '|')}' now={DateTime.Now:HH:mm:ss.fff}");
        if (text.Length == 0) return;

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var style = cfg.Bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = cfg.UseNerdFont
            ? NerdFont.CreateFont(cfg.Size, style)
            : new Font("Consolas", cfg.Size, style, GraphicsUnit.Pixel);

        float cy = Bounds.Y + Bounds.Height / 2f;

        if (cfg.LetterSpacing != 0f)
        {
            RenderSpaced(g, font, text, cfg, cy);
            return;
        }

        SizeF sz = g.MeasureString(text, font);
        float y   = cy - sz.Height / 2f;
        float x = cfg.Align switch
        {
            "left"  => Bounds.X,
            "right" => Bounds.Right - sz.Width,
            _       => Bounds.X + Bounds.Width / 2f - sz.Width / 2f,
        };
        g.DrawString(text, font, Brushes.White, x, y);
    }

    private void RenderSpaced(Graphics g, Font font, string text, LabelConfig cfg, float cy)
    {
        var sf = StringFormat.GenericTypographic;
        var elems = SplitElements(text);
        float spacing = cfg.LetterSpacing;

        float totalW = 0f;
        var widths = new float[elems.Length];
        float height = 0f;
        for (int i = 0; i < elems.Length; i++)
        {
            SizeF sz  = g.MeasureString(elems[i], font, PointF.Empty, sf);
            widths[i] = sz.Width;
            totalW   += sz.Width;
            if (sz.Height > height) height = sz.Height;
        }
        totalW += spacing * Math.Max(0, elems.Length - 1);

        float y = cy - height / 2f;
        float x = cfg.Align switch
        {
            "left"  => Bounds.X,
            "right" => Bounds.Right - totalW,
            _       => Bounds.X + Bounds.Width / 2f - totalW / 2f,
        };

        for (int i = 0; i < elems.Length; i++)
        {
            g.DrawString(elems[i], font, Brushes.White, x, y, sf);
            x += widths[i] + spacing;
        }
    }

    // Splits a string into single-char or surrogate-pair elements.
    private static string[] SplitElements(string s)
    {
        var list = new System.Collections.Generic.List<string>(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
            { list.Add(s.Substring(i, 2)); i += 2; }
            else
            { list.Add(s.Substring(i, 1)); i++; }
        }
        return list.ToArray();
    }

    public void Dispose()
    {
        Stop();
        _state.Changed -= OnStateChanged;
    }
}
