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

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
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
