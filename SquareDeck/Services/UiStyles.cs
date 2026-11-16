using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SquareDeck.Services;

/// <summary>
/// One whole look for the launcher: colors, type, corners, the shape of the buttons, how big the tiles are,
/// how the game's artwork sits behind the home screen, and where things go on it. Pages pick up the colors,
/// type, corners and backdrop through the shared resources set in <see cref="UiStyles.ApplyResources"/>.
/// </summary>
public sealed record UiStyle
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    // Colors: background, text, secondary text, panels/rows, tiles, highlight ("" = the theme's own).
    // Null = the normal light/dark theme colors. Light is used when the theme is set to light (or always, with ForceLight).
    public string[]? Dark { get; init; }
    public string[]? Light { get; init; }
    public bool ForceLight { get; init; }
    public string Font { get; init; } = "Segoe UI";

    public double? TileCorner { get; init; }   // null = the corner setting in Themes
    public double RowCorner { get; init; } = 10;
    public double RingThickness { get; init; } = 4;
    public string Shadow { get; init; } = "soft"; // soft, hard (comic offset), none
    public bool Glow { get; init; }            // a soft glow in the highlight color around the selection
    public bool Glass { get; init; }           // frosted panels with a bright rim over blurred art
    public bool DarkGlass { get; init; }       // with Glass: smoked dark glass instead of white frost
    public bool LightGlass { get; init; }      // with Glass: bright milky glass with dark text
    public string Wallpaper { get; init; } = "";
    // What the style comes with when it's chosen (Settings > Appearance > "Match wallpaper, sounds and music"):
    public string? Mode { get; init; }              // Windows-style theme it comes with: "light", "dark" or "oled" (null = worked out from its colors)
    public string Sounds { get; init; } = "classic";  // a built-in sound set, or "pack:(folder)" for a sound pack
    public string Music { get; init; } = "";          // menu music: "track:(name)", "pack:(folder)", or "" for none
    public string? Glyphs { get; init; }            // button pictures it comes with: "pixel" or "default" (null = your own choice)
    public string Moving { get; init; } = "";         // a moving background (a video in Backgrounds), or "" for none  // a built-in picture behind everything (shown sharp, even with Glass) unless you pick your own
    public string[]? Backdrop { get; init; }   // gradient colors (top to bottom) behind everything when there's no wallpaper
    public string? Stroke { get; init; }       // an outline on panels, rows and tiles ("accent" = the highlight color)
    public double StrokeWidth { get; init; } = 1;

    // Round buttons under the games.
    public string Buttons { get; init; } = "circle"; // circle, square, card (icon + name), tab (pill with name), small
    public double SquareCorner { get; init; } = -1;  // corner for "square" buttons; -1 = the row corner plus a little
    public bool OutlineButtons { get; init; }        // see-through buttons with only an outline
    public bool ButtonDock { get; init; }            // the buttons sit together on one frosted shelf
    public HorizontalAlignment ButtonsAlign { get; init; } = HorizontalAlignment.Center;

    // Home layout: the seven rows (top bar, games, info, buttons, button name, space, footer), the size of the
    // tiles (1 = 270), extra space between them, how much the selected one grows, and where the game's name goes.
    public double[] Rows { get; init; } = { 118, 326, 34, 100, 34, 0, 72 };
    public double TileScale { get; init; } = 1;
    public double TileGap { get; init; }
    public double SelectGrow { get; init; } = 1;
    public VerticalAlignment GamesAlign { get; init; } = VerticalAlignment.Stretch;
    public string Title { get; init; } = "above"; // above the tile, inside (on the tile), big (large, left), row (under the row), none
    public string RowHeader { get; init; } = "";
    public bool PlayPill { get; init; }
    public bool TitleLogo { get; init; }          // with Title = "big": the game's logo picture instead of its name

    // The highlighted game's artwork behind the home screen.
    public double HeroOpacity { get; init; } = 0.28;
    public double HeroBlur { get; init; } = 28;
    public string HeroScrim { get; init; } = "none"; // none, bottom, left
    public bool PageHero { get; init; }              // pages show it too, blurred

    // Whole-screen arrangements (see MainWindow.Layouts):
    //   classic  the rows above (Switch and most styles)
    //   hero     the game's logo, details and Play big at the top, the library row along the bottom (Waves, Xenon, Grids)
    //   toprow   small tiles right under the top bar, the game's name and Play large at the bottom left (Sphere)
    //   sidebar  a navigation sidebar on the left, the game and a "Recently played" row on the right (Desktop)
    //   stage    the selected game large in the middle, its name and buttons centered under it (Stage)
    public string Layout { get; init; } = "classic";
    public bool NavTabs { get; init; }              // the buttons become text tabs (and icons) in the top bar
    public bool TabPill { get; init; }              // with NavTabs: the current tab is a filled pill instead of underlined
    public bool Portrait { get; init; }             // tall 2:3 poster tiles with poster art, instead of square
    public string Actions { get; init; } = "";      // buttons for the selected game: "" none, "icons" (Play, …, ☆), "words" (Play, Add to favorites, More)
    public bool KeyHint { get; init; }              // with Actions: the Play button shows the A / Enter key
    public string Brand { get; init; } = "";        // sidebar heading
    public Point? GrowOrigin { get; init; }         // where the selected tile grows from (null = automatic)
    public bool TopBarFill { get; init; }           // a dark bar behind the top bar
    public string[]? PlayColors { get; init; }      // the Play button's fill (one color, or two for a gradient); null = the highlight color

    public string Footer { get; init; } = "line";    // line, bar (full width), pill (floating), none
    public string Overlay { get; init; } = "none";   // none, scanlines, grid, dots, halftone, carbon, vignette
    public double PageScrim { get; init; } = 0.55;

    /// <summary>Styles that look best with artwork behind them switch it on when chosen.</summary>
    public bool WantsHero => HeroOpacity >= 0.6;

    /// <summary>The theme (Light / Dark / OLED) chosen with this style; null where it suits both.</summary>
    public string? ThemeMode => Mode ?? (ForceLight || (Light != null && Dark == null) ? "light" : Dark != null && Light == null ? "dark" : null);
}

public static class UiStyles
{
    public static readonly UiStyle[] All =
    {
        new()
        {
            Id = "switch", Name = "Switch", Sounds = "classic", Music = "track:Sweetness", Moving = "",
            Description = "The original: round buttons and square tiles on a clean background."
        },
        new()
        {
            Id = "steam", Name = "Steam Big Picture", Font = "Segoe UI Variable Display, Segoe UI", Sounds = "soft", Music = "pack:Steam Big Picture Ambience", Moving = "",
            Description = "Artwork across the screen, a \"Recent games\" row, tabs underneath, and a bar of button hints.",
            Dark = new[] { "#0E1218", "#FFFFFF", "#8F98A0", "#1F2630", "#171C24", "#1A9FFF" },
            TileCorner = 6, RowCorner = 8, RingThickness = 3, Glow = true, Buttons = "tab",
            Rows = new double[] { 78, 476, 52, 58, 0, 0, 46 },
            TileScale = 0.7, TileGap = 2, SelectGrow = 1.06, GamesAlign = VerticalAlignment.Bottom, Title = "row",
            RowHeader = "Recent games",
            HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "bar", PageScrim = 0.85
        },
        new()
        {
            Id = "frosted", Name = "Frosted", Sounds = "glass", Music = "track:Ripples", Moving = "",
            Description = "Thick frosted glass with bright rims: panels, a floating shelf of buttons and a pill of hints over soft, blurred color.",
            Dark = new[] { "#141A2A", "#FFFFFF", "#D9FFFFFF", "#33FFFFFF", "#26FFFFFF", "#9ED8FF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 26, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.06,
            Rows = new double[] { 112, 330, 36, 112, 26, 0, 76 },
            HeroOpacity = 1, HeroBlur = 60, PageHero = true, Footer = "pill", PageScrim = 0.18
        },
        new()
        {
            Id = "frosted-dark", Name = "Frosted Dark", Sounds = "deep", Music = "track:Waves", Moving = "Black Wave",
            Description = "Smoked glass: dark see-through panels with a thin bright rim, a floating shelf of buttons, cool white highlights.",
            Dark = new[] { "#06070A", "#F2F4F8", "#B3FFFFFF", "#80000000", "#66000000", "#BFE8FF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 26, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, DarkGlass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.06,
            Rows = new double[] { 112, 330, 36, 112, 26, 0, 76 },
            HeroOpacity = 1, HeroBlur = 60, PageHero = true, Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "frosted-light", Name = "Frosted Light", Sounds = "chime", Music = "track:Sun Rays", Moving = "", Wallpaper = "preset:6",
            Description = "Bright milky glass with dark text over soft pastel light: airy panels, a white shelf of buttons and a hint pill.",
            Light = new[] { "#E9EEF6", "#1B2233", "#556079", "#C8FFFFFF", "#A8FFFFFF", "#2F6BFF" }, ForceLight = true,
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 26, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, LightGlass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.06,
            Rows = new double[] { 112, 330, 36, 112, 26, 0, 76 },
            HeroOpacity = 0.75, HeroBlur = 60, PageHero = true, Footer = "pill", PageScrim = 0.3
        },
        new()
        {
            Id = "sunset", Name = "Sunset Drive", Sounds = "pack:Dreamy SFX", Music = "track:Sun Rays", Moving = "",
            Description = "Synthwave dusk: a purple-to-orange sky, coral highlights with a soft glow and rounded shapes.",
            Dark = new[] { "#1A0B2E", "#FFF1F8", "#E0B4DD", "#33194F", "#2A1442", "#FF8A5B" },
            Backdrop = new[] { "#140726", "#3D1460", "#A12B6A", "#FF8C42" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Glow = true,
            Buttons = "circle", TileGap = 6, SelectGrow = 1.08, HeroOpacity = 0.3, HeroBlur = 36, HeroScrim = "bottom", Footer = "none",
            Overlay = "vignette", Title = "row", Rows = new double[] { 104, 380, 46, 104, 0, 0, 52 }, TileScale = 0.82, GamesAlign = VerticalAlignment.Bottom
        },
        new()
        {
            Id = "blueprint", Name = "Blueprint", Sounds = "pack:Dreamcast UI SFX", Music = "track:Jumps", Moving = "",
            Description = "Technical drawing: white lines on blueprint blue, a measuring grid, outline buttons and no shadows.",
            Dark = new[] { "#0B3D91", "#F2F7FF", "#A9C4F2", "#0F4BAA", "#0D45A0", "#FFFFFF" },
            Font = "Cascadia Code, Consolas", TileCorner = 4, RowCorner = 4, RingThickness = 2, Shadow = "none", Stroke = "#80FFFFFF",
            Buttons = "square", SquareCorner = 0, OutlineButtons = true, TileGap = 8, HeroOpacity = 0, Overlay = "blueprint"
        },
        new()
        {
            Id = "forest", Name = "Forest", Sounds = "soft", Music = "track:Sweetness", Moving = "Green Blades",
            Description = "Deep woods at dusk: moss greens, a warm amber highlight, rounded tiles with names on them.",
            Dark = new[] { "#0F1A12", "#EEF3E8", "#A3B59A", "#1C2C1F", "#172519", "#E0A84A" },
            Backdrop = new[] { "#0B140D", "#16291A", "#264A2B" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 14, RingThickness = 3,
            Buttons = "small", Rows = new double[] { 104, 400, 40, 90, 34, 0, 52 },
            TileScale = 0.74, TileGap = 6, SelectGrow = 1.08, GamesAlign = VerticalAlignment.Bottom, Title = "inside",
            HeroOpacity = 0.35, HeroBlur = 24, HeroScrim = "bottom", PageHero = true, Footer = "none", PageScrim = 0.72
        },
        new()
        {
            Id = "newsprint", Name = "Newsprint", Sounds = "classic", Music = "", Moving = "",
            Description = "The morning paper: cream paper, black ink, the game's logo as the headline and red ink for highlights.",
            Light = new[] { "#F4F0E6", "#111111", "#5C574C", "#FBF8F1", "#E6E0D2", "#B3261E" }, ForceLight = true,
            Font = "Georgia", TileCorner = 0, RowCorner = 0, RingThickness = 3, Shadow = "none", Stroke = "#111111",
            Buttons = "tab", ButtonsAlign = HorizontalAlignment.Left,
            Rows = new double[] { 70, 196, 260, 70, 0, 0, 56 },
            TileScale = 0.46, TileGap = 6, SelectGrow = 1.12, GamesAlign = VerticalAlignment.Top, Title = "big", TitleLogo = true,
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "ocean", Name = "Deep Ocean", Sounds = "bubble", Music = "track:Waves", Moving = "Blue Wave",
            Description = "Deep blue water with aqua highlights, soft round shapes and your games resting along the bottom.",
            Dark = new[] { "#04131F", "#E6F7FF", "#8FB3C7", "#0B2638", "#08202F", "#2ED3C6" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 22, RowCorner = 18, RingThickness = 3, Glow = true, ButtonDock = true,
            Buttons = "small", Rows = new double[] { 104, 410, 52, 94, 0, 0, 56 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.08, GamesAlign = VerticalAlignment.Bottom, Title = "row",
            HeroOpacity = 0.5, HeroBlur = 30, HeroScrim = "bottom", PageHero = true, Footer = "none", PageScrim = 0.7
        },
        new()
        {
            Id = "sakura", Name = "Sakura", Mode = "light", Sounds = "chime", Music = "track:Sweetness", Moving = "",
            Description = "Cherry-blossom pink, soft and light, with rounded cards. Dark with the theme turns it into a night bloom.",
            Light = new[] { "#FFF1F4", "#3B2230", "#8E6A7A", "#FFFFFF", "#FADCE4", "#E0457B" },
            Dark = new[] { "#1F1418", "#FBE9EF", "#C2A0AE", "#2E1F25", "#28191F", "#FF7AA8" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 24, RowCorner = 20, RingThickness = 3, Glow = true,
            Buttons = "circle", TileGap = 8, SelectGrow = 1.06, HeroOpacity = 0.2, HeroBlur = 44, Footer = "pill", ButtonDock = true
        },
        new()
        {
            Id = "terminal", Name = "Terminal", Sounds = "retro", Music = "track:Jumps", Moving = "",
            Description = "A green phosphor console: glowing text, scan lines, square edges and buttons as labelled commands.",
            Dark = new[] { "#020A04", "#7CFF9B", "#3FA35A", "#08190D", "#061309", "#39FF6A" },
            Font = "Cascadia Mono, Consolas", TileCorner = 2, RowCorner = 2, RingThickness = 2, Glow = true, Shadow = "none", Stroke = "accent",
            Buttons = "tab", ButtonsAlign = HorizontalAlignment.Left, Rows = new double[] { 118, 326, 34, 70, 0, 0, 60 },
            HeroOpacity = 0.12, HeroBlur = 4, Overlay = "scanlines", PageScrim = 0.75
        },
        new()
        {
            Id = "cinema", Name = "Cinema", Sounds = "deep", Music = "track:Ripples", Moving = "",
            Description = "Your games as movie posters: sharp artwork, the game's own logo large, a gold Play button and darkened edges.",
            Dark = new[] { "#0A0908", "#F5EFE6", "#B3A99A", "#1C1A17", "#141210", "#E8B04B" },
            Font = "Georgia", TileCorner = 6, RowCorner = 8, RingThickness = 3, Buttons = "square", SquareCorner = 14,
            ButtonsAlign = HorizontalAlignment.Left,
            Rows = new double[] { 70, 196, 276, 92, 40, 0, 46 },
            TileScale = 0.46, TileGap = 4, SelectGrow = 1.18, GamesAlign = VerticalAlignment.Top, Title = "big", PlayPill = true, TitleLogo = true,
            HeroOpacity = 1, HeroBlur = 0, HeroScrim = "left", PageHero = true, Footer = "none", Overlay = "vignette", PageScrim = 0.82
        },
        new()
        {
            Id = "carbon", Name = "Carbon", Sounds = "pack:Xbox One Sound Pack", Music = "track:Jumps", Moving = "Black Blades",
            Description = "Race-car dashboard: carbon-fiber weave, red highlights, tight corners, button cards and names on the tiles.",
            Dark = new[] { "#111214", "#F2F2F2", "#9A9DA3", "#202226", "#1A1B1E", "#FF3B30" },
            Font = "Bahnschrift SemiBold, Segoe UI Semibold", TileCorner = 8, RowCorner = 8, RingThickness = 3, Glow = true, Buttons = "card",
            Stroke = "#33FFFFFF", Rows = new double[] { 104, 330, 40, 116, 0, 0, 52 },
            TileScale = 0.68, TileGap = 4, SelectGrow = 1.12, GamesAlign = VerticalAlignment.Bottom, Title = "inside",
            HeroOpacity = 0.42, HeroBlur = 10, HeroScrim = "bottom", PageHero = true, Footer = "bar", Overlay = "carbon", PageScrim = 0.75
        },
        new()
        {
            Id = "pastel", Name = "Pastel", Mode = "light", Sounds = "bubble", Music = "track:Sweetness", Moving = "Mint Blades",
            Description = "Soft and friendly: lilac and mint, very round corners, gentle shadows. Light or dark with the theme.",
            Light = new[] { "#F3EEFF", "#2E2A44", "#7D7799", "#FFFFFF", "#E4DCFA", "#7B61FF" },
            Dark = new[] { "#1D1A2C", "#F1EEFF", "#A9A3C8", "#2B2742", "#25213A", "#9DF2D0" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 30, RowCorner = 24, RingThickness = 3,
            Buttons = "circle", TileGap = 8, SelectGrow = 1.06, HeroOpacity = 0.2, HeroBlur = 44, Footer = "pill", ButtonDock = true
        },
        new()
        {
            Id = "pop", Name = "Pop Art", Sounds = "arcade", Music = "track:Jumps", Moving = "",
            Description = "Comic-book bold: sunny yellow, thick black outlines, hard drop shadows and a halftone dot pattern.",
            Light = new[] { "#FFE24A", "#111111", "#3A3A3A", "#FFFFFF", "#FF6F91", "#1E4BFF" },
            ForceLight = true, Font = "Segoe UI Black, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 5,
            Shadow = "hard", Stroke = "#111111", StrokeWidth = 3, Buttons = "square", SquareCorner = 18, TileGap = 8,
            HeroOpacity = 0, Overlay = "halftone"
        },
        new()
        {
            Id = "dot", Name = "Dot Matrix", Sounds = "soft", Music = "", Moving = "",
            Description = "Black, white and one red dot: a faint dot grid, outline buttons, light type and nothing extra.",
            Dark = new[] { "#000000", "#FFFFFF", "#8A8A8A", "#141414", "#0E0E0E", "#D71921" },
            Font = "Segoe UI Variable Display Light, Segoe UI Light, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 2,
            Shadow = "none", Stroke = "#3DFFFFFF", Buttons = "small", OutlineButtons = true, TileScale = 0.82, TileGap = 10,
            Rows = new double[] { 118, 300, 40, 90, 34, 0, 64 }, GamesAlign = VerticalAlignment.Center,
            HeroOpacity = 0, Footer = "none", Overlay = "dots"
        },
        new()
        {
            Id = "neon", Name = "Neon", Sounds = "arcade", Music = "track:Jumps", Moving = "",
            Description = "After-dark arcade: glowing magenta edges, a faint grid, sharp corners and squared type.",
            Dark = new[] { "#07060D", "#F4F1FF", "#A59CD3", "#16132B", "#100E21", "#FF2BD6" },
            Font = "Bahnschrift", TileCorner = 10, RowCorner = 10, RingThickness = 3, Glow = true, Shadow = "none", Stroke = "accent",
            Buttons = "square", SquareCorner = 14, TileGap = 6, SelectGrow = 1.08, HeroOpacity = 0.36, HeroBlur = 22, PageHero = true, Footer = "pill",
            Overlay = "grid", PageScrim = 0.7
        },
        new()
        {
            Id = "minimal", Name = "Minimal", Mode = "dark", Sounds = "soft", Music = "", Moving = "",
            Description = "Quiet and flat: no shadows, small buttons, plenty of space and one warm highlight color.",
            Dark = new[] { "#111111", "#F2F2F2", "#8C8C8C", "#1E1E1E", "#1A1A1A", "#FF6A3D" },
            Light = new[] { "#F6F5F2", "#111111", "#77756F", "#FFFFFF", "#E7E5E0", "#E8501F" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 2, Shadow = "none",
            Buttons = "small", Rows = new double[] { 118, 300, 40, 90, 34, 0, 64 }, TileScale = 0.86, TileGap = 10,
            GamesAlign = VerticalAlignment.Center, HeroOpacity = 0, Footer = "none"
        },
        new()
        {
            Id = "leonida", Name = "Leonida", Sounds = "pack:Dreamy SFX", Music = "track:Sun Rays", Moving = "",
            Description = "Sunset over Ocean Drive: milky glass and deep violet text on a pastel beach skyline, hot-pink highlights, a floating shelf of buttons.",
            Light = new[] { "#FFE6D9", "#2A1846", "#6E4A86", "#C8FFFFFF", "#A8FFFFFF", "#FF3F8E" }, ForceLight = true,
            Wallpaper = Theme.LeonidaWallpaper,
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 24, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, LightGlass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.06,
            Rows = new double[] { 112, 330, 36, 112, 26, 0, 76 },
            HeroOpacity = 0, HeroBlur = 0, Footer = "pill", PageScrim = 0.35
        },
        new()
        {
            Id = "waves", Name = "Waves", Sounds = "pack:Xbox One Sound Pack", Music = "track:Waves", Moving = "",
            Description = "A console dashboard: the game's logo and Play button big over its artwork, tabs along the top, a row of posters underneath, soft waves.",
            Dark = new[] { "#0D1324", "#FFFFFF", "#AEB8D4", "#1B2440", "#151C33", "#3B82F6" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 4, Glow = true,
            Layout = "hero", NavTabs = true, Portrait = true, Actions = "icons", KeyHint = true, TopBarFill = true,
            Buttons = "circle", Rows = new double[] { 70, 290, 0, 0, 0, 0, 40 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.04, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Your library", HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "none",
            Overlay = "waves", PageScrim = 0.8
        },
        new()
        {
            Id = "xenon", Name = "Xenon", Sounds = "pack:Xbox 360 UI", Music = "track:Ripples", Moving = "",
            Description = "Bright and electric: cyan highlights with a glow over the game's artwork, its logo large, tabs on top and posters below.",
            Dark = new[] { "#061018", "#F2FDFF", "#9CC7D6", "#10232E", "#0C1B24", "#22D3EE" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 20, RowCorner = 18, RingThickness = 3, Glow = true,
            Layout = "hero", NavTabs = true, Portrait = true, Actions = "icons", KeyHint = true, TopBarFill = true,
            PlayColors = new[] { "#06B6D4", "#3B82F6" },
            Buttons = "circle", Rows = new double[] { 70, 290, 0, 0, 0, 0, 40 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.05, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Your library", HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "none",
            Overlay = "vignette", PageScrim = 0.8
        },
        new()
        {
            Id = "grids", Name = "Grids", Sounds = "pack:PS5_Sounds", Music = "track:Jumps", Moving = "",
            Description = "The console dashboard on graph paper: square-cut posters, a fine grid over the artwork, lime highlights and tabs on top.",
            Dark = new[] { "#0B0F0A", "#F4FFF0", "#A9B8A3", "#182016", "#121810", "#A3E635" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 4, RowCorner = 4, RingThickness = 3,
            Layout = "hero", NavTabs = true, Portrait = true, Actions = "icons", KeyHint = true, TopBarFill = true,
            Buttons = "square", Rows = new double[] { 70, 290, 0, 0, 0, 0, 40 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.04, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Your library", HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "none",
            Overlay = "grid", PageScrim = 0.8
        },
        new()
        {
            Id = "sphere", Name = "Sphere", Sounds = "pack:PS5_Sounds", Music = "track:Ripples", Moving = "",
            Description = "A media console: small tiles right under the tabs, the highlighted game's artwork filling the screen, its name and Play large at the bottom.",
            Dark = new[] { "#0A0A0E", "#FFFFFF", "#C9C9D2", "#2A2A33", "#1A1A20", "#FFFFFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 24, RingThickness = 5,
            Layout = "toprow", NavTabs = true, Actions = "icons", PlayColors = new[] { "#E6202028" },
            Buttons = "circle", Rows = new double[] { 64, 196, 40, 0, 0, 0, 40 },
            TileScale = 0.5, TileGap = 10, SelectGrow = 1.32, GrowOrigin = new Point(0.5, 0.1), GamesAlign = VerticalAlignment.Top,
            Title = "big", TitleLogo = true, HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "none", PageScrim = 0.82
        },
        new()
        {
            Id = "desktop", Name = "Desktop", Sounds = "soft", Music = "pack:Steam Big Picture Ambience", Moving = "",
            Description = "A PC game launcher: a sidebar with every section, the game's artwork and details up top, Play with a glow and a row of posters.",
            Dark = new[] { "#0C0E22", "#FFFFFF", "#A6AAC8", "#1B1E3C", "#151833", "#7C5CFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 12, RowCorner = 14, RingThickness = 3, Glow = true,
            Layout = "sidebar", Portrait = true, Actions = "icons", Brand = "SQUAREDECK", PlayColors = new[] { "#5B5BFF", "#8B5CF6" },
            Buttons = "circle", Rows = new double[] { 64, 296, 0, 0, 0, 0, 40 },
            TileScale = 0.74, TileGap = 8, SelectGrow = 1.04, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Recently played", HeroOpacity = 1, HeroBlur = 0, HeroScrim = "left", PageHero = true, Footer = "none", PageScrim = 0.85
        },
        new()
        {
            Id = "stage", Name = "Stage", Sounds = "pack:Switch 2 Sounds", Music = "track:Sun Rays", Moving = "",
            Description = "The spotlight: the highlighted game large in the middle over its blurred artwork, its name, buttons and details centered underneath.",
            Dark = new[] { "#1B2533", "#FFFFFF", "#C6CDD8", "#E6202A38", "#1A2230", "#3B82F6" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 12, RingThickness = 4, Glow = true,
            Layout = "stage", NavTabs = true, TabPill = true, Portrait = true, Actions = "words",
            Buttons = "circle", Rows = new double[] { 64, 404, 0, 0, 0, 0, 36 },
            TileScale = 1.06, TileGap = 8, SelectGrow = 1.26, GrowOrigin = new Point(0.5, 0.62), GamesAlign = VerticalAlignment.Center,
            Title = "big", TitleLogo = true, HeroOpacity = 1, HeroBlur = 70, PageHero = true, Footer = "none", PageScrim = 0.6
        },
        new()
        {
            Id = "aurora", Name = "Aurora", Sounds = "glass", Music = "track:Ripples", Moving = "",
            Description = "Northern lights: green, teal and violet light behind frosted glass, white type and a soft glow on everything you pick.",
            Dark = new[] { "#071018", "#F2FFFB", "#BFE6DE", "#33FFFFFF", "#22FFFFFF", "#5CFFC8" },
            Backdrop = new[] { "#050B14", "#0B3B4A", "#19866F", "#5E3AA8" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 24, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.07,
            Rows = new double[] { 108, 334, 40, 112, 26, 0, 74 }, Title = "row",
            HeroOpacity = 0.35, HeroBlur = 60, Footer = "pill", PageScrim = 0.25
        },
        new()
        {
            Id = "mono", Name = "Mono",
            Sounds = "soft", Music = "", Moving = "",
            Description = "A magazine cover in black and white: the game's name huge, thin lines, flat tiles and one bold white highlight.",
            Dark = new[] { "#0A0A0A", "#FFFFFF", "#9A9A9A", "#161616", "#121212", "#FFFFFF" },
            Font = "Segoe UI Variable Display Semibold, Segoe UI Semibold", TileCorner = 6, RowCorner = 6, RingThickness = 3, Shadow = "none",
            Stroke = "#26FFFFFF", Buttons = "tab", ButtonsAlign = HorizontalAlignment.Left,
            Rows = new double[] { 70, 196, 262, 70, 0, 0, 52 }, TileScale = 0.48, TileGap = 6, SelectGrow = 1.1,
            GamesAlign = VerticalAlignment.Top, Title = "big", TitleLogo = true,
            HeroOpacity = 0.5, HeroBlur = 0, HeroScrim = "left", PageHero = true, Footer = "line", PageScrim = 0.85
        },
        new()
        {
            Id = "velvet", Name = "Velvet", Sounds = "deep", Music = "track:Sweetness", Moving = "Gold Wave",
            Description = "A private lounge: deep wine and plum, gold highlights with a warm glow, rounded tiles and a soft spotlight.",
            Dark = new[] { "#1A0A12", "#FBEFE7", "#C9A9AE", "#2C121F", "#24101A", "#E7B85C" },
            Backdrop = new[] { "#12060C", "#2E0E1E", "#4A1530" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 18, RowCorner = 16, RingThickness = 3, Glow = true,
            Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.07, Title = "row",
            Rows = new double[] { 104, 390, 46, 104, 0, 0, 52 }, TileScale = 0.84, GamesAlign = VerticalAlignment.Bottom,
            HeroOpacity = 0.3, HeroBlur = 40, HeroScrim = "bottom", Footer = "pill", Overlay = "vignette", PageScrim = 0.7
        },
        new()
        {
            Id = "tokyo", Name = "Tokyo Night", Sounds = "arcade", Music = "track:Jumps", Moving = "Black Blades",
            Description = "Neon signs after rain: small tiles under the tabs, the game's art across the screen, magenta and cyan glow.",
            Dark = new[] { "#0B0716", "#F6F1FF", "#B5A8D9", "#E61A1430", "#140F24", "#FF3DB8" },
            Font = "Bahnschrift, Segoe UI", TileCorner = 12, RowCorner = 20, RingThickness = 4, Glow = true,
            Layout = "toprow", NavTabs = true, Actions = "icons", PlayColors = new[] { "#FF3DB8", "#7C4DFF" },
            Buttons = "circle", Rows = new double[] { 64, 196, 40, 0, 0, 0, 40 },
            TileScale = 0.5, TileGap = 10, SelectGrow = 1.3, GrowOrigin = new Point(0.5, 0.1), GamesAlign = VerticalAlignment.Top,
            Title = "big", TitleLogo = true, HeroOpacity = 1, HeroBlur = 0, HeroScrim = "bottom", PageHero = true, Footer = "none", Overlay = "grid", PageScrim = 0.82
        },
        new()
        {
            Id = "slate", Name = "Slate", Sounds = "chime", Music = "", Moving = "",
            Description = "Clean and bright like a phone's settings: light grey, white cards, a sidebar of sections and blue highlights.",
            Light = new[] { "#EEF0F4", "#14161B", "#6B7080", "#FFFFFF", "#FFFFFF", "#2F6BFF" }, ForceLight = true,
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3,
            Layout = "sidebar", Portrait = true, Actions = "icons", Brand = "SQUAREDECK",
            Buttons = "circle", Rows = new double[] { 64, 296, 0, 0, 0, 0, 40 },
            TileScale = 0.74, TileGap = 8, SelectGrow = 1.04, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Recently played", HeroOpacity = 0.55, HeroBlur = 0, HeroScrim = "left", PageHero = true, Footer = "none", PageScrim = 0.9
        },
        new()
        {
            Id = "oled", Name = "OLED", Mode = "oled", Sounds = "soft", Music = "", Moving = "",
            Description = "True black for OLED screens: unlit background, thin grey outlines, no glow or artwork behind, crisp white type. Easiest on the battery.",
            Dark = new[] { "#000000", "#FFFFFF", "#8A8A8A", "#000000", "#000000", "#3DDCFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Shadow = "none",
            Stroke = "#2A2A2A", OutlineButtons = true, Buttons = "small", TileGap = 6, SelectGrow = 1.04,
            Rows = new double[] { 104, 360, 60, 96, 30, 0, 52 }, Title = "row", TileScale = 0.9, GamesAlign = VerticalAlignment.Bottom,
            HeroOpacity = 0, HeroBlur = 0, PageHero = false, Footer = "line", PageScrim = 1
        },
        new()
        {
            Id = "pixel-city", Name = "Pixel City", Sounds = "retro", Music = "track:Coffee Shop", Moving = "Lofi City", Glyphs = "pixel",
            Description = "A lo-fi evening in a pixel-art city: square tiles with hard shadows, pixel buttons and coffee-shop music.",
            Dark = new[] { "#140F26", "#FFF4E6", "#C9B6E4", "#E6201838", "#1E1734", "#FF8FB1" },
            Font = "Cascadia Mono, Consolas", TileCorner = 0, RowCorner = 0, RingThickness = 4, Shadow = "hard", Stroke = "accent",
            Buttons = "square", SquareCorner = 0, ButtonsAlign = HorizontalAlignment.Left,
            Rows = new double[] { 70, 196, 222, 108, 0, 0, 56 }, // a row tall enough for the square buttons
            TileScale = 0.46, TileGap = 6, SelectGrow = 1.1, GamesAlign = VerticalAlignment.Top, Title = "big", TitleLogo = true,
            HeroOpacity = 0, Footer = "line", PageScrim = 0.72
        },
        new()
        {
            Id = "pixel-rain", Name = "Pixel Rain", Sounds = "arcade", Music = "track:Gaming Vibes", Moving = "Cyberpunk Rain", Glyphs = "pixel",
            Description = "A pixel-art cyberpunk street in the rain: the game's logo big, tabs on top, posters below, cyan neon and scan lines.",
            Dark = new[] { "#07090F", "#E8FBFF", "#8FB3C7", "#E60C1622", "#0E1A26", "#00F0FF" },
            Font = "Cascadia Mono, Consolas", TileCorner = 0, RowCorner = 0, RingThickness = 4, Glow = true, Shadow = "none",
            Layout = "hero", NavTabs = true, Portrait = true, Actions = "icons", KeyHint = true, TopBarFill = true,
            PlayColors = new[] { "#00F0FF", "#FF2E88" },
            Buttons = "square", SquareCorner = 0, Rows = new double[] { 70, 290, 0, 0, 0, 0, 40 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.04, GamesAlign = VerticalAlignment.Bottom, Title = "big", TitleLogo = true,
            RowHeader = "Your games", HeroOpacity = 0, Footer = "none", Overlay = "scanlines", PageScrim = 0.8
        },
        new()
        {
            Id = "pixel-forest", Name = "Pixel Forest", Sounds = "retro", Music = "track:Meditation", Moving = "Mushroom Forest", Glyphs = "pixel",
            Description = "Glowing mushrooms in a pixel-art forest at night: small tiles under the tabs, soft green glow and calm music.",
            Dark = new[] { "#08130F", "#EAFFF2", "#9CC9B0", "#E60F2219", "#11241C", "#7CFFB2" },
            Font = "Cascadia Mono, Consolas", TileCorner = 0, RowCorner = 0, RingThickness = 4, Glow = true, Shadow = "none",
            Layout = "toprow", NavTabs = true, Actions = "icons", PlayColors = new[] { "#7CFFB2", "#2BB673" },
            Buttons = "square", SquareCorner = 0, Rows = new double[] { 64, 196, 40, 0, 0, 0, 40 },
            TileScale = 0.5, TileGap = 10, SelectGrow = 1.2, GrowOrigin = new Point(0.5, 0.1), GamesAlign = VerticalAlignment.Top,
            Title = "big", TitleLogo = true, HeroOpacity = 0, Footer = "none", PageScrim = 0.8
        },
        new()
        {
            Id = "pixel-dawn", Name = "Pixel Dawn", Sounds = "arcade", Music = "track:Relaxing", Moving = "City Dawn", Glyphs = "pixel",
            Description = "Sunrise over a pixel-art skyline: the highlighted game large in the middle, warm orange light and relaxing music.",
            Dark = new[] { "#1A0F14", "#FFF1E0", "#E0B79C", "#E6261821", "#2A1A22", "#FFB347" },
            Font = "Cascadia Mono, Consolas", TileCorner = 0, RowCorner = 0, RingThickness = 4, Glow = true, Shadow = "none",
            Layout = "stage", NavTabs = true, TabPill = true, Portrait = true, Actions = "words",
            Buttons = "square", SquareCorner = 0, Rows = new double[] { 64, 404, 0, 0, 0, 0, 36 },
            TileScale = 1.06, TileGap = 8, SelectGrow = 1.2, GrowOrigin = new Point(0.5, 0.62), GamesAlign = VerticalAlignment.Center,
            Title = "big", TitleLogo = true, HeroOpacity = 0, Footer = "none", PageScrim = 0.7
        },
        new()
        {
            Id = "retro", Name = "Retro", Sounds = "retro", Music = "track:Jumps", Moving = "",
            Description = "An old amber monitor: scan lines, chunky square edges and typewriter type.",
            Dark = new[] { "#0B0A06", "#FFE7A8", "#B89C5C", "#1E1A0D", "#16130A", "#FFB000" },
            Font = "Cascadia Mono, Consolas", TileCorner = 2, RowCorner = 2, RingThickness = 4, Glow = true, Shadow = "none", Stroke = "accent", Buttons = "square",
            HeroOpacity = 0.18, HeroBlur = 6, Overlay = "scanlines", PageScrim = 0.7
        }
    };

    public static UiStyle Current { get; private set; } = All[0];

    /// <summary>The style with this id. Styles from older versions map to their nearest replacement.</summary>
    public static UiStyle Find(string? id) => All.FirstOrDefault(s => s.Id == Upgrade(id)) ?? All[0];

    public static string Upgrade(string? id) => id switch
    {
        "glass" => "frosted",
        "ps5" => "cinema",
        "xbox" => "switch",
        null or "" => "switch",
        _ => id
    };

    public static void Select(string? id) => Current = Find(id);

    /// <summary>The colors for this style on the current theme, or null to keep the theme's own.</summary>
    public static string[]? PaletteFor(UiStyle style, bool dark) =>
        style.ForceLight ? style.Light : !dark && style.Light != null ? style.Light : style.Dark ?? (dark ? null : style.Light);

    /// <summary>A colorful backdrop for Frosted when there's no wallpaper or moving background to see through.</summary>
    public static Brush GlassBackdrop(UiStyle? style = null)
    {
        var st = style ?? Current;
        if (st.LightGlass)
        {
            // Soft pastel light behind bright glass.
            var light = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            light.GradientStops.Add(new GradientStop(Color.FromRgb(0xC9, 0xE2, 0xFF), 0));
            light.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xD8, 0xFF), 0.5));
            light.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xDD, 0xE4), 1));
            light.Freeze();
            return light;
        }
        if (st.DarkGlass)
        {
            // Darker and moodier behind smoked glass.
            var dark = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            dark.GradientStops.Add(new GradientStop(Color.FromRgb(0x05, 0x0A, 0x18), 0));
            dark.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x2C, 0x52), 0.45));
            dark.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x1E, 0x5C), 0.8));
            dark.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x0A, 0x12), 1));
            dark.Freeze();
            return dark;
        }
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x3B, 0x73), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x8B, 0xC0), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8E, 0x5B, 0xD9), 0.7));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0x8C, 0xB6), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Smoked glass (Frosted Dark): dark and see-through, a touch lighter at the top.</summary>
    public static Brush SmokeBrush(byte top, byte bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(top, 0x1C, 0x1F, 0x27), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 0x06, 0x07, 0x0A), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>A glass shelf or pill for this style (the buttons' shelf, the hints' pill, previews).</summary>
    public static Brush ShelfBrush(UiStyle s) => s.DarkGlass ? SmokeBrush(0x9A, 0x70) : s.LightGlass ? FrostBrush(0xA0, 0x70) : FrostBrush(0x3A, 0x16);

    /// <summary>The bright rim along the edge of glass panels.</summary>
    public static Brush RimBrush(UiStyle s) => s.DarkGlass ? FrostBrush(0x66, 0x10) : s.LightGlass ? FrostBrush(0xFF, 0x90) : FrostBrush(0xB0, 0x30);

    /// <summary>What shows behind everything when there's no wallpaper or moving background: glass colors, a style's own gradient, or nothing.</summary>
    public static Brush? Backdrop(UiStyle s)
    {
        if (s.Wallpaper.Length > 0 && Theme.WallpaperBrush(s.Wallpaper) is { } picture) return picture;
        if (s.Glass) return GlassBackdrop(s);
        if (s.Backdrop is not { Length: > 1 } colors) return null;
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        for (int i = 0; i < colors.Length; i++)
            if (Theme.TryParseArgb(colors[i], out var c)) brush.GradientStops.Add(new GradientStop(c, (double)i / (colors.Length - 1)));
        brush.Freeze();
        return brush;
    }

    /// <summary>Frosted glass: brighter at the top like light catching it, fading toward the bottom.</summary>
    public static Brush FrostBrush(byte top, byte bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(top, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The shared resources every page and the home screen read: corners, edges, button shape, shadows and glow.
    /// Called after the theme colors are set, since some of these are built from them.
    /// </summary>
    public static void ApplyResources()
    {
        var s = Current;
        var res = Application.Current.Resources;
        Color accent = (res["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        Brush bg = res["BgBrush"] as Brush ?? Brushes.Black;

        // Frosted panels catch the light at the top edge.
        if (s.Glass)
        {
            res["CircleBrush"] = s.DarkGlass ? SmokeBrush(0x8C, 0x60) : s.LightGlass ? FrostBrush(0xD0, 0x98) : FrostBrush(0x48, 0x1C);
            res["TileBrush"] = s.DarkGlass ? SmokeBrush(0x70, 0x4C) : s.LightGlass ? FrostBrush(0xB0, 0x78) : FrostBrush(0x38, 0x14);
        }
        Brush circle = res["CircleBrush"] as Brush ?? Brushes.Gray;

        // The shape that rounds the home tiles' corners must be solid, or the cover art shows through it faintly.
        res["TileMaskBrush"] = s.Glass
            ? Frozen(new SolidColorBrush(s.DarkGlass ? Color.FromRgb(0x14, 0x16, 0x1C) : s.LightGlass ? Color.FromRgb(0xE2, 0xE8, 0xF2) : Color.FromRgb(0x4A, 0x55, 0x72)))
            : res["TileBrush"] is SolidColorBrush { Color.A: < 255 } see ? Frozen(new SolidColorBrush(Color.FromRgb(see.Color.R, see.Color.G, see.Color.B)))
            : res["TileBrush"];

        res["RowCorner"] = new CornerRadius(s.RowCorner);
        res["RingThickness"] = new Thickness(s.RingThickness);
        res["TileRingMargin"] = new Thickness(s.TileGap);
        res["TileInnerMargin"] = new Thickness(s.TileGap + s.RingThickness + 4);
        res["TileTitleOpacity"] = s.Title == "above" ? 1.0 : 0.0;
        res["TileNameOpacity"] = s.Title == "inside" ? 1.0 : 0.0;

        Brush stroke = Brushes.Transparent;
        double strokeWidth = 0;
        if (s.Glass)
        {
            // A bright rim along the top, fading out down the sides.
            stroke = s.DarkGlass ? FrostBrush(0x5A, 0x0E) : s.LightGlass ? FrostBrush(0xFF, 0x80) : FrostBrush(0xA0, 0x24);
            strokeWidth = 1.2;
        }
        else if (s.Stroke == "accent")
        {
            stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B)));
            strokeWidth = s.StrokeWidth;
        }
        else if (s.Stroke != null && Theme.TryParseArgb(s.Stroke, out var strokeColor))
        {
            stroke = Frozen(new SolidColorBrush(strokeColor));
            strokeWidth = s.StrokeWidth;
        }
        res["PanelStroke"] = stroke;
        res["PanelStrokeThickness"] = new Thickness(strokeWidth);

        // Dialogs and pop-ups: a little see-through when frosted.
        if (s.Glass && bg is SolidColorBrush solid)
            res["DialogBrush"] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(s.DarkGlass ? 0xEC : 0xE0), solid.Color.R, solid.Color.G, solid.Color.B)));
        else res["DialogBrush"] = bg;

        Effect? Shadow(double soft, double depth, double opacity) => s.Shadow switch
        {
            "hard" => Frozen(new DropShadowEffect { BlurRadius = 0, ShadowDepth = 6, Direction = 315, Opacity = 1, Color = Colors.Black }),
            "none" => null,
            _ => s.Glass ? Frozen(new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.28 })
                         : Frozen(new DropShadowEffect { BlurRadius = soft, ShadowDepth = depth, Opacity = opacity })
        };
        res["TileShadow"] = Perf.Active ? null : Shadow(8, 2, 0.22);
        res["RingGlow"] = s.Glow && !Perf.Active ? Frozen(new DropShadowEffect
        {
            Color = s.Glass && !s.LightGlass ? Colors.White : accent, BlurRadius = s.Glass ? 20 : 24, ShadowDepth = 0, Opacity = s.Glass ? 0.65 : 0.9
        }) : null;
        res["ButtonShadow"] = Perf.Active ? null : s.Glow && !s.Glass ? Frozen(new DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 })
                            : s.OutlineButtons || s.ButtonDock ? null : Shadow(6, 1, 0.18);

        // The round buttons: size, corner, icon size, and whether the name shows on the button.
        double square = s.SquareCorner >= 0 ? s.SquareCorner : s.RowCorner + 4;
        var (w, h, corner, icon, label, margin, orientation) = s.Buttons switch
        {
            "card" => (100.0, 86.0, Math.Max(4, s.RowCorner + 4), 26.0, true, 6.0, Orientation.Vertical),
            "tab" => (152.0, 46.0, 23.0, 16.0, true, 3.0, Orientation.Horizontal), // room for "Web browser"
            "square" => (88.0, 88.0, square, 30.0, false, 6.0, Orientation.Vertical),
            "small" => (70.0, 70.0, 35.0, 24.0, false, 8.0, Orientation.Vertical),
            _ => s.ButtonDock ? (84.0, 84.0, 42.0, 28.0, false, 4.0, Orientation.Vertical) : (96.0, 96.0, 48.0, 32.0, false, 6.0, Orientation.Vertical)
        };
        res["RoundWidth"] = w;
        res["RoundLabelMax"] = s.Buttons == "tab" ? 104.0 : 76.0;
        res["RoundHeight"] = h;
        res["RoundCorner"] = new CornerRadius(corner);
        res["RoundInnerCorner"] = new CornerRadius(Math.Max(0, corner - 9));
        res["RoundIconSize"] = icon;
        res["RoundLabelVisibility"] = label ? Visibility.Visible : Visibility.Collapsed;
        res["RoundOrientation"] = orientation;
        res["RoundMargin"] = new Thickness(margin, 0, margin, 0);
        res["RoundFill"] = s.Buttons == "tab" ? Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)))
                         : s.OutlineButtons ? Brushes.Transparent
                         : s.ButtonDock ? (s.DarkGlass ? SmokeBrush(0x70, 0x50) : s.LightGlass ? FrostBrush(0xE0, 0xB0) : FrostBrush(0x30, 0x10))
                         : circle;
        if (s.OutlineButtons) res["PanelStrokeThickness"] = new Thickness(Math.Max(1, strokeWidth));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static DrawingBrush Tiled(Drawing drawing, double size)
    {
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, size, size), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size), ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>The pattern laid over everything (scan lines, grid, dots, carbon weave, vignette), or null for none.</summary>
    public static Brush? OverlayBrush(UiStyle style)
    {
        switch (style.Overlay)
        {
            case "scanlines":
            {
                var line = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 1)));
                var brush = new DrawingBrush(line)
                {
                    TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 3), ViewportUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, 4, 3), ViewboxUnits = BrushMappingMode.Absolute
                };
                brush.Freeze();
                return brush;
            }
            case "grid":
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0xE5, 0xFF)), 1);
                var group = new GeometryGroup();
                group.Children.Add(new LineGeometry(new Point(0, 0), new Point(48, 0)));
                group.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 48)));
                return Tiled(new GeometryDrawing(null, pen, group), 48);
            }
            case "dots":
            case "halftone":
            {
                // A small dot in each cell: white on black (Dot Matrix) or black on yellow (Pop Art halftone).
                bool light = style.Overlay == "dots";
                double size = light ? 10 : 8;
                var fill = new SolidColorBrush(light ? Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1C, 0, 0, 0));
                var drawings = new DrawingGroup();
                drawings.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
                drawings.Children.Add(new GeometryDrawing(fill, null, new EllipseGeometry(new Point(size / 2, size / 2), light ? 1.1 : 1.6, light ? 1.1 : 1.6)));
                return Tiled(drawings, size);
            }
            case "blueprint":
            {
                // A fine grid with a heavier line every fifth square, like drafting paper.
                var minor = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1);
                var major = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
                var drawings = new DrawingGroup();
                drawings.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 80, 80))));
                for (int i = 1; i < 5; i++)
                {
                    drawings.Children.Add(new GeometryDrawing(null, minor, new LineGeometry(new Point(i * 16, 0), new Point(i * 16, 80))));
                    drawings.Children.Add(new GeometryDrawing(null, minor, new LineGeometry(new Point(0, i * 16), new Point(80, i * 16))));
                }
                drawings.Children.Add(new GeometryDrawing(null, major, new LineGeometry(new Point(0, 0), new Point(80, 0))));
                drawings.Children.Add(new GeometryDrawing(null, major, new LineGeometry(new Point(0, 0), new Point(0, 80))));
                return Tiled(drawings, 80);
            }
            case "carbon":
            {
                // A woven twill: two shades in a checker of slanted bars.
                var drawings = new DrawingGroup();
                var dark = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));
                var light = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
                drawings.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
                drawings.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
                drawings.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
                drawings.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(4, 0, 4, 2))));
                drawings.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 4, 4, 2))));
                return Tiled(drawings, 8);
            }
            case "waves":
            {
                // Soft flowing lines across the screen.
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1280, 720))));
                for (int i = 0; i < 7; i++)
                {
                    var figure = new PathFigure { StartPoint = new Point(-20, 470 + i * 26) };
                    for (int x = 0; x <= 1300; x += 20)
                        figure.Segments.Add(new LineSegment(new Point(x, 470 + i * 26 + Math.Sin(x / 170.0 + i * 0.6) * (34 + i * 5)), true));
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(0x22 - i * 3), 0xFF, 0xFF, 0xFF)), 1.4);
                    group.Children.Add(new GeometryDrawing(null, pen, new PathGeometry(new[] { figure })));
                }
                var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
                brush.Freeze();
                return brush;
            }
            case "vignette":
            {
                var brush = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.85 };
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xA8, 0, 0, 0), 1));
                brush.Freeze();
                return brush;
            }
            default:
                return null;
        }
    }
}
