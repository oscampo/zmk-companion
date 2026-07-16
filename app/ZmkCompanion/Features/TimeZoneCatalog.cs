namespace ZmkCompanion.Features;

public sealed record TimeZoneEntry(string Id, string DisplayName, string ShortName);

// Curated shortlist of common IANA time zone IDs for the picker's chip UI.
// Unlike SportsFeature.AllLeagues, this needs no network fetch: IANA's
// database is static and already bundled with .NET (via ICU), so this list
// exists purely as a convenience shortcut, not as the only valid input.
// FindOrCreate falls back to building an entry from any raw IANA id the user
// types directly (e.g. into the canvas template or a custom league path),
// so the picker's curated list never limits what's actually usable.
public static class TimeZoneCatalog
{
    public static readonly IReadOnlyList<TimeZoneEntry> Common =
    [
        new("America/New_York",    "Nueva York",        "NY"),
        new("America/Chicago",     "Chicago",           "CHI"),
        new("America/Denver",      "Denver",            "DEN"),
        new("America/Los_Angeles", "Los Ángeles",        "LA"),
        new("America/Mexico_City", "Ciudad de México",  "CDMX"),
        new("America/Bogota",      "Bogotá",            "BOG"),
        new("America/Lima",        "Lima",              "LIM"),
        new("America/Santiago",    "Santiago",          "SCL"),
        new("America/Sao_Paulo",   "São Paulo",         "SAO"),
        new("America/Buenos_Aires","Buenos Aires",      "BUE"),
        new("Europe/Madrid",       "Madrid",            "MAD"),
        new("Europe/London",       "Londres",           "LON"),
        new("Europe/Paris",        "París",             "PAR"),
        new("Europe/Berlin",       "Berlín",            "BER"),
        new("Europe/Rome",         "Roma",              "ROM"),
        new("Europe/Moscow",       "Moscú",             "MOS"),
        new("Asia/Dubai",          "Dubái",             "DXB"),
        new("Asia/Kolkata",        "Nueva Delhi",       "DEL"),
        new("Asia/Shanghai",       "Shanghái",          "SHA"),
        new("Asia/Tokyo",          "Tokio",             "TOK"),
        new("Asia/Seoul",          "Seúl",              "SEL"),
        new("Australia/Sydney",   "Sídney",            "SYD"),
        new("Pacific/Auckland",   "Auckland",           "AKL"),
        new("UTC",                 "UTC",               "UTC"),
    ];

    public static TimeZoneEntry? Find(string id) =>
        Common.FirstOrDefault(z => z.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // Returns a known entry or builds a minimal one from the raw IANA id
    // (used for chips seeded from settings that reference an id outside the
    // curated list, e.g. one typed by hand or migrated from elsewhere).
    public static TimeZoneEntry FindOrCreate(string id)
    {
        var found = Find(id);
        if (found != null) return found;
        string shortName = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ') : id;
        return new TimeZoneEntry(id, shortName, shortName);
    }

    // Confirms .NET can actually resolve this as a time zone, catching a typo
    // at picker-input time instead of letting it silently fail every render
    // tick as an unresolved {time:...} token.
    public static bool IsValid(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException)  { return false; }
    }
}
