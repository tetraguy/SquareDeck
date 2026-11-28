using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The Y menu for a game or app, the way the Switch 2 shows a game's options: a large card in the UI style's colors,
/// the game's cover small at the top left with its name and a line about it, a thin line under that; then the groups
/// of options down the left (the one shown in the highlight color with a bar beside it) and that group's options on
/// the right as flat rows on thin lines, with the rimmed highlight; B Back and A OK at the bottom right. Moving down
/// the left shows each group's options; right (or A) goes into them, left comes back. Returns the chosen option's index.
/// </summary>
public sealed class GameOptionsScreen : Screen
{
    public sealed record Option(string Group, string Label, string Glyph, bool Danger = false);

    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyList<Option> _options;
    private readonly List<string> _groups;
    private readonly StackPanel _nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 6, 14, 16) };
    private readonly ScrollViewer _rowsScroll;
    private readonly List<Button> _navButtons = new();
    private string _group = "";
    private int _result = -1;

    public override bool IsDialog => true;

    public Task<int> Result => _done.Task;

    public GameOptionsScreen(Game game, IReadOnlyList<Option> options, string? subtitle)
    {
        _options = options;
        _groups = options.Select(o => o.Group).Distinct().ToList();
        Switch2Settings.ApplyColors(this);

        // ---- the top: the cover, the name and a line about it, then a thin line
        var head = new Grid { Margin = new Thickness(40, 26, 40, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(GameCard.Round(6)), ClipToBounds = true, Margin = new Thickness(0, 0, 18, 0) };
        if (game.Cover != null) cover.Background = new ImageBrush(game.Cover) { Stretch = Stretch.UniformToFill };
        else { cover.SetResourceReference(Border.BackgroundProperty, "S2Line"); cover.Child = Ui.Glyph("\uE7FC", 30, "SubtleBrush"); }
        cover.Clip = new RectangleGeometry(new Rect(0, 0, 76, 76), GameCard.Round(6), GameCard.Round(6));
        head.Children.Add(cover);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(game.Name, 26, "FgBrush");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        titles.Children.Add(name);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = Ui.Text(subtitle, 16, "SubtleBrush");
            sub.Margin = new Thickness(0, 4, 0, 0);
            titles.Children.Add(sub);
        }
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        // ---- the groups on the left, the group's options on the right
        foreach (var g in _groups)
        {
            string group = g;
            var item = Switch2Pages.NavItem(group, null, false, () => EnterGroup(group));
            item.GotKeyboardFocus += (_, _) => ShowGroup(group);
            item.MouseEnter += (_, _) => ShowGroup(group);
            item.DataContext = group;
            _navButtons.Add(item);
            _nav.Children.Add(item);
        }
        _rowsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _rows
        };
        var panes = Switch2Pages.Shell(_nav, _rowsScroll, navWidth: 330);
        panes.Margin = new Thickness(26, 10, 26, 0);

        // ---- the bottom: a thin line, then B Back and A OK on the right
        var foot = new Grid { Margin = new Thickness(26, 0, 26, 0) };
        foot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var footLine = new Rectangle { Height = 1 };
        footLine.SetResourceReference(Shape.FillProperty, "S2Line");
        foot.Children.Add(footLine);
        var hints = new HintBar("{Back}  Back      {Select}  OK") { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 14, 18) };
        Grid.SetRow(hints, 1);
        foot.Children.Add(hints);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(head);
        var headLine = new Rectangle { Height = 1, Margin = new Thickness(26, 20, 26, 0) };
        headLine.SetResourceReference(Shape.FillProperty, "S2Line");
        Grid.SetRow(headLine, 1);
        layout.Children.Add(headLine);
        Grid.SetRow(panes, 2);
        layout.Children.Add(panes);
        Grid.SetRow(foot, 3);
        layout.Children.Add(foot);

        var box = new Border
        {
            MaxWidth = 1240, MaxHeight = 780, CornerRadius = new CornerRadius(18), Child = layout, Margin = new Thickness(70, 46, 70, 46),
            Background = new SolidColorBrush(Switch2Settings.Card),
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 44, ShadowDepth = 8, Opacity = 0.5 },
        };
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == backdrop) Host.Pop(this); };
        backdrop.Children.Add(box);
        Content = backdrop;

        if (_groups.Count > 0) ShowGroup(_groups[0]);
    }

    /// <summary>Shows a group's options on the right, and marks it on the left.</summary>
    private void ShowGroup(string group)
    {
        if (group == _group) return;
        _group = group;
        foreach (var b in _navButtons) b.Tag = Equals(b.DataContext, group) ? "selected" : null;
        _rows.Children.Clear();
        for (int i = 0; i < _options.Count; i++)
        {
            var o = _options[i];
            if (o.Group != group) continue;
            int index = i;
            var row = Switch2Pages.SettingRow(o.Label, null, () => Choose(index));
            row.MinHeight = 62;
            if (o.Danger) row.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E));
            _rows.Children.Add(row);
        }
        _rowsScroll.ScrollToTop();
    }

    /// <summary>Into a group's options (A or right on the group): the first one is highlighted.</summary>
    private void EnterGroup(string group)
    {
        ShowGroup(group);
        _rows.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    /// <summary>The highlight moved: the move sound and a light rumble (8.5.85), as on the home row.</summary>
    private void Moved()
    {
        UiSound.Move();
        Host.Rumble();
    }

    public override bool OnAction(NavAction action)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        bool inNav = focused != null && _navButtons.Any(b => ReferenceEquals(b, focused));
        bool inRows = focused is Button row && _rows.Children.Contains(row);
        if (action == NavAction.Right && inNav) { EnterGroup(_group); Moved(); return true; }
        if ((action == NavAction.Left || action == NavAction.Back) && inRows)
        {
            // Back to the groups, on the one being shown (B from the options goes back there first, then closes).
            _navButtons.FirstOrDefault(b => Equals(b.DataContext, _group))?.Focus();
            Moved();
            return true;
        }
        if (inRows && action is NavAction.Up or NavAction.Down)
        {
            var rows = _rows.Children.OfType<Button>().ToList();
            int at = rows.IndexOf((Button)focused!);
            int next = Math.Clamp(at + (action == NavAction.Down ? 1 : -1), 0, rows.Count - 1);
            if (next != at) { rows[next].Focus(); rows[next].BringIntoView(); Moved(); }
            return true;
        }
        if (inNav && action is NavAction.Up or NavAction.Down)
        {
            int at = _navButtons.IndexOf((Button)focused!);
            int next = Math.Clamp(at + (action == NavAction.Down ? 1 : -1), 0, _navButtons.Count - 1);
            if (next != at) { _navButtons[next].Focus(); Moved(); }
            return true;
        }
        return false;
    }

    private void Choose(int index)
    {
        _result = index;
        Host.Pop(this);
    }

    public override void OnClosed() => _done.TrySetResult(_result);

    public override void FocusDefault() => _navButtons.FirstOrDefault()?.Focus();
}
