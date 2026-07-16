using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

sealed class WeatherFeature
{
    // Use the system proxy (same source as the browser) with Windows credentials.
    // User-Agent is required by some corporate proxies that block non-browser requests.
    // NTLM negotiation requires multiple round-trips so 30s timeout is needed.
    private static readonly HttpClient Http = CreateHttpClient();
    private static HttpClient CreateHttpClient()
    {
        var proxy = System.Net.WebRequest.DefaultWebProxy;
        proxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
        var client = new HttpClient(new HttpClientHandler { Proxy = proxy, UseProxy = true })
            { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZmkCompanion/1.0");
        return client;
    }

    // Returns (message, summary) or throws on error.
    public async Task<(string Message, string Summary)> FetchAndSendAsync(BleService ble, string city)
    {
        var data = await FetchWeatherAsync(city);
        bool imperial = IsImperial();
        string tempStr = imperial
            ? $"{data.TempF:F0}°F"
            : $"{data.TempC:F0}°C";

        string truncCity = data.City.Length > 12 ? data.City[..12] : data.City;
        string message = Protocol.BuildText(truncCity, tempStr, data.Label, data.Icon);

        bool sent = await ble.SendAsync(message);
        if (!sent)
            throw new Exception("BLE write failed — is the keyboard still connected?");
        return (message, $"{truncCity}: {data.Icon} {tempStr}, {data.Label}");
    }

    public static async Task<WeatherData> FetchWeatherAsync(string city)
    {
        double lat, lon;
        string resolvedCity;

        if (!string.IsNullOrWhiteSpace(city))
        {
            var geo = await GetJsonAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
            var results = geo?["results"]?.AsArray();
            if (results is null || results.Count == 0)
                throw new Exception($"City not found: {city}");
            lat = results[0]!["latitude"]!.GetValue<double>();
            lon = results[0]!["longitude"]!.GetValue<double>();
            resolvedCity = results[0]!["name"]?.GetValue<string>() ?? city;
        }
        else
        {
            // Auto-detect via ipinfo.io (free HTTPS, no key required).
            var loc = await GetJsonAsync("https://ipinfo.io/json");
            string? locStr = loc?["loc"]?.GetValue<string>(); // "lat,lon"
            if (locStr is null || !locStr.Contains(','))
                throw new Exception("IP geolocation failed — specify a city in Settings.");
            var parts = locStr.Split(',');
            lat = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            lon = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            resolvedCity = loc?["city"]?.GetValue<string>() ?? "?";
        }

        // InvariantCulture prevents es-CO (and other comma-decimal locales) from
        // formatting 3.43054 as "3,43054", which the API interprets as lat=43054.
        var wx = await GetJsonAsync(
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true&temperature_unit=celsius"));
        var cw = wx!["current_weather"]!;
        double tempC = cw["temperature"]!.GetValue<double>();
        int wmo = cw["weathercode"]!.GetValue<int>();
        // is_day: 1 during the city's local daytime, 0 at night (Open-Meteo
        // computes this from the location's own sunrise/sunset, not our
        // clock) — this is what makes {weather.icon} show a moon instead of
        // a sun for a city currently in nighttime, regardless of what time
        // it is where the app is running. Defaults to day if the field is
        // ever absent (unverified against a live response in this sandbox;
        // network access is blocked here, so this fallback is unverified).
        bool isDay = cw["is_day"]?.GetValue<int>() != 0;

        return new WeatherData
        {
            City   = resolvedCity,
            TempC  = tempC,
            TempF  = tempC * 9 / 5 + 32,
            Icon   = WmoIcon(wmo, isDay),
            Label  = WmoLabel(wmo),
        };
    }

    // Fetches a URL and parses JSON, logging the response body on non-2xx so
    // the debug log shows exactly what the proxy or server returned.
    private static async Task<JsonObject?> GetJsonAsync(string url)
    {
        var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync();
            string snippet = body.Length > 120 ? body[..120] + "…" : body;
            DebugLog.Log($"weather HTTP {(int)resp.StatusCode} from {new Uri(url).Host}: {snippet}");
            resp.EnsureSuccessStatusCode(); // throws HttpRequestException with status code
        }
        return await resp.Content.ReadFromJsonAsync<JsonObject>();
    }

    // WMO 4501 weather code -> Nerd Font day/night icon + short label. Day/night
    // pairs only differ where the glyph itself depicts a sun (Sunny, PCloudy/
    // Cloudy, Rain, Snow/Showers): Overcast/Fog/Storm glyphs show no sun or moon
    // in this icon set, so they're identical either way, not an oversight.
    private static readonly (int Lo, int Hi, char DayIcon, char NightIcon, string Label)[] WmoCodes =
    [
        ( 0,  0, '', '', "Sunny"),
        ( 1,  1, '', '', "PCloudy"),
        ( 2,  2, '', '', "Cloudy"),
        ( 3,  3, '', '', "Overcast"),
        (45, 48, '', '', "Fog"),
        (51, 67, '', '', "Rain"),
        (71, 77, '', '', "Snow"),
        (80, 82, '', '', "Showers"),
        (85, 86, '', '', "Snowshwrs"),
        (95, 95, '', '', "Storm"),
        (96, 99, '', '', "HvyStorm"),
    ];

    private static char WmoIcon(int code, bool isDay)
    {
        foreach (var (lo, hi, dayIcon, nightIcon, _) in WmoCodes)
            if (code >= lo && code <= hi) return isDay ? dayIcon : nightIcon;
        return '';
    }

    private static string WmoLabel(int code)
    {
        foreach (var (lo, hi, _, _, label) in WmoCodes)
            if (code >= lo && code <= hi) return label;
        return $"WMO{code}";
    }

    private static bool IsImperial()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International");
            if (key?.GetValue("iMeasure") is string v)
                return v == "1";
        }
        catch { }
        return false;
    }
}

sealed class WeatherData
{
    public string City  { get; init; } = "";
    public double TempC { get; init; }
    public double TempF { get; init; }
    public char   Icon  { get; init; }
    public string Label { get; init; } = "";
}
