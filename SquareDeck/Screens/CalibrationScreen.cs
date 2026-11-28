using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.13: Recalibrate a stick, a trigger or the gyro, as Armoury Crate's Calibration does it, in the Switch 2 look.
///
/// Sticks: leave it alone (SquareDeck measures how far it drifts at rest), then turn it in big circles (how far it
/// reaches every way). The result goes into the controller: a dead zone just past the drift, and "reaches full at" just
/// inside the reach, for that stick only. Triggers: let go, then press all the way. Gyro: lay the Ally flat, and its
/// resting reading is taken off every reading from then on. Y puts a stick or trigger back to the calibration it left
/// the factory with (in the controller itself) and its tuning to ASUS's defaults.
/// </summary>
public sealed class CalibrationScreen : Screen
{
    public enum Kind { LeftStick, RightStick, LeftTrigger, RightTrigger, Gyro }

    private readonly Kind _kind;
    private readonly Gamepad _pad = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TextBlock _step = Ui.Text("", 20, "FgBrush", bold: true);
    private readonly TextBlock _detail = Ui.Text("", 16, "SubtleBrush");
    private readonly Canvas _art = new() { Width = 420, Height = 420 };
    private readonly Ellipse _dot = new() { Width = 26, Height = 26 };
    private readonly Path _cross = new() { StrokeThickness = 4 };
    private readonly Ellipse _ring = new() { StrokeThickness = 6, Width = 300, Height = 300 };
    private readonly Path _arc = new() { StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly StackPanel _gyroBars = new() { Width = 760 };
    private string _phase = "ready"; // ready, rest, move, done
    private DateTime _phaseAt;
    private readonly List<(double X, double Y)> _rest = new();
    private readonly double[] _reach = new double[16];
    private double _turned, _lastAngle = double.NaN;
    private (double X, double Y, double Z) _gyroSum;
    private int _gyroCount;
    private AllyHid.Tuning? _saved;

    private bool IsStick => _kind is Kind.LeftStick or Kind.RightStick;
    private bool IsTrigger => _kind is Kind.LeftTrigger or Kind.RightTrigger;
    private bool Left => _kind is Kind.LeftStick or Kind.LeftTrigger;

    public static string NameOf(Kind k) => k switch
    {
        Kind.LeftStick => "Left Stick", Kind.RightStick => "Right Stick", Kind.LeftTrigger => "Left Trigger", Kind.RightTrigger => "Right Trigger", _ => "Gyro"
    };

    public CalibrationScreen(Kind kind)
    {
        _kind = kind;
        Switch2Settings.Refresh();
        var root = new Grid { Background = new SolidColorBrush(Switch2Settings.Background) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

        var head = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(78, 26, 62, 0) };
        var glyph = Ui.Glyph("\uE7C9", 32, "FgBrush");
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 2, 16, 0);
        head.Children.Add(glyph);
        var title = Ui.Text("Recalibrate " + NameOf(kind), 32, "FgBrush");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        root.Children.Add(head);
        var under = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 0), Fill = new SolidColorBrush(Switch2Settings.Line) };
        root.Children.Add(under);

        var body = new Grid { Margin = new Thickness(90, 24, 90, 10) };
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        words.Children.Add(_step);
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.MaxWidth = 900;
        _detail.Margin = new Thickness(0, 8, 0, 0);
        words.Children.Add(_detail);
        body.Children.Add(words);
        var artHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
        if (kind == Kind.Gyro) artHost.Children.Add(_gyroBars);
        else artHost.Children.Add(_art);
        body.Children.Add(artHost);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var foot = new Grid { Margin = new Thickness(40, 0, 40, 0) };
        foot.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Fill = new SolidColorBrush(Switch2Settings.Line) });
        var hints = new HintBar(kind == Kind.Gyro ? "{Select}  Calibrate      {Details}  Forget the calibration      {Back}  Back"
                                                  : "{Options}  Back to Default Settings      {Select}  Calibrate      {Back}  Back")
            { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 26, 0) };
        foot.Children.Add(hints);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;

        BuildArt();
        _tick.Tick += (_, _) => Frame();
        Ready();
    }

    public override void OnShown()
    {
        if (_kind == Kind.Gyro) GyroAim.Configure(Host.Settings, measuring: true);
        _tick.Start();
    }

    public override void OnClosed()
    {
        _tick.Stop();
        if (_kind == Kind.Gyro) GyroAim.Configure(Host.Settings);
        if (_saved != null) { var t = _saved; _ = Task.Run(() => AllyHid.SetTuning(t)); } // left mid-way: the tuning goes back
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Accept when _phase is "ready" or "done":
                Start();
                return true;
            case NavAction.Y when _kind != Kind.Gyro && _phase is "ready" or "done":
                _ = FactoryAsync();
                return true;
            case NavAction.X when _kind == Kind.Gyro:
                var s = Host.Settings;
                s.GyroBiasX = s.GyroBiasY = s.GyroBiasZ = 0;
                s.Save();
                _step.Text = "The gyro calibration is cleared.";
                return true;
            case NavAction.Back:
                Host.Pop(this);
                return true;
        }
        return true; // nothing else leaks out while calibrating
    }

    // ---------------------------------------------------------------- steps

    private void Ready()
    {
        _phase = "ready";
        _step.Text = "Press A to start calibration.";
        _detail.Text = _kind switch
        {
            Kind.Gyro => "Lay the ROG Ally flat on a still surface. Make sure the dots stay close to the middle, or the aim will drift in games.",
            _ when IsTrigger => "1. Let go of the trigger.  2. When asked, press it all the way down and hold it.",
            _ => "1. ✚ is shown when the stick is left alone; the dot while it moves.  2. When asked, turn the stick in big circles so the ring lights all the way round."
        };
    }

    private void Start()
    {
        var s = Host.Settings;
        if (_kind != Kind.Gyro && !AllyHid.HasController)
        {
            _step.Text = "SquareDeck can't reach the controller settings right now.";
            _detail.Text = "Try again in a moment (it reconnects by itself), or restart SquareDeck.";
            return;
        }
        _rest.Clear();
        Array.Clear(_reach);
        _turned = 0;
        _lastAngle = double.NaN;
        _gyroSum = (0, 0, 0);
        _gyroCount = 0;
        if (_kind != Kind.Gyro)
        {
            // Measured with this side's dead zone and travel opened all the way, so the readings are the stick's own.
            _saved = AllyControl.TuningFor(s);
            var open = _kind switch
            {
                Kind.LeftStick => _saved with { StickL = (0, 100), Adz = (0, _saved.Adz.Right), CurveL = AllyHid.LinearCurve },
                Kind.RightStick => _saved with { StickR = (0, 100), Adz = (_saved.Adz.Left, 0), CurveR = AllyHid.LinearCurve },
                Kind.LeftTrigger => _saved with { TriggerL = (0, 100) },
                _ => _saved with { TriggerR = (0, 100) },
            };
            _ = Task.Run(() => AllyHid.SetTuning(open));
        }
        _phase = "rest";
        _phaseAt = DateTime.UtcNow;
        _step.Text = _kind == Kind.Gyro ? "Measuring… keep it still." : IsTrigger ? "Let go of the trigger…" : "Leave the stick alone…";
        _detail.Text = "";
        UiSound.Select();
    }

    private void Frame()
    {
        if (_kind == Kind.Gyro) { GyroFrame(); return; }
        var raw = _pad.GetRaw();
        double x = 0, y = 0, trig = 0;
        if (raw.Connected)
        {
            static double N(short v) => Math.Clamp(v / 32767.0, -1, 1);
            if (_kind == Kind.LeftStick) { x = N(raw.LeftX); y = N(raw.LeftY); }
            else if (_kind == Kind.RightStick) { x = N(raw.RightX); y = N(raw.RightY); }
            else trig = (Left ? raw.LeftTrigger : raw.RightTrigger) / 255.0;
        }
        Show(x, y, trig);
        double age = (DateTime.UtcNow - _phaseAt).TotalSeconds;
        switch (_phase)
        {
            case "rest":
                if (age > 0.4) _rest.Add(IsTrigger ? (trig, 0) : (x, y)); // a moment to settle first
                if (age >= 2.2)
                {
                    _phase = "move";
                    _phaseAt = DateTime.UtcNow;
                    _step.Text = IsTrigger ? "Now press the trigger all the way down and hold it." : "Now turn the stick in big circles, three times.";
                    UiSound.Select();
                }
                break;
            case "move":
                if (IsTrigger)
                {
                    _reach[0] = Math.Max(_reach[0], trig);
                    if (age > 1.5 && trig > 0.2 && _reach[0] - trig < 0.02 && age > 3) Finish();
                    if (age > 8) Finish();
                }
                else
                {
                    double mag = Math.Sqrt(x * x + y * y);
                    if (mag > 0.4)
                    {
                        double angle = Math.Atan2(y, x);
                        int sector = (int)((angle + Math.PI) / (2 * Math.PI) * 16) % 16;
                        _reach[sector] = Math.Max(_reach[sector], mag);
                        if (!double.IsNaN(_lastAngle))
                        {
                            double d = angle - _lastAngle;
                            if (d > Math.PI) d -= 2 * Math.PI;
                            if (d < -Math.PI) d += 2 * Math.PI;
                            _turned += Math.Abs(d);
                        }
                        _lastAngle = angle;
                    }
                    _detail.Text = $"{Math.Min(3, _turned / (2 * Math.PI)):0.0} of 3 turns";
                    if (_turned >= 3 * 2 * Math.PI && _reach.All(r => r > 0)) Finish();
                    if (age > 20) Finish();
                }
                break;
        }
    }

    private void Finish()
    {
        var s = Host.Settings;
        _phase = "done";
        double drift = _rest.Count == 0 ? 0 : IsTrigger ? _rest.Max(r => r.X) : _rest.Max(r => Math.Sqrt(r.X * r.X + r.Y * r.Y));
        double reach = IsTrigger ? _reach[0] : _reach.Where(r => r > 0).DefaultIfEmpty(1).Min();
        // A dead zone just past the drift, and full reached just inside the shortest reach.
        int inner = drift < 0.02 ? 0 : Math.Min(IsTrigger ? 50 : 50, (int)Math.Ceiling(drift * 100) + 2);
        int outer = reach >= 0.985 ? 100 : Math.Max(IsTrigger ? 50 : 60, (int)Math.Floor(reach * 100) - 1);
        switch (_kind)
        {
            case Kind.LeftStick:
                if (s.AllyStickInnerR < 0) s.AllyStickInnerR = s.AllyStickInner;
                if (s.AllyStickOuterR < 0) s.AllyStickOuterR = s.AllyStickOuter;
                s.AllyStickInner = inner; s.AllyStickOuter = outer; break;
            case Kind.RightStick: s.AllyStickInnerR = inner; s.AllyStickOuterR = outer; break;
            case Kind.LeftTrigger:
                if (s.AllyTriggerInnerR < 0) s.AllyTriggerInnerR = s.AllyTriggerInner;
                if (s.AllyTriggerOuterR < 0) s.AllyTriggerOuterR = s.AllyTriggerOuter;
                s.AllyTriggerInner = inner; s.AllyTriggerOuter = outer; break;
            case Kind.RightTrigger: s.AllyTriggerInnerR = inner; s.AllyTriggerOuterR = outer; break;
        }
        s.Save();
        _saved = null;
        _ = Task.Run(AllyControl.ApplyTuning);
        UiSound.Toast();
        _step.Text = "Calibrated.";
        string driftText = drift < 0.02 ? "No drift at rest" : $"It drifted up to {drift * 100:0}% at rest, so the dead zone is now {inner}%";
        string reachText = reach >= 0.985 ? "it reaches full every way" : $"its shortest reach was {reach * 100:0}%, so it now counts as full at {outer}%";
        _detail.Text = driftText + ", and " + reachText + ". Press A to calibrate again, or B to go back.";
    }

    private async Task FactoryAsync()
    {
        var axis = _kind switch
        {
            Kind.LeftStick => AllyHid.CalibrationAxis.LeftStick, Kind.RightStick => AllyHid.CalibrationAxis.RightStick,
            Kind.LeftTrigger => AllyHid.CalibrationAxis.LeftTrigger, _ => AllyHid.CalibrationAxis.RightTrigger
        };
        if (await Host.AskAsync("Back to default settings?", "The " + NameOf(_kind).ToLowerInvariant() + " goes back to the calibration it left the factory with, " +
                "and its dead zone and travel to ASUS's defaults.", "Reset", "Cancel") != 0) return;
        bool ok = await Task.Run(() => AllyHid.ResetCalibration(axis));
        var s = Host.Settings;
        switch (_kind)
        {
            case Kind.LeftStick: if (s.AllyStickInnerR < 0) s.AllyStickInnerR = s.AllyStickInner; if (s.AllyStickOuterR < 0) s.AllyStickOuterR = s.AllyStickOuter; s.AllyStickInner = 0; s.AllyStickOuter = 100; s.AllyAdzL = 0; break;
            case Kind.RightStick: s.AllyStickInnerR = 0; s.AllyStickOuterR = 100; s.AllyAdzR = 0; break;
            case Kind.LeftTrigger: if (s.AllyTriggerInnerR < 0) s.AllyTriggerInnerR = s.AllyTriggerInner; if (s.AllyTriggerOuterR < 0) s.AllyTriggerOuterR = s.AllyTriggerOuter; s.AllyTriggerInner = 0; s.AllyTriggerOuter = 100; break;
            case Kind.RightTrigger: s.AllyTriggerInnerR = 0; s.AllyTriggerOuterR = 100; break;
        }
        s.Save();
        _ = Task.Run(AllyControl.ApplyTuning);
        _step.Text = ok ? "Back to the factory calibration." : "The controller didn't take the reset.";
        _detail.Text = ok ? "Its dead zone and travel are at ASUS's defaults too." : "Try again in a moment.";
    }

    // ---------------------------------------------------------------- drawing

    private void BuildArt()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xF0, 0x30, 0x4A));
        var dim = new SolidColorBrush(Switch2Settings.Line);
        var fg = new SolidColorBrush(Switch2Settings.Text);
        if (_kind == Kind.Gyro)
        {
            foreach (var name in new[] { "Roll", "Yaw", "Pitch" })
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 34) };
                row.Children.Add(Ui.Text(name, 18, "FgBrush"));
                var value = Ui.Text("", 16, "SubtleBrush");
                value.HorizontalAlignment = HorizontalAlignment.Right;
                value.Tag = name;
                row.Children.Add(value);
                var track = new Grid { Height = 22, Margin = new Thickness(0, 30, 0, 0) };
                track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = dim, VerticalAlignment = VerticalAlignment.Center });
                track.Children.Add(new Border { Width = 2, Height = 18, Background = accent, HorizontalAlignment = HorizontalAlignment.Center });
                var knob = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Tag = name + ":knob" };
                track.Children.Add(knob);
                row.Children.Add(track);
                _gyroBars.Children.Add(row);
            }
            return;
        }
        if (IsStick)
        {
            var outerRing = new Ellipse { Width = 380, Height = 380, Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)) };
            Canvas.SetLeft(outerRing, 20); Canvas.SetTop(outerRing, 20);
            _art.Children.Add(outerRing);
            _ring.Stroke = dim;
            Canvas.SetLeft(_ring, 60); Canvas.SetTop(_ring, 60);
            _art.Children.Add(_ring);
            _art.Children.Add(new Line { X1 = 210, Y1 = 0, X2 = 210, Y2 = 420, Stroke = accent, StrokeThickness = 1.5, Opacity = 0.7 });
            _art.Children.Add(new Line { X1 = 0, Y1 = 210, X2 = 420, Y2 = 210, Stroke = accent, StrokeThickness = 1.5, Opacity = 0.7 });
            _dot.Fill = accent;
            _art.Children.Add(_dot);
            _cross.Stroke = accent;
            _cross.Data = Geometry.Parse("M -14,0 L 14,0 M 0,-14 L 0,14");
            _art.Children.Add(_cross);
        }
        else
        {
            var track = new Path { Stroke = dim, StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = ArcGeometry(1) };
            _art.Children.Add(track);
            _arc.Stroke = accent;
            _art.Children.Add(_arc);
            _dot.Fill = Brushes.White;
            _art.Children.Add(_dot);
            var label = Ui.Text(NameOf(_kind), 18, "FgBrush");
            Canvas.SetLeft(label, 230); Canvas.SetTop(label, 196);
            _art.Children.Add(label);
        }
    }

    /// <summary>A half-ring from the top round the left side to the bottom, filled to <paramref name="part"/>.</summary>
    private static Geometry ArcGeometry(double part)
    {
        double cx = 210, cy = 210, r = 170;
        double a0 = -Math.PI / 2, a1 = a0 - Math.PI * Math.Clamp(part, 0.001, 1);
        var start = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var end = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));
        var fig = new PathFigure { StartPoint = start };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, part > 0.999 && false, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    private void Show(double x, double y, double trig)
    {
        if (IsStick)
        {
            double px = 210 + x * 150, py = 210 - y * 150;
            bool rest = Math.Sqrt(x * x + y * y) < 0.06;
            _cross.Visibility = rest ? Visibility.Visible : Visibility.Collapsed;
            _dot.Visibility = rest ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(_dot, px - 13); Canvas.SetTop(_dot, py - 13);
            _cross.RenderTransform = new TranslateTransform(px, py);
            int lit = _reach.Count(r => r > 0.4);
            _ring.Stroke = lit >= 16 ? _dot.Fill : new SolidColorBrush(Switch2Settings.Line);
        }
        else
        {
            _arc.Data = ArcGeometry(Math.Max(0.001, trig));
            double a = -Math.PI / 2 - Math.PI * trig;
            Canvas.SetLeft(_dot, 210 + 170 * Math.Cos(a) - 13);
            Canvas.SetTop(_dot, 210 + 170 * Math.Sin(a) - 13);
            _dot.Width = _dot.Height = 26;
        }
    }

    private void GyroFrame()
    {
        var raw = GyroAim.Raw;
        var s = Host.Settings;
        (double X, double Y, double Z) shown = _phase == "rest" ? raw : (raw.X - s.GyroBiasX, raw.Y - s.GyroBiasY, raw.Z - s.GyroBiasZ);
        void Bar(string name, double value)
        {
            foreach (var row in _gyroBars.Children.OfType<Grid>())
            {
                foreach (var t in row.Children.OfType<TextBlock>().Where(t => t.Tag as string == name)) t.Text = $"{value:0.000} °/s";
                foreach (var track in row.Children.OfType<Grid>())
                    foreach (var k in track.Children.OfType<Ellipse>().Where(e => e.Tag as string == name + ":knob"))
                        k.Margin = new Thickness(Math.Clamp(0.5 + value / 20.0, 0, 1) * Math.Max(0, _gyroBars.Width - 18), 0, 0, 0);
            }
        }
        Bar("Roll", shown.Z);
        Bar("Yaw", shown.Y);
        Bar("Pitch", shown.X);
        if (!GyroAim.Available) { _step.Text = "Windows doesn't show a gyro on this device."; return; }
        if (_phase != "rest") return;
        double age = (DateTime.UtcNow - _phaseAt).TotalSeconds;
        if (age > 0.5) { _gyroSum = (_gyroSum.X + raw.X, _gyroSum.Y + raw.Y, _gyroSum.Z + raw.Z); _gyroCount++; }
        if (age < 3.5) return;
        if (_gyroCount > 10)
        {
            s.GyroBiasX = _gyroSum.X / _gyroCount;
            s.GyroBiasY = _gyroSum.Y / _gyroCount;
            s.GyroBiasZ = _gyroSum.Z / _gyroCount;
            s.Save();
            _step.Text = "Calibrated.";
            _detail.Text = "Its resting reading is taken off from now on, so the aim stays still when you do. Press A to calibrate again, or B to go back.";
            UiSound.Toast();
        }
        else
        {
            _step.Text = "The gyro didn't send any readings.";
            _detail.Text = "Make sure nothing else holds the sensor, then try again.";
        }
        _phase = "done";
    }
}
