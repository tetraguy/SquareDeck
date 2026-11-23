using System.Diagnostics;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// ASUS's "ASUS Optimization" Windows service (installed with Armoury Crate SE and the MyASUS drivers on the ROG Ally
/// family). It handles the Ally's special buttons and puts back ASUS's own power limits, so with it running, the
/// buttons SquareDeck maps and the TDP SquareDeck sets can be taken over. Settings can switch it off (and back on).
/// Changing a service needs administrator rights, so Windows asks first.
/// </summary>
public static class AsusOptimization
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>The service's name on this PC, found by its name or by "ASUS Optimization" as its display name; null when it isn't installed.</summary>
    public static string? ServiceName
    {
        get
        {
            try
            {
                using var services = Registry.LocalMachine.OpenSubKey(ServicesKey);
                if (services == null) return null;
                foreach (var name in services.GetSubKeyNames())
                {
                    if (name.Equals("ASUSOptimization", StringComparison.OrdinalIgnoreCase)) return name;
                    using var key = services.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is string display && display.Trim().Equals("ASUS Optimization", StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }
            catch (Exception) { }
            return null;
        }
    }

    public static bool Installed => ServiceName != null;

    /// <summary>Whether Windows starts it (its start type isn't Disabled).</summary>
    public static bool Enabled
    {
        get
        {
            if (ServiceName is not { } name) return false;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + "\\" + name);
                return key?.GetValue("Start") is not int start || start != 4;
            }
            catch (Exception) { return true; }
        }
    }

    /// <summary>Whether it's running right now.</summary>
    public static bool Running
    {
        get
        {
            try { return Process.GetProcessesByName("AsusOptimization").Length > 0; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Switches it off (stopped, and not started with Windows) or back on (automatic, and started). Null if it worked, else why not.</summary>
    public static async Task<string?> SetEnabledAsync(bool on)
    {
        if (ServiceName is not { } name) return "The ASUS Optimization service isn't installed.";
        string output = on
            ? await Maintenance.RunElevatedAsync($"sc config \"{name}\" start= auto", $"sc start \"{name}\"")
            : await Maintenance.RunElevatedAsync($"sc stop \"{name}\"", $"sc config \"{name}\" start= disabled");
        if (output.Contains("declined", StringComparison.OrdinalIgnoreCase)) return output;
        return Enabled == on ? null : "Windows didn't change the service: " + output;
    }
}
