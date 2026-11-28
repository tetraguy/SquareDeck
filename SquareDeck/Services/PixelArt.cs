using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

/// <summary>
/// Little pixel pictures drawn from text maps (one character per pixel), for the companion playground (9.0.4). Each
/// map becomes one small frozen bitmap shown with sharp, unblurred pixels, so a whole scene of furniture costs a
/// handful of images instead of thousands of rectangles.
/// </summary>
public static class PixelArt
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    /// <summary>A bitmap of the map; '.' and ' ' are see-through, other characters take their color from the palette.</summary>
    public static BitmapSource Bitmap(string[] map, IReadOnlyDictionary<char, Color> palette)
    {
        string key = string.Join("\n", map) + "|" + string.Join(",", palette.Select(p => p.Key + p.Value.ToString()));
        if (Cache.TryGetValue(key, out var known)) return known;
        int h = map.Length, w = map.Max(r => r.Length);
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < map[y].Length; x++)
            {
                char ch = map[y][x];
                if (ch is '.' or ' ' || !palette.TryGetValue(ch, out var c)) continue;
                int o = (y * w + x) * 4;
                // Premultiplied BGRA.
                double a = c.A / 255.0;
                pixels[o] = (byte)(c.B * a); pixels[o + 1] = (byte)(c.G * a); pixels[o + 2] = (byte)(c.R * a); pixels[o + 3] = c.A;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        bmp.Freeze();
        if (Cache.Count > 400) Cache.Clear();
        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>An image of the map, <paramref name="cell"/> screen pixels to each map pixel, with crisp edges.</summary>
    public static Image Image(string[] map, IReadOnlyDictionary<char, Color> palette, double cell)
    {
        var bmp = Bitmap(map, palette);
        var img = new Image { Source = bmp, Width = bmp.PixelWidth * cell, Height = bmp.PixelHeight * cell, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
