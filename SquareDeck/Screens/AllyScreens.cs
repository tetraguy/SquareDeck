using System.Windows;
using System.Windows.Controls;
using SquareDeck.Services;
using Media = System.Windows.Media;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > ROG Ally controls: lights, ASUS operating mode, fan curves, battery care and the controller. The firmware
/// and HID messages come from HandheldCompanion (see AllyHardware.cs); the light effects are SquareDeck's own.
/// </summary>
public sealed class AllyControlScreen : ListScreen
{
    private readonly string? _only;

    /// <summary>The page names its parts by their headers; <paramref name="only"/> keeps one of them (from Handheld Specific Settings).</summary>
    private static readonly Dictionary<string, (string Header, string Title)> Parts = new()
    {
        ["lights"] = ("Lights", "Lights"),
        ["performance"] = ("Performance", "Performance and fans"),
        ["battery"] = ("Battery care", "Battery care"),
        ["controller"] = ("Controller", "Controller"),
        ["buttons"] = ("Buttons", "Buttons"),
    };

    public AllyControlScreen(string? only = null)
        : base(only != null && Parts.TryGetValue(only, out var p) ? Handheld.DisplayName + " – " + p.Title : "ROG Ally controls")
    {
        _only = only != null && Parts.ContainsKey(only) ? only : null;
    }

    /// <summary>After building: only the chosen part's rows stay (its header too, as the page's heading).</summary>
    private void KeepOnlyPart()
    {
        if (_only == null) return;
        string keep = Parts[_only].Header;
        var headers = Parts.Values.Select(v => v.Header).ToHashSet();
        string? current = null;
        foreach (var e in Rows.Children.Cast<UIElement>().ToList())
        {
            if (e is TextBlock t && headers.Contains(t.Text)) current = t.Text;
            if (current != keep) Rows.Children.Remove(e);
        }
    }

    private static string OnOff(bool on) => on ? "On" : "Off";

    /// <summary>The X20's eight zones, each its own color, until OK; then the lights go back.</summary>
    private async Task TestEightZonesAsync()
    {
        LightFx.Stop();
        Media.Color[] colors =
        {
            Media.Color.FromRgb(255, 0, 0), Media.Color.FromRgb(255, 110, 0), Media.Color.FromRgb(255, 230, 0), Media.Color.FromRgb(0, 230, 60),
            Media.Color.FromRgb(0, 220, 255), Media.Color.FromRgb(0, 60, 255), Media.Color.FromRgb(170, 0, 255), Media.Color.FromRgb(255, 255, 255),
        };
        bool ok = await Task.Run(() => AllyHid.TestX20Zones(colors));
        await Host.AskAsync("8-zone test", ok
            ? "Zones 1 to 8 are lit: red, orange, yellow, green, cyan, blue, purple, white. Note which LED shows which color (and if some stay dark), then choose OK to put your lights back."
            : "The controller didn't take the test.", "OK");
        AllyControl.ApplyLights(force: true);
    }

    private void Save(Action change, Action? apply = null)
    {
        change();
        Host.Settings.Save();
        apply?.Invoke();
        Rebuild();
    }

    private async void Pick<T>(string title, T[] options, Func<T, string> name, Action<T> store, Action? apply = null)
    {
        int choice = await Host.AskAsync(title, null, options.Select(name).ToArray());
        if (choice < 0 || choice >= options.Length) return;
        Save(() => store(options[choice]), apply);
    }

    internal static string ColorName(string hex) =>
        AllyControl.Palette.FirstOrDefault(p => string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase)).Name ?? "Custom (" + hex + ")";

    /// <summary>Picks a light effect and applies it straight away, saying whether the lights took it.</summary>
    private async Task ChooseEffectAsync()
    {
        var s = Host.Settings;
        var modes = AllyControl.LightModes;
        int current = Array.FindIndex(modes, m => m.Id == s.AllyLights);
        // The list stays open: each choice is saved and shown on the lights straight away; B closes it.
        await LightEffectPicker.AskAsync(this, "Light effect", null, LightEffectPicker.AllEffects(), current, AllyControl.FxInputs(s), ApplyEffectAsync);
        Rebuild();
    }

    private async Task ApplyEffectAsync(int choice)
    {
        var s = Host.Settings;
        var modes = AllyControl.LightModes;
        if (choice < 0 || choice >= modes.Length) return;
        s.AllyLights = modes[choice].Id;
        bool woke = s.AllyLights is not ("" or "off") && AllyControl.WakeLights(s);
        s.Save();
        if (woke) Host.ShowToast("ROG ALLY", "\"Awake\" is back on", "It was off in When the lights are on, which kept the lights dark whatever the effect.", "", "info");
        if (s.AllyLights.Length == 0) return; // "leave as it is" changes nothing
        bool ok = await AllyControl.ApplyLightsAsync(force: true);
        if (!ok)
            Host.ShowToast("ROG ALLY", "Couldn't reach the lights", "SquareDeck will keep trying. Armoury Crate SE may be holding them.", "", "info");
        else if (AllyControl.DynamicLightingOn)
            Host.ShowToast("ROG ALLY", "Windows' Dynamic Lighting is on", "It holds the Ally's lights. Turn it off in Settings > Personalization > Dynamic Lighting.", "", "info");
    }

    /// <summary>Windows' Dynamic Lighting holds the Ally's lights while it's on, so nothing else can change them.</summary>
    private void AddDynamicLightingNote() =>
        AddRow("Windows' Dynamic Lighting is on", "Open", "", click: () => ProcessUtil.Open("ms-settings:personalization-lighting"),
            subtitle: "It takes over the Ally's lights, so SquareDeck's changes don't show. Turn off \"Use Dynamic Lighting on my devices\"");

    protected override void Build()
    {
        var s = Host.Settings;
        bool acpi = AsusAcpi.Available;
        bool lights = AllyHid.HasLights;
        bool pad = AllyHid.HasController;

        // ---------------------------------------------------------------- lights
        AddHeader("Lights");
        if (!lights) AddNote("SquareDeck couldn't find the Ally's light controller. These settings apply once it's found.");
        if (AsusOptimization.Installed && !AsusOptimization.Enabled)
            AddRow("ASUS Optimization is off", "Switch on anyway", "", click: async () =>
            {
                var error = await AsusOptimization.SetEnabledAsync(true);
                if (error != null) await Host.AskAsync("ASUS Optimization", error, "OK");
                else { AllyControl.ApplyLights(force: true); AllyControl.ApplyXboxLight(force: true); }
                Rebuild();
            }, subtitle: "SquareDeck now introduces itself to the lights and buttons without it. If they still don't change, switch it back on. Windows asks for permission");
        if (s.AllyLights is not ("" or "off") && AllyControl.AwakeOff(s))
            AddNote("The lights are dark: \"Awake\" is off under When the lights are on (" + (Battery.IsPluggedIn() ? "plugged in" : "on battery") + "). Turn it on there, or choose the effect again.");
        if (AllyControl.DynamicLightingOn) AddDynamicLightingNote();
        if (s.AllyLights.Length == 0) AddNote("The lights are left as they are. Choose an effect (or a color) and SquareDeck takes them over.");
        var mode = AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights);
        AddRow("Effect", mode.Name ?? "Leave as it is", "",
            click: () => _ = ChooseEffectAsync(),
            subtitle: string.IsNullOrEmpty(mode.About) ? null : mode.About);
        AddRow("Surprise me", null, "\uE8B1", click: () => Save(() =>
        {
            var (fx, one, two) = AllyControl.Surprise();
            s.AllyLights = fx; s.AllyLightColor = one; s.AllyLightColor2 = two;
        }, () => AllyControl.ApplyLights(force: true)), subtitle: "A random effect with two colors that go together. Press again for another");
        if (LightFx.UsesColor(s.AllyLights))
        {
            AddRow(s.AllyLights == "sticks" ? "Left stick color" : "Color", ColorName(s.AllyLightColor), "",
                click: () => Host.Push(new LedColorScreen(s.AllyLights == "sticks" ? "Left stick color" : "Light color", s.AllyLightColor,
                    hex => { s.AllyLightColor = hex; if (s.AllyLights.Length == 0) s.AllyLights = "solid"; s.Save(); AllyControl.ApplyLights(force: true); Rebuild(); })));
            if (LightFx.UsesSecondColor(s.AllyLights))
                AddRow(s.AllyLights == "sticks" ? "Right stick color" : "Second color", ColorName(s.AllyLightColor2), "",
                    click: () => Host.Push(new LedColorScreen(s.AllyLights == "sticks" ? "Right stick color" : "Second light color", s.AllyLightColor2,
                        hex => { s.AllyLightColor2 = hex; s.Save(); AllyControl.ApplyLights(force: true); })));
        }
        if (LightFx.UsesSpeed(s.AllyLights))
            AddRow("Speed", AllyControl.Speeds.FirstOrDefault(x => x.Id == s.AllyLightSpeed).Name ?? "Medium", "",
                click: () => Save(() => s.AllyLightSpeed = s.AllyLightSpeed switch { "slow" => "medium", "medium" => "fast", _ => "slow" }, () => AllyControl.ApplyLights(force: true)));
        if (s.AllyLights is not ("" or "off"))
        {
            AddRow("Brightness", AllyControl.BrightnessPercent(s) + "%", "",
                click: () => Pick("Brightness", AllyControl.Brightness, b => b + "%", b => s.AllyLightPercent = b, () => AllyControl.ApplyLights(force: true)),
                subtitle: s.AllyLights is "rainbow" or "wheel" ? "Rainbow and color wheel use the Ally's nearest of its three steps" : "5%, 10%, 30%, 60% or 100%");
            AddRow("Off while playing", OnOff(s.AllyLightsOffPlaying), "",
                click: () => Save(() => s.AllyLightsOffPlaying = !s.AllyLightsOffPlaying, () => AllyControl.ApplyLights(force: true)),
                subtitle: s.AllyGameLights ? "Game lights are on, so they win: turn those off to use this" : "Saves a little battery and keeps them out of your eyes; they come back when the game closes");
        }
        AddRow("Game lights while playing", OnOff(s.AllyGameLights), "\uE7FC",
            click: () => Save(() => s.AllyGameLights = !s.AllyGameLights, () => AllyControl.ApplyLights(force: true)),
            subtitle: "While a game runs, the lights take its colors from its cover, or the effect chosen in its game profile");
        AddRow("Pulse red when the battery is low", OnOff(s.AllyLowBatteryFlash), "",
            click: () => Save(() => s.AllyLowBatteryFlash = !s.AllyLowBatteryFlash, () => AllyControl.ApplyLights(force: true)),
            subtitle: "At 15% or less, over whatever the lights are doing, until you plug in");
        if (AllyHid.X20EightZones)
            AddRow("Test the 8 zones", null, "\uE9D9", click: () => _ = TestEightZonesAsync(),
                subtitle: "Lights the X20's eight zones in red, orange, yellow, green, cyan, blue, purple and white, so you can see which LED is which");
        AddRow("Lights react to achievements and downloads", OnOff(s.AllyLightReactions), "\uE7E8",
            click: () => Save(() => s.AllyLightReactions = !s.AllyLightReactions, () => AllyControl.ApplyLights(force: true)),
            subtitle: "A gold sweep when you unlock an achievement, a green pulse when a download or update finishes");
        if (s.AllyLights is not ("" or "off") && AsusOptimization.Running)
            AddNote("ASUS Optimization is running, and it can put its own light color back for a moment now and then (a flash over your effect). If you see that, turn the lights off in Armoury Crate SE, or switch the service off below.");

        // ---- When the lights may light (Armoury Crate SE's power states)
        AddHeader("When the lights are on");
        void Power(string label, bool on, Action flip, string subtitle) =>
            AddRow(label, OnOff(on), "", subtitle: subtitle,
                click: () => Save(() => { flip(); s.AllyLightPowerSet = true; }, () => { AllyControl.ApplyLightPower(force: true); AllyControl.ApplyLights(force: true); }));
        if (!s.AllyLightPowerSet) AddNote("Armoury Crate SE's settings apply until you change one here.");
        Power("Boot up and shut down", s.AllyLightBootShutdown, () => s.AllyLightBootShutdown = !s.AllyLightBootShutdown, "While it starts and while it turns off");
        Power("Awake, on battery", s.AllyLightAwakeBattery, () => s.AllyLightAwakeBattery = !s.AllyLightAwakeBattery, "While you use it unplugged");
        if (s.AllyLightAwakeBattery)
            AddRow("Off after a minute without input", OnOff(s.AllyLightBatteryIdleOff), "",
                click: () => Save(() => s.AllyLightBatteryIdleOff = !s.AllyLightBatteryIdleOff, () => AllyControl.ApplyLights(force: true)),
                subtitle: s.AllyLights is "" or "off" ? "On battery. Works with an effect chosen above" : "On battery: they come back the moment you touch it");
        Power("Awake, plugged in", s.AllyLightAwakePlugged, () => s.AllyLightAwakePlugged = !s.AllyLightAwakePlugged, "While it's charging or on the charger");
        Power("Sleep", s.AllyLightSleep, () => s.AllyLightSleep = !s.AllyLightSleep, "While it sleeps");

        // ---- The Xbox button's light (ROG Xbox Ally)
        if (AllyControl.HasXboxLight)
        {
            AddHeader("Xbox button light");
            var states = new[] { ("", "Leave as it is"), ("on", "On"), ("off", "Off") };
            AddRow("Light", states.FirstOrDefault(x => x.Item1 == s.AllyXboxLight).Item2 ?? "Leave as it is", "",
                click: () => Pick("Xbox button light", states, x => x.Item2, x => { s.AllyXboxLight = x.Item1; if (x.Item1 == "on") AllyControl.WakeLights(s); }, () => AllyControl.ApplyXboxLight(force: true)),
                subtitle: s.AllyXboxLight.Length == 0 ? "Armoury Crate SE (or the last setting) decides" : "The green glow in the Xbox button");
            if (s.AllyXboxLight == "on")
            {
                AddRow("Brightness", AllyControl.XboxLightLevelName(s.AllyXboxLightLevel), "",
                    click: () => Pick("Xbox button brightness", AllyControl.XboxLightLevels, l => l.Name, l => s.AllyXboxLightLevel = l.Level, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Bright is how ASUS ships it");
                AddRow("Effect", s.AllyXboxLightBreathe ? "Breathing" : "Steady", "",
                    click: () => Save(() => s.AllyXboxLightBreathe = !s.AllyXboxLightBreathe, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Steady, or a slow fade in and out");
                AddRow("Off while playing", OnOff(s.AllyXboxLightOffPlaying), "",
                    click: () => Save(() => s.AllyXboxLightOffPlaying = !s.AllyXboxLightOffPlaying, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Keeps it out of the corner of your eye; it comes back when the game closes");
                AddRow("Breathe when the battery is low", OnOff(s.AllyXboxLightLowBattery), "",
                    click: () => Save(() => s.AllyXboxLightLowBattery = !s.AllyXboxLightLowBattery, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "At 15% or less, bright and breathing, until you plug in");
            }
        }

        // ---- Graphics memory (VRAM): shown here, changed in Armoury Crate SE or the UEFI setup
        AddHeader("Graphics memory (VRAM)");
        AddRow("Assigned to the graphics now", AllyVram.CurrentText(), "", click: Rebuild,
            subtitle: "Auto, 4, 6, 8, 12 or 16 GB depending on the model. More helps texture-heavy games and leaves less for Windows");
        AddRow("Change it in Armoury Crate SE", null, "", click: () => ProcessUtil.Open("asusac://"),
            subtitle: "Settings > Operating mode > GPU settings > Memory assigned to GPU. It takes effect after a restart");
        AddRow("Restart into the UEFI setup", null, "", click: async () =>
            {
                if (await Host.AskAsync("Restart into the UEFI setup?", "Save what you're doing first. In the setup, look under Advanced for \"UMA frame buffer size\" (Auto, or a size in GB), then save and exit.", "Cancel", "Restart") != 1) return;
                var result = await AllyVram.RestartToSetupAsync();
                if (result.Contains("declined")) Host.ShowToast("ROG ALLY", "Not restarted", result, "", "info");
            },
            subtitle: "The same setting, before Windows starts. Windows asks first");
        AddNote("SquareDeck doesn't write this one to the firmware itself: on the Ally X and Xbox Allys ASUS hasn't published the values, and a wrong one can stop the Ally from starting.");

        // ---------------------------------------------------------------- performance
        AddHeader("Performance");
        if (!acpi) AddNote("SquareDeck couldn't reach ASUS's firmware driver (ATKACPI, installed with Armoury Crate SE or the MyASUS drivers), so these settings can't be applied.");
        AddRow("ASUS mode", AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name ?? "Leave as it is", "",
            click: () => Pick("ASUS mode", AllyControl.AsusModes, m => m.Name, m => s.AllyAsusMode = m.Id, () => Task.Run(AllyControl.ApplyPower)),
            subtitle: "The same as Armoury Crate's Silent, Performance and Turbo. The TDP limit (Processor power, in this device's section of Settings), when on, is applied on top");
        var fan = AllyControl.FanCurves.FirstOrDefault(f => f.Id == s.AllyFan);
        AddRow("Fans", fan.Name ?? "Leave as it is", "",
            click: () => Pick("Fans", AllyControl.FanChoices, f => f.Name, f => s.AllyFan = f.Id, () => Task.Run(AllyControl.ApplyPower)),
            subtitle: string.IsNullOrEmpty(fan.About) ? null : fan.About);
        if (acpi && AsusAcpi.FanRpm(AsusAcpi.CpuFan) is int rpm) AddRow("Fan speed now", rpm.ToString("N0") + " RPM", "", click: Rebuild, subtitle: "Select to refresh");

        // ---------------------------------------------------------------- battery
        AddHeader("Battery care");
        AddRow("Stop charging at", s.AllyChargeLimit is > 0 and < 100 ? s.AllyChargeLimit + "%" : s.AllyChargeLimit == 100 ? "100%" : "Leave as it is", "",
            click: () => Pick("Stop charging at", AllyControl.ChargeLimits, l => l == 0 ? "Leave as it is" : l + "%", l => s.AllyChargeLimit = l, () => Task.Run(AllyControl.ApplyBattery)),
            subtitle: "80% is kinder to the battery when the Ally is mostly plugged in");
        if (s.AllyChargeLimit is > 0 and < 100)
            AddRow("Charge to 100% this once", AllyControl.ChargingFullOnce ? "Until SquareDeck restarts" : null, "",
                click: () => { AllyControl.ChargeFullOnce(); Rebuild(); },
                subtitle: "For a long trip: charges fully now; the limit comes back next time SquareDeck starts");

        // ---- The startup sound (kept by the firmware, like the BIOS setting and Armoury Crate SE's)
        if (AsusAcpi.BootSound is bool chime)
        {
            AddHeader("Starting up");
            AddRow("Sound when it turns on", chime ? "On" : "Off", "\uE767", click: async () =>
            {
                bool ok = await Task.Run(() => AsusAcpi.SetBootSound(!chime));
                if (!ok) await Host.AskAsync("Sound when it turns on", "The firmware didn't take the change. You can still change it in the BIOS or Armoury Crate SE.", "OK");
                Rebuild();
            }, subtitle: "The chime when the Ally starts. The same setting as in the BIOS and Armoury Crate SE");
        }

        // ---------------------------------------------------------------- controller
        AddHeader("Controller");
        if (!pad) AddNote("SquareDeck couldn't find the Ally's controller settings device. These settings apply once it's found.");
        AddRow("M1 and M2 back buttons", s.AllyBackKeys ? "Usable in SquareDeck" : "As ASUS set them", "",
            click: async () =>
            {
                if (!s.AllyBackKeys && await Host.AskAsync("Use the back buttons?",
                        "M1 and M2 will send the F18 and F17 keys, so you can give them actions in ROG Ally buttons (and games can bind them). " +
                        "This puts the controller's other buttons back to ASUS's defaults, undoing mappings made in Armoury Crate SE.",
                        "Use them", "Cancel") != 0) return;
                Save(() => s.AllyBackKeys = !s.AllyBackKeys, () => { Task.Run(AllyControl.ApplyController); Host.ApplyAllyButtons(); });
            }, subtitle: s.AllyBackKeys ? "Give them actions in ROG Ally buttons. Put back as ASUS ships them when SquareDeck closes" : "Turn on to give them actions");
        AddRow("Mouse mode", OnOff(AllyControl.MouseMode), "",
            click: () => { if (!AllyControl.ToggleMouseMode()) Host.ShowToast("ROG ALLY", "Couldn't change the mode", "", "", "info"); Rebuild(); },
            subtitle: "The right stick moves the pointer and the triggers click, for the desktop. Game mode comes back when SquareDeck closes");
        AddRow("Switch off the built-in controller", null, "", click: async () =>
        {
            if (await Host.AskAsync("Switch off the built-in controller?",
                    "For when you play with another controller, so the Ally's own doesn't also count as a player. " +
                    "Make sure the other controller is connected: the touch screen still works, and the built-in controller comes back when SquareDeck closes or the Ally restarts.",
                    "Switch off", "Switch back on", "Cancel") is int pick and (0 or 1))
            {
                bool ok = AllyHid.SetBuiltInController(pick == 1);
                Host.ShowToast("ROG ALLY", ok ? (pick == 1 ? "Built-in controller on" : "Built-in controller off") : "Couldn't change the controller", "", "", "info");
            }
        });

        AddHeader("Buttons");
        AddRow("ROG Ally buttons", s.AllyRemap ? "On" : "Off", "", click: () => Host.Push(new AllyButtonsScreen()),
            subtitle: "Armoury Crate, Command Center, Xbox Library" + (s.AllyBackKeys ? ", M1 and M2" : ""));

        AddNote("The firmware and controller messages come from HandheldCompanion by Valkirie (github.com/Valkirie/HandheldCompanion). " +
                "Armoury Crate SE can change the same settings; if it's running, the last one to change something wins.");
        KeepOnlyPart();
    }
}

/// <summary>
/// A light color, picked from swatches like the highlight color (Settings > Theme): the LED-friendly palette, the
/// current highlight color, and any color with the custom picker. The one in use has a check.
/// </summary>
public sealed class LedColorScreen : GridScreen
{
    private readonly Action<string> _picked;

    public LedColorScreen(string title, string current, Action<string> picked) : base(title)
    {
        _picked = picked;
        if (Switch2Pages.PcPage(this)) UseSwitch2Grid(title, Switch2Pages.PcGlyph(this)); // swatches with the rimmed highlight
        var swatches = AllyControl.Palette.ToList();
        // The highlight color too, when it isn't one of the swatches already.
        if ((Application.Current.Resources["AccentBrush"] as Media.SolidColorBrush)?.Color is Media.Color accent)
        {
            string hex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
            if (!swatches.Any(x => string.Equals(x.Hex, hex, StringComparison.OrdinalIgnoreCase))) swatches.Insert(0, (hex, "Highlight color"));
        }
        foreach (var (hex, name) in swatches)
        {
            Theme.TryParseColor(hex, out var color);
            var swatch = new Border { Width = 120, Height = 120, Background = new Media.SolidColorBrush(color), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
            if (string.Equals(hex, current, StringComparison.OrdinalIgnoreCase))
            {
                // A check on the color in use, dark or light so it shows on the swatch.
                double light = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
                swatch.Child = new TextBlock
                {
                    Text = "\uE73E", FontFamily = Ui.IconFont, FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = light > 0.6 ? Media.Brushes.Black : Media.Brushes.White
                };
            }
            string chosen = hex;
            AddTile(swatch, name, 120, () => Pick(chosen));
        }

        // A rainbow tile for any color.
        var rainbow = new Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var stops = new[] { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#00C7BE", "#007AFF", "#AF52DE" };
        for (int i = 0; i < stops.Length; i++)
            rainbow.GradientStops.Add(new Media.GradientStop((Media.Color)Media.ColorConverter.ConvertFromString(stops[i]), i / 6.0));
        AddTile(new Border { Width = 120, Height = 120, Background = rainbow }, "Custom\u2026", 120, () =>
        {
            Theme.TryParseColor(current, out var start);
            Host.Push(new ColorPickerScreen(start, Pick)); // Pick closes this page, and the picker with it
        });
    }

    private void Pick(string hex)
    {
        // The custom picker may hand back #AARRGGBB: the lights only use the color.
        if (Theme.TryParseColor(hex, out var c)) hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _picked(hex);
        Host.Pop(this);
    }
}
