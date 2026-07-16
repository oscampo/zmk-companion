namespace ZmkCompanion.Core;

// One user-defined auto-start entry (CellGridEditorForm's "Inicio automático"
// tab): a named shell command line the user wants relaunched every login,
// e.g. a daily-phrase sender piped into zkc, or a sensor script piped into
// zkc --set. See AutoStartManager for how a list of these becomes the actual
// Windows Startup .bat file.
sealed class AutoStartEntry
{
    public string Name    { get; set; } = "";
    public string Command { get; set; } = "";
    public bool   Enabled { get; set; } = true;

    public AutoStartEntry Clone() => new()
    {
        Name    = Name,
        Command = Command,
        Enabled = Enabled,
    };
}
