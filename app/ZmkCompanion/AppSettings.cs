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

    // Canvas pages — each page is an independent set of widget placements.
    // When CyclePages is true and there is more than one page, AppContext
    // rotates through them using each page's DurationSeconds.
    public List<CanvasPage> Pages       { get; set; } = [new CanvasPage { Widgets = [new WidgetPlacement()] }];
    public bool             CyclePages  { get; set; } = false;

    // Legacy single-page layout — kept only so old settings.json files can be
    // migrated into Pages on load. Not written after migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetPlacement>? Canvas { get; set; }

    // Weather data source — city name for API queries (blank = IP geolocation).
    public string City { get; set; } = "";
    // Selected leagues as ESPN paths, e.g. ["football/nfl", "soccer/eng.1"]
    public List<string> SelectedLeagues { get; set; } = ["football/nfl"];
    // Team abbreviation filter for sports (e.g. "KC", "FRA")
    public string SportsTeam { get; set; } = "";

    // Kept for migration from older settings; not written after migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NflTeam { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SportEspnPath { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var s = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
                // Migrate NflTeam → SportsTeam
                if (string.IsNullOrEmpty(s.SportsTeam) && !string.IsNullOrEmpty(s.NflTeam))
                    s.SportsTeam = s.NflTeam;
                // Migrate SportEspnPath (single) → SelectedLeagues (list)
                if ((s.SelectedLeagues == null || s.SelectedLeagues.Count == 0) &&
                    !string.IsNullOrEmpty(s.SportEspnPath))
                    s.SelectedLeagues = [s.SportEspnPath];
                if (s.SelectedLeagues == null || s.SelectedLeagues.Count == 0)
                    s.SelectedLeagues = ["football/nfl"];
                // Clear legacy field so it isn't written back
                s.SportEspnPath = null;
                // Migrate single-page Canvas → Pages (old files only; Canvas is never
                // written after migration, so non-empty here means a pre-Pages file).
                if (s.Canvas is { Count: > 0 } oldCanvas)
                    s.Pages = [new CanvasPage { Name = "Page 1", Widgets = oldCanvas }];
                s.Canvas = null;
                if (s.Pages == null || s.Pages.Count == 0)
                    s.Pages = [new CanvasPage { Widgets = [new WidgetPlacement()] }];
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
