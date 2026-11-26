using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Zone exit animations": leaving Recomp Zone or Retro Lounge (Leave on the "Leave…?" question) plays a
/// short goodbye in the zone's own spirit before Home comes back.
///  - Recomp Zone "unlinks": the page breaks into tiles that turn into machine code and stream into a glowing core over
///    a blueprint grid, which collapses as "exit(0)" types itself out.
///  - Retro Lounge switches off like an old CRT: the picture squashes to a bright line, the line to a dot, and the dot
///    fades, with scanlines and the glow of the tube.
/// Each returns the layer to lay over the page and a task that ends when it's done.
/// </summary>
public static class ZoneExit
{
    public static bool On => Labs.On("zoneexit");

    /// <summary>The page as it looks now, as a picture.</summary>
    private static BitmapSource? Snapshot(FrameworkElement page)
    {
        try
        {
            if (page.ActualWidth < 10 || page.ActualHeight < 10) return null;
            var dpi = VisualTreeHelper.GetDpi(page);
            var rtb = new RenderTargetBitmap((int)(page.ActualWidth * dpi.DpiScaleX), (int)(page.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(page);
            rtb.Freeze();
            return rtb;
        }
        catch (Exception) { return null; }
    }

    private static DoubleAnimation A(double from, double to, int ms, int delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };

    private static readonly string[] Hex =
    {
        "3C 08 80 1F", "27 BD FF E8", "AF BF 00 14", "0C 00 93 2A", "03 E0 00 08", "jr ra", "nop", "blr", "DE AD BE EF",
        "lw a0, 0x4F10", "free(ctx);", "return 0;", "SDL_Quit();", "exit(0);", "unlink", "0x80000400",
    };

    // ---------------------------------------------------------------- Recomp Zone

    public static (FrameworkElement Layer, Task Done) Recomp(FrameworkElement page)
    {
        double w = Math.Max(1, page.ActualWidth), h = Math.Max(1, page.ActualHeight);
        var picture = Snapshot(page);
        var teal = Color.FromRgb(0x3E, 0xF0, 0xE6);
        var root = new Grid { IsHitTestVisible = true, Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0A, 0x11)) };
        // The blueprint grid the code was built on.
        var cell = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0x22, teal.R, teal.G, teal.B)), 1), new RectangleGeometry(new Rect(0, 0, 44, 44)));
        root.Children.Add(new Rectangle { Fill = new DrawingBrush(cell) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 44, 44), ViewportUnits = BrushMappingMode.Absolute } });
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
        root.Children.Add(canvas);
        var center = new Point(w / 2, h / 2 - 20);
        const int total = 1650;

        if (picture == null || Theme.ReduceMotion)
        {
            root.Opacity = 0;
            root.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 250));
            return (root, Task.Delay(400));
        }

        // The page in tiles, each turning into code and streaming into the core, edges first.
        const int cols = 10, rows = 6;
        double tw = w / cols, th = h / rows;
        double sx = picture.PixelWidth / w, sy = picture.PixelHeight / h;
        var rand = new Random();
        var order = Enumerable.Range(0, cols * rows)
            .OrderByDescending(i => Math.Abs((i % cols + 0.5) * tw - center.X) / w + Math.Abs((i / cols + 0.5) * th - center.Y) / h + rand.NextDouble() * 0.25).ToList();
        var mono = new FontFamily("Consolas");
        for (int n = 0; n < order.Count; n++)
        {
            int i = order[n], cx = i % cols, cy = i / cols;
            double x = cx * tw, y = cy * th;
            var crop = new CroppedBitmap(picture, new Int32Rect((int)(x * sx), (int)(y * sy), Math.Max(1, (int)(tw * sx)), Math.Max(1, (int)(th * sy))));
            var tile = new Image { Source = crop, Width = tw + 1, Height = th + 1, Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5) };
            var move = new TranslateTransform();
            var scale = new ScaleTransform(1, 1);
            tile.RenderTransform = new TransformGroup { Children = { scale, move } };
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
            int at = 60 + n * 900 / order.Count;
            double dx = center.X - (x + tw / 2), dy = center.Y - (y + th / 2);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            move.BeginAnimation(TranslateTransform.XProperty, A(0, dx * 0.35, 320, at, ease));
            move.BeginAnimation(TranslateTransform.YProperty, A(0, dy * 0.35, 320, at, ease));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, A(1, 0, 320, at, ease));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(1, 0, 320, at, ease));

            // Out of each tile, a line of code that flies on into the core.
            var code = new TextBlock
            {
                Text = Hex[rand.Next(Hex.Length)], FontFamily = mono, FontSize = 13, Foreground = new SolidColorBrush(teal), Opacity = 0,
                Effect = new DropShadowEffect { Color = teal, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 }
            };
            Canvas.SetLeft(code, x + tw / 2 - 30);
            Canvas.SetTop(code, y + th / 2 - 8);
            var fly = new TranslateTransform();
            code.RenderTransform = fly;
            canvas.Children.Add(code);
            var show = new DoubleAnimationUsingKeyFrames();
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 120))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 200))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at + 560))));
            code.BeginAnimation(UIElement.OpacityProperty, show);
            fly.BeginAnimation(TranslateTransform.XProperty, A(0, dx, 440, at + 120, ease));
            fly.BeginAnimation(TranslateTransform.YProperty, A(0, dy, 440, at + 120, ease));
        }

        // The core: a ring that swells as the code pours in, then collapses to nothing.
        var core = new Ellipse { Width = 90, Height = 90, Stroke = new SolidColorBrush(teal), StrokeThickness = 4, RenderTransformOrigin = new Point(0.5, 0.5),
                                 Effect = new DropShadowEffect { Color = teal, BlurRadius = 30, ShadowDepth = 0, Opacity = 1 } };
        var coreScale = new ScaleTransform(0.2, 0.2);
        core.RenderTransform = coreScale;
        Canvas.SetLeft(core, center.X - 45);
        Canvas.SetTop(core, center.Y - 45);
        canvas.Children.Add(core);
        var swell = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, v) in new[] { (0, 0.2), (300, 0.7), (1000, 1.25), (1250, 1.35), (1450, 0.0) })
            swell.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase()));
        coreScale.BeginAnimation(ScaleTransform.ScaleXProperty, swell);
        coreScale.BeginAnimation(ScaleTransform.ScaleYProperty, swell);

        // The last word, typed out.
        var line = new TextBlock { FontFamily = mono, FontSize = 22, Foreground = new SolidColorBrush(teal), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90) };
        root.Children.Add(line);
        const string words = "> unlinking recomp zone ... exit(0)";
        var typing = new StringAnimationUsingKeyFrames();
        for (int k = 0; k <= words.Length; k++)
            typing.KeyFrames.Add(new DiscreteStringKeyFrame(words[..k] + (k < words.Length ? "_" : ""), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250 + k * 28))));
        line.BeginAnimation(TextBlock.TextProperty, typing);

        // And the whole thing fades out at the end.
        root.BeginAnimation(UIElement.OpacityProperty, A(1, 0, 220, total - 220));
        return (root, Task.Delay(total));
    }

    // ---------------------------------------------------------------- Retro Lounge

    public static (FrameworkElement Layer, Task Done) Retro(FrameworkElement page)
    {
        var picture = Snapshot(page);
        var root = new Grid { Background = Brushes.Black, IsHitTestVisible = true };
        const int total = 1050;
        if (picture == null || Theme.ReduceMotion)
        {
            root.Opacity = 0;
            root.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 250));
            return (root, Task.Delay(400));
        }

        // The picture on the tube, with scanlines over it, squashing down to a bright line and then a dot.
        var tube = new Grid { RenderTransformOrigin = new Point(0.5, 0.5) };
        tube.Children.Add(new Image { Source = picture, Stretch = Stretch.Fill });
        var lines = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 2)));
        tube.Children.Add(new Rectangle { Fill = new DrawingBrush(lines) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute } });
        var white = new Rectangle { Fill = Brushes.White, Opacity = 0 };
        tube.Children.Add(white);
        var squash = new ScaleTransform(1, 1);
        tube.RenderTransform = squash;
        tube.Effect = new DropShadowEffect { Color = Color.FromRgb(0xCF, 0xE8, 0xFF), BlurRadius = 40, ShadowDepth = 0, Opacity = 0 };
        root.Children.Add(tube);

        var inEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        // A little jolt first, as the set loses power.
        var jolt = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, v) in new[] { (0, 1.0), (60, 1.02), (120, 1.0) })
            jolt.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        var downY = new DoubleAnimationUsingKeyFrames();
        downY.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        downY.KeyFrames.Add(new EasingDoubleKeyFrame(0.006, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), inEase));
        squash.BeginAnimation(ScaleTransform.ScaleYProperty, downY);
        var downX = new DoubleAnimationUsingKeyFrames();
        downX.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        downX.KeyFrames.Add(new LinearDoubleKeyFrame(1.02, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
        downX.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
        downX.KeyFrames.Add(new EasingDoubleKeyFrame(0.004, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), inEase));
        squash.BeginAnimation(ScaleTransform.ScaleXProperty, downX);
        white.BeginAnimation(UIElement.OpacityProperty, A(0, 1, 260, 140));
        tube.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, A(0, 1, 260, 200));
        tube.BeginAnimation(UIElement.OpacityProperty, A(1, 0, 60, 640));

        // The dot that lingers in the middle of the tube, then fades.
        var dot = new Ellipse
        {
            Width = 16, Height = 16, Fill = Brushes.White, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0xCF, 0xE8, 0xFF), BlurRadius = 30, ShadowDepth = 0, Opacity = 1 }
        };
        root.Children.Add(dot);
        var glow = new DoubleAnimationUsingKeyFrames();
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640))));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total - 50)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        dot.BeginAnimation(UIElement.OpacityProperty, glow);
        var shrink = new ScaleTransform(1, 1);
        dot.RenderTransformOrigin = new Point(0.5, 0.5);
        dot.RenderTransform = shrink;
        shrink.BeginAnimation(ScaleTransform.ScaleXProperty, A(1, 0.3, 400, 640));
        shrink.BeginAnimation(ScaleTransform.ScaleYProperty, A(1, 0.3, 400, 640));
        return (root, Task.Delay(total));
    }
}
