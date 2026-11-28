using System.Threading.Tasks;

namespace SquareDeck.Services;

/// <summary>
/// SquareDeck's own files read side by side at startup (9.0.1). The library, play time and achievements used to be
/// read one after another on the interface's thread as the window was made; they're independent, so they're read on
/// background threads as soon as SquareDeck knows it's the copy that stays, while the settings and the window are
/// set up. The settings, read once for the frame rate before the window existed, aren't read a second time.
/// </summary>
public static class StartupLoads
{
    private static Task<LibraryStore>? _library;
    private static Task<PlaytimeStore>? _playtime;
    private static Task<AchievementStore>? _achievements;
    private static Settings? _settings;

    /// <summary>Starts reading the files (once this copy owns SquareDeck's data).</summary>
    public static void Begin()
    {
        _library ??= Task.Run(LibraryStore.Load);
        _playtime ??= Task.Run(PlaytimeStore.Load);
        _achievements ??= Task.Run(AchievementStore.Load);
    }

    /// <summary>The settings, read now; the window takes this same copy instead of reading the file again.</summary>
    public static Settings ReadSettings() => _settings ??= Settings.Load();

    public static Settings TakeSettings() => Take(ref _settings) ?? Settings.Load();
    public static LibraryStore TakeLibrary() => Take(ref _library, LibraryStore.Load);
    public static PlaytimeStore TakePlaytime() => Take(ref _playtime, PlaytimeStore.Load);
    public static AchievementStore TakeAchievements() => Take(ref _achievements, AchievementStore.Load);

    private static T? Take<T>(ref T? value) where T : class
    {
        var v = value;
        value = null; // taken once: a later Load reads the file as before
        return v;
    }

    private static T Take<T>(ref Task<T>? task, Func<T> load)
    {
        var t = task;
        task = null;
        if (t == null) return load();
        try { return t.GetAwaiter().GetResult(); }
        catch (Exception) { return load(); }
    }
}
