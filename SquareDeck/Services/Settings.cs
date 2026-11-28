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

    /// <summary>Highlight effect id ("" for none), its colors ("palette:id" or "#hex,#hex,…"), speed and glow.</summary>
    public string? HighlightEffect { get; set; } = HighlightEffects.SwitchEffect;
    public string? HighlightColors { get; set; } = HighlightEffects.SwitchColors;
    public string? HighlightSpeed { get; set; } = "normal";
    public string? HighlightGlow { get; set; } = "style";

    /// <summary>Short controller rumble each time a tile is highlighted.</summary>
    public bool Vibration { get; set; }

    public bool UiSounds { get; set; }

    /// <summary>"preset:N" for a built-in picture, or the path of a custom image.</summary>
    public string Avatar { get; set; } = "preset:0";

    /// <summary>Blurred artwork of the highlighted game behind the home row (off with the Switch style, which starts out without it).</summary>
    public bool HeroBackgrounds { get; set; }

    /// <summary>8.5.29: the hero background set once to off for the Switch style, its default.</summary>
    public bool HeroOff8529 { get; set; }
    public bool CloudIntro8533 { get; set; }
    public bool BrowserKeep8539 { get; set; }

    // Explorer: folders pinned to its sidebar, grid or list, sort order, and hidden files.
    public List<string> ExplorerPins { get; set; } = new();
    public List<FtpServer> FtpServers { get; set; } = new();
    public string ExplorerView { get; set; } = "grid";
    public bool WelcomeMessage { get; set; } = true;
    public bool SteamArtworkFirst { get; set; }              // set once SteamGridDB copies of Steam's own art were let go (6.3)
    public bool ShowHowLongToBeat { get; set; } = true;       // game pages: HowLongToBeat times (kept in hltb.json)
    public bool ShowPlayerStats { get; set; } = true;                 // game pages: players now, peaks, owners, monthly players, review history
    public bool ShowClock { get; set; } = true;
    public int WallpaperBlur { get; set; }                  // still wallpaper blur radius (same levels as the moving background)
    public string UiFont { get; set; } = "style";           // app-wide font, or "style" for the UI style's own
    public string TileArt { get; set; } = "style";          // home row art: style | square | tall | wide
    public string TileSize { get; set; } = "style";         // home row size: style | small | medium | large
    // The choices of home button style, button shelf and footer were removed in 6.4.1 (theming is being rebuilt).
    // Kept so older settings files load; always "style" (the UI style's own) and no longer read.
    public string NavButtonStyle { get; set; } = "style";
    public string FooterStyle { get; set; } = "style";
    public string DockStyle { get; set; } = "style";
    public string LockScreenSource { get; set; } = "";       // the picture SquareDeck last put on the Windows lock screen
    // The performance overlay (Power and performance)
    public bool OsdEnabled { get; set; }
    /// <summary>Lets SquareDeck's Xbox Game Bar widget connect (TDP, stats and quick toggles from Game Bar).</summary>
    public bool GameBarWidget { get; set; } = true;
    /// <summary>The widget package version SquareDeck last tried to install by itself (so a declined prompt isn't repeated).</summary>
    public string WidgetInstallTried { get; set; } = "";
    /// <summary>"Don't ask again" on the start-up offer to install the Game Bar widget.</summary>
    public bool WidgetPromptIgnored { get; set; }
    /// <summary>The Game Bar widget's colors: "squaredeck" (teal), "style" (the UI style's highlight color) or "game" (the running game's art).</summary>
    public string? WidgetTheme { get; set; } = "squaredeck";
    /// <summary>The widget's background: squaredeck, glass, oled, accent, aurora, sunset or game (the running game's art).</summary>
    public string WidgetBackground { get; set; } = "squaredeck";
    /// <summary>The widget's quick settings that show, in order (null: all of them). Changed from the widget's Edit button or Settings.</summary>
    public List<string>? WidgetQuick { get; set; }
    /// <summary>9.0.16: the order of the round buttons under the games, by id (null: as they come). See MainWindow.HomeButtonIds.</summary>
    public List<string>? HomeButtonOrder { get; set; }
    /// <summary>Every quick setting the widget has (id, name), in its own order.</summary>
    public static readonly (string Id, string Name)[] WidgetQuickTiles =
    {
        ("perf", "Overlay"), ("refresh", "Refresh rate"), ("nightlight", "Night light"), ("hdr", "HDR"), ("dnd", "Do not disturb"),
        ("playpause", "Play / pause"), ("wifi", "Wi-Fi"), ("bluetooth", "Bluetooth"), ("keyboard", "Keyboard"), ("taskmanager", "Task Manager"),
        ("amdoverlay", "AMD overlay"), ("screenoff", "Screen off"), ("mic", "Microphone"), ("desktop", "Show desktop"), ("amdmetrics", "AMD metrics"),
        ("cpuboost", "CPU boost"), ("ris", "Image sharpening"), ("chill", "Radeon Chill"), ("ledlevel", "LED brightness"),
        ("airplane", "Airplane mode"),
    };
    /// <summary>The widget's cards that show, in order (Power always shows).</summary>
    public List<string>? WidgetSections { get; set; } = new(WidgetSectionIds);
    public static readonly string[] WidgetSectionIds = { "game", "live", "ally", "launch", "favorites", "quick" };
    public bool WidgetCards917 { get; set; } // 9.0.17: Launch and Favorites joined the cards that can be hidden (on to start with)
    /// <summary>The widget's Close game button (two presses) on the game card.</summary>
    public bool WidgetCloseGame { get; set; } = true;
    public string OsdWhen { get; set; } = "games";        // games (full-screen programs) | always
    public string OsdPosition { get; set; } = "top-left";
    public string OsdLayout { get; set; } = "row";         // row | column | tiles
    public string OsdDetail { get; set; } = "standard";    // minimal | standard | full
    public string OsdStyle { get; set; } = "dark";         // dark | glass | text | neon | accent | retro
    public string OsdSize { get; set; } = "medium";
    public string OsdItems { get; set; } = "";            // Details > Custom: the readings, comma-separated (PerfOverlay.Readings)
    public string OsdMeters { get; set; } = "numbers";    // numbers | bars | rings | blocks
    public string OsdShape { get; set; } = "rounded";     // rounded | square | pill
    public bool OsdColors { get; set; } = true;           // green / amber / red by how hard something works
    public int OsdOpacity { get; set; } = 90;          // "Good morning, Richy" by the profile picture at startup
    public List<string> CloudServices { get; set; } = new(); // cloud gaming services shown on the Stores page (xcloud, geforcenow, luna, antstream)
    public string CloudCorner { get; set; } = "top-right";
    /// <summary>Controller buttons held to leave a cloud game (an id from CloudGameScreen.LeaveButtons).</summary>
    public string CloudLeaveButtons { get; set; } = "view+start";
    /// <summary>How long they're held, in seconds.</summary>
    public double CloudLeaveHold { get; set; } = 1.5;
    /// <summary>Keyboard shortcut that leaves a cloud game (an id from CloudGameScreen.LeaveKeys).</summary>
    public string CloudLeaveKey { get; set; } = "ctrl+shift+q";
    public string CloudIntro { get; set; } = "switch2";     // switch2 | skyrise | storm | beam | random | none    // where the little exit button sits during a cloud game
    public string LibraryLayout { get; set; } = "sidebar"; // Game library: sidebar, compact, large, list, shelves or spotlight
    public string LibraryView { get; set; } = "square"; // Game library: square covers, tall "grid" art, or wide "header" capsules
    public string ExplorerSort { get; set; } = "name";
    public bool ExplorerSortDescending { get; set; }
    public bool ExplorerShowHidden { get; set; }
    /// <summary>The features that left Experimental in 7.2 have been turned on once (those meant to be on by default).</summary>
    public bool LabsGraduated72 { get; set; }
    public bool LabsGraduated818 { get; set; }
    public bool LabsGraduated819 { get; set; }
    public bool LabsGraduated8124 { get; set; }
    public bool LabsGraduated8158 { get; set; }
    public bool SwitchBootOff8541 { get; set; }
    public bool TdpBoost8542 { get; set; }
    public bool ParallaxOff8160 { get; set; }
    public bool LabsGraduated8162 { get; set; }
    public bool OemFooterOff8165 { get; set; }
    public bool LabsGraduated8166 { get; set; }
    public bool LabsGraduated8168 { get; set; }
    public bool LabsGraduatedPcTools { get; set; }
    public bool LabsGraduated85102 { get; set; }
    /// <summary>9.0.20: the companion playground left Experimental (on once, with a switch).</summary>
    public bool LabsGraduated9020 { get; set; }
    public bool NavSwitch2_903 { get; set; }
    public bool PlayerStats913 { get; set; }  // 9.0.13: Player stats on game details is on by default
    /// <summary>Explorer's sidebar is open (L1 or F1 folds it away).</summary>
    public bool ExplorerSidebarOpen { get; set; } = true;

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
    public string TdpBoostWindow { get; set; } = "short";

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

    /// <summary>The look chosen in Settings > Themes (see Looks.All). "switch" is the original design.</summary>
    public string LookId { get; set; } = "switch";

    /// <summary>The look of the whole launcher. Only "switch" exists now (see UiStyles.All); kept for the theming rebuild.</summary>
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
    public string ThemePalette { get; set; } = "steel";

    /// <summary>Set once Steel has been made the Switch style's color theme for someone who had none.</summary>
    public bool SteelDefault { get; set; }
    /// <summary>Set once the Switch 2 gradient highlight has been given to someone already on Switch (7.3.17).</summary>
    public bool SwitchRingDefault { get; set; }

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
    public bool Rumble917 { get; set; } // 9.0.17: interface rumble on by default

    /// <summary>Silence Windows notification pop-ups while a game is running.</summary>
    public bool DoNotDisturb { get; set; }

    /// <summary>Set while we have notification pop-ups switched off, so a crash can't leave them off.</summary>
    public bool ToastsSuppressed { get; set; }

    /// <summary>Folder that receives an automatic backup each time the launcher closes. Empty for none.</summary>
    public string BackupFolder { get; set; } = "";

    /// <summary>Card with session length and achievements when a game closes.</summary>
    public bool SessionSummary { get; set; } = true;

    /// <summary>While a game runs, the summon shortcut opens SquareDeck's Game Bar widget instead of the whole launcher.</summary>
    public bool InGameQuickMenu { get; set; } = true;

    /// <summary>9.0.21: what opens over a game: "widget" (SquareDeck's Xbox Game Bar widget) or "squaredeck" (the quick menu).</summary>
    public string InGameMenu { get; set; } = "widget";
    /// <summary>9.0.21: the quick menu's side: left or right.</summary>
    public string QuickMenuSide { get; set; } = "left";
    /// <summary>9.0.21: the quick menu takes the focus (and so the controller) from the game while it's open.</summary>
    public bool QuickMenuFocus { get; set; } = true;

    /// <summary>Tint the highlight to match the highlighted game's cover.</summary>
    public bool GameAccent { get; set; }

    /// <summary>Three-second "Starting…" window with Cancel before a game launches.</summary>
    public bool LaunchCountdown { get; set; }

    /// <summary>Length of the launch countdown, in seconds.</summary>
    public int LaunchCountdownSeconds { get; set; } = 3;

    /// <summary>How the countdown looks: "s2" (full screen, the Switch 2 manner) or "card" (the pop-up card).</summary>
    public string LaunchCountdownLook { get; set; } = "s2";

    /// <summary>Before a Steam game with a waiting update starts, ask whether to update it first.</summary>
    public bool AskAboutUpdates { get; set; } = true;

    /// <summary>Background music pauses while your own music plays (the Music page, Spotify, any media player).</summary>
    public bool MenuMusicPausesForMedia { get; set; } = true;

    /// <summary>YouTube in the Streaming tab uses its TV layout (built for arrow keys / a controller).</summary>
    public bool StreamingYouTubeTv { get; set; }
    /// <summary>The animation while a streaming service opens: an id from StreamingIntro.Styles.</summary>
    public string StreamingIntro { get; set; } = "random";
    /// <summary>How the library groups games: "", "category", "genre" or "played".</summary>
    public string LibraryGroupBy { get; set; } = "";

    /// <summary>Show a splash screen with the game's cover from pressing Play until the game is running.</summary>
    public bool LaunchSplash { get; set; } = true;
    /// <summary>Recomp Zone games start with a short "recompiling" animation.</summary>
    public bool RecompLaunchAnimation { get; set; } = true;

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
    public string GlyphStyle { get; set; } = "auto";

    /// <summary>How buttons are drawn in hints: "text" (names on pills), "default" (controller icons) or "pixel".</summary>
    public string GlyphLook { get; set; } = "text";

    /// <summary>Music apps removed from the "Open player" list (by app id).</summary>
    public List<string> HiddenPlayers { get; set; } = new();

    /// <summary>Background music on the home screen: empty for none, or "pack:&lt;folder&gt;" for a music sound pack.</summary>
    public string MenuMusic { get; set; } = "";

    /// <summary>Looks the user saved (Settings > Appearance > My themes): every appearance setting at once.</summary>
    public List<SavedTheme> SavedThemes { get; set; } = new();

    /// <summary>UI styles you made yourself (Settings > Themes > Your styles).</summary>
    public List<CustomStyle> CustomStyles { get; set; } = new();

    /// <summary>The sound set for moving around ("" = the same as <see cref="SoundSet"/>). Styles of your own can choose their own.</summary>
    public string NavSoundSet { get; set; } = "pack:Switch 2 Sounds"; // 9.0.3: the Switch style's own (a new install starts on Switch)

    /// <summary>The color variant chosen for each Advanced style that has some (style id → variant id).</summary>
    public Dictionary<string, string> SkinVariants { get; set; } = new();
    public HashSet<string> UnlockedStyles { get; set; } = new();   // Unlockable styles earned (Themes > Unlockable)
    public bool UnlocksChecked { get; set; }                        // the first check unlocks quietly what was already earned

    /// <summary>Loudness of the menu music, in percent.</summary>
    public int MenuMusicVolume { get; set; } = 30;
    /// <summary>Recomp Zone plays its own music while it's open (at the same loudness as the launcher's).</summary>
    public bool RecompZoneMusic { get; set; } = true;
    /// <summary>Retro Lounge plays its own music while it's open (RetroLounge\music.mp3), at the launcher's music loudness.</summary>
    public bool RetroLoungeMusic { get; set; } = true;
    /// <summary>Device health: a notice a little after start when something needs attention.</summary>
    public bool HealthStartupCheck { get; set; } = true;
    /// <summary>Device health checks set aside ("Don't warn me about this"), by id.</summary>
    public List<string> HealthDismissed { get; set; } = new();
    /// <summary>What's new: after an update, the new version's changes show once on the next start.</summary>
    public bool WhatsNewAfterUpdate { get; set; } = true;
    /// <summary>The last version whose What's new was shown (or that was first run).</summary>
    public string WhatsNewSeen { get; set; } = "";
    /// <summary>Retro Lounge: RetroAchievements sign-in (the user name and their web API key).</summary>
    public string RaUser { get; set; } = "";
    public string RaApiKey { get; set; } = "";
    /// <summary>Retro Lounge: a folder of No-Intro / Redump DAT files to check ROMs against.</summary>
    public string RetroDatFolder { get; set; } = "";

    /// <summary>Look for a newer release on GitHub when the launcher starts.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>When SquareDeck last looked for a new version (it looks every 12 hours while Check for updates automatically is on).</summary>
    public DateTime LastUpdateCheck { get; set; }

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
    public List<string> PagesOff { get; set; } = new() { "library", "captures", "music", "friends", "settings" };
    public bool ShowFriendsButton { get; set; } = true;
    public bool ShowExplorerButton { get; set; } = true;

    /// <summary>The News button on the home screen.</summary>
    public bool ShowNewsButton { get; set; } = true;
    public bool HomeButtons913 { get; set; } // 9.0.13: every home button on (My Device moved to Settings)

    /// <summary>
    /// 8.5.15: the home buttons were set once to the new defaults (Home, Favorites, Apps, Captures, Multimedia, Web
    /// browser, Explorer, Stores, Settings and Power; Friends, News and My Device off, each still a switch in Settings).
    /// </summary>
    public bool HomeButtons8515 { get; set; }

    /// <summary>8.5.16: LB / RB set once to flip between Home, Favorites and Apps only (the rest can be added back in Settings).</summary>
    public bool PageCycle8516 { get; set; }

    /// <summary>Built-in web browser: start page, search engine ("google", "bing", "duckduckgo") and zoom in percent.</summary>
    public string BrowserHome { get; set; } = "https://www.google.com";
    public string BrowserSearch { get; set; } = "google";
    public int BrowserZoom { get; set; } = 125;

    /// <summary>Keep the browser (and its tabs) running when you leave it, so it comes back exactly where you were.</summary>
    public bool BrowserKeepOpen { get; set; } // off: the browser is closed when you leave it (8.5.39); on, it sleeps in the background

    /// <summary>Web links anywhere in SquareDeck open in its own browser rather than Windows' default one.</summary>
    public bool OpenLinksInSquareDeck { get; set; } = true;

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
    /// <summary>Buttons set up by pressing them: raw code ("147", "m166" with Mode held) to action. These come first.</summary>
    public Dictionary<string, string> AllyCodeBindings { get; set; } = new();
    /// <summary>The Ally button (an id from AllyButtons.Buttons) that opens the in-game quick menu; "" = off. (Was experimental; the name is kept so the setting carries over.)</summary>
    public string ExperimentAllyQuickMenu { get; set; } = "";
    /// <summary>Which ROG Ally this is, when it isn't found by itself ("" = find out by itself).</summary>
    public string AllyModel { get; set; } = "";

    /// <summary>Also handle the Ally's other keys (microphone, screenshot, keyboard, record).</summary>
    public bool AllyExtraKeys { get; set; } = true;

    // ---- ROG Ally controls (Settings > ROG Ally). "" means leave it as ASUS / Armoury Crate set it.
    /// <summary>The lights: "" (leave), off, solid, breathing, rainbow, wheel, sticks, accent, battery, game.</summary>
    public string AllyLights { get; set; } = "";
    /// <summary>The effect to come back to when the lights are switched on again.</summary>
    public string AllyLightsLastOn { get; set; } = "";
    public string AllyLightColor { get; set; } = "#FF0033";
    public string AllyLightColor2 { get; set; } = "#00A0FF";
    /// <summary>slow, medium or fast.</summary>
    public string AllyLightSpeed { get; set; } = "medium";
    /// <summary>5, 10, 30, 60 or 100 (percent).</summary>
    public int AllyLightPercent { get; set; } = 100;
    /// <summary>The lights pulse red when the battery is low (whatever they're set to).</summary>
    public bool AllyLowBatteryFlash { get; set; } = true;
    /// <summary>The lights react: a gold sweep for an achievement, a green double pulse when a download or update finishes (8.5.49, off).</summary>
    public bool AllyLightReactions { get; set; }
    /// <summary>A game's details show how long the battery should last in it, from its own past sessions (8.5.49, off).</summary>
    public bool BatteryEstimate { get; set; }
    /// <summary>The running game is paused (frozen) while SquareDeck is in front, and carries on when you go back (8.5.49, off).</summary>
    public bool SuspendGames { get; set; }
    /// <summary>Auto TDP remembers, per game, the lowest power that held the frame rate, and starts there next time (8.5.49, off).</summary>
    public bool AutoTdpLearn { get; set; }
    /// <summary>Game lights: while a game runs, the lights use its game profile's effect (its cover's colors by default).</summary>
    public bool AllyGameLights { get; set; } = true;
    /// <summary>The lights go off while a game is running, and come back after.</summary>
    public bool AllyLightsOffPlaying { get; set; }
    /// <summary>The lights' power states have been chosen here (otherwise Armoury Crate SE's stay).</summary>
    public bool AllyLightPowerSet { get; set; }
    /// <summary>The rings light while the Ally boots up and shuts down.</summary>
    public bool AllyLightBootShutdown { get; set; } = true;
    /// <summary>The rings light while it's awake, on battery.</summary>
    public bool AllyLightAwakeBattery { get; set; } = true;
    /// <summary>The rings light while it's awake, plugged in.</summary>
    public bool AllyLightAwakePlugged { get; set; } = true;
    /// <summary>On battery, the rings go dark after a minute without input (and come back at the next touch).</summary>
    public bool AllyLightBatteryIdleOff { get; set; }
    /// <summary>The rings light while it sleeps.</summary>
    public bool AllyLightSleep { get; set; }
    /// <summary>The Xbox button's light (ROG Xbox Ally): "" (leave), "on" or "off".</summary>
    public string AllyXboxLight { get; set; } = "";
    /// <summary>Its brightness, 1 (dim) to 3 (ASUS's default).</summary>
    public int AllyXboxLightLevel { get; set; } = 3;
    /// <summary>It breathes instead of staying steady.</summary>
    public bool AllyXboxLightBreathe { get; set; }
    /// <summary>It goes dark while a game runs.</summary>
    public bool AllyXboxLightOffPlaying { get; set; }
    /// <summary>It breathes when the battery is low (15% or less, unplugged).</summary>
    public bool AllyXboxLightLowBattery { get; set; } = true;
    /// <summary>ASUS operating mode: "" (leave), silent, performance or turbo.</summary>
    public string AllyAsusMode { get; set; } = "";
    /// <summary>Fan curve: "" (leave), quiet, balanced, cool or max.</summary>
    public string AllyFan { get; set; } = "";
    /// <summary>Stop charging at this percentage; 0 = leave as it is.</summary>
    public int AllyChargeLimit { get; set; }
    /// <summary>M1 / M2 send F18 / F17, so they can be given actions in ROG Ally buttons.</summary>
    public bool AllyBackKeys { get; set; }
    /// <summary>The built-in controller's rumble strength, in its firmware (percent; ASUS's default is 100).</summary>
    public int AllyVibration { get; set; } = 100;
    /// <summary>The sticks' dead zone (inner) and where they reach full (outer), in the controller's firmware, percent.</summary>
    public int AllyStickInner { get; set; }
    public int AllyStickOuter { get; set; } = 100;
    /// <summary>The triggers' dead zone (inner) and where they reach full (outer), percent.</summary>
    public int AllyTriggerInner { get; set; }
    public int AllyTriggerOuter { get; set; } = 100;
    // 9.0.13: the left and right sides on their own. The values above are the left stick, the left trigger and the left
    // motor; these are the right ones (-1: the same as the left, as before 9.0.13).
    public int AllyVibrationR { get; set; } = -1;
    public int AllyStickInnerR { get; set; } = -1;
    public int AllyStickOuterR { get; set; } = -1;
    public int AllyTriggerInnerR { get; set; } = -1;
    public int AllyTriggerOuterR { get; set; } = -1;
    /// <summary>The sticks' anti-deadzone (0 to 32%): the smallest movement past the dead zone a game sees, for games with dead zones of their own.</summary>
    public int AllyAdzL { get; set; }
    public int AllyAdzR { get; set; }
    /// <summary>Each stick's response curve: four points of "movement, response" in percent, comma-separated ("" is straight).</summary>
    public string AllyCurveL { get; set; } = "";
    public string AllyCurveR { get; set; } = "";
    /// <summary>9.0.13: gyro aiming. "off", or "mouse" (tilting the Ally moves the mouse, for games that aim with a mouse).</summary>
    public string GyroMode { get; set; } = "off";
    public int GyroSensitivity { get; set; } = 100;
    public int GyroDeadzone { get; set; }
    /// <summary>When the gyro aims: "always", or while a button is held ("lb", "rb", "lt", "rt", "ls", "rs", "lbrb").</summary>
    public string GyroButton { get; set; } = "always";
    public bool GyroInvertX { get; set; }
    public bool GyroInvertY { get; set; }
    /// <summary>Turning the Ally (yaw) or tilting it sideways (roll) moves the aim left and right.</summary>
    public string GyroHorizontal { get; set; } = "yaw";
    public bool GyroOnlyInGames { get; set; } = true;
    /// <summary>The gyro's resting reading, measured by Calibration > Gyro (degrees per second), taken off every reading.</summary>
    public double GyroBiasX { get; set; }
    public double GyroBiasY { get; set; }
    public double GyroBiasZ { get; set; }
    /// <summary>9.0.13 OLED care: SquareDeck's picture moves by a few pixels now and then, so nothing stays in the same place.</summary>
    public bool OledPixelShift { get; set; }
    /// <summary>OLED care: after this many idle minutes on SquareDeck (0 off), a pixel refresh plays until you press something.</summary>
    public int OledRefreshMinutes { get; set; }

    /// <summary>9.0.18: how the user manual was last turned: 0 as usual, 90 turned right, 270 turned left.</summary>
    public int ManualRotation { get; set; }
    /// <summary>The one-time note that SquareDeck is made for the ROG Ally family was shown (on any other PC, 8.5.81).</summary>
    public bool NotAllyNoticeShown { get; set; }
    /// <summary>"My Device" on the home screen's buttons (handhelds only).</summary>
    public bool ShowMyDeviceButton { get; set; }
    /// <summary>Settings > Experimental: Adrenaline Hook (AMD graphics only).</summary>
    public bool ExperimentAdrenalineHook { get; set; }
    /// <summary>Experimental features (Settings > Experimental) that are turned on, by id (see Labs.Features).</summary>
    public List<string> Labs { get; set; } = new();
    /// <summary>Choices and remembered state for the experimental features (style schedule, dead zone, celebrated milestones...).</summary>
    public Dictionary<string, string> LabValues { get; set; } = new();
    /// <summary>Adrenaline Hook: add new games from the library to AMD Software by themselves.</summary>
    public bool AdrenalineAutoHook { get; set; }

    /// <summary>The update screen's moving background (an id from SilentUpdate.Looks; "random" = a different one each time).</summary>
    public string UpdaterLook { get; set; } = "random";
    /// <summary>The update screen's vibration style (an id from SilentUpdate.Haptics).</summary>
    public string UpdaterHaptic { get; set; } = "random";
    /// <summary>How TDP is set: "" (RyzenAdj helper) or "asus" (the Ally's firmware, no setup needed).</summary>
    public string TdpMethod { get; set; } = "";

    /// <summary>Open Captures showing one album per game.</summary>
    public bool CaptureAlbums { get; set; }
    /// <summary>How many captures fit across the Captures page (3 to 6).</summary>
    public int CapturesPerRow { get; set; } = 3;
    public bool CaptureAlbumIcons { get; set; } = true; // the game's picture next to each album's name
    public bool RumbleOnNotifications { get; set; } = true; // a short buzz when a notification pops up
    public bool RumbleOnButtons { get; set; } = true;       // A taps, B thuds, on every page
    public bool RumbleInSettings { get; set; } = true;      // a light tick moving from row to row in Settings
    public bool PerformanceBoost { get; set; }              // free memory before a game starts, with an animation
    public string BoostAnimation { get; set; } = "switch2"; // switch2, rings, warp, gauge, orbit, shockwave… or random
    public bool BoostRumble { get; set; } = true;           // the animation's vibration pattern

    /// <summary>Store apps hidden from the Stores page (by name).</summary>
    public List<string> HiddenStores { get; set; } = new();

    /// <summary>Hand the graphics card back to the game: draw the launcher in software while a game runs.</summary>
    public bool FreeGpuDuringGames { get; set; } = true;
    public bool LeanInBackground { get; set; } = true;      // the same after a minute out of sight behind another app or minimized
    public bool LeanWhilePlaying { get; set; } = true;      // let go of covers, pages and caches while a game runs in front
    public bool ClosePagesWhilePlaying { get; set; } = true; // part of the above: open pages close (you come back to Home)

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
    /// <summary>Accessibility: a thicker highlight ring around the selected item.</summary>
    public bool ThickFocusRing { get; set; }
    /// <summary>Game Genie writes answers for newly added games in the background (GeniePrep).</summary>
    public bool GeniePrepare { get; set; } = true;
    /// <summary>Accessibility: holding a direction repeats "normal", "slow" or "off".</summary>
    public string NavRepeat { get; set; } = "normal";
    /// <summary>Accessibility: how long pop-up notifications stay, in seconds (0 = the usual 4.5).</summary>
    public int ToastSeconds { get; set; }
    /// <summary>Accessibility: interface sounds also flash the screen's edge (for people who can't hear them).</summary>
    public bool VisualSounds { get; set; }

    /// <summary>Home screen buttons grow when highlighted and spring when pressed.</summary>
    public bool ButtonAnimations { get; set; } = true;
    public bool PunchIn { get; set; } = true;               // a game or app punches in as it's opened
    public string LaunchAnimation { get; set; } = "classic"; // the splash while a game starts: classic, spotlight, flip, ripples, ring, cinema or random

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

    /// <summary>Wash the game details page in a soft gradient of the cover's colors.</summary>
    public bool DetailsGradient { get; set; } = true;

    /// <summary>Switch style: the game's hero artwork, blurred, behind the card at the top of the details page (instead of the plain card color).</summary>
    public bool DetailsCardHero { get; set; }

    /// <summary>Show the game's logo artwork instead of its name at the top of the details page.</summary>
    public bool DetailsLogo { get; set; } = true;

    /// <summary>Play SteamGridDB's animated cover on the highlighted tile, when one exists.</summary>
    public bool AnimatedCovers { get; set; } = true;

    /// <summary>Chime and short animation when the launcher opens.</summary>
    public bool StartupIntro { get; set; } = true;

    /// <summary>The user's own Steam Web API key, for the Friends page. Empty until they add one.</summary>
    public string SteamWebApiKey { get; set; } = "";
    public string GeminiApiKey { get; set; } = "";          // Game Genie: your own Google Gemini key (empty: the one built into this copy)
    /// <summary>A notification when a wishlist or watched game goes on sale (at least <see cref="SaleAlertMin"/>% off).</summary>
    public bool SaleAlerts { get; set; } = true;
    public int SaleAlertMin { get; set; } = 1;
    /// <summary>A notification on the day a wishlist game comes out.</summary>
    public bool ReleaseAlerts { get; set; } = true;

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
                    s.Labs ??= new();
                    s.LabValues ??= new();
                    s.Wallpaper ??= "";
                    s.ThemePalette ??= "";
                    // Steel became Switch's standard color theme: given once to anyone on Switch with no color theme.
                    if (!s.SteelDefault)
                    {
                        if (s.ThemePalette.Length == 0 && (string.IsNullOrEmpty(s.UiStyle) || s.UiStyle == "switch")) s.ThemePalette = "steel";
                        s.SteelDefault = true;
                    }
                    // 7.3.17: the Switch 2 gradient became Switch's standard highlight; given once to anyone on Switch still on the plain ring.
                    if (!s.SwitchRingDefault)
                    {
                        if ((string.IsNullOrEmpty(s.UiStyle) || s.UiStyle == "switch") && string.IsNullOrEmpty(s.HighlightEffect)
                            && (string.IsNullOrEmpty(s.HighlightColors) || s.HighlightColors == "palette:accent"))
                        {
                            s.HighlightEffect = HighlightEffects.SwitchEffect;
                            s.HighlightColors = HighlightEffects.SwitchColors;
                        }
                        s.SwitchRingDefault = true;
                    }
                    s.LaunchChime ??= "rise";
                    s.BackupFolder ??= "";
                    s.BootVideo ??= "";
                    s.BootAnimation ??= "logo";
                    s.SummonCombo ??= "default";
                    s.SummonKey ??= "none";
                    s.MainDisplay ??= "";
                    s.HiddenStores ??= new();
                    s.ButtonAnimationStyle ??= "grow";
                    s.PageTransition ??= "slide";
                    s.TileHighlight ??= "pulse";
                    if (s.LaunchCountdownSeconds is < 1 or > 30) s.LaunchCountdownSeconds = 3;
                    s.LaunchCountdownLook ??= "s2";
                    if (!s.HomeButtons8515)
                    {
                        s.ShowMusicButton = s.ShowBrowserButton = s.ShowExplorerButton = true;
                        s.ShowFriendsButton = s.ShowNewsButton = s.ShowMyDeviceButton = false;
                        s.HomeButtons8515 = true;
                    }
                    if (!s.HomeButtons913)
                    {
                        // 9.0.13: with My Device gone from the row, every button under the games is on (each can still be hidden in Settings).
                        s.ShowMusicButton = s.ShowBrowserButton = s.ShowExplorerButton = s.ShowFriendsButton = s.ShowNewsButton = true;
                        s.HomeButtons913 = true;
                    }
                    if (!s.TdpBoost8542)
                    {
                        s.TdpBoostWindow = "short"; // short bursts above the limit, as ASUS's own modes allow: no more sluggish menus
                        s.TdpBoost8542 = true;
                    }
                    if (!s.BrowserKeep8539)
                    {
                        s.BrowserKeepOpen = false; // pages you've left don't stay loaded in the background
                        s.BrowserKeep8539 = true;
                    }
                    if (!s.PlayerStats913)
                    {
                        s.ShowPlayerStats = true; // 9.0.13: on for everyone once; the switch stays in Settings > Library and stores
                        s.PlayerStats913 = true;
                    }
                    if (!s.NavSwitch2_903)
                    {
                        // 9.0.3: the Switch style moves around with the Switch 2 sounds (moving the same as the other sounds before).
                        if (s.UiStyle == "switch" && string.IsNullOrEmpty(s.NavSoundSet)) s.NavSoundSet = "pack:Switch 2 Sounds";
                        s.NavSwitch2_903 = true;
                    }
                    if (!s.CloudIntro8533)
                    {
                        if (s.CloudIntro is null or "random") s.CloudIntro = "switch2"; // the Switch 2 animation is the new default
                        s.CloudIntro8533 = true;
                    }
                    if (!s.HeroOff8529)
                    {
                        if (s.UiStyle == "switch") s.HeroBackgrounds = false;
                        s.HeroOff8529 = true;
                    }
                    if (!s.PageCycle8516)
                    {
                        s.PagesOff = new List<string> { "library", "captures", "music", "friends", "settings" };
                        s.PageCycle8516 = true;
                    }
                    s.DisabledStores ??= new();
                    s.ExplorerPins ??= new();
                    s.FtpServers ??= new();
                    s.ExplorerView ??= "grid";
                    s.ExplorerSort ??= "name";
                    s.TaskManagerTab ??= "Apps";
                    s.DynamicBackground ??= "";
                    s.RecentSearches ??= new();
                    s.PerformanceMode ??= "off";
                    s.WindowsPowerMode ??= "";
                    s.BrowserFavorites ??= new();
                    // 6.4: the old UI styles were removed (theming is being rebuilt; the looks have their own styles now).
                    // Someone on a removed style is moved to Switch once; when their style had brought its own colors, wallpaper, sounds, music,
                    // moving background and button pictures (style matching), those go back to Switch's too.
                    if (!string.IsNullOrEmpty(s.UiStyle) && !MyStyles.IsCustom(s.UiStyle) && !UiStyles.IsAdvanced(s.UiStyle) && UiStyles.All.All(x => x.Id != s.UiStyle))
                    {
                        if (s.StyleMatching)
                        {
                            var basic = UiStyles.All[0];
                            s.AccentColor = "";
                            s.ThemePalette = "steel";
                            s.Wallpaper = basic.Wallpaper;
                            s.SoundSet = basic.Sounds;
                            s.MenuMusic = basic.Music;
                            s.DynamicBackground = basic.Moving;
                            if (s.GlyphLook == "pixel") s.GlyphLook = "text";
                        }
                        s.UiStyle = "switch";
                    }
                    s.UiStyle = UiStyles.Upgrade(s.UiStyle);
                    s.LookId = Looks.Find(s.LookId).Id; // a look that's gone (or none yet) becomes Switch
                    s.NavButtonStyle = s.FooterStyle = s.DockStyle = "style"; // 6.4.1: these choices are gone
                    s.TaskManagerSort ??= "memory";
                    s.TouchKeyboard ??= "windows";
                    if (!s.KeyboardChosen)
                    {
                        s.TouchKeyboard = "windows";
                        s.KeyboardChosen = true;
                    }
                    s.AllyBindings ??= AllyButtons.DefaultBindings();
                    foreach (var (k, v) in AllyButtons.DefaultBindings()) s.AllyBindings.TryAdd(k, v);
                    // 7.3.25: the quick menu became the Game Bar widget.
                    foreach (var k in s.AllyBindings.Where(b => b.Value == "quickmenu").Select(b => b.Key).ToList()) s.AllyBindings[k] = "widget";
                    s.WidgetTheme ??= "squaredeck";
                    s.WidgetSections ??= new List<string>(WidgetSectionIds);
                    if (!s.WidgetCards917)
                    {
                        foreach (var id in new[] { "launch", "favorites" }) if (!s.WidgetSections.Contains(id)) s.WidgetSections.Add(id);
                        s.WidgetCards917 = true;
                    }
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
                    if (!s.Rumble917)
                    {
                        // 9.0.17: the controller's little buzzes in the interface are on by default (Settings > Controller turns them off).
                        if (s.RumbleStrength <= 0) s.RumbleStrength = 60;
                        s.RumbleOnButtons = true;
                        s.Rumble917 = true;
                    }
                    s.LaunchChimeFile ??= "";
                    s.SoundSet ??= "classic";
                    s.SoundFolder ??= "";
                    if (!Hints.IsValidMap(s.ButtonMap)) s.ButtonMap = "ABXY";
                    s.NavSoundSet ??= "";
                    s.SkinVariants ??= new();
                    s.UnlockedStyles ??= new();
                    s.CustomStyles ??= new();
                    s.CustomStyles.RemoveAll(c => c == null);
                    MyStyles.Load(s);
                    // A style of yours that's gone (deleted, or a settings file copied without it) becomes Switch.
                    if (MyStyles.IsCustom(s.UiStyle) && !UiStyles.Exists(s.UiStyle)) s.UiStyle = "switch";
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt settings file: fall back to defaults.
        }
        return new Settings { ThemeMode = "system", SetupDone = false, RumbleStrength = 60, Rumble917 = true };
    }

    public void Save()
    {
        try
        {
            // Gathered with the saves of the next moment and written on a background thread (9.0.1).
            SafeFile.WriteSoon(FilePath, () => JsonSerializer.Serialize(this, SafeFile.Indented));
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
        s.UiStyle = UiStyles.Find(UiStyle).Id; s.ThemeMode = ThemeMode; // a theme saved with a removed style uses Switch s.ThemePalette = ThemePalette; s.AccentColor = AccentColor;
        s.Wallpaper = Wallpaper; s.DynamicBackground = DynamicBackground; s.DynamicBlur = DynamicBlur; s.TileCorner = TileCorner;
        s.SoundSet = SoundSet; s.SoundFolder = SoundFolder; s.MenuMusic = MenuMusic; s.GlyphLook = GlyphLook; s.HeroBackgrounds = HeroBackgrounds;
    }
}
