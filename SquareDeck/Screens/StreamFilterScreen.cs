using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Experimental "Streaming service filters" (8.5.89): the look, how strong, and the extras.</summary>
public sealed class StreamFilterScreen : ListScreen
{
    public StreamFilterScreen() : base("Streaming service filters") { }

    protected override void Build()
    {
        string look = StreamFilters.Look;
        AddHeader("The look");
        foreach (var (id, name, about) in StreamFilters.Looks)
        {
            var chosen = id;
            AddRow(name, id == look ? "In use" : null, id == look ? "\uE73E" : "\uE7F4", subtitle: about,
                click: () => { Labs.Put("streamfx.look", chosen); UiSound.Select(); StreamFilters.Notify(); Rebuild(); });
        }
        AddHeader("Options");
        AddRow("Strength", StreamFilters.Strengths.FirstOrDefault(x => x.Id == StreamFilters.Strength).Name ?? "Normal", "\uE706", click: () =>
        {
            int at = Array.FindIndex(StreamFilters.Strengths, x => x.Id == StreamFilters.Strength);
            Labs.Put("streamfx.strength", StreamFilters.Strengths[(at + 1) % StreamFilters.Strengths.Length].Id);
            StreamFilters.Notify();
            Rebuild();
        }, subtitle: "From a hint of the old set to the full effect");
        AddRow("Switch on like an old TV", StreamFilters.SwitchOn ? "On" : "Off", "\uE7E8",
            click: () => { Labs.Put("streamfx.poweron", StreamFilters.SwitchOn ? "0" : "1"); StreamFilters.Notify(); Rebuild(); },
            subtitle: "A bright line opens up from the middle of the screen as the service opens");
        if (look == "vhs")
            AddRow("VCR text on screen", StreamFilters.VcrText ? "On" : "Off", "\uE714",
                click: () => { Labs.Put("streamfx.osd", StreamFilters.VcrText ? "0" : "1"); StreamFilters.Notify(); Rebuild(); },
                subtitle: "PLAY ▶ for a few seconds, SP in the corner and the date and time at the bottom");
        AddRow("Music services too", StreamFilters.OnMusic ? "On" : "Off", "\uE8D6",
            click: () => { Labs.Put("streamfx.music", StreamFilters.OnMusic ? "0" : "1"); StreamFilters.Notify(); Rebuild(); },
            subtitle: "YouTube Music and Apple Music (off: video services only)");
        AddNote("A service that's open in the background changes at once. It's drawn inside the page, so it covers the video in full screen too. " +
                "Strong looks blur a little more, which costs a little battery.");
    }
}
