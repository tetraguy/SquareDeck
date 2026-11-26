using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SquareDeck.Services;

/// <summary>One experimental feature (Settings > Experimental). Each is off until turned on.</summary>
public sealed record LabFeature(string Id, string Name, string About, string Glyph, string Group);

/// <summary>
/// The experimental features: which are on, their saved choices, and the work behind them that doesn't need the
/// interface. The pages are in LabsScreens.cs; the parts that live on the home screen are in MainWindow.Labs.cs.
/// </summary>
public static partial class Labs
{
    public const string Storage = "Storage and downloads", Tools = "Tools", Stores = "Stores and friends", Looks = "Looks and sound", Play = "Play";
    // 8.0
    public const string WidgetGroup = "Game Bar widget";
    public const string Playing = "While you play", LibraryGroup = "Library and Home", Retro = "Retro Lounge", Social = "Friends and media", SystemGroup = "System and troubleshooting";

    public static readonly LabFeature[] Features =
    {
        new("move", "Move games between drives", "Moves a Steam game to another library folder, such as a microSD card. In a game's options", "\uE8DE", Storage),
        new("storage", "Storage advisor", "Finds games you haven't played in six months and how much space they use", "\uEDA2", Storage),
        new("installestimate", "Install estimate", "Before installing: the download size, about how long it takes, and the space left on each drive", "\uE896", Storage),
        new("cleanup", "Library cleanup", "Broken shortcuts, missing programs and leftover folders of uninstalled games", "\uE74D", Storage),
        new("overnight", "Overnight downloads", "Keeps the PC awake while it's charging and downloading, then lets it sleep when done", "\uE708", Storage),
        new("steamfullscreen", "Open Steam in fullscreen", "Steam pages opened from SquareDeck (store, library, friends, downloads) open in Steam's Big Picture mode", "\uE740", Storage),
        new("closelaunchers", "Close store launchers after playing", "When a game ends, its store's launcher closes too (Epic, EA, Ubisoft, GOG, Battle.net, and Steam if you choose), like Playnite", "\uE711", Storage),
        new("autoupdate", "Download updates automatically", "New SquareDeck versions download first, so Install starts straight away", "\uE896", Storage),
        new("bugreport", "One-tap bug report", "Packs the crash log, version, style and device into a GitHub issue, with personal details removed", "\uEBE8", Tools),
        new("refund", "Steam refund reminder", "Warns before a new game passes Steam's 2-hour refund limit", "\uE8C7", Tools),
        new("diary", "Game diary", "Each game's sessions, screenshots and notes as a timeline. In a game's options", "\uE787", Tools),
        new("fixgame", "Fix this game", "Verify files, clear the shader cache, reset settings, reinstall redistributables. In a game's options", "\uE90F", Tools),
        new("sticks", "Stick check and dead zone", "Shows where each stick reads, measures drift, and sets SquareDeck's dead zone", "\uE7FC", Tools),
        new("battery", "Battery health", "Charge cycles, capacity compared with new, and capacity over time", "\uEBA7", Tools),
        new("trailers", "Trailers in SquareDeck", "\u201CWatch the trailer\u201D plays it here in a pop-up window that can be expanded, instead of opening Steam's page", "\uE714", Tools),
        new("watermark", "Watermark screenshots", "Put the game's logo on a screenshot, in a corner and size you choose. In Captures, when viewing a screenshot", "\uE8B9", Tools),
        new("guides", "Achievement guides", "A \u201CHow to get this\u201D search for every locked achievement", "\uE897", Tools),
        new("gamegenie", "Game Genie", "An \u201CAsk Genie\u201D button on a game's page: its key facts and questions and answers about it, written by Google Gemini, and answers to your own questions", "\uE945", Play),
        new("vacuumtube", "DeckTube", "YouTube's TV app, made for the controller, with ad blocking, SponsorBlock and DeArrow from VacuumTube: a DeckTube tab on the Music page, next to Spotify", "\uE714", Stores),
        new("musicstreams", "YouTube Music and Apple Music", "Both play inside SquareDeck from Multimedia > Streaming, made for the controller, and keep playing while you browse or play a game", "\uE93C", Stores),
        new("discord", "Discord", "Your Discord status shows the game you're playing from SquareDeck, and the Friends page gets a Discord button to open Discord or your messages", "\uE8BD", Stores),
        new("widgetmedia", "Media controls in the widget", "When music or video plays from SquareDeck (your music, Spotify, YouTube Music, Apple Music…), the Game Bar widget shows a row with its cover art, play / pause, next and previous", "\uE8D6", Tools),
        new("recomp", "Recomp Zone", "Native PC versions of console games, rebuilt from their code: browse, install, update and play them (from the recomplauncher.com list), on the Stores page", "\uE7FC", Stores),
        new("freegames", "Free games tracker", "Epic, Prime Gaming and Steam giveaways in one place, on the Stores page", "\uE719", Stores),
        new("leaderboard", "Friends leaderboard", "Hours played recently by you and your Steam friends, and the games you share", "\uE716", Stores),
        new("settingsview", "Settings view style", "Choose from five layouts for the Settings page: single column, wide, side drawer, two panes or tiles", "\uE8A9", Looks),
        new("styleschedule", "Style schedule", "A different style for mornings and nights or weekends, or a random Advanced style each day", "\uE823", Looks),
        new("wallpaperengine", "Wallpaper Engine import", "Use a Wallpaper Engine video wallpaper you own as the moving background", "\uE91B", Looks),
        new("coverdim", "Darken covers that aren't highlighted", "Covers other than the highlighted one are dimmed, so it stands out. Brightness and contrast can be set", "\uE706", Looks),
        new("covereffect", "Cover art effect", "Redraws the Home covers in another look: pixel art, toy bricks, comic, pencil sketch, blueprint, neon, VHS, stained glass and more", "\uE7B8", Looks),
        new("genresound", "Sound that follows the genre", "Home music changes with the highlighted game's genre", "\uE8D6", Looks),
        new("tilt", "Covers that tilt", "Tilting the handheld shifts the highlighted cover in 3D", "\uE8B4", Looks),
        new("dust", "Dusty tiles", "Games you haven't played in months gather dust on the home row", "\uE91C", Looks),
        new("companion", "Companion", "A small pixel friend in the corner of Home: six characters, any color, your choice of name, and reactions to what you do", "\uE899", Looks),
        new("achshot", "Screenshot on achievements", "A screenshot when an achievement unlocks, named after it", "\uE722", Play),
        new("milestones", "Milestone celebrations", "Confetti for 100 hours in a game, your 100th achievement, a finished game and more", "\uE7C1", Play),
        new("boss", "Backlog boss fight", "Your unfinished games as a monster to defeat, an hour at a time", "\uE7EF", Play),
        new("unlockables", "Unlockables", "Earn extra highlight colors and companion outfits by playing", "\uE734", Play),
        new("stickers", "Achievement sticker book", "Unlocked achievements as stickers on a corkboard. In a game's options", "\uE8EC", Play),
        new("passport", "Gamer passport", "Stamps for each genre, decade and store you've played", "\uE8F1", Play),
        new("mood", "Mood picker", "Pick a mood and get three games from your library that fit", "\uE76E", Play),

        // ---- 8.0
        new("fpscap", "Frame rate cap per game", "Cap a game at 30, 40, 45 or 60 FPS (set in its game profile), applied when it starts and lifted when it closes. Uses Radeon's own frame limiter", "\uEC4A", Playing),
        new("docked", "Docked / TV mode", "When an external display is connected: bigger text and controls, more power, no mouse pointer, and the sound on the TV. All put back when you undock", "\uE7F4", Playing),
        new("sessiontimer", "Session timer and break reminder", "A gentle reminder after the time you choose, or when the battery gets low, shown over the game", "\uE916", Playing),
        new("launchopts", "Launch options per game", "Command-line options, environment variables and \u201CRun as administrator\u201D, in each game's profile", "\uE756", Playing),
        new("scripts", "Scripts before and after a game", "Run a program or command before a game starts and after it closes (a mod manager, closing an app, mounting a drive). In each game's profile", "\uE943", Playing),
        new("returnanim", "Smooth return after playing", "When a game closes, Home fades back in on the game you launched, instead of jumping", "\uE72B", Playing),
        new("s2game", "Switch 2 style launch, stop and return", "The launch countdown, the Stop game confirmation (and Stopping…), the update-before-playing card and \"is still running\" in your UI style's colors and the Switch 2 manner: a plain card, quiet text, flat buttons with the soft rimmed highlight. And when a game closes, the Switch way back: the game's cover large on the page color, flying down into its tile on Home as the screen fades back", "\uE768", Playing),
        new("charger", "Charger check", "Tells you when the charger can't keep up: plugged in, but the battery is still going down", "\uE83E", Playing),
        new("smartcollections", "Smart collections", "Library shelves that fill themselves: never played, played once and left, short sessions, best on battery and more", "\uE8F1", LibraryGroup),
        new("dailypick", "Daily pick", "Once a day, one game from your library you haven't played in a while, and why it's worth another look", "\uE734", LibraryGroup),
        new("gamepass", "Game Pass shelf", "A Game Pass button in the Library: games just added and leaving soon, from Xbox's own lists", "\uE7FC", LibraryGroup),
        new("aboutgame", "About This Game", "A game's \u201CAbout This Game\u201D as on its Steam store page, with its artwork, moving images and screenshots, from a \u201CLearn more\u201D button in the About section of the game's page", "\uE946", LibraryGroup),
        new("visualmodes", "Visual modes", "ASUS GameVisual screen modes (Racing, Scenery, RTS/RPG, FPS, Cinema, Vivid, Eyecare\u2026) and color temperature, as Armoury Crate offers them, with AMD\u2019s OLED Power Optimization handled for the X20\u2019s OLED screen", "\uE790", SystemGroup),
        new("cpucores", "CPU core configuration", "Choose how many P-cores and E-cores are enabled, as Armoury Crate\u2019s More Settings does: fewer cores can save battery in light games. Takes effect after a restart", "\uE950", SystemGroup),
        new("fancurve", "Custom fan curve", "Draw your own fan curve: the fan speed at each of eight temperatures, shown as a graph, then pick \u201CCustom\u201D anywhere fans are chosen (Settings, the ROG Ally page, game profiles)", "\uE9CA", SystemGroup),
        new("trailerpreview", "Trailer previews on Home", "Rest on a game for a moment and its trailer plays, muted, behind the home row", "\uE714", LibraryGroup),
        new("microsd", "microSD awareness", "Games on each microSD card get their own Library shelf, and stay listed (grayed out) when the card is out", "\uE88E", LibraryGroup),
        new("rascraper", "Box art and screenshots from ScreenScraper", "Fills in missing Retro Lounge covers and screenshots from ScreenScraper's database (free account needed)", "\uEB9F", Retro),
        new("rashaders", "Shaders and bezels per console", "A CRT look for NES and SNES, an LCD grid for Game Boy, borders at the sides: chosen per console, for games played in RetroArch", "\uE790", Retro),
        new("rastates", "Save state browser", "A game's RetroArch save states with their pictures, and \u201CLoad\u201D to start the game straight from one", "\uE74E", Retro),
        new("rasurprise", "Surprise me", "A button on Retro Lounge's Home that picks a random game for you", "\uE8B1", Retro),
        new("friendswidget", "Friends playing on Home", "A small card on Home with the Steam friends who are playing right now", "\uE716", Social),
        new("achtoasts", "Achievement pop-ups", "A pop-up in your style when you unlock a Steam achievement or a RetroAchievement, shown over the game", "\uE734", Social),
        new("twitch", "Twitch", "Twitch on the Multimedia page's Streaming tab, with the other services", "\uE714", Social),
        new("crashrecovery", "Crash recovery", "If SquareDeck closes unexpectedly, it offers to go back to where you were and to send the log", "\uE7BA", SystemGroup),
        new("padtest", "Controller tester", "Every button, trigger and stick live on screen, plus a stick drift check and a rumble test", "\uE7FC", SystemGroup),
        new("updateguard", "Windows Update guard", "Warns when Windows wants to restart, during a game too, and keeps it from restarting in the hours you play", "\uE895", SystemGroup),
        new("drivercheck", "Driver check", "Your graphics, BIOS and ASUS driver versions and how old they are, with links to the official downloads", "\uE964", SystemGroup),
        new("drivenotice", "Drive connected notice", "A notification when a USB drive, external SSD or hard drive, or microSD card is connected: select it to open the drive in SquareDeck's File Explorer", "\uE88E", SystemGroup),
        new("wfpscap", "Widget: frame cap buttons", "Off, 30, 40 and 60 FPS in the widget's Power card, with \u201CSave to this game\u201D", "\uEC4A", WidgetGroup),
        new("wautotdp", "Widget: Auto TDP", "In the Power card: the lowest TDP that holds the frame rate you pick, adjusted every few seconds (needs the frame counter and TDP control)", "\uE945", WidgetGroup),
        new("wthermal", "Widget: thermal warning", "The Live card turns amber with a note when the chip gets close to its temperature limit", "\uE7BA", WidgetGroup),
        new("wnotes", "Widget: game notes", "The running game's notes in the widget, with \u201CAdd note\u201D for codes, hints and where you left off", "\uE70B", WidgetGroup),
        new("wach", "Widget: achievements", "Progress for the running Steam game, the next locked achievements, and \u201CHow to get this\u201D", "\uE734", WidgetGroup),
        new("wgenie", "Widget: Ask Genie", "Ask Game Genie about the game you're playing, without leaving it", "\uE945", WidgetGroup),
        new("wpad", "Widget: controller card", "Controller battery, rumble strength and a stick drift check", "\uE7FC", WidgetGroup),
        new("wfriends", "Widget: friends online", "Your Steam friends who are online or playing, with a button to message them", "\uE716", WidgetGroup),
        new("winbox", "Widget: notifications", "SquareDeck's latest notifications (downloads, drives, achievements…), readable in the widget", "\uEA8F", WidgetGroup),
        new("wdownloads", "Widget: downloads", "What Steam and SquareDeck are downloading, with Steam's downloads page and stopping SquareDeck's queue", "\uE896", WidgetGroup),
        new("wparty", "Widget: invite friends", "An \u201CInvite\u201D button on the game card: Steam's friends list for Steam games, Game Bar's Xbox Social for the rest", "\uE8FA", WidgetGroup),
        new("wapps", "Widget: launch apps", "A row of your apps (Discord, a browser, Spotify…) to open over the game", "\uE71D", WidgetGroup),
        new("wshortcuts", "Widget: SquareDeck shortcuts", "Buttons that open SquareDeck's Home, Library, Retro Lounge, Captures or Settings", "\uE80F", WidgetGroup),
        new("wcards", "Widget: card order and hiding", "An Edit button in the widget's header: move cards up and down and hide the ones you don't use", "\uE8CB", WidgetGroup),
        new("wgameaware", "Widget: layout per game", "Each game remembers the widget's card order, hidden cards and Live tiles from the last time you played it", "\uE8A9", WidgetGroup),
        new("wpresets", "Widget: presets per game", "\u201CSave this setup to the game\u201D in the card editor: card order, hidden cards, Live tiles and Quick tiles", "\uE74E", WidgetGroup),
        new("wtheme", "Widget: match my style", "The widget takes on the current SquareDeck style: its colors, font and wallpaper (Mythical styles too)", "\uE790", WidgetGroup),
        new("wrumble", "Widget: rumble", "The controller buzzes as you move around the widget, like it does in SquareDeck (uses the rumble strength from Settings > Controller)", "\uE7FC", WidgetGroup),
        new("wpowerlook", "Widget: wattage look", "A smaller look for the TDP in the widget's Power card: slim bar, mini dial beside the presets, the number alone (select it for the full dial) or preset chips. Select to choose", "\uE945", WidgetGroup),
        new("newsettings", "Switch 2 style Settings", "Settings drawn like the Nintendo Switch 2\u2019s System Settings: flat rows on thin lines, values in blue, pill switches, sections listed on the left with a blue bar, and the soft blue-to-pink highlight. Light or dark, following your theme", "\uE713", Looks),
        new("newlibrary", "Switch 2 style Library", "The game library laid out like the Switch 2\u2019s System Settings: sections on the left with a blue bar and a storage gauge, games as flat rows with small covers, play time in blue, grouped under Today, This week or A, B, C, and the soft highlight. Follows your UI style", "\uE8F1", Looks),
        new("switchboot", "Switch-style game boot", "Starting a game plays the Switch's own boot: the cover rushes toward you as the screen fades to black, then the game's icon waits in the middle with the ring of dots turning in the corner until the game is up. Plays even with the launch splash off", "\uE7FC", Play),
        new("storeintro", "Store opening animations", "Opening Steam, Xbox, Epic Games, Ubisoft Connect or another store from the Stores page plays a shopping-and-gaming animation in the store's colors (a shopping cart, a shopping bag, an arcade coin slot or a store shelf) and keeps it going until the store is up", "\uE719", Stores),
        new("wfpsslider", "Widget: frame cap slider", "The Game Bar widget\u2019s frame cap becomes an Off button and a slider from 30 to 120 FPS (in steps of 5), and the Save button goes: the cap you set is kept for that game straight away", "\uEC4A", WidgetGroup),
        new("ws2", "Switch 2 style widget", "The Game Bar widget dressed like SquareDeck's Switch 2 style pages, in the colors of the UI style you use: a plain panel in the page color, a thin line under the header, each card titled with a short bar and its name, quiet cards and tiles, and the highlight in the style's color", "\uE8A9", WidgetGroup),
        new("switchbootplus", "Switch boot: logo and loading line", "With the Switch-style game boot, the Hide hint goes, the game\u2019s logo sits in the top-left corner (its name when there\u2019s no logo), and in the bottom-right corner, beside the turning ring, a line keeps changing as it loads: Warming up the pixels\u2026, Spawning the player\u2026, Checking for dragons\u2026 and more", "\uE7FC", Play),
        new("s2setup", "Switch 2 style first-time setup", "The welcome pages (first start, or Settings > Run first-time setup again) laid out like the Switch 2\u2019s System Settings, in your UI style\u2019s colors: System Setup with the gear and a line under it, every step listed down the left with a tick on the ones done and a progress bar, the page on the right with bar-and-name headings and rimmed cards, and a line over Back, Skip and Next", "\uE713", Looks),
        new("s2apps", "Switch 2 style Installed apps", "Settings > Apps > Installed apps, and the page for each app (uninstall, modify, repair, reset), laid out like the Switch 2\u2019s System Settings, in your UI style\u2019s colors: All, desktop and Store apps and the sort order down the left with their counts, the space your apps use with the five biggest as a bar, flat rows with blue sizes, and each app\u2019s page headed by its icon, name, publisher and size", "\uE71D", Looks),
        new("plxcontinue", "Continue where you left off parallax", "On Home, while the Continue where you left off game is there, moving the handheld moves its artwork in depth with the gyro: the art slides and leans one way, the logo and name drift the other, and in the big-artwork Home layout the artwork behind the page moves too. Whatever angle you hold it at becomes the middle after a moment. Select to choose how deep", "\uE8B4", Looks),
        new("s2play", "Switch 2 style Passport, Backlog Boss, Mood picker, Leaderboard and Free games", "My Gamer Passport, the Backlog Boss, How are you feeling?, the Friends leaderboard and Free games rebuilt like the Switch 2 style pages, in your UI style\u2019s colors, with much more in each: a passport page and over 60 stamps in levels; the boss\u2019s health log, your pace and tonight\u2019s target; eleven moods and how long you\u2019ve got; a podium, what friends play, games in common, awards and your place over time; and free games from Epic (with next week\u2019s), Steam, Amazon Prime Gaming, GOG, Ubisoft, EA, Battle.net and itch.io, free loot, and deals from many stores including your wishlist", "\uE8A9", Looks),
        new("s2toast", "Switch 2 style notifications", "The pop-ups in the bottom right corner (achievements, downloads, friends, battery, Windows notifications) drawn like the Switch 2 style pages, in your UI style\u2019s colors: a soft rounded card with a thin line around it, the icon in a rounded square, the heading as a short colored bar and its name, and a slim bar along the bottom that runs down while it\u2019s up. Slides in from the right", "\uEA8F", Looks),
        new("zoneexit", "Recomp Zone and Retro Lounge goodbyes", "Choosing Leave in Recomp Zone or Retro Lounge plays a goodbye in the zone\u2019s spirit. Recomp Zone unlinks: the page breaks into tiles that turn into lines of machine code and stream into a glowing core on the blueprint grid, which collapses as \u201Cexit(0)\u201D types itself out. Retro Lounge switches off like an old CRT TV: the picture squashes to a bright line, then to a dot that fades", "\uE7E8", Playing),
        new("oemfooter", "Your handheld in the corner", "The controller symbol at the bottom left of Home (and of the Switch 2 style pages) becomes a drawing of the handheld SquareDeck detected, the same one the controller tester shows, in your UI style's text color", "\uE7FC", Looks),
        new("s2pages", "Switch 2 style Captures, Multimedia and Stores", "Captures, Multimedia and Stores laid out like the Switch 2\u2019s System Settings, as the new Settings and Library are: sections on the left with a blue bar, thin lines, bar-and-name headings, rows and tiles with the soft highlight. Follows your UI style", "\uE8A9", Looks),
        new("s2tools", "Switch 2 style Recomp Zone, Retro Lounge, Explorer and Task Manager", "Recomp Zone, Retro Lounge, Explorer and Task Manager laid out like the Switch 2\u2019s System Settings, as the new Settings and Library are: the title with a line under it, sections on the left with a blue bar, flat rows and tiles with the soft highlight. Follows your UI style", "\uE7FC", Looks),
        new("s2social", "Switch 2 style News, Friends and Your profile", "News, Friends and Your profile laid out like the Switch 2\u2019s System Settings, as the new Settings and Library are: the title with a line under it, sections down the left with a blue bar, bar-and-name headings, and stories, friends and cards with the soft highlight. Follows your UI style", "\uE789", Looks),
        new("s2system", "Switch 2 style menus, Search, Browser, Notifications and game pages", "The Quick menu, the Power menu, the Wi-Fi and Bluetooth blades, Search, the Browser, the Notifications blade, a game's page and its options menu in the Switch 2 manner of the new Settings and Library: the UI style's colors, flat rows on thin lines, bar-and-name headings and the soft rimmed highlight", "\uE713", Looks),
        new("wcompanion", "Widget: companion", "Your pixel companion in the widget's header, reacting to achievements, low battery and long sessions", "\uE899", WidgetGroup),
        new("startupapps", "Startup apps manager", "See the apps that start with Windows and turn them off, right here", "\uE7E8", SystemGroup),
    };

    private static Settings? _settings;

    // ---- features that have left Experimental (7.2)

    /// <summary>Always on: they're simply part of SquareDeck now, with no switch.</summary>
    public static readonly HashSet<string> AlwaysOn = new()
    {
        "move", "storage", "cleanup", "fixgame", "sticks", "battery", "gamegenie", "unlockables",
        // 8.1.18
        "visualmodes", "cpucores", "fancurve", "padtest", "drivercheck", "startupapps", "twitch", "aboutgame",
        // 8.1.19
        "fpscap", "launchopts", "scripts", "widgetmedia", "wautotdp", "wthermal", "wnotes", "wapps", "wcards", "wgameaware",
        "wpresets", "wpowerlook",
        // 8.1.20
        "wfpscap",
        // 8.1.21: Retro Lounge (all but Surprise me)
        "rascraper", "rashaders", "rastates",
    };

    /// <summary>On for everyone once (when updating to 8.1.19), with a switch in their new place in Settings.</summary>
    public static readonly HashSet<string> DefaultOn819 = new() { "charger", "wtheme", "wcompanion" };

    /// <summary>On for everyone once (when updating to 8.1.24), with a switch in Settings > Display.</summary>
    public static readonly HashSet<string> DefaultOn8124 = new() { "newsettings" };

    /// <summary>On for everyone once (when updating to 8.1.58): every look and sound experiment, the Switch boot and the other launch, store and zone animations, and the widget's Switch 2 style and frame cap slider. Each keeps a switch in its new place in Settings.</summary>
    public static readonly HashSet<string> DefaultOn8158 = new() { "newlibrary", "s2pages", "s2tools", "s2social", "s2system", "s2play", "s2apps", "s2setup", "s2toast", "oemfooter", "switchboot", "switchbootplus", "s2game", "zoneexit", "storeintro", "rasurprise", "ws2", "wfpsslider" };

    /// <summary>On for everyone once (when updating to 8.1.18), with a switch in their new place in Settings.</summary>
    public static readonly HashSet<string> DefaultOn818 = new() { "crashrecovery", "drivenotice", "microsd", "achtoasts" };

    /// <summary>On for everyone once (when updating to 7.2), with a switch in their new place in Settings.</summary>
    public static readonly HashSet<string> DefaultOn = new() { "installestimate", "boss", "mood", "passport", "vacuumtube", "musicstreams", "recomp", "freegames", "leaderboard" };

    /// <summary>Every feature that has moved out of Settings > Experimental to its own place (these, and the two lists above).</summary>
    public static readonly HashSet<string> Graduated = new(AlwaysOn.Concat(DefaultOn).Concat(new[]
    {
        "overnight", "discord", "achshot", "watermark", "guides", "trailers", "steamfullscreen", "refund", "milestones", "diary",
        "closelaunchers", "autoupdate", "settingsview", "styleschedule", "coverdim", "covereffect", "tilt", "dust", "companion", "genresound",
        "wallpaperengine", "bugreport", "stickers",
        // 8.1.18: out of Experimental. Windows Update guard keeps its switch as you had it (it changes a Windows setting).
        "crashrecovery", "drivenotice", "microsd", "achtoasts", "updateguard",
        // 8.1.19. Docked / TV mode and the session timer keep their switches as you had them (they change how things behave).
        "charger", "wtheme", "wcompanion", "docked", "sessiontimer",
        // 8.1.24: the Switch 2 style Settings is the default look.
        "newsettings",
        // 8.1.58: the Switch 2 style everywhere, and the launch, store and zone animations, are the defaults.
        "newlibrary", "s2pages", "s2tools", "s2social", "s2system", "s2play", "s2apps", "s2setup", "s2toast", "oemfooter", "plxcontinue", "switchboot", "switchbootplus", "s2game", "zoneexit", "storeintro", "rasurprise", "ws2", "wfpsslider",
    }));

    public static LabFeature? Feature(string id) => Features.FirstOrDefault(f => f.Id == id);

    /// <summary>Called once at startup with the settings in use.</summary>
    public static void Init(Settings settings)
    {
        _settings = settings;
        if (!settings.LabsGraduated72)
        {
            foreach (var id in DefaultOn) if (!settings.Labs.Contains(id)) settings.Labs.Add(id);
            settings.LabsGraduated72 = true;
            settings.Save();
        }
        if (!settings.LabsGraduated818)
        {
            foreach (var id in DefaultOn818) if (!settings.Labs.Contains(id)) settings.Labs.Add(id);
            settings.LabsGraduated818 = true;
            settings.Save();
        }
        if (!settings.LabsGraduated819)
        {
            foreach (var id in DefaultOn819) if (!settings.Labs.Contains(id)) settings.Labs.Add(id);
            settings.LabsGraduated819 = true;
            settings.Save();
        }
        if (!settings.LabsGraduated8124)
        {
            foreach (var id in DefaultOn8124) if (!settings.Labs.Contains(id)) settings.Labs.Add(id);
            settings.LabsGraduated8124 = true;
            settings.Save();
        }
        if (!settings.LabsGraduated8158)
        {
            foreach (var id in DefaultOn8158) if (!settings.Labs.Contains(id)) settings.Labs.Add(id);
            settings.LabsGraduated8158 = true;
            settings.Save();
        }
        // 8.1.60: the Continue where you left off parallax is off by default (8.1.58 turned it on): off once, then it's yours to turn on.
        if (!settings.ParallaxOff8160)
        {
            settings.Labs.Remove("plxcontinue");
            settings.ParallaxOff8160 = true;
            settings.Save();
        }
    }

    public static bool On(string id) => AlwaysOn.Contains(id) || _settings?.Labs.Contains(id) == true;

    public static void Set(string id, bool on)
    {
        if (_settings == null) return;
        _settings.Labs.Remove(id);
        if (on) _settings.Labs.Add(id);
        _settings.Save();
        Changed?.Invoke(id);
    }

    /// <summary>A feature was turned on or off.</summary>
    public static event Action<string>? Changed;

    public static string Get(string key, string fallback = "") =>
        _settings != null && _settings.LabValues.TryGetValue(key, out var v) ? v : fallback;

    public static void Put(string key, string value, bool save = true)
    {
        if (_settings == null) return;
        _settings.LabValues[key] = value;
        if (save) _settings.Save();
    }

    public static bool Has(string key) => _settings?.LabValues.ContainsKey(key) == true;

    internal static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/" + Updater.Current);
        return http;
    }

    public static string Size(double bytes) =>
        bytes >= 1L << 40 ? $"{bytes / (1L << 40):0.0} TB" : bytes >= 1L << 30 ? $"{bytes / (1L << 30):0.0} GB" :
        bytes >= 1L << 20 ? $"{bytes / (1L << 20):0} MB" : $"{bytes / 1024:0} KB";

    public static string Hours(double hours) =>
        hours < 1 ? $"{Math.Round(hours * 60):0} min" : hours < 10 ? $"{hours:0.#} h" : $"{hours:0} h";

    /// <summary>Total size of a folder, stopping early after a time limit (a huge folder shouldn't freeze anything).</summary>
    public static long FolderSize(string path, TimeSpan? limit = null)
    {
        long total = 0;
        var until = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(20));
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                total += f.Length;
                if (DateTime.UtcNow > until) break;
            }
        }
        catch (Exception) { }
        return total;
    }

    // ================================================================ drives (move, install estimate, storage)

    public sealed record LibraryDrive(string Library, string Drive, string Label, long Free, long Total, bool Removable);

    /// <summary>Each Steam library with its drive's free space; microSD cards and USB drives are marked.</summary>
    public static List<LibraryDrive> Drives()
    {
        var list = new List<LibraryDrive>();
        foreach (var lib in SteamLibrary.LibraryFolders())
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(lib)!);
                if (!d.IsReady) continue;
                string label = d.VolumeLabel.Length > 0 ? d.VolumeLabel : d.DriveType == DriveType.Removable ? "microSD / USB" : "Local disk";
                list.Add(new LibraryDrive(lib, d.Name.TrimEnd('\\'), label, d.AvailableFreeSpace, d.TotalSize, d.DriveType == DriveType.Removable));
            }
            catch (Exception) { }
        }
        return list;
    }

    // ================================================================ move a game

    /// <summary>
    /// Moves an installed Steam game (its folder, manifest and workshop items) to another library. Steam must be
    /// closed, since it holds these files; the caller closes it first. Copies, checks, then removes the original,
    /// so a failure part-way leaves the game where it was.
    /// </summary>
    public static async Task MoveGameAsync(Game game, string targetLibrary, IProgress<double>? progress, CancellationToken ct)
    {
        string manifest = SteamLibrary.ManifestPath(game.AppId) ?? throw new InvalidOperationException("Steam's record of this game wasn't found.");
        string sourceApps = Path.GetDirectoryName(manifest)!;
        string targetApps = Path.Combine(targetLibrary, "steamapps");
        if (string.Equals(Path.GetFullPath(sourceApps).TrimEnd('\\'), Path.GetFullPath(targetApps).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The game is already in that library.");
        string? source = game.InstallDir;
        if (source == null || !Directory.Exists(source)) throw new InvalidOperationException("The game's folder wasn't found.");
        string target = Path.Combine(targetApps, "common", Path.GetFileName(source));
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new InvalidOperationException("A folder with this game's name is already in that library: " + target);

        // Workshop items go along too.
        string workshopSource = Path.Combine(sourceApps, "workshop", "content", game.AppId.ToString());
        string workshopManifest = Path.Combine(sourceApps, "workshop", $"appworkshop_{game.AppId}.acf");

        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.Combine(targetApps, "common"));
            bool sameDrive = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
            if (sameDrive)
            {
                // The same drive: renaming is instant and safe.
                Directory.Move(source, target);
                progress?.Report(0.95);
            }
            else
            {
                var files = new DirectoryInfo(source).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                long total = Math.Max(1, files.Sum(f => f.Length)), done = 0;
                var buffer = new byte[1 << 20];
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    string to = Path.Combine(target, Path.GetRelativePath(source, f.FullName));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    using (var input = f.OpenRead())
                    using (var output = File.Create(to))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            done += read;
                            progress?.Report(0.95 * done / total);
                        }
                    }
                    File.SetLastWriteTime(to, f.LastWriteTime);
                }
                // Check every file arrived whole before removing anything.
                foreach (var f in files)
                {
                    var to = new FileInfo(Path.Combine(target, Path.GetRelativePath(source, f.FullName)));
                    if (!to.Exists || to.Length != f.Length) throw new IOException("A file didn't copy completely: " + f.Name);
                }
            }
            if (Directory.Exists(workshopSource))
            {
                string workshopTarget = Path.Combine(targetApps, "workshop", "content", game.AppId.ToString());
                Directory.CreateDirectory(Path.GetDirectoryName(workshopTarget)!);
                CopyFolder(workshopSource, workshopTarget);
                if (File.Exists(workshopManifest)) File.Copy(workshopManifest, Path.Combine(targetApps, "workshop", Path.GetFileName(workshopManifest)), true);
            }
            File.Copy(manifest, Path.Combine(targetApps, Path.GetFileName(manifest)), true);
            // Everything is in place: now the originals go.
            File.Delete(manifest);
            if (!sameDrive && Directory.Exists(source)) Directory.Delete(source, true);
            if (Directory.Exists(workshopSource)) { try { Directory.Delete(workshopSource, true); } catch (Exception) { } }
            if (File.Exists(workshopManifest)) { try { File.Delete(workshopManifest); } catch (Exception) { } }
            progress?.Report(1);
        }, ct);
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    /// <summary>Asks Steam to close and waits for it (up to 40 seconds). True once it's gone.</summary>
    public static async Task<bool> CloseSteamAsync()
    {
        if (!SteamLibrary.ClientRunning()) return true;
        ProcessUtil.Open("steam://exit");
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(500);
            if (!SteamLibrary.ClientRunning()) { await Task.Delay(1500); return true; }
        }
        return false;
    }

    // ================================================================ install estimate

    /// <summary>The disk space a Steam game says it needs, from its store page's PC requirements. Null if not stated.</summary>
    public static async Task<long?> RequiredSpaceAsync(int appId, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=pc_requirements", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var app) || !app.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("pc_requirements", out var req) || req.ValueKind != JsonValueKind.Object) return null;
            string text = (req.TryGetProperty("recommended", out var r) ? r.GetString() : null) ?? (req.TryGetProperty("minimum", out var m) ? m.GetString() : null) ?? "";
            text = Regex.Replace(text, "<[^>]+>", " ");
            var match = Regex.Match(text, @"(?:Storage|Hard\s*Drive|Hard\s*Disk|Disk\s*Space)\s*:?\s*(\d+(?:[.,]\d+)?)\s*(GB|MB|TB)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            double n = double.Parse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            return (long)(n * match.Groups[2].Value.ToUpperInvariant() switch { "TB" => 1L << 40, "MB" => 1L << 20, _ => 1L << 30 });
        }
        catch (Exception) { return null; }
    }

    /// <summary>Download speed in bytes a second, measured with a short test download (about 10 MB). Null if it failed.</summary>
    public static async Task<double?> MeasureDownloadSpeedAsync(CancellationToken ct)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            using var resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=10000000", HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (watch.Elapsed > TimeSpan.FromSeconds(8)) break;
            }
            return total / Math.Max(0.2, watch.Elapsed.TotalSeconds);
        }
        catch (Exception) { return null; }
    }

    // ================================================================ refund window

    /// <summary>
    /// About when a Steam game was bought: the earliest of when SquareDeck first saw it and when it was installed.
    /// Steam doesn't tell other programs purchase dates, so this is an estimate.
    /// </summary>
    public static DateTime? BoughtAround(Game game)
    {
        DateTime? seen = DateTime.TryParse(Get("seen:" + game.AppId), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var s) ? s : null;
        var installed = SteamLibrary.InstalledAt(game.AppId);
        if (seen == null) return installed;
        if (installed == null) return seen;
        return seen < installed ? seen : installed;
    }

    /// <summary>Notes the first day each Steam game was seen (for the refund window).</summary>
    public static void NoteSeen(IEnumerable<Game> games)
    {
        if (_settings == null) return;
        bool changed = false;
        foreach (var g in games.Where(g => g.IsSteam))
        {
            string key = "seen:" + g.AppId;
            if (_settings.LabValues.ContainsKey(key)) continue;
            // Games already installed long ago when this was turned on use their install date instead.
            var installed = SteamLibrary.InstalledAt(g.AppId) ?? DateTime.Now;
            _settings.LabValues[key] = installed.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            changed = true;
        }
        if (changed) _settings.Save();
    }

    public sealed record RefundStatus(Game Game, double MinutesPlayed, DateTime Bought, double MinutesLeft, TimeSpan TimeLeft);

    /// <summary>Steam games still inside the refund window (under 2 hours, under 14 days), most urgent first.</summary>
    public static List<RefundStatus> RefundWindow(IEnumerable<Game> games, Func<Game, double> extraMinutes)
    {
        var list = new List<RefundStatus>();
        foreach (var g in games.Where(g => g.IsSteam))
        {
            if (BoughtAround(g) is not DateTime bought) continue;
            var left = bought.AddDays(14) - DateTime.Now;
            if (left <= TimeSpan.Zero) continue;
            double minutes = (SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0) + extraMinutes(g);
            if (minutes >= 120) continue;
            list.Add(new RefundStatus(g, minutes, bought, 120 - minutes, left));
        }
        return list.OrderBy(r => r.MinutesLeft).ToList();
    }

    // ================================================================ bug report

    /// <summary>
    /// The text of a bug report: version, Windows, device, style and the last crash, with personal details taken
    /// out (the user name, paths under the user folder, keys, Steam id, e-mail addresses).
    /// </summary>
    public static string BugReport(Settings s, string? note = null)
    {
        var b = new StringBuilder();
        b.AppendLine("**What happened**").AppendLine(string.IsNullOrWhiteSpace(note) ? "(Describe what you were doing and what went wrong.)" : note).AppendLine();
        b.AppendLine("**SquareDeck**: " + Updater.Current);
        b.AppendLine("**Windows**: " + Environment.OSVersion.VersionString);
        b.AppendLine("**Device**: " + (Handheld.IsHandheld ? Handheld.DisplayName : "Not a known handheld"));
        b.AppendLine("**Style**: " + s.UiStyle);
        b.AppendLine("**Screen**: " + System.Windows.SystemParameters.PrimaryScreenWidth + " × " + System.Windows.SystemParameters.PrimaryScreenHeight);
        b.AppendLine("**Experimental features on**: " + (s.Labs.Count == 0 ? "none" : string.Join(", ", s.Labs)));
        b.AppendLine();
        try
        {
            if (File.Exists(CrashLog.Path))
            {
                var log = File.ReadAllText(CrashLog.Path);
                int last = log.LastIndexOf("==== ", StringComparison.Ordinal);
                var entry = last >= 0 ? log[last..] : log;
                if (entry.Length > 3500) entry = entry[..3500] + "\n…";
                b.AppendLine("**Last error**").AppendLine("```").AppendLine(entry.Trim()).AppendLine("```");
            }
            else b.AppendLine("No errors have been logged.");
        }
        catch (Exception) { }
        return Scrub(b.ToString(), s);
    }

    /// <summary>Takes personal details out of text that's about to leave the PC.</summary>
    public static string Scrub(string text, Settings s)
    {
        string user = Environment.UserName;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) text = text.Replace(profile, @"C:\Users\<user>", StringComparison.OrdinalIgnoreCase);
        if (user.Length > 1) text = Regex.Replace(text, Regex.Escape(user), "<user>", RegexOptions.IgnoreCase);
        foreach (var secret in new[] { s.SteamGridDbApiKey, s.SteamWebApiKey })
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length > 6) text = text.Replace(secret, "<key>");
        text = Regex.Replace(text, @"\b7656\d{13}\b", "<steam id>");
        text = Regex.Replace(text, @"[\w.+-]+@[\w-]+\.[\w.-]+", "<e-mail>");
        text = Regex.Replace(text, @"\b[0-9a-fA-F]{32}\b", "<key>");
        return text;
    }

    public static string IssueUrl(string body)
    {
        string title = "Bug: ";
        // Links have a length limit; long reports are trimmed (the full text can be copied instead).
        if (body.Length > 5500) body = body[..5500] + "\n…(trimmed)";
        return "https://github.com/" + Updater.Repo + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
    }

    // ================================================================ library cleanup

    public sealed record Leftover(string Path, string Library, long Size);

    /// <summary>Folders in steamapps\common that no installed game uses (left behind by uninstalls).</summary>
    public static List<Leftover> LeftoverFolders()
    {
        var list = new List<Leftover>();
        foreach (var lib in SteamLibrary.LibraryFolders())
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            var used = SteamLibrary.UsedInstallDirs(lib);
            foreach (var dir in Directory.EnumerateDirectories(common))
            {
                var name = Path.GetFileName(dir);
                if (used.Contains(name) || name.Equals("Steamworks Shared", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new Leftover(dir, lib, FolderSize(dir, TimeSpan.FromSeconds(4))));
            }
        }
        return list.OrderByDescending(l => l.Size).ToList();
    }

    /// <summary>Sends a folder to the Recycle Bin, so it can be brought back.</summary>
    public static void Recycle(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    // ================================================================ fix this game

    /// <summary>Steam's shader cache folder for a game, if there is one.</summary>
    public static string? ShaderCacheOf(Game game)
    {
        if (SteamLibrary.LibraryOf(game.AppId) is not { } lib) return null;
        var dir = Path.Combine(lib, "steamapps", "shadercache", game.AppId.ToString());
        return Directory.Exists(dir) ? dir : null;
    }

    private static string Simple(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Folders that look like the game's settings or saves (Documents\My Games, AppData and so on).</summary>
    public static List<string> SettingsFoldersOf(Game game)
    {
        string name = Simple(game.Name), original = Simple(game.OriginalName);
        if (name.Length < 3) return new();
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
        };
        var found = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var s = Simple(Path.GetFileName(dir));
                    if (s.Length >= 3 && (s == name || s == original)) found.Add(dir);
                    // Publisher folders one level down (AppData\LocalLow\<Studio>\<Game>).
                    else if (root.EndsWith("LocalLow", StringComparison.OrdinalIgnoreCase) || root.EndsWith("Roaming", StringComparison.OrdinalIgnoreCase) || root.EndsWith("Local", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            foreach (var inner in Directory.EnumerateDirectories(dir))
                            {
                                var si = Simple(Path.GetFileName(inner));
                                if (si.Length >= 3 && (si == name || si == original)) found.Add(inner);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Installers the game ships for Visual C++ and DirectX (usually in _CommonRedist).</summary>
    public static List<string> RedistributablesOf(Game game)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(game.InstallDir) || !Directory.Exists(game.InstallDir)) return list;
        try
        {
            foreach (var file in Directory.EnumerateFiles(game.InstallDir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }))
            {
                var n = Path.GetFileName(file).ToLowerInvariant();
                var dir = file.ToLowerInvariant();
                if (!(dir.Contains("redist") || dir.Contains("directx") || dir.Contains("vcredist"))) continue;
                if (n.StartsWith("vc_redist") || n.StartsWith("vcredist") || n == "dxsetup.exe") list.Add(file);
            }
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>Quiet-install arguments for a redistributable installer.</summary>
    public static string QuietArgs(string installer) =>
        Path.GetFileName(installer).Equals("DXSETUP.exe", StringComparison.OrdinalIgnoreCase) ? "/silent" : "/install /quiet /norestart";

    /// <summary>Programs in the game's folder that could be the game (for "Run as administrator").</summary>
    public static List<string> ProgramsOf(Game game)
    {
        var list = new List<FileInfo>();
        if (string.IsNullOrEmpty(game.InstallDir) || !Directory.Exists(game.InstallDir)) return new();
        try
        {
            foreach (var f in new DirectoryInfo(game.InstallDir).EnumerateFiles("*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
            {
                var n = f.Name.ToLowerInvariant();
                var path = f.FullName.ToLowerInvariant();
                if (path.Contains("redist") || n.Contains("crash") || n.Contains("unins") || n.Contains("setup") || n.Contains("launcherhelper")) continue;
                list.Add(f);
            }
        }
        catch (Exception) { }
        return list.OrderByDescending(f => f.Length).Take(8).Select(f => f.FullName).ToList();
    }

    // ================================================================ battery health

    public sealed record BatteryHealth(long Design, long Full, int? Cycles, string Name, List<(DateTime When, long Full, long Design)> History);

    /// <summary>Windows' battery report (powercfg), read from its XML form. Null on PCs without a battery.</summary>
    public static async Task<BatteryHealth?> ReadBatteryAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), "squaredeck-battery.xml");
        try
        {
            if (File.Exists(file)) File.Delete(file);
            using (var p = Process.Start(new ProcessStartInfo("powercfg", $"/batteryreport /xml /output \"{file}\"") { CreateNoWindow = true, UseShellExecute = false }))
                if (p != null) await p.WaitForExitAsync();
            if (!File.Exists(file)) return null;
            var doc = XDocument.Load(file);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var battery = doc.Descendants(ns + "Battery").FirstOrDefault();
            if (battery == null) return null;
            long Num(XElement? e) => long.TryParse(e?.Value, out var v) ? v : 0;
            long design = Num(battery.Element(ns + "DesignCapacity")), full = Num(battery.Element(ns + "FullChargeCapacity"));
            int? cycles = int.TryParse(battery.Element(ns + "CycleCount")?.Value, out var c) ? c : null;
            string name = (battery.Element(ns + "Id")?.Value ?? "Battery").Trim();
            var history = new List<(DateTime, long, long)>();
            foreach (var h in doc.Descendants(ns + "HistoryEntry"))
            {
                if (!DateTime.TryParse((string?)h.Attribute("StartDate"), out var when)) continue;
                long f = long.TryParse((string?)h.Attribute("FullChargeCapacity"), out var fv) ? fv : 0;
                long d = long.TryParse((string?)h.Attribute("DesignCapacity"), out var dv) ? dv : design;
                if (f > 0) history.Add((when, f, d));
            }
            return design <= 0 ? null : new BatteryHealth(design, full, cycles, name, history);
        }
        catch (Exception) { return null; }
    }

    // ================================================================ keeping the PC awake (overnight downloads)

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

    public static void KeepAwake(bool on) => SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);

    // ================================================================ screenshots (Windows' own: Win + Alt + Print Screen)

    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private const byte VkLWin = 0x5B, VkMenu = 0x12, VkSnapshot = 0x2C;
    private const uint KeyUp = 0x2;

    /// <summary>Takes a screenshot of the game in front with Windows' Game Bar (it goes to Videos\Captures).</summary>
    public static void GameBarScreenshot()
    {
        keybd_event(VkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkSnapshot, 0, 0, UIntPtr.Zero);
        keybd_event(VkSnapshot, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkLWin, 0, KeyUp, UIntPtr.Zero);
    }

    /// <summary>Renames the newest capture (made in the last few seconds) to end with the achievement's name.</summary>
    public static async Task NameNewestCaptureAsync(string folder, string achievement)
    {
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(500);
            try
            {
                if (!Directory.Exists(folder)) continue;
                var newest = new DirectoryInfo(folder).EnumerateFiles("*.png").Concat(new DirectoryInfo(folder).EnumerateFiles("*.jpg"))
                    .Where(f => DateTime.Now - f.CreationTime < TimeSpan.FromSeconds(15)).OrderByDescending(f => f.CreationTime).FirstOrDefault();
                if (newest == null) continue;
                var safe = new string(achievement.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? ' ' : ch).ToArray()).Trim();
                var to = Path.Combine(folder, Path.GetFileNameWithoutExtension(newest.Name) + " - " + safe + newest.Extension);
                await Task.Delay(800); // let the Game Bar finish writing it
                if (!File.Exists(to)) File.Move(newest.FullName, to);
                return;
            }
            catch (Exception) { }
        }
    }

    // ================================================================ achievements with their internal names and rarity

    public sealed record AchievementInfo(string Api, string Name, string Description, string Icon, bool Unlocked, DateTime? When);

    /// <summary>The user's achievements for a game with internal names and unlock times (public Steam profile needed).</summary>
    public static async Task<List<AchievementInfo>?> AchievementsAsync(int appId, CancellationToken ct)
    {
        var steamId = SteamLibrary.GetSteamId64();
        if (steamId == null) return null;
        try
        {
            var xml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/stats/{appId}/?xml=1", ct);
            if (!xml.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return null;
            var list = new List<AchievementInfo>();
            foreach (var a in XDocument.Parse(xml).Descendants("achievement"))
            {
                bool unlocked = (string?)a.Attribute("closed") == "1";
                DateTime? when = long.TryParse((string?)a.Element("unlockTimestamp"), out var unix) && unix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : null;
                list.Add(new AchievementInfo(((string?)a.Element("apiname") ?? "").Trim(), ((string?)a.Element("name") ?? "").Trim(),
                    ((string?)a.Element("description") ?? "").Trim(), (((string?)a.Element(unlocked ? "iconClosed" : "iconOpen")) ?? "").Trim(), unlocked, when));
            }
            return list.Count == 0 ? null : list;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Worldwide unlock percentage of each achievement by internal name (Steam's public numbers).</summary>
    public static async Task<Dictionary<string, double>> RarityAsync(int appId, CancellationToken ct)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = await Http.GetStringAsync($"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var a in doc.RootElement.GetProperty("achievementpercentages").GetProperty("achievements").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                double pct = a.GetProperty("percent").ValueKind == JsonValueKind.String
                    ? double.Parse(a.GetProperty("percent").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : a.GetProperty("percent").GetDouble();
                map[name] = pct;
            }
        }
        catch (Exception) { }
        return map;
    }

    public static string GuideSearch(Game game, string achievement) =>
        "https://www.google.com/search?q=" + Uri.EscapeDataString($"\"{game.Name}\" \"{achievement}\" achievement guide");
}
