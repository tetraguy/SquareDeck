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
        foreach (var dir in new[] { CoverDir, InfoDir, HeroDir, LogoDir, AnimatedDir, IconDir })
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

        using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
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
    public static string AnimatedDir { get; } = Path.Combine(Settings.DataDir, "animated");

    /// <summary>Wide "hero" artwork for the home background. Null if none exists.</summary>
    public Task<string?> GetHeroAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "heroes", HeroDir, "?types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false", ct);

    public static string IconDir { get; } = Path.Combine(Settings.DataDir, "icons");

    /// <summary>The game's icon, used on the tile when there is no square cover. Null if none exists.</summary>
    public Task<string?> GetIconAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "icons", IconDir, "?types=static&mimes=image/png&nsfw=false&humor=false", ct);

    /// <summary>The game's logo (title artwork on a transparent background). Null if none exists.</summary>
    public Task<string?> GetLogoAsync(Game item, CancellationToken ct) =>
        GetArtAsync(item, "logos", LogoDir, "?types=static&mimes=image/png&nsfw=false&humor=false", ct);

    private async Task<string?> GetArtAsync(Game item, string kind, string dir, string query, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, HashKey(item.Key));
        foreach (var ext in ImageExtensions)
            if (File.Exists(basePath + ext)) return basePath + ext;
        if (File.Exists(basePath + ".none")) return null;

        string? endpoint = null;
        if (item.IsSteam) endpoint = $"{kind}/steam/{item.AppId}";
        else if (await FindGameIdAsync(item.OriginalName, ct).ConfigureAwait(false) is int id) endpoint = $"{kind}/game/{id}";

        string? url = null;
        if (endpoint != null)
        {
            using var doc = await GetJsonAsync(endpoint + query, ct).ConfigureAwait(false);
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
        using var doc = await GetJsonAsync(endpoint + "?types=static&mimes=image/png&nsfw=false&humor=false", ct).ConfigureAwait(false);
        if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var icon in data.EnumerateArray())
        {
            var url = icon.TryGetProperty("url", out var u) ? u.GetString() : null;
            var thumb = icon.TryGetProperty("thumb", out var t) ? t.GetString() : null;
            if (!string.IsNullOrEmpty(url)) result.Add(new GridImage(url, string.IsNullOrEmpty(thumb) ? url : thumb));
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
    public async Task<string?> DownloadWithRetryAsync(string url, string? fallbackUrl, string pathWithoutExtension, CancellationToken ct)
    {
        foreach (var address in new[] { url, fallbackUrl })
        {
            if (string.IsNullOrEmpty(address)) continue;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var path = await DownloadAsync(address, pathWithoutExtension, ct).ConfigureAwait(false);
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
    public async Task<string?> DownloadAsync(string url, string pathWithoutExtension, CancellationToken ct)
    {
        var extension = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var path = pathWithoutExtension + extension;
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Written under a temporary name first, so a download that is cut off never leaves a half image behind.
        var temp = path + ".part";
        await using (var fs = File.Create(temp))
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private static void TryWrite(string path, string text)
    {
        try { File.WriteAllText(path, text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => _http.Dispose();
}
