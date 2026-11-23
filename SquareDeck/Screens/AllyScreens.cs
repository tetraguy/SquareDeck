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
        int choice = await Host.AskAsync("Light effect", null, modes.Select(m => m.Name).ToArray());
        if (choice < 0 || choice >= modes.Length) return;
        s.AllyLights = modes[choice].Id;
        s.Save();
        Rebuild();
        if (s.AllyLights.Length == 0) return; // "leave as it is" changes nothing
        bool ok = await AllyControl.ApplyLightsAsync(force: true);
        if (!ok)
            Host.ShowToast("ROG ALLY", "Couldn't reach the lights", "SquareDeck will keep trying. Armoury Crate SE may be holding them.", "", "info");
        else if (s.AllyLights == "accent")
            Host.ShowToast("ROG ALLY", "Lights match SquareDeck's accent color", "They follow it when the theme or the game's color changes.", "", "info");
    }

    protected override void Build()
    {
        var s = Host.Settings;
        bool acpi = AsusAcpi.Available;
        bool lights = AllyHid.HasLights;
        bool pad = AllyHid.HasController;

        // ---------------------------------------------------------------- lights
        AddHeader("Lights");
        if (!lights) AddNote("SquareDeck couldn't find the Ally's light controller. These settings apply once it's found.");
        var mode = AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights);
        AddRow("Effect", mode.Name ?? "Leave as it is", "",
            click: () => _ = ChooseEffectAsync(),
            subtitle: string.IsNullOrEmpty(mode.About) ? null : mode.About);
        if (s.AllyLights is "solid" or "breathing" or "sticks" or "")
        {
            AddRow(s.AllyLights == "sticks" ? "Left stick color" : "Color", ColorName(s.AllyLightColor), "",
                click: () => Host.Push(new LedColorScreen(s.AllyLights == "sticks" ? "Left stick color" : "Light color", s.AllyLightColor,
                    hex => { s.AllyLightColor = hex; s.Save(); AllyControl.ApplyLights(force: true); })));
            if (s.AllyLights is "breathing" or "sticks")
                AddRow(s.AllyLights == "sticks" ? "Right stick color" : "Second color", ColorName(s.AllyLightColor2), "",
                    click: () => Host.Push(new LedColorScreen(s.AllyLights == "sticks" ? "Right stick color" : "Second light color", s.AllyLightColor2,
                        hex => { s.AllyLightColor2 = hex; s.Save(); AllyControl.ApplyLights(force: true); })));
        }
        if (s.AllyLights is "breathing" or "rainbow" or "wheel")
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

        // ---------------------------------------------------------------- performance
        AddHeader("Performance");
        if (!acpi) AddNote("SquareDeck couldn't reach ASUS's firmware driver (ATKACPI, installed with Armoury Crate SE or the MyASUS drivers), so these settings can't be applied.");
        AddRow("ASUS mode", AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name ?? "Leave as it is", "",
            click: () => Pick("ASUS mode", AllyControl.AsusModes, m => m.Name, m => s.AllyAsusMode = m.Id, () => Task.Run(AllyControl.ApplyPower)),
            subtitle: "The same as Armoury Crate's Silent, Performance and Turbo. The TDP limit (Processor power, in this device's section of Settings), when on, is applied on top");
        var fan = AllyControl.FanCurves.FirstOrDefault(f => f.Id == s.AllyFan);
        AddRow("Fans", fan.Name ?? "Leave as it is", "",
            click: () => Pick("Fans", AllyControl.FanCurves, f => f.Name, f => s.AllyFan = f.Id, () => Task.Run(AllyControl.ApplyPower)),
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
