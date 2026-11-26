using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A page or dialog shown on top of the home screen. Screens are built in code and navigated with
/// normal keyboard focus, which the main window also drives from the controller.
/// </summary>
public abstract class Screen : UserControl
{
    public const string DefaultHints = "{Select}  Select        {Back}  Back";

    public MainWindow Host { get; internal set; } = null!;

    /// <summary>Dialogs are drawn over the screen below instead of replacing it.</summary>
    public virtual bool IsDialog => false;

    internal IInputElement? LastFocus { get; set; }

    /// <summary>How far each of the page's scrolling areas was scrolled when another page opened over it (put back on return).</summary>
    internal List<double>? SavedScroll { get; set; }

    protected Screen()
    {
        Focusable = false;
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    /// <summary>Called once, when the screen is first shown.</summary>
    public virtual void OnShown() { }

    /// <summary>Called when a screen that was opened on top of this one has closed.</summary>
    public virtual void OnResumed() { }

    public virtual void OnClosed() { }

    /// <summary>Gives the screen first go at an input. Return true if it was handled.</summary>
    public virtual bool OnAction(NavAction action) => false;

    /// <summary>Puts the highlight back where it was (when that's still on this page), or on the default.</summary>
    protected void Refocus(IInputElement? element)
    {
        if (element is UIElement e && IsAncestorOf(e) && e.IsVisible) e.Focus();
        else FocusDefault();
    }

    public virtual void FocusDefault() => FindFirstFocusable(this)?.Focus();

    /// <summary>Focuses the default control once layout has caught up.</summary>
    public void FocusDefaultLater() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Host != null && Host.IsTop(this)) FocusDefault();
        }));

    protected void FocusLater(UIElement? element) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Host == null || !Host.IsTop(this)) return;
            if (element == null || !element.Focus()) FocusDefault();
        }));

    private static Control? FindFirstFocusable(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject d) continue;
            if (child is Control { Focusable: true, IsEnabled: true, Visibility: Visibility.Visible } c and (Button or TextBox or Slider))
                return c;
            if (child is UIElement { Visibility: not Visibility.Visible }) continue;
            if (FindFirstFocusable(d) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>The right thumbstick, each frame it is pushed (-1 to 1). True if the page scrolled itself.</summary>
    public virtual bool OnRightStick(double y) => false;

    /// <summary>The right stick with both directions and whether it's pressed in, every frame. True if the page used it.</summary>
    public virtual bool OnRightStick(double x, double y, bool pressed) => y != 0 && OnRightStick(y);

    /// <summary>Called when another page or pop-up opens on top of this one.</summary>
    public virtual void OnCovered() { }

    /// <summary>
    /// SquareDeck went to the background (true) or came back (false): pause and resume anything the page animates or
    /// refreshes on its own. Music or video the person chose to play keeps going.
    /// </summary>
    public virtual void OnAppRest(bool resting) { }

    private Grid? _pageRoot;

    /// <summary>
    /// Washes the page in a soft diagonal gradient of one color (a game's cover color), fading into the
    /// normal background so text stays readable.
    /// </summary>
    protected void SetPageTint(System.Windows.Media.Color color)
    {
        if (_pageRoot == null) return;
        var bg = (Application.Current.Resources["BgBrush"] as System.Windows.Media.SolidColorBrush)?.Color
                 ?? System.Windows.Media.Colors.Black;

        System.Windows.Media.Color Mix(double amount) => System.Windows.Media.Color.FromRgb(
            (byte)(bg.R + (color.R - bg.R) * amount),
            (byte)(bg.G + (color.G - bg.G) * amount),
            (byte)(bg.B + (color.B - bg.B) * amount));

        // Strongest in the top-left corner, gone by the bottom-right. Lighter on the light theme.
        double strength = Theme.CurrentIsDark ? 0.62 : 0.4;
        var brush = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(Mix(strength), 0));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(Mix(strength * 0.55), 0.4));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(Mix(strength * 0.2), 0.75));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(bg, 1));
        brush.Freeze();
        _pageRoot.Background = brush;
        // The game's own colors take the place of the wallpaper on this page.
        if (_wallpaperLayer != null) _wallpaperLayer.Visibility = Visibility.Collapsed;
        if (_wallpaperScrim != null) _wallpaperScrim.Visibility = Visibility.Collapsed;
    }

    private TextBlock? _titleText;
    private DockPanel? _header;

    /// <summary>Shows a logo image where the page title is. The title text stays as a fallback until then.</summary>
    protected void SetTitleLogo(System.Windows.Media.ImageSource logo)
    {
        if (_header == null || _titleText == null || !_header.Children.Contains(_titleText)) return;

        var image = new Image
        {
            Source = logo,
            Height = 60,
            MaxWidth = 460,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);

        FrameworkElement element = image;
        if (!Theme.CurrentIsDark)
        {
            // Logos are usually drawn light, for dark backgrounds: give them one on the light theme.
            element = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 4, 14, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = image
            };
        }
        element.Margin = new Thickness(0, -8, 0, 0);

        int index = _header.Children.IndexOf(_titleText);
        _header.Children.RemoveAt(index);
        _header.Children.Insert(index, element);
    }

    private System.Windows.Shapes.Rectangle? _wallpaperLayer, _wallpaperScrim;
    private Image? _backdrop;

    /// <summary>
    /// The page's background: the home screen's wallpaper (softened), or the game's artwork in styles that show it.
    /// Rebuilt by <see cref="RefreshBackdrop"/> when the look changes while the page is open.
    /// </summary>
    private void AddBackdrop(Grid root)
    {
        int at = 0;
        void Put(UIElement e)
        {
            if (e is FrameworkElement fe) fe.Tag = "page-backdrop";
            root.Children.Insert(at++, e);
        }
        // The same wallpaper as the home screen, softened a little more so pages of text stay readable.
        // Styles that show the game's artwork behind the home screen show it, blurred, behind pages too.
        var style = UiStyles.Current;
        if (style.PageHero && Theme.CurrentHero != null && !Perf.Active)
        {
            // The artwork comes already blurred when it can (blurring it live made every page open with a stall).
            var soft = Theme.CurrentHeroSoft;
            var hero = new Image
            {
                Source = soft ?? Theme.CurrentHero, Stretch = System.Windows.Media.Stretch.UniformToFill, IsHitTestVisible = false,
                Margin = new Thickness(-60), Opacity = style.Glass ? 1 : 0.9,
                Effect = soft != null ? null : new System.Windows.Media.Effects.BlurEffect { Radius = style.Glass ? 50 : 36, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance }
            };
            // A live blur is kept as a ready-made picture so it isn't redone whenever something on the page moves.
            var heroHost = new Grid { IsHitTestVisible = false, ClipToBounds = true, CacheMode = soft != null ? null : new System.Windows.Media.BitmapCache(0.5), Children = { hero } };
            Grid.SetRowSpan(heroHost, 3);
            Put(heroHost);
            var heroScrim = new System.Windows.Shapes.Rectangle { Opacity = style.PageScrim, IsHitTestVisible = false };
            heroScrim.SetResourceReference(Shape.FillProperty, "BgBrush");
            Grid.SetRowSpan(heroScrim, 3);
            Put(heroScrim);
        }
        else if (Theme.CurrentWallpaper != null)
        {
            _wallpaperLayer = new System.Windows.Shapes.Rectangle { Fill = Theme.CurrentWallpaper, IsHitTestVisible = false };
            UIElement wallpaper = _wallpaperLayer;
            // A moving background that's already blurred isn't blurred again behind each page: blurring the copy too,
            // every frame and for every open page, was enough work to make some graphics drivers drop frames (black).
            bool alreadySoft = Theme.CurrentWallpaper is System.Windows.Media.VisualBrush && (Application.Current.MainWindow as MainWindow)?.Settings.DynamicBlur >= 20;
            if (style.Glass && !Perf.Active && !alreadySoft)
            {
                _wallpaperLayer.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 40 };
                _wallpaperLayer.Margin = new Thickness(-60);
                // A still wallpaper is cached once blurred; a moving one changes every frame, so caching wouldn't help.
                var host = new Grid { IsHitTestVisible = false, ClipToBounds = true, Children = { _wallpaperLayer } };
                if (Theme.CurrentWallpaper is not System.Windows.Media.VisualBrush) host.CacheMode = new System.Windows.Media.BitmapCache(0.5);
                wallpaper = host;
            }
            Grid.SetRowSpan(wallpaper, 3);
            Put(wallpaper);
            _wallpaperScrim = new System.Windows.Shapes.Rectangle { Opacity = style.Glass ? style.PageScrim : 0.55, IsHitTestVisible = false };
            _wallpaperScrim.SetResourceReference(Shape.FillProperty, "BgBrush");
            Grid.SetRowSpan(_wallpaperScrim, 3);
            Put(_wallpaperScrim);
        }

    }

    /// <summary>
    /// A light tick on the controller each time the highlight moves to another button, and a firmer one when a button
    /// is pressed or a slider moves (with "Vibrate while moving through menus" on). Used by quick settings and the
    /// power menu.
    /// </summary>
    protected void RumbleOnMove()
    {
        AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (Host?.Settings.RumbleInSettings == true && e.NewFocus is Button or Slider && e.OldFocus is Button or Slider && !ReferenceEquals(e.NewFocus, e.OldFocus))
                Host.Rumble(16);
        }), true);
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, _) =>
        {
            if (Host?.Settings.RumbleInSettings == true) Host.Rumble(40);
        }), true);
        AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) =>
        {
            if (Host?.Settings.RumbleInSettings == true && IsKeyboardFocusWithin) Host.Rumble(12);
        }), true);
    }

    /// <summary>Lays something over the whole page (a progress card, for instance).</summary>
    protected void ShowOver(UIElement element)
    {
        if (_pageRoot is not Grid root) return;
        Grid.SetRowSpan(element, Math.Max(1, root.RowDefinitions.Count));
        root.Children.Add(element);
    }

    /// <summary>Puts the current UI style's wallpaper or artwork behind the page (after a change of style or wallpaper).</summary>
    public void RefreshBackdrop()
    {
        if (_pageRoot is not Grid root) return;
        foreach (var old in root.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "page-backdrop").ToList()) root.Children.Remove(old);
        _wallpaperLayer = _wallpaperScrim = null;
        AddBackdrop(root);
    }

    /// <summary>Standard full-page layout: title bar with a Back button, body, and a hint footer.</summary>
    /// <param name="wide">Use nearly the full width (for pages that are a big grid).</param>
    protected void SetPage(string title, UIElement body, string hints = DefaultHints, bool wide = false)
    {
        double side = wide ? 26 : 62;
        var root = new Grid { ClipToBounds = true };
        _pageRoot = root;
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        AddBackdrop(root);

        // A blurred picture behind everything (album art on the Music page); empty until asked for.
        _backdrop = new Image
        {
            Stretch = System.Windows.Media.Stretch.UniformToFill, Opacity = 0, Margin = new Thickness(-60),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 70 }
        };
        Grid.SetRowSpan(_backdrop, 3);
        root.Children.Add(_backdrop);

        // The title, as the look draws it (Settings > Themes): bold, thin spaced capitals, or large and light.
        var design = Looks.Current.Design;
        var header = new DockPanel { Margin = new Thickness(side, 30, side, 0), VerticalAlignment = VerticalAlignment.Top };
        if (design.HeaderClock) header.Children.Add(HeaderClock());
        _titleText = design.Title switch
        {
            "spaced" => Ui.Text(Spaced(title), design.TitleSize, "FgBrush"),
            "light" => Ui.Text(title, design.TitleSize, "FgBrush"),
            _ => Ui.Text(title, design.TitleSize, "FgBrush", bold: true)
        };
        if (design.Title == "light") _titleText.FontWeight = FontWeights.Light;
        if (design.Title == "spaced") { _titleText.FontWeight = FontWeights.Light; _titleText.Margin = new Thickness(0, 6, 0, 0); }
        _header = header;
        header.Children.Add(_titleText);
        root.Children.Add(header);

        var bodyHost = new Grid { Margin = new Thickness(side, 0, side, 12) };
        _bodyHost = bodyHost;
        bodyHost.Children.Add(body);
        Grid.SetRow(bodyHost, 1);
        root.Children.Add(bodyHost);

        // The footer with the button hints, as the look draws it: a thin line over them, a solid band, or hints alone.
        var footer = new Grid { Margin = design.Footer == "bar" ? new Thickness(0) : new Thickness(30, 0, 30, 0) };
        if (design.Footer == "bar")
            footer.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0, 0, 0));
        else if (design.Footer == "line")
        {
            var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.15 };
            line.SetResourceReference(Shape.FillProperty, "FgBrush");
            footer.Children.Add(line);
        }
        var hint = new HintBar(hints); // the same button shapes as the home screen
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Margin = new Thickness(0, 0, design.Footer == "bar" ? 56 : 26, 0);
        footer.Children.Add(hint);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        _footer = footer;

        Content = root;
    }

    private Grid? _bodyHost;
    private Grid? _footer;

    /// <summary>Takes the colors of the UI style in use again (it may have just been changed, from Themes).</summary>
    protected void RefreshSwitch2Colors()
    {
        if (_pageRoot == null) return;
        Switch2Settings.ApplyColors(this);
        _pageRoot.Background = new System.Windows.Media.SolidColorBrush(Switch2Settings.Background);
    }

    /// <summary>
    /// The page drawn like the Switch 2's System Settings (Settings, and the Switch 2 style Library): the UI style's
    /// colors over its background, the gear (or another icon) and title on the left with a line under them, and a line
    /// over the button hints with the controller at the left.
    /// </summary>
    /// <summary>
    /// Switch 2 style for a page drawn its own way (the controller testers): the title with its icon and a line under
    /// it, and the page's cards, tracks and keys in the Switch 2 card color.
    /// </summary>
    protected void UseSwitch2Cards(string title, string glyph)
    {
        UseSwitch2Chrome(title, glyph);
        var card = new System.Windows.Media.SolidColorBrush(Switch2Settings.Card);
        card.Freeze();
        Resources["CardBrush"] = card;
        Resources["TileBrush"] = card;
        Resources["CircleBrush"] = card;
    }

    protected void UseSwitch2Chrome(string title, string glyph = "\uE713")
    {
        if (_pageRoot == null) return;
        Switch2Settings.ApplyColors(this);
        _pageRoot.Background = new System.Windows.Media.SolidColorBrush(Switch2Settings.Background);
        // The UI style's background shows through, as on other pages: its wallpaper (or the game artwork some styles
        // put behind pages) under a wash of the page color. The wash takes this page's own background color, since it
        // reads BgBrush from the page, so the Switch 2 colors stay and only the picture shows softly through them.

        if (_header != null && _titleText != null)
        {
            _header.Children.Remove(_titleText);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var gear = Ui.Glyph(glyph, 34, "FgBrush");
            gear.VerticalAlignment = VerticalAlignment.Center;
            gear.Margin = new Thickness(0, 2, 16, 0);
            name.Children.Add(gear);
            var text = Ui.Text(title, 34, "FgBrush");
            text.VerticalAlignment = VerticalAlignment.Center;
            name.Children.Add(text);
            _titleText = text;
            _header.Children.Add(name);
            _header.Margin = new Thickness(78, 26, 62, 0);
        }
        var under = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 0) };
        under.SetResourceReference(Shape.FillProperty, "S2Line");
        _pageRoot.Children.Add(under);

        if (_footer != null)
        {
            _footer.Background = null;
            _footer.Margin = new Thickness(40, 0, 40, 0);
            foreach (var old in _footer.Children.OfType<Rectangle>().ToList()) _footer.Children.Remove(old);
            var over = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            over.SetResourceReference(Shape.FillProperty, "S2Line");
            _footer.Children.Insert(0, over);
            FrameworkElement pad = Ui.DeviceArt(30) ?? Ui.Glyph("\uE7FC", 30, "FgBrush"); // your own handheld, with that experiment on
            pad.HorizontalAlignment = HorizontalAlignment.Left;
            pad.VerticalAlignment = VerticalAlignment.Center;
            pad.Margin = new Thickness(16, 2, 0, 0);
            _footer.Children.Add(pad);
        }
    }

    /// <summary>"Settings" as "S E T T I N G S": the thin, widely spaced capitals some looks title their pages with.</summary>
    private static string Spaced(string title) => string.Join("\u2009", title.ToUpperInvariant().ToCharArray());

    /// <summary>The battery and time at the right of the title bar, in looks that show them on every page.</summary>
    private static FrameworkElement HeaderClock()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 6, 0, 0) };
        DockPanel.SetDock(row, Dock.Right);
        var battery = Ui.Text("", 18, "SubtleBrush");
        battery.Margin = new Thickness(0, 0, 18, 0);
        var time = Ui.Text("", 22, "FgBrush", bold: true);
        row.Children.Add(battery);
        row.Children.Add(time);
        void Update()
        {
            time.Text = DateTime.Now.ToString("t");
            battery.Text = Battery.GetPercent() is int p ? p + "%" + (Battery.IsPluggedIn() ? "  \u26A1" : "") : "";
            battery.Visibility = battery.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        Update();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => Update();
        row.Loaded += (_, _) => { Update(); timer.Start(); };
        row.Unloaded += (_, _) => timer.Stop();
        return row;
    }

    /// <summary>Swaps the page's content for another layout (with the wide page margins).</summary>
    /// <summary>
    /// Takes an element out of whatever holds it, so it can go into a new layout (Windows refuses an element that's
    /// still in another one). Pages call this on their scroller before putting it into Switch2Pages.Shell.
    /// </summary>
    protected static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    protected void ReplaceBody(UIElement body)
    {
        if (_bodyHost == null) return;
        _bodyHost.Children.Clear();
        _bodyHost.Margin = new Thickness(26, 0, 26, 12);
        _bodyHost.Children.Add(body);
    }

    /// <summary>
    /// For full-window tools (Explorer, Task Manager) that draw their own header: drops the big page title
    /// and gives the space to the body.
    /// </summary>
    protected void CompactHeader()
    {
        if (_pageRoot == null) return;
        _pageRoot.RowDefinitions[0].Height = new GridLength(18);
        if (_header != null) _header.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Puts an element edge to edge behind the page's title and content (the game page's artwork banner),
    /// above the wallpaper and tint. Null removes it.
    /// </summary>
    protected void SetPageHero(UIElement? layer)
    {
        if (_pageRoot == null) return;
        if (_hero != null) _pageRoot.Children.Remove(_hero);
        _hero = layer;
        if (layer == null) return;
        Grid.SetRowSpan(layer, 2);
        int index = _backdrop != null ? _pageRoot.Children.IndexOf(_backdrop) + 1 : 0;
        _pageRoot.Children.Insert(index, layer);
    }

    private UIElement? _hero;

    /// <summary>Shows a picture, heavily blurred and dimmed, behind the page. Null removes it.</summary>
    /// <summary>The page's blurred background picture (used by views placed inside a page).</summary>
    public void SetBackdrop(System.Windows.Media.ImageSource? picture) => SetPageBackdrop(picture);

    protected void SetPageBackdrop(System.Windows.Media.ImageSource? picture)
    {
        if (_backdrop == null) return;
        if (picture != null) _backdrop.Source = picture;
        double target = picture == null ? 0 : 0.42;
        if (Theme.ReduceMotion) { _backdrop.BeginAnimation(OpacityProperty, null); _backdrop.Opacity = target; }
        else _backdrop.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(500)));
    }
}

/// <summary>A page that is a scrolling list of rows. Subclasses fill it in <see cref="Build"/>.</summary>
public abstract class ListScreen : Screen
{
    protected StackPanel Rows { get; } = new() { Width = 940, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };

    protected ListScreen(string title, string hints = DefaultHints) : this(title, hints, null) { }

    /// <summary>With <paramref name="layout"/>, the scrolling rows are placed inside a layout of the page's own (Settings' two panes).</summary>
    protected ListScreen(string title, string hints, Func<ScrollViewer, UIElement>? layout)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, // touch scrolling
            Focusable = false,
            Content = Rows
        };
        RowsScroll = scroll;
        // Remember the row with the highlight, so a rebuild (after changing a setting, or coming back from a page
        // opened from it) puts the highlight back on that row instead of the top of the page.
        Rows.PreviewGotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is not Button b) return;
            var buttons = Rows.Children.OfType<Button>().ToList();
            int i = buttons.IndexOf(b);
            if (i < 0) return;
            _lastIndex = i;
            _lastLabel = LabelOf(b)?.Label;
        };
        if (layout == null) SetPage(title, scroll, hints);
        else SetPage(title, layout(scroll), hints, wide: true);

        // Switch 2 style PC tools and info pages: the page's name with its icon and a line under it, and (after each
        // build) bar-and-name headings and flat rows with the rimmed highlight, in the UI style's colors.
        _s2Pc = layout == null && Switch2Pages.ListPage(this);
        if (_s2Pc)
        {
            UseSwitch2Chrome(title, Switch2Pages.PcGlyph(this));
            Rows.Width = double.NaN;
            Rows.MaxWidth = 1100;
            Rows.HorizontalAlignment = HorizontalAlignment.Stretch;
            Rows.Margin = new Thickness(60, 0, 60, 24);
        }
    }

    private readonly bool _s2Pc;

    protected ScrollViewer RowsScroll { get; }

    /// <summary>While true, rebuilding the rows leaves the focus where it is (Settings changing pane as you move).</summary>
    protected bool SuppressAutoFocus { get; set; }

    private int _lastIndex;
    private string? _lastLabel;

    protected abstract void Build();

    /// <summary>After the rows are built and grouped: a page can redraw them its own way (New Settings UI).</summary>
    protected virtual void AfterBuild() { }

    public override void OnShown() => Rebuild();

    /// <summary>Recreates the rows and keeps the highlight on the same position.</summary>
    public void Rebuild()
    {
        var buttons = Rows.Children.OfType<Button>().ToList();
        int index = buttons.FindIndex(b => b.IsKeyboardFocused);
        if (index >= 0) { _lastIndex = index; _lastLabel = LabelOf(buttons[index])?.Label; }
        else index = _lastIndex;
        string? label = _lastLabel;
        double offset = RowsScroll.VerticalOffset;

        Rows.Children.Clear();
        Build();
        Ui.GroupRows(Rows); // runs of rows become grouped cards
        AfterBuild();
        if (_s2Pc) Switch2Settings.Restyle(Rows);

        buttons = Rows.Children.OfType<Button>().ToList();
        if (buttons.Count == 0 || SuppressAutoFocus) return;
        // The same row as before (by name, as rows can come and go), at the same scroll position.
        if (FocusLabel == null && label != null && buttons.FirstOrDefault(b => LabelOf(b)?.Label == label) is { } same)
        {
            RowsScroll.ScrollToVerticalOffset(offset);
            FocusLater(same);
            return;
        }
        // Opened from Search: land on the row that was searched for.
        if (FocusLabel != null)
        {
            var wanted = buttons.FirstOrDefault(b => LabelOf(b)?.Label == FocusLabel);
            FocusLabel = null;
            if (wanted != null)
            {
                FocusLater(wanted);
                return;
            }
        }
        FocusLater(buttons[Math.Clamp(index, 0, buttons.Count - 1)]);
    }

    /// <summary>A row to put the highlight on when the page opens (set by Search).</summary>
    internal string? FocusLabel { get; set; }

    /// <summary>Builds the page off screen and returns each row's name and subtitle (for Search).</summary>
    internal List<(string Label, string? Subtitle)> RowLabels()
    {
        Rows.Children.Clear();
        Build();
        var result = new List<(string, string?)>();
        foreach (var b in Rows.Children.OfType<Button>())
            if (LabelOf(b) is { } l && l.Label.Length > 0) result.Add(l);
        Rows.Children.Clear();
        return result;
    }

    /// <summary>The name and subtitle written on a row (icons skipped).</summary>
    internal static (string Label, string? Subtitle)? LabelOf(Button row)
    {
        var texts = new List<TextBlock>();
        void Walk(object node)
        {
            if (node is TextBlock t && !Equals(t.FontFamily, Ui.IconFont) && !string.IsNullOrWhiteSpace(t.Text)) texts.Add(t);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(row);
        if (texts.Count == 0) return null;
        var sub = texts.Skip(1).FirstOrDefault(t => t.FontSize <= 14.5 && t.Parent is StackPanel);
        return (texts[0].Text, sub?.Text);
    }

    /// <summary>Recreates the rows with the highlight back on the first one (after moving to a different list).</summary>
    /// <summary>Forgets which row had the highlight (a different page is about to be built).</summary>
    protected void ForgetRow()
    {
        _lastIndex = 0;
        _lastLabel = null;
    }

    protected void RebuildFromTop()
    {
        _lastIndex = 0;
        _lastLabel = null;
        System.Windows.Input.Keyboard.ClearFocus();
        Rebuild();
    }

    protected Button AddRow(string label, string? value = null, string? glyph = null, Image? icon = null, Action? click = null, string? subtitle = null)
    {
        var row = Ui.Row(label, value, glyph, icon, subtitle);
        if (click != null) row.Click += (_, _) => click();
        Rows.Children.Add(row);
        return row;
    }

    protected void AddHeader(string text) => Rows.Children.Add(Ui.Header(text));

    protected void AddNote(string text)
    {
        var t = Ui.Text(text, 15, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        t.LineHeight = 22;
        t.Margin = new Thickness(8, 6, 8, 8);
        Rows.Children.Add(t);
    }
}

/// <summary>A page that is a scrolling grid of tiles.</summary>
public abstract class GridScreen : Screen
{
    protected WrapPanel Tiles { get; } = new();
    protected StackPanel Body { get; } = new();

    protected GridScreen(string title, string hints = DefaultHints)
    {
        Body.Children.Add(Tiles);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, // touch scrolling
            Focusable = false,
            Content = Body
        };
        GridScroll = scroll;
        SetPage(title, scroll, hints);
    }

    protected ScrollViewer GridScroll { get; }

    /// <summary>
    /// Switch 2 style: the title with its icon and a line under it, tiles in the card color with the rimmed highlight
    /// (redrawn as they're added), and, given <paramref name="nav"/>, the page's sections down the left.
    /// </summary>
    protected void UseSwitch2Grid(string title, string glyph, UIElement? nav = null, UIElement? underNav = null)
    {
        UseSwitch2Chrome(title, glyph);
        var card = new System.Windows.Media.SolidColorBrush(Switch2Settings.Card);
        card.Freeze();
        Resources["TileBrush"] = card;
        Resources["CircleBrush"] = card;
        Body.Margin = new Thickness(14, 8, 18, 24); // room for the highlight's rim and glow
        if (nav != null)
        {
            Detach(GridScroll); // still in the old layout: let go of it first
            ReplaceBody(Switch2Pages.Shell(nav, GridScroll, underNav));
        }
        Switch2Pages.AutoRestyle(Body); // the tiles, and any rows of tiles a page adds beside them
    }

    protected Button AddTile(UIElement visual, string? caption, double width, Action click, object? tag = null,
        Panel? into = null)
    {
        var stack = new StackPanel { Width = width };
        stack.Children.Add(visual);
        if (caption != null)
        {
            var c = Ui.Text(caption, 15, "FgBrush", bold: true);
            c.HorizontalAlignment = HorizontalAlignment.Left;
            c.TextTrimming = TextTrimming.CharacterEllipsis;
            c.Margin = new Thickness(2, 8, 2, 0);
            LookChrome.StyleTileCaption(c);
            stack.Children.Add(c);
        }
        var tile = Ui.Tile(stack);
        tile.Tag = tag;
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.Click += (_, _) => click();
        (into ?? Tiles).Children.Add(tile);
        return tile;
    }

    /// <summary>A fixed-size box with the tile background holding an image that fills it.</summary>
    public static Border ImageBox(double width, double height, out Image image)
    {
        image = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill };
        var grid = new Grid();
        grid.Children.Add(image);
        var box = new Border { Width = width, Height = height, ClipToBounds = true, Child = grid };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        // Follow the tile-corner style chosen in Themes.
        // Pages always round their pictures a little, whatever the home row uses.
        double corner = LookChrome.TileCorner(Math.Max(Theme.TileCorner, 10));
        box.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, width, height), corner, corner);
        box.CornerRadius = new CornerRadius(corner);
        LookChrome.StyleTileBox(box);
        return box;
    }

    /// <summary>A fixed-size box showing an icon, for action tiles.</summary>
    public static Border GlyphBox(double width, double height, string glyph)
    {
        var box = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(LookChrome.TileCorner(Math.Max(Theme.TileCorner, 10))), Child = Ui.Glyph(glyph, 44, "AccentBrush") };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        LookChrome.StyleTileBox(box);
        return box;
    }
}

/// <summary>
/// A dialog: title, optional message, optional text box with an on-screen keyboard, and a column of
/// choices (each optionally with an icon).
/// </summary>
public sealed class MenuScreen : Screen
{
    private static readonly string[] KeyRows = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm._@" };

    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox? _input;
    private readonly List<(Button Key, char Letter)> _letterKeys = new();
    private bool _shift;
    private int _result = -1;

    public override bool IsDialog => true;

    /// <summary>Index of the chosen option, or -1 if the dialog was dismissed.</summary>
    public Task<int> Result => _done.Task;

    public string InputText => _input?.Text ?? "";

    /// <param name="glyphs">Optional icon (Segoe MDL2 glyph) for each option, in the same order.</param>
    /// <param name="secret">The typed text shows as dots (a password).</param>
    public MenuScreen(string title, string? message, IReadOnlyList<string> options, string? inputInitial = null,
        IReadOnlyList<string>? glyphs = null, bool secret = false)
    {
        var panel = new StackPanel();

        var heading = Ui.Text(title, 26);
        heading.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(heading);

        if (!string.IsNullOrEmpty(message))
        {
            var m = Ui.Text(message, 18, "SubtleBrush");
            m.TextWrapping = TextWrapping.Wrap;
            m.TextTrimming = TextTrimming.None;
            m.Margin = new Thickness(0, 12, 0, 0);
            // Long text (a queue, a long explanation) scrolls instead of pushing the buttons off the page.
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                PanningMode = PanningMode.VerticalOnly,
                Focusable = false,
                Content = m
            });
        }

        if (inputInitial != null)
        {
            _input = new TextBox
            {
                Text = inputInitial,
                FontSize = 20,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 16, 0, 0),
                BorderThickness = new Thickness(2),
                FocusVisualStyle = null
            };
            // In the page's own colors rather than Windows' white box.
            _input.SetResourceReference(Control.BackgroundProperty, "CircleBrush");
            _input.SetResourceReference(Control.ForegroundProperty, "FgBrush");
            _input.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            _input.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
            _input.SetResourceReference(TextBox.SelectionBrushProperty, "AccentBrush");
            if (secret)
            {
                // A password: the box's own text is invisible and dots stand in for it (the caret still shows).
                _input.Foreground = System.Windows.Media.Brushes.Transparent;
                var dots = Ui.Text("", 20, "FgBrush");
                dots.Margin = new Thickness(16, 27, 12, 0);
                dots.IsHitTestVisible = false;
                _input.TextChanged += (_, _) => dots.Text = new string('\u2022', _input.Text.Length);
                var holder = new Grid();
                holder.Children.Add(_input);
                holder.Children.Add(dots);
                panel.Children.Add(holder);
            }
            else panel.Children.Add(_input);
            // The built-in keys, unless Settings asks for Windows' touch keyboard (which opens on focus, below).
            if (!TouchKeyboard.UseWindows) panel.Children.Add(BuildKeyboard());
        }

        var buttons = new StackPanel { Margin = new Thickness(0, inputInitial != null ? 8 : 20, 0, 0) };
        _choices = buttons;
        _heading = heading;
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var row = Ui.Row(options[i], glyph: glyphs != null && i < glyphs.Count ? glyphs[i] : null);
            row.Click += (_, _) => Choose(index);
            buttons.Children.Add(row);
        }
        // Many options (a long list of choices) scroll too.
        Ui.GroupRows(buttons); // the choices read as one rounded group
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = inputInitial != null ? 130 : 440,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = buttons
        });

        var box = new Border
        {
            Width = inputInitial != null ? (TouchKeyboard.UseWindows ? 680 : 760) : 620,
            Padding = new Thickness(34, 28, 34, 28),
            CornerRadius = new CornerRadius(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center,
            // Above Windows' touch keyboard, which covers the bottom of the screen.
            VerticalAlignment = inputInitial != null && TouchKeyboard.UseWindows ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = inputInitial != null && TouchKeyboard.UseWindows ? new Thickness(0, 50, 0, 0) : new Thickness(0),
            Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");

        var backdrop = new Grid
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0, 0, 0))
        };
        backdrop.Children.Add(Ui.SkinBox(box));
        Content = backdrop;
        _box = box;
        _shade = backdrop;
    }

    private StackPanel? _choices;
    private TextBlock? _heading;
    private Border? _box;
    private Grid? _shade;

    /// <summary>
    /// Drawn like the Switch 2's pop-up lists (New Settings UI, over Settings): a light floating card, the choices on
    /// thin lines with a round mark at the right, filled in blue for the one in use (a choice written with ✓).
    /// </summary>
    /// <param name="marks">The round mark at each choice's right (a list of options); off for a menu of things to do
    /// (Open Terminal's Command Prompt / PowerShell), which are plain rows.</param>
    public void UseSwitch2Look(bool marks = true)
    {
        if (_choices == null || _box == null) return;
        Switch2Settings.ApplyColors(this);
        if (_shade != null) _shade.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0, 0, 0));
        _box.Background = new System.Windows.Media.SolidColorBrush(Switch2Settings.Card);
        _box.BorderThickness = new Thickness(0);
        _box.CornerRadius = new CornerRadius(16);
        _box.Padding = new Thickness(26, 24, 26, 20);
        if (_heading != null) { _heading.FontSize = 22; _heading.FontWeight = FontWeights.Normal; }
        foreach (var row in _choices.Children.OfType<Button>())
        {
            row.Style = Switch2Settings.RowStyle;
            row.Margin = new Thickness(0);
            row.Resources.Remove("RowCorner");
            if (row.Content is not Grid grid) continue;
            var label = grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 1);
            bool chosen = false;
            if (label != null && label.Text.StartsWith("\u2713"))
            {
                chosen = true;
                label.Text = label.Text.TrimStart('\u2713').TrimStart();
                label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }
            foreach (var badge in grid.Children.OfType<FrameworkElement>().Where(e => Grid.GetColumn(e) == 0).ToList()) badge.Visibility = Visibility.Collapsed;
            if (!marks && !chosen) continue;
            var ring = new Grid { Width = 30, Height = 30, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var outer = new Ellipse { StrokeThickness = chosen ? 0 : 2 };
            if (chosen) outer.SetResourceReference(Shape.FillProperty, "AccentBrush");
            else outer.SetResourceReference(Shape.StrokeProperty, "SubtleBrush");
            ring.Children.Add(outer);
            if (chosen) ring.Children.Add(new Ellipse { Width = 11, Height = 11, Fill = System.Windows.Media.Brushes.White });
            Grid.SetColumn(ring, 2);
            grid.Children.Add(ring);
        }
    }

    // ---------------------------------------------------------------- on-screen keyboard

    /// <summary>Keys you can reach with the controller, so typing never needs a real keyboard.</summary>
    private FrameworkElement BuildKeyboard()
    {
        var keyboard = new StackPanel { Margin = new Thickness(-8, 8, -8, 0) };

        foreach (var letters in KeyRows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (char c in letters)
            {
                var key = MakeKey(c.ToString(), 50);
                char letter = c;
                key.Click += (_, _) => Type((_shift ? char.ToUpperInvariant(letter) : letter).ToString());
                _letterKeys.Add((key, letter));
                row.Children.Add(key);
            }
            keyboard.Children.Add(row);
        }

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var shift = MakeKey("Shift", 104);
        shift.Click += (_, _) =>
        {
            _shift = !_shift;
            foreach (var (key, letter) in _letterKeys)
                ((TextBlock)((Border)key.Content).Child).Text = (_shift ? char.ToUpperInvariant(letter) : letter).ToString();
        };
        bottom.Children.Add(shift);
        var space = MakeKey("Space", 236);
        space.Click += (_, _) => Type(" ");
        bottom.Children.Add(space);
        var erase = MakeKey("⌫  Delete", 138);
        erase.Click += (_, _) =>
        {
            if (_input is { Text.Length: > 0 }) _input.Text = _input.Text[..^1];
            MoveCaretToEnd();
        };
        bottom.Children.Add(erase);
        var clear = MakeKey("Clear", 104);
        clear.Click += (_, _) => { if (_input != null) _input.Text = ""; };
        bottom.Children.Add(clear);
        keyboard.Children.Add(bottom);
        return keyboard;
    }

    private static Button MakeKey(string label, double width)
    {
        var text = Ui.Text(label, 19);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var face = new Border { Width = width, Height = 38, CornerRadius = new CornerRadius(4), Child = text };
        face.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var key = Ui.Tile(face);
        key.Margin = new Thickness(-5, -5, -5, -5); // tiles carry a wide highlight ring; pull the keys together
        return key;
    }

    private void Type(string text)
    {
        if (_input == null) return;
        _input.Text += text;
        MoveCaretToEnd();
    }

    private void MoveCaretToEnd()
    {
        if (_input != null) _input.CaretIndex = _input.Text.Length;
    }

    // ---------------------------------------------------------------- result

    private void Choose(int index)
    {
        _result = index;
        Host.Pop(this);
    }

    public override void OnClosed() => _done.TrySetResult(_result);

    public override void FocusDefault()
    {
        if (_input != null)
        {
            _input.Focus();
            _input.SelectAll();
            if (TouchKeyboard.UseWindows) _ = TouchKeyboard.ShowAsync();
        }
        else base.FocusDefault();
    }

    /// <summary>A choice has to be made: B doesn't close it (a mandatory update, say).</summary>
    public bool Mandatory { get; init; }

    public override bool OnAction(NavAction action)
    {
        if (Mandatory && action == NavAction.Back) return true;
        // Enter / A while typing confirms with the first option.
        if (action == NavAction.Accept && _input != null && _input.IsKeyboardFocused)
        {
            Choose(0);
            return true;
        }
        return false;
    }
}

/// <summary>"Closing…" and similar: a message with moving dots that stays until the work is done.</summary>
public sealed class BusyScreen : Screen
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly TextBlock _dots = Ui.Text("", 30, "AccentBrush", bold: true);
    private int _step;

    public override bool IsDialog => true;

    /// <summary>When set, B (or the Cancel button) stops the wait: for waits on something outside SquareDeck, like a browser sign-in.</summary>
    private readonly Action? _cancel;

    public BusyScreen(string message, System.Windows.Media.ImageSource? picture = null, Action? cancel = null)
    {
        _cancel = cancel;
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (picture != null)
        {
            var art = new Border
            {
                Width = 150, Height = 150, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new System.Windows.Media.ImageBrush(picture) { Stretch = System.Windows.Media.Stretch.UniformToFill }
            };
            panel.Children.Add(art);
        }
        var text = Ui.Text(message, 26);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.TextWrapping = TextWrapping.Wrap;
        text.TextAlignment = TextAlignment.Center;
        panel.Children.Add(text);
        _dots.HorizontalAlignment = HorizontalAlignment.Center;
        _dots.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_dots);
        if (cancel != null)
        {
            var stop = Ui.PanelButton("\uE711", "Cancel");
            stop.HorizontalAlignment = HorizontalAlignment.Center;
            stop.Margin = new Thickness(0, 18, 0, 0);
            stop.Click += (_, _) => Cancel();
            panel.Children.Add(stop);
            _stop = stop;
            var hint = Ui.Text(Hints.Format("{Back}  Cancel"), 14, "SubtleBrush");
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(hint);
        }

        var box = new Border
        {
            Width = 560, Padding = new Thickness(40), CornerRadius = new CornerRadius(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xC0, 0, 0, 0))
        };
        backdrop.Children.Add(Ui.SkinBox(box));
        Content = backdrop;

        _timer.Tick += (_, _) => _dots.Text = new string('●', _step = _step % 3 + 1);
        _dots.Text = "●";
    }

    public override void OnShown() => _timer.Start();

    public override void OnClosed() => _timer.Stop();

    private Button? _stop;

    public override void FocusDefault() => _stop?.Focus();

    private void Cancel()
    {
        if (_cancel == null) return;
        UiSound.Back();
        _cancel();
        Host.Pop(this);
    }

    // Without a cancel, nothing can dismiss it: it goes away when the work finishes. With one, B stops the wait.
    public override bool OnAction(NavAction action)
    {
        if (_cancel != null && action == NavAction.Back) { Cancel(); return true; }
        if (_cancel != null && action == NavAction.Accept && _stop != null) { Cancel(); return true; }
        return true;
    }
}
