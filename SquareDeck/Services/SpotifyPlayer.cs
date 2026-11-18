using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SquareDeck.Services;

/// <summary>
/// Spotify playing inside SquareDeck: Spotify's own Web Playback SDK runs in a tiny hidden web view, which makes
/// SquareDeck a Spotify device ("SquareDeck") like a phone or a speaker. Music then comes out of the launcher itself, with no
/// Spotify app needed, and the Multimedia page controls it like any other device. Needs Spotify Premium (Spotify's
/// rule for the SDK) and a sign-in with the "streaming" permission.
/// </summary>
public static class SpotifyPlayer
{
    private const string VirtualHost = "squaredeck-player.local";

    private static WebView2? _web;
    private static Task<string?>? _starting;
    private static TaskCompletionSource<string?>? _ready;
    private static SpotifyClient? _client;

    /// <summary>The device id Spotify gave the built-in player, once it's connected.</summary>
    public static string? DeviceId { get; private set; }

    /// <summary>Why the built-in player can't play, in plain words (null when it's fine).</summary>
    public static string? Problem { get; private set; }

    public static bool IsPlaying { get; private set; }

    public sealed record TrackInfo(string Title, string Artist, string Album, string? ArtUrl);

    /// <summary>The song the built-in player has loaded (playing or paused), with its cover.</summary>
    public static TrackInfo? Track { get; private set; }

    public static string DeviceName => "SquareDeck";

    /// <summary>Raised (on the UI thread) when the player connects, stops, or starts / stops playing.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Starts the built-in player if it isn't running and waits for Spotify to accept it as a device. Returns the device
    /// id, or null (with <see cref="Problem"/> saying why).
    /// </summary>
    public static Task<string?> EnsureStartedAsync(MainWindow host, Settings settings)
    {
        if (DeviceId != null && _web != null) return Task.FromResult<string?>(DeviceId);
        return _starting ??= StartAsync(host, settings);
    }

    private static async Task<string?> StartAsync(MainWindow host, Settings settings)
    {
        try
        {
            Problem = null;
            _client = new SpotifyClient(settings);
            if (!_client.IsSignedIn) { Problem = "Sign in to Spotify first."; return null; }
            if (_client.NeedsNewSignIn) { Problem = "Sign in to Spotify again: playing inside SquareDeck needs one more permission."; return null; }

            // The page is served from a local folder under an https name, because Spotify's player (and the copy
            // protection it uses) only runs on secure pages.
            string folder = Path.Combine(Settings.DataDir, "SpotifyPlayer", "page");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "player.html"), Page.Replace("%NAME%", JsonSerializer.Serialize(DeviceName)));

            // Autoplay allowed: there's no one to click "play" on a page nobody sees.
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "SpotifyPlayer", "profile"), options);

            // A 2-pixel view just outside the window: it has to be part of the window to run, but nobody sees it.
            _web = new WebView2
            {
                Width = 2, Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-40, -40, 0, 0), IsHitTestVisible = false, Focusable = false
            };
            Panel.SetZIndex(_web, -100);
            host.RootGrid.Children.Add(_web);
            await _web.EnsureCoreWebView2Async(environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.SetVirtualHostNameToFolderMapping(VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += (_, e) => _ = OnMessageAsync(e.TryGetWebMessageAsString());

            _ready = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _web.Source = new Uri($"https://{VirtualHost}/player.html");
            var finished = await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            if (finished != _ready.Task)
            {
                Problem ??= "Spotify's player didn't start (no answer from Spotify). Check the internet connection.";
                Stop();
                return null;
            }
            var id = await _ready.Task;
            if (id == null) Stop();
            return id;
        }
        catch (Exception ex)
        {
            Problem = "The built-in player couldn't start: " + ex.Message;
            Stop();
            return null;
        }
        finally
        {
            _starting = null;
        }
    }

    private static async Task OnMessageAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";
            switch (type)
            {
                case "token":
                    // The player asks for a fresh sign-in token whenever it needs one.
                    var token = _client == null ? "" : await _client.GetAccessTokenAsync(CancellationToken.None);
                    _web?.CoreWebView2?.PostWebMessageAsString(JsonSerializer.Serialize(new { type = "token", token }));
                    break;
                case "ready":
                    DeviceId = root.GetProperty("id").GetString();
                    Problem = null;
                    _ready?.TrySetResult(DeviceId);
                    Changed?.Invoke();
                    break;
                case "notready":
                    DeviceId = null;
                    Changed?.Invoke();
                    break;
                case "state":
                    IsPlaying = root.TryGetProperty("playing", out var p) && p.GetBoolean();
                    string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    var title = Str("title");
                    if (title.Length > 0) Track = new TrackInfo(title, Str("artist"), Str("album"), Str("art") is { Length: > 0 } art ? art : null);
                    Changed?.Invoke();
                    break;
                case "error":
                    string kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                    string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    Problem = kind switch
                    {
                        "account_error" => "Playing inside SquareDeck needs Spotify Premium (Spotify's rule). You can still play on the Spotify app or another device.",
                        "authentication_error" => "Spotify didn't accept the sign-in. Sign in to Spotify again from Settings > Sound.",
                        "initialization_error" => "This PC's web engine can't play Spotify's protected music: " + message,
                        "playback_error" => null, // one song failed; the player carries on
                        _ => "Spotify's player stopped: " + message
                    };
                    if (Problem != null) _ready?.TrySetResult(null);
                    Changed?.Invoke();
                    break;
            }
        }
        catch (Exception) { }
    }

    /// <summary>Play / pause on the built-in player.</summary>
    public static void TogglePlay() => _web?.CoreWebView2?.PostWebMessageAsString("{\"type\":\"toggle\"}");

    /// <summary>Sets the built-in player's own volume (0–100).</summary>
    public static void SetVolume(int percent) =>
        _web?.CoreWebView2?.PostWebMessageAsString(JsonSerializer.Serialize(new { type = "volume", value = Math.Clamp(percent, 0, 100) / 100.0 }));

    /// <summary>Stops the built-in player: SquareDeck stops being a Spotify device and the music stops.</summary>
    public static void Stop()
    {
        try { _web?.CoreWebView2?.PostWebMessageAsString("{\"type\":\"disconnect\"}"); } catch (Exception) { }
        if (_web != null)
        {
            try
            {
                if (_web.Parent is Panel panel) panel.Children.Remove(_web);
                _web.Dispose();
            }
            catch (Exception) { }
        }
        _web = null;
        DeviceId = null;
        IsPlaying = false;
        Track = null;
        Changed?.Invoke();
    }

    public static bool IsRunning => _web != null && DeviceId != null;

    private const string Page = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>SquareDeck player</title></head>
        <body>
        <script>
          const post = m => window.chrome.webview.postMessage(JSON.stringify(m));
          let waiting = [];
          window.chrome.webview.addEventListener('message', e => {
            const m = JSON.parse(e.data);
            if (m.type === 'token') { const w = waiting; waiting = []; w.forEach(cb => cb(m.token)); }
            else if (m.type === 'volume' && window.player) window.player.setVolume(m.value);
            else if (m.type === 'disconnect' && window.player) window.player.disconnect();
            else if (m.type === 'toggle' && window.player) window.player.togglePlay();
          });
          window.onSpotifyWebPlaybackSDKReady = () => {
            const player = new Spotify.Player({
              name: %NAME%,
              getOAuthToken: cb => { waiting.push(cb); post({ type: 'token' }); },
              volume: 0.8
            });
            window.player = player;
            player.addListener('ready', ({ device_id }) => post({ type: 'ready', id: device_id }));
            player.addListener('not_ready', ({ device_id }) => post({ type: 'notready', id: device_id }));
            ['initialization_error', 'authentication_error', 'account_error', 'playback_error'].forEach(kind =>
              player.addListener(kind, ({ message }) => post({ type: 'error', kind, message })));
            // What's playing: to SquareDeck (for its now-playing pill), and to Windows' media controls with the cover.
            player.addListener('player_state_changed', s => {
              const t = s && s.track_window && s.track_window.current_track;
              const msg = { type: 'state', playing: !!(s && !s.paused) };
              if (t) {
                const images = (t.album && t.album.images) || [];
                const big = images.slice().sort((a, b) => (b.width || 0) - (a.width || 0))[0];
                msg.title = t.name || '';
                msg.artist = (t.artists || []).map(a => a.name).join(', ');
                msg.album = (t.album && t.album.name) || '';
                msg.art = big ? big.url : '';
                document.title = msg.title + (msg.artist ? ' – ' + msg.artist : '');
                if ('mediaSession' in navigator) {
                  navigator.mediaSession.metadata = new MediaMetadata({
                    title: msg.title, artist: msg.artist, album: msg.album,
                    artwork: images.map(i => ({ src: i.url, sizes: (i.width || 300) + 'x' + (i.height || 300) }))
                  });
                  navigator.mediaSession.playbackState = msg.playing ? 'playing' : 'paused';
                  navigator.mediaSession.setActionHandler('play', () => player.resume());
                  navigator.mediaSession.setActionHandler('pause', () => player.pause());
                  navigator.mediaSession.setActionHandler('nexttrack', () => player.nextTrack());
                  navigator.mediaSession.setActionHandler('previoustrack', () => player.previousTrack());
                }
              }
              post(msg);
            });
            player.connect().then(ok => { if (!ok) post({ type: 'error', kind: 'connect', message: 'Spotify refused the connection' }); });
          };
        </script>
        <script src="https://sdk.scdn.co/spotify-player.js"></script>
        </body></html>
        """;
}
