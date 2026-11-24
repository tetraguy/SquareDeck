using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using SquareDeck.Widget.Controls;

namespace SquareDeck.Widget
{
    /// <summary>
    /// The Live card: four tiles, each a number with its last half minute as a line. Select a tile to choose what it
    /// shows (processor, graphics, the APU's power use, the TDP limit, frame rate, temperatures, fan, memory, battery…).
    /// The choice is kept in the widget's own settings.
    /// </summary>
    public sealed partial class WidgetPage
    {
        private sealed class LiveStat
        {
            public string Id, Label, Name;
            public Color Color;
            public double? Max;
            public Func<JsonObject, double?> Read;
            public Func<double, string> Show;
        }

        private static Color Amber => Color.FromArgb(255, 0xFF, 0xB0, 0x3B);
        private static Color Green => Color.FromArgb(255, 0x3D, 0xDC, 0x84);
        private static Color Red => Color.FromArgb(255, 0xFF, 0x6B, 0x5B);

        private static string Compact(double v) => v >= 1000 ? (v / 1000).ToString("0.0") + "k" : v.ToString("0");

        // Made on first use: the colors it uses are set up in the other half of the class, which may come later.
        private static LiveStat[] _liveStats;
        private static LiveStat[] LiveStats => _liveStats ?? (_liveStats = new[]
        {
            new LiveStat { Id = "cpu", Label = "CPU", Name = "Processor use", Color = Teal, Max = 100, Read = s => Num(s, "cpu"), Show = v => v.ToString("0") + "%" },
            new LiveStat { Id = "gpu", Label = "GPU", Name = "Graphics use", Color = Blue, Max = 100, Read = s => Num(s, "gpu"), Show = v => v.ToString("0") + "%" },
            new LiveStat { Id = "apu", Label = "APU POWER", Name = "APU power (what the chip uses now)", Color = Amber, Max = null, Read = s => Num(s, "apu"), Show = v => v.ToString(v < 10 ? "0.0" : "0") + "W" },
            new LiveStat { Id = "tdp", Label = "TDP", Name = "TDP limit in effect", Color = Amber, Max = null, Read = s => Num(Obj(s, "tdp"), "watts"), Show = v => v.ToString("0") + "W" },
            new LiveStat { Id = "draw", Label = "DRAW", Name = "Power from the battery", Color = Red, Max = null, Read = s => Num(s, "draw"), Show = v => v.ToString(v < 10 ? "0.0" : "0") + "W" },
            new LiveStat { Id = "fps", Label = "FPS", Name = "Frames per second", Color = Green, Max = null, Read = s => Num(s, "fps"), Show = v => v.ToString("0") },
            new LiveStat { Id = "cputemp", Label = "CPU TEMP", Name = "Processor temperature", Color = Red, Max = 100, Read = s => Num(s, "cputemp"), Show = v => v.ToString("0") + "°" },
            new LiveStat { Id = "gputemp", Label = "GPU TEMP", Name = "Graphics temperature", Color = Red, Max = 100, Read = s => Num(s, "gputemp"), Show = v => v.ToString("0") + "°" },
            new LiveStat { Id = "mhz", Label = "CPU GHZ", Name = "Processor speed", Color = Teal, Max = null, Read = s => Num(s, "mhz"), Show = v => (v / 1000).ToString("0.0") },
            new LiveStat { Id = "fan", Label = "FAN", Name = "Fan speed (rpm)", Color = Violet, Max = null, Read = s => Num(s, "fan"), Show = Compact },
            new LiveStat { Id = "ram", Label = "MEMORY", Name = "Memory in use", Color = Violet, Max = 100, Read = s => Num(s, "ram"), Show = v => v.ToString("0") + "%" },
            new LiveStat { Id = "battery", Label = "BATTERY", Name = "Battery", Color = Pink, Max = 100, Read = s => Num(s, "battery"), Show = v => v.ToString("0") + "%" },
            new LiveStat { Id = "left", Label = "TIME LEFT", Name = "Battery time left", Color = Pink, Max = null, Read = s => Num(s, "left"), Show = v => ((int)v / 60) + ":" + ((int)v % 60).ToString("00") },
        });

        private static readonly string[] DefaultLive = { "cpu", "gpu", "fan", "battery" };
        private readonly string[] _live = new string[4];
        private readonly Dictionary<string, (TextBlock Value, Sparkline Line)> _stats = new Dictionary<string, (TextBlock, Sparkline)>();
        private readonly Border[] _liveBoxes = new Border[4];
        private readonly TextBlock[] _liveLabels = new TextBlock[4];
        private Grid _liveGrid;
        private JsonObject _lastState;

        private FrameworkElement BuildLive()
        {
            string saved = null;
            try { saved = ApplicationData.Current.LocalSettings.Values["live"] as string; } catch (Exception) { }
            var ids = (saved ?? string.Join(",", DefaultLive)).Split(',');
            for (int i = 0; i < 4; i++) _live[i] = i < ids.Length && LiveStats.Any(x => x.Id == ids[i]) ? ids[i] : DefaultLive[i];

            _liveGrid = Columns(4);
            for (int i = 0; i < 4; i++) BuildLiveTile(i);
            var hint = new TextBlock { Text = "Select a tile to change it", FontSize = 11, Foreground = new SolidColorBrush(Subtle) };
            return Card("", "Live", _liveGrid, hint);
        }

        private static LiveStat StatOf(string id) => LiveStats.FirstOrDefault(x => x.Id == id) ?? LiveStats[0];

        private void BuildLiveTile(int slot)
        {
            var stat = StatOf(_live[slot]);
            var tile = new StackPanel();
            var label = new TextBlock { Text = stat.Label, FontSize = 10, FontWeight = FontWeights.Bold, CharacterSpacing = 60, Foreground = new SolidColorBrush(Subtle), TextTrimming = TextTrimming.CharacterEllipsis };
            tile.Children.Add(label);
            var value = new TextBlock { Text = "--", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 2, 0, 4) };
            tile.Children.Add(value);
            var line = new Sparkline(stat.Id == "cpu" ? _accent : stat.Color, stat.Max);
            tile.Children.Add(line);
            var button = new Button
            {
                Style = DeckButton, Content = tile, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF))
            };
            ToolTipService.SetToolTip(button, stat.Name + " (select to change)");
            int at = slot;
            button.Click += (s, e) => ChooseLive(at, button);
            var box = new Border { Child = button };
            Grid.SetColumn(box, slot);
            if (_liveBoxes[slot] != null) _liveGrid.Children.Remove(_liveBoxes[slot]);
            _liveGrid.Children.Add(box);
            _liveBoxes[slot] = box;
            _liveLabels[slot] = label;
            _stats[slot.ToString()] = (value, line);
            if (_lastState != null) UpdateLive(_lastState, false);
        }

        /// <summary>Live tiles chosen by SquareDeck (a game's layout or preset, or your own saved there).</summary>
        private void ApplyLiveIds(string ids)
        {
            if (string.IsNullOrEmpty(ids) || _liveGrid == null) return;
            var parts = ids.Split(',');
            if (parts.Length < 4) return;
            bool changed = false;
            for (int i = 0; i < 4; i++)
            {
                if (!LiveStats.Any(x => x.Id == parts[i]) || _live[i] == parts[i]) continue;
                _live[i] = parts[i];
                changed = true;
                BuildLiveTile(i);
            }
            if (changed) try { ApplicationData.Current.LocalSettings.Values["live"] = string.Join(",", _live); } catch (Exception) { }
        }

        private void ChooseLive(int slot, FrameworkElement at)
        {
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
            foreach (var stat in LiveStats)
            {
                var item = new ToggleMenuFlyoutItem { Text = stat.Name, IsChecked = _live[slot] == stat.Id };
                string id = stat.Id;
                item.Click += (s, e) =>
                {
                    _live[slot] = id;
                    try { ApplicationData.Current.LocalSettings.Values["live"] = string.Join(",", _live); } catch (Exception) { }
                    _link.Send("live", "ids", string.Join(",", _live));
                    BuildLiveTile(slot);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var reset = new MenuFlyoutItem { Text = "Back to the usual four" };
            reset.Click += (s, e) =>
            {
                for (int i = 0; i < 4; i++) _live[i] = DefaultLive[i];
                try { ApplicationData.Current.LocalSettings.Values["live"] = string.Join(",", _live); } catch (Exception) { }
                _link.Send("live", "ids", string.Join(",", _live));
                for (int i = 0; i < 4; i++) BuildLiveTile(i);
            };
            menu.Items.Add(reset);
            menu.ShowAt(at);
        }

        /// <summary>Each tile's number, and a point on its line about once a second.</summary>
        private void UpdateLive(JsonObject s, bool sample)
        {
            _lastState = s;
            for (int i = 0; i < 4; i++)
            {
                (TextBlock Value, Sparkline Line) tile;
                if (!_stats.TryGetValue(i.ToString(), out tile)) continue;
                var stat = StatOf(_live[i]);
                double? v = null;
                try { v = stat.Read(s); } catch (Exception) { }
                tile.Value.Text = v == null ? "--" : stat.Show(v.Value);
                if (sample && v != null) tile.Line.Add(v.Value);
            }
        }

        /// <summary>The processor tile follows the highlight color, as before.</summary>
        private void AccentLive(Color c)
        {
            for (int i = 0; i < 4; i++)
            {
                (TextBlock Value, Sparkline Line) tile;
                if (_live[i] == "cpu" && _stats.TryGetValue(i.ToString(), out tile)) tile.Line.SetColor(c);
            }
        }
    }
}
