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

    /// <summary>Every outfit, by id, with the name shown for it (earned in Unlockables).</summary>
    public static readonly (string Id, string Name)[] Outfits =
    {
        ("cap", "Cap"), ("scarf", "Scarf"), ("shades", "Sunglasses"), ("crown", "Crown"),
        ("headphones", "Headphones"), ("wizard", "Wizard hat"), ("halo", "Halo"), ("bow", "Bow"), ("tophat", "Top hat"),
        ("party", "Party hat"), ("flower", "Flower"), ("bandana", "Bandana"), ("horns", "Little horns"), ("visor", "Space visor"),
        // 9.0.17: twelve more.
        ("beanie", "Beanie"), ("chef", "Chef's hat"), ("cowboy", "Cowboy hat"), ("pirate", "Pirate hat"), ("antlers", "Antlers"),
        ("beret", "Beret"), ("knight", "Knight's helmet"), ("catears", "Cat-ear headband"), ("santa", "Santa hat"),
        ("propeller", "Propeller cap"), ("grad", "Graduation cap"), ("sprout", "Little sprout"),
    };

    /// <summary>
    /// Accessories (9.0.3): worn with any outfit, on the face, cheeks or neck. A few come free; the rest are bought with
    /// star bits in the companion playground (Experimental).
    /// </summary>
    public static readonly (string Id, string Name)[] Accessories =
    {
        ("bowtie", "Bow tie"), ("glasses", "Reading glasses"), ("freckles", "Freckles"), ("blush", "Rosy cheeks"),
        ("mustache", "Fancy mustache"), ("monocle", "Monocle"), ("star", "Star sticker"), ("collar", "Bell collar"),
        ("necktie", "Necktie"), ("eyepatch", "Pirate patch"), ("heart", "Heart badge"), ("sweatband", "Sweatband"), ("pearls", "Pearl necklace"),
        // 9.0.17: twelve more.
        ("bandaid", "Bandage"), ("beautymark", "Beauty mark"), ("earring", "Gold earring"), ("medal", "Gold medal"), ("lei", "Flower lei"),
        ("gem", "Forehead gem"), ("clownnose", "Clown nose"), ("goggles", "Goggles"), ("sparkles", "Sparkles"), ("bubble", "Bubble gum"),
        ("whiskers", "Drawn-on whiskers"), ("bandananeck", "Neck bandana"),
    };

    public static string AccessoryName(string id) => id.Length == 0 ? "None" : Accessories.FirstOrDefault(o => o.Id == id).Name ?? id;

    /// <summary>The accessory it's wearing ("" for none).</summary>
    public static string Accessory => Labs.Get("companion.accessory", "");

    public static string OutfitName(string id) => id.Length == 0 ? "None" : Outfits.FirstOrDefault(o => o.Id == id).Name ?? id;

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
    public static void Draw(Canvas canvas, string species, string colorHex, string mood, bool blink, string outfit, int look = 0, bool wave = false, double cell = 6, string? accessory = null)
    {
        accessory ??= Accessory;
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
            // 8.5.37: ten more, earned in Unlockables.
            case "headphones":
                for (int x = 3; x <= 8; x++) Put(x, 1, 'H');
                Put(2, 2, 'H'); Put(9, 2, 'H'); Put(1, 3, 'H'); Put(10, 3, 'H');
                for (int y = 5; y <= 7; y++) { Put(0, y, 'H'); Put(1, y, 'H'); Put(10, y, 'H'); Put(11, y, 'H'); }
                Put(0, 6, 'C'); Put(11, 6, 'C');
                break;
            case "wizard":
                Put(7, 0, 'V'); Put(6, 1, 'V'); Put(7, 1, 'V'); for (int x = 4; x <= 7; x++) Put(x, 2, 'V'); Put(5, 2, 'G');
                for (int x = 1; x <= 10; x++) Put(x, 3, 'V');
                break;
            case "halo": for (int x = 3; x <= 8; x++) Put(x, 0, 'G'); Put(2, 1, 'G'); Put(9, 1, 'G'); break;
            case "bow":
                Put(6, 1, 'R'); Put(6, 2, 'R'); Put(6, 3, 'R'); Put(7, 2, 'R'); Put(8, 2, 'P');
                Put(9, 2, 'R'); Put(10, 1, 'R'); Put(10, 2, 'R'); Put(10, 3, 'R');
                break;
            case "tophat":
                for (int y = 0; y <= 1; y++) for (int x = 4; x <= 7; x++) Put(x, y, 'K');
                for (int x = 4; x <= 7; x++) Put(x, 2, 'S');
                for (int x = 2; x <= 9; x++) Put(x, 3, 'K');
                break;
            case "party":
                Put(6, 0, 'W'); Put(5, 1, 'C'); Put(6, 1, 'G'); for (int x = 4; x <= 7; x++) Put(x, 2, x % 2 == 0 ? 'C' : 'G');
                for (int x = 3; x <= 8; x++) Put(x, 3, x % 2 == 0 ? 'G' : 'C');
                break;
            case "flower": Put(3, 1, 'W'); Put(2, 2, 'W'); Put(4, 2, 'W'); Put(3, 3, 'W'); Put(3, 2, 'G'); Put(4, 3, 'L'); Put(5, 3, 'L'); break;
            case "bandana":
                for (int x = 2; x <= 9; x++) Put(x, 3, 'S');
                for (int x = 1; x <= 10; x++) Put(x, 4, 'S');
                Put(4, 3, 'W'); Put(7, 4, 'W'); Put(3, 4, 'W'); Put(11, 4, 'S'); Put(11, 5, 'S');
                break;
            case "horns": Put(2, 0, 'M'); Put(2, 1, 'M'); Put(3, 2, 'M'); Put(9, 0, 'M'); Put(9, 1, 'M'); Put(8, 2, 'M'); break;
            case "visor":
                Put(6, 0, 'R'); Put(6, 1, 'X'); Put(6, 2, 'X');
                for (int x = 1; x <= 10; x++) Put(x, 4, 'X');
                for (int y = 5; y <= 6; y++) for (int x = 2; x <= 9; x++) Put(x, y, 'A');
                Put(1, 5, 'X'); Put(10, 5, 'X'); Put(1, 6, 'X'); Put(10, 6, 'X'); Put(3, 5, 'W');
                break;
            // 9.0.17
            case "beanie":
                Put(6, 0, 'W'); for (int x = 4; x <= 7; x++) Put(x, 1, 'C');
                for (int x = 3; x <= 8; x++) Put(x, 2, 'C');
                for (int x = 2; x <= 9; x++) Put(x, 3, x % 2 == 0 ? 'W' : 'X');
                break;
            case "chef":
                for (int x = 3; x <= 8; x++) Put(x, 0, 'W');
                for (int x = 2; x <= 9; x++) Put(x, 1, 'W');
                for (int x = 3; x <= 8; x++) Put(x, 2, 'W');
                for (int x = 3; x <= 8; x++) Put(x, 3, 'X');
                break;
            case "cowboy":
                for (int x = 4; x <= 7; x++) Put(x, 0, 'N');
                for (int x = 3; x <= 8; x++) Put(x, 1, 'N');
                for (int x = 3; x <= 8; x++) Put(x, 2, x is 3 or 8 ? 'N' : 'O');
                for (int x = 0; x <= 11; x++) Put(x, 3, 'N');
                break;
            case "pirate":
                for (int x = 3; x <= 8; x++) Put(x, 0, 'K');
                for (int x = 1; x <= 10; x++) Put(x, 1, 'K');
                for (int x = 1; x <= 10; x++) Put(x, 2, 'K');
                Put(5, 1, 'W'); Put(6, 1, 'W'); Put(5, 2, 'W'); Put(6, 2, 'W');
                for (int x = 2; x <= 9; x++) Put(x, 3, 'G');
                break;
            case "antlers":
                Put(1, 0, 'N'); Put(3, 0, 'N'); Put(2, 1, 'N'); Put(1, 1, 'N'); Put(2, 2, 'N'); Put(3, 2, 'N');
                Put(10, 0, 'N'); Put(8, 0, 'N'); Put(9, 1, 'N'); Put(10, 1, 'N'); Put(9, 2, 'N'); Put(8, 2, 'N');
                break;
            case "beret":
                Put(6, 0, 'R'); for (int x = 3; x <= 9; x++) Put(x, 1, 'R');
                for (int x = 2; x <= 10; x++) Put(x, 2, 'R');
                break;
            case "knight":
                for (int x = 3; x <= 8; x++) Put(x, 0, 'X');
                for (int x = 2; x <= 9; x++) Put(x, 1, 'X');
                for (int y = 2; y <= 6; y++) { Put(1, y, 'X'); Put(10, y, 'X'); }
                for (int x = 2; x <= 9; x++) { Put(x, 2, 'X'); Put(x, 3, 'X'); Put(x, 4, 'X'); }
                for (int x = 2; x <= 9; x++) Put(x, 5, 'K');
                Put(6, 0, 'R'); Put(7, 0, 'R'); Put(8, 0, 'R');
                break;
            case "catears":
                Put(2, 0, 'K'); Put(3, 1, 'K'); Put(2, 1, 'P'); Put(9, 0, 'K'); Put(8, 1, 'K'); Put(9, 1, 'P');
                for (int x = 2; x <= 9; x++) Put(x, 2, 'K');
                break;
            case "santa":
                Put(10, 2, 'W'); Put(9, 1, 'R'); Put(8, 0, 'R'); for (int x = 4; x <= 8; x++) Put(x, 1, 'R');
                for (int x = 3; x <= 8; x++) Put(x, 2, 'R');
                for (int x = 2; x <= 9; x++) Put(x, 3, 'W');
                break;
            case "propeller":
                Put(4, 0, 'R'); Put(5, 0, 'R'); Put(6, 0, 'G'); Put(7, 0, 'C'); Put(8, 0, 'C');
                for (int x = 3; x <= 8; x++) Put(x, 1, x % 2 == 0 ? 'R' : 'G');
                for (int x = 2; x <= 9; x++) Put(x, 2, x % 3 == 0 ? 'C' : x % 3 == 1 ? 'R' : 'G');
                break;
            case "grad":
                for (int x = 1; x <= 10; x++) Put(x, 1, 'K');
                for (int x = 3; x <= 8; x++) Put(x, 2, 'K');
                Put(6, 0, 'K'); Put(10, 2, 'G'); Put(10, 3, 'G'); Put(10, 4, 'G');
                break;
            case "sprout": Put(6, 2, 'L'); Put(6, 1, 'L'); Put(5, 0, 'L'); Put(4, 0, 'L'); Put(7, 0, 'L'); Put(8, 1, 'L'); break;
        }
        // Accessories, over the outfit (9.0.3).
        switch (accessory)
        {
            case "bowtie": Put(3, 10, 'R'); Put(4, 10, 'R'); Put(5, 10, 'M'); Put(6, 10, 'M'); Put(7, 10, 'R'); Put(8, 10, 'R'); Put(3, 9, 'R'); Put(8, 9, 'R'); break;
            case "glasses": for (int x = 3; x <= 8; x++) Put(x, 4, 'K'); Put(2, 5, 'K'); Put(9, 5, 'K'); Put(5, 5, 'K'); Put(6, 5, 'K'); break;
            case "freckles": Put(2, 8, 'D'); Put(3, 7, 'D'); Put(9, 8, 'D'); Put(8, 7, 'D'); break;
            case "blush": Put(2, 7, 'P'); Put(3, 7, 'P'); Put(8, 7, 'P'); Put(9, 7, 'P'); break;
            case "mustache": Put(4, 7, 'K'); Put(5, 7, 'K'); Put(6, 7, 'K'); Put(7, 7, 'K'); Put(3, 8, 'K'); Put(8, 8, 'K'); break;
            case "monocle": Put(6, 4, 'G'); Put(7, 4, 'G'); Put(8, 4, 'G'); Put(8, 5, 'G'); Put(8, 6, 'G'); Put(9, 7, 'G'); Put(9, 8, 'G'); Put(10, 9, 'G'); break;
            case "star": Put(9, 7, 'G'); Put(8, 8, 'G'); Put(9, 8, 'G'); Put(10, 8, 'G'); Put(9, 9, 'G'); break;
            case "collar": for (int x = 2; x <= 9; x++) Put(x, 10, 'R'); Put(5, 10, 'G'); Put(6, 10, 'G'); Put(5, 11, 'G'); Put(6, 11, 'G'); break;
            case "necktie": Put(5, 10, 'C'); Put(6, 10, 'C'); Put(5, 11, 'C'); Put(4, 10, 'W'); Put(7, 10, 'W'); break;
            case "eyepatch": for (int x = 1; x <= 10; x++) Put(x, 4, 'K'); Put(7, 5, 'K'); Put(8, 5, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); break;
            case "heart": Put(8, 9, 'R'); Put(10, 9, 'R'); Put(8, 10, 'R'); Put(9, 10, 'R'); Put(10, 10, 'R'); Put(9, 11, 'R'); Put(9, 9, 'P'); break;
            case "sweatband": for (int x = 1; x <= 10; x++) Put(x, 3, x % 3 == 0 ? 'R' : 'W'); break;
            case "pearls": for (int x = 2; x <= 9; x++) Put(x, 10, x % 2 == 0 ? 'W' : 'X'); break;
            // 9.0.17
            case "bandaid": Put(8, 7, 'O'); Put(9, 7, 'W'); Put(10, 7, 'O'); Put(9, 8, 'O'); break;
            case "beautymark": Put(8, 8, 'K'); break;
            case "earring": Put(11, 7, 'G'); Put(11, 8, 'G'); break;
            case "medal": Put(5, 9, 'C'); Put(6, 9, 'R'); Put(5, 10, 'G'); Put(6, 10, 'G'); Put(5, 11, 'G'); Put(6, 11, 'G'); break;
            case "lei": for (int x = 1; x <= 10; x++) Put(x, 10, (x % 3) switch { 0 => 'P', 1 => 'G', _ => 'W' }); break;
            case "gem": Put(5, 3, 'A'); Put(6, 3, 'A'); Put(5, 4, 'C'); Put(6, 4, 'C'); break;
            case "clownnose": Put(5, 7, 'R'); Put(6, 7, 'R'); break;
            case "goggles": for (int x = 1; x <= 10; x++) Put(x, 3, 'N'); Put(3, 3, 'A'); Put(4, 3, 'A'); Put(7, 3, 'A'); Put(8, 3, 'A'); Put(5, 3, 'X'); Put(6, 3, 'X'); break;
            case "sparkles": Put(0, 2, 'G'); Put(11, 3, 'W'); Put(0, 9, 'W'); Put(11, 10, 'G'); Put(10, 1, 'G'); break;
            case "bubble": Put(6, 8, 'P'); Put(7, 8, 'P'); Put(6, 9, 'P'); Put(7, 9, 'P'); Put(8, 8, 'P'); Put(8, 9, 'E'); break;
            case "whiskers": Put(1, 7, 'K'); Put(2, 8, 'K'); Put(1, 9, 'K'); Put(10, 7, 'K'); Put(9, 8, 'K'); Put(10, 9, 'K'); break;
            case "bandananeck": for (int x = 2; x <= 9; x++) Put(x, 10, 'C'); Put(5, 11, 'C'); Put(6, 11, 'C'); Put(4, 10, 'W'); Put(7, 10, 'W'); break;
        }
        var body = (Color)ColorConverter.ConvertFromString(colorHex);
        var dark = Color.FromRgb((byte)(body.R * 0.68), (byte)(body.G * 0.68), (byte)(body.B * 0.68));
        var colors = new Dictionary<char, Color>
        {
            ['B'] = body, ['D'] = dark, ['K'] = Color.FromRgb(0x1E, 0x22, 0x2B), ['W'] = System.Windows.Media.Colors.White,
            ['R'] = Color.FromRgb(0xE0, 0x4F, 0x5F), ['P'] = Color.FromRgb(0xF5, 0x8F, 0xA8), ['E'] = Color.FromRgb(0xF5, 0xB5, 0xC5),
            ['A'] = Color.FromRgb(0x6F, 0xE6, 0xFF), ['C'] = Color.FromRgb(0x3E, 0x6F, 0xD9), ['G'] = Color.FromRgb(0xF2, 0xC1, 0x4E),
            ['S'] = Color.FromRgb(0xD9, 0x48, 0x48),
            ['H'] = Color.FromRgb(0x3A, 0x3F, 0x4B), ['V'] = Color.FromRgb(0x6B, 0x4B, 0xD8), ['L'] = Color.FromRgb(0x4C, 0xC2, 0x6A),
            ['M'] = Color.FromRgb(0xB3, 0x26, 0x3A), ['X'] = Color.FromRgb(0xC9, 0xD0, 0xD8),
            ['N'] = Color.FromRgb(0x8B, 0x5A, 0x2B), ['O'] = Color.FromRgb(0xF0, 0x9A, 0x3E),
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
