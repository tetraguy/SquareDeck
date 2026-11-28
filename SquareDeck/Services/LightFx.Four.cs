using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>
/// 9.0.12: every effect drawn for the X20's eight LEDs has a version of its own for the four zones of the other ROG
/// Allys (the ROG Ally, Ally X and Xbox Ally): left outer, left inner, right inner, right outer, each half of a stick's
/// ring. Until now those Allys got the eight-LED frame averaged in pairs, which halved every moving light and lost
/// anything that moved up and down (four-zone rings have no top and bottom). Here, motion round a ring swaps between
/// its two halves, motion across the sticks runs over the four zones in a row, and what rose from the bottom LEDs to
/// the top (rain, tides, fire, sunrise…) now moves in from the outer halves, flashes, or pulses instead.
/// </summary>
public static partial class LightFx
{
    private static readonly double[] StickTrail4 = new double[4];

    /// <summary>The frame for the lights this Ally has, as eight colors (four zones are shown on two LEDs each), for previews.</summary>
    public static Color[] FrameForDevice(string mode, double t, Inputs i)
    {
        if (AllyHid.ZoneCount == 8) return Frame8(mode, t, i);
        var f = Frame(mode, t, i);
        return new[] { f[0], f[0], f[1], f[1], f[2], f[2], f[3], f[3] };
    }

    /// <summary>
    /// The four zones (0 left outer, 1 left inner, 2 right inner, 3 right outer) for an effect first drawn for eight
    /// LEDs. Zone z is half <c>z % 2</c> of ring <c>z / 2</c>.
    /// </summary>
    private static Color[] Frame4(string mode, double t, Inputs i)
    {
        double s = i.Speed;
        var c = new Color[4];
        Color dark = Color.FromRgb(0, 0, 0);
        static int Ring(int z) => z / 2;
        static int Half(int z) => z % 2;
        // How far half h of a ring is from a point at phase (a fraction of a turn, clockwise from lower left), 0 to 1.
        // The left half's middle sits an eighth of a turn round, the right half's five eighths.
        static double D2(int h, double phase) { double d = Math.Abs(Frac(h * 0.5 + 0.125 - phase)); return Math.Min(d, 1 - d) * 2; }
        // How far zone z is from a point going round all four zones in a loop (0 to 2).
        static double Loop(int z, double phase) { double d = Math.Abs(Frac(z / 4.0 - phase)); return Math.Min(d, 1 - d) * 4; }
        void All(Color col) { for (int z = 0; z < 4; z++) c[z] = col; }

        switch (mode)
        {
            // ---- 8.5.61
            case "orbit":
            {
                // The point swaps from one half of each ring to the other, the rings turning opposite ways.
                double p = t * 0.8 * s;
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, Math.Max(0.04, 1 - D2(Half(z), Ring(z) == 0 ? p : -p) * 0.95));
                return c;
            }
            case "twin":
            {
                double p = t * 0.7 * s;
                for (int z = 0; z < 4; z++)
                {
                    double ph = Ring(z) == 0 ? p : -p;
                    c[z] = Add(Scale(i.One, Math.Max(0, 1 - D2(Half(z), ph))), Scale(i.Two, Math.Max(0, 1 - D2(Half(z), ph + 0.5))));
                }
                return c;
            }
            case "rainbowring":
            {
                double h = t * 90 * s;
                for (int z = 0; z < 4; z++) c[z] = Hue(h + Half(z) * 180 + Ring(z) * 90);
                return c;
            }
            case "radar":
            {
                double p = t * 0.6 * s;
                for (int z = 0; z < 4; z++)
                {
                    double behind = Frac(p - (Half(z) * 0.5 + 0.125));
                    c[z] = Scale(i.One, behind < 0.06 ? 1 : Math.Max(0.03, 1 - behind * 1.3));
                }
                return c;
            }
            case "pinwheel":
            {
                int step = (int)Math.Floor(t * 3 * s);
                for (int z = 0; z < 4; z++) c[z] = (z + step) % 2 == 0 ? i.One : i.Two;
                return c;
            }
            case "fillup":
            {
                // The four zones fill left to right, hold, then empty.
                double cycle = 3.2 / s, x = (t % cycle) / cycle;
                double level = x < 0.45 ? x / 0.45 * 4 : x < 0.6 ? 4 : Math.Max(0, (1 - (x - 0.6) / 0.4) * 4);
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, Math.Clamp(level - z, 0.04, 1));
                return c;
            }
            case "batteryring":
            {
                // The charge across the four zones (each a quarter); the next one breathes while charging.
                double level = Math.Clamp(i.Battery, 0, 100) / 25.0;
                var col = AllyControl.BatteryColorFor(i.Battery);
                for (int z = 0; z < 4; z++)
                {
                    double v = Math.Clamp(level - z, 0, 1);
                    if (i.Plugged && z == (int)Math.Min(3, Math.Floor(level))) v = Math.Max(v, Wave(t, 0.6));
                    c[z] = Scale(col, Math.Max(0.03, v));
                }
                return c;
            }
            case "cpuring":
            {
                double load = Math.Clamp(i.Cpu ?? 10, 0, 100), level = load / 25;
                Color[] stops = { Color.FromRgb(0x00, 0xE0, 0x6A), Color.FromRgb(0xFF, 0xB0, 0x00), Color.FromRgb(0xFF, 0x10, 0x20) };
                for (int z = 0; z < 4; z++) c[z] = Scale(Along(stops, z / 3.0), Math.Max(0.03, Math.Clamp(level - z, 0, 1)));
                return c;
            }
            case "snake":
            {
                // A two-zone snake going round all four zones.
                double head = t * 2.6 * s;
                for (int z = 0; z < 4; z++)
                {
                    double d = Frac((head - z) / 4) * 4;
                    c[z] = d < 2 ? Scale(i.One, 1 - d / 2.3) : Scale(i.One, 0.03);
                }
                return c;
            }
            case "figure8":
            {
                // Through the middle twice a lap: round the left stick's halves, across, round the right's, and back.
                int[] path = { 1, 0, 1, 2, 3, 2 };
                double head = t * 3 * s;
                for (int z = 0; z < 4; z++) c[z] = Scale(i.Two, 0.05);
                for (int n = 0; n < path.Length; n++)
                {
                    double d = Frac((head - n) / path.Length) * path.Length;
                    if (d < 1.6) c[path[n]] = Mix(c[path[n]], i.One, 1 - d / 1.6);
                }
                return c;
            }
            case "fireworks":
            {
                for (int z = 0; z < 4; z++)
                {
                    double x = t * 2.2 * s + z * 0.37, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 4 + z + 900) > 0.5 ? Scale(Hue(Hash((int)slot * 13 + z) * 360), Math.Pow(1 - age, 2)) : dark;
                }
                return c;
            }
            case "rain":
            {
                // Drops landing on a zone: a pale splash, then the water fading back.
                var water = Color.FromRgb(0x30, 0x90, 0xFF);
                for (int z = 0; z < 4; z++)
                {
                    double x = t * 1.6 * s + z * 0.29, slot = Math.Floor(x), age = Frac(x);
                    var col = Scale(water, 0.06);
                    if (Hash((int)slot * 4 + z) > 0.4)
                        col = age < 0.15 ? Mix(water, Color.FromRgb(0xC8, 0xE8, 0xFF), 1 - age / 0.15) : Mix(col, water, Math.Max(0, 1 - (age - 0.15) / 0.6));
                    c[z] = col;
                }
                return c;
            }
            case "fire":
            {
                // Flames: the outer halves burn steadily, the inner ones flicker higher.
                for (int z = 0; z < 4; z++)
                {
                    bool outer = z is 0 or 3;
                    double n = Noise(t * 9 * s + z * 4.1) * 0.65 + Noise(t * 23 * s + z * 2.3) * 0.35;
                    var col = Mix(Color.FromRgb(0xFF, 0x22, 0x00), Color.FromRgb(0xFF, 0xC0, 0x20), outer ? n * 0.6 : n);
                    c[z] = Scale(col, (outer ? 0.65 : 0.35) + n * (outer ? 0.35 : 0.65));
                }
                return c;
            }
            case "matrix":
            {
                for (int z = 0; z < 4; z++)
                {
                    double v = Hash((int)Math.Floor(t * 7 * s + z * 1.7) * 4 + z);
                    c[z] = v > 0.84 ? Color.FromRgb(0xC0, 0xFF, 0xC0) : Scale(Color.FromRgb(0x00, 0xFF, 0x41), 0.15 + v * 0.6);
                }
                return c;
            }
            case "candy":
            {
                int step = (int)Math.Floor(t * 2.5 * s);
                for (int z = 0; z < 4; z++) c[z] = (z + step) % 2 == 0 ? Color.FromRgb(0xFF, 0x10, 0x30) : Colors.White;
                return c;
            }
            case "lightbar":
            {
                double p = t * 1.3 * s;
                for (int z = 0; z < 4; z++)
                    c[z] = Ring(z) == 0 ? Scale(Color.FromRgb(0xFF, 0x10, 0x20), Math.Max(0, 1 - D2(Half(z), p) * 1.1))
                                        : Scale(Color.FromRgb(0x10, 0x40, 0xFF), Math.Max(0, 1 - D2(Half(z), p + 0.5) * 1.1));
                return c;
            }
            case "vortex":
            {
                double turn = (t + 0.6 * Math.Sin(t * 0.7 * s)) * 140 * s;
                for (int z = 0; z < 4; z++) c[z] = Ring(z) == 0 ? Hue(turn + Half(z) * 180) : Hue(-turn + Half(z) * 180 + 90);
                return c;
            }
            case "gradientring":
            {
                double p = t * 0.2 * s;
                for (int z = 0; z < 4; z++) c[z] = Mix(i.One, i.Two, Wave(Half(z) * 0.5 + (Ring(z) == 0 ? -p : p + 0.25), 1));
                return c;
            }
            case "confetti":
            {
                for (int z = 0; z < 4; z++)
                {
                    double x = t * 4 * s + z * 0.61, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 4 + z + 300) > 0.3 ? Scale(Hue(Hash((int)slot * 31 + z) * 360), 1 - age * 0.7) : dark;
                }
                return c;
            }
            case "pulsering":
            {
                // A wave of brightness running along all four zones.
                double p = t * 0.9 * s;
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, 0.12 + 0.88 * Wave(z / 4.0 - p, 1));
                return c;
            }
            case "yinyang":
            {
                // Each stick half one color and half the other, the halves trading places slowly.
                double a = Wave(t * 0.35 * s, 1);
                for (int z = 0; z < 4; z++)
                {
                    bool first = (Half(z) == 0) ^ (Ring(z) == 1);
                    c[z] = first ? Mix(i.One, i.Two, a) : Mix(i.Two, i.One, a);
                }
                return c;
            }
            case "eyes":
            {
                // Two eyes glancing left and right together, blinking now and then.
                double look = Math.Floor(t * 0.7 * s), glance = Hash((int)look), prev = Hash((int)look - 1);
                double f = Math.Clamp(Frac(t * 0.7 * s) / 0.15, 0, 1), at = prev + (glance - prev) * f; // 0 left .. 1 right
                bool blink = Frac(t * 0.23) < 0.03;
                for (int z = 0; z < 4; z++) c[z] = blink ? dark : Scale(i.One, Math.Max(0.06, 1 - Math.Abs(Half(z) - at) * 0.9));
                return c;
            }
            case "metronome":
            {
                double beat = 1.0 / s, x = t % (beat * 2);
                bool left = x < beat;
                double v = Math.Max(0.05, 1 - (x % beat) / beat * 1.5);
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, (Ring(z) == 0) == left ? v : 0.04);
                return c;
            }
            case "dualspin":
            {
                double p = t * 0.9 * s;
                for (int z = 0; z < 4; z++)
                    c[z] = Ring(z) == 0 ? Scale(i.One, Math.Max(0.05, 1 - D2(Half(z), p) * 0.8)) : Scale(i.Two, Math.Max(0.05, 1 - D2(Half(z), -p) * 0.8));
                return c;
            }

            // ---- 8.5.62
            case "lighthouse":
            {
                double p = t * 0.25 * s;
                var beam = Color.FromRgb(0xFF, 0xF0, 0xC0);
                for (int z = 0; z < 4; z++) { double behind = Frac(p - z / 4.0); c[z] = Scale(beam, behind < 0.07 ? 1 : Math.Max(0.02, 0.7 - behind * 1.2)); }
                return c;
            }
            case "bounce":
            {
                // A ball crossing from stick to stick, brightest as it lands.
                double x = Frac(t * 0.7 * s), side = (x < 0.5 ? x * 2 : 2 - x * 2) * 3;
                double height = Math.Abs(Math.Sin(t * 0.7 * s * Math.PI * 2));
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, Math.Max(0.03, (1 - Math.Abs(z - side) * 0.7) * (1 - 0.45 * height)));
                return c;
            }
            case "pingpong":
            {
                double cycle = 1.4 / s, x = (t % cycle) / cycle;
                bool toRight = x < 0.5;
                double y = toRight ? x * 2 : (x - 0.5) * 2, pos = toRight ? y * 3 : 3 - y * 3;
                for (int z = 0; z < 4; z++) c[z] = Scale(i.One, Math.Max(0.03, 1 - Math.Abs(z - pos) * 0.9));
                if (y < 0.12) { int hit = toRight ? 0 : 2; for (int k = 0; k < 2; k++) c[hit + k] = Mix(c[hit + k], i.Two, 1 - y / 0.12); }
                return c;
            }
            case "heartring":
            {
                // A heartbeat starting on the outer halves and reaching the inner ones a moment later.
                double beat = 1.1 / s, x = t % beat;
                for (int z = 0; z < 4; z++)
                {
                    double lag = z is 1 or 2 ? 0.08 : 0;
                    c[z] = Scale(i.One, 0.06 + 0.94 * Math.Max(Pulse(x - lag, 0, 0.09), 0.75 * Pulse(x - lag, 0.22, 0.10)));
                }
                return c;
            }
            case "sonar":
            {
                // A ping in the middle, fading out to the outer halves last.
                double cycle = 2.2 / s, x = (t % cycle) / cycle;
                for (int z = 0; z < 4; z++)
                {
                    double delay = z is 1 or 2 ? 0 : 0.09;
                    c[z] = Scale(i.One, Math.Max(0.02, x < delay ? 0 : Math.Exp(-(x - delay) * 9)));
                }
                return c;
            }
            case "hourglass":
            {
                // Sand running from one stick into the other, then turned over and running back.
                double cycle = 6 / s, x = (t % cycle) / cycle;
                bool flipped = (int)Math.Floor(t / cycle) % 2 == 1;
                var sand = Color.FromRgb(0xFF, 0xC0, 0x50);
                double grain = Wave(t * 6 * s, 1) * 0.15;
                for (int z = 0; z < 4; z++)
                {
                    bool from = (Ring(z) == 0) != flipped;
                    c[z] = Scale(sand, Math.Max(0.03, from ? 1 - x : x) + (from ? 0 : grain * (1 - x)));
                }
                return c;
            }
            case "traffic":
            {
                double cycle = 6 / s, x = (t % cycle) / cycle;
                var col = x < 0.45 ? Color.FromRgb(0x00, 0xE0, 0x40) : x < 0.6 ? Color.FromRgb(0xFF, 0xA0, 0x00) : Color.FromRgb(0xFF, 0x10, 0x10);
                All(x > 0.45 && x < 0.6 && Frac(t * 4) < 0.5 ? Scale(col, 0.15) : col);
                return c;
            }
            case "thunderstorm":
            {
                double slot = Math.Floor(t * 0.8 * s), age = Frac(t * 0.8 * s);
                double flash = Hash((int)slot) > 0.6 ? (age < 0.06 ? 1 : age > 0.1 && age < 0.14 ? 0.7 : 0) : 0;
                for (int z = 0; z < 4; z++)
                {
                    var cloud = Scale(Color.FromRgb(0x20, 0x28, 0x60), 0.25 + Noise(t * 0.6 + z * 3.3) * 0.3);
                    c[z] = Mix(cloud, Colors.White, flash * (Hash((int)slot * 4 + z) > 0.35 ? 1 : 0.55));
                }
                return c;
            }
            case "embers":
            {
                for (int z = 0; z < 4; z++) c[z] = Mix(Color.FromRgb(0x30, 0x02, 0x00), Color.FromRgb(0xFF, 0x60, 0x10), Math.Pow(Noise(t * 1.5 * s + z * 5.3), 3) * 1.4);
                return c;
            }
            case "tide":
            {
                // The tide coming in over the outer halves first, then the inner ones, and going out.
                double level = Wave(t * 0.15 * s, 1);
                var deep = Color.FromRgb(0x00, 0x30, 0xA0);
                var foam = Color.FromRgb(0x80, 0xE8, 0xFF);
                for (int z = 0; z < 4; z++)
                {
                    double v = z is 1 or 2 ? Math.Clamp((level - 0.5) * 2, 0, 1) : Math.Clamp(level * 2, 0, 1);
                    c[z] = Mix(Scale(deep, 0.08), Mix(deep, foam, Noise(t * 2 + z) * 0.6), v);
                }
                return c;
            }
            case "aurorawave":
            {
                Color[] stops = { Color.FromRgb(0x20, 0xFF, 0x8A), Color.FromRgb(0x00, 0xC8, 0xFF), Color.FromRgb(0xA0, 0x30, 0xFF), Color.FromRgb(0x20, 0xFF, 0x8A) };
                for (int z = 0; z < 4; z++)
                    c[z] = Scale(Along(stops, Frac(t * 0.07 * s + z / 4.0 + Noise(t * 0.3 + z) * 0.15)), 0.5 + 0.5 * Wave(t * 0.2 * s + z / 4.0, 1));
                return c;
            }
            case "disco":
            {
                int beat = (int)Math.Floor(t * 2.2 * s);
                for (int z = 0; z < 4; z++) c[z] = Hue(Math.Floor(Hash(beat * 4 + z + 77) * 6) * 60);
                return c;
            }
            case "alternator":
            {
                double cycle = 0.5 / s, x = t % (cycle * 2);
                bool left = x < cycle, on = (x % cycle) < cycle * 0.35;
                for (int z = 0; z < 4; z++) c[z] = Ring(z) == 0 ? (left && on ? i.One : dark) : (!left && on ? i.Two : dark);
                return c;
            }
            case "spectrum":
            {
                // A bar per zone, green when low through amber to red at the peak.
                for (int z = 0; z < 4; z++)
                {
                    double level = 0.25 + 0.75 * Math.Abs(Math.Sin(t * (2.1 + z * 0.7) * s + z)) * (0.6 + 0.4 * Noise(t * 3 + z * 9));
                    var col = level > 0.85 ? Color.FromRgb(0xFF, 0x20, 0x20) : level > 0.6 ? Color.FromRgb(0xFF, 0xB0, 0x00) : Color.FromRgb(0x00, 0xE0, 0x50);
                    c[z] = Scale(col, Math.Clamp(level, 0.05, 1));
                }
                return c;
            }
            case "dna":
            {
                for (int z = 0; z < 4; z++)
                {
                    double ph = t * 1.2 * s + z / 4.0 * Math.PI * 2;
                    c[z] = Add(Scale(i.One, 0.5 + 0.5 * Math.Sin(ph)), Scale(i.Two, 0.5 - 0.5 * Math.Sin(ph)));
                }
                return c;
            }
            case "sunrise":
            {
                // The glow starting between the sticks and spreading out to the outer halves.
                double day = Wave(t * 0.06 * s, 1);
                Color[] stops = { Color.FromRgb(0x06, 0x0A, 0x30), Color.FromRgb(0xFF, 0x50, 0x20), Color.FromRgb(0xFF, 0xC0, 0x60), Color.FromRgb(0xC0, 0xE8, 0xFF) };
                for (int z = 0; z < 4; z++) c[z] = Along(stops, z is 1 or 2 ? Math.Clamp(day / 0.6, 0, 1) : Math.Clamp((day - 0.3) / 0.7, 0, 1));
                return c;
            }
            case "neon":
            {
                for (int z = 0; z < 4; z++)
                {
                    var tube = Ring(z) == 0 ? Color.FromRgb(0xFF, 0x2E, 0xC8) : Color.FromRgb(0x20, 0xF0, 0xFF);
                    double flicker = Hash((int)Math.Floor(t * 12) * 8 + Ring(z)) > 0.94 ? 0.15 : 0.85 + 0.15 * Noise(t * 30 + z);
                    c[z] = Scale(tube, flicker);
                }
                return c;
            }
            case "rgbquad":
            {
                // Red, green, blue and white, one per zone, stepping along.
                Color[] four = { Color.FromRgb(0xFF, 0x10, 0x10), Color.FromRgb(0x10, 0xFF, 0x30), Color.FromRgb(0x10, 0x40, 0xFF), Colors.White };
                int step = (int)Math.Floor(t * 2 * s);
                for (int z = 0; z < 4; z++) c[z] = four[(z + step) % 4];
                return c;
            }
            case "countdown":
            {
                // Four lights going out one by one, right to left, then a flash.
                double cycle = 5 / s, x = (t % cycle) / cycle * 5;
                int gone = (int)Math.Floor(x);
                for (int n = 0; n < 4; n++) c[3 - n] = gone >= 4 ? (Frac(x) < 0.5 ? Colors.White : dark) : n < gone ? Scale(i.One, 0.04) : i.One;
                return c;
            }
            case "wormholes":
            {
                double p = t * 0.9 * s;
                for (int z = 0; z < 4; z++)
                    c[z] = Ring(z) == 0 ? Mix(Color.FromRgb(0x60, 0x18, 0x00), Color.FromRgb(0xFF, 0x8A, 0x10), Wave(Half(z) * 0.5 - p, 1))
                                        : Mix(Color.FromRgb(0x00, 0x18, 0x60), Color.FromRgb(0x20, 0x9C, 0xFF), Wave(Half(z) * 0.5 + p, 1));
                return c;
            }
            case "jellyfish":
            {
                // A soft glow pulsing between the sticks, its tentacles trailing out to the edges.
                double x = t * 0.5 * s;
                var col = Mix(Color.FromRgb(0x30, 0xE0, 0xFF), Color.FromRgb(0xFF, 0x60, 0xD0), Wave(t * 0.08, 1));
                for (int z = 0; z < 4; z++) c[z] = Scale(col, 0.08 + 0.92 * (z is 1 or 2 ? Wave(x, 1) : Wave(x - 0.15, 1) * 0.7));
                return c;
            }
            case "plasma":
            {
                for (int z = 0; z < 4; z++) c[z] = Hue(Noise(t * 0.5 * s + z * 1.6) * 540 + t * 20 * s);
                return c;
            }
            case "sos":
            {
                double unit = 0.18 / s;
                int[] pattern = { 1, 0, 1, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 };
                All(pattern[(int)(t / unit) % pattern.Length] == 1 ? i.One : Scale(i.One, 0.03));
                return c;
            }
            case "carousel":
            {
                double p = t * 0.35 * s;
                for (int z = 0; z < 4; z++) c[z] = Scale(Mix(Hue(z * 90 + p * 360), Colors.White, 0.35), 0.55 + 0.45 * Wave(t * 0.8 * s + z * 0.25, 1));
                return c;
            }
            case "kaleidoscope":
            {
                // The sticks mirror each other: outer halves alike, inner halves alike.
                for (int z = 0; z < 4; z++) c[z] = Hue(Noise(t * 0.6 * s + Math.Min(z, 3 - z) * 2.1) * 360 + t * 30 * s);
                return c;
            }

            // ---- 8.5.63
            case "pendulum":
            {
                // A light swinging from one half of each stick to the other, slowing at the ends; the sticks swing opposite ways.
                double swing = Math.Sin(t * 1.6 * s);
                for (int z = 0; z < 4; z++)
                {
                    double pos = Half(z) == 0 ? -0.7 : 0.7, at = Ring(z) == 0 ? swing : -swing;
                    c[z] = Scale(i.One, Math.Max(0.03, 1 - Math.Abs(pos - at) * 0.85));
                }
                return c;
            }
            case "glitch":
            {
                double slot = Math.Floor(t * 14 * s);
                for (int z = 0; z < 4; z++) { double g = Hash((int)slot * 4 + z + 500); c[z] = g > 0.92 ? Colors.White : g > 0.85 ? i.Two : g > 0.8 ? dark : i.One; }
                return c;
            }
            case "cometrain":
            {
                // Comets passing here and there: a white head, then a blue tail fading.
                var tail = Color.FromRgb(0x50, 0x90, 0xFF);
                for (int z = 0; z < 4; z++)
                {
                    double x = t * 1.8 * s + z * 0.41, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 4 + z) < 0.45 ? Scale(tail, 0.04)
                         : age < 0.15 ? Mix(tail, Colors.White, 1 - age / 0.15) : Scale(tail, Math.Max(0.04, 1 - (age - 0.15) / 0.5));
                }
                return c;
            }
            case "firefly":
            {
                var glow = Color.FromRgb(0xC8, 0xFF, 0x40);
                for (int z = 0; z < 4; z++)
                {
                    double x = t * 0.45 * s + z * 0.73, slot = Math.Floor(x), age = Frac(x);
                    c[z] = Hash((int)slot * 4 + z + 40) > 0.45 ? Scale(glow, Math.Sin(age * Math.PI)) : Color.FromRgb(0x02, 0x08, 0x02);
                }
                return c;
            }
            case "blobs":
            {
                // Lava-lamp blobs drifting from one half of each stick to the other.
                for (int z = 0; z < 4; z++)
                {
                    double at = Wave(t * 0.12 * s + Ring(z) * 0.5, 1);
                    c[z] = Mix(Color.FromRgb(0x60, 0x00, 0x30), Color.FromRgb(0xFF, 0x50, 0x20), Math.Max(0, 1 - Math.Abs(Half(z) - at) * 1.2));
                }
                return c;
            }
            case "duobreath":
            {
                double a = Wave(t * 0.3 * s, 1);
                for (int z = 0; z < 4; z++) c[z] = Ring(z) == 0 ? Scale(i.One, 0.05 + 0.95 * a) : Scale(i.Two, 0.05 + 0.95 * (1 - a));
                return c;
            }
            case "starfield":
            {
                for (int z = 0; z < 4; z++)
                {
                    double tw = Noise(t * (1.2 + Hash(z + 9) * 2) * s + z * 9.1);
                    c[z] = Mix(Color.FromRgb(0x02, 0x04, 0x18), Color.FromRgb(0xE8, 0xF0, 0xFF), Math.Pow(tw, 2.5) * (0.4 + Hash(z + 60) * 0.6));
                }
                return c;
            }
            case "seasons":
            {
                Color[] four = { Color.FromRgb(0x50, 0xE0, 0x40), Color.FromRgb(0xFF, 0xD0, 0x30), Color.FromRgb(0xFF, 0x60, 0x10), Color.FromRgb(0xD8, 0xF0, 0xFF) };
                double p = t * 0.25 * s;
                for (int z = 0; z < 4; z++) c[z] = Along(new[] { four[0], four[1], four[2], four[3], four[0] }, Frac(z / 4.0 + p));
                return c;
            }
            case "candles":
            {
                // A candle on each stick: the outer half its steady glow, the inner half its flickering flame.
                for (int z = 0; z < 4; z++)
                {
                    double n = Noise(t * 8 * s + z * 6.7 + Ring(z) * 40);
                    c[z] = z is 1 or 2 ? Scale(Color.FromRgb(0xFF, 0x90, 0x20), 0.5 + 0.5 * n) : Scale(Color.FromRgb(0xFF, 0x50, 0x08), 0.35 + 0.2 * n);
                }
                return c;
            }
            case "amberbeacon":
            {
                double p = t * 1.8 * s;
                var amber = Color.FromRgb(0xFF, 0x9A, 0x00);
                for (int z = 0; z < 4; z++) c[z] = Scale(amber, Math.Max(0.02, 1 - Loop(z, p) * 0.9));
                return c;
            }
            case "toxic":
            {
                for (int z = 0; z < 4; z++) c[z] = Mix(Color.FromRgb(0x10, 0x40, 0x00), Color.FromRgb(0xB0, 0xFF, 0x20), 0.4 + 0.6 * Noise(t * 2.5 * s + z * 3.9) * Wave(t * 0.4, 1));
                return c;
            }
            case "frost":
            {
                for (int z = 0; z < 4; z++)
                {
                    double glint = Math.Max(0, Noise(t * 2 * s + z * 11.3) - 0.78) / 0.22;
                    c[z] = Mix(Mix(Color.FromRgb(0x30, 0x70, 0xC0), Color.FromRgb(0x90, 0xD8, 0xFF), Noise(t * 0.3 + z)), Colors.White, glint);
                }
                return c;
            }
            case "sakura":
            {
                // Blossom pinks, with a white petal drifting past a zone now and then.
                for (int z = 0; z < 4; z++)
                {
                    var col = Mix(Color.FromRgb(0xFF, 0x70, 0xA8), Color.FromRgb(0xFF, 0xB8, 0xD0), Noise(t * 0.4 + z * 2));
                    double x = t * 0.9 * s + z * 0.53, slot = Math.Floor(x), age = Frac(x);
                    if (Hash((int)slot * 4 + z + 7) >= 0.5) col = Mix(col, Colors.White, Math.Sin(age * Math.PI));
                    c[z] = col;
                }
                return c;
            }
            case "surf":
            {
                // A wave rolling in from the outer halves, breaking white between the sticks, and washing back.
                double cycle = 3 / s, x = (t % cycle) / cycle;
                var sea = Color.FromRgb(0x00, 0x60, 0xD0);
                for (int z = 0; z < 4; z++)
                {
                    bool inner = z is 1 or 2;
                    double rise = inner ? Math.Clamp((x - 0.35) / 0.25, 0, 1) : Math.Clamp(x / 0.35, 0, 1);
                    double crash = inner && x > 0.6 && x < 0.75 ? 1 - Math.Abs(x - 0.67) / 0.08 : 0;
                    double back = x > 0.75 ? 1 - (x - 0.75) / 0.25 : 1;
                    c[z] = Mix(Scale(sea, 0.1 + 0.9 * rise * back), Colors.White, Math.Max(0, crash));
                }
                return c;
            }
            case "heat":
            {
                Color[] stops = { Color.FromRgb(0x00, 0x30, 0xFF), Color.FromRgb(0x00, 0xE0, 0x80), Color.FromRgb(0xFF, 0xE0, 0x00), Color.FromRgb(0xFF, 0x10, 0x10) };
                for (int z = 0; z < 4; z++) c[z] = Along(stops, Noise(t * 0.35 * s + z * 1.9));
                return c;
            }
            case "dice":
            {
                // The zones tumble, then settle on a number: one to four lit, and five or six with one or two in your second color.
                double cycle = 2.6 / s, x = (t % cycle) / cycle;
                int roll = (int)Math.Floor(t / cycle);
                int shown = x < 0.35 ? 1 + (int)(Hash((int)(t * 20)) * 4) % 4 : 1 + (int)(Hash(roll) * 6);
                int[] order = { 1, 2, 0, 3 };
                for (int n = 0; n < 4; n++) c[order[n]] = n < Math.Min(4, shown) ? (n < shown - 4 ? i.Two : i.One) : Scale(i.One, 0.04);
                return c;
            }
            case "spinner":
            {
                double p = t * 1.5 * s;
                for (int z = 0; z < 4; z++)
                {
                    double behind = Frac(p - (Half(z) * 0.5 + 0.125));
                    c[z] = Scale(i.One, behind < 0.35 ? 1 - behind : Math.Max(0.04, 0.4 - behind * 0.5));
                }
                return c;
            }
            case "bricks":
            {
                double cycle = 6 / s, x = (t % cycle) / cycle * 6;
                int gone = Math.Min(4, (int)Math.Floor(x));
                int[] order = { 1, 2, 0, 3 };
                for (int n = 0; n < 4; n++) c[order[n]] = n == gone && Frac(x) < 0.15 ? Colors.White : n < gone ? Scale(i.One, 0.03) : i.One;
                return c;
            }
            case "stacker":
            {
                // Blocks dropping in from the outer halves to the middle, then the row clears.
                double cycle = 4 / s, x = (t % cycle) / cycle * 6;
                int placed = Math.Min(4, (int)Math.Floor(x)), round = (int)Math.Floor(t / cycle);
                int[] order = { 0, 3, 1, 2 };
                bool clear = x >= 5;
                for (int n = 0; n < 4; n++)
                    c[order[n]] = clear ? (Frac(x * 3) < 0.5 ? Colors.White : dark) : n < placed ? Hue(Math.Floor(Hash(round * 4 + n) * 6) * 60) : dark;
                return c;
            }
            case "stereo":
            {
                // Each stick its own meter: the outer half lights first, the inner half as it peaks.
                for (int r = 0; r < 2; r++)
                {
                    double level = Math.Clamp(0.25 + 0.75 * Math.Abs(Math.Sin(t * (2.3 + r * 0.9) * s + r * 2)) * (0.5 + 0.5 * Noise(t * 4 + r * 30)), 0, 1) * 2;
                    int outer = r == 0 ? 0 : 3, inner = r == 0 ? 1 : 2;
                    c[outer] = Scale(Color.FromRgb(0x00, 0xE0, 0x50), Math.Clamp(level, 0.04, 1));
                    c[inner] = Scale(level > 1.75 ? Color.FromRgb(0xFF, 0x20, 0x20) : Color.FromRgb(0xFF, 0xB0, 0x00), Math.Clamp(level - 1, 0.04, 1));
                }
                return c;
            }
            case "chameleon":
            {
                for (int z = 0; z < 4; z++) c[z] = Mix(i.Accent, i.Game, Wave(t * 0.1 * s + z / 4.0, 1));
                return c;
            }
            case "constellation":
            {
                int[][] shapes = { new[] { 0, 3 }, new[] { 1, 2 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 0, 1, 3 }, new[] { 0, 2, 3 } };
                double x = t * 0.4 * s;
                var shape = shapes[(int)Math.Floor(x) % shapes.Length];
                All(Color.FromRgb(0x03, 0x04, 0x12));
                foreach (int z in shape) c[z] = Mix(c[z], Color.FromRgb(0xE0, 0xE8, 0xFF), Math.Sin(Frac(x) * Math.PI));
                return c;
            }
            case "meteor":
            {
                double cycle = 2.5 / s, x = (t % cycle) / cycle * 2;
                All(Color.FromRgb(0x02, 0x02, 0x08));
                if (x < 1)
                {
                    double head = x * 4.8;
                    for (int z = 0; z < 4; z++)
                    {
                        double d = head - z;
                        if (d >= 0 && d < 1.8) c[z] = Scale(Mix(Color.FromRgb(0xFF, 0x80, 0x20), Colors.White, Math.Max(0, 1 - d)), 1 - d / 1.8);
                    }
                }
                return c;
            }
            case "drip":
            {
                // A drop of your color forming on the inner half of each stick, then running to the outer half and fading.
                for (int r = 0; r < 2; r++)
                {
                    double x = Frac(t * 0.7 * s + r * 0.5);
                    int inner = r == 0 ? 1 : 2, outer = r == 0 ? 0 : 3;
                    double vi = x < 0.4 ? x / 0.4 : x < 0.7 ? 1 - (x - 0.4) / 0.3 : 0;
                    double vo = x < 0.4 ? 0 : x < 0.7 ? (x - 0.4) / 0.3 : 1 - (x - 0.7) / 0.3;
                    c[inner] = Scale(i.One, Math.Max(0.05, vi));
                    c[outer] = Scale(i.One, Math.Max(0.05, vo));
                }
                return c;
            }
            case "nebula":
            {
                double p = t * 0.3 * s;
                for (int z = 0; z < 4; z++)
                {
                    var gas = Mix(Color.FromRgb(0x50, 0x10, 0x90), Color.FromRgb(0xFF, 0x40, 0xA0), Wave(Half(z) * 0.5 + (Ring(z) == 0 ? -p : p), 1));
                    c[z] = Mix(gas, Colors.White, Math.Max(0, Noise(t * 2.5 + z * 7.7) - 0.85) / 0.15);
                }
                return c;
            }

            // ---- 8.5.81
            case "tesla":
            {
                All(Color.FromRgb(0x10, 0x04, 0x24));
                double step = 0.11 / s, n0 = t / step;
                int n = (int)Math.Floor(n0);
                if (Hash(n * 3 + 1) > 0.35)
                {
                    int a = (int)(Hash(n * 7 + 2) * 4) % 4, b = (a + 1 + (int)(Hash(n * 11 + 5) * 3)) % 4;
                    var arc = Mix(Color.FromRgb(0xA0, 0x60, 0xFF), Colors.White, Hash(n * 13) * 0.7);
                    double v = 1 - Frac(n0);
                    c[a] = Mix(c[a], arc, v);
                    c[b] = Mix(c[b], arc, v * 0.8);
                }
                return c;
            }
            case "chomper":
            {
                // The chomper swaps halves round each stick, a ghost one step behind.
                double p = t * 0.35 * s;
                Color[] ghosts = { Color.FromRgb(0xFF, 0x40, 0x40), Color.FromRgb(0xFF, 0x9E, 0xE8), Color.FromRgb(0x30, 0xE0, 0xFF), Color.FromRgb(0xFF, 0xA0, 0x30) };
                var ghost = ghosts[(int)Math.Floor(p) % 4];
                double chomp = 0.65 + 0.35 * Math.Abs(Math.Sin(t * 10 * s));
                for (int z = 0; z < 4; z++)
                {
                    double ph = Ring(z) == 0 ? p : p + 0.5;
                    var col = Add(Scale(Colors.White, 0.12), Scale(ghost, Math.Max(0, 1 - D2(Half(z), ph - 0.5))));
                    c[z] = Mix(col, Scale(Color.FromRgb(0xFF, 0xD8, 0x00), chomp), Math.Max(0, 1 - D2(Half(z), ph)));
                }
                return c;
            }
            case "prism":
            {
                double x = Frac(t * 0.4 * s) * 1.6;
                for (int z = 0; z < 4; z++)
                    c[z] = Scale(z < 2 ? Colors.White : Hue(t * 40 * s + (z - 2) * 120), Math.Max(0.06, 1 - Math.Abs(x * 4 - z - 0.5) * 0.8));
                return c;
            }
            case "clockhands":
            {
                var now = DateTime.Now;
                double hour = (now.Hour % 12 + now.Minute / 60.0) / 12, minute = (now.Minute + now.Second / 60.0) / 60;
                for (int z = 0; z < 4; z++)
                    c[z] = Mix(Scale(i.Two, 0.08), i.One, Math.Max(0, 1 - D2(Half(z), (Ring(z) == 0 ? hour : minute) - 0.625)));
                return c;
            }
            case "biolum":
            {
                for (int z = 0; z < 4; z++)
                {
                    double w = Frac(t * 0.25 * s - z * 0.18);
                    double crest = Math.Exp(-Math.Pow((w - 0.5) * 6, 2));
                    double sparkle = Math.Max(0, Noise(t * 3 * s + z * 9.1) - 0.6) / 0.4;
                    var glow = Mix(Color.FromRgb(0x00, 0xC8, 0xB4), Color.FromRgb(0x40, 0xA0, 0xFF), Noise(z * 3.3 + t * 0.5));
                    c[z] = Mix(Color.FromRgb(0x00, 0x04, 0x12), glow, Math.Min(1, crest * (0.7 + 0.6 * sparkle)));
                }
                return c;
            }
            case "signals":
            {
                var amber = Color.FromRgb(0xFF, 0x8A, 0x00);
                double phase = t / (0.66 / s);
                int blink = (int)Math.Floor(phase) % 9;
                bool on = Frac(phase) < 0.55, left = blink < 3 || blink >= 6, right = blink >= 3;
                for (int z = 0; z < 4; z++) c[z] = on && (Ring(z) == 0 ? left : right) ? amber : Color.FromRgb(0x14, 0x08, 0x00);
                return c;
            }
            case "typewriter":
            {
                double n = t / (0.3 / s);
                int pos = (int)Math.Floor(n) % 7;
                for (int z = 0; z < 4; z++)
                    c[z] = pos >= 4 ? (pos == 4 ? Colors.White : Scale(i.One, pos == 5 ? 0.4 : 0.04))
                         : z < pos ? i.One : z == pos ? Mix(i.One, Colors.White, 1 - Frac(n)) : Scale(i.One, 0.04);
                return c;
            }
            case "supernova":
            {
                double cycle = 6 / s, x = (t % cycle) / cycle;
                Color col;
                if (x < 0.5) col = Scale(Mix(Color.FromRgb(0x60, 0x08, 0x00), Color.FromRgb(0xFF, 0x80, 0x20), x * 2), 0.1 + x * (0.8 + 0.2 * Math.Sin(t * 30)));
                else
                {
                    double y = (x - 0.5) * 2;
                    col = y < 0.08 ? Colors.White
                        : Scale(Along(new[] { Colors.White, Color.FromRgb(0x60, 0xA0, 0xFF), Color.FromRgb(0xA0, 0x30, 0xFF), Color.FromRgb(0x20, 0x00, 0x30) }, y), 1 - y * 0.85);
                }
                for (int z = 0; z < 4; z++) c[z] = Scale(col, x >= 0.5 && x < 0.6 && z is 0 or 3 ? 0.7 : 1);
                return c;
            }
            case "goldenhour":
            {
                for (int z = 0; z < 4; z++) c[z] = Scale(Mix(Color.FromRgb(0xFF, 0x9A, 0x20), Color.FromRgb(0xFF, 0x4E, 0x7A), Wave(t * 0.06 * s + z * 0.08, 1)), 0.85 + 0.15 * Wave(t * 0.2 + z * 0.3, 1));
                return c;
            }
            case "volcano":
            {
                // A dark red rumble, then an eruption bursting between the sticks and spreading out.
                double cycle = 5 / s, x = (t % cycle) / cycle;
                for (int z = 0; z < 4; z++)
                {
                    var col = Scale(Color.FromRgb(0xC0, 0x10, 0x00), 0.25 + 0.2 * Noise(t * 8 * s + z * 4.1));
                    if (x > 0.7)
                    {
                        double e = (x - 0.7) / 0.3;
                        bool inner = z is 1 or 2;
                        double h = inner ? Math.Min(1, e * 3) : Math.Max(0, e * 2 - 0.5);
                        double fade = e > 0.6 ? 1 - (e - 0.6) / 0.4 : 1;
                        col = Mix(col, Mix(Color.FromRgb(0xFF, 0x50, 0x00), Color.FromRgb(0xFF, 0xE0, 0x40), inner ? 0.6 : 0.2), Math.Clamp(h, 0, 1) * fade);
                    }
                    c[z] = col;
                }
                return c;
            }
            case "scanner":
            {
                double u = Frac(t * 0.45 * s), pos = (u < 0.5 ? u * 2 : 2 - u * 2) * 3;
                for (int z = 0; z < 4; z++)
                {
                    double d = z - pos;
                    if (u >= 0.5) d = -d;
                    c[z] = Scale(i.One, d > 0.5 ? 0.02 : d > -0.5 ? 1 : Math.Max(0.02, 1 + (d + 0.5) * 0.45));
                }
                return c;
            }
            case "ghost":
            {
                double cycle = 3 / s, n0 = t / cycle, x = Frac(n0);
                int n = (int)Math.Floor(n0);
                All(Color.FromRgb(0x02, 0x03, 0x06));
                int a = (int)(Hash(n * 5 + 3) * 4) % 4, b = a % 2 == 0 ? a + 1 : a - 1; // drifting to the stick's other half
                var pale = Color.FromRgb(0xB8, 0xD8, 0xFF);
                double v = Math.Sin(x * Math.PI);
                c[a] = Mix(c[a], pale, v * (1 - x));
                c[b] = Mix(c[b], pale, v * x);
                return c;
            }
            case "snowfall":
            {
                // White flakes drifting past each zone, slowly, on a night-blue sky.
                var flake = Color.FromRgb(0xE8, 0xF2, 0xFF);
                for (int z = 0; z < 4; z++)
                {
                    double x = Frac(t * 0.22 * s + Hash(z * 17));
                    c[z] = Mix(Color.FromRgb(0x06, 0x0A, 0x18), flake, x < 0.6 ? Math.Sin(x / 0.6 * Math.PI) * 0.9 : 0);
                }
                return c;
            }
            case "tornado":
            {
                double cycle = 8 / s, x = (t % cycle) / cycle;
                double turn = (x / 2 - Math.Sin(2 * Math.PI * x) / (4 * Math.PI)) * 3 * cycle * s + Math.Floor(t / cycle) * 1.5 * cycle * s;
                double speed = Math.Pow(Math.Sin(Math.PI * x), 2), tail = 0.9 - speed * 0.5;
                var col = Mix(Color.FromRgb(0x70, 0x80, 0x90), Color.FromRgb(0xE0, 0xF0, 0xFF), speed);
                for (int z = 0; z < 4; z++) c[z] = Scale(col, Math.Max(0.05, 1 - D2(Half(z), turn + Ring(z) * 0.5) * tail));
                return c;
            }
            case "gears":
            {
                double p = t * 0.25 * s;
                for (int z = 0; z < 4; z++)
                {
                    double a = Ring(z) == 0 ? 0.5 + 0.5 * Math.Cos(2 * Math.PI * (Half(z) * 0.5 - p) * 2)
                                            : 0.5 + 0.5 * Math.Cos(2 * Math.PI * (Half(z) * 0.5 + p) * 2 + Math.PI);
                    c[z] = Ring(z) == 0 ? Mix(Scale(i.One, 0.1), i.One, a) : Mix(Scale(i.Two, 0.1), i.Two, a);
                }
                return c;
            }
            case "rainbowbreath":
            {
                double period = 4 / s, x = t / period, v = Math.Sin(Frac(x) * Math.PI);
                All(Scale(Hue(Math.Floor(x) * 67), 0.04 + 0.96 * v * v));
                return c;
            }
            case "peacock":
            {
                Color[] stops = { Color.FromRgb(0x00, 0x6E, 0x6E), Color.FromRgb(0x10, 0xC0, 0x90), Color.FromRgb(0x20, 0x50, 0xD0), Color.FromRgb(0xE0, 0xB0, 0x30), Color.FromRgb(0x00, 0x6E, 0x6E) };
                for (int z = 0; z < 4; z++) c[z] = Scale(Along(stops, Frac(t * 0.12 * s + Hash(z * 31 + 3) * 0.9)), 0.7 + 0.3 * Math.Sin(t * 3 * s + z * 1.7));
                return c;
            }
            case "moodring":
            {
                double hours = (DateTime.Now - SessionStart).TotalHours;
                Color[] stops = { Color.FromRgb(0x20, 0x60, 0xFF), Color.FromRgb(0x00, 0xC0, 0xB0), Color.FromRgb(0x40, 0xD0, 0x40), Color.FromRgb(0xFF, 0xB0, 0x20), Color.FromRgb(0xFF, 0x40, 0x30), Color.FromRgb(0xC0, 0x20, 0xA0) };
                var col = Along(stops, Math.Clamp(hours / 4.0, 0, 1));
                for (int z = 0; z < 4; z++) c[z] = Scale(col, 0.75 + 0.25 * Wave(t + z * 0.2, 0.12));
                return c;
            }
            case "focustimer":
            {
                double x = t % (30 * 60);
                if (x < 25 * 60)
                {
                    double fill = x / (25 * 60) * 4;
                    for (int z = 0; z < 4; z++) c[z] = z < Math.Floor(fill) ? i.One : z < fill ? Scale(i.One, 0.1 + 0.9 * Frac(fill)) : Scale(i.One, 0.06);
                }
                else All(Scale(Color.FromRgb(0x30, 0xE0, 0x80), 0.3 + 0.7 * Wave(x, 0.25)));
                return c;
            }
            case "holiday":
            {
                Color[] bulbs = { Color.FromRgb(0xFF, 0x10, 0x10), Color.FromRgb(0x10, 0xD0, 0x30), Color.FromRgb(0xFF, 0xC0, 0x20) };
                int n = (int)Math.Floor(t / (0.8 / s));
                for (int z = 0; z < 4; z++) c[z] = Scale(bulbs[(z + n) % 3], 0.55 + 0.45 * Noise(t * 4 * s + z * 5.3));
                return c;
            }
            case "current":
            {
                double x = Frac(t * 0.9 * s) * 5.5 - 0.5;
                var spark = Mix(i.One, Colors.White, 0.35);
                for (int z = 0; z < 4; z++)
                    c[z] = Add(Scale(i.One, 0.06), Scale(spark, Math.Max(0, 1 - Math.Abs(x - z) * 1.2) * (0.85 + 0.15 * Noise(t * 40 + z))));
                return c;
            }
            case "quasar":
            {
                // Two opposite beams turning round each stick: one half bright while the other dims, round and round.
                double p = t * 0.5 * s;
                var beam = Color.FromRgb(0xC8, 0xE0, 0xFF);
                var core = Color.FromRgb(0x28, 0x10, 0x50);
                for (int z = 0; z < 4; z++)
                {
                    double ph = Ring(z) == 0 ? p : -p;
                    c[z] = Mix(core, beam, 0.5 + 0.5 * Math.Cos(4 * Math.PI * (Half(z) * 0.5 + 0.125 - ph)));
                }
                return c;
            }
            case "magnet":
            {
                double x = Frac(t * 0.35 * s), pos = x < 0.5 ? Math.Pow(x * 2, 2) : 1 - (x - 0.5) * 2;
                double flash = Math.Max(0, 1 - Math.Abs(x - 0.5) * 12);
                for (int z = 0; z < 4; z++)
                {
                    bool left = Ring(z) == 0;
                    double target = left ? pos * 1.5 : 3 - pos * 1.5;
                    c[z] = Mix(Scale(left ? i.One : i.Two, Math.Max(0.05, 1 - Math.Abs(z - target) * 0.9)), Colors.White, flash * (z is 1 or 2 ? 1 : 0.3));
                }
                return c;
            }
            case "vinyl":
            {
                double p = t * 0.55 * s;
                var disc = Add(Color.FromRgb(0x10, 0x08, 0x14), Scale(i.One, 0.25));
                for (int z = 0; z < 4; z++) c[z] = Mix(disc, Colors.White, 0.9 * Math.Max(0, 1 - D2(Half(z), p + Ring(z) * 0.5) * 1.2));
                return c;
            }
            case "interactive":
            {
                // Each stick lights the half it's pushed toward (left stick, first color; right stick, second), brighter
                // the further it goes, fading behind as it turns.
                var st = i.Sticks ?? new[] { Math.Cos(t * 2.2), Math.Sin(t * 2.2), Math.Cos(-t * 1.7 + 1), Math.Sin(-t * 1.7 + 1) };
                double[] dirs = { 180, 0 }; // each ring's left half points left, its right half right
                lock (StickTrail4)
                {
                    for (int r = 0; r < 2; r++)
                    {
                        double x = st[r * 2], y = st[r * 2 + 1];
                        double mag = Math.Clamp(Math.Sqrt(x * x + y * y), 0, 1);
                        mag = mag < 0.18 ? 0 : (mag - 0.18) / 0.82;
                        double angle = Math.Atan2(y, x) * 180 / Math.PI;
                        for (int h = 0; h < 2; h++)
                        {
                            double d = Math.Abs(((angle - dirs[h]) % 360 + 540) % 360 - 180);
                            double now = mag * Math.Pow(Math.Max(0, Math.Cos(d * Math.PI / 180)), 1.2);
                            int z = r * 2 + h;
                            StickTrail4[z] = Math.Max(now, StickTrail4[z] * 0.8);
                            c[z] = Scale(r == 0 ? i.One : i.Two, Math.Max(0.06, StickTrail4[z]));
                        }
                    }
                }
                return c;
            }
            case "ufo":
            {
                // Colored lamps going round the outer halves, a green tractor beam pulsing between the sticks.
                double p = t * 0.8 * s, beam = Wave(t * s, 0.5);
                Color[] lamps = { Color.FromRgb(0xFF, 0x30, 0x30), Color.FromRgb(0x30, 0xFF, 0x60), Color.FromRgb(0x30, 0x80, 0xFF), Color.FromRgb(0xFF, 0xE0, 0x30) };
                int step = (int)Math.Floor(p * 4);
                for (int z = 0; z < 4; z++) c[z] = z is 0 or 3 ? lamps[(step + z) % 4] : Scale(Color.FromRgb(0x40, 0xFF, 0x70), 0.1 + 0.9 * beam);
                return c;
            }
        }

        // Anything drawn for eight LEDs without a four-zone version yet: each half ring takes the brighter of its two
        // LEDs (an average halved every moving light).
        var eight = Frame8(mode, t, i);
        return new[] { Brighter(eight[0], eight[1]), Brighter(eight[2], eight[3]), Brighter(eight[4], eight[5]), Brighter(eight[6], eight[7]) };
    }

    /// <summary>The brighter of two colors, with a little of the other mixed in.</summary>
    private static Color Brighter(Color a, Color b)
    {
        int la = a.R * 3 + a.G * 6 + a.B, lb = b.R * 3 + b.G * 6 + b.B;
        return la >= lb ? Mix(a, b, 0.2) : Mix(b, a, 0.2);
    }

    /// <summary>The effects with a four-zone version of their own (all of the eight-LED ones, as of 9.0.12).</summary>
    public static bool HasFourZoneVersion(string mode)
    {
        if (!Native8.Contains(mode)) return true; // drawn for four zones in the first place
        return Frame4Modes.Contains(mode);
    }

    private static readonly HashSet<string> Frame4Modes = new(StringComparer.Ordinal)
    {
        "orbit", "twin", "rainbowring", "radar", "pinwheel", "fillup", "batteryring", "cpuring", "snake", "figure8", "fireworks",
        "rain", "fire", "matrix", "candy", "lightbar", "vortex", "gradientring", "confetti", "pulsering", "yinyang", "eyes",
        "metronome", "dualspin", "lighthouse", "bounce", "pingpong", "heartring", "sonar", "hourglass", "traffic", "thunderstorm",
        "embers", "tide", "aurorawave", "disco", "alternator", "spectrum", "dna", "sunrise", "neon", "rgbquad", "countdown",
        "wormholes", "jellyfish", "plasma", "sos", "carousel", "kaleidoscope", "pendulum", "glitch", "cometrain", "firefly",
        "blobs", "duobreath", "starfield", "seasons", "candles", "amberbeacon", "toxic", "frost", "sakura", "surf", "heat",
        "dice", "spinner", "bricks", "stacker", "stereo", "chameleon", "constellation", "meteor", "drip", "nebula", "tesla",
        "chomper", "prism", "clockhands", "biolum", "signals", "typewriter", "supernova", "goldenhour", "volcano", "scanner",
        "ghost", "snowfall", "tornado", "gears", "rainbowbreath", "peacock", "moodring", "focustimer", "holiday", "current",
        "quasar", "magnet", "vinyl", "interactive", "ufo",
    };
}
