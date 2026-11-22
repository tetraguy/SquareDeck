using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A card in the middle of the page while a picked image downloads: the image's preview, what's happening, and a
/// progress bar that fills with the download. It turns into a tick and "Applied" when the image is in place, or
/// says plainly what went wrong. It covers the page, so it can't be missed at the bottom of a long grid.
/// </summary>
public sealed class DownloadSheet : Grid
{
    private readonly TextBlock _title, _detail;
    private readonly Border _fill, _track;
    private readonly ProgressFill _barFill;
    private long _done;
    private long? _total; // set by Report: until then the line under the title is left as it is
    private readonly Grid _badge;
    private readonly Path _tick;
    private readonly Border _card;
    private double _shown;

    public DownloadSheet(ImageSource? preview, double previewWidth, double previewHeight, string title)
    {
        Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
        Opacity = 0;
        Panel.SetZIndex(this, 500);

        _card = new Border
        {
            Width = 520, CornerRadius = new CornerRadius(22), Padding = new Thickness(30, 28, 30, 26),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.92, 0.92)
        };
        _card.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        _card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _card.BorderThickness = new Thickness(1);
        var stack = new StackPanel();

        // The picture being fetched, with the result badge over its corner.
        double scale = Math.Min(1, Math.Min(300 / Math.Max(1, previewWidth), 170 / Math.Max(1, previewHeight)));
        var pictureHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        var picture = new Border
        {
            Width = previewWidth * scale, Height = previewHeight * scale, CornerRadius = new CornerRadius(12), ClipToBounds = true,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 }
        };
        if (preview != null) picture.Background = new ImageBrush(preview) { Stretch = Stretch.UniformToFill };
        else picture.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        pictureHost.Children.Add(picture);
        _badge = new Grid { Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -16, -14), Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.4, 0.4) };
        var circle = new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x72)), Stroke = Brushes.White, StrokeThickness = 3 };
        _badge.Children.Add(circle);
        _tick = new Path { Data = Geometry.Parse("M13,24 L20,31 L34,16"), Stroke = Brushes.White, StrokeThickness = 4.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        _badge.Children.Add(_tick);
        pictureHost.Children.Add(_badge);
        stack.Children.Add(pictureHost);

        _title = Ui.Text(title, 22, "FgBrush", bold: true);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(_title);
        _detail = Ui.Text("Starting…", 15, "SubtleBrush");
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        _detail.TextAlignment = TextAlignment.Center;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.Margin = new Thickness(0, 6, 0, 18);
        stack.Children.Add(_detail);

        // The bar: fills with the download; slides back and forth while the size isn't known.
        _track = new Border { Height = 10, CornerRadius = new CornerRadius(5), ClipToBounds = true };
        _track.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        _fill = new Border { CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _track.Child = _fill;
        _barFill = new ProgressFill(_track, _fill);
        // The size line is written from the bar as it slides, so its percentage matches the bar on every frame.
        _barFill.Shown = v => { if (_total != null || _done > 0) _detail.Text = _total is > 0 ? $"{Size((long)(_total.Value * v))} of {Size(_total.Value)}  ·  {Math.Floor(v * 100 + 0.0001):0}%" : Size(_done); };
        stack.Children.Add(_track);

        _card.Child = stack;
        Children.Add(_card);
        Loaded += (_, _) => Appear();
    }

    private double BarWidth => 520 - 60;

    private void Appear()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        ((ScaleTransform)_card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)_card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        Indeterminate();
    }

    /// <summary>The size isn't known yet: a short bar sweeps along the track.</summary>
    private void Indeterminate()
    {
        _fill.Width = BarWidth * 0.28;
        var move = new TranslateTransform();
        _fill.RenderTransform = move;
        if (Theme.ReduceMotion) return;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-BarWidth * 0.3, BarWidth, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    /// <summary>How far along the download is (0–1), with the size in the line under the title.</summary>
    public void Report(double part, long done, long? total)
    {
        if (_fill.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); _fill.RenderTransform = null; }
        part = Math.Clamp(part, 0, 1);
        if (part < _shown) part = _shown;
        _shown = part;
        _done = done; _total = total;
        _barFill.Set(part, 180);
    }

    public void Status(string text) => _detail.Text = text;

    /// <summary>In place: the bar completes, a tick pops onto the picture and the title says so.</summary>
    public async Task DoneAsync(string title, string detail)
    {
        if (_fill.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); _fill.RenderTransform = null; }
        _barFill.Shown = null;
        _barFill.Set(1, 200);
        _fill.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x72));
        _title.Text = title;
        _detail.Text = detail;
        UiSound.Select();
        _badge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut } };
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)_badge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        await Task.Delay(1000);
    }

    /// <summary>Didn't work: says why, the bar turns red, and it stays a moment before going.</summary>
    public async Task FailedAsync(string detail)
    {
        if (_fill.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); _fill.RenderTransform = null; }
        _barFill.Shown = null;
        _barFill.Set(1, 0);
        _fill.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x3C));
        _title.Text = "Couldn't download that image";
        _detail.Text = detail;
        UiSound.Back();
        await Task.Delay(2600);
    }

    public async Task HideAsync()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
        await Task.Delay(190);
        if (Parent is Panel p) p.Children.Remove(this);
    }

    private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
