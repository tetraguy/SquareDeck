using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "New Settings UI": the Settings page drawn like the Nintendo Switch 2's System Settings. Flat rows on
/// thin lines with no icons, values in blue, pill switches, section titles with a short bar, a plain list of sections
/// on the left with a blue bar beside the one you're in, and the highlight as a white card with a soft blue, purple
/// and pink rim. Light (Basic White) or dark (Basic Black), following the theme.
/// </summary>
public static class Switch2Settings
{
    public static bool On => Labs.On("newsettings");

    public static bool Dark => Theme.CurrentIsDark;

    /// <summary>
    /// The page's colors. With Switch (SquareDeck's own style) they're the Switch 2's Basic White or Basic Black; with
    /// any other UI style they come from that style: its background, text and highlight, with the lines, the
    /// highlight card and the off switches mixed from them, so the layout stays the Switch 2's but the look is yours.
    /// </summary>
    private static (Color Bg, Color Fg, Color Sub, Color Line, Color Accent, Color Card, Color Off) Palette()
    {
        bool dark = Dark;
        if (UiStyles.Current.Id == "switch")
            return dark
                ? (Rgb(0x24, 0x24, 0x26), Rgb(0xF2, 0xF2, 0xF2), Rgb(0x9C, 0x9C, 0xA0), Rgb(0x48, 0x48, 0x4C), Rgb(0x4D, 0x8D, 0xFF), Rgb(0x36, 0x36, 0x3A), Rgb(0x5C, 0x5C, 0x62))
                : (Rgb(0xEB, 0xEB, 0xEB), Rgb(0x1C, 0x1C, 0x1E), Rgb(0x70, 0x70, 0x74), Rgb(0xC8, 0xC8, 0xCA), Rgb(0x1E, 0x5F, 0xE5), Rgb(0xF8, 0xF8, 0xF9), Rgb(0xCB, 0xCB, 0xCF));
        Color bg = Res("BgBrush", dark ? Rgb(0x24, 0x24, 0x26) : Rgb(0xEB, 0xEB, 0xEB));
        Color fg = Res("FgBrush", dark ? Colors.White : Rgb(0x1C, 0x1C, 0x1E));
        Color sub = Res("SubtleBrush", Mix(fg, bg, 0.45));
        Color accent = Res("AccentBrush", Rgb(0x1E, 0x5F, 0xE5));
        Color card = dark ? Mix(bg, fg, 0.12) : Mix(bg, Colors.White, 0.75);
        return (bg, fg, sub, Mix(bg, fg, 0.2), accent, card, Mix(bg, fg, 0.32));
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color Res(string key, Color fallback) =>
        Application.Current?.Resources[key] is SolidColorBrush b ? Color.FromRgb(b.Color.R, b.Color.G, b.Color.B) : fallback;

    private static (Color Bg, Color Fg, Color Sub, Color Line, Color Accent, Color Card, Color Off) _p = Palette();

    public static Color Background => _p.Bg;
    public static Color Text => _p.Fg;
    public static Color Subtle => _p.Sub;
    public static Color Line => _p.Line;
    public static Color Blue => _p.Accent;
    public static Color Card => _p.Card;
    public static Color Off => _p.Off;

    private static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Reads the UI style's colors again, for things drawn outside a page (the game cards, the return).</summary>
    public static void Refresh() => _p = Palette();

    /// <summary>The highlight's rim, for things drawn outside a page.</summary>
    public static Brush RimBrush => Rim();

    /// <summary>The page's own colors, set on the page so everything on it picks them up.</summary>
    public static void ApplyColors(FrameworkElement page)
    {
        _p = Palette(); // the UI style in use now
        page.Resources["BgBrush"] = B(Background);
        page.Resources["FgBrush"] = B(Text);
        page.Resources["SubtleBrush"] = B(Subtle);
        page.Resources["AccentBrush"] = B(Blue);
        page.Resources["S2Line"] = B(Line);
        page.Resources["S2Card"] = B(Card);
        page.Resources["S2Rim"] = Rim();
        var glow = UiStyles.Current.Id == "switch" ? Color.FromRgb(0x86, 0x8E, 0xFF) : _p.Accent;
        page.Resources["S2Glow"] = new DropShadowEffect { Color = glow, BlurRadius = 16, ShadowDepth = 0, Opacity = Dark ? 0.55 : 0.45 };
    }

    /// <summary>The highlight's rim: blue into purple into pink and back to blue, as on the Switch 2.</summary>
    private static Brush Rim()
    {
        var style = UiStyles.Current;
        if (style.Id != "switch")
        {
            // Another UI style: its own gradient ring, or its highlight color brightening along the rim.
            var own = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
            var stops = new List<Color>();
            if (style.RingColors is { Length: > 1 } ring)
                foreach (var hex in ring) if (Theme.TryParseArgb(hex, out var c)) stops.Add(c);
            if (stops.Count < 2) stops = new List<Color> { _p.Accent, Mix(_p.Accent, Colors.White, 0.45), _p.Accent };
            for (int i = 0; i < stops.Count; i++) own.GradientStops.Add(new GradientStop(stops[i], (double)i / (stops.Count - 1)));
            own.Freeze();
            return own;
        }
        var g = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0x7C, 0xF4), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x7D, 0xB6, 0xFF), 0.3));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xB4, 0x8C, 0xF6), 0.62));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xA6, 0xD4), 0.85));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x6F, 0x9B, 0xFF), 1));
        g.Freeze();
        return g;
    }

    private static Style? _row, _nav;

    /// <summary>A settings row: flat on a thin line; highlighted, a card with the rim.</summary>
    public static Style RowStyle => _row ??= Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource FgBrush}"/>
          <Setter Property="FontSize" Value="20"/>
          <Setter Property="Margin" Value="0"/>
          <Setter Property="MinHeight" Value="64"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="Line" Height="1" VerticalAlignment="Bottom" Margin="14,0,14,0" Background="{DynamicResource S2Line}"/>
                  <Border x:Name="Card" CornerRadius="10" BorderThickness="3" BorderBrush="Transparent" Background="Transparent" Margin="-2,-1,-2,-1"/>
                  <Border Padding="26,10,26,10">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource S2Card}"/>
                    <Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource S2Rim}"/>
                    <Setter TargetName="Card" Property="Effect" Value="{DynamicResource S2Glow}"/>
                    <Setter TargetName="Line" Property="Opacity" Value="0"/>
                  </Trigger>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource S2Card}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    /// <summary>A section on the left: plain text; the one you're in is blue with a bar beside it.</summary>
    public static Style NavStyle => _nav ??= Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource FgBrush}"/>
          <Setter Property="FontSize" Value="19"/>
          <Setter Property="Margin" Value="0"/>
          <Setter Property="MinHeight" Value="62"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="Card" CornerRadius="10" BorderThickness="3" BorderBrush="Transparent" Background="Transparent" Margin="14,0,0,0"/>
                  <Border x:Name="Bar" Width="4" CornerRadius="2" Margin="0,12,0,12" HorizontalAlignment="Left" Background="{DynamicResource AccentBrush}" Visibility="Collapsed"/>
                  <Border Padding="40,10,18,10">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="Tag" Value="selected">
                    <Setter TargetName="Bar" Property="Visibility" Value="Visible"/>
                    <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource S2Card}"/>
                    <Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource S2Rim}"/>
                    <Setter TargetName="Card" Property="Effect" Value="{DynamicResource S2Glow}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    private static Style Parse(string xaml) => (Style)XamlReader.Parse(xaml);

    /// <summary>A section title: a short thick bar, then the name in regular type, as the Switch 2 marks "TV" or "Save Data".</summary>
    public static FrameworkElement Header(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 30, 0, 8) };
        var bar = new Border { Width = 4, Height = 24, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        row.Children.Add(bar);
        var label = Ui.Text(text, 18, "FgBrush");
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        // Searching Settings reads headers as text: keep the words findable.
        row.Tag = "s2header";
        return row;
    }

    /// <summary>A pill switch: blue with the knob on the right when on, gray with it on the left when off.</summary>
    public static FrameworkElement Toggle(bool on)
    {
        var grid = new Grid { Width = 60, Height = 34, VerticalAlignment = VerticalAlignment.Center, ToolTip = on ? "On" : "Off" };
        var track = new Border { CornerRadius = new CornerRadius(17), Background = on ? B(Blue) : B(Off) };
        grid.Children.Add(track);
        var knob = new Ellipse
        {
            Width = 28, Height = 28, Fill = Brushes.White, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.25 }
        };
        grid.Children.Add(knob);
        return grid;
    }

    /// <summary>
    /// Redraws the rows SquareDeck built in the Switch 2 manner: flat rows on lines, no icon badges, blue values,
    /// pill switches. Anything that isn't a plain settings row (panels, cards of styles) is left as it is.
    /// </summary>
    public static void Restyle(Panel rows)
    {
        var plain = Ui.StyleOf("RowButton");
        // Section titles become the bar-and-name kind.
        foreach (var title in rows.Children.OfType<TextBlock>().Where(t => t.Tag as string == "group").ToList())
        {
            int at = rows.Children.IndexOf(title);
            rows.Children.RemoveAt(at);
            rows.Children.Insert(at, Header(title.Text));
        }
        foreach (var button in rows.Children.OfType<Button>().ToList())
        {
            if (button.Style != plain) continue;
            button.Style = RowStyle;
            button.Margin = new Thickness(0);
            button.Resources.Remove("RowCorner");
            if (button.Content is not Grid grid) continue;
            foreach (var child in grid.Children.OfType<FrameworkElement>().ToList())
            {
                int col = Grid.GetColumn(child);
                if (col == 0 && !ContainsImage(child)) child.Visibility = Visibility.Collapsed; // no icons on the Switch 2's rows
                else if (col == 1 && child is TextBlock label) label.FontSize = 20;
                else if (col == 1 && child is StackPanel two && two.Children.Count > 0 && two.Children[0] is TextBlock first) first.FontSize = 20;
                else if (col == 2 && child is TextBlock value) { value.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); value.FontSize = 19; }
                else if (col == 2 && child is Grid { Width: 50, ToolTip: string state } && state is "On" or "Off")
                {
                    int at = grid.Children.IndexOf(child);
                    grid.Children.Remove(child);
                    var pill = Toggle(state == "On");
                    pill.Margin = new Thickness(16, 0, 0, 0);
                    Grid.SetColumn(pill, 2);
                    grid.Children.Insert(at, pill);
                }
            }
        }
    }

    private static bool ContainsImage(DependencyObject node)
    {
        if (node is Image) return true;
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d && ContainsImage(d)) return true;
        return false;
    }
}
