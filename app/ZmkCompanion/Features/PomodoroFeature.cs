namespace ZmkCompanion.Features;

enum PomodoroPhase { Work, Break, LongBreak, Done }

sealed class PomodoroConfig
{
    public int WorkMin      { get; set; } = 25;
    public int BreakMin     { get; set; } = 5;
    public int Cycles       { get; set; } = 4;
    public int LongBreakMin { get; set; } = 15;

    // User-pickable phase icons (Nerd Font glyphs, via GlyphPickerDialog).
    // Default to the built-in Font Awesome icons so existing sessions keep
    // their current look until the user picks something else.
    public string WorkIcon  { get; set; } = PomodoroFeature.IconWork.ToString();
    public string BreakIcon { get; set; } = PomodoroFeature.IconBreak.ToString();
    public string LongIcon  { get; set; } = PomodoroFeature.IconLong.ToString();

    private static readonly Dictionary<string, PomodoroConfig> Presets = new()
    {
        ["classic"] = new() { WorkMin = 25, BreakMin = 5,  Cycles = 4, LongBreakMin = 15 },
        ["short"]   = new() { WorkMin = 15, BreakMin = 3,  Cycles = 4, LongBreakMin = 10 },
        ["long"]    = new() { WorkMin = 50, BreakMin = 10, Cycles = 3, LongBreakMin = 20 },
    };

    public static PomodoroConfig Parse(string value)
    {
        if (Presets.TryGetValue(value.Trim().ToLower(), out var preset))
            return preset;

        var parts = value.Split(',');
        if (parts.Length is not (3 or 4))
            throw new ArgumentException($"Invalid pomodoro config: {value}");
        int[] nums = parts.Select(p => int.Parse(p.Trim())).ToArray();
        return new PomodoroConfig
        {
            WorkMin      = nums[0],
            BreakMin     = nums[1],
            Cycles       = nums[2],
            LongBreakMin = nums.Length == 4 ? nums[3] : 0,
        };
    }
}

// Pure timer state machine — no direct BLE access. Callers subscribe to
// StateChanged and read GetDisplayState() to update LiveState / the tray.
sealed class PomodoroFeature : IDisposable
{
    // FiraCode Nerd Font progress bar (U+EE00-EE05). Verified against the
    // bundled Resources/glyphnames.tsv: extra-progress_full_left=EE03,
    // extra-progress_full_mid=EE04, extra-progress_full_right=EE05 — the
    // three "full" codepoints were previously rotated by one (First/Mid/Last
    // pointed at Mid/Right/Left respectively), so the first filled cycle
    // showed the "mid" shape instead of "left".
    internal const char PbFirstFull  = ''; // left filled
    internal const char PbMidFull    = ''; // middle filled
    internal const char PbLastFull   = ''; // right filled
    internal const char PbEmpty      = ''; // middle empty
    internal const char PbFirstEmpty = ''; // left empty
    internal const char PbEndEmpty   = ''; // right empty

    // Phase icons — Font Awesome (U+F000-F2E0)
    internal const char IconWork  = ''; // nf-fa-gavel
    internal const char IconBreak = ''; // nf-fa-coffee
    internal const char IconLong  = ''; // nf-fa-hourglass

    private System.Windows.Forms.Timer? _timer;
    private PomodoroConfig? _cfg;

    public PomodoroPhase Phase          { get; private set; } = PomodoroPhase.Done;
    public int CurrentCycle             { get; private set; }
    public int TotalCycles              { get; private set; }
    public int SecondsRemaining         { get; private set; }

    public event Action? StateChanged;
    public event Action? SessionCompleted;

    public void Start(PomodoroConfig cfg)
    {
        Stop();
        TotalCycles  = cfg.Cycles;
        CurrentCycle = 1;
        EnterPhase(PomodoroPhase.Work, cfg.WorkMin * 60, cfg);
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        Phase  = PomodoroPhase.Done;
        StateChanged?.Invoke();
    }

    private void EnterPhase(PomodoroPhase phase, int seconds, PomodoroConfig cfg)
    {
        _cfg             = cfg;
        Phase            = phase;
        SecondsRemaining = seconds;
        StateChanged?.Invoke();

        _timer?.Dispose();
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        SecondsRemaining--;
        StateChanged?.Invoke();
        if (SecondsRemaining > 0) return;
        _timer?.Stop();
        AdvancePhase();
    }

    private void AdvancePhase()
    {
        if (_cfg is null) return;

        switch (Phase)
        {
            case PomodoroPhase.Work:
                bool isLast = CurrentCycle == TotalCycles;
                if (isLast && _cfg.LongBreakMin > 0)
                    EnterPhase(PomodoroPhase.LongBreak, _cfg.LongBreakMin * 60, _cfg);
                else
                    EnterPhase(PomodoroPhase.Break, _cfg.BreakMin * 60, _cfg);
                break;

            case PomodoroPhase.Break:
                CurrentCycle++;
                if (CurrentCycle > TotalCycles)
                    FinishSession();
                else
                    EnterPhase(PomodoroPhase.Work, _cfg.WorkMin * 60, _cfg);
                break;

            case PomodoroPhase.LongBreak:
                FinishSession();
                break;
        }
    }

    private void FinishSession()
    {
        Phase = PomodoroPhase.Done;
        StateChanged?.Invoke();
        SessionCompleted?.Invoke();
    }

    // Returns all display fields needed by LiveState.UpdatePomodoro().
    internal (string Time, string Phase, string Bar, string Icon, string Cycle) GetDisplayState()
    {
        if (Phase == PomodoroPhase.Done)
            return ("--:--", "", "", "", "");

        string time  = $"{SecondsRemaining / 60:D2}:{SecondsRemaining % 60:D2}";
        string phase = Phase switch
        {
            PomodoroPhase.Work      => "Work",
            PomodoroPhase.Break     => "Break",
            PomodoroPhase.LongBreak => "Long Break",
            _                       => "",
        };
        string icon = Phase switch
        {
            PomodoroPhase.Work      => _cfg?.WorkIcon  ?? IconWork.ToString(),
            PomodoroPhase.Break     => _cfg?.BreakIcon ?? IconBreak.ToString(),
            _                       => _cfg?.LongIcon  ?? IconLong.ToString(),
        };
        string bar   = BuildBar(CurrentCycle - 1, TotalCycles);
        string cycle = $"{CurrentCycle}/{TotalCycles}";
        return (time, phase, bar, icon, cycle);
    }

    internal static string BuildBar(int done, int total)
    {
        var sb = new System.Text.StringBuilder(total);
        for (int i = 0; i < total; i++)
        {
            bool isLast = i == total - 1;
            if (i >= done)
                sb.Append(i == 0 ? PbFirstEmpty : isLast ? PbEndEmpty : PbEmpty);
            else if (i == 0)
                sb.Append(PbFirstFull);
            else if (isLast)
                sb.Append(PbLastFull);
            else
                sb.Append(PbMidFull);
        }
        return sb.ToString();
    }

    public void Dispose() => Stop();
}
