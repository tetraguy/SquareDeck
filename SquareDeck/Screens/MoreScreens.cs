using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
/// A small web browser (Microsoft Edge WebView2) that works with a controller: the d-pad jumps between links,
/// A clicks, B goes back, the right stick scrolls, X opens the address box and Y the browser menu.
/// </summary>
public sealed class BrowserScreen : Screen
{
    public static readonly (string Id, string Name, string Url)[] Engines =
    {
        ("google", "Google", "https://www.google.com/search?q="),
        ("bing", "Bing", "https://www.bing.com/search?q="),
        ("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q=")
    };

    private readonly WebView2 _web = new();
    private readonly TextBlock _address = Ui.Text("", 16);
    private readonly TextBlock _status = Ui.Text("Starting the browser…", 20, "SubtleBrush");
    private readonly Button _reload;
    private readonly DockPanel _toolbar = new() { Margin = new Thickness(20, 12, 20, 10), LastChildFill = true };
    private readonly Grid _footer = new() { Margin = new Thickness(30, 0, 30, 0) };
    private readonly string? _startUrl;
    private bool _ready, _loading, _closed;
    private double _scrollCarry;

    public BrowserScreen(string? url = null)
    {
        _startUrl = url;

        Button Tool(string glyph, string tip, Action click)
        {
            var b = Ui.PanelButton(glyph);
            b.FontFamily = Ui.IconFont;
            b.FontSize = 18;
            b.Focusable = false; // mouse and touch; the controller has its own buttons for these
            b.ToolTip = tip;
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += (_, _) => click();
            return b;
        }

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Tool("", "Back", () => { if (_ready && _web.CanGoBack) _web.GoBack(); }));
        left.Children.Add(Tool("", "Forward", () => { if (_ready && _web.CanGoForward) _web.GoForward(); }));
        _reload = Tool("", "Reload", () => { if (!_ready) return; if (_loading) _web.Stop(); else _web.Reload(); });
        left.Children.Add(_reload);
        left.Children.Add(Tool("", "Home page", GoHome));
        DockPanel.SetDock(left, Dock.Left);
        _toolbar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Tool("", "Zoom out", () => Zoom(-10)));
        right.Children.Add(Tool("", "Zoom in", () => Zoom(+10)));
        right.Children.Add(Tool("", "Menu", () => _ = MenuAsync()));
        var close = Tool("", "Close the browser", () => Host.Pop(this));
        close.Margin = new Thickness(0);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        _toolbar.Children.Add(right);

        var addressBox = Ui.PanelButton("");
        addressBox.Focusable = false;
        addressBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        addressBox.Margin = new Thickness(4, 0, 10, 0);
        addressBox.Content = _address;
        addressBox.Click += (_, _) => _ = EnterAddressAsync();
        _toolbar.Children.Add(addressBox);

        var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        line.SetResourceReference(Shape.FillProperty, "FgBrush");
        _footer.Children.Add(line);
        _footer.Children.Add(new HintBar(
            "D-pad  Move between links      {Select}  Click      {Details}  Address      {Options}  Menu      LB / RB  Back / Forward      {Back}  Back")
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
        root.Children.Add(_toolbar);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        _web.Visibility = Visibility.Hidden;
        Grid.SetRow(_web, 1);
        root.Children.Add(_web);
        Grid.SetRow(_footer, 2);
        root.Children.Add(_footer);
        Content = root;
    }

    public override void FocusDefault() { }

    public override void OnShown() => _ = StartAsync();

    // The web view is a window of its own and would cover any pop-up drawn over it, so it steps aside for them.
    public override void OnCovered() => _web.Visibility = Visibility.Hidden;

    public override void OnResumed()
    {
        if (_ready) _web.Visibility = Visibility.Visible;
    }

    public override void OnClosed()
    {
        _closed = true;
        try { _web.Dispose(); } catch (Exception) { }
    }

    private async Task StartAsync()
    {
        try
        {
            // Cookies and sign-ins are kept in the launcher's own folder, separate from Edge.
            var data = System.IO.Path.Combine(Settings.DataDir, "Browser");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(environment);
            if (_closed) return;

            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            // Links that want a new window open here instead: there is only this one page.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri)) core.Navigate(e.Uri);
            };
            core.DocumentTitleChanged += (_, _) => ShowAddress();
            // Full-screen video takes the whole page: hide our own bars while it plays.
            core.ContainsFullScreenElementChanged += (_, _) =>
                _toolbar.Visibility = _footer.Visibility = core.ContainsFullScreenElement ? Visibility.Collapsed : Visibility.Visible;

            _web.NavigationStarting += (_, _) => { _loading = true; _reload.Content = ""; };
            _web.NavigationCompleted += (_, _) =>
            {
                _loading = false;
                _reload.Content = "";
                ShowAddress();
                _ = RunAsync(FocusRingScript);
            };
            _web.SourceChanged += (_, _) => ShowAddress();

            _web.ZoomFactor = Host.Settings.BrowserZoom / 100.0;
            _ready = true;
            _status.Visibility = Visibility.Collapsed;
            if (Host.IsTop(this)) _web.Visibility = Visibility.Visible;
            Navigate(_startUrl ?? Host.Settings.BrowserHome);
        }
        catch (Exception ex)
        {
            _status.Text = "The built-in browser couldn't start. It needs the Microsoft Edge WebView2 Runtime, which comes with " +
                           "Windows 11 and current Windows 10. Install it from microsoft.com/edge/webview2 and try again.\n\n" + ex.Message;
        }
    }

    private void ShowAddress()
    {
        if (!_ready) return;
        string title = _web.CoreWebView2.DocumentTitle;
        string url = _web.Source?.ToString() ?? "";
        _address.Text = title.Length > 0 && title != url ? title + "   –   " + url : url;
    }

    /// <summary>Goes to an address, or searches for the words if it doesn't look like one.</summary>
    private void Navigate(string text)
    {
        if (!_ready) return;
        text = text.Trim();
        if (text.Length == 0) return;

        string url;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = text;
        else if (!text.Contains(' ') && text.Contains('.') && Uri.TryCreate("https://" + text, UriKind.Absolute, out _))
            url = "https://" + text;
        else
            url = Engines.FirstOrDefault(e => e.Id == Host.Settings.BrowserSearch).Url is { } engine
                ? engine + Uri.EscapeDataString(text)
                : Engines[0].Url + Uri.EscapeDataString(text);

        try { _web.CoreWebView2.Navigate(url); }
        catch (Exception) { /* not an address the browser accepts */ }
    }

    private void GoHome() => Navigate(Host.Settings.BrowserHome);

    private void Zoom(int step)
    {
        var s = Host.Settings;
        s.BrowserZoom = Math.Clamp(s.BrowserZoom + step, 50, 300);
        s.Save();
        if (_ready) _web.ZoomFactor = s.BrowserZoom / 100.0;
    }

    private async Task EnterAddressAsync()
    {
        if (!_ready) return;
        var text = await Host.PromptAsync("Address or search", "Type a web address, or words to search for.", "", "Go");
        if (!string.IsNullOrWhiteSpace(text)) Navigate(text);
    }

    private async Task MenuAsync()
    {
        var s = Host.Settings;
        string engine = Engines.FirstOrDefault(e => e.Id == s.BrowserSearch).Name ?? "Google";
        int choice = await Host.AskWithIconsAsync("Browser", _ready ? _web.Source?.ToString() : null,
            ("Address or search", ""), ("Reload", ""), ("Home page", ""),
            ("Zoom in  (" + s.BrowserZoom + "%)", ""), ("Zoom out", ""),
            ("Use this page as the home page", ""), ("Search engine: " + engine, ""),
            ("Clear cookies, history and saved data", ""), ("Open this page in the default browser", ""),
            ("Close the browser", ""));
        switch (choice)
        {
            case 0: await EnterAddressAsync(); break;
            case 1: if (_ready) _web.Reload(); break;
            case 2: GoHome(); break;
            case 3: Zoom(+10); break;
            case 4: Zoom(-10); break;
            case 5:
                if (_ready && _web.Source != null) { s.BrowserHome = _web.Source.ToString(); s.Save(); }
                break;
            case 6:
                int index = Array.FindIndex(Engines, e => e.Id == s.BrowserSearch);
                s.BrowserSearch = Engines[(index + 1) % Engines.Length].Id;
                s.Save();
                break;
            case 7:
                if (_ready && await Host.AskAsync("Clear browsing data?", "You will be signed out of websites in this browser.", "Cancel", "Clear") == 1)
                {
                    try { await _web.CoreWebView2.Profile.ClearBrowsingDataAsync(); } catch (Exception) { }
                }
                break;
            case 8:
                if (_ready && _web.Source != null) ProcessUtil.Open(_web.Source.ToString());
                break;
            case 9: Host.Pop(this); break;
        }
    }

    // ---------------------------------------------------------------- controller

    // A clear ring around whatever link or box the d-pad has landed on.
    private const string FocusRingScript = """
        (function(){ if(document.getElementById('sd-focus-ring')) return;
          var s=document.createElement('style'); s.id='sd-focus-ring';
          s.textContent='*:focus{outline:3px solid #20E0D0 !important;outline-offset:2px !important;}';
          (document.head||document.documentElement).appendChild(s); })();
        """;

    // Moves to the nearest link, button or box in one direction, judged by where things are on the page.
    private const string MoveScript = """
        (function(dir){
          var sel='a[href],button,input:not([type=hidden]),select,textarea,summary,[role=button],[role=link],[role=tab],[role=menuitem],[tabindex]:not([tabindex="-1"])';
          var vw=window.innerWidth, vh=window.innerHeight;
          var els=Array.prototype.filter.call(document.querySelectorAll(sel),function(e){
            if(e.disabled) return false; var r=e.getBoundingClientRect();
            if(r.width<2||r.height<2) return false; var c=getComputedStyle(e);
            return c.visibility!=='hidden'&&c.display!=='none'; });
          if(!els.length){ window.scrollBy(0,(dir==='down'?1:dir==='up'?-1:0)*vh*0.5); return; }
          var cur=document.activeElement;
          if(!cur||cur===document.body||els.indexOf(cur)<0){
            var first=els.find(function(e){var r=e.getBoundingClientRect();return r.top>=0&&r.top<vh&&r.left>=0&&r.left<vw;})||els[0];
            first.focus(); first.scrollIntoView({block:'nearest',inline:'nearest'}); return; }
          var cr=cur.getBoundingClientRect(), cx=cr.left+cr.width/2, cy=cr.top+cr.height/2, best=null, bs=1e18;
          els.forEach(function(e){ if(e===cur) return; var r=e.getBoundingClientRect();
            var dx=r.left+r.width/2-cx, dy=r.top+r.height/2-cy, main, cross;
            if(dir==='right'){ if(dx<6) return; main=dx; cross=Math.abs(dy); }
            else if(dir==='left'){ if(dx>-6) return; main=-dx; cross=Math.abs(dy); }
            else if(dir==='down'){ if(dy<6) return; main=dy; cross=Math.abs(dx); }
            else { if(dy>-6) return; main=-dy; cross=Math.abs(dx); }
            var score=main+cross*2.5; if(score<bs){ bs=score; best=e; } });
          if(best){ best.focus(); best.scrollIntoView({block:'nearest',inline:'nearest'}); }
          else if(dir==='down'||dir==='up'){ window.scrollBy(0,(dir==='down'?1:-1)*vh*0.5); }
        })
        """;

    // Clicks what is highlighted. For a text box it reports back instead, so the on-screen keyboard can be shown.
    private const string ClickScript = """
        (function(){ var e=document.activeElement; if(!e||e===document.body) return 'none';
          var t=(e.tagName||'').toLowerCase();
          var text=(t==='input'&&!/^(button|submit|checkbox|radio|range|file|color|image|reset)$/i.test(e.type||'text'))||t==='textarea';
          if(text) return 'text:'+(e.type==='password'?'':(e.value||''));
          e.click(); return 'ok'; })();
        """;

    private async Task<string> RunAsync(string script)
    {
        if (!_ready || _closed) return "";
        try
        {
            var json = await _web.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<string>(json) ?? "";
        }
        catch (Exception) { return ""; } // page still loading, or the result wasn't text
    }

    private async Task ClickAsync()
    {
        var result = await RunAsync(ClickScript);
        if (!result.StartsWith("text:", StringComparison.Ordinal)) return;

        var typed = await Host.PromptAsync("Type here", null, result[5..], "Enter");
        if (typed == null) return;
        // Put the text in the box the page had highlighted, tell the page it changed, and send a search box on its way.
        string value = JsonSerializer.Serialize(typed);
        await RunAsync($$"""
            (function(v){ var e=document.activeElement; if(!e) return 'none';
              var p=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
              var set=Object.getOwnPropertyDescriptor(p,'value'); if(set&&set.set) set.set.call(e,v); else e.value=v;
              e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true}));
              var search=e.type==='search'||e.name==='q'||e.getAttribute('role')==='searchbox'||e.getAttribute('role')==='combobox';
              if(search&&e.form){ if(e.form.requestSubmit) e.form.requestSubmit(); else e.form.submit(); }
              return 'ok'; })({{value}});
            """);
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Up: _ = RunAsync(MoveScript + "('up');"); return true;
            case NavAction.Down: _ = RunAsync(MoveScript + "('down');"); return true;
            case NavAction.Left: _ = RunAsync(MoveScript + "('left');"); return true;
            case NavAction.Right: _ = RunAsync(MoveScript + "('right');"); return true;
            case NavAction.Accept: _ = ClickAsync(); return true;
            case NavAction.X: _ = EnterAddressAsync(); return true;
            case NavAction.Y: _ = MenuAsync(); return true;
            case NavAction.LB: if (_ready && _web.CanGoBack) _web.GoBack(); return true;
            case NavAction.RB: if (_ready && _web.CanGoForward) _web.GoForward(); return true;
            case NavAction.LT: _ = RunAsync("window.scrollBy(0,-window.innerHeight*0.85);"); return true;
            case NavAction.RT: _ = RunAsync("window.scrollBy(0,window.innerHeight*0.85);"); return true;
            case NavAction.Back:
                // B is the browser's Back button until there is nowhere left to go back to.
                if (_ready && _web.CanGoBack) { _web.GoBack(); return true; }
                return false;
        }
        return false;
    }

    public override bool OnRightStick(double y)
    {
        // Sent in small batches: one script call per frame would be more than the page needs.
        _scrollCarry += -y * Math.Abs(y) * 34;
        if (Math.Abs(_scrollCarry) < 24) return true;
        int amount = (int)_scrollCarry;
        _scrollCarry = 0;
        _ = RunAsync($"window.scrollBy(0,{amount});");
        return true;
    }
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
        AddRow("Open the release page", glyph: "", click: () => ProcessUtil.Open(_release.PageUrl));
        AddRow("Not now", glyph: "", click: () => Host.Pop(this));

        AddHeader("What's new");
        var notes = Updater.PlainNotes(_release.Notes);
        if (notes.Length == 0) AddNote("The release has no description.");
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
    private readonly TranslateTransform _slide = new(BladeWidth, 0);
    private readonly Grid _backdrop = new();
    private bool _closing;

    public override bool IsDialog => true;

    public NotificationsScreen()
    {
        var title = Ui.Text("Notifications", 28);
        var close = Ui.PanelButton("");
        close.FontFamily = Ui.IconFont;
        close.Focusable = false;
        close.Click += (_, _) => Dismiss();
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

    public override void FocusDefault() => _rows.Children.OfType<Button>().FirstOrDefault()?.Focus();

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
            case NavAction.Left or NavAction.Right: return true; // one column: nothing to the sides
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
                if (entry.Kind == "windows") glyph = "\uE782";
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
                        face.Child = new Image { Source = game.Cover, Margin = new Thickness(8), Stretch = Stretch.Uniform };
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
                row.Click += (_, _) => _ = Host.AskAsync(entry.Title, entry.Text + "\n\n" + entry.When.ToString("f"), "OK");
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

/// <summary>"Starting … in 3": the game's logo on top, Start now first, then Cancel.</summary>
public sealed class CountdownScreen : Screen
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _text;
    private readonly Image _logo = new() { MaxWidth = 380, MaxHeight = 130, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _cover;
    private readonly Game _game;
    private int _seconds = 3;
    private bool _go;

    public override bool IsDialog => true;

    /// <summary>True to start the game, false if it was cancelled.</summary>
    public Task<bool> Result => _done.Task;

    public CountdownScreen(Game game)
    {
        _game = game;
        var panel = new StackPanel();

        // The logo sits at the top, centered. Until it arrives (or if there is none), the cover stands in.
        _cover = new Border
        {
            Width = 110, Height = 110, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center,
            Background = game.Cover != null ? new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill } : null,
            Visibility = game.Cover != null ? Visibility.Visible : Visibility.Collapsed
        };
        _logo.Visibility = Visibility.Collapsed;
        var top = new Grid { Margin = new Thickness(0, 0, 0, 20), MinHeight = game.Cover != null ? 110 : 0 };
        top.Children.Add(_cover);
        top.Children.Add(_logo);
        panel.Children.Add(top);

        _text = Ui.Text("", 26);
        _text.TextWrapping = TextWrapping.Wrap;
        _text.TextAlignment = TextAlignment.Center;
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(_text);

        var now = Ui.Row("Start now", glyph: "");
        now.Margin = new Thickness(0, 20, 0, 3);
        now.Click += (_, _) => { _go = true; Host.Pop(this); };
        panel.Children.Add(now);
        var cancel = Ui.Row("Cancel", glyph: "");
        cancel.Click += (_, _) => Host.Pop(this);
        panel.Children.Add(cancel);

        var box = new Border
        {
            Width = 560, Padding = new Thickness(32), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;

        _timer.Tick += (_, _) =>
        {
            if (--_seconds > 0) { Show(); return; }
            _go = true;
            Host.Pop(this);
        };
        Show();
    }

    private void Show() => _text.Text = $"Starting {_game.Name} in {_seconds}…";

    public override void OnShown()
    {
        _timer.Start();
        _ = LoadLogoAsync();
    }

    private async Task LoadLogoAsync()
    {
        try
        {
            var key = Host.Settings.SteamGridDbApiKey;
            var ct = _cts.Token;
            var logo = await Task.Run(async () =>
            {
                using var client = new SteamGridDbClient(key);
                var path = await client.GetLogoAsync(_game, ct);
                return path == null ? null : ImageLoader.FromFile(path, 760);
            }, ct);
            if (logo == null || ct.IsCancellationRequested) return;
            _logo.Source = logo;
            _logo.Visibility = Visibility.Visible;
            _cover.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // No logo, offline, or the countdown ended first: the cover stays.
        }
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _cts.Cancel();
        _done.TrySetResult(_go);
    }
}
