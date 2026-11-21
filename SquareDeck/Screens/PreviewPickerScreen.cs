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

    /// <summary>A few game tiles in an art shape and size.</summary>
    public static FrameworkElement Art(string shape, string size)
    {
        if (shape == "style" && size == "style") return Caption("SquareDeck's standard look");
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
        var family = font == "style" ? (FontFamily)Application.Current.MainWindow.FontFamily : Services.AppFonts.Family(font);
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
