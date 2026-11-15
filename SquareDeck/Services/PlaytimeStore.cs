using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

public sealed class SessionEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public double Seconds { get; set; }
}

/// <summary>
/// Play time the launcher measures itself: seconds per game per day (playtime.json).
/// Covers every game seen running while the launcher is open, Steam or not.
/// </summary>
public sealed class PlaytimeStore
{
    /// <summary>Game key, then day ("yyyy-MM-dd"), then seconds played that day.</summary>
    public Dictionary<string, Dictionary<string, double>> Seconds { get; set; } = new();

    /// <summary>Last known title for each key, so games that were since removed still have a name.</summary>
    public Dictionary<string, string> Names { get; set; } = new();

    /// <summary>Finished play sessions, newest last.</summary>
    public List<SessionEntry> Sessions { get; set; } = new();

    public void AddSession(string key, string name, DateTime start, double seconds)
    {
        if (seconds < 30) return; // a launch that didn't turn into play
        Sessions.Add(new SessionEntry { Key = key, Name = name, Start = start, Seconds = seconds });
        if (Sessions.Count > 1000) Sessions.RemoveRange(0, Sessions.Count - 1000);
    }

    /// <summary>The longest single session on record.</summary>
    public SessionEntry? LongestSession() => Sessions.OrderByDescending(s => s.Seconds).FirstOrDefault();

    /// <summary>Most days in a row with at least a minute of play, and the day that run ended.</summary>
    public (int Days, DateTime End) LongestStreak()
    {
        var days = Seconds.Values
            .SelectMany(d => d)
            .GroupBy(d => d.Key)
            .Where(g => g.Sum(x => x.Value) >= 60)
            .Select(g => DateTime.TryParse(g.Key, out var day) ? day : DateTime.MinValue)
            .Where(d => d > DateTime.MinValue)
            .OrderBy(d => d)
            .ToList();

        int best = 0, run = 0;
        DateTime bestEnd = DateTime.MinValue, previous = DateTime.MinValue;
        foreach (var day in days)
        {
            run = previous != DateTime.MinValue && (day - previous).Days == 1 ? run + 1 : 1;
            if (run > best) { best = run; bestEnd = day; }
            previous = day;
        }
        return (best, bestEnd);
    }

    /// <summary>The calendar month with the most play time.</summary>
    public (string Month, double Seconds)? BestMonth()
    {
        var months = Seconds.Values
            .SelectMany(d => d)
            .Where(d => d.Key.Length >= 7)
            .GroupBy(d => d.Key[..7])
            .Select(g => (Month: g.Key, Seconds: g.Sum(x => x.Value)))
            .OrderByDescending(m => m.Seconds)
            .ToList();
        if (months.Count == 0 || months[0].Seconds < 60) return null;
        var label = DateTime.TryParse(months[0].Month + "-01", out var first) ? first.ToString("MMMM yyyy") : months[0].Month;
        return (label, months[0].Seconds);
    }

    private static string FilePath => Path.Combine(Settings.DataDir, "playtime.json");

    public static PlaytimeStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonSerializer.Deserialize<PlaytimeStore>(File.ReadAllText(FilePath)) ?? new PlaytimeStore();
                store.Seconds ??= new();
                store.Names ??= new();
                store.Sessions ??= new();
                return store;
            }
        }
        catch
        {
        }
        return new PlaytimeStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
        }
    }

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd");

    public void Add(string key, string name, double seconds)
    {
        if (seconds <= 0) return;
        if (!Seconds.TryGetValue(key, out var days)) Seconds[key] = days = new();
        var today = DayKey(DateTime.Now);
        days[today] = days.GetValueOrDefault(today) + seconds;
        Names[key] = name;
    }

    public double TotalSeconds(string key) =>
        Seconds.TryGetValue(key, out var days) ? days.Values.Sum() : 0;

    public double SecondsOn(string key, DateTime day) =>
        Seconds.TryGetValue(key, out var days) ? days.GetValueOrDefault(DayKey(day)) : 0;

    /// <summary>Seconds played by all games on one day.</summary>
    public double SecondsOn(DateTime day)
    {
        var k = DayKey(day);
        return Seconds.Values.Sum(days => days.GetValueOrDefault(k));
    }

    /// <summary>Monday of the week containing <paramref name="day"/>.</summary>
    public static DateTime WeekStart(DateTime day)
    {
        int back = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return day.Date.AddDays(-back);
    }

    /// <summary>Per-game totals for the seven days starting at <paramref name="weekStart"/>, most played first.</summary>
    public List<(string Key, string Name, double Seconds)> GamesInWeek(DateTime weekStart)
    {
        var dayKeys = Enumerable.Range(0, 7).Select(i => DayKey(weekStart.AddDays(i))).ToList();
        return Seconds
            .Select(g => (g.Key, Name: Names.GetValueOrDefault(g.Key, g.Key),
                Seconds: dayKeys.Sum(d => g.Value.GetValueOrDefault(d))))
            .Where(g => g.Seconds >= 1)
            .OrderByDescending(g => g.Seconds)
            .ToList();
    }

    public List<(string Key, string Name, double Seconds)> AllTime() =>
        Seconds
            .Select(g => (g.Key, Name: Names.GetValueOrDefault(g.Key, g.Key), Seconds: g.Value.Values.Sum()))
            .Where(g => g.Seconds >= 1)
            .OrderByDescending(g => g.Seconds)
            .ToList();

    public static string Format(double seconds)
    {
        int minutes = (int)Math.Round(seconds / 60);
        if (minutes < 1) return seconds >= 1 ? "under 1 min" : "0 min";
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60}h {minutes % 60:00}m";
    }
}
