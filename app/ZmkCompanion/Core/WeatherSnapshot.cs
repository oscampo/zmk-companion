namespace ZmkCompanion.Core;

// Formatted weather fields for the {weather.*} bindings, one per configured
// city. A record (like SportsSnapshot) so LiveState can cheaply compare old
// vs. new by value and skip firing Changed when a poll returns unchanged data.
sealed record WeatherSnapshot
{
    public string Icon { get; init; } = "";
    public string Temp { get; init; } = "--°";
    public string City { get; init; } = "";
}
