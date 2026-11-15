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

    /// <summary>Called when another page or pop-up opens on top of this one.</summary>
    public virtual void OnCovered() { }

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

        // The same wallpaper as the home screen, softened a little more so pages of text stay readable.
        if (Theme.CurrentWallpaper != null)
        {
            _wallpaperLayer = new System.Windows.Shapes.Rectangle { Fill = Theme.CurrentWallpaper, IsHitTestVisible = false };
            Grid.SetRowSpan(_wallpaperLayer, 3);
            root.Children.Add(_wallpaperLayer);
            _wallpaperScrim = new System.Windows.Shapes.Rectangle { Opacity = 0.55, IsHitTestVisible = false };
            _wallpaperScrim.SetResourceReference(Shape.FillProperty, "BgBrush");
            Grid.SetRowSpan(_wallpaperScrim, 3);
            root.Children.Add(_wallpaperScrim);
        }

        // A blurred picture behind everything (album art on the Music page); empty until asked for.
        _backdrop = new Image
        {
            Stretch = System.Windows.Media.Stretch.UniformToFill, Opacity = 0, Margin = new Thickness(-60),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 70 }
        };
        Grid.SetRowSpan(_backdrop, 3);
        root.Children.Add(_backdrop);

        var header = new DockPanel { Margin = new Thickness(side, 30, side, 0), VerticalAlignment = VerticalAlignment.Top };
        var back = new Button { Style = Ui.StyleOf("PillButton"), Content = "\u2039  Back", VerticalAlignment = VerticalAlignment.Center };
        back.Focusable = false; // for mouse / touch; the controller uses B
        back.Click += (_, _) => Host.Pop(this);
        DockPanel.SetDock(back, Dock.Right);
        header.Children.Add(back);
        _titleText = Ui.Text(title, 34, "FgBrush", bold: true);
        _header = header;
        header.Children.Add(_titleText);
        root.Children.Add(header);

        var bodyHost = new Grid { Margin = new Thickness(side, 0, side, 12) };
        bodyHost.Children.Add(body);
        Grid.SetRow(bodyHost, 1);
        root.Children.Add(bodyHost);

        var footer = new Grid { Margin = new Thickness(30, 0, 30, 0) };
        var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.15 };
        line.SetResourceReference(Shape.FillProperty, "FgBrush");
        footer.Children.Add(line);
        var hint = new HintBar(hints); // the same button shapes as the home screen
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Margin = new Thickness(0, 0, 26, 0);
        footer.Children.Add(hint);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
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

    protected ListScreen(string title, string hints = DefaultHints)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, // touch scrolling
            Focusable = false,
            Content = Rows
        };
        SetPage(title, scroll, hints);
    }

    private int _lastIndex;

    protected abstract void Build();

    public override void OnShown() => Rebuild();

    /// <summary>Recreates the rows and keeps the highlight on the same position.</summary>
    public void Rebuild()
    {
        var buttons = Rows.Children.OfType<Button>().ToList();
        int index = buttons.FindIndex(b => b.IsKeyboardFocused);
        if (index >= 0) _lastIndex = index; else index = _lastIndex;

        Rows.Children.Clear();
        Build();
        Ui.GroupRows(Rows); // runs of rows become grouped cards

        buttons = Rows.Children.OfType<Button>().ToList();
        if (buttons.Count == 0) return;
        FocusLater(buttons[Math.Clamp(index, 0, buttons.Count - 1)]);
    }

    /// <summary>Recreates the rows with the highlight back on the first one (after moving to a different list).</summary>
    protected void RebuildFromTop()
    {
        _lastIndex = 0;
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
        SetPage(title, scroll, hints);
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
        double corner = Math.Max(Theme.TileCorner, 10);
        box.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, width, height), corner, corner);
        return box;
    }

    /// <summary>A fixed-size box showing an icon, for action tiles.</summary>
    public static Border GlyphBox(double width, double height, string glyph)
    {
        var box = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 10)), Child = Ui.Glyph(glyph, 44, "AccentBrush") };
        box.SetResourceReference(Border.BackgroundProperty, "TileBrush");
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
    public MenuScreen(string title, string? message, IReadOnlyList<string> options, string? inputInitial = null,
        IReadOnlyList<string>? glyphs = null)
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
                Padding = new Thickness(8),
                Margin = new Thickness(0, 16, 0, 0)
            };
            panel.Children.Add(_input);
            panel.Children.Add(BuildKeyboard());
        }

        var buttons = new StackPanel { Margin = new Thickness(0, inputInitial != null ? 8 : 20, 0, 0) };
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
            Width = inputInitial != null ? 760 : 620,
            Padding = new Thickness(34, 28, 34, 28),
            CornerRadius = new CornerRadius(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");

        var backdrop = new Grid
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0, 0, 0))
        };
        backdrop.Children.Add(box);
        Content = backdrop;
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
        }
        else base.FocusDefault();
    }

    public override bool OnAction(NavAction action)
    {
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

    public BusyScreen(string message, System.Windows.Media.ImageSource? picture = null)
    {
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

        var box = new Border
        {
            Width = 560, Padding = new Thickness(40), CornerRadius = new CornerRadius(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        var backdrop = new Grid
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xC0, 0, 0, 0))
        };
        backdrop.Children.Add(box);
        Content = backdrop;

        _timer.Tick += (_, _) => _dots.Text = new string('●', _step = _step % 3 + 1);
        _dots.Text = "●";
    }

    public override void OnShown() => _timer.Start();

    public override void OnClosed() => _timer.Stop();

    public override void FocusDefault() { }

    // Nothing can dismiss it; it goes away when the work finishes.
    public override bool OnAction(NavAction action) => true;
}
