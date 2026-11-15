using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        if (value != null)
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
        IsHitTestVisible = false;
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

    private void Rebuild()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var items = Hints.Parse(_template);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            bool first = true;
            foreach (var (text, isKey) in item.Keys)
            {
                if (isKey)
                {
                    row.Children.Add(Pill(text, first ? 0 : 6));
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
        Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 1160, Child = row };
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
    public static void StartPulse(UIElement? ring)
    {
        if (ring == null) return;
        if (Theme.ReduceMotion) { StopPulse(ring); return; }
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
