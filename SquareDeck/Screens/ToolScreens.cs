using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A file (or folder) chooser that works with the controller: common places, drives, then folders
/// and matching files. B goes up a level.
/// </summary>
public sealed class FilePickerScreen : ListScreen
{
    private const int MaxEntries = 600;
    private static string? _lastFolder; // where the previous pick was made, for this run of the launcher

    private readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string[]? _extensions; // null: choose a folder
    private string? _folder;                // null: showing the list of places
    private string? _result;

    public Task<string?> Result => _done.Task;

    /// <param name="extensions">File types to show, e.g. ".png". Null to choose a folder instead of a file.</param>
    public FilePickerScreen(string title, string[]? extensions)
        : base(title, "{Select}  Open        {Back}  Up a level")
    {
        _extensions = extensions;
        if (_lastFolder != null && Directory.Exists(_lastFolder)) _folder = _lastFolder;
    }

    public override void OnClosed() => _done.TrySetResult(_result);

    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.Back || _folder == null) return false;
        GoTo(System.IO.Path.GetDirectoryName(_folder)); // null at a drive root: back to the places list
        return true;
    }

    private void GoTo(string? folder)
    {
        _folder = folder;
        RebuildFromTop();
    }

    private void Finish(string path)
    {
        _lastFolder = _extensions == null ? path : System.IO.Path.GetDirectoryName(path);
        _result = path;
        Host.Pop(this);
    }

    protected override void Build()
    {
        if (_folder == null)
        {
            BuildPlaces();
            return;
        }

        AddNote(_folder);
        AddRow("Up a level", glyph: "", click: () => GoTo(System.IO.Path.GetDirectoryName(_folder)));
        if (_extensions == null) AddRow("Choose this folder", glyph: "", click: () => Finish(_folder));

        try
        {
            var info = new DirectoryInfo(_folder);
            int shown = 0;
            foreach (var dir in info.EnumerateDirectories().Where(Visible).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (shown++ >= MaxEntries) break;
                var path = dir.FullName;
                AddRow(dir.Name, glyph: "", click: () => GoTo(path));
            }

            if (_extensions != null)
            {
                var files = info.EnumerateFiles()
                    .Where(f => Visible(f) && _extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase);
                foreach (var file in files)
                {
                    if (shown++ >= MaxEntries) break;
                    var path = file.FullName;
                    AddRow(file.Name, $"{file.Length / 1024d / 1024d:0.#} MB", "", click: () => Finish(path));
                }
            }
            if (shown >= MaxEntries) AddNote("This folder has more items than can be listed here.");
        }
        catch (Exception)
        {
            AddNote("This folder can't be opened.");
        }
    }

    private static bool Visible(FileSystemInfo item) =>
        (item.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private void BuildPlaces()
    {
        AddHeader("Places");
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Place("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "");
        Place("Downloads", System.IO.Path.Combine(user, "Downloads"), "");
        Place("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
        Place("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "");
        Place("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "");
        Place("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "");
        // Choosing a program: where programs and their shortcuts live, too.
        if (_extensions != null && _extensions.Any(e => e is ".exe" or ".lnk"))
        {
            Place("Start menu programs", Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "\uE8FC");
            Place("Your Start menu programs", Environment.GetFolderPath(Environment.SpecialFolder.Programs), "\uE8FC");
            Place("Program Files", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "\uE8B7");
            Place("Program Files (x86)", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "\uE8B7");
        }

        AddHeader("Drives");
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var root = drive.RootDirectory.FullName;
                string label = drive.VolumeLabel.Length > 0 ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}" : drive.Name.TrimEnd('\\');
                AddRow(label, $"{drive.AvailableFreeSpace / 1024d / 1024d / 1024d:0.#} GB free", "", click: () => GoTo(root));
            }
        }
        catch (Exception) { }

        AddHeader("Other");
        AddRow("Use the Windows file window instead (mouse or touch)", glyph: "", click: UseWindowsDialog);
    }

    private void Place(string name, string path, string glyph)
    {
        if (Directory.Exists(path)) AddRow(name, glyph: glyph, click: () => GoTo(path));
    }

    private void UseWindowsDialog()
    {
        if (_extensions == null)
        {
            var folders = new OpenFolderDialog { Title = "Choose a folder" };
            if (folders.ShowDialog(Host) == true) Finish(folders.FolderName);
            return;
        }
        var pattern = string.Join(";", _extensions.Select(e => "*" + e));
        var files = new OpenFileDialog { Title = "Choose a file", Filter = $"Matching files|{pattern}|All files|*.*" };
        if (files.ShowDialog(Host) == true) Finish(files.FileName);
    }
}

/// <summary>A dialog with one 0–100 slider.</summary>
public sealed class SliderDialog : Screen
{
    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Slider _slider;

    public override bool IsDialog => true;

    /// <summary>The value when the dialog closed.</summary>
    public Task<int> Result => _done.Task;

    public SliderDialog(string title, string message, string label, string glyph, int value, Action<int>? changed = null,
                        int min = 0, int max = 100, Func<int, string>? format = null)
    {
        format ??= v => v == 0 ? "Off" : v + "%";
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(title, 26));
        var note = Ui.Text(message, 18, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.Margin = new Thickness(0, 12, 0, 16);
        panel.Children.Add(note);

        panel.Children.Add(Ui.SliderRow(label, glyph, out _slider, out var text));
        _slider.Minimum = min;
        _slider.Maximum = max;
        _slider.Value = value;
        text.Text = format(value);
        _slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            text.Text = format(v);
            changed?.Invoke(v);
        };

        var done = Ui.Row("Done");
        done.Click += (_, _) => Host.Pop(this);
        panel.Children.Add(done);

        var box = new Border
        {
            Width = 640, Padding = new Thickness(32), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;
    }

    public override void FocusDefault() => _slider.Focus();

    public override void OnClosed() => _done.TrySetResult((int)Math.Round(_slider.Value));
}


/// <summary>PC settings to switch to while one game runs.</summary>
public sealed class GameProfileScreen : ListScreen
{
    private readonly Game _game;

    public GameProfileScreen(Game game) : base("Game profile – " + game.Name)
    {
        _game = game;
    }

    private GameProfile Profile => Host.Library.For(_game.Key).Profile ??= new GameProfile();

    protected override void Build()
    {
        var p = Host.Library.Find(_game.Key)?.Profile ?? new GameProfile();
        AddNote("These are applied when the game starts and put back when it closes. Anything left on “Don't change” is not touched.");
        AddInsight(p);
        AddRow("Resolution", p.Width > 0 ? $"{p.Width} × {p.Height}" : "Don't change", "", click: Resolution);
        AddRow("Refresh rate", p.RefreshRate > 0 ? p.RefreshRate + " Hz" : "Don't change", "", click: Refresh);
        AddRow("HDR", p.Hdr switch { true => "On", false => "Off", _ => "Don't change" }, "", click: HdrChoice);
        AddRow("Power plan", p.PowerPlanName ?? "Don't change", "", click: Plan);
        AddRow("Sound output", p.AudioDeviceName ?? "Don't change", "", click: Output);

        // Power: only offered where TDP control can work (AMD handhelds).
        if (TdpControl.Chip is { } chip)
        {
            AddHeader("Power");
            AddRow("TDP on battery", p.TdpWatts > 0 ? TdpControl.Clamp(p.TdpWatts) + " W" : "Default (" + TdpControl.Clamp(Host.Settings.TdpWatts) + " W)",
                "\uE83F", click: () => TdpChoice(plugged: false),
                subtitle: !TdpControl.Installed ? "Set up TDP control in Settings > " + SettingsScreen.TdpPlace + " first"
                        : !Host.Settings.TdpEnabled ? "TDP control is off in Settings" : null);
            var s = Host.Settings;
            int everydayPlugged = s.TdpPluggedWatts > 0 ? s.TdpPluggedWatts : s.TdpWatts;
            AddRow("TDP when plugged in", p.TdpPluggedWatts > 0 ? TdpControl.Clamp(p.TdpPluggedWatts) + " W"
                    : p.TdpWatts > 0 ? "Same as on battery (" + TdpControl.Clamp(p.TdpWatts) + " W)"
                    : "Default (" + TdpControl.Clamp(everydayPlugged) + " W)",
                "\uEBB5", click: () => TdpChoice(plugged: true));
            AddRow("CPU boost", p.CpuBoost switch { true => "On", false => "Off", _ => "Default" }, "\uE9F5", click: BoostChoice);
            AddRow("Maximum CPU speed", p.MaxCpu > 0 ? (p.MaxCpu >= 100 ? "No limit" : p.MaxCpu + "%") : "Default", "\uEC48", click: CpuChoice);
            AddRow("Windows power mode", p.PowerMode == null ? "Default" : TdpControl.PowerModeName(p.PowerMode), "\uE83F", click: PowerModeChoice);
        }
        // The ROG Ally's lights while this game runs.
        if (AllyControl.IsAlly)
        {
            AddHeader("Lights (ROG Ally)");
            string effect = p.Lights switch
            {
                null => "Game color (default)",
                "everyday" => "Same as outside games",
                var id => AllyControl.LightModes.FirstOrDefault(m => m.Id == id).Name ?? id
            };
            AddRow("Light effect while playing", effect, "\uE781", click: LightsChoice,
                subtitle: Host.Settings.AllyGameLights ? (p.Lights == null ? "The main color of the game's cover" : null)
                                                       : "Game lights are off in Settings > ROG Ally controls, so this isn't used");
            string mode = p.Lights ?? "game";
            if (mode is "solid" or "breathing" or "sticks")
            {
                string one = p.LightColor ?? Host.Settings.AllyLightColor, two = p.LightColor2 ?? Host.Settings.AllyLightColor2;
                AddRow(mode == "sticks" ? "Left stick color" : "Color", AllyControlScreen.ColorName(one), "\uE790",
                    click: () => Host.Push(new LedColorScreen("Light color – " + _game.Name, one, hex => { Profile.LightColor = hex; Saved(); })));
                if (mode is "breathing" or "sticks")
                    AddRow(mode == "sticks" ? "Right stick color" : "Second color", AllyControlScreen.ColorName(two), "\uE790",
                        click: () => Host.Push(new LedColorScreen("Second light color – " + _game.Name, two, hex => { Profile.LightColor2 = hex; Saved(); })));
            }
        }
        // The Ally's fans, rumble and Xbox button light while this game runs.
        if (AllyControl.IsAlly)
        {
            AddHeader("While playing (ROG Ally)");
            AddRow("Fan curve", p.FanCurve == null ? "Default" : AllyControl.FanCurves.FirstOrDefault(f => f.Id == p.FanCurve).Name ?? p.FanCurve, "\uE9CA", click: async () =>
            {
                var curves = AllyControl.FanCurves.Where(f => f.Id.Length > 0).ToArray();
                int choice = await Host.AskAsync("Fan curve while playing", null, curves.Select(f => f.Name + "  \u00B7  " + f.About).Prepend("Default (Settings')").ToArray());
                if (choice < 0) return;
                Profile.FanCurve = choice == 0 ? null : curves[choice - 1].Id;
                Saved();
            }, subtitle: "Needs ASUS's firmware driver");
            AddRow("Rumble strength", p.Vibration is int v ? (v == 0 ? "Off" : v + "%") : "Default", "\uE877", click: async () =>
            {
                int[] levels = { -1, 0, 25, 50, 75, 100 };
                int choice = await Host.AskAsync("Rumble strength while playing", null, levels.Select(l => l < 0 ? "Default (Settings')" : l == 0 ? "Off" : l + "%").ToArray());
                if (choice < 0) return;
                Profile.Vibration = levels[choice] < 0 ? null : levels[choice];
                Saved();
            });
            if (AllyControl.HasXboxLight)
                AddRow("Xbox button light", p.XboxLightOff switch { true => "Off while playing", false => "On while playing", _ => "Default" }, "\uE7FC", click: async () =>
                {
                    int choice = await Host.AskAsync("Xbox button light while playing", null, "Default (Settings')", "Off while playing", "On while playing");
                    if (choice < 0) return;
                    Profile.XboxLightOff = choice switch { 1 => true, 2 => false, _ => null };
                    Saved();
                });
        }
        // The Game Bar widget's quick tiles while this game runs.
        if (Host.Settings.GameBarWidget)
        {
            AddHeader("Game Bar widget");
            AddRow("Quick tiles of its own", p.WidgetQuick != null ? p.WidgetQuick.Count + " tiles" : "Off", "\uE8A9", click: () =>
            {
                Profile.WidgetQuick = Profile.WidgetQuick != null ? null
                    : new List<string>(Host.Settings.WidgetQuick ?? Settings.WidgetQuickTiles.Select(x => x.Id).ToList());
                Saved();
            }, subtitle: p.WidgetQuick != null ? "While it runs, Edit in the widget's Quick card changes this game's tiles" : "Starts from your usual tiles; change them with Edit in the widget while the game runs");
        }
        // AMD Adrenalin settings for this game (with Adrenaline Hook on, and AMD graphics).
        if (Host.Settings.ExperimentAdrenalineHook && AdrenalineHook.HasAmdGraphics) AddAmdSection(p);

        if (!p.IsEmpty) AddRow("Clear this profile", glyph: "", click: () => { Host.Library.For(_game.Key).Profile = null; Saved(); });
    }

    /// <summary>
    /// Battery and performance from the game's sessions, a suggested starting point (Battery saver, Balanced or
    /// Performance, sized to this processor), and "Best battery" (a quarter less power and the lower refresh rate).
    /// </summary>
    private void AddInsight(GameProfile p)
    {
        AddHeader("Battery and performance");
        var i = SessionLog.Insight(_game.Key);
        if (i == null) AddRow("No sessions yet", null, "\uE9D9", subtitle: "Play it for a few minutes: battery use, power, frame rate and temperature are noted");
        else
        {
            var facts = new List<string> { $"{i.Sessions} {(i.Sessions == 1 ? "session" : "sessions")}" };
            if (i.Fps is double fps) facts.Add($"{fps:0} FPS");
            if (i.Temp is double temp) facts.Add($"{temp:0} \u00B0C" + (i.MaxTemp is int max ? $" (peak {max})" : ""));
            AddRow(SessionLog.BatteryText(i) is { } battery ? char.ToUpper(battery[0]) + battery[1..] : "Battery life: play it unplugged for 10 minutes to know",
                null, "\uE859", click: () => Host.Push(new GameSessionsScreen(_game.Key, _game.Name)), subtitle: string.Join("  \u00B7  ", facts));
        }
        if (TdpControl.Chip is not { } chip) return;
        var presets = ProfileAdvisor.Presets(chip);
        var (suggestId, why) = ProfileAdvisor.Suggest(_game.Key);
        var suggested = presets.First(x => x.Id == suggestId);
        AddRow("Suggested settings", suggested.Name, "\uE945", click: async () =>
        {
            var names = presets.Select(x => (x.Id == suggestId ? "\u2605 " : "") + x.Name + $"  \u00B7  {x.Watts} W" + (x.RefreshRate > 0 ? $", {x.RefreshRate} Hz" : "") + "  \u00B7  " + x.About).ToArray();
            int choice = await Host.AskAsync("Suggested for " + _game.Name, why, names);
            if (choice < 0) return;
            ApplyPreset(presets[choice].Name, presets[choice].Watts, presets[choice].RefreshRate);
        }, subtitle: why);
        AddRow("Best battery for this game", null, "\uE863", click: () =>
        {
            var (watts, hz) = ProfileAdvisor.BestBattery(_game.Key, chip);
            ApplyPreset("Best battery", watts, hz);
        }, subtitle: "A quarter less power than it has been using, and the lower refresh rate. You can undo it");
    }

    /// <summary>Sets the TDP on battery (and the refresh rate, when given), with Undo for a minute.</summary>
    private void ApplyPreset(string name, int watts, int refresh)
    {
        int oldWatts = Profile.TdpWatts, oldRefresh = Profile.RefreshRate, oldWidth = Profile.Width, oldHeight = Profile.Height;
        Profile.TdpWatts = TdpControl.Clamp(watts);
        if (refresh > 0) Profile.RefreshRate = refresh;
        Saved();
        var key = _game.Key;
        Host.OfferUndo($"{name} for {_game.Name}", () =>
        {
            var o = Host.Library.For(key).Profile ??= new GameProfile();
            o.TdpWatts = oldWatts; o.RefreshRate = oldRefresh; o.Width = oldWidth; o.Height = oldHeight;
            Host.Library.Save();
        });
        Host.ShowToast("GAME PROFILE", $"{name}: {Profile.TdpWatts} W" + (refresh > 0 ? $", {refresh} Hz" : ""), _game.Name, "", "info", remember: false);
    }

    private void Saved()
    {
        var o = Host.Library.For(_game.Key);
        if (o.Profile is { IsEmpty: true }) o.Profile = null;
        Host.Library.Save();
        AllyControl.ProfileChanged(_game, o.Profile); // shows straight away if the game is running
        Rebuild();
    }

    /// <summary>
    /// The game's entry in AMD Software: its Adrenalin switches, what AMD Software recorded about playing it, and the
    /// metrics overlay while it runs. Steam games are usually in AMD Software already; others can be added from here.
    /// </summary>
    private void AddAmdSection(GameProfile p)
    {
        AddHeader("AMD Adrenalin");
        var entry = AdrenalineHook.FindFor(_game);
        if (entry == null)
        {
            AddRow("Add to AMD Software", "Not in AMD Software", "\uE710", click: async () =>
            {
                Host.ShowToast("ADRENALINE HOOK", "Adding " + _game.Name + "…", "", "", "info");
                var added = await AdrenalineHook.AddGameAsync(_game);
                if (added == null)
                {
                    int pick = await Host.AskAsync("Couldn't find its program", "SquareDeck couldn't tell which program runs " + _game.Name + ". Choose it?", "Choose…", "Cancel");
                    if (pick == 0)
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose " + _game.Name + "'s program", Filter = "Programs (*.exe)|*.exe", CheckFileExists = true,
                            InitialDirectory = !string.IsNullOrEmpty(_game.InstallDir) && System.IO.Directory.Exists(_game.InstallDir) ? _game.InstallDir : null };
                        if (dialog.ShowDialog() == true) AdrenalineHook.Add(new[] { new HookCandidate(_game.Name, dialog.FileName, dialog.FileName, "Manual") });
                    }
                }
                Host.ShowToast("ADRENALINE HOOK", AdrenalineHook.FindFor(_game) != null ? _game.Name + " is in AMD Software" : "Not added",
                    AdrenalineHook.ClosedForChange ? "AMD Software was closed to save it" : "", "", "info");
                Rebuild();
            }, subtitle: _game.IsSteam ? "AMD Software usually adds Steam games by itself; it hasn't found this one yet"
                                       : "So AMD Software's per-game settings work for it");
        }
        else
        {
            // What AMD Software has recorded about playing it.
            var facts = new List<string>();
            if (entry.PlayedMinutes > 0) facts.Add(AdrenalineGameScreen.Played(entry.PlayedMinutes));
            if (entry.AverageFps > 0) facts.Add($"average {entry.AverageFps:0} FPS");
            if (entry.Percentile95Ms > 0) facts.Add($"95% of frames under {entry.Percentile95Ms:0.0} ms");
            AddNote("In AMD Software as \u201C" + entry.Title + "\u201D" + (entry.SteamId > 0 ? " (added by AMD Software from Steam)" : "") +
                    (facts.Count > 0 ? ". AMD Software recorded: " + string.Join(", ", facts) + "." : "."));
            var sw = AdrenalineHook.Switches(entry.Guid);
            if (sw != null)
            {
                string key = entry.Guid;
                void Change(Func<AdrenalineHook.GameSwitches, AdrenalineHook.GameSwitches> edit)
                {
                    var now = AdrenalineHook.Switches(key);
                    if (now == null) return;
                    bool ok = AdrenalineHook.SetSwitches(key, edit(now));
                    Host.ShowToast("AMD ADRENALIN", ok ? "Saved for " + _game.Name : "Couldn't save", AdrenalineHook.ClosedForChange ? "AMD Software was closed to save it" : "", "", "info");
                    Rebuild();
                }
                static string OnOff(bool on) => on ? "On" : "Off";
                AddRow("Radeon Super Resolution", OnOff(sw.Upscaling), "\uE740", click: () => Change(x => x with { Upscaling = !x.Upscaling }),
                    subtitle: "Upscaling for when the game runs below the screen's resolution");
                if (sw.Upscaling)
                    AddRow("Sharpness", sw.Sharpness + "%", "\uE7B3", click: async () =>
                    {
                        var dialog = new SliderDialog("Sharpness – " + _game.Name, "How much Radeon Super Resolution sharpens the picture.", "Sharpness", "\uE7B3",
                            sw.Sharpness, null, 0, 100, v => v + "%");
                        Host.Push(dialog);
                        int value = await dialog.Result;
                        Change(x => x with { Sharpness = value });
                    });
                AddRow("Fluid Motion Frames", OnOff(sw.FrameGen), "\uE916", click: () => Change(x => x with { FrameGen = !x.FrameGen }),
                    subtitle: "AMD's frame generation, for smoother motion");
                if (sw.FrameGen)
                    AddRow("Frame generation mode", sw.FrameGenMode == 1 ? "Performance" : "Quality", "\uE9E9",
                        click: () => Change(x => x with { FrameGenMode = x.FrameGenMode == 1 ? 0 : 1 }));
                AddRow("Force FreeSync", OnOff(sw.FreeSync), "\uE7F4", click: () => Change(x => x with { FreeSync = !x.FreeSync }));
            }
            AddRow("Performance metrics while playing", p.AmdMetricsOverlay ? "On" : "Off", "\uE9D9", click: () =>
            {
                Profile.AmdMetricsOverlay = !Profile.AmdMetricsOverlay;
                Saved();
            }, subtitle: "AMD Software's FPS and temperature overlay shows by itself when " + _game.Name + " starts, and goes when it closes");
            var e = entry;
            AddRow("More in Adrenaline Hook", null, "\uE768", click: () => Host.Push(new AdrenalineGameScreen(e.Title, e.ExePath, e.Guid)),
                subtitle: "Favorite and hidden in AMD Software, its folder, removing it");
        }
    }

    private async void LightsChoice()
    {
        // The game's color first (the default), then the same as outside games, then every effect.
        var options = new List<(string? Id, string Name)> { (null, "Game color (default)"), ("everyday", "Same as outside games") };
        options.AddRange(AllyControl.LightModes.Where(m => m.Id is not ("" or "game")).Select(m => ((string?)m.Id, m.Name)));
        int choice = await Host.AskAsync("Light effect while playing", "Used while " + _game.Name + " runs, with game lights on.",
            options.Select(o => o.Name).ToArray());
        if (choice < 0 || choice >= options.Count) return;
        Profile.Lights = options[choice].Id;
        if (options[choice].Id is not ("solid" or "breathing" or "sticks")) { Profile.LightColor = null; Profile.LightColor2 = null; }
        Saved();
    }

    private async void Resolution()
    {
        var sizes = DisplaySettings.All().Select(m => (m.Width, m.Height)).Distinct().Take(12).ToList();
        var labels = sizes.Select(s => $"{s.Width} × {s.Height}").Prepend("Don't change").ToArray();
        int choice = await Host.AskAsync("Resolution while playing", null, labels);
        if (choice < 0) return;
        (Profile.Width, Profile.Height) = choice == 0 ? (0, 0) : sizes[choice - 1];
        Saved();
    }

    private async void Refresh()
    {
        var rates = DisplaySettings.All().Select(m => m.RefreshRate).Distinct().OrderByDescending(r => r).Take(10).ToList();
        int choice = await Host.AskAsync("Refresh rate while playing",
            "If the display doesn't support the rate at the chosen resolution, it is left as it is.",
            rates.Select(r => r + " Hz").Prepend("Don't change").ToArray());
        if (choice < 0) return;
        Profile.RefreshRate = choice == 0 ? 0 : rates[choice - 1];
        Saved();
    }

    private async void HdrChoice()
    {
        if (Hdr.Get() == null)
        {
            await Host.AskAsync("HDR isn't available", "Windows reports that this display doesn't support HDR.", "OK");
            return;
        }
        int choice = await Host.AskAsync("HDR while playing", null, "Don't change", "On", "Off");
        if (choice < 0) return;
        Profile.Hdr = choice switch { 1 => true, 2 => false, _ => null };
        Saved();
    }

    private async void Plan()
    {
        var plans = await PowerSettings.GetPlansAsync();
        int choice = await Host.AskAsync("Power plan while playing", null, plans.Select(p => p.Name).Prepend("Don't change").ToArray());
        if (choice < 0) return;
        (Profile.PowerPlanId, Profile.PowerPlanName) = choice == 0 ? (null, null) : (plans[choice - 1].Id, plans[choice - 1].Name);
        Saved();
    }

    private async void TdpChoice(bool plugged)
    {
        var chip = TdpControl.Chip;
        if (chip == null) return;
        string when = plugged ? "when plugged in" : "on battery";
        int choice = await Host.AskAsync("TDP " + when, plugged ? "Used while the charger is connected." : "Used while running on battery.",
            plugged ? "Same as on battery" : "Default", "Choose a limit…");
        if (choice < 0) return;
        if (choice == 0)
        {
            if (plugged) Profile.TdpPluggedWatts = 0; else Profile.TdpWatts = 0;
            Saved();
            return;
        }
        var s = Host.Settings;
        int start = plugged ? (Profile.TdpPluggedWatts > 0 ? Profile.TdpPluggedWatts : s.TdpPluggedWatts > 0 ? s.TdpPluggedWatts : chip.Max)
                            : (Profile.TdpWatts > 0 ? Profile.TdpWatts : s.TdpWatts);
        var dialog = new SliderDialog("TDP " + when + " – " + _game.Name, "Applied while the game runs and put back when it closes.",
            "Watts", "\uE945", TdpControl.Clamp(start), null, chip.Min, chip.Max, v => v + " W");
        Host.Push(dialog);
        int watts = TdpControl.Clamp(await dialog.Result);
        if (plugged) Profile.TdpPluggedWatts = watts; else Profile.TdpWatts = watts;
        Saved();
    }

    private async void BoostChoice()
    {
        int choice = await Host.AskAsync("CPU boost while playing", null, "Default", "On", "Off");
        if (choice < 0) return;
        Profile.CpuBoost = choice switch { 1 => true, 2 => false, _ => null };
        Saved();
    }

    private async void CpuChoice()
    {
        int[] caps = { 0, 100, 90, 80, 70, 60, 50 };
        int choice = await Host.AskAsync("Maximum CPU speed while playing", null, caps.Select(c => c == 0 ? "Default" : c >= 100 ? "No limit" : c + "%").ToArray());
        if (choice < 0) return;
        Profile.MaxCpu = caps[choice];
        Saved();
    }

    private async void PowerModeChoice()
    {
        var modes = TdpControl.WindowsPowerModes.Skip(1).ToArray(); // "Don't change" is "Default" here
        int choice = await Host.AskAsync("Windows power mode while playing", null, modes.Select(m => m.Name).Prepend("Default").ToArray());
        if (choice < 0) return;
        Profile.PowerMode = choice == 0 ? null : modes[choice - 1].Id;
        Saved();
    }

    private async void Output()
    {
        var outputs = await Task.Run(Audio.GetOutputs);
        int choice = await Host.AskAsync("Sound output while playing", null, outputs.Select(o => o.Name).Prepend("Don't change").ToArray());
        if (choice < 0) return;
        (Profile.AudioDeviceId, Profile.AudioDeviceName) = choice == 0 ? (null, null) : (outputs[choice - 1].Id, outputs[choice - 1].Name);
        Saved();
    }
}

/// <summary>One game's achievements, straight from the Y menu.</summary>
public sealed class GameAchievementsScreen : ListScreen
{
    private readonly Game _game;
    private readonly CancellationTokenSource _cts = new();
    private List<Achievement>? _list;
    private string? _note = "Loading achievements…";

    public GameAchievementsScreen(Game game) : base("Achievements – " + game.Name, "{Back}  Back")
    {
        _game = game;
    }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            var mine = await Task.Run(() => SteamStoreClient.GetAchievementsAsync(_game.AppId, ct), ct);
            if (mine != null)
            {
                _list = mine.OrderByDescending(a => a.Unlocked == true).ToList();
                _note = $"{mine.Count(a => a.Unlocked == true)} of {mine.Count} unlocked";
                if (Host.Achievements.NoteProgress(_game.AppId, mine)) Host.Achievements.Save();
            }
            else
            {
                var details = await Task.Run(() => SteamStoreClient.GetDetailsAsync(_game.AppId, ct), ct);
                _list = details?.HighlightedAchievements ?? new();
                _note = _list.Count == 0
                    ? "This game has no achievements, or Steam couldn't be reached."
                    : $"Showing {_list.Count} of {details!.AchievementTotal}. Set “Game details” to Public in your Steam " +
                      "profile's privacy settings to see them all and which ones you have.";
            }
        }
        catch (OperationCanceledException) { return; }
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (_note != null) AddNote(_note);
        if (_list == null) return;
        foreach (var a in _list.Take(200))
        {
            var icon = new Image();
            if (a.Icon.Length > 0) _ = LoadAsync(icon, a.Icon);
            var achievement = a;
            bool guide = Labs.On("guides") && a.Unlocked != true;
            var row = AddRow(a.Description.Length > 0 ? a.Name + "  –  " + a.Description : a.Name,
                a.Unlocked switch { true => "Unlocked ✓", false => guide ? "How to get this" : "Locked", _ => guide ? "How to get this" : null }, icon: icon,
                click: guide ? () => ProcessUtil.Open(Labs.GuideSearch(_game, achievement.Name)) : null);
            if (a.Unlocked == false) row.Opacity = 0.6;
        }
    }

    private static async Task LoadAsync(Image view, string url) =>
        view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 64));
}


/// <summary>Ping, download and upload speed.</summary>
public sealed class SpeedTestScreen : ListScreen
{
    private readonly CancellationTokenSource _cts = new();
    private string _ping = "–", _down = "–", _up = "–";
    private string? _status;
    private bool _running;

    public SpeedTestScreen() : base("Network speed test") { }

    public override void OnClosed() => _cts.Cancel();

    protected override void Build()
    {
        AddRow(_running ? "Testing…" : "Start test", glyph: "", click: () => _ = RunAsync());
        if (_status != null) AddNote(_status);
        AddHeader("Results");
        AddRow("Ping", _ping, "");
        AddRow("Download", _down, "");
        AddRow("Upload", _up, "");
        AddNote("Measured against public speed-test servers (Cloudflare first, with others as a fallback). " +
                "The test can use a few hundred MB on a fast connection, so avoid it on a metered one.");
    }

    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        var ct = _cts.Token;
        try
        {
            _ping = _down = _up = "…";
            _status = "Measuring ping…";
            Rebuild();
            var ping = await SpeedTest.PingAsync();
            _ping = ping == null ? "No reply" : $"{ping:0} ms";

            _status = "Measuring download speed…";
            Rebuild();
            var down = await Task.Run(() => SpeedTest.DownloadAsync(ct), ct);
            _down = down == null ? "No test server answered" : $"{down:0.#} Mbps";

            _status = "Measuring upload speed…";
            Rebuild();
            var up = await Task.Run(() => SpeedTest.UploadAsync(ct), ct);
            _up = up == null ? "Not available" : $"{up:0.#} Mbps";
            _status = null;
        }
        catch (OperationCanceledException) { return; }
        finally { _running = false; }
        if (Host.IsTop(this)) Rebuild();
    }
}

/// <summary>Live picture of the controller: sticks, triggers and every button, plus a rumble test.</summary>
public sealed class ControllerTestScreen : Screen
{
    private static readonly (string Label, ushort Mask)[] ButtonMap =
    {
        ("A", 0x1000), ("B", 0x2000), ("X", 0x4000), ("Y", 0x8000), ("LB", 0x0100), ("RB", 0x0200),
        ("View", 0x0020), ("Menu", 0x0010), ("L3", 0x0040), ("R3", 0x0080),
        ("Up", 0x0001), ("Down", 0x0002), ("Left", 0x0004), ("Right", 0x0008), ("Guide", 0x0400)
    };

    private const double StickSize = 150;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Ellipse _leftDot = Dot(), _rightDot = Dot();
    private readonly Border _leftTrigger = Bar(), _rightTrigger = Bar();
    private readonly Dictionary<ushort, Border> _chips = new();
    private readonly TextBlock _status = Ui.Text("", 18, "SubtleBrush");
    private readonly Button _first;

    public ControllerTestScreen()
    {
        var sticks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        sticks.Children.Add(TriggerColumn("LT", _leftTrigger));
        sticks.Children.Add(StickPad("Left stick", _leftDot));
        sticks.Children.Add(StickPad("Right stick", _rightDot));
        sticks.Children.Add(TriggerColumn("RT", _rightTrigger));

        var chips = new WrapPanel { Margin = new Thickness(0, 18, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (label, mask) in ButtonMap)
        {
            var chip = new Border
            {
                MinWidth = 62, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            ((TextBlock)chip.Child).SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            chip.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
            _chips[mask] = chip;
            chips.Children.Add(chip);
        }

        var actions = new StackPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        _first = Ui.Row("Rumble test: light", glyph: "");
        _first.Click += (_, _) => Host.RumbleTest(35);
        actions.Children.Add(_first);
        var strong = Ui.Row("Rumble test: strong", glyph: "");
        strong.Click += (_, _) => Host.RumbleTest(100);
        actions.Children.Add(strong);

        var body = new StackPanel();
        body.Children.Add(_status);
        body.Children.Add(sticks);
        body.Children.Add(chips);
        body.Children.Add(actions);
        SetPage("Controller test", body, "{Back}  Back  (it lights up here too)");

        _timer.Tick += (_, _) => Refresh();
    }

    private static Ellipse Dot()
    {
        var dot = new Ellipse { Width = 26, Height = 26 };
        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        return dot;
    }

    private static Border Bar()
    {
        var bar = new Border { Width = 26, Height = 0, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4) };
        bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        return bar;
    }

    private static FrameworkElement StickPad(string label, Ellipse dot)
    {
        var ring = new Ellipse { Width = StickSize, Height = StickSize, StrokeThickness = 3 };
        ring.SetResourceReference(Shape.StrokeProperty, "SubtleBrush");
        ring.SetResourceReference(Shape.FillProperty, "CircleBrush");
        var canvas = new Canvas { Width = StickSize, Height = StickSize };
        canvas.Children.Add(ring);
        canvas.Children.Add(dot);
        var caption = Ui.Text(label, 15, "SubtleBrush");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 6, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(18, 0, 18, 0) };
        stack.Children.Add(canvas);
        stack.Children.Add(caption);
        return stack;
    }

    private static FrameworkElement TriggerColumn(string label, Border bar)
    {
        var track = new Grid { Width = 26, Height = StickSize };
        var back = new Border { CornerRadius = new CornerRadius(4) };
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        track.Children.Add(back);
        track.Children.Add(bar);
        var caption = Ui.Text(label, 15, "SubtleBrush");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 6, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        stack.Children.Add(track);
        stack.Children.Add(caption);
        return stack;
    }

    public override void FocusDefault() => _first.Focus();

    public override void OnShown()
    {
        _timer.Start();
        Refresh();
    }

    public override void OnClosed() => _timer.Stop();

    // Nothing to read with SquareDeck in the background: the readout pauses.
    public override void OnAppRest(bool resting) { if (resting) _timer.Stop(); else if (Host.IsTop(this)) _timer.Start(); }

    private static void Place(Ellipse dot, short x, short y)
    {
        double half = StickSize / 2, reach = half - 16;
        Canvas.SetLeft(dot, half + x / 32767.0 * reach - dot.Width / 2);
        Canvas.SetTop(dot, half - y / 32767.0 * reach - dot.Height / 2);
    }

    private void Refresh()
    {
        var s = Host.PadState;
        _status.Text = s.Connected
            ? "Controller connected" + (Host.ControllerBattery is string b ? "  ·  battery: " + b : "")
            : "No controller detected. Connect one or press a button on it.";

        Place(_leftDot, s.LeftX, s.LeftY);
        Place(_rightDot, s.RightX, s.RightY);
        _leftTrigger.Height = s.LeftTrigger / 255.0 * StickSize;
        _rightTrigger.Height = s.RightTrigger / 255.0 * StickSize;

        foreach (var (mask, chip) in _chips)
        {
            if ((s.Buttons & mask) != 0) chip.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else chip.Background = Brushes.Transparent;
        }
    }
}

/// <summary>The game stores installed on this PC, and a way to get the others.</summary>
public sealed class StoresScreen : GridScreen
{
    /// <summary>ubiquity: the page is laid out like its home screen (see <see cref="BuildShowcase"/>).</summary>
    private readonly bool _cine = Looks.Current.Id is "ubiquity" or "aniki" or "solaris" or "orbit";
    /// <summary>SquareDeck: labeled rows ("Stores", "More") with no big space at the top, like a handheld's library.</summary>
    private readonly bool _rows = Looks.Current.Id == "deck";
    private double TileSize => _cine ? 150 : 170;
    private List<StoreApp> _all = new();
    private readonly WrapPanel _more = new();
    /// <summary>Free games, the wishlist and the release calendar: their own section, under the stores.</summary>
    private readonly WrapPanel _deals = new();
    private TextBlock? _dealsLabel;
    private readonly Image _heroImage = new() { Height = 150, MaxWidth = 300, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _heroGlyph = Ui.Glyph("", 110, "AccentBrush");
    private readonly TextBlock _heroName = Ui.Text("", 44, "FgBrush");
    private readonly TextBlock _heroAbout = Ui.Text("", 17, "SubtleBrush");

    public StoresScreen() : base("Stores", "{Select}  Open      {Options}  Hide or uninstall      {Back}  Back") { }

    public override void OnShown() => _ = LoadAsync();

    public override void OnResumed() => _ = LoadAsync(); // a store may just have been installed or removed

    private async Task LoadAsync()
    {
        _all = await Task.Run(Stores.Detect);
        Tiles.Children.Clear();
        _more.Children.Clear();
        _deals.Children.Clear();
        if (_cine || _rows) BuildShowcase();
        else if (!Body.Children.Contains(_deals))
        {
            // The plain grid: the stores, then their own section for deals and releases.
            _dealsLabel = Ui.Text("Deals and releases", 22, "FgBrush", bold: true);
            _dealsLabel.Margin = new Thickness(10, 26, 0, 10);
            Body.Children.Insert(Body.Children.IndexOf(Tiles) + 1, _dealsLabel);
            Body.Children.Insert(Body.Children.IndexOf(Tiles) + 2, _deals);
        }
        var hidden = Host.Settings.HiddenStores;

        // Recomp Zone first: native PC versions of classic games, made by fans.
        if (Labs.On("recomp"))
        {
            // Recomp Zone's own emblem (the SquareDeck mark in Recomp Zone's colors), or a plain icon without it.
            string emblem = System.IO.Path.Combine(AppContext.BaseDirectory, "RecompZone", "logo.png");
            FrameworkElement face = GlyphBox(TileSize, TileSize, "\uE7FC");
            if (System.IO.File.Exists(emblem) && ImageLoader.FromFile(emblem, (int)(TileSize * 2)) is { } picture)
            {
                var box = ImageBox(TileSize, TileSize, out var image);
                image.Source = picture;
                face = box;
            }
            AddTile(face, _cine ? null : "Recomp Zone", TileSize, () => Host.Push(new RecompZoneScreen()), "Recomp Zone");
        }

        // Retro Lounge beside it: emulators and your own classic games (it lives in the Library too).
        {
            FrameworkElement face = GlyphBox(TileSize, TileSize, "\uE7FC");
            if (RetroUi.Logo((int)(TileSize * 2)) is { } picture)
            {
                var box = ImageBox(TileSize, TileSize, out var image);
                image.Source = picture;
                face = box;
            }
            AddTile(face, _cine ? null : "Retro Lounge", TileSize, () => Host.Push(new RetroLoungeScreen()), "Retro Lounge");
        }

        foreach (var store in _all.Where(s => s.Installed && !hidden.Contains(s.Name)))
        {
            var s = store;
            var box = ImageBox(TileSize, TileSize, out var icon);
            icon.Stretch = Stretch.Uniform;
            icon.Margin = new Thickness(34);
            AddTile(box, _cine ? null : s.Name, TileSize, () =>
            {
                if (!Stores.Open(s)) _ = Host.AskAsync("Couldn't open " + s.Name, "Its app didn't start.", "OK");
            }, s);
            // The store's own logo where SquareDeck has one; otherwise its app icon on a gradient of its colors.
            if (StoreLogos.For(s.Name) is { } logo) { icon.Source = logo; icon.Margin = new Thickness(28); }
            else _ = LoadIconAsync(icon, box, s);
        }

        // Cloud gaming services turned on in Settings, with their logos.
        foreach (var service in CloudService.All.Where(c => Host.Settings.CloudServices.Contains(c.Id)))
        {
            var cs = service;
            AddTile(cs.Face(TileSize, TileSize, _cine ? 2 : 18), _cine ? null : cs.Name, TileSize, () => Host.Push(new CloudGameScreen(cs)), cs.Name);
        }

        // Deals and releases, in their own section: free games, the wishlist and its prices, the release calendar.
        int onSale = Host.PriceWatch.Games.Count(g => g.OnSale);
        Panel? extras = _cine || _rows ? _more : null;
        double small = _cine ? 110 : TileSize;
        if (Labs.On("freegames"))
            AddTile(GlyphBox(small, small, "\uE719"), _cine ? null : "Free games", small, () => Host.Push(new FreeGamesScreen()), "Free games", _deals);
        string wish = onSale > 0 ? $"Wishlist · {onSale} on sale" : "Wishlist and sales";
        AddTile(GlyphBox(small, small, "\uE8EC"), _cine ? null : wish, small, () => Host.Push(new WishlistScreen()), wish, _deals);
        AddTile(GlyphBox(small, small, "\uE787"), _cine ? null : "Release calendar", small, () => Host.Push(new ReleaseCalendarScreen()), "Release calendar", _deals);
        AddTile(GlyphBox(small, small, "\uE710"), _cine ? null : "Add more stores", small, () => Host.Push(new AddStoresScreen()), "Add more stores", extras);
        int hiddenInstalled = _all.Count(s => s.Installed && hidden.Contains(s.Name));
        if (hiddenInstalled > 0)
            AddTile(GlyphBox(small, small, "\uE7B3"), _cine ? null : $"Hidden stores ({hiddenInstalled})", small, () => _ = ShowHiddenAsync(), $"Hidden stores ({hiddenInstalled})", extras);
        if (_cine) HookShowcase();
        FocusDefaultLater();
    }

    /// <summary>
    /// ubiquity's Stores page, laid out like its home screen: the store you're on large at the top left (its logo, its
    /// name in thin spaced capitals, a line about it), "S T O R E S" over a row of small square tiles, then
    /// "M O R E" over smaller ones for the wishlist, the release calendar and adding stores. No names under the tiles.
    /// </summary>
    private void BuildShowcase()
    {
        Body.Children.Clear();
        Body.Margin = new Thickness(40, 0, 0, 0);
        string look = Looks.Current.Id;
        var hero = new StackPanel { Margin = new Thickness(8, 10, 0, 34), Height = 300, Visibility = _cine ? Visibility.Visible : Visibility.Collapsed };
        var picture = new Grid { Height = 150, HorizontalAlignment = HorizontalAlignment.Left };
        _heroGlyph.HorizontalAlignment = HorizontalAlignment.Left;
        _heroGlyph.VerticalAlignment = VerticalAlignment.Center;
        _heroImage.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6 };
        picture.Children.Add(_heroImage);
        picture.Children.Add(_heroGlyph);
        hero.Children.Add(picture);
        // The name in each look's type: thin spaced capitals (ubiquity), heavy (Aniki), light capitals (Solaris), light (Orbit).
        _heroName.FontWeight = look switch { "aniki" => FontWeights.Black, _ => FontWeights.Light };
        _heroName.FontSize = look switch { "aniki" => 56, "solaris" => 60, "orbit" => 50, _ => 44 };
        _heroName.Margin = new Thickness(0, 22, 0, 0);
        hero.Children.Add(_heroName);
        var line = new Border { Width = 64, Height = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 12, 0, 10),
                                Visibility = look == "ubiquity" ? Visibility.Visible : Visibility.Collapsed };
        line.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        hero.Children.Add(line);
        if (look != "ubiquity") _heroAbout.Margin = new Thickness(2, 10, 0, 0);
        _heroAbout.TextWrapping = TextWrapping.Wrap;
        _heroAbout.TextTrimming = TextTrimming.None;
        _heroAbout.MaxWidth = 720;
        hero.Children.Add(_heroAbout);
        Body.Children.Add(hero);

        TextBlock Label(string text)
        {
            var l = look switch
            {
                "ubiquity" => Ui.Text(LookChrome.Spaced(text), 14, "SubtleBrush"),
                "solaris" => Ui.Text(text.ToUpperInvariant(), 13, "AccentBrush", bold: true),
                "deck" => Ui.Text(text, 22, "FgBrush", bold: true),
                _ => Ui.Text(text, 20, "FgBrush", bold: look == "aniki")
            };
            if (look == "orbit") l.FontWeight = FontWeights.Light;
            l.Margin = new Thickness(10, 0, 0, look == "deck" ? 8 : 2);
            return l;
        }
        Body.Children.Add(Label("Stores"));
        Body.Children.Add(Tiles);
        var dealsLabel = Label("Deals and releases");
        dealsLabel.Margin = new Thickness(10, 18, 0, 2);
        Body.Children.Add(dealsLabel);
        Body.Children.Add(_deals);
        var moreLabel = Label("More");
        moreLabel.Margin = new Thickness(10, 18, 0, 2);
        Body.Children.Add(moreLabel);
        Body.Children.Add(_more);
    }

    /// <summary>Moving onto a tile shows it in the big space at the top.</summary>
    private void HookShowcase()
    {
        foreach (var tile in Tiles.Children.OfType<Button>().Concat(_deals.Children.OfType<Button>()).Concat(_more.Children.OfType<Button>()))
        {
            var t = tile;
            t.GotKeyboardFocus += (_, _) => ShowInHero(t);
        }
    }

    private void ShowInHero(Button tile)
    {
        // The tile's own picture (a store's logo) or its symbol (the wishlist, the calendar...).
        var image = FindChild<Image>(tile);
        var glyph = FindChild<TextBlock>(tile, g => Equals(g.FontFamily, Ui.IconFont));
        if (image?.Source != null && glyph == null)
        {
            _heroImage.Source = image.Source;
            _heroImage.Visibility = Visibility.Visible;
            _heroGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            _heroGlyph.Text = glyph?.Text ?? "\uE719";
            _heroGlyph.Visibility = Visibility.Visible;
            _heroImage.Visibility = Visibility.Collapsed;
        }
        string name = tile.Tag switch { StoreApp s => s.Name, string s => s, _ => "" };
        string shown = name.Split(" · ")[0];
        _heroName.Text = Looks.Current.Id switch { "ubiquity" => LookChrome.Spaced(shown), "solaris" => shown.ToUpperInvariant(), _ => shown };
        _heroAbout.Text = tile.Tag switch
        {
            StoreApp s => $"Opens {s.Name}.  Press Y to hide it from this page or uninstall it.",
            string s when s.StartsWith("Wishlist") => s.Contains("on sale") ? s.Split(" · ")[1] + " from your wishlist. Prices and release days of the games you want." : "Prices and release days of the games you want.",
            "Release calendar" => "What's coming out, day by day.",
            "Free games" => "Games you can keep for free right now, from the stores' giveaways.",
            "Add more stores" => "Install another game store or launcher.",
            "Recomp Zone" => "Native PC versions of classic console games, rebuilt by fans. Bring your own copy of the game.",
            "Retro Lounge" => "Emulators and your own classic games in one place: get the emulators your consoles need and play.",
            string s when s.StartsWith("Hidden") => "Stores you've hidden from this page. Pick one to show it again.",
            string s => "Play " + s + " in the cloud.",
            _ => ""
        };
    }

    private static T? FindChild<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found && (match == null || match(found))) return found;
            if (FindChild(child, match) is { } deeper) return deeper;
        }
        return null;
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Y && System.Windows.Input.Keyboard.FocusedElement is Button { Tag: StoreApp store })
        {
            _ = OptionsAsync(store);
            return true;
        }
        return false;
    }

    private async Task OptionsAsync(StoreApp store)
    {
        int choice = await Host.AskWithIconsAsync(store.Name, null,
            ("Open", "\uE8A7"), ("Hide from this page", "\uE7B3"), ("Uninstall from this PC", "\uE74D"), ("Cancel", "\uE711"));
        switch (choice)
        {
            case 0:
                Stores.Open(store);
                break;
            case 1:
                if (!Host.Settings.HiddenStores.Contains(store.Name)) Host.Settings.HiddenStores.Add(store.Name);
                Host.Settings.Save();
                await LoadAsync();
                break;
            case 2:
                await UninstallAsync(store);
                break;
        }
    }

    private async Task UninstallAsync(StoreApp store)
    {
        if (await Host.AskAsync("Uninstall " + store.Name + "?",
                "Its own uninstaller may open and ask you to confirm. Games installed through it are usually removed with it, " +
                "or stop working until it is reinstalled.", "Cancel", "Uninstall") != 1) return;

        bool ok = false;
        await Host.RunBusyAsync("Uninstalling " + store.Name + "\u2026", async () => ok = await Stores.UninstallAsync(store));
        if (ok)
        {
            Host.Settings.HiddenStores.Remove(store.Name);
            Host.Settings.Save();
        }
        else if (await Host.AskAsync("Couldn't uninstall " + store.Name,
                     "The Windows Package Manager couldn't remove it, or the uninstall was cancelled. You can remove it from " +
                     "Windows Settings > Apps instead.", "Open Installed apps", "Close") == 0)
        {
            ProcessUtil.Open("ms-settings:appsfeatures");
        }
        await LoadAsync();
    }

    private async Task ShowHiddenAsync()
    {
        var hidden = _all.Where(s => s.Installed && Host.Settings.HiddenStores.Contains(s.Name)).ToList();
        if (hidden.Count == 0) return;
        int choice = await Host.AskAsync("Hidden stores", "Pick one to show it on this page again.",
            hidden.Select(s => "Show " + s.Name).Append("Show all").ToArray());
        if (choice < 0) return;
        if (choice == hidden.Count) Host.Settings.HiddenStores.Clear();
        else Host.Settings.HiddenStores.Remove(hidden[choice].Name);
        Host.Settings.Save();
        await LoadAsync();
    }

    /// <summary>The store app's own icon, on a gradient made from its colors.</summary>
    private static async Task LoadIconAsync(Image view, Border box, StoreApp store)
    {
        var (icon, gradient) = await Task.Run(() =>
        {
            var source = store.ExePath != null ? ShellThumb.Get(store.ExePath, 256)
                : store.OpenTarget != null && store.OpenTarget.StartsWith("shell:") ? ShellThumb.Get(store.OpenTarget, 256)
                : null;
            return (source, source == null ? null : ImageLoader.GradientFrom(source));
        });
        if (icon == null) return;
        view.Source = icon;
        box.Background = gradient;
    }
}

public sealed class AddStoresScreen : ListScreen
{
    private List<StoreApp>? _missing;
    private string? _busy;

    public AddStoresScreen() : base("Add more stores") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _missing = (await Task.Run(Stores.Detect)).Where(s => !s.Installed).ToList();
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        BuildCloud();
        AddHeader("Stores to install");
        if (_busy != null) { AddNote(_busy); return; }
        if (_missing == null) { AddNote("Checking which stores are installed…"); return; }
        if (_missing.Count == 0) { AddNote("Every store this launcher knows about is already installed."); return; }

        AddNote("Choose a store to install it. Its games then appear in your library after the next rescan.");
        foreach (var store in _missing)
        {
            var s = store;
            AddRow(s.Name, "Not installed", "", click: () => Install(s));
        }
    }

    /// <summary>Cloud gaming services: nothing to install, just switch each on to give it a tile on the Stores page.</summary>
    private void BuildCloud()
    {
        var settings = Host.Settings;
        AddHeader("Cloud gaming");
        AddNote("Play in the cloud with nothing to download. Each service you turn on gets a tile on the Stores page (these are the same switches as in Settings > Games).");
        foreach (var service in CloudService.All)
        {
            var cs = service;
            bool on = settings.CloudServices.Contains(cs.Id);
            AddRow(cs.Name, on ? "On" : "Off", "\uE753", click: () =>
            {
                if (!settings.CloudServices.Remove(cs.Id)) settings.CloudServices.Add(cs.Id);
                settings.Save();
                Rebuild();
            }, subtitle: cs.About);
        }
    }

    private async void Install(StoreApp store)
    {
        bool github = store.Name == RecompLauncher.Name;
        int choice = await Host.AskAsync("Get " + store.Name,
            github ? "Download the newest x64 installer from its GitHub releases and run it, or open the releases page."
                   : "Install it here with the Windows Package Manager, or download the installer from its website.",
            "Install now", github ? "Open the releases page" : "Open the download page", "Cancel");
        if (choice == 1) { ProcessUtil.Open(store.DownloadUrl); return; }
        if (choice != 0) return;

        _busy = github ? $"Downloading {store.Name}…" : $"Installing {store.Name}… this can take a few minutes. Windows may ask for permission.";
        Rebuild();
        bool ok;
        var progress = new Progress<double>(p =>
        {
            _busy = p < 0.999 ? $"Downloading {store.Name}… {p:P0}" : $"Running the {store.Name} installer… follow its steps.";
            Rebuild();
        });
        try { ok = await Task.Run(() => Stores.InstallAsync(store, progress)); }
        catch (Exception) { ok = false; }
        _busy = null;

        if (ok) await Host.AskAsync(store.Name + " was installed", "Open it from the Stores page to sign in.", "OK");
        else if (await Host.AskAsync("Couldn't install " + store.Name,
                     github ? "The download or the installer didn't finish." : "The Windows Package Manager didn't complete the install.", "Open the download page", "Close") == 0)
            ProcessUtil.Open(store.DownloadUrl);
        await LoadAsync();
    }
}

/// <summary>Save everything personal to one file, and bring it back.</summary>
public sealed class BackupScreen : ListScreen
{
    public BackupScreen() : base("Backup and restore") { }

    protected override void Build()
    {
        var s = Host.Settings;
        AddNote("A backup holds your settings, favorites, categories, ratings, notes, custom games, play time, achievements " +
                "feed and the covers you chose yourself. Downloaded artwork is left out because it can be fetched again. " +
                "The file includes your API keys, so keep it private.");
        AddRow("Back up now…", glyph: "", click: BackupNow);
        AddRow("Restore from a backup…", glyph: "", click: RestoreNow);

        AddHeader("Automatic");
        AddRow("Back up to a folder every time the launcher closes", s.BackupFolder.Length > 0 ? s.BackupFolder : "Off", "", click: ChooseAuto);
        AddNote("Pick a folder that syncs (OneDrive, Dropbox…) to keep a copy off this PC. One file is kept and replaced each time.");
    }

    private async void BackupNow()
    {
        var folder = await Host.PickFolderAsync("Where should the backup go?");
        if (folder == null) return;
        try
        {
            Host.SaveEverything();
            var path = await Task.Run(() => Backup.Create(folder));
            await Host.AskAsync("Backup saved", path, "OK");
        }
        catch (Exception ex)
        {
            await Host.AskAsync("The backup failed", ex.Message, "OK");
        }
    }

    private async void RestoreNow()
    {
        var file = await Host.PickFileAsync("Choose a backup file", new[] { ".zip" });
        if (file == null) return;
        int choice = await Host.AskAsync("Restore this backup?",
            "Your current settings, library changes and play time are replaced by the ones in the backup, " +
            "and the launcher restarts.", "Cancel", "Restore and restart");
        if (choice != 1) return;
        try
        {
            await Task.Run(() => Backup.Restore(file));
            Host.RestartWithoutSaving();
        }
        catch (Exception ex)
        {
            await Host.AskAsync("The restore failed", ex.Message, "OK");
        }
    }

    private async void ChooseAuto()
    {
        int choice = await Host.AskAsync("Automatic backup", null, "Choose a folder…", "Turn off", "Cancel");
        if (choice == 1) Host.Settings.BackupFolder = "";
        else if (choice == 0)
        {
            var folder = await Host.PickFolderAsync("Folder for automatic backups");
            if (folder == null) return;
            Host.Settings.BackupFolder = folder;
        }
        else return;
        Host.Settings.Save();
        Rebuild();
    }
}
