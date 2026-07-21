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

    // Returns the latest release if it's newer than the running build, or
    // null if already current. Deliberately does NOT swallow exceptions
    // (offline, a proxy blocking it, GitHub's rate limit, a malformed
    // response) the way WeatherFeature/SportsFeature's own network calls
    // do - those features fail silently because there's always other
    // content to show instead. Here, the caller needs to tell "checked,
    // you're current" apart from "couldn't check at all", especially for
    // the manual "Check for updates…" action, so this throws and leaves
    // that distinction to the caller instead of collapsing both into null.
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var resp = await Http.GetAsync(ReleasesApiUrl);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        string? tag = json?["tag_name"]?.GetValue<string>();
        string? url = json?["html_url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(tag))
            throw new InvalidOperationException("GitHub release response had no tag_name.");

        string versionPart = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Version.TryParse(versionPart, out var latest))
            throw new InvalidOperationException($"Could not parse release tag as a version: '{tag}'.");

        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(0, 0, 0);
        return latest > current ? new UpdateInfo(versionPart, url ?? ReleasesPageUrl) : null;
    }
}
