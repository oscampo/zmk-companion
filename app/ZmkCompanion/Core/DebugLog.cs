namespace ZmkCompanion.Core;

// Lightweight append-only debug log for diagnosing display update issues
// (clock drift, page cycling, render pipeline). Writes to
// %AppData%\ZmkCompanion\debug.log. Reset() truncates it at app start so
// each session's log is easy to read on its own.
static class DebugLog
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZmkCompanion", "debug.log");

    private static readonly object _lock = new();

    public static void Reset()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, $"=== ZMK Companion debug log — session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            }
        }
        catch { }
    }
}
