using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// News for the games you play: the latest posts from their Steam pages (patch notes and announcements
/// included), Steam's own news, IGN, PC Gamer, Kotaku, Eurogamer, Nintendo Life, Xbox Wire, and sales (your wishlist first, then the Steam store's specials).
/// The sections sit on one bar at the top; the newest story is featured large, the rest are cards three to a row.
/// </summary>
public sealed class GameNewsScreen : Screen
{
    private const int GamesToFollow = 18, PostsPerGame = 4;
    private static readonly int SteamNewsAppId = 593110; // Steam's own news feed
    private static readonly (string Id, string Name)[] Sections =
        { ("all", "Latest"), ("patches", "Patches"), ("sales", "Sales"), ("announcements", "Announcements"), ("ign", "IGN PC"), ("igngames", "IGN Games"),
          ("pcgamer", "PC Gamer"), ("kotaku", "Kotaku"), ("eurogamer", "Eurogamer"), ("nintendolife", "Nintendo Life"), ("xboxwire", "Xbox Wire") };

    // Kept between visits for a quarter of an hour, so opening News again is instant.
    private static List<(NewsItem Post, Game? Game, string GameName)>? _cachedNews;
    private static List<SaleItem>? _cachedSales;
    private static DateTime _cachedAt;

    private readonly WrapPanel _tabs = new() { Margin = new Thickness(0) }; // eleven tabs: wrap rather than run off the page
    private readonly StackPanel _list = new();
    private readonly TextBlock _note = Ui.Text("", 16, "SubtleBrush");
    private readonly CancellationTokenSource _cts = new();
    private string _section = "all";
    private string? _focusId;
    private bool _loading = true;

    /// <summary>Experimental: Switch 2 style News and Friends. The sections down the left, the stories on the right.</summary>
    private readonly bool _s2 = Switch2Pages.SocialOn;
    private readonly StackPanel _s2Nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly Border _s2Title = new();

    // The news sites' feeds (IGN's PC articles and all games, PC Gamer, Kotaku, Eurogamer, Nintendo Life, Xbox Wire): read only while this page
    // is open, and let go when it closes (never kept between visits).
    private static readonly Dictionary<string, (string Url, string About, string Site)> Feeds = new()
    {
        ["ign"] = (RssFeed.IgnPc, "IGN's latest PC articles", "IGN"),
        ["igngames"] = (RssFeed.IgnGames, "IGN's latest games news", "IGN"),
        ["pcgamer"] = (RssFeed.PcGamer, "PC Gamer's latest news", "PC Gamer"),
        ["kotaku"] = (RssFeed.Kotaku, "Kotaku's latest stories", "Kotaku"),
        ["eurogamer"] = (RssFeed.Eurogamer, "Eurogamer's latest news", "Eurogamer"),
        ["nintendolife"] = (RssFeed.NintendoLife, "Nintendo Life's latest news", "Nintendo Life"),
        ["xboxwire"] = (RssFeed.XboxWire, "the latest from Xbox Wire", "Xbox Wire"),
    };
    private readonly Dictionary<string, List<NewsItem>?> _feeds = new();
    private readonly HashSet<string> _feedsLoading = new();
    private readonly Dictionary<string, string> _feedErrors = new();
    private static bool IsFeed(string section) => Feeds.ContainsKey(section);

    public GameNewsScreen()
    {
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(6, 6, 0, 10);
        var body = new StackPanel { Margin = new Thickness(0, 0, 14, 24) };
        // The sections sit together on one rounded bar; the one showing is filled with the highlight color.
        var bar = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 2, 6)), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Child = _tabs };
        bar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        if (_s2)
        {
            body.Margin = new Thickness(0, 0, 18, 24);
            body.Children.Add(_s2Title);
            _note.Margin = new Thickness(14, 6, 14, 10);
            // Room round the stories, so the highlight's rim and glow aren't cut off at the edge of the scrolling area.
            _list.Margin = new Thickness(14, 8, 6, 0);
        }
        else body.Children.Add(bar);
        body.Children.Add(_note);
        body.Children.Add(_list);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = body
        };
        // Switch 2 style: the sections down the left like System Settings', the stories on the right.
        SetPage("News", _s2 ? Switch2Pages.Shell(_s2Nav, scroll) : scroll, "LB RB / LT RT  Section      {Select}  Open      {Details}  Refresh      {Back}  Back", wide: true);
        if (_s2) UseSwitch2Chrome("News", "\uE789");
    }

    public override void OnShown()
    {
        Build();
        _ = LoadAsync(refresh: false);
        foreach (var id in Feeds.Keys) _ = LoadFeedAsync(id);
    }

    private async Task LoadFeedAsync(string id)
    {
        if (!_feedsLoading.Add(id)) return;
        _feedErrors.Remove(id);
        try { _feeds[id] = await Task.Run(() => RssFeed.ReadAsync(Feeds[id].Url, Feeds[id].Site, _cts.Token), _cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { _feedErrors[id] = "Couldn't reach " + Feeds[id].Site + ". Check the internet connection, then press Details to try again."; }
        finally { _feedsLoading.Remove(id); }
        if (Host.IsTop(this) && (_section == id || _focusId == null)) Build();
        else if (Host.IsTop(this)) RefreshTabs();
    }

    private void RefreshTabs()
    {
        var focus = _focusId;
        Build();
        _focusId = focus;
    }

    public override void OnResumed()
    {
        if (_s2) RefreshSwitch2Colors();
        Build();
    }

    public override void OnClosed()
    {
        _cts.Cancel();
        _feeds.Clear();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y:
            case NavAction.RB:
            case NavAction.RT:
                MoveSection(+1);
                return true;
            case NavAction.LB:
            case NavAction.LT:
                MoveSection(-1);
                return true;
            case NavAction.X:
                if (IsFeed(_section)) _ = LoadFeedAsync(_section);
                else _ = LoadAsync(refresh: true);
                return true;
        }
        return false;
    }

    /// <summary>Next or previous section (LB / RB or LT / RT, or Y for next), with the first post of it selected.</summary>
    private void MoveSection(int step)
    {
        int i = Array.FindIndex(Sections, x => x.Id == _section);
        _section = Sections[(i + step + Sections.Length) % Sections.Length].Id;
        _focusId = null; // the first card of the new section
        UiSound.Move();
        Build();
    }

    // ---------------------------------------------------------------- data

    private async Task LoadAsync(bool refresh)
    {
        if (!refresh && _cachedNews != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(15))
        {
            _loading = false;
            Build();
            return;
        }
        _loading = true;
        Build();
        var ct = _cts.Token;

        // The games you play most recently, favorites, and anything with an update waiting.
        var follow = Host.AllGames.Where(g => g.IsSteam)
            .OrderByDescending(g => g.HasUpdate).ThenByDescending(g => g.IsFavorite).ThenByDescending(g => g.RecentAt)
            .Take(GamesToFollow).ToList();

        try
        {
            using var gate = new SemaphoreSlim(4);
            var newsTask = Task.WhenAll(follow.Select(async g =>
            {
                await gate.WaitAsync(ct);
                try { return (Game: (Game?)g, Name: g.Name, Posts: await Task.Run(() => SteamStoreClient.GetNewsAsync(g.AppId, PostsPerGame, ct), ct)); }
                finally { gate.Release(); }
            }).Append(Task.Run(async () =>
                (Game: (Game?)null, Name: "Steam", Posts: await SteamStoreClient.GetNewsAsync(SteamNewsAppId, 4, ct)), ct)));
            var salesTask = Task.Run(() => SteamStoreClient.GetSpecialsAsync(ct), ct);
            _ = Host.LoadWishlistAsync(); // the saved wishlist, for "on sale from your wishlist"

            var results = await newsTask;
            _cachedNews = results.SelectMany(r => r.Posts.Select(p => (p, r.Game, r.Name)))
                .Where(x => x.p.Date > DateTime.Now.AddDays(-60)) // two months is "latest" enough
                .OrderByDescending(x => x.p.Date).Take(120).ToList();
            _cachedSales = await salesTask;
            _cachedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { }

        _loading = false;
        if (Host.IsTop(this)) Build();
    }

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        _tabs.Children.Clear();
        _s2Nav.Children.Clear();
        foreach (var (id, name) in Sections)
        {
            string section = id;
            int count = Items(section).Count();
            bool showing = _section == id;
            bool counted = !((IsFeed(section) ? _feedsLoading.Contains(section) : _loading) || count == 0 || section == "sales" && _cachedSales == null);
            if (_s2)
            {
                // A section on the left: its name and count; the one showing blue with a bar. A thin line before
                // Announcements and before the news sites' feeds.
                if (id is "announcements" or "ign") _s2Nav.Children.Add(Switch2Pages.NavLine());
                var item = Switch2Pages.NavItem(name, counted ? (count > 99 ? "99+" : count.ToString()) : null, showing, () => { _section = section; Build(); });
                item.DataContext = "tab:" + id;
                item.GotKeyboardFocus += (_, _) => _focusId = "tab:" + section;
                _s2Nav.Children.Add(item);
                if (showing) _s2Title.Child = Switch2Pages.PageTitle(name, counted ? (count == 1 ? "1 story" : $"{count:N0} {(section == "sales" ? "deals" : "stories")}") : null);
                continue;
            }
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(Ui.Text(name, 16, showing ? "BgBrush" : "FgBrush", bold: true));
            if (counted)
            {
                var number = Ui.Text(count > 99 ? "99+" : count.ToString(), 12, showing ? "BgBrush" : "SubtleBrush", bold: true);
                number.Margin = new Thickness(8, 2, 0, 0);
                number.Opacity = 0.8;
                label.Children.Add(number);
            }
            var pill = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 2 - 4, 4)), Padding = new Thickness(18, 8, 18, 9), Child = label,
                                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
            if (showing) pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var tab = new Button { Style = Ui.StyleOf("BareButton"), Focusable = true, FocusVisualStyle = null, Content = pill, Margin = new Thickness(0, 0, 2, 0) };
            tab.GotKeyboardFocus += (_, _) => pill.SetResourceReference(Border.BorderBrushProperty, showing ? "FgBrush" : "AccentBrush");
            tab.LostKeyboardFocus += (_, _) => pill.BorderBrush = Brushes.Transparent;
            tab.DataContext = "tab:" + id;
            tab.GotKeyboardFocus += (_, _) => _focusId = "tab:" + section;
            tab.Click += (_, _) => { _section = section; Build(); };
            _tabs.Children.Add(tab);
        }

        _list.Children.Clear();
        if (_section == "sales") BuildSales();
        else BuildNews();

        var target = FindButton(_s2 ? _s2Nav : (Panel)_tabs, _focusId) ?? FindButton(_list, _focusId) ?? _list.FindDescendants<Button>().FirstOrDefault()
                     ?? (_s2 ? _s2Nav : (Panel)_tabs).Children.OfType<Button>().FirstOrDefault();
        FocusLater(target);
    }

    private static Button? FindButton(Panel panel, string? id) =>
        id == null ? null : panel.FindDescendants<Button>().FirstOrDefault(b => b.DataContext as string == id);

    private IEnumerable<object> Items(string section) => section switch
    {
        "sales" => (_cachedSales ?? new List<SaleItem>()).Cast<object>(),
        "patches" => (_cachedNews ?? new()).Where(n => n.Post.IsPatch).Cast<object>(),
        "announcements" => (_cachedNews ?? new()).Where(n => n.Post.IsAnnouncement && !n.Post.IsPatch).Cast<object>(),
        _ when IsFeed(section) => (_feeds.GetValueOrDefault(section) ?? new()).Select(p => (Post: p, Game: (Game?)null, GameName: Feeds[section].Site)).Cast<object>(),
        _ => (_cachedNews ?? new()).Cast<object>()
    };

    private void BuildNews()
    {
        var posts = Items(_section).Cast<(NewsItem Post, Game? Game, string GameName)>().ToList();
        _note.Text = IsFeed(_section)
            ? (_feedErrors.GetValueOrDefault(_section) ?? (_feedsLoading.Contains(_section) && _feeds.GetValueOrDefault(_section) == null
                ? "Getting " + Feeds[_section].About + "…" : posts.Count == 0 ? Feeds[_section].Site + "'s feed is empty right now." : ""))
            : _loading && _cachedNews == null ? "Getting the latest news for your games…"
            : posts.Count == 0 ? "Nothing new here in the last two months."
            : _loading ? "Checking for newer posts…" : "";
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (posts.Count == 0) return;
        // The newest story big across the top, the rest as cards three to a row.
        _list.Children.Add(HeroCard(posts[0].Post, posts[0].Game, posts[0].GameName));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = _s2 ? new Thickness(0, 4, 0, 0) : new Thickness(-6, 4, -6, 0) };
        foreach (var (post, game, gameName) in posts.Skip(1)) grid.Children.Add(NewsCard(post, game, gameName));
        _list.Children.Add(grid);
    }

    /// <summary>The best picture for a post: its own, else the game's wide store header, else the game's cover.</summary>
    private static void ShowPicture(Image image, NewsItem post, Game? game)
    {
        if (post.Image.Length > 0) _ = LoadAsync(image, post.Image);
        else if (game is { IsSteam: true }) _ = LoadAsync(image, $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/header.jpg");
        else if (game?.Cover != null) image.Source = game.Cover;
    }

    /// <summary>A small rounded label on a picture: the game or source, or "Patch notes".</summary>
    private static Border Chip(string text, bool accent)
    {
        var chip = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 6)), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 6, 0),
                                Child = Ui.Text(text, 12, accent ? "BgBrush" : "FgBrush", bold: true), VerticalAlignment = VerticalAlignment.Center };
        if (accent) chip.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else chip.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14));
        if (!accent && chip.Child is TextBlock t) t.Foreground = Brushes.White;
        return chip;
    }

    private static string Kind(NewsItem post) => post.IsPatch ? "Patch notes" : post.IsAnnouncement ? "Announcement" : post.Feed;

    /// <summary>Rounds a card's corners, its picture included (the clip follows the card's size).</summary>
    private static void RoundCorners(Border card, double radius)
    {
        card.CornerRadius = new CornerRadius(radius);
        card.SizeChanged += (_, _) => card.Clip = new RectangleGeometry(new Rect(card.RenderSize), radius, radius);
    }

    /// <summary>The featured story: its picture across the page, the title large over a shade at the bottom.</summary>
    private Button HeroCard(NewsItem post, Game? game, string gameName)
    {
        var face = new Grid { Height = 330 };
        var image = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        face.Children.Add(image);
        ShowPicture(image, post, game);
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.25));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xE8, 0, 0, 0), 1));
        face.Children.Add(new Border { Background = shade });

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(28, 0, 28, 24), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        chips.Children.Add(Chip(gameName.ToUpperInvariant(), accent: true));
        if (Kind(post) is { Length: > 0 } kind) chips.Children.Add(Chip(kind, accent: false));
        chips.Children.Add(Chip(Ui.Ago(post.Date), accent: false));
        words.Children.Add(chips);
        var title = new TextBlock { Text = post.Title, FontSize = 32, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 86 };
        words.Children.Add(title);
        if (post.Snippet.Length > 0)
            words.Children.Add(new TextBlock { Text = post.Snippet, FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 44, Margin = new Thickness(0, 6, 0, 0) });
        face.Children.Add(words);

        var card = new Border { Child = face };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        RoundCorners(card, Math.Max(Theme.TileCorner + 6, 8));
        return Wrap(card, post, _s2 ? new Thickness(0, 4, 8, 10) : new Thickness(-8, 4, -8, 6));
    }

    /// <summary>A story card: picture on top (with the game's name on it), then when, the title and a line of the text.</summary>
    private Button NewsCard(NewsItem post, Game? game, string gameName)
    {
        var stack = new StackPanel();
        var top = new Grid { Height = 176 };
        top.SetResourceReference(Panel.BackgroundProperty, "TileBrush");
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        top.Children.Add(image);
        ShowPicture(image, post, game);
        var chips = new WrapPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top };
        chips.Children.Add(Chip(gameName.ToUpperInvariant(), accent: true));
        if (post.IsPatch || post.IsAnnouncement) chips.Children.Add(Chip(Kind(post), accent: false));
        top.Children.Add(chips);
        stack.Children.Add(top);

        var words = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        var when = Ui.Text(Ui.Ago(post.Date) + (!post.IsPatch && !post.IsAnnouncement && post.Feed.Length > 0 ? "  ·  " + post.Feed : ""), 13, "SubtleBrush");
        when.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(when);
        var title = Ui.Text(post.Title, 17, "FgBrush", bold: true);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Height = 46;
        title.Margin = new Thickness(0, 4, 0, 4);
        words.Children.Add(title);
        var snippet = Ui.Text(post.Snippet, 13, "SubtleBrush");
        snippet.TextWrapping = TextWrapping.Wrap;
        snippet.TextTrimming = TextTrimming.CharacterEllipsis;
        snippet.Height = 36;
        words.Children.Add(snippet);
        stack.Children.Add(words);

        var card = new Border { Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        RoundCorners(card, Math.Max(Theme.TileCorner + 4, 6));
        return Wrap(card, post, new Thickness(0, 0, 0, 4));
    }

    private Button Wrap(Border card, NewsItem post, Thickness margin)
    {
        var button = Ui.Tile(card);
        if (_s2) Switch2Pages.RestyleTile(button); // the rimmed highlight card round the story
        button.Margin = margin;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.DataContext = "post:" + post.Url;
        button.GotKeyboardFocus += (_, _) => _focusId = "post:" + post.Url;
        button.Click += (_, _) => Host.Push(new NewsReaderScreen(post));
        return button;
    }

    private void BuildSales()
    {
        var wish = Host.WishlistGames.Where(g => g.DiscountPercent > 0).OrderByDescending(g => g.DiscountPercent).ToList();
        var specials = _cachedSales ?? new List<SaleItem>();
        _note.Text = _loading && _cachedSales == null ? "Getting this week's sales…"
            : wish.Count == 0 && specials.Count == 0 ? "The Steam store didn't return any sales right now." : "";
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (wish.Count > 0)
        {
            AddHeading($"On sale from your wishlist  ({wish.Count})");
            var panel = SaleRows();
            Host.EnsureCovers(wish);
            foreach (var g in wish.Take(24))
                panel.Children.Add(Fit(SaleCard(g.AppId, g.Name, g.DiscountPercent, "", g.Price, null, g, null)));
            _list.Children.Add(panel);
        }
        if (specials.Count > 0)
        {
            AddHeading("Steam specials");
            var panel = SaleRows();
            foreach (var sale in specials)
                panel.Children.Add(Fit(SaleCard(sale.AppId, sale.Name, sale.DiscountPercent, sale.OldPrice, sale.Price, sale.Image, null, sale.Ends)));
            _list.Children.Add(panel);
        }
    }

    /// <summary>Sales three to a row, whatever the width of the page.</summary>
    private static System.Windows.Controls.Primitives.UniformGrid SaleRows() => new() { Columns = 3, VerticalAlignment = VerticalAlignment.Top };

    /// <summary>A sale card in its column: its own size where there's room, a little smaller where three wouldn't fit.</summary>
    private static Viewbox Fit(Button card) => new()
    {
        Child = card, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
    };

    private void AddHeading(string text)
    {
        var heading = Ui.Text(text, 18, "FgBrush", bold: true);
        heading.Margin = new Thickness(4, 16, 0, 8);
        _list.Children.Add(heading);
    }

    private Button SaleCard(int appId, string name, int discount, string oldPrice, string price, string? imageUrl, Game? game, DateTime? ends)
    {
        const double width = 276, height = 129; // Steam's capsule shape
        var stack = new StackPanel { Width = width };
        var picture = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(10), ClipToBounds = true };
        picture.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var image = new Image { Stretch = Stretch.UniformToFill };
        var face = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, width, height), 10, 10) };
        face.Children.Add(image);
        // The discount, big, in the corner.
        var off = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(8),
                               HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                               Child = Ui.Text("-" + discount + "%", 16, "BgBrush", bold: true) };
        off.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        face.Children.Add(off);
        picture.Child = face;
        stack.Children.Add(picture);

        if (imageUrl != null) _ = LoadAsync(image, imageUrl);
        else
        {
            // Wishlist games: Steam's wide header art, or the square cover meanwhile.
            image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = game });
            _ = LoadAsync(image, $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg");
        }

        var title = Ui.Text(name, 15, "FgBrush", bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(2, 8, 2, 0);
        stack.Children.Add(title);
        var prices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 2, 0) };
        if (oldPrice.Length > 0)
        {
            var was = Ui.Text(oldPrice, 14, "SubtleBrush");
            was.TextDecorations = TextDecorations.Strikethrough;
            was.Margin = new Thickness(0, 0, 8, 0);
            prices.Children.Add(was);
        }
        if (price.Length > 0) prices.Children.Add(Ui.Text(price, 14, "FgBrush", bold: true));
        if (ends is DateTime end && end > DateTime.Now)
            prices.Children.Add(Ui.Text("  ·  ends " + (end - DateTime.Now).TotalHours switch
            {
                < 24 => "today",
                < 48 => "tomorrow",
                _ => end.ToString("MMM d")
            }, 13, "SubtleBrush"));
        stack.Children.Add(prices);

        var button = Ui.Tile(stack);
        if (_s2) Switch2Pages.RestyleTile(button);
        button.DataContext = "sale:" + appId;
        button.GotKeyboardFocus += (_, _) => _focusId = "sale:" + appId;
        button.Click += (_, _) => ProcessUtil.Open("steam://store/" + appId);
        return button;
    }

    private static async Task LoadAsync(Image view, string url)
    {
        try
        {
            var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 560));
            if (source != null) view.Source = source;
        }
        catch (Exception) { }
    }
}
