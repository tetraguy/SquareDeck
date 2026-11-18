using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

// ==================================================================== not-installed and wishlist cache

/// <summary>
/// The Steam games you own but haven't installed, and your wishlist, saved between runs so the library
/// shows them (and their counts) straight away instead of asking Steam every time.
/// </summary>
public sealed class GameListCache
{
    public sealed class OwnedEntry
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class WishEntry
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public int Discount { get; set; }
        public DateTime Added { get; set; }
        public DateTime PriceAt { get; set; }
    }

    public List<OwnedEntry> Owned { get; set; } = new();
    public DateTime OwnedAt { get; set; }
    public List<WishEntry> Wishlist { get; set; } = new();
    public int WishlistTotal { get; set; }
    public DateTime WishlistAt { get; set; }

    private static string FilePath => Path.Combine(Settings.DataDir, "steam-lists.json");

    public static GameListCache Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cache = JsonSerializer.Deserialize<GameListCache>(File.ReadAllText(FilePath));
                if (cache != null)
                {
                    cache.Owned ??= new();
                    cache.Wishlist ??= new();
                    return cache;
                }
            }
        }
        catch (Exception) { }
        return new GameListCache();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, true);
        }
        catch (Exception) { }
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch (Exception) { }
    }
}

// ==================================================================== Night light

/// <summary>
/// Windows' Night light (warmer colors in the evening). Windows has no public switch for it, so this
/// uses the setting Windows itself stores in the registry. It works on current Windows 10 and 11; if the
/// stored format is ever different, <see cref="IsSupported"/> is false and the Settings page is offered instead.
/// </summary>
public static class NightLight
{
    private const string StateKey =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private const byte OnMarker = 0x15, OffMarker = 0x13;

    private static byte[]? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey);
            return key?.GetValue("Data") as byte[];
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsSupported => Read() is { Length: > 24 } data && data[18] is OnMarker or OffMarker;

    /// <summary>True when Night light is on right now (null if it can't be read).</summary>
    public static bool? IsOn => Read() is { Length: > 24 } data && data[18] is OnMarker or OffMarker ? data[18] == OnMarker : null;

    /// <summary>Turns Night light on or off. False if the stored setting isn't in the expected format.</summary>
    public static bool Set(bool on)
    {
        var data = Read();
        if (data is not { Length: > 24 } || data[18] is not (OnMarker or OffMarker)) return false;
        if ((data[18] == OnMarker) == on) return true;

        byte[] changed;
        if (on)
        {
            // "On" adds two bytes (10 00) after the header.
            changed = new byte[data.Length + 2];
            Array.Copy(data, 0, changed, 0, 22);
            Array.Copy(data, 22, changed, 24, data.Length - 22);
            changed[18] = OnMarker;
            changed[22] = 0x10;
            changed[23] = 0x00;
        }
        else
        {
            changed = new byte[data.Length - 2];
            Array.Copy(data, 0, changed, 0, 22);
            Array.Copy(data, 24, changed, 22, data.Length - 24);
            changed[18] = OffMarker;
        }

        // Bump the stored change counter so Windows notices and applies it immediately.
        for (int i = 10; i < 15; i++)
        {
            if (changed[i] != 0xFF) { changed[i]++; break; }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey, writable: true);
            if (key == null) return false;
            key.SetValue("Data", changed, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// ==================================================================== Steam profile

public sealed record SteamBadge(int BadgeId, int AppId, int Level, int Xp, DateTime Earned, string Name);

public sealed record SteamRecentGame(int AppId, string Name, int MinutesTwoWeeks, int MinutesTotal, string IconUrl);

public sealed record SteamProfileData(
    string? Name, string? AvatarUrl, string? ProfileUrl, DateTime? MemberSince,
    int? Level, int Xp, int XpForThisLevel, int XpToNextLevel,
    List<SteamBadge> Badges, List<SteamRecentGame> Recent, int RecentTotal);

/// <summary>Your Steam level, badges and recently played games, from the Steam Web API (needs your own key).</summary>
public static class SteamProfile
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static SteamProfileData? _cached;
    private static DateTime _cachedAt;

    /// <summary>Badges Steam itself awards (not tied to a game) that have well-known names.</summary>
    private static readonly Dictionary<int, string> CommunityBadges = new()
    {
        [1] = "Years of Service",
        [13] = "Game Collector",
        [2] = "Community Ambassador",
        [17] = "Steam Community Leader"
    };

    public static async Task<SteamProfileData?> GetAsync(string apiKey, bool refresh, CancellationToken ct)
    {
        if (!refresh && _cached != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(10)) return _cached;
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null || string.IsNullOrWhiteSpace(apiKey)) return null;
        string key = Uri.EscapeDataString(apiKey.Trim());
        string Api(string path) => $"https://api.steampowered.com/{path}?key={key}&steamid={steamId}&steamids={steamId}";

        async Task<JsonDocument?> Get(string url)
        {
            try { return JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false)); }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }

        var summaryTask = Get(Api("ISteamUser/GetPlayerSummaries/v2/"));
        var badgesTask = Get(Api("IPlayerService/GetBadges/v1/"));
        var recentTask = Get(Api("IPlayerService/GetRecentlyPlayedGames/v1/") + "&count=12");
        await Task.WhenAll(summaryTask, badgesTask, recentTask).ConfigureAwait(false);

        using var summary = summaryTask.Result;
        using var badgesDoc = badgesTask.Result;
        using var recentDoc = recentTask.Result;
        if (summary == null && badgesDoc == null && recentDoc == null) return null;

        string? name = null, avatar = null, url = null;
        DateTime? since = null;
        if (summary != null && summary.RootElement.TryGetProperty("response", out var sr) &&
            sr.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
        {
            var p = players[0];
            name = Str(p, "personaname");
            avatar = Str(p, "avatarfull") ?? Str(p, "avatarmedium");
            url = Str(p, "profileurl");
            if (p.TryGetProperty("timecreated", out var tc) && tc.TryGetInt64(out var created))
                since = DateTimeOffset.FromUnixTimeSeconds(created).LocalDateTime;
        }

        int? level = null;
        int xp = 0, xpThisLevel = 0, xpToNext = 0;
        var badges = new List<SteamBadge>();
        if (badgesDoc != null && badgesDoc.RootElement.TryGetProperty("response", out var br))
        {
            level = Int(br, "player_level");
            xp = Int(br, "player_xp") ?? 0;
            xpThisLevel = Int(br, "player_xp_needed_current_level") ?? 0;
            xpToNext = Int(br, "player_xp_needed_to_level_up") ?? 0;
            if (br.TryGetProperty("badges", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in list.EnumerateArray())
                {
                    int id = Int(b, "badgeid") ?? 0, appId = Int(b, "appid") ?? 0;
                    long when = b.TryGetProperty("completion_time", out var ct2) && ct2.TryGetInt64(out var t) ? t : 0;
                    badges.Add(new SteamBadge(id, appId, Int(b, "level") ?? 0, Int(b, "xp") ?? 0,
                        DateTimeOffset.FromUnixTimeSeconds(when).LocalDateTime,
                        appId > 0 ? "" : CommunityBadges.TryGetValue(id, out var n) ? n : "Steam badge"));
                }
            }
        }

        var recent = new List<SteamRecentGame>();
        int recentTotal = 0;
        if (recentDoc != null && recentDoc.RootElement.TryGetProperty("response", out var rr))
        {
            recentTotal = Int(rr, "total_count") ?? 0;
            if (rr.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in games.EnumerateArray())
                {
                    int appId = Int(g, "appid") ?? 0;
                    string icon = Str(g, "img_icon_url") is { Length: > 0 } hash
                        ? $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{hash}.jpg" : "";
                    recent.Add(new SteamRecentGame(appId, Str(g, "name") ?? "App " + appId,
                        Int(g, "playtime_2weeks") ?? 0, Int(g, "playtime_forever") ?? 0, icon));
                }
            }
        }

        _cached = new SteamProfileData(name, avatar, url, since, level, xp, xpThisLevel, xpToNext,
            badges.OrderByDescending(b => b.Earned).ToList(), recent, recentTotal);
        _cachedAt = DateTime.UtcNow;
        return _cached;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}

// ==================================================================== background music

/// <summary>One piece of background music: a built-in track, one of yours, or a music sound pack.</summary>
public sealed record MusicTrack(string Id, string Name, string File);

public static class BackgroundMusic
{
    public static string BuiltInDir => Path.Combine(AppContext.BaseDirectory, "Music");
    public static string UserDir => Path.Combine(Settings.DataDir, "Music");

    private static readonly string[] Types = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

    /// <summary>Every track that can be picked, built-in ones first.</summary>
    public static List<MusicTrack> List()
    {
        var tracks = new List<MusicTrack>();
        foreach (var dir in new[] { BuiltInDir, UserDir })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir).Where(f => Types.Contains(Path.GetExtension(f).ToLowerInvariant()))
                             .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    tracks.RemoveAll(t => t.Id == "track:" + name); // yours replaces a built-in one of the same name
                    tracks.Add(new MusicTrack("track:" + name, name, file));
                }
            }
            catch (Exception) { }
        }
        foreach (var pack in SoundPacks.List().Where(p => p.Music))
            if (SoundPacks.MusicFile(pack) is string file) tracks.Add(new MusicTrack(pack.Id, pack.Name, file));
        return tracks;
    }

    /// <summary>The name shown in Settings for a stored choice.</summary>
    public static string NameOf(string id) => id switch
    {
        "" => "Off",
        "shuffle" => "Shuffle all",
        _ => List().FirstOrDefault(t => t.Id == id)?.Name ?? "Off"
    };

    /// <summary>The file to play for a choice; for "shuffle" a random track other than <paramref name="avoid"/>.</summary>
    public static string? Resolve(string id, string? avoid = null)
    {
        var all = List();
        if (all.Count == 0 || id.Length == 0) return null;
        if (id == "shuffle")
        {
            var choices = all.Where(t => t.File != avoid).ToList();
            if (choices.Count == 0) choices = all;
            return choices[Random.Shared.Next(choices.Count)].File;
        }
        return all.FirstOrDefault(t => t.Id == id)?.File;
    }
}

// ==================================================================== small helpers

/// <summary>Encrypts text so only this Windows account on this PC can read it back (for sign-in tokens).</summary>
public static class LocalSecret
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[]? Run(byte[] data, bool protect)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            const int NoUi = 1;
            bool ok = protect
                ? CryptProtectData(ref input, "SquareDeck", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, ref output);
            if (!ok) return null;
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    public static string Protect(string text) =>
        text.Length == 0 ? "" : Run(Encoding.UTF8.GetBytes(text), true) is { } b ? Convert.ToBase64String(b) : "";

    public static string Unprotect(string stored)
    {
        if (stored.Length == 0) return "";
        try { return Run(Convert.FromBase64String(stored), false) is { } b ? Encoding.UTF8.GetString(b) : ""; }
        catch (FormatException) { return ""; }
    }
}

// ==================================================================== Spotify

public sealed record SpotifyItem(string Kind, string Uri, string Name, string Detail, string ImageUrl);

public sealed record SpotifyState(bool IsPlaying, string Title, string Artist, string ImageUrl, string Uri, int ProgressMs, int DurationMs,
                                  bool Shuffle, string Repeat, string Device, string DeviceId, int Volume);

public sealed record SpotifyDevice(string Id, string Name, string Type, bool Active);

/// <summary>
/// Spotify search and your playlists through the Spotify Web API. Spotify requires every app to be
/// registered, so the user creates a free app at developer.spotify.com and pastes its Client ID in Settings.
/// Sign-in uses the standard PKCE flow in the browser; the token is kept encrypted on this PC only.
/// </summary>
public sealed class SpotifyClient
{
    public const int Port = 43821;
    public static string RedirectUri => $"http://127.0.0.1:{Port}/callback";
    // Playlists, Liked Songs, recently played, and playing / controlling music on your Spotify devices.
    private const string Scopes = "playlist-read-private playlist-read-collaborative user-read-playback-state user-modify-playback-state " +
                                  "user-read-currently-playing user-library-read user-read-recently-played " +
                                  // Playing inside SquareDeck (Spotify's Web Playback SDK).
                                  "streaming user-read-email user-read-private";

    /// <summary>Bumped when new permissions are needed, so an older sign-in is asked to sign in again.</summary>
    public const int ScopesVersion = 3;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Settings _settings;
    private string? _accessToken;
    private DateTime _expires;

    public SpotifyClient(Settings settings) => _settings = settings;

    /// <summary>
    /// SquareDeck's own Spotify app, used unless you enter the Client ID of an app of your own in Settings. A Client ID
    /// isn't a secret (sign-in uses PKCE, with no client secret), so it can ship with the launcher.
    /// </summary>
    public const string BuiltInClientId = "cf67066c8c464657aab3f60fd20ce346";

    /// <summary>The Client ID in use: your own if you entered one, else SquareDeck's.</summary>
    public string ClientId => _settings.SpotifyClientId.Trim() is { Length: > 0 } own ? own : BuiltInClientId;

    public bool UsesBuiltInApp => _settings.SpotifyClientId.Trim().Length == 0;

    public bool HasClientId => ClientId.Length > 0;
    public bool IsSignedIn => LocalSecret.Unprotect(_settings.SpotifyToken).Length > 0;

    /// <summary>Signed in before the built-in player needed more permissions: sign in once more.</summary>
    public bool NeedsNewSignIn => IsSignedIn && _settings.SpotifyScopes < ScopesVersion;

    public void SignOut()
    {
        _settings.SpotifyToken = "";
        _settings.Save();
        _accessToken = null;
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Opens the Spotify sign-in page in the browser and waits (up to three minutes) for the answer.</summary>
    public async Task SignInAsync(CancellationToken ct)
    {
        if (!HasClientId) throw new InvalidOperationException("Add your Spotify Client ID in Settings first.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(12));

        var listener = new TcpListener(IPAddress.Loopback, Port);
        try { listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException($"Port {Port} is in use by another program, so sign-in can't finish."); }

        try
        {
            string url = "https://accounts.spotify.com/authorize?response_type=code" +
                         "&client_id=" + Uri.EscapeDataString(ClientId) +
                         "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                         "&scope=" + Uri.EscapeDataString(Scopes) +
                         "&code_challenge_method=S256&code_challenge=" + challenge + "&state=" + state;
            ProcessUtil.Open(url);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            string? code = null, error = null;
            while (code == null && error == null)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                await using var stream = client.GetStream();
                var buffer = new byte[8192];
                int read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                var firstLine = request.Split('\n')[0]; // GET /callback?code=...&state=... HTTP/1.1
                var parts = firstLine.Split(' ');
                if (parts.Length < 2 || !parts[1].StartsWith("/callback")) continue;
                var query = System.Web.HttpUtility.ParseQueryString(parts[1].Contains('?') ? parts[1][(parts[1].IndexOf('?') + 1)..] : "");
                if (query["state"] != state) error = "The sign-in answer didn't match. Try again.";
                else if (query["error"] is string e) error = e == "access_denied" ? "Sign-in was cancelled." : "Spotify said: " + e;
                else code = query["code"];

                string page = "<html><body style='font-family:Segoe UI;background:#121212;color:#fff;text-align:center;padding-top:15%'>" +
                              (code != null ? "<h2>Signed in to Spotify</h2><p>You can close this tab and go back to SquareDeck.</p>"
                                            : "<h2>Sign-in didn't finish</h2><p>Go back to SquareDeck and try again.</p>") + "</body></html>";
                var body = Encoding.UTF8.GetBytes(page);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
            }
            if (error != null) throw new InvalidOperationException(error);

            await TokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code!, ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId, ["code_verifier"] = verifier
            }, ct).ConfigureAwait(false);
            _settings.SpotifyScopes = ScopesVersion;
            _settings.Save();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Sign-in timed out.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!resp.IsSuccessStatusCode)
        {
            string why = root.TryGetProperty("error_description", out var d) ? d.GetString() ?? "" : resp.StatusCode.ToString();
            if (form["grant_type"] == "refresh_token") SignOut(); // the saved sign-in no longer works
            throw new InvalidOperationException("Spotify refused the sign-in: " + why);
        }
        _accessToken = root.GetProperty("access_token").GetString();
        _expires = DateTime.UtcNow.AddSeconds((root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 60);
        if (root.TryGetProperty("refresh_token", out var r) && r.GetString() is { Length: > 0 } refresh)
        {
            _settings.SpotifyToken = LocalSecret.Protect(refresh);
            _settings.Save();
        }
    }

    /// <summary>A current sign-in token, refreshed when needed (for the built-in player).</summary>
    public Task<string> GetAccessTokenAsync(CancellationToken ct) => AccessTokenAsync(ct);

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _expires) return _accessToken;
        var refresh = LocalSecret.Unprotect(_settings.SpotifyToken);
        if (refresh.Length == 0) throw new InvalidOperationException("Sign in to Spotify first.");
        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = ClientId
        }, ct).ConfigureAwait(false);
        return _accessToken!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? jsonBody, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/" + path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct).ConfigureAwait(false));
        if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await Http.SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<List<SpotifyItem>> SearchAsync(string text, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, "search?limit=8&type=track,album,playlist,artist&q=" + Uri.EscapeDataString(text), null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return ParseSearch(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    public async Task<List<SpotifyItem>> MyPlaylistsAsync(CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, "me/playlists?limit=50", null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var list = new List<SpotifyItem>();
        if (doc.RootElement.TryGetProperty("items", out var items))
            foreach (var p in items.EnumerateArray())
                if (Item(p, "playlist") is { } item) list.Add(item);
        return list;
    }

    /// <summary>
    /// Plays an item on whichever device Spotify is active on. Returns null when it started, otherwise a reason
    /// (no Spotify open anywhere, or Spotify only allows remote play with Premium).
    /// </summary>
    public async Task<string?> PlayAsync(SpotifyItem item, CancellationToken ct)
    {
        string body = item.Kind == "track"
            ? JsonSerializer.Serialize(new { uris = new[] { item.Uri } })
            : JsonSerializer.Serialize(new { context_uri = item.Uri });
        using var resp = await SendAsync(HttpMethod.Put, "me/player/play", body, ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode) return null;
        return resp.StatusCode switch
        {
            HttpStatusCode.NotFound => "nodevice",
            HttpStatusCode.Forbidden => "premium",
            _ => "Spotify answered " + (int)resp.StatusCode
        };
    }

    // ---------------------------------------------------------------- the built-in Spotify page

    /// <summary>What's playing on your Spotify account right now (null when nothing is, or Spotify is closed everywhere).</summary>
    public async Task<SpotifyState?> StateAsync(CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, "me/player?additional_types=episode", null, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (json.Length == 0) return null;
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string title = "", artist = "", image = "", uri = "";
        int duration = 0;
        if (r.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            uri = item.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "";
            duration = item.TryGetProperty("duration_ms", out var d) ? d.GetInt32() : 0;
            if (item.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array)
                artist = string.Join(", ", a.EnumerateArray().Select(x => x.TryGetProperty("name", out var an) ? an.GetString() : null).Where(x => x != null));
            else if (item.TryGetProperty("show", out var show) && show.TryGetProperty("name", out var sn)) artist = sn.GetString() ?? "";
            var images = item.TryGetProperty("album", out var album) && album.TryGetProperty("images", out var ai) ? ai
                       : item.TryGetProperty("images", out var ii) ? ii : default;
            if (images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0) image = images[0].GetProperty("url").GetString() ?? "";
        }
        string device = "", deviceId = "";
        int volume = -1;
        if (r.TryGetProperty("device", out var dev) && dev.ValueKind == JsonValueKind.Object)
        {
            device = dev.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "";
            deviceId = dev.TryGetProperty("id", out var di) ? di.GetString() ?? "" : "";
            volume = dev.TryGetProperty("volume_percent", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
        }
        return new SpotifyState(
            r.TryGetProperty("is_playing", out var p) && p.GetBoolean(), title, artist, image, uri,
            r.TryGetProperty("progress_ms", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : 0, duration,
            r.TryGetProperty("shuffle_state", out var sh) && sh.ValueKind == JsonValueKind.True,
            r.TryGetProperty("repeat_state", out var rp) ? rp.GetString() ?? "off" : "off", device, deviceId, volume);
    }

    /// <summary>A playback command. Returns null when it worked, otherwise "nodevice", "premium" or a reason.</summary>
    private async Task<string?> CommandAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode) return null;
        return resp.StatusCode switch
        {
            HttpStatusCode.NotFound => "nodevice",
            HttpStatusCode.Forbidden => "premium",
            HttpStatusCode.Unauthorized => "signin",
            _ => "Spotify answered " + (int)resp.StatusCode
        };
    }

    public Task<string?> ResumeAsync(CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/play", null, ct);
    public Task<string?> PauseAsync(CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/pause", null, ct);
    public Task<string?> NextAsync(CancellationToken ct) => CommandAsync(HttpMethod.Post, "me/player/next", null, ct);
    public Task<string?> PreviousAsync(CancellationToken ct) => CommandAsync(HttpMethod.Post, "me/player/previous", null, ct);
    public Task<string?> SeekAsync(int ms, CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/seek?position_ms=" + Math.Max(0, ms), null, ct);
    public Task<string?> ShuffleAsync(bool on, CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/shuffle?state=" + (on ? "true" : "false"), null, ct);
    public Task<string?> RepeatAsync(string mode, CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/repeat?state=" + mode, null, ct);
    public Task<string?> VolumeAsync(int percent, CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/volume?volume_percent=" + Math.Clamp(percent, 0, 100), null, ct);

    /// <summary>Moves playback to a device (and starts it).</summary>
    public Task<string?> TransferAsync(string deviceId, CancellationToken ct, bool play = true) =>
        CommandAsync(HttpMethod.Put, "me/player", JsonSerializer.Serialize(new { device_ids = new[] { deviceId }, play }), ct);

    /// <summary>Plays a playlist or album from one of its songs.</summary>
    public Task<string?> PlayFromAsync(string contextUri, string trackUri, CancellationToken ct) =>
        CommandAsync(HttpMethod.Put, "me/player/play", JsonSerializer.Serialize(new { context_uri = contextUri, offset = new { uri = trackUri } }), ct);

    /// <summary>Plays songs in order (Liked Songs, recently played).</summary>
    public Task<string?> PlayTracksAsync(IEnumerable<string> uris, CancellationToken ct) =>
        CommandAsync(HttpMethod.Put, "me/player/play", JsonSerializer.Serialize(new { uris = uris.Take(100).ToArray() }), ct);

    public async Task<List<SpotifyDevice>> DevicesAsync(CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, "me/player/devices", null, ct).ConfigureAwait(false);
        var list = new List<SpotifyDevice>();
        if (!resp.IsSuccessStatusCode) return list;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (doc.RootElement.TryGetProperty("devices", out var devices))
            foreach (var d in devices.EnumerateArray())
                list.Add(new SpotifyDevice(d.GetProperty("id").GetString() ?? "", d.GetProperty("name").GetString() ?? "",
                    d.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "", d.TryGetProperty("is_active", out var a) && a.GetBoolean()));
        return list;
    }

    private async Task<List<SpotifyItem>> TrackListAsync(string path, string? wrapper, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("scope");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var list = new List<SpotifyItem>();
        if (doc.RootElement.TryGetProperty("items", out var items))
            foreach (var e in items.EnumerateArray())
            {
                var track = wrapper != null && e.TryGetProperty(wrapper, out var w) ? w : e;
                if (Item(track, "track") is { } item && list.All(x => x.Uri != item.Uri)) list.Add(item);
            }
        return list;
    }

    public Task<List<SpotifyItem>> LikedAsync(CancellationToken ct) => TrackListAsync("me/tracks?limit=50", "track", ct);
    public Task<List<SpotifyItem>> RecentAsync(CancellationToken ct) => TrackListAsync("me/player/recently-played?limit=40", "track", ct);

    /// <summary>The songs of a playlist (spotify:playlist:id) or an album (spotify:album:id).</summary>
    public Task<List<SpotifyItem>> TracksOfAsync(string uri, CancellationToken ct)
    {
        var parts = uri.Split(':');
        if (parts.Length < 3) return Task.FromResult(new List<SpotifyItem>());
        return parts[1] == "album"
            ? TrackListAsync($"albums/{parts[2]}/tracks?limit=50", null, ct)
            : TrackListAsync($"playlists/{parts[2]}/tracks?limit=100", "track", ct);
    }

    public static List<SpotifyItem> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<SpotifyItem>();
        foreach (var (group, kind) in new[] { ("tracks", "track"), ("playlists", "playlist"), ("albums", "album"), ("artists", "artist") })
        {
            if (!doc.RootElement.TryGetProperty(group, out var g) || !g.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in items.EnumerateArray())
                if (Item(e, kind) is { } item) list.Add(item);
        }
        return list;
    }

    private static SpotifyItem? Item(JsonElement e, string kind)
    {
        if (e.ValueKind != JsonValueKind.Object) return null; // Spotify sometimes returns null entries
        string? uri = e.TryGetProperty("uri", out var u) ? u.GetString() : null;
        string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (uri == null || name.Length == 0) return null;

        string Artists(JsonElement x) => x.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(y => y.TryGetProperty("name", out var an) ? an.GetString() : null).Where(s => s != null))
            : "";
        string Image(JsonElement x) => x.TryGetProperty("images", out var im) && im.ValueKind == JsonValueKind.Array && im.GetArrayLength() > 0
            ? im[im.GetArrayLength() > 1 ? im.GetArrayLength() - 2 : 0].GetProperty("url").GetString() ?? "" : "";

        string detail, image;
        switch (kind)
        {
            case "track":
                detail = "Song  ·  " + Artists(e);
                image = e.TryGetProperty("album", out var album) ? Image(album) : "";
                break;
            case "album":
                detail = "Album  ·  " + Artists(e);
                image = Image(e);
                break;
            case "playlist":
                int count = e.TryGetProperty("tracks", out var t) && t.TryGetProperty("total", out var total) ? total.GetInt32() : 0;
                string owner = e.TryGetProperty("owner", out var o) && o.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
                detail = "Playlist" + (owner.Length > 0 ? "  ·  " + owner : "") + (count > 0 ? $"  ·  {count} songs" : "");
                image = Image(e);
                break;
            default:
                detail = "Artist";
                image = Image(e);
                break;
        }
        return new SpotifyItem(kind, uri, name, detail, image);
    }
}
