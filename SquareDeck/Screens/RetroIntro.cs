using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Retro Lounge's opening, a console booting on an old TV:
///   1. the tube switches on: a bright line, a burst of static, then the picture opens out;
///   2. a synthwave night: a striped sunset sun, pixel stars and a grid floor rolling toward you;
///   3. the logo drops in from the top as a handheld's does at boot, lands with a flash and a two-note chime;
///   4. a blinking pixel status line and a bar of lit blocks while the ROM folders are looked through.
/// It switches off into the page (the picture squeezes to a line and a dot) once the games are found. With reduced
/// motion it simply appears and goes.
/// </summary>
public sealed class RetroIntro : Grid
{
    private const int Blocks = 24;

    private readonly TextBlock _status, _detail, _caret;
    private readonly Border[] _blocks = new Border[Blocks];
    private readonly Grid _screen;
    private readonly ScaleTransform _tube = new(1, 1);
    private readonly Image _static;
    private readonly StackPanel _logo;
    private readonly TranslateTransform _drop = new(0, 0);
    private readonly Border _flash;
    private readonly Canvas _stars;
    private readonly FontFamily _pixel = AppFonts.Family("file:VT323-Regular.ttf#VT323");
    private double _fraction;
    private int _lit;

    private static readonly Color Night = Color.FromRgb(0x0B, 0x07, 0x12);
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x3D, 0x8B);
    private static readonly Color Violet = Color.FromRgb(0xB4, 0x3C, 0xFF);

    /// <summary>How long the opening needs to play through before it may switch off.</summary>
    public static TimeSpan Length => Theme.ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(2700);

    public RetroIntro()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Black;
        ClipToBounds = true;

        // ---- the picture inside the tube
        _screen = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _tube, ClipToBounds = true };
        _screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58, GridUnitType.Star) });
        _screen.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42, GridUnitType.Star) });

        // Sky: deep violet into a warm horizon.
        var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sky.GradientStops.Add(new GradientStop(Night, 0));
        sky.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x0E, 0x3C), 0.55));
        sky.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x16, 0x52), 0.9));
        sky.GradientStops.Add(new GradientStop(Color.FromRgb(0x8A, 0x22, 0x5A), 1));
        var skyBox = new Border { Background = sky };
        Grid.SetRowSpan(skyBox, 2);
        _screen.Children.Add(skyBox);

        _stars = new Canvas { IsHitTestVisible = false };
        Grid.SetRow(_stars, 0);
        _screen.Children.Add(_stars);

        // The sun sits on the horizon, cut by widening dark bands toward its foot.
        var sun = new Grid { Width = 360, Height = 180, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true, Opacity = 0.92 };
        var disc = new Ellipse { Width = 360, Height = 360, VerticalAlignment = VerticalAlignment.Top, Fill = Sunset() };
        disc.OpacityMask = SunBands();
        disc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Pink, BlurRadius = 60, ShadowDepth = 0, Opacity = 0.8 };
        sun.Children.Add(disc);
        Grid.SetRow(sun, 0);
        _screen.Children.Add(sun);

        // The floor: a perspective grid that rolls toward you.
        var floor = new Grid { ClipToBounds = true };
        var ground = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        ground.GradientStops.Add(new GradientStop(Color.FromRgb(0x1C, 0x06, 0x2A), 0));
        ground.GradientStops.Add(new GradientStop(Night, 1));
        floor.Children.Add(new Border { Background = ground });
        floor.Children.Add(GridFloor());
        floor.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0xC8)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Pink, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 } });
        Grid.SetRow(floor, 1);
        _screen.Children.Add(floor);

        // ---- the logo, the status line and the blocks
        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRowSpan(middle, 2);
        _logo = new StackPanel { RenderTransform = _drop };
        string logoFile = System.IO.Path.Combine(AppContext.BaseDirectory, "RetroLounge", "logo-512.png");
        if (File.Exists(logoFile) && ImageLoader.FromFile(logoFile, 320) is { } logo)
        {
            var picture = new Image { Source = logo, Width = 190, Height = 190, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            _logo.Children.Add(picture);
        }
        var name = new TextBlock
        {
            Text = "RETRO LOUNGE", FontFamily = _pixel, FontSize = 72, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0), Foreground = Sunset()
        };
        name.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Violet, BlurRadius = 0, ShadowDepth = 5, Direction = 315, Opacity = 0.9 };
        _logo.Children.Add(name);
        middle.Children.Add(_logo);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        _status = new TextBlock { FontFamily = _pixel, FontSize = 30, Foreground = Brushes.White };
        _caret = new TextBlock { FontFamily = _pixel, FontSize = 30, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE4, 0x5C)), Text = "_", Margin = new Thickness(4, 0, 0, 0) };
        line.Children.Add(_status);
        line.Children.Add(_caret);
        middle.Children.Add(line);
        _detail = new TextBlock { FontFamily = _pixel, FontSize = 22, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        middle.Children.Add(_detail);

        var bar = new Border
        {
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0))
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var colors = Sunset(vertical: false);
        for (int i = 0; i < Blocks; i++)
        {
            var color = ColorAt(colors, i / (double)(Blocks - 1));
            _blocks[i] = new Border { Width = 12, Height = 16, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0), Background = new SolidColorBrush(color), Opacity = 0.12 };
            row.Children.Add(_blocks[i]);
        }
        bar.Child = row;
        middle.Children.Add(bar);
        _screen.Children.Add(middle);

        // Static: shown for a moment as the tube warms up.
        _static = new Image { Stretch = Stretch.Fill, Opacity = 0, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_static, BitmapScalingMode.NearestNeighbor);
        Grid.SetRowSpan(_static, 2);
        _screen.Children.Add(_static);

        _flash = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        Grid.SetRowSpan(_flash, 2);
        _screen.Children.Add(_flash);
        Children.Add(_screen);

        // Scanlines over everything, and a dark edge like curved glass.
        Children.Add(new Rectangle { Fill = Scanlines(), IsHitTestVisible = false, Opacity = 0.6 });
        var edge = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.75 };
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.62));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 1));
        Children.Add(new Rectangle { Fill = edge, IsHitTestVisible = false });

        Loaded += (_, _) => Boot();
        Unloaded += (_, _) => _ticker?.Stop();
    }

    // ================================================================ shared looks (used across Retro Lounge)

    public static LinearGradientBrush Sunset(bool vertical = true)
    {
        var b = vertical ? new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) } : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xE4, 0x5C), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x9A, 0x3D), 0.35));
        b.GradientStops.Add(new GradientStop(Pink, 0.7));
        b.GradientStops.Add(new GradientStop(Violet, 1));
        b.Freeze();
        return b;
    }

    /// <summary>Thin dark lines across, every few pixels.</summary>
    public static Brush Scanlines()
    {
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 1.5)));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    // ================================================================ the scene's parts

    private static Color ColorAt(LinearGradientBrush brush, double t)
    {
        var stops = brush.GradientStops.OrderBy(s => s.Offset).ToList();
        for (int i = 1; i < stops.Count; i++)
        {
            if (t > stops[i].Offset) continue;
            var a = stops[i - 1]; var b = stops[i];
            double f = (t - a.Offset) / Math.Max(0.0001, b.Offset - a.Offset);
            byte Mix(byte x, byte y) => (byte)(x + (y - x) * f);
            return Color.FromRgb(Mix(a.Color.R, b.Color.R), Mix(a.Color.G, b.Color.G), Mix(a.Color.B, b.Color.B));
        }
        return stops[^1].Color;
    }

    /// <summary>The sun's bands: solid at the top, then gaps that widen toward the horizon.</summary>
    private static Brush SunBands()
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        // Bands only on the visible (upper) half of the disc: 0.28 to 0.5.
        double y = 0.27;
        for (int i = 0; i < 6; i++)
        {
            double gap = 0.004 + i * 0.0035;
            mask.GradientStops.Add(new GradientStop(Colors.Black, y));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, y));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, y + gap));
            mask.GradientStops.Add(new GradientStop(Colors.Black, y + gap));
            y += 0.036;
        }
        mask.Freeze();
        return mask;
    }

    private readonly List<Line> _crossLines = new();
    private Size _floorSize;
    private double _phase;

    /// <summary>The floor's lines: rays fanning out from the vanishing point, and crosswise lines that roll toward you.</summary>
    private UIElement GridFloor()
    {
        var host = new Grid { IsHitTestVisible = false };
        var pen = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x4F, 0xC0));
        pen.Freeze();
        var rays = new Canvas();
        var crossing = new Canvas();
        for (int i = 0; i < 28; i++)
        {
            var line = new Line { Stroke = pen };
            _crossLines.Add(line);
            crossing.Children.Add(line);
        }
        host.SizeChanged += (_, e) =>
        {
            rays.Children.Clear();
            _floorSize = e.NewSize;
            double w = e.NewSize.Width, h = e.NewSize.Height;
            if (w < 1 || h < 1) return;
            for (int i = -12; i <= 12; i++)
                rays.Children.Add(new Line { X1 = w / 2 + i * 22, Y1 = 0, X2 = w / 2 + i * w / 7, Y2 = h, Stroke = pen, StrokeThickness = 1.5 });
            PlaceCrossLines();
        };
        // Fade the lines toward the horizon so the bunching reads as distance.
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x20, 0, 0, 0), 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.6));
        host.OpacityMask = fade;
        host.Children.Add(crossing);
        host.Children.Add(rays);
        return host;
    }

    /// <summary>Each crosswise line at its distance (plus the rolling phase): bunched near the horizon, wide up close.</summary>
    private void PlaceCrossLines()
    {
        double w = _floorSize.Width, h = _floorSize.Height;
        if (w < 1 || h < 1) return;
        for (int i = 0; i < _crossLines.Count; i++)
        {
            double z = i + _phase;
            double y = h * (1 - 1 / (1 + z * z * 0.06));
            var line = _crossLines[i];
            line.X1 = 0; line.X2 = w; line.Y1 = y; line.Y2 = y;
            line.StrokeThickness = 0.8 + z * 0.08;
        }
    }

    private readonly Random _random = new();

    private void ScatterStars()
    {
        _stars.Children.Clear();
        double w = Math.Max(1, _stars.ActualWidth), h = Math.Max(1, _stars.ActualHeight);
        for (int i = 0; i < 70; i++)
        {
            double size = _random.Next(3) == 0 ? 4 : 2;
            var star = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.3 + _random.NextDouble() * 0.6 };
            Canvas.SetLeft(star, Math.Round(_random.NextDouble() * w));
            Canvas.SetTop(star, Math.Round(_random.NextDouble() * h * 0.8));
            _stars.Children.Add(star);
            if (!Theme.ReduceMotion && i % 3 == 0)
                star.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(500 + _random.Next(900)))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(_random.Next(800)) });
        }
    }

    /// <summary>A frame of TV static, small and scaled up so the specks are chunky.</summary>
    private BitmapSource Noise()
    {
        const int w = 160, h = 90;
        var pixels = new byte[w * h];
        _random.NextBytes(pixels);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w);
        bitmap.Freeze();
        return bitmap;
    }

    // ================================================================ playing it

    private System.Windows.Threading.DispatcherTimer? _ticker;

    private async void Boot()
    {
        ScatterStars();
        if (Theme.ReduceMotion) { _caret.Text = ""; return; }

        // 1. The tube: a line across, static as it opens, then the picture.
        var open = new DoubleAnimationUsingKeyFrames();
        open.KeyFrames.Add(new LinearDoubleKeyFrame(0.006, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        open.KeyFrames.Add(new LinearDoubleKeyFrame(0.006, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), new SineEase()));
        var wide = new DoubleAnimationUsingKeyFrames();
        wide.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        wide.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        _tube.BeginAnimation(ScaleTransform.ScaleYProperty, open);
        _tube.BeginAnimation(ScaleTransform.ScaleXProperty, wide);

        // The logo waits above the screen until it drops.
        _logo.Opacity = 0;
        _static.Opacity = 0.85;
        var started = DateTime.UtcNow;
        _ticker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _ticker.Tick += (_, _) =>
        {
            double ms = (DateTime.UtcNow - started).TotalMilliseconds;
            if (_static.Opacity > 0.01) _static.Source = Noise();
            // The floor rolls toward you, one line's spacing at a time.
            _phase = (_phase + 0.06) % 1;
            PlaceCrossLines();
            _caret.Opacity = (int)(ms / 420) % 2 == 0 ? 1 : 0;
        };
        _ticker.Start();
        _static.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(260) });

        // 2. The logo drops in from above and stops dead, as a handheld's does at boot.
        await Task.Delay(700);
        double from = -(ActualHeight / 2 + 200);
        _logo.Opacity = 1;
        var drop = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(1050));
        drop.Completed += (_, _) => Landed();
        _drop.BeginAnimation(TranslateTransform.YProperty, drop);
    }

    /// <summary>The logo has landed: a flash, the chime, and a little shake of the picture.</summary>
    private void Landed()
    {
        UiSound.RetroBoot();
        _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(320)));
        var shake = new DoubleAnimationUsingKeyFrames();
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new SineEase()));
        _drop.BeginAnimation(TranslateTransform.YProperty, shake);
    }

    /// <summary>What's happening, and how far along (0 to 1): the blocks light up to match.</summary>
    public void Report(string status, string detail, double fraction)
    {
        _status.Text = status.ToUpperInvariant();
        _detail.Text = detail;
        _fraction = Math.Max(_fraction, Math.Clamp(fraction, 0, 1));
        int lit = (int)Math.Round(_fraction * Blocks);
        for (int i = _lit; i < lit && i < Blocks; i++)
        {
            if (Theme.ReduceMotion) _blocks[i].Opacity = 1;
            else _blocks[i].BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)) { BeginTime = TimeSpan.FromMilliseconds((i - _lit) * 30) });
        }
        _lit = Math.Max(_lit, lit);
    }

    /// <summary>Lights the last blocks, then the TV switches off into the page: a line, a dot, then dark.</summary>
    public async Task FinishAsync()
    {
        Report(_status.Text, _detail.Text, 1);
        if (Theme.ReduceMotion) return;
        await Task.Delay(320);
        _ticker?.Stop();
        var flat = new DoubleAnimation(1, 0.004, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var narrow = new DoubleAnimation(1, 0.002, TimeSpan.FromMilliseconds(160)) { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(200)));
        _tube.BeginAnimation(ScaleTransform.ScaleYProperty, flat);
        _tube.BeginAnimation(ScaleTransform.ScaleXProperty, narrow);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(300) });
        await Task.Delay(580);
    }
}
