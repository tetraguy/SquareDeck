using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A store site (Store > Deals and releases > Buy Steam Keys, and Nexus Mods) on a page of its own, in Microsoft Edge WebView2, made for
/// a controller. Its sign-in is kept in SquareDeck's own folder (apart from Edge and the built-in browser), so you stay
/// signed in. Sign-in and payment pages on other sites (PayPal, card checks) open here too, in the same page.
///
/// Leaving is always one press away: View closes it straight away, B goes back a page and closes it on the first one,
/// Y has Close at the top, and there's a Close button in the top corner for touch. It isn't kept running afterwards,
/// unless something is still downloading: then it carries on in the background and closes itself when it's done.
///
/// Downloads (mods from Nexus, usually) have their own manager instead of Edge's pop-up: they save to Downloads\(store),
/// the bottom bar shows how far along they are, Y > Downloads lists them (pause, resume, cancel), and opening a finished
/// one shows it in SquareDeck's own File Explorer.
///
/// Controls: d-pad moves between links and buttons, A chooses (or clicks at the pointer), the right stick moves a
/// pointer (press it to click), LT / RT scroll, LB / RB go back / forward, X types in the box that's highlighted.
/// </summary>
public sealed class StoreWebScreen : Screen
{
    /// <summary>The store's name, its page, and the icon on its tile.</summary>
    public sealed record KeyStore(string Name, string Url, string Glyph);

    public static readonly KeyStore Loaded = new("Loaded", "https://www.loaded.com/pc/steam", "\uE8D7");

    /// <summary>Nexus Mods (Store > under Add more stores). Files download to the Downloads folder.</summary>
    public static readonly KeyStore NexusMods = new("Nexus Mods", "https://www.nexusmods.com/", "\uEA86");

    private static Task<CoreWebView2Environment>? _environment;

    // A store page still downloading after you left it, so opening the store again comes back to it.
    private static readonly Dictionary<string, StoreWebScreen> Parked = new();

    /// <summary>The store's page: the one still downloading in the background, or a new one.</summary>
    public static StoreWebScreen Open(KeyStore store) =>
        Parked.TryGetValue(store.Name, out var kept) && !kept._closed ? kept : new StoreWebScreen(store);

    /// <summary>Where this store's downloads go: Downloads\(store name).</summary>
    public static string FolderFor(KeyStore store) => Path.Combine(Diagnostics.DownloadsFolder, store.Name);

    private readonly TextBlock _downloadText = Ui.Text("", 14, "FgBrush");
    private readonly Border _downloadChip = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 16, 5), Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
    private readonly System.Windows.Threading.DispatcherTimer _downloadTick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _parked;

    private readonly KeyStore _store;
    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden };
    private readonly TextBlock _title = Ui.Text("", 16, "SubtleBrush");
    private readonly TextBlock _status = Ui.Text("", 20, "SubtleBrush");
    private readonly Border _loading = new() { Height = 3, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, VerticalAlignment = VerticalAlignment.Bottom };
    private bool _started, _ready, _closed;
    private double _moveX, _moveY;
    private bool _stickWasPressed;
    private DateTime _pointerMovedAt = DateTime.MinValue;

    private StoreWebScreen(KeyStore store)
    {
        _store = store;

        // Top bar: the store's icon and name, the page's title, and Close.
        var top = new Grid { Height = 58 };
        top.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
        // The store's own logo when SquareDeck has one (Nexus Mods), otherwise its icon.
        if (StoreLogos.For(store.Name) is { } logo)
            left.Children.Add(new Image { Source = logo, Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        else
        {
            var glyph = Ui.Glyph(store.Glyph, 20, "AccentBrush");
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(glyph);
        }
        var name = Ui.Text(store.Name, 20, "FgBrush", bold: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 0, 18, 0);
        left.Children.Add(name);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.MaxWidth = 760;
        left.Children.Add(_title);
        top.Children.Add(left);

        var closeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var closeGlyph = Ui.Glyph("\uE711", 14, "FgBrush");
        closeGlyph.VerticalAlignment = VerticalAlignment.Center;
        closeGlyph.Margin = new Thickness(0, 0, 8, 0);
        closeRow.Children.Add(closeGlyph);
        closeRow.Children.Add(Ui.Text("Close", 15, "FgBrush", bold: true));
        var close = new Button
        {
            Style = Ui.StyleOf("PillButton"), Content = closeRow, Focusable = false, Padding = new Thickness(18, 8, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0)
        };
        close.Click += (_, _) => Leave();
        top.Children.Add(close);
        _loading.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        top.Children.Add(_loading);

        var footer = new Grid { Height = 52 };
        footer.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        var hints = new HintBar("{Select}  Choose      {Details}  Type      LB RB  Back / forward      LT RT  Scroll      {Options}  Menu      {Back}  Back      View  Close")
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        footer.Children.Add(hints);
        // How the downloads are going, on the right of the bottom bar.
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
        var chipGlyph = Ui.Glyph("\uE896", 14, "AccentBrush");
        chipGlyph.VerticalAlignment = VerticalAlignment.Center;
        chipGlyph.Margin = new Thickness(0, 0, 8, 0);
        chipRow.Children.Add(chipGlyph);
        _downloadText.VerticalAlignment = VerticalAlignment.Center;
        chipRow.Children.Add(_downloadText);
        _downloadChip.Child = chipRow;
        _downloadChip.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        footer.Children.Add(_downloadChip);
        _downloadTick.Tick += (_, _) => ShowDownloads();

        var page = new Grid();
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.MaxWidth = 760;
        _status.Text = "Opening " + store.Name + "…";
        page.Children.Add(_status);
        page.Children.Add(_web);

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(top);
        Grid.SetRow(page, 1);
        root.Children.Add(page);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    public override void OnShown()
    {
        _parked = false;
        Parked.Remove(_store.Name);
        ShowDownloads();
        if (_started) { Dispatcher.BeginInvoke(() => _web.Focus()); return; }
        _started = true;
        _ = StartAsync();
    }

    public override void OnClosed()
    {
        // Still downloading: it carries on in the background, and closes itself when the last download ends.
        if (StoreDownloads.Active(this) > 0)
        {
            _parked = true;
            Parked[_store.Name] = this;
            Host.ShowToast(_store.Name.ToUpperInvariant(), "Downloads carry on in the background", "Open " + _store.Name + " again to see them", "", "info", remember: false);
            base.OnClosed();
            return;
        }
        Shutdown();
        base.OnClosed();
    }

    /// <summary>Not kept running: the sign-in stays in its folder for next time.</summary>
    private void Shutdown()
    {
        if (_closed) return;
        _closed = true;
        _downloadTick.Stop();
        Parked.Remove(_store.Name);
        try { _web.Dispose(); } catch (Exception) { }
    }

    /// <summary>The bottom bar's download line, and closing a page left in the background once it's done.</summary>
    private void ShowDownloads()
    {
        var mine = StoreDownloads.All.Where(d => d.Owner == this).ToList();
        var active = mine.Where(d => d.IsActive).ToList();
        if (_parked && active.Count == 0) { Shutdown(); return; }
        if (active.Count > 0)
        {
            long got = active.Sum(d => d.Received), total = active.Sum(d => d.Total ?? 0);
            string pct = total > 0 ? $"{100.0 * got / total:0}%" : StoreDownloads.Size(got);
            _downloadText.Text = active.Count == 1 ? $"{Trim(active[0].Name)}  \u00B7  {pct}" : $"{active.Count} downloads  \u00B7  {pct}";
            _downloadChip.Visibility = Visibility.Visible;
            if (!_downloadTick.IsEnabled) _downloadTick.Start();
        }
        else
        {
            _downloadTick.Stop();
            var last = mine.LastOrDefault();
            _downloadChip.Visibility = last == null ? Visibility.Collapsed : Visibility.Visible;
            if (last != null) _downloadText.Text = last.State == StoreDownloads.Done ? $"{Trim(last.Name)} downloaded  \u00B7  Y > Downloads" : $"{Trim(last.Name)}: {last.State}";
        }
    }

    private static string Trim(string name) => name.Length > 34 ? name[..31] + "\u2026" : name;

    private async Task StartAsync()
    {
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "KeyStores"));
            await _web.EnsureCoreWebView2Async(await _environment);
            if (_closed) return;
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            // Pop-ups (PayPal, card checks, "sign in with Steam") open in this same page: no second window to get lost in.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "https" or "http") core.Navigate(e.Uri);
            };
            // Links that would leave for another program (steam://, mailto:) aren't followed from inside the store, except
            // Nexus Mods' "Mod manager download" (nxm://), handed to the mod manager that's installed (Vortex, Mod Organizer).
            _web.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http" or "about" or "data" or "blob"))
                {
                    e.Cancel = true;
                    if (u?.Scheme == "nxm")
                    {
                        bool opened = ProcessUtil.Open(e.Uri);
                        Host.ShowToast("NEXUS MODS", opened ? "Sent to your mod manager" : "No mod manager is installed",
                            opened ? "It downloads and installs there" : "Install Vortex or Mod Organizer 2, or use Manual download", "", "info", remember: false);
                    }
                    return;
                }
                ShowLoading(true);
            };
            core.DocumentTitleChanged += (_, _) => _title.Text = core.DocumentTitle;
            // Downloads: SquareDeck's own manager instead of Edge's pop-up, into Downloads\(store).
            core.DownloadStarting += (_, e) =>
            {
                try
                {
                    string folder = FolderFor(_store);
                    Directory.CreateDirectory(folder);
                    e.ResultFilePath = StoreDownloads.FreeName(folder, Path.GetFileName(e.ResultFilePath));
                    e.Handled = true; // no Edge download pop-up over the page
                    StoreDownloads.Track(this, _store, e.DownloadOperation, e.ResultFilePath, OnDownloadEnded);
                    UiSound.Select();
                    ShowDownloads();
                }
                catch (Exception) { }
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScreen.HelperScript);
            _web.NavigationCompleted += (_, _) =>
            {
                ShowLoading(false);
                if (_ready) return;
                _ready = true;
                _status.Visibility = Visibility.Collapsed;
                _web.Visibility = Visibility.Visible;
                _web.Focus();
            };
            _web.Source = new Uri(_store.Url);
        }
        catch (Exception ex)
        {
            _environment = null;
            _status.Text = _store.Name + " couldn't open. It needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11.\n\n" + ex.Message;
        }
    }

    private void ShowLoading(bool on)
    {
        _loading.BeginAnimation(WidthProperty, null);
        if (!on) { _loading.Width = 0; return; }
        if (Theme.ReduceMotion) { _loading.Width = 240; return; }
        _loading.BeginAnimation(WidthProperty, new DoubleAnimation(0, 900, TimeSpan.FromSeconds(4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Leave()
    {
        if (_closed) return;
        UiSound.Back();
        Host.Pop(this);
    }

    private async Task<string> Run(string script)
    {
        if (!_ready || _closed) return "";
        try
        {
            var json = await _web.ExecuteScriptAsync(script);
            return json.StartsWith('"') ? JsonSerializer.Deserialize<string>(json) ?? "" : json;
        }
        catch (Exception) { return ""; }
    }

    private Task<string> Sd(string call) =>
        Run("(function(){" + BrowserScreen.HelperScript + "; return window.__sd ? window.__sd." + call + " : '';})()");

    private bool PointerActive => DateTime.UtcNow - _pointerMovedAt < TimeSpan.FromSeconds(4);

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.View) { Leave(); return true; }
        if (!_ready) { if (action == NavAction.Back) { Leave(); return true; } return false; }
        if (action is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right or NavAction.Accept && !_web.IsKeyboardFocusWithin) _web.Focus();
        switch (action)
        {
            case NavAction.Up: _ = Sd("move('up')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Down: _ = Sd("move('down')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Left: _ = Sd("move('left')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Right: _ = Sd("move('right')"); _pointerMovedAt = DateTime.MinValue; return true;
            case NavAction.Accept:
                if (PointerActive) MouseClick(); else _ = ClickAsync();
                return true;
            case NavAction.X: _ = TypeAsync(null); return true;
            case NavAction.LT: _ = Sd("scroll(-innerHeight*0.8)"); return true;
            case NavAction.RT: _ = Sd("scroll(innerHeight*0.8)"); return true;
            case NavAction.LB: if (_web.CanGoBack) _web.GoBack(); return true;
            case NavAction.RB: if (_web.CanGoForward) _web.GoForward(); return true;
            case NavAction.Y: _ = MenuAsync(); return true;
            case NavAction.Back:
                if (_web.CanGoBack) _web.GoBack(); else Leave();
                return true;
        }
        return false;
    }

    private async Task ClickAsync()
    {
        var result = await Sd("click()");
        if (result.StartsWith("text:", StringComparison.Ordinal)) await TypeAsync(result[5..]);
    }

    /// <summary>Types into the box that's highlighted: Windows' touch keyboard, or SquareDeck's own.</summary>
    private async Task TypeAsync(string? current)
    {
        if (TouchKeyboard.UseWindows)
        {
            _web.Focus();
            await TouchKeyboard.ShowAsync();
            return;
        }
        var typed = await Host.PromptAsync("Type here", null, current ?? "", "Enter");
        if (typed == null) return;
        await Sd("type(" + JsonSerializer.Serialize(typed) + ")");
    }

    private void OnDownloadEnded(StoreDownloads.Item item)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowDownloads();
            if (item.State == StoreDownloads.Done)
                Host.ShowToast(_store.Name.ToUpperInvariant(), item.Name + " downloaded", "Y > Downloads to open it in File Explorer", "", "info", remember: false);
            else if (item.State != "Cancelled")
                Host.ShowToast(_store.Name.ToUpperInvariant(), item.Name + " didn't finish", item.State, "", "info", remember: false);
        });
    }

    private async Task MenuAsync()
    {
        int active = StoreDownloads.Active(this);
        var options = new List<(string Label, string Glyph, Action Run)>
        {
            ("Close " + _store.Name, "\uE711", Leave),
            (active > 0 ? $"Downloads  \u00B7  {active} in progress" : "Downloads", "\uE896", () => Host.Push(new StoreDownloadsScreen(_store))),
            (_store.Name + " home", "\uE80F", () => _web.CoreWebView2?.Navigate(_store.Url)),
            ("Reload", "\uE72C", () => _web.Reload()),
            ("Open in the web browser", "\uE774", () => { var url = _web.Source?.ToString() ?? _store.Url; Host.Pop(this); Host.Push(BrowserScreen.Open(Host, url)); }),
            ("Open the downloads folder", "\uE838", () => { Directory.CreateDirectory(FolderFor(_store)); Host.Push(new ExplorerScreen(FolderFor(_store))); }),
        };
        int choice = await Host.AskWithIconsAsync(_store.Name, _title.Text.Length > 0 ? _title.Text : null, options.Select(o => (o.Label, o.Glyph)).ToArray());
        if (choice >= 0) options[choice].Run();
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
/// The store pages' downloads (StoreWebScreen): what's downloading, how far along, and what finished, for the bottom
/// bar and the Downloads page. Kept for as long as SquareDeck runs; files already in the folder are listed too.
/// </summary>
public static class StoreDownloads
{
    public const string Downloading = "Downloading", Paused = "Paused", Done = "Downloaded";

    public sealed class Item
    {
        public required StoreWebScreen Owner;
        public required StoreWebScreen.KeyStore Store;
        public required CoreWebView2DownloadOperation Operation;
        public required string Path;
        public string Name => System.IO.Path.GetFileName(Path);
        public long Received;
        public long? Total;
        public string State = Downloading;
        public DateTime Started = DateTime.Now;
        public bool IsActive => State is Downloading or Paused;
    }

    public static readonly List<Item> All = new();

    /// <summary>Something changed (progress, finished, cancelled): the Downloads page redraws.</summary>
    public static event Action? Changed;

    public static int Active(StoreWebScreen owner) => All.Count(d => d.Owner == owner && d.IsActive);

    public static void Track(StoreWebScreen owner, StoreWebScreen.KeyStore store, CoreWebView2DownloadOperation op, string path, Action<Item> ended)
    {
        var item = new Item { Owner = owner, Store = store, Operation = op, Path = path, Total = op.TotalBytesToReceive is ulong t && t > 0 ? (long)t : null };
        All.Add(item);
        op.BytesReceivedChanged += (_, _) =>
        {
            item.Received = op.BytesReceived;
            if (op.TotalBytesToReceive is ulong total && total > 0) item.Total = (long)total;
            Changed?.Invoke();
        };
        op.StateChanged += (_, _) =>
        {
            switch (op.State)
            {
                case CoreWebView2DownloadState.InProgress: item.State = op.CanResume && op.InterruptReason != CoreWebView2DownloadInterruptReason.None ? Paused : Downloading; break;
                case CoreWebView2DownloadState.Completed: item.State = Done; item.Received = item.Total ?? item.Received; ended(item); break;
                case CoreWebView2DownloadState.Interrupted:
                    item.State = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "Cancelled" : "Stopped (" + op.InterruptReason + ")";
                    ended(item);
                    break;
            }
            Changed?.Invoke();
        };
        Changed?.Invoke();
    }

    public static void Pause(Item d) { try { d.Operation.Pause(); d.State = Paused; } catch (Exception) { } Changed?.Invoke(); }
    public static void Resume(Item d) { try { d.Operation.Resume(); d.State = Downloading; } catch (Exception) { } Changed?.Invoke(); }
    public static void Cancel(Item d) { try { d.Operation.Cancel(); } catch (Exception) { } }

    /// <summary>"file.zip", or "file (2).zip" when that's taken.</summary>
    public static string FreeName(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        string path = System.IO.Path.Combine(folder, name);
        string stem = System.IO.Path.GetFileNameWithoutExtension(name), ext = System.IO.Path.GetExtension(name);
        for (int i = 2; File.Exists(path) || All.Any(d => d.IsActive && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)); i++)
            path = System.IO.Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>
/// A store's downloads (StoreWebScreen > Y > Downloads): this session's, with progress, pause, resume and cancel,
/// then the files already in its folder. Opening a finished one shows it in SquareDeck's File Explorer.
/// </summary>
public sealed class StoreDownloadsScreen : ListScreen
{
    private readonly StoreWebScreen.KeyStore _store;
    private readonly System.Windows.Threading.DispatcherTimer _redraw = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _dirty;

    public StoreDownloadsScreen(StoreWebScreen.KeyStore store) : base(store.Name + " downloads")
    {
        _store = store;
        _redraw.Tick += (_, _) => { if (_dirty) { _dirty = false; Rebuild(); } };
    }

    private void OnChanged() => _dirty = true;

    public override void OnShown()
    {
        StoreDownloads.Changed -= OnChanged;
        StoreDownloads.Changed += OnChanged;
        _redraw.Start();
        base.OnShown();
    }

    public override void OnClosed()
    {
        StoreDownloads.Changed -= OnChanged;
        _redraw.Stop();
        base.OnClosed();
    }

    protected override void Build()
    {
        string folder = StoreWebScreen.FolderFor(_store);
        AddRow("Open the downloads folder", null, "\uE838", click: () => { Directory.CreateDirectory(folder); Host.Push(new ExplorerScreen(folder)); },
            subtitle: folder);

        var session = StoreDownloads.All.Where(d => d.Store == _store).Reverse().ToList();
        if (session.Count > 0)
        {
            AddHeader("This time");
            foreach (var d in session)
            {
                var item = d;
                string value = item.State switch
                {
                    StoreDownloads.Downloading => item.Total is long t && t > 0 ? $"{100.0 * item.Received / t:0}%" : StoreDownloads.Size(item.Received),
                    StoreDownloads.Done => "Downloaded",
                    _ => item.State,
                };
                string sub = item.Total is long total && total > 0 ? $"{StoreDownloads.Size(item.Received)} of {StoreDownloads.Size(total)}" : StoreDownloads.Size(item.Received);
                AddRow(item.Name, value, item.State == StoreDownloads.Done ? "\uE73E" : "\uE896", click: () => _ = ChooseAsync(item), subtitle: sub);
            }
        }

        var shown = session.Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<FileInfo> files = new();
        try { if (Directory.Exists(folder)) files = new DirectoryInfo(folder).GetFiles().Where(f => !shown.Contains(f.FullName)).OrderByDescending(f => f.LastWriteTime).Take(60).ToList(); }
        catch (Exception) { }
        if (files.Count > 0)
        {
            AddHeader("In the folder");
            foreach (var f in files)
            {
                var file = f;
                AddRow(file.Name, StoreDownloads.Size(file.Length), "\uE8A5", click: () => OpenInExplorer(file.FullName),
                    subtitle: file.LastWriteTime.ToString("g"));
            }
        }
        if (session.Count == 0 && files.Count == 0)
            AddNote("Nothing downloaded yet. Files you download on " + _store.Name + " show up here, and open in SquareDeck's File Explorer.");
    }

    private void OpenInExplorer(string path)
    {
        if (!File.Exists(path)) { Rebuild(); return; }
        Host.Push(new ExplorerScreen(Path.GetDirectoryName(path)) { SelectPath = path });
    }

    private async Task ChooseAsync(StoreDownloads.Item d)
    {
        var options = new List<(string Label, string Glyph, Action Run)>();
        if (d.State == StoreDownloads.Done)
        {
            options.Add(("Open in File Explorer", "\uE838", () => OpenInExplorer(d.Path)));
            options.Add(("Open", "\uE8A5", () => ProcessUtil.OpenOutside(d.Path)));
        }
        if (d.State == StoreDownloads.Downloading && d.Operation.CanResume) options.Add(("Pause", "\uE769", () => StoreDownloads.Pause(d)));
        if (d.State == StoreDownloads.Paused) options.Add(("Resume", "\uE768", () => StoreDownloads.Resume(d)));
        if (d.IsActive) options.Add(("Cancel the download", "\uE711", () => StoreDownloads.Cancel(d)));
        if (options.Count == 0) { OpenInExplorer(d.Path); return; }
        int choice = await Host.AskWithIconsAsync(d.Name, d.State, options.Select(o => (o.Label, o.Glyph)).ToArray());
        if (choice >= 0) options[choice].Run();
    }
}
