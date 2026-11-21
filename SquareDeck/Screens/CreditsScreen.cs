using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Credits: the UI kits the Advanced styles are made from, the sound packs and the fonts, with who made them
/// and the terms they're used under.
/// </summary>
public sealed class CreditsScreen : ListScreen
{
    public CreditsScreen() : base("Credits") { }

    private static readonly (string Name, string Author, string Use, string License, string? Link)[] Kits =
    {
        ("NEONIX UI", "GONKEN Games", "Neon Grid", "NEONIX UI assets by GONKEN Games — attribution required", "https://gonkengames.itch.io/"),
        ("Free Hologram Interface", "Wenrexa", "Hologram", "Free asset pack", null),
        ("Glowing UI icons", "Author not named in the pack", "Glow", "Free asset pack", null),
        ("Mega Cozy UI Pack and Cozy UI Pack (demos)", "dobo_ui", "Cozy Pixel", "Free demo packs", "https://dobo-ui.itch.io/"),
        ("Pixel UI Kit (free sample)", "Northleaf Studio", "Cozy Pixel, Arcade", "Royalty-free", "https://northleafstudio.itch.io/"),
        ("Tropical Night UI Lite", "Brunka Games", "Tropical Night", "Royalty-free inside finished projects", "https://brunkagames.itch.io/"),
        ("Retro Windows GUI", "Author not named in the pack", "Retro Windows", "Free asset pack", null),
        ("Retro Arcade UI Mini Pack", "Author not named in the pack", "Arcade", "CC0 1.0 (public domain)", null),
        ("Softline UI Kit – Flat Night", "The Cooked Works", "Softline", "Free asset pack", null),
        ("Fern & Brass (free sample)", "Kaliopa Studio", "Fern & Brass", "Inside finished projects", null),
        ("Nebula UI (free sample)", "kama-labs", "Nebula", "UI: Nebula UI by kama-labs", null),
        ("Soft Modern UI Lite", "Author not named in the pack", "Nebula", "Free to use in projects", null),
        ("Indivial UI", "Author not named in the pack", "Indivial (three colors: icons, switches, home buttons, colors)", "Free asset pack", null),
        ("Scholar GUI", "Author not named in the pack", "Scholar (cards, speech-bubble pop-ups, buttons, check box, pointer)", "Free asset pack", null),
        ("Sapphire GUI", "Author not named in the pack", "Sapphire (three colors)", "Free asset pack", null),
        ("Spectrum UI", "Author not named in the pack", "Spectrum (five colors)", "Free asset pack", null),
        ("Fatanzia UI", "Author not named in the pack", "Fatanzia (panels, buttons, icons, wallpaper)", "Free asset pack", null),
        ("Travel Book UI (Lite)", "Author not named in the pack", "Cozy Place", "Free Lite version", null),
        ("Windows XP UI pack", "NullTale", "Luna XP (frames, buttons, check boxes, icons, pointer)", "Packed by NullTale; Windows XP's look, icons, sounds and Bliss belong to Microsoft", "https://nulltale.itch.io/"),
    };

    private static readonly (string Name, string Author, string Use, string License, string? Link)[] Sounds =
    {
        ("Free Essential UI Sound Pack", "CAPTNPHANTOM", "Essential UI and Essential UI Soft sounds", "UI SFX by CAPTNPHANTOM", null),
        ("UI & Menu SFX (free)", "Northleaf Studio", "Northleaf UI sounds", "Royalty-free", "https://northleafstudio.itch.io/"),
        ("At Rest, Daybreak and Thinking of You", "Kevin MacLeod (incompetech.com)", "Scholar's music, and in the music list", "Licensed under Creative Commons: By Attribution 4.0", "https://incompetech.com/"),
        ("Windows 2000 sounds", "Microsoft", "Windows 2000 sounds (Retro Windows)", "From Sample Focus", null),
        ("Windows 2000 icons", "Microsoft", "Retro Windows (Home button icons)", "Windows 2000's icons belong to Microsoft; from a Stardock IconPackager set", null),
        ("Windows XP sounds", "Microsoft", "Windows XP sounds (Luna XP)", "From the NullTale pack", null),
    };

    private static readonly (string Name, string Author, string Use, string License, string? Link)[] Fonts =
    {
        ("VT323", "Peter Hull", "Arcade", "SIL Open Font License 1.1", null),
        ("Jersey 10", "Sarah Cadigan-Fried", "Tropical Night", "SIL Open Font License 1.1", null),
        ("Pixel Operator", "Jayvee Enaguas", "Cozy Pixel", "CC0 (public domain)", null),
        ("Barlow Condensed", "Jeremy Tribby", "Sapphire", "SIL Open Font License 1.1", null),
        ("Afacad Flux", "Kristian Möller", "Scholar", "SIL Open Font License 1.1", null),
        ("OpenDyslexic", "Abbie Gonzalez", "Font list", "SIL Open Font License 1.1", null),
        ("Windows 2000 and XP (font)", "Author not named in the font", "Retro Windows", "Supplied by you", null),
        ("WindowsXPlike A", "Author not named in the font", "Luna XP", "Supplied by you", null),
    };

    protected override void Build()
    {
        AddNote("The Advanced styles are made from these UI kits, sound packs and fonts. Thank you to everyone who made them.");
        Section("UI kits", "", Kits);
        Section("Sounds", "", Sounds);
        Section("Fonts", "", Fonts);
        AddNote("Each kit, sound pack and font stays its makers' work, under its own terms. SquareDeck's own code and artwork are separate.");
    }

    private void Section(string title, string glyph, (string Name, string Author, string Use, string License, string? Link)[] items)
    {
        AddHeader(title);
        foreach (var (name, author, use, license, link) in items)
        {
            string? url = link;
            AddRow(name, url != null ? "Open page" : null, glyph,
                click: url != null ? new Action(() => ProcessUtil.Open(url)) : null,
                subtitle: "By " + author + "  \u00B7  Used in " + use + "  \u00B7  " + license);
        }
    }
}
