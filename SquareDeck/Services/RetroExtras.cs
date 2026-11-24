using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace SquareDeck.Services;

/// <summary>
/// Retro Lounge's extras: controller hotkeys per emulator (RetroArch's are set by Retro Lounge so the help is true),
/// a game's save states and screenshots, RetroAchievements, and checking ROMs against No-Intro / Redump DAT files.
/// </summary>
public static class RetroExtras
{
    // ================================================================ hotkeys

    /// <summary>
    /// RetroArch's controller combos, written into its retroarch.cfg before a game starts unless you've set your own:
    /// the menu on both sticks pressed in (L3 + R3), quitting on LB + RB + Start + Back. The values are RetroArch's
    /// "gamepad combo" choices (2 = L3 + R3, 3 = L1 + R1 + Start + Select).
    /// </summary>
    public static void EnsureRetroArchHotkeys(RetroEmuInstall ra)
    {
        try
        {
            string cfg = Path.Combine(ra.Folder, "retroarch.cfg");
            var lines = File.Exists(cfg) ? File.ReadAllLines(cfg).ToList() : new List<string>();
            bool changed = false;
            void Want(string key, string value)
            {
                int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + " ", StringComparison.Ordinal) || l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
                if (i >= 0 && !lines[i].Contains("\"0\"")) return; // you chose a combo of your own: kept
                string line = $"{key} = \"{value}\"";
                if (i >= 0) { if (lines[i] != line) { lines[i] = line; changed = true; } }
                else { lines.Add(line); changed = true; }
            }
            Want("input_menu_toggle_gamepad_combo", "2");
            Want("input_quit_gamepad_combo", "3");
            if (changed) File.WriteAllLines(cfg, lines);
        }
        catch (Exception) { }
    }

    /// <summary>What the buttons do in an emulator, as lines of (what, how).</summary>
    public static List<(string What, string How)> Hotkeys(string emulatorId) => emulatorId switch
    {
        "retroarch" => new()
        {
            ("Open RetroArch's menu", "Press both sticks in (L3 + R3)"),
            ("Save or load a state, reset, change discs", "In the menu's Quick Menu"),
            ("Quit back to SquareDeck", "LB + RB + Start + Back (View + Menu)"),
            ("Change the buttons", "Menu > Settings > Input > Hotkeys"),
        },
        "dolphin" => new()
        {
            ("Save a state", "Shift + F1 to F8 (a slot each)"),
            ("Load a state", "F1 to F8"),
            ("Quit", "Escape, or close the window"),
            ("Controller hotkeys", "Options > Hotkey Settings"),
        },
        "pcsx2" => new()
        {
            ("Save a state", "F1 (F2 picks the slot)"),
            ("Load a state", "F3"),
            ("Pause menu", "Escape"),
            ("Controller hotkeys", "Settings > Hotkeys"),
        },
        "ppsspp" => new()
        {
            ("Pause menu (save and load states there)", "Escape, or the controller's pause button"),
            ("Save a state", "F2"),
            ("Load a state", "F4"),
            ("Controller hotkeys", "Settings > Controls > Control mapping"),
        },
        "mgba" => new()
        {
            ("Save a state", "Shift + F1 to F9"),
            ("Load a state", "F1 to F9"),
            ("Fast forward", "Tab (hold)"),
            ("Controller hotkeys", "Tools > Settings > Shortcuts"),
        },
        _ => new()
        {
            ("Quit back to SquareDeck", "Close the emulator (Alt + F4 on the keyboard)"),
            ("Save states and hotkeys", "In the emulator's own settings"),
        },
    };

    // ================================================================ save states and screenshots

    public sealed record SaveFile(string Path, DateTime When, bool Screenshot);

    /// <summary>The emulator a game uses, and its install, when installed.</summary>
    private static (string EmuId, RetroEmuInstall? Install) EmulatorFor(RetroGame game)
    {
        var system = RetroLounge.SystemOf(game.System);
        if (system == null) return ("", null);
        string player = RetroLounge.PlayerFor(system, game);
        string emuId = player.StartsWith("ra:") ? "retroarch" : player;
        return (emuId, emuId.Length > 0 ? RetroLounge.InstalledFor(emuId) : null);
    }

    public static string EmulatorIdFor(RetroGame game) => EmulatorFor(game).EmuId;

    /// <summary>
    /// A game's save states and screenshots, newest first. RetroArch names them after the game's file (in "states" and
    /// "screenshots", sorted into a folder per core or not); other emulators keep their own names, so only RetroArch's
    /// are found.
    /// </summary>
    public static List<SaveFile> SavesFor(RetroGame game)
    {
        var list = new List<SaveFile>();
        var (emuId, install) = EmulatorFor(game);
        if (emuId != "retroarch" || install == null) return list;
        string name = Path.GetFileNameWithoutExtension(game.Discs.Count > 1 ? Path.Combine(RetroLounge.Folder, "Playlists", RetroLounge.Hash(game.Key)) : game.Path);
        void Look(string folder, bool shots)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, name + "*", SearchOption.AllDirectories))
                {
                    string file = Path.GetFileName(f).ToLowerInvariant();
                    bool isShot = file.EndsWith(".png"), isState = file.Contains(".state") && !isShot;
                    if (shots ? !isShot : !isState) continue;
                    list.Add(new SaveFile(f, File.GetLastWriteTime(f), shots));
                }
            }
            catch (Exception) { }
        }
        Look(Path.Combine(install.Folder, "states"), false);
        Look(Path.Combine(install.Folder, "screenshots"), true);
        return list.OrderByDescending(f => f.When).ToList();
    }

    public static string? StatesFolder(RetroGame game)
    {
        var (emuId, install) = EmulatorFor(game);
        if (install == null) return null;
        string states = Path.Combine(install.Folder, "states");
        return emuId == "retroarch" && Directory.Exists(states) ? states : install.Folder;
    }

    // ================================================================ RetroAchievements

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static RetroExtras()
    {
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/" + Updater.Current); } catch (Exception) { }
    }

    public sealed record RaAchievement(string Title, string Description, int Points, DateTime? Earned, string Badge);
    public sealed record RaProgress(int GameId, string Title, int Total, int Earned, int Points, int PointsEarned, List<RaAchievement> Achievements);

    /// <summary>Consoles RetroAchievements hashes as the whole file (some without a header), by Retro Lounge id.</summary>
    private static readonly HashSet<string> WholeFile = new() { "gb", "gbc", "gba", "genesis", "mastersystem", "gamegear", "atari2600", "n64", "sega32x", "wonderswan", "pcengine" };

    public static bool CanHash(RetroGame game) =>
        (WholeFile.Contains(game.System) || game.System is "nes" or "snes") && File.Exists(game.Path) && game.Size < 128L * 1024 * 1024;

    /// <summary>The ROM's bytes (the first file inside a .zip), or null for anything else.</summary>
    private static byte[]? ReadRom(string path)
    {
        try
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.Where(e => e.Length > 0).OrderByDescending(e => e.Length).FirstOrDefault();
                if (entry == null) return null;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return null;
            return File.ReadAllBytes(path);
        }
        catch (Exception) { return null; }
    }

    /// <summary>RetroAchievements' hash of a cartridge game (MD5, without the NES and SNES copier headers; N64 as big-endian).</summary>
    public static string? RaHash(RetroGame game)
    {
        if (!CanHash(game)) return null;
        var data = ReadRom(game.Path);
        if (data == null || data.Length == 0) return null;
        int skip = 0;
        if (game.System == "nes" && data.Length >= 16 && data[0] == 'N' && data[1] == 'E' && data[2] == 'S' && data[3] == 0x1A) skip = 16;
        if (game.System == "snes" && data.Length % 1024 == 512) skip = 512;
        if (game.System == "n64" && data.Length >= 4)
        {
            if (data[0] == 0x37 && data[1] == 0x80) for (int i = 0; i + 1 < data.Length; i += 2) (data[i], data[i + 1]) = (data[i + 1], data[i]);                       // .v64
            else if (data[0] == 0x40 && data[1] == 0x12) for (int i = 0; i + 3 < data.Length; i += 4) { (data[i], data[i + 3]) = (data[i + 3], data[i]); (data[i + 1], data[i + 2]) = (data[i + 2], data[i + 1]); } // .n64
        }
        return Convert.ToHexString(MD5.HashData(data.AsSpan(skip))).ToLowerInvariant();
    }

    private static string Q(string s) => Uri.EscapeDataString(s);

    /// <summary>Signs in: checks the user name and web API key against your profile. Null when they work.</summary>
    public static async Task<string?> CheckSignInAsync(string user, string key)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://retroachievements.org/API/API_GetUserProfile.php?u={Q(user)}&y={Q(key)}");
            var node = JsonNode.Parse(json);
            return node?["User"] != null ? null : "RetroAchievements didn't recognize that user name and key.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        { return "RetroAchievements didn't accept that web API key."; }
        catch (Exception ex) { return "Couldn't reach RetroAchievements: " + ex.Message; }
    }

    private static readonly Dictionary<string, int> GameIds = new();

    /// <summary>Your progress in a game (null when it isn't on RetroAchievements, or you aren't signed in).</summary>
    public static async Task<RaProgress?> ProgressAsync(RetroGame game, Settings s, CancellationToken ct = default)
    {
        if (s.RaUser.Length == 0 || s.RaApiKey.Length == 0) return null;
        try
        {
            if (!GameIds.TryGetValue(game.Key, out int id))
            {
                string? hash = await Task.Run(() => RaHash(game), ct);
                if (hash == null) return null;
                var found = JsonNode.Parse(await Http.GetStringAsync($"https://retroachievements.org/dorequest.php?r=gameid&m={hash}", ct));
                id = found?["GameID"]?.GetValue<int>() ?? 0;
                GameIds[game.Key] = id;
            }
            if (id <= 0) return null;
            var node = JsonNode.Parse(await Http.GetStringAsync(
                $"https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?g={id}&u={Q(s.RaUser)}&y={Q(s.RaApiKey)}", ct));
            if (node == null) return null;
            var list = new List<RaAchievement>();
            if (node["Achievements"] is JsonObject all)
                foreach (var (_, a) in all)
                {
                    if (a == null) continue;
                    DateTime? earned = null;
                    string? when = a["DateEarnedHardcore"]?.GetValue<string?>() ?? a["DateEarned"]?.GetValue<string?>();
                    if (!string.IsNullOrEmpty(when) && DateTime.TryParse(when, out var d)) earned = d;
                    list.Add(new RaAchievement(a["Title"]?.GetValue<string>() ?? "", a["Description"]?.GetValue<string>() ?? "",
                        a["Points"]?.GetValue<int>() ?? 0, earned, a["BadgeName"]?.GetValue<string>() ?? ""));
                }
            int total = list.Count, got = list.Count(a => a.Earned != null);
            return new RaProgress(id, node["Title"]?.GetValue<string>() ?? game.Title, total, got,
                list.Sum(a => a.Points), list.Where(a => a.Earned != null).Sum(a => a.Points),
                list.OrderByDescending(a => a.Earned != null).ThenByDescending(a => a.Earned).ToList());
        }
        catch (Exception) { return null; }
    }

    /// <summary>Turns achievements on in RetroArch for your account (its password is entered once in RetroArch itself).</summary>
    public static bool EnableInRetroArch(string user)
    {
        if (RetroLounge.InstalledFor("retroarch") is not { } ra) return false;
        try
        {
            string cfg = Path.Combine(ra.Folder, "retroarch.cfg");
            var lines = File.Exists(cfg) ? File.ReadAllLines(cfg).ToList() : new List<string>();
            void Set(string key, string value)
            {
                int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + " ", StringComparison.Ordinal) || l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
                string line = $"{key} = \"{value}\"";
                if (i >= 0) lines[i] = line; else lines.Add(line);
            }
            Set("cheevos_enable", "true");
            Set("cheevos_username", user);
            File.WriteAllLines(cfg, lines);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ================================================================ DAT files (No-Intro, Redump)

    public enum Verdict { Verified, Unknown, Duplicate, Skipped }

    public sealed record VerifyResult(string Path, string System, Verdict Verdict, string? DatName, string? Duplicate);

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(Stream s)
    {
        uint crc = 0xFFFFFFFFu;
        var buffer = new byte[1 << 16];
        int read;
        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Every ROM entry in the DAT files in <paramref name="folder"/>: CRC32 to the game's name in the DAT.</summary>
    public static Dictionary<uint, string> LoadDats(string folder)
    {
        var map = new Dictionary<uint, string>();
        if (!Directory.Exists(folder)) return map;
        foreach (var file in Directory.EnumerateFiles(folder, "*.dat", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories)))
        {
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                string game = "";
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Name is "game" or "machine") game = reader.GetAttribute("name") ?? "";
                    else if (reader.Name == "rom" && reader.GetAttribute("crc") is { Length: 8 } crc && uint.TryParse(crc, System.Globalization.NumberStyles.HexNumber, null, out var value))
                        map.TryAdd(value, game);
                }
            }
            catch (Exception) { }
        }
        return map;
    }

    /// <summary>
    /// Checks every cartridge ROM (single files, or the files inside a .zip) against the DATs: Verified (a known good
    /// dump), Unknown (not in any DAT: a bad dump, a hack or translation, or a console whose DAT you don't have), or
    /// Duplicate (the same data as another file). Disc games and folders are skipped.
    /// </summary>
    public static async Task<List<VerifyResult>> VerifyAsync(string datFolder, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var dats = await Task.Run(() => LoadDats(datFolder), ct);
        var games = RetroLounge.Games.Where(g => RetroLounge.PeekState(g)?.Hidden != true).ToList();
        return await Task.Run(() =>
        {
            var results = new List<VerifyResult>();
            var seen = new Dictionary<uint, string>();
            int done = 0;
            foreach (var g in games)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((done++, games.Count));
                bool disc = RetroLounge.SystemOf(g.System)?.Disc == true;
                if (disc || !File.Exists(g.Path) || g.Size > 512L * 1024 * 1024) { results.Add(new VerifyResult(g.Path, g.System, Verdict.Skipped, null, null)); continue; }
                uint? crc = null;
                try
                {
                    if (g.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using var zip = ZipFile.OpenRead(g.Path);
                        var entry = zip.Entries.Where(e => e.Length > 0).OrderByDescending(e => e.Length).FirstOrDefault();
                        if (entry != null) crc = entry.Crc32;
                    }
                    else if (!g.Path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        using var file = File.OpenRead(g.Path);
                        crc = Crc32(file);
                    }
                }
                catch (Exception) { }
                if (crc is not uint value) { results.Add(new VerifyResult(g.Path, g.System, Verdict.Skipped, null, null)); continue; }
                if (seen.TryGetValue(value, out var first)) { results.Add(new VerifyResult(g.Path, g.System, Verdict.Duplicate, dats.GetValueOrDefault(value), first)); continue; }
                seen[value] = g.Path;
                results.Add(dats.TryGetValue(value, out var name)
                    ? new VerifyResult(g.Path, g.System, Verdict.Verified, name, null)
                    : new VerifyResult(g.Path, g.System, Verdict.Unknown, null, null));
            }
            progress?.Report((games.Count, games.Count));
            try { File.WriteAllText(VerifyFile, JsonSerializer.Serialize(results)); } catch (Exception) { }
            return results;
        }, ct);
    }

    private static string VerifyFile => Path.Combine(RetroLounge.Folder, "verify.json");

    /// <summary>The last check's results (empty when there hasn't been one).</summary>
    public static List<VerifyResult> LastVerify()
    {
        try { return File.Exists(VerifyFile) ? JsonSerializer.Deserialize<List<VerifyResult>>(File.ReadAllText(VerifyFile)) ?? new() : new(); }
        catch (Exception) { return new(); }
    }
}
