using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SquareDeck.Services;

public static class Theme
{
    /// <summary>Highlight colors offered in Settings (besides the default and a custom one).</summary>
    public static readonly (string Name, string Hex)[] AccentChoices =
    {
        ("Gold", "#F2B01E"), ("Red", "#E53935"), ("Blue", "#1E88E5"), ("Green", "#2EB872"),
        ("Indigo", "#5B5BD6"), ("Pink", "#EC4899"), ("White", "#FFFFFF")
    };

    /// <summary>Built-in home backgrounds: a name and the two colors of its gradient.</summary>
    public static readonly (string Name, string From, string To)[] Wallpapers =
    {
        ("Slate", "#232931", "#4E5563"), ("Dusk", "#2B1B4B", "#B24C63"), ("Ocean", "#0B3C5D", "#1CA7A6"),
        ("Forest", "#12372A", "#6A994E"), ("Sunset", "#C0392B", "#F2994A"), ("Grape", "#3A1C71", "#D76D77"),
        ("Sky", "#8EC5FC", "#E0C3FC"), ("Sand", "#E6DADA", "#C9B79C")
    };

    /// <summary>Built-in picture wallpapers (in the Wallpapers folder next to the app): a name and its file.</summary>
    public static readonly (string Name, string File)[] PictureWallpapers =
    {
        ("Leonida", "Leonida.jpg"), ("Alone", "Alone.jpg"), ("Blue Moon", "Blue Moon.jpg"), ("Cafe", "Cafe.jpg"),
        ("Crimson", "Crimson.jpg"), ("Gold Dust", "Gold Dust.jpg"), ("Lavender", "Lavender.jpg"), ("Next Door", "Next Door.jpg"),
        ("Nostalgia", "Nostalgia.jpg"), ("Jewels", "Jewels.jpg"), ("Terra", "Terra.jpg"),
        ("Me o Tu", "Me o Tu.jpg"), ("The View", "The View.jpg"), ("Tu Reflexion", "Tu Reflexion.jpg"), ("Cloud Pool", "Cloud Pool.jpg"),
        ("Viborl", "Viborl.jpg"), ("Dorado Obscur", "Dorado Obscur.jpg"), ("Windows 95", "Windows 95.jpg")
    };

    public const string LeonidaWallpaper = "image:Leonida.jpg";

    /// <summary>
    /// Color themes for the base colors, dark and light: background, text, faint text, buttons, tiles, highlight.
    /// A UI style with colors of its own still uses those.
    /// </summary>
    public static readonly Dictionary<string, (string Name, string[] Dark, string[] Light)> Palettes = new()
    {
        // Leonida at dusk: violet sky, pastel Art Deco buildings, a hot-pink and orange sunset over peach sand.
        ["gtavi"] = ("GTA VI",
            new[] { "#24143F", "#FFF3EC", "#F4BCCB", "#4A2A6A", "#3A2058", "#FF4F9A" },
            new[] { "#FFE6D9", "#2A1846", "#7B4C8C", "#FFF8F4", "#F6CFC6", "#E8337A" }),
        // Arctic blues and frost.
        ["nord"] = ("Nord",
            new[] { "#2E3440", "#ECEFF4", "#A3ABBA", "#3B4252", "#434C5E", "#88C0D0" },
            new[] { "#ECEFF4", "#2E3440", "#5E6779", "#FFFFFF", "#D8DEE9", "#5E81AC" }),
        // Night violet with a purple highlight.
        ["dracula"] = ("Dracula",
            new[] { "#282A36", "#F8F8F2", "#B4B8D0", "#343746", "#3C3F52", "#BD93F9" },
            new[] { "#F6F5FB", "#282A36", "#6C6F85", "#FFFFFF", "#E4E1F2", "#7C5CE0" }),
        // Deep teal and warm cream.
        ["solarized"] = ("Solarized",
            new[] { "#002B36", "#EEE8D5", "#93A1A1", "#073642", "#0A4250", "#B58900" },
            new[] { "#FDF6E3", "#073642", "#586E75", "#FFFDF6", "#EEE8D5", "#268BD2" }),
        // Soft pastels on plum.
        ["catppuccin"] = ("Catppuccin",
            new[] { "#1E1E2E", "#CDD6F4", "#A6ADC8", "#313244", "#2A2B3C", "#F5C2E7" },
            new[] { "#EFF1F5", "#4C4F69", "#6C6F85", "#FFFFFF", "#DCE0E8", "#8839EF" }),
        // Pine greens.
        ["evergreen"] = ("Evergreen",
            new[] { "#13201A", "#E8F3EC", "#9DB8A8", "#1F3329", "#243B30", "#6FCF97" },
            new[] { "#EEF5EF", "#16281F", "#4F6B5C", "#FFFFFF", "#D5E6DA", "#2E8B57" }),
        // Charcoal and glowing orange.
        ["ember"] = ("Ember",
            new[] { "#1C1210", "#FFEFE6", "#D2A99A", "#2C1C18", "#33211C", "#FF6B35" },
            new[] { "#FFF3EC", "#2B1712", "#7A5246", "#FFFFFF", "#F4D9CC", "#E4572E" }),
        // Navy depths and bright sky blue.
        ["abyss"] = ("Abyss",
            new[] { "#0B1A2A", "#E6F2FF", "#9AB4CF", "#122840", "#163050", "#38BDF8" },
            new[] { "#EAF4FB", "#0C2236", "#4A6580", "#FFFFFF", "#D2E5F3", "#0284C7" }),
        // Cherry blossom pinks.
        ["blossom"] = ("Blossom",
            new[] { "#241821", "#FFEFF5", "#D9AFC2", "#33222F", "#3B2837", "#FF8FB8" },
            new[] { "#FFF2F6", "#3A1F2D", "#8A5C70", "#FFFFFF", "#F8D6E3", "#E0457B" }),
        // 80s neon: indigo night and hot magenta.
        ["synthwave"] = ("Synthwave",
            new[] { "#1A1033", "#F5EEFF", "#BBA8E0", "#2A1B4D", "#2F1F58", "#FF3EA5" },
            new[] { "#F6F0FF", "#22123F", "#6E5A96", "#FFFFFF", "#E5DAFA", "#C2187A" }),
        // Royal: aubergine and gold.
        ["royal"] = ("Royal",
            new[] { "#151019", "#FFF8E7", "#C9BDA2", "#231B2B", "#2B2134", "#E8B84A" },
            new[] { "#FBF7EE", "#231A2E", "#6F6280", "#FFFFFF", "#ECE3D2", "#A87B1E" }),
        // Order per palette: background, text, subtle text, tile, circle/panel, highlight.
        // Fresh mint on deep sea-glass.
        ["mint"] = ("Mint Chip",
            new[] { "#0F1F1C", "#EAFBF5", "#9FC7BB", "#183029", "#1C382F", "#5EEAD4" },
            new[] { "#F0FBF7", "#10302A", "#4E7A6E", "#FFFFFF", "#D4EFE6", "#0D9488" }),
        // Desert dusk: terracotta, sand and sage.
        ["mesa"] = ("Mesa",
            new[] { "#22160F", "#FBEFE3", "#D3B29A", "#33231A", "#3A281D", "#E07A3F" },
            new[] { "#FBF1E6", "#3A2214", "#8A6248", "#FFFFFF", "#F0DCC8", "#C2552A" }),
        // Lavender fields at twilight.
        ["lavender"] = ("Lavender",
            new[] { "#1B1726", "#F3EEFF", "#B9AED6", "#272138", "#2D2640", "#B794F6" },
            new[] { "#F6F2FF", "#2A2140", "#6C5F8C", "#FFFFFF", "#E6DEFA", "#7C3AED" }),
        // Retro handheld greens (four-shade LCD).
        ["gameboy"] = ("Pocket LCD",
            new[] { "#0F380F", "#E0F8D0", "#9BBC0F", "#1E4D1E", "#306230", "#8BAC0F" },
            new[] { "#E0F8D0", "#0F380F", "#306230", "#F2FFE8", "#C4DCA8", "#306230" }),
        // Volcanic black and lava red.
        ["magma"] = ("Magma",
            new[] { "#140C0C", "#FFECE8", "#C99A92", "#241414", "#2C1818", "#FF3B30" },
            new[] { "#FFF1EE", "#2A1010", "#7E4A44", "#FFFFFF", "#F6D4CE", "#D7261E" }),
        // Arctic aurora: midnight blue with green-cyan light.
        ["aurora"] = ("Aurora",
            new[] { "#0A1424", "#E8FFF8", "#9CC2C0", "#12223A", "#162A46", "#34D399" },
            new[] { "#EEF8F6", "#0B2030", "#4A6A72", "#FFFFFF", "#D2ECE8", "#059669" }),
        // Coffee house: espresso, crema and caramel.
        ["espresso"] = ("Espresso",
            new[] { "#1A130F", "#F7EDE4", "#C4AE9C", "#2A1F18", "#30241C", "#D4A373" },
            new[] { "#F8F0E8", "#2B1D14", "#7D6352", "#FFFFFF", "#EADBCB", "#9C6B3C" }),
        // Citrus pop: charcoal with zesty lime and lemon.
        ["citrus"] = ("Citrus",
            new[] { "#15170F", "#F8FBE6", "#BDC79A", "#22261A", "#282D1E", "#C6F432" },
            new[] { "#FAFDEB", "#1F2410", "#636E40", "#FFFFFF", "#E8F0C6", "#65A30D" }),
        // Ocean coral: deep navy with living coral.
        ["coral"] = ("Coral Reef",
            new[] { "#0E1A26", "#FFF0EC", "#A9BCCB", "#16283A", "#1B3044", "#FF7F6E" },
            new[] { "#FFF4F1", "#13283A", "#5D7486", "#FFFFFF", "#F7DDD6", "#E85D4A" }),
        // Monochrome steel with an ice-blue edge.
        ["steel"] = ("Steel",
            new[] { "#16181C", "#EEF1F5", "#A4ABB6", "#22262C", "#2A2E35", "#7DD3FC" },
            new[] { "#F1F3F6", "#1C2026", "#5C6470", "#FFFFFF", "#DDE2E8", "#0EA5E9" })
    };

    public static readonly (string Name, int Radius)[] CornerStyles = { ("Square", 0), ("Rounded", 10), ("Round", 22) };

    /// <summary>One-tap looks: theme, highlight color, wallpaper, tile corners and sounds together.</summary>
    public static readonly (string Name, string Mode, string Accent, string Wallpaper, int Corner, string Sounds, string Palette)[] Presets =
    {
        ("Classic", "light", "", "", 0, "classic", ""),
        ("Midnight", "dark", "#5B5BD6", "preset:0", 10, "soft", ""),
        ("Sunset", "dark", "#F2B01E", "preset:4", 10, "classic", ""),
        ("Ocean", "dark", "", "preset:2", 22, "soft", ""),
        ("Forest", "dark", "#2EB872", "preset:3", 10, "soft", ""),
        ("Retro arcade", "dark", "#EC4899", "preset:5", 0, "retro", ""),
        ("OLED", "oled", "", "", 10, "soft", ""),
        ("Daylight", "light", "#1E88E5", "preset:6", 22, "soft", ""),
        ("GTA VI", "dark", "", LeonidaWallpaper, 22, "soft", "gtavi")
    };

    private static readonly Dictionary<string, Brush> PictureCache = new();

    /// <summary>True when animations should be skipped (Settings > Accessibility).</summary>
    public static bool ReduceMotion { get; private set; }

    /// <summary>Scale applied to pages for larger text, 1.0 to 1.5.</summary>
    public static double TextScale { get; private set; } = 1;

    /// <summary>The wallpaper currently in use, so every page can show it. Null for none.</summary>
    public static Brush? CurrentWallpaper { get; set; }

    /// <summary>Corner radius currently used for tiles (for the ones built in code).</summary>
    public static double TileCorner { get; private set; }

    /// <summary>Brush for a wallpaper setting, or null for none.</summary>
    public static Brush? WallpaperBrush(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return null;
        try
        {
            if (setting.StartsWith("preset:", StringComparison.Ordinal))
            {
                if (!int.TryParse(setting[7..], out var index) || index < 0 || index >= Wallpapers.Length) return null;
                var (_, from, to) = Wallpapers[index];
                var gradient = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to),
                    new Point(0, 0), new Point(1, 1));
                gradient.Freeze();
                return gradient;
            }

            if (setting.StartsWith("image:", StringComparison.Ordinal))
            {
                // A built-in picture: loaded once and shared (styles' previews ask for it too).
                if (PictureCache.TryGetValue(setting, out var cached)) return cached;
                var file = System.IO.Path.Combine(AppContext.BaseDirectory, "Wallpapers", setting[6..]);
                if (!System.IO.File.Exists(file)) return null;
                var picture = ImageLoader.FromFile(file, 1920);
                if (picture == null) return null;
                var pictureBrush = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
                pictureBrush.Freeze();
                PictureCache[setting] = pictureBrush;
                return pictureBrush;
            }

            if (!System.IO.File.Exists(setting)) return null;
            var image = ImageLoader.FromFile(setting, 1920);
            if (image == null) return null;
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether the dark palette is the one currently applied.</summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>True when Windows is set to the dark app theme.</summary>
    public static bool SystemIsDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool ShouldBeDark(Settings settings) => settings.ThemeMode switch
    {
        "dark" or "oled" or "hc-dark" => true,
        "hc-light" => false,
        "light" => false,
        _ => SystemIsDark()
    };

    /// <summary>#RRGGBB or #AARRGGBB.</summary>
    public static bool TryParseArgb(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(text.Trim());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length != 7) return false; // #RRGGBB only
        try
        {
            color = (Color)ColorConverter.ConvertFromString(t);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The highlighted game's artwork, for pages in styles that show it behind them.</summary>
    public static ImageSource? CurrentHero { get; set; }

    public static void Apply(Settings settings)
    {
        UiStyles.Select(settings.UiStyle);
        UiStyles.ApplyOverrides(settings);
        var style = UiStyles.Current;
        bool dark = ShouldBeDark(settings);
        // Styles with only a dark palette are dark whatever the theme is set to.
        if (style.Dark != null && style.Light == null && settings.ThemeMode is not ("hc-dark" or "hc-light")) dark = true;
        if (style.ForceLight && settings.ThemeMode is not ("hc-dark" or "hc-light")) dark = false;
        CurrentIsDark = dark;

        ReduceMotion = settings.ReduceMotion;
        TextScale = Math.Clamp(settings.TextScale, 100, 200) / 100.0;

        // High contrast: pure black and white, no grays, and a highlight that can't be missed.
        if (settings.ThemeMode is "hc-dark" or "hc-light")
        {
            bool onBlack = settings.ThemeMode == "hc-dark";
            Set("BgBrush", onBlack ? "#000000" : "#FFFFFF");
            Set("FgBrush", onBlack ? "#FFFFFF" : "#000000");
            Set("SubtleBrush", onBlack ? "#FFFFFF" : "#000000");
            Set("CircleBrush", onBlack ? "#1A1A1A" : "#E6E6E6");
            Set("TileBrush", onBlack ? "#262626" : "#D0D0D0");
            Set("AccentBrush", onBlack ? "#FFE600" : "#0033CC");
            TileCorner = Math.Clamp(settings.TileCorner, 0, 40);
            Application.Current.Resources["TileCorner"] = new CornerRadius(TileCorner);
            Application.Current.Resources["RingCorner"] = new CornerRadius(TileCorner + 5);
            UiStyles.ApplyResources();
            return;
        }

        // OLED: true black, so those pixels are switched off entirely.
        bool oled = settings.ThemeMode == "oled";
        Set("BgBrush", oled ? "#000000" : dark ? "#2D2D2D" : "#EBEBEB");
        Set("FgBrush", dark ? "#FFFFFF" : "#2D2D2D");
        Set("SubtleBrush", oled ? "#A0A0A0" : dark ? "#B4B4B4" : "#6B6B6B");
        Set("CircleBrush", oled ? "#1C1C1C" : dark ? "#4B4B4B" : "#FFFFFF");
        Set("TileBrush", oled ? "#141414" : dark ? "#3C3C3C" : "#D2D2D2");

        // A color theme (GTA VI...) replaces the grays.
        if (Palettes.TryGetValue(settings.ThemePalette ?? "", out var theme))
        {
            var colors = dark ? theme.Dark : theme.Light;
            Set("BgBrush", oled ? "#000000" : colors[0]);
            Set("FgBrush", colors[1]);
            Set("SubtleBrush", colors[2]);
            Set("CircleBrush", colors[3]);
            Set("TileBrush", colors[4]);
        }

        // The style's own colors (OLED keeps its true black background).
        if (UiStyles.PaletteFor(style, dark) is { } palette)
        {
            Set("BgBrush", oled ? "#000000" : palette[0]);
            Set("FgBrush", palette[1]);
            Set("SubtleBrush", palette[2]);
            Set("CircleBrush", palette[3]);
            Set("TileBrush", palette[4]);
        }

        TileCorner = style.TileCorner ?? Math.Clamp(settings.TileCorner, 0, 40);
        Application.Current.Resources["TileCorner"] = new CornerRadius(TileCorner);
        Application.Current.Resources["RingCorner"] = new CornerRadius(TileCorner + 5);

        // The user's highlight color wins over the theme's own.
        ApplyAccent(settings);
        UiStyles.ApplyResources();
    }

    /// <summary>Puts the highlight color back to the user's choice (or the theme's own).</summary>
    public static void ApplyAccent(Settings settings)
    {
        if (settings.ThemeMode is "hc-dark" or "hc-light")
        {
            Set("AccentBrush", settings.ThemeMode == "hc-dark" ? "#FFE600" : "#0033CC");
            return;
        }
        if (settings.AccentColor == "system" && SystemAccent() is Color system) TintAccent(system);
        else if (TryParseColor(settings.AccentColor, out var accent)) Set("AccentBrush", accent);
        else if (UiStyles.PaletteFor(UiStyles.Current, CurrentIsDark) is { } palette && palette[5].Length > 0) Set("AccentBrush", palette[5]);
        else if (Palettes.TryGetValue(settings.ThemePalette ?? "", out var theme)) Set("AccentBrush", (CurrentIsDark ? theme.Dark : theme.Light)[5]);
        else Set("AccentBrush", CurrentIsDark ? "#20E0D0" : "#0AB9C9");
    }

    /// <summary>The accent color chosen in Windows' own Personalization settings, if it can be read.</summary>
    public static Color? SystemAccent()
    {
        try
        {
            // Stored as 0xAABBGGRR.
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int raw)
                return Color.FromRgb((byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF), (byte)((raw >> 16) & 0xFF));
        }
        catch (Exception) { }
        try { return SystemParameters.WindowGlassColor; }
        catch (Exception) { return null; }
    }

    /// <summary>Temporarily tints the highlight (per-game accent), made bright enough to read on the current theme.</summary>
    public static void TintAccent(Color color)
    {
        // Pull very dark or very pale colors toward something visible on this background.
        double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        if (CurrentIsDark && brightness < 0.45)
        {
            double lift = (0.45 - brightness) / 0.55;
            color = Color.FromRgb((byte)(color.R + (255 - color.R) * lift), (byte)(color.G + (255 - color.G) * lift),
                (byte)(color.B + (255 - color.B) * lift));
        }
        else if (!CurrentIsDark && brightness > 0.6)
        {
            double keep = 0.6 / brightness;
            color = Color.FromRgb((byte)(color.R * keep), (byte)(color.G * keep), (byte)(color.B * keep));
        }
        Set("AccentBrush", color);
    }

    private static void Set(string key, string hex) => Set(key, (Color)ColorConverter.ConvertFromString(hex));

    private static void Set(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
        if (key == "AccentBrush") AllyControl.AccentChanged(); // the Ally's lights, when they follow the accent
    }
}
