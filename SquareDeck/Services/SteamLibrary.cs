using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>Reads Steam's own files and registry keys: installed games, running state, playtime.</summary>
public static partial class SteamLibrary
{
    private const long SteamId64Base = 76561197960265728;

    // Tools and runtimes Steam installs alongside games; not something to launch.
    private static readonly HashSet<int> IgnoredAppIds = new() { 228980 };
    private static readonly string[] IgnoredNameParts =
    {
        "Steamworks Common Redistributables", "Steam Linux Runtime", "Proton "
    };

    private static VdfNode? _localConfig;
    private static DateTime _localConfigStamp;

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    public static string? FindSteamPath()
    {
        string?[] candidates =
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
            @"C:\Program Files (x86)\Steam"
        };
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var path = c.Replace('/', '\\');
            if (Directory.Exists(Path.Combine(path, "steamapps"))) return path;
        }
        return null;
    }

    /// <summary>Installed games, most recently played or installed first.</summary>
    public static List<Game> GetInstalledGames()
    {
        var games = new Dictionary<int, Game>();
        var steam = FindSteamPath();
        if (steam == null) return new List<Game>();

        foreach (var lib in GetLibraryFolders(steam))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var game = ParseManifest(File.ReadAllText(manifest), apps, File.GetCreationTime(manifest));
                    if (game != null) games.TryAdd(game.AppId, game);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return games.Values
            .OrderByDescending(g => g.RecentAt)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetLibraryFolders(string steamPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seen.Add(Path.GetFullPath(steamPath))) yield return steamPath;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string text;
        try { text = File.ReadAllText(vdf); }
        catch (IOException) { yield break; }

        foreach (Match m in LibraryPathRegex().Matches(text))
        {
            var path = m.Groups[1].Value.Replace(@"\\", @"\");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { continue; }
            if (seen.Add(full)) yield return full;
        }
    }

    private static Game? ParseManifest(string text, string steamAppsDir, DateTime installedAt)
    {
        var state = Vdf.Parse(text)["AppState"];
        if (state == null) return null;

        if (!int.TryParse(state["appid"]?.Value, out var appId)) return null;
        var name = state["name"]?.Value;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (IgnoredAppIds.Contains(appId)) return null;
        if (IgnoredNameParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) return null;

        DateTime? lastPlayed = null;
        if (long.TryParse(state["LastPlayed"]?.Value, out var unix) && unix > 0)
            lastPlayed = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

        long.TryParse(state["SizeOnDisk"]?.Value, out var size);

        string? installDir = null;
        var dirName = state["installdir"]?.Value;
        if (!string.IsNullOrWhiteSpace(dirName))
            installDir = Path.Combine(steamAppsDir, "common", dirName);

        return new Game
        {
            Kind = ItemKind.SteamGame,
            AppId = appId,
            Name = name,
            OriginalName = name,
            InstallDir = installDir,
            LastPlayed = lastPlayed,
            // A freshly installed game counts as recent even before its first launch.
            RecentAt = lastPlayed is DateTime played && played > installedAt ? played : installedAt,
            UpdateText = DescribeUpdate(state),
            SizeOnDisk = size
        };
    }

    private static string? FindManifest(int appId)
    {
        var steam = FindSteamPath();
        if (steam == null) return null;
        foreach (var lib in GetLibraryFolders(steam))
        {
            var path = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>App ids of the DLC that Steam has actually downloaded for a game.</summary>
    public static HashSet<int> GetInstalledDlcIds(int appId)
    {
        var ids = new HashSet<int>();
        try
        {
            var manifest = FindManifest(appId);
            if (manifest == null) return ids;
            var depots = Vdf.Parse(File.ReadAllText(manifest)).Path("AppState", "InstalledDepots");
            if (depots == null) return ids;
            foreach (var depot in depots.Children.Values)
                if (int.TryParse(depot["dlcappid"]?.Value, out var dlc)) ids.Add(dlc);
        }
        catch (Exception) { }
        return ids;
    }

    /// <summary>Steam Workshop items (mods) installed for a game.</summary>
    public static List<string> GetWorkshopItemIds(int appId)
    {
        try
        {
            var manifest = FindManifest(appId);
            if (manifest == null) return new();
            var file = Path.Combine(Path.GetDirectoryName(manifest)!, "workshop", $"appworkshop_{appId}.acf");
            if (!File.Exists(file)) return new();
            var items = Vdf.Parse(File.ReadAllText(file)).Path("AppWorkshop", "WorkshopItemsInstalled");
            return items?.Children.Keys.ToList() ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>True once a game is fully installed (used by the install queue).</summary>
    public static bool IsFullyInstalled(int appId)
    {
        try
        {
            var manifest = FindManifest(appId);
            if (manifest == null) return false;
            var state = Vdf.Parse(File.ReadAllText(manifest))["AppState"];
            return state?["StateFlags"]?.Value == "4";
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Turns a manifest's state into "Update queued" / "Updating 40%" (empty when up to date).</summary>
    private static string DescribeUpdate(VdfNode state)
    {
        int.TryParse(state["StateFlags"]?.Value, out var flags);
        long.TryParse(state["BytesToDownload"]?.Value, out var total);
        long.TryParse(state["BytesDownloaded"]?.Value, out var done);

        const int FullyInstalled = 4, UpdateRequired = 2;
        if (flags == FullyInstalled) return "";
        if (total > 0 && done < total)
            return done > 0 ? $"Updating {done * 100 / total}%" : "Update queued";
        return (flags & UpdateRequired) != 0 ? "Update queued" : "";
    }

    /// <summary>Current update state of every installed game, by app id. Cheap enough to poll.</summary>
    public static Dictionary<int, string> GetUpdateStates()
    {
        var result = new Dictionary<int, string>();
        var steam = FindSteamPath();
        if (steam == null) return result;

        foreach (var lib in GetLibraryFolders(steam))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var state = Vdf.Parse(File.ReadAllText(manifest))["AppState"];
                    if (state != null && int.TryParse(state["appid"]?.Value, out var id))
                        result[id] = DescribeUpdate(state);
                }
                catch (IOException) { }               // Steam is writing the file right now
                catch (UnauthorizedAccessException) { }
            }
        }
        return result;
    }

    /// <summary>Which of the given games Steam currently reports as running.</summary>
    public static HashSet<int> GetRunningAppIds(IEnumerable<int> appIds)
    {
        var running = new HashSet<int>();
        try
        {
            using var apps = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps");
            if (apps == null) return running;
            foreach (var id in appIds)
            {
                using var key = apps.OpenSubKey(id.ToString());
                if (key?.GetValue("Running") is int flag && flag != 0) running.Add(id);
            }
        }
        catch (Exception)
        {
            // Registry unavailable: report nothing running.
        }
        return running;
    }

    /// <summary>Account id (the userdata folder name) of the signed-in Steam user.</summary>
    internal static long? GetAccountId()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null) is int active
                && active != 0)
                return (uint)active;

            var steam = FindSteamPath();
            if (steam == null) return null;
            var loginUsers = Path.Combine(steam, "config", "loginusers.vdf");
            if (!File.Exists(loginUsers)) return null;

            var users = Vdf.Parse(File.ReadAllText(loginUsers))["users"];
            if (users == null) return null;
            var recent = users.Children.FirstOrDefault(u => u.Value["MostRecent"]?.Value == "1");
            var idText = recent.Key ?? users.Children.Keys.FirstOrDefault();
            return long.TryParse(idText, out var id64) ? id64 - SteamId64Base : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static long? GetSteamId64() => GetAccountId() is long a ? a + SteamId64Base : null;

    /// <summary>The signed-in Steam user's profile name (and account name), from Steam's login list.</summary>
    public static (string? Persona, string? Account) GetSteamNames()
    {
        try
        {
            var steam = FindSteamPath();
            var id = GetSteamId64();
            if (steam == null || id == null) return (null, null);
            var loginUsers = Path.Combine(steam, "config", "loginusers.vdf");
            if (!File.Exists(loginUsers)) return (null, null);
            var user = Vdf.Parse(File.ReadAllText(loginUsers))["users"]?[id.Value.ToString()];
            return (user?["PersonaName"]?.Value, user?["AccountName"]?.Value);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>Total minutes played, from Steam's local config. Null when unknown.</summary>
    public static int? GetPlaytimeMinutes(int appId)
    {
        try
        {
            var steam = FindSteamPath();
            var account = GetAccountId();
            if (steam == null || account == null) return null;

            var file = Path.Combine(steam, "userdata", account.Value.ToString(), "config", "localconfig.vdf");
            if (!File.Exists(file)) return null;

            var stamp = File.GetLastWriteTimeUtc(file);
            if (_localConfig == null || stamp != _localConfigStamp)
            {
                _localConfig = Vdf.Parse(File.ReadAllText(file));
                _localConfigStamp = stamp;
            }

            var value = _localConfig
                .Path("UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId.ToString(), "Playtime")?.Value;
            return int.TryParse(value, out var minutes) ? minutes : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
