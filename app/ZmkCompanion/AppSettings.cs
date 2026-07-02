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

    // Weather data source — city name for API queries (blank = IP geolocation).
    public string City { get; set; } = "";
    // Selected leagues as ESPN paths, e.g. ["football/nfl", "soccer/eng.1"]
    public List<string> SelectedLeagues { get; set; } = ["football/nfl"];
    // Per-league team abbreviation filter keyed by ESPN path, e.g.
    // {"football/nfl": "KC", "soccer/fifa.cwc": "COL"}
    public Dictionary<string, string> SportsTeams { get; set; } = new();

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
