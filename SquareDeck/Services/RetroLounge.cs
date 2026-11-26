using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

/// <summary>A console (or computer) Retro Lounge knows: where its games live, what plays them, what it needs.</summary>
public sealed class RetroSystem
{
    public required string Id, Name, Short, Maker;
    public int Year;
    /// <summary>Folder names it's found under (ES-DE, EmuDeck, RetroBat and RetroArch names): the first is the one Retro Lounge makes.</summary>
    public required string[] Folders;
    public required string[] Extensions;
    /// <summary>What plays it, best first: an emulator id, or "ra:core" for RetroArch with that core.</summary>
    public required string[] Players;
    /// <summary>The libretro-thumbnails list its box art comes from.</summary>
    public string Thumbs = "";
    public required string Color1, Color2;
    /// <summary>BIOS files: each entry is one file, or alternatives separated by '|' (any one will do).</summary>
    public string[] Bios = Array.Empty<string>();
    public bool BiosRequired;
    public string BiosNote = "";
    /// <summary>Games are folders (a PS3 or PS4 game), found by the file inside.</summary>
    public string? FolderGameFile;
    public bool Disc;
}

/// <summary>An emulator Retro Lounge can download, update, set up and start.</summary>
public sealed class RetroEmulator
{
    public required string Id, Name, Blurb, License, Site;
    public required string[] Systems;
    /// <summary>Where it downloads from: "github", "page" (a link found on its download page) or "retroarch".</summary>
    public string Source = "github";
    public string Repo = "";
    /// <summary>The Windows download's name (GitHub) or link (page), as a pattern.</summary>
    public string Asset = "";
    public string Page = "";
    public bool Prerelease;
    /// <summary>Its program's name (a pattern when it carries a version).</summary>
    public required string Program;
    /// <summary>The command line: {rom} is the game, {core} a RetroArch core. {fs} adds the full-screen switch.</summary>
    public string Args = "\"{rom}\"";
    public string FullScreen = "";
    /// <summary>Files or folders that keep it portable (its settings and saves in its own folder).</summary>
    public string[] Portable = Array.Empty<string>();
    /// <summary>Its BIOS folder inside its own, where Retro Lounge copies the BIOS files it needs.</summary>
    public string BiosDir = "";
    public string Note = "";
}

/// <summary>An emulator installed by Retro Lounge, or found where the player already had it.</summary>
public sealed class RetroEmuInstall
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Program { get; set; } = "";
    public bool Located { get; set; }
    public DateTime InstalledAt { get; set; }
}

public sealed record RetroRelease(string Version, string Url, string FileName, long Size, string? Sha256, string Page);

/// <summary>One game found in the ROM folders (several discs are one game).</summary>
public sealed class RetroGame
{
    public string Path { get; set; } = "";
    public string System { get; set; } = "";
    public string Name { get; set; } = "";      // the file's name without its extension: "Super Mario World (USA)"
    public string Title { get; set; } = "";     // "Super Mario World"
    public List<string> Tags { get; set; } = new();
    public string Region { get; set; } = "";
    public List<string> Discs { get; set; } = new();
    public long Size { get; set; }
    public DateTime Added { get; set; }
    public string Key => Path.ToLowerInvariant();
    public string Format => System.Length == 0 ? "" : (Directory.Exists(Path) ? "Folder" : global::System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant());
}

/// <summary>What the player did with a game: favorite, last played, how long, which emulator.</summary>
public sealed class RetroGameState
{
    public bool Favorite { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int Plays { get; set; }
    public double Minutes { get; set; }
    public string? Player { get; set; }
    public bool Hidden { get; set; }
}

public sealed class RetroConfig
{
    public List<string> Roots { get; set; } = new();
    public string BiosFolder { get; set; } = "";
    public string Region { get; set; } = "USA";
    public bool OnePerGame { get; set; } = true;
    public bool FullScreen { get; set; } = true;
    public bool OnlineArt { get; set; } = true;
    /// <summary>The emulator chosen for each console (otherwise the best one installed).</summary>
    public Dictionary<string, string> SystemPlayer { get; set; } = new();
    public Dictionary<string, RetroGameState> Games { get; set; } = new();
    public bool Welcomed { get; set; }
}

/// <summary>
/// Retro Lounge: emulators and your own game files in one place. Emulators download from their own projects (GitHub
/// releases, or the download link on their site), unpacked into Retro Lounge's folder and set up to keep their
/// settings and saves there (portable, as EmuDeck and RetroBat do it). Games are found in your ROM folders by console
/// (folder names as ES-DE, EmuDeck and RetroBat use them), with box art from libretro-thumbnails (as RetroArch shows
/// it) or your own pictures. Nothing here downloads games or BIOS files: those come from what you own.
/// </summary>
public static class RetroLounge
{
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck-RetroLounge/1.0 (+https://github.com/tetraguy/SquareDeck)");
        return c;
    }

    public static string Folder => Path.Combine(Settings.DataDir, "RetroLounge");
    public static string EmulatorsFolder => Path.Combine(Folder, "Emulators");
    private static string ConfigFile => Path.Combine(Folder, "config.json");
    private static string InstalledFile => Path.Combine(Folder, "emulators.json");
    private static string LibraryFile => Path.Combine(Folder, "library.json");
    private static string ArtFolder => Path.Combine(Folder, "Art");

    // ================================================================ consoles

    public static readonly RetroSystem[] Systems =
    {
        new() { Id = "nes", Name = "Nintendo Entertainment System", Short = "NES", Maker = "Nintendo", Year = 1985, Folders = new[] { "nes", "famicom" },
            Extensions = new[] { ".nes", ".unf", ".unif", ".zip", ".7z" }, Players = new[] { "mesen", "ra:mesen", "ra:nestopia", "ares" },
            Thumbs = "Nintendo - Nintendo Entertainment System", Color1 = "#C8102E", Color2 = "#5A0E18" },
        new() { Id = "fds", Name = "Famicom Disk System", Short = "FDS", Maker = "Nintendo", Year = 1986, Folders = new[] { "fds" },
            Extensions = new[] { ".fds", ".zip", ".7z" }, Players = new[] { "mesen", "ra:mesen" }, Thumbs = "Nintendo - Family Computer Disk System",
            Color1 = "#D4A017", Color2 = "#5C3D00", Bios = new[] { "disksys.rom" }, BiosRequired = true },
        new() { Id = "snes", Name = "Super Nintendo", Short = "SNES", Maker = "Nintendo", Year = 1991, Folders = new[] { "snes", "sfc", "superfamicom" },
            Extensions = new[] { ".sfc", ".smc", ".fig", ".swc", ".bs", ".zip", ".7z" }, Players = new[] { "snes9x", "ra:snes9x", "ra:bsnes", "mesen", "ares" },
            Thumbs = "Nintendo - Super Nintendo Entertainment System", Color1 = "#7B5FC7", Color2 = "#2B1E57" },
        new() { Id = "n64", Name = "Nintendo 64", Short = "N64", Maker = "Nintendo", Year = 1996, Folders = new[] { "n64", "nintendo64" },
            Extensions = new[] { ".n64", ".z64", ".v64", ".zip", ".7z" }, Players = new[] { "rmg", "ra:mupen64plus_next", "ares" },
            Thumbs = "Nintendo - Nintendo 64", Color1 = "#1F9D55", Color2 = "#0B3D6E" },
        new() { Id = "gb", Name = "Game Boy", Short = "GB", Maker = "Nintendo", Year = 1989, Folders = new[] { "gb", "gameboy" },
            Extensions = new[] { ".gb", ".zip", ".7z" }, Players = new[] { "mgba", "ra:gambatte", "mesen", "ares" },
            Thumbs = "Nintendo - Game Boy", Color1 = "#8BAC0F", Color2 = "#2F4A0B" },
        new() { Id = "gbc", Name = "Game Boy Color", Short = "GBC", Maker = "Nintendo", Year = 1998, Folders = new[] { "gbc", "gameboycolor" },
            Extensions = new[] { ".gbc", ".zip", ".7z" }, Players = new[] { "mgba", "ra:gambatte", "mesen", "ares" },
            Thumbs = "Nintendo - Game Boy Color", Color1 = "#E8317B", Color2 = "#5B1442" },
        new() { Id = "gba", Name = "Game Boy Advance", Short = "GBA", Maker = "Nintendo", Year = 2001, Folders = new[] { "gba", "gameboyadvance" },
            Extensions = new[] { ".gba", ".zip", ".7z" }, Players = new[] { "mgba", "ra:mgba" },
            Thumbs = "Nintendo - Game Boy Advance", Color1 = "#4B3CC9", Color2 = "#1A1550", Bios = new[] { "gba_bios.bin" } },
        new() { Id = "nds", Name = "Nintendo DS", Short = "DS", Maker = "Nintendo", Year = 2004, Folders = new[] { "nds", "ds" },
            Extensions = new[] { ".nds", ".dsi", ".zip", ".7z" }, Players = new[] { "melonds", "ra:melondsds" },
            Thumbs = "Nintendo - Nintendo DS", Color1 = "#9AA3AD", Color2 = "#2C3036", Bios = new[] { "bios7.bin", "bios9.bin", "firmware.bin" },
            BiosNote = "Optional: melonDS has its own free BIOS. Real ones fix a few games." },
        new() { Id = "n3ds", Name = "Nintendo 3DS", Short = "3DS", Maker = "Nintendo", Year = 2011, Folders = new[] { "n3ds", "3ds" },
            Extensions = new[] { ".3ds", ".cci", ".cxi", ".3dsx", ".app", ".z3ds", ".zcci", ".zcxi" }, Players = new[] { "azahar" },
            Thumbs = "Nintendo - Nintendo 3DS", Color1 = "#D2152B", Color2 = "#4A0710", BiosNote = "Decrypted games need nothing else." },
        new() { Id = "gc", Name = "GameCube", Short = "GCN", Maker = "Nintendo", Year = 2001, Folders = new[] { "gc", "gamecube", "ngc" },
            Extensions = new[] { ".iso", ".gcm", ".gcz", ".rvz", ".ciso", ".dol", ".m3u" }, Players = new[] { "dolphin", "ra:dolphin" },
            Thumbs = "Nintendo - GameCube", Color1 = "#5B3E96", Color2 = "#1E1238", Disc = true },
        new() { Id = "wii", Name = "Wii", Short = "Wii", Maker = "Nintendo", Year = 2006, Folders = new[] { "wii" },
            Extensions = new[] { ".iso", ".wbfs", ".rvz", ".wia", ".gcz", ".wad", ".ciso" }, Players = new[] { "dolphin", "ra:dolphin" },
            Thumbs = "Nintendo - Wii", Color1 = "#4FB3E8", Color2 = "#123E5C", Disc = true },
        new() { Id = "wiiu", Name = "Wii U", Short = "Wii U", Maker = "Nintendo", Year = 2012, Folders = new[] { "wiiu" },
            Extensions = new[] { ".wua", ".wud", ".wux", ".rpx" }, Players = new[] { "cemu" }, Thumbs = "Nintendo - Wii U",
            Color1 = "#0099C6", Color2 = "#003A4D", BiosNote = "Encrypted dumps (.wud, .wux) need your keys.txt in Cemu's folder.", Disc = true },
        new() { Id = "virtualboy", Name = "Virtual Boy", Short = "VB", Maker = "Nintendo", Year = 1995, Folders = new[] { "virtualboy", "vb" },
            Extensions = new[] { ".vb", ".vboy", ".zip", ".7z" }, Players = new[] { "ra:mednafen_vb" }, Thumbs = "Nintendo - Virtual Boy", Color1 = "#E10600", Color2 = "#300000" },
        new() { Id = "psx", Name = "PlayStation", Short = "PS1", Maker = "Sony", Year = 1994, Folders = new[] { "psx", "ps1", "playstation" },
            Extensions = new[] { ".cue", ".chd", ".pbp", ".m3u", ".iso", ".img", ".ccd", ".mds", ".ecm", ".exe" }, Players = new[] { "duckstation", "ra:mednafen_psx_hw", "ra:swanstation" },
            Thumbs = "Sony - PlayStation", Color1 = "#6E7681", Color2 = "#1B1F24", Disc = true,
            Bios = new[] { "scph5501.bin|scph5500.bin|scph5502.bin|scph1001.bin|scph7001.bin|scph101.bin|ps1_rom.bin" }, BiosRequired = true,
            BiosNote = "One PlayStation BIOS, from your own console." },
        new() { Id = "ps2", Name = "PlayStation 2", Short = "PS2", Maker = "Sony", Year = 2000, Folders = new[] { "ps2", "playstation2" },
            Extensions = new[] { ".iso", ".chd", ".cso", ".zso", ".gz", ".cue", ".elf", ".mdf" }, Players = new[] { "pcsx2" },
            Thumbs = "Sony - PlayStation 2", Color1 = "#1F3FAF", Color2 = "#08133D", Disc = true,
            Bios = new[] { "ps2:*" }, BiosRequired = true, BiosNote = "A PS2 BIOS from your own console (a 4 MB .bin, any region)." },
        new() { Id = "ps3", Name = "PlayStation 3", Short = "PS3", Maker = "Sony", Year = 2006, Folders = new[] { "ps3", "playstation3" },
            Extensions = new[] { ".iso" }, Players = new[] { "rpcs3" }, Thumbs = "Sony - PlayStation 3", Color1 = "#2B2B2B", Color2 = "#050505",
            FolderGameFile = "PS3_GAME\\USRDIR\\EBOOT.BIN", BiosNote = "Install Sony's PS3 firmware inside RPCS3 (File > Install Firmware) once.", Disc = true },
        new() { Id = "psp", Name = "PlayStation Portable", Short = "PSP", Maker = "Sony", Year = 2004, Folders = new[] { "psp" },
            Extensions = new[] { ".iso", ".cso", ".pbp", ".chd", ".elf" }, Players = new[] { "ppsspp", "ra:ppsspp" },
            Thumbs = "Sony - PlayStation Portable", Color1 = "#3D3D3D", Color2 = "#0E0E0E" },
        new() { Id = "psvita", Name = "PlayStation Vita", Short = "Vita", Maker = "Sony", Year = 2011, Folders = new[] { "psvita", "vita" },
            Extensions = new[] { ".vpk", ".zip" }, Players = new[] { "vita3k" }, Thumbs = "Sony - PlayStation Vita", Color1 = "#0070D1", Color2 = "#001F3D",
            BiosNote = "Vita3K installs Sony's firmware and your games itself the first time it starts." },
        new() { Id = "ps4", Name = "PlayStation 4", Short = "PS4", Maker = "Sony", Year = 2013, Folders = new[] { "ps4" },
            Extensions = Array.Empty<string>(), Players = new[] { "shadps4" }, Color1 = "#00439C", Color2 = "#00102B", FolderGameFile = "eboot.bin",
            BiosNote = "Experimental: shadPS4 plays a growing list of games, from your own dumped game folders." },
        new() { Id = "genesis", Name = "Genesis / Mega Drive", Short = "MD", Maker = "Sega", Year = 1988, Folders = new[] { "genesis", "megadrive", "md" },
            Extensions = new[] { ".md", ".gen", ".bin", ".smd", ".zip", ".7z" }, Players = new[] { "ra:genesis_plus_gx", "ares" },
            Thumbs = "Sega - Mega Drive - Genesis", Color1 = "#1F1F1F", Color2 = "#B10D0D" },
        new() { Id = "mastersystem", Name = "Master System", Short = "SMS", Maker = "Sega", Year = 1986, Folders = new[] { "mastersystem", "sms" },
            Extensions = new[] { ".sms", ".zip", ".7z" }, Players = new[] { "ra:genesis_plus_gx", "mesen", "ares" },
            Thumbs = "Sega - Master System - Mark III", Color1 = "#C8102E", Color2 = "#1A1A1A" },
        new() { Id = "gamegear", Name = "Game Gear", Short = "GG", Maker = "Sega", Year = 1990, Folders = new[] { "gamegear", "gg" },
            Extensions = new[] { ".gg", ".zip", ".7z" }, Players = new[] { "ra:genesis_plus_gx", "mesen", "ares" },
            Thumbs = "Sega - Game Gear", Color1 = "#0067B1", Color2 = "#0A1A33" },
        new() { Id = "segacd", Name = "Sega CD / Mega-CD", Short = "SCD", Maker = "Sega", Year = 1992, Folders = new[] { "segacd", "megacd" },
            Extensions = new[] { ".cue", ".chd", ".iso", ".m3u" }, Players = new[] { "ra:genesis_plus_gx", "ares" }, Thumbs = "Sega - Mega-CD - Sega CD",
            Color1 = "#202020", Color2 = "#6B0000", Bios = new[] { "bios_CD_U.bin|bios_CD_E.bin|bios_CD_J.bin" }, BiosRequired = true, Disc = true },
        new() { Id = "sega32x", Name = "32X", Short = "32X", Maker = "Sega", Year = 1994, Folders = new[] { "sega32x", "32x" },
            Extensions = new[] { ".32x", ".zip", ".7z" }, Players = new[] { "ra:picodrive", "ares" }, Thumbs = "Sega - 32X", Color1 = "#D11F2B", Color2 = "#2A0A0D" },
        new() { Id = "saturn", Name = "Saturn", Short = "Saturn", Maker = "Sega", Year = 1994, Folders = new[] { "saturn" },
            Extensions = new[] { ".cue", ".chd", ".ccd", ".m3u", ".iso", ".mds" }, Players = new[] { "ra:mednafen_saturn", "ra:kronos" }, Thumbs = "Sega - Saturn",
            Color1 = "#3B3B98", Color2 = "#11112E", Bios = new[] { "sega_101.bin|mpr-17933.bin|saturn_bios.bin" }, BiosRequired = true, Disc = true },
        new() { Id = "dreamcast", Name = "Dreamcast", Short = "DC", Maker = "Sega", Year = 1999, Folders = new[] { "dreamcast", "dc" },
            Extensions = new[] { ".gdi", ".cdi", ".chd", ".cue", ".m3u" }, Players = new[] { "flycast", "ra:flycast" }, Thumbs = "Sega - Dreamcast",
            Color1 = "#F36F21", Color2 = "#3D1604", Bios = new[] { "dc_boot.bin", "dc_flash.bin" }, BiosNote = "Optional: Flycast starts most games without them.", Disc = true },
        new() { Id = "arcade", Name = "Arcade", Short = "Arcade", Maker = "Various", Year = 1978, Folders = new[] { "arcade", "fbneo", "mame" },
            Extensions = new[] { ".zip", ".7z" }, Players = new[] { "ra:fbneo", "ra:mame" }, Thumbs = "FBNeo - Arcade Games", Color1 = "#FFB000", Color2 = "#5C1A00",
            BiosNote = "Arcade games need sets that match the emulator's version (FinalBurn Neo by default)." },
        new() { Id = "neogeo", Name = "Neo Geo", Short = "NEO", Maker = "SNK", Year = 1990, Folders = new[] { "neogeo" },
            Extensions = new[] { ".zip", ".7z" }, Players = new[] { "ra:fbneo" }, Thumbs = "SNK - Neo Geo", Color1 = "#C9A227", Color2 = "#2B2100",
            Bios = new[] { "neogeo.zip" }, BiosRequired = true, BiosNote = "neogeo.zip goes in the BIOS folder (or next to the games)." },
        new() { Id = "pcengine", Name = "PC Engine / TurboGrafx-16", Short = "PCE", Maker = "NEC", Year = 1987, Folders = new[] { "pcengine", "tg16", "turbografx16" },
            Extensions = new[] { ".pce", ".sgx", ".zip", ".7z" }, Players = new[] { "ra:mednafen_pce_fast", "mesen", "ares" },
            Thumbs = "NEC - PC Engine - TurboGrafx 16", Color1 = "#F05A28", Color2 = "#3A1206" },
        new() { Id = "pcenginecd", Name = "PC Engine CD / TurboGrafx-CD", Short = "PCE-CD", Maker = "NEC", Year = 1988, Folders = new[] { "pcenginecd", "tg-cd", "turbografxcd" },
            Extensions = new[] { ".cue", ".chd", ".ccd", ".m3u" }, Players = new[] { "ra:mednafen_pce", "ra:mednafen_pce_fast" }, Thumbs = "NEC - PC Engine CD - TurboGrafx-CD",
            Color1 = "#E2462A", Color2 = "#2F0D06", Bios = new[] { "syscard3.pce" }, BiosRequired = true, Disc = true },
        new() { Id = "atari2600", Name = "Atari 2600", Short = "2600", Maker = "Atari", Year = 1977, Folders = new[] { "atari2600", "a2600" },
            Extensions = new[] { ".a26", ".bin", ".zip", ".7z" }, Players = new[] { "stella", "ra:stella" }, Thumbs = "Atari - 2600", Color1 = "#B5651D", Color2 = "#2A1607" },
        new() { Id = "atari7800", Name = "Atari 7800", Short = "7800", Maker = "Atari", Year = 1986, Folders = new[] { "atari7800", "a7800" },
            Extensions = new[] { ".a78", ".bin", ".zip", ".7z" }, Players = new[] { "ra:prosystem" }, Thumbs = "Atari - 7800", Color1 = "#C62828", Color2 = "#260707" },
        new() { Id = "atarilynx", Name = "Atari Lynx", Short = "Lynx", Maker = "Atari", Year = 1989, Folders = new[] { "atarilynx", "lynx" },
            Extensions = new[] { ".lnx", ".zip", ".7z" }, Players = new[] { "ra:handy" }, Thumbs = "Atari - Lynx", Color1 = "#E65100", Color2 = "#2B1000",
            Bios = new[] { "lynxboot.img" }, BiosRequired = true },
        new() { Id = "jaguar", Name = "Atari Jaguar", Short = "Jaguar", Maker = "Atari", Year = 1993, Folders = new[] { "atarijaguar", "jaguar" },
            Extensions = new[] { ".j64", ".jag", ".zip", ".7z" }, Players = new[] { "ra:virtualjaguar" }, Thumbs = "Atari - Jaguar", Color1 = "#B71C1C", Color2 = "#1C0505" },
        new() { Id = "ngp", Name = "Neo Geo Pocket Color", Short = "NGPC", Maker = "SNK", Year = 1999, Folders = new[] { "ngpc", "ngp" },
            Extensions = new[] { ".ngp", ".ngc", ".zip", ".7z" }, Players = new[] { "ra:mednafen_ngp" }, Thumbs = "SNK - Neo Geo Pocket Color", Color1 = "#00838F", Color2 = "#002226" },
        new() { Id = "wonderswan", Name = "WonderSwan Color", Short = "WSC", Maker = "Bandai", Year = 1999, Folders = new[] { "wonderswancolor", "wonderswan", "wsc", "ws" },
            Extensions = new[] { ".ws", ".wsc", ".zip", ".7z" }, Players = new[] { "ra:mednafen_wswan", "ares" }, Thumbs = "Bandai - WonderSwan Color", Color1 = "#5E35B1", Color2 = "#170D2E" },
        new() { Id = "3do", Name = "3DO", Short = "3DO", Maker = "Panasonic", Year = 1993, Folders = new[] { "3do" },
            Extensions = new[] { ".iso", ".chd", ".cue" }, Players = new[] { "ra:opera" }, Thumbs = "The 3DO Company - 3DO", Color1 = "#455A64", Color2 = "#101619",
            Bios = new[] { "panafz10.bin|panafz1.bin|goldstar.bin|panafz10-norsa.bin" }, BiosRequired = true, Disc = true },
        new() { Id = "colecovision", Name = "ColecoVision", Short = "Coleco", Maker = "Coleco", Year = 1982, Folders = new[] { "colecovision", "coleco" },
            Extensions = new[] { ".col", ".zip", ".7z" }, Players = new[] { "ra:gearcoleco" }, Thumbs = "Coleco - ColecoVision", Color1 = "#37474F", Color2 = "#0E1316",
            Bios = new[] { "colecovision.rom" }, BiosRequired = true },
        new() { Id = "msx", Name = "MSX", Short = "MSX", Maker = "Microsoft / ASCII", Year = 1983, Folders = new[] { "msx", "msx2" },
            Extensions = new[] { ".rom", ".mx1", ".mx2", ".dsk", ".cas", ".zip", ".7z" }, Players = new[] { "ra:bluemsx" }, Thumbs = "Microsoft - MSX", Color1 = "#1565C0", Color2 = "#061A33" },
        new() { Id = "dos", Name = "MS-DOS", Short = "DOS", Maker = "PC", Year = 1981, Folders = new[] { "dos", "pc" },
            Extensions = new[] { ".zip", ".dosz", ".exe", ".com", ".bat", ".conf" }, Players = new[] { "dosbox", "ra:dosbox_pure" }, Thumbs = "DOS", Color1 = "#2E7D32", Color2 = "#08200A" },
        new() { Id = "xbox", Name = "Xbox", Short = "Xbox", Maker = "Microsoft", Year = 2001, Folders = new[] { "xbox" },
            Extensions = new[] { ".iso", ".xiso" }, Players = new[] { "xemu" }, Thumbs = "Microsoft - Xbox", Color1 = "#107C10", Color2 = "#032A03", Disc = true,
            BiosRequired = true, Bios = new[] { "mcpx_1.0.bin", "Complex_4627.bin|Complex_4627v1.03.bin|Complex.bin" },
            BiosNote = "xemu also needs a hard disk image; it asks for these files the first time it starts." },
        new() { Id = "xbox360", Name = "Xbox 360", Short = "360", Maker = "Microsoft", Year = 2005, Folders = new[] { "xbox360", "x360" },
            Extensions = new[] { ".iso", ".xex", ".zar" }, Players = new[] { "xenia" }, Thumbs = "Microsoft - Xbox 360", Color1 = "#5DC21E", Color2 = "#163306", Disc = true },
    };

    public static RetroSystem? SystemOf(string id) => Systems.FirstOrDefault(s => s.Id == id);

    // ================================================================ emulators

    public static readonly RetroEmulator[] Emulators =
    {
        new() { Id = "retroarch", Name = "RetroArch", Systems = new[] { "nes", "fds", "snes", "n64", "gb", "gbc", "gba", "nds", "gc", "wii", "virtualboy", "psx", "psp", "genesis", "mastersystem", "gamegear", "segacd", "sega32x", "saturn", "dreamcast", "arcade", "neogeo", "pcengine", "pcenginecd", "atari2600", "atari7800", "atarilynx", "jaguar", "ngp", "wonderswan", "3do", "colecovision", "msx", "dos" },
            Blurb = "One app for dozens of consoles, through its cores. Retro Lounge downloads each core the first time a console needs it.",
            License = "GPL-3.0", Site = "https://www.retroarch.com", Source = "retroarch", Repo = "libretro/RetroArch", Program = "retroarch.exe",
            Args = "-L \"{core}\" \"{rom}\"", FullScreen = "-f", BiosDir = "system" },
        new() { Id = "duckstation", Name = "DuckStation", Systems = new[] { "psx" }, Blurb = "Fast, accurate PlayStation emulator with upscaling, widescreen hacks and achievements.",
            License = "CC BY-NC-ND 4.0", Site = "https://www.duckstation.org", Repo = "stenzek/duckstation", Asset = @"duckstation-windows-x64-release\.zip$", Prerelease = true,
            Program = @"duckstation-qt.*\.exe", Args = "-batch {fs} -- \"{rom}\"", FullScreen = "-fullscreen", Portable = new[] { "portable.txt" }, BiosDir = "bios" },
        new() { Id = "pcsx2", Name = "PCSX2", Systems = new[] { "ps2" }, Blurb = "The PlayStation 2 emulator: high resolutions, widescreen patches and a big compatibility list.",
            License = "GPL-3.0", Site = "https://pcsx2.net", Repo = "PCSX2/pcsx2", Asset = @"windows-x64-qt\.7z$", Program = "pcsx2-qt.exe",
            Args = "-batch {fs} -- \"{rom}\"", FullScreen = "-fullscreen", Portable = new[] { "portable.ini" }, BiosDir = "bios" },
        new() { Id = "ppsspp", Name = "PPSSPP", Systems = new[] { "psp" }, Blurb = "PlayStation Portable games in HD, with save states and texture upscaling.",
            License = "GPL-2.0", Site = "https://www.ppsspp.org", Source = "page", Page = "https://www.ppsspp.org/download/", Asset = @"(?:https://www\.ppsspp\.org)?/files/[0-9_]+/ppsspp_win\.zip",
            Repo = "hrydgard/ppsspp", Program = "PPSSPPWindows64.exe", Args = "{fs} \"{rom}\"", FullScreen = "--fullscreen" },
        new() { Id = "dolphin", Name = "Dolphin", Systems = new[] { "gc", "wii" }, Blurb = "GameCube and Wii in HD, with widescreen, texture packs and real Wii Remotes.",
            License = "GPL-2.0", Site = "https://dolphin-emu.org", Source = "page", Page = "https://dolphin-emu.org/download/", Asset = @"https://dl\.dolphin-emu\.org/releases/[^""'<>\s]+-x64\.7z",
            Program = "Dolphin.exe", Args = "-b {fs} -e \"{rom}\"", FullScreen = "-C Dolphin.Display.Fullscreen=True", Portable = new[] { "portable.txt" },
            Note = "Needs the Microsoft Visual C++ 2022 (x64) runtime, which most PCs already have." },
        new() { Id = "rpcs3", Name = "RPCS3", Systems = new[] { "ps3" }, Blurb = "The PlayStation 3 emulator. Install Sony's firmware once from its File menu.",
            License = "GPL-2.0", Site = "https://rpcs3.net", Repo = "RPCS3/rpcs3-binaries-win", Asset = @"win64.*\.7z$", Program = "rpcs3.exe", Args = "--no-gui \"{rom}\"" },
        new() { Id = "cemu", Name = "Cemu", Systems = new[] { "wiiu" }, Blurb = "Wii U games at high resolutions, with graphic packs and gyro support.",
            License = "MPL-2.0", Site = "https://cemu.info", Repo = "cemu-project/Cemu", Asset = @"windows-x64\.zip$", Prerelease = true, Program = "Cemu.exe",
            Args = "{fs} -g \"{rom}\"", FullScreen = "-f", Portable = new[] { "portable\\" } },
        new() { Id = "xemu", Name = "xemu", Systems = new[] { "xbox" }, Blurb = "The original Xbox, low level and accurate. It asks for your BIOS and hard disk image on first start.",
            License = "GPL-2.0", Site = "https://xemu.app", Repo = "xemu-project/xemu", Asset = @"win.*x86_64.*release\.zip$", Program = "xemu.exe",
            Args = "{fs} -dvd_path \"{rom}\"", FullScreen = "-full-screen" },
        new() { Id = "xenia", Name = "Xenia Canary", Systems = new[] { "xbox360" }, Blurb = "Xbox 360 games, from the actively developed Canary branch.",
            License = "BSD-3-Clause", Site = "https://xenia.jp", Repo = "xenia-canary/xenia-canary-releases", Asset = @"xenia_canary.*windows.*\.zip$|xenia_canary\.zip$", Prerelease = true,
            Program = "xenia_canary.exe", Args = "{fs} \"{rom}\"", FullScreen = "--fullscreen", Portable = new[] { "portable.txt" } },
        new() { Id = "azahar", Name = "Azahar", Systems = new[] { "n3ds" }, Blurb = "The Nintendo 3DS emulator that carries on Citra and Lime3DS.",
            License = "GPL-2.0", Site = "https://azahar-emu.org", Repo = "azahar-emu/azahar", Asset = @"windows-msvc\.zip$", Program = "azahar.exe",
            Args = "{fs} \"{rom}\"", FullScreen = "-f", Portable = new[] { "user\\" } },
        new() { Id = "melonds", Name = "melonDS", Systems = new[] { "nds" }, Blurb = "Nintendo DS with local wireless, DSi mode and upscaling.",
            License = "GPL-3.0", Site = "https://melonds.kuribo64.net", Repo = "melonDS-emu/melonDS", Asset = @"windows.*x86_64\.zip$", Program = "melonDS.exe",
            Args = "{fs} \"{rom}\"", FullScreen = "-f" },
        new() { Id = "mgba", Name = "mGBA", Systems = new[] { "gb", "gbc", "gba" }, Blurb = "Game Boy, Color and Advance: accurate, quick and light.",
            License = "MPL-2.0", Site = "https://mgba.io", Repo = "mgba-emu/mgba", Asset = @"win64\.7z$", Program = "mGBA.exe", Args = "{fs} \"{rom}\"", FullScreen = "-f",
            Portable = new[] { "portable.ini" } },
        new() { Id = "flycast", Name = "Flycast", Systems = new[] { "dreamcast" }, Blurb = "Dreamcast, NAOMI and Atomiswave, with widescreen and online play.",
            License = "GPL-2.0", Site = "https://github.com/flyinghead/flycast", Repo = "flyinghead/flycast", Asset = @"win64.*\.zip$|windows.*\.zip$", Program = "flycast.exe",
            Args = "\"{rom}\"", BiosDir = "data" },
        new() { Id = "vita3k", Name = "Vita3K", Systems = new[] { "psvita" }, Blurb = "Experimental PlayStation Vita emulator. It installs the firmware and your .vpk games for you.",
            License = "GPL-2.0", Site = "https://vita3k.org", Repo = "Vita3K/Vita3K", Asset = @"windows.*\.zip$", Prerelease = true, Program = "Vita3K.exe", Args = "\"{rom}\"" },
        new() { Id = "shadps4", Name = "shadPS4", Systems = new[] { "ps4" }, Blurb = "Early PlayStation 4 emulator: a growing list of games run, from your own dumps.",
            License = "GPL-2.0", Site = "https://shadps4.net", Repo = "shadps4-emu/shadPS4", Asset = @"win64.*sdl.*\.zip$|win64.*\.zip$", Program = "shadPS4.exe", Args = "-g \"{rom}\"" },
        new() { Id = "mesen", Name = "Mesen", Systems = new[] { "nes", "fds", "snes", "gb", "gbc", "pcengine", "mastersystem", "gamegear" }, Blurb = "Very accurate NES, SNES, Game Boy, PC Engine and Master System, with great tools.",
            License = "GPL-3.0", Site = "https://www.mesen.ca", Repo = "SourMesen/Mesen2", Asset = @"windows.*\.zip$", Program = "Mesen.exe", Args = "\"{rom}\" {fs}", FullScreen = "--fullscreen",
            Note = "Needs the .NET 8 Desktop Runtime." },
        new() { Id = "snes9x", Name = "Snes9x", Systems = new[] { "snes" }, Blurb = "The classic Super Nintendo emulator: light, quick and compatible.",
            License = "Snes9x (non-commercial)", Site = "https://www.snes9x.com", Repo = "snes9xgit/snes9x", Asset = @"win32-x64\.zip$", Program = @"snes9x(-x64)?\.exe", Args = "{fs} \"{rom}\"", FullScreen = "-fullscreen" },
        new() { Id = "rmg", Name = "Rosalie's Mupen GUI", Systems = new[] { "n64" }, Blurb = "Nintendo 64 through mupen64plus, with a modern interface and good defaults.",
            License = "GPL-3.0", Site = "https://github.com/Rosalie241/RMG", Repo = "Rosalie241/RMG", Asset = @"portable.*windows.*\.zip$|windows64.*\.zip$", Program = "RMG.exe",
            Args = "{fs} --quit-after-emulation \"{rom}\"", FullScreen = "--fullscreen", Portable = new[] { "portable.txt" } },
        new() { Id = "ares", Name = "ares", Systems = new[] { "nes", "snes", "n64", "gb", "gbc", "genesis", "mastersystem", "gamegear", "segacd", "sega32x", "pcengine", "wonderswan" }, Blurb = "Accuracy-first emulator for many consoles, from the creator of higan's team.",
            License = "ISC", Site = "https://ares-emu.net", Repo = "ares-emulator/ares", Asset = @"windows.*\.zip$", Program = "ares.exe", Args = "{fs} \"{rom}\"", FullScreen = "--fullscreen" },
        new() { Id = "stella", Name = "Stella", Systems = new[] { "atari2600" }, Blurb = "The Atari 2600 emulator, accurate down to the TV effects.",
            License = "GPL-2.0", Site = "https://stella-emu.github.io", Repo = "stella-emu/stella", Asset = @"windows\.zip$", Program = "Stella.exe", Args = "{fs} \"{rom}\"", FullScreen = "-fullscreen 1" },
        new() { Id = "dosbox", Name = "DOSBox Staging", Systems = new[] { "dos" }, Blurb = "MS-DOS games, with modern sound, shaders and sensible defaults.",
            License = "GPL-2.0", Site = "https://www.dosbox-staging.org", Repo = "dosbox-staging/dosbox-staging", Asset = @"windows-x64.*\.zip$", Program = "dosbox.exe",
            Args = "{fs} \"{rom}\"", FullScreen = "--fullscreen" },
    };

    public static RetroEmulator? Emulator(string id) => Emulators.FirstOrDefault(e => e.Id == id);

    /// <summary>A RetroArch core's name for people ("snes9x" → "Snes9x core").</summary>
    public static string CoreName(string core) => core switch
    {
        "mesen" => "Mesen", "nestopia" => "Nestopia", "snes9x" => "Snes9x", "bsnes" => "bsnes", "mupen64plus_next" => "Mupen64Plus-Next",
        "gambatte" => "Gambatte", "mgba" => "mGBA", "melondsds" => "melonDS DS", "dolphin" => "Dolphin", "mednafen_vb" => "Beetle VB",
        "mednafen_psx_hw" => "Beetle PSX HW", "swanstation" => "SwanStation", "ppsspp" => "PPSSPP", "genesis_plus_gx" => "Genesis Plus GX",
        "picodrive" => "PicoDrive", "mednafen_saturn" => "Beetle Saturn", "kronos" => "Kronos", "flycast" => "Flycast", "fbneo" => "FinalBurn Neo",
        "mame" => "MAME", "mednafen_pce_fast" => "Beetle PCE Fast", "mednafen_pce" => "Beetle PCE", "stella" => "Stella", "prosystem" => "ProSystem",
        "handy" => "Handy", "virtualjaguar" => "Virtual Jaguar", "mednafen_ngp" => "Beetle NeoPop", "mednafen_wswan" => "Beetle WonderSwan",
        "opera" => "Opera", "gearcoleco" => "Gearcoleco", "bluemsx" => "blueMSX", "dosbox_pure" => "DOSBox Pure", _ => core
    };

    /// <summary>"RetroArch (Snes9x)" or the emulator's name.</summary>
    public static string PlayerName(string player) =>
        player.StartsWith("ra:") ? $"RetroArch ({CoreName(player[3..])})" : Emulator(player)?.Name ?? player;

    // ================================================================ settings and the player's game data

    private static RetroConfig? _config;
    public static RetroConfig Config => _config ??= LoadConfig();

    private static RetroConfig LoadConfig()
    {
        try { if (File.Exists(ConfigFile)) return JsonSerializer.Deserialize<RetroConfig>(File.ReadAllText(ConfigFile)) ?? new(); }
        catch (Exception) { }
        return new RetroConfig();
    }

    public static void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    public static RetroGameState StateOf(RetroGame g)
    {
        if (!Config.Games.TryGetValue(g.Key, out var state)) { state = new RetroGameState(); Config.Games[g.Key] = state; }
        return state;
    }

    public static RetroGameState? PeekState(RetroGame g) => Config.Games.TryGetValue(g.Key, out var s) ? s : null;

    /// <summary>Where BIOS files go: the chosen folder, or "bios" beside the first ROM folder.</summary>
    public static string BiosFolder =>
        Config.BiosFolder.Length > 0 ? Config.BiosFolder
        : Config.Roots.Count > 0 ? Path.Combine(Path.GetDirectoryName(Config.Roots[0].TrimEnd('\\')) ?? Config.Roots[0], "bios")
        : Path.Combine(Folder, "BIOS");

    // ================================================================ emulators installed

    public static List<RetroEmuInstall> Installed()
    {
        try { return File.Exists(InstalledFile) ? JsonSerializer.Deserialize<List<RetroEmuInstall>>(File.ReadAllText(InstalledFile)) ?? new() : new(); }
        catch (Exception) { return new(); }
    }

    private static void SaveInstalled(List<RetroEmuInstall> list)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(InstalledFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static RetroEmuInstall? InstalledFor(string id) => Installed().FirstOrDefault(i => i.Id == id && File.Exists(i.Program));

    /// <summary>Can this player run now (its emulator is installed; a RetroArch core downloads when needed)?</summary>
    public static bool Ready(string player) => InstalledFor(player.StartsWith("ra:") ? "retroarch" : player) != null;

    /// <summary>What plays this console: the one chosen, else the first installed, else the first recommended.</summary>
    public static string PlayerFor(RetroSystem system, RetroGame? game = null)
    {
        if (game != null && PeekState(game)?.Player is { Length: > 0 } own && Ready(own)) return own;
        if (Config.SystemPlayer.TryGetValue(system.Id, out var chosen) && Ready(chosen)) return chosen;
        return system.Players.FirstOrDefault(Ready) ?? system.Players.FirstOrDefault() ?? "";
    }

    /// <summary>The emulator a console needs, if none of its players is installed yet.</summary>
    public static RetroEmulator? MissingFor(RetroSystem system)
    {
        if (system.Players.Any(Ready)) return null;
        var first = system.Players.FirstOrDefault();
        return first == null ? null : Emulator(first.StartsWith("ra:") ? "retroarch" : first);
    }

    // ---------------------------------------------------------------- the newest version

    public static async Task<RetroRelease?> ReleaseAsync(RetroEmulator emu, CancellationToken ct = default)
    {
        switch (emu.Source)
        {
            case "retroarch":
            {
                // The newest stable from GitHub's tag, downloaded from libretro's own buildbot (GitHub has no Windows build).
                var text = await Http.GetStringAsync($"https://api.github.com/repos/{emu.Repo}/releases/latest", ct);
                string tag = Str(JsonNode.Parse(text)?["tag_name"]).TrimStart('v');
                if (tag.Length == 0) return null;
                return new RetroRelease(tag, $"https://buildbot.libretro.com/stable/{tag}/windows/x86_64/RetroArch.7z", "RetroArch.7z", 0, null,
                    $"https://github.com/{emu.Repo}/releases/tag/v{tag}");
            }
            case "page":
            {
                try
                {
                    var html = await Http.GetStringAsync(emu.Page, ct);
                    var m = Regex.Match(html, emu.Asset, RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        string url = m.Value.StartsWith("/") ? new Uri(new Uri(emu.Page), m.Value).ToString() : m.Value;
                        string file = Path.GetFileName(new Uri(url).AbsolutePath);
                        string version = Regex.Match(url, @"\d+[._]\d+(?:[._]\d+)?[a-z]?").Value.Replace('_', '.');
                        return new RetroRelease(version.Length > 0 ? version : file, url, file, 0, null, emu.Page);
                    }
                }
                catch (HttpRequestException) when (emu.Repo.Length > 0) { }
                // PPSSPP: its GitHub tag gives the version of the download on its site.
                if (emu.Id == "ppsspp")
                {
                    var text = await Http.GetStringAsync($"https://api.github.com/repos/{emu.Repo}/releases/latest", ct);
                    string tag = Str(JsonNode.Parse(text)?["tag_name"]).TrimStart('v');
                    if (tag.Length > 0) return new RetroRelease(tag, $"https://www.ppsspp.org/files/{tag.Replace('.', '_')}/ppsspp_win.zip", "ppsspp_win.zip", 0, null, emu.Page);
                }
                return null;
            }
            default:
            {
                var text = await Http.GetStringAsync($"https://api.github.com/repos/{emu.Repo}/releases?per_page=12", ct);
                if (JsonNode.Parse(text) is not JsonArray releases) return null;
                var usable = releases.OfType<JsonObject>().Where(r => !(r["draft"]?.GetValue<bool>() ?? false)).ToList();
                JsonObject? asset = null, chosen = null;
                foreach (bool wantStable in emu.Prerelease ? new[] { false } : new[] { true, false })
                {
                    foreach (var r in usable)
                    {
                        if (wantStable && (r["prerelease"]?.GetValue<bool>() ?? false)) continue;
                        asset = Asset(r, emu.Asset);
                        if (asset != null) { chosen = r; break; }
                    }
                    if (chosen != null) break;
                }
                if (chosen == null || asset == null) return null;
                string digest = Str(asset["digest"]);
                string version = Str(chosen["tag_name"]);
                if (version is "latest" or "continuous" or "preview" or "nightly")
                    version = DateTime.TryParse(Str(asset["updated_at"]), out var d) ? d.ToString("yyyy-MM-dd") : version;
                return new RetroRelease(version.TrimStart('v'), Str(asset["browser_download_url"]), Str(asset["name"]), asset["size"]?.GetValue<long>() ?? 0,
                    digest.StartsWith("sha256:") ? digest[7..] : null, Str(chosen["html_url"]));
            }
        }
    }

    private static JsonObject? Asset(JsonObject release, string pattern)
    {
        if (release["assets"] is not JsonArray assets) return null;
        var bad = new Regex(@"arm64|aarch64|linux|macos|mac-|osx|darwin|appimage|flatpak|android|\.apk$|\.deb$|\.dmg$|\.tar|source|symbols|pdb|debug|installer|setup", RegexOptions.IgnoreCase);
        var good = new Regex(pattern, RegexOptions.IgnoreCase);
        return assets.OfType<JsonObject>().FirstOrDefault(a => good.IsMatch(Str(a["name"])) && !bad.IsMatch(Str(a["name"])));
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    // ---------------------------------------------------------------- install, locate, uninstall

    public static async Task<RetroEmuInstall> InstallAsync(RetroEmulator emu, RetroRelease release, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        string temp = Path.Combine(Folder, "Downloads");
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, release.FileName);
        await DownloadAsync(release.Url, file, release.Size, progress, ct);
        if (release.Sha256 is { Length: 64 } expected)
        {
            string actual;
            await using (var check = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidOperationException("The download didn't match GitHub's checksum, so it wasn't installed. Try again.");
            }
        }
        string folder = Path.Combine(EmulatorsFolder, emu.Id);
        Directory.CreateDirectory(folder);
        await UnpackAsync(file, folder, ct);
        try { File.Delete(file); } catch (Exception) { }

        // Its settings and saves stay in its own folder.
        foreach (var marker in emu.Portable)
        {
            string path = Path.Combine(folder, marker.TrimEnd('\\'));
            if (marker.EndsWith('\\')) Directory.CreateDirectory(path);
            else if (!File.Exists(path)) File.WriteAllText(path, "");
        }
        string program = FindProgram(folder, emu.Program)
                         ?? throw new InvalidOperationException($"{emu.Name} was unpacked, but its program couldn't be found.");
        var record = new RetroEmuInstall { Id = emu.Id, Version = release.Version, Folder = folder, Program = program, InstalledAt = DateTime.Now };
        var list = Installed();
        list.RemoveAll(i => i.Id == emu.Id);
        list.Add(record);
        SaveInstalled(list);
        return record;
    }

    private static async Task DownloadAsync(string url, string file, long size, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? size;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(file);
        var buffer = new byte[1 << 16];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }
    }

    /// <summary>Unpacks a .zip (built in) or .7z (with 7-Zip: yours, or 7-Zip's own small 7zr.exe) into a folder, without a lone top folder.</summary>
    private static async Task UnpackAsync(string file, string folder, CancellationToken ct)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext == ".exe") { File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true); return; }
        string staging = Path.Combine(Folder, "Unpack-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            if (ext == ".zip") await Task.Run(() => ExtractZip(file, staging), ct);
            else
            {
                string sevenZip = await SevenZipAsync(ct);
                var start = new ProcessStartInfo(sevenZip, $"x \"{file}\" -o\"{staging}\" -y") { CreateNoWindow = true, UseShellExecute = false };
                using var p = Process.Start(start) ?? throw new InvalidOperationException("7-Zip didn't start.");
                await p.WaitForExitAsync(ct);
                if (p.ExitCode > 1) throw new InvalidOperationException("The download couldn't be unpacked (7-Zip said " + p.ExitCode + ").");
            }
            // One folder at the top: its contents go straight into the emulator's folder.
            string source = staging;
            var dirs = Directory.GetDirectories(staging);
            if (dirs.Length == 1 && Directory.GetFiles(staging).Length == 0) source = dirs[0];
            await Task.Run(() => CopyOver(source, folder), ct);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch (Exception) { }
        }
    }

    private static void ExtractZip(string file, string folder)
    {
        using var zip = ZipFile.OpenRead(file);
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // nothing may land outside the folder
            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>Copies a folder over another: an update replaces the program's files and keeps settings and saves.</summary>
    private static void CopyOver(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: true);
        }
    }

    private static async Task<string> SevenZipAsync(CancellationToken ct)
    {
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            string path = Path.Combine(dir, "7-Zip", "7z.exe");
            if (dir.Length > 0 && File.Exists(path)) return path;
        }
        string own = Path.Combine(Folder, "Tools", "7zr.exe");
        if (!File.Exists(own))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(own)!);
            await DownloadAsync("https://www.7-zip.org/a/7zr.exe", own + ".part", 0, null, ct);
            File.Move(own + ".part", own, overwrite: true);
        }
        return own;
    }

    /// <summary>The emulator's program: its name (or pattern), else the biggest .exe that isn't a helper.</summary>
    public static string? FindProgram(string folder, string pattern)
    {
        if (!Directory.Exists(folder)) return null;
        var exes = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar)).ToList();
        var rx = new Regex("^" + (pattern.Contains('*') || pattern.Contains('(') || pattern.Contains('?') ? pattern : Regex.Escape(pattern)) + "$", RegexOptions.IgnoreCase);
        return exes.FirstOrDefault(p => rx.IsMatch(Path.GetFileName(p)))
               ?? exes.Where(p => !Regex.IsMatch(Path.GetFileName(p), @"unins|crash|redist|vc_|setup|update|helper|7z", RegexOptions.IgnoreCase))
                      .OrderByDescending(p => new FileInfo(p).Length).FirstOrDefault();
    }

    /// <summary>Remembers an emulator the player installed themselves.</summary>
    public static RetroEmuInstall Locate(RetroEmulator emu, string program)
    {
        var record = new RetroEmuInstall { Id = emu.Id, Folder = Path.GetDirectoryName(program)!, Program = program, InstalledAt = DateTime.Now, Located = true };
        var list = Installed();
        list.RemoveAll(i => i.Id == emu.Id);
        list.Add(record);
        SaveInstalled(list);
        return record;
    }

    /// <summary>Removes an emulator: Retro Lounge's own go to the Recycle Bin (with their saves: say so first); a located one is only forgotten.</summary>
    public static void Uninstall(string id)
    {
        var list = Installed();
        var record = list.FirstOrDefault(i => i.Id == id);
        if (record == null) return;
        if (!record.Located && Directory.Exists(record.Folder)) FileOps.Recycle(new[] { record.Folder });
        list.Remove(record);
        SaveInstalled(list);
    }

    /// <summary>Opens the emulator on its own (to set it up: controls, graphics, firmware).</summary>
    public static void OpenEmulator(RetroEmuInstall install) =>
        Process.Start(new ProcessStartInfo(install.Program) { WorkingDirectory = install.Folder, UseShellExecute = true });

    // ---------------------------------------------------------------- RetroArch cores

    public static string CoreFile(RetroEmuInstall ra, string core) => Path.Combine(ra.Folder, "cores", core + "_libretro.dll");

    /// <summary>Downloads a RetroArch core from libretro's buildbot if it isn't there yet.</summary>
    public static async Task EnsureCoreAsync(RetroEmuInstall ra, string core, CancellationToken ct = default)
    {
        string dll = CoreFile(ra, core);
        if (File.Exists(dll)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        string zip = dll + ".zip";
        await DownloadAsync($"https://buildbot.libretro.com/nightly/windows/x86_64/latest/{core}_libretro.dll.zip", zip, 0, null, ct);
        await Task.Run(() => ExtractZip(zip, Path.GetDirectoryName(dll)!), ct);
        try { File.Delete(zip); } catch (Exception) { }
        if (!File.Exists(dll)) throw new InvalidOperationException("The " + CoreName(core) + " core couldn't be downloaded.");
    }

    // ================================================================ games

    private static List<RetroGame>? _games;
    public static List<RetroGame> Games => _games ??= LoadLibrary();
    public static DateTime LastScan { get; private set; }

    private static List<RetroGame> LoadLibrary()
    {
        try { if (File.Exists(LibraryFile)) return JsonSerializer.Deserialize<List<RetroGame>>(File.ReadAllText(LibraryFile)) ?? new(); }
        catch (Exception) { }
        return new();
    }

    /// <summary>Which console a folder name belongs to.</summary>
    public static RetroSystem? SystemForFolder(string name)
    {
        string n = name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        return Systems.FirstOrDefault(s => s.Folders.Any(f => f.Replace("-", "") == n));
    }

    private static readonly Regex DiscTag = new(@"\s*[\(\[]\s*(?:disc|disk|cd)\s*(\d+)(?:\s*of\s*\d+)?\s*[\)\]]", RegexOptions.IgnoreCase);
    private static readonly string[] RegionWords = { "USA", "Europe", "Japan", "World", "Asia", "Korea", "Brazil", "Australia", "France", "Germany", "Spain", "Italy", "China", "Canada", "Netherlands", "Sweden" };

    /// <summary>Looks through the ROM folders (a worker thread). Reports which console it's on and how many games so far.</summary>
    public static async Task<List<RetroGame>> ScanAsync(IProgress<(string System, int Count)>? progress = null, CancellationToken ct = default)
    {
        var roots = Config.Roots.Where(Directory.Exists).ToList();
        var known = Games.ToDictionary(g => g.Key, g => g.Added);
        var found = await Task.Run(() =>
        {
            var list = new List<RetroGame>();
            foreach (var root in roots)
            {
                foreach (var dir in SafeDirs(root))
                {
                    ct.ThrowIfCancellationRequested();
                    var system = SystemForFolder(Path.GetFileName(dir));
                    if (system == null) continue;
                    progress?.Report((system.Short, list.Count));
                    list.AddRange(ScanSystem(system, dir, known));
                }
                // A console's folder chosen directly as a ROM folder.
                if (SystemForFolder(Path.GetFileName(root.TrimEnd('\\'))) is { } direct) list.AddRange(ScanSystem(direct, root, known));
            }
            return list.GroupBy(g => g.Key).Select(x => x.First()).ToList();
        }, ct);
        _games = found;
        LastScan = DateTime.Now;
        try
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(LibraryFile, JsonSerializer.Serialize(found), ct);
        }
        catch (Exception) { }
        return found;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); } catch (Exception) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir, int depth)
    {
        var all = new List<string>();
        try
        {
            all.AddRange(Directory.GetFiles(dir));
            if (depth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if (name is "media" or "images" or "covers" or "videos" or "manuals" or "bios" or "saves" or "states" || name.StartsWith('.')) continue;
                    all.AddRange(SafeFiles(sub, depth - 1));
                }
        }
        catch (Exception) { }
        return all;
    }

    private static List<RetroGame> ScanSystem(RetroSystem system, string dir, Dictionary<string, DateTime> known)
    {
        var games = new List<RetroGame>();
        DateTime When(string path) => known.TryGetValue(path.ToLowerInvariant(), out var d) ? d : DateTime.Now;

        // Games that are folders (PS3, PS4): each folder holding the game's own file.
        if (system.FolderGameFile != null)
        {
            foreach (var sub in SafeDirs(dir))
                if (File.Exists(Path.Combine(sub, system.FolderGameFile)))
                    games.Add(Make(system, sub, Path.GetFileName(sub), When(sub), 0));
        }
        var files = SafeFiles(dir, 2).Where(f => system.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

        // Track files a .cue / .gdi / .m3u already points to aren't games of their own.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in files.Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string folder = Path.GetDirectoryName(sheet)!;
                foreach (var line in File.ReadLines(sheet).Take(400))
                {
                    string? name = null;
                    var m = Regex.Match(line, "FILE\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (m.Success) name = m.Groups[1].Value;
                    else if (sheet.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) && line.Trim().Length > 0 && !line.StartsWith('#')) name = line.Trim();
                    else if (sheet.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase)) { var g = Regex.Match(line, @"^\s*\d+\s+\d+\s+\d+\s+\d+\s+(""[^""]+""|\S+)"); if (g.Success) name = g.Groups[1].Value.Trim('"'); }
                    if (name != null) referenced.Add(Path.GetFullPath(Path.Combine(folder, name)));
                }
            }
            catch (Exception) { }
        }
        files = files.Where(f => !referenced.Contains(Path.GetFullPath(f))).ToList();

        // Discs of one game become one entry (unless an .m3u already lists them).
        foreach (var group in files.GroupBy(f => (Path.GetDirectoryName(f)!.ToLowerInvariant(), DiscTag.Replace(Path.GetFileNameWithoutExtension(f), "").ToLowerInvariant())))
        {
            var discs = group.OrderBy(f => DiscTag.Match(Path.GetFileNameWithoutExtension(f)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0).ToList();
            string first = discs[0];
            long size = discs.Sum(f => { try { return new FileInfo(f).Length; } catch (Exception) { return 0L; } });
            var game = Make(system, first, Path.GetFileNameWithoutExtension(first), When(first), size);
            if (discs.Count > 1 && system.Disc) game.Discs = discs;
            else if (discs.Count > 1) { games.AddRange(discs.Skip(1).Select(d => Make(system, d, Path.GetFileNameWithoutExtension(d), When(d), 0))); }
            games.Add(game);
        }
        return games;
    }

    private static RetroGame Make(RetroSystem system, string path, string name, DateTime added, long size)
    {
        var tags = Regex.Matches(name, @"[\(\[]([^\)\]]+)[\)\]]").Select(m => m.Groups[1].Value.Trim()).ToList();
        string title = Regex.Replace(name, @"\s*[\(\[].*$", "").Trim();
        // "Legend of Zelda, The - A Link to the Past" → "The Legend of Zelda - A Link to the Past"
        var the = Regex.Match(title, @"^(.*?),\s*(The|A|An)(\s+-\s+.*|:.*)?$", RegexOptions.IgnoreCase);
        if (the.Success) title = the.Groups[2].Value + " " + the.Groups[1].Value + the.Groups[3].Value;
        if (title.Length == 0) title = name;
        string region = tags.SelectMany(t => t.Split(',', StringSplitOptions.TrimEntries)).FirstOrDefault(t => RegionWords.Contains(t, StringComparer.OrdinalIgnoreCase)) ?? "";
        return new RetroGame { Path = path, System = system.Id, Name = name, Title = title, Tags = tags, Region = region, Size = size, Added = added };
    }

    /// <summary>
    /// The games to show: hidden ones out, and with "one per game" the best copy of each title (your region, then
    /// World, then any; release versions before betas, demos and hacks; the newest revision).
    /// </summary>
    public static List<RetroGame> Visible(IEnumerable<RetroGame>? games = null)
    {
        var list = (games ?? Games).Where(g => PeekState(g)?.Hidden != true).ToList();
        if (!Config.OnePerGame) return list;
        int Score(RetroGame g)
        {
            int s = 0;
            if (g.Region.Equals(Config.Region, StringComparison.OrdinalIgnoreCase)) s += 100;
            else if (g.Region == "World") s += 80;
            else if (g.Region is "USA" or "Europe") s += 40;
            if (g.Tags.Any(t => Regex.IsMatch(t, @"beta|proto|demo|sample|hack|pirate|unl|kiosk", RegexOptions.IgnoreCase))) s -= 200;
            var rev = g.Tags.Select(t => Regex.Match(t, @"^Rev\s*(\d+)", RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success);
            if (rev != null) s += int.Parse(rev.Groups[1].Value);
            if (PeekState(g) is { } st && (st.Favorite || st.LastPlayed != null)) s += 1000; // the copy you actually play stays
            return s;
        }
        return list.GroupBy(g => g.System + "|" + g.Title.ToLowerInvariant()).Select(x => x.OrderByDescending(Score).First()).ToList();
    }

    /// <summary>Makes a ROM folder with one folder per console (and a BIOS folder beside it), as EmuDeck does.</summary>
    public static string CreateFolders(string parent)
    {
        string roms = Path.Combine(parent, "roms");
        foreach (var s in Systems) Directory.CreateDirectory(Path.Combine(roms, s.Folders[0]));
        Directory.CreateDirectory(Path.Combine(parent, "bios"));
        if (!Config.Roots.Contains(roms, StringComparer.OrdinalIgnoreCase)) Config.Roots.Add(roms);
        Config.BiosFolder = Path.Combine(parent, "bios");
        SaveConfig();
        return roms;
    }

    /// <summary>ROM folders other frontends made (EmuDeck, RetroBat, ES-DE, LaunchBox): found on each drive, suggested once.</summary>
    public static List<string> FindExistingRomFolders()
    {
        var found = new List<string>();
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string> { Path.Combine(user, "ROMs"), Path.Combine(user, "Emulation", "roms"), Path.Combine(user, "Games", "ROMs") };
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
            {
                string r = drive.RootDirectory.FullName;
                candidates.AddRange(new[] { Path.Combine(r, "Emulation", "roms"), Path.Combine(r, "RetroBat", "roms"), Path.Combine(r, "ROMs"), Path.Combine(r, "Roms"), Path.Combine(r, "Games", "ROMs"), Path.Combine(r, "LaunchBox", "Games") });
            }
        }
        catch (Exception) { }
        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (Directory.Exists(c) && SafeDirs(c).Any(d => SystemForFolder(Path.GetFileName(d)) != null) && !Config.Roots.Contains(c, StringComparer.OrdinalIgnoreCase))
                found.Add(c);
        return found;
    }

    // ================================================================ BIOS

    public sealed record BiosCheck(string Label, bool Found, string? File);

    /// <summary>Which of a console's BIOS files are in the BIOS folder.</summary>
    public static List<BiosCheck> CheckBios(RetroSystem system)
    {
        var result = new List<BiosCheck>();
        string folder = BiosFolder;
        foreach (var entry in system.Bios)
        {
            if (entry == "ps2:*")
            {
                var ps2 = Ps2Bios(folder);
                result.Add(new BiosCheck("A PS2 BIOS (.bin, 4 MB)", ps2 != null, ps2));
                continue;
            }
            var names = entry.Split('|');
            string? hit = names.Select(n => FindFile(folder, n)).FirstOrDefault(p => p != null);
            result.Add(new BiosCheck(names.Length > 1 ? string.Join(" or ", names.Take(3)) + (names.Length > 3 ? " …" : "") : names[0], hit != null, hit));
        }
        return result;
    }

    public static bool BiosOk(RetroSystem system) => !system.BiosRequired || system.Bios.Length == 0 || CheckBios(system).All(b => b.Found);

    private static string? FindFile(string folder, string name)
    {
        if (!Directory.Exists(folder)) return null;
        string direct = Path.Combine(folder, name);
        if (File.Exists(direct)) return direct;
        try { return Directory.EnumerateFiles(folder, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault(); }
        catch (Exception) { return null; }
    }

    private static string? Ps2Bios(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        try
        {
            return Directory.EnumerateFiles(folder, "*.bin", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 })
                .FirstOrDefault(f => new FileInfo(f).Length == 4 * 1024 * 1024 && !Path.GetFileName(f).StartsWith("scph5", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return null; }
    }

    /// <summary>Copies the console's BIOS files into the emulator's own BIOS folder (when it has one and they aren't there yet).</summary>
    private static void ShareBios(RetroSystem system, RetroEmulator emu, RetroEmuInstall install)
    {
        if (emu.BiosDir.Length == 0 || install.Located) return;
        foreach (var check in CheckBios(system).Where(b => b.Found && b.File != null))
        {
            try
            {
                string sub = emu.Id == "retroarch" && system.Id == "dreamcast" ? "dc" : emu.Id == "flycast" ? "" : "";
                string target = Path.Combine(install.Folder, emu.BiosDir, sub, Path.GetFileName(check.File!));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(check.File!, target);
            }
            catch (Exception) { }
        }
    }

    // ================================================================ playing

    /// <summary>The program and command line that play a game (downloading a RetroArch core first if needed).</summary>
    public static async Task<(string Program, string Arguments, string Folder)> CommandAsync(RetroGame game, string? player = null, CancellationToken ct = default)
    {
        var system = SystemOf(game.System) ?? throw new InvalidOperationException("Unknown console.");
        player ??= PlayerFor(system, game);
        if (player.Length == 0) throw new InvalidOperationException("Nothing plays " + system.Name + " yet.");
        string emuId = player.StartsWith("ra:") ? "retroarch" : player;
        var emu = Emulator(emuId) ?? throw new InvalidOperationException("Unknown emulator.");
        var install = InstalledFor(emuId) ?? throw new InvalidOperationException(emu.Name + " isn't installed. Get it in Retro Lounge > Emulators.");
        string rom = RomPath(game, system);
        string core = "";
        if (player.StartsWith("ra:"))
        {
            await EnsureCoreAsync(install, player[3..], ct);
            core = CoreFile(install, player[3..]);
            RetroExtras.EnsureRetroArchHotkeys(install); // the menu and quit combos the game page describes
        }
        ShareBios(system, emu, install);
        string args = emu.Args.Replace("{rom}", rom).Replace("{core}", core).Replace("{fs}", Config.FullScreen ? emu.FullScreen : "");
        if (Config.FullScreen && emu.FullScreen.Length > 0 && !emu.Args.Contains("{fs}")) args = emu.FullScreen + " " + args;
        // Experimental: the console's shader and bezel, and a save state to start from (RetroArch only).
        if (emuId == "retroarch" && RetroEight.ExtraArgs(game, install) is { Length: > 0 } extra) args = extra + " " + args;
        else RetroEight.PendingSlot = null;
        return (install.Program, Regex.Replace(args, @"\s{2,}", " ").Trim(), install.Folder);
    }

    /// <summary>The file to hand the emulator: a folder game's own file, or a playlist of the discs (made in Retro Lounge's folder).</summary>
    private static string RomPath(RetroGame game, RetroSystem system)
    {
        if (Directory.Exists(game.Path))
        {
            if (system.Id == "ps3") return Path.Combine(game.Path, system.FolderGameFile!);
            if (system.FolderGameFile != null) return Path.Combine(game.Path, system.FolderGameFile);
            return game.Path;
        }
        if (game.Discs.Count > 1)
        {
            string list = Path.Combine(Folder, "Playlists", Hash(game.Key) + ".m3u");
            Directory.CreateDirectory(Path.GetDirectoryName(list)!);
            File.WriteAllLines(list, game.Discs);
            return list;
        }
        return game.Path;
    }

    public static event Action? Played;

    /// <summary>Starts a game and keeps count of when and how long it was played.</summary>
    public static async Task PlayAsync(RetroGame game, string? player = null)
    {
        var (program, arguments, folder) = await CommandAsync(game, player);
        var process = Process.Start(new ProcessStartInfo(program, arguments) { WorkingDirectory = folder, UseShellExecute = false });
        RetroEight.Running = process != null ? game : null;
        var state = StateOf(game);
        state.LastPlayed = DateTime.Now;
        state.Plays++;
        SaveConfig();
        Played?.Invoke();
        if (process == null) return;
        var started = DateTime.Now;
        _ = Task.Run(async () =>
        {
            try { await process.WaitForExitAsync(); } catch (Exception) { }
            if (ReferenceEquals(RetroEight.Running, game)) RetroEight.Running = null;
            double minutes = (DateTime.Now - started).TotalMinutes;
            if (minutes < 0.2) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { StateOf(game).Minutes += minutes; SaveConfig(); Played?.Invoke(); });
        });
    }

    public static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    // ================================================================ box art

    private static readonly SemaphoreSlim ArtGate = new(6);
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> ArtMemory = new();

    /// <summary>libretro-thumbnails' file name rule: these characters become underscores.</summary>
    private static string ThumbName(string name) => Regex.Replace(name, @"[&\*/:`<>\?\\\|""]", "_");

    /// <summary>Names to try on libretro-thumbnails: the file's own, then without disc and extra tags, then with a region.</summary>
    private static IEnumerable<string> ArtNames(RetroGame g)
    {
        yield return g.Name;
        string noDisc = DiscTag.Replace(g.Name, "").Trim();
        if (noDisc != g.Name) yield return noDisc;
        string baseName = Regex.Replace(g.Name, @"\s*[\(\[].*$", "").Trim();
        if (g.Region.Length > 0) yield return $"{baseName} ({g.Region})";
        yield return baseName + " (USA)";
    }

    /// <summary>
    /// A game's picture: the player's own first (next to the game, in a media or covers folder, or ES-DE's downloaded
    /// media), then libretro-thumbnails ("Named_Boxarts", or "Named_Snaps" / "Named_Titles"). Kept on disk; misses too.
    /// </summary>
    public static async Task<System.Windows.Media.ImageSource?> ArtAsync(RetroGame g, string kind, int width, CancellationToken ct = default)
    {
        string memKey = kind + "|" + width + "|" + g.Key;
        lock (ArtMemory) if (ArtMemory.TryGetValue(memKey, out var known)) return known;
        System.Windows.Media.ImageSource? image = null;
        await ArtGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (kind == "Named_Boxarts" && LocalArt(g) is { } local) image = ImageLoader.FromBytes(await File.ReadAllBytesAsync(local, ct).ConfigureAwait(false), width);
            // Experimental: pictures downloaded from ScreenScraper.
            if (image == null && kind == "Named_Boxarts" && RetroEight.Cover(g) is { } scraped) image = ImageLoader.FromBytes(await File.ReadAllBytesAsync(scraped, ct).ConfigureAwait(false), width);
            if (image == null && kind is "Named_Snaps" or "Named_Titles" && RetroEight.Snap(g) is { } snap) image = ImageLoader.FromBytes(await File.ReadAllBytesAsync(snap, ct).ConfigureAwait(false), width);
            var system = SystemOf(g.System);
            if (image == null && Config.OnlineArt && system != null && system.Thumbs.Length > 0)
            {
                Directory.CreateDirectory(ArtFolder);
                foreach (var name in ArtNames(g).Distinct())
                {
                    string url = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(system.Thumbs)}/{kind}/{Uri.EscapeDataString(ThumbName(name))}.png";
                    string file = Path.Combine(ArtFolder, Hash(url) + ".png");
                    if (File.Exists(file)) { image = ImageLoader.FromBytes(await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false), width); if (image != null) break; }
                    if (File.Exists(file + ".none") && DateTime.Now - File.GetLastWriteTime(file + ".none") < TimeSpan.FromDays(14)) continue;
                    try
                    {
                        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) { await File.WriteAllTextAsync(file + ".none", "", ct).ConfigureAwait(false); continue; }
                        var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        image = ImageLoader.FromBytes(data, width);
                        if (image == null) continue;
                        await File.WriteAllBytesAsync(file, data, ct).ConfigureAwait(false);
                        break;
                    }
                    catch (HttpRequestException) { break; } // offline: try again next time
                }
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { }
        finally { ArtGate.Release(); }
        lock (ArtMemory)
        {
            if (ArtMemory.Count > 600) ArtMemory.Clear();
            ArtMemory[memKey] = image;
        }
        return image;
    }

    public static void ForgetArt() { lock (ArtMemory) ArtMemory.Clear(); }

    /// <summary>Forgets one game's pictures in memory (after new ones were downloaded for it).</summary>
    public static void ForgetArt(RetroGame g) { lock (ArtMemory) foreach (var k in ArtMemory.Keys.Where(k => k.EndsWith("|" + g.Key)).ToList()) ArtMemory.Remove(k); }

    private static string? LocalArt(RetroGame g)
    {
        string? folder = Directory.Exists(g.Path) ? g.Path : Path.GetDirectoryName(g.Path);
        if (folder == null) return null;
        string system = SystemOf(g.System)?.Folders[0] ?? g.System;
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new[]
        {
            folder, Path.Combine(folder, "media", "covers"), Path.Combine(folder, "media", "box2dfront"), Path.Combine(folder, "images"),
            Path.Combine(folder, "covers"), Path.Combine(user, "ES-DE", "downloaded_media", system, "covers"),
        };
        foreach (var place in places)
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string f = Path.Combine(place, g.Name + ext);
                if (File.Exists(f)) return f;
                if (Directory.Exists(g.Path))
                {
                    string inside = Path.Combine(g.Path, "PS3_GAME", "ICON0.PNG");
                    if (File.Exists(inside)) return inside;
                    string ps4 = Path.Combine(g.Path, "sce_sys", "icon0.png");
                    if (File.Exists(ps4)) return ps4;
                }
            }
        return null;
    }

    /// <summary>The picture saved for Home (Add to Home): the box art kept on disk, copied to Retro Lounge's covers.</summary>
    public static async Task<string?> CoverFileAsync(RetroGame g)
    {
        if (LocalArt(g) is { } local) return local;
        await ArtAsync(g, "Named_Boxarts", 400);
        var system = SystemOf(g.System);
        if (system == null) return null;
        foreach (var name in ArtNames(g).Distinct())
        {
            string url = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(system.Thumbs)}/Named_Boxarts/{Uri.EscapeDataString(ThumbName(name))}.png";
            string file = Path.Combine(ArtFolder, Hash(url) + ".png");
            if (File.Exists(file)) return file;
        }
        return null;
    }
}
