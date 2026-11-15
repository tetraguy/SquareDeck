using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>Every app added to the launcher, as tiles. A opens, Y is the usual options menu.</summary>
public sealed class AllAppsScreen : GridScreen
{
    private const double TileSize = 164;
    private string? _focusKey;

    public AllAppsScreen() : base("All apps", "{Select}  Open      {Options}  Options      {Back}  Back") { }

    public override void OnShown()
    {
        Host.LibraryChanged += Refresh;
        Build();
    }

    public override void OnResumed() => Build();

    public override void OnClosed() => Host.LibraryChanged -= Refresh;

    private void Refresh()
    {
        if (Host.IsTop(this)) Build();
    }

    private void Build()
    {
        Tiles.Children.Clear();
        Button? focus = null;
        foreach (var app in Host.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var a = app;
            var box = ImageBox(TileSize, TileSize, out var icon);
            icon.Stretch = Stretch.Uniform;
            icon.Margin = new Thickness(TileSize * 0.22);
            icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = a });
            if (a.TileBackground != null) box.Background = a.TileBackground;
            var tile = AddTile(box, a.Name, TileSize, () => _ = Host.ActivateAsync(a), a);
            tile.GotKeyboardFocus += (_, _) => _focusKey = a.Key;
            if (a.Key == _focusKey) focus = tile;
        }
        AddTile(GlyphBox(TileSize, TileSize, ""), "Add app", TileSize, () => Host.Push(new AddAppScreen()));
        FocusLater(focus ?? Tiles.Children.OfType<Button>().FirstOrDefault());
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Y && Keyboard.FocusedElement is Button { Tag: Game app })
        {
            _ = Host.OpenOptionsAsync(app);
            return true;
        }
        return false;
    }
}
