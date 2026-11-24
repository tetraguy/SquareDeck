using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Recomp Zone (experimental): native PC versions of console games, rebuilt from their code by fans. Only recomps
/// with a Windows build are listed. Installed ones first, then the rest grouped by console, or filtered by platform
/// and genre and sorted by stars, newest or name. Covers load as small copies, a few at a time, as they come into view.
/// </summary>
public sealed class RecompZoneScreen : GridScreen
{
    private string _search = "", _platform = "", _genre = "", _sort = "console";
    private bool _loaded;
    private string? _error;
    private CancellationTokenSource _cts = new();
    private readonly List<Lazy> _lazy = new();
    private readonly System.Windows.Threading.DispatcherTimer _realizeSoon = new() { Interval = TimeSpan.FromMilliseconds(60) };

    private sealed class Lazy
    {
        public required FrameworkElement Box;
        public required Image Image;
        public required string Url;
        public bool Started;
    }

    private static readonly (string Key, string Name)[] Sorts = { ("console", "By console"), ("stars", "Most stars"), ("new", "Recently updated"), ("added", "Newest in the list"), ("name", "A to Z") };

    public RecompZoneScreen() : base("Recomp Zone", "{Select}  Open        {LB}{RB}  Sections        {Options}  Search        {Details}  Filter and sort        {Back}  Back")
    {
        _realizeSoon.Tick += (_, _) => Realize();
        if (Body.Parent is ScrollViewer scroll)
        {
            scroll.ScrollChanged += (_, _) => RealizeSoon(); // the Home rows' sideways scrolling bubbles up here too
            // The sidebar on the left, the section beside it.
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
        Body.SizeChanged += (_, _) => RealizeSoon();
        SizeChanged += (_, _) => { bool compact = ActualWidth < 1450; if (compact != _compact) { _compact = compact; UpdateNavShape(); } };
        _switchSoon.Tick += (_, _) => { _switchSoon.Stop(); if (_pendingSection != null) ShowSection(_pendingSection, focusContent: false); };
        _heroTimer.Tick += (_, _) => { if (_heroPanel?.IsKeyboardFocusWithin != true && Host?.IsTop(this) == true) ShowHero(_heroIndex + 1); };
        RecompDownloads.Changed += OnDownloadsChanged;
        RecompDownloads.Progressed += OnDownloadProgress;
    }

    private RecompIntro? _intro;
    private readonly CancellationTokenSource _life = new(); // until Recomp Zone closes
    private CancellationTokenSource? _introSkip;
    private HashSet<string> _installedAtBuild = new();
    private string? _focusId;     // the game last opened, so a rebuild comes back to it
    private bool _built;
    private const int TileDecode = 240;

    /// <summary>
    /// Opening: the decompile / recompile animation plays while the list is read, the covers are fetched to disk and
    /// the tiles are built (a few at a time, so the launcher keeps moving), then it completes and fades to the page.
    /// </summary>
    public override async void OnShown()
    {
        var intro = new RecompIntro { Rumble = ms => Host.Rumble(ms) };
        intro.SkipRequested = () => _introSkip?.Cancel();
        _intro = intro;
        ShowOver(intro);
        intro.Focus();
        var minimum = Task.Delay(Theme.ReduceMotion ? 0 : 1600); // long enough to read, even when everything is cached
        var ct = _life.Token;
        try
        {
            intro.Report("Fetching the catalog", "recomplauncher.com/pc-catalog.json", 0.04);
            using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                _ = CreepAsync(intro, 0.04, 0.14, waiting.Token);
                await LoadAsync(false, build: false);
                waiting.Cancel();
            }
            intro.Report("Decompiling the catalog", $"{RecompZone.Games.Count} recomps found", 0.16);
            if (ct.IsCancellationRequested) return;

            // Covers to disk, so the page shows them at once (and they're there next time, offline too).
            var urls = Listed().Select(g => g.Cover).Where(u => u.Length > 0).Distinct().ToList();
            int cached = urls.Count(u => RecompZone.HasPicture(u, TileDecode));
            if (urls.Count > 0)
            {
                _introSkip = CancellationTokenSource.CreateLinkedTokenSource(ct);
                intro.SetSkippable(cached < urls.Count);
                var progress = new Progress<(int Done, int Total)>(p =>
                    intro.Report("Recompiling the covers", $"{p.Done} / {p.Total}", 0.18 + 0.62 * p.Done / Math.Max(1, p.Total)));
                intro.Report("Recompiling the covers", $"{cached} / {urls.Count}", 0.18 + 0.62 * cached / urls.Count);
                try { await RecompZone.PrefetchAsync(urls, TileDecode, progress, _introSkip.Token); }
                catch (OperationCanceledException) { }
                intro.SetSkippable(false);
            }
            if (ct.IsCancellationRequested) return;

            // The page itself, built in small batches.
            intro.Report("Linking the page", "", 0.82);
            await BuildAsync(new Progress<double>(f => intro.Report("Linking the page", $"{(int)(f * 100)}%", 0.82 + 0.16 * f)), focus: false);
            if (ct.IsCancellationRequested) return;
            await Task.Delay(120); // the covers on screen come off the disk
            await minimum;
            // Recomp Zone's music comes in as the opening finishes (the launcher's fades out).
            MusicReady = true;
            Host.RefreshBackgroundPlayback();
            await intro.FinishAsync();
            _ = RecompDownloads.CheckAsync();
        }
        catch (Exception) { }
        finally
        {
            if (ReferenceEquals(_intro, intro))
            {
                (intro.Parent as Panel)?.Children.Remove(intro);
                _intro = null;
                if (!ct.IsCancellationRequested)
                {
                    if (!MusicReady) { MusicReady = true; Host.RefreshBackgroundPlayback(); } // even if the opening was cut short
                    if (!_built) Build();
                    else if (Host?.IsTop(this) == true) FocusDefault();
                }
            }
        }
    }

    /// <summary>Moves the bar along slowly while waiting on something that doesn't report progress.</summary>
    private static async Task CreepAsync(RecompIntro intro, double from, double to, CancellationToken ct)
    {
        for (double f = from; f < to && !ct.IsCancellationRequested; f += 0.004)
        {
            intro.Report("Fetching the catalog", "recomplauncher.com/pc-catalog.json", f);
            try { await Task.Delay(60, ct); } catch (OperationCanceledException) { return; }
        }
    }

    public override void FocusDefault()
    {
        if (_intro != null) { _intro.Focus(); return; }
        if (_section != "catalog")
        {
            if (FirstButton(Body) is { } b) b.Focus();
            else NavButton(_section)?.Focus();
            return;
        }
        var target = Body.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Button>())
            .FirstOrDefault(b => _focusId != null && b.Tag as string == _focusId)
            ?? Body.Children.OfType<WrapPanel>().Skip(1).SelectMany(p => p.Children.OfType<Button>()).FirstOrDefault();
        if (target != null) target.Focus();
        else base.FocusDefault();
    }

    private HashSet<string> InstalledIds() => RecompZone.Installed().Where(i => File.Exists(i.Program)).Select(i => i.Id).ToHashSet();

    /// <summary>
    /// Back from a game's page: the grid is still here as it was (kept until Recomp Zone closes), so nothing is
    /// rebuilt. Only if a recomp was installed or removed meanwhile is it rebuilt, coming back to the same game.
    /// </summary>
    public override void OnResumed()
    {
        if (_intro != null) return;
        Host.RefreshBackgroundPlayback(); // the music comes back after a trailer or a page that paused it
        // Home's "Jump back in" follows what was just played; Library and Downloads what was installed or removed.
        if (_built && (!InstalledIds().SetEquals(_installedAtBuild) || _section == "home")) KeepFocus(() => _ = BuildAsync(null, focus: !_nav.IsKeyboardFocusWithin));
        else RealizeSoon();
        BuildNav();
    }

    public override void OnClosed()
    {
        _introSkip?.Cancel();
        _life.Cancel();
        _cts.Cancel();
        _realizeSoon.Stop();
        _heroTimer.Stop();
        // The launcher's music fades back in.
        MusicReady = false;
        Dispatcher.BeginInvoke(() => Host.RefreshBackgroundPlayback(), System.Windows.Threading.DispatcherPriority.Background);
        _switchSoon.Stop();
        RecompDownloads.Changed -= OnDownloadsChanged;
        RecompDownloads.Progressed -= OnDownloadProgress;
        RecompZone.ForgetPictures();
    }

    private async Task LoadAsync(bool force, bool build = true)
    {
        _error = null;
        try { await RecompZone.LoadAsync(force); }
        catch (Exception ex) { _error = ex.Message; }
        _loaded = true;
        if (build) Build();
    }

    public override bool OnAction(NavAction action)
    {
        if (_intro != null)
        {
            // During the opening: A skips the rest of the covers, B leaves, nothing else does anything.
            if (action == NavAction.Accept) { _introSkip?.Cancel(); return true; }
            return action != NavAction.Back;
        }
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
                if (_section != "catalog") ShowSection("catalog", focusContent: true);
                _ = SearchAsync();
                return true;
            case NavAction.X:
                if (_section != "catalog") return true;
                _ = FilterMenuAsync();
                return true;
        }
        return false;
    }

    private async Task SearchAsync()
    {
        var text = await Host.PromptAsync("Search Recomp Zone", "A game, a recomp's name, a console, a series or a developer.", _search, "Search");
        if (text == null) return;
        _search = text.Trim();
        Rebuild();
    }

    // ---------------------------------------------------------------- filters

    private static List<RecompGame> Listed()
    {
        var all = RecompZone.Games.Where(g => !g.Soon).ToList();
        // Only recomps that run on Windows. (An old copy of the list without system details shows everything.)
        bool known = all.Any(g => g.Systems.Count > 0 || g.WindowsBuild);
        return known ? all.Where(g => g.ForWindows).ToList() : all;
    }

    private async Task FilterMenuAsync()
    {
        var pick = await Host.AskAsync("Filter and sort", null,
            "Platform: " + PlatformLabel, "Genre: " + GenreLabel, "Sort: " + SortLabel, "Search" + (_search.Length > 0 ? $": \"{_search}\"" : ""),
            "Show everything", "Refresh the list");
        switch (pick)
        {
            case 0: await PickPlatformAsync(); break;
            case 1: await PickGenreAsync(); break;
            case 2: await PickSortAsync(); break;
            case 3: await SearchAsync(); break;
            case 4: _platform = _genre = _search = ""; _sort = "console"; Rebuild(); break;
            case 5: _loaded = false; Build(); await LoadAsync(true); break;
        }
    }

    private string PlatformLabel => _platform.Length == 0 ? "All" : RecompZone.ConsoleName(_platform);
    private string GenreLabel => _genre.Length == 0 ? "All" : RecompZone.GenreName(_genre);
    private string SortLabel => Sorts.FirstOrDefault(s => s.Key == _sort).Name ?? "By console";

    private async Task PickPlatformAsync()
    {
        var origins = Listed().GroupBy(g => g.Origin.ToLowerInvariant()).OrderBy(x => RecompZone.ConsoleName(x.Key), StringComparer.OrdinalIgnoreCase).Select(x => (Key: x.Key, Label: $"{RecompZone.ConsoleName(x.Key)}  ({x.Count()})")).ToList();
        var options = new[] { "All platforms" }.Concat(origins.Select(o => o.Label)).ToArray();
        int pick = await Host.AskAsync("Platform", "The console the original game came out on.", options);
        if (pick < 0) return;
        _platform = pick == 0 ? "" : origins[pick - 1].Key;
        Rebuild();
    }

    private async Task PickGenreAsync()
    {
        var listed = Listed();
        var genres = RecompZone.GenreKeys.Select(k => (Key: k, Count: listed.Count(g => g.Genre == k))).Where(x => x.Count > 0).ToList();
        var options = new[] { "All genres" }.Concat(genres.Select(x => $"{RecompZone.GenreName(x.Key)}  ({x.Count})")).ToArray();
        int pick = await Host.AskAsync("Genre", null, options);
        if (pick < 0) return;
        _genre = pick == 0 ? "" : genres[pick - 1].Key;
        Rebuild();
    }

    private async Task PickSortAsync()
    {
        int pick = await Host.AskAsync("Sort", null, Sorts.Select(s => s.Name).ToArray());
        if (pick < 0) return;
        _sort = Sorts[pick].Key;
        Rebuild();
    }

    // ---------------------------------------------------------------- the page

    private void Note(string text, double size = 16)
    {
        var t = Ui.Text(text, size, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 0, 0, 14);
        Body.Children.Add(t);
    }

    private void Heading(string text)
    {
        var h = Ui.Text(text, 20, "FgBrush", bold: true);
        h.Margin = new Thickness(4, 10, 0, 10);
        Body.Children.Add(h);
    }

    /// <summary>A chip in the bar at the top: what's filtered, and a press to change it.</summary>
    private Button Chip(Panel bar, string glyph, string label, Action click)
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
        b.Click += (_, _) => click();
        bar.Children.Add(b);
        return b;
    }

    /// <summary>
    /// After a filter, sort or search change: the loading animation first, then the new list once it has had a
    /// moment on screen (building a few hundred tiles takes a beat, and this way the page never just freezes).
    /// </summary>
    private async void Rebuild()
    {
        int ticket = ++_rebuilds;
        _cts.Cancel();
        _lazy.Clear();
        Body.Children.Clear();
        string what = _platform.Length > 0 ? RecompZone.ConsoleName(_platform) + " recomps" : _genre.Length > 0 ? RecompZone.GenreName(_genre) + " recomps" : "recomps";
        Body.Children.Add(Loading($"Finding {what}…"));
        await Task.Delay(Theme.ReduceMotion ? 0 : 380);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        if (ticket != _rebuilds) return; // another change came meanwhile
        Build();
    }

    private int _rebuilds;

    /// <summary>A ring turning in the highlight color, with a line saying what's happening, in the middle of the page.</summary>
    private static FrameworkElement Loading(string text)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 140, 0, 140) };
        var ring = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
        var track = new System.Windows.Shapes.Ellipse { StrokeThickness = 6, Opacity = 0.18 };
        track.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "FgBrush");
        ring.Children.Add(track);
        double around = Math.PI * (64 - 6) / 6;
        var arc = new System.Windows.Shapes.Ellipse
        {
            StrokeThickness = 6, StrokeDashArray = new DoubleCollection { around * 0.28, around * 2 }, StrokeDashCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90)
        };
        arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        ring.Children.Add(arc);
        panel.Children.Add(ring);
        var label = Ui.Text(text, 17, "SubtleBrush");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 22, 0, 0);
        panel.Children.Add(label);
        if (!Theme.ReduceMotion)
        {
            ((RotateTransform)arc.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(-90, 270, TimeSpan.FromSeconds(0.95)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            panel.Opacity = 0;
            panel.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        }
        return panel;
    }

    private void Build() => _ = BuildAsync(null, focus: true);

    /// <summary>Builds the page: the bar, then the sections, then their tiles in batches so the launcher never stalls.</summary>
    private async Task BuildAsync(IProgress<double>? progress, bool focus)
    {
        int ticket = ++_rebuilds;
        _built = false;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _lazy.Clear();
        Body.Children.Clear();
        if (!_loaded) { Body.Children.Add(Loading("Loading the list of recomps…")); return; }
        if (_error != null && RecompZone.Games.Count == 0) { Note(_error, 18); return; }
        BuildNav();
        if (_section != "catalog")
        {
            _installedAtBuild = InstalledIds();
            switch (_section)
            {
                case "home": BuildHome(focus); break;
                case "soon": BuildSoon(focus); break;
                case "library": BuildLibrary(focus); break;
                case "downloads": BuildDownloads(focus); break;
            }
            progress?.Report(1);
            _built = true;
            RealizeSoon();
            return;
        }
        _heroTimer.Stop();
        Note("Native PC versions of console games, rebuilt from the original code by their developers. Only ones with a Windows version are shown. " +
             "Each needs your own copy of the game: SquareDeck never downloads game files." + (RecompZone.Offline ? "  (Offline: showing the list from last time.)" : ""));

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        Body.Children.Add(bar);
        var firstChip = Chip(bar, "", "Platform: " + PlatformLabel, () => _ = PickPlatformAsync());
        Chip(bar, "", "Genre: " + GenreLabel, () => _ = PickGenreAsync());
        Chip(bar, "", "Sort: " + SortLabel, () => _ = PickSortAsync());
        Chip(bar, "", _search.Length > 0 ? $"\"{_search}\"" : "Search", () => _ = SearchAsync());
        if (_platform.Length > 0 || _genre.Length > 0 || _search.Length > 0 || _sort != "console")
            Chip(bar, "", "Clear", () => { _platform = _genre = _search = ""; _sort = "console"; Rebuild(); });

        var installed = InstalledIds();
        _installedAtBuild = installed;
        var games = Listed();
        if (_platform.Length > 0) games = games.Where(g => g.Origin.Equals(_platform, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_genre.Length > 0) games = games.Where(g => g.Genre == _genre).ToList();
        if (_search.Length > 0)
            games = games.Where(g => $"{g.Name} {g.Game} {g.ConsoleName} {g.Developer} {g.AboutSeries} {g.AboutDeveloper} {g.GenreName} {string.Join(' ', g.Tags)}".Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
        bool filtered = _platform.Length > 0 || _genre.Length > 0 || _search.Length > 0;
        if (filtered) Note($"{games.Count} {(games.Count == 1 ? "recomp" : "recomps")}.");

        Button? first = null, target = null;
        var jobs = new List<(Panel Panel, List<RecompGame> Bundle)>();
        void Section(string title, IEnumerable<List<RecompGame>> list)
        {
            var items = list.ToList();
            if (items.Count == 0) return;
            Heading(title);
            var panel = new WrapPanel();
            Body.Children.Add(panel);
            foreach (var bundle in items) jobs.Add((panel, bundle));
        }

        // One tile per game: recomps of the same game (by its title or its cover) share a tile, with a count in the
        // corner, as in RecompLauncher; selecting it asks which one.
        Button MakeTile(Panel panel, List<RecompGame> bundle) => Tile(panel, bundle, installed);

        var shown = games.Where(g => !installed.Contains(g.Id) || filtered || _sort != "console").ToList();
        if (!filtered && _sort == "console") Section("Installed", Bundle(games.Where(g => installed.Contains(g.Id))).OrderBy(BundleName, StringComparer.OrdinalIgnoreCase));
        var bundles = Bundle(shown);
        switch (_sort)
        {
            case "stars": Section("Most stars", bundles.OrderByDescending(b => b.Max(g => g.Stars)).ThenBy(BundleName, StringComparer.OrdinalIgnoreCase)); break;
            case "new": Section("Recently updated", bundles.OrderByDescending(b => b.Max(g => g.Released ?? DateTime.MinValue))); break;
            case "added": Section("Newest in the list", bundles.OrderByDescending(b => b.Max(g => g.Added ?? g.FirstRelease ?? DateTime.MinValue))); break;
            case "name": Section("A to Z", bundles.OrderBy(BundleName, StringComparer.OrdinalIgnoreCase)); break;
            default:
                foreach (var group in bundles.GroupBy(b => b[0].ConsoleName).OrderByDescending(x => x.Count()).ThenBy(x => x.Key))
                    Section(group.Key, group.OrderBy(BundleName, StringComparer.OrdinalIgnoreCase));
                break;
        }
        // The tiles, a batch at a time with a breath between, so the launcher keeps drawing (and the opening animating).
        const int Batch = 24;
        for (int i = 0; i < jobs.Count; i++)
        {
            var tile = MakeTile(jobs[i].Panel, jobs[i].Bundle);
            first ??= tile;
            if (_focusId != null && jobs[i].Bundle.Any(g => g.Id == _focusId)) target = tile;
            if (i == Batch - 1 && focus && _focusId == null) FocusLater(first);
            if ((i + 1) % Batch == 0)
            {
                progress?.Report((i + 1) / (double)jobs.Count);
                RealizeSoon();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                if (ticket != _rebuilds) return; // replaced by a newer build
            }
        }
        progress?.Report(1);
        _built = true;
        if (first == null) Note(filtered ? "Nothing matches. Press " + Hints.KeyLabel("Details") + " to change the filters." : "The list is empty right now.");
        if (focus) FocusLater((UIElement?)target ?? first ?? firstChip);
        RealizeSoon();
    }

    // ---------------------------------------------------------------- the sidebar (as in RecompLauncher)

    private static readonly (string Id, string Name, string Glyph)[] Sections =
    {
        ("home", "Home", ""),
        ("catalog", "Catalog", ""),
        ("soon", "Coming soon", ""),
        ("library", "Library", ""),
        ("downloads", "Downloads", ""),
    };

    /// <summary>Recomp Zone's own music may play (its opening has finished, and it's still open).</summary>
    public bool MusicReady { get; private set; }

    private string _section = "home";
    private string? _pendingSection;
    private readonly System.Windows.Threading.DispatcherTimer _switchSoon = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly StackPanel _nav = new();
    private Border _navBox = null!;
    private bool _compact;          // a small screen (the handheld): the sidebar is icons until you move into it
    private bool? _navCollapsedBuilt;

    private bool NavCollapsed => _compact && !_nav.IsKeyboardFocusWithin;

    private FrameworkElement BuildNavPanel()
    {
        _navBox = new Border { Child = _nav, Padding = new Thickness(8), CornerRadius = new CornerRadius(18), VerticalAlignment = VerticalAlignment.Top };
        var tint = new Border { CornerRadius = new CornerRadius(18), Opacity = 0.55 };
        tint.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var holder = new Grid { Margin = new Thickness(0, 0, 24, 12), VerticalAlignment = VerticalAlignment.Top };
        holder.Children.Add(tint);
        holder.Children.Add(_navBox);
        // Moving into the sidebar on a small screen opens it up to its names; leaving shrinks it back to icons.
        _nav.IsKeyboardFocusWithinChanged += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(UpdateNavShape));
        BuildNav();
        return holder;
    }

    private void UpdateNavShape()
    {
        if (_navCollapsedBuilt == NavCollapsed) return;
        BuildNav();
    }

    private Button? NavButton(string id) => _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == "nav:" + id);

    /// <summary>Builds the sidebar (names, or icons on a small screen), keeping the highlight on the same entry.</summary>
    private void BuildNav()
    {
        string? focused = _nav.Children.OfType<Button>().FirstOrDefault(b => b.IsKeyboardFocused)?.DataContext as string;
        bool collapsed = NavCollapsed;
        _navCollapsedBuilt = collapsed;
        _nav.Children.Clear();
        int badge = RecompDownloads.Badge;
        foreach (var (id, name, glyph) in Sections)
        {
            int count = id == "downloads" ? badge : 0;
            Button b;
            if (collapsed)
            {
                b = Ui.BladeButton(glyph, name, selected: id == _section);
                b.Margin = new Thickness(0, 2, 0, 2);
                if (count > 0 && b.Content is Grid face)
                {
                    var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -5, -5, 0),
                        Child = new TextBlock { Text = count > 9 ? "9+" : count.ToString(), FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    ((TextBlock)dot.Child).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                    face.Children.Add(dot);
                }
            }
            else
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = Ui.Glyph(glyph, 18);
                icon.ClearValue(TextBlock.ForegroundProperty); // takes the row's color: the highlight color when it's the section showing
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(icon);
                var label = new TextBlock { Text = name, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (count > 0)
                {
                    var pill = new Border { CornerRadius = new CornerRadius(11), MinWidth = 22, Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
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
            // Moving onto a section shows it after a moment; selecting it goes into it.
            b.GotKeyboardFocus += (_, _) => { _pendingSection = target; _switchSoon.Stop(); _switchSoon.Start(); };
            b.Click += (_, _) => ShowSection(target, focusContent: true);
            _nav.Children.Add(b);
        }
        if (focused != null) _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == focused)?.Focus();
    }

    /// <summary>Shows a section, keeping the highlight in the sidebar unless asked to move into the section.</summary>
    private void ShowSection(string id, bool focusContent)
    {
        _switchSoon.Stop();
        if (_intro != null) return;
        if (id == _section)
        {
            if (focusContent) FocusDefault();
            return;
        }
        _section = id;
        _focusId = null;
        if (Body.Parent is ScrollViewer scroll) scroll.ScrollToTop();
        BuildNav();
        _ = BuildAsync(null, focus: focusContent);
    }

    /// <summary>Rebuilds while keeping the highlight where it was (the sidebar, or the same game).</summary>
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

    /// <summary>After building a section: the game last opened, otherwise the first thing in it.</summary>
    private void FocusSection(bool focus)
    {
        if (!focus) return;
        var target = (_focusId != null ? ButtonTagged(Body, _focusId) : null) ?? FirstButton(Body);
        FocusLater(target);
    }

    private void OnDownloadsChanged()
    {
        BuildNav();
        if (_intro == null && _built && _section is "downloads" or "library") KeepFocus(() => _ = BuildAsync(null, focus: Keyboard.FocusedElement is UIElement f && Body.IsAncestorOf(f)));
    }

    private readonly Dictionary<string, (TextBlock Text, Border Fill)> _progressViews = new();

    private void OnDownloadProgress(RecompDownload d)
    {
        if (!_progressViews.TryGetValue(d.Game.Id, out var v)) return;
        v.Text.Text = d.Status;
        v.Fill.Width = Math.Max(0, (v.Fill.Parent as FrameworkElement)?.ActualWidth ?? 0) * d.Fraction;
    }

    /// <summary>A row of tiles that scrolls sideways (Home's rows).</summary>
    private Panel Shelf(string title)
    {
        Heading(title);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        Body.Children.Add(new ScrollViewer
        {
            Content = panel, Focusable = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly, Margin = new Thickness(0, 0, 0, 6)
        });
        return panel;
    }

    private Panel TileWall(string? title)
    {
        if (title != null) Heading(title);
        var panel = new WrapPanel();
        Body.Children.Add(panel);
        return panel;
    }

    private RecompGame? GameFor(string id) => RecompZone.Games.FirstOrDefault(g => g.Id == id);

    /// <summary>Installed recomps that are in the catalog, the most recently played first.</summary>
    private List<(RecompInstall Install, RecompGame Game)> InstalledGames() =>
        RecompZone.Installed().Where(i => File.Exists(i.Program))
            .Select(i => (Install: i, Game: GameFor(i.Id)))
            .Where(x => x.Game != null)
            .Select(x => (x.Install, x.Game!))
            .OrderByDescending(x => x.Install.LastPlayed ?? x.Install.InstalledAt)
            .ToList();

    private void PlayInstalled(RecompInstall install)
    {
        try { RecompZone.Play(install); }
        catch (Exception ex) { _ = Host.AskAsync("Couldn't start it", ex.Message, "OK"); }
    }

    // ---------------------------------------------------------------- Home

    private List<List<RecompGame>> _heroes = new();
    private int _heroIndex;
    private Grid? _heroPanel;
    private Image? _heroArt, _heroLogo;
    private TextBlock? _heroKicker, _heroName, _heroBased, _heroBlurb, _heroPlayText, _heroPlayGlyph;
    private StackPanel? _heroDots, _heroText;
    private readonly System.Windows.Threading.DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    private void BuildHome(bool focus)
    {
        var installed = InstalledIds();
        var bundles = Bundle(Listed());

        // The banner: the most popular recomps (by players, then stars), the ones with artwork first.
        _heroes = bundles.Where(b => b[0].Hero.Length > 0 || b[0].Cover.Length > 0)
            .OrderByDescending(b => b[0].Hero.Length > 0)
            .ThenByDescending(b => b.Max(g => g.Players))
            .ThenByDescending(b => b.Max(g => g.Stars))
            .Take(6).ToList();
        if (_heroes.Count > 0) { Body.Children.Add(BuildHero()); ShowHero(0, animate: false); }

        var played = InstalledGames();
        if (played.Count > 0)
        {
            var shelf = Shelf("Jump back in");
            foreach (var (install, game) in played.Take(15))
            {
                var tile = Tile(shelf, new List<RecompGame> { game }, installed, () => PlayInstalled(install), corner: "▶  Play");
                tile.ToolTip = "Play " + game.Name;
            }
        }

        var updated = bundles.Where(b => b.Any(g => g.Released != null)).OrderByDescending(b => b.Max(g => g.Released ?? DateTime.MinValue)).Take(15).ToList();
        if (updated.Count > 0) { var shelf = Shelf("Recently updated"); foreach (var b in updated) Tile(shelf, b, installed); }

        var added = bundles.Where(b => b.Any(g => (g.Added ?? g.FirstRelease) != null)).OrderByDescending(b => b.Max(g => g.Added ?? g.FirstRelease ?? DateTime.MinValue)).Take(15).ToList();
        if (added.Count > 0) { var shelf = Shelf("New in the catalog"); foreach (var b in added) Tile(shelf, b, installed); }

        var stars = bundles.OrderByDescending(b => b.Max(g => g.Stars)).Take(15).ToList();
        if (stars.Count > 0) { var shelf = Shelf("Most stars"); foreach (var b in stars) Tile(shelf, b, installed); }

        if (RecompZone.Offline) Note("Offline: showing the list from last time.");
        if (_heroes.Count > 1 && !Theme.ReduceMotion) _heroTimer.Start(); else _heroTimer.Stop();
        FocusSection(focus);
    }

    private FrameworkElement BuildHero()
    {
        var panel = new Grid { Height = 380, Margin = new Thickness(0, 4, 10, 18) };
        _heroPanel = panel;
        var frame = new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var layers = new Grid();
        frame.Child = layers;
        panel.SizeChanged += (_, e) => layers.Clip = new RectangleGeometry(new Rect(e.NewSize), 22, 22);
        _heroArt = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right };
        layers.Children.Add(_heroArt);
        // Dark on the left for the words, clear on the right for the picture; a little darker along the bottom.
        var left = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0x08, 0x0A, 0x10), 0));
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x08, 0x0A, 0x10), 0.4));
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x08, 0x0A, 0x10), 0.8));
        layers.Children.Add(new Border { Background = left });
        layers.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x90, 0, 0, 0), 90) });
        panel.Children.Add(frame);

        // Which one of the set is showing.
        _heroDots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 26, 0, 0) };
        panel.Children.Add(_heroDots);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(40, 0, 40, 32), MaxWidth = 620 };
        _heroText = text;
        _heroKicker = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
        _heroKicker.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        text.Children.Add(_heroKicker);
        _heroLogo = new Image { MaxHeight = 120, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        text.Children.Add(_heroLogo);
        _heroName = new TextBlock { FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        text.Children.Add(_heroName);
        _heroBased = new TextBlock { FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(_heroBased);
        _heroBlurb = new TextBlock { FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, MaxHeight = 46, TextTrimming = TextTrimming.WordEllipsis, Margin = new Thickness(0, 0, 0, 18) };
        text.Children.Add(_heroBlurb);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var playRow = new StackPanel { Orientation = Orientation.Horizontal };
        _heroPlayGlyph = Ui.Glyph("", 16);
        _heroPlayGlyph.ClearValue(TextBlock.ForegroundProperty);
        _heroPlayGlyph.Margin = new Thickness(0, 1, 10, 0);
        playRow.Children.Add(_heroPlayGlyph);
        _heroPlayText = new TextBlock { FontSize = 18 };
        playRow.Children.Add(_heroPlayText);
        var play = new Button { Style = Ui.StyleOf("AccentButton"), Content = playRow, Margin = new Thickness(-3, 0, 10, 0) };
        play.Click += (_, _) => HeroPlay();
        buttons.Children.Add(play);
        var info = new Button { Style = Ui.StyleOf("PillButton"), Content = HeroLabel("", "Details"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        info.Click += (_, _) => { if (_heroes.Count > 0) _ = OpenBundleAsync(_heroes[_heroIndex], InstalledIds()); };
        buttons.Children.Add(info);
        if (_heroes.Count > 1)
        {
            var next = new Button { Style = Ui.StyleOf("PillButton"), Content = HeroLabel("", "Next"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "The next one" };
            next.Click += (_, _) => ShowHero(_heroIndex + 1);
            buttons.Children.Add(next);
        }
        text.Children.Add(buttons);
        panel.Children.Add(text);
        return panel;
    }

    private static StackPanel HeroLabel(string glyph, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(glyph, 14);
        g.ClearValue(TextBlock.ForegroundProperty);
        g.Margin = new Thickness(0, 1, 8, 0);
        row.Children.Add(g);
        row.Children.Add(new TextBlock { Text = label, FontSize = 16 });
        return row;
    }

    /// <summary>Puts one of the set in the banner (the same buttons stay, so the highlight doesn't move).</summary>
    private void ShowHero(int index, bool animate = true)
    {
        if (_heroes.Count == 0 || _heroPanel == null) return;
        _heroIndex = (index % _heroes.Count + _heroes.Count) % _heroes.Count;
        var bundle = _heroes[_heroIndex];
        var g = bundle[0];
        string kicker = "MOST POPULAR  ·  " + g.ConsoleName.ToUpperInvariant();
        if (bundle.Count > 1) kicker += $"  ·  {bundle.Count} RECOMPS";
        _heroKicker!.Text = kicker;
        _heroArt!.Source = null;
        string art = g.Hero.Length > 0 ? g.Hero : g.Cover;
        double artOpacity = g.Hero.Length > 0 ? 1 : 0.5;
        _heroArt.BeginAnimation(OpacityProperty, null);
        _heroArt.Opacity = artOpacity;
        if (art.Length > 0) _ = LoadInto(_heroArt, art, g.Hero.Length > 0 ? 1280 : 400);
        _heroLogo!.Source = null;
        _heroLogo.Visibility = g.Logo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _heroName!.Visibility = g.Logo.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        _heroName.Text = BundleName(bundle);
        if (g.Logo.Length > 0) _ = LoadInto(_heroLogo, g.Logo, 480);
        string based = g.Game.Length > 0 && g.Game != g.Name ? "Based on " + g.Game : g.Name;
        if (g.Developer.Length > 0) based += "   ·   Recomp by " + g.Developer;
        _heroBased!.Text = based;
        _heroBlurb!.Text = g.Blurb.Length > 0 ? g.Blurb : g.Summary;
        _heroBlurb.Visibility = _heroBlurb.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool has = bundle.Any(b => RecompZone.InstalledFor(b.Id) != null);
        _heroPlayText!.Text = has ? "Play" : "Get it";
        _heroPlayGlyph!.Text = has ? "" : "";
        _heroDots!.Children.Clear();
        for (int i = 0; i < _heroes.Count; i++)
        {
            var dot = new Border { Height = 4, Width = i == _heroIndex ? 46 : 26, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), Opacity = i == _heroIndex ? 0.95 : 0.35, Background = Brushes.White };
            _heroDots.Children.Add(dot);
        }
        if (animate && !Theme.ReduceMotion)
        {
            _heroArt.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, artOpacity, TimeSpan.FromMilliseconds(450)));
            _heroText!.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }
        // A press of Next starts the wait over.
        if (_heroTimer.IsEnabled) { _heroTimer.Stop(); _heroTimer.Start(); }
    }

    private void HeroPlay()
    {
        if (_heroes.Count == 0) return;
        var bundle = _heroes[_heroIndex];
        var install = bundle.Select(b => RecompZone.InstalledFor(b.Id)).FirstOrDefault(i => i != null);
        if (install != null) PlayInstalled(install);
        else _ = OpenBundleAsync(bundle, InstalledIds());
    }

    // ---------------------------------------------------------------- Coming soon, Library, Downloads

    private void BuildSoon(bool focus)
    {
        _heroTimer.Stop();
        var soon = RecompZone.Games.Where(g => g.Soon).ToList();
        Note("Recomps their developers are still working on. Each one's page has news on how it's coming along.");
        if (soon.Count == 0) { Note("Nothing announced right now."); FocusSection(focus); return; }
        var panel = TileWall(null);
        var installed = InstalledIds();
        foreach (var bundle in Bundle(soon).OrderByDescending(b => b.Max(g => g.Stars)).ThenBy(BundleName, StringComparer.OrdinalIgnoreCase))
            Tile(panel, bundle, installed, corner: "Coming soon");
        FocusSection(focus);
    }

    private void BuildLibrary(bool focus)
    {
        _heroTimer.Stop();
        var installed = InstalledIds();
        var games = InstalledGames();
        if (games.Count == 0)
        {
            Note("Recomps you install show up here, ready to play.");
            var bar = new WrapPanel();
            Body.Children.Add(bar);
            Chip(bar, "", "Browse the catalog", () => ShowSection("catalog", focusContent: true));
            FocusSection(focus);
            return;
        }
        int updates = games.Count(x => RecompDownloads.Updates.ContainsKey(x.Install.Id));
        Note($"{games.Count} installed" + (updates > 0 ? $"  ·  {updates} with an update (Downloads)" : "") + ". The most recently played first.");
        var panel = TileWall(null);
        foreach (var (install, game) in games)
        {
            string? corner = RecompDownloads.For(game.Id) != null ? "Downloading" : RecompDownloads.Updates.ContainsKey(game.Id) ? "Update" : null;
            Tile(panel, new List<RecompGame> { game }, installed, corner: corner, hideInstalled: true);
        }
        FocusSection(focus);
    }

    private void BuildDownloads(bool focus)
    {
        _heroTimer.Stop();
        _progressViews.Clear();
        Heading("Downloading");
        if (RecompDownloads.Active.Count == 0) Note("Nothing is downloading. Installs keep going here when you leave a game's page.");
        foreach (var d in RecompDownloads.Active.ToList())
        {
            var card = new Grid { Margin = new Thickness(0, 0, 10, 10) };
            card.ColumnDefinitions.Add(new ColumnDefinition());
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(d.Game.Name, 18, "FgBrush", bold: true));
            var status = Ui.Text(d.Status, 14, "SubtleBrush");
            status.Margin = new Thickness(0, 4, 0, 8);
            info.Children.Add(status);
            var track = new Grid { Height = 8 };
            var back = new Border { CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            track.Children.Add(fill);
            track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * d.Fraction;
            info.Children.Add(track);
            card.Children.Add(info);
            var cancel = new Button { Style = Ui.StyleOf("PillButton"), Content = HeroLabel("", "Cancel"), VerticalAlignment = VerticalAlignment.Center, Tag = "dl:" + d.Game.Id };
            cancel.Click += (_, _) => d.Cancel.Cancel();
            Grid.SetColumn(cancel, 1);
            card.Children.Add(cancel);
            var box = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16, 16, 16), Margin = new Thickness(0, 0, 10, 10), Child = card };
            box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            Body.Children.Add(box);
            _progressViews[d.Game.Id] = (status, fill);
        }

        Heading("Updates");
        var bar = new WrapPanel();
        Body.Children.Add(bar);
        string when = RecompDownloads.Checking ? "Checking GitHub…"
            : RecompDownloads.LastCheck == DateTime.MinValue ? "Check for updates"
            : "Check again (last checked " + RecompDownloads.LastCheck.ToString("t") + ")";
        Chip(bar, "", when, () => { if (!RecompDownloads.Checking) _ = RecompDownloads.CheckAsync(force: true); });
        var waiting = RecompDownloads.Updates.Where(u => RecompDownloads.For(u.Key) == null).ToList();
        if (waiting.Count == 0)
            Note(RecompDownloads.LastCheck == DateTime.MinValue ? "SquareDeck checks the recomps you've installed for new versions on GitHub." : "Everything you've installed is up to date.");
        foreach (var (id, release) in waiting)
        {
            var game = GameFor(id);
            if (game == null) continue;
            var current = RecompZone.InstalledFor(id)?.Tag ?? "";
            var row = Ui.Row(game.Name, (current.Length > 0 ? current + "  →  " : "") + release.Tag, "",
                subtitle: (release.Size > 0 ? SystemStats.Bytes(release.Size) + "  ·  " : "") + "Unpacked over the old version; saves and settings stay");
            row.Tag = "up:" + id;
            row.Margin = new Thickness(0, 2, 10, 2);
            row.Click += (_, _) => RecompDownloads.Start(game, release, Host);
            Body.Children.Add(row);
        }
        FocusSection(focus);
    }

    // ---------------------------------------------------------------- one tile per game

    /// <summary>
    /// One tile per game: recomps of the same game (by its title or its cover) share a tile, with a count in the
    /// corner, as in RecompLauncher; selecting it asks which one (or does <paramref name="click"/>).
    /// </summary>
    private Button Tile(Panel panel, List<RecompGame> bundle, HashSet<string> installed, Action? click = null, string? corner = null, bool hideInstalled = false)
    {
        var g = bundle[0]; // the most-starred
        var box = ImageBox(150, 225, out var image);
        var grid = (Grid)box.Child;
        if (corner != null) grid.Children.Add(Badge(corner, HorizontalAlignment.Left, VerticalAlignment.Top, accent: true));
        else if (!hideInstalled && bundle.Any(b => installed.Contains(b.Id))) grid.Children.Add(Badge("Installed", HorizontalAlignment.Left, VerticalAlignment.Top, accent: true));
        int stars = bundle.Max(b => b.Stars);
        if (stars > 0) grid.Children.Add(Badge("\u2605 " + Short(stars), HorizontalAlignment.Left, VerticalAlignment.Bottom, accent: false));
        if (bundle.Count > 1) grid.Children.Add(CountBadge(bundle.Count));
        if (g.Cover.Length == 0)
        {
            var name = Ui.Text(g.Game.Length > 0 ? g.Game : g.Name, 16, "FgBrush", bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextAlignment = TextAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(10);
            grid.Children.Add(name);
        }
        else _lazy.Add(new Lazy { Box = box, Image = image, Url = g.Cover });
        string label = bundle.Count > 1 && g.Game.Length > 0 ? g.Game : g.Name;
        return AddTile(box, label, 150, click ?? (() => _ = OpenBundleAsync(bundle, installed)), g.Id, panel);
    }

    private static string BundleName(List<RecompGame> bundle) => bundle.Count > 1 && bundle[0].Game.Length > 0 ? bundle[0].Game : bundle[0].Name;

    private static string Plain(string text) => new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Groups recomps of the same game: the same console and the same original game title, or the same cover
    /// picture. Each group's most-starred recomp comes first.
    /// </summary>
    internal static List<List<RecompGame>> Bundle(IEnumerable<RecompGame> games)
    {
        var list = games.ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        var byKey = new Dictionary<string, int>();
        // A picture used by many different recomps is a stand-in ("no cover yet"), not the game's own: not a match.
        var coverUses = list.Where(g => g.Cover.Length > 0).GroupBy(g => g.Cover.Trim().ToLowerInvariant()).ToDictionary(x => x.Key, x => x.Count());
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            var keys = new List<string>();
            if (Plain(g.Game).Length >= 3) keys.Add("game:" + g.Origin.ToLowerInvariant() + ":" + Plain(g.Game));
            string cover = g.Cover.Trim().ToLowerInvariant();
            if (cover.Length > 0 && coverUses[cover] <= 6) keys.Add("cover:" + g.Origin.ToLowerInvariant() + ":" + cover);
            foreach (var key in keys)
            {
                if (byKey.TryGetValue(key, out int other)) Join(other, i);
                else byKey[key] = i;
            }
        }
        return list.Select((g, i) => (g, root: Find(i)))
                   .GroupBy(x => x.root)
                   .Select(grp => grp.Select(x => x.g).OrderByDescending(g => g.Stars).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList())
                   .ToList();
    }

    /// <summary>The number of recomps on a tile, bottom right, with a stack icon (as RecompLauncher shows it).</summary>
    private static Border CountBadge(int count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Glyph("\uE81E", 12, "FgBrush");
        icon.Margin = new Thickness(0, 1, 5, 0);
        row.Children.Add(icon);
        row.Children.Add(Ui.Text(count.ToString(), 12, "FgBrush", bold: true));
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8), Child = row,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14))
        };
    }

    /// <summary>One recomp opens straight away; several ask which one first, with their stars, maker and whether it's installed.</summary>
    private async Task OpenBundleAsync(List<RecompGame> bundle, HashSet<string> installed)
    {
        var pick = bundle[0];
        if (bundle.Count > 1)
        {
            var options = bundle.Select(g =>
            {
                var parts = new List<string> { g.Name };
                if (g.Stars > 0) parts.Add("\u2605 " + Short(g.Stars));
                if (g.Developer.Length > 0) parts.Add(g.Developer);
                if (installed.Contains(g.Id)) parts.Add("Installed");
                return (string.Join("  \u00B7  ", parts), installed.Contains(g.Id) ? "\uE73E" : "\uE896");
            }).ToArray();
            string title = bundle[0].Game.Length > 0 ? bundle[0].Game : bundle[0].Name;
            int choice = await Host.AskWithIconsAsync(title, $"{bundle.Count} recomps of this game. Which one would you like?", options);
            if (choice < 0 || choice >= bundle.Count) return;
            pick = bundle[choice];
        }
        _focusId = pick.Id;
        Host.Push(new RecompGameScreen(pick));
    }

    private static Border Badge(string text, HorizontalAlignment h, VerticalAlignment v, bool accent)
    {
        var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8), HorizontalAlignment = h, VerticalAlignment = v,
            Child = Ui.Text(text, 12, accent ? "BgBrush" : "FgBrush", bold: true) };
        if (accent) badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else badge.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14));
        return badge;
    }

    internal static string Short(int n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();

    // ---------------------------------------------------------------- covers only near the screen

    private void RealizeSoon()
    {
        _realizeSoon.Stop();
        _realizeSoon.Start();
    }

    private void Realize()
    {
        _realizeSoon.Stop();
        if (Body.Parent is not ScrollViewer scroll || _lazy.Count == 0) return;
        double vw = scroll.ViewportWidth, vh = scroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;
        var near = new Rect(-vw, -vh, vw * 3, vh * 3);      // what's on screen, and a screen above and below
        var far = new Rect(-vw * 2, -vh * 4, vw * 5, vh * 9);
        var ct = _cts.Token;
        foreach (var t in _lazy)
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
                _ = LoadInto(t.Image, t.Url, 240, ct);
            }
            else if (t.Started && !r.IntersectsWith(far))
            {
                t.Image.Source = null; // let go of covers scrolled far away; the disk copy brings them back at once
                t.Started = false;
            }
        }
    }

    internal static async Task LoadInto(Image image, string url, int width, CancellationToken ct = default)
    {
        try
        {
            var picture = await RecompZone.PictureAsync(url, width, ct);
            if (picture != null && !ct.IsCancellationRequested) image.Source = picture;
        }
        catch (Exception) { }
    }
}

/// <summary>One recomp: what it is, which version is available, and Install / Play / Update / Add to Home / Uninstall.</summary>
public sealed class RecompGameScreen : ListScreen
{
    private readonly RecompGame _game;
    private RecompRelease? _release;
    private bool _releaseChecked;
    private string? _releaseError;
    private RecompDownload? Download => RecompDownloads.For(_game.Id);
    private readonly TextBlock _progressText = Ui.Text("", 15, "SubtleBrush");
    private readonly Border _progressFill = new() { Height = 8, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Width = 0 };

    public RecompGameScreen(RecompGame game) : base(game.Name)
    {
        _game = game;
        _progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        RecompDownloads.Progressed += OnProgress;
        RecompDownloads.Changed += OnDownloadsChanged;
    }

    /// <summary>The download carries on in the background when this page closes (Recomp Zone > Downloads shows it).</summary>
    private void OnProgress(RecompDownload d)
    {
        if (d.Game.Id != _game.Id) return;
        _progressFill.Width = 928 * d.Fraction;
        _progressText.Text = d.Status;
    }

    private bool _hadDownload;

    private void OnDownloadsChanged()
    {
        bool has = Download != null;
        if (has == _hadDownload) return;
        _hadDownload = has;
        Rebuild();
    }

    public override async void OnShown()
    {
        base.OnShown();
        ToTop();
        if (!_game.CanInstall || !_game.ForWindows || _game.Soon) { _releaseChecked = true; Rebuild(); ToTop(); return; }
        try { _release = await RecompZone.ReleaseAsync(_game); }
        catch (Exception ex) { _releaseError = ex.Message.Contains("403") ? "GitHub is limiting requests right now. Try again in a few minutes." : "Couldn't check GitHub for its download: " + ex.Message; }
        _releaseChecked = true;
        Rebuild();
        if (!_moved) ToTop();
    }

    private bool _moved; // the player has moved the highlight since the page opened

    public override bool OnAction(NavAction action)
    {
        if (action is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right) _moved = true;
        return base.OnAction(action);
    }

    /// <summary>The page opens at the top (the banner and the install options), with the first option highlighted.</summary>
    private void ToTop() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
    {
        Rows.Children.OfType<Button>().FirstOrDefault()?.Focus();
        RowsScroll.ScrollToTop();
    }));

    public override void OnClosed()
    {
        RecompDownloads.Progressed -= OnProgress;
        RecompDownloads.Changed -= OnDownloadsChanged;
    }

    protected override void Build()
    {
        BuildBanner();

        var install = RecompZone.InstalledFor(_game.Id);
        AddHeader(install != null ? "Installed" : "Get it");
        _hadDownload = Download != null;
        if (Download is { } download)
        {
            OnProgress(download);
            var box = new StackPanel { Margin = new Thickness(6, 4, 6, 10) };
            box.Children.Add(_progressText);
            var track = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var back = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            track.Children.Add(_progressFill);
            box.Children.Add(track);
            Rows.Children.Add(box);
            AddRow("Cancel", null, "", click: () => Download?.Cancel.Cancel(), subtitle: "It keeps going if you leave this page: Recomp Zone > Downloads shows it");
        }
        else if (install != null)
        {
            AddRow("Play", install.Located ? "Your copy" : install.Tag, "", click: () => Play(install));
            if (install.Located)
                AddNote("Found on your PC with Locate installed: SquareDeck plays it from where it is and leaves updating it to you.");
            else if (_release != null && _release.Tag != install.Tag)
                AddRow("Update to " + _release.Tag, Size(_release.Size), "", click: () => _ = InstallAsync(), subtitle: "Unpacked over this version; your saves and settings stay");
            else if (_releaseChecked && _release != null) AddRow("Up to date", _release.Tag, "", click: null);
            AddRow("Add to Home", null, "", click: () => _ = AddToHomeAsync(install), subtitle: "A tile for it on the home row, like your other games");
            AddRow("Open its folder", null, "", click: () => Host.Push(new ExplorerScreen(install.Folder)));
            AddRow("Uninstall", null, "", click: () => _ = UninstallAsync(install));
        }
        else if (_game.Soon) { AddNote("This recomp isn't out yet." + (_game.StatusNote.Length > 0 ? " " + _game.StatusNote : " Its page has news on how it's coming along.")); AddRow("Open its page", null, "\uE774", click: OpenPage); }
        else if (!_game.ForWindows) AddNote("There's no Windows version of this recomp yet.");
        else if (!_game.CanInstall) AddRow("Get it from its page", null, "", click: OpenPage, subtitle: "This one isn't on GitHub, so it's installed from its own page");
        else if (!_releaseChecked) AddNote("Checking GitHub for the newest version…");
        else if (_release == null) { AddNote(_releaseError ?? "There's no Windows download for it on GitHub right now."); AddRow("Open its page", null, "", click: OpenPage); }
        else AddRow("Install", $"{_release.Tag}  ·  {Size(_release.Size)}", "", click: () => _ = InstallAsync(),
            subtitle: "Downloaded from the developer's GitHub release and checked against GitHub's checksum");
        // Already installed by hand (or with RecompLauncher): no need to download it again.
        if (Download == null && install == null && !_game.Soon)
            AddRow("Locate installed", null, "\uE838", click: () => _ = LocateAsync(),
                subtitle: "Already have it? Choose its program (.exe) and SquareDeck plays it from where it is, without installing it again");

        // The rest of what there is to know, under the install options (with room to breathe between them).
        Rows.Children.Add(new Border { Height = 22 });
        BuildDetails();

        AddHeader("More");
        if (_game.Screenshots.Count > 0) AddRow("Screenshots", _game.Screenshots.Count.ToString(), "", click: () => Host.Push(new ViewerScreen(_game.Screenshots, 0)));
        if (_release != null && _release.Notes.Length > 0)
            AddRow("What's new in " + _release.Tag, _release.Published > DateTime.MinValue ? _release.Published.ToString("MMM d, yyyy") : null, "",
                click: () => _ = Host.AskAsync(_release.Name, Clean(_release.Notes), "OK"));
        AddRow("The developer's page", _game.Repo, "", click: OpenPage);
        AddNote($"You need your own copy of {(_game.Game.Length > 0 ? _game.Game : "the original game")}. The recomp asks for it (or for files from it) the first time it starts, " +
                "and its page says which version it needs. SquareDeck never downloads game files. Recomp Zone's list comes from recomplauncher.com.");
    }

    // ---------------------------------------------------------------- the top of the page, like a Library game's

    private static TextBlock Line(string text, double size, string brush = "SubtleBrush", bool bold = false, bool wrap = false)
    {
        var t = Ui.Text(text, size, brush, bold);
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        else t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static Border Card(UIElement child, Thickness padding, Thickness margin)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = padding, Margin = margin, Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return card;
    }

    private static Border Pill(string text, string brush = "CircleBrush")
    {
        var p = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 8, 8), Child = Ui.Text(text, 13, "FgBrush", bold: true) };
        p.SetResourceReference(Border.BackgroundProperty, brush);
        return p;
    }

    private void BuildBanner()
    {
        // The banner: the backdrop (or the cover, blurred by its size), the cover poster, and the logo or name beside it.
        var banner = new Grid { Height = 270, Margin = new Thickness(0, 0, 0, 14) };
        banner.SetResourceReference(Panel.BackgroundProperty, "TileBrush");
        banner.Clip = new RectangleGeometry(new Rect(0, 0, 940, 270), 18, 18);
        string art = _game.Hero.Length > 0 ? _game.Hero : _game.Cover;
        if (art.Length > 0)
        {
            var image = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center, Opacity = _game.Hero.Length > 0 ? 1 : 0.45 };
            banner.Children.Add(image);
            _ = RecompZoneScreen.LoadInto(image, art, _game.Hero.Length > 0 ? 960 : 400);
        }
        banner.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xD8, 0, 0, 0), 90)
        });
        var front = new DockPanel { Margin = new Thickness(26, 0, 26, 22), VerticalAlignment = VerticalAlignment.Bottom, LastChildFill = true };
        if (_game.Cover.Length > 0)
        {
            var poster = GridScreen.ImageBox(124, 186, out var cover);
            poster.Margin = new Thickness(0, 0, 22, 0);
            poster.VerticalAlignment = VerticalAlignment.Bottom;
            _ = RecompZoneScreen.LoadInto(cover, _game.Cover, 240);
            DockPanel.SetDock(poster, Dock.Left);
            front.Children.Add(poster);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (_game.Logo.Length > 0)
        {
            var logo = new Image { MaxHeight = 100, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            _ = RecompZoneScreen.LoadInto(logo, _game.Logo, 480);
            titles.Children.Add(logo);
        }
        else
        {
            var name = Ui.Text(_game.Name, 34, "FgBrush", bold: true);
            name.Foreground = Brushes.White;
            name.TextWrapping = TextWrapping.Wrap;
            titles.Children.Add(name);
        }
        string sub = string.Join("   ·   ", new[] { _game.Game.Length > 0 && _game.Game != _game.Name ? _game.Game : "", _game.ConsoleName, _game.Developer.Length > 0 ? "Recomp by " + _game.Developer : "" }.Where(x => x.Length > 0));
        var subLine = Ui.Text(sub, 16, "FgBrush");
        subLine.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        subLine.TextWrapping = TextWrapping.Wrap;
        titles.Children.Add(subLine);
        front.Children.Add(titles);
        banner.Children.Add(front);
        Rows.Children.Add(banner);
    }

    private void BuildDetails()
    {
        // Numbers at a glance.
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Chip(string glyph, string value, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 18, "AccentBrush");
            g.Margin = new Thickness(0, 0, 10, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(g);
            var text = new StackPanel();
            text.Children.Add(Line(value, 18, "FgBrush", bold: true));
            text.Children.Add(Line(label, 12));
            row.Children.Add(text);
            chips.Children.Add(Card(row, new Thickness(16, 10, 18, 10), new Thickness(0, 0, 10, 10)));
        }
        if (_game.Stars > 0) Chip("", RecompZoneScreen.Short(_game.Stars), "stars on " + (_game.Host is { Length: > 0 } h ? char.ToUpper(h[0]) + h[1..] : "GitHub"));
        if (_game.Downloads > 0) Chip("", RecompZoneScreen.Short(_game.Downloads), "downloads");
        if (_game.Players > 0) Chip("", RecompZoneScreen.Short(_game.Players), "PCs played it, last 90 days");
        if (_game.Releases > 0) Chip("", _game.Releases.ToString(), _game.Releases == 1 ? "release" : "releases");
        if (_game.Released is DateTime upd) Chip("", Ago(upd), "last update");
        if (chips.Children.Count > 0) Rows.Children.Add(chips);

        // Badges: how far along it is, and whether it plays well on handhelds.
        var badges = new WrapPanel();
        if (_game.HandheldOk) badges.Children.Add(Pill("Great on handhelds" + (_game.HandheldDevices.Count > 0 ? ": " + string.Join(", ", _game.HandheldDevices.Take(4)) : ""), "CircleBrush"));
        string status = _game.Status switch { "complete" => "Finished by its developer", "paused" => "On hold", "ended" or "abandoned" => "No longer updated", _ => "" };
        if (status.Length > 0) badges.Children.Add(Pill(status));
        if (_game.GenreName.Length > 0) badges.Children.Add(Pill(_game.GenreName));
        foreach (var tag in _game.Tags.Take(8)) badges.Children.Add(Pill(tag));
        if (badges.Children.Count > 0) Rows.Children.Add(badges);

        // About: the blurb, then the original game's summary.
        string about = _game.Blurb.Length > 0 ? _game.Blurb : _game.Summary;
        if (about.Length > 0)
        {
            var text = Line(about, 16, "FgBrush", wrap: true);
            text.LineHeight = 24;
            Rows.Children.Add(Card(text, new Thickness(22, 18, 22, 18), new Thickness(0, 6, 0, 12)));
        }

        // Facts: the recomp's, and the original game's.
        void Facts(string title, IEnumerable<(string Label, string Value)> list)
        {
            var items = list.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToList();
            if (items.Count == 0) return;
            var panel = new StackPanel();
            panel.Children.Add(Line(title.ToUpperInvariant(), 12, "AccentBrush", bold: true));
            var grid = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (label, value) in items)
            {
                var f = new StackPanel { Width = 210, Margin = new Thickness(0, 0, 14, 14) };
                f.Children.Add(Line(label.ToUpperInvariant(), 11, "SubtleBrush", bold: true));
                var v = Line(value, 15, "FgBrush", wrap: true);
                v.Margin = new Thickness(0, 2, 0, 0);
                f.Children.Add(v);
                grid.Children.Add(f);
            }
            panel.Children.Add(grid);
            Rows.Children.Add(Card(panel, new Thickness(22, 16, 22, 4), new Thickness(0, 0, 0, 12)));
        }
        string Day(DateTime? d) => d is DateTime x ? x.ToString("MMM d, yyyy") : "";
        var runs = new List<string>();
        if (_game.ForWindows) runs.Add("Windows");
        if (_game.Systems.Contains("linux")) runs.Add("Linux");
        if (_game.Systems.Contains("macos")) runs.Add("Mac");
        Facts("The recomp", new[]
        {
            ("Made by", _game.Developer), ("Latest version", _game.Version), ("Size", _game.Size), ("Latest release", Day(_game.Released)),
            ("First release", Day(_game.FirstRelease)), ("License", _game.License), ("Runs on", string.Join(", ", runs)), ("Project", _game.Repo)
        });
        Facts("The original game", new[]
        {
            ("Game", _game.Game.Length > 0 ? _game.Game : _game.Name), ("Console", _game.ConsoleName), ("Released", _game.AboutReleased),
            ("Developer", _game.AboutDeveloper), ("Publisher", _game.AboutPublisher), ("Series", _game.AboutSeries),
            ("Genres", string.Join(", ", _game.AboutGenres)), ("Modes", _game.AboutModes), ("Rating", _game.AboutRating)
        });
        if (_game.Blurb.Length > 0 && _game.Summary.Length > 0 && _game.Summary != _game.Blurb) AddNote(_game.Summary);
    }

    private static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span.TotalDays < 1) return "Today";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays < 45) return $"{(int)span.TotalDays} days ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} months ago";
        return when.ToString("MMM yyyy");
    }

    /// <summary>"Locate installed": the player picks the program of a copy they already have.</summary>
    private async Task LocateAsync()
    {
        // Its program, or simply the folder it's in (SquareDeck finds the program there).
        var picked = await Host.PickFileAsync($"Choose {_game.Name}'s program or folder", new[] { ".exe" }, orFolder: true);
        if (picked == null) return;
        string? program = picked;
        if (Directory.Exists(picked))
        {
            try { program = await Task.Run(() => RecompZone.FindProgram(picked, _game.StartProgram)); } catch (Exception) { program = null; }
            if (program == null)
            {
                await Host.AskAsync("No program found", "There's no program (.exe) for " + _game.Name + " in that folder or the folders inside it. Choose the folder it's installed in, or its .exe.", "OK");
                return;
            }
        }
        if (!File.Exists(program)) return;
        RecompZone.Locate(_game, program);
        Rebuild();
        Host.ShowToast("Recomp Zone", _game.Name + " found", "Play it from here, or add it to Home.", "", "info");
    }

    private static string Size(long bytes) => bytes <= 0 ? "" : SystemStats.Bytes(bytes);

    /// <summary>Release notes without the Markdown marks, kept to a readable length.</summary>
    private static string Clean(string notes)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(notes, @"\*\*|__|`|^#+\s*|!\[[^\]]*\]\([^)]*\)", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return text.Length > 2400 ? text[..2400] + "…" : text.Trim();
    }

    private void OpenPage() => Host.Push(BrowserScreen.Open(Host, _release?.Page is { Length: > 0 } p ? p : _game.Page));

    private void Play(RecompInstall install)
    {
        try { RecompZone.Play(install); }
        catch (Exception ex) { _ = Host.AskAsync("Couldn't start it", ex.Message, "OK"); }
    }

    private Task InstallAsync()
    {
        if (_release == null || Download != null) return Task.CompletedTask;
        _progressText.Text = "Starting the download…";
        _progressFill.Width = 0;
        RecompDownloads.Start(_game, _release, Host);
        return Task.CompletedTask;
    }

    private async Task AddToHomeAsync(RecompInstall install)
    {
        string id = "recomp-" + install.Id;
        if (Host.Library.CustomGames.Any(c => c.Id == id || string.Equals(c.Path, install.Program, StringComparison.OrdinalIgnoreCase)))
        {
            await Host.AskAsync("It's already on Home", null, "OK");
            return;
        }
        string? cover = null;
        if (_game.Cover.Length > 0)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(_game.Cover);
                string ext = Path.GetExtension(new Uri(_game.Cover).AbsolutePath);
                cover = Path.Combine(RecompZone.Folder, "Covers", install.Id + (ext.Length is > 1 and <= 5 ? ext : ".jpg"));
                Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
                await File.WriteAllBytesAsync(cover, bytes);
            }
            catch (Exception) { cover = null; }
        }
        Host.AddCustomGame(id, _game.Name, install.Program, cover);
        Host.ShowToast("Added to Home", _game.Name, "It's on the home row and in the Library.", "", "info");
    }

    private async Task UninstallAsync(RecompInstall install)
    {
        if (install.Located)
        {
            RecompZone.Uninstall(install.Id);
            Rebuild();
            return;
        }
        if (await Host.AskAsync("Uninstall " + _game.Name + "?", "Its folder goes to the Recycle Bin. Saves it keeps elsewhere (in your user folder) aren't touched.", "Cancel", "Uninstall") != 1) return;
        try { await Host.RunBusyAsync("Uninstalling…", () => Task.Run(() => RecompZone.Uninstall(install.Id))); }
        catch (Exception ex) { await Host.AskAsync("Couldn't uninstall it", ex.Message, "OK"); }
        Rebuild();
    }
}
