using ZmkCompanion.Features;

namespace ZmkCompanion.Tests;

public class PomodoroConfigTests
{
    // ── Named presets ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("classic", 25,  5, 4, 15)]
    [InlineData("short",   15,  3, 4, 10)]
    [InlineData("long",    50, 10, 3, 20)]
    public void NamedPresets_ParseCorrectly(
        string name, int work, int brk, int cycles, int longBrk)
    {
        var cfg = PomodoroConfig.Parse(name);
        Assert.Equal(work,    cfg.WorkMin);
        Assert.Equal(brk,     cfg.BreakMin);
        Assert.Equal(cycles,  cfg.Cycles);
        Assert.Equal(longBrk, cfg.LongBreakMin);
    }

    [Theory]
    [InlineData("CLASSIC")]
    [InlineData("Classic")]
    [InlineData("SHORT")]
    [InlineData("LONG")]
    public void NamedPresets_CaseInsensitive(string name) =>
        Assert.NotNull(PomodoroConfig.Parse(name)); // doesn't throw

    // ── Custom numeric format ──────────────────────────────────────────────────

    [Fact]
    public void Custom_ThreeParts_NoLongBreak()
    {
        var cfg = PomodoroConfig.Parse("30,10,3");
        Assert.Equal(30, cfg.WorkMin);
        Assert.Equal(10, cfg.BreakMin);
        Assert.Equal(3,  cfg.Cycles);
        Assert.Equal(0,  cfg.LongBreakMin);
    }

    [Fact]
    public void Custom_FourParts_WithLongBreak()
    {
        var cfg = PomodoroConfig.Parse("25,5,4,15");
        Assert.Equal(25, cfg.WorkMin);
        Assert.Equal(5,  cfg.BreakMin);
        Assert.Equal(4,  cfg.Cycles);
        Assert.Equal(15, cfg.LongBreakMin);
    }

    [Fact]
    public void Custom_WithSpaces_Parsed()
    {
        var cfg = PomodoroConfig.Parse("20, 5, 4, 10");
        Assert.Equal(20, cfg.WorkMin);
        Assert.Equal(5,  cfg.BreakMin);
    }

    // ── Invalid inputs throw ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("badpreset")]
    [InlineData("25,5")]           // only 2 parts
    [InlineData("25,5,4,15,99")]   // 5 parts
    [InlineData("25,abc,4")]       // non-numeric
    [InlineData(",,")]             // empty parts
    public void InvalidInput_Throws(string input) =>
        Assert.ThrowsAny<Exception>(() => PomodoroConfig.Parse(input));

    // ── Phase progression ──────────────────────────────────────────────────────

    [Fact]
    public void InitialPhase_IsDone()
    {
        var feature = new PomodoroFeature(null!); // null BleService — not called in ctor
        Assert.Equal(PomodoroPhase.Done, feature.Phase);
    }

    // Note: full PomodoroFeature timer tests require a UI message pump (WinForms Timer).
    // Those are covered in manual integration testing on Windows.
}
