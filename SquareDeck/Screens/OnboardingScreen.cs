using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The first time SquareDeck opens: a full-screen, one-thing-at-a-time setup over a slowly drifting aurora.
/// Hello (in a few languages) → your device (the detected ROG Ally, with its picture and a few one-tap settings) → you
/// (picture and greeting) → a style (the favorites first, applied live, and every other one a press away) →
/// background → controller → games (found covers pop in) → extras → done (confetti). Steps slide left and right;
/// dots along the top show where you are. Every choice applies straight away, "Use recommended settings" skips to
/// the end, and all of it can be changed in Settings or run again from Settings > Updates, backup and reset.
/// </summary>
public sealed class OnboardingScreen : Screen
{
    /// <summary>The steps, by id. "Your device" is only there on a recognized handheld.</summary>
    private readonly (string Id, string Name)[] Steps;

    /// <summary>The styles offered first, in this order (the rest are behind "Show every style").</summary>
    private static readonly string[] FeaturedStyles =
        { "switch", "frosted-dark", "first-light", "plumbers-holiday", "the-view", "adv-retro-windows", "adv-tropical-night", "adv-cozy-place", "adv-meridian" };
    private bool _allStyles;
    private Grid? _root;
    private static readonly string[] Hellos = { "Hello", "Hola", "Bonjour", "Olá", "Ciao", "Hallo", "こんにちは", "안녕하세요", "Привет", "你好" };
    private static readonly string[] SuggestedBackgrounds = { "", "Blue Wave", "Black Wave", "Green Blades", "Lofi City", "Night River", "La Fuente", "Analog" };

    private readonly TaskCompletionSource _done;
    private readonly Grid _stage = new() { ClipToBounds = false };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back, _next, _skip;
    private readonly Canvas _sky = new() { IsHitTestVisible = false };
    private readonly System.Windows.Threading.DispatcherTimer _helloTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private int _step, _styleIndex = -1, _helloIndex;
    private bool _startOtherStores;
    private Button? _carousel;

    /// <summary>Experimental "Switch 2 style first-time setup": the steps down the left, the page on the right, thin lines.</summary>
    private readonly bool _s2 = Labs.On("s2setup");
    private readonly StackPanel _s2Nav = new() { Margin = new Thickness(0, 6, 0, 10) };

    public OnboardingScreen(TaskCompletionSource done)
    {
        _done = done;
        var steps = new List<(string, string)> { ("hello", "Hello") };
        if (Handheld.IsHandheld) steps.Add(("device", "Your device"));
        steps.AddRange(new[] { ("you", "You"), ("look", "Style"), ("background", "Background"), ("controller", "Controller"),
                               ("games", "Games"), ("extras", "Extras"), ("ready", "Ready") });
        Steps = steps.ToArray();
        _back = Ui.PanelButton("", "Back");
        _back.Click += (_, _) => Go(_step - 1);
        _skip = Ui.PanelButton("Skip setup");
        _skip.Click += (_, _) => Finish();
        _next = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Next", MinWidth = 220 };
        _next.Click += (_, _) => Advance();

        var root = new Grid { ClipToBounds = true };
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.Children.Add(_sky);
        // A faint dotted grid over the aurora.
        var dots = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), null, new EllipseGeometry(new Point(1, 1), 1, 1)))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 28, 28), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 28, 28), ViewboxUnits = BrushMappingMode.Absolute
        };
        root.Children.Add(new Rectangle { Fill = dots, IsHitTestVisible = false });

        var layout = new Grid { Margin = new Thickness(56, 30, 56, 26) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top: the mark, where you are, and Skip.
        var top = new Grid { Height = 52 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Logo(34));
        var name = Ui.Text("SquareDeck", 19, "FgBrush", bold: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(12, 0, 0, 0);
        brand.Children.Add(name);
        top.Children.Add(brand);
        top.Children.Add(_dots);
        _skip.HorizontalAlignment = HorizontalAlignment.Right;
        _skip.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_skip);
        layout.Children.Add(top);

        Grid.SetRow(_stage, 1);
        layout.Children.Add(_stage);

        var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(_back);
        var hints = new HintBar("{Select}  Choose      {LB}  Back      {RB}  Next") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
        bottom.Children.Add(hints);
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.Children.Add(_next);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(bottom);
        root.Children.Add(layout);
        Content = root;

        _helloTimer.Tick += (_, _) => NextHello();
        _root = root;
        root.Loaded += (_, _) => StartAurora(root);
        if (_s2) BuildSwitch2Frame();
    }

    public override void OnShown()
    {
        _startOtherStores = Host.Settings.OtherStores;
        Go(0);
    }

    public override void OnClosed() => _helloTimer.Stop();

    public override bool OnAction(NavAction action)
    {
        // The style carousel: left / right browse while it's highlighted.
        if (_carousel != null && ReferenceEquals(Keyboard.FocusedElement, _carousel) && action is NavAction.Left or NavAction.Right)
        {
            StepStyle(action == NavAction.Right ? 1 : -1);
            return true;
        }
        switch (action)
        {
            case NavAction.Back:
                if (_step > 0) Go(_step - 1);
                return true; // never leaves by accident; Skip setup is a button
            case NavAction.Start:
            case NavAction.RB:
                Advance();
                return true;
            case NavAction.LB:
                if (_step > 0) Go(_step - 1);
                return true;
        }
        return false;
    }

    private void Advance()
    {
        if (_step == Steps.Length - 1) Finish(); else Go(_step + 1);
    }

    private void Finish()
    {
        var s = Host.Settings;
        s.SetupDone = true;
        s.Save();
        Host.ApplySettings();
        Host.Pop(this);
        if (s.OtherStores != _startOtherStores) _ = Host.ReloadAsync(waitForArt: false); // the other launchers' games come or go
        _done.TrySetResult();
    }

    // ================================================================ frame

    /// <summary>Three big soft color blobs drifting behind everything, in the highlight color and two neighbors of it.</summary>
    private void StartAurora(Grid root)
    {
        if (_s2) return;
        _sky.Children.Clear();
        double w = Math.Max(root.ActualWidth, 1280), h = Math.Max(root.ActualHeight, 720);
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x2E, 0x9B, 0xFF);
        Color Shift(Color c, double hue)
        {
            // Rotate the hue a little for companions to the accent.
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min, hh = 0, ss = 0;
            if (d > 0)
            {
                ss = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                hh = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                hh /= 6;
            }
            hh = (hh + hue + 1) % 1;
            double Q(double p, double q, double t) { t = (t + 1) % 1; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
            double qq = l < 0.5 ? l * (1 + ss) : l + ss - l * ss, pp = 2 * l - qq;
            return Color.FromRgb((byte)(Q(pp, qq, hh + 1.0 / 3) * 255), (byte)(Q(pp, qq, hh) * 255), (byte)(Q(pp, qq, hh - 1.0 / 3) * 255));
        }
        var blobs = new[] { (accent, 0.0, 0.0), (Shift(accent, 0.12), 0.55, 0.15), (Shift(accent, -0.15), 0.25, 0.6) };
        int i = 0;
        foreach (var (color, fx, fy) in blobs)
        {
            double size = Math.Max(w, h) * 0.75;
            var blob = new Ellipse
            {
                Width = size, Height = size, Opacity = 0.30,
                Fill = new RadialGradientBrush(Color.FromArgb(0xC0, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B))
            };
            Canvas.SetLeft(blob, w * fx - size * 0.25);
            Canvas.SetTop(blob, h * fy - size * 0.3);
            _sky.Children.Add(blob);
            if (!Theme.ReduceMotion && !Perf.Active)
            {
                var drift = new TranslateTransform();
                blob.RenderTransform = drift;
                double dx = (i % 2 == 0 ? 1 : -1) * w * 0.12, dy = (i == 1 ? -1 : 1) * h * 0.1;
                drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(13 + i * 4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(11 + i * 3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }
            i++;
        }
    }

    private void BuildDots()
    {
        _dots.Children.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            bool current = i == _step, done = i < _step;
            var dot = new Border { Height = 10, Width = current ? 38 : 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(4, 0, 4, 0), Opacity = current || done ? 1 : 0.3, ToolTip = Steps[i].Name, Cursor = Cursors.Hand };
            dot.SetResourceReference(Border.BackgroundProperty, current || done ? "AccentBrush" : "FgBrush");
            int index = i;
            dot.MouseLeftButtonUp += (_, _) => Go(index);
            _dots.Children.Add(dot);
        }
        if (_s2) BuildSwitch2Nav();
    }

    /// <param name="keep">Redrawing the same step after a choice: no slide, and the highlight stays on what was picked.</param>
    private void Go(int step, bool keep = false)
    {
        int from = _step;
        _step = Math.Clamp(step, 0, Steps.Length - 1);
        _helloTimer.Stop();
        _carousel = null;
        BuildDots();
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _skip.Visibility = _step == Steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
        _next.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _step == Steps.Length - 1 ? "Start playing" : "Next";

        FrameworkElement page = Steps[_step].Id switch
        {
            "hello" => HelloPage(),
            "device" => DevicePage(),
            "you" => YouPage(),
            "look" => LookPage(),
            "background" => BackgroundPage(),
            "controller" => ControllerPage(),
            "games" => GamesPage(),
            "extras" => ExtrasPage(),
            _ => ReadyPage()
        };

        // Slide: forward pages come in from the right, back from the left; the old page slides out the other way.
        var old = _stage.Children.OfType<FrameworkElement>().LastOrDefault();
        if (!keep && !Theme.ReduceMotion && old != null)
        {
            double dir = _step >= from ? 1 : -1;
            var outMove = new TranslateTransform();
            old.RenderTransform = outMove;
            old.IsHitTestVisible = false;
            outMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -120 * dir, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            old.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
            var gone = old;
            _ = Task.Delay(280).ContinueWith(_ => _stage.Children.Remove(gone), TaskScheduler.FromCurrentSynchronizationContext());
            var inMove = new TranslateTransform(160 * dir, 0);
            page.RenderTransform = inMove;
            page.Opacity = 0;
            inMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(160 * dir, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(80) });
        }
        else _stage.Children.Clear();
        _stage.Children.Add(page);
        if (_s2) Switch2Polish(page);

        var buttons = page.FindDescendants<Button>().Where(b => b.Focusable).ToList();
        FocusLater(_step == Steps.Length - 1 ? _next : _carousel ?? buttons.FirstOrDefault(b => b.Tag as string == "current") ?? buttons.FirstOrDefault() ?? _next);
        if (!keep) UiSound.Move();
    }

    // ================================================================ Switch 2 style

    /// <summary>
    /// The setup drawn like the Switch 2's System Settings, in the UI style's colors: "System Setup" with the gear and a
    /// line under it, every step listed down the left (a blue bar on the one you're on, a tick on the ones done), the
    /// page on the right, and a line over Back, the hints, Skip and Next.
    /// </summary>
    private void BuildSwitch2Frame()
    {
        Switch2Settings.Refresh();
        Switch2Settings.ApplyColors(this);
        // The cards and toggles on the pages take the Switch 2 colors too.
        SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Resources["TileBrush"] = B(Switch2Settings.Card);
        Resources["DialogBrush"] = B(Switch2Settings.Card);
        Resources["CircleBrush"] = B(Switch2Settings.Off);
        Resources["PanelStroke"] = B(Switch2Settings.Line);

        foreach (var e in new FrameworkElement[] { _stage, _back, _next, _skip, _dots })
            (e.Parent as Panel)?.Children.Remove(e);

        var root = new Grid { ClipToBounds = true, Background = B(Switch2Settings.Background) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

        var head = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(78, 26, 62, 0) };
        var gear = Ui.Glyph("\uE713", 34, "FgBrush");
        gear.VerticalAlignment = VerticalAlignment.Center;
        gear.Margin = new Thickness(0, 2, 16, 0);
        head.Children.Add(gear);
        var title = Ui.Text("System Setup", 34, "FgBrush");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        root.Children.Add(head);
        var under = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 0) };
        under.SetResourceReference(Shape.FillProperty, "S2Line");
        root.Children.Add(under);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 26, 62, 0) };
        brand.Children.Add(Logo(28));
        var name = Ui.Text("SquareDeck", 17, "SubtleBrush");
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(name);
        root.Children.Add(brand);

        _stage.Margin = new Thickness(0, 6, 18, 0);
        var body = Switch2Pages.Shell(_s2Nav, _stage);
        body.Margin = new Thickness(26, 4, 26, 6);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var foot = new Grid { Margin = new Thickness(40, 0, 40, 0) };
        var over = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        over.SetResourceReference(Shape.FillProperty, "S2Line");
        foot.Children.Add(over);
        FrameworkElement pad = Ui.DeviceArt(30) ?? Ui.Glyph(Ui.HandheldGlyph, 30, "FgBrush");
        pad.HorizontalAlignment = HorizontalAlignment.Left;
        pad.VerticalAlignment = VerticalAlignment.Center;
        pad.Margin = new Thickness(16, 2, 0, 0);
        foot.Children.Add(pad);
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _back.VerticalAlignment = VerticalAlignment.Center;
        _back.Margin = new Thickness(64, 0, 0, 0);
        foot.Children.Add(_back);
        var hints = new HintBar("{Select}  Choose      {LB}  Back      {RB}  Next") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        foot.Children.Add(hints);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _skip.HorizontalAlignment = HorizontalAlignment.Left;
        _skip.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(_skip);
        _next.HorizontalAlignment = HorizontalAlignment.Left;
        right.Children.Add(_next);
        foot.Children.Add(right);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);

        _root = root;
        Content = root;
    }

    /// <summary>The steps down the left: done ones ticked, the current one in blue with its bar.</summary>
    private void BuildSwitch2Nav()
    {
        _s2Nav.Children.Clear();
        var count = Ui.Text($"Step {_step + 1} of {Steps.Length}", 15, "SubtleBrush");
        count.Margin = new Thickness(40, 4, 0, 10);
        _s2Nav.Children.Add(count);
        for (int i = 0; i < Steps.Length; i++)
        {
            int index = i;
            // Not in the controller's path (Back / Next move through the steps), but a tap or click goes straight there.
            var item = Switch2Pages.NavItem(Steps[i].Name, i < _step ? "\u2713" : null, i == _step, () => Go(index), focusable: false);
            item.Opacity = i > _step ? 0.6 : 1;
            _s2Nav.Children.Add(item);
        }
        var bar = new Grid { Height = 6, Margin = new Thickness(40, 18, 18, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Switch2Settings.Off) });
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        double part = Steps.Length <= 1 ? 1 : _step / (double)(Steps.Length - 1);
        bar.SizeChanged += (_, e) => fill.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(6, e.NewSize.Width * part), TimeSpan.FromMilliseconds(300)));
        bar.Children.Add(fill);
        _s2Nav.Children.Add(bar);
    }

    /// <summary>A page redrawn the Switch 2 way: lighter titles, bar-and-name section headings, tiles with the rimmed highlight.</summary>
    private static void Switch2Polish(FrameworkElement page)
    {
        foreach (var t in page.FindDescendants<TextBlock>().ToList())
        {
            if (t.FontSize >= 40) { t.FontSize = 34; t.FontWeight = FontWeights.Normal; t.HorizontalAlignment = HorizontalAlignment.Left; t.TextAlignment = TextAlignment.Left; continue; }
            // The small capitals section titles become "▌ Name".
            if (t.FontSize == 13 && t.FontWeight == FontWeights.Bold && t.Text.Length > 1 && t.Text == t.Text.ToUpperInvariant() && t.Parent is Panel panel && t.Text.Any(char.IsLetter))
            {
                int at = panel.Children.IndexOf(t);
                panel.Children.RemoveAt(at);
                var header = Switch2Settings.Header(t.Text[0] + t.Text[1..].ToLowerInvariant());
                header.Margin = new Thickness(4, 22, 0, 10);
                panel.Children.Insert(at, header);
            }
        }
        // The lead line under the title: left aligned with it.
        foreach (var t in page.FindDescendants<TextBlock>().Where(x => x.FontSize == 18 && x.TextAlignment == TextAlignment.Center).ToList())
        {
            t.TextAlignment = TextAlignment.Left;
            t.HorizontalAlignment = HorizontalAlignment.Left;
        }
        foreach (var column in page.FindDescendants<StackPanel>().Where(p => p.MaxWidth == 1120).ToList())
        {
            column.HorizontalAlignment = HorizontalAlignment.Stretch;
            column.Margin = new Thickness(14, 18, 14, 12);
            column.VerticalAlignment = VerticalAlignment.Top;
        }
        var tile = Ui.StyleOf("TileButton");
        foreach (var b in page.FindDescendants<Button>().Where(b => b.Style == tile).ToList())
        {
            var margin = b.Margin;
            b.Style = Switch2Pages.TileStyle;
            b.Margin = margin;
        }
    }

    // ================================================================ building blocks

    /// <summary>A page: a big title and a line under it, centered over a column of content.</summary>
    private static (Grid Page, StackPanel Body) Page(string title, string lead)
    {
        var grid = new Grid();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
        var column = new StackPanel { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        var t = Ui.Text(title, 46, "FgBrush", bold: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        column.Children.Add(t);
        var l = Ui.Text(lead, 18, "SubtleBrush");
        l.TextWrapping = TextWrapping.Wrap;
        l.TextAlignment = TextAlignment.Center;
        l.MaxWidth = 760;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.Margin = new Thickness(0, 8, 0, 28);
        column.Children.Add(l);
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        column.Children.Add(body);
        scroll.Content = column;
        grid.Children.Add(scroll);
        return (grid, body);
    }

    private static TextBlock Section(string text)
    {
        var t = Ui.Text(text.ToUpperInvariant(), 13, "SubtleBrush", bold: true);
        t.Margin = new Thickness(2, 18, 0, 10);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>A picture card with its name under it. The chosen one carries a tick in the highlight color.</summary>
    private static Button Card(UIElement picture, double width, double height, string name, bool chosen, Action pick, string? sub = null)
    {
        var face = new Grid { Width = width, Height = height, Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16) };
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        face.Children.Add(back);
        face.Children.Add(picture);
        if (chosen)
        {
            var edge = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(16) };
            edge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            face.Children.Add(edge);
            face.Children.Add(Tick(30, new Thickness(0, 10, 10, 0)));
        }
        var card = new StackPanel { Width = width };
        card.Children.Add(face);
        var label = Ui.Text(name, 16, chosen ? "AccentBrush" : "FgBrush", bold: true);
        label.Margin = new Thickness(2, 10, 0, 0);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        card.Children.Add(label);
        if (sub != null)
        {
            var s = Ui.Text(sub, 13, "SubtleBrush");
            s.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(s);
        }
        var b = Ui.Tile(card);
        b.Margin = new Thickness(0, 0, 12, 12);
        if (chosen) b.Tag = "current";
        b.Click += (_, _) => pick();
        return b;
    }

    private static Border Tick(double size, Thickness margin)
    {
        var tick = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = margin };
        tick.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var mark = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        tick.Child = mark;
        return tick;
    }

    /// <summary>A row of pill choices; the chosen one is filled in.</summary>
    private WrapPanel Choices((string Id, string Text)[] options, string current, Action<string> pick)
    {
        var w = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (id, text) in options)
        {
            string o = id;
            bool chosen = current == o;
            var b = chosen ? new Button { Style = Ui.StyleOf("AccentButton"), Content = "✓  " + text } : Ui.PanelButton(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            if (chosen) b.Tag = "current";
            b.Click += (_, _) => { pick(o); Host.Settings.Save(); Go(_step, keep: true); };
            w.Children.Add(b);
        }
        return w;
    }

    /// <summary>A big square toggle card: icon, name, a line about it and On / Off.</summary>
    private Button Toggle(string glyph, string title, string about, bool on, Action flip)
    {
        var face = new Border { Width = 320, Height = 168, CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 18, 20, 16), BorderThickness = new Thickness(2) };
        face.SetResourceReference(Border.BackgroundProperty, on ? "DialogBrush" : "TileBrush");
        face.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "PanelStroke");
        var stack = new DockPanel();
        var head = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        var icon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12) };
        icon.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "CircleBrush");
        var g = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.SetResourceReference(TextBlock.ForegroundProperty, on ? "BgBrush" : "FgBrush");
        icon.Child = g;
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        // The switch.
        var track = new Border { Width = 52, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "CircleBrush");
        var knob = new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(4, 0, 4, 0) };
        track.Child = knob;
        head.Children.Add(track);
        stack.Children.Add(head);
        var words = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        words.Children.Add(Ui.Text(title, 18, "FgBrush", bold: true));
        var a = Ui.Text(about, 14, "SubtleBrush");
        a.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(a);
        stack.Children.Add(words);
        face.Child = stack;
        var b = Ui.Tile(face);
        b.Margin = new Thickness(0, 0, 12, 12);
        b.Click += (_, _) => { flip(); Host.Settings.Save(); Go(_step, keep: true); FocusSame(title); };
        b.Tag = "toggle:" + title;
        return b;
    }

    private void FocusSame(string title) =>
        Dispatcher.BeginInvoke(() =>
        {
            var b = _stage.FindDescendants<Button>().FirstOrDefault(x => x.Tag as string == "toggle:" + title);
            b?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);

    private static Grid Logo(double size)
    {
        // The SquareDeck mark: a rounded square with a frame and a small filled square in its corner.
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Rectangle { RadiusX = size * 0.2, RadiusY = size * 0.2, Fill = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)) });
        var frame = new Rectangle { Width = size * 0.45, Height = size * 0.45, RadiusX = size * 0.06, RadiusY = size * 0.06, StrokeThickness = size * 0.0625 };
        frame.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        g.Children.Add(frame);
        var dot = new Rectangle { Width = size * 0.166, Height = size * 0.166, RadiusX = size * 0.04, RadiusY = size * 0.04, Margin = new Thickness(size * 0.33, size * 0.33, 0, 0) };
        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        g.Children.Add(dot);
        if (size > 60) g.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.4 };
        return g;
    }

    // ================================================================ pages

    private TextBlock? _hello;

    private FrameworkElement HelloPage()
    {
        var grid = new Grid();
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // The logo builds itself: the square pops, the frame spins in, the dot drops into its corner.
        var logo = Logo(150);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(logo);
        if (!Theme.ReduceMotion)
        {
            var scale = new ScaleTransform(0.3, 0.3);
            logo.RenderTransformOrigin = new Point(0.5, 0.5);
            logo.RenderTransform = new TransformGroup { Children = { scale, new RotateTransform() } };
            var pop = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = pop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = pop });
            ((RotateTransform)((TransformGroup)logo.RenderTransform).Children[1]).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-90, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }

        _hello = new TextBlock { Text = Hellos[0], FontSize = 88, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        _hello.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        column.Children.Add(_hello);
        string first = MainWindow.FirstName();
        var lead = Ui.Text((first.Length > 0 ? $"Welcome, {first}. " : "Welcome. ") + "Let's make SquareDeck yours. It takes about a minute.", 20, "SubtleBrush");
        lead.HorizontalAlignment = HorizontalAlignment.Center;
        lead.Margin = new Thickness(0, 6, 0, 40);
        column.Children.Add(lead);

        var start = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Let's go", MinWidth = 300, FontSize = 22, Padding = new Thickness(30, 14, 30, 14), HorizontalAlignment = HorizontalAlignment.Center, Tag = "current" };
        start.Click += (_, _) => Go(1);
        column.Children.Add(start);
        var quick = Ui.PanelButton("", "Use recommended settings");
        quick.HorizontalAlignment = HorizontalAlignment.Center;
        quick.Margin = new Thickness(0, 14, 0, 0);
        quick.Click += (_, _) => Go(Steps.Length - 1);
        column.Children.Add(quick);
        var note = Ui.Text("Everything can be changed later in Settings.", 14, "SubtleBrush");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 14, 0, 0);
        column.Children.Add(note);
        grid.Children.Add(column);

        _helloIndex = 0;
        _helloTimer.Start();
        return grid;
    }

    /// <summary>The greeting changes language: the old word lifts away and the next rises into place.</summary>
    private void NextHello()
    {
        if (_hello == null || _step != 0) { _helloTimer.Stop(); return; }
        _helloIndex = (_helloIndex + 1) % Hellos.Length;
        var word = _hello;
        if (Theme.ReduceMotion) { word.Text = Hellos[_helloIndex]; return; }
        var move = new TranslateTransform();
        word.RenderTransform = move;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            word.Text = Hellos[_helloIndex];
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            word.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        };
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200)));
        word.BeginAnimation(OpacityProperty, fadeOut);
    }

    private FrameworkElement YouPage()
    {
        var s = Host.Settings;
        string first = MainWindow.FirstName();
        var (page, body) = Page("First, you", "Pick a picture for the top of the home screen. A photo of your own can be chosen later in Settings > Profile.");

        // The greeting as it will look, with the chosen picture.
        var preview = new Border { CornerRadius = new CornerRadius(34), Padding = new Thickness(10, 10, 26, 10), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 26), BorderThickness = new Thickness(1.5) };
        preview.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        preview.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Avatars.Build(s.Avatar, 52));
        var hi = Ui.Text(MainWindow.Greeting(DateTime.Now) + (first.Length > 0 ? ", " + first : ""), 22, "FgBrush", bold: true);
        hi.VerticalAlignment = VerticalAlignment.Center;
        hi.Margin = new Thickness(16, 0, 0, 0);
        row.Children.Add(hi);
        row.Children.Add(new TextBlock { Text = " 👋", FontSize = 20, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center });
        preview.Child = row;
        body.Children.Add(preview);

        var pictures = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 900 };
        for (int i = 0; i < 16; i++)
        {
            string id = "preset:" + i;
            bool chosen = s.Avatar == id;
            var face = new Grid();
            face.Children.Add(Avatars.Build(id, 84));
            if (chosen) face.Children.Add(Tick(26, new Thickness(0, -4, -4, 0)));
            var b = Ui.Tile(face);
            b.Margin = new Thickness(0, 0, 6, 6);
            if (chosen) b.Tag = "current";
            b.Click += (_, _) => { s.Avatar = id; s.Save(); Host.ApplyAvatar(); Go(_step, keep: true); };
            pictures.Children.Add(b);
        }
        body.Children.Add(pictures);

        body.Children.Add(Section("Say hello when SquareDeck opens?"));
        body.Children.Add(Choices(new[] { ("on", "Yes, greet me"), ("off", "No thanks") }, s.WelcomeMessage ? "on" : "off", id => s.WelcomeMessage = id == "on"));
        return page;
    }

    private FrameworkElement LookPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("Pick a style", "Each style dresses all of SquareDeck: colors, fonts, sounds, music and a background. It changes right behind this page as you pick.");
        const double W = 330;
        var current = UiStyles.Find(s.UiStyle);

        Button StyleTile(UiStyle st)
        {
            s.SkinVariants.TryGetValue(st.Id, out var variant);
            bool inUse = st.Id == current.Id;
            var tile = st.Skin != null ? SettingsScreen.AdvancedCard(st, variant, inUse, W) : SettingsScreen.StyleCard(st, inUse, W);
            if (inUse) tile.Tag = "current";
            tile.Click += (_, _) =>
            {
                if (st.Id == UiStyles.Find(Host.Settings.UiStyle).Id) return;
                Host.UseStyle(st);
                UiSound.Select();
                if (_root != null) StartAurora(_root); // the aurora takes the new highlight color
                Go(_step, keep: true);
                FocusStyle(st.Id);
            };
            tile.DataContext = "style:" + st.Id;
            return tile;
        }

        var available = UiStyles.All.Concat(UiStyles.Advanced).Where(x => !StyleUnlocks.IsLocked(x)).GroupBy(x => x.Id).Select(g => g.First()).ToList();
        body.Children.Add(Section("Favorites"));
        var featured = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = (W + 10) * 3 + 4 };
        foreach (var id in FeaturedStyles)
            if (available.FirstOrDefault(x => x.Id == id) is { } st) featured.Children.Add(StyleTile(st));
        body.Children.Add(featured);

        // Light or dark: Switch lets you choose; the other styles bring their own.
        if (current.Id == "switch")
        {
            body.Children.Add(Section("Switch: light or dark"));
            body.Children.Add(Choices(new[] { ("system", "Follow Windows"), ("light", "Light"), ("dark", "Dark"), ("oled", "Black (OLED)") }, s.ThemeMode, m =>
            {
                s.ThemeMode = m;
                s.DarkTheme = Theme.ShouldBeDark(s);
                Host.ApplyLook();
            }));
        }

        var rest = available.Where(x => !FeaturedStyles.Contains(x.Id)).ToList();
        if (rest.Count > 0)
        {
            if (!_allStyles)
            {
                var more = Ui.PanelButton("", $"Show every style ({rest.Count} more)");
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.Margin = new Thickness(0, 18, 0, 0);
                more.Click += (_, _) => { _allStyles = true; Go(_step, keep: true); };
                body.Children.Add(more);
            }
            else
            {
                body.Children.Add(Section("Every other style"));
                var all = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = (W + 10) * 3 + 4 };
                foreach (var st in rest) all.Children.Add(StyleTile(st));
                body.Children.Add(all);
            }
        }
        var note = Ui.Text("Your own styles, and the ones you unlock by playing, are in Settings > Themes.", 14, "SubtleBrush");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(note);
        return page;
    }

    private void FocusStyle(string id) =>
        Dispatcher.BeginInvoke(() =>
        {
            var b = _stage.FindDescendants<Button>().FirstOrDefault(x => x.DataContext as string == "style:" + id);
            b?.Focus();
            b?.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// The handheld SquareDeck found: its picture and what's inside, and a few settings that suit it, one tap each.
    /// </summary>
    private FrameworkElement DevicePage()
    {
        var s = Host.Settings;
        bool ally = Handheld.Vendor == HandheldVendor.Asus;
        var (page, body) = Page("Hello, " + Handheld.DisplayName,
            "SquareDeck found your " + Handheld.DisplayName + " and is set up for it. Its power, lights, fans, controller and battery care are all in Settings, in its own section.");

        // The device, floating, with what's inside as chips.
        var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        if (MyDeviceScreen.DevicePhoto(460) is FrameworkElement photo)
        {
            photo.HorizontalAlignment = HorizontalAlignment.Center;
            hero.Children.Add(photo);
        }
        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        void Chip(string glyph, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 14, "AccentBrush");
            g.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(text, 15, "FgBrush"));
            var chip = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 7, 14, 8), Margin = new Thickness(0, 0, 8, 8), Child = row };
            chip.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            chips.Children.Add(chip);
        }
        Chip("", TdpControl.CpuName);
        if (Handheld.Current is { } device) Chip("", $"{device.TdpMin}–{device.TdpMax} W");
        if (DisplaySettings.Current() is { } screen) Chip("", $"{screen.Resolution}  ·  {screen.RefreshRate} Hz");
        if (Battery.GetPercent() is int battery) Chip("", battery + "% battery");
        hero.Children.Add(chips);
        body.Children.Add(hero);

        // Who runs the hardware: SquareDeck, or the maker's own app (so the two don't fight over the same settings).
        body.Children.Add(Section("Who should run its power, lights and buttons?"));
        string owner = s.TdpEnabled || s.AllyRemap || s.AllyLights.Length > 0 ? "squaredeck" : "maker";
        string maker = ally ? "Armoury Crate SE" : "The maker's app";
        body.Children.Add(Choices(new[] { ("squaredeck", "SquareDeck"), ("maker", maker) }, owner, id =>
        {
            bool mine = id == "squaredeck";
            s.TdpEnabled = mine && TdpControl.CanApply;
            if (ally)
            {
                s.AllyRemap = mine;
                if (mine && s.AllyLights.Length == 0) s.AllyLights = "accent";
                if (!mine) s.AllyLights = "";
                AllyControl.ApplyLights(force: true);
                Host.ApplyAllyButtons();
            }
            if (s.TdpEnabled) _ = TdpControl.ApplyAsync(quiet: true);
        }));
        var ownerNote = Ui.Text(owner == "squaredeck"
            ? "SquareDeck sets the TDP, maps the special buttons" + (ally ? " and runs the lights" : "") + ". " + maker + " can stay installed; whichever changes something last wins."
            : maker + " keeps doing it, and SquareDeck leaves those settings alone. You can switch any of them on in Settings later.", 15, "SubtleBrush");
        ownerNote.TextWrapping = TextWrapping.Wrap;
        ownerNote.TextAlignment = TextAlignment.Center;
        ownerNote.MaxWidth = 820;
        ownerNote.Margin = new Thickness(0, 4, 0, 10);
        ownerNote.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(ownerNote);

        body.Children.Add(Section("A few things to start with"));
        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1020 };
        if (ally)
        {
            cards.Children.Add(Toggle("", "Battery care", "Stop charging at 80%, kinder to the battery when it's mostly plugged in", s.AllyChargeLimit is > 0 and < 100, () =>
            {
                s.AllyChargeLimit = s.AllyChargeLimit is > 0 and < 100 ? 0 : 80;
                Task.Run(AllyControl.ApplyBattery);
            }));
            cards.Children.Add(Toggle("", "Lights match SquareDeck", "The rings around the sticks take the style's highlight color", s.AllyLights == "accent", () =>
            {
                s.AllyLights = s.AllyLights == "accent" ? "" : "accent";
                AllyControl.ApplyLights(force: true);
            }));
        }
        cards.Children.Add(Toggle("", "My Device on the home screen", "A button for live temperatures, fans, battery health and power draw", s.ShowMyDeviceButton, () =>
        {
            s.ShowMyDeviceButton = !s.ShowMyDeviceButton;
            Host.RefreshHomeButtonsPublic();
        }));
        body.Children.Add(cards);
        return page;
    }

    private void StepStyle(int by) { } // nothing to step through now: one style

    private FrameworkElement BackgroundPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("A moving background?", "A slow, silent video behind everything. It stops while you play, so it costs nothing in games.");
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1120 };
        var names = SuggestedBackgrounds.ToList();
        if (s.DynamicBackground.Length > 0 && !names.Contains(s.DynamicBackground, StringComparer.OrdinalIgnoreCase)) names.Insert(1, s.DynamicBackground);
        foreach (var name in names)
        {
            string n = name;
            if (n.Length > 0 && MainWindow.DynamicPathFor(n) == null) continue;
            string still = System.IO.Path.Combine(MainWindow.BackgroundsFolder, "previews", n + ".jpg");
            UIElement picture;
            if (n.Length > 0 && File.Exists(still) && ImageLoader.FromFile(still, 480) is { } pic)
                picture = new Image { Source = pic, Stretch = Stretch.UniformToFill };
            else
            {
                var off = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
                off.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                picture = off;
            }
            bool chosen = string.Equals(s.DynamicBackground, n, StringComparison.OrdinalIgnoreCase);
            row.Children.Add(Card(picture, 252, 142, n.Length == 0 ? "None" : n, chosen, () =>
            {
                if (n.Length > 0 && s.DynamicBlur == 0) s.DynamicBlur = 8; // a little softness keeps text readable
                Host.UseDynamicBackground(n);
                Go(_step, keep: true);
            }));
        }
        body.Children.Add(row);
        var more = Ui.Text("More backgrounds, wallpapers and blur: Settings > Appearance.", 14, "SubtleBrush");
        more.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(more);
        return page;
    }

    private FrameworkElement ControllerPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("Which controller?", "So the buttons on screen match the ones in your hands.");
        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (id, name) in Hints.Styles)
        {
            string style = id;
            // A big drawing of that family's four face buttons.
            var art = new Grid();
            var (labels, colors) = id switch
            {
                "playstation" => (new[] { "△", "○", "✕", "□" }, new[] { "#3FC8A8", "#FF5A64", "#6FA8FF", "#E07BD6" }),
                "switch" => (new[] { "X", "A", "B", "Y" }, new[] { "#DADDE3", "#DADDE3", "#DADDE3", "#DADDE3" }),
                _ => (new[] { "Y", "B", "A", "X" }, new[] { "#F2C200", "#E8453C", "#2EB872", "#2B7DE0" })
            };
            var diamond = new Canvas { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var spots = new[] { (50.0, 0.0), (100.0, 50.0), (50.0, 100.0), (0.0, 50.0) };
            for (int i = 0; i < 4; i++)
            {
                var c = (Color)ColorConverter.ConvertFromString(colors[i]);
                var button = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(25), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2C)), BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(2.5),
                                          Child = new TextBlock { Text = labels[i], FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                Canvas.SetLeft(button, spots[i].Item1);
                Canvas.SetTop(button, spots[i].Item2);
                diamond.Children.Add(button);
            }
            art.Children.Add(diamond);
            cards.Children.Add(Card(art, 280, 190, name, s.GlyphStyle == style, () =>
            {
                s.GlyphStyle = style;
                Hints.SetStyle(style);
                // Nintendo controllers select with the right button.
                if (style == "switch" && Hints.Map != "BAYX") Host.SetButtonMap("BAYX");
                else if (style != "switch" && Hints.Map == "BAYX") Host.SetButtonMap("ABXY");
                Host.ApplySettings();
                Go(_step, keep: true);
            }));
        }
        body.Children.Add(cards);
        body.Children.Add(Section("Which button selects"));
        body.Children.Add(Choices(new[] { ("ABXY", "Bottom button"), ("BAYX", "Right button (Nintendo)") }, Hints.Map, id => Host.SetButtonMap(id)));
        body.Children.Add(Section("How buttons are drawn"));
        body.Children.Add(Choices(Hints.Looks, s.GlyphLook, id => { s.GlyphLook = id; Hints.SetLook(id); }));

        var sample = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 12, 22, 12), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        sample.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        sample.Child = new HintBar("{Select}  Select      {Back}  Back      {Details}  Details      {Options}  Options");
        body.Children.Add(sample);
        return page;
    }

    private FrameworkElement GamesPage()
    {
        var s = Host.Settings;
        var games = Host.AllGames.Where(g => g.IsGame).ToList();
        int steam = games.Count(g => g.IsSteam), others = games.Count - steam;
        var (page, body) = Page("Your games", games.Count > 0
            ? (steam > 0 ? $"{steam} from Steam" : "") + (others > 0 ? (steam > 0 ? " and " : "") + $"{others} from elsewhere" : "") + ", ready to play. Covers download by themselves."
            : "Steam games show up by themselves, with their covers. Nothing to do here unless you want more.");

        // The big number counts up, and covers pop in under it.
        var count = Ui.Text("0", 96, "AccentBrush", bold: true);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(count);
        var label = Ui.Text(games.Count == 1 ? "game found" : "games found", 18, "SubtleBrush");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, -6, 0, 18);
        body.Children.Add(label);
        if (games.Count > 0 && !Theme.ReduceMotion)
        {
            var started = DateTime.UtcNow;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 1100);
                count.Text = ((int)Math.Round(games.Count * (1 - Math.Pow(1 - t, 3)))).ToString();
                if (t >= 1 || Steps[_step].Id != "games") timer.Stop();
            };
            timer.Start();
        }
        else count.Text = games.Count.ToString();

        var wall = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1000, Margin = new Thickness(0, 0, 0, 10) };
        int n = 0;
        foreach (var g in games.Where(g => g.Cover != null).Take(14))
        {
            var tile = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), Margin = new Thickness(4), Background = new ImageBrush(g.Cover) { Stretch = Stretch.UniformToFill } };
            if (!Theme.ReduceMotion)
            {
                var sc = new ScaleTransform(0, 0);
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
                tile.RenderTransform = sc;
                var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(200 + n * 60), EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            wall.Children.Add(tile);
            n++;
        }
        body.Children.Add(wall);

        var toggles = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        toggles.Children.Add(Toggle("", "Other launchers", "Epic, GOG, Ubisoft, EA, Xbox / Game Pass, Battle.net and Steam shortcuts.", s.OtherStores, () => s.OtherStores = !s.OtherStores));
        bool hasKey = !string.IsNullOrWhiteSpace(s.SteamWebApiKey);
        var key = Toggle("\uE8D7", "Sign in with Steam", hasKey ? "Done: Friends and your whole Steam library are on." : "Scan a QR code with the Steam app for Friends and every game you own.",
            hasKey, () => { });
        key.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(Host.Settings.SteamWebApiKey)) await SettingsScreen.EditSteamKeyAsync(Host, () => { });
            else
            {
                // Steam's sign-in page with its QR code; the key is fetched by itself once you're in.
                var signIn = new SteamSignInScreen();
                Host.Push(signIn);
                await signIn.Result;
            }
            Go(_step, keep: true);
        };
        toggles.Children.Add(key);
        body.Children.Add(toggles);
        return page;
    }

    private FrameworkElement ExtrasPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("A few extras", "Turn on what you like. Each one is a press away later in Settings, too.");
        var grid = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1020 };
        grid.Children.Add(Toggle("", "Startup animation", "A short animation and chime while SquareDeck opens.", s.StartupIntro, () => s.StartupIntro = !s.StartupIntro));
        grid.Children.Add(Toggle("", "Open when I sign in", "SquareDeck starts with Windows, ready with a controller.", Startup.RunAtSignIn, () =>
        {
            try { Startup.RunAtSignIn = !Startup.RunAtSignIn; } catch (Exception) { }
        }));
        grid.Children.Add(Toggle("", "Sale alerts", "A notification when a game on your Steam wishlist goes on sale.", s.SaleAlerts, () => s.SaleAlerts = !s.SaleAlerts));
        grid.Children.Add(Toggle("", "Game art backgrounds", "The highlighted game's artwork fills the home screen.", s.HeroBackgrounds, () => { s.HeroBackgrounds = !s.HeroBackgrounds; Host.ApplyLook(); }));
        grid.Children.Add(Toggle("", "Interface sounds", "Little clicks and chimes as you move around.", s.UiSounds, () => { s.UiSounds = !s.UiSounds; UiSound.Configure(s); }));
        grid.Children.Add(Toggle("", "Reduce motion", "Calmer: fewer animations and moving effects.", s.ReduceMotion, () => { s.ReduceMotion = !s.ReduceMotion; Host.ApplySettings(); }));
        body.Children.Add(grid);
        return page;
    }

    private FrameworkElement ReadyPage()
    {
        var s = Host.Settings;
        string first = MainWindow.FirstName();
        var (page, body) = Page(first.Length > 0 ? $"You're all set, {first}!" : "You're all set!", "Here's what you chose. Press any of them to change it, or start playing.");

        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 1000 };
        void Chosen(string glyph, string label, string value, int step)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 12, 0) });
            var words = new StackPanel();
            words.Children.Add(Ui.Text(label.ToUpperInvariant(), 11, "SubtleBrush", bold: true));
            words.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold });
            line.Children.Add(words);
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = line, Padding = new Thickness(20, 10, 24, 10), Margin = new Thickness(0, 0, 10, 10) };
            b.Click += (_, _) => Go(step);
            chips.Children.Add(b);
        }
        Chosen("", "Look", s.ThemeMode switch { "light" => "Light", "dark" => "Dark", "oled" => "Black (OLED)", _ => "Follow Windows" }, 2);
        Chosen("", "Background", s.DynamicBackground.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(s.DynamicBackground) : "None", 3);
        Chosen("", "Controller", Hints.Styles.FirstOrDefault(x => x.Item1 == s.GlyphStyle).Item2 ?? "Xbox", 4);
        Chosen("", "Games", s.OtherStores ? "Steam and other launchers" : "Steam", 5);
        body.Children.Add(chips);

        body.Children.Add(Section("Worth remembering"));
        var tips = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Center, Width = 1040 };
        void Tip(string keyHint, string title, string text)
        {
            var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(6) };
            card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            var st = new StackPanel();
            var key = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 3, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10),
                                   Child = new TextBlock { Text = keyHint, FontSize = 14, FontWeight = FontWeights.Bold } };
            key.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            st.Children.Add(key);
            st.Children.Add(Ui.Text(title, 16, "FgBrush", bold: true));
            var t = Ui.Text(text, 13, "SubtleBrush");
            t.TextWrapping = TextWrapping.Wrap;
            st.Children.Add(t);
            card.Child = st;
            tips.Children.Add(card);
        }
        Tip(Hints.KeyLabel("Options"), "Game options", "Details, artwork, notebook, collections.");
        Tip(Hints.Keyboard ? "Q" : "View", "Quick settings", "Volume, brightness, Wi-Fi, from anywhere.");
        Tip(Hints.Keyboard ? "/" : "Menu", "Search", "Games, apps, settings and files.");
        Tip(Hints.Keyboard ? "Ctrl+Alt+Q" : "Guide", "While you play", "Bring SquareDeck back, or open its Game Bar widget.");
        body.Children.Add(tips);

        // Confetti.
        if (!Theme.ReduceMotion) page.Loaded += (_, _) => Confetti(page);
        return page;
    }

    private static void Confetti(Grid page)
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        page.Children.Add(canvas);
        double w = Math.Max(page.ActualWidth, 800), h = Math.Max(page.ActualHeight, 500);
        var rand = new Random();
        string[] palette = { "#FF5A5F", "#FFD166", "#3DDC97", "#4EA8FF", "#B084FF", "#FF9F43" };
        for (int i = 0; i < 90; i++)
        {
            var piece = new Rectangle { Width = 6 + rand.NextDouble() * 6, Height = 10 + rand.NextDouble() * 8, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[rand.Next(palette.Length)])) };
            double x = w / 2 + (rand.NextDouble() - 0.5) * 120, y = h * 0.42;
            Canvas.SetLeft(piece, x);
            Canvas.SetTop(piece, y);
            var move = new TranslateTransform();
            var spin = new RotateTransform(rand.Next(360));
            piece.RenderTransform = new TransformGroup { Children = { spin, move } };
            piece.RenderTransformOrigin = new Point(0.5, 0.5);
            canvas.Children.Add(piece);
            double angle = -Math.PI / 2 + (rand.NextDouble() - 0.5) * Math.PI * 0.95, speed = 380 + rand.NextDouble() * 520;
            double dx = Math.Cos(angle) * speed, up = Math.Sin(angle) * speed;
            int delay = rand.Next(0, 220);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx * 1.4, TimeSpan.FromMilliseconds(2200)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var fall = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay) };
            fall.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(up, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(h * 0.7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            move.BeginAnimation(TranslateTransform.YProperty, fall);
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(spin.Angle, spin.Angle + (rand.Next(2) == 0 ? -1 : 1) * 720, TimeSpan.FromMilliseconds(2400)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
            piece.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(delay + 1900) });
        }
        _ = Task.Delay(3000).ContinueWith(_ => page.Children.Remove(canvas), TaskScheduler.FromCurrentSynchronizationContext());
    }
}
