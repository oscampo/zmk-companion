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
        AllLeagues.FirstOrDefault(l => l.EspnPath == espnPath);

    // Fetches the most recent completed games for a league (optionally filtered by team abbreviation).
    public static Task<List<SportsGame>> FetchResultsAsync(SportsLeague league, string teamFilter = "") =>
        league.WeekBased
            ? FetchResultsWeekBasedAsync(league, teamFilter)
            : FetchResultsCalendarAsync(league, teamFilter);

    // Fetches next upcoming games for a league (optionally filtered by team abbreviation).
    public static Task<List<SportsGame>> FetchScheduleAsync(SportsLeague league, string teamFilter = "") =>
        league.WeekBased
            ? FetchScheduleWeekBasedAsync(league, teamFilter)
            : FetchScheduleCalendarAsync(league, teamFilter);

    // ── Week-based navigation (NFL) ───────────────────────────────────────────

    private static async Task<List<SportsGame>> FetchResultsWeekBasedAsync(SportsLeague league, string teamFilter)
    {
        string url  = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    data = await GetJsonAsync(url);
        if (data is null) return [];

        int season     = data["season"]?["year"]?.GetValue<int>() ?? 2024;
        int weekNum    = data["week"]?["number"]?.GetValue<int>() ?? 18;
        int seasonType = data["season"]?["type"]?.GetValue<int>() ?? 2;

        for (int step = 0; step < 25; step++)
        {
            JsonObject? page;
            if (step == 0)
                page = data;
            else
            {
                int wk = weekNum - step, st = seasonType, yr = season;
                if (wk < 1) { yr--; st = 3; wk = 5; }
                page = await GetJsonAsync($"{url}&season={yr}&seasontype={st}&week={wk}");
                if (page is null) continue;
            }

            var final = ParseGames(page, league, teamFilter).Where(g => g.StatusState == "post").ToList();
            if (final.Count > 0) return final;
        }
        return [];
    }

    private static async Task<List<SportsGame>> FetchScheduleWeekBasedAsync(SportsLeague league, string teamFilter)
    {
        string url  = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    data = await GetJsonAsync(url);
        if (data is null) return [];

        int season     = data["season"]?["year"]?.GetValue<int>() ?? 2025;
        int weekNum    = data["week"]?["number"]?.GetValue<int>() ?? 1;
        int seasonType = data["season"]?["type"]?.GetValue<int>() ?? 2;

        for (int step = 0; step < 10; step++)
        {
            var page = step == 0
                ? data
                : await GetJsonAsync($"{url}&season={season}&seasontype={seasonType}&week={weekNum + step}");
            if (page is null) continue;

            var pre = ParseGames(page, league, teamFilter).Where(g => g.StatusState == "pre").ToList();
            if (pre.Count > 0) return pre;
        }
        return [];
    }

    // ── Calendar-based navigation (soccer, NBA, NHL) ──────────────────────────

    private static async Task<List<SportsGame>> FetchResultsCalendarAsync(SportsLeague league, string teamFilter)
    {
        string url   = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    today = DateTime.UtcNow.Date;

        for (int step = 0; step < 30; step++)
        {
            string date = today.AddDays(-step).ToString("yyyyMMdd");
            var page = await GetJsonAsync($"{url}&dates={date}");
            if (page is null) continue;

            var final = ParseGames(page, league, teamFilter).Where(g => g.StatusState == "post").ToList();
            if (final.Count > 0) return final;
        }
        return [];
    }

    private static async Task<List<SportsGame>> FetchScheduleCalendarAsync(SportsLeague league, string teamFilter)
    {
        string url   = $"{BaseUrl}/{league.EspnPath}/scoreboard?limit=100";
        var    today = DateTime.UtcNow.Date;

        for (int step = 0; step < 30; step++)
        {
            string date = today.AddDays(step).ToString("yyyyMMdd");
            var page = await GetJsonAsync($"{url}&dates={date}");
            if (page is null) continue;

            var pre = ParseGames(page, league, teamFilter).Where(g => g.StatusState == "pre").ToList();
            if (pre.Count > 0) return pre;
        }
        return [];
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
        return $"{g.Away}  {g.Home}\n{dateS} {timeS}\n{lg}".TrimEnd();
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
        return $"{g.Away}  {g.Home}\n{dateS} {timeS}\n{lg}".TrimEnd();
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
        return $"{g.Away}  {g.Home}\n{dateS} {timeS}\n{lg}".TrimEnd();
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
            await Task.Delay(5000, ct);
        }
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static List<SportsGame> ParseGames(JsonObject data, SportsLeague league, string teamFilter)
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

        if (!string.IsNullOrWhiteSpace(teamFilter))
        {
            string tf = teamFilter.ToUpper();
            games = games.Where(g => g.Away == tf || g.Home == tf).ToList();
        }
        return games;
    }

    private static async Task<JsonObject?> GetJsonAsync(string url)
    {
        try { return await Http.GetFromJsonAsync<JsonObject>(url); }
        catch { return null; }
    }
}
