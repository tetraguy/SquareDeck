using System.IO;
using System.Text.Json;

namespace SquareDeck.Services;

/// <summary>User settings, stored in %LOCALAPPDATA%\SquareDeck\settings.json.</summary>
public sealed class Settings
{
    /// <summary>Key used until the user saves a different one in Settings.</summary>
    private const string DefaultApiKey = "92f04a765a92a367d9135cec7b563540";

    public string SteamGridDbApiKey { get; set; } = DefaultApiKey;
    /// <summary>Kept so settings saved by older versions still load; see <see cref="ThemeMode"/>.</summary>
    public bool DarkTheme { get; set; }

    /// <summary>"light", "dark" or "system" (follow the Windows app theme).</summary>
    public string? ThemeMode { get; set; }

    /// <summary>Highlight / title color as #RRGGBB, "system" to follow the Windows accent color, or empty for the theme's own.</summary>
    public string AccentColor { get; set; } = "";

    /// <summary>Short controller rumble each time a tile is highlighted.</summary>
    public bool Vibration { get; set; }

    public bool UiSounds { get; set; }

    /// <summary>"preset:N" for a built-in picture, or the path of a custom image.</summary>
    public string Avatar { get; set; } = "preset:0";

    /// <summary>Blurred artwork of the highlighted game behind the home row.</summary>
    public bool HeroBackgrounds { get; set; } = true;

    // Explorer: folders pinned to its sidebar, grid or list, sort order, and hidden files.
    public List<string> ExplorerPins { get; set; } = new();
    public string ExplorerView { get; set; } = "grid";
    public bool WelcomeMessage { get; set; } = true;
    // The performance overlay (Power and performance)
    public bool OsdEnabled { get; set; }
    public string OsdWhen { get; set; } = "games";        // games (full-screen programs) | always
    public string OsdPosition { get; set; } = "top-left";
    public string OsdLayout { get; set; } = "row";         // row | column | tiles
    public string OsdDetail { get; set; } = "standard";    // minimal | standard | full
    public string OsdStyle { get; set; } = "dark";         // dark | glass | text | neon | accent | retro
    public string OsdSize { get; set; } = "medium";
    public int OsdOpacity { get; set; } = 90;          // "Good morning, Richy" by the profile picture at startup
    public List<string> CloudServices { get; set; } = new(); // cloud gaming services shown on the Stores page (xcloud, geforcenow, luna, antstream)
    public string CloudCorner { get; set; } = "top-right";    // where the little exit button sits during a cloud game
    public string LibraryLayout { get; set; } = "sidebar"; // Game library: sidebar, compact, large, list, shelves or spotlight
    public string LibraryView { get; set; } = "square"; // Game library: square covers, tall "grid" art, or wide "header" capsules
    public string ExplorerSort { get; set; } = "name";
    public bool ExplorerSortDescending { get; set; }
    public bool ExplorerShowHidden { get; set; }

    // Task Manager: the tab it opens on, the sort, and how often it updates (0 = paused).
    public string TaskManagerTab { get; set; } = "Apps";
    public string TaskManagerSort { get; set; } = "memory";
    public int TaskManagerRefreshMs { get; set; } = 1500;

    /// <summary>The last few things searched for (Search everything), newest first.</summary>
    public List<string> RecentSearches { get; set; } = new();

    // ---- TDP control (Settings > Power and performance). See TdpControl.
    public bool TdpEnabled { get; set; }
    /// <summary>The processor power limit, in watts.</summary>
    public int TdpWatts { get; set; } = 15;
    /// <summary>A different limit while plugged in (0 = the same).</summary>
    public int TdpPluggedWatts { get; set; }
    public bool CpuBoost { get; set; } = true;
    /// <summary>Maximum CPU speed as a percentage (100 = no cap).</summary>
    public int MaxCpu { get; set; } = 100;
    /// <summary>Windows' power mode: "", "efficiency", "balanced" or "performance".</summary>
    public string WindowsPowerMode { get; set; } = "";
    /// <summary>Processor temperature limit in °C (0 = the device's own).</summary>
    public int TdpTempLimit { get; set; }

    /// <summary>Boost window: "off" (all three limits equal) or "short" (brief bursts above the limit, like the device's own software).</summary>
    public string TdpBoostWindow { get; set; } = "off";

    /// <summary>RyzenAdj's own power profile: "" (leave), "saving" or "performance".</summary>
    public string RyzenProfile { get; set; } = "";

    /// <summary>Energy preference for the CPU, 0 (performance) to 100 (power saving); -1 leaves Windows' setting.</summary>
    public int CpuEpp { get; set; } = -1;

    /// <summary>Share of CPU cores Windows may use, in percent (100 = all). Fewer cores save battery in light games.</summary>
    public int CpuCores { get; set; } = 100;
    /// <summary>Apply the limit again every few seconds (for devices whose own software puts theirs back).</summary>
    public bool TdpReapply { get; set; }

    /// <summary>Choosing a UI style also brings its wallpaper or moving background, sounds, menu music and colors.</summary>
    public bool StyleMatching { get; set; } = true;

    /// <summary>Performance mode: "off", "battery" (on while unplugged) or "on". See Perf.</summary>
    public string PerformanceMode { get; set; } = "off";

    /// <summary>Web pages saved in the browser's favorites, in order.</summary>
    public List<BrowserFavorite> BrowserFavorites { get; set; } = new();

    /// <summary>Settings' list of sections shrunk to icons only.</summary>
    public bool SettingsNavCollapsed { get; set; }

    /// <summary>The game library's sidebar shrunk to a blade of icons.</summary>
    public bool LibrarySidebarCollapsed { get; set; }

    /// <summary>Moving background: a name from Backgrounds\ (or a video file's full path); empty for none.</summary>
    public string DynamicBackground { get; set; } = "";

    /// <summary>How much the moving background is blurred (blur radius, 0 for none).</summary>
    public int DynamicBlur { get; set; }

    /// <summary>The look of the whole launcher (see UiStyles.All for the ids).</summary>
    public string UiStyle { get; set; } = "switch";

    /// <summary>Come back to the front when a game closes.</summary>
    public bool ReturnToLauncher { get; set; } = true;

    /// <summary>Start Steam games from their own program instead of the steam:// link (no Steam launch pop-up).</summary>
    public bool SteamDirectLaunch { get; set; }

    /// <summary>Guide button (or View+Menu together) brings the launcher to the front from anywhere.</summary>
    public bool GuideSummon { get; set; } = true;

    /// <summary>Show games from Epic, GOG, Xbox, Battle.net and Steam's non-Steam shortcuts.</summary>
    public bool OtherStores { get; set; } = true;

    /// <summary>Other stores left out of the library (ids: epic, gog, battlenet, xbox, steamshortcuts).</summary>
    public List<string> DisabledStores { get; set; } = new();

    /// <summary>Keyboard for typing in SquareDeck: "squaredeck" (built-in, works with a controller) or "windows" (touch keyboard).</summary>
    public string TouchKeyboard { get; set; } = "windows";

    /// <summary>The keyboard was picked by hand (Windows' touch keyboard became the default in 5.9; earlier picks of
    /// SquareDeck's keyboard were the old default, so they're moved over once).</summary>
    public bool KeyboardChosen { get; set; }

    /// <summary>Home background: empty for none, "preset:N" for a built-in one, or an image path.</summary>
    public string Wallpaper { get; set; } = "";

    /// <summary>A color theme for the base colors ("" = the standard grays, "gtavi" = GTA VI). See Theme.Palettes.</summary>
    public string ThemePalette { get; set; } = "";

    /// <summary>Corner radius of the game tiles: 0 square, 10 rounded, 22 round.</summary>
    public int TileCorner { get; set; }

    /// <summary>"classic", "soft", "retro", or "custom" (sound files from <see cref="SoundFolder"/>).</summary>
    public string SoundSet { get; set; } = "classic";

    public string SoundFolder { get; set; } = "";

    /// <summary>Large "Continue playing" tile at the start of the home row.</summary>
    public bool ContinueTile { get; set; } = true;

    /// <summary>Pop-up when a game you just closed gave you new achievements.</summary>
    public bool AchievementToasts { get; set; } = true;

    /// <summary>Controller rumble strength, 0 (off) to 100. -1 means "not set yet" (older settings files).</summary>
    public int RumbleStrength { get; set; } = -1;

    /// <summary>Silence Windows notification pop-ups while a game is running.</summary>
    public bool DoNotDisturb { get; set; }

    /// <summary>Set while we have notification pop-ups switched off, so a crash can't leave them off.</summary>
    public bool ToastsSuppressed { get; set; }

    /// <summary>Folder that receives an automatic backup each time the launcher closes. Empty for none.</summary>
    public string BackupFolder { get; set; } = "";

    /// <summary>Card with session length and achievements when a game closes.</summary>
    public bool SessionSummary { get; set; } = true;

    /// <summary>While a game runs, the summon shortcut opens a small overlay instead of the whole launcher.</summary>
    public bool InGameQuickMenu { get; set; } = true;

    /// <summary>Tint the highlight to match the highlighted game's cover.</summary>
    public bool GameAccent { get; set; }

    /// <summary>Three-second "Starting…" window with Cancel before a game launches.</summary>
    public bool LaunchCountdown { get; set; }

    /// <summary>Length of the launch countdown, in seconds.</summary>
    public int LaunchCountdownSeconds { get; set; } = 3;

    /// <summary>Before a Steam game with a waiting update starts, ask whether to update it first.</summary>
    public bool AskAboutUpdates { get; set; } = true;

    /// <summary>Background music pauses while your own music plays (the Music page, Spotify, any media player).</summary>
    public bool MenuMusicPausesForMedia { get; set; } = true;

    /// <summary>YouTube in the Streaming tab uses its TV layout (built for arrow keys / a controller).</summary>
    public bool StreamingYouTubeTv { get; set; }

    /// <summary>Show a splash screen with the game's cover from pressing Play until the game is running.</summary>
    public bool LaunchSplash { get; set; } = true;

    /// <summary>Limit for downloaded art and video, in megabytes. 0 means no limit.</summary>
    public int CacheLimitMb { get; set; }

    public bool Clock24Hour { get; set; }
    public bool ClockSeconds { get; set; }
    public bool ClockDate { get; set; }

    /// <summary>Video played instead of the logo animation at startup. Empty for the built-in animation.</summary>
    public string BootVideo { get; set; } = "";

    /// <summary>False until the first-run setup has been completed (or skipped).</summary>
    public bool? SetupDone { get; set; }

    /// <summary>Sound played when a game starts: "off", "rise", "sparkle", "powerup", or "custom".</summary>
    public string LaunchChime { get; set; } = "rise";

    /// <summary>WAV file used when <see cref="LaunchChime"/> is "custom".</summary>
    public string LaunchChimeFile { get; set; } = "";

    /// <summary>How buttons are named in hints: "xbox", "playstation" or "switch".</summary>
    public string GlyphStyle { get; set; } = "xbox";

    /// <summary>How buttons are drawn in hints: "text" (names on pills), "default" (controller icons) or "pixel".</summary>
    public string GlyphLook { get; set; } = "text";

    /// <summary>Music apps removed from the "Open player" list (by app id).</summary>
    public List<string> HiddenPlayers { get; set; } = new();

    /// <summary>Background music on the home screen: empty for none, or "pack:&lt;folder&gt;" for a music sound pack.</summary>
    public string MenuMusic { get; set; } = "";

    /// <summary>Looks the user saved (Settings > Appearance > My themes): every appearance setting at once.</summary>
    public List<SavedTheme> SavedThemes { get; set; } = new();

    /// <summary>Loudness of the menu music, in percent.</summary>
    public int MenuMusicVolume { get; set; } = 30;

    /// <summary>Look for a newer release on GitHub when the launcher starts.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>A release the user chose to ignore; it isn't offered again.</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>What shows after the home screen has been idle: "off", "gradient" or "heroes".</summary>
    public string Screensaver { get; set; } = "off";

    /// <summary>Idle minutes before the screen saver starts.</summary>
    public int ScreensaverMinutes { get; set; } = 5;

    /// <summary>Optional round buttons on the home screen.</summary>
    public bool ShowBrowserButton { get; set; } = true;
    public bool ShowMusicButton { get; set; } = true;

    /// <summary>SquareDeck is registered as the Xbox mode home app (Services/HomeApp), and kept registered from this folder.</summary>
    public bool HomeAppOn { get; set; }

    /// <summary>Windows' home app before SquareDeck took over, given back when it's switched off.</summary>
    public string PreviousHomeApp { get; set; } = "";

    /// <summary>The pages LB / RB flip through, in order (ids from MainWindow.CyclePages).</summary>
    public List<string> PageOrder { get; set; } = new();

    /// <summary>Pages LB / RB skip over (they can still be opened from their buttons).</summary>
    public List<string> PagesOff { get; set; } = new();
    public bool ShowFriendsButton { get; set; } = true;
    public bool ShowExplorerButton { get; set; } = true;

    /// <summary>The News button on the home screen.</summary>
    public bool ShowNewsButton { get; set; } = true;

    /// <summary>Built-in web browser: start page, search engine ("google", "bing", "duckduckgo") and zoom in percent.</summary>
    public string BrowserHome { get; set; } = "https://www.google.com";
    public string BrowserSearch { get; set; } = "google";
    public int BrowserZoom { get; set; } = 125;

    /// <summary>Keep the browser (and its tabs) running when you leave it, so it comes back exactly where you were.</summary>
    public bool BrowserKeepOpen { get; set; } = true;

    /// <summary>The right stick moves a mouse pointer in the browser (otherwise it scrolls).</summary>
    public bool BrowserStickMouse { get; set; } = true;

    /// <summary>Most browser tabs open at once (each one costs memory and battery).</summary>
    public int BrowserMaxTabs { get; set; } = 6;

    /// <summary>The starter favorites (YouTube, Twitch...) have been added once.</summary>
    public bool BrowserFavoritesSeeded { get; set; }

    /// <summary>A notification when the battery runs low (and again when it's nearly empty).</summary>
    public bool LowBatteryAlert { get; set; } = true;

    /// <summary>Battery percentage for the low-battery notification.</summary>
    public int LowBatteryLevel { get; set; } = 20;

    /// <summary>Closing animation: "logo", "fade", "shrink", "curtain" or "tv".</summary>
    public string ExitStyle { get; set; } = "logo";

    /// <summary>Layout shape: "16:9", "16:10", or "auto" to follow the screen.</summary>
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>Loudness of interface sounds, in percent (25 to 100).</summary>
    public int SoundVolume { get; set; } = 100;

    /// <summary>Which kinds of interface sound play: moving the highlight, select / back, changing page, pop-ups.</summary>
    public bool SoundMove { get; set; } = true;
    public bool SoundSelect { get; set; } = true;
    public bool SoundPage { get; set; } = true;
    public bool SoundToast { get; set; } = true;

    /// <summary>Remap the ROG Ally's Armoury Crate / Command Center / Library buttons.</summary>
    public bool AllyRemap { get; set; }

    /// <summary>Ally button id ("ac", "mode-cc", ...) to action id (see AllyButtons.Actions).</summary>
    public Dictionary<string, string> AllyBindings { get; set; } = AllyButtons.DefaultBindings();

    /// <summary>Also handle the Ally's other keys (microphone, screenshot, keyboard, record).</summary>
    public bool AllyExtraKeys { get; set; } = true;

    /// <summary>Open Captures showing one album per game.</summary>
    public bool CaptureAlbums { get; set; }
    public bool CaptureAlbumIcons { get; set; } = true; // the game's picture next to each album's name
    public bool RumbleOnNotifications { get; set; } = true; // a short buzz when a notification pops up
    public bool RumbleInSettings { get; set; } = true;      // a light tick moving from row to row in Settings
    public bool PerformanceBoost { get; set; }              // free memory before a game starts, with an animation
    public string BoostAnimation { get; set; } = "rings";   // rings, warp, gauge, orbit, shockwave or random
    public bool BoostRumble { get; set; } = true;           // the animation's vibration pattern

    /// <summary>Store apps hidden from the Stores page (by name).</summary>
    public List<string> HiddenStores { get; set; } = new();

    /// <summary>Hand the graphics card back to the game: draw the launcher in software while a game runs.</summary>
    public bool FreeGpuDuringGames { get; set; } = true;

    /// <summary>Search Spotify and YouTube Music from the Music page.</summary>
    public bool MusicServices { get; set; }

    /// <summary>Which service the Music page searches: "youtube" or "spotify".</summary>
    public string MusicService { get; set; } = "youtube";

    /// <summary>The Client ID of the user's own Spotify app (developer.spotify.com).</summary>
    public string SpotifyClientId { get; set; } = "";

    /// <summary>The permissions version of the Spotify sign-in (see SpotifyClient.ScopesVersion).</summary>
    public int SpotifyScopes { get; set; }

    /// <summary>Spotify plays inside SquareDeck (as a Spotify device called "SquareDeck"), no Spotify app needed.</summary>
    public bool SpotifyInLauncher { get; set; } = true;

    /// <summary>The folder the Music page's own player plays from.</summary>
    public string MusicFolder { get; set; } = "";

    /// <summary>The Music page's tab: "now", "local" or "spotify".</summary>
    public string MusicTab { get; set; } = "now";

    public int LocalVolume { get; set; } = 80;
    public bool LocalShuffle { get; set; }
    public string LocalRepeat { get; set; } = "off";

    /// <summary>Spotify sign-in, encrypted for this Windows account (see LocalSecret).</summary>
    public string SpotifyToken { get; set; } = "";

    /// <summary>Device name of the display the launcher opens on ("" = wherever Windows puts it).</summary>
    public string MainDisplay { get; set; } = "";

    /// <summary>Show other apps' Windows notifications in the launcher too.</summary>
    public bool WindowsNotifications { get; set; } = true;

    /// <summary>Fewer moving parts: no pulsing highlight, slides, fades or intro animation.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Home screen buttons grow when highlighted and spring when pressed.</summary>
    public bool ButtonAnimations { get; set; } = true;

    /// <summary>How home buttons react to the highlight: "grow", "lift", "bounce", "pulse" or "wiggle".</summary>
    public string ButtonAnimationStyle { get; set; } = "grow";

    /// <summary>How pages open: "slide", "fade", "zoom" or "none".</summary>
    public string PageTransition { get; set; } = "slide";

    /// <summary>The selected game's tile: "pulse" (breathing ring), "steady" (still ring) or "grow" (tile grows).</summary>
    public string TileHighlight { get; set; } = "pulse";

    /// <summary>Size of text and controls on pages, in percent (100, 115, 130 or 150).</summary>
    public int TextScale { get; set; } = 100;

    /// <summary>Fade-out animation when leaving with "Exit SquareDeck".</summary>
    public bool ExitAnimation { get; set; } = true;

    /// <summary>Startup animation: "logo", "tiles", "sweep", "rings", "typed", "zoom" or "random".</summary>
    public string BootAnimation { get; set; } = "logo";

    /// <summary>Controller buttons that summon the launcher / in-game menu: "default", "guide", "view+menu", "l3+r3", "lb+rb+view".</summary>
    public string SummonCombo { get; set; } = "default";

    /// <summary>Keyboard shortcut that does the same: "none", "f9", "f10", "ctrl+alt+q", "ctrl+shift+space".</summary>
    public string SummonKey { get; set; } = "none";

    /// <summary>Where the in-game quick menu appears: "right", "left" or "center".</summary>
    public string OverlaySide { get; set; } = "right";

    /// <summary>Wash the game details page in a soft gradient of the cover's colors.</summary>
    public bool DetailsGradient { get; set; } = true;

    /// <summary>Show the game's logo artwork instead of its name at the top of the details page.</summary>
    public bool DetailsLogo { get; set; } = true;

    /// <summary>Play SteamGridDB's animated cover on the highlighted tile, when one exists.</summary>
    public bool AnimatedCovers { get; set; } = true;

    /// <summary>Chime and short animation when the launcher opens.</summary>
    public bool StartupIntro { get; set; } = true;

    /// <summary>The user's own Steam Web API key, for the Friends page. Empty until they add one.</summary>
    public string SteamWebApiKey { get; set; } = "";

    /// <summary>Show the battery percentage next to the battery icon.</summary>
    public bool ShowBatteryPercent { get; set; } = true;

    /// <summary>Battery of connected Bluetooth headphones beside the Bluetooth icon on the home screen.</summary>
    public bool ShowHeadphoneBattery { get; set; } = true;

    /// <summary>Steam friends pinned to the top of the Friends page (SteamID64s).</summary>
    public List<string> PinnedFriends { get; set; } = new();

    /// <summary>Pop-ups when friends come online or start a game: "all", "pinned" or "off".</summary>
    public string FriendAlerts { get; set; } = "all";

    /// <summary>Controller buttons for Select, Back, Details, Options, in that order.</summary>
    public string ButtonMap { get; set; } = "ABXY";

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquareDeck");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (string.IsNullOrWhiteSpace(s.SteamGridDbApiKey)) s.SteamGridDbApiKey = DefaultApiKey;
                    if (string.IsNullOrWhiteSpace(s.Avatar)) s.Avatar = "preset:0";
                    s.ThemeMode ??= s.DarkTheme ? "dark" : "light"; // saved before "follow system" existed
                    s.AccentColor ??= "";
                    s.SteamWebApiKey ??= "";
                    s.Wallpaper ??= "";
                    s.ThemePalette ??= "";
                    s.LaunchChime ??= "rise";
                    s.BackupFolder ??= "";
                    s.BootVideo ??= "";
                    s.BootAnimation ??= "logo";
                    s.SummonCombo ??= "default";
                    s.SummonKey ??= "none";
                    s.OverlaySide ??= "right";
                    s.MainDisplay ??= "";
                    s.HiddenStores ??= new();
                    s.ButtonAnimationStyle ??= "grow";
                    s.PageTransition ??= "slide";
                    s.TileHighlight ??= "pulse";
                    if (s.LaunchCountdownSeconds is < 1 or > 30) s.LaunchCountdownSeconds = 3;
                    s.DisabledStores ??= new();
                    s.ExplorerPins ??= new();
                    s.ExplorerView ??= "grid";
                    s.ExplorerSort ??= "name";
                    s.TaskManagerTab ??= "Apps";
                    s.DynamicBackground ??= "";
                    s.RecentSearches ??= new();
                    s.PerformanceMode ??= "off";
                    s.WindowsPowerMode ??= "";
                    s.BrowserFavorites ??= new();
                    s.UiStyle = UiStyles.Upgrade(s.UiStyle); // Glass became Frosted; Xbox and PlayStation were retired
                    s.TaskManagerSort ??= "memory";
                    s.TouchKeyboard ??= "windows";
                    if (!s.KeyboardChosen)
                    {
                        s.TouchKeyboard = "windows";
                        s.KeyboardChosen = true;
                    }
                    s.AllyBindings ??= AllyButtons.DefaultBindings();
                    foreach (var (k, v) in AllyButtons.DefaultBindings()) s.AllyBindings.TryAdd(k, v);
                    s.SpotifyClientId ??= "";
                    s.MusicService ??= "youtube";
                    s.SpotifyToken ??= "";
                    s.MusicFolder ??= "";
                    s.MusicTab ??= "now";
                    s.LocalRepeat ??= "off";
                    if (s.TextScale < 100 || s.TextScale > 200) s.TextScale = 100;
                    if (s.SoundVolume < 10 || s.SoundVolume > 100) s.SoundVolume = 100;
                    s.GlyphStyle ??= "xbox";
                    s.GlyphLook ??= "text";
                    s.HiddenPlayers ??= new();
                    s.PageOrder ??= new();
                    s.PagesOff ??= new();
                    s.TdpBoostWindow ??= "off";
                    s.RyzenProfile ??= "";
                    s.SavedThemes ??= new();
                    s.PinnedFriends ??= new();
                    s.FriendAlerts ??= "all";
                    s.PreviousHomeApp ??= "";
                    s.MenuMusic ??= "";
                    s.SkippedVersion ??= "";
                    s.Screensaver ??= "off";
                    if (s.ScreensaverMinutes < 1) s.ScreensaverMinutes = 5;
                    s.BrowserHome ??= "https://www.google.com";
                    s.BrowserSearch ??= "google";
                    if (s.BrowserZoom < 50 || s.BrowserZoom > 300) s.BrowserZoom = 125;
                    s.ExitStyle ??= "logo";
                    s.AspectRatio ??= "16:9";
                    s.SetupDone ??= true;                                   // an existing user: don't run first-time setup
                    if (s.RumbleStrength < 0) s.RumbleStrength = s.Vibration ? 60 : 0;
                    s.LaunchChimeFile ??= "";
                    s.SoundSet ??= "classic";
                    s.SoundFolder ??= "";
                    if (!Hints.IsValidMap(s.ButtonMap)) s.ButtonMap = "ABXY";
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt settings file: fall back to defaults.
        }
        return new Settings { ThemeMode = "system", SetupDone = false, RumbleStrength = 0 };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are a convenience; never crash the launcher over them.
        }
    }
}

/// <summary>A page saved in the browser's favorites.</summary>
public sealed class BrowserFavorite
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>A look the user saved: UI style, colors, wallpaper, moving background, sounds, music and button pictures.</summary>
public sealed class SavedTheme
{
    public string Name { get; set; } = "My theme";
    public DateTime Saved { get; set; } = DateTime.Now;
    public string UiStyle { get; set; } = "switch";
    public string? ThemeMode { get; set; }
    public string ThemePalette { get; set; } = "";
    public string AccentColor { get; set; } = "";
    public string Wallpaper { get; set; } = "";
    public string DynamicBackground { get; set; } = "";
    public int DynamicBlur { get; set; }
    public int TileCorner { get; set; }
    public string SoundSet { get; set; } = "classic";
    public string SoundFolder { get; set; } = "";
    public string MenuMusic { get; set; } = "";
    public string GlyphLook { get; set; } = "text";
    public bool HeroBackgrounds { get; set; } = true;

    public static SavedTheme From(Settings s, string name) => new()
    {
        Name = name, Saved = DateTime.Now, UiStyle = s.UiStyle, ThemeMode = s.ThemeMode, ThemePalette = s.ThemePalette ?? "",
        AccentColor = s.AccentColor ?? "", Wallpaper = s.Wallpaper ?? "", DynamicBackground = s.DynamicBackground ?? "",
        DynamicBlur = s.DynamicBlur, TileCorner = s.TileCorner, SoundSet = s.SoundSet ?? "classic", SoundFolder = s.SoundFolder ?? "",
        MenuMusic = s.MenuMusic ?? "", GlyphLook = s.GlyphLook ?? "text", HeroBackgrounds = s.HeroBackgrounds
    };

    /// <summary>Copies this look into the settings (the caller applies and saves).</summary>
    public void ApplyTo(Settings s)
    {
        s.UiStyle = UiStyle; s.ThemeMode = ThemeMode; s.ThemePalette = ThemePalette; s.AccentColor = AccentColor;
        s.Wallpaper = Wallpaper; s.DynamicBackground = DynamicBackground; s.DynamicBlur = DynamicBlur; s.TileCorner = TileCorner;
        s.SoundSet = SoundSet; s.SoundFolder = SoundFolder; s.MenuMusic = MenuMusic; s.GlyphLook = GlyphLook; s.HeroBackgrounds = HeroBackgrounds;
    }
}
