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

    public static ImageSource? FromFile(string path, int decodeWidth)
    {
        try
        {
            return FromBytes(File.ReadAllBytes(path), decodeWidth);
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
