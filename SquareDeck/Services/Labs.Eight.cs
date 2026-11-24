using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// The work behind the experimental features added in 8.0 that doesn't need the interface: per-game launch options,
/// scripts and frame caps, external display detection, startup apps, driver versions, Windows Update, smart
/// collections and microSD cards. The pages are in Screens/EightScreens.cs, the parts on Home in MainWindow.Eight.cs.
/// </summary>
public static class Eight
{
    // ================================================================ per-game values

    private static string Val(string kind, string key) => Labs.Get(kind + ":" + key);
    private static void Put(string kind, string key, string value)
    {
        if (value.Length == 0) { RemoveValue(kind + ":" + key); return; }
        Labs.Put(kind + ":" + key, value);
    }

    private static void RemoveValue(string full)
    {
        var s = MainWindow.CurrentSettings;
        if (s == null || !s.LabValues.Remove(full)) return;
        s.Save();
    }

    /// <summary>The frame cap for a game (0: none, or the default for all games when it has none of its own).</summary>
    public static int FpsCap(string key)
    {
        string own = Val("fps", key);
        if (own == "off") return 0;
        if (int.TryParse(own, out var fps) && fps > 0) return fps;
        return int.TryParse(Labs.Get("fps.default"), out var all) ? all : 0;
    }

    /// <summary>"" for the default, "off" for no cap, or a number.</summary>
    public static string FpsChoice(string key) => Val("fps", key);
    public static void SetFpsChoice(string key, string value) => Put("fps", key, value);
    public static readonly int[] FpsSteps = { 30, 36, 40, 45, 50, 60, 72, 90, 120 };

    public static string Args(string key) => Val("args", key);
    public static void SetArgs(string key, string value) => Put("args", key, value.Trim());
    public static bool Admin(string key) => Val("admin", key) == "1";
    public static void SetAdmin(string key, bool on) => Put("admin", key, on ? "1" : "");
    /// <summary>Environment variables as NAME=value, one per line in the saved text (separated by ';' when typed).</summary>
    public static string Env(string key) => Val("env", key);
    public static void SetEnv(string key, string value) => Put("env", key, value.Trim());
    public static string PreScript(string key) => Val("pre", key);
    public static void SetPreScript(string key, string value) => Put("pre", key, value.Trim());
    public static string PostScript(string key) => Val("post", key);
    public static void SetPostScript(string key, string value) => Put("post", key, value.Trim());
    public static bool WaitForPre(string key) => Val("prewait", key) != "0";
    public static void SetWaitForPre(string key, bool wait) => Put("prewait", key, wait ? "" : "0");

    public static IEnumerable<(string Name, string Value)> EnvPairs(string key)
    {
        foreach (var part in Env(key).Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            yield return (part[..eq].Trim(), part[(eq + 1)..].Trim());
        }
    }

    /// <summary>True when the game has launch options SquareDeck has to pass itself.</summary>
    public static bool HasLaunchOptions(Game g) =>
        Labs.On("launchopts") && (Args(g.Key).Length > 0 || Admin(g.Key) || Env(g.Key).Length > 0);

    /// <summary>
    /// Starts a program with the game's own launch options: its arguments after the usual ones, its environment
    /// variables (passed on to the program), and as administrator if chosen.
    /// </summary>
    public static bool StartWithOptions(Game g, string path, string? baseArgs)
    {
        string args = string.Join(" ", new[] { baseArgs ?? "", Args(g.Key) }.Where(a => a.Length > 0));
        var saved = new List<(string Name, string? Value)>();
        try
        {
            foreach (var (name, value) in EnvPairs(g.Key))
            {
                saved.Add((name, Environment.GetEnvironmentVariable(name)));
                Environment.SetEnvironmentVariable(name, value);
            }
            var psi = new ProcessStartInfo(path) { UseShellExecute = true, Arguments = args };
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
            if (Admin(g.Key)) psi.Verb = "runas";
            Process.Start(psi);
            return true;
        }
        catch (Exception) { return false; }
        finally { foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value); }
    }

    /// <summary>A Steam game with its own launch options: Steam's run link, which takes them after the app id.</summary>
    public static string SteamRunLink(Game g) => $"steam://run/{g.AppId}//{Uri.EscapeDataString(Args(g.Key))}/";

    /// <summary>
    /// Runs a script line: a program (with its arguments) or a command for the command prompt, hidden. Waits for it
    /// up to <paramref name="waitSeconds"/> (0: doesn't wait). Returns false if it couldn't start.
    /// </summary>
    public static async Task<bool> RunScriptAsync(string line, int waitSeconds)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + line) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p == null) return false;
            if (waitSeconds > 0)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(waitSeconds));
                try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    // ================================================================ external displays (docked / TV mode)

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathArray, ref uint modes, byte[] modeArray, IntPtr topology);

    private const uint OnlyActivePaths = 2;
    private const int PathSize = 72, ModeSize = 64, TechnologyOffset = 36;
    private const uint Internal = 0x80000000, DisplayPortEmbedded = 11, UdiEmbedded = 13, Miracast = 15;

    /// <summary>
    /// True when a display other than the built-in screen is in use (a TV or monitor through a dock, USB-C or HDMI),
    /// from each active display path's connection type.
    /// </summary>
    public static bool ExternalDisplay()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var paths, out var modes) != 0 || paths == 0) return false;
            var pathData = new byte[paths * PathSize];
            var modeData = new byte[modes * ModeSize];
            if (QueryDisplayConfig(OnlyActivePaths, ref paths, pathData, ref modes, modeData, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < paths; i++)
            {
                uint tech = BitConverter.ToUInt32(pathData, i * PathSize + TechnologyOffset);
                if (tech != Internal && tech != DisplayPortEmbedded && tech != UdiEmbedded) return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>A sound output that's probably the TV or monitor (HDMI / DisplayPort audio), if there is one.</summary>
    public static (string Id, string Name)? TvSoundOutput()
    {
        var outputs = Audio.GetOutputs();
        string[] words = { "HDMI", "DisplayPort", "Display Audio", "High Definition Audio Device", "TV", "Monitor", "NVIDIA High Definition", "AMD High Definition" };
        foreach (var (id, name, _) in outputs)
            if (words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)) && !name.Contains("Speakers", StringComparison.OrdinalIgnoreCase))
                return (id, name);
        return null;
    }

    // ================================================================ startup apps

    public sealed record StartupApp(string Name, string Command, string Where, bool Enabled, bool CanChange, RegistryHive Hive, string ApprovedKey, string ValueName);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>
    /// The apps that start with Windows: the Run entries for you and for everyone, and the Startup folders, with
    /// whether each is on (Windows' own "StartupApproved" switches, the ones Task Manager and Settings use).
    /// </summary>
    public static List<StartupApp> StartupApps()
    {
        var list = new List<StartupApp>();
        bool Approved(RegistryKey root, string key, string name)
        {
            try
            {
                using var k = root.OpenSubKey(key);
                // The first byte is even (02, 06) when on, odd (03, 07) when turned off.
                return k?.GetValue(name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;
            }
            catch (Exception) { return true; }
        }
        void Run(RegistryHive hive, string where, bool canChange)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var run = root.OpenSubKey(RunKey);
                if (run == null) return;
                foreach (var name in run.GetValueNames())
                {
                    if (name.Length == 0) continue;
                    string command = run.GetValue(name) as string ?? "";
                    list.Add(new StartupApp(name, command, where, Approved(root, ApprovedRun, name), canChange, hive, ApprovedRun, name));
                }
            }
            catch (Exception) { }
        }
        Run(RegistryHive.CurrentUser, "For you", true);
        Run(RegistryHive.LocalMachine, "For everyone on this PC", false);
        void Folder(Environment.SpecialFolder folder, RegistryHive hive, string where, bool canChange)
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (!Directory.Exists(path)) return;
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                foreach (var f in Directory.EnumerateFiles(path).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    string file = Path.GetFileName(f);
                    list.Add(new StartupApp(Path.GetFileNameWithoutExtension(f), f, where, Approved(root, ApprovedFolder, file), canChange, hive, ApprovedFolder, file));
                }
            }
            catch (Exception) { }
        }
        Folder(Environment.SpecialFolder.Startup, RegistryHive.CurrentUser, "Your Startup folder", true);
        Folder(Environment.SpecialFolder.CommonStartup, RegistryHive.LocalMachine, "Everyone's Startup folder", false);
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Turns a startup app on or off the way Windows does (it isn't removed, so it can be turned back on).</summary>
    public static bool SetStartupApp(StartupApp app, bool on)
    {
        if (!app.CanChange) return false;
        try
        {
            using var root = RegistryKey.OpenBaseKey(app.Hive, RegistryView.Registry64);
            using var key = root.CreateSubKey(app.ApprovedKey);
            var data = new byte[12];
            data[0] = on ? (byte)2 : (byte)3;
            if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            key.SetValue(app.ValueName, data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ================================================================ drivers

    public sealed record DriverInfo(string Name, string Version, DateTime? Date, string Maker, string Kind);

    /// <summary>
    /// The graphics drivers, the BIOS, and ASUS's own drivers, from the registry (the same entries Device Manager
    /// shows). Newest versions can't be looked up automatically: AMD and ASUS don't publish them in a way programs can read.
    /// </summary>
    public static List<DriverInfo> Drivers()
    {
        var list = new List<DriverInfo>();
        static DateTime? Date(object? value) =>
            value is string s && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios?.GetValue("BIOSVersion") is string version)
                list.Add(new DriverInfo((bios.GetValue("SystemProductName") as string ?? "This PC") + " BIOS", version,
                    Date(bios.GetValue("BIOSReleaseDate")), bios.GetValue("SystemManufacturer") as string ?? "", "bios"));
        }
        catch (Exception) { }
        try
        {
            using var classes = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class");
            if (classes != null)
                foreach (var cls in classes.GetSubKeyNames())
                {
                    bool display = cls.Equals("{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        using var c = classes.OpenSubKey(cls);
                        if (c == null) continue;
                        foreach (var name in c.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
                        {
                            using var k = c.OpenSubKey(name);
                            if (k?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                            string maker = k.GetValue("ProviderName") as string ?? "";
                            bool asus = maker.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
                            if (!display && !asus) continue;
                            if (display && (desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                                || desc.Contains("Remote", StringComparison.OrdinalIgnoreCase) || desc.Contains("Parsec", StringComparison.OrdinalIgnoreCase))) continue;
                            if (list.Any(d => d.Name == desc)) continue;
                            list.Add(new DriverInfo(desc, k.GetValue("DriverVersion") as string ?? "", Date(k.GetValue("DriverDate")), maker, display ? "graphics" : "asus"));
                        }
                    }
                    catch (Exception) { }
                }
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>Where each kind of driver comes from officially.</summary>
    public static string DownloadPage(DriverInfo d) => d.Kind switch
    {
        "graphics" when d.Maker.Contains("AMD", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            => "https://www.amd.com/en/support/download/drivers.html",
        "graphics" when d.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) => "https://www.intel.com/content/www/us/en/download-center/home.html",
        "graphics" when d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) => "https://www.nvidia.com/en-us/drivers/",
        _ => "https://rog.asus.com/support/"
    };

    // ================================================================ Windows Update

    private const string WuSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    /// <summary>Windows' active hours (no automatic restarts between them), or null if they can't be read.</summary>
    public static (int Start, int End)? ActiveHours()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(WuSettings);
            if (k?.GetValue("ActiveHoursStart") is int a && k.GetValue("ActiveHoursEnd") is int b) return (a, b);
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Updates paused until, if they are.</summary>
    public static DateTime? PausedUntil()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(WuSettings);
            if (k?.GetValue("PauseUpdatesExpiryTime") is string s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) && d > DateTime.UtcNow)
                return d.ToLocalTime();
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// Changes Windows Update settings that need administrator rights, through one PowerShell command that Windows
    /// asks you to allow. Returns false if it was refused or failed.
    /// </summary>
    private static async Task<bool> ElevatedAsync(string script)
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Sets active hours (Windows allows at most 18 of them).</summary>
    public static Task<bool> SetActiveHoursAsync(int start, int end) => ElevatedAsync(
        $"$k='HKLM:\\{WuSettings}'; New-Item -Path $k -Force | Out-Null; " +
        $"Set-ItemProperty -Path $k -Name ActiveHoursStart -Value {start} -Type DWord; Set-ItemProperty -Path $k -Name ActiveHoursEnd -Value {end} -Type DWord; " +
        "Set-ItemProperty -Path $k -Name SmartActiveHoursState -Value 0 -Type DWord");

    /// <summary>Pauses updates for some days (Windows' own pause, as in Settings > Windows Update).</summary>
    public static Task<bool> PauseUpdatesAsync(int days)
    {
        string start = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), end = DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return ElevatedAsync($"$k='HKLM:\\{WuSettings}'; New-Item -Path $k -Force | Out-Null; " +
            string.Join(" ", new[] { "PauseUpdatesStartTime", "PauseFeatureUpdatesStartTime", "PauseQualityUpdatesStartTime" }.Select(n => $"Set-ItemProperty -Path $k -Name {n} -Value '{start}';")) + " " +
            string.Join(" ", new[] { "PauseUpdatesExpiryTime", "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesEndTime" }.Select(n => $"Set-ItemProperty -Path $k -Name {n} -Value '{end}';")));
    }

    // ================================================================ smart collections

    public sealed record SmartShelf(string Id, string Name, string Glyph, string About);

    public static readonly SmartShelf[] Shelves =
    {
        new("never", "Never played", "", "Installed, but not played even once"),
        new("once", "Played once and left", "", "One short go, then nothing for a month or more"),
        new("short", "Short sessions", "", "Games you usually play for under half an hour: good for a quick break"),
        new("battery", "Best on battery", "", "The longest battery life, from your own sessions"),
        new("forgotten", "Not played in a year", "", "Installed games you haven't touched in twelve months"),
        new("big", "Big and rarely played", "", "Over 20 GB and under 2 hours played: candidates to free up space"),
        new("favorite", "Most played", "", "Your top ten by hours"),
    };

    private static double Hours(Game g, PlaytimeStore playtime) =>
        Math.Max(playtime.TotalSeconds(g.Key) / 3600, (g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : 0) / 60.0);

    private static DateTime? Last(Game g, PlaytimeStore playtime, LibraryStore library)
    {
        DateTime? last = g.LastPlayed is { Year: > 2000 } lp ? lp : null;
        var own = library.Find(g.Key)?.LastPlayed;
        if (own is { Year: > 2000 } o && (last == null || o > last)) last = o;
        var session = playtime.Sessions.Where(s => s.Key == g.Key).Select(s => (DateTime?)s.Start).DefaultIfEmpty(null).Max();
        if (session != null && (last == null || session > last)) last = session;
        return last;
    }

    private static Dictionary<string, HashSet<string>>? _shelfCache;
    private static DateTime _shelfAt;

    /// <summary>Which shelf each installed game is on (worked out at most every 30 seconds).</summary>
    public static Dictionary<string, HashSet<string>> ShelfMembers(IEnumerable<Game> games, PlaytimeStore playtime, LibraryStore library)
    {
        if (_shelfCache != null && DateTime.UtcNow - _shelfAt < TimeSpan.FromSeconds(30)) return _shelfCache;
        var result = Shelves.ToDictionary(s => s.Id, _ => new HashSet<string>());
        var installed = games.Where(g => g.IsGame && g.IsInstalled).ToList();
        var now = DateTime.Now;
        var insights = SessionLog.AllInsights().ToDictionary(i => i.Key);
        foreach (var g in installed)
        {
            double h = Hours(g, playtime);
            var last = Last(g, playtime, library);
            var sessions = playtime.Sessions.Where(s => s.Key == g.Key).ToList();
            if (h < 0.02 && last == null) result["never"].Add(g.Key);
            if (sessions.Count == 1 && sessions[0].Seconds < 3600 && h < 1.5 && last is DateTime l1 && now - l1 > TimeSpan.FromDays(30)) result["once"].Add(g.Key);
            if (sessions.Count >= 3 && sessions.Average(s => s.Seconds) < 30 * 60) result["short"].Add(g.Key);
            if (last is DateTime l2 && now - l2 > TimeSpan.FromDays(365)) result["forgotten"].Add(g.Key);
            if (g.SizeOnDisk > 20L * 1024 * 1024 * 1024 && h < 2) result["big"].Add(g.Key);
        }
        foreach (var g in installed.Where(g => insights.TryGetValue(g.Key, out var i) && i.HoursOnBattery != null)
                     .OrderByDescending(g => insights[g.Key].HoursOnBattery).Take(12))
            result["battery"].Add(g.Key);
        foreach (var g in installed.Where(g => Hours(g, playtime) >= 1).OrderByDescending(g => Hours(g, playtime)).Take(10)) result["favorite"].Add(g.Key);
        _shelfAt = DateTime.UtcNow;
        return _shelfCache = result;
    }

    public static void ForgetShelves() { _shelfCache = null; _knownCards = null; }

    // ================================================================ daily pick

    /// <summary>
    /// Today's pick: an installed game played before but not for at least three weeks, chosen the same way all day,
    /// with a reason. Null when nothing fits.
    /// </summary>
    public static (Game Game, string Why)? DailyPick(IEnumerable<Game> games, PlaytimeStore playtime, LibraryStore library)
    {
        var now = DateTime.Now;
        var candidates = games.Where(g => g.IsGame && g.IsInstalled && !g.IsRunning)
            .Select(g => (Game: g, Hours: Hours(g, playtime), Last: Last(g, playtime, library)))
            .Where(x => x.Hours >= 0.5 && x.Last is DateTime l && now - l > TimeSpan.FromDays(21))
            .OrderBy(x => x.Game.Key, StringComparer.Ordinal).ToList();
        if (candidates.Count == 0)
        {
            // Nothing played and left: an installed game never started instead.
            var fresh = games.Where(g => g.IsGame && g.IsInstalled && !g.IsRunning && Hours(g, playtime) < 0.02).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (fresh.Count == 0) return null;
            var f = fresh[DaySeed() % fresh.Count];
            return (f, "Installed and waiting: you haven't started it yet.");
        }
        var pick = candidates[DaySeed() % candidates.Count];
        string when = Season(pick.Last!.Value);
        string why = $"You put {Labs.Hours(pick.Hours)} into this {when}" + (library.Find(pick.Game.Key)?.Status is { Length: > 0 } st ? $" (marked {st.ToLowerInvariant()})." : ".");
        return (pick.Game, why);
    }

    private static int DaySeed() => (int)((DateTime.Today.Ticks / TimeSpan.TicksPerDay) % 100_000);

    private static string Season(DateTime d)
    {
        var now = DateTime.Now;
        if ((now - d).TotalDays < 60) return "a few weeks ago";
        string season = d.Month switch { 12 or 1 or 2 => "winter", 3 or 4 or 5 => "spring", 6 or 7 or 8 => "summer", _ => "fall" };
        if (d.Year == now.Year || (d.Month == 12 && d.Year == now.Year - 1 && now.Month <= 2)) return "this " + season;
        if (d.Year == now.Year - 1) return "last " + season;
        return "back in " + d.Year;
    }

    // ================================================================ microSD cards

    public sealed record Card(string Id, string Label, string Drive, bool Present, List<string> Games);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fs, int fsSize);

    private static string CardsFile => Path.Combine(Settings.DataDir, "cards.json");
    private static Dictionary<string, (string Label, List<string> Games)>? _cards;

    private static Dictionary<string, (string Label, List<string> Games)> LoadCards()
    {
        if (_cards != null) return _cards;
        _cards = new();
        try
        {
            if (File.Exists(CardsFile))
                foreach (var e in JsonDocument.Parse(File.ReadAllText(CardsFile)).RootElement.EnumerateObject())
                    _cards[e.Name] = (e.Value.GetProperty("label").GetString() ?? "microSD",
                        e.Value.GetProperty("games").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList());
        }
        catch (Exception) { }
        return _cards;
    }

    private static void SaveCards()
    {
        if (_cards == null) return;
        try
        {
            var o = _cards.ToDictionary(c => c.Key, c => new { label = c.Value.Label, games = c.Value.Games });
            File.WriteAllText(CardsFile, JsonSerializer.Serialize(o));
        }
        catch (Exception) { }
    }

    /// <summary>The removable drives in now, by their volume serial number.</summary>
    private static List<(string Id, string Label, string Drive)> CardsIn()
    {
        var list = new List<(string, string, string)>();
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Removable))
            {
                if (!GetVolumeInformation(d.RootDirectory.FullName, null, 0, out var serial, out _, out _, null, 0)) continue;
                string label = d.VolumeLabel.Length > 0 ? d.VolumeLabel : "microSD";
                list.Add((serial.ToString("X8"), label, d.Name.TrimEnd('\\')));
            }
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>
    /// Notes which installed games are on each card that's in, and returns every card known, in or not. Returns the
    /// cards that were just inserted or removed since the last look, too.
    /// </summary>
    public static (List<Card> Cards, List<Card> Arrived, List<Card> Left) UpdateCards(IEnumerable<Game> games, bool track = true)
    {
        var known = LoadCards();
        var inNow = CardsIn();
        bool changed = false;
        foreach (var (id, label, drive) in inNow)
        {
            var on = games.Where(g => g.IsGame && g.IsInstalled && g.InstallDir is { } dir && dir.StartsWith(drive, StringComparison.OrdinalIgnoreCase)).Select(g => g.Key).ToList();
            if (!known.TryGetValue(id, out var before) || before.Label != label || !before.Games.SequenceEqual(on))
            {
                // Keep games seen on the card before, in case Steam hasn't listed them yet.
                var merged = before.Games == null ? on : on.Union(before.Games.Where(k => !games.Any(g => g.Key == k && g.IsInstalled))).ToList();
                known[id] = (label, merged);
                changed = true;
            }
        }
        if (changed) SaveCards();
        var cards = known.Select(k =>
        {
            var present = inNow.FirstOrDefault(c => c.Id == k.Key);
            return new Card(k.Key, k.Value.Label, present.Drive ?? "", present.Id != null, k.Value.Games);
        }).Where(c => c.Games.Count > 0 || c.Present).ToList();
        var arrived = new List<Card>();
        var left = new List<Card>();
        var nowIds = inNow.Select(c => c.Id).ToHashSet();
        if (!track) return (cards, arrived, left);
        if (_lastIn != null)
        {
            arrived.AddRange(cards.Where(c => c.Present && !_lastIn.Contains(c.Id)));
            left.AddRange(cards.Where(c => !c.Present && _lastIn.Contains(c.Id)));
        }
        _lastIn = nowIds;
        return (cards, arrived, left);
    }

    private static HashSet<string>? _lastIn;

    private static List<Card>? _knownCards;
    private static DateTime _knownAt;

    /// <summary>Every card known, in or not (looked up at most every few seconds: the Library asks for each game).</summary>
    public static List<Card> KnownCards(IEnumerable<Game> games)
    {
        if (_knownCards != null && DateTime.UtcNow - _knownAt < TimeSpan.FromSeconds(5)) return _knownCards;
        _knownAt = DateTime.UtcNow;
        return _knownCards = UpdateCards(games, track: false).Cards;
    }
}
