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

    /// <summary>Stands for a drawn handheld console (Segoe MDL2 Assets has none), wherever a glyph is taken.</summary>
    public const string HandheldGlyph = "{handheld}";

    internal static readonly Geometry HandheldShape = Geometry.Parse(
        "F0 M5,3 H19 A4,4 0 0 1 23,7 V9 A4,4 0 0 1 19,13 H5 A4,4 0 0 1 1,9 V7 A4,4 0 0 1 5,3 Z " +
        "M7.5,4.8 H16.5 V11.2 H7.5 Z " +
        "M3.6,6.4 H4.8 V7.4 H5.8 V8.6 H4.8 V9.6 H3.6 V8.6 H2.6 V7.4 H3.6 Z " +
        "M18.4,7.2 A0.8,0.8 0 1 0 20,7.2 A0.8,0.8 0 1 0 18.4,7.2 Z M19.8,8.8 A0.8,0.8 0 1 0 21.4,8.8 A0.8,0.8 0 1 0 19.8,8.8 Z");

    private static ImageSource? _deviceArt;
    private static bool _deviceArtTried;

    /// <summary>
    /// Experimental "Your handheld in the corner": the drawing of the detected handheld (the one the controller tester
    /// shows), as a silhouette in the given color and height. Null when the experiment is off or the device has no drawing.
    /// </summary>
    public static FrameworkElement? DeviceArt(double height, string brushKey = "FgBrush")
    {
        if (!Services.Labs.On("oemfooter")) return null;
        if (!_deviceArtTried) { _deviceArtTried = true; _deviceArt = Services.OemGlyphs.Load(Services.OemGlyphs.Controller); }
        if (_deviceArt is not System.Windows.Media.Imaging.BitmapSource art || art.PixelHeight == 0) return null;
        var shape = new System.Windows.Shapes.Rectangle
        {
            Height = height, Width = Math.Round(height * art.PixelWidth / art.PixelHeight),
            OpacityMask = new ImageBrush(art) { Stretch = Stretch.Uniform }, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Services.Handheld.DisplayName, Tag = "devart"
        };
        shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        return shape;
    }

    public static TextBlock Glyph(string glyph, double size, string brushKey = "FgBrush")
    {
        if (glyph == HandheldGlyph)
        {
            // A drawing in a TextBlock, so it sits wherever glyphs do and takes the same color.
            var holder = Text("", size, brushKey);
            holder.HorizontalAlignment = HorizontalAlignment.Center;
            holder.Tag = HandheldGlyph;
            var path = new System.Windows.Shapes.Path
            {
                Data = HandheldShape, Stretch = Stretch.Uniform, Width = size * 1.35, Height = size * 0.95, SnapsToDevicePixels = true
            };
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = holder });
            holder.Inlines.Add(new System.Windows.Documents.InlineUIContainer(path) { BaselineAlignment = BaselineAlignment.Center });
            return holder;
        }
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

        var row = new Button { Style = StyleOf("RowButton"), Content = grid };
        // Narrator and other screen readers read the row's name and value, and the line under it as help.
        System.Windows.Automation.AutomationProperties.SetName(row, value != null ? label + ", " + value : label);
        if (subtitle != null) System.Windows.Automation.AutomationProperties.SetHelpText(row, subtitle);
        return row;
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

    /// <summary>
    /// Puts an Advanced style's dialog artwork behind a dialog box (the box's own fill goes see-through then).
    /// Returns what to add to the screen in place of the box; with a Basic style it looks exactly as before.
    /// </summary>
    public static FrameworkElement SkinBox(Border box)
    {
        var holder = new Grid
        {
            Width = box.Width, HorizontalAlignment = box.HorizontalAlignment, VerticalAlignment = box.VerticalAlignment,
            Margin = box.Margin, Effect = box.Effect
        };
        box.Width = double.NaN;
        box.Margin = new Thickness(0);
        box.Effect = null;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.VerticalAlignment = VerticalAlignment.Stretch;
        var art = new NineSlice();
        art.SetResourceReference(NineSlice.SourceProperty, "SkinDialog");
        art.SetResourceReference(NineSlice.SliceProperty, "SkinDialogSlice");
        art.SetResourceReference(NineSlice.ScaleProperty, "SkinScale");
        art.SetResourceReference(NineSlice.PixelProperty, "SkinPixel");
        holder.Children.Add(art);
        box.SetResourceReference(Border.BackgroundProperty, "SkinDialogFill");
        // Artwork with a title bar (Retro Windows, Luna XP): the text starts below it.
        if (Services.Skins.Current is { Dialog: not null, DialogPad: > 0 } titled)
            box.Padding = new Thickness(box.Padding.Left, box.Padding.Top + titled.DialogPad, box.Padding.Right, box.Padding.Bottom);
        holder.Children.Add(box);
        return holder;
    }

    /// <summary>
    /// An Advanced style's artwork ("panel", "button", "dialog" or "round") as a brush for a border's background,
    /// stretched to the border the nine-slice way; null with a Basic style or when the kit has no such picture.
    /// Used where a screen builds its own boxes instead of the shared button templates (power menu, quick settings).
    /// </summary>
    public static Brush? SkinBrush(FrameworkElement owner, string part)
    {
        if (Services.Skins.Current is not { } skin) return null;
        var (source, slice) = part switch
        {
            "panel" => (skin.Panel, skin.PanelSlice),
            "button" => (skin.Button, skin.ButtonSlice),
            "dialog" => (skin.Dialog, skin.DialogSlice),
            "round" => (skin.Round, new Thickness(0)),
            _ => (null, new Thickness(0))
        };
        if (source is not System.Windows.Media.Imaging.BitmapSource bitmap) return null;
        // A drawing the exact size of the area it fills, redrawn when the border changes size: no stretching, nothing cut off.
        // A Border paints its background inside its border lines, so the drawing is fitted to that inner area; fitted to
        // the outside it lost a few pixels on every edge (the art's own outline, on the Power menu and Quick Settings buttons).
        var brush = new DrawingBrush { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                                       ViewportUnits = BrushMappingMode.RelativeToBoundingBox, Viewport = new Rect(0, 0, 1, 1) };
        void Fit()
        {
            var edge = owner is Border b ? b.BorderThickness : new Thickness(0);
            double w = Math.Max(1, owner.ActualWidth - edge.Left - edge.Right), h = Math.Max(1, owner.ActualHeight - edge.Top - edge.Bottom);
            if (part == "round")
            {
                var group = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(group, skin.Pixel ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, w, h)));
                brush.Drawing = group;
            }
            else brush.Drawing = NineSlice.Drawing(bitmap, slice, skin.Scale, w, h, skin.Pixel);
            brush.Viewbox = new Rect(0, 0, w, h);
        }
        owner.SizeChanged += (_, _) => Fit();
        Fit();
        return brush;
    }

    /// <summary>A border's resting background: the style's artwork when it has some, otherwise the given brush resource.</summary>
    public static void SkinFill(Border border, Brush? art, string fallback)
    {
        SkinCorners(border, art != null);
        if (art != null) border.Background = art;
        else border.SetResourceReference(Border.BackgroundProperty, fallback);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, object> _usualCorners = new();

    /// <summary>
    /// The artwork draws its own shape, so while it's shown the border's rounding is turned off: rounded for the style
    /// (a circle on the Power menu) it cut the corners off the art. Back to the usual rounding for the highlight color.
    /// </summary>
    public static void SkinCorners(Border border, bool artShown)
    {
        if (!_usualCorners.TryGetValue(border, out var usual)) { usual = border.CornerRadius; _usualCorners.Add(border, usual); }
        border.CornerRadius = artShown ? new CornerRadius(0) : (CornerRadius)usual;
    }

    /// <summary>A switch drawing: a pill with a knob, filled with the highlight color when on.</summary>
    public static FrameworkElement Toggle(bool on)
    {
        // Advanced styles bring their own switch pictures.
        if (Services.Skins.Current is { } skin && (on ? skin.ToggleOn : skin.ToggleOff) is ImageSource picture)
        {
            var image = new Image
            {
                Source = picture, Height = 30, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = on ? "On" : "Off"
            };
            RenderOptions.SetBitmapScalingMode(image, skin.Pixel ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            return image;
        }
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
        if (Services.LookChrome.Design.Icons != "badge") return Services.LookChrome.Icon(glyph, size);
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
        // Looks that don't draw rows as cards keep their own spacing: rows on lines touch, separate cards keep a gap.
        string kind = Services.LookChrome.Design.Rows;
        if (kind != "card")
        {
            double gap = kind is "line" or "flat" ? 0 : 3;
            foreach (var row in children.OfType<Button>().Where(b => b.Style == rowStyle))
                row.Margin = new Thickness(row.Margin.Left, gap, row.Margin.Right, gap);
            return;
        }
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

    /// <summary>
    /// A panel button with an icon. The icon is drawn in the icon font, so it shows with every UI style's own text
    /// font (pixel and themed fonts have no icons of their own, and showed an empty box).
    /// </summary>
    public static Button PanelButton(string glyph, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
        line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Style = StyleOf("PanelButton"), Content = line };
    }

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
        if (Services.LookChrome.Design.Headers != "bold") return Services.LookChrome.Header(text);
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

    /// <summary>Settings > Home screen > Animations > "Punch-in when opening": a game or app punches in as it's opened.</summary>
    public static bool PunchIn { get; set; } = true;

    /// <summary>
    /// The "punch-in" played on a game or app as it's opened: it's pressed in, then punches forward past its size and
    /// settles back, about a third of a second. The task finishes at the forward peak, so whatever opens next (the
    /// game's page, the splash, the app) comes in right on the punch. Works on top of any highlight growth.
    /// </summary>
    public static Task PunchAsync(UIElement? element)
    {
        if (element == null || !PunchIn || Theme.ReduceMotion) return Task.CompletedTask;
        var parts = PartsOf(element);
        double size = Math.Max(0.5, parts.Scale.ScaleX); // a grown tile punches from its grown size
        var punch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(340) };
        punch.KeyFrames.Add(new EasingDoubleKeyFrame(size * 0.88, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        punch.KeyFrames.Add(new EasingDoubleKeyFrame(size * 1.13, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
        punch.KeyFrames.Add(new EasingDoubleKeyFrame(size, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, punch);
        parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, punch);
        // A quick brightening flash as it lands, for a bit of impact.
        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(340) };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(element.Opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(Math.Max(0.75, element.Opacity * 0.82), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(element.Opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340))));
        flash.FillBehavior = FillBehavior.Stop;
        element.BeginAnimation(UIElement.OpacityProperty, flash);
        return Task.Delay(190);
    }

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

    /// <summary>Page animations run at three quarters of their first length: they felt slow on a handheld.</summary>
    private static TimeSpan Paced(double ms) => TimeSpan.FromMilliseconds(ms * 0.75);

    /// <summary>A page or pop-up opening, in the style chosen in Settings.</summary>
    public static void OpenPage(FrameworkElement page, bool dialog)
    {
        if (Theme.ReduceMotion || PageStyle == "none") return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var appear = new DoubleAnimation(0, 1, Paced(dialog ? 160 : 220));
        // While it moves in, the page is drawn once and the picture is moved, instead of redrawing every part of
        // it (text, cards, shadows) each frame. It goes back to normal drawing as soon as it has arrived.
        if (page.CacheMode == null)
        {
            double scale = 1;
            try { scale = Math.Max(1, VisualTreeHelper.GetDpi(page).DpiScaleX); } catch (Exception) { }
            var cache = new BitmapCache(scale) { SnapsToDevicePixels = true };
            page.CacheMode = cache;
            appear.Completed += (_, _) => { if (ReferenceEquals(page.CacheMode, cache)) page.CacheMode = null; };
        }
        page.BeginAnimation(UIElement.OpacityProperty, appear);
        if (PageStyle == "fade") return;
        var parts = PartsOf(page);
        void Scale(double from, int ms, IEasingFunction e, bool x = true, bool y = true)
        {
            var a = new DoubleAnimation(from, 1, Paced(ms)) { EasingFunction = e };
            if (x) parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            if (y) parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        void Move(DependencyProperty axis, double from, int ms, IEasingFunction e) =>
            parts.Move.BeginAnimation(axis, new DoubleAnimation(from, 0, Paced(ms)) { EasingFunction = e });

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
                    new DoubleAnimation(-5, 0, Paced(420)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
                Scale(0.96, 420, ease);
                break;
            case "focus":
                // Comes into focus from a blur (skipped in Performance mode).
                if (!Perf.Active)
                {
                    var blur = new System.Windows.Media.Effects.BlurEffect { Radius = 18, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
                    page.Effect = blur;
                    var sharpen = new DoubleAnimation(18, 0, Paced(300)) { EasingFunction = ease };
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

    /// <summary>Highlight animations for the selected home tile (Settings > Home screen > Selected game).</summary>
    public static readonly (string Id, string Name)[] TileHighlights =
    {
        ("pulse", "Pulsing ring"), ("steady", "Steady ring"), ("grow", "Tile grows"), ("bounce", "Bounce"),
        ("lift", "Lift"), ("wobble", "Wobble"), ("breathe", "Breathe"), ("tilt", "Tilt"), ("glow", "Glow pulse"), ("shake", "Shake"),
        // 6.25: five more, made for SquareDeck.
        ("lockon", "Lock-on"), ("jelly", "Jelly"), ("float", "Float"), ("heartbeat", "Heartbeat"), ("rainbow", "Rainbow ring")
    };

    /// <summary>How much the highlight animation alone grows the tile (when the UI style doesn't).</summary>
    public static double HighlightGrow => TileStyle switch
    {
        "grow" => 1.05, "bounce" => 1.07, "lift" => 1.03, "wobble" => 1.04, "breathe" => 1.05, "tilt" => 1.05, "shake" => 1.03,
        "jelly" => 1.05, "float" => 1.03, "heartbeat" => 1.04, _ => 1
    };

    /// <summary>Counts each change of a tile's highlight, so a finished animation can tell whether it's still the current one.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, System.Runtime.CompilerServices.StrongBox<int>> TileTurns = new();

    public static void GrowTile(UIElement? tile, bool on)
    {
        if (tile == null) return;
        var turnBox = TileTurns.GetValue(tile, _ => new System.Runtime.CompilerServices.StrongBox<int>());
        int turn = ++turnBox.Value;
        var parts = PartsOf(tile);
        var scale = parts.Scale;
        double grow = SelectGrow > 1.001 ? SelectGrow : HighlightGrow;
        double target = on && !Theme.ReduceMotion ? grow : 1;
        Panel.SetZIndex(tile, on ? 1 : 0); // a grown tile sits over its neighbours
        // Big growth goes upward, into the space kept for the title, so the bottom of the tile isn't cut off.
        tile.RenderTransformOrigin = GrowOrigin ?? (grow > 1.1 ? new Point(0.5, 0.92) : new Point(0.5, 0.5));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        bool motion = on && !Theme.ReduceMotion;

        // Off: everything settles back.
        parts.Rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        parts.Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        parts.Move.BeginAnimation(TranslateTransform.XProperty, null);

        switch (motion ? TileStyle : "")
        {
            case "bounce":
            {
                var pop = new DoubleAnimation(target, TimeSpan.FromMilliseconds(460)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                return;
            }
            case "lift":
                parts.Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } });
                break;
            case "wobble":
            {
                var wob = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, a) in new[] { (0, 0.0), (90, -4.0), (180, 3.5), (270, -2.0), (360, 1.0), (450, 0.0) })
                    wob.KeyFrames.Add(new EasingDoubleKeyFrame(a, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                parts.Rotate.BeginAnimation(RotateTransform.AngleProperty, wob);
                break;
            }
            case "tilt":
                parts.Rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-2.5, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } });
                break;
            case "shake":
            {
                var shake = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, x) in new[] { (0, 0.0), (50, -6.0), (100, 6.0), (150, -4.0), (200, 4.0), (250, -2.0), (300, 0.0) })
                    shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                parts.Move.BeginAnimation(TranslateTransform.XProperty, shake);
                break;
            }
            case "jelly":
            {
                // Squash and stretch: wide and short, then tall and narrow, wobbling to rest like jelly.
                var x = new DoubleAnimationUsingKeyFrames();
                var y = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, sx, sy) in new[] { (0, 1.0, 1.0), (110, 1.14, 0.88), (220, 0.93, 1.08), (330, 1.05, 0.97), (430, 0.99, 1.01), (520, 1.0, 1.0) })
                {
                    var kt = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(target * sx, kt, new SineEase()));
                    y.KeyFrames.Add(new EasingDoubleKeyFrame(target * sy, kt, new SineEase()));
                }
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, x);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, y);
                return;
            }
            case "float":
            {
                // Lifts off once, then bobs gently up and down for as long as it stays selected, like it's floating on
                // water. The bob is its own endless back-and-forth starting where the lift ends, so it never jumps back
                // to the ground and lifts again (which looked like a hiccup every couple of seconds).
                var move = parts.Move;
                var lift = new DoubleAnimation(-12, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
                if (!Perf.Active)
                {
                    lift.Completed += (_, _) =>
                    {
                        // Only while it's still the selected one (moving on starts its own animation back down).
                        if (turnBox.Value != turn) return;
                        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, -6, TimeSpan.FromMilliseconds(1700))
                        {
                            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        });
                    };
                }
                move.BeginAnimation(TranslateTransform.YProperty, lift);
                break;
            }
            case "heartbeat":
            {
                // Two quick beats, a rest, and again: lub-dub.
                var beat = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                foreach (var (ms, k) in new[] { (0, 1.0), (110, 1.045), (220, 1.0), (330, 1.03), (470, 1.0), (1400, 1.0) })
                    beat.KeyFrames.Add(new EasingDoubleKeyFrame(target * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                if (Perf.Active) break;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
                return;
            }
            case "breathe":
            {
                // Grows, then gently breathes in and out while it stays selected.
                var breathe = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                breathe.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), ease));
                breathe.KeyFrames.Add(new EasingDoubleKeyFrame(target - 0.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1460)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                breathe.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2660)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                if (Perf.Active) break; // Performance mode: no endless animation
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                return;
            }
        }
        To(scale, ScaleTransform.ScaleXProperty, target, 220, ease);
        To(scale, ScaleTransform.ScaleYProperty, target, 220, ease);
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
        if (TileStyle == "glow" && !Theme.ReduceMotion && !Perf.Active && ring is FrameworkElement fe)
        {
            // A soft halo in the highlight color that swells and fades around the ring.
            var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
            var halo = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.9 };
            fe.Effect = halo;
            halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(8, 34, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            return;
        }
        if (TileStyle == "lockon" && !Theme.ReduceMotion && ring is FrameworkElement lockRing)
        {
            // The ring zooms in from wide, like a target locking on, overshoots a touch and holds steady.
            StopPulse(ring);
            var zoom = new ScaleTransform(1, 1);
            lockRing.RenderTransformOrigin = new Point(0.5, 0.5);
            lockRing.RenderTransform = zoom;
            var inward = new DoubleAnimation(1.4, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, inward);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, inward);
            ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            return;
        }
        if (TileStyle == "rainbow" && !Theme.ReduceMotion && !Perf.Active && ring is Border rainbowRing)
        {
            // The ring slowly cycles through the colors of the rainbow.
            var brush = new SolidColorBrush(Colors.Red);
            rainbowRing.BorderBrush = brush;
            var hues = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var colors = new[] { "#FF3B3B", "#FF9F1C", "#FFE53B", "#3BFF6E", "#2FE0FF", "#4B6BFF", "#C04BFF", "#FF3B3B" };
            for (int i = 0; i < colors.Length; i++)
                hues.KeyFrames.Add(new LinearColorKeyFrame((Color)ColorConverter.ConvertFromString(colors[i]), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 500))));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, hues);
            return;
        }
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
        // Undo a glow pulse: back to the ring's own effect from the UI style.
        if (ring is FrameworkElement fe && fe.Effect is System.Windows.Media.Effects.DropShadowEffect { IsFrozen: false })
        {
            if (fe.Name == "Ring") fe.SetResourceReference(UIElement.EffectProperty, "RingGlow");
            else fe.ClearValue(UIElement.EffectProperty);
        }
        ring.BeginAnimation(UIElement.OpacityProperty, null);
        ring.Opacity = 1;
        // Undo a lock-on zoom and a rainbow ring.
        if (ring.RenderTransform is ScaleTransform) ring.RenderTransform = Transform.Identity;
        if (ring is Border b && b.ReadLocalValue(Border.BorderBrushProperty) != DependencyProperty.UnsetValue) b.ClearValue(Border.BorderBrushProperty);
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
    // Index order is kept (settings store "preset:N"): the first twelve are the originals, the rest added in 5.9.
    public static readonly (string Color, string Symbol)[] Presets =
    {
        ("#E8453C", "\U0001F3AE"), ("#7B5BD6", "\U0001F47E"), ("#2B7DE0", "\U0001F680"), ("#F2A100", "\u2B50"),
        ("#2EB872", "\U0001F431"), ("#D9603B", "\U0001F436"), ("#C2410C", "\U0001F98A"), ("#3F4A5A", "\U0001F43C"),
        ("#0AB9C9", "\U0001F3A7"), ("#CA8A04", "\u26A1"), ("#DC2626", "\U0001F525"), ("#4338CA", "\U0001F319"),
        // Creatures
        ("#15803D", "\U0001F409"), ("#DB2777", "\U0001F984"), ("#65A30D", "\U0001F438"), ("#B45309", "\U0001F981"),
        ("#EA580C", "\U0001F42F"), ("#0F766E", "\U0001F427"), ("#7C2D12", "\U0001F989"), ("#9333EA", "\U0001F419"),
        ("#166534", "\U0001F996"), ("#475569", "\U0001F43A"), ("#A16207", "\U0001F43B"), ("#0369A1", "\U0001F988"),
        // Characters
        ("#334155", "\U0001F47B"), ("#0E7490", "\U0001F916"), ("#4D7C0F", "\U0001F47D"), ("#1F2937", "\U0001F480"),
        ("#6D28D9", "\U0001F9D9"), ("#111827", "\U0001F977"), ("#BE123C", "\U0001F383"), ("#7E22CE", "\U0001F451"),
        // Games
        ("#B91C1C", "\U0001F3B2"), ("#CA8A04", "\U0001F3C6"), ("#DC2626", "\U0001F3AF"), ("#1D4ED8", "\U0001F579"),
        ("#57534E", "\u2694"), ("#1E40AF", "\U0001F6E1"), ("#BE185D", "\U0001F48E"), ("#C2410C", "\U0001F3CE"),
        // Hobbies and things
        ("#15803D", "\u26BD"), ("#EA580C", "\U0001F3C0"), ("#9F1239", "\U0001F3B8"), ("#7C3AED", "\U0001F3B5"),
        ("#D97706", "\U0001F355"), ("#DB2777", "\U0001F369"), ("#0891B2", "\U0001F4F8"), ("#4F46E5", "\U0001F4DA"),
        // Nature
        ("#EC4899", "\U0001F338"), ("#0284C7", "\U0001F30A"), ("#16A34A", "\U0001F335"), ("#0EA5E9", "\u2744"),
        ("#F59E0B", "\u2600"), ("#8B5CF6", "\U0001F308"), ("#059669", "\U0001F340"), ("#B45309", "\U0001F341")
    };

    /// <summary>Groups for the picker: a name and the range of presets in it.</summary>
    public static readonly (string Name, int From, int Count)[] Groups =
    {
        ("Classics", 0, 12), ("Creatures", 12, 12), ("Characters", 24, 8), ("Games", 32, 8), ("Hobbies", 40, 8), ("Nature", 48, 8)
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

        // A soft diagonal gradient from the color to a deeper shade of it.
        var c = (Color)ColorConverter.ConvertFromString(color);
        circle.Fill = new LinearGradientBrush(
            Color.FromRgb((byte)Math.Min(255, c.R + 28), (byte)Math.Min(255, c.G + 28), (byte)Math.Min(255, c.B + 28)),
            Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62)), 45);
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

/// <summary>
/// A progress bar's fill that always matches its percentage. It's sized from the track's width at the moment it
/// changes, and again whenever the track itself changes size (text beside it growing, the dialog resizing). A slide to a
/// new point is played frame by frame here rather than by a WPF animation, and every frame reports where the bar is now
/// through <see cref="Shown"/>, so a percentage written from that moves with the bar instead of jumping ahead of it.
/// </summary>
public sealed class ProgressFill
{
    private readonly FrameworkElement _track;
    private readonly FrameworkElement _fill;
    private double _fraction;  // where the bar is going
    private double _now;       // where the bar is on screen
    private double _from;
    private DateTime _slideStart;
    private double _slideMs;
    private bool _sliding;
    private bool _set; // until the first Set, the fill is left alone (a "still starting" sweep may be using it)

    /// <summary>Called with the bar's fraction (0–1) each time it's drawn somewhere new: show the percentage from this.</summary>
    public Action<double>? Shown { get; set; }

    public ProgressFill(FrameworkElement track, FrameworkElement fill)
    {
        _track = track;
        _fill = fill;
        track.SizeChanged += (_, _) => { if (_set) Draw(); };
        fill.Unloaded += (_, _) => StopSliding();
    }

    /// <summary>Where the bar is heading.</summary>
    public double Fraction => _fraction;

    /// <summary>Where the bar is on screen right now (behind <see cref="Fraction"/> while it slides).</summary>
    public double Current => _now;

    /// <summary>The track's inner width (inside its border and padding).</summary>
    private double Full => _track is Border b
        ? Math.Max(0, b.ActualWidth - b.BorderThickness.Left - b.BorderThickness.Right - b.Padding.Left - b.Padding.Right)
        : Math.Max(0, _track.ActualWidth);

    /// <summary>Moves the fill to a fraction (0–1), sliding there over <paramref name="ms"/> milliseconds.</summary>
    public void Set(double fraction, int ms = 180)
    {
        _fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        _set = true;
        if (ms <= 0 || Theme.ReduceMotion || Full <= 0) { Snap(); return; }
        _from = _now;
        _slideStart = DateTime.UtcNow;
        _slideMs = ms;
        if (!_sliding)
        {
            _sliding = true;
            System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>Straight to a fraction (0–1), no slide.</summary>
    public void Snap(double fraction)
    {
        _fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        _set = true;
        Snap();
    }

    /// <summary>Straight to where it's heading, ending any slide.</summary>
    public void Snap()
    {
        StopSliding();
        _now = _fraction;
        Draw();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double t = Math.Clamp((DateTime.UtcNow - _slideStart).TotalMilliseconds / _slideMs, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3); // ease out
        _now = _from + (_fraction - _from) * eased;
        Draw();
        if (t >= 1) StopSliding();
    }

    private void StopSliding()
    {
        if (!_sliding) return;
        _sliding = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
    }

    private void Draw()
    {
        _fill.BeginAnimation(FrameworkElement.WidthProperty, null);
        if (Full > 0) _fill.Width = Full * _now;
        Shown?.Invoke(_now);
    }
}

