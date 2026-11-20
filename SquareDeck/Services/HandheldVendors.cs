namespace SquareDeck.Services;

/// <summary>
/// Lenovo Legion Go and MSI Claw settings, through the device helper (their WMI methods need administrator rights).
/// The values follow HandheldCompanion by Valkirie (Devices/Lenovo/LegionGo.cs, Devices/MSI/ClawA1M.cs), licensed
/// CC BY-NC-SA 4.0; this file, adapted from it, is shared under the same licence.
/// </summary>
public static class HandheldVendors
{
    /// <summary>Legion modes (Lenovo's "smart fan" modes). Custom lets SquareDeck's TDP limit apply.</summary>
    public static readonly (int Id, string Name, string About)[] LegionModes =
    {
        (-1, "Leave as it is", "Legion Space (or the last setting) decides"),
        (1, "Quiet", "Low power, quiet fans"),
        (2, "Balanced", "Lenovo's everyday mode"),
        (3, "Performance", "Higher power, louder fans"),
        (255, "Custom", "Follows SquareDeck's TDP limit"),
    };

    /// <summary>MSI user scenarios (as in MSI Center M), with the values the Claw's firmware takes.</summary>
    public static readonly (int Id, string Name, string About)[] ClawModes =
    {
        (-1, "Leave as it is", "MSI Center M (or the last setting) decides"),
        (2, "Super Battery", "The lowest power, the longest battery"),
        (1, "Silent", "Quiet and cool"),
        (0, "Balanced", "MSI's everyday scenario"),
        (4, "Extreme Performance", "The most power, the loudest fans"),
    };

    public static readonly int[] ClawChargeLimits = { 0, 60, 70, 80, 90, 100 };

    public static (int Id, string Name, string About)[] Modes =>
        Handheld.Vendor == HandheldVendor.Lenovo ? LegionModes : Handheld.Vendor == HandheldVendor.Msi ? ClawModes : Array.Empty<(int, string, string)>();

    public static bool Supported => Handheld.Vendor is HandheldVendor.Lenovo or HandheldVendor.Msi;

    /// <summary>At start: puts back the mode, fan boost and charge limit chosen in SquareDeck (once the helper is set up).</summary>
    public static async Task ApplyAllAsync(Settings s)
    {
        if (!Supported || !DeviceHelper.Installed) return;
        if (s.HandheldPowerMode >= 0) await SetModeAsync(s.HandheldPowerMode);
        if (s.HandheldFanBoost) await SetFanBoostAsync(true);
        if (s.HandheldChargeLimit > 0) await SetChargeLimitAsync(s.HandheldChargeLimit);
    }

    public static async Task<bool> SetModeAsync(int mode)
    {
        if (mode < 0) return true;
        bool ok = Handheld.Vendor switch
        {
            HandheldVendor.Lenovo => (await DeviceHelper.RunAsync($"legion-mode {mode}")).Ok,
            HandheldVendor.Msi => (await DeviceHelper.RunAsync($"claw-mode {mode}")).Ok,
            _ => false
        };
        // A new mode can put the firmware's own power limits back: SquareDeck's TDP goes on top again.
        if (ok && TdpControl.Ready) _ = TdpControl.ApplyAsync(quiet: true);
        return ok;
    }

    public static async Task<bool> SetFanBoostAsync(bool on) => Handheld.Vendor switch
    {
        HandheldVendor.Lenovo => (await DeviceHelper.RunAsync($"legion-fanfull {(on ? 1 : 0)}")).Ok,
        HandheldVendor.Msi => (await DeviceHelper.RunAsync($"claw-fanfull {(on ? 1 : 0)}")).Ok,
        _ => false
    };

    /// <summary>Lenovo has one limit (80%, on or off); MSI takes a percentage.</summary>
    public static async Task<bool> SetChargeLimitAsync(int percent) => Handheld.Vendor switch
    {
        HandheldVendor.Lenovo => (await DeviceHelper.RunAsync($"legion-charge {(percent is > 0 and < 100 ? 1 : 0)}")).Ok,
        HandheldVendor.Msi => (await DeviceHelper.RunAsync($"claw-charge {Math.Clamp(percent == 0 ? 100 : percent, 20, 100)}")).Ok,
        _ => false
    };

    /// <summary>Legion Go: temperatures (°C), fan speeds (RPM) and the mode, from Lenovo's firmware.</summary>
    public static async Task<Dictionary<string, string>> ReadLegionAsync()
    {
        if (Handheld.Vendor != HandheldVendor.Lenovo || !DeviceHelper.Installed) return new();
        var (ok, lines) = await DeviceHelper.RunAsync("legion-read");
        return ok ? DeviceHelper.Values(lines) : new();
    }
}
