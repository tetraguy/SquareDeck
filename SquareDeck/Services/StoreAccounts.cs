using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Store accounts (9.0.8, out of Experimental in 9.0.20): sign in to Epic Games and to Microsoft (Xbox) so the library also shows the
/// games you own there that aren't installed. Installing, starting and uninstalling stay with each store's own app
/// (the Epic Games Launcher, the Xbox app): SquareDeck asks them to do it and never downloads game files itself.
///
/// Epic: the same sign-in other launchers use (Epic's own login page, which hands back a one-time code), then the
/// account's library and each game's name and art from Epic's catalog. Xbox: Microsoft's own sign-in page, then
/// Xbox Live's list of games played on the account (PC ones), and, if you choose, the whole PC Game Pass catalog.
/// The sign-ins are kept encrypted for this Windows account (like the Spotify one).
/// </summary>
public static class StoreAccounts
{
    public const string EpicLab = "epicaccount";
    public const string XboxLab = "xboxaccount";

    /// <summary>A game the account owns (or Game Pass includes), as saved between runs.</summary>
    public sealed class Entry
    {
        public string Store { get; set; } = "";      // "Epic" or "Xbox"
        public string Id { get; set; } = "";         // Epic: app name; Xbox: Store product id
        public string Name { get; set; } = "";
        public string Namespace { get; set; } = "";  // Epic
        public string CatalogId { get; set; } = "";  // Epic
        public string Art { get; set; } = "";        // a picture's address, if the store gave one
        public bool GamePass { get; set; }
    }

    private sealed class Cache
    {
        public List<Entry> Epic { get; set; } = new();
        public List<Entry> Xbox { get; set; } = new();
        public DateTime EpicAt { get; set; }
        public DateTime XboxAt { get; set; }
        public string EpicName { get; set; } = "";
        public string XboxName { get; set; } = "";
    }

    private static readonly string CachePath = Path.Combine(Settings.DataDir, "store-accounts.json");
    private static Cache? _cache;
    private static Cache Data
    {
        get
        {
            if (_cache != null) return _cache;
            try { _cache = File.Exists(CachePath) ? JsonSerializer.Deserialize<Cache>(File.ReadAllText(CachePath)) : null; }
            catch (Exception) { }
            return _cache ??= new Cache();
        }
    }

    private static void SaveCache()
    {
        try { SafeFile.WriteAllText(CachePath, JsonSerializer.Serialize(Data)); } catch (Exception) { }
    }

    public static IReadOnlyList<Entry> EpicGames => Data.Epic;
    public static IReadOnlyList<Entry> XboxGames => Data.Xbox;
    public static DateTime EpicUpdated => Data.EpicAt;
    public static DateTime XboxUpdated => Data.XboxAt;
    public static string EpicName => Data.EpicName;
    public static string XboxName => Data.XboxName;

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/9 (Windows)");
        return c;
    }

    /// <summary>Why the last Epic / Xbox step failed, in plain words (empty when it worked).</summary>
    public static string EpicError { get; private set; } = "";
    public static string XboxError { get; private set; } = "";

    /// <summary>Notes each step's answer to store-accounts.log (status codes and error ids only, never tokens).</summary>
    private static void Log(string line)
    {
        try
        {
            var path = Path.Combine(Settings.DataDir, "store-accounts.log");
            if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    // ================================================================ Epic Games

    // The Epic Games Launcher's public client: what Epic's login page hands codes to (other launchers use it too).
    private const string EpicClient = "34a02cf8f4414e29b15921876da36f9a";
    private const string EpicSecret = "daafbccc737745039dffe53d94fc76cf";
    private const string EpicToken = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";

    public static string EpicLoginUrl =>
        "https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString($"https://www.epicgames.com/id/api/redirect?clientId={EpicClient}&responseType=code");

    public static bool EpicSignedIn => Labs.Get("epic.refresh", "").Length > 0;

    /// <summary>Trades the one-time code from Epic's page for a sign-in that lasts. Null when it worked, else why not.</summary>
    public static async Task<string?> EpicSignInAsync(string code)
    {
        var r = await EpicTokenAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["token_type"] = "eg1" }).ConfigureAwait(false);
        return r == null ? "Epic didn't accept the sign-in. Try again." : null;
    }

    private static string _epicAccess = "";
    private static DateTime _epicExpires;

    private static async Task<JsonDocument?> EpicTokenAsync(Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, EpicToken) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(EpicClient + ":" + EpicSecret)));
        try
        {
            string grant = form.GetValueOrDefault("grant_type") ?? "";
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string code = "";
                try { using var err = JsonDocument.Parse(text); code = Str(err.RootElement, "errorCode"); } catch (Exception) { }
                Log($"Epic: token ({grant}) failed {(int)resp.StatusCode} {code}");
                EpicError = code.Contains("invalid_grant") || code.Contains("expired") ? "Your Epic sign-in expired. Sign in again." : $"Epic's sign-in didn't answer ({(int)resp.StatusCode}).";
                return null;
            }
            Log($"Epic: token ({grant}) -> ok");
            var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            _epicAccess = root.GetProperty("access_token").GetString() ?? "";
            _epicExpires = DateTime.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var e) ? e.GetInt32() - 60 : 3000);
            if (root.TryGetProperty("refresh_token", out var rt)) Labs.Put("epic.refresh", LocalSecret.Protect(rt.GetString() ?? ""));
            if (root.TryGetProperty("account_id", out var id)) Labs.Put("epic.account", id.GetString() ?? "");
            if (root.TryGetProperty("displayName", out var dn) && dn.GetString() is { Length: > 0 } name) { Data.EpicName = name; SaveCache(); }
            return doc;
        }
        catch (Exception ex) { Log($"Epic: token error {ex.GetType().Name}"); EpicError = "Couldn't reach Epic. Check the internet connection."; return null; }
    }

    private static async Task<string?> EpicAccessAsync()
    {
        if (_epicAccess.Length > 0 && DateTime.UtcNow < _epicExpires) return _epicAccess;
        string refresh = LocalSecret.Unprotect(Labs.Get("epic.refresh", ""));
        if (refresh.Length == 0) { EpicError = "You're signed out of Epic Games. Sign in again."; return null; }
        using var doc = await EpicTokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["token_type"] = "eg1" }).ConfigureAwait(false);
        return doc == null ? null : _epicAccess;
    }

    public static void EpicSignOut()
    {
        Labs.Put("epic.refresh", "");
        Labs.Put("epic.account", "");
        _epicAccess = "";
        Data.Epic.Clear();
        Data.EpicName = "";
        Data.EpicAt = DateTime.MinValue;
        SaveCache();
    }

    /// <summary>Gets the account's games from Epic (names and art from its catalog). Null when it couldn't.</summary>
    public static async Task<List<Entry>?> RefreshEpicAsync(CancellationToken ct)
    {
        var token = await EpicAccessAsync().ConfigureAwait(false);
        if (token == null) return null;
        var records = new List<(string Ns, string Catalog, string App)>();
        string? cursor = null;
        try
        {
            for (int page = 0; page < 40; page++)
            {
                string url = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true" + (cursor != null ? "&cursor=" + Uri.EscapeDataString(cursor) : "");
                var (doc, status) = await GetJsonStatusAsync(url, "bearer " + token, ct).ConfigureAwait(false);
                using var _ = doc;
                Log($"Epic: library page {page + 1} -> {status}");
                if (doc == null)
                {
                    EpicError = status == 401 ? "Your Epic sign-in expired. Sign in again." : $"Epic didn't send the game list ({status}). Try again in a moment.";
                    if (status == 401) _epicAccess = "";
                    return null;
                }
                if (!doc.RootElement.TryGetProperty("records", out var recs) || recs.ValueKind != JsonValueKind.Array) break;
                foreach (var r in recs.EnumerateArray())
                {
                    string ns = Str(r, "namespace"), cat = Str(r, "catalogItemId"), app = Str(r, "appName");
                    if (ns.Length == 0 || cat.Length == 0 || app.Length == 0 || ns == "ue") continue; // Unreal Engine assets
                    if (Str(r, "sandboxType") is "PRIVATE") continue;
                    records.Add((ns, cat, app));
                }
                cursor = doc.RootElement.TryGetProperty("responseMetadata", out var meta) && meta.TryGetProperty("nextCursor", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
                if (string.IsNullOrEmpty(cursor)) break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log($"Epic: library error {ex.GetType().Name}"); EpicError = "Couldn't read the Epic game list."; return null; }
        Log($"Epic: {records.Count} items in the library");

        // Names and art, a namespace at a time (several at once, 50 items a request); add-ons (DLC) are left out.
        var known = Data.Epic.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var details = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement>();
        var missing = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(records.GroupBy(r => r.Ns), new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, async (group, token2) =>
        {
            foreach (var chunk in group.Select(g => g.Catalog).Distinct().Chunk(50))
            {
                try
                {
                    using var cdoc = await GetJsonAsync($"https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/{Uri.EscapeDataString(group.Key)}/bulk/items?id={string.Join(",", chunk)}&includeDLCDetails=true&includeMainGameDetails=true&country=US&locale=en-US",
                        "bearer " + token, token2).ConfigureAwait(false);
                    if (cdoc == null || cdoc.RootElement.ValueKind != JsonValueKind.Object) { missing.Add(group.Key); continue; }
                    foreach (var p in cdoc.RootElement.EnumerateObject()) details[p.Name] = p.Value.Clone();
                }
                catch (Exception) when (!token2.IsCancellationRequested) { missing.Add(group.Key); }
            }
        }).ConfigureAwait(false);
        if (!missing.IsEmpty) Log($"Epic: no catalog details for {missing.Distinct().Count()} namespaces");
        var list = new List<Entry>();
        foreach (var group in records.GroupBy(r => r.Ns))
        {
            foreach (var (ns, cat, app) in group)
            {
                string name = "", art = "";
                bool addon = false;
                try
                {
                    if (details.TryGetValue(cat, out var item))
                    {
                        name = Str(item, "title");
                        addon = item.TryGetProperty("mainGameItem", out var main) && main.ValueKind == JsonValueKind.Object;
                        if (item.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                            addon |= cats.EnumerateArray().Any(c => Str(c, "path").StartsWith("addons", StringComparison.OrdinalIgnoreCase) || Str(c, "path") == "digitalextras");
                        if (item.TryGetProperty("keyImages", out var images) && images.ValueKind == JsonValueKind.Array)
                        {
                            // Epic lists some picture types more than once: take the first of each.
                            var byType = images.EnumerateArray().GroupBy(i => Str(i, "type"), StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => Str(g.First(), "url"), StringComparer.OrdinalIgnoreCase);
                            art = new[] { "DieselGameBoxTall", "OfferImageTall", "DieselGameBox", "Thumbnail" }.Select(t => byType.GetValueOrDefault(t) ?? "").FirstOrDefault(u => u.Length > 0) ?? "";
                        }
                    }
                }
                catch (Exception) { name = ""; }
                if (name.Length == 0 && known.TryGetValue(app, out var old)) { name = old.Name; art = old.Art; }
                if (addon || name.Length == 0) continue;
                list.Add(new Entry { Store = "Epic", Id = app, Name = name, Namespace = ns, CatalogId = cat, Art = art });
            }
        }
        list = list.GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Log($"Epic: {list.Count} games on the account");
        EpicError = "";
        Data.Epic = list;
        Data.EpicAt = DateTime.Now;
        SaveCache();
        return list;
    }

    public static string EpicInstallUri(Entry e) =>
        $"com.epicgames.launcher://apps/{Uri.EscapeDataString(e.Namespace)}%3A{Uri.EscapeDataString(e.CatalogId)}%3A{Uri.EscapeDataString(e.Id)}?action=install";

    public static string EpicLaunchUri(string appName) => $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true";

    // ================================================================ Microsoft (Xbox)

    // Microsoft's sign-in for Xbox Live. 9.0.10: the old Xbox tools' client (0000000048093EE3) now gets a 403 from
    // Xbox Live, so this uses the Minecraft launcher's public client instead, which still gets an Xbox token: its
    // page hands back a one-time code on Microsoft's own "desktop" page, which SquareDeck trades for the sign-in.
    private const string XboxClient = "00000000402b5328";
    private const string XboxScope = "service::user.auth.xboxlive.com::MBI_SSL";
    private const string XboxRedirect = "https://login.live.com/oauth20_desktop.srf";

    public static string XboxLoginUrl =>
        $"https://login.live.com/oauth20_authorize.srf?client_id={XboxClient}&redirect_uri={Uri.EscapeDataString(XboxRedirect)}&response_type=code&display=touch&prompt=select_account&scope={Uri.EscapeDataString(XboxScope)}&locale=en";

    public static bool IsXboxRedirect(Uri uri) => uri.AbsoluteUri.StartsWith(XboxRedirect, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when Microsoft's desktop page carries an answer (a code, a token or an error).</summary>
    public static bool IsXboxAnswer(Uri uri) =>
        IsXboxRedirect(uri) && (uri.Query + uri.Fragment).Split('?', '#', '&').Any(p => p.StartsWith("code=") || p.StartsWith("access_token=") || p.StartsWith("error="));

    // A sign-in saved by an older SquareDeck belongs to the old client and can't be refreshed with this one.
    public static bool XboxSignedIn => Labs.Get("xbox.refresh", "").Length > 0 && Labs.Get("xbox.client", "") == XboxClient;

    public static bool ShowGamePass { get => Labs.Get("xbox.gamepass", "") == "1"; set => Labs.Put("xbox.gamepass", value ? "1" : ""); }

    private static string _msAccess = "";
    private static DateTime _msExpires;
    private static (string Uhs, string Token, string Xuid, DateTime Until)? _xsts;

    /// <summary>Reads the tokens Microsoft put in the address after signing in. Null when it worked, else why not.</summary>
    public static async Task<string?> XboxSignInAsync(Uri redirect)
    {
        var parts = (redirect.Query.TrimStart('?') + "&" + redirect.Fragment.TrimStart('#')).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1]));
        if (parts.TryGetValue("error", out var error))
        {
            Log($"Xbox sign-in: Microsoft answered {error}");
            return error == "access_denied" ? "Signing in was cancelled." : parts.GetValueOrDefault("error_description") ?? error;
        }
        _xsts = null;
        _msAccess = "";
        if (parts.TryGetValue("code", out var code) && code.Length > 0)
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = XboxClient, ["grant_type"] = "authorization_code", ["code"] = code, ["scope"] = XboxScope, ["redirect_uri"] = XboxRedirect,
            };
            if (!await MsTokenAsync(form, "code").ConfigureAwait(false)) return XboxError.Length > 0 ? XboxError : "Microsoft didn't finish signing in.";
        }
        else if (parts.TryGetValue("access_token", out var access) && access.Length > 0)
        {
            _msAccess = access;
            _msExpires = DateTime.UtcNow.AddSeconds(int.TryParse(parts.GetValueOrDefault("expires_in"), out var s) ? s - 60 : 3000);
            if (parts.TryGetValue("refresh_token", out var refresh)) { Labs.Put("xbox.refresh", LocalSecret.Protect(refresh)); Labs.Put("xbox.client", XboxClient); }
        }
        else { Log("Xbox sign-in: no code or token in the answer"); return "Microsoft didn't finish signing in."; }
        Log("Xbox sign-in: Microsoft signed in");
        if (await XstsAsync().ConfigureAwait(false) != null) return null;
        // Xbox Live said no: don't keep a half sign-in around (it would only fail again on every refresh).
        var why = XboxError.Length > 0 ? XboxError : "Xbox Live didn't accept the sign-in.";
        Labs.Put("xbox.refresh", "");
        _msAccess = "";
        return why;
    }

    /// <summary>Asks Microsoft for a token (from a one-time code or the saved refresh token) and keeps it.</summary>
    private static async Task<bool> MsTokenAsync(Dictionary<string, string> form, string what)
    {
        try
        {
            using var resp = await Http.PostAsync("https://login.live.com/oauth20_token.srf", new FormUrlEncodedContent(form)).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = "";
                try { using var e = JsonDocument.Parse(body); err = Str(e.RootElement, "error"); } catch (Exception) { }
                Log($"Xbox: Microsoft token ({what}) failed {(int)resp.StatusCode} {err}");
                XboxError = err == "invalid_grant" ? "Your Microsoft sign-in expired. Sign in again." : $"Microsoft sign-in didn't answer ({(int)resp.StatusCode}).";
                return false;
            }
            using var doc = JsonDocument.Parse(body);
            _msAccess = Str(doc.RootElement, "access_token");
            _msExpires = DateTime.UtcNow.AddSeconds(doc.RootElement.TryGetProperty("expires_in", out var x) && x.TryGetInt32(out var sec) ? sec - 60 : 3000);
            if (Str(doc.RootElement, "refresh_token") is { Length: > 0 } next) { Labs.Put("xbox.refresh", LocalSecret.Protect(next)); Labs.Put("xbox.client", XboxClient); }
            Log($"Xbox: Microsoft token ({what}) -> ok");
            return _msAccess.Length > 0;
        }
        catch (Exception ex) { Log($"Xbox: Microsoft token ({what}) error {ex.GetType().Name}"); XboxError = "Couldn't reach Microsoft. Check the internet connection."; return false; }
    }

    private static async Task<string?> MsAccessAsync()
    {
        if (_msAccess.Length > 0 && DateTime.UtcNow < _msExpires) return _msAccess;
        string refresh = XboxSignedIn ? LocalSecret.Unprotect(Labs.Get("xbox.refresh", "")) : "";
        if (refresh.Length == 0) { XboxError = "You're signed out of Xbox. Sign in again."; return null; }
        var form = new Dictionary<string, string>
        {
            ["client_id"] = XboxClient, ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["scope"] = XboxScope, ["redirect_uri"] = XboxRedirect,
        };
        return await MsTokenAsync(form, "refresh").ConfigureAwait(false) ? _msAccess : null;
    }

    /// <summary>The Xbox Live token (user token, then XSTS) and the account's id.</summary>
    private static async Task<(string Uhs, string Token, string Xuid, DateTime Until)?> XstsAsync()
    {
        if (_xsts is { } x && DateTime.UtcNow < x.Until) return x;
        var access = await MsAccessAsync().ConfigureAwait(false);
        if (access == null) return null;
        try
        {
            // A Microsoft (Live) sign-in goes to Xbox Live as "t=<token>"; older tools sent it bare, Azure ones as "d=".
            string userToken = "";
            foreach (var ticket in new[] { "t=" + access, access, "d=" + access })
            {
                var (user, status, _) = await PostXblAsync("https://user.auth.xboxlive.com/user/authenticate", new
                {
                    RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT",
                    Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = ticket },
                }).ConfigureAwait(false);
                using (user)
                {
                    Log($"Xbox: user token ({(ticket.StartsWith("t=") ? "t=" : ticket.StartsWith("d=") ? "d=" : "bare")}) -> {status}");
                    if (user != null) { userToken = Str(user.RootElement, "Token"); break; }
                }
            }
            if (userToken.Length == 0) { XboxError = "Xbox Live didn't accept the Microsoft sign-in. Sign in again."; return null; }

            var (xsts, xstatus, xerr) = await PostXblAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
            {
                RelyingParty = "http://xboxlive.com", TokenType = "JWT",
                Properties = new { UserTokens = new[] { userToken }, SandboxId = "RETAIL" },
            }).ConfigureAwait(false);
            using (xsts)
            {
                Log($"Xbox: XSTS -> {xstatus}{(xerr.Length > 0 ? " XErr " + xerr : "")}");
                if (xsts == null)
                {
                    XboxError = xerr switch
                    {
                        "2148916233" => "This Microsoft account has no Xbox profile yet. Sign in once at xbox.com to make one, then try again.",
                        "2148916235" => "Xbox Live isn't available in this account's country or region.",
                        "2148916236" or "2148916237" => "This account needs adult verification on xbox.com before it can sign in.",
                        "2148916238" => "This is a child account: an adult in the family needs to add it to a Microsoft family group first.",
                        _ => $"Xbox Live didn't accept the sign-in ({xstatus}).",
                    };
                    return null;
                }
                var xui = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0];
                if (Str(xui, "gtg") is { Length: > 0 } tag) { Data.XboxName = tag; SaveCache(); }
                _xsts = (Str(xui, "uhs"), Str(xsts.RootElement, "Token"), Str(xui, "xid"), DateTime.UtcNow.AddHours(12));
                if (_xsts.Value.Xuid.Length == 0) Log("Xbox: XSTS gave no account id");
                XboxError = "";
                return _xsts;
            }
        }
        catch (Exception ex) { Log($"Xbox: Xbox Live error {ex.GetType().Name}"); XboxError = "Couldn't reach Xbox Live. Check the internet connection."; return null; }
    }

    public static void XboxSignOut()
    {
        Labs.Put("xbox.refresh", "");
        Labs.Put("xbox.client", "");
        _msAccess = "";
        _xsts = null;
        Data.Xbox.Clear();
        Data.XboxName = "";
        Data.XboxAt = DateTime.MinValue;
        SaveCache();
    }

    /// <summary>The PC games played on the account (and, if chosen, the PC Game Pass catalog). Null when it couldn't.</summary>
    public static async Task<List<Entry>?> RefreshXboxAsync(CancellationToken ct)
    {
        var x = await XstsAsync().ConfigureAwait(false);
        if (x == null) return null;
        var list = new List<Entry>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://titlehub.xboxlive.com/users/xuid({x.Value.Xuid})/titles/titlehistory/decoration/detail,image,productid");
            req.Headers.TryAddWithoutValidation("Authorization", $"XBL3.0 x={x.Value.Uhs};{x.Value.Token}");
            req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "2");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            Log($"Xbox: title history -> {(int)resp.StatusCode}");
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) _xsts = null;
                XboxError = $"Xbox Live didn't send the game list ({(int)resp.StatusCode}). Try again in a moment.";
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("titles", out var titles) || titles.ValueKind != JsonValueKind.Array) { Log("Xbox: title history had no list"); titles = default; }
            if (titles.ValueKind == JsonValueKind.Array) foreach (var t in titles.EnumerateArray())
            {
                if (Str(t, "type") != "Game") continue;
                var devices = t.TryGetProperty("devices", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "").ToList() : new();
                if (!devices.Any(v => v is "PC" or "Win32")) continue;
                string pid = Str(t, "productId");
                if (pid.Length == 0 && t.TryGetProperty("detail", out var detail)) pid = Str(detail, "productId");
                if (pid.Length == 0) continue;
                list.Add(new Entry { Store = "Xbox", Id = pid, Name = Str(t, "name"), Art = Str(t, "displayImage") });
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log($"Xbox: title history error {ex.GetType().Name}"); XboxError = "Couldn't read the Xbox game list."; return null; }
        Log($"Xbox: {list.Count} PC games on the account");
        XboxError = "";

        if (ShowGamePass)
        {
            var pass = await GamePassAsync(ct).ConfigureAwait(false);
            var mine = list.Select(e => e.Id).ToHashSet();
            if (pass != null) list.AddRange(pass.Where(p => !mine.Contains(p.Id)));
        }
        list = list.Where(e => e.Name.Length > 0).GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Data.Xbox = list;
        Data.XboxAt = DateTime.Now;
        SaveCache();
        return list;
    }

    /// <summary>Every game in PC Game Pass right now (Microsoft's public catalog), with names and art.</summary>
    private static async Task<List<Entry>?> GamePassAsync(CancellationToken ct)
    {
        try
        {
            using var sigl = await GetJsonAsync("https://catalog.gamepass.com/sigls/v2?id=fdd9e2a7-0fee-49f6-ad69-4354098401ff&language=en-us&market=US", null, ct).ConfigureAwait(false);
            if (sigl == null) return null;
            var ids = sigl.RootElement.EnumerateArray().Select(e => Str(e, "id")).Where(id => id.Length > 0).Distinct().ToList();
            var list = new List<Entry>();
            foreach (var batch in ids.Chunk(20))
            {
                using var doc = await GetJsonAsync($"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={string.Join(",", batch)}&market=US&languages=en-us", null, ct).ConfigureAwait(false);
                if (doc == null) continue;
                foreach (var p in doc.RootElement.GetProperty("Products").EnumerateArray())
                {
                    var loc = p.GetProperty("LocalizedProperties")[0];
                    string art = "";
                    if (loc.TryGetProperty("Images", out var images))
                    {
                        var all = images.EnumerateArray().Select(i => (Purpose: Str(i, "ImagePurpose"), Uri: Str(i, "Uri"))).ToList();
                        art = new[] { "Poster", "BoxArt", "Tile" }.Select(k => all.FirstOrDefault(a => a.Purpose == k).Uri ?? "").FirstOrDefault(u => u.Length > 0) ?? "";
                        if (art.StartsWith("//", StringComparison.Ordinal)) art = "https:" + art;
                    }
                    list.Add(new Entry { Store = "Xbox", Id = Str(p, "ProductId"), Name = Str(loc, "ProductTitle"), Art = art, GamePass = true });
                }
            }
            return list;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    /// <summary>The game's page in the Xbox app (install, play, manage), or the Microsoft Store when there's no Xbox app.</summary>
    public static string XboxGameUri(string productId) => $"msxbox://game/?productId={Uri.EscapeDataString(productId)}";
    public static string StoreUri(string productId) => $"ms-windows-store://pdp/?productid={Uri.EscapeDataString(productId)}";

    // ================================================================ helpers

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static async Task<(JsonDocument? Doc, int Status)> GetJsonStatusAsync(string url, string? auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null) req.Headers.TryAddWithoutValidation("Authorization", auth);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return (null, (int)resp.StatusCode);
        return (JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)), (int)resp.StatusCode);
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url, string? auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null) req.Headers.TryAddWithoutValidation("Authorization", auth);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>POSTs to Xbox Live; gives the answer (null when refused), the status code and any XErr id.</summary>
    private static async Task<(JsonDocument? Doc, int Status, string XErr)> PostXblAsync(string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.IsSuccessStatusCode && text.Length > 0) return (JsonDocument.Parse(text), (int)resp.StatusCode, "");
        string xerr = "";
        try { using var err = JsonDocument.Parse(text); if (err.RootElement.TryGetProperty("XErr", out var v)) xerr = v.ToString(); } catch (Exception) { }
        // What Xbox Live said, for the log (its refusals carry error ids, not tokens).
        string headers = string.Join(" ", resp.Headers.Where(h => h.Key.StartsWith("x-err", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Key + "=" + string.Join(",", h.Value)));
        Log($"Xbox: {new Uri(url).Host} refused {(int)resp.StatusCode} {headers} {(text.Length > 160 ? text[..160] : text)}".TrimEnd());
        return (null, (int)resp.StatusCode, xerr);
    }

    private static async Task<JsonDocument?> PostJsonAsync(string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
