// Generated from SettingsScreens.cs: which settings each part and group of Settings shows (the properties its
// rows read and write). Regenerate after adding settings rows (see SettingsSections.cs).
namespace SquareDeck.Services;

public static partial class SettingsSections
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> Map = new()
    {
        ["access"] = new()
        {
            ["Controls and timing"] = new[] { "NavRepeat", "ToastSeconds" },
            ["Hearing"] = new[] { "VisualSounds" },
            ["Motion"] = new[] { "ReduceMotion" },
            ["Seeing"] = new[] { "DarkTheme", "TextScale", "ThemeMode", "ThickFocusRing" },
        },
        ["controller"] = new()
        {
            ["Buttons"] = new[] { "GlyphLook", "GlyphStyle" },
            ["Summon"] = new[] { "GuideSummon", "InGameQuickMenu" },
            ["Vibration and testing"] = new[] { "RumbleOnButtons", "RumbleInSettings", "RumbleOnNotifications" },
            ["Xbox Game Bar widget"] = new[] { "GameBarWidget", "WidgetCloseGame" },
        },
        ["data"] = new()
        {
            ["Updates"] = new[] { "AutoUpdateCheck", "UpdaterHaptic", "UpdaterLook", "WhatsNewAfterUpdate" },
        },
        ["games"] = new()
        {
            ["Achievements, play time and captures"] = new[] { "AchievementToasts", "CaptureAlbumIcons", "CaptureAlbums" },
            ["Cloud gaming"] = new[] { "CloudCorner", "CloudIntro", "CloudLeaveButtons", "CloudLeaveHold", "CloudServices" },
            ["Launching games"] = new[] { "AskAboutUpdates", "LaunchCountdown", "LaunchCountdownSeconds", "LaunchCountdownLook", "LaunchSplash", "SteamDirectLaunch" },
            ["Library and stores"] = new[] { "DisabledStores", "LibraryView", "OtherStores", "ReleaseAlerts", "SaleAlertMin", "SaleAlerts", "ShowHowLongToBeat", "ShowPlayerStats" },
            ["Service keys"] = new[] { "GeniePrepare" },
            ["While playing"] = new[] { "ClosePagesWhilePlaying", "DoNotDisturb", "FreeGpuDuringGames", "LeanInBackground", "LeanWhilePlaying", "ReturnToLauncher", "SessionSummary" },
        },
        ["handheld"] = new()
        {
            [""] = new[] { "CpuBoost", "CpuCores", "CpuEpp", "MaxCpu", "RyzenProfile", "TdpBoostWindow", "TdpEnabled", "TdpPluggedWatts", "TdpReapply", "TdpTempLimit", "TdpWatts", "WindowsPowerMode" },
            ["ASUS software"] = new[] { "HandheldChargeLimit", "HandheldFanBoost", "HandheldPowerMode" },
            ["Advanced"] = new[] { "TdpEnabled", "TdpReapply" },
            ["Battery care"] = new[] { "AllyChargeLimit" },
            ["Controller"] = new[] { "AllyStickInner", "AllyStickOuter", "AllyTriggerInner", "AllyTriggerOuter", "AllyVibration" },
            ["Fans and temperature"] = new[] { "AllyFan" },
            ["Lights"] = new[] { "AllyGameLights", "AllyLightColor", "AllyLightColor2", "AllyLightPercent", "AllyLightSpeed", "AllyLightsOffPlaying", "AllyLowBatteryFlash" },
            ["This device"] = new[] { "ShowMyDeviceButton" },
            ["When the lights are on"] = new[] { "AllyLightAwakeBattery", "AllyLightAwakePlugged", "AllyLightBatteryIdleOff", "AllyLightBootShutdown", "AllyLightPowerSet", "AllyLightSleep" },
            ["Xbox button light"] = new[] { "AllyXboxLight", "AllyXboxLightBreathe", "AllyXboxLightLevel", "AllyXboxLightLowBattery", "AllyXboxLightOffPlaying" },
        },
        ["home"] = new()
        {
            ["Animations"] = new[] { "ButtonAnimationStyle", "ButtonAnimations", "PageTransition", "PunchIn", "TileHighlight" },
            ["Buttons under the games"] = new[] { "ShowBrowserButton", "ShowExplorerButton", "ShowFriendsButton", "ShowMusicButton", "ShowMyDeviceButton", "ShowNewsButton" },
            ["Home row and buttons"] = new[] { "ContinueTile", "TileArt", "TileSize" },
            ["Screen saver"] = new[] { "Screensaver", "ScreensaverMinutes" },
            ["Starting and closing"] = new[] { "ExitAnimation", "StartupIntro", "WelcomeMessage" },
        },
        ["look"] = new()
        {
            ["Artwork"] = new[] { "AnimatedCovers", "DetailsGradient", "DetailsCardHero", "HeroBackgrounds" },
            ["Colors"] = new[] { "DarkTheme", "GameAccent", "ThemeMode" },
            ["Display"] = new[] { "AspectRatio" },
            ["Font"] = new[] { "UiFont" },
            ["Top bar"] = new[] { "ShowBatteryPercent", "ShowClock", "ShowHeadphoneBattery" },
        },
        ["looks"] = new()
        {
            [""] = new[] { "StyleMatching" },
        },
        ["network"] = new()
        {
            ["Web browser"] = new[] { "BrowserHome", "BrowserKeepOpen", "BrowserMaxTabs", "BrowserSearch", "BrowserStickMouse", "BrowserZoom", "OpenLinksInSquareDeck", "ShowBrowserButton" },
        },
        ["power"] = new()
        {
            ["Performance Boost"] = new[] { "BoostRumble", "PerformanceBoost", "PerformanceMode" },
            ["Performance overlay"] = new[] { "CpuBoost", "CpuCores", "CpuEpp", "MaxCpu", "OsdDetail", "OsdEnabled", "OsdLayout", "OsdOpacity", "OsdPosition", "OsdSize", "OsdStyle", "OsdWhen", "RyzenProfile", "TdpBoostWindow", "TdpEnabled", "TdpPluggedWatts", "TdpReapply", "TdpTempLimit", "TdpWatts", "WindowsPowerMode" },
        },
        ["sound"] = new()
        {
            ["Background music"] = new[] { "MenuMusicPausesForMedia", "MenuMusicVolume", "RecompZoneMusic", "RetroLoungeMusic" },
            ["Multimedia page"] = new[] { "MusicServices", "SpotifyInLauncher", "StreamingIntro" },
        },
        ["system"] = new()
        {
            ["This PC"] = new[] { "LowBatteryAlert", "LowBatteryLevel" },
            ["Tools"] = new[] { "ExperimentAdrenalineHook" },
        },
    };
}
