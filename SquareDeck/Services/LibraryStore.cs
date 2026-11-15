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

    public bool IsEmpty => Width == 0 && RefreshRate == 0 && Hdr == null && PowerPlanId == null && AudioDeviceId == null;
}

/// <summary>A game the user added by hand: any program on the PC.</summary>
public sealed class CustomGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
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
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
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
