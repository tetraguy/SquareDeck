using System.Text.Json.Nodes;
using System.Windows.Media;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// 9.0.21: SquareDeck's own in-game quick menu (Screens/QuickMenuWindow.cs), the other choice beside the Game Bar
/// widget for what the summon buttons, the keyboard shortcut and the Ally's button open over a game. It reads and
/// changes everything through the widget's own calls, so the two always do the same things.
/// </summary>
public partial class MainWindow
{
    private QuickMenuWindow? _quickMenu;

    /// <summary>The quick menu is on the screen.</summary>
    public bool QuickMenuOpen => _quickMenu?.IsOpen == true;

    /// <summary>Opens the quick menu over whatever is in front, or closes it when it's open.</summary>
    public void ToggleQuickMenu()
    {
        _quickMenu ??= new QuickMenuWindow(this);
        if (_quickMenu.IsOpen) _quickMenu.CloseMenu();
        else _quickMenu.OpenMenu();
        _gamepad.Reset(); // the buttons that opened (or closed) it don't count as presses
    }

    /// <summary>What the widget is told (the game, power, sound, toggles…), for the menu.</summary>
    public JsonObject QuickMenuState() => WidgetSnapshot();

    /// <summary>A change the widget could ask for (the TDP, volume, a quick tile…), asked for by the menu.</summary>
    public void QuickMenuRequest(string op, JsonObject? args = null) => WidgetRequest(op, args ?? new JsonObject());

    /// <summary>One of the actions the Ally's buttons can do (screenshot, record, keyboard, SquareDeck…).</summary>
    public void QuickMenuAction(string id) => RunAllyAction(id);

    /// <summary>A game's cover for the menu's game card.</summary>
    public ImageSource? QuickMenuCover(string key)
    {
        try
        {
            if (AllGames.FirstOrDefault(g => g.Key == key) is not { } game) return null;
            if (game.Cover is { } cover) return cover;
            string? file = game.ParkedCoverFile ?? CustomCoverPath(game) ?? (game.IsSteam ? SteamGridDbClient.FindCachedCover(game.AppId) : null);
            return file != null ? ImageLoader.FromFile(file, 256) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// While the menu is open the controller is its own: every button goes to it, and the summon buttons close it.
    /// True when the menu took the controller this time.
    /// </summary>
    private bool PollQuickMenu()
    {
        if (_quickMenu is not { IsOpen: true } menu) return false;
        if (_gamepad.PollSummon())
        {
            menu.CloseMenu();
            _gamepad.Reset();
            return true;
        }
        foreach (var action in _gamepad.Poll()) menu.OnAction(action);
        return true;
    }
}
