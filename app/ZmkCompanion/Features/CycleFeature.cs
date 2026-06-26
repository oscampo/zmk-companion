using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

// Automatically cycles Clock -> Weather -> Sports results, then repeats.
sealed class CycleFeature
{
    private const int ClockDurationMs   = 15_000;
    private const int WeatherDurationMs = 15_000;

    private readonly BleService    _ble;
    private readonly WeatherFeature _weather;
    private readonly SportsFeature  _sports;

    public CycleFeature(BleService ble, WeatherFeature weather, SportsFeature sports)
    {
        _ble     = ble;
        _weather = weather;
        _sports  = sports;
    }

    public async Task RunAsync(AppSettings settings, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Clock
            await _ble.SendAsync(Protocol.BuildClock());
            await Delay(ClockDurationMs, ct);
            if (ct.IsCancellationRequested) break;

            // Weather
            try { await _weather.FetchAndSendAsync(_ble, settings.City); }
            catch { /* non-fatal — display stays on clock */ }
            await Delay(WeatherDurationMs, ct);
            if (ct.IsCancellationRequested) break;

            // Sports: last results, 5 s per game
            var league = SportsFeature.FindLeague(settings.SportEspnPath) ?? SportsFeature.DefaultLeague;
            var games  = await SportsFeature.FetchResultsAsync(league, settings.SportsTeam);
            if (games.Count > 0)
                await _sports.CycleGamesAsync(_ble, games, ct);
        }
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); }
        catch (OperationCanceledException) { }
    }
}
