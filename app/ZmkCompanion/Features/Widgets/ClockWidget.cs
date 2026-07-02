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
        _timer = new System.Windows.Forms.Timer();
        _timer.Tick += OnTick;
        ScheduleNextTick(DateTime.Now);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var tickTime = DateTime.Now;
        _timer!.Stop();
        Invalidated?.Invoke();
        ScheduleNextTick(tickTime);
        _timer.Start();
    }

    // Recomputed from wall clock on every tick so any single tick's jitter
    // never accumulates into a permanently drifted display.
    private void ScheduleNextTick(DateTime tickTime)
    {
        int ms = (60 - tickTime.Second) * 1000 - tickTime.Millisecond;
        _timer!.Interval = Math.Max(250, ms);
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

        string timeStr = is12h ? now.ToString("h:mm") : now.ToString("HH:mm");
        string ampm    = is12h && cfg.ShowAmPm ? now.ToString("tt") : "";
        string dateStr = cfg.ShowDate ? now.ToString("ddd dd").ToUpper() : "";

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        float scale = Math.Clamp(cfg.Scale, 0.4f, 2.0f);
        using var timeFont = new Font("Consolas", 20f * scale, FontStyle.Bold,    GraphicsUnit.Pixel);
        using var ampmFont = new Font("Consolas", 13f * scale, FontStyle.Regular,  GraphicsUnit.Pixel);
        using var dateFont = new Font("Consolas", 10f * scale, FontStyle.Regular,  GraphicsUnit.Pixel);

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
