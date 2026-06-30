using System.Text;

namespace ZmkCompanion.Core;

// Shared live data (battery, connection). Widgets subscribe to Changed
// and call Expand() at render time to get current values.
// All access is on the UI thread.
sealed class LiveState
{
    public event Action? Changed;

    public int  BatteryLevel    { get; private set; } = -1;
    public bool BatteryCharging { get; private set; }
    public bool UsbActive       { get; private set; }
    public int  BleProfile      { get; private set; } = -1;  // 0-4

    public void UpdateBattery(int level, bool charging)
    {
        BatteryLevel    = level;
        BatteryCharging = charging;
        Changed?.Invoke();
    }

    public void UpdateConnection(bool usb, int profile)
    {
        UsbActive  = usb;
        BleProfile = profile;
        Changed?.Invoke();
    }

    // Resolves a single binding key to its current display value.
    public string Resolve(string key, bool use24h = false)
    {
        bool h24 = use24h || !Protocol.Detect12h();
        return key switch
        {
            "battery.icon"    => NerdFont.BatteryGlyph(BatteryLevel, BatteryCharging),
            "battery.level"   => BatteryLevel < 0 ? "--"  : $"{BatteryLevel}",
            "battery.percent" => BatteryLevel < 0 ? "--%": $"{BatteryLevel}%",
            "conn.icon"       => UsbActive ? NerdFont.Usb : NerdFont.Bluetooth,
            "conn.type"       => UsbActive ? "USB" : "BLE",
            "conn.profile"    => UsbActive || BleProfile < 0 ? "?" : $"{BleProfile + 1}",
            "time"            => DateTime.Now.ToString(h24 ? "HH:mm" : "h:mm"),
            "time24"          => DateTime.Now.ToString("HH:mm"),
            "time12"          => DateTime.Now.ToString("h:mm"),
            "ampm"            => h24 ? "" : DateTime.Now.ToString("tt"),
            "date"            => DateTime.Now.ToString("ddd d").ToUpper(),
            "date.day"        => DateTime.Now.Day.ToString(),
            "date.month"      => DateTime.Now.ToString("MMM").ToUpper(),
            _                 => $"{{{key}}}",
        };
    }

    // Expands all {binding} tokens in a template string.
    public string Expand(string template, bool use24h = false)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains('{'))
            return template;

        var sb    = new StringBuilder(template.Length + 16);
        int i     = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0) { sb.Append(template[i..]); break; }
            sb.Append(template[i..open]);
            int close = template.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(template[open..]); break; }
            sb.Append(Resolve(template[(open + 1)..close], use24h));
            i = close + 1;
        }
        return sb.ToString();
    }

    // True if template contains time or date bindings (drives clock timer in LabelWidget).
    public static bool HasTimeBind(string template) =>
        template.Contains("{time") || template.Contains("{date") || template.Contains("{ampm}");
}
