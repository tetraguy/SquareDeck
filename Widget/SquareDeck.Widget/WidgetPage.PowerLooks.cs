using System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using SquareDeck.Widget.Controls;

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

        /// <summary>Shows the chosen look ("dial", "bar", "mini", "number" or "chips").</summary>
        private void ApplyPowerLook(string look)
        {
            if (_lookBar == null) return;
            if (look != "bar" && look != "mini" && look != "number" && look != "chips") look = "dial";
            if (look != _powerLook) { _numberOpen = false; _chipsCustom = false; }
            _powerLook = look;
            _powerBody.Visibility = look == "dial" || (look == "number" && _numberOpen) ? Visibility.Visible : Visibility.Collapsed;
            _presetsGrid.Visibility = look == "dial" ? Visibility.Visible : Visibility.Collapsed;
            _lookBar.Visibility = look == "bar" || (look == "chips" && _chipsCustom) ? Visibility.Visible : Visibility.Collapsed;
            _lookMini.Visibility = look == "mini" ? Visibility.Visible : Visibility.Collapsed;
            _lookNumber.Visibility = look == "number" ? Visibility.Visible : Visibility.Collapsed;
            _lookChips.Visibility = look == "chips" ? Visibility.Visible : Visibility.Collapsed;
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
    }
}
