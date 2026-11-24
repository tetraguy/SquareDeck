using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

public enum HomeView { Games, Favorites, Apps }

public partial class MainWindow : Window
{
    // Must match the panel margin in MainWindow.xaml. Tile widths come from Game.TileWidth.
    private const double RowSideMargin = 86;

    // Positions of the Favorites and Apps buttons in the round-button row.
    // Positions in the row of round buttons (some can be hidden in Settings, so they are looked up).
    private int HomeButton => Array.IndexOf(_buttons, HomeBtn);
    private int FavoritesButton => Array.IndexOf(_buttons, FavBtn);
    private int AppsButton => Array.IndexOf(_buttons, AppsBtn);

    private readonly ObservableCollection<Game> _view = new();
    private readonly List<Game> _steam = new();
    private readonly List<Game> _apps = new();
    private readonly List<Game> _custom = new();
    private readonly List<Game> _external = new();   // Epic, GOG, Xbox, Battle.net, Steam shortcuts
    private readonly Game _addTile = new() { Kind = ItemKind.AddTile, Name = "Add app", OriginalName = "Add app" };
    private readonly Game _moreAppsTile = new() { Kind = ItemKind.MoreAppsTile, Name = "More apps", OriginalName = "More apps" };
    private const int HomeAppCount = 10;
    private readonly Game _libraryTile = new() { Kind = ItemKind.LibraryTile, Name = "Game library", OriginalName = "Game library" };

    /// <summary>How many recent games the home row shows before the "Game library" tile.</summary>
    private const int HomeGameCount = 10;
    private readonly Dictionary<int, DateTime> _stoppedAt = new();
    private readonly HashSet<int> _stoppedByUs = new();     // Steam games we closed; see PollRunning

    private readonly Gamepad _gamepad = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _padTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _heroCts;
    private string? _heroKey;
    private bool _heroPreBlurred; // the artwork showing was blurred when it loaded
    private bool _anyGameWasRunning;
    private int _topIndex = -1;             // >= 0: a top-bar icon has focus (index into TopButtons)

    private readonly List<Screen> _screens = new();
    private Button[] _buttons = Array.Empty<Button>();
    private int _buttonIndex = -1;          // -1: the game row has focus; otherwise index into _buttons
    private HomeView _mode = HomeView.Games;
    private ScrollViewer? _scroll;
    private double _scrollTarget;
    private bool _scrollAnimating;
    private bool _tileWasSelected;
    private bool _pollingRunning;
    private CancellationTokenSource? _loadCts;

    public Settings Settings { get; } = Remember(Services.Settings.Load());

    /// <summary>The running launcher's settings, for code that has no window at hand (another thread).</summary>
    public static Settings? CurrentSettings { get; private set; }

    private static Settings Remember(Settings settings) => CurrentSettings = settings;
    public LibraryStore Library { get; } = LibraryStore.Load();

    /// <summary>Play time measured by the launcher itself.</summary>
    public PlaytimeStore Playtime { get; } = PlaytimeStore.Load();

    private DateTime _lastPlaytimeTick = DateTime.UtcNow;
    private DateTime _lastPlaytimeSave = DateTime.UtcNow;
    private Game? _animatedGame;
    private Game? _continueTile;
    private int _stopReturnIndex;           // round button to go back to from Stop

    /// <summary>Which achievements were already unlocked, and the feed of new ones.</summary>
    public AchievementStore Achievements { get; } = AchievementStore.Load();

    private readonly HashSet<int> _steamWasRunning = new();
    // Steam games started from their own program: Steam may never mark them "running", so their processes are watched.
    private readonly Dictionary<int, DateTime> _directLaunched = new();
    private readonly Queue<(string Heading, string Title, string Detail, string IconUrl, ImageSource? Picture, string Kind, Game? Game)> _toasts = new();
    private bool _toastShowing;

    public MainWindow()
    {
        InitializeComponent();
        // Touch: no press-and-hold circle, flick gestures or tap ripples getting in the way of scrolling and tapping.
        System.Windows.Input.Stylus.SetIsPressAndHoldEnabled(this, false);
        System.Windows.Input.Stylus.SetIsFlicksEnabled(this, false);
        System.Windows.Input.Stylus.SetIsTapFeedbackEnabled(this, false);
        Theme.Apply(Settings);
        UiSound.Alerted += FlashForAlert;
        Hints.Map = Settings.ButtonMap;
        Hints.SetStyle(Settings.GlyphStyle);
        // The keyboard choice from the start (it used to be applied only after a setting changed, so after a restart
        // SquareDeck's own keyboard came back whatever Settings said).
        TouchKeyboard.UseWindows = Settings.TouchKeyboard == "windows";
        Hints.SetLook(Settings.GlyphLook);
        ApplyHintLabels();
        UiSound.Configure(Settings);
        UiSound.SetLaunchChime(Settings.LaunchChime, Settings.LaunchChimeFile);
        RelocateSavedPaths();
        Motion.RegisterTilePulse();
        _gamepad.SummonCombo = Settings.SummonCombo;
        ApplyWallpaper();
        ApplyAvatar();
        InitExtras();
        InitMore();
        InitFullscreenGuard();
        InitPower();
        ApplyUiStyle();
        InitDynamicBackground();
        InitLeanMode();
        InitRest();
        InitLabs();

        GameList.ItemsSource = _view;
        RefreshHomeButtons();
        foreach (var b in ButtonRow.Children.OfType<Button>())
        {
            b.MouseEnter += (_, _) => ButtonLabel.Text = b.ToolTip as string ?? "";
            b.MouseLeave += (_, _) => UpdateButtonHighlight();
            ToolTipService.SetIsEnabled(b, false); // the label under the row replaces tooltips
        }

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _padTimer.Tick += (_, _) => PollGamepad();
        _padTimer.Start();
        Deactivated += (_, _) => _gamepad.Reset();

        _runningTimer.Tick += (_, _) => PollRunning();
        _runningTimer.Start();

        _updateTimer.Tick += (_, _) => RefreshUpdateStates();
        _updateTimer.Start();

        // Wait until the highlight settles before fetching artwork for the background.
        _heroTimer.Tick += (_, _) =>
        {
            _heroTimer.Stop();
            _ = ShowHeroAsync((GameList.SelectedItem as Game)?.Actual);
            _ = ShowAnimatedCoverAsync(GameList.SelectedItem as Game);
            _ = ApplyGameAccentAsync((GameList.SelectedItem as Game)?.Actual);
        };

        _connectionTimer.Tick += (_, _) => UpdateConnections();
        _connectionTimer.Start();
        UpdateConnections();

        Loaded += async (_, _) =>
        {
            _scroll = FindChild<ScrollViewer>(GameList);
            Focus();
            if (Settings.StartupIntro) PlayIntro();
            // The greeting by the profile picture, once the intro has cleared.
            if (Settings.WelcomeMessage)
            {
                var greet = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Settings.StartupIntro ? 2600 : 700) };
                greet.Tick += (_, _) => { greet.Stop(); if (Settings.SetupDone == true) ShowWelcome(); };
                greet.Start();
            }
            // After a silent update: say whether it worked.
            if (SilentUpdate.TakeResult() is { } update)
            {
                if (update.Code is 0 or 3010)
                    ShowToast("Updated", "SquareDeck " + Updater.Current, "The update installed. See what's new in Settings > About.", "", "success");
                else if (update.Code is not (1602 or 1223))
                    ShowToast("Update", "The update didn't install", $"Windows Installer stopped with error {update.Code}. Try again from Settings > About.", "", "warning");
            }
            StartPriceWatch();
            StartStreamPill();
            // Games load while the welcome pages show, so they can say what was found.
            var loading = ReloadAsync();
            if (Settings.SetupDone != true)
            {
                await Task.Delay(Settings.StartupIntro ? 2100 : 200); // let the intro finish first
                await RunSetupAsync();
            }
            await loading;
            if (Settings.AutoUpdateCheck) _ = CheckForUpdatesAsync(manual: false);
        };
        Closed += (_, _) =>
        {
            Screens.PerfOverlay.CloseAll();
            _loadCts?.Cancel();
            OnClosedExtras();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ================================================================ library

    public int GameCount => _steam.Count(g => !IsHidden(g));

    /// <summary>Raised when games, titles, favorites or categories change, so open pages can refresh.</summary>
    public event Action? LibraryChanged;

    /// <summary>Every game in the library: Steam, other stores and custom games, minus the ones removed from the list.</summary>
    public IEnumerable<Game> AllGames =>
        _steam.Concat(_external).Where(g => !IsHidden(g)).Concat(_custom);

    public string? CategoryOf(Game item)
    {
        var c = Library.Find(item.Key)?.Category;
        return string.IsNullOrWhiteSpace(c) ? null : c;
    }

    /// <summary>Categories currently used by at least one game, A to Z.</summary>
    public List<string> Categories => AllGames
        .Select(CategoryOf)
        .Where(c => c != null)
        .Select(c => c!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Refreshes the home row and tells open pages that the library changed.</summary>
    private void NotifyLibraryChanged()
    {
        PruneNotInstalled();
        ApplyView();
        LibraryChanged?.Invoke();
    }

    public int HiddenCount => Library.Items.Values.Count(o => o.Hidden);

    private bool IsHidden(Game g) => Library.Find(g.Key)?.Hidden == true;

    /// <summary>Rescans Steam and reloads covers. Optionally returns before the covers have finished.</summary>
    public async Task ReloadAsync(bool waitForArt = true)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;

        StatusText.Text = "Scanning Steam library…";

        List<Game> found;
        try
        {
            found = await Task.Run(SteamLibrary.GetInstalledGames, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            found = new List<Game>();
            StatusText.Text = "Could not read the Steam library: " + ex.Message;
        }
        if (ct.IsCancellationRequested) return;

        foreach (var g in found)
        {
            var o = Library.Find(g.Key);
            if (!string.IsNullOrWhiteSpace(o?.Title)) g.Name = o.Title;
            g.IsFavorite = o?.Favorite == true;
        }

        // 6.3: Steam games' banners, logos and tall covers now come from Steam itself. Once, the ones downloaded from
        // SteamGridDB before are let go, so Steam's are fetched in their place (pictures you picked stay).
        if (!Settings.SteamArtworkFirst)
        {
            var steamGames = found.Where(g => g.IsSteam).ToList();
            await Task.Run(() => SteamGridDbClient.ForgetDownloadedArt(steamGames));
            Settings.SteamArtworkFirst = true;
            Settings.Save();
        }

        _steam.Clear();
        _steam.AddRange(found);
        SyncApps();
        SyncCustomGames();
        NotifyLibraryChanged();

        var art = LoadArtAsync(found, ct);
        var others = LoadExternalGamesAsync(ct);
        // Once every launcher's games are in: new ones get Genie answers ready in the background.
        _ = others.ContinueWith(_ => Dispatcher.BeginInvoke(() => GeniePrep.NoteLibrary(AllGames)), TaskScheduler.Default);
        if (waitForArt) await Task.WhenAll(art, others);
    }

    /// <summary>Finds games from the other launchers (slower than Steam, so it runs after the row is up).</summary>
    private async Task LoadExternalGamesAsync(CancellationToken ct)
    {
        List<Game> found;
        try
        {
            found = Settings.OtherStores
                ? await Task.Run(() => ExternalStores.ScanAsync(ct, Settings.DisabledStores.ToHashSet()), ct)
                : new List<Game>();
        }
        catch (Exception)
        {
            return;
        }
        if (ct.IsCancellationRequested) return;

        foreach (var g in found)
        {
            var o = Library.Find(g.Key);
            if (!string.IsNullOrWhiteSpace(o?.Title)) g.Name = o.Title;
            g.IsFavorite = o?.Favorite == true;
            if (o?.LastPlayed is DateTime played && played > g.RecentAt) g.RecentAt = played;
        }
        _external.Clear();
        _external.AddRange(found);
        NotifyLibraryChanged();

        // Covers: the user's own pick, else SteamGridDB looked up by the game's title.
        try
        {
            using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
            foreach (var g in found)
            {
                if (ct.IsCancellationRequested) return;
                var custom = CustomCoverPath(g);
                if (SteamGridDbClient.IsIconFile(custom)) { await ShowIconFileAsync(g, custom!); continue; }
                var path = custom
                           ?? await Task.Run(() => client.GetCoverByNameAsync(g.Key, g.OriginalName, ct), ct);
                if (path != null) g.Cover = await Task.Run(() => ImageLoader.FromFile(path, 640), ct);
                else await UseIconAsync(g, client, ct);
            }
        }
        catch (Exception)
        {
            // Offline or a rejected key: tiles keep showing their titles.
        }
    }

    public async Task RedownloadArtAsync()
    {
        _loadCts?.Cancel();
        await Task.Run(SteamGridDbClient.ClearCache);
        await ReloadAsync();
    }

    public void RestoreHidden()
    {
        foreach (var o in Library.Items.Values) o.Hidden = false;
        Library.Save();
        NotifyLibraryChanged();
    }

    /// <summary>Makes the app tiles match the saved list, reusing tiles that already exist.</summary>
    private void SyncApps()
    {
        var existing = _apps.ToDictionary(a => a.LaunchId, StringComparer.OrdinalIgnoreCase);
        _apps.Clear();
        foreach (var entry in Library.Apps)
        {
            if (!existing.TryGetValue(entry.AppId, out var app))
            {
                app = new Game { Kind = ItemKind.App, LaunchId = entry.AppId, OriginalName = entry.Name, Name = entry.Name };
                _ = RefreshCoverAsync(app);
            }
            var o = Library.Find(app.Key);
            app.Name = string.IsNullOrWhiteSpace(o?.Title) ? entry.Name : o.Title;
            app.IsFavorite = o?.Favorite == true;
            _apps.Add(app);
        }
    }

    /// <summary>Makes the custom-game tiles match the saved list, reusing tiles that already exist.</summary>
    private void SyncCustomGames()
    {
        var existing = _custom.ToDictionary(c => c.LaunchId);
        _custom.Clear();
        foreach (var entry in Library.CustomGames)
        {
            if (!existing.TryGetValue(entry.Id, out var game) || game.LaunchPath != entry.Path || (game.LaunchArgs ?? "") != (entry.Arguments ?? ""))
            {
                game = new Game
                {
                    Kind = ItemKind.Custom,
                    LaunchId = entry.Id,
                    LaunchPath = entry.Path,
                    LaunchArgs = string.IsNullOrWhiteSpace(entry.Arguments) ? null : entry.Arguments,
                    InstallDir = Path.GetDirectoryName(entry.Path),
                    OriginalName = entry.Name,
                    Name = entry.Name
                };
                _ = RefreshCoverAsync(game);
            }
            var o = Library.Find(game.Key);
            game.Name = string.IsNullOrWhiteSpace(o?.Title) ? entry.Name : o.Title;
            game.IsFavorite = o?.Favorite == true;
            game.RecentAt = o?.LastPlayed ?? entry.AddedAt;
            if (entry.ReleaseYear is int year) game.ReleaseYear = year;
            _custom.Add(game);
        }
    }

    public void AddCustomGame(string id, string name, string programPath, string? coverPath, string? arguments = null)
    {
        Library.CustomGames.Add(new CustomGame { Id = id, Name = name, Path = programPath, Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments, AddedAt = DateTime.Now });
        if (coverPath != null) Library.For("custom:" + id).CoverPath = coverPath;
        Library.Save();
        SyncCustomGames();
        NotifyLibraryChanged();
    }

    /// <summary>Saves changes to a game added by hand (Edit information) and refreshes its tile.</summary>
    public void UpdateCustomGame(CustomGame entry)
    {
        Library.Save();
        SyncCustomGames();
        NotifyLibraryChanged();
    }

    private void RemoveCustomGame(Game game)
    {
        SetCustomCover(game, null); // also deletes its image file
        Library.CustomGames.RemoveAll(c => c.Id == game.LaunchId);
        Library.Items.Remove(game.Key);
        Library.Save();
        SyncCustomGames();
        NotifyLibraryChanged();
    }

    public bool HasApp(string appId) =>
        Library.Apps.Any(a => string.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase));

    public void AddApp(string name, string appId)
    {
        if (HasApp(appId)) return;
        Library.Apps.Add(new AppEntry { Name = name, AppId = appId });
        Library.Save();
        SyncApps();
        ApplyView();
    }

    public void RemoveApp(string appId)
    {
        Library.Apps.RemoveAll(a => string.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase));
        Library.Items.Remove("app:" + appId);
        Library.Save();
        SyncApps();
        ApplyView();
    }

    /// <summary>Fills the home row for the current view, keeping the same tile selected when possible.</summary>
    private void ApplyView()
    {
        var selectedKey = (GameList.SelectedItem as Game)?.Key;

        IEnumerable<Game> items = _mode switch
        {
            HomeView.Favorites => AllGames.Concat(_apps).Where(g => g.IsFavorite),
            // The app opened last comes first. From ten apps on, the row ends with More apps, which holds Add app.
            HomeView.Apps => _apps.Count >= HomeAppCount
                ? _apps.OrderByDescending(a => a.RecentAt).Take(HomeAppCount).Append(_moreAppsTile)
                : _apps.OrderByDescending(a => a.RecentAt).Append(_addTile),
            // Home: the most recently played / installed games, then the way in to everything else.
            _ => HomeRow()
        };

        _view.Clear();
        foreach (var g in items) _view.Add(g);

        ModeText.Text = _mode switch
        {
            HomeView.Favorites => "Favorites",
            HomeView.Apps => "Apps",
            _ => ""
        };

        if (_view.Count == 0)
        {
            EmptyText.Text = "No favorites yet.\nHighlight a game and press Y to add it.";
            EmptyText.Visibility = Visibility.Visible;
            InfoText.Text = "";
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
            GameList.SelectedItem = _view.FirstOrDefault(g => g.Key == selectedKey) ?? _view[0];
        }
        UpdateButtonHighlight();
        ScrollSelectionIntoView();
        if (Labs.On("dust")) RefreshDust();
    }

    /// <summary>"Continue playing" (if on), the most recent games, then the Game library tile.</summary>
    private IEnumerable<Game> HomeRow()
    {
        var recent = AllGames.OrderByDescending(g => g.RecentAt).ToList();
        var row = new List<Game>();

        // Only for a game that has actually been played or installed at a known time.
        if (Settings.ContinueTile && recent.Count > 0 && recent[0].RecentAt > DateTime.MinValue)
        {
            // The game played (or installed) last, as one big tile with its artwork.
            var target = recent[0];
            if (_continueTile == null || !ReferenceEquals(_continueTile.Target, target))
            {
                _continueTile = new Game { Kind = ItemKind.ContinueTile, Target = target, Cover = target.Cover };
                _ = LoadContinueArtAsync(_continueTile);
            }
            _continueTile.Name = target.Name;
            _continueTile.Cover ??= target.Cover; // made while the cover was put away during a game, or still loading
            _continueTile.IsRunning = target.IsRunning;
            _continueTile.Subtitle = ContinueSubtitle(target);
            FillContinueDetail(_continueTile, target);
            row.Add(_continueTile);
            row.AddRange(recent.Skip(1).Take(HomeGameCount - 1));
        }
        else
        {
            _continueTile = null;
            row.AddRange(recent.Take(HomeGameCount));
        }

        row.Add(_libraryTile);
        return row;
    }

    private string ContinueSubtitle(Game target)
    {
        if (target.IsRunning) return "Running now";
        var parts = new List<string> { "Last played " + Ui.Ago(target.RecentAt) };
        // The last session: how long it lasted.
        var last = Playtime.Sessions.LastOrDefault(x => x.Key == target.Key);
        if (last != null && last.Seconds >= 60) parts.Add(PlaytimeStore.Format(last.Seconds) + " last session");
        double week = Playtime.GamesInWeek(PlaytimeStore.WeekStart(DateTime.Now))
            .FirstOrDefault(g => g.Key == target.Key).Seconds;
        if (week >= 60) parts.Add(PlaytimeStore.Format(week) + " this week");
        return string.Join("  \u00B7  ", parts);
    }

    /// <summary>
    /// The Continue tile's second line and bar: play time in total, achievements (with a bar), where you are in it
    /// (the status you gave it), your rating, and the start of your newest note for it.
    /// </summary>
    private void FillContinueDetail(Game tile, Game target)
    {
        var parts = new List<string>();
        double total = Playtime.TotalSeconds(target.Key);
        if (total >= 60) parts.Add(PlaytimeStore.Format(total) + " played");
        double progress = -1;
        if (target.IsSteam && Achievements.ProgressOf(target.AppId) is { Total: > 0 } a)
        {
            parts.Add($"{a.Unlocked} of {a.Total} achievements");
            progress = (double)a.Unlocked / a.Total;
        }
        var o = Library.Find(target.Key);
        if (o?.Status is { Length: > 0 } status) parts.Add(char.ToUpper(status[0]) + status[1..]);
        if (o is { Rating: > 0 }) parts.Add(new string('\u2605', Math.Clamp(o.Rating, 1, 5)));
        if (o?.Notes is { Length: > 0 } notes)
        {
            string line = notes.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.EndsWith(":")) ?? "";
            if (line.Length > 0) parts.Add("\u201C" + (line.Length > 48 ? line[..48] + "\u2026" : line) + "\u201D");
        }
        tile.ContinueDetail = string.Join("  \u00B7  ", parts);
        tile.ContinueProgress = progress;
    }

    /// <summary>The wide tile looks best with wide artwork; until it arrives it shows the square cover.</summary>
    private async Task LoadContinueArtAsync(Game tile)
    {
        var target = tile.Target!;
        try
        {
            var art = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
                var path = await client.GetHeroAsync(target, CancellationToken.None);
                return path == null ? null : ImageLoader.FromFile(path, 1280);
            });
            if (art != null) tile.Cover = art;
            else if (tile.Cover == null) tile.Cover = target.Cover;
        }
        catch (Exception)
        {
            tile.Cover ??= target.Cover;
        }
        // The logo goes where the name was.
        try
        {
            var logo = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
                var path = await client.GetLogoAsync(target, CancellationToken.None);
                return path == null ? null : ImageLoader.FromFile(path, 720);
            });
            if (logo != null) tile.ContinueLogo = logo;
        }
        catch (Exception) { }
    }

    private void SetMode(HomeView mode)
    {
        _mode = mode;
        _scrollTarget = 0;
        GameList.SelectedItem = null;
        ApplyView();
    }

    // ================================================================ cover art

    private async Task LoadArtAsync(List<Game> games, CancellationToken ct)
    {
        using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
        using var gate = new SemaphoreSlim(4);
        int done = 0;
        string? error = null;

        async Task LoadOne(Game game)
        {
            try
            {
                await gate.WaitAsync(ct);
            }
            catch (OperationCanceledException) { return; }

            try
            {
                var custom = CustomCoverPath(game);
                bool customIcon = SteamGridDbClient.IsIconFile(custom);
                if (customIcon) custom = null; // shown as an icon below, once the rest has loaded
                if (error != null)
                {
                    // e.g. bad API key: don't hammer the server, but still show what we have on disk.
                    var local = custom ?? SteamGridDbClient.FindCachedCover(game.AppId);
                    if (local != null) game.Cover = await Task.Run(() => ImageLoader.FromFile(local, 640), ct);
                    return;
                }

                var (info, image) = await Task.Run(async () =>
                {
                    var r = await client.GetAsync(game.AppId, ct);
                    var path = custom ?? r.CoverPath;
                    var img = path == null ? null : ImageLoader.FromFile(path, 640);
                    if (img == null && path != null && custom == null)
                    {
                        // Truncated download: drop it so it is fetched again next time.
                        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    return (r.Info, img);
                }, ct);

                if (ct.IsCancellationRequested) return;
                if (customIcon) await ShowIconFileAsync(game, CustomCoverPath(game)!);
                else if (image != null) game.Cover = image;
                else await UseIconAsync(game, client, ct); // no square cover exists: show the game's icon
                if (info?.ReleaseYear != null) game.ReleaseYear = info.ReleaseYear;
                if (ReferenceEquals(GameList.SelectedItem, game)) InfoText.Text = game.InfoLine;
            }
            catch (OperationCanceledException) { }
            catch (UnauthorizedAccessException)
            {
                error = "SteamGridDB rejected the API key. Check it in Settings.";
            }
            catch (HttpRequestException)
            {
                error ??= "Couldn't reach SteamGridDB. Showing saved covers only.";
            }
            catch (Exception)
            {
                // One bad title should not stop the rest.
            }
            finally
            {
                gate.Release();
                done++;
                if (!ct.IsCancellationRequested && error == null)
                    StatusText.Text = $"Loading covers {done}/{games.Count}";
            }
        }

        await Task.WhenAll(games.Select(LoadOne));
        if (ct.IsCancellationRequested) return;

        // Nothing to say when all went well.
        StatusText.Text = error ?? "";
    }

    /// <summary>
    /// When a game has no square cover, shows its icon instead, centered on a gradient of the icon's
    /// colors: SteamGridDB's icon, else Steam's own cached one, else the program's icon.
    /// </summary>
    private async Task UseIconAsync(Game game, SteamGridDbClient? client, CancellationToken ct)
    {
        try
        {
            bool fromGrid = false;
            var (icon, gradient) = await Task.Run(async () =>
            {
                System.Windows.Media.Imaging.BitmapSource? source = null;

                if (client != null)
                {
                    try
                    {
                        var path = await client.GetIconAsync(game, ct);
                        if (path != null) source = ImageLoader.FromFile(path, 512) as System.Windows.Media.Imaging.BitmapSource;
                        fromGrid = source != null;
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { }
                }

                if (source == null && game.IsSteam && SteamLibrary.FindSteamPath() is string steam)
                {
                    var cached = Path.Combine(steam, "appcache", "librarycache", game.AppId + "_icon.jpg");
                    if (File.Exists(cached)) source = ImageLoader.FromFile(cached, 256) as System.Windows.Media.Imaging.BitmapSource;
                }

                if (source == null && game.LaunchPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(game.LaunchPath))
                    source = ShellThumb.Get(game.LaunchPath, 256);
                if (source == null && game.LaunchPath.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
                    source = ShellThumb.Get(game.LaunchPath, 256);

                return (source, source == null ? null : ImageLoader.GradientFrom(source));
            }, ct);

            if (icon == null || ct.IsCancellationRequested) return;
            game.TileBackground = gradient;
            game.Cover = icon;
            game.IconFills = fromGrid; // SteamGridDB artwork fills the tile; program icons stay centered
            game.UsesIcon = true;
        }
        catch (Exception)
        {
            // No icon either: the tile keeps showing the title.
        }
    }

    /// <summary>An icon chosen from SteamGridDB: centered on a wash of its own colors, like the automatic icon tiles.</summary>
    private async Task ShowIconFileAsync(Game item, string path)
    {
        var (icon, gradient) = await Task.Run(() =>
        {
            var source = ImageLoader.FromFile(path, 512) as System.Windows.Media.Imaging.BitmapSource;
            return (source, source == null ? null : ImageLoader.GradientFrom(source));
        });
        if (icon == null) return;
        item.TileBackground = gradient;
        item.Cover = icon;
        item.IconFills = true; // chosen from SteamGridDB: as big as the tile, like a cover
        item.IconIsSmall = false;
        item.UsesIcon = true;
    }

    private string? CustomCoverPath(Game item)
    {
        // After restoring a backup made elsewhere the saved path may not exist, but the file does in our folder.
        return Backup.Relocate(Library.Find(item.Key)?.CoverPath, SteamGridDbClient.CustomDir);
    }

    /// <summary>Reloads one tile's image: the user's own choice, else the default cover or app icon.</summary>
    private async Task RefreshCoverAsync(Game item)
    {
        var path = CustomCoverPath(item)
                   ?? (item.IsSteam ? SteamGridDbClient.FindCachedCover(item.AppId)
                       : item.IsExternal ? SteamGridDbClient.FindCachedCoverByName(item.Key)
                       : null);
        if (SteamGridDbClient.IsIconFile(path))
        {
            await ShowIconFileAsync(item, path!);
            return;
        }
        if (path != null)
        {
            item.Cover = await Task.Run(() => ImageLoader.FromFile(path, 640));
            item.UsesIcon = false;
            return;
        }

        if (!item.IsApp)
        {
            item.Cover = null;
            item.UsesIcon = false;
            await UseIconAsync(item, null, CancellationToken.None); // custom and other-store games: the program's icon
            return;
        }

        // Apps show their own icon, centered on a gradient made from the icon's colors.
        var (icon, gradient) = await Task.Run(() =>
        {
            var source = ShellThumb.BestForApp(item.LaunchId);
            return (source, source == null ? null : ImageLoader.GradientFrom(source));
        });
        bool small = icon != null && await Task.Run(() => ShellThumb.Detail(icon)) <= 64;
        item.TileBackground = gradient;
        item.IconIsSmall = small;
        item.IconFills = false;
        item.Cover = icon;
        item.UsesIcon = icon != null;
    }

    /// <summary>Sets (or with null, clears) the user's own image for a tile.</summary>
    public void SetCustomCover(Game item, string? path)
    {
        var o = Library.For(item.Key);
        var old = o.CoverPath;
        o.CoverPath = path;
        Library.Save();

        if (!string.IsNullOrEmpty(old) && old != path &&
            old.StartsWith(SteamGridDbClient.CustomDir, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        _ = RefreshCoverAsync(item);
    }

    // ================================================================ launching and stopping

    /// <summary>What "A" does on a tile: start the game or app (or open the add-app page).</summary>
    public async Task ActivateAsync(Game item)
    {
        // The game or app punches in as it opens (Settings > Home screen > Animations).
        if (item.Actual is { } real && (real.IsGame || real.IsApp) && Motion.PunchIn && !Theme.ReduceMotion)
        {
            // The home row's tile, or (on another page: library, apps, a game's page) the button that was pressed.
            UIElement? target = _screens.Count == 0 ? GameList.ItemContainerGenerator.ContainerFromItem(item) as UIElement : null;
            if (target is not { IsVisible: true } && Keyboard.FocusedElement is UIElement focused && (focused is ButtonBase || focused is ListBoxItem))
                target = focused;
            if (target is { IsVisible: true }) await Motion.PunchAsync(target);
        }
        item = item.Actual; // "Continue playing" stands in for a real game
        if (item.IsAddTile)
        {
            Push(new AddAppScreen());
            return;
        }

        if (item.IsLibraryTile)
        {
            Push(new LibraryScreen());
            return;
        }

        if (item.IsMoreAppsTile)
        {
            Push(new AllAppsScreen());
            return;
        }

        if (item.IsApp)
        {
            bool appStarted = InstalledApps.Launch(item.LaunchId);
            StatusText.Text = appStarted ? "" : "Couldn't start " + item.Name + ".";
            if (appStarted)
            {
                // Remembered, so the app opened last moves to the front of the Apps row.
                item.RecentAt = DateTime.Now;
                Library.For(item.Key).LastPlayed = item.RecentAt;
                Library.Save();
                if (_mode == HomeView.Apps) ApplyView();
            }
            return;
        }

        if (item.OnWishlist)
        {
            await OfferWishlistAsync(item);
            return;
        }

        if (!item.IsInstalled)
        {
            await OfferInstallAsync(item);
            return;
        }

        // Already running: go back to it rather than starting it again.
        if (item.IsRunning)
        {
            ReturnToGame(item);
            return;
        }

        // Only one game at a time: the other one has to be stopped first.
        var running = AllGames.FirstOrDefault(g => g.IsRunning && g != item);
        if (running != null)
        {
            UiSound.Error();
            var ask = new GameConfirmScreen(running, running.Name + " is still running",
                $"It has to be stopped before {item.Name} can start. Any unsaved progress in {running.Name} will be lost.",
                "Stop and start", "Still running");
            Push(ask);
            if (!await ask.Result) return;
            // The "Closing software" pop-up stays up a moment longer so Steam notices the game has gone.
            if (!await StopGameAsync(running, confirm: false, settleMs: 1500)) return;
        }

        // An update waiting in Steam: update first, or play the version that's here?
        if (Settings.AskAboutUpdates && item.IsSteam && item.HasUpdate)
        {
            var update = new GameUpdateScreen(item);
            Push(update);
            if (!await update.Result) return;
        }

        if (Settings.LaunchCountdown && !item.IsRunning)
        {
            var countdown = new CountdownScreen(item, Settings.LaunchCountdownSeconds);
            Push(countdown);
            if (!await countdown.Result) return;
        }

        // Performance Boost: free memory first, with its animation. With the launch splash on, the splash fades in
        // over the end of the boost (a cross-fade) instead of the boost closing to the home screen first.
        LaunchSplashScreen? splash = null;
        if (Settings.PerformanceBoost && !item.IsRunning)
        {
            var boost = new BoostScreen(item, Settings.BoostAnimation, Settings.BoostRumble) { KeepOpenOnFinish = Settings.LaunchSplash };
            Push(boost);
            await boost.Result;
            if (boost.KeepOpenOnFinish && IsTop(boost))
            {
                splash = new LaunchSplashScreen(item);
                Push(splash);
                boost.Visibility = Visibility.Visible; // stays under the splash while it fades in
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
                fade.Completed += (_, _) => RemoveBelow(boost);
                splash.BeginAnimation(OpacityProperty, fade);
            }
            else if (boost.KeepOpenOnFinish) RemoveBelow(boost);
        }

        // Switch to the game's own display / sound / power settings, and quieten Windows, before it starts.
        if (!item.IsRunning) await PrepareLaunchAsync(item);

        bool started;
        if (item.IsCustom || item.IsExternal)
        {
            started =
                item.LaunchPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                    ? ProcessUtil.Open("explorer.exe", item.LaunchPath)       // Xbox / Store game
                : item.LaunchPath.Contains("://")
                    ? ProcessUtil.Open(item.LaunchPath)                       // another launcher's link
                    : ProcessUtil.Start(item.LaunchPath, item.LaunchArgs);    // a program
            StatusText.Text = started ? "" : "Couldn't start " + item.Name + ". Has it been moved or uninstalled?";
            if (started)
            {
                Library.For(item.Key).LastPlayed = DateTime.Now; // Steam tracks this itself; other games need us to
                Library.Save();
            }
        }
        else
        {
            // Straight from the game's own program when that's switched on (no Steam "Launching…" pop-up over the
            // splash). Steam must already be running; otherwise, or with no program found, the usual Steam link.
            if (Settings.SteamDirectLaunch && await Task.Run(() => SteamDirect.SteamIsRunning() ? SteamDirect.FindExe(item, Library.For(item.Key)) : null) is string exe
                && SteamDirect.Start(item, exe))
            {
                started = true;
                _directLaunched[item.AppId] = DateTime.UtcNow;
            }
            else started = ProcessUtil.Open($"steam://rungameid/{item.AppId}");
            StatusText.Text = started ? "" : "Couldn't start the game. Is Steam installed?";
        }

        if (!started)
        {
            await RestoreLaunchAsync();
            if (splash != null) Pop(splash);
        }

        if (started)
        {
            UiSound.Launch();
            CompanionReact("bye");
            // A splash screen with the cover until the game is running.
            if (Settings.LaunchSplash && !item.IsRunning && splash == null) Push(new LaunchSplashScreen(item));
            _ = CheckWindowsUpdateAsync(atLaunch: true); // a pending restart could cut the session short
            // Move it to the front of the home row straight away.
            item.RecentAt = DateTime.Now;
            NotifyLibraryChanged();
        }
    }

    /// <summary>Ends a running game's processes. Returns false if cancelled or nothing could be stopped.</summary>
    public async Task<bool> StopGameAsync(Game game, bool confirm, int settleMs = 0)
    {
        if (confirm)
        {
            var ask = new GameConfirmScreen(game, "Stop " + game.Name + "?",
                "The game will be closed immediately. Any unsaved progress will be lost.", "Stop game");
            Push(ask);
            if (!await ask.Result) return false;
        }

        // Like a console: a "Closing software" pop-up, with the game's picture, that stays until the game has really
        // gone. Shown in front of the game, even when it's closed from the in-game menu.
        var busy = new GameBusyScreen(game, "Closing software");
        HoldVisible(true);
        Push(busy);
        int killed = 0;
        try
        {
            var shownAt = DateTime.UtcNow;
            // The closing animation plays for three seconds before the game is told to stop, so it's always seen
            // (some games close so fast it used to flash by).
            await Task.Delay(3000);
            string? exe = ExactProgram(game);
            string? dir = exe == null && !string.IsNullOrEmpty(game.InstallDir) && Directory.Exists(game.InstallDir) ? game.InstallDir : null;

            if (exe != null) killed = await Task.Run(() => ProcessUtil.KillByPath(exe));
            else if (dir != null) killed = await Task.Run(() => ProcessUtil.KillProcessesUnder(dir));

            if (killed > 0)
            {
                // Some games take a few seconds to let go (saving, anti-cheat, their own launcher).
                for (int i = 0; i < 40 && await Task.Run(() => ProcessUtil.IsRunning(exe, dir)); i++)
                {
                    await Task.Delay(250);
                    if (i == 12) // still there after three seconds: ask again, in case something restarted it
                    {
                        if (exe != null) await Task.Run(() => ProcessUtil.KillByPath(exe));
                        else if (dir != null) await Task.Run(() => ProcessUtil.KillProcessesUnder(dir));
                    }
                }
                if (settleMs > 0) await Task.Delay(settleMs);
                // Long enough to read, even when the game closes instantly.
                var remaining = TimeSpan.FromMilliseconds(900) - (DateTime.UtcNow - shownAt);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            }
        }
        finally
        {
            Pop(busy);
            HoldVisible(false);
        }

        if (killed == 0)
        {
            await AskAsync("Couldn't stop " + game.Name,
                "No running program was found in the game's folder. Close it from the game itself or from Steam.", "OK");
            return false;
        }

        if (game.IsSteam) { _stoppedAt[game.AppId] = DateTime.UtcNow; _stoppedByUs.Add(game.AppId); }
        game.IsRunning = false;
        if (_continueTile?.Target == game) _continueTile.IsRunning = false;
        UpdateRunningBanner();
        return true;
    }

    /// <summary>
    /// For games identified by one program rather than a folder (custom games and Steam shortcuts,
    /// whose folder may be shared with other things): that program's path. Null otherwise.
    /// </summary>
    /// <summary>
    /// Notes which program is the game (the one in its folder that shows the game's window), so "Start Steam games
    /// directly" can start that same program next time instead of guessing.
    /// </summary>
    private async Task RememberSteamProgramAsync(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallDir)) return;
        try
        {
            for (int i = 0; i < 12 && game.IsRunning; i++)
            {
                await Task.Delay(5000);
                var program = await Task.Run(() => ProcessUtil.FindGameWindowProgram(null, game.InstallDir));
                if (program == null) continue;
                var entry = Library.For(game.Key);
                if (!string.Equals(entry.SteamExeSeen, program, StringComparison.OrdinalIgnoreCase))
                {
                    entry.SteamExeSeen = program;
                    Library.Save();
                }
                return;
            }
        }
        catch (Exception) { }
    }

    /// <summary>Game options > "Program to start directly": pick the game's .exe, or go back to automatic.</summary>
    private async Task ChooseSteamProgramAsync(Game game)
    {
        var entry = Library.For(game.Key);
        int choice = await AskAsync("Program to start directly",
            "Used when \"Start Steam games directly\" is on in Settings.\n\nNow: " + SteamDirect.Describe(game, entry) +
            (string.IsNullOrEmpty(game.InstallDir) ? "" : "\nGame folder: " + game.InstallDir),
            "Choose a program…", "Automatic", "Cancel");
        if (choice == 0)
        {
            var picked = await PickFileAsync("Choose the game's program", new[] { ".exe" });
            if (picked == null) return;
            entry.SteamExe = picked;
        }
        else if (choice == 1) { entry.SteamExe = null; entry.SteamExeSeen = null; }
        else return;
        Library.Save();
    }

    internal static string? ExactProgram(Game game)
    {
        bool byProgram = game.IsCustom || (game.IsExternal && string.IsNullOrEmpty(game.InstallDir));
        return byProgram && game.LaunchPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? game.LaunchPath
            : null;
    }

    private async void PollRunning()
    {
        if (_pollingRunning) return;
        _pollingRunning = true;
        try
        {
            // Steam says which of its games are running; everything else is matched against running programs.
            var ids = _steam.Select(g => g.AppId).ToList();
            var others = _external.Concat(_custom).ToList();
            var (steamRunning, programs) = await Task.Run(() => (
                SteamLibrary.GetRunningAppIds(ids),
                others.Count > 0 || _stoppedByUs.Count > 0 || _directLaunched.Count > 0 ? ProcessUtil.GetRunningImagePaths() : new List<string>()));

            var now = DateTime.UtcNow;
            foreach (var g in _steam)
            {
                // Steam takes a few seconds to clear its flag after we end a game.
                bool justStopped = _stoppedAt.TryGetValue(g.AppId, out var at) && now - at < TimeSpan.FromSeconds(8);
                bool flagged = steamRunning.Contains(g.AppId);
                if (!flagged) _stoppedByUs.Remove(g.AppId); // Steam has caught up
                else if (_stoppedByUs.Contains(g.AppId) && !justStopped)
                {
                    // We closed it, but Steam still says "running" (it can hold that for a long time while it
                    // syncs saves). Believe the processes instead, so the "is running" label doesn't linger.
                    string? prefix = string.IsNullOrEmpty(g.InstallDir) ? null : g.InstallDir.TrimEnd('\\') + "\\";
                    flagged = prefix != null && programs.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                }
                if (_directLaunched.TryGetValue(g.AppId, out var launchedAt))
                {
                    string? prefix = string.IsNullOrEmpty(g.InstallDir) ? null : g.InstallDir.TrimEnd('\\') + "\\";
                    bool alive = prefix != null && programs.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    flagged = alive; // its processes say when it's running, whatever Steam's flag says
                    if (!alive && now - launchedAt > TimeSpan.FromSeconds(20)) _directLaunched.Remove(g.AppId);
                }
                g.IsRunning = flagged && !justStopped;
            }
            foreach (var g in others)
            {
                if (ExactProgram(g) is string exe)
                    g.IsRunning = programs.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
                else if (!string.IsNullOrEmpty(g.InstallDir))
                {
                    var prefix = g.InstallDir.TrimEnd('\\') + "\\";
                    g.IsRunning = programs.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (_continueTile?.Target is { } continuing && _continueTile.IsRunning != continuing.IsRunning)
            {
                _continueTile.IsRunning = continuing.IsRunning;
                _continueTile.Subtitle = ContinueSubtitle(continuing);
            }

            // Steam games starting and stopping: note what was unlocked before, look for new ones after.
            foreach (var g in _steam)
            {
                bool was = _steamWasRunning.Contains(g.AppId);
                if (g.IsRunning && !was)
                {
                    _steamWasRunning.Add(g.AppId);
                    _ = SnapshotAchievementsAsync(g);
                    _ = RememberSteamProgramAsync(g);
                }
                else if (!g.IsRunning && was)
                {
                    _steamWasRunning.Remove(g.AppId);
                    _ = CheckAchievementsAsync(g);
                }
            }

            UpdateRunningBanner();
            TrackPlaytime();
            TrackSessions();

            // A game just closed: come back to the front, with the focus, so the controller works straight away.
            bool anyRunning = AllGames.Any(g => g.IsRunning);
            if (_anyGameWasRunning && !anyRunning && Settings.ReturnToLauncher)
                ComeBackAfterGame();
            _anyGameWasRunning = anyRunning;
        }
        catch (Exception)
        {
            // Never let a failed check stop the timer.
        }
        finally
        {
            _pollingRunning = false;
        }
    }

    // ---------------------------------------------------------------- achievements and pop-ups

    private async Task SnapshotAchievementsAsync(Game game)
    {
        if (!Settings.AchievementToasts) return;
        try
        {
            var all = await Task.Run(() => SteamStoreClient.GetAchievementsAsync(game.AppId, CancellationToken.None));
            if (all != null) Achievements.SetBaseline(game.AppId, all);
        }
        catch (Exception) { }
    }

    private async Task CheckAchievementsAsync(Game game)
    {
        if (!Settings.AchievementToasts) return; // (the session summary is then shown straight away instead)
        try
        {
            // Steam's public page can lag behind the game by a little, so look twice.
            foreach (var wait in new[] { 8, 60 })
            {
                await Task.Delay(TimeSpan.FromSeconds(wait));
                var all = await Task.Run(() => SteamStoreClient.GetAchievementsAsync(game.AppId, CancellationToken.None));
                if (all == null)
                {
                    FinishPendingSummary(game, null); // profile not public: report the session without a count
                    return;
                }
                Achievements.NoteProgress(game.AppId, all);
                var fresh = Achievements.TakeNew(game.AppId, game.Name, all);
                FinishPendingSummary(game, fresh.Count);
                foreach (var a in fresh)
                    ShowToast("ACHIEVEMENT UNLOCKED", a.Name, game.Name, a.Icon, "achievement", game: game);
            }
        }
        catch (Exception) { }
    }

    /// <summary>Queues a small pop-up in the bottom-right corner. They show one at a time.</summary>
    public void ShowToast(string heading, string title, string detail, string iconUrl, string kind = "info", ImageSource? picture = null, Game? game = null, bool remember = true)
    {
        if (kind == "achievement") CompanionReact("jump");
        // Everything that pops up is also kept in the notification center (with its game, for the icon there),
        // except quick confirmations of something you just did.
        if (remember)
        {
            Notifications.Add(kind, title, detail, iconUrl, game?.Key ?? "");
            UpdateBell();
        }
        _toasts.Enqueue((heading, title, detail, iconUrl, picture, kind, game));
        if (!_toastShowing) _ = RunToastsAsync();
    }

    private async Task RunToastsAsync()
    {
        _toastShowing = true;
        try
        {
            while (_toasts.Count > 0)
            {
                var (heading, title, detail, iconUrl, picture, kind, game) = _toasts.Dequeue();
                if (Settings.RumbleOnNotifications) Rumble(110); // feel it as it pops up

                var icon = new Image { Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0), Stretch = Stretch.UniformToFill };
                if (picture != null) icon.Source = picture;
                else if (iconUrl.Length > 0) _ = LoadToastIconAsync(icon, iconUrl);
                else icon.Visibility = Visibility.Collapsed;

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360 };
                var top = Ui.Text(heading, 12, "AccentBrush", bold: true);
                text.Children.Add(top);
                text.Children.Add(Ui.Text(title, 20, "FgBrush", bold: true));
                text.Children.Add(Ui.Text(detail, 15, "SubtleBrush"));

                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(icon);
                content.Children.Add(text);

                var slide = new TranslateTransform(0, 30);
                var toast = new Border
                {
                    Child = content,
                    Padding = new Thickness(14),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 40, 96),
                    RenderTransform = slide,
                    Opacity = 0
                };
                toast.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
                toast.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                // Tap (or click) it to open what it's about; it goes away straight after.
                var tapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                toast.Cursor = System.Windows.Input.Cursors.Hand;
                toast.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (!tapped.TrySetResult()) return;
                    OpenNotification(kind, title, detail, game);
                };
                ToastLayer.Children.Add(toast);

                var quick = TimeSpan.FromMilliseconds(300);
                toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, quick));
                slide.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(30, 0, quick));
                UiSound.Toast();

                // Accessibility can keep them up longer, to read them.
                await Task.WhenAny(Task.Delay(Settings.ToastSeconds > 0 ? Settings.ToastSeconds * 1000 : 4500), tapped.Task);
                toast.IsHitTestVisible = false;
                toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, quick));
                await Task.Delay(340);
                ToastLayer.Children.Remove(toast);
            }
        }
        finally
        {
            _toastShowing = false;
        }
    }

    /// <summary>
    /// Opens what a notification is about: the game's page (achievements, downloads, play sessions), the Friends page,
    /// quick settings for the battery, the notification center for Windows notifications, or else the full text.
    /// </summary>
    public void OpenNotification(string kind, string title, string detail, Game? game)
    {
        UiSound.Select();
        // A session summary opens Your play time.
        if (kind == "session") { Push(new PlaytimeScreen()); return; }
        if (game != null && game.IsGame) { Push(new DetailsScreen(game)); return; }
        switch (kind)
        {
            case "friend": Push(new FriendsScreen()); return;
            case "battery": Push(new QuickSettingsScreen()); return;
            case "windows": Push(new NotificationsScreen()); return;
            case "download" when _installing != null: Push(new DetailsScreen(_installing)); return;
        }
        _ = AskAsync(title, detail.Length > 0 ? detail : null, "OK");
    }

    private static async Task LoadToastIconAsync(Image view, string url) =>
        view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 112));

    /// <summary>Adds the time since the last check to every game that is running.</summary>
    private void TrackPlaytime()
    {
        var now = DateTime.UtcNow;
        // Capped so a sleep / resume gap is never counted as play time.
        double elapsed = Math.Min((now - _lastPlaytimeTick).TotalSeconds, 10);
        _lastPlaytimeTick = now;

        bool any = false;
        foreach (var g in AllGames.Where(g => g.IsRunning))
        {
            Playtime.Add(g.Key, g.Name, elapsed);
            any = true;
        }
        if (any && now - _lastPlaytimeSave > TimeSpan.FromSeconds(60))
        {
            _lastPlaytimeSave = now;
            Playtime.Save();
        }
    }

    /// <summary>Re-reads Steam's update state so "Update queued" / "Updating 40%" stay current.</summary>
    private async void RefreshUpdateStates()
    {
        ProcessInstallQueue();
        if (_steam.Count == 0) return;
        try
        {
            var states = await Task.Run(SteamLibrary.GetUpdateStates);
            foreach (var g in _steam)
            {
                if (!states.TryGetValue(g.AppId, out var text)) continue;
                if (g.UpdateText.StartsWith("Updating") && text.Length == 0)
                    ShowToast("DOWNLOAD", g.Name, "has finished updating", "", "download", game: g);
                g.UpdateText = text;
            }
            if (GameList.SelectedItem is Game selected) InfoText.Text = selected.InfoLine;
        }
        catch (Exception) { }
    }

    private void UpdateRunningBanner()
    {
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        RunningPanel.Visibility = running == null ? Visibility.Collapsed : Visibility.Visible;
        CompanionGameState(running != null);
        // Checked every second while a game runs, so SquareDeck is back quickly once it closes.
        var interval = TimeSpan.FromSeconds(running == null ? 2 : 1);
        if (_runningTimer.Interval != interval) _runningTimer.Interval = interval;
        UpdateGpuSaver(); // hand the graphics memory to the game while it runs
        UpdateHomeHints(); // "Back to game" only shows while one is running
        // The Ally's lights while playing: off, or the game's own effect (game profile, its cover's colors by default).
        AllyControl.SetRunning(running, running == null ? null : Library.Find(running.Key)?.Profile);
        UpdateAmdMetrics(running);
        if (running != null)
        {
            EnsureRunningMarquee();
            string label = running.Name + " is running";
            if (RunningText.Text != label) RunningText.Text = label;
            ShowRunningIcon(running);
        }
        else
        {
            if (_stopFocused) SetStopFocus(false); // the game closed while Stop was highlighted
            // Back to the everyday limit once the game has closed.
            if (TdpControl.GameProfile != null || TdpControl.SessionWatts != null) TdpControl.SetGame(null);
        }
    }

    private MarqueeText? _runningMarquee;
    private Game? _runningIconFor;
    private ImageSource? _runningIconCover;

    /// <summary>The game's name scrolls when it's longer than the space for it.</summary>
    private void EnsureRunningMarquee()
    {
        if (_runningMarquee != null || RunningText.Parent is not Panel panel) return;
        int index = panel.Children.IndexOf(RunningText);
        panel.Children.Remove(RunningText);
        _runningMarquee = new MarqueeText(RunningText) { MaxWidth = 190, VerticalAlignment = VerticalAlignment.Center, Margin = RunningText.Margin };
        RunningText.Margin = new Thickness(0);
        panel.Children.Insert(index, _runningMarquee);
    }

    /// <summary>The running game's own icon (or cover) instead of a green dot; the dot stays if it has no picture.</summary>
    private void ShowRunningIcon(Game game)
    {
        // Called every second while a game runs: the picture is only made again when the game (or its art) changed.
        if (ReferenceEquals(_runningIconFor, game) && ReferenceEquals(_runningIconCover, game.Cover)) return;
        _runningIconFor = game;
        _runningIconCover = game.Cover;
        if (game.Cover == null)
        {
            RunningIcon.Visibility = Visibility.Collapsed;
            RunningDot.Visibility = Visibility.Visible;
            return;
        }
        if (game.UsesIcon)
        {
            RunningIcon.Background = game.TileBackground ?? (Brush?)TryFindResource("CircleBrush");
            RunningIcon.Child = new Image { Source = game.Cover, Margin = new Thickness(game.IconFills ? 0 : 4), Stretch = game.IconFills ? Stretch.UniformToFill : Stretch.Uniform };
        }
        else
        {
            RunningIcon.Background = new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill };
            RunningIcon.Child = null;
        }
        RunningIcon.Visibility = Visibility.Visible;
        RunningDot.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Brings the running game back in front of the launcher. It never starts the game: if the game
    /// turns out to have closed, the launcher just says so.
    /// </summary>
    private void ReturnToGame(Game game)
    {
        string? exe = ExactProgram(game);
        string? dir = exe == null && !string.IsNullOrEmpty(game.InstallDir) ? game.InstallDir : null;
        if (ProcessUtil.FocusWindow(exe, dir)) return;

        if (!ProcessUtil.IsRunning(exe, dir))
        {
            // Our "running" mark was out of date (the game closed a moment ago, or Steam hadn't caught up).
            game.IsRunning = false;
            if (game.IsSteam) { _stoppedAt[game.AppId] = DateTime.UtcNow; _stoppedByUs.Add(game.AppId); }
            UpdateRunningBanner();
            StatusText.Text = game.Name + " is no longer running.";
            return;
        }
        StatusText.Text = "Couldn't switch to " + game.Name + ". Use Alt+Tab.";
    }

    private async void StopRunning_Click(object sender, RoutedEventArgs e)
    {
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        if (running != null) await StopGameAsync(running, confirm: true);
    }

    // ================================================================ per-tile options (Y)

    public void ToggleFavorite(Game item, FrameworkElement? from = null)
    {
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite) { ShowHeartBurst(item, from); CompanionReact("hearts"); } // a heart pops up once, instead of a badge that stays
        Library.For(item.Key).Favorite = item.IsFavorite;
        Library.Save();
        NotifyLibraryChanged();
    }

    /// <summary>Every added app, for the All apps page.</summary>
    public IReadOnlyList<Game> Apps => _apps;

    /// <summary>The Y menu for a tile.</summary>
    public async Task OpenOptionsAsync(Game item)
    {
        item = item.Actual;
        if (item.IsActionTile) return;
        if (!item.IsInstalled)
        {
            await OfferInstallAsync(item);
            return;
        }

        // Grouped, each with an icon, under the game's logo.
        var options = new List<(GameOptionsScreen.Option Option, Func<Task> Run)>();
        void Add(string group, string label, string glyph, Action run, bool danger = false) =>
            options.Add((new GameOptionsScreen.Option(group, label, glyph, danger), () => { run(); return Task.CompletedTask; }));
        void AddAsync(string group, string label, string glyph, Func<Task> run, bool danger = false) =>
            options.Add((new GameOptionsScreen.Option(group, label, glyph, danger), run));
        bool hasFolder = !string.IsNullOrEmpty(item.InstallDir) && Directory.Exists(item.InstallDir);
        void Hide() { Library.For(item.Key).Hidden = true; Library.Save(); NotifyLibraryChanged(); }

        const string Play = "Play", Mine = "Your library", Look = "Customize", Steam = "Steam", Manage = "Manage";
        if (item.IsRunning)
        {
            Add(Play, "Return to game", "\uE768", () => ReturnToGame(item));
            AddAsync(Play, "Stop game", "\uE71A", () => StopGameAsync(item, confirm: true), danger: true);
        }
        // Not when the menu was opened from the game's details page itself.
        if (item.IsGame && _screens.LastOrDefault() is not DetailsScreen) Add(Play, "Game details", "\uE946", () => Push(new DetailsScreen(item)));
        if (item.IsGame && GameCaptures.For(item, 1).Count > 0)
            Add(Play, "Screenshots", "\uE722", () =>
            {
                var shots = GameCaptures.For(item, 200);
                if (shots.Count > 0) Push(new ViewerScreen(shots, 0));
            });

        Add(Mine, item.IsFavorite ? "Remove from favorites" : "Add to favorites", item.IsFavorite ? "\uE735" : "\uE734", () => ToggleFavorite(item));
        if (item.IsGame)
        {
            AddAsync(Mine, "Add to a collection", "\uE8F1", () => ChooseCollectionsAsync(item));
            AddAsync(Mine, "Set category", "\uE8EC", () => SetCategoryAsync(item));
            Add(Mine, "Notebook, rating and status", "\uE70B", () => Push(new GameNotesScreen(item)));
            if (Labs.On("diary")) Add(Mine, "Game diary", "\uE787", () => Push(new GameDiaryScreen(item)));
            if (Labs.On("stickers") && item.IsSteam) Add(Mine, "Sticker book", "\uE8EC", () => Push(new StickerBookScreen(item)));
        }

        Add(Look, item.IsApp ? "Change icon" : "Change artwork", "\uE8B9", () => Push(new CoverPickerScreen(item)));
        // Games added by hand can change everything about them; the rest just their title.
        if (item.IsCustom) Add(Look, "Edit information", "\uE70F", () => Push(new EditCustomGameScreen(item)));
        else AddAsync(Look, "Edit title", "\uE70F", () => EditTitleAsync(item));
        if (item.IsGame) Add(Look, "Game profile", "\uE713", () => Push(new GameProfileScreen(item)));

        if (item.IsSteam)
        {
            Add(Steam, "Achievements", "\uE7C1", () => Push(new GameAchievementsScreen(item)));
            Add(Steam, "Store page", "\uE719", () => ProcessUtil.Open($"steam://store/{item.AppId}"));
            if (Settings.SteamDirectLaunch && item.IsInstalled) AddAsync(Steam, "Program to start directly", "\uE756", () => ChooseSteamProgramAsync(item));
        }

        if (item.IsCustom)
        {
            if (hasFolder) Add(Manage, "Show in folder", "\uE838", () => ProcessUtil.Open("explorer.exe", "/select,\"" + item.LaunchPath + "\""));
            if (hasFolder) Add(Manage, "Fix this game", "\uE90F", () => Push(new FixGameScreen(item)));
            Add(Manage, "Remove from library", "\uE711", () => RemoveCustomGame(item), danger: true);
        }
        else if (item.IsExternal)
        {
            if (hasFolder) Add(Manage, "Show in folder", "\uE838", () => ProcessUtil.Open("explorer.exe", "\"" + item.InstallDir + "\""));
            if (hasFolder) Add(Manage, "Fix this game", "\uE90F", () => Push(new FixGameScreen(item)));
            Add(Manage, "Remove from list", "\uE711", Hide, danger: true);
        }
        else if (item.IsSteam)
        {
            if (hasFolder) Add(Manage, "Show in folder", "\uE838", () => ProcessUtil.Open("explorer.exe", "\"" + item.InstallDir + "\""));
            Add(Manage, "Fix this game", "\uE90F", () => Push(new FixGameScreen(item)));
            Add(Manage, "Move to another drive", "\uE8DE", () => Push(new MoveGameScreen(item)));
            AddAsync(Manage, "Uninstall", "\uE74D", () => UninstallAsync(item), danger: true);
            Add(Manage, "Remove from list", "\uE711", Hide, danger: true);
        }
        else
        {
            Add(Manage, "Uninstall (Windows settings)", "\uE74D", () => ProcessUtil.Open("ms-settings:appsfeatures"), danger: true);
            Add(Manage, "Remove from launcher", "\uE711", () => RemoveApp(item.LaunchId), danger: true);
        }

        // A line about the game under its logo.
        string source = item.IsSteam ? "Steam" : item.IsApp ? "App" : item.IsRecomp ? "Recomp Zone" : item.IsRetro ? "Retro Lounge" : item.Source.Length > 0 ? item.Source : "Added by you";
        string subtitle = item.IsRunning ? "Running now  ·  " + source : source;
        var sheet = new GameOptionsScreen(item, options.Select(o => o.Option).ToList(), subtitle);
        Push(sheet);
        int choice = await sheet.Result;
        if (choice >= 0) await options[choice].Run();
    }

    private async Task EditTitleAsync(Game item)
    {
        var title = await PromptAsync("Edit title", "Leave it empty to go back to the original name.", item.Name);
        if (title == null) return;
        title = title.Trim();
        Library.For(item.Key).Title = title.Length == 0 ? null : title;
        Library.Save();
        item.Name = title.Length == 0 ? item.OriginalName : title;
        LibraryChanged?.Invoke();
    }

    private async Task SetCategoryAsync(Game item)
    {
        var existing = Categories;
        var labels = existing.Concat(new[] { "New category\u2026", "No category" }).ToArray();
        var current = CategoryOf(item);
        int choice = await AskAsync("Category for " + item.Name,
            current == null ? "Not in a category yet." : "Currently in: " + current, labels);
        if (choice < 0) return;

        string? category;
        if (choice < existing.Count) category = existing[choice];
        else if (choice == existing.Count)
        {
            category = (await PromptAsync("New category", "For example: Co-op, Backlog, Racing.", "", "Create"))?.Trim();
            if (string.IsNullOrEmpty(category)) return;
        }
        else category = null;

        Library.For(item.Key).Category = category;
        Library.Save();
        NotifyLibraryChanged();
    }

    private async Task UninstallAsync(Game game)
    {
        int choice = await AskAsync("Uninstall " + game.Name + "?",
            "Steam will open and ask you to confirm. The game's files will be removed from this PC.",
            "Continue in Steam", "Cancel");
        if (choice == 0) ProcessUtil.Open($"steam://uninstall/{game.AppId}");
    }

    // ================================================================ screens

    /// <summary>The page at the bottom of the stack (the one opened from the home screen), if any.</summary>
    public Screen? RootScreen => _screens.Count > 0 ? _screens[0] : null;

    /// <summary>Puts the keyboard / controller focus on an element once the page has finished laying out.</summary>
    public void FocusSoon(UIElement? element)
    {
        if (element == null) return;
        Dispatcher.BeginInvoke(new Action(() => element.Focus()), System.Windows.Threading.DispatcherPriority.Background);
    }

    public bool IsTop(Screen screen) => _screens.Count > 0 && ReferenceEquals(_screens[^1], screen);

    public void Push(Screen screen)
    {
        // A page that is already open (the browser kept running in the background): go back to it instead.
        int open = _screens.IndexOf(screen);
        if (open >= 0)
        {
            if (open < _screens.Count - 1) Pop(_screens[open + 1]);
            return;
        }
        screen.Visibility = Visibility.Visible; // a page reused from earlier may still be hidden

        if (_screens.Count > 0)
        {
            var below = _screens[^1];
            below.LastFocus = Keyboard.FocusedElement;
            below.SavedScroll = ScrollersOf(below).Select(v => v.VerticalOffset).ToList();
            below.OnCovered();
            if (!screen.IsDialog) below.Visibility = Visibility.Collapsed;
        }

        screen.Host = this;
        // Accessibility: larger text and controls on every page and pop-up.
        if (Theme.TextScale != 1) screen.LayoutTransform = new ScaleTransform(Theme.TextScale, Theme.TextScale);
        _screens.Add(screen);
        ScreenLayer.Children.Add(screen);
        Motion.OpenPage(screen, screen.IsDialog);
        screen.OnShown();
        screen.FocusDefaultLater();
    }

    /// <summary>Takes a screen out from under the top one without disturbing what's on top.</summary>
    public void RemoveBelow(Screen screen)
    {
        if (IsTop(screen)) { Pop(screen); return; }
        if (!_screens.Remove(screen)) return;
        ScreenLayer.Children.Remove(screen);
        screen.OnClosed();
    }

    /// <summary>Closes a screen (and anything opened on top of it).</summary>
    public void Pop(Screen screen)
    {
        int index = _screens.IndexOf(screen);
        if (index < 0) return;

        bool closedConnectionPage = false;
        for (int i = _screens.Count - 1; i >= index; i--)
        {
            var closing = _screens[i];
            closedConnectionPage |= closing is WifiScreen or BluetoothScreen or ConnectionBladeScreen;
            _screens.RemoveAt(i);
            ScreenLayer.Children.Remove(closing);
            closing.OnClosed();
        }

        if (closedConnectionPage) UpdateConnections();

        if (_screens.Count == 0)
        {
            Keyboard.ClearFocus();
            Focus();
            return;
        }

        var top = _screens[^1];
        top.Visibility = Visibility.Visible;
        top.OnResumed();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsTop(top)) return;
            if (Keyboard.FocusedElement is Visual current && top.IsAncestorOf(current)) return;
            if (top.LastFocus is UIElement last && top.IsAncestorOf(last) && last.Focus()) return;
            top.FocusDefault();
        }));
        // Back where you were: the page scrolled as it was when you left it, with the highlight on something in view
        // (a page that rebuilt itself on return would otherwise start again from the top).
        if (top.SavedScroll is { Count: > 0 } saved)
        {
            top.SavedScroll = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RestoreScroll(top, saved)));
        }
    }

    /// <summary>The vertically scrolling areas inside a page, in order.</summary>
    private static List<ScrollViewer> ScrollersOf(DependencyObject root)
    {
        var list = new List<ScrollViewer>();
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is ScrollViewer { VerticalScrollBarVisibility: not ScrollBarVisibility.Disabled } v) list.Add(v);
                Walk(child);
            }
        }
        Walk(root);
        return list;
    }

    private void RestoreScroll(Screen top, List<double> saved)
    {
        if (!IsTop(top)) return;
        var scrollers = ScrollersOf(top);
        ScrollViewer? main = null;
        for (int i = 0; i < scrollers.Count && i < saved.Count; i++)
        {
            if (saved[i] <= 1 || Math.Abs(scrollers[i].VerticalOffset - saved[i]) <= 1) continue;
            scrollers[i].ScrollToVerticalOffset(saved[i]);
            main ??= scrollers[i];
        }
        if (main == null) return;
        top.UpdateLayout();
        // If the highlight is off screen now, move it to the first button in view instead of scrolling back to it.
        bool Visible(FrameworkElement e)
        {
            try
            {
                if (!e.IsVisible || !main.IsAncestorOf(e)) return false;
                var r = e.TransformToAncestor(main).TransformBounds(new Rect(e.RenderSize));
                return r.Bottom > 0 && r.Top < main.ViewportHeight;
            }
            catch (InvalidOperationException) { return false; }
        }
        if (Keyboard.FocusedElement is FrameworkElement f && (!main.IsAncestorOf(f) || Visible(f))) return;
        var first = FindVisualChildren<Button>(main).FirstOrDefault(b => b.Focusable && b.IsEnabled && Visible(b));
        first?.Focus();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject d) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindVisualChildren<T>(child)) yield return deeper;
        }
    }

    /// <summary>Shows a dialog with choices. Returns the chosen index, or -1 if dismissed.</summary>
    public async Task<int> AskAsync(string title, string? message, params string[] options)
    {
        var dialog = new MenuScreen(title, message, options);
        Push(dialog);
        return await dialog.Result;
    }

    /// <summary>A dialog that B can't close: one of the options has to be chosen.</summary>
    public async Task<int> AskMandatoryAsync(string title, string? message, params string[] options)
    {
        var dialog = new MenuScreen(title, message, options) { Mandatory = true };
        Push(dialog);
        return await dialog.Result;
    }

    /// <summary>A dialog of choices with an icon beside each. Returns the chosen index, or -1 if dismissed.</summary>
    public async Task<int> AskWithIconsAsync(string title, string? message, params (string Label, string Glyph)[] options)
    {
        var dialog = new MenuScreen(title, message, options.Select(o => o.Label).ToArray(), null, options.Select(o => o.Glyph).ToArray());
        Push(dialog);
        return await dialog.Result;
    }

    /// <summary>Shows a "working" pop-up that can't be dismissed while <paramref name="work"/> runs.</summary>
    public async Task RunBusyAsync(string message, Func<Task> work, ImageSource? picture = null)
    {
        var busy = new BusyScreen(message, picture);
        Push(busy);
        try { await work(); }
        finally { Pop(busy); }
    }

    /// <summary>
    /// Like RunBusyAsync, for a wait that depends on something outside SquareDeck (finishing a sign-in in the browser):
    /// B or Cancel stops it, so coming back without finishing never leaves the screen stuck. Returns false if cancelled.
    /// </summary>
    public async Task<bool> RunCancellableBusyAsync(string message, Func<CancellationToken, Task> work, CancellationToken outer = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var busy = new BusyScreen(message, cancel: () => { try { cts.Cancel(); } catch (ObjectDisposedException) { } });
        Push(busy);
        try
        {
            await work(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return false; }
        finally { Pop(busy); }
    }

    /// <summary>Asks for a password: what's typed shows as dots. Returns null if cancelled.</summary>
    public async Task<string?> PromptSecretAsync(string title, string? message, string okLabel = "OK")
    {
        var dialog = new MenuScreen(title, message, new[] { okLabel, "Cancel" }, "", secret: true);
        Push(dialog);
        return await dialog.Result == 0 ? dialog.InputText : null;
    }

    /// <summary>Asks for a line of text. Returns null if cancelled.</summary>
    public async Task<string?> PromptAsync(string title, string? message, string initial, string okLabel = "Save")
    {
        var dialog = new MenuScreen(title, message, new[] { okLabel, "Cancel" }, initial);
        Push(dialog);
        return await dialog.Result == 0 ? dialog.InputText : null;
    }

    // ================================================================ input

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        bool home = _screens.Count == 0;
        if (home && e.Key == Key.F5)
        {
            _ = ReloadAsync();
            e.Handled = true;
            return;
        }

        // A rich-text page (notes) keeps every key except Escape (leave) and Page Up / Down.
        if (Keyboard.FocusedElement is RichTextBox && e.Key is not (Key.Escape or Key.PageUp or Key.PageDown)) return;
        // While typing, letters and the left/right arrows belong to the text box.
        bool typing = Keyboard.FocusedElement is TextBox;
        NavAction? action = e.Key switch
        {
            Key.Up => NavAction.Up,
            Key.Down => NavAction.Down,
            Key.Enter => NavAction.Accept,
            Key.Escape => NavAction.Back,
            Key.Left when !typing => NavAction.Left,
            Key.Right when !typing => NavAction.Right,
            Key.Space when !typing => NavAction.Accept,
            Key.Back when !typing => NavAction.Back,
            Key.X when !typing => NavAction.X,
            Key.Y when !typing => NavAction.Y,
            Key.Q when !typing => NavAction.View,
            Key.F3 => NavAction.Start,
            Key.OemQuestion when !typing => NavAction.Start, // "/" to search, like many apps
            Key.PageUp => NavAction.LB,
            Key.PageDown => NavAction.RB,
            Key.OemOpenBrackets when !typing => NavAction.LT,
            Key.OemCloseBrackets when !typing => NavAction.RT,
            _ => null
        };
        if (action == null) return;
        e.Handled = true;
        Hints.SetKeyboard(true); // hints show keys while the keyboard is in use
        Handle(action.Value);
    }

    private void PollGamepad()
    {
        // A cloud game is in front: the controller belongs to the page, except the hold-to-exit combo.
        // A web page (streaming, browser, cloud) is its own window inside ours: while it has the keyboard focus WPF can
        // report the launcher as not active even though it's in front, so the foreground window decides too.
        bool active = IsActive || WindowActivator.IsInFront(this);
        if (CloudGameScreen.Active is { } cloud && active)
        {
            cloud.PollController(_gamepad);
            _gamepad.Reset();
            return;
        }
        // A page that reads the controller itself (a streaming service, VacuumTube): SquareDeck keeps out of its way (the page
        // says it's alive every second; if it stops, SquareDeck's own handling below takes over again).
        if (active && _screens.Count > 0 && _screens[^1] is IPagePad { PadInPage: true })
        {
            _gamepad.Poll(); // keeps the button state, so nothing fires twice when SquareDeck takes over
            return;
        }
        // Ignore the controller while a game (or anything else) is in front of us.
        if (!active)
        {
            // ...except for the one shortcut that brings the launcher back (or, over a game, opens the Game Bar widget).
            if ((Settings.GuideSummon || Settings.InGameQuickMenu) && _gamepad.PollSummon())
            {
                if (WidgetWanted()) OpenWidget();
                else if (Settings.GuideSummon) WindowActivator.BringToFront(this);
            }
            _gamepad.Reset();
            return;
        }
        // SquareDeck in front while a game it started is still running (switched back with Alt+Tab, say): the summon
        // buttons open the Game Bar widget here too.
        if (Settings.InGameQuickMenu && AllGames.Any(g => g.IsRunning) && _gamepad.PollSummon())
        {
            OpenWidget();
            _gamepad.Reset();
            return;
        }
        foreach (var action in _gamepad.Poll())
        {
            Hints.SetKeyboard(false); // back to controller buttons in the hints
            Handle(action);
        }
        ScrollWithRightStick();
    }

    private void Handle(NavAction action)
    {
        if (_bootVideo != null)
        {
            EndBootVideo(); // any button skips the boot video
            return;
        }
        if (WakeFromScreensaver()) return; // the press that wakes the screen does nothing else

        switch (action)
        {
            case NavAction.Accept: UiSound.Select(); break;
            case NavAction.Back: UiSound.Back(); break;
            case NavAction.Left or NavAction.Right or NavAction.Up or NavAction.Down: UiSound.Move(); break;
        }

        // LB / RB flip through the main pages from wherever that makes sense.
        if (action is NavAction.LB or NavAction.RB)
        {
            // A page may use the shoulder buttons itself (the screenshot viewer does).
            if (_screens.Count > 0 && _screens[^1].OnAction(action)) return;
            int page = CurrentPage();
            if (page >= 0)
            {
                UiSound.Page();
                StepPage(page, action == NavAction.RB ? 1 : -1);
            }
            return;
        }

        if (action == NavAction.View)
        {
            // A full-screen web page (DeckTube, a streaming service): View + Menu together goes back to SquareDeck, so
            // View alone waits a moment before quick settings, in case Menu follows.
            if (_screens.Count > 0 && _screens[^1] is IPagePad and Screen pagePad)
            {
                if (pagePad.OnAction(action)) return;
                var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                wait.Tick += (_, _) =>
                {
                    wait.Stop();
                    if (_screens.Count > 0 && ReferenceEquals(_screens[^1], pagePad)) Push(new QuickSettingsScreen());
                };
                wait.Start();
                return;
            }
            if (_screens.Count > 0 && _screens[^1] is QuickSettingsScreen quick) Pop(quick);
            else if (_screens.Count == 0 || !_screens[^1].IsDialog) Push(new QuickSettingsScreen());
            return;
        }

        if (_screens.Count > 0) HandleScreen(_screens[^1], action);
        else HandleHome(action);
    }

    /// <summary>Every page LB / RB can flip through. The index is the page number used below.</summary>
    public static readonly (string Id, string Name, string Glyph)[] CyclePages =
    {
        ("games", "Home", "\uE80F"), ("favorites", "Favorites", "\uEB51"), ("apps", "Apps", "\uE71D"),
        ("library", "Game library", "\uE8F1"), ("captures", "Captures", "\uE722"), ("music", "Multimedia", "\uE8D6"),
        ("friends", "Friends", "\uE716"), ("settings", "Settings", "\uE713")
    };

    /// <summary>The pages in the order chosen in Settings (any page missing from the saved order goes at the end).</summary>
    public static List<int> PageOrderOf(Settings s)
    {
        var order = new List<int>();
        foreach (var id in s.PageOrder)
        {
            int i = Array.FindIndex(CyclePages, p => p.Id == id);
            if (i >= 0 && !order.Contains(i)) order.Add(i);
        }
        for (int i = 0; i < CyclePages.Length; i++) if (!order.Contains(i)) order.Add(i);
        return order;
    }

    /// <summary>Moves one page along the chosen order, skipping pages that are switched off.</summary>
    private void StepPage(int current, int step)
    {
        var order = PageOrderOf(Settings);
        int at = order.IndexOf(current);
        for (int n = 1; n <= order.Count; n++)
        {
            int page = order[(((at + step * n) % order.Count) + order.Count) % order.Count];
            if (page == current) return; // nothing else is switched on
            if (PageEnabled(page)) { GoToPage(page, step); return; }
        }
    }

    /// <summary>Index of the page being shown, or -1 when a sub-page or dialog is open.</summary>
    private int CurrentPage()
    {
        if (_screens.Count == 0) return (int)_mode;       // 0 Games, 1 Favorites, 2 Apps
        if (_screens.Count > 1) return -1;
        return _screens[0] switch
        {
            LibraryScreen => 3,
            CapturesScreen => 4,
            MediaScreen => 5,
            FriendsScreen => 6,
            SettingsScreen => 7,
            _ => -1
        };
    }

    /// <param name="direction">+1 or -1 to slide the new page in from that side; 0 for no animation.</param>
    /// <summary>False for a page whose home-screen button has been switched off in Settings.</summary>
    private bool PageEnabled(int page) => !Settings.PagesOff.Contains(CyclePages[page].Id) && page switch
    {
        5 => Settings.ShowMusicButton,
        6 => Settings.ShowFriendsButton,
        _ => true
    };

    private void GoToPage(int page, int direction = 0)
    {
        while (_screens.Count > 0) Pop(_screens[0]);
        SetButtonIndex(-1);
        SetTopIndex(-1);
        if (_stopFocused) SetStopFocus(false);

        switch (page)
        {
            case 3: Push(new LibraryScreen()); break;
            case 4: Push(new CapturesScreen()); break;
            case 5: Push(new MediaScreen()); break;
            case 6: Push(new FriendsScreen()); break;
            case 7: Push(new SettingsScreen()); break;
            default:
                if (_mode != (HomeView)page) SetMode((HomeView)page);
                break;
        }

        // Slide the new page (or the home row, for the home views) in from the side we moved toward.
        SlideIn(_screens.Count > 0 ? _screens[^1] : GameList, direction);
    }

    private void HandleScreen(Screen top, NavAction action)
    {
        if (top.OnAction(action)) return;

        var focused = Keyboard.FocusedElement as UIElement;
        bool inside = focused != null && top.IsAncestorOf(focused);

        // Left / right on a highlighted slider changes its value instead of moving the highlight.
        if (inside && focused is Slider slider && action is (NavAction.Left or NavAction.Right))
        {
            double step = action == NavAction.Right ? slider.LargeChange : -slider.LargeChange;
            slider.Value = Math.Clamp(slider.Value + step, slider.Minimum, slider.Maximum);
            return;
        }

        switch (action)
        {
            case NavAction.Left: MoveFocus(FocusNavigationDirection.Left); break;
            case NavAction.Right: MoveFocus(FocusNavigationDirection.Right); break;
            case NavAction.Up: MoveFocus(FocusNavigationDirection.Up); break;
            case NavAction.Down: MoveFocus(FocusNavigationDirection.Down); break;
            case NavAction.Accept:
                if (inside && focused is Button button) button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                else if (!inside) top.FocusDefault();
                break;
            case NavAction.Back:
                Pop(top);
                break;
        }

        void MoveFocus(FocusNavigationDirection direction)
        {
            if (inside) focused!.MoveFocus(new TraversalRequest(direction));
            else top.FocusDefault();
            // Nothing further to highlight (a page of text): scroll instead, so long pages can be read to the end.
            if (inside && ReferenceEquals(Keyboard.FocusedElement, focused) &&
                direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down &&
                FindScrollable(top) is { } scroller)
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset + (direction == FocusNavigationDirection.Down ? 120 : -120));
            }
            // Pages made of tiles (library, captures, pickers, Explorer, Multimedia) buzz like the home row does.
            if (top is GridScreen or LibraryScreen or ExplorerScreen or MediaScreen && !ReferenceEquals(Keyboard.FocusedElement, focused)) Rumble();
        }
    }

    private void HandleHome(NavAction action)
    {
        var selected = GameList.SelectedItem as Game;

        if (action == NavAction.Start)
        {
            Push(new SearchScreen()); // search everything: games, apps, settings, files, store pages
            return;
        }

        // R2 / L2: next / previous song while something is playing (R1 / L1 still flip the pages).
        if (action is NavAction.RT or NavAction.LT)
        {
            if (MusicButton.Visibility == Visibility.Visible) _ = SkipSongAsync(action == NavAction.RT);
            return;
        }

        if (_footerIndex >= 0)
        {
            HandleFooter(action);
            return;
        }

        int viewButtonIndex = Math.Max(0, _mode == HomeView.Apps ? AppsButton : _mode == HomeView.Favorites ? FavoritesButton : HomeButton);
        if (_actionIndex >= 0)
        {
            // Below the action buttons: the round buttons in layouts that have them, else the footer.
            HandleActionRow(action, () =>
            {
                if (!TabsOnTop && !SidebarNav) SetButtonIndex(viewButtonIndex);
                else if (FooterButtons().Count > 0) { _stopReturnIndex = -1; SetStopFocus(true); }
            });
            return;
        }

        if (_topIndex >= 0)
        {
            var top = TopButtons();
            if (top.Count == 0) { SetTopIndex(-1); return; }
            int index = Math.Min(_topIndex, top.Count - 1);
            switch (action)
            {
                case NavAction.Left: SetTopIndex(Math.Max(0, index - 1)); break;
                case NavAction.Right: SetTopIndex(Math.Min(top.Count - 1, index + 1)); break;
                case NavAction.Down:
                case NavAction.Back: SetTopIndex(-1); break;
                case NavAction.Accept:
                    var target = top[index];
                    Motion.PressThen(target, () =>
                    {
                        SetTopIndex(-1);
                        target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    });
                    break;
            }
            return;
        }

        if (_buttonIndex >= 0)
        {
            // Sidebar (Desktop): the list goes up and down; Right goes back to the games.
            if (SidebarNav)
            {
                switch (action)
                {
                    case NavAction.Up: SetButtonIndex(Math.Max(0, _buttonIndex - 1)); return;
                    case NavAction.Down: SetButtonIndex(Math.Min(_buttons.Length - 1, _buttonIndex + 1)); return;
                    case NavAction.Right or NavAction.Back: SetButtonIndex(-1); return;
                    case NavAction.Left: return;
                }
            }
            // Tabs in the top bar: Down goes back to the games, Up to the status icons.
            else if (TabsOnTop)
            {
                switch (action)
                {
                    case NavAction.Down or NavAction.Back: SetButtonIndex(-1); return;
                    case NavAction.Up:
                        SetButtonIndex(-1);
                        var status = TopButtons();
                        if (status.Count > 0) SetTopIndex(status.Count - 1);
                        return;
                }
            }
            switch (action)
            {
                case NavAction.Left: SetButtonIndex(Math.Max(0, _buttonIndex - 1)); break;
                case NavAction.Right: SetButtonIndex(Math.Min(_buttons.Length - 1, _buttonIndex + 1)); break;
                case NavAction.Up:
                case NavAction.Back: SetButtonIndex(-1); break;
                case NavAction.Down:
                    // Below the round buttons is the footer: the now-playing button and, while a game runs, Stop.
                    if (FooterButtons().Count > 0)
                    {
                        _stopReturnIndex = _buttonIndex;
                        SetButtonIndex(-1);
                        SetStopFocus(true); // Stop if a game is running, otherwise the now-playing button
                    }
                    break;
                case NavAction.Accept:
                    var pressed = _buttons[_buttonIndex];
                    Motion.PressThen(pressed, () => pressed.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
                    break;
            }
            return;
        }

        int viewButton = Math.Max(0, _mode == HomeView.Apps ? AppsButton : _mode == HomeView.Favorites ? FavoritesButton : HomeButton);
        // Tabs on top: Up reaches them. Sidebar: Left from the first game reaches it.
        if (TabsOnTop && action == NavAction.Up) { SetButtonIndex(viewButton); return; }
        if (SidebarNav && action == NavAction.Left && GameList.SelectedIndex <= 0) { SetButtonIndex(viewButton); return; }
        // Down from the games: first the Play / favorite / More buttons under the game, in styles that show them.
        if (action == NavAction.Down && ActionsReachable) { SetActionIndex(0); return; }
        if ((TabsOnTop || SidebarNav) && action == NavAction.Down)
        {
            // Nothing below the games but the footer (Stop / now playing).
            if (FooterButtons().Count > 0) { _stopReturnIndex = -1; SetStopFocus(true); }
            return;
        }
        switch (action)
        {
            case NavAction.Left: MoveSelection(-1); break;
            case NavAction.Right: MoveSelection(+1); break;
            case NavAction.Down:
                SetButtonIndex(viewButton);
                break;
            case NavAction.Up:
                // Up from the games goes to the status icons: Wi-Fi first if it is showing.
                var icons = TopButtons();
                int wifi = icons.IndexOf(WifiButton);
                SetTopIndex(wifi >= 0 ? wifi : icons.Count - 1);
                break;
            case NavAction.Accept:
                if (selected != null) _ = ActivateAsync(selected);
                break;
            case NavAction.X:
                if (selected?.Actual is { IsGame: true } game) Push(new DetailsScreen(game));
                break;
            case NavAction.Y:
                if (selected != null) _ = OpenOptionsAsync(selected);
                break;
            case NavAction.Back:
                // With a game running, B goes back to it; otherwise it leaves Favorites / Apps.
                // (ReturnToGame only ever switches to it; it never starts a game that has closed.)
                if (AllGames.FirstOrDefault(g => g.IsRunning) is { } runningGame) ReturnToGame(runningGame);
                else if (_mode != HomeView.Games) SetMode(HomeView.Games);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_view.Count == 0) return;
        GameList.SelectedIndex = Math.Clamp(GameList.SelectedIndex + delta, 0, _view.Count - 1);
    }

    /// <summary>Top-bar buttons the thumbstick can reach, left to right (hidden ones are skipped).</summary>
    private Button[] AllTopButtons => new[] { AvatarButton, BellButton, QuickButton, WifiButton, BluetoothButton, BatteryButton };

    private List<Button> TopButtons() =>
        new[] { AvatarButton, BellButton, QuickButton, WifiButton, BluetoothButton, BatteryButton }
            .Where(b => b.Visibility == Visibility.Visible).ToList();

    private void SetTopIndex(int index)
    {
        if (index >= 0 && index != _topIndex) Rumble();
        _topIndex = index;
        var visible = TopButtons();
        // Every top-bar button, hidden ones included, so no highlight is ever left behind (the battery's used to stick).
        foreach (var b in AllTopButtons)
        {
            b.Tag = null;
            Motion.Highlight(b, false);
        }
        if (index >= 0 && index < visible.Count)
        {
            visible[index].Tag = "selected";
            Motion.Highlight(visible[index], true, 1.18);
            ButtonLabel.Text = visible[index] == AvatarButton ? "Your profile"
                : visible[index] == BellButton ? "Notifications"
                : visible[index] == QuickButton ? "Quick settings"
                : visible[index] == WifiButton ? "Wi-Fi"
                : visible[index] == BluetoothButton ? "Bluetooth" : "Battery";
        }
        else if (_buttonIndex < 0) ButtonLabel.Text = "";
        UpdateHomeRing();
    }

    private void SetButtonIndex(int index)
    {
        if (index >= 0 && index != _buttonIndex) Rumble();
        _buttonIndex = index;
        UpdateButtonHighlight();
    }

    private void UpdateButtonHighlight()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            bool viewIsOn = (i == HomeButton && _mode == HomeView.Games) ||
                            (i == FavoritesButton && _mode == HomeView.Favorites) ||
                            (i == AppsButton && _mode == HomeView.Apps);
            bool focused = i == _buttonIndex;
            _buttons[i].Tag = focused && viewIsOn ? "selected-active" : focused ? "selected" : viewIsOn ? "active" : null;
            // Tabs and sidebar items barely grow (their ring or fill shows the focus).
            Motion.Highlight(_buttons[i], focused, TabsOnTop || SidebarNav ? 1.04 : 1.12);
        }
        UpdateHomeRing();
        ButtonLabel.Text = _buttonIndex >= 0 && _buttonIndex < _buttons.Length ? _buttons[_buttonIndex].ToolTip as string ?? "" : "";
    }

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string before = InfoText.Text;
        InfoText.Text = (GameList.SelectedItem as Game)?.InfoLine ?? "";
        // The big title follows the info line; apps all share the same one, so it wouldn't change: update it here.
        if (InfoText.Text == before) UpdateStyleTitle();
        ScrollSelectionIntoView();
        OnHomeSelectionForLabs();
        // The highlight ring breathes on the selected tile only.
        foreach (var old in e.RemovedItems)
        {
            Motion.StopPulse(RingOf(old));
            Motion.GrowTile(GameList.ItemContainerGenerator.ContainerFromItem(old) as UIElement, false);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            Motion.StartPulse(RingOf(GameList.SelectedItem));
            if (GameList.SelectedItem != null)
                Motion.GrowTile(GameList.ItemContainerGenerator.ContainerFromItem(GameList.SelectedItem) as UIElement, true);
        }));
        if (GameList.SelectedItem != null && e.RemovedItems.Count > 0) Rumble();
        if (Game.Portrait || Game.Wide) StartPortraits(); // poster art for the games coming into view
        // The animated cover belongs to the highlighted tile only.
        if (_animatedGame != null && !ReferenceEquals(_animatedGame, GameList.SelectedItem))
        {
            _animatedGame.AnimatedCover = null;
            _animatedGame = null;
        }
        _heroTimer.Stop();
        _heroTimer.Start();
    }

    private UIElement? RingOf(object? item) =>
        item != null && GameList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container
            ? container.Template?.FindName("Ring", container) as UIElement
            : null;

    /// <summary>Short controller pulse, if the user turned vibration on.</summary>
    public void Rumble()
    {
        if (Settings.RumbleStrength > 0) _gamepad.Pulse(45, Settings.RumbleStrength);
    }

    /// <summary>A controller pulse of <paramref name="milliseconds"/>, if the user turned vibration on.</summary>
    public void Rumble(int milliseconds)
    {
        if (Settings.RumbleStrength > 0) _gamepad.Pulse(milliseconds, Settings.RumbleStrength);
    }

    /// <summary>
    /// A pulse at a share of full power (0–1) relative to the user's vibration strength, for effects that build up
    /// (the boost animations). Nothing when vibration is off.
    /// </summary>
    public void RumbleScaled(int milliseconds, double power)
    {
        if (Settings.RumbleStrength <= 0) return;
        _gamepad.Pulse(milliseconds, (int)Math.Clamp(Settings.RumbleStrength * power * 1.6, 1, 100));
    }

    private void GameList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (_screens.Count > 0) return;
        SetButtonIndex(-1);
        MoveSelection(e.Delta < 0 ? +1 : -1);
    }

    private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _tileWasSelected = sender is ListBoxItem { IsSelected: true };
        SetButtonIndex(-1);
    }

    private void Tile_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // First click selects a tile; clicking the already-selected tile starts it.
        if (_tileWasSelected && sender is ListBoxItem { IsSelected: true, DataContext: Game g }) _ = ActivateAsync(g);
        _tileWasSelected = false;
    }

    // Touch is handled on its own rather than through the mouse events Windows makes from it, which didn't always
    // arrive (the Game library tile wouldn't open from a tap). A tap selects a tile; a tap on the selected tile
    // opens it; the Game library, More apps and Add app tiles open from the first tap.
    private bool _touchWasSelected;
    private Point _tileTouchStart;

    private void Tile_TouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is not ListBoxItem item) return;
        _touchWasSelected = item.IsSelected;
        _tileTouchStart = e.GetTouchPoint(this).Position;
        SetButtonIndex(-1);
    }

    private void Tile_TouchUp(object? sender, TouchEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: Game g } item) return;
        var at = e.GetTouchPoint(this).Position;
        if (Math.Abs(at.X - _tileTouchStart.X) + Math.Abs(at.Y - _tileTouchStart.Y) > 30) return; // a swipe, not a tap
        e.Handled = true; // no mouse click made from this tap as well
        _tileWasSelected = false;
        item.IsSelected = true;
        if (_touchWasSelected || g.IsActionTile) _ = ActivateAsync(g);
    }

    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: Game g } item) return;
        item.IsSelected = true;
        _ = OpenOptionsAsync(g);
    }

    // ================================================================ smooth scrolling

    private void ScrollSelectionIntoView()
    {
        if (_scroll == null || GameList.SelectedIndex < 0) return;

        // Tiles are not all the same width, so add up the ones before the selection.
        double side = _rowSide;
        double left = side;
        for (int i = 0; i < GameList.SelectedIndex && i < _view.Count; i++) left += _view[i].TileWidth;
        double right = left + _view[Math.Min(GameList.SelectedIndex, _view.Count - 1)].TileWidth;
        double viewport = _scroll.ViewportWidth;
        double target = _scrollTarget;

        if (LayoutName == "stage") target = (left + right) / 2 - viewport / 2; // Stage: the selected game sits in the middle
        else if (left - side < target) target = left - side;
        else if (right + side > target + viewport) target = right + side - viewport;

        _scrollTarget = Math.Max(0, target);
        if (!_scrollAnimating)
        {
            _scrollAnimating = true;
            _scrollLastFrame = TimeSpan.Zero;
            _scrollPos = _scroll.HorizontalOffset;
            CompositionTarget.Rendering += AnimateScroll;
        }
    }

    private TimeSpan _scrollLastFrame;

    /// <summary>
    /// Where the glide is, kept here rather than read back from the row: the row only takes a new offset at its next
    /// layout, so reading it every frame got last frame's value, and the glide stuttered and fought the selection when
    /// the stick was held.
    /// </summary>
    private double _scrollPos;

    /// <summary>
    /// Glides the row to the selection. Timed by the clock rather than by frames, so it's just as quick when frames
    /// are capped at 30 a second (Performance mode) or some are dropped; it settles in about a sixth of a second.
    /// </summary>
    private void AnimateScroll(object? sender, EventArgs e)
    {
        if (_scroll == null) return;
        var frameTime = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (frameTime == _scrollLastFrame && frameTime != TimeSpan.Zero) return; // the same frame reported twice
        double ms = _scrollLastFrame == TimeSpan.Zero ? 16.7 : Math.Clamp((frameTime - _scrollLastFrame).TotalMilliseconds, 1, 100);
        _scrollLastFrame = frameTime;

        double target = Math.Min(_scrollTarget, Math.Max(0, _scroll.ScrollableWidth));
        double diff = target - _scrollPos;
        if (Math.Abs(diff) < 0.5)
        {
            _scrollPos = target;
            _scroll.ScrollToHorizontalOffset(target);
            _scrollAnimating = false;
            CompositionTarget.Rendering -= AnimateScroll;
            return;
        }
        double step = 1 - Math.Exp(-ms / 45.0);
        _scrollPos += diff * step;
        // Whole device pixels: a fractional offset makes every cover resample (and shimmer) on each frame.
        _scroll.ScrollToHorizontalOffset(Math.Round(_scrollPos));
    }

    // ================================================================ round buttons

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        SetButtonIndex(-1);
        SetMode(_mode == HomeView.Favorites ? HomeView.Games : HomeView.Favorites);
    }

    private void Apps_Click(object sender, RoutedEventArgs e)
    {
        SetButtonIndex(-1);
        SetMode(_mode == HomeView.Apps ? HomeView.Games : HomeView.Apps);
    }

    private void Captures_Click(object sender, RoutedEventArgs e) => Push(new CapturesScreen());

    private void Music_Click(object sender, RoutedEventArgs e) => Push(new MediaScreen());

    private void Friends_Click(object sender, RoutedEventArgs e) => Push(new FriendsScreen());

    private void Settings_Click(object sender, RoutedEventArgs e) => Push(new SettingsScreen());

    private void Power_Click(object sender, RoutedEventArgs e) => _ = PowerMenuAsync();

    public async Task PowerMenuAsync()
    {
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        var menu = new PowerMenuScreen(running == null ? null : running.Name + " is still running. Restarting or shutting down loses unsaved progress.");
        Push(menu);
        switch (await menu.Result)
        {
            case 0: Power.Lock(); break;
            case 1: Power.Sleep(); break;
            case 2: Power.Hibernate(); break;
            case 3: Power.Restart(); break;   // confirmed on the button itself
            case 4: Power.ShutDown(); break;
            case 5: await ExitToDesktopAsync(); break;
        }
    }

    private bool _exiting;

    /// <summary>Closes the launcher, with a short closing animation if that is switched on.</summary>
    public async Task ExitToDesktopAsync()
    {
        if (_exiting) return;
        _exiting = true;

        // If we are standing in for the desktop, bring the real one up before leaving.
        if (Startup.ReplacesDesktop && !Startup.DesktopIsRunning) await Startup.StartDesktopAsync();

        if (Settings.ExitAnimation && !Theme.ReduceMotion) await PlayExitAnimationAsync();
        Close();
    }

    private async Task<bool> ConfirmPowerAsync(string question, string action)
    {
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        string? warning = running == null
            ? null
            : running.Name + " is still running. Unsaved progress will be lost.";
        return await AskAsync(question, warning, action, "Cancel") == 0;
    }

    private void Quick_Click(object sender, RoutedEventArgs e) => Push(new QuickSettingsScreen());

    private void Wifi_Click(object sender, RoutedEventArgs e) => Push(new ConnectionBladeScreen(bluetooth: false));

    private void Bluetooth_Click(object sender, RoutedEventArgs e) => Push(new ConnectionBladeScreen(bluetooth: true));

    private DateTime _phonesCheckedAt;
    private (string Name, int Percent)? _phones;

    /// <summary>Refreshes the Wi-Fi and Bluetooth icons in the top bar.</summary>
    private async void UpdateConnections()
    {
        ConnectionStatus status;
        try { status = await NetworkService.GetStatusAsync(); }
        catch (Exception) { return; }

        WifiButton.Visibility = status.WifiOn ? Visibility.Visible : Visibility.Collapsed;
        // Signal strength when connected; a dimmed icon when Wi-Fi is on but not connected.
        WifiIcon.Text = status.WifiBars switch
        {
            null => "\uE701",
            <= 1 => "\uE872",
            2 => "\uE873",
            3 => "\uE874",
            _ => "\uE701"
        };
        WifiIcon.Opacity = status.WifiBars == null ? 0.35 : 1;

        BluetoothButton.Visibility = status.BluetoothOn ? Visibility.Visible : Visibility.Collapsed;

        // Headphones' battery beside the Bluetooth icon (Settings > Home screen > Headphone battery).
        // Asked once a minute (the connection icons refresh every 10 seconds), and not during a game.
        (string Name, int Percent)? phones = null;
        if (status.BluetoothOn && Settings.ShowHeadphoneBattery && !AllGames.Any(g => g.IsRunning))
        {
            if (DateTime.UtcNow - _phonesCheckedAt > TimeSpan.FromSeconds(55))
            {
                _phonesCheckedAt = DateTime.UtcNow;
                _phones = await NetworkService.GetHeadphoneBatteryAsync();
            }
            phones = _phones;
        }
        HeadphonePanel.Visibility = phones == null ? Visibility.Collapsed : Visibility.Visible;
        if (phones is { } p)
        {
            HeadphoneText.Text = p.Percent + "%";
            HeadphoneText.Foreground = p.Percent <= 15 ? Brushes.OrangeRed : (Brush)FindResource("FgBrush");
            BluetoothButton.ToolTip = $"{p.Name}: {p.Percent}% battery";
        }
        else BluetoothButton.ToolTip = null;
    }

    private void Avatar_Click(object sender, RoutedEventArgs e) => Push(new ProfileScreen());

    // ================================================================ misc

    /// <summary>Battery of the connected controller ("Full", "Low", "Wired"…), or null if there is none.</summary>
    public string? ControllerBattery => _gamepad.GetBattery();

    /// <summary>Re-applies settings that change what the home screen shows.</summary>
    public void ApplySettings()
    {
        Dispatcher.BeginInvoke(new Action(FitButtonRow), System.Windows.Threading.DispatcherPriority.Loaded); // buttons may have been shown or hidden
        _clockTimer.Interval = TimeSpan.FromSeconds(Settings.ClockSeconds ? 1 : 5);
        UpdateClock();
        _gamepad.SummonCombo = Settings.SummonCombo;
        try { Screens.PerfOverlay.Apply(Settings); } catch (Exception ex) { CrashLog.Write("Performance overlay", ex); }
        Motion.ButtonAnimations = Settings.ButtonAnimations;
        Motion.ButtonStyle = Settings.ButtonAnimationStyle;
        Motion.PageStyle = Settings.PageTransition;
        Motion.TileStyle = Settings.TileHighlight;
        Motion.PunchIn = Settings.PunchIn;
        TouchKeyboard.UseWindows = Settings.TouchKeyboard == "windows";
        ApplySummonKey();
        Hints.SetStyle(Settings.GlyphStyle);
        Hints.SetLook(Settings.GlyphLook);
        RefreshHomeButtons();
        ApplyAspect();
        UpdateMenuMusic();
        if (Theme.ReduceMotion) Motion.StopPulse(RingOf(GameList.SelectedItem));
        else Motion.StartPulse(RingOf(GameList.SelectedItem));
        if (GameList.SelectedItem != null)
            Motion.GrowTile(GameList.ItemContainerGenerator.ContainerFromItem(GameList.SelectedItem) as UIElement, true);
        _ = ApplyGameAccentAsync((GameList.SelectedItem as Game)?.Actual);
        if (!Settings.AnimatedCovers && _animatedGame != null)
        {
            _animatedGame.AnimatedCover = null;
            _animatedGame = null;
        }
        if (Settings.HeroBackgrounds) _ = ShowHeroAsync((GameList.SelectedItem as Game)?.Actual);
        else ClearHero();
        ApplyView(); // the "Continue playing" tile may have been switched on or off
    }

    /// <summary>For pages that changed something about a game (rating, status, notes).</summary>
    public void LibraryEdited() => NotifyLibraryChanged();

    /// <summary>Re-applies everything on the Themes page: colors, tile corners, sounds and wallpaper.</summary>
    public void ApplyLook()
    {
        Theme.Apply(Settings);
        UiSound.Configure(Settings);
        UpdateHomeRing(); // the ring brush follows the new highlight color
        ApplyWallpaper();
        ApplyUiStyle();
        UpdateMenuMusic();
        // Open pages take on the new wallpaper or artwork straight away (their colors and font follow on their own).
        foreach (var screen in _screens) screen.RefreshBackdrop();
    }

    private void ApplyWallpaper()
    {
        var brush = Theme.WallpaperBrush(Settings.Wallpaper) ?? UiStyles.Backdrop(UiStyles.Current);
        Theme.CurrentWallpaper = brush; // pages show the same wallpaper behind them
        WallpaperLayer.Fill = brush;
        // Blur (Settings > wallpaper), with the edges pushed off screen so they don't go soft. None in Performance mode.
        int blur = Perf.Active ? 0 : Math.Clamp(Settings.WallpaperBlur, 0, 100);
        WallpaperLayer.Effect = blur > 0 ? new System.Windows.Media.Effects.BlurEffect { Radius = blur, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance } : null;
        WallpaperLayer.Margin = new Thickness(-blur);
        WallpaperLayer.Visibility = WallpaperScrim.Visibility = brush == null ? Visibility.Collapsed : Visibility.Visible;
        if (_dynamicVideo != null) ApplyDynamicBackground(); // a moving background takes the wallpaper's place
    }

    public void SetButtonMap(string map)
    {
        if (!Hints.IsValidMap(map)) return;
        Settings.ButtonMap = map;
        Settings.Save();
        Hints.Map = map;
        ApplyHintLabels();
    }

    private void ApplyHintLabels()
    {
        Hints.NotifyChanged();
        UpdateHomeHints();
    }

    // ---------------------------------------------------------------- animated covers

    private async Task ShowAnimatedCoverAsync(Game? game)
    {
        if (!Settings.AnimatedCovers || Theme.ReduceMotion || Perf.Active || game == null || !game.IsGame || game.HasAnimatedCover) return;
        try
        {
            var path = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
                return await client.GetAnimatedCoverAsync(game, CancellationToken.None);
            });
            // Only if the highlight is still on this game.
            if (path == null || !ReferenceEquals(GameList.SelectedItem, game) || !Settings.AnimatedCovers) return;
            _animatedGame = game;
            game.AnimatedCover = new Uri(path);
        }
        catch (Exception)
        {
            // None available or offline: the still cover stays.
        }
    }

    private void Animated_SourceChanged(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is not MediaElement media) return;
        try
        {
            if (media.Source != null) media.Play();
            else media.Close();
        }
        catch (Exception) { }
    }

    private void Animated_Ended(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement media) return;
        try
        {
            media.Position = TimeSpan.Zero; // loop
            media.Play();
        }
        catch (Exception) { }
    }

    private void Animated_Failed(object? sender, ExceptionRoutedEventArgs e)
    {
        // This PC can't play the clip (missing video codec): quietly go back to the still cover.
        if (sender is MediaElement { DataContext: Game game }) game.AnimatedCover = null;
    }

    // ---------------------------------------------------------------- startup intro

    /// <summary>The logo: an outlined tile with a solid one tucked into its corner.</summary>
    private static Border BuildLogoMark(double size)
    {
        double k = size / 120;
        var mark = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(16 * k), BorderThickness = new Thickness(9 * k),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mark.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var inner = new Border
        {
            Width = 46 * k, Height = 46 * k, CornerRadius = new CornerRadius(8 * k),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12 * k, 12 * k)
        };
        inner.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        mark.Child = inner;
        return mark;
    }

    /// <summary>The built-in startup animations, by the name saved in settings.</summary>
    public static readonly (string Id, string Name)[] BootAnimations =
    {
        ("logo", "Logo pop"), ("tiles", "Falling tiles"), ("sweep", "Color sweep"), ("rings", "Ripples"),
        ("typed", "Typed name"), ("zoom", "Zoom through"),
        ("glitch", "Glitch"), ("assemble", "Pixel assemble"), ("neon", "Neon sign"), ("liquid", "Liquid fill"), ("orbit", "Orbit"),
        ("blinds", "Blinds"), ("burst", "Particle burst"), ("hologram", "Hologram scan"), ("flip", "Card flip"), ("aurora", "Aurora"),
        ("random", "Surprise me")
    };

    /// <summary>Plays the chosen startup animation now, so it can be seen from Settings.</summary>
    public void PreviewIntro()
    {
        if (IntroLayer.Children.Count == 0) PlayIntro();
    }

    /// <summary>A short animation and chime over the home screen while it loads.</summary>
    private void PlayIntro()
    {
        if (TryPlayBootVideo()) return; // the user's own video replaces the built-in animation
        UiSound.Startup();
        HoldMusicForStartup(); // the menu music starts once the startup sound has finished
        if (Theme.ReduceMotion) return; // chime only

        static System.Windows.Media.Animation.DoubleAnimation Anim(double from, double to, int ms, int delayMs = 0,
            System.Windows.Media.Animation.IEasingFunction? ease = null) =>
            new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };
        var easeOut = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var bounce = new System.Windows.Media.Animation.BackEase { Amplitude = 0.5, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        TextBlock Name(double size = 40)
        {
            var t = new TextBlock
            {
                Text = "SquareDeck", FontSize = size, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            return t;
        }

        var cover = new Grid { ClipToBounds = true };
        cover.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        string style = Settings.BootAnimation;
        if (style == "random")
        {
            var choices = BootAnimations.Where(b => b.Id != "random").ToArray();
            style = choices[Random.Shared.Next(choices.Length)].Id;
        }

        int total = 1500; // when the cover starts to fade
        if (style == "look") style = "logo"; // "From the look" (6.12 to 6.14): the looks it belonged to are gone
        if (PlayMoreIntro(style, cover, center, ref total)) style = "";
        switch (style)
        {
            case "":
                break;
            case "tiles":
            {
                // Five tiles drop into a row, one after another, like the home screen assembling itself.
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                for (int i = 0; i < 5; i++)
                {
                    var drop = new TranslateTransform(0, -260);
                    var tile = new Border
                    {
                        Width = 74, Height = 74, Margin = new Thickness(7), CornerRadius = new CornerRadius(8),
                        RenderTransform = drop, Opacity = 0
                    };
                    tile.SetResourceReference(Border.BackgroundProperty, i == 2 ? "AccentBrush" : "TileBrush");
                    row.Children.Add(tile);
                    drop.BeginAnimation(TranslateTransform.YProperty, Anim(-260, 0, 480, i * 110, bounce));
                    tile.BeginAnimation(OpacityProperty, Anim(0, 1, 200, i * 110));
                }
                var name = Name();
                name.Opacity = 0;
                name.BeginAnimation(OpacityProperty, Anim(0, 1, 380, 800));
                center.Children.Add(row);
                center.Children.Add(name);
                total = 1900;
                break;
            }
            case "sweep":
            {
                // A band of the highlight color wipes across the screen and leaves the name behind it.
                var move = new TranslateTransform(-1500, 0);
                var band = new Border { Width = 1500, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = move };
                band.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                cover.Children.Add(band);
                move.BeginAnimation(TranslateTransform.XProperty, Anim(-1500, 1400, 1000, 100,
                    new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }));
                var mark = BuildLogoMark(120);
                var name = Name();
                mark.Opacity = name.Opacity = 0;
                mark.BeginAnimation(OpacityProperty, Anim(0, 1, 300, 620));
                name.BeginAnimation(OpacityProperty, Anim(0, 1, 300, 760));
                center.Children.Add(mark);
                center.Children.Add(name);
                total = 1800;
                break;
            }
            case "rings":
            {
                // Ripples spread out from the logo.
                for (int i = 0; i < 3; i++)
                {
                    var grow = new ScaleTransform(0.2, 0.2);
                    var ring = new System.Windows.Shapes.Ellipse
                    {
                        Width = 520, Height = 520, StrokeThickness = 5, Opacity = 0,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = grow
                    };
                    ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
                    cover.Children.Add(ring);
                    var spread = Anim(0.2, 1.7, 1200, i * 260, easeOut);
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
                    ring.BeginAnimation(OpacityProperty, Anim(0.75, 0, 1200, i * 260));
                }
                var mark = BuildLogoMark(120);
                var name = Name();
                center.Opacity = 0;
                center.BeginAnimation(OpacityProperty, Anim(0, 1, 420, 150));
                center.Children.Add(mark);
                center.Children.Add(name);
                total = 1800;
                break;
            }
            case "typed":
            {
                // The name is typed out one letter at a time, with a blinking cursor.
                const string word = "SquareDeck";
                var name = Name(54);
                name.Margin = new Thickness(0);
                name.Text = "";
                name.FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New");
                name.Width = 400; // fixed, so the text doesn't slide sideways as it grows
                name.TextAlignment = TextAlignment.Left;
                center.Children.Add(name);
                int shown = 0;
                var typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(95) };
                typing.Tick += (_, _) =>
                {
                    shown++;
                    bool done = shown >= word.Length;
                    name.Text = word[..Math.Min(shown, word.Length)] + (done && shown % 6 >= 3 ? "" : "_");
                    if (shown > word.Length + 8) typing.Stop();
                };
                typing.Start();
                total = 1700;
                break;
            }
            case "zoom":
            {
                // The logo rushes toward the viewer and the home screen appears through it.
                var grow = new ScaleTransform(0.4, 0.4);
                var mark = BuildLogoMark(120);
                mark.RenderTransformOrigin = new Point(0.5, 0.5);
                mark.RenderTransform = grow;
                center.Children.Add(mark);
                var settle = Anim(0.4, 1, 450, 0, bounce);
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                grow.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
                var rush = Anim(1, 16, 520, 950, new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn });
                var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
                later.Tick += (_, _) =>
                {
                    later.Stop();
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 16, TimeSpan.FromMilliseconds(520)) { EasingFunction = rush.EasingFunction });
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 16, TimeSpan.FromMilliseconds(520)) { EasingFunction = rush.EasingFunction });
                };
                later.Start();
                total = 1100;
                break;
            }
            default:
            {
                // The logo pops in with a small bounce.
                var scale = new ScaleTransform(0.6, 0.6);
                center.RenderTransformOrigin = new Point(0.5, 0.5);
                center.RenderTransform = scale;
                center.Opacity = 0;
                center.Children.Add(BuildLogoMark(120));
                center.Children.Add(Name());
                var pop = Anim(0.6, 1, 520, 0, bounce);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                center.BeginAnimation(OpacityProperty, Anim(0, 1, 380));
                break;
            }
        }

        cover.Children.Add(center);
        IntroLayer.Children.Add(cover);

        var fade = Anim(1, 0, 450, total);
        fade.Completed += (_, _) => IntroLayer.Children.Remove(cover);
        cover.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- hero background

    private void ClearHero()
    {
        _heroCts?.Cancel();
        _heroKey = null;
        Theme.CurrentHero = null;
        Theme.CurrentHeroSoft = null;
        HeroImage.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>Fades in blurred SteamGridDB artwork for the highlighted game.</summary>
    private async Task ShowHeroAsync(Game? game)
    {
        if (!Settings.HeroBackgrounds || game == null || !game.IsGame)
        {
            ClearHero();
            return;
        }
        if (game.Key == _heroKey) return;

        _heroCts?.Cancel();
        var cts = _heroCts = new CancellationTokenSource();
        var ct = cts.Token;
        _heroKey = game.Key;

        ImageSource? image = null;
        ImageSource? soft = null, pageSoft = null;
        try
        {
            var style = UiStyles.Current;
            (image, soft, pageSoft) = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
                var path = await client.GetHeroAsync(game, ct);
                // Blurred styles only need a small decode; styles that show it sharp need the full width.
                // Performance mode loads blurred styles' artwork tiny instead of blurring it.
                int width = style.HeroBlur < 10 ? 1600 : Perf.Active ? 64 : 480;
                var picture = path == null ? null : ImageLoader.FromFile(path, width);
                if (picture == null || Perf.Active) return (picture, (ImageSource?)null, (ImageSource?)null);
                // Blurred here, once, rather than live by the graphics card on every frame (see ImageLoader.Blurred).
                ImageSource? home = style.HeroBlur >= 10 ? ImageLoader.Blurred(picture, style.HeroBlur) : null;
                ImageSource? page = style.PageHero ? ImageLoader.Blurred(picture, style.Glass ? 50 : 36) : null;
                return (picture, home, page);
            }, ct);
        }
        catch (Exception)
        {
            // No artwork, offline, or the highlight moved on.
        }
        if (ct.IsCancellationRequested) return;

        if (image == null)
        {
            HeroImage.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            return;
        }
        HeroImage.Source = soft ?? image;
        _heroPreBlurred = soft != null;
        // Already blurred: no live blur on top (styles with only a slight blur keep the live one).
        HeroImage.Effect = soft != null ? null : UiStyles.Current.HeroBlur > 0 && !Perf.Active ? HeroBlur : null;
        UpdateLayerCaches();
        Theme.CurrentHero = image;
        Theme.CurrentHeroSoft = pageSoft;
        HeroImage.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, UiStyles.Current.HeroOpacity, TimeSpan.FromMilliseconds(450)));
    }

    public void ApplyAvatar() => AvatarButton.Content = Avatars.Build(Settings.Avatar, 58);

    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Normal; // must leave Maximized before removing the border
            WindowStyle = WindowStyle.None;
            // No resize frame in full screen: with one, Windows keeps an invisible 8-pixel border inside the window,
            // which pushed the edges of the interface off screen.
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
    }

    private void UpdateClock()
    {
        string time = Settings.Clock24Hour
            ? (Settings.ClockSeconds ? "H:mm:ss" : "H:mm")
            : (Settings.ClockSeconds ? "h:mm:ss tt" : "h:mm tt");
        ClockText.Text = DateTime.Now.ToString((Settings.ClockDate ? "ddd, MMM d    " : "") + time);
        ClockText.Visibility = Settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;

        // "Follow system": pick up a Windows light/dark switch without a restart.
        if (Settings.ThemeMode == "system" && Theme.SystemIsDark() != Theme.CurrentIsDark) Theme.Apply(Settings);

        CheckPerformancePower(); // "on battery only" follows the cable
        var battery = Battery.GetPercent();
        BatteryButton.Visibility = battery == null ? Visibility.Collapsed : Visibility.Visible;
        if (battery != null)
        {
            BatteryText.Text = battery + "%";
            bool pluggedIn = Battery.IsPluggedIn();
            BatteryIcon.Text = Battery.Glyph(battery.Value, pluggedIn); // shows the charging bolt
            CheckBattery(battery.Value, pluggedIn);
        }
        BatteryText.Visibility = Settings.ShowBatteryPercent ? Visibility.Visible : Visibility.Collapsed;

        // Only wireless controllers have a level worth showing.
        var pad = ControllerBattery;
        bool wireless = pad != null && pad != "Wired";
        PadPanel.Visibility = wireless ? Visibility.Visible : Visibility.Collapsed;
        if (wireless) PadText.Text = pad;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
