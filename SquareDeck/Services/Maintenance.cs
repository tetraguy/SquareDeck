using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace SquareDeck.Services;

/// <summary>
/// Settings > System and tools > Disk cleanup and Network troubleshooting: things to clear and the usual network fixes
/// (ipconfig, netsh), in plain words. Anything Windows only allows administrators to do asks for permission first.
/// </summary>
public static class Maintenance
{
    // ================================================================ disk cleanup

    /// <summary>One kind of leftover files. Folders are emptied, not removed; files in use are skipped.</summary>
    public sealed record CleanupItem(string Id, string Name, string About, string Glyph, string[] Folders, bool OnByDefault,
                                      TimeSpan MinAge = default, bool RecycleBin = false)
    {
        public long Bytes { get; set; } = -1;
        public bool Selected { get; set; } = OnByDefault;
    }

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static List<CleanupItem> CleanupItems()
    {
        string steam = SteamLibrary.FindSteamPath() ?? "";
        var list = new List<CleanupItem>
        {
            new("temp", "Temporary files", "Left behind by apps and installers (only files older than a day)", "",
                new[] { Path.GetTempPath() }, true, TimeSpan.FromDays(1)),
            new("recycle", "Recycle Bin", "Everything in it is deleted for good", "", Array.Empty<string>(), false, RecycleBin: true),
            new("crash", "Crash reports", "Dumps saved when apps or games crashed", "",
                new[] { Path.Combine(Local, "CrashDumps"), Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportArchive"),
                        Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportQueue") }, true),
            new("shaders", "Graphics shader caches", "DirectX, AMD and NVIDIA. Games rebuild them, so the first minutes may stutter", "",
                new[] { Path.Combine(Local, "D3DSCache"), Path.Combine(Local, "AMD", "DxCache"), Path.Combine(Local, "AMD", "DxcCache"),
                        Path.Combine(Local, "AMD", "VkCache"), Path.Combine(Local, "AMD", "GLCache"), Path.Combine(Local, "NVIDIA", "DXCache"),
                        Path.Combine(Local, "NVIDIA", "GLCache") }, false),
            new("thumbs", "Thumbnail cache", "Small pictures File Explorer makes again when needed", "",
                new[] { Path.Combine(Local, "Microsoft", "Windows", "Explorer") }, false),
            new("web", "SquareDeck browser cache", "Saved pages and pictures from the built-in browser (sign-ins are kept)", "",
                new[] { Path.Combine(Settings.DataDir, "Browser", "EBWebView", "Default", "Cache"),
                        Path.Combine(Settings.DataDir, "Browser", "EBWebView", "Default", "Code Cache"),
                        Path.Combine(Settings.DataDir, "Browser", "EBWebView", "Default", "GPUCache") }, true),
            new("art", "SquareDeck artwork cache", "Downloaded covers and artwork (your own picks are kept); they download again", "",
                new[] { SteamGridDbClient.CoverDir, SteamGridDbClient.HeroDir, SteamGridDbClient.LogoDir, SteamGridDbClient.PortraitDir,
                        SteamGridDbClient.IconDir, SteamGridDbClient.AnimatedDir }, false),
        };
        if (steam.Length > 0)
            list.Add(new("steam", "Steam web cache", "Store and community pages Steam saved (close Steam first for all of it)", "",
                new[] { Path.Combine(steam, "appcache", "httpcache"), Path.Combine(steam, "depotcache") }, false));
        return list;
    }

    private static IEnumerable<FileInfo> FilesOf(CleanupItem item)
    {
        foreach (var folder in item.Folders)
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<FileInfo> files;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = item.Id != "thumbs", IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
                files = new DirectoryInfo(folder).EnumerateFiles(item.Id == "thumbs" ? "thumbcache_*.db" : "*", options);
            }
            catch (Exception) { continue; }
            foreach (var f in files)
            {
                if (item.MinAge > TimeSpan.Zero)
                {
                    DateTime touched;
                    try { touched = f.LastWriteTime; } catch (Exception) { continue; }
                    if (DateTime.Now - touched < item.MinAge) continue;
                }
                yield return f;
            }
        }
    }

    /// <summary>How much space an item takes, in bytes.</summary>
    public static long Measure(CleanupItem item)
    {
        if (item.RecycleBin)
        {
            var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }
        long total = 0;
        foreach (var f in FilesOf(item))
        {
            try { total += f.Length; } catch (Exception) { }
        }
        return total;
    }

    /// <summary>Deletes what it can of an item; returns the bytes freed. Files in use are left alone.</summary>
    public static long Clean(CleanupItem item)
    {
        if (item.RecycleBin)
        {
            long before = Measure(item);
            const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
            SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
            return Math.Max(0, before - Measure(item));
        }
        long freed = 0;
        foreach (var f in FilesOf(item).ToList())
        {
            try
            {
                long size = f.Length;
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
                freed += size;
            }
            catch (Exception) { } // in use or protected: skipped
        }
        // Empty folders left behind (not the top folders themselves).
        foreach (var folder in item.Folders.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
        return freed;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 0 => "…",
        < 1024 * 1024 => $"{Math.Max(0, bytes / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB"
    };

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ShQueryRbInfo { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref ShQueryRbInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    // ================================================================ network

    /// <summary>The adapters in use, with their addresses, gateway and DNS, in plain words.</summary>
    public static string ConnectionSummary()
    {
        var sb = new StringBuilder();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                 && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
        {
            var ip = nic.GetIPProperties();
            sb.AppendLine(nic.Name + "  (" + nic.Description + ")");
            foreach (var a in ip.UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                sb.AppendLine("  IPv4 address    " + a.Address + (a.IPv4Mask != null ? "  /  " + a.IPv4Mask : ""));
            foreach (var a in ip.UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Take(2))
                sb.AppendLine("  IPv6 address    " + a.Address);
            foreach (var g in ip.GatewayAddresses.Where(g => !g.Address.Equals(System.Net.IPAddress.Any)))
                sb.AppendLine("  Gateway         " + g.Address);
            foreach (var d in ip.DnsAddresses.Take(3)) sb.AppendLine("  DNS server      " + d);
            try { if (ip.GetIPv4Properties() is { IsDhcpEnabled: var dhcp }) sb.AppendLine("  Address from    " + (dhcp ? "the router (DHCP)" : "set by hand")); } catch (Exception) { }
            if (nic.Speed > 0) sb.AppendLine("  Link speed      " + (nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1e9:0.#} Gbps" : $"{nic.Speed / 1e6:0} Mbps"));
            var mac = nic.GetPhysicalAddress().ToString();
            if (mac.Length == 12) sb.AppendLine("  MAC address     " + string.Join("-", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))));
            sb.AppendLine();
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No network connection is active.";
    }

    /// <summary>Pings the router, a public address and a website, and says in a line or two what that means.</summary>
    public static async Task<string> DiagnoseAsync()
    {
        var sb = new StringBuilder();
        string? gateway = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))?.ToString();

        async Task<(int Ok, long Avg)> Ping(string host)
        {
            using var p = new Ping();
            int ok = 0; long sum = 0;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var reply = await p.SendPingAsync(host, 1500);
                    if (reply.Status == IPStatus.Success) { ok++; sum += reply.RoundtripTime; }
                }
                catch (Exception) { }
            }
            return (ok, ok > 0 ? sum / ok : 0);
        }
        string Line(string what, (int Ok, long Avg) r) =>
            $"{what,-26}{(r.Ok == 0 ? "no answer" : $"{r.Avg} ms, {4 - r.Ok} of 4 lost")}";

        var router = gateway != null ? await Ping(gateway) : (0, 0);
        var internet = await Ping("1.1.1.1");
        var site = await Ping("www.microsoft.com");
        sb.AppendLine(Line("Your router" + (gateway != null ? $" ({gateway})" : ""), router));
        sb.AppendLine(Line("The internet (1.1.1.1)", internet));
        sb.AppendLine(Line("A website by name", site));
        sb.AppendLine();
        sb.AppendLine(gateway == null ? "Not connected to a network. Check Wi-Fi or the cable."
            : router.Item1 == 0 ? "The router doesn't answer. Restart the router, or reconnect to Wi-Fi."
            : internet.Item1 == 0 ? "The router answers but the internet doesn't. The problem is likely the router or your provider."
            : site.Item1 == 0 ? "The internet works but names don't resolve. Try \"Flush DNS cache\", or a different DNS server."
            : internet.Item2 > 120 || internet.Item1 < 4 ? "Connected, but slow or dropping packets. Move closer to the router, or try a cable."
            : "Everything answers normally.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Runs a command and returns what it printed.</summary>
    public static async Task<string> RunAsync(string file, string arguments)
    {
        var (_, output) = await ProcessUtil.RunAsync(file, arguments).ConfigureAwait(false);
        return output.Trim();
    }

    /// <summary>
    /// Runs commands as administrator (Windows asks first) and returns what they printed, or a line saying the prompt
    /// was declined.
    /// </summary>
    public static async Task<string> RunElevatedAsync(params string[] commands)
    {
        Directory.CreateDirectory(Settings.DataDir);
        string id = Guid.NewGuid().ToString("N")[..8];
        string script = Path.Combine(Settings.DataDir, $"net-{id}.cmd"), output = Path.Combine(Settings.DataDir, $"net-{id}.txt");
        var lines = new StringBuilder("@echo off\r\nchcp 65001 >nul\r\n");
        foreach (var c in commands) lines.Append(c).Append(" >> \"").Append(output).Append("\" 2>&1\r\n");
        await File.WriteAllTextAsync(script, lines.ToString()).ConfigureAwait(false);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (process == null) return "Couldn't start the command.";
            await process.WaitForExitAsync().ConfigureAwait(false);
            return File.Exists(output) ? (await File.ReadAllTextAsync(output).ConfigureAwait(false)).Trim() : "Done.";
        }
        catch (System.ComponentModel.Win32Exception) { return "The administrator prompt was declined, so nothing was changed."; }
        finally
        {
            try { File.Delete(script); } catch (Exception) { }
            try { File.Delete(output); } catch (Exception) { }
        }
    }
}
