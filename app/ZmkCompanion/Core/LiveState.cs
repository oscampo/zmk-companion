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
    public int  BleProfileMask  { get; private set; } = 0b11111;  // bits 0-4: profiles 1-5 bonded

    // Weather snapshot, refreshed periodically by AppContext from WeatherFeature.
    public string WeatherIcon { get; private set; } = "";
    public string WeatherTemp { get; private set; } = "--°";
    public string WeatherCity { get; private set; } = "";

    // Last text pushed by an external process via the named pipe (zkc CLI).
    public string ExternalText { get; private set; } = "";

    // Formatted game text per league (keyed by SportsLeague.ShortName, upper-case),
    // refreshed periodically by AppContext from SportsFeature. "default" is the
    // first configured league, used by the bare {sports} binding.
    private readonly Dictionary<string, string> _sportsText = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateBattery(int level, bool charging)
    {
        BatteryLevel    = level;
        BatteryCharging = charging;
        Changed?.Invoke();
    }

    public void UpdateConnection(bool usb, int profile, int profileMask = 0b11111)
    {
        UsbActive      = usb;
        BleProfile     = profile;
        BleProfileMask = profileMask;
        Changed?.Invoke();
    }

    public void UpdateWeather(string icon, string temp, string city)
    {
        WeatherIcon = icon;
        WeatherTemp = temp;
        WeatherCity = city;
        Changed?.Invoke();
    }

    public void UpdateExternalText(string text)
    {
        ExternalText = text;
        Changed?.Invoke();
    }

    public void UpdateSports(string leagueKey, string text)
    {
        _sportsText[leagueKey] = text;
        Changed?.Invoke();
    }

    private string SportsText(string leagueKey) =>
        _sportsText.TryGetValue(leagueKey, out var v) ? v : "···";

    // Resolves a single binding key to its current display value (no glyph styling).
    public string Resolve(string key, bool use24h = false) => Resolve(key, use24h, null);

    // Resolves a single binding key, applying glyph styles from cfg when provided.
    public string Resolve(string key, bool use24h, LabelConfig? cfg)
    {
        bool h24 = use24h || !Protocol.Detect12h();

        // Battery icon — respects BatteryGlyphStyle
        if (key == "battery.icon")
        {
            string style = cfg?.BatteryGlyphStyle ?? "nf";
            return style switch
            {
                "md_level"     => NerdFont.MdBatteryLevelGlyph(BatteryLevel, BatteryCharging),
                "md_threshold" => NerdFont.MdBatteryThresholdGlyph(BatteryLevel, BatteryCharging),
                _              => NerdFont.BatteryGlyph(BatteryLevel, BatteryCharging),
            };
        }

        // Connection icon — respects ConnBleGlyph / ConnUsbGlyph
        if (key == "conn.icon")
        {
            string bleG = cfg?.ConnBleGlyph is { Length: > 0 } b ? b : NerdFont.Bluetooth;
            string usbG = cfg?.ConnUsbGlyph is { Length: > 0 } u ? u : NerdFont.Usb;
            return UsbActive ? usbG : bleG;
        }

        // Sports — {sports} uses the first configured league; {sports:NFL} picks one by ShortName.
        if (key.StartsWith("sports", StringComparison.OrdinalIgnoreCase))
        {
            int colon = key.IndexOf(':');
            return SportsText(colon >= 0 ? key[(colon + 1)..] : "default");
        }

        string raw = key switch
        {
            "battery.level"   => BatteryLevel < 0 ? "--"  : $"{BatteryLevel}",
            "battery.percent" => BatteryLevel < 0 ? "--%": $"{BatteryLevel}%",
            "conn.type"       => UsbActive ? "USB" : "BLE",
            "conn.profile"    => UsbActive || BleProfile < 0 ? "?" : $"{BleProfile + 1}",
            "time"            => DateTime.Now.ToString(h24 ? "HH:mm" : "h:mm"),
            "time24"          => DateTime.Now.ToString("HH:mm"),
            "time12"          => DateTime.Now.ToString("h:mm"),
            "ampm"            => h24 ? "" : DateTime.Now.ToString("tt"),
            "date"            => DateTime.Now.ToString("ddd d").ToUpper(),
            "date.day"        => DateTime.Now.Day.ToString(),
            "date.month"      => DateTime.Now.ToString("MMM").ToUpper(),
            "weather.icon"    => WeatherIcon,
            "weather.temp"    => WeatherTemp,
            "weather.city"    => WeatherCity,
            "weather"         => $"{WeatherIcon} {WeatherTemp}".Trim(),
            "ext.text"        => ExternalText,
            _                 => $"{{{key}}}",
        };

        // Apply numeric/alpha glyph conversion for relevant bindings
        if (cfg != null)
        {
            bool numConvert = cfg.NumericStyle   != "text";
            bool alphaConvert = cfg.AlphaStyle   != "text";
            if (numConvert || alphaConvert)
            {
                bool applyAlpha = alphaConvert && key is "date" or "date.month";
                bool applyNum   = numConvert   && key is "time" or "time24" or "time12"
                                                        or "date" or "date.day" or "date.month"
                                                        or "conn.profile";
                if (applyNum || applyAlpha)
                    raw = ApplyGlyphStyles(raw, applyNum ? cfg.NumericStyle : "text",
                                               applyAlpha ? cfg.AlphaStyle : "text");
            }
        }
        return raw;
    }

    // Converts digits and/or letters in a string to MD Nerd Font glyphs.
    private static string ApplyGlyphStyles(string value, string numStyle, string alphaStyle)
    {
        var sb = new StringBuilder(value.Length * 2);
        foreach (char c in value)
        {
            if (numStyle != "text" && char.IsAsciiDigit(c))
            {
                string? g = NerdFont.NumericGlyph(c - '0', numStyle);
                sb.Append(g ?? c.ToString());
            }
            else if (alphaStyle != "text" && char.IsAsciiLetter(c))
            {
                string? g = NerdFont.AlphaGlyph(c, alphaStyle);
                sb.Append(g ?? c.ToString());
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // Expands all {binding} tokens in a template string (no glyph styling).
    public string Expand(string template, bool use24h = false) => Expand(template, use24h, null);

    // Expands all {binding} tokens, applying glyph styles from cfg when provided.
    public string Expand(string template, bool use24h, LabelConfig? cfg)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains('{'))
            return template;

        var sb = new StringBuilder(template.Length + 16);
        int i  = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0) { sb.Append(template[i..]); break; }
            sb.Append(template[i..open]);
            int close = template.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(template[open..]); break; }
            sb.Append(Resolve(template[(open + 1)..close], use24h, cfg));
            i = close + 1;
        }
        return sb.ToString();
    }

    // True if template contains time or date bindings (drives clock timer in LabelWidget).
    public static bool HasTimeBind(string template) =>
        template.Contains("{time") || template.Contains("{date") || template.Contains("{ampm}");
}
