using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// Windows' own touch keyboard (TabTip), for typing in SquareDeck's text boxes instead of its built-in
/// controller keyboard (Settings > Controller and quick menu > Typing).
/// </summary>
public static class TouchKeyboard
{
    /// <summary>Set from Settings: use the Windows touch keyboard instead of the built-in one.</summary>
    public static bool UseWindows { get; set; }

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch { }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr window);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    private static string TabTipPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");

    /// <summary>True while the touch keyboard is on screen.</summary>
    public static bool IsOpen
    {
        get
        {
            var window = FindWindow("IPTip_Main_Window", null);
            if (window == IntPtr.Zero) return false;
            const int Style = -16, Visible = 0x10000000, Disabled = 0x08000000;
            int style = GetWindowLong(window, Style);
            return (style & Visible) != 0 && (style & Disabled) == 0;
        }
    }

    /// <summary>Opens the touch keyboard (starting its process first if Windows hasn't).</summary>
    public static async Task ShowAsync()
    {
        try
        {
            if (IsOpen) return;
            if (Process.GetProcessesByName("TabTip").Length == 0 && File.Exists(TabTipPath))
            {
                Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
                await Task.Delay(500);
                if (IsOpen) return;
            }
            var tip = (ITipInvocation)new UIHostNoLaunch();
            tip.Toggle(GetDesktopWindow());
            Marshal.ReleaseComObject(tip);
        }
        catch (Exception)
        {
            // Older Windows: starting TabTip shows the keyboard by itself.
            try { if (File.Exists(TabTipPath)) Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }
}
