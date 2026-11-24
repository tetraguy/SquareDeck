using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace SquareDeck.Widget.Controls
{
    /// <summary>
    /// The power dial: a 270° arc for the TDP range, filled in SquareDeck's colors (teal at the bottom of the range,
    /// through violet, to pink at the top), the watts large in the middle and a word for how hard it's working.
    /// The arc glides to a new value instead of jumping, and a soft glow behind it breathes with the power.
    /// </summary>
    public sealed class PowerDial : Grid
    {
        private const double Size = 196, Thickness = 15, StartAngle = 135, Sweep = 270;
        private readonly Path _track = new Path(), _arc = new Path(), _tip = new Path();
        private readonly Ellipse[] _glow = new Ellipse[6];
        private readonly TextBlock _watts = new TextBlock(), _unit = new TextBlock(), _mood = new TextBlock();
        private readonly DispatcherTimer _glide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private double _shown = -1, _target;
        private int _min = 5, _max = 30;
        private Color _accent = Color.FromArgb(255, 0x0A, 0xB9, 0xC9);
        private GradientStop _firstStop;

        public PowerDial()
        {
            Width = Size;
            Height = Size;

            // The glow behind: rings of the color, smaller ones on top, so it's brightest in the middle and fades out.
            // Teal, warming toward pink as the power rises.
            for (int i = 0; i < _glow.Length; i++)
            {
                double d = Size * (0.95 - i * 0.09);
                _glow[i] = new Ellipse { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                Children.Add(_glow[i]);
            }

            _track.Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            _track.StrokeThickness = Thickness;
            _track.StrokeStartLineCap = _track.StrokeEndLineCap = PenLineCap.Round;
            _track.Data = ArcGeometry(StartAngle, Sweep);
            Children.Add(_track);

            var fill = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
            _firstStop = new GradientStop { Color = _accent, Offset = 0 };
            fill.GradientStops.Add(_firstStop);
            fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0x3F, 0x7C, 0xFF), Offset = 0.45 });
            fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0xC4, 0x6B, 0xE6), Offset = 0.75 });
            fill.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0xFF, 0x7F, 0xB5), Offset = 1 });
            _arc.Stroke = fill;
            _arc.StrokeThickness = Thickness;
            _arc.StrokeStartLineCap = _arc.StrokeEndLineCap = PenLineCap.Round;
            Children.Add(_arc);

            // A bright bead at the end of the arc.
            _tip.Fill = new SolidColorBrush(Colors.White);
            Children.Add(_tip);

            var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var number = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            _watts.FontSize = 54;
            _watts.FontWeight = FontWeights.Bold;
            _watts.Foreground = new SolidColorBrush(Colors.White);
            _watts.Text = "--";
            number.Children.Add(_watts);
            _unit.Text = "W";
            _unit.FontSize = 20;
            _unit.FontWeight = FontWeights.SemiBold;
            _unit.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            _unit.VerticalAlignment = VerticalAlignment.Bottom;
            _unit.Margin = new Thickness(3, 0, 0, 11);
            number.Children.Add(_unit);
            middle.Children.Add(number);
            _mood.FontSize = 13;
            _mood.FontWeight = FontWeights.SemiBold;
            _mood.HorizontalAlignment = HorizontalAlignment.Center;
            _mood.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0xB9, 0xC9));
            _mood.Margin = new Thickness(0, -6, 0, 0);
            middle.Children.Add(_mood);
            Children.Add(middle);

            _glide.Tick += (s, e) => Step();
            Unloaded += (s, e) => _glide.Stop();
            Draw(0);
        }

        /// <summary>The highlight color the arc starts from (the widget's theme).</summary>
        public void SetAccent(Color accent)
        {
            _accent = accent;
            _firstStop.Color = accent;
            Draw(_shown < 0 ? 0 : _shown);
            if (_watts.Text != "--") _mood.Foreground = new SolidColorBrush(Blend(_target));
        }

        public void SetRange(int min, int max)
        {
            if (max <= min) return;
            _min = min;
            _max = max;
        }

        /// <summary>Moves the dial to a value (gliding there).</summary>
        public void SetWatts(int watts, bool animate = true)
        {
            _watts.Text = watts.ToString();
            double part = Math.Max(0, Math.Min(1, (watts - _min) / (double)(_max - _min)));
            _target = part;
            _mood.Text = Mood(part);
            _mood.Foreground = new SolidColorBrush(Blend(part));
            if (!animate || _shown < 0) { _shown = part; Draw(part); return; }
            if (!_glide.IsEnabled) _glide.Start();
        }

        /// <summary>No TDP to show (not set up, or SquareDeck isn't connected).</summary>
        public void SetEmpty(string word)
        {
            _watts.Text = "--";
            _mood.Text = word;
            _mood.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            _target = 0;
            if (!_glide.IsEnabled) _glide.Start();
        }

        private static string Mood(double part)
        {
            if (part < 0.2) return "Sipping power";
            if (part < 0.45) return "Cruising";
            if (part < 0.7) return "Balanced";
            if (part < 0.92) return "Performance";
            return "Full send";
        }

        private void Step()
        {
            double diff = _target - _shown;
            if (Math.Abs(diff) < 0.003) { _shown = _target; _glide.Stop(); }
            else _shown += diff * 0.2;
            Draw(_shown);
        }

        private void Draw(double part)
        {
            double sweep = Sweep * part;
            _arc.Visibility = sweep < 0.5 ? Visibility.Collapsed : Visibility.Visible;
            if (sweep >= 0.5) _arc.Data = ArcGeometry(StartAngle, sweep);
            var end = PointAt(StartAngle + sweep);
            double r = Thickness * 0.32;
            _tip.Data = new EllipseGeometry { Center = end, RadiusX = r, RadiusY = r };
            _tip.Visibility = sweep < 0.5 ? Visibility.Collapsed : Visibility.Visible;
            var color = Blend(part);
            byte alpha = (byte)(10 + 16 * part);
            var ring = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            foreach (var e in _glow) e.Fill = ring;
        }

        private static Point PointAt(double angle)
        {
            double radius = (Size - Thickness) / 2, rad = angle * Math.PI / 180;
            return new Point(Size / 2 + radius * Math.Cos(rad), Size / 2 + radius * Math.Sin(rad));
        }

        private static Geometry ArcGeometry(double start, double sweep)
        {
            double radius = (Size - Thickness) / 2;
            var figure = new PathFigure { StartPoint = PointAt(start), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = PointAt(start + Math.Min(sweep, 359.9)),
                Size = new Size(radius, radius),
                IsLargeArc = sweep > 180,
                SweepDirection = SweepDirection.Clockwise,
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>The highlight color (teal by default) → blue → violet → pink across the range.</summary>
        public Color Blend(double part)
        {
            Color[] stops = { _accent, Color.FromArgb(255, 0x3F, 0x7C, 0xFF), Color.FromArgb(255, 0xC4, 0x6B, 0xE6), Color.FromArgb(255, 0xFF, 0x7F, 0xB5) };
            double[] at = { 0, 0.45, 0.75, 1 };
            for (int i = 1; i < stops.Length; i++)
            {
                if (part > at[i]) continue;
                double t = (part - at[i - 1]) / (at[i] - at[i - 1]);
                Color a = stops[i - 1], b = stops[i];
                return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
            }
            return stops[stops.Length - 1];
        }
    }
}
