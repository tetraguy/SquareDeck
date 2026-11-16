using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The page behind the profile picture: a banner with your picture, name and Steam level, a row of headline
/// numbers, then recently played games, achievements, badges, your wishlist and play-time records, as cards.
/// </summary>
public sealed class ProfileScreen : Screen
{
    private const double CardRadius = 12;
    private readonly StackPanel _content = new() { Margin = new Thickness(0, 0, 14, 24) };
    private readonly CancellationTokenSource _cts = new();
    private SteamProfileData? _steam;
    private string _steamState = "loading"; // loading | ready | failed | nokey
    private ImageSource? _banner, _steamAvatar;
    private string? _focusId;

    public ProfileScreen()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = _content
        };
        SetPage("Your profile", scroll, "{Select}  Open      {Options}  Refresh from Steam      {Back}  Back", wide: true);
    }

    public override void OnShown()
    {
        Host.LibraryChanged += OnLibraryChanged;
        Build();
        _ = LoadSteamAsync(refresh: false);
        _ = LoadBannerAsync();
        _ = Host.LoadWishlistAsync(); // uses the saved copy when there is one
    }

    public override void OnResumed() => Build();

    public override void OnClosed()
    {
        _cts.Cancel();
        Host.LibraryChanged -= OnLibraryChanged;
    }

    private void OnLibraryChanged()
    {
        if (Host.IsTop(this)) Build();
    }

    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.Y) return false;
        _steamState = "loading";
        Build();
        _ = LoadSteamAsync(refresh: true);
        _ = Host.LoadWishlistAsync(refresh: true);
        return true;
    }

    // ---------------------------------------------------------------- data

    private async Task LoadSteamAsync(bool refresh)
    {
        var key = Host.Settings.SteamWebApiKey;
        if (key.Length == 0) { _steamState = "nokey"; Build(); return; }
        var ct = _cts.Token;
        SteamProfileData? data = null;
        try { data = await Task.Run(() => SteamProfile.GetAsync(key, refresh, ct)); }
        catch (Exception) { }
        if (ct.IsCancellationRequested) return;
        _steam = data;
        _steamState = data == null ? "failed" : "ready";
        if (data?.AvatarUrl is string avatar)
        {
            try { _steamAvatar = await Task.Run(() => ImageLoader.FromUrlAsync(avatar, 184)); } catch (Exception) { }
        }
        if (!ct.IsCancellationRequested && Host.IsTop(this)) Build();
    }

    /// <summary>The banner shows artwork from the game you played last.</summary>
    private async Task LoadBannerAsync()
    {
        var last = Host.AllGames.Where(g => g.IsGame && g.RecentAt.Year > 2000).OrderByDescending(g => g.RecentAt).FirstOrDefault();
        if (last == null) return;
        var key = Host.Settings.SteamGridDbApiKey;
        var ct = _cts.Token;
        try
        {
            _banner = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var path = await client.GetHeroAsync(last, ct);
                return path == null ? null : ImageLoader.FromFile(path, 1280);
            }, ct);
        }
        catch (Exception) { }
        if (_banner != null && !ct.IsCancellationRequested && Host.IsTop(this)) Build();
    }

    // ---------------------------------------------------------------- layout helpers

    private static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(CardRadius), Padding = padding ?? new Thickness(20), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return card;
    }

    private void Section(string title, string? action = null, Action? click = null)
    {
        var row = new DockPanel { Margin = new Thickness(2, 26, 0, 10), LastChildFill = true };
        if (action != null && click != null)
        {
            var link = Ui.PanelButton(action);
            link.FontSize = 15;
            link.Padding = new Thickness(0);
            link.DataContext = "section:" + title;
            link.Click += (_, _) => click();
            Track(link);
            DockPanel.SetDock(link, Dock.Right);
            row.Children.Add(link);
        }
        row.Children.Add(Ui.Text(title, 22, "FgBrush", bold: true));
        _content.Children.Add(row);
    }

    /// <summary>Remembers which card had the focus so a refresh doesn't throw the highlight back to the top.</summary>
    private void Track(Button button)
    {
        button.GotKeyboardFocus += (_, _) => _focusId = button.DataContext as string;
    }

    private Button CardButton(UIElement content, string id, Action click, double? width = null)
    {
        var button = Ui.Tile(content);
        button.Margin = new Thickness(0, 0, 12, 12);
        if (width != null) button.Width = width.Value;
        button.DataContext = id;
        button.Click += (_, _) => click();
        Track(button);
        return button;
    }

    private static TextBlock Line(string text, double size, string brush = "SubtleBrush", bool bold = false)
    {
        var t = Ui.Text(text, size, brush, bold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static FrameworkElement Bar(double fraction, double height = 8)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var grid = new Grid();
        grid.Children.Add(track);
        grid.Children.Add(fill);
        grid.SizeChanged += (_, e) => fill.Width = Math.Max(height, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return grid;
    }

    private static Border Pill(string text, bool accent)
    {
        var label = Ui.Text(text, 13, accent ? "BgBrush" : "FgBrush", bold: true);
        var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Child = label, Margin = new Thickness(0, 0, 6, 0) };
        pill.SetResourceReference(Border.BackgroundProperty, accent ? "AccentBrush" : "CircleBrush");
        return pill;
    }

    /// <summary>A square picture: bound to a game's cover, or loaded from a web address.</summary>
    private static Border Picture(double size, Game? game, string? url, string glyph, double radius = 8)
    {
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), ClipToBounds = true };
        box.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var grid = new Grid();
        var mark = Ui.Glyph(glyph, size * 0.32, "SubtleBrush");
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(mark);
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        grid.Children.Add(image);
        // Rounded corners for the picture itself.
        grid.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        box.Child = grid;

        if (game != null)
        {
            image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = game });
            void Face()
            {
                bool centered = game.UsesIcon && !game.IconFills;
                image.Stretch = centered ? Stretch.Uniform : Stretch.UniformToFill;
                image.Margin = centered ? new Thickness(size * 0.22) : new Thickness(0);
                if (game.UsesIcon && game.TileBackground != null) box.Background = game.TileBackground;
            }
            Face();
            System.ComponentModel.PropertyChangedEventManager.AddHandler(game, (_, _) => Face(), "");
        }
        else if (!string.IsNullOrEmpty(url))
        {
            _ = Load();
            async Task Load()
            {
                try
                {
                    var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, (int)(size * 2)));
                    if (source != null) image.Source = source;
                }
                catch (Exception) { }
            }
        }
        return box;
    }

    // ---------------------------------------------------------------- the page

    private void Build()
    {
        _content.Children.Clear();
        var games = Host.AllGames.ToList();

        BuildBanner();
        BuildHeadlines(games);
        BuildRecent(games);
        BuildAchievements(games);
        BuildBadges(games);
        BuildWishlist();
        BuildRecords(games);

        var target = _content.FindDescendants<Button>().FirstOrDefault(b => _focusId != null && b.DataContext as string == _focusId)
                     ?? _content.FindDescendants<Button>().FirstOrDefault();
        FocusLater(target);
    }

    private void BuildBanner()
    {
        var steam = _steam;
        var banner = new Grid { Height = 236, ClipToBounds = true };
        var shape = new Border { CornerRadius = new CornerRadius(CardRadius + 4), ClipToBounds = true };
        shape.SetResourceReference(Border.BackgroundProperty, "TileBrush");

        var inside = new Grid();
        if (_banner != null)
        {
            inside.Children.Add(new Image
            {
                Source = _banner, Stretch = Stretch.UniformToFill, Opacity = 0.75,
                Effect = new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance }
            });
        }
        // A dark wash from the left so the name always reads, whatever the artwork.
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0, 0, 0), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0, 0, 0), 0.55));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0, 0, 0), 1));
        inside.Children.Add(new Border { Background = wash });

        var row = new DockPanel { Margin = new Thickness(32, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };

        // The picture: Steam's if it could be loaded, otherwise the launcher's own, in a ring of the highlight color.
        FrameworkElement face;
        if (_steamAvatar != null)
            face = new Border { Width = 132, Height = 132, CornerRadius = new CornerRadius(66), Background = new ImageBrush(_steamAvatar) { Stretch = Stretch.UniformToFill } };
        else
            face = Avatars.Build(Host.Settings.Avatar, 132);
        var ring = new Border
        {
            Width = 146, Height = 146, CornerRadius = new CornerRadius(73), BorderThickness = new Thickness(4), Child = face,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 }
        };
        ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        DockPanel.SetDock(ring, Dock.Left);
        row.Children.Add(ring);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var change = Ui.PanelButton("Change picture");
        change.DataContext = "banner:picture";
        change.Margin = new Thickness(0, 0, 0, 10);
        change.Click += (_, _) => Host.Push(new AvatarScreen());
        Track(change);
        buttons.Children.Add(change);
        var open = Ui.PanelButton(steam?.ProfileUrl != null ? "Steam profile" : "Open Steam");
        open.DataContext = "banner:steam";
        open.Click += (_, _) => ProcessUtil.Open(steam?.ProfileUrl != null ? "steam://openurl/" + steam.ProfileUrl : "steam://open/main");
        Track(open);
        buttons.Children.Add(open);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        var info = new StackPanel { Margin = new Thickness(28, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(steam?.Name ?? Environment.UserName, 38, "FgBrush", bold: true);
        name.Foreground = Brushes.White;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(name);

        var facts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (steam?.Level is int level)
        {
            // Steam's level badge: a ring with the number.
            var badge = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2.5),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = level.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            facts.Children.Add(badge);
            facts.Children.Add(new TextBlock { Text = "Steam level", Foreground = Brushes.White, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) });
        }
        if (steam?.MemberSince is DateTime since)
            facts.Children.Add(new TextBlock { Text = $"Member since {since:yyyy}", Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
        if (facts.Children.Count > 0) info.Children.Add(facts);

        if (steam is { Level: not null, XpToNextLevel: > 0 })
        {
            int span = steam.XpToNextLevel + (steam.Xp - steam.XpForThisLevel);
            double fraction = span > 0 ? (steam.Xp - steam.XpForThisLevel) / (double)span : 0;
            var xp = Bar(fraction, 6);
            xp.Margin = new Thickness(0, 14, 0, 4);
            xp.MaxWidth = 420;
            xp.HorizontalAlignment = HorizontalAlignment.Left;
            xp.Width = 420;
            info.Children.Add(xp);
            info.Children.Add(new TextBlock { Text = $"{steam.XpToNextLevel:N0} XP to level {steam.Level + 1}", Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), FontSize = 14 });
        }
        else if (_steamState == "nokey")
        {
            info.Children.Add(new TextBlock { Text = "Add your Steam Web API key to show your Steam level, badges and recent games.", Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        }
        else if (_steamState == "loading")
        {
            info.Children.Add(new TextBlock { Text = "Getting your Steam profile…", Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), FontSize = 16, Margin = new Thickness(0, 8, 0, 0) });
        }
        row.Children.Add(info);

        inside.Children.Add(row);
        shape.Child = inside;
        banner.Children.Add(shape);
        _content.Children.Add(banner);

        if (_steamState == "nokey")
        {
            var add = Ui.PanelButton("Add Steam Web API key");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 14, 0, 0);
            add.DataContext = "banner:key";
            add.Click += (_, _) => _ = SettingsScreen.EditSteamKeyAsync(Host, () => _ = LoadSteamAsync(refresh: true));
            Track(add);
            _content.Children.Add(add);
        }
        else if (_steamState == "failed")
        {
            var note = Ui.Text("Steam didn't return your profile. Check the API key in Settings, and that your Steam profile's game details " +
                               "aren't private. Press " + Hints.KeyLabel("Options") + " to try again.", 15, "SubtleBrush");
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(4, 12, 0, 0);
            _content.Children.Add(note);
        }
    }

    private void BuildHeadlines(List<Game> games)
    {
        double steamMinutes = games.Where(g => g.IsSteam).Sum(g => (double)(SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0));
        double localSeconds = Host.Playtime.AllTime().Sum(x => x.Seconds);
        double totalSeconds = Math.Max(steamMinutes * 60, 0) + games.Where(g => !g.IsSteam).Sum(g => Host.Playtime.TotalSeconds(g.Key));
        if (totalSeconds <= 0) totalSeconds = localSeconds;

        int unlocked = 0, total = 0;
        foreach (var g in games.Where(g => g.IsSteam))
            if (Host.Achievements.ProgressOf(g.AppId) is { Total: > 0 } p) { unlocked += p.Unlocked; total += p.Total; }

        int owned = games.Count(g => g.IsGame) + Host.NotInstalledGames.Count;

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 18, 0, 0) };
        void Stat(string id, string glyph, string value, string label, Action click)
        {
            var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            var icon = Ui.Glyph(glyph, 22, "AccentBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(icon);
            stack.Children.Add(Line(value, 30, "FgBrush", bold: true));
            stack.Children.Add(Line(label, 15));
            var card = Card(stack, new Thickness(0));
            var button = CardButton(card, id, click);
            button.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(button);
        }
        Stat("stat:time", "", totalSeconds >= 3600 ? $"{totalSeconds / 3600:N0} h" : PlaytimeStore.Format(totalSeconds), "Time played", () => Host.Push(new PlaytimeScreen()));
        Stat("stat:games", "", owned.ToString("N0"), $"Games  ·  {games.Count(g => g.IsGame && g.IsInstalled):N0} installed", () => Host.Push(new LibraryScreen()));
        Stat("stat:achievements", "", total > 0 ? $"{100.0 * unlocked / total:0}%" : "–",
            total > 0 ? $"Achievements  ·  {unlocked:N0} of {total:N0}" : "Achievements", () => Host.Push(new UnlocksScreen()));
        Stat("stat:wishlist", "", Host.WishlistState == "ready" ? Host.WishlistGames.Count.ToString("N0") : "–",
            "On your wishlist", () => ProcessUtil.Open("steam://url/UserWishlist"));
        _content.Children.Add(grid);
    }

    private void BuildRecent(List<Game> games)
    {
        // Steam's own list when there is one (it knows about other PCs too), with the launcher's covers where it can.
        var bySteamId = games.Where(g => g.IsSteam).GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First());
        var items = new List<(Game? Game, string Name, string Detail, string? Icon, int AppId)>();
        if (_steam?.Recent is { Count: > 0 } recent)
        {
            foreach (var r in recent)
            {
                bySteamId.TryGetValue(r.AppId, out var local);
                string detail = r.MinutesTwoWeeks >= 60 ? $"{r.MinutesTwoWeeks / 60.0:0.#} h this fortnight" : $"{r.MinutesTwoWeeks} min this fortnight";
                items.Add((local, local?.Name ?? r.Name, detail, r.IconUrl, r.AppId));
            }
        }
        foreach (var g in games.Where(g => g.RecentAt.Year > 2000).OrderByDescending(g => g.RecentAt))
        {
            if (items.Count >= 10) break;
            if (items.Any(i => ReferenceEquals(i.Game, g) || (g.IsSteam && i.AppId == g.AppId))) continue;
            items.Add((g, g.Name, Ui.Ago(g.RecentAt), null, g.IsSteam ? g.AppId : 0));
        }
        if (items.Count == 0) return;

        Section("Recently played", "Play time details", () => Host.Push(new PlaytimeScreen()));
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items.Take(10))
        {
            var it = item;
            var stack = new StackPanel { Width = 150 };
            stack.Children.Add(Picture(150, it.Game, it.Game == null ? it.Icon : null, ""));
            var title = Line(it.Name, 15, "FgBrush", bold: true);
            title.Margin = new Thickness(2, 8, 2, 0);
            stack.Children.Add(title);
            var detail = Line(it.Detail, 13);
            detail.Margin = new Thickness(2, 1, 2, 2);
            stack.Children.Add(detail);
            strip.Children.Add(CardButton(stack, "recent:" + it.Name, () =>
            {
                if (it.Game != null) Host.Push(new DetailsScreen(it.Game));
                else if (it.AppId > 0) ProcessUtil.Open("steam://nav/games/details/" + it.AppId);
            }));
        }
        _content.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly, Focusable = false, Content = strip
        });
        if (_steam?.RecentTotal > 0)
        {
            var note = Line($"{_steam.RecentTotal} games played on Steam in the last two weeks.", 14);
            note.Margin = new Thickness(4, 0, 0, 0);
            _content.Children.Add(note);
        }
    }

    private void BuildAchievements(List<Game> games)
    {
        int unlocked = 0, total = 0, perfect = 0, tracked = 0;
        Game? closest = null;
        double closestFraction = 0;
        foreach (var g in games.Where(g => g.IsSteam))
        {
            if (Host.Achievements.ProgressOf(g.AppId) is not { Total: > 0 } p) continue;
            tracked++;
            unlocked += p.Unlocked;
            total += p.Total;
            if (p.Unlocked == p.Total) perfect++;
            else if ((double)p.Unlocked / p.Total > closestFraction) { closestFraction = (double)p.Unlocked / p.Total; closest = g; }
        }
        var feed = Host.Achievements.Feed.OrderByDescending(u => u.When).Take(4).ToList();
        if (total == 0 && feed.Count == 0) return;

        Section("Achievements", "All recent", () => Host.Push(new UnlocksScreen()));
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        var summary = new StackPanel();
        if (total > 0)
        {
            summary.Children.Add(Line($"{unlocked:N0} of {total:N0} unlocked", 20, "FgBrush", bold: true));
            var bar = Bar((double)unlocked / total, 10);
            bar.Margin = new Thickness(0, 12, 0, 12);
            summary.Children.Add(bar);
            summary.Children.Add(Line($"{perfect} of {tracked} games completed 100%", 15));
            if (closest != null)
                summary.Children.Add(Line($"Closest to 100%: {closest.Name} ({closestFraction * 100:0}%)", 15));
        }
        else
        {
            summary.Children.Add(Line("Totals appear once a game's details page has been opened.", 15));
        }
        var summaryButton = CardButton(Card(summary), "ach:summary", () => Host.Push(new UnlocksScreen()));
        layout.Children.Add(summaryButton);

        var list = new StackPanel();
        foreach (var u in feed)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var icon = Picture(46, null, u.Icon, "", 6);
            icon.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Line(u.Name, 16, "FgBrush", bold: true));
            text.Children.Add(Line(u.Game + "  ·  " + Ui.Ago(u.When), 13));
            row.Children.Add(text);
            list.Children.Add(row);
        }
        if (feed.Count == 0) list.Children.Add(Line("Achievements you earn while the launcher is open show here.", 15));
        var listButton = CardButton(Card(list), "ach:feed", () => Host.Push(new UnlocksScreen()));
        Grid.SetColumn(listButton, 1);
        layout.Children.Add(listButton);
        _content.Children.Add(layout);
    }

    private void BuildBadges(List<Game> games)
    {
        if (_steam?.Badges is not { Count: > 0 } badges) return;
        var names = games.Where(g => g.IsSteam).GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (var g in Host.NotInstalledGames) names.TryAdd(g.AppId, g.Name);

        Section($"Steam badges  ({badges.Count})", "Open in Steam", () => ProcessUtil.Open(_steam?.ProfileUrl is string u ? "steam://openurl/" + u + "badges/" : "steam://open/main"));
        var wrap = new WrapPanel();
        foreach (var b in badges.Take(12))
        {
            string name = b.AppId > 0 ? (names.TryGetValue(b.AppId, out var n) ? n : "Game badge") : b.Name;
            var stack = new DockPanel { Margin = new Thickness(14, 12, 16, 12) };
            var icon = Ui.Glyph(b.AppId > 0 ? "" : "", 24, "AccentBrush");
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            stack.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Line(name, 15, "FgBrush", bold: true));
            text.Children.Add(Line((b.Level > 0 ? $"Level {b.Level}  ·  " : "") + $"{b.Xp:N0} XP", 13));
            stack.Children.Add(text);
            var card = Card(stack, new Thickness(0));
            card.Width = 260;
            wrap.Children.Add(CardButton(card, "badge:" + b.BadgeId + ":" + b.AppId,
                () => ProcessUtil.Open(_steam?.ProfileUrl is string u ? "steam://openurl/" + u + "badges/" : "steam://open/main")));
        }
        _content.Children.Add(wrap);
    }

    private void BuildWishlist()
    {
        if (Host.WishlistState != "ready" || Host.WishlistGames.Count == 0) return;
        var items = Host.WishlistGames.OrderByDescending(g => g.DiscountPercent).ThenByDescending(g => g.RecentAt).Take(10).ToList();
        Host.EnsureCovers(items);

        int onSale = Host.WishlistGames.Count(g => g.DiscountPercent > 0);
        Section(onSale > 0 ? $"Wishlist  ·  {onSale} on sale" : "Wishlist", "Open in Steam", () => ProcessUtil.Open("steam://url/UserWishlist"));
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var game in items)
        {
            var g = game;
            var stack = new StackPanel { Width = 150 };
            stack.Children.Add(Picture(150, g, null, ""));
            var title = Line(g.Name, 15, "FgBrush", bold: true);
            title.Margin = new Thickness(2, 8, 2, 4);
            stack.Children.Add(title);
            var prices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 2) };
            if (g.DiscountPercent > 0) prices.Children.Add(Pill("-" + g.DiscountPercent + "%", accent: true));
            if (g.Price.Length > 0) prices.Children.Add(Pill(g.Price, accent: false));
            stack.Children.Add(prices);
            strip.Children.Add(CardButton(stack, "wish:" + g.AppId, () => ProcessUtil.Open("steam://store/" + g.AppId)));
        }
        _content.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly, Focusable = false, Content = strip
        });
    }

    private void BuildRecords(List<Game> games)
    {
        var playtime = Host.Playtime;
        var cards = new List<(string Glyph, string Value, string Label)>();
        var today = DateTime.Today;
        cards.Add(("", PlaytimeStore.Format(playtime.SecondsOn(today)), "Today"));
        cards.Add(("", PlaytimeStore.Format(playtime.GamesInWeek(PlaytimeStore.WeekStart(today)).Sum(x => x.Seconds)), "This week"));
        var allTime = playtime.AllTime();
        if (allTime.Count > 0) cards.Add(("", allTime[0].Name, "Most played here  ·  " + PlaytimeStore.Format(allTime[0].Seconds)));
        if (playtime.LongestSession() is { } longest) cards.Add(("", PlaytimeStore.Format(longest.Seconds), $"Longest session  ·  {longest.Name}"));
        var streak = playtime.LongestStreak();
        if (streak.Days > 1) cards.Add(("", $"{streak.Days} days", "Longest streak"));
        if (playtime.BestMonth() is { } month) cards.Add(("", month.Month, "Busiest month  ·  " + PlaytimeStore.Format(month.Seconds)));
        int played = games.Count(g => g.IsGame && g.RecentAt.Year > 2000);
        int gameCount = games.Count(g => g.IsGame);
        if (gameCount > 0) cards.Add(("", $"{100.0 * played / gameCount:0}%", $"Of your games played  ·  {played} of {gameCount}"));
        long bytes = games.Where(g => g.IsInstalled).Sum(g => g.SizeOnDisk);
        if (bytes > 0) cards.Add(("", SystemStats.Bytes(bytes), "Space used by installed games"));

        Section("Records and numbers");
        var grid = new UniformGrid { Columns = 4 };
        foreach (var (glyph, value, label) in cards)
        {
            var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            var icon = Ui.Glyph(glyph, 18, "AccentBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(icon);
            stack.Children.Add(Line(value, 20, "FgBrush", bold: true));
            stack.Children.Add(Line(label, 13));
            var button = CardButton(Card(stack, new Thickness(0)), "record:" + label, () => Host.Push(new PlaytimeScreen()));
            button.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(button);
        }
        _content.Children.Add(grid);
    }
}

internal static class VisualTreeSearch
{
    /// <summary>Every element of a type under <paramref name="root"/>, in visual order.</summary>
    public static IEnumerable<T> FindDescendants<T>(this DependencyObject root) where T : DependencyObject
    {
        // Logical children cover content that hasn't been laid out yet (just built); visual children cover templates.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var deeper in child.FindDescendants<T>()) yield return deeper;
        }
    }
}
