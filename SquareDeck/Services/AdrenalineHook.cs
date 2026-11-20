using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>A game or program that can be added to AMD Software's game list.</summary>
public sealed record HookCandidate(string Name, string ExePath, string? ImagePath, string Source)
{
    public bool Selected { get; set; }
}

/// <summary>
/// Adrenaline Hook, inside SquareDeck: adds games AMD Software: Adrenalin Edition doesn't find by itself (Xbox /
/// Game Pass and Microsoft Store games above all) to its game list, so its per-game settings work for them.
/// Rebuilt from Adrenaline Hook by tetraguy (github.com/tetraguy/Adrenaline-Hook): the game list is the JSON file
/// %LOCALAPPDATA%\AMD\CN\gmdb.blb, written while AMD Software is closed, with the same entry template.
/// New here: per-game Adrenalin switches stored in that list, repairing games whose folder moved after an update,
/// adding new Xbox games by themselves, dated backups, and the Radeon overlay shortcuts.
/// </summary>
public static class AdrenalineHook
{
    public static string GmdbPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "CN", "gmdb.blb");
    private static string AmdBackup => Path.Combine(Path.GetDirectoryName(GmdbPath)!, "backup.blb");
    public static string BackupDir => Path.Combine(Settings.DataDir, "AdrenalineBackups");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // ---------------------------------------------------------------- AMD graphics and software

    /// <summary>The first AMD Radeon graphics on this PC, or "" (Adrenaline Hook needs one).</summary>
    public static string GraphicsName =>
        Hardware.Info.Gpus.Select(g => g.Name).FirstOrDefault(n => n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        ?? Hardware.Info.Gpus.Select(g => g.Name).FirstOrDefault() ?? "";

    public static bool HasAmdGraphics => Hardware.Info.Gpus.Any(g => g.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] SoftwarePaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "Radeon Software", "RadeonSoftware.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
    };

    public static string? SoftwarePath => SoftwarePaths.FirstOrDefault(File.Exists);

    /// <summary>AMD Software's version, from its registry entry (or "").</summary>
    public static string SoftwareVersion
    {
        get
        {
            try { return (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AMD\CN", "RadeonSoftwareVersion", "") as string ?? "").Trim(); }
            catch (Exception) { return ""; }
        }
    }

    public static bool SoftwareRunning => Process.GetProcessesByName("RadeonSoftware").Length > 0;

    public static bool OpenSoftware()
    {
        var path = SoftwarePath;
        return path != null && ProcessUtil.Start(path);
    }

    /// <summary>AMD Software keeps the list in memory and writes it back when it closes, so it's closed before a change.</summary>
    private static bool CloseSoftware()
    {
        bool was = false;
        foreach (var p in Process.GetProcessesByName("RadeonSoftware"))
        {
            was = true;
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
            finally { p.Dispose(); }
        }
        return was;
    }

    /// <summary>True when AMD Software was open and closed for the last change (so it can be reopened).</summary>
    public static bool ClosedForChange { get; private set; }

    // ---------------------------------------------------------------- the game list

    public static bool ListExists => File.Exists(GmdbPath);

    private static JsonObject? Load()
    {
        try
        {
            if (!File.Exists(GmdbPath)) return null;
            string json = File.ReadAllText(GmdbPath);
            return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception) { return null; }
    }

    private static JsonObject LoadOrNew() => Load() ?? new JsonObject { ["engines"] = new JsonArray(), ["games"] = new JsonArray() };

    private static void Save(JsonObject root)
    {
        ClosedForChange = CloseSoftware();
        Directory.CreateDirectory(Path.GetDirectoryName(GmdbPath)!);
        AutoBackup();
        File.WriteAllText(GmdbPath, root.ToJsonString(Indented));
    }

    private static JsonArray Games(JsonObject root)
    {
        if (root["games"] is JsonArray a) return a;
        var games = new JsonArray();
        root["games"] = games;
        return games;
    }

    private static string Str(JsonNode? node, string key)
    {
        try { return node?[key]?.ToString() ?? ""; } catch (Exception) { return ""; }
    }

    /// <summary>A game in AMD Software's list, with what AMD Software records about playing it.</summary>
    public sealed record HookedGame(string Title, string ExePath, string Guid, bool Manual, bool Missing,
        string ImageInfo, long SteamId, int PlayedMinutes, double AverageFps, double Percentile95Ms, string LastLaunch)
    {
        /// <summary>A picture for the game: its logo (Store games) or its program's icon.</summary>
        public string? IconPath => File.Exists(ImageInfo) ? ImageInfo : File.Exists(ExePath) ? ExePath : null;
    }

    private static HookedGame ToGame(JsonNode? n)
    {
        long.TryParse(Str(n, "steam_id"), out long steam);
        int.TryParse(Str(n, "total_played_mins"), out int played);
        double.TryParse(Str(n, "averageFPS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fps);
        double.TryParse(Str(n, "percentile95_msec"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p95);
        string exe = Str(n, "exe_path");
        return new HookedGame(Str(n, "title"), exe, Str(n, "guid"), Str(n, "manual") == "TRUE", exe.Length > 0 && !File.Exists(exe),
            Str(n, "image_info"), steam, played, fps, p95, Str(n, "lastlaunchtime"));
    }

    public static List<HookedGame> Hooked()
    {
        var root = Load();
        if (root == null) return new();
        return Games(root).Where(n => n != null).Select(ToGame)
            .Where(g => g.Title.Length > 0)
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Simple(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// A SquareDeck game's entry in AMD Software's list, if it has one: by Steam app id (AMD Software adds Steam games by
    /// itself), by its program or install folder, then by name (ignoring ™, ®, colons and such).
    /// </summary>
    public static HookedGame? FindFor(Game game)
    {
        var all = Hooked();
        if (all.Count == 0) return null;
        if (game.IsSteam && game.AppId > 0 && all.FirstOrDefault(h => h.SteamId == game.AppId) is { } bySteam) return bySteam;
        if (game.IsCustom && game.LaunchPath.Length > 0)
            if (all.FirstOrDefault(h => h.ExePath.Equals(game.LaunchPath, StringComparison.OrdinalIgnoreCase)) is { } byExe) return byExe;
        if (!string.IsNullOrEmpty(game.InstallDir))
        {
            string dir = game.InstallDir.TrimEnd('\\') + "\\";
            if (all.FirstOrDefault(h => h.ExePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) is { } byFolder) return byFolder;
        }
        string name = Simple(game.Name);
        return name.Length == 0 ? null : all.FirstOrDefault(h => Simple(h.Title) == name);
    }

    /// <summary>Adds one SquareDeck game to AMD Software's list, finding its program. Returns its entry, or null.</summary>
    public static async Task<HookedGame?> AddGameAsync(Game game)
    {
        var found = await ScanLibraryAsync(new[] { game });
        if (found.Count == 0) return null;
        Add(found);
        return FindFor(game);
    }

    public static HashSet<string> HookedKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in Hooked()) { set.Add(g.Title); if (g.ExePath.Length > 0) set.Add(g.ExePath); }
        return set;
    }

    /// <summary>The entry template from Adrenaline Hook (which mirrors what AMD Software writes), with safe defaults.</summary>
    private static JsonObject Entry(HookCandidate app) => new()
    {
        ["FRAMEGEN_PerfMode"] = 0, ["FRAMEGEN_SearchMode"] = 0, ["amdId"] = -1, ["appDisplayScalingSet"] = "FALSE",
        ["appHistogramCapture"] = "FALSE", ["arguments"] = "", ["athena_support"] = "FALSE", ["auto_enable_ps_state"] = "USEGLOBAL",
        ["averageFPS"] = -1, ["color_enabled"] = "FALSE", ["colors"] = new JsonArray(), ["commandline"] = "", ["exe_path"] = app.ExePath,
        ["eyefinity_enabled"] = "FALSE", ["framegen_enabled"] = 0, ["freeSyncForceSet"] = "FALSE", ["guid"] = Guid.NewGuid().ToString(),
        ["has_framegen_profile"] = "FALSE", ["has_upscaling_profile"] = "FALSE", ["hidden"] = "FALSE", ["image_info"] = app.ImagePath ?? app.ExePath,
        ["install_location"] = "", ["installer_id"] = "", ["is_ai_app"] = "FALSE", ["is_appforlink"] = "FALSE", ["is_favourite"] = "FALSE",
        ["last_played_mins"] = 0, ["lastlaunchtime"] = "", ["lastperformancereporttime"] = "", ["lnk_path"] = "",
        ["manual"] = app.Source == "Manual" ? "TRUE" : "FALSE", ["origin_id"] = -1, ["overdrive"] = new JsonArray(), ["overdrive_enabled"] = "FALSE",
        ["percentile95_msec"] = -1, ["profileCustomized"] = "FALSE", ["profileEnabled"] = "TRUE", ["rayTracing"] = "FALSE",
        ["rendering_process"] = "", ["revertuserprofiletype"] = -1, ["smartshift_enabled"] = "FALSE", ["special_flags"] = "",
        ["steam_id"] = -1, ["title"] = app.Name, ["total_played_mins"] = 0, ["uninstall_location"] = -1, ["uninstalled"] = "FALSE",
        ["uplay_id"] = -1, ["upscaling_enabled"] = "FALSE", ["upscaling_sharpness"] = 0, ["upscaling_target_resolution"] = "",
        ["upscaling_use_borderless"] = "FALSE", ["useEyefinity"] = "FALSE", ["userprofiletype"] = -1, ["week_played_mins"] = 0
    };

    /// <summary>Adds games to the list (skipping ones already there by name or program). Returns how many were added and skipped.</summary>
    public static (int Added, int Skipped) Add(IEnumerable<HookCandidate> apps)
    {
        var list = apps.ToList();
        if (list.Count == 0) return (0, 0);
        var root = LoadOrNew();
        var games = Games(root);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in games) { known.Add(Str(n, "title")); known.Add(Str(n, "exe_path")); }
        int added = 0, skipped = 0;
        foreach (var app in list)
        {
            if (known.Contains(app.Name) || known.Contains(app.ExePath)) { skipped++; continue; }
            games.Add(Entry(app));
            known.Add(app.Name); known.Add(app.ExePath);
            added++;
        }
        if (added > 0) Save(root);
        return (added, skipped);
    }

    public static int Remove(IEnumerable<string> titles)
    {
        var drop = titles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = Load();
        if (root == null || drop.Count == 0) return 0;
        var games = Games(root);
        var gone = games.Where(n => drop.Contains(Str(n, "title")) || drop.Contains(Str(n, "guid"))).ToList();
        foreach (var n in gone) games.Remove(n);
        if (gone.Count > 0) Save(root);
        return gone.Count;
    }

    // ---------------------------------------------------------------- per-game switches

    /// <summary>The Adrenalin switches stored with a game in the list (RSR, sharpness, AFMF, FreeSync, favorite, hidden).</summary>
    public sealed record GameSwitches(bool Upscaling, int Sharpness, bool FrameGen, int FrameGenMode, bool FreeSync, bool Favorite, bool Hidden);

    public static GameSwitches? Switches(string title)
    {
        var node = Find(Load(), title);
        if (node == null) return null;
        int Int(string k) { try { return int.TryParse(Str(node, k), out int v) ? v : 0; } catch (Exception) { return 0; } }
        bool True(string k) => Str(node, k).Equals("TRUE", StringComparison.OrdinalIgnoreCase) || Str(node, k) == "1";
        return new GameSwitches(True("upscaling_enabled"), Int("upscaling_sharpness"), Int("framegen_enabled") == 1,
            Int("FRAMEGEN_PerfMode"), True("freeSyncForceSet"), True("is_favourite"), True("hidden"));
    }

    /// <summary>An entry by its guid, or by its title.</summary>
    private static JsonNode? Find(JsonObject? root, string key) =>
        root == null ? null : Games(root).FirstOrDefault(n => Str(n, "guid").Equals(key, StringComparison.OrdinalIgnoreCase))
                              ?? Games(root).FirstOrDefault(n => Str(n, "title").Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Changes one game's switches and marks its profile as customized, so AMD Software reads them.</summary>
    public static bool SetSwitches(string title, GameSwitches s)
    {
        var root = Load();
        var node = Find(root, title) as JsonObject;
        if (root == null || node == null) return false;
        static string B(bool v) => v ? "TRUE" : "FALSE";
        node["upscaling_enabled"] = B(s.Upscaling);
        node["upscaling_sharpness"] = Math.Clamp(s.Sharpness, 0, 100);
        node["has_upscaling_profile"] = B(s.Upscaling);
        node["framegen_enabled"] = s.FrameGen ? 1 : 0;
        node["FRAMEGEN_PerfMode"] = Math.Clamp(s.FrameGenMode, 0, 1);
        node["has_framegen_profile"] = B(s.FrameGen);
        node["freeSyncForceSet"] = B(s.FreeSync);
        node["is_favourite"] = B(s.Favorite);
        node["hidden"] = B(s.Hidden);
        node["profileCustomized"] = "TRUE";
        node["profileEnabled"] = "TRUE";
        Save(root);
        return true;
    }

    // ---------------------------------------------------------------- finding games

    /// <summary>Xbox / Game Pass and Microsoft Store apps (Get-AppxPackage), with each one's program and logo.</summary>
    public static async Task<List<HookCandidate>> ScanStoreAsync(bool gamesOnly = false)
    {
        string json = await ProcessUtil.RunPowerShellAsync(
            "Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' } | " +
            "Select-Object Name, InstallLocation | ConvertTo-Json -Depth 2 -Compress");
        var found = new List<HookCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(json.Trim().Length > 0 ? json : "[]");
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            foreach (var el in items)
            {
                string install = el.TryGetProperty("InstallLocation", out var i) ? i.ToString() : "";
                string pkg = el.TryGetProperty("Name", out var n) ? n.ToString() : "";
                if (install.Length == 0 || !Directory.Exists(install)) continue;
                bool isGame = File.Exists(Path.Combine(install, "MicrosoftGame.config"));
                if (gamesOnly && !isGame) continue;
                string name = ManifestName(install) ?? pkg;
                if (name.Length == 0 || name.Contains("ms-resource", StringComparison.OrdinalIgnoreCase) || name.Contains("WindowsAppRuntime", StringComparison.OrdinalIgnoreCase)) continue;
                string? exe = GameExe(install) ?? FirstExe(install, 2);
                if (exe == null) continue;
                found.Add(new HookCandidate(name, exe, ManifestLogo(install), isGame ? "Xbox" : "Store"));
            }
        }
        catch (Exception) { }
        return found.GroupBy(f => f.ExePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderByDescending(f => f.Source == "Xbox").ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static XDocument? Xml(string path)
    {
        try { return File.Exists(path) ? XDocument.Load(path) : null; } catch (Exception) { return null; }
    }

    private static string? ManifestName(string install)
    {
        var doc = Xml(Path.Combine(install, "AppxManifest.xml"));
        string? name = doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? ManifestLogo(string install)
    {
        var doc = Xml(Path.Combine(install, "AppxManifest.xml"));
        string? logo = doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
        if (string.IsNullOrWhiteSpace(logo)) return null;
        string full = Path.Combine(install, logo.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    /// <summary>The program a Game Pass game declares in MicrosoftGame.config.</summary>
    private static string? GameExe(string install)
    {
        var doc = Xml(Path.Combine(install, "MicrosoftGame.config"));
        var exe = doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");
        string? name = exe?.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value ?? exe?.Value;
        if (string.IsNullOrWhiteSpace(name)) return null;
        string direct = Path.Combine(install, name);
        if (File.Exists(direct)) return direct;
        try { return Directory.EnumerateFiles(install, Path.GetFileName(name), SearchOption.AllDirectories).FirstOrDefault(); }
        catch (Exception) { return null; }
    }

    private static readonly string[] NotGames = { "unins", "crash", "setup", "redist", "vc_", "dxsetup", "dotnet", "update", "helper", "report", "launcher", "install", "easyanticheat", "be_" };

    /// <summary>The largest program in a folder (a game's main program usually is), skipping installers and helpers.</summary>
    private static string? FirstExe(string folder, int depth)
    {
        try
        {
            var files = new List<FileInfo>();
            void Walk(string dir, int level)
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.exe")) files.Add(new FileInfo(f));
                if (level >= depth) return;
                foreach (var d in Directory.EnumerateDirectories(dir)) { try { Walk(d, level + 1); } catch (Exception) { } }
            }
            Walk(folder, 0);
            return files.Where(f => !NotGames.Any(x => f.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(f => f.Length).FirstOrDefault()?.FullName;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Programs installed for everyone (Windows' uninstall list), like Adrenaline Hook's "installed software" scan.</summary>
    public static Task<List<HookCandidate>> ScanInstalledAsync() => Task.Run(() =>
    {
        var found = new List<HookCandidate>();
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var k = key.OpenSubKey(sub);
                    string name = k?.GetValue("DisplayName") as string ?? "";
                    if (name.Length == 0 || k?.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    string? exe = null;
                    string location = k?.GetValue("InstallLocation") as string ?? "";
                    if (location.Length > 0 && Directory.Exists(location)) exe = FirstExe(location, 1);
                    if (exe == null && k?.GetValue("DisplayIcon") is string icon)
                    {
                        string path = icon.Split(',')[0].Trim('"', ' ');
                        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) exe = path;
                    }
                    if (exe != null) found.Add(new HookCandidate(name, exe, exe, "Installed"));
                }
            }
            catch (Exception) { }
        }
        return found.GroupBy(f => f.ExePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    });

    /// <summary>Games in SquareDeck's library with a program SquareDeck can find (added games, and installed store games' folders).</summary>
    public static Task<List<HookCandidate>> ScanLibraryAsync(IEnumerable<Game> games) => Task.Run(() =>
    {
        var found = new List<HookCandidate>();
        foreach (var g in games.Where(g => g.IsGame && g.IsInstalled))
        {
            string? exe = null;
            if (g.IsCustom && g.LaunchPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(g.LaunchPath)) exe = g.LaunchPath;
            else if (!string.IsNullOrEmpty(g.InstallDir) && Directory.Exists(g.InstallDir)) exe = GameExe(g.InstallDir) ?? FirstExe(g.InstallDir, 2);
            if (exe != null) found.Add(new HookCandidate(g.Name, exe, null, "Library"));
        }
        return found.GroupBy(f => f.ExePath, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    });

    // ---------------------------------------------------------------- repair, auto-hook, backups

    /// <summary>
    /// After an update, an Xbox game's folder name changes with its version, so AMD Software loses it. Finds each missing
    /// game again by name among the installed Store games and points its entry at the new program. Returns how many.
    /// </summary>
    public static async Task<int> RepairAsync()
    {
        var missing = Hooked().Where(h => h.Missing).ToList();
        if (missing.Count == 0) return 0;
        var store = await ScanStoreAsync();
        var root = Load();
        if (root == null) return 0;
        int fixedCount = 0;
        foreach (var m in missing)
        {
            var match = store.FirstOrDefault(s => s.Name.Equals(m.Title, StringComparison.OrdinalIgnoreCase));
            if (match == null || Find(root, m.Title) is not JsonObject node) continue;
            node["exe_path"] = match.ExePath;
            if (match.ImagePath != null) node["image_info"] = match.ImagePath;
            fixedCount++;
        }
        if (fixedCount > 0) Save(root);
        return fixedCount;
    }

    /// <summary>Adds Xbox games that aren't in AMD Software's list yet (Settings: add new games by themselves).</summary>
    public static async Task<int> AutoHookAsync()
    {
        if (!HasAmdGraphics) return 0;
        var keys = HookedKeys();
        var fresh = (await ScanStoreAsync(gamesOnly: true)).Where(c => !keys.Contains(c.Name) && !keys.Contains(c.ExePath)).ToList();
        return fresh.Count == 0 ? 0 : Add(fresh).Added;
    }

    /// <summary>A dated copy before every change (the 15 newest are kept), plus AMD's own backup.blb from Adrenaline Hook.</summary>
    private static void AutoBackup()
    {
        try
        {
            if (!File.Exists(GmdbPath)) return;
            Directory.CreateDirectory(BackupDir);
            File.Copy(GmdbPath, Path.Combine(BackupDir, $"gmdb-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.blb"), overwrite: true);
            foreach (var old in Backups().Skip(15)) { try { File.Delete(old.Path); } catch (Exception) { } }
        }
        catch (Exception) { }
    }

    public static string? BackUpNow()
    {
        try
        {
            if (!File.Exists(GmdbPath)) return "AMD Software's game list wasn't found.";
            AutoBackup();
            File.Copy(GmdbPath, AmdBackup, overwrite: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static List<(string Path, DateTime At, int Games)> Backups()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return new();
            return Directory.EnumerateFiles(BackupDir, "gmdb-*.blb").Select(f =>
            {
                int count = 0;
                try { count = (JsonNode.Parse(File.ReadAllText(f))?["games"] as JsonArray)?.Count ?? 0; } catch (Exception) { }
                return (f, File.GetLastWriteTime(f), count);
            }).OrderByDescending(b => b.Item2).ToList();
        }
        catch (Exception) { return new(); }
    }

    public static string? Restore(string backup)
    {
        try
        {
            ClosedForChange = CloseSoftware();
            AutoBackup();
            File.Copy(backup, GmdbPath, overwrite: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Empties the list (AMD Software builds it again from the games it finds). A backup is kept first.</summary>
    public static string? StartOver()
    {
        try
        {
            ClosedForChange = CloseSoftware();
            AutoBackup();
            if (File.Exists(GmdbPath)) File.Delete(GmdbPath);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------------------------------------------------------------- Radeon overlays (AMD Software's default shortcuts)

    private const ushort Alt = 0x12, Ctrl = 0x11, Shift = 0x10;

    public static readonly (string Id, string Name, string About, ushort[] Keys)[] Shortcuts =
    {
        ("amdoverlay", "Radeon overlay", "Alt + R: AMD Software's in-game overlay", new ushort[] { Alt, 0x52 }),
        ("amdmetrics", "Performance metrics", "Ctrl + Shift + O: FPS, temperatures and clocks over the game", new ushort[] { Ctrl, Shift, 0x4F }),
        ("amdrecord", "Start or stop recording", "Ctrl + Shift + E", new ushort[] { Ctrl, Shift, 0x45 }),
        ("amdreplay", "Save instant replay", "Ctrl + Shift + S", new ushort[] { Ctrl, Shift, 0x53 }),
        ("amdscreenshot", "Screenshot", "Ctrl + Shift + I", new ushort[] { Ctrl, Shift, 0x49 }),
    };

    public static bool Send(string id)
    {
        var s = Shortcuts.FirstOrDefault(x => x.Id == id);
        if (s.Keys == null) return false;
        AllyButtons.SendKeys(s.Keys);
        return true;
    }
}
