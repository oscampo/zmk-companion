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
    // Sports mode: ESPN sport/league path, e.g. "football/nfl", "soccer/fifa.world"
    public string SportEspnPath { get; set; } = "football/nfl";
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
                // Migrate: promote old NflTeam to SportsTeam on first load with new settings
                if (string.IsNullOrEmpty(s.SportsTeam) && !string.IsNullOrEmpty(s.NflTeam))
                    s.SportsTeam = s.NflTeam;
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
