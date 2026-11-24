using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// ASUS "GameVisual" display modes (Default, Racing, Scenery, RTS/RPG, FPS, Cinema, Vivid, Eyecare, Disabled) and the
/// color temperature, through ASUS's own AsusSplendid tool, which comes with the ASUS System Control Interface driver
/// (it needs the ASUS Optimization service). The command numbers were documented by G-Helper (github.com/seerge/g-helper);
/// this is SquareDeck's own code.
/// </summary>
public static class VisualModes
{
    public sealed record Mode(int Id, string Name, string About, bool UsesTemperature);

    public static readonly Mode[] Modes =
    {
        new(11, "Default", "The screen as ASUS tuned it", true),
        new(21, "Racing", "Brighter and punchier, for racing games", true),
        new(22, "Scenery", "Richer greens and blues, for open worlds and landscapes", true),
        new(23, "RTS/RPG", "Sharper detail and color, for strategy and role-playing games", true),
        new(24, "FPS", "Lifts dark areas, to spot enemies in the shadows", true),
        new(25, "Cinema", "Warmer and deeper, for films and story games", true),
        new(13, "Vivid", "The most saturated colors", true),
        new(17, "Eyecare", "Less blue light, for long sessions and night play", false),
        new(18, "Disabled", "No GameVisual processing at all", false),
    };

    public static readonly (int Value, string Name)[] Temperatures =
    {
        (0, "Warmest"), (15, "Warmer"), (30, "Warm"), (50, "Neutral"), (70, "Cold"), (85, "Colder"), (100, "Coldest"),
    };

    public const int DefaultMode = 11, Neutral = 50;
    private const int InitCommand = 10;

    public static int SavedMode => int.TryParse(Labs.Get("visual.mode"), out var m) && Modes.Any(x => x.Id == m) ? m : DefaultMode;
    public static int SavedTemperature => int.TryParse(Labs.Get("visual.temp"), out var t) ? Math.Clamp(t, 0, 100) : Neutral;

    private static string? _folder;
    private static bool _looked;

    /// <summary>The folder of ASUS's ACPI driver (ATKWMIACPIIO), where AsusSplendid.exe lives.</summary>
    private static string? Folder()
    {
        if (_looked) return _folder;
        _looked = true;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ATKWMIACPIIO");
            if (key?.GetValue("ImagePath") is string image)
            {
                string path = image.Trim('"');
                if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path[4..];
                if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path[12..]);
                else if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
                path = Environment.ExpandEnvironmentVariables(path);
                _folder = Path.GetDirectoryName(path);
            }
        }
        catch (Exception) { }
        return _folder;
    }

    public static string? Program => Folder() is { } f && File.Exists(Path.Combine(f, "AsusSplendid.exe")) ? Path.Combine(f, "AsusSplendid.exe") : null;

    /// <summary>Whether visual modes can be changed here (an ASUS device with its display tool installed).</summary>
    public static bool Available => Program != null;

    private static async Task<string> RunAsync(params int?[] args)
    {
        if (Program is not { } exe) return "";
        try
        {
            var psi = new ProcessStartInfo(exe, string.Join(" ", args.Select(a => a?.ToString() ?? "")).TrimEnd())
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = p.StandardOutput.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { p.Kill(); } catch (Exception) { } }
            return await output;
        }
        catch (Exception) { return ""; }
    }

    /// <summary>
    /// Sets a mode and color temperature. Null when it went through; otherwise why not, in a sentence. Remembered, so it's
    /// put back when SquareDeck starts.
    /// </summary>
    public static async Task<string?> ApplyAsync(int mode, int temperature)
    {
        if (!Available) return "This device doesn't have ASUS's display tool (it comes with the ASUS System Control Interface driver).";
        if (AmdOled.IsOn() == true)
            return "AMD's OLED Power Optimization is on, and while it is, visual modes have no effect. Turn it off on this page (or in AMD Software: Display), then pick the mode again.";
        Labs.Put("visual.mode", mode.ToString(), save: false);
        Labs.Put("visual.temp", temperature.ToString());
        // Disabled: first value 2. Eyecare: its own fixed level. The rest: the color temperature.
        int? p1 = mode == 18 ? 2 : 0;
        int? p2 = mode == 18 ? null : mode == 17 ? 4 : temperature;
        string result = await RunAsync(mode, p1, p2);
        if (result.Contains("Visual is disabled", StringComparison.OrdinalIgnoreCase))
        {
            // GameVisual was turned off (Armoury Crate's switch): turn it back on, wake it, try again.
            try { Registry.SetValue(@"HKEY_CURRENT_USER\Software\ASUS\ARMOURY CRATE Service\GameVisual", "ActiveGVStatus", 1, RegistryValueKind.DWord); } catch (Exception) { }
            await RunAsync(InitCommand);
            result = await RunAsync(mode, p1, p2);
        }
        else if (result.Contains("file not exist", StringComparison.OrdinalIgnoreCase) || result.Trim().Length == 0)
        {
            await RunAsync(InitCommand);
            result = await RunAsync(mode, p1, p2);
        }
        if (result.Contains("return code: -1", StringComparison.OrdinalIgnoreCase))
        {
            await RunAsync(DefaultMode, 0, Neutral);
            Labs.Put("visual.mode", DefaultMode.ToString(), save: false);
            Labs.Put("visual.temp", Neutral.ToString());
            return "The display refused that mode, so it was put back to Default. (Visual modes don't work while HDR is on, and need the ASUS Optimization service.)";
        }
        return null;
    }
}

/// <summary>
/// AMD "OLED Power Optimization" (also called SCE): on OLED screens such as the ROG Xbox Ally X20's, AMD's driver can
/// manage the panel's colors to save power, and while it does, GameVisual modes have no effect. G-Helper documented
/// how to read and turn it off through AMD's display library (atiadlxx.dll); this is SquareDeck's own code.
/// </summary>
public static class AmdOled
{
    private const string Dll = "atiadlxx.dll";
    private const int Ok = 0;

    private delegate nint Alloc(int size);
    private static readonly Alloc AllocKeep = size => System.Runtime.InteropServices.Marshal.AllocHGlobal(size);

    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Main_Control_Create(Alloc callback, int connectedOnly, out nint context);
    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Main_Control_Destroy(nint context);
    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Adapter_NumberOfAdapters_Get(nint context, out int count);
    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Display_DisplayInfo_Get(nint context, int adapter, out int count, out nint info, int forceDetect);
    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Display_SCE_State_Get(nint context, int adapter, int display, out int state, out int defaultState);
    [System.Runtime.InteropServices.DllImport(Dll)] private static extern int ADL2_Display_SCE_State_Set(nint context, int adapter, int display, int state);

    // One ADLDisplayInfo: display id (4 ints), controller index (int), two 256-char names, then 5 ints; the last is the info value.
    private const int NameLength = 256;
    private const int InfoSize = 4 * 4 + 4 + NameLength * 2 + 5 * 4;
    private const int LogicalIndexOffset = 0;
    private const int InfoValueOffset = InfoSize - 4;

    /// <summary>Runs <paramref name="work"/> for the display whose OLED optimization AMD's library can read, with its state.</summary>
    private static T WithPanel<T>(Func<nint, int, int, int, T> work, T fallback)
    {
        nint context = 0;
        try
        {
            if (ADL2_Main_Control_Create(AllocKeep, 1, out context) != Ok || context == 0) return fallback;
            if (ADL2_Adapter_NumberOfAdapters_Get(context, out int adapters) != Ok) return fallback;
            for (int a = 0; a < adapters; a++)
            {
                if (ADL2_Display_DisplayInfo_Get(context, a, out int count, out nint info, 0) != Ok || info == 0) continue;
                try
                {
                    for (int d = 0; d < count; d++)
                    {
                        nint entry = info + d * InfoSize;
                        int value = System.Runtime.InteropServices.Marshal.ReadInt32(entry, InfoValueOffset);
                        if ((value & 1) == 0) continue; // not connected
                        int display = System.Runtime.InteropServices.Marshal.ReadInt32(entry, LogicalIndexOffset);
                        if (ADL2_Display_SCE_State_Get(context, a, display, out int state, out _) == Ok)
                            return work(context, a, display, state);
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(info); }
            }
        }
        catch (Exception) { } // no AMD driver, or a different one
        finally { if (context != 0) try { ADL2_Main_Control_Destroy(context); } catch (Exception) { } }
        return fallback;
    }

    /// <summary>On, off, or null when it can't be read (no AMD graphics, or a screen without it).</summary>
    public static bool? IsOn()
    {
        bool? live = WithPanel<bool?>((_, _, _, state) => state > 1, null);
        return live ?? FromRegistry();
    }

    /// <summary>The older way to read it: AMD's driver keeps the switch in DAL_SCE_Settings (byte 4, bit 2).</summary>
    private static bool? FromRegistry()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            foreach (var name in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var path = cls.OpenSubKey(name + @"\DAL2_DATA__2_0\DisplayPath_0");
                if (path?.GetValue("DAL_SCE_Settings") is byte[] data && data.Length > 4) return (data[4] & 0x02) != 0;
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Turns it off (as AMD Software's own switch does). True when it went through.</summary>
    public static bool TurnOff() => WithPanel((context, a, display, _) => ADL2_Display_SCE_State_Set(context, a, display, 1) == Ok, false);
}
