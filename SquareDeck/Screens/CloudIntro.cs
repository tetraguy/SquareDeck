using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The animations that play while a cloud gaming service opens. Each one lasts about two and a half seconds; the page
/// takes over as soon as both the animation and the page's first content are ready.
/// </summary>
public static class CloudIntro
{
    public static readonly (string Id, string Name, string About)[] Styles =
    {
        ("skyrise", "Above the clouds", "Fly up through a layer of clouds and break out into clear sky where the logo waits"),
        ("storm", "Thunder cloud", "A cloud gathers and darkens, and a lightning strike reveals the logo"),
        ("beam", "Beam up", "Glowing game pieces float up and stream into a cloud overhead"),
        ("random", "Surprise me", "A different one each time"),
        ("none", "None", "Just the logo"),
    };

    public const int Length = 2500;

    /// <summary>A soft cartoon cloud: overlapping circles on a flat base.</summary>
    public static Path Cloud(double width, Brush fill, double opacity = 1)
    {
        double h = width * 0.55;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(width * 0.28, h * 0.68), width * 0.2, h * 0.3));
        group.Children.Add(new EllipseGeometry(new Point(width * 0.48, h * 0.45), width * 0.25, h * 0.42));
        group.Children.Add(new EllipseGeometry(new Point(width * 0.7, h * 0.6), width * 0.2, h * 0.32));
        group.Children.Add(new RectangleGeometry(new Rect(width * 0.12, h * 0.62, width * 0.76, h * 0.36), h * 0.18, h * 0.18));
        return new Path { Data = group, Fill = fill, Opacity = opacity, Width = width, Height = h, Stretch = Stretch.None };
    }

    private static DoubleAnimation A(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };

    /// <summary>
    /// Builds the animation for a service. <paramref name="logo"/> is the service's tile; it's placed in the middle and
    /// animated in. Returns the layer to put behind the "how to leave" text.
    /// </summary>
    public static FrameworkElement Build(string style, CloudService service, FrameworkElement logo)
    {
        if (style == "random") style = Styles[Random.Shared.Next(3)].Id;
        var brand = service.Brand;
        var root = new Grid { ClipToBounds = true };
        var canvas = new Canvas { IsHitTestVisible = false };
        root.Children.Add(canvas);
        var middle = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 80) };
        middle.Children.Add(logo);
        root.Children.Add(middle);
        var scale = new ScaleTransform(1, 1);
        logo.RenderTransformOrigin = new Point(0.5, 0.5);
        logo.RenderTransform = scale;
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var pop = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };

        if (Theme.ReduceMotion || style == "none")
        {
            root.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x12));
            logo.Opacity = 0;
            logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 400));
            return root;
        }

        switch (style)
        {
            case "storm":
            {
                // Night sky; a cloud swells and darkens, flickers, a bolt strikes and the logo appears in the flash.
                root.Background = new LinearGradientBrush(Color.FromRgb(0x10, 0x12, 0x22), Color.FromRgb(0x05, 0x06, 0x0C), 90);
                var cloud = Cloud(560, new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x78)));
                var cloudScale = new ScaleTransform(0.6, 0.6);
                cloud.RenderTransformOrigin = new Point(0.5, 0.5);
                cloud.RenderTransform = cloudScale;
                cloud.HorizontalAlignment = HorizontalAlignment.Center;
                cloud.VerticalAlignment = VerticalAlignment.Top;
                cloud.Margin = new Thickness(0, 70, 0, 0);
                cloud.Opacity = 0;
                root.Children.Insert(1, cloud);
                cloud.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 600));
                cloudScale.BeginAnimation(ScaleTransform.ScaleXProperty, A(0.6, 1, 1100, 0, easeOut));
                cloudScale.BeginAnimation(ScaleTransform.ScaleYProperty, A(0.6, 1, 1100, 0, easeOut));
                var darken = new ColorAnimation(Color.FromRgb(0x5A, 0x60, 0x78), Color.FromRgb(0x2A, 0x2E, 0x40), TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(400) };
                cloud.Fill.BeginAnimation(SolidColorBrush.ColorProperty, darken);

                // Flickers inside the cloud, then the strike.
                var glow = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, v) in new[] { (0, 0.0), (900, 0.0), (960, 0.5), (1020, 0.0), (1200, 0.7), (1260, 0.0), (1500, 0.0) })
                    glow.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                cloud.Effect = new DropShadowEffect { Color = Color.FromRgb(0xC8, 0xD8, 0xFF), BlurRadius = 60, ShadowDepth = 0, Opacity = 0 };
                cloud.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, glow);

                var bolt = new Polyline
                {
                    Stroke = Brushes.White, StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round, Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 250, 0, 0),
                    Points = new PointCollection { new(30, 0), new(0, 70), new(28, 70), new(-6, 160), new(22, 160), new(-10, 250) },
                    Effect = new DropShadowEffect { Color = brand, BlurRadius = 30, ShadowDepth = 0, Opacity = 1 }
                };
                root.Children.Insert(2, bolt);
                var strike = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, v) in new[] { (0, 0.0), (1450, 0.0), (1480, 1.0), (1560, 0.3), (1620, 1.0), (1900, 0.0) })
                    strike.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                bolt.BeginAnimation(UIElement.OpacityProperty, strike);

                var flash = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
                root.Children.Add(flash);
                flash.BeginAnimation(UIElement.OpacityProperty, A(0.85, 0, 600, 1480));
                logo.Opacity = 0;
                logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 120, 1480));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, A(1.3, 1, 600, 1480, pop));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(1.3, 1, 600, 1480, pop));
                middle.Margin = new Thickness(0, 160, 0, 0);
                break;
            }
            case "beam":
            {
                // Dark floor; little glowing game pieces (squares, triangles, rings, crosses) float up in streams into a
                // cloud overhead, which lights up in the brand color; then the logo drops from the cloud.
                root.Background = new RadialGradientBrush(Color.FromRgb(0x18, 0x1A, 0x2A), Color.FromRgb(0x06, 0x07, 0x0C)) { Center = new Point(0.5, 0.2), GradientOrigin = new Point(0.5, 0.2), RadiusX = 0.9, RadiusY = 0.9 };
                var cloud = Cloud(420, new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF5)), 0.95);
                cloud.HorizontalAlignment = HorizontalAlignment.Center;
                cloud.VerticalAlignment = VerticalAlignment.Top;
                cloud.Margin = new Thickness(0, 40, 0, 0);
                cloud.Effect = new DropShadowEffect { Color = brand, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.4 };
                root.Children.Insert(1, cloud);
                cloud.Opacity = 0;
                cloud.BeginAnimation(UIElement.OpacityProperty, A(0, 0.95, 500));
                cloud.Effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, A(10, 70, 1800, 300));
                cloud.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, A(0.4, 1, 1800, 300));

                root.Loaded += (_, _) =>
                {
                    double w = root.ActualWidth, h = root.ActualHeight;
                    var random = new Random();
                    for (int i = 0; i < 42; i++)
                    {
                        Shape piece = (i % 4) switch
                        {
                            0 => new Rectangle { Width = 14, Height = 14, RadiusX = 3, RadiusY = 3 },
                            1 => new Ellipse { Width = 16, Height = 16, StrokeThickness = 3 },
                            2 => new Polygon { Points = new PointCollection { new(8, 0), new(16, 14), new(0, 14) } },
                            _ => new Path { Data = Geometry.Parse("M5,0 H11 V5 H16 V11 H11 V16 H5 V11 H0 V5 H5 Z") },
                        };
                        var color = i % 3 == 0 ? Colors.White : brand;
                        if (piece is Ellipse) piece.Stroke = new SolidColorBrush(color); else piece.Fill = new SolidColorBrush(color);
                        piece.Effect = new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
                        double lane = w / 2 + (random.NextDouble() - 0.5) * w * 0.7;
                        Canvas.SetLeft(piece, lane);
                        Canvas.SetTop(piece, 0);
                        var move = new TranslateTransform(0, h + 20);
                        var spin = new RotateTransform(random.Next(360), 8, 8);
                        piece.RenderTransform = new TransformGroup { Children = { spin, move } };
                        canvas.Children.Add(piece);
                        int delay = random.Next(1500), ms = 900 + random.Next(500);
                        // Up and in toward the middle of the cloud, shrinking a little.
                        move.BeginAnimation(TranslateTransform.YProperty, A(h + 20, 120, ms, delay, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                        move.BeginAnimation(TranslateTransform.XProperty, A(0, (w / 2 - lane) * 0.85, ms, delay, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                        spin.BeginAnimation(RotateTransform.AngleProperty, A(spin.Angle, spin.Angle + 360, ms, delay));
                        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay) };
                        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
                        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms - 150))));
                        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                        piece.Opacity = 0;
                        piece.BeginAnimation(UIElement.OpacityProperty, fade);
                    }
                };
                logo.Opacity = 0;
                var drop = new TranslateTransform(0, -160);
                middle.RenderTransform = drop;
                middle.Margin = new Thickness(0, 140, 0, 0);
                logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 300, 1700));
                drop.BeginAnimation(TranslateTransform.YProperty, A(-160, 0, 650, 1700, new BounceEase { Bounces = 2, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
                break;
            }
            default: // skyrise
            {
                // Blue sky; layers of clouds rush downward past the camera (we're flying up), thin out, and the logo
                // rises into the clear sky with a sun glow behind it.
                var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                var top = new GradientStop(Color.FromRgb(0x1C, 0x5C, 0xC8), 0);
                var bottom = new GradientStop(Color.FromRgb(0x8C, 0xC4, 0xF4), 1);
                sky.GradientStops.Add(top); sky.GradientStops.Add(bottom);
                root.Background = sky;
                top.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromRgb(0x5A, 0x6A, 0x80), Color.FromRgb(0x1C, 0x5C, 0xC8), TimeSpan.FromMilliseconds(1600)));
                bottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromRgb(0xC8, 0xCE, 0xD8), Color.FromRgb(0x8C, 0xC4, 0xF4), TimeSpan.FromMilliseconds(1600)));

                var sun = new Ellipse
                {
                    Width = 520, Height = 520, IsHitTestVisible = false, Opacity = 0, Margin = new Thickness(0, 0, 0, 80),
                    Fill = new RadialGradientBrush(Color.FromArgb(0xC0, 0xFF, 0xF4, 0xD0), Color.FromArgb(0, 0xFF, 0xF4, 0xD0))
                };
                root.Children.Insert(1, sun);
                sun.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 900, 1300));

                root.Loaded += (_, _) =>
                {
                    double w = root.ActualWidth, h = root.ActualHeight;
                    var random = new Random();
                    for (int i = 0; i < 26; i++)
                    {
                        double size = 260 + random.NextDouble() * 520;
                        var cloud = Cloud(size, Brushes.White, 0.75 + random.NextDouble() * 0.25);
                        Canvas.SetLeft(cloud, random.NextDouble() * w - size / 2);
                        var move = new TranslateTransform();
                        cloud.RenderTransform = move;
                        canvas.Children.Add(cloud);
                        // Early clouds cover the screen; each one sweeps downward and out of view.
                        int delay = (int)(i * 55 + random.NextDouble() * 120), ms = 900 + random.Next(500);
                        double startY = -size * 0.5 + random.NextDouble() * h;
                        move.BeginAnimation(TranslateTransform.YProperty, A(startY, h + size, ms, delay, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                    }
                };
                logo.Opacity = 0;
                var rise = new TranslateTransform(0, 120);
                middle.RenderTransform = rise;
                logo.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 500, 1400));
                rise.BeginAnimation(TranslateTransform.YProperty, A(120, 0, 900, 1400, easeOut));
                break;
            }
        }
        return root;
    }
}
