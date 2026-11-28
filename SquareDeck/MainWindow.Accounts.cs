using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Store accounts (9.0.8, out of Experimental in 9.0.20): the Epic and Xbox games on your accounts that aren't installed join the
/// library's "Not installed" games (with the Steam ones). Choosing one asks that store's app to install it.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<string, Game> _accountTiles = new();
    private bool _accountsLoading;

    private static string Plain(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Puts the account games that aren't installed into the not-installed list (and takes out the rest). A game owned
    /// on several stores shows once: Steam's tile if there is one, else Epic's, else Xbox's, with the others folded into
    /// it (a count on the tile, and a choice of store when it's opened). A game installed from any store isn't listed.
    /// </summary>
    public void MergeAccountGames()
    {
        NotInstalledGames.RemoveAll(g => g.IsExternal && !g.IsInstalled);
        foreach (var g in NotInstalledGames) g.OtherStores.Clear();
        foreach (var g in _accountTiles.Values) g.OtherStores.Clear();

        static string NameOf(Game g) => Plain(string.IsNullOrWhiteSpace(g.OriginalName) ? g.Name : g.OriginalName);
        var installedNames = AllGames.Where(g => g.IsGame && g.IsInstalled).Select(NameOf).Where(n => n.Length > 0).ToHashSet();
        var shown = new Dictionary<string, Game>();
        foreach (var g in NotInstalledGames.Where(g => g.IsSteam))
            if (NameOf(g) is { Length: > 0 } n) shown.TryAdd(n, g);

        var candidates = new List<Game>();
        if (Labs.On(StoreAccounts.EpicLab) && StoreAccounts.EpicSignedIn)
        {
            var installed = AllGames.Where(g => g.IsExternal && g.Source == "Epic").Select(g => g.LaunchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates.AddRange(StoreAccounts.EpicGames.Where(e => !installed.Contains(e.Id)).Select(AccountTile));
        }
        if (Labs.On(StoreAccounts.XboxLab) && StoreAccounts.XboxSignedIn)
            candidates.AddRange(StoreAccounts.XboxGames.Select(AccountTile));

        foreach (var g in candidates)
        {
            if (IsHidden(g)) continue;
            string n = NameOf(g);
            if (n.Length > 0 && installedNames.Contains(n)) continue; // already installed (from this store or another)
            if (n.Length > 0 && shown.TryGetValue(n, out var first))
            {
                // Same game on another store: fold it in (once per store).
                if (first.Source != g.Source && first.SourceName != g.SourceName && first.OtherStores.All(o => o.Source != g.Source)) first.OtherStores.Add(g);
                continue;
            }
            if (n.Length > 0) shown[n] = g;
            NotInstalledGames.Add(g);
        }
    }

    private Game AccountTile(StoreAccounts.Entry e)
    {
        string key = $"ext:{e.Store}:{e.Id}";
        if (_accountTiles.TryGetValue(key, out var tile)) return tile;
        tile = new Game
        {
            Kind = ItemKind.External, Source = e.Store, LaunchId = e.Id, Name = e.Name, OriginalName = e.Name, IsInstalled = false,
            LaunchPath = e.Store == "Epic" ? StoreAccounts.EpicLaunchUri(e.Id) : StoreAccounts.XboxGameUri(e.Id),
        };
        var o = Library.Find(tile.Key);
        if (!string.IsNullOrWhiteSpace(o?.Title)) tile.Name = o.Title;
        tile.IsFavorite = o?.Favorite == true;
        if (e.GamePass) tile.Subtitle = "Game Pass";
        _accountTiles[key] = tile;
        return tile;
    }

    /// <summary>
    /// Gets the account games (from the saved copy at once, then from the stores when it's old or asked for).
    /// True unless a store was asked and couldn't answer.
    /// </summary>
    public async Task<bool> LoadAccountGamesAsync(bool refresh = false, string? store = null)
    {
        MergeAccountGames();
        LibraryChanged?.Invoke();
        if (_accountsLoading && store == null) return true;
        bool ok = true;
        _accountsLoading = true;
        try
        {
            bool Due(DateTime at) => refresh || DateTime.Now - at > TimeSpan.FromHours(6);
            if (store is null or "Epic" && Labs.On(StoreAccounts.EpicLab) && StoreAccounts.EpicSignedIn && Due(StoreAccounts.EpicUpdated))
                ok &= await Task.Run(() => StoreAccounts.RefreshEpicAsync(CancellationToken.None)) != null;
            if (store is null or "Xbox" && Labs.On(StoreAccounts.XboxLab) && StoreAccounts.XboxSignedIn && Due(StoreAccounts.XboxUpdated))
                ok &= await Task.Run(() => StoreAccounts.RefreshXboxAsync(CancellationToken.None)) != null;
        }
        catch (Exception) { ok = false; }
        finally { _accountsLoading = false; }
        MergeAccountGames();
        LibraryChanged?.Invoke();
        return ok;
    }

    /// <summary>
    /// A not-installed game owned on several stores: asks which store to install it from, then that store's own
    /// install. Null when cancelled; the game itself when it's only on one store.
    /// </summary>
    private async Task<Game?> PickStoreAsync(Game game)
    {
        if (game.OtherStores.Count == 0) return game;
        var all = new List<Game> { game };
        all.AddRange(game.OtherStores);
        var options = all.Select(g =>
        {
            string store = g.IsSteam ? "Steam" : g.Source == "Epic" ? "Epic Games" : g.Subtitle == "Game Pass" ? "Xbox (PC Game Pass)" : "Xbox";
            string glyph = g.IsSteam ? "\uE7FC" : g.Source == "Epic" ? "\uE719" : "\uE990";
            return ($"Install from {store}", glyph);
        }).ToArray();
        int choice = await AskWithIconsAsync(game.Name, $"You have this game on {all.Count} stores. Which one would you like to install it from?", options);
        return choice >= 0 && choice < all.Count ? all[choice] : null;
    }

    /// <summary>A game from an account that isn't installed: install it through its store's app.</summary>
    private async Task OfferAccountInstallAsync(Game game)
    {
        bool epic = game.Source == "Epic";
        var entry = (epic ? StoreAccounts.EpicGames : StoreAccounts.XboxGames).FirstOrDefault(e => e.Id == game.LaunchId);
        int c = await AskAsync(game.Name,
            epic ? "This game is on your Epic Games account but isn't installed on this PC. The Epic Games Launcher installs it (it asks where)."
                 : (entry?.GamePass == true ? "This game is in PC Game Pass. " : "This game is on your Xbox account. ") + "The Xbox app installs it on this PC.",
            "Install", epic ? "Open in the Epic Games Launcher" : "View in the Microsoft Store", "Cancel");
        if (c == 0)
        {
            if (epic) ProcessUtil.Open(entry != null ? StoreAccounts.EpicInstallUri(entry) : $"com.epicgames.launcher://apps/{Uri.EscapeDataString(game.LaunchId)}?action=install");
            else ProcessUtil.Open(StoreAccounts.XboxGameUri(game.LaunchId));
            ShowToast(epic ? "EPIC GAMES" : "XBOX", "Installing " + game.Name, epic ? "In the Epic Games Launcher. It appears in SquareDeck once it's installed." : "In the Xbox app. It appears in SquareDeck once it's installed.", "", "download");
        }
        else if (c == 1)
            ProcessUtil.Open(epic ? "com.epicgames.launcher://" : StoreAccounts.StoreUri(game.LaunchId));
    }

    /// <summary>From the account page: the game's tile if it's in the library, else its install question.</summary>
    public void OpenStoreGame(StoreAccounts.Entry e)
    {
        var installed = AllGames.FirstOrDefault(g => g.IsExternal && g.Source == e.Store && (g.LaunchId == e.Id || Plain(g.OriginalName) == Plain(e.Name)));
        if (installed != null) { _ = OpenOptionsAsync(installed); return; }
        _ = OfferAccountInstallAsync(AccountTile(e));
    }
}
