using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Recomp Zone's opening, shown while the list, the covers and the page load: a console cartridge on the left
/// streams its machine code (hex bytes and MIPS / PowerPC instructions) into the Recomp emblem in the middle, which
/// sends rebuilt C code out to a PC window on the right, where the tiles of a native build light up one by one as
/// loading really progresses. Under it, what's happening and a progress bar. When everything is in, the build
/// completes with a flash and the whole thing fades away to the page.
/// </summary>
public sealed class RecompIntro : Grid
{
    private const double W = 1200, H = 620;
    private const int Cols = 6, Rows = 4;
    private static readonly Point Core = new(600, 300);

    private static readonly string[] MachineCode =
    {
        "3C 08 80 1F", "27 BD FF E8", "AF BF 00 14", "8C 84 4F 10", "0C 00 93 2A", "03 E0 00 08", "24 02 00 01",
        "lui t0, 0x801F", "addiu sp, sp, -0x18", "sw ra, 0x14(sp)", "jal func_8024A8", "lw a0, 0x4F10(a0)", "jr ra",
        "mflr r0", "stwu r1, -0x20(r1)", "bl 0x80123ABC", "lfs f1, 0x8(r3)", "blr", "nop", "beq v0, zero, .L1",
        "94 21 FF E0", "7C 08 02 A6", "4E 80 00 20", "lhu t6, 0x2(a1)", "ori at, at, 0x8000", "DE AD BE EF",
    };

    private static readonly string[] CCode =
    {
        "void func_8024A8(void)", "gSaveContext.health = 0x30;", "if (input->press.button & BTN_A)", "return true;",
        "for (i = 0; i < 16; i++)", "Actor_Update(actor, play);", "SDL_GL_SwapWindow(win);", "render_frame(&gfx);",
        "x86_64", "static int ctx_r4;", "memcpy(dst, src, size);", "audio_push(samples, n);", "uint32_t pc = 0x80000400;",
        "#include \"recomp.h\"", "vec3f pos = {0};", "while (running)", "} // 60 fps", "widescreen = true;",
    };

    private sealed class Particle
    {
        public required TextBlock Text;
        public Point From, Ctrl, To;
        public double Born, Life;
        public bool Outgoing;
    }

    private readonly Canvas _stage = new() { Width = W, Height = H };
    private readonly List<Particle> _particles = new();
    private readonly Border[] _cells = new Border[Cols * Rows];
    private readonly bool[] _lit = new bool[Cols * Rows];
    private readonly TextBlock _phase, _detail, _percent, _skip;
    private readonly Border _barFill;
    private readonly RotateTransform _ringTurn = new(0), _ringTurn2 = new(0);
    private readonly ScaleTransform _coreScale = new(1, 1);
    private readonly DropShadowEffect? _glow;
    private readonly Border _cartridge;
    private readonly Rectangle _flash;
    private readonly Random _rand = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _calm;
    private double _target, _shown, _last, _nextIn, _nextOut;
    private bool _running;

    /// <summary>Vibration for the build: a tick as each tile lights, a thump when a stage starts, a rumble at the end.</summary>
    public Action<int>? Rumble { get; set; }
    private string _lastPhase = "";

    public RecompIntro()
    {
        _calm = Theme.ReduceMotion;
        Focusable = true;

        // Backdrop: deep blue-black with a faint blueprint grid and a glow behind the core.
        Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x07, 0x0A, 0x11));
        var grid = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0x3E, 0xF0, 0xE6)), 1), new RectangleGeometry(new Rect(0, 0, 44, 44)));
        Children.Add(new Rectangle { Fill = new DrawingBrush(grid) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 44, 44), ViewportUnits = BrushMappingMode.Absolute }, IsHitTestVisible = false });
        Children.Add(new Rectangle
        {
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0x1F, 0x8F, 0xC9), Color.FromArgb(0, 0, 0, 0)) { RadiusX = 0.45, RadiusY = 0.6, Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45) }
        });
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _stage, Margin = new Thickness(30, 10, 30, 10) });

        // ---- title
        var title = Text("RECOMP ZONE", 30, Colors.White, bold: true);
        title.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
        Center(title, 34);
        var sub = Text("decompiling  ·  recompiling  ·  linking", 15, Color.FromRgb(0x6F, 0xD8, 0xE6));
        sub.FontFamily = new FontFamily("Consolas, Cascadia Mono");
        Center(sub, 78);

        // ---- the original: a cartridge, its label and its pins
        _stage.Children.Add(Place(Caption("ORIGINAL CODE"), 132, 176));
        var label = new Border
        {
            Margin = new Thickness(22, 20, 22, 0), Height = 92, CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(Color.FromRgb(0xD9, 0xE2, 0xF2), Color.FromRgb(0xA9, 0xB6, 0xD0), 90),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Centered(Text("ROM", 30, Color.FromRgb(0x1C, 0x22, 0x35), bold: true)),
                    Centered(Text("MIPS · PowerPC · SH-4", 11, Color.FromRgb(0x3A, 0x44, 0x60))),
                }
            }
        };
        var pins = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < 11; i++) pins.Children.Add(new Border { Width = 8, Height = 18, Margin = new Thickness(2.5, 0, 2.5, 0), CornerRadius = new CornerRadius(1.5), Background = new LinearGradientBrush(Color.FromRgb(0xF2, 0xC9, 0x5C), Color.FromRgb(0xB8, 0x8A, 0x2A), 90) });
        var shell = new Grid();
        shell.Children.Add(label);
        shell.Children.Add(pins);
        _cartridge = new Border
        {
            Width = 210, Height = 200, CornerRadius = new CornerRadius(16, 16, 6, 6), Child = shell,
            Background = new LinearGradientBrush(Color.FromRgb(0x44, 0x4A, 0x66), Color.FromRgb(0x22, 0x26, 0x3A), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1.5),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };
        _stage.Children.Add(Place(_cartridge, 115, 200));

        // ---- the core: the Recomp emblem inside two turning rings
        var ring = new Ellipse
        {
            Width = 236, Height = 236, StrokeThickness = 4, StrokeDashArray = new DoubleCollection { 6, 4 }, StrokeDashCap = PenLineCap.Round,
            Stroke = new LinearGradientBrush(Color.FromRgb(0x3E, 0xF0, 0xE6), Color.FromRgb(0x3A, 0x7B, 0xD5), 45),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _ringTurn
        };
        _stage.Children.Add(Place(ring, Core.X - 118, Core.Y - 118));
        var ring2 = new Ellipse
        {
            Width = 196, Height = 196, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 18, 10 }, Opacity = 0.55,
            Stroke = new SolidColorBrush(Color.FromRgb(0x3E, 0xF0, 0xE6)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _ringTurn2
        };
        _stage.Children.Add(Place(ring2, Core.X - 98, Core.Y - 98));
        var emblem = new Border
        {
            Width = 150, Height = 150, CornerRadius = new CornerRadius(30), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x16)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _coreScale
        };
        string logo = System.IO.Path.Combine(AppContext.BaseDirectory, "RecompZone", "logo.png");
        if (File.Exists(logo) && ImageLoader.FromFile(logo, 300) is { } picture)
            emblem.Child = new Image { Source = picture, Stretch = Stretch.UniformToFill };
        else
            emblem.Child = new TextBlock { Text = "R", FontSize = 96, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new LinearGradientBrush(Color.FromRgb(0x3E, 0xF0, 0xE6), Color.FromRgb(0x3A, 0x7B, 0xD5), 90) };
        if (!Perf.Active)
        {
            _glow = new DropShadowEffect { Color = Color.FromRgb(0x3E, 0xD8, 0xE6), BlurRadius = 36, ShadowDepth = 0, Opacity = 0.75 };
            emblem.Effect = _glow;
        }
        _stage.Children.Add(Place(emblem, Core.X - 75, Core.Y - 75));

        // ---- the result: a PC window whose build tiles light up with the progress
        _stage.Children.Add(Place(Caption("NATIVE PC BUILD"), 870, 176));
        var window = new Border
        {
            Width = 244, Height = 200, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x13, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x3E, 0xF0, 0xE6)), BorderThickness = new Thickness(1.5)
        };
        var inside = new StackPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 0, 8) };
        foreach (var c in new[] { Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFF, 0xBD, 0x2E), Color.FromRgb(0x28, 0xC9, 0x40) })
            bar.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0) });
        var exe = Text("game.exe", 11, Color.FromRgb(0x7F, 0x93, 0xB0));
        exe.FontFamily = new FontFamily("Consolas, Cascadia Mono");
        exe.Margin = new Thickness(8, -1, 0, 0);
        bar.Children.Add(exe);
        inside.Children.Add(bar);
        var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = Cols, Rows = Rows, Width = 216, Height = 144, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new Border
            {
                Margin = new Thickness(3), CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x22, 0x33)),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            cells.Children.Add(_cells[i]);
        }
        inside.Children.Add(cells);
        window.Child = inside;
        _stage.Children.Add(Place(window, 858, 200));

        // ---- what's happening, and how far along
        _phase = Text("Starting up", 24, Colors.White, bold: true);
        _phase.Width = W;
        _phase.TextAlignment = TextAlignment.Center;
        _stage.Children.Add(Place(_phase, 0, 446));
        _detail = Text("", 15, Color.FromRgb(0x8F, 0xA3, 0xBF));
        _detail.FontFamily = new FontFamily("Consolas, Cascadia Mono");
        _detail.Width = W;
        _detail.TextAlignment = TextAlignment.Center;
        _stage.Children.Add(Place(_detail, 0, 484));
        var track = new Border { Width = 560, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x26, 0x38)) };
        _stage.Children.Add(Place(track, 320, 524));
        _barFill = new Border
        {
            Width = 0, Height = 8, CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush(Color.FromRgb(0x3E, 0xF0, 0xE6), Color.FromRgb(0x3A, 0x7B, 0xD5), 0)
        };
        if (!Perf.Active) _barFill.Effect = new DropShadowEffect { Color = Color.FromRgb(0x3E, 0xF0, 0xE6), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 };
        _stage.Children.Add(Place(_barFill, 320, 524));
        _percent = Text("0%", 14, Color.FromRgb(0x6F, 0xD8, 0xE6), bold: true);
        _percent.FontFamily = new FontFamily("Consolas, Cascadia Mono");
        _stage.Children.Add(Place(_percent, 894, 517));
        _skip = Text(SkipText(), 13, Color.FromRgb(0x6B, 0x7C, 0x96));
        _skip.Cursor = System.Windows.Input.Cursors.Hand;
        _skip.MouseLeftButtonUp += (_, _) => SkipRequested?.Invoke();
        Action relabel = () => Dispatcher.BeginInvoke(new Action(() => _skip.Text = SkipText()));
        Loaded += (_, _) => { Hints.Changed -= relabel; Hints.Changed += relabel; };
        Unloaded += (_, _) => Hints.Changed -= relabel;
        _skip.Width = W;
        _skip.TextAlignment = TextAlignment.Center;
        _skip.Visibility = Visibility.Hidden;
        _stage.Children.Add(Place(_skip, 0, 560));

        _flash = new Rectangle { Fill = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        Children.Add(_flash);

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    // ---------------------------------------------------------------- what the page tells it

    /// <summary>Shows a stage of the loading: its name, a detail line, and how far along everything is (0–1).</summary>
    public void Report(string phase, string detail, double progress)
    {
        if (phase != _lastPhase) { _lastPhase = phase; Rumble?.Invoke(34); }
        _phase.Text = phase;
        _detail.Text = detail;
        _target = Math.Max(_target, Math.Clamp(progress, 0, 1));
    }

    /// <summary>The skip line, with the key or button in use (Enter, or A on the controller), or a click.</summary>
    private static string SkipText() => Hints.KeyLabel("Select") + "  Skip ahead (the rest of the covers load as you scroll)" + (Hints.Keyboard ? "  \u00B7  or click here" : "");

    /// <summary>The player asked to skip (clicked the line).</summary>
    public Action? SkipRequested { get; set; }

    public void SetSkippable(bool on) => _skip.Visibility = on ? Visibility.Visible : Visibility.Hidden;

    /// <summary>Completes the build (every tile lit, a flash), then fades out. The caller removes it afterwards.</summary>
    public async Task FinishAsync()
    {
        _target = 1;
        SetSkippable(false);
        _phase.Text = "Build complete";
        _detail.Text = "Have fun";
        if (_calm) { _shown = 1; Draw(0); return; }
        for (int i = 0; i < 30 && _shown < 0.995; i++) await Task.Delay(20);
        _shown = 1;
        Draw(0);
        Rumble?.Invoke(140);
        _coreScale.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse());
        _coreScale.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse());
        _flash.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)), new EasingDoubleKeyFrame(0.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))), new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))) }
        });
        await Task.Delay(520);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        await Task.Delay(370);
        Stop();
    }

    private static DoubleAnimationUsingKeyFrames Pulse() => new()
    {
        KeyFrames =
        {
            new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new CubicEase { EasingMode = EasingMode.EaseOut }),
            new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), new ElasticEase { Oscillations = 1, Springiness = 4 })
        }
    };

    // ---------------------------------------------------------------- the animation

    private void Start()
    {
        if (_running) return;
        _running = true;
        _last = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        Draw(dt);
    }

    private void Draw(double dt)
    {
        double now = _clock.Elapsed.TotalSeconds;
        _shown += (_target - _shown) * Math.Min(1, dt * 5);
        if (_target - _shown < 0.002) _shown = _target;
        _barFill.Width = 560 * _shown;
        _percent.Text = $"{(int)Math.Round(_shown * 100)}%";

        // Build tiles light up with the progress, each with a little pop.
        int lit = (int)Math.Floor(_shown * _cells.Length + 0.0001);
        for (int i = 0; i < _cells.Length; i++)
        {
            bool on = i < lit;
            if (on == _lit[i]) continue;
            _lit[i] = on;
            _cells[i].Background = on
                ? new LinearGradientBrush(Color.FromRgb(0x3E, 0xF0, 0xE6), Color.FromRgb(0x3A, 0x7B, 0xD5), 45)
                : new SolidColorBrush(Color.FromRgb(0x18, 0x22, 0x33));
            if (on) Rumble?.Invoke(12);
            if (on && !_calm && _cells[i].RenderTransform is ScaleTransform pop)
            {
                var a = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }
        if (_calm || dt == 0) return;

        // The rings turn, the core breathes, the cartridge hums.
        _ringTurn.Angle = (_ringTurn.Angle + dt * 50) % 360;
        _ringTurn2.Angle = (_ringTurn2.Angle - dt * 80) % 360;
        if (_glow != null) _glow.BlurRadius = 30 + 14 * Math.Sin(now * 3.2);
        if (_cartridge.RenderTransform is ScaleTransform hum) hum.ScaleX = hum.ScaleY = 1 + 0.012 * Math.Sin(now * 9);

        // Machine code streams in from the cartridge; rebuilt code streams out to the PC.
        if (now >= _nextIn) { Spawn(false, now); _nextIn = now + 0.06 + _rand.NextDouble() * 0.05; }
        if (now >= _nextOut) { Spawn(true, now); _nextOut = now + 0.09 + _rand.NextDouble() * 0.07; }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            double t = (now - p.Born) / p.Life;
            if (t >= 1) { _stage.Children.Remove(p.Text); _particles.RemoveAt(i); continue; }
            double e = t * t * (3 - 2 * t); // smooth start and end
            double u = 1 - e;
            double x = u * u * p.From.X + 2 * u * e * p.Ctrl.X + e * e * p.To.X;
            double y = u * u * p.From.Y + 2 * u * e * p.Ctrl.Y + e * e * p.To.Y;
            Canvas.SetLeft(p.Text, x);
            Canvas.SetTop(p.Text, y - 8);
            double fade = t < 0.15 ? t / 0.15 : t > 0.75 ? (1 - t) / 0.25 : 1;
            p.Text.Opacity = fade * 0.95;
            if (p.Text.RenderTransform is ScaleTransform s)
                s.ScaleX = s.ScaleY = p.Outgoing ? 0.7 + 0.4 * (1 - t) : 1.05 - 0.6 * t; // squeezed into the core, unpacked out of it
        }
    }

    private void Spawn(bool outgoing, double now)
    {
        if (_particles.Count > 70) return;
        var words = outgoing ? CCode : MachineCode;
        var text = new TextBlock
        {
            Text = words[_rand.Next(words.Length)], FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"), FontSize = outgoing ? 13 : 14,
            Foreground = new SolidColorBrush(outgoing ? Color.FromRgb(0xA9, 0xC4, 0xFF) : Color.FromRgb(0x7C, 0xF3, 0xEA)),
            RenderTransform = new ScaleTransform(1, 1), IsHitTestVisible = false, Opacity = 0
        };
        Point from, to, ctrl;
        if (!outgoing)
        {
            from = new Point(330 + _rand.NextDouble() * 20, 215 + _rand.NextDouble() * 170);
            to = new Point(Core.X - 40, Core.Y + (_rand.NextDouble() - 0.5) * 20);
            ctrl = new Point(450, from.Y + (_rand.NextDouble() - 0.5) * 160);
        }
        else
        {
            from = new Point(Core.X + 30, Core.Y + (_rand.NextDouble() - 0.5) * 40);
            int cell = _rand.Next(_cells.Length);
            to = new Point(870 + 14 + (cell % Cols) * 36, 238 + (cell / Cols) * 36 + 10);
            ctrl = new Point(760, 200 + _rand.NextDouble() * 220);
        }
        var p = new Particle { Text = text, From = from, To = to, Ctrl = ctrl, Born = now, Life = 0.9 + _rand.NextDouble() * 0.5, Outgoing = outgoing };
        Canvas.SetLeft(text, from.X);
        Canvas.SetTop(text, from.Y);
        _stage.Children.Add(text);
        _particles.Add(p);
    }

    // ---------------------------------------------------------------- small helpers

    private static TextBlock Text(string text, double size, Color color, bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };

    private static TextBlock Caption(string text)
    {
        var t = Text(text, 12, Color.FromRgb(0x6B, 0x7C, 0x96), bold: true);
        t.FontFamily = new FontFamily("Consolas, Cascadia Mono");
        return t;
    }

    private static TextBlock Centered(TextBlock t)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    private static UIElement Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    private void Center(TextBlock t, double y)
    {
        t.Width = W;
        t.TextAlignment = TextAlignment.Center;
        _stage.Children.Add(Place(t, 0, y));
    }
}
