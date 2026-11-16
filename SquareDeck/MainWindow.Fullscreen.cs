using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SquareDeck;

/// <summary>
/// Keeps the launcher covering the whole screen. When the Windows touch keyboard docks, Windows shrinks the space it
/// gives maximized windows, so the launcher used to squash itself (and the whole UI scaled down) every time the
/// keyboard appeared. Now the keyboard just floats over the bottom of the screen and the launcher stays put.
/// </summary>
public partial class MainWindow
{
    private const int WM_GETMINMAXINFO = 0x0024, WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr Hwnd, After; public int X, Y, Cx, Cy; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

    private void InitFullscreenGuard()
    {
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(FullscreenHook);
        };
    }

    private bool Fullscreen => WindowStyle == WindowStyle.None && WindowState == WindowState.Maximized;

    private static bool MonitorRect(IntPtr hwnd, out RECT monitor)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        bool ok = GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        monitor = info.Monitor;
        return ok;
    }

    private IntPtr FullscreenHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == WM_GETMINMAXINFO && WindowStyle == WindowStyle.None && MonitorRect(hwnd, out var m))
            {
                // "Maximized" means the whole monitor, not the space left over by the taskbar or a docked keyboard.
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.MaxPosition = new POINT { X = 0, Y = 0 };
                info.MaxSize = new POINT { X = m.Right - m.Left, Y = m.Bottom - m.Top };
                info.MaxTrackSize = info.MaxSize;
                Marshal.StructureToPtr(info, lParam, false);
                handled = true;
            }
            else if (msg == WM_WINDOWPOSCHANGING && Fullscreen && !IsIconic(hwnd) && MonitorRect(hwnd, out var r))
            {
                // Anything that tries to shrink or move the full-screen launcher is put back.
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.Flags & SWP_NOSIZE) == 0 || (pos.Flags & SWP_NOMOVE) == 0)
                {
                    pos.X = r.Left;
                    pos.Y = r.Top;
                    pos.Cx = r.Right - r.Left;
                    pos.Cy = r.Bottom - r.Top;
                    pos.Flags &= ~(SWP_NOSIZE | SWP_NOMOVE);
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
        }
        catch (Exception) { }
        return IntPtr.Zero;
    }
}
