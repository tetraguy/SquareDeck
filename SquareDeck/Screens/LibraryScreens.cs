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

    private string KindName => _kind switch { "heroes" => "hero image", "logos" => "logo", "headers" => "header capsule", _ => "tall grid" };

    public ArtPickerScreen(Game item, string kind)
        : base("Artwork – " + item.Name, ArtTabs.Hints)
    {
        _item = item;
        _kind = kind;
        (_w, _h) = kind switch { "heroes" => (372.0, 120.0), "logos" => (276.0, 138.0), "headers" => (322.0, 151.0), _ => (148.0, 222.0) };
        _note = Ui.Text("Loading images from SteamGridDB…", 17, "SubtleBrush");
        _note.Margin = new Thickness(4, 0, 0, 10);
        Body.Children.Insert(0, _note); // above the images, where it's seen
        Body.Children.Insert(0, ArtTabs.Bar(item, kind, tab => ArtTabs.Switch(Host, this, item, tab)));

        bool hasPick = SteamGridDbClient.FindChosenArt(item, kind) != null;
        AddTile(GlyphBox(_w, _h, ""), "Choose a file…", _w, Browse);
        AddTile(GlyphBox(_w, _h, ""), hasPick ? "Use the default" : "Default (in use)", _w, () => Finish(null));
    }

    public override void OnShown() => _ = LoadAsync();

    public override void OnClosed() => _cts.Cancel();

    public override bool OnAction(NavAction action) => ArtTabs.Step(action, Host, this, _item, _kind);

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
                AddTile(box, null, _w, () => Pick(img.Url, img.Thumb, view.Source));
                loads.Add(LoadThumbAsync(view, img.Thumb, _kind is "heroes" or "headers" ? 480 : 300, gate, ct));
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

    private async void Pick(string url, string thumb, ImageSource? preview)
    {
        if (_busy) return;
        _busy = true;
        var sheet = new DownloadSheet(preview, _w, _h, $"Downloading {KindName}…");
        ShowOver(sheet);
        var progress = new Progress<(long Done, long? Total)>(p => sheet.Report(p.Total is > 0 ? (double)p.Done / p.Total.Value : 0, p.Done, p.Total));
        var retrying = new Progress<int>(n => sheet.Status($"The connection dropped. Trying again ({n} of 3)…"));
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
                    var file = await client.DownloadWithRetryAsync(address, null, dest, CancellationToken.None, progress, retrying);
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

        if (path != null)
        {
            sheet.Status("Applying…");
            await Task.Delay(60);
            if (Save(path))
            {
                Host.RefreshArt(_item, _kind);
                await sheet.DoneAsync("Applied", $"The new {KindName} is now used for {_item.Name}.");
                Host.Pop(this);
                return;
            }
        }
        await sheet.FailedAsync("Check the internet connection, or pick another image.");
        await sheet.HideAsync();
        _busy = false;
        FocusDefault();
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
    private readonly string? _tab; // "covers" or "icons" when shown as a tab of the artwork page
    private bool _busy;

    /// <param name="picked">If given, receives the chosen file instead of it being saved for the tile.</param>
    /// <param name="tab">For games: which tab of the artwork page this is ("covers" or "icons").</param>
    public CoverPickerScreen(Game item, Action<string?>? picked = null, string tab = "covers")
        : base((picked == null && item.IsGame ? "Artwork – " : "Choose image – ") + item.Name, picked == null && item.IsGame ? ArtTabs.Hints : DefaultHints)
    {
        _item = item;
        _picked = picked;
        _tab = picked == null && item.IsGame ? tab : null;
        _note = Ui.Text("Loading images from SteamGridDB…", 17, "SubtleBrush");
        _note.Margin = new Thickness(4, 0, 0, 10);
        Body.Children.Insert(0, _note); // above the images, where it's seen
        if (_tab != null) Body.Children.Insert(0, ArtTabs.Bar(item, _tab, t => ArtTabs.Switch(Host, this, item, t)));

        AddTile(GlyphBox(TileSize, TileSize, ""), "Choose a file…", TileSize, Browse);
        AddTile(GlyphBox(TileSize, TileSize, ""), "Use the default", TileSize, () => Finish(null));
    }

    public override bool OnAction(NavAction action) => _tab != null && ArtTabs.Step(action, Host, this, _item, _tab);

    public override void OnShown()
    {
        if (_tab == "icons") { _note.Text = "Loading icons from SteamGridDB…"; _ = LoadIconsAsync(); }
        else _ = LoadAsync();
    }

    public override void OnClosed() => _cts.Cancel();

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            using var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey);
            var images = await Task.Run(() => client.ListSquareAsync(_item, ct), ct);
            if (ct.IsCancellationRequested) return;

            _note.Text = images.Count == 0 ? (_tab != null ? "SteamGridDB has no square images for this title. Try the Icons tab, or choose a file." : "SteamGridDB has no square images for this title, so here are its icons instead.") : "";
            if (images.Count == 0) { if (_tab == null) await LoadIconsAsync(); return; }
            if (_tab == null) AddTile(GlyphBox(TileSize, TileSize, "\uE8B9"), "Search icons instead", TileSize, () => _ = LoadIconsAsync());
            using var gate = new SemaphoreSlim(6);
            var loads = new List<Task>();
            foreach (var image in images.Take(60))
            {
                var img = image;
                var box = ImageBox(TileSize, TileSize, out var view);
                AddTile(box, null, TileSize, () => PickRemote(img.Url, img.Thumb, view.Source));
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
            _note.Text = "";
            var header = Ui.Text("Icons", 20, "FgBrush", bold: true);
            header.Margin = new Thickness(4, 18, 0, 8);
            // As its own tab, the icons sit in the main grid, after Choose a file and Use the default.
            Panel panel = _tab == "icons" ? Tiles : new WrapPanel();
            if (_tab != "icons") { Body.Children.Add(header); Body.Children.Add(panel); }
            using var gate = new SemaphoreSlim(6);
            var loads = new List<Task>();
            Button? first = null;
            foreach (var icon in icons.Take(60))
            {
                var ic = icon;
                var box = ImageBox(TileSize, TileSize, out var view);
                view.Stretch = Stretch.Uniform;
                view.Margin = new Thickness(TileSize * 0.2);
                var tile = AddTile(box, null, TileSize, () => PickRemote(ic.Url, ic.Thumb, view.Source, asIcon: true), null, panel);
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

    private async void PickRemote(string url, string thumb, ImageSource? preview, bool asIcon = false)
    {
        if (_busy) return;
        _busy = true;
        var what = asIcon ? "icon" : "cover";
        var sheet = new DownloadSheet(preview, TileSize, TileSize, $"Downloading {what}\u2026");
        ShowOver(sheet);
        var progress = new Progress<(long Done, long? Total)>(p => sheet.Report(p.Total is > 0 ? (double)p.Done / p.Total.Value : 0, p.Done, p.Total));
        var retrying = new Progress<int>(n => sheet.Status($"The connection dropped. Trying again ({n} of 3)\u2026"));
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
                    var file = await client.DownloadWithRetryAsync(address, null, dest, CancellationToken.None, progress, retrying);
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

        if (path != null)
        {
            sheet.Status("Applying\u2026");
            await Task.Delay(60);
            if (_picked != null) _picked(path);
            else Host.SetCustomCover(_item, path);
            await sheet.DoneAsync("Applied", $"The new {what} is now used for {_item.Name}.");
            Host.Pop(this);
            return;
        }
        await sheet.FailedAsync("Check the internet connection, or pick another image.");
        await sheet.HideAsync();
        _busy = false;
        FocusDefault();
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
/// The artwork page's tabs: one per kind of image a game can have. LB / RB (or the tabs themselves) switch between them;
/// each tab is its own picker page, swapped in place.
/// </summary>
public static class ArtTabs
{
    public const string Hints = "{Select}  Use        {LB}{RB}  Switch tabs        {Back}  Back";

    public static readonly (string Id, string Name, string Glyph)[] Tabs =
    {
        ("covers", "Square covers", ""), ("icons", "Icons", ""), ("grids", "Tall grids", ""),
        ("headers", "Header capsules", ""), ("heroes", "Hero images", ""), ("logos", "Logos", ""),
    };

    public static Screen Create(Game item, string tab) => tab switch
    {
        "covers" or "icons" => new CoverPickerScreen(item, null, tab),
        _ => new ArtPickerScreen(item, tab)
    };

    /// <summary>A row of tabs with the current one filled in, and a line saying where that kind of image is used.</summary>
    public static FrameworkElement Bar(Game item, string active, Action<string> go)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var bar = new Border { CornerRadius = new CornerRadius(30), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left };
        bar.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var row = new WrapPanel();
        foreach (var (id, name, glyph) in Tabs)
        {
            bool on = id == active;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
            line.Children.Add(new TextBlock { Text = name, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal });
            var tab = new Button { Style = Ui.StyleOf("PillButton"), Content = line, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 4, 0), Tag = "tab:" + id };
            if (on) { tab.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); tab.SetResourceReference(Control.ForegroundProperty, "BgBrush"); }
            else tab.Background = Brushes.Transparent;
            string target = id;
            tab.Click += (_, _) => { if (target != active) go(target); };
            row.Children.Add(tab);
        }
        bar.Child = row;
        outer.Children.Add(bar);
        var what = Ui.Text(active switch
        {
            "covers" => "The square picture on the home row, Favorites and Apps.",
            "icons" => "A game icon shown centered on a color wash, used instead of a square cover.",
            "grids" => "Tall 2:3 poster art, for tall tiles and poster layouts.",
            "headers" => "Wide 460×215 banners, for rectangle tiles and Showcase.",
            "heroes" => "The big background picture behind a game's page and the home screen.",
            _ => "The game's name as artwork, shown over hero images."
        }, 14, "SubtleBrush");
        what.Margin = new Thickness(8, 10, 0, 0);
        outer.Children.Add(what);
        return outer;
    }

    /// <summary>Swaps the current tab's page for another's.</summary>
    public static void Switch(MainWindow host, Screen current, Game item, string tab)
    {
        var next = Create(item, tab);
        host.Push(next);
        host.RemoveBelow(current);
    }

    /// <summary>LB / RB: the tab to the left or right.</summary>
    public static bool Step(NavAction action, MainWindow host, Screen current, Game item, string active)
    {
        if (action is not (NavAction.LB or NavAction.RB)) return false;
        int index = Array.FindIndex(Tabs, t => t.Id == active);
        int next = (index + (action == NavAction.RB ? 1 : -1) + Tabs.Length) % Tabs.Length;
        UiSound.Select();
        Switch(host, current, item, Tabs[next].Id);
        return true;
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
    // How tall the artwork at the top is, and how the page is laid out: both come from the look in use
    // (Settings > Themes; see PageDesign.Details for the layouts).
    private readonly double HeroHeight = Looks.Current.Design.HeroHeight;
    private readonly string _layout = Looks.Current.Design.Details;
    private readonly string _statsLook = Looks.Current.Design.Stats;
    private HorizontalAlignment _logoAlign = HorizontalAlignment.Right;
    private double _logoHeight = 150, _logoWidth = 380;
    private bool _logoReplacesTitle;
    private Image? _media; // the big picture beside the details card (Solaris)
    private const double CardRadius = 12;

    private readonly Game _game;
    private readonly CancellationTokenSource _cts = new();

    // The artwork banner behind the top of the page, which fades as the page scrolls.
    private readonly Grid _hero = new() { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
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
    private readonly StackPanel _hltb = new();
    private HltbTimes? _hltbTimes;
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
        RumbleOnMove(); // a light tick as the highlight moves from item to item (Vibrate while moving through menus)

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

        _hero.Height = HeroHeight;
        _description.TextWrapping = TextWrapping.Wrap;
        _description.TextTrimming = TextTrimming.None;
        _description.LineHeight = 27;
        var descriptionCard = Card(_description, new Thickness(24, 20, 24, 22));
        var factsCard = Card(_facts, new Thickness(22, 18, 22, 18));
        factsCard.VerticalAlignment = VerticalAlignment.Top;

        // ---- the top of the page, as the look lays it out
        bool descriptionPlaced = false;
        switch (_layout)
        {
            case "showcase": ArrangeShowcase(title, side: false); break;
            case "sidepanel": ArrangeShowcase(title, side: true); break;
            case "banner": ArrangeBanner(title); break;
            case "panels": ArrangePanels(title, descriptionCard); descriptionPlaced = true; break;
            case "split": ArrangeSplit(title); break;
            case "dash": ArrangeDash(title); break;
            case "orbit": ArrangeOrbit(title); break;
            default: ArrangeSwitch(); break;
        }
        _page.Children.Add(_hltb);
        _page.Children.Add(_notes);
        AddScreenshots();

        // ---- about: description beside a column of facts (the facts alone where the description is up top)
        _page.Children.Add(SectionTitle("About"));
        if (descriptionPlaced) _page.Children.Add(factsCard);
        else
        {
            var about = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            about.Children.Add(descriptionCard);
            Grid.SetColumn(factsCard, 2);
            about.Children.Add(factsCard);
            _page.Children.Add(about);
        }
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

    // ---------------------------------------------------------------- the looks' layouts

    /// <summary>Switch: the cover, name and logo in a banner; actions, then cards, under it.</summary>
    private void ArrangeSwitch()
    {
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
    }

    /// <summary>Puts the game's logo on the left, large, in place of its name once it has loaded.</summary>
    private void LogoOnLeft(double height, double width)
    {
        _logoAlign = HorizontalAlignment.Left;
        _logoHeight = height;
        _logoWidth = width;
        _logoReplacesTitle = true;
        _logoHost.HorizontalAlignment = HorizontalAlignment.Left;
        _logoHost.VerticalAlignment = VerticalAlignment.Top;
        _logoHost.Margin = new Thickness(0, 0, 0, 14);
    }

    /// <summary>
    /// ubiquity and Aniki: the artwork fills the screen. The logo, details, numbers and actions sit on the left over
    /// it; with <paramref name="side"/> (Aniki) the numbers go in a panel on the right instead.
    /// </summary>
    private void ArrangeShowcase(TextBlock title, bool side)
    {
        LogoOnLeft(180, 560);
        _coverBox.Visibility = Visibility.Collapsed;
        var left = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        left.Children.Add(_logoHost);
        left.Children.Add(_titleHost);
        left.Children.Add(_meta);
        left.Children.Add(_badges);
        if (!side) left.Children.Add(_stats);
        left.Children.Add(_actions);
        left.Children.Add(_actionLabel);
        if (!side)
        {
            left.Margin = new Thickness(18, HeroHeight - 560, 0, 0);
            _page.Children.Add(left);
            return;
        }
        var grid = new Grid { Margin = new Thickness(18, HeroHeight - 560, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        _stats.Margin = new Thickness(0);
        _stats.Orientation = Orientation.Vertical;
        var panel = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 14, 18, 8), MinWidth = 280, Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x0C, 0x0F, 0x14)), Child = _stats
        };
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);
        _page.Children.Add(grid);
    }

    /// <summary>
    /// SquareDeck: the artwork as a banner with the logo in the middle of it, a play bar under it (Play and the other
    /// actions, the numbers on the right), then the cover beside the name and details.
    /// </summary>
    private void ArrangeBanner(TextBlock title)
    {
        _logoAlign = HorizontalAlignment.Center;
        _logoHeight = 200;
        _logoWidth = 640;
        _logoHost.HorizontalAlignment = HorizontalAlignment.Center;
        _logoHost.VerticalAlignment = VerticalAlignment.Center;
        _logoHost.Margin = new Thickness(0);
        var logoArea = new Grid { Height = Math.Max(160, HeroHeight - 110) };
        logoArea.Children.Add(_logoHost);
        _page.Children.Add(logoArea);

        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _actions.Margin = new Thickness(0);
        bar.Children.Add(_actions);
        _stats.Margin = new Thickness(0, 6, 0, 0);
        _stats.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_stats, 1);
        bar.Children.Add(_stats);
        var band = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(20, 16, 20, 6), Child = bar };
        band.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        _page.Children.Add(band);
        _page.Children.Add(_actionLabel);

        SizeCover(128);
        title.FontSize = 32;
        var info = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_titleHost);
        info.Children.Add(_meta);
        info.Children.Add(_badges);
        var row = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
        DockPanel.SetDock(_coverBox, Dock.Left);
        row.Children.Add(_coverBox);
        row.Children.Add(info);
        _page.Children.Add(row);
    }

    /// <summary>
    /// J Hero: the logo, details and tags at the top left over the artwork; under them, the numbers in panels with
    /// Play on the left, and the description on the right.
    /// </summary>
    private void ArrangePanels(TextBlock title, Border descriptionCard)
    {
        LogoOnLeft(160, 480);
        _coverBox.Visibility = Visibility.Collapsed;
        var top = new StackPanel { Margin = new Thickness(10, Math.Max(40, HeroHeight - 470), 0, 0) };
        top.Children.Add(_logoHost);
        top.Children.Add(_titleHost);
        top.Children.Add(_meta);
        top.Children.Add(_badges);
        _page.Children.Add(top);

        var grid = new Grid { Margin = new Thickness(0, 28, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        _stats.Margin = new Thickness(0);
        left.Children.Add(_stats);
        _actions.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_actions);
        left.Children.Add(_actionLabel);
        grid.Children.Add(left);
        descriptionCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(descriptionCard, 2);
        grid.Children.Add(descriptionCard);
        _page.Children.Add(grid);
    }

    /// <summary>
    /// Solaris: the logo large over the artwork; under it a card with the cover, name, details, numbers and Play,
    /// beside a big picture of the game.
    /// </summary>
    private void ArrangeSplit(TextBlock title)
    {
        LogoOnLeft(170, 520);
        _logoReplacesTitle = false; // the card has the name
        _logoHost.Margin = new Thickness(10, Math.Max(40, HeroHeight - 520), 0, 24);
        _page.Children.Add(_logoHost);

        SizeCover(150);
        title.FontSize = 28;
        var info = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        info.Children.Add(_titleHost);
        info.Children.Add(_meta);
        info.Children.Add(_badges);
        var head = new DockPanel();
        DockPanel.SetDock(_coverBox, Dock.Left);
        head.Children.Add(_coverBox);
        head.Children.Add(info);
        var inside = new StackPanel();
        inside.Children.Add(head);
        _stats.Margin = new Thickness(0, 18, 0, 0);
        inside.Children.Add(_stats);
        _actions.Margin = new Thickness(0, 10, 0, 0);
        inside.Children.Add(_actions);
        inside.Children.Add(_actionLabel);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 18, 20, 10), Child = inside };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");

        _media = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_media, BitmapScalingMode.HighQuality);
        var mediaBox = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Height = 380, VerticalAlignment = VerticalAlignment.Top, Child = _media };
        mediaBox.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        mediaBox.SizeChanged += (_, e) => mediaBox.Clip = new RectangleGeometry(new Rect(e.NewSize), 10, 10);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Star) });
        grid.Children.Add(card);
        Grid.SetColumn(mediaBox, 2);
        grid.Children.Add(mediaBox);
        _page.Children.Add(grid);
    }

    /// <summary>
    /// X Dash: a console game page. The logo (or name), details, Play and the other actions and the numbers on the
    /// left; the cover large on the right; the artwork across the whole top.
    /// </summary>
    private void ArrangeDash(TextBlock title)
    {
        LogoOnLeft(150, 520);
        title.FontSize = 46;
        title.FontWeight = FontWeights.SemiBold;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        left.Children.Add(_logoHost);
        left.Children.Add(_titleHost);
        left.Children.Add(_meta);
        left.Children.Add(_badges);
        left.Children.Add(_actions);
        left.Children.Add(_actionLabel);
        left.Children.Add(_stats);
        SizeCover(300);
        _coverBox.CornerRadius = new CornerRadius(4);
        _coverBox.VerticalAlignment = VerticalAlignment.Bottom;
        _coverBox.Margin = new Thickness(30, 0, 10, 0);
        var grid = new Grid { Margin = new Thickness(18, Math.Max(40, HeroHeight - 560), 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(_coverBox, 1);
        grid.Children.Add(_coverBox);
        _page.Children.Add(grid);
    }

    /// <summary>
    /// Orbit: the artwork fills the screen; the logo, details and a big Play near the bottom left; the numbers as
    /// large glass cards along the bottom.
    /// </summary>
    private void ArrangeOrbit(TextBlock title)
    {
        LogoOnLeft(170, 560);
        _coverBox.Visibility = Visibility.Collapsed;
        var block = new StackPanel { Margin = new Thickness(18, Math.Max(40, HeroHeight - 500), 0, 0) };
        block.Children.Add(_logoHost);
        block.Children.Add(_titleHost);
        block.Children.Add(_meta);
        block.Children.Add(_badges);
        block.Children.Add(_actions);
        block.Children.Add(_actionLabel);
        _stats.Margin = new Thickness(0, 36, 0, 0);
        block.Children.Add(_stats);
        _page.Children.Add(block);
    }

    /// <summary>Makes the cover a different size (it's square, with rounded corners).</summary>
    private void SizeCover(double size)
    {
        _coverBox.Width = _coverBox.Height = size;
        if (_coverBox.Child is Grid g) g.Clip = new RectangleGeometry(new Rect(0, 0, size, size), CardRadius, CardRadius);
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

        // Looks with the details over the artwork darken its left side more, so the text stays readable.
        bool over = _layout is "showcase" or "sidepanel" or "panels" or "dash" or "orbit";
        var side = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        side.GradientStops.Add(new GradientStop(Color.FromArgb(over ? (byte)0xE0 : (byte)0x99, bg.R, bg.G, bg.B), 0));
        side.GradientStops.Add(new GradientStop(Color.FromArgb(over ? (byte)0x80 : (byte)0x00, bg.R, bg.G, bg.B), over ? 0.45 : 0.6));
        side.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, bg.R, bg.G, bg.B), over ? 0.8 : 0.6));
        _hero.Children.Add(new Border { Background = side });
        SetPageHero(_hero);
    }

    private void ShowHero(ImageSource? picture)
    {
        if (picture == null || _heroImage.Source != null) return;
        _heroImage.Source = picture;
        if (_media != null && _media.Source == null) _media.Source = picture;
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
        _ = LoadHltbAsync();
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
        BuildHltb();  // play time may have grown
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
        var main = new Button { Style = Looks.PlayStyle(), Content = GlyphLabel(playGlyph, play, 20), Margin = new Thickness(0, 0, 14, 10) };
        main.Click += (_, _) => _ = Host.ActivateAsync(_game);
        Watch(main, "action:play", play);
        _actions.Children.Add(main);

        if (_game.IsRunning)
            Pill("", "Stop", "Stop game", async () =>
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
        // Experimental: Game Genie, questions and answers about the game by Google Gemini.
        if (Labs.On("gamegenie") && _game.IsGame)
            Pill("", "Ask Genie", "Questions and answers about this game, by Game Genie", () => Host.Push(new GameGenieScreen(_game, _details, _wiki)), wand: true);
        if (_details is { Trailers.Count: > 0 }) Pill("", "Trailer", "Watch the trailer", WatchTrailer);
        Pill("", "Notebook", "Notebook, rating and status", () => Host.Push(new GameNotesScreen(_game)));
        if (_storeId > 0) Pill("\uE719", "Store", _game.IsSteam ? "Open the Steam store page" : "Open its page on the Steam store", () => ProcessUtil.Open("steam://store/" + _storeId));
        if (_wiki?.Url is { Length: > 0 } wikiUrl) Pill("\uE774", "Wikipedia", "Read the full article", () => Host.Push(BrowserScreen.Open(Host, wikiUrl)));
        if (!_game.IsSteam && InstallFolder() is string dir)
            Pill("\uE838", "Files", "Open the install folder", () => Host.Push(new ExplorerScreen(dir)));
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

    private void Pill(string glyph, string label, string description, Action click, bool highlight = false, bool wand = false)
    {
        var content = GlyphLabel(glyph, label, 16);
        if (wand)
        {
            // A magic wand (the icon font has none), in the button's own text color like the other icons.
            var icon = GameGenieScreen.Wand(15, null);
            icon.Margin = new Thickness(0, 1, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            foreach (var path in ((Canvas)icon.Child).Children.OfType<System.Windows.Shapes.Path>())
                path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground")
                    { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            content.Children.RemoveAt(0);
            content.Children.Insert(0, icon);
        }
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
        // Experimental: played in a pop-up here, streams included, instead of the full-screen viewer or Steam's page.
        if (Labs.On("trailers") && _details is { Trailers.Count: > 0 } d && d.Trailers.Any(t => t.Url != null || t.Stream != null))
        {
            Host.Push(new TrailerScreen(_game.Name, d.Trailers.Where(t => t.Url != null || t.Stream != null).ToList()));
            return;
        }
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
            switch (_statsLook)
            {
                case "text":
                {
                    // The label small above the value, no card (ubiquity, SquareDeck, X Dash).
                    var col = new StackPanel { Margin = new Thickness(0, 0, 34, 12), MinWidth = 96 };
                    col.Children.Add(Line(label.ToUpperInvariant(), 12, "SubtleBrush", bold: true));
                    var v = Line(value, 21, "FgBrush", bold: true);
                    v.Margin = new Thickness(0, 3, 0, 0);
                    col.Children.Add(v);
                    if (fraction is double tf) { var tb = Bar(tf, 4); tb.Margin = new Thickness(0, 6, 0, 0); col.Children.Add(tb); }
                    _stats.Children.Add(col);
                    return;
                }
                case "list":
                {
                    // An icon, the label and the value on one row, in a column (Aniki's side panel).
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), MinWidth = 260 };
                    var ic = Ui.Glyph(glyph, 16, "FgBrush");
                    ic.Margin = new Thickness(0, 2, 12, 0);
                    DockPanel.SetDock(ic, Dock.Left);
                    row.Children.Add(ic);
                    var val = Line(value, 17, "FgBrush", bold: true);
                    val.Margin = new Thickness(16, 0, 0, 0);
                    DockPanel.SetDock(val, Dock.Right);
                    row.Children.Add(val);
                    row.Children.Add(Line(label, 15));
                    _stats.Children.Add(row);
                    return;
                }
                case "large":
                {
                    // Big glass cards along the bottom (Orbit).
                    var inner = new StackPanel();
                    inner.Children.Add(Line(label, 15));
                    var bv = Line(value, 30, "FgBrush", bold: true);
                    bv.Margin = new Thickness(0, 8, 0, 0);
                    inner.Children.Add(bv);
                    if (fraction is double lf) { var lb = Bar(lf, 6); lb.Margin = new Thickness(0, 12, 0, 0); inner.Children.Add(lb); }
                    var glass = new Border
                    {
                        Width = 250, MinHeight = 120, CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 14, 14),
                        Background = new SolidColorBrush(Color.FromArgb(0x99, 0x20, 0x22, 0x30)), Child = inner
                    };
                    _stats.Children.Add(glass);
                    return;
                }
            }
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

    /// <summary>
    /// HowLongToBeat's times, shown at once from what's kept and looked up (then kept) when there's nothing recent.
    /// Nothing is shown when the game isn't on the site or the setting is off.
    /// </summary>
    private async Task LoadHltbAsync()
    {
        if (!Host.Settings.ShowHowLongToBeat || !_game.IsGame) return;
        var (kept, known, _) = HowLongToBeat.Cached(_game);
        _hltbTimes = kept;
        BuildHltb(loading: !known);
        if (!HowLongToBeat.IsStale(_game)) return;
        try
        {
            var ct = _cts.Token;
            var times = await Task.Run(() => HowLongToBeat.GetAsync(_game, ct), ct);
            if (ct.IsCancellationRequested) return;
            _hltbTimes = times;
        }
        catch (Exception) { } // offline or the site changed: keep what was there (if anything)
        BuildHltb();
    }

    private void BuildHltb(bool loading = false)
    {
        _hltb.Children.Clear();
        if (!Host.Settings.ShowHowLongToBeat || !_game.IsGame) return;
        if (_hltbTimes is not { } t)
        {
            if (!loading) return;
            _hltb.Children.Add(SectionTitle("How long to beat"));
            _hltb.Children.Add(Line("Looking it up on HowLongToBeat…", 15));
            return;
        }

        var stack = new StackPanel();
        var row = new UniformGrid { Rows = 1 };
        double longest = new[] { t.Main, t.Extra, t.Complete }.Max();
        void Time(string label, double hours, string glyph)
        {
            if (hours <= 0) return;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            var head = GlyphLabel(glyph, label.ToUpperInvariant(), 13);
            head.Opacity = 0.85;
            cell.Children.Add(head);
            var value = Line(HowLongToBeat.Format(hours), 26, "FgBrush", bold: true);
            value.Margin = new Thickness(0, 6, 0, 8);
            cell.Children.Add(value);
            cell.Children.Add(Bar(longest > 0 ? hours / longest : 0, 5));
            row.Children.Add(cell);
        }
        Time("Main story", t.Main, "\uE768");
        Time("Main + extras", t.Extra, "\uE710");
        Time("Completionist", t.Complete, "\uE734");
        if (row.Children.Count == 0) Time("All styles", t.AllStyles, "\uE916");
        stack.Children.Add(row);

        // Your own time beside it.
        double minutes = _game.IsSteam ? SteamLibrary.GetPlaytimeMinutes(_game.AppId) ?? 0 : Host.Playtime.TotalSeconds(_game.Key) / 60;
        double target = t.Main > 0 ? t.Main : t.AllStyles;
        if (minutes >= 1 && target > 0)
        {
            double played = minutes / 60;
            double share = played / target;
            string mine = played < 1 ? $"{minutes:0} min" : $"{played:0.#} h";
            string text = share >= 1
                ? $"You've played {mine}: longer than the main story takes most people" + (t.Complete > 0 && played < t.Complete ? $", {100 * played / t.Complete:0}% of the way to completionist." : ".")
                : $"You've played {mine}: about {100 * share:0}% of the main story.";
            var you = Line(text, 15, "FgBrush", wrap: true);
            you.Margin = new Thickness(0, 18, 0, 8);
            stack.Children.Add(you);
            stack.Children.Add(Bar(Math.Min(1, share), 8));
        }
        var (_, _, checkedAt) = HowLongToBeat.Cached(_game);
        var from = Line("From HowLongToBeat.com" + (t.Name != _game.Name ? "  ·  " + t.Name : "") +
                        (checkedAt.Year > 2000 ? "  ·  checked " + Ui.Ago(checkedAt.ToLocalTime()) : "") + "  ·  press to open its page", 13);
        from.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(from);

        _hltb.Children.Add(SectionTitle("How long to beat"));
        var card = Card(stack, new Thickness(22, 18, 22, 18));
        _hltb.Children.Add(CardButton(card, "hltb", () => ProcessUtil.Open(t.PageUrl)));
    }

    private void BuildNotes()
    {
        _notes.Children.Clear();
        if (GameNotebook.Migrate(_game, Host.Library)) Host.Library.Save();
        var mine = Host.Library.Find(_game.Key);
        var notebook = GameNotebook.Ordered(GameNotebook.Load(_game)).ToList();
        bool any = mine is { Rating: > 0 } || !string.IsNullOrWhiteSpace(mine?.Status) || notebook.Count > 0;
        if (!any) return;

        var stack = new StackPanel();
        var top = new WrapPanel();
        mine ??= new ItemOverride();
        if (mine.Rating > 0)
        {
            var stars = Ui.Text(Ui.Stars(mine.Rating), 22, "AccentBrush");
            stars.Margin = new Thickness(0, 0, 16, 0);
            top.Children.Add(stars);
        }
        if (!string.IsNullOrWhiteSpace(mine.Status)) top.Children.Add(Pill(mine.Status, "CircleBrush"));
        stack.Children.Add(top);
        // The latest (or pinned) notes, with how many there are.
        foreach (var note in notebook.Take(2))
        {
            var line = Line(note.Title + (note.Preview.Length > 0 ? "  —  " + note.Preview : ""), 16, "FgBrush", wrap: true);
            line.Margin = new Thickness(0, 8, 0, 0);
            line.MaxHeight = 48;
            stack.Children.Add(line);
        }
        if (notebook.Count > 0)
        {
            var more = Line(notebook.Count == 1 ? "1 note in your notebook" : $"{notebook.Count} notes in your notebook", 13, "SubtleBrush");
            more.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(more);
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
            if (Host.Settings.ShowPlayerStats) await LoadPlayerStatsAsync(ct);
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
        _sections.Children.Add(SectionTitle("Developer's Screenshots"));
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

    /// <summary>
    /// Player stats (Settings > Games > Player stats on game pages): players now and peaks, owner estimate, store
    /// facts, monthly players for the last year and the history of user reviews, plus SteamDB's own page (with Twitch
    /// stats) in the built-in browser.
    /// </summary>
    private async Task LoadPlayerStatsAsync(CancellationToken ct)
    {
        var placeholder = Line("Loading player stats…", 15);
        var title = SectionTitle("Player stats");
        _sections.Children.Add(title);
        _sections.Children.Add(placeholder);
        GameStatsData stats;
        try { stats = await Task.Run(() => GameStats.GetAsync(_storeId, ct), ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { stats = new GameStatsData(); }
        if (ct.IsCancellationRequested) return;
        int at = _sections.Children.IndexOf(placeholder);
        _sections.Children.Remove(placeholder);
        var block = new StackPanel();
        _sections.Children.Insert(Math.Max(0, at), block);

        // ---- headline numbers
        var chips = new WrapPanel();
        void Chip(string glyph, string value, string label, string id)
        {
            var s = new StackPanel { Width = 196 };
            var icon = Ui.Glyph(glyph, 20, "AccentBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(icon);
            var v = Line(value, 26, "FgBrush", bold: true);
            v.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(v);
            s.Children.Add(Line(label, 13));
            chips.Children.Add(CardButton(Card(s, new Thickness(18, 14, 18, 14)), "stat:" + id, () => OpenSteamDb()));
        }
        if (stats.PlayingNow is int now) Chip("\uE716", now.ToString("N0"), "playing right now", "now");
        if (stats.Peak24h is int day) Chip("\uE9D9", day.ToString("N0"), "24-hour peak", "24h");
        if (stats.PeakAllTime is int all) Chip("\uE734", all.ToString("N0"), "all-time peak", "all");
        if (stats.Owners != null) Chip("\uE77B", stats.Owners.Split(' ')[0] + "+", "estimated owners", "owners");
        if (stats.Positive is int pos && stats.Negative is int neg && pos + neg > 0)
            Chip("\uE8E1", $"{100.0 * pos / (pos + neg):0}%", $"positive of {GameStats.Short(pos + neg)} reviews", "reviews");
        if (stats.AverageForeverMinutes is int avg && avg > 0) Chip("\uE823", $"{avg / 60.0:0.#}h", "average play time", "avgtime");
        if (chips.Children.Count > 0) block.Children.Add(chips);

        // ---- store data
        if (_details != null)
        {
            var facts = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            void Fact(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var f = new StackPanel { Margin = new Thickness(0, 0, 34, 10) };
                f.Children.Add(Line(label.ToUpperInvariant(), 11, "AccentBrush", bold: true));
                f.Children.Add(Line(value, 15, "FgBrush"));
                facts.Children.Add(f);
            }
            Fact("Released", _details.ReleaseDate);
            Fact("Developer", _details.Developers);
            Fact("Publisher", _details.Publishers);
            Fact("Price", _game.Price);
            if (_details.Metacritic is int mc) Fact("Metacritic", mc.ToString());
            Fact("Achievements", _details.AchievementTotal > 0 ? _details.AchievementTotal.ToString() : "");
            if (stats.MedianForeverMinutes is int med && med > 0) Fact("Median play time", $"{med / 60.0:0.#} hours");
            if (stats.PeakYesterday is int y && y > 0) Fact("Peak yesterday", y.ToString("N0"));
            block.Children.Add(Card(facts, new Thickness(20, 16, 20, 6)));
            if (stats.Tags.Count > 0)
            {
                var tags = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                foreach (var t in stats.Tags) tags.Children.Add(Pill(t, "CircleBrush"));
                block.Children.Add(tags);
            }
        }

        // ---- monthly players: average bars with the peak marked above each
        var months = stats.Months.Where(m => !m.Month.StartsWith("Last", StringComparison.OrdinalIgnoreCase)).Take(12).Reverse().ToList();
        if (months.Count > 1)
        {
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var legend = Line("\u25A0 average   \u25CF peak", 12);
            DockPanel.SetDock(legend, Dock.Right);
            head.Children.Add(legend);
            head.Children.Add(Line("MONTHLY PLAYERS", 12, "AccentBrush", bold: true));
            panel.Children.Add(head);
            double top = Math.Max(1, months.Max(m => m.Peak));
            var chart = new Grid { Height = 170 };
            for (int i = 0; i < months.Count; i++)
            {
                var m = months[i];
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                var col = new Grid { Margin = new Thickness(5, 0, 5, 0) };
                col.RowDefinitions.Add(new RowDefinition());
                col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var area = new Grid();
                var avgBar = new Border { CornerRadius = new CornerRadius(5, 5, 2, 2), VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(3, 130 * m.Average / top) };
                avgBar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                area.Children.Add(avgBar);
                var peak = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 130 * m.Peak / top - 4), Fill = Brushes.White };
                area.Children.Add(peak);
                area.ToolTip = $"{m.Month}: average {m.Average:N0}, peak {m.Peak:N0}";
                col.Children.Add(area);
                var name = Line(m.Month.Length >= 3 ? m.Month[..3] + (m.Month.Contains(' ') ? " '" + m.Month[^2..] : "") : m.Month, 11);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 6, 0, 0);
                Grid.SetRow(name, 1);
                col.Children.Add(name);
                Grid.SetColumn(col, i);
                chart.Children.Add(col);
            }
            panel.Children.Add(chart);
            var last = stats.Months[0];
            panel.Children.Add(Line($"{last.Month}: average {last.Average:N0} players, peak {last.Peak:N0}", 13));
            var card = Card(panel, new Thickness(20, 16, 20, 16));
            card.Margin = new Thickness(0, 10, 0, 0);
            block.Children.Add(CardButton(card, "stat:months", OpenSteamDb));
        }

        // ---- review history: thumbs up above the line, thumbs down below, month by month
        var reviews = stats.Reviews.TakeLast(24).ToList();
        if (reviews.Count > 1)
        {
            var panel = new StackPanel();
            int ups = reviews.Sum(r => r.Up), downs = reviews.Sum(r => r.Down);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var summary = Line($"{ups:N0} \u25B2   {downs:N0} \u25BC   ({100.0 * ups / Math.Max(1, ups + downs):0}% positive)", 12);
            DockPanel.SetDock(summary, Dock.Right);
            head.Children.Add(summary);
            head.Children.Add(Line("USER REVIEW HISTORY", 12, "AccentBrush", bold: true));
            panel.Children.Add(head);
            double top = Math.Max(1, reviews.Max(r => Math.Max(r.Up, r.Down)));
            var chart = new Grid { Height = 160 };
            chart.RowDefinitions.Add(new RowDefinition());
            chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            chart.RowDefinitions.Add(new RowDefinition());
            var axis = new Border { Opacity = 0.4 };
            axis.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
            Grid.SetRow(axis, 1);
            Grid.SetColumnSpan(axis, reviews.Count);
            chart.Children.Add(axis);
            for (int i = 0; i < reviews.Count; i++)
            {
                var r = reviews[i];
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                var up = new Border { Background = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0x6E)), CornerRadius = new CornerRadius(3, 3, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0), Height = Math.Max(r.Up > 0 ? 2 : 0, 76 * r.Up / top) };
                var down = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x4E)), CornerRadius = new CornerRadius(0, 0, 3, 3), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 0, 2, 0), Height = Math.Max(r.Down > 0 ? 2 : 0, 76 * r.Down / top) };
                up.ToolTip = down.ToolTip = $"{r.Month:MMMM yyyy}: {r.Up:N0} positive, {r.Down:N0} negative";
                Grid.SetColumn(up, i); Grid.SetColumn(down, i); Grid.SetRow(down, 2);
                chart.Children.Add(up); chart.Children.Add(down);
            }
            panel.Children.Add(chart);
            panel.Children.Add(Line($"{reviews[0].Month:MMM yyyy} – {reviews[^1].Month:MMM yyyy}", 12));
            var card = Card(panel, new Thickness(20, 16, 20, 16));
            card.Margin = new Thickness(0, 10, 0, 0);
            block.Children.Add(CardButton(card, "stat:reviews", OpenSteamDb));
        }

        // ---- the full picture on SteamDB (Twitch stats, owners history, price history...)
        var more = new StackPanel { Orientation = Orientation.Horizontal };
        var go = Ui.Glyph("\uE774", 20, "AccentBrush");
        go.Margin = new Thickness(0, 0, 12, 0);
        more.Children.Add(go);
        var text = new StackPanel();
        text.Children.Add(Line("Open on SteamDB", 16, "FgBrush", bold: true));
        text.Children.Add(Line("Full charts, Twitch viewers, price history and more, in the built-in browser", 13));
        more.Children.Add(text);
        var moreCard = Card(more, new Thickness(18, 14, 18, 14));
        moreCard.Margin = new Thickness(0, 10, 0, 0);
        block.Children.Add(CardButton(moreCard, "stat:steamdb", OpenSteamDb));
        block.Children.Add(Line("Players and reviews from Steam, owner estimates from SteamSpy, monthly players from SteamCharts.", 12));
    }

    private void OpenSteamDb() => Host.Push(BrowserScreen.Open(Host, GameStats.SteamDbUrl(_storeId)));

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
                Source = logo, MaxHeight = _logoHeight, MaxWidth = _logoWidth, Stretch = Stretch.Uniform, HorizontalAlignment = _logoAlign,
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
                    Padding = new Thickness(18, 10, 18, 10), HorizontalAlignment = _logoAlign, Child = image
                };
            }
            else _logoHost.Content = image;
            // Looks that show the logo large in place of the name: the name goes (it's in the logo).
            if (_logoReplacesTitle) _titleHost.Visibility = Visibility.Collapsed;
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

        AddHeader("Your own");
        AddRow("Choose a program…", null, "\uE8E5", click: ChooseProgram, subtitle: "Any program, shortcut or script on this PC, even if it isn't in the Start menu");
        foreach (var own in Host.Library.Apps.Where(a => System.IO.Path.IsPathRooted(a.AppId)).ToList())
        {
            var entry = own;
            AddRow(entry.Name, "Added  ✓", "\uE7AC", click: () => { Host.RemoveApp(entry.AppId); Rebuild(); }, subtitle: entry.AppId + "  ·  Select to remove");
        }

        AddHeader("Installed on this PC");
        if (_apps == null) { AddNote("Scanning installed apps…"); return; }
        if (_error != null) AddNote(_error);

        var pending = new List<(Image View, string AppId)>();
        foreach (var app in _apps.Where(a => !IsGame(a)))
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

    // Folders and links that belong to game stores: their shortcuts are games, which the library has already.
    private static readonly string[] GameMarks =
    {
        "steamapps", "steam://", "com.epicgames.launcher", "epic games", "gog galaxy", "goggalaxy://", "ubisoft game launcher", "uplay://",
        "origin2://", "ea games", "ea app", "battle.net", "battlenet://", "xboxgames", "amazon games", "itch.io", "riot games", "rockstar games\\",
    };

    /// <summary>Whether a Start-menu entry is really a game (a store's shortcut, or one already in the library).</summary>
    private bool IsGame(InstalledApp a)
    {
        var id = a.AppId.ToLowerInvariant();
        if (GameMarks.Any(id.Contains)) return true;
        if (id.Contains("://")) return true; // a store's link, not a program
        var name = a.Name.Trim();
        return Host.AllGames.Any(g => g.IsGame && (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(g.OriginalName, name, StringComparison.OrdinalIgnoreCase)));
    }

    private async void ChooseProgram()
    {
        // SquareDeck's own file picker (works with the controller), not Windows' file dialog.
        var path = await Host.PickFileAsync("Choose a program to add", new[] { ".exe", ".lnk", ".bat", ".cmd", ".url" });
        if (string.IsNullOrEmpty(path)) return;
        var suggested = System.IO.Path.GetFileNameWithoutExtension(path);
        try { if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d) suggested = d; }
        catch (Exception) { }
        var name = await Host.PromptAsync("Name it", "How it's shown on the launcher.", suggested, "Add");
        if (string.IsNullOrWhiteSpace(name)) return;
        Host.AddApp(name.Trim(), path);
        Rebuild();
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
/// Every game in one place. Section tabs across the top (LB / RB) choose what to show: installed, favorites, recently
/// played, not installed, wishlist, updates and collections. Chips under them narrow it down by store, category or your
/// own status; a slim toolbar has the search, sort, view and everything else under More. The header stays put while
/// the games scroll.
/// </summary>
public sealed class LibraryScreen : Screen
{
    private double TileSize => Layout switch { "compact" => 124, "large" => 226, "posters" => 176, "showcase" => 286, "mini" => 92, _ => 164 };
    private bool ListLayout => Layout is "list" or "focus";
    private double _showcaseWidth;

    /// <summary>
    /// Showcase: wide banners, exactly four to a row. Each tile takes its picture's width plus 16 for the highlight
    /// ring's padding and 4 of spacing, so the picture width is a quarter of the row less that.
    /// </summary>
    private double ShowcaseWidth()
    {
        double row = _groups.ActualWidth > 200 ? _groups.ActualWidth : 1180;
        _showcaseWidth = row;
        return Math.Floor((row - 2) / 4 - 20);
    }
    private bool ShelfLayout => Layout is "shelves" or "timeline";
    private bool SpotlightLayout => Layout is "spotlight" or "focus";

    /// <summary>
    /// The page's views (the toolbar's View, or Settings > Games and library). "sidebar" is the id the usual grid has
    /// always had (6.26 replaced the sidebar with tabs and chips; the id is kept so saved settings still match).
    /// </summary>
    public static readonly (string Id, string Name, string About)[] Layouts =
    {
        ("sidebar", "Grid", "Covers in rows, the usual"),
        ("compact", "Compact grid", "Smaller covers, to see more at once"),
        ("large", "Large grid", "Big covers"),
        ("mini", "Mini grid", "Tiny covers, to see your whole library at once"),
        ("posters", "Posters", "Tall movie-poster art in big rows"),
        ("showcase", "Banners", "Wide store banners, four to a row"),
        ("list", "List", "One row per game, with its store, play time, when you last played and size"),
        ("spotlight", "Spotlight", "The highlighted game large across the top, covers below"),
        ("focus", "Spotlight list", "The highlighted game large across the top, a list below"),
        ("shelves", "Shelves", "Rows that scroll sideways: recent, favorites, each store, collections"),
        ("timeline", "Timeline", "Rows by when you last played: today, this week, this month, earlier, never"),
    };

    private string Layout => Layouts.Any(l => l.Id == Host.Settings.LibraryLayout) ? Host.Settings.LibraryLayout : "sidebar";
    private readonly Border _spotlight = new() { Margin = new Thickness(0, 0, 4, 14), Height = 250, Visibility = Visibility.Collapsed };
    private const string Uncategorized = "Uncategorized";
    private const int MaxNotInstalledShown = 400;

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _chipScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.HorizontalOnly, Focusable = false, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly List<string> _tabIds = new();
    private readonly TextBox _searchInput = new();
    private readonly Border _searchBar = new();
    private readonly System.Windows.Threading.DispatcherTimer _searchSoon = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _groups = new();
    private ScrollViewer? _pageScroll;
    private readonly Border _letterBadge = new()
    {
        Width = 150, Height = 150, CornerRadius = new CornerRadius(30), HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false
    };
    private readonly TextBlock _letterText = new() { FontSize = 84, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private string _filter = "all";     // all | favorites | recent | installed | notinstalled | wishlist | updates | collections | col:<id> | rated | src:<store> | cat:<name> | status:<s>
    private string _sort = "recent";    // recent | name | playtime | size | rating
    private string _search = "";
    /// <summary>"" (no groups), "category", "genre" (main genre) or "played" (played / never played).</summary>
    private string _groupBy = "";
    private static readonly (string Id, string Name)[] Groupings =
    {
        ("", "No groups"), ("category", "Category"), ("genre", "Main genre"), ("played", "Played or never played"),
    };
    private string? _lastFocus;         // game key, "tool:<index>", "tab:<filter>" or "chip:<filter>"

    public LibraryScreen()
    {
        BuildSearchBar();

        // The header: tabs, chips and the toolbar. It stays put; only the games scroll under it.
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly, Focusable = false, Margin = new Thickness(0, 0, 0, 6), Content = _tabs
        };
        // Second row: the search box, the chips and the count on the left (scrolling sideways if there are many), the tools on the right.
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_searchBar);
        left.Children.Add(_chips);
        _chipScroll.Content = left;
        var second = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        DockPanel.SetDock(_toolbar, Dock.Right);
        second.Children.Add(_toolbar);
        second.Children.Add(_chipScroll);
        var header = new StackPanel();
        header.Children.Add(tabScroll);
        header.Children.Add(second);

        // Room above the first row: a highlighted tile that lifts or floats (some styles' highlight) isn't cut off.
        var content = new StackPanel { Margin = new Thickness(0, 22, 0, 10) };
        content.Children.Add(_spotlight);
        content.Children.Add(_groups);
        // Scrolling to a tile leaves the same headroom, so its lift stays in view as you move up the page.
        bool widening = false;
        content.RequestBringIntoView += (_, e) =>
        {
            if (widening || e.TargetObject is not FrameworkElement fe || fe is not Button) return;
            e.Handled = true;
            widening = true;
            try { fe.BringIntoView(new Rect(-12, -30, fe.ActualWidth + 24, fe.ActualHeight + 42)); }
            finally { widening = false; }
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = content
        };
        _pageScroll = scroll;
        _genresSettled.Tick += (_, _) => { _genresSettled.Stop(); if (_groupBy == "genre" && Host.IsTop(this)) Reorganize(); };
        _realizeSoon.Tick += (_, _) => Realize();
        // Scrolling the page, or a shelf sideways (its scroll event bubbles up here), brings tiles into view.
        scroll.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RealizeSoon()));
        scroll.SizeChanged += (_, _) => RealizeSoon();
        // Showcase fits four banners to the row, so a change of width lays it out again.
        _groups.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) CenterGrids();
            if (Layout == "showcase" && e.WidthChanged && Math.Abs(e.NewSize.Width - _showcaseWidth) > 8) Dispatcher.BeginInvoke(new Action(Rebuild));
        };

        // The letter you jumped to (LT / RT), shown big for a moment over the games.
        _letterBadge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _letterText.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        _letterBadge.Child = _letterText;

        var body = new Grid();
        body.Children.Add(scroll);
        body.Children.Add(_letterBadge);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        SetPage("Game library", root,
            "{Select}  Play      {Details}  Details      {Options}  Options      LB / RB  Section      LT / RT  Jump by letter      {Back}  Back", wide: true);
    }

    // ---------------------------------------------------------------- search bar

    /// <summary>
    /// A real search box: type on a keyboard and the games filter as you go. With a controller, select it to type
    /// on the on-screen keyboard. B clears it.
    /// </summary>
    private void BuildSearchBar()
    {
        var grid = new Grid { Width = 260, Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glass = Ui.Text("\uE721", 14, "SubtleBrush");
        glass.FontFamily = Ui.IconFont;
        glass.HorizontalAlignment = HorizontalAlignment.Center;
        glass.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(glass);

        var placeholder = Ui.Text("Search", 14, "SubtleBrush");
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(placeholder, 1);
        grid.Children.Add(placeholder);

        _searchInput.Background = Brushes.Transparent;
        _searchInput.BorderThickness = new Thickness(0);
        _searchInput.FontSize = 14;
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
        _searchBar.Margin = new Thickness(0, 0, 10, 0);
        _searchBar.BorderThickness = new Thickness(2);
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

    private void UpdateSpotlight() => _spotlight.Visibility = SpotlightLayout ? Visibility.Visible : Visibility.Collapsed;

    public override void OnShown()
    {
        UpdateSpotlight();
        _groupBy = Groupings.Any(x => x.Id == Host.Settings.LibraryGroupBy) ? Host.Settings.LibraryGroupBy : "";
        GenreCache.Updated += OnGenres;
        Host.LibraryChanged += OnLibraryChanged;
        Host.ApplyCachedLists(); // saved lists and counts show at once; they refresh when their section is picked
        Rebuild();
        _ = Host.RefreshAchievementProgressAsync();
    }

    public override void OnResumed() => Rebuild();

    public override void OnClosed()
    {
        Host.LibraryChanged -= OnLibraryChanged;
        GenreCache.Updated -= OnGenres;
        _artCts?.Cancel();
    }

    // Genres arrive one by one in the background: the page is reorganized once they've stopped arriving for a
    // moment, not once per game (that rebuilt it over and over, and it froze while sorting).
    private readonly System.Windows.Threading.DispatcherTimer _genresSettled = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private void OnGenres() => Dispatcher.BeginInvoke(() =>
    {
        if (_groupBy != "genre") return;
        _genresSettled.Stop();
        _genresSettled.Start();
    });

    private Border? _reorganizing;

    /// <summary>Shows "Reorganizing…" while the page is sorted into its groups, then rebuilds it.</summary>
    private void Reorganize()
    {
        if (_reorganizing == null)
        {
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            var spin = Ui.Glyph("\uE895", 18, "AccentBrush");
            spin.RenderTransformOrigin = new Point(0.5, 0.5);
            var turn = new RotateTransform();
            spin.RenderTransform = turn;
            if (!Theme.ReduceMotion) turn.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            words.Children.Add(spin);
            var label = Ui.Text("Reorganizing…", 16, "FgBrush", bold: true);
            label.Margin = new Thickness(10, 0, 0, 0);
            words.Children.Add(label);
            _reorganizing = new Border { Child = words, Padding = new Thickness(18, 10, 18, 10), CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 70), IsHitTestVisible = false };
            _reorganizing.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            Panel.SetZIndex(_reorganizing, 50);
            if (Content is Panel root) { Grid.SetRowSpan(_reorganizing, 99); Grid.SetColumnSpan(_reorganizing, 99); root.Children.Add(_reorganizing); }
        }
        _reorganizing.Visibility = Visibility.Visible;
        // Drawn first, then the work: the pop-up shows while the page is sorted.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Host.IsTop(this)) Rebuild();
            Dispatcher.BeginInvoke(new Action(() => { if (_reorganizing != null) _reorganizing.Visibility = Visibility.Collapsed; }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---------------------------------------------------------------- groups that fold away

    private static readonly HashSet<string> Collapsed = new();

    /// <summary>A group's heading that folds the group away (and back) when selected.</summary>
    private bool AddGroupHeading(string name, int count)
    {
        string key = _groupBy + ":" + name;
        bool folded = Collapsed.Contains(key);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Glyph(folded ? "\uE76C" : "\uE70D", 14, "SubtleBrush"));
        var title = Ui.Text($"{name}  ({count})", 20, "FgBrush", bold: true);
        title.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(title);
        var heading = new Button { Style = Ui.StyleOf("RowButton"), Content = row, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 14, 6),
            Margin = new Thickness(0, 10, 0, 4), ToolTip = folded ? "Show this group" : "Fold this group away", Tag = "group:" + key };
        heading.Click += (_, _) =>
        {
            if (!Collapsed.Remove(key)) Collapsed.Add(key);
            FocusTag = "group:" + key;
            Rebuild();
        };
        _groups.Children.Add(heading);
        return !folded;
    }

    private string? FocusTag;

    private void OnLibraryChanged()
    {
        if (Host.IsTop(this)) Rebuild();
    }

    // ---------------------------------------------------------------- what to show

    private GameCollection? CollectionOf(string filter) =>
        filter.StartsWith("col:") ? Host.Library.Collections.FirstOrDefault(c => c.Id == filter[4..]) : null;

    private string FilterName(string filter) => filter switch
    {
        "all" => "Installed",
        "favorites" => "Favorites",
        "recent" => "Recently played",
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
        "recent" => g.IsInstalled && g.RecentAt > DateTime.Now.AddDays(-30) && g.RecentAt < DateTime.Now.AddDays(1),
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
        "playtime" => "Play time",
        "rating" => "Rating",
        _ => "Recent"
    };

    private void Rebuild()
    {
        Dispatcher.BeginInvoke(new Action(CenterGrids), System.Windows.Threading.DispatcherPriority.Loaded);
        var everything = Host.AllGames.Concat(Host.NotInstalledGames).Concat(Host.WishlistGames).ToList();

        BuildTabs(everything);
        BuildChips(everything);
        BuildToolbar();
        _groups.Children.Clear();
        _lazy.Clear();
        _pendingArt.Clear();
        _shownArt.Clear();
        _coversAsked.Clear();
        Dispatcher.BeginInvoke(new Action(Realize), System.Windows.Threading.DispatcherPriority.Loaded);
        // Art still on its way for the old tiles isn't wanted any more.
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        _artQueue.Clear();

        IEnumerable<Game> games = everything.Where(g => Matches(g, _filter));
        if (_search.Length > 0)
            games = games.Where(g => g.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase));
        games = (_filter == "recent" ? "recent" : _sort) switch
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

        if (_filter == "notinstalled") AddNotInstalledNotice();
        if (_filter == "wishlist") AddWishlistNotice();

        if (_filter == "collections")
        {
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
                AddNote(_search.Length > 0 ? "No games match \u201C" + _search + "\u201D here. Try another section (LB / RB), or clear the search." : "Nothing here yet. Try another section with LB / RB.");
        }
        else if (Layout == "shelves" && _filter == "all" && _search.Length == 0)
        {
            AddShelves(list);
        }
        else if (Layout == "timeline" && _filter == "all" && _search.Length == 0)
        {
            AddTimeline(list);
        }
        else if (_groupBy == "category")
        {
            var byCategory = list.GroupBy(g => Host.CategoryOf(g) ?? Uncategorized)
                .OrderBy(g => g.Key == Uncategorized)          // uncategorized last
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in byCategory)
                if (AddGroupHeading(group.Key, group.Count())) AddGames(group);
        }
        else if (_groupBy == "genre")
        {
            // The first genre on each game's store page; looked up in the background, filled in as found.
            const string unknown = "Genre not known yet", none = "No genre";
            var byGenre = list.GroupBy(g => GenreCache.Of(g) is { } genre ? (genre.Length > 0 ? genre : none) : g.IsGame ? unknown : "Apps and programs")
                .OrderBy(g => g.Key is unknown or none or "Apps and programs")
                .ThenByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in byGenre)
                if (AddGroupHeading(group.Key, group.Count())) AddGames(group);
            if (list.Any(g => g.IsGame && GenreCache.Of(g) == null)) _ = Task.Run(() => GenreCache.EnsureAsync(list.ToList()));
        }
        else if (_groupBy == "played")
        {
            bool Played(Game g) => Host.Playtime.TotalSeconds(g.Key) > 0 || g.LastPlayed is not null;
            var played = list.Where(Played).ToList();
            var never = list.Where(g => !Played(g)).ToList();
            if (played.Count > 0 && AddGroupHeading("Played", played.Count)) AddGames(played);
            if (never.Count > 0 && AddGroupHeading("Never played", never.Count)) AddGames(never);
        }
        else
        {
            AddGames(list);
        }

        // Covers for games that aren't installed are only fetched once they are actually on screen (see Realize).
        _online = online;
        if (FocusTag != null && _groups.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == FocusTag) is { } folded)
        {
            FocusTag = null;
            FocusLater(folded);
            return;
        }
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

    /// <summary>Timeline: rows by when each game was last played, newest first, then the ones never played.</summary>
    private void AddTimeline(List<Game> all)
    {
        var now = DateTime.Now;
        var played = all.Where(g => g.RecentAt > DateTime.MinValue.AddYears(1) && g.RecentAt <= now.AddDays(1)).OrderByDescending(g => g.RecentAt).ToList();
        void Shelf(string heading, IEnumerable<Game> games)
        {
            var list = games.Take(40).ToList();
            if (list.Count == 0) return;
            _groups.Children.Add(Ui.Header($"{heading}  ({list.Count})"));
            AddGames(list, shelf: true);
        }
        Shelf("Today", played.Where(g => g.RecentAt.Date == now.Date));
        Shelf("This week", played.Where(g => g.RecentAt.Date < now.Date && g.RecentAt > now.AddDays(-7)));
        Shelf("This month", played.Where(g => g.RecentAt <= now.AddDays(-7) && g.RecentAt > now.AddDays(-31)));
        Shelf("This year", played.Where(g => g.RecentAt <= now.AddDays(-31) && g.RecentAt > now.AddDays(-365)));
        Shelf("Longer ago", played.Where(g => g.RecentAt <= now.AddDays(-365)));
        Shelf("Never played", all.Where(g => g.IsInstalled && !played.Contains(g)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        Shelf("Not installed", all.Where(g => !g.IsInstalled && !g.OnWishlist && !played.Contains(g)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
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

    // ---------------------------------------------------------------- tabs, chips and toolbar

    /// <summary>A pill with an optional icon, a name and a quieter count. Text takes the button's color, so it flips on the active one.</summary>
    private static Button Chip(string? glyph, string label, int? count, bool active, double size = 13)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null)
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size - 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, label.Length > 0 ? 6 : 0, 0) });
        if (label.Length > 0)
            row.Children.Add(new TextBlock { Text = label, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (count != null)
            row.Children.Add(new TextBlock { Text = count.Value.ToString("N0"), FontSize = size - 2, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) });
        var button = new Button { Style = Ui.StyleOf("ChipButton"), Content = row, Tag = active ? "active" : null, Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 4, 0) };
        return button;
    }

    /// <summary>The tab a filter belongs under (store, category and status chips refine Installed; a collection is under Collections).</summary>
    private string TabOf(string filter) => filter switch
    {
        _ when filter.StartsWith("src:") || filter.StartsWith("cat:") || filter.StartsWith("status:") || filter == "rated" || filter == "installed" => "all",
        _ when filter.StartsWith("col:") => "collections",
        _ => filter
    };

    /// <summary>Shows another section (a tab or a chip) and puts the highlight where it makes sense.</summary>
    private void SelectFilter(string filter, string? focus)
    {
        _filter = filter;
        _lastFocus = focus;
        // Saved lists show at once and refresh in the background each time their section is picked.
        if (filter == "notinstalled") _ = Host.LoadOwnedGamesAsync(refresh: true);
        if (filter == "wishlist") _ = Host.LoadWishlistAsync(refresh: true);
        Rebuild();
    }

    /// <summary>The sections across the top, with how many games each has.</summary>
    private void BuildTabs(List<Game> everything)
    {
        _tabs.Children.Clear();
        _tabIds.Clear();
        var installed = everything.Where(g => g.IsInstalled).ToList();
        string current = TabOf(_filter);
        void Tab(string filter, string glyph, int? count)
        {
            _tabIds.Add(filter);
            var tab = Chip(glyph, filter == "recent" ? "Recent" : FilterName(filter), count, current == filter, 15);
            tab.Padding = new Thickness(13, 5, 13, 5);
            string id = "tab:" + filter;
            tab.DataContext = id;
            tab.Click += (_, _) => SelectFilter(filter, id);
            tab.GotKeyboardFocus += (_, _) => _lastFocus = id;
            _tabs.Children.Add(tab);
        }
        Tab("all", "", installed.Count);
        Tab("favorites", "", everything.Count(g => g.IsFavorite));
        int recent = installed.Count(g => Matches(g, "recent"));
        if (recent > 0) Tab("recent", "", recent);
        Tab("notinstalled", "", Host.OwnedGamesState == "ready" ? Host.NotInstalledGames.Count : null);
        Tab("wishlist", "", Host.WishlistState == "ready" ? Host.WishlistGames.Count : null);
        int updates = installed.Count(g => g.HasUpdate);
        if (updates > 0 || _filter == "updates") Tab("updates", "", updates);
        Tab("collections", "", Host.Library.Collections.Count);

        // Next to Collections: the mood picker, the backlog boss and your passport (each opens its own page).
        void Action(string glyph, string label, string tip, Func<Screen> page, UIElement? icon = null)
        {
            var b = Chip(glyph, label, null, false, 15);
            b.Padding = new Thickness(13, 5, 13, 5);
            b.ToolTip = tip;
            if (icon != null && b.Content is StackPanel row && row.Children.Count > 0) { row.Children.RemoveAt(0); row.Children.Insert(0, icon); }
            string id = "act:" + label;
            b.DataContext = id;
            b.Click += (_, _) => Host.Push(page());
            b.GotKeyboardFocus += (_, _) => _lastFocus = id;
            _tabs.Children.Add(b);
        }
        if (Labs.On("mood")) Action("\uE76E", "How are you feeling?", "Pick a mood, get three games that fit", () => new MoodScreen());
        if (Labs.On("boss")) Action("\uE7EF", "Fight the Backlog Boss", "Your unfinished games as a boss to defeat, an hour at a time", () => new BossScreen());
        if (Labs.On("passport")) Action("", "My Gamer Passport", "Stamps for every genre, decade and store you've played", () => new PassportScreen(),
            PassportArt.Icon(16, Brushes.Transparent));
    }

    /// <summary>
    /// Under Installed: chips for each store, your categories, your own statuses and your top rated, to narrow it down.
    /// Inside a collection: a way back to all collections.
    /// </summary>
    private void BuildChips(List<Game> everything)
    {
        _chips.Children.Clear();
        void Add(string filter, string label, int? count, string? glyph = null, ImageSource? logo = null)
        {
            var chip = Chip(glyph, label, count, _filter == filter);
            if (logo != null && chip.Content is StackPanel row)
                row.Children.Insert(0, new Image { Source = logo, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            string id = "chip:" + filter;
            chip.DataContext = id;
            chip.Click += (_, _) => SelectFilter(filter, id);
            chip.GotKeyboardFocus += (_, _) => _lastFocus = id;
            _chips.Children.Add(chip);
        }

        if (_filter.StartsWith("col:"))
        {
            var back = Chip("", "All collections", null, false);
            back.DataContext = "chip:collections";
            back.Click += (_, _) => SelectFilter("collections", "col-tile:" + _filter[4..]);
            back.GotKeyboardFocus += (_, _) => _lastFocus = "chip:collections";
            _chips.Children.Add(back);
            Add(_filter, FilterName(_filter), null, "");
        }
        else if (TabOf(_filter) == "all")
        {
            var installed = everything.Where(g => g.IsInstalled).ToList();
            var sources = installed.Select(g => g.SourceName).Distinct().OrderBy(s => s).ToList();
            var categories = Host.Categories;
            var statuses = GameNotesScreen.Statuses.Where(st => everything.Any(g => Matches(g, "status:" + st))).ToList();
            bool anyRated = everything.Any(g => Matches(g, "rated"));
            if (sources.Count > 1 || categories.Count > 0 || statuses.Count > 0 || anyRated)
            {
                Add("all", "Everything", null);
                if (sources.Count > 1)
                    foreach (var s in sources) Add("src:" + s, s, installed.Count(g => g.SourceName == s), logo: StoreLogos.For(s));
                foreach (var c in categories) Add("cat:" + c, c, everything.Count(g => Matches(g, "cat:" + c)), "");
                foreach (var st in statuses) Add("status:" + st, st, everything.Count(g => Matches(g, "status:" + st)), "");
                if (anyRated) Add("rated", "4 stars and up", everything.Count(g => Matches(g, "rated")), "");
            }
        }
        _chipScroll.Visibility = _chips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ViewName => Layouts.First(l => l.Id == Layout).Name;

    private void BuildToolbar()
    {
        _toolbar.Children.Clear();
        // Small buttons with short labels: Sort shows the order in use, View the view, then the cover shape and More.
        AddTool("\uE8CB", SortLabel, ChooseSort, "Sort");
        AddTool("\uE8A9", ViewName, ChooseLayout, "View");
        if (!ShelfLayout && !ListLayout && Layout is not ("posters" or "showcase" or "mini")) AddTool("\uE91B", ViewLabel, ChooseView, "Cover shape");
        if (_groupBy.Length > 0) AddTool("\uE8FD", Groupings.First(x => x.Id == _groupBy).Name, ChooseGrouping, "Group by");
        if (Host.InstallQueue.Count > 0) AddTool("\uE896", $"Queue ({Host.InstallQueue.Count})", ShowQueue, "Install queue");
        AddTool("\uE712", "More", () => _ = MoreAsync(), "Add a game, group by, play time, achievements");
    }

    /// <summary>Everything else, in one place: add a game, group the list, play time, achievements.</summary>
    private async Task MoreAsync()
    {
        int choice = await Host.AskWithIconsAsync("Library", null,
            ("Add a custom game", ""), ("Group by: " + Groupings.First(x => x.Id == _groupBy).Name, ""),
            ("Play time", ""), ("Achievements", ""), ("Clear the search and filters", ""));
        switch (choice)
        {
            case 0: Host.Push(new AddCustomGameScreen()); break;
            case 1: ChooseGrouping(); break;
            case 2: Host.Push(new PlaytimeScreen()); break;
            case 3: Host.Push(new UnlocksScreen()); break;
            case 4:
                _searchInput.Text = "";
                _search = "";
                SelectFilter("all", null);
                break;
        }
    }

    private async void ChooseGrouping()
    {
        int choice = await Host.AskAsync("Group by", null, Groupings.Select(x => x.Name).ToArray());
        if (choice < 0) return;
        _groupBy = Groupings[choice].Id;
        Host.Settings.LibraryGroupBy = _groupBy;
        Host.Settings.Save();
        Reorganize();
    }

    private void AddTool(string glyph, string label, Action click, string tip)
    {
        var button = Chip(glyph, label, null, false, 13);
        button.Padding = label.Length > 0 ? new Thickness(11, 5, 11, 5) : new Thickness(9, 5, 9, 5);
        button.ToolTip = tip;
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
        Panel panel = shelf ? new StackPanel { Orientation = Orientation.Horizontal } : ListLayout ? new StackPanel() : new WrapPanel();
        if (ListLayout && !shelf)
        {
            foreach (var game in games)
            {
                var g = game;
                var row = ListRow(g, out var rowTitle);
                row.Tag = g;
                row.Click += (_, _) => _ = Host.ActivateAsync(g);
                row.GotKeyboardFocus += (_, _) => { _lastFocus = g.Key; rowTitle.Active = true; ShowSpotlight(g); };
                row.LostKeyboardFocus += (_, _) => rowTitle.Active = false;
                panel.Children.Add(row);
            }
            _groups.Children.Add(panel);
            return;
        }
        // Square: the cover as it is. Grid: tall 2:3 poster art. Rectangle: the wide 460×215 header, three to a row.
        // Poster wall and Showcase always use their own art shape; Mini grid keeps it square.
        string view = Layout switch { "posters" => "grid", "showcase" => "header", "mini" => "square", _ => Host.Settings.LibraryView };
        double tileWidth = Layout == "showcase" && !shelf ? ShowcaseWidth() : view == "header" ? TileSize * 2 + 4 : TileSize;
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
            // Bound once the tile comes near the screen (see Realize), so covers that finish downloading later still appear.
            Lazy(box, cover, g);
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
                QueueArt(g, art, () => { art.Visibility = Visibility.Visible; name.Visibility = Visibility.Collapsed; }, box);
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
            // The same highlight animation as the home screen (Settings > Home screen > Selected game): the ring pulse
            // or glow comes with every tile; grow, bounce, lift, wobble, tilt, shake and breathe move the cover here.
            tile.GotKeyboardFocus += (_, _) =>
            {
                _lastFocus = g.Key; title.Active = true; ShowSpotlight(g);
                Panel.SetZIndex(tile, 1);
                // The whole tile moves, so the highlight ring around it grows with the cover (it stayed put before).
                if (Motion.HighlightGrow > 1 || Motion.SelectGrow > 1.001) Motion.GrowTile(tile, true);
            };
            tile.LostKeyboardFocus += (_, _) =>
            {
                title.Active = false;
                Panel.SetZIndex(tile, 0);
                if (Motion.HighlightGrow > 1 || Motion.SelectGrow > 1.001) Motion.GrowTile(tile, false);
            };
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

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> _usualMargins = new();

    /// <summary>
    /// Centers the grids of tiles. Each row is only as wide as the tiles that fit, so a left-aligned grid left a gap
    /// on the right. Every grid on the page gets the same width (the most whole tiles that fit), so with groups the
    /// grids line up with each other, and the headings move in with them. The last row still starts at the left.
    /// </summary>
    private void CenterGrids()
    {
        RealizeSoon(); // the grids may change width: check which tiles are near the screen again afterwards
        double room = _groups.ActualWidth;
        var grids = _groups.Children.OfType<WrapPanel>().Where(p => p.Children.Count > 0).ToList();
        if (room <= 0 || grids.Count == 0) return;
        double stride = grids.Select(p => p.Children[0].DesiredSize.Width).FirstOrDefault(w => w > 0);
        if (stride <= 0) return;
        int columns = Math.Max(1, (int)Math.Floor((room + 0.5) / stride));
        double width = columns * stride;
        foreach (var grid in grids)
        {
            grid.Width = width;
            grid.HorizontalAlignment = HorizontalAlignment.Center;
        }
        // Headings, notes and buttons between the grids start where the tiles do.
        double inset = Math.Max(0, Math.Floor((room - width) / 2));
        foreach (var other in _groups.Children.OfType<FrameworkElement>().Where(e => e is not WrapPanel))
        {
            if (!_usualMargins.TryGetValue(other, out var usual)) { usual = other.Margin; _usualMargins.Add(other, usual); }
            var m = (Thickness)usual;
            other.Margin = new Thickness(m.Left + inset, m.Top, m.Right, m.Bottom);
        }
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
        Tiles().Concat(_toolbar.Children.OfType<Button>()).Concat(_tabs.Children.OfType<Button>()).Concat(_chips.Children.OfType<Button>())
            .Concat(_groups.Children.OfType<Button>()).Concat(ShelfTiles());

    /// <summary>Tiles in the sideways rows (Shelves, Timeline) and the list rows.</summary>
    private IEnumerable<Button> ShelfTiles() =>
        _groups.Children.OfType<ScrollViewer>().Select(s => s.Content).OfType<Panel>().SelectMany(p => p.Children.OfType<Button>())
            .Concat(_groups.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()));

    private Button? FirstTab => _tabs.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "active")) ?? _tabs.Children.OfType<Button>().FirstOrDefault();

    private void RestoreFocus()
    {
        // Typing in the search box: leave the focus there while the results change underneath.
        if (_searchInput.IsKeyboardFocusWithin) return;
        if (_lastFocus == "search") { FocusLater(_searchInput); return; }
        var target = AllButtons().FirstOrDefault(b =>
            b.Tag is Game g ? g.Key == _lastFocus : b.DataContext as string == _lastFocus);
        FocusLater(target ?? Tiles().FirstOrDefault() ?? ShelfTiles().FirstOrDefault() ?? _groups.Children.OfType<Button>().FirstOrDefault() ?? FirstTab);
    }

    public override void FocusDefault() =>
        (Tiles().FirstOrDefault() ?? ShelfTiles().FirstOrDefault() ?? FirstTab)?.Focus();

    public override bool OnAction(NavAction action)
    {
        if (_searchInput.IsKeyboardFocused)
        {
            switch (action)
            {
                case NavAction.Accept:
                    // Enter on a keyboard goes to the results; A on a controller opens the on-screen keyboard.
                    if (Hints.Keyboard) (Tiles().FirstOrDefault() as UIElement ?? ShelfTiles().FirstOrDefault() ?? FirstTab)?.Focus();
                    else _ = TypeSearchAsync();
                    return true;
                case NavAction.Back when _searchInput.Text.Length > 0:
                    _searchInput.Text = "";
                    return true;
            }
        }
        if (action == NavAction.View)
        {
            ChooseLayout();
            return true;
        }
        if (action is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right) return Navigate(action);
        // LB / RB: the section before or after, from anywhere on the page.
        if (action is NavAction.LB or NavAction.RB && _tabIds.Count > 0)
        {
            int at = Math.Max(0, _tabIds.IndexOf(TabOf(_filter)));
            int next = (at + (action == NavAction.RB ? 1 : -1) + _tabIds.Count) % _tabIds.Count;
            UiSound.Page();
            SelectFilter(_tabIds[next], null);
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
            SelectFilter("collections", "col-tile:" + _filter[4..]);
            return true;
        }
        // On a store, category or status chip's games, Back goes to everything installed first.
        if (action == NavAction.Back && TabOf(_filter) == "all" && _filter != "all")
        {
            SelectFilter("all", "chip:all");
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

    /// <summary>
    /// The controller's d-pad and stick on this page, worked out from where things are on the page, scrolled out of
    /// sight or not. Windows' own moves only pick from what's already on screen, so with the next row (or the next
    /// tile along a shelf) out of sight the highlight stayed put and the page crept a little with each press, in
    /// every layout. Up / down go to the closest thing in the row above or below; left / right along the row, and in a
    /// grid from the end of one row to the start of the next. The new one is then scrolled fully into view.
    /// </summary>
    private bool Navigate(NavAction action)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is not FrameworkElement current || !IsAncestorOf(current)) return false;
        // Typing in the search box: left / right move the caret.
        if (current == _searchInput && action is NavAction.Left or NavAction.Right) return false;

        Rect At(FrameworkElement e)
        {
            try { return e.IsVisible && e.ActualWidth > 0 ? new Rect(e.TranslatePoint(new Point(0, 0), this), e.RenderSize) : Rect.Empty; }
            catch (InvalidOperationException) { return Rect.Empty; }
        }
        var here = At(current);
        if (here.IsEmpty) return false;
        var places = Focusables(this)
            .Where(e => e != current)
            .Select(e => (Item: e, At: At(e))).Where(x => !x.At.IsEmpty).ToList();
        double cx = here.Left + here.Width / 2, cy = here.Top + here.Height / 2;

        FrameworkElement? target = null;
        if (action is NavAction.Up or NavAction.Down)
        {
            bool down = action == NavAction.Down;
            // Things clearly above / below: their middle is past this one's edge.
            var beyond = places.Where(x => down ? x.At.Top + x.At.Height / 2 > here.Bottom - 2 : x.At.Top + x.At.Height / 2 < here.Top + 2).ToList();
            if (beyond.Count > 0)
            {
                // The nearest row, then the closest across in it.
                double edge = down ? beyond.Min(x => x.At.Top) : beyond.Max(x => x.At.Bottom);
                var row = beyond.Where(x => down ? x.At.Top < edge + Math.Max(12, x.At.Height / 2) : x.At.Bottom > edge - Math.Max(12, x.At.Height / 2));
                target = row.OrderBy(x => Math.Max(0, Math.Max(x.At.Left - cx, cx - x.At.Right))) // overlapping across first
                            .ThenBy(x => Math.Abs(x.At.Left + x.At.Width / 2 - cx)).First().Item;
            }
            else if (down && _pageScroll != null)
            {
                _pageScroll.ScrollToEnd(); // the last row: show the rest of it
                return true;
            }
        }
        else
        {
            bool right = action == NavAction.Right;
            // Same row: overlapping up and down with this one.
            var along = places.Where(x => x.At.Top < here.Bottom - 4 && x.At.Bottom > here.Top + 4 &&
                                          (right ? x.At.Left + x.At.Width / 2 > cx + 1 : x.At.Left + x.At.Width / 2 < cx - 1)).ToList();
            if (along.Count > 0)
                target = along.OrderBy(x => Math.Abs(x.At.Left + x.At.Width / 2 - cx)).ThenBy(x => Math.Abs(x.At.Top + x.At.Height / 2 - cy)).First().Item;
            else if (current is Button tile && Tiles() is var tiles && tiles.IndexOf(tile) is int i and >= 0)
            {
                // A grid: past the end of a row to the start of the next, and back.
                int next = i + (right ? 1 : -1);
                if (next >= 0 && next < tiles.Count) target = tiles[next];
            }
        }
        if (target == null) return true; // nothing that way: stay put (don't let Windows scroll the page by bits)
        target.Focus();
        Host.Rumble();
        // A little room around it for the highlight ring and a grown tile.
        target.BringIntoView(new Rect(-12, -36, target.ActualWidth + 24, target.ActualHeight + 56));
        return true;
    }

    /// <summary>Everything on the page the highlight can go to (buttons, the search box), on screen or scrolled away.</summary>
    private static IEnumerable<FrameworkElement> Focusables(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { IsVisible: false }) continue;
            if (child is Control { Focusable: true, IsTabStop: true, IsEnabled: true } control and (ButtonBase or TextBox)) { yield return control; continue; }
            foreach (var inner in Focusables(child)) yield return inner;
        }
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
        ShowLetter(Initial((Game)tiles[i].Tag));
    }

    /// <summary>The letter just jumped to, large in the middle for a moment.</summary>
    private void ShowLetter(char letter)
    {
        _letterText.Text = letter.ToString();
        if (Theme.ReduceMotion) { _letterBadge.Opacity = 0; return; }
        var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.92, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.92, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(820))));
        _letterBadge.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- toolbar actions

    /// <summary>Pick one of the six layouts (also in Settings > Games and library).</summary>
    private async void ChooseLayout()
    {
        int choice = await Host.AskAsync("View", "How the games are laid out. Also in Settings > Games and library.", Layouts.Select(l => l.Name).ToArray());
        if (choice < 0 || Layouts[choice].Id == Layout) return;
        Host.Settings.LibraryLayout = Layouts[choice].Id;
        if (Layout == "showcase") Host.Settings.LibraryView = "header"; // Showcase is rectangles
        Host.Settings.Save();
        if (ShelfLayout) _filter = "all";
        UpdateSpotlight();
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
        if (!SpotlightLayout) return;
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
        Lazy(box, cover, g);
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

    private string ViewLabel => (Layout switch { "posters" => "grid", "showcase" => "header", "mini" => "square", _ => Host.Settings.LibraryView }) switch { "grid" => "Grid", "header" => "Rectangle", _ => "Square" };

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

    private void QueueArt(Game game, Image picture, Action shown, FrameworkElement near) => _pendingArt[near] = (game, picture, shown);

    // ---------------------------------------------------------------- pictures only near the screen
    //
    // With hundreds of games, giving every tile its cover (and fetching wide or tall art for all of them) at once
    // decoded hundreds of pictures and kept them all drawn. Now a tile gets its picture when it comes within a
    // screen or so of view, and lets go of it again when it's scrolled far away; covers of games that aren't
    // installed, and the wide or tall art, are only fetched for tiles that come into view.

    private sealed class LazyTile
    {
        public required FrameworkElement Bounds;
        public required Image Cover;
        public required Game Game;
        public bool Bound;
    }

    private readonly List<LazyTile> _lazy = new();
    private readonly Dictionary<FrameworkElement, (Game Game, Image Picture, Action Shown)> _pendingArt = new();
    private readonly Dictionary<FrameworkElement, (Game Game, Image Picture, Action Shown)> _shownArt = new();
    private readonly HashSet<string> _coversAsked = new();
    private readonly System.Windows.Threading.DispatcherTimer _realizeSoon = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private bool _online;

    private void Lazy(FrameworkElement bounds, Image cover, Game game) => _lazy.Add(new LazyTile { Bounds = bounds, Cover = cover, Game = game });

    private void RealizeSoon()
    {
        _realizeSoon.Stop();
        _realizeSoon.Start();
    }

    private void Realize()
    {
        _realizeSoon.Stop();
        if (_pageScroll == null || _lazy.Count == 0) return;
        double vw = _pageScroll.ViewportWidth, vh = _pageScroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;
        var near = new Rect(-vw, -vh * 1.5, vw * 3, vh * 4);
        var far = new Rect(-vw * 2, -vh * 5, vw * 5, vh * 11);
        var missing = new List<Game>();
        foreach (var t in _lazy)
        {
            Rect r;
            try
            {
                if (!t.Bounds.IsVisible) continue;
                r = t.Bounds.TransformToAncestor(_pageScroll).TransformBounds(new Rect(t.Bounds.RenderSize));
            }
            catch (InvalidOperationException) { continue; }
            if (r.IntersectsWith(near))
            {
                if (!t.Bound)
                {
                    t.Cover.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = t.Game });
                    t.Bound = true;
                }
                if (t.Game.Cover == null && t.Game.IsSteam) missing.Add(t.Game);
                if (_pendingArt.Remove(t.Bounds, out var job)) { _artQueue.Add(job); _shownArt[t.Bounds] = job; }
            }
            else if (t.Bound && !r.IntersectsWith(far))
            {
                System.Windows.Data.BindingOperations.ClearBinding(t.Cover, Image.SourceProperty);
                t.Cover.Source = null;
                t.Bound = false;
                if (_shownArt.Remove(t.Bounds, out var shown)) { shown.Picture.Source = null; _pendingArt[t.Bounds] = shown; }
            }
        }
        if (_artQueue.Count > 0) LoadArt();
        if (_online && missing.Count > 0 && missing.Any(g => _coversAsked.Add(g.Key))) Host.EnsureCovers(missing);
    }

    private async void LoadArt()
    {
        if (_artQueue.Count == 0) return;
        var cts = _artCts ??= new CancellationTokenSource();
        var jobs = _artQueue.ToList();
        _artQueue.Clear();
        bool header = (Layout switch { "posters" => "grid", "showcase" => "header", "mini" => "square", _ => Host.Settings.LibraryView }) == "header";
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

/// <summary>
/// Edit information, for games added by hand: the title, the program it starts, launch options, the release year,
/// and its artwork, all in one place.
/// </summary>
public sealed class EditCustomGameScreen : ListScreen
{
    private readonly Game _game;

    public EditCustomGameScreen(Game game) : base("Edit information – " + game.Name) => _game = game;

    private CustomGame? Entry => Host.Library.CustomGames.FirstOrDefault(c => c.Id == _game.LaunchId);

    protected override void Build()
    {
        var entry = Entry;
        if (entry == null) { AddNote("This game isn't in your library any more."); return; }
        var mine = Host.Library.Find(_game.Key);
        string title = string.IsNullOrWhiteSpace(mine?.Title) ? entry.Name : mine!.Title!;

        AddHeader("Game");
        AddRow("Title", title, "", click: EditTitle, subtitle: "The name on its tile and pages");
        AddRow("Game program", Path.GetFileName(entry.Path), "", click: ChooseProgram, subtitle: entry.Path);
        AddRow("Launch options", string.IsNullOrWhiteSpace(entry.Arguments) ? "None" : entry.Arguments, "", click: EditArguments,
            subtitle: "Passed to the program when it starts, such as -windowed or -skipintro");
        AddRow("Release year", entry.ReleaseYear?.ToString() ?? "Not set", "", click: EditYear);

        AddHeader("Artwork");
        AddRow("Change artwork", glyph: "", click: () => Host.Push(new CoverPickerScreen(_game)), subtitle: "Square cover, icon, tall grid, header capsule, hero and logo");

        AddHeader("Folder");
        AddRow("Show the program in its folder", glyph: "", click: () =>
        {
            if (File.Exists(entry.Path)) ProcessUtil.Open("explorer.exe", "/select,\"" + entry.Path + "\"");
        });
        if (!File.Exists(entry.Path)) AddNote("The program isn't at that place any more. Choose it again above.");
    }

    private void Save(Action<CustomGame> change)
    {
        if (Entry is not { } entry) return;
        change(entry);
        Host.UpdateCustomGame(entry);
        Rebuild();
    }

    private async void EditTitle()
    {
        if (Entry is not { } entry) return;
        var text = await Host.PromptAsync("Game title", null, _game.Name, "OK");
        if (string.IsNullOrWhiteSpace(text)) return;
        Save(e =>
        {
            e.Name = text.Trim();
            // A title set from Options before wins over the saved name, so it goes too.
            if (Host.Library.Find(_game.Key) is { } o) o.Title = null;
        });
        _game.Name = text.Trim();
    }

    private async void ChooseProgram()
    {
        var picked = await Host.PickFileAsync("Choose the game's program", new[] { ".exe", ".lnk", ".bat", ".cmd", ".url" });
        if (picked != null) Save(e => e.Path = picked);
    }

    private async void EditArguments()
    {
        if (Entry is not { } entry) return;
        var text = await Host.PromptAsync("Launch options", "Leave it empty for none.", entry.Arguments ?? "", "OK");
        if (text != null) Save(e => e.Arguments = text.Trim().Length == 0 ? null : text.Trim());
    }

    private async void EditYear()
    {
        if (Entry is not { } entry) return;
        var text = await Host.PromptAsync("Release year", "Four digits, like 2024. Leave it empty to clear it.", entry.ReleaseYear?.ToString() ?? "", "OK");
        if (text == null) return;
        if (text.Trim().Length == 0) { Save(e => e.ReleaseYear = null); return; }
        if (int.TryParse(text.Trim(), out int year) && year is > 1950 and < 2200) Save(e => e.ReleaseYear = year);
        else await Host.AskAsync("That isn't a year", "Use four digits, like 2024.", "OK");
    }
}
