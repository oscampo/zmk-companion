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
    public string Game      { get; init; } = "";  // "AWY @ HME" or "AWY 24-20 HME"
    public string Marker    { get; init; } = "";  // live/final glyph, blank when scheduled
    public string Time      { get; init; } = "";  // live clock/period, blank otherwise
    public string Scheduled { get; init; } = "";  // date/time text, blank unless scheduled
    public string Summary   { get; init; } = "";  // full multi-line text (the plain {sports} binding)
}
