using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Pick a game's tall grid (poster styles), hero (background artwork) or logo from SteamGridDB, or any file.
/// The pick is kept with the user's own images and used everywhere that kind of art appears.
/// </summary>
public sealed class ArtPickerScreen : GridScreen
{
    private readonly Game _item;
    private readonly string _kind;
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _note;
    private readonly double _w, _h;
    private bool _busy;

    private string KindName => _kind switch { "heroes" => "hero image", "logos" => "logo", _ => "tall grid" };

    public ArtPickerScreen(Game item, string kind)
        : base((kind switch { "heroes" => "Choose hero", "logos" => "Choose logo", _ => "Choose tall grid" }) + " – " + item.Name)
    {
        _item = item;
        _kind = kind;
        (_w, _h) = kind switch { "heroes" => (372.0, 120.0), "logos" => (276.0, 138.0), _ => (148.0, 222.0) };
        _note = Ui.Text("Loading images from SteamGridDB…", 17, "SubtleBrush");
        _note.Margin = new Thickness(4, 8, 0, 0);
        Body.Children.Add(_note);

        bool hasPick = SteamGridDbClient.FindChosenArt(item, kind) != null;
        AddTile(GlyphBox(_w, _h, ""), "Choose a file…", _w, Browse);
        AddTile(GlyphBox(_w, _h, ""), hasPick ? "Use the default" : "Default (in use)", _w, () => Finish(null));
    }

    public override void OnShown() => _ = LoadAsync();

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
            var images = await Task.Run(() => client.ListArtAsync(_item, _kind, ct), ct);
            if (ct.IsCancellationRequested) return;
            _note.Text = images.Count == 0 ? $"SteamGridDB has no {KindName}s for this title. Choose a file instead." : "";
            using var gate = new SemaphoreSlim(6);
            var loads = new List<Task>();
            foreach (var image in images.Take(60))
            {
                var img = image;
                var box = ImageBox(_w, _h, out var view);
                if (_kind == "logos")
                {
                    // Logos are see-through: shown whole, on a dark backing so light ones are readable.
                    view.Stretch = Stretch.Uniform;
                    view.Margin = new Thickness(14);
                    box.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x24));
                }
                AddTile(box, null, _w, () => Pick(img.Url, img.Thumb));
                loads.Add(LoadThumbAsync(view, img.Thumb, _kind == "heroes" ? 480 : 300, gate, ct));
            }
            await Task.WhenAll(loads);
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            _note.Text = "SteamGridDB rejected the API key. Check it in Settings.";
        }
        catch (Exception)
        {
            _note.Text = "Couldn't reach SteamGridDB.";
        }
    }

    private static async Task LoadThumbAsync(Image view, string url, int width, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, width, ct), ct);
                if (!ct.IsCancellationRequested) view.Source = source;
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async void Pick(string url, string thumb)
    {
        if (_busy) return;
        _busy = true;
        _note.Text = "Downloading…";
        string? path = null;
        try
        {
            var key = Host.Settings.SteamGridDbApiKey;
            var dest = SteamGridDbClient.ChosenArtBase(_item, _kind) + "_new";
            path = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                // The full image first (with retries), then the preview if the full one never works.
                foreach (var address in new[] { url, thumb }.Distinct())
                {
                    if (string.IsNullOrEmpty(address)) continue;
                    var file = await client.DownloadWithRetryAsync(address, null, dest, CancellationToken.None);
                    if (file == null) continue;
                    if (ImageLoader.FromFile(file, 64) != null) return file;
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                return null;
            });
        }
        catch (Exception)
        {
            // Reported below.
        }

        if (path != null && Save(path)) { Finish(path); return; }
        _busy = false;
        _note.Text = "That image couldn't be downloaded after several tries. Check the internet connection, or pick another one.";
    }

    /// <summary>Puts a downloaded or chosen file in place as this game's pick (replacing an earlier one).</summary>
    private bool Save(string file)
    {
        try
        {
            var ext = Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            SteamGridDbClient.ClearChosenArt(_item, _kind);
            Directory.CreateDirectory(SteamGridDbClient.CustomDir);
            File.Move(file, SteamGridDbClient.ChosenArtBase(_item, _kind) + ext, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }

    private async void Browse()
    {
        var picked = await Host.PickFileAsync($"Choose an image for the {KindName}", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            if (ImageLoader.FromFile(picked, 64) == null) throw new IOException("Windows can't open that picture.");
            Directory.CreateDirectory(SteamGridDbClient.CustomDir);
            var copy = SteamGridDbClient.ChosenArtBase(_item, _kind) + "_new" + Path.GetExtension(picked);
            File.Copy(picked, copy, overwrite: true);
            if (Save(copy)) Finish(copy);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }

    private void Finish(string? picked)
    {
        if (picked == null) SteamGridDbClient.ClearChosenArt(_item, _kind);
        Host.RefreshArt(_item, _kind);
        Host.Pop(this);
    }
}

/// <summary>Pick a different square image for a tile: from SteamGridDB, or any file.</summary>
public sealed class CoverPickerScreen : GridScreen
{
    private const double TileSize = 196;
    private readonly Game _item;
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _note;
    private readonly Action<string?>? _picked;
    private bool _busy;

    /// <param name="picked">If given, receives the chosen file instead of it being saved for the tile.</param>
    public CoverPickerScreen(Game item, Action<string?>? picked = null) : base("Choose image – " + item.Name)
    {
        _item = item;
        _picked = picked;
        _note = Ui.Text("Loading images from SteamGridDB…", 17, "SubtleBrush");
        _note.Margin = new Thickness(4, 8, 0, 0);
        Body.Children.Add(_note);

        AddTile(GlyphBox(TileSize, TileSize, ""), "Choose a file…", TileSize, Browse);
        AddTile(GlyphBox(TileSize, TileSize, ""), "Use the default", TileSize, () => Finish(null));

        // The other kinds of SteamGridDB art for a game: tall grids (poster styles), heroes (backgrounds) and logos.
        if (picked == null && item.IsGame)
        {
            AddTile(GlyphBox(TileSize, TileSize, "\uE8A9"), "Tall grids", TileSize, () => Host.Push(new ArtPickerScreen(item, "grids")));
            AddTile(GlyphBox(TileSize, TileSize, "\uEB9F"), "Hero images", TileSize, () => Host.Push(new ArtPickerScreen(item, "heroes")));
            AddTile(GlyphBox(TileSize, TileSize, "\uE8D2"), "Logos", TileSize, () => Host.Push(new ArtPickerScreen(item, "logos")));
        }
    }

    public override void OnShown() => _ = LoadAsync();

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
            var images = await Task.Run(() => client.ListSquareAsync(_item, ct), ct);
            if (ct.IsCancellationRequested) return;

            _note.Text = images.Count == 0 ? "SteamGridDB has no square images for this title, so here are its icons instead." : "";
            if (images.Count == 0) { await LoadIconsAsync(); return; }
            AddTile(GlyphBox(TileSize, TileSize, "\uE8B9"), "Search icons instead", TileSize, () => _ = LoadIconsAsync());
            using var gate = new SemaphoreSlim(6);
            var loads = new List<Task>();
            foreach (var image in images.Take(60))
            {
                var img = image;
                var box = ImageBox(TileSize, TileSize, out var view);
                AddTile(box, null, TileSize, () => PickRemote(img.Url, img.Thumb));
                loads.Add(LoadThumbAsync(view, img.Thumb, gate, ct));
            }
            await Task.WhenAll(loads);
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            _note.Text = "SteamGridDB rejected the API key. Check it in Settings.";
        }
        catch (Exception)
        {
            _note.Text = "Couldn't reach SteamGridDB.";
        }
    }

    private bool _iconsShown;

    /// <summary>SteamGridDB's icons for the game; a chosen one is shown centered on a color wash, like other icon tiles.</summary>
    private async Task LoadIconsAsync()
    {
        if (_iconsShown) return;
        _iconsShown = true;
        var ct = _cts.Token;
        try
        {
            using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
            var icons = await Task.Run(() => client.ListIconsAsync(_item, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (icons.Count == 0)
            {
                _note.Text = "SteamGridDB has no square images or icons for this title. Choose a file instead.";
                return;
            }
            var header = Ui.Text("Icons", 20, "FgBrush", bold: true);
            header.Margin = new Thickness(4, 18, 0, 8);
            var panel = new WrapPanel();
            Body.Children.Add(header);
            Body.Children.Add(panel);
            using var gate = new SemaphoreSlim(6);
            var loads = new List<Task>();
            Button? first = null;
            foreach (var icon in icons.Take(60))
            {
                var ic = icon;
                var box = ImageBox(TileSize, TileSize, out var view);
                view.Stretch = Stretch.Uniform;
                view.Margin = new Thickness(TileSize * 0.2);
                var tile = AddTile(box, null, TileSize, () => PickRemote(ic.Url, ic.Thumb, asIcon: true), null, panel);
                first ??= tile;
                loads.Add(LoadThumbAsync(view, ic.Thumb, gate, ct));
            }
            FocusLater(first);
            await Task.WhenAll(loads);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _note.Text = "Couldn't get icons from SteamGridDB.";
        }
    }

    private static async Task LoadThumbAsync(Image view, string url, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 256, ct), ct);
                if (!ct.IsCancellationRequested) view.Source = source;
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static string SafeKey(Game item) =>
        string.Concat(item.Key.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + "_" + DateTime.Now.Ticks;

    private async void PickRemote(string url, string thumb, bool asIcon = false)
    {
        if (_busy) return;
        _busy = true;
        _note.Text = "Downloading\u2026";
        string? path = null;
        try
        {
            var key = Host.Settings.SteamGridDbApiKey;
            var dest = Path.Combine(SteamGridDbClient.CustomDir, (asIcon ? SteamGridDbClient.IconFilePrefix : "") + SafeKey(_item));
            path = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                // The full image first (with retries); if it never arrives or Windows can't open it,
                // the preview that is already showing in the tile is used instead.
                foreach (var address in new[] { url, thumb }.Distinct())
                {
                    if (string.IsNullOrEmpty(address)) continue;
                    var file = await client.DownloadWithRetryAsync(address, null, dest, CancellationToken.None);
                    if (file == null) continue;
                    if (ImageLoader.FromFile(file, 64) != null) return file;
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                return null;
            });
        }
        catch (Exception)
        {
            // Reported below.
        }

        if (path != null) { Finish(path); return; }
        _busy = false;
        _note.Text = "That image couldn't be downloaded after several tries. Check the internet connection, or pick another one.";
    }

    private async void Browse()
    {
        var picked = await Host.PickFileAsync("Choose an image (square works best)", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            Directory.CreateDirectory(SteamGridDbClient.CustomDir);
            var dest = Path.Combine(SteamGridDbClient.CustomDir, SafeKey(_item) + Path.GetExtension(picked));
            File.Copy(picked, dest, overwrite: true);
            Finish(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }

    private void Finish(string? customPath)
    {
        if (_picked != null) _picked(customPath);
        else Host.SetCustomCover(_item, customPath);
        Host.Pop(this);
    }
}

/// <summary>
/// Everything about one game: store info, screenshots, trailer, play time, achievements. Games from other stores and
/// ones added by hand get the same page: their details come from the same game's Steam store page when it's sold
/// there, or from Wikipedia, and their artwork and release date from SteamGridDB.
/// </summary>
public sealed class DetailsScreen : Screen
{
    private const int MaxAchievementCards = 150;
    private const double HeroHeight = 380;
    private const double CardRadius = 12;

    private readonly Game _game;
    private readonly CancellationTokenSource _cts = new();

    // The artwork banner behind the top of the page, which fades as the page scrolls.
    private readonly Grid _hero = new() { Height = HeroHeight, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
    private readonly Image _heroImage = new() { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
    private readonly TranslateTransform _heroShift = new();

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 14, 24) };
    private readonly Image _cover = new() { Stretch = Stretch.UniformToFill };
    private readonly Border _coverBox;
    private readonly ContentControl _titleHost = new() { Focusable = false };
    // The game's logo artwork sits on the right of the banner; the title itself is always the game's name.
    private readonly ContentControl _logoHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 0, 6) };
    private readonly TextBlock _meta = Ui.Text("", 17, "SubtleBrush");
    private readonly WrapPanel _badges = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel _actions = new() { Margin = new Thickness(0, 18, 0, 0) };
    private readonly TextBlock _actionLabel = Ui.Text("", 14, "SubtleBrush");
    private readonly WrapPanel _stats = new() { Margin = new Thickness(0, 26, 0, 0) };
    private readonly StackPanel _notes = new();
    private readonly TextBlock _description = Ui.Text("Loading details…", 18, "FgBrush");
    private readonly StackPanel _facts = new();
    private readonly StackPanel _sections = new();
    private string? _reviews;
    private StoreDetails? _details;
    private (int Unlocked, int Total)? _progress;
    private string? _focusId;

    // Where the store details come from: the game's own app id for Steam games, the matching Steam app for others
    // (0 when there's none), and Wikipedia's summary when that's all there is.
    private int _storeId;
    private GameLookup.WikiSummary? _wiki;
    private DateTime? _sgdbReleased;
    private long _folderSize;
    private readonly TextBlock _source = Ui.Text("", 13, "SubtleBrush");

    public DetailsScreen(Game game)
    {
        _game = game;

        // ---- banner: cover, title or logo, facts, actions
        _coverBox = new Border
        {
            Width = 196, Height = 196, CornerRadius = new CornerRadius(CardRadius), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 }
        };
        _coverBox.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var coverGrid = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 196, 196), CardRadius, CardRadius) };
        coverGrid.Children.Add(_cover);
        _coverBox.Child = coverGrid;
        ShowCover();
        System.ComponentModel.PropertyChangedEventManager.AddHandler(game, (_, _) => ShowCover(), nameof(Game.Cover));

        var title = Ui.Text(game.Name, 42, "FgBrush", bold: true);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.None;
        _titleHost.Content = title;

        _meta.TextWrapping = TextWrapping.Wrap;
        _meta.Margin = new Thickness(0, 8, 0, 0);
        _actionLabel.Margin = new Thickness(6, 6, 0, 0);

        var info = new StackPanel { Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
        info.Children.Add(_titleHost);
        info.Children.Add(_meta);
        info.Children.Add(_badges);

        var banner = new DockPanel { Margin = new Thickness(0, HeroHeight - 300, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_coverBox, Dock.Left);
        banner.Children.Add(_coverBox);
        DockPanel.SetDock(_logoHost, Dock.Right);
        banner.Children.Add(_logoHost);
        banner.Children.Add(info);
        _page.Children.Add(banner);
        _page.Children.Add(_actions);
        _page.Children.Add(_actionLabel);
        _page.Children.Add(_stats);
        _page.Children.Add(_notes);
        AddScreenshots();

        // ---- about: description beside a column of facts
        _description.TextWrapping = TextWrapping.Wrap;
        _description.TextTrimming = TextTrimming.None;
        _description.LineHeight = 27;
        var about = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        about.Children.Add(Card(_description, new Thickness(24, 20, 24, 22)));
        var factsCard = Card(_facts, new Thickness(22, 18, 22, 18));
        factsCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(factsCard, 2);
        about.Children.Add(factsCard);
        _page.Children.Add(SectionTitle("About"));
        _page.Children.Add(about);
        _source.Margin = new Thickness(4, 8, 0, 0);
        _source.TextWrapping = TextWrapping.Wrap;
        _page.Children.Add(_source);
        _page.Children.Add(_sections);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = _page,
            Margin = new Thickness(0, -60, 0, 0) // the content starts under the page title, over the artwork
        };
        // The artwork drifts up and fades as the page scrolls.
        _scroll.ScrollChanged += (_, e) =>
        {
            double offset = e.VerticalOffset;
            _heroShift.Y = -offset * 0.4;
            _hero.Opacity = _heroImage.Source == null ? 0 : Math.Max(0, 1 - offset / (HeroHeight * 0.9));
        };

        SetPage("", _scroll, "{Select}  Choose      {Details}  Collections      {Options}  Rating and notes      {Back}  Back", wide: true);
        BuildHero();
    }

    /// <summary>Your screenshots of this game (Game Bar and Steam), newest first; select one to see it full screen.</summary>
    private void AddScreenshots()
    {
        var shots = GameCaptures.For(_game, 24);
        if (shots.Count == 0) return;
        _page.Children.Add(SectionTitle("Your screenshots"));
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 0, 0, 6) };
        for (int i = 0; i < shots.Count; i++)
        {
            int index = i;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var face = new Border { Width = 256, Height = 144, CornerRadius = new CornerRadius(CardRadius), ClipToBounds = true, Child = image };
            face.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            face.Clip = new RectangleGeometry(new Rect(0, 0, 256, 144), CardRadius, CardRadius);
            var tile = Ui.Tile(face);
            tile.ToolTip = System.IO.Path.GetFileName(shots[i]);
            tile.Click += (_, _) => Host.Push(new ViewerScreen(shots, index));
            strip.Children.Add(tile);
            string path = shots[i];
            _ = Task.Run(() => ImageLoader.FromFile(path, 320)).ContinueWith(t =>
            {
                if (t.Result != null) image.Source = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        _page.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly, Focusable = false, Content = strip
        });
    }

    // ---------------------------------------------------------------- building blocks

    private void ShowCover() => _cover.Source = _game.Cover ?? _cover.Source;

    private static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(CardRadius), Padding = padding ?? new Thickness(20), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        return card;
    }

    private static TextBlock SectionTitle(string text)
    {
        var t = Ui.Text(text, 22, "FgBrush", bold: true);
        t.Margin = new Thickness(2, 30, 0, 12);
        return t;
    }

    private static TextBlock Line(string text, double size, string brush = "SubtleBrush", bool bold = false, bool wrap = false)
    {
        var t = Ui.Text(text, size, brush, bold);
        if (wrap) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }
        else t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static Border Pill(string text, string background, string foreground = "FgBrush")
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 3, 11, 4), Margin = new Thickness(0, 0, 8, 6),
            Child = Ui.Text(text, 14, foreground, bold: true)
        };
        pill.SetResourceReference(Border.BackgroundProperty, background);
        return pill;
    }

    private static FrameworkElement Bar(double fraction, double height = 6)
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

    /// <summary>A focusable card; remembers itself so a refresh keeps the highlight where it was.</summary>
    private Button CardButton(UIElement content, string id, Action click)
    {
        var button = Ui.Tile(content);
        button.Margin = new Thickness(0, 0, 10, 10);
        button.DataContext = id;
        button.Click += (_, _) => click();
        button.GotKeyboardFocus += (_, _) => _focusId = id;
        return button;
    }

    private static ScrollViewer Strip(UIElement content) => new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.HorizontalOnly,
        Focusable = false,
        Content = content
    };

    /// <summary>The artwork layer: the picture, faded into the page at the bottom and darkened on the left.</summary>
    private void BuildHero()
    {
        var bg = (Application.Current.Resources["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
        RenderOptions.SetBitmapScalingMode(_heroImage, BitmapScalingMode.HighQuality);
        _heroImage.RenderTransform = _heroShift;
        _hero.Children.Add(_heroImage);

        var down = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        down.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, bg.R, bg.G, bg.B), 0));
        down.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, bg.R, bg.G, bg.B), 0.35));
        down.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, bg.R, bg.G, bg.B), 0.78));
        down.GradientStops.Add(new GradientStop(bg, 1));
        _hero.Children.Add(new Border { Background = down });

        var side = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        side.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, bg.R, bg.G, bg.B), 0));
        side.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, bg.R, bg.G, bg.B), 0.6));
        _hero.Children.Add(new Border { Background = side });
        SetPageHero(_hero);
    }

    private void ShowHero(ImageSource? picture)
    {
        if (picture == null || _heroImage.Source != null) return;
        _heroImage.Source = picture;
        double target = Math.Max(0, 1 - _scroll.VerticalOffset / (HeroHeight * 0.9));
        if (Theme.ReduceMotion) _hero.Opacity = target;
        else _hero.BeginAnimation(OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(500)) { FillBehavior = FillBehavior.Stop });
        _hero.Opacity = target;
    }

    // ---------------------------------------------------------------- lifecycle

    public override void OnShown()
    {
        BuildActions();
        BuildBadges();
        BuildStats();
        BuildNotes();
        BuildFacts();
        _ = TintFromCoverAsync();
        _ = LoadHeroAsync();
        _ = LoadAsync();
    }

    public override void OnResumed()
    {
        BuildActions(); // favorite, collections or running may have changed
        BuildBadges();
        BuildStats();
        BuildNotes(); // rating, status or notes may have just changed
    }

    public override void OnClosed() => _cts.Cancel();

    public override void FocusDefault() => _actions.Children.OfType<Button>().FirstOrDefault()?.Focus();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.X:
                _ = CollectionsAsync();
                return true;
            case NavAction.Y:
                Host.Push(new GameNotesScreen(_game));
                return true;
        }
        return false;
    }

    /// <summary>Colors the page background from the game's cover art (Settings can turn this off).</summary>
    private async Task TintFromCoverAsync()
    {
        if (!Host.Settings.DetailsGradient || _game.Cover is not System.Windows.Media.Imaging.BitmapSource cover) return;
        var color = await Task.Run(() => ImageLoader.DominantColor(cover));
        if (color is Color c && !_cts.IsCancellationRequested) SetPageTint(c);
    }

    private async Task LoadHeroAsync()
    {
        if (!_game.IsGame) return;
        var key = Host.Settings.SteamGridDbApiKey;
        var ct = _cts.Token;
        try
        {
            var picture = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var path = await client.GetHeroAsync(_game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 1920);
            }, ct);
            if (!ct.IsCancellationRequested) ShowHero(picture);
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------- actions

    private void BuildActions()
    {
        _actions.Children.Clear();
        string play = _game.IsRunning ? "Return to game" : _game.OnWishlist ? "View in store" : !_game.IsInstalled ? "Install" : "Play";
        string playGlyph = _game.IsRunning ? "" : _game.OnWishlist ? "" : !_game.IsInstalled ? "" : "";
        var main = new Button { Style = Ui.StyleOf("AccentButton"), Content = GlyphLabel(playGlyph, play, 20), Margin = new Thickness(0, 0, 14, 10) };
        main.Click += (_, _) => _ = Host.ActivateAsync(_game);
        Watch(main, "action:play", play);
        _actions.Children.Add(main);

        if (_game.IsRunning)
            Pill("", "Stop", "Stop game", async () =>
            {
                await Host.StopGameAsync(_game, confirm: true);
                BuildActions();
                BuildBadges();
            });
        Pill(_game.IsFavorite ? "" : "", _game.IsFavorite ? "Favorite" : "Favorite", _game.IsFavorite ? "Remove from favorites" : "Add to favorites", () =>
        {
            Host.ToggleFavorite(_game, this);
            BuildActions();
        }, highlight: _game.IsFavorite);
        int inCollections = Host.Library.Collections.Count(c => c.Games.Contains(_game.Key));
        Pill("", inCollections == 0 ? "Collections" : $"Collections ({inCollections})",
            inCollections == 0 ? "Add to a collection" : $"In {inCollections} {(inCollections == 1 ? "collection" : "collections")}", () => _ = CollectionsAsync());
        if (_details is { Trailers.Count: > 0 }) Pill("", "Trailer", "Watch the trailer", WatchTrailer);
        Pill("", "Rate", "Rating, status and notes", () => Host.Push(new GameNotesScreen(_game)));
        if (_storeId > 0) Pill("\uE719", "Store", _game.IsSteam ? "Open the Steam store page" : "Open its page on the Steam store", () => ProcessUtil.Open("steam://store/" + _storeId));
        if (_wiki?.Url is { Length: > 0 } wikiUrl) Pill("\uE774", "Wikipedia", "Read the full article", () => Host.Push(BrowserScreen.Open(Host, wikiUrl)));
        if (!_game.IsSteam && InstallFolder() is string dir)
            Pill("\uE838", "Files", "Open the install folder", () => Host.Push(new ExplorerScreen(dir)));
        Pill("", "Time to beat", "How long to beat", () =>
            ProcessUtil.Open("https://howlongtobeat.com/?q=" + Uri.EscapeDataString(_game.OriginalName)));
        if (_game.IsGame && !_game.OnWishlist) Pill("", "More", "More options", () => _ = Host.OpenOptionsAsync(_game));

        var target = _actions.Children.OfType<Button>().FirstOrDefault(b => _focusId != null && b.DataContext as string == _focusId)
                     ?? _actions.Children.OfType<Button>().First();
        FocusLater(target);
    }

    private static StackPanel GlyphLabel(string glyph, string label, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size * 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) };
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void Pill(string glyph, string label, string description, Action click, bool highlight = false)
    {
        var content = GlyphLabel(glyph, label, 16);
        var button = new Button { Style = Ui.StyleOf("PillButton"), Content = content, Margin = new Thickness(0, 0, 10, 10) };
        if (highlight) button.SetResourceReference(ForegroundProperty, "AccentBrush");
        button.Click += (_, _) => click();
        Watch(button, "action:" + label, description);
        _actions.Children.Add(button);
    }

    /// <summary>Under the action bar, says what the highlighted action does (like a console's button label).</summary>
    private void Watch(Button button, string id, string description)
    {
        button.DataContext = id;
        button.ToolTip = description;
        button.GotKeyboardFocus += (_, _) => { _focusId = id; _actionLabel.Text = description; };
        button.LostKeyboardFocus += (_, _) => { if (_actionLabel.Text == description) _actionLabel.Text = ""; };
    }

    private async Task CollectionsAsync()
    {
        await Host.ChooseCollectionsAsync(_game);
        BuildActions();
    }

    private void WatchTrailer()
    {
        // Steam serves some trailers only as adaptive streams, which can't be played here: open the store page then.
        var playable = _details?.Trailers.FirstOrDefault(t => t.Url != null);
        if (playable?.Url != null) Host.Push(new ViewerScreen(new Uri(playable.Url)));
        else ProcessUtil.Open($"steam://store/{_storeId}");
    }

    // ---------------------------------------------------------------- top facts

    private void BuildBadges()
    {
        _badges.Children.Clear();
        if (_game.IsRunning) _badges.Children.Add(Pill("RUNNING", "AccentBrush", "BgBrush"));
        if (_game.OnWishlist)
        {
            _badges.Children.Add(Pill("ON YOUR WISHLIST", "AccentBrush", "BgBrush"));
            if (_game.DiscountPercent > 0) _badges.Children.Add(Pill($"-{_game.DiscountPercent}%", "AccentBrush", "BgBrush"));
            if (_game.Price.Length > 0) _badges.Children.Add(Pill(_game.Price, "CircleBrush"));
        }
        else if (_game.IsGame)
        {
            _badges.Children.Add(Pill(_game.IsInstalled ? _game.SourceName : "Not installed", "CircleBrush"));
        }
        if (_game.HasUpdate) _badges.Children.Add(Pill("UPDATE", "AccentBrush", "BgBrush"));
        if (_details?.Metacritic is int score) _badges.Children.Add(Pill("Metacritic " + score, "CircleBrush"));
        if (_details?.Genres is { Length: > 0 } genres)
            foreach (var genre in genres.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(3))
                _badges.Children.Add(Pill(genre, "CircleBrush"));
    }

    private void BuildStats()
    {
        _stats.Children.Clear();
        void Stat(string glyph, string value, string label, double? fraction = null)
        {
            var stack = new StackPanel { Width = 170 };
            var icon = Ui.Glyph(glyph, 20, "AccentBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(icon);
            var big = Line(value, 24, "FgBrush", bold: true);
            big.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(big);
            stack.Children.Add(Line(label, 14));
            if (fraction is double f)
            {
                var bar = Bar(f);
                bar.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(bar);
            }
            var card = Card(stack, new Thickness(18, 16, 18, 16));
            card.Margin = new Thickness(0, 0, 12, 12);
            _stats.Children.Add(card);
        }

        double minutes = _game.IsSteam ? SteamLibrary.GetPlaytimeMinutes(_game.AppId) ?? 0 : Host.Playtime.TotalSeconds(_game.Key) / 60;
        if (minutes >= 1) Stat("", minutes < 60 ? $"{minutes:0} min" : $"{minutes / 60:0.#} h", "Time played");
        var last = _game.LastPlayed ?? (_game.RecentAt.Year > 2000 ? _game.RecentAt : null);
        if (last is DateTime played) Stat("", Ui.Ago(played), "Last played");
        if (_progress is { Total: > 0 } p)
            Stat("", $"{p.Unlocked} / {p.Total}", $"Achievements  ·  {100.0 * p.Unlocked / p.Total:0}%", (double)p.Unlocked / p.Total);
        else if (_details is { AchievementTotal: > 0 })
            Stat("", _details.AchievementTotal.ToString(), "Achievements");
        if (_reviews != null) Stat("", _reviews.Split(',')[0], "Steam reviews" + (_reviews.Contains(',') ? "  ·  " + _reviews[(_reviews.IndexOf(',') + 1)..].Trim() : ""));
        long size = _game.SizeOnDisk > 0 ? _game.SizeOnDisk : Math.Abs(_folderSize);
        if (size > 0) Stat("\uEDA2", (_folderSize < 0 ? "≥ " : "") + (size >= 1L << 30 ? $"{size / 1024d / 1024d / 1024d:0.0} GB" : $"{size / 1024d / 1024d:0} MB"), "Size on disk");
        if (_game.HasUpdate) Stat("", "Update", _game.UpdateText);
        _stats.Visibility = _stats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildNotes()
    {
        _notes.Children.Clear();
        var mine = Host.Library.Find(_game.Key);
        bool any = mine is { Rating: > 0 } || !string.IsNullOrWhiteSpace(mine?.Status) || !string.IsNullOrWhiteSpace(mine?.Notes);
        if (!any) return;

        var stack = new StackPanel();
        var top = new WrapPanel();
        if (mine!.Rating > 0)
        {
            var stars = Ui.Text(Ui.Stars(mine.Rating), 22, "AccentBrush");
            stars.Margin = new Thickness(0, 0, 16, 0);
            top.Children.Add(stars);
        }
        if (!string.IsNullOrWhiteSpace(mine.Status)) top.Children.Add(Pill(mine.Status, "CircleBrush"));
        stack.Children.Add(top);
        if (!string.IsNullOrWhiteSpace(mine.Notes))
        {
            var notes = Line(mine.Notes, 16, "FgBrush", wrap: true);
            notes.Margin = new Thickness(0, 8, 0, 0);
            notes.MaxHeight = 96;
            stack.Children.Add(notes);
        }
        _notes.Children.Add(SectionTitle("Your notes"));
        var card = Card(stack, new Thickness(20, 16, 20, 16));
        _notes.Children.Add(CardButton(card, "notes", () => Host.Push(new GameNotesScreen(_game))));
    }

    private void BuildFacts()
    {
        _facts.Children.Clear();
        void Fact(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _facts.Children.Add(Line(label.ToUpperInvariant(), 12, "SubtleBrush", bold: true));
            var v = Line(value, 16, "FgBrush", wrap: true);
            v.Margin = new Thickness(0, 2, 0, 14);
            _facts.Children.Add(v);
        }
        Fact("Developer", _details?.Developers ?? "");
        Fact("Publisher", _details?.Publishers ?? "");
        Fact("Released", _details?.ReleaseDate is { Length: > 0 } date ? date : _sgdbReleased?.ToString("MMMM d, yyyy") ?? "");
        Fact("Genres", _details?.Genres ?? "");
        Fact("Store", _game.OnWishlist ? "Steam (wishlist)" : _game.IsGame ? _game.SourceName : "");
        if (_game.IsSteam) Fact("Steam app ID", _game.AppId.ToString());
        else
        {
            if (_wiki?.Description is { Length: > 0 } kind) Fact("What it is", char.ToUpper(kind[0]) + kind[1..]);
            string? folder = InstallFolder();
            if (folder != null) Fact("Installed in", folder);
            if (_game.LaunchPath.Length > 0 && !_game.LaunchPath.Contains("://") && !_game.LaunchPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Fact("Starts", System.IO.Path.GetFileName(_game.LaunchPath) + (string.IsNullOrWhiteSpace(_game.LaunchArgs) ? "" : "  " + _game.LaunchArgs));
            if (_storeId > 0) Fact("Also on Steam", "App " + _storeId);
        }
        if (_facts.Children.Count == 0) _facts.Children.Add(Line("No details yet.", 15));
    }

    // ---------------------------------------------------------------- store data

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        if (!_game.IsSteam)
        {
            await LoadOtherStoreAsync(ct);
            return;
        }
        _storeId = _game.AppId;
        try
        {
            if (!await LoadStoreAsync(ct)) _description.Text = "Store details aren't available for this title right now.";
            await LoadLogoAsync(ct);
            _ = LoadReviewsAsync(ct);
            await LoadNewsAsync(ct);
            await LoadDlcAsync(ct);
            await LoadAchievementsAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _description.Text = "Couldn't load store details.";
        }
    }

    /// <summary>
    /// A game added by hand or from another store: the Steam store's page for the same game when there is one,
    /// else Wikipedia; SteamGridDB for the artwork and release date; and what's on this PC (folder, size, program).
    /// </summary>
    private async Task LoadOtherStoreAsync(CancellationToken ct)
    {
        _description.Text = "Looking up " + _game.Name + "…";
        _ = MeasureFolderAsync(ct);
        _ = LoadSgdbInfoAsync(ct);
        _ = LoadLogoAsync(ct);
        try
        {
            string name = _game.OriginalName.Length > 0 ? _game.OriginalName : _game.Name;
            _storeId = await Task.Run(() => GameLookup.FindSteamMatchAsync(name, ct), ct);
            if (_storeId == 0 && _game.Name != name) _storeId = await Task.Run(() => GameLookup.FindSteamMatchAsync(_game.Name, ct), ct);
            if (_storeId > 0 && await LoadStoreAsync(ct))
            {
                _source.Text = "Description, screenshots and reviews from the game's Steam store page.";
                _ = LoadReviewsAsync(ct);
                await LoadNewsAsync(ct);
                return;
            }

            _wiki = await Task.Run(() => GameLookup.GetWikipediaAsync(name, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (_wiki != null)
            {
                _description.Text = _wiki.Extract;
                _source.Text = "From Wikipedia (" + _wiki.Title + "), available under CC BY-SA.";
            }
            else
            {
                _description.Text = "No store or encyclopedia page was found for this title. The artwork comes from SteamGridDB; " +
                                    "you can add your own notes with Rate.";
            }
            BuildFacts();
            if (Host.IsTop(this)) BuildActions();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _description.Text = "Couldn't look up details right now. Check the internet connection.";
        }
    }

    private string? InstallFolder()
    {
        if (!string.IsNullOrEmpty(_game.InstallDir) && System.IO.Directory.Exists(_game.InstallDir)) return _game.InstallDir;
        try
        {
            if (_game.LaunchPath.Length > 0 && System.IO.File.Exists(_game.LaunchPath)) return System.IO.Path.GetDirectoryName(_game.LaunchPath);
        }
        catch (Exception) { }
        return null;
    }

    private async Task MeasureFolderAsync(CancellationToken ct)
    {
        if (_game.SizeOnDisk > 0 || InstallFolder() is not string folder) return;
        // A drive's top level isn't a game's own folder: don't count it.
        if (System.IO.Path.GetPathRoot(folder) == folder) return;
        try
        {
            _folderSize = await Task.Run(() => GameLookup.FolderSize(folder, ct), ct);
            if (!ct.IsCancellationRequested) BuildStats();
        }
        catch (Exception) { }
    }

    private async Task LoadSgdbInfoAsync(CancellationToken ct)
    {
        try
        {
            var info = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
                return await client.GetGameInfoAsync(_game.OriginalName.Length > 0 ? _game.OriginalName : _game.Name, ct);
            }, ct);
            if (info?.Released is not DateTime released || ct.IsCancellationRequested) return;
            _sgdbReleased = released;
            if (_meta.Text.Length == 0) _meta.Text = released.ToString("yyyy");
            BuildFacts();
        }
        catch (Exception) { }
    }

    /// <summary>The Steam store's details for the store id; false if the store had nothing.</summary>
    private async Task<bool> LoadStoreAsync(CancellationToken ct)
    {
        var details = await Task.Run(() => SteamStoreClient.GetDetailsAsync(_storeId, ct), ct);
        if (ct.IsCancellationRequested || details == null) return false;
        _details = details;
        var meta = new List<string>();
        if (details.Developers.Length > 0) meta.Add(details.Developers);
        if (details.ReleaseDate.Length > 0) meta.Add(details.ReleaseDate);
        _meta.Text = string.Join("  ·  ", meta);
        _description.Text = details.Description.Length > 0 ? details.Description : "No description.";
        BuildFacts();
        BuildBadges();
        BuildStats();
        if (Host.IsTop(this)) BuildActions(); // the trailer and store buttons depend on the details

        // Fallbacks for the artwork: the store's header image for the cover, a screenshot for the banner.
        if (_cover.Source == null && details.HeaderImage != null)
            _cover.Source = await Task.Run(() => ImageLoader.FromUrlAsync(details.HeaderImage, 600, ct), ct);
        if (_heroImage.Source == null && details.Screenshots.Count > 0)
            _ = LoadHeroFallbackAsync(details.Screenshots[0].Full, ct);
        AddScreenshots(details);
        return true;
    }

    private async Task LoadHeroFallbackAsync(string url, CancellationToken ct)
    {
        await Task.Delay(1500, ct); // give SteamGridDB's banner the first chance
        if (_heroImage.Source != null) return;
        try { ShowHero(await Task.Run(() => ImageLoader.FromUrlAsync(url, 1600, ct), ct)); }
        catch (Exception) { }
    }

    private void AddScreenshots(StoreDetails details)
    {
        var shots = details.Screenshots.Take(12).ToList();
        if (shots.Count == 0) return;
        var fullSize = shots.Select(x => x.Full).ToList();
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < shots.Count; i++)
        {
            int position = i;
            var view = new Image { Stretch = Stretch.UniformToFill };
            var box = new Border { Width = 288, Height = 162, CornerRadius = new CornerRadius(10), ClipToBounds = true };
            box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            var clip = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 288, 162), 10, 10) };
            clip.Children.Add(view);
            box.Child = clip;
            strip.Children.Add(CardButton(box, "shot:" + i, () => Host.Push(new ViewerScreen(fullSize, position))));
            _ = LoadImageAsync(view, shots[i].Thumb, 480, null, _cts.Token);
        }
        _sections.Children.Add(SectionTitle("Screenshots"));
        _sections.Children.Add(Strip(strip));
    }

    private async Task LoadReviewsAsync(CancellationToken ct)
    {
        try
        {
            var summary = await Task.Run(() => SteamStoreClient.GetReviewSummaryAsync(_storeId, ct), ct);
            if (summary is not { } r || ct.IsCancellationRequested) return;
            _reviews = $"{r.Verdict}, {r.Percent}% of {r.Total:N0}";
            BuildStats();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Recent announcements and patch notes, as cards you can read without a browser.</summary>
    private async Task LoadNewsAsync(CancellationToken ct)
    {
        var items = await Task.Run(() => SteamStoreClient.GetNewsAsync(_storeId, 6, ct), ct);
        if (ct.IsCancellationRequested || items.Count == 0) return;

        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items)
        {
            var post = item;
            var stack = new StackPanel { Width = 300, Height = 132 };
            stack.Children.Add(Line(post.Date.ToString("MMMM d, yyyy").ToUpperInvariant(), 12, "AccentBrush", bold: true));
            var title = Line(post.Title, 17, "FgBrush", bold: true, wrap: true);
            title.MaxHeight = 48;
            title.Margin = new Thickness(0, 6, 0, 6);
            stack.Children.Add(title);
            var snippet = Line(post.Snippet, 14, "SubtleBrush", wrap: true);
            snippet.MaxHeight = 40;
            stack.Children.Add(snippet);
            strip.Children.Add(CardButton(Card(stack, new Thickness(18, 14, 18, 14)), "news:" + post.Url, () => Host.Push(new NewsReaderScreen(post))));
        }
        _sections.Children.Add(SectionTitle("News and updates"));
        _sections.Children.Add(Strip(strip));
    }

    /// <summary>DLC the store lists (and which of it is installed), and installed Steam Workshop mods.</summary>
    private async Task LoadDlcAsync(CancellationToken ct)
    {
        var (dlc, mods) = await Task.Run(async () =>
        {
            var dlcRows = new List<(string Name, bool Installed, int Id)>();
            var installed = SteamLibrary.GetInstalledDlcIds(_game.AppId);
            foreach (var id in (_details?.DlcIds ?? new List<int>()).Take(25))
            {
                ct.ThrowIfCancellationRequested();
                var name = await SteamStoreClient.GetAppNameAsync(id, ct);
                if (name != null) dlcRows.Add((name, installed.Contains(id), id));
            }

            var modIds = SteamLibrary.GetWorkshopItemIds(_game.AppId).Take(30).ToList();
            var modNames = modIds.Count == 0 ? new List<string>() : await SteamStoreClient.GetWorkshopTitlesAsync(modIds, ct);
            return (dlcRows, modNames);
        }, ct);
        if (ct.IsCancellationRequested || (dlc.Count == 0 && mods.Count == 0)) return;

        var wrap = new WrapPanel();
        void Chip(string glyph, string name, string status, bool dim, string id, Action click)
        {
            var row = new DockPanel { Width = 300 };
            var icon = Ui.Glyph(glyph, 20, dim ? "SubtleBrush" : "AccentBrush");
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Line(name, 15, "FgBrush", bold: true));
            text.Children.Add(Line(status, 13));
            row.Children.Add(text);
            var card = Card(row, new Thickness(16, 12, 16, 12));
            if (dim) card.Opacity = 0.7;
            wrap.Children.Add(CardButton(card, id, click));
        }
        foreach (var (name, installed, id) in dlc.OrderByDescending(d => d.Installed))
            Chip("", name, installed ? "Installed" : "Not installed", !installed, "dlc:" + id, () => ProcessUtil.Open("steam://store/" + id));
        foreach (var mod in mods)
            Chip("", mod, "Workshop mod", false, "mod:" + mod, () => ProcessUtil.Open($"steam://url/SteamWorkshopPage/{_game.AppId}"));

        _sections.Children.Add(SectionTitle(mods.Count > 0 ? "DLC and mods" : "DLC"));
        _sections.Children.Add(wrap);
    }

    /// <summary>Puts the game's logo artwork on the right of the banner, when SteamGridDB has one.</summary>
    private async Task LoadLogoAsync(CancellationToken ct)
    {
        try
        {
            var logo = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
                var path = await client.GetLogoAsync(_game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 920);
            }, ct);
            if (logo == null || ct.IsCancellationRequested) return;
            var image = new Image
            {
                Source = logo, MaxHeight = 150, MaxWidth = 380, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = _game.Name,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 }
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            if (!Theme.CurrentIsDark)
            {
                // Logos are usually drawn light, for dark backgrounds: give them one on the light theme.
                _logoHost.Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(18, 10, 18, 10), HorizontalAlignment = HorizontalAlignment.Right, Child = image
                };
            }
            else _logoHost.Content = image;
            if (!Theme.ReduceMotion) _logoHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        }
        catch (Exception)
        {
            // No logo, offline, or the page was closed: the banner just has the title.
        }
    }

    private async Task LoadAchievementsAsync(CancellationToken ct)
    {
        var mine = await Task.Run(() => SteamStoreClient.GetAchievementsAsync(_game.AppId, ct), ct);
        if (ct.IsCancellationRequested) return;

        List<Achievement> list;
        string? note = null;
        if (mine != null)
        {
            // Unlocked ones first, each group in the game's own order.
            list = mine.OrderByDescending(a => a.Unlocked == true).ToList();
            _progress = (mine.Count(a => a.Unlocked == true), mine.Count);
            BuildStats();
        }
        else if (_details is { HighlightedAchievements.Count: > 0 })
        {
            list = _details.HighlightedAchievements;
            note = $"Showing {list.Count} of {_details.AchievementTotal}. To see every achievement and which ones you have unlocked, " +
                   "set “Game details” to Public in your Steam profile's privacy settings.";
        }
        else return;

        var header = new DockPanel { Margin = new Thickness(2, 30, 0, 12), LastChildFill = true };
        if (_progress is { Total: > 0 } p)
        {
            var count = Ui.Text($"{p.Unlocked} of {p.Total}  ·  {100.0 * p.Unlocked / p.Total:0}%", 16, "SubtleBrush");
            count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right);
            header.Children.Add(count);
        }
        header.Children.Add(Ui.Text("Achievements", 22, "FgBrush", bold: true));
        _sections.Children.Add(header);
        if (_progress is { Total: > 0 } progress)
        {
            var bar = Bar((double)progress.Unlocked / progress.Total, 8);
            bar.Margin = new Thickness(0, 0, 10, 16);
            _sections.Children.Add(bar);
        }
        if (note != null)
        {
            var n = Line(note, 15, "SubtleBrush", wrap: true);
            n.Margin = new Thickness(2, 0, 0, 12);
            _sections.Children.Add(n);
        }

        var grid = new UniformGrid { Columns = 2 };
        using var gate = new SemaphoreSlim(6);
        var loads = new List<Task>();
        foreach (var a in list.Take(MaxAchievementCards))
        {
            var row = new DockPanel();
            var icon = new Image { Width = 52, Height = 52 };
            var iconBox = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = icon, Margin = new Thickness(0, 0, 14, 0) };
            iconBox.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            DockPanel.SetDock(iconBox, Dock.Left);
            row.Children.Add(iconBox);

            if (a.Unlocked is bool unlocked)
            {
                var state = Ui.Glyph(unlocked ? "" : "", 18, unlocked ? "AccentBrush" : "SubtleBrush");
                state.VerticalAlignment = VerticalAlignment.Center;
                state.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(state, Dock.Right);
                row.Children.Add(state);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Line(a.Name, 16, "FgBrush", bold: true));
            if (a.Description.Length > 0)
            {
                var d = Line(a.Description, 13, "SubtleBrush", wrap: true);
                d.MaxHeight = 36;
                text.Children.Add(d);
            }
            row.Children.Add(text);

            var card = Card(row, new Thickness(14, 12, 16, 12));
            card.Margin = new Thickness(0, 0, 10, 10);
            if (a.Unlocked == false) card.Opacity = 0.55;
            grid.Children.Add(card);
            if (a.Icon.Length > 0) loads.Add(LoadImageAsync(icon, a.Icon, 104, gate, ct));
        }
        _sections.Children.Add(grid);
        await Task.WhenAll(loads);
    }

    private static async Task LoadImageAsync(Image view, string url, int width, SemaphoreSlim? gate, CancellationToken ct)
    {
        try
        {
            if (gate != null) await gate.WaitAsync(ct);
            try
            {
                var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, width, ct), ct);
                if (!ct.IsCancellationRequested) view.Source = source;
            }
            finally
            {
                gate?.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}

/// <summary>Add apps to the launcher: pick from what is installed, or get one from the Microsoft Store.</summary>
public sealed class AddAppScreen : ListScreen
{
    private List<InstalledApp>? _apps;
    private string? _error;
    private CancellationTokenSource _iconCts = new();

    public AddAppScreen() : base("Add apps") { }

    public override void OnShown()
    {
        Rebuild();
        _ = ScanAsync();
    }

    public override void OnResumed() => _ = ScanAsync(); // something may have just been installed

    public override void OnClosed() => _iconCts.Cancel();

    private async Task ScanAsync()
    {
        try
        {
            _apps = await Task.Run(() => InstalledApps.ScanAsync());
            _error = null;
        }
        catch (Exception ex)
        {
            _apps = new();
            _error = "Couldn't list installed apps: " + ex.Message;
        }
        Rebuild();
    }

    protected override void Build()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;

        AddHeader("Microsoft Store");
        AddRow("Search the Microsoft Store and install…", glyph: "", click: SearchStore);
        AddRow("Open the Microsoft Store app", glyph: "", click: () => ProcessUtil.Open("ms-windows-store://home"));

        AddHeader("Installed on this PC");
        if (_apps == null) { AddNote("Scanning installed apps…"); return; }
        if (_error != null) AddNote(_error);

        var pending = new List<(Image View, string AppId)>();
        foreach (var app in _apps)
        {
            var a = app;
            var icon = new Image();
            AddRow(a.Name, Host.HasApp(a.AppId) ? "Added  ✓" : null, icon: icon, click: () =>
            {
                if (Host.HasApp(a.AppId)) Host.RemoveApp(a.AppId);
                else Host.AddApp(a.Name, a.AppId);
                Rebuild();
            });
            pending.Add((icon, a.AppId));
        }

        // Icons come from the shell one at a time in the background so the list appears immediately.
        Task.Run(() =>
        {
            foreach (var (view, appId) in pending)
            {
                if (ct.IsCancellationRequested) return;
                var source = ShellThumb.ForApp(appId, 48);
                if (source != null) Dispatcher.BeginInvoke(new Action(() => view.Source = source));
            }
        }, ct);
    }

    private async void SearchStore()
    {
        var query = await Host.PromptAsync("Search the Microsoft Store", "Type the name of an app or game.", "", "Search");
        if (string.IsNullOrWhiteSpace(query)) return;
        Host.Push(new StoreSearchScreen(query.Trim()));
    }
}

public sealed class StoreSearchScreen : ListScreen
{
    private readonly string _query;
    private List<StoreResult>? _results;
    private string? _message;
    private bool _installing;

    public StoreSearchScreen(string query) : base("Microsoft Store – “" + query + "”")
    {
        _query = query;
    }

    public override void OnShown()
    {
        Rebuild();
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        try
        {
            _results = await Task.Run(() => InstalledApps.SearchStoreAsync(_query));
            if (_results.Count == 0) _message = "No results.";
        }
        catch (Exception)
        {
            _results = new();
            _message = "The Windows Package Manager (winget) isn't available, so the Store can't be searched from here. " +
                       "Use the button below to search in the Microsoft Store app instead.";
        }
        Rebuild();
    }

    protected override void Build()
    {
        AddRow("Show these results in the Microsoft Store app", glyph: "",
            click: () => ProcessUtil.Open("ms-windows-store://search/?query=" + Uri.EscapeDataString(_query)));

        if (_installing) { AddNote("Installing… this can take a few minutes. You can keep using the launcher."); }
        if (_results == null) { AddNote("Searching…"); return; }
        if (_message != null) AddNote(_message);

        if (_results.Count > 0) AddHeader("Results");
        foreach (var r in _results)
        {
            var result = r;
            AddRow(r.Name, "Install", "", click: () => Install(result));
        }
    }

    private async void Install(StoreResult result)
    {
        if (_installing) return;
        if (await Host.AskAsync("Install " + result.Name + "?",
                "It will be downloaded from the Microsoft Store. Windows may ask for confirmation.",
                "Install", "Cancel") != 0) return;

        _installing = true;
        Rebuild();
        bool ok;
        try { ok = await Task.Run(() => InstalledApps.InstallFromStoreAsync(result.Id)); }
        catch (Exception) { ok = false; }
        _installing = false;
        Rebuild();

        if (ok)
            await Host.AskAsync(result.Name + " was installed",
                "Go back to “Add apps” to put it on the launcher.", "OK");
        else
            await Host.AskAsync("Couldn't install " + result.Name,
                "Try installing it from the Microsoft Store app instead (first row on this page).", "OK");
    }
}

/// <summary>
/// Every game in one place. A sidebar chooses what to show (all, favorites, installed, not installed,
/// by store, category or status); the grid can be sorted, grouped and searched.
/// </summary>
public sealed class LibraryScreen : Screen
{
    // With the sidebar shrunk to a blade, tiles are a little smaller so every row holds exactly six.
    private double TileSize => Layout switch { "compact" => 124, "large" => 226, _ => Collapsed ? 148 : 164 };

    /// <summary>
    /// The page's layout (Settings > Games and library, or the toolbar):
    ///  - sidebar: sections on the left, tiles on the right (the usual);
    ///  - compact: an icons-only sidebar and small tiles, to see more at once;
    ///  - large: no sidebar (a "Show" button picks the section), big tiles;
    ///  - list: sections on the left, one row per game with its facts;
    ///  - shelves: no sidebar, rows that scroll sideways (recently played, favorites, each store, collections);
    ///  - spotlight: an icons-only sidebar, the highlighted game large across the top, tiles below.
    /// </summary>
    public static readonly (string Id, string Name, string About)[] Layouts =
    {
        ("sidebar", "Sidebar", "Sections on the left, tiles on the right"),
        ("compact", "Compact", "Icons-only sidebar and small tiles, to see more at once"),
        ("large", "Large tiles", "No sidebar and big tiles; pick the section with Show"),
        ("list", "List", "One row per game, with its store, play time and when you last played"),
        ("shelves", "Shelves", "Rows that scroll sideways: recent, favorites, each store, collections"),
        ("spotlight", "Spotlight", "The highlighted game large across the top, tiles below"),
    };

    private string Layout => Layouts.Any(l => l.Id == Host.Settings.LibraryLayout) ? Host.Settings.LibraryLayout : "sidebar";
    private bool SidebarHidden => Layout is "large" or "shelves";
    private readonly Border _spotlight = new() { Margin = new Thickness(0, 0, 4, 14), Height = 250, Visibility = Visibility.Collapsed };
    private const string Uncategorized = "Uncategorized";
    private const int MaxNotInstalledShown = 400;

    private readonly StackPanel _sidebar = new() { Width = 236 };
    private Border _sidePanel = null!;
    private readonly TextBox _searchInput = new();
    private readonly Border _searchBar = new();
    private readonly System.Windows.Threading.DispatcherTimer _searchSoon = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly WrapPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _groups = new();
    private readonly TextBlock _count = Ui.Text("", 16, "SubtleBrush");

    private string _filter = "all";     // all | favorites | installed | notinstalled | rated | src:<store> | cat:<name> | status:<s>
    private string _sort = "recent";    // recent | name | playtime | size | rating
    private string _search = "";
    private bool _grouped;
    private string? _lastFocus;         // game key, "tool:<index>" or "side:<filter>"

    public LibraryScreen()
    {
        static ScrollViewer Scroller(UIElement content) => new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = content
        };

        var right = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
        right.Children.Add(_spotlight);
        right.Children.Add(_toolbar);
        right.Children.Add(_groups);

        // Sidebar on a slightly raised panel so the two halves read as separate areas. It can shrink to just icons.
        var sidePanel = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 4, 8), Child = Scroller(_sidebar), ClipToBounds = true };
        sidePanel.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        _sidePanel = sidePanel;
        BuildSearchBar();

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(sidePanel);
        var rightScroll = Scroller(right);
        Grid.SetColumn(rightScroll, 1);
        layout.Children.Add(rightScroll);

        SetPage("Game library", layout,
            "{Select}  Play      {Details}  Details      {Options}  Options      LT / RT  Jump by letter      {Back}  Back", wide: true);
    }

    // ---------------------------------------------------------------- search bar

    /// <summary>
    /// A real search box: type on a keyboard and the games filter as you go. With a controller, select it to type
    /// on the on-screen keyboard. B clears it.
    /// </summary>
    private void BuildSearchBar()
    {
        var grid = new Grid { Width = 380, Height = 46 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glass = Ui.Text("\uE721", 17, "SubtleBrush");
        glass.FontFamily = Ui.IconFont;
        glass.HorizontalAlignment = HorizontalAlignment.Center;
        glass.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(glass);

        var placeholder = Ui.Text("Search your games", 17, "SubtleBrush");
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(placeholder, 1);
        grid.Children.Add(placeholder);

        _searchInput.Background = Brushes.Transparent;
        _searchInput.BorderThickness = new Thickness(0);
        _searchInput.FontSize = 17;
        _searchInput.VerticalContentAlignment = VerticalAlignment.Center;
        _searchInput.FocusVisualStyle = null;
        _searchInput.SetResourceReference(Control.ForegroundProperty, "FgBrush");
        _searchInput.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        Grid.SetColumn(_searchInput, 1);
        grid.Children.Add(_searchInput);

        var clear = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Clear" };
        var x = Ui.Text("\uE711", 13, "SubtleBrush");
        x.FontFamily = Ui.IconFont;
        clear.Content = x;
        clear.Click += (_, _) => _searchInput.Text = "";
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);

        _searchBar.Child = grid;
        _searchBar.Margin = new Thickness(0, 0, 10, 6);
        _searchBar.BorderThickness = new Thickness(3);
        _searchBar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _searchBar.SetResourceReference(Border.CornerRadiusProperty, "RowCorner");
        _searchBar.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");

        _searchInput.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _searchInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = _searchInput.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _searchSoon.Stop();
            _searchSoon.Start();
        };
        _searchSoon.Tick += (_, _) =>
        {
            _searchSoon.Stop();
            string text = _searchInput.Text.Trim();
            if (text == _search) return;
            _search = text;
            Rebuild();
        };
        // The highlight ring shows where the controller is, like every other control.
        _searchInput.GotKeyboardFocus += (_, _) =>
        {
            _searchBar.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            _lastFocus = "search";
        };
        _searchInput.LostKeyboardFocus += (_, _) => _searchBar.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
    }

    /// <summary>A controller press on the search box: type on the on-screen keyboard.</summary>
    private async Task TypeSearchAsync()
    {
        // Windows' touch keyboard: type straight into the search bar, no pop-up.
        if (TouchKeyboard.UseWindows)
        {
            _searchInput.Focus();
            _searchInput.CaretIndex = _searchInput.Text.Length;
            await TouchKeyboard.ShowAsync();
            return;
        }
        var text = await Host.PromptAsync("Search games", "Leave it empty to show everything.", _searchInput.Text, "Search");
        if (text == null) return;
        _searchInput.Text = text.Trim();
        _search = text.Trim();
        _searchSoon.Stop();
        _lastFocus = null; // show the results
        Rebuild();
    }

    // ---------------------------------------------------------------- sidebar size

    private bool Collapsed => Layout is "compact" or "spotlight" || Host.Settings.LibrarySidebarCollapsed;

    /// <summary>Shrinks the sidebar to a blade of icons, or opens it back up.</summary>
    private void ToggleSidebar()
    {
        // The compact and spotlight layouts always use the icons-only sidebar; opening it goes back to the usual one.
        if (Layout is "compact" or "spotlight") { Host.Settings.LibraryLayout = "sidebar"; Host.Settings.LibrarySidebarCollapsed = true; }
        Host.Settings.LibrarySidebarCollapsed = !Collapsed;
        Host.Settings.Save();
        _lastFocus = "side:toggle";
        SizeSidebar(animate: true);
        Rebuild();
    }

    private void SizeSidebar(bool animate)
    {
        _sidePanel.Visibility = SidebarHidden ? Visibility.Collapsed : Visibility.Visible;
        if (_sidePanel.Parent is Grid layout && layout.Children.Count > 1 && layout.Children[1] is FrameworkElement rightSide)
            rightSide.Margin = SidebarHidden ? new Thickness(-22, 0, 0, 0) : new Thickness(0);
        _spotlight.Visibility = Layout == "spotlight" ? Visibility.Visible : Visibility.Collapsed;
        double target = Collapsed ? 92 : 264;
        _sidebar.Width = Collapsed ? 72 : 236;
        _sidePanel.Padding = Collapsed ? new Thickness(6, 4, 6, 8) : new Thickness(10, 4, 4, 8);
        if (!animate || Theme.ReduceMotion || double.IsNaN(_sidePanel.Width))
        {
            _sidePanel.BeginAnimation(WidthProperty, null);
            _sidePanel.Width = target;
            return;
        }
        var slide = new System.Windows.Media.Animation.DoubleAnimation(_sidePanel.ActualWidth > 0 ? _sidePanel.ActualWidth : target, target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        slide.Completed += (_, _) => { _sidePanel.BeginAnimation(WidthProperty, null); _sidePanel.Width = target; };
        _sidePanel.BeginAnimation(WidthProperty, slide);
    }

    public override void OnShown()
    {
        SizeSidebar(animate: false);
        Host.LibraryChanged += OnLibraryChanged;
        Host.ApplyCachedLists(); // saved lists and counts show at once; they refresh when their section is picked
        Rebuild();
        _ = Host.RefreshAchievementProgressAsync();
    }

    public override void OnResumed() => Rebuild();

    public override void OnClosed()
    {
        Host.LibraryChanged -= OnLibraryChanged;
        _artCts?.Cancel();
    }

    private void OnLibraryChanged()
    {
        if (Host.IsTop(this)) Rebuild();
    }

    // ---------------------------------------------------------------- what to show

    private GameCollection? CollectionOf(string filter) =>
        filter.StartsWith("col:") ? Host.Library.Collections.FirstOrDefault(c => c.Id == filter[4..]) : null;

    private string FilterName(string filter) => filter switch
    {
        "all" => "All games",
        "favorites" => "Favorites",
        "installed" => "Installed",
        "notinstalled" => "Not installed",
        "wishlist" => "Wishlist",
        "updates" => "Updates",
        "collections" => "Collections",
        _ when filter.StartsWith("col:") => CollectionOf(filter)?.Name ?? "Collection",
        "rated" => "4 stars and up",
        _ when filter.StartsWith("status:") => filter[7..],
        _ => filter[4..]
    };

    private bool Matches(Game g, string filter) => filter switch
    {
        "all" => g.IsInstalled,
        "favorites" => g.IsFavorite,
        "installed" => g.IsInstalled,
        "notinstalled" => !g.IsInstalled && !g.OnWishlist,
        "wishlist" => g.OnWishlist,
        "updates" => g.IsInstalled && g.HasUpdate,
        "collections" => false,
        _ when filter.StartsWith("col:") => CollectionOf(filter)?.Games.Contains(g.Key) == true,
        "rated" => Host.Library.Find(g.Key) is { Rating: >= 4 },
        _ when filter.StartsWith("status:") => Host.Library.Find(g.Key)?.Status == filter[7..],
        _ when filter.StartsWith("src:") => g.IsInstalled && g.SourceName == filter[4..],
        _ => string.Equals(Host.CategoryOf(g), filter[4..], StringComparison.OrdinalIgnoreCase)
    };

    private string SortLabel => _sort switch
    {
        "name" => "Name",
        "size" => "Size",
        "playtime" => "Time played",
        "rating" => "My rating",
        _ => "Recently played"
    };

    private void Rebuild()
    {
        var everything = Host.AllGames.Concat(Host.NotInstalledGames).Concat(Host.WishlistGames).ToList();

        BuildSidebar(everything);
        BuildToolbar();
        _groups.Children.Clear();
        // Art still on its way for the old tiles isn't wanted any more.
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        _artQueue.Clear();

        IEnumerable<Game> games = everything.Where(g => Matches(g, _filter));
        if (_search.Length > 0)
            games = games.Where(g => g.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase));
        games = _sort switch
        {
            "name" => games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            "size" => games.OrderByDescending(g => g.SizeOnDisk),
            "rating" => games.OrderByDescending(g => Host.Library.Find(g.Key)?.Rating ?? 0)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            // Steam knows its own totals; for everything else use what the launcher has measured.
            "playtime" => games.OrderByDescending(g =>
                g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : Host.Playtime.TotalSeconds(g.Key) / 60),
            // Games that were never played or installed here have no date: list those by name.
            _ => games.OrderByDescending(g => g.RecentAt).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        bool online = _filter is "notinstalled" or "wishlist";
        var list = games.Take(online ? MaxNotInstalledShown : int.MaxValue).ToList();
        _count.Text = list.Count == 1 ? "1 game" : list.Count + " games";

        if (_filter == "notinstalled") AddNotInstalledNotice();
        if (_filter == "wishlist") AddWishlistNotice();

        if (_filter == "collections")
        {
            _count.Text = Host.Library.Collections.Count == 1 ? "1 collection" : Host.Library.Collections.Count + " collections";
            AddCollections(everything);
        }
        else if (_filter == "updates" && list.Count == 0)
        {
            AddNote("Every installed Steam game is up to date. Games appear here while Steam has an update waiting or downloading for them.");
        }
        else if (CollectionOf(_filter) is { } emptyCollection && list.Count == 0)
        {
            AddNote("\u201C" + emptyCollection.Name + "\u201D is empty. To add games, highlight one anywhere in the library, press " +
                    Hints.KeyLabel("Options") + " and choose \u201CAdd to a collection\u201D (or use the same row on a game's details page).");
        }
        else if (list.Count == 0)
        {
            if ((_filter != "notinstalled" || Host.OwnedGamesState == "ready") && (_filter != "wishlist" || Host.WishlistState == "ready"))
                AddNote("Nothing to show here. Pick another section on the left, or clear the search.");
        }
        else if (Layout == "shelves" && _filter == "all" && _search.Length == 0)
        {
            AddShelves(list);
        }
        else if (_grouped)
        {
            var byCategory = list.GroupBy(g => Host.CategoryOf(g) ?? Uncategorized)
                .OrderBy(g => g.Key == Uncategorized)          // uncategorized last
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in byCategory)
            {
                _groups.Children.Add(Ui.Header($"{group.Key}  ({group.Count()})"));
                AddGames(group);
            }
        }
        else
        {
            AddGames(list);
        }

        // Covers for games that aren't installed are only fetched once they are actually on screen.
        if (online) Host.EnsureCovers(list);
        RestoreFocus();
    }

    /// <summary>Shelves: rows of games that scroll sideways, each with a heading.</summary>
    private void AddShelves(List<Game> all)
    {
        var installed = all.Where(g => g.IsInstalled).ToList();
        void Shelf(string heading, IEnumerable<Game> games)
        {
            var list = games.Take(30).ToList();
            if (list.Count == 0) return;
            var header = Ui.Header(heading);
            _groups.Children.Add(header);
            AddGames(list, shelf: true);
        }
        Shelf("Recently played", installed.Where(g => g.RecentAt > DateTime.MinValue.AddYears(1)).OrderByDescending(g => g.RecentAt));
        Shelf("Favorites", installed.Where(g => g.IsFavorite));
        Shelf("Update waiting", installed.Where(g => g.HasUpdate));
        foreach (var store in installed.GroupBy(g => g.SourceName).OrderByDescending(x => x.Count()))
            Shelf(store.Key, store.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        foreach (var c in Host.Library.Collections)
            Shelf(c.Name, installed.Where(g => Matches(g, "col:" + c.Id)));
        Shelf("Not installed", all.Where(g => !g.IsInstalled && !g.OnWishlist).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    private void AddNote(string text)
    {
        var note = Ui.Text(text, 17, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.Margin = new Thickness(4, 8, 8, 10);
        _groups.Children.Add(note);
    }

    // ---------------------------------------------------------------- collections

    /// <summary>Icons a collection can use instead of a picture.</summary>
    private static readonly (string Name, string Glyph)[] CollectionIcons =
    {
        ("Folder", "\uE8F1"), ("Star", "\uE734"), ("Heart", "\uEB52"), ("Controller", "\uE7FC"), ("Trophy", "\uE7C1"),
        ("People (co-op)", "\uE716"), ("Clock (backlog)", "\uE823"), ("Flag (finished)", "\uE7C1"), ("Globe (online)", "\uE774"),
        ("Rocket", "\uE7B5"), ("Puzzle", "\uEA86"), ("Car (racing)", "\uE804"), ("Music", "\uE8D6"), ("Film (story)", "\uE8B2"),
        ("Lightning (action)", "\uE945"), ("Map (open world)", "\uE707"), ("Shield", "\uEA18"), ("Home (cozy)", "\uE80F"),
        ("Pin", "\uE718"), ("Tag", "\uE8EC")
    };

    /// <summary>A tile for making a collection, then one per collection: its picture or icon, name and size.</summary>
    private void AddCollections(List<Game> everything)
    {
        var panel = new WrapPanel();

        Button CollectionTile(UIElement face, string name, string sub, string id)
        {
            var stack = new StackPanel { Width = TileSize };
            stack.Children.Add(face);
            var title = Ui.Text(name, 15, "FgBrush", bold: true);
            title.Margin = new Thickness(2, 6, 2, 0);
            stack.Children.Add(title);
            var line = Ui.Text(sub, 13, "SubtleBrush");
            line.Margin = new Thickness(2, 1, 2, 2);
            stack.Children.Add(line);
            var tile = Ui.Tile(stack);
            tile.Margin = new Thickness(0, 0, 4, 6);
            tile.DataContext = id;
            tile.GotKeyboardFocus += (_, _) => _lastFocus = id;
            panel.Children.Add(tile);
            return tile;
        }

        var create = CollectionTile(GridScreen.GlyphBox(TileSize, TileSize, "\uE710"), "New collection", "Make one", "col-new");
        create.Click += async (_, _) =>
        {
            var name = await Host.PromptAsync("New collection", "For example: Co-op night, Backlog, Cozy games.", "", "Create");
            if (string.IsNullOrWhiteSpace(name)) return;
            _lastFocus = "col-tile:" + Host.CreateCollection(name).Id;
            Rebuild();
        };

        var keys = everything.Select(g => g.Key).ToHashSet();
        foreach (var collection in Host.Library.Collections)
        {
            var c = collection;
            UIElement face;
            var picture = !string.IsNullOrEmpty(c.CoverPath) && File.Exists(c.CoverPath) ? ImageLoader.FromFile(c.CoverPath, 400) : null;
            if (picture != null)
            {
                var box = GridScreen.ImageBox(TileSize, TileSize, out var view);
                view.Source = picture;
                face = box;
            }
            else
            {
                face = GridScreen.GlyphBox(TileSize, TileSize, c.Icon);
            }
            int count = c.Games.Count(keys.Contains);
            var tile = CollectionTile(face, c.Name, count == 1 ? "1 game" : count + " games", "col-tile:" + c.Id);
            tile.Tag = c;
            tile.Click += (_, _) => OpenCollection(c);
        }
        _groups.Children.Add(panel);

        if (Host.Library.Collections.Count == 0)
            AddNote("Collections are your own groups of games. A game can be in as many as you like. Make one here, then add games " +
                    "with " + Hints.KeyLabel("Options") + " on any game, or from a game's details page.");
        else
            AddNote(Hints.KeyLabel("Options") + " on a collection renames it, changes its picture or icon, or deletes it.");
    }

    private void OpenCollection(GameCollection collection)
    {
        _filter = "col:" + collection.Id;
        _lastFocus = "";
        Rebuild();
    }

    private async Task CollectionOptionsAsync(GameCollection c)
    {
        int choice = await Host.AskWithIconsAsync(c.Name, null,
            ("Open", "\uE8E5"), ("Rename", "\uE8AC"), ("Choose a picture (1:1 image file)", "\uEB9F"),
            ("Use a game's cover from this collection", "\uE7FC"), ("Choose an icon", "\uE8F1"), ("Delete collection", "\uE74D"));
        switch (choice)
        {
            case 0:
                OpenCollection(c);
                return;
            case 1:
                var name = await Host.PromptAsync("Rename collection", null, c.Name, "Rename");
                if (!string.IsNullOrWhiteSpace(name)) c.Name = name.Trim();
                break;
            case 2:
                var file = await Host.PickFileAsync("Choose a square picture", new[] { ".png", ".jpg", ".jpeg", ".bmp" });
                if (file == null) return;
                try
                {
                    // Kept with the launcher's own art, so it survives the original being moved or deleted.
                    Directory.CreateDirectory(SteamGridDbClient.CustomDir);
                    var copy = Path.Combine(SteamGridDbClient.CustomDir, "collection_" + c.Id + "_" + DateTime.Now.Ticks + Path.GetExtension(file));
                    File.Copy(file, copy, true);
                    c.CoverPath = copy;
                }
                catch (Exception)
                {
                    c.CoverPath = file;
                }
                break;
            case 3:
                var games = Host.AllGames.Concat(Host.NotInstalledGames)
                    .Where(g => c.Games.Contains(g.Key) && g.Cover is System.Windows.Media.Imaging.BitmapSource).Take(30).ToList();
                if (games.Count == 0)
                {
                    await Host.AskAsync("No covers to use yet", "Add a game with a cover to this collection first.", "OK");
                    return;
                }
                int game = await Host.AskAsync("Use which game's cover?", null, games.Select(g => g.Name).ToArray());
                if (game < 0) return;
                try
                {
                    // A copy of the cover as it is now, so the collection keeps it even if the game's art changes.
                    Directory.CreateDirectory(SteamGridDbClient.CustomDir);
                    var copy = Path.Combine(SteamGridDbClient.CustomDir, "collection_" + c.Id + "_" + DateTime.Now.Ticks + ".png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create((System.Windows.Media.Imaging.BitmapSource)games[game].Cover!));
                    using (var stream = File.Create(copy)) encoder.Save(stream);
                    c.CoverPath = copy;
                }
                catch (Exception)
                {
                    await Host.AskAsync("Couldn't use that cover", "The picture could not be saved.", "OK");
                    return;
                }
                break;
            case 4:
                int icon = await Host.AskWithIconsAsync("Icon for " + c.Name, null, CollectionIcons.Select(i => (i.Name, i.Glyph)).ToArray());
                if (icon < 0) return;
                c.Icon = CollectionIcons[icon].Glyph;
                c.CoverPath = null; // the icon replaces any picture
                break;
            case 5:
                if (await Host.AskAsync("Delete \u201C" + c.Name + "\u201D?", "The games stay in your library; only the collection goes.",
                        "Cancel", "Delete") != 1) return;
                Host.Library.Collections.Remove(c);
                break;
            default:
                return;
        }
        Host.Library.Save();
        Rebuild();
    }

    /// <summary>The "Not installed" list comes from your Steam account, which needs your own API key.</summary>
    private void AddNotInstalledNotice()
    {
        switch (Host.OwnedGamesState)
        {
            case "nokey":
                AddNote("To list the Steam games you own but haven't installed, add your Steam Web API key. " +
                        "It's free at steamcommunity.com/dev/apikey and is stored only on this PC.");
                var add = Ui.PanelButton("Add Steam Web API key");
                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Tag = "tool:key";
                add.Click += (_, _) => _ = SettingsScreen.EditSteamKeyAsync(Host, () => _ = Host.LoadOwnedGamesAsync());
                _groups.Children.Add(add);
                break;
            case "loading":
                AddNote("Getting your Steam library…");
                break;
            case "ready" when Host.OwnedRefreshing:
                AddNote("Checking Steam for changes\u2026");
                break;
            case "failed":
                AddNote("Steam didn't return your library. Check the API key in Settings, and that “Game details” " +
                        "in your Steam profile's privacy settings is not set to Private.");
                break;
            default:
                if (Host.InstallQueue.Count > 0)
                    AddNote($"Install queue: {string.Join(", ", Host.InstallQueue.Take(4).Select(g => g.Name))}" +
                            (Host.InstallQueue.Count > 4 ? $" and {Host.InstallQueue.Count - 4} more" : "") +
                            ". Steam asks you to confirm each one; the next starts when the one before has finished.");
                break;
        }
    }

    private void AddWishlistNotice()
    {
        switch (Host.WishlistState)
        {
            case "loading":
                AddNote("Getting your wishlist\u2026");
                break;
            case "failed":
                AddNote("Your wishlist couldn't be read. Steam needs to have been signed in on this PC, and \u201CGame details\u201D in your " +
                        "Steam profile's privacy settings must be Public (or add your Steam Web API key in Settings).");
                break;
            case "ready" when Host.WishlistRefreshing:
                AddNote("Checking Steam for changes\u2026");
                break;
        }
    }

    // ---------------------------------------------------------------- sidebar and toolbar

    private void BuildSidebar(List<Game> everything)
    {
        _sidebar.Children.Clear();

        bool blade = Collapsed;

        // First row: shrink to icons / open back up.
        var toggle = blade ? BladeRow("\uE76C", null, "Show the names") : Ui.Row("Show icons only", null, "\uE76B");
        toggle.Margin = blade ? new Thickness(0, 6, 0, 0) : new Thickness(0, 8, 6, 2);
        toggle.FontSize = 17;
        toggle.DataContext = "side:toggle";
        toggle.Click += (_, _) => ToggleSidebar();
        toggle.GotKeyboardFocus += (_, _) => _lastFocus = "side:toggle";
        _sidebar.Children.Add(toggle);

        void Section(string title)
        {
            if (!blade) { _sidebar.Children.Add(Ui.Header(title)); return; }
            var line = new System.Windows.Shapes.Rectangle { Height = 1, Margin = new Thickness(10, 12, 16, 10), Opacity = 0.2 };
            line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "FgBrush");
            _sidebar.Children.Add(line);
        }
        void Item(string filter, string glyph, int? count = null)
        {
            string label = FilterName(filter);
            // Stores, categories and your own lists all share one icon, so the blade shows their initials instead.
            bool initials = filter.StartsWith("src:") || filter.StartsWith("cat:") || filter.StartsWith("status:");
            // Stores with a logo show it instead.
            var logo = filter.StartsWith("src:") ? StoreLogos.For(filter[4..]) : null;
            if (logo != null) initials = false;
            var row = blade ? BladeRow(initials ? null : glyph, initials ? Initials(label) : null, label + (count != null ? $"  ({count})" : ""), _filter == filter, logo)
                            : logo != null ? Ui.Row(label, count?.ToString(), null, new Image { Source = logo })
                            : Ui.Row(label, count?.ToString(), glyph);
            row.Margin = blade ? new Thickness(0) : new Thickness(0, 2, 6, 2);
            row.FontSize = 17;
            string id = "side:" + filter;
            if (_filter == filter) row.Tag = "selected"; // drawn with the highlight color so you can see where you are
            row.Click += (_, _) =>
            {
                _filter = filter;
                _lastFocus = id;
                // Saved lists show at once and refresh in the background each time their section is picked.
                if (filter == "notinstalled") _ = Host.LoadOwnedGamesAsync(refresh: true);
                if (filter == "wishlist") _ = Host.LoadWishlistAsync(refresh: true);
                Rebuild();
            };
            row.GotKeyboardFocus += (_, _) => _lastFocus = id;
            row.DataContext = id;
            _sidebar.Children.Add(row);
        }

        var installed = everything.Where(g => g.IsInstalled).ToList();

        Section("Library");
        Item("all", "", installed.Count);
        Item("favorites", "", everything.Count(g => g.IsFavorite));
        Item("notinstalled", "", Host.OwnedGamesState == "ready" ? Host.NotInstalledGames.Count : null);
        Item("wishlist", "\uE7BF", Host.WishlistState == "ready" ? Host.WishlistGames.Count : null);
        Item("updates", "\uE777", installed.Count(g => g.HasUpdate));
        Item("collections", "\uE8F1", Host.Library.Collections.Count);

        var sources = installed.Select(g => g.SourceName).Distinct().OrderBy(s => s).ToList();
        if (sources.Count > 1)
        {
            Section("Stores");
            foreach (var s in sources) Item("src:" + s, "", installed.Count(g => g.SourceName == s));
        }

        var categories = Host.Categories;
        if (categories.Count > 0)
        {
            Section("Categories");
            foreach (var c in categories) Item("cat:" + c, "", everything.Count(g => Matches(g, "cat:" + c)));
        }

        var statuses = GameNotesScreen.Statuses.Where(st => everything.Any(g => Matches(g, "status:" + st))).ToList();
        bool anyRated = everything.Any(g => Matches(g, "rated"));
        if (statuses.Count > 0 || anyRated)
        {
            Section("Yours");
            foreach (var st in statuses) Item("status:" + st, "", everything.Count(g => Matches(g, "status:" + st)));
            if (anyRated) Item("rated", "", everything.Count(g => Matches(g, "rated")));
        }
        Ui.GroupRows(_sidebar); // sections read as rounded groups
    }

    /// <summary>A sidebar row in the icon-only blade: just the icon (or initials), the name as a tooltip.</summary>
    private static Button BladeRow(string? glyph, string? initials, string tip, bool selected = false, ImageSource? logo = null)
    {
        UIElement face;
        if (logo != null)
        {
            face = new Image { Source = logo, Width = 36, Height = 36, Margin = new Thickness(2), Stretch = Stretch.Uniform };
        }
        else if (initials != null)
        {
            var badge = new Grid { Width = 40, Height = 40 };
            var tint = new Border { CornerRadius = new CornerRadius(11), Opacity = selected ? 0.42 : 0.16 };
            tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            badge.Children.Add(tint);
            var text = Ui.Text(initials, 15, selected ? "FgBrush" : "AccentBrush", bold: true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            badge.Children.Add(text);
            face = badge;
        }
        else
        {
            var badge = Ui.IconBadge(glyph ?? "\uE8A9", 40);
            badge.Margin = new Thickness(0);
            if (selected && badge.Children[0] is Border tint) tint.Opacity = 0.42; // where you are, at a glance
            face = badge;
        }
        // A square icon button with a ring when highlighted (rows are built for text and squeezed the icon).
        var button = new Button { Style = Ui.StyleOf("TileButton"), Content = face, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Center };
        if (face is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        return button;
    }

    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        if (words.Count == 0) return "?";
        return words.Count == 1 ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                                : (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
    }

    private void BuildToolbar()
    {
        _toolbar.Children.Clear();
        _toolbar.Children.Add(_searchBar);
        AddTool("Sort: " + SortLabel, ChooseSort);
        AddTool("View: " + ViewLabel, ChooseView);
        AddTool("Layout: " + Layouts.First(l => l.Id == Layout).Name, ChooseLayout);
        if (SidebarHidden && Layout != "shelves") AddTool("Show: " + FilterName(_filter), ChooseSection);
        AddTool("Group by category: " + (_grouped ? "On" : "Off"), () => { _grouped = !_grouped; Rebuild(); });
        AddTool("+  Add custom game", () => Host.Push(new AddCustomGameScreen()));
        AddTool("Play time", () => Host.Push(new PlaytimeScreen()));
        AddTool("Achievements", () => Host.Push(new UnlocksScreen()));
        if (Host.InstallQueue.Count > 0) AddTool($"Install queue ({Host.InstallQueue.Count})", ShowQueue);
        _count.Margin = new Thickness(6, 0, 0, 6);
        _toolbar.Children.Add(_count);
    }

    private void AddTool(string label, Action click)
    {
        var button = Ui.PanelButton(label);
        button.FontSize = 16;
        button.Margin = new Thickness(0, 0, 6, 6);
        button.Click += (_, _) => click();
        string id = "tool:" + _toolbar.Children.Count;
        button.DataContext = id;
        button.GotKeyboardFocus += (_, _) => _lastFocus = id;
        _toolbar.Children.Add(button);
    }

    // ---------------------------------------------------------------- tiles

    private void AddGames(IEnumerable<Game> games, bool shelf = false)
    {
        // Shelves: one row that scrolls sideways. List: one game per row. Otherwise tiles that wrap.
        Panel panel = shelf ? new StackPanel { Orientation = Orientation.Horizontal } : Layout == "list" ? new StackPanel() : new WrapPanel();
        if (Layout == "list" && !shelf)
        {
            foreach (var game in games)
            {
                var g = game;
                var row = ListRow(g, out var rowTitle);
                row.Tag = g;
                row.Click += (_, _) => _ = Host.ActivateAsync(g);
                row.GotKeyboardFocus += (_, _) => { _lastFocus = g.Key; rowTitle.Active = true; };
                row.LostKeyboardFocus += (_, _) => rowTitle.Active = false;
                panel.Children.Add(row);
            }
            _groups.Children.Add(panel);
            return;
        }
        // Square: the cover as it is. Grid: tall 2:3 poster art. Rectangle: the wide 460×215 header, three to a row.
        string view = Host.Settings.LibraryView;
        double tileWidth = view == "header" ? TileSize * 2 + 4 : TileSize;
        double tileHeight = view == "grid" ? Math.Round(TileSize * 1.5) : view == "header" ? Math.Round(tileWidth * 215 / 460) : TileSize;
        foreach (var game in games)
        {
            var g = game;
            var box = GridScreen.ImageBox(tileWidth, tileHeight, out var cover);
            var face = (Grid)box.Child;

            // Shown until (or unless) there is a cover.
            var name = Ui.Text(g.Name, 16, "SubtleBrush", bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextAlignment = TextAlignment.Center;
            name.Margin = new Thickness(10);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            face.Children.Insert(0, name);
            // Bound, so covers that finish downloading later still appear.
            cover.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = g });
            // Not installed: the cover is dimmed with a shade on top (fading the picture itself let the name show through).
            if (!g.IsInstalled && !g.OnWishlist)
            {
                var shade = new Border { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), IsHitTestVisible = false };
                face.Children.Add(shade);
                face.Children.Add(new TextBlock
                {
                    Text = "\uE896", FontFamily = Ui.IconFont, FontSize = 26, Foreground = Brushes.White, Opacity = 0.9,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
                });
            }
            // Games with no square cover show their icon: SteamGridDB icons fill the tile, program icons sit centered.
            void ApplyFace()
            {
                bool icon = g.UsesIcon, fills = icon && g.IconFills;
                cover.Stretch = icon && !fills ? Stretch.Uniform : Stretch.UniformToFill;
                cover.Margin = icon && !fills ? new Thickness(Math.Min(tileWidth, tileHeight) * 0.24) : new Thickness(0);
                if (icon && g.TileBackground != null) box.Background = g.TileBackground;
                // The name is only a placeholder until there is a picture.
                name.Visibility = g.Cover == null ? Visibility.Visible : Visibility.Collapsed;
            }
            ApplyFace();
            System.ComponentModel.PropertyChangedEventManager.AddHandler(g, (_, _) => ApplyFace(), "");
            if (view != "square" && g.Actual.IsGame)
            {
                // Grid / Rectangle art goes on top of the square cover once it arrives (the cover shows meanwhile).
                var art = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
                RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
                face.Children.Insert(face.Children.IndexOf(cover) + 1, art);
                QueueArt(g, art, () => { art.Visibility = Visibility.Visible; name.Visibility = Visibility.Collapsed; });
            }
            AddBadges(face, g);

            var stack = new StackPanel { Width = tileWidth };
            stack.Children.Add(box);

            // Achievement progress, once it is known for the game.
            if (g.IsSteam && Host.Achievements.ProgressOf(g.AppId) is { } progress)
            {
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4, 0, 0) };
                track.SetResourceReference(Border.BackgroundProperty, "TileBrush");
                var fill = new Border
                {
                    Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                    Width = tileWidth * progress.Unlocked / progress.Total
                };
                fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var bar = new Grid { ToolTip = $"{progress.Unlocked} of {progress.Total} achievements" };
                bar.Children.Add(track);
                bar.Children.Add(fill);
                stack.Children.Add(bar);
            }

            // Long names slide along while the tile is highlighted, and end in "…" otherwise.
            var titleText = Ui.Text(g.Name, 15, "FgBrush", bold: true);
            var title = new MarqueeText(titleText, onlyWhenActive: true) { Margin = new Thickness(2, 6, 2, 0), Width = tileWidth - 4, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(title);

            // e.g. "Steam · Co-op · ★★★★☆ · 12/40"
            var mine = Host.Library.Find(g.Key);
            var facts = new List<string>
            {
                g.OnWishlist ? (g.Price.Length == 0 ? "Wishlist" : g.DiscountPercent > 0 ? $"{g.Price}  -{g.DiscountPercent}%" : g.Price)
                    : g.IsInstalled ? g.SourceName : "Not installed"
            };
            if (Host.CategoryOf(g) is string category) facts.Add(category);
            if (mine is { Rating: > 0 }) facts.Add(Ui.Stars(mine.Rating));
            if (!string.IsNullOrWhiteSpace(mine?.Status)) facts.Add(mine.Status);
            var sub = Ui.Text(string.Join("  ·  ", facts), 13, "SubtleBrush");
            sub.Margin = new Thickness(2, 1, 2, 2);
            stack.Children.Add(sub);

            var tile = Ui.Tile(stack);
            tile.Tag = g;
            tile.Margin = new Thickness(0, 0, 4, 6);
            tile.Click += (_, _) => _ = Host.ActivateAsync(g);
            tile.GotKeyboardFocus += (_, _) => { _lastFocus = g.Key; title.Active = true; ShowSpotlight(g); };
            tile.LostKeyboardFocus += (_, _) => title.Active = false;
            tile.MouseEnter += (_, _) => title.Active = true;
            tile.MouseLeave += (_, _) => title.Active = tile.IsKeyboardFocused;
            panel.Children.Add(tile);
        }
        if (shelf)
            _groups.Children.Add(new ScrollViewer
            {
                Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.HorizontalOnly, Focusable = false, Margin = new Thickness(0, 0, 0, 8)
            });
        else _groups.Children.Add(panel);
        LoadArt();
    }

    /// <summary>Small markers in the corners of a cover: running, update waiting.</summary>
    private static void AddBadges(Grid cover, Game g)
    {
        string? label = g.IsRunning ? "RUNNING" : g.HasUpdate ? "UPDATE" : null;
        if (label == null) return;
        cover.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6),
            Background = new SolidColorBrush(g.IsRunning ? Color.FromRgb(0x2E, 0xB8, 0x72) : Color.FromRgb(0x2B, 0x7D, 0xE0)),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
        });
    }

    // ---------------------------------------------------------------- focus

    private List<Button> Tiles() =>
        _groups.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Button>()).ToList();

    private IEnumerable<Button> AllButtons() =>
        Tiles().Concat(_toolbar.Children.OfType<Button>()).Concat(_sidebar.Children.OfType<Button>())
            .Concat(_groups.Children.OfType<Button>());

    private void RestoreFocus()
    {
        // Typing in the search box: leave the focus there while the results change underneath.
        if (_searchInput.IsKeyboardFocusWithin) return;
        if (_lastFocus == "search") { FocusLater(_searchInput); return; }
        var target = AllButtons().FirstOrDefault(b =>
            b.Tag is Game g ? g.Key == _lastFocus : b.DataContext as string == _lastFocus);
        FocusLater(target ?? Tiles().FirstOrDefault() ?? _sidebar.Children.OfType<Button>().FirstOrDefault());
    }

    public override void FocusDefault() =>
        (Tiles().FirstOrDefault() ?? _sidebar.Children.OfType<Button>().FirstOrDefault())?.Focus();

    public override bool OnAction(NavAction action)
    {
        if (_searchInput.IsKeyboardFocused)
        {
            switch (action)
            {
                case NavAction.Accept:
                    // Enter on a keyboard goes to the results; A on a controller opens the on-screen keyboard.
                    if (Hints.Keyboard) (Tiles().FirstOrDefault() as UIElement ?? _sidebar.Children.OfType<Button>().FirstOrDefault())?.Focus();
                    else _ = TypeSearchAsync();
                    return true;
                case NavAction.Back when _searchInput.Text.Length > 0:
                    _searchInput.Text = "";
                    return true;
            }
        }
        if (action == NavAction.View)
        {
            ToggleSidebar();
            return true;
        }
        if (action is NavAction.LT or NavAction.RT)
        {
            JumpByLetter(action == NavAction.RT ? +1 : -1);
            return true;
        }
        // Inside a collection, Back returns to the list of collections before leaving the library.
        if (action == NavAction.Back && _filter.StartsWith("col:"))
        {
            _lastFocus = "col-tile:" + _filter[4..];
            _filter = "collections";
            Rebuild();
            return true;
        }
        if (action is not (NavAction.X or NavAction.Y)) return false;
        if (System.Windows.Input.Keyboard.FocusedElement is Button { Tag: GameCollection collection })
        {
            if (action == NavAction.Y) _ = CollectionOptionsAsync(collection);
            return true;
        }
        if (System.Windows.Input.Keyboard.FocusedElement is Button { Tag: Game game })
        {
            if (action == NavAction.Y) _ = Host.OpenOptionsAsync(game);
            else if (game.IsGame) Host.Push(new DetailsScreen(game));
        }
        return true;
    }

    private static char Initial(Game g)
    {
        char c = g.Name.Length > 0 ? char.ToUpperInvariant(g.Name[0]) : '#';
        return char.IsLetter(c) ? c : '#';
    }

    /// <summary>Moves the highlight to the first tile of the next (or previous) starting letter.</summary>
    private void JumpByLetter(int direction)
    {
        var tiles = Tiles();
        if (tiles.Count == 0) return;
        int current = tiles.FindIndex(t => t.IsKeyboardFocused);
        if (current < 0) { tiles[0].Focus(); return; }

        char letter = Initial((Game)tiles[current].Tag);
        int i = current;
        if (direction > 0)
        {
            while (i < tiles.Count && Initial((Game)tiles[i].Tag) == letter) i++;
            if (i >= tiles.Count) i = 0; // wrap to the start
        }
        else
        {
            // Back to the start of this letter's run, then to the start of the run before it.
            while (i > 0 && Initial((Game)tiles[i - 1].Tag) == letter) i--;
            if (i == current)
            {
                i = i == 0 ? tiles.Count - 1 : i - 1;
                char before = Initial((Game)tiles[i].Tag);
                while (i > 0 && Initial((Game)tiles[i - 1].Tag) == before) i--;
            }
        }
        tiles[i].Focus();
        Host.Rumble();
    }

    // ---------------------------------------------------------------- toolbar actions

    /// <summary>Pick one of the six layouts (also in Settings > Games and library).</summary>
    private async void ChooseLayout()
    {
        int choice = await Host.AskAsync("Library layout", string.Join("\n", Layouts.Select(l => $"{l.Name}: {l.About}")), Layouts.Select(l => l.Name).ToArray());
        if (choice < 0 || Layouts[choice].Id == Layout) return;
        Host.Settings.LibraryLayout = Layouts[choice].Id;
        Host.Settings.Save();
        if (Layout == "shelves") _filter = "all";
        SizeSidebar(animate: false);
        Rebuild();
    }

    /// <summary>Without a sidebar: the sections in a list (the main ones, then each store).</summary>
    private async void ChooseSection()
    {
        var everything = Host.AllGames.Where(g => g.IsGame && g.IsInstalled);
        var filters = new List<string> { "all", "favorites", "installed", "notinstalled", "wishlist", "collections", "updates", "rated" };
        filters.AddRange(everything.Select(g => g.SourceName).Distinct().OrderBy(x => x).Select(x => "src:" + x));
        filters.AddRange(Host.Library.Collections.Select(c => "col:" + c.Id));
        var menu = new MenuScreen("Show", null, filters.Select(FilterName).ToArray());
        Host.Push(menu);
        int choice = await menu.Result;
        if (choice < 0 || choice >= filters.Count) return;
        _filter = filters[choice];
        _lastFocus = null;
        Rebuild();
    }

    /// <summary>Spotlight: the highlighted game large across the top: its cover over a blur of itself, name and facts.</summary>
    private void ShowSpotlight(Game g)
    {
        if (Layout != "spotlight") return;
        var grid = new Grid { ClipToBounds = true };
        double corner = Math.Max(Theme.TileCorner + 6, 8);
        grid.SizeChanged += (_, _) => grid.Clip = new RectangleGeometry(new Rect(grid.RenderSize), corner, corner);
        if (g.Cover != null)
        {
            var back = new Image { Source = g.Cover, Stretch = Stretch.UniformToFill, Opacity = 0.55, Margin = new Thickness(-60) };
            if (!Perf.Active) back.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 50 };
            grid.Children.Add(back);
        }
        var shade = new LinearGradientBrush(Color.FromArgb(0xD0, 0, 0, 0), Color.FromArgb(0x30, 0, 0, 0), 0);
        grid.Children.Add(new Border { Background = shade });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        var art = new Border { Width = 200, Height = 200, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner + 2, 6)), Margin = new Thickness(0, 0, 26, 0),
                               Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 } };
        if (g.Cover != null) art.Background = new ImageBrush(g.Cover) { Stretch = g.UsesIcon && !g.IconFills ? Stretch.Uniform : Stretch.UniformToFill };
        else art.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        row.Children.Add(art);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 900 };
        words.Children.Add(new TextBlock { Text = g.Name, FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        double minutes = g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : Host.Playtime.TotalSeconds(g.Key) / 60;
        var facts = new List<string> { g.IsInstalled ? g.SourceName : g.OnWishlist ? "Wishlist" : "Not installed" };
        if (minutes >= 1) facts.Add(minutes >= 60 ? $"{minutes / 60:0.#} hours played" : $"{minutes:0} minutes played");
        if (g.RecentAt > DateTime.MinValue.AddYears(1) && g.RecentAt < DateTime.Now.AddDays(1)) facts.Add("Last played " + Ui.Ago(g.RecentAt));
        if (Host.CategoryOf(g) is string category) facts.Add(category);
        words.Children.Add(new TextBlock { Text = string.Join("   ·   ", facts), FontSize = 17, Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 8, 0, 0) });
        var hint = new HintBar("{Select}  Play      {Details}  Details") { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        words.Children.Add(hint);
        row.Children.Add(words);
        grid.Children.Add(row);
        _spotlight.Child = grid;
    }

    /// <summary>List layout: one row per game, its cover small on the left and its facts across.</summary>
    private Button ListRow(Game g, out MarqueeText title)
    {
        var grid = new Grid { Height = 74 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var box = GridScreen.ImageBox(62, 62, out var cover);
        cover.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = g });
        if (g.UsesIcon && !g.IconFills) { cover.Stretch = Stretch.Uniform; cover.Margin = new Thickness(12); if (g.TileBackground != null) box.Background = g.TileBackground; }
        box.VerticalAlignment = VerticalAlignment.Center;
        if (!g.IsInstalled && !g.OnWishlist) box.Opacity = 0.5;
        grid.Children.Add(box);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
        var name = Ui.Text(g.Name, 17, "FgBrush", bold: true);
        title = new MarqueeText(name, onlyWhenActive: true);
        words.Children.Add(title);
        var mine = Host.Library.Find(g.Key);
        var line = new List<string> { g.IsInstalled ? g.SourceName : g.OnWishlist ? "Wishlist" : "Not installed" };
        if (Host.CategoryOf(g) is string category) line.Add(category);
        if (mine is { Rating: > 0 }) line.Add(Ui.Stars(mine.Rating));
        if (!string.IsNullOrWhiteSpace(mine?.Status)) line.Add(mine.Status);
        words.Children.Add(Ui.Text(string.Join("  ·  ", line), 13, "SubtleBrush"));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        double minutes = g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : Host.Playtime.TotalSeconds(g.Key) / 60;
        TextBlock Cell(string text, int column)
        {
            var t = Ui.Text(text, 14, "SubtleBrush");
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
            return t;
        }
        Cell(minutes >= 60 ? $"{minutes / 60:0.#} h played" : minutes >= 1 ? $"{minutes:0} min played" : "", 2);
        Cell(g.RecentAt > DateTime.MinValue.AddYears(1) && g.RecentAt < DateTime.Now.AddDays(1) ? Ui.Ago(g.RecentAt) : "", 3);
        Cell(g.SizeOnDisk > 0 ? $"{g.SizeOnDisk / 1073741824.0:0.#} GB" : "", 4);
        var card = new Border { Child = grid, Padding = new Thickness(8, 4, 12, 4), CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 8)) };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var button = Ui.Tile(card);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 4, 2);
        return button;
    }

    private string ViewLabel => Host.Settings.LibraryView switch { "grid" => "Grid", "header" => "Rectangle", _ => "Square" };

    /// <summary>How the games look: square covers, tall grid art, or wide header capsules.</summary>
    private async void ChooseView()
    {
        int choice = await Host.AskAsync("View",
            "Square shows each game's cover as it is. Grid uses tall poster art, Rectangle the wide banner from the store page.",
            "Square", "Grid", "Rectangle");
        if (choice < 0) return;
        string view = choice switch { 1 => "grid", 2 => "header", _ => "square" };
        if (view == Host.Settings.LibraryView) return;
        Host.Settings.LibraryView = view;
        Host.Settings.Save();
        Rebuild();
    }

    // ---- art for the Grid and Rectangle views, fetched a few at a time and kept for the session

    private static readonly Dictionary<string, ImageSource?> HeaderArt = new();

    /// <summary>Forgets the wide and tall art fetched for the library (it's read again from disk when the page opens).</summary>
    public static void ForgetArt() => HeaderArt.Clear();
    private CancellationTokenSource? _artCts;
    private readonly List<(Game Game, Image Picture, Action Shown)> _artQueue = new();

    private void QueueArt(Game game, Image picture, Action shown) => _artQueue.Add((game, picture, shown));

    private async void LoadArt()
    {
        if (_artQueue.Count == 0) return;
        var cts = _artCts ??= new CancellationTokenSource();
        var jobs = _artQueue.ToList();
        _artQueue.Clear();
        bool header = Host.Settings.LibraryView == "header";
        using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
        using var gate = new SemaphoreSlim(3);
        var work = jobs.Select(async job =>
        {
            var actual = job.Game.Actual;
            ImageSource? known = header ? (HeaderArt.TryGetValue(actual.Key, out var h) ? h : null) : actual.PortraitCover;
            bool tried = header ? HeaderArt.ContainsKey(actual.Key) : actual.PortraitTried;
            if (known == null && !tried)
            {
                await gate.WaitAsync();
                try
                {
                    if (cts.IsCancellationRequested) return;
                    known = await Task.Run(async () =>
                    {
                        var path = header ? await client.GetHeaderAsync(actual, cts.Token) : await client.GetPortraitAsync(actual, cts.Token);
                        return path == null ? null : (ImageSource?)ImageLoader.FromFile(path, header ? 520 : 360);
                    }, cts.Token);
                }
                catch (Exception) { known = null; }
                finally { gate.Release(); }
                if (cts.IsCancellationRequested) return;
                if (header) HeaderArt[actual.Key] = known;
                else { actual.PortraitTried = true; if (known != null) actual.PortraitCover = known; }
            }
            if (known != null && !cts.IsCancellationRequested)
            {
                job.Picture.Source = known;
                job.Shown();
            }
        });
        try { await Task.WhenAll(work); } catch (Exception) { }
    }

    private async void ChooseSort()
    {
        int choice = await Host.AskAsync("Sort by",
            "Jump by letter (LT / RT) is most useful when sorted by name.",
            "Recently played", "Name", "Time played", "Size on disk", "My rating");
        if (choice < 0) return;
        _sort = choice switch { 1 => "name", 2 => "playtime", 3 => "size", 4 => "rating", _ => "recent" };
        Rebuild();
    }


    private async void ShowQueue()
    {
        var queue = Host.InstallQueue;
        int choice = await Host.AskAsync("Install queue",
            string.Join("\n", queue.Select((g, i) => $"{i + 1}. {g.Name}")), "Close", "Clear the queue");
        if (choice == 1) Host.ClearInstallQueue();
        Rebuild();
    }
}

/// <summary>Add any program as a game: title, what to run, and a square image.</summary>
public sealed class AddCustomGameScreen : ListScreen
{
    private readonly string _id = Guid.NewGuid().ToString("N");
    private string _title = "";
    private string _program = "";
    private string? _cover;
    private bool _added;

    public AddCustomGameScreen() : base("Add custom game") { }

    private Game Draft => new() { Kind = ItemKind.Custom, LaunchId = _id, Name = _title, OriginalName = _title };

    protected override void Build()
    {
        AddHeader("Game");
        AddRow("Title", _title.Length > 0 ? _title : "Not set", "", click: EditTitle);
        AddRow("Game program", _program.Length > 0 ? Path.GetFileName(_program) : "Not set", "", click: ChooseProgram);

        AddHeader("Square image (1:1)");
        AddRow("Custom: choose an image file…", glyph: "", click: ChooseImageFile);
        AddRow("Download: pick one from SteamGridDB…", glyph: "", click: Download);

        if (_cover != null)
        {
            var preview = new Image
            {
                Source = ImageLoader.FromFile(_cover, 320),
                Stretch = Stretch.UniformToFill
            };
            var box = new Border
            {
                Width = 150, Height = 150, ClipToBounds = true, Child = preview,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 8, 0, 8)
            };
            Rows.Children.Add(box);
        }
        else
        {
            AddNote("No image chosen yet. The tile will show the title instead.");
        }

        AddHeader("Finish");
        AddRow("Add to library", glyph: "", click: Finish);
    }

    private async void EditTitle()
    {
        var text = await Host.PromptAsync("Game title", null, _title, "OK");
        if (text == null) return;
        _title = text.Trim();
        Rebuild();
    }

    private async void ChooseProgram()
    {
        var picked = await Host.PickFileAsync("Choose the game's program", new[] { ".exe", ".lnk", ".bat", ".cmd", ".url" });
        if (picked == null) return;
        _program = picked;
        if (_title.Length == 0) _title = Path.GetFileNameWithoutExtension(_program);
        Rebuild();
    }

    private async void ChooseImageFile()
    {
        var picked = await Host.PickFileAsync("Choose an image (square works best)", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            Directory.CreateDirectory(SteamGridDbClient.CustomDir);
            var dest = Path.Combine(SteamGridDbClient.CustomDir,
                "custom_" + _id + "_" + DateTime.Now.Ticks + Path.GetExtension(picked));
            File.Copy(picked, dest, overwrite: true);
            SetCover(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }

    private async void Download()
    {
        if (_title.Length == 0)
        {
            await Host.AskAsync("Enter the title first", "SteamGridDB is searched by the game's title.", "OK");
            return;
        }
        Host.Push(new CoverPickerScreen(Draft, SetCover));
    }

    private void SetCover(string? path)
    {
        DeleteCover();
        _cover = path;
        Rebuild();
    }

    private void DeleteCover()
    {
        if (_cover == null) return;
        try { File.Delete(_cover); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        _cover = null;
    }

    private async void Finish()
    {
        if (_title.Length == 0 || _program.Length == 0)
        {
            await Host.AskAsync("Almost there", "A custom game needs a title and the program to start.", "OK");
            return;
        }
        _added = true;
        Host.AddCustomGame(_id, _title, _program, _cover);
        Host.Pop(this);
    }

    public override void OnClosed()
    {
        // Backed out without adding: don't leave the picked image behind.
        if (!_added) DeleteCover();
    }
}
