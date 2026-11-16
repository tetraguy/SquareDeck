using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Bits shared by the Music page's "My music" and "Spotify" tabs.</summary>
internal static class MusicUi
{
    public static Button Control(string glyph, string tip, Action click, double size = 54, bool primary = false)
    {
        var text = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size * 0.38, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = text };
        if (primary)
        {
            face.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        }
        else
        {
            face.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        }
        var b = Ui.Tile(face);
        b.ToolTip = tip;
        b.Margin = new Thickness(-2);
        b.Click += (_, _) => click();
        return b;
    }

    public static void SetGlyph(Button b, string glyph, bool on = false)
    {
        if (b.Content is Border { Child: TextBlock t } face)
        {
            t.Text = glyph;
            face.Opacity = 1;
            if (on) t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            else if (face.Background is not SolidColorBrush s || !ReferenceEquals(s, face.TryFindResource("AccentBrush"))) t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        }
    }

    /// <summary>A chip in a row of choices (Songs / Albums / Artists, Playlists / Liked songs...).</summary>
    public static Button Chip(string text, bool selected, Action click)
    {
        var label = Ui.Text(text, 15, selected ? "BgBrush" : "FgBrush", bold: selected);
        var face = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 7, 16, 7), Child = label };
        face.SetResourceReference(Border.BackgroundProperty, selected ? "AccentBrush" : "CircleBrush");
        var b = Ui.Tile(face);
        b.Margin = new Thickness(-4, -4, 2, -4);
        b.Click += (_, _) => click();
        return b;
    }

    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>The square artwork box with a music note until there's a picture.</summary>
    /// <summary>The album cover with the song's title and artist beside it.</summary>
    public static Grid NowPlayingHeader(Image art, TextBlock title, TextBlock artist)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var box = ArtBox(art, 150);
        box.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(box);
        var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.FontSize = 22;
        title.Margin = new Thickness(0, 0, 0, 4);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxHeight = 62; // two lines
        artist.TextWrapping = TextWrapping.Wrap;
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.MaxHeight = 46;
        text.Children.Add(title);
        text.Children.Add(artist);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>The player column: scrolls if larger text makes it taller than the page.</summary>
    public static ScrollViewer Column(StackPanel content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = content
    };

    public static Border ArtBox(Image image, double size)
    {
        var grid = new Grid();
        var note = Ui.Glyph("", size * 0.3, "SubtleBrush");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(note);
        grid.Children.Add(image);
        var box = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = grid,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.45 }
        };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Clip = new RectangleGeometry(new Rect(0, 0, size, size), 16, 16);
        return box;
    }
}

/// <summary>
/// "My music": a player for a folder of songs. Now playing on the left (art, progress, controls, volume, what's
/// next); the library on the right as songs, albums or artists.
/// </summary>
public sealed class LocalMusicView : Grid
{
    private readonly Screen _page;
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _title = Ui.Text("Nothing playing", 26, "FgBrush", bold: true);
    private readonly TextBlock _artist = Ui.Text("", 17, "SubtleBrush");
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, Focusable = true, IsMoveToPointEnabled = true };
    private readonly TextBlock _time = Ui.Text("", 14, "SubtleBrush"), _length = Ui.Text("", 14, "SubtleBrush");
    private readonly Button _play, _shuffle, _repeat;
    private readonly StackPanel _list = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _folder = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _upNext = Ui.Text("", 14, "SubtleBrush");
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string _view = "songs";          // songs, albums, artists
    private string? _filterAlbum, _filterArtist;
    private bool _seeking;
    private string _artKey = "";

    public LocalMusicView(Screen page)
    {
        _page = page;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- now playing
        var left = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
        // Cover and title side by side, so the player controls below still fit on the page.
        left.Children.Add(MusicUi.NowPlayingHeader(_art, _title, _artist));
        _seek.Margin = new Thickness(0, 14, 0, 0);
        _seek.PreviewMouseDown += (_, _) => _seeking = true;
        _seek.PreviewMouseUp += (_, _) => { _seeking = false; LocalMusic.Seek(TimeSpan.FromSeconds(_seek.Value)); };
        _seek.KeyUp += (_, _) => LocalMusic.Seek(TimeSpan.FromSeconds(_seek.Value));
        _seek.SmallChange = 5;
        _seek.LargeChange = 15;
        left.Children.Add(_seek);
        var times = new DockPanel();
        DockPanel.SetDock(_length, Dock.Right);
        times.Children.Add(_length);
        times.Children.Add(_time);
        left.Children.Add(times);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 12, 0, 0) };
        _shuffle = MusicUi.Control("", "Shuffle", LocalMusic.ToggleShuffle, 46);
        controls.Children.Add(_shuffle);
        controls.Children.Add(MusicUi.Control("", "Previous", LocalMusic.Previous));
        _play = MusicUi.Control("", "Play or pause", Play, 66, primary: true);
        controls.Children.Add(_play);
        controls.Children.Add(MusicUi.Control("", "Next", LocalMusic.Next));
        _repeat = MusicUi.Control("", "Repeat", LocalMusic.CycleRepeat, 46);
        controls.Children.Add(_repeat);
        left.Children.Add(controls);

        left.Children.Add(Ui.SliderRow("Volume", "", out var volume, out var volumeText));
        volume.Value = page.Host?.Settings.LocalVolume ?? 80;
        volumeText.Text = (int)volume.Value + "%";
        volume.ValueChanged += (_, e) => { LocalMusic.SetVolume((int)e.NewValue); volumeText.Text = (int)e.NewValue + "%"; };
        _upNext.Margin = new Thickness(4, 12, 0, 0);
        _upNext.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(_upNext);
        Children.Add(MusicUi.Column(left));

        // ---- library
        var right = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var choose = Ui.PanelButton("Choose folder");
        choose.Click += (_, _) => _ = ChooseFolderAsync();
        DockPanel.SetDock(choose, Dock.Right);
        top.Children.Add(choose);
        var refresh = Ui.PanelButton("Refresh");
        refresh.Margin = new Thickness(0, 0, 8, 0);
        refresh.Click += (_, _) => _ = ScanAsync();
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        _folder.VerticalAlignment = VerticalAlignment.Center;
        _folder.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(_folder);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        DockPanel.SetDock(_chips, Dock.Top);
        right.Children.Add(_chips);
        right.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _list
        });
        SetColumn(right, 1);
        Children.Add(right);

        _clock.Tick += (_, _) => ShowProgress();
        Loaded += (_, _) =>
        {
            LocalMusic.StateChanged += ShowState;
            LocalMusic.LibraryChanged += BuildList;
            _clock.Start();
            ShowState();
            BuildList();
            if (LocalMusic.Tracks.Count == 0 && (_page.Host?.Settings.MusicFolder.Length ?? 0) > 0) _ = ScanAsync();
        };
        Unloaded += (_, _) =>
        {
            LocalMusic.StateChanged -= ShowState;
            LocalMusic.LibraryChanged -= BuildList;
            _clock.Stop();
        };
    }

    public void Play() => LocalMusic.TogglePlay();

    public UIElement? FirstFocus => _play;

    private async Task ScanAsync()
    {
        _folder.Text = "Reading your music…";
        await LocalMusic.ScanAsync();
    }

    private async Task ChooseFolderAsync()
    {
        var host = _page.Host;
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        int choice = await host.AskWithIconsAsync("Music folder", "Songs in this folder and the folders inside it are played here.",
            ("Music (" + music + ")", ""), ("Choose another folder…", ""));
        string? folder = choice switch
        {
            0 => music,
            1 => await host.PickFolderAsync("Choose your music folder"),
            _ => null
        };
        if (string.IsNullOrEmpty(folder)) return;
        host.Settings.MusicFolder = folder;
        host.Settings.Save();
        _filterAlbum = _filterArtist = null;
        _view = "songs";
        await ScanAsync();
    }

    private void ShowState()
    {
        var t = LocalMusic.Current;
        _title.Text = t?.Title ?? "Nothing playing";
        _artist.Text = t == null ? (LocalMusic.Tracks.Count > 0 ? "Choose a song, or press Play to start from the top." : "")
                                 : t.Artist + (t.Album.Length > 0 ? "  ·  " + t.Album : "");
        MusicUi.SetGlyph(_play, LocalMusic.IsPlaying ? "" : "");
        MusicUi.SetGlyph(_shuffle, "", LocalMusic.Shuffle);
        MusicUi.SetGlyph(_repeat, LocalMusic.Repeat == "one" ? "" : "", LocalMusic.Repeat != "off");
        var next = LocalMusic.UpNext(3).ToList();
        _upNext.Text = next.Count == 0 ? "" : "Up next:  " + string.Join("   ·   ", next.Select(n => n.Title));
        if (t != null && t.Path != _artKey)
        {
            _artKey = t.Path;
            _ = LoadArtAsync(t);
        }
        HighlightPlaying();
        ShowProgress();
    }

    private async Task LoadArtAsync(LocalTrack t)
    {
        var bytes = await LocalMusic.ArtAsync(t);
        if (_artKey != t.Path) return;
        _art.Source = bytes != null ? ImageLoader.FromBytes(bytes, 600) : null;
        _page.SetBackdrop(_art.Source);
    }

    private void ShowProgress()
    {
        var length = LocalMusic.Length;
        var position = LocalMusic.Position;
        _time.Text = LocalMusic.Current == null ? "" : MusicUi.Clock(position);
        _length.Text = length > TimeSpan.Zero ? MusicUi.Clock(length) : "";
        if (_seeking || _seek.IsKeyboardFocused) return;
        _seek.Maximum = Math.Max(1, length.TotalSeconds);
        _seek.Value = Math.Min(_seek.Maximum, position.TotalSeconds);
    }

    // ---------------------------------------------------------------- the library

    private void BuildList()
    {
        var s = _page.Host?.Settings;
        string folder = s?.MusicFolder ?? "";
        var tracks = LocalMusic.Tracks;
        _folder.Text = folder.Length == 0 ? "No music folder chosen yet"
            : LocalMusic.Scanning ? "Reading your music…"
            : $"{tracks.Count} songs  ·  {folder}";

        _chips.Children.Clear();
        foreach (var (id, name) in new[] { ("songs", "Songs"), ("albums", "Albums"), ("artists", "Artists") })
        {
            string v = id;
            _chips.Children.Add(MusicUi.Chip(name, _view == id && _filterAlbum == null && _filterArtist == null, () =>
            {
                _view = v;
                _filterAlbum = _filterArtist = null;
                BuildList();
            }));
        }
        if (_filterAlbum != null || _filterArtist != null)
            _chips.Children.Add(MusicUi.Chip("‹  " + (_filterAlbum ?? _filterArtist), true, () => { _filterAlbum = _filterArtist = null; BuildList(); }));

        _list.Children.Clear();
        if (folder.Length == 0 || (tracks.Count == 0 && !LocalMusic.Scanning))
        {
            var note = Ui.Text(folder.Length == 0
                ? "Choose the folder your songs are in (MP3, M4A, AAC, WMA, WAV or FLAC). Folders inside it are included."
                : "No songs found in this folder.", 17, "SubtleBrush");
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(6, 20, 0, 0);
            _list.Children.Add(note);
            return;
        }

        if (_view == "albums" && _filterAlbum == null)
        {
            foreach (var album in tracks.GroupBy(t => (t.Album, t.Artist)).OrderBy(g => g.Key.Album, StringComparer.CurrentCultureIgnoreCase).Take(400))
            {
                var key = album.Key;
                AddRow(key.Album.Length > 0 ? key.Album : "Unknown album", $"{album.Count()} songs", "", key.Artist,
                    () => { _filterAlbum = key.Album; _filterArtist = null; BuildList(); });
            }
            Ui.GroupRows(_list);
            return;
        }
        if (_view == "artists" && _filterArtist == null)
        {
            foreach (var artist in tracks.GroupBy(t => t.Artist).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Take(400))
            {
                string name = artist.Key;
                AddRow(name, $"{artist.Count()} songs", "", null, () => { _filterArtist = name; _filterAlbum = null; BuildList(); });
            }
            Ui.GroupRows(_list);
            return;
        }

        var songs = tracks.Where(t => (_filterAlbum == null || t.Album == _filterAlbum) && (_filterArtist == null || t.Artist == _filterArtist)).ToList();
        if (songs.Count > 1)
        {
            AddRow("Play all", songs.Count + " songs", "", null, () => LocalMusic.PlayList(songs, 0));
            AddRow("Shuffle all", null, "", null, () =>
            {
                if (!LocalMusic.Shuffle) LocalMusic.ToggleShuffle();
                LocalMusic.PlayList(songs, new Random().Next(songs.Count));
            });
        }
        const int Shown = 400;
        for (int i = 0; i < Math.Min(Shown, songs.Count); i++)
        {
            int index = i;
            var t = songs[i];
            var row = AddRow(t.Title, t.Duration, "", t.Artist + (t.Album.Length > 0 ? "  ·  " + t.Album : ""), () => LocalMusic.PlayList(songs, index));
            row.DataContext = t.Path;
        }
        if (songs.Count > Shown)
        {
            var more = Ui.Text($"Showing {Shown} of {songs.Count} songs. Choose an album or artist to see the rest.", 14, "SubtleBrush");
            more.Margin = new Thickness(6, 10, 0, 0);
            _list.Children.Add(more);
        }
        Ui.GroupRows(_list);
        HighlightPlaying();
    }

    private Button AddRow(string label, string? value, string glyph, string? subtitle, Action click)
    {
        var row = Ui.Row(label, value, glyph, subtitle: subtitle);
        row.FontSize = 17;
        row.Click += (_, _) => click();
        _list.Children.Add(row);
        return row;
    }

    /// <summary>The song that's playing is marked in the list.</summary>
    private void HighlightPlaying()
    {
        string? playing = LocalMusic.Current?.Path;
        foreach (var b in _list.Children.OfType<Button>())
            b.Tag = b.DataContext is string path && path == playing ? "selected" : null;
    }

    /// <summary>The page's controller shortcuts.</summary>
    public bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.X: LocalMusic.TogglePlay(); return true;
            case NavAction.LT: LocalMusic.Previous(); return true;
            case NavAction.RT: LocalMusic.Next(); return true;
            case NavAction.Y: _ = ChooseFolderAsync(); return true;
        }
        return false;
    }
}

/// <summary>
/// Spotify, inside the launcher: what's playing with full controls, the device it plays on, and your playlists,
/// Liked Songs, recently played and search. Music plays on a Spotify device (this PC's Spotify app, a phone, a
/// speaker); controlling it from here needs Spotify Premium, otherwise songs open in the Spotify app.
/// </summary>
public sealed class SpotifyView : Grid
{
    private readonly Screen _page;
    private readonly SpotifyClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _title = Ui.Text("Nothing playing", 26, "FgBrush", bold: true);
    private readonly TextBlock _artist = Ui.Text("", 17, "SubtleBrush");
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 16, 0, 4), BorderThickness = new Thickness(0) };
    private readonly TextBlock _time = Ui.Text("", 14, "SubtleBrush"), _length = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _device = Ui.Text("", 15, "SubtleBrush");
    private readonly Button _play, _shuffle, _repeat;
    private readonly StackPanel _list = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Grid _setup = new();
    private readonly Grid _player = new();
    private SpotifyState? _state;
    private string _view = "playlists";
    private string _artUrl = "";
    private (string Uri, string Name)? _openList;
    private string? _lastSearch;

    public SpotifyView(Screen page)
    {
        _page = page;
        _client = new SpotifyClient(page.Host.Settings);
        Children.Add(_setup);
        Children.Add(_player);

        _player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        _player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
        // Cover and title side by side, so the player controls below still fit on the page.
        left.Children.Add(MusicUi.NowPlayingHeader(_art, _title, _artist));
        _progress.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _progress.SetResourceReference(Control.BackgroundProperty, "TileBrush");
        left.Children.Add(_progress);
        var times = new DockPanel();
        DockPanel.SetDock(_length, Dock.Right);
        times.Children.Add(_length);
        times.Children.Add(_time);
        left.Children.Add(times);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 12, 0, 0) };
        _shuffle = MusicUi.Control("", "Shuffle", () => _ = Do(c => c.ShuffleAsync(!(_state?.Shuffle ?? false), _cts.Token)), 46);
        controls.Children.Add(_shuffle);
        controls.Children.Add(MusicUi.Control("", "Previous", () => _ = Do(c => c.PreviousAsync(_cts.Token))));
        _play = MusicUi.Control("", "Play or pause", () => _ = TogglePlayAsync(), 66, primary: true);
        controls.Children.Add(_play);
        controls.Children.Add(MusicUi.Control("", "Next", () => _ = Do(c => c.NextAsync(_cts.Token))));
        _repeat = MusicUi.Control("", "Repeat", () => _ = Do(c => c.RepeatAsync((_state?.Repeat ?? "off") switch { "off" => "context", "context" => "track", _ => "off" }, _cts.Token)), 46);
        controls.Children.Add(_repeat);
        left.Children.Add(controls);
        var seekRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 6, 0, 0) };
        seekRow.Children.Add(MusicUi.Control("", "Back 15 seconds", () => _ = Do(c => c.SeekAsync((_state?.ProgressMs ?? 0) - 15000, _cts.Token)), 40));
        seekRow.Children.Add(MusicUi.Control("", "Forward 15 seconds", () => _ = Do(c => c.SeekAsync((_state?.ProgressMs ?? 0) + 15000, _cts.Token)), 40));
        seekRow.Children.Add(MusicUi.Control("", "Volume down", () => _ = Do(c => c.VolumeAsync(Math.Max(0, (_state?.Volume ?? 50) - 10), _cts.Token)), 40));
        seekRow.Children.Add(MusicUi.Control("", "Volume up", () => _ = Do(c => c.VolumeAsync(Math.Min(100, (_state?.Volume ?? 50) + 10), _cts.Token)), 40));
        left.Children.Add(seekRow);
        var device = Ui.Row("Playing on", null, "");
        device.Margin = new Thickness(0, 14, 0, 0);
        device.Click += (_, _) => _ = ChooseDeviceAsync();
        ((Grid)device.Content).Children.Add(_device);
        Grid.SetColumn(_device, 2);
        _device.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(device);
        _player.Children.Add(MusicUi.Column(left));

        var right = new DockPanel();
        DockPanel.SetDock(_chips, Dock.Top);
        right.Children.Add(_chips);
        right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _list });
        Grid.SetColumn(right, 1);
        _player.Children.Add(right);

        _poll.Tick += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => { Show(); };
        Unloaded += (_, _) => _poll.Stop();
    }

    public UIElement? FirstFocus => _player.Visibility == Visibility.Visible ? _play : _setup.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<Button>()).FirstOrDefault();

    /// <summary>Set up and signed in: the player. Otherwise the steps to get there.</summary>
    private void Show()
    {
        bool ready = _client.HasClientId && _client.IsSignedIn && !_client.NeedsNewSignIn;
        _player.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _setup.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        if (!ready) { BuildSetup(); _poll.Stop(); return; }
        _poll.Start();
        _ = RefreshAsync();
        _ = LoadListAsync();
    }

    private void BuildSetup()
    {
        _setup.Children.Clear();
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var heading = Ui.Text("Spotify in SquareDeck", 28, "FgBrush", bold: true);
        panel.Children.Add(heading);
        var about = Ui.Text(
            "See what's playing and control it, choose the device it plays on, and play your playlists, Liked Songs and anything you search for. " +
            "Music plays through Spotify (the Spotify app on this PC, your phone or a speaker). Controlling playback from here needs Spotify Premium.",
            16, "SubtleBrush");
        about.TextWrapping = TextWrapping.Wrap;
        about.Margin = new Thickness(0, 8, 0, 18);
        panel.Children.Add(about);
        var rows = new StackPanel();
        void Step(string label, string glyph, string? sub, Action click)
        {
            var r = Ui.Row(label, null, glyph, subtitle: sub);
            r.Click += (_, _) => click();
            rows.Children.Add(r);
        }
        var host = _page.Host;
        if (!_client.HasClientId)
        {
            Step("1.  Create a free Spotify app", "",
                "Opens Spotify's developer dashboard. Create an app, choose Web API, and add the redirect URI " + SpotifyClient.RedirectUri,
                () => host.Push(BrowserScreen.Open(host, "https://developer.spotify.com/dashboard")));
            Step("2.  Paste its Client ID", "", "Copied from the app's settings page", () => _ = EnterClientIdAsync());
        }
        else
        {
            Step(_client.NeedsNewSignIn ? "Sign in again (the player needs a few more permissions)" : "Sign in to Spotify", "",
                "Finish signing in in the browser window that opens", () => _ = SignInAsync());
            Step("Change the Client ID", "", null, () => _ = EnterClientIdAsync());
        }
        Step("Open the Spotify app instead", "", null, () => ProcessUtil.Open("spotify:"));
        Ui.GroupRows(rows);
        panel.Children.Add(rows);
        _setup.Children.Add(panel);
    }

    private async Task EnterClientIdAsync()
    {
        var host = _page.Host;
        var id = await host.PromptAsync("Spotify Client ID", "From your app at developer.spotify.com (32 letters and numbers).", host.Settings.SpotifyClientId);
        if (id == null) return;
        host.Settings.SpotifyClientId = id.Trim();
        host.Settings.Save();
        Show();
        _page.Host.FocusSoon(FirstFocus);
    }

    private async Task SignInAsync()
    {
        try
        {
            await _page.Host.RunBusyAsync("Finish signing in to Spotify in your browser…", () => _client.SignInAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            await _page.Host.AskAsync("Couldn't sign in to Spotify", ex.Message, "OK");
        }
        Show();
        _page.Host.FocusSoon(FirstFocus);
    }

    // ---------------------------------------------------------------- playback

    private async Task RefreshAsync()
    {
        try
        {
            _state = await _client.StateAsync(_cts.Token);
        }
        catch (Exception) { return; }
        var st = _state;
        _title.Text = st?.Title.Length > 0 ? st.Title : "Nothing playing";
        _artist.Text = st == null ? "Play something from your playlists, or choose a device below." : st.Artist;
        MusicUi.SetGlyph(_play, st?.IsPlaying == true ? "" : "");
        MusicUi.SetGlyph(_shuffle, "", st?.Shuffle == true);
        MusicUi.SetGlyph(_repeat, st?.Repeat == "track" ? "" : "", st != null && st.Repeat != "off");
        _device.Text = st?.Device.Length > 0 ? st.Device + (st.Volume >= 0 ? $"  ·  {st.Volume}%" : "") : "No device";
        _progress.Value = st is { DurationMs: > 0 } ? (double)st.ProgressMs / st.DurationMs : 0;
        _time.Text = st == null ? "" : MusicUi.Clock(TimeSpan.FromMilliseconds(st.ProgressMs));
        _length.Text = st is { DurationMs: > 0 } ? MusicUi.Clock(TimeSpan.FromMilliseconds(st.DurationMs)) : "";
        string url = st?.ImageUrl ?? "";
        if (url != _artUrl)
        {
            _artUrl = url;
            _art.Source = url.Length > 0 ? await ImageLoader.FromUrlAsync(url, 600, _cts.Token) : null;
            _page.SetBackdrop(_art.Source);
        }
    }

    private async Task TogglePlayAsync()
    {
        if (_state?.IsPlaying == true) await Do(c => c.PauseAsync(_cts.Token));
        else await Do(c => c.ResumeAsync(_cts.Token));
    }

    /// <summary>Runs a playback command and explains when it can't work (no device open, or no Premium).</summary>
    private async Task Do(Func<SpotifyClient, Task<string?>> command, string? openUri = null)
    {
        string? problem;
        try { problem = await command(_client); }
        catch (Exception ex) { problem = ex.Message; }
        if (problem == null)
        {
            await Task.Delay(350);
            await RefreshAsync();
            return;
        }
        var host = _page.Host;
        switch (problem)
        {
            case "nodevice":
                int choice = await host.AskWithIconsAsync("Nothing to play on",
                    "Spotify isn't open on any device. Open it on this PC (it starts in the background), or choose another device.",
                    ("Open Spotify on this PC", ""), ("Choose a device", ""));
                if (choice == 0) await StartLocalSpotifyAsync(openUri);
                else if (choice == 1) await ChooseDeviceAsync();
                break;
            case "premium":
                if (openUri != null) { ProcessUtil.Open(openUri); break; }
                await host.AskAsync("Spotify Premium needed", "Spotify only lets other apps control playback for Premium accounts. You can still open songs in the Spotify app.", "OK");
                break;
            case "signin":
                Show();
                break;
            default:
                host.ShowToast("Spotify", "Couldn't do that", problem, "", "info");
                break;
        }
    }

    /// <summary>Starts this PC's Spotify app and moves playback to it once it shows up.</summary>
    private async Task StartLocalSpotifyAsync(string? uri)
    {
        ProcessUtil.Open(uri ?? "spotify:");
        var host = _page.Host;
        await host.RunBusyAsync("Opening Spotify…", async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(750);
                var devices = await _client.DevicesAsync(_cts.Token);
                var pc = devices.FirstOrDefault(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase) && d.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                         ?? devices.FirstOrDefault(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase));
                if (pc != null) { await _client.TransferAsync(pc.Id, _cts.Token); return; }
            }
        });
        WindowActivator.BringToFront(host); // Spotify's window came forward; back to the launcher
        await RefreshAsync();
    }

    private async Task ChooseDeviceAsync()
    {
        var host = _page.Host;
        List<SpotifyDevice> devices;
        try { devices = await _client.DevicesAsync(_cts.Token); }
        catch (Exception) { devices = new(); }
        var options = devices.Select(d => (d.Name + (d.Active ? "  (playing)" : ""), d.Type switch
        {
            "Smartphone" => "", "Speaker" => "", "TV" => "", _ => ""
        })).Append(("Open Spotify on this PC", "")).ToArray();
        int choice = await host.AskWithIconsAsync("Play on", devices.Count == 0 ? "No Spotify devices are open right now." : null, options);
        if (choice < 0) return;
        if (choice == devices.Count) { await StartLocalSpotifyAsync(null); return; }
        await Do(c => c.TransferAsync(devices[choice].Id, _cts.Token));
    }

    // ---------------------------------------------------------------- browsing

    private void BuildChips()
    {
        _chips.Children.Clear();
        foreach (var (id, name) in new[] { ("playlists", "Playlists"), ("liked", "Liked songs"), ("recent", "Recently played"), ("search", "Search") })
        {
            string v = id;
            _chips.Children.Add(MusicUi.Chip(name, _view == id && _openList == null, () =>
            {
                _view = v;
                _openList = null;
                if (v == "search") _ = SearchAsync(); else _ = LoadListAsync();
            }));
        }
        if (_openList is { } open) _chips.Children.Add(MusicUi.Chip("‹  " + open.Name, true, () => { _openList = null; _ = LoadListAsync(); }));
    }

    private async Task LoadListAsync(List<SpotifyItem>? given = null)
    {
        BuildChips();
        _list.Children.Clear();
        var loading = Ui.Text("Loading…", 16, "SubtleBrush");
        loading.Margin = new Thickness(6, 14, 0, 0);
        _list.Children.Add(loading);
        List<SpotifyItem> items;
        try
        {
            items = given ?? (_openList is { } open ? await _client.TracksOfAsync(open.Uri, _cts.Token)
                : _view switch
                {
                    "liked" => await _client.LikedAsync(_cts.Token),
                    "recent" => await _client.RecentAsync(_cts.Token),
                    "search" => new List<SpotifyItem>(),
                    _ => await _client.MyPlaylistsAsync(_cts.Token)
                });
        }
        catch (InvalidOperationException ex) when (ex.Message == "scope")
        {
            _page.Host.Settings.SpotifyScopes = 0; // needs the newer permissions
            _page.Host.Settings.Save();
            Show();
            return;
        }
        catch (Exception ex)
        {
            loading.Text = "Couldn't load from Spotify: " + ex.Message;
            return;
        }
        _list.Children.Clear();
        if (items.Count == 0)
        {
            loading.Text = _view == "search" ? "Press X (or choose Search) to search Spotify." : "Nothing here yet.";
            _list.Children.Add(loading);
            return;
        }
        // Songs of one list play from that song onward; a playlist or album row opens its songs.
        var trackUris = items.Where(i => i.Kind == "track").Select(i => i.Uri).ToList();
        foreach (var item in items)
        {
            var it = item;
            string glyph = it.Kind switch { "playlist" => "", "album" => "", "artist" => "", _ => "" };
            var row = Ui.Row(it.Name, it.Kind == "track" ? null : "Open", glyph, subtitle: it.Detail);
            row.FontSize = 17;
            row.Click += (_, _) => _ = ChooseAsync(it, trackUris);
            _list.Children.Add(row);
            if (it.ImageUrl.Length > 0) _ = SetRowImageAsync(row, it.ImageUrl);
        }
        Ui.GroupRows(_list);
    }

    private async Task SetRowImageAsync(Button row, string url)
    {
        var image = await ImageLoader.FromUrlAsync(url, 96, _cts.Token);
        if (image == null || row.Content is not Grid grid) return;
        var badge = grid.Children.OfType<Grid>().FirstOrDefault();
        if (badge == null) return;
        badge.Children.Clear();
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill } });
    }

    private async Task ChooseAsync(SpotifyItem item, List<string> trackUris)
    {
        if (item.Kind is "playlist" or "album")
        {
            int choice = await _page.Host.AskWithIconsAsync(item.Name, item.Detail, ("Play", ""), ("Show songs", ""));
            if (choice == 0) await Do(c => c.PlayAsync(item, _cts.Token), item.Uri);
            else if (choice == 1) { _openList = (item.Uri, item.Name); await LoadListAsync(); }
            return;
        }
        if (item.Kind == "artist") { await Do(c => c.PlayAsync(item, _cts.Token), item.Uri); return; }
        // A song: from this song on, in the list it's in.
        if (_openList is { } open) await Do(c => c.PlayFromAsync(open.Uri, item.Uri, _cts.Token), item.Uri);
        else
        {
            int at = Math.Max(0, trackUris.IndexOf(item.Uri));
            await Do(c => c.PlayTracksAsync(trackUris.Skip(at), _cts.Token), item.Uri);
        }
    }

    private async Task SearchAsync()
    {
        _view = "search";
        _openList = null;
        var text = await _page.Host.PromptAsync("Search Spotify", "A song, artist, album or playlist.", _lastSearch ?? "", "Search");
        if (string.IsNullOrWhiteSpace(text)) { await LoadListAsync(new List<SpotifyItem>()); return; }
        _lastSearch = text;
        List<SpotifyItem> results;
        try { results = await _client.SearchAsync(text, _cts.Token); }
        catch (Exception ex) { _page.Host.ShowToast("Spotify", "Search didn't work", ex.Message, "", "info"); return; }
        await LoadListAsync(results);
    }

    public bool OnAction(NavAction action)
    {
        if (_player.Visibility != Visibility.Visible) return false;
        switch (action)
        {
            case NavAction.X: _ = SearchAsync(); return true;
            case NavAction.Y: _ = TogglePlayAsync(); return true;
            case NavAction.LT: _ = Do(c => c.PreviousAsync(_cts.Token)); return true;
            case NavAction.RT: _ = Do(c => c.NextAsync(_cts.Token)); return true;
        }
        return false;
    }

    public void Stop()
    {
        _poll.Stop();
        _cts.Cancel();
    }
}
