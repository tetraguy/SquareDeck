using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Box art that loads only for tiles near the screen (and lets go of ones scrolled far away).</summary>
internal sealed class RetroArtWall
{
    private sealed class Item { public required FrameworkElement Box; public required Image Image; public required RetroGame Game; public bool Started; }

    private readonly List<Item> _items = new();
    private readonly System.Windows.Threading.DispatcherTimer _soon = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private CancellationTokenSource _cts = new();
    private readonly Func<ScrollViewer?> _scroll;

    public RetroArtWall(Func<ScrollViewer?> scroll)
    {
        _scroll = scroll;
        _soon.Tick += (_, _) => Realize();
    }

    public void Add(FrameworkElement box, Image image, RetroGame game) => _items.Add(new Item { Box = box, Image = image, Game = game });

    public void Clear()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _items.Clear();
    }

    public void Soon() { _soon.Stop(); _soon.Start(); }

    public void Stop() { _soon.Stop(); _cts.Cancel(); }

    private void Realize()
    {
        _soon.Stop();
        if (_scroll() is not { } scroll || _items.Count == 0) return;
        double vw = scroll.ViewportWidth, vh = scroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;
        var near = new Rect(-vw, -vh, vw * 3, vh * 3);
        var far = new Rect(-vw * 2, -vh * 4, vw * 5, vh * 9);
        var ct = _cts.Token;
        foreach (var t in _items)
        {
            Rect r;
            try
            {
                if (!t.Box.IsVisible) continue;
                r = t.Box.TransformToAncestor(scroll).TransformBounds(new Rect(t.Box.RenderSize));
            }
            catch (InvalidOperationException) { continue; }
            if (r.IntersectsWith(near))
            {
                if (t.Started) continue;
                t.Started = true;
                var item = t;
                _ = Load(item, ct);
            }
            else if (t.Started && !r.IntersectsWith(far))
            {
                t.Image.Source = null;
                t.Started = false;
            }
        }
    }

    private static async Task Load(Item t, CancellationToken ct)
    {
        try
        {
            var art = await RetroLounge.ArtAsync(t.Game, "Named_Boxarts", 300, ct);
            if (art == null || ct.IsCancellationRequested) return;
            t.Image.Source = art;
            // Box art arrived: the title written on the tile steps back.
            if (t.Image.Parent is Grid g) foreach (var label in g.Children.OfType<TextBlock>().Where(x => x.Tag as string == "title")) label.Visibility = Visibility.Collapsed;
            if (!Theme.ReduceMotion) t.Image.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        catch (Exception) { }
    }
}

/// <summary>Retro Lounge's shared pieces: console colors, tiles, chips and cards.</summary>
internal static class RetroUi
{
    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static LinearGradientBrush SystemBrush(RetroSystem s, double angle = 1)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(angle, 1) };
        b.GradientStops.Add(new GradientStop(Hex(s.Color1), 0));
        b.GradientStops.Add(new GradientStop(Hex(s.Color2), 1));
        b.Freeze();
        return b;
    }

    public static ImageSource? Logo(int size)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "RetroLounge", size > 256 ? "logo-512.png" : "logo.png");
        return File.Exists(file) ? ImageLoader.FromFile(file, size) : null;
    }

    public static Border Badge(string text, HorizontalAlignment h, VerticalAlignment v, bool accent, double margin = 8)
    {
        var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(margin), HorizontalAlignment = h, VerticalAlignment = v,
            Child = Ui.Text(text, 12, accent ? "BgBrush" : "FgBrush", bold: true) };
        if (accent) badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else badge.Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x0C, 0x16));
        return badge;
    }

    /// <summary>A game's tile: its box art over the console's colors (the title written on it until the art arrives).</summary>
    public static Border GameBox(RetroGame g, RetroArtWall wall, double width = 150, double height = 200)
    {
        var box = GridScreen.ImageBox(width, height, out var image);
        image.Stretch = Stretch.Uniform;
        var grid = (Grid)box.Child;
        var system = RetroLounge.SystemOf(g.System);
        if (system != null) grid.Children.Insert(0, new Border { Background = SystemBrush(system), Opacity = 0.9 });
        grid.Children.Insert(1, new Border { Background = RetroIntro.Scanlines(), Opacity = 0.35 });
        var title = new TextBlock
        {
            Text = g.Title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12), Tag = "title"
        };
        grid.Children.Insert(2, title);
        image.Margin = new Thickness(6);
        if (system != null) grid.Children.Add(Badge(system.Short, HorizontalAlignment.Left, VerticalAlignment.Bottom, accent: false, margin: 6));
        var state = RetroLounge.PeekState(g);
        if (state?.Favorite == true) grid.Children.Add(Badge("★", HorizontalAlignment.Right, VerticalAlignment.Top, accent: true, margin: 6));
        if (g.Discs.Count > 1) grid.Children.Add(Badge(g.Discs.Count + " discs", HorizontalAlignment.Right, VerticalAlignment.Bottom, accent: false, margin: 6));
        wall.Add(box, image, g);
        return box;
    }

    /// <summary>A console's tile: its colors, its short name large, how many games, and whether it can play yet.</summary>
    public static Border SystemBox(RetroSystem s, int count, double width = 220, double height = 150)
    {
        double corner = LookChrome.TileCorner(Math.Max(Theme.TileCorner, 12));
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new Border { Background = SystemBrush(s), CornerRadius = new CornerRadius(corner) });
        grid.Children.Add(new Border { Background = RetroIntro.Scanlines(), CornerRadius = new CornerRadius(corner), Opacity = 0.4 });
        // A big faint watermark of the short name, like a cartridge label.
        grid.Children.Add(new TextBlock { Text = s.Short, FontSize = Math.Min(96, width / Math.Max(2.2, s.Short.Length * 0.62)), FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, -14) });
        var words = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        words.Children.Add(new TextBlock { Text = s.Short, FontSize = 30, FontWeight = FontWeights.Black, Foreground = Brushes.White });
        words.Children.Add(new TextBlock { Text = s.Name, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(words);
        if (count > 0) grid.Children.Add(Badge(count == 1 ? "1 game" : count + " games", HorizontalAlignment.Left, VerticalAlignment.Bottom, accent: true, margin: 12));
        bool ready = s.Players.Any(RetroLounge.Ready);
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(ready ? Color.FromRgb(0x4C, 0xE0, 0x7A) : Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), ToolTip = ready ? "Ready to play" : "No emulator yet" };
        grid.Children.Add(dot);
        var box = new Border { Child = grid, CornerRadius = new CornerRadius(corner), ClipToBounds = true };
        box.Clip = new RectangleGeometry(new Rect(0, 0, width, height), corner, corner);
        return box;
    }

    public static Border Card(UIElement child, Thickness padding, Thickness margin)
    {
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = padding, Margin = margin, Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return card;
    }

    public static Border Pill(string text, Brush? background = null)
    {
        var p = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 8, 8), Child = Ui.Text(text, 13, "FgBrush", bold: true) };
        if (background != null) { p.Background = background; ((TextBlock)p.Child).Foreground = Brushes.White; }
        else p.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        return p;
    }

    public static string Played(RetroGameState? s)
    {
        if (s == null || s.Plays == 0) return "Not played yet";
        string time = s.Minutes >= 60 ? $"{s.Minutes / 60:0.#} hours" : s.Minutes >= 1 ? $"{s.Minutes:0} minutes" : "a moment";
        return $"Played {time}" + (s.LastPlayed is DateTime d ? "  ·  last " + Ui.Ago(d) : "");
    }

    /// <summary>Plays a game, downloading a RetroArch core first if it needs one (with the busy card up).</summary>
    public static async Task PlayAsync(MainWindow host, RetroGame game, string? player = null)
    {
        var system = RetroLounge.SystemOf(game.System);
        if (system == null) return;
        player ??= RetroLounge.PlayerFor(system, game);
        if (!RetroLounge.Ready(player))
        {
            var need = RetroLounge.Emulator(player.StartsWith("ra:") ? "retroarch" : player);
            if (need == null) return;
            if (await host.AskAsync(need.Name + " isn't installed", $"{system.Name} games play with {need.Name}. Get it now? It downloads from {need.Name}'s own project and is ready in a minute.", "Not now", "Get " + need.Name) == 1)
            {
                await RetroDownloads.StartNewestAsync(need, host);
                host.Push(new RetroEmulatorScreen(need));
            }
            return;
        }
        if (system.BiosRequired && !RetroLounge.BiosOk(system))
        {
            int pick = await host.AskAsync("A BIOS file is missing", $"{system.Name} games need {string.Join(", ", RetroLounge.CheckBios(system).Where(b => !b.Found).Select(b => b.Label))} in your BIOS folder ({RetroLounge.BiosFolder}). {system.BiosNote}",
                "Play anyway", "Open the BIOS folder", "Cancel");
            if (pick == 1) { Directory.CreateDirectory(RetroLounge.BiosFolder); host.Push(new ExplorerScreen(RetroLounge.BiosFolder)); return; }
            if (pick != 0) return;
        }
        try
        {
            if (player.StartsWith("ra:") && RetroLounge.InstalledFor("retroarch") is { } ra && !File.Exists(RetroLounge.CoreFile(ra, player[3..])))
                await host.RunBusyAsync("Getting RetroArch's " + RetroLounge.CoreName(player[3..]) + " core…", () => RetroLounge.PlayAsync(game, player));
            else await RetroLounge.PlayAsync(game, player);
        }
        catch (Exception ex) { await host.AskAsync("Couldn't start it", ex.Message, "OK"); }
    }
}

/// <summary>
/// Retro Lounge: emulators and your own games, in one place. A sidebar (as Recomp Zone has): Home (keep playing,
/// favorites, new arrivals), Consoles, Games (every game, filtered and sorted), Emulators (download, update, set up),
/// BIOS (what each console needs and what's there), Downloads and Setup (ROM folders and preferences).
/// </summary>
public sealed class RetroLoungeScreen : GridScreen
{
    private static readonly (string Id, string Name, string Glyph)[] Sections =
    {
        ("home", "Home", ""), ("systems", "Consoles", ""), ("games", "Games", ""),
        ("emulators", "Emulators", ""), ("bios", "BIOS", ""), ("downloads", "Downloads", ""), ("setup", "Setup", ""),
    };

    private string _section = "home";
    private string? _pendingSection, _focusId;
    private readonly System.Windows.Threading.DispatcherTimer _switchSoon = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly StackPanel _nav = new();
    private bool _compact;
    private bool? _navCollapsedBuilt;
    private RetroIntro? _intro;

    /// <summary>Retro Lounge's music may play: its opening has reached the end (the launcher's music fades out).</summary>
    public bool MusicReady { get; private set; }
    private readonly CancellationTokenSource _life = new();
    private readonly RetroArtWall _wall;
    private bool _built, _scanning;
    private string _search = "", _sort = "name", _system = "";
    private bool _favorites;
    private List<string> _found = new();

    public RetroLoungeScreen() : base("Retro Lounge", "{Select}  Open        {LB}{RB}  Sections        {Options}  Search        {Details}  Filter and sort        {Back}  Back")
    {
        _wall = new RetroArtWall(() => Body.Parent as ScrollViewer);
        if (Body.Parent is ScrollViewer scroll)
        {
            scroll.ScrollChanged += (_, _) => _wall.Soon();
            if (scroll.Parent is Grid host)
            {
                host.Children.Remove(scroll);
                var layout = new Grid();
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition());
                layout.Children.Add(BuildNavPanel());
                Grid.SetColumn(scroll, 1);
                layout.Children.Add(scroll);
                host.Children.Add(layout);
            }
        }
        Body.SizeChanged += (_, _) => _wall.Soon();
        SizeChanged += (_, _) => { bool compact = ActualWidth < 1450; if (compact != _compact) { _compact = compact; UpdateNavShape(); } };
        _switchSoon.Tick += (_, _) => { _switchSoon.Stop(); if (_pendingSection != null) ShowSection(_pendingSection, focusContent: false); };
        RetroDownloads.Changed += OnDownloadsChanged;
        RetroDownloads.Progressed += OnDownloadProgress;
        RetroLounge.Played += OnPlayed;
    }

    /// <summary>Opening: the TV switches on while the ROM folders are looked through, then fades into Home.</summary>
    public override async void OnShown()
    {
        var intro = new RetroIntro();
        _intro = intro;
        ShowOver(intro);
        intro.Focus();
        var minimum = Task.Delay(RetroIntro.Length);
        var ct = _life.Token;
        try
        {
            intro.Report("Plugging in the consoles", RetroLounge.Config.Roots.Count == 0 ? "No ROM folder yet" : string.Join("  ·  ", RetroLounge.Config.Roots.Take(2)), 0.08);
            if (RetroLounge.Config.Roots.Count > 0)
            {
                _scanning = true;
                var progress = new Progress<(string System, int Count)>(p => intro.Report("Blowing on the cartridges", $"{p.System}  ·  {p.Count} games so far", Math.Min(0.85, 0.1 + p.Count / 2000.0 + 0.02)));
                await RetroLounge.ScanAsync(progress, ct);
                _scanning = false;
            }
            else _found = await Task.Run(RetroLounge.FindExistingRomFolders, ct);
            intro.Report("Warming up the tube", $"{RetroLounge.Visible().Count} games  ·  {RetroLounge.Installed().Count} emulators", 0.92);
            if (ct.IsCancellationRequested) return;
            Build(focus: false);
            await minimum;
            // Retro Lounge's music comes in as the opening finishes (the launcher's fades out).
            MusicReady = true;
            Host.RefreshBackgroundPlayback();
            await intro.FinishAsync();
            _ = RetroDownloads.CheckAsync();
        }
        catch (Exception) { }
        finally
        {
            _scanning = false;
            if (!MusicReady && !ct.IsCancellationRequested) { MusicReady = true; Host.RefreshBackgroundPlayback(); }
            if (ReferenceEquals(_intro, intro))
            {
                (intro.Parent as Panel)?.Children.Remove(intro);
                _intro = null;
                if (!ct.IsCancellationRequested)
                {
                    if (!_built) Build(focus: true);
                    else FocusDefault();
                }
            }
        }
    }

    public override void FocusDefault()
    {
        if (_intro != null) { _intro.Focus(); return; }
        var target = (_focusId != null ? ButtonTagged(Body, _focusId) : null) ?? FirstButton(Body);
        if (target != null) target.Focus();
        else NavButton(_section)?.Focus();
    }

    public override void OnResumed()
    {
        if (_intro != null) return;
        BuildNav();
        // Back from a game, a console or an emulator: what was played, installed or changed shows straight away.
        KeepFocus(() => Build(focus: !_nav.IsKeyboardFocusWithin));
    }

    public override void OnClosed()
    {
        _life.Cancel();
        _wall.Stop();
        _switchSoon.Stop();
        RetroDownloads.Changed -= OnDownloadsChanged;
        RetroDownloads.Progressed -= OnDownloadProgress;
        RetroLounge.Played -= OnPlayed;
        RetroLounge.ForgetArt();
    }

    private void OnPlayed() { if (_section == "home" && Host?.IsTop(this) == true) KeepFocus(() => Build(focus: false)); }

    public override bool OnAction(NavAction action)
    {
        if (_intro != null) return action != NavAction.Back;
        switch (action)
        {
            case NavAction.LB or NavAction.RB:
            {
                int i = Array.FindIndex(Sections, x => x.Id == _section);
                i = (i + (action == NavAction.RB ? 1 : -1) + Sections.Length) % Sections.Length;
                ShowSection(Sections[i].Id, focusContent: true);
                return true;
            }
            case NavAction.Y:
                _ = SearchAsync();
                return true;
            case NavAction.X:
                if (_section != "games") ShowSection("games", focusContent: true);
                _ = FilterMenuAsync();
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- the sidebar

    private bool NavCollapsed => _compact && !_nav.IsKeyboardFocusWithin;

    private FrameworkElement BuildNavPanel()
    {
        var box = new Border { Child = _nav, Padding = new Thickness(8), CornerRadius = new CornerRadius(18), VerticalAlignment = VerticalAlignment.Top };
        var tint = new Border { CornerRadius = new CornerRadius(18), Opacity = 0.55 };
        tint.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var holder = new Grid { Margin = new Thickness(0, 0, 24, 12), VerticalAlignment = VerticalAlignment.Top };
        holder.Children.Add(tint);
        holder.Children.Add(box);
        _nav.IsKeyboardFocusWithinChanged += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(UpdateNavShape));
        BuildNav();
        return holder;
    }

    private void UpdateNavShape() { if (_navCollapsedBuilt != NavCollapsed) BuildNav(); }

    private Button? NavButton(string id) => _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == "nav:" + id);

    private void BuildNav()
    {
        string? focused = _nav.Children.OfType<Button>().FirstOrDefault(b => b.IsKeyboardFocused)?.DataContext as string;
        bool collapsed = NavCollapsed;
        _navCollapsedBuilt = collapsed;
        _nav.Children.Clear();
        int badge = RetroDownloads.Badge;
        foreach (var (id, name, glyph) in Sections)
        {
            int count = id == "downloads" ? badge : 0;
            Button b;
            if (collapsed)
            {
                b = Ui.BladeButton(glyph, name, selected: id == _section);
                b.Margin = new Thickness(0, 2, 0, 2);
            }
            else
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = Ui.Glyph(glyph, 18);
                icon.ClearValue(TextBlock.ForegroundProperty);
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(icon);
                var label = new TextBlock { Text = name, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (count > 0)
                {
                    var pill = new Border { CornerRadius = new CornerRadius(11), MinWidth = 22, Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(10, 0, 0, 0),
                        Child = new TextBlock { Text = count.ToString(), FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center } };
                    pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    ((TextBlock)pill.Child).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                    Grid.SetColumn(pill, 2);
                    row.Children.Add(pill);
                }
                b = new Button { Style = Ui.StyleOf("RowButton"), Content = row, Width = 228, Margin = new Thickness(0, 2, 0, 2) };
                if (id == _section) b.Tag = "selected";
            }
            b.DataContext = "nav:" + id;
            string target = id;
            b.GotKeyboardFocus += (_, _) => { _pendingSection = target; _switchSoon.Stop(); _switchSoon.Start(); };
            b.Click += (_, _) => ShowSection(target, focusContent: true);
            _nav.Children.Add(b);
        }
        if (focused != null) _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == focused)?.Focus();
    }

    private void ShowSection(string id, bool focusContent)
    {
        _switchSoon.Stop();
        if (_intro != null) return;
        if (id == _section) { if (focusContent) FocusDefault(); return; }
        _section = id;
        _focusId = null;
        if (Body.Parent is ScrollViewer scroll) scroll.ScrollToTop();
        BuildNav();
        Build(focus: focusContent);
    }

    private void KeepFocus(Action rebuild)
    {
        if (Keyboard.FocusedElement is Button { Tag: string id } && !_nav.IsKeyboardFocusWithin) _focusId = id;
        rebuild();
    }

    private static Button? FirstButton(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { IsEnabled: true, Focusable: true, Visibility: Visibility.Visible } b) return b;
            if (child is UIElement { Visibility: not Visibility.Visible }) continue;
            if (FirstButton(child) is { } inner) return inner;
        }
        return null;
    }

    private static Button? ButtonTagged(DependencyObject root, string tag)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Tag: string t } b && t == tag) return b;
            if (ButtonTagged(child, tag) is { } inner) return inner;
        }
        return null;
    }

    private void OnDownloadsChanged()
    {
        BuildNav();
        if (_intro == null && _built && _section is "downloads" or "emulators" or "systems") KeepFocus(() => Build(focus: Keyboard.FocusedElement is UIElement f && Body.IsAncestorOf(f)));
    }

    private readonly Dictionary<string, (TextBlock Text, Border Fill)> _progressViews = new();

    private void OnDownloadProgress(RetroDownload d)
    {
        if (!_progressViews.TryGetValue(d.Emulator.Id, out var v)) return;
        v.Text.Text = d.Status;
        v.Fill.Width = Math.Max(0, (v.Fill.Parent as FrameworkElement)?.ActualWidth ?? 0) * d.Fraction;
    }

    // ---------------------------------------------------------------- building

    private void Note(string text, double size = 16)
    {
        var t = Ui.Text(text, size, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 0, 10, 14);
        Body.Children.Add(t);
    }

    private void Heading(string text, string? side = null)
    {
        var row = new DockPanel { Margin = new Thickness(4, 12, 10, 10), LastChildFill = false };
        row.Children.Add(Ui.Text(text, 20, "FgBrush", bold: true));
        if (side != null)
        {
            var s = Ui.Text(side, 14, "SubtleBrush");
            s.VerticalAlignment = VerticalAlignment.Bottom;
            s.Margin = new Thickness(12, 0, 0, 2);
            row.Children.Add(s);
        }
        Body.Children.Add(row);
    }

    private Button Chip(Panel bar, string glyph, string label, Action click, string? tag = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 16, 8) };
        var g = Ui.Glyph(glyph, 16, "AccentBrush");
        g.Margin = new Thickness(0, 0, 8, 0);
        g.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(g);
        var t = Ui.Text(label, 15, "FgBrush", bold: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        var b = Ui.Tile(row);
        b.Margin = new Thickness(0, 0, 10, 10);
        b.Tag = tag ?? "chip:" + label;
        b.Click += (_, _) => click();
        bar.Children.Add(b);
        return b;
    }

    private Panel Shelf(string title, string? side = null)
    {
        Heading(title, side);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer { Content = panel, Focusable = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly, Margin = new Thickness(0, 0, 0, 6) };
        scroll.ScrollChanged += (_, _) => _wall.Soon();
        Body.Children.Add(scroll);
        return panel;
    }

    private Button GameTile(Panel panel, RetroGame g)
    {
        var box = RetroUi.GameBox(g, _wall);
        var tile = AddTile(box, g.Title, 150, () => { _focusId = "g:" + g.Key; Host.Push(new RetroGameScreen(g)); }, "g:" + g.Key, panel);
        tile.ToolTip = g.Name;
        return tile;
    }

    private Button SystemTile(Panel panel, RetroSystem s, int count) =>
        AddTile(RetroUi.SystemBox(s, count), null, 220, () => { _focusId = "s:" + s.Id; Host.Push(new RetroSystemScreen(s)); }, "s:" + s.Id, panel);

    private void Build(bool focus)
    {
        _built = true;
        _wall.Clear();
        _progressViews.Clear();
        Body.Children.Clear();
        BuildNav();
        switch (_section)
        {
            case "home": BuildHome(); break;
            case "systems": BuildSystems(); break;
            case "games": BuildGames(); break;
            case "emulators": BuildEmulators(); break;
            case "bios": BuildBios(); break;
            case "downloads": BuildDownloads(); break;
            case "setup": BuildSetup(); break;
        }
        _wall.Soon();
        if (focus) FocusLater((_focusId != null ? ButtonTagged(Body, _focusId) : null) ?? FirstButton(Body));
    }

    // ---------------------------------------------------------------- Home

    private void BuildHome()
    {
        var all = RetroLounge.Visible();
        if (RetroLounge.Config.Roots.Count == 0 || all.Count == 0) { BuildWelcome(all.Count == 0 && RetroLounge.Config.Roots.Count > 0); return; }

        var played = all.Select(g => (Game: g, State: RetroLounge.PeekState(g))).Where(x => x.State?.LastPlayed != null).OrderByDescending(x => x.State!.LastPlayed).Select(x => x.Game).ToList();
        var hero = played.FirstOrDefault() ?? all.Where(g => RetroLounge.PeekState(g)?.Favorite == true).FirstOrDefault() ?? all.OrderByDescending(g => g.Added).First();
        Body.Children.Add(BuildHero(hero, played.Count > 0));

        if (played.Count > 0) { var shelf = Shelf("Keep playing"); foreach (var g in played.Take(15)) GameTile(shelf, g); }
        var favorites = all.Where(g => RetroLounge.PeekState(g)?.Favorite == true).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
        if (favorites.Count > 0) { var shelf = Shelf("Favorites", favorites.Count.ToString()); foreach (var g in favorites.Take(20)) GameTile(shelf, g); }
        var added = all.OrderByDescending(g => g.Added).Take(15).ToList();
        if (added.Count > 0) { var shelf = Shelf("Just added"); foreach (var g in added) GameTile(shelf, g); }

        // Your consoles, then a row per console with the most games.
        var counts = all.GroupBy(g => g.System).ToDictionary(x => x.Key, x => x.Count());
        var consoles = RetroLounge.Systems.Where(s => counts.ContainsKey(s.Id)).OrderByDescending(s => counts[s.Id]).ToList();
        var row = Shelf("Your consoles", consoles.Count.ToString());
        foreach (var s in consoles) SystemTile(row, s, counts[s.Id]);
        foreach (var s in consoles.Take(4))
        {
            var shelf = Shelf(s.Name, counts[s.Id] + " games");
            foreach (var g in all.Where(g => g.System == s.Id).OrderBy(_ => Random.Shared.Next()).Take(12)) GameTile(shelf, g);
        }
        var missing = consoles.Select(RetroLounge.MissingFor).Where(e => e != null).Distinct().ToList();
        if (missing.Count > 0)
        {
            Heading("Get set up");
            var bar = new WrapPanel();
            Body.Children.Add(bar);
            if (missing.Count > 1) Chip(bar, "", $"Get all {missing.Count}", () => _ = GetAllAsync(missing!), "need:all");
            foreach (var emu in missing.Take(6)) Chip(bar, "", "Get " + emu!.Name, () => Host.Push(new RetroEmulatorScreen(emu)), "need:" + emu.Id);
        }
    }

    private FrameworkElement BuildHero(RetroGame game, bool resume)
    {
        var system = RetroLounge.SystemOf(game.System);
        var panel = new Grid { Height = 340, Margin = new Thickness(0, 4, 10, 18) };
        var frame = new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true };
        var layers = new Grid();
        frame.Child = layers;
        panel.SizeChanged += (_, e) => layers.Clip = new RectangleGeometry(new Rect(e.NewSize), 22, 22);
        if (system != null) layers.Children.Add(new Border { Background = RetroUi.SystemBrush(system, 0.4) });
        var snap = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Right };
        RenderOptions.SetBitmapScalingMode(snap, BitmapScalingMode.NearestNeighbor); // crisp pixels, as the game drew them
        layers.Children.Add(snap);
        var left = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0x0B, 0x07, 0x12), 0));
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 0x0B, 0x07, 0x12), 0.45));
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0x0B, 0x07, 0x12), 0.85));
        layers.Children.Add(new Border { Background = left });
        layers.Children.Add(new Border { Background = RetroIntro.Scanlines(), Opacity = 0.5 });
        panel.Children.Add(frame);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(36, 0, 36, 0), VerticalAlignment = VerticalAlignment.Center };
        var poster = GridScreen.ImageBox(190, 250, out var cover);
        cover.Stretch = Stretch.Uniform;
        poster.Margin = new Thickness(0, 0, 30, 0);
        if (system != null) poster.Background = RetroUi.SystemBrush(system);
        poster.Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 4, Opacity = 0.55 };
        row.Children.Add(poster);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 620 };
        var kicker = new TextBlock { Text = (resume ? "KEEP PLAYING" : "PICK UP SOMETHING") + (system != null ? "  ·  " + system.Name.ToUpperInvariant() : ""), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = RetroIntro.Sunset(vertical: false), Margin = new Thickness(0, 0, 0, 10) };
        words.Children.Add(kicker);
        words.Children.Add(new TextBlock { Text = game.Title, FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string>();
        if (game.Region.Length > 0) facts.Add(game.Region);
        facts.Add(RetroUi.Played(RetroLounge.PeekState(game)));
        if (system != null) facts.Add("Plays with " + RetroLounge.PlayerName(RetroLounge.PlayerFor(system, game)));
        words.Children.Add(new TextBlock { Text = string.Join("   ·   ", facts), FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 10, 0, 22), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var play = new Button { Style = Ui.StyleOf("AccentButton"), Content = Label("", resume ? "Continue" : "Play"), Margin = new Thickness(-3, 0, 10, 0), Tag = "hero:play" };
        play.Click += (_, _) => _ = RetroUi.PlayAsync(Host, game);
        buttons.Children.Add(play);
        var info = new Button { Style = Ui.StyleOf("PillButton"), Content = Label("", "Details"), VerticalAlignment = VerticalAlignment.Center, Tag = "hero:info" };
        info.Click += (_, _) => Host.Push(new RetroGameScreen(game));
        buttons.Children.Add(info);
        words.Children.Add(buttons);
        row.Children.Add(words);
        panel.Children.Add(row);

        _ = Fill();
        return panel;

        async Task Fill()
        {
            var art = await RetroLounge.ArtAsync(game, "Named_Boxarts", 400);
            if (art != null) cover.Source = art;
            var shot = await RetroLounge.ArtAsync(game, "Named_Snaps", 960) ?? await RetroLounge.ArtAsync(game, "Named_Titles", 960);
            if (shot == null) return;
            snap.Source = shot;
            if (Theme.ReduceMotion) snap.Opacity = 0.85;
            else snap.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(500)));
        }
    }

    private static StackPanel Label(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(glyph, 15);
        g.ClearValue(TextBlock.ForegroundProperty);
        g.Margin = new Thickness(0, 1, 10, 0);
        row.Children.Add(g);
        row.Children.Add(new TextBlock { Text = text, FontSize = 17 });
        return row;
    }

    /// <summary>First visit (or no games found): what Retro Lounge is, and three steps to get going.</summary>
    private void BuildWelcome(bool emptyFolders)
    {
        var card = new Grid { Margin = new Thickness(0, 4, 10, 18), MinHeight = 300 };
        var bg = new Border { CornerRadius = new CornerRadius(22) };
        var glow = new RadialGradientBrush { Center = new Point(0.25, 0.3), GradientOrigin = new Point(0.25, 0.3), RadiusX = 0.9, RadiusY = 1.2 };
        glow.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x1A, 0x55), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x0B, 0x20), 1));
        bg.Background = glow;
        card.Children.Add(bg);
        card.Children.Add(new Border { CornerRadius = new CornerRadius(22), Background = RetroIntro.Scanlines(), Opacity = 0.45 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(36, 30, 36, 30) };
        if (RetroUi.Logo(256) is { } logo) row.Children.Add(new Image { Source = logo, Width = 180, Height = 180, Margin = new Thickness(0, 0, 32, 0), VerticalAlignment = VerticalAlignment.Center });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 640 };
        words.Children.Add(new TextBlock { Text = "WELCOME TO", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = RetroIntro.Sunset(vertical: false) });
        words.Children.Add(new TextBlock { Text = "Retro Lounge", FontSize = 44, FontWeight = FontWeights.Black, Foreground = Brushes.White });
        words.Children.Add(new TextBlock
        {
            Text = emptyFolders ? "Your ROM folder is set, but no games turned up yet. Put each console's games in its own folder (snes, psx, gba…), then look again."
                : "Your classic games and the emulators that play them, all in one place. Point it at your ROM folder, get the emulators your consoles need with one press, and play.",
            FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });
        row.Children.Add(words);
        card.Children.Add(row);
        Body.Children.Add(card);

        Heading("1  ·  Your games");
        var bar = new WrapPanel();
        Body.Children.Add(bar);
        foreach (var path in _found)
            Chip(bar, "", "Use " + path, () => { RetroLounge.Config.Roots.Add(path); RetroLounge.SaveConfig(); _ = RescanAsync(); }, "found:" + path);
        Chip(bar, "", RetroLounge.Config.Roots.Count == 0 ? "Choose my ROM folder" : "Add another ROM folder", () => _ = AddRootAsync(), "root:add");
        Chip(bar, "", "Make a Retro Lounge folder", () => _ = MakeFoldersAsync(), "root:make");
        if (RetroLounge.Config.Roots.Count > 0) Chip(bar, "", "Look again", () => _ = RescanAsync(), "root:scan");
        Note("A folder with one folder per console inside (snes, n64, psx, ps2…), as EmuDeck, RetroBat and ES-DE make them. \"Make a Retro Lounge folder\" makes one for you, with a BIOS folder beside it.");

        Heading("2  ·  Emulators");
        Note("Retro Lounge suggests the right emulator for each console you have, downloads it from its own project and sets it up to keep its settings and saves in one place.");
        var emus = new WrapPanel();
        Body.Children.Add(emus);
        foreach (var id in new[] { "retroarch", "duckstation", "pcsx2", "dolphin", "ppsspp" })
            if (RetroLounge.Emulator(id) is { } emu) Chip(emus, RetroLounge.InstalledFor(id) != null ? "" : "", emu.Name, () => Host.Push(new RetroEmulatorScreen(emu)), "emu:" + id);

        Heading("3  ·  BIOS");
        Note("A few consoles (PlayStation, PS2, Saturn, Sega CD…) need a BIOS file from your own console. The BIOS section says which, and where they go. Retro Lounge never downloads games or BIOS files.");
    }

    // ---------------------------------------------------------------- Consoles

    private void BuildSystems()
    {
        var all = RetroLounge.Visible();
        var counts = all.GroupBy(g => g.System).ToDictionary(x => x.Key, x => x.Count());
        var yours = RetroLounge.Systems.Where(s => counts.ContainsKey(s.Id)).OrderBy(s => s.Maker).ThenBy(s => s.Year).ToList();
        if (yours.Count > 0)
        {
            Heading("Your consoles", $"{yours.Count} consoles  ·  {all.Count} games");
            var wall = new WrapPanel();
            Body.Children.Add(wall);
            foreach (var s in yours) SystemTile(wall, s, counts[s.Id]);
        }
        foreach (var maker in RetroLounge.Systems.Where(s => !counts.ContainsKey(s.Id)).GroupBy(s => s.Maker is "Nintendo" or "Sony" or "Sega" or "Microsoft" ? s.Maker : "More"))
        {
            Heading(yours.Count == 0 ? maker.Key : "More from " + maker.Key);
            var wall = new WrapPanel();
            Body.Children.Add(wall);
            foreach (var s in maker.OrderBy(s => s.Year)) SystemTile(wall, s, 0);
        }
        Note("The green dot means an emulator for it is ready. Open a console to choose which emulator plays it.");
    }

    // ---------------------------------------------------------------- Games

    private List<RetroGame> Filtered()
    {
        var games = RetroLounge.Visible();
        if (_system.Length > 0) games = games.Where(g => g.System == _system).ToList();
        if (_favorites) games = games.Where(g => RetroLounge.PeekState(g)?.Favorite == true).ToList();
        if (_search.Length > 0) games = games.Where(g => (g.Name + " " + (RetroLounge.SystemOf(g.System)?.Name ?? "")).Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
        return _sort switch
        {
            "recent" => games.OrderByDescending(g => RetroLounge.PeekState(g)?.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Title).ToList(),
            "most" => games.OrderByDescending(g => RetroLounge.PeekState(g)?.Minutes ?? 0).ThenBy(g => g.Title).ToList(),
            "added" => games.OrderByDescending(g => g.Added).ToList(),
            "system" => games.OrderBy(g => RetroLounge.SystemOf(g.System)?.Name).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static readonly (string Id, string Name)[] Sorts = { ("name", "A to Z"), ("system", "By console"), ("recent", "Recently played"), ("most", "Most played"), ("added", "Recently added") };

    private void BuildGames()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        Body.Children.Add(bar);
        Chip(bar, "", "Console: " + (_system.Length == 0 ? "All" : RetroLounge.SystemOf(_system)?.Short ?? _system), () => _ = PickSystemAsync(), "chip:system");
        Chip(bar, "", "Sort: " + Sorts.First(s => s.Id == _sort).Name, () => _ = PickSortAsync(), "chip:sort");
        Chip(bar, _favorites ? "" : "", _favorites ? "Favorites only" : "All games", () => { _favorites = !_favorites; Build(focus: true); }, "chip:fav");
        Chip(bar, "", _search.Length > 0 ? $"\"{_search}\"" : "Search", () => _ = SearchAsync(), "chip:search");
        if (_system.Length > 0 || _favorites || _search.Length > 0) Chip(bar, "", "Clear", () => { _system = _search = ""; _favorites = false; Build(focus: true); }, "chip:clear");

        var games = Filtered();
        if (games.Count == 0)
        {
            Note(RetroLounge.Games.Count == 0 ? "No games yet. Setup has your ROM folders." : "Nothing matches. Press " + Hints.KeyLabel("Details") + " to change the filters.");
            return;
        }
        Note($"{games.Count} {(games.Count == 1 ? "game" : "games")}" + (RetroLounge.Config.OnePerGame ? "  ·  one copy per game, your region first (Setup)" : ""));
        if (_sort == "system")
            foreach (var group in games.GroupBy(g => g.System))
            {
                Heading(RetroLounge.SystemOf(group.Key)?.Name ?? group.Key, group.Count().ToString());
                var panel = new WrapPanel();
                Body.Children.Add(panel);
                foreach (var g in group) GameTile(panel, g);
            }
        else
        {
            var panel = new WrapPanel();
            Body.Children.Add(panel);
            foreach (var g in games) GameTile(panel, g);
        }
    }

    private async Task SearchAsync()
    {
        var text = await Host.PromptAsync("Search Retro Lounge", "A game's name, or a console.", _search, "Search");
        if (text == null) return;
        _search = text.Trim();
        if (_section != "games") { _section = "games"; BuildNav(); }
        Build(focus: true);
    }

    private async Task PickSystemAsync()
    {
        var counts = RetroLounge.Visible().GroupBy(g => g.System).ToDictionary(x => x.Key, x => x.Count());
        var list = RetroLounge.Systems.Where(s => counts.ContainsKey(s.Id)).ToList();
        int pick = await Host.AskAsync("Console", null, new[] { "All consoles" }.Concat(list.Select(s => $"{s.Name}  ({counts[s.Id]})")).ToArray());
        if (pick < 0) return;
        _system = pick == 0 ? "" : list[pick - 1].Id;
        Build(focus: true);
    }

    private async Task PickSortAsync()
    {
        int pick = await Host.AskAsync("Sort", null, Sorts.Select(s => s.Name).ToArray());
        if (pick < 0) return;
        _sort = Sorts[pick].Id;
        Build(focus: true);
    }

    private async Task FilterMenuAsync()
    {
        int pick = await Host.AskAsync("Filter and sort", null, "Console", "Sort", _favorites ? "Show all games" : "Favorites only", "Search", "Clear everything");
        switch (pick)
        {
            case 0: await PickSystemAsync(); break;
            case 1: await PickSortAsync(); break;
            case 2: _favorites = !_favorites; Build(focus: true); break;
            case 3: await SearchAsync(); break;
            case 4: _system = _search = ""; _favorites = false; _sort = "name"; Build(focus: true); break;
        }
    }

    // ---------------------------------------------------------------- Emulators

    private void BuildEmulators()
    {
        var installed = RetroLounge.Installed().Where(i => File.Exists(i.Program)).ToList();
        var counts = RetroLounge.Visible().GroupBy(g => g.System).ToDictionary(x => x.Key, x => x.Count());
        Note("Each downloads from its own project and keeps its settings and saves in Retro Lounge's folder. Open one to set up controls or graphics.");

        void Row(RetroEmulator emu, string? value, string subtitle)
        {
            var row = Ui.Row(emu.Name, value, RetroLounge.InstalledFor(emu.Id) != null ? "" : "", subtitle: subtitle);
            row.Tag = "e:" + emu.Id;
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += (_, _) => { _focusId = "e:" + emu.Id; Host.Push(new RetroEmulatorScreen(emu)); };
            Body.Children.Add(row);
        }
        string Consoles(RetroEmulator e) => string.Join(", ", e.Systems.Select(id => RetroLounge.SystemOf(id)?.Short ?? id).Take(8)) + (e.Systems.Length > 8 ? $" and {e.Systems.Length - 8} more" : "");

        if (installed.Count > 0)
        {
            Heading("Installed", installed.Count.ToString());
            foreach (var i in installed)
                if (RetroLounge.Emulator(i.Id) is { } emu)
                {
                    string value = RetroDownloads.For(emu.Id) != null ? "Downloading" : RetroDownloads.Updates.TryGetValue(emu.Id, out var up) ? "Update: " + up.Version : i.Located ? "Your copy" : i.Version;
                    Row(emu, value, Consoles(emu));
                }
        }
        var wanted = counts.Keys.Select(id => RetroLounge.SystemOf(id)).Where(s => s != null).Select(s => RetroLounge.MissingFor(s!)).Where(e => e != null).Distinct().ToList();
        if (wanted.Count > 0)
        {
            Heading("Recommended for your games");
            if (wanted.Count > 1)
            {
                var all = new WrapPanel();
                Body.Children.Add(all);
                Chip(all, "", $"Get all {wanted.Count}", () => _ = GetAllAsync(wanted!), "need:all");
            }
            foreach (var emu in wanted)
            {
                var need = emu!.Systems.Where(counts.ContainsKey).Select(id => $"{RetroLounge.SystemOf(id)!.Short} ({counts[id]})");
                Row(emu, RetroDownloads.For(emu.Id) != null ? "Downloading" : "Get it", "For " + string.Join(", ", need));
            }
        }
        Heading("Every emulator");
        foreach (var emu in RetroLounge.Emulators.Where(e => installed.All(i => i.Id != e.Id) && !wanted.Contains(e)))
            Row(emu, RetroDownloads.For(emu.Id) != null ? "Downloading" : null, Consoles(emu));
    }

    /// <summary>Installs every emulator in the list, one after another in the background (Downloads shows them).</summary>
    private async Task GetAllAsync(IEnumerable<RetroEmulator?> emulators)
    {
        var list = emulators.Where(e => e != null && RetroLounge.InstalledFor(e.Id) == null && RetroDownloads.For(e.Id) == null).Select(e => e!).ToList();
        if (list.Count == 0) return;
        if (await Host.AskAsync($"Get {list.Count} emulators?", string.Join(", ", list.Select(e => e.Name)) + ". Each downloads from its own project; Downloads shows how they're doing.", "Cancel", "Get them") != 1) return;
        foreach (var emu in list) await RetroDownloads.StartNewestAsync(emu, Host);
        ShowSection("downloads", focusContent: true);
    }

    // ---------------------------------------------------------------- BIOS

    private void BuildBios()
    {
        string folder = RetroLounge.BiosFolder;
        Note("Some consoles need a BIOS (start-up) file from your own console. Put them in the BIOS folder: Retro Lounge copies each into the emulators that need it. It never downloads BIOS files.");
        var bar = new WrapPanel();
        Body.Children.Add(bar);
        Chip(bar, "", "Open the BIOS folder", () => { Directory.CreateDirectory(folder); Host.Push(new ExplorerScreen(folder)); }, "bios:open");
        Chip(bar, "", "Use another folder", async () =>
        {
            var picked = await Host.PickFolderAsync("Choose your BIOS folder");
            if (picked == null) return;
            RetroLounge.Config.BiosFolder = picked;
            RetroLounge.SaveConfig();
            Build(focus: true);
        }, "bios:change");
        Note(folder);
        var counts = RetroLounge.Visible().GroupBy(g => g.System).ToDictionary(x => x.Key, x => x.Count());
        var systems = RetroLounge.Systems.Where(s => s.Bios.Length > 0 || s.BiosNote.Length > 0).OrderByDescending(s => counts.ContainsKey(s.Id)).ThenBy(s => s.Name).ToList();
        var panel = new WrapPanel();
        Body.Children.Add(panel);
        foreach (var s in systems)
        {
            var checks = RetroLounge.CheckBios(s);
            bool ok = checks.All(c => c.Found);
            var inner = new StackPanel { Width = 360 };
            var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var chip = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Background = RetroUi.SystemBrush(s), Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = s.Short.Length > 4 ? s.Short[..3] : s.Short, FontSize = 13, FontWeight = FontWeights.Black, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(chip, Dock.Left);
            head.Children.Add(chip);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Ui.Text(s.Name, 16, "FgBrush", bold: true));
            string state = checks.Count == 0 ? "Nothing to add here" : ok ? "All set" : s.BiosRequired ? "Needed to play" : "Optional";
            title.Children.Add(Ui.Text(state + (counts.TryGetValue(s.Id, out var n) ? $"  ·  {n} games" : ""), 13, ok || checks.Count == 0 ? "SubtleBrush" : s.BiosRequired ? "AccentBrush" : "SubtleBrush"));
            head.Children.Add(title);
            inner.Children.Add(head);
            foreach (var c in checks)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 2) };
                var mark = Ui.Glyph(c.Found ? "" : "", 13, c.Found ? "AccentBrush" : "SubtleBrush");
                mark.Margin = new Thickness(0, 2, 8, 0);
                line.Children.Add(mark);
                var name = Ui.Text(c.Label, 14, "FgBrush");
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.MaxWidth = 320;
                line.Children.Add(name);
                inner.Children.Add(line);
            }
            if (s.BiosNote.Length > 0)
            {
                var note = Ui.Text(s.BiosNote, 13, "SubtleBrush");
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(2, 8, 0, 0);
                inner.Children.Add(note);
            }
            var tile = Ui.Tile(RetroUi.Card(inner, new Thickness(18, 16, 18, 16), new Thickness(0)));
            tile.Tag = "bios:" + s.Id;
            tile.Margin = new Thickness(0, 0, 10, 10);
            tile.Click += (_, _) => { Directory.CreateDirectory(folder); Host.Push(new ExplorerScreen(folder)); };
            panel.Children.Add(tile);
        }
    }

    // ---------------------------------------------------------------- Downloads

    private void BuildDownloads()
    {
        Heading("Downloading");
        if (RetroDownloads.Active.Count == 0) Note("Nothing is downloading. Emulator installs keep going here when you leave their page.");
        foreach (var d in RetroDownloads.Active.ToList())
        {
            var card = new Grid();
            card.ColumnDefinitions.Add(new ColumnDefinition());
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(d.Emulator.Name, 18, "FgBrush", bold: true));
            var status = Ui.Text(d.Status, 14, "SubtleBrush");
            status.Margin = new Thickness(0, 4, 0, 8);
            info.Children.Add(status);
            var track = new Grid { Height = 8 };
            var back = new Border { CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = RetroIntro.Sunset(vertical: false) };
            track.Children.Add(fill);
            track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * d.Fraction;
            info.Children.Add(track);
            card.Children.Add(info);
            var cancel = new Button { Style = Ui.StyleOf("PillButton"), Content = Label("", "Cancel"), VerticalAlignment = VerticalAlignment.Center, Tag = "dl:" + d.Emulator.Id };
            cancel.Click += (_, _) => d.Cancel.Cancel();
            Grid.SetColumn(cancel, 1);
            card.Children.Add(cancel);
            Body.Children.Add(RetroUi.Card(card, new Thickness(20, 16, 16, 16), new Thickness(0, 0, 10, 10)));
            _progressViews[d.Emulator.Id] = (status, fill);
        }
        Heading("Updates");
        var bar = new WrapPanel();
        Body.Children.Add(bar);
        string when = RetroDownloads.Checking ? "Checking…" : RetroDownloads.LastCheck == DateTime.MinValue ? "Check for updates" : "Check again (last checked " + RetroDownloads.LastCheck.ToString("t") + ")";
        Chip(bar, "", when, () => { if (!RetroDownloads.Checking) _ = RetroDownloads.CheckAsync(force: true); }, "up:check");
        var waiting = RetroDownloads.Updates.Where(u => RetroDownloads.For(u.Key) == null).ToList();
        if (waiting.Count == 0) Note(RetroDownloads.LastCheck == DateTime.MinValue ? "Retro Lounge checks the emulators it installed for new versions." : "Every emulator is up to date.");
        else if (waiting.Count > 1)
            Chip(bar, "", $"Update all {waiting.Count}", () =>
            {
                foreach (var (id, release) in waiting.ToList())
                    if (RetroLounge.Emulator(id) is { } e) RetroDownloads.Start(e, release, Host);
            }, "up:all");
        foreach (var (id, release) in waiting)
        {
            if (RetroLounge.Emulator(id) is not { } emu) continue;
            string current = RetroLounge.InstalledFor(id)?.Version ?? "";
            var row = Ui.Row(emu.Name, (current.Length > 0 ? current + "  →  " : "") + release.Version, "", subtitle: "Installed over the old version; settings and saves stay");
            row.Tag = "up:" + id;
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += (_, _) => RetroDownloads.Start(emu, release, Host);
            Body.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- Setup

    private void BuildSetup()
    {
        var c = RetroLounge.Config;
        Heading("ROM folders");
        if (c.Roots.Count == 0) Note("No ROM folder yet.");
        foreach (var root in c.Roots.ToList())
        {
            int found = RetroLounge.Games.Count(g => g.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            var row = Ui.Row(root, found + " games", "", subtitle: Directory.Exists(root) ? "Select to remove it from Retro Lounge (nothing is deleted)" : "Not found (an unplugged drive?)");
            row.Tag = "root:" + root;
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += async (_, _) =>
            {
                int pick = await Host.AskAsync(root, null, "Open it", "Remove from Retro Lounge", "Cancel");
                if (pick == 0 && Directory.Exists(root)) Host.Push(new ExplorerScreen(root));
                if (pick == 1) { c.Roots.Remove(root); RetroLounge.SaveConfig(); await RescanAsync(); }
            };
            Body.Children.Add(row);
        }
        var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(bar);
        Chip(bar, "", "Add a ROM folder", () => _ = AddRootAsync(), "root:add");
        Chip(bar, "", "Make a Retro Lounge folder", () => _ = MakeFoldersAsync(), "root:make");
        Chip(bar, "", _scanning ? "Looking…" : "Look for games again", () => { if (!_scanning) _ = RescanAsync(); }, "root:scan");
        if (RetroLounge.LastScan > DateTime.MinValue) Note($"{RetroLounge.Games.Count} files found, last looked {RetroLounge.LastScan:t}.");

        var hidden = RetroLounge.Games.Where(g => RetroLounge.PeekState(g)?.Hidden == true).ToList();
        if (hidden.Count > 0)
        {
            var row = Ui.Row("Hidden games", hidden.Count.ToString(), "", subtitle: "Games you hid from Retro Lounge. Select to bring them back");
            row.Tag = "root:hidden";
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += async (_, _) =>
            {
                var options = new[] { "Show them all again" }.Concat(hidden.Select(g => $"{g.Title}  ·  {RetroLounge.SystemOf(g.System)?.Short}")).ToArray();
                int pick = await Host.AskAsync("Hidden games", "Bring one back, or all of them.", options);
                if (pick < 0) return;
                foreach (var g in pick == 0 ? hidden : new List<RetroGame> { hidden[pick - 1] }) RetroLounge.StateOf(g).Hidden = false;
                RetroLounge.SaveConfig();
                KeepFocus(() => Build(focus: true));
            };
            Body.Children.Add(row);
        }

        Heading("Achievements and checks");
        var s = Host.Settings;
        var ra = Ui.Row("RetroAchievements", s.RaUser.Length > 0 ? s.RaUser : "Sign in", "\uE734",
            subtitle: s.RaUser.Length > 0 ? "Your progress shows on each game's page. Select to turn them on in RetroArch, or sign out" : "See your achievements for each game, and earn them in RetroArch");
        ra.Tag = "pref:ra";
        ra.Margin = new Thickness(0, 2, 10, 2);
        ra.Click += async (_, _) => { await RetroAccount.SignInAsync(Host); KeepFocus(() => Build(focus: true)); };
        Body.Children.Add(ra);
        var last = RetroExtras.LastVerify();
        var dat = Ui.Row("Check ROMs against DAT files", last.Count > 0 ? $"{last.Count(r => r.Verdict == RetroExtras.Verdict.Verified)} verified" : null, "\uE9D5",
            subtitle: s.RetroDatFolder.Length > 0 ? "Using " + s.RetroDatFolder + ": finds bad copies and duplicates" : "With No-Intro or Redump DAT files: finds bad copies and duplicates");
        dat.Tag = "pref:dat";
        dat.Margin = new Thickness(0, 2, 10, 2);
        dat.Click += (_, _) => _ = RetroAccount.VerifyAsync(Host);
        Body.Children.Add(dat);

        Heading("Preferences");
        void Toggle(string label, bool on, Action flip, string subtitle, string tag)
        {
            var row = Ui.Row(label, on ? "On" : "Off", null, subtitle: subtitle);
            row.Tag = tag;
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += (_, _) => { flip(); RetroLounge.SaveConfig(); KeepFocus(() => Build(focus: true)); };
            Body.Children.Add(row);
        }
        Toggle("One copy of each game", c.OnePerGame, () => c.OnePerGame = !c.OnePerGame, "When you have a game in several regions or versions, show the best one", "pref:one");
        var region = Ui.Row("Your region", c.Region, null, subtitle: "Which copy comes first, and the box art to look for");
        region.Tag = "pref:region";
        region.Margin = new Thickness(0, 2, 10, 2);
        region.Click += async (_, _) =>
        {
            var regions = new[] { "USA", "Europe", "Japan", "World", "Australia", "Brazil", "Korea" };
            int pick = await Host.AskAsync("Your region", null, regions);
            if (pick < 0) return;
            c.Region = regions[pick];
            RetroLounge.SaveConfig();
            KeepFocus(() => Build(focus: true));
        };
        Body.Children.Add(region);
        Toggle("Play full screen", c.FullScreen, () => c.FullScreen = !c.FullScreen, "Games open full screen in every emulator that can", "pref:fs");
        Toggle("Box art from the web", c.OnlineArt, () => c.OnlineArt = !c.OnlineArt, "From libretro-thumbnails (as RetroArch shows it). Your own pictures next to the games always come first", "pref:art");
        var folder = Ui.Row("Retro Lounge's folder", null, "", subtitle: RetroLounge.Folder + " (emulators, their saves and settings, box art)");
        folder.Tag = "pref:folder";
        folder.Margin = new Thickness(0, 2, 10, 2);
        folder.Click += (_, _) => { Directory.CreateDirectory(RetroLounge.Folder); Host.Push(new ExplorerScreen(RetroLounge.Folder)); };
        Body.Children.Add(folder);
        Note("Retro Lounge's ideas come from EmuDeck (one folder per console, BIOS beside it, portable emulators), RetroBat and ES-DE (folder names, one place for everything), Playnite (emulator command lines) and RetroArch (box art from libretro-thumbnails).");
    }

    private async Task AddRootAsync()
    {
        var picked = await Host.PickFolderAsync("Choose your ROM folder (the one with a folder per console)");
        if (picked == null) return;
        if (!RetroLounge.Config.Roots.Contains(picked, StringComparer.OrdinalIgnoreCase)) RetroLounge.Config.Roots.Add(picked);
        RetroLounge.SaveConfig();
        await RescanAsync();
    }

    private async Task MakeFoldersAsync()
    {
        var parent = await Host.PickFolderAsync("Where should the Retro Lounge folder go? (a \"roms\" and a \"bios\" folder are made inside)");
        if (parent == null) return;
        try
        {
            string roms = RetroLounge.CreateFolders(Path.Combine(parent, "Retro Lounge"));
            Host.ShowToast("Retro Lounge", "Folders made", "Put each console's games in its folder inside " + roms + ".", "", "info");
            await RescanAsync();
        }
        catch (Exception ex) { await Host.AskAsync("Couldn't make the folders", ex.Message, "OK"); }
    }

    private async Task RescanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try { await Host.RunBusyAsync("Looking for your games…", () => RetroLounge.ScanAsync()); }
        catch (Exception ex) { await Host.AskAsync("Couldn't look through the folders", ex.Message, "OK"); }
        finally { _scanning = false; }
        Host.ShowToast("Retro Lounge", $"{RetroLounge.Visible().Count} games found", $"Across {RetroLounge.Visible().Select(g => g.System).Distinct().Count()} consoles.", "", "info");
        KeepFocus(() => Build(focus: true));
    }
}

/// <summary>One console: its banner, what plays it (and a way to choose), BIOS status, and its games.</summary>
public sealed class RetroSystemScreen : GridScreen
{
    private readonly RetroSystem _system;
    private readonly RetroArtWall _wall;
    private string _sort = "name";
    private string? _focusId;

    public RetroSystemScreen(RetroSystem system) : base(system.Name, "{Select}  Open        {Details}  Sort        {Back}  Back")
    {
        _system = system;
        _wall = new RetroArtWall(() => Body.Parent as ScrollViewer);
        if (Body.Parent is ScrollViewer scroll) scroll.ScrollChanged += (_, _) => _wall.Soon();
        Body.SizeChanged += (_, _) => _wall.Soon();
        SetPageTint(RetroUi.Hex(system.Color1));
        RetroDownloads.Changed += Rebuild;
    }

    public override void OnShown() => Build(focus: true);
    public override void OnResumed() => Build(focus: true);
    public override void OnClosed() { _wall.Stop(); RetroDownloads.Changed -= Rebuild; }

    private void Rebuild() => Build(focus: false);

    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.X) return false;
        _ = SortAsync();
        return true;
    }

    private async Task SortAsync()
    {
        var sorts = new[] { ("name", "A to Z"), ("recent", "Recently played"), ("added", "Recently added"), ("region", "By region") };
        int pick = await Host.AskAsync("Sort", null, sorts.Select(s => s.Item2).ToArray());
        if (pick < 0) return;
        _sort = sorts[pick].Item1;
        Build(focus: true);
    }

    private void Build(bool focus)
    {
        if (Keyboard.FocusedElement is Button { Tag: string t }) _focusId = t;
        _wall.Clear();
        Body.Children.Clear();
        var s = _system;
        var games = RetroLounge.Visible().Where(g => g.System == s.Id).ToList();

        // The banner.
        var banner = new Grid { Height = 210, Margin = new Thickness(0, 0, 10, 16) };
        banner.Children.Add(new Border { CornerRadius = new CornerRadius(22), Background = RetroUi.SystemBrush(s, 0.6) });
        banner.Children.Add(new Border { CornerRadius = new CornerRadius(22), Background = RetroIntro.Scanlines(), Opacity = 0.45 });
        banner.Children.Add(new TextBlock { Text = s.Short, FontSize = 170, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 30, 0) });
        var words = new StackPanel { Margin = new Thickness(32, 0, 32, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = (s.Maker + "  ·  " + s.Year).ToUpperInvariant(), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)) });
        words.Children.Add(new TextBlock { Text = s.Name, FontSize = 40, FontWeight = FontWeights.Black, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        var pills = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var dark = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0));
        pills.Children.Add(RetroUi.Pill(games.Count == 1 ? "1 game" : games.Count + " games", dark));
        pills.Children.Add(RetroUi.Pill(string.Join("  ", s.Extensions.Where(e => e is not ".zip" and not ".7z").Take(5)).ToUpperInvariant(), dark));
        if (s.Bios.Length > 0) pills.Children.Add(RetroUi.Pill(RetroLounge.BiosOk(s) ? "BIOS ready" : s.BiosRequired ? "BIOS needed" : "BIOS optional", dark));
        words.Children.Add(pills);
        banner.Children.Add(words);
        Body.Children.Add(banner);

        // What plays it.
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        Body.Children.Add(bar);
        string player = RetroLounge.PlayerFor(s);
        bool ready = player.Length > 0 && RetroLounge.Ready(player);
        Button? first = null;
        if (ready) first = Chip(bar, "", "Plays with " + RetroLounge.PlayerName(player), () => _ = ChoosePlayerAsync(), "sys:player");
        else if (RetroLounge.MissingFor(s) is { } need)
        {
            first = Chip(bar, "", RetroDownloads.For(need.Id) != null ? "Getting " + need.Name + "…" : "Get " + need.Name, () => Host.Push(new RetroEmulatorScreen(need)), "sys:get");
            Chip(bar, "", "Other emulators", () => _ = ChoosePlayerAsync(), "sys:player");
        }
        if (ready && RetroLounge.InstalledFor(player.StartsWith("ra:") ? "retroarch" : player) is { } install)
            Chip(bar, "", "Set up " + (RetroLounge.Emulator(install.Id)?.Name ?? ""), () => { try { RetroLounge.OpenEmulator(install); } catch (Exception ex) { _ = Host.AskAsync("Couldn't open it", ex.Message, "OK"); } }, "sys:setup");
        if (s.Bios.Length > 0 && !RetroLounge.BiosOk(s))
            Chip(bar, "", "Add the BIOS", () => { Directory.CreateDirectory(RetroLounge.BiosFolder); Host.Push(new ExplorerScreen(RetroLounge.BiosFolder)); }, "sys:bios");
        if (s.BiosNote.Length > 0) Note(s.BiosNote);

        if (games.Count == 0)
        {
            string where = RetroLounge.Config.Roots.Count > 0 ? Path.Combine(RetroLounge.Config.Roots[0], s.Folders[0]) : "a \"" + s.Folders[0] + "\" folder in your ROM folder";
            Note($"No {s.Short} games yet. Put them in {where} ({string.Join(", ", s.Extensions.Take(6))}), then look again from Retro Lounge > Setup.");
            FocusLater(first);
            return;
        }
        games = _sort switch
        {
            "recent" => games.OrderByDescending(g => RetroLounge.PeekState(g)?.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Title).ToList(),
            "added" => games.OrderByDescending(g => g.Added).ToList(),
            "region" => games.OrderBy(g => g.Region).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        var panel = new WrapPanel();
        Body.Children.Add(panel);
        Button? target = null, firstGame = null;
        foreach (var g in games)
        {
            var box = RetroUi.GameBox(g, _wall);
            var tile = AddTile(box, g.Title, 150, () => Host.Push(new RetroGameScreen(g)), "g:" + g.Key, panel);
            firstGame ??= tile;
            if (_focusId == "g:" + g.Key) target = tile;
        }
        _wall.Soon();
        if (focus) FocusLater(target ?? firstGame ?? first);
    }

    private void Note(string text)
    {
        var t = Ui.Text(text, 15, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 0, 10, 12);
        Body.Children.Add(t);
    }

    private Button Chip(Panel bar, string glyph, string label, Action click, string tag)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 16, 8) };
        var g = Ui.Glyph(glyph, 16, "AccentBrush");
        g.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(g);
        row.Children.Add(Ui.Text(label, 15, "FgBrush", bold: true));
        var b = Ui.Tile(row);
        b.Margin = new Thickness(0, 0, 10, 10);
        b.Tag = tag;
        b.Click += (_, _) => click();
        bar.Children.Add(b);
        return b;
    }

    private async Task ChoosePlayerAsync()
    {
        var players = _system.Players;
        string current = RetroLounge.PlayerFor(_system);
        var options = players.Select(p => (RetroLounge.PlayerName(p) + (RetroLounge.Ready(p) ? "" : "  ·  not installed") + (p == current ? "  ·  playing now" : ""), RetroLounge.Ready(p) ? "" : "")).ToArray();
        int pick = await Host.AskWithIconsAsync("What plays " + _system.Short + " games?", "The best one installed plays unless you choose. A game can have its own choice too.", options);
        if (pick < 0 || pick >= players.Length) return;
        string chosen = players[pick];
        if (!RetroLounge.Ready(chosen))
        {
            if (RetroLounge.Emulator(chosen.StartsWith("ra:") ? "retroarch" : chosen) is { } emu) Host.Push(new RetroEmulatorScreen(emu));
            return;
        }
        RetroLounge.Config.SystemPlayer[_system.Id] = chosen;
        RetroLounge.SaveConfig();
        Build(focus: true);
    }
}

/// <summary>One game: play it (or get what plays it), choose its emulator, favorite it, add it to Home, and its details.</summary>
public sealed class RetroGameScreen : ListScreen
{
    private readonly RetroGame _game;
    private readonly RetroSystem? _system;

    public RetroGameScreen(RetroGame game) : base(game.Title)
    {
        _game = game;
        _system = RetroLounge.SystemOf(game.System);
        if (_system != null) SetPageTint(RetroUi.Hex(_system.Color1));
        RetroLounge.Played += OnPlayed;
    }

    private void OnPlayed() => Rebuild();
    public override void OnClosed() => RetroLounge.Played -= OnPlayed;

    public override void OnShown()
    {
        base.OnShown();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { Rows.Children.OfType<Button>().FirstOrDefault()?.Focus(); RowsScroll.ScrollToTop(); }));
    }

    protected override void Build()
    {
        BuildBanner();
        var s = _system;
        if (s == null) { AddNote("This console isn't one Retro Lounge knows."); return; }
        var state = RetroLounge.PeekState(_game);
        string player = RetroLounge.PlayerFor(s, _game);
        bool ready = RetroLounge.Ready(player);

        AddHeader("Play");
        if (ready) AddRow(state?.Plays > 0 ? "Continue" : "Play", RetroLounge.PlayerName(player), "", click: () => _ = RetroUi.PlayAsync(Host, _game));
        else if (RetroLounge.MissingFor(s) is { } need)
            AddRow("Get " + need.Name + " to play", RetroDownloads.For(need.Id) != null ? "Downloading" : null, "", click: () => Host.Push(new RetroEmulatorScreen(need)),
                subtitle: need.Blurb);
        if (s.BiosRequired && !RetroLounge.BiosOk(s))
            AddRow("A BIOS file is missing", string.Join(", ", RetroLounge.CheckBios(s).Where(b => !b.Found).Select(b => b.Label)), "",
                click: () => { Directory.CreateDirectory(RetroLounge.BiosFolder); Host.Push(new ExplorerScreen(RetroLounge.BiosFolder)); }, subtitle: "Put it in the BIOS folder: " + RetroLounge.BiosFolder);
        if (s.Players.Length > 1)
            AddRow("Play with…", state?.Player is { } own ? RetroLounge.PlayerName(own) : "The console's choice", "", click: () => _ = ChoosePlayerAsync(),
                subtitle: "Another emulator for this game only");
        AddRow(state?.Favorite == true ? "Favorite" : "Add to favorites", state?.Favorite == true ? "★" : null, state?.Favorite == true ? "" : "",
            click: () => { var st = RetroLounge.StateOf(_game); st.Favorite = !st.Favorite; RetroLounge.SaveConfig(); Rebuild(); });
        if (ready) AddRow("Add to Home", null, "", click: () => _ = AddToHomeAsync(), subtitle: "A tile on the home row that starts it in " + RetroLounge.PlayerName(player));

        AddHeader("About this copy");
        var facts = new WrapPanel();
        void Fact(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var f = new StackPanel { Width = 210, Margin = new Thickness(0, 0, 14, 14) };
            f.Children.Add(Ui.Text(label.ToUpperInvariant(), 11, "SubtleBrush", bold: true));
            var v = Ui.Text(value, 15, "FgBrush");
            v.TextWrapping = TextWrapping.Wrap;
            v.Margin = new Thickness(0, 2, 0, 0);
            f.Children.Add(v);
            facts.Children.Add(f);
        }
        Fact("Console", s.Name);
        Fact("Region", _game.Region);
        Fact("Version", string.Join(", ", _game.Tags.Where(t => !t.Split(',').Any(x => x.Trim() == _game.Region) && !t.StartsWith("Disc", StringComparison.OrdinalIgnoreCase))));
        Fact("Format", _game.Format);
        Fact("Size", _game.Size > 0 ? SystemStats.Bytes(_game.Size) : "");
        Fact("Discs", _game.Discs.Count > 1 ? _game.Discs.Count.ToString() : "");
        Fact("Played", RetroUi.Played(state));
        Fact("Found", _game.Added.ToString("MMM d, yyyy"));
        Rows.Children.Add(RetroUi.Card(facts, new Thickness(22, 16, 22, 4), new Thickness(0, 0, 0, 12)));
        AddNote(_game.Discs.Count > 1 ? string.Join("\n", _game.Discs.Select(Path.GetFileName)) : _game.Path);

        AddExtras(s);

        AddHeader("More");
        string? folder = Directory.Exists(_game.Path) ? _game.Path : Path.GetDirectoryName(_game.Path);
        if (folder != null) AddRow("Open its folder", null, "", click: () => Host.Push(new ExplorerScreen(folder)));
        AddRow("Hide it from Retro Lounge", null, "", click: async () =>
        {
            if (await Host.AskAsync("Hide " + _game.Title + "?", "It stays on your drive; Retro Lounge just stops showing it. Retro Lounge > Setup > Hidden games brings it back.", "Cancel", "Hide") != 1) return;
            RetroLounge.StateOf(_game).Hidden = true;
            RetroLounge.SaveConfig();
            Host.Pop(this);
        });
    }

    private RetroExtras.RaProgress? _ra;
    private bool _raLoading, _raLoaded;

    /// <summary>Its controls in the emulator, its save states and screenshots, RetroAchievements, and the DAT check.</summary>
    private void AddExtras(RetroSystem s)
    {
        string emuId = RetroExtras.EmulatorIdFor(_game);
        if (emuId.Length > 0)
        {
            AddHeader("Controls in " + (RetroLounge.Emulator(emuId)?.Name ?? "the emulator"));
            foreach (var (what, how) in RetroExtras.Hotkeys(emuId)) AddRow(what, null, "\uE7FC", subtitle: how);
        }

        var saves = RetroExtras.SavesFor(_game);
        var states = saves.Where(f => !f.Screenshot).ToList();
        var shots = saves.Where(f => f.Screenshot).Take(6).ToList();
        if (states.Count > 0 || shots.Count > 0 || RetroExtras.StatesFolder(_game) != null)
        {
            AddHeader("Saves and screenshots");
            if (states.Count > 0)
                AddRow($"{states.Count} save {(states.Count == 1 ? "state" : "states")}", "Latest " + states[0].When.ToString("MMM d, h:mm tt"), "\uE74E",
                    click: () => Host.Push(new ExplorerScreen(Path.GetDirectoryName(states[0].Path)!)), subtitle: "Load them from the emulator's menu while playing");
            else if (RetroExtras.StatesFolder(_game) is { } where)
                AddRow("No save states yet", null, "\uE74E", click: () => { if (Directory.Exists(where)) Host.Push(new ExplorerScreen(where)); },
                    subtitle: emuId == "retroarch" ? "Save one from RetroArch's Quick Menu while playing" : "This emulator keeps them in its own folder");
            if (shots.Count > 0)
            {
                var wall = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
                foreach (var shot in shots)
                {
                    var box = GridScreen.ImageBox(220, 165, out var image);
                    box.Margin = new Thickness(0, 0, 10, 10);
                    image.Stretch = Stretch.Uniform;
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    string file = shot.Path;
                    _ = Task.Run(() => ImageLoader.FromFile(file, 440)).ContinueWith(task => { if (task.Result != null) image.Source = task.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
                    wall.Children.Add(box);
                }
                Rows.Children.Add(wall);
                AddRow("Open the screenshots folder", null, "\uE91B", click: () => Host.Push(new ExplorerScreen(Path.GetDirectoryName(shots[0].Path)!)));
            }
        }

        AddHeader("RetroAchievements");
        var settings = Host.Settings;
        if (settings.RaUser.Length == 0)
            AddRow("Sign in to RetroAchievements", null, "\uE734", click: async () => { await RetroAccount.SignInAsync(Host); _raLoaded = false; Rebuild(); },
                subtitle: "See which achievements you've earned in this game");
        else if (!RetroExtras.CanHash(_game))
            AddRow("Not checked for this copy", null, "\uE734", subtitle: "Retro Lounge can match cartridge games (and their .zip files); disc games aren't matched yet");
        else if (!_raLoaded)
        {
            AddRow("Looking it up\u2026", null, "\uE734");
            if (!_raLoading)
            {
                _raLoading = true;
                _ = Task.Run(async () =>
                {
                    var p = await RetroExtras.ProgressAsync(_game, settings);
                    Dispatcher.BeginInvoke(new Action(() => { _ra = p; _raLoaded = true; _raLoading = false; Rebuild(); }));
                });
            }
        }
        else if (_ra == null)
            AddRow("Not on RetroAchievements", null, "\uE734", subtitle: "This copy didn't match a game there (another region or revision may)");
        else
        {
            var progress = _ra;
            AddRow($"{progress.Earned} of {progress.Total} earned", $"{progress.PointsEarned} / {progress.Points} pts", "\uE734",
                click: () => Host.Push(new RetroAchievementsScreen(progress)), subtitle: progress.Total == 0 ? "No achievements yet for this game" : "Select to see them all");
        }

        // The last DAT check's word on this file.
        var verdict = RetroExtras.LastVerify().FirstOrDefault(r => string.Equals(r.Path, _game.Path, StringComparison.OrdinalIgnoreCase));
        if (verdict != null && verdict.Verdict != RetroExtras.Verdict.Skipped)
        {
            AddHeader("Checked against your DAT files");
            AddRow(verdict.Verdict switch
                {
                    RetroExtras.Verdict.Verified => "Verified: a known good copy",
                    RetroExtras.Verdict.Duplicate => "A duplicate",
                    _ => "Not in your DAT files",
                }, null, verdict.Verdict == RetroExtras.Verdict.Verified ? "\uE73E" : "\uE7BA",
                subtitle: verdict.Verdict switch
                {
                    RetroExtras.Verdict.Verified => verdict.DatName,
                    RetroExtras.Verdict.Duplicate => "Same data as " + Path.GetFileName(verdict.Duplicate),
                    _ => "A bad copy, a hack or translation, or its console's DAT file isn't in the folder",
                });
        }
    }

    private void BuildBanner()
    {
        var banner = new Grid { Height = 280, Margin = new Thickness(0, 0, 0, 14) };
        if (_system != null) banner.Background = RetroUi.SystemBrush(_system, 0.5);
        banner.Clip = new RectangleGeometry(new Rect(0, 0, 940, 280), 18, 18);
        var snap = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(snap, BitmapScalingMode.NearestNeighbor);
        banner.Children.Add(snap);
        banner.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0xE0, 0, 0, 0), 90) });
        banner.Children.Add(new Border { Background = RetroIntro.Scanlines(), Opacity = 0.45 });
        var front = new DockPanel { Margin = new Thickness(26, 0, 26, 22), VerticalAlignment = VerticalAlignment.Bottom };
        var poster = GridScreen.ImageBox(150, 200, out var cover);
        cover.Stretch = Stretch.Uniform;
        poster.Margin = new Thickness(0, 0, 22, 0);
        if (_system != null) poster.Background = RetroUi.SystemBrush(_system);
        DockPanel.SetDock(poster, Dock.Left);
        front.Children.Add(poster);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        titles.Children.Add(new TextBlock { Text = _game.Title, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        var pills = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var dark = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));
        if (_system != null) pills.Children.Add(RetroUi.Pill(_system.Name, dark));
        if (_game.Region.Length > 0) pills.Children.Add(RetroUi.Pill(_game.Region, dark));
        if (_game.Format.Length > 0) pills.Children.Add(RetroUi.Pill(_game.Format, dark));
        titles.Children.Add(pills);
        front.Children.Add(titles);
        banner.Children.Add(front);
        Rows.Children.Add(banner);
        _ = Fill();

        async Task Fill()
        {
            if (await RetroLounge.ArtAsync(_game, "Named_Boxarts", 400) is { } art) cover.Source = art;
            var shot = await RetroLounge.ArtAsync(_game, "Named_Snaps", 960) ?? await RetroLounge.ArtAsync(_game, "Named_Titles", 960);
            if (shot == null) return;
            snap.Source = shot;
            if (Theme.ReduceMotion) snap.Opacity = 0.9;
            else snap.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(450)));
        }
    }

    private async Task ChoosePlayerAsync()
    {
        if (_system == null) return;
        var players = new[] { "" }.Concat(_system.Players).ToArray();
        var options = players.Select(p => p.Length == 0 ? ("The console's choice (" + RetroLounge.PlayerName(RetroLounge.PlayerFor(_system)) + ")", "")
            : (RetroLounge.PlayerName(p) + (RetroLounge.Ready(p) ? "" : "  ·  not installed"), RetroLounge.Ready(p) ? "" : "")).ToArray();
        int pick = await Host.AskWithIconsAsync("Play " + _game.Title + " with…", null, options);
        if (pick < 0) return;
        string chosen = players[pick];
        if (chosen.Length > 0 && !RetroLounge.Ready(chosen))
        {
            if (RetroLounge.Emulator(chosen.StartsWith("ra:") ? "retroarch" : chosen) is { } emu) Host.Push(new RetroEmulatorScreen(emu));
            return;
        }
        RetroLounge.StateOf(_game).Player = chosen.Length == 0 ? null : chosen;
        RetroLounge.SaveConfig();
        Rebuild();
    }

    private async Task AddToHomeAsync()
    {
        string id = "retro-" + RetroLounge.Hash(_game.Key);
        if (Host.Library.CustomGames.Any(c => c.Id == id)) { await Host.AskAsync("It's already on Home", null, "OK"); return; }
        try
        {
            (string Program, string Arguments, string Folder) command = default;
            await Host.RunBusyAsync("Adding it to Home…", async () => command = await RetroLounge.CommandAsync(_game));
            string? cover = await RetroLounge.CoverFileAsync(_game);
            Host.AddCustomGame(id, _game.Title, command.Program, cover, command.Arguments);
            Host.ShowToast("Added to Home", _game.Title, "It's on the home row and in the Library.", "", "info");
        }
        catch (Exception ex) { await Host.AskAsync("Couldn't add it", ex.Message, "OK"); }
    }
}

/// <summary>One emulator: what it plays, Install / Update / Set up / Locate / Uninstall, and where it comes from.</summary>
public sealed class RetroEmulatorScreen : ListScreen
{
    private readonly RetroEmulator _emu;
    private RetroRelease? _release;
    private bool _checked;
    private string? _error;
    private readonly TextBlock _progressText = Ui.Text("", 15, "SubtleBrush");
    private readonly Border _progressFill = new() { Height = 8, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Width = 0 };
    private bool _hadDownload;

    private RetroDownload? Download => RetroDownloads.For(_emu.Id);

    public RetroEmulatorScreen(RetroEmulator emu) : base(emu.Name)
    {
        _emu = emu;
        _progressFill.Background = RetroIntro.Sunset(vertical: false);
        RetroDownloads.Progressed += OnProgress;
        RetroDownloads.Changed += OnChanged;
    }

    private void OnProgress(RetroDownload d)
    {
        if (d.Emulator.Id != _emu.Id) return;
        _progressFill.Width = 928 * d.Fraction;
        _progressText.Text = d.Status;
    }

    private void OnChanged()
    {
        bool has = Download != null;
        if (has == _hadDownload) return;
        _hadDownload = has;
        Rebuild();
    }

    public override async void OnShown()
    {
        base.OnShown();
        try { _release = await RetroLounge.ReleaseAsync(_emu); }
        catch (Exception ex) { _error = ex.Message.Contains("403") ? "GitHub is limiting requests right now. Try again in a few minutes." : "Couldn't check for the newest version: " + ex.Message; }
        _checked = true;
        Rebuild();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => Rows.Children.OfType<Button>().FirstOrDefault()?.Focus()));
    }

    public override void OnClosed()
    {
        RetroDownloads.Progressed -= OnProgress;
        RetroDownloads.Changed -= OnChanged;
    }

    protected override void Build()
    {
        // The header: name, what it is, what it plays.
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "EMULATOR", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = RetroIntro.Sunset(vertical: false) });
        head.Children.Add(Ui.Text(_emu.Name, 34, "FgBrush", bold: true));
        var blurb = Ui.Text(_emu.Blurb, 16, "SubtleBrush");
        blurb.TextWrapping = TextWrapping.Wrap;
        blurb.Margin = new Thickness(0, 6, 0, 12);
        head.Children.Add(blurb);
        var plays = new WrapPanel();
        foreach (var id in _emu.Systems) if (RetroLounge.SystemOf(id) is { } s) plays.Children.Add(RetroUi.Pill(s.Short, RetroUi.SystemBrush(s)));
        head.Children.Add(plays);
        Rows.Children.Add(RetroUi.Card(head, new Thickness(24, 20, 24, 14), new Thickness(0, 0, 0, 14)));

        var install = RetroLounge.InstalledFor(_emu.Id);
        AddHeader(install != null ? "Installed" : "Get it");
        _hadDownload = Download != null;
        if (Download is { } d)
        {
            OnProgress(d);
            var box = new StackPanel { Margin = new Thickness(6, 4, 6, 10) };
            box.Children.Add(_progressText);
            var track = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var back = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            track.Children.Add(_progressFill);
            box.Children.Add(track);
            Rows.Children.Add(box);
            AddRow("Cancel", null, "", click: () => Download?.Cancel.Cancel(), subtitle: "It keeps going if you leave: Retro Lounge > Downloads shows it");
        }
        else if (install != null)
        {
            AddRow("Open " + _emu.Name, install.Located ? "Your copy" : install.Version, "", click: () => { try { RetroLounge.OpenEmulator(install); } catch (Exception ex) { _ = Host.AskAsync("Couldn't open it", ex.Message, "OK"); } },
                subtitle: "Set up its controls, graphics" + (_emu.Id is "rpcs3" or "vita3k" ? " and Sony's firmware" : "") + " the first time");
            if (install.Located) AddNote("Found on your PC with Locate installed: Retro Lounge plays games with it and leaves updating it to you.");
            else if (_release != null && _release.Version != install.Version)
                AddRow("Update to " + _release.Version, _release.Size > 0 ? SystemStats.Bytes(_release.Size) : null, "", click: () => RetroDownloads.Start(_emu, _release, Host), subtitle: "Installed over this version; its settings and saves stay");
            else if (_checked && _release != null) AddRow("Up to date", _release.Version, "", click: null);
            AddRow("Open its folder", null, "", click: () => Host.Push(new ExplorerScreen(install.Folder)));
            AddRow("Uninstall", null, "", click: () => _ = UninstallAsync(install));
        }
        else if (!_checked) AddNote("Checking for the newest version…");
        else if (_release == null) AddNote(_error ?? "There's no Windows download for it right now. Its page may have changed; Locate installed works with a copy you already have.");
        else AddRow("Install", _release.Version + (_release.Size > 0 ? "  ·  " + SystemStats.Bytes(_release.Size) : ""), "", click: () => { _progressText.Text = "Starting the download…"; RetroDownloads.Start(_emu, _release, Host); },
            subtitle: _emu.Source == "github" ? "From its GitHub release, checked against GitHub's checksum" : "From its official download page");
        if (Download == null && install == null)
            AddRow("Locate installed", null, "", click: () => _ = LocateAsync(), subtitle: "Already have it? Choose its program and Retro Lounge uses that copy");
        if (_emu.Note.Length > 0) AddNote(_emu.Note);

        AddHeader("About");
        AddRow("Its website", _emu.Site.Replace("https://", ""), "", click: () => Host.Push(BrowserScreen.Open(Host, _emu.Site)));
        if (_release?.Page is { Length: > 0 } page && page != _emu.Site) AddRow("This version's page", null, "", click: () => Host.Push(BrowserScreen.Open(Host, page)));
        AddRow("License", _emu.License, "", click: null);
        if (_emu.Id == "retroarch")
        {
            var ra = RetroLounge.InstalledFor("retroarch");
            int cores = ra != null && Directory.Exists(Path.Combine(ra.Folder, "cores")) ? Directory.GetFiles(Path.Combine(ra.Folder, "cores"), "*_libretro.dll").Length : 0;
            AddNote($"Cores download the first time a console needs one ({cores} so far), from libretro's buildbot.");
        }
    }

    private async Task LocateAsync()
    {
        var program = await Host.PickFileAsync($"Choose {_emu.Name}'s program", new[] { ".exe" });
        if (program == null || !File.Exists(program)) return;
        RetroLounge.Locate(_emu, program);
        Rebuild();
        Host.ShowToast("Retro Lounge", _emu.Name + " found", "Its games play with your copy now.", "", "info");
    }

    private async Task UninstallAsync(RetroEmuInstall install)
    {
        if (install.Located) { RetroLounge.Uninstall(install.Id); Rebuild(); return; }
        if (await Host.AskAsync("Uninstall " + _emu.Name + "?", "Its folder goes to the Recycle Bin, with its settings and the saves it kept there. Your games aren't touched.", "Cancel", "Uninstall") != 1) return;
        try { await Host.RunBusyAsync("Uninstalling…", () => Task.Run(() => RetroLounge.Uninstall(install.Id))); }
        catch (Exception ex) { await Host.AskAsync("Couldn't uninstall it", ex.Message, "OK"); }
        Rebuild();
    }
}
