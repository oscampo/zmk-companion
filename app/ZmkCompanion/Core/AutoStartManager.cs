namespace ZmkCompanion.Core;

// Launches the user's registered AutoStartEntry commands. Originally this
// wrote a .bat into the Windows Startup folder, but that only fires once per
// Windows login, not once per ZmkCompanion.exe process, so closing the app
// (to reinstall an update, for example) and reopening it without logging out
// never re-ran anything, the exact bug that motivated this rewrite: the
// entries were configured but their effect (a phrase, a sensor value) was
// gone until the next login. Running them from AppContext's own startup
// instead ties the trigger to "this process is starting", which covers both
// a fresh login and a manual relaunch, with no separate file living outside
// the app's own settings to fall out of sync.
static class AutoStartManager
{
    // Short grace period before actually launching. By the time
    // AppContext.OnFirstIdle calls this, the named pipe server is already
    // constructed (see _pipe.Start earlier in that method), so this is a
    // small safety margin against startup contention, not a race-avoidance
    // delay the way the old Windows-Startup-.bat's 15s wait had to be.
    private const int LaunchDelaySeconds = 2;

    // Tracks every cmd.exe wrapper launched via Launch(), so a later StopAll()
    // (Library "Load" switching to a different Canvas's Auto-start set) can
    // kill them. Only the wrapper's own PID is known here, not any child
    // process it spawns (powershell.exe, python.exe, ...), see StopAll().
    private static readonly List<System.Diagnostics.Process> _running = new();

    public static void LaunchAll(IEnumerable<AutoStartEntry> entries)
    {
        foreach (var e in entries)
            if (e.Enabled && e.Command.Trim().Length > 0)
                Launch(e.Command);
    }

    // Runs one command line via a minimized cmd.exe, independent of the
    // caller (fire-and-forget), so one bad or long-running entry can't block
    // app startup or the others.
    public static void Launch(string command)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/c timeout /t {LaunchDelaySeconds} /nobreak >nul & {command}",
                UseShellExecute = true,
                WindowStyle     = System.Diagnostics.ProcessWindowStyle.Minimized,
            });
            if (p != null) _running.Add(p);
        }
        catch { /* a malformed command is the user's problem to fix, not a reason to crash the app */ }
    }

    // Kills every process this manager has launched, used when switching to a
    // different Canvas (Library "Load") whose own Auto-start set should
    // replace whatever was running before, not run alongside it - two
    // scripts feeding the same {ext.text}/{custom.NAME} token from different
    // Canvases would otherwise fight over it.
    //
    // "taskkill /T" (kill the whole process tree), not Process.Kill(): the
    // tracked process is the cmd.exe wrapper, which by design spawns a real
    // child (powershell.exe, python.exe, ...) to do the actual work. Killing
    // only the wrapper leaves that child running, orphaned but still
    // sending data. taskkill's /T walks the tree Windows already tracks via
    // parent PID, cheaper than standing up a Job Object for this.
    public static void StopAll()
    {
        foreach (var p in _running)
        {
            try
            {
                if (p.HasExited) continue;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "taskkill.exe",
                    Arguments       = $"/PID {p.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                });
            }
            catch { /* already exited or unkillable - nothing more to do about it */ }
        }
        _running.Clear();
    }
}
