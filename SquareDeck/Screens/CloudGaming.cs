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
        Loaded += (_, _) => how.Text = "To leave: " + HowToLeave + ", or use the small button in the corner.";
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
        try { _web.Dispose(); } catch (Exception) { }
        Dispatcher.BeginInvoke(() => Host.RefreshBackgroundPlayback(), DispatcherPriority.Background); // and pick up again
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
        bool combo = raw.Connected && LeaveHeld(raw, Host.Settings.CloudLeaveButtons);
        if (!combo) { _comboSince = null; _corner?.Progress(0); return; }
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
        Host.Pop(this);
        UiSound.Back();
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
/// The "stealthy" button for leaving a cloud game: a tiny, nearly see-through dot in a corner that never takes the
/// focus from the game. Tap or click it (or move the mouse onto it) and it opens into Exit, Reload and Hide; it also
/// fills up while the leave buttons are held. It's a window of its own, since nothing can be drawn over the page itself.
/// </summary>
public sealed class CloudCorner : Window
{
    private readonly CloudGameScreen _screen;
    private readonly string _corner;
    private readonly Border _dot, _menu, _ring;
    private readonly ScaleTransform _ringScale = new(0, 0);
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _onTop = new() { Interval = TimeSpan.FromMilliseconds(800) };

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

        // The dot: a faint ring that fills up while the exit combo is held.
        _ringScale = new ScaleTransform(0, 0);
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

        // The menu it opens into.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Button("", "Exit", () => _screen.Exit(), danger: true));
        row.Children.Add(Button("", "Reload", () => { _screen.Reload(); Collapse(); }));
        row.Children.Add(Button("", "Hide", Collapse));
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock { Text = screen.HowToLeave, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(6, 6, 6, 0) });
        _menu = new Border
        {
            Child = stack, Padding = new Thickness(8), CornerRadius = new CornerRadius(14), Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x16, 0x16, 0x1C)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
        };
        var root = new Grid { Margin = new Thickness(10) };
        root.Children.Add(_dot);
        root.Children.Add(_menu);
        Content = root;

        _collapse.Tick += (_, _) => Collapse();
        _onTop.Tick += (_, _) => WindowActivator.KeepOnTop(this);
        SourceInitialized += (_, _) => NoActivate();
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) => { Place(); _onTop.Start(); };
        Closed += (_, _) => { _onTop.Stop(); _collapse.Stop(); };
    }

    private Border Button(string glyph, string label, Action click, bool danger = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 14, Foreground = Brushes.White, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            Child = content, Padding = new Thickness(14, 8, 16, 9), Margin = new Thickness(3), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(danger ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        b.TouchUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private void Open()
    {
        _dot.Visibility = Visibility.Collapsed;
        _menu.Visibility = Visibility.Visible;
        _collapse.Stop();
        _collapse.Start();
    }

    private void Collapse()
    {
        _collapse.Stop();
        _menu.Visibility = Visibility.Collapsed;
        _dot.Visibility = Visibility.Visible;
    }

    /// <summary>0 to 1 while the leave buttons are held: the dot shows itself and fills up.</summary>
    public void Progress(double part)
    {
        _ringScale.ScaleX = _ringScale.ScaleY = part;
        _dot.Opacity = part > 0 ? 0.95 : _dot.IsMouseOver ? 0.9 : 0.18;
    }

    private void Place()
    {
        double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
        Left = _corner.EndsWith("left") ? 0 : w - ActualWidth;
        Top = _corner.StartsWith("top") ? 0 : h - ActualHeight;
    }

    private void NoActivate()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            const int ExStyle = -20, NoActivateFlag = 0x08000000, ToolWindow = 0x80;
            SetWindowLong(handle, ExStyle, GetWindowLong(handle, ExStyle) | NoActivateFlag | ToolWindow);
        }
        catch (Exception) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
