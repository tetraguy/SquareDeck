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
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Changed(); }
    }

    /// <summary>Width of the tile on the home row. Must match what MainWindow uses for scrolling.</summary>
    public double TileWidth => IsContinueTile ? 640 : 270;

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
    public ImageSource? Cover
    {
        get => _cover;
        set { _cover = value; Changed(); }
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
