using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>One achievement the launcher noticed being unlocked.</summary>
public sealed class UnlockEntry
{
    public DateTime When { get; set; }
    public int AppId { get; set; }
    public string Game { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
}

/// <summary>
/// Remembers which achievements were already unlocked in each game, so new ones can be spotted
/// after a play session, and keeps a feed of the ones it has seen (achievements.json).
/// </summary>
public sealed class AchievementStore
{
    private const int MaxFeed = 300;

    /// <summary>Steam app id, then the names of achievements known to be unlocked.</summary>
    public Dictionary<int, List<string>> Known { get; set; } = new();

    /// <summary>Newest first.</summary>
    public List<UnlockEntry> Feed { get; set; } = new();

    /// <summary>Steam app id to "unlocked/total", as last seen. Drives the progress bars in the library.</summary>
    public Dictionary<int, string> Progress { get; set; } = new();

    public (int Unlocked, int Total)? ProgressOf(int appId)
    {
        if (!Progress.TryGetValue(appId, out var text)) return null;
        var parts = text.Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out var u) && int.TryParse(parts[1], out var t) && t > 0 ? (u, t) : null;
    }

    /// <summary>Remembers how far along a game is. Returns true if that changed.</summary>
    public bool NoteProgress(int appId, List<Achievement> all)
    {
        var text = all.Count(a => a.Unlocked == true) + "/" + all.Count;
        if (Progress.TryGetValue(appId, out var old) && old == text) return false;
        Progress[appId] = text;
        return true;
    }

    private static string FilePath => Path.Combine(Settings.DataDir, "achievements.json");

    public static AchievementStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonSerializer.Deserialize<AchievementStore>(File.ReadAllText(FilePath)) ?? new AchievementStore();
                store.Known ??= new();
                store.Feed ??= new();
                store.Progress ??= new();
                return store;
            }
        }
        catch
        {
        }
        return new AchievementStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
        }
    }

    /// <summary>Records what is unlocked right now (taken when a game starts) without reporting anything.</summary>
    public void SetBaseline(int appId, IEnumerable<Achievement> all)
    {
        Known[appId] = all.Where(a => a.Unlocked == true).Select(a => a.Name).ToList();
        Save();
    }

    /// <summary>
    /// Compares the current list with what was known and returns the newly unlocked ones, adding them
    /// to the feed. The first time a game is seen there is nothing to compare with, so nothing is new.
    /// </summary>
    public List<Achievement> TakeNew(int appId, string gameName, List<Achievement> all)
    {
        var unlocked = all.Where(a => a.Unlocked == true).ToList();
        if (!Known.TryGetValue(appId, out var known))
        {
            Known[appId] = unlocked.Select(a => a.Name).ToList();
            Save();
            return new List<Achievement>();
        }

        var seen = new HashSet<string>(known);
        var fresh = unlocked.Where(a => !seen.Contains(a.Name)).ToList();
        if (fresh.Count == 0) return fresh;

        foreach (var a in fresh)
        {
            known.Add(a.Name);
            Feed.Insert(0, new UnlockEntry
            {
                When = DateTime.Now, AppId = appId, Game = gameName,
                Name = a.Name, Description = a.Description, Icon = a.Icon
            });
        }
        if (Feed.Count > MaxFeed) Feed.RemoveRange(MaxFeed, Feed.Count - MaxFeed);
        Save();
        return fresh;
    }
}
