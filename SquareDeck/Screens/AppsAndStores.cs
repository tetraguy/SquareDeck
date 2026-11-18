using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Every app added to the launcher. Recently used apps first, then all of them A–Z under letter headings.
/// A opens, Y is the usual options menu, X filters by name, LB / RB jump between letters.
/// </summary>
public sealed class AllAppsScreen : GridScreen
{
    private const double TileSize = 168;
    private const int RecentCount = 6;
    private string? _focusKey;
    private string _filter = "";
    private readonly List<(string Letter, Button First)> _letters = new();

    public AllAppsScreen() : base("Apps",
        "{Select}  Open      {Details}  Find      LB RB  Letters      {Options}  Options      {Back}  Back") { }

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
        Body.Children.Clear();
        Tiles.Children.Clear();
        _letters.Clear();
        Button? focus = null;

        var all = Host.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var shown = _filter.Length == 0 ? all
            : all.Where(a => a.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

        // Header: how many, the filter in use, and the way to add more.
        var header = new DockPanel { Margin = new Thickness(4, 0, 10, 14), LastChildFill = true };
        var add = Ui.PanelButton("\uE710", "Add app");
        add.Click += (_, _) => Host.Push(new AddAppScreen());
        DockPanel.SetDock(add, Dock.Right);
        header.Children.Add(add);
        var count = Ui.Text(_filter.Length == 0
                ? $"{all.Count} app{(all.Count == 1 ? "" : "s")}"
                : $"{shown.Count} of {all.Count} apps matching \u201c{_filter}\u201d   ·   X to change, B to clear", 17, "SubtleBrush");
        count.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(count);
        Body.Children.Add(header);

        if (all.Count == 0)
        {
            var empty = Ui.Text("No apps yet. Add the programs and Store apps you use, and open them from here with the controller.", 18, "SubtleBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(4, 10, 0, 16);
            Body.Children.Add(empty);
            var row = new WrapPanel();
            Body.Children.Add(row);
            var addTile = AddTile(GlyphBox(TileSize, TileSize, "\uE710"), "Add app", TileSize, () => Host.Push(new AddAppScreen()), into: row);
            FocusLater(addTile);
            return;
        }

        void Track(Button tile, Game a)
        {
            tile.GotKeyboardFocus += (_, _) => _focusKey = a.Key;
            if (a.Key == _focusKey && focus == null) focus = tile;
        }

        // Recently used: the ones opened last, bigger, so the usual few are one press away.
        var recent = _filter.Length == 0
            ? all.Where(a => a.RecentAt > DateTime.MinValue).OrderByDescending(a => a.RecentAt).Take(RecentCount).ToList()
            : new List<Game>();
        if (recent.Count > 0)
        {
            Body.Children.Add(Heading("Recently used"));
            var row = new WrapPanel();
            Body.Children.Add(row);
            foreach (var a in recent) Track(AddTile(AppFace(a, TileSize), a.Name, TileSize, () => _ = Host.ActivateAsync(a), a, row), a);
        }

        // Everything, A–Z, under a heading per letter.
        foreach (var group in shown.GroupBy(a => Letter(a.Name)))
        {
            Body.Children.Add(Heading(group.Key));
            var row = new WrapPanel();
            Body.Children.Add(row);
            Button? first = null;
            foreach (var a in group)
            {
                var tile = AddTile(AppFace(a, TileSize), a.Name, TileSize, () => _ = Host.ActivateAsync(a), a, row);
                first ??= tile;
                Track(tile, a);
            }
            if (first != null) _letters.Add((group.Key, first));
        }

        if (shown.Count == 0)
        {
            var none = Ui.Text("No app names match.", 18, "SubtleBrush");
            none.Margin = new Thickness(4, 6, 0, 0);
            Body.Children.Add(none);
            FocusLater(add);
            return;
        }

        FocusLater(focus ?? Body.Children.OfType<WrapPanel>().SelectMany(w => w.Children.OfType<Button>()).FirstOrDefault());
    }

    private static TextBlock Heading(string text)
    {
        var heading = Ui.Text(text, 18, "FgBrush", bold: true);
        heading.Margin = new Thickness(4, 8, 0, 10);
        return heading;
    }

    private static string Letter(string name)
    {
        var c = name.TrimStart().FirstOrDefault();
        return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : "#";
    }

    /// <summary>
    /// An app's tile: its icon sharp and centered on a wash of its own colors, never drawn bigger than the
    /// icon really is. SteamGridDB artwork chosen with Change icon fills the tile instead.
    /// </summary>
    public static Border AppFace(Game a, double size)
    {
        var box = ImageBox(size, size, out var icon);
        if (a.TileBackground != null) box.Background = a.TileBackground;
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Game.Cover)) { Source = a });
        if (a.UsesIcon && a.IconFills)
        {
            icon.Stretch = Stretch.UniformToFill;
            return box;
        }
        // Store logos and large program icons get most of the tile; icons that only exist small stay small.
        double side = a.IconIsSmall ? size * 0.42 : size * 0.6;
        icon.Stretch = Stretch.Uniform;
        icon.Width = icon.Height = side;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35 };

        // A soft light from the top so the tile isn't flat.
        if (icon.Parent is Grid grid)
        {
            grid.Children.Insert(0, new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90)
            });
        }
        return box;
    }

    private async void Find()
    {
        var text = await Host.PromptAsync("Find an app", "Type part of its name.", _filter);
        if (text == null) return;
        _filter = text.Trim();
        _focusKey = null;
        Build();
    }

    /// <summary>LB / RB: to the first app of the previous or next letter.</summary>
    private void JumpLetter(int step)
    {
        if (_letters.Count == 0) return;
        var focused = Keyboard.FocusedElement as Button;
        int current = -1;
        if (focused?.Tag is Game g)
        {
            var letter = Letter(g.Name);
            current = _letters.FindIndex(l => l.Letter == letter);
            // In Recently used: LB goes nowhere, RB to the first letter.
            if (focused.Parent is WrapPanel row && Body.Children.IndexOf(row) <= 2 && _filter.Length == 0 &&
                !_letters.Any(l => l.First.Parent == row)) current = -1;
        }
        int next = Math.Clamp(current + step, 0, _letters.Count - 1);
        if (current < 0 && step < 0) return;
        _letters[next].First.Focus();
        _letters[next].First.BringIntoView();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y when Keyboard.FocusedElement is Button { Tag: Game app }:
                _ = Host.OpenOptionsAsync(app);
                return true;
            case NavAction.X:
                Find();
                return true;
            case NavAction.LB:
                JumpLetter(-1);
                return true;
            case NavAction.RB:
                JumpLetter(1);
                return true;
            case NavAction.Back when _filter.Length > 0:
                _filter = "";
                Build();
                return true;
        }
        return false;
    }
}
