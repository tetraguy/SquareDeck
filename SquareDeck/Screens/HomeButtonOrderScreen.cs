using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.16: Settings > Home screen > Reorder the buttons. The round buttons under the games, drawn as they are on Home
/// (their own icons and colors), in a row you rearrange: move along with left / right, A picks a button up (it lifts),
/// left / right carries it along the row (LB / RB to either end), A puts it down. Y shows or hides the buttons that can
/// be hidden, X puts the usual order back. Every change shows on Home at once.
/// </summary>
public sealed class HomeButtonOrderScreen : Screen
{
    private List<string> _order = new();
    private int _at;
    private int _heldFrom = -1; // where the held button was picked up from (-1: nothing held)
    private readonly Grid _row = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _name = Ui.Text("", 26, "FgBrush");
    private readonly TextBlock _where = Ui.Text("", 16, "SubtleBrush");
    private readonly TextBlock _how = Ui.Text("", 16, "SubtleBrush");
    private readonly HintBar _hints = new("");
    private const double Size = 92, Gap = 18;

    private static readonly Dictionary<string, string> Names = new()
    {
        ["home"] = "Home", ["favorites"] = "Favorites", ["apps"] = "Apps", ["captures"] = "Captures", ["music"] = "Multimedia",
        ["friends"] = "Friends", ["news"] = "News", ["browser"] = "Web browser", ["explorer"] = "Explorer", ["stores"] = "Stores",
        ["settings"] = "Settings", ["power"] = "Power",
    };

    public HomeButtonOrderScreen()
    {
        Switch2Settings.Refresh();
        var root = new Grid { Background = new SolidColorBrush(Switch2Settings.Background) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

        var head = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(78, 26, 62, 0) };
        var glyph = Ui.Glyph("\uE8CB", 32, "FgBrush");
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 2, 16, 0);
        head.Children.Add(glyph);
        var title = Ui.Text("Reorder the buttons", 32, "FgBrush");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        root.Children.Add(head);
        root.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 0), Fill = new SolidColorBrush(Switch2Settings.Line) });

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 30) };
        var lead = Ui.Text("The buttons under the games on Home, in their order. Pick one up and move it where you want it.", 18, "SubtleBrush");
        lead.HorizontalAlignment = HorizontalAlignment.Center;
        lead.Margin = new Thickness(0, 0, 0, 40);
        body.Children.Add(lead);

        // The preview: a strip like the bottom of the home screen.
        var strip = new Border { CornerRadius = new CornerRadius(28), Padding = new Thickness(30, 46, 30, 30), HorizontalAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(1) };
        strip.Background = new SolidColorBrush(Switch2Settings.Card);
        strip.BorderBrush = new SolidColorBrush(Switch2Settings.Line);
        strip.Child = _row;
        body.Children.Add(strip);

        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.Margin = new Thickness(0, 30, 0, 0);
        body.Children.Add(_name);
        _where.HorizontalAlignment = HorizontalAlignment.Center;
        _where.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(_where);
        _how.HorizontalAlignment = HorizontalAlignment.Center;
        _how.TextAlignment = TextAlignment.Center;
        _how.Margin = new Thickness(0, 18, 0, 0);
        body.Children.Add(_how);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var foot = new Grid { Margin = new Thickness(40, 0, 40, 0) };
        foot.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Fill = new SolidColorBrush(Switch2Settings.Line) });
        _hints.HorizontalAlignment = HorizontalAlignment.Right;
        _hints.VerticalAlignment = VerticalAlignment.Center;
        _hints.Margin = new Thickness(0, 0, 26, 0);
        foot.Children.Add(_hints);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;
    }

    public override void OnShown()
    {
        _order = Host.HomeButtonOrder();
        _at = 0;
        Draw();
    }

    public override bool OnAction(NavAction action)
    {
        bool holding = _heldFrom >= 0;
        switch (action)
        {
            case NavAction.Left: Step(-1); return true;
            case NavAction.Right: Step(+1); return true;
            case NavAction.LB when holding: MoveHeld(0); return true;
            case NavAction.RB when holding: MoveHeld(_order.Count - 1); return true;
            case NavAction.Accept:
                if (holding) Drop(); else PickUp();
                return true;
            case NavAction.Y when !holding:
                ToggleShown(_order[_at]);
                return true;
            case NavAction.X when !holding:
                _order = MainWindow.HomeButtonIds.ToList();
                Save();
                UiSound.Select();
                Draw();
                return true;
            case NavAction.Back:
                if (holding) { MoveHeld(_heldFrom); _heldFrom = -1; UiSound.Back(); Save(); Draw(); return true; } // put it back where it was
                Host.Pop(this);
                return true;
        }
        return true;
    }

    private void Step(int by)
    {
        if (_heldFrom >= 0) { MoveHeld(Math.Clamp(_at + by, 0, _order.Count - 1)); return; }
        int next = Math.Clamp(_at + by, 0, _order.Count - 1);
        if (next == _at) return;
        _at = next;
        UiSound.Move();
        Draw();
    }

    private void PickUp()
    {
        _heldFrom = _at;
        UiSound.Select();
        Host.Rumble(20);
        Draw();
    }

    private void Drop()
    {
        _heldFrom = -1;
        UiSound.Select();
        Save();
        Draw();
    }

    /// <summary>Carries the held button to <paramref name="to"/>, the others making room.</summary>
    private void MoveHeld(int to)
    {
        to = Math.Clamp(to, 0, _order.Count - 1);
        if (to == _at) return;
        var id = _order[_at];
        _order.RemoveAt(_at);
        _order.Insert(to, id);
        _at = to;
        UiSound.Move();
        Save();
        Draw();
    }

    private void Save()
    {
        Host.Settings.HomeButtonOrder = _order.ToList();
        Host.Settings.Save();
        Host.RefreshHomeButtonsPublic();
    }

    private static bool CanHide(string id) => id is "music" or "friends" or "news" or "browser" or "explorer";

    private bool Shown(string id)
    {
        var s = Host.Settings;
        return id switch
        {
            "music" => s.ShowMusicButton, "friends" => s.ShowFriendsButton, "news" => s.ShowNewsButton,
            "browser" => s.ShowBrowserButton, "explorer" => s.ShowExplorerButton, _ => true
        };
    }

    private void ToggleShown(string id)
    {
        if (!CanHide(id)) { UiSound.Error(); _how.Text = (Names.GetValueOrDefault(id) ?? id) + " is always on the row."; return; }
        var s = Host.Settings;
        switch (id)
        {
            case "music": s.ShowMusicButton = !s.ShowMusicButton; break;
            case "friends": s.ShowFriendsButton = !s.ShowFriendsButton; break;
            case "news": s.ShowNewsButton = !s.ShowNewsButton; break;
            case "browser": s.ShowBrowserButton = !s.ShowBrowserButton; break;
            case "explorer": s.ShowExplorerButton = !s.ShowExplorerButton; break;
        }
        UiSound.Select();
        Save();
        Draw();
    }

    private void Draw()
    {
        _row.Children.Clear();
        _row.ColumnDefinitions.Clear();
        bool holding = _heldFrom >= 0;
        var accent = (Brush)(TryFindResource("AccentBrush") ?? Brushes.DeepSkyBlue);
        for (int i = 0; i < _order.Count; i++)
        {
            _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Size + Gap) });
            string id = _order[i];
            var real = Host.HomeButtonFor(id);
            bool shown = Shown(id), here = i == _at, held = holding && here;

            var cell = new Grid { Width = Size, Height = Size + 40, Cursor = Cursors.Hand };
            var circle = new Ellipse { Width = Size, Height = Size, VerticalAlignment = VerticalAlignment.Top, Fill = new SolidColorBrush(Switch2Settings.Background) };
            if (here) { circle.Stroke = accent; circle.StrokeThickness = held ? 5 : 3.5; }
            else { circle.Stroke = new SolidColorBrush(Switch2Settings.Line); circle.StrokeThickness = 1.5; }
            cell.Children.Add(circle);
            var icon = new TextBlock
            {
                Text = real?.Content as string ?? "", FontFamily = Ui.IconFont, FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, (Size - 40) / 2, 0, 0),
                Foreground = real?.Foreground ?? (Brush)(TryFindResource("FgBrush") ?? Brushes.White),
            };
            cell.Children.Add(icon);
            if (!shown)
            {
                // Hidden from Home: faded, with a small eye-off badge.
                circle.Opacity = icon.Opacity = 0.35;
                var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Switch2Settings.Off) };
                badge.Child = new TextBlock { Text = "\uED1A", FontFamily = Ui.IconFont, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cell.Children.Add(badge);
            }
            var number = Ui.Text((i + 1).ToString(), 14, here ? "FgBrush" : "SubtleBrush");
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Bottom;
            cell.Children.Add(number);
            if (held)
            {
                // Picked up: lifted above the row, with a shadow.
                var lift = new TranslateTransform(0, -24);
                cell.RenderTransform = lift;
                cell.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 10, Opacity = 0.5 };
                if (!Theme.ReduceMotion) lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, -24, TimeSpan.FromMilliseconds(140)));
            }
            int index = i;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                // Touch and mouse: a tap picks a button up, a tap on another spot puts it down there.
                if (_heldFrom >= 0) { MoveHeld(index); Drop(); }
                else { _at = index; PickUp(); }
            };
            Grid.SetColumn(cell, i);
            _row.Children.Add(cell);
        }

        string current = _order.Count > 0 ? _order[_at] : "";
        string name = Names.GetValueOrDefault(current) ?? current;
        _name.Text = holding ? "Moving " + name : name;
        _where.Text = $"{_at + 1} of {_order.Count}" + (Shown(current) ? "" : "  ·  hidden from Home");
        _how.Text = holding
            ? "Left and right carry it along the row; LB and RB take it to either end. A puts it down, B puts it back."
            : "A picks it up to move it." + (CanHide(current) ? " Y " + (Shown(current) ? "hides it from" : "shows it on") + " Home." : "") + " X puts the usual order back.";
        _hints.Template = holding ? "◀▶  Move it      {LB}{RB}  To either end      {Select}  Put down      {Back}  Put back"
                              : "◀▶  Choose      {Select}  Pick up      {Options}  Show / hide      {Details}  Usual order      {Back}  Done";
    }
}
