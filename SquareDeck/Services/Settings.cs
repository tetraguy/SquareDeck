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

    /// <summary>Come back to the front when a game closes.</summary>
    public bool ReturnToLauncher { get; set; } = true;

    /// <summary>Guide button (or View+Menu together) brings the launcher to the front from anywhere.</summary>
    public bool GuideSummon { get; set; } = true;

    /// <summary>Show games from Epic, GOG, Xbox, Battle.net and Steam's non-Steam shortcuts.</summary>
    public bool OtherStores { get; set; } = true;

    /// <summary>Home background: empty for none, "preset:N" for a built-in one, or an image path.</summary>
    public string Wallpaper { get; set; } = "";

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

    /// <summary>Music apps removed from the "Open player" list (by app id).</summary>
    public List<string> HiddenPlayers { get; set; } = new();

    /// <summary>Background music on the home screen: empty for none, or "pack:&lt;folder&gt;" for a music sound pack.</summary>
    public string MenuMusic { get; set; } = "";

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
    public bool ShowFriendsButton { get; set; } = true;
    public bool ShowExplorerButton { get; set; } = true;

    /// <summary>Built-in web browser: start page, search engine ("google", "bing", "duckduckgo") and zoom in percent.</summary>
    public string BrowserHome { get; set; } = "https://www.google.com";
    public string BrowserSearch { get; set; } = "google";
    public int BrowserZoom { get; set; } = 125;

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

    /// <summary>Spotify sign-in, encrypted for this Windows account (see LocalSecret).</summary>
    public string SpotifyToken { get; set; } = "";

    /// <summary>Device name of the display the launcher opens on ("" = wherever Windows puts it).</summary>
    public string MainDisplay { get; set; } = "";

    /// <summary>Show other apps' Windows notifications in the launcher too.</summary>
    public bool WindowsNotifications { get; set; } = true;

    /// <summary>Fewer moving parts: no pulsing highlight, slides, fades or intro animation.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Size of text and controls on pages, in percent (100, 115, 130 or 150).</summary>
    public int TextScale { get; set; } = 100;

    /// <summary>Fade-out animation when leaving with "Exit to desktop".</summary>
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
                    s.LaunchChime ??= "rise";
                    s.BackupFolder ??= "";
                    s.BootVideo ??= "";
                    s.BootAnimation ??= "logo";
                    s.SummonCombo ??= "default";
                    s.SummonKey ??= "none";
                    s.OverlaySide ??= "right";
                    s.MainDisplay ??= "";
                    s.HiddenStores ??= new();
                    s.AllyBindings ??= AllyButtons.DefaultBindings();
                    foreach (var (k, v) in AllyButtons.DefaultBindings()) s.AllyBindings.TryAdd(k, v);
                    s.SpotifyClientId ??= "";
                    s.MusicService ??= "youtube";
                    s.SpotifyToken ??= "";
                    if (s.TextScale < 100 || s.TextScale > 200) s.TextScale = 100;
                    if (s.SoundVolume < 10 || s.SoundVolume > 100) s.SoundVolume = 100;
                    s.GlyphStyle ??= "xbox";
                    s.HiddenPlayers ??= new();
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
