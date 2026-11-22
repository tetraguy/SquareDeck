using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// The companion's pictures: six original pixel characters (12 × 12), in any color, with faces for each mood and
/// poses for its animations. Drawn with rectangles, so they stay crisp at any size.
/// </summary>
public static class CompanionArt
{
    public static readonly (string Id, string Name)[] Species =
    {
        ("blob", "Blob"), ("cat", "Cat"), ("bunny", "Bunny"), ("ghost", "Ghost"), ("robot", "Robot"), ("dino", "Dino"),
    };

    public static readonly (string Name, string Hex)[] Colors =
    {
        ("Mint", "#7FD8BE"), ("Sky", "#7FB8FF"), ("Peach", "#FFB38A"), ("Lilac", "#C3A6FF"),
        ("Lemon", "#FFE27A"), ("Rose", "#FF8FB1"), ("Snow", "#E8ECF2"), ("Charcoal", "#6A7383"),
    };

    public static string SpeciesId => Labs.Get("companion.species", "blob");
    public static string ColorHex => Labs.Get("companion.color", "#7FD8BE");
    public static string Name => Labs.Get("companion.name", "Pip");

    // B body, D darker body, K black, W white, P cheek pink, E inner ear pink, A antenna light.
    private static readonly Dictionary<string, string[]> Maps = new()
    {
        ["blob"] = new[]
        {
            "............", "............", "....BBBB....", "..BBBBBBBB..", ".BBBBBBBBBB.", ".BBBBBBBBBB.",
            "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", ".BBBBBBBBBB.", "..BBBBBBBB..", "...D....D...",
        },
        ["cat"] = new[]
        {
            "............", ".B........B.", ".BE......EB.", ".BBBBBBBBBB.", "BBBBBBBBBBBB", "BBBBBBBBBBBB",
            "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", ".BBBBBBBBBB.", "..BBBBBBBBDD", "..D.D..D.D..",
        },
        ["bunny"] = new[]
        {
            "...BB..BB...", "...BE..EB...", "...BE..EB...", "...BB..BB...", "..BBBBBBBB..", ".BBBBBBBBBB.",
            "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", ".BBBBBBBBBB.", "..BBBBBBBB..", "...DD..DD...",
        },
        ["ghost"] = new[]
        {
            "............", "....BBBB....", "..BBBBBBBB..", ".BBBBBBBBBB.", ".BBBBBBBBBB.", "BBBBBBBBBBBB",
            "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBB", "B.BB.BB.BB.B",
        },
        ["robot"] = new[]
        {
            ".....A......", ".....D......", "..DDDDDDDD..", "..BBBBBBBB..", ".DBBBBBBBBD.", ".DBBBBBBBBD.",
            ".DBBBBBBBBD.", ".DBBBBBBBBD.", "..BBBBBBBB..", "..DDDDDDDD..", "...BB..BB...", "..DDD..DDD..",
        },
        ["dino"] = new[]
        {
            "............", "....D.D.....", "...BBBBBB...", "..BBBBBBBB..", ".BBBBBBBBBB.", ".BBBBBBBBBB.",
            "BBBBBBBBBBBB", "BBBBBBBBBBBB", "BBBBBBBBBBBD", ".BBBBBBBBBDD", "..BBBBBBBB..", "..DD....DD..",
        },
    };

    /// <summary>
    /// Draws the companion into a canvas (six screen pixels to each art pixel unless <paramref name="cell"/> says otherwise).
    /// <paramref name="mood"/>: happy, sleepy, cheer, bored, worried, love, dance. <paramref name="look"/>: -1 left, 0 ahead, 1 right.
    /// <paramref name="wave"/>: an arm raised (for waving, alternate it).
    /// </summary>
    public static void Draw(Canvas canvas, string species, string colorHex, string mood, bool blink, string outfit, int look = 0, bool wave = false, double cell = 6)
    {
        canvas.Children.Clear();
        var map = Maps.TryGetValue(species, out var m) ? m : Maps["blob"];
        var grid = map.Select(r => r.ToCharArray()).ToArray();
        void Put(int x, int y, char c) { if (y >= 0 && y < 12 && x >= 0 && x < 12) grid[y][x] = c; }
        int ex = look; // eyes shift toward where you're going
        bool robot = species == "robot";
        // Eyes.
        if (mood == "sleepy" || blink) { Put(3 + ex, 6, 'K'); Put(4 + ex, 6, 'K'); Put(7 + ex, 6, 'K'); Put(8 + ex, 6, 'K'); }
        else if (mood == "bored") { Put(3, 6, 'K'); Put(4, 6, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); }
        else if (mood == "love") { Put(4, 5, 'P'); Put(4, 6, 'P'); Put(7, 5, 'P'); Put(7, 6, 'P'); }
        else if (mood == "cheer") { Put(3, 6, 'K'); Put(4, 5, 'K'); Put(5, 6, 'K'); Put(6, 6, 'K'); Put(7, 5, 'K'); Put(8, 6, 'K'); }
        else { Put(4 + ex, 5, robot ? 'A' : 'W'); Put(4 + ex, 6, 'K'); Put(7 + ex, 5, robot ? 'A' : 'W'); Put(7 + ex, 6, 'K'); }
        // Mouth.
        switch (mood)
        {
            case "cheer": Put(5, 8, 'K'); Put(6, 8, 'K'); Put(5, 9, 'R'); Put(6, 9, 'R'); break;
            case "bored": Put(5, 8, 'K'); Put(6, 8, 'K'); break;
            case "worried": Put(4, 9, 'K'); Put(5, 8, 'K'); Put(6, 8, 'K'); Put(7, 9, 'K'); break;
            case "sleepy": Put(5, 8, 'K'); break;
            default: Put(4, 8, 'K'); Put(7, 8, 'K'); Put(5, 9, 'K'); Put(6, 9, 'K'); break;
        }
        if (mood is "happy" or "cheer" or "love" or "dance") { Put(2, 7, 'P'); Put(9, 7, 'P'); }
        // Arms: up when cheering, one up when waving.
        if (mood == "cheer") { Put(0, 4, 'B'); Put(11, 4, 'B'); Put(0, 5, 'B'); Put(11, 5, 'B'); }
        if (wave) { Put(11, 3, 'B'); Put(11, 4, 'B'); Put(10, 5, 'B'); }
        // Outfits.
        switch (outfit)
        {
            case "cap": for (int x = 3; x <= 8; x++) Put(x, 1, 'C'); for (int x = 2; x <= 9; x++) Put(x, 2, 'C'); Put(10, 2, 'C'); Put(11, 2, 'C'); break;
            case "crown": Put(3, 0, 'G'); Put(6, 0, 'G'); Put(8, 0, 'G'); for (int x = 3; x <= 8; x++) Put(x, 1, 'G'); break;
            case "scarf": for (int x = 1; x <= 10; x++) Put(x, 10, 'S'); Put(9, 11, 'S'); break;
            case "shades": for (int x = 2; x <= 9; x++) Put(x, 5, 'K'); Put(3, 6, 'K'); Put(4, 6, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); break;
        }
        var body = (Color)ColorConverter.ConvertFromString(colorHex);
        var dark = Color.FromRgb((byte)(body.R * 0.68), (byte)(body.G * 0.68), (byte)(body.B * 0.68));
        var colors = new Dictionary<char, Color>
        {
            ['B'] = body, ['D'] = dark, ['K'] = Color.FromRgb(0x1E, 0x22, 0x2B), ['W'] = System.Windows.Media.Colors.White,
            ['R'] = Color.FromRgb(0xE0, 0x4F, 0x5F), ['P'] = Color.FromRgb(0xF5, 0x8F, 0xA8), ['E'] = Color.FromRgb(0xF5, 0xB5, 0xC5),
            ['A'] = Color.FromRgb(0x6F, 0xE6, 0xFF), ['C'] = Color.FromRgb(0x3E, 0x6F, 0xD9), ['G'] = Color.FromRgb(0xF2, 0xC1, 0x4E),
            ['S'] = Color.FromRgb(0xD9, 0x48, 0x48),
        };
        var brushes = colors.ToDictionary(kv => kv.Key, kv => { var b = new SolidColorBrush(kv.Value); b.Freeze(); return (Brush)b; });
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                if (!brushes.TryGetValue(grid[y][x], out var brush)) continue;
                var r = new Rectangle { Width = cell, Height = cell, Fill = brush };
                Canvas.SetLeft(r, x * cell);
                Canvas.SetTop(r, y * cell);
                canvas.Children.Add(r);
            }
    }
}
