using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The look shared by the pop-ups about one game (the countdown before it starts, "Stop game?", "Closing…"):
/// a dark glass card tinted and lit in the game's own main color, taken from its cover.
/// </summary>
public static class GameCard
{
    /// <param name="Accent">The game's main color, bright enough to read on dark.</param>
    /// <param name="Deep">A dark shade of it, for the card.</param>
    /// <param name="OnAccent">Black or white, whichever reads on the accent.</param>
    public sealed record Palette(Color Accent, Color Deep, Color OnAccent);

    /// <summary>
    /// A corner radius that fits the UI style: the usual round one, or nearly square for the pixel, blueprint and
    /// newsprint styles, whose tiles have sharp corners.
    /// </summary>
    public static double Round(double usual) => Theme.TileCorner <= 4 ? Math.Min(usual, Theme.TileCorner + 2) : usual;

    /// <summary>
    /// Experimental "Switch 2 style launch and return": the game's cards (the countdown, "Stop this game?", an update
    /// before playing) in the UI style's own colors and the Switch 2 manner: a plain card, quiet text, flat buttons that
    /// take the rimmed highlight, instead of the game's colors and glow.
    /// </summary>
    public static bool S2 => Labs.On("s2game");

    private static Brush Fore => S2 ? new SolidColorBrush(Switch2Settings.Text) : Brushes.White;

    public static Palette ColorsOf(Game? game)
    {
        if (S2)
        {
            Switch2Settings.Refresh();
            var blue = Switch2Settings.Blue;
            double lum = (0.299 * blue.R + 0.587 * blue.G + 0.114 * blue.B) / 255;
            return new Palette(blue, Switch2Settings.Background, lum > 0.6 ? Color.FromRgb(0x10, 0x12, 0x16) : Colors.White);
        }
        Color? main = null;
        try { if (game?.Cover is BitmapSource cover) main = ImageLoader.DominantColor(cover); } catch (Exception) { }
        var c = main ?? (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x3B, 0x82, 0xF6);

        // Lift dull or dark colors so the accent always glows: keep the hue, raise brightness and color.
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max > 0.001)
        {
            double value = Math.Max(max, 0.82), k = value / max;
            r *= k; g *= k; b *= k; min *= k;
            double saturation = (value - min) / value;
            if (saturation < 0.5 && saturation > 0.04)
            {
                // Pull the channels away from the brightest one to deepen the color.
                double t = (0.5 - saturation) / (1 - saturation);
                r -= (value - r) * t; g -= (value - g) * t; b -= (value - b) * t;
            }
        }
        var accent = Color.FromRgb(Byte(r), Byte(g), Byte(b));
        var deep = Color.FromRgb((byte)(accent.R * 0.2 + 10), (byte)(accent.G * 0.2 + 11), (byte)(accent.B * 0.2 + 14));
        double luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        return new Palette(accent, deep, luminance > 0.6 ? Color.FromRgb(0x10, 0x12, 0x16) : Colors.White);

        static byte Byte(double v) => (byte)Math.Clamp(v * 255, 0, 255);
    }

    /// <summary>The dimmed screen with the card in the middle.</summary>
    public static Grid Frame(Palette p, UIElement content, double width = 620)
    {
        var card = new Border
        {
            Width = width, Padding = new Thickness(40, 36, 40, 34), CornerRadius = new CornerRadius(Round(30)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = content,
            BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, p.Accent.R, p.Accent.G, p.Accent.B)),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xF6, (byte)Math.Min(255, p.Deep.R + 22), (byte)Math.Min(255, p.Deep.G + 22), (byte)Math.Min(255, p.Deep.B + 22)),
                Color.FromArgb(0xFA, 0x0B, 0x0D, 0x11), 90),
            Effect = Perf.Active ? null : new DropShadowEffect { Color = p.Accent, BlurRadius = 70, ShadowDepth = 0, Opacity = 0.45 },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        if (S2)
        {
            // A plain card in the page color, no outline or colored glow, a soft shadow.
            card.Background = new SolidColorBrush(Switch2Settings.Background);
            card.BorderThickness = new Thickness(0);
            card.CornerRadius = new CornerRadius(Round(18));
            card.Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 40, ShadowDepth = 6, Opacity = 0.35 };
        }
        var glow = new RadialGradientBrush { RadiusX = 0.6, RadiusY = 0.7 };
        if (S2) glow.Opacity = 0;
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, p.Accent.R, p.Accent.G, p.Accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, p.Accent.R, p.Accent.G, p.Accent.B), 1));
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0)) };
        backdrop.Children.Add(new Border { Background = glow, IsHitTestVisible = false });
        backdrop.Children.Add(card);

        if (!Theme.ReduceMotion)
        {
            // The card rises into place.
            var scale = new ScaleTransform(0.94, 0.94);
            card.RenderTransform = scale;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        return backdrop;
    }

    /// <summary>The game's cover with rounded corners and a soft shadow.</summary>
    public static Border Cover(Game game, double size)
    {
        var cover = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(Round(size * 0.18)), HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.55 }
        };
        if (game.Cover == null)
        {
            cover.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            cover.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size * 0.4, Foreground = Brushes.White,
                                          Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else if (game.UsesIcon && !game.IconFills)
        {
            cover.Background = game.TileBackground ?? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            cover.Child = new Image { Source = game.Cover, Margin = new Thickness(size * 0.22), Stretch = Stretch.Uniform };
        }
        else cover.Background = new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill };
        return cover;
    }

    public static TextBlock Text(string text, double size, bool bold = false, double opacity = 1) => new()
    {
        Text = text, FontSize = size, FontWeight = bold && !S2 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Fore, Opacity = opacity,
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

    /// <summary>A small spaced-out label in the game's color ("STARTING IN").</summary>
    public static TextBlock Eyebrow(string text, Palette p) => new()
    {
        Text = string.Join(" ", text.ToUpperInvariant().ToCharArray()), FontSize = 13, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(p.Accent), HorizontalAlignment = HorizontalAlignment.Center
    };

    /// <summary>
    /// A pill button. The main one is filled with the game's color; the other is quiet glass. Whichever has the
    /// highlight gets a white ring and grows a little.
    /// </summary>
    public static Button Pill(string label, string glyph, bool primary, Palette p, Action click)
    {
        var fore = new SolidColorBrush(primary ? p.OnAccent : S2 ? Switch2Settings.Text : Colors.White);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = fore,
                                         VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0),
                                         Visibility = glyph.Length > 0 ? Visibility.Visible : Visibility.Collapsed });
        row.Children.Add(new TextBlock { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = fore, VerticalAlignment = VerticalAlignment.Center });
        var scale = new ScaleTransform(1, 1);
        var face = new Border
        {
            // Sized so three fit side by side on the card, with room to grow when highlighted.
            CornerRadius = new CornerRadius(Round(24)), Padding = new Thickness(20, 10, 22, 11), MinWidth = 140, Child = row,
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent,
            Background = primary ? new SolidColorBrush(p.Accent) : S2 ? new SolidColorBrush(Switch2Settings.Card) : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };
        var button = new Button
        {
            Content = face, Margin = new Thickness(8, 0, 8, 0), FocusVisualStyle = null, Cursor = Cursors.Hand,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        if (S2) face.CornerRadius = new CornerRadius(Round(12));
        void Highlight(bool on)
        {
            face.BorderBrush = on ? (S2 ? Switch2Settings.RimBrush : Brushes.White) : Brushes.Transparent;
            if (S2) face.Effect = on && !Perf.Active ? new DropShadowEffect { Color = Switch2Settings.Blue, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5 } : null;
            double to = on && !Theme.ReduceMotion ? 1.03 : 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
        }
        button.GotKeyboardFocus += (_, _) => Highlight(true);
        button.LostKeyboardFocus += (_, _) => Highlight(false);
        button.Click += (_, _) => click();
        return button;
    }

    public static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    // ---------------------------------------------------------------- Switch 2 pop-ups

    /// <summary>
    /// The Switch's own pop-up: a wide card in the UI style's card color over a dimmed screen. The top holds the
    /// picture and words; <paramref name="buttons"/>, if any, sit under a thin line as equal flat buttons split by
    /// thin lines (Cancel | Close), the highlighted one with the rimmed card.
    /// </summary>
    public static Grid SwitchPopup(UIElement top, double width, params Button[] buttons)
    {
        Switch2Settings.Refresh();
        var stack = new StackPanel();
        var upper = new Border { Padding = new Thickness(34, 30, 34, 30), Child = top };
        stack.Children.Add(upper);
        if (buttons.Length > 0)
        {
            stack.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(Switch2Settings.Line) });
            var row = new Grid { Height = 76 };
            for (int i = 0; i < buttons.Length; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition());
                if (i > 0)
                {
                    var split = new Rectangle { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Switch2Settings.Line), Margin = new Thickness(0, 10, 0, 10) };
                    Grid.SetColumn(split, i);
                    row.Children.Add(split);
                }
                Grid.SetColumn(buttons[i], i);
                row.Children.Add(buttons[i]);
            }
            stack.Children.Add(row);
        }
        var card = new Border
        {
            Width = width, CornerRadius = new CornerRadius(Round(16)), ClipToBounds = false, Child = stack,
            Background = new SolidColorBrush(Switch2Settings.Card), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 40, ShadowDepth = 6, Opacity = 0.35 }, RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
        backdrop.Children.Add(card);
        if (!Theme.ReduceMotion)
        {
            var scale = new ScaleTransform(0.96, 0.96);
            card.RenderTransform = scale;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
        return backdrop;
    }

    /// <summary>A Switch pop-up button: its label in the highlight color across its half; highlighted, the rimmed card.</summary>
    public static Button SwitchButton(string label, Action click)
    {
        var text = new TextBlock { Text = label, FontSize = 20, Foreground = new SolidColorBrush(Switch2Settings.Blue), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { CornerRadius = new CornerRadius(Round(10)), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Margin = new Thickness(6), Child = text };
        var button = new Button
        {
            Content = face, FocusVisualStyle = null, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        void Highlight(bool on)
        {
            face.BorderBrush = on ? Switch2Settings.RimBrush : Brushes.Transparent;
            face.Background = on ? new SolidColorBrush(Color.FromArgb(0x14, Switch2Settings.Blue.R, Switch2Settings.Blue.G, Switch2Settings.Blue.B)) : Brushes.Transparent;
            face.Effect = on && !Perf.Active ? new DropShadowEffect { Color = Switch2Settings.Blue, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 } : null;
        }
        button.GotKeyboardFocus += (_, _) => Highlight(true);
        button.LostKeyboardFocus += (_, _) => Highlight(false);
        button.MouseEnter += (_, _) => button.Focus();
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The game's cover, small and squarer, for the left of a Switch pop-up.</summary>
    public static Border SwitchCover(Game game, double size)
    {
        var cover = Cover(game, size);
        cover.CornerRadius = new CornerRadius(Round(8));
        cover.Effect = null;
        cover.VerticalAlignment = VerticalAlignment.Center;
        return cover;
    }

    /// <summary>A ring in the game's color. <paramref name="part"/> is how much of it is drawn (1 = all).</summary>
    public static Ellipse Ring(double size, double thickness, Color color, double part = 1)
    {
        double around = Math.PI * (size - thickness) / thickness; // its length, measured in stroke widths
        return new Ellipse
        {
            Width = size, Height = size, StrokeThickness = thickness, Stroke = new SolidColorBrush(color),
            StrokeDashArray = new DoubleCollection { around * part, around * 2 }, StrokeDashCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90), Tag = around
        };
    }
}

/// <summary>"Stop this game?" (and "… is still running"), in the game's colors. Result is true to go ahead.</summary>
public sealed class GameConfirmScreen : Screen
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button _confirm;
    private bool _yes;

    public override bool IsDialog => true;
    public Task<bool> Result => _done.Task;

    public GameConfirmScreen(Game game, string title, string message, string confirmLabel, string eyebrow = "Stop game")
    {
        if (GameCard.S2)
        {
            // The Switch's "Close the software?": the cover on the left, the question beside it; Cancel | Close under a line.
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(GameCard.SwitchCover(game, 112));
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
            var question = new TextBlock { Text = title, FontSize = 22, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Switch2Settings.Text) };
            words.Children.Add(question);
            if (message.Length > 0)
                words.Children.Add(new TextBlock { Text = message, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Switch2Settings.Subtle) });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var cancel = GameCard.SwitchButton("Cancel", () => Host.Pop(this));
            _confirm = GameCard.SwitchButton(confirmLabel, () => { _yes = true; Host.Pop(this); });
            Content = GameCard.SwitchPopup(row, 680, cancel, _confirm);
            return;
        }
        var p = GameCard.ColorsOf(game);
        var panel = new StackPanel();
        panel.Children.Add(GameCard.Cover(game, 124));
        var label = GameCard.Eyebrow(eyebrow, p);
        label.Margin = new Thickness(0, 24, 0, 8);
        panel.Children.Add(label);
        panel.Children.Add(GameCard.Text(title, 28, bold: true));
        var body = GameCard.Text(message, 17, opacity: 0.78);
        body.Margin = new Thickness(0, 12, 0, 0);
        body.MaxWidth = 500;
        panel.Children.Add(body);
        _confirm = GameCard.Pill(confirmLabel, "", true, p, () => { _yes = true; Host.Pop(this); });
        panel.Children.Add(GameCard.Buttons(_confirm, GameCard.Pill("Cancel", "", false, p, () => Host.Pop(this))));
        Content = GameCard.Frame(p, panel);
    }

    public override void FocusDefault() => _confirm.Focus();
    public override void OnClosed() => _done.TrySetResult(_yes);
}

/// <summary>"Closing software": the game's cover inside a turning ring of its color, until the game has gone.</summary>
public sealed class GameBusyScreen : Screen
{
    public override bool IsDialog => true;

    public GameBusyScreen(Game game, string message)
    {
        if (GameCard.S2)
        {
            // The same Switch card: the cover with a light running round its edge, "Closing software" and the game beside it.
            Switch2Settings.Refresh();
            const double s = 96, g = 8, t = 4;
            double o = s + 2 * (g + t), rr = GameCard.Round(8) + g + t / 2, sd = o - t;
            double around = (4 * (sd - 2 * rr) + 2 * Math.PI * rr) / t;
            var pic = new Grid { Width = o, Height = o, VerticalAlignment = VerticalAlignment.Center };
            pic.Children.Add(new Rectangle { Width = o, Height = o, RadiusX = rr, RadiusY = rr, StrokeThickness = t, Stroke = new SolidColorBrush(Switch2Settings.Line) });
            var light = new Rectangle
            {
                Width = o, Height = o, RadiusX = rr, RadiusY = rr, StrokeThickness = t, Stroke = new SolidColorBrush(Switch2Settings.Blue),
                StrokeDashArray = new DoubleCollection { around * 0.24, around * 0.76 }, StrokeDashCap = PenLineCap.Round
            };
            pic.Children.Add(light);
            var picture = GameCard.SwitchCover(game, s);
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            pic.Children.Add(picture);
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(pic);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
            var what = new TextBlock { Text = message + "…", FontSize = 22, Foreground = new SolidColorBrush(Switch2Settings.Text) };
            words.Children.Add(what);
            words.Children.Add(new TextBlock { Text = game.Name, FontSize = 16, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Switch2Settings.Subtle) });
            Grid.SetColumn(words, 1);
            line.Children.Add(words);
            Content = GameCard.SwitchPopup(line, 600);
            if (!Theme.ReduceMotion)
                light.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -around, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
            return;
        }
        var p = GameCard.ColorsOf(game);
        var panel = new StackPanel();
        // The game's own square art (as on the home row), with a light running around its edge.
        const double size = 150, gap = 12, thick = 5;
        double outer = size + 2 * (gap + thick);
        double corner = GameCard.Round(size * 0.18);
        var art = new Grid { Width = outer, Height = outer, HorizontalAlignment = HorizontalAlignment.Center };
        double radius = corner + gap + thick / 2;
        double side = outer - thick;
        double perimeter = (4 * (side - 2 * radius) + 2 * Math.PI * radius) / thick; // in stroke widths
        art.Children.Add(new Rectangle { Width = outer, Height = outer, RadiusX = radius, RadiusY = radius, StrokeThickness = thick, Stroke = new SolidColorBrush(GameCard.S2 ? Switch2Settings.Line : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
        var runner = new Rectangle
        {
            Width = outer, Height = outer, RadiusX = radius, RadiusY = radius, StrokeThickness = thick, Stroke = new SolidColorBrush(p.Accent),
            StrokeDashArray = new DoubleCollection { perimeter * 0.24, perimeter * 0.76 }, StrokeDashCap = PenLineCap.Round
        };
        art.Children.Add(runner);
        var cover = GameCard.Cover(game, size);
        cover.VerticalAlignment = VerticalAlignment.Center;
        art.Children.Add(cover);
        panel.Children.Add(art);
        var label = GameCard.Eyebrow(message, p);
        label.Margin = new Thickness(0, 26, 0, 8);
        panel.Children.Add(label);
        var name = GameCard.Text(game.Name, 26, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxHeight = 70;
        panel.Children.Add(name);
        Content = GameCard.Frame(p, panel, 520);

        if (!Theme.ReduceMotion)
            runner.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0, -perimeter, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    public override void FocusDefault() { }

    // Nothing can dismiss it; it goes away when the game has closed.
    public override bool OnAction(NavAction action) => true;
}

/// <summary>
/// The countdown before a game starts: its cover (or logo), a ring in its color emptying around the seconds left,
/// then Start now and Cancel.
/// </summary>
public sealed class CountdownScreen : Screen
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _number;
    private readonly ScaleTransform _pop = new(1, 1);
    private readonly Image _logo = new() { MaxWidth = 380, MaxHeight = 120, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Border _cover;
    private readonly Ellipse _ring;
    private readonly Button _now;
    private readonly Game _game;
    private readonly int _total;
    private int _seconds;
    private bool _go;

    public override bool IsDialog => true;

    /// <summary>True to start the game, false if it was cancelled.</summary>
    public Task<bool> Result => _done.Task;

    public CountdownScreen(Game game, int seconds = 3)
    {
        _game = game;
        _total = _seconds = Math.Max(1, seconds);
        var p = GameCard.ColorsOf(game);
        var panel = new StackPanel();

        // The logo takes the cover's place when it arrives.
        _cover = GameCard.Cover(game, 124);
        var top = new Grid { MinHeight = 124 };
        top.Children.Add(_cover);
        top.Children.Add(_logo);
        panel.Children.Add(top);

        var name = GameCard.Text(game.Name, 24, bold: true);
        name.Margin = new Thickness(0, 20, 0, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxHeight = 66;
        panel.Children.Add(name);
        var label = GameCard.Eyebrow("Starting in", p);
        label.Margin = new Thickness(0, 18, 0, 12);
        panel.Children.Add(label);

        var dial = new Grid { Width = 132, Height = 132, HorizontalAlignment = HorizontalAlignment.Center };
        dial.Children.Add(GameCard.Ring(132, 8, GameCard.S2 ? Switch2Settings.Line : Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)));
        _ring = GameCard.Ring(132, 8, p.Accent);
        if (!Perf.Active) _ring.Effect = new DropShadowEffect { Color = p.Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 };
        dial.Children.Add(_ring);
        _number = new TextBlock { FontSize = 64, FontWeight = GameCard.S2 ? FontWeights.Normal : FontWeights.Bold, Foreground = GameCard.S2 ? new SolidColorBrush(Switch2Settings.Text) : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                                  VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _pop };
        dial.Children.Add(_number);
        panel.Children.Add(dial);

        _now = GameCard.Pill("Start now", "", true, p, () => { _go = true; Host.Pop(this); });
        panel.Children.Add(GameCard.Buttons(_now, GameCard.Pill("Cancel", "", false, p, () => Host.Pop(this))));
        Content = GameCard.Frame(p, panel);

        _timer.Tick += (_, _) =>
        {
            if (--_seconds > 0) { Show(); Host.Rumble(70); return; }
            Host.Rumble(160); // a longer pulse as the game starts
            _go = true;
            Host.Pop(this);
        };
        Show();
    }

    private void Show()
    {
        _number.Text = _seconds.ToString();
        if (Theme.ReduceMotion) return;
        var pop = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    public override void FocusDefault() => _now.Focus();

    public override void OnShown()
    {
        _timer.Start();
        Host.Rumble(70); // a small pulse on each second, if vibration is on
        // The ring empties smoothly over the whole countdown.
        if (_ring.Tag is double around)
            _ring.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, around, TimeSpan.FromSeconds(_total)));
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
            _logo.VerticalAlignment = VerticalAlignment.Center;
            _logo.Visibility = Visibility.Visible;
            _cover.Visibility = Visibility.Hidden; // keeps its space, so nothing jumps
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

/// <summary>
/// "An update is available" before a Steam game starts: its cover with a download badge, what Steam says about the
/// update, and Update / Play anyway / Cancel, in the game's colors and the UI style's corners and font. While Steam
/// downloads, a bar fills with the progress; when it's done the card says so and Play is ready.
/// </summary>
public sealed class GameUpdateScreen : Screen
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Game _game;
    private readonly GameCard.Palette _p;
    private readonly TextBlock _eyebrow, _title, _body, _percent;
    private readonly Border _bar, _fill;
    private readonly ProgressFill _barFill;
    private readonly Grid _progress;
    private readonly StackPanel _buttons;
    private Button _first = null!;
    private bool _play, _asked, _finished;

    public override bool IsDialog => true;

    /// <summary>True to start the game (updated or not), false to go back.</summary>
    public Task<bool> Result => _done.Task;

    public GameUpdateScreen(Game game)
    {
        _game = game;
        _p = GameCard.ColorsOf(game);
        var panel = new StackPanel();

        // The cover, with a round download badge in the game's color on its corner.
        var art = new Grid { Width = 140, Height = 140, HorizontalAlignment = HorizontalAlignment.Center };
        var cover = GameCard.Cover(game, 124);
        cover.VerticalAlignment = VerticalAlignment.Center;
        art.Children.Add(cover);
        var badge = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(GameCard.Round(23)), Background = new SolidColorBrush(_p.Accent),
            BorderThickness = new Thickness(4), BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, _p.Deep.R, _p.Deep.G, _p.Deep.B)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = new SolidColorBrush(_p.OnAccent),
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        art.Children.Add(badge);
        panel.Children.Add(art);

        _eyebrow = GameCard.Eyebrow("Update available", _p);
        _eyebrow.Margin = new Thickness(0, 22, 0, 8);
        panel.Children.Add(_eyebrow);
        _title = GameCard.Text(game.Name, 26, bold: true);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.MaxHeight = 70;
        panel.Children.Add(_title);
        _body = GameCard.Text("", 17, opacity: 0.78);
        _body.Margin = new Thickness(0, 10, 0, 0);
        _body.MaxWidth = 520;
        panel.Children.Add(_body);

        // Download progress: a bar in the game's color with the percentage beside it.
        _progress = new Grid { Margin = new Thickness(10, 22, 10, 0), Visibility = Visibility.Collapsed };
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _bar = new Border { Height = 10, CornerRadius = new CornerRadius(GameCard.Round(5)), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border
        {
            Height = 10, Width = 0, CornerRadius = new CornerRadius(GameCard.Round(5)), Background = new SolidColorBrush(_p.Accent), HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Perf.Active ? null : new DropShadowEffect { Color = _p.Accent, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 }
        };
        _bar.Child = _fill;
        _barFill = new ProgressFill(_bar, _fill);
        // The number is read off the bar as it slides, so the two always agree.
        _barFill.Shown = v => { if (_showPercent) _percent.Text = (int)Math.Floor(v * 100 + 0.0001) + "%"; };
        _progress.Children.Add(_bar);
        _percent = GameCard.Text("", 16, bold: true);
        _percent.Margin = new Thickness(14, 0, 0, 0);
        _percent.MinWidth = 48;
        Grid.SetColumn(_percent, 1);
        _progress.Children.Add(_percent);
        panel.Children.Add(_progress);

        _buttons = GameCard.Buttons();
        panel.Children.Add(_buttons);
        Content = GameCard.Frame(_p, panel, 680);

        _poll.Tick += async (_, _) => await PollAsync();
        Show(game.UpdateText);
    }

    /// <summary>Lays the card out for what Steam says now: waiting, downloading (with a percentage) or done.</summary>
    private void Show(string state)
    {
        bool downloading = state.StartsWith("Updating", StringComparison.Ordinal);
        int percent = 0;
        if (downloading) int.TryParse(new string(state.Where(char.IsDigit).ToArray()), out percent);

        if (state.Length == 0 && (_asked || downloading || _finished || !_game.HasUpdate))
        {
            // Up to date (it finished while the card was open).
            _finished = true;
            _eyebrow.Text = Spaced("Up to date");
            _body.Text = "The update is installed. Ready when you are.";
            _progress.Visibility = Visibility.Visible;
            SetBar(100);
            SetButtons(("Play", "", true, () => Close(true)), ("Not now", "", false, () => Close(false)));
            UiSound.Toast();
            return;
        }

        _progress.Visibility = downloading || _asked ? Visibility.Visible : Visibility.Collapsed;
        if (downloading)
        {
            _eyebrow.Text = Spaced("Updating");
            _body.Text = "Steam is downloading the update. You can wait here, or play the version you have.";
            SetBar(percent);
            if (_buttons.Children.Count != 2 || _asked == false)
                SetButtons(("Play anyway", "", false, () => Close(true)), ("Cancel", "", false, () => Close(false)));
            _asked = true;
        }
        else if (_asked)
        {
            _eyebrow.Text = Spaced("Waiting for Steam");
            _body.Text = "Start the update in Steam's Downloads page (it's open now), then come back. The bar fills as it downloads.";
            SetBar(0);
            _showPercent = false;
            _percent.Text = "…";
        }
        else
        {
            _eyebrow.Text = Spaced("Update available");
            _body.Text = "A new version is waiting in Steam. Update first, or play the version you have now. " +
                         "Some online games won't connect until they're updated.";
            SetButtons(("Update", "", true, StartUpdate), ("Play anyway", "", false, () => Close(true)), ("Cancel", "", false, () => Close(false)));
        }
    }

    private static string Spaced(string text) => string.Join(" ", text.ToUpperInvariant().ToCharArray());

    private bool _showPercent = true;

    private void SetBar(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        _showPercent = true;
        _barFill.Set(percent / 100.0, 400);
    }

    private void SetButtons(params (string Label, string Glyph, bool Primary, Action Click)[] buttons)
    {
        _buttons.Children.Clear();
        foreach (var b in buttons)
        {
            var pill = GameCard.Pill(b.Label, b.Glyph, b.Primary, _p, b.Click);
            pill.Margin = new Thickness(5, 4, 5, 4);
            _buttons.Children.Add(pill);
        }
        _first = (Button)_buttons.Children[0];
        if (IsLoaded) Dispatcher.BeginInvoke(() => _first.Focus(), DispatcherPriority.Input);
    }

    /// <summary>
    /// Steam has no quiet "update this now" command, so its Downloads page opens, where the waiting update can be
    /// started with one press. The card stays here and follows the download.
    /// </summary>
    private void StartUpdate()
    {
        _asked = true;
        ProcessUtil.Open("steam://open/downloads");
        Show(_game.UpdateText);
        _poll.Start();
    }

    private async Task PollAsync()
    {
        try
        {
            var states = await Task.Run(SteamLibrary.GetUpdateStates);
            if (!states.TryGetValue(_game.AppId, out var state)) return;
            _game.UpdateText = state;
            if (!_finished) Show(state);
            if (_finished) _poll.Stop();
        }
        catch (Exception) { }
    }

    private void Close(bool play)
    {
        _play = play;
        Host.Pop(this);
    }

    public override void FocusDefault() => _first.Focus();

    public override void OnShown()
    {
        // Already downloading? Follow it straight away.
        if (_game.UpdateText.StartsWith("Updating", StringComparison.Ordinal)) _poll.Start();
    }

    public override void OnResumed() => _first.Focus();

    public override void OnClosed()
    {
        _poll.Stop();
        _done.TrySetResult(_play);
    }
}
