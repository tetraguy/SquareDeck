using System.Diagnostics;
using System.IO;

namespace SquareDeck.Services;

/// <summary>
/// Starts a Steam game from its own program instead of the steam:// link, so Steam's "Launching…" pop-up doesn't
/// come up over SquareDeck's splash screen (Settings > Games and library > "Start Steam games directly").
///
/// The program is, in order: the one chosen for the game in its options, the one SquareDeck saw running the last
/// time the game was played, or a best guess from the game's folder. The game is given Steam's app id the way
/// Steam itself does (the SteamAppId / SteamGameId variables), so Steamworks games don't restart themselves through
/// Steam. Steam has to be running already; otherwise, or when no program can be found, the normal Steam link is used.
/// </summary>
public static class SteamDirect
{
    // Programs in a game's folder that aren't the game.
    private static readonly string[] NotGames =
    {
        "unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dotnet", "crash", "report", "helper",
        "update", "patch", "config", "settings", "benchmark", "diagnos", "server", "editor", "tool", "touchup", "cleanup",
        "easyanticheat", "eac", "battleye", "beservice", "cefprocess", "subprocess", "webhelper", "ue4prereq", "ueprereq",
        "prereq", "physx", "oalinst", "directx", "notification", "overlay", "activation", "register", "support"
    };

    public static bool SteamIsRunning()
    {
        try
        {
            var found = Process.GetProcessesByName("steam");
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The program to start for a Steam game, or null if none can be worked out.</summary>
    public static string? FindExe(Game game, ItemOverride entry)
    {
        if (!string.IsNullOrEmpty(entry.SteamExe) && File.Exists(entry.SteamExe)) return entry.SteamExe;
        if (!string.IsNullOrEmpty(entry.SteamExeSeen) && File.Exists(entry.SteamExeSeen)) return entry.SteamExeSeen;
        return Guess(game);
    }

    /// <summary>Where the program came from, for the game's options.</summary>
    public static string Describe(Game game, ItemOverride entry)
    {
        if (!string.IsNullOrEmpty(entry.SteamExe) && File.Exists(entry.SteamExe)) return Path.GetFileName(entry.SteamExe) + " (chosen)";
        if (!string.IsNullOrEmpty(entry.SteamExeSeen) && File.Exists(entry.SteamExeSeen)) return Path.GetFileName(entry.SteamExeSeen);
        return Guess(game) is string guess ? Path.GetFileName(guess) + " (best guess)" : "Not found: starts through Steam";
    }

    /// <summary>
    /// A best guess: programs up to three folders deep that don't look like installers or helpers, preferring a name
    /// like the game's or its folder's, then the top folder, then the largest file.
    /// </summary>
    public static string? Guess(Game game)
    {
        var dir = game.InstallDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        try
        {
            string Squash(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            string folder = Squash(Path.GetFileName(dir.TrimEnd('\\'))), name = Squash(game.OriginalName.Length > 0 ? game.OriginalName : game.Name);
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
            (string Path, long Score)? best = null;
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe", options).Take(400))
            {
                string stem = Squash(Path.GetFileNameWithoutExtension(file));
                string relative = file[dir.TrimEnd('\\').Length..].ToLowerInvariant();
                if (NotGames.Any(n => stem.Contains(n)) || relative.Contains("\\redist") || relative.Contains("\\_commonredist") ||
                    relative.Contains("\\engine\\extras") || relative.Contains("\\support\\")) continue;
                long size;
                try { size = new FileInfo(file).Length; } catch (Exception) { continue; }
                int depth = relative.Count(c => c == '\\') - 1;
                long score = size / 1024; // KB: bigger programs are more likely the game
                if (stem.Length > 2 && (stem == folder || stem == name)) score += 4_000_000;
                else if (stem.Length > 3 && (folder.Contains(stem) || name.Contains(stem) || stem.Contains(name) && name.Length > 3)) score += 2_000_000;
                if (stem.EndsWith("shipping")) score += 3_000_000;      // Unreal: the real game behind the small starter
                if (stem.Contains("launcher")) score -= 1_000_000;      // a launcher brings its own window up first
                score -= depth * 200_000L;
                if (best == null || score > best.Value.Score) best = (file, score);
            }
            return best?.Path;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Starts the program from its own folder, with Steam's app id set the way Steam sets it.</summary>
    public static bool Start(Game game, string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
            psi.Environment["SteamAppId"] = game.AppId.ToString();
            psi.Environment["SteamGameId"] = game.AppId.ToString();
            psi.Environment["SteamOverlayGameId"] = game.AppId.ToString();
            using var process = Process.Start(psi);
            return process != null;
        }
        catch (Exception) { return false; }
    }
}
