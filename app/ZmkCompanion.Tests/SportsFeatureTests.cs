using ZmkCompanion.Features;

namespace ZmkCompanion.Tests;

public class SportsFeatureTests
{
    static readonly SportsLeague Nfl    = SportsFeature.AllLeagues.First(l => l.EspnPath == "football/nfl");
    static readonly SportsLeague FifaWC = SportsFeature.AllLeagues.First(l => l.EspnPath == "soccer/fifa.world");
    static readonly SportsLeague Nba    = SportsFeature.AllLeagues.First(l => l.EspnPath == "basketball/nba");
    static readonly SportsLeague Nhl    = SportsFeature.AllLeagues.First(l => l.EspnPath == "hockey/nhl");
    static readonly SportsLeague Pl     = SportsFeature.AllLeagues.First(l => l.EspnPath == "soccer/eng.1");

    // ── League catalogue ──────────────────────────────────────────────────────

    [Fact]
    public void AllLeagues_ContainsMajorLeagues()
    {
        Assert.NotNull(SportsFeature.FindLeague("football/nfl"));
        Assert.NotNull(SportsFeature.FindLeague("basketball/nba"));
        Assert.NotNull(SportsFeature.FindLeague("hockey/nhl"));
        Assert.NotNull(SportsFeature.FindLeague("soccer/fifa.world"));
        Assert.NotNull(SportsFeature.FindLeague("soccer/eng.1"));
    }

    [Fact]
    public void FindLeague_UnknownPath_ReturnsNull() =>
        Assert.Null(SportsFeature.FindLeague("unknown/path"));

    [Fact]
    public void FindLeague_PremierLeague_HasCorrectShortName()
    {
        var l = SportsFeature.FindLeague("soccer/eng.1");
        Assert.NotNull(l);
        Assert.Equal("PL", l.ShortName);
    }

    [Fact]
    public void DefaultLeague_IsNfl() =>
        Assert.Equal("football/nfl", SportsFeature.DefaultLeague.EspnPath);

    [Fact]
    public void NflLeague_IsWeekBased() =>
        Assert.True(Nfl.WeekBased);

    [Fact]
    public void SoccerLeagues_AreNotWeekBased()
    {
        foreach (var l in SportsFeature.AllLeagues.Where(l => l.Sport == SportKind.Soccer))
            Assert.False(l.WeekBased);
    }

    // ── NFL format ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatGame_Nfl_Final_ThreeLines()
    {
        var g = MakeGame(Nfl, "KC", "SF", "25", "22", "post", "Final");
        string[] lines = SportsFeature.FormatGame(g).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("KC", lines[0]);
        Assert.Contains("SF", lines[0]);
        Assert.Contains("25", lines[1]);
        Assert.Contains("22", lines[1]);
        Assert.Contains("Final", lines[2]);
    }

    [Fact]
    public void FormatGame_Nfl_Live_ShowsBolt()
    {
        var g = MakeGame(Nfl, "DAL", "PHI", "14", "21", "in", "Q3 4:32");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("", result); // nf-fa-bolt
        Assert.Contains("DAL", result);
        Assert.Contains("PHI", result);
    }

    [Fact]
    public void FormatGame_Nfl_Live_DetailTruncatedAt8()
    {
        var g = MakeGame(Nfl, "NE", "BUF", "7", "10", "in", "Q4 00:32 remaining");
        string[] lines = SportsFeature.FormatGame(g).Split('\n');
        Assert.True(lines[1].Length <= 8, $"Detail line too long: '{lines[1]}'");
    }

    [Fact]
    public void FormatGame_Nfl_Pre_ShowsDateAndTime()
    {
        var g = MakeGame(Nfl, "LAR", "SEA", "", "", "pre", "9/13 - 4:25 PM ET");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("LAR", result);
        Assert.Contains("9/13", result);
        Assert.Contains("4:25p", result);
    }

    // ── Soccer format ─────────────────────────────────────────────────────────

    [Fact]
    public void FormatGame_Soccer_Final_ScoreOnFirstLine()
    {
        var g = MakeGame(FifaWC, "ARG", "FRA", "3", "3", "post", "Final");
        string[] lines = SportsFeature.FormatGame(g).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("ARG", lines[0]);
        Assert.Contains("3-3", lines[0]);
        Assert.Contains("FRA", lines[0]);
        Assert.Contains(FifaWC.ShortName, lines[2]);
    }

    [Fact]
    public void FormatGame_Soccer_Live_ShowsBolt()
    {
        var g = MakeGame(FifaWC, "ARG", "FRA", "1", "0", "in", "75'");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("", result);
        Assert.Contains("1-0", result);
    }

    [Fact]
    public void FormatGame_Soccer_Pre_DateAndTime()
    {
        var g = MakeGame(Pl, "ARS", "CHE", "", "", "pre", "8/17 - 12:30 PM ET");
        string[] lines = SportsFeature.FormatGame(g).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("ARS", lines[0]);
        Assert.Contains("8/17", lines[1]);
        Assert.Contains("12:30p", lines[2]);
    }

    // ── NBA format ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatGame_Nba_Final_ContainsScoresAndLeague()
    {
        var g = MakeGame(Nba, "LAL", "BOS", "108", "112", "post", "Final");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("108", result);
        Assert.Contains("112", result);
        Assert.Contains("NBA", result);
    }

    [Fact]
    public void FormatGame_Nba_Live_ShowsBolt()
    {
        var g = MakeGame(Nba, "LAL", "BOS", "95", "98", "in", "Q4 2:15");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("", result);
        Assert.Contains("Q4 2:15", result);
    }

    // ── NHL format ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatGame_Nhl_Final_ShowsScore()
    {
        var g = MakeGame(Nhl, "TOR", "MTL", "3", "2", "post", "Final");
        string result = SportsFeature.FormatGame(g);
        Assert.Contains("TOR", result);
        Assert.Contains("3-2", result);
        Assert.Contains("NHL", result);
    }

    [Fact]
    public void FormatGame_Nhl_Live_ThreeLines()
    {
        var g = MakeGame(Nhl, "TOR", "MTL", "1", "1", "in", "P2 5:33");
        string[] lines = SportsFeature.FormatGame(g).Split('\n');
        Assert.Equal(3, lines.Length);
    }

    // ── Protocol compliance: all outputs fit in 64 bytes ──────────────────────

    [Theory]
    [MemberData(nameof(AllSportsGames))]
    public void FormatGame_AllSports_FitIn64Bytes(SportsGame game)
    {
        string result = SportsFeature.FormatGame(game);
        byte[] bytes  = System.Text.Encoding.UTF8.GetBytes(result);
        Assert.True(bytes.Length <= 64,
            $"Output too long ({bytes.Length} bytes): {result}");
    }

    public static TheoryData<SportsGame> AllSportsGames() => new()
    {
        // NFL: post / in / pre
        MakeGame(Nfl, "KC",  "SF",  "25", "22", "post", "Final"),
        MakeGame(Nfl, "KC",  "SF",  "14", "10", "in",   "Q2 5:00"),
        MakeGame(Nfl, "KC",  "SF",  "",   "",   "pre",  "2/2 - 6:30 PM ET"),
        // Soccer: post / in / pre
        MakeGame(FifaWC, "ARG", "FRA", "3", "3", "post", "Final"),
        MakeGame(FifaWC, "ARG", "FRA", "1", "0", "in",   "75' 1H"),
        MakeGame(FifaWC, "ARG", "FRA", "",  "",  "pre",  "6/14 - 3:00 PM ET"),
        // NBA: post / in
        MakeGame(Nba, "LAL", "BOS", "128", "115", "post", "Final"),
        MakeGame(Nba, "LAL", "BOS", "95",  "98",  "in",   "Q4 2:15"),
        // NHL: post / in
        MakeGame(Nhl, "TOR", "MTL", "3", "2", "post", "Final"),
        MakeGame(Nhl, "TOR", "MTL", "1", "1", "in",   "P2 5:33"),
    };

    // ── Helper ────────────────────────────────────────────────────────────────

    static SportsGame MakeGame(SportsLeague lg, string away, string home,
        string awayS, string homeS, string state, string detail) =>
        new()
        {
            Away = away, Home = home, AwayScore = awayS, HomeScore = homeS,
            StatusState = state, StatusDetail = detail,
            LeagueShort = lg.ShortName, Sport = lg.Sport,
        };
}
