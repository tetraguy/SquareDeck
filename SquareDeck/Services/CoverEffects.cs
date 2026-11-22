using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SquareDeck.Services;

/// <summary>
/// Experimental "Cover art effect": redraws Home covers in another look (pixel art, comic, blueprint, VHS…).
/// Covers are processed off the UI thread, a few at a time, and kept until the effect or its options change;
/// until a cover is ready it shows as it was, so nothing waits on it.
/// </summary>
public static class CoverEffects
{
    /// <summary>Every effect, in the order the picker shows them.</summary>
    public static readonly (string Id, string Name, string About)[] All =
    {
        ("retro", "Retro", "Pixelated, with fewer colors, like an old console game"),
        ("gameboy", "Pocket", "Four shades of green with dithering, like a 1989 handheld's screen"),
        ("bricks", "Bricks", "Rebuilt from toy bricks, studs and all, in a toy-brick color set"),
        ("terminal", "Terminal", "Drawn in green text characters on a glowing old computer screen"),
        ("comic", "Comic", "Bold ink outlines, flat colors and printed dots, like a comic book panel"),
        ("newspaper", "Newspaper", "Black halftone dots on off-white paper, like a photo in a newspaper"),
        ("sketch", "Pencil sketch", "Drawn in pencil on paper, with shading in the dark parts"),
        ("blueprint", "Blueprint", "White outlines on a blue engineering grid"),
        ("neon", "Neon", "Outlines glowing cyan to pink on a dark background"),
        ("duotone", "Duotone", "Two-color print in the colors you choose"),
        ("thermal", "Thermal camera", "Seen through a heat camera, from cold blue to white hot"),
        ("nightvision", "Night vision", "Green, grainy and round, like night-vision goggles"),
        ("vhs", "VHS", "A worn videotape: color fringes, scanlines, noise and a tracking glitch"),
        ("glitch", "Glitch", "Broken data: shifted slices, split colors and sorted pixels"),
        ("oil", "Oil painting", "Soft brush strokes on canvas"),
        ("stainedglass", "Stained glass", "Pieces of colored glass held together with lead"),
        ("oldphoto", "Old photo", "Sepia, faded, scratched and dusty"),
        ("instant", "Instant photo", "In a white instant-photo frame, warm and faded, with a light leak"),
        ("shuffle", "Shuffle", "Every cover gets a different effect"),
    };

    /// <summary>Color pairs for Duotone (shadow, middle, highlight); "accent" uses the highlight color.</summary>
    public static readonly (string Id, string Name, string[] Colors)[] DuotonePalettes =
    {
        ("accent", "Highlight color", Array.Empty<string>()),
        ("sunset", "Sunset", new[] { "#2D0B59", "#E0457B", "#FFD07A" }),
        ("ocean", "Ocean", new[] { "#071A33", "#1F78B4", "#7FFFD4" }),
        ("forest", "Forest", new[] { "#0D1F12", "#3E7B3A", "#E8F5C8" }),
        ("candy", "Candy", new[] { "#3A0CA3", "#F72585", "#FFF3B0" }),
        ("mono", "Black and white", new[] { "#000000", "#808080", "#FFFFFF" }),
    };

    /// <summary>The effect in use ("" for none). Set by the Labs page; changing it redraws the covers.</summary>
    public static string Kind { get; set; } = "";

    /// <summary>The Detail option (Chunky 32 / Classic 48 / Fine 72) for the pixel and grid effects.</summary>
    public static int Detail { get; set; } = 48;

    /// <summary>The Duotone palette's id.</summary>
    public static string Duotone { get; set; } = "accent";

    /// <summary>Effects drawn as hard pixels, which should be scaled up without smoothing.</summary>
    public static bool IsPixelArt(string kind) => kind is "retro" or "gameboy";

    public static string NameOf(string kind) => All.FirstOrDefault(e => e.Id == kind).Name ?? "Retro";

    private sealed class Entry
    {
        public string Key = "";
        public ImageSource? Result, Previous;
        public bool Pending;
        public readonly List<Action> Waiters = new();
    }

    private static readonly ConditionalWeakTable<ImageSource, Entry> Cache = new();
    private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    /// <summary>Forgets every processed cover (after the effect or its options change).</summary>
    public static void Reset() => Cache.Clear();

    private static string Signature => $"{Kind}|{Detail}|{Duotone}|{AccentHex()}";

    private static string AccentHex() => Application.Current?.Resources["AccentBrush"] is SolidColorBrush b ? b.Color.ToString() : "";

    /// <summary>
    /// The cover with the current effect: the finished picture if it's ready, otherwise the last one shown (or the
    /// plain cover) while it's made; <paramref name="ready"/> is called on the UI thread when it's done.
    /// </summary>
    public static ImageSource Apply(ImageSource source, Action ready)
    {
        if (Kind.Length == 0 || source is not BitmapSource { IsFrozen: true } bitmap || bitmap.PixelWidth < 32) return source;
        string key = Signature;
        var entry = Cache.GetValue(source, _ => new Entry());
        lock (entry)
        {
            if (entry.Key == key && entry.Result != null) return entry.Result;
            entry.Waiters.Add(ready);
            if (entry.Key == key && entry.Pending) return entry.Previous ?? source;
            entry.Previous = entry.Result ?? entry.Previous;
            entry.Key = key;
            entry.Result = null;
            entry.Pending = true;
        }
        string kind = Kind == "shuffle" ? Shuffled(source) : Kind;
        var options = new Options(Detail, DuotoneColors());
        if (kind == "terminal") EnsureGlyphs();
        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(async () =>
        {
            BitmapSource result;
            await Gate.WaitAsync().ConfigureAwait(false);
            try { result = Render(bitmap, kind, options); }
            catch { result = bitmap; }
            finally { Gate.Release(); }
            if (!result.IsFrozen) result.Freeze();
            List<Action> waiting;
            lock (entry)
            {
                if (entry.Key != key) return;
                entry.Result = result;
                entry.Previous = null;
                entry.Pending = false;
                waiting = entry.Waiters.ToList();
                entry.Waiters.Clear();
            }
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => { foreach (var w in waiting) w(); });
        });
        return entry.Previous ?? source;
    }

    /// <summary>Makes one picture with an effect, right away (for the settings page's preview).</summary>
    public static BitmapSource RenderNow(BitmapSource source, string kind)
    {
        if (kind == "terminal") EnsureGlyphs();
        var result = Render(source, kind == "shuffle" ? "comic" : kind, new Options(Detail, DuotoneColors()));
        if (!result.IsFrozen) result.Freeze();
        return result;
    }

    private static string Shuffled(ImageSource source)
    {
        var choices = All.Where(e => e.Id != "shuffle").ToArray();
        return choices[(RuntimeHelpers.GetHashCode(source) & 0x7FFFFFFF) % choices.Length].Id;
    }

    private sealed record Options(int Detail, Color[] Duotone);

    private static Color[] DuotoneColors()
    {
        var palette = DuotonePalettes.FirstOrDefault(p => p.Id == Duotone);
        if (palette.Colors is { Length: 3 })
            return palette.Colors.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();
        var accent = (Application.Current?.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0xE0, 0xD0);
        return new[] { Mix(Colors.Black, accent, 0.14), accent, Mix(accent, Colors.White, 0.7) };
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static BitmapSource Render(BitmapSource source, string kind, Options o) => kind switch
    {
        "retro" => Retro(source, o.Detail),
        "gameboy" => Pocket(source, o.Detail),
        "bricks" => Bricks(source, o.Detail),
        "terminal" => Terminal(source, o.Detail),
        "comic" => Comic(source),
        "newspaper" => Newspaper(source),
        "sketch" => Sketch(source),
        "blueprint" => Blueprint(source),
        "neon" => Neon(source),
        "duotone" => Gradient(source, o.Duotone),
        "thermal" => Gradient(source, new[] { "#0A0028", "#3C008C", "#C8008C", "#FF3C00", "#FFC800", "#FFFFDC" }.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray(), blur: true),
        "nightvision" => NightVision(source),
        "vhs" => Vhs(source),
        "glitch" => Glitch(source),
        "oil" => Oil(source),
        "stainedglass" => StainedGlass(source),
        "oldphoto" => OldPhoto(source),
        "instant" => Instant(source),
        _ => source,
    };

    // ---------------------------------------------------------------- pixels

    /// <summary>A picture as BGRA bytes.</summary>
    private sealed class Px
    {
        public readonly int W, H;
        public readonly byte[] D;
        public Px(int w, int h) { W = w; H = h; D = new byte[w * h * 4]; }

        public void Set(int x, int y, double r, double g, double b)
        {
            int i = (y * W + x) * 4;
            D[i] = B(b); D[i + 1] = B(g); D[i + 2] = B(r); D[i + 3] = 255;
        }

        public void Set(int x, int y, Color c) => Set(x, y, c.R, c.G, c.B);

        public (double R, double G, double B) Get(int x, int y)
        {
            x = Math.Clamp(x, 0, W - 1); y = Math.Clamp(y, 0, H - 1);
            int i = (y * W + x) * 4;
            return (D[i + 2], D[i + 1], D[i]);
        }

        public BitmapSource ToBitmap()
        {
            var b = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, D, W * 4);
            b.Freeze();
            return b;
        }
    }

    private static byte B(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5);

    /// <summary>The picture scaled so its longer side is <paramref name="size"/> pixels (never larger than it is).</summary>
    private static Px Load(BitmapSource source, int size) => Load(source, size, size, keepAspect: true);

    private static Px Load(BitmapSource source, int width, int height, bool keepAspect)
    {
        double sx, sy;
        if (keepAspect)
        {
            double s = Math.Min(1.0, width / (double)Math.Max(source.PixelWidth, source.PixelHeight));
            sx = sy = s;
        }
        else { sx = width / (double)source.PixelWidth; sy = height / (double)source.PixelHeight; }
        BitmapSource scaled = Math.Abs(sx - 1) < 0.001 && Math.Abs(sy - 1) < 0.001 ? source : new TransformedBitmap(source, new ScaleTransform(sx, sy));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var px = new Px(converted.PixelWidth, converted.PixelHeight);
        converted.CopyPixels(px.D, px.W * 4, 0);
        return px;
    }

    private static float[] Luma(Px p)
    {
        var l = new float[p.W * p.H];
        for (int i = 0, j = 0; i < l.Length; i++, j += 4) l[i] = 0.114f * p.D[j] + 0.587f * p.D[j + 1] + 0.299f * p.D[j + 2];
        return l;
    }

    /// <summary>Stretches brightness so the darkest 1% is black and the brightest 1% white.</summary>
    private static void Stretch(float[] l)
    {
        var sorted = (float[])l.Clone();
        Array.Sort(sorted);
        float lo = sorted[sorted.Length / 100], hi = sorted[sorted.Length - 1 - sorted.Length / 100];
        if (hi - lo < 8) return;
        for (int i = 0; i < l.Length; i++) l[i] = Math.Clamp((l[i] - lo) * 255f / (hi - lo), 0, 255);
    }

    /// <summary>Box blur, three passes (close to a Gaussian).</summary>
    private static float[] Blur(float[] a, int w, int h, int r, int passes = 3)
    {
        var cur = (float[])a.Clone();
        var tmp = new float[a.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < h; y++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += cur[y * w + Math.Clamp(k, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = sum / (2 * r + 1);
                    sum += cur[y * w + Math.Min(x + r + 1, w - 1)] - cur[y * w + Math.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    cur[y * w + x] = sum / (2 * r + 1);
                    sum += tmp[Math.Min(y + r + 1, h - 1) * w + x] - tmp[Math.Max(y - r, 0) * w + x];
                }
            }
        }
        return cur;
    }

    /// <summary>Edge strength (Sobel).</summary>
    private static float[] Edges(float[] l, int w, int h)
    {
        var e = new float[l.Length];
        float L(int x, int y) => l[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float gx = L(x + 1, y - 1) + 2 * L(x + 1, y) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2 * L(x - 1, y) - L(x - 1, y + 1);
                float gy = L(x - 1, y + 1) + 2 * L(x, y + 1) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2 * L(x, y - 1) - L(x + 1, y - 1);
                e[y * w + x] = MathF.Sqrt(gx * gx + gy * gy) / 4f;
            }
        return e;
    }

    /// <summary>Repeatable noise in 0..1 for a pixel.</summary>
    private static double Noise(int x, int y, int seed = 0)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    private static double Smooth(double a, double b, double v)
    {
        double t = Math.Clamp((v - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static (double R, double G, double B) Saturate((double R, double G, double B) c, double amount)
    {
        double l = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return (l + (c.R - l) * amount, l + (c.G - l) * amount, l + (c.B - l) * amount);
    }

    /// <summary>Scales a pixel-art picture up without smoothing, so it stays crisp however it's drawn.</summary>
    private static Px Enlarge(Px p, int factor)
    {
        var o = new Px(p.W * factor, p.H * factor);
        for (int y = 0; y < o.H; y++)
            Buffer.BlockCopy(Row(p, y / factor, factor), 0, o.D, y * o.W * 4, o.W * 4);
        return o;
    }

    private static byte[] Row(Px p, int y, int factor)
    {
        var row = new byte[p.W * factor * 4];
        for (int x = 0; x < p.W * factor; x++) Buffer.BlockCopy(p.D, (y * p.W + x / factor) * 4, row, x * 4, 4);
        return row;
    }

    // ---------------------------------------------------------------- the effects

    private static BitmapSource Retro(BitmapSource s, int detail)
    {
        var p = Load(s, detail);
        for (int i = 0; i < p.D.Length; i += 4)
            for (int c = 0; c < 3; c++) p.D[i + c] = (byte)(Math.Round(p.D[i + c] / 51.0) * 51);
        return Enlarge(p, Math.Max(1, 256 / Math.Max(p.W, p.H))).ToBitmap();
    }

    private static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    private static BitmapSource Pocket(BitmapSource s, int detail)
    {
        var shades = new[] { Color.FromRgb(0x0F, 0x38, 0x0F), Color.FromRgb(0x30, 0x62, 0x30), Color.FromRgb(0x8B, 0xAC, 0x0F), Color.FromRgb(0x9B, 0xBC, 0x0F) };
        var p = Load(s, detail * 2);
        var l = Luma(p);
        Stretch(l);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double t = (Bayer[y & 3, x & 3] + 0.5) / 16.0;
                int level = Math.Clamp((int)Math.Floor(l[y * p.W + x] / 255.0 * 3 + t), 0, 3);
                p.Set(x, y, shades[level]);
            }
        return Enlarge(p, Math.Max(1, 320 / Math.Max(p.W, p.H))).ToBitmap();
    }

    private static readonly Color[] BrickColors = new[]
    {
        "#F4F4F4", "#A0A5A9", "#6C6E68", "#1B2A34", "#C91A09", "#720E0F", "#FE8A18", "#F2CD37", "#E4CD9E", "#958A73", "#583927",
        "#BBE90B", "#237841", "#184632", "#9FC3E9", "#5A93DB", "#0055BF", "#0A3463", "#6074A1", "#923978", "#FC97AC", "#AC78BA", "#36AEBF",
    }.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();

    private static BitmapSource Bricks(BitmapSource s, int detail)
    {
        int across = detail switch { <= 32 => 16, >= 72 => 32, _ => 24 };
        double aspect = s.PixelHeight / (double)s.PixelWidth;
        int cw = aspect <= 1 ? across : Math.Max(4, (int)Math.Round(across / aspect));
        int ch = aspect <= 1 ? Math.Max(4, (int)Math.Round(across * aspect)) : across;
        var small = Load(s, cw, ch, keepAspect: false);
        var colors = new Color[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                var (r, g, b) = small.Get(x, y);
                colors[y * cw + x] = BrickColors.MinBy(c => 2 * (c.R - r) * (c.R - r) + 4 * (c.G - g) * (c.G - g) + 3 * (c.B - b) * (c.B - b));
            }
        const int cell = 16;
        var o = new Px(cw * cell, ch * cell);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                var c = colors[y / cell * cw + x / cell];
                double lx = x % cell + 0.5, ly = y % cell + 0.5, f = 1.0;
                if (lx < 1.2 || ly < 1.2) f = 1.2;
                else if (lx > cell - 1.2 || ly > cell - 1.2) f = 0.68;
                double shadow = Math.Sqrt((lx - 9.2) * (lx - 9.2) + (ly - 9.2) * (ly - 9.2));
                f *= 1 - 0.2 * Math.Clamp(5.4 - shadow, 0, 1);
                double d = Math.Sqrt((lx - 7.6) * (lx - 7.6) + (ly - 7.6) * (ly - 7.6));
                double stud = Math.Clamp(5.0 - d, 0, 1);
                double shine = 1.06 + 0.22 * Math.Clamp(((7.6 - lx) + (7.6 - ly)) / 9.0, -1, 1);
                if (Math.Abs(d - 4.6) < 0.7) shine *= 0.9;
                f = f + (shine - f) * stud;
                o.Set(x, y, c.R * f + (f > 1 ? (f - 1) * 60 : 0), c.G * f + (f > 1 ? (f - 1) * 60 : 0), c.B * f + (f > 1 ? (f - 1) * 60 : 0));
            }
        return o.ToBitmap();
    }

    private const string Ramp = " .:-=+*#%@";
    private const int GlyphW = 8, GlyphH = 14;
    private static byte[][]? _glyphs;

    /// <summary>Renders the text characters once, on the UI thread, as small brightness masks.</summary>
    private static void EnsureGlyphs()
    {
        if (_glyphs != null) return;
        var typeface = new Typeface("Consolas");
        var masks = new byte[Ramp.Length][];
        for (int i = 0; i < Ramp.Length; i++)
        {
            const int scale = 4;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, GlyphW * scale, GlyphH * scale));
                var text = new FormattedText(Ramp[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
                    GlyphH * scale * 0.95, Brushes.White, 1.0);
                dc.DrawText(text, new Point((GlyphW * scale - text.Width) / 2, (GlyphH * scale - text.Height) / 2));
            }
            var rtb = new RenderTargetBitmap(GlyphW * scale, GlyphH * scale, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var small = new FormatConvertedBitmap(new TransformedBitmap(rtb, new ScaleTransform(1.0 / scale, 1.0 / scale)), PixelFormats.Bgra32, null, 0);
            var buffer = new byte[GlyphW * GlyphH * 4];
            small.CopyPixels(buffer, GlyphW * 4, 0);
            masks[i] = Enumerable.Range(0, GlyphW * GlyphH).Select(k => buffer[k * 4 + 1]).ToArray();
        }
        _glyphs = masks;
    }

    private static BitmapSource Terminal(BitmapSource s, int detail)
    {
        var glyphs = _glyphs!;
        int cols = detail switch { <= 32 => 36, >= 72 => 64, _ => 48 };
        int rows = Math.Max(8, (int)Math.Round(cols * (s.PixelHeight / (double)s.PixelWidth) * GlyphW / GlyphH));
        var small = Load(s, cols, rows, keepAspect: false);
        var l = Luma(small);
        Stretch(l);
        var o = new Px(cols * GlyphW, rows * GlyphH);
        var glow = new float[o.W * o.H];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var mask = glyphs[Math.Clamp((int)(l[r * cols + c] / 256f * Ramp.Length), 0, Ramp.Length - 1)];
                for (int y = 0; y < GlyphH; y++)
                    for (int x = 0; x < GlyphW; x++) glow[(r * GlyphH + y) * o.W + c * GlyphW + x] = mask[y * GlyphW + x];
            }
        var halo = Blur(glow, o.W, o.H, 2, 2);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                double v = Math.Min(1.0, glow[y * o.W + x] / 255.0 + halo[y * o.W + x] / 255.0 * 0.6) * (y % 2 == 1 ? 0.78 : 1.0);
                double vignette = 1 - 0.35 * Smooth(0.5, 1.0, Math.Sqrt(Math.Pow(x / (double)o.W - 0.5, 2) + Math.Pow(y / (double)o.H - 0.5, 2)) * 1.6);
                o.Set(x, y, (6 + 45 * v) * vignette, (16 + 239 * v) * vignette, (10 + 95 * v) * vignette);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Comic(BitmapSource s)
    {
        var p = Load(s, 300);
        var l = Luma(p);
        var e = Edges(Blur(l, p.W, p.H, 1, 2), p.W, p.H);
        const double spacing = 5.0;
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                var c = Saturate(p.Get(x, y), 1.5);
                double Post(double v) => Math.Round(Math.Clamp(v, 0, 255) / 85.0) * 85;
                double r = Post(c.R), g = Post(c.G), b = Post(c.B);
                // Printed dots: rotated 45°, bigger where the picture is darker; paper shows between them.
                double u = (x + y) / Math.Sqrt(2) / spacing, v = (x - y) / Math.Sqrt(2) / spacing;
                double du = u - Math.Round(u), dv = v - Math.Round(v);
                double dist = Math.Sqrt(du * du + dv * dv) * spacing;
                double radius = 0.6 + 2.6 * (1 - l[y * p.W + x] / 255.0);
                double paper = Math.Clamp(dist - radius + 0.5, 0, 1) * 0.4;
                r += (255 - r) * paper; g += (255 - g) * paper; b += (255 - b) * paper;
                double ink = Smooth(28, 55, e[y * p.W + x]);
                p.Set(x, y, r * (1 - ink) + 12 * ink, g * (1 - ink) + 10 * ink, b * (1 - ink) + 16 * ink);
            }
        return p.ToBitmap();
    }

    private static BitmapSource Newspaper(BitmapSource s)
    {
        var p = Load(s, 300);
        var l = Blur(Luma(p), p.W, p.H, 1, 1);
        Stretch(l);
        const double spacing = 6.0;
        double rt = Math.Sqrt(0.5);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double u = (x + y) * rt / spacing, v = (x - y) * rt / spacing;
                double cu = Math.Round(u), cv = Math.Round(v);
                // The dot's centre back in picture space, to read how dark the picture is there.
                int sx = (int)Math.Round((cu + cv) * spacing * rt), sy = (int)Math.Round((cu - cv) * spacing * rt);
                double dark = 1 - l[Math.Clamp(sy, 0, p.H - 1) * p.W + Math.Clamp(sx, 0, p.W - 1)] / 255.0;
                double radius = spacing * 0.62 * Math.Sqrt(dark);
                double dist = Math.Sqrt(Math.Pow(u - cu, 2) + Math.Pow(v - cv, 2)) * spacing;
                double ink = Math.Clamp(radius - dist + 0.5, 0, 1);
                double grain = (Noise(x, y) - 0.5) * 10;
                o.Set(x, y, 238 + grain - 208 * ink, 232 + grain - 204 * ink, 214 + grain - 184 * ink);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Sketch(BitmapSource s)
    {
        var p = Load(s, 320);
        var g = Luma(p);
        Stretch(g);
        var inverted = g.Select(v => 255 - v).ToArray();
        var soft = Blur(inverted, p.W, p.H, 4);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                double dodge = Math.Min(255, g[i] * 255.0 / (256 - soft[i]));
                double v = 255 * Math.Pow(dodge / 255, 2.4);
                if (g[i] < 110 && (x + y) % 5 == 0) v -= (110 - g[i]) * 0.5;      // hatching in the shadows
                if (g[i] < 55 && (x - y + 1000) % 5 == 0) v -= (55 - g[i]) * 0.8;  // and cross-hatching in the darkest
                double grain = (Noise(x, y, 3) - 0.5) * 12;
                v = Math.Clamp(v + grain, 0, 255) / 255;
                p.Set(x, y, 248 * v, 243 * v, 230 * v);
            }
        return p.ToBitmap();
    }

    private static BitmapSource Blueprint(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        var e = Edges(Blur(l, p.W, p.H, 1, 2), p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double r = 20, g = 62, b = 128;
                if (x % 32 == 0 || y % 32 == 0) { r += 34; g += 44; b += 52; }
                else if (x % 8 == 0 || y % 8 == 0) { r += 10; g += 14; b += 18; }
                double ghost = l[y * p.W + x] / 255.0 * 14;
                double line = Smooth(14, 40, e[y * p.W + x]) * 0.95;
                p.Set(x, y, r + ghost + (236 - r) * line, g + ghost + (244 - g) * line, b + ghost + (255 - b) * line);
            }
        return p.ToBitmap();
    }

    private static BitmapSource Neon(BitmapSource s)
    {
        var p = Load(s, 300);
        var l = Luma(p);
        var e = Edges(Blur(l, p.W, p.H, 1, 2), p.W, p.H).Select(v => (float)Smooth(12, 38, v)).ToArray();
        var glow = Blur(e, p.W, p.H, 3, 2);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                double t = (x / (double)p.W + y / (double)p.H) / 2;
                double cr = 0 + 255 * t, cg = 240 - 200 * t, cb = 255 - 55 * t;
                var (r, g, b) = p.Get(x, y);
                double gl = Math.Min(1, glow[i] * 2.2), core = e[i];
                o.Set(x, y, 12 + r * 0.1 + cr * gl * 0.8 + (cr * 0.6 + 100) * core,
                            6 + g * 0.1 + cg * gl * 0.8 + (cg * 0.6 + 100) * core,
                            24 + b * 0.12 + cb * gl * 0.8 + (cb * 0.6 + 100) * core);
            }
        return o.ToBitmap();
    }

    /// <summary>Brightness mapped onto a run of colors (Duotone, Thermal camera).</summary>
    private static BitmapSource Gradient(BitmapSource s, Color[] stops, bool blur = false)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        if (blur) l = Blur(l, p.W, p.H, 1, 2);
        Stretch(l);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double t = l[y * p.W + x] / 255.0 * (stops.Length - 1);
                int k = Math.Min((int)t, stops.Length - 2);
                double f = t - k;
                f = f * f * (3 - 2 * f);
                var a = stops[k]; var b = stops[k + 1];
                p.Set(x, y, a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f);
            }
        return p.ToBitmap();
    }

    private static BitmapSource NightVision(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        Stretch(l);
        var bright = Blur(l.Select(v => v > 180 ? v - 180 : 0).ToArray(), p.W, p.H, 4, 2);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                double v = 255 * Math.Pow(l[i] / 255.0, 0.75) + bright[i] * 1.5 + (Noise(x, y, 7) - 0.5) * 50;
                v *= y % 2 == 0 ? 1.0 : 0.85;
                double d = Math.Sqrt(Math.Pow((x - p.W / 2.0) / (p.W / 2.0), 2) + Math.Pow((y - p.H / 2.0) / (p.H / 2.0), 2));
                v *= 1 - Smooth(0.72, 1.0, d);
                p.Set(x, y, v * 0.32, v * 1.05, v * 0.38);
            }
        return p.ToBitmap();
    }

    private static BitmapSource Vhs(BitmapSource s)
    {
        var p = Load(s, 320);
        var o = new Px(p.W, p.H);
        int bandTop = (int)(p.H * 0.8), bandBottom = (int)(p.H * 0.85);
        for (int y = 0; y < p.H; y++)
        {
            bool band = y >= bandTop && y < bandBottom;
            int shift = band ? 8 + (int)(Noise(0, y, 5) * 10) : (int)Math.Round(Math.Sin(y * 0.07) * 0.8);
            for (int x = 0; x < p.W; x++)
            {
                int sx = x - shift;
                double r = (p.Get(sx + 3, y).R + p.Get(sx + 4, y).R) / 2;
                double g = (p.Get(sx - 1, y).G + p.Get(sx, y).G + p.Get(sx + 1, y).G) / 3;
                double b = (p.Get(sx - 3, y).B + p.Get(sx - 4, y).B) / 2;
                (r, g, b) = Saturate((r, g, b), 0.85);
                double lift = 14, scan = y % 2 == 1 ? 0.8 : 1.0, noise = (Noise(x, y, 11) - 0.5) * 22;
                if (band && Noise(x, y, 13) > 0.82) noise += 120;
                o.Set(x, y, (r * 0.92 + lift) * scan + noise, (g * 0.92 + lift) * scan + noise, (b * 0.92 + lift + 4) * scan + noise);
            }
        }
        return o.ToBitmap();
    }

    private static BitmapSource Glitch(BitmapSource s)
    {
        var p = Load(s, 320);
        var rng = new Random(p.W * 7919 + p.H * 31 + p.D[p.D.Length / 2] * 131 + p.D[p.D.Length / 3]);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
                o.Set(x, y, p.Get(x + 4, y).R, p.Get(x, y).G, p.Get(x - 4, y).B);
        // Slices of the picture shifted sideways, some with their colors swapped.
        for (int n = rng.Next(7, 12); n > 0; n--)
        {
            int top = rng.Next(p.H), height = rng.Next(2, 24), offset = rng.Next(-36, 37);
            bool swap = rng.Next(4) == 0;
            for (int y = top; y < Math.Min(p.H, top + height); y++)
            {
                var row = new byte[p.W * 4];
                Buffer.BlockCopy(o.D, y * p.W * 4, row, 0, row.Length);
                for (int x = 0; x < p.W; x++)
                {
                    int from = ((x - offset) % p.W + p.W) % p.W * 4, to = (y * p.W + x) * 4;
                    o.D[to] = row[from + (swap ? 2 : 0)]; o.D[to + 1] = row[from + 1]; o.D[to + 2] = row[from + (swap ? 0 : 2)];
                }
            }
        }
        // A band of rows with their bright pixels sorted.
        int sortTop = rng.Next(p.H / 4, p.H * 3 / 4), sortHeight = rng.Next(10, 30);
        for (int y = sortTop; y < Math.Min(p.H, sortTop + sortHeight); y++)
        {
            int x = 0;
            while (x < p.W)
            {
                while (x < p.W && Lum(o, x, y) < 90) x++;
                int start = x;
                while (x < p.W && Lum(o, x, y) >= 90) x++;
                if (x - start < 3) continue;
                var run = Enumerable.Range(start, x - start).Select(k => o.Get(k, y)).OrderBy(c => c.R * 0.3 + c.G * 0.59 + c.B * 0.11).ToArray();
                for (int k = 0; k < run.Length; k++) o.Set(start + k, y, run[k].R, run[k].G, run[k].B);
            }
        }
        // Blocks of flat color, like a broken video frame.
        for (int n = rng.Next(2, 5); n > 0; n--)
        {
            int bw = rng.Next(8, 28), bh = rng.Next(6, 18), bx = rng.Next(p.W - bw), by = rng.Next(p.H - bh);
            var c = o.Get(rng.Next(p.W), rng.Next(p.H));
            for (int y = by; y < by + bh; y++) for (int x = bx; x < bx + bw; x++) o.Set(x, y, c.R, c.G, c.B);
        }
        return o.ToBitmap();
    }

    private static double Lum(Px p, int x, int y) { var c = p.Get(x, y); return c.R * 0.3 + c.G * 0.59 + c.B * 0.11; }

    private static BitmapSource Oil(BitmapSource s)
    {
        var p = Load(s, 220);
        var o = new Px(p.W, p.H);
        const int r = 3;
        Span<double> mean = stackalloc double[12];
        Span<double> variance = stackalloc double[4];
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                // Kuwahara: of the four squares around the pixel, use the color of the calmest one.
                for (int q = 0; q < 4; q++)
                {
                    int x0 = q % 2 == 0 ? x - r : x, y0 = q < 2 ? y - r : y;
                    double sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0;
                    for (int yy = y0; yy <= y0 + r; yy++)
                        for (int xx = x0; xx <= x0 + r; xx++)
                        {
                            var c = p.Get(xx, yy);
                            double lum = c.R * 0.3 + c.G * 0.59 + c.B * 0.11;
                            sr += c.R; sg += c.G; sb += c.B; sl += lum; sl2 += lum * lum;
                        }
                    const double n = (r + 1) * (r + 1);
                    mean[q * 3] = sr / n; mean[q * 3 + 1] = sg / n; mean[q * 3 + 2] = sb / n;
                    variance[q] = sl2 / n - (sl / n) * (sl / n);
                }
                int best = 0;
                for (int q = 1; q < 4; q++) if (variance[q] < variance[best]) best = q;
                var col = Saturate((mean[best * 3], mean[best * 3 + 1], mean[best * 3 + 2]), 1.15);
                double canvas = ((x % 4 < 2) ^ (y % 4 < 2) ? 5 : -5) + (Noise(x, y, 17) - 0.5) * 8;
                o.Set(x, y, col.R + canvas, col.G + canvas, col.B + canvas);
            }
        return o.ToBitmap();
    }

    private static BitmapSource StainedGlass(BitmapSource s)
    {
        var p = Load(s, 300);
        const int cell = 18;
        int gw = p.W / cell + 2, gh = p.H / cell + 2;
        var seeds = new (double X, double Y)[gw * gh];
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
                seeds[gy * gw + gx] = ((gx + 0.1 + Noise(gx, gy, 21) * 0.8) * cell, (gy + 0.1 + Noise(gx, gy, 22) * 0.8) * cell);
        var owner = new int[p.W * p.H];
        var d1s = new float[p.W * p.H];
        var gap = new float[p.W * p.H];
        var sums = new double[seeds.Length * 4];
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int cx = x / cell, cy = y / cell, best = 0;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = cx + ox, gy = cy + oy;
                        if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) continue;
                        var sd = seeds[gy * gw + gx];
                        double d = Math.Sqrt((sd.X - x) * (sd.X - x) + (sd.Y - y) * (sd.Y - y));
                        if (d < d1) { d2 = d1; d1 = d; best = gy * gw + gx; }
                        else if (d < d2) d2 = d;
                    }
                int i = y * p.W + x;
                owner[i] = best; d1s[i] = (float)d1; gap[i] = (float)(d2 - d1);
                var c = p.Get(x, y);
                sums[best * 4] += c.R; sums[best * 4 + 1] += c.G; sums[best * 4 + 2] += c.B; sums[best * 4 + 3]++;
            }
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x, k = owner[i];
                double n = Math.Max(1, sums[k * 4 + 3]);
                var c = Saturate((sums[k * 4] / n, sums[k * 4 + 1] / n, sums[k * 4 + 2] / n), 1.35);
                double light = 1.12 - 0.25 * d1s[i] / cell + (Noise(x, y, 23) - 0.5) * 0.08;
                double lead = 1 - Smooth(1.2, 2.6, gap[i]);
                p.Set(x, y, c.R * light * (1 - lead) + 26 * lead, c.G * light * (1 - lead) + 24 * lead, c.B * light * (1 - lead) + 22 * lead);
            }
        return p.ToBitmap();
    }

    private static BitmapSource OldPhoto(BitmapSource s)
    {
        var p = Load(s, 320);
        var rng = new Random(p.W * 131 + p.H + p.D[p.D.Length / 2]);
        var scratches = Enumerable.Range(0, rng.Next(2, 6)).Select(_ => (X: rng.Next(p.W), Seed: rng.Next(1000))).ToArray();
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                var (r, g, b) = p.Get(x, y);
                double sr = 0.393 * r + 0.769 * g + 0.189 * b, sg = 0.349 * r + 0.686 * g + 0.168 * b, sb = 0.272 * r + 0.534 * g + 0.131 * b;
                double d = Math.Sqrt(Math.Pow(x / (double)p.W - 0.5, 2) + Math.Pow(y / (double)p.H - 0.5, 2)) / 0.7071;
                double f = 0.82 * (1 - 0.55 * Smooth(0.45, 1.0, d));
                double grain = (Noise(x, y, 31) - 0.5) * 26;
                double extra = 0;
                foreach (var sc in scratches) if (Math.Abs(x - sc.X - Math.Sin(y * 0.02 + sc.Seed) * 2) < 0.7 && Noise(sc.Seed, y / 6, 33) > 0.35) extra += 45;
                if (Noise(x, y, 37) > 0.9993) extra -= 90;
                if (Noise(x, y, 39) > 0.9993) extra += 90;
                p.Set(x, y, sr * f + 22 + grain + extra, sg * f + 18 + grain + extra, sb * f + 10 + grain + extra);
            }
        return p.ToBitmap();
    }

    private static BitmapSource Instant(BitmapSource s)
    {
        var p = Load(s, 340);
        int w = p.W, h = p.H;
        int side = (int)(w * 0.06), top = (int)(h * 0.06), bottom = (int)(h * 0.2);
        int iw = w - side * 2, ih = h - top - bottom;
        var o = new Px(w, h);
        // The picture is cropped to fill the frame's window.
        double scale = Math.Max(iw / (double)w, ih / (double)h);
        double ox = (w * scale - iw) / 2, oy = (h * scale - ih) / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double paper = (Noise(x, y, 41) - 0.5) * 6;
                if (x < side || x >= w - side || y < top || y >= h - bottom)
                {
                    o.Set(x, y, 250 + paper, 248 + paper, 241 + paper);
                    continue;
                }
                double lx = x - side, ly = y - top;
                var (r, g, b) = p.Get((int)((lx + ox) / scale), (int)((ly + oy) / scale));
                r = r * 0.86 + 30; g = g * 0.88 + 20; b = b * 0.78 + 26;
                double d = Math.Sqrt(Math.Pow(lx / iw - 0.5, 2) + Math.Pow(ly / ih - 0.5, 2)) / 0.7071;
                double v = 1 - 0.25 * Smooth(0.5, 1.0, d);
                double leak = Math.Max(0, 1 - Math.Sqrt(Math.Pow(lx / iw - 1, 2) + Math.Pow(ly / ih, 2)) / 0.55);
                double inner = Math.Min(Math.Min(lx, iw - lx), Math.Min(ly, ih - ly)) < 1.5 ? 0.82 : 1;
                o.Set(x, y, (r * v + 120 * leak) * inner, (g * v + 55 * leak) * inner, (b * v + 10 * leak) * inner);
            }
        return o.ToBitmap();
    }
}
