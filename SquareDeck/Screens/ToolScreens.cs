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

    public SliderDialog(string title, string message, string label, string glyph, int value, Action<int>? changed = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(title, 26));
        var note = Ui.Text(message, 18, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.Margin = new Thickness(0, 12, 0, 16);
        panel.Children.Add(note);

        panel.Children.Add(Ui.SliderRow(label, glyph, out _slider, out var text));
        _slider.Value = value;
        text.Text = value == 0 ? "Off" : value + "%";
        _slider.ValueChanged += (_, e) =>
        {
            int v = (int)e.NewValue;
            text.Text = v == 0 ? "Off" : v + "%";
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
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;
    }

    public override void FocusDefault() => _slider.Focus();

    public override void OnClosed() => _done.TrySetResult((int)_slider.Value);
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
        AddRow("Resolution", p.Width > 0 ? $"{p.Width} × {p.Height}" : "Don't change", "", click: Resolution);
        AddRow("Refresh rate", p.RefreshRate > 0 ? p.RefreshRate + " Hz" : "Don't change", "", click: Refresh);
        AddRow("HDR", p.Hdr switch { true => "On", false => "Off", _ => "Don't change" }, "", click: HdrChoice);
        AddRow("Power plan", p.PowerPlanName ?? "Don't change", "", click: Plan);
        AddRow("Sound output", p.AudioDeviceName ?? "Don't change", "", click: Output);
        if (!p.IsEmpty) AddRow("Clear this profile", glyph: "", click: () => { Host.Library.For(_game.Key).Profile = null; Saved(); });
    }

    private void Saved()
    {
        var o = Host.Library.For(_game.Key);
        if (o.Profile is { IsEmpty: true }) o.Profile = null;
        Host.Library.Save();
        Rebuild();
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
            var row = AddRow(a.Description.Length > 0 ? a.Name + "  –  " + a.Description : a.Name,
                a.Unlocked switch { true => "Unlocked ✓", false => "Locked", _ => null }, icon: icon);
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
    private const double TileSize = 170;
    private List<StoreApp> _all = new();

    public StoresScreen() : base("Stores", "{Select}  Open      {Options}  Hide or uninstall      {Back}  Back") { }

    public override void OnShown() => _ = LoadAsync();

    public override void OnResumed() => _ = LoadAsync(); // a store may just have been installed or removed

    private async Task LoadAsync()
    {
        _all = await Task.Run(Stores.Detect);
        Tiles.Children.Clear();
        var hidden = Host.Settings.HiddenStores;

        foreach (var store in _all.Where(s => s.Installed && !hidden.Contains(s.Name)))
        {
            var s = store;
            var box = ImageBox(TileSize, TileSize, out var icon);
            icon.Stretch = Stretch.Uniform;
            icon.Margin = new Thickness(34);
            AddTile(box, s.Name, TileSize, () =>
            {
                if (!Stores.Open(s)) _ = Host.AskAsync("Couldn't open " + s.Name, "Its app didn't start.", "OK");
            }, s);
            _ = LoadIconAsync(icon, box, s);
        }

        AddTile(GlyphBox(TileSize, TileSize, "\uE710"), "Add more stores", TileSize, () => Host.Push(new AddStoresScreen()));
        int hiddenInstalled = _all.Count(s => s.Installed && hidden.Contains(s.Name));
        if (hiddenInstalled > 0)
            AddTile(GlyphBox(TileSize, TileSize, "\uE7B3"), $"Hidden stores ({hiddenInstalled})", TileSize, () => _ = ShowHiddenAsync());
        FocusDefaultLater();
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

    private async void Install(StoreApp store)
    {
        int choice = await Host.AskAsync("Get " + store.Name,
            "Install it here with the Windows Package Manager, or download the installer from its website.",
            "Install now", "Open the download page", "Cancel");
        if (choice == 1) { ProcessUtil.Open(store.DownloadUrl); return; }
        if (choice != 0) return;

        _busy = $"Installing {store.Name}… this can take a few minutes. Windows may ask for permission.";
        Rebuild();
        bool ok;
        try { ok = await Task.Run(() => Stores.InstallAsync(store)); }
        catch (Exception) { ok = false; }
        _busy = null;

        if (ok) await Host.AskAsync(store.Name + " was installed", "Open it from the Stores page to sign in.", "OK");
        else if (await Host.AskAsync("Couldn't install " + store.Name,
                     "The Windows Package Manager didn't complete the install.", "Open the download page", "Close") == 0)
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
