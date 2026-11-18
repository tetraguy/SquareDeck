using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Performance Boost: frees memory before a game starts. Every other program's working set is trimmed (Windows
/// pages out what they aren't using; they get it back when they need it), the launcher trims itself, and, when
/// SquareDeck runs as administrator, Windows' standby list is emptied too. Returns the memory that came free.
/// </summary>
public static class Booster
{
    private static readonly string[] Skip = { "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "services", "lsass", "audiodg" };

    public static ulong Run()
    {
        ulong before = Hardware.MemoryStatus()?.Available ?? 0;
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me || p.Id <= 4 || Skip.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                IntPtr handle = OpenProcess(ProcessSetQuota | ProcessQueryInformation, false, p.Id);
                if (handle == IntPtr.Zero) continue;
                try { EmptyWorkingSet(handle); }
                finally { CloseHandle(handle); }
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        PurgeStandbyList();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        ProcessUtil.TrimWorkingSet();
        ulong after = Hardware.MemoryStatus()?.Available ?? 0;
        return after > before ? after - before : 0;
    }

    /// <summary>Empties the standby list (cached files Windows keeps around). Only works as administrator; ignored otherwise.</summary>
    private static void PurgeStandbyList()
    {
        try
        {
            if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return;
            int command = 4; // MemoryPurgeStandbyList
            NtSetSystemInformation(80 /* SystemMemoryListInformation */, ref command, sizeof(int));
        }
        catch (Exception) { }
    }

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out var token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid)) return false;
            var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            return AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(token); }
    }

    private const int ProcessSetQuota = 0x0100, ProcessQueryInformation = 0x0400;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int kind, ref int info, int length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returned);
}

/// <summary>
/// The Performance Boost animation, shown while the boost runs, just before a game starts. Five looks, each with its
/// own vibration pattern timed to it:
///  - rings: rings pulse out from the game's cover; a tap on the controller with each ring;
///  - warp: stars streak past faster and faster; the vibration speeds up with them, ending in a thump;
///  - gauge: a turbo gauge sweeps to 100%; a tick for every few marks, harder near the top;
///  - orbit: sparks circle the cover and spiral in; a heartbeat (two beats) while they turn;
///  - shockwave: the cover charges up and shakes, then a flash and a shock ring; a rising buzz, then a jolt.
/// </summary>
public sealed class BoostScreen : Screen
{
    public static readonly (string Id, string Name, string About)[] Styles =
    {
        ("rings", "Pulse rings", "Rings pulse out from the cover, a tap with each ring"),
        ("warp", "Warp speed", "Stars streak past faster and faster, then a thump"),
        ("gauge", "Turbo gauge", "A gauge sweeps to 100%, ticking as it climbs"),
        ("orbit", "Orbit", "Sparks circle in, with a heartbeat"),
        ("shockwave", "Shockwave", "Charges up and shakes, then a blast"),
        ("vortex", "Vortex", "A spiral of light winds tighter and tighter, then lets go"),
        ("grid", "Power grid", "A floor of squares lights up in waves under the cover"),
        ("lightning", "Lightning", "Bolts strike the cover, each with a sharp jolt"),
        ("equalizer", "Equalizer", "Bars bounce to a beat around the cover, louder each bar"),
        ("hyperloop", "Hyperloop", "Rush down a tunnel of light rings to the finish"),
    };

    private const double Duration = 2.8; // seconds of animation (the boost itself runs alongside)

    private readonly Game? _game;
    private readonly string _style;
    private readonly bool _rumble, _preview;
    private readonly TaskCompletionSource<bool> _done = new();
    private readonly Grid _root = new();
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Border _cover;
    private readonly ScaleTransform _coverScale = new(1, 1);
    private readonly TranslateTransform _coverShake = new();
    private readonly TextBlock _title = Ui.Text("PERFORMANCE BOOST", 15, "AccentBrush", bold: true);
    private readonly TextBlock _status = Ui.Text("Freeing memory…", 26, "FgBrush", bold: true);
    private readonly Ellipse _glow = new() { IsHitTestVisible = false };
    private readonly Border _flash = new() { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
    private readonly Random _random = new();
    private readonly List<(int At, int Ms, double Power)> _pattern = new();
    private int _nextPulse;
    private DateTime _start;
    private Color _accent;
    private ulong? _freed;
    private bool _finishing;

    // Per-style pieces
    private readonly List<Ellipse> _rings = new();
    private readonly List<(Line Line, double Angle, double Distance)> _streaks = new();
    private readonly List<Ellipse> _sparks = new();
    private Path? _arc;
    private TextBlock? _gaugeNumber;
    private readonly List<Line> _ticks = new();
    private readonly List<(Ellipse Dot, double Angle, double Distance)> _motes = new();
    private readonly List<Shape> _pieces = new();
    private readonly List<double> _seeds = new();
    private double _lastBolt = -1;

    public override bool IsDialog => false;
    public Task<bool> Result => _done.Task;

    /// <param name="style">One of <see cref="Styles"/>, or "random".</param>
    /// <param name="preview">Just the animation (from Settings): nothing is freed.</param>
    public BoostScreen(Game? game, string style, bool rumble, bool preview = false)
    {
        _game = game;
        _style = style == "random" || Styles.All(s => s.Id != style) ? Styles[Random.Shared.Next(Styles.Length)].Id : style;
        _rumble = rumble;
        _preview = preview;

        _root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        _root.Children.Add(_canvas);
        _root.Children.Add(_flash);

        // The game's cover in the middle (or SquareDeck's mark for a preview).
        double size = _style == "gauge" ? 150 : 190;
        _cover = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner + 4, 6)), RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 }
        };
        var group = new TransformGroup();
        group.Children.Add(_coverScale);
        group.Children.Add(_coverShake);
        _cover.RenderTransform = group;
        if (game?.Cover != null) _cover.Background = new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill };
        else
        {
            _cover.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _cover.Child = Ui.Glyph("", 72, "BgBrush");
        }
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90) };
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Margin = new Thickness(0, 6, 0, 0);
        words.Children.Add(_title);
        words.Children.Add(_status);
        if (game != null)
        {
            var name = Ui.Text(game.Name, 16, "SubtleBrush");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 4, 0, 0);
            words.Children.Add(name);
        }
        _root.Children.Add(words);
        Content = _root;
    }

    public override void FocusDefault() { }

    public override async void OnShown()
    {
        if (_start != default) return;
        _accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        Build();
        _start = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
        if (!_preview)
        {
            try { _freed = await Task.Run(Booster.Run); }
            catch (Exception) { _freed = 0; }
        }
        else _freed = 0;
    }

    public override void OnClosed()
    {
        CompositionTarget.Rendering -= OnFrame;
        _done.TrySetResult(true);
    }

    // Nothing to press: it goes on its own in a few seconds. B skips it.
    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back && !_finishing) Finish();
        return true;
    }

    private double W => Math.Max(_canvas.ActualWidth, 800);
    private double H => Math.Max(_canvas.ActualHeight, 600);
    private Point Center => new(W / 2, H / 2 - 50);

    private Color A(byte alpha) => Color.FromArgb(alpha, _accent.R, _accent.G, _accent.B);

    private T Add<T>(T element) where T : UIElement
    {
        _canvas.Children.Add(element);
        return element;
    }

    /// <summary>Lays out the style's pieces and its vibration pattern (times in ms from the start).</summary>
    private void Build()
    {
        int count(int full) => Perf.Active ? full / 2 : full;
        // A soft glow of the highlight color behind everything.
        _glow.Width = _glow.Height = 900;
        _glow.Fill = new RadialGradientBrush(A(0x55), A(0x00));
        Add(_glow);

        switch (_style)
        {
            case "rings":
                for (int i = 0; i < 5; i++) _rings.Add(Add(new Ellipse { StrokeThickness = 6, Stroke = new SolidColorBrush(_accent), Opacity = 0 }));
                for (int i = 0; i < 5; i++) _pattern.Add((i * 480, 70, 0.55 + i * 0.08));
                _pattern.Add((2450, 180, 1.0));
                break;
            case "warp":
                for (int i = 0; i < count(150); i++)
                {
                    var line = Add(new Line { Stroke = new SolidColorBrush(i % 4 == 0 ? A(0xFF) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    _streaks.Add((line, _random.NextDouble() * Math.PI * 2, _random.NextDouble()));
                }
                for (int t = 0, i = 0; t < 2300; i++)
                {
                    double f = t / 2300.0;
                    _pattern.Add((t, 40, 0.25 + 0.55 * f));
                    t += (int)(230 - 170 * f);
                }
                _pattern.Add((2400, 220, 1.0));
                break;
            case "gauge":
                var track = Add(new Path { Stroke = new SolidColorBrush(A(0x30)), StrokeThickness = 16, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                track.Data = ArcGeometry(1);
                _arc = Add(new Path { Stroke = new SolidColorBrush(_accent), StrokeThickness = 16, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                for (int i = 0; i <= 30; i++)
                    _ticks.Add(Add(new Line { StrokeThickness = 3, Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), StrokeEndLineCap = PenLineCap.Round }));
                _gaugeNumber = Add(Ui.Text("0%", 34, "FgBrush", bold: true));
                for (int i = 1; i <= 10; i++) _pattern.Add(((int)(2400 * Ease(i / 10.0, inverse: true)), 35, 0.3 + 0.06 * i));
                _pattern.Add((2450, 160, 1.0));
                break;
            case "orbit":
                for (int i = 0; i < count(28); i++)
                    _sparks.Add(Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : _accent) }));
                for (int beat = 0; beat < 4; beat++)
                {
                    _pattern.Add((beat * 620, 60, 0.6));
                    _pattern.Add((beat * 620 + 180, 60, 0.45));
                }
                _pattern.Add((2450, 220, 1.0));
                break;
            case "vortex":
                for (int i = 0; i < count(90); i++)
                {
                    _pieces.Add(Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(i % 4 == 0 ? Colors.White : _accent) }));
                    _seeds.Add(_random.NextDouble());
                }
                for (int t = 0, gap = 300; t < 2300; t += gap, gap = Math.Max(70, gap - 30)) _pattern.Add((t, 45, 0.25 + 0.6 * t / 2300.0));
                _pattern.Add((2420, 240, 1.0));
                break;
            case "grid":
                for (int i = 0; i < 16 * 9; i++)
                {
                    _pieces.Add(Add(new Rectangle { RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(_accent), Opacity = 0 }));
                    _seeds.Add(_random.NextDouble());
                }
                for (int w = 0; w < 4; w++) _pattern.Add((w * 600, 90, 0.4 + 0.12 * w));
                _pattern.Add((2420, 200, 1.0));
                break;
            case "lightning":
                for (int i = 0; i < 3; i++)
                    _pieces.Add(Add(new Polyline { Stroke = Brushes.White, StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, Opacity = 0,
                        Effect = Perf.Active ? null : new DropShadowEffect { Color = _accent, BlurRadius = 24, ShadowDepth = 0, Opacity = 1 } }));
                foreach (int at in new[] { 300, 900, 1350, 1750, 2050, 2250 }) _pattern.Add((at, 70, 0.7));
                _pattern.Add((2420, 260, 1.0));
                break;
            case "equalizer":
                for (int i = 0; i < 32; i++)
                {
                    _pieces.Add(Add(new Rectangle { Width = 14, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(i % 8 == 0 ? Colors.White : _accent) }));
                    _seeds.Add(_random.NextDouble() * Math.PI * 2);
                }
                for (int b = 0; b < 8; b++) _pattern.Add((b * 300, 50, 0.3 + 0.07 * b));
                _pattern.Add((2420, 220, 1.0));
                break;
            case "hyperloop":
                for (int i = 0; i < 14; i++)
                {
                    _pieces.Add(Add(new Rectangle { StrokeThickness = 4, Stroke = new SolidColorBrush(i % 3 == 0 ? Colors.White : _accent), RadiusX = 30, RadiusY = 30 }));
                    _seeds.Add(i / 14.0);
                }
                for (int t = 0, gap = 260; t < 2300; t += gap, gap = Math.Max(60, gap - 25)) _pattern.Add((t, 35, 0.2 + 0.5 * t / 2300.0));
                _pattern.Add((2420, 280, 1.0));
                break;
            default: // shockwave
                for (int i = 0; i < count(40); i++)
                {
                    var dot = Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(i % 2 == 0 ? Colors.White : _accent) });
                    _motes.Add((dot, _random.NextDouble() * Math.PI * 2, 0.6 + _random.NextDouble() * 0.5));
                }
                _rings.Add(Add(new Ellipse { StrokeThickness = 14, Stroke = Brushes.White, Opacity = 0 }));
                for (int t = 0; t < 1850; t += 110) _pattern.Add((t, 100, 0.2 + 0.6 * t / 1850.0));
                _pattern.Add((1900, 260, 1.0));
                break;
        }
        _canvas.Children.Add(_cover);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        var c = Center;
        Canvas.SetLeft(_glow, c.X - _glow.Width / 2);
        Canvas.SetTop(_glow, c.Y - _glow.Height / 2);
        Canvas.SetLeft(_cover, c.X - _cover.Width / 2);
        Canvas.SetTop(_cover, c.Y - _cover.Height / 2);
        _glow.Opacity = 0.6 + 0.4 * Math.Sin(t * 4);

        // The vibration, on cue.
        while (_nextPulse < _pattern.Count && t * 1000 >= _pattern[_nextPulse].At)
        {
            var (_, ms, power) = _pattern[_nextPulse++];
            if (_rumble) Host.RumbleScaled(ms, power);
        }

        switch (_style)
        {
            case "rings": Rings(t, c); break;
            case "warp": Warp(t, c); break;
            case "gauge": Gauge(t, c); break;
            case "orbit": Orbit(t, c); break;
            case "vortex": Vortex(t, c); break;
            case "grid": PowerGrid(t, c); break;
            case "lightning": Lightning(t, c); break;
            case "equalizer": Equalizer(t, c); break;
            case "hyperloop": Hyperloop(t, c); break;
            default: Shockwave(t, c); break;
        }

        // Done when the animation has played and the boost has finished.
        if (!_finishing && t >= Duration && _freed != null) Finish();
    }

    private async void Finish()
    {
        _finishing = true;
        CompositionTarget.Rendering -= OnFrame;
        _status.Text = _preview ? "Boosted" : _freed is ulong f && f >= 1 << 20 ? $"Freed {Size(f)}  ·  ready to play" : "Ready to play";
        _coverScale.ScaleX = _coverScale.ScaleY = 1;
        _coverShake.X = _coverShake.Y = 0;
        await Task.Delay(_preview ? 900 : 650);
        Host.Pop(this);
    }

    private static string Size(ulong bytes) => bytes >= 1UL << 30 ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0} MB";

    private static double Ease(double x, bool inverse = false)
    {
        x = Math.Clamp(x, 0, 1);
        return inverse ? 1 - Math.Sqrt(1 - x) : 1 - Math.Pow(1 - x, 3);
    }

    // ---------------------------------------------------------------- the looks

    private void Rings(double t, Point c)
    {
        for (int i = 0; i < _rings.Count; i++)
        {
            double age = t - i * 0.48;
            var ring = _rings[i];
            if (age < 0 || age > 1.3) { ring.Opacity = 0; continue; }
            double r = 100 + 520 * Ease(age / 1.3);
            ring.Width = ring.Height = r * 2;
            ring.StrokeThickness = 8 * (1 - age / 1.3) + 1;
            ring.Opacity = 1 - age / 1.3;
            Canvas.SetLeft(ring, c.X - r);
            Canvas.SetTop(ring, c.Y - r);
        }
        double since = (t % 0.48) / 0.48;
        double pop = t < 2.4 ? 1 + 0.08 * (1 - since) : 1 + 0.12 * Math.Max(0, 1 - (t - 2.4) * 4);
        _coverScale.ScaleX = _coverScale.ScaleY = pop;
    }

    private void Warp(double t, Point c)
    {
        double speed = 0.15 + 2.6 * Math.Pow(Math.Min(t / 2.4, 1), 2);
        double max = Math.Sqrt(W * W + H * H) / 2;
        for (int i = 0; i < _streaks.Count; i++)
        {
            var (line, angle, d) = _streaks[i];
            d += speed * 0.016 * (0.4 + d);
            if (d > 1.05) { d = 0.02 + _random.NextDouble() * 0.1; angle = _random.NextDouble() * Math.PI * 2; }
            _streaks[i] = (line, angle, d);
            double r2 = 60 + d * max, r1 = Math.Max(60, r2 - (8 + speed * 120 * d));
            line.X1 = c.X + Math.Cos(angle) * r1; line.Y1 = c.Y + Math.Sin(angle) * r1;
            line.X2 = c.X + Math.Cos(angle) * r2; line.Y2 = c.Y + Math.Sin(angle) * r2;
            line.Opacity = Math.Min(1, d * 1.6);
            line.StrokeThickness = 1 + d * 2.5;
        }
        _coverScale.ScaleX = _coverScale.ScaleY = 1 + 0.04 * Math.Sin(t * 30) * Math.Min(t / 2.4, 1);
        _flash.Opacity = t > 2.4 ? Math.Max(0, 0.55 - (t - 2.4) * 1.6) : 0;
    }

    private Geometry ArcGeometry(double part)
    {
        var c = Center;
        double r = 165, start = 135, sweep = 270 * Math.Clamp(part, 0.001, 1);
        Point At(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(start), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(start + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private void Gauge(double t, Point c)
    {
        double part = Ease(t / 2.4, inverse: false);
        if (_arc != null) _arc.Data = ArcGeometry(part);
        if (_canvas.Children[1] is Path track) track.Data = ArcGeometry(1);
        for (int i = 0; i < _ticks.Count; i++)
        {
            double deg = 135 + 270.0 * i / (_ticks.Count - 1), rad = deg * Math.PI / 180;
            var tick = _ticks[i];
            tick.X1 = c.X + 192 * Math.Cos(rad); tick.Y1 = c.Y + 192 * Math.Sin(rad);
            tick.X2 = c.X + 206 * Math.Cos(rad); tick.Y2 = c.Y + 206 * Math.Sin(rad);
            bool lit = (double)i / (_ticks.Count - 1) <= part;
            tick.Stroke = lit ? new SolidColorBrush(i > _ticks.Count * 0.8 ? Colors.White : _accent) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        }
        if (_gaugeNumber != null)
        {
            _gaugeNumber.Text = $"{part * 100:0}%";
            _gaugeNumber.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_gaugeNumber, c.X - _gaugeNumber.DesiredSize.Width / 2);
            Canvas.SetTop(_gaugeNumber, c.Y + 120);
        }
        _coverShake.X = part > 0.85 && t < 2.5 ? (_random.NextDouble() - 0.5) * 4 : 0;
        _coverScale.ScaleX = _coverScale.ScaleY = t > 2.4 ? 1 + 0.1 * Math.Max(0, 1 - (t - 2.4) * 4) : 1;
    }

    private void Orbit(double t, Point c)
    {
        double f = Math.Min(t / 2.4, 1);
        double radius = 330 - 220 * Ease(f, inverse: true);
        double spin = t * (2 + 9 * f * f);
        for (int i = 0; i < _sparks.Count; i++)
        {
            double angle = spin + i * Math.PI * 2 / _sparks.Count;
            double wobble = 18 * Math.Sin(t * 3 + i);
            double r = t > 2.4 ? radius * Math.Max(0, 1 - (t - 2.4) * 3) : radius + wobble;
            var s = _sparks[i];
            double size = 6 + 8 * (1 - f) + (i % 3 == 0 ? 4 : 0);
            s.Width = s.Height = size;
            Canvas.SetLeft(s, c.X + Math.Cos(angle) * r - size / 2);
            Canvas.SetTop(s, c.Y + Math.Sin(angle) * r * 0.62 - size / 2);
            s.Opacity = t > 2.4 ? Math.Max(0, 1 - (t - 2.4) * 3) : 0.6 + 0.4 * Math.Sin(angle * 2 + t);
        }
        double beat = (t % 0.62);
        _coverScale.ScaleX = _coverScale.ScaleY = (beat < 0.08 || (beat > 0.18 && beat < 0.26)) && t < 2.4 ? 1.06 : t > 2.4 ? 1 + 0.12 * Math.Max(0, 1 - (t - 2.4) * 4) : 1;
        _glow.Opacity = t > 2.4 ? 1 : _glow.Opacity;
    }

    private void Shockwave(double t, Point c)
    {
        const double blast = 1.9;
        if (t < blast)
        {
            double f = t / blast;
            _coverShake.X = (_random.NextDouble() - 0.5) * 10 * f;
            _coverShake.Y = (_random.NextDouble() - 0.5) * 10 * f;
            _coverScale.ScaleX = _coverScale.ScaleY = 1 - 0.06 * f;
            _glow.Width = _glow.Height = 500 + 600 * f;
            double max = Math.Max(W, H) * 0.6;
            foreach (var (dot, angle, speed) in _motes)
            {
                double d = max * (1 - Math.Min(1, ((t * speed) % 1.0) + f * 0.2));
                Canvas.SetLeft(dot, c.X + Math.Cos(angle) * d);
                Canvas.SetTop(dot, c.Y + Math.Sin(angle) * d);
                dot.Opacity = 0.4 + 0.6 * (1 - d / max);
            }
        }
        else
        {
            double a = t - blast;
            _coverShake.X = _coverShake.Y = 0;
            _coverScale.ScaleX = _coverScale.ScaleY = 1 + 0.22 * Math.Max(0, 1 - a * 3);
            _flash.Opacity = Math.Max(0, 0.8 - a * 2.4);
            var ring = _rings[0];
            double r = 120 + 1400 * Ease(a / 0.8);
            ring.Width = ring.Height = r * 2;
            ring.StrokeThickness = Math.Max(1, 18 * (1 - a / 0.8));
            ring.Opacity = Math.Max(0, 1 - a / 0.8);
            Canvas.SetLeft(ring, c.X - r);
            Canvas.SetTop(ring, c.Y - r);
            foreach (var (dot, angle, speed) in _motes)
            {
                double d = 80 + a * 1500 * speed;
                Canvas.SetLeft(dot, c.X + Math.Cos(angle) * d);
                Canvas.SetTop(dot, c.Y + Math.Sin(angle) * d);
                dot.Opacity = Math.Max(0, 1 - a * 1.5);
            }
        }
    }

    /// <summary>Lands the cover after the build-up: a quick swell, a flash, and back to size.</summary>
    private void Landing(double t, double at = 2.4, double swell = 0.14, double flash = 0.5)
    {
        if (t < at) return;
        double a = t - at;
        _coverShake.X = _coverShake.Y = 0;
        _coverScale.ScaleX = _coverScale.ScaleY = 1 + swell * Math.Max(0, 1 - a * 4);
        _flash.Opacity = Math.Max(0, flash - a * 1.8);
    }

    private void Vortex(double t, Point c)
    {
        double f = Math.Min(t / 2.4, 1);
        double reach = Math.Max(W, H) * 0.55;
        for (int i = 0; i < _pieces.Count; i++)
        {
            double seed = _seeds[i];
            double life = (seed + t * (0.35 + 0.9 * f)) % 1.0;
            double r = t < 2.4 ? 110 + reach * (1 - life) * (1 - 0.5 * f) : (110 + reach * (1 - life)) * (1 + (t - 2.4) * 6);
            double angle = seed * Math.PI * 2 + (1 - life) * 5 + t * (1.5 + 5 * f);
            var p = _pieces[i];
            double size = 3 + 9 * life;
            p.Width = p.Height = size;
            Canvas.SetLeft(p, c.X + Math.Cos(angle) * r - size / 2);
            Canvas.SetTop(p, c.Y + Math.Sin(angle) * r - size / 2);
            p.Opacity = t < 2.4 ? 0.3 + 0.7 * life : Math.Max(0, 1 - (t - 2.4) * 2.5);
        }
        if (t < 2.4)
        {
            _coverScale.ScaleX = _coverScale.ScaleY = 1 - 0.08 * f;
            _cover.RenderTransformOrigin = new Point(0.5, 0.5);
            _coverShake.X = (_random.NextDouble() - 0.5) * 6 * f * f;
        }
        Landing(t, swell: 0.2);
    }

    private void PowerGrid(double t, Point c)
    {
        const int cols = 16, rows = 9;
        double cell = Math.Min(W / cols, H / rows);
        double ox = (W - cell * cols) / 2, oy = (H - cell * rows) / 2;
        for (int i = 0; i < _pieces.Count; i++)
        {
            int x = i % cols, y = i / cols;
            var p = _pieces[i];
            p.Width = p.Height = cell - 6;
            Canvas.SetLeft(p, ox + x * cell + 3);
            Canvas.SetTop(p, oy + y * cell + 3);
            double cx = ox + (x + 0.5) * cell, cy = oy + (y + 0.5) * cell;
            double d = Math.Sqrt((cx - c.X) * (cx - c.X) + (cy - c.Y) * (cy - c.Y)) / Math.Max(W, H);
            double glow = 0;
            for (int w = 0; w < 4; w++)
            {
                double front = (t - w * 0.6) * 1.1;
                glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(front - d) * 9) * (0.35 + 0.15 * w));
            }
            double baseLevel = 0.06 + 0.06 * _seeds[i];
            p.Opacity = t < 2.4 ? baseLevel + glow : Math.Max(0, (0.9 - (t - 2.4) * 1.5) * (1 - d));
        }
        double since = ((t + 0.0001) % 0.6) / 0.6;
        _coverScale.ScaleX = _coverScale.ScaleY = t < 2.4 ? 1 + 0.06 * Math.Max(0, 1 - since * 4) : _coverScale.ScaleX;
        Landing(t);
    }

    private void Lightning(double t, Point c)
    {
        double[] strikes = { 0.3, 0.9, 1.35, 1.75, 2.05, 2.25 };
        double latest = strikes.LastOrDefault(s => s <= t, -1);
        if (latest > _lastBolt && t < 2.4)
        {
            _lastBolt = latest;
            foreach (var shape in _pieces)
            {
                // A jagged path from a random spot at the screen's edge to the cover.
                var bolt = (Polyline)shape;
                double angle = _random.NextDouble() * Math.PI * 2, from = Math.Max(W, H) * 0.7;
                var points = new PointCollection();
                for (int k = 0; k <= 10; k++)
                {
                    double d = from * (1 - k / 10.0) + 95 * (k / 10.0);
                    double jitter = k is 0 or 10 ? 0 : (_random.NextDouble() - 0.5) * 70;
                    points.Add(new Point(c.X + Math.Cos(angle) * d - Math.Sin(angle) * jitter, c.Y + Math.Sin(angle) * d + Math.Cos(angle) * jitter));
                }
                bolt.Points = points;
            }
        }
        double age = t - _lastBolt;
        for (int i = 0; i < _pieces.Count; i++)
            _pieces[i].Opacity = _lastBolt < 0 || t >= 2.4 ? 0 : Math.Max(0, 1 - age * 5) * (i == 0 ? 1 : 0.6);
        if (t < 2.4)
        {
            _flash.Opacity = _lastBolt < 0 ? 0 : Math.Max(0, 0.25 - age * 1.5);
            double kick = _lastBolt < 0 ? 0 : Math.Max(0, 1 - age * 6);
            _coverShake.X = (_random.NextDouble() - 0.5) * 16 * kick;
            _coverShake.Y = (_random.NextDouble() - 0.5) * 16 * kick;
            _glow.Opacity = 0.4 + 0.6 * kick;
        }
        Landing(t, swell: 0.18, flash: 0.75);
    }

    private void Equalizer(double t, Point c)
    {
        int n = _pieces.Count;
        double beat = (t % 0.3) / 0.3, loud = Math.Min(t / 2.4, 1);
        for (int i = 0; i < n; i++)
        {
            double angle = i * Math.PI * 2 / n - Math.PI / 2;
            double level = t < 2.4
                ? 20 + (40 + 120 * loud) * Math.Abs(Math.Sin(t * 7 + _seeds[i])) * (0.5 + 0.5 * Math.Max(0, 1 - beat * 2.5))
                : Math.Max(0, 180 * (1 - (t - 2.4) * 2.5));
            var bar = (Rectangle)_pieces[i];
            bar.Height = Math.Max(4, level);
            double r = 140;
            Canvas.SetLeft(bar, c.X + Math.Cos(angle) * r - bar.Width / 2);
            Canvas.SetTop(bar, c.Y + Math.Sin(angle) * r);
            // Point each bar outward from the cover.
            bar.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, bar.Width / 2, 0);
            bar.Opacity = t < 2.4 ? 0.85 : Math.Max(0, 1 - (t - 2.4) * 2);
        }
        if (t < 2.4) _coverScale.ScaleX = _coverScale.ScaleY = 1 + 0.07 * loud * Math.Max(0, 1 - beat * 3);
        Landing(t, swell: 0.16);
    }

    private void Hyperloop(double t, Point c)
    {
        double speed = 0.25 + 1.6 * Math.Pow(Math.Min(t / 2.4, 1), 2);
        for (int i = 0; i < _pieces.Count; i++)
        {
            // Each ring rushes from the far end of the tunnel (tiny) toward you (past the edges of the screen).
            double z = (_seeds[i] + t * speed) % 1.0;
            double scale = 0.05 + Math.Pow(z, 3) * 2.2;
            var ring = (Rectangle)_pieces[i];
            ring.Width = W * scale;
            ring.Height = H * scale * 0.9;
            ring.RadiusX = ring.RadiusY = 40 * scale;
            ring.StrokeThickness = 1 + 7 * z;
            Canvas.SetLeft(ring, c.X - ring.Width / 2);
            Canvas.SetTop(ring, c.Y - ring.Height / 2);
            ring.Opacity = t < 2.4 ? Math.Min(1, z * 2) * (1 - Math.Max(0, z - 0.85) / 0.15) : Math.Max(0, 1 - (t - 2.4) * 3);
        }
        if (t < 2.4)
        {
            _coverShake.Y = Math.Sin(t * 40) * 3 * Math.Min(t / 2.4, 1);
            _coverScale.ScaleX = _coverScale.ScaleY = 0.9 + 0.1 * Math.Min(t / 2.4, 1);
        }
        Landing(t, swell: 0.22, flash: 0.7);
    }
}
