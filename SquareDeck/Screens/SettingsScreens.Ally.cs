using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.12: the device's section of Settings, laid out like Armoury Crate SE but drawn in the Switch 2 style. The section
/// itself is a home page: the device with Control Mode beside it (Configure opens Gamepad Mode), then a grid of pages:
/// General, Performance, Update Center, Calibration, Power Management, Lighting, Audio, Display, Connection, Shortcuts
/// and Key Binding, and Devices. Each page opens on its own with its tabs down the left (as Armoury Crate has them) and
/// their settings on the right. The settings are the same ones as before, sorted into those pages and tabs.
/// </summary>
public sealed partial class SettingsScreen
{
    private readonly string? _allyPage;

    private sealed record AllyTab(string Id, string Name, string About);
    private sealed record AllyPageInfo(string Id, string Name, string Glyph, string About, AllyTab[] Tabs);

    private static bool IsAlly => Handheld.Vendor == HandheldVendor.Asus;

    /// <summary>The pages, in Armoury Crate's order. Gamepad Mode is the one Control Mode's Configure opens.</summary>
    private static AllyPageInfo[] AllyPages
    {
        get
        {
            var lightingTabs = new List<AllyTab>();
            if (AllyControl.HasXboxLight) lightingTabs.Add(new("xbox", "XBOX Button", "The glow in the Xbox button: on or off, brightness, steady or breathing"));
            lightingTabs.Add(new("lighting", "Lighting", "The rings round the sticks: the effect, its colors, speed and brightness"));
            lightingTabs.Add(new("settings", "Settings", "When the lights may light: starting up, awake on battery or plugged in, and asleep"));
            return new AllyPageInfo[]
            {
                new("gamepad", "Gamepad Mode", "\uE7FC", "The built-in controller: buttons, sticks, triggers, vibration and other controllers", new AllyTab[]
                {
                    new("keys", "Key Mapping", "The back buttons and the desktop's mouse mode. What each special button does is in Shortcuts and Key Binding"),
                    new("lstick", "Left Stick", "Response curve, dead zone, full travel and anti-deadzone, kept in the controller itself"),
                    new("rstick", "Right Stick", "Response curve, dead zone, full travel and anti-deadzone, kept in the controller itself"),
                    new("trigger", "Trigger", "Where each trigger starts and where it counts as fully pressed"),
                    new("vibration", "Vibration", "How strongly each side vibrates, in every game"),
                    new("gyro", "Gyro", "Aim by turning and tilting the Ally: it moves the mouse, for games that aim with one"),
                    new("external", "External Controller", "When another controller is player one"),
                }),
                new("general", "General", "\uE713", "Personalization, the real-time monitor, device info, storage and ASUS software", new AllyTab[]
                {
                    new("personal", "Personalization", "The startup sound and the logo in the corner"),
                    new("monitor", "Real-time Monitor", "Live temperatures, fans and power, and the performance overlay over your games"),
                    new("info", "Device Info", "What this device is, its user manual, and what SquareDeck can change on it"),
                    new("storage", "Storage", "Space, cleaning up and the drives"),
                    new("about", "About", "Armoury Crate SE and the ASUS Optimization service"),
                }),
                new("performance", "Performance", "\uEC4A", "Operating mode and TDP, system stats, graphics memory, the processor and the fans", new AllyTab[]
                {
                    new("mode", "Operating Mode", "Silent, Performance or Turbo, and SquareDeck's own TDP limit on top"),
                    new("stats", "System Stats", "Live readings, and how each game does on battery"),
                    new("gpu", "GPU Settings", "Memory assigned to the graphics"),
                    new("cpu", "CPU Settings", "CPU boost, speed, energy preference, cores and the Windows power mode"),
                    new("fans", "Fans", "Fan curves, your own curve, and the fans now"),
                    new("advanced", "Advanced", "The TDP helper (RyzenAdj)"),
                }),
                new("update", "Update Center", "\uE895", "Drivers, SquareDeck's own updates and Windows Update", new AllyTab[]
                {
                    new("updates", "Updates", "Drivers, SquareDeck and Windows Update"),
                }),
                new("calibration", "Calibration", "\uE7C9", "Recalibrate the sticks, triggers and gyro, and test every button", new AllyTab[]
                {
                    new("calibrate", "Calibration", "Select a function to calibrate"),
                    new("check", "Stick Check", "See exactly where each stick and trigger is, live"),
                    new("buttons", "Buttons", "Every button, the D-pad and the vibration, tested"),
                }),
                new("power", "Power Management", "\uE83F", "Battery care, battery saver and the battery's health", new AllyTab[]
                {
                    new("care", "Battery Care", "Stop charging early to make the battery last longer"),
                    new("eco", "Eco Assist", "Battery saver, and what each game costs on battery"),
                    new("charging", "Charging", "The battery's health, wear and charge cycles"),
                }),
                new("lighting", "Lighting", "\uE781", "The stick lights" + (AllyControl.HasXboxLight ? ", the Xbox button" : "") + " and when they light", lightingTabs.ToArray()),
                new("audio", "Audio", "\uE767", "SquareDeck EQ, interface sounds and the sound output", new AllyTab[]
                {
                    new("audio", "Audio", "SquareDeck EQ, the interface sounds and where sound goes"),
                }),
                new("display", "Display", "\uE7F4", "GameVisual, refresh rate, resolution and brightness", new AllyTab[]
                {
                    new("visual", "GameVisual", "The screen's color modes, refresh rate and resolution"),
                    new("oled", "OLED Settings", "OLED Care: keep still things from wearing the screen"),
                }),
                new("connection", "Connection", "\uE701", "Wi-Fi, Bluetooth and network troubleshooting", new AllyTab[]
                {
                    new("network", "Connection", "Wi-Fi, Bluetooth, the speed test and troubleshooting"),
                }),
                new("shortcuts", "Shortcuts & Key Binding", "\uE765", "What the Armoury Crate, Command Center and back buttons do", new AllyTab[]
                {
                    new("actions", "Button Actions", "What each of the Ally's special buttons does, on a press and a hold"),
                    new("back", "Back Buttons", "M1 and M2 on the back"),
                }),
                new("devices", "Devices", "\uE772", "Other controllers, Bluetooth devices and the Game Bar widget", new AllyTab[]
                {
                    new("devices", "Devices", "Other controllers, Bluetooth devices and the Game Bar widget"),
                }),
            };
        }
    }

    private static AllyPageInfo AllyInfo(string id) => AllyPages.FirstOrDefault(p => p.Id == id) ?? AllyPages[1];
    private static string AllyTitle(string id) => AllyInfo(id).Name;
    private static string AllyGlyph(string id) => AllyInfo(id).Glyph;

    /// <summary>The tab a page opens on: the one asked for if it has it, else its first (Lighting opens on Lighting).</summary>
    private static string AllyFirstTab(string page, string? tab)
    {
        var info = AllyInfo(page);
        if (tab != null && info.Tabs.Any(t => t.Id == tab)) return tab;
        return info.Tabs.FirstOrDefault(t => t.Id == "lighting")?.Id ?? info.Tabs[0].Id;
    }

    /// <summary>Opens one of the device's pages, on a tab.</summary>
    public static SettingsScreen AllyPage(string page, string? tab = null) => new(tab, page);

    /// <summary>Where the TDP is: the device's Performance page on a handheld, Gaming elsewhere.</summary>
    public static SettingsScreen TdpPage() => Handheld.IsHandheld ? AllyPage("performance", "mode") : new SettingsScreen("gaming");

    // ---------------------------------------------------------------- the home page

    /// <summary>The device's section: the device and Control Mode, then a tile for each page.</summary>
    private void BuildAllyHub(Settings s)
    {
        var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

        // Control Mode: the device on the left, the words and Configure on the right. The whole card is the button.
        var hero = new Grid { MinHeight = 300 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (MyDeviceScreen.DevicePhoto(420) is FrameworkElement photo)
        {
            photo.HorizontalAlignment = HorizontalAlignment.Center;
            photo.VerticalAlignment = VerticalAlignment.Center;
            photo.Margin = new Thickness(10, 10, 10, 10);
            hero.Children.Add(photo);
        }
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 18, 24, 18) };
        words.Children.Add(Ui.Text("Control Mode", 34, "FgBrush", bold: true));
        var lead = Ui.Text("Set up the built-in controller the way you like it: buttons, sticks, triggers, vibration, and what happens with another controller.", 17, "SubtleBrush");
        lead.TextWrapping = TextWrapping.Wrap;
        lead.Margin = new Thickness(0, 10, 0, 18);
        words.Children.Add(lead);
        var configure = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10), Padding = new Thickness(30, 10, 30, 11), HorizontalAlignment = HorizontalAlignment.Left };
        configure.SetResourceReference(Border.BorderBrushProperty, "FgBrush");
        configure.Child = Ui.Text("Configure", 18, "FgBrush");
        words.Children.Add(configure);
        // What it's set to now, small.
        var now = new List<string>();
        if (TdpControl.Ready) now.Add(TdpControl.EffectiveWatts + " W TDP");
        if (IsAlly && s.AllyAsusMode.Length > 0 && AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name is { } mode) now.Add(mode);
        if (Battery.GetPercent() is int battery) now.Add(battery + "% battery");
        if (IsAlly) now.Add(AllyHid.ZoneCount == 8 ? "8 light zones" : "4 light zones");
        if (now.Count > 0)
        {
            var line = Ui.Text(string.Join("   ·   ", now), 14, "SubtleBrush");
            line.Margin = new Thickness(0, 18, 0, 0);
            words.Children.Add(line);
        }
        Grid.SetColumn(words, 1);
        hero.Children.Add(words);
        var heroCard = new Button
        {
            Style = Switch2Pages.TileStyle, Content = hero, Margin = new Thickness(0, 8, 0, 14), Tag = "pinned",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new LinearGradientBrush(Color.FromArgb(0x30, a.R, a.G, a.B), Color.FromArgb(0x06, a.R, a.G, a.B), 0),
        };
        heroCard.Click += (_, _) => Host.Push(AllyPage("gamepad"));
        Rows.Children.Add(heroCard);

        // The pages, three to a row, as Armoury Crate lays them out.
        var grid = new UniformGrid { Columns = 3, Tag = "pinned", Margin = new Thickness(0, 0, 0, 6) };
        foreach (var page in AllyPages.Where(p => p.Id != "gamepad"))
        {
            var id = page.Id;
            var row = new DockPanel { LastChildFill = true };
            var glyph = Ui.Glyph(page.Glyph, 24, "FgBrush");
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 16, 0);
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            var name = Ui.Text(page.Name, 20, "FgBrush");
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(name);
            var tile = new Button
            {
                Style = Switch2Pages.TileStyle, Content = row, Height = 78, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(22, 0, 16, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = page.About, DataContext = "allypage:" + id,
            };
            tile.Click += (_, _) => Host.Push(AllyPage(id));
            grid.Children.Add(tile);
        }
        Rows.Children.Add(grid);
        var health = Ui.Row("Device health", null, "\uE95E", subtitle: "What's in control of the lights, TDP and buttons, anything in the way, and one-press fixes");
        health.Click += (_, _) => Host.Push(new DeviceHealthScreen());
        Rows.Children.Add(health);
        if (Handheld.ManualUrl != null)
        {
            var book = Ui.Row("User manual", null, "\uE736", subtitle: "ASUS's manual page for the " + Handheld.DisplayName + ", opened right here. A manual you open can be turned sideways to read like a book");
            book.Click += (_, _) => OpenManual();
            Rows.Children.Add(book);
        }
    }

    /// <summary>9.0.19: the model's user manual page on ASUS's site, opened inside SquareDeck.</summary>
    private void AddManualRow()
    {
        if (Handheld.ManualUrl == null) return;
        AddRow("User manual", null, "\uE736", click: OpenManual,
            subtitle: "ASUS's manual page for the " + Handheld.DisplayName + ", opened right here. A manual you open turns sideways with Y");
    }

    private void OpenManual()
    {
        if (Handheld.ManualUrl is not { } url) return;
        Host.Push(StoreWebScreen.Open(new StoreWebScreen.KeyStore(Handheld.DisplayName + " manual", url, "\uE736", Manual: true)));
    }

    // ---------------------------------------------------------------- a page: its tabs down the left

    private void BuildAllyNav()
    {
        var info = AllyInfo(_allyPage!);
        if (_switch2) _navPanel.Padding = new Thickness(0, 4, 18, 4);
        foreach (var tab in info.Tabs)
        {
            string id = tab.Id;
            Button row;
            if (_switch2)
            {
                var text = new TextBlock { Text = tab.Name, FontSize = 19, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                row = new Button { Style = Switch2Settings.NavStyle, Content = text };
            }
            else
            {
                row = Ui.Row(tab.Name, null, info.Glyph);
                row.FontSize = 16;
                row.Margin = new Thickness(0, 1, 0, 1);
            }
            row.DataContext = "nav:" + id;
            if (id == _category) row.Tag = "selected";
            row.GotKeyboardFocus += (_, _) =>
            {
                _pendingCategory = id;
                _switchSoon.Stop();
                _switchSoon.Start();
            };
            row.Click += (_, _) => Show(id, focusContent: true);
            _nav.Children.Add(row);
        }
    }

    /// <summary>The tab's settings (every tab, one after another, when there's no list of tabs on the left).</summary>
    private void BuildAllyPage(Settings s)
    {
        var info = AllyInfo(_allyPage!);
        var tabs = _panes ? info.Tabs.Where(t => t.Id == _category).ToArray() : info.Tabs;
        if (tabs.Length == 0) tabs = new[] { info.Tabs[0] };
        foreach (var tab in tabs)
        {
            Rows.Children.Add(SectionHint(tab.About));
            BuildAllyTab(s, info.Id, tab.Id);
        }
    }

    /// <summary>Builds a part of the old section and keeps only the named groups of it.</summary>
    private void Keep(Action build, params string[] groups)
    {
        int start = Rows.Children.Count;
        build();
        KeepGroups(start, groups);
    }

    private void BuildAllyTab(Settings s, string page, string tab)
    {
        bool ally = IsAlly;
        Action hardware = ally ? () => BuildAllyHardware(s) : () => { };
        void NotAlly() { if (!ally) AddNote("These settings are for the ROG Ally family."); }
        Action tdp = () => BuildTdp(s, handheld: true, first: ally ? () =>
        {
            AddRow("Operating mode", AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name ?? "Leave as it is", "\uEC4A",
                click: () => AllyPick("Operating mode", AllyControl.AsusModes, m => m.Name, m => s.AllyAsusMode = m.Id, () => Task.Run(AllyControl.ApplyPower)),
                subtitle: "Silent, Performance or Turbo, as in Armoury Crate. The TDP limit below, when on, is applied on top");
        } : null);

        switch (page + "/" + tab)
        {
            // ---- Gamepad Mode (Control Mode > Configure)
            case "gamepad/keys": NotAlly(); Keep(hardware, "Back buttons", "Desktop mode"); break;
            case "gamepad/lstick": NotAlly(); Keep(hardware, "Left Stick", "Sticks", "Controller defaults"); CalibrateRow(CalibrationScreen.Kind.LeftStick); break;
            case "gamepad/rstick": NotAlly(); Keep(hardware, "Right Stick", "Sticks", "Controller defaults"); CalibrateRow(CalibrationScreen.Kind.RightStick); break;
            case "gamepad/gyro": BuildGyro(s); break;
            case "gamepad/trigger": NotAlly(); Keep(hardware, "Triggers", "Controller defaults"); CalibrateRow(CalibrationScreen.Kind.LeftTrigger); CalibrateRow(CalibrationScreen.Kind.RightTrigger); break;
            case "gamepad/vibration": NotAlly(); Keep(hardware, "Vibration", "Controller defaults"); break;
            case "gamepad/external":
                NotAlly();
                Keep(hardware, "External controller");
                AddHeader("Other controllers");
                AddRow("Bluetooth", glyph: "\uE702", click: () => Host.Push(new BluetoothScreen()), subtitle: "Pair a controller, headphones or a keyboard");
                AddRow("Controller and Game Bar widget settings", glyph: "\uE7FC", click: () => Host.Push(new SettingsScreen("controller")),
                    subtitle: "Button prompts, vibration in SquareDeck, typing and the Game Bar widget");
                break;

            // ---- General
            case "general/personal": Keep(hardware, "Starting up", "Corner logo"); break;
            case "general/monitor":
                AddHeader("Real-time monitor");
                AddRow("My Device", null, Ui.HandheldGlyph, click: () => Host.Push(new MyDeviceScreen()), subtitle: "Temperatures, fan speed, battery health and power draw, live");
                AddRow("Performance overlay", null, "\uE9D9", click: () => Host.Push(new SettingsScreen("gaming") { FocusLabel = "Performance overlay" }),
                    subtitle: "FPS, temperatures and power over your games: what it shows, where, its size and colors (it's in Gaming)");
                break;
            case "general/info": BuildAllyInfo(); break;
            case "general/storage":
                AddHeader("Storage");
                AddRow("Disk cleanup", glyph: "\uE74D", click: () => Host.Push(new DiskCleanupScreen()), subtitle: "Make room: temporary files, caches and leftovers");
                AddRow("Storage advisor", glyph: "\uEDA2", click: () => Host.Push(new StorageAdvisorScreen()), subtitle: "What's taking the space, and games you haven't played in a while");
                AddRow("Format external storage", glyph: "\uE88E", click: () => Host.Push(new StorageScreen()), subtitle: "A microSD card or USB drive, ready for games");
                break;
            case "general/about":
                Keep(hardware, "ASUS software");
                AddHeader("SquareDeck");
                AddRow("This version", "SquareDeck " + Updater.Current, "\uE946", click: () => Host.Push(new SettingsScreen("data")), subtitle: "Updates, backup and reset are in Updates and data");
                break;

            // ---- Performance
            case "performance/mode": Keep(tdp, "Performance"); break;
            case "performance/stats":
                AddHeader("System stats");
                AddRow("My Device", null, Ui.HandheldGlyph, click: () => Host.Push(new MyDeviceScreen()), subtitle: "Processor and graphics load, temperatures, fans and power draw, live");
                AddRow("Battery and performance by game", null, "\uE859", click: () => Host.Push(new BatteryInsightScreen()), subtitle: "How long a full battery lasts in each game, and at what TDP");
                AddRow("Device health", null, "\uE95E", click: () => Host.Push(new DeviceHealthScreen()), subtitle: "What's in control of the lights, TDP and buttons, and one-press fixes");
                break;
            case "performance/gpu": NotAlly(); Keep(hardware, "Graphics memory (VRAM)"); break;
            case "performance/cpu": Keep(tdp, "Processor tuning"); Keep(hardware, "Processor cores"); break;
            case "performance/fans": NotAlly(); Keep(hardware, "Fans and temperature"); break;
            case "performance/advanced":
                BuildTdpHelper(s, "TDP helper");
                if (DeviceHelper.Installed)
                    AddRow("Remove the old device helper", "Not used any more", "\uE74D", click: async () =>
                    {
                        var error = await DeviceHelper.UninstallAsync();
                        await Host.AskAsync("Device helper", error ?? "Removed. Its background task and its folder in Program Files are gone.", "OK");
                        Rebuild();
                    }, subtitle: "An earlier version set it up for other handhelds. Removing it takes one Windows permission prompt");
                if (!TdpControl.Installed && !DeviceHelper.Installed) AddNote("Nothing to set up here: the TDP is set through the Ally's firmware.");
                break;

            // ---- Update Center
            case "update/updates":
                AddHeader("Drivers");
                if (LabPage("drivercheck") is { } drivers) AddRow("Driver check", null, "\uE964", click: drivers, subtitle: "The graphics, chipset and ASUS drivers this device needs, and whether they're current");
                else AddRow("Driver check", null, "\uE964", click: () => Host.Push(new DriverCheckScreen()), subtitle: "The graphics, chipset and ASUS drivers this device needs, and whether they're current");
                if (ally) AddRow("ASUS's update center", "Armoury Crate SE", "\uE8A7", click: () => ProcessUtil.Open("asusac://"), subtitle: "Firmware and ASUS driver updates, in Armoury Crate SE");
                AddHeader("SquareDeck");
                AddRow("Check for updates now", "SquareDeck " + Updater.Current, "\uE895", click: () => _ = Host.CheckForUpdatesAsync(manual: true));
                AddRow("Check for updates automatically", OnOff(s.AutoUpdateCheck), "\uE777", click: () => Toggle(() => s.AutoUpdateCheck = !s.AutoUpdateCheck),
                    subtitle: "When SquareDeck starts and every 12 hours");
                AddHeader("Windows");
                AddRow("Windows Update", null, "\uE895", click: () => ProcessUtil.Open("ms-settings:windowsupdate"));
                AddFeature("updateguard");
                break;

            // ---- Calibration
            case "calibration/calibrate": BuildCalibrationTiles(); break;
            case "calibration/check":
                AddHeader("Stick check");
                AddRow("Stick and trigger check", null, "\uE7FC", click: () => Host.Push(new StickCheckScreen()),
                    subtitle: "Exactly where each stick and trigger is, live: see drift and how far they travel");
                if (ally)
                {
                    AddRow("Left stick settings", null, "\uE7FC", click: () => Host.Push(AllyPage("gamepad", "lstick")), subtitle: "Response curve, dead zone, travel and anti-deadzone");
                    AddRow("Right stick settings", null, "\uE7FC", click: () => Host.Push(AllyPage("gamepad", "rstick")), subtitle: "Response curve, dead zone, travel and anti-deadzone");
                    AddRow("Trigger settings", null, "\uE7FC", click: () => Host.Push(AllyPage("gamepad", "trigger")));
                }
                break;
            case "calibration/buttons":
                AddHeader("Buttons");
                AddRow("Controller test", null, "\uE7FC", click: () => Host.Push(new ControllerTesterScreen()), subtitle: "Every button, the D-pad, the sticks and the vibration, drawn as you press them");
                if (ally) AddRow("ROG Ally button test", null, "\uE765", click: () => Host.Push(new AllyButtonsScreen()), subtitle: "The special buttons (Armoury Crate, Command Center, M1, M2), in ROG Ally buttons");
                break;

            // ---- Power Management
            case "power/care": NotAlly(); Keep(hardware, "Battery care"); break;
            case "power/eco":
                AddHeader("Eco assist");
                AddRow("Battery saver and usage", null, "\uEBB5", click: () => Host.Push(new BatteryScreen()), subtitle: "Battery saver, what uses the battery, and charging");
                AddRow("Battery and performance by game", null, "\uE859", click: () => Host.Push(new BatteryInsightScreen()), subtitle: "How long a full battery lasts in each game");
                AddRow("Different TDP when plugged in", s.TdpPluggedWatts > 0 ? TdpControl.Clamp(s.TdpPluggedWatts) + " W" : "Same", "\uE945", click: () => Host.Push(AllyPage("performance", "mode")),
                    subtitle: "In Performance > Operating Mode");
                break;
            case "power/charging":
                AddHeader("Battery health");
                AddRow("Battery health", null, "\uE83F", click: () => Host.Push(new BatteryHealthScreen()), subtitle: "Wear, charge cycles, and how the capacity has changed");
                AddRow("Battery", null, "\uEBB5", click: () => Host.Push(new BatteryScreen()), subtitle: "Charging now, time left and usage");
                break;

            // ---- Lighting
            case "lighting/xbox": NotAlly(); Keep(hardware, "Xbox button light"); break;
            case "lighting/lighting": NotAlly(); Keep(hardware, "Lights"); break;
            case "lighting/settings": NotAlly(); Keep(hardware, "When the lights are on"); break;

            // ---- Audio
            case "audio/audio":
                AddHeader("Sound");
                AddRow("SquareDeck EQ", null, "\uE9E9", click: () => Host.Push(new EqScreen()), subtitle: "An equalizer for everything you hear (with Equalizer APO)");
                AddRow("Interface sounds", s.UiSounds ? "On" : "Off", "\uE767", click: () => Host.Push(new SoundsScreen()), subtitle: "SquareDeck's clicks and chimes, and the navigation sound");
                AddRow("Sound and music settings", null, "\uE8D6", click: () => Host.Push(new SettingsScreen("sound")), subtitle: "Menu music, the Multimedia page and sound output");
                AddRow("Windows sound settings", null, "\uE7F3", click: () => ProcessUtil.Open("ms-settings:sound"), subtitle: "Output and input devices");
                Keep(hardware, "Starting up");
                break;

            // ---- Display
            case "display/visual":
                Keep(hardware, "Display");
                BuildHandheldScreen();
                break;
            case "display/oled": BuildOled(s); break;

            // ---- Connection
            case "connection/network":
                AddHeader("Connection");
                AddRow("Wi-Fi", glyph: "\uE701", click: () => Host.Push(new WifiScreen()));
                AddRow("Bluetooth", glyph: "\uE702", click: () => Host.Push(new BluetoothScreen()));
                AddRow("Network speed test", glyph: "\uE774", click: () => Host.Push(new SpeedTestScreen()));
                AddRow("Network troubleshooting", glyph: "\uE9D9", click: () => Host.Push(new NetworkToolsScreen()));
                break;

            // ---- Shortcuts and Key Binding
            case "shortcuts/actions": NotAlly(); Keep(hardware, "Button actions"); break;
            case "shortcuts/back": NotAlly(); Keep(hardware, "Back buttons"); break;

            // ---- Devices
            case "devices/devices":
                Keep(hardware, "External controller");
                AddHeader("Devices");
                AddRow("Bluetooth devices", glyph: "\uE702", click: () => Host.Push(new BluetoothScreen()), subtitle: "Controllers, headphones and keyboards");
                AddRow("Game Bar widget", null, "\uE7FC", click: () => Host.Push(new SettingsScreen("controller") { FocusLabel = "Install the widget" }),
                    subtitle: "SquareDeck in Game Bar, over your games (in Controller and Game Bar widget)");
                break;
        }
    }

    private void CalibrateRow(CalibrationScreen.Kind kind)
    {
        if (!IsAlly) return;
        if (!Rows.Children.OfType<TextBlock>().Any(t => t.Tag as string == "group" && t.Text == "Calibration")) AddHeader("Calibration");
        AddRow("Recalibrate " + CalibrationScreen.NameOf(kind).ToLowerInvariant(), null, "\uE7C9", click: () => Host.Push(new CalibrationScreen(kind)),
            subtitle: "Measures drift and reach, and sets the dead zone and travel to match");
    }

    /// <summary>Calibration: a card for each part, as Armoury Crate lays them out (left stick, right stick, triggers, gyro).</summary>
    private void BuildCalibrationTiles()
    {
        var grid = new UniformGrid { Columns = 5, Tag = "pinned", Margin = new Thickness(0, 4, 0, 12) };
        var kinds = new List<CalibrationScreen.Kind>();
        if (IsAlly) kinds.AddRange(new[] { CalibrationScreen.Kind.LeftStick, CalibrationScreen.Kind.RightStick, CalibrationScreen.Kind.LeftTrigger, CalibrationScreen.Kind.RightTrigger });
        kinds.Add(CalibrationScreen.Kind.Gyro);
        foreach (var kind in kinds)
        {
            var k = kind;
            var face = new Grid { Height = 250 };
            var art = new System.Windows.Shapes.Ellipse { Width = 92, Height = 92, StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
            art.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "FgBrush");
            if (k is CalibrationScreen.Kind.LeftTrigger or CalibrationScreen.Kind.RightTrigger) { art.Width = 60; art.Height = 110; }
            face.Children.Add(art);
            var glyph = Ui.Glyph(k == CalibrationScreen.Kind.Gyro ? "\uE7C9" : "\uE7FC", 30, "FgBrush");
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 0, 40);
            face.Children.Add(glyph);
            var name = Ui.Text(CalibrationScreen.NameOf(k), 17, "FgBrush");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.Margin = new Thickness(0, 0, 0, 22);
            face.Children.Add(name);
            var tile = new Button { Style = Switch2Pages.TileStyle, Content = face, Margin = new Thickness(0, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            tile.Click += (_, _) => Host.Push(new CalibrationScreen(k));
            grid.Children.Add(tile);
        }
        Rows.Children.Add(grid);
        AddNote("Sticks: leave the stick alone, then turn it in big circles; SquareDeck sets that stick's dead zone just past any drift, and its travel to how far it reaches. " +
                "Triggers: let go, then press all the way. Gyro: lay the Ally flat. Back to Default Settings (Y) puts a stick or trigger back to its factory calibration, in the controller itself.");
    }

    /// <summary>Gamepad Mode > Gyro: gyro aiming (as the mouse), as Armoury Crate lays it out.</summary>
    private void BuildGyro(Settings s)
    {
        void Set(Action change) { change(); s.Save(); GyroAim.Configure(s); Rebuild(); }
        if (!GyroAim.Available) AddNote("Windows doesn't show a gyro on this device, so gyro aiming can't work here.");
        AddHeader("Gyro behavior");
        AddRow("Gyro behavior", s.GyroMode == "mouse" ? "As mouse" : "Off", "\uE962", click: () => Set(() => s.GyroMode = s.GyroMode == "mouse" ? "off" : "mouse"),
            subtitle: "As mouse: turning and tilting the Ally moves the mouse, for games that aim with a mouse (and the desktop)");
        if (s.GyroMode != "mouse") { AddRow("Calibrate the gyro", null, "\uE7C9", click: () => Host.Push(new CalibrationScreen(CalibrationScreen.Kind.Gyro))); return; }
        AddHeader("Accuracy");
        AddRow("Gyro sensitivity", s.GyroSensitivity + "%", "\uE7C9", click: async () =>
        {
            var dialog = new SliderDialog("Gyro sensitivity", "How far the aim moves for each turn of the Ally. 100% is a good start.", "Sensitivity", "\uE7C9",
                s.GyroSensitivity, null, 10, 400, v => v + "%", step: 10);
            Host.Push(dialog);
            int v = await dialog.Result;
            Set(() => s.GyroSensitivity = Math.Clamp(v, 10, 400));
        });
        AddRow("Dead zone", s.GyroDeadzone + "%", "\uE7C9", click: async () =>
        {
            var dialog = new SliderDialog("Gyro dead zone", "How much movement to ignore before it aims. Raise it if the aim picks up your breathing or your hands' shake.", "Dead zone", "\uE7C9",
                s.GyroDeadzone, null, 0, 100, v => v + "%", step: 5);
            Host.Push(dialog);
            int v = await dialog.Result;
            Set(() => s.GyroDeadzone = Math.Clamp(v, 0, 100));
        });
        AddRow("Left and right by", s.GyroHorizontal == "roll" ? "Tilting sideways (roll)" : "Turning (yaw)", "\uE7AD",
            click: () => Set(() => s.GyroHorizontal = s.GyroHorizontal == "roll" ? "yaw" : "roll"), subtitle: "Turn the Ally like a steering wheel or swing it left and right");
        AddRow("Invert horizontal axis", OnOff(s.GyroInvertX), "\uE7AD", click: () => Set(() => s.GyroInvertX = !s.GyroInvertX));
        AddRow("Invert vertical axis", OnOff(s.GyroInvertY), "\uE7AD", click: () => Set(() => s.GyroInvertY = !s.GyroInvertY),
            subtitle: "Tilting the screen up aims down, and down aims up");
        AddHeader("Controls");
        AddRow("Gyro enable button", GyroAim.Buttons.FirstOrDefault(b => b.Id == s.GyroButton).Name ?? "Always on", "\uE7FC", click: async () =>
        {
            int c = await Host.AskAsync("Gyro enable button", "Set a button to turn the gyro on while it's held, or keep it on all the time.", GyroAim.Buttons.Select(b => b.Name).ToArray());
            if (c >= 0) Set(() => s.GyroButton = GyroAim.Buttons[c].Id);
        }, subtitle: "Set a button to enable the gyro function");
        AddRow("Only while a game is running", OnOff(s.GyroOnlyInGames), "\uE768", click: () => Set(() => s.GyroOnlyInGames = !s.GyroOnlyInGames),
            subtitle: "So it doesn't move the pointer in SquareDeck or on the desktop");
        AddRow("Calibrate the gyro", null, "\uE7C9", click: () => Host.Push(new CalibrationScreen(CalibrationScreen.Kind.Gyro)),
            subtitle: "Lay the Ally flat and still, so the aim doesn't drift");
        AddRow("Reset", null, "\uE777", click: () => Set(() =>
        {
            s.GyroSensitivity = 100; s.GyroDeadzone = 0; s.GyroButton = "always"; s.GyroInvertX = s.GyroInvertY = false; s.GyroHorizontal = "yaw"; s.GyroOnlyInGames = true;
        }), subtitle: "The gyro settings back to how they start (the calibration stays)");
    }

    /// <summary>Display > OLED Settings: OLED Care.</summary>
    private void BuildOled(Settings s)
    {
        AddHeader("OLED Care");
        AddNote("Optimization settings that keep still things from wearing an OLED screen and make it last longer.");
        int[] times = { 0, 5, 10, 15, 30, 60 };
        AddRow("Pixel refresh", s.OledRefreshMinutes == 0 ? "Off" : s.OledRefreshMinutes + " min", "\uE7F4", click: async () =>
        {
            int c = await Host.AskAsync("Pixel refresh", "A special screen that plays after SquareDeck has been left alone for this long: slow bands of color, so every pixel works evenly. Any button ends it.",
                times.Select(t => t == 0 ? "Off" : t + " minutes").ToArray());
            if (c >= 0) Toggle(() => s.OledRefreshMinutes = times[c]);
        }, subtitle: "Active time: how long SquareDeck waits, idle and with no game running");
        AddRow("Try the pixel refresh", null, "\uE768", click: () => Host.StartScreensaver("pixelrefresh"), subtitle: "Press any button to stop it");
        AddRow("Pixel shift", OnOff(s.OledPixelShift), "\uE7F4", click: () => { Toggle(() => s.OledPixelShift = !s.OledPixelShift); Host.ApplyPixelShift(); },
            subtitle: "SquareDeck's picture moves by a few pixels every few minutes, so nothing stays on the same pixels for hours");
        AddHeader("Windows");
        bool hide = OledCare.TaskbarAutoHide, glass = OledCare.Transparency, dark = OledCare.DarkMode;
        AddRow("Auto-hide the taskbar", OnOff(hide), "\uE90E", click: () => { OledCare.TaskbarAutoHide = !hide; Rebuild(); },
            subtitle: "The taskbar stays out of sight until you need it, instead of sitting still at the bottom");
        AddRow("Transparency effects", OnOff(glass), "\uE790", click: () => { OledCare.Transparency = !glass; Rebuild(); },
            subtitle: "Off keeps Windows' menus and taskbar solid, which is kinder to an OLED screen");
        AddRow("Dark mode", OnOff(dark), "\uE708", click: () => { OledCare.DarkMode = !dark; Rebuild(); },
            subtitle: "Windows and its apps in dark colors: less light, less wear, more battery");
    }

    /// <summary>Device Info: what this device is, and where to look further.</summary>
    private void BuildAllyInfo()
    {
        AddHeader("This device");
        AddRow("Model", Handheld.DisplayName, Ui.HandheldGlyph, click: () => Host.Push(new MyDeviceScreen()));
        AddManualRow(); // 9.0.18
        if (TdpControl.CpuName.Length > 0) AddRow("Processor", TdpControl.CpuName, "\uE950", click: () => Host.Push(new MyDeviceScreen()));
        if (Handheld.Current is { } device)
        {
            AddRow("Power range", $"{device.TdpMin}–{device.TdpMax} W", "\uE945", click: () => Host.Push(AllyPage("performance", "mode")));
            AddNote("SquareDeck can change: " + Handheld.ControlsText(device) + ".");
        }
        if (DisplaySettings.Current() is { } screen) AddRow("Screen", $"{screen.Resolution}  ·  {screen.RefreshRate} Hz", "\uE7F4", click: () => Host.Push(AllyPage("display")));
        if (IsAlly) AddRow("Light zones", AllyHid.ZoneCount == 8 ? "8 (four LEDs round each stick)" : "4 (two halves round each stick)", "\uE781", click: () => Host.Push(AllyPage("lighting")));
        AddHeader("More");
        AddRow("My Device", null, Ui.HandheldGlyph, click: () => Host.Push(new MyDeviceScreen()), subtitle: "Temperatures, fan speed, battery health, power draw and more, live");
        AddRow("Device health", null, "\uE95E", click: () => Host.Push(new DeviceHealthScreen()), subtitle: "What's in control of the lights, TDP and buttons, and one-press fixes");
    }

    /// <summary>Search: every row of the device's pages, opened on its tab.</summary>
    private static void AddAllyIndex(MainWindow host, List<SearchSetting> list)
    {
        if (!Handheld.IsHandheld) return;
        foreach (var page in AllyPages)
        {
            var p = page;
            list.Add(new SearchSetting(p.Name, p.About, Handheld.DisplayName, p.Glyph, () => AllyPage(p.Id)));
            foreach (var tab in p.Tabs)
            {
                var t = tab;
                try
                {
                    var screen = new SettingsScreen(t.Id, p.Id) { Host = host };
                    foreach (var (label, sub) in screen.RowLabels())
                    {
                        string l = label;
                        list.Add(new SearchSetting(l, sub, Handheld.DisplayName + " > " + p.Name, p.Glyph, () => new SettingsScreen(t.Id, p.Id) { FocusLabel = l }));
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
