using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>Starting with Windows, and optionally standing in for the desktop.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string ValueName = "SquareDeck";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Open the launcher after signing in (the normal desktop still loads behind it).</summary>
    public static bool RunAtSignIn
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception) { return false; }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, "\"" + ExePath + "\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Use the launcher instead of the Windows desktop for this user: no taskbar or Start menu
    /// until "Exit SquareDeck". Only this user account is affected.
    /// </summary>
    public static bool ReplacesDesktop
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(WinlogonKey);
                return key?.GetValue("Shell") is string shell &&
                       shell.Contains("SquareDeck", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(WinlogonKey);
            if (value) key.SetValue("Shell", "\"" + ExePath + "\"");
            else key.DeleteValue("Shell", throwOnMissingValue: false);
        }
    }

    /// <summary>True when the taskbar exists, i.e. the normal desktop is loaded.</summary>
    public static bool DesktopIsRunning => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>
    /// Brings up the normal Windows desktop when we are standing in for it. Explorer only acts as the
    /// desktop while it is the registered shell, so the setting is lifted for a moment and put back.
    /// </summary>
    public static async Task StartDesktopAsync()
    {
        bool wasReplacing = ReplacesDesktop;
        try
        {
            if (wasReplacing) ReplacesDesktop = false;
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            await Task.Delay(4000);
        }
        catch (Exception) { }
        finally
        {
            try { if (wasReplacing) ReplacesDesktop = true; } catch (Exception) { }
        }
    }
}

/// <summary>Bringing the launcher window in front of a running game.</summary>
public static class WindowActivator
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>The window in front right now (to give the focus back to it later).</summary>
    public static IntPtr Foreground()
    {
        try { return GetForegroundWindow(); } catch (Exception) { return IntPtr.Zero; }
    }

    /// <summary>Gives the keyboard and controller back to another window (the game, after the quick menu).</summary>
    public static void GiveFocusTo(IntPtr target)
    {
        try
        {
            if (target == IntPtr.Zero || !IsWindow(target)) return;
            var front = GetForegroundWindow();
            uint frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, IntPtr.Zero);
            uint mine = GetCurrentThreadId();
            bool attached = frontThread != 0 && frontThread != mine && AttachThreadInput(mine, frontThread, true);
            BringWindowToTop(target);
            bool ok = SetForegroundWindow(target);
            if (attached) AttachThreadInput(mine, frontThread, false);
            if (!ok)
            {
                const byte Alt = 0x12;
                keybd_event(Alt, 0, 0, UIntPtr.Zero);
                keybd_event(Alt, 0, 0x2, UIntPtr.Zero);
                SetForegroundWindow(target);
            }
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Puts a window on top of everything, even a full-screen game or the Xbox full screen experience, and gives it
    /// the keyboard and controller. Windows normally refuses this to apps in the background, so this borrows the
    /// input of whatever is in front for a moment.
    /// </summary>
    public static void ShowOnTop(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Maximized;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) { window.Activate(); return; }
            const uint NoSize = 0x1, NoMove = 0x2, ShowWindow = 0x40;
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | NoMove | ShowWindow); // always on top
            var front = GetForegroundWindow();
            uint frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, IntPtr.Zero);
            uint mine = GetCurrentThreadId();
            bool attached = frontThread != 0 && frontThread != mine && AttachThreadInput(mine, frontThread, true);
            const byte Alt = 0x12;
            keybd_event(Alt, 0, 0, UIntPtr.Zero);
            keybd_event(Alt, 0, 0x2, UIntPtr.Zero);
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            if (attached) AttachThreadInput(mine, frontThread, false);
            window.Activate();
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Puts the window back at the top of the always-on-top windows without taking the focus: run regularly while the
    /// launch splash shows, so a store's window (Steam, EA app, Ubisoft Connect…) can't come up over it.
    /// </summary>
    public static void KeepOnTop(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10, ShowWindow = 0x40;
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | NoMove | NoActivate | ShowWindow);
        }
        catch (Exception) { }
    }

    /// <summary>True when the window in front is this one.</summary>
    public static bool IsInFront(Window window)
    {
        try { return GetForegroundWindow() == new WindowInteropHelper(window).Handle; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// True when another app's window is in front and covers the whole screen SquareDeck is on (a maximized or full
    /// screen app), so none of SquareDeck can be seen. The desktop and the taskbar don't count.
    /// </summary>
    public static bool FrontCovers(Window window)
    {
        try
        {
            var mine = new WindowInteropHelper(window).Handle;
            var front = GetForegroundWindow();
            if (front == IntPtr.Zero || front == mine) return false;
            GetWindowThreadProcessId(front, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(front, cls, cls.Capacity);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
            if (!GetWindowRect(front, out var r)) return false;
            var monitor = MonitorFromWindow(mine, 2);
            if (MonitorFromWindow(front, 2) != monitor) return false;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;
            // Its whole work area at least (a maximized window leaves the taskbar showing; SquareDeck is behind it all the same).
            return r.Left <= info.Work.Left + 8 && r.Top <= info.Work.Top + 8 && r.Right >= info.Work.Right - 8 && r.Bottom >= info.Work.Bottom - 8;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Takes away "always on top" again.</summary>
    public static void ClearOnTop(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    /// <summary>The app whose window is in front: its window, process, title, and whether it fills its screen.</summary>
    public readonly record struct FrontWindow(IntPtr Handle, int ProcessId, string Title, string Process, bool FullScreen);

    // The desktop, the taskbar, the Xbox app (the home of the full screen experience) and SquareDeck itself aren't games.
    private static readonly string[] NotGames = { "explorer", "XboxPcApp", "XboxApp", "GamingApp", "ShellExperienceHost", "StartMenuExperienceHost",
                                                  "SearchHost", "TextInputHost", "ApplicationFrameHost", "LockApp", "SquareDeck" };

    /// <summary>The app in front, if it's something other than the desktop, the Xbox app or SquareDeck.</summary>
    public static FrontWindow? FrontApp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return null;
            string process;
            try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); process = p.ProcessName; }
            catch (Exception) { return null; }
            if (NotGames.Any(n => n.Equals(process, StringComparison.OrdinalIgnoreCase))) return null;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            bool full = false;
            if (GetWindowRect(hwnd, out var r))
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
                    full = r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
            }
            string name = title.ToString().Trim();
            return new FrontWindow(hwnd, (int)pid, name.Length > 0 ? name : process, process, full);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Asks an app to close (like its close button), and ends it if it's still running a few seconds later.</summary>
    public static async Task CloseAppAsync(FrontWindow app)
    {
        try
        {
            PostMessage(app.Handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < 20 && IsWindow(app.Handle); i++) await Task.Delay(250);
            using var p = System.Diagnostics.Process.GetProcessById(app.ProcessId);
            if (!p.HasExited && IsWindow(app.Handle)) p.Kill(entireProcessTree: true);
        }
        catch (Exception) { }
    }

    public static void BringToFront(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Maximized;
            var handle = new WindowInteropHelper(window).Handle;
            // Windows only lets a background app take the foreground right after input; a tap of Alt counts.
            const byte Alt = 0x12;
            const uint KeyUp = 0x2;
            keybd_event(Alt, 0, 0, UIntPtr.Zero);
            keybd_event(Alt, 0, KeyUp, UIntPtr.Zero);
            SetForegroundWindow(handle);
            window.Activate();
        }
        catch (Exception) { }
    }
}

/// <summary>Master volume of the default speakers / headphones (Windows Core Audio).</summary>
public static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // Only string values are read, so the variant is just its type tag and a pointer.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer;
        public IntPtr Extra;
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    // Windows has no documented call for "make this the default device"; this is the interface its own
    // Sound settings use, and what audio-switcher tools rely on.
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Slots before the one we need, in native order.
        int GetMixFormat(); int GetDeviceFormat(); int ResetDeviceFormat(); int SetDeviceFormat();
        int GetProcessingPeriod(); int SetProcessingPeriod(); int GetShareMode(); int SetShareMode();
        int GetPropertyValue(); int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }

    /// <summary>Speakers, headphones, TVs… that are connected and enabled: id, name, and whether it is the default.</summary>
    public static List<(string Id, string Name, bool IsDefault)> GetOutputs()
    {
        var result = new List<(string, string, bool)>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            const int Render = 0, Multimedia = 1, ActiveOnly = 1, Read = 0;

            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var current) == 0) current.GetId(out defaultId);

            if (enumerator.EnumAudioEndpoints(Render, ActiveOnly, out var devices) != 0) return result;
            devices.GetCount(out var count);
            // PKEY_Device_FriendlyName
            var nameKey = new PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };

            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) continue;

                string name = "Audio device";
                if (device.OpenPropertyStore(Read, out var store) == 0 && store.GetValue(ref nameKey, out var value) == 0)
                {
                    const ushort WideString = 31;
                    if (value.Type == WideString && value.Pointer != IntPtr.Zero)
                        name = Marshal.PtrToStringUni(value.Pointer) ?? name;
                    PropVariantClear(ref value);
                }
                result.Add((id, name, id == defaultId));
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    /// <summary>Makes a device the default for everything (media, system sounds and calls).</summary>
    public static bool SetDefaultOutput(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            bool ok = true;
            for (int role = 0; role <= 2; role++) // console, multimedia, communications
                ok &= policy.SetDefaultEndpoint(deviceId, role) == 0;
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        // Order matters: these mirror the native interface slot by slot.
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int count);
        int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    private static IAudioMeterInformation? _meter;

    /// <summary>How loud the PC's output is right now, 0 to 1. Drives the music visualizer.</summary>
    public static float GetPeak()
    {
        try
        {
            if (_meter == null)
            {
                var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
                const int Render = 0, Multimedia = 1, ClsCtxAll = 23;
                if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device) != 0) return 0;
                var iid = typeof(IAudioMeterInformation).GUID;
                if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) != 0) return 0;
                _meter = (IAudioMeterInformation)instance;
            }
            return _meter.GetPeakValue(out var peak) == 0 ? peak : 0;
        }
        catch (Exception)
        {
            _meter = null; // the default device may have changed; look it up again next time
            return 0;
        }
    }

    /// <summary>Forget the cached meter (call after the default output changes).</summary>
    public static void ResetMeter() => _meter = null;

    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            const int Render = 0, Multimedia = 1, ClsCtxAll = 23;
            if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) == 0
                ? (IAudioEndpointVolume)instance
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>0–100, or null when there is no audio device.</summary>
    public static int? GetVolume()
    {
        var ep = Endpoint();
        if (ep == null || ep.GetMasterVolumeLevelScalar(out var level) != 0) return null;
        return (int)Math.Round(level * 100);
    }

    public static void SetVolume(int percent)
    {
        var ep = Endpoint();
        if (ep == null) return;
        var context = Guid.Empty;
        ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context);
    }

    public static bool GetMute()
    {
        var ep = Endpoint();
        return ep != null && ep.GetMute(out var mute) == 0 && mute;
    }

    public static void SetMute(bool mute)
    {
        var ep = Endpoint();
        if (ep == null) return;
        var context = Guid.Empty;
        ep.SetMute(mute, ref context);
    }
}

/// <summary>
/// Brightness of a built-in screen (laptops and handhelds). Not available on desktop monitors.
/// Read and set through Windows' management service (WMI) directly, which takes a few milliseconds, instead of
/// starting PowerShell each time (most of a second). The last level is kept, so the quick settings can show the
/// slider the moment they open and correct it after.
/// </summary>
public static class Brightness
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _directFailed;

    /// <summary>The last level read or set (0–100), or null before the first read.</summary>
    public static int? Last { get; private set; }

    /// <summary>False once a read found the screen's brightness can't be controlled; null when not known yet.</summary>
    public static bool? Supported { get; private set; }

    /// <summary>Reads the level in the background (at start), so it's ready when the quick settings open.</summary>
    public static void Prefetch() => _ = Task.Run(GetAsync);

    /// <summary>0–100, or null when the display's brightness can't be controlled.</summary>
    public static async Task<int?> GetAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            int? level = null;
            if (!_directFailed)
            {
                try { level = await Task.Run(ReadDirect).ConfigureAwait(false); }
                catch (Exception) { _directFailed = true; }
            }
            if (_directFailed)
            {
                var output = await ProcessUtil.RunPowerShellAsync(
                    "(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | " +
                    "Select-Object -First 1).CurrentBrightness").ConfigureAwait(false);
                level = int.TryParse(output.Trim(), out var value) ? Math.Clamp(value, 0, 100) : null;
            }
            Supported = level != null;
            if (level != null) Last = level;
            return level;
        }
        catch (Exception)
        {
            return null;
        }
        finally { Gate.Release(); }
    }

    private static readonly object Pending = new();
    private static int? _target;      // the level asked for most recently, not yet applied
    private static bool _applying;
    private static DateTime _lastSet;

    /// <summary>When the brightness was last changed from SquareDeck (a read just after may still see the old level).</summary>
    public static bool RecentlySet => (DateTime.UtcNow - _lastSet).TotalSeconds < 1.5;

    /// <summary>
    /// Sets the level. Quick changes in a row (a slider being dragged) don't queue up: only the newest level is applied,
    /// so the screen keeps up with the slider instead of working through every step behind it.
    /// </summary>
    public static async Task SetAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Last = percent;
        _lastSet = DateTime.UtcNow;
        lock (Pending) { _target = percent; if (_applying) return; _applying = true; }
        try
        {
            while (true)
            {
                int level;
                lock (Pending) { if (_target is not int t) { _applying = false; return; } level = t; _target = null; }
                await Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!_directFailed)
                    {
                        try { await Task.Run(() => WriteDirect(level)).ConfigureAwait(false); continue; }
                        catch (Exception) { _directFailed = true; }
                    }
                    await ProcessUtil.RunPowerShellAsync(
                        "Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods | " +
                        $"Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{{Timeout=1; Brightness={level}}} | Out-Null").ConfigureAwait(false);
                }
                catch (Exception) { }
                finally { _lastSet = DateTime.UtcNow; Gate.Release(); }
            }
        }
        catch (Exception) { lock (Pending) _applying = false; }
    }

    /// <summary>Reads the real level now and then (for the widget), unless SquareDeck has just changed it.</summary>
    public static void Refresh()
    {
        if (RecentlySet || _applying || Gate.CurrentCount == 0) return;
        _ = Task.Run(GetAsync);
    }

    private static dynamic? _service;
    private static dynamic? _methods;

    /// <summary>The WMI namespace with the screen's brightness, through Windows' own scripting object (kept, since connecting is slow).</summary>
    private static dynamic Wmi()
    {
        if (_service != null) return _service;
        var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new NotSupportedException();
        dynamic locator = Activator.CreateInstance(type)!;
        return _service = locator.ConnectServer(".", "root\\WMI");
    }

    /// <summary>Null when there's no built-in screen whose brightness Windows controls. Throws if WMI can't be used.</summary>
    private static int? ReadDirect()
    {
        try { return Read(); }
        catch (Exception) { _service = null; _methods = null; return Read(); } // the kept connection went stale: once more

        static int? Read()
        {
            dynamic service = Wmi();
            foreach (dynamic monitor in service.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                return Math.Clamp(Convert.ToInt32(monitor.CurrentBrightness), 0, 100);
            return null;
        }
    }

    private static void WriteDirect(int percent)
    {
        try { Write(percent); }
        catch (Exception) { _service = null; _methods = null; Write(percent); }

        static void Write(int percent)
        {
            if (_methods == null)
            {
                dynamic service = Wmi();
                foreach (dynamic methods in service.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods")) { _methods = methods; break; }
            }
            _methods?.WmiSetBrightness(1, (byte)percent);
        }
    }
}

/// <summary>Facts about this PC for the Device information page.</summary>
public sealed class DeviceInfo
{
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Bios { get; init; } = "";
    public string Cpu { get; init; } = "";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public long MemoryBytes { get; init; }
    public string Gpu { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshRate { get; init; }
    public string Os { get; init; } = "";
    public string OsVersion { get; init; } = "";

    private const string Script =
        "$cs=Get-CimInstance Win32_ComputerSystem; $bios=Get-CimInstance Win32_BIOS; " +
        "$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1; $gpus=@(Get-CimInstance Win32_VideoController); " +
        "$os=Get-CimInstance Win32_OperatingSystem; " +
        "$scr=$gpus | Where-Object { $_.CurrentHorizontalResolution } | Select-Object -First 1; " +
        "[pscustomobject]@{ Manufacturer=[string]$cs.Manufacturer; Model=[string]$cs.Model; " +
        "MemoryBytes=[int64]$cs.TotalPhysicalMemory; Serial=[string]$bios.SerialNumber; Bios=[string]$bios.SMBIOSBIOSVersion; " +
        "Cpu=[string]$cpu.Name; Cores=[int]$cpu.NumberOfCores; Threads=[int]$cpu.NumberOfLogicalProcessors; " +
        "Gpu=(($gpus | ForEach-Object { $_.Name }) -join ', '); Width=[int]$scr.CurrentHorizontalResolution; " +
        "Height=[int]$scr.CurrentVerticalResolution; RefreshRate=[int]$scr.CurrentRefreshRate; " +
        "Os=[string]$os.Caption; OsVersion=[string]$os.Version } | ConvertTo-Json -Compress";

    public static async Task<DeviceInfo?> LoadAsync()
    {
        try
        {
            var output = await ProcessUtil.RunPowerShellAsync(Script);
            int start = output.IndexOf('{');
            return start < 0 ? null : JsonSerializer.Deserialize<DeviceInfo>(output[start..]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>IPv4 addresses of the active network connections.</summary>
    public static List<string> GetIpAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add($"{address.Address} ({nic.Name})");
                }
            }
        }
        catch (Exception) { }
        return result;
    }
}
