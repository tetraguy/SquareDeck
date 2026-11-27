using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SquareDeck.Services;

public static class ProcessUtil
{
    private const uint QueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static string? GetImagePath(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Whether a program with this name (no ".exe") is running. What it finds is let go straight away.</summary>
    public static bool IsNamedRunning(string name)
    {
        var found = Process.GetProcessesByName(name);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    /// <summary>Ends every process whose executable lives under <paramref name="directory"/>.</summary>
    /// <returns>How many processes were ended.</returns>
    public static int KillProcessesUnder(string directory)
    {
        var prefix = System.IO.Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        int killed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = GetImagePath(p.Id);
                if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
                killed++;
            }
            catch (Exception)
            {
                // Already exited, or protected: nothing more we can do for this one.
            }
            finally
            {
                p.Dispose();
            }
        }
        return killed;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>
    /// Brings a running program's window to the front: the one program at <paramref name="exePath"/>,
    /// or any program under <paramref name="directory"/>. Returns false if no window was found.
    /// </summary>
    public static bool FocusWindow(string? exePath, string? directory)
    {
        string? prefix = string.IsNullOrEmpty(directory) ? null : directory.TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var handle = p.MainWindowHandle;
                if (handle == IntPtr.Zero || !IsWindowVisible(handle)) continue; // helpers and crash handlers have no real window
                var path = GetImagePath(p.Id);
                if (path == null) continue;
                bool match = exePath != null
                    ? string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)
                    : prefix != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;

                const int Restore = 9;
                if (IsIconic(handle)) ShowWindow(handle, Restore);
                SetForegroundWindow(handle);
                return true;
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);

    /// <summary>
    /// True once the game shows a real window: a visible, game-sized main window belonging to the program at
    /// <paramref name="exePath"/> or to any program under <paramref name="directory"/>. (Splash screens of the
    /// game itself count; small helper and crash-reporter windows don't.)
    /// </summary>
    public static bool HasGameWindow(string? exePath, string? directory) => FindGameWindowProgram(exePath, directory) != null;

    /// <summary>The program that owns such a window, or null.</summary>
    public static string? FindGameWindowProgram(string? exePath, string? directory)
    {
        string? prefix = string.IsNullOrEmpty(directory) ? null : directory.TrimEnd('\\') + "\\";
        if (exePath == null && prefix == null) return null;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var handle = p.MainWindowHandle;
                if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle)) continue;
                if (!GetWindowRect(handle, out var r) || r.Right - r.Left < 400 || r.Bottom - r.Top < 300) continue;
                var path = GetImagePath(p.Id);
                if (path == null) continue;
                if (exePath != null ? string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)
                                    : path.StartsWith(prefix!, StringComparison.OrdinalIgnoreCase)) return path;
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Hands memory this program isn't actively using back to Windows (it pages back in when needed).</summary>
    public static void TrimWorkingSet()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            EmptyWorkingSet(me.Handle);
        }
        catch (Exception) { }
    }

    /// <summary>True while the program at <paramref name="exePath"/>, or any program under <paramref name="directory"/>, is running.</summary>
    public static bool IsRunning(string? exePath, string? directory)
    {
        string? prefix = string.IsNullOrEmpty(directory) ? null : directory.TrimEnd('\\') + "\\";
        foreach (var path in GetRunningImagePaths())
        {
            if (exePath != null ? string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)
                                : prefix != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Full paths of every running program we are allowed to look at.</summary>
    public static List<string> GetRunningImagePaths()
    {
        var paths = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = GetImagePath(p.Id);
                if (path != null) paths.Add(path);
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return paths;
    }

    /// <summary>Process ids of the program at <paramref name="exePath"/>, or of every program under <paramref name="directory"/>.</summary>
    public static List<int> PidsFor(string? exePath, string? directory)
    {
        var pids = new List<int>();
        string? prefix = string.IsNullOrEmpty(directory) ? null : System.IO.Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = GetImagePath(p.Id);
                if (path == null) continue;
                if (exePath != null ? string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)
                                    : prefix != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) pids.Add(p.Id);
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return pids;
    }

    /// <summary>Ends every running copy of one program.</summary>
    public static int KillByPath(string exePath)
    {
        int killed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = GetImagePath(p.Id);
                if (path == null || !string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
                killed++;
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return killed;
    }

    /// <summary>Runs a PowerShell script hidden and returns what it printed.</summary>
    public static async Task<string> RunPowerShellAsync(string script, CancellationToken ct = default)
    {
        // Encoded so quoting inside the script can never be mangled on the way in.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script));
        var (_, output) = await RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, ct).ConfigureAwait(false);
        return output;
    }

    /// <summary>
    /// Opens a URL, protocol link or file with its default handler. Web links go to SquareDeck's own browser when
    /// Settings allows (see <see cref="WebLinks"/>).
    /// </summary>
    public static bool Open(string target, string? arguments = null)
    {
        if (arguments == null && SteamFullscreen(target)) return true;
        if (arguments == null && WebLinks.TryInside(target)) return true;
        return OpenOutside(target, arguments);
    }

    /// <summary>
    /// Experimental "Open Steam in fullscreen": a Steam page (store, library, friends, settings…) opens in Steam's
    /// Big Picture mode. Starting games, closing Steam and its file checks are left as they are.
    /// </summary>
    private static bool SteamFullscreen(string target)
    {
        if (!Labs.On("steamfullscreen") || !target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = target[8..].ToLowerInvariant();
        if (rest.StartsWith("rungameid") || rest.StartsWith("run/") || rest.StartsWith("launch") || rest.StartsWith("exit") || rest.StartsWith("validate") ||
            rest.StartsWith("open/bigpicture") || rest.StartsWith("flushconfig") || rest.StartsWith("forceinputappid")) return false;
        OpenOutside("steam://open/bigpicture");
        // Once Big Picture is up, the page opens inside it.
        _ = Task.Delay(SteamLibrary.ClientRunning() ? 900 : 5000).ContinueWith(_ => OpenOutside(target));
        return true;
    }

    /// <summary>Opens with Windows' default handler, whatever the browser setting (Windows' own browser for links).</summary>
    public static bool OpenOutside(string target, string? arguments = null)
    {
        try
        {
            var psi = arguments == null
                ? new ProcessStartInfo(target) { UseShellExecute = true }
                : new ProcessStartInfo(target, arguments) { UseShellExecute = true };
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts a program (or shortcut) from its own folder, as if double-clicked.</summary>
    public static bool Start(string path, string? arguments = null)
    {
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(arguments)) psi.Arguments = arguments;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Runs a console program hidden and captures its standard output.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + file);
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        await stderr.ConfigureAwait(false);
        return (p.ExitCode, output);
    }
}
