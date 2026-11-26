using System.IO;
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

/// <summary>A cloud game streaming service SquareDeck can open full screen (turned on in Settings > Games and library).</summary>
public sealed record CloudService(string Id, string Name, string Url, Color Brand, Color OnBrand, string About)
{
    public static readonly CloudService[] All =
    {
        new("xcloud", "Xbox Cloud Gaming", "https://www.xbox.com/en-us/play", Color.FromRgb(0x10, 0x7C, 0x10), Colors.White,
            "Hundreds of Game Pass games (needs Game Pass Ultimate)"),
        new("geforcenow", "GeForce NOW", "https://play.geforcenow.com/", Color.FromRgb(0x76, 0xB9, 0x00), Color.FromRgb(0x10, 0x10, 0x10),
            "Your own Steam, Epic and other games, played on NVIDIA's computers"),
        new("luna", "Amazon Luna", "https://luna.amazon.com/", Color.FromRgb(0x7B, 0x2F, 0xF7), Colors.White,
            "Amazon's cloud games, and channels like Ubisoft+"),
        new("antstream", "Antstream Arcade", "https://www.antstream.com/", Color.FromRgb(0xE6, 0x00, 0x7E), Colors.White,
            "More than a thousand classic arcade, console and computer games"),
    };

    /// <summary>The service's logo (CloudLogos\(id).png next to the app), else its name on its color.</summary>
    public Border Face(double width, double height, double fontSize)
    {
        // Same shape as the other store tiles: the tile-corner style, rounded at least a little.
        double corner = Math.Max(Theme.TileCorner, 10);
        var box = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(corner), ClipToBounds = true };
        box.Clip = new RectangleGeometry(new Rect(0, 0, width, height), corner, corner);
        var path = Path.Combine(AppContext.BaseDirectory, "CloudLogos", Id + ".png");
        if (File.Exists(path) && ImageLoader.FromFile(path, 512) is { } logo)
        {
            // The logos are full square tiles in the service's colors: fill the tile, kept square.
            box.Width = box.Height = Math.Min(width, height);
            box.Clip = new RectangleGeometry(new Rect(0, 0, box.Width, box.Height), corner, corner);
            var img = new Image { Source = logo, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            box.Child = img;
            return box;
        }
        var brush = new LinearGradientBrush(Brand, Color.FromRgb((byte)(Brand.R * 0.55), (byte)(Brand.G * 0.55), (byte)(Brand.B * 0.55)), 45);
        box.Background = brush;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var cloud = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = fontSize * 1.6, Foreground = new SolidColorBrush(OnBrand), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };
        stack.Children.Add(cloud);
        stack.Children.Add(new TextBlock
        {
            Text = Name, FontSize = fontSize, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(OnBrand), TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 0)
        });
        box.Child = stack;
        return box;
    }
}

/// <summary>
/// A cloud gaming service, full screen. The controller goes straight to the page (the services read it like any
/// browser game), so SquareDeck stops acting on it while this is open. To leave:
///  - hold the leave buttons (View + Menu unless changed in Settings) for about a second and a half;
///  - or press the leave shortcut (Ctrl + Shift + Q unless changed) on a keyboard;
///  - or use the small, nearly invisible button in a corner of the screen (tap or click it, then Exit).
/// </summary>
public sealed class CloudGameScreen : Screen
{
    /// <summary>Controller combinations that can leave a cloud game (Settings > Cloud gaming > Leave with).</summary>
    public static readonly (string Id, string Name, string About)[] LeaveButtons =
    {
        ("view+start", "View + Menu", "The two small buttons in the middle"),
        ("l3+r3", "Both sticks pressed in", "Click in the left and right sticks together"),
        ("lb+rb", "LB + RB", "Both shoulder buttons"),
        ("lb+rb+view", "LB + RB + View", "Hardest to press by accident"),
        ("lt+rt+view", "LT + RT + View", "Both triggers pulled, plus View"),
        ("view+b", "View + B", "View and the B button"),
        ("guide", "Guide (Xbox) button", "Only if Windows doesn't keep it for the Game Bar"),
    };

    public static readonly (string Id, string Name, string About)[] LeaveHolds =
    {
        ("0.5", "Half a second", ""), ("1", "1 second", ""), ("1.5", "1.5 seconds", ""), ("2", "2 seconds", ""), ("3", "3 seconds", ""),
    };

    /// <summary>Keyboard shortcuts that can leave a cloud game.</summary>
    public static readonly (string Id, string Name, string About)[] LeaveKeys =
    {
        ("ctrl+shift+q", "Ctrl + Shift + Q", ""), ("ctrl+alt+q", "Ctrl + Alt + Q", ""), ("ctrl+shift+x", "Ctrl + Shift + X", ""),
        ("ctrl+shift+end", "Ctrl + Shift + End", ""), ("alt+f12", "Alt + F12", ""), ("none", "None", ""),
    };

    public static string LeaveButtonsName(string id) => LeaveButtons.FirstOrDefault(b => b.Id == id).Name ?? LeaveButtons[0].Name;
    public static string LeaveKeyName(string id) => LeaveKeys.FirstOrDefault(k => k.Id == id).Name ?? LeaveKeys[0].Name;

    /// <summary>Whether the chosen leave combination is held in this controller reading.</summary>
    private static bool LeaveHeld(Gamepad.RawState raw, string id)
    {
        const ushort Start = 0x0010, View = 0x0020, L3 = 0x0040, R3 = 0x0080, LB = 0x0100, RB = 0x0200, Guide = 0x0400, B = 0x2000;
        bool Has(ushort mask) => (raw.Buttons & mask) == mask;
        return id switch
        {
            "l3+r3" => Has(L3 | R3),
            "lb+rb" => Has(LB | RB),
            "lb+rb+view" => Has(LB | RB | View),
            "lt+rt+view" => raw.LeftTrigger > 160 && raw.RightTrigger > 160 && Has(View),
            "view+b" => Has(View | B),
            "guide" => Has(Guide),
            _ => Has(View | Start),
        };
    }

    private static bool LeaveKeyPressed(Key key, ModifierKeys mods, string id) => id switch
    {
        "none" => false,
        "ctrl+alt+q" => key == Key.Q && mods == (ModifierKeys.Control | ModifierKeys.Alt),
        "ctrl+shift+x" => key == Key.X && mods == (ModifierKeys.Control | ModifierKeys.Shift),
        "ctrl+shift+end" => key == Key.End && mods == (ModifierKeys.Control | ModifierKeys.Shift),
        "alt+f12" => key == Key.F12 && mods == ModifierKeys.Alt,
        _ => key == Key.Q && mods == (ModifierKeys.Control | ModifierKeys.Shift),
    };

    private double HoldSeconds => Math.Clamp(Host?.Settings.CloudLeaveHold ?? 1.5, 0.3, 5);

    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>The service that's open, if any (the launcher hands it the controller while it's in front).</summary>
    public static CloudGameScreen? Active { get; private set; }

    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden };
    private readonly TextBlock _status = Ui.Text("", 22, "SubtleBrush");
    private readonly Grid _introHost = new();
    private DateTime _openedAt = DateTime.UtcNow;
    private bool _revealed;
    private CloudCorner? _corner;
    private DateTime? _comboSince;
    private bool _closing, _started;

    public CloudService Service { get; }

    /// <summary>When the service was opened (for the Game Bar widget's "min now").</summary>
    public DateTime OpenedAt => _openedAt.ToLocalTime();

    public CloudGameScreen(CloudService service)
    {
        Service = service;
        var root = new Grid { Background = Brushes.Black };
        // The launch animation goes here when the screen opens (it needs the settings).
        root.Children.Add(_introHost);
        var waiting = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 60) };
        _status.Text = "Opening " + service.Name + "…";
        _status.Margin = new Thickness(0, 22, 0, 0);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxWidth = 760;
        waiting.Children.Add(_status);
        var how = Ui.Text("", 16, "SubtleBrush");
        Loaded += (_, _) => how.Text = "To leave: tap the screen or quickly press " + LeaveButtonsName(Host?.Settings.CloudLeaveButtons ?? "view+start") +
                                       " for the leave menu, or " + HowToLeave + ".";
        how.Margin = new Thickness(0, 12, 0, 0);
        how.HorizontalAlignment = HorizontalAlignment.Center;
        waiting.Children.Add(how);
        root.Children.Add(waiting);
        root.Children.Add(_web);
        Content = root;
        // Keyboard shortcut: the page passes Ctrl/Alt key presses on to SquareDeck.
        _web.PreviewKeyDown += (_, e) => CheckKeys(e);
        PreviewKeyDown += (_, e) => CheckKeys(e);
    }

    private void CheckKeys(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (LeaveKeyPressed(key, Keyboard.Modifiers, Host?.Settings.CloudLeaveKey ?? "ctrl+shift+q"))
        {
            e.Handled = true;
            Exit();
        }
    }

    public override void FocusDefault() => _web.Focus();

    public override void OnShown()
    {
        Active = this;
        if (!_started)
        {
            _started = true;
            _openedAt = DateTime.UtcNow;
            _introHost.Children.Add(CloudIntro.Build(Host.Settings.CloudIntro, Service, Service.Face(180, 180, 20)));
            _ = StartAsync();
        }
        else if (_web.CoreWebView2 != null) _web.Visibility = Visibility.Visible;
        _corner ??= new CloudCorner(this, Host.Settings.CloudCorner);
        _corner.Show();
        Dispatcher.BeginInvoke(() => _web.Focus(), DispatcherPriority.Input);
        Dispatcher.BeginInvoke(() => Host.RefreshBackgroundPlayback(), DispatcherPriority.Background); // menu music and moving background pause
    }

    // The page is a window of its own: it steps aside for anything drawn over it (a question, for instance).
    public override void OnCovered()
    {
        _web.Visibility = Visibility.Hidden;
        _corner?.Hide();
        if (Active == this) Active = null;
    }

    public override void OnResumed()
    {
        if (_closing) return; // on its way out: don't bring the page (or its corner button) back
        Active = this;
        if (_web.CoreWebView2 != null) _web.Visibility = Visibility.Visible;
        _corner?.Show();
        Dispatcher.BeginInvoke(() => _web.Focus(), DispatcherPriority.Input);
    }

    public override void OnClosed()
    {
        if (Active == this) Active = null;
        _corner?.Close();
        _corner = null;
        ReleasePage();
        Dispatcher.BeginInvoke(() => Host.RefreshBackgroundPlayback(), DispatcherPriority.Background); // and pick up again
    }

    private bool _released;

    /// <summary>
    /// Lets go of the page without holding up SquareDeck. Disposing the web view straight away, while it still had the
    /// keyboard and a stream playing, could hang SquareDeck's whole window (closing a cloud game from the Game Bar
    /// widget froze it). So the stream is stopped and the page blanked first, the view leaves the screen, and it's
    /// disposed a moment later once SquareDeck is idle.
    /// </summary>
    private void ReleasePage()
    {
        if (_released) return;
        _released = true;
        var web = _web;
        try
        {
            if (web.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            web.Visibility = Visibility.Hidden;
            if (web.CoreWebView2 is { } core)
            {
                core.Stop();
                core.Navigate("about:blank");
            }
        }
        catch (Exception) { }
        Dispatcher.BeginInvoke(() =>
        {
            try { (web.Parent as Panel)?.Children.Remove(web); } catch (Exception) { }
            Dispatcher.BeginInvoke(() => { try { web.Dispose(); } catch (Exception) { } }, DispatcherPriority.ApplicationIdle);
        }, DispatcherPriority.Background);
    }

    /// <summary>Shows the page, once the launch animation has had its two and a half seconds.</summary>
    private async void Reveal()
    {
        if (_revealed || _closing || _web.CoreWebView2 == null) return;
        _revealed = true;
        var left = TimeSpan.FromMilliseconds(CloudIntro.Length) - (DateTime.UtcNow - _openedAt);
        if (left > TimeSpan.Zero) await Task.Delay(left);
        if (_closing) return;
        _status.Text = "";
        // The page is its own window and can't fade, so the animation fades out over it first.
        _introHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        await Task.Delay(250);
        if (_closing || !Host.IsTop(this)) return;
        _web.Visibility = Visibility.Visible;
        _web.Focus();
    }

    private async Task StartAsync()
    {
        try
        {
            // Own sign-in folder; sound and video may start without a click; the page may go full screen itself.
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "CloudGaming"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await _web.EnsureCoreWebView2Async(await _environment);
            if (_closing) return;
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            // Sign-ins and links that open a new window stay in this one.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };
            // The page takes over as soon as it has something to show (its first content), not when every last
            // script and picture has loaded, which on these sites can take twenty seconds.
            core.DOMContentLoaded += (_, _) => Reveal();
            _web.NavigationCompleted += (_, _) => Reveal();
            _web.Source = new Uri(Service.Url);
            // And whatever happens, no more than six seconds behind the animation.
            _ = Task.Delay(6000).ContinueWith(_ => Dispatcher.BeginInvoke(Reveal));
        }
        catch (Exception ex)
        {
            _environment = null;
            _status.Text = Service.Name + " couldn't open. It needs the Microsoft Edge WebView2 Runtime (it comes with Windows 11).\n\n" + ex.Message;
        }
    }

    /// <summary>
    /// Called by the launcher about 60 times a second while this is in front, instead of its own controller handling:
    /// everything goes to the page except the hold-to-exit combo.
    /// </summary>
    public void PollController(Gamepad pad)
    {
        var raw = pad.GetRaw();
        _corner?.PanelInput(raw); // left / right, A and B while the leave panel is open
        bool combo = raw.Connected && LeaveHeld(raw, Host.Settings.CloudLeaveButtons);
        if (!combo)
        {
            // A quick press of the leave buttons (let go before the hold finished) opens the leave panel.
            if (_comboSince is { } began && (DateTime.UtcNow - began).TotalSeconds < HoldSeconds) _corner?.Open();
            _comboSince = null;
            _corner?.Progress(0);
            return;
        }
        _comboSince ??= DateTime.UtcNow;
        double held = (DateTime.UtcNow - _comboSince.Value).TotalSeconds;
        _corner?.Progress(Math.Min(1, held / HoldSeconds));
        if (held >= HoldSeconds) { _comboSince = null; Exit(); }
    }

    // Nothing reaches here while the page has the controller, but keep keyboard B / Esc in the page too.
    public override bool OnAction(NavAction action) => true;

    public void Reload() => _web.CoreWebView2?.Reload();

    public void Exit()
    {
        if (_closing) return;
        _closing = true;
        _corner?.Close();
        _corner = null;
        // The stream stops and the page lets go of the keyboard before the screen closes (see ReleasePage).
        ReleasePage();
        Dispatcher.BeginInvoke(() =>
        {
            if (Host != null && Host.IsOpen(this)) Host.Pop(this);
            UiSound.Back();
        }, DispatcherPriority.Input);
    }

    /// <summary>Back to SquareDeck's home without closing the game? Not offered: the stream would time out anyway.</summary>
    public string HowToLeave
    {
        get
        {
            var buttons = "hold " + LeaveButtonsName(Host?.Settings.CloudLeaveButtons ?? "view+start");
            var key = Host?.Settings.CloudLeaveKey ?? "ctrl+shift+q";
            return key == "none" ? buttons : buttons + ", or press " + LeaveKeyName(key);
        }
    }
}

/// <summary>
/// Leaving a cloud game, made easy (8.5.23). A Switch 2 style panel drops in at the top of the screen with Leave,
/// Reload and Keep playing whenever you tap the screen, move the mouse to the top edge, tap the little dot in the
/// corner, or give the leave buttons a quick press (holding them still leaves right away, filling the panel's bar).
/// With the controller, left / right moves between the panel's buttons, A presses one and B closes the panel. It
/// never takes the focus from the game, lets taps through to it, and slides away by itself after a few seconds. These
/// are windows of their own, since nothing can be drawn over the page itself.
/// </summary>
public sealed class CloudCorner : Window
{
    private readonly CloudGameScreen _screen;
    private readonly string _corner;
    private readonly Border _dot, _ring;
    private readonly ScaleTransform _ringScale = new(0, 0);
    private readonly DispatcherTimer _onTop = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly CloudLeavePanel _panel;
    private IntPtr _hook;
    private LowLevelMouseProc? _hookProc; // kept alive while the hook is in place
    private DateTime _lastPop;

    public CloudCorner(CloudGameScreen screen, string corner)
    {
        _screen = screen;
        _corner = corner is "top-left" or "bottom-left" or "bottom-right" ? corner : "top-right";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        // The dot: a faint ring that fills up while the leave buttons are held; tapping it opens the panel.
        _ring = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x4D, 0x4D)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _ringScale };
        var dotInner = new Grid();
        dotInner.Children.Add(_ring);
        dotInner.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _dot = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = dotInner, Opacity = 0.18, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x90, 0x10, 0x10, 0x14)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
        };
        _dot.MouseEnter += (_, _) => _dot.Opacity = 0.9;
        _dot.MouseLeave += (_, _) => _dot.Opacity = 0.18;
        _dot.MouseLeftButtonUp += (_, _) => Open();
        _dot.TouchUp += (_, _) => Open();
        Content = new Grid { Margin = new Thickness(10), Children = { _dot } };

        _panel = new CloudLeavePanel(screen);
        _onTop.Tick += (_, _) => WindowActivator.KeepOnTop(this);
        SourceInitialized += (_, _) => CloudLeavePanel.NoActivate(this);
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) => { Place(); _onTop.Start(); InstallHook(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _panel.HidePanel(); };
        Closed += (_, _) => { _onTop.Stop(); RemoveHook(); _panel.Close(); };
    }

    /// <summary>Opens the leave panel.</summary>
    public void Open()
    {
        if (!IsVisible) return;
        _panel.ShowPanel();
    }

    public bool PanelOpen => _panel.IsOpen;

    /// <summary>The controller while the panel is open: left / right, A and B (others go to the game as usual).</summary>
    public void PanelInput(Gamepad.RawState raw) => _panel.Input(raw);

    /// <summary>0 to 1 while the leave buttons are held: the dot and the panel's bar fill up.</summary>
    public void Progress(double part)
    {
        _ringScale.ScaleX = _ringScale.ScaleY = part;
        _dot.Opacity = part > 0 ? 0.95 : _dot.IsMouseOver ? 0.9 : 0.18;
        if (part > 0.15) _panel.ShowPanel(holding: true);
        _panel.Progress(part);
    }

    private void Place()
    {
        double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
        Left = _corner.EndsWith("left") ? 0 : w - ActualWidth;
        Top = _corner.StartsWith("top") ? 0 : h - ActualHeight;
    }

    // ---- taps on the screen (and the mouse at the top edge), seen without getting in the game's way

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MouseHookData { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint thread);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        try
        {
            _hookProc = HookCallback;
            _hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _hookProc, GetModuleHandle(null), 0);
        }
        catch (Exception) { _hook = IntPtr.Zero; }
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        try { UnhookWindowsHookEx(_hook); } catch (Exception) { }
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MouseHookData>(data);
                int msg = message.ToInt32();
                bool touch = ((ulong)info.ExtraInfo & 0xFFFFFF00) == 0xFF515700; // Windows marks mouse messages made from touch
                const int ButtonUp = 0x0202, Move = 0x0200;
                if ((msg == ButtonUp && touch) || (msg == Move && !touch && info.Y <= 1))
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastPop).TotalMilliseconds > 400)
                    {
                        _lastPop = now;
                        Dispatcher.BeginInvoke(() => { if (!_panel.Contains(info.X, info.Y)) Open(); });
                    }
                }
            }
        }
        catch (Exception) { }
        return CallNextHookEx(_hook, code, message, data);
    }
}

/// <summary>The leave panel itself: a floating Switch 2 style card at the top of the screen.</summary>
public sealed class CloudLeavePanel : Window
{
    private readonly CloudGameScreen _screen;
    private readonly List<Border> _buttons = new();
    private readonly List<Action> _actions = new();
    private readonly Border _card, _fill;
    private readonly TranslateTransform _slide = new(0, -40);
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _onTop = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private int _selected = 2; // Keep playing, so a stray A doesn't end the game
    private ushort _lastButtons;
    private bool _open;

    public bool IsOpen => _open;

    public CloudLeavePanel(CloudGameScreen screen)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Switch2Settings.ApplyColors(this);

        // The service and how long it's been on, then the three buttons, then the hold bar and how to leave.
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 16) };
        var face = screen.Service.Face(44, 44, 12);
        face.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(face);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(screen.Service.Name, 20, "FgBrush"));
        var since = Ui.Text("", 14, "SubtleBrush");
        titles.Children.Add(since);
        head.Children.Add(titles);
        IsVisibleChanged += (_, _) =>
        {
            int minutes = (int)(DateTime.Now - screen.OpenedAt).TotalMinutes;
            since.Text = minutes < 1 ? "Playing now" : minutes == 1 ? "Playing for 1 minute" : $"Playing for {minutes} minutes";
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton(row, "", "Leave", () => _screen.Exit(), danger: true);
        AddButton(row, "", "Reload", () => { _screen.Reload(); HidePanel(); });
        AddButton(row, "", "Keep playing", HidePanel);

        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(6, 14, 6, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "S2Line");
        _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x3C)) };
        var bar = new Grid();
        bar.Children.Add(track);
        bar.Children.Add(_fill);
        _fill.Margin = track.Margin;

        string combo = CloudGameScreen.LeaveButtonsName(screen.Host?.Settings.CloudLeaveButtons ?? "view+start");
        var how = Ui.Text($"Tap the screen or quickly press {combo} to bring this back  ·  Hold {combo} to leave right away", 13, "SubtleBrush");
        how.Margin = new Thickness(6, 10, 6, 0);
        how.TextWrapping = TextWrapping.Wrap;
        how.MaxWidth = 560;

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(row);
        stack.Children.Add(bar);
        stack.Children.Add(how);
        _card = new Border
        {
            Child = stack, Padding = new Thickness(22, 18, 22, 16), CornerRadius = new CornerRadius(18), Margin = new Thickness(20),
            Background = new SolidColorBrush(Switch2Settings.Card), RenderTransform = _slide,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 }
        };
        Content = _card;

        _hide.Tick += (_, _) => HidePanel();
        _onTop.Tick += (_, _) => WindowActivator.KeepOnTop(this);
        SourceInitialized += (_, _) => NoActivate(this);
        SizeChanged += (_, _) => Place();
        Select(_selected);
    }

    private void AddButton(Panel row, string glyph, string label, Action click, bool danger = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var g = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 16, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = label, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
        var fg = danger ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)) : new SolidColorBrush(Switch2Settings.Text);
        g.Foreground = t.Foreground = fg;
        content.Children.Add(g);
        content.Children.Add(t);
        var b = new Border
        {
            Child = content, Padding = new Thickness(20, 12, 22, 13), Margin = new Thickness(5), CornerRadius = new CornerRadius(26), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Background = new SolidColorBrush(Switch2Settings.Line)
        };
        int index = _buttons.Count;
        b.MouseEnter += (_, _) => { Select(index); KeepOpen(); };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        b.TouchUp += (_, e) => { e.Handled = true; click(); };
        _buttons.Add(b);
        _actions.Add(click);
        row.Children.Add(b);
    }

    /// <summary>The highlighted button: the Switch 2 rim and glow.</summary>
    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _buttons.Count - 1);
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == _selected;
            _buttons[i].BorderBrush = on ? Switch2Settings.RimBrush : Brushes.Transparent;
            _buttons[i].Background = new SolidColorBrush(on ? Switch2Settings.Card : Switch2Settings.Line);
            _buttons[i].Effect = on && !Perf.Active ? new System.Windows.Media.Effects.DropShadowEffect { Color = Switch2Settings.Blue, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5 } : null;
        }
    }

    private void KeepOpen()
    {
        _hide.Stop();
        _hide.Start();
    }

    public void ShowPanel(bool holding = false)
    {
        KeepOpen();
        if (_open) return;
        _open = true;
        Select(2);
        Show();
        _onTop.Start();
        Place();
        if (Theme.ReduceMotion) { _slide.Y = 0; _card.Opacity = 1; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-40, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        UiSound.Toast();
    }

    public void HidePanel()
    {
        _hide.Stop();
        if (!_open) return;
        _open = false;
        _onTop.Stop();
        Progress(0);
        if (Theme.ReduceMotion || !IsVisible) { Hide(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { if (!_open) Hide(); };
        _card.BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-30, TimeSpan.FromMilliseconds(160)));
    }

    public void Progress(double part)
    {
        if (_card.Child is not StackPanel || ActualWidth <= 0) { _fill.Width = 0; return; }
        _fill.Width = Math.Max(0, (_card.ActualWidth - 44 - 12) * Math.Clamp(part, 0, 1));
        if (part > 0) KeepOpen();
    }

    /// <summary>The controller while it's open: left / right moves, A presses, B closes.</summary>
    public void Input(Gamepad.RawState raw)
    {
        const ushort Left = 0x0004, Right = 0x0008, A = 0x1000, B = 0x2000;
        ushort pressed = (ushort)(raw.Buttons & ~_lastButtons);
        _lastButtons = raw.Buttons;
        if (!_open) return;
        if ((pressed & Left) != 0) { Select(_selected - 1); KeepOpen(); UiSound.Move(); }
        else if ((pressed & Right) != 0) { Select(_selected + 1); KeepOpen(); UiSound.Move(); }
        else if ((pressed & A) != 0) { UiSound.Select(); _actions[_selected](); }
        else if ((pressed & B) != 0) HidePanel();
    }

    /// <summary>Whether a screen point (in pixels) is on the panel, so a tap on it isn't taken as "open the panel".</summary>
    public bool Contains(int x, int y)
    {
        if (!_open || !IsVisible) return false;
        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X && y >= topLeft.Y && y <= bottomRight.Y;
        }
        catch (Exception) { return false; }
    }

    private void Place()
    {
        double w = SystemParameters.PrimaryScreenWidth;
        Left = (w - ActualWidth) / 2;
        Top = 0;
    }

    /// <summary>Never takes the focus (or the controller) from the game, and stays off Alt + Tab.</summary>
    public static void NoActivate(Window window)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            const int ExStyle = -20, NoActivateFlag = 0x08000000, ToolWindow = 0x80;
            SetWindowLong(handle, ExStyle, GetWindowLong(handle, ExStyle) | NoActivateFlag | ToolWindow);
        }
        catch (Exception) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
