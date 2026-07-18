using System.Text.Json.Nodes;

namespace ZmkCompanion.Core;

// Checks GitHub's public Releases API for a newer tagged version than the
// one currently running. No account, no telemetry, just one GET to a public
// endpoint - the same kind of network access the app already uses for
// weather/sports data, not a new "phones home" concern.
static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/oscampo/zmk-companion/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/oscampo/zmk-companion/releases";

    // Same proxy/User-Agent setup as WeatherFeature: corporate proxies (a
    // work PC, the exact "two machines" case this whole feature set is
    // for) often block non-browser traffic without both of these.
    private static readonly HttpClient Http = CreateHttpClient();
    private static HttpClient CreateHttpClient()
    {
        var proxy = System.Net.WebRequest.DefaultWebProxy;
        proxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
        var client = new HttpClient(new HttpClientHandler { Proxy = proxy, UseProxy = true })
            { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's API rejects requests with no User-Agent header at all.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZmkCompanion-UpdateCheck");
        return client;
    }

    public readonly record struct UpdateInfo(string Version, string Url);

    // Returns the latest release if it's newer than the running build, null
    // if already current OR the check failed for any reason (offline, a
    // proxy blocking it, GitHub's rate limit, a malformed response). A
    // failed check is never an error to surface to the user, it's just
    // "nothing to report this time" - the same silent-fallback approach
    // WeatherFeature/SportsFeature already use for their own network calls.
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var resp = await Http.GetAsync(ReleasesApiUrl);
            if (!resp.IsSuccessStatusCode) return null;

            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            string? tag = json?["tag_name"]?.GetValue<string>();
            string? url = json?["html_url"]?.GetValue<string>();
            if (string.IsNullOrEmpty(tag)) return null;

            string versionPart = tag.StartsWith('v') ? tag[1..] : tag;
            if (!Version.TryParse(versionPart, out var latest)) return null;

            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(0, 0, 0);
            return latest > current ? new UpdateInfo(versionPart, url ?? ReleasesPageUrl) : null;
        }
        catch
        {
            return null;
        }
    }
}
