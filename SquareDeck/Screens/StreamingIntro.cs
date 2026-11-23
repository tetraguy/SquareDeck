using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Short "show is starting" animations for the streaming services (Netflix, Hulu and the rest), about two seconds
/// each, drawn while the service's page loads: theater curtains, a film countdown, a clapperboard, a marquee of
/// lights, or popcorn. The service's own tile is the star at the end of each. The music services (YouTube Music,
/// Apple Music) have their own, about music: a record on a turntable, an equalizer, notes off a staff, or a speaker.
/// </summary>
public static class StreamingIntro
{
    public static readonly (string Id, string Name, string About)[] Styles =
    {
        ("curtain", "Curtain call", "Red velvet curtains part under a spotlight to reveal the service"),
        ("countdown", "Film countdown", "An old film leader counts down 3, 2, 1 with flicker and grain"),
        ("clapper", "Clapperboard", "A clapperboard drops in and snaps shut: action!"),
        ("marquee", "Marquee lights", "A cinema sign lights up bulb by bulb around the logo"),
        ("popcorn", "Popcorn", "Popcorn bursts up from the bottom of the screen and the logo pops in"),
        ("random", "Surprise me", "A different one each time"),
        ("none", "None", "Just the logo"),
    };

    /// <summary>The music services' own openings (one of these at random; "None" in Settings still turns them off).</summary>
    public static readonly (string Id, string Name, string About)[] MusicStyles =
    {
        ("vinyl", "Turntable", "A record slides onto the turntable, spins up and the tonearm drops"),
        ("equalizer", "Equalizer", "Equalizer bars bounce to the beat as the logo comes up"),
        ("staff", "Sheet music", "A staff draws across the screen and notes hop along it, then float away"),
        ("speaker", "Speaker", "A speaker thumps and sound waves ripple out from it"),
    };

    public const int Length = 2300;

    private static DoubleAnimation A(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };

    public static FrameworkElement Build(string style, StreamingService service, FrameworkElement logo)
    {
        if (style == "random" || Styles.All(s => s.Id != style)) style = Styles[Random.Shared.Next(5)].Id;
        // Music gets music: one of its own openings (unless they're turned off altogether).
        if (service.Music && style != "none") style = MusicStyles[Random.Shared.Next(MusicStyles.Length)].Id;
        var root = new Grid { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x07, 0x07, 0x0B)) };
        var canvas = new Canvas { IsHitTestVisible = false };
        root.Children.Add(canvas);
        var middle = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(logo);
        root.Children.Add(middle);
        var scale = new ScaleTransform(1, 1);
        logo.RenderTransformOrigin = new Point(0.5, 0.5);
        logo.RenderTransform = scale;
        var pop = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        void PopLogo(int delay)
        {
            logo.Opacity = 0;
            logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 260, delay));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.5, 1, 520, delay, pop));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.5, 1, 520, delay, pop));
        }

        if (Theme.ReduceMotion || style == "none")
        {
            logo.Opacity = 0;
            logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 400));
            return root;
        }

        // The canvas needs the screen's size, which is only known once laid out.
        root.Loaded += (_, _) =>
        {
            double w = Math.Max(root.ActualWidth, 800), h = Math.Max(root.ActualHeight, 600);
            switch (style)
            {
                case "curtain": Curtain(canvas, root, w, h, service, easeIn, easeOut); PopLogo(900); break;
                case "countdown": Countdown(canvas, w, h); PopLogo(1650); break;
                case "clapper": Clapper(canvas, w, h, easeIn); PopLogo(1250); break;
                case "marquee": Marquee(canvas, w, h, service); PopLogo(250); break;
                case "popcorn": Popcorn(canvas, w, h); PopLogo(1000); break;
                case "vinyl": Vinyl(canvas, w, h, service.Brand, easeOut); PopLogo(1150); break;
                case "equalizer": Equalizer(canvas, w, h, service.Brand); PopLogo(650); break;
                case "staff": Staff(canvas, w, h, service.Brand); PopLogo(1350); break;
                case "speaker": Speaker(canvas, w, h, service.Brand, easeOut); PopLogo(700); break;
            }
        };
        logo.Opacity = 0;
        return root;
    }

    // ------------------------------------------------------------------ curtains

    private static void Curtain(Canvas canvas, Grid root, double w, double h, StreamingService service, IEasingFunction easeIn, IEasingFunction easeOut)
    {
        // A warm spotlight on the stage, then two velvet curtains with folds that draw apart.
        var spot = new Ellipse
        {
            Width = w * 0.7, Height = h * 0.9, Opacity = 0,
            Fill = new RadialGradientBrush(Color.FromArgb(0x80, 0xFF, 0xE8, 0xB0), Color.FromArgb(0, 0xFF, 0xE8, 0xB0))
        };
        Canvas.SetLeft(spot, w * 0.15);
        Canvas.SetTop(spot, h * 0.05);
        canvas.Children.Add(spot);
        spot.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 500, 700));

        Rectangle Drape(bool left)
        {
            var folds = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), SpreadMethod = GradientSpreadMethod.Repeat };
            folds.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x05, 0x10), 0));
            folds.GradientStops.Add(new GradientStop(Color.FromRgb(0xB0, 0x12, 0x24), 0.5));
            folds.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x05, 0x10), 1));
            folds.RelativeTransform = new ScaleTransform(0.12, 1);
            var drape = new Rectangle { Width = w / 2 + 40, Height = h, Fill = folds, Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.7 } };
            var move = new TranslateTransform();
            drape.RenderTransform = move;
            Canvas.SetLeft(drape, left ? -20 : w / 2 - 20);
            canvas.Children.Add(drape);
            move.BeginAnimation(TranslateTransform.XProperty, A(0, left ? -w / 2 - 60 : w / 2 + 60, 1100, 450, easeIn));
            return drape;
        }
        Drape(true);
        Drape(false);
        // The valance across the top stays.
        var valance = new Rectangle { Width = w, Height = h * 0.12, Fill = new LinearGradientBrush(Color.FromRgb(0x7A, 0x08, 0x16), Color.FromRgb(0x3A, 0x02, 0x08), 90) };
        canvas.Children.Add(valance);
        var trim = new Rectangle { Width = w, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(0xD8, 0xB0, 0x48)) };
        Canvas.SetTop(trim, h * 0.12);
        canvas.Children.Add(trim);
    }

    // ------------------------------------------------------------------ film countdown

    private static void Countdown(Canvas canvas, double w, double h)
    {
        // Sepia leader: crosshair, a sweeping wedge each second, big numbers 3-2-1, grain and flicker.
        var sepia = new Rectangle { Width = w, Height = h, Fill = new RadialGradientBrush(Color.FromRgb(0xC9, 0xB3, 0x8A), Color.FromRgb(0x5A, 0x48, 0x30)) };
        canvas.Children.Add(sepia);
        var flicker = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(180) };
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.88, KeyTime.FromPercent(0.4)));
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.96, KeyTime.FromPercent(0.7)));
        sepia.BeginAnimation(UIElement.OpacityProperty, flicker);

        double cx = w / 2, cy = h / 2, r = Math.Min(w, h) * 0.32;
        var ink = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x18, 0x10));
        canvas.Children.Add(new Line { X1 = 0, Y1 = cy, X2 = w, Y2 = cy, Stroke = ink, StrokeThickness = 3 });
        canvas.Children.Add(new Line { X1 = cx, Y1 = 0, X2 = cx, Y2 = h, Stroke = ink, StrokeThickness = 3 });
        foreach (double k in new[] { 1.0, 0.82 })
        {
            var ring = new Ellipse { Width = r * 2 * k, Height = r * 2 * k, Stroke = Brushes.White, StrokeThickness = 6, Opacity = 0.9 };
            Canvas.SetLeft(ring, cx - r * k);
            Canvas.SetTop(ring, cy - r * k);
            canvas.Children.Add(ring);
        }

        // The sweep: a dark wedge growing clockwise, once a second.
        var wedge = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x0A, 0x05)) };
        canvas.Children.Add(wedge);
        var number = new TextBlock { FontSize = r * 1.1, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x12, 0x0A)), FontFamily = new FontFamily("Georgia") };
        canvas.Children.Add(number);
        var started = DateTime.UtcNow;
        void Frame(object? s, EventArgs e)
        {
            double t = (DateTime.UtcNow - started).TotalMilliseconds;
            if (t > 1650 || canvas.Parent == null) { CompositionTarget.Rendering -= Frame; canvas.Children.Clear(); return; }
            int left = 3 - (int)(t / 550);
            double part = (t % 550) / 550;
            number.Text = left.ToString();
            number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(number, cx - number.DesiredSize.Width / 2);
            Canvas.SetTop(number, cy - number.DesiredSize.Height / 2);
            double angle = part * 360;
            if (angle < 1) { wedge.Data = null; return; }
            double rad = (angle - 90) * Math.PI / 180;
            var end = new Point(cx + r * 1.6 * Math.Cos(rad), cy + r * 1.6 * Math.Sin(rad));
            var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(cx, cy - r * 1.6), true));
            fig.Segments.Add(new ArcSegment(end, new Size(r * 1.6, r * 1.6), 0, angle > 180, SweepDirection.Clockwise, true));
            wedge.Data = new PathGeometry(new[] { fig });
        }
        CompositionTarget.Rendering += Frame;

        // Dust and scratches.
        var rand = new Random();
        for (int i = 0; i < 14; i++)
        {
            var scratch = new Rectangle { Width = 1.5, Height = h * (0.2 + rand.NextDouble() * 0.6), Fill = Brushes.White, Opacity = 0 };
            Canvas.SetLeft(scratch, rand.NextDouble() * w);
            Canvas.SetTop(scratch, rand.NextDouble() * h * 0.4);
            canvas.Children.Add(scratch);
            scratch.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(60))
                { BeginTime = TimeSpan.FromMilliseconds(rand.Next(0, 1500)), AutoReverse = true });
        }
    }

    // ------------------------------------------------------------------ clapperboard

    private static void Clapper(Canvas canvas, double w, double h, IEasingFunction easeIn)
    {
        double bw = 420, bh = 300, x = (w - bw) / 2, y = (h - bh) / 2;
        var board = new Grid { Width = bw, Height = bh };
        Canvas.SetLeft(board, x);
        Canvas.SetTop(board, y);
        var slate = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20)), Margin = new Thickness(0, 60, 0, 0),
                                 BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), BorderThickness = new Thickness(3) };
        var lines = new StackPanel { Margin = new Thickness(26, 22, 26, 0) };
        foreach (var (label, value) in new[] { ("SCENE", "1"), ("TAKE", "1"), ("ROLL", "A") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 22, FontFamily = new FontFamily("Consolas"), Width = 120 });
            row.Children.Add(new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 30, FontFamily = new FontFamily("Segoe Print") });
            lines.Children.Add(row);
        }
        slate.Child = lines;
        board.Children.Add(slate);

        Border Stripes()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), SpreadMethod = GradientSpreadMethod.Repeat, MappingMode = BrushMappingMode.Absolute };
            brush.EndPoint = new Point(60, 0);
            brush.GradientStops.Add(new GradientStop(Colors.White, 0));
            brush.GradientStops.Add(new GradientStop(Colors.White, 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x18, 0x18, 0x1C), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x18, 0x18, 0x1C), 1));
            brush.RelativeTransform = new SkewTransform(-30, 0);
            return new Border { Height = 46, CornerRadius = new CornerRadius(6), Background = brush, BorderBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C)), BorderThickness = new Thickness(3) };
        }
        var bottomStick = Stripes();
        bottomStick.VerticalAlignment = VerticalAlignment.Top;
        bottomStick.Margin = new Thickness(0, 14, 0, 0);
        board.Children.Add(bottomStick);
        var topStick = Stripes();
        topStick.VerticalAlignment = VerticalAlignment.Top;
        topStick.Margin = new Thickness(0, 14, 0, 0);
        var hinge = new RotateTransform(-28);
        topStick.RenderTransformOrigin = new Point(0, 0.5);
        topStick.RenderTransform = hinge;
        board.Children.Add(topStick);
        canvas.Children.Add(board);

        // Drops in, snaps shut with a flash, then falls away for the logo.
        var drop = new TranslateTransform();
        board.RenderTransform = drop;
        drop.BeginAnimation(TranslateTransform.YProperty, A(-h, 0, 450, 0, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }));
        hinge.BeginAnimation(RotateTransform.AngleProperty, A(-28, 0, 160, 750, easeIn));
        var flash = new Rectangle { Width = w, Height = h, Fill = Brushes.White, Opacity = 0 };
        canvas.Children.Add(flash);
        flash.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(70)) { BeginTime = TimeSpan.FromMilliseconds(910), AutoReverse = true });
        var shake = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(910) };
        foreach (var (t, v) in new[] { (0, 0.0), (40, -8.0), (80, 6.0), (120, -3.0), (160, 0.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        drop.BeginAnimation(TranslateTransform.XProperty, shake);
        board.BeginAnimation(UIElement.OpacityProperty, A(1, 0, 300, 1150));
    }

    // ------------------------------------------------------------------ marquee

    private static void Marquee(Canvas canvas, double w, double h, StreamingService service)
    {
        // A sign frame around the middle, its bulbs lighting one after another and then chasing.
        double fw = 560, fh = 360, x = (w - fw) / 2, y = (h - fh) / 2;
        var brand = service.Brand;
        var frame = new Border { Width = fw, Height = fh, CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(10),
                                 BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x9B, 0x3C)),
                                 Background = new RadialGradientBrush(Color.FromArgb(0x60, brand.R, brand.G, brand.B), Color.FromArgb(0x10, brand.R, brand.G, brand.B)) };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        canvas.Children.Add(frame);
        var glow = Perf.Active ? null : new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD8, 0x70), BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
        var bulbs = new List<Ellipse>();
        void Bulb(double bx, double by)
        {
            var b = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x9A)), Opacity = 0.15, Effect = glow };
            Canvas.SetLeft(b, bx - 8);
            Canvas.SetTop(b, by - 8);
            canvas.Children.Add(b);
            bulbs.Add(b);
        }
        double inset = -18;
        int across = 16, down = 10;
        for (int i = 0; i <= across; i++) Bulb(x + i * fw / across, y + inset);
        for (int i = 1; i <= down; i++) Bulb(x + fw - inset, y + i * fh / down);
        for (int i = across - 1; i >= 0; i--) Bulb(x + i * fw / across, y + fh - inset);
        for (int i = down - 1; i >= 1; i--) Bulb(x + inset, y + i * fh / down);
        for (int i = 0; i < bulbs.Count; i++)
        {
            // Each bulb switches on in turn, then the lights chase around.
            var on = new DoubleAnimationUsingKeyFrames();
            int at = i * 900 / bulbs.Count;
            on.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            on.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
            for (int t = 1000; t < 2300; t += 120)
                on.KeyFrames.Add(new DiscreteDoubleKeyFrame(((i + t / 120) % 3 == 0) ? 0.35 : 1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
            bulbs[i].BeginAnimation(UIElement.OpacityProperty, on);
        }
        var now = new TextBlock { Text = "NOW SHOWING", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x9A)), Opacity = 0, Effect = glow };
        now.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(now, (w - now.DesiredSize.Width) / 2);
        Canvas.SetTop(now, y + 26);
        canvas.Children.Add(now);
        now.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 300, 900));
    }

    // ------------------------------------------------------------------ popcorn

    private static void Popcorn(Canvas canvas, double w, double h)
    {
        // A striped bucket rises, kernels burst out of it in arcs, then it all drops away.
        var bucket = new Path
        {
            Data = Geometry.Parse("M0,0 L180,0 L150,220 L30,220 Z"),
            Fill = MakeStripes(), Stroke = new SolidColorBrush(Color.FromRgb(0xA0, 0x10, 0x18)), StrokeThickness = 3
        };
        var bucketMove = new TranslateTransform();
        bucket.RenderTransform = bucketMove;
        Canvas.SetLeft(bucket, (w - 180) / 2);
        Canvas.SetTop(bucket, h - 230);
        var rand = new Random();
        var kernels = new List<(UIElement Kernel, double Vx, double Vy, double Delay)>();
        for (int i = 0; i < 46; i++)
        {
            double size = 22 + rand.NextDouble() * 18;
            var kernel = new Path { Data = KernelShape(size, rand), Fill = new SolidColorBrush(Color.FromRgb(0xFF, (byte)(0xF0 + rand.Next(15)), (byte)(0xC8 + rand.Next(40)))),
                                    Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xC8, 0x8A, 0x20)), StrokeThickness = 1.2, Opacity = 0 };
            Canvas.SetLeft(kernel, w / 2 - size / 2);
            Canvas.SetTop(kernel, h - 230);
            canvas.Children.Add(kernel);
            kernels.Add((kernel, (rand.NextDouble() - 0.5) * 900, -(500 + rand.NextDouble() * 650), rand.NextDouble() * 700));
        }
        canvas.Children.Add(bucket);
        bucketMove.BeginAnimation(TranslateTransform.YProperty, A(260, 0, 350, 0, new CubicEase { EasingMode = EasingMode.EaseOut }));
        foreach (var (kernel, vx, vy, delay) in kernels)
        {
            var move = new TranslateTransform();
            var spin = new RotateTransform();
            kernel.RenderTransform = new TransformGroup { Children = { spin, move } };
            int start = 300 + (int)delay;
            kernel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(60)) { BeginTime = TimeSpan.FromMilliseconds(start) });
            // A throw: out fast, slowing at the top, falling back down.
            move.BeginAnimation(TranslateTransform.XProperty, A(0, vx, 1300, start));
            var arc = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(start) };
            arc.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            arc.KeyFrames.Add(new EasingDoubleKeyFrame(vy, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            arc.KeyFrames.Add(new EasingDoubleKeyFrame(vy * 0.2 + 400, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            move.BeginAnimation(TranslateTransform.YProperty, arc);
            spin.BeginAnimation(RotateTransform.AngleProperty, A(0, (vx > 0 ? 1 : -1) * 540, 1300, start));
        }
        bucketMove.BeginAnimation(TranslateTransform.YProperty, A(0, 300, 400, 1400, new CubicEase { EasingMode = EasingMode.EaseIn }));
    }

    private static Brush MakeStripes()
    {
        var stripes = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(36, 0), MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat };
        stripes.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0x20, 0x2A), 0));
        stripes.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0x20, 0x2A), 0.5));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 1));
        return stripes;
    }

    /// <summary>A puffy popcorn kernel: a few overlapping blobs.</summary>
    private static Geometry KernelShape(double size, Random rand)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int i = 0; i < 4; i++)
        {
            double rx = size * (0.28 + rand.NextDouble() * 0.14), ry = size * (0.24 + rand.NextDouble() * 0.14);
            group.Children.Add(new EllipseGeometry(new Point(size / 2 + (rand.NextDouble() - 0.5) * size * 0.45, size / 2 + (rand.NextDouble() - 0.5) * size * 0.45), rx, ry));
        }
        return group;
    }

    // ================================================================ music

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Brush(Color c, byte alpha = 0xFF) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>A record slides onto the platter and spins up, the tonearm swings in and drops, notes rise, and the logo lands on the label.</summary>
    private static void Vinyl(Canvas canvas, double w, double h, Color brand, IEasingFunction easeOut)
    {
        double r = Math.Min(w, h) * 0.36, cx = w / 2, cy = h / 2;
        // The platter under it.
        var platter = new Ellipse { Width = r * 2.16, Height = r * 2.16, Fill = new RadialGradientBrush(Color.FromRgb(0x3A, 0x3A, 0x42), Color.FromRgb(0x18, 0x18, 0x1E)) };
        Canvas.SetLeft(platter, cx - r * 1.08);
        Canvas.SetTop(platter, cy - r * 1.08);
        platter.Opacity = 0;
        canvas.Children.Add(platter);
        platter.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 300));

        // The record: black with grooves, a colored label, and a light streak (the streak stays put as it spins).
        var disc = new Grid { Width = r * 2, Height = r * 2 };
        disc.Children.Add(new Ellipse { Fill = new RadialGradientBrush(Color.FromRgb(0x22, 0x22, 0x26), Color.FromRgb(0x08, 0x08, 0x0A)) });
        for (double g = 0.42; g < 0.98; g += 0.045)
            disc.Children.Add(new Ellipse { Width = r * 2 * g, Height = r * 2 * g, Stroke = new SolidColorBrush(Color.FromArgb((byte)(0x28 + (int)(g * 30) % 20), 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var spinning = new Grid();
        var label = new Ellipse { Width = r * 0.74, Height = r * 0.74, Fill = new RadialGradientBrush(Mix(brand, Colors.White, 0.25), brand), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        spinning.Children.Add(label);
        // A mark on the label, so the spin shows.
        spinning.Children.Add(new Rectangle { Width = r * 0.24, Height = 6, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(r * 0.3, 0, 0, 0) });
        spinning.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var spin = new RotateTransform();
        spinning.RenderTransformOrigin = new Point(0.5, 0.5);
        spinning.RenderTransform = spin;
        disc.Children.Add(spinning);
        disc.Children.Add(new Path
        {
            Data = new PathGeometry(new[] { new PathFigure(new Point(r, r), new PathSegment[] { new LineSegment(new Point(r - r * 0.35, 0.05 * r), true), new ArcSegment(new Point(r + r * 0.35, 0.05 * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true) }, true) }),
            Fill = new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90)
        });
        var slide = new TranslateTransform();
        disc.RenderTransform = slide;
        Canvas.SetLeft(disc, cx - r);
        Canvas.SetTop(disc, cy - r);
        canvas.Children.Add(disc);
        slide.BeginAnimation(TranslateTransform.XProperty, A(-w / 2 - r * 2, 0, 520, 0, easeOut));
        // Spins up to 33⅓, then keeps going.
        var up = new DoubleAnimationUsingKeyFrames();
        up.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
        up.KeyFrames.Add(new EasingDoubleKeyFrame(360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        up.KeyFrames.Add(new LinearDoubleKeyFrame(360 * 3.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        spin.BeginAnimation(RotateTransform.AngleProperty, up);

        // The tonearm swings over from its rest and lowers onto the record.
        double px = cx + r * 1.18, py = cy - r * 0.95;
        var arm = new Canvas();
        arm.Children.Add(new Ellipse { Width = 34, Height = 34, Fill = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC6)), Stroke = new SolidColorBrush(Color.FromRgb(0x60, 0x64, 0x6E)), StrokeThickness = 2 });
        Canvas.SetLeft(arm.Children[0], -17);
        Canvas.SetTop(arm.Children[0], -17);
        var rod = new Line { X1 = 0, Y1 = 0, X2 = -r * 0.32, Y2 = r * 1.15, Stroke = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        arm.Children.Add(rod);
        var head = new Rectangle { Width = 22, Height = 36, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)), Stroke = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE4)), StrokeThickness = 2, RenderTransform = new RotateTransform(18, 11, 0) };
        Canvas.SetLeft(head, -r * 0.32 - 11);
        Canvas.SetTop(head, r * 1.15 - 6);
        arm.Children.Add(head);
        var swing = new RotateTransform(-38);
        var lift = new ScaleTransform(1.06, 1.06);
        arm.RenderTransform = new TransformGroup { Children = { lift, swing } };
        Canvas.SetLeft(arm, px);
        Canvas.SetTop(arm, py);
        arm.Opacity = 0;
        canvas.Children.Add(arm);
        arm.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 200, 350));
        swing.BeginAnimation(RotateTransform.AngleProperty, A(-38, 0, 500, 520, easeOut));
        lift.BeginAnimation(ScaleTransform.ScaleXProperty, A(1.06, 1, 160, 1020));
        lift.BeginAnimation(ScaleTransform.ScaleYProperty, A(1.06, 1, 160, 1020));

        // Once the needle drops: notes drift up, and the record dims behind the logo.
        FloatNotes(canvas, w, h, brand, 1150, 10, cx, cy);
        disc.BeginAnimation(UIElement.OpacityProperty, A(1, 0.45, 500, 1150));
        platter.BeginAnimation(UIElement.OpacityProperty, A(1, 0.45, 500, 1150));
    }

    /// <summary>A row of equalizer bars springs up and bounces to a beat, with a glow pulsing behind the logo.</summary>
    private static void Equalizer(Canvas canvas, double w, double h, Color brand)
    {
        var rand = new Random();
        // The glow, pulsing on the beat.
        var glow = new Ellipse { Width = h * 0.9, Height = h * 0.9, Fill = new RadialGradientBrush(Color.FromArgb(0x70, brand.R, brand.G, brand.B), Color.FromArgb(0, brand.R, brand.G, brand.B)), RenderTransformOrigin = new Point(0.5, 0.5) };
        var pulse = new ScaleTransform(0.6, 0.6);
        glow.RenderTransform = pulse;
        Canvas.SetLeft(glow, w / 2 - h * 0.45);
        Canvas.SetTop(glow, h / 2 - h * 0.45);
        canvas.Children.Add(glow);
        var beat = new DoubleAnimation(0.85, 1.08, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        pulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
        pulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);

        // The bars, top and bottom, mirrored, in the brand's color fading to white at the tips.
        int n = 32;
        double gap = 6, barW = (w - gap * (n + 1)) / n, maxH = h * 0.3;
        for (int mirror = 0; mirror < 2; mirror++)
            for (int i = 0; i < n; i++)
            {
                double centre = 1 - Math.Abs(i - (n - 1) / 2.0) / (n / 2.0); // taller in the middle
                var bar = new Rectangle
                {
                    Width = barW, Height = maxH, RadiusX = Math.Min(4, barW / 2), RadiusY = Math.Min(4, barW / 2),
                    Fill = new LinearGradientBrush(Mix(brand, Colors.White, 0.55), brand, mirror == 0 ? 90 : 270),
                    Opacity = mirror == 0 ? 0.95 : 0.35, RenderTransformOrigin = new Point(0.5, mirror == 0 ? 1 : 0)
                };
                var size = new ScaleTransform(1, 0.02);
                bar.RenderTransform = size;
                Canvas.SetLeft(bar, gap + i * (barW + gap));
                Canvas.SetTop(bar, mirror == 0 ? h - maxH - 8 : 8);
                canvas.Children.Add(bar);
                double peak = 0.25 + 0.75 * (0.35 + 0.65 * centre) * (0.55 + rand.NextDouble() * 0.45);
                var dance = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 12 % 160), RepeatBehavior = RepeatBehavior.Forever };
                int t = 0;
                dance.KeyFrames.Add(new LinearDoubleKeyFrame(0.02, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                for (int k = 0; k < 6; k++)
                {
                    t += 120 + rand.Next(120);
                    double v = k % 2 == 0 ? peak * (0.6 + rand.NextDouble() * 0.4) : peak * (0.15 + rand.NextDouble() * 0.3);
                    dance.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                }
                size.BeginAnimation(ScaleTransform.ScaleYProperty, dance);
            }
    }

    /// <summary>A five-line staff draws across, notes hop along it one after another, then lift off and float away.</summary>
    private static void Staff(Canvas canvas, double w, double h, Color brand)
    {
        double top = h / 2 - 60, space = 30, left = w * 0.08, width = w * 0.84;
        var lines = new List<Line>();
        for (int i = 0; i < 5; i++)
        {
            var line = new Line { X1 = 0, Y1 = 0, X2 = width, Y2 = 0, Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2 };
            var grow = new ScaleTransform(0, 1);
            line.RenderTransform = grow;
            Canvas.SetLeft(line, left);
            Canvas.SetTop(line, top + i * space);
            canvas.Children.Add(line);
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, A(0, 1, 520, i * 50, new CubicEase { EasingMode = EasingMode.EaseOut }));
            line.BeginAnimation(UIElement.OpacityProperty, A(1, 0, 400, 1300));
            lines.Add(line);
        }
        // A treble clef at the start.
        var clef = new TextBlock { Text = "\U0001D11E", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = space * 5.4, Foreground = Brushes.White, Opacity = 0 };
        Canvas.SetLeft(clef, left + 6);
        Canvas.SetTop(clef, top - space * 2.1);
        canvas.Children.Add(clef);
        var clefFade = new DoubleAnimationUsingKeyFrames();
        clefFade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        clefFade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
        clefFade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300))));
        clefFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
        clef.BeginAnimation(UIElement.OpacityProperty, clefFade);

        // The notes: a little melody, each landing on its line or space, then rising off the page.
        string[] glyphs = { "\u2669", "\u266A", "\u266B", "\u266C", "\u266A", "\u2669", "\u266B", "\u266A", "\u266C", "\u266A" };
        int[] steps = { 4, 3, 5, 2, 4, 6, 3, 5, 1, 4 };
        for (int i = 0; i < glyphs.Length; i++)
        {
            var note = new TextBlock
            {
                Text = glyphs[i], FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = space * 2.2, FontWeight = FontWeights.Bold,
                Foreground = i % 3 == 1 ? new SolidColorBrush(Mix(brand, Colors.White, 0.2)) : Brushes.White, Opacity = 0
            };
            var move = new TranslateTransform();
            note.RenderTransform = move;
            double x = left + width * (0.16 + 0.8 * i / glyphs.Length);
            Canvas.SetLeft(note, x);
            Canvas.SetTop(note, top + steps[i] * space / 2 - space * 1.9);
            canvas.Children.Add(note);
            int land = 320 + i * 85;
            note.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 90, land));
            // Hop in from above, then (after the melody) float up and away.
            var y = new DoubleAnimationUsingKeyFrames();
            y.KeyFrames.Add(new DiscreteDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(land))));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(land + 200)), new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1250 + i * 25))));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-h * 0.5 - i * 10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2250 + i * 25)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            move.BeginAnimation(TranslateTransform.YProperty, y);
            move.BeginAnimation(TranslateTransform.XProperty, A(0, (i % 2 == 0 ? -1 : 1) * (30 + i * 4), 1000, 1250 + i * 25));
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(land + 90))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2250))));
            note.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    /// <summary>A speaker thumps to a beat and rings of sound ripple out across the screen; the logo bumps with it.</summary>
    private static void Speaker(Canvas canvas, double w, double h, Color brand, IEasingFunction easeOut)
    {
        double cx = w / 2, cy = h / 2, r = Math.Min(w, h) * 0.24;
        // Sound waves: rings leaving the speaker on every beat.
        for (int i = 0; i < 9; i++)
        {
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Brush(Mix(brand, Colors.White, i % 2 * 0.4)), StrokeThickness = 5, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            var grow = new ScaleTransform(1, 1);
            ring.RenderTransform = grow;
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            canvas.Children.Add(ring);
            int at = 200 + i * 230;
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, A(1, 3.6, 1100, at, easeOut));
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, A(1, 3.6, 1100, at, easeOut));
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 1100))));
            ring.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        // The speaker: a dark cabinet circle, the cone and the dust cap, pumping on each beat.
        var cone = new Grid { Width = r * 2, Height = r * 2, RenderTransformOrigin = new Point(0.5, 0.5) };
        cone.Children.Add(new Ellipse { Fill = new RadialGradientBrush(Color.FromRgb(0x2C, 0x2C, 0x34), Color.FromRgb(0x0C, 0x0C, 0x10)), Stroke = Brush(Color.FromRgb(0x50, 0x50, 0x5A)), StrokeThickness = 8 });
        cone.Children.Add(new Ellipse { Width = r * 1.5, Height = r * 1.5, Fill = new RadialGradientBrush(Color.FromRgb(0x14, 0x14, 0x18), Color.FromRgb(0x30, 0x30, 0x38)), Stroke = Brush(brand, 0xA0), StrokeThickness = 3 });
        cone.Children.Add(new Ellipse { Width = r * 0.5, Height = r * 0.5, Fill = new RadialGradientBrush(Mix(brand, Colors.White, 0.3), brand) });
        var thump = new ScaleTransform(0.2, 0.2);
        cone.RenderTransform = thump;
        Canvas.SetLeft(cone, cx - r);
        Canvas.SetTop(cone, cy - r);
        canvas.Children.Add(cone);
        thump.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.2, 1, 300, 0, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
        thump.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.2, 1, 300, 0, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
        var beat = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        beat.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        // Hand over from the pop-in to the beat.
        var after = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        after.Tick += (_, _) =>
        {
            after.Stop();
            thump.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            thump.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        };
        after.Start();
        // It steps back behind the logo once the logo arrives.
        cone.BeginAnimation(UIElement.OpacityProperty, A(1, 0.4, 400, 700));
        FloatNotes(canvas, w, h, brand, 500, 8, cx, cy);
    }

    /// <summary>Music notes drifting up and out from a point, fading as they go.</summary>
    private static void FloatNotes(Canvas canvas, double w, double h, Color brand, int start, int count, double fromX, double fromY)
    {
        var rand = new Random();
        string[] glyphs = { "\u266A", "\u266B", "\u2669", "\u266C" };
        for (int i = 0; i < count; i++)
        {
            double size = 34 + rand.NextDouble() * 30;
            var note = new TextBlock { Text = glyphs[rand.Next(glyphs.Length)], FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = size, FontWeight = FontWeights.Bold,
                                       Foreground = i % 2 == 0 ? Brushes.White : new SolidColorBrush(Mix(brand, Colors.White, 0.25)), Opacity = 0 };
            var move = new TranslateTransform();
            var tilt = new RotateTransform();
            note.RenderTransform = new TransformGroup { Children = { tilt, move } };
            Canvas.SetLeft(note, fromX - size / 3);
            Canvas.SetTop(note, fromY - size / 2);
            canvas.Children.Add(note);
            int at = start + i * 90 + rand.Next(60);
            double dx = (rand.NextDouble() - 0.5) * w * 0.8, dy = -(h * 0.3 + rand.NextDouble() * h * 0.3);
            move.BeginAnimation(TranslateTransform.XProperty, A(0, dx, 1100, at, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            move.BeginAnimation(TranslateTransform.YProperty, A(0, dy, 1100, at, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            tilt.BeginAnimation(RotateTransform.AngleProperty, A(0, (rand.NextDouble() - 0.5) * 50, 1100, at));
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 150))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 1100))));
            note.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
