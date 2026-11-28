using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquareDeck.Screens;

/// <summary>
/// The number of stores a not-installed game is on, bottom right of its tile, with a stack icon (as Recomp Zone shows
/// how many recomps a game has). The same game from Steam, Epic and Xbox shows once, with this count (9.0.11).
/// </summary>
public static class StoreCountBadge
{
    public static Border? For(Game g)
    {
        if (g.StoreCount < 2) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = "\uE81E", FontFamily = Ui.IconFont, FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = g.StoreCount.ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8), Child = row,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x14)),
            ToolTip = $"On {g.StoreCount} stores"
        };
    }

    /// <summary>"3 stores", for the list rows.</summary>
    public static string? Words(Game g) => g.StoreCount < 2 ? null : $"{g.StoreCount} stores";
}
