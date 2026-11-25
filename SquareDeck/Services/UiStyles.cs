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
    public string[]? RingColors { get; init; }  // the highlight ring as a gradient (Switch); null = the highlight color
    public AdvancedSkin? Skin { get; init; }      // Advanced styles: their artwork (Skins\), drawn over the Switch layout
    public string? IconSkin { get; init; }        // a style of your own: another style's home button icons only (a Skins\ folder), when it has no artwork of its own
    public bool Legendary { get; init; }          // an Advanced style drawn by Claude, shown in Themes > Legendary instead of Advanced
    public bool Mythical { get; init; }           // the top tier: drawn by Claude with the richest artwork, shown in Themes > Mythical
    public string? Unlock { get; init; }          // an Unlockable style's goal ("hours:10", "achievements:100", "finished:10", "streak:7", "nights:5"); see StyleUnlocks
    public string? SkinVariant { get; init; }     // the color variant of that artwork in use (set from settings)
    public bool Glass { get; init; }           // frosted panels with a bright rim over blurred art
    public bool DarkGlass { get; init; }       // with Glass: smoked dark glass instead of white frost
    public bool LightGlass { get; init; }      // with Glass: bright milky glass with dark text
    public bool Gloss { get; init; }           // glossy, bubbly buttons with a shine across their top half (Aero)
    public string Wallpaper { get; init; } = "";
    // What the style comes with when it's chosen (Settings > Appearance > "Match wallpaper, sounds and music"):
    public string? Mode { get; init; }              // Windows-style theme it comes with: "light", "dark" or "oled" (null = worked out from its colors)
    public string Sounds { get; init; } = "classic";  // a built-in sound set, or "pack:(folder)" for a sound pack
    public string Music { get; init; } = "";          // menu music: "track:(name)", "pack:(folder)", or "" for none
    public string? NavSounds { get; init; }           // a sound set for moving the highlight (null = the same as Sounds)
    public string? TileAnimation { get; init; }       // how the selected game stands out (Motion.TileHighlights), or null to leave yours
    /// <summary>The game-start chime the style brings (an id from UiSound.LaunchChimes), or null to leave yours.</summary>
    public string? LaunchChime { get; init; }
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
    public string ButtonLook { get; init; } = "";      // "" (the usual fill), accent (filled with the highlight color), neon (glowing rings)
    public string DockLook { get; init; } = "glass";  // the shelf: glass (frosted), solid, outline, glow
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
    public bool Wide { get; init; }                 // wide 460×215 header-capsule tiles (Settings > Home screen > Game art)
    public string Actions { get; init; } = "";      // buttons for the selected game: "" none, "icons" (Play, …, ☆), "words" (Play, Add to favorites, More)
    public bool KeyHint { get; init; }              // with Actions: the Play button shows the A / Enter key
    public string Brand { get; init; } = "";        // sidebar heading
    public Point? GrowOrigin { get; init; }         // where the selected tile grows from (null = automatic)
    public bool TopBarFill { get; init; }           // a dark bar behind the top bar
    public string[]? PlayColors { get; init; }      // the Play button's fill (one color, or two for a gradient); null = the highlight color

    public string Footer { get; init; } = "line";    // line, bar (full width), pill (floating), none
    public string Overlay { get; init; } = "none";   // none, scanlines, grid, dots, halftone, carbon, vignette, paper, lcd, hud, notebook, cockpit, sheen
    public string Ambient { get; init; } = "";       // gentle animated decoration behind the home screen (see MainWindow.Ambient)
    public double PageScrim { get; init; } = 0.55;

    /// <summary>Styles that look best with artwork behind them switch it on when chosen.</summary>
    public bool WantsHero => HeroOpacity >= 0.6;

    /// <summary>The theme (Light / Dark / OLED) chosen with this style; null where it suits both.</summary>
    public string? ThemeMode => Mode ?? (ForceLight || (Light != null && Dark == null) ? "light" : Dark != null && Light == null ? "dark" : null);
}

public static class UiStyles
{
    /// <summary>
    /// The styles of the Switch look (Settings > Themes): Switch itself and 99 more, all keeping Switch's layout. Each draws the home
    /// screen, colors, corners, tiles and buttons its own way, and brings its own wallpaper, sounds and music. An id
    /// that's no longer here (the separate looks removed in 6.15) falls back to Switch.
    /// </summary>
    public static readonly UiStyle[] All =
    {
        new()
        {
            Id = "switch", Name = "Switch", Sounds = "classic", Music = "track:Sweetness", Moving = "",
            Description = "The original: round buttons and square tiles on a clean background, with a soft blue-to-pink gradient ring around what's highlighted.",
            RingColors = new[] { "#3F7CFF", "#7B6CFF", "#C46BE6", "#FF7FB5", "#55C8FF" }
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
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 26, RowCorner = 20, RingThickness = 3, Glow = true,
            Glass = true, DarkGlass = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.06,
            Rows = new double[] { 112, 330, 36, 112, 26, 0, 76 },
            HeroOpacity = 1, HeroBlur = 60, PageHero = true, Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "frosted-light", Name = "Frosted Light", Sounds = "chime", Music = "track:Sun Rays", Moving = "", Wallpaper = "preset:6",
            Description = "Bright milky glass with dark text over soft pastel light: airy panels, a white shelf of buttons and a hint pill.",
            Light = new[] { "#E9EEF6", "#1B2233", "#556079", "#C8FFFFFF", "#A8FFFFFF", "#2F6BFF" }, ForceLight = true,
            Font = "file:Tindamare.ttf#Tindamare", TileCorner = 26, RowCorner = 20, RingThickness = 3, Glow = true,
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
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 14, RowCorner = 14, RingThickness = 3, Glow = true,
            Buttons = "circle", TileGap = 6, SelectGrow = 1.08, HeroOpacity = 0.3, HeroBlur = 36, HeroScrim = "bottom", Footer = "none",
            Overlay = "vignette", Title = "row", Rows = new double[] { 104, 380, 46, 104, 0, 0, 52 }, TileScale = 0.82, GamesAlign = VerticalAlignment.Bottom
        },
        new()
        {
            Id = "blueprint", Name = "Blueprint", Sounds = "glass", Music = "track:Jumps", Moving = "",
            Description = "Technical drawing: white lines on blueprint blue, a measuring grid, outline buttons and no shadows.",
            Dark = new[] { "#0B3D91", "#F2F7FF", "#A9C4F2", "#0F4BAA", "#0D45A0", "#FFFFFF" },
            Font = "file:Sixrow-Thin.ttf#Sixrow#Sixrow Thin", TileCorner = 4, RowCorner = 4, RingThickness = 2, Shadow = "none", Stroke = "#80FFFFFF",
            Buttons = "circle", OutlineButtons = true, TileGap = 8, HeroOpacity = 0, Overlay = "blueprint"
        },
        new()
        {
            Id = "forest", Name = "Forest", Sounds = "soft", Music = "track:Sweetness", Moving = "Green Blades",
            Description = "Deep woods at dusk: moss greens, a warm amber highlight, rounded tiles with names on them.",
            Dark = new[] { "#0F1A12", "#EEF3E8", "#A3B59A", "#1C2C1F", "#172519", "#E0A84A" },
            Backdrop = new[] { "#0B140D", "#16291A", "#264A2B" },
            Font = "Sitka Subheading, Georgia, Segoe UI", TileCorner = 16, RowCorner = 14, RingThickness = 3,
            Buttons = "circle", Rows = new double[] { 104, 400, 40, 90, 34, 0, 52 },
            TileScale = 0.74, TileGap = 6, SelectGrow = 1.08, GamesAlign = VerticalAlignment.Bottom, Title = "inside",
            HeroOpacity = 0.35, HeroBlur = 24, HeroScrim = "bottom", PageHero = true, Footer = "none", PageScrim = 0.72
        },
        new()
        {
            Id = "newsprint", Name = "Newsprint", Sounds = "classic", Music = "", Moving = "",
            Description = "The morning paper: cream paper, black ink, the game's logo as the headline and red ink for highlights.",
            Light = new[] { "#F4F0E6", "#111111", "#5C574C", "#FBF8F1", "#E6E0D2", "#B3261E" }, ForceLight = true,
            Font = "Georgia", TileCorner = 0, RowCorner = 0, RingThickness = 3, Shadow = "none", Stroke = "#111111",
            Buttons = "circle",
            Rows = new double[] { 70, 196, 260, 70, 0, 0, 56 },
            TileScale = 0.46, TileGap = 6, SelectGrow = 1.12, GamesAlign = VerticalAlignment.Top, Title = "big", TitleLogo = true,
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "ocean", Name = "Deep Ocean", Sounds = "bubble", Music = "track:Waves", Moving = "Blue Wave",
            Description = "Deep blue water with aqua highlights, soft round shapes and your games resting along the bottom.",
            Dark = new[] { "#04131F", "#E6F7FF", "#8FB3C7", "#0B2638", "#08202F", "#2ED3C6" },
            Font = "Corbel, Segoe UI", TileCorner = 22, RowCorner = 18, RingThickness = 3, Glow = true, ButtonDock = true,
            Buttons = "circle", Rows = new double[] { 104, 410, 52, 94, 0, 0, 56 },
            TileScale = 0.78, TileGap = 6, SelectGrow = 1.08, GamesAlign = VerticalAlignment.Bottom, Title = "row",
            HeroOpacity = 0.5, HeroBlur = 30, HeroScrim = "bottom", PageHero = true, Footer = "none", PageScrim = 0.7
        },
        new()
        {
            Id = "sakura", Name = "Sakura", Mode = "light", Sounds = "chime", Music = "track:Sweetness", Moving = "",
            Description = "Cherry-blossom pink, soft and light, with rounded cards. Dark with the theme turns it into a night bloom.",
            Light = new[] { "#FFF1F4", "#3B2230", "#8E6A7A", "#FFFFFF", "#FADCE4", "#E0457B" },
            Dark = new[] { "#1F1418", "#FBE9EF", "#C2A0AE", "#2E1F25", "#28191F", "#FF7AA8" },
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 24, RowCorner = 20, RingThickness = 3, Glow = true,
            Buttons = "circle", TileGap = 8, SelectGrow = 1.06, HeroOpacity = 0.2, HeroBlur = 44, Footer = "pill", ButtonDock = true
        },
        new()
        {
            Id = "terminal", Name = "Terminal", Sounds = "retro", Music = "track:Jumps", Moving = "",
            Description = "A green phosphor console: glowing text, scan lines, square edges and buttons as labelled commands.",
            Dark = new[] { "#020A04", "#7CFF9B", "#3FA35A", "#08190D", "#061309", "#39FF6A" },
            Font = "Cascadia Mono, Consolas", TileCorner = 2, RowCorner = 2, RingThickness = 2, Glow = true, Shadow = "none", Stroke = "accent",
            Buttons = "circle", Rows = new double[] { 118, 326, 34, 70, 0, 0, 60 },
            HeroOpacity = 0.12, HeroBlur = 4, Overlay = "scanlines", PageScrim = 0.75
        },
        new()
        {
            Id = "cinema", Name = "Cinema", Sounds = "deep", Music = "track:Ripples", Moving = "",
            Description = "Your games as movie posters: sharp artwork, the game's own logo large, a gold Play button and darkened edges.",
            Dark = new[] { "#0A0908", "#F5EFE6", "#B3A99A", "#1C1A17", "#141210", "#E8B04B" },
            Font = "file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", TileCorner = 6, RowCorner = 8, RingThickness = 3, Buttons = "circle", 
           
            Rows = new double[] { 70, 196, 276, 92, 40, 0, 46 },
            TileScale = 0.46, TileGap = 4, SelectGrow = 1.18, GamesAlign = VerticalAlignment.Top, Title = "big", PlayPill = true, TitleLogo = true,
            HeroOpacity = 1, HeroBlur = 0, HeroScrim = "left", PageHero = true, Footer = "none", Overlay = "vignette", PageScrim = 0.82
        },
        new()
        {
            Id = "carbon", Name = "Carbon", Sounds = "deep", Music = "track:Jumps", Moving = "Black Blades",
            Description = "Race-car dashboard: carbon-fiber weave, red highlights, tight corners, button cards and names on the tiles.",
            Dark = new[] { "#111214", "#F2F2F2", "#9A9DA3", "#202226", "#1A1B1E", "#FF3B30" },
            Font = "Bahnschrift SemiBold, Segoe UI Semibold", TileCorner = 8, RowCorner = 8, RingThickness = 3, Glow = true, Buttons = "circle",
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
            Font = "Trebuchet MS, Segoe UI", TileCorner = 30, RowCorner = 24, RingThickness = 3,
            Buttons = "circle", TileGap = 8, SelectGrow = 1.06, HeroOpacity = 0.2, HeroBlur = 44, Footer = "pill", ButtonDock = true
        },
        new()
        {
            Id = "pop", Name = "Pop Art", Sounds = "arcade", Music = "track:Jumps", Moving = "",
            Description = "Comic-book bold: sunny yellow, thick black outlines, hard drop shadows and a halftone dot pattern.",
            Light = new[] { "#FFE24A", "#111111", "#3A3A3A", "#FFFFFF", "#FF6F91", "#1E4BFF" },
            ForceLight = true, Font = "Segoe UI Black, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 5,
            Shadow = "hard", Stroke = "#111111", StrokeWidth = 3, Buttons = "circle", TileGap = 8,
            HeroOpacity = 0, Overlay = "halftone"
        },
        new()
        {
            Id = "neon", Name = "Neon", Sounds = "arcade", Music = "track:Jumps", Moving = "",
            Description = "After-dark arcade: glowing magenta edges, a faint grid, sharp corners and squared type.",
            Dark = new[] { "#07060D", "#F4F1FF", "#A59CD3", "#16132B", "#100E21", "#FF2BD6" },
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 10, RowCorner = 10, RingThickness = 3, Glow = true, Shadow = "none", Stroke = "accent",
            Buttons = "circle", TileGap = 6, SelectGrow = 1.08, HeroOpacity = 0.36, HeroBlur = 22, PageHero = true, Footer = "pill",
            Overlay = "grid", PageScrim = 0.7
        },
        new()
        {
            Id = "minimal", Name = "Minimal", Mode = "dark", Sounds = "soft", Music = "", Moving = "",
            Description = "Quiet and flat: no shadows, small buttons, plenty of space and one warm highlight color.",
            Dark = new[] { "#111111", "#F2F2F2", "#8C8C8C", "#1E1E1E", "#1A1A1A", "#FF6A3D" },
            Light = new[] { "#F6F5F2", "#111111", "#77756F", "#FFFFFF", "#E7E5E0", "#E8501F" },
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 10, RowCorner = 10, RingThickness = 2, Shadow = "none",
            Buttons = "circle", Rows = new double[] { 118, 300, 40, 90, 34, 0, 64 }, TileScale = 0.86, TileGap = 10,
            GamesAlign = VerticalAlignment.Center, HeroOpacity = 0, Footer = "none"
        },
        new()
        {
            Id = "oled", Name = "OLED", Mode = "oled", Sounds = "soft", Music = "", Moving = "",
            Description = "True black for OLED screens: unlit background, thin grey outlines, no glow or artwork behind, crisp white type. Easiest on the battery.",
            Dark = new[] { "#000000", "#FFFFFF", "#8A8A8A", "#000000", "#000000", "#3DDCFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Shadow = "none",
            Stroke = "#2A2A2A", OutlineButtons = true, Buttons = "circle", TileGap = 6, SelectGrow = 1.04,
            Rows = new double[] { 104, 360, 60, 96, 30, 0, 52 }, Title = "row", TileScale = 0.9, GamesAlign = VerticalAlignment.Bottom,
            HeroOpacity = 0, HeroBlur = 0, PageHero = false, Footer = "line", PageScrim = 1
        },
        new()
        {
            Id = "pixel-city", Name = "Pixel City", Sounds = "retro", Music = "track:Coffee Shop", Moving = "Lofi City", Glyphs = "pixel",
            Description = "A lo-fi evening in a pixel-art city: square tiles with hard shadows, pixel buttons and coffee-shop music.",
            Dark = new[] { "#140F26", "#FFF4E6", "#C9B6E4", "#E6201838", "#1E1734", "#FF8FB1" },
            Font = "file:pixel-it-5x4.ttf#Pixel It! 5x4", TileCorner = 0, RowCorner = 0, RingThickness = 4, Shadow = "hard", Stroke = "accent",
            Buttons = "circle", 
            Rows = new double[] { 70, 196, 222, 108, 0, 0, 56 }, // a row tall enough for the square buttons
            TileScale = 0.46, TileGap = 6, SelectGrow = 1.1, GamesAlign = VerticalAlignment.Top, Title = "big", TitleLogo = true,
            HeroOpacity = 0, Footer = "line", PageScrim = 0.72
        },
        new()
        {
            Id = "retro", Name = "Retro", Sounds = "retro", Music = "track:Jumps", Moving = "",
            Description = "An old amber monitor: scan lines, chunky square edges and typewriter type.",
            Dark = new[] { "#0B0A06", "#FFE7A8", "#B89C5C", "#1E1A0D", "#16130A", "#FFB000" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 2, RowCorner = 2, RingThickness = 4, Glow = true, Shadow = "none", Stroke = "accent", Buttons = "circle",
            HeroOpacity = 0.18, HeroBlur = 6, Overlay = "scanlines", PageScrim = 0.7
        },
        new()
        {
            Id = "aero", Name = "Aero", Mode = "light", Sounds = "bubble", Music = "track:Aero", Moving = "Aero",
            Description = "Frutiger Aero: glossy bubble buttons, milky glass, aqua highlights and a glowing aurora over water, with a bright, hopeful soundtrack.",
            Light = new[] { "#DDF3FF", "#06263F", "#2F6585", "#C4FFFFFF", "#9CFFFFFF", "#00A8E8" }, ForceLight = true,
            Font = "Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Glow = true,
            Glass = true, LightGlass = true, Gloss = true, Buttons = "circle", ButtonDock = true, TileGap = 6, SelectGrow = 1.07,
            Rows = new double[] { 108, 334, 40, 112, 26, 0, 74 }, Title = "row",
            HeroOpacity = 0, HeroBlur = 0, Footer = "pill", PageScrim = 0.35
        },

        // ---- 6.16: fifty more, each a wallpaper or moving background with matching colors, sounds and music
        new()
        {
            Id = "moonlit-river", Name = "Moonlit River", Sounds = "soft", Music = "track:Ripples", Moving = "Night River", Wallpaper = "",
            Description = "A slow river under the moon: ink-blue panels, pale moonlight text, a soft glow on the tile you're on and gentle ripples in the music.",
            Dark = new[] { "#0B1220", "#EAF2FF", "#8FA3BF", "#18233A", "#141D30", "#7FB8FF" },
            Font = "file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "first-light", Name = "First Light", Sounds = "chime", Music = "track:Sun Rays", Moving = "City Dawn", Wallpaper = "",
            Description = "The city waking up: dusky plum shadows, warm apricot highlights and bright little chimes, like the first coffee of the day.",
            Dark = new[] { "#1A1420", "#FFF4EC", "#C9A99A", "#2A1E2B", "#251A26", "#FF9E6B" },
            Font = "Corbel, Segoe UI", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "rain-circuit", Name = "Rain Circuit", Sounds = "arcade", Music = "track:Gaming Vibes", Moving = "Cyberpunk Rain", Wallpaper = "",
            Description = "Neon rain on a megacity: hot-pink highlights, scanlines over violet steel, arcade blips and a driving beat.",
            Dark = new[] { "#07060F", "#F0EAFF", "#9C8FC4", "#16122A", "#120E22", "#FF2E97" },
            Font = "file:Cyber_Gaming.ttf#Cyber Gaming", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "scanlines", Footer = "line"
        },
        new()
        {
            Id = "cloud-nine", Name = "Cloud Nine", Sounds = "pack:Dreamy SFX", Music = "track:Relaxing", Moving = "Heaven", Wallpaper = "",
            Description = "Floating above the clouds: milky glass, periwinkle highlights, dreamy sound effects and music to drift off to.",
            Light = new[] { "#EEF4FF", "#1B2340", "#5D6A8C", "#C8FFFFFF", "#B0FFFFFF", "#6C8CFF" }, ForceLight = true,
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 20, RowCorner = 20, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glass = true, LightGlass = true, Glow = true, Footer = "pill", PageScrim = 0.35
        },
        new()
        {
            Id = "stargazer", Name = "Stargazer", Sounds = "pack:Dreamy SFX", Music = "track:Meditation", Moving = "Stars and Me", Wallpaper = "",
            Description = "A night under a sky full of stars: deep space panels, lilac starlight on the selection and a calm, spacious soundtrack.",
            Dark = new[] { "#05070F", "#E8ECFF", "#8C95BD", "#11162B", "#0D1224", "#B69CFF" },
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "none"
        },
        new()
        {
            Id = "spore-grove", Name = "Spore Grove", Sounds = "bubble", Music = "track:Jumps", Moving = "Mushroom Forest", Wallpaper = "",
            Description = "A glowing mushroom forest: mossy greens, bioluminescent lime highlights, bubbly sounds and a bouncy tune.",
            Dark = new[] { "#0E1410", "#F0F7E8", "#9DB394", "#1A241B", "#152018", "#9BE15D" },
            Font = "Ink Free, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "meadow", Name = "Meadow", Sounds = "soft", Music = "track:Sweetness", Moving = "Pasture", Wallpaper = "",
            Description = "Rolling green fields on a breezy afternoon: cream paper, grass-green highlights, soft taps and a sweet, easy melody.",
            Light = new[] { "#F4F7EC", "#1F2A16", "#5E6B4E", "#FFFFFF", "#E3EAD2", "#5C9E31" }, ForceLight = true,
            Font = "Candara, Segoe UI", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "geyser-steam", Name = "Geyser Steam", Sounds = "glass", Music = "track:Waves", Moving = "Yellowstone", Wallpaper = "",
            Description = "Steam rising over hot springs: mineral blues and stone greys, a turquoise spring highlight and clear glassy sounds.",
            Dark = new[] { "#10171A", "#EFF7F6", "#96ABA9", "#1B2629", "#172124", "#3FD0C9" },
            Font = "Sitka Heading, Georgia, Segoe UI", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "fountain-plaza", Name = "Fountain Plaza", Sounds = "chime", Music = "track:Recuerdos", Moving = "La Fuente", Wallpaper = "",
            Description = "An old plaza at golden hour: warm stone, brass highlights, elegant serif type, a little chime and a nostalgic guitar.",
            Dark = new[] { "#12100D", "#FBF4E9", "#B9A88E", "#221D16", "#1C1812", "#E8B35A" },
            Font = "Palatino Linotype, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "line"
        },
        new()
        {
            Id = "silver-blade", Name = "Silver Blade", Sounds = "deep", Music = "track:Fraxsu", Moving = "Witcher", Wallpaper = "",
            Description = "Monster-hunter grit: forged steel, a blood-red highlight, sharp corners, heavy serif type and deep, weighty sounds.",
            Dark = new[] { "#0D0E10", "#EDEDE8", "#9C9A92", "#1C1D20", "#17181A", "#C9302C" },
            Font = "Cambria, Segoe UI", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "bar", Shadow = "none"
        },
        new()
        {
            Id = "temple-of-time", Name = "Temple of Time", Sounds = "chime", Music = "track:Jewels", Moving = "Ocarina", Wallpaper = "",
            Description = "A hero's sanctuary: forest-green stone, an emerald glow, storybook serif type and sparkling chimes.",
            Dark = new[] { "#0B1410", "#F2F6E9", "#A3B496", "#17241C", "#132018", "#4FC27A" },
            Font = "Palatino Linotype, Georgia, Segoe UI", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Glow = true, Footer = "line"
        },
        new()
        {
            Id = "plague-doctor", Name = "Plague Doctor", Sounds = "deep", Music = "track:Es Vapour", Moving = "Plague", Wallpaper = "",
            Description = "A candlelit apothecary: soot and parchment, tarnished-brass highlights, old-world type and low, eerie sounds.",
            Dark = new[] { "#0E0D0B", "#EDE6D6", "#A39A86", "#1E1C18", "#191714", "#C49A3A" },
            Font = "file:ESSENTIALO.ttf#ESSENTIALO", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "line", Shadow = "none"
        },
        new()
        {
            Id = "pocket-pals", Name = "Pocket Pals", Sounds = "pack:Switch 2 Sounds", Music = "track:Jumps", TileAnimation = "jelly", Moving = "Pokee", Wallpaper = "",
            Description = "A cheerful creature adventure: sunny cream, cherry-red highlights, chunky round corners with a cartoon shadow and playful console sounds.",
            Light = new[] { "#FFF8E6", "#2A2140", "#6E6488", "#FFFFFF", "#FCEFC8", "#FF5A5F" }, ForceLight = true,
            Font = "file:pixel-it-5x4.ttf#Pixel It! 5x4", TileCorner = 22, RowCorner = 22, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Shadow = "hard", Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "plumbers-holiday", Name = "Plumber's Holiday", Sounds = "arcade", Music = "track:Sweetness", Moving = "Chill Mario", Wallpaper = "",
            Description = "A lazy day off for a famous plumber: night-sky navy, a bright red cap highlight, arcade coins and a sweet, relaxed tune.",
            Dark = new[] { "#0F1A33", "#FFF6E6", "#A9B5D4", "#1C2A4A", "#18243F", "#E52521" },
            Font = "file:Happy_Chicken.ttf#Happy Chicken", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "analog-signal", Name = "Analog Signal", Sounds = "pack:PSP Sounds", Music = "track:Es Vapour", Moving = "Analog", Wallpaper = "",
            Description = "A late-night broadcast on an old tube TV: amber phosphor highlights, monospaced type, scanlines and retro bleeps.",
            Dark = new[] { "#0C0C0C", "#F2EFE6", "#A8A397", "#1C1B19", "#171614", "#FFB000" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "scanlines", Footer = "bar", Shadow = "none"
        },
        new()
        {
            Id = "gilded", Name = "Gilded", Sounds = "glass", Music = "track:Jewels", Moving = "Gold Wave", Wallpaper = "",
            Description = "Liquid gold in slow motion: black lacquer, gold-leaf highlights with a glossy shine and crystal-clear sounds.",
            Dark = new[] { "#110E07", "#FFF7E3", "#C2AE80", "#241D0E", "#1D170B", "#E7B84B" },
            Font = "file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Gloss = true, Footer = "pill"
        },
        new()
        {
            Id = "quicksilver", Name = "Quicksilver", Sounds = "glass", Music = "pack:Steam Big Picture Ambience", Moving = "Silver Wave", Wallpaper = "",
            Description = "Molten chrome: brushed steel greys, a polished silver highlight, glassy sounds and a cool ambient hum.",
            Dark = new[] { "#101215", "#F4F6F9", "#A5ACB6", "#1E2227", "#191C20", "#C9D3E0" },
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Gloss = true, Footer = "line"
        },
        new()
        {
            Id = "velvet-static", Name = "Velvet Static", Sounds = "soft", Music = "track:Fraxsu", Moving = "Fuzzy Blades", Wallpaper = "",
            Description = "Plush and fuzzy: crushed-velvet purples, an orchid glow, soft taps and a hazy, dreamy beat.",
            Dark = new[] { "#120D17", "#F5EEF7", "#B39BB9", "#23192A", "#1D1523", "#D46BFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "soft-focus", Name = "Soft Focus", Sounds = "soft", Music = "track:Meditation", Moving = "Blurry Blades", Wallpaper = "",
            Description = "Everything gently out of focus: hushed blue-greys, a calm sky-blue highlight and quiet, unhurried sounds.",
            Dark = new[] { "#0F1218", "#EEF2F8", "#9AA3B3", "#1B202A", "#171B23", "#8AB4F8" },
            Font = "Corbel, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "none"
        },
        new()
        {
            Id = "gamer-den", Name = "Gamer Den", Sounds = "arcade", Music = "track:Gaming Vibes", Moving = "Gamer Den", Wallpaper = "",
            Description = "A pixel-art bedroom built for gaming: posters, consoles and a glowing monitor, sky-blue panels, a bubblegum-pink highlight, pixel button pictures, arcade blips and a gaming beat.",
            Light = new[] { "#EAF2FF", "#1C2140", "#5B6488", "#FFFFFF", "#DCE8FA", "#FF4FA0" }, ForceLight = true,
            Font = "file:pixel-it-5x4.ttf#Pixel It! 5x4", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glyphs = "pixel", Shadow = "hard", Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "emerald-skies", Name = "Emerald Skies", Sounds = "pack:Switch 2 Sounds", Music = "track:Jumps", Moving = "Emerald", Wallpaper = "",
            Description = "A pixel adventure by the sea: a great green dragon in a summer sky, crisp white and sky blue, an emerald highlight, cheerful console sounds and a bouncy tune.",
            Light = new[] { "#EEF7FF", "#10283A", "#4F6E84", "#FFFFFF", "#DDEBF7", "#1FA35A" }, ForceLight = true,
            Font = "file:Happy_Chicken.ttf#Happy Chicken", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glyphs = "pixel", Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "ember-horizon", Name = "Ember Horizon", Sounds = "deep", Music = "track:Fraxsu", Moving = "Dark Sunset", Wallpaper = "",
            Description = "A pixel sun sinking into a red sea: smoky charcoal, an ember-orange highlight, scanlines, VHS-style lettering, deep sounds and a slow beat.",
            Dark = new[] { "#141212", "#FFF1EA", "#C9A79A", "#CC261C1A", "#CC1E1614", "#FF5A1F" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "scanlines", Footer = "line", Shadow = "none", PageScrim = 0.55
        },
        new()
        {
            Id = "pixel-woods", Name = "Pixel Woods", Sounds = "retro", Music = "track:Ripples", Moving = "Pixel Forest", Wallpaper = "",
            Description = "Deep in a pixel forest: mossy greens and bark browns, a fern-green highlight, pixel lettering and button pictures, soft retro blips and gentle rippling music.",
            Dark = new[] { "#0C140C", "#EAF3E2", "#9DB28E", "#CC172416", "#CC121D12", "#8CD65A" },
            Font = "file:pixel-it-5x4.ttf#Pixel It! 5x4", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glyphs = "pixel", Footer = "line", PageScrim = 0.55
        },
        new()
        {
            Id = "late-night-study", Name = "Late Night Study", Sounds = "soft", Music = "track:Coffee Shop", Moving = "Lofi City", Wallpaper = "",
            Description = "Lo-fi beats to play games to: violet night, a warm desk-lamp highlight, soft clicks and coffee-shop music.",
            Dark = new[] { "#141020", "#F3ECFF", "#A89CC4", "#221B33", "#1C162B", "#FFB86B" },
            Font = "file:Always_be_honest.otf#Always be honest", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", NavSounds = "pack:Purple Lavender Switches"
        },
        new()
        {
            Id = "midnight-valley", Name = "Midnight Valley", Sounds = "soft", Music = "track:Relaxing", Moving = "Midnight", Wallpaper = "",
            Description = "A valley town after dark, seen from the hills: deep navy and starlight, a firefly-green highlight, tall poster lettering, soft sounds and relaxing music.",
            Dark = new[] { "#050A1E", "#E8F0FF", "#8FA0C8", "#CC0E1838", "#CC0A1430", "#3DFFB0" },
            Font = "file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "none", PageScrim = 0.55
        },
        new()
        {
            Id = "coastline-cruise", Name = "Coastline Cruise", Sounds = "arcade", Music = "track:Es Vapour", Moving = "Pixel Ride", Wallpaper = "",
            Description = "A hot-pink coupe parked on the coast road at sunset: violet dusk, a neon-pink highlight, thin 80s lettering, arcade sounds and a vaporwave track.",
            Dark = new[] { "#1A0F2A", "#FFF0FA", "#D3A8D6", "#CC2A1640", "#CC221236", "#FF3FB4" },
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", PageScrim = 0.55
        },
        new()
        {
            Id = "solitude", Name = "Solitude", Sounds = "soft", Music = "track:Meditation", Moving = "", Wallpaper = "image:Alone.jpg",
            Description = "A quiet moment alone: muted slate, a pale blue highlight, nothing extra, just calm sounds and meditative music.",
            Dark = new[] { "#0E1116", "#E9EEF5", "#94A0B2", "#1A1F27", "#151A21", "#9DB7D5" },
            Font = "file:ForbiddenLove.otf#Forbidden Love", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "none", Overlay = "vignette"
        },
        new()
        {
            Id = "blue-moon", Name = "Blue Moon", Sounds = "chime", Music = "track:Ripples", Moving = "", Wallpaper = "image:Blue Moon.jpg",
            Description = "Once in a blue moon: midnight navy, a luminous blue glow, chimes and gentle, rippling music.",
            Dark = new[] { "#070B1A", "#E6ECFF", "#8E9AC6", "#121A33", "#0E152B", "#5DA9FF" },
            Font = "file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "corner-cafe", Name = "Corner Café", Sounds = "pack:Tactile UI", Music = "track:Coffee Shop", Moving = "", Wallpaper = "image:Cafe.jpg",
            Description = "Your favorite table by the window: espresso browns, a caramel highlight, cozy serif type and café music.",
            Dark = new[] { "#17110D", "#F8EFE6", "#BCA592", "#2A1F18", "#231A14", "#D98C4A" },
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "paper", Footer = "line"
        },
        new()
        {
            Id = "cloud-pool", Name = "Cloud Pool", Sounds = "bubble", Music = "track:Relaxing", Moving = "", Wallpaper = "image:Cloud Pool.jpg",
            Description = "A pool in the sky: airy white glass, pool-blue highlights, bubbly sounds and a laid-back soundtrack.",
            Light = new[] { "#EEF6FB", "#16303F", "#577487", "#C8FFFFFF", "#B0FFFFFF", "#2FA4D9" }, ForceLight = true,
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 20, RowCorner = 20, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glass = true, LightGlass = true, Footer = "pill", PageScrim = 0.35
        },
        new()
        {
            Id = "crimson-hour", Name = "Crimson Hour", Sounds = "deep", Music = "track:Fraxsu", Moving = "", Wallpaper = "image:Crimson.jpg",
            Description = "The sky turning red: wine-dark panels, a scarlet glow, deep sounds and a moody beat.",
            Dark = new[] { "#140709", "#FFECEF", "#C9959D", "#2A1014", "#220C10", "#FF3355" },
            Font = "Franklin Gothic Medium, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line"
        },
        new()
        {
            Id = "dorado", Name = "Dorado", Sounds = "glass", Music = "track:Jewels", Moving = "", Wallpaper = "image:Dorado Obscur.jpg",
            Description = "Dark gold and old masters: shadowy umber, antique-gold highlights, classical serif type and crystal sounds.",
            Dark = new[] { "#0D0A06", "#FBF1DC", "#BDA47A", "#211A10", "#1A140B", "#D4A24C" },
            Font = "file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "line"
        },
        new()
        {
            Id = "gold-dust", Name = "Gold Dust", Sounds = "chime", Music = "track:Sun Rays", Moving = "", Wallpaper = "image:Gold Dust.jpg",
            Description = "Glitter in the sunlight: warm black, a sparkling gold glow, bright chimes and sunny music.",
            Dark = new[] { "#120F0A", "#FFF6E2", "#C6B08A", "#251F15", "#1E1910", "#F2C14E" },
            Font = "Constantia, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "jewel-box", Name = "Jewel Box", Sounds = "glass", Music = "track:Jewels", Moving = "", Wallpaper = "image:Jewels.jpg",
            Description = "Gems on velvet: amethyst panels, a glossy magenta-violet highlight, glassy clinks and the Jewels theme.",
            Dark = new[] { "#0C0814", "#F5EEFF", "#B09CCB", "#1D1430", "#170F27", "#E05BFF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Gloss = true, Footer = "pill"
        },
        new()
        {
            Id = "lavender-fields", Name = "Lavender Fields", Sounds = "chime", Music = "track:Sweetness", Moving = "", Wallpaper = "image:Lavender.jpg",
            Description = "Rows of lavender in Provence: soft lilac and white, a violet highlight, light chimes and a sweet melody.",
            Light = new[] { "#F5F0FB", "#2B2140", "#6F6488", "#FFFFFF", "#E9E0F5", "#8E6CEF" }, ForceLight = true,
            Font = "file:ForbiddenLove.otf#Forbidden Love", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", PageScrim = 0.4, NavSounds = "pack:Purple Lavender Switches"
        },
        new()
        {
            Id = "vice-beach", Name = "Vice Beach", Sounds = "arcade", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "image:Leonida.jpg",
            Description = "Sunset on the boulevard: palm-shadow purples, a flamingo-pink glow, bold type, arcade sounds and a cruising beat.",
            Dark = new[] { "#1A0E1C", "#FFF0F6", "#D7A6C0", "#2D1730", "#261328", "#FF5FA2" },
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line"
        },
        new()
        {
            Id = "me-o-tu", Name = "Me o Tú", Sounds = "soft", Music = "track:Recuerdos", Moving = "", Wallpaper = "image:Me o Tu.jpg",
            Description = "A little love letter: soft charcoal, a warm golden highlight, gentle taps and a tender guitar.",
            Dark = new[] { "#12141A", "#F3F1EC", "#A6A39B", "#1F222A", "#1A1D24", "#FFC46B" },
            Font = "file:Always_be_honest.otf#Always be honest", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "next-door", Name = "Next Door", Sounds = "classic", Music = "track:Sweetness", Moving = "", Wallpaper = "image:Next Door.jpg",
            Description = "The friendly neighborhood: plain and comfortable greys, a leafy green highlight, the classic sounds and a sweet tune.",
            Dark = new[] { "#121417", "#F1F3F5", "#9CA3AB", "#1F2328", "#1A1D21", "#6BCB77" },
            Font = "Trebuchet MS, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "nostalgia", Name = "Nostalgia", Sounds = "pack:PSP Sounds", Music = "track:Recuerdos", Moving = "", Wallpaper = "image:Nostalgia.jpg",
            Description = "Old photos in a shoebox: sepia browns, a faded-orange highlight, paper grain, retro bleeps and memories in the music.",
            Dark = new[] { "#1B1410", "#FCEFDC", "#C7AB8E", "#2D221B", "#261D17", "#E9965A" },
            Font = "Georgia, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "paper", Footer = "line"
        },
        new()
        {
            Id = "terra", Name = "Terra", Sounds = "deep", Music = "track:Es Vapour", Moving = "", Wallpaper = "image:Terra.jpg",
            Description = "Earth and clay: sandstone and terracotta, a burnt-orange highlight, grounded sounds and a warm, hazy track.",
            Dark = new[] { "#14110C", "#F5EFE3", "#B3A68C", "#262016", "#201B12", "#C97B3D" },
            Font = "Corbel, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line"
        },
        new()
        {
            Id = "the-view", Name = "The View", Sounds = "glass", Music = "track:Waves", Moving = "", Wallpaper = "image:The View.jpg",
            Description = "From the top of the hill: open-sky blues, a clear highlight, glassy sounds and waves in the distance.",
            Dark = new[] { "#0D1218", "#EEF3F8", "#96A5B5", "#19212B", "#151C25", "#6FC3FF" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill"
        },
        new()
        {
            Id = "reflections", Name = "Reflections", Sounds = "chime", Music = "track:Ripples", Moving = "", Wallpaper = "image:Tu Reflexion.jpg",
            Description = "Your reflection in still water: ink indigo, a soft violet glow, chimes and rippling music.",
            Dark = new[] { "#0B0D14", "#EDEFFA", "#959BBB", "#181C2B", "#141725", "#A78BFA" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "none"
        },
        new()
        {
            Id = "viborl", Name = "Viborl", Sounds = "bubble", Music = "track:Fraxsu", Moving = "", Wallpaper = "image:Viborl.jpg",
            Description = "Vivid and alive: deep teal panels, a jade-green glow, bubbly pops and a groovy track.",
            Dark = new[] { "#0A1210", "#E9F6F1", "#8DB1A6", "#13241F", "#0F1E1A", "#2FD3A3" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill"
        },
        new()
        {
            Id = "start-menu", Name = "Start Menu", Sounds = "retro", Music = "track:Es Vapour", Moving = "", Wallpaper = "image:Windows 95.jpg",
            Description = "It's 1995: grey bevelled windows, a navy highlight, square corners, chunky shadows, retro beeps and a vaporwave haze.",
            Light = new[] { "#D4D0C8", "#000000", "#404040", "#FFFFFF", "#C0C0C0", "#000080" }, ForceLight = true,
            Font = "Microsoft Sans Serif, Tahoma, Segoe UI", TileCorner = 0, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, RowCorner = 0, Shadow = "hard", Footer = "bar", PageScrim = 0.5
        },
        new()
        {
            Id = "blossom-night", Name = "Blossom Night", Sounds = "pack:Dreamy SFX", Music = "track:Sun Rays", TileAnimation = "heartbeat", Moving = "Fall Blossom", Wallpaper = "",
            Description = "Blossoms drifting under a starry sky: midnight blue and twilight violet, a petal-pink glow, an elegant serif, dreamy sounds and warm music.",
            Dark = new[] { "#0E1030", "#FDEFFF", "#B9A6D9", "#CC1C1A48", "#CC16163E", "#FF7AD9" },
            Font = "file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill", PageScrim = 0.5
        },
        new()
        {
            Id = "grape-soda", Name = "Grape Soda", Sounds = "bubble", Music = "track:Jumps", Moving = "", Wallpaper = "preset:5",
            Description = "Fizzy and sweet: grape-purple gradient, a bubblegum highlight, glossy buttons, bubbly pops and a bouncy tune.",
            Dark = new[] { "#1E0E33", "#F7EEFF", "#BFA2DB", "#2E1A4B", "#271541", "#FF8BD1" },
            Font = "file:Happy_Chicken.ttf#Happy Chicken", TileCorner = 20, RowCorner = 20, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Gloss = true, Footer = "pill"
        },
        new()
        {
            Id = "sandstone", Name = "Sandstone", Sounds = "soft", Music = "track:Coffee Shop", Moving = "", Wallpaper = "preset:7",
            Description = "Warm desert sand: oatmeal and linen, a caramel highlight, a gentle humanist font, soft taps and café music.",
            Light = new[] { "#F5EFE6", "#2E2418", "#75664F", "#FFFFFF", "#EBE2D3", "#C8873A" }, ForceLight = true,
            Font = "Candara, Segoe UI", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", PageScrim = 0.4
        },
        new()
        {
            Id = "nap-time", Name = "Nap Time", Sounds = "soft", Music = "track:Meditation", NavSounds = "marimba", TileAnimation = "float", Moving = "Sleepy", Wallpaper = "",
            Description = "A sleepy pixel friend dozing under a big summer cloud: sky blue and cloud white, a sunflower-yellow highlight, hand-drawn lettering, soft sounds and calm music.",
            Light = new[] { "#EAF5FF", "#1B2A3A", "#5A7088", "#FFFFFF", "#DCEBF7", "#E8A21C" }, ForceLight = true,
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 22, RowCorner = 22, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glyphs = "pixel", Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "seaside-summer", Name = "Seaside Summer", Sounds = "bubble", Music = "track:Waves", Moving = "Summer Steam", Wallpaper = "",
            Description = "A summer ride along the beach path: sand, sea-glass blue and white, a scooter-red highlight, bubbly sounds and the sound of the waves.",
            Light = new[] { "#F1FAFB", "#173040", "#557684", "#FFFFFF", "#DFEFF2", "#F25C54" }, ForceLight = true,
            Font = "Trebuchet MS, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", PageScrim = 0.4
        },
        new()
        {
            Id = "pine-lodge", Name = "Pine Lodge", Sounds = "deep", Music = "track:Meditation", Moving = "", Wallpaper = "preset:3",
            Description = "A cabin in the pines: forest greens, a lantern-gold highlight, rustic serif type, deep sounds and calm music.",
            Dark = new[] { "#0F1E16", "#EFF6EE", "#9EB7A2", "#1A2E22", "#15271C", "#E2B04A" },
            Font = "Georgia, Segoe UI", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "line"
        },
        new()
        {
            Id = "dreamcore", Name = "Dreamcore", Sounds = "pack:Dreamy SFX", Music = "track:Dreamcore", Moving = "Dreamcore", Wallpaper = "",
            Description = "Liminal and half-remembered: an endless tiled hall over open water, ultramarine panels with a faint tile grid, a warm amber highlight like the lone sign in the distance, dreamy sounds and a hazy track. Somewhere you've been before, but never quite.",
            Dark = new[] { "#081640", "#F5F1E8", "#A9B6D9", "#B3132A6A", "#B30F2259", "#F07A2A" },
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 4, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "grid", Footer = "pill", PageScrim = 0.6
        },
        new()
        {
            Id = "acid-garden", Name = "Acid Garden", Sounds = "deep", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "image:Bad Trip.jpg",
            Description = "A blacklight poster come to life: neon doodles crawling over black, near-black panels so it stays readable, an acid-green highlight, scratchy lettering and a pounding beat.",
            Dark = new[] { "#0B0712", "#F6F0FF", "#B9A4D9", "#EB180F28", "#EB140C22", "#39FF88" },
            Font = "file:ESSENTIALO.ttf#ESSENTIALO", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", PageScrim = 0.7
        },
        new()
        {
            Id = "melt", Name = "Melt", Sounds = "bubble", Music = "track:Es Vapour", Moving = "", Wallpaper = "image:Too Much Acid.jpg",
            Description = "Everything's melting: 60s swirls of cobalt, tangerine and gold, deep-blue panels, a marigold highlight, bouncy lettering, bubbly pops and a woozy vaporwave track.",
            Dark = new[] { "#0E1340", "#FFF6E0", "#CFC6A8", "#EB1A2266", "#EB151C58", "#FFB020" },
            Font = "file:Happy_Chicken.ttf#Happy Chicken", TileCorner = 24, RowCorner = 22, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "pill", PageScrim = 0.7
        },
        new()
        {
            Id = "kaleidoscope", Name = "Kaleidoscope", Sounds = "pack:Dreamy SFX", Music = "track:Jewels", TileAnimation = "rainbow", Moving = "", Wallpaper = "image:Your Fault.jpg",
            Description = "Rainbow spirals staring back out of the dark: smoky black panels, an electric-cyan highlight that pops against the fire, tall poster lettering and dreamy sounds.",
            Dark = new[] { "#0A0505", "#FFF3E6", "#D1B49A", "#EB201010", "#EB1A0C0C", "#2FE0FF" },
            Font = "file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "vignette", Footer = "pill", PageScrim = 0.7
        },
        new()
        {
            Id = "black-bug", Name = "Black Bug", Mode = "oled", Sounds = "retro", Music = "track:Fraxsu", Moving = "Black Bug", Wallpaper = "",
            Description = "A Y2K desktop that never sleeps: blinking windows, logs and world clocks in pure black and white, true black for OLED screens, a white highlight, scanlines, VHS lettering and retro blips.",
            Dark = new[] { "#000000", "#F2F2F2", "#8C8C8C", "#E6111111", "#E60A0A0A", "#F2F2F2" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 2, RowCorner = 2, RingThickness = 2, Buttons = "circle",
            HeroOpacity = 0, Overlay = "scanlines", Footer = "bar", Shadow = "none", Stroke = "#3A3A3A", PageScrim = 0.6
        },
        new()
        {
            Id = "silver-lining", Name = "Silver Lining", Mode = "light", Sounds = "glass", Music = "track:Aero", Moving = "", Wallpaper = "image:Silver Lining.jpg",
            Description = "Chrome bubbles floating over a pink-lilac sea: milky glass panels, glossy bubble buttons, a candy-pink highlight, airy futuristic lettering, glassy sounds and the Aero soundtrack.",
            Light = new[] { "#F1EEFA", "#231E3A", "#6A6488", "#C8FFFFFF", "#A8FFFFFF", "#F04FB8" }, ForceLight = true,
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 22, RowCorner = 20, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Glass = true, LightGlass = true, Gloss = true, Footer = "pill", PageScrim = 0.35
        },
        new()
        {
            Id = "waivy-web", Name = "Waivy Web", Sounds = "glass", Music = "track:Ripples", Moving = "", Wallpaper = "image:Waivy Web.jpg",
            Description = "A warped wireframe grid straight off a 2000s screensaver: graphite panels with a thin outline, a lilac highlight, thin retro-future lettering, glassy sounds and rippling music.",
            Dark = new[] { "#141416", "#F5F5F7", "#A0A0A8", "#E6222226", "#E61B1B1F", "#C9A8FF" },
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Stroke = "accent", Footer = "line", PageScrim = 0.55
        },
        new()
        {
            Id = "arcade-cabinet", Name = "Arcade Cabinet", Sounds = "arcade", Music = "track:Gaming Vibes", NavSounds = "laser", TileAnimation = "lockon", Moving = "", Wallpaper = "",
            Description = "Insert coin: a dark cabinet with chasing marquee bulbs top and bottom, scan lines, neon-magenta highlights, pixel lettering and arcade blips.",
            Dark = new[] { "#120A1E", "#FFF0FF", "#C3A6D6", "#241638", "#1D112E", "#FF2BD6" },
            Font = "file:pixel-it-5x4.ttf#Pixel It! 5x4", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glyphs = "pixel", Ambient = "marquee", Overlay = "scanlines", Glow = true, Footer = "bar", Backdrop = new[] { "#1B0C2E", "#0A0614" }, PageScrim = 0.55
        },
        new()
        {
            Id = "lullaby", Name = "Lullaby", Sounds = "soft", Music = "track:Meditation", NavSounds = "marimba", TileAnimation = "float", Moving = "", Wallpaper = "",
            Description = "Bedtime under a pixel sky: deep indigo, blinking square stars, a moon-yellow highlight, soft hand lettering and calm music.",
            Dark = new[] { "#0C1030", "#EEF0FF", "#A6ADD9", "#1A2050", "#151A44", "#FFD866" },
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 22, RowCorner = 22, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "pixelstars", Footer = "pill", Backdrop = new[] { "#141A4A", "#070A20" }, PageScrim = 0.55
        },
        new()
        {
            Id = "autumn-walk", Name = "Autumn Walk", Sounds = "soft", Music = "track:Coffee Shop", Moving = "", Wallpaper = "",
            Description = "A walk through the park in October: rust, amber and cream, leaves drifting down the screen, a pumpkin-orange highlight and café music.",
            Dark = new[] { "#2A160C", "#FFF1E2", "#D9B49A", "#3D2214", "#341D11", "#FF8A2B" },
            Font = "Sitka Subheading, Georgia, Segoe UI", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "leaves", Overlay = "vignette", Footer = "pill", Backdrop = new[] { "#5A2A12", "#1E0F07" }, PageScrim = 0.55
        },
        new()
        {
            Id = "mission-control", Name = "Mission Control", Sounds = "deep", Music = "track:Fraxsu", NavSounds = "tick", TileAnimation = "lockon", Moving = "", Wallpaper = "preset:0",
            Description = "Houston, we have a library: slate panels in cockpit framing, a radar sweeping the corner, a signal-green highlight and deep, steady sounds.",
            Dark = new[] { "#141A1F", "#E8F4EE", "#93A9A0", "#1E2A30", "#19232A", "#38F28C" },
            Font = "Bahnschrift, Segoe UI", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "radar", Overlay = "cockpit", Glow = true, Footer = "line", Shadow = "none", PageScrim = 0.55
        },
        new()
        {
            Id = "glitch-pop", Name = "Glitch Pop", Sounds = "retro", Music = "track:Es Vapour", NavSounds = "laser", TileAnimation = "jelly", Moving = "", Wallpaper = "",
            Description = "A bad signal that looks good: black to magenta, slices that flash cyan and pink, scan lines, VHS lettering and retro blips.",
            Dark = new[] { "#0A0410", "#FFFFFF", "#C9A6D6", "#1C0B26", "#16081F", "#00F0FF" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "glitch", Overlay = "scanlines", Glow = true, Footer = "line", Backdrop = new[] { "#3A0A4A", "#05020A" }, PageScrim = 0.55
        },
        new()
        {
            Id = "party-time", Name = "Party Time", Sounds = "bubble", Music = "track:Jumps", NavSounds = "kalimba", TileAnimation = "jelly", Moving = "", Wallpaper = "",
            Description = "Balloons out: a bright lemonade backdrop, chunky confetti bobbing around the edges, polka dots, a cherry highlight and bubbly pops.",
            Light = new[] { "#FFF7DB", "#2B1E3A", "#6E5E86", "#FFFFFF", "#FFEDB0", "#FF4D6D" }, ForceLight = true,
            Font = "file:orange_juice_2.0.ttf#orange juice", TileCorner = 24, RowCorner = 24, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "confetti", Overlay = "dots", Shadow = "hard", Footer = "pill", Backdrop = new[] { "#FFF3B8", "#FFD6E7" }, PageScrim = 0.4
        },
        new()
        {
            Id = "scrapbook", Name = "Scrapbook", Sounds = "chime", Music = "track:Sweetness", NavSounds = "tick", Moving = "", Wallpaper = "",
            Description = "Notebook pages full of memories: lined paper, stickers and washi tape stuck at odd angles, a marker-blue highlight, neat handwriting and chimes.",
            Light = new[] { "#FBF8F1", "#2A2A33", "#6B6A78", "#FFFFFF", "#F1ECDF", "#2F6FEB" }, ForceLight = true,
            Font = "file:Always_be_honest.otf#Always be honest", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "stickers", Overlay = "notebook", Footer = "line", Shadow = "hard", PageScrim = 0.4
        },
        new()
        {
            Id = "treasure-map", Name = "Treasure Map", Sounds = "classic", Music = "track:Recuerdos", NavSounds = "marimba", Moving = "", Wallpaper = "",
            Description = "X marks the spot: aged parchment, an inked compass rose swaying in the corner, a sealing-wax red highlight and an elegant serif.",
            Light = new[] { "#F3E6C8", "#3A2614", "#7A5C3E", "#FBF2DD", "#E9D7AF", "#B23A2A" }, ForceLight = true,
            Font = "file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "compass", Overlay = "paper", Footer = "line", Backdrop = new[] { "#F6E9CB", "#E2CC9C" }, PageScrim = 0.4
        },
        new()
        {
            Id = "neon-highway", Name = "Neon Highway", Sounds = "arcade", Music = "track:Es Vapour", NavSounds = "laser", TileAnimation = "rainbow", Moving = "", Wallpaper = "",
            Description = "Racing into the sunset grid: a purple sky, a neon floor rushing toward you, a hot-pink highlight, thin 80s lettering and a vaporwave track.",
            Dark = new[] { "#14062A", "#FFEFFF", "#D6A8E6", "#250D44", "#1E0A3A", "#FF2E97" },
            Font = "file:Synth2-Thin.ttf#Synth#Synth Thin", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "horizon", Glow = true, Footer = "line", Backdrop = new[] { "#3B0B5E", "#FF5E8A", "#14062A" }, PageScrim = 0.55
        },
        new()
        {
            Id = "cozy-cabin", Name = "Cozy Cabin", Sounds = "soft", Music = "track:Coffee Shop", NavSounds = "marimba", TileAnimation = "heartbeat", Moving = "", Wallpaper = "",
            Description = "Snowed in by the fire: warm walnut panels, soft lights drifting like embers, paper grain, a candle-gold highlight and café music.",
            Dark = new[] { "#1E140F", "#FFF3E6", "#D2B7A2", "#2E1F17", "#271A13", "#FFB648" },
            Font = "Constantia, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "bokeh", Overlay = "paper", Footer = "pill", Backdrop = new[] { "#3A2216", "#140C08" }, PageScrim = 0.55
        },
        new()
        {
            Id = "hologram", Name = "Hologram", Sounds = "pack:EDGE UI Sounds", Music = "track:Aero", Moving = "", Wallpaper = "",
            Description = "Iridescent and see-through: milky glass over a pearly gradient, glassy shapes floating by, a lilac highlight and airy lettering.",
            Light = new[] { "#F2F0FF", "#221E3C", "#6A6490", "#C8FFFFFF", "#A8FFFFFF", "#8A5CFF" }, ForceLight = true,
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 22, RowCorner = 22, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "float", Glass = true, LightGlass = true, Glow = true, Footer = "pill", PageScrim = 0.35, Backdrop = new[] { "#CFE6FF", "#F4D8FF", "#FFE3EC" }
        },
        new()
        {
            Id = "starship-bridge", Name = "Starship Bridge", Sounds = "deep", Music = "track:Fraxsu", NavSounds = "laser", TileAnimation = "lockon", Moving = "", Wallpaper = "",
            Description = "On the bridge at warp: a heads-up display, status lights pulsing along the edge, deep space blues, a cyan highlight and deep sounds.",
            Dark = new[] { "#050B18", "#E6F6FF", "#8FB2C9", "#0E1A2E", "#0A1526", "#2FD8FF" },
            Font = "file:Cyber_Gaming.ttf#Cyber Gaming", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "status", Overlay = "hud", Glow = true, Footer = "bar", Shadow = "none", Backdrop = new[] { "#0B1B36", "#02050C" }, PageScrim = 0.55
        },
        new()
        {
            Id = "sparkle-club", Name = "Sparkle Club", Sounds = "bubble", Music = "track:Jewels", NavSounds = "kalimba", TileAnimation = "rainbow", Moving = "", Wallpaper = "",
            Description = "Glitter and gloss: pink chrome, twinkling sparkles, glossy bubble buttons, a sheen across the screen and a candy-pink highlight.",
            Light = new[] { "#FFF0F7", "#3A1630", "#8A5A7C", "#FFFFFF", "#FFDDEE", "#FF3FA4" }, ForceLight = true,
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 24, RowCorner = 24, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "sparkle", Overlay = "sheen", Gloss = true, Glow = true, Footer = "pill", Backdrop = new[] { "#FFD6EC", "#F3E1FF" }, PageScrim = 0.4
        },
        new()
        {
            Id = "third-eye", Name = "Third Eye", Sounds = "deep", Music = "track:Fraxsu", Moving = "", Wallpaper = "image:Chased.jpg",
            Description = "Wide awake and seeing everything: a wild psychedelic poster, near-black panels so it stays readable, a lime highlight, tall poster lettering and deep sounds.",
            Dark = new[] { "#0A0806", "#FFF4E6", "#D6B99A", "#EB1A1410", "#EB140F0C", "#B6F23D" },
            Font = "file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "vignette", Footer = "pill", PageScrim = 0.7
        },
        new()
        {
            Id = "rewind", Name = "Rewind", Sounds = "pack:PSP Sounds", Music = "track:Recuerdos", Moving = "", Wallpaper = "",
            Description = "Press play: the deep blue of a VCR's screen, a worn tape rolling by with its counter running, scan lines, a white highlight and camcorder lettering.",
            Dark = new[] { "#0018A8", "#FFFFFF", "#B8C4FF", "#0A26C4", "#0820B8", "#FFFFFF" },
            Font = "file:HomeVideo-Regular.ttf#Home Video", TileCorner = 2, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "vhs", Overlay = "scanlines", Footer = "none", Shadow = "none", Backdrop = new[] { "#0022C8", "#000E78" }, PageScrim = 0.55
        },
        new()
        {
            Id = "ocean-breeze", Name = "Ocean Breeze", Sounds = "glass", Music = "track:Waves", NavSounds = "droplet", TileAnimation = "float", Moving = "", Wallpaper = "preset:2",
            Description = "A salty breeze off the water: deep teal to turquoise, glassy shapes drifting like bubbles, a sea-foam highlight and the sound of waves.",
            Dark = new[] { "#06303A", "#E8FBFF", "#93C9D2", "#0C4250", "#093846", "#5FF2D9" },
            Font = "file:Tindamare.ttf#Tindamare", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "float", Glow = true, Footer = "pill", PageScrim = 0.55
        },
        new()
        {
            Id = "desert-dusk", Name = "Desert Dusk", Sounds = "soft", Music = "track:Sun Rays", Moving = "", Wallpaper = "preset:4",
            Description = "The last light over the dunes: a red-to-amber sky, sand-colored panels, a soft dark edge, a turquoise highlight like desert stone and warm music.",
            Dark = new[] { "#2A120C", "#FFF2E6", "#E0B8A0", "#3E1C12", "#34170F", "#3FD6C6" },
            Font = "Sitka Heading, Georgia, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "vignette", Footer = "line", PageScrim = 0.55
        },
        new()
        {
            Id = "starfall", Name = "Starfall", Sounds = "pack:Dreamy SFX", Music = "track:Dreamcore", Moving = "", Wallpaper = "preset:1",
            Description = "Wishing on falling stars: plum dusk to rose, twinkling sparkles, a starlight highlight, sweet hand lettering and dreamy sounds.",
            Dark = new[] { "#1E1030", "#FFF0FA", "#D8A8CC", "#2E1A46", "#26153C", "#FFE38A" },
            Font = "file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", TileCorner = 20, RowCorner = 20, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Ambient = "sparkle", Glow = true, Footer = "pill", PageScrim = 0.55
        },
        new()
        {
            Id = "rainy-window", Name = "Rainy Window", Sounds = "soft", Music = "track:Coffee Shop", NavSounds = "droplet", Moving = "Cyberpunk Rain", Wallpaper = "",
            Description = "Watching the rain from inside: the city blurred through wet glass, smoky panels, a lamp-amber highlight, handwritten notes and café music.",
            Dark = new[] { "#0C0E14", "#EEF0F6", "#9AA0B4", "#CC161A24", "#CC12151E", "#FFB547" },
            Font = "file:Always_be_honest.otf#Always be honest", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, DarkGlass = true, Footer = "pill", PageScrim = 0.55
        },
        new()
        {
            Id = "matcha", Name = "Matcha", Sounds = "pack:Tactile UI", Music = "track:Relaxing", TileAnimation = "float", Moving = "", Wallpaper = "",
            Description = "A slow afternoon tea: whisked-green and oat-milk cream, paper grain, a matcha highlight, a friendly humanist font and relaxing music.",
            Light = new[] { "#F3F1E4", "#2A3324", "#66705A", "#FFFFFF", "#E4E8D0", "#5E9B3A" }, ForceLight = true,
            Font = "Candara, Segoe UI", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "paper", Footer = "pill", Backdrop = new[] { "#F4F1E2", "#DCE6C4" }, PageScrim = 0.4
        },
        new()
        {
            Id = "bubblegum", Name = "Bubblegum", Sounds = "bubble", Music = "track:Sweetness", NavSounds = "droplet", TileAnimation = "jelly", Moving = "", Wallpaper = "",
            Description = "Pop! Pastel pink and sky blue, glossy bubble buttons with a shine, a hot-pink highlight, bubbly hand lettering and bubbly sounds.",
            Light = new[] { "#FFF0F6", "#3A1A34", "#86607E", "#FFFFFF", "#FFDCEB", "#FF4FA0" }, ForceLight = true,
            Font = "file:orange_juice_2.0.ttf#orange juice", TileCorner = 26, RowCorner = 26, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Gloss = true, Footer = "pill", Shadow = "hard", Backdrop = new[] { "#FFD3E8", "#CDEBFF" }, PageScrim = 0.4
        },
        new()
        {
            Id = "phosphor", Name = "Phosphor", Sounds = "retro", Music = "track:Fraxsu", NavSounds = "tick", TileAnimation = "lockon", Moving = "", Wallpaper = "",
            Description = "An old green-screen terminal: true black, glowing phosphor green, scan lines, thin dot-matrix capitals and retro blips.",
            Dark = new[] { "#000000", "#7CFF8A", "#3FAE4E", "#0A140C", "#071009", "#39FF6A" },
            Font = "file:Sixrow-Thin.ttf#Sixrow#Sixrow Thin", TileCorner = 2, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Mode = "oled", Overlay = "scanlines", Glow = true, Footer = "none", Shadow = "none", Stroke = "#1C4A24", PageScrim = 0.55
        },
    };

    /// <summary>
    /// Advanced styles (Settings > Themes > Advanced): the Switch layout dressed in a whole UI kit's artwork. Rows,
    /// buttons, chips, dialogs, the round buttons, switches, the mouse pointer and (in some) the home button icons are
    /// drawn with the kit's own pictures, with its own sounds and font. Some come in color variants.
    /// </summary>
    public static readonly UiStyle[] Advanced =
    {
        new()
        {
            Id = "adv-meridian", Name = "Meridian", Sounds = "chime", NavSounds = "glass", Music = "track:Meditation", Moving = "", Wallpaper = "image:Meridian.jpg",
            Description = "A celestial atlas: midnight-blue cards framed in engraved gold, pop-ups with a ruled header and astrolabe corners, astrolabe rings for the Home buttons, switches that turn a new moon full, check boxes that light a star, gold line icons, a compass-needle pointer, and a hand-plotted star chart with constellations behind it all.",
            Dark = new[] { "#070D1F", "#F3E9D2", "#A9B3CF", "#101A36", "#0E1730", "#E8C27A" }, Mode = "dark",
            Font = "file:Marcellus-Regular.ttf#Marcellus", TileCorner = 8, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#E8C27A", "#FFF1C9", "#C9973F", "#FFE3A3", "#E8C27A" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#0B1638", "#060B1E" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("meridian"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-atelier", Name = "Atelier", Sounds = "kalimba", NavSounds = "marimba", Music = "track:Sun Rays", Moving = "", Wallpaper = "image:Atelier.jpg",
            Description = "An Art Nouveau poster: cream cards with sage frames and copper whiplash curls, pop-ups with a sage header and little flowers, copper-ringed buttons, switches where a bud blooms into a flower, leaf check boxes, a pen-nib pointer and a poster of petals, ribbons and lilies.",
            Light = new[] { "#EFE6D2", "#3B3328", "#7A6E5C", "#F7F0E2", "#E6DAC2", "#B8734A" }, ForceLight = true,
            Font = "file:Philosopher-Regular.ttf#Philosopher", TileCorner = 10, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#EFE6D2", "#E3D5B8" }, PageScrim = 0.35,
            Legendary = true, Skin = new AdvancedSkin("atelier"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-abyss", Name = "Abyss", Sounds = "droplet", NavSounds = "bubble", Music = "track:Waves", Moving = "", Wallpaper = "image:Abyss.jpg",
            Description = "The deep sea, glowing: dark glass with glowing cyan rims, bubble buttons, an anglerfish's lure that lights up for switches, glowing pearls for check boxes, a little fish for a pointer, and jellyfish drifting through light from far above.",
            Dark = new[] { "#020B10", "#E6FFFC", "#8FC9C4", "#0B2A36", "#08202A", "#4DF5E3" }, Mode = "dark",
            Font = "file:Play-Regular.ttf#Play", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "float", Backdrop = new[] { "#07323E", "#010508" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("abyss"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-greenhouse", Name = "Greenhouse", Sounds = "marimba", NavSounds = "soft", Music = "track:Relaxing", Moving = "", Wallpaper = "image:Greenhouse.jpg",
            Description = "A Victorian glasshouse: frosted glass panes in wrought-iron frames with brass rivets, brass plate buttons, porthole home buttons, switches where a seed sprouts in a terracotta pot, leaf check boxes and ferns, under arched iron and big leaves.",
            Light = new[] { "#E4EDE2", "#1E2B22", "#5C6E60", "#F3F7F1", "#D7E3D5", "#3E7B4F" }, ForceLight = true,
            Font = "file:CrimsonText-Regular.ttf#Crimson Text", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#DCEBD9", "#A9C9A5" }, PageScrim = 0.35,
            Legendary = true, Skin = new AdvancedSkin("greenhouse"), TileAnimation = "lift"
        },
        new()
        {
            Id = "adv-origami", Name = "Origami", Sounds = "pack:Tactile UI", Music = "track:Sweetness", Moving = "", Wallpaper = "image:Origami.jpg",
            Description = "Folded paper: paper cards with a folded corner, coral paper buttons, pop-ups whose header is a folded flap, pinwheel home buttons, switches that fold into a pinwheel, a paper-airplane pointer, and layered paper mountains with cranes.",
            Light = new[] { "#F3EDE4", "#2E3447", "#7A7F8F", "#FFFFFF", "#E9E1D6", "#F27D72" }, ForceLight = true,
            Font = "file:VarelaRound-Regular.ttf#Varela Round", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#FBEFE6", "#F2E2D8" }, PageScrim = 0.3,
            Legendary = true, Skin = new AdvancedSkin("origami"), TileAnimation = "lift"
        },
        new()
        {
            Id = "adv-sumie", Name = "Sumi-e", Sounds = "kalimba", NavSounds = "soft", Music = "track:Thinking of You", Moving = "", Wallpaper = "image:Sumie.jpg",
            Description = "Ink and brush: rice paper with brushed ink borders, a red seal on every pop-up, ensō circles for the home buttons, switches stamped with a red seal, brushed check marks, a brush pointer and misty ink mountains under a red sun.",
            Light = new[] { "#EFE9DD", "#1C1A17", "#6E675C", "#F7F3EA", "#E2DBCC", "#C0392B" }, ForceLight = true,
            Font = "file:Spectral-Regular.ttf#Spectral", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "none", Backdrop = new[] { "#F6F1E6", "#ECE4D3" }, PageScrim = 0.3,
            Legendary = true, Skin = new AdvancedSkin("sumie"), TileAnimation = "steady"
        },
        new()
        {
            Id = "adv-forge", Name = "Forge", Sounds = "deep", NavSounds = "tick", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "image:Forge.jpg",
            Description = "Iron and fire: riveted iron plates with glowing seams, pop-ups headed by a bar of hot metal, cog home buttons glowing at the heart, switches that light a coal, molten icons, a pointer with a glowing tip, and sparks rising from the anvil.",
            Dark = new[] { "#141110", "#F4E6D8", "#A8968A", "#241F1C", "#1E1A18", "#FF7A1A" }, Mode = "dark",
            Font = "file:SairaCondensed-Regular.ttf#Saira Condensed", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "hard", Backdrop = new[] { "#0E0A09", "#1A0F0A" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("forge"), TileAnimation = "lockon"
        },
        new()
        {
            Id = "adv-glacier", Name = "Glacier", Sounds = "glass", NavSounds = "droplet", Music = "track:Daybreak", Moving = "", Wallpaper = "image:Glacier.jpg",
            Description = "Ice under the aurora: deep-blue glass framed in ice with frost growing from the corners, faceted buttons, crystal home buttons, switches that turn from a snowflake to aurora light, snowflake check boxes, an ice-shard pointer, and the northern lights over a glacier.",
            Dark = new[] { "#0A1626", "#EAF6FF", "#9DB5C9", "#14273D", "#102034", "#7FE7D6" }, Mode = "dark",
            Font = "file:DidactGothic-Regular.ttf#Didact Gothic", TileCorner = 12, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#050B18", "#0E2238" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("glacier"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-cartographer", Name = "Cartographer", Sounds = "marimba", NavSounds = "tick", Music = "track:Recuerdos", Moving = "", Wallpaper = "image:Cartographer.jpg",
            Description = "An old sea chart: parchment cards ruled in ink with compass points, pop-ups under a red ribbon with a compass rose, compass home buttons, switches sealed with red wax, X-marks-the-spot check boxes, a quill pointer and a hand-drawn chart with a route, rhumb lines and a sea serpent.",
            Light = new[] { "#E6D3AD", "#3A2A18", "#7A6346", "#F1E4C6", "#DCC79D", "#A83A2A" }, ForceLight = true,
            Font = "file:IMFeENrm28P.ttf#IM FELL English", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#E6D3AD", "#D4BC8A" }, PageScrim = 0.35,
            Legendary = true, Skin = new AdvancedSkin("cartographer"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-sweetshop", Name = "Sweet Shop", Sounds = "bubble", NavSounds = "bubble", Music = "track:Jumps", Moving = "", Wallpaper = "image:SweetShop.jpg",
            Description = "A sweet shop: pink cards piped with frosting and sprinkles, glossy mint candy buttons, pop-ups with dripping icing, donut home buttons, swirl-lollipop switches, candy check boxes, a candy-cane pointer and a pastel sky of macarons, lollipops and sprinkles.",
            Light = new[] { "#FFEFF5", "#5A2E3E", "#A36F82", "#FFFFFF", "#FFE0EC", "#FF5C9A" }, ForceLight = true,
            Font = "file:Sniglet-Regular.ttf#Sniglet", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", Shadow = "soft", Backdrop = new[] { "#FFE9F2", "#E6F7FF" }, PageScrim = 0.3,
            Legendary = true, Skin = new AdvancedSkin("sweetshop"), TileAnimation = "jelly"
        },
        new()
        {
            Id = "adv-gatsby", Name = "Gatsby", Sounds = "chime", NavSounds = "tick", Music = "track:Coffee Shop", Moving = "", Wallpaper = "image:Gatsby.jpg",
            Description = "Art Deco black and gold: black lacquer cards with stepped gold corners, pop-ups with gold sunburst fans, sunburst home buttons, diamond switches, gold diamond check boxes, a geometric gold pointer, and a sunburst rising behind a skyline of stepped towers.",
            Dark = new[] { "#0B0B0C", "#F3E6C4", "#B8A57A", "#17161A", "#121114", "#D4AF37" }, Mode = "dark",
            Font = "file:PoiretOne-Regular.ttf#Poiret One", TileCorner = 2, RowCorner = 2, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Backdrop = new[] { "#0D0C0F", "#050506" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("gatsby"), TileAnimation = "glow"
        },
        // 7.0: ten more drawn by Claude for the Legendary tab.
        new()
        {
            Id = "adv-stainedglass", Name = "Stained Glass", Sounds = "chime", NavSounds = "glass", Music = "track:At Rest", Moving = "", Wallpaper = "image:StainedGlass.jpg",
            Description = "A cathedral window at night: smoky glass cards with jewel panes set in lead at the corners, pop-ups headed by a row of lancet windows, rose-window home buttons, switches that light a little rose, a ruby pointer, and a great gothic window glowing over dark stone.",
            Dark = new[] { "#0C0A14", "#F6EEDC", "#B3A9C2", "#1A1626", "#16121F", "#E8B04A" }, Mode = "dark",
            Font = "file:Cinzel-Regular.ttf#Cinzel", TileCorner = 6, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#C2183A", "#E3A21A", "#1E8C4E", "#1F4FB8", "#7A2BA8", "#C2183A" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Backdrop = new[] { "#120E18", "#0A0810" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("stainedglass"), TileAnimation = "glow"
        },
        new()
        {
            Id = "adv-koipond", Name = "Koi Pond", Sounds = "droplet", NavSounds = "droplet", Music = "track:Ripples", Moving = "", Wallpaper = "image:KoiPond.jpg",
            Description = "A garden pond seen from above: dark-water cards with a lily pad in the corner and ripples, pop-ups with a lotus in bloom, lily-pad home buttons, switches where a lotus bud opens, koi-orange check boxes, a koi pointer, and koi gliding under lily pads.",
            Dark = new[] { "#06201E", "#EAF7EE", "#9CC2B4", "#0D2F2B", "#0B2925", "#FF8A3D" }, Mode = "dark",
            Font = "file:Comfortaa-Regular.ttf#Comfortaa", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "float", Backdrop = new[] { "#0D3B35", "#05201C" }, PageScrim = 0.45,
            Legendary = true, Skin = new AdvancedSkin("koipond"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-blueprint", Name = "Blueprint", Sounds = "pack:EDGE UI Sounds", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "image:Blueprint.jpg",
            Description = "An engineer's drawing: gridded blueprint cards with registration marks, pop-ups headed by a title block, measuring-dial home buttons, crosshair switches, a drafting pointer, and a fully dimensioned technical drawing of a handheld behind it all.",
            Dark = new[] { "#0B2A55", "#F2F7FF", "#A8C1E3", "#11366A", "#0F3163", "#FFD34D" }, Mode = "dark",
            Font = "file:ShareTechMono-Regular.ttf#Share Tech Mono", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "none", Backdrop = new[] { "#164384", "#0B2C5A" }, PageScrim = 0.45,
            Legendary = true, Skin = new AdvancedSkin("blueprint"), TileAnimation = "lockon"
        },
        new()
        {
            Id = "adv-circuit", Name = "Circuit", Sounds = "pack:EDGE UI Sounds", Music = "track:Fraxsu", Moving = "", Wallpaper = "image:Circuit.jpg",
            Description = "A circuit board up close: green solder-mask cards with gold traces and vias, buttons that are little chips with gold pins, gold-ringed home buttons, switches that light an LED, gold pads for check boxes, and a processor with data pulsing along its traces.",
            Dark = new[] { "#04140C", "#E8FFF0", "#8DBFA3", "#0A2618", "#082014", "#F2C14E" }, Mode = "dark",
            Font = "file:ChakraPetch-Regular.ttf#Chakra Petch", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "hard", Backdrop = new[] { "#0E3B22", "#062413" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("circuit"), TileAnimation = "lockon"
        },
        new()
        {
            Id = "adv-grimoire", Name = "Grimoire", Sounds = "deep", NavSounds = "marimba", Music = "track:Dreamcore", Moving = "", Wallpaper = "image:Grimoire.jpg",
            Description = "An old spell book: tooled leather cards with gilt borders and corner fleurons, pop-ups set with a violet gem, home buttons ringed with glowing runes, switches that wake a gem, a gilt wand pointer, and an open book by candlelight with a sigil glowing on its page.",
            Dark = new[] { "#140D0A", "#F2E3C6", "#B39A7A", "#24170F", "#1E130D", "#B48CFF" }, Mode = "dark",
            Font = "file:Almendra-Regular.ttf#Almendra", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#1C110B", "#120B08" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("grimoire"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-bauhaus", Name = "Bauhaus", Sounds = "pack:EDGE UI Sounds", Music = "track:Jewels", Moving = "", Wallpaper = "image:Bauhaus.jpg",
            Description = "A Bauhaus poster: cream cards with a red quarter-circle and a yellow square, yellow buttons with a hard black shadow, a blue pop-up header with a circle and triangle, yellow-and-red ring buttons, switches with a square that turns into a circle, and a poster of bold shapes.",
            Light = new[] { "#F2EDE3", "#1A1A1A", "#5E5A52", "#FBF8F2", "#ECE5D8", "#D7263D" }, ForceLight = true,
            Font = "file:JosefinSans-Regular.ttf#Josefin Sans", TileCorner = 0, RowCorner = 2, RingThickness = 4, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "hard", Backdrop = new[] { "#F4EEE2", "#EBE2D1" }, PageScrim = 0.5,
            Legendary = true, Skin = new AdvancedSkin("bauhaus"), TileAnimation = "steady"
        },
        new()
        {
            Id = "adv-mirage", Name = "Mirage", Sounds = "kalimba", NavSounds = "soft", Music = "track:Sun Rays", Moving = "", Wallpaper = "image:Mirage.jpg",
            Description = "The desert at golden hour: sand cards with wind ripples and a low sun, pop-ups headed by a sunset over the dunes, sun home buttons, switches that go from a starry night with a moon to a sunny day, and a caravan crossing the dunes beneath a huge shimmering sun.",
            Light = new[] { "#F3E3C8", "#3D2A1C", "#8A6B52", "#FBF1E1", "#EED9BA", "#D9733B" }, ForceLight = true,
            Font = "file:TenorSans-Regular.ttf#Tenor Sans", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#F6E2C4", "#E6866A" }, PageScrim = 0.35,
            Legendary = true, Skin = new AdvancedSkin("mirage"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-riad", Name = "Riad", Sounds = "marimba", NavSounds = "kalimba", Music = "track:Relaxing", Moving = "", Wallpaper = "image:Riad.jpg",
            Description = "A Moroccan courtyard: white plaster cards with zellige stars in the corners, pop-ups with a cobalt header, a tile border and a horseshoe arch, eight-pointed star home buttons, switches with a gold star, and a tiled courtyard wall with an arch onto a palm and the sky.",
            Light = new[] { "#F4EEE4", "#1F2E3A", "#66727C", "#FFFFFF", "#EEE5D6", "#1F7A8C" }, ForceLight = true,
            Font = "file:ElMessiri-Regular.ttf#El Messiri", TileCorner = 8, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#FFFDF8", "#F1E9DC" }, PageScrim = 0.4,
            Legendary = true, Skin = new AdvancedSkin("riad"), TileAnimation = "lift"
        },
        new()
        {
            Id = "adv-autumn", Name = "Autumn", Sounds = "marimba", NavSounds = "soft", Music = "track:Coffee Shop", Moving = "", Wallpaper = "image:Autumn.jpg",
            Description = "A misty autumn wood: cream cards with a maple and an oak leaf, pop-ups headed with falling leaves, home buttons in a wreath of leaves, switches where a green leaf turns red, an orange leaf pointer, and a sunlit wood with leaves falling and covering the ground.",
            Light = new[] { "#F5EAD7", "#3A2618", "#84664C", "#FDF6EA", "#F0E0C6", "#D4572A" }, ForceLight = true,
            Font = "file:Merienda-Regular.ttf#Merienda", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Ambient = "float", Backdrop = new[] { "#FBE7C6", "#E59A5E" }, PageScrim = 0.4,
            Legendary = true, Skin = new AdvancedSkin("autumn"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-halftone", Name = "Halftone", Sounds = "bubble", NavSounds = "laser", Music = "track:Jumps", Moving = "", Wallpaper = "image:Halftone.jpg",
            Description = "A comic book: white cards inked in black with halftone dots and a hard shadow, yellow buttons, pop-ups headed in red with a POW! burst, starburst home buttons, switches that go off with a bang, a red pointer, and a comic page full of bursts, speed lines and speech bubbles.",
            Light = new[] { "#FFF6D6", "#111111", "#4A4A4A", "#FFFFFF", "#FFF0B8", "#E52521" }, ForceLight = true,
            Font = "file:Bangers-Regular.ttf#Bangers", TileCorner = 4, RowCorner = 4, RingThickness = 4, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "hard", Backdrop = new[] { "#FFF6D6", "#FFE58A" }, PageScrim = 0.55,
            Legendary = true, Skin = new AdvancedSkin("halftone"), TileAnimation = "jelly"
        },
        // 7.3.46: the Mythical tier, drawn by Claude: each a world of its own, down to the switches and the pointer.
        new()
        {
            Id = "myth-olympus", Name = "Olympus", Sounds = "chime", NavSounds = "glass", Music = "track:Daybreak", Moving = "", Wallpaper = "image:Olympus.jpg",
            Description = "The home of the gods above the clouds: white marble cards veined in grey with gold Greek-key corners, pop-ups headed in Aegean blue with gold rules and a thunderbolt, home buttons crowned in gold laurel, switches that charge a lightning bolt, a gold thunderbolt pointer, and a marble temple glowing at sunset on a sea of clouds while Zeus's lightning cracks a storm.",
            Light = new[] { "#F4EFE6", "#1E2A44", "#6A6A72", "#FFFFFF", "#ECE5D8", "#2A55A0" }, ForceLight = true,
            Font = "file:Forum-Regular.ttf#Forum", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#C9A24A", "#FFF1C2", "#2A55A0", "#FFF1C2", "#C9A24A" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "bokeh", Backdrop = new[] { "#FBE9D4", "#C9B6DA" }, PageScrim = 0.35,
            Mythical = true, Skin = new AdvancedSkin("olympus"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "myth-hoard", Name = "Dragon's Hoard", Sounds = "deep", NavSounds = "marimba", Music = "track:Dreamcore", Moving = "", Wallpaper = "image:DragonsHoard.jpg",
            Description = "A dragon's lair: polished obsidian cards held at the corners by gold claws set with rubies, pop-ups headed in smouldering red with a dragon's eye, home buttons ringed in gold spikes and ruby, switches where a sleeping eye opens and glows, a gold pointer with a ruby, and a dragon coiled behind mountains of gold, one eye open, embers drifting up.",
            Dark = new[] { "#0C0505", "#F6E7D0", "#C2A68C", "#1E0E0C", "#180A08", "#FF5A3C" }, Mode = "dark",
            Font = "file:UncialAntiqua-Regular.ttf#Uncial Antiqua", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#F2C760", "#FF5A3C", "#FFE7A8", "#C2380E", "#F2C760" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#1C0A08", "#080303" }, PageScrim = 0.55,
            Mythical = true, Skin = new AdvancedSkin("hoard"), TileAnimation = "glow"
        },
        new()
        {
            Id = "myth-aurora", Name = "Aurora", Sounds = "glass", NavSounds = "glass", Music = "track:Waves", Moving = "", Wallpaper = "image:Aurora.jpg",
            Description = "The northern lights: night-ice cards with the aurora glowing along their top and snowflakes in the corners, pop-ups headed by ribbons of green, teal and violet, hexagonal ice-crystal home buttons, switches that fill with aurora light, and curtains of the aurora swaying over snowy peaks, mirrored in a frozen lake.",
            Dark = new[] { "#06111D", "#EFFFFA", "#9CC6D4", "#0C2236", "#0A1C2C", "#5CF2B0" }, Mode = "dark",
            Font = "file:JuliusSansOne-Regular.ttf#Julius Sans One", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#5CF2B0", "#2ED3C6", "#6A7CFF", "#B36BFF", "#5CF2B0" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#0B2034", "#050B1C" }, PageScrim = 0.5,
            Mythical = true, Skin = new AdvancedSkin("aurora"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "myth-chronos", Name = "Chronos", Sounds = "tick", NavSounds = "tick", Music = "track:Jewels", Moving = "", Wallpaper = "image:Chronos.jpg",
            Description = "A master watchmaker's piece: cream guilloché enamel cards with rose-gold gears set with rubies, pop-ups headed in blued-steel enamel with a little watch face, home buttons that are watch dials in knurled rose gold, switches with a gear that turns blue, a blued watch-hand pointer, and a great dial with Roman numerals opening onto its turning gears and balance wheel.",
            Light = new[] { "#F4EDDD", "#1A2440", "#6A6A7A", "#FFFDF8", "#EEE4D0", "#2E5FD0" }, ForceLight = true,
            Font = "file:CormorantSC-Regular.ttf#Cormorant SC", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#E8B08C", "#FFE6D6", "#2E5FD0", "#FFE6D6", "#E8B08C" },
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#FBF6EA", "#E8DCC2" }, PageScrim = 0.35,
            Mythical = true, Skin = new AdvancedSkin("chronos"), TileAnimation = "lockon"
        },
        new()
        {
            Id = "myth-kitsune", Name = "Kitsune Shrine", Sounds = "kalimba", NavSounds = "soft", Music = "track:Relaxing", Moving = "", Wallpaper = "image:KitsuneShrine.jpg",
            Description = "A fox spirit's shrine by moonlight: indigo cards washed with seigaiha waves and a sakura blossom in each corner, pop-ups headed in vermilion lacquer with a torii gate, home buttons ringed by nine blue fox-fires for nine tails, switches where a sakura blooms, a vermilion pointer, and a path of torii gates and lanterns under a full moon, a fox and its fox-fire waiting among falling petals.",
            Dark = new[] { "#0C0A1A", "#FFF1E0", "#C9B8D6", "#1A1430", "#151026", "#FF5A3C" }, Mode = "dark",
            Font = "file:Shojumaru-Regular.ttf#Shojumaru", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#E8432B", "#FFC2D1", "#7FE0FF", "#FFC2D1", "#E8432B" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "float", Backdrop = new[] { "#1A1238", "#0A0814" }, PageScrim = 0.5,
            Mythical = true, Skin = new AdvancedSkin("kitsune"), TileAnimation = "float"
        },
        new()
        {
            Id = "myth-pharaoh", Name = "Pharaoh", Sounds = "chime", NavSounds = "marimba", Music = "track:Sun Rays", Moving = "", Wallpaper = "image:Pharaoh.jpg",
            Description = "A pharaoh's treasury: lapis lazuli cards flecked with gold and little gold pyramids with turquoise capstones at the corners, pop-ups headed in gold banded with lapis and turquoise under a winged sun, home buttons wearing a jewelled collar, switches with the Eye of Horus, a gold pointer, and pyramids catching the moon beneath the Milky Way and a great golden winged sun.",
            Dark = new[] { "#081A44", "#F8EED6", "#B9C4E6", "#0E2A6B", "#0C2460", "#F2C14E" }, Mode = "dark",
            Font = "file:CinzelDecorative-Regular.ttf#Cinzel Decorative", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            RingColors = new[] { "#F2C760", "#2EC4B6", "#FFF4C8", "#123A8C", "#F2C760" },
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#0A1240", "#2A1A3A" }, PageScrim = 0.5,
            Mythical = true, Skin = new AdvancedSkin("pharaoh"), TileAnimation = "glow"
        },
        // 7.2: Unlockable styles, earned by playing (Themes > Unlockable). Drawn by Claude.
        new()
        {
            Id = "adv-bronze", Name = "Bronze", Sounds = "chime", NavSounds = "tick", Music = "track:Daybreak", Moving = "", Wallpaper = "image:Bronze.jpg",
            Description = "Your first medal: warm dark cards with a bronze frame and studs at the corners, bronze buttons, home buttons ringed like a medal's edge, and a bronze medal hanging in a spotlight.",
            Dark = new[] { "#1A120C", "#F5E6D8", "#BFA48C", "#2A1A10", "#22150C", "#CD7F32" }, Mode = "dark",
            Font = "file:Cinzel-Regular.ttf#Cinzel", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Backdrop = new[] { "#20140C", "#080402" }, PageScrim = 0.45,
            Unlock = "hours:10", Skin = new AdvancedSkin("bronze"), TileAnimation = "lift"
        },
        new()
        {
            Id = "adv-silver", Name = "Silver", Sounds = "glass", NavSounds = "tick", Music = "track:Relaxing", Moving = "", Wallpaper = "image:Silver.jpg",
            Description = "Polished and bright: brushed-silver cards and buttons with a chrome frame, finely engraved home buttons, and a silver cup on an engraved plate.",
            Light = new[] { "#E9ECF0", "#1E2329", "#5E6773", "#F7F9FB", "#E2E6EB", "#5B6B7D" }, ForceLight = true,
            Font = "file:JosefinSans-Regular.ttf#Josefin Sans", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Backdrop = new[] { "#F4F6F9", "#C9D0D8" }, PageScrim = 0.4,
            Unlock = "hours:50", Skin = new AdvancedSkin("silver"), TileAnimation = "lift"
        },
        new()
        {
            Id = "adv-gold", Name = "Gold", Sounds = "chime", NavSounds = "glass", Music = "track:Sun Rays", Moving = "", Wallpaper = "image:Gold.jpg",
            Description = "First place: black cards with a gold frame and stars in the corners, gold home buttons, switches that light up gold, and a gold trophy on the top step of the podium in a shower of confetti.",
            Dark = new[] { "#120E05", "#FFF4D6", "#C9B98A", "#1C1608", "#171206", "#F2C14E" }, Mode = "dark",
            Font = "file:PoiretOne-Regular.ttf#Poiret One", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#14100A", "#050402" }, PageScrim = 0.5,
            Unlock = "achievements:100", Skin = new AdvancedSkin("gold"), TileAnimation = "glow"
        },
        new()
        {
            Id = "adv-platinum", Name = "Platinum", Sounds = "glass", NavSounds = "kalimba", Music = "track:Sweetness", Moving = "", Wallpaper = "image:Platinum.jpg",
            Description = "The rarest trophy: pale cards edged in shimmering holographic foil, iridescent buttons and switches, and a faceted crystal on a rainbow-foil background.",
            Light = new[] { "#F2F4F8", "#1B1F2A", "#5A6070", "#FBFCFE", "#EEF1F6", "#7B61FF" }, ForceLight = true,
            Font = "file:Comfortaa-Regular.ttf#Comfortaa", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#F7F2FF", "#E6F4FF" }, PageScrim = 0.45,
            Unlock = "finished:10", Skin = new AdvancedSkin("platinum"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-nightowl", Name = "Night Owl", Sounds = "soft", NavSounds = "droplet", Music = "track:Meditation", Moving = "", Wallpaper = "image:NightOwl.jpg",
            Description = "For late players: midnight-blue cards sprinkled with stars, moonlit home buttons, gold icons like moonlight, and an owl watching from a branch under a huge moon over the forest.",
            Dark = new[] { "#070B1C", "#E6ECFF", "#9AA6C8", "#101A3A", "#0C1430", "#FFD36B" }, Mode = "dark",
            Font = "file:CrimsonText-Regular.ttf#Crimson Text", TileCorner = 10, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#0A1230", "#03050E" }, PageScrim = 0.45,
            Unlock = "nights:5", Skin = new AdvancedSkin("nightowl"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-ember", Name = "Ember", Sounds = "deep", NavSounds = "tick", Music = "track:Coffee Shop", Moving = "", Wallpaper = "image:Ember.jpg",
            Description = "Keep the streak burning: charcoal cards with glowing embers in the corners, fiery buttons, home buttons ringed with flames, and a campfire throwing sparks into the dark.",
            Dark = new[] { "#140806", "#FFE9DC", "#C99C88", "#24100A", "#1C0C06", "#FF6A2B" }, Mode = "dark",
            Font = "file:SairaCondensed-Regular.ttf#Saira Condensed", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "line", Shadow = "soft", Ambient = "sparkle", Backdrop = new[] { "#160806", "#050202" }, PageScrim = 0.5,
            Unlock = "streak:7", Skin = new AdvancedSkin("ember"), TileAnimation = "glow"
        },
        new()
        {
            Id = "adv-neon-grid", Name = "Neon Grid", Sounds = "pack:Essential UI", Music = "track:Fraxsu", Moving = "", Wallpaper = "",
            Description = "A cyberpunk control panel: glowing framed panels with header strips, neon switches and checkboxes, a grid over deep navy and a pointer of its own.",
            Dark = new[] { "#060B1A", "#E6F4FF", "#8EA8C8", "#0C1630", "#0A1228", "#22D3EE" },
            Font = "file:Cyber_Gaming.ttf#Cyber Gaming", TileCorner = 10, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "grid", Footer = "line", Backdrop = new[] { "#0A1530", "#04070F" },
            Skin = new AdvancedSkin("neon"), TileAnimation = "lockon", NavSounds = "laser"
        },
        new()
        {
            Id = "adv-hologram", Name = "Hologram", Sounds = "pack:Essential UI", Music = "track:Ripples", Moving = "", Wallpaper = "",
            Description = "A starship's holographic interface: translucent teal cards that shimmer, pop-ups with a glowing header band, glowing ring buttons, holo pill switches and check boxes, and hologram icons on the home buttons.",
            Dark = new[] { "#02110F", "#D9FFF7", "#7FC9BD", "#06201C", "#051A17", "#5BF2D6" },
            Font = "file:Vislar-Thin.otf#Vislar#Vislar Thin", TileCorner = 8, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Overlay = "hud", Ambient = "status", Footer = "line", Backdrop = new[] { "#03201C", "#010807" },
            Skin = new AdvancedSkin("hologram"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-glow", Name = "Glow", Sounds = "pack:Essential UI Soft", Music = "track:Meditation", Moving = "", Wallpaper = "",
            Description = "Thin neon outlines glowing on true black: outlined rows, pill buttons, ring buttons and glowing line icons. Choose blue, green, red or yellow.",
            Dark = new[] { "#020204", "#EEF2FF", "#8C93AA", "#0B0D14", "#090A10", "#7FB2FF" }, Mode = "oled",
            Font = "file:Tindamare.ttf#Tindamare", TileCorner = 16, RowCorner = 16, RingThickness = 2, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Footer = "none",
            Skin = new AdvancedSkin("glow", new[] { ("Blue", "Blue", "#7FB2FF"), ("Green", "Green", "#6CFF9E"), ("Red", "Red", "#FF6B7A"), ("Yellow", "Yellow", "#FFE36B") }),
            TileAnimation = "glow"
        },
        new()
        {
            Id = "adv-cozy-pixel", Name = "Cozy Pixel", Sounds = "pack:Northleaf UI", Music = "track:Coffee Shop", Moving = "", Wallpaper = "",
            Description = "A cozy pixel-art game menu: striped paper rows, chunky wooden buttons, ON / OFF switches, a framed pop-up and a pixel pointer. Five colors.",
            Light = new[] { "#FBF1DE", "#4A2E1F", "#8A6A55", "#FFF8EC", "#F3E2C4", "#C8734A" }, ForceLight = true,
            Font = "file:PixelOperator16-Regular.ttf#Pixel Operator", TileCorner = 6, RowCorner = 6, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", Shadow = "hard", Backdrop = new[] { "#FCE9C8", "#F6D7A8" }, PageScrim = 0.4, Glyphs = "pixel",
            Skin = new AdvancedSkin("cozy", new[] { ("cream", "Cream", "#C8734A"), ("blue", "Blue", "#3B9BE0"), ("green", "Green", "#5FA834"), ("pink", "Pink", "#E85A92"), ("purple", "Purple", "#8B6BD6") }),
            TileAnimation = "jelly", NavSounds = "pack:Northleaf UI"
        },
        new()
        {
            Id = "adv-tropical-night", Name = "Tropical Night", Sounds = "pack:Northleaf UI", Music = "track:Relaxing", Moving = "Pixel Forest", Wallpaper = "",
            Description = "Gold on near-black under a jungle moon: leaf-cornered panels with 1-pixel gold outlines, moss-green switches and gold checkboxes, in its own pixel font.",
            Dark = new[] { "#0B120E", "#F2E6C4", "#A9B39A", "#13201A", "#101A15", "#E2B653" },
            Font = "file:Jersey10-Regular.ttf#Jersey 10", TileCorner = 4, RowCorner = 6, RingThickness = 2, Buttons = "circle",
            HeroOpacity = 0, Ambient = "leaves", Overlay = "vignette", Footer = "line", Backdrop = new[] { "#0F1C16", "#060B08" },
            Skin = new AdvancedSkin("tropical"), TileAnimation = "steady"
        },
        new()
        {
            Id = "adv-retro-windows", Name = "Retro Windows", Sounds = "pack:Windows XP", Music = "track:Recuerdos", Moving = "", Wallpaper = "",
            Description = "Like a 90s desktop: beveled gray windows and buttons, a navy title bar on pop-ups, square checkboxes for switches and a gray taskbar, on a teal desktop seen through an old CRT.",
            Light = new[] { "#C0C0C0", "#000000", "#3A3A3A", "#D4D0C8", "#BDBDBD", "#000080" }, ForceLight = true,
            Font = "file:Windows2000XP.otf#windows 2000 and xp", TileCorner = 0, RowCorner = 0, RingThickness = 2, Buttons = "circle",
            HeroOpacity = 0, Footer = "taskbar", Shadow = "none", Backdrop = new[] { "#008080", "#008080" }, PageScrim = 0.2,
            Ambient = "vhs", Overlay = "scanlines",
            Skin = new AdvancedSkin("retrowin"), TileAnimation = "steady", NavSounds = "pack:Windows XP"
        },
        new()
        {
            Id = "adv-arcade", Name = "Arcade", Sounds = "pack:Northleaf UI", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "",
            Description = "Insert coin: neon pixel frames, segmented bars, scan lines and chasing marquee bulbs, in VT323. Choose cyan, magenta or amber.",
            Dark = new[] { "#0A0E1F", "#F2F6FF", "#9AA6C8", "#121A33", "#0E152B", "#4DE8FF" },
            Font = "file:VT323-Regular.ttf#VT323", TileCorner = 2, RowCorner = 2, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, ButtonLook = "neon", Stroke = "accent", StrokeWidth = 2, Overlay = "scanlines", Ambient = "marquee", Footer = "line",
            Backdrop = new[] { "#0A0E1F", "#05070F" }, Glyphs = "pixel",
            Skin = new AdvancedSkin("arcade", new[] { ("cyan", "Cyan", "#4DE8FF"), ("magenta", "Magenta", "#FF5FD2"), ("amber", "Amber", "#FFC94A") }),
            TileAnimation = "lockon", NavSounds = "pack:Northleaf UI"
        },
        new()
        {
            Id = "adv-softline", Name = "Softline", Sounds = "pack:Essential UI Soft", Music = "track:Coffee Shop", Moving = "", Wallpaper = "",
            Description = "Clean and flat after dark: soft charcoal panels and buttons, coral switches and checkboxes, nothing extra. A calm, modern app look.",
            Dark = new[] { "#16171B", "#ECEEF2", "#9198A6", "#202228", "#1C1E23", "#FF7A45" },
            Font = "Segoe UI Variable Display, Segoe UI", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "pill", Shadow = "none", Backdrop = new[] { "#1A1B20", "#121317" },
            Skin = new AdvancedSkin("softline"), TileAnimation = "grow"
        },
        new()
        {
            Id = "adv-fern-brass", Name = "Fern & Brass", Sounds = "pack:Essential UI Soft", Music = "track:Recuerdos", Moving = "", Wallpaper = "",
            Description = "A cosy shop counter: cream card panels with brass rims, pine-green ink, brass checkboxes and a paper grain, in a bookish serif.",
            Light = new[] { "#F6EDD6", "#173C35", "#5E6F62", "#FAF4E4", "#EFE2C2", "#355D4F" }, ForceLight = true,
            Font = "Constantia, Georgia, Segoe UI", TileCorner = 12, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Overlay = "paper", Footer = "line", Backdrop = new[] { "#F6EDD6", "#E9DBB6" }, PageScrim = 0.35,
            Skin = new AdvancedSkin("fern"), TileAnimation = "breathe"
        },
        new()
        {
            Id = "adv-nebula", Name = "Nebula", Sounds = "pack:Essential UI", Music = "track:Aero", Moving = "", Wallpaper = "",
            Description = "Glossy space-age controls: navy glass panels, bright blue pill buttons, a green switch and a soft pointer, drifting through a starry nebula.",
            Dark = new[] { "#0B1024", "#EAF0FF", "#98A4CC", "#151C3A", "#111733", "#5C8DFF" },
            Font = "file:Tindamare.ttf#Tindamare", TileCorner = 18, RowCorner = 18, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Glow = true, Gloss = true, Ambient = "pixelstars", Footer = "pill", Backdrop = new[] { "#141B3E", "#070A18" },
            Skin = new AdvancedSkin("nebula"), TileAnimation = "float"
        },
        new()
        {
            Id = "adv-luna", Name = "Luna XP", Sounds = "pack:Windows XP", Music = "track:Windows XP Installation", Moving = "", Wallpaper = "image:Bliss.jpg",
            Description = "Early-2000s desktop: cream XP buttons with blue edges, blue title bars on pop-ups, glossy blue round buttons with classic icons, green check boxes for switches and that green hill.",
            Light = new[] { "#ECE9D8", "#000000", "#3D3D3D", "#FFFFFF", "#D6DFF7", "#316AC5" }, ForceLight = true,
            Font = "file:WindowsXPLike.otf#WindowsXPlike A", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "soft", Backdrop = new[] { "#5A8DE8", "#2E63C9" }, PageScrim = 0.35,
            Skin = new AdvancedSkin("luna"), TileAnimation = "steady", NavSounds = "pack:Windows XP"
        },
        new()
        {
            Id = "adv-cozy-place", Name = "Cozy Place", Sounds = "pack:Essential UI Soft", Music = "track:Sweetness", Moving = "Gamer Den", Wallpaper = "",
            Description = "A pixel travel journal in a cozy room: cream note-card buttons with a folded corner, tan rows, pop-ups on a book page, dark leather home buttons with little pixel icons, and a pixel pointer.",
            Light = new[] { "#F6E3C4", "#432828", "#7A5A4A", "#FFE7C2", "#E8B778", "#B55C60" }, ForceLight = true,
            Font = "file:PixelOperator16-Regular.ttf#Pixel Operator", TileCorner = 4, RowCorner = 4, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "hard", Backdrop = new[] { "#B55C60", "#8E3F45" }, PageScrim = 0.35, Glyphs = "pixel",
            Overlay = "paper", Skin = new AdvancedSkin("cozyplace"), TileAnimation = "jelly", NavSounds = "pack:Essential UI Soft"
        },
        new()
        {
            Id = "adv-ubuntu", Name = "Ubuntu", Sounds = "soft", Music = "track:At Rest", Moving = "", Wallpaper = "image:Ubuntu.jpg",
            Description = "A Linux desktop in the Ubuntu spirit: dark grey GNOME-style cards and buttons, pop-ups with a header bar and window buttons, orange switches and check boxes, the Ubuntu font, over an aubergine and orange wallpaper.",
            Dark = new[] { "#2C2C2C", "#F2F2F2", "#A8A8A8", "#383838", "#424242", "#E95420" }, Mode = "dark",
            Font = "file:Ubuntu-Regular.ttf#Ubuntu", TileCorner = 12, RowCorner = 10, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "soft", Backdrop = new[] { "#2C001E", "#4A1340" }, PageScrim = 0.45,
            Skin = new AdvancedSkin("ubuntu"), TileAnimation = "lift", NavSounds = "soft"
        },
        new()
        {
            Id = "adv-fatanzia", Name = "Fatanzia", Sounds = "pack:Essential UI", Music = "track:Meditation", Moving = "", Wallpaper = "image:Fatanzia.jpg",
            Description = "A sci-fi colony command screen: dark navy rows with corner brackets, pop-ups with an amber top line, bracketed home buttons with white colony icons, over a sunrise on an alien ridge.",
            Dark = new[] { "#0E151B", "#E6EDF3", "#8A9BAA", "#16212A", "#1B2733", "#E89A2E" }, Mode = "dark",
            Font = "Bahnschrift, Segoe UI", TileCorner = 2, RowCorner = 2, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "none", Backdrop = new[] { "#101820", "#0A1016" }, PageScrim = 0.55,
            Overlay = "hud", Skin = new AdvancedSkin("fatanzia"), TileAnimation = "steady", NavSounds = "pack:Essential UI"
        },
        new()
        {
            Id = "adv-spectrum", Name = "Spectrum", Sounds = "glass", Music = "track:Jewels", Moving = "", Wallpaper = "",
            Description = "Clean and bright, five ways: white cards with a bold colored edge, pop-ups with a glossy colored header, diamond home buttons and soft outlined rows. Choose violet, red, purple, gold or blue when you pick it.",
            Light = new[] { "#F4F2FA", "#1E1B2E", "#6B6880", "#FFFFFF", "#ECE8F6", "#5C22D0" }, ForceLight = true,
            Font = "Segoe UI Variable Text, Segoe UI", TileCorner = 12, RowCorner = 12, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "soft", Backdrop = new[] { "#F6F4FB", "#E7E3F2" }, PageScrim = 0.25,
            Skin = new AdvancedSkin("spectrum", new[] { ("violet", "Violet", "#5C22D0"), ("red", "Red", "#D02230"), ("purple", "Purple", "#A722D0"), ("gold", "Gold", "#D09C22"), ("blue", "Blue", "#2578CC") }) { AskColor = true },
            TileAnimation = "lift", NavSounds = "glass"
        },
        new()
        {
            Id = "adv-sapphire", Name = "Sapphire", Sounds = "bubble", Music = "track:Sweetness", Moving = "", Wallpaper = "",
            Description = "A dreamy visual-novel look: pastel gradient cards with little hearts, glossy gradient buttons, round candy home buttons with white symbols and condensed type. Choose green, orange or pink when you pick it.",
            Light = new[] { "#F6F3FA", "#2A2238", "#6F6680", "#FFFFFF", "#EFEAF6", "#D64FD0" }, ForceLight = true,
            Font = "file:BarlowCondensed-SemiBold.ttf#Barlow Condensed SemiBold#Barlow Condensed", TileCorner = 16, RowCorner = 16, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "soft", Backdrop = new[] { "#FAF7FD", "#EEE8F6" }, PageScrim = 0.25,
            Skin = new AdvancedSkin("sapphire", new[] { ("green", "Green", "#5DB82E"), ("orange", "Orange", "#F26B4F"), ("pink", "Pink", "#D64FD0") }) { AskColor = true },
            TileAnimation = "float", NavSounds = "bubble"
        },
        new()
        {
            Id = "adv-scholar", Name = "Scholar", Sounds = "pack:Tactile UI", Music = "track:Daybreak", Moving = "", Wallpaper = "",
            Description = "A tidy study desk: soft white cards on warm paper with notebook lines, pale blue buttons with an ink-blue edge, pop-ups in speech bubbles, round ink-blue home buttons, check-box switches and calm piano.",
            Light = new[] { "#F4F1EA", "#22252B", "#666B73", "#FFFFFF", "#E9E5DC", "#2F5DA8" }, ForceLight = true,
            Font = "file:AfacadFlux-Regular.ttf#Afacad Flux", TileCorner = 14, RowCorner = 14, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "soft", Backdrop = new[] { "#F7F4EE", "#ECE7DC" }, PageScrim = 0.25,
            Overlay = "notebook", Skin = new AdvancedSkin("scholar"), TileAnimation = "lift", NavSounds = "pack:Purple Lavender Switches"
        },
        new()
        {
            Id = "adv-indivial", Name = "Indivial", Sounds = "tick", Music = "track:Gaming Vibes", Moving = "", Wallpaper = "",
            Description = "Bold flat game UI: chunky buttons with a raised lip, dark rows, pop-ups with a colored top edge, square home buttons with crisp white icons and pill switches. Choose red, yellow or black when you pick it.",
            Dark = new[] { "#1C1C1F", "#F2F2F4", "#A3A3AB", "#2C2C30", "#2A2A2E", "#C62526" }, Mode = "dark",
            Font = "Bahnschrift SemiBold, Bahnschrift, Segoe UI", TileCorner = 8, RowCorner = 8, RingThickness = 3, Buttons = "circle",
            HeroOpacity = 0, Footer = "bar", Shadow = "hard", Backdrop = new[] { "#1C1C1F", "#121214" }, PageScrim = 0.5,
            Skin = new AdvancedSkin("indivial", new[] { ("red", "Red", "#C62526"), ("yellow", "Yellow", "#E0C21A"), ("black", "Black", "#BFBFC6") }) { AskColor = true },
            TileAnimation = "bounce", NavSounds = "tick"
        },
    };

    public static UiStyle Current { get; private set; } = All[0];

    /// <summary>Styles you made yourself (Settings > Themes > Your styles), built from settings by MyStyles.Load.</summary>
    public static IReadOnlyList<UiStyle> Custom { get; private set; } = Array.Empty<UiStyle>();

    public static void SetCustom(IReadOnlyList<UiStyle> styles) => Custom = styles;

    /// <summary>The style with this id (yours too). Styles from older versions map to their nearest replacement.</summary>
    public static UiStyle Find(string? id) =>
        MyStyles.IsCustom(id) ? Custom.FirstOrDefault(s => s.Id == id) ?? All[0]
        : All.FirstOrDefault(s => s.Id == Upgrade(id)) ?? Advanced.FirstOrDefault(s => s.Id == id) ?? All[0];

    /// <summary>Whether there's a style with this id (a style of yours that was deleted isn't).</summary>
    public static bool Exists(string? id) =>
        MyStyles.IsCustom(id) ? Custom.Any(s => s.Id == id) : All.Any(s => s.Id == Upgrade(id)) || Advanced.Any(s => s.Id == id);

    /// <summary>Whether a style is one of the Advanced ones (Settings > Themes > Advanced).</summary>
    public static bool IsAdvanced(string? id) => Advanced.Any(s => s.Id == id);

    /// <summary>Whether a style is one of the Legendary ones (Advanced styles drawn by Claude: Settings > Themes > Legendary).</summary>
    public static bool IsLegendary(string? id) => Advanced.Any(s => s.Id == id && s.Legendary);

    /// <summary>Whether a style is one of the Mythical ones (the top tier, drawn by Claude: Settings > Themes > Mythical).</summary>
    public static bool IsMythical(string? id) => Advanced.Any(s => s.Id == id && s.Mythical);

    /// <summary>
    /// How many styles come with SquareDeck (Basic plus Advanced), counted from the lists themselves, so text that
    /// mentions the number (Settings, the Themes page) stays right as styles are added. Your own styles aren't counted.
    /// </summary>
    public static int ReadyMadeCount => All.Length + Advanced.Length;

    public static string Upgrade(string? id) => id switch
    {
        "glass" => "frosted",
        "ps5" => "cinema",
        "xbox" => "switch",
        null or "" => "switch",
        _ => id
    };

    public static void Select(string? id) => Current = Find(id);

    public static readonly (string Id, string Name, string About)[] TileArts =
    {
        ("style", "Default", "SquareDeck's standard look"),
        ("square", "Square", "The square cover"),
        ("tall", "Tall grid", "Tall 2:3 poster art"),
        ("wide", "Rectangle", "The wide header capsule (460×215)"),
    };

    public static readonly (string Id, string Name, string About)[] TileSizes =
    {
        ("style", "Default", "SquareDeck's standard size"),
        ("small", "Small", "More games on screen"),
        ("medium", "Medium", "The usual size"),
        ("large", "Large", "Bigger art, fewer on screen"),
    };

    /// <summary>
    /// Your own choices laid over the UI style: the font, and the game art's shape and size. "style" keeps the
    /// style's own. (Choices for the home buttons, their shelf and the footer were removed in 6.4.1.)
    /// </summary>
    public static void ApplyOverrides(Settings settings)
    {
        var s = Current;
        // An Advanced style's color variant, and the highlight color that goes with it.
        if (s.Skin is { Variants.Length: > 0 } skin)
        {
            settings.SkinVariants.TryGetValue(s.Id, out var chosen);
            var variant = skin.Variants.FirstOrDefault(v => v.Id == chosen);
            if (variant.Id == null) variant = skin.Variants[0];
            s = s with { SkinVariant = variant.Id };
            if (s.Dark is { } dark) { var d = (string[])dark.Clone(); d[5] = variant.Accent; s = s with { Dark = d }; }
            if (s.Light is { } light) { var l = (string[])light.Clone(); l[5] = variant.Accent; s = s with { Light = l }; }
        }
        if (settings.UiFont is { Length: > 0 } font && font != "style") s = s with { Font = font };
        s = settings.TileArt switch
        {
            "square" => s with { Portrait = false, Wide = false },
            "tall" => s with { Portrait = true, Wide = false },
            "wide" => s with { Portrait = false, Wide = true },
            _ => s
        };
        // Wide tiles are about twice as wide, so they're drawn smaller to keep a few on screen.
        double size = (settings.TileSize switch { "small" => 0.82, "large" => 1.16, _ => 1.0 }) * (s.Wide ? 0.66 : 1.0);
        if (Math.Abs(size - 1) > 0.001) s = s with { TileScale = s.TileScale * size };
        Current = s;
    }

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
        res["RingBrush"] = RingBrush(s, accent);
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
        // Accessibility's thicker highlight ring: well over the style's own, so the selected item is easy to find.
        double ringWidth = Theme.ThickRing ? Math.Max(7, s.RingThickness * 1.8) : s.RingThickness;
        res["RingThickness"] = new Thickness(ringWidth);
        res["TileRingMargin"] = new Thickness(s.TileGap);
        res["TileInnerMargin"] = new Thickness(s.TileGap + ringWidth + 4);
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
        if (!Theme.HighContrast && HighlightEffects.GlowFor(accent) is (true, var glow)) res["RingGlow"] = glow;
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
            "large" => (116.0, 116.0, 58.0, 38.0, false, 6.0, Orientation.Vertical),
            "soft" => (92.0, 92.0, 32.0, 30.0, false, 6.0, Orientation.Vertical),
            "wide" => (134.0, 82.0, Math.Max(8, s.RowCorner + 6), 26.0, true, 5.0, Orientation.Vertical),
            _ => s.ButtonDock ? (84.0, 84.0, 42.0, 28.0, false, 4.0, Orientation.Vertical) : (96.0, 96.0, 48.0, 32.0, false, 6.0, Orientation.Vertical)
        };
        res["RoundWidth"] = w;
        res["RoundLabelMax"] = s.Buttons is "tab" or "wide" ? 104.0 : 76.0;
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
        // Accent circles: filled with the highlight color. Neon rings: an outline that glows in it.
        if (s.ButtonLook == "accent")
        {
            res["RoundFill"] = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B)));
            res["ButtonShadow"] = Perf.Active ? null : Frozen(new DropShadowEffect { Color = accent, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 });
        }
        else if (s.ButtonLook == "neon")
        {
            res["RoundFill"] = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)));
            res["PanelStrokeThickness"] = new Thickness(2);
            res["ButtonShadow"] = Perf.Active ? null : Frozen(new DropShadowEffect { Color = accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.95 });
        }

        // Advanced styles: their artwork for rows, buttons, dialogs and the round buttons.
        Skins.Apply(s, res, circle);
        if (Skins.Current?.Round != null) { res["RoundFill"] = Brushes.Transparent; res["ButtonShadow"] = null; }
        // A square (or slightly rounded) button picture gets a highlight ring of the same shape, not a circle around it.
        if (Skins.Current is { Round: not null, RoundCorner: >= 0 } shaped)
        {
            double picture = w - 18;
            double ring = shaped.RoundCorner <= 0 ? 2 : shaped.RoundCorner * picture + 7;
            res["RoundCorner"] = new CornerRadius(ring);
            res["RoundInnerCorner"] = new CornerRadius(Math.Max(0, ring - 9));
        }

        // Glossy buttons: a bright shine over the top half that ends in a crisp line, like polished plastic.
        res["GlossVisibility"] = s.Gloss ? Visibility.Visible : Visibility.Collapsed;
        if (s.Gloss)
        {
            var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF), 0));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.46));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.8));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, accent.R, accent.G, accent.B), 1));
            res["GlossBrush"] = Frozen(shine);
        }
    }

    /// <summary>
    /// The highlight ring: a gradient that slowly turns (Switch, like the Switch 2's), or the plain highlight color.
    /// A highlight color you picked yourself replaces the gradient.
    /// </summary>
    /// <summary>Puts the ring back in step with the highlight color (after it changes on its own, such as the game's color).</summary>
    public static void RefreshRing(bool gradient)
    {
        var res = Application.Current.Resources;
        Color accent = (res["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        res["RingBrush"] = RingBrush(Current, accent, gradient);
        if (!Theme.HighContrast && HighlightEffects.GlowFor(accent) is (true, var glow)) res["RingGlow"] = glow;
        (Application.Current.MainWindow as MainWindow)?.RefreshHomeRing();
    }

    private static Brush RingBrush(UiStyle s, Color accent, bool gradient = true)
    {
        if (HighlightEffects.Active && !Theme.HighContrast) return HighlightEffects.Build(accent);
        if (!gradient || s.RingColors is not { Length: > 1 } colors || Theme.UserAccent) return Frozen(new SolidColorBrush(accent));
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < colors.Length; i++)
            if (Theme.TryParseArgb(colors[i], out var c)) brush.GradientStops.Add(new GradientStop(c, (double)i / (colors.Length - 1)));
        if (Theme.ReduceMotion || Perf.Active) return Frozen(brush);
        // The colors drift around the ring.
        var turn = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = turn;
        turn.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(6))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        return brush;
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
            case "paper":
            {
                // Paper grain: tiny speckles, plus a sketched double border around the whole screen.
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1280, 720))));
                var random = new Random(7);
                var speck = new SolidColorBrush(Color.FromArgb(0x18, 0x40, 0x2A, 0x10));
                var specks = new GeometryGroup();
                for (int i = 0; i < 900; i++) specks.Children.Add(new EllipseGeometry(new Point(random.NextDouble() * 1280, random.NextDouble() * 720), 0.6 + random.NextDouble(), 0.6 + random.NextDouble()));
                group.Children.Add(new GeometryDrawing(speck, null, specks));
                var ink = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x3B, 0x2A, 0x1A)), 1.6);
                for (int k = 0; k < 2; k++)
                {
                    var f = new PathFigure { StartPoint = new Point(14 + k * 5, 16 + k * 4), IsClosed = true };
                    foreach (var (x, y) in new[] { (1266 - k * 4, 13 + k * 5), (1268 - k * 5, 704 - k * 4), (12 + k * 4, 706 - k * 5) })
                        f.Segments.Add(new LineSegment(new Point(x + random.NextDouble() * 2, y + random.NextDouble() * 2), true));
                    group.Children.Add(new GeometryDrawing(null, ink, new PathGeometry(new[] { f })));
                }
                var paper = new DrawingBrush(group) { Stretch = Stretch.Fill };
                paper.Freeze();
                return paper;
            }
            case "lcd":
            {
                // The pixel grid of a small LCD: faint dark lines between every 4px "pixel".
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0x23, 0x2A, 0x1C)), 1);
                var g = new GeometryGroup();
                g.Children.Add(new LineGeometry(new Point(0, 0), new Point(4, 0)));
                g.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 4)));
                return Tiled(new GeometryDrawing(null, pen, g), 4);
            }
            case "hud":
            {
                // Corner brackets around the screen and a faint 32px grid, like a targeting display.
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1280, 720))));
                var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x0C, 0xB5, 0xC9, 0x4A)), 1);
                for (int x = 0; x <= 1280; x += 32) group.Children.Add(new GeometryDrawing(null, grid, new LineGeometry(new Point(x, 0), new Point(x, 720))));
                for (int y = 0; y <= 720; y += 32) group.Children.Add(new GeometryDrawing(null, grid, new LineGeometry(new Point(0, y), new Point(1280, y))));
                var bracket = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xB5, 0xC9, 0x4A)), 2);
                foreach (var (cx, cy, dx, dy) in new[] { (12.0, 12.0, 1, 1), (1268.0, 12.0, -1, 1), (12.0, 708.0, 1, -1), (1268.0, 708.0, -1, -1) })
                {
                    var f = new PathFigure { StartPoint = new Point(cx, cy + dy * 40) };
                    f.Segments.Add(new LineSegment(new Point(cx, cy), true));
                    f.Segments.Add(new LineSegment(new Point(cx + dx * 40, cy), true));
                    group.Children.Add(new GeometryDrawing(null, bracket, new PathGeometry(new[] { f })));
                }
                var hud = new DrawingBrush(group) { Stretch = Stretch.Fill };
                hud.Freeze();
                return hud;
            }
            case "notebook":
            {
                // Ruled journal lines with a red margin line on the left.
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1280, 720))));
                var rule = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0x2F, 0x5A, 0x8A)), 1);
                for (int y = 60; y < 720; y += 30) group.Children.Add(new GeometryDrawing(null, rule, new LineGeometry(new Point(0, y), new Point(1280, y))));
                group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xC0, 0x40, 0x40)), 1.4), new LineGeometry(new Point(72, 0), new Point(72, 720))));
                var notebook = new DrawingBrush(group) { Stretch = Stretch.Fill };
                notebook.Freeze();
                return notebook;
            }
            case "cockpit":
            {
                // Instrument rings in the corners and orbital ellipses across the middle.
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1280, 720))));
                var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x1A, 0x3F, 0xE0, 0xFF)), 1);
                var tick = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x3F, 0xE0, 0xFF)), 1.5);
                foreach (var c in new[] { new Point(-40, 760), new Point(1320, 760) })
                    for (int r = 120; r <= 300; r += 60) group.Children.Add(new GeometryDrawing(null, r == 300 ? tick : faint, new EllipseGeometry(c, r, r)));
                group.Children.Add(new GeometryDrawing(null, faint, new EllipseGeometry(new Point(640, 330), 600, 150)));
                group.Children.Add(new GeometryDrawing(null, faint, new EllipseGeometry(new Point(640, 330), 450, 100)));
                var cockpit = new DrawingBrush(group) { Stretch = Stretch.Fill };
                cockpit.Freeze();
                return cockpit;
            }
            case "sheen":
            {
                // A soft iridescent wash across the screen, like light on a soap bubble.
                var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 0x7F, 0xD6, 0xFF), 0));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.35));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0x9A, 0xD5), 0.65));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xE3, 0xA3), 1));
                sheen.Freeze();
                return sheen;
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
