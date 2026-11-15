using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquareDeck.Services;

public sealed record DisplayMode(int Width, int Height, int RefreshRate)
{
    public string Resolution => $"{Width} × {Height}";
}

/// <summary>Resolution and refresh rate of the main display.</summary>
public static class DisplaySettings
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode mode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DevMode mode, int flags);

    private const int CurrentSettings = -1;
    private const int FieldWidth = 0x80000, FieldHeight = 0x100000, FieldFrequency = 0x400000;
    private const int UpdateRegistry = 0x1;

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    public static DisplayMode? Current()
    {
        var dm = NewMode();
        return EnumDisplaySettings(null, CurrentSettings, ref dm)
            ? new DisplayMode(dm.PelsWidth, dm.PelsHeight, dm.DisplayFrequency)
            : null;
    }

    /// <summary>Every mode the display reports (full color only), largest first.</summary>
    public static List<DisplayMode> All()
    {
        var modes = new HashSet<DisplayMode>();
        var dm = NewMode();
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.BitsPerPel >= 32 && dm.PelsWidth >= 800 && dm.PelsHeight >= 600)
                modes.Add(new DisplayMode(dm.PelsWidth, dm.PelsHeight, dm.DisplayFrequency));
        }
        return modes
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.Width)
            .ThenByDescending(m => m.RefreshRate)
            .ToList();
    }

    /// <summary>
    /// Switches the display. With <paramref name="keep"/> false the change lasts only until the next
    /// sign-in, which is how a change is tried out before it is confirmed.
    /// </summary>
    public static bool Apply(DisplayMode mode, bool keep)
    {
        var dm = NewMode();
        if (!EnumDisplaySettings(null, CurrentSettings, ref dm)) return false;
        dm.PelsWidth = mode.Width;
        dm.PelsHeight = mode.Height;
        dm.DisplayFrequency = mode.RefreshRate;
        dm.Fields = FieldWidth | FieldHeight | FieldFrequency;
        return ChangeDisplaySettings(ref dm, keep ? UpdateRegistry : 0) == 0;
    }
}

/// <summary>Screen-off and sleep timers and the power plan, through Windows' powercfg tool.</summary>
public static class PowerSettings
{
    public static readonly int[] MinuteChoices = { 1, 2, 5, 10, 15, 30, 60, 120, 0 };

    public static string Describe(int minutes) =>
        minutes <= 0 ? "Never" : minutes < 60 ? minutes + " min" : minutes / 60 + (minutes == 60 ? " hour" : " hours");

    /// <summary>Timeout in minutes (0 = never) on mains power and on battery. Null if it can't be read.</summary>
    private static async Task<(int PluggedIn, int Battery)?> QueryAsync(string subGroup, string setting)
    {
        try
        {
            var (_, output) = await ProcessUtil.RunAsync("powercfg.exe", $"/query SCHEME_CURRENT {subGroup} {setting}");
            // The labels are translated but the values aren't: the last two hex numbers are AC then DC, in seconds.
            var values = Regex.Matches(output, @"0x[0-9a-fA-F]{8}").Select(m => Convert.ToInt32(m.Value, 16)).ToList();
            if (values.Count < 2) return null;
            return (values[^2] / 60, values[^1] / 60);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Task<(int PluggedIn, int Battery)?> GetScreenTimeoutAsync() => QueryAsync("SUB_VIDEO", "VIDEOIDLE");

    public static Task<(int PluggedIn, int Battery)?> GetSleepTimeoutAsync() => QueryAsync("SUB_SLEEP", "STANDBYIDLE");

    /// <param name="what">"monitor" or "standby".</param>
    public static async Task SetAsync(string what, bool onBattery, int minutes)
    {
        try
        {
            await ProcessUtil.RunAsync("powercfg.exe", $"/change {what}-timeout-{(onBattery ? "dc" : "ac")} {Math.Max(0, minutes)}");
        }
        catch (Exception) { }
    }

    /// <summary>Power plans on this PC: id, name, and whether it is the active one.</summary>
    public static async Task<List<(string Id, string Name, bool Active)>> GetPlansAsync()
    {
        var plans = new List<(string, string, bool)>();
        try
        {
            var (_, output) = await ProcessUtil.RunAsync("powercfg.exe", "/list");
            foreach (Match m in Regex.Matches(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})\s+\((.+?)\)(\s*\*)?"))
                plans.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success));
        }
        catch (Exception) { }
        return plans;
    }

    public static async Task SetPlanAsync(string id)
    {
        try { await ProcessUtil.RunAsync("powercfg.exe", "/setactive " + id); }
        catch (Exception) { }
    }
}

public sealed record ExternalDrive(string Letter, string Label, string FileSystem, long Size, long Free)
{
    public string Title => $"{Letter}:  {(Label.Length > 0 ? Label : "No name")}";
}

/// <summary>USB drives and memory cards, and formatting them.</summary>
public static class ExternalDrives
{
    public static readonly (string Id, string Name, string About)[] FileSystems =
    {
        ("exFAT", "exFAT", "Best for memory cards and USB drives. Works on Windows, Mac, consoles and handhelds."),
        ("NTFS", "NTFS", "Best for installing PC games on. Windows only."),
        ("FAT32", "FAT32", "Oldest and most compatible, but only for drives up to 32 GB and files up to 4 GB.")
    };

    private static string SystemLetter => Path.GetPathRoot(Environment.SystemDirectory)?[..1].ToUpperInvariant() ?? "C";

    /// <summary>
    /// Drives that are safe to offer for formatting: removable ones and those on a USB / SD connection.
    /// The Windows drive is never listed.
    /// </summary>
    public static async Task<List<ExternalDrive>> ListAsync()
    {
        var drives = new Dictionary<string, ExternalDrive>();

        // Windows flags sticks and cards as "removable" itself.
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Removable || !d.IsReady) continue;
                var letter = d.Name[..1].ToUpperInvariant();
                drives[letter] = new ExternalDrive(letter, d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace);
            }
        }
        catch (Exception) { }

        // External hard drives look like ordinary disks, so ask which disks are attached by USB or card reader.
        try
        {
            const string script =
                "$out = Get-Disk | Where-Object { $_.BusType -in 'USB','SD','MMC' } | Get-Partition | " +
                "Where-Object { $_.DriveLetter } | ForEach-Object { $v = Get-Volume -DriveLetter $_.DriveLetter; " +
                "[pscustomobject]@{ Letter=[string]$_.DriveLetter; Label=[string]$v.FileSystemLabel; " +
                "FileSystem=[string]$v.FileSystem; Size=[int64]$v.Size; Free=[int64]$v.SizeRemaining } }; " +
                "ConvertTo-Json -Compress -InputObject @($out)";
            var output = await ProcessUtil.RunPowerShellAsync(script);
            int start = output.IndexOf('[');
            if (start >= 0)
            {
                using var doc = JsonDocument.Parse(output[start..]);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var letter = (e.GetProperty("Letter").GetString() ?? "").ToUpperInvariant();
                    if (letter.Length != 1) continue;
                    drives[letter] = new ExternalDrive(letter,
                        e.GetProperty("Label").GetString() ?? "", e.GetProperty("FileSystem").GetString() ?? "",
                        e.GetProperty("Size").GetInt64(), e.GetProperty("Free").GetInt64());
                }
            }
        }
        catch (Exception) { }

        drives.Remove(SystemLetter);
        return drives.Values.OrderBy(d => d.Letter).ToList();
    }

    /// <summary>
    /// Erases a drive and gives it a new file system. Windows asks for administrator approval.
    /// Returns null on success, otherwise a message to show.
    /// </summary>
    public static async Task<string?> FormatAsync(ExternalDrive drive, string fileSystem)
    {
        // Belt and braces: a single letter, a known file system, and never the Windows drive.
        if (drive.Letter.Length != 1 || !char.IsLetter(drive.Letter[0])) return "That is not a drive letter.";
        if (drive.Letter.Equals(SystemLetter, StringComparison.OrdinalIgnoreCase)) return "The Windows drive can't be formatted.";
        if (FileSystems.All(f => f.Id != fileSystem)) return "Unknown file system.";

        var label = Regex.Replace(drive.Label, @"[^A-Za-z0-9 _-]", "");
        if (label.Length > 11) label = label[..11];
        var script =
            $"try {{ Format-Volume -DriveLetter {drive.Letter} -FileSystem {fileSystem} " +
            $"-NewFileSystemLabel '{label}' -Confirm:$false -ErrorAction Stop | Out-Null; exit 0 }} catch {{ exit 1 }}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = "runas",                 // formatting needs administrator rights
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (p == null) return "Couldn't start the format.";
            await p.WaitForExitAsync();
            return p.ExitCode == 0
                ? null
                : "Windows couldn't format the drive. It may be write-protected, in use, or too large for " + fileSystem + ".";
        }
        catch (Win32Exception)
        {
            return "Formatting was cancelled at the Windows permission prompt.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
