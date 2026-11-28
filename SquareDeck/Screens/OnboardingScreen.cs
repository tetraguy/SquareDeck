using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The first time SquareDeck opens (and Settings > Updates, backup and reset > Run first-time setup again): System
/// Setup, drawn like the Switch 2's System Settings in the UI style's colors, every step down the left.
///
/// 9.0.12, rebuilt for what SquareDeck has become since: Hello (in a few languages, with "Restore from a backup" for a
/// new PC) → your ROG Ally (who runs the hardware, battery care, and the stick lights, which can now follow the UI
/// style) → you → a style (Switch, then the Mythical styles in their order, then every other tier, each previewed live
/// with its music) → sound (navigation sound, background music, loudness) → home screen (game art, cover shapes and
/// poses, game art backgrounds, a moving background) → controller → games (Steam, other launchers, and the Experimental
/// Epic Games and Xbox accounts) → your companion (character, color, name) → extras (the Game Bar widget, updates,
/// startup) → done. Every choice applies straight away; "Use recommended settings" goes straight to the end.
/// </summary>
public sealed class OnboardingScreen : Screen
{
    /// <summary>The steps, by id, and their names down the left. "Your ROG Ally" is only there on a recognized handheld.</summary>
    private readonly (string Id, string Name)[] Steps;

    private static readonly string[] Hellos = { "Hello", "Hola", "Bonjour", "Olá", "Ciao", "Hallo", "こんにちは", "안녕하세요", "Привет", "你好" };
    private static readonly string[] SuggestedBackgrounds = { "", "Blue Wave", "Black Wave", "Green Blades", "Lofi City", "Night River", "La Fuente", "Analog" };

    /// <summary>The cover shapes offered here (the rest are in Settings > Home screen > Cover styles).</summary>
    private static readonly string[] SetupShapes = { "style", "squircle", "sharp", "circle", "hexagon", "ticket", "arch", "shield" };

    /// <summary>The navigation sounds offered here; "Same as the sound set" is always first.</summary>
    private static readonly (string Id, string Name)[] SetupNavSounds =
        { ("pack:Switch 2 Sounds", "Switch 2"), ("tick", "Tick"), ("marimba", "Marimba"), ("kalimba", "Kalimba"), ("droplet", "Droplet"), ("laser", "Laser") };

    private readonly TaskCompletionSource _done;
    private readonly Grid _stage = new() { ClipToBounds = false };
    private readonly Button _back, _next, _skip;
    private readonly System.Windows.Threading.DispatcherTimer _helloTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly StackPanel _nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private int _step, _helloIndex;
    private bool _startOtherStores, _allStyles;
    private readonly HashSet<string> _accountsChanged = new();

    public OnboardingScreen(TaskCompletionSource done)
    {
        _done = done;
        var steps = new List<(string, string)> { ("hello", "Hello") };
        if (Handheld.IsHandheld) steps.Add(("device", AllyName()));
        steps.AddRange(new[]
        {
            ("you", "You"), ("look", "Style"), ("sound", "Sound"), ("home", "Home screen"), ("controller", "Controller"),
            ("games", "Games"), ("companion", "Companion"), ("extras", "Extras"), ("ready", "Ready"),
        });
        Steps = steps.ToArray();

        _back = Ui.PanelButton("\uE72B", "Back");
        _back.Click += (_, _) => Go(_step - 1);
        _skip = Ui.PanelButton("Skip setup");
        _skip.Click += (_, _) => Finish();
        _next = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Next", MinWidth = 220 };
        _next.Click += (_, _) => Advance();
        _helloTimer.Tick += (_, _) => NextHello();
        BuildFrame();
    }

    private static string AllyName() => Handheld.DisplayName.Length > 0 && Handheld.DisplayName.Length <= 22 ? "Your " + Handheld.DisplayName : "Your ROG Ally";

    public override void OnShown()
    {
        _startOtherStores = Host.Settings.OtherStores;
        Go(0);
    }

    public override void OnResumed()
    {
        // Back from a page opened from here (Steam or store sign-in, a backup): redraw, so it shows what changed.
        Go(_step, keep: true);
    }

    public override void OnClosed() => _helloTimer.Stop();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Back:
            case NavAction.LB:
                if (_step > 0) Go(_step - 1);
                return true; // never leaves by accident; Skip setup is a button
            case NavAction.Start:
            case NavAction.RB:
                Advance();
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
        else if (_accountsChanged.Count > 0) _ = Host.LoadAccountGamesAsync();
        _done.TrySetResult();
    }

    private int IndexOf(string id) => Math.Max(0, Array.FindIndex(Steps, x => x.Id == id));

    // ================================================================ frame

    /// <summary>
    /// "System Setup" with the gear and a line under it, every step down the left (a tick on the ones done, the current
    /// one in blue with its bar, a progress bar under them), the page on the right, and a line over Back, Skip and Next.
    /// </summary>
    private void BuildFrame()
    {
        Switch2Settings.Refresh();
        Switch2Settings.ApplyColors(this);
        SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Resources["TileBrush"] = B(Switch2Settings.Card);
        Resources["DialogBrush"] = B(Switch2Settings.Card);
        Resources["CircleBrush"] = B(Switch2Settings.Off);
        Resources["PanelStroke"] = B(Switch2Settings.Line);

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
        var name = Ui.Text("SquareDeck " + Updater.Current, 17, "SubtleBrush");
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(name);
        root.Children.Add(brand);

        _stage.Margin = new Thickness(0, 6, 18, 0);
        var body = Switch2Pages.Shell(_nav, _stage);
        body.Margin = new Thickness(26, 4, 26, 6);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var foot = new Grid { Margin = new Thickness(40, 0, 40, 0) };
        var over = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        over.SetResourceReference(Shape.FillProperty, "S2Line");
        foot.Children.Add(over);
        FrameworkElement pad = Ui.DeviceArt(30) ?? Ui.Glyph("\uE7FC", 30, "FgBrush");
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
        _skip.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(_skip);
        right.Children.Add(_next);
        foot.Children.Add(right);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;
    }

    /// <summary>The steps down the left: done ones ticked, the current one in blue with its bar.</summary>
    private void BuildNav()
    {
        _nav.Children.Clear();
        var count = Ui.Text($"Step {_step + 1} of {Steps.Length}", 15, "SubtleBrush");
        count.Margin = new Thickness(40, 4, 0, 10);
        _nav.Children.Add(count);
        for (int i = 0; i < Steps.Length; i++)
        {
            int index = i;
            // Not in the controller's path (Back / Next move through the steps), but a tap or click goes straight there.
            var item = Switch2Pages.NavItem(Steps[i].Name, i < _step ? "✓" : null, i == _step, () => Go(index), focusable: false);
            item.Opacity = i > _step ? 0.6 : 1;
            _nav.Children.Add(item);
        }
        var bar = new Grid { Height = 6, Margin = new Thickness(40, 18, 18, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Switch2Settings.Off) });
        // Starts at its width (never NaN), so the animation always has somewhere to start from.
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 6 };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        double part = Steps.Length <= 1 ? 1 : _step / (double)(Steps.Length - 1);
        bar.SizeChanged += (_, e) =>
        {
            double to = Math.Max(6, e.NewSize.Width * part);
            if (Theme.ReduceMotion) fill.Width = to;
            else fill.BeginAnimation(WidthProperty, new DoubleAnimation(double.IsNaN(fill.ActualWidth) || fill.ActualWidth <= 0 ? 6 : fill.ActualWidth, to, TimeSpan.FromMilliseconds(300)));
        };
        bar.Children.Add(fill);
        _nav.Children.Add(bar);
    }

    /// <param name="keep">Redrawing the same step after a choice: no slide, and the highlight stays on what was picked.</param>
    private void Go(int step, bool keep = false)
    {
        int from = _step;
        var focusedTag = keep ? (Keyboard.FocusedElement as FrameworkElement)?.Tag as string : null;
        double scroll = keep ? _stage.FindDescendants<ScrollViewer>().FirstOrDefault()?.VerticalOffset ?? 0 : 0;
        _step = Math.Clamp(step, 0, Steps.Length - 1);
        _helloTimer.Stop();
        BuildNav();
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
            "sound" => SoundPage(),
            "home" => HomePage(),
            "controller" => ControllerPage(),
            "games" => GamesPage(),
            "companion" => CompanionPage(),
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
            outMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -120 * dir, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            old.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)));
            var gone = old;
            _ = Task.Delay(260).ContinueWith(_ => _stage.Children.Remove(gone), TaskScheduler.FromCurrentSynchronizationContext());
            var inMove = new TranslateTransform(160 * dir, 0);
            page.RenderTransform = inMove;
            page.Opacity = 0;
            inMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(160 * dir, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { BeginTime = TimeSpan.FromMilliseconds(60) });
        }
        else _stage.Children.Clear();
        _stage.Children.Add(page);
        Polish(page);

        if (keep && scroll > 0)
            Dispatcher.BeginInvoke(() => page.FindDescendants<ScrollViewer>().FirstOrDefault()?.ScrollToVerticalOffset(scroll), System.Windows.Threading.DispatcherPriority.Loaded);
        var buttons = page.FindDescendants<Button>().Where(b => b.Focusable).ToList();
        var target = (focusedTag != null ? buttons.FirstOrDefault(b => b.Tag as string == focusedTag) : null)
                     ?? (_step == Steps.Length - 1 ? _next : buttons.FirstOrDefault(b => b.Tag as string == "current") ?? buttons.FirstOrDefault() ?? _next);
        FocusLater(target);
        if (!keep) UiSound.Move();
    }

    /// <summary>A page drawn the Switch 2 way: lighter titles, bar-and-name section headings, tiles with the rimmed highlight.</summary>
    private static void Polish(FrameworkElement page)
    {
        var tile = Ui.StyleOf("TileButton");
        foreach (var b in page.FindDescendants<Button>().Where(b => b.Style == tile).ToList())
        {
            var margin = b.Margin;
            b.Style = Switch2Pages.TileStyle;
            b.Margin = margin;
        }
    }

    // ================================================================ building blocks

    /// <summary>A page: its title and a line under it, over a scrolling column of content.</summary>
    private static (Grid Page, StackPanel Body) Page(string title, string lead)
    {
        var grid = new Grid();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
        var column = new StackPanel { Margin = new Thickness(14, 18, 14, 12), VerticalAlignment = VerticalAlignment.Top };
        column.Children.Add(Ui.Text(title, 34, "FgBrush"));
        var l = Ui.Text(lead, 18, "SubtleBrush");
        l.TextWrapping = TextWrapping.Wrap;
        l.MaxWidth = 900;
        l.HorizontalAlignment = HorizontalAlignment.Left;
        l.Margin = new Thickness(0, 8, 0, 14);
        column.Children.Add(l);
        var body = new StackPanel();
        column.Children.Add(body);
        scroll.Content = column;
        grid.Children.Add(scroll);
        return (grid, body);
    }

    private static FrameworkElement Section(string text)
    {
        var header = Switch2Settings.Header(text);
        header.Margin = new Thickness(4, 22, 0, 10);
        return header;
    }

    private static TextBlock Note(string text)
    {
        var t = Ui.Text(text, 14, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.MaxWidth = 900;
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.Margin = new Thickness(4, 4, 0, 6);
        return t;
    }

    /// <summary>A picture card with its name under it. The chosen one carries a tick in the highlight color.</summary>
    private static Button Card(UIElement picture, double width, double height, string name, bool chosen, Action pick, string tag, string? sub = null)
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
        b.Tag = chosen ? "current" : tag;
        b.Click += (_, _) => pick();
        return b;
    }

    private static Border Tick(double size, Thickness margin)
    {
        var tick = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = margin };
        tick.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var mark = new TextBlock { Text = "\uE73E", FontFamily = Ui.IconFont, FontSize = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        tick.Child = mark;
        return tick;
    }

    /// <summary>A row of pill choices; the chosen one is filled in.</summary>
    private WrapPanel Choices((string Id, string Text)[] options, string current, Action<string> pick, string group)
    {
        var w = new WrapPanel();
        foreach (var (id, text) in options)
        {
            string o = id;
            bool chosen = current == o;
            var b = chosen ? new Button { Style = Ui.StyleOf("AccentButton"), Content = "✓  " + text } : Ui.PanelButton(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Tag = group + ":" + o;
            b.Click += (_, _) => { pick(o); Host.Settings.Save(); Go(_step, keep: true); };
            w.Children.Add(b);
        }
        return w;
    }

    /// <summary>A toggle card: icon, name, a line about it and an On / Off switch.</summary>
    private Button Toggle(string glyph, string title, string about, bool on, Action flip, double width = 320)
    {
        var face = new Border { Width = width, MinHeight = 156, CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 18, 20, 16), BorderThickness = new Thickness(2) };
        face.SetResourceReference(Border.BackgroundProperty, "TileBrush");
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
        var toggle = Switch2Settings.Toggle(on);
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(toggle);
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
        b.Tag = "toggle:" + title;
        b.Click += (_, _) => { flip(); Host.Settings.Save(); Go(_step, keep: true); };
        return b;
    }

    /// <summary>An action card (sign in, install, open): like a toggle card, with a word on the right instead of a switch.</summary>
    private Button Action(string glyph, string title, string about, string state, bool done, Func<Task> run, double width = 320)
    {
        var face = new Border { Width = width, MinHeight = 156, CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 18, 20, 16), BorderThickness = new Thickness(2) };
        face.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        face.SetResourceReference(Border.BorderBrushProperty, done ? "AccentBrush" : "PanelStroke");
        var stack = new DockPanel();
        var head = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        var icon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12) };
        icon.SetResourceReference(Border.BackgroundProperty, done ? "AccentBrush" : "CircleBrush");
        var g = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.SetResourceReference(TextBlock.ForegroundProperty, done ? "BgBrush" : "FgBrush");
        icon.Child = g;
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        var word = Ui.Text(state, 15, done ? "AccentBrush" : "FgBrush", bold: true);
        word.HorizontalAlignment = HorizontalAlignment.Right;
        word.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(word);
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
        b.Tag = "action:" + title;
        b.Click += async (_, _) =>
        {
            try { await run(); } catch (Exception) { }
            if (IsVisible) Go(_step, keep: true);
        };
        return b;
    }

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

    private static WrapPanel Wrap(double maxWidth = 1060) => new() { MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Left };

    // ================================================================ hello

    private TextBlock? _hello;

    private FrameworkElement HelloPage()
    {
        var grid = new Grid();
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // The logo builds itself: the square pops, the frame spins in.
        var logo = Logo(140);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(logo);
        if (!Theme.ReduceMotion)
        {
            var scale = new ScaleTransform(0.3, 0.3);
            var turn = new RotateTransform();
            logo.RenderTransformOrigin = new Point(0.5, 0.5);
            logo.RenderTransform = new TransformGroup { Children = { scale, turn } };
            var pop = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = pop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = pop });
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-90, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }

        _hello = new TextBlock { Text = Hellos[0], FontSize = 80, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        _hello.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        column.Children.Add(_hello);
        string first = MainWindow.FirstName();
        var lead = Ui.Text((first.Length > 0 ? $"Welcome, {first}. " : "Welcome. ") + "Let's make SquareDeck yours. It takes a couple of minutes.", 20, "SubtleBrush");
        lead.HorizontalAlignment = HorizontalAlignment.Center;
        lead.Margin = new Thickness(0, 6, 0, 36);
        column.Children.Add(lead);

        var start = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Let's go", MinWidth = 300, FontSize = 22, Padding = new Thickness(30, 14, 30, 14), HorizontalAlignment = HorizontalAlignment.Center, Tag = "current" };
        start.Click += (_, _) => Go(1);
        column.Children.Add(start);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var quick = Ui.PanelButton("\uE734", "Use recommended settings");
        quick.Margin = new Thickness(0, 0, 10, 0);
        quick.Click += (_, _) => Go(Steps.Length - 1);
        row.Children.Add(quick);
        // On a new PC: bring everything back from a backup made on the old one (it restarts SquareDeck).
        var restore = Ui.PanelButton("\uE74E", "Restore from a backup");
        restore.Click += (_, _) => Host.Push(new BackupScreen());
        row.Children.Add(restore);
        column.Children.Add(row);
        var note = Ui.Text("Everything here can be changed later in Settings, and this setup can be run again from Settings > Updates, backup and reset.", 14, "SubtleBrush");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.TextAlignment = TextAlignment.Center;
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 720;
        note.Margin = new Thickness(0, 16, 0, 0);
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
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            word.Text = Hellos[_helloIndex];
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            word.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        };
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200)));
        word.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ================================================================ your ROG Ally

    /// <summary>The ROG Ally SquareDeck found: its picture and what's inside, who runs it, battery care and the lights.</summary>
    private FrameworkElement DevicePage()
    {
        var s = Host.Settings;
        bool ally = Handheld.Vendor == HandheldVendor.Asus;
        var (page, body) = Page("Hello, " + Handheld.DisplayName,
            "SquareDeck found your " + Handheld.DisplayName + " and is set up for it. Its power, lights, fans, buttons and battery care have a section of their own in Settings, and My Device (temperatures, fans, battery health and power draw, live) is at the top of Settings.");

        var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        if (MyDeviceScreen.DevicePhoto(380) is FrameworkElement photo)
        {
            photo.HorizontalAlignment = HorizontalAlignment.Left;
            hero.Children.Add(photo);
        }
        var chips = Wrap();
        chips.Margin = new Thickness(0, 12, 0, 0);
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
        Chip("\uE950", TdpControl.CpuName);
        if (Handheld.Current is { } device) Chip("\uE945", $"{device.TdpMin}–{device.TdpMax} W");
        if (DisplaySettings.Current() is { } screen) Chip("\uE7F4", $"{screen.Resolution}  ·  {screen.RefreshRate} Hz");
        if (Battery.GetPercent() is int battery) Chip("\uE83F", battery + "% battery");
        if (ally) Chip("\uE781", AllyHid.ZoneCount == 8 ? "8 light zones" : "4 light zones");
        hero.Children.Add(chips);
        body.Children.Add(hero);

        // Who runs the hardware: SquareDeck, or Armoury Crate SE (so the two don't fight over the same settings).
        body.Children.Add(Section("Who should run its power, lights and buttons?"));
        string owner = s.TdpEnabled || s.AllyRemap || s.AllyLights.Length > 0 || StyleLights.On ? "squaredeck" : "maker";
        string maker = ally ? "Armoury Crate SE" : "The maker's app";
        body.Children.Add(Choices(new[] { ("squaredeck", "SquareDeck"), ("maker", maker) }, owner, id =>
        {
            bool mine = id == "squaredeck";
            s.TdpEnabled = mine && TdpControl.CanApply;
            if (ally)
            {
                s.AllyRemap = mine;
                if (mine && s.AllyLights.Length == 0 && !StyleLights.On) s.AllyLights = "accent";
                if (!mine) { s.AllyLights = ""; Labs.Set(StyleLights.Lab, false); }
                AllyControl.ApplyLights(force: true);
                Host.ApplyAllyButtons();
            }
            if (s.TdpEnabled) _ = TdpControl.ApplyAsync(quiet: true);
        }, "owner"));
        body.Children.Add(Note(owner == "squaredeck"
            ? "SquareDeck sets the TDP, maps the special buttons" + (ally ? " and runs the lights" : "") + ". " + maker + " can stay installed; whichever changes something last wins."
            : maker + " keeps doing it, and SquareDeck leaves those settings alone. You can switch any of them on in Settings later."));

        if (ally && owner == "squaredeck")
        {
            // The stick lights: off, the highlight color, or an effect picked for each UI style (it changes with the style).
            body.Children.Add(Section("The lights round the sticks"));
            string lights = StyleLights.On ? "style" : s.AllyLights == "off" ? "off" : s.AllyLights == "accent" ? "accent" : "other";
            var options = new List<(string, string)> { ("style", "Match the UI style"), ("accent", "Highlight color"), ("off", "Off") };
            if (lights == "other") options.Insert(0, ("other", AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights).Name ?? "Your effect"));
            body.Children.Add(Choices(options.ToArray(), lights, id =>
            {
                if (id == "other") return;
                Labs.Set(StyleLights.Lab, id == "style");
                if (id != "style") s.AllyLights = id;
                else if (s.AllyLights.Length == 0) s.AllyLights = "accent";
                AllyControl.ApplyLights(force: true);
            }, "lights"));
            body.Children.Add(Note(AllyHid.ZoneCount == 8
                ? "“Match the UI style” plays an effect chosen for each style, in its colors, drawn LED by LED for this Ally's eight light zones. More than 150 effects are in Settings."
                : "“Match the UI style” plays an effect chosen for each style, in its colors, made for this Ally's four light zones. More than 150 effects are in Settings."));
        }

        var cards = Wrap();
        if (ally) body.Children.Add(Section("A few things to start with"));
        if (ally)
            cards.Children.Add(Toggle("\uE83F", "Battery care", "Stop charging at 80%, kinder to the battery when it's mostly plugged in.", s.AllyChargeLimit is > 0 and < 100, () =>
            {
                s.AllyChargeLimit = s.AllyChargeLimit is > 0 and < 100 ? 0 : 80;
                Task.Run(AllyControl.ApplyBattery);
            }));
        body.Children.Add(cards);
        return page;
    }

    // ================================================================ you

    private FrameworkElement YouPage()
    {
        var s = Host.Settings;
        string first = MainWindow.FirstName();
        var (page, body) = Page("First, you", "Pick a picture for the top of the home screen. A photo of your own, or your Steam picture, can be chosen later in Settings > Profile.");

        // The greeting as it will look, with the chosen picture.
        var preview = new Border { CornerRadius = new CornerRadius(34), Padding = new Thickness(10, 10, 26, 10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 20), BorderThickness = new Thickness(1.5) };
        preview.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        preview.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Avatars.Build(s.Avatar, 52));
        var hi = Ui.Text(MainWindow.Greeting(DateTime.Now) + (first.Length > 0 ? ", " + first : ""), 22, "FgBrush", bold: true);
        hi.VerticalAlignment = VerticalAlignment.Center;
        hi.Margin = new Thickness(16, 0, 0, 0);
        row.Children.Add(hi);
        preview.Child = row;
        body.Children.Add(preview);

        var pictures = Wrap(940);
        for (int i = 0; i < 16; i++)
        {
            string id = "preset:" + i;
            bool chosen = s.Avatar == id;
            var face = new Grid();
            face.Children.Add(Avatars.Build(id, 84));
            if (chosen) face.Children.Add(Tick(26, new Thickness(0, -4, -4, 0)));
            var b = Ui.Tile(face);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Tag = chosen ? "current" : "avatar:" + i;
            b.Click += (_, _) => { s.Avatar = id; s.Save(); Host.ApplyAvatar(); Go(_step, keep: true); };
            pictures.Children.Add(b);
        }
        body.Children.Add(pictures);

        body.Children.Add(Section("Say hello when SquareDeck opens?"));
        body.Children.Add(Choices(new[] { ("on", "Yes, greet me"), ("off", "No thanks") }, s.WelcomeMessage ? "on" : "off", id => s.WelcomeMessage = id == "on", "greet"));
        return page;
    }

    // ================================================================ style

    private FrameworkElement LookPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("Pick a style", "Each style dresses all of SquareDeck: colors, fonts, sounds, its own music, a background" +
            (Handheld.Vendor == HandheldVendor.Asus ? " and, if you like, the stick lights" : "") + ". It changes right behind this page as you pick, music and all.");
        const double W = 300;
        var current = UiStyles.Find(s.UiStyle);

        Button StyleTile(UiStyle st)
        {
            s.SkinVariants.TryGetValue(st.Id, out var variant);
            bool inUse = st.Id == current.Id;
            var tile = st.Skin != null ? SettingsScreen.AdvancedCard(st, variant, inUse, W) : SettingsScreen.StyleCard(st, inUse, W);
            tile.Tag = inUse ? "current" : "style:" + st.Id;
            tile.Click += (_, _) =>
            {
                if (st.Id == UiStyles.Find(Host.Settings.UiStyle).Id) return;
                Host.UseStyle(st); // its colors, sounds, music and background (and lights, when they follow the style)
                UiSound.Select();
                ReColor();
                Go(_step, keep: true);
            };
            return tile;
        }
        WrapPanel Row(IEnumerable<UiStyle> styles)
        {
            var w = Wrap((W + 12) * 3 + 4);
            foreach (var st in styles) w.Children.Add(StyleTile(st));
            return w;
        }

        var available = UiStyles.All.Concat(UiStyles.Advanced).Where(x => !StyleUnlocks.IsLocked(x)).GroupBy(x => x.Id).Select(g => g.First()).ToList();
        bool Has(UiStyle x) => available.Any(a => a.Id == x.Id);
        var mythical = UiStyles.MythicalInOrder().Where(Has).ToList();
        var switchStyle = available.FirstOrDefault(x => x.Id == "switch");

        // Switch (the default) and the first Mythical styles, in their order.
        var featured = new List<UiStyle>();
        if (switchStyle != null) featured.Add(switchStyle);
        featured.AddRange(mythical.Take(8));
        body.Children.Add(Section("Switch and the Mythical styles"));
        body.Children.Add(Row(featured));

        // Light or dark: Switch lets you choose; the other styles bring their own.
        if (current.Id == "switch")
        {
            body.Children.Add(Section("Switch: light or dark"));
            body.Children.Add(Choices(new[] { ("system", "Follow Windows"), ("light", "Light"), ("dark", "Dark"), ("oled", "Black (OLED)") }, s.ThemeMode, m =>
            {
                s.ThemeMode = m;
                s.DarkTheme = Theme.ShouldBeDark(s);
                Host.ApplyLook();
                ReColor();
            }, "mode"));
        }

        var shown = featured.Select(x => x.Id).ToHashSet();
        var moreMythical = mythical.Where(x => !shown.Contains(x.Id)).ToList();
        var legendary = available.Where(x => !x.Mythical && !shown.Contains(x.Id) && (x.Legendary || UiStyles.All.Any(a => a.Id == x.Id))).ToList();
        var others = available.Where(x => !x.Mythical && !shown.Contains(x.Id) && !legendary.Contains(x)).ToList();
        int rest = moreMythical.Count + legendary.Count + others.Count;
        if (rest > 0)
        {
            if (!_allStyles)
            {
                var more = Ui.PanelButton("\uE712", $"Show every style ({rest} more)");
                more.HorizontalAlignment = HorizontalAlignment.Left;
                more.Margin = new Thickness(0, 14, 0, 0);
                more.Tag = "allstyles";
                more.Click += (_, _) => { _allStyles = true; Go(_step, keep: true); };
                body.Children.Add(more);
            }
            else
            {
                if (moreMythical.Count > 0) { body.Children.Add(Section("More Mythical styles")); body.Children.Add(Row(moreMythical)); }
                if (legendary.Count > 0) { body.Children.Add(Section("Legendary")); body.Children.Add(Row(legendary)); }
                if (others.Count > 0) { body.Children.Add(Section("Every other style")); body.Children.Add(Row(others)); }
            }
        }
        body.Children.Add(Note("Later, Settings > Themes opens the style gallery, with every style previewed live, styles you unlock by playing, a style schedule and styles of your own."));
        return page;
    }

    /// <summary>After a style or light / dark change: the setup takes the new colors.</summary>
    private void ReColor()
    {
        Switch2Settings.Refresh();
        Switch2Settings.ApplyColors(this);
        SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Resources["TileBrush"] = B(Switch2Settings.Card);
        Resources["DialogBrush"] = B(Switch2Settings.Card);
        Resources["CircleBrush"] = B(Switch2Settings.Off);
        Resources["PanelStroke"] = B(Switch2Settings.Line);
        if (Content is Grid root) root.Background = B(Switch2Settings.Background);
    }

    // ================================================================ sound

    private FrameworkElement SoundPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("How should it sound?", "The style you picked brings its own sounds and music. Change any of it here; it plays as you choose.");

        var cards = Wrap();
        cards.Children.Add(Toggle("\uE767", "Interface sounds", "Little clicks and chimes as you move around.", s.UiSounds, () =>
        {
            s.UiSounds = !s.UiSounds;
            UiSound.Configure(s);
            UiSound.Select();
        }));
        body.Children.Add(cards);

        if (s.UiSounds)
        {
            body.Children.Add(Section("Moving the highlight"));
            var navs = new List<(string, string)> { ("", "Same as the sound set") };
            foreach (var (id, name) in SetupNavSounds)
                if (!id.StartsWith("pack:", StringComparison.Ordinal) || SoundPacks.Find(id) != null) navs.Add((id, name));
            if (s.NavSoundSet.Length > 0 && navs.All(n => n.Item1 != s.NavSoundSet)) navs.Add((s.NavSoundSet, UiSound.SetName(s.NavSoundSet)));
            body.Children.Add(Choices(navs.ToArray(), s.NavSoundSet, id =>
            {
                s.NavSoundSet = id;
                UiSound.Configure(s);
                UiSound.Preview(id.Length > 0 ? id : s.SoundSet, move: true);
            }, "nav"));
            body.Children.Add(Section("Loudness"));
            body.Children.Add(Choices(new[] { ("25", "Quiet"), ("50", "Soft"), ("75", "Medium"), ("100", "Full") }, s.SoundVolume.ToString(), id =>
            {
                s.SoundVolume = int.Parse(id);
                UiSound.Configure(s);
                UiSound.Select();
            }, "volume"));
        }

        body.Children.Add(Section("Background music"));
        string styleMusic = UiStyles.Current.Music ?? "";
        string music = s.MenuMusic.Length == 0 ? "" : s.MenuMusic == "shuffle" ? "shuffle" : s.MenuMusic == styleMusic ? "style" : "mine";
        var musicOptions = new List<(string, string)>();
        if (styleMusic.Length > 0) musicOptions.Add(("style", "The style's music (" + BackgroundMusic.NameOf(styleMusic) + ")"));
        musicOptions.Add(("shuffle", "Shuffle all"));
        if (music == "mine") musicOptions.Add(("mine", BackgroundMusic.NameOf(s.MenuMusic)));
        musicOptions.Add(("", "Off"));
        body.Children.Add(Choices(musicOptions.ToArray(), music, id =>
        {
            if (id == "mine") return;
            s.MenuMusic = id == "style" ? styleMusic : id;
            s.Save();
            Host.RestartMenuMusic();
        }, "music"));
        body.Children.Add(Note("It plays quietly only while no game is running, and pauses for your own music and videos. Your own tracks, sound packs and SquareDeck EQ (with Equalizer APO) are in Settings > Sound."));
        return page;
    }

    // ================================================================ home screen

    private FrameworkElement HomePage()
    {
        var s = Host.Settings;
        var (page, body) = Page("Your home screen", "How your games look on the home row, and what's behind them.");

        // Game art: square covers, tall posters or wide headers.
        body.Children.Add(Section("Game art"));
        body.Children.Add(Choices(UiStyles.TileArts.Select(a => (a.Id, a.Name)).ToArray(), s.TileArt, id =>
        {
            s.TileArt = id;
            Host.ApplySettings();
        }, "art"));

        // Cover shapes, drawn on one of your own covers.
        var cover = Host.AllGames.Where(g => g.IsGame && !g.UsesIcon).Select(g => g.Cover).OfType<BitmapSource>().FirstOrDefault();
        var look = CoverStyles.Read("home");
        body.Children.Add(Section("Cover shape"));
        var shapes = Wrap();
        foreach (var id in SetupShapes)
        {
            var info = CoverStyles.Shapes.FirstOrDefault(x => x.Id == id);
            if (info.Id == null) continue;
            const double size = 118;
            var face = new Grid { Width = size + 24, Height = size + 24 };
            FrameworkElement art = cover != null
                ? new Image { Source = cover, Stretch = Stretch.UniformToFill, Width = size, Height = size }
                : new Border { Width = size, Height = size, Background = (Brush)(TryFindResource("AccentBrush") ?? Brushes.SteelBlue) };
            art.HorizontalAlignment = HorizontalAlignment.Center;
            art.VerticalAlignment = VerticalAlignment.Center;
            art.Clip = CoverStyles.Outline(id, size, size, 14) ?? new RectangleGeometry(new Rect(0, 0, size, size), 14, 14);
            face.Children.Add(art);
            bool chosen = CoverStyles.On && look.Shape == id || !CoverStyles.On && id == "style";
            shapes.Children.Add(Card(face, size + 24, size + 24, info.Name, chosen, () =>
            {
                Labs.Set(CoverStyles.Lab, true);
                Labs.Put("coverstyle.home.shape", id);
                Host.ApplyCoverStyle();
                Go(_step, keep: true);
            }, "shape:" + id));
        }
        body.Children.Add(shapes);
        body.Children.Add(Section("How they sit on the row"));
        var poses = CoverStyles.Poses.Where(p => p.Id is "flat" or "tiltleft" or "scatter" or "fan" or "lean" or "bob").Select(p => (p.Id, p.Name)).ToArray();
        body.Children.Add(Choices(poses, CoverStyles.On ? look.Pose : "flat", id =>
        {
            Labs.Set(CoverStyles.Lab, true);
            Labs.Put("coverstyle.home.pose", id);
            Host.ApplyCoverStyle();
        }, "pose"));
        body.Children.Add(Note("Frames (Polaroid, cartridge, gallery gold…), more shapes and a separate look for the library are in Settings > Home screen > Cover styles."));

        body.Children.Add(Section("Behind your games"));
        var cards = Wrap();
        cards.Children.Add(Toggle("\uE91B", "Game art backgrounds", "The highlighted game's artwork fills the home screen.", s.HeroBackgrounds, () => { s.HeroBackgrounds = !s.HeroBackgrounds; Host.ApplyLook(); }));
        cards.Children.Add(Toggle("\uE768", "Continue where you left off", "Your last game as a large tile with its artwork, first on the row.", s.ContinueTile, () => { s.ContinueTile = !s.ContinueTile; Host.ApplySettings(); }));
        body.Children.Add(cards);

        // A moving background: a slow, silent video behind everything (it stops while you play).
        body.Children.Add(Section("A moving background"));
        var row = Wrap();
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
                var off = new TextBlock { Text = "\uE711", FontFamily = Ui.IconFont, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
                off.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                picture = off;
            }
            bool chosen = string.Equals(s.DynamicBackground, n, StringComparison.OrdinalIgnoreCase);
            row.Children.Add(Card(picture, 220, 124, n.Length == 0 ? "The style's own" : n, chosen, () =>
            {
                if (n.Length > 0 && s.DynamicBlur == 0) s.DynamicBlur = 8; // a little softness keeps text readable
                Host.UseDynamicBackground(n);
                Go(_step, keep: true);
            }, "bg:" + n));
        }
        body.Children.Add(row);
        body.Children.Add(Note("It stops while you play, so it costs nothing in games. Wallpapers, Wallpaper Engine and blur are in Settings > Themes."));
        return page;
    }

    // ================================================================ controller

    private FrameworkElement ControllerPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("Which buttons?", "So the buttons drawn on screen match the ones in your hands" + (Handheld.IsHandheld ? " (the Ally's are Xbox style)." : "."));
        var cards = Wrap();
        foreach (var (id, name) in Hints.Styles)
        {
            string style = id;
            var art = new Grid();
            var (labels, colors) = id switch
            {
                "playstation" => (new[] { "△", "○", "✕", "□" }, new[] { "#3FC8A8", "#FF5A64", "#6FA8FF", "#E07BD6" }),
                "switch" => (new[] { "X", "A", "B", "Y" }, new[] { "#DADDE3", "#DADDE3", "#DADDE3", "#DADDE3" }),
                _ => (new[] { "Y", "B", "A", "X" }, new[] { "#F2C200", "#E8453C", "#2EB872", "#2B7DE0" })
            };
            var diamond = new Canvas { Width = 140, Height = 140, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var spots = new[] { (46.0, 0.0), (92.0, 46.0), (46.0, 92.0), (0.0, 46.0) };
            for (int i = 0; i < 4; i++)
            {
                var c = (Color)ColorConverter.ConvertFromString(colors[i]);
                var button = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2C)), BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(2.5),
                                          Child = new TextBlock { Text = labels[i], FontSize = 21, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                Canvas.SetLeft(button, spots[i].Item1);
                Canvas.SetTop(button, spots[i].Item2);
                diamond.Children.Add(button);
            }
            art.Children.Add(diamond);
            cards.Children.Add(Card(art, 260, 176, name, s.GlyphStyle == style, () =>
            {
                s.GlyphStyle = style;
                Hints.SetStyle(style);
                // Nintendo controllers select with the right button.
                if (style == "switch" && Hints.Map != "BAYX") Host.SetButtonMap("BAYX");
                else if (style != "switch" && Hints.Map == "BAYX") Host.SetButtonMap("ABXY");
                Host.ApplySettings();
                Go(_step, keep: true);
            }, "pad:" + style));
        }
        body.Children.Add(cards);
        body.Children.Add(Section("Which button selects"));
        body.Children.Add(Choices(new[] { ("ABXY", "Bottom button"), ("BAYX", "Right button (Nintendo)") }, Hints.Map, id => Host.SetButtonMap(id), "map"));
        body.Children.Add(Section("How buttons are drawn"));
        body.Children.Add(Choices(Hints.Looks, s.GlyphLook, id => { s.GlyphLook = id; Hints.SetLook(id); }, "glyphs"));

        var sample = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 12, 22, 12), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        sample.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        sample.Child = new HintBar("{Select}  Select      {Back}  Back      {Details}  Details      {Options}  Options");
        body.Children.Add(sample);
        return page;
    }

    // ================================================================ games

    private FrameworkElement GamesPage()
    {
        var s = Host.Settings;
        var games = Host.AllGames.Where(g => g.IsGame).ToList();
        int steam = games.Count(g => g.IsSteam), others = games.Count - steam;
        var (page, body) = Page("Your games", games.Count > 0
            ? (steam > 0 ? $"{steam} from Steam" : "") + (others > 0 ? (steam > 0 ? " and " : "") + $"{others} from elsewhere" : "") + ", ready to play. Covers download by themselves."
            : "Steam games show up by themselves, with their covers. Sign in below for the rest of your library.");

        // The big number counts up, and covers pop in beside it.
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        var count = Ui.Text("0", 84, "AccentBrush", bold: true);
        numbers.Children.Add(count);
        var label = Ui.Text(games.Count == 1 ? "game installed" : "games installed", 17, "SubtleBrush");
        label.Margin = new Thickness(4, -6, 0, 0);
        numbers.Children.Add(label);
        top.Children.Add(numbers);
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
        var wall = Wrap(620);
        wall.VerticalAlignment = VerticalAlignment.Center;
        int n = 0;
        foreach (var g in games.Where(g => g.Cover != null).Take(16))
        {
            var tile = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(12), Margin = new Thickness(4), Background = new ImageBrush(g.Cover) { Stretch = Stretch.UniformToFill } };
            if (!Theme.ReduceMotion)
            {
                var sc = new ScaleTransform(0, 0);
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
                tile.RenderTransform = sc;
                var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(200 + n * 50), EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            wall.Children.Add(tile);
            n++;
        }
        top.Children.Add(wall);
        body.Children.Add(top);

        body.Children.Add(Section("Your libraries"));
        var cards = Wrap();
        bool hasKey = !string.IsNullOrWhiteSpace(s.SteamWebApiKey);
        cards.Children.Add(Action("\uE8D7", "Sign in with Steam", hasKey ? "Friends, and every game you own (installed or not), are on." : "Scan a QR code with the Steam app: Friends, and every game you own in the library.",
            hasKey ? "Signed in" : "Sign in", hasKey, async () =>
            {
                if (!string.IsNullOrWhiteSpace(Host.Settings.SteamWebApiKey)) { await SettingsScreen.EditSteamKeyAsync(Host, () => { }); return; }
                var signIn = new SteamSignInScreen();
                Host.Push(signIn);
                await signIn.Result;
            }));
        cards.Children.Add(Toggle("\uE7FC", "Other launchers", "Installed games from Epic, GOG, Ubisoft, EA, the Xbox app, Battle.net and Steam shortcuts.", s.OtherStores, () => s.OtherStores = !s.OtherStores));
        body.Children.Add(cards);

        // The games on your Epic and Xbox accounts that aren't installed, next to Steam's (out of Experimental in 9.0.20).
        body.Children.Add(Section("Your Epic Games and Xbox accounts"));
        var accounts = Wrap();
        accounts.Children.Add(StoreCard("Epic", "\uE719", "Epic Games", StoreAccounts.EpicSignedIn && Labs.On(StoreAccounts.EpicLab), StoreAccounts.EpicName, StoreAccounts.EpicGames.Count,
            "Every game on your Epic account in the library, installed or not. Installing goes through the Epic Games Launcher."));
        accounts.Children.Add(StoreCard("Xbox", "\uE990", "Xbox and PC Game Pass", StoreAccounts.XboxSignedIn && Labs.On(StoreAccounts.XboxLab), StoreAccounts.XboxName, StoreAccounts.XboxGames.Count,
            "Your PC games from Xbox, and all of PC Game Pass if you like. Installing goes through the Xbox app."));
        body.Children.Add(accounts);
        body.Children.Add(Note("A game you own on more than one store shows once among the games that aren't installed, with the number of stores on it; choosing it asks which store to install from."));
        return page;
    }

    private Button StoreCard(string store, string glyph, string name, bool signedIn, string who, int games, string about) =>
        Action(glyph, name, signedIn ? (who.Length > 0 ? who + "  ·  " : "") + (games == 1 ? "1 game" : games + " games") : about,
            signedIn ? "Signed in" : "Sign in", signedIn, async () =>
            {
                if (signedIn)
                {
                    Host.Push(new StoreAccountScreen(store));
                    return;
                }
                Labs.Set(store == "Epic" ? StoreAccounts.EpicLab : StoreAccounts.XboxLab, true);
                var signIn = new StoreSignInScreen(store);
                Host.Push(signIn);
                if (await signIn.Result)
                {
                    _accountsChanged.Add(store);
                    await Host.LoadAccountGamesAsync(refresh: true, store: store);
                }
            });

    // ================================================================ companion

    private FrameworkElement CompanionPage()
    {
        bool on = Labs.On("companion");
        var (page, body) = Page("A little companion?", "A small pixel friend in the corner of Home. It reacts to what you do, cheers your achievements, acts out the game you're on, and has a playground of its own with neighbors and mini-games.");

        var cards = Wrap();
        cards.Children.Add(Toggle("\uE899", "Companion", on ? "On. It shows up on Home, the update screen and the Game Bar widget." : "Off. Turn it on to meet it.", on, () =>
        {
            Labs.Set("companion", !Labs.On("companion"));
            Host.RefreshCompanion();
        }));
        body.Children.Add(cards);
        if (!on) return page;

        body.Children.Add(Section("Who is it?"));
        var kinds = Wrap();
        foreach (var (id, kindName) in CompanionArt.Species)
        {
            string k = id;
            var canvas = new Canvas { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            CompanionArt.Draw(canvas, k, CompanionArt.ColorHex, "happy", false, Labs.Outfit, cell: 8);
            kinds.Children.Add(Card(canvas, 150, 130, kindName, CompanionArt.SpeciesId == k, () =>
            {
                Labs.Put("companion.species", k);
                Host.RefreshCompanion();
                Host.CompanionReact("jump");
                Go(_step, keep: true);
            }, "species:" + k));
        }
        body.Children.Add(kinds);

        body.Children.Add(Section("Its color"));
        var colors = Wrap();
        foreach (var (colorName, hex) in CompanionArt.Colors)
        {
            string h = hex;
            bool chosen = string.Equals(CompanionArt.ColorHex, h, StringComparison.OrdinalIgnoreCase);
            var swatch = new Grid();
            swatch.Children.Add(new Ellipse { Width = 54, Height = 54, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)) });
            if (chosen) swatch.Children.Add(Tick(22, new Thickness(0, -4, -4, 0)));
            var b = Ui.Tile(swatch);
            b.ToolTip = colorName;
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Tag = chosen ? "current" : "color:" + h;
            b.Click += (_, _) => { Labs.Put("companion.color", h); Host.RefreshCompanion(); Go(_step, keep: true); };
            colors.Children.Add(b);
        }
        body.Children.Add(colors);

        body.Children.Add(Section("Its name"));
        var name = Ui.PanelButton("\uE70F", CompanionArt.Name);
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.Tag = "companion-name";
        name.Click += async (_, _) =>
        {
            var n = await Host.PromptAsync("Name your companion", null, CompanionArt.Name);
            if (n != null && n.Trim().Length > 0)
            {
                Labs.Put("companion.name", n.Trim().Length > 16 ? n.Trim()[..16] : n.Trim());
                Host.RefreshCompanion();
                Host.CompanionReact("hearts");
            }
            Go(_step, keep: true);
        };
        body.Children.Add(name);
        body.Children.Add(Note("Outfits are earned by playing, accessories come from the playground's Star Shop, and every reaction can be tried in Settings > Home screen > Companion."));
        return page;
    }

    // ================================================================ extras

    private FrameworkElement ExtrasPage()
    {
        var s = Host.Settings;
        var (page, body) = Page("A few extras", "Turn on what you like. Each one is a press away later in Settings, too.");

        body.Children.Add(Section("While you play"));
        var play = Wrap();
        var installed = WidgetInstaller.InstalledVersion();
        var bundled = WidgetInstaller.Find();
        if (bundled != null || installed != null)
        {
            bool upToDate = installed != null && (bundled == null || installed >= bundled.Version);
            play.Children.Add(Action("\uE7FC", "Game Bar widget", "SquareDeck in Game Bar (the Xbox button, or Win + G): TDP, frame cap, FPS overlay, brightness and the Ally's modes, over your game.",
                upToDate ? "Installed" : installed == null ? "Install" : "Update", upToDate, async () =>
                {
                    if (upToDate || bundled == null) return;
                    s.GameBarWidget = true;
                    WidgetBridge.Start();
                    await Host.InstallWidgetWithScreenAsync(bundled, update: installed != null);
                }));
        }
        play.Children.Add(Toggle("\uE8EC", "Sale alerts", "A notification when a game on your Steam wishlist goes on sale.", s.SaleAlerts, () => s.SaleAlerts = !s.SaleAlerts));
        body.Children.Add(play);

        body.Children.Add(Section("Starting up and staying current"));
        var start = Wrap();
        start.Children.Add(Toggle("\uE7E8", "Open when I sign in", "SquareDeck starts with Windows, ready with a controller.", Startup.RunAtSignIn, () =>
        {
            try { Startup.RunAtSignIn = !Startup.RunAtSignIn; } catch (Exception) { }
        }));
        start.Children.Add(Toggle("\uE768", "Startup animation", "A short animation and chime while SquareDeck opens.", s.StartupIntro, () => s.StartupIntro = !s.StartupIntro));
        start.Children.Add(Toggle("\uE895", "Check for updates", "When SquareDeck starts and every 12 hours, and it updates itself in a few seconds.", s.AutoUpdateCheck, () => s.AutoUpdateCheck = !s.AutoUpdateCheck));
        start.Children.Add(Toggle("\uE789", "What's new after an update", "The new version's changes, once, after it updates.", s.WhatsNewAfterUpdate, () => s.WhatsNewAfterUpdate = !s.WhatsNewAfterUpdate));
        body.Children.Add(start);

        body.Children.Add(Section("Comfort"));
        var comfort = Wrap();
        comfort.Children.Add(Toggle("\uE916", "Reduce motion", "Calmer: fewer animations and moving effects.", s.ReduceMotion, () => { s.ReduceMotion = !s.ReduceMotion; Host.ApplySettings(); }));
        body.Children.Add(comfort);
        body.Children.Add(Note("Larger text, high contrast and the rest of accessibility are in Settings > Accessibility."));
        return page;
    }

    // ================================================================ ready

    private FrameworkElement ReadyPage()
    {
        var s = Host.Settings;
        string first = MainWindow.FirstName();
        var (page, body) = Page(first.Length > 0 ? $"You're all set, {first}!" : "You're all set!", "Here's what you chose. Press any of them to change it, or start playing.");

        var chips = Wrap();
        void Chosen(string glyph, string label, string value, string step)
        {
            if (Array.FindIndex(Steps, x => x.Id == step) < 0) return;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 18, "AccentBrush");
            g.VerticalAlignment = VerticalAlignment.Center;
            g.Margin = new Thickness(0, 1, 12, 0);
            line.Children.Add(g);
            var words = new StackPanel();
            words.Children.Add(Ui.Text(label, 12, "SubtleBrush"));
            words.Children.Add(Ui.Text(value, 16, "FgBrush", bold: true));
            line.Children.Add(words);
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = line, Padding = new Thickness(20, 10, 24, 10), Margin = new Thickness(0, 0, 10, 10), Tag = "chip:" + step };
            b.Click += (_, _) => Go(IndexOf(step));
            chips.Children.Add(b);
        }
        if (Handheld.IsHandheld)
            Chosen("\uE781", "Lights", StyleLights.On ? "Match the UI style" : AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights).Name ?? "As they are", "device");
        Chosen("\uE790", "Style", UiStyles.Current.Name, "look");
        Chosen("\uE767", "Sound", !s.UiSounds ? "Silent" : s.NavSoundSet.Length > 0 ? UiSound.SetName(s.NavSoundSet) : UiSound.SetName(s.SoundSet), "sound");
        Chosen("\uE8D6", "Music", BackgroundMusic.NameOf(s.MenuMusic), "sound");
        Chosen("\uE80F", "Covers", CoverStyles.On ? CoverStyles.Shapes.FirstOrDefault(x => x.Id == CoverStyles.Read("home").Shape).Name ?? "Rounded" : "Rounded", "home");
        Chosen("\uE7FC", "Buttons", Hints.Styles.FirstOrDefault(x => x.Item1 == s.GlyphStyle).Item2 ?? "Xbox", "controller");
        var stores = new List<string> { "Steam" };
        if (s.OtherStores) stores.Add("other launchers");
        if (StoreAccounts.EpicSignedIn && Labs.On(StoreAccounts.EpicLab)) stores.Add("Epic");
        if (StoreAccounts.XboxSignedIn && Labs.On(StoreAccounts.XboxLab)) stores.Add("Xbox");
        Chosen("\uE768", "Games", string.Join(", ", stores), "games");
        Chosen("\uE899", "Companion", Labs.On("companion") ? CompanionArt.Name : "Off", "companion");
        body.Children.Add(chips);

        body.Children.Add(Section("Worth remembering"));
        var tips = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 1060 };
        void Tip(string keyHint, string title, string text)
        {
            var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 10, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            var st = new StackPanel();
            var key = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 3, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            var keyText = Ui.Text(keyHint, 14, "FgBrush", bold: true);
            key.Child = keyText;
            key.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            st.Children.Add(key);
            st.Children.Add(Ui.Text(title, 16, "FgBrush", bold: true));
            var t = Ui.Text(text, 13, "SubtleBrush");
            t.TextWrapping = TextWrapping.Wrap;
            st.Children.Add(t);
            card.Child = st;
            tips.Children.Add(card);
        }
        Tip(Hints.KeyLabel("Options"), "Game options", "Details, artwork, notebook, collections, and which store to install from.");
        Tip(Hints.Keyboard ? "Q" : "View", "Quick settings", "Volume, brightness, TDP and Wi-Fi, from anywhere.");
        Tip(Hints.Keyboard ? "/" : "Menu", "Search", "Games, apps, settings and files.");
        Tip(Hints.Keyboard ? "Ctrl+Alt+Q" : "Guide", "While you play", "Bring SquareDeck back, or open its Game Bar widget.");
        body.Children.Add(tips);

        body.Children.Add(Section("Worth a look"));
        var places = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 1060 };
        void Place(string glyph, string title, string text)
        {
            var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 10, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            var st = new StackPanel();
            var g = Ui.Glyph(glyph, 22, "AccentBrush");
            g.HorizontalAlignment = HorizontalAlignment.Left;
            g.Margin = new Thickness(0, 0, 0, 10);
            st.Children.Add(g);
            st.Children.Add(Ui.Text(title, 16, "FgBrush", bold: true));
            var t = Ui.Text(text, 13, "SubtleBrush");
            t.TextWrapping = TextWrapping.Wrap;
            st.Children.Add(t);
            card.Child = st;
            places.Children.Add(card);
        }
        Place("\uE7FC", "Game Genie", "Tips, guides and help for the game you're on.");
        Place("\uE7F4", "Recomp Zone", "Native PC ports of console classics, installed in a press.");
        Place("\uE7FC", "Retro Lounge", "Your retro games with shaders, bezels and box art.");
        Place("\uE790", "Style gallery", "Every style previewed live, in Settings > Themes.");
        body.Children.Add(places);

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
