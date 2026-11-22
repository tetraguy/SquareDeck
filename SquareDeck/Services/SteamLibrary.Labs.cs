using System.IO;

namespace SquareDeck.Services;

/// <summary>Steam library details the experimental features need (moving games, cleanup, refund window).</summary>
public static partial class SteamLibrary
{
    /// <summary>Every Steam library folder on this PC (each holds a steamapps folder).</summary>
    public static List<string> LibraryFolders()
    {
        var steam = FindSteamPath();
        return steam == null ? new List<string>() : GetLibraryFolders(steam).Where(Directory.Exists).ToList();
    }

    /// <summary>The appmanifest file of an installed game, or null.</summary>
    public static string? ManifestPath(int appId) => FindManifest(appId);

    /// <summary>The library folder (the one holding steamapps) a game is installed in, or null.</summary>
    public static string? LibraryOf(int appId) =>
        FindManifest(appId) is { } manifest ? Path.GetDirectoryName(Path.GetDirectoryName(manifest)) : null;

    /// <summary>When the game's manifest was made: about when it was installed (or last moved).</summary>
    public static DateTime? InstalledAt(int appId)
    {
        try { return FindManifest(appId) is { } m ? File.GetCreationTime(m) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The folder names under steamapps\common that installed games use, for one library.</summary>
    public static HashSet<string> UsedInstallDirs(string library)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = Path.Combine(library, "steamapps");
        if (!Directory.Exists(apps)) return used;
        foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
        {
            try
            {
                var dir = Vdf.Parse(File.ReadAllText(manifest))["AppState"]?["installdir"]?.Value;
                if (!string.IsNullOrWhiteSpace(dir)) used.Add(dir);
            }
            catch (Exception) { }
        }
        return used;
    }

    /// <summary>Whether the Steam client is running.</summary>
    public static bool ClientRunning() => System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
}
