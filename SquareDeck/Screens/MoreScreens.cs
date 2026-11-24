using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Everything Windows knows about the battery: level, time left, health, and the controller's battery.</summary>
public sealed class BatteryScreen : ListScreen
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Dictionary<string, string>? _details;
    private string? _plan;

    public BatteryScreen() : base("Battery")
    {
        _timer.Tick += (_, _) => { if (Host.IsTop(this)) Rebuild(); };
    }

    public override void OnShown()
    {
        Rebuild();
        _timer.Start();
        _ = LoadAsync();
    }

    public override void OnClosed() => _timer.Stop();

    private async Task LoadAsync()
    {
        try
        {
            // Capacity, wear and charge rate come from the battery's own report, which only PowerShell can read here.
            const string script =
                "$ErrorActionPreference='SilentlyContinue';" +
                "$s=Get-CimInstance -Namespace root\\wmi -ClassName BatteryStaticData|Select-Object -First 1;" +
                "$f=Get-CimInstance -Namespace root\\wmi -ClassName BatteryFullChargedCapacity|Select-Object -First 1;" +
                "$c=Get-CimInstance -Namespace root\\wmi -ClassName BatteryCycleCount|Select-Object -First 1;" +
                "$t=Get-CimInstance -Namespace root\\wmi -ClassName BatteryStatus|Select-Object -First 1;" +
                "$b=Get-CimInstance Win32_Battery|Select-Object -First 1;" +
                "\"design=$($s.DesignedCapacity)\";\"full=$($f.FullChargedCapacity)\";\"cycles=$($c.CycleCount)\";" +
                "\"charge=$($t.ChargeRate)\";\"discharge=$($t.DischargeRate)\";\"remaining=$($t.RemainingCapacity)\";" +
                "\"voltage=$($t.Voltage)\";\"name=$($b.Name)\"";
            var output = await ProcessUtil.RunPowerShellAsync(script);
            var details = new Dictionary<string, string>();
            foreach (var line in output.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[(eq + 1)..].Trim() is { Length: > 0 } value) details[line[..eq].Trim()] = value;
            }
            _details = details;
        }
        catch (Exception)
        {
            _details = new();
        }
        try { _plan = (await PowerSettings.GetPlansAsync()).FirstOrDefault(p => p.Active).Name; } catch (Exception) { }
        if (Host.IsTop(this)) Rebuild();
    }

    private double Number(string key) =>
        _details != null && _details.TryGetValue(key, out var text) && double.TryParse(text, out var n) ? n : 0;

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{Math.Max(1, t.Minutes)} min";

    protected override void Build()
    {
        var percent = Battery.GetPercent();
        if (percent == null)
        {
            AddNote("This PC has no battery (or Windows can't see one).");
        }
        else
        {
            bool plugged = Battery.IsPluggedIn();
            double charge = Number("charge"), discharge = Number("discharge");
            double remaining = Number("remaining"), full = Number("full"), design = Number("design");

            AddHeader("Now");
            AddRow("Charge", percent + "%", Battery.Glyph(percent.Value, plugged));
            AddRow("Status", !plugged ? "On battery" : percent >= 100 ? "Fully charged" : charge > 0 ? "Charging" : "Plugged in", "");

            string timeLeft;
            if (plugged)
            {
                // Windows gives no "time until full"; work it out from the charge rate when the battery reports one.
                timeLeft = charge > 0 && full > remaining
                    ? Duration(TimeSpan.FromHours((full - remaining) / charge)) + " until full"
                    : percent >= 100 ? "Full" : "Charging";
            }
            else if (Battery.GetTimeRemaining() is TimeSpan left) timeLeft = Duration(left) + " left";
            else if (discharge > 0 && remaining > 0) timeLeft = Duration(TimeSpan.FromHours(remaining / discharge)) + " left";
            else timeLeft = "Working it out…";
            AddRow("Time", timeLeft, "");

            if (charge > 0) AddRow("Charging at", $"{charge / 1000:0.0} W", "");
            else if (discharge > 0) AddRow("Power use", $"{discharge / 1000:0.0} W", "");
            AddRow("Battery saver", Battery.SaverOn() ? "On" : "Off", "");
            if (_plan != null) AddRow("Power plan", _plan, "");

            AddHeader("Battery health");
            if (_details == null) AddNote("Reading the battery…");
            else if (full <= 0 || design <= 0) AddNote("This battery doesn't report its capacity to Windows.");
            else
            {
                AddRow("Health", $"{Math.Min(100, full / design * 100):0}% of its original capacity", "");
                AddRow("Full charge holds", $"{full / 1000:0.0} Wh  (was {design / 1000:0.0} Wh when new)", "");
                if (Number("cycles") > 0) AddRow("Charge cycles", Number("cycles").ToString("0"), "");
                if (Number("voltage") > 0) AddRow("Voltage", $"{Number("voltage") / 1000:0.00} V", "");
            }
            AddRow("Battery health over time", null, "\uEBA7", click: () => Host.Push(new BatteryHealthScreen()),
                subtitle: "Charge cycles, capacity compared with new, and how it has changed");
        }

        if (Host.ControllerBattery is string pad)
        {
            AddHeader("Controller");
            AddRow("Controller battery", pad, "");
        }

        AddHeader("More");
        AddRow("Sleep, screen-off and power plan", glyph: "", click: () => Host.Push(new PcSettingsScreen()));
        AddRow("Windows battery settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:batterysaver"));
        if (percent != null) AddRow("Create a full battery report", glyph: "", click: Report);
    }

    private async void Report()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "battery-report.html");
        try
        {
            await Host.RunBusyAsync("Creating the report", () => ProcessUtil.RunAsync("powercfg.exe", "/batteryreport /output \"" + file + "\""));
        }
        catch (Exception) { }
        if (File.Exists(file)) ProcessUtil.Open(file);
        else await Host.AskAsync("Couldn't create the report", "Windows didn't produce a battery report on this PC.", "OK");
    }
}

/// <summary>
/// The built-in web browser (Microsoft Edge WebView2), made for a controller: tabs, a rounded address bar, and the
/// right stick as a mouse pointer. The d-pad jumps between links, A clicks (at the pointer if you've just moved
/// it), pressing the right stick clicks at the pointer, LB / RB switch tabs, B goes back, X types an address,
/// Y opens the menu, Menu opens a new tab. With "Keep the browser open" (Settings) it keeps running when you
/// leave it, tabs and all, and comes back exactly where you were.
/// </summary>
public sealed class BrowserScreen : Screen
{
    public static readonly (string Id, string Name, string Url)[] Engines =
    {
        ("google", "Google", "https://www.google.com/search?q="),
        ("bing", "Bing", "https://www.bing.com/search?q="),
        ("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q=")
    };

    private sealed class Tab
    {
        public WebView2 Web = null!;
        public Button Chip = null!;
        public TextBlock Title = null!;
        public Image Icon = null!;
        public bool Ready, Loading;
        public string? PendingUrl;
    }

    // The one browser kept running between visits (when Settings allows), and the shared Edge profile.
    private static BrowserScreen? _kept;
    private static Task<CoreWebView2Environment>? _environment;

    private readonly List<Tab> _tabs = new();
    private Tab? _current;
    private readonly Grid _pages = new();
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _address = new();
    private readonly TextBlock _lock = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _zoomText = Ui.Text("", 14, "SubtleBrush");
    private readonly TextBlock _star = new() { FontFamily = Ui.IconFont, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _favBar = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _favScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.HorizontalOnly, Margin = new Thickness(18, 0, 18, 8) };
    private readonly Button _reload;
    private readonly Border _progress = new() { Height = 3, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(2) };
    private readonly TextBlock _status = Ui.Text("Starting the browser…", 20, "SubtleBrush");
    private readonly Grid _chrome = new();
    private readonly Grid _footer = new() { Margin = new Thickness(30, 0, 30, 0) };
    private readonly string? _startUrl;
    private bool _started, _disposed;
    private double _scrollCarry;
    private DateTime _pointerMovedAt = DateTime.MinValue;
    private bool _stickWasPressed;
    private double _moveX, _moveY;

    /// <summary>Opens the browser: the one kept running (with a new tab for <paramref name="url"/>), or a new one.</summary>
    public static BrowserScreen Open(MainWindow host, string? url = null)
    {
        if (host.Settings.BrowserKeepOpen && _kept is { _disposed: false } kept)
        {
            if (url != null) kept.Dispatcher.BeginInvoke(new Action(() => kept.NewTab(url)));
            return kept;
        }
        return new BrowserScreen(url);
    }

    /// <summary>Closes a browser kept in the background (when the setting is switched off).</summary>
    public static void DisposeKept()
    {
        _kept?.DisposeAll();
        _kept = null;
    }

    public BrowserScreen(string? url = null)
    {
        _startUrl = url;

        Button Round(string glyph, string tip, Action click)
        {
            var b = new Button
            {
                Style = Ui.StyleOf("PillButton"), Width = 46, Height = 46, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0),
                Focusable = false, ToolTip = tip,
                Content = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center }
            };
            b.Click += (_, _) => click();
            return b;
        }

        // Row 1: tabs and "new tab".
        var stripRow = new DockPanel { Margin = new Thickness(18, 10, 18, 6), LastChildFill = true };
        var newTab = Round("", "New tab", () => NewTab(Host.Settings.BrowserHome));
        newTab.Width = newTab.Height = 36;
        DockPanel.SetDock(newTab, Dock.Right);
        stripRow.Children.Add(newTab);
        stripRow.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.HorizontalOnly, Content = _strip
        });

        // Row 2: back, forward, reload, home, the address bar, zoom and menu.
        var bar = new DockPanel { Margin = new Thickness(18, 0, 18, 8), LastChildFill = true };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Round("", "Back", () => { if (_current is { Ready: true } t && t.Web.CanGoBack) t.Web.GoBack(); }));
        left.Children.Add(Round("", "Forward", () => { if (_current is { Ready: true } t && t.Web.CanGoForward) t.Web.GoForward(); }));
        _reload = Round("", "Reload", () =>
        {
            if (_current is not { Ready: true } t) return;
            if (t.Loading) t.Web.Stop(); else t.Web.Reload();
        });
        left.Children.Add(_reload);
        left.Children.Add(Round("", "Home page", GoHome));
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var zoom = new Border { CornerRadius = new CornerRadius(23), Height = 46, Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(4, 0, 4, 0) };
        zoom.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var minus = Round("", "Zoom out", () => Zoom(-10));
        minus.Width = minus.Height = 38;
        minus.Margin = new Thickness(0);
        var plus = Round("", "Zoom in", () => Zoom(+10));
        plus.Width = plus.Height = 38;
        plus.Margin = new Thickness(0);
        _zoomText.VerticalAlignment = VerticalAlignment.Center;
        _zoomText.Margin = new Thickness(6, 0, 6, 0);
        zoomRow.Children.Add(minus);
        zoomRow.Children.Add(_zoomText);
        zoomRow.Children.Add(plus);
        zoom.Child = zoomRow;
        right.Children.Add(zoom);
        right.Children.Add(Round("", "Menu", () => _ = MenuAsync()));
        var close = Round("", "Close the browser", () => Host.Pop(this));
        close.Margin = new Thickness(0);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);

        // The address bar: a rounded pill with a padlock for secure pages; type straight into it.
        var pill = new Border { CornerRadius = new CornerRadius(23), Height = 46, Margin = new Thickness(4, 0, 4, 0), BorderThickness = new Thickness(2) };
        pill.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        pill.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        var pillGrid = new Grid();
        pillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        pillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        // The star: add this page to the favorites, or take it off.
        var starButton = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, ToolTip = "Add to favorites", Content = _star, HorizontalAlignment = HorizontalAlignment.Center };
        starButton.Click += (_, _) => ToggleFavorite();
        Grid.SetColumn(starButton, 2);
        pillGrid.Children.Add(starButton);
        _lock.FontFamily = Ui.IconFont;
        _lock.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.VerticalAlignment = VerticalAlignment.Center;
        pillGrid.Children.Add(_lock);
        _address.Background = Brushes.Transparent;
        _address.BorderThickness = new Thickness(0);
        _address.FontSize = 16;
        _address.VerticalContentAlignment = VerticalAlignment.Center;
        _address.FocusVisualStyle = null;
        _address.SetResourceReference(Control.ForegroundProperty, "FgBrush");
        _address.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        _address.GotKeyboardFocus += (_, _) =>
        {
            pill.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            _address.Text = _current?.Web.Source?.ToString() ?? "";
            _address.SelectAll();
        };
        _address.LostKeyboardFocus += (_, _) => { pill.SetResourceReference(Border.BorderBrushProperty, "PanelStroke"); ShowAddress(); };
        _address.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { Navigate(_address.Text); _current?.Web.Focus(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape) { _current?.Web.Focus(); e.Handled = true; }
        };
        Grid.SetColumn(_address, 1);
        pillGrid.Children.Add(_address);
        pill.Child = pillGrid;
        bar.Children.Add(pill);

        _progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var chromeStack = new StackPanel();
        chromeStack.Children.Add(stripRow);
        chromeStack.Children.Add(bar);
        _favScroll.Content = _favBar;
        chromeStack.Children.Add(_favScroll);
        chromeStack.Children.Add(_progress);
        _chrome.Children.Add(chromeStack);

        var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.15 };
        line.SetResourceReference(Shape.FillProperty, "FgBrush");
        _footer.Children.Add(line);
        _footer.Children.Add(new HintBar(
            "R-stick  Pointer      {Select}  Click      LB / RB  Tabs      {Details}  Address      {Options}  Menu      {Back}  Back")
        { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 26, 0) });

        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.MaxWidth = 760;

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.Children.Add(_chrome);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        Grid.SetRow(_pages, 1);
        root.Children.Add(_pages);
        Grid.SetRow(_footer, 2);
        root.Children.Add(_footer);
        Content = root;
    }

    public override void FocusDefault() { }

    /// <summary>Starter favorites: sites a gamer is likely to want, added once (they can be removed or renamed).</summary>
    public static readonly (string Title, string Url)[] StarterFavorites =
    {
        ("YouTube", "https://www.youtube.com/"), ("Twitch", "https://www.twitch.tv/"), ("Reddit", "https://www.reddit.com/"),
        ("Outlook", "https://outlook.live.com/mail/"), ("Loaded", "https://www.loaded.com/"), ("Amazon", "https://www.amazon.com/"),
        ("Steam Store", "https://store.steampowered.com/"), ("Discord", "https://discord.com/app"),
        ("PCGamingWiki", "https://www.pcgamingwiki.com/"), ("HowLongToBeat", "https://howlongtobeat.com/"),
        ("IsThereAnyDeal", "https://isthereanydeal.com/"), ("Nexus Mods", "https://www.nexusmods.com/"), ("IGN", "https://www.ign.com/")
    };

    private void SeedFavorites()
    {
        var s = Host.Settings;
        if (s.BrowserFavoritesSeeded) return;
        foreach (var (title, url) in StarterFavorites)
            if (!s.BrowserFavorites.Any(f => string.Equals(f.Url.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                s.BrowserFavorites.Add(new BrowserFavorite { Title = title, Url = url });
        s.BrowserFavoritesSeeded = true;
        s.Save();
    }

    public override void OnShown()
    {
        _zoomText.Text = Host.Settings.BrowserZoom + "%";
        SeedFavorites();
        BuildFavoritesBar();
        if (Host.Settings.BrowserKeepOpen) _kept = this;
        if (_started)
        {
            // Back from the background: everything is as it was.
            ShowCurrent();
            return;
        }
        _started = true;
        _ = StartAsync();
    }

    // The web view is a window of its own and would cover any pop-up drawn over it, so it steps aside for them.
    public override void OnCovered() => _pages.Visibility = Visibility.Hidden;

    public override void OnResumed() => _pages.Visibility = Visibility.Visible;

    public override void OnClosed()
    {
        _pages.Visibility = Visibility.Visible;
        if (Host.Settings.BrowserKeepOpen && !_disposed)
        {
            // Stays running in the background; pause any video or sound so it doesn't keep playing unseen.
            foreach (var t in _tabs.Where(t => t.Ready))
                _ = t.Web.ExecuteScriptAsync("document.querySelectorAll('video,audio').forEach(function(m){try{m.pause();}catch(e){}});");
            _kept = this;
            return;
        }
        DisposeAll();
        if (ReferenceEquals(_kept, this)) _kept = null;
    }

    private void DisposeAll()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var t in _tabs)
        {
            try { t.Web.Dispose(); } catch (Exception) { }
        }
        _tabs.Clear();
    }

    private async Task StartAsync()
    {
        try
        {
            // Cookies and sign-ins are kept in the launcher's own folder, separate from Edge.
            _environment ??= CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(Settings.DataDir, "Browser"));
            await _environment;
            _status.Visibility = Visibility.Collapsed;
            NewTab(_startUrl ?? Host.Settings.BrowserHome);
        }
        catch (Exception ex)
        {
            _environment = null;
            _status.Text = "The built-in browser couldn't start. It needs the Microsoft Edge WebView2 Runtime, which comes with " +
                           "Windows 11 and current Windows 10. Install it from microsoft.com/edge/webview2 and try again.\n\n" + ex.Message;
        }
    }

    // ================================================================ tabs

    private void NewTab(string url)
    {
        if (_disposed || _environment == null) return;
        // Too many tabs slow a handheld down: at the limit, the page opens in the current tab instead.
        int max = Math.Max(1, Host.Settings.BrowserMaxTabs);
        if (_tabs.Count >= max && _current != null)
        {
            Host.ShowToast("Browser", $"{max} tabs is the limit", "Opened here instead. Close a tab (or raise the limit in Settings) to open more.", "", "info");
            Navigate(url, _current);
            return;
        }
        var tab = new Tab { Web = new WebView2 { Visibility = Visibility.Hidden }, PendingUrl = url };
        _pages.Children.Add(tab.Web);

        // The chip in the tab strip: the site's icon, its title and a close button.
        tab.Icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        tab.Title = Ui.Text("New tab", 14, "FgBrush");
        tab.Title.MaxWidth = 170;
        tab.Title.TextTrimming = TextTrimming.CharacterEllipsis;
        tab.Title.VerticalAlignment = VerticalAlignment.Center;
        var x = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Close tab",
                             Content = Ui.Glyph("", 10, "SubtleBrush") };
        x.Click += (_, e) => { e.Handled = true; CloseTab(tab); };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(tab.Icon);
        row.Children.Add(tab.Title);
        row.Children.Add(x);
        var face = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 10, 8), Child = row, BorderThickness = new Thickness(0, 0, 0, 3) };
        tab.Chip = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Content = face, Margin = new Thickness(0, 0, 6, 0) };
        tab.Chip.Click += (_, _) => Select(tab);
        _strip.Children.Add(tab.Chip);
        _tabs.Add(tab);
        Select(tab);
        _ = InitTabAsync(tab);
    }

    private async Task InitTabAsync(Tab tab)
    {
        try
        {
            await tab.Web.EnsureCoreWebView2Async(await _environment!);
            if (_disposed) return;
            var core = tab.Web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            // Links that want a new window open in a new tab.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri)) NewTab(e.Uri);
            };
            core.DocumentTitleChanged += (_, _) => { tab.Title.Text = core.DocumentTitle.Length > 0 ? core.DocumentTitle : "New tab"; ShowAddress(); };
            core.FaviconChanged += async (_, _) =>
            {
                try
                {
                    using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                    using var ms = new System.IO.MemoryStream();
                    await stream.CopyToAsync(ms);
                    tab.Icon.Source = ms.Length > 0 ? ImageLoader.FromBytes(ms.ToArray(), 32) : null;
                }
                catch (Exception) { }
            };
            // Full-screen video takes the whole page: hide our own bars while it plays.
            core.ContainsFullScreenElementChanged += (_, _) =>
                _chrome.Visibility = _footer.Visibility = core.ContainsFullScreenElement ? Visibility.Collapsed : Visibility.Visible;
            tab.Web.NavigationStarting += (_, _) => { tab.Loading = true; if (tab == _current) ShowLoading(true); };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(HelperScript);
            tab.Web.NavigationCompleted += (_, _) =>
            {
                tab.Loading = false;
                if (tab == _current) { ShowLoading(false); ShowAddress(); }
                _ = Run(tab, HelperScript); // in case the page was created before the helper was added
            };
            tab.Web.SourceChanged += (_, _) => { if (tab == _current) ShowAddress(); };
            tab.Web.ZoomFactor = Host.Settings.BrowserZoom / 100.0;
            tab.Ready = true;
            if (tab.PendingUrl != null) Navigate(tab.PendingUrl, tab);
            tab.PendingUrl = null;
            if (tab == _current) ShowCurrent();
        }
        catch (Exception) { tab.Title.Text = "Couldn't open"; }
    }

    private void Select(Tab tab)
    {
        _current = tab;
        ShowCurrent();
    }

    /// <summary>Shows the current tab's page, marks its chip, and fills in the address bar.</summary>
    private void ShowCurrent()
    {
        foreach (var t in _tabs)
        {
            bool on = t == _current;
            t.Web.Visibility = on && t.Ready ? Visibility.Visible : Visibility.Hidden;
            if (t.Chip.Content is Border face)
            {
                if (on) { face.SetResourceReference(Border.BackgroundProperty, "CircleBrush"); face.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
                else { face.Background = Brushes.Transparent; face.BorderBrush = Brushes.Transparent; }
            }
        }
        _current?.Chip.BringIntoView();
        ShowLoading(_current?.Loading == true);
        ShowAddress();
    }

    private void CloseTab(Tab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        _strip.Children.Remove(tab.Chip);
        _pages.Children.Remove(tab.Web);
        try { tab.Web.Dispose(); } catch (Exception) { }
        if (_tabs.Count == 0)
        {
            // Closing the last tab opens a fresh one at the home page.
            NewTab(Host.Settings.BrowserHome);
            return;
        }
        if (tab == _current) Select(_tabs[Math.Min(index, _tabs.Count - 1)]);
    }

    private void StepTab(int step)
    {
        if (_tabs.Count < 2 || _current == null) return;
        int i = (_tabs.IndexOf(_current) + step + _tabs.Count) % _tabs.Count;
        Select(_tabs[i]);
        UiSound.Page();
    }

    // ================================================================ favorites

    private string? CurrentUrl => _current is { Ready: true } t ? t.Web.Source?.ToString() : null;

    private BrowserFavorite? FavoriteFor(string? url) =>
        url == null ? null : Host.Settings.BrowserFavorites.FirstOrDefault(f => string.Equals(f.Url.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    private void UpdateStar()
    {
        bool saved = FavoriteFor(CurrentUrl) != null;
        _star.Text = saved ? "" : ""; // filled or outline star
        _star.SetResourceReference(TextBlock.ForegroundProperty, saved ? "AccentBrush" : "SubtleBrush");
    }

    /// <summary>Adds the page to the favorites, or takes it off if it's already there.</summary>
    private void ToggleFavorite()
    {
        var url = CurrentUrl;
        if (string.IsNullOrEmpty(url) || url == "about:blank") return;
        var s = Host.Settings;
        if (FavoriteFor(url) is { } existing)
        {
            s.BrowserFavorites.Remove(existing);
            Host.ShowToast("Browser", "Removed from favorites", existing.Title, "", "info");
        }
        else
        {
            string title = _current?.Web.CoreWebView2?.DocumentTitle is { Length: > 0 } t ? t : new Uri(url).Host;
            s.BrowserFavorites.Add(new BrowserFavorite { Title = title, Url = url });
            Host.ShowToast("Browser", "Added to favorites", title, "", "info");
        }
        s.Save();
        UiSound.Select();
        UpdateStar();
        BuildFavoritesBar();
    }

    /// <summary>The favorites as chips under the address bar (hidden when there are none).</summary>
    private void BuildFavoritesBar()
    {
        _favBar.Children.Clear();
        var favorites = Host.Settings.BrowserFavorites;
        _favScroll.Visibility = favorites.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var f in favorites)
        {
            var fav = f;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var star = Ui.Glyph("", 12, "AccentBrush");
            star.VerticalAlignment = VerticalAlignment.Center;
            star.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(star);
            var name = Ui.Text(fav.Title, 13, "FgBrush");
            name.MaxWidth = 160;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(name);
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Child = row };
            chip.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var b = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Content = chip, Margin = new Thickness(0, 0, 6, 0), ToolTip = fav.Url };
            b.Click += (_, _) => Navigate(fav.Url);
            b.MouseRightButtonUp += (_, e) => { e.Handled = true; _ = FavoriteOptionsAsync(fav); };
            _favBar.Children.Add(b);
        }
    }

    /// <summary>All favorites in a list: open one, or manage it.</summary>
    private async Task FavoritesMenuAsync()
    {
        var favorites = Host.Settings.BrowserFavorites;
        if (favorites.Count == 0)
        {
            await Host.AskAsync("No favorites yet", "Open a page and press the star in the address bar (or choose \"Add to favorites\" in the menu).", "OK");
            return;
        }
        var options = favorites.Select(f => (f.Title, "")).Append(("Manage favorites…", "")).ToArray();
        int pick = await Host.AskWithIconsAsync("Favorites", null, options);
        if (pick < 0) return;
        if (pick < favorites.Count) { Navigate(favorites[pick].Url); return; }
        int which = await Host.AskWithIconsAsync("Manage favorites", "Choose one to rename, move or remove.", favorites.Select(f => (f.Title, "")).ToArray());
        if (which >= 0) await FavoriteOptionsAsync(favorites[which]);
    }

    private async Task FavoriteOptionsAsync(BrowserFavorite fav)
    {
        var s = Host.Settings;
        int i = s.BrowserFavorites.IndexOf(fav);
        int choice = await Host.AskWithIconsAsync(fav.Title, fav.Url,
            ("Open", ""), ("Open in a new tab", ""), ("Rename", ""), ("Move left", ""), ("Move right", ""), ("Remove", ""));
        switch (choice)
        {
            case 0: Navigate(fav.Url); return;
            case 1: NewTab(fav.Url); return;
            case 2:
                var name = await Host.PromptAsync("Rename favorite", null, fav.Title, "Save");
                if (!string.IsNullOrWhiteSpace(name)) fav.Title = name.Trim();
                break;
            case 3 when i > 0: s.BrowserFavorites.RemoveAt(i); s.BrowserFavorites.Insert(i - 1, fav); break;
            case 4 when i < s.BrowserFavorites.Count - 1: s.BrowserFavorites.RemoveAt(i); s.BrowserFavorites.Insert(i + 1, fav); break;
            case 5: s.BrowserFavorites.Remove(fav); break;
            default: return;
        }
        s.Save();
        BuildFavoritesBar();
        UpdateStar();
    }

    // ================================================================ page

    private void ShowLoading(bool loading)
    {
        if (_reload.Content is TextBlock glyph) glyph.Text = loading ? "" : "";
        _progress.BeginAnimation(WidthProperty, null);
        if (!loading) { _progress.Width = 0; return; }
        // A bar that creeps across while the page loads.
        double full = Math.Max(200, ActualWidth);
        _progress.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(0, full * 0.85, TimeSpan.FromSeconds(4))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void ShowAddress()
    {
        UpdateStar();
        if (_address.IsKeyboardFocused) return;
        var source = _current is { Ready: true } t ? t.Web.Source : null;
        if (source == null || source.ToString() == "about:blank") { _address.Text = ""; _lock.Text = ""; return; }
        _lock.Text = source.Scheme == "https" ? "" : ""; // padlock, or a warning for plain http
        // The site bold-ish first, then the rest of the address.
        _address.Text = source.Host.StartsWith("www.") ? source.Host[4..] + source.PathAndQuery.TrimEnd('/') : source.Host + source.PathAndQuery.TrimEnd('/');
    }

    /// <summary>Goes to an address, or searches for the words if it doesn't look like one.</summary>
    private void Navigate(string text, Tab? tab = null)
    {
        tab ??= _current;
        if (tab == null) return;
        text = text.Trim();
        if (text.Length == 0) return;
        if (!tab.Ready) { tab.PendingUrl = text; return; }

        string url;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = text;
        else if (!text.Contains(' ') && text.Contains('.') && Uri.TryCreate("https://" + text, UriKind.Absolute, out _))
            url = "https://" + text;
        else
            url = (Engines.FirstOrDefault(e => e.Id == Host.Settings.BrowserSearch).Url ?? Engines[0].Url) + Uri.EscapeDataString(text);

        try { tab.Web.CoreWebView2.Navigate(url); }
        catch (Exception) { /* not an address the browser accepts */ }
    }

    private void GoHome() => Navigate(Host.Settings.BrowserHome);

    private void Zoom(int step)
    {
        var s = Host.Settings;
        s.BrowserZoom = Math.Clamp(s.BrowserZoom + step, 50, 300);
        s.Save();
        _zoomText.Text = s.BrowserZoom + "%";
        foreach (var t in _tabs.Where(t => t.Ready)) t.Web.ZoomFactor = s.BrowserZoom / 100.0;
    }

    /// <summary>Type an address: straight into the bar with Windows' keyboard, or with SquareDeck's own keys.</summary>
    private async Task EnterAddressAsync()
    {
        if (TouchKeyboard.UseWindows)
        {
            _address.Focus();
            await TouchKeyboard.ShowAsync();
            return;
        }
        var text = await Host.PromptAsync("Address or search", "Type a web address, or words to search for.", "", "Go");
        if (!string.IsNullOrWhiteSpace(text)) Navigate(text);
    }

    private async Task MenuAsync()
    {
        var s = Host.Settings;
        string engine = Engines.FirstOrDefault(e => e.Id == s.BrowserSearch).Name ?? "Google";
        var web = _current is { Ready: true } t ? t.Web : null;
        bool saved = FavoriteFor(CurrentUrl) != null;
        int fav = await Host.AskWithIconsAsync("Browser", web?.Source?.ToString(),
            ("Favorites\u2026", "\uE728"), (saved ? "Remove from favorites" : "Add to favorites", saved ? "\uE735" : "\uE734"), ("More\u2026", "\uE712"));
        if (fav == 0) { await FavoritesMenuAsync(); return; }
        if (fav == 1) { ToggleFavorite(); return; }
        if (fav != 2) return;
        int choice = await Host.AskWithIconsAsync("Browser", web?.Source?.ToString(),
            ("New tab", ""), ("Close this tab", ""), ("Address or search", ""), ("Forward", ""),
            ("Reload", ""), ("Home page", ""), ("Use this page as the home page", ""),
            ("Search engine: " + engine, ""),
            ("Right stick: " + (s.BrowserStickMouse ? "moves the pointer" : "scrolls the page"), ""),
            ("Keep the browser open when I leave: " + (s.BrowserKeepOpen ? "On" : "Off"), ""),
            ("Clear cookies, history and saved data", ""), ("Open this page in the default browser", ""),
            ("Close the browser", ""));
        switch (choice)
        {
            case 0: NewTab(s.BrowserHome); break;
            case 1: if (_current != null) CloseTab(_current); break;
            case 2: await EnterAddressAsync(); break;
            case 3: if (web is { CanGoForward: true }) web.GoForward(); break;
            case 4: web?.Reload(); break;
            case 5: GoHome(); break;
            case 6: if (web?.Source != null) { s.BrowserHome = web.Source.ToString(); s.Save(); } break;
            case 7:
                int index = Array.FindIndex(Engines, e => e.Id == s.BrowserSearch);
                s.BrowserSearch = Engines[(index + 1) % Engines.Length].Id;
                s.Save();
                break;
            case 8: s.BrowserStickMouse = !s.BrowserStickMouse; s.Save(); break;
            case 9: s.BrowserKeepOpen = !s.BrowserKeepOpen; s.Save(); break;
            case 10:
                if (web != null && await Host.AskAsync("Clear browsing data?", "You will be signed out of websites in this browser.", "Cancel", "Clear") == 1)
                {
                    try { await web.CoreWebView2.Profile.ClearBrowsingDataAsync(); } catch (Exception) { }
                }
                break;
            case 11: if (web?.Source != null) ProcessUtil.OpenOutside(web.Source.ToString()); break; // Windows' own browser, always
            case 12:
                DisposeAll(); // closing from the menu really closes it, even when it would otherwise stay open
                if (ReferenceEquals(_kept, this)) _kept = null;
                Host.Pop(this);
                break;
        }
    }

    // ================================================================ controller

    // Helpers added to every page as it loads (window.__sd), so the controller works the same on every site:
    //  - looks through shadow DOM (YouTube and other modern sites build their pages from it) and into frames from the
    //    same site, which a plain search of the page misses, so links were sometimes unreachable;
    //  - scrolls whatever part of the page actually scrolls (mail, chat and feed sites scroll a panel, not the page);
    //  - draws a clear ring around what the d-pad has landed on.
    internal const string HelperScript = """
        (function(){ if(window.__sd) return;
          var SEL='a[href],button,input:not([type=hidden]),select,textarea,summary,video,[role=button],[role=link],[role=tab],[role=menuitem],[role=option],[role=checkbox],[role=switch],[contenteditable=true],[tabindex]:not([tabindex="-1"])';
          function ring(doc){ try{ if(doc.getElementById('sd-focus-ring')) return; var s=doc.createElement('style'); s.id='sd-focus-ring';
            s.textContent='*:focus,*:focus-visible{outline:3px solid #20E0D0 !important;outline-offset:2px !important;}';
            (doc.head||doc.documentElement).appendChild(s);}catch(e){} }
          function all(root, ox, oy, out){
            var list; try{ list=root.querySelectorAll('*'); }catch(e){ return; }
            for(var i=0;i<list.length;i++){ var e=list[i];
              if(e.matches&&e.matches(SEL)) out.push({e:e,ox:ox,oy:oy});
              if(e.shadowRoot) all(e.shadowRoot, ox, oy, out);
              if(e.tagName==='IFRAME'){ try{ var d=e.contentDocument; if(d){ var r=e.getBoundingClientRect(); ring(d); all(d, ox+r.left, oy+r.top, out);} }catch(x){} } }
          }
          function rect(it){ var r=it.e.getBoundingClientRect(); return {l:r.left+it.ox,t:r.top+it.oy,w:r.width,h:r.height}; }
          function visible(it){ var e=it.e; if(e.disabled) return false; var r=rect(it); if(r.w<2||r.h<2) return false;
            var c=getComputedStyle(e); return c.visibility!=='hidden'&&c.display!=='none'&&c.opacity!=='0'; }
          function active(){ var e=document.activeElement;
            for(var n=0;n<20&&e;n++){ if(e.shadowRoot&&e.shadowRoot.activeElement){ e=e.shadowRoot.activeElement; continue; }
              if(e.tagName==='IFRAME'){ try{ var a=e.contentDocument&&e.contentDocument.activeElement; if(a&&a!==e.contentDocument.body){ e=a; continue; } }catch(x){} }
              break; }
            return e; }
          function scroller(from){ var e=from;
            while(e&&e!==document.body&&e!==document.documentElement){
              var c=getComputedStyle(e); if(/(auto|scroll|overlay)/.test(c.overflowY)&&e.scrollHeight>e.clientHeight+4) return e;
              e=e.parentElement||(e.getRootNode&&e.getRootNode().host)||null; }
            return null; }
          function scroll(dy){ var a=active(); var start=(a&&a!==document.body)?a:document.elementFromPoint(innerWidth/2,innerHeight/2);
            var s=scroller(start); if(s) s.scrollBy({top:dy,behavior:'smooth'}); else window.scrollBy({top:dy,behavior:'smooth'}); }
          function move(dir){
            var vw=innerWidth, vh=innerHeight, items=[]; all(document,0,0,items); items=items.filter(visible);
            if(!items.length){ scroll((dir==='down'?1:dir==='up'?-1:0)*vh*0.5); return 'scroll'; }
            var cur=active(), ci=-1; for(var i=0;i<items.length;i++) if(items[i].e===cur){ ci=i; break; }
            if(ci<0){ var first=null; for(var j=0;j<items.length;j++){ var r=rect(items[j]); if(r.t>=0&&r.t<vh&&r.l>=0&&r.l<vw){ first=items[j]; break; } }
              first=first||items[0]; first.e.focus({preventScroll:true}); first.e.scrollIntoView({block:'nearest',inline:'nearest'}); return 'first'; }
            var cr=rect(items[ci]), cx=cr.l+cr.w/2, cy=cr.t+cr.h/2, best=null, bs=1e18;
            items.forEach(function(it,k){ if(k===ci) return; var r=rect(it); var dx=r.l+r.w/2-cx, dy=r.t+r.h/2-cy, main, cross;
              if(dir==='right'){ if(dx<6) return; main=dx; cross=Math.abs(dy); }
              else if(dir==='left'){ if(dx>-6) return; main=-dx; cross=Math.abs(dy); }
              else if(dir==='down'){ if(dy<6) return; main=dy; cross=Math.abs(dx); }
              else { if(dy>-6) return; main=-dy; cross=Math.abs(dx); }
              var score=main+cross*2.5; if(score<bs){ bs=score; best=it; } });
            if(best){ best.e.focus({preventScroll:true}); best.e.scrollIntoView({block:'nearest',inline:'nearest',behavior:'smooth'}); return 'moved'; }
            if(dir==='down'||dir==='up'){ scroll((dir==='down'?1:-1)*vh*0.5); return 'scroll'; }
            return 'edge'; }
          function click(){ var e=active(); if(!e||e===document.body) return 'none';
            var t=(e.tagName||'').toLowerCase();
            var text=(t==='input'&&!/^(button|submit|checkbox|radio|range|file|color|image|reset)$/i.test(e.type||'text'))||t==='textarea'||e.isContentEditable;
            if(text) return 'text:'+(e.type==='password'?'':(e.value||''));
            if(t==='video'){ if(e.paused) e.play(); else e.pause(); return 'ok'; }
            e.click(); return 'ok'; }
          function type(v){ var e=active(); if(!e) return 'none';
            if(e.isContentEditable){ e.focus(); document.execCommand('insertText',false,v); return 'ok'; }
            var p=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
            var d=Object.getOwnPropertyDescriptor(p,'value'); if(d&&d.set) d.set.call(e,v); else e.value=v;
            e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true}));
            var search=e.type==='search'||e.name==='q'||e.getAttribute('role')==='searchbox'||e.getAttribute('role')==='combobox';
            if(search&&e.form){ if(e.form.requestSubmit) e.form.requestSubmit(); else e.form.submit(); }
            else if(search){ e.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,bubbles:true})); }
            return 'ok'; }
          window.__sd={move:move,click:click,type:type,scroll:scroll};
          if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',function(){ring(document);}); else ring(document);
        })();
        """;

    private async Task<string> Run(Tab? tab, string script)
    {
        if (tab is not { Ready: true } || _disposed) return "";
        try
        {
            var json = await tab.Web.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<string>(json) ?? "";
        }
        catch (Exception) { return ""; } // page still loading, or the result wasn't text
    }

    private Task<string> RunAsync(string script) => Run(_current, script);

    /// <summary>Runs one of the page helpers, adding them first if the page doesn't have them yet.</summary>
    private Task<string> Sd(string call) => RunAsync("(function(){" + HelperScript + "; return window.__sd ? window.__sd." + call + " : '';})()");

    private async Task ClickAsync()
    {
        var result = await Sd("click()");
        if (!result.StartsWith("text:", StringComparison.Ordinal)) return;

        // Windows' touch keyboard types straight into the page.
        if (TouchKeyboard.UseWindows)
        {
            _current?.Web.Focus();
            await TouchKeyboard.ShowAsync();
            return;
        }
        var typed = await Host.PromptAsync("Type here", null, result[5..], "Enter");
        if (typed == null) return;
        // Put the text in the box the page had highlighted, tell the page it changed, and send a search box on its way.
        await Sd("type(" + JsonSerializer.Serialize(typed) + ")");
    }

    /// <summary>The page gets the keyboard focus, so the ring shows and keys reach it (not the launcher's own bars).</summary>
    private void EnsurePageFocus()
    {
        // Only when the focus is somewhere else (giving it again would make the page lose its own highlight).
        if (_current is { Ready: true } t && !_address.IsKeyboardFocused &&
            !ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, t.Web) && !t.Web.IsKeyboardFocusWithin)
            t.Web.Focus();
    }

    /// <summary>True for a few seconds after the pointer was moved with the right stick: A then clicks at the pointer.</summary>
    private bool PointerActive => DateTime.UtcNow - _pointerMovedAt < TimeSpan.FromSeconds(4);

    public override bool OnAction(NavAction action)
    {
        if (_address.IsKeyboardFocused) return action is not NavAction.Back ? false : FocusPage();
        if (action is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right or NavAction.Accept) EnsurePageFocus();
        switch (action)
        {
            case NavAction.Up: _ = Sd("move('up')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Down: _ = Sd("move('down')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Left: _ = Sd("move('left')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Right: _ = Sd("move('right')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Accept:
                if (PointerActive) MouseClick(); else _ = ClickAsync();
                return true;
            case NavAction.X: _ = EnterAddressAsync(); return true;
            case NavAction.Y: _ = MenuAsync(); return true;
            case NavAction.Start: NewTab(Host.Settings.BrowserHome); return true;
            case NavAction.LB: StepTab(-1); return true;
            case NavAction.RB: StepTab(+1); return true;
            case NavAction.LT: _ = Sd("scroll(-innerHeight*0.85)"); return true;
            case NavAction.RT: _ = Sd("scroll(innerHeight*0.85)"); return true;
            case NavAction.Back:
                // B is the browser's Back button until there is nowhere left to go back to.
                if (_current is { Ready: true } t && t.Web.CanGoBack) { t.Web.GoBack(); return true; }
                return false;
        }
        return false;
    }

    private bool FocusPage()
    {
        _current?.Web.Focus();
        return true;
    }

    public override bool OnRightStick(double x, double y, bool pressed)
    {
        // Right stick pressed in: click where the pointer is.
        if (pressed && !_stickWasPressed) MouseClick();
        _stickWasPressed = pressed;
        if (x == 0 && y == 0) { _moveX = _moveY = 0; return true; }

        if (!Host.Settings.BrowserStickMouse)
        {
            // Scroll instead, sent in small batches.
            _scrollCarry += -y * Math.Abs(y) * 34;
            if (Math.Abs(_scrollCarry) < 24) return true;
            int amount = (int)_scrollCarry;
            _scrollCarry = 0;
            _ = Sd($"scroll({amount})");
            return true;
        }

        // The pointer speeds up the further the stick is pushed; fractions carry over so slow moves stay smooth.
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
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // left button down
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // left button up
        _pointerMovedAt = DateTime.UtcNow;
        UiSound.Select();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
}

/// <summary>What's new in a release, with the button that downloads and installs it.</summary>
public sealed class UpdateScreen : ListScreen
{
    private readonly ReleaseInfo _release;

    public UpdateScreen(ReleaseInfo release) : base("SquareDeck " + release.Tag) => _release = release;

    protected override void Build()
    {
        AddNote($"You have version {Updater.Current}." +
                (_release.Published is DateTime when ? $"  This release was published on {when:MMMM d, yyyy}." : ""));

        if (_release.AssetUrl != null)
            AddRow("Download and install", _release.AssetName + (_release.AssetSize > 0 ? "   " + SystemStats.Bytes(_release.AssetSize) : ""),
                "", click: () => _ = Host.InstallUpdateAsync(_release));
        else
            AddNote("This release has no installer or ZIP attached, so it can't be installed from here. Open its page to get it.");
        AddRow("View changelogs", null, glyph: "\uE81C", click: () => Host.Push(new ChangelogScreen()));
        AddRow("Not now", glyph: "", click: () => Host.Pop(this));

        AddHeader("What's new");
        var notes = Updater.PlainNotes(_release.Notes);
        if (notes.Length == 0) AddNote("No changes are listed for this version yet. Choose View changelogs to see the latest.");
        foreach (var paragraph in notes.Split('\n'))
        {
            if (paragraph.Trim().Length == 0) continue;
            var block = Ui.Text(paragraph, 18);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            block.Margin = new Thickness(4, 4, 12, 4);
            Rows.Children.Add(block);
        }
    }
}

/// <summary>A grid of symbols to choose one from (collection tiles use it).</summary>
public sealed class IconPickerScreen : GridScreen
{
    public static readonly string[] Icons =
    {
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", ""
    };

    public IconPickerScreen(string title, Action<string> picked) : base(title)
    {
        foreach (var icon in Icons)
        {
            string glyph = icon;
            AddTile(GlyphBox(110, 110, glyph), null, 110, () => { Host.Pop(this); picked(glyph); });
        }
    }
}

/// <summary>
/// Pick any color: tap or click the color field, or use the three sliders (hue, saturation, brightness)
/// with the controller.
/// </summary>
public sealed class ColorPickerScreen : Screen
{
    private const double FieldWidth = 620, FieldHeight = 250;

    private readonly Action<string> _picked;
    private readonly Slider _hue, _saturation, _brightness;
    private readonly Ellipse _cursor = new() { Width = 22, Height = 22, StrokeThickness = 3, Stroke = Brushes.White, IsHitTestVisible = false };
    private readonly Rectangle _dim = new() { Fill = Brushes.Black, IsHitTestVisible = false };
    private readonly Border _swatch = new() { Width = 150, Height = 150, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2) };
    private readonly TextBlock _hex = Ui.Text("", 24, "FgBrush", bold: true);
    private readonly Button _use;
    private bool _syncing;

    public ColorPickerScreen(Color start, Action<string> picked)
    {
        _picked = picked;

        // Every hue left to right, fading to white toward the bottom; the brightness slider darkens the whole field.
        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var rainbow = new[] { "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FF0000" };
        for (int i = 0; i < rainbow.Length; i++)
            hues.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(rainbow[i]), i / 6.0));
        var white = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        white.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        white.GradientStops.Add(new GradientStop(Colors.White, 1));

        var field = new Grid { Width = FieldWidth, Height = FieldHeight, Cursor = Cursors.Cross, ClipToBounds = true };
        field.Children.Add(new Rectangle { Fill = hues });
        field.Children.Add(new Rectangle { Fill = white });
        field.Children.Add(_dim);
        var canvas = new Canvas { IsHitTestVisible = false };
        _cursor.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 };
        canvas.Children.Add(_cursor);
        field.Children.Add(canvas);

        void PickAt(Point p)
        {
            _syncing = true;
            _hue!.Value = Math.Clamp(p.X / FieldWidth, 0, 1) * 360;
            _saturation!.Value = (1 - Math.Clamp(p.Y / FieldHeight, 0, 1)) * 100;
            _syncing = false;
            Update();
        }
        field.MouseLeftButtonDown += (_, e) => { field.CaptureMouse(); PickAt(e.GetPosition(field)); };
        field.MouseMove += (_, e) => { if (field.IsMouseCaptured) PickAt(e.GetPosition(field)); };
        field.MouseLeftButtonUp += (_, _) => field.ReleaseMouseCapture();

        var fieldBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = field, HorizontalAlignment = HorizontalAlignment.Left };
        fieldBox.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");

        var sliders = new StackPanel { Width = FieldWidth + 2, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        sliders.Children.Add(Ui.SliderRow("Hue", "", out _hue, out var hueText));
        sliders.Children.Add(Ui.SliderRow("Saturation", "", out _saturation, out var satText));
        sliders.Children.Add(Ui.SliderRow("Brightness", "", out _brightness, out var brightText));
        _hue.Maximum = 360;
        _hue.LargeChange = 6;
        _saturation.LargeChange = _brightness.LargeChange = 4;

        var (h, s, v) = ToHsv(start);
        _hue.Value = h;
        _saturation.Value = s * 100;
        _brightness.Value = Math.Max(20, v * 100);
        _hue.ValueChanged += (_, _) => { hueText.Text = (int)_hue.Value + "°"; if (!_syncing) Update(); };
        _saturation.ValueChanged += (_, _) => { satText.Text = (int)_saturation.Value + "%"; if (!_syncing) Update(); };
        _brightness.ValueChanged += (_, _) => { brightText.Text = (int)_brightness.Value + "%"; if (!_syncing) Update(); };
        hueText.Text = (int)_hue.Value + "°";
        satText.Text = (int)_saturation.Value + "%";
        brightText.Text = (int)_brightness.Value + "%";

        var left = new StackPanel();
        left.Children.Add(fieldBox);
        left.Children.Add(sliders);

        _swatch.SetResourceReference(Border.BorderBrushProperty, "FgBrush");
        _hex.Margin = new Thickness(0, 12, 0, 16);
        _use = Ui.PanelButton("Use this color");
        _use.Click += (_, _) => _picked(Hex());
        var typed = Ui.PanelButton("Type a color code…");
        typed.Margin = new Thickness(0, 8, 0, 0);
        typed.Click += async (_, _) =>
        {
            var text = await Host.PromptAsync("Color code", "Six hex digits, for example #FF8800 for orange.", Hex(), "Use color");
            if (text == null) return;
            if (Theme.TryParseColor(text, out var color)) _picked($"#{color.R:X2}{color.G:X2}{color.B:X2}");
            else await Host.AskAsync("That isn't a color code", "Use the form #RRGGBB, such as #FF8800.", "OK");
        };
        var right = new StackPanel { Margin = new Thickness(40, 0, 0, 0), Width = 260 };
        _swatch.HorizontalAlignment = HorizontalAlignment.Left;
        right.Children.Add(_swatch);
        right.Children.Add(_hex);
        right.Children.Add(_use);
        right.Children.Add(typed);

        var body = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(left);
        body.Children.Add(right);
        SetPage("Custom color", body, "Left / Right  Adjust      {Select}  Choose      {Back}  Back");
        Update();
    }

    public override void FocusDefault() => _hue.Focus();

    private Color Current() => FromHsv(_hue.Value, _saturation.Value / 100, _brightness.Value / 100);

    private string Hex()
    {
        var c = Current();
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void Update()
    {
        _swatch.Background = new SolidColorBrush(Current());
        _hex.Text = Hex();
        _dim.Opacity = 1 - _brightness.Value / 100;
        Canvas.SetLeft(_cursor, _hue.Value / 360 * FieldWidth - 11);
        Canvas.SetTop(_cursor, (1 - _saturation.Value / 100) * FieldHeight - 11);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x)
            : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }
}

/// <summary>
/// The notification center: a panel that slides in from the right edge over whatever is showing,
/// instead of a page of its own.
/// </summary>
public sealed class NotificationsScreen : Screen
{
    /// <summary>The game a notification is about: saved with it, or (for older ones) matched by name.</summary>
    private Game? GameFor(NotificationEntry entry)
    {
        if (entry.Kind is "friend" or "battery" or "windows" or "info") return null;
        var games = Host.AllGames.Concat(Host.NotInstalledGames);
        if (entry.GameKey.Length > 0) return games.FirstOrDefault(g => g.Key == entry.GameKey);
        string name = entry.Kind == "achievement" ? entry.Text : entry.Title;
        return games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task LoadPictureAsync(Border target, UIElement placeholder, string url)
    {
        try
        {
            var source = await Task.Run(() => ImageLoader.FromUrlAsync(url, 96));
            if (source == null) return;
            target.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
            placeholder.Visibility = Visibility.Hidden;
        }
        catch (Exception) { }
    }

    private const double BladeWidth = 480;

    private readonly StackPanel _rows = new();
    private Button _close = null!;
    private readonly TranslateTransform _slide = new(BladeWidth, 0);
    private readonly Grid _backdrop = new();
    private bool _closing;

    public override bool IsDialog => true;

    public NotificationsScreen()
    {
        var title = Ui.Text("Notifications", 28);
        var close = Ui.PanelButton("");
        close.FontFamily = Ui.IconFont;
        close.Focusable = true; // reachable with the controller: Up from the first notification, or Right
        close.Click += (_, _) => Dismiss();
        _close = close;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _rows
        };
        var hints = new HintBar("{Select}  Open      {Details}  Clear all      {Back}  Close")
        {
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hints, Dock.Bottom);
        panel.Children.Add(header);
        panel.Children.Add(hints);
        panel.Children.Add(scroll);

        var blade = new Border
        {
            Width = BladeWidth, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(24, 30, 18, 22),
            BorderThickness = new Thickness(3, 0, 0, 0), Child = panel, RenderTransform = _slide,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 34, ShadowDepth = 0, Opacity = 0.55 }
        };
        blade.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        blade.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        KeyboardNavigation.SetDirectionalNavigation(blade, KeyboardNavigationMode.Contained);

        // The rest of the screen stays visible, slightly dimmed; clicking it closes the panel.
        _backdrop.Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        _backdrop.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, _backdrop)) Dismiss(); };
        _backdrop.Children.Add(blade);
        Content = _backdrop;
    }

    public override void OnShown()
    {
        Rebuild();
        Host.Notifications.MarkRead();
        Host.UpdateBell();
        if (Theme.ReduceMotion) { _slide.X = 0; return; }
        _slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(BladeWidth, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    public override void OnResumed() => FocusDefault();

    public override void FocusDefault()
    {
        if (_rows.Children.OfType<Button>().FirstOrDefault() is { } first) first.Focus();
        else _close.Focus(); // nothing to list: the close button
    }

    /// <summary>Slides the panel back out, then closes it.</summary>
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        if (Theme.ReduceMotion) { Host.Pop(this); return; }
        var away = new DoubleAnimation(BladeWidth, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        away.Completed += (_, _) => Host.Pop(this);
        _slide.BeginAnimation(TranslateTransform.XProperty, away);
        _backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)));
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Back: Dismiss(); return true;
            case NavAction.X: Clear(); return true;
            case NavAction.Up when _rows.Children.OfType<Button>().FirstOrDefault() is { } first && first.IsKeyboardFocusWithin:
                _close.Focus(); // above the first notification: the close button
                return true;
            case NavAction.Up when _close.IsKeyboardFocused:
                return true;
            case NavAction.Down when _close.IsKeyboardFocused:
                if (_rows.Children.OfType<Button>().FirstOrDefault() is { } top) { top.Focus(); top.BringIntoView(); }
                return true;
            case NavAction.Right:
                _close.Focus(); // the close button sits to the right of everything
                return true;
            case NavAction.Left when _close.IsKeyboardFocused:
                if (_rows.Children.OfType<Button>().Any()) FocusDefault();
                return true;
            case NavAction.Left: return true; // one column: nothing to the left
        }
        return false;
    }

    private void Clear()
    {
        Host.Notifications.Clear();
        Host.UpdateBell();
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var items = Host.Notifications.Items;
        if (items.Count == 0)
        {
            var empty = Ui.Text("Nothing yet. Achievements, session summaries, friends coming online, finished updates and " +
                                "low-battery warnings are collected here.", 17, "SubtleBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            _rows.Children.Add(empty);
            return;
        }

        foreach (var day in items.Take(150).GroupBy(n => n.When.Date))
        {
            _rows.Children.Add(Ui.Header(day.Key == DateTime.Today ? "Today"
                : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("MMMM d")));
            foreach (var n in day)
            {
                var entry = n;
                string glyph = entry.Kind switch
                {
                    "achievement" => "", "friend" => "", "download" => "",
                    "battery" => "", "session" => "", _ => ""
                };
                if (entry.Kind == "windows") glyph = "\uEA8F"; // a bell (the old symbol isn't in the icon font, so it drew a square)
                var mark = Ui.Glyph(glyph, 20, "AccentBrush");
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.Margin = new Thickness(0, 3, 0, 0);
                var icon = new Grid { Width = 36, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
                icon.Children.Add(mark);
                // Notifications about a game show that game's cover or icon; an achievement's own icon sits in its corner.
                var game = GameFor(entry);
                if (game?.Cover != null)
                {
                    icon.Width = 44;
                    var face = new Border
                    {
                        Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 0, 0),
                        Background = game.UsesIcon ? (game.TileBackground ?? (Brush?)TryFindResource("CircleBrush")) : new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill }
                    };
                    if (game.UsesIcon)
                        face.Child = new Image { Source = game.Cover, Margin = new Thickness(game.IconFills ? 0 : 8), Stretch = game.IconFills ? Stretch.UniformToFill : Stretch.Uniform };
                    icon.Children.Add(face);
                    mark.Visibility = Visibility.Hidden;
                    if (entry.Kind == "achievement" && entry.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        var badge = new Border
                        {
                            Width = 22, Height = 22, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -6), BorderThickness = new Thickness(2)
                        };
                        badge.SetResourceReference(Border.BorderBrushProperty, "BgBrush");
                        icon.Children.Add(badge);
                        _ = LoadPictureAsync(badge, new Grid(), entry.Icon);
                    }
                }
                else if (entry.Icon.Length > 0 && !entry.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                         System.IO.File.Exists(entry.Icon) && ImageLoader.FromFile(entry.Icon, 96) is { } appIcon)
                {
                    // The app that sent a Windows notification, by its own icon.
                    icon.Children.Add(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 2, 0, 0),
                                                   Background = new ImageBrush(appIcon) { Stretch = Stretch.Uniform } });
                    mark.Visibility = Visibility.Hidden;
                }
                else if (entry.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // A friend's picture or an achievement's icon takes the place of the symbol once it has loaded.
                    var picture = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(entry.Kind == "friend" ? 18 : 4), Margin = new Thickness(0, 2, 0, 0) };
                    icon.Children.Add(picture);
                    _ = LoadPictureAsync(picture, mark, entry.Icon);
                }

                var text = new StackPanel();
                var title = Ui.Text(entry.Title, 17, "FgBrush", bold: true);
                title.TextWrapping = TextWrapping.Wrap;
                title.TextTrimming = TextTrimming.None;
                text.Children.Add(title);
                if (entry.Text.Length > 0)
                {
                    var detail = Ui.Text(entry.Text, 15, "SubtleBrush");
                    detail.TextWrapping = TextWrapping.Wrap;
                    detail.MaxHeight = 60;
                    text.Children.Add(detail);
                }
                text.Children.Add(Ui.Text(entry.When.ToString("h:mm tt"), 12, "SubtleBrush"));

                var content = new DockPanel();
                DockPanel.SetDock(icon, Dock.Left);
                content.Children.Add(icon);
                content.Children.Add(text);

                var row = new Button { Style = Ui.StyleOf("RowButton"), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                if (entry.Kind == "drive") row.Click += (_, _) => Host.OpenNotification("drive", entry.Title, entry.Text, null); // opens the drive
                else row.Click += (_, _) => _ = Host.AskAsync(entry.Title, entry.Text + "\n\n" + entry.When.ToString("f"), "OK");
                _rows.Children.Add(row);
            }
        }

        var clear = Ui.Row("Clear all notifications", glyph: "");
        clear.Margin = new Thickness(0, 14, 0, 3);
        clear.Click += (_, _) => Clear();
        _rows.Children.Add(clear);
        FocusLater(_rows.Children.OfType<Button>().FirstOrDefault());
    }
}

/// <summary>
/// Shown from pressing Play until the game is running: the cover large in the middle of the screen, the game's
/// artwork softly blurred behind it, and "Starting". B hides it early; it gives up after a minute and a half.
/// </summary>
public sealed class LaunchSplashScreen : Screen
{
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(90);

    /// <summary>The animations to choose from (Settings > Gaming > Splash animation).</summary>
    public static readonly (string Id, string Name, string About)[] Styles =
    {
        ("classic", "Classic", "The cover pops in and breathes"),
        ("spotlight", "Spotlight", "Light rays turn behind the cover as it zooms in"),
        ("flip", "Flip", "The cover flips round like a card, then shines"),
        ("ripples", "Ripples", "Rings of light ripple out from the cover"),
        ("ring", "Loading ring", "A ring of light chases round the cover"),
        ("cinema", "Cinematic", "Letterbox bars close in as the cover glides into place"),
        ("cartridge", "Cartridge", "Retro: the cover slots in like a game cartridge, the power light blinks on, and the name types itself out"),
        ("crt", "CRT power-on", "Retro: an old TV switches on, from a bright line to a flickering picture with scanlines"),
    };

    /// <summary>A few seconds of the animation with a stand-in game, from Settings.</summary>
    public static LaunchSplashScreen Preview(string style) =>
        new(new Game { Name = "Your game" }, style, preview: true);

    private readonly string _style;
    private readonly bool _preview;

    private readonly Game _game;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DateTime _opened = DateTime.UtcNow;
    private readonly Image _backdrop = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly TextBlock _status = Ui.Text("Starting", 20, "SubtleBrush");
    private readonly CancellationTokenSource _cts = new();
    private int _dots;
    private bool _closing;

    public LaunchSplashScreen(Game game, string? style = null, bool preview = false)
    {
        _game = game;
        _preview = preview;
        style ??= (Application.Current.MainWindow as MainWindow)?.Settings.LaunchAnimation ?? "classic";
        _style = style == "random" || Styles.All(x => x.Id != style) ? Styles[Random.Shared.Next(Styles.Length)].Id : style;
        var root = new Grid { ClipToBounds = true };
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");

        // Behind everything: the game's artwork (or cover), blurred, with the page color washed over it.
        _backdrop.Effect = new BlurEffect { Radius = 40, RenderingBias = RenderingBias.Performance };
        _backdrop.Margin = new Thickness(-80);
        root.Children.Add(_backdrop);
        var bg = (Application.Current.Resources["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
        var wash = new RadialGradientBrush { RadiusX = 0.8, RadiusY = 0.8 };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, bg.R, bg.G, bg.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0xEE, bg.R, bg.G, bg.B), 1));
        root.Children.Add(new Border { Background = wash });

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var breathe = new ScaleTransform(1, 1);
        var cover = new Border
        {
            Width = 300, Height = 300, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = breathe,
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 10, Opacity = 0.55 }
        };
        if (game.Cover != null)
        {
            if (game.UsesIcon && !game.IconFills)
            {
                cover.Background = game.TileBackground ?? (Brush?)TryFindResource("TileBrush");
                cover.Child = new Image { Source = game.Cover, Margin = new Thickness(70), Stretch = Stretch.Uniform };
            }
            else cover.Background = new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill };
            _backdrop.Source = game.Cover;
            _backdrop.Opacity = 0.6;
        }
        else
        {
            cover.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            cover.Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 90, HorizontalAlignment = HorizontalAlignment.Center,
                                          VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
        }
        // The cover sits on a stage with room around it for the style's light (rays, rings, the loading ring).
        var stage = new Grid { Width = 300, Height = 300, HorizontalAlignment = HorizontalAlignment.Center };
        var fx = new Grid { Width = 300, Height = 300, IsHitTestVisible = false };
        stage.Children.Add(fx);
        stage.Children.Add(cover);
        center.Children.Add(stage);

        var name = Ui.Text(game.Name, 36, "FgBrush", bold: true);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.TextAlignment = TextAlignment.Center;
        name.MaxWidth = 900;
        name.Margin = new Thickness(0, 34, 0, 8);
        center.Children.Add(name);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(_status);
        root.Children.Add(center);

        var hint = new HintBar("{Back}  Hide");
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.VerticalAlignment = VerticalAlignment.Bottom;
        hint.Margin = new Thickness(0, 0, 40, 24);
        root.Children.Add(hint);
        Content = root;

        if (!Theme.ReduceMotion) Animate(root, fx, cover, breathe, name);
        if (Labs.On("companion") && !Theme.ReduceMotion) AddCompanion(root, cover);

        _timer.Tick += (_, _) => Tick();
    }

    // ---------------------------------------------------------------- the companion's act

    /// <summary>The companion's acts while a game starts (with the Companion experiment on): one is picked each time.</summary>
    public static readonly (string Id, string Name)[] CompanionActs =
    {
        ("hop", "Hops onto the cover and bounces on it"), ("run", "Runs back and forth under the cover, kicking up dust"),
        ("rocket", "Blasts off from below and hovers beside the cover"), ("peek", "Peeks out from behind the cover and waves"),
        ("drumroll", "A drumroll, then a jump for joy with stars"), ("orbit", "Flies round and round the cover"),
        ("cheer", "Cheers with confetti, arms in the air"), ("nap", "Dozes off, wakes with a start, then cheers"),
        ("dance", "Dances beside the cover with music notes"), ("parachute", "Floats down on a parachute and lands"),
        ("skate", "Skates in from the side, does a trick and stops"), ("juggle", "Juggles little stars while it waits"),
    };

    private DispatcherTimer? _pal;

    /// <summary>The companion in the launch animation: on a layer over everything, placed around the cover once it's laid out.</summary>
    private void AddCompanion(Grid root, Border cover)
    {
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
        root.Children.Add(layer);
        // In front of the cover (6) or, for peeking and the far side of an orbit, just behind it (4).
        foreach (var panel in root.Children.OfType<StackPanel>()) Panel.SetZIndex(panel, 5);
        Panel.SetZIndex(layer, 6);
        const double size = 96;
        var sprite = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 1) };
        var scale = new ScaleTransform(1, 1);
        var turn = new RotateTransform();
        var move = new TranslateTransform();
        sprite.RenderTransform = new TransformGroup { Children = { scale, turn, move } };
        layer.Children.Add(sprite);
        string act = CompanionActs[Random.Shared.Next(CompanionActs.Length)].Id;
        string mood = "happy";
        int look = 0;
        bool wave = false, blink = false;
        int frame = 0;
        void Redraw() => CompanionArt.Draw(sprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, blink, Labs.Outfit, look, wave, size / 12);
        Redraw();
        sprite.Opacity = 0;

        // A little text symbol that flies off (stars, notes, dust, confetti).
        void Puff(string symbol, string hex, double x, double y, double dx, double dy, double seconds, double sizePx = 18, double delay = 0)
        {
            var t = new TextBlock { Text = symbol, FontSize = sizePx, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            layer.Children.Add(t);
            var span = TimeSpan.FromSeconds(seconds);
            var begin = TimeSpan.FromSeconds(delay);
            t.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + dx, span) { BeginTime = begin });
            t.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, y + dy, span) { BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimation(1, 0, span) { BeginTime = begin };
            fade.Completed += (_, _) => layer.Children.Remove(t);
            t.BeginAnimation(OpacityProperty, fade);
        }
        DoubleAnimation A(double from, double to, double seconds, bool back = false, RepeatBehavior? repeat = null, IEasingFunction? ease = null, double delay = 0) =>
            new(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = back, RepeatBehavior = repeat ?? new RepeatBehavior(1), EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(delay) };
        var forever = RepeatBehavior.Forever;
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        var outEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // Frames: blinking, waving and act-specific moods, five times a second.
        _pal = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        double elapsed = 0;
        _pal.Tick += (_, _) =>
        {
            frame++;
            elapsed += 0.2;
            blink = mood is "happy" or "dance" && frame % 17 == 0;
            if (act is "peek" or "rocket" or "parachute" or "skate") wave = elapsed > 1.6 && frame % 2 == 0;
            if (act == "nap") mood = elapsed < 2.2 ? "sleepy" : elapsed < 2.8 ? "worried" : "cheer";
            if (act == "drumroll") mood = elapsed < 1.6 ? "happy" : "cheer";
            Redraw();
        };
        _pal.Start();

        layer.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // Measured on the cover's stage, which doesn't move (the cover itself may be mid-animation).
            var stage = cover.Parent as FrameworkElement ?? cover;
            var topLeft = stage.TranslatePoint(new Point(0, 0), layer);
            double cx = topLeft.X, cy = topLeft.Y, cw = stage.ActualWidth, ch = stage.ActualHeight;
            double right = cx + cw + 28, ground = cy + ch - size, left = cx - size - 28;
            void Place(double x, double y) { Canvas.SetLeft(sprite, x); Canvas.SetTop(sprite, y); }
            sprite.BeginAnimation(OpacityProperty, A(0, 1, 0.3));
            switch (act)
            {
                case "hop":
                {
                    // From the floor beside the cover, up in an arc onto its top edge, then bouncing there.
                    Place(right, ground);
                    mood = "cheer";
                    var x = new DoubleAnimation(right, cx + cw / 2 - size / 2, TimeSpan.FromSeconds(0.7)) { BeginTime = TimeSpan.FromSeconds(0.4), EasingFunction = sine };
                    var y = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(0.4) };
                    y.KeyFrames.Add(new EasingDoubleKeyFrame(cy - size - 120, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.38)), outEase));
                    y.KeyFrames.Add(new EasingDoubleKeyFrame(cy - size + 6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                    sprite.BeginAnimation(Canvas.LeftProperty, x);
                    sprite.BeginAnimation(Canvas.TopProperty, y);
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -26, 0.28, true, forever, outEase, 1.2));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.85, 1, 0.14, false, forever, null, 1.2));
                    break;
                }
                case "run":
                {
                    Place(cx - size, cy + ch + 70);
                    var x = new DoubleAnimationUsingKeyFrames { RepeatBehavior = forever };
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(cx + cw, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6)), sine));
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(cx - size, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2)), sine));
                    sprite.BeginAnimation(Canvas.LeftProperty, x);
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -12, 0.13, true, forever));
                    var dust = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                    dust.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { dust.Stop(); return; }
                        double sx = Canvas.GetLeft(sprite);
                        look = (frame / 8) % 2 == 0 ? 1 : -1;
                        Puff("•", "#AAB4C8", sx + size / 2 - look * 30, cy + ch + 70 + size - 14, -look * 20, -14, 0.6, 14);
                    };
                    dust.Start();
                    break;
                }
                case "rocket":
                {
                    Place(right, cy + ch + 200);
                    sprite.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(cy + ch + 200, cy + ch / 2 - size / 2, TimeSpan.FromSeconds(1.2)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -10, 0.9, true, forever, sine, 1.3));
                    var flame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                    flame.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { flame.Stop(); return; }
                        double sx = Canvas.GetLeft(sprite), sy = Canvas.GetTop(sprite) + move.Y;
                        Puff(Random.Shared.Next(2) == 0 ? "▲" : "●", Random.Shared.Next(2) == 0 ? "#FFB347" : "#FF5A3C", sx + size / 2 - 8 + Random.Shared.Next(-10, 10), sy + size - 6, Random.Shared.Next(-12, 12), 40, 0.5, 14);
                    };
                    flame.Start();
                    mood = "cheer";
                    break;
                }
                case "peek":
                {
                    // Behind the cover's left edge: slides out, waves, ducks back, and again.
                    Panel.SetZIndex(layer, 0);
                    Place(cx + 10, cy + ch / 2 - size / 2);
                    var x = new DoubleAnimationUsingKeyFrames { RepeatBehavior = forever };
                    x.KeyFrames.Add(new LinearDoubleKeyFrame(cx + 10, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(cx - size * 0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)), new BackEase { EasingMode = EasingMode.EaseOut }));
                    x.KeyFrames.Add(new LinearDoubleKeyFrame(cx - size * 0.8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(cx + 10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6)), sine));
                    x.KeyFrames.Add(new LinearDoubleKeyFrame(cx + 10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
                    sprite.BeginAnimation(Canvas.LeftProperty, x);
                    look = -1;
                    Panel.SetZIndex(layer, 4); // behind the cover
                    break;
                }
                case "drumroll":
                {
                    Place(right, ground);
                    move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(50)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(1.6)) });
                    var jump = new DoubleAnimation(0, -70, TimeSpan.FromSeconds(0.35)) { AutoReverse = true, BeginTime = TimeSpan.FromSeconds(1.7), EasingFunction = outEase };
                    jump.Completed += (_, _) => move.BeginAnimation(TranslateTransform.YProperty, A(0, -14, 0.3, true, forever, outEase));
                    move.BeginAnimation(TranslateTransform.YProperty, jump);
                    for (int i = 0; i < 10; i++) Puff("★", "#FFD84D", right + size / 2, ground + 10, Random.Shared.Next(-120, 120), Random.Shared.Next(-140, -40), 1.1, 20, 1.75);
                    break;
                }
                case "orbit":
                {
                    // An ellipse round the cover; it faces the way it flies and goes behind the cover on the far side.
                    double ox = cx + cw / 2 - size / 2, oy = cy + ch / 2 - size / 2, rx = cw / 2 + 90, ry = 60;
                    Place(ox, oy);
                    var spin = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                    var start = DateTime.UtcNow;
                    spin.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { spin.Stop(); return; }
                        double a = (DateTime.UtcNow - start).TotalSeconds / 3.6 * Math.PI * 2;
                        Place(ox + rx * Math.Cos(a), oy + ry * Math.Sin(a) + 40);
                        look = Math.Sin(a) > 0 ? 1 : -1;
                        double depth = 0.75 + 0.25 * Math.Sin(a);
                        scale.ScaleX = scale.ScaleY = depth;
                        Panel.SetZIndex(layer, Math.Sin(a) < 0 ? 4 : 6);
                    };
                    spin.Start();
                    mood = "cheer";
                    break;
                }
                case "cheer":
                {
                    Place(left, ground);
                    mood = "cheer";
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -40, 0.3, true, forever, outEase));
                    var confetti = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                    string[] colors = { "#FF6B8A", "#FFD84D", "#7FD8BE", "#7FB8FF", "#C3A6FF" };
                    confetti.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { confetti.Stop(); return; }
                        Puff("■", colors[Random.Shared.Next(colors.Length)], left + size / 2 + Random.Shared.Next(-40, 40), ground - 20, Random.Shared.Next(-60, 60), Random.Shared.Next(-120, -40), 1.2, 10);
                    };
                    confetti.Start();
                    break;
                }
                case "nap":
                {
                    Place(right, ground);
                    turn.Angle = 0;
                    var z = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    z.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { z.Stop(); return; }
                        if (elapsed < 2.2) Puff("z", "#AAB4C8", right + size - 10, ground, 20, -50, 1.4, 16);
                        else if (elapsed < 2.8) Puff("!", "#FF6B6B", right + size / 2, ground - 30, 0, -20, 0.6, 28);
                    };
                    z.Start();
                    var wake = new DoubleAnimation(0, -50, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, BeginTime = TimeSpan.FromSeconds(2.3), EasingFunction = outEase };
                    wake.Completed += (_, _) => move.BeginAnimation(TranslateTransform.YProperty, A(0, -24, 0.3, true, forever, outEase));
                    move.BeginAnimation(TranslateTransform.YProperty, wake);
                    break;
                }
                case "dance":
                {
                    Place(right, ground);
                    mood = "dance";
                    turn.BeginAnimation(RotateTransform.AngleProperty, A(-14, 14, 0.38, true, forever, sine));
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -10, 0.19, true, forever));
                    var notes = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                    notes.Tick += (_, _) =>
                    {
                        if (!IsLoaded) { notes.Stop(); return; }
                        Puff(Random.Shared.Next(2) == 0 ? "♪" : "♫", "#C3A6FF", right + Random.Shared.Next(0, (int)size), ground, Random.Shared.Next(-30, 30), -80, 1.6, 22);
                    };
                    notes.Start();
                    break;
                }
                case "parachute":
                {
                    // A little canopy over it while it drifts down, swaying, then folds away on landing.
                    Place(right, -size - 120);
                    var chute = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M 0,40 A 48,40 0 0 1 96,40 L 82,40 L 48,96 L 14,40 Z"), Width = 96, Height = 100,
                        Fill = new SolidColorBrush(Color.FromArgb(0xEE, Accent.R, Accent.G, Accent.B)), Stroke = Brushes.White, StrokeThickness = 2
                    };
                    var chuteHolder = new Canvas { Width = size, Height = 100 };
                    chuteHolder.Children.Add(chute);
                    Canvas.SetTop(chuteHolder, -size * 0.9);
                    sprite.Children.Add(chuteHolder);
                    var fall = new DoubleAnimation(-size - 120, ground, TimeSpan.FromSeconds(2.4)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
                    fall.Completed += (_, _) =>
                    {
                        chuteHolder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
                        turn.BeginAnimation(RotateTransform.AngleProperty, null);
                        mood = "cheer";
                        move.BeginAnimation(TranslateTransform.YProperty, A(0, -18, 0.3, true, forever, outEase, 0.5));
                    };
                    sprite.BeginAnimation(Canvas.TopProperty, fall);
                    turn.BeginAnimation(RotateTransform.AngleProperty, A(-10, 10, 0.8, true, new RepeatBehavior(3), sine));
                    break;
                }
                case "skate":
                {
                    Place(-size * 2, ground + 40);
                    var x = new DoubleAnimation(-size * 2, right, TimeSpan.FromSeconds(1.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    x.Completed += (_, _) =>
                    {
                        // The trick: a jump with a full spin.
                        move.BeginAnimation(TranslateTransform.YProperty, A(0, -80, 0.4, true, null, outEase));
                        turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)));
                        for (int i = 0; i < 6; i++) Puff("✦", "#FFFFFF", right + size / 2, ground + 20, Random.Shared.Next(-80, 80), Random.Shared.Next(-100, -30), 0.9, 16, 0.3);
                    };
                    sprite.BeginAnimation(Canvas.LeftProperty, x);
                    look = 1;
                    var board = new Border { Width = size * 0.9, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x3C)) };
                    Canvas.SetLeft(board, size * 0.05); Canvas.SetTop(board, size - 4);
                    sprite.Children.Add(board);
                    break;
                }
                default: // juggle
                {
                    Place(right, ground);
                    mood = "dance";
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, -4, 0.3, true, forever));
                    for (int i = 0; i < 3; i++)
                    {
                        var star = new TextBlock { Text = "★", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(new[] { "#FFD84D", "#FF6B8A", "#7FB8FF" }[i])) };
                        layer.Children.Add(star);
                        var begin = TimeSpan.FromSeconds(i * 0.35);
                        var sx = new DoubleAnimationUsingKeyFrames { BeginTime = begin, RepeatBehavior = forever };
                        sx.KeyFrames.Add(new LinearDoubleKeyFrame(right + 10, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        sx.KeyFrames.Add(new LinearDoubleKeyFrame(right + size - 20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.05))));
                        var sy = new DoubleAnimationUsingKeyFrames { BeginTime = begin, RepeatBehavior = forever };
                        sy.KeyFrames.Add(new LinearDoubleKeyFrame(ground + 30, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        sy.KeyFrames.Add(new EasingDoubleKeyFrame(ground - 90, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.52)), outEase));
                        sy.KeyFrames.Add(new EasingDoubleKeyFrame(ground + 30, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.05)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                        star.BeginAnimation(Canvas.LeftProperty, sx);
                        star.BeginAnimation(Canvas.TopProperty, sy);
                    }
                    break;
                }
            }
        });
    }

    // ---------------------------------------------------------------- the animations

    private static Color Accent => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    private static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(ms));

    private static void Both(ScaleTransform scale, AnimationTimeline animation)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>The gentle breathing the cover does while the game loads, after it has arrived.</summary>
    private static void Breathe(ScaleTransform scale, double from = 1, double to = 1.035, double seconds = 1.6) =>
        Both(scale, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });

    private void Animate(Grid root, Grid fx, Border cover, ScaleTransform scale, TextBlock name)
    {
        var accent = Accent;
        switch (_style)
        {
            case "spotlight":
            {
                // Twelve soft rays of the highlight color turning slowly behind the cover, which zooms in from far away.
                var rays = new System.Windows.Shapes.Path { Width = 1000, Height = 1000, Stretch = Stretch.None, Opacity = 0, IsHitTestVisible = false };
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                    for (int i = 0; i < 12; i++)
                    {
                        double a1 = i * Math.PI / 6, a2 = a1 + Math.PI / 18;
                        g.BeginFigure(new Point(500, 500), true, true);
                        g.LineTo(new Point(500 + 500 * Math.Cos(a1), 500 + 500 * Math.Sin(a1)), false, false);
                        g.LineTo(new Point(500 + 500 * Math.Cos(a2), 500 + 500 * Math.Sin(a2)), false, false);
                    }
                geometry.Freeze();
                rays.Data = geometry;
                var fill = new RadialGradientBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
                rays.Fill = fill;
                var spin = new RotateTransform(0, 500, 500);
                rays.RenderTransform = spin;
                rays.Margin = new Thickness(-350);
                fx.Children.Add(rays);
                spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14)) { RepeatBehavior = RepeatBehavior.Forever });
                rays.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(700)) { BeginTime = TimeSpan.FromMilliseconds(250) });
                var zoom = new DoubleAnimation(0.15, 1, Ms(650)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
                zoom.Completed += (_, _) => Breathe(scale);
                Both(scale, zoom);
                cover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
                break;
            }
            case "flip":
            {
                // Turns round like a card (edge-on first), then a band of light sweeps across it now and then.
                var flip = new DoubleAnimationUsingKeyFrames { Duration = Ms(700) };
                flip.KeyFrames.Add(new EasingDoubleKeyFrame(0.02, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                flip.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new SineEase { EasingMode = EasingMode.EaseOut }));
                flip.KeyFrames.Add(new EasingDoubleKeyFrame(0.02, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new SineEase { EasingMode = EasingMode.EaseIn }));
                flip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
                flip.Completed += (_, _) => Breathe(scale);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, flip);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, Ms(700)));
                var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.35));
                band.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0.5));
                band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.65));
                var move = new TranslateTransform();
                band.RelativeTransform = move;
                var shine = new Border { CornerRadius = cover.CornerRadius, Background = band, IsHitTestVisible = false };
                var coverGrid = new Grid();
                if (cover.Child is UIElement inside) { cover.Child = null; coverGrid.Children.Add(inside); }
                coverGrid.Children.Add(shine);
                cover.Child = coverGrid;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1.2, 1.2, TimeSpan.FromSeconds(2.4))
                {
                    BeginTime = TimeSpan.FromMilliseconds(700), RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                break;
            }
            case "ripples":
            {
                // Four rings of the highlight color ripple out from behind the cover, one after another, forever.
                for (int i = 0; i < 4; i++)
                {
                    var ring = new System.Windows.Shapes.Ellipse
                    {
                        Width = 300, Height = 300, StrokeThickness = 5, Stroke = new SolidColorBrush(accent), Opacity = 0,
                        RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false
                    };
                    var grow = new ScaleTransform(1, 1);
                    ring.RenderTransform = grow;
                    fx.Children.Add(ring);
                    var begin = TimeSpan.FromMilliseconds(300 + i * 650);
                    var spread = new DoubleAnimation(1, 2.8, TimeSpan.FromSeconds(2.6)) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    Both(grow, spread);
                    ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0, TimeSpan.FromSeconds(2.6)) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever });
                }
                var pop = new DoubleAnimation(0.8, 1, Ms(480)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut } };
                pop.Completed += (_, _) => Breathe(scale, 1, 1.025, 1.3);
                Both(scale, pop);
                break;
            }
            case "ring":
            {
                // A faint track around the cover and a bright arc chasing round it, like a loading ring.
                double size = 372, thickness = 6;
                var track = new System.Windows.Shapes.Ellipse { Width = size, Height = size, StrokeThickness = thickness, Stroke = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), Margin = new Thickness(-36), IsHitTestVisible = false };
                double circle = Math.PI * (size - thickness) / thickness; // in stroke widths, how dashes are measured
                var arc = new System.Windows.Shapes.Ellipse
                {
                    Width = size, Height = size, StrokeThickness = thickness, Stroke = new SolidColorBrush(accent), Margin = new Thickness(-36),
                    StrokeDashArray = new DoubleCollection { circle * 0.22, circle * 0.78 }, StrokeDashCap = PenLineCap.Round,
                    RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false,
                    Effect = Perf.Active ? null : new DropShadowEffect { Color = accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 }
                };
                var turn = new RotateTransform();
                arc.RenderTransform = turn;
                fx.Children.Add(track);
                fx.Children.Add(arc);
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
                // The arc stretches and shrinks as it goes round.
                arc.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -circle, TimeSpan.FromSeconds(3.6)) { RepeatBehavior = RepeatBehavior.Forever });
                track.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(400)));
                var pop = new DoubleAnimation(0.85, 1, Ms(420)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
                pop.Completed += (_, _) => Breathe(scale, 1, 1.02, 1.8);
                Both(scale, pop);
                break;
            }
            case "cinema":
            {
                // Letterbox bars close in from the top and bottom, a streak of light crosses the screen, and the cover
                // glides in from the left; then it pushes in slowly, like a film's opening shot.
                var top = new Border { Background = Brushes.Black, Height = 0, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                var bottom = new Border { Background = Brushes.Black, Height = 0, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
                root.Children.Add(top);
                root.Children.Add(bottom);
                var bars = new DoubleAnimation(0, 92, Ms(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                top.BeginAnimation(HeightProperty, bars);
                bottom.BeginAnimation(HeightProperty, bars);
                var streakBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0));
                streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 255, 255, 255), 0.5));
                streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
                var streakMove = new TranslateTransform(-1400, 0);
                var streak = new Border { Height = 3, Width = 900, Background = streakBrush, IsHitTestVisible = false, RenderTransform = streakMove };
                root.Children.Add(streak);
                streakMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1400, 1400, Ms(900)) { BeginTime = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                var slide = new TranslateTransform(-420, 0);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(slide);
                cover.RenderTransform = group;
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-420, 0, Ms(800)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                cover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(500)));
                var nameMove = new TranslateTransform(0, 24);
                name.RenderTransform = nameMove;
                nameMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, Ms(700)) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                name.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(600)) { BeginTime = TimeSpan.FromMilliseconds(350) });
                Both(scale, new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(9)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
                break;
            }
            case "cartridge":
            {
                // A console slot at the bottom of the stage; the cover drops into it with a clunk, the power light goes
                // from red to green, and the name types itself out in blocky letters over faint scanlines.
                var slot = new Border { Width = 320, Height = 34, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -26),
                    Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2D, 0x34), Color.FromRgb(0x14, 0x15, 0x19), 90), IsHitTestVisible = false };
                var mouth = new Border { Width = 270, Height = 8, CornerRadius = new CornerRadius(3), Background = Brushes.Black, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
                var led = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 6) };
                var slotGrid = new Grid();
                slotGrid.Children.Add(mouth);
                slotGrid.Children.Add(led);
                slot.Child = slotGrid;
                fx.Children.Add(slot);
                var drop = new TranslateTransform(0, -520);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(drop);
                cover.RenderTransform = group;
                var fall = new DoubleAnimationUsingKeyFrames();
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(-520, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640))));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(780))));
                drop.BeginAnimation(TranslateTransform.YProperty, fall);
                var power = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                int blinks = 0;
                power.Tick += (_, _) =>
                {
                    power.Interval = TimeSpan.FromMilliseconds(140);
                    led.Fill = blinks++ % 2 == 0 ? Brushes.LimeGreen : Brushes.DarkGreen;
                    if (blinks >= 5) { led.Fill = Brushes.LimeGreen; power.Stop(); }
                };
                power.Start();
                // The name types out, one letter at a time, with a blinking block cursor.
                string full = name.Text;
                name.FontFamily = new FontFamily("Consolas");
                name.Text = "";
                int shown = 0;
                var type = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
                var startTyping = DateTime.UtcNow.AddMilliseconds(900);
                type.Tick += (_, _) =>
                {
                    if (DateTime.UtcNow < startTyping) return;
                    shown = Math.Min(full.Length, shown + 1);
                    bool cursor = (DateTime.UtcNow.Millisecond / 300) % 2 == 0;
                    name.Text = full[..shown] + (shown < full.Length || cursor ? "█" : " ");
                    if (shown >= full.Length && DateTime.UtcNow > startTyping.AddSeconds(4)) { name.Text = full; type.Stop(); }
                };
                type.Start();
                root.Children.Add(Scanlines(0.10));
                break;
            }
            case "crt":
            {
                // An old TV switching on: a bright line in the middle opens up into the picture with a flash, then a
                // gentle flicker and scanlines while the game loads.
                var tv = new ScaleTransform(1, 0.004, 640, 360);
                foreach (var child in root.Children.OfType<FrameworkElement>().ToList()) child.RenderTransform = tv; // what's there now: the picture
                var line = new Border { Height = 4, Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 } };
                root.Children.Add(line);
                var open = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(260) };
                open.KeyFrames.Add(new EasingDoubleKeyFrame(0.004, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                open.KeyFrames.Add(new EasingDoubleKeyFrame(1.04, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                open.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
                tv.BeginAnimation(ScaleTransform.ScaleYProperty, open);
                var lineFade = new DoubleAnimation(1, 0, Ms(300)) { BeginTime = TimeSpan.FromMilliseconds(380) };
                line.BeginAnimation(OpacityProperty, lineFade);
                var flash = new Border { Background = Brushes.White, IsHitTestVisible = false, Opacity = 0 };
                root.Children.Add(flash);
                var flashKeys = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(460) };
                flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
                flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
                flash.BeginAnimation(OpacityProperty, flashKeys);
                var lines = Scanlines(0.16);
                root.Children.Add(lines);
                // A faint, uneven flicker, like an old tube.
                var flicker = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(900) };
                foreach (var (ms, o) in new[] { (0, 1.0), (90, 0.93), (160, 1.0), (1300, 1.0), (1350, 0.9), (1420, 1.0), (2600, 1.0) })
                    flicker.KeyFrames.Add(new LinearDoubleKeyFrame(o, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                cover.BeginAnimation(OpacityProperty, flicker);
                Breathe(scale, 1, 1.02, 2.2);
                break;
            }
            default:
            {
                // Classic: the cover pops in, then breathes gently while the game loads.
                var pop = new DoubleAnimation(0.85, 1, Ms(420)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
                pop.Completed += (_, _) => Breathe(scale);
                Both(scale, pop);
                break;
            }
        }
    }

    /// <summary>Faint horizontal lines over the screen, like an old TV.</summary>
    private static FrameworkElement Scanlines(double strength)
    {
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(strength * 255), 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 2)));
        var brush = new DrawingBrush(new DrawingGroup { Children = { new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 4))), drawing } })
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4), ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return new Border { Background = brush, IsHitTestVisible = false };
    }

    private bool _held;

    public override void OnShown()
    {
        _timer.Start();
        if (_preview) return; // just the animation: nothing to load, nothing to keep in front
        _ = LoadArtworkAsync();
        if (!_held) { _held = true; Host.HoldVisible(true); } // stays in front until the game is running
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _pal?.Stop();
        _cts.Cancel();
        if (_held) { _held = false; Host.HoldVisible(false); }
    }

    public override void FocusDefault() => Focus();

    /// <summary>Wide artwork for the background, when SteamGridDB has some (usually already downloaded).</summary>
    private async Task LoadArtworkAsync()
    {
        if (!_game.IsGame) return;
        var key = Host.Settings.SteamGridDbApiKey;
        var ct = _cts.Token;
        try
        {
            var art = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var path = await client.GetHeroAsync(_game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 1280);
            }, ct);
            if (art == null || ct.IsCancellationRequested) return;
            _backdrop.Source = art;
            _backdrop.Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance };
            _backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(500)));
        }
        catch (Exception) { }
    }

    private static readonly TimeSpan Buffer = TimeSpan.FromSeconds(3);
    private DateTime? _runningSince, _windowAt;
    private bool _looking;

    private void Tick()
    {
        _dots = (_dots + 1) % 4;
        _status.Text = "Starting" + new string('.', _dots);
        if (_closing) return;
        if (_preview) { if (DateTime.UtcNow - _opened > TimeSpan.FromSeconds(4.5)) Close(); return; }
        if (DateTime.UtcNow - _opened > GiveUpAfter) { Close(); return; } // it never showed up

        // Stays in front of everything, store windows included (Steam, EA app, Ubisoft Connect, Epic…), until the game's
        // own window appears. Until the game is running it also takes the focus back from them.
        bool running = _game.IsRunning;
        if (running) _runningSince ??= DateTime.UtcNow;
        Host.KeepInFront(takeFocus: !running);
        if (running && !_looking) _ = LookForWindowAsync();
    }

    /// <summary>Closes the splash once the game has a window of its own (or, where that can't be seen, soon after it starts).</summary>
    private async Task LookForWindowAsync()
    {
        _looking = true;
        try
        {
            string? exe = MainWindow.ExactProgram(_game);
            string? dir = exe == null && !string.IsNullOrEmpty(_game.InstallDir) && Directory.Exists(_game.InstallDir) ? _game.InstallDir : null;
            var since = DateTime.UtcNow - (_runningSince ?? DateTime.UtcNow);
            bool done;
            if (exe == null && dir == null) done = since > TimeSpan.FromSeconds(2); // Xbox / Store games: no folder to watch
            else done = await Task.Run(() => ProcessUtil.HasGameWindow(exe, dir)) || since > TimeSpan.FromSeconds(30);
            if (!done || _closing) return;
            // The game's window is up. It gets the focus (not its store), and the splash stays over it a few seconds
            // more as a buffer, hiding the black frames and logos of the first moments.
            if (_windowAt == null)
            {
                _windowAt = DateTime.UtcNow;
                if (exe != null || dir != null) _ = Task.Run(() => ProcessUtil.FocusWindow(exe, dir));
            }
            else if (DateTime.UtcNow - _windowAt > Buffer) Close();
        }
        catch (Exception) { }
        finally { _looking = false; }
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        // Out of the way at once, so the game's window comes in front while the splash fades.
        if (_held) { _held = false; Host.HoldVisible(false); }
        if (Theme.ReduceMotion) { Host.Pop(this); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
        fade.Completed += (_, _) => Host.Pop(this);
        BeginAnimation(OpacityProperty, fade);
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) Close();
        return true; // nothing else does anything while the game starts
    }
}
