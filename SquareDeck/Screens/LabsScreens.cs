using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;
using Path = System.IO.Path;

namespace SquareDeck.Screens;

// The pages of the experimental features (Settings > Experimental). Each is a simple list page.

/// <summary>Moves a Steam game to another library folder (another drive, a microSD card).</summary>
public sealed class MoveGameScreen : ListScreen
{
    private readonly Game _game;
    private readonly CancellationTokenSource _cts = new();
    private List<Labs.LibraryDrive> _drives = new();
    private string? _status;
    private bool _busy;
    private double _fraction;

    // The progress card: a bar that slides along with a shine, the percentage and what's happening.
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 14, CornerRadius = new CornerRadius(7) };
    private readonly Border _shine = new() { Width = 120, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly TextBlock _percent = Ui.Text("0%", 30, "FgBrush", bold: true);
    private readonly TextBlock _step = Ui.Text("", 15, "SubtleBrush");
    private readonly ProgressFill _bar;
    private readonly StackPanel _card = new() { Margin = new Thickness(8, 6, 8, 14) };

    public MoveGameScreen(Game game) : base("Move " + game.Name, "{Select}  Move here      {Back}  Back")
    {
        _game = game;
        var track = new Border { Height = 14, CornerRadius = new CornerRadius(7) };
        track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _fill.ClipToBounds = true;
        var shineBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        shineBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        shineBrush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5));
        shineBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        _shine.Background = shineBrush;
        _shine.RenderTransform = new TranslateTransform(-120, 0);
        _fill.Child = new Canvas { Children = { _shine } };
        var bar = new Grid();
        bar.Children.Add(track);
        bar.Children.Add(_fill);
        _bar = new ProgressFill(bar, _fill);
        _bar.Shown = v => _percent.Text = $"{Math.Floor(v * 100 + 0.0001):0}%"; // read off the bar as it slides
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_percent, Dock.Right);
        top.Children.Add(_percent);
        _step.VerticalAlignment = VerticalAlignment.Bottom;
        top.Children.Add(_step);
        _card.Children.Add(top);
        _card.Children.Add(bar);
        // Switch 2 style: the bar's track in the page's line color.
        if (Switch2Pages.PcPage(this)) Resources["CircleBrush"] = new SolidColorBrush(Switch2Settings.Line);
    }

    private void ShowProgress(double fraction, string step)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        _step.Text = step;
        _bar.Set(_fraction, 300);
    }

    public override void OnShown()
    {
        _drives = Labs.Drives();
        Rebuild();
    }

    public override void OnClosed() => _cts.Cancel();

    protected override void Build()
    {
        string? here = SteamLibrary.LibraryOf(_game.AppId);
        long size = _game.SizeOnDisk > 0 ? _game.SizeOnDisk : 0;
        AddNote($"{_game.Name} uses {(size > 0 ? Labs.Size(size) : "an unknown amount of space")}. Pick the Steam library to move it to. " +
                "Steam closes while it moves and opens again afterwards.");
        if (_busy)
        {
            // Kept, not rebuilt, so the bar slides smoothly.
            if (_card.Parent is Panel old) old.Children.Remove(_card);
            Rows.Children.Add(_card);
            if (!Theme.ReduceMotion)
                ((TranslateTransform)_shine.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(-120, 900, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
        else if (_status != null) AddNote(_status);
        AddHeader("Steam libraries");
        foreach (var d in _drives)
        {
            bool current = here != null && string.Equals(Path.GetFullPath(here).TrimEnd('\\'), Path.GetFullPath(d.Library).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            bool fits = size <= 0 || d.Free > size * 1.05;
            string kind = d.Removable ? "microSD / USB" : d.Label;
            var drive = d;
            var row = AddRow($"{d.Drive}  {kind}", current ? "Here now" : fits ? Labs.Size(d.Free) + " free" : "Not enough space", d.Removable ? "" : "",
                click: current || !fits || _busy ? null : () => _ = MoveAsync(drive), subtitle: d.Library + $"  ·  {Labs.Size(d.Free)} free of {Labs.Size(d.Total)}");
            if (current || !fits) row.Opacity = 0.55;
        }
        AddHeader("Another drive");
        AddRow("Add a Steam library on another drive", null, "", click: () => ProcessUtil.Open("steam://open/settings"),
            subtitle: "In Steam: Settings > Storage > Add drive. It shows up here afterwards");
    }

    private async Task MoveAsync(Labs.LibraryDrive drive)
    {
        if (_game.IsRunning) { await Host.AskAsync("Close the game first", _game.Name + " is running.", "OK"); return; }
        if (await Host.AskAsync("Move " + _game.Name + "?", $"To {drive.Library}. Steam will close while it moves. Don't turn the PC off until it's done.",
                "Move", "Cancel") != 0) return;
        _busy = true;
        Rebuild();
        ShowProgress(0, "Closing Steam…");
        if (!await Labs.CloseSteamAsync())
        {
            _busy = false;
            _status = "Steam didn't close. Close it yourself (right-click its icon by the clock, Exit), then try again.";
            Rebuild();
            return;
        }
        var progress = new Progress<double>(f => ShowProgress(f, f < 0.95 ? "Copying the game's files…" : "Checking every file and tidying up…"));
        try
        {
            await Labs.MoveGameAsync(_game, drive.Library, progress, _cts.Token);
            ShowProgress(1, "Done. Steam is opening again.");
            await Task.Delay(900);
            _status = "Moved to " + drive.Library + ". Steam is opening again.";
            ProcessUtil.Open("steam://open/main");
            await Task.Delay(1500);
            _ = Host.ReloadAsync(waitForArt: false);
        }
        catch (OperationCanceledException) { _status = "Stopped. The game is still where it was."; }
        catch (Exception ex) { _status = "Couldn't move it: " + ex.Message + " The game is still where it was."; ProcessUtil.Open("steam://open/main"); }
        _busy = false;
        _drives = Labs.Drives();
        if (Host.IsTop(this)) Rebuild();
    }
}

/// <summary>Games not played in six months, biggest first, with the space they'd free.</summary>
public sealed class StorageAdvisorScreen : ListScreen
{
    public StorageAdvisorScreen() : base("Storage advisor") { }

    private List<Game> Stale() => Host.AllGames
        .Where(g => g.IsSteam && g.IsInstalled && g.SizeOnDisk > 0 && (g.LastPlayed ?? g.RecentAt) < DateTime.Now.AddMonths(-6))
        .OrderByDescending(g => g.SizeOnDisk).ToList();

    protected override void Build()
    {
        var stale = Stale();
        foreach (var d in Labs.Drives())
            AddRow($"{d.Drive}  {(d.Removable ? "microSD / USB" : d.Label)}", Labs.Size(d.Free) + " free", d.Removable ? "" : "",
                subtitle: $"{Labs.Size(d.Total - d.Free)} used of {Labs.Size(d.Total)}");
        if (stale.Count == 0) { AddNote("Every installed Steam game has been played in the last six months."); return; }
        AddHeader($"{stale.Count} games not played in 6 months  ·  {Labs.Size(stale.Sum(g => g.SizeOnDisk))}");
        AddNote("Uninstalled games stay in your library as not installed, and can be installed again any time.");
        AddRow("Uninstall all of these", null, "", click: () => _ = UninstallAsync(stale), subtitle: "Steam asks you to confirm each one");
        foreach (var g in stale)
        {
            var game = g;
            var last = g.LastPlayed is DateTime lp ? "Last played " + Ui.Ago(lp) : "Never played";
            AddRow(g.Name, Labs.Size(g.SizeOnDisk), "", click: () => _ = UninstallAsync(new List<Game> { game }), subtitle: last);
        }
    }

    private async Task UninstallAsync(List<Game> games)
    {
        if (await Host.AskAsync(games.Count == 1 ? "Uninstall " + games[0].Name + "?" : $"Uninstall {games.Count} games?",
                $"Frees about {Labs.Size(games.Sum(g => g.SizeOnDisk))}. Steam opens and asks you to confirm.", "Continue in Steam", "Cancel") != 0) return;
        foreach (var g in games)
        {
            ProcessUtil.Open($"steam://uninstall/{g.AppId}");
            await Task.Delay(games.Count > 1 ? 2500 : 0);
        }
    }
}

/// <summary>Broken shortcuts, games whose program is gone, and leftover folders.</summary>
public sealed class CleanupScreen : ListScreen
{
    private List<Labs.Leftover>? _leftovers;

    public CleanupScreen() : base("Library cleanup") { }

    public override void OnShown()
    {
        Rebuild();
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        _leftovers = await Task.Run(Labs.LeftoverFolders);
        if (Host.IsTop(this)) Rebuild();
    }

    private static bool Missing(Game g)
    {
        if (g.IsCustom)
        {
            var p = g.LaunchPath;
            return Path.IsPathRooted(p) && !p.Contains("://") && !File.Exists(p);
        }
        return g.IsInstalled && !string.IsNullOrEmpty(g.InstallDir) && !Directory.Exists(g.InstallDir);
    }

    protected override void Build()
    {
        var broken = Host.AllGames.Where(Missing).ToList();
        AddHeader(broken.Count == 0 ? "Shortcuts and games: all fine" : $"{broken.Count} games or shortcuts that point nowhere");
        foreach (var g in broken)
        {
            var game = g;
            AddRow(g.Name, g.IsCustom ? "Program missing" : "Folder missing", "", click: () => _ = Host.OpenOptionsAsync(game),
                subtitle: (g.IsCustom ? g.LaunchPath : g.InstallDir) + "  ·  Options to remove or fix it");
        }
        if (_leftovers == null) { AddNote("Looking for leftover folders…"); return; }
        AddHeader(_leftovers.Count == 0 ? "No leftover folders" : $"{_leftovers.Count} leftover folders  ·  {Labs.Size(_leftovers.Sum(l => l.Size))}");
        if (_leftovers.Count > 0) AddNote("Folders in Steam's games folder that no installed game uses. They're sent to the Recycle Bin, so they can be brought back.");
        foreach (var l in _leftovers)
        {
            var left = l;
            AddRow(Path.GetFileName(l.Path), Labs.Size(l.Size), "", click: () => _ = RemoveAsync(left), subtitle: l.Path);
        }
    }

    private async Task RemoveAsync(Labs.Leftover left)
    {
        int c = await Host.AskAsync(Path.GetFileName(left.Path), $"{Labs.Size(left.Size)}. Send it to the Recycle Bin?", "Recycle it", "Show in folder", "Cancel");
        if (c == 1) { ProcessUtil.Open("explorer.exe", "\"" + left.Path + "\""); return; }
        if (c != 0) return;
        try { await Task.Run(() => Labs.Recycle(left.Path)); _leftovers?.Remove(left); }
        catch (Exception ex) { await Host.AskAsync("Couldn't remove it", ex.Message, "OK"); }
        Rebuild();
    }
}

/// <summary>Overnight downloads: stay awake while charging and downloading, then sleep.</summary>
public sealed class OvernightScreen : ListScreen
{
    public OvernightScreen() : base("Overnight downloads") { }

    protected override void Build()
    {
        bool active = Labs.Get("overnight.active") == "1";
        bool sleep = Labs.Get("overnight.sleep", "1") == "1";
        AddNote("Start it before bed with the charger in. While Steam is downloading or updating, the PC stays awake; when everything " +
                "has finished, it can go to sleep. Queue games first with Install > Add to the install queue.");
        AddRow(active ? "Stop overnight mode" : "Start overnight mode", active ? "On" : "Off", "", click: () =>
        {
            Labs.Put("overnight.active", active ? "0" : "1");
            if (active) Labs.KeepAwake(false);
            Rebuild();
        }, subtitle: active ? Labs.Get("overnight.status", "Waiting for downloads") : "Turns itself off when it's done");
        AddRow("Sleep when everything is done", sleep ? "On" : "Off", "", click: () => { Labs.Put("overnight.sleep", sleep ? "0" : "1"); Rebuild(); });
        if (!Battery.IsPluggedIn()) AddNote("The charger isn't plugged in. Overnight mode waits for it.");
    }
}

/// <summary>A bug report, with personal details removed, to send as a GitHub issue.</summary>
public sealed class BugReportScreen : ListScreen
{
    private string? _note;

    public BugReportScreen() : base("Report a problem") { }

    protected override void Build()
    {
        var text = Labs.BugReport(Host.Settings, _note);
        AddNote("This opens a new issue on SquareDeck's GitHub page with the details filled in. Your user name, folders under your " +
                "user folder, API keys, Steam id and e-mail addresses are taken out. You'll see it all before it's sent, and a GitHub account is needed.");
        AddRow("Say what happened", _note == null ? "Optional" : "Written", "", click: async () =>
        {
            var t = await Host.PromptAsync("What happened?", "What you were doing and what went wrong.", _note ?? "", "Done");
            if (t != null) { _note = t.Trim().Length == 0 ? null : t.Trim(); Rebuild(); }
        }, subtitle: _note);
        AddRow("Open the issue on GitHub", null, "", click: () => ProcessUtil.Open(Labs.IssueUrl(text)));
        AddRow("Copy the report", null, "", click: () => { try { Clipboard.SetText(text); Host.ShowToast("COPIED", "Bug report copied", "Paste it anywhere", "", remember: false); } catch (Exception) { } });
        AddHeader("What will be sent");
        var preview = Ui.Text(text, 13, "SubtleBrush");
        preview.TextWrapping = TextWrapping.Wrap;
        preview.FontFamily = new FontFamily("Consolas");
        preview.Margin = new Thickness(8, 4, 8, 8);
        Rows.Children.Add(preview);
    }
}

/// <summary>Games still inside Steam's refund window.</summary>
public sealed class RefundScreen : ListScreen
{
    public RefundScreen() : base("Steam refund window") { }

    protected override void Build()
    {
        var list = Labs.RefundWindow(Host.AllGames, g => g.IsRunning ? Host.RunningMinutes(g) : 0);
        AddNote("Steam refunds games played under 2 hours, within 14 days of buying them (request it at help.steampowered.com). " +
                "Steam doesn't tell other programs when you bought a game, so the date here is when the game was installed or first seen by SquareDeck.");
        if (list.Count == 0) { AddNote("No games are inside the refund window right now."); return; }
        foreach (var r in list)
        {
            string time = r.MinutesLeft < 60 ? $"{r.MinutesLeft:0} min of play left" : $"{r.MinutesLeft / 60:0.#} h of play left";
            AddRow(r.Game.Name, time, "", click: () => ProcessUtil.Open("https://help.steampowered.com/"),
                subtitle: $"{r.MinutesPlayed:0} min played  ·  bought around {r.Bought:MMM d}  ·  {Math.Ceiling(r.TimeLeft.TotalDays):0} days left");
        }
    }
}

/// <summary>A game's sessions, screenshots and notes as a timeline.</summary>
public sealed class GameDiaryScreen : ListScreen
{
    private readonly Game _game;

    public GameDiaryScreen(Game game) : base("Diary – " + game.Name) { _game = game; }

    protected override void Build()
    {
        var sessions = Host.Playtime.Sessions.Where(s => s.Key == _game.Key).OrderByDescending(s => s.Start).ToList();
        var shots = GameCaptures.For(_game, 400).Select(p => (Path: p, When: File.GetLastWriteTime(p))).ToList();
        var notes = GameNotebook.Load(_game);
        double total = sessions.Sum(s => s.Seconds) / 3600;
        AddNote(sessions.Count == 0 ? "No sessions recorded yet. Sessions are recorded from now on, each time you play through SquareDeck."
            : $"{sessions.Count} sessions  ·  {Labs.Hours(total)}  ·  {shots.Count} screenshots  ·  {notes.Count} notes");

        // Everything on one timeline, newest first, grouped by day.
        var items = new List<(DateTime When, Action Add)>();
        foreach (var s in sessions)
        {
            var session = s;
            var during = shots.Where(x => x.When >= s.Start && x.When <= s.Start.AddSeconds(s.Seconds + 120)).Select(x => x.Path).ToList();
            items.Add((s.Start, () => AddRow($"{session.Start:h:mm tt}  Played {Labs.Hours(session.Seconds / 3600)}", during.Count > 0 ? $"{during.Count} screenshots" : null, "",
                click: during.Count > 0 ? () => Host.Push(new ViewerScreen(during, 0)) : null)));
        }
        foreach (var n in notes)
        {
            var note = n;
            items.Add((n.Created, () => AddRow($"{note.Created:h:mm tt}  Note: {note.Title}", null, "", click: () => Host.Push(new GameNotesScreen(_game)),
                subtitle: note.Preview.Length > 90 ? note.Preview[..90] + "…" : note.Preview)));
        }
        // Screenshots taken outside any recorded session (Steam's F12, older ones).
        var loose = shots.Where(x => !sessions.Any(s => x.When >= s.Start && x.When <= s.Start.AddSeconds(s.Seconds + 120))).ToList();
        foreach (var day in loose.GroupBy(x => x.When.Date))
        {
            var paths = day.Select(x => x.Path).ToList();
            items.Add((day.Max(x => x.When), () => AddRow($"{day.Key:MMM d}  {paths.Count} screenshots", null, "", click: () => Host.Push(new ViewerScreen(paths, 0)))));
        }
        foreach (var day in items.OrderByDescending(i => i.When).GroupBy(i => i.When.Date))
        {
            AddHeader(day.Key == DateTime.Today ? "Today" : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("dddd, MMMM d, yyyy"));
            foreach (var i in day) i.Add();
        }
    }
}

/// <summary>Common fixes for a game that won't start or misbehaves.</summary>
public sealed class FixGameScreen : ListScreen
{
    private readonly Game _game;

    public FixGameScreen(Game game) : base("Fix " + game.Name) { _game = game; }

    protected override void Build()
    {
        AddNote("Try these one at a time, starting from the top. Each one says what it does before doing it.");
        if (_game.IsSteam)
        {
            AddRow("Verify the game's files", null, "", click: () => ProcessUtil.Open($"steam://validate/{_game.AppId}"),
                subtitle: "Steam checks every file and downloads any that are missing or damaged");
            var shader = Labs.ShaderCacheOf(_game);
            AddRow("Clear the shader cache", shader == null ? "None found" : null, "", click: shader == null ? null : () => _ = ClearShadersAsync(shader),
                subtitle: "Steam's saved graphics shaders for this game. They're rebuilt, so the first minutes may stutter");
        }
        var folders = Labs.SettingsFoldersOf(_game);
        AddRow("Reset the game's settings", folders.Count == 0 ? "No folder found" : $"{folders.Count} found", "", click: folders.Count == 0 ? null : () => _ = ResetSettingsAsync(folders),
            subtitle: folders.Count == 0 ? "Its settings folder wasn't found in Documents or AppData" : "Renames its settings folders so the game starts fresh. Renaming them back undoes it");
        var redist = Labs.RedistributablesOf(_game);
        AddRow("Reinstall Visual C++ and DirectX", redist.Count == 0 ? "None included" : $"{redist.Count} installers", "", click: redist.Count == 0 ? null : () => _ = RedistAsync(redist),
            subtitle: "The runtime files that ship with the game. Missing ones are a common reason games don't start");
        AddRow("Run it as administrator", null, "", click: () => _ = RunAsAdminAsync(),
            subtitle: "Once, for games that need permission to write their files");
        if (!string.IsNullOrEmpty(_game.InstallDir) && Directory.Exists(_game.InstallDir))
            AddRow("Open the game's folder", null, "", click: () => ProcessUtil.Open("explorer.exe", "\"" + _game.InstallDir + "\""));
        foreach (var f in folders)
        {
            var folder = f;
            AddRow("Open " + Path.GetFileName(f), null, "", click: () => ProcessUtil.Open("explorer.exe", "\"" + folder + "\""), subtitle: f);
        }
    }

    private async Task ClearShadersAsync(string folder)
    {
        if (await Host.AskAsync("Clear the shader cache?", "Close the game first. Steam rebuilds the shaders the next time it runs.", "Clear", "Cancel") != 0) return;
        try { await Task.Run(() => Directory.Delete(folder, true)); Host.ShowToast("FIXED", "Shader cache cleared", _game.Name, "", remember: false); }
        catch (Exception ex) { await Host.AskAsync("Couldn't clear it", ex.Message + " Close Steam and try again.", "OK"); }
        Rebuild();
    }

    private async Task ResetSettingsAsync(List<string> folders)
    {
        string list = string.Join("\n", folders.Select(Path.GetFileName));
        if (await Host.AskAsync("Reset the settings?", "These folders get “.backup” added to their names, so the game makes fresh ones:\n" + list +
                "\n\nSaves can be in these folders too: they come back if you rename the folder back.", "Reset", "Cancel") != 0) return;
        int done = 0;
        foreach (var f in folders)
        {
            try { Directory.Move(f, f + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm")); done++; }
            catch (Exception) { }
        }
        await Host.AskAsync(done == folders.Count ? "Settings reset" : "Some folders couldn't be renamed", done == folders.Count ? "Start the game to make fresh settings." : "Close the game and try again.", "OK");
        Rebuild();
    }

    private async Task RedistAsync(List<string> installers)
    {
        if (await Host.AskAsync("Reinstall runtime files?", "Windows asks for permission for each installer. They run quietly and take a minute or two.", "Install", "Cancel") != 0) return;
        await Host.RunBusyAsync("Installing runtime files", async () =>
        {
            foreach (var file in installers)
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(file, Labs.QuietArgs(file)) { UseShellExecute = true, Verb = "runas" });
                    if (p != null) await p.WaitForExitAsync();
                }
                catch (Exception) { }
            }
        });
    }

    private async Task RunAsAdminAsync()
    {
        var programs = Labs.ProgramsOf(_game);
        if (programs.Count == 0) { await Host.AskAsync("No program found", "No program was found in the game's folder.", "OK"); return; }
        int c = programs.Count == 1 ? 0 : await Host.AskAsync("Which program is the game?", "The biggest programs in its folder:", programs.Select(Path.GetFileName).Cast<string>().ToArray());
        if (c < 0) return;
        try { Process.Start(new ProcessStartInfo(programs[c]) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(programs[c]) }); }
        catch (Exception) { }
    }
}

/// <summary>Live stick readings, a drift check, and SquareDeck's own dead zone.</summary>
public sealed class StickCheckScreen : Screen
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Ellipse _left = Dot(), _right = Dot();
    private readonly Ellipse _leftZone = Zone(), _rightZone = Zone();
    private readonly TextBlock _leftText = Ui.Text("", 16, "FgBrush"), _rightText = Ui.Text("", 16, "FgBrush");
    private readonly TextBlock _result = Ui.Text("", 16, "SubtleBrush");
    private readonly Button _measure, _zone;
    private const double Pad = 180;
    private DateTime? _measuring;
    private readonly List<(double L, double R)> _samples = new();

    private static Ellipse Dot()
    {
        var e = new Ellipse { Width = 18, Height = 18 };
        e.SetResourceReference(Shape.FillProperty, "AccentBrush");
        return e;
    }

    private static Ellipse Zone()
    {
        var e = new Ellipse { StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 } };
        e.SetResourceReference(Shape.StrokeProperty, "SubtleBrush");
        return e;
    }

    public StickCheckScreen()
    {
        var sticks = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        sticks.Children.Add(Stick("Left stick", _left, _leftZone, _leftText));
        sticks.Children.Add(Stick("Right stick", _right, _rightZone, _rightText));
        _measure = Ui.PanelButton("", "Measure drift (don't touch the sticks)");
        _measure.Click += (_, _) => { _samples.Clear(); _measuring = DateTime.Now; _result.Text = "Measuring… keep your thumbs off the sticks."; };
        _zone = Ui.PanelButton("", ZoneLabel());
        _zone.Click += (_, _) => _ = ChooseZoneAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(_measure);
        _zone.Margin = new Thickness(12, 0, 0, 0);
        buttons.Children.Add(_zone);
        var note = Ui.Text("The dead zone here is for moving around SquareDeck. Games use their own (set them in the game, Steam Input or Armoury Crate).", 14, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 760;
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(0, 18, 0, 0);
        _result.HorizontalAlignment = HorizontalAlignment.Center;
        _result.Margin = new Thickness(0, 14, 0, 0);
        _result.TextWrapping = TextWrapping.Wrap;
        _result.MaxWidth = 760;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(sticks);
        body.Children.Add(buttons);
        body.Children.Add(_result);
        body.Children.Add(note);
        SetPage("Stick check", body, "{Select}  Choose      {Back}  Back");
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Changes the words on a panel button (its last line of text).</summary>
    private static void SetLabel(Button button, string text)
    {
        TextBlock? last = null;
        void Walk(object node)
        {
            if (node is TextBlock tb && !Equals(tb.FontFamily, Ui.IconFont)) last = tb;
            if (node is DependencyObject d) foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(button);
        if (last != null) last.Text = text;
    }

    private static string ZoneLabel() => "SquareDeck dead zone: " + Labs.Get("sticks.zone", "49") + "%";

    private FrameworkElement Stick(string name, Ellipse dot, Ellipse zone, TextBlock text)
    {
        var canvas = new Canvas { Width = Pad, Height = Pad };
        var ring = new Ellipse { Width = Pad, Height = Pad, StrokeThickness = 2 };
        ring.SetResourceReference(Shape.StrokeProperty, "FgBrush");
        ring.SetResourceReference(Shape.FillProperty, "CircleBrush");
        canvas.Children.Add(ring);
        canvas.Children.Add(zone);
        canvas.Children.Add(dot);
        var stack = new StackPanel { Margin = new Thickness(40, 0, 40, 0) };
        var title = Ui.Text(name, 18, "FgBrush", bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(title);
        stack.Children.Add(canvas);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(text);
        return stack;
    }

    public override void FocusDefault() => _measure.Focus();
    public override void OnShown() { _timer.Start(); FocusLater(_measure); }
    public override void OnClosed() => _timer.Stop();
    public override void OnAppRest(bool resting) { if (resting) _timer.Stop(); else if (Host.IsTop(this)) _timer.Start(); }

    private void Tick()
    {
        var s = Host.PadState;
        double zone = (double.TryParse(Labs.Get("sticks.zone", "49"), out var z) ? z : 49) / 100;
        foreach (var (dot, ring, label, x, y) in new[] { (_left, _leftZone, _leftText, s.LeftX / 32768.0, s.LeftY / 32768.0), (_right, _rightZone, _rightText, s.RightX / 32768.0, s.RightY / 32768.0) })
        {
            ring.Width = ring.Height = Pad * zone;
            Canvas.SetLeft(ring, Pad / 2 - ring.Width / 2);
            Canvas.SetTop(ring, Pad / 2 - ring.Height / 2);
            Canvas.SetLeft(dot, Pad / 2 + x * Pad / 2 - 9);
            Canvas.SetTop(dot, Pad / 2 - y * Pad / 2 - 9);
            label.Text = s.Connected ? $"X {x * 100:+0;-0;0}%   Y {y * 100:+0;-0;0}%" : "No controller";
        }
        if (_measuring is DateTime started)
        {
            _samples.Add((Math.Sqrt(Math.Pow(s.LeftX / 32768.0, 2) + Math.Pow(s.LeftY / 32768.0, 2)), Math.Sqrt(Math.Pow(s.RightX / 32768.0, 2) + Math.Pow(s.RightY / 32768.0, 2))));
            if (DateTime.Now - started > TimeSpan.FromSeconds(3))
            {
                _measuring = null;
                double l = _samples.Max(x => x.L) * 100, r = _samples.Max(x => x.R) * 100;
                string Verdict(double v) => v < 6 ? "fine" : v < 12 ? "a little drift" : "noticeable drift";
                _result.Text = $"At rest: left stick {l:0}% off center ({Verdict(l)}), right stick {r:0}% ({Verdict(r)}). " +
                               (Math.Max(l, r) >= 12 ? "A dead zone a few points above that hides it in games that let you set one." : "Up to about 6% is normal.");
            }
        }
    }

    private async Task ChooseZoneAsync()
    {
        var options = new[] { "25%", "35%", "49% (usual)", "60%", "70%" };
        int c = await Host.AskAsync("SquareDeck dead zone", "How far a stick must move before it moves the highlight. Higher hides drift.", options);
        if (c < 0) return;
        int value = new[] { 25, 35, 49, 60, 70 }[c];
        Labs.Put("sticks.zone", value.ToString());
        Gamepad.StickThreshold = (short)(32767 * value / 100);
        SetLabel(_zone, ZoneLabel());
    }
}

/// <summary>Battery health from Windows' battery report.</summary>
public sealed class BatteryHealthScreen : ListScreen
{
    private Labs.BatteryHealth? _health;
    private bool _loaded;

    public BatteryHealthScreen() : base("Battery health") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _health = await Labs.ReadBatteryAsync();
        _loaded = true;
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (!_loaded) { AddNote("Reading Windows' battery report…"); return; }
        if (_health is not { } h) { AddNote("No battery information is available on this PC."); return; }
        double pct = 100.0 * h.Full / h.Design;
        AddRow("Health", $"{pct:0}%", "", subtitle: pct >= 90 ? "Like new" : pct >= 80 ? "Good: some normal wear" : pct >= 65 ? "Worn: shorter battery life than new" : "Very worn: think about a replacement");
        AddRow("Holds now", $"{h.Full / 1000.0:0.0} Wh", "", subtitle: $"It held {h.Design / 1000.0:0.0} Wh when new");
        AddRow("Charge cycles", h.Cycles?.ToString() ?? "Not reported", "", subtitle: "One cycle is a full battery's worth of charging, spread over any number of charges");
        if (h.History.Count >= 2)
        {
            AddHeader("Capacity over time");
            Rows.Children.Add(Chart(h.History));
        }
        AddNote("Tips: keeping it between about 20% and 80% and away from heat slows wear. Armoury Crate can limit charging to 80%.");
    }

    private static FrameworkElement Chart(List<(DateTime When, long Full, long Design)> history)
    {
        var points = history.TakeLast(60).ToList();
        double max = points.Max(p => Math.Max(p.Full, p.Design));
        var grid = new Grid { Height = 160, Margin = new Thickness(8, 4, 8, 4) };
        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var p in points)
        {
            var bar = new Border { Width = Math.Max(3, 880.0 / points.Count - 3), Height = 150 * p.Full / max, Margin = new Thickness(1.5, 0, 1.5, 0),
                CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{p.When:MMM d, yyyy}: {p.Full / 1000.0:0.0} Wh" };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bars.Children.Add(bar);
        }
        grid.Children.Add(bars);
        var first = Ui.Text(points[0].When.ToString("MMM yyyy"), 12, "SubtleBrush");
        first.VerticalAlignment = VerticalAlignment.Top;
        var last = Ui.Text(points[^1].When.ToString("MMM yyyy"), 12, "SubtleBrush");
        last.VerticalAlignment = VerticalAlignment.Top;
        last.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(first);
        grid.Children.Add(last);
        return grid;
    }
}

/// <summary>Free games to claim on Epic, Steam and Prime Gaming.</summary>
public sealed class FreeGamesScreen : ListScreen
{
    private readonly CancellationTokenSource _cts = new();
    private List<Labs.Giveaway>? _list;
    private bool _failed;

    public FreeGamesScreen() : base("Free games", "{Select}  Claim      {Back}  Back") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        _list = await Labs.FreeGamesAsync(_cts.Token);
        _failed = _list == null;
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (_failed) { AddNote("The list of giveaways couldn't be loaded. Check the internet connection."); return; }
        if (_list == null) { AddNote("Looking for free games…"); return; }
        if (_list.Count == 0) { AddNote("No free games right now. Check back in a few days: Epic gives one away every week."); return; }
        AddNote("Free to keep if you claim them before they end. The list comes from GamerPower.");
        foreach (var store in _list.GroupBy(g => g.Store))
        {
            AddHeader(store.Key);
            foreach (var g in store)
            {
                var give = g;
                var icon = new Image();
                if (g.Image.Length > 0) _ = LoadIconAsync(icon, g.Image);
                AddRow(g.Title, Labs.EndsIn(g.Ends), icon: icon, click: () => ProcessUtil.Open(give.Url),
                    subtitle: (g.Worth is { Length: > 0 } w && w != "N/A" ? "Usually " + w + "  ·  " : "") + "Claim it in the store");
            }
        }
    }

    private static async Task LoadIconAsync(Image view, string url) => view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 120));
}

/// <summary>Hours played lately by you and your Steam friends.</summary>
public sealed class LeaderboardScreen : ListScreen
{
    private readonly CancellationTokenSource _cts = new();
    private List<Labs.PlayerWeek>? _players;
    private string? _error;

    public LeaderboardScreen() : base("Friends leaderboard") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        (_players, _error) = await Labs.LeaderboardAsync(Host.Settings.SteamWebApiKey, _cts.Token);
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (_error != null) { AddNote(_error); return; }
        if (_players == null) { AddNote("Asking Steam…"); return; }
        AddNote("Hours played in the last two weeks (the period Steam reports). Friends whose game details are private don't appear.");
        int rank = 1;
        var mine = _players.FirstOrDefault(p => p.IsYou)?.Games.Select(g => g.Game).ToHashSet() ?? new HashSet<string>();
        foreach (var p in _players)
        {
            var shared = p.IsYou ? new List<string>() : p.Games.Select(g => g.Game).Where(mine.Contains).ToList();
            string top = p.Games.Count > 0 ? "Mostly " + p.Games[0].Game : "Nothing lately";
            var row = AddRow($"{rank++}.  {p.Name}{(p.IsYou ? "  (you)" : "")}", Labs.Hours(p.Hours), p.IsYou ? "" : "",
                subtitle: shared.Count > 0 ? top + "  ·  Both playing " + string.Join(", ", shared.Take(3)) : top);
            if (p.IsYou) row.FontWeight = FontWeights.Bold;
        }
    }
}

/// <summary>Which styles the schedule uses.</summary>
public sealed class StyleScheduleScreen : ListScreen
{
    public StyleScheduleScreen() : base("Style schedule") { }

    private static string NameOf(string id) => id.Length == 0 ? "Not chosen" : UiStyles.Find(id).Name;

    protected override void Build()
    {
        // On or off here too, now that the page opens straight from Settings > Themes.
        bool on = Labs.On("styleschedule");
        AddRow("Style schedule", on ? "On" : "Off", "\uE823", click: () => { Labs.Set("styleschedule", !on); UiSound.Select(); Rebuild(); },
            subtitle: "A different style for mornings and nights or weekends, or a random Advanced style each day");
        string mode = Labs.ScheduleMode;
        AddRow("Change", mode switch { "weekend" => "Weekdays and weekends", "random" => "A random Advanced style each day", _ => "Mornings and nights" }, "", click: () => _ = ChooseModeAsync());
        if (mode == "daynight")
        {
            AddRow("Day style", NameOf(Labs.Get("schedule.day")), "", click: () => _ = ChooseStyleAsync("schedule.day"), subtitle: $"From {Labs.Get("schedule.morningHour", "7")}:00");
            AddRow("Night style", NameOf(Labs.Get("schedule.night")), "", click: () => _ = ChooseStyleAsync("schedule.night"), subtitle: $"From {Labs.Get("schedule.nightHour", "19")}:00");
            AddRow("Morning starts at", Labs.Get("schedule.morningHour", "7") + ":00", "", click: () => _ = ChooseHourAsync("schedule.morningHour", 4, 11));
            AddRow("Night starts at", Labs.Get("schedule.nightHour", "19") + ":00", "", click: () => _ = ChooseHourAsync("schedule.nightHour", 16, 23));
        }
        else if (mode == "weekend")
        {
            AddRow("Weekday style", NameOf(Labs.Get("schedule.weekday")), "", click: () => _ = ChooseStyleAsync("schedule.weekday"));
            AddRow("Weekend style", NameOf(Labs.Get("schedule.weekend")), "", click: () => _ = ChooseStyleAsync("schedule.weekend"));
        }
        else AddNote("Today's: " + NameOf(Labs.ScheduledStyle(DateTime.Now) ?? ""));
        AddNote("Choosing a style yourself in Themes still works; the schedule changes it again at the next switch.");
    }

    private async Task ChooseModeAsync()
    {
        int c = await Host.AskAsync("Style schedule", null, "Mornings and nights", "Weekdays and weekends", "A random Advanced style each day");
        if (c < 0) return;
        Labs.Put("schedule.mode", new[] { "daynight", "weekend", "random" }[c]);
        Labs.Put("schedule.applied", "");
        Rebuild();
    }

    private async Task ChooseStyleAsync(string key)
    {
        var styles = UiStyles.All.Concat(UiStyles.Advanced.Where(s => !StyleUnlocks.IsLocked(s))).ToList();
        var menu = new MenuScreen("Choose a style", null, styles.Select(s => s.Name).ToArray());
        Host.Push(menu);
        int c = await menu.Result;
        if (c < 0) return;
        Labs.Put(key, styles[c].Id);
        Labs.Put("schedule.applied", "");
        Rebuild();
    }

    private async Task ChooseHourAsync(string key, int from, int to)
    {
        var hours = Enumerable.Range(from, to - from + 1).ToList();
        int c = await Host.AskAsync("Starts at", null, hours.Select(h => h + ":00").ToArray());
        if (c < 0) return;
        Labs.Put(key, hours[c].ToString());
        Labs.Put("schedule.applied", "");
        Rebuild();
    }
}

/// <summary>Video wallpapers from Wallpaper Engine, as moving backgrounds.</summary>
public sealed class WallpaperEngineScreen : ListScreen
{
    private List<Labs.EngineWallpaper>? _list;

    public WallpaperEngineScreen() : base("Wallpaper Engine") { }

    public override void OnShown()
    {
        Rebuild();
        _ = Task.Run(Labs.WallpaperEngineVideos).ContinueWith(t => Dispatcher.BeginInvoke(() => { _list = t.Result; if (Host.IsTop(this)) Rebuild(); }));
    }

    protected override void Build()
    {
        if (_list == null) { AddNote("Looking for Wallpaper Engine wallpapers…"); return; }
        if (_list.Count == 0)
        {
            AddNote("No video wallpapers were found. Subscribe to video wallpapers in Wallpaper Engine's Workshop (scene and web wallpapers can't be used here).");
            return;
        }
        AddNote("Video wallpapers you've subscribed to in Wallpaper Engine. Importing copies one into SquareDeck, so it appears in " +
                "Moving background with your own videos and keeps working if you unsubscribe. Applying without importing plays it from Wallpaper Engine's folder.");
        foreach (var w in _list)
        {
            var wall = w;
            var icon = new Image();
            if (w.Preview != null) icon.Source = ImageLoader.FromFile(w.Preview, 160);
            string? imported = ImportedCopy(w);
            bool current = string.Equals(Host.Settings.DynamicBackground, w.Video, StringComparison.OrdinalIgnoreCase) ||
                           (imported != null && string.Equals(Host.Settings.DynamicBackground, imported, StringComparison.OrdinalIgnoreCase));
            AddRow(w.Title, current ? "In use" : imported != null ? "Imported" : null, icon: icon, click: () => _ = ChooseAsync(wall));
        }
    }

    /// <summary>Where an imported copy of a wallpaper goes: SquareDeck's own folder for moving backgrounds.</summary>
    private static string CopyPath(Labs.EngineWallpaper w)
    {
        var safe = new string(w.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
        if (safe.Length == 0) safe = "Wallpaper Engine";
        return Path.Combine(DynamicBackgroundScreen.UserFolder, safe + Path.GetExtension(w.Video));
    }

    private static string? ImportedCopy(Labs.EngineWallpaper w)
    {
        var path = CopyPath(w);
        try { return File.Exists(path) && new FileInfo(path).Length == new FileInfo(w.Video).Length ? path : null; }
        catch (Exception) { return null; }
    }

    private async Task ChooseAsync(Labs.EngineWallpaper w)
    {
        string? imported = ImportedCopy(w);
        int c = await Host.AskAsync(w.Title, imported != null ? "It's already imported into SquareDeck." : "Import it into SquareDeck, apply it, or both.",
            imported != null ? new[] { "Apply", "Cancel" } : new[] { "Import into SquareDeck and apply", "Just import", "Just apply", "Cancel" });
        if (imported != null) { if (c == 0) Host.UseDynamicBackground(imported); Rebuild(); return; }
        if (c is < 0 or > 2) return;
        if (c == 2) { Host.UseDynamicBackground(w.Video); Rebuild(); return; }
        string? copy = null, problem = null;
        await Host.RunBusyAsync("Importing " + w.Title, () => Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(DynamicBackgroundScreen.UserFolder);
                var target = CopyPath(w);
                File.Copy(w.Video, target, true);
                copy = target;
            }
            catch (Exception ex) { problem = ex.Message; }
        }));
        if (copy == null) { await Host.AskAsync("Couldn't import it", problem ?? "Something went wrong.", "OK"); return; }
        if (c == 0) Host.UseDynamicBackground(copy);
        else Host.ShowToast("IMPORTED", w.Title, "It's in Settings > Moving background now", "", remember: false);
        Rebuild();
    }
}

/// <summary>Rewards earned by playing.</summary>
public sealed class UnlockablesScreen : ListScreen
{
    public UnlockablesScreen() : base("Unlockables") { }

    protected override void Build()
    {
        var rewards = Labs.Rewards(Host.AllGames, Host.Playtime, Host.Achievements, Host.Library);
        AddNote("Play to earn these. Highlight colors apply straight away; outfits are for the Companion (Settings > Home screen).");
        foreach (var group in rewards.GroupBy(r => r.Kind))
        {
            AddHeader(group.Key == "color" ? "Highlight colors" : "Companion outfits");
            foreach (var r in group)
            {
                var reward = r;
                bool earned = r.Progress >= 1;
                bool inUse = r.Kind == "color" ? string.Equals(Host.Settings.AccentColor, r.Value, StringComparison.OrdinalIgnoreCase) : Labs.Outfit == r.Value;
                var row = AddRow(r.Name, earned ? (inUse ? "In use" : "Earned") : $"{r.Progress * 100:0}%", earned ? "" : "",
                    click: earned ? () => Use(reward, inUse) : null, subtitle: r.Goal);
                if (!earned) row.Opacity = 0.6;
            }
        }
    }

    private void Use(Labs.Reward r, bool inUse)
    {
        if (r.Kind == "color")
        {
            Host.Settings.AccentColor = inUse ? "" : r.Value;
            Host.Settings.Save();
            Host.ApplyLook();
        }
        else Labs.Put("companion.outfit", inUse ? "" : r.Value);
        Rebuild();
    }
}


/// <summary>
/// Pick a mood, get three games: the moods as a row of colored cards, then three big cards for the games that fit,
/// each with its artwork, cover, genre, how long it takes and how much you've played it. The page takes on the
/// highlighted game's artwork behind it. A plays, Y picks again.
/// </summary>
public sealed class MoodScreen : Screen
{
    private static readonly Dictionary<string, (string Line, Color From, Color To)> Looks = new()
    {
        ["chill"] = ("Something relaxed, nothing to prove", Color.FromRgb(0x2E, 0xB8, 0x9A), Color.FromRgb(0x1C, 0x6E, 0x8C)),
        ["adrenaline"] = ("Fast, loud, heart racing", Color.FromRgb(0xF0, 0x4E, 0x3E), Color.FromRgb(0xB0, 0x1E, 0x5A)),
        ["story"] = ("A world to get lost in", Color.FromRgb(0x8E, 0x5C, 0xF0), Color.FromRgb(0x46, 0x2E, 0xA8)),
        ["laugh"] = ("Short, silly, fun right away", Color.FromRgb(0xF7, 0xB5, 0x2C), Color.FromRgb(0xE0, 0x6A, 0x1E)),
        ["brain"] = ("Plan, think, outsmart", Color.FromRgb(0x3E, 0x8E, 0xF0), Color.FromRgb(0x24, 0x4C, 0xB8)),
    };

    private readonly WrapPanel _moods = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _results = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, ImageSource?> _heroes = new();
    private string? _mood;
    private List<Game> _picks = new();
    private Button? _firstMood;

    public MoodScreen()
    {
        var page = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        var intro = Ui.Text("Pick a mood and get three games from your library that fit it.", 17, "SubtleBrush");
        intro.HorizontalAlignment = HorizontalAlignment.Center;
        intro.Margin = new Thickness(0, 0, 0, 18);
        page.Children.Add(intro);
        page.Children.Add(_moods);
        page.Children.Add(_results);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = page };
        SetPage("How are you feeling?", scroll, "{Select}  Choose        {Options}  Pick again        {Back}  Back", wide: true);
        BuildMoods();
    }

    public override void OnShown() => FocusLater(_firstMood);

    public override void OnClosed() => _cts.Cancel();

    public override void FocusDefault()
    {
        if (_firstMood != null) _firstMood.Focus();
        else base.FocusDefault();
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Y && _mood != null) { Pick(_mood, focusResults: true); return true; }
        return false;
    }

    private void BuildMoods()
    {
        _moods.Children.Clear();
        _firstMood = null;
        foreach (var m in Labs.Moods)
        {
            var mood = m;
            (string Line, Color From, Color To) look = Looks.TryGetValue(m.Id, out var l) ? l : ("", Color.FromRgb(0x55, 0x55, 0x66), Color.FromRgb(0x33, 0x33, 0x44));
            bool picked = _mood == m.Id;
            var face = new Grid { Width = 212, Height = 128 };
            var fill = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 14)), Background = new LinearGradientBrush(look.From, look.To, 35), Opacity = _mood == null || picked ? 1 : 0.55 };
            face.Children.Add(fill);
            var glyph = new TextBlock { Text = m.Glyph, FontFamily = Ui.IconFont, FontSize = 64, Foreground = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 12, 0) };
            face.Children.Add(glyph);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 14, 14) };
            words.Children.Add(new TextBlock { Text = m.Name, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            words.Children.Add(new TextBlock { Text = look.Line, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap });
            face.Children.Add(words);
            if (picked)
            {
                var check = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12),
                    Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 14, Foreground = new SolidColorBrush(look.To), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                face.Children.Add(check);
            }
            var tile = Ui.Tile(face);
            tile.Margin = new Thickness(0, 0, 14, 14);
            tile.Tag = m.Id;
            tile.Click += (_, _) => Pick(mood.Id, focusResults: true);
            _moods.Children.Add(tile);
            _firstMood ??= tile;
        }
    }

    private void Pick(string mood, bool focusResults)
    {
        _mood = mood;
        _picks = Labs.PickForMood(mood, Host.AllGames);
        BuildMoods();
        BuildResults(focusResults);
    }

    private void BuildResults(bool focus)
    {
        _results.Children.Clear();
        if (_mood == null) return;
        var heading = Ui.Text("Try one of these", 22, "FgBrush", bold: true);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.Margin = new Thickness(0, 14, 0, 16);
        _results.Children.Add(heading);
        if (_picks.Count == 0)
        {
            var none = Ui.Text("Nothing installed fits this mood yet (genres appear once games' details have been looked up). Try another, or pick again.", 16, "SubtleBrush");
            none.TextWrapping = TextWrapping.Wrap;
            none.TextAlignment = TextAlignment.Center;
            none.MaxWidth = 700;
            _results.Children.Add(none);
            SetPageBackdrop(null);
            return;
        }
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        Button? first = null;
        foreach (var g in _picks)
        {
            var card = GameCard(g);
            row.Children.Add(card);
            first ??= card;
        }
        _results.Children.Add(row);

        var again = Ui.Row("Pick again", null, "", subtitle: "Three more for the same mood");
        again.Width = 420;
        again.HorizontalAlignment = HorizontalAlignment.Center;
        again.Margin = new Thickness(0, 8, 0, 0);
        again.Click += (_, _) => Pick(_mood, focusResults: true);
        _results.Children.Add(again);
        if (focus && first != null) FocusLater(first);
    }

    private Button GameCard(Game game)
    {
        const double W = 372, ArtH = 176;
        var card = new StackPanel { Width = W };

        // Artwork: the wide hero picture (or the cover, filling), with the square cover over its corner.
        var art = new Grid { Height = ArtH, ClipToBounds = true };
        var hero = new Image { Stretch = Stretch.UniformToFill, Source = game.Cover, Opacity = 0.9 };
        art.Children.Add(hero);
        art.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xC8, 0, 0, 0), 90) });
        var cover = new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 10)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 14), ClipToBounds = true,
            Background = game.Cover != null ? new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(2) };
        art.Children.Add(cover);
        var playBadge = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 16) };
        playBadge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var playRow = new StackPanel { Orientation = Orientation.Horizontal };
        playRow.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
        playRow.Children.Add(new TextBlock { Text = game.IsRunning ? "Resume" : "Play", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        playBadge.Child = playRow;
        art.Children.Add(playBadge);
        double corner = Math.Max(Theme.TileCorner, 14);
        art.Clip = new RectangleGeometry(new Rect(0, 0, W, ArtH), corner, corner);
        card.Children.Add(art);
        _ = LoadHeroAsync(game, hero);

        // What it is.
        var info = new StackPanel { Margin = new Thickness(6, 12, 6, 6) };
        var name = Ui.Text(game.Name, 20, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(name);
        var facts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Chip(string glyph, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var c = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            c.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var r = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 12, "AccentBrush");
            g.Margin = new Thickness(0, 1, 6, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            r.Children.Add(g);
            r.Children.Add(Ui.Text(text, 13, "FgBrush", bold: true));
            c.Child = r;
            facts.Children.Add(c);
        }
        Chip("", GenreCache.Of(game) ?? "");
        if (HowLongToBeat.Cached(game).Times is { Main: > 0 } t) Chip("", "About " + Labs.Hours(t.Main) + " to finish");
        double played = Host.Playtime.TotalSeconds(game.Key);
        if (played < 60 && game.IsSteam && SteamLibrary.GetPlaytimeMinutes(game.AppId) is int steamMinutes) played = steamMinutes * 60.0;
        Chip("", played >= 60 ? PlaytimeStore.Format(played) + " played" : "Not played yet");
        if (game.IsSteam && Host.Achievements.ProgressOf(game.AppId) is { Total: > 0 } a) Chip("", $"{a.Unlocked}/{a.Total} achievements");
        info.Children.Add(facts);
        string when = game.RecentAt > DateTime.MinValue ? "Last played " + Ui.Ago(game.RecentAt) : game.IsSteam ? "Steam" : game.Source;
        if (!string.IsNullOrEmpty(when)) info.Children.Add(Ui.Text(when, 13, "SubtleBrush"));
        card.Children.Add(info);

        var tile = Ui.Tile(card);
        tile.Margin = new Thickness(0, 0, 16, 16);
        tile.Click += (_, _) => _ = Host.ActivateAsync(game);
        tile.GotKeyboardFocus += (_, _) => SetPageBackdrop(_heroes.GetValueOrDefault(game.Key) ?? game.Cover);
        return tile;
    }

    private async Task LoadHeroAsync(Game game, Image target)
    {
        if (_heroes.TryGetValue(game.Key, out var known)) { if (known != null) target.Source = known; return; }
        var ct = _cts.Token;
        try
        {
            var picture = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
                var path = await client.GetHeroAsync(game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 800);
            }, ct);
            _heroes[game.Key] = picture;
            if (picture != null && !ct.IsCancellationRequested)
            {
                target.Source = picture;
                target.Opacity = 1;
            }
        }
        catch (Exception) { }
    }
}

/// <summary>Unlocked achievements as stickers on a corkboard: move and turn them, rare ones shine.</summary>
public sealed class StickerBookScreen : Screen
{
    private readonly Game _game;
    private readonly CancellationTokenSource _cts = new();
    private readonly Canvas _board = new() { Width = 1120, Height = 540, ClipToBounds = true };
    private readonly TextBlock _status = Ui.Text("Loading achievements…", 16, "SubtleBrush");
    private Button? _held;
    private const double Size = 92;

    public StickerBookScreen(Game game)
    {
        _game = game;
        var cork = new Border { Width = 1120, Height = 540, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = _board,
            Background = new LinearGradientBrush(Color.FromRgb(0xB9, 0x87, 0x52), Color.FromRgb(0x9C, 0x6C, 0x3E), 45),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x45, 0x22)), BorderThickness = new Thickness(8) };
        // Cork speckles.
        var rand = new Random(7);
        for (int i = 0; i < 260; i++)
        {
            var dot = new Ellipse { Width = 3, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(70, 60, 35, 15)) };
            Canvas.SetLeft(dot, rand.NextDouble() * 1110);
            Canvas.SetTop(dot, rand.NextDouble() * 530);
            _board.Children.Add(dot);
        }
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _status.Margin = new Thickness(4, 0, 0, 10);
        body.Children.Add(_status);
        body.Children.Add(cork);
        SetPage("Sticker book – " + game.Name, body, "{Select}  Pick up / put down      {Details}  Turn      D-pad  Move      {Back}  Back");
    }

    public override void OnShown() => _ = LoadAsync();
    public override void OnClosed() => _cts.Cancel();
    public override void FocusDefault() => _board.Children.OfType<Button>().FirstOrDefault()?.Focus();

    private async Task LoadAsync()
    {
        var list = await Labs.AchievementsAsync(_game.AppId, _cts.Token);
        if (list == null) { _status.Text = "Your achievements couldn't be read. Set “Game details” to Public in your Steam profile's privacy settings."; return; }
        var unlocked = list.Where(a => a.Unlocked).OrderBy(a => a.When ?? DateTime.MaxValue).ToList();
        if (unlocked.Count == 0) { _status.Text = "No achievements unlocked in this game yet. Each one becomes a sticker here."; return; }
        var rarity = await Labs.RarityAsync(_game.AppId, _cts.Token);
        _status.Text = $"{unlocked.Count} of {list.Count} stickers. Rare ones (under 10% of players) are holographic.";
        int i = 0;
        foreach (var a in unlocked)
        {
            bool rare = rarity.TryGetValue(a.Api, out var pct) && pct < 10;
            AddSticker(a, rare, pct, i++);
        }
        FocusDefault();
    }

    private void AddSticker(Labs.AchievementInfo a, bool rare, double pct, int index)
    {
        string key = $"stk:{_game.AppId}:{a.Api}";
        double x, y, angle;
        var saved = Labs.Get(key).Split(',');
        if (saved.Length == 3 && double.TryParse(saved[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
            double.TryParse(saved[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
            double.TryParse(saved[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out angle)) { }
        else
        {
            // A loose grid with a little scatter, as if stuck on by hand.
            var r = new Random(a.Api.GetHashCode());
            x = 24 + index % 10 * 108 + r.Next(-10, 10);
            y = 24 + index / 10 % 5 * 104 + r.Next(-8, 8);
            angle = r.Next(-12, 12);
        }
        var picture = new Image { Stretch = Stretch.UniformToFill };
        _ = LoadAsync(picture, a.Icon);
        var face = new Border { Width = Size, Height = Size, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = picture,
            BorderThickness = new Thickness(5), BorderBrush = Brushes.White, Background = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Opacity = 0.45 } };
        if (rare)
        {
            var holo = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            foreach (var (c, o) in new[] { ("#FF7AD9", 0.0), ("#7AE1FF", 0.33), ("#B6FF7A", 0.66), ("#FFE27A", 1.0) })
                holo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
            face.BorderBrush = holo;
            if (!Theme.ReduceMotion)
            {
                var spin = new RotateTransform(0, 0.5, 0.5);
                holo.RelativeTransform = spin;
                spin.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            }
        }
        var sticker = new Button { Style = Ui.StyleOf("BareButton"), Content = face, Focusable = true, Tag = key, FocusVisualStyle = null,
            ToolTip = a.Name + (a.Description.Length > 0 ? " – " + a.Description : "") + (rare ? $"  ({pct:0.#}% of players)" : ""),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(angle) } } };
        Canvas.SetLeft(sticker, x);
        Canvas.SetTop(sticker, y);
        sticker.GotKeyboardFocus += (_, _) => { Scale(sticker, 1.12); Panel.SetZIndex(sticker, 10); _status.Text = sticker.ToolTip as string ?? ""; };
        sticker.LostKeyboardFocus += (_, _) => { Scale(sticker, _held == sticker ? 1.2 : 1); Panel.SetZIndex(sticker, 1); };
        sticker.Click += (_, _) => Toggle(sticker);
        _board.Children.Add(sticker);
    }

    private static void Scale(Button b, double s)
    {
        if (b.RenderTransform is TransformGroup g && g.Children[0] is ScaleTransform st) { st.ScaleX = s; st.ScaleY = s; }
    }

    private static RotateTransform Turn(Button b) => (RotateTransform)((TransformGroup)b.RenderTransform).Children[1];

    private void Toggle(Button sticker)
    {
        if (_held == sticker) { Drop(); return; }
        if (_held != null) Drop();
        _held = sticker;
        Scale(sticker, 1.2);
        UiSound.Select();
    }

    private void Drop()
    {
        if (_held == null) return;
        var s = _held;
        _held = null;
        Scale(s, s.IsKeyboardFocused ? 1.12 : 1);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Labs.Put((string)s.Tag, string.Format(inv, "{0:0},{1:0},{2:0}", Canvas.GetLeft(s), Canvas.GetTop(s), Turn(s).Angle));
        UiSound.Back();
    }

    public override bool OnAction(NavAction action)
    {
        if (_held == null) return false;
        double step = 24;
        switch (action)
        {
            case NavAction.Left: Canvas.SetLeft(_held, Math.Max(0, Canvas.GetLeft(_held) - step)); return true;
            case NavAction.Right: Canvas.SetLeft(_held, Math.Min(_board.Width - Size, Canvas.GetLeft(_held) + step)); return true;
            case NavAction.Up: Canvas.SetTop(_held, Math.Max(0, Canvas.GetTop(_held) - step)); return true;
            case NavAction.Down: Canvas.SetTop(_held, Math.Min(_board.Height - Size, Canvas.GetTop(_held) + step)); return true;
            case NavAction.X: Turn(_held).Angle += 15; return true;
            case NavAction.Y: Turn(_held).Angle -= 15; return true;
            case NavAction.Back: Drop(); return true;
        }
        return false;
    }

    private static async Task LoadAsync(Image view, string url) => view.Source = url.Length == 0 ? null : await Task.Run(() => ImageLoader.FromUrlAsync(url, 128));
}

/// <summary>The companion's look: which character, its color and name, and a try of each reaction.</summary>
public sealed class CompanionScreen : ListScreen
{
    public CompanionScreen() : base("Companion") { }

    protected override void Build()
    {
        var preview = new Canvas { Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
        CompanionArt.Draw(preview, CompanionArt.SpeciesId, CompanionArt.ColorHex, "happy", false, Labs.Outfit, cell: 10);
        Rows.Children.Add(preview);
        var name = Ui.Text(CompanionArt.Name, 24, "FgBrush"); // regular type, as the Switch 2 pages have it
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 0, 0, 8);
        Rows.Children.Add(name);
        if (!Labs.On("companion")) AddNote("Turn on Companion in Settings > Home screen to see it on Home.");

        AddHeader("Look");
        AddRow("Character", CompanionArt.Species.First(s => s.Id == CompanionArt.SpeciesId).Name, "", click: async () =>
        {
            int c = await Host.AskAsync("Character", null, CompanionArt.Species.Select(s => s.Name).ToArray());
            if (c >= 0) Change("companion.species", CompanionArt.Species[c].Id);
        });
        AddRow("Color", CompanionArt.Colors.FirstOrDefault(c => c.Hex == CompanionArt.ColorHex).Name ?? "Custom", "", click: async () =>
        {
            int c = await Host.AskAsync("Color", null, CompanionArt.Colors.Select(x => x.Name).ToArray());
            if (c >= 0) Change("companion.color", CompanionArt.Colors[c].Hex);
        });
        AddRow("Name", CompanionArt.Name, "", click: async () =>
        {
            var n = await Host.PromptAsync("Name your companion", null, CompanionArt.Name);
            if (n != null && n.Trim().Length > 0) Change("companion.name", n.Trim().Length > 16 ? n.Trim()[..16] : n.Trim());
        });
        AddRow("Outfit", CompanionArt.OutfitName(Labs.Outfit), "", click: () => Host.Push(new UnlockablesScreen()), subtitle: "Earned in Unlockables");

        AddHeader("Try a reaction");
        AddNote("It reacts by itself on Home: a wave when you come back from a game, a jump for achievements, a spin for milestones, " +
                "hearts for new favorites, a worried shake at low battery, dancing to music, yawns when you haven't played for a while, " +
                "sleep late at night, and its eyes follow you along the home row. It comes along to every page, doing what suits it (tinkering in Settings, " +
                "searching the Library, dancing on the Music page, shopping in the Stores…), wanders around the page, gets up to all sorts on Home, and puts on " +
                "a little show while a game starts. Moving between pages, it finishes what it's doing before starting the next thing.");
        foreach (var (id, label) in new[] { ("wave", "Wave"), ("jump", "Jump for joy"), ("spin", "Spin"), ("hearts", "Hearts"), ("worried", "Worried"),
                                            ("dance", "Dance"), ("yawn", "Yawn"), ("sleep", "Sleep"), ("peek", "Peek"), ("bye", "Wave goodbye"),
                                            ("walk", "Go for a walk"), ("juggle", "Juggle"), ("flip", "Somersault"), ("trip", "Trip over"), ("peekaboo", "Peekaboo"),
                                            ("stretch", "Stretch"), ("lookaround", "Look around"), ("balloon", "Balloon"), ("confetti", "Confetti"),
                                            ("tinker", "Tinker"), ("read", "Read"), ("search", "Search"), ("think", "Think"), ("game", "Play a game"),
                                            ("shop", "Go shopping"), ("camera", "Take a picture"), ("trophy", "Trophy"), ("zoom", "Zoom"),
                                            ("roam", "Wander around"), ("hop", "Hop along"), ("cartwheel", "Cartwheel"), ("slide", "Slide"),
                                            ("moonwalk", "Moonwalk"), ("ball", "Kick a ball"), ("bubbles", "Blow bubbles"), ("magic", "Magic trick"),
                                            ("meditate", "Meditate"), ("sneeze", "Sneeze"), ("wobble", "Wobble"), ("cheer", "Cheer"), ("nod", "Nod") })
        {
            var action = id;
            AddRow(label, null, "", click: () => Host.CompanionReact(action));
        }
        AddHeader("Game acts");
        AddNote("Highlight a game on Home for a moment and it acts out what kind of game it is, and says something about it: how long you've played, " +
                "when you last did, how long it takes to beat. It also gets dizzy if you whiz along the row, giggles when you tap it, says goodbye " +
                "in the game's own spirit, asks how it went when you're back, and knows the holidays.");
        foreach (var (id, label) in new[] { ("vroom", "Race car"), ("slash", "Sword swing"), ("pewpew", "Pew pew"), ("punch", "Combo"), ("spooked", "Spooked"),
                                            ("flashlight", "Flashlight"), ("puzzle", "Puzzle"), ("farm", "Grow a flower"), ("dribble", "Dribble"), ("dice", "Roll a d20"),
                                            ("potion", "Potion"), ("chess", "Chess move"), ("rocket", "Rocket launch"), ("plane", "Airplane"), ("coin", "Coin block"),
                                            ("camp", "Campfire"), ("tea", "Tea break"), ("notes", "Rhythm"), ("build", "Build a house"), ("pixel", "8-bit jump"),
                                            ("fish", "Go fishing"), ("dizzy", "Dizzy"), ("pet", "Pet"), ("snow", "Snow"), ("fireworks", "Fireworks"),
                                            ("feast", "Feast"), ("pumpkin", "Pumpkin") })
        {
            var game = id;
            AddRow(label, null, "\uE7FC", click: () => Host.CompanionReact(game));
        }
        AddHeader("Hobbies");
        AddNote("Things it gets up to by itself on Home now and then.");
        foreach (var (id, label) in new[] { ("yoyo", "Yo-yo"), ("kite", "Fly a kite"), ("paint", "Paint"), ("jumprope", "Jump rope"), ("gum", "Bubble gum"),
                                            ("headphones", "Headphones"), ("stargaze", "Stargaze"), ("sandwich", "Snack") })
        {
            var hobby = id;
            AddRow(label, null, "\uE7FC", click: () => Host.CompanionReact(hobby));
        }
        AddHeader("Petting");
        AddRow("Pet " + CompanionArt.Name, null, "\uE76E", click: () => Host.PetCompanion(),
            subtitle: $"Tap it on Home to pet it: it {CompanionSounds.Describe(CompanionArt.SpeciesId)} (each character has its own noises, with interface sounds on), " +
                      "and does a little something of its own");
    }

    private void Change(string key, string value)
    {
        Labs.Put(key, value);
        Host.RefreshCompanion();
        Rebuild();
    }
}

/// <summary>The changelog, read inside SquareDeck: each version's changes, newest first, a few at a time.</summary>
public sealed class ChangelogScreen : ListScreen
{
    private readonly CancellationTokenSource _cts = new();
    private List<(string Version, List<string> Lines)>? _versions;
    private bool _failed;
    private int _shown = 6;

    public ChangelogScreen() : base("Changelogs") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        var text = await Updater.ChangelogAsync(_cts.Token);
        _failed = text == null;
        if (text != null)
        {
            _versions = new();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("## ")) { _versions.Add((line[3..].Trim(), new List<string>())); continue; }
                if (_versions.Count == 0 || line.Trim().Length == 0 || line.StartsWith("# ")) continue;
                _versions[^1].Lines.Add(line);
            }
        }
        if (Host.IsTop(this)) Rebuild();
    }

    /// <summary>Markdown's emphasis and code marks taken out, for plain reading.</summary>
    private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\*\*|__|`", "");

    protected override void Build()
    {
        if (_failed) { AddNote("The changelog couldn't be loaded. Check the internet connection."); return; }
        if (_versions == null) { AddNote("Loading the changelog…"); return; }
        AddNote($"You have version {Updater.Current}.");
        foreach (var (version, lines) in _versions.Take(_shown))
        {
            AddHeader(version == Updater.Current.ToString() ? version + "  (this version)" : version);
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                int indent = line.Length - trimmed.Length;
                bool bullet = trimmed.StartsWith("- ") || trimmed.StartsWith("* ");
                bool heading = !bullet && trimmed.StartsWith("**") && trimmed.EndsWith("**");
                var text = Ui.Text((bullet ? "•  " : "") + Plain(bullet ? trimmed[2..] : trimmed), heading ? 16 : 15, heading ? "FgBrush" : "SubtleBrush", bold: heading);
                text.TextWrapping = TextWrapping.Wrap;
                text.Margin = new Thickness(12 + indent * 10, heading ? 8 : 2, 12, 2);
                Rows.Children.Add(text);
            }
        }
        if (_shown < _versions.Count)
            AddRow("Show older versions", $"{_versions.Count - _shown} more", "", click: () => { _shown += 10; Rebuild(); });
    }
}

/// <summary>Settings for the cover features: how dark and soft the other covers are, and the cover art effect.</summary>
public sealed class CoverLabsScreen : ListScreen
{
    private readonly bool _effect;

    public CoverLabsScreen(bool effect) : base(effect ? "Cover art effect" : "Darken covers") { _effect = effect; }

    protected override void Build()
    {
        if (_effect)
        {
            string kind = Labs.Get("covereffect.kind", "retro");
            AddPreview(kind);
            AddRow("Effect", CoverEffects.NameOf(kind), "\uE7B8", click: () => Host.Push(new CoverEffectPickerScreen(this)),
                subtitle: CoverEffects.All.FirstOrDefault(x => x.Id == kind).About + $". {CoverEffects.All.Length - 1} effects to choose from");
            if (kind is "retro" or "gameboy" or "bricks" or "terminal" or "shuffle")
            {
                string px = Labs.Get("covereffect.pixels", "48");
                AddRow(kind switch { "bricks" => "Brick size", "terminal" => "Text size", _ => "Pixel size" },
                    CoverEffects.DetailLevels.FirstOrDefault(d => d.Value == px).Name ?? "Classic", "\uE80A",
                    click: () => Pick("covereffect.pixels", CoverEffects.DetailLevels),
                    subtitle: "From big chunky blocks to fine detail that's barely pixelated");
            }
            if (kind is "duotone" or "shuffle")
            {
                string palette = Labs.Get("covereffect.duotone", "accent");
                AddRow("Duotone colors", CoverEffects.DuotonePalettes.FirstOrDefault(x => x.Id == palette).Name ?? "Highlight color", "\uE790",
                    click: () => Pick("covereffect.duotone", CoverEffects.DuotonePalettes.Select(x => (x.Id, x.Name)).ToArray()));
            }
            AddNote("Applies to the covers on Home. Covers change over a moment as each one is redrawn. Turn it off in Settings > Home screen to go back to sharp covers.");
            return;
        }
        string dark = Labs.Get("coverdim.dark", "40"), soft = Labs.Get("coverdim.soft", "0");
        AddRow("Brightness", dark switch { "20" => "Slightly darker", "30" => "Darker", "40" => "Dark", "55" => "Very dark", "70" => "Darkest", _ => dark + "%" }, "",
            click: () => Pick("coverdim.dark", new[] { ("20", "Slightly darker"), ("30", "Darker"), ("40", "Dark"), ("55", "Very dark"), ("70", "Darkest") }),
            subtitle: "How much darker covers are when they aren't highlighted");
        AddRow("Contrast", soft switch { "0" => "Unchanged", "15" => "A little softer", "30" => "Softer", "45" => "Much softer", _ => soft + "%" }, "",
            click: () => Pick("coverdim.soft", new[] { ("0", "Unchanged"), ("15", "A little softer"), ("30", "Softer"), ("45", "Much softer") }),
            subtitle: "Lower contrast makes the other covers recede further");
        AddNote("The highlighted cover is always shown as it is.");
    }

    /// <summary>A cover from the library, before and after, so the effect can be seen without leaving the page.</summary>
    private void AddPreview(string kind)
    {
        var source = Host.AllGames.Select(g => g.Cover).OfType<System.Windows.Media.Imaging.BitmapSource>().FirstOrDefault(b => b.IsFrozen);
        if (source == null) return;
        ImageSource after;
        try { after = CoverEffects.RenderNow(source, kind); } catch { return; }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 14) };
        foreach (var (picture, label) in new[] { ((ImageSource)source, "Before"), (after, "After") })
        {
            var image = new Image { Source = picture, Width = 200, Height = 200, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, CoverEffects.IsPixelArt(kind) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            var frame = new Border { Width = 200, Height = 200, Child = image, Clip = new RectangleGeometry(new Rect(0, 0, 200, 200), 10, 10) };
            var caption = Ui.Text(label, 14, "SubtleBrush");
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 6, 0, 0);
            var column = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            column.Children.Add(frame);
            column.Children.Add(caption);
            row.Children.Add(column);
        }
        Rows.Children.Add(row);
    }

    /// <summary>Called by the effect picker after a choice.</summary>
    public void Chosen()
    {
        Host.ApplyCoverLabs();
        Rebuild();
    }

    private async void Pick(string key, (string Value, string Name)[] options)
    {
        int c = await Host.AskAsync(_effect ? "Cover art effect" : "Darken covers", null, options.Select(o => o.Name).ToArray());
        if (c < 0) return;
        Labs.Put(key, options[c].Value);
        Host.ApplyCoverLabs();
        Rebuild();
    }
}

/// <summary>Every cover art effect, each with what it looks like; choosing one goes back to the cover settings.</summary>
public sealed class CoverEffectPickerScreen : ListScreen
{
    private readonly CoverLabsScreen _owner;

    public CoverEffectPickerScreen(CoverLabsScreen owner) : base("Choose an effect") { _owner = owner; }

    protected override void Build()
    {
        string current = Labs.Get("covereffect.kind", "retro");
        foreach (var (id, name, about) in CoverEffects.All)
            AddRow(name, id == current ? "In use" : null, id == current ? "\uE73E" : "\uE7B8", subtitle: about, click: () =>
            {
                Labs.Put("covereffect.kind", id);
                Host.Pop(this);
                _owner.Chosen();
            });
    }
}

