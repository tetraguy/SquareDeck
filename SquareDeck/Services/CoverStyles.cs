using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SquareDeck.Services;

/// <summary>
/// Experimental "Cover styles" (8.5.81): how the covers look on Home and in the library. A cut (the cover's outline:
/// hexagon, ticket, arch...), a pose (tilted, slanted, scattered, fanned...), a frame (Polaroid, film strip, cartridge,
/// gallery gold...) and the shape of game icons. The art shape itself (square, tall, wide) is the existing Home tile
/// art and Library view settings, gathered on the same page.
/// </summary>
public static class CoverStyles
{
    public const string Lab = "coverstyle";

    public static readonly (string Id, string Name, string About)[] Shapes =
    {
        ("style", "Rounded", "The UI style's own corners"),
        ("sharp", "Sharp", "Square corners, no rounding"),
        ("squircle", "Squircle", "Soft, deeply rounded corners, like an app icon"),
        ("circle", "Circle", "A circle (an oval on tall or wide art)"),
        ("hexagon", "Hexagon", "Six sides, pointed at the left and right"),
        ("octagon", "Octagon", "Eight sides, like a stop sign"),
        ("diamond", "Diamond", "Turned on its point"),
        ("cut", "Cut corners", "Each corner clipped off"),
        ("ticket", "Ticket", "A notch bitten out of each side, like an admission ticket"),
        ("leaf", "Leaf", "Two opposite corners rounded right round, the other two square"),
        ("arch", "Arch", "A rounded top, like a window in a church"),
        ("shield", "Shield", "A crest, coming to a point at the bottom"),
        ("slant", "Slanted", "A parallelogram leaning to the right"),
    };

    public static readonly (string Id, string Name, string About)[] Poses =
    {
        ("flat", "Flat", "Straight, as they are"),
        ("tiltleft", "Tilted left", "Every cover turned a little to the left"),
        ("tiltright", "Tilted right", "Every cover turned a little to the right"),
        ("slant", "Slanted", "Leaning forward, like italics"),
        ("scatter", "Scattered", "Each at its own little angle, like photos on a table"),
        ("fan", "Fanned", "Fanning out from the highlighted cover, like a hand of cards"),
        ("lean", "Lean in", "The covers on each side lean toward the highlighted one"),
        ("stairs", "Staircase", "Stepping down and back up along the row"),
        ("bob", "Gentle bob", "Floating up and down, each in its own time"),
    };

    public static readonly (string Id, string Name, string About)[] Frames =
    {
        ("none", "None", "No frame"),
        ("thin", "Thin outline", "A fine line round the cover"),
        ("thick", "Thick outline", "A bold line round the cover"),
        ("double", "Double line", "Two fine lines, one inside the other"),
        ("glow", "Glow", "A soft glow round the edge"),
        ("gradient", "Gradient ring", "A ring blending from the frame color to white"),
        ("dashed", "Dashed", "A dashed line, like a cut-out coupon"),
        ("polaroid", "Polaroid", "A white instant-photo border, deeper at the bottom"),
        ("film", "Film strip", "Dark bands with sprocket holes along the top and bottom"),
        ("cartridge", "Cartridge", "Grey plastic with grip ridges along the top, the cover as its label"),
        ("case", "Game case", "A dark spine down the left and a plastic shine"),
        ("gold", "Gallery gold", "A gilded picture frame"),
        ("neon", "Neon tube", "A glowing tube of light round the edge"),
        ("pixel", "Pixel border", "A chunky stepped border, like an old console"),
        ("sticker", "Sticker", "A thick white edge and a shadow, like a sticker"),
    };

    public static readonly (string Id, string Name)[] FrameColors =
    {
        ("accent", "Highlight color"), ("white", "White"), ("black", "Black"), ("text", "The style's text color"), ("gold", "Gold"),
    };

    public static readonly (string Id, string Name, string About)[] IconShapes =
    {
        ("style", "As they are", "The UI style's own corners"),
        ("square", "Square", "No rounding"),
        ("rounded", "Rounded", "Gently rounded corners"),
        ("circle", "Circle", "Round"),
        ("squircle", "Squircle", "Like an app icon on a phone"),
        ("badge", "Tilted badge", "A rounded square turned to one side"),
        ("sticker", "Sticker", "A white edge and a shadow"),
    };

    /// <summary>One place's choices (Home row or library).</summary>
    public sealed record Look(string Shape, string Pose, string Frame, string FrameColor, bool Straighten, string Icon)
    {
        public bool Plain => Shape == "style" && Pose == "flat" && Frame == "none";
    }

    public static bool On => Labs.On(Lab);

    /// <summary>The library uses the Home row's choices unless it has its own.</summary>
    public static bool LibraryMatchesHome => Labs.Get("coverstyle.libsame", "1") == "1";

    public static Look Read(string where) => new(
        Labs.Get($"coverstyle.{where}.shape", "style"), Labs.Get($"coverstyle.{where}.pose", "flat"),
        Labs.Get($"coverstyle.{where}.frame", "none"), Labs.Get($"coverstyle.{where}.color", "accent"),
        Labs.Get($"coverstyle.{where}.straighten", "1") == "1", Labs.Get($"coverstyle.{where}.icon", "style"));

    /// <summary>The Home row's look, or null when the experiment is off.</summary>
    public static Look? Home => On ? Read("home") : null;

    /// <summary>The library's look, or null when the experiment is off.</summary>
    public static Look? Library => On ? Read(LibraryMatchesHome ? "home" : "lib") : null;

    /// <summary>A cover's outline, for a box of the given size (null: leave the style's own rounded corners).</summary>
    public static Geometry? Outline(string shape, double w, double h, double styleCorner)
    {
        if (w <= 0 || h <= 0) return null;
        double m = Math.Min(w, h);
        Geometry g;
        switch (shape)
        {
            case "sharp": g = new RectangleGeometry(new Rect(0, 0, w, h)); break;
            case "squircle": g = new RectangleGeometry(new Rect(0, 0, w, h), m * 0.3, m * 0.3); break;
            case "circle": g = new EllipseGeometry(new Rect(0, 0, w, h)); break;
            case "hexagon": g = Poly((w * 0.22, 0), (w * 0.78, 0), (w, h / 2), (w * 0.78, h), (w * 0.22, h), (0, h / 2)); break;
            case "octagon":
            {
                double c = m * 0.29;
                g = Poly((c, 0), (w - c, 0), (w, c), (w, h - c), (w - c, h), (c, h), (0, h - c), (0, c));
                break;
            }
            case "diamond": g = Poly((w / 2, 0), (w, h / 2), (w / 2, h), (0, h / 2)); break;
            case "cut":
            {
                double c = m * 0.14;
                g = Poly((c, 0), (w - c, 0), (w, c), (w, h - c), (w - c, h), (c, h), (0, h - c), (0, c));
                break;
            }
            case "ticket":
            {
                double r = m * 0.09, rc = Math.Max(styleCorner, 6);
                var body = new RectangleGeometry(new Rect(0, 0, w, h), rc, rc);
                var notches = new GeometryGroup();
                notches.Children.Add(new EllipseGeometry(new Point(0, h / 2), r, r));
                notches.Children.Add(new EllipseGeometry(new Point(w, h / 2), r, r));
                g = new CombinedGeometry(GeometryCombineMode.Exclude, body, notches);
                break;
            }
            case "leaf":
            {
                double r = m * 0.38;
                g = ParseGeo($"M {F(r)},0 L {F(w)},0 L {F(w)},{F(h - r)} A {F(r)},{F(r)} 0 0 1 {F(w - r)},{F(h)} L 0,{F(h)} L 0,{F(r)} A {F(r)},{F(r)} 0 0 1 {F(r)},0 Z");
                break;
            }
            case "arch":
            {
                double r = Math.Min(w / 2, h * 0.6);
                g = ParseGeo($"M 0,{F(h)} L 0,{F(r)} A {F(w / 2)},{F(r)} 0 0 1 {F(w)},{F(r)} L {F(w)},{F(h)} Z");
                break;
            }
            case "shield":
            {
                double r = Math.Max(styleCorner, 6);
                g = ParseGeo($"M {F(r)},0 L {F(w - r)},0 Q {F(w)},0 {F(w)},{F(r)} L {F(w)},{F(h * 0.55)} " +
                         $"C {F(w)},{F(h * 0.8)} {F(w * 0.72)},{F(h * 0.93)} {F(w / 2)},{F(h)} C {F(w * 0.28)},{F(h * 0.93)} 0,{F(h * 0.8)} 0,{F(h * 0.55)} " +
                         $"L 0,{F(r)} Q 0,0 {F(r)},0 Z");
                break;
            }
            case "slant":
            {
                double k = w * 0.12;
                g = Poly((k, 0), (w, 0), (w - k, h), (0, h));
                break;
            }
            default: return null;
        }
        g.Freeze();
        return g;
    }

    /// <summary>An icon's outline (null: leave it as it is).</summary>
    public static Geometry? IconOutline(string shape, double w, double h)
    {
        double m = Math.Min(w, h);
        Geometry? g = shape switch
        {
            "square" => new RectangleGeometry(new Rect(0, 0, w, h)),
            "rounded" => new RectangleGeometry(new Rect(0, 0, w, h), m * 0.16, m * 0.16),
            "circle" => new EllipseGeometry(new Rect(0, 0, w, h)),
            "squircle" or "badge" or "sticker" => new RectangleGeometry(new Rect(0, 0, w, h), m * 0.3, m * 0.3),
            _ => null,
        };
        g?.Freeze();
        return g;
    }

    /// <summary>Shapes an icon box (the library's List view, the apps on Home, the running game's icon).</summary>
    public static void StyleIcon(FrameworkElement box, double w, double h, string shape)
    {
        if (IconOutline(shape, w, h) is not { } outline) return;
        box.Clip = outline;
        if (box is Border b) b.CornerRadius = new CornerRadius(0);
        if (shape == "badge")
        {
            box.RenderTransformOrigin = new Point(0.5, 0.5);
            box.RenderTransform = new RotateTransform(-8);
        }
        else if (shape == "sticker")
        {
            if (box is Border border) { border.BorderBrush = Brushes.White; border.BorderThickness = new Thickness(Math.Max(2, Math.Round(m(w, h) * 0.05))); }
            if (!Perf.Active) box.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.45 };
        }
        static double m(double a, double b) => Math.Min(a, b);
    }

    /// <summary>The pose's transform for the cover at <paramref name="index"/>, with <paramref name="selected"/> highlighted.</summary>
    public static Transform? PoseFor(Look look, int index, int selected, bool library = false)
    {
        bool isSelected = index == selected;
        if (isSelected && look.Straighten && look.Pose != "flat") return null;
        string pose = look.Pose;
        // The library wraps into rows, so poses that follow the highlighted cover along a row become a scatter or flat.
        if (library) pose = pose switch { "fan" => "scatter", "lean" or "bob" => "flat", _ => pose };
        switch (pose)
        {
            case "tiltleft": return new RotateTransform(-4);
            case "tiltright": return new RotateTransform(4);
            case "slant": return new SkewTransform(-8, 0);
            case "scatter":
            {
                double angle = (Hash(index * 7 + 3) - 0.5) * 12, y = (Hash(index * 13 + 1) - 0.5) * 14;
                var t = new TransformGroup();
                t.Children.Add(new RotateTransform(angle));
                t.Children.Add(new TranslateTransform(0, y));
                return t;
            }
            case "fan":
            {
                if (selected < 0) return null;
                int d = index - selected;
                var t = new TransformGroup();
                t.Children.Add(new RotateTransform(Math.Clamp(d * 4.5, -16, 16)));
                t.Children.Add(new TranslateTransform(0, Math.Min(Math.Abs(d), 4) * 7));
                return t;
            }
            case "lean":
            {
                if (selected < 0 || isSelected) return null;
                int side = Math.Sign(index - selected);
                var t = new TransformGroup();
                t.Children.Add(new ScaleTransform(0.94, 0.94));
                t.Children.Add(new SkewTransform(0, side * -6));
                return t;
            }
            case "stairs":
            {
                int step = index % 6;
                return new TranslateTransform(0, (step <= 3 ? step : 6 - step) * 12 - 12);
            }
            case "bob":
            {
                var t = new TranslateTransform();
                if (!Theme.ReduceMotion && !Perf.Active)
                    t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(1.6 + Hash(index) * 0.8))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromSeconds(-Hash(index * 5) * 2),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    });
                return t;
            }
            default: return null;
        }
    }

    /// <summary>
    /// Fills <paramref name="host"/> (laid over a cover of the given size) with the frame, and the highlight drawn along
    /// the cut when the cut isn't the style's own (<paramref name="ring"/>: the brush the highlight ring uses).
    /// </summary>
    public static void Decorate(Grid host, Look look, double w, double h, double styleCorner, Border? ring)
    {
        host.Children.Clear();
        host.Effect = null;
        if (w <= 0 || h <= 0) return;
        var outline = Outline(look.Shape, w, h, styleCorner) ?? Frozen(new RectangleGeometry(new Rect(0, 0, w, h), styleCorner, styleCorner));
        Brush color = FrameBrush(look.FrameColor);
        Path Stroke(Geometry g, double thickness, Brush brush, DoubleCollection? dash = null)
        {
            var p = new Path { Data = g, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
            if (dash != null) p.StrokeDashArray = dash;
            host.Children.Add(p);
            return p;
        }
        Path Band(Thickness inset, Brush fill)
        {
            var inner = Outline(look.Shape == "style" ? "style" : look.Shape, Math.Max(1, w - inset.Left - inset.Right), Math.Max(1, h - inset.Top - inset.Bottom), Math.Max(0, styleCorner - 4))
                        ?? new RectangleGeometry(new Rect(0, 0, Math.Max(1, w - inset.Left - inset.Right), Math.Max(1, h - inset.Top - inset.Bottom)), Math.Max(0, styleCorner - 4), Math.Max(0, styleCorner - 4));
            var moved = inner.Clone();
            moved.Transform = new TranslateTransform(inset.Left, inset.Top);
            var band = new CombinedGeometry(GeometryCombineMode.Exclude, outline, moved);
            var p = new Path { Data = band, Fill = fill, IsHitTestVisible = false };
            host.Children.Add(p);
            return p;
        }
        Geometry Within(Geometry g) => new CombinedGeometry(GeometryCombineMode.Intersect, outline, g);

        switch (look.Frame)
        {
            case "thin": Stroke(outline, 3, color); break;
            case "thick": Stroke(outline, 9, color); break;
            case "double":
            {
                Stroke(outline, 2.5, color);
                var inner = Outline(look.Shape, w - 16, h - 16, Math.Max(0, styleCorner - 6)) ?? new RectangleGeometry(new Rect(0, 0, w - 16, h - 16), Math.Max(0, styleCorner - 6), Math.Max(0, styleCorner - 6));
                var moved = inner.Clone();
                moved.Transform = new TranslateTransform(8, 8);
                Stroke(moved, 1.5, color);
                break;
            }
            case "glow":
            {
                var p = Stroke(outline, 3, color);
                if (!Perf.Active) p.Effect = new DropShadowEffect { Color = (color as SolidColorBrush)?.Color ?? Colors.White, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 };
                break;
            }
            case "gradient":
            {
                var c = (color as SolidColorBrush)?.Color ?? Colors.White;
                Stroke(outline, 5, new LinearGradientBrush(c, Colors.White, 45));
                break;
            }
            case "dashed": Stroke(outline, 3, color, new DoubleCollection { 3, 2 }); break;
            case "polaroid":
            {
                var p = Band(new Thickness(Math.Round(m(w, h) * 0.05), Math.Round(m(w, h) * 0.05), Math.Round(m(w, h) * 0.05), Math.Round(h * 0.16)), new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEC)));
                if (!Perf.Active) p.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.35 };
                break;
            }
            case "film":
            {
                double band = Math.Max(12, h * 0.075);
                var dark = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16));
                var bands = new GeometryGroup();
                bands.Children.Add(new RectangleGeometry(new Rect(0, 0, w, band)));
                bands.Children.Add(new RectangleGeometry(new Rect(0, h - band, w, band)));
                host.Children.Add(new Path { Data = Within(bands), Fill = dark, IsHitTestVisible = false });
                var holes = new GeometryGroup();
                double hole = band * 0.45, step = hole * 2.1;
                for (double x = step * 0.5; x < w - hole; x += step)
                {
                    holes.Children.Add(new RectangleGeometry(new Rect(x, (band - hole) / 2, hole, hole), 1.5, 1.5));
                    holes.Children.Add(new RectangleGeometry(new Rect(x, h - band + (band - hole) / 2, hole, hole), 1.5, 1.5));
                }
                host.Children.Add(new Path { Data = Within(holes), Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xE2, 0xD8)), IsHitTestVisible = false });
                break;
            }
            case "cartridge":
            {
                var grey = new LinearGradientBrush(Color.FromRgb(0x5A, 0x5A, 0x62), Color.FromRgb(0x34, 0x34, 0x3A), 90);
                double top = Math.Round(h * 0.2);
                Band(new Thickness(Math.Round(w * 0.06), top, Math.Round(w * 0.06), Math.Round(w * 0.06)), grey);
                var ridges = new GeometryGroup();
                for (int i = 0; i < 4; i++) ridges.Children.Add(new RectangleGeometry(new Rect(w * 0.25, top * (0.25 + i * 0.16), w * 0.5, Math.Max(1.5, top * 0.06))));
                host.Children.Add(new Path { Data = ridges, Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x18, 0x18, 0x1C)), IsHitTestVisible = false });
                Stroke(outline, 1.5, new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));
                break;
            }
            case "case":
            {
                double spine = Math.Max(12, w * 0.07);
                host.Children.Add(new Path { Data = Within(new RectangleGeometry(new Rect(0, 0, spine, h))), Fill = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22)), IsHitTestVisible = false });
                host.Children.Add(new Path { Data = Within(new RectangleGeometry(new Rect(spine * 0.42, h * 0.1, spine * 0.16, h * 0.8))), Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false });
                var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0));
                shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.45));
                host.Children.Add(new Path { Data = outline, Fill = shine, IsHitTestVisible = false });
                Stroke(outline, 2, new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)));
                break;
            }
            case "gold":
            {
                var gold = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                gold.GradientStops.Add(new GradientStop(Color.FromRgb(0x8A, 0x62, 0x1E), 0));
                gold.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xD7, 0x7A), 0.3));
                gold.GradientStops.Add(new GradientStop(Color.FromRgb(0xA8, 0x7A, 0x2A), 0.55));
                gold.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xE9, 0xA8), 0.8));
                gold.GradientStops.Add(new GradientStop(Color.FromRgb(0x7A, 0x55, 0x18), 1));
                double t = Math.Max(8, m(w, h) * 0.06);
                Band(new Thickness(t), gold);
                var inner = Outline(look.Shape, w - 2 * t, h - 2 * t, 0) ?? new RectangleGeometry(new Rect(0, 0, w - 2 * t, h - 2 * t));
                var moved = inner.Clone();
                moved.Transform = new TranslateTransform(t, t);
                Stroke(moved, 1.5, new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x08)));
                break;
            }
            case "neon":
            {
                var c = (color as SolidColorBrush)?.Color ?? Colors.Cyan;
                var glow = Stroke(outline, 9, new SolidColorBrush(Color.FromArgb(0xB0, c.R, c.G, c.B)));
                if (!Perf.Active) glow.Effect = new BlurEffect { Radius = 10 };
                Stroke(outline, 2.5, new SolidColorBrush(Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2))));
                break;
            }
            case "pixel":
            {
                double s = Math.Max(5, Math.Round(m(w, h) * 0.035));
                Geometry Stepped(double x, double y, double ww, double hh, double k) => Poly(
                    (x + 2 * k, y), (x + ww - 2 * k, y), (x + ww - 2 * k, y + k), (x + ww - k, y + k), (x + ww - k, y + 2 * k), (x + ww, y + 2 * k),
                    (x + ww, y + hh - 2 * k), (x + ww - k, y + hh - 2 * k), (x + ww - k, y + hh - k), (x + ww - 2 * k, y + hh - k), (x + ww - 2 * k, y + hh),
                    (x + 2 * k, y + hh), (x + 2 * k, y + hh - k), (x + k, y + hh - k), (x + k, y + hh - 2 * k), (x, y + hh - 2 * k),
                    (x, y + 2 * k), (x + k, y + 2 * k), (x + k, y + k), (x + 2 * k, y + k));
                var ring2 = new CombinedGeometry(GeometryCombineMode.Exclude, Stepped(0, 0, w, h, s), Stepped(s, s, w - 2 * s, h - 2 * s, s));
                host.Children.Add(new Path { Data = Within(ring2), Fill = color, IsHitTestVisible = false });
                // The corners outside the steps go dark, so the cover's own corners don't show past the pixels.
                var corners = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), Stepped(0, 0, w, h, s));
                host.Children.Add(new Path { Data = Within(corners), Fill = Brushes.Black, IsHitTestVisible = false });
                break;
            }
            case "sticker":
            {
                var p = Stroke(outline, 10, Brushes.White);
                if (!Perf.Active) p.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.5 };
                break;
            }
        }

        // The highlight along the cut: the style's ring is a rounded rectangle, so with a cut of its own the ring follows it.
        if (ring != null && look.Shape != "style")
        {
            var sel = new Path { Data = outline, IsHitTestVisible = false, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round };
            sel.SetBinding(Shape.StrokeProperty, new Binding(nameof(Border.BorderBrush)) { Source = ring });
            sel.SetBinding(UIElement.EffectProperty, new Binding(nameof(UIElement.Effect)) { Source = ring });
            host.Children.Add(sel);
        }
        static double m(double a, double b) => Math.Min(a, b);
    }

    public static Brush FrameBrush(string id)
    {
        var app = Application.Current;
        Brush? Res(string key) => app?.TryFindResource(key) as Brush;
        return id switch
        {
            "white" => Brushes.White,
            "black" => Brushes.Black,
            "text" => Res("FgBrush") ?? Brushes.White,
            "gold" => new SolidColorBrush(Color.FromRgb(0xE2, 0xB8, 0x4A)),
            _ => Res("AccentBrush") ?? Brushes.White,
        };
    }

    private static Geometry Poly(params (double X, double Y)[] points)
    {
        var fig = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y), IsClosed = true, IsFilled = true };
        for (int i = 1; i < points.Length; i++) fig.Segments.Add(new LineSegment(new Point(points[i].X, points[i].Y), true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    private static Geometry ParseGeo(string data) => Geometry.Parse(data);

    private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static Geometry Frozen(Geometry g) { g.Freeze(); return g; }

    private static double Hash(int n)
    {
        unchecked
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7FFFFFFF) / (double)0x7FFFFFFF;
        }
    }
}
