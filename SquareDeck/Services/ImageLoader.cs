using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>Decodes images into frozen bitmaps that can be handed to the UI from any thread.</summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/1.1");
        return c;
    }

    /// <summary>The widest any picture is decoded (a 4K screenshot is shown at 2560 pixels; nobody can tell on a TV).</summary>
    public const int MaxDecodeWidth = 2560;

    /// <summary>Where each picture decoded from a file came from, so it can be let go and decoded again later.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, Tuple<string, int>> Origins = new();

    /// <summary>The file (and decode width) a picture was read from, if it came from one.</summary>
    public static (string Path, int Width)? OriginOf(ImageSource? image) =>
        image != null && Origins.TryGetValue(image, out var o) ? (o.Item1, o.Item2) : null;

    // Pictures already decoded and still in use somewhere (9.0.1): a cover on Home and the same cover in the library, a
    // style's wallpaper in its preview and on its card, are decoded once and shared (they're frozen, so that's safe).
    // Only weak references are kept, so this never holds a picture in memory by itself; a file that has changed since
    // (its time or size) is decoded again.
    private static readonly object RecentGate = new();
    private static readonly Dictionary<string, (DateTime Stamp, long Size, WeakReference<ImageSource> Image)> Recent = new(StringComparer.OrdinalIgnoreCase);
    private static int _recentAdds;

    public static ImageSource? FromFile(string path, int decodeWidth)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            DateTime stamp = info.LastWriteTimeUtc;
            long size = info.Length;
            string key = decodeWidth + "|" + path;
            lock (RecentGate)
            {
                if (Recent.TryGetValue(key, out var known) && known.Stamp == stamp && known.Size == size && known.Image.TryGetTarget(out var alive))
                    return alive;
            }
            var image = FromBytes(File.ReadAllBytes(path), decodeWidth);
            if (image != null)
            {
                Origins.AddOrUpdate(image, Tuple.Create(path, decodeWidth));
                lock (RecentGate)
                {
                    Recent[key] = (stamp, size, new WeakReference<ImageSource>(image));
                    // Now and then, forget the entries whose pictures are gone.
                    if (++_recentAdds % 256 == 0)
                        foreach (var gone in Recent.Where(r => !r.Value.Image.TryGetTarget(out _)).Select(r => r.Key).ToList()) Recent.Remove(gone);
                }
            }
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static ImageSource? FromBytes(byte[] data, int decodeWidth)
    {
        try
        {
            // Windows icon files hold several sizes; use the largest instead of whichever comes first.
            if (data.Length > 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
            {
                using var icoStream = new MemoryStream(data);
                var decoder = new IconBitmapDecoder(icoStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
                if (best != null)
                {
                    best.Freeze();
                    return best;
                }
            }

            using var ms = new MemoryStream(data);
            // Every decoded picture sits in memory (and in video memory once drawn), so decode no larger than
            // asked for, never larger than the picture itself, and never more than MaxDecodeWidth across.
            int width = decodeWidth > 0 ? Math.Min(decodeWidth, MaxDecodeWidth) : MaxDecodeWidth;
            try
            {
                var probe = BitmapFrame.Create(ms, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                if (probe.PixelWidth > 0) width = Math.Min(width, probe.PixelWidth);
            }
            catch (Exception) { }
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = width;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A blurred copy of a picture, made once, for showing stretched behind the interface. A live blur over the whole
    /// screen is redone by the graphics card whenever anything in front of it moves; this costs nothing after it's
    /// made. The picture is shrunk (it's blurry anyway, so the detail isn't missed) and softened with three box blurs,
    /// which together look like the smooth blur Windows draws. <paramref name="radius"/> is in screen pixels at
    /// <paramref name="shownWidth"/> wide.
    /// </summary>
    public static BitmapSource? Blurred(ImageSource? image, double radius, double shownWidth = 1400)
    {
        if (image is not BitmapSource source || radius <= 0 || source.PixelWidth < 2 || source.PixelHeight < 2) return null;
        try
        {
            const int Box = 2; // three passes of a 5-pixel box: about a 2.45-pixel Gaussian in the small copy
            double scale = Math.Max(1, radius / 3.0 / 2.45);
            int width = Math.Clamp((int)Math.Round(shownWidth / scale), 16, source.PixelWidth);
            double f = (double)width / source.PixelWidth;
            BitmapSource small = Math.Abs(f - 1) < 0.001 ? source : new TransformedBitmap(source, new ScaleTransform(f, f));
            var pbgra = new FormatConvertedBitmap(small, PixelFormats.Pbgra32, null, 0);
            int w = pbgra.PixelWidth, h = pbgra.PixelHeight;
            if (w < 2 || h < 2) return null;
            var a = new byte[w * h * 4];
            pbgra.CopyPixels(a, w * 4, 0);
            var b = new byte[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(a, b, w, h, Box, horizontal: true);
                BoxBlur(b, a, w, h, Box, horizontal: false);
            }
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, a, w * 4);
            result.Freeze();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>One pass of a box blur along rows or columns, with the edge pixels repeated past the edge.</summary>
    private static void BoxBlur(byte[] from, byte[] to, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        int span = r * 2 + 1;
        for (int line = 0; line < lines; line++)
        {
            int Index(int i) => horizontal ? (line * w + Math.Clamp(i, 0, length - 1)) * 4 : (Math.Clamp(i, 0, length - 1) * w + line) * 4;
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int i = -r; i <= r; i++)
            {
                int k = Index(i);
                s0 += from[k]; s1 += from[k + 1]; s2 += from[k + 2]; s3 += from[k + 3];
            }
            for (int i = 0; i < length; i++)
            {
                int o = Index(i);
                to[o] = (byte)(s0 / span); to[o + 1] = (byte)(s1 / span); to[o + 2] = (byte)(s2 / span); to[o + 3] = (byte)(s3 / span);
                int add = Index(i + r + 1), drop = Index(i - r);
                s0 += from[add] - from[drop]; s1 += from[add + 1] - from[drop + 1];
                s2 += from[add + 2] - from[drop + 2]; s3 += from[add + 3] - from[drop + 3];
            }
        }
    }

    /// <summary>
    /// The most characteristic color of an image: an average that lets colorful pixels count for more
    /// than grays. Null when the image has no real color. Safe on a background thread.
    /// </summary>
    public static Color? DominantColor(BitmapSource image)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);

            double r = 0, g = 0, b = 0, weight = 0;
            int step = Math.Max(1, Math.Max(w, h) / 64);
            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    int i = y * stride + x * 4;
                    if (px[i + 3] < 128) continue;
                    int max = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    int min = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
                    double saturation = max == 0 ? 0 : (max - min) / (double)max;
                    double wgt = saturation * saturation;
                    if (wgt < 0.02) continue;
                    b += px[i] * wgt; g += px[i + 1] * wgt; r += px[i + 2] * wgt;
                    weight += wgt;
                }
            }
            return weight > 1 ? Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight)) : null;
        }
        catch (Exception)
        {
            return null; // unreadable pixel format
        }
    }

    /// <summary>
    /// A diagonal gradient in the icon's own dominant color, for the tile behind an app icon.
    /// Safe to call from a background thread (the brush is frozen).
    /// </summary>
    public static Brush GradientFrom(BitmapSource icon)
    {
        // Neutral blue-gray for colorless (black / white / gray) icons.
        Color main = DominantColor(icon) ?? Color.FromRgb(0x4A, 0x55, 0x68);

        static Color Mix(Color c, Color with, double amount) => Color.FromRgb(
            (byte)(c.R + (with.R - c.R) * amount),
            (byte)(c.G + (with.G - c.G) * amount),
            (byte)(c.B + (with.B - c.B) * amount));

        // Lighter top-left to darker bottom-right; kept dark enough that any icon stands out on it.
        var brush = new LinearGradientBrush(
            Mix(Mix(main, Colors.Black, 0.25), Colors.White, 0.12),
            Mix(main, Colors.Black, 0.68),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        brush.Freeze();
        return brush;
    }

    public static async Task<ImageSource?> FromUrlAsync(string url, int decodeWidth, CancellationToken ct = default)
    {
        try
        {
            var data = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            return FromBytes(data, decodeWidth);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
