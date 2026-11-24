using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace SquareDeck.Widget.Controls
{
    /// <summary>A small line of the last half minute of a number (under each live stat), with a faint fill below it.</summary>
    public sealed class Sparkline : Grid
    {
        private const int Keep = 30;
        private readonly Queue<double> _values = new Queue<double>();
        private readonly Polyline _line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        private readonly Polygon _area = new Polygon();
        private readonly double? _fixedMax;

        public Sparkline(Color color, double? max = null)
        {
            _fixedMax = max;
            Height = 26;
            _line.Stroke = new SolidColorBrush(color);
            _area.Fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
            Children.Add(_area);
            Children.Add(_line);
            SizeChanged += (s, e) => Draw();
        }

        public void SetColor(Color color)
        {
            _line.Stroke = new SolidColorBrush(color);
            _area.Fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
        }

        public void Add(double value)
        {
            _values.Enqueue(value);
            while (_values.Count > Keep) _values.Dequeue();
            Draw();
        }

        public void Clear()
        {
            _values.Clear();
            Draw();
        }

        private void Draw()
        {
            double w = ActualWidth, h = Height;
            var points = new PointCollection();
            var area = new PointCollection();
            if (w <= 0 || _values.Count < 2) { _line.Points = points; _area.Points = area; return; }
            double max = _fixedMax ?? System.Math.Max(1, _values.Max() * 1.15);
            var list = _values.ToList();
            double step = w / (Keep - 1);
            double x0 = w - step * (list.Count - 1);
            area.Add(new Point(x0, h));
            for (int i = 0; i < list.Count; i++)
            {
                var p = new Point(x0 + step * i, h - 2 - (h - 4) * System.Math.Min(1, list[i] / max));
                points.Add(p);
                area.Add(p);
            }
            area.Add(new Point(w, h));
            _line.Points = points;
            _area.Points = area;
        }
    }
}
