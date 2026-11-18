using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The easter egg: tap or press "SquareDeck version" five times in a row. After the second tap a small message
/// counts down, as Android does ("3 more taps to discover a secret"); the fifth opens <see cref="SecretScreen"/>.
/// </summary>
public static class EasterEgg
{
    private const int Needed = 5;
    private static int _taps;
    private static DateTime _last;

    public static void Tap(MainWindow host)
    {
        var now = DateTime.UtcNow;
        if ((now - _last).TotalSeconds > 2.5) _taps = 0; // a pause starts the count again
        _last = now;
        _taps++;
        if (_taps >= Needed)
        {
            _taps = 0;
            Pill(host, "You found it!");
            host.Push(new SecretScreen());
            return;
        }
        int left = Needed - _taps;
        if (_taps >= 2) Pill(host, left == 1 ? "1 more tap to discover a secret" : $"{left} more taps to discover a secret");
    }

    private static Border? _pill;

    /// <summary>A small rounded message near the bottom of the screen for a moment, like Android's.</summary>
    private static void Pill(MainWindow host, string text)
    {
        if (_pill != null) host.ToastLayer.Children.Remove(_pill);
        var label = new TextBlock { Text = text, FontSize = 17, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        var pill = new Border
        {
            Child = label, Padding = new Thickness(22, 11, 22, 12), CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x28)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90),
            IsHitTestVisible = false
        };
        _pill = pill;
        host.ToastLayer.Children.Add(pill);
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(120)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1500)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1800)));
        fade.Completed += (_, _) => { host.ToastLayer.Children.Remove(pill); if (_pill == pill) _pill = null; };
        pill.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

/// <summary>
/// The secret: a swarm of little squares flies in and builds the SquareDeck mark (the outlined square and the solid
/// one in its corner), a rainbow sweeps across it, the name types itself out, and squares rain down like confetti.
/// The controller beats along. Any button closes it once the mark is built.
/// </summary>
public sealed class SecretScreen : Screen
{
    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Black };
    private readonly TextBlock _name = new() { FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Opacity = 0 };
    private readonly TextBlock _thanks = new() { FontSize = 20, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), Opacity = 0, Text = "You found the secret. Thanks for playing ♥" };
    private readonly List<(Rectangle Box, Point From, Point To, double Delay, double Spin)> _swarm = new();
    private readonly List<(Rectangle Box, double X, double Speed, double Sway, double Spin)> _confetti = new();
    private readonly Random _random = new();
    private readonly List<(int At, int Ms, double Power)> _beats = new();
    private int _nextBeat;
    private DateTime _start;
    private Point _center;
    private const double Unit = 26; // one little square, and the grid the mark is built on

    public SecretScreen()
    {
        _canvas.Children.Add(_name);
        _canvas.Children.Add(_thanks);
        Content = _canvas;
        // The beat: a tap as each part lands, a roll as the rainbow sweeps, a big one at the end.
        for (int i = 0; i < 8; i++) _beats.Add((300 + i * 140, 40, 0.4));
        _beats.Add((1600, 160, 0.9));
        for (int i = 0; i < 6; i++) _beats.Add((2000 + i * 120, 50, 0.3 + i * 0.08));
        _beats.Add((2900, 240, 1.0));
        _beats.Add((3300, 60, 0.5));
        _beats.Add((3450, 60, 0.5));
    }

    public override void FocusDefault() { }

    public override void OnShown()
    {
        if (_start != default) return;
        double w = Math.Max(_canvas.ActualWidth, 1280), h = Math.Max(_canvas.ActualHeight, 720);
        _center = new Point(w / 2, h / 2 - 60);

        // The mark on a 13 × 13 grid: an outlined square (two units thick) with a solid one in its bottom-right corner.
        var targets = new List<Point>();
        for (int y = 0; y < 13; y++)
            for (int x = 0; x < 13; x++)
            {
                bool outline = (x < 2 || x > 10 || y < 2 || y > 10);
                bool solid = x >= 7 && x <= 9 && y >= 7 && y <= 9;
                if (outline || solid) targets.Add(new Point(_center.X + (x - 6.5) * Unit, _center.Y + (y - 6.5) * Unit));
            }
        foreach (var to in targets)
        {
            double angle = _random.NextDouble() * Math.PI * 2, distance = Math.Max(w, h) * (0.7 + _random.NextDouble() * 0.4);
            var from = new Point(_center.X + Math.Cos(angle) * distance, _center.Y + Math.Sin(angle) * distance);
            var box = new Rectangle { Width = Unit - 3, Height = Unit - 3, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.FromRgb(0x0A, 0xB9, 0xC9)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
            _canvas.Children.Add(box);
            _swarm.Add((box, from, to, _random.NextDouble() * 0.9, (_random.NextDouble() - 0.5) * 1080));
        }
        _name.Text = "";
        _start = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
    }

    public override void OnClosed() => CompositionTarget.Rendering -= OnFrame;

    public override bool OnAction(NavAction action)
    {
        if ((DateTime.UtcNow - _start).TotalSeconds > 2.2) Host.Pop(this);
        return true;
    }

    private static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

    private void OnFrame(object? sender, EventArgs e)
    {
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        while (_nextBeat < _beats.Count && t * 1000 >= _beats[_nextBeat].At)
        {
            var (_, ms, power) = _beats[_nextBeat++];
            Host.RumbleScaled(ms, power);
        }

        // 1. The swarm flies in and spins into place.
        for (int i = 0; i < _swarm.Count; i++)
        {
            var (box, from, to, delay, spin) = _swarm[i];
            double f = Ease((t - delay) / 0.9);
            double x = from.X + (to.X - from.X) * f, y = from.Y + (to.Y - from.Y) * f;
            // 3. Then a little breathing pulse, rippling out from the corner.
            double ripple = t > 1.8 ? Math.Max(0, Math.Sin((t - 1.8) * 6 - (to.X + to.Y - _center.X * 2) / 180)) * 4 : 0;
            Canvas.SetLeft(box, x - box.Width / 2 - ripple / 2);
            Canvas.SetTop(box, y - box.Height / 2 - ripple / 2);
            box.Width = box.Height = Unit - 3 + ripple;
            ((RotateTransform)box.RenderTransform).Angle = spin * (1 - f);
            // 2. A rainbow sweeps across the mark once it's built.
            if (t > 1.6 && t < 3.2)
            {
                double hue = ((to.X + to.Y) / 6 - (t - 1.6) * 360) % 360;
                box.Fill = new SolidColorBrush(FromHue(hue < 0 ? hue + 360 : hue));
            }
            else if (t >= 3.2) box.Fill = new SolidColorBrush(Color.FromRgb(0x0A, 0xB9, 0xC9));
        }

        // 4. The name types itself out under the mark.
        if (t > 2.2)
        {
            const string word = "SquareDeck";
            int letters = Math.Min(word.Length, (int)((t - 2.2) / 0.07) + 1);
            _name.Text = word[..letters];
            _name.Opacity = 1;
            _name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_name, _center.X - _name.DesiredSize.Width / 2);
            Canvas.SetTop(_name, _center.Y + 13 * Unit / 2 + 30);
        }
        if (t > 3.1)
        {
            _thanks.Opacity = Math.Min(1, (t - 3.1) * 2);
            _thanks.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_thanks, _center.X - _thanks.DesiredSize.Width / 2);
            Canvas.SetTop(_thanks, _center.Y + 13 * Unit / 2 + 120);
        }

        // 5. Confetti squares rain down.
        if (t > 2.9 && _confetti.Count < (Perf.Active ? 60 : 140) && _random.NextDouble() < 0.7)
        {
            var piece = new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(FromHue(_random.Next(360))), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
            _canvas.Children.Insert(0, piece);
            _confetti.Add((piece, _random.NextDouble() * _canvas.ActualWidth, 120 + _random.NextDouble() * 220, _random.NextDouble() * 40, (_random.NextDouble() - 0.5) * 600));
            Canvas.SetTop(piece, -20);
        }
        double h = _canvas.ActualHeight;
        for (int i = 0; i < _confetti.Count; i++)
        {
            var (piece, x, speed, sway, spin) = _confetti[i];
            double y = Canvas.GetTop(piece) + speed / 60;
            if (y > h + 20) y = -20;
            Canvas.SetTop(piece, y);
            Canvas.SetLeft(piece, x + Math.Sin(y / 50) * sway);
            ((RotateTransform)piece.RenderTransform).Angle += spin / 60;
        }
    }

    private static Color FromHue(double hue)
    {
        double h = hue / 60, x = 1 - Math.Abs(h % 2 - 1);
        (double r, double g, double b) = (int)h switch
        {
            0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x), 3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x)
        };
        return Color.FromRgb((byte)(40 + 215 * r), (byte)(40 + 215 * g), (byte)(40 + 215 * b));
    }
}
