using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Windows.Management.Deployment;

namespace SquareDeck.Services;

/// <summary>
/// Installs or removes Armoury Crate SE with ASUS's own tools (Settings > ROG Ally > ASUS software): downloads the
/// tool's zip from ASUS, unzips it and runs the program inside as administrator. The tool has its own window and does
/// the rest; SquareDeck waits for it to close.
/// </summary>
public static class ArmouryCrate
{
    /// <summary>ASUS's Armoury Crate Uninstall Tool (Armoury_Crate_Uninstall_Tool.zip on ASUS's support pages).</summary>
    private static readonly string[] UninstallUrls =
    {
        "https://dlcdnets.asus.com/pub/ASUS/mb/14Utilities/Armoury_Crate_Uninstall_Tool.zip",
        "https://dlcdnta.asus.com/pub/ASUS/mb/14Utilities/Armoury_Crate_Uninstall_Tool.zip",
    };

    /// <summary>ASUS's Armoury Crate installer (ArmouryCrateInstallTool.zip).</summary>
    private static readonly string[] InstallUrls =
    {
        "https://dlcdnets.asus.com/pub/ASUS/mb/14Utilities/ArmouryCrateInstallTool.zip",
        "https://dlcdnta.asus.com/pub/ASUS/mb/14Utilities/ArmouryCrateInstallTool.zip",
    };

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SquareDeck");
        return http;
    }

    public enum Stage { Downloading, Unzipping, Running, Done }

    /// <summary>Whether Armoury Crate (SE) is installed for this Windows user (it ships as a Store app).</summary>
    public static bool IsInstalled()
    {
        try
        {
            return new PackageManager().FindPackagesForUser(string.Empty)
                .Any(p => p.Id.Name.Contains("ArmouryCrate", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Downloads, unzips and runs ASUS's tool. <paramref name="progress"/> gets the stage, a line about it and how far
    /// the download is (0–1, or null when the size isn't known). Returns null when the tool ran, else why not.
    /// </summary>
    public static async Task<string?> RunAsync(bool uninstall, IProgress<(Stage Stage, string Detail, double? Fraction)> progress, CancellationToken ct)
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-ArmouryCrate", uninstall ? "uninstall" : "install");
        try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (Exception) { }
        Directory.CreateDirectory(work);
        string zip = Path.Combine(work, "tool.zip");

        // 1. Download (from the first ASUS address that answers).
        string? failure = null;
        bool downloaded = false;
        foreach (var url in uninstall ? UninstallUrls : InstallUrls)
        {
            try
            {
                progress.Report((Stage.Downloading, "Connecting to ASUS…", null));
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) { failure = $"ASUS's server answered {(int)response.StatusCode}."; continue; }
                long? total = response.Content.Headers.ContentLength;
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var file = File.Create(zip))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    var lastReport = DateTime.MinValue;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if ((DateTime.Now - lastReport).TotalMilliseconds > 120)
                        {
                            lastReport = DateTime.Now;
                            progress.Report((Stage.Downloading, total is > 0 ? $"{SystemStats.Bytes(done)} of {SystemStats.Bytes(total.Value)}" : SystemStats.Bytes(done),
                                total is > 0 ? done / (double)total.Value : null));
                        }
                    }
                }
                downloaded = true;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { failure = ex.Message; }
        }
        if (!downloaded) return "Couldn't download ASUS's tool. " + (failure ?? "") + " Check the internet connection and try again.";

        // 2. Unzip.
        progress.Report((Stage.Unzipping, "Unpacking ASUS's tool…", null));
        string folder = Path.Combine(work, "tool");
        try { await Task.Run(() => ZipFile.ExtractToDirectory(zip, folder, overwriteFiles: true), ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "The download wasn't a working zip: " + ex.Message; }

        // 3. The program inside: the uninstall tool, or the installer.
        var programs = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories).ToList();
        string? exe = uninstall
            ? programs.FirstOrDefault(p => Path.GetFileName(p).Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
            : programs.FirstOrDefault(p => Path.GetFileName(p).Contains("Install", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
              ?? programs.FirstOrDefault();
        if (exe == null)
            return uninstall ? "The download doesn't contain ASUS's uninstall tool, so nothing was run." : "The download doesn't contain ASUS's installer.";

        // 4. Run it as administrator and wait for it to close.
        progress.Report((Stage.Running, uninstall ? "ASUS's uninstall tool is running. Follow it in its own window." : "ASUS's installer is running. Follow it in its own window.", null));
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe)! });
            if (process == null) return "ASUS's tool didn't start.";
            await process.WaitForExitAsync(ct);
        }
        catch (System.ComponentModel.Win32Exception) { return "The administrator prompt was declined, so nothing was changed."; }
        progress.Report((Stage.Done, "", null));
        return null;
    }
}
