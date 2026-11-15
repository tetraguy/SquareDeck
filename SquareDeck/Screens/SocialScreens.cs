using System.Windows;
using System.Windows.Controls;
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

/// <summary>Steam friends who are online, and what they are playing.</summary>
public sealed class FriendsScreen : ListScreen
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly Dictionary<string, System.Windows.Media.ImageSource?> _avatars = new();
    private CancellationTokenSource _cts = new();
    private List<Friend>? _friends;
    private string? _error;
    private bool _loading;

    public FriendsScreen() : base("Friends")
    {
        _refresh.Tick += (_, _) => _ = LoadAsync();
    }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
        _refresh.Start();
    }

    public override void OnClosed()
    {
        _refresh.Stop();
        _cts.Cancel();
    }

    public override void OnResumed() => Rebuild(); // a refresh may have landed while a dialog was open

    private bool HasKey => Host.Settings.SteamWebApiKey.Length > 0;

    private async Task LoadAsync()
    {
        if (!HasKey || _loading) return;
        _loading = true;
        if (_friends == null) Rebuild();
        try
        {
            var key = Host.Settings.SteamWebApiKey;
            var ct = _cts.Token;
            (_friends, _error) = await Task.Run(() => SteamFriends.GetAsync(key, ct), ct);
        }
        catch (OperationCanceledException) { return; }
        finally { _loading = false; }
        if (Host.IsTop(this)) Rebuild();
    }

    protected override void Build()
    {
        if (!HasKey)
        {
            AddNote("To see your Steam friends here, add your own Steam Web API key. It's free: sign in at " +
                    "steamcommunity.com/dev/apikey, enter any domain name, and copy the key it shows you.");
            AddRow("Add Steam Web API key", glyph: "", click: AddKey);
            AddRow("Open the key page in a browser", glyph: "",
                click: () => ProcessUtil.Open("https://steamcommunity.com/dev/apikey"));
            return;
        }

        if (_error != null)
        {
            AddNote(_error);
            AddRow("Try again", glyph: "", click: () => _ = LoadAsync());
            AddRow("Change the API key", glyph: "", click: AddKey);
            return;
        }
        if (_friends == null) { AddNote("Loading friends…"); return; }

        var playing = _friends.Where(f => f.InGame).ToList();
        var online = _friends.Where(f => f.IsOnline && !f.InGame).ToList();
        int offline = _friends.Count(f => !f.IsOnline);

        AddHeader($"In a game ({playing.Count})");
        if (playing.Count == 0) AddNote("Nobody is playing right now.");
        foreach (var f in playing) AddFriend(f);

        AddHeader($"Online ({online.Count})");
        foreach (var f in online) AddFriend(f);

        AddNote(offline == 1 ? "1 friend is offline." : offline + " friends are offline.");
        AddRow("Refresh", glyph: "", click: () => _ = LoadAsync());
    }

    private void AddFriend(Friend friend)
    {
        var avatar = new Image();
        if (_avatars.TryGetValue(friend.Id, out var cached)) avatar.Source = cached;
        else if (friend.Avatar.Length > 0) _ = LoadAvatarAsync(friend, avatar);
        AddRow(friend.Name, friend.Status, icon: avatar, click: () => _ = OpenFriendAsync(friend));
    }

    private async Task LoadAvatarAsync(Friend friend, Image view)
    {
        var source = await Task.Run(() => ImageLoader.FromUrlAsync(friend.Avatar, 64));
        _avatars[friend.Id] = source;
        view.Source = source;
    }

    private async Task OpenFriendAsync(Friend friend)
    {
        var options = new List<(string Label, Action Run)>();

        // Offer to join in if they're playing something installed here.
        if (friend.GameAppId is int appId &&
            Host.AllGames.FirstOrDefault(g => g.IsSteam && g.AppId == appId) is { } game)
            options.Add(("Play " + game.Name, () => _ = Host.ActivateAsync(game)));
        else if (friend.GameAppId is int storeId)
            options.Add(("View " + friend.Game + " in the Steam store", () => ProcessUtil.Open($"steam://store/{storeId}")));

        options.Add(("Send a message", () => ProcessUtil.Open($"steam://friends/message/{friend.Id}")));
        options.Add(("View Steam profile", () => ProcessUtil.Open($"steam://url/SteamIDPage/{friend.Id}")));

        int choice = await Host.AskAsync(friend.Name, friend.Status, options.Select(o => o.Label).ToArray());
        if (choice >= 0) options[choice].Run();
    }

    private void AddKey() => _ = SettingsScreen.EditSteamKeyAsync(Host, () =>
    {
        _friends = null;
        _error = null;
        Rebuild();
        _ = LoadAsync();
    });
}
