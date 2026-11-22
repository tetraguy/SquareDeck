using System.IO;
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

    // A program added by hand is a file path: its own icon, straight from the file.
    public static BitmapSource? ForApp(string appId, int size = 256) =>
        Get(System.IO.Path.IsPathRooted(appId) ? appId : @"shell:AppsFolder\" + appId, size);

    /// <summary>
    /// The sharpest icon we can find for an installed app: a Microsoft Store app's own logo file at its largest
    /// size (256 px, without the tile plate) when there is one, else the shell's icon. Safe off the UI thread.
    /// </summary>
    public static BitmapSource? BestForApp(string appId)
    {
        if (System.IO.Path.IsPathRooted(appId)) return Get(appId, 256);
        var logo = StoreLogoPath(appId);
        if (logo != null && Services.ImageLoader.FromFile(logo, 256) is BitmapSource fromPackage)
        {
            if (!fromPackage.IsFrozen) fromPackage.Freeze();
            return fromPackage;
        }
        return ForApp(appId, 256);
    }

    // ---------------------------------------------------------------- Microsoft Store app logos

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr[]? fullNames,
        ref uint bufferLength, IntPtr buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint pathLength, System.Text.StringBuilder? path);

    /// <summary>Folder a Store package is installed in, from its family name (the part of the app id before "!").</summary>
    private static string? PackageFolder(string family)
    {
        try
        {
            uint count = 0, length = 0;
            GetPackagesByPackageFamily(family, ref count, null, ref length, IntPtr.Zero);
            if (count == 0 || length == 0) return null;
            var names = new IntPtr[count];
            var buffer = Marshal.AllocHGlobal((int)length * 2);
            try
            {
                if (GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) != 0) return null;
                // The newest version last, usually; any of them has the logos.
                for (int i = (int)count - 1; i >= 0; i--)
                {
                    var full = Marshal.PtrToStringUni(names[i]);
                    if (string.IsNullOrEmpty(full)) continue;
                    uint pathLength = 0;
                    GetPackagePathByFullName(full, ref pathLength, null);
                    if (pathLength == 0) continue;
                    var path = new System.Text.StringBuilder((int)pathLength);
                    if (GetPackagePathByFullName(full, ref pathLength, path) == 0 && Directory.Exists(path.ToString()))
                        return path.ToString();
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// The biggest logo file a Store app ships: its Square44x44Logo at "targetsize-256_altform-unplated" when it has
    /// one (a transparent icon, like the taskbar's), else the largest scale of that or of the 150 px tile logo.
    /// </summary>
    public static string? StoreLogoPath(string appId)
    {
        int bang = appId.IndexOf('!');
        if (bang <= 0 || appId.Contains('\\')) return null;
        var root = PackageFolder(appId[..bang]);
        if (root == null) return null;
        try
        {
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
            var app = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" &&
                string.Equals((string?)e.Attribute("Id"), appId[(bang + 1)..], StringComparison.OrdinalIgnoreCase));
            var visuals = app?.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            if (visuals == null) return null;

            string? best = null;
            int bestScore = 0;
            void Consider(string? logo, int baseSize, int bonus)
            {
                if (string.IsNullOrEmpty(logo)) return;
                var dir = Path.Combine(root, Path.GetDirectoryName(logo) ?? "");
                var stem = Path.GetFileNameWithoutExtension(logo);
                var ext = Path.GetExtension(logo);
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.EnumerateFiles(dir, stem + "*" + ext))
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (name.Contains("contrast-")) continue; // high-contrast versions are black and white
                    int size = baseSize;
                    var t = System.Text.RegularExpressions.Regex.Match(name, @"targetsize-(\d+)");
                    var sc = System.Text.RegularExpressions.Regex.Match(name, @"scale-(\d+)");
                    if (t.Success) size = int.Parse(t.Groups[1].Value);
                    else if (sc.Success) size = baseSize * int.Parse(sc.Groups[1].Value) / 100;
                    else if (name != stem.ToLowerInvariant()) continue;
                    size = Math.Min(size, 256);
                    int score = size * 10 + bonus;
                    if (name.Contains("altform-unplated")) score += 5;      // transparent, no colored square behind
                    else if (name.Contains("altform-lightunplated")) score += 3;
                    if (score > bestScore) { bestScore = score; best = file; }
                }
            }
            Consider((string?)visuals.Attribute("Square44x44Logo"), 44, 2);
            Consider((string?)visuals.Attribute("Square150x150Logo"), 150, 0); // has padding: only wins when much bigger
            return best;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Roughly how many pixels of real detail an icon has. Program icons without a large size come back from
    /// the shell blown up from 32 or 48 px, which looks soft when drawn big; those get drawn smaller instead.
    /// Shrinks the icon to a few sizes and back up: if almost nothing changes, that size is all it ever had.
    /// </summary>
    public static int Detail(BitmapSource source)
    {
        try
        {
            BitmapSource src = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            if (w != h || w < 96) return w;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            int opaque = 0;
            for (int i = 3; i < px.Length; i += 4) if (px[i] > 16) opaque++;
            if (opaque < 200) return w;

            foreach (int s in new[] { 32, 48, 64, 96 })
            {
                if (s * 2 > w) break;
                // Box-filter down to s × s.
                var small = new double[s * s * 4];
                var counts = new int[s * s];
                for (int y = 0; y < h; y++)
                {
                    int sy = y * s / h;
                    for (int x = 0; x < w; x++)
                    {
                        int sx = x * s / w, si = (sy * s + sx), pi = (y * w + x) * 4;
                        counts[si]++;
                        for (int c = 0; c < 4; c++) small[si * 4 + c] += px[pi + c];
                    }
                }
                for (int i = 0; i < counts.Length; i++)
                    for (int c = 0; c < 4; c++) small[i * 4 + c] /= Math.Max(1, counts[i]);

                // Bilinear back up, counting pixels that changed a lot.
                int changed = 0;
                for (int y = 0; y < h; y++)
                {
                    double fy = Math.Clamp((y + 0.5) * s / h - 0.5, 0, s - 1);
                    int y0 = (int)fy, y1 = Math.Min(y0 + 1, s - 1);
                    double ty = fy - y0;
                    for (int x = 0; x < w; x++)
                    {
                        int pi = (y * w + x) * 4;
                        if (px[pi + 3] <= 16) continue;
                        double fx = Math.Clamp((x + 0.5) * s / w - 0.5, 0, s - 1);
                        int x0 = (int)fx, x1 = Math.Min(x0 + 1, s - 1);
                        double tx = fx - x0;
                        double worst = 0;
                        for (int c = 0; c < 4; c++)
                        {
                            double v = (small[(y0 * s + x0) * 4 + c] * (1 - tx) + small[(y0 * s + x1) * 4 + c] * tx) * (1 - ty)
                                     + (small[(y1 * s + x0) * 4 + c] * (1 - tx) + small[(y1 * s + x1) * 4 + c] * tx) * ty;
                            worst = Math.Max(worst, Math.Abs(v - px[pi + c]));
                        }
                        if (worst > 40) changed++;
                    }
                }
                if (changed < opaque * 0.004) return s;
            }
            return w;
        }
        catch (Exception) { return 256; }
    }

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

/// <summary>The signed-in person's name as Windows shows it ("Richy Gomez"), rather than the account's user name.</summary>
public static class UserName
{
    private static string? _display;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserNameEx(int format, System.Text.StringBuilder name, ref uint size);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? server, string user, int level, out IntPtr buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo10
    {
        public string Name, Comment, UserComment, FullName;
    }

    /// <summary>
    /// The full name: the work or school account's display name, else the local or Microsoft account's full name,
    /// else the user name.
    /// </summary>
    public static string Display => _display ??= Find();

    private static string Find()
    {
        try
        {
            uint size = 256;
            var text = new System.Text.StringBuilder((int)size);
            if (GetUserNameEx(3 /* NameDisplay */, text, ref size) && text.ToString().Trim() is { Length: > 0 } shown) return shown;
        }
        catch (Exception) { }
        try
        {
            if (NetUserGetInfo(null, Environment.UserName, 10, out var buffer) == 0 && buffer != IntPtr.Zero)
            {
                try
                {
                    var info = Marshal.PtrToStructure<UserInfo10>(buffer);
                    if (info.FullName?.Trim() is { Length: > 0 } full) return full;
                }
                finally { NetApiBufferFree(buffer); }
            }
        }
        catch (Exception) { }
        return Environment.UserName;
    }
}

/// <summary>Logos for the stores games come from (in StoreLogos next to the app), found by the store's name.</summary>
public static class StoreLogos
{
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> Cache = new();

    /// <summary>The logo file's name for a store, as SquareDeck names it in different places ("Epic", "Epic Games", "EA app").</summary>
    public static string? KeyFor(string store)
    {
        string s = store.Trim().ToLowerInvariant();
        if (s.StartsWith("steam")) return "steam";
        if (s.StartsWith("epic")) return "epic";
        if (s.StartsWith("battle")) return "battlenet";
        if (s.StartsWith("xbox") || s.StartsWith("microsoft")) return "xbox";
        if (s == "ea" || s.StartsWith("ea ") || s.StartsWith("origin")) return "ea";
        if (s.StartsWith("ubisoft") || s.StartsWith("uplay")) return "ubisoft";
        if (s is "custom" or "imported" or "other" or "added") return "imported";
        return null;
    }

    /// <summary>
    /// The store's logo, or null when there isn't one for it. Pixel-art UI styles get the pixel-art versions
    /// (StoreLogos\pixel), when there's one.
    /// </summary>
    public static System.Windows.Media.ImageSource? For(string store)
    {
        if (KeyFor(store) is not string key) return null;
        bool pixel = UiStyles.Current.Glyphs == "pixel";
        string cacheKey = (pixel ? "pixel/" : "") + key;
        if (Cache.TryGetValue(cacheKey, out var known)) return known;
        var folder = Path.Combine(AppContext.BaseDirectory, "StoreLogos");
        var path = Path.Combine(folder, "pixel", key + ".png");
        if (!pixel || !File.Exists(path)) path = Path.Combine(folder, key + ".png");
        var image = File.Exists(path) ? ImageLoader.FromFile(path, 256) : null;
        Cache[cacheKey] = image;
        return image;
    }
}

/// <summary>
/// Writes what went wrong to crash.log in SquareDeck's data folder (newest at the end, kept small), so a crash can be
/// looked into afterwards.
/// </summary>
public static class CrashLog
{
    public static string Path => System.IO.Path.Combine(Settings.DataDir, "crash.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            if (File.Exists(Path) && new FileInfo(Path).Length > 512 * 1024) File.Delete(Path);
            File.AppendAllText(Path, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {where}  (SquareDeck {Updater.Current})\r\n{ex}\r\n\r\n");
        }
        catch (Exception) { }
    }
}
