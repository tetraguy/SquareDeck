using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Choosing a look with a picture of each choice first: every option is a small drawing of how it will look (buttons,
/// the shelf behind them, the footer, the game art, a font...), with the current one marked. Selecting one applies it.
/// </summary>
public sealed class PreviewPickerScreen : GridScreen
{
    private readonly (string Id, string Name, string About)[] _options;
    private readonly Func<string> _current;
    private readonly Func<string, FrameworkElement> _draw;
    private readonly Action<string> _apply;
    private readonly string _note;
    public const double W = 300, H = 170;

    public PreviewPickerScreen(string title, string note, (string Id, string Name, string About)[] options, Func<string> current,
        Func<string, FrameworkElement> draw, Action<string> apply) : base(title, "{Select}  Use this      {Back}  Back")
    {
        _options = options;
        _current = current;
        _draw = draw;
        _apply = apply;
        _note = note;
    }

    public override void OnShown() => Build();

    private void Build()
    {
        Body.Children.Clear();
        Tiles.Children.Clear();
        var note = Ui.Text(_note, 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 0, 0, 14);
        Body.Children.Add(note);
        var grid = new WrapPanel();
        Button? focus = null;
        foreach (var option in _options)
        {
            var o = option;
            bool chosen = o.Id == _current();
            var face = new Grid { Width = W, Height = H, ClipToBounds = true, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12) };
            var back = new Border();
            back.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            face.Children.Add(back);
            var drawing = _draw(o.Id);
            face.Children.Add(drawing);
            if (chosen)
            {
                var ring = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12) };
                ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                face.Children.Add(ring);
                var badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var t = new TextBlock { Text = "✓ In use", FontSize = 12, FontWeight = FontWeights.SemiBold };
                t.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
                badge.Child = t;
                face.Children.Add(badge);
            }
            var tile = AddTile(face, o.Name, W, () =>
            {
                _apply(o.Id);
                UiSound.Select();
                Build();
            }, into: grid);
            tile.ToolTip = o.About;
            if (chosen || focus == null && o.Id == _options[0].Id) focus = tile;
            if (chosen) focus = tile;
        }
        Body.Children.Add(grid);
        FocusLater(focus);
    }

    // ---------------------------------------------------------------- drawings

    private static Color Accent => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    private static Border Box(double w, double h, double corner, Brush? fill = null, string? fillKey = null)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(corner) };
        if (fill != null) b.Background = fill; else b.SetResourceReference(Border.BackgroundProperty, fillKey ?? "TileBrush");
        return b;
    }

    /// <summary>A row of four home buttons in a style (the drawing behind the button-style choices).</summary>
    public static FrameworkElement Buttons(string id)
    {
        if (id == "style") return Caption("Whatever your UI style uses");
        string[] glyphs = { "", "", "", "" };
        Color[] colors = { Color.FromRgb(0x0A, 0xB9, 0xC9), Color.FromRgb(0xE8, 0x45, 0x3C), Color.FromRgb(0xF2, 0xA1, 0x00), Color.FromRgb(0x7B, 0x5B, 0xD6) };
        string[] names = { "Home", "Favorites", "Apps", "Social" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            var (w, h, corner) = id switch
            {
                "square" => (52.0, 52.0, 12.0), "small" => (40.0, 40.0, 20.0), "card" => (60.0, 56.0, 10.0), "tab" => (66.0, 28.0, 14.0),
                "large" => (64.0, 64.0, 32.0), "soft" => (54.0, 54.0, 19.0), "wide" => (74.0, 50.0, 10.0), _ => (54.0, 54.0, 27.0)
            };
            var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(corner), Margin = new Thickness(id == "tab" ? 2 : id == "large" ? 3 : 5) };
            var a = Accent;
            if (id == "outline") { b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(2); b.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush"); }
            else if (id == "accent") { b.Background = new SolidColorBrush(Color.FromArgb(0xE6, a.R, a.G, a.B)); b.Effect = new DropShadowEffect { Color = a, BlurRadius = 10, ShadowDepth = 1, Opacity = 0.5 }; }
            else if (id == "neon") { b.Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)); b.BorderThickness = new Thickness(2); b.BorderBrush = new SolidColorBrush(a); b.Effect = new DropShadowEffect { Color = a, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.95 }; }
            else if (id == "tab") b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            else b.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var inner = new Grid();
            var icon = new TextBlock { Text = glyphs[i], FontFamily = Ui.IconFont, FontSize = id is "small" or "tab" ? 13 : id == "large" ? 24 : 20, Foreground = id == "accent" ? Brushes.White : new SolidColorBrush(colors[i]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (id is "card" or "tab" or "wide")
            {
                var stack = new StackPanel { Orientation = id == "tab" ? Orientation.Horizontal : Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(icon);
                var label = Ui.Text(names[i], 9, "FgBrush");
                label.Margin = id == "tab" ? new Thickness(4, 0, 0, 0) : new Thickness(0, 3, 0, 0);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(label);
                inner.Children.Add(stack);
            }
            else inner.Children.Add(icon);
            if (id == "glossy")
                inner.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(corner, corner, 4, 4), VerticalAlignment = VerticalAlignment.Top, Height = h * 0.48, Margin = new Thickness(3, 2, 3, 0),
                    Background = new LinearGradientBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 90)
                });
            b.Child = inner;
            row.Children.Add(b);
        }
        return row;
    }

    /// <summary>The buttons sitting on a shelf in a style.</summary>
    public static FrameworkElement Shelf(string id)
    {
        if (id == "style") return Caption("Whatever your UI style uses");
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var shelf = new Border { CornerRadius = new CornerRadius(id is "bevel" ? 3 : id == "sticker" ? 16 : id == "underline" ? 0 : 34), Margin = new Thickness(-14, -10, -14, -10) };
        var a = Accent;
        switch (id)
        {
            case "smoke":
                shelf.Background = new SolidColorBrush(Color.FromArgb(0xA8, 0x0C, 0x0E, 0x14));
                shelf.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)); shelf.BorderThickness = new Thickness(1);
                shelf.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 };
                break;
            case "gradient":
                shelf.Background = new LinearGradientBrush(Color.FromArgb(0xC0, a.R, a.G, a.B), Color.FromArgb(0x30, a.R, a.G, a.B), 45);
                shelf.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)); shelf.BorderThickness = new Thickness(1);
                break;
            case "bevel":
                shelf.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
                shelf.BorderBrush = new LinearGradientBrush(Colors.White, Color.FromRgb(0x40, 0x40, 0x40), 45); shelf.BorderThickness = new Thickness(3);
                break;
            case "underline":
                shelf.BorderBrush = new SolidColorBrush(a); shelf.BorderThickness = new Thickness(0, 0, 0, 4);
                break;
            case "sticker":
                shelf.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xF7, 0xEE));
                shelf.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)); shelf.BorderThickness = new Thickness(2.5);
                shelf.Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 5, Direction = 315, Opacity = 0.9, Color = Colors.Black };
                break;
            case "glass":
                shelf.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                shelf.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)); shelf.BorderThickness = new Thickness(1.2);
                shelf.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3 };
                break;
            case "solid":
                shelf.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
                shelf.SetResourceReference(Border.BorderBrushProperty, "PanelStroke"); shelf.BorderThickness = new Thickness(1);
                shelf.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35 };
                break;
            case "outline":
                shelf.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, a.R, a.G, a.B)); shelf.BorderThickness = new Thickness(2);
                break;
            case "glow":
                shelf.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x08, 0x08, 0x10));
                shelf.BorderBrush = new SolidColorBrush(a); shelf.BorderThickness = new Thickness(1.5);
                shelf.Effect = new DropShadowEffect { Color = a, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.8 };
                break;
        }
        if (id != "none") grid.Children.Add(shelf);
        var buttons = Buttons("small");
        grid.Children.Add(buttons);
        return Scene(grid, wallpaper: true);
    }

    /// <summary>A little home screen with its footer in a style.</summary>
    public static FrameworkElement Footer(string id)
    {
        if (id == "style") return Caption("Whatever your UI style uses");
        var grid = new Grid();
        // Three tiles to suggest the home screen.
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 30, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < 3; i++) tiles.Children.Add(new Border { Width = 66, Height = 66, Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(8), Child = null, Background = i == 0 ? new SolidColorBrush(Accent) : null });
        foreach (Border t in tiles.Children) if (t.Background == null) t.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(tiles);
        var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        foreach (var (k, label) in new[] { ("A", "Start"), ("B", "Back") })
        {
            var key = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 0, 4, 0), Background = new SolidColorBrush(k == "A" ? Color.FromRgb(0x2E, 0xB8, 0x72) : Color.FromRgb(0xE8, 0x45, 0x3C)),
                                   Child = new TextBlock { Text = k, FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            hints.Children.Add(key);
            var text = Ui.Text(label, 11, "FgBrush");
            if (id == "taskbar") text.Foreground = Brushes.Black;
            if (id == "accentbar") text.Foreground = Brushes.White;
            hints.Children.Add(text);
        }
        var bar = new Grid { Height = 34, VerticalAlignment = VerticalAlignment.Bottom };
        switch (id)
        {
            case "line":
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 12, 0), Opacity = 0.6 };
                line.SetResourceReference(Border.BackgroundProperty, "FgBrush");
                bar.Children.Add(line);
                break;
            case "bar": bar.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x0D, 0x12)); break;
            case "glassbar": bar.Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)); break;
            case "pill":
                bar.Children.Add(new Border { Margin = new Thickness(14, 2, 14, 6), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)),
                                              BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) });
                break;
            case "fade":
                bar.Height = 46;
                bar.Background = new LinearGradientBrush(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0xC8, 0, 0, 0), 90);
                break;
            case "accentbar": bar.Background = new SolidColorBrush(Color.FromArgb(0xE0, Accent.R, Accent.G, Accent.B)); break;
            case "outlinepill":
                bar.Children.Add(new Border { Margin = new Thickness(14, 2, 14, 6), CornerRadius = new CornerRadius(14), BorderBrush = new SolidColorBrush(Accent), BorderThickness = new Thickness(1.5) });
                break;
            case "floatbar":
            {
                var fb = new Border { Margin = new Thickness(14, 2, 14, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45 } };
                fb.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
                fb.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
                bar.Children.Add(fb);
                break;
            }
            case "neonline":
                bar.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 12, 0), Background = new SolidColorBrush(Accent),
                                              Effect = new DropShadowEffect { Color = Accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 1 } });
                break;
            case "taskbar":
                bar.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
                bar.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White });
                break;
        }
        bar.Children.Add(hints);
        grid.Children.Add(bar);
        return Scene(grid, wallpaper: true);
    }

    /// <summary>A few game tiles in an art shape and size.</summary>
    public static FrameworkElement Art(string shape, string size)
    {
        if (shape == "style" && size == "style") return Caption("Whatever your UI style uses");
        double k = size switch { "small" => 0.8, "large" => 1.2, _ => 1.0 };
        var (w, h) = shape switch { "tall" => (52.0, 78.0), "wide" => (104.0, 49.0), _ => (66.0, 66.0) };
        w *= k; h *= k;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        Color[] colors = { Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xE8, 0x45, 0x3C), Color.FromRgb(0x2E, 0xB8, 0x72), Color.FromRgb(0xF2, 0xA1, 0x00), Color.FromRgb(0x7B, 0x5B, 0xD6) };
        for (int i = 0; i < 6; i++)
        {
            var c = colors[i % colors.Length];
            var tile = new Border
            {
                Width = w, Height = h, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush(c, Color.FromArgb(0xFF, (byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2)), 60)
            };
            if (i == 0)
            {
                tile.BorderThickness = new Thickness(3);
                tile.BorderBrush = Brushes.White;
                tile.RenderTransform = new ScaleTransform(1.08, 1.08, w / 2, h / 2);
            }
            row.Children.Add(tile);
        }
        return row;
    }

    /// <summary>The sample text in a font.</summary>
    public static FrameworkElement Font(string font)
    {
        var family = font == "style" ? (FontFamily)Application.Current.MainWindow.FontFamily : new FontFamily(font);
        var stack = new StackPanel { Margin = new Thickness(18, 16, 14, 0) };
        var big = new TextBlock { Text = "Play Halo", FontFamily = family, FontSize = 30, FontWeight = FontWeights.SemiBold };
        big.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        stack.Children.Add(big);
        var small = new TextBlock { Text = "Settings · Library · 12:45 PM", FontFamily = family, FontSize = 15, Margin = new Thickness(0, 4, 0, 0) };
        small.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
        stack.Children.Add(small);
        var abc = new TextBlock { Text = "AaBbCc 0123456789", FontFamily = family, FontSize = 18, Margin = new Thickness(0, 10, 0, 0) };
        abc.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        stack.Children.Add(abc);
        return stack;
    }

    private static FrameworkElement Scene(UIElement content, bool wallpaper)
    {
        var grid = new Grid();
        if (wallpaper)
        {
            var a = Accent;
            grid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x70, a.R, a.G, a.B), Color.FromArgb(0x10, a.R, a.G, a.B), 45) });
        }
        grid.Children.Add(content);
        return grid;
    }

    private static FrameworkElement Caption(string text)
    {
        var t = Ui.Text(text, 15, "SubtleBrush");
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.TextWrapping = TextWrapping.Wrap;
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(20);
        return t;
    }
}
