using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>A folder of interface sounds in the Steam Deck "audio loader" layout: pack.json plus deck_ui_*.wav files.</summary>
public sealed record SoundPack(string Id, string Name, string Author, string Folder, bool Music,
    Dictionary<string, string[]> Mappings, HashSet<string> Ignore);

/// <summary>
/// Finds sound packs. They live in a "SoundPacks" folder next to the program (the ones that ship with it)
/// and in %LOCALAPPDATA%\SquareDeck\SoundPacks (ones the user adds). Each pack is its own sub-folder.
/// </summary>
public static class SoundPacks
{
    public static string BuiltInDir => Path.Combine(AppContext.BaseDirectory, "SoundPacks");
    public static string UserDir => Path.Combine(Settings.DataDir, "SoundPacks");

    private static List<SoundPack>? _cache;

    /// <summary>Forgets the list, so packs copied in while the launcher is open are found.</summary>
    public static void Refresh() => _cache = null;

    public static List<SoundPack> List()
    {
        if (_cache != null) return _cache;
        var packs = new List<SoundPack>();
        foreach (var root in new[] { BuiltInDir, UserDir })
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var folder in Directory.EnumerateDirectories(root))
                {
                    var pack = Read(folder);
                    // A pack in the user's folder replaces a built-in one with the same folder name.
                    if (pack == null) continue;
                    packs.RemoveAll(p => p.Id == pack.Id);
                    packs.Add(pack);
                }
            }
            catch (Exception) { }
        }
        return _cache = packs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The pack a setting such as "pack:PS5_Sounds" refers to, or null.</summary>
    public static SoundPack? Find(string? setting) =>
        setting != null && setting.StartsWith("pack:", StringComparison.Ordinal)
            ? List().FirstOrDefault(p => p.Id == setting)
            : null;

    private static SoundPack? Read(string folder)
    {
        string name = Path.GetFileName(folder), author = "";
        bool music = false;
        var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manifest = Path.Combine(folder, "pack.json");
            if (File.Exists(manifest))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString() ?? name;
                if (root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.String) author = a.GetString() ?? "";
                if (root.TryGetProperty("music", out var m) && m.ValueKind is JsonValueKind.True) music = true;
                if (root.TryGetProperty("ignore", out var ig) && ig.ValueKind == JsonValueKind.Array)
                    foreach (var item in ig.EnumerateArray())
                        if (item.GetString() is string file) ignore.Add(file);
                if (root.TryGetProperty("mappings", out var map) && map.ValueKind == JsonValueKind.Object)
                    foreach (var entry in map.EnumerateObject())
                        if (entry.Value.ValueKind == JsonValueKind.Array)
                            mappings[entry.Name] = entry.Value.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToArray();
            }
            else if (!Directory.EnumerateFiles(folder, "*.wav").Any())
            {
                return null; // not a sound pack
            }
        }
        catch (Exception)
        {
            // A broken manifest: use the files by their names.
        }
        name = name.Replace('_', ' ').Trim();
        return new SoundPack("pack:" + Path.GetFileName(folder), name, author, folder, music, mappings, ignore);
    }

    /// <summary>
    /// The file a pack provides for one of the standard sound names, trying each name in turn.
    /// Follows the pack's own "mappings" and skips sounds it lists under "ignore".
    /// </summary>
    public static string? Resolve(SoundPack pack, params string[] names)
    {
        foreach (var name in names)
        {
            if (pack.Mappings.TryGetValue(name, out var mapped))
            {
                foreach (var file in mapped)
                {
                    var path = Path.Combine(pack.Folder, file);
                    if (File.Exists(path)) return path;
                }
                continue;
            }
            if (pack.Ignore.Contains(name)) continue;
            var direct = Path.Combine(pack.Folder, name);
            if (File.Exists(direct)) return direct;
        }
        return null;
    }

    public static byte[]? ReadSound(SoundPack pack, params string[] names)
    {
        try { return Resolve(pack, names) is string path ? File.ReadAllBytes(path) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The looping background track of a music pack, if it has one.</summary>
    public static string? MusicFile(SoundPack pack)
    {
        try
        {
            return Resolve(pack, "menu_music.mp3", "menu_music.wav", "menu_music.m4a")
                   ?? Directory.EnumerateFiles(pack.Folder, "*.mp3").FirstOrDefault();
        }
        catch (Exception) { return null; }
    }
}

/// <summary>A published version of the launcher on GitHub.</summary>
public sealed record ReleaseInfo(string Tag, Version? Version, string Title, string Notes, string PageUrl,
    string? AssetUrl, string? AssetName, long AssetSize, DateTime? Published);

/// <summary>Checks the project's GitHub releases for a newer version, and downloads and installs it.</summary>
public static class Updater
{
    public const string Repo = "tetraguy/SquareDeck";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    private const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>"v2.3", "2.3.1", "release-2.3.1-beta" and similar, as a version; null if there are no numbers to read.</summary>
    public static Version? ParseVersion(string tag)
    {
        var match = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"\d+(\.\d+){1,3}");
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"\d+");
            return match.Success && int.TryParse(match.Value, out var major) ? new Version(major, 0, 0) : null;
        }
        if (!Version.TryParse(match.Value, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static bool IsNewer(ReleaseInfo release) => release.Version != null && release.Version > Current;

    /// <summary>
    /// The newest published release. Null when the project has none yet. Throws when GitHub can't be reached.
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/" + Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.GetAsync("https://api.github.com/repos/" + Repo + "/releases/latest", ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases published yet
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        string tag = Text("tag_name");
        if (tag.Length == 0) return null;

        // The file to install: an installer if there is one, otherwise a ZIP of the program.
        string? url = null, file = null;
        long size = 0;
        int best = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string link = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (!link.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string lower = name.ToLowerInvariant();
                int score = lower.EndsWith(".exe") ? (lower.Contains("setup") || lower.Contains("install") ? 5 : 4)
                    : lower.EndsWith(".msi") ? 3
                    : lower.EndsWith(".zip") ? 2 : 0;
                if (score <= best) continue;
                best = score;
                url = link;
                file = name;
                size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            }
        }

        DateTime? published = DateTime.TryParse(Text("published_at"), out var when) ? when.ToLocalTime() : null;
        string title = Text("name");
        return new ReleaseInfo(tag, ParseVersion(tag), title.Length > 0 ? title : tag, Text("body").Replace("\r\n", "\n").Trim(),
            Text("html_url") is { Length: > 0 } page ? page : ReleasesPage, url, file, size, published);
    }

    /// <summary>Release notes written in Markdown, tidied into plain lines for reading on a page.</summary>
    public static string PlainNotes(string notes)
    {
        var lines = new List<string>();
        foreach (var raw in notes.Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#")) line = trimmed.TrimStart('#').Trim().ToUpperInvariant();
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) line = "• " + trimmed[2..];
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1"); // [text](link) -> text
            line = line.Replace("**", "").Replace("__", "").Replace("`", "");
            lines.Add(line);
        }
        return string.Join("\n", lines).Trim();
    }

    /// <summary>Downloads the release's file to the temp folder and returns its path.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.AssetUrl == null || release.AssetName == null || !release.AssetUrl.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This release has no file that can be installed from here.");

        var folder = Path.Combine(Path.GetTempPath(), "SquareDeckUpdate");
        if (Directory.Exists(folder)) { try { Directory.Delete(folder, true); } catch (Exception) { } }
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(release.AssetName));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/" + Current);
        using var resp = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? release.AssetSize;
        await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }
        return path;
    }

    /// <summary>
    /// Starts the installation. An installer (.exe / .msi) is simply run. A ZIP is unpacked and copied over the
    /// program's folder by a small script that waits for the launcher to close, then starts the new version.
    /// The caller should close the launcher straight after this returns.
    /// </summary>
    public static void Install(string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is ".exe" or ".msi")
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return;
        }
        if (extension != ".zip") throw new InvalidOperationException("Don't know how to install " + Path.GetFileName(file) + ".");

        string appDir = AppContext.BaseDirectory.TrimEnd('\\');
        string exe = Environment.ProcessPath ?? Path.Combine(appDir, "SquareDeck.exe");

        // The program's folder must be writable (it isn't under Program Files without administrator rights).
        try
        {
            var probe = Path.Combine(appDir, ".update-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The launcher's folder can't be written to. Download the release and replace the files by hand.");
        }

        string unpacked = Path.Combine(Path.GetDirectoryName(file)!, "files");
        ZipFile.ExtractToDirectory(file, unpacked, overwriteFiles: true);
        // Releases are often zipped as one folder: copy what is inside it.
        string source = unpacked;
        while (Directory.GetFiles(source).Length == 0 && Directory.GetDirectories(source) is { Length: 1 } only) source = only[0];
        if (!File.Exists(Path.Combine(source, Path.GetFileName(exe))))
            throw new InvalidOperationException("The downloaded ZIP doesn't contain " + Path.GetFileName(exe) + ".");

        int pid = Environment.ProcessId;
        string script = Path.Combine(Path.GetDirectoryName(file)!, "update.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"robocopy \"{source}\" \"{appDir}\" /E /R:5 /W:1 >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
        });
    }
}
