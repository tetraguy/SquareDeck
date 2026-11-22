using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>A streaming service the Multimedia page can open, and the sites it's allowed to show.</summary>
/// <param name="Music">A music service: it keeps playing when you leave it (Experimental: music services).</param>
public sealed record StreamingService(string Id, string Name, string Url, string[] Domains, Color Brand, Color OnBrand, bool Music = false)
{
    public static readonly StreamingService[] All =
    {
        new("netflix", "Netflix", "https://www.netflix.com/browse",
            new[] { "netflix.com" }, Color.FromRgb(0xE5, 0x09, 0x14), Colors.White),
        new("youtube", "YouTube", "https://www.youtube.com/",
            // Signing in goes through Google's accounts pages and comes back.
            new[] { "youtube.com", "youtu.be", "accounts.google.com", "consent.google.com", "consent.youtube.com", "accounts.youtube.com" },
            Color.FromRgb(0xFF, 0x00, 0x33), Colors.White),
        new("hulu", "Hulu", "https://www.hulu.com/hub/home",
            new[] { "hulu.com" }, Color.FromRgb(0x1C, 0xE7, 0x83), Color.FromRgb(0x04, 0x12, 0x0A)),
        new("primevideo", "Prime Video", "https://www.primevideo.com/",
            // Signing in goes through Amazon's own site for the country.
            new[] { "primevideo.com", "amazon.com", "amazon.co.uk", "amazon.de", "amazon.ca", "amazon.com.au", "amazon.co.jp",
                    "amazon.in", "amazon.fr", "amazon.it", "amazon.es", "amazon.com.mx", "amazon.com.br" },
            Color.FromRgb(0x00, 0xA8, 0xE1), Colors.White),
        new("crunchyroll", "Crunchyroll", "https://www.crunchyroll.com/",
            new[] { "crunchyroll.com" }, Color.FromRgb(0xF4, 0x75, 0x21), Colors.White),
        new("disneyplus", "Disney+", "https://www.disneyplus.com/home",
            // Signing in can go through MyDisney's pages.
            new[] { "disneyplus.com", "disney.com", "go.com" }, Color.FromRgb(0x11, 0x3C, 0xCF), Colors.White),
        new("paramountplus", "Paramount+", "https://www.paramountplus.com/home/",
            new[] { "paramountplus.com" }, Color.FromRgb(0x00, 0x64, 0xFF), Colors.White),
        new("tubi", "Tubi", "https://tubitv.com/home",
            // Free; signing in with Google goes through Google's accounts pages.
            new[] { "tubitv.com", "tubi.tv", "accounts.google.com" }, Color.FromRgb(0xFF, 0xFF, 0x13), Color.FromRgb(0x10, 0x10, 0x10)),
        // Music (Experimental: YouTube Music and Apple Music): they keep playing in the background.
        new("ytmusic", "YouTube Music", "https://music.youtube.com/",
            new[] { "music.youtube.com", "youtube.com", "accounts.google.com", "consent.google.com", "consent.youtube.com", "accounts.youtube.com" },
            Color.FromRgb(0xFF, 0x00, 0x00), Colors.White, Music: true),
        new("applemusic", "Apple Music", "https://music.apple.com/",
            // Signing in goes through Apple's ID pages.
            new[] { "music.apple.com", "apple.com", "idmsa.apple.com", "appleid.apple.com", "mzstatic.com", "itunes.apple.com" },
            Color.FromRgb(0xFA, 0x23, 0x3B), Colors.White, Music: true),
    };

    /// <summary>The services to show: the music ones only with their experiment on.</summary>
    public static IEnumerable<StreamingService> Shown => All.Where(s => !s.Music || Labs.On("musicstreams"));

    /// <summary>The service's logo, if one has been added (Streaming\&lt;id&gt;.png next to SquareDeck.exe).</summary>
    public string? LogoPath
    {
        get
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Streaming", Id + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }

    /// <summary>True when <paramref name="url"/> is on one of this service's sites (or a page of the browser itself).</summary>
    public bool Allows(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        string host = uri.Host.ToLowerInvariant();
        return Domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>The logo, or the name on the brand color: the face used on tiles and in the menu.</summary>
    public FrameworkElement Face(double width, double height, double fontSize)
    {
        var box = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 2)),
            Background = new SolidColorBrush(Brand), ClipToBounds = true
        };
        if (LogoPath is string logo && ImageLoader.FromFile(logo, (int)Math.Max(width * 2, 256)) is ImageSource image)
        {
            box.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12));
            var img = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(height * 0.16) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            box.Child = img;
        }
        else
        {
            box.Child = new TextBlock
            {
                Text = Name, FontSize = fontSize, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(OnBrand),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 10, 0)
            };
        }
        return box;
    }
}

/// <summary>
/// One streaming service, full screen inside the launcher, made for a controller. Only that service's own sites can
/// be shown: links to anywhere else are blocked rather than followed.
///
/// Controls: d-pad moves between links and buttons, A chooses (or clicks where the pointer is), X plays / pauses,
/// LB / RB skip back / forward 10 seconds, LT / RT scroll, Start goes full screen, the right stick moves a pointer
/// (press it to click), Y opens the menu, and B goes back a page, then out of full screen, then back to the launcher
/// with the service kept open (paused) to come back to.
/// </summary>
public sealed class StreamingScreen : Screen, IPagePad
{
    private static StreamingScreen? _current;
    private static Task<CoreWebView2Environment>? _environment;

    private const string TvUserAgent =
        "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/538.1 (KHTML, like Gecko) Version/6.0 TV Safari/538.1";

    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden };
    private readonly Grid _top = new() { Height = 58 };
    private readonly Grid _footer = new() { Height = 44 };
    private readonly TextBlock _title = Ui.Text("", 16, "SubtleBrush");
    private readonly TextBlock _status = Ui.Text("", 20, "SubtleBrush");
    private readonly Border _blocked;
    private readonly TextBlock _blockedText = Ui.Text("", 15, "FgBrush");
    private readonly DispatcherTimer _blockedTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Border _loading = new() { Height = 3, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Grid _introHost = new() { IsHitTestVisible = false };
    private DateTime _openedAt;
    private bool _ready, _disposed, _started, _tvLayout, _revealed;
    private DateTime _pointerMovedAt = DateTime.MinValue;
    private DateTime _padAliveAt = DateTime.MinValue;

    /// <summary>The page is reading the controller itself (see <see cref="StreamingPad"/>), so SquareDeck stays out of the way.</summary>
    public bool PadInPage => _ready && _revealed && !_disposed && DateTime.UtcNow - _padAliveAt < TimeSpan.FromSeconds(2.5);

    /// <summary>What's on: the show or film's name (or the page's), and whether it's playing.</summary>
    public string NowTitle { get; private set; } = "";
    public bool NowPlaying { get; private set; }

    /// <summary>Raised when a service opens, closes, goes to the background, or what's playing changes.</summary>
    public static event Action? StateChanged;
    private static void Changed() => StateChanged?.Invoke();
    private bool _stickWasPressed;
    private double _moveX, _moveY;

    public StreamingService Service { get; }

    /// <summary>The service that's open (on screen or kept in the background), if any.</summary>
    public static StreamingScreen? Current => _current is { _disposed: false } c ? c : null;

    /// <summary>
    /// Opens <paramref name="service"/>: back to it when it's already open, or after closing the one that is (with a
    /// question first, since whatever it was playing stops).
    /// </summary>
    public static async Task OpenAsync(MainWindow host, StreamingService service)
    {
        if (Current is { } open)
        {
            if (open.Service.Id == service.Id)
            {
                if (!host.IsTop(open)) host.Push(open);
                return;
            }
            int choice = await host.AskWithIconsAsync($"{open.Service.Name} is open",
                $"Close {open.Service.Name} and open {service.Name}?",
                ($"Open {service.Name}", ""), ($"Back to {open.Service.Name}", ""), ("Cancel", ""));
            if (choice == 1) { host.Push(open); return; }
            if (choice != 0) return;
            open.CloseService();
        }
        host.Push(new StreamingScreen(service));
    }

    /// <summary>Closes the service kept in the background (when a game starts, or from the Multimedia page).</summary>
    public static void CloseKept()
    {
        if (Current is { } c && !c.Host.IsTop(c)) c.DisposeAll();
        else if (Current is { } top) top.CloseService();
    }

    private StreamingScreen(StreamingService service)
    {
        Service = service;

        // Top bar: the service, the page it's on, and a lock to say it stays on this service.
        var face = service.Face(118, 38, 15);
        face.Margin = new Thickness(18, 0, 14, 0);
        face.VerticalAlignment = VerticalAlignment.Center;
        var lockRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var lockGlyph = Ui.Glyph("", 13, "SubtleBrush");
        lockGlyph.Margin = new Thickness(0, 0, 8, 0);
        lockGlyph.VerticalAlignment = VerticalAlignment.Center;
        lockRow.Children.Add(lockGlyph);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        lockRow.Children.Add(_title);
        var bar = new DockPanel();
        DockPanel.SetDock(face, Dock.Left);
        bar.Children.Add(face);
        bar.Children.Add(lockRow);
        _top.Children.Add(bar);
        _loading.Background = new SolidColorBrush(service.Brand);
        _top.Children.Add(_loading);
        _top.SetResourceReference(Panel.BackgroundProperty, "BgBrush");

        _footer.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        // The same button pictures as every other page's hint bar.
        var hints = new HintBar("{Select}  Choose      {Details}  Play / pause      LB RB  -10 / +10 s      LT RT  Scroll      Start  Full screen      {Options}  Menu      {Back}  Back");
        hints.VerticalAlignment = VerticalAlignment.Center;
        hints.HorizontalAlignment = HorizontalAlignment.Center;
        _footer.Children.Add(hints);

        // "That link goes elsewhere": a note in the top bar for a few seconds (nothing can be drawn over the page).
        var blockedRow = new StackPanel { Orientation = Orientation.Horizontal };
        var blockedGlyph = Ui.Glyph("", 15, "AccentBrush");
        blockedGlyph.Margin = new Thickness(0, 0, 10, 0);
        blockedRow.Children.Add(blockedGlyph);
        blockedRow.Children.Add(_blockedText);
        _blocked = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 7, 18, 7), Child = blockedRow, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0)
        };
        _blocked.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        _top.Children.Add(_blocked);
        _blockedTimer.Tick += (_, _) => { _blockedTimer.Stop(); _blocked.Visibility = Visibility.Collapsed; };

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_top);
        var page = new Grid();
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxWidth = 760;
        _status.TextAlignment = TextAlignment.Center;
        _status.Text = "Opening " + service.Name + "…";
        page.Children.Add(_status);
        page.Children.Add(_web);
        Grid.SetRow(page, 1);
        root.Children.Add(page);
        Grid.SetRow(_footer, 2);
        root.Children.Add(_footer);
        // The opening animation, over everything until the page is ready.
        Grid.SetRowSpan(_introHost, 3);
        root.Children.Add(_introHost);
        Content = root;
    }

    public override void OnShown()
    {
        _current = this;
        Changed();
        if (_started)
        {
            _web.Visibility = _ready && _revealed && _introHost.Children.Count == 0 ? Visibility.Visible : Visibility.Hidden;
            _ = Run("document.querySelectorAll('video').forEach(function(v){ if(v.dataset.sdPaused==='1'){ v.dataset.sdPaused=''; v.play(); } });");
            Dispatcher.BeginInvoke(() => _web.Focus(), DispatcherPriority.Input);
            return;
        }
        _started = true;
        _openedAt = DateTime.UtcNow;
        _introHost.Children.Add(StreamingIntro.Build(Host.Settings.StreamingIntro, Service, Service.Face(320, 150, 34)));
        _ = StartAsync();
    }

    /// <summary>Shows the page once it has loaded and the opening animation has played.</summary>
    private async void Reveal()
    {
        if (_revealed || _disposed) return;
        _revealed = true;
        var left = TimeSpan.FromMilliseconds(StreamingIntro.Length) - (DateTime.UtcNow - _openedAt);
        if (left > TimeSpan.Zero) await Task.Delay(left);
        if (_disposed) return;
        // The page is a window of its own and can't fade, so the animation fades out over it first.
        _introHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
        await Task.Delay(260);
        _introHost.Children.Clear();
        _introHost.Visibility = Visibility.Collapsed;
        // Full screen, like the cloud services: no bars, just the service.
        _top.Visibility = _footer.Visibility = Visibility.Collapsed;
        if (_disposed || !Host.IsTop(this)) return;
        _web.Visibility = Visibility.Visible;
        _web.Focus();
        await Task.Delay(1200);
        _ = Run("window.__sdpad && window.__sdpad.toast('B  Back   ·   X  Play / pause   ·   Y  Menu')");
    }

    // The page is a window of its own and would cover any pop-up drawn over it, so it steps aside for them.
    public override void OnCovered() => _web.Visibility = Visibility.Hidden;

    public override void OnResumed()
    {
        if (_ready && _introHost.Children.Count == 0) _web.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => _web.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Back to the launcher: the service stays open, paused, to come back to from the Multimedia page.</summary>
    public override void OnClosed()
    {
        if (_disposed) return;
        _web.Visibility = Visibility.Hidden;
        if (Service.Music)
        {
            // Music keeps playing while you play or browse; Windows' media controls and the home screen's pill show it.
            Changed();
            Host.ShowToast("MUSIC", Service.Name + " keeps playing", "Go back to it from Multimedia > Streaming to choose something else.", "", "info");
            return;
        }
        NowPlaying = false;
        Changed();
        _ = Run("document.querySelectorAll('video').forEach(function(v){ if(!v.paused){ v.dataset.sdPaused='1'; v.pause(); } });");
        Host.ShowToast("STREAMING", Service.Name + " is still open", "Paused. Go back to it from Multimedia > Streaming.", "", "info");
    }

    private async Task StartAsync()
    {
        try
        {
            // Sign-ins are kept in the launcher's own folder, apart from Edge and from the built-in browser.
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "Streaming"));
            await _web.EnsureCoreWebView2Async(await _environment);
            if (_disposed) return;
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            _tvLayout = Service.Id == "youtube" && Host.Settings.StreamingYouTubeTv;
            if (_tvLayout) core.Settings.UserAgent = TvUserAgent;

            // Only this service's own sites: anything else is stopped before it loads.
            _web.NavigationStarting += (_, e) =>
            {
                if (Service.Allows(e.Uri)) { ShowLoading(true); return; }
                e.Cancel = true;
                Blocked(e.Uri);
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true; // never a second window
                if (Service.Allows(e.Uri)) _web.CoreWebView2.Navigate(e.Uri);
                else Blocked(e.Uri);
            };
            core.DocumentTitleChanged += (_, _) => _title.Text = core.DocumentTitle;
            // Full-screen video: the bars step aside.
            core.ContainsFullScreenElementChanged += (_, _) =>
                _top.Visibility = _footer.Visibility = core.ContainsFullScreenElement || _revealed ? Visibility.Collapsed : Visibility.Visible;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScreen.HelperScript);
            // The controller, read by the page itself.
            string accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color is { } a ? $"#{a.R:X2}{a.G:X2}{a.B:X2}" : "#3AA0FF";
            await core.AddScriptToExecuteOnDocumentCreatedAsync(StreamingPad.For(accent));
            core.WebMessageReceived += (_, e) =>
            {
                string message;
                try { message = e.TryGetWebMessageAsString(); } catch (Exception) { return; }
                OnPageMessage(message);
            };
            _web.NavigationCompleted += (_, e) =>
            {
                ShowLoading(false);
                if (!_ready)
                {
                    _ready = true;
                    _status.Visibility = Visibility.Collapsed;
                    Reveal();
                }
            };
            _web.Source = new Uri(_tvLayout ? "https://www.youtube.com/tv" : Service.Url);
        }
        catch (Exception ex)
        {
            _environment = null;
            _status.Text = Service.Name + " couldn't open. It needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 " +
                           "and current Windows 10 (microsoft.com/edge/webview2).\n\n" + ex.Message;
        }
    }

    private void OnPageMessage(string message)
    {
        if (message == "sd:alive") { _padAliveAt = DateTime.UtcNow; return; }
        if (!Host.IsTop(this) && !message.StartsWith("sd:media|")) return;
        switch (message)
        {
            case "sd:leave": UiSound.Back(); Host.Pop(this); return; // kept open, paused
            case "sd:menu": _ = MenuAsync(); return;
            case "sd:quick": Host.Push(new QuickSettingsScreen()); return;
        }
        if (message.StartsWith("sd:media|"))
        {
            try
            {
                using var doc = JsonDocument.Parse(message["sd:media|".Length..]);
                var r = doc.RootElement;
                string title = r.GetProperty("title").GetString() ?? "", page = r.GetProperty("page").GetString() ?? "";
                if (title.Length == 0) title = CleanTitle(page);
                bool playing = r.GetProperty("playing").GetBoolean();
                if (title != NowTitle || playing != NowPlaying) { NowTitle = title; NowPlaying = playing; Changed(); }
            }
            catch (Exception) { }
        }
    }

    /// <summary>"Stranger Things | Netflix" → "Stranger Things"; the bare service name when it's just the home page.</summary>
    private string CleanTitle(string page)
    {
        var t = page;
        foreach (var sep in new[] { " | ", " - ", " – ", " • " })
        {
            int i = t.LastIndexOf(sep, StringComparison.Ordinal);
            if (i > 0 && t[(i + sep.Length)..].Contains(Service.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase)) t = t[..i];
        }
        t = t.Trim();
        return t.Equals(Service.Name, StringComparison.OrdinalIgnoreCase) || t.Length == 0 ? "" : t;
    }

    private void ShowLoading(bool on)
    {
        if (!on)
        {
            _loading.BeginAnimation(WidthProperty, null);
            _loading.Width = 0;
            return;
        }
        if (Theme.ReduceMotion) { _loading.Width = 240; return; }
        _loading.BeginAnimation(WidthProperty, new DoubleAnimation(0, 900, TimeSpan.FromSeconds(4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Blocked(string url)
    {
        string where = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "another site";
        _blockedText.Text = $"Stays on {Service.Name}: {where} isn't opened here";
        // Full screen has no bar to show it in, so the page shows it.
        if (_revealed) _ = Run("window.__sdpad && window.__sdpad.toast(" + JsonSerializer.Serialize(_blockedText.Text) + ")");
        _blocked.Visibility = Visibility.Visible;
        _blockedTimer.Stop();
        _blockedTimer.Start();
        UiSound.Error();
    }

    private async Task<string> Run(string script)
    {
        if (!_ready || _disposed) return "";
        try
        {
            var json = await _web.ExecuteScriptAsync(script);
            return json.StartsWith('"') ? JsonSerializer.Deserialize<string>(json) ?? "" : json;
        }
        catch (Exception) { return ""; }
    }

    private Task<string> Sd(string call) =>
        Run("(function(){" + BrowserScreen.HelperScript + "; return window.__sd ? window.__sd." + call + " : '';})()");

    // The video that matters: the one playing, else the biggest on the page.
    private const string FindVideo =
        "var vs=[].slice.call(document.querySelectorAll('video,audio')); var v=vs.filter(function(x){return !x.paused;})[0]||" +
        "vs.sort(function(a,b){return b.clientWidth*b.clientHeight-a.clientWidth*a.clientHeight;})[0];";

    private Task<string> Video(string body) => Run("(function(){" + FindVideo + " if(!v) return 'none'; " + body + " return 'ok';})()");

    private async Task PlayPauseAsync()
    {
        var r = await Video("if(v.paused) v.play(); else v.pause();");
        if (r == "none") await SendKeyAsync(" ", "Space", 32); // players built without a plain <video> tag
    }

    private Task SeekAsync(int seconds) =>
        Video($"v.currentTime=Math.max(0,Math.min((v.duration||1e9)-1,v.currentTime+({seconds})));");

    private async Task FullScreenAsync()
    {
        if (_web.CoreWebView2?.ContainsFullScreenElement == true) { await Run("document.exitFullscreen&&document.exitFullscreen();"); return; }
        // The site's own full-screen button knows its player best; the video itself otherwise.
        var r = await Run("(function(){var b=document.querySelector('.ytp-fullscreen-button,[data-uia=\"control-fullscreen-enter\"],[aria-label*=\"ull screen\" i],[aria-label*=\"ullscreen\" i]');" +
                          "if(b){b.click();return 'ok';} " + FindVideo + " if(v&&v.requestFullscreen){v.requestFullscreen();return 'ok';} return 'none';})()");
        if (r == "none") await SendKeyAsync("f", "KeyF", 70);
    }

    private Task SendKeyAsync(string key, string code, int keyCode) =>
        Run($"(function(){{var t=document.activeElement||document.body;['keydown','keyup'].forEach(function(n){{t.dispatchEvent(new KeyboardEvent(n,{{key:{JsonSerializer.Serialize(key)},code:'{code}',keyCode:{keyCode},which:{keyCode},bubbles:true}}));}});}})()");

    private async Task ClickAsync()
    {
        var result = await Sd("click()");
        if (!result.StartsWith("text:", StringComparison.Ordinal)) return;
        if (TouchKeyboard.UseWindows)
        {
            _web.Focus();
            await TouchKeyboard.ShowAsync();
            return;
        }
        var typed = await Host.PromptAsync("Type here", null, result[5..], "Enter");
        if (typed != null) await Sd("type(" + JsonSerializer.Serialize(typed) + ")");
    }

    private async Task MenuAsync()
    {
        var options = new List<(string Label, string Glyph, Action Run)>
        {
            (Service.Name + " home", "", () => _web.CoreWebView2?.Navigate(_tvLayout ? "https://www.youtube.com/tv" : Service.Url)),
            ("Reload", "", () => _web.Reload()),
            ("Full screen", "", () => _ = FullScreenAsync()),
        };
        if (Service.Id == "youtube")
            options.Add((_tvLayout ? "Use the normal YouTube layout" : "Use YouTube's TV layout (made for controllers)", "", ToggleTvLayout));
        options.Add(("Another service…", "", () => _ = PickAnotherAsync()));
        options.Add(("Back to the launcher (keep it open)", "", () => Host.Pop(this)));
        options.Add(("Close " + Service.Name, "", CloseService));

        int choice = await Host.AskWithIconsAsync(Service.Name, _title.Text.Length > 0 ? _title.Text : null,
            options.Select(o => (o.Label, o.Glyph)).ToArray());
        if (choice >= 0) options[choice].Run();
    }

    private void ToggleTvLayout()
    {
        Host.Settings.StreamingYouTubeTv = !Host.Settings.StreamingYouTubeTv;
        Host.Settings.Save();
        _tvLayout = Host.Settings.StreamingYouTubeTv;
        if (_web.CoreWebView2 == null) return;
        _web.CoreWebView2.Settings.UserAgent = _tvLayout ? TvUserAgent : "";
        _web.CoreWebView2.Navigate(_tvLayout ? "https://www.youtube.com/tv" : Service.Url);
    }

    private async Task PickAnotherAsync()
    {
        var others = StreamingService.Shown.Where(s => s.Id != Service.Id).ToArray();
        int choice = await Host.AskWithIconsAsync("Open another service", $"{Service.Name} closes.",
            others.Select(s => (s.Name, "")).ToArray());
        if (choice < 0) return;
        CloseService();
        Host.Push(new StreamingScreen(others[choice]));
    }

    /// <summary>Closes the service: the page and whatever it was playing go away.</summary>
    public void CloseService()
    {
        DisposeAll();
        if (Host.IsTop(this)) Host.Pop(this);
    }

    private void DisposeAll()
    {
        if (_disposed) return;
        _disposed = true;
        _blockedTimer.Stop();
        try { _web.Dispose(); } catch (Exception) { }
        if (ReferenceEquals(_current, this)) _current = null;
        Changed();
    }

    private bool PointerActive => DateTime.UtcNow - _pointerMovedAt < TimeSpan.FromSeconds(4);

    public override bool OnAction(NavAction action)
    {
        if (!_ready) return action != NavAction.Back;
        if (action is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right or NavAction.Accept &&
            !_web.IsKeyboardFocusWithin) _web.Focus();
        switch (action)
        {
            // YouTube's TV layout is made for arrow keys: they go straight to it.
            case NavAction.Up when _tvLayout: _ = SendKeyAsync("ArrowUp", "ArrowUp", 38); return true;
            case NavAction.Down when _tvLayout: _ = SendKeyAsync("ArrowDown", "ArrowDown", 40); return true;
            case NavAction.Left when _tvLayout: _ = SendKeyAsync("ArrowLeft", "ArrowLeft", 37); return true;
            case NavAction.Right when _tvLayout: _ = SendKeyAsync("ArrowRight", "ArrowRight", 39); return true;
            case NavAction.Accept when _tvLayout && !PointerActive: _ = SendKeyAsync("Enter", "Enter", 13); return true;

            case NavAction.Up: _ = Sd("move('up')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Down: _ = Sd("move('down')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Left: _ = Sd("move('left')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Right: _ = Sd("move('right')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Accept:
                if (PointerActive) MouseClick(); else _ = ClickAsync();
                return true;
            case NavAction.X: _ = PlayPauseAsync(); return true;
            case NavAction.LB: _ = SeekAsync(-10); return true;
            case NavAction.RB: _ = SeekAsync(10); return true;
            case NavAction.LT: _ = Sd("scroll(-innerHeight*0.8)"); return true;
            case NavAction.RT: _ = Sd("scroll(innerHeight*0.8)"); return true;
            case NavAction.Start: _ = FullScreenAsync(); return true;
            case NavAction.Y: _ = MenuAsync(); return true;
            case NavAction.Back:
                if (_web.CoreWebView2?.ContainsFullScreenElement == true) { _ = Run("document.exitFullscreen&&document.exitFullscreen();"); return true; }
                if (_tvLayout) { _ = SendKeyAsync("Escape", "Escape", 27); return true; } // the TV layout has its own Back
                if (_web.CanGoBack) { _web.GoBack(); return true; }
                return false; // to the launcher, kept open
        }
        return false;
    }

    public override bool OnRightStick(double x, double y, bool pressed)
    {
        if (pressed && !_stickWasPressed) MouseClick();
        _stickWasPressed = pressed;
        if (x == 0 && y == 0) { _moveX = _moveY = 0; return true; }
        _moveX += x * Math.Abs(x) * 22;
        _moveY += -y * Math.Abs(y) * 22;
        int dx = (int)_moveX, dy = (int)_moveY;
        _moveX -= dx;
        _moveY -= dy;
        if ((dx != 0 || dy != 0) && GetCursorPos(out var p))
        {
            SetCursorPos(p.X + dx, p.Y + dy);
            _pointerMovedAt = DateTime.UtcNow;
        }
        return true;
    }

    private void MouseClick()
    {
        if (!GetCursorPos(out _)) return;
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        _pointerMovedAt = DateTime.UtcNow;
        UiSound.Select();
    }

    public override void FocusDefault() => _web.Focus();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
}

/// <summary>
/// The Multimedia page's Streaming tab: the services as big tiles, and the one that's open (if any) with Resume
/// and Close.
/// </summary>
public sealed class StreamingView : StackPanel
{
    private readonly Screen _page;
    public UIElement? FirstFocus { get; private set; }

    public StreamingView(Screen page)
    {
        _page = page;
        Build();
    }

    /// <summary>
    /// Moves the highlight to the nearest button in that direction (the d-pad and left stick), scrolling it into view.
    /// Done here rather than by WPF's own focus movement, which didn't move between the tiles reliably.
    /// </summary>
    public bool Move(NavAction action)
    {
        if (action is not (NavAction.Left or NavAction.Right or NavAction.Up or NavAction.Down)) return false;
        var buttons = new List<Button>();
        void Collect(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is UIElement { Visibility: not Visibility.Visible }) continue;
                if (child is Button { Focusable: true, IsEnabled: true } b) buttons.Add(b);
                else if (child is DependencyObject d) Collect(d);
            }
        }
        Collect(this);
        if (buttons.Count == 0) return false;
        if (Keyboard.FocusedElement is not Button current || !buttons.Contains(current))
        {
            if (FirstFocus is UIElement first) { first.Focus(); (first as FrameworkElement)?.BringIntoView(); }
            return true;
        }
        Point Center(Button b) => b.TranslatePoint(new Point(b.ActualWidth / 2, b.ActualHeight / 2), this);
        var from = Center(current);
        Button? best = null;
        double bestScore = double.MaxValue;
        foreach (var b in buttons)
        {
            if (b == current) continue;
            var to = Center(b);
            double dx = to.X - from.X, dy = to.Y - from.Y;
            (double along, double across) = action switch
            {
                NavAction.Right => (dx, Math.Abs(dy)),
                NavAction.Left => (-dx, Math.Abs(dy)),
                NavAction.Down => (dy, Math.Abs(dx)),
                _ => (-dy, Math.Abs(dx)),
            };
            if (along < 8) continue; // not in that direction
            double score = along + across * 2.5;
            if (score < bestScore) { bestScore = score; best = b; }
        }
        if (best != null)
        {
            best.Focus();
            best.BringIntoView();
        }
        return true; // at an edge: stay put
    }

    public void Build()
    {
        Children.Clear();
        FirstFocus = null;
        var host = _page.Host;

        if (StreamingScreen.Current is { } open)
        {
            // The service that's open: a banner in its color with Resume and Close.
            var banner = new Border { CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 4)), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 24) };
            banner.Background = new LinearGradientBrush(Color.FromArgb(0xD0, open.Service.Brand.R, open.Service.Brand.G, open.Service.Brand.B),
                                                        Color.FromArgb(0x30, open.Service.Brand.R, open.Service.Brand.G, open.Service.Brand.B), 0);
            var row = new DockPanel();
            var face = open.Service.Face(150, 64, 18);
            face.Margin = new Thickness(0, 0, 20, 0);
            DockPanel.SetDock(face, Dock.Left);
            row.Children.Add(face);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(buttons, Dock.Right);
            var resume = Ui.PanelButton("", "Resume");
            resume.Click += (_, _) => _ = StreamingScreen.OpenAsync(host, open.Service);
            var close = Ui.PanelButton("", "Close");
            close.Margin = new Thickness(10, 0, 0, 0);
            close.Click += (_, _) => { open.CloseService(); Build(); _page.Host.FocusSoon(FirstFocus); };
            buttons.Children.Add(resume);
            buttons.Children.Add(close);
            row.Children.Add(buttons);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(open.Service.Name + " is open", 22, bold: true);
            name.Foreground = Brushes.White;
            var sub = Ui.Text("Paused in the background. Resume where you left off, or close it to free its memory.", 15);
            sub.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
            sub.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(name);
            text.Children.Add(sub);
            row.Children.Add(text);
            banner.Child = row;
            Children.Add(banner);
            FirstFocus = resume;
        }

        var heading = Ui.Text("Watch", 18, "FgBrush", bold: true);
        heading.Margin = new Thickness(4, 0, 0, 12);
        Children.Add(heading);
        var tiles = new WrapPanel();
        foreach (var service in StreamingService.Shown.Where(x => !x.Music))
        {
            var s = service;
            var stack = new StackPanel();
            var face = s.Face(214, 120, 24);
            stack.Children.Add(face);
            var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 0) };
            label.Children.Add(Ui.Text(s.Name, 16, "FgBrush", bold: true));
            if (StreamingScreen.Current?.Service.Id == s.Id)
            {
                var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                pill.Child = Ui.Text("OPEN", 11, "BgBrush", bold: true);
                label.Children.Add(pill);
            }
            stack.Children.Add(label);
            var tile = new Button { Style = Ui.StyleOf("TileButton"), Content = stack, Margin = new Thickness(0, 0, 18, 18), Tag = s };
            tile.Click += (_, _) => _ = StreamingScreen.OpenAsync(host, s);
            tiles.Children.Add(tile);
            FirstFocus ??= tile;
        }
        Children.Add(tiles);
        var music = StreamingService.Shown.Where(x => x.Music).ToList();
        if (music.Count > 0)
        {
            var listen = Ui.Text("Listen", 18, "FgBrush", bold: true);
            listen.Margin = new Thickness(4, 8, 0, 12);
            Children.Add(listen);
            var musicTiles = new WrapPanel();
            foreach (var service in music)
            {
                var s = service;
                var stack = new StackPanel();
                stack.Children.Add(s.Face(214, 120, 24));
                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 0) };
                label.Children.Add(Ui.Text(s.Name, 16, "FgBrush", bold: true));
                if (StreamingScreen.Current?.Service.Id == s.Id)
                {
                    var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    pill.Child = Ui.Text(StreamingScreen.Current.NowPlaying ? "PLAYING" : "OPEN", 11, "BgBrush", bold: true);
                    label.Children.Add(pill);
                }
                stack.Children.Add(label);
                var tile = new Button { Style = Ui.StyleOf("TileButton"), Content = stack, Margin = new Thickness(0, 0, 18, 18), Tag = s };
                tile.Click += (_, _) => _ = StreamingScreen.OpenAsync(host, s);
                musicTiles.Children.Add(tile);
                FirstFocus ??= tile;
            }
            Children.Add(musicTiles);
        }
        var note = Ui.Text("Each one opens full screen here and stays on its own site. Sign in once and you stay signed in.", 14, "SubtleBrush");
        note.Margin = new Thickness(4, 6, 0, 0);
        note.TextWrapping = TextWrapping.Wrap;
        Children.Add(note);
    }
}
