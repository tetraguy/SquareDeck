using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Play time measured by the launcher: a week at a glance, and all-time totals.</summary>
public sealed class PlaytimeScreen : ListScreen
{
    private int _weeksBack; // 0 = this week

    public PlaytimeScreen() : base("Your play time") { }

    protected override void Build()
    {
        var store = Host.Playtime;
        var start = PlaytimeStore.WeekStart(DateTime.Now).AddDays(-7 * _weeksBack);
        var days = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
        var perDay = days.Select(store.SecondsOn).ToList();
        double total = perDay.Sum();

        string label = _weeksBack switch { 0 => "This week", 1 => "Last week", _ => _weeksBack + " weeks ago" };
        AddRow("Week", $"{label}  ({start:MMM d} – {start.AddDays(6):MMM d})", "", click: async () =>
        {
            int choice = await Host.AskAsync("Which week?", null, "This week", "Last week", "2 weeks ago", "3 weeks ago");
            if (choice < 0) return;
            _weeksBack = choice;
            Rebuild();
        });

        AddHeader("Total: " + PlaytimeStore.Format(total));
        double busiest = Math.Max(1, perDay.Max());
        for (int i = 0; i < 7; i++)
            AddBar(days[i].ToString("dddd"), perDay[i] / busiest, PlaytimeStore.Format(perDay[i]));

        var games = store.GamesInWeek(start);
        AddHeader("Games that week");
        if (games.Count == 0) AddNote("Nothing was played that week while the launcher was open.");
        double top = games.Count > 0 ? games[0].Seconds : 1;
        foreach (var g in games)
            AddBar(g.Name, g.Seconds / top, PlaytimeStore.Format(g.Seconds));

        var allTime = store.AllTime();
        if (allTime.Count > 0)
        {
            AddHeader("All time");
            double best = allTime[0].Seconds;
            foreach (var g in allTime.Take(15))
                AddBar(g.Name, g.Seconds / best, PlaytimeStore.Format(g.Seconds));
        }

        AddHeader("Personal records");
        var longest = store.LongestSession();
        var streak = store.LongestStreak();
        var month = store.BestMonth();
        if (longest == null && streak.Days == 0 && month == null) AddNote("Records appear after your first session.");
        if (longest != null)
            AddRow("Longest session", $"{PlaytimeStore.Format(longest.Seconds)}  \u00B7  {longest.Name}, {longest.Start:MMM d}", "\uE823");
        if (streak.Days > 0)
            AddRow("Longest streak", $"{streak.Days} {(streak.Days == 1 ? "day" : "days")} in a row, ending {streak.End:MMM d}", "\uE787");
        if (month is { } topMonth)
            AddRow("Most-played month", $"{topMonth.Month}  \u00B7  {PlaytimeStore.Format(topMonth.Seconds)}", "\uE787");

        AddNote("Measured by the launcher while it is open, for every game it can see running. " +
                "Steam's own lifetime totals are on each game's details page.");
    }

    /// <summary>A row with a name, a proportional bar and a value.</summary>
    private void AddBar(string label, double fraction, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        grid.Children.Add(Ui.Text(label, 19));

        const double barWidth = 400;
        var track = new Border { Height = 10, Width = barWidth, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
        track.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var fill = new Border
        {
            Height = 10,
            Width = Math.Clamp(fraction, 0, 1) * barWidth,
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var bar = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        bar.Children.Add(track);
        bar.Children.Add(fill);
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        var v = Ui.Text(value, 18, "SubtleBrush");
        v.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(v, 2);
        grid.Children.Add(v);

        Rows.Children.Add(new Button { Style = Ui.StyleOf("RowButton"), Content = grid });
    }
}

/// <summary>
/// Steam friends as a modern hub: big cards for friends in a game (over that game's art, with Play when it's
/// installed here), games several friends are playing, pinned friends first, online friends as avatar tiles
/// with a status ring, and offline friends on request with when they were last on.
/// X filters, Y on a friend pins or unpins them, A opens their options.
/// </summary>
public sealed class FriendsScreen : GridScreen
{
    private enum Filter { All, Playing, Online, Pinned }

    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(60) };
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> Pictures = new();

    /// <summary>Forgets friends' pictures (fetched again when the page opens).</summary>
    public static void ForgetPictures() => Pictures.Clear();
    private CancellationTokenSource _cts = new();
    private List<Friend>? _friends;
    private string? _error;
    private bool _loading, _showOffline;
    private Filter _filter = Filter.All;
    private string? _focusKey;
    private DateTime _loadedAt;

    public FriendsScreen() : base("Friends",
        "{Select}  Open      {Options}  Pin      {Details}  Filter      {Back}  Back")
    {
        _refresh.Tick += (_, _) => _ = LoadAsync();
    }

    public override void OnShown()
    {
        Build();
        _ = LoadAsync();
        _refresh.Start();
    }

    public override void OnClosed()
    {
        _refresh.Stop();
        _cts.Cancel();
    }

    public override void OnResumed() => Build(); // a refresh may have landed while a dialog was open

    private bool HasKey => Host.Settings.SteamWebApiKey.Length > 0;
    private bool IsPinned(Friend f) => Host.Settings.PinnedFriends.Contains(f.Id);

    private async Task LoadAsync()
    {
        if (!HasKey || _loading) return;
        _loading = true;
        if (_friends == null) Build();
        try
        {
            var key = Host.Settings.SteamWebApiKey;
            var ct = _cts.Token;
            (_friends, _error) = await Task.Run(() => SteamFriends.GetAsync(key, ct), ct);
            _loadedAt = DateTime.Now;
        }
        catch (OperationCanceledException) { return; }
        finally { _loading = false; }
        if (Host.IsTop(this)) Build();
    }

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        Body.Children.Clear();
        Tiles.Children.Clear();
        Button? focus = null;

        if (!HasKey)
        {
            Note("See which friends are online and what they're playing, join their game, and message them. " +
                 "It needs your own Steam Web API key, which is free: sign in at steamcommunity.com/dev/apikey, " +
                 "enter any domain name, and copy the key it shows you.");
            var row = Row();
            var add = AddTile(ActionFace("", "Add API key"), null, 220, AddKey, into: row);
            AddTile(ActionFace("", "Open the key page"), null, 220,
                () => ProcessUtil.Open("https://steamcommunity.com/dev/apikey"), into: row);
            FocusLater(add);
            return;
        }
        if (_error != null)
        {
            Note(_error);
            var row = Row();
            var again = AddTile(ActionFace("", "Try again"), null, 220, () => _ = LoadAsync(), into: row);
            AddTile(ActionFace("", "Change API key"), null, 220, AddKey, into: row);
            FocusLater(again);
            return;
        }
        if (_friends == null) { Note("Loading friends…"); return; }

        // Pinned first everywhere, then by name (the service already put players and online friends first).
        var ordered = _friends.OrderByDescending(IsPinned).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var playing = ordered.Where(f => f.InGame).ToList();
        var online = ordered.Where(f => f.IsOnline && !f.InGame).ToList();
        var offline = ordered.Where(f => !f.IsOnline).OrderByDescending(IsPinned)
            .ThenByDescending(f => f.LastOnline ?? DateTime.MinValue).ToList();
        if (_filter == Filter.Pinned)
        {
            playing = playing.Where(IsPinned).ToList();
            online = online.Where(IsPinned).ToList();
            offline = offline.Where(IsPinned).ToList();
        }

        // Summary and actions along the top.
        var top = new StackPanel { Margin = new Thickness(4, 0, 10, 12) };
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var filterButton = Ui.PanelButton("", FilterName);
        filterButton.Click += (_, _) => CycleFilter();
        var alerts = Ui.PanelButton("", "Alerts: " + AlertsName);
        alerts.Margin = new Thickness(8, 0, 0, 0);
        alerts.Click += (_, _) => CycleAlerts();
        var addFriend = Ui.PanelButton("", "Add friend");
        addFriend.Margin = new Thickness(8, 0, 0, 0);
        addFriend.Click += (_, _) => ProcessUtil.Open("steam://friends/add");
        var chat = Ui.PanelButton("", "Steam chat");
        chat.Margin = new Thickness(8, 0, 0, 0);
        chat.Click += (_, _) => ProcessUtil.Open("steam://open/friends");
        actions.Children.Add(filterButton);
        actions.Children.Add(alerts);
        actions.Children.Add(addFriend);
        actions.Children.Add(chat);
        var summary = new StackPanel();
        summary.Children.Add(Ui.Text($"{_friends.Count(f => f.InGame)} playing  ·  {_friends.Count(f => f.IsOnline)} online  ·  {_friends.Count} friends", 18, "FgBrush", bold: true));
        summary.Children.Add(Ui.Text("Updated " + (_loading ? "now…" : _loadedAt.ToString("t")) + "  ·  refreshes every minute", 13, "SubtleBrush"));
        top.Children.Add(summary);
        top.Children.Add(actions);
        Body.Children.Add(top);

        void Track(Button tile, Friend f)
        {
            tile.GotKeyboardFocus += (_, _) => _focusKey = f.Id;
            if (f.Id == _focusKey && focus == null) focus = tile;
        }

        // Games several friends are in right now.
        var together = playing.Where(f => f.GameAppId != null).GroupBy(f => f.GameAppId!.Value)
            .Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).Take(4).ToList();
        if (together.Count > 0 && _filter is Filter.All or Filter.Playing)
        {
            Heading("Popular with your friends", null);
            var row = Row();
            foreach (var g in together)
            {
                var group = g.ToList();
                var t = AddTile(PopularFace(g.Key, group), null, 340, () => _ = OpenGameAsync(g.Key, group), into: row);
                t.GotKeyboardFocus += (_, _) => _focusKey = "game:" + g.Key;
                if (_focusKey == "game:" + g.Key) focus ??= t;
            }
        }

        if (_filter != Filter.Online)
        {
            Heading("Playing now", playing.Count);
            if (playing.Count == 0) Note("Nobody is in a game right now.");
            var row = Row();
            foreach (var f in playing) Track(AddTile(PlayingFace(f), null, 340, () => _ = OpenFriendAsync(f), f, row), f);
        }

        if (_filter != Filter.Playing)
        {
            Heading("Online", online.Count);
            if (online.Count == 0) Note(_filter == Filter.Pinned ? "No pinned friends are online." : "Nobody else is online.");
            var row = Row();
            foreach (var f in online) Track(AddTile(PersonFace(f), null, 150, () => _ = OpenFriendAsync(f), f, row), f);
        }

        if (_filter is Filter.All or Filter.Pinned && offline.Count > 0)
        {
            Heading("Offline", offline.Count);
            var row = Row();
            if (_showOffline || _filter == Filter.Pinned)
                foreach (var f in offline) Track(AddTile(PersonFace(f), null, 150, () => _ = OpenFriendAsync(f), f, row), f);
            else
            {
                var show = AddTile(ActionFace("", $"Show {offline.Count} offline"), null, 220, () =>
                {
                    _showOffline = true;
                    _focusKey = offline[0].Id;
                    Build();
                }, into: row);
            }
        }

        FocusLater(focus ?? Body.Children.OfType<WrapPanel>().SelectMany(w => w.Children.OfType<Button>()).FirstOrDefault()
                   ?? (UIElement)filterButton);
    }

    private WrapPanel Row()
    {
        var row = new WrapPanel();
        Body.Children.Add(row);
        return row;
    }

    private void Heading(string text, int? count)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 10) };
        line.Children.Add(Ui.Text(text, 18, "FgBrush", bold: true));
        if (count is int n)
        {
            var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 1, 9, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            pill.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            pill.Child = Ui.Text(n.ToString(), 13, "SubtleBrush", bold: true);
            line.Children.Add(pill);
        }
        Body.Children.Add(line);
    }

    private void Note(string text)
    {
        var note = Ui.Text(text, 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 0, 20, 12);
        Body.Children.Add(note);
    }

    // ---------------------------------------------------------------- faces

    private static System.Windows.Media.Color StatusColor(Friend f) => f.InGame
        ? System.Windows.Media.Color.FromRgb(0x8B, 0xC5, 0x3F)        // in game: Steam's green
        : f.State switch
        {
            0 => System.Windows.Media.Color.FromRgb(0x6B, 0x6B, 0x6B),   // offline
            2 => System.Windows.Media.Color.FromRgb(0xE0, 0x4A, 0x4A),   // busy
            3 or 4 => System.Windows.Media.Color.FromRgb(0xE8, 0xA8, 0x3A), // away / snooze
            _ => System.Windows.Media.Color.FromRgb(0x57, 0xCB, 0xDE)    // online: Steam's blue
        };

    /// <summary>A round picture with a ring in their status color and a dot in the corner.</summary>
    private Grid Avatar(Friend f, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        var color = StatusColor(f);
        var picture = new System.Windows.Shapes.Ellipse { Margin = new Thickness(4) };
        picture.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "CircleBrush");
        grid.Children.Add(picture);
        var initial = Ui.Text(f.Name.Length > 0 ? f.Name[..1].ToUpperInvariant() : "?", size * 0.38, "SubtleBrush", bold: true);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(initial);
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Stroke = new System.Windows.Media.SolidColorBrush(color),
            StrokeThickness = Math.Max(2.5, size * 0.035),
            Opacity = f.IsOnline ? 1 : 0.5
        });
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = size * 0.22, Height = size * 0.22,
            Fill = new System.Windows.Media.SolidColorBrush(color),
            StrokeThickness = 3,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, size * 0.02, size * 0.02)
        };
        dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TileBrush");
        grid.Children.Add(dot);
        _ = FillPictureAsync(f, picture, initial, size);
        return grid;
    }

    private async Task FillPictureAsync(Friend f, System.Windows.Shapes.Ellipse target, UIElement placeholder, double size)
    {
        string url = size > 80 && f.AvatarFull.Length > 0 ? f.AvatarFull : f.Avatar;
        if (url.Length == 0) return;
        if (!Pictures.TryGetValue(url, out var picture))
        {
            try { picture = await Task.Run(() => ImageLoader.FromUrlAsync(url, size > 80 ? 184 : 64)); }
            catch (Exception) { picture = null; }
            Pictures[url] = picture;
        }
        if (picture == null) return;
        target.Fill = new System.Windows.Media.ImageBrush(picture) { Stretch = System.Windows.Media.Stretch.UniformToFill };
        placeholder.Visibility = Visibility.Collapsed;
    }

    private static string HeaderArt(int appId) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

    private static readonly Dictionary<int, System.Windows.Media.ImageSource?> Art = new();

    /// <summary>A 340 × 150 card over the game's art, darkened at the bottom for the text.</summary>
    private Border ArtCard(int? appId, out Grid content)
    {
        content = new Grid();
        var art = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill, Opacity = 0.9 };
        RenderOptions.SetBitmapScalingMode(art, System.Windows.Media.BitmapScalingMode.HighQuality);
        content.Children.Add(art);
        content.Children.Add(new Border
        {
            Background = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(0x10, 0, 0, 0), System.Windows.Media.Color.FromArgb(0xE0, 0, 0, 0), 90)
        });
        var box = new Border { Width = 340, Height = 150, ClipToBounds = true, Child = content };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        double corner = Math.Max(Theme.TileCorner, 12);
        box.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 340, 150), corner, corner);
        if (appId is int id) _ = FillArtAsync(id, art);
        return box;
    }

    private static async Task FillArtAsync(int appId, Image target)
    {
        if (!Art.TryGetValue(appId, out var picture))
        {
            try { picture = await Task.Run(() => ImageLoader.FromUrlAsync(HeaderArt(appId), 460)); }
            catch (Exception) { picture = null; }
            Art[appId] = picture;
        }
        target.Source = picture;
    }

    private Game? Installed(int? appId) => appId is int id
        ? Host.AllGames.FirstOrDefault(g => g.IsSteam && g.AppId == id && g.IsInstalled) : null;

    private UIElement PlayingFace(Friend f)
    {
        var box = ArtCard(f.GameAppId, out var content);
        var row = new DockPanel { Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Bottom };
        var avatar = Avatar(f, 64);
        avatar.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text((IsPinned(f) ? "★ " : "") + f.Name, 18, bold: true);
        name.Foreground = System.Windows.Media.Brushes.White;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var game = Ui.Text(f.Game ?? "", 15);
        game.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB8, 0xE0, 0x7A));
        game.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        text.Children.Add(game);
        row.Children.Add(text);
        content.Children.Add(row);
        if (Installed(f.GameAppId) != null) content.Children.Add(Badge("", "Installed"));
        return box;
    }

    private UIElement PopularFace(int appId, List<Friend> friends)
    {
        var box = ArtCard(appId, out var content);
        var stack = new StackPanel { Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Bottom };
        var faces = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var f in friends.Take(5))
        {
            var a = Avatar(f, 40);
            a.Margin = new Thickness(0, 0, -8, 0);
            faces.Children.Add(a);
        }
        stack.Children.Add(faces);
        var title = Ui.Text(friends[0].Game ?? "", 18, bold: true);
        title.Foreground = System.Windows.Media.Brushes.White;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(title);
        var who = Ui.Text($"{friends.Count} friends playing", 14);
        who.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD));
        stack.Children.Add(who);
        content.Children.Add(stack);
        if (Installed(appId) != null) content.Children.Add(Badge("", "Installed"));
        return box;
    }

    private static Border Badge(string glyph, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 11, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
        var t = Ui.Text(text, 12, bold: true);
        t.Foreground = System.Windows.Media.Brushes.White;
        line.Children.Add(t);
        return new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0x2B, 0x7D, 0xE0)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = line
        };
    }

    private UIElement PersonFace(Friend f)
    {
        var stack = new StackPanel { Width = 150, Margin = new Thickness(0, 12, 0, 10) };
        var avatar = Avatar(f, 96);
        avatar.HorizontalAlignment = HorizontalAlignment.Center;
        if (!f.IsOnline) avatar.Opacity = 0.6;
        stack.Children.Add(avatar);
        var name = Ui.Text((IsPinned(f) ? "★ " : "") + f.Name, 15, "FgBrush", bold: true);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(8, 10, 8, 0);
        stack.Children.Add(name);
        var status = Ui.Text(f.IsOnline ? f.Status : f.LastSeenText.Replace("Last online ", ""), 13, "SubtleBrush");
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Margin = new Thickness(8, 2, 8, 0);
        stack.Children.Add(status);
        var box = new Border { Width = 150, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 12)), Child = stack };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return box;
    }

    private static Border ActionFace(string glyph, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Ui.Glyph(glyph, 22, "AccentBrush"));
        var t = Ui.Text(label, 16, "FgBrush", bold: true);
        t.Margin = new Thickness(10, 0, 0, 0);
        t.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(t);
        var box = new Border { Width = 220, Height = 64, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 12)), Child = line };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return box;
    }

    // ---------------------------------------------------------------- actions

    private string FilterName => _filter switch
    {
        Filter.Playing => "In a game",
        Filter.Online => "Online",
        Filter.Pinned => "Pinned",
        _ => "Everyone"
    };

    private string AlertsName => Host.Settings.FriendAlerts switch
    {
        "pinned" => "pinned friends",
        "off" => "off",
        _ => "everyone"
    };

    private void CycleFilter()
    {
        _filter = (Filter)(((int)_filter + 1) % 4);
        Build();
    }

    private void CycleAlerts()
    {
        Host.Settings.FriendAlerts = Host.Settings.FriendAlerts switch { "all" => "pinned", "pinned" => "off", _ => "all" };
        Host.Settings.Save();
        Build();
    }

    private void TogglePin(Friend f)
    {
        var pins = Host.Settings.PinnedFriends;
        if (!pins.Remove(f.Id)) pins.Add(f.Id);
        Host.Settings.Save();
        _focusKey = f.Id;
        Build();
    }

    private async Task OpenFriendAsync(Friend friend)
    {
        var options = new List<(string Label, Action Run)>();

        // Join in if they're playing something installed here, else look it up.
        if (Installed(friend.GameAppId) is { } game)
            options.Add(("Play " + game.Name, () => _ = Host.ActivateAsync(game)));
        else if (friend.GameAppId is int storeId)
        {
            var owned = Host.AllGames.FirstOrDefault(g => g.IsSteam && g.AppId == storeId);
            options.Add(owned != null
                ? ("Install " + owned.Name, () => _ = Host.ActivateAsync(owned))
                : ("View " + friend.Game + " in the Steam store", () => ProcessUtil.Open($"steam://store/{storeId}")));
        }

        options.Add(("Send a message", () => ProcessUtil.Open($"steam://friends/message/{friend.Id}")));
        if (friend.InGame)
            options.Add(("Join their game (if they allow it)", () => ProcessUtil.Open($"steam://friends/joingame/{friend.Id}")));
        options.Add((IsPinned(friend) ? "Unpin" : "Pin to the top", () => TogglePin(friend)));
        options.Add(("View Steam profile", () => ProcessUtil.Open($"steam://url/SteamIDPage/{friend.Id}")));

        string status = friend.IsOnline ? friend.Status : friend.LastSeenText;
        int choice = await Host.AskAsync(friend.Name, status, options.Select(o => o.Label).ToArray());
        if (choice >= 0) options[choice].Run();
    }

    private async Task OpenGameAsync(int appId, List<Friend> friends)
    {
        var options = new List<(string Label, Action Run)>();
        var owned = Host.AllGames.FirstOrDefault(g => g.IsSteam && g.AppId == appId);
        if (owned != null) options.Add(((owned.IsInstalled ? "Play " : "Install ") + owned.Name, () => _ = Host.ActivateAsync(owned)));
        else options.Add(("View in the Steam store", () => ProcessUtil.Open($"steam://store/{appId}")));
        foreach (var f in friends) options.Add(("Message " + f.Name, () => ProcessUtil.Open($"steam://friends/message/{f.Id}")));
        int choice = await Host.AskAsync(friends[0].Game ?? "Game",
            string.Join(", ", friends.Select(f => f.Name)) + " are playing.", options.Select(o => o.Label).ToArray());
        if (choice >= 0) options[choice].Run();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y when Keyboard.FocusedElement is Button { Tag: Friend f }:
                TogglePin(f);
                return true;
            case NavAction.X when HasKey && _friends != null:
                CycleFilter();
                return true;
        }
        return false;
    }

    private void AddKey() => _ = SettingsScreen.EditSteamKeyAsync(Host, () =>
    {
        _friends = null;
        _error = null;
        Build();
        _ = LoadAsync();
    });
}
