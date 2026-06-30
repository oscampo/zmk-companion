using System.Drawing;
using System.Drawing.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features.Widgets;

sealed class ConnectionWidget : IWidget
{
    public Rectangle Bounds { get; set; } = new(0, 0, BitmapFrame.Width, BitmapFrame.Height);

    public event Action? Invalidated;

    public ConnectionConfig Config { get; set; } = new();

    private bool _usbActive  = false;
    private int  _bleProfile = -1;   // -1 = unknown, 0-4 = profile index

    private System.Windows.Forms.Timer? _timer;

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
        var  cfg         = Config;
        bool showIcon    = cfg.ShowIcon;
        bool showType    = cfg.ShowType;
        bool showProfile = cfg.ShowProfile && !_usbActive;  // profile only makes sense for BLE

        if (!showIcon && !showType && !showProfile) return;

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        float cx = Bounds.X + Bounds.Width  / 2f;
        float cy = Bounds.Y + Bounds.Height / 2f;

        string icon = _usbActive ? NerdFont.Usb : NerdFont.Bluetooth;

        // Build the text label from enabled parts.
        string label = BuildLabel(showType, showProfile);

        using var iconFont  = NerdFont.CreateFont(28f);
        using var labelFont = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel);

        if (showIcon && label.Length > 0)
        {
            SizeF iconSz  = g.MeasureString(icon,  iconFont);
            SizeF labelSz = g.MeasureString(label, labelFont);
            float totalH  = iconSz.Height + 2f + labelSz.Height;
            float iconY   = cy - totalH / 2f;

            g.DrawString(icon,  iconFont,  Brushes.White, cx - iconSz.Width  / 2f, iconY);
            g.DrawString(label, labelFont, Brushes.White, cx - labelSz.Width / 2f, iconY + iconSz.Height + 2f);
        }
        else if (showIcon)
        {
            SizeF iconSz = g.MeasureString(icon, iconFont);
            g.DrawString(icon, iconFont, Brushes.White, cx - iconSz.Width / 2f, cy - iconSz.Height / 2f);
        }
        else
        {
            SizeF labelSz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, Brushes.White, cx - labelSz.Width / 2f, cy - labelSz.Height / 2f);
        }
    }

    private string BuildLabel(bool showType, bool showProfile)
    {
        if (_usbActive)
            return showType ? "USB" : "";

        // BLE
        string type    = showType    ? "BLE" : "";
        string profile = showProfile ? (_bleProfile >= 0 ? $"{_bleProfile + 1}" : "?") : "";

        if (type.Length > 0 && profile.Length > 0) return $"{type} {profile}";
        return type.Length > 0 ? type : profile;
    }

    public void Dispose() => Stop();
}
