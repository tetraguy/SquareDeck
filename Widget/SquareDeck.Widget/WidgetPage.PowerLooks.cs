using System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using SquareDeck.Widget.Controls;
using Windows.Foundation;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Shapes;

namespace SquareDeck.Widget
{
    /// <summary>
    /// Smaller looks for the Power card's wattage (Experimental, chosen in SquareDeck: Settings > Experimental >
    /// Widget: wattage look): the full dial (as before), a slim bar, a mini dial beside the presets, the number alone
    /// (select it for the full dial), or preset chips with a "custom" chip for exact watts.
    /// </summary>
    public sealed partial class WidgetPage
    {
        private string _powerLook = "dial";
        private Grid _presetsGrid;
        private Grid _lookBar, _lookMini, _lookChips;
        private StackPanel _lookNumber;
        private ColumnDefinition _barFill, _barRest;
        private TextBlock _barText, _numberText, _chipCustomText;
        private PowerDial _miniDial;
        private readonly Button[] _miniChips = new Button[4];
        private readonly Button[] _chips = new Button[4];
        private readonly TextBlock[] _miniChipText = new TextBlock[4];
        private readonly TextBlock[] _chipText = new TextBlock[4];
        private Button _barMinus, _barPlus, _numberMinus, _numberPlus, _numberButton, _chipCustom;
        private bool _numberOpen, _chipsCustom;

        /// <summary>Builds the other looks into the Power card, just under the dial (hidden until chosen).</summary>
        private void BuildPowerLooks(StackPanel body, Grid presets)
        {
            _presetsGrid = presets;
            int at = body.Children.IndexOf(_powerBody) + 1;

            // Slim bar: [–] [■■■■ 15 W ····] [+]
            _lookBar = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lookBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _lookBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _barMinus = SmallButton("", "Less power", () => SetTdp(_watts - 1));
            _lookBar.Children.Add(_barMinus);
            var track = new Grid { Height = 34, Margin = new Thickness(8, 0, 8, 0) };
            var trackBack = new Border { CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
            track.Children.Add(trackBack);
            var fillGrid = new Grid();
            _barFill = new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) };
            _barRest = new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) };
            fillGrid.ColumnDefinitions.Add(_barFill);
            fillGrid.ColumnDefinitions.Add(_barRest);
            fillGrid.Children.Add(new Border { CornerRadius = new CornerRadius(17), Background = _accentBrush, Opacity = 0.8 });
            track.Children.Add(fillGrid);
            _barText = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) };
            track.Children.Add(_barText);
            Grid.SetColumn(track, 1);
            _lookBar.Children.Add(track);
            _barPlus = SmallButton("", "More power", () => SetTdp(_watts + 1));
            Grid.SetColumn(_barPlus, 2);
            _lookBar.Children.Add(_barPlus);

            // Mini dial: a small ring, with the four presets beside it as small chips.
            _lookMini = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookMini.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lookMini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _miniDial = new PowerDial();
            _miniDial.SetAccent(_accent);
            _lookMini.Children.Add(new Viewbox { Height = 96, Width = 96, Child = _miniDial, VerticalAlignment = VerticalAlignment.Center });
            var miniChips = new Grid { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            miniChips.ColumnDefinitions.Add(new ColumnDefinition());
            miniChips.ColumnDefinitions.Add(new ColumnDefinition());
            miniChips.RowDefinitions.Add(new RowDefinition());
            miniChips.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _miniChipText[i] = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var b = new Button { Style = DeckButton, Content = _miniChipText[i], Height = 38, Margin = new Thickness(3), Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
                b.Click += (s, e) => { if (_presetValues[index] > 0) SetTdp(_presetValues[index]); };
                Grid.SetColumn(b, i % 2);
                Grid.SetRow(b, i / 2);
                miniChips.Children.Add(b);
                _miniChips[i] = b;
            }
            Grid.SetColumn(miniChips, 1);
            _lookMini.Children.Add(miniChips);

            // The number alone: ‹ 15 W ›, and selecting the number opens the full dial underneath.
            _lookNumber = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            _numberMinus = SmallButton("", "Less power", () => SetTdp(_watts - 1));
            _lookNumber.Children.Add(_numberMinus);
            _numberText = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, Foreground = _accentBrush };
            _numberButton = new Button { Style = DeckButton, Content = _numberText, Margin = new Thickness(10, 0, 10, 0), Padding = new Thickness(18, 2, 18, 4), MinWidth = 120 };
            ToolTipService.SetToolTip(_numberButton, "Show the full dial");
            _numberButton.Click += (s, e) => { _numberOpen = !_numberOpen; ApplyPowerLook(_powerLook); };
            _lookNumber.Children.Add(_numberButton);
            _numberPlus = SmallButton("", "More power", () => SetTdp(_watts + 1));
            _lookNumber.Children.Add(_numberPlus);

            // Preset chips: 10 W · 15 W · 20 W · 25 W · custom (the custom chip opens the slim bar for exact watts).
            _lookChips = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            for (int i = 0; i < 5; i++) _lookChips.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _chipText[i] = new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Button { Style = DeckButton, Content = _chipText[i], Height = 40, Margin = new Thickness(2), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(20) };
                ToolTipService.SetToolTip(b, PresetLooks[i].Name);
                b.Click += (s, e) => { if (_presetValues[index] > 0) SetTdp(_presetValues[index]); };
                Grid.SetColumn(b, i);
                _lookChips.Children.Add(b);
                _chips[i] = b;
            }
            _chipCustomText = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
            _chipCustom = new Button { Style = DeckButton, Content = _chipCustomText, Height = 40, Margin = new Thickness(2), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(20) };
            ToolTipService.SetToolTip(_chipCustom, "Custom: exact watts");
            _chipCustom.Click += (s, e) => { _chipsCustom = !_chipsCustom; ApplyPowerLook(_powerLook); };
            Grid.SetColumn(_chipCustom, 4);
            _lookChips.Children.Add(_chipCustom);

            BuildMoreLooks(body, at); // 1.6.20: gauge, battery cells, big stepper, slider, ruler
            body.Children.Insert(at, _lookChips);
            body.Children.Insert(at, _lookNumber);
            body.Children.Insert(at, _lookMini);
            body.Children.Insert(at, _lookBar);
        }

        private Button SmallButton(string glyph, string tip, Action click)
        {
            var b = new Button { Style = DeckButton, Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Padding = new Thickness(0), Content = Glyph(glyph, 14), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(b, tip);
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>Every look the widget knows (SquareDeck's Settings lists the same ids).</summary>
        private static readonly string[] Looks = { "dial", "bar", "mini", "number", "chips", "gauge", "cells", "stepper", "slider", "ruler" };

        /// <summary>Shows the chosen look (one of <see cref="Looks"/>; anything else is the full dial).</summary>
        private void ApplyPowerLook(string look)
        {
            if (_lookBar == null) return;
            if (Array.IndexOf(Looks, look) < 0) look = "dial";
            if (look != _powerLook) { _numberOpen = false; _chipsCustom = false; }
            _powerLook = look;
            _powerBody.Visibility = look == "dial" || (look == "number" && _numberOpen) ? Visibility.Visible : Visibility.Collapsed;
            _presetsGrid.Visibility = look == "dial" ? Visibility.Visible : Visibility.Collapsed;
            _lookBar.Visibility = look == "bar" || (look == "chips" && _chipsCustom) ? Visibility.Visible : Visibility.Collapsed;
            _lookMini.Visibility = look == "mini" ? Visibility.Visible : Visibility.Collapsed;
            _lookNumber.Visibility = look == "number" ? Visibility.Visible : Visibility.Collapsed;
            _lookChips.Visibility = look == "chips" ? Visibility.Visible : Visibility.Collapsed;
            _lookGauge.Visibility = look == "gauge" ? Visibility.Visible : Visibility.Collapsed;
            _lookCells.Visibility = look == "cells" ? Visibility.Visible : Visibility.Collapsed;
            _lookStepper.Visibility = look == "stepper" ? Visibility.Visible : Visibility.Collapsed;
            _lookSlider.Visibility = look == "slider" ? Visibility.Visible : Visibility.Collapsed;
            _lookRuler.Visibility = look == "ruler" ? Visibility.Visible : Visibility.Collapsed;
            UpdatePowerLooks();
        }

        /// <summary>Keeps the smaller looks in step with the watts, range, presets and accent.</summary>
        private void UpdatePowerLooks()
        {
            if (_lookBar == null) return;
            string text = _tdpReady ? _watts + " W" : "–";
            double f = _tdpReady && _max > _min ? Math.Max(0.02, Math.Min(1, (double)(_watts - _min) / (_max - _min))) : 0.02;
            _barFill.Width = new GridLength(f, GridUnitType.Star);
            _barRest.Width = new GridLength(1 - f + 0.0001, GridUnitType.Star);
            _barText.Text = text;
            _numberText.Text = text;
            _chipCustomText.Text = _chipsCustom ? "" : "";
            if (_tdpReady) { _miniDial.SetRange(_min, _max); _miniDial.SetWatts(_watts); }
            else _miniDial.SetEmpty("–");
            _miniDial.SetAccent(_accent);
            UpdateMoreLooks(text, f);
            foreach (var b in new[] { _barMinus, _barPlus, _numberMinus, _numberPlus, _numberButton }) b.IsEnabled = _tdpReady;
            for (int i = 0; i < 4; i++)
            {
                string w = _presetValues[i] > 0 ? _presetValues[i] + " W" : PresetLooks[i].Name;
                _chipText[i].Text = w;
                _miniChipText[i].Text = w;
                bool on = _tdpReady && _presetValues[i] == _watts;
                foreach (var b in new[] { _chips[i], _miniChips[i] })
                {
                    b.IsEnabled = _tdpReady;
                    b.Background = new SolidColorBrush(on ? Color.FromArgb(0x55, _accent.R, _accent.G, _accent.B) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                    b.BorderBrush = new SolidColorBrush(on ? _accent : Colors.Transparent);
                }
            }
        }

        // ================================================================ 1.6.20: five more looks

        private Grid _lookGauge, _lookCells, _lookStepper, _lookSlider, _lookRuler;
        private Path _gaugeBack, _gaugeFill;
        private Line _gaugeNeedle;
        private TextBlock _gaugeText, _cellsText, _stepperText, _sliderText;
        private readonly Border[] _cells = new Border[10];
        private Button _gaugeMinus, _gaugePlus, _stepMinus, _stepPlus, _rulerMinus, _rulerPlus;
        private Slider _tdpSlider;
        private bool _sliderSyncing;
        private DispatcherTimer _sliderTimer;
        private int _sliderPending;
        private Canvas _ruler;
        private const double GaugeW = 200, GaugeH = 112, RulerW = 220, RulerH = 54;

        private void BuildMoreLooks(StackPanel body, int at)
        {
            // Gauge: a half-circle speedometer, the arc filling to the watts, a needle, minus and plus at its feet.
            _lookGauge = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookGauge.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lookGauge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _lookGauge.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _gaugeMinus = SmallButton("\uE738", "Less power", () => SetTdp(_watts - 1));
            _gaugeMinus.VerticalAlignment = VerticalAlignment.Bottom;
            _lookGauge.Children.Add(_gaugeMinus);
            var gauge = new Canvas { Width = GaugeW, Height = GaugeH };
            _gaugeBack = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _gaugeFill = new Path { Stroke = _accentBrush, StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _gaugeNeedle = new Line { Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round };
            var hub = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Colors.White) };
            Canvas.SetLeft(hub, GaugeW / 2 - 6);
            Canvas.SetTop(hub, GaugeH - 14 - 6);
            _gaugeText = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), Width = GaugeW, TextAlignment = TextAlignment.Center };
            Canvas.SetTop(_gaugeText, GaugeH - 58);
            _gaugeBack.Data = Arc(1);
            gauge.Children.Add(_gaugeBack);
            gauge.Children.Add(_gaugeFill);
            gauge.Children.Add(_gaugeText);
            gauge.Children.Add(_gaugeNeedle);
            gauge.Children.Add(hub);
            var gaugeBox = new Viewbox { Child = gauge, Height = GaugeH, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(gaugeBox, 1);
            _lookGauge.Children.Add(gaugeBox);
            _gaugePlus = SmallButton("\uE710", "More power", () => SetTdp(_watts + 1));
            _gaugePlus.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(_gaugePlus, 2);
            _lookGauge.Children.Add(_gaugePlus);

            // Battery cells: ten cells across, lit up to the watts; select a cell to go to that level.
            _lookCells = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookCells.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _lookCells.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cellRow = new Grid();
            for (int i = 0; i < 10; i++)
            {
                int index = i;
                cellRow.ColumnDefinitions.Add(new ColumnDefinition());
                _cells[i] = new Border { Height = 30, CornerRadius = new CornerRadius(i == 0 ? 8 : i == 9 ? 8 : 3), Margin = new Thickness(1.5, 0, 1.5, 0) };
                _cells[i].Tapped += (s2, e2) => { if (_tdpReady) SetTdp((int)Math.Round(_min + (_max - _min) * (index + 1) / 10.0)); };
                Grid.SetColumn(_cells[i], i);
                cellRow.Children.Add(_cells[i]);
            }
            _lookCells.Children.Add(cellRow);
            var cellFoot = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            cellFoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cellFoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cellFoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cellMinus = SmallButton("\uE738", "Less power", () => SetTdp(_watts - 1));
            cellFoot.Children.Add(cellMinus);
            _cellsText = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = _accentBrush };
            Grid.SetColumn(_cellsText, 1);
            cellFoot.Children.Add(_cellsText);
            var cellPlus = SmallButton("\uE710", "More power", () => SetTdp(_watts + 1));
            Grid.SetColumn(cellPlus, 2);
            cellFoot.Children.Add(cellPlus);
            Grid.SetRow(cellFoot, 1);
            _lookCells.Children.Add(cellFoot);

            // Big stepper: two big buttons for the thumbs, the watts large between them.
            _lookStepper = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookStepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _lookStepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            _lookStepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _stepMinus = new Button { Style = DeckButton, Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(14), Content = Glyph("\uE738", 22) };
            _stepMinus.Click += (s2, e2) => SetTdp(_watts - 1);
            ToolTipService.SetToolTip(_stepMinus, "Less power");
            _lookStepper.Children.Add(_stepMinus);
            var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _stepperText = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = _accentBrush };
            middle.Children.Add(_stepperText);
            middle.Children.Add(new TextBlock { Text = "watts", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Subtle) });
            Grid.SetColumn(middle, 1);
            _lookStepper.Children.Add(middle);
            _stepPlus = new Button { Style = DeckButton, Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(14), Content = Glyph("\uE710", 22) };
            _stepPlus.Click += (s2, e2) => SetTdp(_watts + 1);
            ToolTipService.SetToolTip(_stepPlus, "More power");
            Grid.SetColumn(_stepPlus, 2);
            _lookStepper.Children.Add(_stepPlus);

            // Slider: a leaf at the quiet end, a flame at the fast end, the watts above.
            _lookSlider = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookSlider.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _lookSlider.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _sliderText = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = _accentBrush };
            _lookSlider.Children.Add(_sliderText);
            var sliderLine = new Grid();
            sliderLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sliderLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leaf = Glyph("\uE708", 16);
            leaf.VerticalAlignment = VerticalAlignment.Center;
            leaf.Foreground = new SolidColorBrush(Subtle);
            sliderLine.Children.Add(leaf);
            _tdpSlider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, SmallChange = 1, LargeChange = 5, Foreground = _accentBrush, IsThumbToolTipEnabled = true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            _tdpSlider.ValueChanged += (s2, e2) =>
            {
                if (_sliderSyncing || !_tdpReady) return;
                _sliderPending = (int)Math.Round(e2.NewValue);
                _sliderText.Text = _sliderPending + " W";
                // Sent once the slider settles, not at every watt on the way.
                _sliderTimer.Stop();
                _sliderTimer.Start();
            };
            Grid.SetColumn(_tdpSlider, 1);
            sliderLine.Children.Add(_tdpSlider);
            var flame = Glyph("\uE945", 16);
            flame.VerticalAlignment = VerticalAlignment.Center;
            flame.Foreground = new SolidColorBrush(Subtle);
            Grid.SetColumn(flame, 2);
            sliderLine.Children.Add(flame);
            Grid.SetRow(sliderLine, 1);
            _lookSlider.Children.Add(sliderLine);
            _sliderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _sliderTimer.Tick += (s2, e2) => { _sliderTimer.Stop(); SetTdp(_sliderPending); };

            // Ruler: a tick for every watt, a number every five, a pointer at the watts; select the ruler to go there.
            _lookRuler = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 4) };
            _lookRuler.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lookRuler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _lookRuler.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rulerMinus = SmallButton("\uE738", "Less power", () => SetTdp(_watts - 1));
            _lookRuler.Children.Add(_rulerMinus);
            _ruler = new Canvas { Width = RulerW, Height = RulerH, Background = new SolidColorBrush(Colors.Transparent) };
            _ruler.Tapped += (s2, e2) =>
            {
                if (!_tdpReady || _max <= _min) return;
                double x = e2.GetPosition(_ruler).X;
                SetTdp((int)Math.Round(_min + (_max - _min) * Math.Max(0, Math.Min(1, (x - 10) / (RulerW - 20)))));
            };
            var rulerBox = new Viewbox { Child = _ruler, Height = RulerH, Margin = new Thickness(6, 0, 6, 0) };
            Grid.SetColumn(rulerBox, 1);
            _lookRuler.Children.Add(rulerBox);
            _rulerPlus = SmallButton("\uE710", "More power", () => SetTdp(_watts + 1));
            Grid.SetColumn(_rulerPlus, 2);
            _lookRuler.Children.Add(_rulerPlus);

            body.Children.Insert(at, _lookRuler);
            body.Children.Insert(at, _lookSlider);
            body.Children.Insert(at, _lookStepper);
            body.Children.Insert(at, _lookCells);
            body.Children.Insert(at, _lookGauge);
        }

        /// <summary>The top half of a circle, from the left end round to <paramref name="fraction"/> of the way.</summary>
        private static Geometry Arc(double fraction)
        {
            double cx = GaugeW / 2, cy = GaugeH - 14, r = GaugeW / 2 - 12;
            double angle = Math.PI * (1 - Math.Max(0.001, Math.Min(1, fraction)));
            var start = new Point(cx - r, cy);
            var end = new Point(cx + r * Math.Cos(angle), cy - r * Math.Sin(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment { Point = end, Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = false });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private void UpdateMoreLooks(string text, double f)
        {
            if (_lookGauge == null) return;
            // Gauge.
            _gaugeFill.Data = Arc(_tdpReady ? f : 0.001);
            _gaugeFill.Opacity = _tdpReady ? 1 : 0;
            double cx = GaugeW / 2, cy = GaugeH - 14, len = GaugeW / 2 - 30, angle = Math.PI * (1 - (_tdpReady ? f : 0));
            _gaugeNeedle.X1 = cx; _gaugeNeedle.Y1 = cy;
            _gaugeNeedle.X2 = cx + len * Math.Cos(angle); _gaugeNeedle.Y2 = cy - len * Math.Sin(angle);
            _gaugeText.Text = text;
            // Cells.
            int lit = _tdpReady ? (int)Math.Ceiling(f * 10 - 0.0001) : 0;
            for (int i = 0; i < 10; i++)
                _cells[i].Background = new SolidColorBrush(i < lit ? Color.FromArgb((byte)(120 + i * 13), _accent.R, _accent.G, _accent.B) : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            _cellsText.Text = text;
            // Stepper and slider.
            _stepperText.Text = _tdpReady ? _watts.ToString() : "–";
            if (!_sliderTimer.IsEnabled)
            {
                _sliderSyncing = true;
                if (_tdpReady && _max > _min) { _tdpSlider.Minimum = _min; _tdpSlider.Maximum = _max; _tdpSlider.Value = _watts; }
                _sliderSyncing = false;
                _sliderText.Text = text;
            }
            _tdpSlider.IsEnabled = _tdpReady;
            // Ruler.
            _ruler.Children.Clear();
            var tick = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            if (_tdpReady && _max > _min)
            {
                for (int w = _min; w <= _max; w++)
                {
                    double x = 10 + (RulerW - 20) * (w - _min) / (double)(_max - _min);
                    bool major = w % 5 == 0;
                    _ruler.Children.Add(new Line { X1 = x, X2 = x, Y1 = 22, Y2 = major ? 40 : 32, Stroke = tick, StrokeThickness = major ? 1.6 : 1 });
                    if (major)
                    {
                        var label = new TextBlock { Text = w.ToString(), FontSize = 9, Foreground = new SolidColorBrush(Subtle) };
                        Canvas.SetLeft(label, x - 6);
                        Canvas.SetTop(label, 41);
                        _ruler.Children.Add(label);
                    }
                }
                double px = 10 + (RulerW - 20) * f;
                var pointer = new Polygon { Fill = _accentBrush, Points = new PointCollection { new Point(px - 7, 4), new Point(px + 7, 4), new Point(px, 20) } };
                _ruler.Children.Add(pointer);
                var value = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = _accentBrush };
                Canvas.SetLeft(value, Math.Max(0, Math.Min(RulerW - 34, px + 10)));
                Canvas.SetTop(value, 2);
                _ruler.Children.Add(value);
            }
            foreach (var b in new[] { _gaugeMinus, _gaugePlus, _stepMinus, _stepPlus, _rulerMinus, _rulerPlus }) b.IsEnabled = _tdpReady;
        }
    }
}
