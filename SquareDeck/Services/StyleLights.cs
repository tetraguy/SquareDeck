using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>
/// Experimental "Lights match the UI style" (8.5.81): each UI style plays a light effect picked for it, in the style's
/// own colors (its highlight color, and its ring or palette color as the second). Nothing is written to your own light
/// settings, so turning it off puts back exactly what you had. Off while the lights are switched off, and while a game
/// profile plays its own lights.
/// </summary>
public static class StyleLights
{
    public const string Lab = "stylelights";

    /// <summary>Each ready-made style's effect and speed (slow, medium or fast).</summary>
    private static readonly Dictionary<string, (string Effect, string Speed)> Picks = new(StringComparer.Ordinal)
    {
        ["switch"] = ("gradientring", "medium"), ["frosted"] = ("frost", "slow"), ["frosted-dark"] = ("frost", "slow"), ["frosted-light"] = ("breathing", "slow"),
        ["sunset"] = ("sunset", "medium"), ["blueprint"] = ("scanner", "slow"), ["forest"] = ("firefly", "medium"), ["newsprint"] = ("typewriter", "medium"),
        ["ocean"] = ("ocean", "medium"), ["sakura"] = ("sakura", "medium"), ["terminal"] = ("matrix", "medium"), ["cinema"] = ("lighthouse", "slow"),
        ["carbon"] = ("pulsering", "slow"), ["pastel"] = ("carousel", "slow"), ["pop"] = ("confetti", "medium"), ["neon"] = ("neon", "medium"),
        ["minimal"] = ("solid", "medium"), ["oled"] = ("breathing", "slow"), ["pixel-city"] = ("bricks", "medium"), ["retro"] = ("stacker", "medium"),
        ["aero"] = ("wave", "slow"), ["moonlit-river"] = ("tide", "slow"), ["first-light"] = ("sunrise", "slow"), ["rain-circuit"] = ("rain", "medium"),
        ["cloud-nine"] = ("duobreath", "slow"), ["stargazer"] = ("starfield", "medium"), ["spore-grove"] = ("firefly", "slow"), ["meadow"] = ("wave", "slow"),
        ["geyser-steam"] = ("surf", "medium"), ["fountain-plaza"] = ("drip", "medium"), ["silver-blade"] = ("comet", "fast"), ["temple-of-time"] = ("hourglass", "medium"),
        ["plague-doctor"] = ("candle", "medium"), ["pocket-pals"] = ("bounce", "medium"), ["plumbers-holiday"] = ("bricks", "medium"), ["analog-signal"] = ("glitch", "medium"),
        ["gilded"] = ("goldenhour", "medium"), ["quicksilver"] = ("spinner", "fast"), ["velvet-static"] = ("plasma", "slow"), ["soft-focus"] = ("breathing", "slow"),
        ["gamer-den"] = ("rainbowring", "medium"), ["emerald-skies"] = ("aurora", "medium"), ["ember-horizon"] = ("embers", "medium"), ["pixel-woods"] = ("seasons", "medium"),
        ["late-night-study"] = ("focustimer", "medium"), ["midnight-valley"] = ("starfield", "slow"), ["coastline-cruise"] = ("surf", "slow"), ["solitude"] = ("breathing", "slow"),
        ["blue-moon"] = ("ghost", "slow"), ["corner-cafe"] = ("vinyl", "medium"), ["cloud-pool"] = ("ripple", "slow"), ["crimson-hour"] = ("heartbeat", "medium"),
        ["dorado"] = ("goldenhour", "medium"), ["gold-dust"] = ("sparkle", "medium"), ["jewel-box"] = ("kaleidoscope", "medium"), ["lavender-fields"] = ("breathing", "slow"),
        ["vice-beach"] = ("sunset", "medium"), ["me-o-tu"] = ("heartring", "medium"), ["next-door"] = ("duobreath", "medium"), ["nostalgia"] = ("moodring", "medium"),
        ["terra"] = ("lava", "medium"), ["the-view"] = ("sunrise", "medium"), ["reflections"] = ("wave", "medium"), ["viborl"] = ("plasma", "medium"),
        ["start-menu"] = ("rgbquad", "medium"), ["blossom-night"] = ("sakura", "slow"), ["grape-soda"] = ("blobs", "medium"), ["sandstone"] = ("goldenhour", "slow"),
        ["nap-time"] = ("breathing", "slow"), ["seaside-summer"] = ("surf", "medium"), ["pine-lodge"] = ("candles", "medium"), ["dreamcore"] = ("nebula", "slow"),
        ["acid-garden"] = ("toxic", "medium"), ["melt"] = ("drip", "slow"), ["kaleidoscope"] = ("kaleidoscope", "medium"), ["black-bug"] = ("eyes", "medium"),
        ["silver-lining"] = ("frost", "medium"), ["waivy-web"] = ("wave", "medium"), ["arcade-cabinet"] = ("chomper", "medium"), ["lullaby"] = ("starfield", "slow"),
        ["autumn-walk"] = ("seasons", "slow"), ["mission-control"] = ("radar", "medium"), ["glitch-pop"] = ("glitch", "fast"), ["party-time"] = ("disco", "fast"),
        ["scrapbook"] = ("confetti", "slow"), ["treasure-map"] = ("lighthouse", "medium"), ["neon-highway"] = ("scanner", "fast"), ["cozy-cabin"] = ("candle", "slow"),
        ["hologram"] = ("quasar", "medium"), ["starship-bridge"] = ("current", "medium"), ["sparkle-club"] = ("sparkle", "fast"), ["third-eye"] = ("vortex", "slow"),
        ["rewind"] = ("countdown", "medium"), ["ocean-breeze"] = ("ocean", "slow"), ["desert-dusk"] = ("sunset", "slow"), ["starfall"] = ("meteor", "medium"),
        ["rainy-window"] = ("rain", "slow"), ["matcha"] = ("breathing", "slow"), ["bubblegum"] = ("candy", "medium"), ["phosphor"] = ("matrix", "slow"),
        // Advanced styles
        ["adv-meridian"] = ("clockhands", "medium"), ["adv-atelier"] = ("pendulum", "slow"), ["adv-abyss"] = ("biolum", "slow"), ["adv-greenhouse"] = ("firefly", "medium"),
        ["adv-origami"] = ("pinwheel", "slow"), ["adv-sumie"] = ("breathing", "slow"), ["adv-forge"] = ("fire", "medium"), ["adv-glacier"] = ("frost", "slow"),
        ["adv-cartographer"] = ("lighthouse", "slow"), ["adv-axiom"] = ("plasma", "slow"), ["adv-sweetshop"] = ("candy", "medium"), ["adv-gatsby"] = ("sparkle", "medium"),
        ["adv-stainedglass"] = ("kaleidoscope", "slow"), ["adv-koipond"] = ("ripple", "slow"), ["adv-blueprint"] = ("scanner", "slow"), ["adv-circuit"] = ("current", "medium"),
        ["adv-grimoire"] = ("ghost", "medium"), ["adv-bauhaus"] = ("alternator", "medium"), ["adv-mirage"] = ("heat", "slow"), ["adv-riad"] = ("candles", "slow"),
        ["adv-autumn"] = ("seasons", "medium"), ["adv-halftone"] = ("dice", "medium"), ["adv-bronze"] = ("pulsering", "slow"), ["adv-silver"] = ("pulsering", "slow"),
        ["adv-gold"] = ("pulsering", "slow"), ["adv-platinum"] = ("pulsering", "slow"), ["adv-nightowl"] = ("eyes", "slow"), ["adv-ember"] = ("embers", "medium"),
        ["adv-ruby"] = ("heartbeat", "medium"), ["adv-sapphire"] = ("ripple", "medium"), ["adv-emerald"] = ("aurora", "medium"), ["adv-diamond"] = ("sparkle", "medium"),
        ["adv-earlybird"] = ("sunrise", "medium"), ["adv-marathon"] = ("fillup", "medium"), ["adv-globetrotter"] = ("orbit", "medium"), ["adv-carnival"] = ("carousel", "medium"),
        ["adv-phoenix"] = ("fire", "fast"), ["adv-eclipse"] = ("sonar", "slow"), ["adv-neon-grid"] = ("neon", "medium"), ["adv-hologram"] = ("quasar", "slow"),
        ["adv-glow"] = ("breathing", "medium"), ["adv-cozy-pixel"] = ("stacker", "slow"), ["adv-tropical-night"] = ("firefly", "medium"), ["adv-retro-windows"] = ("rgbquad", "slow"),
        ["adv-arcade"] = ("chomper", "medium"), ["adv-softline"] = ("duobreath", "slow"), ["adv-fern-brass"] = ("gears", "slow"), ["adv-nebula"] = ("nebula", "medium"),
        ["adv-luna"] = ("rgbquad", "medium"), ["adv-cozy-place"] = ("candle", "slow"), ["adv-ubuntu"] = ("pulsering", "medium"), ["adv-fatanzia"] = ("magnet", "medium"),
        ["adv-spectrum"] = ("spectrum", "medium"), ["adv-scholar"] = ("focustimer", "medium"), ["adv-indivial"] = ("twin", "medium"),
        // Mythical styles
        ["myth-amberglow"] = ("embers", "slow"), ["myth-coralnight"] = ("jellyfish", "slow"), ["myth-neontide"] = ("neon", "medium"), ["myth-violetpulse"] = ("pulsering", "medium"),
        ["myth-plumember"] = ("embers", "slow"), ["myth-goldenhour"] = ("goldenhour", "medium"), ["myth-clouddoodle"] = ("duobreath", "slow"), ["myth-rivetedsky"] = ("gears", "medium"),
        ["myth-petalglass"] = ("sakura", "slow"), ["myth-dialupdream"] = ("glitch", "medium"), ["myth-goodnotes"] = ("typewriter", "slow"), ["myth-atomic"] = ("ufo", "medium"),
        ["myth-ledger"] = ("clockhands", "medium"), ["myth-indigo"] = ("sonar", "medium"), ["myth-uvslate"] = ("nebula", "medium"), ["myth-sunset"] = ("sunset", "medium"),
        ["myth-tealsketch"] = ("drip", "slow"), ["myth-magentanoir"] = ("neon", "slow"), ["myth-embersnow"] = ("snowfall", "medium"), ["myth-pastelpop"] = ("carousel", "medium"),
        ["myth-skybubble"] = ("bounce", "slow"), ["myth-icecap"] = ("frost", "medium"), ["myth-lilacsketch"] = ("breathing", "slow"), ["myth-sagematerial"] = ("spinner", "slow"),
        ["myth-prismpulse"] = ("prism", "medium"), ["myth-infograph"] = ("fillup", "medium"), ["myth-chromecyan"] = ("scanner", "medium"), ["myth-copythat"] = ("rgbquad", "medium"),
        ["myth-violetconsole"] = ("gradientring", "medium"), ["myth-lavendermist"] = ("breathing", "slow"), ["myth-midnightamber"] = ("candle", "medium"), ["myth-blueprintsun"] = ("goldenhour", "medium"),
        ["myth-cyanvoid"] = ("scanner", "medium"), ["myth-indigohaze"] = ("duobreath", "slow"),
        ["myth-olympus"] = ("tesla", "medium"), ["myth-hoard"] = ("fire", "medium"), ["myth-aurora"] = ("aurorawave", "slow"), ["myth-chronos"] = ("hourglass", "medium"),
        ["myth-kitsune"] = ("candles", "medium"), ["myth-pharaoh"] = ("sunrise", "slow"),
    };

    public static bool On => Labs.On(Lab);

    /// <summary>The effect for a style: yours from the style builder, its hand-picked one, or a gradient ring in its colors.</summary>
    public static (string Effect, string Speed) For(UiStyle style, Settings s)
    {
        var mine = s.CustomStyles.FirstOrDefault(c => c.Id == style.Id);
        if (mine != null && mine.Lights.Length > 0) return (mine.Lights, mine.LightSpeed.Length > 0 ? mine.LightSpeed : "medium");
        if (Picks.TryGetValue(style.Id, out var pick)) return pick;
        // A style of your own (or one added later) without a choice: built from its starting style's pick, else a ring in its colors.
        if (mine != null && Picks.TryGetValue(mine.BasedOn, out var from)) return from;
        return ("gradientring", "medium");
    }

    /// <summary>The style's second color: its highlight ring's other color, else its palette's text color.</summary>
    public static Color SecondColor(UiStyle style, Color accent, bool dark)
    {
        foreach (var hex in (style.RingColors ?? Array.Empty<string>()).Reverse())
            if (TryParse(hex, out var c) && Distance(c, accent) > 60) return c;
        var palette = dark ? style.Dark ?? style.Light : style.Light ?? style.Dark;
        if (palette != null)
            foreach (int i in new[] { 4, 3, 1 })
                if (palette.Length > i && TryParse(palette[i], out var c) && Distance(c, accent) > 60 && c.R + c.G + c.B > 90) return c;
        // Nothing different enough: a lighter shade of the highlight.
        return Color.FromRgb((byte)((accent.R + 255) / 2), (byte)((accent.G + 255) / 2), (byte)((accent.B + 255) / 2));
    }

    /// <summary>The name of the effect a style plays (for Settings).</summary>
    public static string NameFor(UiStyle style, Settings s)
    {
        var (fx, _) = For(style, s);
        return AllyControl.LightModes.FirstOrDefault(m => m.Id == fx).Name ?? fx;
    }

    private static bool TryParse(string? hex, out Color c)
    {
        c = default;
        try { if (string.IsNullOrWhiteSpace(hex)) return false; c = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch (FormatException) { return false; }
    }

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>Every ready-made style has a pick (checked by the self-test in Diagnostics, if run).</summary>
    public static IEnumerable<string> Missing() =>
        UiStyles.All.Concat(UiStyles.Advanced).Select(x => x.Id).Where(id => !Picks.ContainsKey(id)).Distinct();
}
