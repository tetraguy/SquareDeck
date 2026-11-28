using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.13: a stick's response curve, drawn as Armoury Crate draws it: stick movement along the bottom, response up the
/// side, four points on a grid. Left / right choose a point, up / down move it (its response), LB / RB move it along
/// (its movement), X steps through the presets, A keeps it, B leaves it as it was. Each change is sent to the controller
/// straight away, so the stick can be tried while editing.
/// </summary>
public sealed class ResponseCurveScreen : Screen
{
    private readonly string _title;
    private readonly int[] _start;
    private readonly int[] _curve;
    private readonly Action<int[]> _keep;
    private readonly Canvas _graph = new() { Width = 520, Height = 360, ClipToBounds = false };
    private readonly TextBlock _readout = Ui.Text("", 18, "FgBrush");
    private readonly TextBlock _preset = Ui.Text("", 15, "SubtleBrush");
    private int _point = 1;
    private bool _done;

    public override bool IsDialog => true;

    public ResponseCurveScreen(string stick, int[] curve, Action<int[]> keep)
    {
        _title = stick;
        _start = AllyHid.NormalizeCurve(curve);
        _curve = (int[])_start.Clone();
        _keep = keep;
        Switch2Settings.Refresh();

        var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(36, 28, 36, 24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1) };
        card.Background = new SolidColorBrush(Switch2Settings.Card);
        card.BorderBrush = new SolidColorBrush(Switch2Settings.Line);
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(stick, 28, "FgBrush"));
        stack.Children.Add(Ui.Text("Response Curve", 18, "SubtleBrush"));
        var holder = new Grid { Margin = new Thickness(0, 18, 0, 10) };
        holder.Children.Add(_graph);
        stack.Children.Add(holder);
        _readout.Margin = new Thickness(0, 6, 0, 2);
        stack.Children.Add(_readout);
        stack.Children.Add(_preset);
        var hints = new HintBar("◀▶  Point      ▲▼  Response      {LB}{RB}  Movement      {Details}  Presets      {Select}  Keep      {Back}  Cancel") { Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(hints);
        card.Child = stack;
        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) };
        root.Children.Add(card);
        Content = root;
        Draw();
    }

    public override bool OnAction(NavAction action)
    {
        int m = (_point - 1) * 2, r = m + 1;
        switch (action)
        {
            case NavAction.Left: _point = Math.Max(1, _point - 1); break;
            case NavAction.Right: _point = Math.Min(4, _point + 1); break;
            case NavAction.Up: _curve[r] = Math.Min(99, _curve[r] + 2); break;
            case NavAction.Down: _curve[r] = Math.Max(1, _curve[r] - 2); break;
            case NavAction.RB: _curve[m] = Math.Min(99, _curve[m] + 2); break;
            case NavAction.LB: _curve[m] = Math.Max(1, _curve[m] - 2); break;
            case NavAction.X:
            {
                var presets = AllyControl.CurvePresets;
                int at = Array.FindIndex(presets, p => p.Curve.SequenceEqual(AllyHid.NormalizeCurve(_curve)));
                var next = presets[(at + 1) % presets.Length].Curve;
                Array.Copy(next, _curve, 8);
                break;
            }
            case NavAction.Accept:
                _done = true;
                _keep(AllyHid.NormalizeCurve(_curve));
                Host.Pop(this);
                return true;
            case NavAction.Back:
                Host.Pop(this);
                return true;
            default: return false;
        }
        var fixedCurve = AllyHid.NormalizeCurve(_curve);
        Array.Copy(fixedCurve, _curve, 8);
        Try();
        Draw();
        UiSound.Move();
        return true;
    }

    public override void OnClosed()
    {
        if (!_done) Send(_start); // cancelled: the controller goes back to what it had
    }

    /// <summary>Sends the curve being edited to the controller (the other side and the rest of the tuning as they are).</summary>
    private void Try() => Send(_curve);

    private void Send(int[] curve)
    {
        var s = Host?.Settings;
        if (s == null) return;
        var t = AllyControl.TuningFor(s);
        t = _title.StartsWith("Left") ? t with { CurveL = (int[])curve.Clone() } : t with { CurveR = (int[])curve.Clone() };
        _ = Task.Run(() => AllyHid.SetTuning(t));
    }

    private void Draw()
    {
        _graph.Children.Clear();
        double w = _graph.Width, h = _graph.Height;
        var line = new SolidColorBrush(Switch2Settings.Line);
        for (int k = 0; k <= 10; k++)
        {
            _graph.Children.Add(new Line { X1 = w * k / 10, X2 = w * k / 10, Y1 = 0, Y2 = h, Stroke = line, StrokeThickness = 1 });
            _graph.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * k / 10, Y2 = h * k / 10, Stroke = line, StrokeThickness = 1 });
        }
        // The straight response, faintly, for comparison.
        _graph.Children.Add(new Line { X1 = 0, Y1 = h, X2 = w, Y2 = 0, Stroke = new SolidColorBrush(Switch2Settings.Subtle), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 }, Opacity = 0.6 });
        Point P(int move, int resp) => new(w * move / 100.0, h - h * resp / 100.0);
        var pts = new List<Point> { new(0, h) };
        for (int p = 0; p < 4; p++) pts.Add(P(_curve[p * 2], _curve[p * 2 + 1]));
        pts.Add(new Point(w, pts[^1].Y));
        var accent = (Brush)(TryFindResource("AccentBrush") ?? Brushes.DeepSkyBlue);
        var poly = new Polyline { Stroke = accent, StrokeThickness = 3, Points = new PointCollection(pts) };
        _graph.Children.Add(poly);
        for (int p = 0; p < 4; p++)
        {
            var at = P(_curve[p * 2], _curve[p * 2 + 1]);
            bool chosen = p + 1 == _point;
            double size = chosen ? 22 : 14;
            var dot = new Rectangle { Width = size, Height = size, Fill = chosen ? accent : Brushes.White, Stroke = chosen ? Brushes.White : accent, StrokeThickness = 2 };
            Canvas.SetLeft(dot, at.X - size / 2);
            Canvas.SetTop(dot, at.Y - size / 2);
            _graph.Children.Add(dot);
        }
        var axisX = Ui.Text("Stick movement (%)", 13, "SubtleBrush");
        Canvas.SetLeft(axisX, w - 150);
        Canvas.SetTop(axisX, h + 6);
        _graph.Children.Add(axisX);
        var axisY = Ui.Text("Response (%)", 13, "SubtleBrush");
        Canvas.SetLeft(axisY, 0);
        Canvas.SetTop(axisY, -22);
        _graph.Children.Add(axisY);

        int m = (_point - 1) * 2;
        _readout.Text = $"Point {_point}:  {_curve[m]}% movement gives {_curve[m + 1]}% response";
        var name = AllyControl.CurvePresets.FirstOrDefault(p => p.Curve.SequenceEqual(_curve)).Name;
        _preset.Text = name != null ? name : "Custom curve";
    }
}
