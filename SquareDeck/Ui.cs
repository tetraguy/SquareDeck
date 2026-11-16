using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>Small factory helpers for the screens that are built in code.</summary>
public static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock Text(string text, double size, string brushKey = "FgBrush", bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static TextBlock Glyph(string glyph, double size, string brushKey = "FgBrush")
    {
        var t = Text(glyph, size, brushKey);
        t.FontFamily = IconFont;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// <summary>
    /// A full-width list row: an icon in a tinted rounded badge (or a picture), the label, and an optional
    /// value on the right. A second line can go under the label.
    /// </summary>
    public static Button Row(string label, string? value = null, string? glyph = null, Image? icon = null, string? subtitle = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (glyph != null)
        {
            grid.Children.Add(IconBadge(glyph, 40));
        }
        else if (icon != null)
        {
            icon.Width = 40;
            icon.Height = 40;
            icon.Stretch = System.Windows.Media.Stretch.UniformToFill;
            var holder = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0), Child = icon };
            holder.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 40, 40), 10, 10);
            grid.Children.Add(holder);
        }

        var text = Text(label, 19);
        if (subtitle != null)
        {
            var two = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            two.Children.Add(text);
            var sub = Text(subtitle, 14, "SubtleBrush");
            sub.Margin = new Thickness(0, 2, 0, 0);
            two.Children.Add(sub);
            Grid.SetColumn(two, 1);
            grid.Children.Add(two);
        }
        else
        {
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }

        if (value is "On" or "Off")
        {
            // On / Off reads at a glance as a switch.
            var toggle = Toggle(value == "On");
            toggle.Margin = new Thickness(16, 0, 4, 0);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);
        }
        else if (value != null)
        {
            var v = Text(value, 16, "SubtleBrush");
            v.Margin = new Thickness(16, 0, 4, 0);
            v.VerticalAlignment = VerticalAlignment.Center;
            v.MaxWidth = 420;
            Grid.SetColumn(v, 2);
            grid.Children.Add(v);
        }

        return new Button { Style = StyleOf("RowButton"), Content = grid };
    }

    /// <summary>
    /// A square icon button for a sidebar shrunk to icons only (library, Settings): the icon (or a picture, or
    /// initials) on a tinted square, the name as a tooltip, a stronger tint for the section you're in.
    /// </summary>
    public static Button BladeButton(string? glyph, string tip, bool selected = false, string? initials = null, UIElement? picture = null)
    {
        UIElement face;
        if (picture != null) face = picture;
        else
        {
            var badge = new Grid { Width = 40, Height = 40 };
            var tint = new Border { CornerRadius = new CornerRadius(11), Opacity = selected ? 0.42 : 0.16 };
            tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            badge.Children.Add(tint);
            var mark = initials != null ? Text(initials, 15, selected ? "FgBrush" : "AccentBrush", bold: true) : Glyph(glyph ?? "\uE8A9", 20, "AccentBrush");
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            badge.Children.Add(mark);
            face = badge;
        }
        if (face is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        return new Button { Style = StyleOf("TileButton"), Content = face, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Center };
    }

    /// <summary>A switch drawing: a pill with a knob, filled with the highlight color when on.</summary>
    public static Grid Toggle(bool on)
    {
        var grid = new Grid { Width = 50, Height = 28, VerticalAlignment = VerticalAlignment.Center, ToolTip = on ? "On" : "Off" };
        var track = new Border { CornerRadius = new CornerRadius(14) };
        if (on) track.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else
        {
            track.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
            track.BorderThickness = new Thickness(2);
            track.Opacity = 0.75;
        }
        grid.Children.Add(track);
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = on ? 20 : 14, Height = on ? 20 : 14, Margin = new Thickness(on ? 0 : 7, 0, on ? 4 : 0, 0),
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        knob.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "BgBrush" : "SubtleBrush");
        grid.Children.Add(knob);
        return grid;
    }

    /// <summary>A symbol in a rounded square tinted with the highlight color.</summary>
    public static Grid IconBadge(string glyph, double size)
    {
        var badge = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var tint = new Border { CornerRadius = new CornerRadius(size * 0.27), Opacity = 0.16 };
        tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        badge.Children.Add(tint);
        var g = Glyph(glyph, size * 0.5, "AccentBrush");
        g.HorizontalAlignment = HorizontalAlignment.Center;
        g.VerticalAlignment = VerticalAlignment.Center;
        badge.Children.Add(g);
        return badge;
    }

    /// <summary>
    /// Makes runs of list rows read as grouped cards: rounded at the top of a group and the bottom, square
    /// in between, with a hairline gap. Headers, notes and other elements start a new group.
    /// </summary>
    public static void GroupRows(Panel panel)
    {
        var rowStyle = StyleOf("RowButton");
        var children = panel.Children.OfType<UIElement>().ToList();
        int i = 0;
        while (i < children.Count)
        {
            if (children[i] is not Button { } first || first.Style != rowStyle) { i++; continue; }
            int start = i;
            while (i < children.Count && children[i] is Button b && b.Style == rowStyle) i++;
            int count = i - start;
            for (int k = 0; k < count; k++)
            {
                var row = (Button)children[start + k];
                double top = k == 0 ? 14 : 4, bottom = k == count - 1 ? 14 : 4;
                row.Resources["RowCorner"] = new CornerRadius(top, top, bottom, bottom);
                row.Margin = new Thickness(row.Margin.Left, k == 0 ? 2 : 1, row.Margin.Right, k == count - 1 ? 2 : 1);
            }
        }
    }

    /// <summary>A row whose right-hand value can be updated later without rebuilding the row.</summary>
    public static Button ValueRow(string label, string glyph, out TextBlock value)
    {
        var row = Row(label, "", glyph);
        value = ((Grid)row.Content).Children.OfType<TextBlock>().Last();
        return row;
    }

    /// <summary>
    /// A 0–100 slider in a row. Drag it, or highlight it and press left / right.
    /// The row shows the highlight ring itself, because a bare slider has none.
    /// </summary>
    public static Border SliderRow(string label, string glyph, out Slider slider, out TextBlock value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var g = Glyph(glyph, 22, "SubtleBrush");
        g.Width = 34;
        g.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(g);

        var text = Text(label, 20);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var s = new Slider
        {
            Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 5,
            IsMoveToPointEnabled = true, FocusVisualStyle = null,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(s, 2);
        grid.Children.Add(s);

        var v = Text("", 18, "SubtleBrush");
        v.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(v, 3);
        grid.Children.Add(v);

        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(0, 3, 0, 3),
            Child = grid
        };
        border.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        s.GotKeyboardFocus += (_, _) => border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        s.LostKeyboardFocus += (_, _) => border.BorderBrush = Brushes.Transparent;

        slider = s;
        value = v;
        return border;
    }

    public static Button PanelButton(string label) => new() { Style = StyleOf("PanelButton"), Content = label };

    /// <summary>A focusable tile that shows a highlight ring.</summary>
    public static Button Tile(UIElement content) => new() { Style = StyleOf("TileButton"), Content = content };

    public static Button Round(string glyph, string brushKey = "FgBrush")
    {
        var b = new Button { Style = StyleOf("RoundButton"), Content = glyph, Focusable = true };
        b.SetResourceReference(Control.ForegroundProperty, brushKey);
        return b;
    }

    /// <summary>"★★★☆☆" for a 1–5 rating; empty for unrated.</summary>
    public static string Stars(int rating) =>
        rating <= 0 ? "" : new string('\u2605', Math.Min(rating, 5)) + new string('\u2606', 5 - Math.Min(rating, 5));

    /// <summary>"just now", "25 minutes ago", "3 days ago"…</summary>
    public static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span < TimeSpan.FromMinutes(2)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} minutes ago";
        if (span < TimeSpan.FromHours(2)) return "an hour ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} hours ago";
        if (span < TimeSpan.FromDays(2)) return "yesterday";
        if (span < TimeSpan.FromDays(31)) return $"{(int)span.TotalDays} days ago";
        if (span < TimeSpan.FromDays(365)) return $"{(int)(span.TotalDays / 30.4)} months ago";
        return when.ToString("MMM yyyy");
    }

    public static TextBlock Header(string text)
    {
        var t = Text(text, 17, "FgBrush", bold: true);
        t.Opacity = 0.85;
        t.Margin = new Thickness(6, 26, 0, 10);
        return t;
    }
}

/// <summary>
/// The row of button hints at the bottom of a page, drawn the same way everywhere: a filled pill with the
/// button (or key) name, then what it does. Redraws itself when the controller style or input device changes.
/// </summary>
public sealed class HintBar : ContentControl
{
    private string _template;

    public HintBar(string template)
    {
        _template = template;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => { Hints.Changed -= Rebuild; Hints.Changed += Rebuild; Rebuild(); };
        Unloaded += (_, _) => Hints.Changed -= Rebuild;
        Rebuild();
    }

    public string Template
    {
        get => _template;
        set { if (_template == value) return; _template = value; Rebuild(); }
    }

    /// <summary>What tapping or clicking the "Back" hint does (the window goes back, as the B button would).</summary>
    public static Action? BackRequested { get; set; }

    private void Rebuild()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var items = Hints.Parse(_template);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // Each hint is its own group; only "Back" can be tapped or clicked (pages no longer have a Back button).
            bool isBack = item.Label.Equals("Back", StringComparison.OrdinalIgnoreCase) || item.Label.Equals("Close", StringComparison.OrdinalIgnoreCase);
            var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = isBack, Background = isBack ? Brushes.Transparent : null };
            if (isBack)
            {
                row.Cursor = System.Windows.Input.Cursors.Hand;
                row.ToolTip = "Back";
                row.MouseLeftButtonUp += (_, e) => { e.Handled = true; BackRequested?.Invoke(); };
                row.TouchUp += (_, e) => { e.Handled = true; BackRequested?.Invoke(); };
            }
            bar.Children.Add(row);
            bool first = true;
            foreach (var (text, isKey, image) in item.Keys)
            {
                if (isKey)
                {
                    row.Children.Add(image != null && Glyph(image, 30, first ? 0 : 6) is { } picture ? picture : Pill(text, first ? 0 : 6));
                }
                else
                {
                    var word = Ui.Text(text, 17);
                    word.Margin = new Thickness(first ? 0 : 6, 0, 0, 0);
                    row.Children.Add(word);
                }
                first = false;
            }
            var label = Ui.Text(item.Label, 20);
            label.Margin = new Thickness(item.Keys.Count > 0 ? 8 : 0, 0, i == items.Count - 1 ? 0 : 24, 0);
            row.Children.Add(label);
        }
        // Long rows (keyboard names are wider) shrink to fit instead of running off the screen.
        Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 1160, Child = bar };
    }

    private static readonly Dictionary<string, ImageSource?> GlyphCache = new();

    /// <summary>A button picture from one of the glyph sets, or null if the file can't be read.</summary>
    public static Image? Glyph(string path, double height, double leftMargin = 0)
    {
        if (!GlyphCache.TryGetValue(path, out var source))
            GlyphCache[path] = source = ImageLoader.FromFile(path, 128);
        if (source == null) return null;
        var image = new Image { Source = source, Height = height, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(leftMargin, 0, 0, 0) };
        // Pixel art stays crisp instead of being smoothed.
        if (Hints.Look == "pixel") RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    /// <summary>A button or key name on a filled shape: a circle for one character, a rounded pill for longer names.</summary>
    public static Border Pill(string text, double leftMargin = 0)
    {
        bool single = text.Length == 1;
        var name = new TextBlock
        {
            Text = text, FontSize = single ? 15 : 13, FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
        var pill = new Border
        {
            MinWidth = 26, Height = 26, CornerRadius = new CornerRadius(single ? 13 : 6),
            Padding = single ? new Thickness(0) : new Thickness(7, 0, 7, 0),
            Margin = new Thickness(leftMargin, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = name
        };
        pill.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        return pill;
    }
}

/// <summary>The soft "breathing" of the highlight ring, and other motion that Accessibility can turn off.</summary>
public static class Motion
{
    /// <summary>Set from Settings: animate the home screen's buttons (Reduce motion also turns it off).</summary>
    public static bool ButtonAnimations { get; set; } = true;

    private static bool Animate => ButtonAnimations && !Theme.ReduceMotion;
    private static readonly Dictionary<UIElement, bool> Focused = new();

    /// <summary>Set from Settings: "grow", "lift", "bounce", "pulse" or "wiggle".</summary>
    public static string ButtonStyle { get; set; } = "grow";

    /// <summary>Set from Settings: how pages open ("slide", "fade", "zoom" or "none").</summary>
    public static string PageStyle { get; set; } = "slide";

    /// <summary>Set from Settings: the selected game tile ("pulse", "steady" or "grow").</summary>
    public static string TileStyle { get; set; } = "pulse";

    private sealed class Parts
    {
        public readonly ScaleTransform Scale = new(1, 1);
        public readonly RotateTransform Rotate = new(0);
        public readonly TranslateTransform Move = new();
    }

    // Weak, so pages that have closed aren't kept alive by their animations.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Parts> Transforms = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, TransformGroup> Groups = new();

    /// <summary>Scale, tilt and offset for an element, created once and reused (again if something replaced them).</summary>
    private static Parts PartsOf(UIElement element)
    {
        if (Transforms.TryGetValue(element, out var parts) && Groups.TryGetValue(element, out var mine) &&
            ReferenceEquals(element.RenderTransform, mine)) return parts;
        parts = new Parts();
        var group = new TransformGroup();
        group.Children.Add(parts.Scale);
        group.Children.Add(parts.Rotate);
        group.Children.Add(parts.Move);
        element.RenderTransform = group;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        Transforms.AddOrUpdate(element, parts);
        Groups.AddOrUpdate(element, group);
        return parts;
    }

    private static ScaleTransform ScaleOf(UIElement element) => PartsOf(element).Scale;

    private static void To(Animatable target, DependencyProperty property, double value, int ms, IEasingFunction? ease = null) =>
        target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });

    private static void Reset(Parts parts)
    {
        foreach (var (t, p) in new (Animatable, DependencyProperty)[]
                 {
                     (parts.Scale, ScaleTransform.ScaleXProperty), (parts.Scale, ScaleTransform.ScaleYProperty),
                     (parts.Rotate, RotateTransform.AngleProperty), (parts.Move, TranslateTransform.YProperty)
                 })
            t.BeginAnimation(p, null);
        parts.Scale.ScaleX = parts.Scale.ScaleY = 1;
        parts.Rotate.Angle = 0;
        parts.Move.Y = 0;
    }

    /// <summary>The size a highlighted button rests at, for the current style.</summary>
    private static double RestSize(double size) => ButtonStyle == "lift" ? 1 + (size - 1) * 0.5 : size;

    /// <summary>
    /// A home button gaining or losing the highlight, in the style chosen in Settings: grow (with a little
    /// overshoot), lift (rises with a shadow), bounce, pulse (keeps breathing) or wiggle. All add a soft glow.
    /// </summary>
    public static void Highlight(UIElement element, bool on, double size = 1.12)
    {
        if (Focused.TryGetValue(element, out var was) && was == on) return;
        Focused[element] = on;
        var parts = PartsOf(element);
        if (!Animate)
        {
            Reset(parts);
            if (element.Effect is System.Windows.Media.Effects.DropShadowEffect) element.Effect = null;
            return;
        }

        var scale = parts.Scale;
        var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (!on)
        {
            Reset(parts); // stop a running pulse or wiggle cleanly…
            scale.ScaleX = scale.ScaleY = Math.Max(1, RestSize(size)); // …and settle down from the highlighted size
            To(scale, ScaleTransform.ScaleXProperty, 1, 180, outEase);
            To(scale, ScaleTransform.ScaleYProperty, 1, 180, outEase);
        }
        else
        {
            double rest = RestSize(size);
            switch (ButtonStyle)
            {
                case "lift":
                    To(parts.Move, TranslateTransform.YProperty, -9, 240, outEase);
                    To(scale, ScaleTransform.ScaleXProperty, rest, 240, outEase);
                    To(scale, ScaleTransform.ScaleYProperty, rest, 240, outEase);
                    break;
                case "bounce":
                {
                    var bounce = new DoubleAnimationUsingKeyFrames();
                    bounce.KeyFrames.Add(new EasingDoubleKeyFrame(size + 0.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), outEase));
                    bounce.KeyFrames.Add(new EasingDoubleKeyFrame(size - 0.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), outEase));
                    bounce.KeyFrames.Add(new EasingDoubleKeyFrame(size + 0.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), outEase));
                    bounce.KeyFrames.Add(new EasingDoubleKeyFrame(size, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)), outEase));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
                    break;
                }
                case "pulse":
                {
                    // Grows, then keeps gently breathing for as long as it has the highlight.
                    var breathe = new DoubleAnimation(size - 0.04, size + 0.04, TimeSpan.FromMilliseconds(700))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                    break;
                }
                case "wiggle":
                {
                    var wiggle = new DoubleAnimationUsingKeyFrames();
                    foreach (var (angle, ms) in new[] { (-9.0, 60), (8.0, 130), (-5.0, 200), (3.0, 260), (0.0, 320) })
                        wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                    parts.Rotate.BeginAnimation(RotateTransform.AngleProperty, wiggle);
                    To(scale, ScaleTransform.ScaleXProperty, size, 220, outEase);
                    To(scale, ScaleTransform.ScaleYProperty, size, 220, outEase);
                    break;
                }
                default: // grow
                    var back = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut };
                    To(scale, ScaleTransform.ScaleXProperty, size, 280, back);
                    To(scale, ScaleTransform.ScaleYProperty, size, 280, back);
                    break;
            }
        }

        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        if (element.Effect is not System.Windows.Media.Effects.DropShadowEffect glow)
        {
            glow = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 20, Opacity = 0, Color = accent };
            element.Effect = glow;
        }
        glow.Color = accent;
        // "Lift" casts a shadow below instead of a glow all round.
        glow.ShadowDepth = on && ButtonStyle == "lift" ? 7 : 0;
        glow.Direction = 270;
        var fade = new DoubleAnimation(on ? 0.75 : 0, TimeSpan.FromMilliseconds(on ? 260 : 160));
        if (!on) fade.Completed += (_, _) => { if (Focused.TryGetValue(element, out var now) && !now) element.Effect = null; };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, fade);
    }

    /// <summary>A page or pop-up opening, in the style chosen in Settings.</summary>
    public static void OpenPage(FrameworkElement page, bool dialog)
    {
        if (Theme.ReduceMotion || PageStyle == "none") return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(dialog ? 160 : 220)));
        if (PageStyle == "fade") return;
        var parts = PartsOf(page);
        void Scale(double from, int ms, IEasingFunction e, bool x = true, bool y = true)
        {
            var a = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e };
            if (x) parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            if (y) parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        void Move(DependencyProperty axis, double from, int ms, IEasingFunction e) =>
            parts.Move.BeginAnimation(axis, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e });

        if (dialog && PageStyle is not ("pop" or "drop" or "swing"))
        {
            Scale(0.94, 200, ease);
            return;
        }
        switch (PageStyle)
        {
            case "zoom": Scale(0.96, 260, ease); break;
            case "slide-left": Move(TranslateTransform.XProperty, 90, 280, ease); break;   // in from the right, like a phone
            case "slide-down": Move(TranslateTransform.YProperty, -40, 280, ease); break;  // drops in from above
            case "rise": Move(TranslateTransform.YProperty, 60, 340, ease); Scale(0.97, 340, ease); break;
            case "pop": Scale(0.85, 380, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }); break;
            case "spring": Move(TranslateTransform.YProperty, 70, 620, new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut }); break;
            case "drop": Move(TranslateTransform.YProperty, -70, 520, new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut }); break;
            case "flip": Scale(0.05, 320, ease, x: true, y: false); break;                    // turns like a card
            case "swing":
                parts.Rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(-5, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
                Scale(0.96, 420, ease);
                break;
            case "focus":
                // Comes into focus from a blur (skipped in Performance mode).
                if (!Perf.Active)
                {
                    var blur = new System.Windows.Media.Effects.BlurEffect { Radius = 18, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
                    page.Effect = blur;
                    var sharpen = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
                    sharpen.Completed += (_, _) => { if (ReferenceEquals(page.Effect, blur)) page.Effect = null; };
                    blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, sharpen);
                }
                Scale(1.03, 300, ease);
                break;
            default: Move(TranslateTransform.YProperty, 36, 260, ease); break;                // slide up
        }
    }

    /// <summary>The selected game tile growing a little (the "grow" tile highlight).</summary>
    /// <summary>How much the UI style grows the selected home tile (1 = only the "Tile grows" animation option).</summary>
    public static double SelectGrow { get; set; } = 1;

    /// <summary>Where the selected tile grows from, if the UI style says (null = automatic).</summary>
    public static Point? GrowOrigin { get; set; }

    public static void GrowTile(UIElement? tile, bool on)
    {
        if (tile == null) return;
        var scale = ScaleOf(tile);
        double grow = SelectGrow > 1.001 ? SelectGrow : TileStyle == "grow" ? 1.05 : 1;
        double target = on && !Theme.ReduceMotion ? grow : 1;
        Panel.SetZIndex(tile, on ? 1 : 0); // a grown tile sits over its neighbours
        // Big growth goes upward, into the space kept for the title, so the bottom of the tile isn't cut off.
        tile.RenderTransformOrigin = GrowOrigin ?? (grow > 1.1 ? new Point(0.5, 0.92) : new Point(0.5, 0.5));
        To(scale, ScaleTransform.ScaleXProperty, target, 220, new CubicEase { EasingMode = EasingMode.EaseOut });
        To(scale, ScaleTransform.ScaleYProperty, target, 220, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    /// <summary>A press: a quick squash, then a spring back to its highlighted size.</summary>
    public static void Press(UIElement element)
    {
        if (!Animate) return;
        var scale = ScaleOf(element);
        double rest = Focused.TryGetValue(element, out var on) && on ? RestSize(1.12) : 1;
        var squash = new DoubleAnimationUsingKeyFrames();
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(0.84, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(rest * 1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, squash);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, squash);
    }

    /// <summary>Plays the press, then (when animating) runs the action once the squash can be seen.</summary>
    public static async void PressThen(UIElement element, Action action)
    {
        Press(element);
        if (Animate) await Task.Delay(110);
        action();
    }

    /// <summary>The mouse or a finger going down on a button: hold it squashed until it is released.</summary>
    public static void HoldDown(UIElement element)
    {
        if (!Animate) return;
        var scale = ScaleOf(element);
        var down = new DoubleAnimation(0.86, TimeSpan.FromMilliseconds(90)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    /// <summary>Let go: spring back to the resting (or highlighted) size.</summary>
    public static void Release(UIElement element)
    {
        if (!Animate) return;
        var scale = ScaleOf(element);
        double rest = Focused.TryGetValue(element, out var on) && on ? RestSize(1.12) : 1;
        var back = new DoubleAnimation(rest, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, back);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, back);
    }

    public static void StartPulse(UIElement? ring)
    {
        if (ring == null) return;
        // Performance mode: a steady ring (a pulse redraws the screen every frame).
        if (Theme.ReduceMotion || TileStyle != "pulse" || Perf.Active) { StopPulse(ring); return; }
        ring.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(1.2))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        });
    }

    public static void StopPulse(UIElement? ring)
    {
        if (ring == null) return;
        ring.BeginAnimation(UIElement.OpacityProperty, null);
        ring.Opacity = 1;
    }

    private static bool _registered;

    /// <summary>Makes every tile-style button pulse its ring while it has the highlight.</summary>
    public static void RegisterTilePulse()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Button), UIElement.GotKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((sender, _) => StartPulse(RingOf(sender))));
        EventManager.RegisterClassHandler(typeof(Button), UIElement.LostKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((sender, _) => StopPulse(RingOf(sender))));
    }

    private static UIElement? RingOf(object sender) =>
        sender is Button b && ReferenceEquals(b.Style, Ui.StyleOf("TileButton")) ? b.Template?.FindName("B", b) as UIElement : null;
}

/// <summary>Profile pictures: a set of built-in ones, or any image file.</summary>
public static class Avatars
{
    public static readonly (string Color, string Symbol)[] Presets =
    {
        ("#E8453C", "\U0001F3AE"), ("#7B5BD6", "\U0001F47E"), ("#2B7DE0", "\U0001F680"), ("#F2A100", "⭐"),
        ("#2EB872", "\U0001F431"), ("#D9603B", "\U0001F436"), ("#C2410C", "\U0001F98A"), ("#3F4A5A", "\U0001F43C"),
        ("#0AB9C9", "\U0001F3A7"), ("#CA8A04", "⚡"), ("#DC2626", "\U0001F525"), ("#4338CA", "\U0001F319")
    };

    /// <summary>Builds the round picture for a saved setting ("preset:N" or an image path).</summary>
    public static FrameworkElement Build(string setting, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        var circle = new Ellipse { StrokeThickness = Math.Max(2, size / 28) };
        circle.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        grid.Children.Add(circle);

        ImageSource? custom = null;
        if (!setting.StartsWith("preset:", StringComparison.Ordinal) && System.IO.File.Exists(setting))
            custom = ImageLoader.FromFile(setting, 256);

        if (custom != null)
        {
            circle.Fill = new ImageBrush(custom) { Stretch = Stretch.UniformToFill };
            return grid;
        }

        int index = 0;
        if (setting.StartsWith("preset:", StringComparison.Ordinal)) int.TryParse(setting[7..], out index);
        var (color, symbol) = Presets[Math.Clamp(index, 0, Presets.Length - 1)];

        circle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        grid.Children.Add(new TextBlock
        {
            Text = symbol,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
            FontSize = size * 0.46,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return grid;
    }
}

/// <summary>
/// Shows one line of text; when it's wider than the space, it slides slowly to show the rest, pauses, and slides
/// back. With <see cref="OnlyWhenActive"/> it only moves while <see cref="Active"/> (the highlighted tile), and
/// shows "…" otherwise. Respects Reduce motion.
/// </summary>
public sealed class MarqueeText : FrameworkElement
{
    private readonly TextBlock _text;
    private readonly TranslateTransform _shift = new();
    private bool _active;
    private double _lastOverflow = -1;

    public MarqueeText(TextBlock text, bool onlyWhenActive = false)
    {
        _text = text;
        _text.TextTrimming = TextTrimming.None;
        _text.TextWrapping = TextWrapping.NoWrap;
        _text.RenderTransform = _shift;
        OnlyWhenActive = onlyWhenActive;
        AddVisualChild(_text);
        AddLogicalChild(_text);
        ClipToBounds = true;
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(_text, (_, _) => { _lastOverflow = -1; InvalidateMeasure(); });
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => { _lastOverflow = -1; InvalidateArrange(); };
    }

    public TextBlock Text => _text;
    public bool OnlyWhenActive { get; }

    /// <summary>Lets the text scroll (for <see cref="OnlyWhenActive"/>), e.g. while its tile is highlighted.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            _lastOverflow = -1;
            InvalidateArrange();
        }
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _text;
    protected override System.Collections.IEnumerator LogicalChildren => new[] { _text }.GetEnumerator();

    protected override Size MeasureOverride(Size available)
    {
        _text.Measure(new Size(double.PositiveInfinity, available.Height));
        var want = _text.DesiredSize;
        return new Size(double.IsInfinity(available.Width) ? want.Width : Math.Min(want.Width, available.Width), want.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double full = _text.DesiredSize.Width;
        double overflow = full - final.Width;
        bool scroll = overflow > 1 && !Theme.ReduceMotion && (!OnlyWhenActive || _active);
        // Not scrolling: fit the box and trim with "…". Scrolling: full width, slid by the animation.
        _text.TextTrimming = scroll || overflow <= 1 ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        _text.Arrange(new Rect(0, 0, scroll ? full : final.Width, final.Height));
        double key = scroll ? overflow : 0;
        if (Math.Abs(key - _lastOverflow) > 0.5)
        {
            _lastOverflow = key;
            if (scroll) Start(overflow); else Stop();
        }
        return final;
    }

    private void Start(double overflow)
    {
        double distance = overflow + 12;
        var move = TimeSpan.FromSeconds(Math.Max(1.2, distance / 32)); // about 32 pixels a second
        var hold = TimeSpan.FromSeconds(1.6);
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var t = TimeSpan.Zero;
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(t)));
        t += hold;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t)));
        t += move;
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
        t += hold;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(t)));
        t += move;
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
        _shift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void Stop()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
    }
}
