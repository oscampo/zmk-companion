using System.Text;
using ZmkCompanion.Core;

namespace ZmkCompanion.Tests;

public class TextConverterTests
{
    // ── Basic Latin (0x20-0x7F) passthrough ───────────────────────────────────

    [Theory]
    [InlineData("Hello World")]
    [InlineData("abc 123 !@#$%^&*()")]
    [InlineData("UPPERCASE lowercase")]
    [InlineData("~/path/to/file.txt")]
    public void BasicLatin_PassesThrough(string input) =>
        Assert.Equal(input, TextConverter.Convert(input));

    // ── Latin-1 Supplement (0xA1-0xFF) passthrough ───────────────────────────

    [Theory]
    [InlineData("Café")]          // é = U+00E9
    [InlineData("Zürich")]        // ü = U+00FC
    [InlineData("naïve")]         // ï = U+00EF
    [InlineData("résumé")]        // é = U+00E9
    [InlineData("jalapeño")]      // ñ = U+00F1
    [InlineData("façade")]        // ç = U+00E7
    [InlineData("Ångström")]      // Å = U+00C5
    public void Latin1Supplement_PassesThrough(string input) =>
        Assert.Equal(input, TextConverter.Convert(input));

    // ── Protocol control characters always pass through ───────────────────────

    [Fact]
    public void Newline_PassesThrough() =>
        Assert.Equal("line1\nline2", TextConverter.Convert("line1\nline2"));

    [Fact]
    public void IconSeparator_PassesThrough() =>
        Assert.Equal("text\x01", TextConverter.Convert("text\x01"));

    [Fact]
    public void CarriageReturn_PassesThrough() =>
        Assert.Equal("a\rb", TextConverter.Convert("a\rb"));

    // ── ExtraMap substitutions ────────────────────────────────────────────────

    [Theory]
    [InlineData("Straße",  "Strasse")]   // ß → ss
    [InlineData("Æsop",   "AEsop")]     // Æ → AE
    [InlineData("æther",  "aether")]    // æ → ae
    [InlineData("œuvre",  "oeuvre")]    // œ → oe
    [InlineData("Œuvre",  "Oeuvre")]    // Œ → OE
    public void ExtraMap_SubstitutesKnownChars(string input, string expected) =>
        Assert.Equal(expected, TextConverter.Convert(input));

    // ── NFD decomposition fallback ────────────────────────────────────────────

    [Theory]
    [InlineData("São Paulo", "Sao Paulo")]   // ã → a (via NFD)
    [InlineData("Łódź",      "Lodz")]        // Ł,ó,ź
    [InlineData("Gdańsk",    "Gdansk")]      // ń → n
    [InlineData("İstanbul",  "Istanbul")]    // İ → I
    public void NfdFallback_StripsAccentsToBase(string input, string expected) =>
        Assert.Equal(expected, TextConverter.Convert(input));

    // ── Characters outside all ranges are dropped ─────────────────────────────

    [Fact]
    public void CJK_Dropped()
    {
        string result = TextConverter.Convert("Hello 世界");
        Assert.Equal("Hello ", result);
    }

    [Fact]
    public void Emoji_Dropped()
    {
        string result = TextConverter.Convert("OK 👍");
        Assert.Equal("OK ", result);
    }

    [Fact]
    public void Arabic_Dropped()
    {
        string result = TextConverter.Convert("test مرحبا end");
        Assert.Equal("test  end", result);
    }

    // ── Nerd Font private-use ranges passthrough ──────────────────────────────

    [Fact]
    public void FontAwesome_PassesThrough()
    {
        string input = ""; // FA icons
        Assert.Equal(input, TextConverter.Convert(input));
    }

    [Fact]
    public void WeatherIcons_PassesThrough()
    {
        string input = ""; // NF weather range
        Assert.Equal(input, TextConverter.Convert(input));
    }

    [Fact]
    public void FiraCodeProgressBar_PassesThrough()
    {
        string input = ""; // progress bar glyphs
        Assert.Equal(input, TextConverter.Convert(input));
    }

    [Fact]
    public void Powerline_PassesThrough()
    {
        string input = "";
        Assert.Equal(input, TextConverter.Convert(input));
    }

    // ── ToBytes: UTF-8 capped at 64 bytes ────────────────────────────────────

    [Fact]
    public void ToBytes_ShortMessage_ExactLength()
    {
        byte[] bytes = TextConverter.ToBytes("Hi");
        Assert.Equal(2, bytes.Length);
        Assert.Equal(Encoding.UTF8.GetBytes("Hi"), bytes);
    }

    [Fact]
    public void ToBytes_LongAscii_CappedAt64()
    {
        string s = new('A', 100);
        byte[] bytes = TextConverter.ToBytes(s);
        Assert.Equal(64, bytes.Length);
    }

    [Fact]
    public void ToBytes_MultibyteCapped_NoPartialSequences()
    {
        // "é" is 2 bytes in UTF-8; 32 of them = 64 bytes exactly
        string s = new('é', 32);
        byte[] bytes = TextConverter.ToBytes(s);
        Assert.Equal(64, bytes.Length);
        // Must decode cleanly — no partial multi-byte sequence
        string decoded = Encoding.UTF8.GetString(bytes);
        Assert.Equal(32, decoded.Length);
    }

    [Fact]
    public void ToBytes_Empty_ReturnsEmpty()
    {
        byte[] bytes = TextConverter.ToBytes("");
        Assert.Empty(bytes);
    }

    // ── Realistic protocol payloads ───────────────────────────────────────────

    [Fact]
    public void ClockPayload_FitsIn64()
    {
        // T:<10-digit-epoch>:A  = 15 chars = 15 bytes
        string payload = "T:1719273600:A";
        byte[] bytes = TextConverter.ToBytes(payload);
        Assert.True(bytes.Length <= 64);
        Assert.Equal(payload, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void WeatherPayload_FitsIn64()
    {
        string payload = "W:Austin\n38°C\nSunny\x01"; // icon = U+E30D
        byte[] bytes = TextConverter.ToBytes(payload);
        Assert.True(bytes.Length <= 64);
    }

    [Fact]
    public void PomodoroPayload_FitsIn64()
    {
        // "25:00\n \x01"  (time + bar + icon)
        string bar = ""; // first+mid+mid+last filled
        string payload = $"25:00\n {bar}\x01";
        byte[] bytes = TextConverter.ToBytes(payload);
        Assert.True(bytes.Length <= 64, $"Payload too long: {bytes.Length} bytes");
    }
}
