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

    public static Palette ColorsOf(Game? game)
    {
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
            Width = width, Padding = new Thickness(40, 36, 40, 34), CornerRadius = new CornerRadius(30),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = content,
            BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, p.Accent.R, p.Accent.G, p.Accent.B)),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xF6, (byte)Math.Min(255, p.Deep.R + 22), (byte)Math.Min(255, p.Deep.G + 22), (byte)Math.Min(255, p.Deep.B + 22)),
                Color.FromArgb(0xFA, 0x0B, 0x0D, 0x11), 90),
            Effect = Perf.Active ? null : new DropShadowEffect { Color = p.Accent, BlurRadius = 70, ShadowDepth = 0, Opacity = 0.45 },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var glow = new RadialGradientBrush { RadiusX = 0.6, RadiusY = 0.7 };
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
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.18), HorizontalAlignment = HorizontalAlignment.Center,
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
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.White, Opacity = opacity,
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
        var fore = new SolidColorBrush(primary ? p.OnAccent : Colors.White);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = fore,
                                         VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = fore, VerticalAlignment = VerticalAlignment.Center });
        var scale = new ScaleTransform(1, 1);
        var face = new Border
        {
            CornerRadius = new CornerRadius(28), Padding = new Thickness(28, 13, 30, 14), MinWidth = 190, Child = row,
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent,
            Background = primary ? new SolidColorBrush(p.Accent) : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };
        var button = new Button
        {
            Content = face, Margin = new Thickness(8, 0, 8, 0), FocusVisualStyle = null, Cursor = Cursors.Hand,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        void Highlight(bool on)
        {
            face.BorderBrush = on ? Brushes.White : Brushes.Transparent;
            double to = on && !Theme.ReduceMotion ? 1.05 : 1;
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
        _confirm = GameCard.Pill(confirmLabel, "", true, p, () => { _yes = true; Host.Pop(this); });
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
        var p = GameCard.ColorsOf(game);
        var panel = new StackPanel();
        var art = new Grid { Width = 190, Height = 190, HorizontalAlignment = HorizontalAlignment.Center };
        art.Children.Add(GameCard.Ring(190, 7, Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
        var spinner = GameCard.Ring(190, 7, p.Accent, 0.28);
        art.Children.Add(spinner);
        var cover = GameCard.Cover(game, 132);
        cover.VerticalAlignment = VerticalAlignment.Center;
        cover.CornerRadius = new CornerRadius(66); // round, inside the ring
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

        if (!Theme.ReduceMotion && spinner.RenderTransform is RotateTransform turn)
            turn.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-90, 270, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
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
        dial.Children.Add(GameCard.Ring(132, 8, Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)));
        _ring = GameCard.Ring(132, 8, p.Accent);
        if (!Perf.Active) _ring.Effect = new DropShadowEffect { Color = p.Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 };
        dial.Children.Add(_ring);
        _number = new TextBlock { FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                                  VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _pop };
        dial.Children.Add(_number);
        panel.Children.Add(dial);

        _now = GameCard.Pill("Start now", "", true, p, () => { _go = true; Host.Pop(this); });
        panel.Children.Add(GameCard.Buttons(_now, GameCard.Pill("Cancel", "", false, p, () => Host.Pop(this))));
        Content = GameCard.Frame(p, panel);

        _timer.Tick += (_, _) =>
        {
            if (--_seconds > 0) { Show(); return; }
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
