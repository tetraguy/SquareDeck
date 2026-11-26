using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Store opening animations": while a game store (Steam, Xbox, Epic Games, Ubisoft Connect...) starts,
/// a short shopping-and-gaming animation plays in the store's colors, then settles into a loop with its logo, game
/// pieces and price tags circling it and a line of patter ("Stocking the shelves…") until the store comes to the front.
/// B goes back to the Stores page (the store keeps opening).
/// </summary>
public sealed class StoreOpeningScreen : Screen
{
    public static bool On => Labs.On("storeintro");

    public static readonly (string Id, string Name, string About)[] Styles =
    {
        ("cart", "Shopping cart", "A cart rolls in and game boxes drop into it, then the store's logo pops out"),
        ("bag", "Shopping bag", "A shopping bag drops in, and a controller, a game disc and sparkles burst out of it"),
        ("coin", "Insert coin", "A coin drops into an arcade slot, CREDIT 01 lights up and price tags fly out"),
        ("shelf", "Store shelf", "Game boxes slide onto the shelves, a spotlight sweeps across and finds the logo"),
        ("random", "Surprise me", "A different one each time"),
    };

    private const int IntroLength = 1900;   // the opening part; the loop follows
    private const int Minimum = 2400;       // shown at least this long, even if the store is very quick
    private const int GiveUp = 20000;       // the store hasn't come to the front by then: back to the Stores page

    private readonly StoreApp _store;
    private readonly Color _brand;
    private readonly Grid _stage = new() { ClipToBounds = true };
    private readonly Grid _logo;
    private readonly TranslateTransform _logoMove = new();
    private readonly ScaleTransform _logoScale = new(1, 1);
    private readonly TextBlock _line = new() { FontSize = 24, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _dots = new() { FontSize = 24, Foreground = Brushes.White, Width = 36 };
    private readonly System.Windows.Threading.DispatcherTimer _patter = new() { Interval = TimeSpan.FromMilliseconds(1700) };
    private readonly System.Windows.Threading.DispatcherTimer _dotTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private bool _done;
    private int _patterAt;
    private readonly string[] _lines;

    public override bool IsDialog => true;

    public StoreOpeningScreen(StoreApp store)
    {
        _store = store;
        _brand = BrandOf(store.Name);
        string name = store.Name;
        _lines = new[]
        {
            $"Opening {name}", "Stocking the shelves", "Dusting off the game boxes", "Checking the bargain bin",
            "Polishing the price tags", "Warming up the checkout", $"Almost in {name}"
        };

        // Night-store backdrop in the store's colors, a soft glow behind the middle.
        var back = new Grid();
        back.Children.Add(new Border { Background = new LinearGradientBrush(Mix(_brand, Colors.Black, 0.78), Color.FromRgb(0x05, 0x06, 0x0A), 90) });
        back.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x70, _brand.R, _brand.G, _brand.B), Colors.Transparent)
            { RadiusX = 0.45, RadiusY = 0.55, Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45) }
        });
        back.Children.Add(_stage);

        _logo = BuildLogo(store);
        _logo.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_logoScale);
        group.Children.Add(_logoMove);
        _logo.RenderTransform = group;
        _logo.Opacity = 0;
        back.Children.Add(_logo);

        // The patter along the bottom, with dots that count up.
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 120) };
        _line.Text = _lines[0];
        words.Children.Add(_line);
        words.Children.Add(_dots);
        back.Children.Add(words);
        back.Children.Add(Shimmer());
        var hint = new HintBar("{Back}  Back to Stores") { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 24) };
        back.Children.Add(hint);
        Content = back;

        _patter.Tick += (_, _) => NextLine();
        _dotTimer.Tick += (_, _) => _dots.Text = _dots.Text.Length >= 3 ? "" : _dots.Text + ".";
    }

    // ---------------------------------------------------------------- life

    public override void OnShown()
    {
        string style = Labs.Get("storeintro.style", "random");
        if (style == "random" || Styles.All(s => s.Id != style)) style = Styles[Random.Shared.Next(4)].Id;
        if (Theme.ReduceMotion) ShowLogo(0, gentle: true);
        else
        {
            switch (style)
            {
                case "bag": Bag(); break;
                case "coin": Coin(); break;
                case "shelf": Shelf(); break;
                default: Cart(); break;
            }
            Delay(IntroLength, Loop);
        }
        _patter.Start();
        _dotTimer.Start();
        Host.Deactivated += OnStoreInFront;
        // The store starts straight away; the animation covers the wait.
        if (!Stores.Open(_store))
        {
            Finish();
            _ = Host.AskAsync("Couldn't open " + _store.Name, "Its app didn't start.", "OK");
            return;
        }
        Delay(GiveUp, () =>
        {
            if (_done) return;
            _line.Text = $"{_store.Name} is taking a while. It may already be open behind SquareDeck";
            _patter.Stop();
            Delay(3500, Finish);
        });
    }

    public override void OnClosed()
    {
        _done = true;
        _patter.Stop();
        _dotTimer.Stop();
        Host.Deactivated -= OnStoreInFront;
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back) { Finish(); return true; }
        return true; // nothing else to do here while the store opens
    }

    /// <summary>SquareDeck went behind another window: the store is up. The animation gets its minimum time first.</summary>
    private void OnStoreInFront(object? sender, EventArgs e)
    {
        var left = Minimum - (DateTime.UtcNow - _shownAt).TotalMilliseconds;
        if (left <= 0) Finish();
        else Delay((int)left, Finish);
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        Host.Pop(this);
    }

    private void NextLine()
    {
        _patterAt = (_patterAt + 1) % _lines.Length;
        if (_patterAt == 0) _patterAt = 1; // the store's name only the first time
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            _line.Text = _lines[_patterAt];
            _line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        };
        _line.BeginAnimation(OpacityProperty, fade);
    }

    private void Delay(int ms, Action then)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        timer.Tick += (_, _) => { timer.Stop(); if (!_done) then(); };
        timer.Start();
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>The store's colors, from the brands everyone knows.</summary>
    private static Color BrandOf(string store)
    {
        string s = store.ToLowerInvariant();
        return s switch
        {
            _ when s.StartsWith("steam") => Color.FromRgb(0x2A, 0x75, 0xBB),
            _ when s.StartsWith("xbox") => Color.FromRgb(0x10, 0x7C, 0x10),
            _ when s.StartsWith("epic") => Color.FromRgb(0x5A, 0x5A, 0x66),
            _ when s.StartsWith("gog") => Color.FromRgb(0x86, 0x32, 0x8A),
            _ when s.StartsWith("battle") => Color.FromRgb(0x14, 0x8E, 0xFF),
            _ when s.StartsWith("ea") => Color.FromRgb(0xFF, 0x47, 0x47),
            _ when s.StartsWith("ubisoft") => Color.FromRgb(0x00, 0x70, 0xFF),
            _ when s.StartsWith("amazon") => Color.FromRgb(0xFF, 0x99, 0x00),
            _ when s.StartsWith("rockstar") => Color.FromRgb(0xFC, 0xAF, 0x17),
            _ when s.StartsWith("itch") => Color.FromRgb(0xFA, 0x5C, 0x5C),
            _ => (Application.Current?.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x4D, 0x8D, 0xFF)
        };
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The store's logo on a card in its colors (a shopping bag symbol if SquareDeck has no logo for it).</summary>
    private static Grid BuildLogo(StoreApp store)
    {
        var brand = BrandOf(store.Name);
        var card = new Border
        {
            Width = 190, Height = 190, CornerRadius = new CornerRadius(36),
            Background = new LinearGradientBrush(Mix(brand, Colors.White, 0.12), Mix(brand, Colors.Black, 0.35), 90),
            Effect = new DropShadowEffect { Color = brand, BlurRadius = 50, ShadowDepth = 0, Opacity = 0.8 }
        };
        if (StoreLogos.For(store.Name) is { } logo)
            card.Child = new Image { Source = logo, Stretch = Stretch.Uniform, Margin = new Thickness(34) };
        else
            card.Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 90, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 90) };
        grid.Children.Add(card);
        return grid;
    }

    /// <summary>A shimmer that runs along a thin bar under the patter, so it's plain something is happening.</summary>
    private UIElement Shimmer()
    {
        var bar = new Grid { Width = 320, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 96), ClipToBounds = true };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        var run = new TranslateTransform(-120, 0);
        bar.Children.Add(new Border
        {
            Width = 120, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = run,
            Background = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Mix(_brand, Colors.White, 0.5), 0.5), new(Colors.Transparent, 1) }, 0)
        });
        if (!Theme.ReduceMotion) run.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-120, 320, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever });
        return bar;
    }

    private static DoubleAnimation A(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };

    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Bounce = new BounceEase { Bounces = 2, Bounciness = 3, EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Pop = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };

    /// <summary>Puts something on the stage at an offset from the middle, with a move, scale and turn to animate.</summary>
    private (TranslateTransform Move, ScaleTransform Scale, RotateTransform Turn) Place(FrameworkElement e, double x, double y)
    {
        e.HorizontalAlignment = HorizontalAlignment.Center;
        e.VerticalAlignment = VerticalAlignment.Center;
        e.IsHitTestVisible = false;
        var move = new TranslateTransform(x, y);
        var scale = new ScaleTransform(1, 1);
        var turn = new RotateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(turn);
        group.Children.Add(move);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = group;
        _stage.Children.Add(e);
        return (move, scale, turn);
    }

    /// <summary>A little boxed game: a colored cover with a white band and a spine.</summary>
    private static Border GameBox(Color color, double w = 46, double h = 62)
    {
        var face = new Grid();
        face.Children.Add(new Border { Height = h * 0.22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * 0.12, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)) });
        face.Children.Add(new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)) });
        return new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(4), Child = face, ClipToBounds = true,
            Background = new LinearGradientBrush(Mix(color, Colors.White, 0.2), Mix(color, Colors.Black, 0.3), 90),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 }
        };
    }

    private static readonly Color[] BoxColors =
    {
        Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0x3E, 0x8E, 0xF7), Color.FromRgb(0x2F, 0xB3, 0x6B),
        Color.FromRgb(0xF2, 0xB1, 0x34), Color.FromRgb(0x9B, 0x5D, 0xE5), Color.FromRgb(0xF0, 0x6E, 0xAA)
    };

    private void ShowLogo(int delay, bool gentle = false)
    {
        _logo.BeginAnimation(OpacityProperty, A(0, 1, gentle ? 400 : 160, delay));
        if (gentle) return;
        _logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.3, 1, 520, delay, Pop));
        _logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.3, 1, 520, delay, Pop));
    }

    private static void FadeAway(UIElement e, int delay, int ms = 300) => e.BeginAnimation(OpacityProperty, A(1, 0, ms, delay));

    // ---------------------------------------------------------------- the openings

    /// <summary>A shopping cart rolls in on a dashed floor, game boxes drop into it, and the logo pops out.</summary>
    private void Cart()
    {
        var cart = new Canvas { Width = 220, Height = 170 };
        var stroke = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF4));
        cart.Children.Add(new Path { Data = Geometry.Parse("M0,18 L34,18 L58,118 L182,118"), Stroke = stroke, StrokeThickness = 9, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        cart.Children.Add(new Path { Data = Geometry.Parse("M44,40 L210,40 L190,100 L60,100 Z"), Fill = new SolidColorBrush(Color.FromArgb(0x55, _brand.R, _brand.G, _brand.B)), Stroke = stroke, StrokeThickness = 7, StrokeLineJoin = PenLineJoin.Round });
        foreach (double x in new[] { 76.0, 166.0 })
        {
            var wheel = new Ellipse { Width = 30, Height = 30, Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x2C)), Stroke = stroke, StrokeThickness = 6 };
            Canvas.SetLeft(wheel, x - 15);
            Canvas.SetTop(wheel, 126);
            cart.Children.Add(wheel);
        }
        var floor = new Line { X1 = -500, X2 = 500, Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 4, 4 } };
        Place(floor, 0, 160);
        var (move, _, tilt) = Place(cart, -900, 70);
        move.BeginAnimation(TranslateTransform.XProperty, A(-900, 0, 900, 0, Out));
        var settle = new DoubleAnimationUsingKeyFrames();
        settle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(820))));
        settle.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950)), Out));
        settle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150)), Out));
        tilt.BeginAnimation(RotateTransform.AngleProperty, settle);

        // Boxes drop in, one after another, and land in the basket.
        var boxes = new List<UIElement> { cart, floor };
        for (int i = 0; i < 4; i++)
        {
            var box = GameBox(BoxColors[i]);
            double x = -48 + i * 32, land = 30 - (i % 2) * 10;
            var (m, _, t) = Place(box, x, -520);
            int at = 850 + i * 160;
            m.BeginAnimation(TranslateTransform.YProperty, A(-520, land, 420, at, Bounce));
            t.BeginAnimation(RotateTransform.AngleProperty, A(-25 + i * 14, -8 + i * 6, 420, at));
            boxes.Add(box);
        }
        foreach (var b in boxes) FadeAway(b, 1650, 260);
        ShowLogo(1700);
    }

    /// <summary>A shopping bag drops in and bounces; a controller, a game disc and sparkles burst out; the logo rises.</summary>
    private void Bag()
    {
        var bag = new Grid { Width = 190, Height = 220 };
        var paper = new LinearGradientBrush(Mix(_brand, Colors.White, 0.25), Mix(_brand, Colors.Black, 0.25), 90);
        bag.Children.Add(new Path { Data = Geometry.Parse("M60,60 C60,10 130,10 130,60"), Stroke = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF4)), StrokeThickness = 8, Margin = new Thickness(0, -6, 0, 0) });
        bag.Children.Add(new Border { Margin = new Thickness(0, 54, 0, 0), CornerRadius = new CornerRadius(10, 10, 18, 18), Background = paper, Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 } });
        bag.Children.Add(new Border { Margin = new Thickness(0, 54, 0, 0), Height = 22, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(10, 10, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) });
        bag.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 54, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) });
        var (move, squash, _) = Place(bag, 0, -700);
        move.BeginAnimation(TranslateTransform.YProperty, A(-700, 40, 700, 0, Bounce));
        var wobble = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, v) in new[] { (0, 1.0), (700, 1.0), (780, 1.08), (860, 0.95), (940, 1.0) })
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        squash.BeginAnimation(ScaleTransform.ScaleXProperty, wobble);

        // What's inside bursts out in arcs.
        var items = new List<(FrameworkElement Item, double X, double Y)>
        {
            (new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 64, Foreground = Brushes.White }, -190, -150),
            (Disc(), 190, -140),
            (new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 36, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)) }, -80, -250),
            (new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)) }, 100, -260),
        };
        var all = new List<UIElement> { bag };
        int i = 0;
        foreach (var (item, x, y) in items)
        {
            item.Effect = new DropShadowEffect { Color = _brand, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.9 };
            item.Opacity = 0;
            var (m, s, t) = Place(item, 0, 20);
            int at = 950 + i * 70;
            item.BeginAnimation(OpacityProperty, A(0, 1, 100, at));
            m.BeginAnimation(TranslateTransform.XProperty, A(0, x, 600, at, Out));
            m.BeginAnimation(TranslateTransform.YProperty, A(20, y, 600, at, Out));
            t.BeginAnimation(RotateTransform.AngleProperty, A(0, x > 0 ? 200 : -200, 900, at));
            s.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.3, 1, 500, at, Pop));
            s.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.3, 1, 500, at, Pop));
            all.Add(item);
            i++;
        }
        foreach (var e in all) FadeAway(e, 1600, 300);
        _logoMove.BeginAnimation(TranslateTransform.YProperty, A(120, 0, 520, 1600, Out));
        ShowLogo(1600);
    }

    private static Grid Disc()
    {
        var disc = new Grid { Width = 74, Height = 74 };
        disc.Children.Add(new Ellipse { Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb(0xD8, 0xE6, 0xF5), 0), new(Color.FromRgb(0xF5, 0xC6, 0xE8), 0.4), new(Color.FromRgb(0xC6, 0xF0, 0xF5), 0.7), new(Color.FromRgb(0xE8, 0xE8, 0xF0), 1) }, 45) });
        disc.Children.Add(new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1E)) });
        return disc;
    }

    /// <summary>A coin drops into an arcade slot, CREDIT 01 lights up, and price tags fly out before the logo.</summary>
    private void Coin()
    {
        var slot = new Border
        {
            Width = 160, Height = 220, CornerRadius = new CornerRadius(18), Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2D, 0x38), Color.FromRgb(0x14, 0x15, 0x1C), 90),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x48, 0x58)), BorderThickness = new Thickness(3)
        };
        var inside = new Grid();
        var slit = new Border { Width = 14, Height = 64, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0), Background = Brushes.Black, BorderThickness = new Thickness(3), BorderBrush = new SolidColorBrush(_brand) };
        slit.Effect = new DropShadowEffect { Color = _brand, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6 };
        inside.Children.Add(slit);
        var credit = new TextBlock { Text = "INSERT COIN", FontFamily = new FontFamily("Consolas"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) };
        inside.Children.Add(credit);
        slot.Child = inside;
        var (_, _, _) = Place(slot, 0, 40);
        slot.Opacity = 0;
        slot.BeginAnimation(OpacityProperty, A(0, 1, 300));
        var blink = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, v) in new[] { (0, 1.0), (200, 0.2), (400, 1.0), (600, 0.2), (800, 1.0) })
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        credit.BeginAnimation(OpacityProperty, blink);

        // The coin: gold, with a ring and a star, spinning as it falls into the slit.
        var coin = new Grid { Width = 70, Height = 70 };
        coin.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x6A), Color.FromRgb(0xD9, 0x9A, 0x1E), 45), Stroke = new SolidColorBrush(Color.FromRgb(0xB0, 0x78, 0x10)), StrokeThickness = 4 });
        coin.Children.Add(new Ellipse { Margin = new Thickness(12), Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xB0, 0x78, 0x10)), StrokeThickness = 3 });
        coin.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x78, 0x10)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var (m, s, _) = Place(coin, 0, -600);
        m.BeginAnimation(TranslateTransform.YProperty, A(-600, -30, 750, 0, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        var spin = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(3) };
        spin.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        spin.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(125))));
        spin.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, spin);
        coin.BeginAnimation(OpacityProperty, A(1, 0, 80, 760));
        Delay(800, () =>
        {
            credit.BeginAnimation(OpacityProperty, null);
            credit.Opacity = 1;
            credit.Text = "CREDIT 01";
            credit.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0xFF, 0x8A));
            slit.Effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, A(6, 40, 300));
            slit.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, A(0.6, 1, 300));
        });

        // Price tags fly out of the machine in all directions.
        var tags = new List<UIElement> { slot };
        string[] prices = { "$", "-50%", "SALE", "$", "-75%", "FREE" };
        for (int i = 0; i < prices.Length; i++)
        {
            var tag = PriceTag(prices[i], BoxColors[i % BoxColors.Length]);
            tag.Opacity = 0;
            double angle = -150 + i * 60 + Random.Shared.Next(-12, 12);
            double rad = angle * Math.PI / 180, dist = 300 + Random.Shared.Next(0, 80);
            var (tm, ts, tt) = Place(tag, 0, 0);
            int at = 1050 + i * 40;
            tag.BeginAnimation(OpacityProperty, A(0, 1, 80, at));
            tm.BeginAnimation(TranslateTransform.XProperty, A(0, Math.Cos(rad) * dist, 650, at, Out));
            tm.BeginAnimation(TranslateTransform.YProperty, A(0, Math.Sin(rad) * dist * 0.7, 650, at, Out));
            tt.BeginAnimation(RotateTransform.AngleProperty, A(0, Random.Shared.Next(-40, 40), 650, at));
            ts.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.4, 1, 400, at, Pop));
            ts.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.4, 1, 400, at, Pop));
            tags.Add(tag);
        }
        foreach (var t in tags) FadeAway(t, 1650, 280);
        ShowLogo(1700);
    }

    private static Border PriceTag(string text, Color color) => new()
    {
        CornerRadius = new CornerRadius(4, 14, 14, 4), Padding = new Thickness(14, 6, 14, 7),
        Background = new SolidColorBrush(color), Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 },
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            }
        }
    };

    /// <summary>Game boxes slide onto three shelves, a spotlight sweeps across them, and lands on the logo.</summary>
    private void Shelf()
    {
        var parts = new List<UIElement>();
        for (int row = 0; row < 3; row++)
        {
            double y = -120 + row * 110;
            var board = new Border { Width = 640, Height = 10, CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(Color.FromRgb(0x8A, 0x6A, 0x48), Color.FromRgb(0x5A, 0x42, 0x2A), 90) };
            Place(board, 0, y + 40);
            board.Opacity = 0;
            board.BeginAnimation(OpacityProperty, A(0, 1, 250, row * 80));
            parts.Add(board);
            for (int i = 0; i < 7; i++)
            {
                var box = GameBox(BoxColors[(i + row * 2) % BoxColors.Length], 50, 68);
                bool fromLeft = row % 2 == 0;
                double x = -270 + i * 90;
                var (m, _, _) = Place(box, fromLeft ? -900 : 900, y);
                m.BeginAnimation(TranslateTransform.XProperty, A(fromLeft ? -900 : 900, x, 520, 150 + row * 140 + i * 45, Out));
                parts.Add(box);
            }
        }
        // The spotlight: a soft beam that sweeps the shelves and stops in the middle.
        var beam = new Ellipse
        {
            Width = 260, Height = 420, Opacity = 0,
            Fill = new RadialGradientBrush(Color.FromArgb(0x90, 0xFF, 0xF4, 0xD0), Colors.Transparent)
        };
        var (bm, _, _) = Place(beam, -420, -20);
        beam.BeginAnimation(OpacityProperty, A(0, 1, 200, 900));
        var sweep = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(900) };
        sweep.KeyFrames.Add(new EasingDoubleKeyFrame(-420, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new EasingDoubleKeyFrame(380, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), new SineEase()));
        sweep.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800)), new SineEase()));
        bm.BeginAnimation(TranslateTransform.XProperty, sweep);
        parts.Add(beam);
        foreach (var p in parts) FadeAway(p, 1700, 300);
        ShowLogo(1700);
    }

    // ---------------------------------------------------------------- the loop, until the store is up

    /// <summary>
    /// After the opening: the logo floats gently and glows, and a ring of game pieces and price tags circles it, until
    /// the store comes to the front.
    /// </summary>
    private void Loop()
    {
        if (_done) return;
        _logo.Opacity = 1;
        var bob = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        _logoMove.BeginAnimation(TranslateTransform.YProperty, bob);

        var ring = new Grid { Width = 420, Height = 420, Opacity = 0 };
        string[] glyphs = { "", "", "", "", "", "", "", "" };
        for (int i = 0; i < glyphs.Length; i++)
        {
            double a = i * Math.PI * 2 / glyphs.Length;
            var piece = new TextBlock
            {
                Text = glyphs[i], FontFamily = Ui.IconFont, FontSize = i % 2 == 0 ? 30 : 24,
                Foreground = new SolidColorBrush(i % 4 == 2 ? Color.FromRgb(0xFF, 0xD5, 0x4F) : Mix(_brand, Colors.White, 0.55)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(Math.Cos(a) * 190, Math.Sin(a) * 190),
                Effect = new DropShadowEffect { Color = _brand, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 }
            };
            ring.Children.Add(piece);
        }
        var (_, _, turn) = Place(ring, 0, -45);
        ring.BeginAnimation(OpacityProperty, A(0, 1, 500));
        turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
