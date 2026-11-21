using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace SquareDeck.Services;

/// <summary>
/// The parts of a look that every list and page share: how list rows, their icons and the headers over them are
/// drawn (see <see cref="PageDesign.Rows"/>, <see cref="PageDesign.Icons"/>, <see cref="PageDesign.Headers"/>).
/// Choosing a look swaps the app's "RowButton" style, so Settings and every other list take on its shape.
/// </summary>
public static class LookChrome
{
    private static Style? _original;
    private static readonly Dictionary<string, Style> Built = new();

    public static PageDesign Design => Looks.Current.Design;

    /// <summary>Puts the look's row style in place (Switch gets the original back).</summary>
    public static void Apply(Look look)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        _original ??= res["RowButton"] as Style;
        string kind = look.Design.Rows;
        ApplyTile(res, kind);
        if (kind == "card" || _original == null) { if (_original != null) res["RowButton"] = _original; return; }
        if (!Built.TryGetValue(kind, out var style))
        {
            try { style = Build(kind); }
            catch (Exception) { style = _original; }
            Built[kind] = style;
        }
        res["RowButton"] = style;
    }

    private static Style? _originalTile;
    private static readonly Dictionary<string, Style> BuiltTiles = new();

    /// <summary>The highlight ring around tiles (stores, apps, cards) in the look's way; Switch keeps the original.</summary>
    private static void ApplyTile(ResourceDictionary res, string kind)
    {
        _originalTile ??= res["TileButton"] as Style;
        if (kind == "card" || _originalTile == null) { if (_originalTile != null) res["TileButton"] = _originalTile; return; }
        if (!BuiltTiles.TryGetValue(kind, out var style))
        {
            var (corner, thickness, brush, glow) = kind switch
            {
                "line" => (4, 2, "{DynamicResource AccentBrush}", true),
                "glass" => (16, 3, "#F2FFFFFF", false),
                "outline" => (12, 3, "{DynamicResource AccentBrush}", true),
                "flat" => (6, 3, "#FFFFFFFF", false),
                "block" => (4, 3, "{DynamicResource AccentBrush}", false),
                "solid" => (8, 4, "#FFFFFFFF", false),
                _ => (24, 3, "#F2FFFFFF", true) // pill
            };
            string effect = glow
                ? kind == "pill"
                    ? @"<Setter TargetName=""B"" Property=""Effect""><Setter.Value><DropShadowEffect Color=""White"" BlurRadius=""24"" ShadowDepth=""0"" Opacity=""0.5""/></Setter.Value></Setter>"
                    : @"<Setter TargetName=""B"" Property=""Effect"" Value=""{DynamicResource RingGlow}""/>"
                : "";
            string xaml = $@"
<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Grid Background=""Transparent"">
    <Border x:Name=""B"" CornerRadius=""{corner}"" BorderThickness=""{thickness}"" BorderBrush=""Transparent""/>
    <ContentPresenter Margin=""8""/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsKeyboardFocused"" Value=""True"">
      <Setter TargetName=""B"" Property=""BorderBrush"" Value=""{brush}""/>
      {effect}
    </Trigger>
    <Trigger Property=""IsMouseOver"" Value=""True"">
      <Setter TargetName=""B"" Property=""Opacity"" Value=""0.9""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            try
            {
                style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(xaml)));
                style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
                style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            }
            catch (Exception) { style = _originalTile; }
            BuiltTiles[kind] = style;
        }
        res["TileButton"] = style;
    }

    /// <summary>Tiles' picture boxes: corner, fill and edge in the look's way (GridScreen.ImageBox / GlyphBox).</summary>
    public static double TileCorner(double usual) => Design.Rows switch
    {
        "line" => 2, "glass" => 12, "outline" => 8, "flat" => 3, "block" => 2, "solid" => 4, "pill" => 18, _ => usual
    };

    public static void StyleTileBox(Border box)
    {
        switch (Design.Rows)
        {
            case "line":
                box.Background = new SolidColorBrush(Color.FromArgb(0x99, 0x05, 0x06, 0x09));
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                box.BorderThickness = new Thickness(1);
                break;
            case "glass":
                box.Background = new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00));
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
                box.BorderThickness = new Thickness(1.5);
                break;
            case "outline":
                box.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0C, 0x17, 0x30));
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x8E, 0xC5, 0xFF));
                box.BorderThickness = new Thickness(1.5);
                break;
            case "flat":
                box.Background = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2E));
                break;
            case "block":
                box.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1C));
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                box.BorderThickness = new Thickness(1);
                break;
            case "solid":
                box.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x34));
                break;
            case "pill":
                box.Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                break;
        }
    }

    /// <summary>The name under a tile in the look's way.</summary>
    public static void StyleTileCaption(TextBlock caption)
    {
        switch (Design.Rows)
        {
            case "line":
                caption.FontWeight = FontWeights.Light;
                caption.FontSize = 16;
                break;
            case "block":
                caption.Text = caption.Text.ToUpperInvariant();
                caption.FontSize = 13;
                break;
            case "solid":
                caption.FontWeight = FontWeights.SemiBold;
                break;
            case "pill":
                caption.FontWeight = FontWeights.Light;
                caption.FontSize = 17;
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                break;
        }
    }

    private static Style Build(string kind)
    {
        // Each kind: the row's surface (B), a glow layer (G) shown on the row you're on, and a bar (Bar) at its left
        // for the one you're on or the chosen entry of a list (Tag = selected).
        var (surface, focus, padding, margin) = kind switch
        {
            "line" => (
                @"<Border x:Name=""B"" Background=""Transparent"" BorderBrush=""#24FFFFFF"" BorderThickness=""0,0,0,1"" Padding=""{0}"">",
                @"<Setter TargetName=""G"" Property=""Visibility"" Value=""Visible""/>
                  <Setter TargetName=""Bar"" Property=""Visibility"" Value=""Visible""/>
                  <Setter TargetName=""B"" Property=""BorderBrush"" Value=""{DynamicResource AccentBrush}""/>",
                "18,13", "0,0"),
            "glass" => (
                @"<Border x:Name=""B"" Background=""#66000000"" BorderBrush=""#22FFFFFF"" BorderThickness=""1.5"" CornerRadius=""12"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""Background"" Value=""#40FFFFFF""/>
                  <Setter TargetName=""B"" Property=""BorderBrush"" Value=""#F2FFFFFF""/>
                  <Setter TargetName=""B"" Property=""BorderThickness"" Value=""2.5""/>",
                "20,13", "0,4"),
            "outline" => (
                @"<Border x:Name=""B"" Background=""#C80C1730"" BorderBrush=""#4D8EC5FF"" BorderThickness=""1.5"" CornerRadius=""8"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""BorderBrush"" Value=""{DynamicResource AccentBrush}""/>
                  <Setter TargetName=""B"" Property=""BorderThickness"" Value=""2.5""/>
                  <Setter TargetName=""B"" Property=""Effect"" Value=""{DynamicResource RingGlow}""/>
                  <Setter TargetName=""G"" Property=""Visibility"" Value=""Visible""/>",
                "18,12", "0,3"),
            "flat" => (
                @"<Border x:Name=""B"" Background=""Transparent"" BorderBrush=""#1AFFFFFF"" BorderThickness=""0,0,0,1"" CornerRadius=""3"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""Background"" Value=""#3DFFFFFF""/>
                  <Setter TargetName=""B"" Property=""BorderBrush"" Value=""Transparent""/>",
                "16,12", "0,0"),
            "block" => (
                @"<Border x:Name=""B"" Background=""#D916181C"" BorderBrush=""#14FFFFFF"" BorderThickness=""1"" CornerRadius=""2"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""BorderBrush"" Value=""{DynamicResource AccentBrush}""/>
                  <Setter TargetName=""B"" Property=""BorderThickness"" Value=""2""/>
                  <Setter TargetName=""Bar"" Property=""Visibility"" Value=""Visible""/>
                  <Setter TargetName=""G"" Property=""Visibility"" Value=""Visible""/>",
                "26,12", "0,3"),
            "solid" => (
                @"<Border x:Name=""B"" Background=""#E6262A2F"" BorderBrush=""Transparent"" BorderThickness=""3"" CornerRadius=""4"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""BorderBrush"" Value=""#FFFFFFFF""/>
                  <Setter TargetName=""B"" Property=""Background"" Value=""#FF33383E""/>",
                "18,11", "0,3"),
            "pill" => (
                @"<Border x:Name=""B"" Background=""#12FFFFFF"" BorderBrush=""Transparent"" BorderThickness=""2"" CornerRadius=""16"" Padding=""{0}"">",
                @"<Setter TargetName=""B"" Property=""Background"" Value=""#30FFFFFF""/>
                  <Setter TargetName=""B"" Property=""BorderBrush"" Value=""#F2FFFFFF""/>
                  <Setter TargetName=""B"" Property=""Effect""><Setter.Value><DropShadowEffect Color=""White"" BlurRadius=""22"" ShadowDepth=""0"" Opacity=""0.45""/></Setter.Value></Setter>",
                "20,12", "0,4"),
            _ => throw new ArgumentException(kind)
        };
        string bar = kind switch
        {
            "line" => @"<Border x:Name=""Bar"" Width=""3"" HorizontalAlignment=""Left"" Background=""{DynamicResource AccentBrush}"" Visibility=""Collapsed""/>",
            "block" => @"<Border x:Name=""Bar"" Width=""8"" HorizontalAlignment=""Left"" Background=""{DynamicResource AccentBrush}"" Visibility=""Collapsed""/>",
            _ => @"<Border x:Name=""Bar"" Width=""4"" CornerRadius=""2"" Margin=""6,10,0,10"" HorizontalAlignment=""Left"" Background=""{DynamicResource AccentBrush}"" Visibility=""Collapsed""/>"
        };
        string glow = kind == "line"
            ? @"<Rectangle x:Name=""G"" Visibility=""Collapsed"" IsHitTestVisible=""False""><Rectangle.Fill><LinearGradientBrush StartPoint=""0,0.5"" EndPoint=""1,0.5""><GradientStop Color=""#55CD0057"" Offset=""0""/><GradientStop Color=""#00CD0057"" Offset=""0.85""/></LinearGradientBrush></Rectangle.Fill></Rectangle>"
            : @"<Border x:Name=""G"" Visibility=""Collapsed"" IsHitTestVisible=""False"" Background=""{DynamicResource AccentBrush}"" Opacity=""0.12"" CornerRadius=""8""/>";

        string xaml = $@"
<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Grid>
    {glow}
    {string.Format(surface, padding)}
      <ContentPresenter HorizontalAlignment=""{{TemplateBinding HorizontalContentAlignment}}"" VerticalAlignment=""Center""/>
    </Border>
    {bar}
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsMouseOver"" Value=""True"">
      <Setter TargetName=""B"" Property=""Opacity"" Value=""0.92""/>
    </Trigger>
    <Trigger Property=""IsKeyboardFocused"" Value=""True"">
      {focus}
    </Trigger>
    <Trigger Property=""Tag"" Value=""selected"">
      <Setter TargetName=""Bar"" Property=""Visibility"" Value=""Visible""/>
      <Setter Property=""Foreground"" Value=""{{DynamicResource AccentBrush}}""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(xaml)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("FgBrush")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 20.0));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, (Thickness)new ThicknessConverter().ConvertFromInvariantString(margin)!));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }

    /// <summary>A row's icon in the look's way (Ui.IconBadge).</summary>
    public static Grid Icon(string glyph, double size)
    {
        var badge = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        string kind = Design.Icons;
        string mark = "AccentBrush";
        switch (kind)
        {
            case "plain":
                mark = "FgBrush";
                badge.Width = size * 0.8;
                break;
            case "outline":
            {
                var ring = new Border { CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.5) };
                ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                badge.Children.Add(ring);
                break;
            }
            case "square":
            {
                var block = new Border { CornerRadius = new CornerRadius(3) };
                block.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                badge.Children.Add(block);
                mark = "BgBrush";
                break;
            }
            case "glass":
                badge.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.3), Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                                                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) });
                mark = "FgBrush";
                break;
            default:
            {
                var tint = new Border { CornerRadius = new CornerRadius(size * 0.27), Opacity = 0.16 };
                tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                badge.Children.Add(tint);
                break;
            }
        }
        var g = Ui.Glyph(glyph, kind == "plain" ? size * 0.55 : size * 0.5, mark);
        g.HorizontalAlignment = HorizontalAlignment.Center;
        g.VerticalAlignment = VerticalAlignment.Center;
        badge.Children.Add(g);
        return badge;
    }

    /// <summary>"Look and feel" → "L O O K   A N D   F E E L".</summary>
    public static string Spaced(string text) =>
        string.Join("   ", text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => string.Join(" ", w.ToCharArray())));

    /// <summary>A header over a group of rows, in the look's way (Ui.Header).</summary>
    public static TextBlock Header(string text)
    {
        TextBlock t;
        switch (Design.Headers)
        {
            case "spaced":
                t = Ui.Text(Spaced(text), 13, "SubtleBrush");
                t.Margin = new Thickness(4, 30, 0, 10);
                break;
            case "caps":
                t = Ui.Text(text.ToUpperInvariant(), 13, Looks.Current.Id == "solaris" ? "AccentBrush" : "SubtleBrush", bold: true);
                t.Margin = new Thickness(4, 26, 0, 8);
                break;
            case "accent":
                t = Ui.Text(text, 18, "AccentBrush", bold: true);
                t.Margin = new Thickness(6, 26, 0, 10);
                break;
            case "light":
                t = Ui.Text(text, 21, "FgBrush");
                t.FontWeight = FontWeights.Light;
                t.Margin = new Thickness(4, 26, 0, 10);
                break;
            default:
                t = Ui.Text(text, 17, "FgBrush", bold: true);
                t.Opacity = 0.85;
                t.Margin = new Thickness(6, 26, 0, 10);
                break;
        }
        return t;
    }

    /// <summary>The banner at the top of a Settings section, in the look's way.</summary>
    public static UIElement Banner(string glyph, string name, string? about)
    {
        var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        TextBlock About(double size)
        {
            var sub = Ui.Text(about ?? "", size, "SubtleBrush");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.TextTrimming = TextTrimming.None;
            sub.Margin = new Thickness(0, 6, 0, 0);
            sub.Visibility = about == null ? Visibility.Collapsed : Visibility.Visible;
            return sub;
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        switch (Design.Banner)
        {
            case "spaced":
            {
                // ubiquity: thin spaced capitals, a short accent line, no panel.
                var title = Ui.Text(Spaced(name), 28, "FgBrush");
                title.FontWeight = FontWeights.Light;
                text.Children.Add(title);
                text.Children.Add(new Border { Width = 64, Height = 2, Margin = new Thickness(2, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(a) });
                text.Children.Add(About(15));
                text.Margin = new Thickness(4, 6, 0, 10);
                return text;
            }
            case "hero":
            {
                // Aniki: a large symbol and big bold name on a dark frosted card with a stripe of the accent.
                var row = new DockPanel();
                var icon = Ui.Glyph(glyph, 46, "FgBrush");
                icon.Margin = new Thickness(0, 0, 24, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
                text.Children.Add(Ui.Text(name, 40, "FgBrush", bold: true));
                text.Children.Add(About(16));
                row.Children.Add(text);
                return new Border
                {
                    Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(28, 22, 28, 22), Margin = new Thickness(0, 0, 0, 8),
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), BorderThickness = new Thickness(5, 0, 0, 0), BorderBrush = new SolidColorBrush(a)
                };
            }
            case "frame":
            {
                // J Hero: a navy panel with a blue edge, the symbol in a ring.
                var row = new DockPanel();
                var ring = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(a), Margin = new Thickness(0, 0, 18, 0) };
                var icon = Ui.Glyph(glyph, 26, "AccentBrush");
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                ring.Child = icon;
                DockPanel.SetDock(ring, Dock.Left);
                row.Children.Add(ring);
                text.Children.Add(Ui.Text(name, 30, "FgBrush", bold: true));
                text.Children.Add(About(15));
                row.Children.Add(text);
                return new Border
                {
                    Child = row, CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 16, 22, 16), Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0C, 0x17, 0x30)), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, a.R, a.G, a.B))
                };
            }
            case "plain":
            {
                // Steam-console style: the name in plain type over a hairline, as at the top of a settings pane.
                text.Children.Add(Ui.Text(name, 28, "FgBrush", bold: true));
                text.Children.Add(About(15));
                text.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 14, 0, 4), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
                text.Margin = new Thickness(2, 4, 0, 4);
                return text;
            }
            case "block":
            {
                // Solaris: a solid square of the accent with the symbol cut out of it, the name in light capitals.
                var row = new DockPanel();
                var block = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(a), Margin = new Thickness(0, 0, 20, 0) };
                var icon = Ui.Glyph(glyph, 28, "BgBrush");
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                block.Child = icon;
                DockPanel.SetDock(block, Dock.Left);
                row.Children.Add(block);
                var title = Ui.Text(name.ToUpperInvariant(), 34, "FgBrush");
                title.FontWeight = FontWeights.Light;
                text.Children.Add(title);
                text.Children.Add(About(15));
                row.Children.Add(text);
                row.Margin = new Thickness(0, 4, 0, 10);
                return row;
            }
            case "light":
            {
                // Console dashboards: just the name, large and light.
                var title = Ui.Text(name, 40, "FgBrush");
                title.FontWeight = FontWeights.Light;
                text.Children.Add(title);
                text.Children.Add(About(16));
                text.Margin = new Thickness(4, 0, 0, 12);
                return text;
            }
        }
        return null!; // "badge": the caller draws the usual one
    }

    // ---------------------------------------------------------------- buttons on sheets (quick settings, power)

    /// <summary>
    /// Paints a square or round button on a sheet (a quick-settings tile, a power option) in the look's way:
    /// resting, switched on, or with the highlight. False for the original look, which paints its own.
    /// </summary>
    public static bool PaintFace(Border face, IEnumerable<TextBlock> texts, bool on, bool focused, bool round = false)
    {
        string kind = Design.Rows;
        if (kind == "card") return false;
        Brush S(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        Brush fill, edge = Brushes.Transparent;
        double edgeWidth = 0, corner;
        string fore = "FgBrush";
        Brush? foreBrush = null;
        System.Windows.Media.Effects.Effect? glow = null;
        switch (kind)
        {
            case "line":
                corner = 2;
                fill = on ? S(0x50, accent.R, accent.G, accent.B) : S(0x14, 0xFF, 0xFF, 0xFF);
                edge = focused ? new SolidColorBrush(accent) : S(0x30, 0xFF, 0xFF, 0xFF); edgeWidth = focused ? 2 : 1;
                if (focused) glow = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 };
                break;
            case "glass":
                corner = 16;
                fill = on ? S(0xEE, 0xFF, 0xFF, 0xFF) : S(0x66, 0, 0, 0);
                if (on) foreBrush = S(0xFF, 0x11, 0x11, 0x14);
                edge = focused ? S(0xF2, 0xFF, 0xFF, 0xFF) : S(0x2E, 0xFF, 0xFF, 0xFF); edgeWidth = focused ? 3 : 1.5;
                break;
            case "outline":
                corner = 10;
                fill = on ? new SolidColorBrush(accent) : S(0xC8, 0x0C, 0x17, 0x30);
                if (on) fore = "BgBrush";
                edge = focused ? new SolidColorBrush(accent) : S(0x4D, 0x8E, 0xC5, 0xFF); edgeWidth = focused ? 3 : 1.5;
                if (focused) glow = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 };
                break;
            case "flat":
                corner = 4;
                fill = on ? new SolidColorBrush(accent) : focused ? S(0xFF, 0x3D, 0x44, 0x50) : S(0xFF, 0x23, 0x26, 0x2E);
                edge = focused ? Brushes.White : Brushes.Transparent; edgeWidth = 2;
                break;
            case "block":
                corner = 2;
                fill = on ? new SolidColorBrush(accent) : S(0xFF, 0x16, 0x18, 0x1C);
                if (on) fore = "BgBrush";
                edge = focused ? new SolidColorBrush(accent) : S(0x1A, 0xFF, 0xFF, 0xFF); edgeWidth = focused ? 3 : 1;
                break;
            case "solid":
                corner = 4;
                fill = on ? new SolidColorBrush(accent) : focused ? S(0xFF, 0x33, 0x38, 0x3E) : S(0xFF, 0x26, 0x2A, 0x2F);
                edge = focused ? Brushes.White : Brushes.Transparent; edgeWidth = 3;
                break;
            default: // pill
                corner = 22;
                fill = on ? S(0xFF, 0xF2, 0xF2, 0xF5) : S(0x22, 0xFF, 0xFF, 0xFF);
                if (on) foreBrush = S(0xFF, 0x11, 0x11, 0x14);
                edge = focused ? S(0xF2, 0xFF, 0xFF, 0xFF) : Brushes.Transparent; edgeWidth = 2.5;
                if (focused) glow = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.45 };
                break;
        }
        face.Background = fill;
        face.BorderBrush = edge;
        face.BorderThickness = new Thickness(edgeWidth);
        face.CornerRadius = new CornerRadius(round ? Math.Max(face.Width, face.Height) / 2 : corner);
        face.Effect = Perf.Active ? null : glow;
        foreach (var text in texts)
        {
            if (foreBrush != null) text.Foreground = foreBrush;
            else text.SetResourceReference(TextBlock.ForegroundProperty, fore);
        }
        return true;
    }

    /// <summary>The sheet behind quick settings or the power options, in the look's way. False for the original.</summary>
    public static bool StyleSheet(Border sheet)
    {
        Brush S(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        switch (Looks.Current.Id)
        {
            case "ubiquity":
                sheet.Background = S(0xFF, 0x07, 0x08, 0x0B);
                sheet.BorderBrush = new SolidColorBrush(accent);
                sheet.BorderThickness = new Thickness(0, 0, 2, 0);
                sheet.CornerRadius = new CornerRadius(0);
                return true;
            case "aniki":
                sheet.Background = S(0xD9, 0x08, 0x09, 0x0D);
                sheet.BorderBrush = S(0x30, 0xFF, 0xFF, 0xFF);
                sheet.BorderThickness = new Thickness(1.5);
                sheet.CornerRadius = new CornerRadius(22);
                return true;
            case "jhero":
                sheet.Background = S(0xF5, 0x0A, 0x14, 0x2A);
                sheet.BorderBrush = S(0xB0, accent.R, accent.G, accent.B);
                sheet.BorderThickness = new Thickness(1.5);
                sheet.CornerRadius = new CornerRadius(12);
                return true;
            case "deck":
                sheet.Background = S(0xFA, 0x17, 0x1A, 0x21);
                sheet.BorderBrush = S(0x20, 0xFF, 0xFF, 0xFF);
                sheet.BorderThickness = new Thickness(1, 0, 0, 0);
                sheet.CornerRadius = new CornerRadius(0);
                return true;
            case "solaris":
                sheet.Background = S(0xFA, 0x0E, 0x0F, 0x12);
                sheet.BorderBrush = new SolidColorBrush(accent);
                sheet.BorderThickness = new Thickness(0, 3, 0, 0);
                sheet.CornerRadius = new CornerRadius(3);
                return true;
            case "xdash":
                sheet.Background = S(0xFA, 0x1B, 0x1D, 0x20);
                sheet.BorderBrush = S(0x26, 0xFF, 0xFF, 0xFF);
                sheet.BorderThickness = new Thickness(1, 0, 0, 0);
                sheet.CornerRadius = new CornerRadius(0);
                return true;
            case "orbit":
                sheet.Background = S(0xEB, 0x12, 0x12, 0x18);
                sheet.BorderBrush = S(0x30, 0xFF, 0xFF, 0xFF);
                sheet.BorderThickness = new Thickness(1, 1, 1, 0);
                sheet.CornerRadius = new CornerRadius(28, 28, 0, 0);
                return true;
        }
        return false;
    }

    /// <summary>Where a sheet sits: left (a drawer from the left edge), right, bottom or center (the look's usual).</summary>
    public static string SheetSide(bool power) => Looks.Current.Id switch
    {
        "ubiquity" => "left",
        "deck" or "xdash" => power && Looks.Current.Id == "deck" ? "center" : "right",
        "orbit" => "bottom",
        _ => "center"
    };
}
