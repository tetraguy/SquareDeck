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
    public List<string> Screenshots = new(), Tags = new(), Systems = new();
    public string? StartProgram;
    public bool Soon;

    /// <summary>Whether SquareDeck can install it itself (a GitHub project with Windows builds).</summary>
    public bool CanInstall => Host.Length == 0 || Host.Equals("github", StringComparison.OrdinalIgnoreCase);
    public bool ForWindows => Systems.Count == 0 || Systems.Any(s => s.StartsWith("win", StringComparison.OrdinalIgnoreCase));
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
        Games = Parse(json);
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static string Url(string u) => u.Length == 0 || u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : Site + (u.StartsWith('/') ? "" : "/") + u;

    private static List<string> Strings(JsonNode? n) => n switch
    {
        JsonArray a => a.Select(Str).Where(s => s.Length > 0).ToList(),
        JsonValue => Str(n).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        _ => new List<string>()
    };

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
                Systems = Strings(node["os"]),
                Soon = node["soon"] is JsonValue sv && sv.TryGetValue<bool>(out var soon) && soon,
            };
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

    public static void Uninstall(string id)
    {
        var list = Installed();
        var record = list.FirstOrDefault(i => i.Id == id);
        if (record == null) return;
        if (Directory.Exists(record.Folder)) FileOps.Recycle(new[] { record.Folder });
        list.Remove(record);
        SaveInstalled(list);
    }

    public static void Play(RecompInstall install)
    {
        var start = new System.Diagnostics.ProcessStartInfo(install.Program) { WorkingDirectory = Path.GetDirectoryName(install.Program)!, UseShellExecute = true };
        System.Diagnostics.Process.Start(start);
    }
}
