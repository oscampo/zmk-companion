using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

public enum SportKind { Football, Soccer, Basketball, Hockey }

public sealed record SportsLeague(
    string EspnPath,
    string DisplayName,
    string ShortName,
    SportKind Sport,
    bool WeekBased = false);

public sealed class SportsGame
{
    public string Away         { get; init; } = "";
    public string Home         { get; init; } = "";
    public string AwayScore    { get; init; } = "";
    public string HomeScore    { get; init; } = "";
    public string StatusState  { get; init; } = "pre"; // pre | in | post
    public string StatusDetail { get; init; } = "";
    public string Week         { get; init; } = "";
    public string LeagueShort  { get; init; } = "";
    public SportKind Sport     { get; init; }
}

public sealed class SportsFeature
{
    private const string BaseUrl    = "https://site.api.espn.com/apis/site/v2/sports";
    private const char   IconTrophy = ''; // nf-fa-trophy
    private const char   IconBolt   = ''; // nf-fa-bolt

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static readonly IReadOnlyList<SportsLeague> AllLeagues =
    [
        new("football/nfl",                  "NFL",                   "NFL",     SportKind.Football,   WeekBased: true),
        new("basketball/nba",                "NBA",                   "NBA",     SportKind.Basketball),
        new("hockey/nhl",                    "NHL",                   "NHL",     SportKind.Hockey),
        new("soccer/fifa.world",             "FIFA World Cup",        "FIFA WC", SportKind.Soccer),
        new("soccer/UEFA.CHAMPIONS",         "UEFA Champions League", "UEFA CL", SportKind.Soccer),
        new("soccer/eng.1",                  "Premier League",        "PL",      SportKind.Soccer),
        new("soccer/esp.1",                  "La Liga",               "LaLiga",  SportKind.Soccer),
        new("soccer/ger.1",                  "Bundesliga",            "Bund",    SportKind.Soccer),
        new("soccer/ita.1",                  "Serie A",               "SerieA",  SportKind.Soccer),
        new("soccer/fra.1",                  "Ligue 1",               "Ligue1",  SportKind.Soccer),
        new("soccer/usa.1",                  "MLS",                   "MLS",     SportKind.Soccer),
        new("soccer/mex.1",                  "Liga MX",               "LigaMX",  SportKind.Soccer),
        new("soccer/CONMEBOL.LIBERTADORES",  "Copa Libertadores",     "Lib",     SportKind.Soccer),
        new("soccer/CONMEBOL.AMERICA",       "Copa America",          "CopaA",   SportKind.Soccer),
        new("soccer/ned.1",                  "Eredivisie",            "Erediv",  SportKind.Soccer),
        new("soccer/por.1",                  "Liga Portugal",         "LigaPT",  SportKind.Soccer),
        new("soccer/bra.1",                  "Brasileirao",           "BRA",     SportKind.Soccer),
    ];

    public static SportsLeague DefaultLeague => AllLeagues[0]; // NFL

    public static SportsLeague? FindLeague(string espnPath) =>
        AllLeagues.FirstOrDefault(l => l.EspnPath.Equals(espnPath, StringComparison.OrdinalIgnoreCase));

    // Returns a known league or builds a minimal one from the ESPN path.
    public static SportsLeague FindOrCreate(string espnPath)
    {
        var found = FindLeague(espnPath);
        if (found != null) return found;
        var parts    = espnPath.Split('/', 2);
        string sport = parts.Length > 0 ? parts[0] : "football";
        string slug  = parts.Length > 1 ? parts[1] : espnPath;
        SportKind sk = sport switch
        {
            "soccer"     => SportKind.Soccer,
            "basketball" => SportKind.Basketball,
            "hockey"     => SportKind.Hockey,
            _            => SportKind.Football,
        };
        return new SportsLeague(espnPath, slug, slug, sk, WeekBased: sk == SportKind.Football && slug == "nfl");
    }

    // Fetches games currently in progress for a league, resolving the league-wide primary game
    // ("" key) and each team's game in the SAME response — live games need no page walking, so
    // there is no per-target cost to avoid here.
    public static async Task<Dictionary<string, SportsGame?>> FetchLiveMultiAsync(SportsLeague league, IReadOnlyList<string> teams)
    {
        string url  = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    data = await GetJsonAsync(url);
        var live = data is null ? [] : ParseGames(data, league).Where(g => g.StatusState == "in").ToList();
        return ResolveTargets(live, teams);
    }

    // Fetches the most recent completed game for a league ("" key) and for each team, walking
    // weeks/dates backward. All targets share the same page walk (see FetchWeekBasedMultiAsync).
    public static Task<Dictionary<string, SportsGame?>> FetchResultsMultiAsync(SportsLeague league, IReadOnlyList<string> teams) =>
        league.WeekBased
            ? FetchWeekBasedMultiAsync(league, teams, wantState: "post", forward: false)
            : FetchCalendarMultiAsync(league, teams, wantState: "post", forward: false);

    // Fetches the next upcoming game for a league ("" key) and for each team, walking weeks/dates
    // forward. All targets share the same page walk (see FetchWeekBasedMultiAsync).
    public static Task<Dictionary<string, SportsGame?>> FetchScheduleMultiAsync(SportsLeague league, IReadOnlyList<string> teams) =>
        league.WeekBased
            ? FetchWeekBasedMultiAsync(league, teams, wantState: "pre", forward: true)
            : FetchCalendarMultiAsync(league, teams, wantState: "pre", forward: true);

    private static Dictionary<string, SportsGame?> ResolveTargets(List<SportsGame> games, IReadOnlyList<string> teams)
    {
        var result = new Dictionary<string, SportsGame?> { [""] = games.FirstOrDefault() };
        foreach (var t in teams.Select(t => t.ToUpper()).Distinct())
            result[t] = games.FirstOrDefault(g => g.Away == t || g.Home == t);
        return result;
    }

    // ── Week-based navigation (NFL) ───────────────────────────────────────────

    // Walks weeks one page at a time, resolving the league-wide game AND every team's game from
    // the SAME fetched page. A team on a bye week only pushes the walk further for that team, not
    // for the league or for other teams — network cost is bounded by the slowest target to
    // resolve, not by (targets × steps).
    private static async Task<Dictionary<string, SportsGame?>> FetchWeekBasedMultiAsync(
        SportsLeague league, IReadOnlyList<string> teams, string wantState, bool forward)
    {
        var targets = new List<string> { "" };
        targets.AddRange(teams.Select(t => t.ToUpper()).Distinct());
        var pending  = new HashSet<string>(targets);
        var resolved = new Dictionary<string, SportsGame?>();

        string url  = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    data = await GetJsonAsync(url);
        if (data is null)
        {
            foreach (var t in targets) resolved[t] = null;
            return resolved;
        }

        var matches0 = ParseGames(data, league).Where(g => g.StatusState == wantState).ToList();
        foreach (var target in pending.ToList())
        {
            var match = target.Length == 0 ? matches0.FirstOrDefault()
                : matches0.FirstOrDefault(g => g.Away == target || g.Home == target);
            if (match != null) { resolved[target] = match; pending.Remove(target); }
        }

        // Step through the league's own calendar periods (real dates), not "week=N" query
        // params: confirmed on the live API that requesting an explicit season+seasontype+week
        // combo ESPN hasn't populated yet (a future preseason week, say) silently returns a
        // PAST season's completed games instead of an empty/future result, e.g. asking for
        // 2026 preseason week 2 came back with 2025's already-played week 2. A "dates=" range
        // can't do that: an unscheduled period just comes back with zero events.
        var periods = ParseCalendarPeriods(data);
        int nowIndex = periods.FindIndex(p => DateTime.UtcNow >= p.Start && DateTime.UtcNow < p.End);
        if (nowIndex < 0) nowIndex = forward ? 0 : periods.Count - 1;

        int maxSteps = forward ? 10 : 25;
        for (int step = 1; step <= maxSteps && pending.Count > 0; step++)
        {
            int idx = forward ? nowIndex + step : nowIndex - step;
            if (idx < 0 || idx >= periods.Count) break;

            var period = periods[idx];
            string range = $"{period.Start:yyyyMMdd}-{period.End.AddDays(-1):yyyyMMdd}";
            var page = await GetJsonAsync($"{url}&dates={range}");
            if (page is null) continue;

            var matches = ParseGames(page, league).Where(g => g.StatusState == wantState).ToList();
            if (matches.Count == 0) continue;

            foreach (var target in pending.ToList())
            {
                var match = target.Length == 0 ? matches[0]
                    : matches.FirstOrDefault(g => g.Away == target || g.Home == target);
                if (match != null) { resolved[target] = match; pending.Remove(target); }
            }
        }
        foreach (var t in pending) resolved[t] = null; // exhausted the step budget unresolved
        return resolved;
    }

    private readonly record struct CalendarPeriod(DateTime Start, DateTime End);

    // Flattens the scoreboard response's own "calendar" block (preseason/regular/postseason
    // weeks, each with real start/end dates) into a single chronological list, so the walk above
    // can step by date instead of by a "week" number ESPN doesn't reliably honor for periods it
    // hasn't populated yet.
    private static List<CalendarPeriod> ParseCalendarPeriods(JsonObject data)
    {
        var periods  = new List<CalendarPeriod>();
        var calendar = data["leagues"]?.AsArray().FirstOrDefault()?["calendar"]?.AsArray();
        if (calendar is null) return periods;

        foreach (var group in calendar)
        {
            var entries = group?["entries"]?.AsArray();
            if (entries is null) continue;
            foreach (var e in entries)
            {
                string? startS = e?["startDate"]?.GetValue<string>();
                string? endS   = e?["endDate"]?.GetValue<string>();
                if (DateTime.TryParse(startS, null, System.Globalization.DateTimeStyles.RoundtripKind, out var start) &&
                    DateTime.TryParse(endS,   null, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
                {
                    periods.Add(new CalendarPeriod(start, end));
                }
            }
        }
        return periods;
    }

    // ── Calendar-based navigation (soccer, NBA, NHL) ──────────────────────────

    // Same shared-walk approach as FetchWeekBasedMultiAsync, but stepping by date instead of week.
    private static async Task<Dictionary<string, SportsGame?>> FetchCalendarMultiAsync(
        SportsLeague league, IReadOnlyList<string> teams, string wantState, bool forward)
    {
        var targets = new List<string> { "" };
        targets.AddRange(teams.Select(t => t.ToUpper()).Distinct());
        var pending  = new HashSet<string>(targets);
        var resolved = new Dictionary<string, SportsGame?>();

        string url   = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    today = DateTime.UtcNow.Date;

        for (int step = 0; step < 30 && pending.Count > 0; step++)
        {
            string date = today.AddDays(forward ? step : -step).ToString("yyyyMMdd");
            var page = await GetJsonAsync($"{url}&dates={date}");
            if (page is null) continue;

            var matches = ParseGames(page, league).Where(g => g.StatusState == wantState).ToList();
            if (matches.Count == 0) continue;

            foreach (var target in pending.ToList())
            {
                var match = target.Length == 0 ? matches[0]
                    : matches.FirstOrDefault(g => g.Away == target || g.Home == target);
                if (match != null) { resolved[target] = match; pending.Remove(target); }
            }
        }
        foreach (var t in pending) resolved[t] = null; // exhausted the step budget unresolved
        return resolved;
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    public static string FormatGame(SportsGame g) => g.Sport switch
    {
        SportKind.Football   => FormatFootball(g),
        SportKind.Soccer     => FormatSoccer(g),
        SportKind.Basketball => FormatBasketball(g),
        SportKind.Hockey     => FormatHockey(g),
        _                    => FormatFootball(g),
    };

    private static string FormatFootball(SportsGame g)
    {
        if (g.StatusState == "post" && !string.IsNullOrEmpty(g.AwayScore))
            return $"{g.Away}  {g.Home}\n{g.AwayScore}  {g.HomeScore}\n{IconTrophy} Final";

        if (g.StatusState == "in")
        {
            string detail = g.StatusDetail.Length > 8 ? g.StatusDetail[..8] : g.StatusDetail;
            return $"{IconBolt}{g.Away}-{g.Home}\n{detail}\n {g.AwayScore}-{g.HomeScore}";
        }

        var parts = g.StatusDetail.Split(" - ", 2);
        string dateS = parts.Length > 0 ? parts[0].Trim() : g.StatusDetail[..Math.Min(5, g.StatusDetail.Length)];
        string timeS = FormatTimeShort(parts.Length > 1 ? parts[1].Trim() : "");
        return $"{g.Away}  {g.Home}\n{dateS}\n{timeS}";
    }

    private static string FormatSoccer(SportsGame g)
    {
        string lg = g.LeagueShort;

        if (g.StatusState == "post")
            return $"{g.Away} {g.AwayScore}-{g.HomeScore} {g.Home}\nFinal\n{lg}";

        if (g.StatusState == "in")
        {
            string detail = g.StatusDetail.Length > 10 ? g.StatusDetail[..10] : g.StatusDetail;
            return $"{IconBolt}{g.Away} {g.AwayScore}-{g.HomeScore} {g.Home}\n{detail}\n{lg}";
        }

        var parts = g.StatusDetail.Split(" - ", 2);
        string dateS = parts.Length > 0 ? parts[0].Trim() : g.StatusDetail[..Math.Min(5, g.StatusDetail.Length)];
        string timeS = FormatTimeShort(parts.Length > 1 ? parts[1].Trim() : "");
        return $"{g.Away}  {g.Home}\n{dateS}\n{timeS}";
    }

    private static string FormatBasketball(SportsGame g)
    {
        string lg = g.LeagueShort;

        if (g.StatusState == "post")
            return $"{g.Away} {g.AwayScore}-{g.Home} {g.HomeScore}\nFinal\n{lg}";

        if (g.StatusState == "in")
        {
            string detail = g.StatusDetail.Length > 10 ? g.StatusDetail[..10] : g.StatusDetail;
            return $"{IconBolt}{g.Away} {g.AwayScore}-{g.Home} {g.HomeScore}\n{detail}\n{lg}";
        }

        var parts = g.StatusDetail.Split(" - ", 2);
        string dateS = parts.Length > 0 ? parts[0].Trim() : g.StatusDetail[..Math.Min(5, g.StatusDetail.Length)];
        string timeS = FormatTimeShort(parts.Length > 1 ? parts[1].Trim() : "");
        return $"{g.Away}  {g.Home}\n{dateS}\n{timeS}";
    }

    private static string FormatHockey(SportsGame g)
    {
        string lg = g.LeagueShort;

        if (g.StatusState == "post")
            return $"{g.Away} {g.AwayScore}-{g.HomeScore} {g.Home}\nFinal\n{lg}";

        if (g.StatusState == "in")
        {
            string detail = g.StatusDetail.Length > 10 ? g.StatusDetail[..10] : g.StatusDetail;
            return $"{IconBolt}{g.Away} {g.AwayScore}-{g.HomeScore} {g.Home}\n{detail}\n{lg}";
        }

        var parts = g.StatusDetail.Split(" - ", 2);
        string dateS = parts.Length > 0 ? parts[0].Trim() : g.StatusDetail[..Math.Min(5, g.StatusDetail.Length)];
        string timeS = FormatTimeShort(parts.Length > 1 ? parts[1].Trim() : "");
        return $"{g.Away}  {g.Home}\n{dateS}\n{timeS}";
    }

    // "4:25 PM ET" -> "4:25p"  |  "1:00 AM ET" -> "1:00a"
    private static string FormatTimeShort(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return "";
        var t = timeStr.Split(' ');
        return t.Length >= 2 ? $"{t[0]}{char.ToLower(t[1][0])}" : t[0];
    }

    // ── Cycle ─────────────────────────────────────────────────────────────────

    internal async Task CycleGamesAsync(BleService ble, List<SportsGame> games, CancellationToken ct)
    {
        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;
            await ble.SendAsync(FormatGame(game));
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static List<SportsGame> ParseGames(JsonObject data, SportsLeague league)
    {
        int weekNum = data["week"]?["number"]?.GetValue<int>() ?? 0;
        string week = weekNum > 0 ? $"Wk{weekNum}" : "";

        var games  = new List<SportsGame>();
        var events = data["events"]?.AsArray();
        if (events is null) return games;

        foreach (var ev in events)
        {
            var comp = ev?["competitions"]?.AsArray().FirstOrDefault();
            if (comp is null) continue;

            var competitors = comp["competitors"]?.AsArray();
            if (competitors is null || competitors.Count < 2) continue;

            string awayAbbr = "", homeAbbr = "", awayScore = "", homeScore = "";
            foreach (var c in competitors)
            {
                string side  = c?["homeAway"]?.GetValue<string>() ?? "home";
                string abbr  = c?["team"]?["abbreviation"]?.GetValue<string>()?.ToUpper() ?? "???";
                string score = c?["score"]?.GetValue<string>() ?? "";
                if (side == "away") { awayAbbr = abbr; awayScore = score; }
                else                { homeAbbr = abbr; homeScore = score; }
            }

            var statusType = comp["status"]?["type"];
            string state  = statusType?["state"]?.GetValue<string>()       ?? "pre";
            string detail = statusType?["shortDetail"]?.GetValue<string>() ?? "";

            // shortDetail can be "Scheduled" (no date info) for non-NFL leagues.
            // Fall back to comp["date"] (ISO 8601 UTC) converted to local time.
            if (state == "pre" && !detail.Contains(" - "))
            {
                string? iso = comp["date"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iso) &&
                    DateTime.TryParse(iso, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
                {
                    var local = utc.ToLocalTime();
                    detail = $"{local:M/d} - {local:h:mm tt}";
                }
            }

            games.Add(new SportsGame
            {
                Away = awayAbbr, Home = homeAbbr,
                AwayScore = awayScore, HomeScore = homeScore,
                StatusState = state, StatusDetail = detail,
                Week = week,
                LeagueShort = league.ShortName,
                Sport = league.Sport,
            });
        }
        return games;
    }

    private static async Task<JsonObject?> GetJsonAsync(string url)
    {
        try { return await Http.GetFromJsonAsync<JsonObject>(url); }
        catch { return null; }
    }
}
