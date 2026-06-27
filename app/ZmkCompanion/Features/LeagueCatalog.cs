using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZmkCompanion.Features;

public static class LeagueCatalog
{
    private const string CoreBase = "https://sports.core.api.espn.com/v2/sports";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZmkCompanion", "leagues_cache.json");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // Returns leagues for the given sport from cache or ESPN Core API.
    // Progress reports 0-100. Returns partial results on failure.
    public static async Task<List<SportsLeague>> GetLeaguesAsync(
        SportKind sport, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var cached = TryLoadCache(sport);
        if (cached != null) { progress?.Report(100); return cached; }
        return await FetchAndCacheAsync(sport, progress, ct);
    }

    public static void InvalidateCache()
    {
        try { File.Delete(CachePath); } catch { }
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    private static List<SportsLeague>? TryLoadCache(SportKind sport)
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var root = JsonSerializer.Deserialize<JsonObject>(File.ReadAllText(CachePath));
            if (root is null) return null;

            var entry = root[SportKey(sport)];
            if (entry is null) return null;

            string? fetchedAt = entry["fetchedAt"]?.GetValue<string>();
            if (fetchedAt is null ||
                !DateTime.TryParse(fetchedAt, out var dt) ||
                DateTime.UtcNow - dt > CacheTtl)
                return null;

            var arr = entry["leagues"]?.AsArray();
            if (arr is null) return null;

            var result = new List<SportsLeague>();
            foreach (var item in arr)
            {
                string? path     = item?["espnPath"]?.GetValue<string>();
                string? name     = item?["displayName"]?.GetValue<string>();
                string? abbr     = item?["abbreviation"]?.GetValue<string>() ?? "";
                bool    weekBased = item?["weekBased"]?.GetValue<bool>() ?? false;
                if (path is null || name is null) continue;
                result.Add(new SportsLeague(path, name, abbr, sport, weekBased));
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    private static void SaveCache(SportKind sport, List<SportsLeague> leagues)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

            JsonObject root = new();
            if (File.Exists(CachePath))
            {
                try { root = JsonSerializer.Deserialize<JsonObject>(File.ReadAllText(CachePath)) ?? new(); }
                catch { root = new(); }
            }

            var arr = new JsonArray();
            foreach (var l in leagues)
                arr.Add(new JsonObject
                {
                    ["espnPath"]     = l.EspnPath,
                    ["displayName"]  = l.DisplayName,
                    ["abbreviation"] = l.ShortName,
                    ["weekBased"]    = l.WeekBased,
                });

            root[SportKey(sport)] = new JsonObject
            {
                ["fetchedAt"] = DateTime.UtcNow.ToString("O"),
                ["leagues"]   = arr,
            };

            File.WriteAllText(CachePath, JsonSerializer.Serialize(root, JsonOpts));
        }
        catch { }
    }

    // ── ESPN Core API ─────────────────────────────────────────────────────────

    private static async Task<List<SportsLeague>> FetchAndCacheAsync(
        SportKind sport, IProgress<int>? progress, CancellationToken ct)
    {
        string sportSlug = SportSlug(sport);

        // Phase 1: collect $ref URLs via paginated list endpoint
        var refs = new List<string>();
        int page = 1, pageCount = 1;
        do
        {
            string url  = $"{CoreBase}/{sportSlug}/leagues?limit=100&page={page}";
            var    data = await GetJsonAsync(url, ct);
            if (data is null) break;
            pageCount = data["pageCount"]?.GetValue<int>() ?? 1;
            var items = data["items"]?.AsArray();
            if (items != null)
                foreach (var item in items)
                {
                    var r = item?["$ref"]?.GetValue<string>();
                    if (r != null) refs.Add(r);
                }
            page++;
        } while (page <= pageCount && !ct.IsCancellationRequested);

        // Phase 2: follow each $ref in parallel batches of 20
        var leagues = new List<SportsLeague>();
        int batchSize = 20;
        for (int i = 0; i < refs.Count && !ct.IsCancellationRequested; i += batchSize)
        {
            var batch = refs.Skip(i).Take(batchSize);
            var tasks = batch.Select(async r =>
            {
                var data = await GetJsonAsync(r, ct);
                if (data is null) return null;
                string? slug = ExtractSlug(r);
                string? name = data["name"]?.GetValue<string>();
                string? abbr = data["abbreviation"]?.GetValue<string>();
                if (slug is null || name is null) return null;
                string espnPath = $"{sportSlug}/{slug}";
                bool weekBased  = sport == SportKind.Football && slug == "nfl";
                return new SportsLeague(espnPath, name, abbr ?? slug, sport, weekBased);
            });
            var results = await Task.WhenAll(tasks);
            leagues.AddRange(results.Where(r => r != null)!);
            progress?.Report(Math.Min(99, (i + batchSize) * 100 / refs.Count));
        }

        var sorted = leagues.OrderBy(l => l.DisplayName).ToList();
        if (sorted.Count > 0) SaveCache(sport, sorted);
        progress?.Report(100);
        return sorted;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string SportKey(SportKind s)  => s.ToString().ToLower();

    private static string SportSlug(SportKind s) => s switch
    {
        SportKind.Soccer     => "soccer",
        SportKind.Basketball => "basketball",
        SportKind.Hockey     => "hockey",
        _                    => "football",
    };

    private static string? ExtractSlug(string refUrl)
    {
        // "…/v2/sports/soccer/leagues/eng.1" → "eng.1"
        try { return new Uri(refUrl).Segments.Last().TrimEnd('/'); }
        catch { return null; }
    }

    private static async Task<JsonObject?> GetJsonAsync(string url, CancellationToken ct)
    {
        try { return await Http.GetFromJsonAsync<JsonObject>(url, ct); }
        catch { return null; }
    }
}
