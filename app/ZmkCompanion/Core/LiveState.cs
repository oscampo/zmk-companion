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

    // Pomodoro state, updated by AppContext on each PomodoroFeature tick.
    public string PomodoroTime  { get; private set; } = "--:--";
    public string PomodoroPhase { get; private set; } = "";
    public string PomodoroBar   { get; private set; } = "";
    public string PomodoroIcon  { get; private set; } = "";
    public string PomodoroCycle { get; private set; } = "";

    // Formatted game data per league (keyed by SportsLeague.ShortName, upper-case),
    // refreshed periodically by AppContext from SportsFeature. "default" is the
    // first configured league, used by the bare {sports} / {sports.*} bindings.
    private readonly Dictionary<string, SportsSnapshot> _sportsData = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SportsSnapshot _emptySports = new();

    // Every Update* below only fires Changed when a value actually differs from
    // what's cached. Changed drives a full-frame BLE resend for every widget on
    // the active page — not just the one bound to this data — so firing it on
    // every poll regardless of whether anything moved (weather rarely changes
    // between 10-min polls, sports scores rarely change between 60s polls)
    // forces far more full-frame transmissions than the display can keep up
    // with, and the clock progressively falls behind while the pipeline is
    // busy re-sending unchanged data.

    public void UpdateBattery(int level, bool charging)
    {
        if (level == BatteryLevel && charging == BatteryCharging) return;
        BatteryLevel    = level;
        BatteryCharging = charging;
        Changed?.Invoke();
    }

    public void UpdateConnection(bool usb, int profile, int profileMask = 0b11111)
    {
        if (usb == UsbActive && profile == BleProfile && profileMask == BleProfileMask) return;
        UsbActive      = usb;
        BleProfile     = profile;
        BleProfileMask = profileMask;
        Changed?.Invoke();
    }

    public void UpdateWeather(string icon, string temp, string city)
    {
        if (icon == WeatherIcon && temp == WeatherTemp && city == WeatherCity) return;
        WeatherIcon = icon;
        WeatherTemp = temp;
        WeatherCity = city;
        Changed?.Invoke();
    }

    public void UpdateExternalText(string text)
    {
        if (text == ExternalText) return;
        ExternalText = text;
        Changed?.Invoke();
    }

    public void UpdatePomodoro(string time, string phase, string bar, string icon, string cycle)
    {
        if (time == PomodoroTime && phase == PomodoroPhase && bar == PomodoroBar
            && icon == PomodoroIcon && cycle == PomodoroCycle) return;
        PomodoroTime  = time;
        PomodoroPhase = phase;
        PomodoroBar   = bar;
        PomodoroIcon  = icon;
        PomodoroCycle = cycle;
        Changed?.Invoke();
    }

    public void UpdateSports(string leagueKey, SportsSnapshot snapshot)
    {
        if (_sportsData.TryGetValue(leagueKey, out var old) && old == snapshot) return;
        _sportsData[leagueKey] = snapshot;
        Changed?.Invoke();
    }

    private SportsSnapshot Sports(string leagueKey) =>
        _sportsData.TryGetValue(leagueKey, out var v) ? v : _emptySports;

    // Resolves a single binding key to its current display value (no glyph styling).
    public string Resolve(string key, bool use24h = false) => Resolve(key, use24h, null);

    // Resolves a single binding key, applying glyph styles from cfg when provided.
    public string Resolve(string key, bool use24h, LabelConfig? cfg)
    {
        bool h24 = use24h || !Protocol.Detect12h();

        // Battery icon — respects BatteryGlyphStyle
        if (key == "battery.icon")
        {
            string style = cfg?.BatteryGlyphStyle ?? "md_level";
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

        // Profile bar — 5 glyphs: active=plain, assigned=box/circle, free=outline.
        // NumericStyle on the row drives the variant: "circle*" → circles, else → boxes.
        if (key == "conn.profilebar")
        {
            bool circle = cfg?.NumericStyle is "circle" or "circle_outline";
            return BuildProfileBar(circle);
        }

        // Pomodoro bindings
        if (key.StartsWith("pomodoro.", StringComparison.OrdinalIgnoreCase))
        {
            return key["pomodoro.".Length..] switch
            {
                "time"  => PomodoroTime,
                "phase" => PomodoroPhase,
                "bar"   => PomodoroBar,
                "icon"  => PomodoroIcon,
                "cycle" => PomodoroCycle,
                _       => $"{{{key}}}",
            };
        }

        // Sports — {sports}, {sports:NFL}, {sports.team}, {sports.team:NFL}, etc.
        bool isSports = key.StartsWith("sports", StringComparison.OrdinalIgnoreCase);
        string raw = isSports ? ResolveSports(key) : key switch
        {
            "battery.level"   => BatteryLevel < 0 ? "--"  : $"{BatteryLevel}",
            "battery.percent" => BatteryLevel < 0 ? "--%": $"{BatteryLevel}%",
            "conn.type"       => UsbActive ? "USB" : "BLE",
            "conn.profile"    => UsbActive || BleProfile < 0 ? "?" : $"{BleProfile + 1}",
            "time"            => DateTime.Now.ToString(h24 ? "HH:mm" : "h:mm"),
            "time24"          => DateTime.Now.ToString("HH:mm"),
            "time12"          => DateTime.Now.ToString("h:mm"),
            "time.hh"         => DateTime.Now.ToString(h24 ? "HH" : "hh"),
            "time.mm"         => DateTime.Now.ToString("mm"),
            "ampm"            => h24 ? "" : DateTime.Now.ToString("tt"),
            "date"            => DateTime.Now.ToString("ddd d").ToUpper(),
            "date.day"        => DateTime.Now.Day.ToString(),
            "time.dd"         => DateTime.Now.ToString("dd"),
            "date.month"      => DateTime.Now.ToString("MMM").ToUpper(),
            "weather.icon"    => WeatherIcon,
            "weather.temp"    => WeatherTemp,
            "weather.city"    => WeatherCity,
            "weather"         => $"{WeatherCity} {WeatherIcon} {WeatherTemp}".Trim(),
            "ext.text"        => ExternalText,
            _ when key.StartsWith("ext.text.", StringComparison.OrdinalIgnoreCase)
                              => ExtTextLine(key["ext.text.".Length..]),
            _                 => $"{{{key}}}",
        };

        // Apply numeric/alpha glyph conversion for relevant bindings
        if (cfg != null)
        {
            bool numConvert   = cfg.NumericStyle != "text";
            bool alphaConvert = cfg.AlphaStyle   != "text";
            if (numConvert || alphaConvert)
            {
                bool applyAlpha = alphaConvert && (isSports || key is "date" or "date.month" or "weather" or "weather.city");
                bool applyNum   = numConvert   && (isSports || key is "time" or "time24" or "time12"
                                                        or "time.hh" or "time.mm" or "time.dd"
                                                        or "date" or "date.day" or "date.month"
                                                        or "conn.profile" or "weather" or "weather.temp");
                if (applyNum || applyAlpha)
                    raw = ApplyGlyphStyles(raw, applyNum ? cfg.NumericStyle : "text",
                                               applyAlpha ? cfg.AlphaStyle : "text");
            }
        }
        return raw;
    }

    // Builds the 5-glyph profile bar string.
    // active (current BLE profile) → plain digit glyph (nf-md-numeric_X)
    // assigned (bonded, not active) → box or circle glyph
    // free (not bonded)            → box_outline or circle_outline glyph
    // When USB is active there is no active BLE profile, only assigned/free states.
    private string BuildProfileBar(bool circle)
    {
        var sb = new StringBuilder();
        for (int p = 0; p < 5; p++)
        {
            bool assigned = (BleProfileMask & (1 << p)) != 0;
            bool active   = !UsbActive && BleProfile == p;

            string style = active   ? (circle ? "circle"         : "box")
                         : assigned ? (circle ? "circle_outline" : "box_outline")
                                    : "plain";

            sb.Append(NerdFont.NumericGlyph(p + 1, style) ?? (p + 1).ToString());
        }
        return sb.ToString();
    }

    // Parses "sports", "sports:NFL", "sports.team", "sports.team:NFL",
    // "sports.next_game", "sports.last_marker", etc.
    // Fields prefixed with "next_" or "last_" look up the _next / _last snapshot.
    private string ResolveSports(string key)
    {
        string rest      = key.Length > "sports".Length ? key["sports".Length..] : "";
        string field     = "summary";
        string leagueKey = "default";
        string suffix    = ""; // "", "_next", "_last"

        if (rest.StartsWith('.'))
        {
            int colon   = rest.IndexOf(':');
            string fp   = colon >= 0 ? rest[1..colon] : rest[1..];
            leagueKey   = colon >= 0 ? rest[(colon + 1)..] : "default";

            int under = fp.IndexOf('_');
            if (under > 0 && (fp.StartsWith("next_") || fp.StartsWith("last_")))
            {
                suffix = "_" + fp[..under];   // "_next" or "_last"
                field  = fp[(under + 1)..];   // e.g. "game", "marker", "date", "gametime"
            }
            else
            {
                field = fp;
            }
        }
        else if (rest.StartsWith(':'))
        {
            leagueKey = rest[1..];
        }

        var s = Sports(leagueKey + suffix);

        // For _next/_last snapshots, "game" shows only teams and "marker" includes
        // the score ("38-35 🏆") so each piece fits in an 11-col tier.
        if (suffix is "_next" or "_last")
        {
            return field switch
            {
                "game"      => $"{s.Away} {s.Home}".Trim(),
                "marker"    => suffix == "_last"
                                   ? $"{s.Score} {s.Marker}".Trim()
                                   : s.Marker,
                "score"     => s.Score,
                "away"      => s.Away,
                "home"      => s.Home,
                "sport"     => s.Sport,
                "league"    => s.League,
                "team"      => s.Team,
                "time"      => s.Time,
                "scheduled" => s.Scheduled,
                "date"      => SplitScheduled(s.Scheduled, 0),
                "gametime"  => SplitScheduled(s.Scheduled, 1),
                _           => s.Summary,
            };
        }

        return field switch
        {
            "sport"     => s.Sport,
            "league"    => s.League,
            "team"      => s.Team,
            "game"      => s.Game,
            "marker"    => s.Marker,
            "time"      => s.Time,
            "scheduled" => s.Scheduled,
            "date"      => SplitScheduled(s.Scheduled, 0), // "7/10"
            "gametime"  => SplitScheduled(s.Scheduled, 1), // "7:30p"
            _           => s.Summary,
        };
    }

    // Returns line i (0-indexed) of ExternalText split by '\n', or "" if out of range.
    private string ExtTextLine(string indexStr)
    {
        if (!int.TryParse(indexStr, out int i)) return "";
        var lines = ExternalText.Split('\n');
        return i < lines.Length ? lines[i].TrimEnd('\r') : "";
    }

    private static string SplitScheduled(string detail, int part)
    {
        int idx = detail.IndexOf(" - ");
        if (idx < 0) return part == 0 ? detail : "";
        return part == 0 ? detail[..idx].Trim() : detail[(idx + 3)..].Trim();
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
