namespace SquareDeck.Services;

/// <summary>
/// Unlockable styles (Settings > Themes > Unlockable): Advanced styles earned by playing. Each has a goal (hours played,
/// achievements, games finished, a play streak, late nights); once reached, it's unlocked for good (Settings.UnlockedStyles)
/// and SquareDeck says so. Locked styles can't be chosen anywhere, including by the style schedule.
/// </summary>
public static class StyleUnlocks
{
    private static Settings? _settings;
    public static void Init(Settings settings) => _settings = settings;

    public static bool IsLocked(UiStyle style) =>
        style.Unlock != null && _settings?.UnlockedStyles.Contains(style.Id) != true;

    public readonly record struct Progress(double Fraction, string Goal, string Have);

    /// <summary>How far along a style's goal is.</summary>
    public static Progress Check(UiStyle style, IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        if (style.Unlock is not string unlock) return new(1, "", "");
        var parts = unlock.Split(':');
        string kind = parts[0];
        double need = parts.Length > 1 && double.TryParse(parts[1], out var n) ? n : 1;
        double have;
        string goal, unit;
        switch (kind)
        {
            case "hours":
                have = playtime.Seconds.Values.Sum(d => d.Values.Sum()) / 3600;
                goal = $"Play {need:0} hours with SquareDeck"; unit = "hours";
                break;
            case "achievements":
                have = achievements.Feed.Count;
                goal = $"Unlock {need:0} achievements"; unit = "achievements";
                break;
            case "finished":
                have = library.Where(g => g.IsGame).Count(g => notes.Find(g.Key)?.Status == "Finished");
                goal = $"Finish {need:0} games (mark them Finished in a game's notebook)"; unit = "finished";
                break;
            case "streak":
                have = playtime.LongestStreak().Days;
                goal = $"Play {need:0} days in a row"; unit = "days";
                break;
            case "nights":
                // Different nights with a session started between midnight and 4 a.m.
                have = playtime.Sessions.Where(s => s.Start.Hour < 4).Select(s => s.Start.Date).Distinct().Count();
                goal = $"Play after midnight on {need:0} different nights"; unit = "nights";
                break;
            case "early":
                have = PlayStats.EarlyMornings(playtime);
                goal = $"Play before 8 a.m. on {need:0} different days"; unit = "mornings";
                break;
            case "games":
                have = PlayStats.GamesPlayed(playtime);
                goal = $"Play {need:0} different games with SquareDeck"; unit = "games";
                break;
            case "weekends":
                have = PlayStats.Weekends(playtime);
                goal = $"Play on {need:0} different weekends"; unit = "weekends";
                break;
            case "marathon":
                have = PlayStats.LongestSessionHours(playtime);
                goal = $"Play for {need:0} hours in one go"; unit = "hours";
                break;
            case "sessions":
                have = playtime.Sessions.Count;
                goal = $"Play {need:0} sessions with SquareDeck"; unit = "sessions";
                break;
            default:
                return new(0, unlock, "");
        }
        double fraction = Math.Clamp(have / Math.Max(1, need), 0, 1);
        return new(fraction, goal, $"{Math.Min(have, need):0} of {need:0} {unit}");
    }

    /// <summary>
    /// Checks every unlockable style; those whose goal is now reached are unlocked and returned (to celebrate).
    /// The first time, styles already earned unlock quietly.
    /// </summary>
    public static List<UiStyle> Update(IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        var fresh = new List<UiStyle>();
        if (_settings == null) return fresh;
        var games = library.ToList();
        foreach (var style in UiStyles.Advanced.Where(s => s.Unlock != null && !_settings.UnlockedStyles.Contains(s.Id)))
        {
            if (Check(style, games, playtime, achievements, notes).Fraction < 1) continue;
            _settings.UnlockedStyles.Add(style.Id);
            fresh.Add(style);
        }
        bool first = !_settings.UnlocksChecked;
        _settings.UnlocksChecked = true;
        if (fresh.Count > 0 || first) _settings.Save();
        return first ? new() : fresh;
    }
}

/// <summary>Counts from your play history, for the unlockable styles and the companion outfits.</summary>
public static class PlayStats
{
    /// <summary>Different days with a session started between 5 and 8 in the morning.</summary>
    public static int EarlyMornings(PlaytimeStore p) => p.Sessions.Where(s => s.Start.Hour is >= 5 and < 8).Select(s => s.Start.Date).Distinct().Count();

    /// <summary>Different games played with SquareDeck (any time at all).</summary>
    public static int GamesPlayed(PlaytimeStore p) => p.Seconds.Count(kv => kv.Value.Values.Sum() > 60);

    /// <summary>Different weekends with play on the Saturday or Sunday.</summary>
    public static int Weekends(PlaytimeStore p) =>
        p.Sessions.Where(s => s.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                  .Select(s => s.Start.Date.AddDays(s.Start.DayOfWeek == DayOfWeek.Sunday ? -1 : 0)).Distinct().Count();

    /// <summary>The longest single session, in hours.</summary>
    public static double LongestSessionHours(PlaytimeStore p) => (p.LongestSession()?.Seconds ?? 0) / 3600;

    /// <summary>Different nights with a session started between midnight and 4 a.m.</summary>
    public static int LateNights(PlaytimeStore p) => p.Sessions.Where(s => s.Start.Hour < 4).Select(s => s.Start.Date).Distinct().Count();
}
