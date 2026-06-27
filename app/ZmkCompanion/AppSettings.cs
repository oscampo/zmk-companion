using System.Text.Json;
using System.Text.Json.Serialization;

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

    public string City { get; set; } = "";
    public string NflTeam { get; set; } = "";
    // Pomodoro preset: "classic" | "short" | "long" | "work,break,cycles[,long_break]"
    public string PomodoroPreset { get; set; } = "classic";
    // Selected leagues as ESPN paths, e.g. ["football/nfl", "soccer/eng.1"]
    public List<string> SelectedLeagues { get; set; } = ["football/nfl"];
    // Kept for migration from older settings; not written after migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SportEspnPath { get; set; }
    // Team abbreviation filter for sports (e.g. "KC", "FRA")
    public string SportsTeam { get; set; } = "";

    // Cycle mode configuration
    public bool   CycleClock           { get; set; } = true;
    public bool   CycleWeather         { get; set; } = true;
    public bool   CyclePomodoro        { get; set; } = false;
    public bool   CycleSports          { get; set; } = true;
    // "live" | "last" | "next"  (applies team filter for last/next)
    public string CycleSportsMode      { get; set; } = "live";
    public string CycleCustomText      { get; set; } = "";
    public int    CycleIntervalSeconds { get; set; } = 10;

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
