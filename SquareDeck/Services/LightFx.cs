using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>
/// The ROG Ally's light effects, frame by frame: what each of the four zones (the outer and inner half of each stick's
/// ring: left outer, left inner, right inner, right outer) shows at a moment. The effect picker draws its preview
/// from this, and the effects the Ally's firmware doesn't have itself (strobe, heartbeat, candle and the rest) are
/// played on the real lights from it too, about ten times a second, without saving anything to the controller.
/// </summary>
public static class LightFx
{
    /// <summary>The effects SquareDeck plays itself (the rest are the Ally's own, or a single color it sets).</summary>
    public static readonly HashSet<string> Software = new(StringComparer.Ordinal)
    {
        "strobe", "heartbeat", "candle", "police", "ocean", "aurora", "sunset", "comet", "swap", "temperature",
        // 8.5.58: ideas from the Colores plugin (written for SquareDeck).
        "gradient", "wave", "cycle", "spiral", "lava", "galaxy", "sparkle", "ripple", "cpu", "clock", "ambilight",
        // 8.5.61: drawn for eight zones (the ROG Xbox Ally X20's four LEDs per stick); merged to four on the other Allys.
        "orbit", "twin", "rainbowring", "radar", "pinwheel", "fillup", "batteryring", "cpuring", "snake", "fireworks",
        "rain", "fire", "matrix", "candy", "lightbar", "vortex", "gradientring", "confetti", "pulsering", "yinyang",
        "eyes", "metronome", "dualspin", "figure8",
        // 8.5.62: 25 more for the eight zones.
        "lighthouse", "bounce", "pingpong", "heartring", "sonar", "hourglass", "traffic", "thunderstorm", "embers", "tide",
        "aurorawave", "disco", "alternator", "spectrum", "dna", "sunrise", "neon", "rgbquad", "countdown", "wormholes",
        "jellyfish", "plasma", "sos", "carousel", "kaleidoscope",
        // 8.5.63: 25 more.
        "pendulum", "glitch", "cometrain", "firefly", "blobs", "duobreath", "starfield", "seasons", "candles", "amberbeacon",
        "toxic", "frost", "sakura", "surf", "heat", "dice", "spinner", "bricks", "stacker", "stereo",
        "chameleon", "constellation", "meteor", "drip", "nebula",
        // 8.5.81: 25 more.
        "tesla", "chomper", "prism", "clockhands", "biolum", "signals", "typewriter", "supernova", "goldenhour", "volcano",
        "scanner", "ghost", "snowfall", "tornado", "gears", "rainbowbreath", "peacock", "moodring", "focustimer", "holiday",
        "current", "quasar", "magnet", "vinyl", "ufo",
        // 9.0.3
        "interactive",
    };

    /// <summary>The effects drawn LED by LED for eight zones (Frame8); on four-zone Allys they're merged in pairs.</summary>
    public static readonly HashSet<string> Native8 = new(StringComparer.Ordinal)
    {
        "orbit", "twin", "rainbowring", "radar", "pinwheel", "fillup", "batteryring", "cpuring", "snake", "fireworks",
        "rain", "fire", "matrix", "candy", "lightbar", "vortex", "gradientring", "confetti", "pulsering", "yinyang",
        "eyes", "metronome", "dualspin", "figure8",
        "lighthouse", "bounce", "pingpong", "heartring", "sonar", "hourglass", "traffic", "thunderstorm", "embers", "tide",
        "aurorawave", "disco", "alternator", "spectrum", "dna", "sunrise", "neon", "rgbquad", "countdown", "wormholes",
        "jellyfish", "plasma", "sos", "carousel", "kaleidoscope",
        // 8.5.63: 25 more.
        "pendulum", "glitch", "cometrain", "firefly", "blobs", "duobreath", "starfield", "seasons", "candles", "amberbeacon",
        "toxic", "frost", "sakura", "surf", "heat", "dice", "spinner", "bricks", "stacker", "stereo",
        "chameleon", "constellation", "meteor", "drip", "nebula",
        // 8.5.81: 25 more.
        "tesla", "chomper", "prism", "clockhands", "biolum", "signals", "typewriter", "supernova", "goldenhour", "volcano",
        "scanner", "ghost", "snowfall", "tornado", "gears", "rainbowbreath", "peacock", "moodring", "focustimer", "holiday",
        "current", "quasar", "magnet", "vinyl", "ufo",
        "interactive",
    };

    public static bool IsSoftware(string mode) => Software.Contains(mode);

    /// <summary>When SquareDeck started (Mood ring warms up over the session).</summary>
    private static readonly DateTime SessionStart = DateTime.Now;

    /// <summary>The effects that use the first color (and the second).</summary>
    public static bool UsesColor(string mode) => mode is "" or "solid" or "breathing" or "sticks" or "strobe" or "heartbeat" or "comet" or "swap"
        or "gradient" or "wave" or "cycle" or "sparkle" or "ripple"
        or "orbit" or "twin" or "radar" or "pinwheel" or "fillup" or "snake" or "gradientring" or "pulsering" or "yinyang" or "eyes"
        or "metronome" or "dualspin" or "figure8"
        or "bounce" or "pingpong" or "heartring" or "sonar" or "alternator" or "dna" or "sos" or "countdown"
        or "pendulum" or "glitch" or "duobreath" or "spinner" or "bricks" or "dice"
        or "clockhands" or "typewriter" or "scanner" or "gears" or "focustimer" or "current" or "magnet" or "vinyl" or "interactive";
    public static bool UsesSecondColor(string mode) => mode is "breathing" or "sticks" or "swap" or "gradient" or "wave" or "cycle" or "sparkle"
        or "twin" or "pinwheel" or "gradientring" or "yinyang" or "dualspin" or "figure8"
        or "pingpong" or "dna" or "alternator" or "duobreath" or "glitch"
        or "clockhands" or "gears" or "magnet" or "interactive";

    /// <summary>The effects with a speed to choose.</summary>
    public static bool UsesSpeed(string mode) => mode is "breathing" or "rainbow" or "wheel" || (IsSoftware(mode) && mode is not ("temperature" or "gradient" or "cpu" or "clock" or "ambilight" or "batteryring" or "cpuring"
        or "clockhands" or "moodring" or "focustimer" or "interactive"));

    /// <summary>How fast: 0.6 slow, 1 medium, 1.6 fast.</summary>
    public static double SpeedFactor(string? speed) => speed switch { "slow" => 0.6, "fast" => 1.6, _ => 1.0 };

    /// <summary>What the lights should show for an effect, at <paramref name="t"/> seconds.</summary>
    public sealed record Inputs(Color One, Color Two, double Speed, Color Accent, Color Game, int Battery, bool Plugged, int? Temperature)
    {
        /// <summary>How busy the processor is, 0 to 100 (read while the CPU meter plays).</summary>
        public double? Cpu { get; init; }
        /// <summary>The screen's colors near each zone (read while Ambilight plays): left outer, left inner, right inner, right outer.</summary>
        public Color[]? Screen { get; init; }
        /// <summary>Where the sticks point (read while Interactive plays): left x, left y, right x, right y, each -1 to 1 (up is +y).</summary>
        public double[]? Sticks { get; init; }
    }

    /// <summary>The four zones' colors: left outer, left inner, right inner, right outer.</summary>
    public static Color[] Frame(string mode, double t, Inputs i)
    {
        if (Native8.Contains(mode))
        {
            var e = Frame8(mode, t, i);
            return new[] { Mix(e[0], e[1], 0.5), Mix(e[2], e[3], 0.5), Mix(e[4], e[5], 0.5), Mix(e[6], e[7], 0.5) };
        }
        double s = i.Speed;
        Color[] Same(Color c) => new[] { c, c, c, c };
        switch (mode)
        {
            case "": return Same(Color.FromRgb(0xFF, 0x00, 0x33)); // Armoury Crate's red, as a hint
            case "off": return Same(Colors.Black);
            case "solid": return Same(i.One);
            case "accent": return Same(i.Accent);
            case "game": return Same(i.Game);
            case "sticks": return new[] { i.One, i.One, i.Two, i.Two };
            case "battery":
            {
                var c = AllyControl.BatteryColorFor(i.Battery);
                return Same(i.Plugged ? Mix(Colors.Black, c, Wave(t, 0.25)) : c);
            }
            case "breathing":
            {
                double period = 4.0 / s;
                return Same(Mix(i.One, i.Two, Wave(t, 1 / period)));
            }
            case "rainbow": return Same(Hue(t * 60 * s));
            case "wheel":
            {
                double h = t * 90 * s;
                return new[] { Hue(h), Hue(h + 90), Hue(h + 180), Hue(h + 270) };
            }

            // ---- SquareDeck's own
            case "strobe":
            {
                double cycle = 0.42 / s, x = t % cycle;
                return Same(x < cycle * 0.3 ? i.One : Colors.Black);
            }
            case "heartbeat":
            {
                double beat = 1.1 / s, x = t % beat;
                double v = Math.Max(Pulse(x, 0, 0.09), 0.75 * Pulse(x, 0.22, 0.10));
                return Same(Scale(i.One, 0.08 + 0.92 * v));
            }
            case "candle":
            {
                var warm = Color.FromRgb(0xFF, 0x6E, 0x14);
                var c = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    double n = Noise(t * 7 * s + z * 13.1) * 0.6 + Noise(t * 19 * s + z * 5.7) * 0.4;
                    c[z] = Scale(Mix(warm, Color.FromRgb(0xFF, 0xB0, 0x30), n * 0.5), 0.45 + 0.55 * n);
                }
                return c;
            }
            case "police":
            {
                var red = Color.FromRgb(0xFF, 0x10, 0x20);
                var blue = Color.FromRgb(0x10, 0x40, 0xFF);
                double cycle = 0.9 / s, x = t % cycle;
                bool leftTurn = x < cycle / 2;
                double y = x % (cycle / 2);
                bool flash = y < cycle * 0.12 || (y > cycle * 0.2 && y < cycle * 0.32); // two quick flashes each side
                var left = leftTurn && flash ? red : Colors.Black;
                var right = !leftTurn && flash ? blue : Colors.Black;
                return new[] { left, left, right, right };
            }
            case "ocean":
            {
                var c = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    double w = Wave(t * 0.18 * s + z * 0.22, 1);
                    c[z] = Scale(Mix(Color.FromRgb(0x00, 0x46, 0xFF), Color.FromRgb(0x00, 0xE5, 0xD0), w), 0.6 + 0.4 * Wave(t * 0.11 * s + z * 0.3, 1));
                }
                return c;
            }
            case "aurora":
            {
                Color[] stops = { Color.FromRgb(0x20, 0xFF, 0x8A), Color.FromRgb(0x00, 0xD8, 0xFF), Color.FromRgb(0x9B, 0x30, 0xFF), Color.FromRgb(0x20, 0xFF, 0x8A) };
                var c = new Color[4];
                for (int z = 0; z < 4; z++) c[z] = Along(stops, Frac(t * 0.08 * s + z * 0.12));
                return c;
            }
            case "sunset":
            {
                Color[] stops = { Color.FromRgb(0xFF, 0x7A, 0x00), Color.FromRgb(0xFF, 0x2E, 0x6E), Color.FromRgb(0x8A, 0x2B, 0xE2), Color.FromRgb(0xFF, 0x7A, 0x00) };
                var c = new Color[4];
                double glow = 0.7 + 0.3 * Wave(t * 0.15 * s, 1);
                for (int z = 0; z < 4; z++) c[z] = Scale(Along(stops, Frac(t * 0.05 * s + z * 0.18)), glow);
                return c;
            }
            case "comet":
            {
                // A bright head running round the four zones (left outer, left inner, right inner, right outer, and back).
                double pos = t * 3.2 * s;
                int[] path = { 0, 1, 2, 3, 2, 1 };
                double step = pos % path.Length;
                int head = path[(int)step];
                int tail = path[((int)step + path.Length - 1) % path.Length];
                var c = Same(Scale(i.One, 0.05));
                c[tail] = Scale(i.One, 0.35);
                c[head] = i.One;
                return c;
            }
            case "swap":
            {
                double cycle = 2.4 / s, x = (t % cycle) / cycle;
                double k = Math.Clamp((Math.Abs(x - 0.5) * 2 - 0.35) / 0.3, 0, 1); // holds, then crossfades
                var left = Mix(i.Two, i.One, k);
                var right = Mix(i.One, i.Two, k);
                return new[] { left, left, right, right };
            }
            // ---- 8.5.58: ideas from the Colores plugin, written for SquareDeck
            case "gradient":
                // Your two colors spread across the four zones, from the left stick's outer half to the right stick's.
                return new[] { i.One, Mix(i.One, i.Two, 1 / 3.0), Mix(i.One, i.Two, 2 / 3.0), i.Two };
            case "wave":
            {
                var c = new Color[4];
                for (int z = 0; z < 4; z++) c[z] = Mix(i.One, i.Two, Wave(t * 0.35 * s - z * 0.22, 1));
                return c;
            }
            case "cycle":
            {
                // Your colors and the accent move round the zones together, one step at a time with a short fade.
                Color[] pal = { i.One, i.Two, i.Accent };
                double step = t * 0.9 * s;
                int k = (int)Math.Floor(step);
                double f = Math.Clamp((step - k - 0.75) / 0.25, 0, 1);
                var c = new Color[4];
                for (int z = 0; z < 4; z++) c[z] = Mix(pal[(z + k) % pal.Length], pal[(z + k + 1) % pal.Length], f);
                return c;
            }
            case "spiral":
            {
                // Each ring spins: its two halves trade a moving hue, the right stick turning the other way.
                double h = t * 120 * s, spin = Wave(t * 0.8 * s, 1);
                return new[] { Mix(Hue(h), Hue(h + 180), spin), Mix(Hue(h + 180), Hue(h), spin), Mix(Hue(-h + 90), Hue(-h + 270), spin), Mix(Hue(-h + 270), Hue(-h + 90), spin) };
            }
            case "lava":
            {
                Color[] stops = { Color.FromRgb(0x5A, 0x00, 0x00), Color.FromRgb(0xFF, 0x22, 0x00), Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0xD0, 0x40) };
                var c = new Color[4];
                for (int z = 0; z < 4; z++) c[z] = Along(stops, Noise(t * 0.7 * s + z * 3.7) * 0.8 + Noise(t * 2.1 * s + z * 1.3) * 0.2);
                return c;
            }
            case "galaxy":
            {
                var c = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    var deep = Mix(Color.FromRgb(0x14, 0x06, 0x50), Color.FromRgb(0x7A, 0x1C, 0xD8), Noise(t * 0.4 * s + z * 2.3));
                    double star = Math.Max(0, Noise(t * 3 * s + z * 7.9) - 0.82) / 0.18; // now and then a star flares
                    c[z] = Mix(deep, Colors.White, star);
                }
                return c;
            }
            case "sparkle":
            {
                var c = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    double slot = Math.Floor(t * 6 * s);
                    double hit = Hash((int)slot * 4 + z);
                    double fade = 1 - Frac(t * 6 * s);
                    c[z] = hit > 0.78 ? Mix(Scale(i.One, 0.35), i.Two == i.One ? Colors.White : i.Two, fade) : Scale(i.One, 0.35);
                }
                return c;
            }
            case "ripple":
            {
                // A ring of light spreads from the inner halves to the outer ones, then fades.
                double cycle = 1.6 / s, x = (t % cycle) / cycle;
                double inner = Math.Max(0, 1 - Math.Abs(x - 0.15) / 0.2), outer = Math.Max(0, 1 - Math.Abs(x - 0.4) / 0.25);
                var ci = Mix(Scale(i.One, 0.1), i.One, inner);
                var co = Mix(Scale(i.One, 0.1), i.One, outer);
                return new[] { co, ci, ci, co };
            }
            case "cpu":
            {
                // Green when idle through amber to red when busy; breathes faster as it works harder.
                double load = Math.Clamp((i.Cpu ?? 10) / 100, 0, 1);
                Color[] stops = { Color.FromRgb(0x00, 0xE0, 0x6A), Color.FromRgb(0xFF, 0xB0, 0x00), Color.FromRgb(0xFF, 0x10, 0x20) };
                var c = Along(stops, load);
                return Same(Scale(c, 0.6 + 0.4 * Wave(t, 0.3 + load * 1.5)));
            }
            case "clock":
                return Same(ClockColor(DateTime.Now));
            case "ambilight":
                return i.Screen is { Length: 4 } screen ? screen : Same(Scale(i.One, 0.3));

            case "temperature":
            {
                int temp = i.Temperature ?? 55;
                var c = TemperatureColor(temp);
                return Same(temp >= 88 ? Mix(Colors.Black, c, Wave(t, 1.2)) : c); // flashes when it's very hot
            }
        }
        return Same(i.One);
    }

    // ---------------------------------------------------------------- eight zones (the X20: four LEDs per stick)

    /// <summary>
    /// The eight zones' colors: 0 to 3 round the left ring, 4 to 7 round the right ring, each ring clockwise from its
    /// lower left (lower left, upper left, upper right, lower right). The four-zone effects are spread over them: each
    /// half ring's color on its two LEDs.
    /// </summary>
    public static Color[] Frame8(string mode, double t, Inputs i)
    {
        if (!Native8.Contains(mode))
        {
            var f = Frame(mode, t, i);
            return new[] { f[0], f[0], f[1], f[1], f[2], f[2], f[3], f[3] };
        }
        double s = i.Speed;
        var c = new Color[8];
        Color dark = Color.FromRgb(0, 0, 0);
        // Position round a ring as a fraction of a turn: the LED at k of 4, turned by phase (clockwise).
        static double Dist(int k, double phase) { double d = Math.Abs(Frac(k / 4.0 - phase)); return Math.Min(d, 1 - d) * 4; } // in LEDs
        switch (mode)
        {
            case "orbit":
            {
                // A point of light circling each ring, the right one turning the other way, with a short tail.
                double p = t * 0.8 * s;
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Scale(i.One, Math.Max(0.04, 1 - Dist(k, p) * 0.75));
                    c[4 + k] = Scale(i.One, Math.Max(0.04, 1 - Dist(k, -p) * 0.75));
                }
                return c;
            }
            case "twin":
            {
                // Two points chasing round each ring: your first color and your second, opposite each other.
                double p = t * 0.7 * s;
                for (int k = 0; k < 4; k++)
                    for (int r = 0; r < 2; r++)
                    {
                        double ph = r == 0 ? p : -p;
                        double a = Math.Max(0, 1 - Dist(k, ph) * 0.9), b = Math.Max(0, 1 - Dist(k, ph + 0.5) * 0.9);
                        c[r * 4 + k] = Add(Scale(i.One, a), Scale(i.Two, b));
                    }
                return c;
            }
            case "rainbowring":
            {
                double h = t * 90 * s;
                for (int k = 0; k < 4; k++) { c[k] = Hue(h + k * 90); c[4 + k] = Hue(h + k * 90 + 45); }
                return c;
            }
            case "radar":
            {
                // A sweep with a fading trail, both rings in step.
                double p = t * 0.6 * s;
                for (int k = 0; k < 4; k++)
                {
                    double behind = Frac(p - k / 4.0); // how far the sweep has gone past this LED
                    double v = behind < 0.03 ? 1 : Math.Max(0.03, 1 - behind * 1.6);
                    c[k] = c[4 + k] = Scale(i.One, v);
                }
                return c;
            }
            case "pinwheel":
            {
                int step = (int)Math.Floor(t * 3 * s);
                for (int k = 0; k < 4; k++) { c[k] = (k + step) % 2 == 0 ? i.One : i.Two; c[4 + k] = (k + step) % 2 == 0 ? i.Two : i.One; }
                return c;
            }
            case "fillup":
            {
                // The rings fill LED by LED, like a loading bar, hold, then empty.
                double cycle = 3.2 / s, x = (t % cycle) / cycle;
                double level = x < 0.45 ? x / 0.45 * 4 : x < 0.6 ? 4 : Math.Max(0, (1 - (x - 0.6) / 0.4) * 4);
                for (int k = 0; k < 4; k++) c[k] = c[4 + k] = Scale(i.One, Math.Clamp(level - k, 0.04, 1));
                return c;
            }
            case "batteryring":
            {
                // The charge across all eight LEDs (each one an eighth), in the battery's color; the next one breathes while charging.
                double level = Math.Clamp(i.Battery, 0, 100) / 12.5;
                var col = AllyControl.BatteryColorFor(i.Battery);
                for (int z = 0; z < 8; z++)
                {
                    double v = Math.Clamp(level - z, 0, 1);
                    if (i.Plugged && z == (int)Math.Min(7, Math.Floor(level))) v = Math.Max(v, Wave(t, 0.6));
                    c[z] = Scale(col, Math.Max(0.03, v));
                }
                return c;
            }
            case "cpuring":
            {
                double load = Math.Clamp(i.Cpu ?? 10, 0, 100), level = load / 12.5;
                Color[] stops = { Color.FromRgb(0x00, 0xE0, 0x6A), Color.FromRgb(0xFF, 0xB0, 0x00), Color.FromRgb(0xFF, 0x10, 0x20) };
                for (int z = 0; z < 8; z++) c[z] = Scale(Along(stops, z / 7.0), Math.Max(0.03, Math.Clamp(level - z, 0, 1)));
                return c;
            }
            case "snake":
            {
                // A three-light snake slithering round the left ring, then the right, and back.
                double head = t * 5 * s;
                for (int z = 0; z < 8; z++)
                {
                    double d = Frac((head - z) / 8) * 8; // how far behind the head
                    c[z] = d < 3 ? Scale(i.One, 1 - d / 3.4) : Scale(i.One, 0.03);
                }
                return c;
            }
            case "figure8":
            {
                // A light tracing a figure eight: clockwise round the left ring, anticlockwise round the right.
                int[] path = { 0, 1, 2, 3, 4, 7, 6, 5 };
                double head = t * 4 * s;
                for (int n = 0; n < 8; n++)
                {
                    double d = Frac((head - n) / 8) * 8;
                    c[path[n]] = d < 2.5 ? Mix(i.Two, i.One, 1 - d / 2.5) : Scale(i.Two, 0.05);
                }
                return c;
            }
            case "fireworks":
            {
                // Bursts of color popping on random LEDs and fading.
                for (int z = 0; z < 8; z++)
                {
                    double slot = Math.Floor(t * 2.2 * s + z * 0.37);
                    double age = Frac(t * 2.2 * s + z * 0.37);
                    bool burst = Hash((int)slot * 8 + z) > 0.55;
                    c[z] = burst ? Scale(Hue(Hash((int)slot * 13 + z) * 360), Math.Pow(1 - age, 2)) : dark;
                }
                return c;
            }
            case "rain":
            {
                // Drops on the top LEDs, then running down to the bottom ones.
                int[] top = { 1, 2, 5, 6 }, bottom = { 0, 3, 7, 4 };
                var water = Color.FromRgb(0x30, 0x90, 0xFF);
                for (int z = 0; z < 8; z++) c[z] = Scale(water, 0.06);
                for (int d = 0; d < 4; d++)
                {
                    double x = t * 1.4 * s + d * 0.29, slot = Math.Floor(x), age = Frac(x);
                    if (Hash((int)slot * 4 + d) < 0.4) continue;
                    c[top[d]] = Mix(c[top[d]], Color.FromRgb(0xC8, 0xE8, 0xFF), age < 0.4 ? 1 - age / 0.4 : 0);
                    c[bottom[d]] = Mix(c[bottom[d]], water, age > 0.3 && age < 0.8 ? 1 - Math.Abs(age - 0.55) / 0.25 : 0);
                }
                return c;
            }
            case "fire":
            {
                // Flames: the lower LEDs burn brightest, the upper ones flicker.
                bool[] low = { true, false, false, true, true, false, false, true };
                for (int z = 0; z < 8; z++)
                {
                    double n = Noise(t * 9 * s + z * 4.1) * 0.65 + Noise(t * 23 * s + z * 2.3) * 0.35;
                    var col = Mix(Color.FromRgb(0xFF, 0x22, 0x00), Color.FromRgb(0xFF, 0xC0, 0x20), low[z] ? n : n * 0.5);
                    c[z] = Scale(col, (low[z] ? 0.7 : 0.25) + n * (low[z] ? 0.3 : 0.55));
                }
                return c;
            }
            case "matrix":
            {
                for (int z = 0; z < 8; z++)
                {
                    double x = t * 7 * s + z * 1.7, slot = Math.Floor(x);
                    double v = Hash((int)slot * 8 + z);
                    c[z] = v > 0.86 ? Color.FromRgb(0xC0, 0xFF, 0xC0) : Scale(Color.FromRgb(0x00, 0xFF, 0x41), 0.15 + v * 0.6);
                }
                return c;
            }
            case "candy":
            {
                int step = (int)Math.Floor(t * 2.5 * s);
                for (int z = 0; z < 8; z++) c[z] = (z + step) % 2 == 0 ? Color.FromRgb(0xFF, 0x10, 0x30) : Colors.White;
                return c;
            }
            case "lightbar":
            {
                // Rotating beacons: red round the left ring, blue round the right.
                double p = t * 1.3 * s;
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Scale(Color.FromRgb(0xFF, 0x10, 0x20), Math.Max(0, 1 - Dist(k, p) * 1.2));
                    c[4 + k] = Scale(Color.FromRgb(0x10, 0x40, 0xFF), Math.Max(0, 1 - Dist(k, p + 0.5) * 1.2));
                }
                return c;
            }
            case "vortex":
            {
                // Rainbows spinning opposite ways, speeding up and slowing down.
                double turn = (t + 0.6 * Math.Sin(t * 0.7 * s)) * 140 * s;
                for (int k = 0; k < 4; k++) { c[k] = Hue(turn + k * 90); c[4 + k] = Hue(-turn + k * 90 + 180); }
                return c;
            }
            case "gradientring":
            {
                double p = t * 0.2 * s;
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Mix(i.One, i.Two, Wave(k / 4.0 - p, 1));
                    c[4 + k] = Mix(i.One, i.Two, Wave(k / 4.0 + p + 0.5, 1));
                }
                return c;
            }
            case "confetti":
            {
                for (int z = 0; z < 8; z++)
                {
                    double x = t * 4 * s + z * 0.61, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 8 + z) > 0.35 ? Scale(Hue(Hash((int)slot * 31 + z) * 360), 1 - age * 0.7) : dark;
                }
                return c;
            }
            case "pulsering":
            {
                // A wave of brightness running round both rings.
                double p = t * 0.9 * s;
                for (int k = 0; k < 4; k++) c[k] = c[4 + k] = Scale(i.One, 0.12 + 0.88 * Wave(k / 4.0 - p, 1));
                return c;
            }
            case "yinyang":
            {
                // Each ring half your first color and half your second, turning slowly.
                double p = t * 0.35 * s;
                for (int k = 0; k < 4; k++)
                {
                    double a = Frac(k / 4.0 - p), b = Frac(k / 4.0 + p);
                    c[k] = Mix(i.One, i.Two, Math.Clamp((Math.Abs(a - 0.5) - 0.2) / 0.1, 0, 1));
                    c[4 + k] = Mix(i.Two, i.One, Math.Clamp((Math.Abs(b - 0.5) - 0.2) / 0.1, 0, 1));
                }
                return c;
            }
            case "eyes":
            {
                // Two eyes looking around together: a bright pupil that glances to a new spot every so often, and blinks.
                double look = Math.Floor(t * 0.7 * s), glance = Hash((int)look) * 4;
                double prev = Hash((int)look - 1) * 4, f = Math.Clamp(Frac(t * 0.7 * s) / 0.15, 0, 1);
                double at = prev + (glance - prev) * f;
                bool blink = Frac(t * 0.23) < 0.03;
                for (int k = 0; k < 4; k++)
                {
                    double d = Math.Min(Math.Abs(k - at), 4 - Math.Abs(k - at));
                    var col = blink ? dark : Scale(i.One, Math.Max(0.06, 1 - d * 0.8));
                    c[k] = c[4 + k] = col;
                }
                return c;
            }
            case "metronome":
            {
                // A tick at the top of the left ring, then the right, back and forth.
                double beat = 1.0 / s, x = t % (beat * 2);
                bool left = x < beat;
                double v = Math.Max(0.05, 1 - (x % beat) / beat * 1.5);
                for (int z = 0; z < 8; z++) c[z] = Scale(i.One, 0.04);
                if (left) { c[1] = Scale(i.One, v); c[2] = Scale(i.One, v); } else { c[5] = Scale(i.One, v); c[6] = Scale(i.One, v); }
                return c;
            }
            case "dualspin":
            {
                // Your first color spinning round the left ring, your second round the right, opposite ways.
                double p = t * 0.9 * s;
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Scale(i.One, Math.Max(0.05, 1 - Dist(k, p) * 0.6));
                    c[4 + k] = Scale(i.Two, Math.Max(0.05, 1 - Dist(k, -p) * 0.6));
                }
                return c;
            }

            // ---- 8.5.62: 25 more for the eight zones
            case "lighthouse":
            {
                // A warm beam sweeping slowly round both rings as one, with a long afterglow.
                double p = t * 0.25 * s;
                int[] round = { 0, 1, 2, 3, 4, 5, 6, 7 };
                var beam = Color.FromRgb(0xFF, 0xF0, 0xC0);
                foreach (int z in round) { double behind = Frac(p - z / 8.0); c[z] = Scale(beam, behind < 0.04 ? 1 : Math.Max(0.02, 0.7 - behind * 1.4)); }
                return c;
            }
            case "bounce":
            {
                // A ball bouncing from the top of one ring over to the other, squashing on the bottom LEDs.
                double x = Frac(t * 0.7 * s);
                double side = x < 0.5 ? x * 2 : 2 - x * 2; // 0 left .. 1 right
                double height = Math.Abs(Math.Sin(t * 0.7 * s * Math.PI * 2)); // up and down twice a crossing
                for (int z = 0; z < 8; z++)
                {
                    double col = Col(z) / 3.0, top = Top(z) ? 1 : 0;
                    double d = Math.Abs(col - side) * 3 + Math.Abs(top - height) * 1.2;
                    c[z] = Scale(i.One, Math.Max(0.03, 1 - d * 0.6));
                }
                return c;
            }
            case "pingpong":
            {
                // A rally: the ball flies across, each ring lights up as it hits.
                double cycle = 1.4 / s, x = (t % cycle) / cycle;
                bool toRight = x < 0.5;
                double y = toRight ? x * 2 : (x - 0.5) * 2;
                double pos = toRight ? y * 3 : 3 - y * 3;
                for (int z = 0; z < 8; z++) c[z] = Scale(i.One, Math.Max(0.03, 1 - Math.Abs(Col(z) - pos) * 0.9));
                if (y < 0.12) { int hit = toRight ? 0 : 4; for (int k = 0; k < 4; k++) c[hit + k] = Mix(c[hit + k], i.Two, 1 - y / 0.12); }
                return c;
            }
            case "heartring":
            {
                // A heartbeat climbing each ring: the lower LEDs beat first, the upper ones a moment later.
                double beat = 1.1 / s, x = t % beat;
                for (int z = 0; z < 8; z++)
                {
                    double lag = Top(z) ? 0.08 : 0;
                    double v = Math.Max(Pulse(x - lag, 0, 0.09), 0.75 * Pulse(x - lag, 0.22, 0.10));
                    c[z] = Scale(i.One, 0.06 + 0.94 * v);
                }
                return c;
            }
            case "sonar":
            {
                // A ping: everything lights at once, then fades, the far LEDs last, and silence before the next.
                double cycle = 2.2 / s, x = (t % cycle) / cycle;
                for (int z = 0; z < 8; z++)
                {
                    double delay = (Top(z) ? 0.08 : 0) + (Col(z) is 1 or 2 ? 0 : 0.05);
                    double v = x < delay ? 0 : Math.Exp(-(x - delay) * 9);
                    c[z] = Scale(i.One, Math.Max(0.02, v));
                }
                return c;
            }
            case "hourglass":
            {
                // Sand running from the upper LEDs into the lower ones, then turned over.
                double cycle = 6 / s, x = (t % cycle) / cycle;
                var sand = Color.FromRgb(0xFF, 0xC0, 0x50);
                double grain = Wave(t * 6 * s, 1) * 0.15;
                for (int z = 0; z < 8; z++) c[z] = Scale(sand, Math.Max(0.03, Top(z) ? 1 - x : x) + (Top(z) ? 0 : grain * (1 - x)));
                return c;
            }
            case "traffic":
            {
                // Green, then amber, then red, round the rings like a crossing.
                double cycle = 6 / s, x = (t % cycle) / cycle;
                var col = x < 0.45 ? Color.FromRgb(0x00, 0xE0, 0x40) : x < 0.6 ? Color.FromRgb(0xFF, 0xA0, 0x00) : Color.FromRgb(0xFF, 0x10, 0x10);
                bool blink = x > 0.45 && x < 0.6 && Frac(t * 4) < 0.5;
                for (int z = 0; z < 8; z++) c[z] = blink ? Scale(col, 0.15) : col;
                return c;
            }
            case "thunderstorm":
            {
                // Dark storm clouds, and now and then lightning cracking across the rings.
                double slot = Math.Floor(t * 0.8 * s), age = Frac(t * 0.8 * s);
                bool strike = Hash((int)slot) > 0.6;
                double flash = strike ? (age < 0.06 ? 1 : age > 0.1 && age < 0.14 ? 0.7 : 0) : 0;
                for (int z = 0; z < 8; z++)
                {
                    var cloud = Scale(Color.FromRgb(0x20, 0x28, 0x60), 0.25 + Noise(t * 0.6 + z * 3.3) * 0.3);
                    c[z] = Mix(cloud, Colors.White, flash * (Top(z) ? 1 : 0.6));
                }
                return c;
            }
            case "embers":
            {
                // A dying fire: dark red, with embers glowing up and fading here and there.
                for (int z = 0; z < 8; z++)
                {
                    double n = Math.Pow(Noise(t * 1.5 * s + z * 5.3), 3);
                    c[z] = Mix(Color.FromRgb(0x30, 0x02, 0x00), Color.FromRgb(0xFF, 0x60, 0x10), n * 1.4);
                }
                return c;
            }
            case "tide":
            {
                // The tide coming in over the lower LEDs, up to the upper ones, and going out.
                double level = Wave(t * 0.15 * s, 1);
                var deep = Color.FromRgb(0x00, 0x30, 0xA0);
                var foam = Color.FromRgb(0x80, 0xE8, 0xFF);
                for (int z = 0; z < 8; z++)
                {
                    double v = Top(z) ? Math.Clamp((level - 0.5) * 2, 0, 1) : Math.Clamp(level * 2, 0, 1);
                    c[z] = Mix(Scale(deep, 0.08), Mix(deep, foam, Noise(t * 2 + z) * 0.6), v);
                }
                return c;
            }
            case "aurorawave":
            {
                Color[] stops = { Color.FromRgb(0x20, 0xFF, 0x8A), Color.FromRgb(0x00, 0xC8, 0xFF), Color.FromRgb(0xA0, 0x30, 0xFF), Color.FromRgb(0x20, 0xFF, 0x8A) };
                for (int z = 0; z < 8; z++)
                    c[z] = Scale(Along(stops, Frac(t * 0.07 * s + z / 8.0 + Noise(t * 0.3 + z) * 0.15)), 0.5 + 0.5 * Wave(t * 0.2 * s + z / 8.0, 1));
                return c;
            }
            case "disco":
            {
                // Every LED a bold color, all changing on the beat.
                int beat = (int)Math.Floor(t * 2.2 * s);
                for (int z = 0; z < 8; z++) c[z] = Hue(Math.Floor(Hash(beat * 8 + z) * 6) * 60);
                return c;
            }
            case "alternator":
            {
                // Quick flashes jumping between the two sticks: your first color, then your second.
                double cycle = 0.5 / s, x = t % (cycle * 2);
                bool left = x < cycle;
                bool on = (x % cycle) < cycle * 0.35;
                for (int k = 0; k < 4; k++) { c[k] = left && on ? i.One : dark; c[4 + k] = !left && on ? i.Two : dark; }
                return c;
            }
            case "spectrum":
            {
                // A music meter: each stick a pair of bars bouncing to a beat, green to red as they rise.
                for (int col = 0; col < 4; col++)
                {
                    double level = 0.35 + 0.65 * Math.Abs(Math.Sin(t * (2.1 + col * 0.7) * s + col)) * (0.6 + 0.4 * Noise(t * 3 + col * 9));
                    int lower = col switch { 0 => 0, 1 => 3, 2 => 4, _ => 7 }, upper = col switch { 0 => 1, 1 => 2, 2 => 5, _ => 6 };
                    c[lower] = Scale(Color.FromRgb(0x00, 0xE0, 0x50), Math.Clamp(level * 2, 0.05, 1));
                    c[upper] = Scale(level > 0.75 ? Color.FromRgb(0xFF, 0x20, 0x20) : Color.FromRgb(0xFF, 0xB0, 0x00), Math.Clamp((level - 0.5) * 2, 0.03, 1));
                }
                return c;
            }
            case "dna":
            {
                // Two strands twisting round the rings: your colors weaving past each other.
                for (int z = 0; z < 8; z++)
                {
                    double ph = t * 1.2 * s + z / 8.0 * Math.PI * 2;
                    c[z] = Add(Scale(i.One, 0.5 + 0.5 * Math.Sin(ph)), Scale(i.Two, 0.5 - 0.5 * Math.Sin(ph)));
                }
                return c;
            }
            case "sunrise":
            {
                // Night to day and back: deep blue, then the lower LEDs glow orange and the light climbs.
                double day = Wave(t * 0.06 * s, 1);
                for (int z = 0; z < 8; z++)
                {
                    double k = Top(z) ? Math.Clamp((day - 0.3) / 0.7, 0, 1) : Math.Clamp(day / 0.6, 0, 1);
                    Color[] stops = { Color.FromRgb(0x06, 0x0A, 0x30), Color.FromRgb(0xFF, 0x50, 0x20), Color.FromRgb(0xFF, 0xC0, 0x60), Color.FromRgb(0xC0, 0xE8, 0xFF) };
                    c[z] = Along(stops, k);
                }
                return c;
            }
            case "neon":
            {
                // A neon sign: pink on the left, cyan on the right, buzzing and now and then dropping out.
                for (int z = 0; z < 8; z++)
                {
                    var tube = z < 4 ? Color.FromRgb(0xFF, 0x2E, 0xC8) : Color.FromRgb(0x20, 0xF0, 0xFF);
                    double flicker = Hash((int)Math.Floor(t * 12) * 8 + z / 4) > 0.94 ? 0.15 : 0.85 + 0.15 * Noise(t * 30 + z);
                    c[z] = Scale(tube, flicker);
                }
                return c;
            }
            case "rgbquad":
            {
                // Red, green, blue and white, one per LED, turning round each ring.
                Color[] four = { Color.FromRgb(0xFF, 0x10, 0x10), Color.FromRgb(0x10, 0xFF, 0x30), Color.FromRgb(0x10, 0x40, 0xFF), Colors.White };
                int step = (int)Math.Floor(t * 2 * s);
                for (int k = 0; k < 4; k++) { c[k] = four[(k + step) % 4]; c[4 + k] = four[(k - step % 4 + 8) % 4]; }
                return c;
            }
            case "countdown":
            {
                // Eight lights going out one by one, then a flash, and again.
                double cycle = 8 / s, x = (t % cycle) / cycle * 9;
                int left = 8 - (int)Math.Floor(x);
                bool flash = left <= 0;
                int[] order = { 6, 5, 4, 7, 3, 0, 1, 2 }; // out from the right stick to the left
                for (int n = 0; n < 8; n++) c[order[n]] = flash ? (Frac(x) < 0.5 ? Colors.White : dark) : n < 8 - left ? Scale(i.One, 0.04) : i.One;
                return c;
            }
            case "wormholes":
            {
                // Two swirling holes: orange spinning on the left, blue on the right.
                double p = t * 0.9 * s;
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Mix(Color.FromRgb(0x60, 0x18, 0x00), Color.FromRgb(0xFF, 0x8A, 0x10), Wave(k / 4.0 - p, 1));
                    c[4 + k] = Mix(Color.FromRgb(0x00, 0x18, 0x60), Color.FromRgb(0x20, 0x9C, 0xFF), Wave(k / 4.0 + p, 1));
                }
                return c;
            }
            case "jellyfish":
            {
                // A soft glow pulsing at the top of each ring, its tentacles trailing down a moment later.
                double x = t * 0.5 * s;
                for (int z = 0; z < 8; z++)
                {
                    double v = Top(z) ? Wave(x, 1) : Wave(x - 0.15, 1) * 0.7;
                    c[z] = Scale(Mix(Color.FromRgb(0x30, 0xE0, 0xFF), Color.FromRgb(0xFF, 0x60, 0xD0), Wave(t * 0.08, 1)), 0.08 + 0.92 * v);
                }
                return c;
            }
            case "plasma":
            {
                for (int z = 0; z < 8; z++) c[z] = Hue(Noise(t * 0.5 * s + z * 0.8) * 540 + t * 20 * s);
                return c;
            }
            case "sos":
            {
                // Three short, three long, three short, in your color.
                double unit = 0.18 / s;
                int[] pattern = { 1, 0, 1, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 };
                bool on = pattern[(int)(t / unit) % pattern.Length] == 1;
                for (int z = 0; z < 8; z++) c[z] = on ? i.One : Scale(i.One, 0.03);
                return c;
            }
            case "carousel":
            {
                // Soft pastel lights going round and bobbing up and down, like a merry-go-round.
                double p = t * 0.35 * s;
                for (int z = 0; z < 8; z++)
                {
                    var pastel = Mix(Hue(z * 45 + p * 360), Colors.White, 0.35);
                    c[z] = Scale(pastel, 0.55 + 0.45 * Wave(t * 0.8 * s + z * 0.25, 1));
                }
                return c;
            }
            case "kaleidoscope":
            {
                // The two rings mirror each other, with colors folding and changing.
                for (int k = 0; k < 4; k++)
                {
                    var col = Hue(Noise(t * 0.6 * s + Math.Min(k, 3 - k) * 2.1) * 360 + t * 30 * s);
                    c[k] = col;
                    c[4 + (3 - k)] = col; // mirrored
                }
                return c;
            }

            // ---- 8.5.63: 25 more
            case "pendulum":
            {
                // A light swinging along the bottom of each ring, slowing at each end.
                double swing = Math.Sin(t * 1.6 * s); // -1 .. 1
                double[] pos = { -1, -0.4, 0.4, 1 }; // lower left, upper left, upper right, lower right, unrolled along the arc
                for (int k = 0; k < 4; k++)
                {
                    double v = Math.Max(0.03, 1 - Math.Abs(pos[k] - swing) * 1.3);
                    c[k] = Scale(i.One, v); c[4 + k] = Scale(i.One, Math.Max(0.03, 1 - Math.Abs(pos[k] + swing) * 1.3));
                }
                return c;
            }
            case "glitch":
            {
                // Your color, with LEDs glitching to your second color, white or off for a split second.
                for (int z = 0; z < 8; z++)
                {
                    double slot = Math.Floor(t * 14 * s);
                    double g = Hash((int)slot * 8 + z);
                    c[z] = g > 0.93 ? Colors.White : g > 0.86 ? i.Two : g > 0.82 ? dark : i.One;
                }
                return c;
            }
            case "cometrain":
            {
                // Little comets falling from the top LEDs to the bottom ones, here and there.
                var tail = Color.FromRgb(0x50, 0x90, 0xFF);
                for (int z = 0; z < 8; z++) c[z] = Scale(tail, 0.04);
                for (int col = 0; col < 4; col++)
                {
                    double x = t * 1.8 * s + col * 0.41, slot = Math.Floor(x), age = Frac(x);
                    if (Hash((int)slot * 4 + col) < 0.45) continue;
                    int up = col switch { 0 => 1, 1 => 2, 2 => 5, _ => 6 }, down = col switch { 0 => 0, 1 => 3, 2 => 4, _ => 7 };
                    c[up] = Mix(c[up], Colors.White, age < 0.35 ? 1 - age / 0.35 : 0);
                    c[down] = Mix(c[down], tail, age > 0.25 && age < 0.75 ? 1 - Math.Abs(age - 0.5) / 0.25 : 0);
                }
                return c;
            }
            case "firefly":
            {
                // Fireflies on a summer night: soft yellow-green glows rising and fading at random.
                var glow = Color.FromRgb(0xC8, 0xFF, 0x40);
                for (int z = 0; z < 8; z++)
                {
                    double x = t * 0.45 * s + z * 0.73, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 8 + z) > 0.5 ? Scale(glow, Math.Sin(age * Math.PI)) : Scale(Color.FromRgb(0x02, 0x08, 0x02), 1);
                }
                return c;
            }
            case "blobs":
            {
                // Lava-lamp blobs drifting up and down each ring.
                for (int r = 0; r < 2; r++)
                {
                    double h = Wave(t * 0.12 * s + r * 0.5, 1); // the blob's height, 0 bottom .. 1 top
                    for (int k = 0; k < 4; k++)
                    {
                        double ledH = k is 1 or 2 ? 1 : 0;
                        double v = Math.Max(0, 1 - Math.Abs(ledH - h) * 1.4);
                        c[r * 4 + k] = Mix(Color.FromRgb(0x60, 0x00, 0x30), Color.FromRgb(0xFF, 0x50, 0x20), v);
                    }
                }
                return c;
            }
            case "duobreath":
            {
                // The left stick breathes your first color, the right your second, taking turns.
                double a = Wave(t * 0.3 * s, 1);
                for (int k = 0; k < 4; k++) { c[k] = Scale(i.One, 0.05 + 0.95 * a); c[4 + k] = Scale(i.Two, 0.05 + 0.95 * (1 - a)); }
                return c;
            }
            case "starfield":
            {
                // A night sky: stars of different brightness twinkling on deep blue.
                for (int z = 0; z < 8; z++)
                {
                    double tw = Noise(t * (1.2 + Hash(z) * 2) * s + z * 9.1);
                    c[z] = Mix(Color.FromRgb(0x02, 0x04, 0x18), Color.FromRgb(0xE8, 0xF0, 0xFF), Math.Pow(tw, 2.5) * (0.4 + Hash(z + 50) * 0.6));
                }
                return c;
            }
            case "seasons":
            {
                // Spring green, summer gold, autumn orange and winter white, turning round the rings.
                Color[] four = { Color.FromRgb(0x50, 0xE0, 0x40), Color.FromRgb(0xFF, 0xD0, 0x30), Color.FromRgb(0xFF, 0x60, 0x10), Color.FromRgb(0xD8, 0xF0, 0xFF) };
                double p = t * 0.25 * s;
                for (int k = 0; k < 4; k++) { c[k] = Along(new[] { four[0], four[1], four[2], four[3], four[0] }, Frac(k / 4.0 + p)); c[4 + k] = c[k]; }
                return c;
            }
            case "candles":
            {
                // A candle on each stick: a warm flame at the top, a steady glow below, each flickering on its own.
                for (int z = 0; z < 8; z++)
                {
                    double n = Noise(t * 8 * s + z * 6.7 + (z < 4 ? 0 : 40));
                    c[z] = Top(z) ? Scale(Color.FromRgb(0xFF, 0x90, 0x20), 0.55 + 0.45 * n) : Scale(Color.FromRgb(0xFF, 0x50, 0x08), 0.35 + 0.2 * n);
                }
                return c;
            }
            case "amberbeacon":
            {
                // A roadworks beacon: an amber flash spinning fast round both rings together.
                double p = t * 1.8 * s;
                var amber = Color.FromRgb(0xFF, 0x9A, 0x00);
                for (int k = 0; k < 4; k++) c[k] = c[4 + k] = Scale(amber, Math.Max(0.02, 1 - Dist(k, p) * 1.5));
                return c;
            }
            case "toxic":
            {
                // Radioactive green, glowing and bubbling.
                for (int z = 0; z < 8; z++)
                {
                    double n = Noise(t * 2.5 * s + z * 3.9);
                    c[z] = Mix(Color.FromRgb(0x10, 0x40, 0x00), Color.FromRgb(0xB0, 0xFF, 0x20), 0.4 + 0.6 * n * Wave(t * 0.4, 1));
                }
                return c;
            }
            case "frost":
            {
                // Ice: pale blues with glints of white catching the light.
                for (int z = 0; z < 8; z++)
                {
                    double glint = Math.Max(0, Noise(t * 2 * s + z * 11.3) - 0.8) / 0.2;
                    c[z] = Mix(Mix(Color.FromRgb(0x30, 0x70, 0xC0), Color.FromRgb(0x90, 0xD8, 0xFF), Noise(t * 0.3 + z)), Colors.White, glint);
                }
                return c;
            }
            case "sakura":
            {
                // Cherry blossom pinks, with petals drifting down from the top LEDs.
                for (int z = 0; z < 8; z++) c[z] = Mix(Color.FromRgb(0xFF, 0x70, 0xA8), Color.FromRgb(0xFF, 0xB8, 0xD0), Noise(t * 0.4 + z * 2));
                for (int col = 0; col < 4; col++)
                {
                    double x = t * 0.9 * s + col * 0.53, slot = Math.Floor(x), age = Frac(x);
                    if (Hash((int)slot * 4 + col + 7) < 0.5) continue;
                    int up = col switch { 0 => 1, 1 => 2, 2 => 5, _ => 6 }, down = col switch { 0 => 0, 1 => 3, 2 => 4, _ => 7 };
                    c[age < 0.5 ? up : down] = Mix(c[age < 0.5 ? up : down], Colors.White, Math.Sin(Frac(age * 2) * Math.PI));
                }
                return c;
            }
            case "surf":
            {
                // A wave building up from the bottom, breaking white at the top, and washing back.
                double cycle = 3 / s, x = (t % cycle) / cycle;
                var sea = Color.FromRgb(0x00, 0x60, 0xD0);
                for (int z = 0; z < 8; z++)
                {
                    double rise = Top(z) ? Math.Clamp((x - 0.35) / 0.25, 0, 1) : Math.Clamp(x / 0.35, 0, 1);
                    double crash = Top(z) && x > 0.6 && x < 0.75 ? 1 - Math.Abs(x - 0.67) / 0.08 : 0;
                    double back = x > 0.75 ? 1 - (x - 0.75) / 0.25 : 1;
                    c[z] = Mix(Scale(sea, 0.1 + 0.9 * rise * back), Colors.White, Math.Max(0, crash));
                }
                return c;
            }
            case "heat":
            {
                // A heat map: each LED drifting between cold blue and hot red.
                Color[] stops = { Color.FromRgb(0x00, 0x30, 0xFF), Color.FromRgb(0x00, 0xE0, 0x80), Color.FromRgb(0xFF, 0xE0, 0x00), Color.FromRgb(0xFF, 0x10, 0x10) };
                for (int z = 0; z < 8; z++) c[z] = Along(stops, Noise(t * 0.35 * s + z * 1.9));
                return c;
            }
            case "dice":
            {
                // A roll of the dice: the LEDs tumble, then settle on a number (one to six lit) for a moment.
                double cycle = 2.6 / s, x = (t % cycle) / cycle;
                int roll = (int)Math.Floor(t / cycle);
                int shown = x < 0.35 ? 1 + (int)(Hash((int)(t * 20)) * 8) % 8 : 1 + (int)(Hash(roll) * 6);
                int[] order = { 1, 6, 2, 5, 0, 7, 3, 4 };
                for (int n = 0; n < 8; n++) c[order[n]] = n < shown ? i.One : Scale(i.One, 0.04);
                return c;
            }
            case "spinner":
            {
                // A loading spinner: two bright LEDs racing round each ring with a short fade behind.
                double p = t * 1.5 * s;
                for (int k = 0; k < 4; k++)
                {
                    double behind = Frac(p - k / 4.0);
                    double v = behind < 0.3 ? 1 - behind : Math.Max(0.04, 0.4 - behind * 0.5);
                    c[k] = c[4 + k] = Scale(i.One, v);
                }
                return c;
            }
            case "bricks":
            {
                // A ball knocking out the lit LEDs one by one with a flash, until the rings are clear; then they rebuild.
                double cycle = 6 / s, x = (t % cycle) / cycle * 10;
                int gone = Math.Min(8, (int)Math.Floor(x));
                int[] order = { 2, 5, 1, 6, 3, 4, 0, 7 };
                for (int n = 0; n < 8; n++)
                {
                    bool hitNow = n == gone && Frac(x) < 0.15;
                    c[order[n]] = hitNow ? Colors.White : n < gone ? Scale(i.One, 0.03) : i.One;
                }
                return c;
            }
            case "stacker":
            {
                // Colored blocks dropping in and stacking from the bottom LEDs up, then the rows clear.
                double cycle = 5 / s, x = (t % cycle) / cycle * 10;
                int placed = Math.Min(8, (int)Math.Floor(x));
                int round = (int)Math.Floor(t / cycle);
                int[] order = { 0, 3, 4, 7, 1, 2, 5, 6 }; // bottom row first, then the top
                bool clear = x >= 9;
                for (int n = 0; n < 8; n++)
                {
                    var col = Hue(Math.Floor(Hash(round * 8 + n) * 6) * 60);
                    c[order[n]] = clear ? (Frac(x * 3) < 0.5 ? Colors.White : dark) : n < placed ? col : dark;
                }
                return c;
            }
            case "stereo":
            {
                // A stereo level meter: the left stick and the right bounce on their own, green, amber, red.
                for (int r = 0; r < 2; r++)
                {
                    double level = Math.Clamp(0.25 + 0.75 * Math.Abs(Math.Sin(t * (2.3 + r * 0.9) * s + r * 2)) * (0.5 + 0.5 * Noise(t * 4 + r * 30)), 0, 1) * 4;
                    int[] up = { 0, 1, 2, 3 }; // lower left, upper left, upper right, lower right: round the ring as it rises
                    Color[] steps = { Color.FromRgb(0x00, 0xE0, 0x50), Color.FromRgb(0x80, 0xF0, 0x20), Color.FromRgb(0xFF, 0xB0, 0x00), Color.FromRgb(0xFF, 0x20, 0x20) };
                    for (int n = 0; n < 4; n++) c[r * 4 + up[n]] = Scale(steps[n], Math.Clamp(level - n, 0.04, 1));
                }
                return c;
            }
            case "chameleon":
            {
                // Slowly shifting between SquareDeck's accent color and the selected game's color, LED by LED.
                for (int z = 0; z < 8; z++) c[z] = Mix(i.Accent, i.Game, Wave(t * 0.1 * s + z / 8.0, 1));
                return c;
            }
            case "constellation":
            {
                // Pairs of stars joining up into shapes: two or three LEDs at a time light and link, then fade.
                int[][] shapes = { new[] { 1, 6 }, new[] { 0, 3, 5 }, new[] { 2, 4, 7 }, new[] { 1, 2, 5, 6 }, new[] { 0, 7 }, new[] { 3, 4, 1 } };
                double x = t * 0.4 * s;
                var shape = shapes[(int)Math.Floor(x) % shapes.Length];
                double v = Math.Sin(Frac(x) * Math.PI);
                for (int z = 0; z < 8; z++) c[z] = Color.FromRgb(0x03, 0x04, 0x12);
                foreach (int z in shape) c[z] = Mix(c[z], Color.FromRgb(0xE0, 0xE8, 0xFF), v);
                return c;
            }
            case "meteor":
            {
                // Now and then a meteor streaks across from the left stick to the right, sparkling as it goes.
                double cycle = 2.5 / s, x = (t % cycle) / cycle * 2;
                for (int z = 0; z < 8; z++) c[z] = Color.FromRgb(0x02, 0x02, 0x08);
                if (x < 1)
                {
                    double head = x * 4.5;
                    for (int z = 0; z < 8; z++)
                    {
                        double d = head - Col(z) - (Top(z) ? 0.3 : 0);
                        if (d >= 0 && d < 1.8) c[z] = Mix(Color.FromRgb(0xFF, 0x80, 0x20), Colors.White, Math.Max(0, 1 - d)) is var m ? Scale(m, 1 - d / 1.8) : dark;
                    }
                }
                return c;
            }
            case "drip":
            {
                // Drops of your color forming at the top of each ring and running down.
                for (int z = 0; z < 8; z++) c[z] = Scale(i.One, 0.05);
                for (int r = 0; r < 2; r++)
                {
                    double x = Frac(t * 0.7 * s + r * 0.5);
                    int a = r * 4;
                    if (x < 0.4) { double v = x / 0.4; c[a + 1] = Scale(i.One, 0.05 + 0.95 * v); c[a + 2] = Scale(i.One, 0.05 + 0.95 * v); }
                    else if (x < 0.7) { double v = 1 - (x - 0.4) / 0.3; c[a + 1] = c[a + 2] = Scale(i.One, Math.Max(0.05, v)); c[a + 0] = c[a + 3] = Scale(i.One, 1 - v); }
                    else { double v = 1 - (x - 0.7) / 0.3; c[a + 0] = c[a + 3] = Scale(i.One, Math.Max(0.05, v)); }
                }
                return c;
            }
            case "nebula":
            {
                // A spinning nebula: pinks and purples swirling round each ring, with the odd star.
                double p = t * 0.3 * s;
                for (int k = 0; k < 4; k++)
                {
                    for (int r = 0; r < 2; r++)
                    {
                        double a = r == 0 ? k / 4.0 - p : k / 4.0 + p;
                        var gas = Mix(Color.FromRgb(0x50, 0x10, 0x90), Color.FromRgb(0xFF, 0x40, 0xA0), Wave(a, 1));
                        double star = Math.Max(0, Noise(t * 2.5 + (r * 4 + k) * 7.7) - 0.85) / 0.15;
                        c[r * 4 + k] = Mix(gas, Colors.White, star);
                    }
                }
                return c;
            }

            // ---- 8.5.81: 25 more.
            case "tesla":
            {
                // Violet sparks jumping between two LEDs at a time, crackling at random.
                for (int z = 0; z < 8; z++) c[z] = Color.FromRgb(0x10, 0x04, 0x24);
                double step = 0.11 / s, n0 = t / step;
                int n = (int)Math.Floor(n0);
                if (Hash(n * 3 + 1) > 0.35)
                {
                    int a = (int)(Hash(n * 7 + 2) * 8) % 8, b = (int)(Hash(n * 11 + 5) * 8) % 8;
                    var arc = Mix(Color.FromRgb(0xA0, 0x60, 0xFF), Colors.White, Hash(n * 13) * 0.7);
                    double v = 1 - Frac(n0);
                    c[a] = Mix(c[a], arc, v);
                    c[b] = Mix(c[b], arc, v * 0.8);
                }
                return c;
            }
            case "chomper":
            {
                // A yellow chomper going round each ring over a trail of pellets, a ghost of a new color chasing it.
                double p = t * 0.35 * s;
                Color[] ghosts = { Color.FromRgb(0xFF, 0x40, 0x40), Color.FromRgb(0xFF, 0x9E, 0xE8), Color.FromRgb(0x30, 0xE0, 0xFF), Color.FromRgb(0xFF, 0xA0, 0x30) };
                var ghost = ghosts[(int)Math.Floor(p) % 4];
                double chomp = 0.65 + 0.35 * Math.Abs(Math.Sin(t * 10 * s));
                for (int r = 0; r < 2; r++)
                    for (int k = 0; k < 4; k++)
                    {
                        double ph = r == 0 ? p : p + 0.5;
                        var col = Add(Scale(Colors.White, 0.12), Scale(ghost, Math.Max(0, 1 - Dist(k, ph - 0.375))));
                        c[r * 4 + k] = Mix(col, Scale(Color.FromRgb(0xFF, 0xD8, 0x00), chomp), Math.Max(0, 1 - Dist(k, ph)));
                    }
                return c;
            }
            case "prism":
            {
                // A beam of white crossing from the left stick and splitting into a rainbow on the right.
                double x = Frac(t * 0.4 * s) * 1.6;
                for (int z = 0; z < 8; z++)
                {
                    int col = Col(z);
                    double v = Math.Max(0.06, 1 - Math.Abs(x * 4 - col - 0.5) * 0.8);
                    c[z] = Scale(col < 2 ? Colors.White : Hue(t * 40 * s + (z - 4) * 90), v);
                }
                return c;
            }
            case "clockhands":
            {
                // The real time: the hour hand on the left ring and the minute hand on the right, in your first color.
                var now = DateTime.Now;
                double hour = (now.Hour % 12 + now.Minute / 60.0) / 12, minute = (now.Minute + now.Second / 60.0) / 60;
                for (int k = 0; k < 4; k++)
                {
                    // LED k sits 0.625 of a turn from twelve o'clock (lower left, upper left, upper right, lower right).
                    c[k] = Mix(Scale(i.Two, 0.08), i.One, Math.Max(0, 1 - Dist(k, hour - 0.625)));
                    c[4 + k] = Mix(Scale(i.Two, 0.08), i.One, Math.Max(0, 1 - Dist(k, minute - 0.625)));
                }
                return c;
            }
            case "biolum":
            {
                // Dark water: waves rolling across, waking blue-green glows as they pass.
                for (int z = 0; z < 8; z++)
                {
                    double w = Frac(t * 0.25 * s - Col(z) * 0.18);
                    double crest = Math.Exp(-Math.Pow((w - 0.5) * 6, 2));
                    double sparkle = Math.Max(0, Noise(t * 3 * s + z * 9.1) - 0.6) / 0.4;
                    var glow = Mix(Color.FromRgb(0x00, 0xC8, 0xB4), Color.FromRgb(0x40, 0xA0, 0xFF), Noise(z * 3.3 + t * 0.5));
                    c[z] = Mix(Color.FromRgb(0x00, 0x04, 0x12), glow, Math.Min(1, crest * (0.7 + 0.6 * sparkle)));
                }
                return c;
            }
            case "signals":
            {
                // Amber blinkers: three on the left, three on the right, then three hazards.
                var amber = Color.FromRgb(0xFF, 0x8A, 0x00);
                double phase = t / (0.66 / s);
                int blink = (int)Math.Floor(phase) % 9;
                bool on = Frac(phase) < 0.55, left = blink < 3 || blink >= 6, right = blink >= 3;
                for (int z = 0; z < 8; z++) c[z] = on && (z < 4 ? left : right) ? amber : Color.FromRgb(0x14, 0x08, 0x00);
                return c;
            }
            case "typewriter":
            {
                // The LEDs typed in one by one, then a ding and the carriage goes back.
                double n = t / (0.22 / s);
                int pos = (int)Math.Floor(n) % 11;
                for (int z = 0; z < 8; z++)
                    c[z] = pos >= 8 ? (pos == 8 ? Colors.White : Scale(i.One, pos == 9 ? 0.4 : 0.04))
                         : z < pos ? i.One : z == pos ? Mix(i.One, Colors.White, 1 - Frac(n)) : Scale(i.One, 0.04);
                return c;
            }
            case "supernova":
            {
                // A star swelling dim red to orange, bursting white, then fading through blue and violet to dark.
                double cycle = 6 / s, x = (t % cycle) / cycle;
                Color col;
                if (x < 0.5) col = Scale(Mix(Color.FromRgb(0x60, 0x08, 0x00), Color.FromRgb(0xFF, 0x80, 0x20), x * 2), 0.1 + x * (0.8 + 0.2 * Math.Sin(t * 30)));
                else
                {
                    double y = (x - 0.5) * 2;
                    col = y < 0.08 ? Colors.White
                        : Scale(Along(new[] { Colors.White, Color.FromRgb(0x60, 0xA0, 0xFF), Color.FromRgb(0xA0, 0x30, 0xFF), Color.FromRgb(0x20, 0x00, 0x30) }, y), 1 - y * 0.85);
                }
                for (int z = 0; z < 8; z++) c[z] = Scale(col, x >= 0.5 && x < 0.6 && Col(z) is 0 or 3 ? 0.7 : 1);
                return c;
            }
            case "goldenhour":
            {
                // The light of a late afternoon: amber drifting slowly to rose and back, brighter on top.
                for (int z = 0; z < 8; z++)
                {
                    double w = Wave(t * 0.06 * s + Col(z) * 0.08 + (Top(z) ? 0.04 : 0), 1);
                    c[z] = Scale(Mix(Color.FromRgb(0xFF, 0x9A, 0x20), Color.FromRgb(0xFF, 0x4E, 0x7A), w), Top(z) ? 1 : 0.8);
                }
                return c;
            }
            case "volcano":
            {
                // A dark red rumble, then an eruption shooting from the bottom LEDs to the top.
                double cycle = 5 / s, x = (t % cycle) / cycle;
                for (int z = 0; z < 8; z++)
                {
                    var col = Scale(Color.FromRgb(0xC0, 0x10, 0x00), 0.25 + 0.2 * Noise(t * 8 * s + z * 4.1));
                    if (x > 0.7)
                    {
                        double e = (x - 0.7) / 0.3;
                        double h = Top(z) ? Math.Max(0, e * 2 - 0.5) : Math.Min(1, e * 3);
                        double fade = e > 0.6 ? 1 - (e - 0.6) / 0.4 : 1;
                        col = Mix(col, Mix(Color.FromRgb(0xFF, 0x50, 0x00), Color.FromRgb(0xFF, 0xE0, 0x40), Top(z) ? 0.6 : 0.2), Math.Clamp(h, 0, 1) * fade);
                    }
                    c[z] = col;
                }
                return c;
            }
            case "scanner":
            {
                // A line of your color sweeping back and forth across both sticks, leaving a fading tail.
                double u = Frac(t * 0.45 * s), pos = (u < 0.5 ? u * 2 : 2 - u * 2) * 3;
                for (int z = 0; z < 8; z++)
                {
                    double d = Col(z) - pos;
                    if (u >= 0.5) d = -d; // positive: ahead of the line
                    double v = d > 0.5 ? 0.02 : d > -0.5 ? 1 : Math.Max(0.02, 1 + (d + 0.5) * 0.45);
                    c[z] = Scale(i.One, v);
                }
                return c;
            }
            case "ghost":
            {
                // A pale light appearing out of the dark, drifting to the next LED and vanishing.
                double cycle = 3 / s, n0 = t / cycle, x = Frac(n0);
                int n = (int)Math.Floor(n0);
                for (int z = 0; z < 8; z++) c[z] = Color.FromRgb(0x02, 0x03, 0x06);
                int a = (int)(Hash(n * 5 + 3) * 8) % 8, b = a / 4 * 4 + (a % 4 + 1) % 4;
                var pale = Color.FromRgb(0xB8, 0xD8, 0xFF);
                double v = Math.Sin(x * Math.PI);
                c[a] = Mix(c[a], pale, v * (1 - x));
                c[b] = Mix(c[b], pale, v * x);
                return c;
            }
            case "snowfall":
            {
                // White flakes drifting slowly down the rings on a night-blue sky.
                for (int z = 0; z < 8; z++) c[z] = Color.FromRgb(0x06, 0x0A, 0x18);
                var flake = Color.FromRgb(0xE8, 0xF2, 0xFF);
                for (int col = 0; col < 4; col++)
                {
                    double x = Frac(t * 0.18 * s + Hash(col * 17));
                    int top = col switch { 0 => 1, 1 => 2, 2 => 5, _ => 6 }, bottom = col switch { 0 => 0, 1 => 3, 2 => 4, _ => 7 };
                    double vt = x < 0.5 ? Math.Sin(x * 2 * Math.PI) : 0, vb = x > 0.3 && x < 0.8 ? Math.Sin((x - 0.3) / 0.5 * Math.PI) : 0;
                    c[top] = Mix(c[top], flake, vt);
                    c[bottom] = Mix(c[bottom], flake, vb * 0.85);
                }
                return c;
            }
            case "tornado":
            {
                // A grey swirl spinning faster and faster, turning white at full speed, then winding down.
                double cycle = 8 / s, x = (t % cycle) / cycle;
                double turn = (x / 2 - Math.Sin(2 * Math.PI * x) / (4 * Math.PI)) * 3 * cycle * s + Math.Floor(t / cycle) * 1.5 * cycle * s;
                double speed = Math.Pow(Math.Sin(Math.PI * x), 2), tail = 0.8 - speed * 0.5;
                var col = Mix(Color.FromRgb(0x70, 0x80, 0x90), Color.FromRgb(0xE0, 0xF0, 0xFF), speed);
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Scale(col, Math.Max(0.05, 1 - Dist(k, turn) * tail));
                    c[4 + k] = Scale(col, Math.Max(0.05, 1 - Dist(k, turn + 0.5) * tail));
                }
                return c;
            }
            case "gears":
            {
                // Two cogs meshing: the left ring turns one way in your first color, the right the other way in your second.
                double p = t * 0.25 * s;
                for (int k = 0; k < 4; k++)
                {
                    double a = 0.5 + 0.5 * Math.Cos(4 * Math.PI * (k / 4.0 - p)), b = 0.5 + 0.5 * Math.Cos(4 * Math.PI * (k / 4.0 + p) + Math.PI);
                    c[k] = Mix(Scale(i.One, 0.1), i.One, a);
                    c[4 + k] = Mix(Scale(i.Two, 0.1), i.Two, b);
                }
                return c;
            }
            case "rainbowbreath":
            {
                // Slow breaths, each one a new color.
                double period = 4 / s, x = t / period, v = Math.Sin(Frac(x) * Math.PI);
                var col = Scale(Hue(Math.Floor(x) * 67), 0.04 + 0.96 * v * v);
                for (int z = 0; z < 8; z++) c[z] = col;
                return c;
            }
            case "peacock":
            {
                // A peacock's tail: teal, green, blue and gold eyes, shimmering.
                Color[] stops = { Color.FromRgb(0x00, 0x6E, 0x6E), Color.FromRgb(0x10, 0xC0, 0x90), Color.FromRgb(0x20, 0x50, 0xD0), Color.FromRgb(0xE0, 0xB0, 0x30), Color.FromRgb(0x00, 0x6E, 0x6E) };
                for (int z = 0; z < 8; z++)
                    c[z] = Scale(Along(stops, Frac(t * 0.12 * s + Hash(z * 31) * 0.9)), 0.7 + 0.3 * Math.Sin(t * 3 * s + z * 1.7));
                return c;
            }
            case "moodring":
            {
                // Like a mood ring, warming over the session: calm blue, teal, green, amber, red, then purple after four hours.
                double hours = (DateTime.Now - SessionStart).TotalHours;
                Color[] stops = { Color.FromRgb(0x20, 0x60, 0xFF), Color.FromRgb(0x00, 0xC0, 0xB0), Color.FromRgb(0x40, 0xD0, 0x40), Color.FromRgb(0xFF, 0xB0, 0x20), Color.FromRgb(0xFF, 0x40, 0x30), Color.FromRgb(0xC0, 0x20, 0xA0) };
                var col = Along(stops, Math.Clamp(hours / 4.0, 0, 1));
                for (int z = 0; z < 8; z++) c[z] = Scale(col, 0.75 + 0.25 * Wave(t + z * 0.1, 0.12));
                return c;
            }
            case "focustimer":
            {
                // A 25-minute focus timer filling the LEDs one by one in your color, then five minutes of a soft green break.
                double x = t % (30 * 60);
                if (x < 25 * 60)
                {
                    double fill = x / (25 * 60) * 8;
                    for (int z = 0; z < 8; z++) c[z] = z < Math.Floor(fill) ? i.One : z < fill ? Scale(i.One, 0.1 + 0.9 * Frac(fill)) : Scale(i.One, 0.06);
                }
                else
                {
                    var rest = Scale(Color.FromRgb(0x30, 0xE0, 0x80), 0.3 + 0.7 * Wave(x, 0.25));
                    for (int z = 0; z < 8; z++) c[z] = rest;
                }
                return c;
            }
            case "holiday":
            {
                // A string of red, green and gold bulbs, twinkling and stepping along.
                Color[] bulbs = { Color.FromRgb(0xFF, 0x10, 0x10), Color.FromRgb(0x10, 0xD0, 0x30), Color.FromRgb(0xFF, 0xC0, 0x20) };
                int n = (int)Math.Floor(t / (0.8 / s));
                for (int z = 0; z < 8; z++) c[z] = Scale(bulbs[(z + n) % 3], 0.55 + 0.45 * Noise(t * 4 * s + z * 5.3));
                return c;
            }
            case "current":
            {
                // A crackling pulse of your color racing from the left stick to the right.
                double x = Frac(t * 0.9 * s) * 5.5 - 0.5;
                var spark = Mix(i.One, Colors.White, 0.35);
                for (int z = 0; z < 8; z++)
                {
                    double d = Math.Abs(x - Col(z) - (Top(z) ? 0.15 : 0));
                    c[z] = Add(Scale(i.One, 0.06), Scale(spark, Math.Max(0, 1 - d * 1.4) * (0.85 + 0.15 * Noise(t * 40 + z))));
                }
                return c;
            }
            case "quasar":
            {
                // Two opposite beams of blue-white light turning round a violet core.
                double p = t * 0.5 * s;
                var beam = Color.FromRgb(0xC8, 0xE0, 0xFF);
                var core = Color.FromRgb(0x28, 0x10, 0x50);
                for (int r = 0; r < 2; r++)
                    for (int k = 0; k < 4; k++)
                    {
                        double ph = r == 0 ? p : -p;
                        double v = Math.Max(Math.Max(0, 1 - Dist(k, ph) * 1.2), Math.Max(0, 1 - Dist(k, ph + 0.5) * 1.2));
                        c[r * 4 + k] = Mix(core, beam, v);
                    }
                return c;
            }
            case "magnet":
            {
                // Your two colors pulled together from the outer edges, meeting in the middle with a flash, then pushed apart.
                double x = Frac(t * 0.35 * s), pos = x < 0.5 ? Math.Pow(x * 2, 2) : 1 - (x - 0.5) * 2;
                double flash = Math.Max(0, 1 - Math.Abs(x - 0.5) * 12);
                for (int z = 0; z < 8; z++)
                {
                    bool left = z < 4;
                    int col = Col(z);
                    double target = left ? pos * 1.5 : 3 - pos * 1.5;
                    double v = Math.Max(0.05, 1 - Math.Abs(col - target) * 0.9);
                    c[z] = Mix(Scale(left ? i.One : i.Two, v), Colors.White, flash * (col is 1 or 2 ? 1 : 0.3));
                }
                return c;
            }
            case "vinyl":
            {
                // A record turning: a dark disc tinted by your color, a glint of light going round at record speed.
                double p = t * 0.55 * s;
                var disc = Add(Color.FromRgb(0x10, 0x08, 0x14), Scale(i.One, 0.25));
                for (int k = 0; k < 4; k++)
                {
                    c[k] = Mix(disc, Colors.White, 0.9 * Math.Max(0, 1 - Dist(k, p) * 1.3));
                    c[4 + k] = Mix(disc, Colors.White, 0.9 * Math.Max(0, 1 - Dist(k, p + 0.5) * 1.3));
                }
                return c;
            }
            case "interactive":
            {
                // 9.0.3: each ring lights where its stick points (left stick, first color; right stick, second), brighter
                // the further it's pushed, and fades behind as you turn it. At rest: a faint glow. With no sticks to read
                // (the preview), the sticks are pretended to turn slowly round.
                var st = i.Sticks ?? new[] { Math.Cos(t * 2.2), Math.Sin(t * 2.2), Math.Cos(-t * 1.7 + 1), Math.Sin(-t * 1.7 + 1) };
                // The LEDs' directions round each ring: lower left, upper left, upper right, lower right.
                double[] dirs = { 225, 135, 45, 315 };
                lock (StickTrail)
                {
                    for (int r = 0; r < 2; r++)
                    {
                        double x = st[r * 2], y = st[r * 2 + 1];
                        double mag = Math.Clamp(Math.Sqrt(x * x + y * y), 0, 1);
                        mag = mag < 0.18 ? 0 : (mag - 0.18) / 0.82; // a small dead zone
                        double angle = Math.Atan2(y, x) * 180 / Math.PI;
                        for (int k = 0; k < 4; k++)
                        {
                            double d = Math.Abs(((angle - dirs[k]) % 360 + 540) % 360 - 180); // 0 to 180 degrees away
                            double now = mag * Math.Pow(Math.Max(0, Math.Cos(d * Math.PI / 180)), 1.6);
                            int z = r * 4 + k;
                            StickTrail[z] = Math.Max(now, StickTrail[z] * 0.8); // a soft trail as it turns
                            c[z] = Scale(r == 0 ? i.One : i.Two, Math.Max(0.06, StickTrail[z]));
                        }
                    }
                }
                return c;
            }
            case "ufo":
            {
                // A flying saucer: colored lights circling the top LEDs, a green tractor beam pulsing below.
                double p = t * 0.8 * s, beam = Wave(t * s, 0.5);
                Color[] lamps = { Color.FromRgb(0xFF, 0x30, 0x30), Color.FromRgb(0x30, 0xFF, 0x60), Color.FromRgb(0x30, 0x80, 0xFF), Color.FromRgb(0xFF, 0xE0, 0x30) };
                for (int z = 0; z < 8; z++)
                    c[z] = Top(z) ? lamps[((int)Math.Floor(p * 4) + z) % 4] : Scale(Color.FromRgb(0x40, 0xFF, 0x70), 0.1 + 0.9 * beam);
                return c;
            }
        }
        for (int z = 0; z < 8; z++) c[z] = i.One;
        return c;
    }

    /// <summary>Which of the four columns an LED is in, left to right (each stick's left and right half).</summary>
    private static int Col(int z) => z switch { 0 or 1 => 0, 2 or 3 => 1, 4 or 5 => 2, _ => 3 };

    /// <summary>True for an LED on the upper half of its ring (upper left and upper right).</summary>
    private static bool Top(int z) => z is 1 or 2 or 5 or 6;

    private static Color Add(Color a, Color b) =>
        Color.FromRgb((byte)Math.Min(255, a.R + b.R), (byte)Math.Min(255, a.G + b.G), (byte)Math.Min(255, a.B + b.B));

    /// <summary>The time of day as a color: deep blue at night, orange at dawn, bright at midday, purple at dusk.</summary>
    public static Color ClockColor(DateTime now)
    {
        double h = now.Hour + now.Minute / 60.0;
        (double At, Color C)[] stops =
        {
            (0, Color.FromRgb(0x10, 0x18, 0x60)), (5.5, Color.FromRgb(0x30, 0x20, 0x80)), (7, Color.FromRgb(0xFF, 0x70, 0x20)),
            (10, Color.FromRgb(0xFF, 0xD8, 0x80)), (14, Color.FromRgb(0xC8, 0xE8, 0xFF)), (18, Color.FromRgb(0xFF, 0x60, 0x40)),
            (20, Color.FromRgb(0x90, 0x30, 0xD0)), (22, Color.FromRgb(0x20, 0x20, 0x80)), (24, Color.FromRgb(0x10, 0x18, 0x60)),
        };
        for (int k = 0; k < stops.Length - 1; k++)
            if (h >= stops[k].At && h <= stops[k + 1].At)
                return Mix(stops[k].C, stops[k + 1].C, (h - stops[k].At) / (stops[k + 1].At - stops[k].At));
        return stops[0].C;
    }

    /// <summary>Blue when cool, green, amber, then red when hot.</summary>
    public static Color TemperatureColor(int celsius)
    {
        Color[] stops = { Color.FromRgb(0x00, 0x80, 0xFF), Color.FromRgb(0x00, 0xE0, 0x6A), Color.FromRgb(0xFF, 0xB0, 0x00), Color.FromRgb(0xFF, 0x10, 0x20) };
        return Along(stops, Math.Clamp((celsius - 45) / 45.0, 0, 1));
    }

    // ---------------------------------------------------------------- color helpers

    public static Color Mix(Color a, Color b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
    }

    public static Color Scale(Color c, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    private static Color Along(Color[] stops, double t)
    {
        double x = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int a = Math.Min((int)x, stops.Length - 2);
        return Mix(stops[a], stops[a + 1], x - a);
    }

    public static Color Hue(double degrees)
    {
        double h = ((degrees % 360) + 360) % 360 / 60;
        double x = 1 - Math.Abs(h % 2 - 1);
        (double r, double g, double b) = h switch
        {
            < 1 => (1d, x, 0d), < 2 => (x, 1d, 0d), < 3 => (0d, 1d, x), < 4 => (0d, x, 1d), < 5 => (x, 0d, 1d), _ => (1d, 0d, x)
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    /// <summary>0 to 1 and back, <paramref name="perSecond"/> times a second.</summary>
    private static double Wave(double t, double perSecond) => 0.5 - 0.5 * Math.Cos(t * perSecond * Math.PI * 2);

    private static double Frac(double x) => x - Math.Floor(x);

    private static double Pulse(double x, double at, double width) => x < at ? 0 : Math.Exp(-(x - at) / width) * Math.Min(1, (x - at) / 0.02);

    /// <summary>Smooth noise from 0 to 1.</summary>
    private static double Noise(double x)
    {
        int i = (int)Math.Floor(x);
        double f = x - i, u = f * f * (3 - 2 * f);
        return Hash(i) * (1 - u) + Hash(i + 1) * u;
    }

    private static double Hash(int n)
    {
        unchecked
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7FFFFFFF) / (double)0x7FFFFFFF;
        }
    }

    // ---------------------------------------------------------------- playing it on the lights

    private static System.Threading.Timer? _timer;
    private static string _mode = "";
    private static Inputs? _inputs;
    private static int _percent = 100;
    private static Color[]? _last;
    private static DateTime _started;
    private static DateTime _tempAt;
    private static readonly object Gate = new();

    /// <summary>Starts (or changes) an effect SquareDeck plays on the lights itself.</summary>
    public static void Play(string mode, Inputs inputs, int percent)
    {
        lock (Gate)
        {
            bool same = _timer != null && _mode == mode;
            _mode = mode;
            _inputs = inputs;
            _percent = percent;
            _last = null;
            if (same) return;
            _started = DateTime.UtcNow;
            int period = mode switch
            {
                "ocean" or "aurora" or "sunset" or "temperature" or "lava" or "galaxy" => 200, // slow drifts need fewer frames
                "gradient" or "clock" => 1000,
                "cpu" => 150,
                "ambilight" => 125,
                "interactive" => 30, // follows the sticks without a lag you can feel
                _ => 90,
            };
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Tick(), null, 0, period);
        }
    }

    /// <summary>Stops playing (another effect, off, or the Ally's own effect has taken over).</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            _mode = "";
            _last = null;
        }
    }

    public static bool Playing => _timer != null;

    // ---------------------------------------------------------------- Interactive: the sticks (9.0.3)

    private static readonly double[] StickTrail = new double[8];
    private static readonly Gamepad StickReader = new();

    /// <summary>Where the first controller's sticks point, -1 to 1 (it's only read, never taken from a game).</summary>
    private static double[] SticksNow()
    {
        var raw = StickReader.GetRaw();
        if (!raw.Connected) return new double[4];
        static double N(short v) => Math.Clamp(v / 32767.0, -1, 1);
        return new[] { N(raw.LeftX), N(raw.LeftY), N(raw.RightX), N(raw.RightY) };
    }

    // ---------------------------------------------------------------- readings for the live effects

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    private static long _idle0, _total0;
    private static double _cpu;

    /// <summary>How busy the processor has been since the last reading, 0 to 100 (smoothed).</summary>
    private static double CpuNow()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return _cpu;
        long total = kernel + user; // kernel time includes idle
        long dTotal = total - _total0, dIdle = idle - _idle0;
        _idle0 = idle; _total0 = total;
        if (dTotal > 0) _cpu = _cpu * 0.6 + 0.4 * Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
        return _cpu;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private static Color[]? _screen;

    /// <summary>
    /// Ambilight: the average color of the screen beside each stick (the lower left and lower right of the screen,
    /// outer and inner halves), shrunk to four pixels by Windows (cheap), made a little more vivid and smoothed.
    /// </summary>
    private static Color[] ScreenColors()
    {
        var result = _screen ?? new Color[4];
        IntPtr screenDc = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
            if (w <= 0 || h <= 0) return result;
            screenDc = GetDC(IntPtr.Zero);
            mem = CreateCompatibleDC(screenDc);
            bmp = CreateCompatibleBitmap(screenDc, 4, 1);
            old = SelectObject(mem, bmp);
            SetStretchBltMode(mem, 4); // HALFTONE: a true average
            int top = h * 45 / 100, height = h - top, quarter = w / 8;
            // left outer, left inner, right inner, right outer
            int[] xs = { 0, quarter, w - 2 * quarter, w - quarter };
            for (int z = 0; z < 4; z++) StretchBlt(mem, z, 0, 1, 1, screenDc, xs[z], top, quarter, height, 0x00CC0020);
            var fresh = new Color[4];
            for (int z = 0; z < 4; z++)
            {
                uint px = GetPixel(mem, z, 0);
                var c = Color.FromRgb((byte)(px & 0xFF), (byte)((px >> 8) & 0xFF), (byte)((px >> 16) & 0xFF));
                fresh[z] = Vivid(c);
            }
            for (int z = 0; z < 4; z++) result[z] = _screen == null ? fresh[z] : Mix(result[z], fresh[z], 0.45);
            _screen = result;
        }
        catch (Exception) { }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
        return (Color[])result.Clone();
    }

    /// <summary>A screen color made punchier for the rings: more saturated, never quite black.</summary>
    private static Color Vivid(Color c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max < 12) return Color.FromRgb(6, 6, 8);
        double gray = (c.R + c.G + c.B) / 3.0, boost = 1.6, lift = 255 / Math.Max(max, 1) * 0.9;
        byte F(double v) => (byte)Math.Clamp((gray + (v - gray) * boost) * Math.Min(lift, 1.8), 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    private static void Tick()
    {
        string mode;
        Inputs? inputs;
        int percent;
        double t;
        lock (Gate)
        {
            if (_timer == null || _inputs == null) return;
            mode = _mode; inputs = _inputs; percent = _percent;
            t = (DateTime.UtcNow - _started).TotalSeconds;
            if (mode == "temperature" && DateTime.UtcNow - _tempAt > TimeSpan.FromSeconds(2))
            {
                _tempAt = DateTime.UtcNow;
                _inputs = inputs = inputs with { Temperature = AsusAcpi.Temperature(AsusAcpi.CpuTemp) ?? inputs.Temperature };
            }
        }
        if (mode is "cpu" or "cpuring") inputs = inputs with { Cpu = CpuNow() };
        if (mode == "ambilight") inputs = inputs with { Screen = ScreenColors() };
        if (mode == "interactive") inputs = inputs with { Sticks = SticksNow() };
        try
        {
            // Eight zones on the X20 (four LEDs per stick), four on the other Allys.
            bool eight = AllyHid.ZoneCount == 8;
            var frame = (eight ? Frame8(mode, t, inputs) : Frame(mode, t, inputs)).Select(c => Scale(c, percent / 100.0)).ToArray();
            if (_last != null && frame.SequenceEqual(_last)) return; // nothing changed: nothing sent
            _last = frame;
            if (frame.All(c => c == frame[0])) AllyHid.SetLive(frame[0]);
            else if (eight) AllyHid.SetZones8Live(frame);
            else AllyHid.SetZonesLive(frame);
        }
        catch (Exception) { }
    }
}
