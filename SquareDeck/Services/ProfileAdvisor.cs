namespace SquareDeck.Services;

/// <summary>
/// Starting points for a game's profile: Battery saver, Balanced and Performance, sized to this processor's TDP range,
/// with one suggested from how the game actually ran (its sessions: frame rate against the refresh rate). And
/// "best battery": a lower TDP and refresh rate than it has been using.
/// </summary>
public static class ProfileAdvisor
{
    public sealed record Preset(string Id, string Name, int Watts, int RefreshRate, string About);

    /// <summary>The refresh rates the screen offers at its current size, lowest first.</summary>
    public static List<int> Rates()
    {
        try
        {
            var now = DisplaySettings.Current();
            if (now == null) return new();
            return DisplaySettings.All().Where(m => m.Width == now.Width && m.Height == now.Height).Select(m => m.RefreshRate).Distinct().OrderBy(r => r).ToList();
        }
        catch (Exception) { return new(); }
    }

    /// <summary>The lowest rate of 60 Hz or more (60 Hz itself where offered), or 0 when there's no choice.</summary>
    private static int Saver(List<int> rates) => rates.Count < 2 ? 0 : rates.Contains(60) ? 60 : rates.FirstOrDefault(r => r >= 60, rates[0]);

    public static List<Preset> Presets(TdpControl.ChipInfo chip)
    {
        var rates = Rates();
        int W(double share) => (int)Math.Round(chip.Min + (chip.Max - chip.Min) * share);
        return new()
        {
            new("battery", "Battery saver", W(0.15), Saver(rates), "Longest play time; fine for indie, 2D and older games"),
            new("balanced", "Balanced", W(0.45), 0, "Smooth in most games at a sensible battery cost"),
            new("performance", "Performance", W(0.8), rates.Count > 1 ? rates[^1] : 0, "Demanding games; best plugged in"),
        };
    }

    /// <summary>Which preset fits the game, and why, from its sessions (Balanced when there's nothing to go on).</summary>
    public static (string Id, string Why) Suggest(string key)
    {
        var i = SessionLog.Insight(key);
        if (i?.Fps is not double fps)
            return ("balanced", "No frame rate recorded yet. With the performance overlay's frame counter on, a session or two makes this suggestion fit the game.");
        var last = SessionLog.For(key).FirstOrDefault(s => s.RefreshRate != null);
        int refresh = last?.RefreshRate ?? 60;
        if (fps >= Math.Min(refresh, 60) * 0.95)
            return ("battery", $"It has run at about {fps:0} FPS{(i.Watts is double w ? $" at {w:0} W" : "")}: there's room to save battery.");
        if (fps < 40)
            return ("performance", $"It has run at about {fps:0} FPS{(i.Watts is double w2 ? $" at {w2:0} W" : "")}: more power should smooth it out.");
        return ("balanced", $"It has run at about {fps:0} FPS: Balanced keeps that without wasting battery.");
    }

    /// <summary>A quarter less power than the game has been using (not under the processor's minimum) and the lower refresh rate.</summary>
    public static (int Watts, int RefreshRate) BestBattery(string key, TdpControl.ChipInfo chip)
    {
        double used = SessionLog.Insight(key)?.Watts ?? TdpControl.EffectiveWatts;
        int watts = Math.Max(chip.Min, (int)Math.Round(used * 0.75));
        return (watts, Saver(Rates()));
    }
}
