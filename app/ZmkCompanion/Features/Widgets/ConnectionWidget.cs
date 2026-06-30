using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class ConnectionWidget : IWidget
{
    public Rectangle Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

    private bool _usbActive  = false;
    private int  _bleProfile = -1;   // -1 = unknown, 0-4 = profile index

    private System.Windows.Forms.Timer? _timer;

    // Called by BleService when status characteristic 0x1526 updates.
    internal void Update(bool usbActive, int bleProfile)
    {
        _usbActive  = usbActive;
        _bleProfile = bleProfile;
        Invalidated?.Invoke();
    }

    public void Start()
    {
        Stop();
        Invalidated?.Invoke();
        _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _timer.Tick += (_, _) => Invalidated?.Invoke();
        _timer.Start();
    }

    public void Stop() { _timer?.Stop(); _timer?.Dispose(); _timer = null; }

    public void Render(Graphics g)
    {
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // Line 1: connection type icon + label
        // Line 2: BLE profile slot (1-5)
        string typeLine    = _usbActive ? "USB" : "BLE";
        string profileLine = _bleProfile >= 0 ? $"Profile {_bleProfile + 1}" : "";

        using var mainFont  = new Font("Consolas", 14f, FontStyle.Bold,    GraphicsUnit.Pixel);
        using var smallFont = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);

        SizeF mainSz  = g.MeasureString(typeLine,    mainFont);
        SizeF smallSz = g.MeasureString(profileLine, smallFont);

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;
        float totalH = mainSz.Height + (profileLine.Length > 0 ? 2f + smallSz.Height : 0f);

        float mainY = cy - totalH / 2f;
        g.DrawString(typeLine, mainFont, Brushes.White, cx - mainSz.Width / 2f, mainY);

        if (profileLine.Length > 0)
            g.DrawString(profileLine, smallFont, Brushes.White,
                cx - smallSz.Width / 2f, mainY + mainSz.Height + 2f);
    }

    public void Dispose() => Stop();
}
