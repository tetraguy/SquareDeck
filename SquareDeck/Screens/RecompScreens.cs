using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    public RecompZoneScreen() : base("Recomp Zone", "{Select}  Open        {Options}  Search        {Details}  Filter and sort        {Back}  Back")
    {
        _realizeSoon.Tick += (_, _) => Realize();
        if (Body.Parent is ScrollViewer scroll) scroll.ScrollChanged += (_, _) => RealizeSoon();
        Body.SizeChanged += (_, _) => RealizeSoon();
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
            await intro.FinishAsync();
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
        if (_built && !InstalledIds().SetEquals(_installedAtBuild)) Build();
        else RealizeSoon();
    }

    public override void OnClosed()
    {
        _introSkip?.Cancel();
        _life.Cancel();
        _cts.Cancel();
        _realizeSoon.Stop();
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
            case NavAction.Y: _ = SearchAsync(); return true;
            case NavAction.X: _ = FilterMenuAsync(); return true;
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
        var jobs = new List<(Panel Panel, RecompGame Game)>();
        void Section(string title, IEnumerable<RecompGame> list)
        {
            var items = list.ToList();
            if (items.Count == 0) return;
            Heading(title);
            var panel = new WrapPanel();
            Body.Children.Add(panel);
            foreach (var game in items) jobs.Add((panel, game));
        }

        Button MakeTile(Panel panel, RecompGame g)
        {
            var box = ImageBox(150, 225, out var image);
            var grid = (Grid)box.Child;
            if (installed.Contains(g.Id)) grid.Children.Add(Badge("Installed", HorizontalAlignment.Left, VerticalAlignment.Top, accent: true));
            if (g.Stars > 0) grid.Children.Add(Badge("\u2605 " + Short(g.Stars), HorizontalAlignment.Left, VerticalAlignment.Bottom, accent: false));
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
            return AddTile(box, g.Name, 150, () => { _focusId = g.Id; Host.Push(new RecompGameScreen(g)); }, g.Id, panel);
        }

        var shown = games.Where(g => !installed.Contains(g.Id) || filtered || _sort != "console").ToList();
        if (!filtered && _sort == "console") Section("Installed", games.Where(g => installed.Contains(g.Id)));
        switch (_sort)
        {
            case "stars": Section("Most stars", shown.OrderByDescending(g => g.Stars).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)); break;
            case "new": Section("Recently updated", shown.OrderByDescending(g => g.Released ?? DateTime.MinValue)); break;
            case "added": Section("Newest in the list", shown.OrderByDescending(g => g.Added ?? g.FirstRelease ?? DateTime.MinValue)); break;
            case "name": Section("A to Z", shown.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)); break;
            default:
                foreach (var group in shown.GroupBy(g => g.ConsoleName).OrderByDescending(x => x.Count()).ThenBy(x => x.Key))
                    Section(group.Key, group.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));
                break;
        }
        // The tiles, a batch at a time with a breath between, so the launcher keeps drawing (and the opening animating).
        const int Batch = 24;
        for (int i = 0; i < jobs.Count; i++)
        {
            var tile = MakeTile(jobs[i].Panel, jobs[i].Game);
            first ??= tile;
            if (_focusId != null && jobs[i].Game.Id == _focusId) target = tile;
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
    private CancellationTokenSource? _install;
    private readonly TextBlock _progressText = Ui.Text("", 15, "SubtleBrush");
    private readonly Border _progressFill = new() { Height = 8, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Width = 0 };

    public RecompGameScreen(RecompGame game) : base(game.Name)
    {
        _game = game;
        _progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
    }

    public override async void OnShown()
    {
        base.OnShown();
        ToTop();
        if (!_game.CanInstall || !_game.ForWindows) { _releaseChecked = true; Rebuild(); ToTop(); return; }
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

    public override void OnClosed() => _install?.Cancel();

    protected override void Build()
    {
        BuildBanner();

        var install = RecompZone.InstalledFor(_game.Id);
        AddHeader(install != null ? "Installed" : "Get it");
        if (_install != null)
        {
            var box = new StackPanel { Margin = new Thickness(6, 4, 6, 10) };
            box.Children.Add(_progressText);
            var track = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var back = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            track.Children.Add(_progressFill);
            box.Children.Add(track);
            Rows.Children.Add(box);
            AddRow("Cancel", null, "", click: () => _install?.Cancel());
        }
        else if (install != null)
        {
            AddRow("Play", install.Tag, "", click: () => Play(install));
            if (_release != null && _release.Tag != install.Tag)
                AddRow("Update to " + _release.Tag, Size(_release.Size), "", click: () => _ = InstallAsync(), subtitle: "Unpacked over this version; your saves and settings stay");
            else if (_releaseChecked && _release != null) AddRow("Up to date", _release.Tag, "", click: null);
            AddRow("Add to Home", null, "", click: () => _ = AddToHomeAsync(install), subtitle: "A tile for it on the home row, like your other games");
            AddRow("Open its folder", null, "", click: () => Host.Push(new ExplorerScreen(install.Folder)));
            AddRow("Uninstall", null, "", click: () => _ = UninstallAsync(install));
        }
        else if (!_game.ForWindows) AddNote("There's no Windows version of this recomp yet.");
        else if (!_game.CanInstall) AddRow("Get it from its page", null, "", click: OpenPage, subtitle: "This one isn't on GitHub, so it's installed from its own page");
        else if (!_releaseChecked) AddNote("Checking GitHub for the newest version…");
        else if (_release == null) { AddNote(_releaseError ?? "There's no Windows download for it on GitHub right now."); AddRow("Open its page", null, "", click: OpenPage); }
        else AddRow("Install", $"{_release.Tag}  ·  {Size(_release.Size)}", "", click: () => _ = InstallAsync(),
            subtitle: "Downloaded from the developer's GitHub release and checked against GitHub's checksum");

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
        var program = await Host.PickFileAsync($"Choose {_game.Name}'s program", new[] { ".exe" });
        if (program == null) return;
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

    private async Task InstallAsync()
    {
        if (_release == null || _install != null) return;
        _install = new CancellationTokenSource();
        var ct = _install.Token;
        var release = _release;
        _progressText.Text = "Starting the download…";
        _progressFill.Width = 0;
        Rebuild();
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            double f = p.Total > 0 ? Math.Clamp(p.Done / (double)p.Total, 0, 1) : 0;
            _progressFill.Width = 928 * f;
            _progressText.Text = p.Total > 0 ? $"Downloading {release.Tag}: {(int)Math.Floor(f * 100)}%  ({SystemStats.Bytes(p.Done)} of {SystemStats.Bytes(p.Total)})" : $"Downloading {release.Tag}: {SystemStats.Bytes(p.Done)}";
            if (f >= 1) _progressText.Text = "Unpacking…";
        });
        try
        {
            var done = await RecompZone.InstallAsync(_game, release, progress, ct);
            _install = null;
            Rebuild();
            Host.ShowToast("Recomp Zone", _game.Name + " is installed", "Press Play to start it. It asks for your copy of the game the first time.", "", "info");
        }
        catch (OperationCanceledException) { _install = null; Rebuild(); }
        catch (Exception ex)
        {
            _install = null;
            Rebuild();
            await Host.AskAsync("Couldn't install it", ex.Message, "OK");
        }
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
