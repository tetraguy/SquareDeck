using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck.Services;

public static class Battery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>True while the PC is on mains power.</summary>
    public static bool IsPluggedIn() => GetSystemPowerStatus(out var s) && s.ACLineStatus == 1;

    /// <summary>
    /// The Segoe MDL2 battery icon for a level: eleven steps from empty to full, with a second set
    /// showing the charging bolt while plugged in.
    /// </summary>
    public static string Glyph(int percent, bool pluggedIn)
    {
        int step = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        if (pluggedIn) return step == 10 ? "\uEA93" : ((char)(0xE85A + step)).ToString();
        return step == 10 ? "\uE83F" : ((char)(0xE850 + step)).ToString();
    }

    /// <summary>Windows' own estimate of the time left on battery; null while plugged in or still being worked out.</summary>
    public static TimeSpan? GetTimeRemaining()
    {
        if (!GetSystemPowerStatus(out var s) || s.ACLineStatus == 1) return null;
        return s.BatteryLifeTime is 0 or uint.MaxValue ? null : TimeSpan.FromSeconds(s.BatteryLifeTime);
    }

    /// <summary>True when Windows' battery saver is switched on.</summary>
    public static bool SaverOn() => GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1;

    /// <summary>Battery percentage, or null on a desktop with no battery.</summary>
    public static int? GetPercent()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryLifePercent > 100) return null; // no battery / unknown
        return s.BatteryLifePercent;
    }
}

/// <summary>Lock, sleep and shut down the PC.</summary>
public static class Power
{
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);

    public static void Lock() => LockWorkStation();

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void Hibernate() => SetSuspendState(true, false, false);

    public static void ShutDown() => RunShutdown("/s /t 0");

    public static void Restart() => RunShutdown("/r /t 0");

    /// <summary>
    /// Restarts straight into the PC's firmware (UEFI / BIOS) setup. Windows only allows this for an
    /// administrator, so it asks for permission first. False if that was declined or couldn't start.
    /// </summary>
    public static bool RestartToFirmware()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /fw /t 0")
            {
                UseShellExecute = true, // needed for the administrator prompt
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
            if (p == null) return false;
            p.WaitForExit(8000);
            return !p.HasExited || p.ExitCode == 0;
        }
        catch (Exception)
        {
            return false; // the permission prompt was declined
        }
    }

    private static void RunShutdown(string arguments)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>
/// Icons and thumbnails from the Windows shell: works for files (video thumbnails) and for
/// installed apps addressed as shell:AppsFolder\{app id}, including Microsoft Store apps.
/// </summary>
public static class ShellThumb
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr hbitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbitmap, uint start, uint lines, byte[] bits,
        ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    public static BitmapSource? ForApp(string appId, int size = 256) => Get(@"shell:AppsFolder\" + appId, size);

    /// <summary>Thumbnail (or icon) for a shell item. Safe to call from a background thread.</summary>
    public static BitmapSource? Get(string parsingName, int size)
    {
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null)
                return null;

            const int BiggerSizeOk = 0x1;
            int hr = factory.GetImage(new NativeSize { Width = size, Height = size }, BiggerSizeOk, out hbitmap);
            Marshal.ReleaseComObject(factory);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;

            if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var bm) == 0 || bm.Width <= 0 || bm.Height <= 0)
                return null;

            // Copy the pixels out top-down as 32-bit BGRA so transparency survives.
            var info = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bm.Width,
                Height = -bm.Height,
                Planes = 1,
                BitCount = 32
            };
            var pixels = new byte[bm.Width * bm.Height * 4];
            var hdc = GetDC(IntPtr.Zero);
            int copied = GetDIBits(hdc, hbitmap, 0, (uint)bm.Height, pixels, ref info, 0);
            ReleaseDC(IntPtr.Zero, hdc);
            if (copied == 0) return null;

            // Thumbnails of photos/videos often carry no alpha at all (all zero): treat as opaque.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }

            var source = BitmapSource.Create(bm.Width, bm.Height, 96, 96,
                hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, bm.Width * 4);
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
        }
    }
}
