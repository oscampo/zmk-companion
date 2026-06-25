using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

public sealed class NflGame
{
    public string Away        { get; init; } = "";
    public string Home        { get; init; } = "";
    public string AwayScore   { get; init; } = "";
    public string HomeScore   { get; init; } = "";
    public string StatusState  { get; init; } = "pre"; // pre | in | post
    public string StatusDetail { get; init; } = "";
    public string Week         { get; init; } = "";
}

sealed class NflFeature
{
    private const string ScoreboardUrl =
        "https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?limit=100";

    private const char IconTrophy = '\uF091';   // nf-fa-trophy
    private const char IconBolt   = '\uF0E7';   // nf-fa-bolt

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Fetches last completed week games (optionally filtered by team abbreviation).
    public static async Task<List<NflGame>> FetchResultsAsync(string teamFilter = "")
    {
        var data = await GetJsonAsync(ScoreboardUrl);
        if (data is null) return [];

        int season     = data["season"]?["year"]?.GetValue<int>()  ?? 2024;
        int weekNum    = data["week"]?["number"]?.GetValue<int>()   ?? 18;
        int seasonType = data["season"]?["type"]?.GetValue<int>()   ?? 2;

        for (int step = 0; step < 25; step++)
        {
            JsonObject? page;
            if (step == 0)
            {
                page = data;
            }
            else
            {
                int wk = weekNum - step, st = seasonType, yr = season;
                if (wk < 1) { yr--; st = 3; wk = 5; }
                page = await GetJsonAsync($"{ScoreboardUrl}&season={yr}&seasontype={st}&week={wk}");
                if (page is null) continue;
            }

            var games = ParseGames(page, teamFilter);
            var final = games.Where(g => g.StatusState == "post").ToList();
            if (final.Count > 0) return final;
        }
        return [];
    }

    // Fetches next upcoming week games (optionally filtered by team abbreviation).
    public static async Task<List<NflGame>> FetchScheduleAsync(string teamFilter = "")
    {
        var data = await GetJsonAsync(ScoreboardUrl);
        if (data is null) return [];

        int season     = data["season"]?["year"]?.GetValue<int>()  ?? 2025;
        int weekNum    = data["week"]?["number"]?.GetValue<int>()   ?? 1;
        int seasonType = data["season"]?["type"]?.GetValue<int>()   ?? 2;

        for (int step = 0; step < 10; step++)
        {
            JsonObject? page;
            if (step == 0)
                page = data;
            else
                page = await GetJsonAsync($"{ScoreboardUrl}&season={season}&seasontype={seasonType}&week={weekNum + step}");
            if (page is null) continue;

            var games = ParseGames(page, teamFilter);
            var pre   = games.Where(g => g.StatusState == "pre").ToList();
            if (pre.Count > 0) return pre;
        }
        return [];
    }

    // Formats a game for the display (matches Python format_nfl_game()).
    public static string FormatGame(NflGame g)
    {
        string away = g.Away, home = g.Home;

        if (g.StatusState == "post" && !string.IsNullOrEmpty(g.AwayScore))
        {
            return $"{away}  {home}\n{g.AwayScore}  {g.HomeScore}\n{IconTrophy} Final";
        }
        if (g.StatusState == "in")
        {
            string detail = g.StatusDetail.Length > 8 ? g.StatusDetail[..8] : g.StatusDetail;
            return $"{IconBolt}{away}-{home}\n{detail}\n {g.AwayScore}-{g.HomeScore}";
        }

        // Pre-game: "9/13 - 4:25 PM ET"
        var parts = g.StatusDetail.Split(" - ", 2);
        string dateS = parts.Length > 0 ? parts[0].Trim() : g.StatusDetail[..Math.Min(5, g.StatusDetail.Length)];
        string timeS = "";
        if (parts.Length > 1)
        {
            var t = parts[1].Trim().Split(' ');
            if (t.Length >= 2)
                timeS = $"{t[0]}{char.ToLower(t[1][0])}";
        }
        return $"{away}  {home}\n{dateS}\n{timeS}";
    }

    // Sends all games cycling with 5s between each.
    public async Task CycleGamesAsync(BleService ble, List<NflGame> games, CancellationToken ct)
    {
        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;
            await ble.SendAsync(FormatGame(game));
            await Task.Delay(5000, ct);
        }
    }

    private static List<NflGame> ParseGames(JsonObject data, string teamFilter)
    {
        int weekNum = data["week"]?["number"]?.GetValue<int>() ?? 0;
        string week = weekNum > 0 ? $"Wk{weekNum}" : "";

        var games = new List<NflGame>();
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

            games.Add(new NflGame
            {
                Away = awayAbbr, Home = homeAbbr,
                AwayScore = awayScore, HomeScore = homeScore,
                StatusState = state, StatusDetail = detail,
                Week = week,
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
