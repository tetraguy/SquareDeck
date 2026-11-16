using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>A setting found by Search: its name, subtitle, which page it's on, and how to open it there.</summary>
public sealed record SearchSetting(string Label, string? Subtitle, string Page, string Glyph, Func<Screen> Open);

/// <summary>
/// Search everything (the Start / Menu button on the home screen): games in the library (installed or not),
/// apps (yours and everything in the Start menu), settings, files in your folders, the Steam store, and links
/// to search the Microsoft Store and the web.
/// </summary>
public sealed class SearchScreen : Screen
{
    private const int MaxGames = 12, MaxApps = 8, MaxSettings = 8, MaxFiles = 10, MaxStore = 8;

    // Built once and reused: they are slow to gather and rarely change while the launcher is open.
    private static List<SearchSetting>? _settingsIndex;
    private static List<InstalledApp>? _windowsApps;
    private static List<(string Path, string Name, bool IsFolder)>? _files;
    private static DateTime _filesAt;
    private static Task? _filesTask;

    private readonly TextBox _input = new();
    private readonly Border _bar = new();
    private readonly TextBlock _placeholder = Ui.Text("Games, apps, settings, files, store pages…", 22, "SubtleBrush");
    private readonly StackPanel _results = new() { Margin = new Thickness(0, 6, 14, 24) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
    private readonly DispatcherTimer _soon = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private CancellationTokenSource _storeCts = new();
    private string _query = "";
    private StackPanel? _storeSection;

    private readonly bool _settingsOnly;

    /// <param name="settingsOnly">Settings' "Find a setting": only settings are searched.</param>
    public SearchScreen(bool settingsOnly = false)
    {
        _settingsOnly = settingsOnly;
        if (settingsOnly) _placeholder.Text = "Wallpaper, vibration, sounds, Wi-Fi\u2026";
        // The big search box.
        var grid = new Grid { Height = 60 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glass = Ui.Text("", 22, "AccentBrush");
        glass.FontFamily = Ui.IconFont;
        glass.HorizontalAlignment = HorizontalAlignment.Center;
        glass.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(glass);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.IsHitTestVisible = false;
        _placeholder.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(_placeholder, 1);
        grid.Children.Add(_placeholder);
        _input.Background = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        _input.FontSize = 22;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        _input.FocusVisualStyle = null;
        _input.SetResourceReference(Control.ForegroundProperty, "FgBrush");
        _input.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        Grid.SetColumn(_input, 1);
        grid.Children.Add(_input);
        _bar.Child = grid;
        _bar.BorderThickness = new Thickness(3);
        _bar.Margin = new Thickness(0, 0, 14, 10);
        _bar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _bar.SetResourceReference(Border.CornerRadiusProperty, "RowCorner");
        _bar.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        _input.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _soon.Stop();
            _soon.Start();
        };
        _input.GotKeyboardFocus += (_, _) => _bar.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _input.LostKeyboardFocus += (_, _) => _bar.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _soon.Tick += (_, _) => { _soon.Stop(); Run(_input.Text.Trim()); };

        _scroll.Content = _results;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_bar);
        Grid.SetRow(_scroll, 1);
        body.Children.Add(_scroll);
        SetPage(settingsOnly ? "Find a setting" : "Search", body, "{Select}  Open      {Details}  More      {Back}  Back", wide: true);
    }

    public override void OnShown()
    {
        Run("");
        _ = WarmUpAsync();
        FocusLater(_input);
        // With a controller, go straight to the on-screen keyboard.
        if (!Hints.Keyboard) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ = TypeAsync()));
    }

    public override void OnClosed()
    {
        _soon.Stop();
        _storeCts.Cancel();
    }

    public override void FocusDefault() => _input.Focus();

    public override bool OnAction(NavAction action)
    {
        if (_input.IsKeyboardFocused)
        {
            if (action == NavAction.Accept)
            {
                if (Hints.Keyboard) FirstResult()?.Focus();
                else _ = TypeAsync();
                return true;
            }
            if (action == NavAction.Back && _input.Text.Length > 0) { _input.Text = ""; return true; }
        }
        if (action == NavAction.Y) { _input.Focus(); _ = TypeAsync(); return true; } // type again from anywhere
        if (action == NavAction.X && System.Windows.Input.Keyboard.FocusedElement is Button { Tag: Func<Task> more })
        {
            _ = more();
            return true;
        }
        return false;
    }

    private Button? FirstResult() => _results.FindDescendants<Button>().FirstOrDefault();

    private async Task TypeAsync()
    {
        // Windows' touch keyboard (Settings > Controller > Typing): type straight into the search box, no pop-up.
        if (TouchKeyboard.UseWindows)
        {
            _input.Focus();
            _input.CaretIndex = _input.Text.Length;
            await TouchKeyboard.ShowAsync();
            return;
        }
        var text = await Host.PromptAsync(_settingsOnly ? "Find a setting" : "Search everything",
            _settingsOnly ? "Type part of a setting's name" : "Games, apps, settings, files and store pages", _input.Text, "Search");
        if (text == null) return;
        _input.Text = text.Trim();
        _soon.Stop();
        Run(_input.Text);
        FocusLater(FirstResult() ?? (UIElement)_input);
    }

    /// <summary>Gathers the slow lists in the background the first time Search is opened.</summary>
    private async Task WarmUpAsync()
    {
        _settingsIndex ??= SettingsScreen.BuildIndex(Host);
        if (_settingsOnly) { if (_query.Length > 0 && Host.IsTop(this)) Run(_query, keepFocus: true); return; }
        if (_windowsApps == null)
        {
            try { _windowsApps = await InstalledApps.ScanAsync(); }
            catch (Exception) { _windowsApps = new(); }
            if (_query.Length > 0 && Host.IsTop(this)) Run(_query, keepFocus: true);
        }
        if (_files == null || DateTime.UtcNow - _filesAt > TimeSpan.FromMinutes(10))
        {
            _filesTask ??= Task.Run(IndexFiles);
            await _filesTask;
            _filesTask = null;
            if (_query.Length > 0 && Host.IsTop(this)) Run(_query, keepFocus: true);
        }
    }

    /// <summary>Names of files and folders in your own folders (not the whole drive), up to a limit.</summary>
    private static void IndexFiles()
    {
        var list = new List<(string, string, bool)>(20000);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(home, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        void Walk(string folder, int depth)
        {
            if (list.Count >= 40000) return;
            try
            {
                foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
                {
                    bool dir = (info.Attributes & FileAttributes.Directory) != 0;
                    list.Add((info.FullName, info.Name, dir));
                    if (dir && depth < 5 && !info.Name.StartsWith('.') && info.Name != "node_modules") Walk(info.FullName, depth + 1);
                    if (list.Count >= 40000) return;
                }
            }
            catch (Exception) { }
        }
        foreach (var root in roots) Walk(root, 0);
        _files = list;
        _filesAt = DateTime.UtcNow;
    }

    /// <summary>0 = starts with the words, 1 = a word starts with them, 2 = contains them, -1 = no match.</summary>
    private static int Score(string name, string query)
    {
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 0;
        int at = name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (at < 0)
        {
            // All the words, in any order.
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 && words.All(w => name.Contains(w, StringComparison.CurrentCultureIgnoreCase)) ? 3 : -1;
        }
        return !char.IsLetterOrDigit(name[at - 1]) ? 1 : 2;
    }

    private static IEnumerable<T> Rank<T>(IEnumerable<T> items, Func<T, string> name, string query) =>
        items.Select(i => (Item: i, Score: Score(name(i), query))).Where(x => x.Score >= 0)
            .OrderBy(x => x.Score).ThenBy(x => name(x.Item).Length).Select(x => x.Item);

    // ================================================================ results

    private void Run(string query, bool keepFocus = false)
    {
        _query = query;
        var focusedTag = keepFocus ? (System.Windows.Input.Keyboard.FocusedElement as Button)?.DataContext as string : null;
        _results.Children.Clear();
        _storeCts.Cancel();
        _storeSection = null;

        if (query.Length == 0)
        {
            ShowRecent();
            return;
        }

        int found = 0;
        if (_settingsOnly)
        {
            if (_settingsIndex == null) { AddNote("Getting the list of settings ready\u2026"); return; }
            var only = Rank(_settingsIndex, x => x.Label, query)
                .Concat(_settingsIndex.Where(x => x.Subtitle != null && x.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .Distinct().Take(30).ToList();
            if (only.Count == 0) { AddNote($"No setting matches \u201C{query}\u201D."); return; }
            var list = Section("Settings", only.Count);
            foreach (var st in only)
            {
                var x = st;
                list.Children.Add(ResultRow(x.Label, x.Page + (x.Subtitle != null ? "  \u00B7  " + x.Subtitle : ""), x.Glyph, null,
                    () => { Remember(); Host.Push(x.Open()); }, "set:" + x.Page + "/" + x.Label));
            }
            Ui.GroupRows(list);
            return;
        }

        // Games: everything in the library, installed first.
        var everything = Host.AllGames.Concat(Host.NotInstalledGames).Concat(Host.WishlistGames)
            .GroupBy(g => g.Key).Select(g => g.First()).ToList();
        var games = Rank(everything, g => g.Name, query).OrderBy(g => g.IsInstalled ? 0 : 1).Take(MaxGames).ToList();
        if (games.Count > 0)
        {
            found += games.Count;
            var row = Section("Games", games.Count);
            var wrap = new WrapPanel();
            Host.EnsureCovers(games);
            foreach (var g in games) wrap.Children.Add(GameTile(g));
            row.Children.Add(wrap);
        }

        // Apps: added to SquareDeck, then the rest of the Start menu.
        var mine = Rank(Host.Apps, a => a.Name, query).Take(MaxApps).ToList();
        var others = _windowsApps == null ? new List<InstalledApp>()
            : Rank(_windowsApps.Where(w => !mine.Any(m => string.Equals(m.Name, w.Name, StringComparison.OrdinalIgnoreCase))), a => a.Name, query)
                .Take(Math.Max(0, MaxApps - mine.Count)).ToList();
        if (mine.Count + others.Count > 0)
        {
            found += mine.Count + others.Count;
            var section = Section("Apps", mine.Count + others.Count);
            foreach (var a in mine)
            {
                var app = a;
                section.Children.Add(ResultRow(app.Name, "Added to SquareDeck", null, app.Cover, () => { Remember(); _ = Host.ActivateAsync(app); }, "app:" + app.Key));
            }
            foreach (var w in others)
            {
                var app = w;
                var row = ResultRow(app.Name, "App", null, null, () => { Remember(); InstalledApps.Launch(app.AppId); }, "win:" + app.AppId);
                LoadIcon(row, () => ShellThumb.ForApp(app.AppId, 64));
                section.Children.Add(row);
            }
        }

        // Settings.
        if (_settingsIndex != null)
        {
            var settings = Rank(_settingsIndex, s => s.Label, query)
                .Concat(_settingsIndex.Where(s => s.Subtitle != null && s.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .Distinct().Take(MaxSettings).ToList();
            if (settings.Count > 0)
            {
                found += settings.Count;
                var section = Section("Settings", settings.Count);
                foreach (var st in settings)
                {
                    var s = st;
                    string where = s.Page == "Settings" ? "Settings" : "Settings  ›  " + s.Page;
                    section.Children.Add(ResultRow(s.Label, where + (s.Subtitle != null ? "  ·  " + s.Subtitle : ""), s.Glyph, null,
                        () => { Remember(); Host.Push(s.Open()); }, "set:" + s.Page + "/" + s.Label));
                }
            }
        }

        // Files and folders in your own folders.
        if (_files != null)
        {
            var files = Rank(_files, f => f.Name, query).Take(MaxFiles).ToList();
            if (files.Count > 0)
            {
                found += files.Count;
                var section = Section("Files", files.Count);
                foreach (var f in files)
                {
                    var file = f;
                    string folder = Path.GetDirectoryName(file.Path) ?? "";
                    var row = ResultRow(file.Name, folder, file.IsFolder ? "" : null, null, () =>
                    {
                        Remember();
                        if (file.IsFolder) Host.Push(new ExplorerScreen(file.Path));
                        else ProcessUtil.Open(file.Path);
                    }, "file:" + file.Path, more: () => { Host.Push(new ExplorerScreen(folder)); return Task.CompletedTask; });
                    if (!file.IsFolder) LoadIcon(row, () => ShellThumb.Get(file.Path, 64));
                    section.Children.Add(row);
                }
            }
        }
        else AddNote("Still reading the names of your files… they'll appear here in a moment.");

        // Steam store (filled in when it answers).
        _storeSection = Section("Steam store", null);
        var waiting = Ui.Text("Searching the Steam store…", 15, "SubtleBrush");
        waiting.Margin = new Thickness(6, 0, 0, 6);
        _storeSection.Children.Add(waiting);
        _ = SearchStoreAsync(query);

        // Elsewhere.
        var more = Section("More", null);
        more.Children.Add(ResultRow($"Search the Microsoft Store for “{query}”", "Apps and games from Microsoft", "", null,
            () => { Remember(); Host.Push(new StoreSearchScreen(query)); }, "ms"));
        more.Children.Add(ResultRow($"Search the web for “{query}”", "Opens in the built-in browser", "", null,
            () => { Remember(); Host.Push(BrowserScreen.Open(Host, "https://www.bing.com/search?q=" + Uri.EscapeDataString(query))); }, "web"));
        Ui.GroupRows(more);

        if (found == 0)
        {
            var none = Ui.Text($"Nothing on this PC matches “{query}”. The store results below may have it.", 17, "SubtleBrush");
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(6, 4, 0, 4);
            _results.Children.Insert(0, none);
        }

        foreach (var section in _results.Children.OfType<StackPanel>()) Ui.GroupRows(section);
        if (focusedTag != null && _results.FindDescendants<Button>().FirstOrDefault(b => b.DataContext as string == focusedTag) is { } again)
            FocusLater(again);
    }

    private void ShowRecent()
    {
        var recent = Host.Settings.RecentSearches;
        if (recent.Count == 0)
        {
            AddNote("Type the name of a game, an app, a setting (“wallpaper”, “vibration”), a file, or something to find in the Steam store.");
            return;
        }
        var section = Section("Recent searches", null);
        foreach (var r in recent)
        {
            string q = r;
            section.Children.Add(ResultRow(q, null, "", null, () => { _input.Text = q; _soon.Stop(); Run(q); FocusLater(FirstResult()); }, "recent:" + q));
        }
        section.Children.Add(ResultRow("Clear recent searches", null, "", null, () =>
        {
            Host.Settings.RecentSearches.Clear();
            Host.Settings.Save();
            Run("");
            FocusLater(_input);
        }, "recent-clear"));
        Ui.GroupRows(section);
    }

    /// <summary>Keeps the search in Recent searches once something from it was opened.</summary>
    private void Remember()
    {
        if (_query.Length == 0) return;
        var list = Host.Settings.RecentSearches;
        list.RemoveAll(x => string.Equals(x, _query, StringComparison.CurrentCultureIgnoreCase));
        list.Insert(0, _query);
        if (list.Count > 8) list.RemoveRange(8, list.Count - 8);
        Host.Settings.Save();
    }

    private async Task SearchStoreAsync(string query)
    {
        _storeCts = new CancellationTokenSource();
        var ct = _storeCts.Token;
        try { await Task.Delay(350, ct); } catch (OperationCanceledException) { return; }
        var items = await SteamStoreClient.SearchStoreAsync(query, ct);
        if (ct.IsCancellationRequested || _storeSection == null || !_results.Children.Contains(_storeSection)) return;
        var section = _storeSection;
        while (section.Children.Count > 1) section.Children.RemoveAt(1);
        if (items.Count == 0)
        {
            var none = Ui.Text("No results from the Steam store (or it couldn't be reached).", 15, "SubtleBrush");
            none.Margin = new Thickness(6, 0, 0, 6);
            section.Children.Add(none);
            return;
        }
        var owned = new HashSet<int>(Host.AllGames.Concat(Host.NotInstalledGames).Where(g => g.IsSteam).Select(g => g.AppId));
        foreach (var (appId, name, price, image) in items.Take(MaxStore))
        {
            int id = appId;
            string detail = owned.Contains(id) ? "In your library" : price.Length > 0 ? price : "Steam store";
            var row = ResultRow(name, detail, null, null, () => { Remember(); ProcessUtil.Open("steam://store/" + id); }, "store:" + id);
            if (image.Length > 0) _ = LoadWebIconAsync(row, image);
            section.Children.Add(row);
        }
        Ui.GroupRows(section);
    }

    // ================================================================ building blocks

    private StackPanel Section(string title, int? count)
    {
        var section = new StackPanel();
        var header = Ui.Header(count is int n ? $"{title}  ({n})" : title);
        header.Margin = new Thickness(6, _results.Children.Count == 0 ? 4 : 22, 0, 8);
        section.Children.Add(header);
        _results.Children.Add(section);
        return section;
    }

    private void AddNote(string text)
    {
        var note = Ui.Text(text, 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(6, 10, 0, 6);
        _results.Children.Add(note);
    }

    private Button GameTile(Game g)
    {
        var box = GridScreen.ImageBox(150, 150, out var cover);
        cover.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = g });
        if (g.UsesIcon && !g.IconFills) { cover.Stretch = Stretch.Uniform; cover.Margin = new Thickness(36); if (g.TileBackground != null) box.Background = g.TileBackground; }
        var stack = new StackPanel { Width = 150 };
        stack.Children.Add(box);
        var name = Ui.Text(g.Name, 14, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(2, 6, 2, 0);
        stack.Children.Add(name);
        stack.Children.Add(Ui.Text(g.IsRunning ? "Running" : g.IsInstalled ? g.SourceName : g.OnWishlist ? "Wishlist" : "Not installed", 12, "SubtleBrush"));
        var tile = Ui.Tile(stack);
        tile.Margin = new Thickness(0, 0, 6, 6);
        tile.DataContext = "game:" + g.Key;
        tile.Click += (_, _) => { Remember(); _ = Host.ActivateAsync(g); };
        tile.Tag = (Func<Task>)(() => { Remember(); Host.Push(new DetailsScreen(g)); return Task.CompletedTask; });
        return tile;
    }

    private Button ResultRow(string label, string? subtitle, string? glyph, ImageSource? picture, Action click, string id, Func<Task>? more = null)
    {
        Image? icon = null;
        if (glyph == null)
        {
            icon = new Image { Source = picture };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        }
        var row = Ui.Row(label, null, glyph, icon, subtitle);
        row.DataContext = id;
        row.Tag = more;
        row.Click += (_, _) => click();
        return row;
    }

    private static Image? IconOf(Button row) => row.FindDescendants<Image>().FirstOrDefault();

    private static void LoadIcon(Button row, Func<System.Windows.Media.Imaging.BitmapSource?> load)
    {
        _ = Task.Run(() =>
        {
            var image = load();
            if (image != null && !image.IsFrozen && image.CanFreeze) image.Freeze();
            return image;
        }).ContinueWith(t => { if (t.Result != null && IconOf(row) is { } view) view.Source = t.Result; },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static async Task LoadWebIconAsync(Button row, string url)
    {
        try
        {
            var image = await Task.Run(() => ImageLoader.FromUrlAsync(url, 120));
            if (image != null && IconOf(row) is { } view) view.Source = image;
        }
        catch (Exception) { }
    }
}
