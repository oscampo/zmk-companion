using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class ClockWidget : IWidget
{
    public Rectangle Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

    public ClockConfig Config { get; set; } = new();

    private System.Windows.Forms.Timer? _timer;

    public void Start()
    {
        Stop();
        Invalidated?.Invoke();
        // First tick fires at the next minute boundary; subsequent ticks every 60s.
        int msUntilNextMinute = (60 - DateTime.Now.Second) * 1000 - DateTime.Now.Millisecond;
        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, msUntilNextMinute) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer!.Interval = 60_000;
        Invalidated?.Invoke();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
    }

    public void Render(Graphics g)
    {
        var cfg    = Config;
        bool is12h = !cfg.Use24h && Protocol.Detect12h();
        var  now   = DateTime.Now;

        string timeStr = cfg.ShowSeconds
            ? (is12h ? now.ToString("h:mm:ss") : now.ToString("HH:mm:ss"))
            : (is12h ? now.ToString("h:mm")    : now.ToString("HH:mm"));
        string ampm    = is12h && cfg.ShowAmPm ? now.ToString("tt") : "";
        string dateStr = cfg.ShowDate ? now.ToString("ddd dd").ToUpper() : "";

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        using var timeFont = new Font("Consolas", 20f, FontStyle.Bold,    GraphicsUnit.Pixel);
        using var ampmFont = new Font("Consolas", 13f, FontStyle.Regular,  GraphicsUnit.Pixel);
        using var dateFont = new Font("Consolas", 10f, FontStyle.Regular,  GraphicsUnit.Pixel);

        SizeF timeSz = g.MeasureString(timeStr, timeFont);
        SizeF ampmSz = ampm.Length > 0 ? g.MeasureString(ampm, ampmFont) : SizeF.Empty;
        SizeF dateSz = dateStr.Length > 0 ? g.MeasureString(dateStr, dateFont) : SizeF.Empty;

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;

        // Total block height: time row + optional date row.
        float blockH = timeSz.Height + (dateSz.Height > 0 ? 3f + dateSz.Height : 0f);
        float timeY  = cy - blockH / 2f;

        // Time + inline AM/PM: center the combined width.
        float rowW  = timeSz.Width + (ampmSz.Width > 0 ? 2f + ampmSz.Width : 0f);
        float timeX = cx - rowW / 2f;
        g.DrawString(timeStr, timeFont, Brushes.White, timeX, timeY);

        if (ampm.Length > 0)
        {
            // Baseline-align AM/PM with the time string.
            float ampmY = timeY + timeSz.Height - ampmSz.Height;
            g.DrawString(ampm, ampmFont, Brushes.White, timeX + timeSz.Width + 2f, ampmY);
        }

        if (dateStr.Length > 0)
        {
            g.DrawString(dateStr, dateFont, Brushes.White,
                cx - dateSz.Width / 2f,
                timeY + timeSz.Height + 3f);
        }
    }

    public void Dispose() => Stop();
}
