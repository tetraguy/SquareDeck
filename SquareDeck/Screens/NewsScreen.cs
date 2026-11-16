using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// News for the games you play: the latest posts from their Steam pages (patch notes and announcements
/// included), Steam's own news, and sales (your wishlist first, then the Steam store's specials).
/// </summary>
public sealed class GameNewsScreen : Screen
{
    private const int GamesToFollow = 18, PostsPerGame = 4;
    private static readonly int SteamNewsAppId = 593110; // Steam's own news feed
    private static readonly (string Id, string Name)[] Sections =
        { ("all", "Latest"), ("patches", "Patches and updates"), ("announcements", "Announcements"), ("sales", "Sales") };

    // Kept between visits for a quarter of an hour, so opening News again is instant.
    private static List<(NewsItem Post, Game? Game, string GameName)>? _cachedNews;
    private static List<SaleItem>? _cachedSales;
    private static DateTime _cachedAt;

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    private readonly StackPanel _list = new();
    private readonly TextBlock _note = Ui.Text("", 16, "SubtleBrush");
    private readonly CancellationTokenSource _cts = new();
    private string _section = "all";
    private string? _focusId;
    private bool _loading = true;

    public GameNewsScreen()
    {
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(6, 6, 0, 10);
        var body = new StackPanel { Margin = new Thickness(0, 0, 14, 24) };
        body.Children.Add(_tabs);
        body.Children.Add(_note);
        body.Children.Add(_list);
        SetPage("News", new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = body
        }, "LB RB  Section      {Select}  Open      {Details}  Refresh      {Back}  Back", wide: true);
    }

    public override void OnShown()
    {
        Build();
        _ = LoadAsync(refresh: false);
    }

    public override void OnResumed() => Build();

    public override void OnClosed() => _cts.Cancel();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y:
            case NavAction.RB:
                MoveSection(+1);
                return true;
            case NavAction.LB:
                MoveSection(-1);
                return true;
            case NavAction.X:
                _ = LoadAsync(refresh: true);
                return true;
        }
        return false;
    }

    /// <summary>Next or previous section (LB / RB, or Y for next), with the first post of it selected.</summary>
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
        foreach (var (id, name) in Sections)
        {
            string section = id;
            int count = Items(section).Count();
            var tab = new Button
            {
                Style = Ui.StyleOf("PillButton"), Margin = new Thickness(0, 0, 10, 0),
                Content = _loading || count == 0 || section == "sales" && _cachedSales == null ? name : $"{name}  {count}"
            };
            if (_section == id) tab.SetResourceReference(ForegroundProperty, "AccentBrush");
            tab.DataContext = "tab:" + id;
            tab.GotKeyboardFocus += (_, _) => _focusId = "tab:" + section;
            tab.Click += (_, _) => { _section = section; Build(); };
            _tabs.Children.Add(tab);
        }

        _list.Children.Clear();
        if (_section == "sales") BuildSales();
        else BuildNews();

        var target = FindButton(_tabs, _focusId) ?? FindButton(_list, _focusId) ?? _list.FindDescendants<Button>().FirstOrDefault()
                     ?? _tabs.Children.OfType<Button>().FirstOrDefault();
        FocusLater(target);
    }

    private static Button? FindButton(Panel panel, string? id) =>
        id == null ? null : panel.FindDescendants<Button>().FirstOrDefault(b => b.DataContext as string == id);

    private IEnumerable<object> Items(string section) => section switch
    {
        "sales" => (_cachedSales ?? new List<SaleItem>()).Cast<object>(),
        "patches" => (_cachedNews ?? new()).Where(n => n.Post.IsPatch).Cast<object>(),
        "announcements" => (_cachedNews ?? new()).Where(n => n.Post.IsAnnouncement && !n.Post.IsPatch).Cast<object>(),
        _ => (_cachedNews ?? new()).Cast<object>()
    };

    private void BuildNews()
    {
        var posts = Items(_section).Cast<(NewsItem Post, Game? Game, string GameName)>().ToList();
        _note.Text = _loading && _cachedNews == null ? "Getting the latest news for your games…"
            : posts.Count == 0 ? "Nothing new here in the last two months."
            : _loading ? "Checking for newer posts…" : "";
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        DateTime? lastDay = null;
        foreach (var (post, game, gameName) in posts)
        {
            // A small heading whenever the day changes.
            var day = post.Date.Date;
            if (lastDay != day)
            {
                lastDay = day;
                var heading = Ui.Text(day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("dddd, MMMM d"),
                    18, "FgBrush", bold: true);
                heading.Margin = new Thickness(4, 16, 0, 8);
                _list.Children.Add(heading);
            }
            _list.Children.Add(NewsCard(post, game, gameName));
        }
    }

    private Button NewsCard(NewsItem post, Game? game, string gameName)
    {
        var row = new DockPanel();

        // The game's cover (or the post's own picture, for Steam news), on the left.
        var picture = new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(0, 0, 18, 0) };
        picture.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var image = new Image { Stretch = Stretch.UniformToFill };
        picture.Child = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 92, 92), 10, 10), Children = { image } };
        if (game?.Cover != null && !(game.UsesIcon && !game.IconFills)) image.Source = game.Cover;
        else if (game?.Cover != null)
        {
            picture.Background = game.TileBackground ?? picture.Background;
            image.Stretch = Stretch.Uniform;
            image.Margin = new Thickness(18);
            image.Source = game.Cover;
        }
        else if (post.Image.Length > 0) _ = LoadAsync(image, post.Image);
        else image.Source = null;
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new WrapPanel();
        top.Children.Add(Small(gameName.ToUpperInvariant(), "AccentBrush"));
        if (post.IsPatch) top.Children.Add(Tag("Patch notes"));
        else if (post.IsAnnouncement) top.Children.Add(Tag("Announcement"));
        else if (post.Feed.Length > 0) top.Children.Add(Small("  ·  " + post.Feed, "SubtleBrush"));
        top.Children.Add(Small("  ·  " + post.Date.ToString("h:mm tt"), "SubtleBrush"));
        text.Children.Add(top);

        var title = Ui.Text(post.Title, 19, "FgBrush", bold: true);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxHeight = 52;
        title.Margin = new Thickness(0, 4, 0, 4);
        text.Children.Add(title);
        var snippet = Ui.Text(post.Snippet, 14, "SubtleBrush");
        snippet.TextWrapping = TextWrapping.Wrap;
        snippet.TextTrimming = TextTrimming.CharacterEllipsis;
        snippet.MaxHeight = 38;
        text.Children.Add(snippet);
        row.Children.Add(text);

        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Child = row };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var button = Ui.Tile(card);
        button.Margin = new Thickness(-8, 0, -8, -2);
        button.DataContext = "post:" + post.Url;
        button.GotKeyboardFocus += (_, _) => _focusId = "post:" + post.Url;
        button.Click += (_, _) => Host.Push(new NewsReaderScreen(post));
        return button;
    }

    private static TextBlock Small(string text, string brush) => Ui.Text(text, 13, brush, bold: brush == "AccentBrush");

    private static Border Tag(string text)
    {
        var tag = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0),
                               Child = Ui.Text(text, 12, "BgBrush", bold: true), VerticalAlignment = VerticalAlignment.Center };
        tag.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        return tag;
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
            var panel = new WrapPanel();
            Host.EnsureCovers(wish);
            foreach (var g in wish.Take(24))
                panel.Children.Add(SaleCard(g.AppId, g.Name, g.DiscountPercent, "", g.Price, null, g, null));
            _list.Children.Add(panel);
        }
        if (specials.Count > 0)
        {
            AddHeading("Steam specials");
            var panel = new WrapPanel();
            foreach (var sale in specials)
                panel.Children.Add(SaleCard(sale.AppId, sale.Name, sale.DiscountPercent, sale.OldPrice, sale.Price, sale.Image, null, sale.Ends));
            _list.Children.Add(panel);
        }
    }

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
