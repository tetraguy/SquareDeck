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
    };

    public static bool IsSoftware(string mode) => Software.Contains(mode);

    /// <summary>The effects that use the first color (and the second).</summary>
    public static bool UsesColor(string mode) => mode is "" or "solid" or "breathing" or "sticks" or "strobe" or "heartbeat" or "comet" or "swap"
        or "gradient" or "wave" or "cycle" or "sparkle" or "ripple";
    public static bool UsesSecondColor(string mode) => mode is "breathing" or "sticks" or "swap" or "gradient" or "wave" or "cycle" or "sparkle";

    /// <summary>The effects with a speed to choose.</summary>
    public static bool UsesSpeed(string mode) => mode is "breathing" or "rainbow" or "wheel" || (IsSoftware(mode) && mode is not ("temperature" or "gradient" or "cpu" or "clock" or "ambilight"));

    /// <summary>How fast: 0.6 slow, 1 medium, 1.6 fast.</summary>
    public static double SpeedFactor(string? speed) => speed switch { "slow" => 0.6, "fast" => 1.6, _ => 1.0 };

    /// <summary>What the lights should show for an effect, at <paramref name="t"/> seconds.</summary>
    public sealed record Inputs(Color One, Color Two, double Speed, Color Accent, Color Game, int Battery, bool Plugged, int? Temperature)
    {
        /// <summary>How busy the processor is, 0 to 100 (read while the CPU meter plays).</summary>
        public double? Cpu { get; init; }
        /// <summary>The screen's colors near each zone (read while Ambilight plays): left outer, left inner, right inner, right outer.</summary>
        public Color[]? Screen { get; init; }
    }

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
        if (mode == "cpu") inputs = inputs with { Cpu = CpuNow() };
        if (mode == "ambilight") inputs = inputs with { Screen = ScreenColors() };
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
