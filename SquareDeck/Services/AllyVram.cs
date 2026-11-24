using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// The memory assigned to the Ally's graphics (VRAM: Auto, 4, 6, 8, 12 or 16 GB, depending on the model).
///
/// SquareDeck shows how much the graphics have now and takes you to where it's changed, but doesn't write the setting
/// to the firmware itself: the switch for it on the original Ally (ACPI 0x000600C1) isn't there on the Ally X and
/// Xbox Allys, the values for each size on those models aren't published, and some values of that switch are known
/// to leave the device unable to start (the Linux asus-armoury driver refuses them for that reason). It's changed in
/// Armoury Crate SE (Settings > Operating mode > GPU settings) or in the UEFI setup, and takes effect after a restart.
/// </summary>
public static class AllyVram
{
    public static readonly string[] Sizes = { "Auto", "4 GB", "6 GB", "8 GB", "12 GB", "16 GB" };

    /// <summary>
    /// The graphics memory Windows sees now, in GB (the graphics driver's own figure), or null if it can't be read.
    /// </summary>
    public static double? CurrentGb()
    {
        try
        {
            using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display == null) return null;
            double best = 0;
            foreach (var name in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var adapter = display.OpenSubKey(name);
                if (adapter == null) continue;
                long bytes = adapter.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => adapter.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (uint)i,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0
                    }
                };
                best = Math.Max(best, bytes / 1073741824.0);
            }
            return best > 0.2 ? Math.Round(best, 1) : null;
        }
        catch (Exception) { return null; }
    }

    public static string CurrentText() => CurrentGb() is double gb ? (gb % 1 == 0 ? $"{gb:0} GB" : $"{gb:0.#} GB") : "Unknown";

    /// <summary>Restarts straight into the UEFI setup, where "UMA frame buffer size" is set (Windows asks first).</summary>
    public static Task<string> RestartToSetupAsync() => Maintenance.RunElevatedAsync("shutdown /r /fw /t 3");
}
