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
        ("win2000", "Windows 2000 icon", "A 256-color desktop icon on the Windows 2000 blue, with its label highlighted"),
        ("mosaic", "Roman mosaic", "Little stone tiles set in grout"),
        ("crossstitch", "Cross-stitch", "Stitched in colored thread on cream cloth"),
        ("lowpoly", "Low poly", "Flat-shaded triangles, like an early 3D model"),
        ("watercolor", "Watercolor", "Soft washes that pool at the edges, on textured paper"),
        ("risograph", "Risograph", "Two fluorescent inks, slightly out of line, like a zine print"),
        ("woodblock", "Woodblock print", "Ukiyo-e colors and inked outlines on fibrous paper"),
        ("engraving", "Engraving", "Fine wavy lines, like the portrait on a banknote"),
        ("crt", "CRT television", "Curved glass, glowing phosphor stripes and scanlines"),
        ("holofoil", "Holo foil", "A rainbow foil card that shimmers with sparkles"),
        ("kaleidoscope", "Kaleidoscope", "Mirrored six ways, like looking down a kaleidoscope"),
        // 8.5.21: six more.
        ("popart", "Pop art", "Four bold, flat-color panels, like a 1960s screen print"),
        ("pointillism", "Pointillism", "Painted in little dots of pure color on white"),
        ("chalkboard", "Chalkboard", "Drawn in white and colored chalk on a green board"),
        ("emboss", "Embossed metal", "Pressed into a sheet of brushed silver"),
        ("infrared", "Infrared", "False-color infrared film: pink leaves and deep teal skies"),
        ("cyberpunk", "Cyberpunk", "Teal and magenta city-light grading with a neon glow and scanlines"),
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

    /// <summary>
    /// The Detail option for the pixel and grid effects: Chunky 32, Classic 48, Fine 72, Detailed 112, Very detailed 160,
    /// Almost smooth 224 (the larger, the more detail and the less pixelated).
    /// </summary>
    public static readonly (string Value, string Name)[] DetailLevels =
        { ("32", "Chunky"), ("48", "Classic"), ("72", "Fine"), ("112", "Detailed"), ("160", "Very detailed"), ("224", "Almost smooth") };

    public static int Detail { get; set; } = 48;

    /// <summary>The Duotone palette's id.</summary>
    public static string Duotone { get; set; } = "accent";

    /// <summary>Effects drawn as hard pixels, which should be scaled up without smoothing.</summary>
    public static bool IsPixelArt(string kind) => kind is "retro" or "gameboy" or "win2000";

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
        "win2000" => Win2000(source, o.Detail),
        "mosaic" => Mosaic(source),
        "crossstitch" => CrossStitch(source),
        "lowpoly" => LowPoly(source),
        "watercolor" => Watercolor(source),
        "risograph" => Risograph(source),
        "woodblock" => Woodblock(source),
        "engraving" => Engraving(source),
        "crt" => Crt(source),
        "holofoil" => HoloFoil(source),
        "kaleidoscope" => Kaleidoscope(source),
        "popart" => PopArt(source),
        "pointillism" => Pointillism(source),
        "chalkboard" => Chalkboard(source),
        "emboss" => Emboss(source),
        "infrared" => Infrared(source),
        "cyberpunk" => Cyberpunk(source),
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
        // More detail also gets more colors, so the fine sizes look less posterized.
        double step = detail switch { >= 200 => 17, >= 150 => 24, >= 100 => 32, _ => 51 };
        for (int i = 0; i < p.D.Length; i += 4)
            for (int c = 0; c < 3; c++) p.D[i + c] = (byte)Math.Min(255, Math.Round(p.D[i + c] / step) * step);
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
        int across = detail switch { <= 32 => 16, <= 48 => 24, <= 72 => 32, <= 112 => 44, <= 160 => 56, _ => 72 };
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
        int cols = detail switch { <= 32 => 36, <= 48 => 48, <= 72 => 64, <= 112 => 88, <= 160 => 112, _ => 140 };
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

    // ---------------------------------------------------------------- the second set

    private static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);

    /// <summary>Windows' 256-color palette: the 6×6×6 color cube plus the 16 classic VGA colors.</summary>
    private static readonly Color[] Palette256 = Enumerable.Range(0, 216).Select(i => Color.FromRgb((byte)(i / 36 * 51), (byte)(i / 6 % 6 * 51), (byte)(i % 6 * 51)))
        .Concat(new[] { "#000000", "#800000", "#008000", "#808000", "#000080", "#800080", "#008080", "#C0C0C0", "#808080", "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#FF00FF", "#00FFFF", "#FFFFFF" }.Select(Hex)).ToArray();

    private static Color Nearest(Color[] palette, double r, double g, double b)
    {
        Color best = palette[0];
        double bestD = double.MaxValue;
        foreach (var c in palette)
        {
            double d = 2 * (c.R - r) * (c.R - r) + 4 * (c.G - g) * (c.G - g) + 3 * (c.B - b) * (c.B - b);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private static BitmapSource Win2000(BitmapSource s, int detail)
    {
        // A desktop the shape of the cover, in Windows 2000's own blue, with one icon on it.
        double aspect = s.PixelHeight / (double)s.PixelWidth;
        int cw = 64, ch = Math.Max(40, (int)Math.Round(64 * aspect));
        int icon = detail switch { <= 32 => 28, <= 48 => 34, <= 72 => 40, <= 112 => 44, _ => 48 };
        var desk = Hex("#3A6EA5");
        var o = new Px(cw, ch);
        for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) o.Set(x, y, desk);
        // The picture: the middle of the cover, squared off, in 256 colors with a little dithering.
        int side = Math.Min(s.PixelWidth, s.PixelHeight);
        var square = new CroppedBitmap(s, new Int32Rect((s.PixelWidth - side) / 2, (s.PixelHeight - side) / 2, side, side));
        var pic = Load(square, icon, icon, keepAspect: false);
        int ix = (cw - icon) / 2, iy = Math.Max(3, (int)(ch * 0.42) - icon / 2 - 4);
        // Its shadow, two pixels down and right, then the picture with a black outline and a bevel of light.
        for (int y = 0; y < icon; y++) for (int x = 0; x < icon; x++)
            {
                int sx = ix + x + 2, sy = iy + y + 2;
                if (sx < cw && sy < ch) { var c = o.Get(sx, sy); o.Set(sx, sy, c.R * 0.55, c.G * 0.55, c.B * 0.55); }
            }
        for (int y = 0; y < icon; y++)
            for (int x = 0; x < icon; x++)
            {
                var (r, g, b) = pic.Get(x, y);
                double t = (Bayer[y & 3, x & 3] + 0.5) / 16.0 - 0.5;
                var c = Nearest(Palette256, r + t * 28, g + t * 28, b + t * 28);
                double f = x == 1 || y == 1 ? 1.25 : x == icon - 2 || y == icon - 2 ? 0.7 : 1.0;
                if (x == 0 || y == 0 || x == icon - 1 || y == icon - 1) o.Set(ix + x, iy + y, 0, 0, 0);
                else o.Set(ix + x, iy + y, Math.Min(255, c.R * f), Math.Min(255, c.G * f), Math.Min(255, c.B * f));
            }
        // The label, selected: a navy box with a dotted focus line and white "text".
        int ly = iy + icon + 4, lw = icon + 10, lx = (cw - lw) / 2, lh = 9;
        if (ly + lh < ch)
        {
            var navy = Hex("#0A246A");
            for (int y = 0; y < lh; y++)
                for (int x = 0; x < lw; x++)
                {
                    bool edge = x == 0 || y == 0 || x == lw - 1 || y == lh - 1;
                    if (edge && (x + y) % 2 == 0) o.Set(lx + x, ly + y, 255, 255, 255);
                    else o.Set(lx + x, ly + y, navy);
                }
            // "GAME.EXE" in a tiny 3×5 pixel font, centered.
            var font = new Dictionary<char, string[]>
            {
                ['G'] = new[] { "111", "100", "101", "101", "111" }, ['A'] = new[] { "010", "101", "111", "101", "101" },
                ['M'] = new[] { "101", "111", "111", "101", "101" }, ['E'] = new[] { "111", "100", "110", "100", "111" },
                ['.'] = new[] { "0", "0", "0", "0", "1" }, ['X'] = new[] { "101", "101", "010", "101", "101" },
            };
            const string label = "GAME.EXE";
            int textW = label.Sum(ch2 => font[ch2][0].Length + 1) - 1;
            int tx = lx + (lw - textW) / 2;
            foreach (char letter in label)
            {
                var rows = font[letter];
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                        if (rows[y][x] == '1') o.Set(tx + x, ly + 2 + y, 255, 255, 255);
                tx += rows[0].Length + 1;
            }
        }
        return Enlarge(o, 5).ToBitmap();
    }

    private static BitmapSource Mosaic(BitmapSource s)
    {
        var p = Load(s, 300);
        var soft = BlurColor(p, 2);
        var o = new Px(p.W, p.H);
        const double cell = 10;
        for (int y = 0; y < p.H; y++)
        {
            int row = (int)(y / cell);
            double offset = (row % 2) * cell / 2 + Noise(row, 0, 51) * 3;
            for (int x = 0; x < p.W; x++)
            {
                double fx = x + offset;
                int col = (int)Math.Floor(fx / cell);
                double lx = fx - col * cell, ly = y - row * cell;
                // Tiles aren't perfectly square: each edge moves a little.
                double left = Noise(col, row, 52) * 1.4, top = Noise(col, row, 53) * 1.4;
                double right = cell - Noise(col, row, 54) * 1.4, bottom = cell - Noise(col, row, 55) * 1.4;
                double inset = Math.Min(Math.Min(lx - left, right - lx), Math.Min(ly - top, bottom - ly));
                double grout = 1 - Math.Clamp(inset - 0.4, 0, 1);
                int cx = (int)Math.Clamp(col * cell - offset + cell / 2, 0, p.W - 1), cy = (int)Math.Clamp(row * cell + cell / 2, 0, p.H - 1);
                var c = Saturate(soft.Get(cx, cy), 1.2);
                double shade = 0.9 + Noise(col, row, 56) * 0.2 + (inset < 2.2 && (lx - left < 2.2 || ly - top < 2.2) ? 0.12 : 0) + (Noise(x, y, 57) - 0.5) * 0.08;
                double gr = 196 + (Noise(x, y, 58) - 0.5) * 20;
                o.Set(x, y, c.R * shade * (1 - grout) + gr * grout, c.G * shade * (1 - grout) + (gr - 6) * grout, c.B * shade * (1 - grout) + (gr - 22) * grout);
            }
        }
        return o.ToBitmap();
    }

    /// <summary>A soft copy of a picture (each channel blurred).</summary>
    private static Px BlurColor(Px p, int r)
    {
        var ch = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            ch[c] = new float[p.W * p.H];
            for (int i = 0; i < ch[c].Length; i++) ch[c][i] = p.D[i * 4 + 2 - c];
            ch[c] = Blur(ch[c], p.W, p.H, r, 2);
        }
        var o = new Px(p.W, p.H);
        for (int i = 0; i < ch[0].Length; i++) o.Set(i % p.W, i / p.W, ch[0][i], ch[1][i], ch[2][i]);
        return o;
    }

    private static BitmapSource CrossStitch(BitmapSource s)
    {
        const int cell = 7;
        double aspect = s.PixelHeight / (double)s.PixelWidth;
        int cols = 46, rows = Math.Max(10, (int)Math.Round(cols * aspect));
        var small = Load(s, cols, rows, keepAspect: false);
        var o = new Px(cols * cell, rows * cell);
        var cloth = (R: 236.0, G: 230.0, B: 214.0);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                int cx = x / cell, cy = y / cell;
                double lx = x % cell + 0.5, ly = y % cell + 0.5;
                // Aida cloth: a woven grid with a hole at every corner.
                double weave = ((x % cell == 0) || (y % cell == 0) ? -16 : 0) + (Noise(x, y, 61) - 0.5) * 10;
                double hole = Math.Clamp(1.6 - Math.Sqrt(Math.Min(lx, cell - lx) * Math.Min(lx, cell - lx) + Math.Min(ly, cell - ly) * Math.Min(ly, cell - ly)), 0, 1) * 70;
                double r = cloth.R + weave - hole, g = cloth.G + weave - hole, b = cloth.B + weave - hole;
                var (sr, sg, sb) = Saturate(small.Get(cx, cy), 1.25);
                // Floss comes in set colors: rounded to a smaller set.
                sr = Math.Round(sr / 36) * 36; sg = Math.Round(sg / 36) * 36; sb = Math.Round(sb / 36) * 36;
                bool stitch = 0.299 * sr + 0.587 * sg + 0.114 * sb < 222;
                if (stitch)
                {
                    double d1 = Math.Abs(lx - ly) / Math.Sqrt(2), d2 = Math.Abs(lx + ly - cell) / Math.Sqrt(2);
                    double a1 = Math.Clamp(1.35 - d1, 0, 1), a2 = Math.Clamp(1.35 - d2, 0, 1);
                    // The thread is lit along its middle; the top stitch casts a little shadow on the bottom one.
                    double sh1 = 0.82 + 0.25 * Math.Clamp(1 - d1, 0, 1), sh2 = 0.9 + 0.3 * Math.Clamp(1 - d2, 0, 1);
                    double edge = Math.Clamp(2.2 - d2, 0, 1) * (1 - a2) * 0.25;
                    r = r * (1 - a1) + sr * sh1 * a1; g = g * (1 - a1) + sg * sh1 * a1; b = b * (1 - a1) + sb * sh1 * a1;
                    r *= 1 - edge; g *= 1 - edge; b *= 1 - edge;
                    r = r * (1 - a2) + sr * sh2 * a2; g = g * (1 - a2) + sg * sh2 * a2; b = b * (1 - a2) + sb * sh2 * a2;
                }
                o.Set(x, y, r, g, b);
            }
        return o.ToBitmap();
    }

    private static BitmapSource LowPoly(BitmapSource s)
    {
        var p = Load(s, 300);
        var soft = BlurColor(p, 3);
        var o = new Px(p.W, p.H);
        const double step = 24;
        int gw = (int)(p.W / step) + 2, gh = (int)(p.H / step) + 2;
        var pts = new (double X, double Y)[gw * gh];
        for (int j = 0; j < gh; j++)
            for (int i = 0; i < gw; i++)
            {
                bool edgeX = i == 0 || i == gw - 1, edgeY = j == 0 || j == gh - 1;
                pts[j * gw + i] = (Math.Clamp(i * step + (edgeX ? 0 : (Noise(i, j, 71) - 0.5) * step * 0.8), 0, p.W + step),
                                   Math.Clamp(j * step + (edgeY ? 0 : (Noise(i, j, 72) - 0.5) * step * 0.8), 0, p.H + step));
            }
        void Fill((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, int seed)
        {
            int cx = (int)Math.Clamp((a.X + b.X + c.X) / 3, 0, p.W - 1), cy = (int)Math.Clamp((a.Y + b.Y + c.Y) / 3, 0, p.H - 1);
            var col = Saturate(soft.Get(cx, cy), 1.15);
            // Facets catch the light differently: the slope of each triangle's brightness decides how much.
            double la = Lum(soft, (int)a.X, (int)a.Y), lb = Lum(soft, (int)b.X, (int)b.Y), lc = Lum(soft, (int)c.X, (int)c.Y);
            double light = 1 + Math.Clamp(((lb - la) - (lc - la)) / 400, -0.12, 0.12) + (Noise(seed, 0, 73) - 0.5) * 0.08;
            int x0 = (int)Math.Max(0, Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = (int)Math.Min(p.W - 1, Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = (int)Math.Max(0, Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = (int)Math.Min(p.H - 1, Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(area) < 1e-6) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                    double w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -0.001 || w1 < -0.001 || w2 < -0.001) continue;
                    o.Set(x, y, col.R * light, col.G * light, col.B * light);
                }
        }
        for (int j = 0; j < gh - 1; j++)
            for (int i = 0; i < gw - 1; i++)
            {
                var a = pts[j * gw + i]; var b = pts[j * gw + i + 1]; var c = pts[(j + 1) * gw + i]; var d = pts[(j + 1) * gw + i + 1];
                if ((i + j) % 2 == 0) { Fill(a, b, d, i * 7 + j * 131); Fill(a, d, c, i * 7 + j * 131 + 1); }
                else { Fill(a, b, c, i * 7 + j * 131); Fill(b, d, c, i * 7 + j * 131 + 1); }
            }
        return o.ToBitmap();
    }

    private static BitmapSource Watercolor(BitmapSource s)
    {
        var p = Load(s, 300);
        var soft = BlurColor(p, 2);
        var o = new Px(p.W, p.H);
        // Pigment pools where one wash meets another, so the edges come out darker.
        var lumSoft = Luma(soft);
        var post = lumSoft.Select(v => (float)(Math.Round(v / 42.0) * 42)).ToArray();
        var edges = Edges(Blur(post, p.W, p.H, 1, 1), p.W, p.H);
        var paperLow = new float[p.W * p.H];
        for (int i = 0; i < paperLow.Length; i++) paperLow[i] = (float)Noise(i % p.W / 6, i / p.W / 6, 81);
        paperLow = Blur(paperLow, p.W, p.H, 4, 2);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                var c = soft.Get(x, y);
                double Step(double v) => Math.Round(v / 32.0) * 32 * 0.6 + v * 0.4;
                double r = Step(c.R), g = Step(c.G), b = Step(c.B);
                (r, g, b) = Saturate((r, g, b), 1.1 + (paperLow[i] - 0.5) * 0.6);
                // Thinned with water: lighter overall, toward the white of the paper.
                double wash = 0.72;
                r = 250 - (250 - r) * wash; g = 247 - (247 - g) * wash; b = 238 - (238 - b) * wash;
                double pool = 1 - Math.Clamp(edges[i] / 70, 0, 0.3);
                double grain = 0.94 + 0.06 * Noise(x, y, 82) + (paperLow[i] - 0.5) * 0.08;
                o.Set(x, y, r * pool * grain, g * pool * grain, b * pool * grain);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Risograph(BitmapSource s)
    {
        var p = Load(s, 300);
        var o = new Px(p.W, p.H);
        var pink = (R: 255.0, G: 72.0, B: 176.0);
        var blue = (R: 0.0, G: 120.0, B: 191.0);
        double Halftone(int x, int y, double density, double angle, double spacing)
        {
            double ca = Math.Cos(angle), sa = Math.Sin(angle);
            double u = (x * ca + y * sa) / spacing, v = (-x * sa + y * ca) / spacing;
            double d = Math.Sqrt(Math.Pow(u - Math.Round(u), 2) + Math.Pow(v - Math.Round(v), 2)) * spacing;
            return Math.Clamp(spacing * 0.68 * Math.Sqrt(Math.Clamp(density, 0, 1)) - d + 0.5, 0, 1);
        }
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                // The blue drum prints where there's little red; the pink one, a little out of line, where there's little green.
                var cb = p.Get(x, y);
                var cp = p.Get(x - 2, y - 1);
                double db = Math.Pow(1 - cb.R / 255.0, 1.1) * 0.95, dp = Math.Pow(1 - cp.G / 255.0, 1.1) * 0.9;
                double inkB = Halftone(x, y, db, 0.26, 4.2) * (0.86 + 0.14 * Noise(x, y, 91));
                double inkP = Halftone(x, y, dp, 1.31, 4.2) * (0.86 + 0.14 * Noise(x, y, 92));
                double r = 246, g = 241, b = 230;
                // Inks are see-through: where both print, they mix (multiply).
                r *= 1 - inkB * (1 - blue.R / 255); g *= 1 - inkB * (1 - blue.G / 255); b *= 1 - inkB * (1 - blue.B / 255);
                r *= 1 - inkP * (1 - pink.R / 255); g *= 1 - inkP * (1 - pink.G / 255); b *= 1 - inkP * (1 - pink.B / 255);
                double grain = (Noise(x, y, 93) - 0.5) * 12;
                o.Set(x, y, r + grain, g + grain, b + grain);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Woodblock(BitmapSource s)
    {
        var palette = new[] { "#263A69", "#3C5F8C", "#7FA3B5", "#CD4632", "#D7A550", "#8CA06E", "#F0E4C8", "#2A2423", "#B9806A" }.Select(Hex).ToArray();
        var p = Load(s, 300);
        var soft = BlurColor(p, 1);
        var l = Luma(soft);
        var e = Edges(Blur(l, p.W, p.H, 1, 1), p.W, p.H);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                var c = Saturate(soft.Get(x, y), 1.3);
                var ink = Nearest(palette, c.R, c.G, c.B);
                // The block's grain shows through the ink, and the paper has long fibers.
                double grain = 0.92 + 0.08 * Math.Sin(y * 0.9 + Math.Sin(x * 0.05) * 3 + Noise(x / 9, y / 2, 101) * 2);
                double fiber = (Noise(x / 7, y, 102) - 0.5) * 10;
                double line = Smooth(26, 48, e[y * p.W + x]);
                o.Set(x, y, (ink.R * grain + fiber) * (1 - line) + 34 * line, (ink.G * grain + fiber) * (1 - line) + 30 * line, (ink.B * grain + fiber) * (1 - line) + 30 * line);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Engraving(BitmapSource s)
    {
        var p = Load(s, 340);
        var l = Blur(Luma(p), p.W, p.H, 1, 1);
        Stretch(l);
        var o = new Px(p.W, p.H);
        const double spacing = 4.6;
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double dark = 1 - l[y * p.W + x] / 255.0;
                // Lines that ripple across the picture, thicker where it's darker.
                double phase = (y + 2.6 * Math.Sin(x * 0.045 + y * 0.012) + 1.4 * Math.Sin(x * 0.11)) / spacing;
                double d = Math.Abs(phase - Math.Round(phase)) * spacing;
                double ink = Math.Clamp(spacing * 0.5 * dark - d + 0.55, 0, 1);
                // Cross-hatching in the deepest shadows.
                if (dark > 0.62)
                {
                    double ph2 = (x * 0.7 + y * 0.7) / spacing;
                    double d2 = Math.Abs(ph2 - Math.Round(ph2)) * spacing;
                    ink = Math.Max(ink, Math.Clamp(spacing * 0.45 * (dark - 0.62) / 0.38 - d2 + 0.5, 0, 1));
                }
                double paper = (Noise(x, y, 111) - 0.5) * 6;
                o.Set(x, y, 240 + paper - 214 * ink, 234 + paper - 196 * ink, 214 + paper - 184 * ink);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Crt(BitmapSource s)
    {
        var p = Load(s, 320);
        var o = new Px(p.W, p.H);
        var glowSrc = BlurColor(p, 3);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                // Curved glass: the picture bulges out toward the middle.
                double nx = x / (double)p.W * 2 - 1, ny = y / (double)p.H * 2 - 1;
                double k = 0.11;
                double bx = nx * (1 + k * ny * ny), by = ny * (1 + k * nx * nx);
                double sx = (bx + 1) / 2 * p.W, sy = (by + 1) / 2 * p.H;
                // The screen's rounded corners, black beyond them.
                double cornerX = Math.Max(0, Math.Abs(bx) - 0.86) / 0.14, cornerY = Math.Max(0, Math.Abs(by) - 0.86) / 0.14;
                if (Math.Abs(bx) > 1 || Math.Abs(by) > 1 || cornerX * cornerX + cornerY * cornerY > 1) { o.Set(x, y, 8, 8, 10); continue; }
                var c = p.Get((int)sx, (int)sy);
                var gl = glowSrc.Get((int)sx, (int)sy);
                // Phosphor stripes: each column lights one of red, green or blue more.
                int phase = x % 3;
                double mr = phase == 0 ? 1.25 : 0.8, mg = phase == 1 ? 1.25 : 0.8, mb = phase == 2 ? 1.25 : 0.8;
                double scan = y % 3 == 2 ? 0.62 : 1.0;
                double vignette = 1 - 0.4 * Smooth(0.4, 1.3, Math.Sqrt(bx * bx + by * by));
                o.Set(x, y, (c.R * mr * scan + gl.R * 0.35) * vignette, (c.G * mg * scan + gl.G * 0.35) * vignette, (c.B * mb * scan + gl.B * 0.35) * vignette);
            }
        return o.ToBitmap();
    }

    // ---------------------------------------------------------------- 8.5.21

    /// <summary>Four panels of the cover, each posterized into three flat colors from a different bold set.</summary>
    private static BitmapSource PopArt(BitmapSource s)
    {
        var p = Load(s, 160);
        var l = Luma(p);
        Stretch(l);
        string[][] sets =
        {
            new[] { "#1B1464", "#ED1E79", "#FCEE21" }, new[] { "#00A99D", "#F7931E", "#FFFFFF" },
            new[] { "#662D91", "#39B54A", "#FBB03B" }, new[] { "#C1272D", "#29ABE2", "#F2F2F2" },
        };
        var o = new Px(p.W * 2, p.H * 2);
        for (int panel = 0; panel < 4; panel++)
        {
            var c = sets[panel].Select(Hex).ToArray();
            int ox = panel % 2 * p.W, oy = panel / 2 * p.H;
            for (int y = 0; y < p.H; y++)
                for (int x = 0; x < p.W; x++)
                {
                    float v = l[y * p.W + x];
                    var col = v < 85 ? c[0] : v < 170 ? c[1] : c[2];
                    bool border = x < 2 || y < 2 || x >= p.W - 2 || y >= p.H - 2;
                    o.Set(ox + x, oy + y, border ? Colors.Black : col);
                }
        }
        return o.ToBitmap();
    }

    /// <summary>Dots of saturated color, each the color under it, scattered over white paper.</summary>
    private static BitmapSource Pointillism(BitmapSource s)
    {
        var p = Load(s, 320);
        var o = new Px(p.W, p.H);
        for (int i = 0; i < o.D.Length; i += 4) { o.D[i] = 236; o.D[i + 1] = 244; o.D[i + 2] = 248; o.D[i + 3] = 255; }
        var rng = new Random(11);
        int dots = p.W * p.H / 7;
        for (int n = 0; n < dots; n++)
        {
            int cx = rng.Next(p.W), cy = rng.Next(p.H);
            var c = Saturate(p.Get(cx, cy), 1.5);
            int rad = 1 + rng.Next(2);
            for (int y = -rad; y <= rad; y++)
                for (int x = -rad; x <= rad; x++)
                {
                    if (x * x + y * y > rad * rad + 1) continue;
                    int px = cx + x, py = cy + y;
                    if (px < 0 || py < 0 || px >= p.W || py >= p.H) continue;
                    o.Set(px, py, c.R, c.G, c.B);
                }
        }
        return o.ToBitmap();
    }

    /// <summary>Edges in chalk (white, with a hint of the cover's color) over a dusty green board.</summary>
    private static BitmapSource Chalkboard(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        var e = Edges(Blur(l, p.W, p.H, 1, 2), p.W, p.H);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                double grain = Noise(x, y, 3) * 18;
                double chalk = Smooth(10, 34, e[i]) * (0.75 + 0.25 * Noise(x / 2, y / 2, 9));
                double fill = l[i] / 255.0 * 0.22; // a soft smudge of the lighter parts
                var (r, g, b) = p.Get(x, y);
                double tint = 0.25;
                double baseR = 34 + grain, baseG = 62 + grain, baseB = 48 + grain;
                double k = Math.Min(1, chalk + fill);
                o.Set(x, y, baseR + (235 * (1 - tint) + r * tint - baseR) * k,
                            baseG + (240 * (1 - tint) + g * tint - baseG) * k,
                            baseB + (230 * (1 - tint) + b * tint - baseB) * k);
            }
        return o.ToBitmap();
    }

    /// <summary>Lit from the top left as if pressed into metal, with fine horizontal brushing.</summary>
    private static BitmapSource Emboss(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Blur(Luma(p), p.W, p.H, 1, 1);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                float a = l[Math.Max(0, y - 1) * p.W + Math.Max(0, x - 1)], b = l[Math.Min(p.H - 1, y + 1) * p.W + Math.Min(p.W - 1, x + 1)];
                double relief = (a - b) * 1.6;
                double brush = Noise(x / 9, y, 5) * 14;
                double v = 150 + relief + brush;
                o.Set(x, y, v * 0.97, v * 0.99, v * 1.03);
            }
        return o.ToBitmap();
    }

    /// <summary>False-color infrared: greens turn pink and white, blues turn deep teal, reds go golden.</summary>
    private static BitmapSource Infrared(BitmapSource s)
    {
        var p = Load(s, 320);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                var (r, g, b) = p.Get(x, y);
                // Channel swap (green becomes red), then a pink lift for what was green.
                double leaf = Math.Max(0, g - (r + b) / 2) / 255.0;
                double nr = g * 0.9 + 255 * leaf * 0.6, ng = b * 0.55 + r * 0.25 + 120 * leaf * 0.4, nb = r * 0.4 + b * 0.45 + 160 * leaf * 0.5;
                o.Set(x, y, nr + 20, ng + 10, nb + 30);
            }
        return o.ToBitmap();
    }

    /// <summary>Shadows to teal, lights to magenta, a neon glow on the edges and faint scanlines.</summary>
    private static BitmapSource Cyberpunk(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        var e = Edges(Blur(l, p.W, p.H, 1, 1), p.W, p.H).Select(v => (float)Smooth(14, 40, v)).ToArray();
        var glow = Blur(e, p.W, p.H, 3, 2);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                int i = y * p.W + x;
                var (r, g, b) = p.Get(x, y);
                double t = l[i] / 255.0;
                double gr = r * 0.55 + (20 + 235 * t) * 0.45, gg = g * 0.45 + (60 * (1 - t) + 20 * t) * 0.55, gb = b * 0.5 + (120 + 100 * t) * 0.5;
                double gl = Math.Min(1, glow[i] * 2);
                double scan = y % 3 == 0 ? 0.85 : 1;
                o.Set(x, y, (gr + 255 * gl * 0.5) * scan, (gg + 40 * gl) * scan, (gb + 230 * gl * 0.5) * scan);
            }
        return o.ToBitmap();
    }

    private static (double R, double G, double B) Hue(double h)
    {
        h = (h % 1 + 1) % 1 * 6;
        double x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch { 0 => (1, x, 0.0), 1 => (x, 1, 0.0), 2 => (0.0, 1, x), 3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1, 0.0, x) };
        return (r * 255, g * 255, b * 255);
    }

    private static BitmapSource HoloFoil(BitmapSource s)
    {
        var p = Load(s, 320);
        var l = Luma(p);
        var o = new Px(p.W, p.H);
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                var c = p.Get(x, y);
                double lum = l[y * p.W + x] / 255.0;
                // The foil's rainbow runs diagonally and shifts with the picture's brightness, as if the card were tilted.
                double t = (x + y * 0.8) / (p.W + p.H) * 2.2 + lum * 0.6 + Math.Sin(x * 0.04) * 0.05;
                var rainbow = Hue(t);
                double foil = 0.28 + 0.25 * lum;
                // Screen blend: light adds, never darkens.
                double Screen(double a, double b) => 255 - (255 - a) * (255 - b) / 255;
                double r = c.R + (Screen(c.R, rainbow.R) - c.R) * foil, g = c.G + (Screen(c.G, rainbow.G) - c.G) * foil, b = c.B + (Screen(c.B, rainbow.B) - c.B) * foil;
                double band = Math.Exp(-Math.Pow(((x - y * 0.6) / (double)p.W - 0.25) * 6, 2)) * 70;
                double sparkle = Noise(x, y, 121) > 0.996 ? 160 : Noise(x / 2, y / 2, 122) > 0.992 ? 60 : 0;
                o.Set(x, y, r + band + sparkle, g + band + sparkle, b + band + sparkle);
            }
        return o.ToBitmap();
    }

    private static BitmapSource Kaleidoscope(BitmapSource s)
    {
        var p = Load(s, 320);
        var o = new Px(p.W, p.H);
        double cx = p.W / 2.0, cy = p.H / 2.0;
        double wedge = Math.PI / 3;
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                double dx = x - cx, dy = y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy), a = Math.Atan2(dy, dx) + Math.PI;
                // Fold every angle into one wedge, mirrored back and forth.
                double m = a % wedge;
                if ((int)(a / wedge) % 2 == 1) m = wedge - m;
                double sa = m - Math.PI / 2 - wedge / 2;
                double sx = cx + r * 0.95 * Math.Cos(sa), sy = cy + r * 0.95 * Math.Sin(sa);
                var c = p.Get((int)Math.Clamp(sx, 0, p.W - 1), (int)Math.Clamp(sy, 0, p.H - 1));
                // A little sparkle of light at the middle, as if through glass.
                double glow = Math.Exp(-r * r / (p.W * p.W * 0.02)) * 40;
                o.Set(x, y, c.R + glow, c.G + glow, c.B + glow);
            }
        return o.ToBitmap();
    }
}
