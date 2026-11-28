using System.Diagnostics;
using System.IO;
using System.Text;

namespace SquareDeck.Services;

/// <summary>
/// What's left of the old device helper (8.5.81). Up to 8.5.80 SquareDeck could set up a "SquareDeck\DeviceHelper"
/// scheduled task for Lenovo Legion Go and MSI Claw handhelds and Intel power limits. SquareDeck is now made for the
/// ROG Ally family only, so the helper is gone; this only finds a copy an earlier version set up and removes it
/// (one Windows permission prompt, from Settings > Power).
/// </summary>
public static class DeviceHelper
{
    public const string TaskName = @"SquareDeck\DeviceHelper";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SquareDeck", "Device");

    /// <summary>An old copy is still set up.</summary>
    public static bool Installed
    {
        get
        {
            try { return File.Exists(Path.Combine(InstallDir, "device-helper.ps1")); }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Removes the old scheduled task and its folder. Null when it's gone, else why not.</summary>
    public static async Task<string?> UninstallAsync()
    {
        string work = Path.Combine(Path.GetTempPath(), "SquareDeck-Device-remove.ps1");
        await File.WriteAllTextAsync(work, $$"""
            schtasks.exe /Delete /F /TN '{{TaskName}}' 2>$null | Out-Null
            Remove-Item -LiteralPath '{{InstallDir.Replace("'", "''")}}' -Recurse -Force -ErrorAction SilentlyContinue
            exit 0
            """, new UTF8Encoding(true));
        int code = await RunElevatedAsync(work);
        try { File.Delete(work); } catch (Exception) { }
        return code == -1 ? "Windows' permission prompt was declined." : Installed ? "Some files couldn't be removed." : null;
    }

    private static async Task<int> RunElevatedAsync(string scriptPath)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process == null) return -1;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }
}
