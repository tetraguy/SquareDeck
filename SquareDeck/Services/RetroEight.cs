using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// Retro Lounge's experimental features from 8.0: box art and screenshots from ScreenScraper, a shader and a bezel
/// per console for RetroArch, starting a game from a save state, and noting which retro game is running (for
/// RetroAchievements pop-ups).
/// </summary>
public static class RetroEight
{
    // ================================================================ the game running now

    /// <summary>The Retro Lounge game being played (null when none).</summary>
    public static RetroGame? Running { get; set; }

    // ================================================================ shaders and bezels (RetroArch)

    /// <summary>A save state slot to start the next RetroArch game from (consumed by the next launch).</summary>
    public static int? PendingSlot { get; set; }

    public static string Shader(string system) => Labs.Get("ra.shader:" + system);
    public static void SetShader(string system, string path) { if (path.Length == 0) Forget("ra.shader:" + system); else Labs.Put("ra.shader:" + system, path); }
    public static string Overlay(string system) => Labs.Get("ra.overlay:" + system);
    public static void SetOverlay(string system, string path) { if (path.Length == 0) Forget("ra.overlay:" + system); else Labs.Put("ra.overlay:" + system, path); }

    private static void Forget(string key)
    {
        var s = MainWindow.CurrentSettings;
        if (s != null && s.LabValues.Remove(key)) s.Save();
    }

    /// <summary>Suggested looks per console: a shader preset found in RetroArch's own shaders folder.</summary>
    public static readonly Dictionary<string, string[]> Suggested = new()
    {
        ["nes"] = new[] { "crt/crt-royale.slangp", "crt/crt-geom.slangp", "crt/crt-easymode.slangp" },
        ["snes"] = new[] { "crt/crt-royale.slangp", "crt/crt-geom.slangp", "crt/crt-easymode.slangp" },
        ["genesis"] = new[] { "crt/crt-royale.slangp", "crt/crt-geom.slangp" },
        ["mastersystem"] = new[] { "crt/crt-easymode.slangp", "crt/crt-geom.slangp" },
        ["pcengine"] = new[] { "crt/crt-easymode.slangp", "crt/crt-geom.slangp" },
        ["psx"] = new[] { "crt/crt-easymode.slangp", "crt/crt-geom.slangp" },
        ["arcade"] = new[] { "crt/crt-royale.slangp", "crt/crt-geom.slangp" },
        ["n64"] = new[] { "crt/crt-easymode.slangp" },
        ["gb"] = new[] { "handheld/gameboy.slangp", "handheld/lcd-grid-v2.slangp", "handheld/dot.slangp" },
        ["gbc"] = new[] { "handheld/lcd-grid-v2-gbc-color.slangp", "handheld/lcd-grid-v2.slangp", "handheld/dot.slangp" },
        ["gba"] = new[] { "handheld/lcd-grid-v2-gba-color.slangp", "handheld/lcd-grid-v2.slangp", "handheld/dot.slangp" },
        ["gamegear"] = new[] { "handheld/lcd-grid-v2.slangp", "handheld/dot.slangp" },
        ["ngp"] = new[] { "handheld/lcd-grid-v2.slangp", "handheld/dot.slangp" },
        ["wonderswan"] = new[] { "handheld/lcd-grid-v2.slangp" },
        ["atarilynx"] = new[] { "handheld/lcd-grid-v2.slangp" },
    };

    private static RetroEmuInstall? RetroArch => RetroLounge.InstalledFor("retroarch");

    /// <summary>
    /// The shader kind RetroArch's video driver takes: "glsl" for the OpenGL driver ("gl"), "slang" for Vulkan,
    /// Direct3D 11/12 and "glcore". Preset names are the same in both, apart from the ending.
    /// </summary>
    public static (string Folder, string Ending) ShaderKind()
    {
        string driver = "";
        try
        {
            if (RetroArch is { } ra && File.Exists(Path.Combine(ra.Folder, "retroarch.cfg")))
                foreach (var line in File.ReadLines(Path.Combine(ra.Folder, "retroarch.cfg")))
                    if (line.TrimStart().StartsWith("video_driver ", StringComparison.Ordinal) || line.TrimStart().StartsWith("video_driver=", StringComparison.Ordinal))
                    { driver = line.Split('=', 2)[1].Trim().Trim('"'); break; }
        }
        catch (Exception) { }
        return driver == "gl" ? ("shaders_glsl", ".glslp") : ("shaders_slang", ".slangp");
    }

    /// <summary>The shader presets in RetroArch's shaders folder for its video driver, relative to it, ending in ".slangp".</summary>
    public static List<string> ShaderPresets()
    {
        var ra = RetroArch;
        if (ra == null) return new();
        var (folder, ending) = ShaderKind();
        string root = Path.Combine(ra.Folder, "shaders", folder);
        if (!Directory.Exists(root)) return new();
        try
        {
            return Directory.EnumerateFiles(root, "*" + ending, SearchOption.AllDirectories)
                .Select(f => Path.ChangeExtension(Path.GetRelativePath(root, f).Replace('\\', '/'), ".slangp"))
                .Where(f => f.StartsWith("crt/") || f.StartsWith("handheld/") || f.StartsWith("presets/") || f.StartsWith("scanlines/") || f.StartsWith("pixel-art-scaling/"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return new(); }
    }

    /// <summary>The overlays (bezels and borders) in RetroArch's overlays folder, relative to it.</summary>
    public static List<string> Overlays()
    {
        var ra = RetroArch;
        if (ra == null) return new();
        string root = Path.Combine(ra.Folder, "overlays");
        if (!Directory.Exists(root)) return new();
        try
        {
            return Directory.EnumerateFiles(root, "*.cfg", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                // On-screen gamepads aren't bezels.
                .Where(f => !f.StartsWith("gamepads/", StringComparison.OrdinalIgnoreCase) && !f.StartsWith("keyboards/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return new(); }
    }

    public static string ShaderFile(string relative)
    {
        if (RetroArch is not { } ra) return "";
        var (folder, ending) = ShaderKind();
        return Path.Combine(ra.Folder, "shaders", folder, Path.ChangeExtension(relative, ending).Replace('/', '\\'));
    }
    public static string OverlayFile(string relative) => RetroArch is { } ra ? Path.Combine(ra.Folder, "overlays", relative.Replace('/', '\\')) : "";

    /// <summary>
    /// Extra RetroArch options for a game: its console's shader and bezel (through a small config file appended to
    /// RetroArch's own for this launch only) and a save state to start from. "" when there's nothing to add.
    /// </summary>
    public static string ExtraArgs(RetroGame game, RetroEmuInstall install)
    {
        var parts = new List<string>();
        if (Labs.On("rashaders"))
        {
            var lines = new List<string>();
            string shader = Shader(game.System), overlay = Overlay(game.System);
            if (shader == "none") lines.Add("video_shader_enable = \"false\"");
            else if (shader.Length > 0 && File.Exists(ShaderFile(shader)))
            {
                lines.Add("video_shader_enable = \"true\"");
                lines.Add($"video_shader = \"{ShaderFile(shader).Replace('\\', '/')}\"");
            }
            if (overlay == "none") lines.Add("input_overlay_enable = \"false\"");
            else if (overlay.Length > 0 && File.Exists(OverlayFile(overlay)))
            {
                lines.Add("input_overlay_enable = \"true\"");
                lines.Add($"input_overlay = \"{OverlayFile(overlay).Replace('\\', '/')}\"");
                lines.Add("input_overlay_hide_in_menu = \"true\"");
                lines.Add("input_overlay_opacity = \"1.000000\"");
            }
            if (lines.Count > 0)
            {
                try
                {
                    string file = Path.Combine(RetroLounge.Folder, "Looks", game.System + ".cfg");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllLines(file, lines);
                    parts.Add($"--appendconfig \"{file}\"");
                }
                catch (Exception) { }
            }
        }
        if (PendingSlot is int slot)
        {
            PendingSlot = null;
            parts.Add("--entryslot=" + slot);
        }
        return string.Join(" ", parts);
    }

    // ================================================================ save states

    public sealed record SaveState(int Slot, string Path, DateTime When, string? Picture);

    /// <summary>A game's RetroArch save states by slot ("auto" is -1), newest first, with their pictures.</summary>
    public static List<SaveState> States(RetroGame game)
    {
        var list = new List<SaveState>();
        foreach (var f in RetroExtras.SavesFor(game).Where(f => !f.Screenshot))
        {
            string file = Path.GetFileName(f.Path);
            if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            int at = file.LastIndexOf(".state", StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            string rest = file[(at + 6)..];
            int slot = rest.Length == 0 ? 0 : rest.Equals(".auto", StringComparison.OrdinalIgnoreCase) || rest.Equals("auto", StringComparison.OrdinalIgnoreCase) ? -1
                : int.TryParse(rest, out var n) ? n : int.MinValue;
            if (slot == int.MinValue) continue;
            string png = f.Path + ".png";
            list.Add(new SaveState(slot, f.Path, f.When, File.Exists(png) ? png : null));
        }
        return list.OrderByDescending(s => s.When).ToList();
    }

    // ================================================================ ScreenScraper

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string User => Labs.Get("ss.user");
    public static string Password => Labs.Get("ss.password");
    public static string DevId => Labs.Get("ss.devid");
    public static string DevPassword => Labs.Get("ss.devpassword");
    public static bool Ready => User.Length > 0 && Password.Length > 0 && DevId.Length > 0 && DevPassword.Length > 0;

    /// <summary>ScreenScraper's console numbers, by Retro Lounge id.</summary>
    private static readonly Dictionary<string, int> SystemIds = new()
    {
        ["genesis"] = 1, ["mastersystem"] = 2, ["nes"] = 3, ["snes"] = 4, ["gb"] = 9, ["gbc"] = 10, ["virtualboy"] = 11, ["gba"] = 12,
        ["gc"] = 13, ["n64"] = 14, ["nds"] = 15, ["wii"] = 16, ["n3ds"] = 17, ["sega32x"] = 19, ["segacd"] = 20, ["gamegear"] = 21,
        ["saturn"] = 22, ["dreamcast"] = 23, ["ngp"] = 25, ["atari2600"] = 26, ["jaguar"] = 27, ["atarilynx"] = 28, ["3do"] = 29,
        ["pcengine"] = 31, ["msx"] = 113, ["atari7800"] = 41, ["wonderswan"] = 45, ["colecovision"] = 48, ["psx"] = 57, ["ps2"] = 58,
        ["psp"] = 61, ["arcade"] = 75, ["fds"] = 106, ["pcenginecd"] = 114, ["dos"] = 135, ["neogeo"] = 142,
    };

    public static string ScrapedFolder(string system) => Path.Combine(RetroLounge.Folder, "Scraped", system);

    /// <summary>A scraped cover for a game, if one was downloaded.</summary>
    public static string? Cover(RetroGame g)
    {
        string f = Path.Combine(ScrapedFolder(g.System), g.Name + ".png");
        return File.Exists(f) ? f : null;
    }

    /// <summary>A scraped screenshot for a game, if one was downloaded.</summary>
    public static string? Snap(RetroGame g)
    {
        string f = Path.Combine(ScrapedFolder(g.System), "snaps", g.Name + ".png");
        return File.Exists(f) ? f : null;
    }

    private static uint Crc32(Stream s)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; }
        uint crc = 0xFFFFFFFFu;
        var buffer = new byte[1 << 16];
        int read;
        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) crc = table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public sealed record ScrapeResult(int Covers, int Snaps, int Missed, string? Error);

    /// <summary>
    /// Looks up each game without a cover (or screenshot) on ScreenScraper, one at a time as its rules ask, and saves
    /// the box art and a screenshot in Retro Lounge's own folder. Retro Lounge then shows them first.
    /// </summary>
    public static async Task<ScrapeResult> ScrapeAsync(IEnumerable<RetroGame> games, bool onlyMissing, IProgress<(int Done, int Total, string Name)>? progress, CancellationToken ct)
    {
        if (!Ready) return new(0, 0, 0, "Enter your ScreenScraper account and developer details first.");
        var list = games.Where(g => SystemIds.ContainsKey(g.System) && (!onlyMissing || Cover(g) == null)).ToList();
        int covers = 0, snaps = 0, missed = 0, done = 0;
        foreach (var g in list)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done, list.Count, g.Title));
            try
            {
                string crc = "";
                long size = g.Size;
                if (File.Exists(g.Path) && new FileInfo(g.Path).Length < 64L * 1024 * 1024)
                {
                    await using var fs = File.OpenRead(g.Path);
                    crc = (await Task.Run(() => Crc32(fs), ct)).ToString("X8");
                    size = fs.Length;
                }
                string url = "https://api.screenscraper.fr/api2/jeuInfos.php?output=json&softname=SquareDeck" +
                             $"&devid={Uri.EscapeDataString(DevId)}&devpassword={Uri.EscapeDataString(DevPassword)}" +
                             $"&ssid={Uri.EscapeDataString(User)}&sspassword={Uri.EscapeDataString(Password)}" +
                             $"&systemeid={SystemIds[g.System]}&romtype=rom&romnom={Uri.EscapeDataString(Path.GetFileName(g.Path))}" +
                             (size > 0 ? "&romtaille=" + size : "") + (crc.Length > 0 ? "&crc=" + crc : "");
                using var response = await Http.GetAsync(url, ct);
                if ((int)response.StatusCode is 401 or 403) return new(covers, snaps, missed, "ScreenScraper didn't accept the account or developer details.");
                if ((int)response.StatusCode == 429 || (int)response.StatusCode == 430) return new(covers, snaps, missed, "ScreenScraper's limit for today has been reached. Try again tomorrow.");
                if (!response.IsSuccessStatusCode) { missed++; continue; }
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("response", out var r) || !r.TryGetProperty("jeu", out var jeu) || !jeu.TryGetProperty("medias", out var medias)) { missed++; continue; }
                string? Pick(string type)
                {
                    string[] regions = { "us", "wor", "eu", "ss", "jp" };
                    var found = medias.EnumerateArray().Where(m => m.TryGetProperty("type", out var t) && t.GetString() == type).ToList();
                    foreach (var region in regions)
                        foreach (var m in found)
                            if (m.TryGetProperty("region", out var reg) && reg.GetString() == region && m.TryGetProperty("url", out var u)) return u.GetString();
                    return found.Select(m => m.TryGetProperty("url", out var u) ? u.GetString() : null).FirstOrDefault(u => u != null);
                }
                async Task<bool> Save(string? mediaUrl, string file)
                {
                    if (mediaUrl == null) return false;
                    var data = await Http.GetByteArrayAsync(mediaUrl, ct);
                    if (data.Length < 200) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllBytesAsync(file, data, ct);
                    return true;
                }
                bool any = false;
                if (await Save(Pick("box-2D"), Path.Combine(ScrapedFolder(g.System), g.Name + ".png"))) { covers++; any = true; }
                if (await Save(Pick("ss") ?? Pick("sstitle"), Path.Combine(ScrapedFolder(g.System), "snaps", g.Name + ".png"))) { snaps++; any = true; }
                if (!any) missed++;
                else RetroLounge.ForgetArt(g);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { missed++; }
            finally { done++; }
            await Task.Delay(1200, ct); // one request at a time, gently, as ScreenScraper asks of free accounts
        }
        progress?.Report((done, list.Count, ""));
        return new(covers, snaps, missed, null);
    }
}
