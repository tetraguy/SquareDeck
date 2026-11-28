using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Experimental "SquareDeck EQ" (8.5.81): setup, the preset for each output, your own curves and the smart extras.</summary>
public sealed class EqScreen : ListScreen
{
    public EqScreen() : base("SquareDeck EQ") { }

    /// <summary>"Installing necessary plugins": downloads and installs Equalizer APO, then sets up SquareDeck EQ.</summary>
    private async void InstallPlugin()
    {
        var screen = new EqInstallScreen();
        Host.Push(screen);
        var problem = await screen.Result;
        Rebuild();
        if (problem is null or "cancelled") return;
        int c = await Host.AskAsync("The plugin isn't installed", problem, "Try again", "Open Equalizer APO's page", "OK");
        if (c == 0) InstallPlugin();
        else if (c == 1) ProcessUtil.OpenOutside(SquareEq.Download);
    }

    protected override void Build()
    {
        if (!SquareEq.Installed)
        {
            AddNote("SquareDeck EQ needs one plugin: Equalizer APO, a free, open-source equalizer for Windows. SquareDeck downloads it from its official page and installs it for you.");
            AddRow("Install", "About 12 MB", "\uE896", click: InstallPlugin,
                subtitle: "Installing necessary plugins: one Windows permission prompt, and a window to tick the Ally's speakers");
            return;
        }
        if (!SquareEq.Ready)
        {
            AddNote("Equalizer APO is installed. One step left: let SquareDeck give it the EQ to play (one Windows permission prompt).");
            AddRow("Set up SquareDeck EQ", "Not set up", "\uE896", click: async () =>
            {
                int c = await Host.AskAsync("Set up SquareDeck EQ",
                    "Should SquareDeck's EQ be the only one Equalizer APO plays? Your own Equalizer APO settings are kept in a backup either way, and come back if you undo the setup.",
                    "SquareDeck's EQ only (recommended)", "Add it to my Equalizer APO settings", "Cancel");
                if (c is < 0 or > 1) return;
                var error = await SquareEq.SetUpAsync(only: c == 0);
                if (error != null) await Host.AskAsync("SquareDeck EQ isn't set up", error, "OK");
                Rebuild();
            });
            AddRow("Equalizer APO's devices", null, "\uE7F5", click: () => SquareEq.OpenDeviceSelector(),
                subtitle: "Tick the Ally's speakers and your headphones in Equalizer APO's Device Selector");
            return;
        }

        SquareEq.Tick();
        var (preset, why) = SquareEq.Current;
        bool guard = SquareEq.Output.Kind == "speakers" && Labs.Get("eq.guard", "1") == "1";
        Rows.Children.Add(EqCurve.Make(preset, 900, 170, guard, margin: new Thickness(0, 8, 0, 6)));
        AddRow("Playing now", preset.Name, "\uE9E9", subtitle: why + (SquareEq.Output.Name.Length > 0 ? "  ·  " + SquareEq.Output.Name : ""),
            click: () => Host.Push(new EqEditorScreen(preset, null)));

        AddHeader("Presets");
        foreach (var (id, name, _) in SquareEq.Outputs)
        {
            var output = id;
            var chosen = SquareEq.Find(SquareEq.PresetFor(output));
            AddRow(name, chosen.Name + (SquareEq.Output.Kind == output ? "  ·  in use" : ""), output switch { "speakers" => "\uE7F5", "headphones" => "\uE7F6", _ => "\uE7F4" },
                click: () => Host.Push(new EqPresetScreen(name, SquareEq.PresetFor(output), p => { Labs.Put("eq." + output, p.Id); SquareEq.Apply(); })),
                subtitle: chosen.About);
        }
        AddNote("Switches by itself when the sound moves to headphones, Bluetooth or a TV. Choosing a preset plays each one as you move through the list, so you can hear it.");

        AddHeader("Your own");
        AddRow("Make your own EQ", null, "\uE710", click: () => Host.Push(new EqEditorScreen(preset with { Id = "", Name = "My EQ" }, saved =>
        {
            Labs.Put("eq." + SquareEq.Output.Kind, saved.Id);
            SquareEq.Apply();
        })), subtitle: "Starts from " + preset.Name + ". Ten bands plus bass, clarity, air and stereo width, heard as you change them");
        foreach (var mine in SquareEq.Mine())
        {
            var p = mine;
            AddRow(p.Name, null, "\uE70F", click: async () =>
            {
                int c = await Host.AskAsync(p.Name, null, "Edit", "Rename", "Delete", "Cancel");
                if (c == 0) Host.Push(new EqEditorScreen(p, _ => SquareEq.Apply()));
                else if (c == 1 && await Host.PromptAsync("Rename", null, p.Name) is { Length: > 0 } name) { SquareEq.SaveMine(p, name.Trim()); Rebuild(); }
                else if (c == 2) { SquareEq.DeleteMine(p.Id); SquareEq.Apply(); Rebuild(); }
            });
        }

        AddHeader("Smart");
        bool on(string key, string def) => Labs.Get(key, def) == "1";
        void Flip(string key, string def) { Labs.Put(key, on(key, def) ? "0" : "1"); SquareEq.Tick(); SquareEq.Apply(); Rebuild(); }
        AddRow("Speaker guard", on("eq.guard", "1") ? "On" : "Off", "\uE7F5", click: () => Flip("eq.guard", "1"),
            subtitle: "On the Ally's speakers, filters out the deep bass they can't play, so they don't distort or rattle and can go louder cleanly");
        AddRow("Loudness at low volume", on("eq.loudness", "0") ? "On" : "Off", "\uE995", click: () => Flip("eq.loudness", "0"),
            subtitle: "Quiet sound loses its bass and treble to the ear first: they come back a little as you turn the volume down");
        AddRow("An EQ for each game", on("eq.games", "1") ? "On" : "Off", "\uE7FC", click: () => Flip("eq.games", "1"),
            subtitle: "A game's profile can have its own preset, or match the game by its genre (in its options, Game profile)");
        int balance = int.TryParse(Labs.Get("eq.balance", "0"), out var bal) ? bal : 0;
        AddRow("Balance", balance == 0 ? "Centre" : balance < 0 ? $"Left {-balance}%" : $"Right {balance}%", "\uE8AB", click: async () =>
        {
            int[] steps = { -50, -30, -15, 0, 15, 30, 50 };
            int c = await Host.AskAsync("Balance", "Turns the other side down.", steps.Select(x => x == 0 ? "Centre" : x < 0 ? $"Left {-x}%" : $"Right {x}%").ToArray());
            if (c < 0) return;
            Labs.Put("eq.balance", steps[c].ToString());
            SquareEq.Apply();
            Rebuild();
        });

        AddHeader("Equalizer APO");
        AddRow("Equalizer APO's devices", null, "\uE7F5", click: () => SquareEq.OpenDeviceSelector(),
            subtitle: "The EQ only plays on the devices ticked there: add headphones, Bluetooth or a TV the first time you use them");
        AddRow("Give Equalizer APO its own settings back", null, "\uE7A7", click: async () =>
        {
            if (await Host.AskAsync("Undo the setup?", "Equalizer APO goes back to the settings it had before, and SquareDeck's EQ stops playing.", "Undo", "Cancel") != 0) return;
            var error = await SquareEq.UndoSetUpAsync();
            if (error != null) await Host.AskAsync("SquareDeck EQ", error, "OK");
            Rebuild();
        });
        AddNote("The EQ is turned down by as much as it boosts, so it never clips: presets with big boosts sound a little quieter. " +
                "Turn SquareDeck EQ off in Settings > Sound and the sound plays as it is.");
    }
}

/// <summary>Every preset by group, each heard as you move to it; choosing one keeps it.</summary>
public sealed class EqPresetScreen : ListScreen
{
    private readonly string _current;
    private readonly Action<SquareEq.Preset> _chosen;
    private bool _picked;

    public EqPresetScreen(string title, string current, Action<SquareEq.Preset> chosen) : base(title) { _current = current; _chosen = chosen; }

    protected override void Build()
    {
        foreach (var group in SquareEq.All.GroupBy(p => p.Group))
        {
            AddHeader(group.Key);
            foreach (var preset in group)
            {
                var p = preset;
                var row = AddRow(p.Name, p.Id == _current ? "In use" : null, p.Id == _current ? "\uE73E" : "\uE9E9", subtitle: p.About, click: () =>
                {
                    _picked = true;
                    SquareEq.Preview(null);
                    _chosen(p);
                    Host.Pop(this);
                });
                if (row != null) row.GotKeyboardFocus += (_, _) => SquareEq.Preview(p); // heard as you move through the list
            }
        }
    }

    public override void OnClosed()
    {
        if (!_picked) SquareEq.Preview(null);
        base.OnClosed();
    }
}

/// <summary>The EQ's curve, drawn: the response from 20 Hz to 20 kHz over a grid, the bands marked.</summary>
public static class EqCurve
{
    public static FrameworkElement Make(SquareEq.Preset p, double width, double height, bool guard, Thickness margin, int selected = -1)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        var back = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(14), Margin = margin, HorizontalAlignment = HorizontalAlignment.Center, Child = canvas };
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        double X(double f) => Math.Log(f / 20) / Math.Log(20000 / 20.0) * width;
        double Y(double db) => height / 2 - db / 15.0 * (height / 2 - 12);
        var subtle = (Brush)Application.Current.FindResource("SubtleBrush");
        foreach (double db in new[] { -12.0, -6, 0, 6, 12 })
        {
            var line = new Line { X1 = 0, X2 = width, Y1 = Y(db), Y2 = Y(db), Stroke = subtle, StrokeThickness = db == 0 ? 1.2 : 0.6, Opacity = db == 0 ? 0.5 : 0.22 };
            canvas.Children.Add(line);
        }
        for (int i = 0; i < SquareEq.Bands.Length; i++)
        {
            double x = X(SquareEq.Bands[i]);
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = subtle, StrokeThickness = i == selected ? 2 : 0.6, Opacity = i == selected ? 0.8 : 0.18 });
            var label = new TextBlock { Text = SquareEq.Bands[i] >= 1000 ? SquareEq.Bands[i] / 1000 + "k" : SquareEq.Bands[i].ToString(), FontSize = 11, Foreground = subtle, Opacity = 0.75 };
            Canvas.SetLeft(label, Math.Clamp(x - 9, 2, width - 24));
            Canvas.SetTop(label, height - 16);
            canvas.Children.Add(label);
        }
        var points = new PointCollection();
        for (double f = 20; f <= 20000; f *= 1.03) points.Add(new Point(X(f), Math.Clamp(Y(SquareEq.Response(p, f, guard)), 2, height - 2)));
        var fill = new PointCollection(points) { new Point(width, height / 2), new Point(0, height / 2) };
        var area = new Polygon { Points = fill, Opacity = 0.18 };
        area.SetResourceReference(Shape.FillProperty, "AccentBrush");
        canvas.Children.Add(area);
        var curve = new Polyline { Points = points, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
        curve.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        canvas.Children.Add(curve);
        return back;
    }
}

/// <summary>
/// Your own EQ: ten bands and four extras (bass, clarity, air, width), each a slider. Left and right choose one, up and
/// down change it, X puts it back to 0, Y makes everything flat, A saves, B leaves. Heard as you change it.
/// </summary>
public sealed class EqEditorScreen : Screen
{
    private static readonly string[] Extras = { "Bass", "Clarity", "Air", "Width" };
    private readonly Action<SquareEq.Preset>? _saved;
    private double[] _gains;
    private double _bass, _clarity, _air;
    private int _width, _column;
    private readonly SquareEq.Preset _start;
    private bool _dirty;
    private readonly Grid _root = new();
    private bool _guard;

    public EqEditorScreen(SquareEq.Preset start, Action<SquareEq.Preset>? saved)
    {
        _start = start;
        _saved = saved;
        _gains = (double[])start.Gains.Clone();
        _bass = start.Bass; _clarity = start.Clarity; _air = start.Air; _width = start.Width;
        Content = _root;
        Focusable = true;
    }

    private SquareEq.Preset Now => _start with { Gains = (double[])_gains.Clone(), Bass = _bass, Clarity = _clarity, Air = _air, Width = _width };

    public override void OnShown()
    {
        _guard = SquareEq.Output.Kind == "speakers" && Labs.Get("eq.guard", "1") == "1";
        Draw();
        SquareEq.Preview(Now);
        Focus();
    }

    public override void FocusDefault() => Focus();

    public override void OnClosed() => SquareEq.Preview(null);

    private void Draw()
    {
        _root.Children.Clear();
        var page = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        page.Children.Add(Ui.Text(_start.Mine ? _start.Name : "Make your own EQ", 30, "FgBrush", bold: true));
        var sub = Ui.Text("Heard as you change it. Starts from " + (_start.Name.Length > 0 ? _start.Name : "your current EQ") + ".", 15, "SubtleBrush");
        sub.Margin = new Thickness(0, 4, 0, 14);
        page.Children.Add(sub);
        page.Children.Add(EqCurve.Make(Now, 1040, 200, _guard, new Thickness(0, 0, 0, 18), _column < 10 ? _column : -1));

        var sliders = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 14; i++)
        {
            bool extra = i >= 10;
            string label = extra ? Extras[i - 10] : SquareEq.Bands[i] >= 1000 ? SquareEq.Bands[i] / 1000 + " kHz" : SquareEq.Bands[i] + " Hz";
            double value = Value(i), min = i == 13 ? -100 : -12, max = i == 13 ? 100 : 12;
            sliders.Children.Add(Column(label, value, min, max, i == _column, i == 13, i == 10));
        }
        page.Children.Add(sliders);
        var hint = new HintBar("Left / right  Choose      Up / down  Change      {Details}  Back to 0      {Options}  All flat      {Select}  Save      {Back}  Leave")
            { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        page.Children.Add(hint);
        _root.Children.Add(page);
    }

    private FrameworkElement Column(string label, double value, double min, double max, bool selected, bool percent, bool gap)
    {
        const double H = 190;
        var column = new StackPanel { Width = 70, Margin = new Thickness(gap ? 22 : 2, 0, 2, 0) };
        var text = Ui.Text(percent ? (value == 0 ? "0" : (value > 0 ? "+" : "") + value + "%") : (value > 0 ? "+" : "") + value.ToString("0.#") + " dB", 13, selected ? "AccentBrush" : "SubtleBrush", bold: selected);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(text);
        var track = new Grid { Height = H, Width = 70, Margin = new Thickness(0, 6, 0, 6) };
        var rail = new Border { Width = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.6 };
        rail.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        track.Children.Add(rail);
        double zeroY = H * max / (max - min), y = H * (max - value) / (max - min);
        var fill = new Border { Width = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                                Margin = new Thickness(0, Math.Min(y, zeroY), 0, 0), Height = Math.Max(2, Math.Abs(zeroY - y)) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Children.Add(fill);
        var knob = new Ellipse { Width = selected ? 30 : 22, Height = selected ? 30 : 22, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                                 Margin = new Thickness(0, y - (selected ? 15 : 11), 0, 0), StrokeThickness = selected ? 3 : 0 };
        knob.SetResourceReference(Shape.FillProperty, selected ? "FgBrush" : "SubtleBrush");
        if (selected) knob.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        track.Children.Add(knob);
        column.Children.Add(track);
        var name = Ui.Text(label, 13, selected ? "FgBrush" : "SubtleBrush", bold: selected);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(name);
        return column;
    }

    private double Value(int i) => i switch { < 10 => _gains[i], 10 => _bass, 11 => _clarity, 12 => _air, _ => _width };

    private void Set(int i, double v)
    {
        switch (i)
        {
            case < 10: _gains[i] = Math.Clamp(v, -12, 12); break;
            case 10: _bass = Math.Clamp(v, -12, 12); break;
            case 11: _clarity = Math.Clamp(v, -12, 12); break;
            case 12: _air = Math.Clamp(v, -12, 12); break;
            default: _width = (int)Math.Clamp(v, -100, 100); break;
        }
        _dirty = true;
        SquareEq.Preview(Now);
        Draw();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Left: _column = (_column + 13) % 14; UiSound.Move(); Draw(); return true;
            case NavAction.Right: _column = (_column + 1) % 14; UiSound.Move(); Draw(); return true;
            case NavAction.Up: Set(_column, Value(_column) + (_column == 13 ? 10 : 0.5)); UiSound.Move(); return true;
            case NavAction.Down: Set(_column, Value(_column) - (_column == 13 ? 10 : 0.5)); UiSound.Move(); return true;
            case NavAction.X: Set(_column, 0); return true;
            case NavAction.Y:
                _gains = new double[10]; _bass = _clarity = _air = 0; _width = 0;
                Set(0, 0);
                return true;
            case NavAction.Accept: _ = SaveAsync(); return true;
            case NavAction.Back: _ = LeaveAsync(); return true;
        }
        return false;
    }

    private async Task SaveAsync()
    {
        string initial = _start.Mine ? _start.Name : "My EQ " + (SquareEq.Mine().Count + 1);
        if (await Host.PromptAsync("Name your EQ", "It joins your presets: choose it for the speakers, headphones, TV or a game.", initial) is not { Length: > 0 } name) return;
        var saved = SquareEq.SaveMine(Now, name.Trim());
        _dirty = false;
        SquareEq.Preview(null);
        _saved?.Invoke(saved);
        Host.Pop(this);
    }

    private async Task LeaveAsync()
    {
        if (_dirty)
        {
            int c = await Host.AskAsync("Save your EQ?", null, "Save", "Leave without saving", "Keep editing");
            if (c == 0) { await SaveAsync(); return; }
            if (c != 1) return;
        }
        SquareEq.Preview(null);
        Host.Pop(this);
    }
}

/// <summary>
/// Experimental "SquareDeck EQ": "Installing necessary plugins". Downloads Equalizer APO's official installer from
/// SourceForge (checked against its published fingerprint), installs it quietly and sets up SquareDeck EQ, all with one
/// Windows permission prompt. <see cref="Result"/> is null when it worked, else what went wrong.
/// </summary>
public sealed class EqInstallScreen : Screen
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<string?> _done = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 12 };
    private readonly Border _track = new() { Height = 12 };
    private readonly TextBlock _percent = Ui.Text("0%", 40, "FgBrush", bold: true);
    private readonly TextBlock _step = Ui.Text("Connecting…", 15, "SubtleBrush");
    private readonly TextBlock _note = Ui.Text("", 14, "SubtleBrush");
    private readonly Button _cancel;
    private readonly System.Windows.Threading.DispatcherTimer _creep = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private double _shown;
    private bool _downloading = true, _started;

    public override bool IsDialog => true;
    public Task<string?> Result => _done.Task;

    public EqInstallScreen()
    {
        Switch2Settings.ApplyColors(this);
        var panel = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 12)), Margin = new Thickness(0, 0, 16, 0), Child = Ui.Glyph("\uE9E9", 26, "BgBrush") };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text("Installing necessary plugins", 22, "FgBrush"));
        titles.Children.Add(Ui.Text("Equalizer APO 1.4.2, for SquareDeck EQ", 15, "SubtleBrush"));
        head.Children.Add(titles);
        panel.Children.Add(head);
        _percent.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(_percent);
        _track.CornerRadius = _fill.CornerRadius = new CornerRadius(6);
        _track.SetResourceReference(Border.BackgroundProperty, "S2Line");
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var bar = new Grid();
        bar.Children.Add(_track);
        bar.Children.Add(_fill);
        panel.Children.Add(bar);
        _step.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_step);
        _note.Margin = new Thickness(0, 8, 0, 0);
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Visibility = Visibility.Collapsed;
        panel.Children.Add(_note);
        var line = new Border { Height = 1, Margin = new Thickness(0, 22, 0, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "S2Line");
        panel.Children.Add(line);
        _cancel = Switch2Pages.SettingRow("Cancel", null, () => { if (_downloading) { _step.Text = "Cancelling…"; _cts.Cancel(); } });
        _cancel.Margin = new Thickness(0);
        panel.Children.Add(_cancel);
        var box = new Border
        {
            Width = 620, Padding = new Thickness(30, 28, 30, 18), CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Switch2Settings.Card),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 }
        };
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;
        // While the installer runs there's nothing to measure: the bar creeps on slowly, never quite reaching the end.
        _creep.Tick += (_, _) => Show(_shown + (0.97 - _shown) * 0.012);
    }

    public override void FocusDefault() => _cancel.Focus();

    /// <summary>The download is 0–60% of the bar, installing and setting up the rest.</summary>
    private void Show(double fraction)
    {
        _shown = Math.Clamp(fraction, 0, 1);
        _percent.Text = $"{Math.Floor(_shown * 100):0}%";
        if (_track.ActualWidth > 0) _fill.Width = _track.ActualWidth * _shown;
    }

    public override async void OnShown()
    {
        if (_started) return;
        _started = true;
        string? problem = null;
        try
        {
            _step.Text = "Downloading Equalizer APO…";
            var progress = new Progress<double>(f => { Show(f * 0.6); _step.Text = $"Downloading Equalizer APO…  {f * 12.0:0.0} of 12 MB"; });
            string installer = await SquareEq.DownloadInstallerAsync(progress, _cts.Token);
            _downloading = false;
            _cancel.Visibility = Visibility.Collapsed;
            Show(0.62);
            _step.Text = "Installing…  Windows will ask for permission once";
            _note.Text = "Equalizer APO's Device Selector opens on the way: make sure the Speakers are ticked (and your headphones, if they're connected), then press OK.";
            _note.Visibility = Visibility.Visible;
            _creep.Start();
            problem = await SquareEq.InstallAsync(installer);
            _creep.Stop();
            if (problem == null)
            {
                Show(1);
                _step.Text = "Done. SquareDeck EQ is ready.";
                _note.Visibility = Visibility.Collapsed;
                UiSound.Select();
                await Task.Delay(1400);
            }
        }
        catch (OperationCanceledException) { problem = "cancelled"; }
        catch (Exception ex) { problem = ex.Message; }
        _creep.Stop();
        Host.Pop(this);
        _done.TrySetResult(problem);
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { if (_downloading) { _step.Text = "Cancelling…"; _cts.Cancel(); } return true; }
        return action != NavAction.Accept;
    }

    public override void OnClosed()
    {
        if (_downloading) _cts.Cancel();
        _creep.Stop();
    }
}
