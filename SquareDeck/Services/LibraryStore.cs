using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>What the user changed about one tile.</summary>
public sealed class ItemOverride
{
    public string? Title { get; set; }
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public string? CoverPath { get; set; }

    /// <summary>User-defined group shown in the game library.</summary>
    public string? Category { get; set; }

    /// <summary>Tracked by the launcher for custom games (Steam tracks its own).</summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>Steam games started directly (Services/SteamDirect): the program chosen for it, and the one last seen running.</summary>
    public string? SteamExe { get; set; }
    public string? SteamExeSeen { get; set; }
    /// <summary>This Steam game starts directly from its own program even with "Start Steam games directly" off in Settings.</summary>
    public bool SteamDirect { get; set; }

    /// <summary>The user's own score, 1 to 5 stars. 0 means not rated.</summary>
    public int Rating { get; set; }

    /// <summary>"Playing", "Finished", "Dropped" or "Backlog"; null when not set.</summary>
    public string? Status { get; set; }

    public string? Notes { get; set; }

    /// <summary>PC settings applied while this game runs.</summary>
    public GameProfile? Profile { get; set; }
}

/// <summary>Display, power and sound settings to switch to when a game starts. Unset parts are left alone.</summary>
public sealed class GameProfile
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public bool? Hdr { get; set; }
    public string? PowerPlanId { get; set; }
    public string? PowerPlanName { get; set; }
    public string? AudioDeviceId { get; set; }
    public string? AudioDeviceName { get; set; }

    // TDP control for this game (0 / null = the default from Settings). TdpWatts is used on battery,
    // TdpPluggedWatts while the charger is connected (0 = the same as on battery).
    public int TdpWatts { get; set; }
    public int TdpPluggedWatts { get; set; }
    public bool? CpuBoost { get; set; }
    public int MaxCpu { get; set; }
    /// <summary>The share of the CPU's cores Windows may use while the game runs (25–100%; 0 = the default from Settings).</summary>
    public int CpuCores { get; set; }
    public string? PowerMode { get; set; }

    public bool HasPower => TdpWatts > 0 || TdpPluggedWatts > 0 || CpuBoost != null || MaxCpu > 0 || CpuCores > 0 || PowerMode != null;

    // The ROG Ally's lights while this game runs (with game lights on in Settings > ROG Ally controls).
    // Null = the game's own color (from its cover); "everyday" = the same as outside games; else an effect id
    // from AllyControl.LightModes. The colors are for one color, breathing and a color per stick (null = the
    // colors from Settings).
    public string? Lights { get; set; }
    /// <summary>Experimental "SquareDeck EQ": the preset while this game runs (null = as outside games, "auto" = match the game).</summary>
    public string? Eq { get; set; }
    public string? LightColor { get; set; }
    public string? LightColor2 { get; set; }

    /// <summary>Adrenaline Hook: AMD Software's performance metrics overlay shows while this game runs.</summary>
    public bool AmdMetricsOverlay { get; set; }

    // While this game runs, on the ROG Ally: a fan curve (an id from AllyControl.FanCurves; null = Settings'), rumble
    // strength (null = Settings'), and the Xbox button's light off (null = Settings').
    public string? FanCurve { get; set; }
    public int? Vibration { get; set; }
    public bool? XboxLightOff { get; set; }
    // The ROG Ally's screen while this game runs: an ASUS GameVisual mode (VisualModes.Modes' id; null = your usual
    // one) and its color temperature (null = your usual one). Put back when the game closes.
    public int? VisualMode { get; set; }
    public int? VisualTemperature { get; set; }
    /// <summary>The Game Bar widget's quick tiles while this game runs (null = the usual ones).</summary>
    public List<string>? WidgetQuick { get; set; }

    public bool IsEmpty => Width == 0 && RefreshRate == 0 && Hdr == null && PowerPlanId == null && AudioDeviceId == null && !HasPower
                           && Lights == null && LightColor == null && LightColor2 == null && !AmdMetricsOverlay
                           && FanCurve == null && Vibration == null && XboxLightOff == null && WidgetQuick == null
                           && VisualMode == null && VisualTemperature == null && Eq == null;
}

/// <summary>A game the user added by hand: any program on the PC.</summary>
public sealed class CustomGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Launch options passed to the program (Edit information).</summary>
    public string? Arguments { get; set; }
    public int? ReleaseYear { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>An app the user added to the launcher.</summary>
public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string AppId { get; set; } = "";
}

/// <summary>A named group of games the user put together. A game can be in several.</summary>
public sealed class GameCollection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Segoe MDL2 glyph shown on the tile when there is no picture.</summary>
    public string Icon { get; set; } = "\uE8F1";

    /// <summary>Square picture for the tile; null to show the icon.</summary>
    public string? CoverPath { get; set; }

    /// <summary>Keys of the games in it.</summary>
    public List<string> Games { get; set; } = new();
}

/// <summary>Favorites, titles, covers, categories, collections, hidden games, custom games and added apps (library.json).</summary>
public sealed class LibraryStore
{
    public Dictionary<string, ItemOverride> Items { get; set; } = new();
    public List<AppEntry> Apps { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();
    public List<GameCollection> Collections { get; set; } = new();

    private static string FilePath => Path.Combine(Settings.DataDir, "library.json");

    public static LibraryStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonSerializer.Deserialize<LibraryStore>(File.ReadAllText(FilePath)) ?? new LibraryStore();
                // Lists missing from files written by older versions come back null.
                store.Items ??= new();
                store.Apps ??= new();
                store.CustomGames ??= new();
                store.Collections ??= new();
                foreach (var c in store.Collections) { c.Games ??= new(); c.Icon ??= "\uE8F1"; }
                return store;
            }
        }
        catch
        {
            // Unreadable file: start fresh rather than fail to launch.
        }
        return new LibraryStore();
    }

    public void Save()
    {
        try
        {
            SafeFile.WriteSoon(FilePath, () => JsonSerializer.Serialize(this, SafeFile.Indented)); // 9.0.1
        }
        catch
        {
        }
    }

    public ItemOverride? Find(string key) => Items.TryGetValue(key, out var o) ? o : null;

    public ItemOverride For(string key)
    {
        if (!Items.TryGetValue(key, out var o)) Items[key] = o = new ItemOverride();
        return o;
    }
}
