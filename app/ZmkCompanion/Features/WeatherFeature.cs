using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using ZmkCompanion.Core;

namespace ZmkCompanion.Features;

sealed class WeatherFeature
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

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
            var geo = await Http.GetFromJsonAsync<JsonObject>(
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
            var loc = await Http.GetFromJsonAsync<JsonObject>("https://ipinfo.io/json");
            string? locStr = loc?["loc"]?.GetValue<string>(); // "lat,lon"
            if (locStr is null || !locStr.Contains(','))
                throw new Exception("IP geolocation failed — specify a city in Settings.");
            var parts = locStr.Split(',');
            lat = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            lon = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            resolvedCity = loc?["city"]?.GetValue<string>() ?? "?";
        }

        var wx = await Http.GetFromJsonAsync<JsonObject>(
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
            "&current_weather=true&temperature_unit=celsius");
        var cw = wx!["current_weather"]!;
        double tempC = cw["temperature"]!.GetValue<double>();
        int wmo = cw["weathercode"]!.GetValue<int>();

        return new WeatherData
        {
            City   = resolvedCity,
            TempC  = tempC,
            TempF  = tempC * 9 / 5 + 32,
            Icon   = WmoIcon(wmo),
            Label  = WmoLabel(wmo),
        };
    }

    // WMO 4501 weather code → Nerd Font icon + short label
    private static readonly (int Lo, int Hi, char Icon, string Label)[] WmoCodes =
    [
        ( 0,  0, '', "Sunny"),
        ( 1,  1, '', "PCloudy"),
        ( 2,  2, '', "Cloudy"),
        ( 3,  3, '', "Overcast"),
        (45, 48, '', "Fog"),
        (51, 67, '', "Rain"),
        (71, 77, '', "Snow"),
        (80, 82, '', "Showers"),
        (85, 86, '', "Snowshwrs"),
        (95, 95, '', "Storm"),
        (96, 99, '', "HvyStorm"),
    ];

    private static char WmoIcon(int code)
    {
        foreach (var (lo, hi, icon, _) in WmoCodes)
            if (code >= lo && code <= hi) return icon;
        return '';
    }

    private static string WmoLabel(int code)
    {
        foreach (var (lo, hi, _, label) in WmoCodes)
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
