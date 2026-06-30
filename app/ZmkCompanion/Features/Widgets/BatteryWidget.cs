using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class BatteryWidget : IWidget
{
    public Rectangle Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

    private int  _level    = -1;   // -1 = unknown
    private bool _charging = false;

    private System.Windows.Forms.Timer? _timer;

    // Called by BleService (or mock) when status characteristic updates.
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
        // Refresh every 60 s in case notifications are missed.
        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += (_, _) => Invalidated?.Invoke();
        _timer.Start();
    }

    public void Stop() { _timer?.Stop(); _timer?.Dispose(); _timer = null; }

    public void Render(Graphics g)
    {
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        string icon  = BatteryIcon(_level, _charging);
        string label = _level < 0 ? "??" : $"{_level}%";

        using var iconFont  = new Font("Consolas", 24f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelFont = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel);

        SizeF iconSz  = g.MeasureString(icon,  iconFont);
        SizeF labelSz = g.MeasureString(label, labelFont);

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;
        float totalH = iconSz.Height + 2f + labelSz.Height;

        float iconY  = cy - totalH / 2f;
        float labelY = iconY + iconSz.Height + 2f;

        g.DrawString(icon,  iconFont,  Brushes.White, cx - iconSz.Width  / 2f, iconY);
        g.DrawString(label, labelFont, Brushes.White, cx - labelSz.Width / 2f, labelY);
    }

    private static string BatteryIcon(int level, bool charging)
    {
        if (charging) return "+";           // placeholder until Nerd Font embedded
        return level switch
        {
            < 0        => "?",
            < 20       => "[ ]",
            < 40       => "[. ]",
            < 60       => "[..]",
            < 80       => "[==]",
            _          => "[==]",
        };
    }

    public void Dispose() => Stop();
}
