using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace SquareDeck.Services;

public sealed record Screenshot(string Thumb, string Full);

/// <summary>A store trailer. Url is null when Steam only offers it as an adaptive stream we can't play in-app.</summary>
public sealed record Trailer(string Name, string? Url)
{
    /// <summary>The adaptive stream (HLS) Steam serves newer trailers as, when there's no plain video file.</summary>
    public string? Stream { get; init; }
}

/// <summary>One post from a game's Steam news feed (announcements, patch notes).</summary>
/// <param name="Body">The whole post as plain text, for reading inside the launcher.</param>
public sealed record NewsItem(string Title, string Url, string Snippet, DateTime Date, string Feed, string Body,
    string[]? Tags = null, int AppId = 0, string FeedName = "", string Image = "")
{
    /// <summary>Patch notes or an update, by Steam's own tag or the usual words in the title.</summary>
    public bool IsPatch =>
        (Tags?.Contains("patchnotes") ?? false) ||
        System.Text.RegularExpressions.Regex.IsMatch(Title, @"\b(patch|hotfix|update|changelog|v\d+(\.\d+)+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Posted by the developer on Steam (an announcement), rather than picked up from a news site.</summary>
    public bool IsAnnouncement => FeedName == "steam_community_announcements";
}

/// <summary>A game on sale in the Steam store.</summary>
public sealed record SaleItem(int AppId, string Name, int DiscountPercent, string OldPrice, string Price, string Image, DateTime? Ends);

public sealed record Achievement(string Name, string Description, string Icon, bool? Unlocked);

/// <summary>Store-page information for one game.</summary>
public sealed class StoreDetails
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Developers { get; init; } = "";
    public string Publishers { get; init; } = "";
    public string Genres { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string? HeaderImage { get; init; }
    public int AchievementTotal { get; init; }
    public int? Metacritic { get; init; }
    public List<Screenshot> Screenshots { get; init; } = new();
    public List<Trailer> Trailers { get; init; } = new();

    /// <summary>App ids of every DLC the store lists for the game.</summary>
    public List<int> DlcIds { get; init; } = new();

    /// <summary>The handful of achievements the store page highlights (names and icons only).</summary>
    public List<Achievement> HighlightedAchievements { get; init; } = new();
}

/// <summary>Game details from the public Steam store API (no key needed), cached on disk for a week.</summary>
public static class SteamStoreClient
{
    private static readonly HttpClient Http = CreateClient();
    private static string CacheDir => Path.Combine(Settings.DataDir, "store");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/1.1");
        return c;
    }

    public static async Task<StoreDetails?> GetDetailsAsync(int appId, CancellationToken ct)
    {
        var cache = Path.Combine(CacheDir, appId + ".json");
        string? json = null;
        try
        {
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(7))
                json = await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false);
        }
        catch (IOException) { }

        if (json == null)
        {
            try
            {
                json = await Http.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Offline: fall back to a stale cache if there is one.
                if (File.Exists(cache)) json = await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false);
                else return null;
            }
        }

        StoreDetails? details;
        try { details = Parse(json, appId); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }

        if (details != null)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(cache, json, ct).ConfigureAwait(false);
            }
            catch (IOException) { }
        }
        return details;
    }

    private static StoreDetails? Parse(string json, int appId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry)) return null;
        if (!entry.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
        if (!entry.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;

        var shots = new List<Screenshot>();
        if (d.TryGetProperty("screenshots", out var ss) && ss.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in ss.EnumerateArray())
            {
                var thumb = Str(s, "path_thumbnail");
                var full = Str(s, "path_full");
                if (thumb.Length > 0) shots.Add(new Screenshot(thumb, full.Length > 0 ? full : thumb));
            }
        }

        int achievements = 0;
        var highlighted = new List<Achievement>();
        if (d.TryGetProperty("achievements", out var ach) && ach.ValueKind == JsonValueKind.Object)
        {
            if (ach.TryGetProperty("total", out var total) && total.TryGetInt32(out var t)) achievements = t;
            if (ach.TryGetProperty("highlighted", out var hl) && hl.ValueKind == JsonValueKind.Array)
                foreach (var h in hl.EnumerateArray())
                    if (Str(h, "name").Length > 0) highlighted.Add(new Achievement(Str(h, "name"), "", Str(h, "path"), null));
        }

        var trailers = new List<Trailer>();
        if (d.TryGetProperty("movies", out var movies) && movies.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in movies.EnumerateArray())
            {
                string? url = null;
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("mp4", out var mp4) &&
                    mp4.ValueKind == JsonValueKind.Object)
                {
                    var best = Str(mp4, "max");
                    if (best.Length == 0) best = Str(mp4, "480");
                    if (best.Length > 0) url = best.Replace("http://", "https://");
                }
                string? stream = m.ValueKind == JsonValueKind.Object ? Str(m, "hls_h264") : null;
                trailers.Add(new Trailer(Str(m, "name"), url) { Stream = string.IsNullOrEmpty(stream) ? null : stream!.Replace("http://", "https://") });
            }
        }

        int? metacritic = null;
        if (d.TryGetProperty("metacritic", out var mc) && mc.ValueKind == JsonValueKind.Object &&
            mc.TryGetProperty("score", out var score) && score.TryGetInt32(out var sc)) metacritic = sc;

        string release = "";
        if (d.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.Object)
            release = Str(rd, "date");

        var genres = new List<string>();
        if (d.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array)
            foreach (var g in gs.EnumerateArray()) genres.Add(Str(g, "description"));

        var header = Str(d, "header_image");
        return new StoreDetails
        {
            Name = Str(d, "name"),
            Description = WebUtility.HtmlDecode(Str(d, "short_description")),
            Developers = JoinArray(d, "developers"),
            Publishers = JoinArray(d, "publishers"),
            Genres = string.Join(", ", genres.Where(g => g.Length > 0)),
            ReleaseDate = release,
            HeaderImage = header.Length > 0 ? header : null,
            AchievementTotal = achievements,
            Metacritic = metacritic,
            Screenshots = shots,
            Trailers = trailers,
            DlcIds = d.TryGetProperty("dlc", out var dlcList) && dlcList.ValueKind == JsonValueKind.Array
                ? dlcList.EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList()
                : new List<int>(),
            HighlightedAchievements = highlighted
        };
    }

    private static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static string JoinArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
    }

    private static readonly Dictionary<int, string> AppNames = new();

    /// <summary>The store name of any app id (used for DLC). Null if Steam doesn't say.</summary>
    public static async Task<string?> GetAppNameAsync(int appId, CancellationToken ct)
    {
        lock (AppNames) if (AppNames.TryGetValue(appId, out var known)) return known;
        try
        {
            var json = await Http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=english", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry) ||
                !entry.TryGetProperty("data", out var data)) return null;
            var name = Str(data, "name");
            if (name.Length == 0) return null;
            lock (AppNames) AppNames[appId] = name;
            return name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Titles of Steam Workshop items, in the order asked for. Unknown ones come back as their id.</summary>
    public static async Task<List<string>> GetWorkshopTitlesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var titles = ids.ToList();
        try
        {
            var form = new Dictionary<string, string> { ["itemcount"] = ids.Count.ToString() };
            for (int i = 0; i < ids.Count; i++) form[$"publishedfileids[{i}]"] = ids[i];
            using var resp = await Http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("response", out var r) ||
                !r.TryGetProperty("publishedfiledetails", out var list)) return titles;
            foreach (var item in list.EnumerateArray())
            {
                int index = ids.ToList().IndexOf(Str(item, "publishedfileid"));
                var title = Str(item, "title");
                if (index >= 0 && title.Length > 0) titles[index] = title;
            }
        }
        catch (Exception) { }
        return titles;
    }

    /// <summary>Steam's review verdict, e.g. ("Very Positive", 92, 14000). Null if there are no reviews.</summary>
    public static async Task<(string Verdict, int Percent, int Total)?> GetReviewSummaryAsync(int appId, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://store.steampowered.com/appreviews/{appId}?json=1&language=all&purchase_type=all&num_per_page=0",
                ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query_summary", out var q)) return null;
            int total = q.TryGetProperty("total_reviews", out var t) && t.TryGetInt32(out var tv) ? tv : 0;
            int positive = q.TryGetProperty("total_positive", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
            var verdict = Str(q, "review_score_desc");
            if (total <= 0 || verdict.Length == 0) return null;
            return (verdict, (int)Math.Round(positive * 100.0 / total), total);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Every game on the account (needs the user's Steam Web API key): app id and name.</summary>
    public static async Task<List<(int AppId, string Name)>?> GetOwnedGamesAsync(string apiKey, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null || string.IsNullOrWhiteSpace(apiKey)) return null;
        try
        {
            var json = await Http.GetStringAsync(
                "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?include_appinfo=1&include_played_free_games=1" +
                $"&key={Uri.EscapeDataString(apiKey.Trim())}&steamid={steamId}", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var games = new List<(int, string)>();
            if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("games", out var list))
                foreach (var g in list.EnumerateArray())
                    if (g.TryGetProperty("appid", out var id) && id.TryGetInt32(out var appId) && Str(g, "name").Length > 0)
                        games.Add((appId, Str(g, "name")));
            return games;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The latest posts from a game's Steam news feed. Empty if there are none or Steam can't be reached.</summary>
    public static async Task<List<NewsItem>> GetNewsAsync(int appId, int count, CancellationToken ct)
    {
        var items = new List<NewsItem>();
        try
        {
            var json = await Http.GetStringAsync(
                $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count={count}",
                ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("appnews", out var news) ||
                !news.TryGetProperty("newsitems", out var list) || list.ValueKind != JsonValueKind.Array) return items;

            foreach (var n in list.EnumerateArray())
            {
                var title = Str(n, "title");
                var url = Str(n, "url");
                if (title.Length == 0 || url.Length == 0) continue;
                long unix = n.TryGetProperty("date", out var d) && d.TryGetInt64(out var u) ? u : 0;
                var raw = Str(n, "contents");
                var body = CleanArticle(raw);
                var flat = CleanText(raw);
                var tags = n.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : null;
                // The first picture in the post, if any (Steam writes {STEAM_CLAN_IMAGE} for its own image host).
                var image = System.Text.RegularExpressions.Regex.Match(raw, @"\[img\](.*?)\[/img\]|<img[^>]+src=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                string picture = image.Success ? (image.Groups[1].Success && image.Groups[1].Value.Length > 0 ? image.Groups[1].Value : image.Groups[2].Value) : "";
                picture = picture.Replace("{STEAM_CLAN_IMAGE}", "https://clan.akamai.steamstatic.com/images");
                if (!picture.StartsWith("http", StringComparison.OrdinalIgnoreCase)) picture = "";
                items.Add(new NewsItem(
                    WebUtility.HtmlDecode(title), url, flat.Length > 300 ? flat[..300] + "\u2026" : flat,
                    DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime, Str(n, "feedlabel"), body,
                    tags, appId, Str(n, "feedname"), picture));
            }
        }
        catch (Exception)
        {
        }
        return items;
    }

    /// <summary>Steam store search by name: app id, name, price text and a small picture.</summary>
    public static async Task<List<(int AppId, string Name, string Price, string Image)>> SearchStoreAsync(string term, CancellationToken ct)
    {
        var items = new List<(int, string, string, string)>();
        try
        {
            var json = await Http.GetStringAsync(
                "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(term), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array) return items;
            foreach (var e in list.EnumerateArray())
            {
                int id = e.TryGetProperty("id", out var i) && i.TryGetInt32(out var v) ? v : 0;
                if (id == 0) continue;
                string price = "";
                if (e.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("final", out var f) && f.TryGetInt64(out var cents))
                    price = FormatPrice(cents, Str(p, "currency"));
                else if (e.TryGetProperty("price", out _) == false) price = "Free";
                items.Add((id, WebUtility.HtmlDecode(Str(e, "name")), price, Str(e, "tiny_image")));
            }
        }
        catch (Exception) { }
        return items;
    }

    /// <summary>Games on sale on the Steam store's front page ("specials"). Empty if the store didn't answer.</summary>
    public static async Task<List<SaleItem>> GetSpecialsAsync(CancellationToken ct)
    {
        var items = new List<SaleItem>();
        try
        {
            var json = await Http.GetStringAsync("https://store.steampowered.com/api/featuredcategories/?l=english", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("specials", out var specials) || !specials.TryGetProperty("items", out var list)) return items;
            foreach (var e in list.EnumerateArray())
            {
                int id = e.TryGetProperty("id", out var i) && i.TryGetInt32(out var v) ? v : 0;
                int discount = e.TryGetProperty("discount_percent", out var dp) && dp.TryGetInt32(out var dv) ? dv : 0;
                if (id == 0 || discount <= 0) continue;
                string currency = Str(e, "currency");
                string Money(string field) => e.TryGetProperty(field, out var m) && m.TryGetInt64(out var cents)
                    ? FormatPrice(cents, currency) : "";
                long ends = e.TryGetProperty("discount_expiration", out var de) && de.TryGetInt64(out var ev) ? ev : 0;
                string image = Str(e, "large_capsule_image");
                if (image.Length == 0) image = Str(e, "header_image");
                items.Add(new SaleItem(id, WebUtility.HtmlDecode(Str(e, "name")), discount, Money("original_price"), Money("final_price"),
                    image, ends > 0 ? DateTimeOffset.FromUnixTimeSeconds(ends).LocalDateTime : null));
            }
        }
        catch (Exception) { }
        return items.GroupBy(x => x.AppId).Select(g => g.First()).ToList();
    }

    private static string FormatPrice(long cents, string currency)
    {
        decimal amount = cents / 100m;
        return currency switch
        {
            "USD" => amount.ToString("$0.00"),
            "EUR" => amount.ToString("0.00") + "\u20AC",
            "GBP" => "\u00A3" + amount.ToString("0.00"),
            "" => amount.ToString("0.00"),
            _ => amount.ToString("0.00") + " " + currency
        };
    }

    /// <summary>
    /// Turns a news post's HTML / Steam [bbcode] into plain paragraphs that can be read in the launcher:
    /// headings and list items keep their own lines, everything else is flowed text.
    /// </summary>
    public static string CleanArticle(string raw)
    {
        var rx = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        string R(string input, string pattern, string with) => System.Text.RegularExpressions.Regex.Replace(input, pattern, with, rx);

        var text = R(raw, @"\{STEAM_CLAN_IMAGE\}\S*", "");
        text = R(text, @"\[img\].*?\[/img\]|<img[^>]*>|\[previewyoutube[^\]]*\]\[/previewyoutube\]", "");
        text = R(text, @"\[\*\]|<li[^>]*>", "\n• ");
        text = R(text, @"<br\s*/?>|</p>|\[/p\]|</div>|</li>|\[/list\]|\[/olist\]|</ul>|</ol>", "\n");
        text = R(text, @"\[h[1-6]\]|<h[1-6][^>]*>", "\n\n");
        text = R(text, @"\[/h[1-6]\]|</h[1-6]>", "\n");
        text = R(text, @"\[url=([^\]]+)\](.*?)\[/url\]", "$2");
        text = R(text, @"<[^>]+>|\[/?[^\]\n]{1,60}\]", "");
        text = WebUtility.HtmlDecode(text);
        text = R(text, @"[ \t]+", " ");
        text = R(text, @" ?\n ?", "\n");
        text = R(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    /// <summary>Strips HTML tags, Steam's [bbcode] and image placeholders from a news excerpt.</summary>
    public static string CleanText(string raw)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(raw, @"\{STEAM_CLAN_IMAGE\}\S*", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>|\[/?[^\]\n]{1,60}\]", " ");
        text = WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// The user's full achievement list (names, descriptions, icons, unlocked or not) from their Steam
    /// community page. Only works when their profile's game details are public; null otherwise.
    /// </summary>
    public static async Task<List<Achievement>?> GetAchievementsAsync(int appId, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null) return null;
        try
        {
            var xml = await Http.GetStringAsync(
                $"https://steamcommunity.com/profiles/{steamId}/stats/{appId}/?xml=1", ct).ConfigureAwait(false);
            if (!xml.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return null;

            var list = new List<Achievement>();
            foreach (var a in XDocument.Parse(xml).Descendants("achievement"))
            {
                bool unlocked = (string?)a.Attribute("closed") == "1";
                var icon = ((string?)a.Element(unlocked ? "iconClosed" : "iconOpen") ?? "").Trim();
                list.Add(new Achievement(
                    ((string?)a.Element("name") ?? "").Trim(),
                    ((string?)a.Element("description") ?? "").Trim(),
                    icon, unlocked));
            }
            return list.Count == 0 ? null : list;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Unlocked/total achievements, or null when the profile isn't public.</summary>
    public static async Task<(int Unlocked, int Total)?> GetAchievementProgressAsync(int appId, CancellationToken ct)
    {
        var all = await GetAchievementsAsync(appId, ct).ConfigureAwait(false);
        return all == null ? null : (all.Count(a => a.Unlocked == true), all.Count);
    }
}
