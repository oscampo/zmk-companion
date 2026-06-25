using ZmkCompanion.Core;

namespace ZmkCompanion.Tests;

public class ProtocolTests
{
    // ── BuildClock ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildClock_HasCorrectPrefix()
    {
        string msg = Protocol.BuildClock();
        Assert.StartsWith("T:", msg);
    }

    [Fact]
    public void BuildClock_HasModeSuffix()
    {
        string msg = Protocol.BuildClock();
        // Mode must be :A (12h) or :H (24h)
        Assert.True(msg.EndsWith(":A") || msg.EndsWith(":H"),
            $"Unexpected suffix in: {msg}");
    }

    [Fact]
    public void BuildClock_EpochIsReasonable()
    {
        // T:<epoch>:X — epoch must be after 2024-01-01 and before 2100-01-01
        string msg = Protocol.BuildClock();
        string[] parts = msg.Split(':');
        Assert.Equal(3, parts.Length); // "T", "<epoch>", "A" or "H"

        long epoch = long.Parse(parts[1]);
        long min = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long max = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.InRange(epoch, min, max);
    }

    [Fact]
    public void BuildClock_FitsIn64Bytes()
    {
        byte[] bytes = TextConverter.ToBytes(Protocol.BuildClock());
        Assert.True(bytes.Length <= 64, $"Clock payload too long: {bytes.Length}");
    }

    // ── BuildClear ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildClear_IsEmpty() =>
        Assert.Equal("", Protocol.BuildClear());

    // ── BuildText ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildText_SingleLine_NoIcon() =>
        Assert.Equal("Hello", Protocol.BuildText("Hello"));

    [Fact]
    public void BuildText_TwoLines() =>
        Assert.Equal("Hello\nWorld", Protocol.BuildText("Hello", "World"));

    [Fact]
    public void BuildText_ThreeLines() =>
        Assert.Equal("A\nB\nC", Protocol.BuildText("A", "B", "C"));

    [Fact]
    public void BuildText_WithIcon()
    {
        string msg = Protocol.BuildText("Score", null, null, '\uF091');
        Assert.Equal("Score\x01\uF091", msg);
    }

    [Fact]
    public void BuildText_TwoLinesWithIcon()
    {
        string msg = Protocol.BuildText("GB  SF", "29 13", null, '\uF091');
        Assert.Equal("GB  SF\n29 13\x01\uF091", msg);
    }

    [Fact]
    public void BuildText_NullMiddleLine_Skipped()
    {
        // line2=null, line3="C" → only "A\nC"
        string msg = Protocol.BuildText("A", null, "C");
        Assert.Equal("A\nC", msg);
    }

    // ── BuildWeather ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Austin",  "38°C",  "Sunny",    '\uE30D', "W:Austin\n38°C\nSunny\x01\uE30D")]
    [InlineData("New York","72°F",  "Cloudy",   '\uE303', "W:New York\n72°F\nCloudy\x01\uE303")]
    [InlineData("London",  "15°C",  "Rain",     '\uE309', "W:London\n15°C\nRain\x01\uE309")]
    public void BuildWeather_CorrectFormat(
        string city, string temp, string label, char icon, string expected) =>
        Assert.Equal(expected, Protocol.BuildWeather(city, temp, label, icon));

    [Fact]
    public void BuildWeather_FitsIn64Bytes()
    {
        string msg = Protocol.BuildWeather("SanFrancisco", "68°F", "PCloudy", '\uE302');
        byte[] bytes = TextConverter.ToBytes(msg);
        Assert.True(bytes.Length <= 64, $"Weather payload too long: {bytes.Length}");
    }
}
