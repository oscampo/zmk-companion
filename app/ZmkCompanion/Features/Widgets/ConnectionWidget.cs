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

    // Called by AppContext when BleService fires StatusChanged (0x1526).
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

        string icon  = _usbActive ? NerdFont.Usb : NerdFont.Bluetooth;
        string label = _usbActive ? "USB" : (_bleProfile >= 0 ? $"BLE {_bleProfile + 1}" : "BLE");

        using var iconFont  = NerdFont.CreateFont(28f);
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

    public void Dispose() => Stop();
}
