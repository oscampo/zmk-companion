using ZmkCompanion.Features;

namespace ZmkCompanion.Tests;

public class NflFeatureTests
{
    // ── FormatGame: Final ──────────────────────────────────────────────────────

    [Fact]
    public void FormatGame_Final_ShowsTeamsScoresTrophy()
    {
        var game = new NflGame
        {
            Away = "GB", Home = "MIN",
            AwayScore = "29", HomeScore = "13",
            StatusState = "post", StatusDetail = "Final",
        };
        string result = NflFeature.FormatGame(game);

        Assert.Contains("GB", result);
        Assert.Contains("MIN", result);
        Assert.Contains("29", result);
        Assert.Contains("13", result);
        Assert.Contains("Final", result);
        Assert.Contains("\n", result); // multi-line
    }

    [Fact]
    public void FormatGame_Final_ThreeLines()
    {
        var game = new NflGame
        {
            Away = "KC", Home = "SF",
            AwayScore = "25", HomeScore = "22",
            StatusState = "post", StatusDetail = "Final",
        };
        string[] lines = NflFeature.FormatGame(game).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("KC", lines[0]);
        Assert.Contains("SF", lines[0]);
        Assert.Contains("25", lines[1]);
        Assert.Contains("22", lines[1]);
        Assert.Contains("Final", lines[2]);
    }

    // ── FormatGame: Live (in-progress) ────────────────────────────────────────

    [Fact]
    public void FormatGame_Live_ShowsBoltAndScore()
    {
        var game = new NflGame
        {
            Away = "DAL", Home = "PHI",
            AwayScore = "14", HomeScore = "21",
            StatusState = "in", StatusDetail = "Q3 4:32",
        };
        string result = NflFeature.FormatGame(game);

        Assert.Contains("DAL", result);
        Assert.Contains("PHI", result);
        Assert.Contains("14", result);
        Assert.Contains("21", result);
        // Lightning bolt icon (nf-fa-bolt) should be present
        Assert.Contains("", result);
    }

    [Fact]
    public void FormatGame_Live_StatusDetailTruncatedAt8()
    {
        var game = new NflGame
        {
            Away = "NE", Home = "BUF",
            AwayScore = "7", HomeScore = "10",
            StatusState = "in", StatusDetail = "Q4 00:32 remaining",
        };
        string result = NflFeature.FormatGame(game);
        string[] lines = result.Split('\n');
        // Line 2 is status detail — must be at most 8 chars
        Assert.True(lines[1].Length <= 8, $"Detail line too long: '{lines[1]}'");
    }

    // ── FormatGame: Pre-game (scheduled) ─────────────────────────────────────

    [Fact]
    public void FormatGame_PreGame_ShowsTeamsAndDate()
    {
        var game = new NflGame
        {
            Away = "LAR", Home = "SEA",
            AwayScore = "", HomeScore = "",
            StatusState = "pre", StatusDetail = "9/13 - 4:25 PM ET",
        };
        string result = NflFeature.FormatGame(game);

        Assert.Contains("LAR", result);
        Assert.Contains("SEA", result);
        Assert.Contains("9/13", result);
    }

    [Fact]
    public void FormatGame_PreGame_ThreeLines()
    {
        var game = new NflGame
        {
            Away = "MIA", Home = "NYJ",
            StatusState = "pre", StatusDetail = "10/6 - 1:00 PM ET",
        };
        string[] lines = NflFeature.FormatGame(game).Split('\n');
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void FormatGame_PreGame_TimeFormattedShort()
    {
        // "4:25 PM ET" → "4:25p"
        var game = new NflGame
        {
            Away = "CLE", Home = "PIT",
            StatusState = "pre", StatusDetail = "9/22 - 4:25 PM ET",
        };
        string result = NflFeature.FormatGame(game);
        Assert.Contains("4:25p", result);
    }

    [Fact]
    public void FormatGame_PreGame_MorningGame()
    {
        // "1:00 PM ET" → "1:00p"
        var game = new NflGame
        {
            Away = "IND", Home = "TEN",
            StatusState = "pre", StatusDetail = "10/1 - 1:00 PM ET",
        };
        string result = NflFeature.FormatGame(game);
        Assert.Contains("1:00p", result);
    }

    // ── Protocol compliance: all outputs fit in 64 bytes ──────────────────────

    [Theory]
    [MemberData(nameof(AllGameStates))]
    public void FormatGame_OutputFitsIn64Bytes(NflGame game)
    {
        string result = NflFeature.FormatGame(game);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(result);
        Assert.True(bytes.Length <= 64,
            $"Game output too long ({bytes.Length} bytes): {result}");
    }

    public static TheoryData<NflGame> AllGameStates() => new()
    {
        new NflGame { Away = "KC", Home = "SF",  AwayScore = "25", HomeScore = "22", StatusState = "post", StatusDetail = "Final" },
        new NflGame { Away = "KC", Home = "SF",  AwayScore = "14", HomeScore = "10", StatusState = "in",   StatusDetail = "Q2 5:00" },
        new NflGame { Away = "KC", Home = "SF",  StatusState = "pre",  StatusDetail = "2/2 - 6:30 PM ET" },
    };
}
