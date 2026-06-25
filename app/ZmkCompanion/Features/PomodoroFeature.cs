using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

enum PomodoroPhase { Work, Break, LongBreak, Done }

sealed class PomodoroConfig
{
    public int WorkMin  { get; init; } = 25;
    public int BreakMin { get; init; } = 5;
    public int Cycles   { get; init; } = 4;
    public int LongBreakMin { get; init; } = 15;

    private static readonly Dictionary<string, PomodoroConfig> Presets = new()
    {
        ["classic"] = new() { WorkMin = 25, BreakMin = 5,  Cycles = 4, LongBreakMin = 15 },
        ["short"]   = new() { WorkMin = 15, BreakMin = 3,  Cycles = 4, LongBreakMin = 10 },
        ["long"]    = new() { WorkMin = 50, BreakMin = 10, Cycles = 3, LongBreakMin = 20 },
    };

    // Parses preset name or "work,break,cycles[,long_break]"
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
            WorkMin = nums[0],
            BreakMin = nums[1],
            Cycles = nums[2],
            LongBreakMin = nums.Length == 4 ? nums[3] : 0,
        };
    }
}

sealed class PomodoroFeature : IDisposable
{
    // FiraCode Nerd Font progress bar glyphs (U+EE00–EE05)
    private const char PbFirstFull  = '';
    private const char PbMidFull    = '';
    private const char PbLastFull   = '';
    private const char PbEmpty      = '';
    private const char PbFirstEmpty = '';
    private const char PbEndEmpty   = '';

    // Phase icons (Font Awesome)
    private const char IconWork  = '';  // nf-fa-gavel
    private const char IconBreak = '';  // nf-fa-coffee
    private const char IconLong  = '';  // nf-fa-hourglass

    private readonly BleService _ble;
    private System.Windows.Forms.Timer? _timer;

    public PomodoroPhase Phase    { get; private set; } = PomodoroPhase.Done;
    public int CurrentCycle       { get; private set; }
    public int TotalCycles        { get; private set; }
    public int SecondsRemaining   { get; private set; }

    public event Action? StateChanged;
    public event Action? SessionCompleted;

    public PomodoroFeature(BleService ble) => _ble = ble;

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
        Phase = PomodoroPhase.Done;
    }

    private PomodoroConfig? _cfg;

    private void EnterPhase(PomodoroPhase phase, int seconds, PomodoroConfig cfg)
    {
        _cfg = cfg;
        Phase = phase;
        SecondsRemaining = seconds;
        SendDisplay();
        StateChanged?.Invoke();

        _timer?.Dispose();
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        SecondsRemaining--;
        SendDisplay();
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
        _ = _ble.SendAsync($"Done!\n{ProgressBar(TotalCycles, TotalCycles)}\x01{IconWork}");
        SessionCompleted?.Invoke();
        StateChanged?.Invoke();
    }

    private void SendDisplay()
    {
        char icon = Phase switch
        {
            PomodoroPhase.Work      => IconWork,
            PomodoroPhase.Break     => IconBreak,
            PomodoroPhase.LongBreak => IconLong,
            _                       => IconWork,
        };
        int doneCycles = Phase == PomodoroPhase.Work ? CurrentCycle - 1 : CurrentCycle - 1;
        string bar  = ProgressBar(doneCycles, TotalCycles);
        string time = FmtTime(SecondsRemaining);
        _ = _ble.SendAsync($"{time}\n {bar}\x01{icon}");
    }

    private static string ProgressBar(int done, int total)
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

    private static string FmtTime(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        return $"{m:D2}:{s:D2}";
    }

    public void Dispose() => Stop();
}
