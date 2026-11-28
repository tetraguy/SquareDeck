using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>"Epic Games account" and "Xbox account" (9.0.8, out of Experimental in 9.0.20): sign in, refresh, and what shows.</summary>
public sealed class StoreAccountScreen : ListScreen
{
    private readonly string _store;
    private bool _refreshing;

    public StoreAccountScreen(string store) : base(store == "Epic" ? "Epic Games account" : "Xbox account") => _store = store;

    private bool Epic => _store == "Epic";
    private bool SignedIn => Epic ? StoreAccounts.EpicSignedIn : StoreAccounts.XboxSignedIn;

    protected override void Build()
    {
        var games = Epic ? StoreAccounts.EpicGames : StoreAccounts.XboxGames;
        var updated = Epic ? StoreAccounts.EpicUpdated : StoreAccounts.XboxUpdated;
        string who = Epic ? StoreAccounts.EpicName : StoreAccounts.XboxName;
        AddHeader("Account");
        if (!SignedIn)
        {
            AddRow(Epic ? "Sign in to Epic Games" : "Sign in with Microsoft", null, "\uE77B", click: () => _ = SignInAsync(),
                subtitle: Epic ? "On Epic's own page. Your Epic games then show in the library, installed or not."
                               : "With the account you use for Xbox. Your PC games then show in the library, installed or not.");
            AddNote(Epic
                ? "Installing, playing and uninstalling still go through the Epic Games Launcher: SquareDeck asks it to, and never downloads game files itself. You'll need the launcher installed."
                : "Installing goes through the Xbox app (or the Microsoft Store). Xbox Live lists the PC games you've played on the account; turn on the Game Pass catalog below to see everything PC Game Pass includes.");
            return;
        }
        AddRow("Signed in" + (who.Length > 0 ? " as " + who : ""), null, "\uE73E",
            subtitle: games.Count > 0 ? $"{games.Count} games  ·  updated {Ui.Ago(updated)}" : "Your games haven't been fetched yet");
        AddRow(_refreshing ? "Refreshing…" : "Refresh my games", null, "\uE72C", click: () => _ = RefreshAsync(),
            subtitle: "New purchases and Game Pass changes; SquareDeck also checks on its own every few hours");
        if (!Epic)
            AddRow("Show the PC Game Pass catalog", StoreAccounts.ShowGamePass ? "On" : "Off", "\uE7FC", click: async () =>
            {
                StoreAccounts.ShowGamePass = !StoreAccounts.ShowGamePass;
                UiSound.Select();
                await RefreshAsync();
            }, subtitle: "Every game in PC Game Pass right now, marked as such (for members)");
        AddRow("Open the " + (Epic ? "Epic Games Launcher" : "Xbox app"), null, "\uE8A7",
            click: () => ProcessUtil.Open(Epic ? "com.epicgames.launcher://" : "msxbox://"), subtitle: "For downloads, settings and anything else");
        AddRow("Sign out", null, "\uE7E8", click: async () =>
        {
            int c = await Host.AskAsync("Sign out?", "Your " + (Epic ? "Epic" : "Xbox") + " games that aren't installed leave the library. Installed games stay.", "Sign out", "Cancel");
            if (c != 0) return;
            if (Epic) StoreAccounts.EpicSignOut(); else StoreAccounts.XboxSignOut();
            Host.MergeAccountGames();
            Rebuild();
        });
        if (games.Count > 0)
        {
            AddHeader("Your games");
            AddNote("They're in the library (Not installed). Choosing one there installs it through the " + (Epic ? "Epic Games Launcher." : "Xbox app."));
            foreach (var g in games.Take(400))
            {
                var game = g;
                AddRow(g.Name, g.GamePass ? "Game Pass" : null, "\uE7FC", click: () => Host.OpenStoreGame(game));
            }
        }
    }

    private async Task SignInAsync()
    {
        var screen = new StoreSignInScreen(_store);
        Host.Push(screen);
        if (await screen.Result) await RefreshAsync();
        else Rebuild();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        Rebuild();
        bool ok = await Host.LoadAccountGamesAsync(refresh: true, store: _store);
        _refreshing = false;
        if (!ok)
        {
            string why = Epic ? StoreAccounts.EpicError : StoreAccounts.XboxError;
            Host.ShowToast(Epic ? "EPIC GAMES" : "XBOX", "Couldn't get your games", why.Length > 0 ? why : "Check the internet connection, or sign in again.", "", "info");
        }
        Rebuild();
    }
}
