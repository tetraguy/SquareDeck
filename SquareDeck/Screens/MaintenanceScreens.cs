using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Settings > System and tools > Disk cleanup: what can be cleared, how big each is, and one button to clear it.</summary>
public sealed class DiskCleanupScreen : ListScreen
{
    private readonly List<Maintenance.CleanupItem> _items = Maintenance.CleanupItems();
    private bool _busy, _measured;
    private string? _result;

    public DiskCleanupScreen() : base("Disk cleanup", "{Select}  Choose / clean      {Back}  Back") { }

    public override void OnShown()
    {
        base.OnShown();
        if (!_measured) _ = MeasureAsync();
    }

    private async Task MeasureAsync()
    {
        _measured = true;
        foreach (var item in _items)
        {
            var it = item;
            it.Bytes = await Task.Run(() => Maintenance.Measure(it));
            if (Host.IsTop(this)) Rebuild();
        }
    }

    protected override void Build()
    {
        long chosen = _items.Where(i => i.Selected && i.Bytes > 0).Sum(i => i.Bytes);
        bool measuring = _items.Any(i => i.Bytes < 0);
        AddNote("Files that are safe to remove. Choose what to clear, then Clean up. Files in use are skipped.");

        AddRow("Clean up", _busy ? "Cleaning…" : measuring ? "Measuring…" : Maintenance.Size(chosen), "",
            click: () => _ = CleanAsync(), subtitle: _result ?? "Everything chosen below");

        AddHeader("Choose what to clear");
        foreach (var item in _items)
        {
            var it = item;
            var row = AddRow(it.Name, (it.Selected ? "✓ Clear  ·  " : "Keep  ·  ") + Maintenance.Size(it.Bytes), it.Glyph,
                click: () => { it.Selected = !it.Selected; Rebuild(); }, subtitle: it.About);
            row.Tag = "item:" + it.Id;
        }

        AddHeader("More space");
        AddRow("Windows Disk Cleanup", glyph: "", click: () => ProcessUtil.Open("cleanmgr.exe"),
            subtitle: "Windows Update leftovers, old system files and more (needs a mouse or touch)");
        AddRow("Storage settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:storagesense"),
            subtitle: "See what takes space, and turn on Storage Sense to clean up by itself");
    }

    private async Task CleanAsync()
    {
        if (_busy) return;
        var chosen = _items.Where(i => i.Selected).ToList();
        if (chosen.Count == 0) { _result = "Choose something to clear first."; Rebuild(); return; }
        if (chosen.Any(i => i.RecycleBin) &&
            await Host.AskAsync("Empty the Recycle Bin?", "Everything in it is deleted for good.", "Clean up", "Cancel") != 0) return;
        _busy = true;
        _result = null;
        Rebuild();
        long freed = 0;
        foreach (var item in chosen)
        {
            var it = item;
            freed += await Task.Run(() => Maintenance.Clean(it));
            it.Bytes = await Task.Run(() => Maintenance.Measure(it));
        }
        _busy = false;
        _result = $"Freed {Maintenance.Size(freed)}." + (chosen.Any(i => i.Bytes > 1024 * 1024) ? " Some files were in use and were left." : "");
        UiSound.Select();
        Rebuild();
    }
}

/// <summary>Settings > System and tools > Network troubleshooting: check the connection and the usual fixes.</summary>
public sealed class NetworkToolsScreen : ListScreen
{
    private string? _busy;

    public NetworkToolsScreen() : base("Network troubleshooting") { }

    protected override void Build()
    {
        if (_busy != null) AddNote(_busy);

        AddHeader("Check");
        AddRow("Diagnose my connection", glyph: "", click: () => Run("Diagnose my connection", Maintenance.DiagnoseAsync),
            subtitle: "Pings your router, the internet and a website, and says what's wrong");
        AddRow("Show my connection details", glyph: "", click: () => Run("Connection details", () => Task.Run(Maintenance.ConnectionSummary)),
            subtitle: "IP address, gateway, DNS servers, link speed (ipconfig)");
        AddRow("Network speed test", glyph: "", click: () => Host.Push(new SpeedTestScreen()));

        AddHeader("Fix");
        AddRow("Flush DNS cache", glyph: "", click: () => Run("Flush DNS cache", () => Maintenance.RunAsync("ipconfig", "/flushdns")),
            subtitle: "When websites won't load or show old pages (ipconfig /flushdns)");
        AddRow("Get a new IP address", glyph: "", click: () => Run("New IP address",
                () => Maintenance.RunElevatedAsync("ipconfig /release", "ipconfig /renew", "ipconfig /flushdns"), ask: true),
            subtitle: "Release and renew. Disconnects for a few seconds. Asks for administrator permission");
        AddRow("Register DNS again", glyph: "", click: () => Run("Register DNS", () => Maintenance.RunElevatedAsync("ipconfig /registerdns"), ask: true),
            subtitle: "ipconfig /registerdns. Asks for administrator permission");
        AddRow("Reset Winsock", glyph: "", click: () => Run("Reset Winsock", () => Maintenance.RunElevatedAsync("netsh winsock reset"), ask: true, restart: true),
            subtitle: "When nothing connects although Wi-Fi is up. Needs a restart afterwards");
        AddRow("Reset TCP/IP", glyph: "", click: () => Run("Reset TCP/IP", () => Maintenance.RunElevatedAsync("netsh int ip reset", "netsh int ipv6 reset"), ask: true, restart: true),
            subtitle: "Puts network settings back to Windows' defaults. Needs a restart afterwards");

        AddHeader("Windows");
        AddRow("Network reset", glyph: "", click: () => ProcessUtil.Open("ms-settings:network-status"),
            subtitle: "Windows' own full reset: removes and reinstalls network adapters");
        AddRow("Advanced network settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:network-advancedsettings"));
    }

    private async void Run(string title, Func<Task<string>> work, bool ask = false, bool restart = false)
    {
        if (_busy != null) return;
        if (ask && await Host.AskAsync(title + "?", "Windows will ask for administrator permission." +
                (restart ? " Restart afterwards for it to take effect." : " You may be offline for a few seconds."), "Go ahead", "Cancel") != 0) return;
        _busy = title + "…";
        Rebuild();
        string output;
        try { output = await work(); }
        catch (Exception ex) { output = ex.Message; }
        _busy = null;
        Rebuild();
        if (string.IsNullOrWhiteSpace(output)) output = "Done.";
        Host.Push(new TextResultScreen(title, output, restart ? "Restart now to finish." : null));
    }
}

/// <summary>A command's result, in a scrollable box of fixed-width text.</summary>
public sealed class TextResultScreen : Screen
{
    private readonly ScrollViewer _scroll;
    private readonly Button _done;

    public TextResultScreen(string title, string text, string? footer)
    {
        var box = new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18)
        };
        box.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        var panel = new Border { CornerRadius = new CornerRadius(14), Child = box };
        panel.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        _scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, PanningMode = PanningMode.VerticalOnly };
        _done = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Done", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        _done.Click += (_, _) => Host.Pop(this);
        var body = new DockPanel();
        var bottom = new StackPanel();
        if (footer != null) bottom.Children.Add(Ui.Text(footer, 16, "AccentBrush", bold: true));
        bottom.Children.Add(_done);
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(bottom);
        body.Children.Add(_scroll);
        SetPage(title, body, "{Select}  Done      Right stick  Scroll      {Back}  Back");
    }

    public override void FocusDefault() => _done.Focus();

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Up) { _scroll.LineUp(); _scroll.LineUp(); return true; }
        if (action == NavAction.Down) { _scroll.LineDown(); _scroll.LineDown(); return true; }
        return false;
    }
}

/// <summary>Settings > System and tools > Storage speed test: which drive, and how big a test.</summary>
public sealed class StorageSpeedScreen : ListScreen
{
    private int _sizeMb = 512;

    public StorageSpeedScreen() : base("Storage speed test", "{Select}  Choose      {Back}  Back") { }

    protected override void Build()
    {
        AddNote("Measures how fast a drive reads and writes, the way games use it. It writes a temporary test file and deletes it " +
                "afterwards. Close games and downloads first for the truest numbers.");
        AddRow("Test size", _sizeMb >= 1024 ? $"{_sizeMb / 1024} GB" : $"{_sizeMb} MB", "", click: () =>
        {
            _sizeMb = _sizeMb switch { 256 => 512, 512 => 1024, _ => 256 };
            Rebuild();
        }, subtitle: "Bigger is more accurate (it gets past the drive's own cache) but takes longer");

        AddHeader("Drives");
        var drives = DiskSpeed.Drives();
        if (drives.Count == 0) AddNote("No drives are ready to test.");
        foreach (var d in drives)
        {
            var drive = d;
            bool room = drive.Free > (long)_sizeMb * 1024 * 1024 + 256L * 1024 * 1024;
            AddRow($"{drive.Label} ({drive.Root.TrimEnd('\\')})",
                room ? Maintenance.Size(drive.Free) + " free" : "Not enough free space",
                drive.Kind == "Internal" ? "" : "",
                click: () => { if (room) Host.Push(new DriveSpeedScreen(drive, _sizeMb)); else UiSound.Error(); },
                subtitle: drive.Kind + "  ·  " + Maintenance.Size(drive.Size));
        }
    }
}

/// <summary>
/// The speed test running: four cards (sequential read and write, random 4 KB read and write) whose numbers and
/// bars climb as each test runs, then a plain-words verdict. B stops it.
/// </summary>
public sealed class DriveSpeedScreen : Screen
{
    private readonly DiskSpeed.Drive _drive;
    private readonly int _sizeMb;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<DiskSpeed.Test, (TextBlock Value, TextBlock Sub, Border Fill, Border Card)> _cards = new();
    private readonly Dictionary<DiskSpeed.Test, double> _results = new();
    private readonly TextBlock _status = Ui.Text("", 17, "SubtleBrush");
    private readonly TextBlock _verdict = Ui.Text("", 20, "FgBrush", bold: true);
    private readonly Border _overall = new() { Height = 6, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(3) };
    private readonly Button _again = Ui.PanelButton("", "Run again");
    private bool _running;

    public DriveSpeedScreen(DiskSpeed.Drive drive, int sizeMb)
    {
        _drive = drive;
        _sizeMb = sizeMb;
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var head = Ui.Text($"{drive.Label}  ({drive.Root.TrimEnd('\\')})  ·  {drive.Kind}", 22, "FgBrush", bold: true);
        panel.Children.Add(head);
        _status.Margin = new Thickness(0, 6, 0, 14);
        panel.Children.Add(_status);
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 26) };
        track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _overall.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Child = _overall;
        panel.Children.Add(track);

        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(Card(DiskSpeed.Test.SeqRead, "Sequential read", "Loading big files: game levels, videos"));
        grid.Children.Add(Card(DiskSpeed.Test.SeqWrite, "Sequential write", "Installing and updating games"));
        grid.Children.Add(Card(DiskSpeed.Test.RandRead, "Random read (4 KB)", "Lots of small files: loading and streaming assets"));
        grid.Children.Add(Card(DiskSpeed.Test.RandWrite, "Random write (4 KB)", "Saves, caches and shader files"));
        panel.Children.Add(grid);

        _verdict.Margin = new Thickness(0, 22, 0, 12);
        _verdict.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_verdict);
        _again.HorizontalAlignment = HorizontalAlignment.Left;
        _again.Visibility = Visibility.Collapsed;
        _again.Click += (_, _) => _ = RunAsync();
        panel.Children.Add(_again);

        SetPage("Storage speed test", new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false },
                "{Select}  Run again      {Back}  Stop / back");
    }

    private Border Card(DiskSpeed.Test test, string name, string about)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(name, 17, "FgBrush", bold: true));
        var aboutText = Ui.Text(about, 13, "SubtleBrush");
        aboutText.Margin = new Thickness(0, 2, 0, 12);
        stack.Children.Add(aboutText);
        var value = Ui.Text("—", 38, "FgBrush", bold: true);
        stack.Children.Add(value);
        var sub = Ui.Text("MB/s", 14, "SubtleBrush");
        stack.Children.Add(sub);
        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 14, 0, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Child = fill;
        stack.Children.Add(track);
        var card = new Border
        {
            CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 4)), Padding = new Thickness(22, 18, 22, 20), Margin = new Thickness(0, 0, 16, 16),
            Child = stack, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Opacity = 0.55
        };
        card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        _cards[test] = (value, sub, fill, card);
        return card;
    }

    public override void OnShown() { if (!_running && _results.Count == 0) _ = RunAsync(); }

    public override void OnClosed() => _cts.Cancel();

    public override void FocusDefault() { if (_again.Visibility == Visibility.Visible) _again.Focus(); }

    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        _results.Clear();
        _again.Visibility = Visibility.Collapsed;
        _verdict.Text = "";
        foreach (var c in _cards.Values) { c.Value.Text = "—"; c.Sub.Text = "MB/s"; c.Fill.Width = 0; c.Card.Opacity = 0.55; c.Card.BorderBrush = Brushes.Transparent; }
        _status.Text = $"Testing with a {(_sizeMb >= 1024 ? _sizeMb / 1024 + " GB" : _sizeMb + " MB")} file…";

        var progress = new Progress<DiskSpeed.Progress>(Show);
        try
        {
            await DiskSpeed.RunAsync(_drive.Root, _sizeMb, progress, _cts.Token);
            _status.Text = "Done. The test file has been deleted.";
            _verdict.Text = DiskSpeed.Describe(_results.GetValueOrDefault(DiskSpeed.Test.SeqRead), _results.GetValueOrDefault(DiskSpeed.Test.RandRead));
            _overall.Width = ((FrameworkElement)_overall.Parent).ActualWidth;
            UiSound.Toast();
        }
        catch (OperationCanceledException) { return; }
        catch (UnauthorizedAccessException) { _status.Text = "Windows didn't allow a test file on this drive."; }
        catch (Exception ex) { _status.Text = "The test stopped: " + ex.Message; }
        finally
        {
            _running = false;
            foreach (var c in _cards.Values) c.Card.BorderBrush = Brushes.Transparent;
        }
        _again.Visibility = Visibility.Visible;
        Host.FocusSoon(_again);
    }

    private void Show(DiskSpeed.Progress p)
    {
        if (!_cards.TryGetValue(p.Test, out var c)) return;
        _results[p.Test] = p.MBps;
        c.Card.Opacity = 1;
        c.Card.SetResourceReference(Border.BorderBrushProperty, p.Done ? "CircleBrush" : "AccentBrush");
        c.Value.Text = p.MBps >= 100 ? p.MBps.ToString("N0") : p.MBps.ToString("N1");
        c.Sub.Text = p.Iops > 0 ? $"MB/s  ·  {p.Iops:N0} IOPS" : "MB/s";
        // Bars on a log scale, so a memory card's 90 MB/s and an NVMe drive's 7,000 both read sensibly.
        double scale = p.Test is DiskSpeed.Test.RandRead or DiskSpeed.Test.RandWrite ? 400 : 7500;
        double part = Math.Clamp(Math.Log10(1 + p.MBps) / Math.Log10(1 + scale), 0, 1);
        if (c.Fill.Parent is FrameworkElement track) c.Fill.Width = track.ActualWidth * part;
        if (_overall.Parent is FrameworkElement all) _overall.Width = all.ActualWidth * p.Fraction;
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back && _running)
        {
            _cts.Cancel();
            return false; // leaves; the test file is deleted as it stops
        }
        return false;
    }
}
