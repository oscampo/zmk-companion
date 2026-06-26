using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// Cycles through the user-selected display modes in sequence.
// Each enabled slide is shown for CycleIntervalSeconds before advancing.
// Failed steps (e.g. weather with no internet) are skipped silently.
sealed class CycleFeature
{
    private readonly BleService      _ble;
    private readonly WeatherFeature  _weather;
    private readonly SportsFeature   _sports;
    private readonly PomodoroFeature _pomodoro;

    public CycleFeature(BleService ble, WeatherFeature weather,
                        SportsFeature sports, PomodoroFeature pomodoro)
    {
        _ble      = ble;
        _weather  = weather;
        _sports   = sports;
        _pomodoro = pomodoro;
    }

    // onSlide: called with a short label each time a new slide starts ("Clock", "Weather", …).
    public async Task RunAsync(AppSettings settings, Action<string> onSlide, CancellationToken ct)
    {
        int ms = Math.Max(1000, settings.CycleIntervalSeconds * 1000);

        while (!ct.IsCancellationRequested)
        {
            bool any = false;

            // ── Clock ─────────────────────────────────────────────────────────
            if (settings.CycleClock)
            {
                onSlide("Clock");
                await _ble.SendAsync(Protocol.BuildClock());
                await Delay(ms, ct);
                any = true;
            }

            // ── Weather ───────────────────────────────────────────────────────
            if (!ct.IsCancellationRequested && settings.CycleWeather)
            {
                onSlide("Weather");
                try { await _weather.FetchAndSendAsync(_ble, settings.City); }
                catch { /* skip silently */ }
                await Delay(ms, ct);
                any = true;
            }

            // ── Pomodoro (only when a session is running) ──────────────────────
            if (!ct.IsCancellationRequested && settings.CyclePomodoro)
            {
                var text = _pomodoro.GetDisplayText();
                if (text is not null)
                {
                    onSlide("Pomodoro");
                    await _ble.SendAsync(text);
                    await Delay(ms, ct);
                    any = true;
                }
            }

            // ── Sports ────────────────────────────────────────────────────────
            if (!ct.IsCancellationRequested && settings.CycleSports)
            {
                var league = SportsFeature.FindLeague(settings.SportEspnPath)
                             ?? SportsFeature.DefaultLeague;
                var games = settings.CycleSportsMode switch
                {
                    "last" => await SportsFeature.FetchResultsAsync(league, settings.SportsTeam),
                    "next" => await SportsFeature.FetchScheduleAsync(league, settings.SportsTeam),
                    _      => await SportsFeature.FetchLiveAsync(league),   // "live" (default)
                };
                foreach (var game in games)
                {
                    if (ct.IsCancellationRequested) break;
                    onSlide("Sports");
                    await _ble.SendAsync(SportsFeature.FormatGame(game));
                    await Delay(ms, ct);
                    any = true;
                }
                // If no games found (e.g. nothing live), skip silently with no delay.
            }

            // ── Custom text ────────────────────────────────────────────────────
            if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(settings.CycleCustomText))
            {
                onSlide("Text");
                await _ble.SendAsync(settings.CycleCustomText);
                await Delay(ms, ct);
                any = true;
            }

            // Nothing enabled — idle briefly to avoid a tight spin
            if (!any && !ct.IsCancellationRequested)
                await Delay(1000, ct);
        }
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); }
        catch (OperationCanceledException) { }
    }
}
