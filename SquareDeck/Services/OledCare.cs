using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// 9.0.13: OLED care, as Armoury Crate's Display > OLED Settings offers it: the Windows settings that keep still things
/// off an OLED screen (hide the taskbar until it's needed, no see-through effects, dark mode). SquareDeck's own pixel
/// shift and pixel refresh are in the main window (Settings.OledPixelShift, Settings.OledRefreshMinutes).
/// </summary>
public static class OledCare
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint Message, Edge;
        public int Left, Top, Right, Bottom;
        public IntPtr Param;
    }

    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    private const uint GetState = 4, SetState = 10;
    private const int AutoHide = 1;

    /// <summary>Windows hides the taskbar until the pointer (or a swipe) brings it up.</summary>
    public static bool TaskbarAutoHide
    {
        get
        {
            try
            {
                var d = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
                return ((long)SHAppBarMessage(GetState, ref d) & AutoHide) != 0;
            }
            catch (Exception) { return false; }
        }
        set
        {
            try
            {
                var d = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Param = (IntPtr)(value ? AutoHide : 0) };
                SHAppBarMessage(SetState, ref d);
            }
            catch (Exception) { }
        }
    }

    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static int? Read(string name)
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(Personalize); return key?.GetValue(name) as int?; }
        catch (Exception) { return null; }
    }

    private static void Write(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Personalize);
            key.SetValue(name, value, RegistryValueKind.DWord);
            // Tells open windows (and the taskbar) the colors changed, so it shows without signing out.
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 500, out _);
        }
        catch (Exception) { }
    }

    /// <summary>Windows' see-through effects (on by default).</summary>
    public static bool Transparency { get => Read("EnableTransparency") != 0; set => Write("EnableTransparency", value ? 1 : 0); }

    /// <summary>Windows and its apps in dark mode.</summary>
    public static bool DarkMode
    {
        get => Read("AppsUseLightTheme") == 0 && Read("SystemUsesLightTheme") == 0;
        set { Write("AppsUseLightTheme", value ? 0 : 1); Write("SystemUsesLightTheme", value ? 0 : 1); }
    }
}
