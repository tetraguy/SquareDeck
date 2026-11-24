using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>One play session: when, how long, the battery at both ends, and what the hardware did on average.</summary>
public sealed class PlaySession
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
    public int? BatteryStart { get; set; }
    public int? BatteryEnd { get; set; }
    /// <summary>True when the charger was connected at any point (the battery numbers then say nothing about drain).</summary>
    public bool Plugged { get; set; }
    public double? Watts { get; set; }
    public double? Fps { get; set; }
    public double? MinFps { get; set; }
    public double? Temp { get; set; }
    public int? MaxTemp { get; set; }
    public int? RefreshRate { get; set; }
}

/// <summary>What a game's sessions say: battery life on its own, typical power, frame rate and heat.</summary>
public sealed record GameInsight(string Key, string Name, int Sessions, double Minutes, double? HoursOnBattery, double? PercentPerHour,
    double? Watts, double? Fps, double? Temp, int? MaxTemp, DateTime Last);

/// <summary>
/// Battery and performance per game (sessions.json): while a game runs, every 15 seconds the battery, the TDP, the frame
/// rate (when the overlay's counter runs), the temperature and the refresh rate are noted; when it closes, the session
/// is kept (the last 400).
/// </summary>
public static class SessionLog
{
    private static string FilePath => Path.Combine(Settings.DataDir, "sessions.json");
    private static List<PlaySession>? _all;
    private static readonly object Gate = new();

    private sealed class Running
    {
        public int? BatteryStart, BatteryNow;
        public bool Plugged;
        public readonly List<double> Watts = new(), Fps = new(), Temps = new();
        public int? MaxTemp, Refresh;
    }

    private static readonly Dictionary<string, Running> Live = new();
    private static DateTime _lastSample = DateTime.MinValue;

    public static List<PlaySession> All
    {
        get
        {
            lock (Gate)
            {
                if (_all != null) return _all;
                try { _all = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<PlaySession>>(File.ReadAllText(FilePath)) ?? new() : new(); }
                catch (Exception) { _all = new(); }
                return _all;
            }
        }
    }

    private static void Save()
    {
        try
        {
            List<PlaySession> copy;
            lock (Gate) copy = All.ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(copy));
        }
        catch (Exception) { }
    }

    /// <summary>Called every few seconds with the games running now; samples every 15 seconds.</summary>
    public static void Sample(IEnumerable<string> runningKeys)
    {
        var keys = runningKeys.ToList();
        if (keys.Count == 0 || DateTime.UtcNow - _lastSample < TimeSpan.FromSeconds(15)) return;
        _lastSample = DateTime.UtcNow;
        int? battery = Battery.GetPercent();
        bool plugged = Battery.IsPluggedIn();
        double? watts = TdpControl.Ready ? TdpControl.EffectiveWatts : null;
        double? fps = Screens.FrameCounter.Fps;
        _ = Task.Run(() =>
        {
            int? temp = null, refresh = null;
            try { temp = DeviceSensors.Read().CpuTemp; } catch (Exception) { }
            try { refresh = DisplaySettings.Current()?.RefreshRate; } catch (Exception) { }
            lock (Gate)
            {
                foreach (var key in keys)
                {
                    if (!Live.TryGetValue(key, out var r)) Live[key] = r = new Running { BatteryStart = battery };
                    r.BatteryNow = battery;
                    r.Plugged |= plugged;
                    if (watts is double w) r.Watts.Add(w);
                    if (fps is double f && f > 0) r.Fps.Add(f);
                    if (temp is int t && t > 0) { r.Temps.Add(t); r.MaxTemp = Math.Max(r.MaxTemp ?? 0, t); }
                    if (refresh is int hz) r.Refresh = hz;
                }
            }
        });
    }

    /// <summary>A game closed: its session is kept (when it ran for at least two minutes).</summary>
    public static void Finish(string key, string name, DateTime started, double seconds)
    {
        Running? r;
        lock (Gate) { Live.Remove(key, out r); }
        if (seconds < 120) return;
        var session = new PlaySession
        {
            Key = key, Name = name, Start = started, Minutes = Math.Round(seconds / 60, 1),
            BatteryStart = r?.BatteryStart, BatteryEnd = r?.BatteryNow ?? Battery.GetPercent(),
            Plugged = r?.Plugged ?? Battery.IsPluggedIn(),
            Watts = r is { Watts.Count: > 0 } ? Math.Round(r.Watts.Average(), 1) : null,
            Fps = r is { Fps.Count: > 0 } ? Math.Round(r.Fps.Average(), 1) : null,
            MinFps = r is { Fps.Count: > 2 } ? Math.Round(r.Fps.OrderBy(x => x).ElementAt(r.Fps.Count / 20), 1) : null,
            Temp = r is { Temps.Count: > 0 } ? Math.Round(r.Temps.Average(), 1) : null,
            MaxTemp = r?.MaxTemp, RefreshRate = r?.Refresh,
        };
        lock (Gate)
        {
            All.Add(session);
            if (All.Count > 400) All.RemoveRange(0, All.Count - 400);
        }
        _ = Task.Run(Save);
    }

    public static List<PlaySession> For(string key)
    {
        lock (Gate) return All.Where(s => s.Key == key).OrderByDescending(s => s.Start).ToList();
    }

    /// <summary>A game's sessions summed up. Battery life comes from sessions on battery of ten minutes or more.</summary>
    public static GameInsight? Insight(string key)
    {
        var list = For(key);
        if (list.Count == 0) return null;
        var unplugged = list.Where(s => !s.Plugged && s.Minutes >= 10 && s.BatteryStart is int a && s.BatteryEnd is int b && a > b).ToList();
        double? perHour = null, hours = null;
        if (unplugged.Count > 0)
        {
            double used = unplugged.Sum(s => s.BatteryStart!.Value - s.BatteryEnd!.Value);
            double h = unplugged.Sum(s => s.Minutes) / 60;
            if (h > 0 && used > 0) { perHour = used / h; hours = 100 / perHour; }
        }
        double? Avg(Func<PlaySession, double?> f) { var v = list.Select(f).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Average() : null; }
        return new GameInsight(key, list[0].Name, list.Count, list.Sum(s => s.Minutes), hours, perHour,
            Avg(s => s.Watts), Avg(s => s.Fps), Avg(s => s.Temp), list.Max(s => s.MaxTemp), list.Max(s => s.Start));
    }

    public static List<GameInsight> AllInsights()
    {
        List<string> keys;
        lock (Gate) keys = All.Select(s => s.Key).Distinct().ToList();
        return keys.Select(Insight).Where(i => i != null).Select(i => i!).OrderByDescending(i => i.Last).ToList();
    }

    /// <summary>"About 1 h 50 m on a full battery", or null when there's no battery session yet.</summary>
    public static string? BatteryText(GameInsight i)
    {
        if (i.HoursOnBattery is not double h) return null;
        h = Math.Min(h, 24);
        int whole = (int)h, minutes = (int)Math.Round((h - whole) * 60 / 5) * 5;
        if (minutes == 60) { whole++; minutes = 0; }
        string time = whole > 0 ? $"{whole} h" + (minutes > 0 ? $" {minutes} m" : "") : $"{minutes} m";
        return $"about {time} on a full battery" + (i.Watts is double w ? $" at {w:0} W" : "");
    }

    public static void Clear(string? key = null)
    {
        lock (Gate)
        {
            if (key == null) All.Clear();
            else All.RemoveAll(s => s.Key == key);
        }
        _ = Task.Run(Save);
    }
}
