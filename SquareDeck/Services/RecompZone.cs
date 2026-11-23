using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SquareDeck.Services;

/// <summary>One recompilation (a native PC port made from a console game's code) in the Recomp Zone catalog.</summary>
public sealed class RecompGame
{
    public string Id = "", Name = "", Game = "", Origin = "", Developer = "", Repo = "", Host = "", ApprovedTag = "";
    public string Cover = "", Hero = "", Logo = "", Blurb = "", Summary = "";
    public List<string> Screenshots = new(), Tags = new(), Systems = new(), HandheldDevices = new();
    public string? StartProgram;
    public bool Soon, WindowsBuild, HandheldOk;

    // What the list says about the recomp itself.
    public int Stars, Downloads, Releases, Players;
    public string Genre = "", License = "", Version = "", Size = "", Status = "", StatusNote = "";
    public DateTime? Released, FirstRelease, Added;

    // What it says about the original game (from IGDB).
    public string AboutReleased = "", AboutDeveloper = "", AboutPublisher = "", AboutSeries = "", AboutModes = "", AboutPlatforms = "", AboutRating = "";
    public List<string> AboutGenres = new();

    /// <summary>Whether SquareDeck can install it itself (a GitHub project with Windows builds).</summary>
    public bool CanInstall => Host.Length == 0 || Host.Equals("github", StringComparison.OrdinalIgnoreCase);
    /// <summary>Whether it has a Windows build: the list names Windows among its systems, or has a Windows download for it.</summary>
    public bool ForWindows => WindowsBuild || Systems.Any(s => s.StartsWith("win", StringComparison.OrdinalIgnoreCase));
    public string GenreName => RecompZone.GenreName(Genre);
    public string Page => Host switch
    {
        "gitlab" => "https://gitlab.com/" + Repo,
        "sourceforge" => "https://sourceforge.net/projects/" + Repo,
        "forgejo" => "https://" + Repo,
        _ => "https://github.com/" + Repo
    };
    public string ConsoleName => RecompZone.ConsoleName(Origin);
}

/// <summary>A recomp installed by SquareDeck.</summary>
public sealed class RecompInstall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Program { get; set; } = "";
    public DateTime InstalledAt { get; set; }
    /// <summary>A copy the player already had, found with "Locate installed": SquareDeck only remembers where it is.</summary>
    public bool Located { get; set; }
}

public sealed record RecompRelease(string Tag, string Name, DateTime Published, string Notes, string AssetName, string AssetUrl, long Size, string? Sha256, string Page);

/// <summary>
/// Recomp Zone (experimental): browse native PC recompilations of console games, read about them, and install,
/// update, play and uninstall them. The list is the one recomplauncher.com publishes for RecompLauncher
/// (github.com/elripalda/RecompLauncher); each game downloads straight from its developer's GitHub release,
/// checked against GitHub's checksum. Nothing here includes or downloads game files: each recomp needs your
/// own copy of the original game, as its page explains.
/// </summary>
public static class RecompZone
{
    public const string Site = "https://recomplauncher.com";
    private const string CatalogUrl = Site + "/pc-catalog.json";
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck-RecompZone/1.0 (+https://github.com/tetraguy/SquareDeck)");
        return c;
    }

    public static string Folder => Path.Combine(Settings.DataDir, "Recomp");
    public static string GamesFolder => Path.Combine(Folder, "Games");
    private static string CatalogFile => Path.Combine(Folder, "catalog.json");
    private static string InstalledFile => Path.Combine(Folder, "installed.json");

    public static List<RecompGame> Games { get; private set; } = new();
    public static bool Offline { get; private set; }

    public static string ConsoleName(string origin) => origin.ToLowerInvariant() switch
    {
        "n64" => "Nintendo 64", "gc" or "gamecube" => "GameCube", "wii" => "Wii", "wiiu" => "Wii U", "switch" => "Switch",
        "nes" => "NES", "snes" => "Super Nintendo", "gb" => "Game Boy", "gbc" => "Game Boy Color", "gba" => "Game Boy Advance",
        "ds" or "nds" => "Nintendo DS", "3ds" => "Nintendo 3DS", "ps1" or "psx" => "PlayStation", "ps2" => "PlayStation 2",
        "ps3" => "PlayStation 3", "ps4" => "PlayStation 4", "psp" => "PSP", "vita" => "PS Vita", "xbox" => "Xbox", "x360" => "Xbox 360",
        "genesis" or "md" => "Genesis / Mega Drive", "saturn" => "Saturn", "dc" or "dreamcast" => "Dreamcast", "arcade" => "Arcade",
        "pc" or "dos" => "PC", "" => "Other",
        _ => origin.ToUpperInvariant()
    };

    /// <summary>The genres the list sorts recomps into.</summary>
    public static readonly string[] GenreKeys = { "action", "adventure", "platformer", "racing", "fighting", "shooter", "sports", "rpg", "puzzle" };

    public static string GenreName(string key) => key.ToLowerInvariant() switch
    {
        "action" => "Action", "adventure" => "Adventure", "platformer" => "Platformer", "racing" => "Racing", "fighting" => "Fighting",
        "shooter" => "Shooter", "sports" => "Sports", "rpg" => "RPG", "puzzle" => "Puzzle", "" => "",
        _ => char.ToUpperInvariant(key[0]) + key[1..]
    };

    // ================================================================ the list

    /// <summary>Reads the list (from the web at most hourly, otherwise the copy on disk).</summary>
    public static async Task LoadAsync(bool force = false)
    {
        Directory.CreateDirectory(Folder);
        string? json = null;
        bool fresh = File.Exists(CatalogFile) && DateTime.Now - File.GetLastWriteTime(CatalogFile) < TimeSpan.FromHours(1);
        if (!force && fresh) json = await File.ReadAllTextAsync(CatalogFile);
        else
        {
            try
            {
                json = await Http.GetStringAsync(CatalogUrl);
                if (JsonNode.Parse(json)?["apps"] is JsonArray) await File.WriteAllTextAsync(CatalogFile, json);
                else json = null;
                Offline = false;
            }
            catch (Exception)
            {
                Offline = true;
            }
            if (json == null && File.Exists(CatalogFile)) json = await File.ReadAllTextAsync(CatalogFile);
        }
        if (json == null) throw new InvalidOperationException("The Recomp Zone list couldn't be loaded. Check the internet connection.");
        // Read on a worker thread: the list is large, and reading it on the screen's thread froze the launcher.
        var text = json;
        Games = await Task.Run(() => Parse(text));
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static string Url(string u) => u.Length == 0 || u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : Site + (u.StartsWith('/') ? "" : "/") + u;

    private static List<string> Strings(JsonNode? n) => n switch
    {
        JsonArray a => a.Select(Str).Where(s => s.Length > 0).ToList(),
        JsonValue => Str(n).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        _ => new List<string>()
    };

    private static int Int(JsonNode? n)
    {
        if (n is not JsonValue v) return n is JsonArray a ? a.Count : 0;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)Math.Min(int.MaxValue, l);
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return int.TryParse(Str(n), out var p) ? p : 0;
    }

    private static DateTime? Date(JsonNode? n)
    {
        if (n is JsonValue v && v.TryGetValue<long>(out var unix) && unix > 0) return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        return DateTime.TryParse(Str(n), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : null;
    }

    /// <summary>A fact that may be text or a list of names.</summary>
    private static string Joined(JsonNode? n) => n is JsonArray ? string.Join(", ", Strings(n)) : Str(n) is { } s && s.Length > 0 ? s : n is JsonValue v && v.TryGetValue<double>(out var d) ? d.ToString("0.#") : "";

    private static List<RecompGame> Parse(string json)
    {
        var list = new List<RecompGame>();
        if (JsonNode.Parse(json)?["apps"] is not JsonArray apps) return list;
        foreach (var node in apps.OfType<JsonObject>())
        {
            var g = new RecompGame
            {
                Id = Str(node["id"]),
                Name = Str(node["name"]),
                Game = Str(node["game"]),
                Origin = Str(node["origin"]),
                Developer = Str(node["dev"]),
                Repo = Str(node["repo"]),
                Host = Str(node["host"]),
                ApprovedTag = Str(node["tag"]),
                Cover = Url(Str(node["cover"])),
                Hero = Url(Str(node["hero"])),
                Logo = Url(Str(node["logo"])),
                Blurb = Str(node["blurb"]),
                Summary = Str(node["about"]?["summary"]),
                Screenshots = Strings(node["screenshots"]).Select(Url).Where(u => u.StartsWith("https://")).ToList(),
                Tags = Strings(node["tags"]),
                Systems = Strings(node["systems"]).Concat(Strings(node["os"])).Select(x => x.ToLowerInvariant()).Distinct().ToList(),
                WindowsBuild = node["assets"] is JsonObject assets && assets["windows"] != null,
                Soon = node["soon"] is JsonValue sv && sv.TryGetValue<bool>(out var soon) && soon,
                Stars = Int(node["stars"]),
                Downloads = Int(node["downloads"]),
                Releases = Int(node["releases"]),
                Players = Int(node["players"]),
                Genre = Str(node["genre"]).ToLowerInvariant(),
                License = Str(node["license"]),
                Version = Str(node["version"]),
                Size = Str(node["size"]),
                Status = Str(node["status"]).ToLowerInvariant(),
                StatusNote = Str(node["status_note"]),
                Released = Date(node["released"]),
                FirstRelease = Date(node["first"]),
                Added = Date(node["added"]),
            };
            if (node["handheld"] is JsonObject hh)
            {
                g.HandheldOk = hh["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var good) && good;
                g.HandheldDevices = Strings(hh["devices"]);
            }
            if (node["about"] is JsonObject about)
            {
                g.AboutReleased = Str(about["released"]);
                g.AboutDeveloper = Joined(about["developer"]);
                g.AboutPublisher = Joined(about["publisher"]);
                g.AboutSeries = Joined(about["series"]);
                g.AboutGenres = Strings(about["genres"]);
                g.AboutModes = Joined(about["modes"]);
                g.AboutPlatforms = Joined(about["platforms"]);
                g.AboutRating = Joined(about["rating"]);
            }
            if (node["launcher"]?["start"]?["program"] is { } program)
            {
                // A program name, a list of them (one per system), or a map of system to program.
                string? pick = program switch
                {
                    JsonValue => Str(program),
                    JsonArray a => a.Select(Str).FirstOrDefault(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? a.Select(Str).FirstOrDefault(),
                    JsonObject o => Str(o["windows"]) is { Length: > 0 } w ? w : null,
                    _ => null
                };
                if (pick is { Length: > 0 } && !pick.Contains('{')) g.StartProgram = pick.Replace('/', '\\');
            }
            if (g.Name.Length == 0) g.Name = g.Game;
            if (g.Id.Length == 0 || g.Name.Length == 0 || g.Repo.Length == 0) continue;
            list.Add(g);
        }
        return list;
    }

    // ================================================================ pictures
    //
    // Covers used to download at full size, all at once, every time the page opened. Now each comes as the site's
    // small copy (160 or 400 pixels wide, made for exactly this), a few at a time, decoded at the size it's shown,
    // and kept on disk so the next visit shows them at once and offline.

    private const string ThumbService = "https://recompstore.com/api/thumb.php";
    private static readonly System.Text.RegularExpressions.Regex MediaFile = new(@"^https://(?:www\.)?recomp(?:store|launcher)\.com/media/([A-Za-z0-9][A-Za-z0-9._-]{0,150}\.(?:jpe?g|png|webp))$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly SemaphoreSlim PictureGate = new(6);
    private static readonly Dictionary<string, System.Windows.Media.ImageSource> Pictures = new();
    private static string PicturesFolder => Path.Combine(Folder, "Pictures");

    /// <summary>The site's own smaller copy of one of its pictures (160, 400 or 640 pixels wide).</summary>
    public static string Thumb(string url, int width)
    {
        var m = MediaFile.Match(url);
        int w = width <= 160 ? 160 : width <= 400 ? 400 : 640;
        return m.Success ? $"{ThumbService}?f={Uri.EscapeDataString(m.Groups[1].Value)}&w={w}" : url;
    }

    /// <summary>A picture at the size it's shown: from memory, then disk, then the web (a few at a time).</summary>
    public static async Task<System.Windows.Media.ImageSource?> PictureAsync(string url, int width, CancellationToken ct = default)
    {
        if (url.Length == 0) return null;
        string key = width + "|" + url;
        lock (Pictures) if (Pictures.TryGetValue(key, out var known)) return known;
        string file = PictureFile(url, width);
        System.Windows.Media.ImageSource? image = null;
        await PictureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(file)) image = ImageLoader.FromBytes(await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false), width);
            if (image == null)
            {
                foreach (var source in new[] { Thumb(url, width), url }.Distinct())
                {
                    try
                    {
                        var data = await Http.GetByteArrayAsync(source, ct).ConfigureAwait(false);
                        image = ImageLoader.FromBytes(data, width);
                        if (image == null) continue;
                        Directory.CreateDirectory(PicturesFolder);
                        await File.WriteAllBytesAsync(file, data, ct).ConfigureAwait(false);
                        break;
                    }
                    catch (HttpRequestException) { }
                }
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { }
        finally { PictureGate.Release(); }
        if (image != null)
            lock (Pictures)
            {
                if (Pictures.Count > 400) Pictures.Clear();
                Pictures[key] = image;
            }
        return image;
    }

    private static string PictureFile(string url, int width) =>
        Path.Combine(PicturesFolder, Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(width + "|" + url)))[..20] + ".img");

    /// <summary>True when a picture is already kept on disk, so it shows at once.</summary>
    public static bool HasPicture(string url, int width) => url.Length > 0 && File.Exists(PictureFile(url, width));

    /// <summary>A JPEG or PNG (what the page can show), not an error page or a format it can't read.</summary>
    private static bool LooksLikePicture(byte[] d) =>
        d.Length > 8 && ((d[0] == 0xFF && d[1] == 0xD8) || (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47));

    /// <summary>
    /// Downloads covers to the disk cache ahead of time (not decoded: that happens as each scrolls into view), eight
    /// at a time, reporting how many are done. Ones already on disk count at once.
    /// </summary>
    public static async Task PrefetchAsync(IReadOnlyList<string> urls, int width, IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        int done = 0, total = urls.Count;
        Directory.CreateDirectory(PicturesFolder);
        using var gate = new SemaphoreSlim(8);
        var work = urls.Select(async url =>
        {
            string file = PictureFile(url, width);
            try
            {
                if (File.Exists(file)) return;
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    foreach (var source in new[] { Thumb(url, width), url }.Distinct())
                    {
                        using var each = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        each.CancelAfter(TimeSpan.FromSeconds(15));
                        try
                        {
                            var data = await Http.GetByteArrayAsync(source, each.Token).ConfigureAwait(false);
                            if (!LooksLikePicture(data)) continue;
                            await File.WriteAllBytesAsync(file, data, ct).ConfigureAwait(false);
                            break;
                        }
                        catch (HttpRequestException) { }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // this one took too long
                    }
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally { progress.Report((Interlocked.Increment(ref done), total)); }
        }).ToList();
        await Task.WhenAll(work).ConfigureAwait(false);
    }

    /// <summary>Drops the pictures kept in memory (the page closed; the disk copies stay).</summary>
    public static void ForgetPictures() { lock (Pictures) Pictures.Clear(); }

    // ================================================================ releases

    /// <summary>The release to install: the version the catalog approved, or the newest full release, with its Windows download.</summary>
    public static async Task<RecompRelease?> ReleaseAsync(RecompGame game, CancellationToken ct = default)
    {
        if (!game.CanInstall) return null;
        var text = await Http.GetStringAsync($"https://api.github.com/repos/{game.Repo}/releases?per_page=15", ct);
        if (JsonNode.Parse(text) is not JsonArray releases) return null;
        var usable = releases.OfType<JsonObject>().Where(r => !(r["draft"]?.GetValue<bool>() ?? false)).ToList();
        JsonObject? chosen = null;
        if (game.ApprovedTag.Length > 0) chosen = usable.FirstOrDefault(r => Str(r["tag_name"]) == game.ApprovedTag);
        chosen ??= usable.FirstOrDefault(r => !(r["prerelease"]?.GetValue<bool>() ?? false) && WindowsAsset(r) != null) ?? usable.FirstOrDefault(r => WindowsAsset(r) != null);
        if (chosen == null) return null;
        var asset = WindowsAsset(chosen)!;
        string digest = Str(asset["digest"]);
        DateTime.TryParse(Str(chosen["published_at"]), out var published);
        return new RecompRelease(Str(chosen["tag_name"]), Str(chosen["name"]) is { Length: > 0 } n ? n : Str(chosen["tag_name"]), published,
            Str(chosen["body"]), Str(asset["name"]), Str(asset["browser_download_url"]), asset["size"]?.GetValue<long>() ?? 0,
            digest.StartsWith("sha256:") ? digest[7..] : null, Str(chosen["html_url"]));
    }

    /// <summary>The release's download for 64-bit Windows: a .zip (or a lone .exe), never Linux, Mac or ARM builds.</summary>
    private static JsonObject? WindowsAsset(JsonObject release)
    {
        if (release["assets"] is not JsonArray assets) return null;
        var files = assets.OfType<JsonObject>().Select(a => (Node: a, Name: Str(a["name"]).ToLowerInvariant())).ToList();
        bool NotOther((JsonObject Node, string Name) f) => !System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"linux|macos|mac-|osx|darwin|appimage|arm64|aarch64|flatpak|\.deb$|\.rpm$|\.dmg$|\.tar|android|\.apk$|source|src\.");
        bool Win((JsonObject Node, string Name) f) => System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"win|windows|x64|x86_64|amd64|pc");
        return files.Where(f => f.Name.EndsWith(".zip") && NotOther(f) && Win(f)).Select(f => f.Node).FirstOrDefault()
               ?? files.Where(f => f.Name.EndsWith(".zip") && NotOther(f)).Select(f => f.Node).FirstOrDefault()
               ?? files.Where(f => f.Name.EndsWith(".exe") && NotOther(f) && !f.Name.Contains("setup") && !f.Name.Contains("install")).Select(f => f.Node).FirstOrDefault();
    }

    // ================================================================ installed games

    public static List<RecompInstall> Installed()
    {
        try { return File.Exists(InstalledFile) ? JsonSerializer.Deserialize<List<RecompInstall>>(File.ReadAllText(InstalledFile)) ?? new() : new(); }
        catch (Exception) { return new(); }
    }

    private static void SaveInstalled(List<RecompInstall> list)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(InstalledFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static RecompInstall? InstalledFor(string id) => Installed().FirstOrDefault(i => i.Id == id && File.Exists(i.Program));

    /// <summary>
    /// Downloads and installs (or updates) a recomp: the file is checked against GitHub's checksum, unpacked into its
    /// own folder (an update unpacks over the old version, keeping anything the game made), and its program found.
    /// </summary>
    public static async Task<RecompInstall> InstallAsync(RecompGame game, RecompRelease release, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(GamesFolder);
        string temp = Path.Combine(Folder, "Downloads");
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, release.AssetName);
        using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(file);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report((done, total));
            }
        }
        if (release.Sha256 is { Length: 64 } expected)
        {
            string actual;
            await using (var check = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidOperationException("The download didn't match GitHub's checksum, so it wasn't installed. Try again.");
            }
        }

        string folder = Path.Combine(GamesFolder, SafeName(game.Id));
        Directory.CreateDirectory(folder);
        await Task.Run(() =>
        {
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(file);
                // A zip that holds a single folder: its contents go straight into the game's folder.
                var tops = zip.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
                string strip = tops.Count == 1 && zip.Entries.Any(e => e.FullName.Contains('/')) ? tops[0] + "/" : "";
                string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (strip.Length > 0) name = name.StartsWith(strip) ? name[strip.Length..] : name;
                    if (name.Length == 0) continue;
                    string target = Path.GetFullPath(Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // nothing may land outside the folder
                    if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            else File.Copy(file, Path.Combine(folder, release.AssetName), overwrite: true);
            File.Delete(file);
        }, ct);

        string program = FindProgram(folder, game.StartProgram)
                         ?? throw new InvalidOperationException("The game was unpacked, but its program couldn't be found. Open its folder to start it yourself.");
        var record = new RecompInstall { Id = game.Id, Name = game.Name, Tag = release.Tag, Folder = folder, Program = program, InstalledAt = DateTime.Now };
        var list = Installed();
        list.RemoveAll(i => i.Id == game.Id);
        list.Add(record);
        SaveInstalled(list);
        return record;
    }

    private static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>The game's program: the one the catalog names, otherwise the biggest .exe that isn't a helper.</summary>
    public static string? FindProgram(string folder, string? named)
    {
        if (named != null)
        {
            string direct = Path.Combine(folder, named);
            if (File.Exists(direct)) return direct;
            var match = Directory.EnumerateFiles(folder, Path.GetFileName(named), SearchOption.AllDirectories).FirstOrDefault();
            if (match != null) return match;
        }
        return Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
            .Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"unins|crash|redist|vc_|dxsetup|setup|update|helper|launcher_update", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ThenByDescending(p => new FileInfo(p).Length)
            .FirstOrDefault();
    }

    /// <summary>Remembers a copy the player installed themselves (its program), so it can be played and updated from here.</summary>
    public static RecompInstall Locate(RecompGame game, string program)
    {
        var record = new RecompInstall { Id = game.Id, Name = game.Name, Tag = "", Folder = Path.GetDirectoryName(program)!, Program = program, InstalledAt = DateTime.Now, Located = true };
        var list = Installed();
        list.RemoveAll(i => i.Id == game.Id);
        list.Add(record);
        SaveInstalled(list);
        return record;
    }

    /// <summary>Removes a recomp: SquareDeck's own installs go to the Recycle Bin; a located copy is only forgotten.</summary>
    public static void Uninstall(string id)
    {
        var list = Installed();
        var record = list.FirstOrDefault(i => i.Id == id);
        if (record == null) return;
        if (!record.Located && Directory.Exists(record.Folder)) FileOps.Recycle(new[] { record.Folder });
        list.Remove(record);
        SaveInstalled(list);
    }

    public static void Play(RecompInstall install)
    {
        var start = new System.Diagnostics.ProcessStartInfo(install.Program) { WorkingDirectory = Path.GetDirectoryName(install.Program)!, UseShellExecute = true };
        System.Diagnostics.Process.Start(start);
    }
}
