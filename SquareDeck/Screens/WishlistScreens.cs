using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>The two pages about games you want: prices and sales, and when they come out. LB / RB switch.</summary>
public static class WishTabs
{
    public static readonly (string Id, string Name, string Glyph)[] Tabs =
    {
        ("wishlist", "Wishlist and sales", ""), ("calendar", "Release calendar", ""),
    };

    public static FrameworkElement Bar(string active, Action<string> go)
    {
        var bar = new Border { CornerRadius = new CornerRadius(30), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        bar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, name, glyph) in Tabs)
        {
            bool on = id == active;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
            line.Children.Add(new TextBlock { Text = name, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal });
            var tab = new Button { Style = Ui.StyleOf("PillButton"), Content = line, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 4, 0) };
            if (on) { tab.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); tab.SetResourceReference(Control.ForegroundProperty, "BgBrush"); }
            else tab.Background = Brushes.Transparent;
            string target = id;
            tab.Click += (_, _) => { if (target != active) go(target); };
            row.Children.Add(tab);
        }
        bar.Child = row;
        return bar;
    }

    public static Screen Create(string tab) => tab == "calendar" ? new ReleaseCalendarScreen() : new WishlistScreen();

    public static void Switch(MainWindow host, Screen current, string tab)
    {
        host.Push(Create(tab));
        host.RemoveBelow(current);
    }

    /// <summary>The store page in Steam (or the web if Steam isn't there).</summary>
    public static void OpenStore(int appId)
    {
        if (!ProcessUtil.Open("steam://store/" + appId)) ProcessUtil.Open("https://store.steampowered.com/app/" + appId);
    }

    /// <summary>A rounded header-capsule picture, loaded in the background.</summary>
    public static Border Picture(WatchedGame g, double width, double height, double corner = 12)
    {
        var box = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(corner), ClipToBounds = true };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var image = new Image { Stretch = Stretch.UniformToFill };
        box.Child = image;
        box.Clip = new RectangleGeometry(new Rect(0, 0, width, height), corner, corner);
        string url = g.HeaderUrl;
        _ = Task.Run(() => ImageLoader.FromUrlAsync(url, (int)(width * 1.5)))
            .ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) image.Source = t.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
        return box;
    }
}

/// <summary>
/// Your Steam wishlist and the games you watch here, with today's prices: on sale first (biggest discount at the
/// top), then coming soon, then the rest. Sale and release-day notifications are set here too.
/// </summary>
public sealed class WishlistScreen : Screen
{
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 0, 24) };
    private readonly TextBlock _status = Ui.Text("", 15, "SubtleBrush");
    private int? _focusId;

    public WishlistScreen()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _page };
        SetPage("Wishlist", scroll, "{Select}  Options        {LB}{RB}  Switch page        {Back}  Back");
    }

    public override void OnShown()
    {
        Host.PricesChecked += Refresh;
        Build();
        if (Host.PriceWatch.Games.Count == 0 || DateTime.Now - Host.PriceWatch.CheckedAt > TimeSpan.FromHours(6)) _ = CheckAsync();
    }

    public override void OnClosed() => Host.PricesChecked -= Refresh;
    public override void OnResumed() => Build();

    private void Refresh()
    {
        if (Host.IsTop(this)) Build();
    }

    public override bool OnAction(NavAction action)
    {
        if (action is NavAction.LB or NavAction.RB) { UiSound.Select(); WishTabs.Switch(Host, this, "calendar"); return true; }
        return false;
    }

    private async Task CheckAsync()
    {
        _status.Text = "Checking prices…";
        await Host.CheckPricesAsync(manual: true);
    }

    private void Build()
    {
        var s = Host.Settings;
        var watch = Host.PriceWatch;
        _page.Children.Clear();
        _page.Children.Add(WishTabs.Bar("wishlist", t => WishTabs.Switch(Host, this, t)));

        var games = watch.Games.Where(g => g.Name.Length > 0).ToList();
        int sale = games.Count(g => g.OnSale), soon = games.Count(g => g.ComingSoon);

        // Summary and actions.
        var summary = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(22, 18, 22, 16), Margin = new Thickness(0, 0, 0, 10) };
        summary.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        var top = new StackPanel();
        var counts = new WrapPanel();
        void Stat(string value, string label, string brush)
        {
            var st = new StackPanel { Margin = new Thickness(0, 0, 40, 6) };
            st.Children.Add(Ui.Text(value, 34, brush, bold: true));
            st.Children.Add(Ui.Text(label, 14, "SubtleBrush"));
            counts.Children.Add(st);
        }
        Stat(games.Count.ToString(), games.Count == 1 ? "game" : "games", "FgBrush");
        Stat(sale.ToString(), "on sale now", sale > 0 ? "AccentBrush" : "FgBrush");
        Stat(soon.ToString(), "coming soon", "FgBrush");
        top.Children.Add(counts);
        _status.Text = watch.CheckedAt == default ? "Not checked yet." : "Prices checked " + Ui.Ago(watch.CheckedAt) + ". Checked again every 6 hours.";
        if (watch.WishlistState == "private") _status.Text += " Your Steam wishlist couldn't be read: set “Game details” to Public in your Steam privacy settings, or add a Steam Web API key in Settings.";
        else if (watch.WishlistState == "none") _status.Text += " Sign in to Steam on this PC to bring in your wishlist; you can still add games here.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 4, 0, 12);
        top.Children.Add(_status);

        var actions = new WrapPanel();
        Button Action(string glyph, string text, Action click)
        {
            var b = Ui.PanelButton(glyph, text);
            b.Margin = new Thickness(0, 0, 10, 8);
            b.Click += (_, _) => click();
            actions.Children.Add(b);
            return b;
        }
        Action("", "Check prices now", () => _ = CheckAsync());
        Action("", "Watch another game", () => _ = AddAsync());
        Action("", "Sale alerts: " + (s.SaleAlerts ? (s.SaleAlertMin <= 1 ? "any discount" : $"{s.SaleAlertMin}% off or more") : "off"), () => _ = AlertsAsync());
        Action("", "Release-day alerts: " + (s.ReleaseAlerts ? "on" : "off"), () => { s.ReleaseAlerts = !s.ReleaseAlerts; s.Save(); Build(); });
        top.Children.Add(actions);
        summary.Child = top;
        _page.Children.Add(summary);

        if (games.Count == 0)
        {
            var empty = Ui.Text(watch.CheckedAt == default ? "Getting your wishlist from Steam…" :
                "Nothing here yet. Add games to your Steam wishlist, or press Watch another game to follow one here.", 17, "SubtleBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(4, 12, 0, 0);
            _page.Children.Add(empty);
            FocusLater(actions.Children[1] as Button);
            return;
        }

        Button? focus = null, first = null;
        void Section(string title, IEnumerable<WatchedGame> list)
        {
            var items = list.ToList();
            if (items.Count == 0) return;
            var head = Ui.Text(title, 22, "FgBrush", bold: true);
            head.Margin = new Thickness(4, 22, 0, 12);
            _page.Children.Add(head);
            // Two to a row, each half the page wide.
            var wrap = new UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Top };
            foreach (var g in items)
            {
                var card = Card(g);
                wrap.Children.Add(card);
                first ??= card;
                if (g.AppId == _focusId) focus = card;
            }
            _page.Children.Add(wrap);
        }
        Section("On sale", games.Where(g => g.OnSale).OrderByDescending(g => g.Discount));
        Section("Coming soon", games.Where(g => !g.OnSale && g.ComingSoon).OrderBy(g => g.ReleaseDay ?? g.ReleaseMonth ?? DateTime.MaxValue));
        Section("Everything else", games.Where(g => !g.OnSale && !g.ComingSoon).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        FocusLater(focus ?? first);
    }

    private Button Card(WatchedGame g)
    {
        var face = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Stretch };
        face.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var pic = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        pic.Children.Add(WishTabs.Picture(g, 200, 93));
        if (g.OnSale)
        {
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                                     Background = new SolidColorBrush(Color.FromRgb(0x4C, 0x6B, 0x22)),
                                     Child = new TextBlock { Text = $"-{g.Discount}%", Foreground = new SolidColorBrush(Color.FromRgb(0xBE, 0xEE, 0x11)), FontWeight = FontWeights.Bold, FontSize = 16 } };
            pic.Children.Add(badge);
        }
        grid.Children.Add(pic);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        var name = Ui.Text(g.Name, 19, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(name);
        var priceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (g.OnSale && g.RegularPrice.Length > 0)
        {
            var was = Ui.Text(g.RegularPrice, 15, "SubtleBrush");
            was.TextDecorations = TextDecorations.Strikethrough;
            was.Margin = new Thickness(0, 0, 10, 0);
            was.VerticalAlignment = VerticalAlignment.Center;
            priceRow.Children.Add(was);
        }
        var price = Ui.Text(g.Price.Length > 0 ? g.Price : "No price yet", 20, g.OnSale ? "AccentBrush" : "FgBrush", bold: true);
        priceRow.Children.Add(price);
        info.Children.Add(priceRow);
        string detail = g.ComingSoon ? "Release: " + (g.ReleaseText.Length > 0 ? g.ReleaseText : "to be announced")
                      : g.LowestCents > 0 && g.LowestPrice.Length > 0 ? "Lowest seen here: " + g.LowestPrice : g.ReleaseText.Length > 0 ? "Released " + g.ReleaseText : "";
        if (detail.Length > 0) info.Children.Add(Ui.Text(detail, 14, "SubtleBrush"));
        var source = Ui.Text(g.Source == "wishlist" ? "On your Steam wishlist" : "Watching in SquareDeck", 13, "SubtleBrush");
        source.Opacity = 0.8;
        source.Margin = new Thickness(0, 4, 0, 0);
        info.Children.Add(source);
        grid.Children.Add(info);
        face.Child = grid;
        var tile = Ui.Tile(face);
        tile.Margin = new Thickness(0, 0, 8, 8);
        tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tile.Click += (_, _) => _ = OptionsAsync(g);
        return tile;
    }

    private async Task OptionsAsync(WatchedGame g)
    {
        _focusId = g.AppId;
        var options = new List<(string Label, string Glyph)> { ("Open in the Steam store", "") };
        if (g.Source == "watch") options.Add(("Stop watching", ""));
        options.Add(("Cancel", ""));
        string message = (g.OnSale ? $"{g.Discount}% off: {g.Price} (was {g.RegularPrice}). " : g.Price.Length > 0 ? g.Price + ". " : "") +
                         (g.LowestPrice.Length > 0 ? $"Lowest seen: {g.LowestPrice}. " : "") +
                         (g.Source == "wishlist" ? "Remove it from your wishlist on Steam to stop following it." : "");
        int choice = await Host.AskWithIconsAsync(g.Name, message, options.ToArray());
        if (choice == 0) WishTabs.OpenStore(g.AppId);
        else if (choice == 1 && g.Source == "watch") { Host.PriceWatch.Unwatch(g.AppId); _focusId = null; Build(); }
    }

    private async Task AddAsync()
    {
        var term = await Host.PromptAsync("Watch a game", "Search the Steam store. You get a notification when it goes on sale, and its release date goes on the calendar.", "", "Search");
        if (string.IsNullOrWhiteSpace(term)) return;
        var found = await Task.Run(() => SteamStoreClient.SearchStoreAsync(term.Trim(), CancellationToken.None));
        if (found.Count == 0) { await Host.AskAsync("Nothing found", "The Steam store has nothing called “" + term + "”.", "OK"); return; }
        var top = found.Take(8).ToList();
        int choice = await Host.AskAsync("Which one?", null, top.Select(f => f.Name + (f.Price.Length > 0 ? "  ·  " + f.Price : "")).Append("Cancel").ToArray());
        if (choice < 0 || choice >= top.Count) return;
        var game = Host.PriceWatch.Watch(top[choice].AppId, top[choice].Name);
        _focusId = game.AppId;
        Build();
        await Task.Run(() => Host.PriceWatch.RefreshOneAsync(game, CancellationToken.None));
        Host.ShowToast("Wishlist", "Watching " + game.Name, game.OnSale ? $"It's already {game.Discount}% off: {game.Price}." : "You'll get a notification when it goes on sale.", game.HeaderUrl, remember: false);
        Build();
    }

    private async Task AlertsAsync()
    {
        var s = Host.Settings;
        int choice = await Host.AskAsync("Sale alerts", "A notification when a game on your wishlist (or that you watch) goes on sale, and again if the discount gets deeper.",
            "Any discount", "25% off or more", "50% off or more", "75% off or more", "Off", "Cancel");
        if (choice is < 0 or > 4) return;
        s.SaleAlerts = choice != 4;
        s.SaleAlertMin = choice switch { 1 => 25, 2 => 50, 3 => 75, _ => 1 };
        s.Save();
        Build();
    }
}

/// <summary>
/// A month at a glance: the release days of your wishlist and watched games, with their art on each day. Games with
/// only a month (or quarter) are listed under the calendar. LT / RT (or the arrows) change month.
/// </summary>
public sealed class ReleaseCalendarScreen : Screen
{
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 0, 24) };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? _focusTag;

    public ReleaseCalendarScreen()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _page };
        SetPage("Release calendar", scroll, "{Select}  See games        LT RT  Month        {LB}{RB}  Switch page        {Back}  Back", wide: true);
    }

    public override void OnShown()
    {
        Host.PricesChecked += Refresh;
        Build();
        if (Host.PriceWatch.Games.Count == 0 || DateTime.Now - Host.PriceWatch.CheckedAt > TimeSpan.FromHours(6)) _ = Host.CheckPricesAsync(manual: true);
    }

    public override void OnClosed() => Host.PricesChecked -= Refresh;

    private void Refresh()
    {
        if (Host.IsTop(this)) Build();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.LB or NavAction.RB: UiSound.Select(); WishTabs.Switch(Host, this, "wishlist"); return true;
            case NavAction.LT: Step(-1); return true;
            case NavAction.RT: Step(1); return true;
        }
        return false;
    }

    private void Step(int months)
    {
        _month = _month.AddMonths(months);
        _focusTag = months < 0 ? "prev" : "next";
        UiSound.Page();
        Build();
    }

    private void Build()
    {
        _page.Children.Clear();
        _page.Children.Add(WishTabs.Bar("calendar", t => WishTabs.Switch(Host, this, t)));
        var games = Host.PriceWatch.Games.Where(g => g.Name.Length > 0).ToList();

        // Month header: arrows, the month, Today.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button NavButton(string glyph, string tip, string tag, Action click)
        {
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 16 }, Width = 52, Height = 48, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = tip, Tag = tag };
            b.Click += (_, _) => click();
            nav.Children.Add(b);
            return b;
        }
        var prev = NavButton("", "Previous month", "prev", () => Step(-1));
        var next = NavButton("", "Next month", "next", () => Step(1));
        var today = Ui.PanelButton("", "This month");
        today.Tag = "today";
        today.Click += (_, _) => { _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _focusTag = "today"; Build(); };
        nav.Children.Add(today);
        DockPanel.SetDock(nav, Dock.Right);
        head.Children.Add(nav);
        int inMonth = games.Count(g => g.ReleaseDay is { } d && d.Year == _month.Year && d.Month == _month.Month);
        var title = new StackPanel();
        title.Children.Add(Ui.Text(_month.ToString("MMMM yyyy"), 34, "FgBrush", bold: true));
        title.Children.Add(Ui.Text(inMonth == 0 ? "No release days this month" : inMonth == 1 ? "1 release this month" : $"{inMonth} releases this month", 15, "SubtleBrush"));
        head.Children.Add(title);
        _page.Children.Add(head);

        // Weekday names, then six weeks of days.
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 6) };
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        int firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var n = Ui.Text(culture.DateTimeFormat.AbbreviatedDayNames[(firstDay + i) % 7].ToUpperInvariant(), 13, "SubtleBrush", bold: true);
            n.Margin = new Thickness(10, 0, 0, 0);
            names.Children.Add(n);
        }
        _page.Children.Add(names);

        var grid = new UniformGrid { Columns = 7, Rows = 6 };
        int offset = ((int)_month.DayOfWeek - firstDay + 7) % 7;
        var start = _month.AddDays(-offset);
        Button? focusButton = null, firstWithGames = null;
        int index = 0;
        for (int i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var on = games.Where(g => g.ReleaseDay?.Date == day).ToList();
            bool inThisMonth = day.Month == _month.Month;
            var cell = DayCell(day, on, inThisMonth);
            grid.Children.Add(cell);
            if (cell is Button b)
            {
                firstWithGames ??= b;
                if (_focusTag == "day:" + day.ToString("yyyy-MM-dd")) focusButton = b;
            }
            index++;
        }
        _page.Children.Add(grid);
        // Pop the days in, row by row.
        if (!Theme.ReduceMotion)
        {
            grid.Opacity = 0;
            grid.RenderTransform = new TranslateTransform(0, 12);
            grid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            ((TranslateTransform)grid.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Only the month known.
        var monthOnly = games.Where(g => g.ReleaseDay == null && g.ReleaseMonth is { } m && m.Year == _month.Year && m.Month == _month.Month).ToList();
        if (monthOnly.Count > 0)
        {
            var t = Ui.Text("Sometime in " + _month.ToString("MMMM") + " (or that quarter)", 20, "FgBrush", bold: true);
            t.Margin = new Thickness(4, 22, 0, 10);
            _page.Children.Add(t);
            var row = new WrapPanel();
            foreach (var g in monthOnly) row.Children.Add(Chip(g));
            _page.Children.Add(row);
        }
        int undated = games.Count(g => g.ComingSoon && g.ReleaseDay == null && g.ReleaseMonth == null);
        var upcoming = games.Where(g => g.ReleaseDay is { } d && d > _month.AddMonths(1).AddDays(-1)).OrderBy(g => g.ReleaseDay).Take(6).ToList();
        if (upcoming.Count > 0)
        {
            var t = Ui.Text("After this month", 20, "FgBrush", bold: true);
            t.Margin = new Thickness(4, 22, 0, 10);
            _page.Children.Add(t);
            var row = new WrapPanel();
            foreach (var g in upcoming) row.Children.Add(Chip(g, g.ReleaseDay!.Value.ToString("MMM d, yyyy")));
            _page.Children.Add(row);
        }
        if (undated > 0)
        {
            var t = Ui.Text(undated == 1 ? "1 more game has no release date yet." : $"{undated} more games have no release date yet.", 15, "SubtleBrush");
            t.Margin = new Thickness(4, 16, 0, 0);
            _page.Children.Add(t);
        }
        if (games.Count == 0)
        {
            var t = Ui.Text(Host.PriceWatch.CheckedAt == default ? "Getting your wishlist from Steam…" : "Add games to your Steam wishlist, or watch them on the Wishlist page, to see their release days here.", 16, "SubtleBrush");
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(4, 16, 0, 0);
            _page.Children.Add(t);
        }

        FocusLater(focusButton ?? _focusTag switch { "prev" => prev, "next" => next, "today" => today, _ => null } ?? firstWithGames ?? next);
        _focusTag = null;
    }

    /// <summary>A day: its number, and the art of anything coming out (a button then, to see the list).</summary>
    private UIElement DayCell(DateTime day, List<WatchedGame> games, bool inThisMonth)
    {
        bool today = day == DateTime.Today;
        var face = new Border { Height = 118, CornerRadius = new CornerRadius(14), Margin = new Thickness(3), Padding = new Thickness(8, 6, 8, 8), BorderThickness = new Thickness(today ? 2 : 1) };
        face.SetResourceReference(Border.BackgroundProperty, games.Count > 0 ? "DialogBrush" : "TileBrush");
        face.SetResourceReference(Border.BorderBrushProperty, today ? "AccentBrush" : "PanelStroke");
        face.Opacity = inThisMonth ? 1 : 0.4;
        var stack = new DockPanel();
        var number = new TextBlock { Text = day.Day.ToString(), FontSize = 16, FontWeight = today ? FontWeights.Bold : FontWeights.SemiBold };
        number.SetResourceReference(TextBlock.ForegroundProperty, today ? "AccentBrush" : "FgBrush");
        var headRow = new DockPanel();
        DockPanel.SetDock(headRow, Dock.Top);
        if (games.Count > 1)
        {
            var more = Ui.Text($"{games.Count} games", 12, "SubtleBrush");
            DockPanel.SetDock(more, Dock.Right);
            headRow.Children.Add(more);
        }
        headRow.Children.Add(number);
        stack.Children.Add(headRow);
        if (games.Count > 0)
        {
            var g = games[0];
            var pic = WishTabs.Picture(g, 150, 54, 8);
            pic.Width = double.NaN;
            pic.HorizontalAlignment = HorizontalAlignment.Stretch;
            pic.Margin = new Thickness(0, 4, 0, 2);
            pic.Clip = null;
            DockPanel.SetDock(pic, Dock.Top);
            stack.Children.Add(pic);
            var name = Ui.Text(g.Name, 12, "FgBrush");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(name);
        }
        face.Child = stack;
        if (games.Count == 0) return face;

        var button = new Button { Style = Ui.StyleOf("TileButton"), Content = face, Tag = "day:" + day.ToString("yyyy-MM-dd"), Padding = new Thickness(0), Margin = new Thickness(-8) };
        button.Click += async (_, _) =>
        {
            _focusTag = "day:" + day.ToString("yyyy-MM-dd");
            int choice = await Host.AskAsync(day.ToString("dddd, MMMM d"), "Out on this day. Choose one to open its store page.",
                games.Select(x => x.Name + (x.Price.Length > 0 && x.Price != "Coming soon" ? "  ·  " + x.Price : "")).Append("Close").ToArray());
            if (choice >= 0 && choice < games.Count) WishTabs.OpenStore(games[choice].AppId);
        };
        return button;
    }

    private Button Chip(WatchedGame g, string? when = null)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var pic = WishTabs.Picture(g, 92, 43, 8);
        pic.Margin = new Thickness(0, 0, 12, 0);
        line.Children.Add(pic);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis });
        var sub = Ui.Text(when ?? g.ReleaseText, 13, "SubtleBrush");
        text.Children.Add(sub);
        line.Children.Add(text);
        var b = new Button { Style = Ui.StyleOf("PillButton"), Content = line, Padding = new Thickness(8, 8, 18, 8), Margin = new Thickness(0, 0, 10, 10) };
        b.Click += (_, _) => WishTabs.OpenStore(g.AppId);
        return b;
    }
}
