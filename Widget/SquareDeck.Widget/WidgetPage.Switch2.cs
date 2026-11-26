using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace SquareDeck.Widget
{
    /// <summary>
    /// Experimental "Switch 2 style widget" (SquareDeck 8.1.45): the widget dressed like SquareDeck's Switch 2 style
    /// pages, in the colors of the UI style in use (sent by SquareDeck). A plain panel in the page color, the header
    /// with a thin line under it, each card's title as a short bar and its name in regular type (no spaced capitals),
    /// quiet cards, tiles and the rail in the card color, and the highlight ring in the style's highlight color.
    /// Everything it changes is remembered, so turning it off puts the widget back as it was.
    /// </summary>
    public sealed partial class WidgetPage
    {
        private readonly List<Action> _s2Undo = new List<Action>();
        private string _s2Key;
        private Grid _headerGrid;

        /// <summary>Called with every state from SquareDeck: applies, updates or removes the Switch 2 look.</summary>
        private void ApplySwitch2(JsonObject s)
        {
            JsonObject p = _labs.Contains("ws2") ? Obj(s, "s2") : null;
            if (p == null)
            {
                if (_s2Key != null) UndoSwitch2();
                return;
            }
            string key = string.Join("|", new[] { "bg", "fg", "sub", "line", "accent", "card" }.Select(k => Str(p, k) ?? ""));
            if (key == _s2Key) return;
            if (_s2Key != null) UndoSwitch2(); // a new UI style: start from the widget's own look again
            _s2Key = key;

            Color bg, fg, sub, line, accent, card;
            if (!TryParseColor(Str(p, "bg"), out bg) || !TryParseColor(Str(p, "fg"), out fg)) { _s2Key = null; return; }
            if (!TryParseColor(Str(p, "sub"), out sub)) sub = Color.FromArgb(0x99, fg.R, fg.G, fg.B);
            if (!TryParseColor(Str(p, "line"), out line)) line = Color.FromArgb(0x40, fg.R, fg.G, fg.B);
            if (!TryParseColor(Str(p, "card"), out card)) card = Color.FromArgb(0xFF, (byte)Math.Min(255, bg.R + 18), (byte)Math.Min(255, bg.G + 18), (byte)Math.Min(255, bg.B + 18));
            bool hasAccent = TryParseColor(Str(p, "accent"), out accent);

            // The panel: the page color, no teal glow in the corner.
            var oldRoot = Root.Background;
            Root.Background = new SolidColorBrush(Color.FromArgb(0xF4, bg.R, bg.G, bg.B));
            _s2Undo.Add(() => Root.Background = oldRoot);
            foreach (var glow in Root.Children.OfType<Ellipse>().ToList())
            {
                var g = glow;
                double was = g.Opacity;
                g.Opacity = 0;
                _s2Undo.Add(() => g.Opacity = was);
            }

            // The shared brushes the cards, tiles and focus ring use.
            Swap("DeckCardBrush", Color.FromArgb(0xE6, card.R, card.G, card.B));
            Swap("DeckTileBrush", Color.FromArgb(0x40, line.R, line.G, line.B));
            Swap("DeckSubtleBrush", sub);
            if (hasAccent) Swap("DeckTealBrush", accent);

            // The header: regular type, a thin line under it, like the Switch 2's title.
            if (_headerGrid != null)
            {
                var hb = _headerGrid.BorderBrush;
                var ht = _headerGrid.BorderThickness;
                var hm = _headerGrid.Margin;
                _headerGrid.BorderBrush = new SolidColorBrush(line);
                _headerGrid.BorderThickness = new Thickness(0, 0, 0, 1);
                _headerGrid.Margin = new Thickness(10, 0, 10, 8);
                _s2Undo.Add(() => { _headerGrid.BorderBrush = hb; _headerGrid.BorderThickness = ht; _headerGrid.Margin = hm; });
            }
            if (_headerName != null)
            {
                var w = _headerName.FontWeight;
                var f = _headerName.Foreground;
                _headerName.FontWeight = FontWeights.Normal;
                _headerName.Foreground = new SolidColorBrush(fg);
                _s2Undo.Add(() => { _headerName.FontWeight = w; _headerName.Foreground = f; });
            }

            // The rail and the note line take the card color.
            if (_railBack != null)
            {
                var rb = _railBack.Background;
                _railBack.Background = new SolidColorBrush(Color.FromArgb(0xE6, card.R, card.G, card.B));
                _s2Undo.Add(() => _railBack.Background = rb);
            }
            if (_notePill != null)
            {
                var nb = _notePill.Background;
                var nr = _notePill.CornerRadius;
                _notePill.Background = new SolidColorBrush(card);
                _notePill.CornerRadius = new CornerRadius(12);
                _s2Undo.Add(() => { _notePill.Background = nb; _notePill.CornerRadius = nr; });
            }

            // Each card: rounder corners down to the Switch 2's, no outline, and its title as a bar and a name.
            if (_cardsPanel != null)
                foreach (var c in _cardsPanel.Children.OfType<Border>().ToList()) RestyleCard(c, fg, line);
        }

        private void Swap(string key, Color color)
        {
            var brush = Application.Current.Resources[key] as SolidColorBrush;
            if (brush == null) return;
            var was = brush.Color;
            brush.Color = color;
            _s2Undo.Add(() => brush.Color = was);
        }

        private void RestyleCard(Border card, Color fg, Color line)
        {
            var radius = card.CornerRadius;
            var thick = card.BorderThickness;
            card.CornerRadius = new CornerRadius(12);
            card.BorderThickness = new Thickness(0);
            _s2Undo.Add(() => { card.CornerRadius = radius; card.BorderThickness = thick; });

            var stack = card.Child as StackPanel;
            if (stack == null || stack.Children.Count == 0) return;
            var head = stack.Children[0] as Grid;
            if (head == null || head.Children.Count == 0) return;
            var name = head.Children[0] as StackPanel;
            if (name == null || name.Children.Count < 2) return;
            var glyph = name.Children[0] as TextBlock;
            var title = name.Children[1] as TextBlock;
            if (glyph == null || title == null) return;

            // "P O W E R" in spaced capitals becomes a short bar and "Power".
            var text = title.Text;
            var size = title.FontSize;
            var weight = title.FontWeight;
            var spacing = title.CharacterSpacing;
            var fore = title.Foreground;
            var glyphShown = glyph.Visibility;
            title.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
            title.FontSize = 16;
            title.FontWeight = FontWeights.Normal;
            title.CharacterSpacing = 0;
            title.Foreground = new SolidColorBrush(fg);
            glyph.Visibility = Visibility.Collapsed;
            var bar = new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(fg), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            name.Children.Insert(0, bar);
            _s2Undo.Add(() =>
            {
                name.Children.Remove(bar);
                title.Text = text; title.FontSize = size; title.FontWeight = weight; title.CharacterSpacing = spacing; title.Foreground = fore;
                glyph.Visibility = glyphShown;
            });
        }

        private void UndoSwitch2()
        {
            for (int i = _s2Undo.Count - 1; i >= 0; i--)
            {
                try { _s2Undo[i](); } catch (Exception) { }
            }
            _s2Undo.Clear();
            _s2Key = null;
        }
    }
}
