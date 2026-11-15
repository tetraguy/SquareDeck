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

    public static readonly (string Name, int Radius)[] CornerStyles = { ("Square", 0), ("Rounded", 10), ("Round", 22) };

    /// <summary>One-tap looks: theme, highlight color, wallpaper, tile corners and sounds together.</summary>
    public static readonly (string Name, string Mode, string Accent, string Wallpaper, int Corner, string Sounds)[] Presets =
    {
        ("Classic", "light", "", "", 0, "classic"),
        ("Midnight", "dark", "#5B5BD6", "preset:0", 10, "soft"),
        ("Sunset", "dark", "#F2B01E", "preset:4", 10, "classic"),
        ("Ocean", "dark", "", "preset:2", 22, "soft"),
        ("Forest", "dark", "#2EB872", "preset:3", 10, "soft"),
        ("Retro arcade", "dark", "#EC4899", "preset:5", 0, "retro"),
        ("OLED", "oled", "", "", 10, "soft"),
        ("Daylight", "light", "#1E88E5", "preset:6", 22, "soft")
    };

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

    public static void Apply(Settings settings)
    {
        bool dark = ShouldBeDark(settings);
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
            return;
        }

        // OLED: true black, so those pixels are switched off entirely.
        bool oled = settings.ThemeMode == "oled";
        Set("BgBrush", oled ? "#000000" : dark ? "#2D2D2D" : "#EBEBEB");
        Set("FgBrush", dark ? "#FFFFFF" : "#2D2D2D");
        Set("SubtleBrush", oled ? "#A0A0A0" : dark ? "#B4B4B4" : "#6B6B6B");
        Set("CircleBrush", oled ? "#1C1C1C" : dark ? "#4B4B4B" : "#FFFFFF");
        Set("TileBrush", oled ? "#141414" : dark ? "#3C3C3C" : "#D2D2D2");

        TileCorner = Math.Clamp(settings.TileCorner, 0, 40);
        Application.Current.Resources["TileCorner"] = new CornerRadius(TileCorner);
        Application.Current.Resources["RingCorner"] = new CornerRadius(TileCorner + 5);

        // The user's highlight color wins over the theme's own.
        ApplyAccent(settings);
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
    }
}
