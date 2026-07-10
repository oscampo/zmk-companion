using System.Text.Json;
using System.Text.Json.Serialization;
using ZmkCompanion.Core;

namespace ZmkCompanion;

sealed class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZmkCompanion",
        "settings.json");

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Cell-grid display pages — primary display model (0x1527).
    // Each page is a sequence of rows (tier + template) rendered glyph-by-glyph.
    public List<CellGridPage> DisplayPages      { get; set; } = [DefaultDisplayPage()];
    public bool               CycleDisplayPages { get; set; } = false;

    // Pomodoro timer configuration.
    public int PomodoroWorkMin      { get; set; } = 25;
    public int PomodoroBreakMin     { get; set; } = 5;
    public int PomodoroCycles       { get; set; } = 4;
    public int PomodoroLongBreakMin { get; set; } = 15;

    // User-pickable phase icons (Nerd Font glyphs). Default to the built-in
    // Font Awesome icons so existing settings.json files keep their look.
    public string PomodoroWorkIcon  { get; set; } = Features.PomodoroFeature.IconWork.ToString();
    public string PomodoroBreakIcon { get; set; } = Features.PomodoroFeature.IconBreak.ToString();
    public string PomodoroLongIcon  { get; set; } = Features.PomodoroFeature.IconLong.ToString();

    // Remembered CLI tab command line (CellGridEditorForm's "Launch zkc"
    // button). Empty = fall back to the default "zkc -h" terminal.
    public string CliLastCommand { get; set; } = "";

    // Auto-start entries (CellGridEditorForm's "Inicio automático" tab):
    // scripts the user wants relaunched every login (a daily-phrase sender,
    // a sensor monitor piped into zkc --set, etc). This is only the source
    // data, AutoStartManager.Apply projects it into a single regenerated
    // .bat in the Windows Startup folder, on explicit user action, never
    // written silently.
    public List<AutoStartEntry> AutoStartEntries { get; set; } = [];

    // WelcomeForm: the app <Version> (ZmkCompanion.csproj) for which the user
    // last checked "don't show again". Compared against the running build's
    // version on startup — different (including "never dismissed", "") means
    // show it again, so bumping <Version> on a release re-surfaces it to
    // announce whatever changed. This only works if releases actually bump
    // that version; nothing enforces it today.
    public string WelcomeDismissedVersion { get; set; } = "";

    // Weather data source — city names for API queries, up to 4 (see
    // CellGridEditorForm's Weather tab, which enforces the cap). Empty list =
    // single IP-geolocated city (blank = auto-detect, same as before this was
    // a list). First entry backs the bare {weather}/{weather.*} bindings;
    // any additional ones need the ":<city>" suffix, e.g. {weather.temp:Madrid}.
    // Temperature unit: "celsius" or "fahrenheit".
    public List<string> WeatherCities { get; set; } = new();
    public string WeatherUnit { get; set; } = "celsius";
    // Selected leagues as ESPN paths, e.g. ["football/nfl", "soccer/eng.1"]
    public List<string> SelectedLeagues { get; set; } = ["football/nfl"];
    // Per-league team abbreviation filter keyed by ESPN path, e.g.
    // {"football/nfl": "KC", "soccer/fifa.cwc": "COL"}
    public Dictionary<string, string> SportsTeams { get; set; } = new();

    // Selected foreign time zones as IANA ids, e.g. ["America/New_York", "Asia/Tokyo"].
    // Powers the {time:ID}/{date:ID}/etc. binding-picker category (Time Zone tab).
    public List<string> SelectedTimeZones { get; set; } = new();

    // User-declared {custom.NAME} tokens (name + picker category only, no
    // value, values are runtime-only, pushed via `zkc --set`). Managed from
    // the tray's "Tokens personalizados…" menu item.
    public List<CustomTokenDef> CustomTokens { get; set; } = new();

    // UI language, "es" or "en". Changed anytime from the tray's "Idioma" menu.
    public string Language { get; set; } = "es";

    // Legacy fields — not written after migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CanvasPage>? Pages { get; set; }
    public bool CyclePages { get; set; } = false;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetPlacement>? Canvas { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NflTeam { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SportEspnPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SportsTeam { get; set; } // migrated → SportsTeams
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? City { get; set; } // migrated → WeatherCities

    private static CellGridPage DefaultDisplayPage() => new()
    {
        Name = "Reloj",
        Rows =
        [
            new CellGridRow { TierId = 4, Template = "{time}",  Align = "center" },
            new CellGridRow { TierId = 0, Template = "{date}",  Align = "center" },
        ],
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var s = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
                // Migrate NflTeam → SportsTeam (first pass)
                if (string.IsNullOrEmpty(s.SportsTeam) && !string.IsNullOrEmpty(s.NflTeam))
                    s.SportsTeam = s.NflTeam;
                s.NflTeam = null;
                // Migrate SportsTeam → SportsTeams (per-league dict)
                if (!string.IsNullOrEmpty(s.SportsTeam) && s.SportsTeams.Count == 0)
                    foreach (var path in s.SelectedLeagues)
                        s.SportsTeams[path] = s.SportsTeam;
                s.SportsTeam = null;
                if ((s.SelectedLeagues == null || s.SelectedLeagues.Count == 0) &&
                    !string.IsNullOrEmpty(s.SportEspnPath))
                    s.SelectedLeagues = [s.SportEspnPath];
                if (s.SelectedLeagues == null || s.SelectedLeagues.Count == 0)
                    s.SelectedLeagues = ["football/nfl"];
                s.SportEspnPath = null;
                // Migrate City (single string, blank = auto-detect) → WeatherCities.
                // An explicitly-empty WeatherCities list is itself a valid, already-
                // migrated state (same auto-detect meaning) — only backfill from the
                // legacy field when City was actually set to a non-blank value.
                if ((s.WeatherCities == null || s.WeatherCities.Count == 0) &&
                    !string.IsNullOrEmpty(s.City))
                    s.WeatherCities = [s.City];
                s.WeatherCities ??= new List<string>();
                s.City = null;
                // Clear legacy canvas fields
                s.Canvas = null;
                s.Pages  = null;
                // Ensure at least one display page
                if (s.DisplayPages == null || s.DisplayPages.Count == 0)
                    s.DisplayPages = [DefaultDisplayPage()];
                return s;
            }
        }
        catch { /* corrupt file → defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, _json));
        }
        catch { /* non-fatal */ }
    }
}
