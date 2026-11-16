using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// A handful of Windows settings worth having a controller-friendly switch for (Settings > System > Windows),
/// all per-user, so they don't need administrator rights.
/// </summary>
public static class WindowsTweaks
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string StickyKeys = @"Control Panel\Accessibility\StickyKeys";

    private static int Get(string key, string name, int fallback)
    {
        try { return Registry.CurrentUser.OpenSubKey(key)?.GetValue(name) is int v ? v : fallback; }
        catch (Exception) { return fallback; }
    }

    private static void Set(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Windows itself (apps and the taskbar) in dark mode.</summary>
    public static bool DarkMode
    {
        get => Get(Personalize, "AppsUseLightTheme", 1) == 0;
        set
        {
            Set(Personalize, "AppsUseLightTheme", value ? 0 : 1);
            Set(Personalize, "SystemUsesLightTheme", value ? 0 : 1);
            Broadcast("ImmersiveColorSet");
        }
    }

    /// <summary>See-through effects in Windows' own menus and taskbar (costs a little battery).</summary>
    public static bool Transparency
    {
        get => Get(Personalize, "EnableTransparency", 1) == 1;
        set { Set(Personalize, "EnableTransparency", value ? 1 : 0); Broadcast("ImmersiveColorSet"); }
    }

    /// <summary>Windows' Game Mode: games get priority, Windows Update stays quiet while you play.</summary>
    public static bool GameMode
    {
        get => Get(GameBar, "AutoGameModeEnabled", 1) == 1;
        set => Set(GameBar, "AutoGameModeEnabled", value ? 1 : 0);
    }

    /// <summary>Recording the last few minutes of play in the background (Game Bar). Off saves battery and performance.</summary>
    public static bool BackgroundRecording
    {
        get => Get(GameDvr, "HistoricalCaptureEnabled", 0) == 1;
        set => Set(GameDvr, "HistoricalCaptureEnabled", value ? 1 : 0);
    }

    /// <summary>Game Bar opening with the controller's Xbox button.</summary>
    public static bool GameBarOnGuide
    {
        get => Get(GameBar, "UseNexusForGameBarEnabled", 1) == 1;
        set => Set(GameBar, "UseNexusForGameBarEnabled", value ? 1 : 0);
    }

    /// <summary>
    /// The "Turn on Sticky Keys?" pop-up after pressing Shift five times, which handheld buttons mapped to Shift
    /// set off by accident.
    /// </summary>
    public static bool StickyKeysShortcut
    {
        get
        {
            try { return int.TryParse(Registry.CurrentUser.OpenSubKey(StickyKeys)?.GetValue("Flags") as string, out int f) && (f & 4) != 0; }
            catch (Exception) { return true; }
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(StickyKeys);
            int flags = int.TryParse(k.GetValue("Flags") as string, out int f) ? f : 510;
            flags = value ? flags | 4 : flags & ~4;
            k.SetValue("Flags", flags.ToString(), RegistryValueKind.String);
        }
    }

    /// <summary>Windows Settings pages that are handy from a handheld.</summary>
    public static readonly (string Name, string Uri, string Glyph, string About)[] Pages =
    {
        ("Windows Update", "ms-settings:windowsupdate", "", "Updates and restarts"),
        ("Night light", "ms-settings:nightlight", "", "Warmer colors in the evening"),
        ("Graphics (HDR, variable refresh rate, GPU per app)", "ms-settings:display-advancedgraphics", "", "Auto HDR, VRR and which GPU each app uses"),
        ("Battery saver", "ms-settings:batterysaver", "", "Turns on by itself at a battery level you choose"),
        ("Do not disturb and notifications", "ms-settings:notifications", "", "Quiet Windows' own pop-ups"),
        ("Airplane mode", "ms-settings:network-airplanemode", "", "All wireless off"),
        ("Storage and Storage Sense", "ms-settings:storagesense", "", "Free up space for games"),
        ("Startup apps", "ms-settings:startupapps", "", "Fewer apps at sign-in means a faster start"),
        ("Touch keyboard and typing", "ms-settings:typing", "", "Size and behaviour of the touch keyboard"),
        ("Mouse and touchpad", "ms-settings:mousetouchpad", "", "Pointer speed and scrolling")
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>Tells open windows a setting changed (so dark mode applies straight away).</summary>
    private static void Broadcast(string what)
    {
        try { SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, what, 0x0002, 1000, out _); }
        catch (Exception) { }
    }
}
