using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace SquareDeck.Services;

/// <summary>Windows calls for the task manager (from Handheld Task Manager).</summary>
internal static class TaskNative
{
    // ---- theming / icons ----------------------------------------------------------------
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr handle);

    // ---- windows --------------------------------------------------------------------------
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int DWMWA_CLOAKED = 14;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;

    // ---- processes ------------------------------------------------------------------------
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Full path of a process's executable, or null when Windows won't tell us.</summary>
    public static string? GetProcessPath(int pid)
    {
        const uint QueryLimitedInformation = 0x1000;
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally { CloseHandle(handle); }
    }

    // ---- system counters ------------------------------------------------------------------
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}



/// <summary>All processes that share an executable name, measured over one sampling interval.</summary>
public sealed class TaskGroupData
{
    public string Key = "";
    public string Name = "";
    public string Title = "";
    public string? Path;
    public double Cpu;
    public long Memory;
    public readonly List<int> Pids = new();
    public readonly List<IntPtr> Windows = new();
}

public sealed class TaskDriveStat
{
    public string Label { get; init; } = "";
    public string Sub { get; init; } = "";
    public double Fraction { get; init; }
}

public sealed class TaskSample
{
    public List<TaskGroupData> Groups = new();
    public int ProcessCount;
    public double CpuTotal;
    public ulong MemTotal, MemAvailable;
    public bool HasBattery, OnAcPower, Charging;
    public int BatteryPercent = -1;
    public int BatterySecondsLeft = -1;
    public double NetDownBytesPerSec, NetUpBytesPerSec;
    public List<TaskDriveStat> Drives = new();
    public TimeSpan Uptime;
}

/// <summary>
/// Collects process and system statistics. Call <see cref="Collect"/> from a background thread,
/// one call at a time; usage figures are averages since the previous call.
/// </summary>
public sealed class TaskSampler
{
    private readonly Dictionary<int, TimeSpan> _cpuTimes = new();
    private readonly HashSet<int> _cpuDenied = new();
    private readonly Dictionary<int, string?> _paths = new();
    private long _lastStamp;
    private long _lastIdle, _lastKernel, _lastUser;
    private long _lastNetDown, _lastNetUp;
    private bool _hasNetBaseline;

    public TaskSample Collect()
    {
        var sample = new TaskSample();
        long stamp = Stopwatch.GetTimestamp();
        double elapsed = _lastStamp == 0 ? 0 : (stamp - _lastStamp) / (double)Stopwatch.Frequency;
        _lastStamp = stamp;

        CollectProcesses(sample, elapsed);
        CollectSystem(sample, elapsed);
        return sample;
    }

    // ---- processes ------------------------------------------------------------------------

    private void CollectProcesses(TaskSample sample, double elapsed)
    {
        int cores = Math.Max(1, Environment.ProcessorCount);
        var groups = new Dictionary<string, TaskGroupData>(StringComparer.OrdinalIgnoreCase);
        var byPid = new Dictionary<int, TaskGroupData>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                int pid = process.Id;
                if (pid == 0) continue; // System Idle Process

                string name = process.ProcessName;
                long memory = process.WorkingSet64;
                double cpu = 0;

                // Protected system processes refuse the query; remember them so we don't pay for the exception every tick.
                if (!_cpuDenied.Contains(pid))
                {
                    try
                    {
                        TimeSpan total = process.TotalProcessorTime;
                        if (elapsed > 0 && _cpuTimes.TryGetValue(pid, out var previous))
                        {
                            double used = (total - previous).TotalSeconds;
                            if (used > 0) cpu = Math.Min(100, used / (elapsed * cores) * 100);
                        }
                        _cpuTimes[pid] = total;
                    }
                    catch { _cpuDenied.Add(pid); }
                }

                if (!_paths.TryGetValue(pid, out var path))
                {
                    path = TaskNative.GetProcessPath(pid);
                    _paths[pid] = path;
                }

                if (!groups.TryGetValue(name, out var group))
                {
                    group = new TaskGroupData { Key = name.ToLowerInvariant(), Name = name };
                    groups[name] = group;
                }
                group.Pids.Add(pid);
                group.Cpu += cpu;
                group.Memory += memory;
                group.Path ??= path;
                byPid[pid] = group;
                sample.ProcessCount++;
            }
            catch { /* the process exited while we were looking at it */ }
            finally { process.Dispose(); }
        }

        // Forget processes that are gone (their IDs get reused).
        foreach (var pid in _cpuTimes.Keys.Where(p => !byPid.ContainsKey(p)).ToList()) _cpuTimes.Remove(pid);
        foreach (var pid in _paths.Keys.Where(p => !byPid.ContainsKey(p)).ToList()) _paths.Remove(pid);
        _cpuDenied.RemoveWhere(p => !byPid.ContainsKey(p));

        foreach (var (hwnd, pid, title) in TopLevelWindows())
        {
            if (!byPid.TryGetValue(pid, out var group)) continue;
            group.Windows.Add(hwnd);
            if (group.Title.Length == 0) group.Title = title;
        }

        sample.Groups = groups.Values.ToList();
    }

    /// <summary>The windows a user would think of as "open apps".</summary>
    private static List<(IntPtr Hwnd, int Pid, string Title)> TopLevelWindows()
    {
        var result = new List<(IntPtr, int, string)>();
        var buffer = new StringBuilder(512);

        TaskNative.EnumWindows((hwnd, _) =>
        {
            if (!TaskNative.IsWindowVisible(hwnd)) return true;
            if (TaskNative.GetWindow(hwnd, TaskNative.GW_OWNER) != IntPtr.Zero) return true;                    // dialogs, popups
            if ((TaskNative.GetWindowLong(hwnd, TaskNative.GWL_EXSTYLE) & TaskNative.WS_EX_TOOLWINDOW) != 0) return true;
            if (TaskNative.DwmGetWindowAttribute(hwnd, TaskNative.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;                                                                            // hidden Store-app shells

            buffer.Clear();
            if (TaskNative.GetWindowTextW(hwnd, buffer, buffer.Capacity) <= 0) return true;
            string title = buffer.ToString();
            if (title == "Program Manager") return true;                                               // the desktop

            TaskNative.GetWindowThreadProcessId(hwnd, out uint pid);
            result.Add((hwnd, (int)pid, title));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    // ---- system ---------------------------------------------------------------------------

    private void CollectSystem(TaskSample sample, double elapsed)
    {
        // CPU: kernel time includes idle time.
        if (TaskNative.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long total = (kernel - _lastKernel) + (user - _lastUser);
            long idleDelta = idle - _lastIdle;
            if (_lastKernel != 0 && total > 0)
                sample.CpuTotal = Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
        }

        var memory = new TaskNative.MEMORYSTATUSEX { dwLength = 64 };
        if (TaskNative.GlobalMemoryStatusEx(ref memory))
        {
            sample.MemTotal = memory.ullTotalPhys;
            sample.MemAvailable = memory.ullAvailPhys;
        }

        if (TaskNative.GetSystemPowerStatus(out var power))
        {
            sample.HasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255;
            sample.OnAcPower = power.ACLineStatus == 1;
            sample.Charging = (power.BatteryFlag & 8) != 0;
            sample.BatteryPercent = power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : -1;
            sample.BatterySecondsLeft = power.BatteryLifeTime;
        }

        try
        {
            long down = 0, up = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                down += stats.BytesReceived;
                up += stats.BytesSent;
            }
            if (_hasNetBaseline && elapsed > 0)
            {
                sample.NetDownBytesPerSec = Math.Max(0, (down - _lastNetDown) / elapsed);
                sample.NetUpBytesPerSec = Math.Max(0, (up - _lastNetUp) / elapsed);
            }
            _lastNetDown = down; _lastNetUp = up; _hasNetBaseline = true;
        }
        catch { /* network stack unavailable */ }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network) continue;
                long total = drive.TotalSize, free = drive.AvailableFreeSpace;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                sample.Drives.Add(new TaskDriveStat
                {
                    Label = $"{label} ({drive.Name.TrimEnd('\\')})",
                    Sub = $"{SystemStats.Bytes(free)} free of {SystemStats.Bytes(total)}",
                    Fraction = total > 0 ? 1.0 - (double)free / total : 0
                });
            }
            catch { /* drive vanished mid-query */ }
        }

        sample.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
    }
}
