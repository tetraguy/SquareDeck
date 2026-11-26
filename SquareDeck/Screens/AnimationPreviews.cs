using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Live previews for the animation choices in Settings > Home screen > Animations: home buttons, opening pages,
/// the selected game and the punch-in. Each choice in the picker is a small scene; while a choice is highlighted
/// (or under the mouse) it plays its animation on a loop, using the very same animation code as Home, with that
/// choice swapped in just for the preview.
/// </summary>
public static class AnimationPreviews
{
    public const string Buttons = "buttons", Pages = "pages", Tiles = "tiles", Punch = "punch";

    private static readonly ConditionalWeakTable<FrameworkElement, DispatcherTimer> Timers = new();
    private static readonly Color[] Colors = { Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xE8, 0x45, 0x3C), Color.FromRgb(0x2E, 0xB8, 0x72), Color.FromRgb(0xF2, 0xA1, 0x00) };

    /// <summary>Runs <paramref name="action"/> with one animation choice in place of the one in use, then puts it back.</summary>
    private static void With(string kind, string id, Action action)
    {
        bool on = Motion.ButtonAnimations, punch = Motion.PunchIn;
        string button = Motion.ButtonStyle, page = Motion.PageStyle, tile = Motion.TileStyle;
        try
        {
            switch (kind)
            {
                case Buttons: Motion.ButtonAnimations = id != "off"; if (id != "off") Motion.ButtonStyle = id; break;
                case Pages: Motion.PageStyle = id; break;
                case Tiles: Motion.TileStyle = id; break;
                case Punch: Motion.PunchIn = id == "on"; break;
            }
            action();
        }
        finally
        {
            Motion.ButtonAnimations = on;
            Motion.PunchIn = punch;
            Motion.ButtonStyle = button;
            Motion.PageStyle = page;
            Motion.TileStyle = tile;
        }
    }

    // ---------------------------------------------------------------- the scenes

    /// <summary>The scene for a choice (still until it plays).</summary>
    public static FrameworkElement Draw(string kind, string id) => kind switch
    {
        Buttons => ButtonRow(),
        Pages => PageScene(),
        _ => TileRow(kind == Tiles),
    };

    /// <summary>Four round home buttons with icons.</summary>
    private static FrameworkElement ButtonRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var glyph in new[] { "\uE80F", "\uE8D6", "\uE716", "\uE713" })
        {
            var circle = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(10, 0, 10, 0), Child = Ui.Glyph(glyph, 20) };
            circle.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            row.Children.Add(circle);
        }
        return row;
    }

    /// <summary>A tiny screen: a top bar, and a page card that opens over it.</summary>
    private static FrameworkElement PageScene()
    {
        var grid = new Grid();
        var bar = new Border { Height = 10, Width = 120, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 14, 0, 0), Opacity = 0.5 };
        bar.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(bar);
        var page = new Border { Margin = new Thickness(26, 34, 26, 14), CornerRadius = new CornerRadius(10) };
        page.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        var lines = new StackPanel { Margin = new Thickness(14, 12, 14, 0) };
        var title = new Border { Height = 12, Width = 110, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
        title.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        lines.Children.Add(title);
        for (int i = 0; i < 3; i++)
        {
            var line = new Border { Height = 8, Width = 190 - i * 34, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.6 };
            line.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            lines.Children.Add(line);
        }
        page.Child = lines;
        page.Tag = "page";
        grid.Children.Add(page);
        return grid;
    }

    /// <summary>A row of game tiles, each with a highlight ring (shown on the selected one).</summary>
    private static FrameworkElement TileRow(bool rings)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            var c = Colors[i];
            var tile = new Grid { Width = 58, Height = 58, Margin = new Thickness(7, 0, 7, 0) };
            tile.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(c, Color.FromRgb((byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2)), 60)
            });
            if (rings)
            {
                var ring = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(10), Margin = new Thickness(-4), Visibility = Visibility.Hidden };
                ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                tile.Children.Add(ring);
            }
            row.Children.Add(tile);
        }
        return row;
    }

    // ---------------------------------------------------------------- playing

    /// <summary>Starts or stops a choice's animation on its scene.</summary>
    public static void Play(string kind, string id, FrameworkElement scene, bool on)
    {
        if (Timers.TryGetValue(scene, out var running))
        {
            running.Stop();
            Timers.Remove(scene);
        }
        if (!on) { Settle(kind, id, scene); return; }
        if (Theme.ReduceMotion) return;
        int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(kind == Pages ? 1500 : 1250) };
        void Tick()
        {
            switch (kind)
            {
                case Buttons:
                {
                    var buttons = ((Panel)scene).Children.OfType<UIElement>().ToList();
                    int now = step % buttons.Count, before = (step + buttons.Count - 1) % buttons.Count;
                    With(kind, id, () =>
                    {
                        if (step > 0) Motion.Highlight(buttons[before], false);
                        Motion.Highlight(buttons[now], true);
                    });
                    break;
                }
                case Pages:
                {
                    if (((Grid)scene).Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "page")) is not { } page) break;
                    page.BeginAnimation(UIElement.OpacityProperty, null);
                    page.Opacity = 0;
                    // A beat with the page gone, then it opens.
                    var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                    later.Tick += (_, _) =>
                    {
                        later.Stop();
                        page.Opacity = 1;
                        With(kind, id, () => Motion.OpenPage(page, dialog: false));
                    };
                    later.Start();
                    break;
                }
                case Tiles:
                {
                    var tiles = ((Panel)scene).Children.OfType<Grid>().ToList();
                    int now = step % tiles.Count, before = (step + tiles.Count - 1) % tiles.Count;
                    With(kind, id, () =>
                    {
                        if (step > 0)
                        {
                            Motion.GrowTile(tiles[before], false);
                            if (Ring(tiles[before]) is { } oldRing) { Motion.StopPulse(oldRing); oldRing.Visibility = Visibility.Hidden; }
                        }
                        Motion.GrowTile(tiles[now], true);
                        if (Ring(tiles[now]) is { } ring) { ring.Visibility = Visibility.Visible; Motion.StartPulse(ring); }
                    });
                    break;
                }
                default: // punch-in: the second tile is highlighted and "opened" over and over
                {
                    var tiles = ((Panel)scene).Children.OfType<Grid>().ToList();
                    var target = tiles[1];
                    if (step == 0 && !target.Children.OfType<Border>().Any(b => Equals(b.Tag, "ring"))) target.Children.Add(new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(10), Margin = new Thickness(-4), BorderBrush = Brushes.White, Tag = "ring" });
                    With(kind, id, () => _ = Motion.PunchAsync(target));
                    break;
                }
            }
            step++;
        }
        timer.Tick += (_, _) => Tick();
        Timers.AddOrUpdate(scene, timer);
        Tick();
        timer.Start();
    }

    private static Border? Ring(Grid tile) => tile.Children.OfType<Border>().Skip(1).FirstOrDefault();

    /// <summary>Back to still: nothing highlighted, everything at rest.</summary>
    private static void Settle(string kind, string id, FrameworkElement scene)
    {
        switch (kind)
        {
            case Buttons:
                With(kind, id, () => { foreach (var b in ((Panel)scene).Children.OfType<UIElement>()) Motion.Highlight(b, false); });
                break;
            case Tiles:
                With(kind, id, () =>
                {
                    foreach (var t in ((Panel)scene).Children.OfType<Grid>())
                    {
                        Motion.GrowTile(t, false);
                        if (Ring(t) is { } ring) { Motion.StopPulse(ring); ring.Visibility = Visibility.Hidden; }
                    }
                });
                break;
            case Pages:
                if (((Grid)scene).Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "page")) is { } page)
                {
                    page.BeginAnimation(UIElement.OpacityProperty, null);
                    page.Opacity = 1;
                }
                break;
        }
    }
}
