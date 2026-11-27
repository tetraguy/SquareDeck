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
    };

    public static bool IsSoftware(string mode) => Software.Contains(mode);

    /// <summary>The effects that use the first color (and the second).</summary>
    public static bool UsesColor(string mode) => mode is "" or "solid" or "breathing" or "sticks" or "strobe" or "heartbeat" or "comet" or "swap";
    public static bool UsesSecondColor(string mode) => mode is "breathing" or "sticks" or "swap";

    /// <summary>The effects with a speed to choose.</summary>
    public static bool UsesSpeed(string mode) => mode is "breathing" or "rainbow" or "wheel" || (IsSoftware(mode) && mode != "temperature");

    /// <summary>How fast: 0.6 slow, 1 medium, 1.6 fast.</summary>
    public static double SpeedFactor(string? speed) => speed switch { "slow" => 0.6, "fast" => 1.6, _ => 1.0 };

    /// <summary>What the lights should show for an effect, at <paramref name="t"/> seconds.</summary>
    public sealed record Inputs(Color One, Color Two, double Speed, Color Accent, Color Game, int Battery, bool Plugged, int? Temperature);

    /// <summary>The four zones' colors: left outer, left inner, right inner, right outer.</summary>
    public static Color[] Frame(string mode, double t, Inputs i)
    {
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
            case "temperature":
            {
                int temp = i.Temperature ?? 55;
                var c = TemperatureColor(temp);
                return Same(temp >= 88 ? Mix(Colors.Black, c, Wave(t, 1.2)) : c); // flashes when it's very hot
            }
        }
        return Same(i.One);
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
            int period = mode is "ocean" or "aurora" or "sunset" or "temperature" ? 200 : 90; // slow drifts need fewer frames
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
        try
        {
            var frame = Frame(mode, t, inputs).Select(c => Scale(c, percent / 100.0)).ToArray();
            if (_last != null && frame.SequenceEqual(_last)) return; // nothing changed: nothing sent
            _last = frame;
            if (frame.All(c => c == frame[0])) AllyHid.SetLive(frame[0]);
            else AllyHid.SetZonesLive(frame);
        }
        catch (Exception) { }
    }
}
