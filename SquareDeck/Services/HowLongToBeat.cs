using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

/// <summary>How long a game takes to finish, from HowLongToBeat.com (hours; 0 when there's no data for that style).</summary>
public sealed record HltbTimes(int Id, string Name, double Main, double Extra, double Complete, double AllStyles, string? ImageUrl)
{
    public string PageUrl => "https://howlongtobeat.com/game/" + Id;
    public bool Any => Main > 0 || Extra > 0 || Complete > 0 || AllStyles > 0;
}

/// <summary>
/// Looks games up on HowLongToBeat.com, the way the howlongtobeat projects do (ckatzorke/howlongtobeat for the search
/// and its fields; the actively kept HowLongToBeat-PythonAPI for the site's current steps):
/// <list type="number">
/// <item>The search address is read from the site's own scripts (it changes now and then): the one sent with POST.</item>
/// <item>A short-lived token comes from that address + "/init".</item>
/// <item>The search is posted with the token (and the extra key and value it names), and the best match is picked:
/// the same Steam app when the site lists one, otherwise the closest name.</item>
/// </list>
/// Results are kept in hltb.json: a game's page reads it from there, and the site is only asked again after a month
/// (or a few days when it wasn't found). Nothing is kept when the site can't be reached, so it's tried again next time.
/// </summary>
public static class HowLongToBeat
{
    private const string Base = "https://howlongtobeat.com/";
    private static readonly TimeSpan KeepFound = TimeSpan.FromDays(30);
    private static readonly TimeSpan KeepMissing = TimeSpan.FromDays(4);

    private static readonly HttpClient Http = MakeClient();
    private static readonly SemaphoreSlim Gate = new(1, 1); // one lookup at a time, gentle on the site
    private static readonly object Lock = new();
    private static Dictionary<string, Entry>? _cache;
    private static string? _searchPath;
    private static DateTime _searchPathAt;

    private sealed class Entry
    {
        public HltbTimes? Times { get; set; }
        public DateTime Checked { get; set; }
    }

    private static string FilePath => Path.Combine(Settings.DataDir, "hltb.json");

    private static HttpClient MakeClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // The site turns away requests that don't look like a browser.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri(Base);
        return http;
    }

    private static Dictionary<string, Entry> Cache
    {
        get
        {
            lock (Lock)
            {
                if (_cache != null) return _cache;
                try { _cache = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) : null; }
                catch (Exception) { }
                return _cache ??= new();
            }
        }
    }

    private static void Save()
    {
        try
        {
            string json;
            lock (Lock) json = JsonSerializer.Serialize(Cache);
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception) { }
    }

    /// <summary>What's kept for a game: (times or null when it wasn't found, true when it was looked up at all).</summary>
    public static (HltbTimes? Times, bool Known, DateTime Checked) Cached(Game game)
    {
        lock (Lock)
            return Cache.TryGetValue(game.Key, out var e) ? (e.Times, true, e.Checked) : (null, false, default);
    }

    /// <summary>Whether what's kept is old enough to ask the site again.</summary>
    public static bool IsStale(Game game)
    {
        var (times, known, when) = Cached(game);
        return !known || DateTime.UtcNow - when > (times != null ? KeepFound : KeepMissing);
    }

    /// <summary>Forgets every kept result (Settings).</summary>
    public static void Clear()
    {
        lock (Lock) Cache.Clear();
        try { File.Delete(FilePath); } catch (Exception) { }
    }

    /// <summary>
    /// The game's times: kept ones when they're recent, otherwise looked up (and kept). Null when it isn't on the site.
    /// Throws when the site can't be reached (then whatever was kept, if anything, is still there for next time).
    /// </summary>
    public static async Task<HltbTimes?> GetAsync(Game game, CancellationToken ct)
    {
        if (!IsStale(game)) return Cached(game).Times;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsStale(game)) return Cached(game).Times;
            string name = game.OriginalName.Length > 0 ? game.OriginalName : game.Name;
            var found = await FindAsync(name, game.IsSteam ? game.AppId : 0, ct).ConfigureAwait(false);
            // A long name with a subtitle may be listed under its main title ("Title: Subtitle Edition").
            if (found == null && Regex.Match(name, @"^(.{3,}?)\s*[:\-–]\s") is { Success: true } m)
                found = await FindAsync(m.Groups[1].Value, game.IsSteam ? game.AppId : 0, ct).ConfigureAwait(false);
            lock (Lock) Cache[game.Key] = new Entry { Times = found, Checked = DateTime.UtcNow };
            Save();
            return found;
        }
        finally { Gate.Release(); }
    }

    // ------------------------------------------------------------------ the site

    private static async Task<HltbTimes?> FindAsync(string name, int steamAppId, CancellationToken ct)
    {
        string terms = Clean(name);
        if (terms.Length == 0) return null;
        JsonNode? results;
        try { results = await SearchAsync(terms, ct).ConfigureAwait(false); }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            // The address or token may have just changed: read them again once.
            _searchPath = null;
            results = await SearchAsync(terms, ct).ConfigureAwait(false);
        }
        if (results?["data"] is not JsonArray data) return null;

        HltbTimes? best = null;
        double bestScore = 0;
        string wanted = Normalize(name);
        foreach (var item in data)
        {
            if (item == null) continue;
            int id = Int(item["game_id"]);
            string title = Str(item["game_name"]);
            if (id <= 0 || title.Length == 0) continue;
            double score;
            if (steamAppId > 0 && Int(item["profile_steam"]) == steamAppId) score = 2; // the same Steam game: certain
            else
            {
                score = Similarity(wanted, Normalize(title));
                foreach (var alias in Str(item["game_alias"]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    score = Math.Max(score, Similarity(wanted, Normalize(alias)));
            }
            if (score <= bestScore) continue; // results come most popular first: on a tie the popular one stays
            bestScore = score;
            string image = Str(item["game_image"]);
            best = new HltbTimes(id, title, Hours(item["comp_main"]), Hours(item["comp_plus"]), Hours(item["comp_100"]), Hours(item["comp_all"]),
                image.Length > 0 ? Base + "games/" + image : null);
        }
        return bestScore >= 0.62 && best is { Any: true } ? best : null;
    }

    private static async Task<JsonNode?> SearchAsync(string terms, CancellationToken ct)
    {
        string path = await SearchPathAsync(ct).ConfigureAwait(false);

        // The token, and the extra key and value the site wants back with the search.
        string? token = null, key = null, value = null;
        try
        {
            var init = await Http.GetStringAsync(Base + path + "/init?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
            if (JsonNode.Parse(init) is JsonObject auth)
                foreach (var (field, v) in auth)
                {
                    string text = v is JsonValue ? v.ToString() : "";
                    string lower = field.ToLowerInvariant();
                    if (lower == "token") token = text;
                    else if (lower.Contains("key")) key = text;
                    else if (lower.Contains("val")) value = text;
                }
        }
        catch (HttpRequestException) { } // older versions of the site had no token: the search may still work

        var body = new JsonObject
        {
            ["searchType"] = "games",
            ["searchTerms"] = new JsonArray(terms.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => (JsonNode)t).ToArray()),
            ["searchPage"] = 1,
            ["size"] = 20,
            ["searchOptions"] = new JsonObject
            {
                ["games"] = new JsonObject
                {
                    ["userId"] = 0, ["platform"] = "", ["sortCategory"] = "popular", ["rangeCategory"] = "main",
                    ["rangeTime"] = new JsonObject { ["min"] = 0, ["max"] = 0 },
                    ["gameplay"] = new JsonObject { ["perspective"] = "", ["flow"] = "", ["genre"] = "", ["difficulty"] = "" },
                    ["rangeYear"] = new JsonObject { ["max"] = "", ["min"] = "" },
                    ["modifier"] = "hide_dlc"
                },
                ["users"] = new JsonObject { ["sortCategory"] = "postcount" },
                ["lists"] = new JsonObject { ["sortCategory"] = "follows" },
                ["filter"] = "", ["sort"] = 0, ["randomizer"] = 0
            },
            ["useCache"] = true
        };
        if (!string.IsNullOrEmpty(key) && value != null) body[key] = value;

        using var request = new HttpRequestMessage(HttpMethod.Post, Base + path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Origin", Base);
        if (token != null) request.Headers.TryAddWithoutValidation("x-auth-token", token);
        if (key != null) request.Headers.TryAddWithoutValidation("x-hp-key", key);
        if (value != null) request.Headers.TryAddWithoutValidation("x-hp-val", value);
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The search address ("api/…"), found in the site's scripts: the fetch call sent with method POST. Kept for a few
    /// hours. "api/s" (the last one known) when it can't be found.
    /// </summary>
    private static async Task<string> SearchPathAsync(CancellationToken ct)
    {
        if (_searchPath != null && DateTime.UtcNow - _searchPathAt < TimeSpan.FromHours(6)) return _searchPath;
        string found = "api/s";
        try
        {
            var html = await Http.GetStringAsync(Base, ct).ConfigureAwait(false);
            var scripts = Regex.Matches(html, @"src=""(/_next/static/chunks/[^""]+\.js)""").Select(m => m.Groups[1].Value).Distinct().ToList();
            // The page's own scripts (_app, pages) are the likely ones: look at those first.
            scripts = scripts.OrderBy(s => s.Contains("/pages/") || s.Contains("_app") ? 0 : 1).Take(40).ToList();
            var post = new Regex(@"fetch\s*\(\s*[""']/api/([a-zA-Z0-9_/]+)[^""']*[""']\s*,\s*\{[^}]*method:\s*[""']POST[""'][^}]*\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (var src in scripts)
            {
                ct.ThrowIfCancellationRequested();
                string js;
                try { js = await Http.GetStringAsync(Base.TrimEnd('/') + src, ct).ConfigureAwait(false); }
                catch (HttpRequestException) { continue; }
                var m = post.Match(js);
                if (!m.Success) continue;
                found = "api/" + m.Groups[1].Value.TrimEnd('/');
                break;
            }
        }
        catch (HttpRequestException) { }
        _searchPath = found;
        _searchPathAt = DateTime.UtcNow;
        return found;
    }

    // ------------------------------------------------------------------ helpers

    private static double Hours(JsonNode? seconds) =>
        seconds is JsonValue v && v.TryGetValue<double>(out var s) && s > 0 ? s / 3600.0 : 0;

    private static int Int(JsonNode? node)
    {
        if (node is not JsonValue v) return 0;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        return int.TryParse(v.ToString(), out i) ? i : 0;
    }

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    /// <summary>The words to search for: symbols such as ™ and ® dropped.</summary>
    private static string Clean(string name) =>
        Regex.Replace(Regex.Replace(name, @"[™®©]", ""), @"\s+", " ").Trim();

    /// <summary>Lower case letters and digits only, single spaces, for comparing names.</summary>
    private static string Normalize(string name) =>
        Regex.Replace(Regex.Replace(Clean(name).ToLowerInvariant().Replace("&", " and "), @"[^\p{L}\p{N}]+", " "), @"\s+", " ").Trim();

    /// <summary>How alike two names are, 0 to 1 (1 minus the edit distance over the longer length).</summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>"12½ Hours", "45 Mins", as the site writes them.</summary>
    public static string Format(double hours)
    {
        if (hours <= 0) return "--";
        if (hours < 1) return $"{Math.Max(1, Math.Round(hours * 60)):0} Mins";
        double whole = Math.Floor(hours);
        double rest = hours - whole;
        // Half hours under 10 hours, whole hours above, the way the site shows them.
        if (hours >= 10) return $"{Math.Round(hours):0} Hours";
        if (rest >= 0.75) return $"{whole + 1:0} Hours";
        return rest >= 0.25 ? $"{whole:0}½ Hours" : $"{whole:0} Hour{(whole == 1 ? "" : "s")}";
    }
}
