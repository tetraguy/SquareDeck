using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Ambient decoration for UI styles that have one (<see cref="UiStyle.Ambient"/>): a few gently animated shapes
/// behind the home screen, such as blinking pixel stars, a radar sweep, drifting leaves or floating glass shapes.
/// They stay still with Reduce motion or Performance mode, and stop drawing while a game is in front.
/// </summary>
public partial class MainWindow
{
    private Canvas? _ambient;
    private DispatcherTimer? _ambientTimer;

    private static DoubleAnimation Loop(double from, double to, double seconds, double delay = 0, bool reverse = true, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(delay), AutoReverse = reverse, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease ?? new SineEase { EasingMode = EasingMode.EaseInOut }
        };

    /// <summary>Builds (or clears) the ambient layer for the current UI style.</summary>
    private void ApplyAmbient(UiStyle s)
    {
        _ambientTimer?.Stop();
        _ambientTimer = null;
        if (_ambient == null)
        {
            _ambient = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Width = 1280, Height = 720 };
            Grid.SetRowSpan(_ambient, 7);
            // Behind everything on the home screen, in front of the wallpaper and artwork.
            int at = RootGrid.Children.IndexOf(HeroScrim);
            RootGrid.Children.Insert(at >= 0 ? at + 1 : 0, _ambient);
        }
        _ambient.Children.Clear();
        _ambient.Visibility = s.Ambient.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (s.Ambient.Length == 0) return;
        bool still = Theme.ReduceMotion || Perf.Active;
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        var random = new Random(s.Id.GetHashCode());

        void Add(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            _ambient.Children.Add(e);
        }

        switch (s.Ambient)
        {
            case "pixelstars":
                // Square "pixel" stars that blink on and off at their own pace.
                for (int i = 0; i < 26; i++)
                {
                    double size = random.Next(2) == 0 ? 4 : 8;
                    var star = new Rectangle { Width = size, Height = size, Fill = new SolidColorBrush(i % 5 == 0 ? accent : Colors.White) };
                    Add(star, random.Next(1280), random.Next(260));
                    if (!still)
                    {
                        var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 3) };
                        double period = 1.2 + random.NextDouble() * 2.4;
                        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period * 0.7))));
                        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period))));
                        star.BeginAnimation(OpacityProperty, blink);
                    }
                }
                break;

            case "sparkle":
                // Four-point sparkles that twinkle and turn.
                for (int i = 0; i < 14; i++)
                {
                    double size = 10 + random.NextDouble() * 18;
                    var sparkle = new Path
                    {
                        Data = Geometry.Parse("M 5,0 C 5.6,3.8 6.2,4.4 10,5 C 6.2,5.6 5.6,6.2 5,10 C 4.4,6.2 3.8,5.6 0,5 C 3.8,4.4 4.4,3.8 5,0 Z"),
                        Fill = Brushes.White, Width = size, Height = size, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5),
                        Effect = still ? null : new DropShadowEffect { Color = accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 }
                    };
                    var scale = new ScaleTransform(1, 1);
                    var turn = new RotateTransform();
                    sparkle.RenderTransform = new TransformGroup { Children = { scale, turn } };
                    Add(sparkle, random.Next(1260), random.Next(700));
                    if (!still)
                    {
                        double t = 1.6 + random.NextDouble() * 2, d = random.NextDouble() * 3;
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(0.2, 1, t, d));
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(0.2, 1, t, d));
                        sparkle.BeginAnimation(OpacityProperty, Loop(0.1, 0.95, t, d));
                        turn.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 90, t * 2, d, reverse: false, ease: new PowerEase()));
                    }
                }
                break;

            case "status":
                // Status lights along the top-right edge, each pulsing in its own rhythm, and a slow scanning line.
                for (int i = 0; i < 6; i++)
                {
                    var c = i == 4 ? Color.FromRgb(0xFF, 0xB0, 0x3A) : accent;
                    var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(c) };
                    Add(dot, 1140 + i * 18, 692);
                    if (!still) dot.BeginAnimation(OpacityProperty, Loop(0.25, 1, 0.6 + i * 0.27));
                }
                {
                    var scan = new Rectangle { Width = 1280, Height = 2, Fill = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)) };
                    var move = new TranslateTransform();
                    scan.RenderTransform = move;
                    Add(scan, 0, 0);
                    if (!still) move.BeginAnimation(TranslateTransform.YProperty, Loop(0, 720, 7, reverse: false, ease: new PowerEase { Power = 1 }));
                }
                break;

            case "bokeh":
            case "float":
            {
                // Soft round lights (cozy) or iridescent glass shapes (holographic) drifting slowly.
                bool holo = s.Ambient == "float";
                var colors = holo
                    ? new[] { Color.FromRgb(0x7F, 0xD6, 0xFF), Color.FromRgb(0xB9, 0x8C, 0xFF), Color.FromRgb(0xFF, 0x9A, 0xD5), Color.FromRgb(0xFF, 0xE3, 0xA3) }
                    : new[] { Color.FromRgb(0xFF, 0xC8, 0x8A), Color.FromRgb(0xF1, 0x9A, 0x7F), Color.FromRgb(0xFF, 0xE6, 0xB3) };
                for (int i = 0; i < (holo ? 9 : 12); i++)
                {
                    var c = colors[i % colors.Length];
                    double size = holo ? 80 + random.NextDouble() * 160 : 40 + random.NextDouble() * 90;
                    Shape shape = holo && i % 3 == 1
                        ? new Rectangle { Width = size, Height = size, RadiusX = size * 0.3, RadiusY = size * 0.3 }
                        : new Ellipse { Width = size, Height = size };
                    shape.Fill = new RadialGradientBrush(Color.FromArgb(holo ? (byte)0x60 : (byte)0x50, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                    if (holo)
                    {
                        shape.Stroke = new LinearGradientBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 45);
                        shape.StrokeThickness = 1.5;
                    }
                    var drift = new TranslateTransform();
                    var spin = new RotateTransform(random.Next(360), size / 2, size / 2);
                    shape.RenderTransform = new TransformGroup { Children = { spin, drift } };
                    Add(shape, random.Next(-60, 1220), random.Next(-40, 640));
                    if (!still)
                    {
                        double t = 9 + random.NextDouble() * 9;
                        drift.BeginAnimation(TranslateTransform.XProperty, Loop(0, random.Next(-120, 120), t, random.NextDouble() * 4));
                        drift.BeginAnimation(TranslateTransform.YProperty, Loop(0, random.Next(-80, 80), t * 0.8, random.NextDouble() * 4));
                        shape.BeginAnimation(OpacityProperty, Loop(0.5, 1, t / 2, random.NextDouble() * 3));
                        if (holo) spin.BeginAnimation(RotateTransform.AngleProperty, Loop(spin.Angle, spin.Angle + 180, t * 2, reverse: false, ease: new PowerEase { Power = 1 }));
                    }
                }
                break;
            }

            case "compass":
            {
                // An inked compass rose in the corner, its needle swaying gently, like a map in a storybook.
                var ink = new SolidColorBrush(Color.FromArgb(0x55, 0x3B, 0x2A, 0x1A));
                var rose = new Canvas { Width = 140, Height = 140 };
                rose.Children.Add(new Ellipse { Width = 140, Height = 140, Stroke = ink, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 2, 3 } });
                rose.Children.Add(new Ellipse { Width = 100, Height = 100, Stroke = ink, StrokeThickness = 1.5, Margin = new Thickness(20) });
                var star = new Path { Data = Geometry.Parse("M70,6 L80,60 L134,70 L80,80 L70,134 L60,80 L6,70 L60,60 Z"), Stroke = ink, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x18, 0x3B, 0x2A, 0x1A)) };
                rose.Children.Add(star);
                var needle = new Path { Data = Geometry.Parse("M70,22 L76,70 L70,118 L64,70 Z"), Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xB4, 0x53, 0x2A)), RenderTransform = new RotateTransform(0, 70, 70) };
                rose.Children.Add(needle);
                var n = new TextBlock { Text = "N", FontFamily = new FontFamily("Ink Free, Segoe Print"), FontSize = 18, Foreground = ink };
                Canvas.SetLeft(n, 64); Canvas.SetTop(n, -24);
                rose.Children.Add(n);
                Add(rose, 1100, 60);
                if (!still) ((RotateTransform)needle.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, Loop(-14, 14, 3.5));
                break;
            }

            case "glitch":
                // Every few seconds, a few horizontal slices flash in cyan and pink, like a bad signal.
                if (!still)
                {
                    _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
                    _ambientTimer.Tick += (_, _) =>
                    {
                        if (_ambient == null || !_ambient.IsVisible) return;
                        for (int i = 0; i < 3; i++)
                        {
                            var slice = new Rectangle
                            {
                                Width = 200 + random.Next(700), Height = 2 + random.Next(14),
                                Fill = new SolidColorBrush(i % 2 == 0 ? Color.FromArgb(0x60, 0x00, 0xE5, 0xFF) : Color.FromArgb(0x60, 0xFF, 0x2B, 0xD6))
                            };
                            Add(slice, random.Next(-100, 1000), random.Next(720));
                            var flick = new DoubleAnimationUsingKeyFrames();
                            foreach (var (ms, v) in new[] { (0, 1.0), (60, 0.2), (110, 0.9), (180, 0.0) })
                                flick.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                            flick.Completed += (_, _) => _ambient.Children.Remove(slice);
                            slice.BeginAnimation(OpacityProperty, flick);
                        }
                    };
                    _ambientTimer.Start();
                }
                break;

            case "confetti":
                // Chunky plastic shapes in primary colors bobbing and spinning around the edges.
                {
                    var colors = new[] { Color.FromRgb(0xFF, 0x3B, 0x6B), Color.FromRgb(0x3B, 0xC7, 0xFF), Color.FromRgb(0x2E, 0xD5, 0x73), Color.FromRgb(0x8E, 0x5B, 0xFF) };
                    for (int i = 0; i < 14; i++)
                    {
                        double size = 22 + random.Next(20);
                        Shape shape = (i % 4) switch
                        {
                            0 => new Ellipse { Width = size, Height = size },
                            1 => new Rectangle { Width = size, Height = size, RadiusX = 5, RadiusY = 5 },
                            2 => new Polygon { Points = new PointCollection { new(size / 2, 0), new(size, size), new(0, size) } },
                            _ => new Path { Data = Geometry.Parse("M0,8 Q8,0 16,8 T32,8"), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
                        };
                        var c = new SolidColorBrush(colors[i % colors.Length]);
                        if (shape is Path) shape.Stroke = c; else { shape.Fill = c; shape.Stroke = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)); shape.StrokeThickness = 3; }
                        var bob = new TranslateTransform();
                        var turn = new RotateTransform(random.Next(360), size / 2, size / 2);
                        shape.RenderTransform = new TransformGroup { Children = { turn, bob } };
                        // Keep to the edges, away from the games in the middle.
                        double x = i % 2 == 0 ? random.Next(10, 180) : random.Next(1100, 1250), y = random.Next(20, 690);
                        Add(shape, x, y);
                        if (!still)
                        {
                            bob.BeginAnimation(TranslateTransform.YProperty, Loop(0, -16, 0.9 + random.NextDouble(), random.NextDouble(), ease: new BounceEase { Bounces = 1, Bounciness = 2 }));
                            turn.BeginAnimation(RotateTransform.AngleProperty, Loop(turn.Angle - 20, turn.Angle + 20, 1.6 + random.NextDouble()));
                        }
                    }
                }
                break;

            case "marquee":
                // Bulbs along the top and bottom edges, chasing like a cabinet's marquee.
                for (int i = 0; i < 64; i++)
                {
                    foreach (double y in new[] { 6.0, 704.0 })
                    {
                        var bulb = new Ellipse
                        {
                            Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x6A)),
                            Effect = still ? null : new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xC7, 0x00), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 }
                        };
                        Add(bulb, 5 + i * 20, y);
                        if (!still)
                        {
                            var chase = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                            int phase = i % 3;
                            for (int k = 0; k < 3; k++)
                                chase.KeyFrames.Add(new DiscreteDoubleKeyFrame(k == phase ? 1 : 0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(k * 220))));
                            chase.KeyFrames.Add(new DiscreteDoubleKeyFrame(phase == 0 ? 1 : 0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(660))));
                            bulb.BeginAnimation(OpacityProperty, chase);
                        }
                        else bulb.Opacity = i % 3 == 0 ? 1 : 0.4;
                    }
                }
                break;

            case "leaves":
                // Leaves drifting down and swaying across the screen.
                for (int i = 0; i < 10; i++)
                {
                    var leaf = new Path
                    {
                        Data = Geometry.Parse("M0,10 C4,0 16,0 20,10 C16,20 4,20 0,10 Z M0,10 L20,10"),
                        Fill = new SolidColorBrush(i % 3 == 0 ? Color.FromArgb(0x90, 0xC8, 0x8A, 0x2E) : Color.FromArgb(0x90, 0x6F, 0x8F, 0x3A)),
                        Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x2F, 0x3A, 0x24)), StrokeThickness = 1,
                        Width = 28, Height = 28, Stretch = Stretch.Uniform
                    };
                    var fall = new TranslateTransform();
                    var sway = new RotateTransform(0, 14, 14);
                    leaf.RenderTransform = new TransformGroup { Children = { sway, fall } };
                    Add(leaf, random.Next(1280), -40);
                    if (!still)
                    {
                        double t = 12 + random.NextDouble() * 10, d = random.NextDouble() * 12;
                        fall.BeginAnimation(TranslateTransform.YProperty, Loop(-40, 780, t, d, reverse: false, ease: new PowerEase { Power = 1 }));
                        fall.BeginAnimation(TranslateTransform.XProperty, Loop(-40, 40, 2.4 + random.NextDouble(), d));
                        sway.BeginAnimation(RotateTransform.AngleProperty, Loop(-35, 35, 2 + random.NextDouble(), d));
                    }
                    else Canvas.SetTop(leaf, random.Next(700));
                }
                break;

            case "radar":
            {
                // A radar scope in the corner: rings, a sweeping beam and blips that light up as it passes.
                var scope = new Canvas { Width = 180, Height = 180, Opacity = 0.75 };
                var line = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B));
                for (int r = 30; r <= 90; r += 30)
                {
                    var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = line, StrokeThickness = 1 };
                    Canvas.SetLeft(ring, 90 - r); Canvas.SetTop(ring, 90 - r);
                    scope.Children.Add(ring);
                }
                scope.Children.Add(new Line { X1 = 0, Y1 = 90, X2 = 180, Y2 = 90, Stroke = line });
                scope.Children.Add(new Line { X1 = 90, Y1 = 0, X2 = 90, Y2 = 180, Stroke = line });
                var beam = new Path
                {
                    Data = Geometry.Parse("M90,90 L90,0 A90,90 0 0 1 153.6,26.4 Z"),
                    Fill = new LinearGradientBrush(Color.FromArgb(0x00, accent.R, accent.G, accent.B), Color.FromArgb(0x80, accent.R, accent.G, accent.B), 0),
                    RenderTransform = new RotateTransform(0, 90, 90)
                };
                scope.Children.Add(beam);
                for (int i = 0; i < 4; i++)
                {
                    double a = random.NextDouble() * Math.PI * 2, r = 25 + random.NextDouble() * 60;
                    var blip = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(accent) };
                    Canvas.SetLeft(blip, 90 + Math.Cos(a) * r - 3); Canvas.SetTop(blip, 90 + Math.Sin(a) * r - 3);
                    scope.Children.Add(blip);
                    if (!still)
                    {
                        // Lights up as the beam passes (the beam turns once every 4 seconds).
                        double passAt = ((a * 180 / Math.PI + 90) % 360) / 360 * 4;
                        var flash = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4) };
                        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(passAt))));
                        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Min(3.99, passAt + 1.5)))));
                        blip.BeginAnimation(OpacityProperty, flash);
                    }
                }
                Add(scope, 1080, 470);
                if (!still) ((RotateTransform)beam.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 4, reverse: false, ease: new PowerEase { Power = 1 }));
                break;
            }

            case "vhs":
            {
                // A worn tape: a soft tracking band rolling down the screen, and the deck's "PLAY" with a running counter.
                var band = new Rectangle
                {
                    Width = 1280, Height = 46,
                    Fill = new LinearGradientBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 90) { SpreadMethod = GradientSpreadMethod.Reflect, EndPoint = new Point(0, 0.5) }
                };
                var roll = new TranslateTransform();
                band.RenderTransform = roll;
                Add(band, 0, -60);
                if (!still) roll.BeginAnimation(TranslateTransform.YProperty, Loop(0, 840, 9, reverse: false, ease: new PowerEase { Power = 1 }));
                var osd = new TextBlock
                {
                    Text = "PLAY \u25B6", FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 22, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, Opacity = 0.75, Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 2, Opacity = 0.6 }
                };
                Add(osd, 1110, 646);
                var counter = new TextBlock { FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 18, Foreground = Brushes.White, Opacity = 0.6 };
                Add(counter, 1110, 676);
                var started = DateTime.Now;
                void Tick() => counter.Text = "SP  " + (DateTime.Now - started).ToString(@"h\:mm\:ss");
                Tick();
                if (!still)
                {
                    var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                    blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
                    blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8))));
                    osd.BeginAnimation(OpacityProperty, blink);
                    _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _ambientTimer.Tick += (_, _) => { if (_ambient?.IsVisible == true) Tick(); };
                    _ambientTimer.Start();
                }
                break;
            }

            case "horizon":
            {
                // The neon floor: lines rushing toward you over a grid that runs to a vanishing point on the horizon.
                const double horizon = 470, cx = 640;
                var pen = new SolidColorBrush(Color.FromArgb(0x70, accent.R, accent.G, accent.B));
                var glow = new LinearGradientBrush(Color.FromArgb(0, accent.R, accent.G, accent.B), Color.FromArgb(0x40, accent.R, accent.G, accent.B), 90);
                Add(new Rectangle { Width = 1280, Height = 720 - horizon, Fill = glow }, 0, horizon);
                for (int i = -12; i <= 12; i++)
                    _ambient.Children.Add(new Line { X1 = cx + i * 18, Y1 = horizon, X2 = cx + i * 200, Y2 = 720, Stroke = pen, StrokeThickness = 1.2 });
                _ambient.Children.Add(new Line { X1 = 0, Y1 = horizon, X2 = 1280, Y2 = horizon, Stroke = new SolidColorBrush(accent), StrokeThickness = 2, Opacity = 0.8 });
                for (int i = 0; i < 8; i++)
                {
                    var line = new Line { X1 = 0, X2 = 1280, Y1 = 0, Y2 = 0, Stroke = pen, StrokeThickness = 1.4 };
                    var move = new TranslateTransform(0, horizon);
                    line.RenderTransform = move;
                    _ambient.Children.Add(line);
                    if (!still)
                        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(horizon, 720, TimeSpan.FromSeconds(4))
                        {
                            BeginTime = TimeSpan.FromSeconds(i * 0.5), RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseIn }
                        });
                    else move.Y = horizon + Math.Pow(i / 8.0, 2.5) * (720 - horizon);
                }
                break;
            }

            case "stickers":
            {
                // Stickers stuck around the edges at odd angles, with washi tape, wobbling a little now and then.
                string[] faces = { "★", "♥", "GG!", "☺", "1UP", "✦", "WIN", "♪" };
                var colors = new[] { Color.FromRgb(0xFF, 0x5C, 0x8A), Color.FromRgb(0xFF, 0xB4, 0x43), Color.FromRgb(0x5C, 0xC8, 0xFF), Color.FromRgb(0x7E, 0xD9, 0x57), Color.FromRgb(0xB0, 0x8C, 0xFF) };
                var spots = new[] { (30.0, 120.0), (60.0, 520.0), (1150.0, 110.0), (1170.0, 560.0), (520.0, 660.0), (860.0, 40.0), (300.0, 30.0), (1010.0, 650.0) };
                for (int i = 0; i < faces.Length; i++)
                {
                    var c = colors[i % colors.Length];
                    var sticker = new Border
                    {
                        Width = 74, Height = 74, CornerRadius = new CornerRadius(i % 2 == 0 ? 37 : 14), Background = new SolidColorBrush(c),
                        BorderBrush = Brushes.White, BorderThickness = new Thickness(5), RenderTransformOrigin = new Point(0.5, 0.5),
                        Effect = still ? null : new DropShadowEffect { BlurRadius = 0, ShadowDepth = 3, Opacity = 0.35 },
                        Child = new TextBlock
                        {
                            Text = faces[i], FontFamily = new FontFamily("Segoe Print, Ink Free, Segoe UI Symbol"), FontSize = faces[i].Length > 1 ? 18 : 30,
                            FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    var tilt = new RotateTransform(random.Next(-25, 25));
                    sticker.RenderTransform = tilt;
                    Add(sticker, spots[i].Item1, spots[i].Item2);
                    if (i % 3 == 0)
                    {
                        var tape = new Rectangle { Width = 54, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xF1, 0xB0)), RenderTransform = new RotateTransform(-30 + random.Next(20)) };
                        Add(tape, spots[i].Item1 - 10, spots[i].Item2 - 4);
                    }
                    if (!still)
                    {
                        var wobble = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 6) };
                        double baseAngle = tilt.Angle;
                        foreach (var (sec, delta) in new[] { (0.0, 0.0), (5.0, 0.0), (5.15, 8.0), (5.3, -6.0), (5.45, 3.0), (5.6, 0.0), (8.0, 0.0) })
                            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(baseAngle + delta, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(sec))));
                        tilt.BeginAnimation(RotateTransform.AngleProperty, wobble);
                    }
                }
                break;
            }
        }
    }

    /// <summary>The ambient layer doesn't draw while a game is in front.</summary>
    private void PauseAmbient(bool paused)
    {
        if (_ambient == null || UiStyles.Current.Ambient.Length == 0) return;
        _ambient.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
    }
}
