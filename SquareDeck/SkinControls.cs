using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquareDeck;

/// <summary>
/// Draws a picture as a nine-slice frame (Advanced styles): the four corners stay their own size, the edges stretch
/// along their length and the middle fills the rest, so a small button or panel picture fits any size without its
/// border or corners stretching. <see cref="Slice"/> is in the picture's own pixels; <see cref="Scale"/> sets how big
/// those pixels are drawn. Pixel art is drawn with hard edges.
/// </summary>
public sealed class NineSlice : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(NineSlice),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((NineSlice)d).UpdateVisibility()));
    public static readonly DependencyProperty SliceProperty = DependencyProperty.Register(nameof(Slice), typeof(Thickness), typeof(NineSlice),
        new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double), typeof(NineSlice),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PixelProperty = DependencyProperty.Register(nameof(Pixel), typeof(bool), typeof(NineSlice),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => RenderOptions.SetBitmapScalingMode(d, (bool)e.NewValue ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality)));

    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Thickness Slice { get => (Thickness)GetValue(SliceProperty); set => SetValue(SliceProperty, value); }
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public bool Pixel { get => (bool)GetValue(PixelProperty); set => SetValue(PixelProperty, value); }

    public NineSlice()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Visibility = Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    private void UpdateVisibility() => Visibility = Source == null ? Visibility.Collapsed : Visibility.Visible;

    // The nine pieces of each picture, cut once and kept for as long as the picture is in use.
    private static readonly ConditionalWeakTable<BitmapSource, Dictionary<Thickness, BitmapSource[]>> Pieces = new();

    private static BitmapSource[]? Cut(BitmapSource bmp, Thickness s)
    {
        var byInsets = Pieces.GetOrCreateValue(bmp);
        lock (byInsets)
        {
            if (byInsets.TryGetValue(s, out var known)) return known;
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            int l = Math.Clamp((int)s.Left, 0, w), r = Math.Clamp((int)s.Right, 0, w - l), t = Math.Clamp((int)s.Top, 0, h), b = Math.Clamp((int)s.Bottom, 0, h - t);
            int[] xs = { 0, l, w - r, w }, ys = { 0, t, h - b, h };
            var parts = new BitmapSource[9];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    int x = xs[col], y = ys[row], pw = xs[col + 1] - x, ph = ys[row + 1] - y;
                    if (pw <= 0 || ph <= 0) continue;
                    var piece = new CroppedBitmap(bmp, new Int32Rect(x, y, pw, ph));
                    piece.Freeze();
                    parts[row * 3 + col] = piece;
                }
            byInsets[s] = parts;
            return parts;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Source is not BitmapSource bmp || ActualWidth <= 0 || ActualHeight <= 0) return;
        Draw(dc, bmp, Slice, Scale, ActualWidth, ActualHeight);
    }

    /// <summary>The same nine-slice picture as a drawing of the given size (for backgrounds painted by code).</summary>
    public static DrawingGroup Drawing(BitmapSource bmp, Thickness slice, double scale, double width, double height, bool pixel)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, pixel ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var dc = group.Open()) Draw(dc, bmp, slice, scale, width, height);
        group.Freeze();
        return group;
    }

    private static void Draw(DrawingContext dc, BitmapSource bmp, Thickness s, double scale, double W, double H)
    {
        var parts = Cut(bmp, s);
        if (parts == null) return;
        double k = Math.Max(0.01, scale);
        // The border at its drawn size, made smaller as a whole when the box is too small for it (so a pill stays a pill).
        double L = s.Left * k, R = s.Right * k, T = s.Top * k, B = s.Bottom * k;
        double fit = Math.Min(1, Math.Min(W / Math.Max(1, L + R), H / Math.Max(1, T + B)));
        L *= fit; R *= fit; T *= fit; B *= fit;
        double[] xs = { 0, L, W - R, W }, ys = { 0, T, H - B, H };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var piece = parts[row * 3 + col];
                double w = xs[col + 1] - xs[col], h = ys[row + 1] - ys[row];
                if (piece == null || w <= 0 || h <= 0) continue;
                // A hair of overlap hides seams between pieces.
                dc.DrawImage(piece, new Rect(xs[col], ys[row], w + (col < 2 ? 0.5 : 0), h + (row < 2 ? 0.5 : 0)));
            }
    }
}

/// <summary>Pictures an Advanced style gives a home button in place of its symbol (set on the button, read by its template).</summary>
public static class SkinParts
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(ImageSource), typeof(SkinParts),
        new FrameworkPropertyMetadata(null, (d, e) =>
        {
            d.SetValue(GlyphVisibilityProperty, e.NewValue == null ? Visibility.Visible : Visibility.Collapsed);
            d.SetValue(IconVisibilityProperty, e.NewValue == null ? Visibility.Collapsed : Visibility.Visible);
        }));
    public static readonly DependencyProperty IconVisibilityProperty = DependencyProperty.RegisterAttached("IconVisibility", typeof(Visibility), typeof(SkinParts),
        new FrameworkPropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty IconScalingProperty = DependencyProperty.RegisterAttached("IconScaling", typeof(BitmapScalingMode), typeof(SkinParts),
        new FrameworkPropertyMetadata(BitmapScalingMode.HighQuality));
    public static readonly DependencyProperty GlyphVisibilityProperty = DependencyProperty.RegisterAttached("GlyphVisibility", typeof(Visibility), typeof(SkinParts),
        new FrameworkPropertyMetadata(Visibility.Visible));

    public static ImageSource? GetIcon(DependencyObject d) => (ImageSource?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, ImageSource? value) => d.SetValue(IconProperty, value);
    public static Visibility GetIconVisibility(DependencyObject d) => (Visibility)d.GetValue(IconVisibilityProperty);
    public static void SetIconVisibility(DependencyObject d, Visibility value) => d.SetValue(IconVisibilityProperty, value);
    public static BitmapScalingMode GetIconScaling(DependencyObject d) => (BitmapScalingMode)d.GetValue(IconScalingProperty);
    public static void SetIconScaling(DependencyObject d, BitmapScalingMode value) => d.SetValue(IconScalingProperty, value);
    public static Visibility GetGlyphVisibility(DependencyObject d) => (Visibility)d.GetValue(GlyphVisibilityProperty);
    public static void SetGlyphVisibility(DependencyObject d, Visibility value) => d.SetValue(GlyphVisibilityProperty, value);
}
