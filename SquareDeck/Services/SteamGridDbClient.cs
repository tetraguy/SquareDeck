using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SquareDeck.Services;

public sealed record GameInfo(string? Name, int? ReleaseYear);

public sealed record ArtResult(string? CoverPath, GameInfo? Info);

/// <summary>One square image offered by SteamGridDB: full-size URL and a small preview.</summary>
public sealed record GridImage(string Url, string Thumb);

/// <summary>
/// Downloads square (1:1) cover art and basic game info from SteamGridDB,
/// looked up by Steam app id, and caches both on disk.
/// </summary>
public sealed class SteamGridDbClient : IDisposable
{
    private const string ApiBase = "https://www.steamgriddb.com/api/v2/";

    // Square grid sizes only, in formats WPF can decode without extra codecs.
    private const string SquareGridQuery =
        "?dimensions=1024x1024,512x512&types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false";

    private static readonly string[] ImageExtensions = { ".png", ".jpg" };

    private readonly HttpClient _http;

    public static string CoverDir { get; } = Path.Combine(Settings.DataDir, "covers");
    public static string InfoDir { get; } = Path.Combine(Settings.DataDir, "info");

    /// <summary>Covers the user picked themselves; never cleared by "re-download cover art".</summary>
    public static string CustomDir { get; } = Path.Combine(Settings.DataDir, "custom");

    public SteamGridDbClient(string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(ApiBase), Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/1.1");
        Directory.CreateDirectory(CoverDir);
        Directory.CreateDirectory(InfoDir);
    }

    /// <summary>Path of the already-downloaded default cover for a Steam game, if any.</summary>
    public static string? FindCachedCover(int appId)
    {
        foreach (var ext in ImageExtensions)
        {
            var cached = Path.Combine(CoverDir, appId + ext);
            if (File.Exists(cached)) return cached;
        }
        return null;
    }

    public static void ClearCache()
    {
        foreach (var dir in new[] { CoverDir, InfoDir, HeroDir, LogoDir, AnimatedDir, IconDir, PortraitDir, HeaderDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Returns the cached cover path and info for a Steam app, downloading whatever is missing.
    /// Throws <see cref="UnauthorizedAccessException"/> if SteamGridDB rejects the API key.
    /// </summary>
    public async Task<ArtResult> GetAsync(int appId, CancellationToken ct)
    {
        var cover = await GetCoverAsync(appId, ct).ConfigureAwait(false);
        var info = await GetInfoAsync(appId, ct).ConfigureAwait(false);
        return new ArtResult(cover, info);
    }

    /// <summary>Just the square cover (cached file, or downloaded); null if SteamGridDB has none.</summary>
    public async Task<string?> GetCoverAsync(int appId, CancellationToken ct)
    {
        foreach (var ext in ImageExtensions)
        {
            var cached = Path.Combine(CoverDir, appId + ext);
            if (File.Exists(cached)) return cached;
        }

        // Marker written when SteamGridDB has no square art, so we don't ask again every start.
        var noneMarker = Path.Combine(CoverDir, appId + ".none");
        if (File.Exists(noneMarker)) return null;

        using var doc = await GetJsonAsync($"grids/steam/{appId}{SquareGridQuery}", ct).ConfigureAwait(false);
        string? url = null;
        if (doc != null && doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            // Results come back best-first; take the first one that really is square.
            foreach (var grid in data.EnumerateArray())
            {
                if (grid.TryGetProperty("width", out var w) && grid.TryGetProperty("height", out var h) &&
                    w.TryGetInt32(out var wi) && h.TryGetInt32(out var hi) && wi != hi) continue;
                if (grid.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } s)
                {
                    url = s;
                    break;
                }
            }
        }

        if (url == null)
        {
            TryWrite(noneMarker, "");
            return null;
        }

        // The address may carry a query string; the file type is in the part before it.
        var plain = url.Split('?', '#')[0];
        var extension = plain.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : plain.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".jpg";
        var path = Path.Combine(CoverDir, appId + extension);
        var temp = path + ".part";

        using (var resp = await ImageHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            if (!resp.IsSuccessStatusCode) return null;
            await using var fs = File.Create(temp);
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private async Task<GameInfo?> GetInfoAsync(int appId, CancellationToken ct)
    {
        var cached = Path.Combine(InfoDir, appId + ".json");
        if (File.Exists(cached))
        {
            try { return JsonSerializer.Deserialize<GameInfo>(await File.ReadAllTextAsync(cached, ct).ConfigureAwait(false)); }
            catch (JsonException) { }
        }

        using var doc = await GetJsonAsync($"games/steam/{appId}", ct).ConfigureAwait(false);
        if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object) return null;

        string? name = data.TryGetProperty("name", out var n) ? n.GetString() : null;
        int? year = null;
        if (data.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.Number &&
            rd.TryGetInt64(out var unix) && unix > 0)
            year = DateTimeOffset.FromUnixTimeSeconds(unix).Year;

        var info = new GameInfo(name, year);
        TryWrite(cached, JsonSerializer.Serialize(info));
        return info;
    }

    /// <summary>Null when the game simply isn't on SteamGridDB (404).</summary>
    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativeUrl, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("SteamGridDB rejected the API key.");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    public static string HeroDir { get; } = Path.Combine(Settings.DataDir, "heroes");

    /// <summary>Short, file-name-safe id for anything that isn't a Steam app id.</summary>
    public static string HashKey(string key) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];

    /// <summary>Path of the already-downloaded default cover for a game from another store, if any.</summary>
    public static string? FindCachedCoverByName(string key)
    {
        foreach (var ext in ImageExtensions)
        {
            var cached = Path.Combine(CoverDir, "x_" + HashKey(key) + ext);
            if (File.Exists(cached)) return cached;
        }
        return null;
    }

    /// <summary>Looks a title up on SteamGridDB. Null if it isn't there.</summary>
    private async Task<int?> FindGameIdAsync(string name, CancellationToken ct)
    {
        using var search = await GetJsonAsync("search/autocomplete/" + Uri.EscapeDataString(name), ct)
            .ConfigureAwait(false);
        if (search == null || !search.RootElement.TryGetProperty("data", out var hits) ||
            hits.ValueKind != JsonValueKind.Array || hits.GetArrayLength() == 0) return null;
        return hits[0].TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) ? id : null;
    }

    /// <summary>What SteamGridDB knows about a title beyond its artwork: its proper name and release date.</summary>
    public async Task<(string Name, DateTime? Released)?> GetGameInfoAsync(string name, CancellationToken ct)
    {
        using var search = await GetJsonAsync("search/autocomplete/" + Uri.EscapeDataString(name), ct).ConfigureAwait(false);
        if (search == null || !search.RootElement.TryGetProperty("data", out var hits) ||
            hits.ValueKind != JsonValueKind.Array || hits.GetArrayLength() == 0) return null;
        var hit = hits[0];
        string title = hit.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
        DateTime? released = hit.TryGetProperty("release_date", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var unix) && unix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : null;
        return (title, released);
    }

    /// <summary>Square cover for a non-Steam game, found by its title. Cached like Steam covers.</summary>
    public async Task<string?> GetCoverByNameAsync(string key, string name, CancellationToken ct)
    {
        var cached = FindCachedCoverByName(key);
        if (cached != null) return cached;

        var basePath = Path.Combine(CoverDir, "x_" + HashKey(key));
        if (File.Exists(basePath + ".none")) return null;

        string? url = null;
        if (await FindGameIdAsync(name, ct).ConfigureAwait(false) is int id)
        {
            using var doc = await GetJsonAsync($"grids/game/{id}{SquareGridQuery}", ct).ConfigureAwait(false);
            url = FirstUrl(doc, squareOnly: true);
        }
        if (url == null)
        {
            TryWrite(basePath + ".none", "");
            return null;
        }
        return await DownloadAsync(url, basePath, ct).ConfigureAwait(false);
    }

    public static string LogoDir { get; } = Path.Combine(Settings.DataDir, "logos");
    public static string PortraitDir { get; } = Path.Combine(Settings.DataDir, "portrait");

    /// <summary>Tall 2:3 cover art (posters), for the poster-shaped UI styles. Null if none exists.</summary>
    public Task<string?> GetPortraitAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "grids", PortraitDir, PortraitQuery, ct);

    public static string HeaderDir { get; } = Path.Combine(Settings.DataDir, "headers");
    private const string HeaderQuery = "?dimensions=460x215,920x430&types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false";

    /// <summary>
    /// Wide "header capsule" art (460×215, like the banner on a Steam store page), for the library's Rectangle view.
    /// Steam games use Steam's own header; others come from SteamGridDB's wide grids.
    /// </summary>
    public async Task<string?> GetHeaderAsync(Game item, CancellationToken ct)
    {
        // One you picked yourself (from SteamGridDB or a file) comes first.
        if (FindChosenArt(item, "headers") is { } chosen) return chosen;
        Directory.CreateDirectory(HeaderDir);
        var basePath = Path.Combine(HeaderDir, HashKey(item.Key));
        foreach (var ext in ImageExtensions)
            if (File.Exists(basePath + ext)) return basePath + ext;
        return await GetArtAsync(item, "grids", HeaderDir, HeaderQuery, ct, useChosen: false).ConfigureAwait(false);
    }

    private const string PortraitQuery = "?dimensions=600x900,342x482,660x930&types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false";
    private const string HeroQuery = "?types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false";
    private const string LogoQuery = "?types=static&mimes=image/png&nsfw=false&humor=false";

    // ---- Artwork the user picked: tall grid ("grids"), hero ("heroes") or logo ("logos").
    // Kept with the user's own images (backed up, never removed by cache clean-up) and used before any download.

    private static string ChosenPrefix(string kind) => kind switch { "heroes" => "hero_", "logos" => "logo_", "headers" => "header_", _ => "grid_" };

    /// <summary>Where a picked image of this kind is saved for a game (plus .png/.jpg).</summary>
    public static string ChosenArtBase(Game item, string kind) => Path.Combine(CustomDir, ChosenPrefix(kind) + HashKey(item.Key));

    /// <summary>The image of this kind the user picked for a game, if any.</summary>
    public static string? FindChosenArt(Game item, string kind)
    {
        var basePath = ChosenArtBase(item, kind);
        foreach (var ext in ImageExtensions)
            if (File.Exists(basePath + ext)) return basePath + ext;
        return null;
    }

    /// <summary>Removes a picked image, so the game goes back to SteamGridDB's default.</summary>
    public static void ClearChosenArt(Game item, string kind)
    {
        var basePath = ChosenArtBase(item, kind);
        foreach (var ext in ImageExtensions.Append(".jpeg").Append(".bmp").Append(".gif"))
        {
            try { if (File.Exists(basePath + ext)) File.Delete(basePath + ext); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Every image SteamGridDB has of one kind ("grids" = tall covers, "heroes", "logos") for a game.</summary>
    public async Task<List<GridImage>> ListArtAsync(Game item, string kind, CancellationToken ct)
    {
        string query = kind switch { "heroes" => HeroQuery, "logos" => LogoQuery, "headers" => HeaderQuery, _ => PortraitQuery };
        // Header capsules are SteamGridDB's wide grids.
        if (kind == "headers") kind = "grids";
        var result = new List<GridImage>();
        void Read(JsonDocument? doc)
        {
            if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
            foreach (var image in data.EnumerateArray())
            {
                var url = image.TryGetProperty("url", out var u) ? u.GetString() : null;
                var thumb = image.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(url) && result.All(r => r.Url != url))
                    result.Add(new GridImage(url, string.IsNullOrEmpty(thumb) ? url : thumb));
            }
        }

        string? endpoint = null;
        if (item.IsSteam) endpoint = $"{kind}/steam/{item.AppId}";
        else if (await FindGameIdAsync(item.OriginalName.Length > 0 ? item.OriginalName : item.Name, ct).ConfigureAwait(false) is int id) endpoint = $"{kind}/game/{id}";
        if (endpoint != null)
            using (var doc = await GetJsonAsync(endpoint + query, ct).ConfigureAwait(false)) Read(doc);
        // Not linked to the Steam id on SteamGridDB: try the title.
        if (result.Count == 0 && item.IsSteam && await FindGameIdAsync(item.OriginalName.Length > 0 ? item.OriginalName : item.Name, ct).ConfigureAwait(false) is int byName)
            using (var doc = await GetJsonAsync($"{kind}/game/{byName}{query}", ct).ConfigureAwait(false)) Read(doc);
        return result;
    }
    public static string AnimatedDir { get; } = Path.Combine(Settings.DataDir, "animated");

    /// <summary>Wide "hero" artwork for the home background. Null if none exists.</summary>
    public Task<string?> GetHeroAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "heroes", HeroDir, HeroQuery, ct);

    public static string IconDir { get; } = Path.Combine(Settings.DataDir, "icons");

    /// <summary>The game's icon, used on the tile when there is no square cover. Null if none exists.</summary>
    public Task<string?> GetIconAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "icons", IconDir, IconQuery, ct);

    // PNG and Windows icon files: many games' icons on SteamGridDB are only .ico.
    private const string IconQuery = "?types=static&mimes=image/png,image/vnd.microsoft.icon&nsfw=false&humor=false";

    /// <summary>The game's logo (title artwork on a transparent background). Null if none exists.</summary>
    public Task<string?> GetLogoAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "logos", LogoDir, LogoQuery, ct);

    private async Task<string?> GetArtAsync(Game item, string kind, string dir, string query, CancellationToken ct, bool useChosen = true)
    {
        // The user's own pick always wins.
        if (useChosen && kind != "icons" && FindChosenArt(item, kind) is string chosen) return chosen;
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, HashKey(item.Key));
        foreach (var ext in ImageExtensions)
            if (File.Exists(basePath + ext)) return basePath + ext;
        // Steam games: Steam's own artwork first; SteamGridDB only when Steam has none of this kind.
        if (await GetSteamArtAsync(item, kind, dir, basePath, ct).ConfigureAwait(false) is string fromSteam) return fromSteam;
        // "Nothing found" is remembered for a week, then asked again (SteamGridDB keeps gaining art).
        var none = basePath + ".none";
        if (File.Exists(none) && DateTime.Now - File.GetLastWriteTime(none) < TimeSpan.FromDays(7)) return null;

        string? endpoint = null;
        if (item.IsSteam) endpoint = $"{kind}/steam/{item.AppId}";
        else if (await FindGameIdAsync(item.OriginalName, ct).ConfigureAwait(false) is int id) endpoint = $"{kind}/game/{id}";

        string? url = null;
        if (endpoint != null)
        {
            using var doc = await GetJsonAsync(endpoint + query, ct).ConfigureAwait(false);
            url = FirstUrl(doc, squareOnly: false);
        }
        // Not linked to the Steam id on SteamGridDB: try the title.
        if (url == null && item.IsSteam && await FindGameIdAsync(item.OriginalName.Length > 0 ? item.OriginalName : item.Name, ct).ConfigureAwait(false) is int byName)
        {
            using var doc = await GetJsonAsync($"{kind}/game/{byName}{query}", ct).ConfigureAwait(false);
            url = FirstUrl(doc, squareOnly: false);
        }
        if (url == null)
        {
            TryWrite(basePath + ".none", "");
            return null;
        }
        return await DownloadAsync(url, basePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A square animated cover as a small looping video. SteamGridDB stores animated art in formats
    /// Windows can't show directly, but offers a video preview of each one, which is what this fetches.
    /// </summary>
    public async Task<string?> GetAnimatedCoverAsync(Game item, CancellationToken ct)
    {
        Directory.CreateDirectory(AnimatedDir);
        var basePath = Path.Combine(AnimatedDir, HashKey(item.Key));
        if (File.Exists(basePath + ".webm")) return basePath + ".webm";
        if (File.Exists(basePath + ".none")) return null;

        string? endpoint = null;
        if (item.IsSteam) endpoint = $"grids/steam/{item.AppId}";
        else if (await FindGameIdAsync(item.OriginalName, ct).ConfigureAwait(false) is int id) endpoint = $"grids/game/{id}";

        string? video = null;
        if (endpoint != null)
        {
            using var doc = await GetJsonAsync(
                endpoint + "?dimensions=1024x1024,512x512&types=animated&nsfw=false&humor=false", ct).ConfigureAwait(false);
            if (doc != null && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var grid in data.EnumerateArray())
                {
                    var thumb = grid.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                    if (thumb != null && thumb.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)) { video = thumb; break; }
                }
            }
        }

        if (video == null)
        {
            TryWrite(basePath + ".none", "");
            return null;
        }

        var path = basePath + ".webm";
        using var resp = await _http.GetAsync(video, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var temp = path + ".part";
        await using (var fs = File.Create(temp))
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private static string? FirstUrl(JsonDocument? doc, bool squareOnly)
    {
        if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array) return null;
        foreach (var image in data.EnumerateArray())
        {
            if (squareOnly && image.TryGetProperty("width", out var w) && image.TryGetProperty("height", out var h) &&
                w.TryGetInt32(out var wi) && h.TryGetInt32(out var hi) && wi != hi) continue;
            if (image.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } s) return s;
        }
        return null;
    }

    /// <summary>A file chosen from SteamGridDB's icons is saved with this prefix, so it is shown as an icon (centered).</summary>
    public const string IconFilePrefix = "icon_";

    public static bool IsIconFile(string? path) =>
        path != null && Path.GetFileName(path).StartsWith(IconFilePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>All icons SteamGridDB has for a game: an alternative when it has no square cover.</summary>
    public async Task<List<GridImage>> ListIconsAsync(Game item, CancellationToken ct)
    {
        var result = new List<GridImage>();
        string endpoint;
        if (item.IsSteam) endpoint = $"icons/steam/{item.AppId}";
        else
        {
            if (await FindGameIdAsync(item.Name, ct).ConfigureAwait(false) is not int gameId) return result;
            endpoint = $"icons/game/{gameId}";
        }
        void Read(JsonDocument? doc)
        {
            if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
            foreach (var icon in data.EnumerateArray())
            {
                var url = icon.TryGetProperty("url", out var u) ? u.GetString() : null;
                var thumb = icon.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(url) && result.All(r => r.Url != url))
                    result.Add(new GridImage(url, string.IsNullOrEmpty(thumb) ? url : thumb));
            }
        }

        using (var doc = await GetJsonAsync(endpoint + IconQuery, ct).ConfigureAwait(false)) Read(doc);
        // A Steam game SteamGridDB doesn't link to its Steam id (or lists no icons under it): look it up by title too.
        if (result.Count == 0 && item.IsSteam && await FindGameIdAsync(item.OriginalName.Length > 0 ? item.OriginalName : item.Name, ct).ConfigureAwait(false) is int byName)
        {
            using var doc = await GetJsonAsync($"icons/game/{byName}{IconQuery}", ct).ConfigureAwait(false);
            Read(doc);
        }
        return result;
    }

    /// <summary>All square covers SteamGridDB has for a game (by Steam id) or an app (by name search).</summary>
    public async Task<List<GridImage>> ListSquareAsync(Game item, CancellationToken ct)
    {
        var result = new List<GridImage>();
        string endpoint;
        if (item.IsSteam)
        {
            endpoint = $"grids/steam/{item.AppId}";
        }
        else
        {
            if (await FindGameIdAsync(item.Name, ct).ConfigureAwait(false) is not int gameId) return result;
            endpoint = $"grids/game/{gameId}";
        }

        using var doc = await GetJsonAsync(endpoint + SquareGridQuery, ct).ConfigureAwait(false);
        if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array) return result;

        foreach (var grid in data.EnumerateArray())
        {
            if (grid.TryGetProperty("width", out var w) && grid.TryGetProperty("height", out var h) &&
                w.TryGetInt32(out var wi) && h.TryGetInt32(out var hi) && wi != hi) continue;
            var url = grid.TryGetProperty("url", out var u) ? u.GetString() : null;
            var thumb = grid.TryGetProperty("thumb", out var t) ? t.GetString() : null;
            if (!string.IsNullOrEmpty(url)) result.Add(new GridImage(url, string.IsNullOrEmpty(thumb) ? url : thumb));
        }
        return result;
    }

    /// <summary>
    /// Downloads an image, trying again after a short wait if the server is slow or drops the connection,
    /// and falling back to a second address (the smaller preview) if the first never works.
    /// Returns null only when every attempt failed.
    /// </summary>
    public async Task<string?> DownloadWithRetryAsync(string url, string? fallbackUrl, string pathWithoutExtension, CancellationToken ct,
        IProgress<(long Done, long? Total)>? progress = null, IProgress<int>? retrying = null)
    {
        foreach (var address in new[] { url, fallbackUrl })
        {
            if (string.IsNullOrEmpty(address)) continue;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (attempt > 1) retrying?.Report(attempt);
                    var path = await DownloadAsync(address, pathWithoutExtension, ct, progress).ConfigureAwait(false);
                    // A truncated file would show as a broken tile: check it really is an image.
                    if (path != null && new FileInfo(path).Length > 512) return path;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                }
                await Task.Delay(700 * attempt, ct).ConfigureAwait(false);
            }
        }
        return null;
    }

    /// <summary>Downloads an image to <paramref name="pathWithoutExtension"/> plus .png/.jpg.</summary>
    public async Task<string?> DownloadAsync(string url, string pathWithoutExtension, CancellationToken ct, IProgress<(long Done, long? Total)>? progress = null)
    {
        var extension = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var path = pathWithoutExtension + extension;
        using var resp = await ImageHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        // Steam answers a missing picture with a small page or placeholder now and then: only real images are kept.
        if (resp.Content.Headers.ContentType?.MediaType is string type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Written under a temporary name first, so a download that is cut off never leaves a half image behind.
        var temp = path + ".part";
        await using (var fs = File.Create(temp))
        {
            if (progress == null) await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            else
            {
                long? total = resp.Content.Headers.ContentLength;
                await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buffer = new byte[65536];
                long done = 0;
                int read;
                progress.Report((0, total));
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress.Report((done, total));
                }
            }
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Downloads pictures (from SteamGridDB's image server, Steam's and others). Separate from the API client so the
    /// SteamGridDB key, which only its API needs, isn't sent to every image host.
    /// </summary>
    private static readonly HttpClient ImageHttp = MakeImageClient();

    private static HttpClient MakeImageClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/6");
        return http;
    }

    // ---- Steam's own artwork, used first for Steam games (SteamGridDB when Steam has none).

    /// <summary>
    /// The addresses of Steam's own picture of this kind for an app, best first: the library hero banner, the logo,
    /// the tall library cover (twice the size first). Steam's image servers keep each under two addresses.
    /// Null for kinds Steam doesn't make (square covers, icons).
    /// </summary>
    public static string[]? SteamArtUrls(int appId, string kind, string dir)
    {
        string[] files = kind switch
        {
            "heroes" => new[] { "library_hero.jpg" },
            "logos" => new[] { "logo.png" },
            "grids" when dir == PortraitDir => new[] { "library_600x900_2x.jpg", "library_600x900.jpg" },
            "grids" when dir == HeaderDir => new[] { "header.jpg" },
            _ => Array.Empty<string>()
        };
        if (files.Length == 0) return null;
        return files.SelectMany(f => new[]
        {
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/{f}",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/{f}"
        }).ToArray();
    }

    /// <summary>
    /// Lets go of the downloaded banners, logos, tall covers and headers of these games (not pictures the user picked,
    /// not square covers), so they're fetched again: from Steam first, now.
    /// </summary>
    public static void ForgetDownloadedArt(IEnumerable<Game> games)
    {
        foreach (var game in games)
        {
            string id = HashKey(game.Key);
            foreach (var dir in new[] { HeroDir, LogoDir, PortraitDir, HeaderDir })
                foreach (var ext in ImageExtensions.Append(".none").Append(".nosteam"))
                {
                    try { File.Delete(Path.Combine(dir, id + ext)); } catch (Exception) { }
                }
        }
    }

    /// <summary>Steam's own picture of this kind for a Steam game, downloaded; null when Steam has none.</summary>
    private async Task<string?> GetSteamArtAsync(Game item, string kind, string dir, string basePath, CancellationToken ct)
    {
        if (!item.IsSteam || item.AppId <= 0 || SteamArtUrls(item.AppId, kind, dir) is not { } urls) return null;
        // Remembered for a few days when Steam has none, so SteamGridDB is asked straight away meanwhile.
        var none = basePath + ".nosteam";
        if (File.Exists(none) && DateTime.Now - File.GetLastWriteTime(none) < TimeSpan.FromDays(5)) return null;
        foreach (var url in urls)
        {
            try
            {
                var path = await DownloadAsync(url, basePath, ct).ConfigureAwait(false);
                if (path != null && new FileInfo(path).Length > 1024) return path;
                if (path != null) File.Delete(path);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { }
        }
        TryWrite(none, "");
        return null;
    }

    private static void TryWrite(string path, string text)
    {
        try { File.WriteAllText(path, text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => _http.Dispose();
}
