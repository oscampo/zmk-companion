using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class BatteryWidget : IWidget
{
    public Rectangle Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

    public BatteryConfig Config { get; set; } = new();

    private int  _level    = -1;
    private bool _charging = false;

    private System.Windows.Forms.Timer? _timer;

    internal void Update(int level, bool charging)
    {
        _level    = level;
        _charging = charging;
        Invalidated?.Invoke();
    }

    public void Start()
    {
        Stop();
        Invalidated?.Invoke();
        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += (_, _) => Invalidated?.Invoke();
        _timer.Start();
    }

    public void Stop() { _timer?.Stop(); _timer?.Dispose(); _timer = null; }

    public void Render(Graphics g)
    {
        var  cfg      = Config;
        bool showIcon = cfg.ShowIcon;
        bool showPct  = cfg.ShowPercent;
        if (!showIcon && !showPct) return;

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        float cx    = Bounds.X + Bounds.Width  / 2f;
        float cy    = Bounds.Y + Bounds.Height / 2f;
        float scale = Math.Clamp(cfg.Scale, 0.4f, 2.0f);

        if (showIcon && showPct)
        {
            string icon  = NerdFont.BatteryGlyph(_level, _charging);
            string label = _level < 0 ? "--" : $"{_level}%";

            using var iconFont  = NerdFont.CreateFont(28f * scale);
            using var labelFont = new Font("Consolas", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            SizeF iconSz  = g.MeasureString(icon,  iconFont);
            SizeF labelSz = g.MeasureString(label, labelFont);
            float totalH  = iconSz.Height + 2f + labelSz.Height;
            float iconY   = cy - totalH / 2f;

            g.DrawString(icon,  iconFont,  Brushes.White, cx - iconSz.Width  / 2f, iconY);
            g.DrawString(label, labelFont, Brushes.White, cx - labelSz.Width / 2f, iconY + iconSz.Height + 2f);
        }
        else if (showIcon)
        {
            string icon = NerdFont.BatteryGlyph(_level, _charging);
            using var iconFont = NerdFont.CreateFont(28f * scale);
            SizeF iconSz = g.MeasureString(icon, iconFont);
            g.DrawString(icon, iconFont, Brushes.White, cx - iconSz.Width / 2f, cy - iconSz.Height / 2f);
        }
        else
        {
            string label = _level < 0 ? "--" : $"{_level}%";
            using var labelFont = new Font("Consolas", 14f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF labelSz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, Brushes.White, cx - labelSz.Width / 2f, cy - labelSz.Height / 2f);
        }
    }

    public void Dispose() => Stop();
}
