using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// Renders the current time as a bitmap and sends it via the 0x1525 characteristic.
// Fires once at start then every 30 s so the displayed minute stays accurate.
sealed class ClockFeature : IDisposable
{
    private readonly BleService _ble;
    private System.Windows.Forms.Timer? _timer;
    private System.Windows.Forms.Timer? _pauseTimer;

    public ClockFeature(BleService ble) => _ble = ble;

    public void Start()
    {
        Stop();
        _ = SendFrameAsync();

        _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _timer.Tick += async (_, _) => await SendFrameAsync();
        _timer.Start();
    }

    // Pauses the clock for `duration`, then restarts it.
    // Called by AppContext when CLI pipe text displaces the clock.
    public void PauseFor(TimeSpan duration)
    {
        Stop();
        _pauseTimer = new System.Windows.Forms.Timer { Interval = (int)duration.TotalMilliseconds };
        _pauseTimer.Tick += (_, _) =>
        {
            _pauseTimer!.Stop(); _pauseTimer.Dispose(); _pauseTimer = null;
            Start();
        };
        _pauseTimer.Start();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _pauseTimer?.Stop(); _pauseTimer?.Dispose(); _pauseTimer = null;
    }

    public event Action<string>? SendFailed;

    private async Task SendFrameAsync()
    {
        using var bmp = RenderClock();
        bool ok = await _ble.SendBitmapAsync(BitmapFrame.Pack(bmp));
        if (!ok) SendFailed?.Invoke("Bitmap write failed — check characteristic write type");
    }

    private static Bitmap RenderClock()
    {
        bool is12h = Protocol.Detect12h();
        var now = DateTime.Now;

        string timeStr = is12h ? now.ToString("h:mm")  : now.ToString("HH:mm");
        string ampm    = is12h ? now.ToString("tt")     : "";
        string dateStr = now.ToString("ddd dd").ToUpper();

        var bmp = BitmapFrame.CreateCanvas();
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        using var timeFont = new Font("Consolas", 20f, FontStyle.Bold,    GraphicsUnit.Pixel);
        using var smallFont = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);

        SizeF timeSz = g.MeasureString(timeStr, timeFont);
        SizeF dateSz = g.MeasureString(dateStr, smallFont);

        float cx = BitmapFrame.Width  / 2f;
        float cy = BitmapFrame.Height / 2f;

        // Time block: time + optional AM/PM — centered slightly above middle
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

        // Date below the time
        g.DrawString(dateStr, smallFont, Brushes.White,
            cx - dateSz.Width / 2f,
            timeY + timeSz.Height + 3f);

        return bmp;
    }

    public void Dispose() => Stop();
}
