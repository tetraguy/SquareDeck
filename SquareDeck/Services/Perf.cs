using System.Windows;
using System.Windows.Media.Animation;

namespace SquareDeck.Services;

/// <summary>
/// Performance mode (Settings > Power and performance > Launcher performance): for handhelds, where the battery and the graphics
/// chip are shared with the game. When it's on, the launcher skips its most expensive effects: drop shadows and
/// glows, live blurs (artwork is shown softened a cheaper way instead), the always-pulsing highlight, animated
/// covers, and it draws animations at 30 frames a second instead of 60.
/// </summary>
public static class Perf
{
    public static readonly (string Id, string Name)[] Modes =
    {
        ("off", "Off (full effects)"), ("battery", "On battery only"), ("on", "Always on")
    };

    /// <summary>True while the cheaper drawing is in use.</summary>
    public static bool Active { get; private set; }

    /// <summary>Works out whether it should be on now. True if that changed.</summary>
    public static bool Update(Settings s)
    {
        bool active = s.PerformanceMode switch
        {
            "on" => true,
            "battery" => Battery.GetPercent() != null && !Battery.IsPluggedIn(),
            _ => false
        };
        bool changed = active != Active;
        Active = active;
        return changed;
    }

    public static string Label(string mode) => Modes.FirstOrDefault(m => m.Id == mode).Name ?? Modes[0].Name;

    /// <summary>
    /// Draws animations at 30 frames a second. WPF only allows this before the first animation is created, so it is
    /// decided at start-up (a change in Settings takes effect the next time SquareDeck starts).
    /// </summary>
    public static void CapFrameRate(Settings s)
    {
        if (!(s.PerformanceMode == "on" || s.PerformanceMode == "battery" && Battery.GetPercent() != null && !Battery.IsPluggedIn())) return;
        try
        {
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });
        }
        catch (Exception) { }
    }
}
