using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace SquareDeck.Services;

// ====================================================================== Windows Update

public static class WindowsUpdate
{
    /// <summary>True when Windows has installed updates and is waiting for a restart to finish them.</summary>
    public static bool RestartPending()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var wu = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            if (wu != null) return true;
            using var cbs = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            return cbs != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// ====================================================================== AnyFSE

/// <summary>
/// AnyFSE (github.com/ashpynov/AnyFSE) lets another launcher act as the Home app of Windows'
/// gaming full-screen experience. This only finds it, downloads its installer and opens it.
/// </summary>
public static class AnyFse
{
    public const string ReleasesPage = "https://github.com/ashpynov/AnyFSE/releases";

    private const string GamingKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration";

    /// <summary>
    /// AnyFSE's settings file (AnyFSE.json): in its package's LocalState folder, or LocalState next to the program
    /// when it runs without one (AnyFSE src/Tools/Paths.cpp).
    /// </summary>
    public static string? ConfigFile()
    {
        try
        {
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(packages))
                foreach (var dir in Directory.EnumerateDirectories(packages, "*AnyFSE*"))
                {
                    var state = Path.Combine(dir, "LocalState");
                    if (Directory.Exists(state)) return Path.Combine(state, "AnyFSE.json");
                }
            var program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyFSE", "LocalState");
            if (Directory.Exists(program)) return Path.Combine(program, "AnyFSE.json");
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>True when AnyFSE is set to start this launcher.</summary>
    public static bool LaunchesUs()
    {
        try
        {
            var file = ConfigFile();
            if (file == null || !File.Exists(file) || Environment.ProcessPath is not string me) return false;
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
            var path = root?["Launcher"]?["Path"]?.GetValue<string>();
            return string.Equals(path, me, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Points AnyFSE at this launcher as a custom launcher, keeping its other settings. AnyFSE reads it the next
    /// time Windows starts the full screen experience. False if AnyFSE's settings folder couldn't be found or written.
    /// </summary>
    public static bool UseUs(bool on)
    {
        try
        {
            var file = ConfigFile();
            if (file == null || Environment.ProcessPath is not string me) return false;
            var root = (File.Exists(file) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) : null) as System.Text.Json.Nodes.JsonObject
                       ?? new System.Text.Json.Nodes.JsonObject();
            if (on)
            {
                root["Launcher"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["Path"] = me, ["StartCommand"] = me, ["StartArg"] = "", ["CustomSettings"] = true,
                    ["ProcessName"] = Path.GetFileName(me), ["WindowTitle"] = "SquareDeck", ["IconFile"] = me
                };
            }
            else if (root["Launcher"]?["Path"]?.GetValue<string>() is string current &&
                     string.Equals(current, me, StringComparison.OrdinalIgnoreCase))
            {
                root.Remove("Launcher"); // AnyFSE goes back to its default (the Xbox app)
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>True when Windows' full screen experience has AnyFSE chosen as its Home app.</summary>
    public static bool IsHomeApp() =>
        Microsoft.Win32.Registry.GetValue(GamingKey, "GamingHomeApp", null) is string app && app.Contains("AnyFSE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows' own switch: open the full screen experience when you sign in.</summary>
    public static bool StartsAtSignIn
    {
        get => Microsoft.Win32.Registry.GetValue(GamingKey, "StartupToGamingHome", 0) is int v && v != 0;
        set
        {
            try { Microsoft.Win32.Registry.SetValue(GamingKey, "StartupToGamingHome", value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); }
            catch (Exception) { }
        }
    }
    private const string LatestApi = "https://api.github.com/repos/ashpynov/AnyFSE/releases/latest";
    private const string InstallerName = "AnyFSE.Installer.exe";

    public static bool IsInstalled()
    {
        try
        {
            if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyFSE"))) return true;
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AnyFSE");
            return key != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Opens AnyFSE's own settings from its Start menu entry.</summary>
    public static async Task<bool> OpenAsync()
    {
        try
        {
            var apps = await InstalledApps.ScanAsync();
            var entry = apps.FirstOrDefault(a => a.Name.Contains("AnyFSE", StringComparison.OrdinalIgnoreCase));
            return entry != null && InstalledApps.Launch(entry.AppId);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads the newest AnyFSE.Installer.exe from the project's GitHub releases and returns where it
    /// was saved along with the version. Throws with a readable message if that isn't possible.
    /// </summary>
    public static async Task<(string Path, string Version)> DownloadInstallerAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SquareDeck/2.1");

        var json = await http.GetStringAsync(LatestApi, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var version = doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";

        string? url = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, InstallerName, StringComparison.OrdinalIgnoreCase)) continue;
                url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                break;
            }
        }
        // Only ever fetch from the project's own GitHub release downloads.
        if (url == null || !url.StartsWith("https://github.com/ashpynov/AnyFSE/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The latest release doesn't include " + InstallerName + ".");

        var folder = Path.Combine(Path.GetTempPath(), "SquareDeck");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, InstallerName);
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(path);
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        return (path, version);
    }
}

// ====================================================================== system statistics (task manager)

public sealed record ProcessRow(string Name, int Count, double CpuPercent, long MemoryBytes, List<int> Ids, string? Title);

/// <summary>CPU, memory, network and per-process figures, sampled on demand.</summary>
public sealed class SystemStats
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private long _idle, _kernel, _user, _netIn, _netOut;
    private DateTime _lastSample = DateTime.MinValue;
    private Dictionary<int, TimeSpan> _cpuTimes = new();

    public double CpuPercent { get; private set; }
    public long MemoryTotal { get; private set; }
    public long MemoryUsed { get; private set; }
    public double DownloadBytesPerSecond { get; private set; }
    public double UploadBytesPerSecond { get; private set; }
    public List<ProcessRow> Processes { get; private set; } = new();
    public List<ProcessRow> Apps { get; private set; } = new();

    /// <summary>Takes a fresh reading. Call it off the UI thread; rates compare with the previous call.</summary>
    public void Sample()
    {
        var now = DateTime.UtcNow;
        double seconds = _lastSample == DateTime.MinValue ? 0 : (now - _lastSample).TotalSeconds;
        _lastSample = now;

        // Whole-PC CPU: the share of the interval that wasn't idle. (Kernel time includes idle time.)
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long total = (kernel - _kernel) + (user - _user), idleDelta = idle - _idle;
            if (_kernel != 0 && total > 0) CpuPercent = Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100);
            (_idle, _kernel, _user) = (idle, kernel, user);
        }

        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            MemoryTotal = (long)mem.TotalPhys;
            MemoryUsed = (long)(mem.TotalPhys - mem.AvailPhys);
        }

        try
        {
            long received = 0, sent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            if (seconds > 0 && _netIn != 0)
            {
                DownloadBytesPerSecond = Math.Max(0, (received - _netIn) / seconds);
                UploadBytesPerSecond = Math.Max(0, (sent - _netOut) / seconds);
            }
            (_netIn, _netOut) = (received, sent);
        }
        catch (Exception) { }

        SampleProcesses(seconds);
    }

    private void SampleProcesses(double seconds)
    {
        var times = new Dictionary<int, TimeSpan>();
        var groups = new Dictionary<string, (int Count, double Cpu, long Memory, List<int> Ids)>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<ProcessRow>();
        int cores = Environment.ProcessorCount;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue; // the idle "process"
                double cpu = 0;
                try
                {
                    // Not readable for a few protected system processes unless elevated: those show 0%.
                    var used = p.TotalProcessorTime;
                    times[p.Id] = used;
                    if (seconds > 0 && _cpuTimes.TryGetValue(p.Id, out var before))
                        cpu = Math.Clamp((used - before).TotalSeconds / seconds / cores * 100, 0, 100);
                }
                catch (Exception) { }

                long memory = p.WorkingSet64;
                if (!groups.TryGetValue(p.ProcessName, out var g)) g = (0, 0d, 0L, new List<int>());
                g.Ids.Add(p.Id);
                groups[p.ProcessName] = (g.Count + 1, g.Cpu + cpu, g.Memory + memory, g.Ids);

                var title = p.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(title) && p.MainWindowHandle != IntPtr.Zero)
                    apps.Add(new ProcessRow(p.ProcessName, 1, cpu, memory, new List<int> { p.Id }, title));
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }

        _cpuTimes = times;
        Processes = groups.Select(g => new ProcessRow(g.Key, g.Value.Count, g.Value.Cpu, g.Value.Memory, g.Value.Ids, null)).ToList();
        Apps = apps;
    }

    // Ending these crashes Windows or signs the user out, so the task manager refuses.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "Memory Compression", "Secure System", "MsMpEng", "sihost", "explorer"
    };

    public static bool IsProtected(string processName) => Protected.Contains(processName);

    /// <summary>Asks windows to close politely (so the app can save). Returns how many accepted the request.</summary>
    public static int Close(IEnumerable<int> ids)
    {
        int closed = 0;
        foreach (var id in ids)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                if (p.CloseMainWindow()) closed++;
            }
            catch (Exception) { }
        }
        return closed;
    }

    /// <summary>Force-closes processes and everything they started. Returns how many were ended.</summary>
    public static int Kill(IEnumerable<int> ids)
    {
        int killed = 0;
        foreach (var id in ids)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                p.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception) { }
        }
        return killed;
    }

    public static bool SetPriority(IEnumerable<int> ids, ProcessPriorityClass priority)
    {
        bool any = false;
        foreach (var id in ids)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                p.PriorityClass = priority;
                any = true;
            }
            catch (Exception) { }
        }
        return any;
    }

    public static string Bytes(double bytes) =>
        bytes >= 1024d * 1024 * 1024 ? $"{bytes / 1024 / 1024 / 1024:0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / 1024 / 1024:0} MB"
        : $"{bytes / 1024:0} KB";
}

// ====================================================================== file operations (explorer)

public static class FileOps
{
    /// <summary>A name in <paramref name="folder"/> that isn't taken: "name", then "name (2)", "name (3)"…</summary>
    public static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    /// <summary>Copies or moves files and folders into a folder. Nothing is ever overwritten.</summary>
    public static void Transfer(IEnumerable<string> sources, string destinationFolder, bool move, CancellationToken ct)
    {
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var target = UniquePath(destinationFolder, Path.GetFileName(source.TrimEnd('\\')));
            if (Directory.Exists(source))
            {
                // A folder can't go inside itself.
                if ((destinationFolder + "\\").StartsWith(source.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A folder can't be copied or moved into itself.");
                if (move && Path.GetPathRoot(source) == Path.GetPathRoot(target)) Directory.Move(source, target);
                else
                {
                    CopyFolder(source, target, ct);
                    if (move) Directory.Delete(source, recursive: true);
                }
            }
            else if (File.Exists(source))
            {
                if (move) File.Move(source, target);
                else File.Copy(source, target);
            }
        }
    }

    private static void CopyFolder(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyFolder(dir, Path.Combine(target, Path.GetFileName(dir)), ct);
    }

    /// <summary>Sends files and folders to the Recycle Bin.</summary>
    public static void Recycle(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(path))
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    public static string Rename(string path, string newName)
    {
        var folder = Path.GetDirectoryName(path.TrimEnd('\\')) ?? throw new IOException("That item can't be renamed.");
        var target = Path.Combine(folder, newName);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Something with that name is already here.");
        if (Directory.Exists(path)) Directory.Move(path, target);
        else File.Move(path, target);
        return target;
    }

    /// <summary>Packs the items into a new .zip next to them and returns its path.</summary>
    public static string Zip(IReadOnlyList<string> paths, string folder)
    {
        string name = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0].TrimEnd('\\')) : "Archive";
        var zipPath = UniquePath(folder, name + ".zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (File.Exists(path)) zip.CreateEntryFromFile(path, Path.GetFileName(path));
            else if (Directory.Exists(path))
            {
                var root = Path.GetFileName(path.TrimEnd('\\'));
                foreach (var file in Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, root + "/" + Path.GetRelativePath(path, file).Replace('\\', '/'));
            }
        }
        return zipPath;
    }

    /// <summary>Unpacks a .zip into a new folder beside it and returns that folder.</summary>
    public static string Unzip(string zipPath)
    {
        var folder = UniquePath(Path.GetDirectoryName(zipPath)!, Path.GetFileNameWithoutExtension(zipPath));
        ZipFile.ExtractToDirectory(zipPath, folder);
        return folder;
    }
}
