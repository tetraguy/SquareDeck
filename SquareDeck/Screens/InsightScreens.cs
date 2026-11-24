using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Battery and performance by game: how long a full battery lasts in each, and its power, frame rate and heat.</summary>
public sealed class BatteryInsightScreen : ListScreen
{
    public BatteryInsightScreen() : base("Battery and performance by game") { }

    protected override void Build()
    {
        var all = SessionLog.AllInsights();
        AddNote("While a game runs, SquareDeck notes the battery, the TDP, the temperature and (with the performance overlay's frame counter on) the frame rate. Battery life comes from sessions of ten minutes or more on battery.");
        if (all.Count == 0) { AddRow("No sessions yet", null, "", subtitle: "Play a game for a few minutes and come back"); return; }
        foreach (var i in all.OrderByDescending(x => x.HoursOnBattery.HasValue).ThenByDescending(x => x.Last))
        {
            var insight = i;
            var facts = new List<string> { $"{i.Sessions} {(i.Sessions == 1 ? "session" : "sessions")}", PlaytimeStore.Format(i.Minutes * 60) };
            if (i.Watts is double w) facts.Add($"{w:0} W");
            if (i.Fps is double f) facts.Add($"{f:0} FPS");
            if (i.Temp is double t) facts.Add($"{t:0} °C");
            AddRow(i.Name, SessionLog.BatteryText(insight) is { } b ? b.Replace("about ", "").Replace(" on a full battery", "") : "Plugged in", "",
                click: () => Host.Push(new GameSessionsScreen(insight.Key, insight.Name)), subtitle: string.Join("  ·  ", facts));
        }
        AddRow("Forget all sessions", null, "", click: async () =>
        {
            if (await Host.AskAsync("Forget all sessions?", "The battery and performance notes for every game are removed. Play time is kept.", "Forget", "Cancel") != 0) return;
            SessionLog.Clear();
            Rebuild();
        });
    }
}

/// <summary>One game's sessions, newest first.</summary>
public sealed class GameSessionsScreen : ListScreen
{
    private readonly string _key;

    public GameSessionsScreen(string key, string name) : base("Sessions – " + name) => _key = key;

    protected override void Build()
    {
        if (SessionLog.Insight(_key) is { } i)
            AddNote((SessionLog.BatteryText(i) is { } b ? char.ToUpper(b[0]) + b[1..] + ". " : "") +
                    (i.PercentPerHour is double pph ? $"It uses about {pph:0}% of the battery an hour. " : "") +
                    (i.Fps is double fps ? $"Usually {fps:0} FPS. " : "") + (i.MaxTemp is int max ? $"Hottest: {max} °C." : ""));
        foreach (var s in SessionLog.For(_key))
        {
            var facts = new List<string> { PlaytimeStore.Format(s.Minutes * 60) };
            if (s.Plugged) facts.Add("plugged in");
            else if (s.BatteryStart is int a && s.BatteryEnd is int e) facts.Add($"battery {a}% → {e}%");
            if (s.Watts is double w) facts.Add($"{w:0} W");
            if (s.Fps is double f) facts.Add($"{f:0} FPS" + (s.MinFps is double low ? $" (lows {low:0})" : ""));
            if (s.Temp is double t) facts.Add($"{t:0} °C" + (s.MaxTemp is int m ? $" (peak {m})" : ""));
            if (s.RefreshRate is int hz) facts.Add($"{hz} Hz");
            AddRow(s.Start.ToString("ddd, MMM d  h:mm tt"), null, s.Plugged ? "" : "", subtitle: string.Join("  ·  ", facts));
        }
        AddRow("Forget this game's sessions", null, "", click: () => { SessionLog.Clear(_key); Host.Pop(this); });
    }
}
