using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>
/// A UI style the user made themselves (Settings > Themes > Your styles): a starting style with their own choice of
/// background, music, sounds, colors, font, home screen buttons, shelf, effect, button pictures, game art,
/// animations and lock screen picture. Kept in settings.json; files of their own are copied to Styles\(id)\.
/// </summary>
public sealed class CustomStyle
{
    public string Id { get; set; } = MyStyles.NewId();
    public string Name { get; set; } = "My style";
    public DateTime Saved { get; set; } = DateTime.Now;
    public string BasedOn { get; set; } = "switch";   // the ready-made style it started from

    // Background: a picture ("image:File.jpg", "preset:N" or a file of your own) or a moving background (a name or a video file).
    public string Wallpaper { get; set; } = "";
    public string Moving { get; set; } = "";

    // Sound
    public string Music { get; set; } = "";
    public string Sounds { get; set; } = "classic";     // select, back, pages and pop-ups
    public string NavSounds { get; set; } = "";          // moving around; "" = the same as Sounds

    // Colors
    public string Mode { get; set; } = "dark";           // light, dark or oled
    public string Palette { get; set; } = "style";       // "style" (the starting style's), "plain" (SquareDeck's grays) or a color theme
    public string Accent { get; set; } = "";             // "" = the colors' own highlight

    // Type and shape
    public string Font { get; set; } = "style";          // "style" = the starting style's
    public int Corners { get; set; } = -1;               // -1 = the starting style's

    // Home screen
    public string ButtonShape { get; set; } = "circle";
    public string ButtonFill { get; set; } = "solid";    // solid, outline, accent, neon, glossy, frosted
    public bool Glow { get; set; }
    public string Shelf { get; set; } = "none";          // none, glass, solid, outline, glow
    public string Footer { get; set; } = "line";
    public string Effect { get; set; } = "style";        // see MyStyles.Effects
    public string Glyphs { get; set; } = "text";         // Hints.Looks: text, default, pixel
    public string TileArt { get; set; } = "square";      // square, tall, wide
    public bool ContinueTile { get; set; } = true;

    // Animations
    public string ButtonAnimation { get; set; } = "grow";
    public string PageAnimation { get; set; } = "slide";
    public string TileAnimation { get; set; } = "pulse";
    public string BootAnimation { get; set; } = "logo";
    public string ExitAnimation { get; set; } = "logo";
    public string LaunchAnimation { get; set; } = "classic";
    public string UpdateLook { get; set; } = "";         // the update screen's animation ("" = leave yours)
    public string UpdateHaptic { get; set; } = "";       // and its vibration

    // Home button icons borrowed from another style (its id; "" = the starting style's own, or the symbols).
    public string IconsFrom { get; set; } = "";

    // Windows lock screen: "" leaves it alone, "wallpaper" uses this style's picture, or a picture ("image:File.jpg" or a file).
    public string LockScreen { get; set; } = "";

    public CustomStyle Copy() => (CustomStyle)MemberwiseClone();

    /// <summary>The style itself, built on the one it started from.</summary>
    public UiStyle ToStyle()
    {
        var b = MyStyles.BuiltIn(BasedOn);

        // Colors: the chosen color theme (or the starting style's), made to fit light, dark or OLED.
        string[]? dark = b.Dark, light = b.Light;
        if (Palette == "plain") { dark = null; light = null; }
        else if (Theme.Palettes.TryGetValue(Palette, out var theme)) { dark = theme.Dark; light = theme.Light; }
        string fallbackAccent = (dark ?? light) is { } any && any.Length > 5 ? any[5] : "";
        string[] colors = Mode == "light"
            ? (string[])(light ?? new[] { "#EBEBEB", "#2D2D2D", "#6B6B6B", "#FFFFFF", "#D2D2D2", fallbackAccent }).Clone()
            : (string[])(dark ?? new[] { "#2D2D2D", "#FFFFFF", "#B4B4B4", "#4B4B4B", "#3C3C3C", fallbackAccent }).Clone();
        if (Mode == "oled") { colors[0] = "#000000"; }
        if (Accent.Length > 0) colors[5] = Accent;

        var s = b with
        {
            Id = Id, Name = Name,
            Description = MyStyles.Describe(this),
            Dark = Mode == "light" ? null : colors,
            Light = Mode == "light" ? colors : null,
            ForceLight = Mode == "light",
            Mode = Mode,
            Wallpaper = Wallpaper, Moving = Moving,
            Sounds = Sounds, Music = Music, LaunchChime = null,
            Glyphs = Glyphs,
            Font = Font == "style" ? b.Font : Font,
            Buttons = ButtonShape,
            Portrait = TileArt == "tall", Wide = TileArt == "wide",
            Footer = Footer,
            Layout = "classic", NavTabs = false,
            IconSkin = MyStyles.IconFolder(IconsFrom),
        };
        if (Corners >= 0) s = s with { TileCorner = Corners, RowCorner = Math.Max(4, Corners) };
        // Your own highlight color replaces a gradient ring.
        if (Accent.Length > 0) s = s with { RingColors = null };

        // The buttons' fill.
        s = s with
        {
            OutlineButtons = ButtonFill == "outline",
            ButtonLook = ButtonFill is "accent" or "neon" ? ButtonFill : "",
            Gloss = ButtonFill == "glossy" || (Effect == "y2k" && ButtonFill == "solid"),
            Glass = ButtonFill == "frosted",
            DarkGlass = ButtonFill == "frosted" && Mode != "light",
            LightGlass = ButtonFill == "frosted" && Mode == "light",
            Glow = Glow,
        };
        // Outlines need something to draw with.
        if (ButtonFill is "outline" or "neon" && s.Stroke == null) s = s with { Stroke = "accent", StrokeWidth = 2 };

        // The shelf behind the buttons.
        s = s with { ButtonDock = Shelf != "none", DockLook = Shelf == "none" ? "glass" : Shelf };

        // The effect: a pattern over everything, a moving decoration, and a few touches to match.
        var fx = MyStyles.Effects.FirstOrDefault(e => e.Id == Effect);
        if (fx.Id != null && fx.Id != "style")
        {
            s = s with { Overlay = fx.Overlay, Ambient = fx.Ambient };
            if (fx.Id == "comic") s = s with { Shadow = "hard", Stroke = "#000000", StrokeWidth = 2, RingThickness = 5 };
            if (fx.Glow) s = s with { Glow = true };
        }
        return s;
    }

    /// <summary>A new style that starts as a copy of a ready-made one (and your animations as they are now).</summary>
    public static CustomStyle From(UiStyle b, Settings settings, string name)
    {
        string fill = b.Glass ? "frosted" : b.OutlineButtons ? "outline" : b.ButtonLook is "accent" or "neon" ? b.ButtonLook : b.Gloss ? "glossy" : "solid";
        return new CustomStyle
        {
            Name = name, BasedOn = b.Id,
            Wallpaper = b.Wallpaper, Moving = b.Moving,
            Music = b.Music, Sounds = b.Sounds, NavSounds = "",
            Mode = b.ThemeMode is "light" or "oled" ? b.ThemeMode : "dark",
            Palette = "style", Accent = "", Font = "style", Corners = -1,
            ButtonShape = b.Buttons, ButtonFill = fill, Glow = b.Glow,
            Shelf = b.ButtonDock ? b.DockLook : "none", Footer = b.Footer, Effect = "style",
            Glyphs = b.Glyphs ?? settings.GlyphLook, TileArt = b.Portrait ? "tall" : b.Wide ? "wide" : "square",
            ContinueTile = settings.ContinueTile,
            ButtonAnimation = settings.ButtonAnimations ? settings.ButtonAnimationStyle : "grow",
            PageAnimation = settings.PageTransition, TileAnimation = settings.TileHighlight,
            BootAnimation = settings.BootAnimation, ExitAnimation = settings.ExitStyle, LaunchAnimation = settings.LaunchAnimation,
        };
    }
}

public static class MyStyles
{
    public const string Prefix = "custom-";
    public const string Extension = ".sdstyle";

    public static string NewId() => Prefix + Guid.NewGuid().ToString("N")[..10];
    public static bool IsCustom(string? id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Where a style's own files (your picture, video or lock screen picture) are kept.</summary>
    public static string FolderFor(string id) => Path.Combine(Settings.DataDir, "Styles", id);

    /// <summary>A ready-made style to start from: Basic, Advanced, Legendary or Mythical (My own style can start from any).</summary>
    public static UiStyle BuiltIn(string id) =>
        UiStyles.All.FirstOrDefault(x => x.Id == UiStyles.Upgrade(id)) ?? UiStyles.Advanced.FirstOrDefault(x => x.Id == id) ?? UiStyles.All[0];

    /// <summary>Which tab a ready-made style is on.</summary>
    public static string TierOf(UiStyle s) =>
        s.Mythical ? "Mythical" : s.Unlock != null ? "Unlockable" : s.Legendary ? "Legendary" : UiStyles.IsAdvanced(s.Id) ? "Advanced" : "Basic";

    /// <summary>Every ready-made style there is to remix from, in tab order (locked Unlockable ones aren't offered yet).</summary>
    public static List<UiStyle> Everything()
    {
        var list = new List<UiStyle>(UiStyles.All);
        list.AddRange(UiStyles.Advanced.Where(x => !x.Legendary && !x.Mythical && x.Unlock == null));
        list.AddRange(UiStyles.Advanced.Where(x => x.Legendary && !x.Mythical && x.Unlock == null));
        list.AddRange(UiStyles.Advanced.Where(x => x.Mythical));
        list.AddRange(UiStyles.Advanced.Where(x => x.Unlock != null && !StyleUnlocks.IsLocked(x)));
        return list;
    }

    /// <summary>The Skins folder with a style's home button icons, or null when it has none.</summary>
    public static string? IconFolder(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return null;
        var st = UiStyles.Advanced.FirstOrDefault(x => x.Id == styleId);
        if (st?.Skin is not { } skin) return null;
        try
        {
            var folder = Skins.FolderFor(skin, null);
            return Directory.Exists(Path.Combine(folder, "icons")) ? folder : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The styles whose home button icons can be borrowed.</summary>
    public static List<UiStyle> IconSets() => Everything().Where(x => IconFolder(x.Id) != null).ToList();

    /// <summary>
    /// Effects: a pattern laid over everything, a gentle moving decoration behind the home screen, and whether the
    /// selection glows. Built from the pieces the ready-made styles use.
    /// </summary>
    public static readonly (string Id, string Name, string About, string Overlay, string Ambient, bool Glow)[] Effects =
    {
        ("style", "From the starting style", "Whatever the style you started from has", "", "", false),
        ("none", "None", "Clean, nothing over the screen", "none", "", false),
        ("halftone", "Halftone", "Printed dots over everything", "halftone", "", false),
        ("comic", "Comic book", "Halftone dots, thick black outlines, hard shadows and stickers", "halftone", "stickers", false),
        ("retro", "Retro tape", "Scan lines and a worn VHS tape rolling by", "scanlines", "vhs", false),
        ("arcade", "Arcade", "Scan lines and chasing marquee bulbs", "scanlines", "marquee", false),
        ("pixel", "Pixel night", "Blinking pixel stars", "none", "pixelstars", false),
        ("futuristic", "Futuristic", "A heads-up display with pulsing status lights", "hud", "status", true),
        ("cockpit", "Sci-fi cockpit", "Cockpit framing and a sweeping radar", "cockpit", "radar", true),
        ("synthwave", "Synthwave", "A neon floor racing to the horizon", "none", "horizon", true),
        ("y2k", "Y2K", "Chrome sheen, twinkling sparkles and glossy buttons", "sheen", "sparkle", true),
        ("dreamcore", "Dreamcore", "A soft dark edge and floating glassy shapes", "vignette", "float", true),
        ("glitch", "Glitch", "Scan lines and a signal that breaks up now and then", "scanlines", "glitch", false),
        ("cozy", "Cozy", "Paper grain and soft drifting lights", "paper", "bokeh", false),
        ("autumn", "Autumn", "Leaves drifting down past a soft dark edge", "vignette", "leaves", false),
        ("scrapbook", "Scrapbook", "Notebook paper with stickers and washi tape", "notebook", "stickers", false),
        ("storybook", "Storybook", "Paper and an inked compass rose", "paper", "compass", false),
        ("party", "Party", "Dots and bobbing plastic confetti", "dots", "confetti", false),
        ("blueprint", "Blueprint", "Drafting lines", "blueprint", "", false),
        ("carbon", "Carbon fiber", "A woven carbon pattern", "carbon", "", false),
        ("lcd", "Handheld LCD", "The pixel grid of an old handheld screen", "lcd", "", false),
        ("vignette", "Vignette", "A soft dark edge around the screen", "vignette", "", false),
    };

    public static readonly (string Id, string Name, string About)[] Shapes =
    {
        ("circle", "Round", "Switch's round buttons"), ("large", "Big round", "Larger round buttons"),
        ("small", "Small round", "Small round buttons"), ("soft", "Rounded square", "Soft squircles"),
        ("square", "Square", "Square buttons"), ("card", "Card", "Cards with the name under the icon"),
        ("tab", "Pill", "Pills with the icon and name"), ("wide", "Wide card", "Wide cards with the name"),
    };

    public static readonly (string Id, string Name, string About)[] Fills =
    {
        ("solid", "Solid", "Filled with the button color"), ("outline", "Outline", "See-through with an outline"),
        ("accent", "Highlight color", "Filled with the highlight color"), ("neon", "Neon", "Glowing rings in the highlight color"),
        ("glossy", "Glossy", "A shine across the top, like polished plastic"), ("frosted", "Frosted glass", "Frosted glass buttons and panels"),
    };

    public static readonly (string Id, string Name, string About)[] Shelves =
    {
        ("none", "No shelf", "The buttons sit on their own"), ("glass", "Glass", "A frosted glass shelf"),
        ("solid", "Solid", "A solid shelf"), ("outline", "Outline", "An outlined shelf"), ("glow", "Glow", "A shelf that glows in the highlight color"),
    };

    public static readonly (string Id, string Name, string About)[] Footers =
    {
        ("line", "Line", "A thin line above the hints"), ("bar", "Bar", "A full-width bar"),
        ("pill", "Floating pill", "A floating pill"), ("none", "None", "Just the hints"),
    };

    public static readonly (string Id, string Name, string About)[] Arts =
    {
        ("square", "Square", "The square cover"), ("tall", "Tall", "Tall 2:3 poster art"), ("wide", "Rectangle", "The wide header capsule"),
    };

    public static readonly (string Id, string Name)[] Modes = { ("light", "Light"), ("dark", "Dark"), ("oled", "OLED (true black)") };

    public static readonly (string Name, int Radius)[] CornerChoices =
        { ("Square", 0), ("Slightly rounded", 6), ("Rounded", 14), ("Round", 24), ("Very round", 34) };

    public static readonly (string Name, string Hex)[] MoreAccents =
    {
        ("Orange", "#FF7A1A"), ("Coral", "#FF5F6D"), ("Hot pink", "#FF3FB4"), ("Purple", "#9B5DE5"), ("Cyan", "#2FE0FF"),
        ("Teal", "#1FC9A8"), ("Lime", "#B6F23D"), ("Sunflower", "#FFC93C"),
    };

    /// <summary>A line about the style, shown under its card.</summary>
    public static string Describe(CustomStyle c)
    {
        var parts = new List<string>();
        parts.Add(c.Moving.Length > 0 ? "Moving background" : c.Wallpaper.Length > 0 ? "Wallpaper" : "Plain background");
        parts.Add(Modes.FirstOrDefault(m => m.Id == c.Mode).Name ?? "Dark");
        if (c.Effect is not ("style" or "none")) parts.Add(Effects.FirstOrDefault(e => e.Id == c.Effect).Name ?? "");
        parts.Add((Shapes.FirstOrDefault(x => x.Id == c.ButtonShape).Name ?? "Round") + " buttons");
        return "Your style: " + string.Join(", ", parts.Where(p => p.Length > 0)) + ".";
    }

    /// <summary>Your styles, as styles, for <see cref="UiStyles.Find"/>.</summary>
    public static void Load(Settings settings)
    {
        var list = new List<UiStyle>();
        foreach (var c in settings.CustomStyles ?? new())
        {
            try { list.Add(c.ToStyle()); } catch (Exception) { }
        }
        UiStyles.SetCustom(list);
    }

    /// <summary>What a style of your own sets beyond its looks: sounds for moving, animations, button pictures, Continue, the lock screen.</summary>
    public static void ApplyExtras(Settings s, CustomStyle c)
    {
        s.NavSoundSet = c.NavSounds;
        s.GlyphLook = c.Glyphs;
        Hints.SetLook(s.GlyphLook);
        s.ContinueTile = c.ContinueTile;
        s.ButtonAnimations = true;
        s.ButtonAnimationStyle = c.ButtonAnimation;
        s.PageTransition = c.PageAnimation;
        s.TileHighlight = c.TileAnimation;
        s.BootAnimation = c.BootAnimation;
        s.ExitStyle = c.ExitAnimation;
        s.ExitAnimation = true;
        s.LaunchAnimation = c.LaunchAnimation;
        if (c.UpdateLook.Length > 0) s.UpdaterLook = c.UpdateLook;
        if (c.UpdateHaptic.Length > 0) s.UpdaterHaptic = c.UpdateHaptic;
        // The style's own font and game art (your choices in Look and feel would cover them).
        s.UiFont = "style";
        s.TileArt = "style";
    }

    /// <summary>The picture to put on the lock screen for this style, or null to leave it alone.</summary>
    public static string? LockScreenFile(CustomStyle c)
    {
        string pick = c.LockScreen == "wallpaper" ? c.Wallpaper : c.LockScreen;
        if (pick.Length == 0 || pick.StartsWith("preset:", StringComparison.Ordinal)) return null;
        string path = pick.StartsWith("image:", StringComparison.Ordinal) ? Path.Combine(AppContext.BaseDirectory, "Wallpapers", pick[6..]) : pick;
        return File.Exists(path) ? path : null;
    }

    /// <summary>Copies a file of yours into the style's own folder (so it keeps working if the original moves) and returns the copy.</summary>
    public static string Keep(string id, string source, string kind)
    {
        var folder = FolderFor(id);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, kind + Path.GetExtension(source).ToLowerInvariant());
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, target, true);
        return target;
    }

    /// <summary>Forgets a style and its files.</summary>
    public static void Delete(Settings s, CustomStyle c)
    {
        s.CustomStyles.RemoveAll(x => x.Id == c.Id);
        try { if (Directory.Exists(FolderFor(c.Id))) Directory.Delete(FolderFor(c.Id), true); } catch (Exception) { }
    }

    /// <summary>A copy with a new id and its own copies of the files.</summary>
    public static CustomStyle Duplicate(CustomStyle c, string name)
    {
        var copy = c.Copy();
        copy.Id = NewId();
        copy.Name = name;
        copy.Saved = DateTime.Now;
        copy.Wallpaper = CopyIfOwn(c.Id, copy.Id, c.Wallpaper);
        copy.Moving = CopyIfOwn(c.Id, copy.Id, c.Moving);
        copy.LockScreen = CopyIfOwn(c.Id, copy.Id, c.LockScreen);
        return copy;
    }

    private static string CopyIfOwn(string fromId, string toId, string value)
    {
        if (!Path.IsPathRooted(value) || !File.Exists(value)) return value;
        if (!value.StartsWith(FolderFor(fromId), StringComparison.OrdinalIgnoreCase)) return value;
        var folder = FolderFor(toId);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(value));
        File.Copy(value, target, true);
        return target;
    }

    /// <summary>A name not used by another of your styles ("Neon", "Neon (2)", ...).</summary>
    public static string UniqueName(Settings s, string name, string? exceptId = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? "My style" : name.Trim();
        bool Taken(string n) => s.CustomStyles.Any(x => x.Id != exceptId && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        for (int i = 2; ; i++)
            if (!Taken($"{name} ({i})")) return $"{name} ({i})";
    }

    // ================================================================ export and import

    private const string BundlePrefix = "bundle:";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Saves a style as a .sdstyle file (a zip): style.json, plus any files of your own it uses: your picture,
    /// video and lock screen picture, a music track you added, and sound packs you added.
    /// </summary>
    public static string Export(CustomStyle c, string folder)
    {
        var copy = c.Copy();
        string safe = string.Concat(c.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "My style";
        string path = Path.Combine(folder, safe + Extension);
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        string Bundle(string value, string name)
        {
            if (!Path.IsPathRooted(value) || !File.Exists(value)) return value;
            string entry = "files/" + name + Path.GetExtension(value).ToLowerInvariant();
            zip.CreateEntryFromFile(value, entry);
            return BundlePrefix + entry;
        }
        copy.Wallpaper = Bundle(c.Wallpaper, "wallpaper");
        copy.Moving = Bundle(c.Moving, "moving");
        copy.LockScreen = Bundle(c.LockScreen, "lockscreen");

        // Music you added yourself.
        if (c.Music.StartsWith("track:", StringComparison.Ordinal) && BackgroundMusic.List().FirstOrDefault(t => t.Id == c.Music) is { } track
            && track.File.StartsWith(BackgroundMusic.UserDir, StringComparison.OrdinalIgnoreCase))
            zip.CreateEntryFromFile(track.File, "music/" + Path.GetFileName(track.File));

        // Sound packs you added yourself (the ones that come with SquareDeck are already there for everyone).
        foreach (var id in new[] { c.Sounds, c.NavSounds, c.Music }.Distinct())
        {
            if (SoundPacks.Find(id) is not { } pack || !pack.Folder.StartsWith(SoundPacks.UserDir, StringComparison.OrdinalIgnoreCase)) continue;
            string packName = Path.GetFileName(pack.Folder);
            foreach (var file in Directory.EnumerateFiles(pack.Folder, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, "packs/" + packName + "/" + Path.GetRelativePath(pack.Folder, file).Replace('\\', '/'));
        }

        var entryJson = zip.CreateEntry("style.json");
        using (var writer = new StreamWriter(entryJson.Open()))
            writer.Write(JsonSerializer.Serialize(copy, Json));
        return path;
    }

    /// <summary>Reads a .sdstyle file into a new style of yours (not yet saved to settings).</summary>
    public static CustomStyle Import(string file, Settings s)
    {
        using var zip = ZipFile.OpenRead(file);
        var jsonEntry = zip.GetEntry("style.json") ?? throw new InvalidDataException("This isn't a SquareDeck style (style.json is missing).");
        CustomStyle style;
        using (var reader = new StreamReader(jsonEntry.Open()))
            style = JsonSerializer.Deserialize<CustomStyle>(reader.ReadToEnd()) ?? throw new InvalidDataException("The style couldn't be read.");

        style.Id = NewId();
        style.Saved = DateTime.Now;
        style.Name = UniqueName(s, style.Name ?? "Imported style");
        var folder = FolderFor(style.Id);
        Directory.CreateDirectory(folder);

        // Only plain relative names inside the zip are accepted, written only inside the folders they belong in.
        static string? Inside(string root, string relative)
        {
            if (relative.Length == 0 || relative.Contains("..") || Path.IsPathRooted(relative)) return null;
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            string? target = null;
            if (entry.FullName.StartsWith("files/", StringComparison.Ordinal)) target = Inside(folder, entry.FullName[6..]);
            else if (entry.FullName.StartsWith("music/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(BackgroundMusic.UserDir);
                target = Inside(BackgroundMusic.UserDir, entry.FullName[6..]);
                if (target != null && File.Exists(target)) continue; // keep yours
            }
            else if (entry.FullName.StartsWith("packs/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(SoundPacks.UserDir);
                target = Inside(SoundPacks.UserDir, entry.FullName[6..]);
                if (target != null && File.Exists(target)) continue;
            }
            if (target == null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
        SoundPacks.Refresh();

        string Unbundle(string value) =>
            value.StartsWith(BundlePrefix, StringComparison.Ordinal) && Inside(folder, value[(BundlePrefix.Length + 6)..]) is { } path && File.Exists(path) ? path
            : Path.IsPathRooted(value) ? "" // a file on someone else's computer
            : value;
        // Names of built-in pictures and backgrounds are plain names; anything that reaches into other folders is dropped.
        string Clean(string? value)
        {
            value ??= "";
            if (value.StartsWith(BundlePrefix, StringComparison.Ordinal)) return Unbundle(value);
            if (Path.IsPathRooted(value)) return ""; // a file on someone else's computer
            return value.Contains("..") || value.Contains('/') || value.Contains('\\') ? "" : value;
        }
        style.Wallpaper = Clean(style.Wallpaper);
        style.Moving = Clean(style.Moving);
        style.LockScreen = Clean(style.LockScreen);
        style.Name ??= "Imported style";
        style.Music ??= ""; style.Sounds ??= "classic"; style.NavSounds ??= ""; style.Palette ??= "style"; style.Accent ??= "";
        style.Font ??= "style"; style.Glyphs ??= "text";
        style.ButtonAnimation ??= "grow"; style.PageAnimation ??= "slide"; style.TileAnimation ??= "pulse";
        style.BootAnimation ??= "logo"; style.ExitAnimation ??= "logo"; style.LaunchAnimation ??= "classic";
        if (style.Music.Contains("..") || style.Music.Contains('/') || style.Music.Contains('\\')) style.Music = "";
        if (style.Accent.Length > 0 && !Theme.TryParseColor(style.Accent, out _)) style.Accent = "";
        if (style.Font.Contains("..") || style.Font.Contains('/') || style.Font.Contains('\\')) style.Font = "style";
        // Choices this version doesn't know fall back to the usual ones.
        if (Modes.All(m => m.Id != style.Mode)) style.Mode = "dark";
        if (Effects.All(e => e.Id != style.Effect)) style.Effect = "style";
        if (Shapes.All(x => x.Id != style.ButtonShape)) style.ButtonShape = "circle";
        if (Fills.All(x => x.Id != style.ButtonFill)) style.ButtonFill = "solid";
        if (Shelves.All(x => x.Id != style.Shelf)) style.Shelf = "none";
        if (Footers.All(x => x.Id != style.Footer)) style.Footer = "line";
        if (Arts.All(x => x.Id != style.TileArt)) style.TileArt = "square";
        style.BasedOn = BuiltIn(style.BasedOn).Id;
        return style;
    }
}
