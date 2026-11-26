using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Switch 2 style Library": the game library laid out the way the Switch 2 lays out System Settings.
/// The sections on the left in plain type, the one you're in blue with a bar beside it, and a storage gauge under
/// them like the Switch's System Memory bar. The games on the right as flat rows on thin lines, each with its small
/// cover, its store and facts in gray and the play time in blue, under bar-and-name headings (Today, This week... or
/// A, B, C...). The highlight is the soft card with the rim, and every color follows the UI style, as Settings does.
///
/// Controls: up / down move, moving through the sections shows each straight away, right goes into the games and left
/// back to the sections. A plays, X opens the game's page, Y its options, LT / RT change section, View changes how the games are shown (rows, compact list, square icons, tall covers,
/// banners or spotlight), Sort by changes the
/// order, and B goes from the games back to the sections, then out.
/// </summary>
public sealed class Switch2LibraryScreen : Screen
{
    public static bool On => Labs.On("newlibrary");

    /// <summary>The library page in the look chosen in Experimental.</summary>
    public static Screen Create() => On ? new Switch2LibraryScreen() : new LibraryScreen();

    private const int MaxShown = 400;

    private sealed record Section(string Id, string Name, string Group, Func<Game, bool> Matches);

    private static readonly (string Id, string Name)[] Sorts =
        { ("recent", "Recently played"), ("name", "Name"), ("playtime", "Play time"), ("size", "Size") };

    /// <summary>How the games are shown (Show as, or the View button).</summary>
    public static readonly (string Id, string Name, string About)[] Views =
    {
        ("rows", "Rows", "A row per game: small cover, facts and play time"),
        ("compact", "Compact list", "Just the names and play time, to see many at once"),
        ("square", "Square icons", "The square covers in a grid, as on the Home screen"),
        ("tall", "Tall covers", "Tall poster art in a grid"),
        ("wide", "Banners", "Wide store banners, two or three to a row"),
        ("spotlight", "Spotlight", "The highlighted game large across the top, square icons below"),
    };

    private bool GridView => _view is "square" or "tall" or "wide" or "spotlight";

    /// <summary>A cover on the page: bound to its picture only near the screen.</summary>
    private sealed class CoverSlot
    {
        public required Border Box;
        public required Image Picture;
        public required Game Game;
        public string Kind = "square"; // square | tall | wide
        public bool Bound;
    }

    /// <summary>Wide banner art fetched this session, by game (tall art is kept on the game itself).</summary>
    private static readonly Dictionary<string, ImageSource?> BannerArt = new();

    private readonly StackPanel _nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 0, 18, 30) };
    private readonly ScrollViewer _navScroll;
    private readonly ScrollViewer _rowScroll;
    private readonly Border _storage = new() { Margin = new Thickness(10, 6, 10, 6) };
    private readonly System.Windows.Threading.DispatcherTimer _showSoon = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private readonly System.Windows.Threading.DispatcherTimer _realizeSoon = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly List<CoverSlot> _covers = new();
    private readonly Border _spotlight = new() { Margin = new Thickness(6, 14, 6, 4), Height = 230, CornerRadius = new CornerRadius(14), ClipToBounds = true };
    private CancellationTokenSource? _artCts;
    private readonly SemaphoreSlim _artGate = new(3);
    private string _view = "rows";

    private List<Section> _sections = new();
    private string _section = "all";
    private string _sort = "recent";
    private string? _lastGame;
    private Button? _pendingNav;

    public Switch2LibraryScreen()
    {
        _navScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _nav
        };
        _rowScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _rows
        };
        _rowScroll.ScrollChanged += (_, _) => RealizeSoon();
        _rowScroll.SizeChanged += (_, e) =>
        {
            RealizeSoon();
            // Banners are sized to fit the row: a new width lays them out again.
            if (GridView && e.WidthChanged && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 40 && Host?.IsTop(this) == true)
                Dispatcher.BeginInvoke(new Action(() => ShowSection(_section, focusRows: _rows.IsKeyboardFocusWithin)));
        };
        _realizeSoon.Tick += (_, _) => Realize();
        _showSoon.Tick += (_, _) =>
        {
            _showSoon.Stop();
            if (_pendingNav?.DataContext is string id && id != _section) ShowSection(id, focusRows: false);
        };

        // Left: the sections over the storage gauge. Right: the games. A thin line between, as on the Switch 2.
        var left = new Grid { Width = 272 };
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(_navScroll);
        Grid.SetRow(_storage, 1);
        left.Children.Add(_storage);

        var divider = new Rectangle { Width = 1, Margin = new Thickness(8, 18, 18, 18) };
        divider.SetResourceReference(Shape.FillProperty, "S2Line");

        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(left);
        Grid.SetColumn(divider, 1);
        body.Children.Add(divider);
        Grid.SetColumn(_rowScroll, 2);
        body.Children.Add(_rowScroll);

        SetPage("Library", body,
            "{Select}  Play      {Details}  Details      {Options}  Options      LT / RT  Section      View  Show as      {Back}  Back", wide: true);
        UseSwitch2Chrome("Library", "\uE8F1");
        // A light tick on the controller as the highlight moves, as in Settings (if vibration is on there).
        AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (Host?.Settings.RumbleInSettings == true && e.NewFocus is Button && e.OldFocus is Button && !ReferenceEquals(e.NewFocus, e.OldFocus))
                Host.Rumble(16);
        }));
    }

    // ---------------------------------------------------------------- life

    public override void OnShown()
    {
        _sort = Sorts.Any(s => s.Id == Labs.Get("newlibrary.sort", "recent")) ? Labs.Get("newlibrary.sort", "recent") : "recent";
        _view = Views.Any(v => v.Id == Labs.Get("newlibrary.view", "rows")) ? Labs.Get("newlibrary.view", "rows") : "rows";
        Host.LibraryChanged += OnLibraryChanged;
        Host.ApplyCachedLists();
        Rebuild(focusRows: true);
    }

    public override void OnResumed()
    {
        RefreshSwitch2Colors(); // a UI style may have been chosen meanwhile
        Rebuild(focusRows: true);
    }

    public override void OnClosed()
    {
        Host.LibraryChanged -= OnLibraryChanged;
        _showSoon.Stop();
        _realizeSoon.Stop();
        _artCts?.Cancel();
    }

    private void OnLibraryChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (Host.IsTop(this)) Rebuild(focusRows: _rows.IsKeyboardFocusWithin);
    }));

    // ---------------------------------------------------------------- what's in the library

    private List<Game> Everything() => Host.AllGames.Concat(Host.NotInstalledGames).Concat(Host.WishlistGames)
        .Where(g => g.IsGame).GroupBy(g => g.Key).Select(x => x.First()).ToList();

    private double Minutes(Game g) => g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : Host.Playtime.TotalSeconds(g.Key) / 60;

    private static bool Played(DateTime when) => when > DateTime.MinValue.AddYears(1) && when < DateTime.Now.AddDays(1);

    private List<Section> BuildSections(List<Game> everything)
    {
        var list = new List<Section>
        {
            new("all", "All software", "Library", g => g.IsInstalled),
            new("recent", "Recently played", "Library", g => g.IsInstalled && Played(g.RecentAt) && g.RecentAt > DateTime.Now.AddDays(-30)),
            new("favorites", "Favorites", "Library", g => g.IsFavorite),
            new("updates", "Updates", "Library", g => g.IsInstalled && g.HasUpdate),
        };
        foreach (var store in everything.Where(g => g.IsInstalled).Select(g => g.SourceName).Where(s => s.Length > 0)
                     .Distinct().OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            list.Add(new("src:" + store, store, "Stores", g => g.IsInstalled && g.SourceName == store));
        foreach (var c in Host.Library.Collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var keys = new HashSet<string>(c.Games);
            list.Add(new("col:" + c.Id, c.Name, "Collections", g => keys.Contains(g.Key)));
        }
        list.Add(new("notinstalled", "Not installed", "More", g => !g.IsInstalled && !g.OnWishlist));
        list.Add(new("wishlist", "Wishlist", "More", g => g.OnWishlist));
        return list;
    }

    // ---------------------------------------------------------------- building

    private void Rebuild(bool focusRows)
    {
        var everything = Everything();
        _sections = BuildSections(everything);
        if (_sections.All(s => s.Id != _section)) _section = "all";
        BuildNav(everything);
        BuildStorage(everything);
        ShowSection(_section, focusRows);
    }

    private void BuildNav(List<Game> everything)
    {
        _nav.Children.Clear();
        string? group = null;
        foreach (var s in _sections)
        {
            if (group != null && s.Group != group)
            {
                var line = new Rectangle { Height = 1, Margin = new Thickness(40, 8, 18, 8) };
                line.SetResourceReference(Shape.FillProperty, "S2Line");
                _nav.Children.Add(line);
            }
            group = s.Group;
            int count = everything.Count(s.Matches);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = s.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            if (count > 0)
            {
                var n = Ui.Text(count.ToString("N0"), 16, "SubtleBrush");
                n.VerticalAlignment = VerticalAlignment.Center;
                n.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(n, 1);
                grid.Children.Add(n);
            }
            var button = new Button { Style = Switch2Settings.NavStyle, Content = grid, DataContext = s.Id, Tag = s.Id == _section ? "selected" : null };
            button.GotKeyboardFocus += (_, _) =>
            {
                // Moving through the sections shows each one, once the highlight settles.
                _pendingNav = button;
                _showSoon.Stop();
                _showSoon.Start();
            };
            button.Click += (_, _) =>
            {
                _showSoon.Stop();
                if (s.Id != _section) ShowSection(s.Id, focusRows: true);
                else FocusRows();
            };
            _nav.Children.Add(button);
        }
    }

    /// <summary>
    /// Under the sections: how much room the games take, as the Switch shows System Memory. The bar is the drives the
    /// games are installed on: the games in the highlight color, everything else on them in gray, the free space empty.
    /// </summary>
    private void BuildStorage(List<Game> everything)
    {
        var installed = everything.Where(g => g.IsInstalled).ToList();
        long games = installed.Sum(g => Math.Max(0, g.SizeOnDisk));
        long total = 0, free = 0;
        try
        {
            var roots = installed.Select(g => g.InstallDir).Where(d => !string.IsNullOrEmpty(d))
                .Select(d => System.IO.Path.GetPathRoot(d!)).Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var drive = new DriveInfo(root!);
                if (!drive.IsReady) continue;
                total += drive.TotalSize;
                free += drive.AvailableFreeSpace;
            }
        }
        catch (Exception) { }

        var panel = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Ui.Text("Storage", 16, "FgBrush"));
        var used = Ui.Text(Size(games) + " in games", 16, "AccentBrush");
        used.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(used);
        panel.Children.Add(head);

        if (total > 0)
        {
            double width = 252;
            double gameShare = Math.Clamp((double)games / total, 0, 1);
            double otherShare = Math.Clamp((double)(total - free - games) / total, 0, 1 - gameShare);
            var track = new Grid { Height = 12, Width = width, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            track.Clip = new RectangleGeometry(new Rect(0, 0, width, 12), 6, 6);
            var back = new Border { Background = new SolidColorBrush(Switch2Settings.Off) { Opacity = 0.55 } };
            track.Children.Add(back);
            var bars = new StackPanel { Orientation = Orientation.Horizontal };
            var mine = new Border { Width = Math.Max(gameShare > 0 ? 4 : 0, width * gameShare) };
            mine.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bars.Children.Add(mine);
            bars.Children.Add(new Border { Width = width * otherShare, Background = new SolidColorBrush(Switch2Settings.Subtle) { Opacity = 0.6 } });
            track.Children.Add(bars);
            panel.Children.Add(track);
            panel.Children.Add(Ui.Text($"{Size(free)} free of {Size(total)}", 14, "SubtleBrush"));
            ((TextBlock)panel.Children[^1]).Margin = new Thickness(0, 8, 0, 0);
        }
        _storage.Child = panel;
        _storage.Visibility = games > 0 || total > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Size(long bytes) =>
        bytes >= 1L << 40 ? $"{bytes / (double)(1L << 40):0.#} TB" : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private void ShowSection(string id, bool focusRows)
    {
        _section = id;
        foreach (var b in _nav.Children.OfType<Button>()) b.Tag = Equals(b.DataContext, id) ? "selected" : null;
        var section = _sections.FirstOrDefault(s => s.Id == id) ?? _sections[0];

        IEnumerable<Game> games = Everything().Where(section.Matches);
        string sort = id == "recent" ? "recent" : _sort;
        games = sort switch
        {
            "name" => games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            "playtime" => games.OrderByDescending(Minutes).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            "size" => games.OrderByDescending(g => g.SizeOnDisk).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => games.OrderByDescending(g => Played(g.RecentAt) ? g.RecentAt : DateTime.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var list = games.Take(MaxShown).ToList();

        _rows.Children.Clear();
        _covers.Clear();
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        _rowScroll.ScrollToTop();

        // The section's own title, with how many games and the order they're in.
        var title = new Grid { Margin = new Thickness(14, 22, 14, 4) };
        title.Children.Add(Ui.Text(section.Name, 28, "FgBrush"));
        var count = Ui.Text(list.Count == 0 ? "" : list.Count == 1 ? "1 game" : $"{list.Count:N0} games", 18, "SubtleBrush");
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Bottom;
        title.Children.Add(count);
        _rows.Children.Add(title);

        // Show as and Sort by side by side on one line: one from the left edge, the other from the middle.
        var options = new UniformGrid { Rows = 1, Columns = 2 };
        options.Children.Add(SettingRow("Show as", Views.First(v => v.Id == _view).Name, () => _ = ChooseViewAsync()));
        if (id != "recent") options.Children.Add(SettingRow("Sort by", Sorts.First(s => s.Id == _sort).Name, CycleSort));
        _rows.Children.Add(options);
        if (_view == "spotlight" && list.Count > 0)
        {
            _rows.Children.Add(_spotlight);
            ShowSpotlight(list.FirstOrDefault(g => g.Key == _lastGame) ?? list[0]);
        }

        if (list.Count == 0)
        {
            var empty = Ui.Text(id switch
            {
                "favorites" => "No favorites yet. Press Y on a game and choose Favorite to add one.",
                "updates" => "Everything's up to date.",
                "recent" => "Nothing played in the last 30 days.",
                "wishlist" => "Your Steam wishlist is empty, or Steam isn't connected.",
                _ when id.StartsWith("col:") => "This collection is empty. Press Y on a game to add it.",
                _ => "No games here."
            }, 18, "SubtleBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(26, 30, 26, 0);
            _rows.Children.Add(empty);
        }

        string? heading = null;
        WrapPanel? shelf = null;
        foreach (var g in list)
        {
            string? next = sort switch
            {
                "recent" => When(g),
                "name" when !GridView || list.Count > 24 => Letter(g),
                _ => null
            };
            if (next != null && next != heading)
            {
                heading = next;
                _rows.Children.Add(Switch2Settings.Header(next));
                shelf = null;
            }
            if (GridView)
            {
                if (shelf == null) { shelf = new WrapPanel { Margin = new Thickness(4, 4, 0, 6) }; _rows.Children.Add(shelf); }
                shelf.Children.Add(GameTile(g));
            }
            else _rows.Children.Add(_view == "compact" ? CompactRow(g) : GameRow(g));
        }
        if (list.Count == MaxShown)
            _rows.Children.Add(new TextBlock { Text = $"Showing the first {MaxShown}.", Margin = new Thickness(26, 18, 0, 0) }
                .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush")));

        if (id is "notinstalled" or "wishlist")
        {
            var missing = list.Where(g => g.Cover == null && g.IsSteam).Take(80).ToList();
            if (missing.Count > 0) Host.EnsureCovers(missing);
        }
        Dispatcher.BeginInvoke(new Action(Realize), System.Windows.Threading.DispatcherPriority.Loaded);
        if (focusRows) FocusRows();
    }

    private static string When(Game g)
    {
        if (!Played(g.RecentAt)) return "Not played yet";
        var days = (DateTime.Today - g.RecentAt.Date).TotalDays;
        return days < 1 ? "Today" : days < 2 ? "Yesterday" : days < 7 ? "This week" : days < 31 ? "This month" : days < 365 ? "This year" : "Earlier";
    }

    private static string Letter(Game g)
    {
        char c = g.Name.Length > 0 ? char.ToUpperInvariant(g.Name[0]) : '#';
        return char.IsLetter(c) ? c.ToString() : "#";
    }

    /// <summary>A plain settings row: the name on the left, its value in blue on the right.</summary>
    private Button SettingRow(string name, string value, Action click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var v = Ui.Text(value, 19, "AccentBrush");
        v.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        var button = new Button { Style = Switch2Settings.RowStyle, Content = grid, DataContext = "setting:" + name };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A game: its small cover, the name, its store and facts in gray, small labels, and the play time in blue.</summary>
    private Button GameRow(Game g)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = GridScreen.ImageBox(60, 60, out var cover);
        if (g.UsesIcon && !g.IconFills) { cover.Stretch = Stretch.Uniform; cover.Margin = new Thickness(10); if (g.TileBackground != null) box.Background = g.TileBackground; }
        box.VerticalAlignment = VerticalAlignment.Center;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        if (!g.IsInstalled && !g.OnWishlist) box.Opacity = 0.55;
        grid.Children.Add(box);
        _covers.Add(new CoverSlot { Box = box, Picture = cover, Game = g });

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        words.Children.Add(new TextBlock { Text = g.Name, FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis });
        var facts = new List<string> { g.IsInstalled ? g.SourceName : g.OnWishlist ? "Wishlist" + (g.Price.Length > 0 ? "  ·  " + g.Price : "") : "Not installed" };
        if (Host.CategoryOf(g) is string category) facts.Add(category);
        if (g.SizeOnDisk > 0) facts.Add(Size(g.SizeOnDisk));
        if (Played(g.RecentAt)) facts.Add("Played " + Ui.Ago(g.RecentAt));
        var line = Ui.Text(string.Join("  ·  ", facts), 15, "SubtleBrush");
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        line.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(line);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (g.IsRunning) right.Children.Add(Label("Running", Color.FromRgb(0x2E, 0xB8, 0x72)));
        else if (g.HasUpdate) right.Children.Add(Label("Update", Switch2Settings.Blue));
        if (g.IsFavorite) right.Children.Add(Ui.Glyph("\uE735", 16, "AccentBrush").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 16, 0); }));
        double minutes = Minutes(g);
        string value = _sort == "size" && g.SizeOnDisk > 0 ? Size(g.SizeOnDisk)
            : minutes >= 60 ? $"{minutes / 60:0.#} hrs" : minutes >= 1 ? $"{minutes:0} min" : "";
        if (value.Length > 0)
        {
            var v = Ui.Text(value, 19, "AccentBrush");
            v.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(v);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var button = new Button { Style = Switch2Settings.RowStyle, Content = grid, Tag = g, MinHeight = 84 };
        button.Click += (_, _) => _ = Host.ActivateAsync(g);
        button.GotKeyboardFocus += (_, _) => _lastGame = g.Key;
        return button;
    }

    private string PlayValue(Game g)
    {
        double minutes = Minutes(g);
        return _sort == "size" && g.SizeOnDisk > 0 ? Size(g.SizeOnDisk)
            : minutes >= 60 ? $"{minutes / 60:0.#} hrs" : minutes >= 1 ? $"{minutes:0} min" : "";
    }

    /// <summary>Compact list: a plain settings row per game, the name and the play time in blue, nothing else.</summary>
    private Button CompactRow(Game g)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = g.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (!g.IsInstalled && !g.OnWishlist) name.Opacity = 0.6;
        grid.Children.Add(name);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (g.IsRunning) right.Children.Add(Label("Running", Color.FromRgb(0x2E, 0xB8, 0x72)));
        else if (g.HasUpdate) right.Children.Add(Label("Update", Switch2Settings.Blue));
        if (g.IsFavorite) right.Children.Add(Ui.Glyph("\uE735", 15, "AccentBrush").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 16, 0); }));
        right.Children.Add(Ui.Text(PlayValue(g), 19, "AccentBrush").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var button = new Button { Style = Switch2Settings.RowStyle, Content = grid, Tag = g, MinHeight = 58 };
        button.Click += (_, _) => _ = Host.ActivateAsync(g);
        button.GotKeyboardFocus += (_, _) => _lastGame = g.Key;
        return button;
    }

    /// <summary>
    /// A game in one of the grids: its picture in a card that takes the Switch 2's rimmed highlight, the name under it
    /// (and the play time in blue, for the bigger ones).
    /// </summary>
    private Button GameTile(Game g)
    {
        string kind = _view switch { "tall" => "tall", "wide" => "wide", _ => "square" };
        double cell = CellWidth(_view == "spotlight" ? 6 : 5);
        (double w, double h) = kind switch { "tall" => (cell, Math.Round(cell * 1.5)), "wide" => (WideWidth(), WideWidth() * 215 / 460), _ => (cell, cell) };
        var box = GridScreen.ImageBox(w, h, out var picture);
        if (kind == "square" && g.UsesIcon && !g.IconFills) { picture.Stretch = Stretch.Uniform; picture.Margin = new Thickness(w * 0.18); if (g.TileBackground != null) box.Background = g.TileBackground; }
        if (!g.IsInstalled && !g.OnWishlist) box.Opacity = 0.55;
        _covers.Add(new CoverSlot { Box = box, Picture = picture, Game = g, Kind = kind });

        var art = new Grid();
        art.Children.Add(box);
        string? label = g.IsRunning ? "Running" : g.HasUpdate ? "Update" : null;
        if (label != null)
        {
            var tag = Label(label, g.IsRunning ? Color.FromRgb(0x2E, 0xB8, 0x72) : Switch2Settings.Blue);
            tag.Margin = new Thickness(8);
            tag.HorizontalAlignment = HorizontalAlignment.Left;
            tag.VerticalAlignment = VerticalAlignment.Top;
            art.Children.Add(tag);
        }
        if (g.IsFavorite)
        {
            var star = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                Child = Ui.Glyph("\uE735", 15, "AccentBrush").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; })
            };
            art.Children.Add(star);
        }

        var stack = new StackPanel { Width = w };
        stack.Children.Add(art);
        if (_view != "spotlight")
        {
            stack.Children.Add(new TextBlock { Text = g.Name, FontSize = kind == "wide" ? 17 : 15, Margin = new Thickness(2, 8, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            if (kind != "square" && PlayValue(g) is { Length: > 0 } value)
                stack.Children.Add(Ui.Text(value, 14, "AccentBrush").Also(t => t.Margin = new Thickness(2, 2, 2, 0)));
        }
        var button = new Button { Style = Switch2Pages.TileStyle, Content = stack, Tag = g, ToolTip = g.Name };
        button.Click += (_, _) => _ = Host.ActivateAsync(g);
        button.GotKeyboardFocus += (_, _) =>
        {
            _lastGame = g.Key;
            if (_view == "spotlight") ShowSpotlight(g);
        };
        return button;
    }

    /// <summary>
    /// Square icons and tall covers fit five to a row (six in Spotlight): the picture width is the row shared out, less
    /// each tile's padding (16) and gap (8), kept between a size that stays readable and one that isn't oversized.
    /// </summary>
    private double CellWidth(int across)
    {
        double room = _rowScroll.ActualWidth > 300 ? _rowScroll.ActualWidth - 42 : 900;
        return Math.Clamp(Math.Floor(room / across - 24), 110, 230);
    }

    /// <summary>Banners fit two to a row on a handheld and three on a wider screen.</summary>
    private double WideWidth()
    {
        double room = _rowScroll.ActualWidth > 300 ? _rowScroll.ActualWidth - 40 : 820;
        int across = room >= 1150 ? 3 : 2;
        return Math.Floor(room / across - 22);
    }

    /// <summary>
    /// Spotlight: the highlighted game large across the top, its hero picture faded into the page with the name, its
    /// facts and the play time in blue, as the Switch 2 shows the software you've picked.
    /// </summary>
    private void ShowSpotlight(Game g)
    {
        var grid = new Grid();
        grid.SetResourceReference(Panel.BackgroundProperty, "S2Card");
        var hero = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.9 };
        hero.OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.Black, 0) { StartPoint = new Point(0.15, 0), EndPoint = new Point(0.7, 0) };
        grid.Children.Add(hero);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        var box = GridScreen.ImageBox(170, 170, out var cover);
        cover.Source = g.Cover;
        if (g.UsesIcon && !g.IconFills) { cover.Stretch = Stretch.Uniform; cover.Margin = new Thickness(28); if (g.TileBackground != null) box.Background = g.TileBackground; }
        row.Children.Add(box);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0), MaxWidth = 520 };
        words.Children.Add(new TextBlock { Text = g.Name, FontSize = 30, TextWrapping = TextWrapping.Wrap, MaxHeight = 82, TextTrimming = TextTrimming.CharacterEllipsis });
        var facts = new List<string> { g.IsInstalled ? g.SourceName : g.OnWishlist ? "Wishlist" : "Not installed" };
        if (Host.CategoryOf(g) is string category) facts.Add(category);
        if (g.SizeOnDisk > 0) facts.Add(Size(g.SizeOnDisk));
        words.Children.Add(Ui.Text(string.Join("  ·  ", facts), 16, "SubtleBrush").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
        double minutes = Minutes(g);
        var line = new List<string>();
        if (minutes >= 1) line.Add(minutes >= 60 ? $"{minutes / 60:0.#} hours played" : $"{minutes:0} minutes played");
        if (Played(g.RecentAt)) line.Add("Last played " + Ui.Ago(g.RecentAt));
        if (line.Count > 0) words.Children.Add(Ui.Text(string.Join("  ·  ", line), 17, "AccentBrush").Also(t => t.Margin = new Thickness(0, 10, 0, 0)));
        row.Children.Add(words);
        grid.Children.Add(row);
        _spotlight.Child = grid;

        // The wide banner behind, when there is one (fetched once, then kept).
        if (BannerArt.TryGetValue(g.Actual.Key, out var known)) { hero.Source = known ?? g.Cover; return; }
        hero.Source = g.Cover;
        var slot = new CoverSlot { Box = _spotlight, Picture = hero, Game = g, Kind = "wide", Bound = true };
        _ = FetchArtAsync(slot);
    }

    /// <summary>A small rounded label, as the Switch 2 marks software with an update.</summary>
    private static Border Label(string text, Color color) => new()
    {
        CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 3, 12, 4), Margin = new Thickness(0, 0, 16, 0),
        VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(color),
        Child = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
    };

    // ---------------------------------------------------------------- covers, only near the screen

    private void RealizeSoon()
    {
        _realizeSoon.Stop();
        _realizeSoon.Start();
    }

    private void Realize()
    {
        _realizeSoon.Stop();
        double vh = _rowScroll.ViewportHeight;
        if (vh <= 0) return;
        var near = new Rect(-2000, -vh, 6000, vh * 3);
        var far = new Rect(-2000, -vh * 4, 6000, vh * 9);
        foreach (var c in _covers)
        {
            Rect r;
            try
            {
                if (!c.Box.IsVisible) continue;
                r = c.Box.TransformToAncestor(_rowScroll).TransformBounds(new Rect(c.Box.RenderSize));
            }
            catch (InvalidOperationException) { continue; }
            if (r.IntersectsWith(near))
            {
                if (c.Bound) continue;
                c.Bound = true;
                var known = c.Kind == "tall" ? c.Game.Actual.PortraitCover
                    : c.Kind == "wide" && BannerArt.TryGetValue(c.Game.Actual.Key, out var b) ? b : null;
                if (known != null) { c.Picture.Source = known; continue; }
                // The square cover until the tall or wide art arrives (or for good, if there's none).
                c.Picture.SetBinding(Image.SourceProperty, new Binding(nameof(Game.Cover)) { Source = c.Game });
                bool tried = c.Kind == "tall" ? c.Game.Actual.PortraitTried : c.Kind == "wide" && BannerArt.ContainsKey(c.Game.Actual.Key);
                if (c.Kind != "square" && !tried) _ = FetchArtAsync(c);
            }
            else if (c.Bound && !r.IntersectsWith(far))
            {
                BindingOperations.ClearBinding(c.Picture, Image.SourceProperty);
                c.Picture.Source = null;
                c.Bound = false;
            }
        }
    }

    /// <summary>Tall or wide art from SteamGridDB (or Steam's own banner), a few at a time, kept for the session.</summary>
    private async Task FetchArtAsync(CoverSlot slot)
    {
        var cts = _artCts;
        if (cts == null) return;
        var actual = slot.Game.Actual;
        bool tall = slot.Kind == "tall";
        ImageSource? art = null;
        await _artGate.WaitAsync();
        try
        {
            if (cts.IsCancellationRequested) return;
            if (tall ? actual.PortraitCover is { } p : BannerArt.TryGetValue(actual.Key, out var had) && had is { } h)
                art = tall ? actual.PortraitCover : BannerArt[actual.Key];
            else
            {
                using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
                art = await Task.Run(async () =>
                {
                    var path = tall ? await client.GetPortraitAsync(actual, cts.Token) : await client.GetHeaderAsync(actual, cts.Token);
                    return path == null ? null : (ImageSource?)ImageLoader.FromFile(path, tall ? 360 : 640);
                }, cts.Token);
                if (tall) { actual.PortraitTried = true; if (art != null) actual.PortraitCover = art; }
                else BannerArt[actual.Key] = art;
            }
        }
        catch (Exception) { }
        finally { _artGate.Release(); }
        if (art == null || cts.IsCancellationRequested || !slot.Bound) return;
        BindingOperations.ClearBinding(slot.Picture, Image.SourceProperty);
        slot.Picture.Source = art;
    }

    // ---------------------------------------------------------------- moving around

    private IEnumerable<Button> NavButtons => _nav.Children.OfType<Button>();

    /// <summary>Everything to choose on the right: the setting rows, the game rows and the tiles in the grids.</summary>
    private IEnumerable<Button> RowButtons => _rows.Children.OfType<FrameworkElement>()
        .SelectMany(e => e is Panel p and not Grid ? p.Children.OfType<Button>() : e is Button b ? new[] { b } : Enumerable.Empty<Button>());

    private Button? SelectedNav => NavButtons.FirstOrDefault(b => Equals(b.Tag, "selected")) ?? NavButtons.FirstOrDefault();

    private void FocusRows()
    {
        var target = RowButtons.FirstOrDefault(b => b.Tag is Game g && g.Key == _lastGame)
                     ?? RowButtons.FirstOrDefault(b => b.Tag is Game) ?? RowButtons.FirstOrDefault();
        FocusLater(target ?? SelectedNav);
    }

    public override void FocusDefault() => (RowButtons.FirstOrDefault(b => b.Tag is Game) ?? RowButtons.FirstOrDefault() ?? SelectedNav)?.Focus();

    private void CycleSort()
    {
        int at = Array.FindIndex(Sorts, s => s.Id == _sort);
        _sort = Sorts[(at + 1) % Sorts.Length].Id;
        Labs.Put("newlibrary.sort", _sort);
        ShowSection(_section, focusRows: false);
        FocusLater(RowButtons.FirstOrDefault(b => Equals(b.DataContext, "setting:Sort by")));
    }

    /// <summary>Show as: the ways to lay out the games, the one in use marked.</summary>
    private async Task ChooseViewAsync()
    {
        var options = Views.Select(v => (v.Id == _view ? "\u2713  " : "") + v.Name).Append("Cancel").ToArray();
        int choice = await Host.AskAsync("Show games as", string.Join("\n", Views.Select(v => v.Name + ": " + v.About)), options);
        if (choice < 0 || choice >= Views.Length) return;
        SetView(Views[choice].Id, "setting:Show as");
    }

    private void SetView(string id, string? focus)
    {
        if (id == _view) return;
        _view = id;
        Labs.Put("newlibrary.view", id);
        ShowSection(_section, focusRows: focus == null);
        if (focus != null) FocusLater(RowButtons.FirstOrDefault(b => Equals(b.DataContext, focus)));
    }

    private Rect At(FrameworkElement e)
    {
        try { return e.IsVisible && e.ActualWidth > 0 ? e.TransformToAncestor(_rows).TransformBounds(new Rect(e.RenderSize)) : Rect.Empty; }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>
    /// The next thing on the right, worked out from where things are (rows or grids, scrolled out of sight or not):
    /// up / down to the nearest row above or below and the closest across in it, left / right along the row.
    /// </summary>
    private Button? Next(Button current, NavAction action)
    {
        var here = At(current);
        if (here.IsEmpty) return null;
        var places = RowButtons.Where(b => b != current).Select(b => (Item: b, At: At(b))).Where(x => !x.At.IsEmpty).ToList();
        double cx = here.Left + here.Width / 2, cy = here.Top + here.Height / 2;
        bool wideRow = current.Tag is not Game || current.Style == Switch2Settings.RowStyle;
        if (action is NavAction.Up or NavAction.Down)
        {
            bool down = action == NavAction.Down;
            var beyond = places.Where(x => down ? x.At.Top + x.At.Height / 2 > here.Bottom - 2 : x.At.Top + x.At.Height / 2 < here.Top + 2).ToList();
            if (beyond.Count == 0) return null;
            double edge = down ? beyond.Min(x => x.At.Top) : beyond.Max(x => x.At.Bottom);
            var row = beyond.Where(x => down ? x.At.Top < edge + Math.Max(12, x.At.Height / 2) : x.At.Bottom > edge - Math.Max(12, x.At.Height / 2)).ToList();
            // From a full-width row into a grid: the first tile of the row, as reading starts.
            if (wideRow) return row.OrderBy(x => x.At.Left).First().Item;
            return row.OrderBy(x => Math.Max(0, Math.Max(x.At.Left - cx, cx - x.At.Right))).ThenBy(x => Math.Abs(x.At.Left + x.At.Width / 2 - cx)).First().Item;
        }
        bool right = action == NavAction.Right;
        return places.Where(x => Math.Abs(x.At.Top + x.At.Height / 2 - cy) < here.Height / 2 && (right ? x.At.Left >= here.Right - 2 : x.At.Right <= here.Left + 2))
            .OrderBy(x => right ? x.At.Left - here.Right : here.Left - x.At.Right).Select(x => x.Item).FirstOrDefault();
    }

    private void Go(Button target)
    {
        target.Focus();
        if (ReferenceEquals(target, RowButtons.FirstOrDefault())) _rowScroll.ScrollToTop(); // the section title stays in view
        else target.BringIntoView(new Rect(-10, -60, target.ActualWidth + 20, target.ActualHeight + 90));
    }

    public override bool OnAction(NavAction action)
    {
        var focused = System.Windows.Input.Keyboard.FocusedElement as Button;
        bool inRows = focused != null && _rows.IsAncestorOf(focused);
        bool inNav = focused != null && _nav.Children.Contains(focused);

        switch (action)
        {
            case NavAction.Right when inNav:
                _showSoon.Stop();
                if (_pendingNav?.DataContext is string id && id != _section) ShowSection(id, focusRows: true);
                else FocusRows();
                return true;
            case NavAction.Back when inRows:
                SelectedNav?.Focus();
                return true;
            case NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right when inRows:
            {
                var next = Next(focused!, action);
                if (next != null) Go(next);
                else if (action == NavAction.Left) SelectedNav?.Focus(); // past the left edge: back to the sections
                return true; // at the other edges, nothing happens
            }
            case NavAction.Up or NavAction.Down when inNav:
            {
                var column = NavButtons.ToList();
                int at = column.IndexOf(focused!) + (action == NavAction.Down ? 1 : -1);
                if (at >= 0 && at < column.Count) { column[at].Focus(); column[at].BringIntoView(); }
                return true;
            }
            case NavAction.Left when inNav:
                return true;
            case NavAction.LT or NavAction.RT:
            {
                // The triggers change section (All software, Favorites, each store...).
                var ids = _sections.Select(s => s.Id).ToList();
                int at = Math.Max(0, ids.IndexOf(_section));
                string next = ids[(at + (action == NavAction.RT ? 1 : -1) + ids.Count) % ids.Count];
                UiSound.Page();
                ShowSection(next, focusRows: !inNav);
                if (inNav) FocusLater(NavButtons.FirstOrDefault(b => Equals(b.DataContext, next)));
                return true;
            }
            case NavAction.View:
            {
                // The next way of showing the games, staying on the same game.
                int at = Array.FindIndex(Views, v => v.Id == _view);
                UiSound.Page();
                SetView(Views[(at + 1) % Views.Length].Id, null);
                return true;
            }
            case NavAction.X when focused?.Tag is Game game:
                Host.Push(new DetailsScreen(game));
                return true;
            case NavAction.Y when focused?.Tag is Game game:
                _ = Host.OpenOptionsAsync(game);
                return true;
        }
        return false;
    }
}

internal static class Switch2LibraryExtensions
{
    /// <summary>Sets something up inline and hands it back.</summary>
    public static T Also<T>(this T item, Action<T> setUp) { setUp(item); return item; }
}
