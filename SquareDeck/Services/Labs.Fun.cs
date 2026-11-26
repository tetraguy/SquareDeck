using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>The experimental features about stores, friends, looks and play.</summary>
public static partial class Labs
{
    // ================================================================ free games

    public sealed record Giveaway(string Title, string Store, string Worth, string Url, DateTime? Ends, string Image, string Description);

    /// <summary>Current free-game giveaways on Epic, Steam and Prime Gaming (from GamerPower's public list).</summary>
    public static async Task<List<Giveaway>?> FreeGamesAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://www.gamerpower.com/api/filter?platform=epic-games-store.steam.prime-gaming&type=game", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();
            var list = new List<Giveaway>();
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                string S(string name) => g.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (S("status").Length > 0 && !S("status").Equals("Active", StringComparison.OrdinalIgnoreCase)) continue;
                string platforms = S("platforms");
                string store = platforms.Contains("Epic", StringComparison.OrdinalIgnoreCase) ? "Epic Games Store"
                    : platforms.Contains("Prime", StringComparison.OrdinalIgnoreCase) || platforms.Contains("Amazon", StringComparison.OrdinalIgnoreCase) ? "Prime Gaming"
                    : platforms.Contains("Steam", StringComparison.OrdinalIgnoreCase) ? "Steam" : platforms;
                DateTime? ends = DateTime.TryParse(S("end_date"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var e) ? e : null;
                string title = S("title").Replace(" Giveaway", "", StringComparison.OrdinalIgnoreCase).Replace(" (Epic Games)", "").Replace(" (Steam)", "").Trim();
                list.Add(new Giveaway(title, store, S("worth"), S("open_giveaway_url").Length > 0 ? S("open_giveaway_url") : S("gamerpower_url"), ends, S("thumbnail"), S("description")));
            }
            return list.OrderBy(g => g.Store).ThenBy(g => g.Ends ?? DateTime.MaxValue).ToList();
        }
        catch (Exception) { return null; }
    }

    public static string EndsIn(DateTime? ends)
    {
        if (ends == null) return "No end date";
        var left = ends.Value - DateTime.Now;
        if (left <= TimeSpan.Zero) return "Ending now";
        return left.TotalDays >= 2 ? $"Ends in {Math.Floor(left.TotalDays):0} days" : left.TotalHours >= 2 ? $"Ends in {Math.Floor(left.TotalHours):0} hours" : "Ends within 2 hours";
    }

    // ================================================================ friends leaderboard

    public sealed record PlayerWeek(string Name, bool IsYou, double Hours, List<(string Game, double Hours)> Games);

    /// <summary>
    /// Hours played over the last two weeks by you and your Steam friends (Steam reports two weeks, not one),
    /// from Steam's Web API. Friends whose game details are private are left out.
    /// </summary>
    public static async Task<(List<PlayerWeek>? Players, string? Error)> LeaderboardAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, "This needs a Steam Web API key (Settings > Accounts and stores > Steam friends).");
        var (friends, error) = await SteamFriends.GetAsync(apiKey, ct);
        if (friends == null) return (null, error ?? "Your Steam friends couldn't be loaded.");
        var me = SteamLibrary.GetSteamId64();
        var people = friends.Take(40).Select(f => (f.Id, f.Name, false)).ToList();
        if (me != null) people.Insert(0, (me.Value.ToString(), SteamLibrary.GetSteamNames().Persona ?? "You", true));
        var results = new List<PlayerWeek>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(people.Select(async p =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var json = await Http.GetStringAsync($"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={Uri.EscapeDataString(apiKey.Trim())}&steamid={p.Item1}", ct);
                using var doc = JsonDocument.Parse(json);
                var games = new List<(string, double)>();
                if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("games", out var arr))
                    foreach (var g in arr.EnumerateArray())
                    {
                        double h = (g.TryGetProperty("playtime_2weeks", out var t) ? t.GetDouble() : 0) / 60;
                        if (h > 0) games.Add((g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", h));
                    }
                if (games.Count > 0 || p.Item3)
                    lock (results) results.Add(new PlayerWeek(p.Item2, p.Item3, games.Sum(x => x.Item2), games.OrderByDescending(x => x.Item2).ToList()));
            }
            catch (Exception) { }
            finally { gate.Release(); }
        }));
        return (results.OrderByDescending(r => r.Hours).ToList(), null);
    }

    // ================================================================ Wallpaper Engine

    public sealed record EngineWallpaper(string Title, string Video, string? Preview);

    /// <summary>Video wallpapers from Wallpaper Engine's Steam Workshop folder (scene and web wallpapers can't be shown).</summary>
    public static List<EngineWallpaper> WallpaperEngineVideos()
    {
        var list = new List<EngineWallpaper>();
        foreach (var lib in SteamLibrary.LibraryFolders())
        {
            var root = Path.Combine(lib, "steamapps", "workshop", "content", "431960");
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var project = Path.Combine(dir, "project.json");
                    if (!File.Exists(project)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(project));
                    var r = doc.RootElement;
                    string type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    string file = r.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
                    if (!type.Equals("video", StringComparison.OrdinalIgnoreCase) || file.Length == 0) continue;
                    var video = Path.Combine(dir, file);
                    if (!File.Exists(video)) continue;
                    string title = r.TryGetProperty("title", out var n) ? n.GetString() ?? Path.GetFileName(dir) : Path.GetFileName(dir);
                    string? preview = r.TryGetProperty("preview", out var pv) && pv.GetString() is { Length: > 0 } p && File.Exists(Path.Combine(dir, p)) ? Path.Combine(dir, p) : null;
                    list.Add(new EngineWallpaper(title, video, preview));
                }
                catch (Exception) { }
            }
        }
        return list.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ================================================================ style schedule

    /// <summary>"daynight", "weekend" or "random" (empty: none chosen yet).</summary>
    public static string ScheduleMode => Get("schedule.mode", "daynight");

    /// <summary>The style the schedule wants right now, or null when it has nothing to say.</summary>
    public static string? ScheduledStyle(DateTime now)
    {
        switch (ScheduleMode)
        {
            case "daynight":
            {
                int morning = int.TryParse(Get("schedule.morningHour", "7"), out var m) ? m : 7;
                int night = int.TryParse(Get("schedule.nightHour", "19"), out var n) ? n : 19;
                bool day = now.Hour >= morning && now.Hour < night;
                var id = Get(day ? "schedule.day" : "schedule.night");
                return id.Length > 0 ? id : null;
            }
            case "weekend":
            {
                bool weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var id = Get(weekend ? "schedule.weekend" : "schedule.weekday");
                return id.Length > 0 ? id : null;
            }
            case "random":
            {
                var all = UiStyles.Advanced.Where(s => !StyleUnlocks.IsLocked(s)).ToArray();
                if (all.Length == 0) return null;
                int seed = now.Year * 1000 + now.DayOfYear;
                return all[(int)((uint)(seed * 2654435761u) % (uint)all.Length)].Id;
            }
        }
        return null;
    }

    // ================================================================ genre sound

    /// <summary>A bundled track that suits a genre, or null to keep the chosen music.</summary>
    public static string? TrackForGenre(string? genre)
    {
        if (string.IsNullOrEmpty(genre)) return null;
        string g = genre.ToLowerInvariant();
        if (g.Contains("horror")) return "track:Dreamcore";
        if (g.Contains("racing") || g.Contains("driving")) return "track:Fraxsu";
        if (g.Contains("action") || g.Contains("shooter") || g.Contains("fighting")) return "track:Gaming Vibes";
        if (g.Contains("rpg") || g.Contains("role") || g.Contains("adventure")) return "track:Recuerdos";
        if (g.Contains("simulation") || g.Contains("casual") || g.Contains("farming")) return "track:Coffee Shop";
        if (g.Contains("strategy") || g.Contains("puzzle")) return "track:Thinking of You";
        if (g.Contains("sports")) return "track:Sun Rays";
        if (g.Contains("indie") || g.Contains("platform")) return "track:Jumps";
        return null;
    }

    // ================================================================ dusty tiles

    private static readonly Dictionary<int, ImageSource> DustCache = new();

    /// <summary>A dust overlay for a game not played for a while: none under two months, more the longer it waits.</summary>
    public static ImageSource? DustFor(Game game)
    {
        if (!game.IsGame || game.IsRunning) return null;
        var last = game.LastPlayed ?? game.RecentAt;
        if (last <= DateTime.MinValue.AddYears(1)) return null;
        double days = (DateTime.Now - last).TotalDays;
        int level = days < 60 ? 0 : days < 180 ? 1 : days < 365 ? 2 : 3;
        if (level == 0) return null;
        int seed = Math.Abs(game.Key.GetHashCode()) % 7;
        int cacheKey = level * 10 + seed;
        if (DustCache.TryGetValue(cacheKey, out var cached)) return cached;
        var rand = new Random(seed * 97 + level);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        // A grey film, then specks.
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(18 * level), 200, 190, 170)), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        var speck = new SolidColorBrush(Color.FromArgb(150, 225, 218, 200));
        for (int i = 0; i < 40 * level; i++)
        {
            double x = rand.NextDouble() * 100, y = rand.NextDouble() * 100, r = 0.25 + rand.NextDouble() * 0.6;
            group.Children.Add(new GeometryDrawing(speck, null, new EllipseGeometry(new Point(x, y), r, r)));
        }
        if (level == 3)
        {
            // A cobweb in the top corner.
            var web = new Pen(new SolidColorBrush(Color.FromArgb(170, 235, 235, 235)), 0.35);
            var corner = new Point(100, 0);
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 2 + i * Math.PI / 10;
                group.Children.Add(new GeometryDrawing(null, web, new LineGeometry(corner, new Point(100 + Math.Cos(a) * 34, Math.Sin(a) * 34))));
            }
            for (int ring = 1; ring <= 4; ring++)
            {
                var fig = new PathFigure { StartPoint = new Point(100 + Math.Cos(Math.PI / 2) * ring * 8, ring * 8) };
                for (int i = 1; i < 6; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 10;
                    fig.Segments.Add(new QuadraticBezierSegment(new Point(100 + Math.Cos(a - 0.15) * ring * 6.5, Math.Sin(a - 0.15) * ring * 6.5),
                        new Point(100 + Math.Cos(a) * ring * 8, Math.Sin(a) * ring * 8), true));
                }
                group.Children.Add(new GeometryDrawing(null, web, new PathGeometry(new[] { fig })));
            }
        }
        var image = new DrawingImage(group);
        image.Freeze();
        DustCache[cacheKey] = image;
        return image;
    }

    // ================================================================ mood picker

    public static readonly (string Id, string Name, string Glyph, string[] Genres, double MaxHours)[] Moods =
    {
        ("chill", "Chill", "", new[] { "simulation", "casual", "puzzle", "indie", "farming" }, 0),
        ("adrenaline", "Adrenaline", "", new[] { "action", "racing", "shooter", "fighting", "sports" }, 0),
        ("story", "Story time", "", new[] { "rpg", "adventure", "role" }, 0),
        ("laugh", "Quick laugh", "", new[] { "casual", "indie", "party", "platform" }, 8),
        ("brain", "Brain workout", "", new[] { "strategy", "puzzle", "simulation" }, 0),
    };

    /// <summary>Three installed games that fit a mood, by main genre and length, a little random so it changes.</summary>
    public static List<Game> PickForMood(string mood, IEnumerable<Game> games)
    {
        var m = Moods.First(x => x.Id == mood);
        var rand = new Random();
        return games.Where(g => g.IsGame && g.IsInstalled)
            .Select(g =>
            {
                var genre = (GenreCache.Of(g) ?? "").ToLowerInvariant();
                double score = m.Genres.Any(genre.Contains) ? 10 : 0;
                if (m.MaxHours > 0 && HowLongToBeat.Cached(g).Times is { Main: > 0 } t) score += t.Main <= m.MaxHours ? 4 : -4;
                return (g, score: score + rand.NextDouble() * 4);
            })
            .Where(x => x.score >= 4).OrderByDescending(x => x.score).Take(3).Select(x => x.g).ToList();
    }

    // ================================================================ passport

    public sealed record Stamp(string Group, string Name, bool Earned, string Detail, Game? Art = null);

    /// <summary>
    /// Stamps for genres, decades, stores and firsts, each with a game that earned it (for its artwork). Genres and
    /// stores show the ones not yet earned too.
    /// </summary>
    public static List<Stamp> Passport(IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        var games = library.Where(g => g.IsGame).ToList();
        double Hours(Game g) => Math.Max(playtime.TotalSeconds(g.Key) / 3600, (g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : 0) / 60.0);
        bool Played(Game g) => Hours(g) > 1 / 6.0;
        var played = games.Where(Played).OrderByDescending(Hours).ToList();
        var stamps = new List<Stamp>();
        var allGenres = games.Select(GenreCache.Of).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        foreach (var genre in allGenres)
        {
            var these = played.Where(g => GenreCache.Of(g) == genre).ToList();
            stamps.Add(new Stamp("Genres", genre!, these.Count > 0, these.Count > 0 ? $"{these.Count} played, most: {these[0].Name}" : "Not played yet", these.FirstOrDefault()));
        }
        foreach (var decade in played.Where(g => g.ReleaseYear is > 1970).GroupBy(g => g.ReleaseYear!.Value / 10 * 10).OrderBy(x => x.Key))
            stamps.Add(new Stamp("Decades", $"The {decade.Key}s", true, $"{decade.Count()} played", decade.First()));
        foreach (var store in games.Select(g => g.SourceName).Distinct().OrderBy(x => x))
        {
            var these = played.Where(g => g.SourceName == store).ToList();
            stamps.Add(new Stamp("Stores", store, these.Count > 0, these.Count > 0 ? $"{these.Count} played" : "Not played yet", these.FirstOrDefault()));
        }
        var finishedGames = games.Where(g => notes.Find(g.Key)?.Status == "Finished").ToList();
        stamps.Add(new Stamp("Firsts", "First finished game", finishedGames.Count > 0, finishedGames.Count > 0 ? $"{finishedGames.Count} finished" : "Mark a game Finished in its notebook", finishedGames.FirstOrDefault()));
        var hundred = games.FirstOrDefault(g => Hours(g) >= 100);
        stamps.Add(new Stamp("Firsts", "100-hour club", hundred != null, hundred != null ? hundred.Name : "100 hours in one game", hundred));
        stamps.Add(new Stamp("Firsts", "Achievement hunter", achievements.Feed.Count >= 100, $"{achievements.Feed.Count} of 100 achievements"));
        var owl = playtime.Sessions.FirstOrDefault(s => s.Start.Hour is >= 0 and < 4);
        stamps.Add(new Stamp("Firsts", "Night owl", owl != null, owl != null ? owl.Name : "A session after midnight", owl == null ? null : games.FirstOrDefault(g => g.Key == owl.Key)));
        var marathon = playtime.Sessions.Where(s => s.Seconds >= 4 * 3600).OrderByDescending(s => s.Seconds).FirstOrDefault();
        stamps.Add(new Stamp("Firsts", "Marathon", marathon != null, marathon != null ? $"{marathon.Name}, {marathon.Seconds / 3600:0.#} h" : "One session of 4 hours", marathon == null ? null : games.FirstOrDefault(g => g.Key == marathon.Key)));
        return stamps;
    }

    // ================================================================ backlog boss

    /// <summary>One unfinished game in the boss: How Long To Beat's main story time, and how much of it is left.</summary>
    public sealed record Minion(Game Game, double Main, double Played, double Left);

    public sealed record Boss(double HoursLeft, double MaxHours, int Games, List<string> RecentlyFinished, List<Minion> Minions, List<Game> Finished);

    /// <summary>The backlog as a boss: hours left on unfinished games (How Long To Beat's main story time less time played).</summary>
    public static Boss BacklogBoss(IEnumerable<Game> library, PlaytimeStore playtime, LibraryStore notes)
    {
        var minions = new List<Minion>();
        foreach (var g in library.Where(g => g.IsGame && g.IsInstalled))
        {
            var status = notes.Find(g.Key)?.Status;
            if (status is "Finished" or "Dropped") continue;
            if (HowLongToBeat.Cached(g).Times is not { Main: > 0 } t) continue;
            double played = Math.Max(playtime.TotalSeconds(g.Key) / 3600, (g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : 0) / 60.0);
            double rest = Math.Max(0, t.Main - played);
            if (rest <= 0) continue;
            minions.Add(new Minion(g, t.Main, played, rest));
        }
        double left = minions.Sum(m => m.Left);
        double max = double.TryParse(Get("boss.max"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mx) ? mx : 0;
        if (left > max || max <= 0) { max = left; Put("boss.max", max.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        var finished = library.Where(g => g.IsGame && notes.Find(g.Key)?.Status == "Finished").ToList();
        return new Boss(left, max, minions.Count, finished.Select(g => g.Name).Take(5).ToList(), minions.OrderBy(m => m.Left).ToList(), finished);
    }

    // ================================================================ unlockables

    public sealed record Reward(string Id, string Name, string Kind, string Value, string Goal, double Progress);

    /// <summary>Things to earn by playing: highlight colors and companion outfits.</summary>
    public static List<Reward> Rewards(IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        var games = library.Where(g => g.IsGame).ToList();
        int unlocked = achievements.Feed.Count;
        int finished = games.Count(g => notes.Find(g.Key)?.Status == "Finished");
        double hours = playtime.Seconds.Values.Sum(d => d.Values.Sum()) / 3600;
        int streak = playtime.LongestStreak().Days;
        double P(double have, double need) => Math.Clamp(have / need, 0, 1);
        return new()
        {
            new("cap", "Companion cap", "outfit", "cap", "Finish your first game", P(finished, 1)),
            new("scarf", "Companion scarf", "outfit", "scarf", "Unlock 50 achievements", P(unlocked, 50)),
            new("shades", "Companion sunglasses", "outfit", "shades", "Play 100 hours with SquareDeck", P(hours, 100)),
            new("crown", "Companion crown", "outfit", "crown", "Finish 10 games", P(finished, 10)),
            new("headphones", "Companion headphones", "outfit", "headphones", "Play 10 different games with SquareDeck", P(PlayStats.GamesPlayed(playtime), 10)),
            new("wizard", "Companion wizard hat", "outfit", "wizard", "Unlock 250 achievements", P(unlocked, 250)),
            new("halo", "Companion halo", "outfit", "halo", "Play 14 days in a row", P(streak, 14)),
            new("bow", "Companion bow", "outfit", "bow", "Play on 5 different weekends", P(PlayStats.Weekends(playtime), 5)),
            new("tophat", "Companion top hat", "outfit", "tophat", "Finish 5 games", P(finished, 5)),
            new("party", "Companion party hat", "outfit", "party", "Play 100 sessions with SquareDeck", P(playtime.Sessions.Count, 100)),
            new("flower", "Companion flower", "outfit", "flower", "Play before 8 a.m. on 3 different days", P(PlayStats.EarlyMornings(playtime), 3)),
            new("bandana", "Companion bandana", "outfit", "bandana", "Play for 2 hours in one go", P(PlayStats.LongestSessionHours(playtime), 2)),
            new("horns", "Companion little horns", "outfit", "horns", "Play after midnight on 10 different nights", P(PlayStats.LateNights(playtime), 10)),
            new("visor", "Companion space visor", "outfit", "visor", "Play 500 hours with SquareDeck", P(hours, 500)),
            new("mint", "Mint highlight", "color", "#3EE6B0", "Play 5 days in a row", P(streak, 5)),
            new("gold", "Gold highlight", "color", "#F2C14E", "Unlock 100 achievements", P(unlocked, 100)),
            new("sunset", "Sunset highlight", "color", "#FF7A59", "Play 250 hours with SquareDeck", P(hours, 250)),
            new("royal", "Royal violet highlight", "color", "#9B6BFF", "Finish 25 games", P(finished, 25)),
        };
    }

    public static string Outfit => Get("companion.outfit", "");

    // ================================================================ milestones

    public sealed record Milestone(string Key, string Title, string Detail);

    /// <summary>Milestones reached that haven't been celebrated. The first time, everything already reached is noted quietly.</summary>
    public static List<Milestone> NewMilestones(IEnumerable<Game> library, PlaytimeStore playtime, AchievementStore achievements, LibraryStore notes)
    {
        var reached = new List<Milestone>();
        foreach (var g in library.Where(g => g.IsGame))
        {
            double h = playtime.TotalSeconds(g.Key) / 3600;
            if (g.IsSteam) h = Math.Max(h, (SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0) / 60.0);
            foreach (var n in new[] { 10, 50, 100, 250, 500, 1000 })
                if (h >= n) reached.Add(new($"ms:h:{g.Key}:{n}", $"{n} hours in {g.Name}", "That's dedication."));
            if (notes.Find(g.Key)?.Status == "Finished") reached.Add(new($"ms:fin:{g.Key}", $"You finished {g.Name}!", "Another one off the list."));
        }
        foreach (var n in new[] { 50, 100, 250, 500, 1000, 2500 })
            if (achievements.Feed.Count >= n) reached.Add(new($"ms:ach:{n}", $"Achievement number {n}", "Unlocked with SquareDeck."));
        var first = playtime.Sessions.Select(s => s.Start).DefaultIfEmpty(DateTime.Now).Min();
        int years = (int)((DateTime.Now - first).TotalDays / 365.25);
        for (int y = 1; y <= years; y++) reached.Add(new($"ms:year:{y}", y == 1 ? "A year with SquareDeck" : $"{y} years with SquareDeck", "Thanks for playing."));

        bool firstRun = !Has("ms:baseline");
        var fresh = reached.Where(m => !Has(m.Key)).ToList();
        foreach (var m in fresh) Put(m.Key, "1", save: false);
        if (firstRun) Put("ms:baseline", "1", save: false);
        if (fresh.Count > 0 || firstRun) _settings?.Save();
        return firstRun ? new() : fresh;
    }

    // ================================================================ companion

    /// <summary>"sleepy", "cheer", "bored" or "happy", from the time and how you've been playing.</summary>
    public static string CompanionMood(PlaytimeStore playtime, AchievementStore achievements, DateTime? celebratedAt)
    {
        var now = DateTime.Now;
        if (celebratedAt is DateTime c && now - c < TimeSpan.FromMinutes(10)) return "cheer";
        if (achievements.Feed.Count > 0 && now - achievements.Feed.Max(f => f.When) < TimeSpan.FromMinutes(15)) return "cheer";
        if (now.Hour >= 23 || now.Hour < 6) return "sleepy";
        var last = playtime.Sessions.Select(s => s.Start).DefaultIfEmpty(DateTime.MinValue).Max();
        if (last > DateTime.MinValue && now - last > TimeSpan.FromDays(3)) return "bored";
        return "happy";
    }
}
