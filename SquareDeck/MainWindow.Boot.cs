using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>The welcome greeting next to the profile picture, and ten more startup animations.</summary>
public partial class MainWindow
{
    // ================================================================ welcome greeting

    private Grid? _welcomeHost;

    /// <summary>"Good morning", "Good afternoon" or "Good evening", by the clock.</summary>
    public static string Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 18 => "Good afternoon",
        _ => "Good evening"
    };

    /// <summary>The first name to greet: the first word of the name Windows has for the account.</summary>
    public static string FirstName()
    {
        string full = UserName.Display.Trim();
        int space = full.IndexOf(' ');
        return space > 0 ? full[..space] : full;
    }

    /// <summary>
    /// The greeting slides out from behind the profile picture to its right, stays three seconds, then slides back to
    /// the left and fades away. It floats in a rounded bubble above the top bar, so it reads cleanly in every UI style,
    /// including the ones with page tabs or buttons right next to the picture (it covers them for those few seconds
    /// instead of running into them).
    /// </summary>
    public void ShowWelcome()
    {
        if (!Settings.WelcomeMessage || !AvatarButton.IsVisible || AvatarButton.ActualWidth <= 0) return;
        if (_welcomeHost != null) RootGrid.Children.Remove(_welcomeHost);

        var text = new TextBlock
        {
            Text = $"{Greeting(DateTime.Now)}, {FirstName()}", FontSize = 21, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        // WPF draws emoji in one colour, so the hand is white (it was the default black, hard to see on dark bubbles),
        // with a soft shadow so it still reads on light styles. It gives a little wave when the bubble appears.
        var wave = new TextBlock
        {
            Text = "👋", FontSize = 19, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji"),
            Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0), RenderTransformOrigin = new Point(0.7, 0.85),
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black }
        };
        if (!Theme.ReduceMotion)
        {
            var spin = new RotateTransform();
            wave.RenderTransform = spin;
            var swing = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(450) };
            foreach (var (t, a) in new[] { (0, 0.0), (150, 18.0), (300, -8.0), (450, 18.0), (600, -4.0), (750, 0.0) })
                swing.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(a, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
            spin.BeginAnimation(RotateTransform.AngleProperty, swing);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(text);
        row.Children.Add(wave);

        var bubble = new Border
        {
            Child = row, Height = 48, Padding = new Thickness(20, 0, 22, 0), CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35 }
        };
        bubble.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        bubble.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = bubble.DesiredSize.Width + 24; // room for the shadow

        // Just right of the picture, centred on it, above everything in the top bar.
        var at = AvatarButton.TranslatePoint(new Point(AvatarButton.ActualWidth, AvatarButton.ActualHeight / 2), RootGrid);
        var slide = new TranslateTransform(-width, 0);
        bubble.RenderTransform = slide;
        bubble.Opacity = 0;
        bubble.Margin = new Thickness(0, 12, 0, 12);
        var host = new Grid
        {
            ClipToBounds = true, Width = width, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(at.X + 10, at.Y - 36, 0, 0), IsHitTestVisible = false
        };
        host.Children.Add(bubble);
        Grid.SetRowSpan(host, Math.Max(1, RootGrid.RowDefinitions.Count));
        _welcomeHost = host;
        // Over the top bar, but under any page or menu opened while it shows.
        int under = RootGrid.Children.IndexOf(ScreenLayer);
        if (under >= 0) RootGrid.Children.Insert(under, host); else RootGrid.Children.Add(host);

        void Remove()
        {
            RootGrid.Children.Remove(host);
            if (_welcomeHost == host) _welcomeHost = null;
        }

        if (Theme.ReduceMotion)
        {
            slide.X = 0;
            bubble.Opacity = 1;
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            later.Tick += (_, _) => { later.Stop(); Remove(); };
            later.Start();
            return;
        }

        // In: out from behind the picture with a little overshoot, fading up; the hand gives a wave.
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-width, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } });
        bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        var tilt = new RotateTransform(0);
        wave.RenderTransformOrigin = new Point(0.7, 0.8);
        wave.RenderTransform = tilt;
        var waving = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(500) };
        foreach (var (ms, angle) in new[] { (0, 0.0), (120, 18.0), (240, -10.0), (360, 16.0), (480, -6.0), (600, 0.0) })
            waving.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        tilt.BeginAnimation(RotateTransform.AngleProperty, waving);

        // Out, three seconds later: back to the left, behind the picture, fading.
        var hold = TimeSpan.FromMilliseconds(560 + 3000);
        var back = new DoubleAnimation(0, -width, TimeSpan.FromMilliseconds(460)) { BeginTime = hold, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        back.Completed += (_, _) => Remove();
        slide.BeginAnimation(TranslateTransform.XProperty, back, HandoffBehavior.Compose);
        bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = hold + TimeSpan.FromMilliseconds(60) }, HandoffBehavior.Compose);
    }

    // ================================================================ ten more startup animations

    public static readonly (string Id, string Name)[] MoreBootAnimations =
    {
        ("glitch", "Glitch"), ("assemble", "Pixel assemble"), ("neon", "Neon sign"), ("liquid", "Liquid fill"), ("orbit", "Orbit"),
        ("blinds", "Blinds"), ("burst", "Particle burst"), ("hologram", "Hologram scan"), ("flip", "Card flip"), ("aurora", "Aurora"),
    };

    private static DoubleAnimation A(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };

    private TextBlock IntroName(double size = 40)
    {
        var t = new TextBlock { Text = "SquareDeck", FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        return t;
    }

    private static Color AccentColor => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    /// <summary>Builds one of the newer animations into the intro cover. False for an id it doesn't know.</summary>
    private bool PlayMoreIntro(string style, Grid cover, StackPanel center, ref int total)
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var accent = AccentColor;
        switch (style)
        {
            case "glitch":
            {
                // Red and cyan copies of the logo jitter apart, slices of it shear sideways, then it all snaps clean.
                var stack = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                var red = BuildLogoMark(130); var cyan = BuildLogoMark(130); var main = BuildLogoMark(130);
                red.BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x20, 0x40)); ((Border)red.Child).Background = red.BorderBrush;
                cyan.BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x20, 0xF0, 0xFF)); ((Border)cyan.Child).Background = cyan.BorderBrush;
                var rt = new TranslateTransform(); var ct = new TranslateTransform(); var mt = new TranslateTransform();
                red.RenderTransform = rt; cyan.RenderTransform = ct; main.RenderTransform = mt;
                stack.Children.Add(red); stack.Children.Add(cyan); stack.Children.Add(main);
                center.Children.Add(stack);
                var name = IntroName(); center.Children.Add(name);
                var random = new Random();
                var start = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
                timer.Tick += (_, _) =>
                {
                    double t = (DateTime.UtcNow - start).TotalMilliseconds;
                    double amount = t < 900 ? (random.NextDouble() < 0.6 ? 1 : 0.2) * (1 - t / 1100) : 0;
                    rt.X = (random.NextDouble() - 0.3) * 24 * amount; ct.X = -(random.NextDouble() - 0.3) * 24 * amount;
                    mt.X = random.NextDouble() < 0.25 ? (random.NextDouble() - 0.5) * 30 * amount : 0;
                    red.Opacity = cyan.Opacity = amount > 0 ? 0.9 : 0;
                    main.Opacity = amount > 0.5 && random.NextDouble() < 0.3 ? 0.2 : 1;
                    name.Opacity = t < 400 ? 0 : amount > 0 && random.NextDouble() < 0.35 ? 0.3 : 1;
                    name.RenderTransform = new TranslateTransform((random.NextDouble() - 0.5) * 16 * amount, 0);
                    if (t > 1300) timer.Stop();
                };
                timer.Start();
                total = 1700;
                return true;
            }
            case "assemble":
            {
                // Little squares fly in from all sides and click together into the logo.
                const double unit = 12;
                var canvas = new Canvas { Width = 13 * unit, Height = 13 * unit, HorizontalAlignment = HorizontalAlignment.Center };
                var random = new Random();
                int i = 0;
                for (int y = 0; y < 13; y++)
                    for (int x = 0; x < 13; x++)
                    {
                        bool outline = x < 2 || x > 10 || y < 2 || y > 10;
                        bool solid = x >= 7 && x <= 9 && y >= 7 && y <= 9;
                        if (!outline && !solid) continue;
                        var box = new Rectangle { Width = unit - 1.5, Height = unit - 1.5, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(accent) };
                        double angle = random.NextDouble() * Math.PI * 2, dist = 700 + random.NextDouble() * 400;
                        var move = new TranslateTransform(Math.Cos(angle) * dist, Math.Sin(angle) * dist);
                        box.RenderTransform = move;
                        Canvas.SetLeft(box, x * unit); Canvas.SetTop(box, y * unit);
                        canvas.Children.Add(box);
                        int delay = (int)(random.NextDouble() * 500);
                        move.BeginAnimation(TranslateTransform.XProperty, A(move.X, 0, 650, delay, easeOut));
                        move.BeginAnimation(TranslateTransform.YProperty, A(move.Y, 0, 650, delay, easeOut));
                        i++;
                    }
                center.Children.Add(canvas);
                var name = IntroName(); name.Opacity = 0;
                name.BeginAnimation(OpacityProperty, A(0, 1, 380, 1100));
                center.Children.Add(name);
                total = 1900;
                return true;
            }
            case "neon":
            {
                // The logo flickers on like a neon tube warming up, then glows steady.
                var mark = BuildLogoMark(130);
                mark.Effect = new DropShadowEffect { Color = accent, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.9 };
                var flicker = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, v) in new[] { (0, 0.0), (120, 0.8), (160, 0.1), (300, 0.9), (330, 0.2), (380, 0.0), (520, 1.0), (560, 0.4), (620, 1.0) })
                    flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                mark.BeginAnimation(OpacityProperty, flicker);
                var name = IntroName(44);
                name.Effect = new DropShadowEffect { Color = accent, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 };
                var nameFlicker = flicker.Clone(); nameFlicker.BeginTime = TimeSpan.FromMilliseconds(380);
                name.Opacity = 0;
                name.BeginAnimation(OpacityProperty, nameFlicker);
                center.Children.Add(mark); center.Children.Add(name);
                cover.Background = new SolidColorBrush(Color.FromRgb(0x08, 0x06, 0x10));
                total = 1700;
                return true;
            }
            case "liquid":
            {
                // The logo's outline fills up with the highlight color like a glass of water, with a wave on top.
                const double size = 140;
                var outline = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(9), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center };
                var fill = new Canvas { Width = size, Height = size, ClipToBounds = true };
                var water = new Path { Fill = new SolidColorBrush(accent) };
                fill.Children.Add(water);
                var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
                grid.Clip = new RectangleGeometry(new Rect(0, 0, size, size), 18, 18);
                grid.Children.Add(fill);
                var mark = BuildLogoMark(size);
                mark.Opacity = 0;
                grid.Children.Add(outline);
                grid.Children.Add(mark);
                center.Children.Add(grid);
                var name = IntroName(); name.Opacity = 0; center.Children.Add(name);
                var start = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) =>
                {
                    double t = (DateTime.UtcNow - start).TotalSeconds;
                    double level = size * (1 - Math.Min(1, t / 1.1));
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(0, size), true, true);
                        for (double x = 0; x <= size; x += 6) g.LineTo(new Point(x, level + Math.Sin(x / 18 + t * 9) * 6 * (1 - Math.Min(1, t / 1.3))), true, false);
                        g.LineTo(new Point(size, size), true, false);
                    }
                    water.Data = geo;
                    if (t > 1.2 && mark.Opacity == 0)
                    {
                        mark.BeginAnimation(OpacityProperty, A(0, 1, 250));
                        fill.BeginAnimation(OpacityProperty, A(1, 0, 300));
                        name.BeginAnimation(OpacityProperty, A(0, 1, 350));
                    }
                    if (t > 1.6) timer.Stop();
                };
                timer.Start();
                total = 1900;
                return true;
            }
            case "orbit":
            {
                // Dots circle in a widening loop, then dive into the middle where the logo lands.
                var canvas = new Canvas { Width = 0, Height = 0 };
                cover.Children.Add(canvas);
                canvas.HorizontalAlignment = HorizontalAlignment.Center; canvas.VerticalAlignment = VerticalAlignment.Center;
                var dots = Enumerable.Range(0, 12).Select(i => new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : accent) }).ToList();
                foreach (var d in dots) canvas.Children.Add(d);
                var mark = BuildLogoMark(120); mark.Opacity = 0;
                var scale = new ScaleTransform(0.5, 0.5); mark.RenderTransformOrigin = new Point(0.5, 0.5); mark.RenderTransform = scale;
                var name = IntroName(); name.Opacity = 0;
                center.Children.Add(mark); center.Children.Add(name);
                var start = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) =>
                {
                    double t = (DateTime.UtcNow - start).TotalSeconds;
                    double r = t < 0.8 ? 60 + 160 * (t / 0.8) : 220 * Math.Max(0, 1 - (t - 0.8) / 0.35);
                    for (int i = 0; i < dots.Count; i++)
                    {
                        double a = t * 7 + i * Math.PI * 2 / dots.Count;
                        Canvas.SetLeft(dots[i], Math.Cos(a) * r - 6 - 0); Canvas.SetTop(dots[i], Math.Sin(a) * r * 0.7 - 6 - 26);
                        dots[i].Opacity = r < 10 ? 0 : 1;
                    }
                    if (t > 1.1 && mark.Opacity == 0)
                    {
                        mark.BeginAnimation(OpacityProperty, A(0, 1, 200));
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.5, 1, 420, 0, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.5, 1, 420, 0, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                        name.BeginAnimation(OpacityProperty, A(0, 1, 350, 200));
                    }
                    if (t > 1.5) timer.Stop();
                };
                timer.Start();
                total = 2000;
                return true;
            }
            case "blinds":
            {
                // Horizontal slats in the highlight color flip open one after another, revealing the logo behind them.
                center.Children.Add(BuildLogoMark(120)); center.Children.Add(IntroName());
                var slats = new UniformGrid { Rows = 10 };
                for (int i = 0; i < 10; i++)
                {
                    var s = new Border { Background = new SolidColorBrush(i % 2 == 0 ? accent : Color.FromArgb(0xFF, (byte)(accent.R * 0.7), (byte)(accent.G * 0.7), (byte)(accent.B * 0.7))), RenderTransformOrigin = new Point(0.5, 0.5) };
                    var squash = new ScaleTransform(1, 1); s.RenderTransform = squash;
                    squash.BeginAnimation(ScaleTransform.ScaleYProperty, A(1, 0, 360, 250 + i * 60, new CubicEase { EasingMode = EasingMode.EaseIn }));
                    slats.Children.Add(s);
                }
                cover.Children.Add(slats);
                Panel.SetZIndex(slats, 10);
                total = 1800;
                return true;
            }
            case "burst":
            {
                // A flash in the middle throws sparks outward and leaves the logo behind.
                var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 52) };
                cover.Children.Add(canvas);
                var random = new Random();
                for (int i = 0; i < 60; i++)
                {
                    double angle = random.NextDouble() * Math.PI * 2, dist = 200 + random.NextDouble() * 500, size = 3 + random.NextDouble() * 7;
                    var p = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(random.NextDouble() < 0.3 ? Colors.White : accent) };
                    var move = new TranslateTransform(); p.RenderTransform = move;
                    Canvas.SetLeft(p, -size / 2); Canvas.SetTop(p, -size / 2);
                    canvas.Children.Add(p);
                    int ms = 700 + random.Next(500);
                    move.BeginAnimation(TranslateTransform.XProperty, A(0, Math.Cos(angle) * dist, ms, 150, easeOut));
                    move.BeginAnimation(TranslateTransform.YProperty, A(0, Math.Sin(angle) * dist, ms, 150, easeOut));
                    p.BeginAnimation(OpacityProperty, A(1, 0, ms, 150));
                }
                var flash = new Ellipse { Width = 300, Height = 300, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, accent.R, accent.G, accent.B)), RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false };
                var fs = new ScaleTransform(0, 0); flash.RenderTransform = fs;
                fs.BeginAnimation(ScaleTransform.ScaleXProperty, A(0, 2.4, 450, 100, easeOut)); fs.BeginAnimation(ScaleTransform.ScaleYProperty, A(0, 2.4, 450, 100, easeOut));
                flash.BeginAnimation(OpacityProperty, A(1, 0, 500, 150));
                cover.Children.Add(flash);
                var mark = BuildLogoMark(120); var name = IntroName();
                center.Opacity = 0; center.BeginAnimation(OpacityProperty, A(0, 1, 300, 250));
                center.Children.Add(mark); center.Children.Add(name);
                total = 1800;
                return true;
            }
            case "hologram":
            {
                // A bright scan line sweeps down and draws the logo as it goes, over faint scan lines, with a flicker.
                var mark = BuildLogoMark(130); var name = IntroName();
                var holder = new StackPanel(); holder.Children.Add(mark); holder.Children.Add(name);
                var reveal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                var edge1 = new GradientStop(Colors.Black, 0); var edge2 = new GradientStop(Colors.Transparent, 0);
                reveal.GradientStops.Add(edge1); reveal.GradientStops.Add(edge2);
                holder.OpacityMask = reveal;
                edge1.BeginAnimation(GradientStop.OffsetProperty, A(0, 1.05, 1000, 100));
                edge2.BeginAnimation(GradientStop.OffsetProperty, A(0.02, 1.07, 1000, 100));
                holder.Effect = new DropShadowEffect { Color = Color.FromRgb(0x40, 0xE0, 0xFF), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 };
                center.Children.Add(holder);
                var line = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(0x80, 0xF0, 0xFF)), Effect = new DropShadowEffect { Color = Color.FromRgb(0x40, 0xE0, 0xFF), BlurRadius = 20, ShadowDepth = 0 } };
                var lineMove = new TranslateTransform(); line.RenderTransform = lineMove;
                cover.Children.Add(line);
                cover.Loaded += (_, _) =>
                {
                    lineMove.BeginAnimation(TranslateTransform.YProperty, A(cover.ActualHeight * 0.3, cover.ActualHeight * 0.68, 1000, 100));
                    line.BeginAnimation(OpacityProperty, A(1, 0, 250, 1100));
                };
                var scan = new Border { Background = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x26, 0x40, 0xE0, 0xFF)), null, new RectangleGeometry(new Rect(0, 0, 4, 2)))) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute }, IsHitTestVisible = false };
                cover.Children.Add(scan);
                cover.Background = new SolidColorBrush(Color.FromRgb(0x02, 0x08, 0x10));
                total = 1800;
                return true;
            }
            case "flip":
            {
                // Five cards flip over one by one to spell the logo, the last one in the highlight color.
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                string[] letters = { "S", "Q", "U", "A", "D" };
                for (int i = 0; i < 5; i++)
                {
                    var face = new Border { Width = 70, Height = 96, Margin = new Thickness(6), CornerRadius = new CornerRadius(10), RenderTransformOrigin = new Point(0.5, 0.5) };
                    face.SetResourceReference(Border.BackgroundProperty, i == 4 ? "AccentBrush" : "TileBrush");
                    var letter = new TextBlock { Text = letters[i], FontSize = 44, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
                    letter.SetResourceReference(TextBlock.ForegroundProperty, i == 4 ? "BgBrush" : "FgBrush");
                    face.Child = letter;
                    var turn = new ScaleTransform(0, 1); face.RenderTransform = turn;
                    var flipIn = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 140) };
                    flipIn.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    flipIn.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
                    turn.BeginAnimation(ScaleTransform.ScaleXProperty, flipIn);
                    letter.BeginAnimation(OpacityProperty, A(0, 1, 120, i * 140 + 190));
                    row.Children.Add(face);
                }
                center.Children.Add(row);
                var name = IntroName(); name.Opacity = 0; name.BeginAnimation(OpacityProperty, A(0, 1, 350, 900));
                center.Children.Add(name);
                total = 1900;
                return true;
            }
            case "aurora":
            {
                // Soft colored lights drift across a dark sky while the name settles in, its letters drawing together.
                cover.Background = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x14));
                var colors = new[] { accent, Color.FromRgb(0x3D, 0xF5, 0xB0), Color.FromRgb(0x8A, 0x5C, 0xFF) };
                for (int i = 0; i < 3; i++)
                {
                    var blob = new Ellipse
                    {
                        Width = 900, Height = 420, Fill = new RadialGradientBrush(Color.FromArgb(0xA0, colors[i].R, colors[i].G, colors[i].B), Color.FromArgb(0, colors[i].R, colors[i].G, colors[i].B)),
                        IsHitTestVisible = false, Effect = Perf.Active ? null : new BlurEffect { Radius = 60 }, Opacity = 0
                    };
                    var drift = new TranslateTransform(-400 + i * 300, -120 + i * 110); blob.RenderTransform = drift;
                    drift.BeginAnimation(TranslateTransform.XProperty, A(drift.X, drift.X + 260 * (i % 2 == 0 ? 1 : -1), 2200, 0, new SineEase()));
                    blob.BeginAnimation(OpacityProperty, A(0, 0.85, 700, i * 150));
                    cover.Children.Add(blob);
                }
                var name = IntroName(56);
                name.FontWeight = FontWeights.Light;
                name.Foreground = Brushes.White;
                name.Margin = new Thickness(0);
                var spacing = new ScaleTransform(1.25, 1); name.RenderTransformOrigin = new Point(0.5, 0.5); name.RenderTransform = spacing;
                spacing.BeginAnimation(ScaleTransform.ScaleXProperty, A(1.25, 1, 1200, 0, easeOut));
                name.Opacity = 0; name.BeginAnimation(OpacityProperty, A(0, 1, 900, 200));
                name.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 };
                center.Children.Add(name);
                total = 2000;
                return true;
            }
        }
        return false;
    }
}
