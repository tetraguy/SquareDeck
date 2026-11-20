using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

// ====================================================================== notifications

public sealed class NotificationEntry
{
    public DateTime When { get; set; }
    public string Kind { get; set; } = "";      // achievement | friend | download | battery | session
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Icon { get; set; } = "";      // image URL, may be empty
    public string GameKey { get; set; } = "";   // the game it is about, if any (for its icon in the panel)
}

/// <summary>Everything that popped up (or would have), kept so it can be read later (notifications.json).</summary>
public sealed class NotificationStore
{
    private const int Max = 200;

    /// <summary>Newest first.</summary>
    public List<NotificationEntry> Items { get; set; } = new();
    public int Unread { get; set; }

    private static string FilePath => Path.Combine(Settings.DataDir, "notifications.json");

    public static NotificationStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<NotificationStore>(File.ReadAllText(FilePath)) ?? new NotificationStore();
                s.Items ??= new();
                return s;
            }
        }
        catch { }
        return new NotificationStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    public void Add(string kind, string title, string text, string icon = "", string gameKey = "")
    {
        Items.Insert(0, new NotificationEntry { When = DateTime.Now, Kind = kind, Title = title, Text = text, Icon = icon, GameKey = gameKey });
        if (Items.Count > Max) Items.RemoveRange(Max, Items.Count - Max);
        Unread++;
        Save();
    }

    public void MarkRead()
    {
        if (Unread == 0) return;
        Unread = 0;
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        Unread = 0;
        Save();
    }
}

// ====================================================================== backup and restore

/// <summary>Packs settings, library, play time, achievements, notifications and your own images into one zip.</summary>
public static class Backup
{
    private static readonly string[] RootPatterns = { "*.json", "avatar_*", "wallpaper_*" };

    /// <summary>Writes a backup into <paramref name="folder"/> and returns its path.</summary>
    public static string Create(string folder, bool dated = true)
    {
        Directory.CreateDirectory(folder);
        var name = dated ? $"SquareDeck-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip" : "SquareDeck-backup-latest.zip";
        var path = Path.Combine(folder, name);
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var pattern in RootPatterns)
                foreach (var file in Directory.EnumerateFiles(Settings.DataDir, pattern))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));

            // Covers and icons the user picked themselves. Downloaded art is left out: it can be fetched again.
            if (Directory.Exists(SteamGridDbClient.CustomDir))
                foreach (var file in Directory.EnumerateFiles(SteamGridDbClient.CustomDir))
                    zip.CreateEntryFromFile(file, "custom/" + Path.GetFileName(file));
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Copies a backup's files over the current ones. The launcher must be restarted afterwards.</summary>
    public static void Restore(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry("settings.json") == null && zip.GetEntry("library.json") == null)
            throw new InvalidDataException("That file is not a SquareDeck backup.");
        Directory.CreateDirectory(Settings.DataDir);
        zip.ExtractToDirectory(Settings.DataDir, overwriteFiles: true);
    }

    /// <summary>
    /// A path saved on another PC (or under another user name) points nowhere here; if a file with the
    /// same name exists in our own data folder, use that instead.
    /// </summary>
    public static string? Relocate(string? savedPath, string localFolder)
    {
        if (string.IsNullOrEmpty(savedPath)) return null;
        if (File.Exists(savedPath)) return savedPath;
        try
        {
            var local = Path.Combine(localFolder, Path.GetFileName(savedPath));
            return File.Exists(local) ? local : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// ====================================================================== download cache

/// <summary>Size limit for downloaded artwork and video. The user's own images are never touched.</summary>
public static class CacheControl
{
    private static IEnumerable<string> Folders => new[]
    {
        SteamGridDbClient.CoverDir, SteamGridDbClient.InfoDir, SteamGridDbClient.HeroDir, SteamGridDbClient.LogoDir,
        SteamGridDbClient.AnimatedDir, SteamGridDbClient.IconDir, Path.Combine(Settings.DataDir, "store")
    };

    private static List<FileInfo> Files()
    {
        var files = new List<FileInfo>();
        foreach (var dir in Folders)
        {
            try
            {
                if (Directory.Exists(dir)) files.AddRange(new DirectoryInfo(dir).EnumerateFiles());
            }
            catch (Exception) { }
        }
        return files;
    }

    public static long SizeBytes() => Files().Sum(f => f.Length);

    /// <summary>Deletes the least recently used files until the cache fits. Returns how many were removed.</summary>
    public static int Trim(long limitBytes)
    {
        if (limitBytes <= 0) return 0;
        var files = Files().OrderBy(f => f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);
        int removed = 0;
        foreach (var f in files)
        {
            if (total <= limitBytes) break;
            try
            {
                total -= f.Length;
                f.Delete();
                removed++;
            }
            catch (Exception) { }
        }
        return removed;
    }
}

// ====================================================================== speed test

public static class SpeedTest
{
    private const string Host = "https://speed.cloudflare.com/";

    /// <summary>Average round trip to a nearby server in milliseconds.</summary>
    public static async Task<double?> PingAsync()
    {
        try
        {
            using var ping = new Ping();
            var times = new List<long>();
            for (int i = 0; i < 4; i++)
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 2000);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            return times.Count == 0 ? null : times.Average();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Tried in order until one answers. Each is a large public test file meant for exactly this.
    private static readonly string[] DownloadSources =
    {
        "https://speed.cloudflare.com/__down?bytes=25000000",
        "https://proof.ovh.net/files/100Mb.dat",
        "https://ash-speed.hetzner.com/100MB.bin",
        "https://speedtest.tele2.net/100MB.zip",
        "http://speedtest.tele2.net/100MB.zip"
    };

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Some test servers turn away requests that don't look like they come from a browser.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SquareDeck/2 SpeedTest");
        http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        http.DefaultRequestHeaders.Referrer = new Uri(Host);
        return http;
    }

    /// <summary>Megabits per second, measured for up to eight seconds.</summary>
    public static async Task<double?> DownloadAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        foreach (var source in DownloadSources)
        {
            ct.ThrowIfCancellationRequested();
            var speed = await MeasureDownloadAsync(http, source, ct).ConfigureAwait(false);
            if (speed != null) return speed;
        }
        return null;
    }

    /// <summary>
    /// Downloads from one address, over and over if the file runs out, until eight seconds have passed.
    /// Null if the server refuses or too little arrived to judge.
    /// </summary>
    private static async Task<double?> MeasureDownloadAsync(HttpClient http, string url, CancellationToken ct)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        var limit = TimeSpan.FromSeconds(8);
        var watch = new Stopwatch();
        try
        {
            for (int round = 0; round < 40 && (round == 0 || watch.Elapsed < limit); round++)
            {
                // A different address each round, so nothing in between answers from a cache.
                string address = url + (url.Contains('?') ? "&" : "?") + "r=" + Guid.NewGuid().ToString("N")[..8];
                using var resp = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) break;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                if (!watch.IsRunning) watch.Start(); // the clock starts when data starts, not while connecting
                while (watch.Elapsed < limit)
                {
                    int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Keep whatever was measured before the connection dropped.
        }
        watch.Stop();
        double seconds = watch.Elapsed.TotalSeconds;
        // At least a quarter of a megabyte, or the number means nothing.
        return total < 250_000 || seconds <= 0.05 ? null : total * 8 / seconds / 1_000_000;
    }

    /// <summary>Megabits per second, from sending blocks of data for up to six seconds.</summary>
    public static async Task<double?> UploadAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        var payload = new byte[2_000_000];
        Random.Shared.NextBytes(payload); // so it can't be compressed on the way
        long sent = 0;
        var watch = Stopwatch.StartNew();
        try
        {
            for (int round = 0; round < 60 && watch.Elapsed < TimeSpan.FromSeconds(6); round++)
            {
                using var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var resp = await http.PostAsync(Host + "__up", content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) break;
                sent += payload.Length;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
        }
        watch.Stop();
        return sent == 0 ? null : sent * 8 / watch.Elapsed.TotalSeconds / 1_000_000;
    }
}

// ====================================================================== HDR

/// <summary>Reads and switches HDR on the main display (Windows' "Use HDR" switch).</summary>
public static class Hdr
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTarget
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLine;
        public int Available;
        public uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }

    // Not read, only sized correctly so Windows can fill the array.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo { }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GetColorInfo { public DeviceHeader Header; public uint Value, Encoding, BitsPerChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SetColorState { public DeviceHeader Header; public uint Enable; }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref GetColorInfo info);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref SetColorState info);

    private const uint ActivePaths = 2, GetAdvancedColor = 9, SetAdvancedColor = 10;

    private static PathTarget? FirstTarget()
    {
        if (GetDisplayConfigBufferSizes(ActivePaths, out var pathCount, out var modeCount) != 0 || pathCount == 0) return null;
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(ActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;
        return paths[0].Target;
    }

    /// <summary>True/false for on/off, or null when the display has no HDR.</summary>
    public static bool? Get()
    {
        try
        {
            if (FirstTarget() is not { } target) return null;
            var info = new GetColorInfo
            {
                Header = new DeviceHeader
                {
                    Type = GetAdvancedColor, Size = (uint)Marshal.SizeOf<GetColorInfo>(), Adapter = target.Adapter, Id = target.Id
                }
            };
            if (DisplayConfigGetDeviceInfo(ref info) != 0) return null;
            bool supported = (info.Value & 0x1) != 0, enabled = (info.Value & 0x2) != 0;
            return supported ? enabled : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool Set(bool on)
    {
        try
        {
            if (FirstTarget() is not { } target) return false;
            var state = new SetColorState
            {
                Header = new DeviceHeader
                {
                    Type = SetAdvancedColor, Size = (uint)Marshal.SizeOf<SetColorState>(), Adapter = target.Adapter, Id = target.Id
                },
                Enable = on ? 1u : 0u
            };
            return DisplayConfigSetDeviceInfo(ref state) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// ====================================================================== per-game profiles

/// <summary>Applies a game's profile and remembers what to put back when the game closes.</summary>
public static class GameProfiles
{
    public sealed class Previous
    {
        public DisplayMode? Mode;
        public bool? Hdr;
        public string? PowerPlanId;
        public string? AudioDeviceId;
    }

    public static async Task<Previous> ApplyAsync(GameProfile profile)
    {
        var before = new Previous();

        if (profile.Width > 0 || profile.RefreshRate > 0)
        {
            var current = DisplaySettings.Current();
            if (current != null)
            {
                var wanted = new DisplayMode(
                    profile.Width > 0 ? profile.Width : current.Width,
                    profile.Height > 0 ? profile.Height : current.Height,
                    profile.RefreshRate > 0 ? profile.RefreshRate : current.RefreshRate);
                // Temporary change only: nothing is saved, so the worst case is fixed by signing out.
                if (wanted != current && DisplaySettings.Apply(wanted, keep: false)) before.Mode = current;
            }
        }

        if (profile.Hdr is bool hdr && Hdr.Get() is bool now && now != hdr && Hdr.Set(hdr)) before.Hdr = now;

        if (profile.PowerPlanId != null)
        {
            var active = (await PowerSettings.GetPlansAsync()).FirstOrDefault(p => p.Active).Id;
            if (active != null && !active.Equals(profile.PowerPlanId, StringComparison.OrdinalIgnoreCase))
            {
                await PowerSettings.SetPlanAsync(profile.PowerPlanId);
                before.PowerPlanId = active;
            }
        }

        if (profile.AudioDeviceId != null)
        {
            var current = Audio.GetOutputs().FirstOrDefault(o => o.IsDefault).Id;
            if (current != null && current != profile.AudioDeviceId && Audio.SetDefaultOutput(profile.AudioDeviceId))
            {
                before.AudioDeviceId = current;
                Audio.ResetMeter();
            }
        }
        return before;
    }

    public static async Task RestoreAsync(Previous before)
    {
        if (before.AudioDeviceId != null) { Audio.SetDefaultOutput(before.AudioDeviceId); Audio.ResetMeter(); }
        if (before.PowerPlanId != null) await PowerSettings.SetPlanAsync(before.PowerPlanId);
        if (before.Hdr is bool hdr) Hdr.Set(hdr);
        if (before.Mode != null) DisplaySettings.Apply(before.Mode, keep: false);
    }
}

// ====================================================================== do not disturb

/// <summary>
/// Hides Windows notification pop-ups. Windows has no public switch for its own "Do not disturb",
/// so this turns off notification banners for the user, which has the same visible effect.
/// </summary>
/// <summary>Turns the screen off (any button, key or touch turns it back on).</summary>
public static class ScreenPower
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static void Off()
    {
        const uint SysCommand = 0x0112;
        const int MonitorPower = 0xF170, PowerOff = 2;
        try { PostMessage(new IntPtr(0xFFFF), SysCommand, new IntPtr(MonitorPower), new IntPtr(PowerOff)); } catch (Exception) { }
    }
}

public static class DoNotDisturb
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    public static bool Set(bool quiet)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("ToastEnabled", quiet ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// ====================================================================== game stores

/// <summary>A game store's desktop app: where to find it, how to open it, and how to get it.</summary>
public sealed record StoreApp(string Name, string? ExePath, string? OpenTarget, string WingetId, string DownloadUrl)
{
    public bool Installed => ExePath != null || OpenTarget != null;
}

public static class Stores
{
    private const string XboxAppId = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    public static List<StoreApp> Detect()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string? steam = SteamLibrary.FindSteamPath() is string sp ? Existing(Path.Combine(sp, "steam.exe")) : null;
        string? epic = Existing(
            Path.Combine(pf86, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
            Path.Combine(pf86, "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe"),
            Path.Combine(pf, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
        string? gog = Existing(
            Reg(@"SOFTWARE\GOG.com\GalaxyClient\paths", "client") is string g ? Path.Combine(g, "GalaxyClient.exe") : null,
            Path.Combine(pf86, "GOG Galaxy", "GalaxyClient.exe"));
        string? battleNet = Existing(Path.Combine(pf86, "Battle.net", "Battle.net Launcher.exe"),
            Path.Combine(pf86, "Battle.net", "Battle.net.exe"));
        string? ea = Existing(
            Reg(@"SOFTWARE\Electronic Arts\EA Desktop", "LauncherAppPath", RegistryView.Registry64),
            Path.Combine(pf, "Electronic Arts", "EA Desktop", "EA Desktop", "EALauncher.exe"));
        string? ubisoft = Existing(
            Reg(@"SOFTWARE\Ubisoft\Launcher", "InstallDir") is string u ? Path.Combine(u, "UbisoftConnect.exe") : null,
            Path.Combine(pf86, "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"));
        string? amazon = Existing(Path.Combine(local, "Amazon Games", "App", "Amazon Games.exe"));
        string? rockstar = Existing(
            Reg(@"SOFTWARE\Rockstar Games\Launcher", "InstallFolder") is string r ? Path.Combine(r, "Launcher.exe") : null,
            Path.Combine(pf, "Rockstar Games", "Launcher", "Launcher.exe"));
        string? itch = Existing(Path.Combine(local, "itch", "itch-setup.exe"));
        bool xbox = ShellThumb.ForApp(XboxAppId, 32) != null;
        string? recomp = RecompLauncher.FindExe();

        return new List<StoreApp>
        {
            new("Steam", steam, steam != null ? "steam://openurl/https://store.steampowered.com/" : null, "Valve.Steam", "https://store.steampowered.com/about/"),
            new("Xbox", null, xbox ? @"shell:AppsFolder\" + XboxAppId : null, "9MV0B5HZVK9Z", "https://www.xbox.com/apps/xbox-app-for-pc"),
            new("Epic Games", epic, epic != null ? "com.epicgames.launcher://store" : null, "EpicGames.EpicGamesLauncher", "https://store.epicgames.com/download"),
            new("GOG Galaxy", gog, null, "GOG.Galaxy", "https://www.gog.com/galaxy"),
            new("Battle.net", battleNet, null, "Blizzard.BattleNet", "https://download.battle.net/"),
            new("EA app", ea, null, "ElectronicArts.EADesktop", "https://www.ea.com/ea-app"),
            new("Ubisoft Connect", ubisoft, null, "Ubisoft.Connect", "https://ubisoftconnect.com/"),
            new("Amazon Games", amazon, null, "Amazon.Games", "https://gaming.amazon.com/amazon-games-app"),
            new("Rockstar Games", rockstar, null, "RockstarGames.Launcher", "https://socialclub.rockstargames.com/rockstar-games-launcher"),
            new("itch.io", itch, null, "ItchIo.Itch", "https://itch.io/app"),
            new(RecompLauncher.Name, recomp, null, "", RecompLauncher.ReleasesPage)
        };
    }

    private static string? Existing(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));

    private static string? Reg(string subKey, string value, RegistryView view = RegistryView.Registry32)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(subKey);
            return key?.GetValue(value) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Opens a store: its own storefront link where it has one, otherwise the app itself.</summary>
    public static bool Open(StoreApp store)
    {
        if (store.OpenTarget != null)
        {
            return store.OpenTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? ProcessUtil.Open("explorer.exe", store.OpenTarget)
                : ProcessUtil.Open(store.OpenTarget);
        }
        return store.ExePath != null && ProcessUtil.Start(store.ExePath);
    }

    /// <summary>Removes a store app with the Windows Package Manager. Its own uninstaller may ask to confirm.</summary>
    public static async Task<bool> UninstallAsync(StoreApp store)
    {
        if (store.Name == RecompLauncher.Name) return await RecompLauncher.UninstallAsync();
        if (store.Name == "Xbox")
        {
            var (code, _) = await ProcessUtil.RunAsync("powershell",
                "-NoProfile -Command \"Get-AppxPackage Microsoft.GamingApp | Remove-AppxPackage\"");
            return code == 0;
        }
        var (exit, _) = await ProcessUtil.RunAsync("winget",
            $"uninstall --id \"{store.WingetId}\" --exact --accept-source-agreements");
        if (exit == 0) return true;
        // Installed some other way than winget expects: let winget match it by name instead.
        var (byName, _) = await ProcessUtil.RunAsync("winget", $"uninstall --name \"{store.Name}\" --accept-source-agreements");
        return byName == 0;
    }

    /// <summary>Installs a store app with the Windows Package Manager. Windows may ask for approval.</summary>
    public static async Task<bool> InstallAsync(StoreApp store, IProgress<double>? progress = null)
    {
        if (store.Name == RecompLauncher.Name) return await RecompLauncher.InstallAsync(progress);
        // The Xbox app comes from the Microsoft Store catalogue; the rest from winget's own.
        string source = store.Name == "Xbox" ? "msstore" : "winget";
        var (exit, _) = await ProcessUtil.RunAsync("winget",
            $"install --id \"{store.WingetId}\" --source {source} --accept-package-agreements --accept-source-agreements --disable-interactivity");
        return exit == 0;
    }
}


/// <summary>
/// RecompLauncher (github.com/elripalda/RecompLauncher): finds, installs, updates and plays PC recomps. It isn't in
/// winget, so SquareDeck downloads the newest x64 setup from its GitHub releases (pre-releases included, as it's in
/// beta) and runs it. Where it ends up is found from Windows' list of installed programs.
/// </summary>
public static class RecompLauncher
{
    public const string Name = "RecompLauncher";
    public const string ReleasesPage = "https://github.com/elripalda/RecompLauncher/releases";
    private const string Api = "https://api.github.com/repos/elripalda/RecompLauncher/releases";

    /// <summary>The installed program, if any: from its uninstall entry, else the usual install folders.</summary>
    public static string? FindExe()
    {
        if (UninstallEntry() is { } entry)
        {
            foreach (var candidate in new[] { entry.Icon, entry.Location is { } l ? Path.Combine(l, Name + ".exe") : null, entry.Location is { } l2 ? Path.Combine(l2, "recomp-launcher.exe") : null })
            {
                var path = candidate?.Split(',')[0].Trim('"');
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
            }
            if (entry.Location is { } folder && Directory.Exists(folder))
                if (Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase)) is { } exe) return exe;
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var dir in new[] { Path.Combine(local, Name), Path.Combine(local, "Programs", Name), Path.Combine(pf, Name) })
        {
            if (!Directory.Exists(dir)) continue;
            var exe = Directory.EnumerateFiles(dir, "*.exe").FirstOrDefault(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
            if (exe != null) return exe;
        }
        return null;
    }

    private static (string? Location, string? Icon, string? Uninstall)? UninstallEntry()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (root == null) continue;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using var key = root.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is not string display || !display.Contains("Recomp", StringComparison.OrdinalIgnoreCase)) continue;
                        return (key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string,
                                key.GetValue("QuietUninstallString") as string ?? key.GetValue("UninstallString") as string);
                    }
                }
                catch (Exception) { }
            }
        return null;
    }

    /// <summary>Downloads the newest x64 setup (reporting 0–1 progress) and runs it, waiting for it to finish.</summary>
    public static async Task<bool> InstallAsync(IProgress<double>? progress)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        string json = await http.GetStringAsync(Api + "?per_page=10");
        string? url = null, file = null;
        using (var doc = System.Text.Json.JsonDocument.Parse(json))
        {
            // Releases come newest first; take the first with an x64 setup program.
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                if (!release.TryGetProperty("assets", out var assets)) continue;
                foreach (var asset in assets.EnumerateArray())
                {
                    string n = asset.GetProperty("name").GetString() ?? "";
                    if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && n.Contains("x64", StringComparison.OrdinalIgnoreCase) && n.Contains("setup", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        file = n;
                        break;
                    }
                }
                if (url != null) break;
            }
        }
        if (url == null || file == null) return false;

        var target = Path.Combine(Path.GetTempPath(), "SquareDeck", file);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using (var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }

        // Its own installer runs as normal (it may ask where to install, or for permission).
        using var setup = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        if (setup == null) return false;
        await setup.WaitForExitAsync();
        try { File.Delete(target); } catch (Exception) { }
        return FindExe() != null;
    }

    public static async Task<bool> UninstallAsync()
    {
        if (UninstallEntry() is not { Uninstall: { Length: > 0 } command }) return false;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + command + "\"") { UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            if (p != null) await p.WaitForExitAsync();
        }
        catch (Exception) { return false; }
        return FindExe() == null;
    }
}
