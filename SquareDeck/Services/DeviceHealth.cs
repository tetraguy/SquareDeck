using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>
/// Device health: what's in control of the hardware SquareDeck manages (the lights, the TDP, the buttons) and anything
/// that gets in its way, each with a one-press fix when there is one. Checks are quick reads (registry, services,
/// processes); none of them change anything until a fix is chosen.
/// </summary>
public static class DeviceHealth
{
    public enum Level { Ok, Info, Warning, Problem }

    /// <summary>A finding: an id (for "don't warn me again"), how serious, what it is, and an optional fix.</summary>
    public sealed record Check(string Id, Level Level, string Area, string Title, string Detail, string? FixLabel = null, Func<Task<string?>>? Fix = null);

    /// <summary>Who's in control of each part, one line each, for the top of the page.</summary>
    public sealed record Owner(string Area, string Glyph, string Who, string Detail);

    private static bool ArmouryCrateRunning()
    {
        try
        {
            foreach (var name in new[] { "ArmouryCrateSE", "ArmouryCrate", "ArmouryCrateSE.Service", "ArmourySocketServer" })
                if (ProcessUtil.IsNamedRunning(name)) return true;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Free space on the Windows drive, in GB (null when it can't be read).</summary>
    private static double? SystemDriveFreeGb()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return new DriveInfo(root).AvailableFreeSpace / 1e9;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Who controls the lights, the TDP and the buttons right now.</summary>
    public static List<Owner> Owners(Settings s)
    {
        var list = new List<Owner>();
        if (AllyControl.IsAlly)
        {
            string lights, lightsDetail;
            if (AllyControl.DynamicLightingOn) { lights = "Windows Dynamic Lighting"; lightsDetail = "Windows holds the lights, so SquareDeck's and Armoury Crate's changes don't show"; }
            else if (AsusOptimization.Installed && !AsusOptimization.Enabled) { lights = s.AllyLights.Length == 0 ? "Left as they are" : "SquareDeck"; lightsDetail = "ASUS Optimization is off, so SquareDeck introduces itself to the lights" + (AllyHid.WokeItself ? " (done)" : ""); }
            else if (s.AllyLights.Length == 0) { lights = ArmouryCrateRunning() ? "Armoury Crate SE" : "Left as they are"; lightsDetail = "SquareDeck leaves them alone. Choose an effect to take them over"; }
            else { lights = "SquareDeck"; lightsDetail = (AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights).Name ?? s.AllyLights) + ", " + AllyControl.BrightnessPercent(s) + "%"; }
            list.Add(new Owner("Lights", "\uE781", lights, lightsDetail));
        }

        string tdp, tdpDetail;
        if (TdpControl.Ready) { tdp = "SquareDeck"; tdpDetail = $"{TdpControl.EffectiveWatts} W through {TdpControl.MethodName(TdpControl.Method)}" + (TdpControl.GameProfile != null ? " (the game's profile)" : ""); }
        else if (!s.TdpEnabled) { tdp = AllyControl.IsAlly ? "ASUS (operating mode)" : "Windows"; tdpDetail = "SquareDeck's TDP limit is off"; }
        else { tdp = "Nobody"; tdpDetail = "SquareDeck's TDP is on but has no way to set it on this PC"; }
        list.Add(new Owner("TDP", "\uE945", tdp, tdpDetail));

        if (AllyControl.IsAlly)
        {
            bool asus = AsusOptimization.Installed && AsusOptimization.Enabled;
            string buttons = s.AllyRemap ? (asus ? "SquareDeck, and ASUS too" : "SquareDeck") : (asus ? "ASUS (Armoury Crate SE)" : "Nobody");
            string detail = s.AllyRemap
                ? (asus ? "ASUS Optimization also reacts to the special buttons, so both may answer a press" : "SquareDeck's mapping, with nothing else reacting")
                : "SquareDeck's button mapping is off";
            list.Add(new Owner("Buttons", "\uE7FC", buttons, detail));
        }
        return list;
    }

    /// <summary>Everything worth knowing, the most serious first. Runs off the window's thread (some reads take a moment).</summary>
    public static List<Check> Run(Settings s, Func<Task<string?>>? installWidget = null)
    {
        var checks = new List<Check>();
        bool ally = AllyControl.IsAlly;

        if (ally)
        {
            // The light controller and the controller's settings device.
            if (!AllyHid.HasLights)
                checks.Add(new Check("lights-missing", Level.Problem, "Lights", "The light controller wasn't found",
                    "SquareDeck can't reach the stick and Xbox button lights. A restart usually brings it back; Armoury Crate SE's drivers provide it.",
                    "Look again", () => { AllyHid.Rescan(); AllyControl.ApplyLights(force: true); return Task.FromResult<string?>(AllyHid.HasLights ? null : "Still not found. Restart the Ally and look again."); }));
            if (!AllyHid.HasController)
                checks.Add(new Check("controller-missing", Level.Warning, "Controller", "The controller's settings device wasn't found",
                    "Rumble strength, dead zones and the back buttons can't be changed until it's found.",
                    "Look again", () => { AllyHid.Rescan(); return Task.FromResult<string?>(AllyHid.HasController ? null : "Still not found. Restart the Ally and look again."); }));

            if (AsusOptimization.Installed && !AsusOptimization.Enabled)
                checks.Add(new Check("asusopt-off", Level.Warning, "Lights", "ASUS Optimization is off",
                    "SquareDeck introduces itself to the lights and the extra buttons without ASUS's service. If the lights or buttons still don't respond, switch it back on. Windows asks for permission.",
                    "Switch it on", async () =>
                    {
                        var error = await AsusOptimization.SetEnabledAsync(true);
                        if (error == null) { AllyControl.ApplyLights(force: true); AllyControl.ApplyXboxLight(force: true); }
                        return error;
                    }));

            if (AllyControl.DynamicLightingOn)
                checks.Add(new Check("dynamiclighting", Level.Problem, "Lights", "Windows Dynamic Lighting is on",
                    "Windows takes over the Ally's lights, so changes from SquareDeck and Armoury Crate SE don't show. The fix turns it off and opens the page to confirm.",
                    "Turn it off", () =>
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Lighting");
                            key?.SetValue("AmbientLightingEnabled", 0, RegistryValueKind.DWord);
                        }
                        catch (Exception) { }
                        ProcessUtil.Open("ms-settings:personalization-lighting");
                        AllyControl.ApplyLights(force: true);
                        return Task.FromResult<string?>(null);
                    }));

            if (s.AllyLights is not ("" or "off") && AllyControl.AwakeOff(s))
                checks.Add(new Check("awake-off", Level.Problem, "Lights", "\"Awake\" is off for the lights",
                    "Under When the lights are on, \"Awake\" is off for " + (Battery.IsPluggedIn() ? "plugged in" : "on battery") + ", so the lights stay dark whatever effect is chosen.",
                    "Turn \"Awake\" on", () =>
                    {
                        AllyControl.WakeLights(s);
                        s.Save();
                        AllyControl.ApplyLightPower(force: true);
                        AllyControl.ApplyLights(force: true);
                        return Task.FromResult<string?>(null);
                    }));

            if (s.AllyLights is not ("" or "off") && AllyControl.BrightnessPercent(s) <= 5)
                checks.Add(new Check("lights-dim", Level.Info, "Lights", "The lights are at 5%",
                    "The lowest step is hard to see, especially with a dark color.", "Set 60%", () =>
                    {
                        s.AllyLightPercent = 60;
                        s.Save();
                        AllyControl.ApplyLights(force: true);
                        return Task.FromResult<string?>(null);
                    }));

            if (!AsusAcpi.Available)
                checks.Add(new Check("atkacpi", Level.Warning, "Performance", "ASUS's firmware driver isn't reachable",
                    "The operating mode, fans and charge limit can't be changed. It comes with Armoury Crate SE or the MyASUS drivers."));

            if (ArmouryCrateRunning() && (s.AllyLights.Length > 0 || TdpControl.Ready))
                checks.Add(new Check("armoury-running", Level.Info, "Conflicts", "Armoury Crate SE is running",
                    "It can change the same lights and power settings as SquareDeck. Whichever changes something last wins."));
        }

        // The TDP.
        if (s.TdpEnabled && !TdpControl.CanApply)
            checks.Add(new Check("tdp-nomethod", Level.Problem, "TDP", "The TDP limit can't be set",
                "SquareDeck's TDP is on, but there's no way to set it on this PC. Install the TDP helper in Settings, or turn the TDP off."));
        else if (TdpControl.Ready && TdpControl.HelperNeedsUpdate)
            checks.Add(new Check("tdp-helper", Level.Warning, "TDP", "The TDP helper needs an update",
                "A newer helper comes with SquareDeck. Update it in Settings > " + Handheld.DisplayName + " > Advanced."));
        if (TdpControl.Ready && TdpControl.LastResult.Contains("fail", StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check("tdp-failed", Level.Warning, "TDP", "The last TDP change didn't go through", TdpControl.LastResult,
                "Try again", async () => await TdpControl.ApplyAsync() ? null : TdpControl.LastResult));
        if (ally && TdpControl.Ready && AsusOptimization.Installed && AsusOptimization.Enabled)
            checks.Add(new Check("asusopt-tdp", Level.Info, "Conflicts", "ASUS Optimization may put ASUS's power limits back",
                "SquareDeck sets its TDP again after an operating mode change. If the TDP drifts, this is why."));

        // The Game Bar widget.
        if (s.GameBarWidget)
        {
            var installed = WidgetInstaller.InstalledVersion();
            var bundled = WidgetInstaller.Find();
            if (installed == null && bundled != null)
                checks.Add(new Check("widget-missing", Level.Warning, "Game Bar widget", "The Game Bar widget isn't installed",
                    "SquareDeck's widget brings its controls into Xbox Game Bar over a game.", installWidget != null ? "Install it" : null, installWidget));
            else if (installed != null && bundled != null && installed < bundled.Version)
                checks.Add(new Check("widget-old", Level.Warning, "Game Bar widget", $"The Game Bar widget is out of date ({installed})",
                    $"Version {bundled.Version} comes with this SquareDeck.", installWidget != null ? "Update it" : null, installWidget));
        }

        // Battery and disk.
        try
        {
            var reading = DeviceSensors.Read();
            if (reading.HealthPercent is double health && health < 80)
                checks.Add(new Check("battery-health", Level.Info, "Battery", $"The battery holds {health:0}% of its original charge",
                    "A charge limit (Battery care) slows the wear from here on."));
        }
        catch (Exception) { }
        if (SystemDriveFreeGb() is double free && free < 10)
            checks.Add(new Check("disk-low", Level.Warning, "Storage", $"Only {free:0.#} GB free on the Windows drive",
                "Games, updates and Windows itself need room. Storage Sense can clear temporary files.", "Open Storage",
                () => { ProcessUtil.Open("ms-settings:storagesense"); return Task.FromResult<string?>(null); }));

        return checks.OrderByDescending(c => c.Level).ToList();
    }

    /// <summary>Problems and warnings the user hasn't set aside, for the notice a little after start.</summary>
    public static List<Check> Unseen(Settings s, List<Check> all) =>
        all.Where(c => c.Level >= Level.Warning && !s.HealthDismissed.Contains(c.Id)).ToList();
}
