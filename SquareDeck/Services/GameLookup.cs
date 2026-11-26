using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

/// <summary>
/// Details for games that aren't from Steam (added by hand, or from Epic, GOG, Xbox and the rest): the same game's
/// Steam store page when it's sold there too (description, screenshots, trailers, genres, reviews), or else its
/// Wikipedia summary. Matches are remembered on disk, so a game is looked up once.
/// </summary>
public static class GameLookup
{
    private static readonly HttpClient Http = CreateClient();
    private static string CacheFile => Path.Combine(Settings.DataDir, "store", "matches.json");
    private static Dictionary<string, int>? _matches;
    private static readonly object Lock = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Wikipedia asks every program to say who it is.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/5 (https://github.com/tetraguy/SquareDeck)");
        return c;
    }

    /// <summary>A title reduced to letters and digits, without ™ ® and edition words, for comparing names.</summary>
    public static string Simplify(string name)
    {
        var s = name.ToLowerInvariant().Replace("&", "and");
        s = Regex.Replace(s, @"[™®©]", "");
        s = Regex.Replace(s, @"\b(the|game of the year|goty|definitive|deluxe|complete|enhanced|remastered|ultimate|standard|digital)\b( edition)?", " ");
        s = Regex.Replace(s, @"\bedition\b", " ");
        return Regex.Replace(s, @"[^a-z0-9]", "");
    }

    /// <summary>
    /// The Steam app id of the same game, when the Steam store sells it under (nearly) the same name. 0 when it doesn't.
    /// </summary>
    public static async Task<int> FindSteamMatchAsync(string name, CancellationToken ct)
    {
        string key = Simplify(name);
        if (key.Length == 0) return 0;
        lock (Lock)
        {
            _matches ??= Load();
            if (_matches.TryGetValue(key, out var known)) return known;
        }
        var hits = await SteamStoreClient.SearchStoreAsync(name, ct).ConfigureAwait(false);
        int id = hits.FirstOrDefault(h => Simplify(h.Name) == key).AppId;
        // Close enough: one name starts with the other ("Hades" / "Hades II" doesn't count: digits must match too).
        if (id == 0)
            id = hits.FirstOrDefault(h =>
            {
                var other = Simplify(h.Name);
                return other.Length > 3 && key.Length > 3 && (other.StartsWith(key) || key.StartsWith(other)) &&
                       !char.IsDigit(other[^1]) && !char.IsDigit(key[^1]);
            }).AppId;
        if (hits.Count > 0 || id != 0) // nothing found because we're offline isn't remembered
        {
            lock (Lock)
            {
                _matches![key] = id;
                Save();
            }
        }
        return id;
    }

    private static Dictionary<string, int> Load()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(CacheFile)) ?? new(); }
        catch (Exception) { return new(); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(_matches));
        }
        catch (Exception) { }
    }

    public sealed record WikiSummary(string Title, string Description, string Extract, string Url);

    /// <summary>The Wikipedia article about the game, if one is easy to find: its short description and first paragraphs.</summary>
    public static async Task<WikiSummary?> GetWikipediaAsync(string name, CancellationToken ct)
    {
        try
        {
            string search = "https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=5&srsearch=" +
                            Uri.EscapeDataString(name + " video game");
            using var found = JsonDocument.Parse(await Http.GetStringAsync(search, ct).ConfigureAwait(false));
            var titles = found.RootElement.GetProperty("query").GetProperty("search").EnumerateArray()
                .Select(e => e.GetProperty("title").GetString() ?? "").Where(t => t.Length > 0).ToList();
            string key = Simplify(name);
            // Prefer an article named like the game; take only articles about a game.
            foreach (var title in titles.OrderByDescending(t => Simplify(Regex.Replace(t, @"\s*\(.*\)$", "")) == key))
            {
                var summaryUrl = "https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(summaryUrl, ct).ConfigureAwait(false));
                var root = doc.RootElement;
                string description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                string extract = root.TryGetProperty("extract", out var x) ? x.GetString() ?? "" : "";
                bool aboutGame = description.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                                 extract.Contains("video game", StringComparison.OrdinalIgnoreCase);
                bool named = Simplify(Regex.Replace(title, @"\s*\(.*\)$", "")).Contains(key) || key.Contains(Simplify(Regex.Replace(title, @"\s*\(.*\)$", "")));
                if (!aboutGame || !named || extract.Length < 40) continue;
                string url = root.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var dt) &&
                             dt.TryGetProperty("page", out var pg) ? pg.GetString() ?? "" : "";
                return new WikiSummary(title, description, extract, url);
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>How much space a folder takes, counted on a background thread (stops after a few seconds on huge folders).</summary>
    public static long FolderSize(string folder, CancellationToken ct)
    {
        long total = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                ct.ThrowIfCancellationRequested();
                total += file.Length;
                if (watch.Elapsed.TotalSeconds > 8) return -total; // negative: "at least"
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        return total;
    }
}
