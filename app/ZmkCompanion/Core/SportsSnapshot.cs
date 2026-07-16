namespace ZmkCompanion.Core;

// Formatted sports fields for the {sports.*} bindings, resolved from the last
// poll. Kept independent of the Features layer's SportsGame/SportsLeague types
// so Core doesn't need to reference Features — AppContext maps one to the other.
// A record so LiveState can cheaply compare old vs. new snapshots by value and
// skip firing Changed (and therefore a full-frame BLE resend) when a poll
// returns the same data as last time — which is the common case.
sealed record SportsSnapshot
{
    public string Sport     { get; init; } = "";  // "Football" | "Soccer" | "Basketball" | "Hockey"
    public string League    { get; init; } = "";  // short league name, e.g. "NFL"
    public string Team      { get; init; } = "";  // tracked team abbreviation, if configured
    public string Away      { get; init; } = "";  // away team abbreviation
    public string Home      { get; init; } = "";  // home team abbreviation
    public string Score     { get; init; } = "";  // "38-35" for post games, "" otherwise
    public string Marker    { get; init; } = "";  // live/final glyph, blank when scheduled
    public string Scheduled { get; init; } = "";  // date/time text, blank unless scheduled

    // {sports.live_*}, populated only while StatusState=="in", so these are
    // the "is a game live right now" fields (replaces the old combined
    // {sports.game}/{sports.time}, which mixed live/pre/post formatting into
    // one string). LiveGame falls back to "No games" when nothing is live,
    // explicit rather than a blank row; LiveScore/LiveTime stay blank instead
    // (a page with all three in adjacent tiers doesn't need "No games" x3).
    public string LiveGame  { get; init; } = "";  // "[Home] [Away]", "No games" otherwise
    public string LiveScore { get; init; } = "";  // "[HomeScore] - [AwayScore]", "" otherwise
    public string LiveTime  { get; init; } = "";  // live clock/period, "" otherwise
    public string Summary   { get; init; } = "";  // full multi-line text (the plain {sports} binding)
}
