using System.Windows;
using System.Windows.Controls;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Search Spotify or YouTube Music and open your playlists from the Music page.
/// YouTube Music opens in the built-in browser (sign in there once); Spotify results come from the
/// Spotify Web API and play on your active Spotify device, or open in the Spotify app.
/// </summary>
public sealed class MusicSearchScreen : ListScreen
{
    private readonly CancellationTokenSource _cts = new();
    private SpotifyClient? _spotify;
    private List<SpotifyItem>? _results;
    private string _resultsTitle = "";
    private string? _status;
    private bool _busy;

    public MusicSearchScreen() : base("Search music", "{Select}  Open      {Back}  Back") { }

    public override void OnShown()
    {
        _spotify = new SpotifyClient(Host.Settings);
        Rebuild();
    }

    public override void OnResumed() => Rebuild();

    public override void OnClosed() => _cts.Cancel();

    private bool IsSpotify => Host.Settings.MusicService == "spotify";

    protected override void Build()
    {
        var s = Host.Settings;
        AddRow("Service", IsSpotify ? "Spotify" : "YouTube Music", IsSpotify ? "" : "", click: () =>
        {
            s.MusicService = IsSpotify ? "youtube" : "spotify";
            s.Save();
            _results = null;
            _status = null;
            RebuildFromTop();
        });

        if (IsSpotify && !(_spotify?.HasClientId ?? false))
        {
            AddNote("Spotify needs a free app of your own before the launcher can search it. Create one at developer.spotify.com, " +
                    "add the redirect URI " + SpotifyClient.RedirectUri + ", then paste its Client ID in Settings > Sound > Multimedia page.");
            AddRow("Open the Spotify developer dashboard", glyph: "", click: () => ProcessUtil.Open("https://developer.spotify.com/dashboard"));
            AddRow("Open the Spotify app instead", glyph: "", click: () => ProcessUtil.Open("spotify:"));
            return;
        }
        if (IsSpotify && !_spotify!.IsSignedIn)
        {
            AddRow("Sign in to Spotify", glyph: "", click: () => _ = SignInAsync());
            if (_status != null) AddNote(_status);
            return;
        }

        AddRow("Search…", glyph: "", click: () => _ = SearchAsync());
        AddRow("My playlists", glyph: "", click: () => _ = PlaylistsAsync());
        if (!IsSpotify)
        {
            AddRow("Open YouTube Music", glyph: "", click: () => Host.Push(BrowserScreen.Open(Host, "https://music.youtube.com/")));
            AddNote("YouTube Music opens inside the launcher. Sign in there once and it stays signed in.");
            return;
        }

        if (_status != null) AddNote(_status);
        if (_busy) { AddNote("Searching…"); return; }
        if (_results == null) return;

        AddHeader(_resultsTitle);
        if (_results.Count == 0) AddNote("Nothing found.");
        foreach (var item in _results)
        {
            var it = item;
            var picture = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill };
            if (it.ImageUrl.Length > 0) _ = LoadAsync(picture, it.ImageUrl);
            AddRow(it.Name, it.Detail, icon: picture, click: () => _ = PlayAsync(it));
        }
    }

    private static async Task LoadAsync(Image view, string url)
    {
        try { view.Source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 96)); }
        catch (Exception) { }
    }

    private async Task SignInAsync()
    {
        try
        {
            await Host.RunBusyAsync("Finish signing in to Spotify in your browser…", () => _spotify!.SignInAsync(_cts.Token));
            _status = null;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        if (Host.IsTop(this)) Rebuild();
    }

    private async Task SearchAsync()
    {
        var text = await Host.PromptAsync(IsSpotify ? "Search Spotify" : "Search YouTube Music", "A song, artist, album or playlist.", "", "Search");
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!IsSpotify)
        {
            Host.Push(BrowserScreen.Open(Host, "https://music.youtube.com/search?q=" + Uri.EscapeDataString(text.Trim())));
            return;
        }
        await RunAsync("Results for “" + text.Trim() + "”", ct => _spotify!.SearchAsync(text.Trim(), ct));
    }

    private async Task PlaylistsAsync()
    {
        if (!IsSpotify)
        {
            Host.Push(BrowserScreen.Open(Host, "https://music.youtube.com/library/playlists"));
            return;
        }
        await RunAsync("Your playlists", ct => _spotify!.MyPlaylistsAsync(ct));
    }

    private async Task RunAsync(string title, Func<CancellationToken, Task<List<SpotifyItem>>> fetch)
    {
        _busy = true;
        _status = null;
        Rebuild();
        try
        {
            _results = await Task.Run(() => fetch(_cts.Token));
            _resultsTitle = title;
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            _results = null;
            _status = "Spotify didn't answer: " + ex.Message;
        }
        _busy = false;
        if (Host.IsTop(this)) RebuildFromTop();
    }

    private async Task PlayAsync(SpotifyItem item)
    {
        string? problem;
        try { problem = await Task.Run(() => _spotify!.PlayAsync(item, _cts.Token)); }
        catch (Exception ex) { problem = ex.Message; }
        if (problem == null)
        {
            _status = "Playing " + item.Name + ".";
            Rebuild();
            return;
        }

        // No Spotify open anywhere, or no Premium: hand it to the Spotify app, which can always play it.
        ProcessUtil.Open(item.Uri);
        _status = problem switch
        {
            "nodevice" => "Opened in the Spotify app (no Spotify was playing anywhere to start it on).",
            "premium" => "Opened in the Spotify app (starting playback from other apps needs Spotify Premium).",
            _ => "Opened in the Spotify app. " + problem
        };
        Rebuild();
    }
}
