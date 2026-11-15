using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// Finds games installed through other launchers: Epic, GOG, Xbox / Game Pass, Battle.net,
/// and the non-Steam shortcuts added to Steam. Each finder is independent and never throws.
/// </summary>
public static class ExternalStores
{
    /// <summary>Everything found. Slow (Xbox needs PowerShell), so call it off the UI thread.</summary>
    public static async Task<List<Game>> ScanAsync(CancellationToken ct)
    {
        var games = new List<Game>();
        games.AddRange(Safe(ScanEpic));
        games.AddRange(Safe(ScanGog));
        games.AddRange(Safe(ScanBattleNet));
        games.AddRange(Safe(ScanSteamShortcuts));
        try { games.AddRange(await ScanXboxAsync(ct).ConfigureAwait(false)); }
        catch (Exception) { }
        return games;
    }

    private static List<Game> Safe(Func<List<Game>> scan)
    {
        try { return scan(); }
        catch (Exception) { return new List<Game>(); }
    }

    private static DateTime FolderDate(string? dir)
    {
        try { return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? Directory.GetCreationTime(dir) : DateTime.MinValue; }
        catch (Exception) { return DateTime.MinValue; }
    }

    private static Game Make(string source, string id, string name, string launchPath, string? installDir, string? args = null) =>
        new()
        {
            Kind = ItemKind.External,
            Source = source,
            LaunchId = id,
            Name = name,
            OriginalName = name,
            LaunchPath = launchPath,
            LaunchArgs = args,
            InstallDir = installDir,
            RecentAt = FolderDate(installDir)
        };

    // ------------------------------------------------------------ Epic Games

    private static List<Game> ScanEpic()
    {
        var games = new List<Game>();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return games;

        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string Str(string name) =>
                    root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

                var appName = Str("AppName");
                var title = Str("DisplayName");
                var install = Str("InstallLocation");
                if (appName.Length == 0 || title.Length == 0) continue;

                // Add-ons point at their base game; unfinished installs can't be started.
                var main = Str("MainGameAppName");
                if (main.Length > 0 && main != appName) continue;
                if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) &&
                    incomplete.ValueKind == JsonValueKind.True) continue;
                if (install.Length > 0 && !Directory.Exists(install)) continue;

                games.Add(Make("Epic", appName, title,
                    $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true", install));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return games;
    }

    // ------------------------------------------------------------ GOG

    private static List<Game> ScanGog()
    {
        var games = new List<Game>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
            .OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (root == null) return games;

        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            if (key == null) continue;
            var name = key.GetValue("gameName") as string;
            var exe = key.GetValue("exe") as string;
            var path = key.GetValue("path") as string;
            var dependsOn = key.GetValue("dependsOn") as string;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe)) continue;
            if (!string.IsNullOrEmpty(dependsOn)) continue; // DLC
            if (!File.Exists(exe)) continue;
            games.Add(Make("GOG", id, name, exe, path ?? Path.GetDirectoryName(exe)));
        }
        return games;
    }

    // ------------------------------------------------------------ Battle.net

    // What Battle.net calls each game when asked to launch it.
    private static readonly (string NamePart, string Code)[] BattleNetCodes =
    {
        ("World of Warcraft", "WoW"), ("Diablo IV", "Fen"), ("Diablo III", "D3"), ("Diablo II", "OSI"),
        ("Diablo Immortal", "ANBS"), ("Overwatch", "Pro"), ("Hearthstone", "WTCG"), ("Heroes of the Storm", "Hero"),
        ("StarCraft II", "S2"), ("StarCraft", "S1"), ("Warcraft III", "W3"), ("Warcraft Rumble", "GRY")
    };

    private static List<Game> ScanBattleNet()
    {
        var games = new List<Game>();
        string? client = null;
        var found = new List<(string Name, string Dir)>();

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var uninstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;

            foreach (var sub in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(sub);
                if (key?.GetValue("Publisher") is not string publisher ||
                    !publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase)) continue;
                var name = key.GetValue("DisplayName") as string;
                var dir = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

                if (name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase))
                {
                    var exe = Path.Combine(dir, "Battle.net.exe");
                    if (File.Exists(exe)) client = exe;
                }
                else if (found.All(f => f.Name != name)) found.Add((name, dir));
            }
        }

        foreach (var (name, dir) in found)
        {
            var code = BattleNetCodes.FirstOrDefault(c => name.Contains(c.NamePart, StringComparison.OrdinalIgnoreCase)).Code;
            if (client != null && code != null)
            {
                games.Add(Make("Battle.net", name, name, client, dir, $"--exec=\"launch {code}\""));
                continue;
            }
            // Unknown title: every Blizzard game ships a small "... Launcher.exe" that opens it in Battle.net.
            var launcher = Directory.EnumerateFiles(dir, "*Launcher.exe").FirstOrDefault();
            if (launcher != null) games.Add(Make("Battle.net", name, name, launcher, dir));
        }
        return games;
    }

    // ------------------------------------------------------------ Steam's non-Steam shortcuts

    private static List<Game> ScanSteamShortcuts()
    {
        var games = new List<Game>();
        var steam = SteamLibrary.FindSteamPath();
        var account = SteamLibrary.GetAccountId();
        if (steam == null || account == null) return games;

        var file = Path.Combine(steam, "userdata", account.Value.ToString(), "config", "shortcuts.vdf");
        if (!File.Exists(file)) return games;

        var data = File.ReadAllBytes(file);
        int pos = 0;
        var root = ReadBinaryMap(data, ref pos);
        if (!root.TryGetValue("shortcuts", out var list) || list is not Dictionary<string, object> shortcuts) return games;

        foreach (var entry in shortcuts.Values.OfType<Dictionary<string, object>>())
        {
            var name = entry.GetValueOrDefault("appname") as string;
            var exe = (entry.GetValueOrDefault("exe") as string)?.Trim().Trim('"');
            var args = entry.GetValueOrDefault("launchoptions") as string;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) continue;
            // The exact program is matched for "running", so a shared folder can't cause mix-ups.
            games.Add(Make("Steam shortcut", exe.ToLowerInvariant(), name, exe, null, string.IsNullOrWhiteSpace(args) ? null : args));
        }
        return games;
    }

    /// <summary>Reads Valve's binary KeyValues: type byte, name, then a nested map, text or number.</summary>
    private static Dictionary<string, object> ReadBinaryMap(byte[] data, ref int pos)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (pos < data.Length)
        {
            byte type = data[pos++];
            if (type == 0x08) break; // end of this map
            var key = ReadCString(data, ref pos);
            switch (type)
            {
                case 0x00: map[key] = ReadBinaryMap(data, ref pos); break;
                case 0x01: map[key] = ReadCString(data, ref pos); break;
                case 0x02: pos += 4; break;                          // 32-bit number (not needed)
                case 0x03: pos += 4; break;                          // float
                case 0x07: pos += 8; break;                          // 64-bit number
                default: return map;                                 // unknown type: stop rather than misread
            }
        }
        return map;
    }

    private static string ReadCString(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0) pos++;
        var text = Encoding.UTF8.GetString(data, start, pos - start);
        pos++; // terminator
        return text;
    }

    // ------------------------------------------------------------ Xbox / Game Pass

    private static async Task<List<Game>> ScanXboxAsync(CancellationToken ct)
    {
        // Xbox PC games are Store packages that carry a MicrosoftGame.config file.
        const string script =
            "$pk = Get-AppxPackage | Where-Object { $_.InstallLocation -and " +
            "(Test-Path -LiteralPath (Join-Path $_.InstallLocation 'MicrosoftGame.config')) }; " +
            "$apps = Get-StartApps; " +
            "$out = foreach ($p in $pk) { $apps | Where-Object { $_.AppID -like ($p.PackageFamilyName + '!*') } | " +
            "ForEach-Object { [pscustomobject]@{ Name=$_.Name; AppID=$_.AppID; Dir=$p.InstallLocation } } }; " +
            "ConvertTo-Json -Compress -InputObject @($out)";

        var games = new List<Game>();
        var output = await ProcessUtil.RunPowerShellAsync(script, ct).ConfigureAwait(false);
        int start = output.IndexOf('[');
        if (start < 0) return games;

        using var doc = JsonDocument.Parse(output[start..]);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var id = item.TryGetProperty("AppID", out var a) ? a.GetString() : null;
            var dir = item.TryGetProperty("Dir", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
            games.Add(Make("Xbox", id, name, @"shell:AppsFolder\" + id, dir));
        }
        return games;
    }
}
