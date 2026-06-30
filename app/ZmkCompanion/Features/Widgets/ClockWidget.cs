using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class ClockWidget : IWidget
{
    public Rectangle Bounds { get; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

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
        bool is12h = Protocol.Detect12h();
        var now = DateTime.Now;

        string timeStr = is12h ? now.ToString("h:mm")  : now.ToString("HH:mm");
        string ampm    = is12h ? now.ToString("tt")     : "";
        string dateStr = now.ToString("ddd dd").ToUpper();

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        using var timeFont  = new Font("Consolas", 20f, FontStyle.Bold,    GraphicsUnit.Pixel);
        using var smallFont = new Font("Consolas", 10f, FontStyle.Regular,  GraphicsUnit.Pixel);

        SizeF timeSz = g.MeasureString(timeStr, timeFont);
        SizeF dateSz = g.MeasureString(dateStr, smallFont);

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;

        float timeY = cy - timeSz.Height / 2f - dateSz.Height / 2f - 2f;
        float timeX = cx - timeSz.Width  / 2f;
        g.DrawString(timeStr, timeFont, Brushes.White, timeX, timeY);

        if (is12h && ampm.Length > 0)
        {
            SizeF ampmSz = g.MeasureString(ampm, smallFont);
            g.DrawString(ampm, smallFont, Brushes.White,
                timeX + timeSz.Width + 1f,
                timeY + timeSz.Height - ampmSz.Height - 1f);
        }

        g.DrawString(dateStr, smallFont, Brushes.White,
            cx - dateSz.Width / 2f,
            timeY + timeSz.Height + 3f);
    }

    public void Dispose() => Stop();
}
