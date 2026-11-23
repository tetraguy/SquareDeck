using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquareDeck.Services;

/// <summary>
/// How a look lays out and draws SquareDeck's pages, beyond the colors and shapes its UI style gives them:
/// the frame every page shares (title, header clock, footer), the game page, the Play button and the library.
/// </summary>
public sealed record PageDesign
{
    // ---- the frame every page shares
    /// <summary>bold (the usual), spaced (thin capitals with wide letter spacing) or light (large, light weight).</summary>
    public string Title { get; init; } = "bold";
    public double TitleSize { get; init; } = 34;
    /// <summary>The time and battery at the right of every page's title bar.</summary>
    public bool HeaderClock { get; init; }
    /// <summary>line (a thin line over the button hints), bar (a solid band along the bottom) or plain (hints only).</summary>
    public string Footer { get; init; } = "line";

    // ---- the game page
    /// <summary>
    /// switch    cover, name and logo in a banner, actions and cards under it (the original)
    /// showcase  the artwork fills the screen; logo, details, stats and actions on the left (ubiquity)
    /// sidepanel like showcase, with the stats in a panel on the right (Aniki)
    /// banner    the artwork as a banner with the logo in it, a play bar with stats under it (SquareDeck)
    /// panels    logo top left; stat panels and Play on the left, the description on the right (J Hero)
    /// split     a card with cover, details, stats and Play beside a big picture of the game (Solaris)
    /// dash      a console game page: name, details and Play on the left, the cover large on the right (X Dash)
    /// orbit     the artwork full screen, logo and Play bottom left, activity cards along the bottom (Orbit)
    /// </summary>
    public string Details { get; init; } = "switch";
    /// <summary>How tall the artwork behind the top of the game page is.</summary>
    public double HeroHeight { get; init; } = 380;
    /// <summary>The game page's numbers: cards (the usual), text (label over value, no card), list (rows) or large (big glass cards).</summary>
    public string Stats { get; init; } = "cards";

    // ---- the Play button (game page)
    /// <summary>Its fill: one color or two for a gradient. Null = the highlight color.</summary>
    public string[]? PlayColors { get; init; }
    public string PlayText { get; init; } = "#FFFFFF";
    public double PlayCorner { get; init; } = 26;
    /// <summary>Drawn as an outline in the fill color, with no fill.</summary>
    public bool PlayOutline { get; init; }

    // ---- lists, Settings and the status icons (see LookChrome)
    /// <summary>
    /// How list rows are drawn everywhere (Settings and every other list): card (the usual rounded cards), line (no
    /// card, a hairline under each row, an accent bar and glow on the one you're on), glass (frosted cards, white
    /// ring), outline (navy cards with a blue edge), flat (plain rows, a light fill on the one you're on), block (square
    /// cards, a block of color at the left of the one you're on), solid (grey tiles, a thick white ring) or pill
    /// (soft rounded rows, a bright fill and glow).
    /// </summary>
    public string Rows { get; init; } = "card";
    /// <summary>A row's icon: badge (tinted square), plain (the symbol alone), outline (a ring), square (a solid block) or glass.</summary>
    public string Icons { get; init; } = "badge";
    /// <summary>Headers over groups of rows: bold, spaced (thin spaced capitals), caps (small capitals), accent or light.</summary>
    public string Headers { get; init; } = "bold";
    /// <summary>Settings: column (one centered column), drawer (a column on the left over the artwork), wide or panes (sections on the left, settings on the right).</summary>
    public string Settings { get; init; } = "column";
    /// <summary>The banner at the top of a Settings section: badge, spaced, hero, frame, plain, block or light.</summary>
    public string Banner { get; init; } = "badge";
    /// <summary>The time and status icons at the top of the home screen: plain, rail, capsule, frame, deck, chip, dash or orbit.</summary>
    public string Status { get; init; } = "plain";

    // ---- the library (used when the look is chosen; you can still change them in the library)
    public string LibraryLayout { get; init; } = "sidebar";
    public string LibraryView { get; init; } = "square";
}

/// <summary>
/// A look: one complete design for SquareDeck, chosen in Settings > Themes.
///
/// A look is more than colors. It owns the home screen (through its UI style), the frame every page shares, the
/// game page, the Play button and the library's layout, so each look's pages are drawn separately from every other
/// look's: changing or adding one look never changes another. Colors, wallpapers, sounds and music sit on top.
/// New looks derive from this class and are added to <see cref="Looks.All"/>.
/// </summary>
public abstract class Look
{
    /// <summary>Saved in the settings (Settings.LookId). Never change it once a look is released.</summary>
    public abstract string Id { get; }
    public abstract string Name { get; }
    /// <summary>One or two sentences shown under the name in Themes.</summary>
    public abstract string Description { get; }
    /// <summary>Where the design comes from, shown small in Themes ("" for none).</summary>
    public virtual string Credit => "";
    /// <summary>The UI style for its home screen, colors, corners, tiles and buttons (see UiStyles).</summary>
    public abstract string StyleId { get; }
    /// <summary>Its pages, game page, Play button and library.</summary>
    public virtual PageDesign Design { get; } = new();
    /// <summary>A small drawing of the look's home screen for the chooser, <paramref name="width"/> wide (16:9).</summary>
    public abstract FrameworkElement Preview(double width);
}

/// <summary>
/// Draws the looks' little home-screen pictures for Themes, on a 320 × 180 grid scaled to the width.
/// Each look draws in its own colors, so every picture shows what it will really look like.
/// </summary>
internal sealed class Sketch
{
    public readonly Canvas Canvas;
    private readonly double _u;

    public Sketch(double width, params string[] background)
    {
        _u = width / 320;
        Canvas = new Canvas { Width = width, Height = width * 9 / 16, ClipToBounds = true };
        Rect(0, 0, 320, 180, 0, background);
    }

    public static Brush Paint(params string[] colors)
    {
        if (colors.Length == 1) return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[0]));
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < colors.Length; i++)
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), i / (double)(colors.Length - 1)));
        return g;
    }

    private T Put<T>(T shape, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(shape, x * _u);
        Canvas.SetTop(shape, y * _u);
        Canvas.Children.Add(shape);
        return shape;
    }

    public Rectangle Rect(double x, double y, double w, double h, double r, params string[] fill) =>
        Put(new Rectangle { Width = w * _u, Height = h * _u, RadiusX = r * _u, RadiusY = r * _u, Fill = Paint(fill) }, x, y);

    public Rectangle Frame(double x, double y, double w, double h, double r, string stroke, double thickness = 2) =>
        Put(new Rectangle { Width = w * _u, Height = h * _u, RadiusX = r * _u, RadiusY = r * _u, Stroke = Paint(stroke), StrokeThickness = thickness * _u }, x, y);

    public Ellipse Dot(double x, double y, double d, string fill) =>
        Put(new Ellipse { Width = d * _u, Height = d * _u, Fill = Paint(fill) }, x, y);

    /// <summary>A fade over the picture (a scrim), from a color at one side to clear at the other.</summary>
    public void Fade(double x, double y, double w, double h, string color, bool vertical)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var g = new LinearGradientBrush { StartPoint = vertical ? new Point(0.5, 1) : new Point(0, 0.5), EndPoint = vertical ? new Point(0.5, 0) : new Point(1, 0.5) };
        g.GradientStops.Add(new GradientStop(c, 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        Put(new Rectangle { Width = w * _u, Height = h * _u, Fill = g }, x, y);
    }

    /// <summary>A soft blob of light, for the game's artwork behind the home screen.</summary>
    public void Glow(double x, double y, double d, string color)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        Put(new Ellipse { Width = d * _u, Height = d * _u, Fill = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B)) }, x - d / 2, y - d / 2);
    }

    /// <summary>A line of text, drawn as a rounded bar.</summary>
    public void Words(double x, double y, double w, double h, string color, double opacity = 1) => Rect(x, y, w, h, h / 2, color).Opacity = opacity;
}

// ==================================================================== the looks

/// <summary>Switch: the original SquareDeck design.</summary>
public sealed class SwitchLook : Look
{
    public override string Id => "switch";
    public override string Name => "Switch";
    public override string Description => "The original SquareDeck design: a row of square game tiles, round buttons underneath, and clean, simple pages.";
    public override string StyleId => "switch";
    public override PageDesign Design { get; } = new();

    public override FrameworkElement Preview(double width)
    {
        var s = new Sketch(width, "#2D2D2D");
        s.Dot(14, 12, 14, "#3C3C3C");
        s.Words(262, 16, 22, 6, "#B4B4B4", 0.6);
        s.Words(288, 16, 18, 6, "#B4B4B4", 0.6);
        s.Frame(15, 49, 64, 64, 9, "#20E0D0", 2.5);
        s.Rect(18, 52, 58, 58, 7, "#20E0D0").Opacity = 0.85;
        for (int i = 1; i < 5; i++) s.Rect(18 + i * 62, 56, 54, 54, 6, "#4B4B4B");
        s.Words(18, 116, 70, 5, "#FFFFFF", 0.8);
        for (int i = 0; i < 6; i++) s.Dot(101 + i * 21, 134, 18, "#3C3C3C");
        s.Rect(10, 162, 300, 1, 0, "#B4B4B4").Opacity = 0.4;
        s.Words(250, 168, 22, 5, "#B4B4B4", 0.6);
        s.Words(278, 168, 30, 5, "#B4B4B4", 0.6);
        return s.Canvas;
    }
}

/// <summary>The looks SquareDeck has, and the one in use.</summary>
public static class Looks
{
    public static readonly Look[] All =
    {
        new SwitchLook()
    };

    public static Look Current { get; private set; } = All[0];

    /// <summary>The look with this id, or Switch when there's none (an older or unknown id).</summary>
    public static Look Find(string? id) => All.FirstOrDefault(l => l.Id == id) ?? All[0];

    /// <summary>Makes a look the current one (it's drawn the next time the screen is laid out).</summary>
    public static void Select(string? id)
    {
        Current = Find(id);
        LookChrome.Apply(Current);
    }

    private static readonly Dictionary<string, Style> PlayStyles = new();

    /// <summary>
    /// The game page's Play button for the current look: its fill (or outline), text color and corners, with the same
    /// highlight ring as every other button. The original look keeps the usual accent button.
    /// </summary>
    public static Style PlayStyle()
    {
        var d = Current.Design;
        if (d.PlayColors == null && !d.PlayOutline && Math.Abs(d.PlayCorner - 26) < 0.1 && d.PlayText == "#FFFFFF")
            return (Style)Application.Current.Resources["AccentButton"];
        if (PlayStyles.TryGetValue(Current.Id, out var cached)) return cached;

        string inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string template = $@"
<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Border x:Name=""Ring"" CornerRadius=""{inv(d.PlayCorner + 4)}"" BorderThickness=""3"" BorderBrush=""Transparent"" Padding=""3"">
    <Border x:Name=""B"" Background=""{{TemplateBinding Background}}"" BorderBrush=""{{TemplateBinding BorderBrush}}""
            BorderThickness=""{{TemplateBinding BorderThickness}}"" CornerRadius=""{inv(d.PlayCorner)}"" Padding=""{{TemplateBinding Padding}}"">
      <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
    </Border>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsKeyboardFocused"" Value=""True"">
      <Setter TargetName=""Ring"" Property=""BorderBrush"" Value=""{{DynamicResource FgBrush}}""/>
    </Trigger>
    <Trigger Property=""IsPressed"" Value=""True"">
      <Setter TargetName=""B"" Property=""Opacity"" Value=""0.85""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(template)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 20.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(34, 11, 34, 11)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        // The look's own color, or the highlight color (following the theme as it changes).
        object fill = d.PlayColors is { Length: > 0 } colors ? Sketch.Paint(colors) : new DynamicResourceExtension("AccentBrush");
        if (d.PlayOutline)
        {
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, fill));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("FgBrush")));
        }
        else
        {
            style.Setters.Add(new Setter(Control.BackgroundProperty, fill));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            // White text on the highlight color follows it, so a light highlight gets dark text instead.
            bool onAccent = d.PlayColors is not { Length: > 0 } && string.Equals(d.PlayText, "#FFFFFF", StringComparison.OrdinalIgnoreCase);
            style.Setters.Add(new Setter(Control.ForegroundProperty, onAccent ? new DynamicResourceExtension("OnAccentBrush") : Sketch.Paint(d.PlayText)));
        }
        PlayStyles[Current.Id] = style;
        return style;
    }
}
