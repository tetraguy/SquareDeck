using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Game Genie looking for answers: an original genie whose body is one piece. A tapering curl of smoke runs from the
/// lamp's spout up into the robe, the head sits on a neck under the turban, and each arm is jointed at the shoulder
/// (under a sleeve) and the elbow, so the hands can reach for things. Every frame the hands are placed where the act
/// wants them and the joints bend to get there.
///
/// The acts, one after another: peering through a spyglass, reading a book (pages turning), circling a crystal ball,
/// unrolling a scroll, sweeping a magnifying glass over question cards, casting a spell with both hands (sparks fly),
/// and having an idea (question marks rise, a bulb flashes, eyebrows jump). All the while it floats, breathes, blinks,
/// tilts its head, its plume sways, wisps rise from the lamp, the jewel glints and stars twinkle; the mouth smiles,
/// hums, gasps and grins along with what it's doing.
/// </summary>
public sealed class GenieSearch : Grid
{
    private const double W = 360, H = 340;
    private const double ActLength = 3.6, Blend = 0.55;
    private static readonly string[] Lines =
    {
        "Rubbing the lamp…", "Peering into the distance…", "Flipping through ancient guides…", "Consulting the crystal ball…",
        "Unrolling the scroll of secrets…", "Searching every corner…", "Casting a finding spell…", "Asking the speedrunners…",
        "Checking the walkthroughs…", "Polishing the answers…",
    };

    // The body's fixed points.
    private static readonly Point RShoulder = new(206, 154), REl0 = new(230, 184), RHand0 = new(240, 216);
    private static readonly Point LShoulder = new(154, 154), LEl0 = new(130, 184), LHand0 = new(120, 216);
    private static readonly Point Neck = new(180, 136), Spout = new(306, 266), Waist = new(180, 210);

    private sealed class Arm
    {
        public required Point Shoulder, Elbow0, Hand0;
        public required RotateTransform Upper, Fore, Prop;
        public required bool Right;
        public double L1 => (Elbow0 - Shoulder).Length;
        public double L2 => (Hand0 - Elbow0).Length;
    }

    private readonly Canvas _stage = new() { Width = W, Height = H };
    private readonly Canvas _fx = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly TextBlock _line;
    private readonly DispatcherTimer _lines = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private readonly Stopwatch _clock = new();
    private readonly Random _rand = new(11);
    private readonly bool _calm;

    private Arm _right = null!, _left = null!;
    private readonly TranslateTransform _float = new(), _pupils = new(), _brows = new();
    private readonly ScaleTransform _blink = new(1, 1, 180, 99), _breathe = new(1, 1, 180, 210);
    private readonly RotateTransform _tilt = new(0, Neck.X, Neck.Y), _plume = new(0, 182, 72);
    private Path _tail = null!, _mouth = null!;
    private Ellipse _glint = null!;
    private readonly List<FrameworkElement> _props = new();
    private readonly Dictionary<string, FrameworkElement> _bits = new();
    private int _lineIndex;
    private double _last, _nextWisp, _nextSpark, _nextBlink = 2.5;
    private (Point R, Point L, double Gx, double Gy, double Tilt) _from, _now;
    private int _act = -1;
    private double _actStart;

    public GenieSearch(string text)
    {
        _calm = Theme.ReduceMotion;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var art = new Grid { Width = W, Height = H };
        art.Children.Add(_stage);
        stack.Children.Add(new Viewbox { Width = W * 0.95, Height = H * 0.95, Child = art });
        var title = Ui.Text(text, 18, "FgBrush", bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxWidth = 620;
        title.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(title);
        _line = Ui.Text(Lines[0], 15, "SubtleBrush");
        _line.HorizontalAlignment = HorizontalAlignment.Center;
        _line.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_line);
        Children.Add(stack);

        Draw();
        _now = _from = (RHand0, LHand0, 0, 0, 0);
        _lines.Tick += (_, _) => NextLine();
        Loaded += (_, _) =>
        {
            _clock.Start();
            _lines.Start();
            if (_calm) { Frame(); return; }
            CompositionTarget.Rendering += OnFrame;
        };
        Unloaded += (_, _) => { _lines.Stop(); CompositionTarget.Rendering -= OnFrame; };
    }

    // ================================================================ drawing (once)

    private static SolidColorBrush B(byte r, byte g, byte b, byte a = 255) => new(Color.FromArgb(a, r, g, b));
    private static readonly Brush Skin = new LinearGradientBrush(Color.FromRgb(0x86, 0xD6, 0xFF), Color.FromRgb(0x3E, 0x92, 0xE6), 90);
    private static readonly Brush SkinFlat = B(0x5C, 0xB2, 0xF4);
    private static readonly Brush Robe = new LinearGradientBrush(Color.FromRgb(0xA4, 0x63, 0xFF), Color.FromRgb(0x55, 0x2C, 0xC0), 90);
    private static readonly Brush RobeDark = B(0x6A, 0x38, 0xD6);
    private static readonly Brush Gold = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x80), Color.FromRgb(0xC8, 0x8A, 0x22), 90);
    private static readonly Brush Ink = B(0x1A, 0x14, 0x3A);

    private static T At<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    private static Canvas Layer() => new() { Width = W, Height = H, IsHitTestVisible = false };

    private void Draw()
    {
        // Stars that twinkle.
        for (int i = 0; i < 16; i++)
        {
            var star = new Path { Data = Geometry.Parse("M 6,0 L 7.4,4.6 L 12,6 L 7.4,7.4 L 6,12 L 4.6,7.4 L 0,6 L 4.6,4.6 Z"), Fill = B(0xFF, 0xF1, 0xB0), Opacity = 0.25 };
            double s = 0.5 + _rand.NextDouble();
            star.RenderTransform = new ScaleTransform(s, s);
            _stage.Children.Add(At(star, _rand.NextDouble() * (W - 20), 8 + _rand.NextDouble() * 210));
            if (!_calm)
                star.BeginAnimation(OpacityProperty, new DoubleAnimation(0.08, 1, TimeSpan.FromSeconds(0.6 + _rand.NextDouble() * 1.3))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(_rand.NextDouble() * 2) });
        }

        // The lamp: bowl, foot, lid and knob, handle and spout, all one gold piece, glowing.
        var glow = At(new Ellipse { Width = 230, Height = 46, Fill = new RadialGradientBrush(Color.FromArgb(0x90, 0xFF, 0xC8, 0x5A), Color.FromArgb(0, 0xFF, 0xC8, 0x5A)) }, 66, 296);
        _stage.Children.Add(glow);
        if (!_calm) glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var lamp = new Path
        {
            Data = Geometry.Parse(
                "M 118,286 Q 180,276 242,286 Q 254,306 180,312 Q 106,306 118,286 Z " +                    // bowl
                "M 234,286 Q 266,290 290,270 Q 302,262 310,264 Q 302,272 296,280 Q 274,302 240,302 Z " +  // spout
                "M 122,290 C 88,280 86,308 114,302 L 116,296 C 100,298 100,290 122,296 Z " +              // handle
                "M 154,284 Q 180,256 206,284 Z " +                                                     // lid
                "M 158,308 L 202,308 L 210,320 L 150,320 Z"),                                          // foot
            Fill = Gold, Stroke = B(0x8A, 0x5A, 0x10), StrokeThickness = 1.4
        };
        if (!Perf.Active) lamp.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xC8, 0x5A), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 };
        _stage.Children.Add(lamp);
        _stage.Children.Add(At(new Ellipse { Width = 11, Height = 11, Fill = Gold, Stroke = B(0x8A, 0x5A, 0x10), StrokeThickness = 1.2 }, 174.5, 252));
        _glint = At(new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Opacity = 0 }, 140, 288);
        if (!Perf.Active) _glint.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 10, ShadowDepth = 0 };
        _stage.Children.Add(_glint);

        // Everything above the lamp floats together (the tail's end stays at the spout: it's redrawn every frame).
        _tail = new Path { Fill = new LinearGradientBrush(Color.FromArgb(0x60, 0xB8, 0xE8, 0xFF), Color.FromArgb(0xF0, 0x6E, 0xC6, 0xFF), new Point(1, 1), new Point(0, 0)) };
        _stage.Children.Add(_tail);

        var body = Layer();
        body.RenderTransform = _float;
        _stage.Children.Add(body);

        // Arms first (behind the robe); sleeves go over the shoulders afterwards.
        _right = MakeArm(RShoulder, REl0, RHand0, right: true, body);
        _left = MakeArm(LShoulder, LEl0, LHand0, right: false, body);

        // Neck, robe, belt and gem, breathing.
        var torso = Layer();
        torso.RenderTransform = _breathe;
        body.Children.Add(torso);
        torso.Children.Add(new Path { Data = Geometry.Parse("M 170,124 L 190,124 L 193,146 L 167,146 Z"), Fill = SkinFlat });
        torso.Children.Add(new Path { Data = Geometry.Parse("M 148,148 C 154,140 166,136 180,136 C 194,136 206,140 212,148 L 224,210 Q 180,226 136,210 Z"), Fill = Robe });
        torso.Children.Add(new Path { Data = Geometry.Parse("M 166,138 L 180,160 L 194,138 Q 180,134 166,138 Z"), Fill = SkinFlat });          // the collar's V
        torso.Children.Add(new Path { Data = Geometry.Parse("M 141,186 Q 180,200 219,186 L 221,198 Q 180,214 139,198 Z"), Fill = Gold });         // belt
        torso.Children.Add(At(new Ellipse { Width = 13, Height = 13, Fill = new RadialGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x9A), Color.FromRgb(0xC0, 0x1E, 0x48)) }, 173.5, 192));
        foreach (var sh in new[] { RShoulder, LShoulder })
            body.Children.Add(At(new Ellipse { Width = 30, Height = 26, Fill = RobeDark, Stroke = B(0x8A, 0x5A, 0xFF, 0x80), StrokeThickness = 1 }, sh.X - 15, sh.Y - 14));

        // The head: face, ears, eyes that look about and blink, brows, a mouth that changes, a small beard, and a turban.
        var head = Layer();
        head.RenderTransform = _tilt;
        body.Children.Add(head);
        head.Children.Add(new Path { Data = Geometry.Parse("M 152,102 q -14,-6 -12,8 q 4,10 14,2 Z M 208,102 q 14,-6 12,8 q -4,10 -14,2 Z"), Fill = SkinFlat });
        head.Children.Add(At(new Ellipse { Width = 60, Height = 64, Fill = Skin }, 150, 72));
        var eyes = Layer();
        eyes.RenderTransform = _blink;
        foreach (double x in new[] { 163.0, 183.0 })
        {
            eyes.Children.Add(At(new Ellipse { Width = 14, Height = 16, Fill = Brushes.White }, x, 91));
            eyes.Children.Add(At(new Ellipse { Width = 7, Height = 8, Fill = Ink, RenderTransform = _pupils }, x + 3.5, 95));
        }
        head.Children.Add(eyes);
        head.Children.Add(new Path { Data = Geometry.Parse("M 162,86 Q 170,81 177,85 M 183,85 Q 190,81 198,86"), Stroke = Ink, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = _brows });
        _mouth = new Path { Stroke = Ink, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        head.Children.Add(_mouth);
        head.Children.Add(new Path { Data = Geometry.Parse("M 172,130 Q 180,148 188,130 Q 180,135 172,130 Z"), Fill = Ink });
        head.Children.Add(new Path { Data = Geometry.Parse("M 146,88 C 138,52 222,52 214,88 Q 180,76 146,88 Z"), Fill = Robe });
        head.Children.Add(new Path { Data = Geometry.Parse("M 146,88 Q 180,76 214,88 L 214,95 Q 180,83 146,95 Z"), Fill = Gold });
        var plume = new Path { Data = Geometry.Parse("M 181,70 C 176,44 198,30 210,38 C 196,42 189,54 186,70 Z"), Fill = B(0xFF, 0x7A, 0xC6), RenderTransform = _plume };
        head.Children.Add(plume);
        var jewel = At(new Ellipse { Width = 15, Height = 15, Fill = new RadialGradientBrush(Color.FromRgb(0x6A, 0xFF, 0xD6), Color.FromRgb(0x10, 0x9E, 0x86)) }, 172.5, 68);
        head.Children.Add(jewel);

        // Things the Genie works with, each shown during its act.
        BuildProps(body);
        body.Children.Add(_fx);
        Mouth("smile");
    }

    private Arm MakeArm(Point shoulder, Point elbow, Point hand, bool right, Panel into)
    {
        var upper = new RotateTransform(0, shoulder.X, shoulder.Y);
        var fore = new RotateTransform(0, elbow.X, elbow.Y);
        var prop = new RotateTransform(0, hand.X, hand.Y);
        var root = Layer();
        root.RenderTransform = upper;
        root.Children.Add(new Line { X1 = shoulder.X, Y1 = shoulder.Y, X2 = elbow.X, Y2 = elbow.Y, Stroke = SkinFlat, StrokeThickness = 17, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var forearm = Layer();
        forearm.RenderTransform = fore;
        forearm.Children.Add(new Line { X1 = elbow.X, Y1 = elbow.Y, X2 = hand.X, Y2 = hand.Y, Stroke = SkinFlat, StrokeThickness = 15, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        // A gold cuff at the wrist, then the hand.
        var held = Layer();
        held.RenderTransform = prop;
        held.Name = right ? "RightHeld" : "LeftHeld";
        forearm.Children.Add(held);
        forearm.Children.Add(At(new Ellipse { Width = 22, Height = 20, Fill = B(0x7C, 0xC8, 0xFF) }, hand.X - 11, hand.Y - 10));
        root.Children.Add(forearm);
        into.Children.Add(root);
        _bits[right ? "rheld" : "lheld"] = held;
        return new Arm { Shoulder = shoulder, Elbow0 = elbow, Hand0 = hand, Upper = upper, Fore = fore, Prop = prop, Right = right };
    }

    private Canvas Prop(string name, Panel into)
    {
        var c = Layer();
        c.Opacity = 0;
        c.Tag = name;
        into.Children.Add(c);
        _props.Add(c);
        return c;
    }

    private void BuildProps(Panel body)
    {
        var rheld = (Panel)_bits["rheld"];
        var h = RHand0;

        // Spyglass, in the right hand, pointing out to the right.
        var spy = Prop("spyglass", rheld);
        spy.Children.Add(new Path { Data = Geometry.Parse($"M {h.X - 14},{h.Y - 5} L {h.X + 46},{h.Y - 9} L {h.X + 46},{h.Y + 3} L {h.X - 14},{h.Y + 5} Z M {h.X + 46},{h.Y - 12} L {h.X + 70},{h.Y - 14} L {h.X + 70},{h.Y + 6} L {h.X + 46},{h.Y + 5} Z"), Fill = Gold, Stroke = B(0x8A, 0x5A, 0x10), StrokeThickness = 1.3 });
        spy.Children.Add(At(new Ellipse { Width = 8, Height = 18, Fill = new RadialGradientBrush(Colors.White, Color.FromRgb(0x7F, 0xD3, 0xFF)) }, h.X + 67, h.Y - 13));

        // Magnifying glass, in the right hand, lens up and out.
        var mag = Prop("magnifier", rheld);
        mag.Children.Add(new Line { X1 = h.X - 4, Y1 = h.Y + 6, X2 = h.X + 18, Y2 = h.Y - 18, Stroke = B(0x7A, 0x4A, 0x1A), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round });
        mag.Children.Add(At(new Ellipse { Width = 34, Height = 34, Stroke = Gold, StrokeThickness = 5, Fill = new RadialGradientBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x30, 0x7F, 0xD3, 0xFF)) }, h.X + 12, h.Y - 48));

        // Book, floating in front, a page turning over and over.
        var book = Prop("book", body);
        book.Children.Add(new Path { Data = Geometry.Parse("M 120,218 L 180,230 L 240,218 L 240,268 L 180,280 L 120,268 Z"), Fill = B(0x7A, 0x2E, 0x1E), Stroke = Gold, StrokeThickness = 2 });
        book.Children.Add(new Path { Data = Geometry.Parse("M 126,218 L 180,228 L 180,274 L 126,262 Z"), Fill = B(0xFF, 0xF6, 0xE0) });
        book.Children.Add(new Path { Data = Geometry.Parse("M 180,228 L 234,218 L 234,262 L 180,274 Z"), Fill = B(0xFF, 0xF0, 0xD0) });
        for (int i = 0; i < 4; i++)
        {
            book.Children.Add(new Line { X1 = 134, Y1 = 232 + i * 8, X2 = 172, Y2 = 238 + i * 8, Stroke = B(0x9A, 0x8A, 0x70), StrokeThickness = 1.4 });
            book.Children.Add(new Line { X1 = 188, Y1 = 238 + i * 8, X2 = 226, Y2 = 232 + i * 8, Stroke = B(0x9A, 0x8A, 0x70), StrokeThickness = 1.4 });
        }
        var page = new Path { Data = Geometry.Parse("M 180,228 L 232,218 L 232,262 L 180,274 Z"), Fill = B(0xFF, 0xFB, 0xEE), Stroke = B(0xD8, 0xC8, 0xA0), StrokeThickness = 1, RenderTransform = new ScaleTransform(1, 1, 180, 0) };
        book.Children.Add(page);
        _bits["page"] = page;

        // Crystal ball on a gold stand, with a swirl inside.
        var ball = Prop("crystal", body);
        ball.Children.Add(new Path { Data = Geometry.Parse("M 158,266 L 202,266 L 196,280 L 164,280 Z"), Fill = Gold });
        var orb = At(new Ellipse { Width = 66, Height = 66, Fill = new RadialGradientBrush(Color.FromArgb(0xE8, 0xE0, 0xD0, 0xFF), Color.FromArgb(0xB8, 0x5A, 0x2E, 0xC8)) { GradientOrigin = new Point(0.35, 0.3) } }, 147, 204);
        if (!Perf.Active) orb.Effect = new DropShadowEffect { Color = Color.FromRgb(0xB0, 0x7A, 0xFF), BlurRadius = 28, ShadowDepth = 0 };
        ball.Children.Add(orb);
        var swirl = new Path { Data = Geometry.Parse("M 180,216 C 206,220 208,250 182,254 C 160,256 158,232 178,230 C 190,230 190,242 182,242"), Stroke = B(0xFF, 0xFF, 0xFF, 0xC0), StrokeThickness = 2.4, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new RotateTransform(0, 180, 237) };
        ball.Children.Add(swirl);
        _bits["swirl"] = swirl;

        // Scroll hanging from a rod, unrolling.
        var scroll = Prop("scroll", body);
        var paper = At(new Border { Width = 92, Height = 0, Background = B(0xFF, 0xF1, 0xCC), BorderBrush = B(0xC8, 0xA0, 0x60), BorderThickness = new Thickness(1.5, 0, 1.5, 0), ClipToBounds = true }, 238, 156);
        var lines = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        for (int i = 0; i < 10; i++) lines.Children.Add(new Border { Height = 3, Margin = new Thickness(0, 0, i % 3 == 2 ? 24 : 0, 7), Background = B(0xA0, 0x80, 0x50, 0xA0), CornerRadius = new CornerRadius(1.5) });
        paper.Child = lines;
        scroll.Children.Add(paper);
        scroll.Children.Add(At(new Border { Width = 106, Height = 12, CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Color.FromRgb(0xC8, 0x8A, 0x4A), Color.FromRgb(0x7A, 0x4A, 0x1A), 90) }, 231, 148));
        var bottom = At(new Border { Width = 106, Height = 14, CornerRadius = new CornerRadius(7), Background = new LinearGradientBrush(Color.FromRgb(0xC8, 0x8A, 0x4A), Color.FromRgb(0x7A, 0x4A, 0x1A), 90), RenderTransform = new TranslateTransform() }, 231, 152);
        scroll.Children.Add(bottom);
        _bits["paper"] = paper;
        _bits["rod"] = bottom;

        // Question cards to search over.
        var cards = Prop("cards", body);
        foreach (var (x, y, r) in new[] { (262.0, 150.0, -8.0), (290.0, 196.0, 6.0), (250.0, 238.0, -4.0) })
        {
            var card = new Border { Width = 40, Height = 50, CornerRadius = new CornerRadius(6), Background = B(0xFF, 0xF6, 0xE0), BorderBrush = Gold, BorderThickness = new Thickness(2), RenderTransform = new RotateTransform(r, 20, 25),
                Child = new TextBlock { Text = "?", FontSize = 28, FontWeight = FontWeights.Black, Foreground = RobeDark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            cards.Children.Add(At(card, x, y));
        }

        // Spell: rings of light around the raised hands are drawn as sparks in the effects layer; nothing to hold.
        Prop("magic", body);

        // Idea: question marks rising, then a light bulb.
        var idea = Prop("idea", body);
        for (int i = 0; i < 3; i++)
        {
            var q = At(new TextBlock { Text = "?", FontSize = 26 + i * 5, FontWeight = FontWeights.Black, Foreground = B(0xFF, 0xD1, 0x6A), RenderTransform = new TranslateTransform() }, 222 + i * 22, 70 - i * 12);
            idea.Children.Add(q);
            _bits["q" + i] = q;
        }
        var bulb = Layer();
        var glass = At(new Ellipse { Width = 44, Height = 44, Fill = new RadialGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xD0), Color.FromRgb(0xFF, 0xD1, 0x3A)) }, 158, 4);
        if (!Perf.Active) glass.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xE0, 0x6A), BlurRadius = 34, ShadowDepth = 0 };
        bulb.Children.Add(glass);
        bulb.Children.Add(At(new Border { Width = 20, Height = 12, CornerRadius = new CornerRadius(3), Background = B(0x9A, 0x9A, 0xA8) }, 170, 46));
        bulb.RenderTransform = new ScaleTransform(0, 0, 180, 28);
        idea.Children.Add(bulb);
        _bits["bulb"] = bulb;
    }

    private void Mouth(string kind) => _mouth.Data = Geometry.Parse(kind switch
    {
        "open" => "M 175,117 a 5,6 0 1 0 10,0 a 5,6 0 1 0 -10,0",       // a gasp
        "grin" => "M 166,114 Q 180,130 194,114 Z",                        // a big grin
        "hum" => "M 171,119 Q 180,121 189,118",                           // concentrating
        _ => "M 168,115 Q 180,126 192,115",                               // a smile
    });

    // ================================================================ every frame

    private void OnFrame(object? sender, EventArgs e) => Frame();

    private void Frame()
    {
        double t = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(t - _last, 0, 0.1);
        _last = t;

        // A new act every few seconds; the pose blends from where the last one left off.
        int act = (int)(t / ActLength) % 7;
        if (act != _act)
        {
            _from = _now;
            _act = act;
            _actStart = Math.Floor(t / ActLength) * ActLength;
            StartAct(act);
        }
        double at = t - _actStart;
        var goal = Pose(act, at, t);
        double k = Math.Clamp(at / Blend, 0, 1);
        k = k * k * (3 - 2 * k);
        _now = (Lerp(_from.R, goal.R, k), Lerp(_from.L, goal.L, k), _from.Gx + (goal.Gx - _from.Gx) * k, _from.Gy + (goal.Gy - _from.Gy) * k, _from.Tilt + (goal.Tilt - _from.Tilt) * k);

        // Float, breathe, sway.
        double bob = _calm ? 0 : -6 * Math.Sin(t * 2.2);
        _float.Y = bob;
        _breathe.ScaleX = 1 + 0.015 * Math.Sin(t * 2.6);
        _breathe.ScaleY = 1 - 0.012 * Math.Sin(t * 2.6);
        _plume.Angle = 9 * Math.Sin(t * 3.1);
        _tilt.Angle = _now.Tilt + (_calm ? 0 : 2 * Math.Sin(t * 1.3));
        _pupils.X = _now.Gx;
        _pupils.Y = _now.Gy;
        Tail(t, bob);

        // Blink now and then.
        if (t >= _nextBlink) { _nextBlink = t + 2.2 + _rand.NextDouble() * 2.5; }
        double blinkPhase = (_nextBlink - t);
        _blink.ScaleY = blinkPhase < 0.12 ? Math.Max(0.1, Math.Abs(blinkPhase - 0.06) / 0.06) : 1;

        // The arms reach for their targets (the body floats, so they're placed relative to it).
        Reach(_right, _now.R);
        Reach(_left, _now.L);

        // The act's own moving parts.
        Animate(act, at, t);

        if (_calm || dt <= 0) return;
        // Wisps rising from the spout, a glint on the lamp, sparks.
        if (t >= _nextWisp) { Wisp(); _nextWisp = t + 0.25 + _rand.NextDouble() * 0.25; }
        if (t >= _nextSpark && act == 5) { Spark(Hand(_right)); Spark(Hand(_left)); _nextSpark = t + 0.07; }
        double g = (t % 3.2) / 3.2;
        Canvas.SetLeft(_glint, 126 + g * 100);
        _glint.Opacity = g < 0.5 ? Math.Sin(g * 2 * Math.PI) * 0.9 : 0;
    }

    /// <summary>Where the hands go, where the eyes look and how the head tilts, for an act at a moment in it.</summary>
    private static (Point R, Point L, double Gx, double Gy, double Tilt) Pose(int act, double at, double t)
    {
        double s = Math.Sin(t * 5), c = Math.Cos(t * 5);
        return act switch
        {
            0 => (new Point(214, 101 + 2 * Math.Sin(t * 3)), new Point(140, 206), at < 2 ? 3.5 : -3, -0.5, -4),                                   // spyglass: looks out, then glances back
            1 => (new Point(228, 240 + 3 * Math.Sin(t * 6)), new Point(132, 240 + 3 * Math.Sin(t * 6 + 1)), 3 * Math.Sin(t * 4), 3, 3),              // book: reads along the lines
            2 => (new Point(224 + 8 * Math.Sin(t * 3), 210 + 6 * Math.Cos(t * 3)), new Point(136 - 8 * Math.Sin(t * 3), 210 + 6 * Math.Cos(t * 3)), 0, 3.2, 0), // crystal ball: hands circle
            3 => (new Point(270, 152), new Point(138, 204), 3, -1 + Math.Min(4, at * 1.5), 5),                                                       // scroll: reads down it
            4 => (new Point(236 + 30 * Math.Sin(at * 2.2), 196 + 12 * Math.Cos(at * 4.4)), new Point(138, 206), 2 + 2 * Math.Sin(at * 2.2), 1 + Math.Cos(at * 4.4), 4), // magnifier: sweeps the cards
            5 => (new Point(254 + 12 * c, 108 + 12 * s), new Point(106 - 12 * c, 108 + 12 * s), 0, -2.5, 0),                                        // spell: both hands up, circling
            _ => (new Point(232, 96), new Point(166, 140), at < 1.7 ? 3 : 0, at < 1.7 ? -2.5 : 0, at < 1.7 ? -6 : 2),                                 // idea: a finger raised, hand on chin
        };
    }

    private void StartAct(int act)
    {
        var fade = TimeSpan.FromMilliseconds(280);
        string name = act switch { 0 => "spyglass", 1 => "book", 2 => "crystal", 3 => "scroll", 4 => "magnifier", 5 => "magic", _ => "idea" };
        foreach (var p in _props)
        {
            bool on = (string)p.Tag == name || (name == "magnifier" && (string)p.Tag == "cards");
            p.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, fade));
        }
        Mouth(act switch { 1 or 3 or 4 => "hum", 5 => "grin", _ => "smile" });
        if (act == 3 && _bits["paper"] is Border paper && _bits["rod"] is Border rod && rod.RenderTransform is TranslateTransform drop)
        {
            var unroll = new DoubleAnimation(0, 118, TimeSpan.FromSeconds(1.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (_calm) { paper.Height = 118; drop.Y = 118; }
            else { paper.BeginAnimation(HeightProperty, unroll); drop.BeginAnimation(TranslateTransform.YProperty, unroll); }
        }
        if (act == 6)
        {
            for (int i = 0; i < 3; i++)
            {
                var q = (TextBlock)_bits["q" + i];
                q.Opacity = 0;
                ((TranslateTransform)q.RenderTransform).Y = 0;
            }
            if (_bits["bulb"].RenderTransform is ScaleTransform pop) { pop.BeginAnimation(ScaleTransform.ScaleXProperty, null); pop.BeginAnimation(ScaleTransform.ScaleYProperty, null); pop.ScaleX = pop.ScaleY = 0; }
            _brows.Y = 0;
        }
    }

    private void Animate(int act, double at, double t)
    {
        if (act == 1 && _bits["page"].RenderTransform is ScaleTransform flip) flip.ScaleX = Math.Cos(t * 5.5);
        if (act == 2 && _bits["swirl"].RenderTransform is RotateTransform spin) spin.Angle = t * 220 % 360;
        if (act == 6)
        {
            for (int i = 0; i < 3; i++)
            {
                var q = (TextBlock)_bits["q" + i];
                double local = at - i * 0.25;
                q.Opacity = local < 0 ? 0 : local < 0.25 ? local / 0.25 : local < 1.3 ? 1 : Math.Max(0, 1 - (local - 1.3) / 0.3);
                ((TranslateTransform)q.RenderTransform).Y = -Math.Max(0, local) * 22;
            }
            if (at >= 1.7 && _bits["bulb"].RenderTransform is ScaleTransform pop && pop.ScaleX == 0 && !pop.HasAnimatedProperties)
            {
                var spring = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5 } };
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                Mouth("open");
                for (int i = 0; i < 10; i++) Spark(new Point(180, 30));
            }
            if (at >= 2.2 && at < 2.25) Mouth("grin");
            _brows.Y = at >= 1.7 && at < 2.6 ? -5 : 0;
        }
    }

    // ================================================================ joints

    /// <summary>Bends the shoulder and elbow so the hand lands on the target (or as close as the arm reaches).</summary>
    private static void Reach(Arm arm, Point target)
    {
        double l1 = arm.L1, l2 = arm.L2;
        var d = target - arm.Shoulder;
        double dist = Math.Clamp(d.Length, Math.Abs(l1 - l2) + 1, l1 + l2 - 0.5);
        double baseAngle = Math.Atan2(d.Y, d.X);
        double a = Math.Acos(Math.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1));
        // Of the two ways to bend, the one with the elbow out to the side.
        double u1 = baseAngle + a, u2 = baseAngle - a;
        var e1 = arm.Shoulder + new Vector(Math.Cos(u1), Math.Sin(u1)) * l1;
        var e2 = arm.Shoulder + new Vector(Math.Cos(u2), Math.Sin(u2)) * l1;
        double u = arm.Right ? (e1.X >= e2.X ? u1 : u2) : (e1.X <= e2.X ? u1 : u2);
        var elbow = arm.Shoulder + new Vector(Math.Cos(u), Math.Sin(u)) * l1;
        var hand = arm.Shoulder + d / d.Length * dist;
        double f = Math.Atan2(hand.Y - elbow.Y, hand.X - elbow.X);
        double u0 = Math.Atan2(arm.Elbow0.Y - arm.Shoulder.Y, arm.Elbow0.X - arm.Shoulder.X);
        double f0 = Math.Atan2(arm.Hand0.Y - arm.Elbow0.Y, arm.Hand0.X - arm.Elbow0.X);
        double up = Deg(u - u0), fore = Deg((f - u) - (f0 - u0));
        arm.Upper.Angle = up;
        arm.Fore.Angle = fore;
        arm.Prop.Angle = -(up + fore); // what it holds stays the way it was drawn
    }

    private static double Deg(double radians)
    {
        double d = radians * 180 / Math.PI;
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }

    private Point Hand(Arm arm)
    {
        var p = arm.Fore.Transform(arm.Hand0);
        p = arm.Upper.Transform(p);
        return new Point(p.X, p.Y + _float.Y);
    }

    private static Point Lerp(Point a, Point b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    // ================================================================ smoke, wisps and sparks

    /// <summary>The curl of smoke: from a thin wisp at the spout to the full width of the robe, swaying as the Genie floats.</summary>
    private void Tail(double t, double bob)
    {
        double sway = _calm ? 0 : Math.Sin(t * 1.8);
        var p0 = Spout;
        var p1 = new Point(336 + 10 * sway, 230);
        var p2 = new Point(250 - 14 * sway, 252 + bob * 0.5);
        var p3 = new Point(Waist.X, Waist.Y + bob);
        const int n = 30;
        var left = new List<Point>();
        var right = new List<Point>();
        for (int i = 0; i <= n; i++)
        {
            double u = i / (double)n, v = 1 - u;
            var p = new Point(v * v * v * p0.X + 3 * v * v * u * p1.X + 3 * v * u * u * p2.X + u * u * u * p3.X,
                              v * v * v * p0.Y + 3 * v * v * u * p1.Y + 3 * v * u * u * p2.Y + u * u * u * p3.Y);
            var tan = new Vector(3 * v * v * (p1.X - p0.X) + 6 * v * u * (p2.X - p1.X) + 3 * u * u * (p3.X - p2.X),
                                 3 * v * v * (p1.Y - p0.Y) + 6 * v * u * (p2.Y - p1.Y) + 3 * u * u * (p3.Y - p2.Y));
            if (tan.Length < 0.001) tan = new Vector(0, -1);
            tan.Normalize();
            var normal = new Vector(-tan.Y, tan.X);
            double half = 3 + 37 * Math.Pow(u, 1.6) + (_calm ? 0 : 1.5 * Math.Sin(u * 12 - t * 6) * u);
            left.Add(p + normal * half);
            right.Add(p - normal * half);
        }
        var fig = new PathFigure { StartPoint = left[0], IsClosed = true };
        fig.Segments.Add(new PolyLineSegment(left.Skip(1), true));
        right.Reverse();
        fig.Segments.Add(new PolyLineSegment(right, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        _tail.Data = geo;
    }

    private void Wisp()
    {
        double size = 6 + _rand.NextDouble() * 8;
        var w = At(new Ellipse { Width = size, Height = size, Fill = B(0xC8, 0xEC, 0xFF, 0x90) }, Spout.X - size / 2 + (_rand.NextDouble() - 0.5) * 6, Spout.Y - size / 2);
        _stage.Children.Add(w);
        var move = new TranslateTransform();
        w.RenderTransform = move;
        var life = TimeSpan.FromSeconds(1.4);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -50 - _rand.NextDouble() * 30, life));
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, (_rand.NextDouble() - 0.3) * 30, life));
        var fade = new DoubleAnimation(0.9, 0, life);
        fade.Completed += (_, _) => _stage.Children.Remove(w);
        w.BeginAnimation(OpacityProperty, fade);
    }

    private void Spark(Point from)
    {
        var colors = new[] { Color.FromRgb(0xFF, 0xE0, 0x6A), Color.FromRgb(0x7F, 0xFF, 0xE9), Color.FromRgb(0xFF, 0x9A, 0xE0), Colors.White };
        var star = new Path { Data = Geometry.Parse("M 4,0 L 5,3 L 8,4 L 5,5 L 4,8 L 3,5 L 0,4 L 3,3 Z"), Fill = new SolidColorBrush(colors[_rand.Next(colors.Length)]) };
        At(star, from.X - 4, from.Y - 4);
        _stage.Children.Add(star);
        var move = new TranslateTransform();
        star.RenderTransform = move;
        double angle = _rand.NextDouble() * Math.PI * 2, dist = 20 + _rand.NextDouble() * 40;
        var life = TimeSpan.FromMilliseconds(600 + _rand.Next(400));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * dist, life) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * dist, life) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, life);
        fade.Completed += (_, _) => _stage.Children.Remove(star);
        star.BeginAnimation(OpacityProperty, fade);
    }

    private void NextLine()
    {
        _lineIndex = (_lineIndex + 1) % Lines.Length;
        var next = Lines[_lineIndex];
        var out1 = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        out1.Completed += (_, _) =>
        {
            _line.Text = next;
            _line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        };
        _line.BeginAnimation(OpacityProperty, out1);
    }
}
