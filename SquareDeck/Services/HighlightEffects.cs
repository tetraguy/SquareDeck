using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SquareDeck.Services;

/// <summary>
/// Highlight effects (Settings > Themes > Highlight color > Effects): the selection ring drawn with your own colors
/// and set moving: spinning, flowing, striped, shimmering, a comet round the edge, cycling, breathing and more,
/// with its own speed and glow. Reduce motion and Performance mode keep the colors and stop the movement.
/// </summary>
public static class HighlightEffects
{
    public static readonly (string Id, string Name, string About)[] All =
    {
        ("", "None", "The plain highlight color, or the UI style's own ring"),
        ("gradient", "Gradient", "Your colors blended across the highlight, standing still"),
        ("spin", "Spin", "The colors turn slowly around the highlight"),
        ("flow", "Flow", "The colors stream along the highlight, over and over"),
        ("stripes", "Candy stripes", "Hard-edged stripes of your colors marching along"),
        ("shimmer", "Shimmer", "A bright shine sweeps across every few seconds"),
        ("comet", "Comet", "A bright spot races around the edge"),
        ("cycle", "Color cycle", "The whole highlight fades from one color to the next"),
        ("breathe", "Breathe", "Slowly fades in and out"),
        ("heartbeat", "Heartbeat", "Two quick beats, then a rest"),
        ("flicker", "Neon flicker", "Steady, with the odd flicker of an old neon sign"),
    };

    /// <summary>Ready-made color sets; "accent" is built from the highlight color.</summary>
    public static readonly (string Id, string Name, string[] Colors)[] Palettes =
    {
        ("accent", "Highlight color", Array.Empty<string>()),
        ("switch2", "Switch 2", new[] { "#3F7CFF", "#7B6CFF", "#C46BE6", "#FF7FB5", "#55C8FF" }),
        ("rainbow", "Rainbow", new[] { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#00C7BE", "#007AFF", "#AF52DE" }),
        ("sunset", "Sunset", new[] { "#FF5E62", "#FF9966", "#FFD86F", "#C850C0" }),
        ("ocean", "Ocean", new[] { "#00C6FF", "#0072FF", "#00F2C3" }),
        ("aurora", "Aurora", new[] { "#00FFA3", "#03E1FF", "#7C4DFF", "#DC1FFF" }),
        ("fire", "Fire", new[] { "#FF2200", "#FF7A00", "#FFD000", "#FF4400" }),
        ("ice", "Ice", new[] { "#E0F7FF", "#7FDBFF", "#3B82F6", "#FFFFFF" }),
        ("candy", "Candy", new[] { "#FF4FA3", "#FFFFFF", "#4FD1FF" }),
        ("synthwave", "Synthwave", new[] { "#FF2BD6", "#7A2BFF", "#22D3EE" }),
        ("gold", "Gold", new[] { "#B8860B", "#FFD700", "#FFF5C0", "#DAA520" }),
        ("toxic", "Toxic", new[] { "#39FF14", "#CCFF00", "#00FF9C" }),
        ("mono", "Silver", new[] { "#FFFFFF", "#9AA0A6", "#E8EAED", "#5F6368" }),
    };

    public static readonly (string Id, string Name)[] Speeds = { ("slow", "Slow"), ("normal", "Normal"), ("fast", "Fast") };
    public static readonly (string Id, string Name)[] Glows = { ("style", "The style's own"), ("off", "Off"), ("soft", "Soft"), ("strong", "Strong") };

    /// <summary>Switch's own highlight: the Switch 2 colors blended round the ring.</summary>
    public const string SwitchEffect = "gradient", SwitchColors = "palette:switch2";

    /// <summary>Whether the highlight is still Switch's own.</summary>
    public static bool IsSwitchDefault(Settings s) => s.HighlightEffect == SwitchEffect && s.HighlightColors == SwitchColors;

    public static string Effect { get; private set; } = "";
    public static string Colors { get; private set; } = "palette:accent";
    public static string Speed { get; private set; } = "normal";
    public static string Glow { get; private set; } = "style";

    /// <summary>Whether an effect replaces the normal ring.</summary>
    public static bool Active => Effect.Length > 0;

    public static void Load(Settings s)
    {
        Effect = All.Any(e => e.Id == s.HighlightEffect) ? s.HighlightEffect ?? "" : "";
        Colors = string.IsNullOrWhiteSpace(s.HighlightColors) ? "palette:accent" : s.HighlightColors;
        Speed = Speeds.Any(x => x.Id == s.HighlightSpeed) ? s.HighlightSpeed! : "normal";
        Glow = Glows.Any(x => x.Id == s.HighlightGlow) ? s.HighlightGlow! : "style";
    }

    /// <summary>The palette's id, or "" for the user's own list.</summary>
    public static string PaletteId => Colors.StartsWith("palette:") ? Colors[8..] : "";

    /// <summary>The colors as hex, after a ready-made set is applied (the highlight color's set uses the current color).</summary>
    public static List<string> Hexes(Color accent)
    {
        if (Colors.StartsWith("palette:"))
        {
            var palette = Palettes.FirstOrDefault(p => p.Id == PaletteId);
            if (palette.Colors is { Length: > 1 }) return palette.Colors.ToList();
            return new[] { accent, Mix(accent, System.Windows.Media.Colors.White, 0.6), accent, Mix(accent, System.Windows.Media.Colors.Black, 0.35) }
                .Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToList();
        }
        var list = Colors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => Theme.TryParseColor(h, out _)).ToList();
        while (list.Count < 2) list.Add(list.Count == 0 ? $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}" : "#FFFFFF");
        return list;
    }

    private static Color[] ColorsFor(Color accent) =>
        Hexes(accent).Select(h => Theme.TryParseColor(h, out var c) ? c : accent).ToArray();

    private static double Pace => Speed switch { "slow" => 1.8, "fast" => 0.5, _ => 1.0 };

    private static bool Moving => !Theme.ReduceMotion && !Perf.Active;

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s * Pace);

    /// <summary>The ring's brush for the current effect.</summary>
    public static Brush Build(Color accent) => Build(Effect, accent);

    public static Brush Build(string effect, Color accent)
    {
        var c = ColorsFor(accent);
        var forever = RepeatBehavior.Forever;
        switch (effect)
        {
            case "spin":
            {
                var brush = Linear(c, new Point(0, 0), new Point(1, 1));
                if (!Moving) return Freeze(brush);
                var turn = new RotateTransform(0, 0.5, 0.5);
                brush.RelativeTransform = turn;
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, Seconds(6)) { RepeatBehavior = forever });
                return brush;
            }
            case "flow":
            {
                // The first color again at the end, so the repeat is seamless.
                var brush = Linear(c.Append(c[0]).ToArray(), new Point(0, 0), new Point(0.5, 0.5));
                brush.SpreadMethod = GradientSpreadMethod.Repeat;
                if (!Moving) return Freeze(brush);
                var move = new TranslateTransform();
                brush.RelativeTransform = move;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 1, Seconds(3)) { RepeatBehavior = forever });
                return brush;
            }
            case "stripes":
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.1, 0.1), SpreadMethod = GradientSpreadMethod.Repeat };
                for (int i = 0; i < c.Length; i++)
                {
                    brush.GradientStops.Add(new GradientStop(c[i], i / (double)c.Length));
                    brush.GradientStops.Add(new GradientStop(c[i], (i + 1) / (double)c.Length - 0.0001));
                }
                if (!Moving) return Freeze(brush);
                var move = new TranslateTransform();
                brush.RelativeTransform = move;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 0.2, Seconds(0.9)) { RepeatBehavior = forever });
                return brush;
            }
            case "shimmer":
            {
                var brush = Linear(c, new Point(0, 0), new Point(1, 1));
                if (!Moving) return Freeze(brush);
                // A band of light that crosses, then rests.
                var shine = new GradientStop(System.Windows.Media.Colors.White, -0.3);
                var lead = new GradientStop(Mix(c[0], c[^1], 0.5), -0.42);
                var tail = new GradientStop(Mix(c[0], c[^1], 0.5), -0.18);
                brush.GradientStops.Add(lead); brush.GradientStops.Add(shine); brush.GradientStops.Add(tail);
                foreach (var (stop, offset) in new[] { (lead, -0.12), (shine, 0.0), (tail, 0.12) })
                {
                    var sweep = new DoubleAnimationUsingKeyFrames { Duration = Seconds(3.4), RepeatBehavior = forever };
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(offset - 0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    sweep.KeyFrames.Add(new EasingDoubleKeyFrame(offset + 1.3, KeyTime.FromTimeSpan(Seconds(1.2))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(offset + 1.3, KeyTime.FromTimeSpan(Seconds(3.4))));
                    stop.BeginAnimation(GradientStop.OffsetProperty, sweep);
                }
                return brush;
            }
            case "comet":
            {
                var brush = new RadialGradientBrush { RadiusX = 0.38, RadiusY = 0.38, Center = new Point(0, 0), GradientOrigin = new Point(0, 0) };
                brush.GradientStops.Add(new GradientStop(Mix(c[0], System.Windows.Media.Colors.White, 0.75), 0));
                brush.GradientStops.Add(new GradientStop(c[0], 0.35));
                for (int i = 1; i < c.Length; i++) brush.GradientStops.Add(new GradientStop(c[i], 0.35 + 0.65 * i / (c.Length - 1)));
                if (!Moving) { brush.Center = brush.GradientOrigin = new Point(0.15, 0.15); return Freeze(brush); }
                var path = new PointAnimationUsingKeyFrames { Duration = Seconds(2.4), RepeatBehavior = forever };
                var corners = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1), new Point(0, 0) };
                for (int i = 0; i < corners.Length; i++)
                    path.KeyFrames.Add(new LinearPointKeyFrame(corners[i], KeyTime.FromPercent(i / 4.0)));
                brush.BeginAnimation(RadialGradientBrush.CenterProperty, path);
                brush.BeginAnimation(RadialGradientBrush.GradientOriginProperty, path.Clone());
                return brush;
            }
            case "cycle":
            {
                var brush = new SolidColorBrush(c[0]);
                if (!Moving) return Freeze(brush);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, CycleAnimation(c));
                return brush;
            }
            case "breathe":
            {
                var brush = Linear(c, new Point(0, 0), new Point(1, 1));
                if (!Moving) return Freeze(brush);
                brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(1, 0.3, Seconds(1.4))
                { AutoReverse = true, RepeatBehavior = forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                return brush;
            }
            case "heartbeat":
                return Beats(Linear(c, new Point(0, 0), new Point(1, 1)), 1.3, false, (0, 0.4), (0.08, 1), (0.18, 0.5), (0.28, 1), (0.5, 0.4));
            case "flicker":
                return Beats(Linear(c, new Point(0, 0), new Point(1, 1)), 4.2, true, (0, 1), (1.6, 1), (1.64, 0.15), (1.7, 1), (1.76, 0.35), (1.84, 1),
                    (3.1, 1), (3.14, 0.5), (3.2, 1));
            default:
                return Freeze(Linear(c, new Point(0, 0), new Point(1, 1)));
        }
    }

    private static ColorAnimationUsingKeyFrames CycleAnimation(Color[] c)
    {
        var anim = new ColorAnimationUsingKeyFrames { Duration = Seconds(1.6 * c.Length), RepeatBehavior = RepeatBehavior.Forever };
        for (int i = 0; i <= c.Length; i++)
            anim.KeyFrames.Add(new LinearColorKeyFrame(c[i % c.Length], KeyTime.FromPercent(i / (double)c.Length)));
        return anim;
    }

    /// <summary>The brush's opacity stepped through (seconds, opacity) points, looping.</summary>
    private static Brush Beats(Brush brush, double length, bool discrete, params (double At, double Opacity)[] points)
    {
        if (!Moving) return Freeze(brush);
        var anim = new DoubleAnimationUsingKeyFrames { Duration = Seconds(length), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (at, opacity) in points)
            anim.KeyFrames.Add(discrete ? new DiscreteDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(Seconds(at)))
                                        : new EasingDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(Seconds(at))) { EasingFunction = new SineEase() });
        brush.BeginAnimation(Brush.OpacityProperty, anim);
        return brush;
    }

    private static LinearGradientBrush Linear(Color[] c, Point start, Point end)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        for (int i = 0; i < c.Length; i++) brush.GradientStops.Add(new GradientStop(c[i], c.Length == 1 ? 0 : i / (double)(c.Length - 1)));
        return brush;
    }

    private static T Freeze<T>(T f) where T : Freezable { if (f.CanFreeze) f.Freeze(); return f; }

    /// <summary>
    /// The ring's glow when the effect changes it: null to leave the style's own (or none), otherwise the effect to use
    /// (which may be null for "Off").
    /// </summary>
    public static (bool Replace, Effect? Effect) GlowFor(Color accent)
    {
        if (!Active || Glow == "style") return (false, null);
        if (Glow == "off" || Perf.Active) return (true, null);
        var c = ColorsFor(accent);
        var glow = Glow == "strong"
            ? new DropShadowEffect { Color = c[0], BlurRadius = 30, ShadowDepth = 0, Opacity = 1 }
            : new DropShadowEffect { Color = c[0], BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 };
        if (Effect == "cycle" && Moving) glow.BeginAnimation(DropShadowEffect.ColorProperty, CycleAnimation(c));
        else glow.Freeze();
        return (true, glow);
    }
}
