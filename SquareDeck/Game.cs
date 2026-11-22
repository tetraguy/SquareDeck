using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SquareDeck;

public enum ItemKind { SteamGame, External, Custom, App, AddTile, LibraryTile, ContinueTile, MoreAppsTile }

/// <summary>One tile on the home row: a Steam game, an added app, or the "Add app" tile.</summary>
public sealed class Game : INotifyPropertyChanged
{
    private string _name = "";
    private ImageSource? _cover;
    private int? _releaseYear;
    private bool _isFavorite;
    private bool _isRunning;
    private string _updateText = "";
    private bool _usesIcon;
    private Brush? _tileBackground;
    private Uri? _animatedCover;
    private string _subtitle = "";

    public ItemKind Kind { get; init; } = ItemKind.SteamGame;

    /// <summary>Steam app id (0 for apps).</summary>
    public int AppId { get; init; }

    /// <summary>Start-menu application id for an app, or the saved id of a custom game.</summary>
    public string LaunchId { get; init; } = "";

    /// <summary>What a custom or other-store game starts: a program, a link, or a shell:AppsFolder item.</summary>
    public string LaunchPath { get; init; } = "";

    /// <summary>Command-line arguments passed to <see cref="LaunchPath"/>, if any.</summary>
    public string? LaunchArgs { get; init; }

    /// <summary>Store a game from another launcher came from: "Epic", "GOG", "Xbox", "Battle.net", "Steam shortcut".</summary>
    public string Source { get; init; } = "";

    /// <summary>When the item was last played (or installed/added, if never played). Orders the home row.</summary>
    public DateTime RecentAt { get; set; }

    /// <summary>Name as Steam / Windows reports it, before any custom title.</summary>
    public string OriginalName { get; init; } = "";

    public string? InstallDir { get; init; }

    /// <summary>False for a Steam game that is owned but not installed on this PC.</summary>
    public bool IsInstalled { get; init; } = true;
    public DateTime? LastPlayed { get; init; }
    public long SizeOnDisk { get; init; }

    /// <summary>Stable key used for saved per-item settings and cache files.</summary>
    public string Key => Kind switch
    {
        ItemKind.SteamGame => "steam:" + AppId,
        ItemKind.App => "app:" + LaunchId,
        ItemKind.Custom => "custom:" + LaunchId,
        ItemKind.External => "ext:" + Source + ":" + LaunchId,
        ItemKind.LibraryTile => "library",
        ItemKind.ContinueTile => "continue",
        ItemKind.MoreAppsTile => "moreapps",
        _ => "add"
    };

    public bool IsSteam => Kind == ItemKind.SteamGame;
    public bool IsCustom => Kind == ItemKind.Custom;
    public bool IsExternal => Kind == ItemKind.External;
    public bool IsGame => IsSteam || IsCustom || IsExternal;

    /// <summary>Where the game comes from, for the library's filter.</summary>
    public string SourceName => IsSteam ? "Steam" : IsCustom ? "Custom" : Source;
    public bool IsApp => Kind == ItemKind.App;
    public bool IsAddTile => Kind == ItemKind.AddTile;
    public bool IsLibraryTile => Kind == ItemKind.LibraryTile;
    public bool IsMoreAppsTile => Kind == ItemKind.MoreAppsTile;

    /// <summary>The wide "Continue playing" tile. It stands in for <see cref="Target"/>.</summary>
    public bool IsContinueTile => Kind == ItemKind.ContinueTile;

    /// <summary>The real game behind a "Continue playing" tile.</summary>
    public Game? Target { get; init; }

    /// <summary>This item, or the game it stands in for.</summary>
    public Game Actual => Target ?? this;

    /// <summary>Second line on the "Continue playing" tile, e.g. "Last played 2 hours ago".</summary>
    /// <summary>The game's logo on the "Continue where you left off" tile (its name is shown until it arrives, or without one).</summary>
    public ImageSource? ContinueLogo
    {
        get => _continueLogo;
        set { _continueLogo = value; Changed(); Changed(nameof(HasContinueLogo)); }
    }
    private ImageSource? _continueLogo;
    public bool HasContinueLogo => _continueLogo != null;

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Changed(); }
    }

    /// <summary>Width of the tile on the home row. Must match what MainWindow uses for scrolling.</summary>
    public double TileWidth => Wide ? WideWidth : Portrait ? PortraitWidth : IsContinueTile ? 540 : 270;

    /// <summary>Home row in wide "header capsule" tiles (460×215 art), instead of square or tall ones.</summary>
    public static bool Wide { get; set; }
    public static double WideWidth { get; set; } = 560;

    private ImageSource? _headerCover;
    /// <summary>The wide header capsule art, for wide home tiles.</summary>
    public ImageSource? HeaderCover
    {
        get => _headerCover;
        set { _headerCover = value; Changed(); Changed(nameof(TileImage)); }
    }
    public bool HeaderTried { get; set; }

    /// <summary>
    /// Width of a poster tile: set by the style so the picture area inside the ring and gap is exactly 2:3. A fixed 180
    /// left it narrower than 2:3, so the sides of the tall art were cut off.
    /// </summary>
    public static double PortraitWidth { get; set; } = 180;

    /// <summary>Styles with tall poster-shaped (2:3) tiles instead of square ones (Waves, Desktop, Stage...).</summary>
    public static bool Portrait { get; set; }

    private ImageSource? _portraitCover;

    /// <summary>Tall (2:3) cover art from SteamGridDB, for the poster-shaped styles. Null until loaded.</summary>
    public ImageSource? PortraitCover
    {
        get => _portraitCover;
        set { _portraitCover = value; Changed(); Changed(nameof(TileImage)); }
    }

    private (string Path, int Width)? _parkedCover, _parkedPortrait;

    /// <summary>
    /// Lets go of the cover pictures while a game is being played, remembering their files so they can be read back
    /// afterwards. Pictures that didn't come from a file (app icons) are small and stay.
    /// </summary>
    public void Park()
    {
        if (_cover != null && _parkedCover == null && Services.ImageLoader.OriginOf(_cover) is { } c) { _parkedCover = c; Cover = null; }
        if (_portraitCover != null && _parkedPortrait == null && Services.ImageLoader.OriginOf(_portraitCover) is { } p) { _parkedPortrait = p; PortraitCover = null; }
    }

    public bool Parked => _parkedCover != null || _parkedPortrait != null;

    /// <summary>Reads the parked pictures back in (off the interface thread) and puts them back on the tile.</summary>
    /// A picture that can't be read back (its file was replaced or deleted meanwhile) is left empty; the caller then
    /// loads that tile's art the usual way (see MainWindow.RestoreMissingArt).
    public async Task UnparkAsync()
    {
        var cover = _parkedCover;
        var portrait = _parkedPortrait;
        _parkedCover = _parkedPortrait = null;
        try
        {
            if (cover is { } c && _cover == null)
            {
                var image = await Task.Run(() => Services.ImageLoader.FromFile(c.Path, c.Width));
                if (_cover == null && image != null) Cover = image;
            }
            if (portrait is { } p && _portraitCover == null)
            {
                var image = await Task.Run(() => Services.ImageLoader.FromFile(p.Path, p.Width));
                if (_portraitCover == null && image != null) PortraitCover = image;
                else if (image == null) PortraitTried = false; // looked for again
            }
        }
        catch (Exception) { }
    }

    /// <summary>True once the tall cover has been looked for (found or not), so it isn't asked for again.</summary>
    public bool PortraitTried { get; set; }

    /// <summary>The picture on the tile: the tall cover in poster styles (when there is one), otherwise the square one.</summary>
    public ImageSource? TileImage
    {
        get
        {
            var image = Wide && _headerCover != null ? _headerCover : Portrait && _portraitCover != null ? _portraitCover : Cover;
            return CoverEffect != null && image != null ? CoverEffect(image, () => Changed(nameof(TileImage))) : image;
        }
    }

    /// <summary>Experimental "Cover art effect": changes how Home covers are drawn (null for none).</summary>
    public static Func<ImageSource, Action, ImageSource>? CoverEffect;

    /// <summary>The wide "Continue where you left off" face (not in poster styles: the tile is too narrow for it).</summary>
    public bool ShowContinueFace => IsContinueTile && !Portrait && !Wide;

    /// <summary>The style changed between square and poster tiles.</summary>
    public void RefreshShape()
    {
        Changed(nameof(TileWidth));
        Changed(nameof(TileImage));
        Changed(nameof(ShowContinueFace));
    }

    /// <summary>Tiles that open a page instead of starting something.</summary>
    public bool IsActionTile => IsAddTile || IsLibraryTile || IsMoreAppsTile;

    /// <summary>Icon drawn on an action tile.</summary>
    public string TileGlyph => IsAddTile ? "\uE710" : IsLibraryTile ? "\uE8F1" : IsMoreAppsTile ? "\uE712" : "";

    public string Name
    {
        get => _name;
        set { _name = value; Changed(); }
    }

    /// <summary>Square cover art. Null until loaded (or if none exists).</summary>
    private ImageSource? _dust;

    /// <summary>Experimental "Dusty tiles": dust over the cover of a game not played for months (null for none).</summary>
    public ImageSource? DustImage
    {
        get => _dust;
        set { if (!ReferenceEquals(_dust, value)) { _dust = value; Changed(); } }
    }

    public ImageSource? Cover
    {
        get => _cover;
        set { _cover = value; Changed(); Changed(nameof(TileImage)); }
    }

    public int? ReleaseYear
    {
        get => _releaseYear;
        set { _releaseYear = value; Changed(); Changed(nameof(InfoLine)); }
    }

    /// <summary>On the user's Steam wishlist (not owned).</summary>
    public bool OnWishlist { get; init; }

    /// <summary>Store price for a wishlist game, e.g. "$19.99" or "Free".</summary>
    public string Price { get; set; } = "";
    public int DiscountPercent { get; set; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { _isFavorite = value; Changed(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set { if (_isRunning == value) return; _isRunning = value; Changed(); }
    }

    /// <summary>True for an app showing its own icon: drawn centered on a gradient instead of filling the tile.</summary>
    public bool UsesIcon
    {
        get => _usesIcon;
        set { if (_usesIcon == value) return; _usesIcon = value; Changed(); }
    }

    /// <summary>
    /// The icon is artwork from SteamGridDB, so it fills the whole tile like a cover. Icons taken from a
    /// program or app are small and stay centered on their color wash.
    /// </summary>
    public bool IconFills
    {
        get => _iconFills;
        set { if (_iconFills == value) return; _iconFills = value; Changed(); }
    }
    private bool _iconFills;

    /// <summary>
    /// The app's icon only exists at a small size (32–64 px), so it's drawn smaller rather than blown up and blurry.
    /// </summary>
    public bool IconIsSmall
    {
        get => _iconIsSmall;
        set { if (_iconIsSmall == value) return; _iconIsSmall = value; Changed(); }
    }
    private bool _iconIsSmall;

    /// <summary>Gradient behind an app icon, built from the icon's own colors.</summary>
    public Brush? TileBackground
    {
        get => _tileBackground;
        set { _tileBackground = value; Changed(); }
    }

    /// <summary>Animated cover (a short looping video file), set only while the tile is highlighted.</summary>
    public Uri? AnimatedCover
    {
        get => _animatedCover;
        set
        {
            if (_animatedCover == value) return;
            _animatedCover = value;
            Changed();
            Changed(nameof(HasAnimatedCover));
        }
    }

    public bool HasAnimatedCover => _animatedCover != null;

    /// <summary>"Update queued" / "Updating 40%" for Steam games; empty when up to date.</summary>
    public string UpdateText
    {
        get => _updateText;
        set
        {
            if (_updateText == value) return;
            _updateText = value;
            Changed();
            Changed(nameof(HasUpdate));
            Changed(nameof(InfoLine));
        }
    }

    public bool HasUpdate => _updateText.Length > 0;

    public string InfoLine
    {
        get
        {
            if (IsAddTile) return "Add an installed app, or get one from the Microsoft Store";
            if (Target != null) return Target.InfoLine;
            if (IsLibraryTile) return "All installed games, with filters and categories";
            if (IsMoreAppsTile) return "Every app you have added";
            if (IsApp) return "App";
            if (IsCustom) return "Custom game";
            if (!IsInstalled) return "Not installed";
            if (IsExternal) return Source;
            var parts = new List<string>();
            if (HasUpdate) parts.Add(UpdateText);
            if (ReleaseYear is int y) parts.Add($"Released {y}");
            if (LastPlayed is DateTime d) parts.Add($"Last played {d:MMM d, yyyy}");
            if (SizeOnDisk > 0) parts.Add($"{SizeOnDisk / 1024d / 1024d / 1024d:0.0} GB");
            return string.Join("   ·   ", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
