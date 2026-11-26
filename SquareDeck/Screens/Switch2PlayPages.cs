using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Experimental "Switch 2 style play pages": My Gamer Passport, the Backlog Boss, the Mood picker, the Friends
/// leaderboard and Free games, rebuilt the way the Switch 2 style Settings and Library are (sections down the left
/// with a blue bar, thin lines, bar-and-name headings, flat rows and rimmed tiles) and in the UI style's colors.
/// Each opens from here so the old pages stay as they were with the experiment off.
/// </summary>
public static class PlayPages
{
    public static bool On => Labs.On("s2play");

    public static Screen Passport() => On ? new S2PassportScreen() : new PassportScreen();
    public static Screen Boss() => On ? new S2BossScreen() : new BossScreen();
    public static Screen Mood() => On ? new S2MoodScreen() : new MoodScreen();
    public static Screen Leaderboard() => On ? new S2LeaderboardScreen() : new LeaderboardScreen();
    public static Screen FreeGames() => On ? new S2FreeGamesScreen() : new FreeGamesScreen();
}

/// <summary>
/// The frame the five pages share: sections on the left, the page on the right, LT / RT to change section, and the
/// pieces the pages are built from (rows, tiles, cards, stat tiles, bars and charts) in the Switch 2 manner.
/// </summary>
public abstract class S2PlayScreen : Screen
{
    protected readonly StackPanel Nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    protected readonly StackPanel Page = new() { Margin = new Thickness(0, 0, 18, 30) };
    protected readonly StackPanel UnderNav = new() { Margin = new Thickness(40, 0, 18, 14) };
    protected readonly ScrollViewer Scroll;
    protected readonly CancellationTokenSource Cts = new();
    protected string Section;

    protected enum FocusTo { Nav, Content, Keep }

    protected S2PlayScreen(string title, string glyph, string hints, string first)
    {
        Section = first;
        Scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = Page
        };
        SetPage(title, new Grid(), hints, wide: true);
        ReplaceBody(Switch2Pages.Shell(Nav, Scroll, UnderNav));
        UseSwitch2Chrome(title, glyph);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (Host?.Settings.RumbleInSettings == true && e.NewFocus is Button && e.OldFocus is Button && !ReferenceEquals(e.NewFocus, e.OldFocus))
                Host.Rumble(16);
            if (e.NewFocus is FrameworkElement f && Page.IsAncestorOf(f)) f.BringIntoView(new Rect(0, -40, f.ActualWidth, f.ActualHeight + 80));
        }));
    }

    /// <summary>The sections, in order: an id, a name and a count; an id of "-" draws a line.</summary>
    protected abstract IEnumerable<(string Id, string Name, string? Count)> Sections();

    /// <summary>Fills <see cref="Page"/> for <see cref="Section"/>.</summary>
    protected abstract void BuildPage();

    /// <summary>Fills the space under the sections (a summary).</summary>
    protected virtual void BuildUnderNav() { }

    /// <summary>X on the page (Sort, Filter…). By default, a game's own page.</summary>
    protected virtual bool OnX()
    {
        if (Keyboard.FocusedElement is FrameworkElement { Tag: Game g }) { Host.Push(new DetailsScreen(g)); return true; }
        return false;
    }

    /// <summary>Y on the page (Refresh, Pick again…).</summary>
    protected virtual bool OnY() => false;

    public override void OnShown() => Rebuild(FocusTo.Nav);
    public override void OnClosed() => Cts.Cancel();

    public override void OnResumed()
    {
        RefreshSwitch2Colors();
        Rebuild(FocusTo.Keep);
    }

    public override void FocusDefault() => (SelectedNav() ?? FirstButton(Page))?.Focus();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.LT or NavAction.RT:
            {
                var ids = Sections().Where(s => s.Id != "-").Select(s => s.Id).ToList();
                if (ids.Count < 2) return true;
                int i = Math.Max(0, ids.IndexOf(Section));
                Section = ids[(i + (action == NavAction.RT ? 1 : -1) + ids.Count) % ids.Count];
                UiSound.Page();
                Rebuild(Nav.IsKeyboardFocusWithin ? FocusTo.Nav : FocusTo.Content);
                return true;
            }
            case NavAction.Left when Page.IsKeyboardFocusWithin && Keyboard.FocusedElement is FrameworkElement f && IsLeftmost(f):
                SelectedNav()?.Focus();
                return true;
            case NavAction.Right when Nav.IsKeyboardFocusWithin:
                if (FirstButton(Page) is { } first) { first.Focus(); return true; }
                return false;
            case NavAction.X: return OnX();
            case NavAction.Y: return OnY();
        }
        return false;
    }

    /// <summary>Is this the first thing on its line, so Left should go back to the sections?</summary>
    private bool IsLeftmost(FrameworkElement f)
    {
        try
        {
            var me = f.TranslatePoint(new Point(0, f.ActualHeight / 2), Page);
            return !Descendants<Button>(Page).Any(b => !ReferenceEquals(b, f) && b.IsVisible && b.Focusable &&
                b.TranslatePoint(new Point(0, 0), Page) is var p && p.X + b.ActualWidth <= me.X + 4 && Math.Abs(p.Y + b.ActualHeight / 2 - me.Y) < f.ActualHeight / 2);
        }
        catch (Exception) { return true; }
    }

    protected void Show(string section, FocusTo to = FocusTo.Content)
    {
        Section = section;
        Rebuild(to);
    }

    /// <summary>Builds the sections and the page again, and puts the highlight back where it was (or where asked).</summary>
    protected void Rebuild(FocusTo to = FocusTo.Keep)
    {
        string? uid = (Keyboard.FocusedElement as UIElement)?.Uid;
        bool inNav = Nav.IsKeyboardFocusWithin;
        Nav.Children.Clear();
        foreach (var (id, name, count) in Sections())
        {
            if (id == "-") { Nav.Children.Add(Switch2Pages.NavLine()); continue; }
            var which = id;
            var item = Switch2Pages.NavItem(name, count, id == Section, () => Show(which, FocusTo.Content));
            item.Uid = "nav:" + id;
            Nav.Children.Add(item);
        }
        UnderNav.Children.Clear();
        BuildUnderNav();
        Page.Children.Clear();
        BuildPage();
        UIElement? target = to switch
        {
            FocusTo.Nav => SelectedNav(),
            FocusTo.Content => FirstButton(Page) ?? SelectedNav(),
            _ => (uid != null && uid.Length > 0 ? Descendants<Button>(this).FirstOrDefault(b => b.Uid == uid) : null)
                 ?? (inNav ? SelectedNav() : FirstButton(Page)) ?? SelectedNav(),
        };
        if (to != FocusTo.Keep) Scroll.ScrollToTop();
        FocusLater(target);
    }

    protected Button? SelectedNav() => Nav.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "selected");

    protected static Button? FirstButton(DependencyObject root) =>
        Descendants<Button>(root).FirstOrDefault(b => b.Focusable && b.IsEnabled && b.Visibility == Visibility.Visible);

    protected static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    // ---------------------------------------------------------------- pieces

    protected static Brush Res(string key) => (Brush)Application.Current.TryFindResource(key) ?? Brushes.Gray;

    protected static Brush OffBrush => new SolidColorBrush(Switch2Settings.Off);

    protected void Title(string name, string? count = null) => Page.Children.Add(Switch2Pages.PageTitle(name, count));

    protected void Header(string text) => Page.Children.Add(Switch2Settings.Header(text));

    protected void Note(string text, Panel? into = null)
    {
        var t = Ui.Text(text, 16, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(14, 6, 14, 10);
        (into ?? Page).Children.Add(t);
    }

    /// <summary>A small rounded badge: in the accent color, or quiet.</summary>
    protected static Border Pill(string text, bool accent = true)
    {
        var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold };
        if (accent) { pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); t.Foreground = Brushes.White; }
        else { pill.Background = OffBrush; t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush"); }
        pill.Child = t;
        return pill;
    }

    /// <summary>A soft card in the card color with a thin line round it.</summary>
    protected static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(20, 16, 20, 16), Margin = new Thickness(6, 6, 6, 8), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "S2Card");
        card.SetResourceReference(Border.BorderBrushProperty, "S2Line");
        return card;
    }

    /// <summary>A line of stat tiles: a big number, and what it is with a small icon.</summary>
    protected void Stats(params (string Value, string Label, string Glyph)[] stats)
    {
        var row = new UniformGrid { Rows = 1, Columns = stats.Length, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (value, label, glyph) in stats)
        {
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 15, "AccentBrush");
            g.Margin = new Thickness(0, 0, 8, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(g);
            var l = Ui.Text(label, 14, "SubtleBrush");
            l.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(l);
            stack.Children.Add(head);
            var v = Ui.Text(value, 28, "FgBrush");
            v.Margin = new Thickness(0, 4, 0, 0);
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(v);
            row.Children.Add(Card(stack, new Thickness(18, 14, 18, 14)));
        }
        Page.Children.Add(row);
    }

    /// <summary>A slim bar: how much of something is done.</summary>
    protected static FrameworkElement Bar(double fraction, double height = 8, double width = double.NaN, Brush? fill = null)
    {
        var g = new Grid { Height = height, Width = width, Margin = new Thickness(0, 6, 0, 2) };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = OffBrush });
        var bar = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        if (fill != null) bar.Background = fill; else bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        fraction = Math.Clamp(fraction, 0, 1);
        g.SizeChanged += (_, e) => bar.Width = Math.Max(fraction > 0 ? height : 0, e.NewSize.Width * fraction);
        g.Children.Add(bar);
        return g;
    }

    /// <summary>A rounded square picture (a cover), or an icon in its place.</summary>
    protected static FrameworkElement Picture(double w, double h, ImageSource? source, string? url = null, string glyph = "\uE7FC", double corner = 10)
    {
        var box = new Grid { Width = w, Height = h };
        var back = new Border { CornerRadius = new CornerRadius(corner), Background = OffBrush };
        box.Children.Add(back);
        var icon = Ui.Glyph(glyph, Math.Min(w, h) * 0.38, "SubtleBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        box.Children.Add(icon);
        var img = new Image { Stretch = Stretch.UniformToFill, Source = source };
        box.Children.Add(img);
        box.Clip = new RectangleGeometry(new Rect(0, 0, w, h), corner, corner);
        if (source != null) icon.Visibility = Visibility.Collapsed;
        else if (!string.IsNullOrEmpty(url)) _ = LoadAsync(img, icon, url, (int)Math.Max(w, 120) * 2);
        return box;
    }

    private static async Task LoadAsync(Image img, UIElement icon, string url, int width)
    {
        try
        {
            var src = await ImageLoader.FromUrlAsync(url, width);
            if (src == null) return;
            img.Source = src;
            icon.Visibility = Visibility.Collapsed;
            img.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        catch (Exception) { }
    }

    /// <summary>A flat row on a thin line: a picture, the name with a line under it, and a value in blue on the right.</summary>
    protected Button Row(string uid, string name, string? sub, string? value, Action? click, ImageSource? pic = null, string? url = null,
                         string? glyph = null, object? tag = null, IEnumerable<UIElement>? badges = null, double? progress = null, Panel? into = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (pic != null || url != null || glyph != null)
        {
            var p = Picture(52, 52, pic, url, glyph ?? "\uE7FC");
            if (p is Grid pg) { pg.Margin = new Thickness(0, 0, 18, 0); pg.VerticalAlignment = VerticalAlignment.Center; }
            grid.Children.Add(p);
        }
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var b in badges ?? Enumerable.Empty<UIElement>()) line.Children.Add(b);
        words.Children.Add(line);
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Ui.Text(sub, 15, "SubtleBrush");
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(s);
        }
        if (progress is double pr) words.Children.Add(Bar(pr, 6, 260));
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        if (!string.IsNullOrEmpty(value))
        {
            var v = Ui.Text(value, 19, "AccentBrush");
            v.VerticalAlignment = VerticalAlignment.Center;
            v.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(v, 2);
            grid.Children.Add(v);
        }
        var button = new Button { Style = Switch2Settings.RowStyle, Content = grid, Uid = uid, Tag = tag, MinHeight = 76 };
        if (click != null) button.Click += (_, _) => click();
        (into ?? Page).Children.Add(button);
        return button;
    }

    /// <summary>A settings-style row with a value (an option to change).</summary>
    protected Button Option(string uid, string name, string value, Action click)
    {
        var b = Switch2Pages.SettingRow(name, value, click);
        b.Uid = uid;
        return b;
    }

    /// <summary>A tile: a picture with a caption and a gray line under it, the rimmed card when highlighted.</summary>
    protected Button Tile(string uid, double w, double h, ImageSource? pic, string? url, string caption, string? sub, Action click,
                          object? tag = null, IEnumerable<UIElement>? badges = null, string glyph = "\uE7FC", double? progress = null)
    {
        var stack = new StackPanel { Width = w };
        var picture = new Grid();
        picture.Children.Add(Picture(w, h, pic, url, glyph, 12));
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
        foreach (var b in badges ?? Enumerable.Empty<UIElement>()) row.Children.Add(b);
        picture.Children.Add(row);
        stack.Children.Add(picture);
        if (progress is double pr) stack.Children.Add(Bar(pr, 6));
        var c = new TextBlock { Text = caption, FontSize = 17, Margin = new Thickness(2, 8, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        c.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        stack.Children.Add(c);
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Ui.Text(sub, 14, "SubtleBrush");
            s.Margin = new Thickness(2, 2, 2, 0);
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(s);
        }
        var tile = new Button { Style = Switch2Pages.TileStyle, Content = stack, Uid = uid, Tag = tag, Margin = new Thickness(0, 0, 8, 10) };
        tile.Click += (_, _) => click();
        return tile;
    }

    protected WrapPanel Tiles()
    {
        var wrap = new WrapPanel { Margin = new Thickness(6, 4, 0, 4) };
        Page.Children.Add(wrap);
        return wrap;
    }

    /// <summary>A small bar chart in a card: one bar per value, with labels under the first and last.</summary>
    protected void Chart(string title, IReadOnlyList<(string Label, double Value)> points, Func<double, string> format, double height = 150)
    {
        var stack = new StackPanel();
        var head = new Grid();
        head.Children.Add(Ui.Text(title, 17, "FgBrush"));
        if (points.Count > 0)
        {
            var last = Ui.Text(format(points[^1].Value), 17, "AccentBrush");
            last.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(last);
        }
        stack.Children.Add(head);
        if (points.Count == 0) { stack.Children.Add(Ui.Text("Nothing yet: it fills in day by day.", 15, "SubtleBrush")); Page.Children.Add(Card(stack)); return; }
        double max = Math.Max(0.0001, points.Max(p => p.Value));
        var bars = new UniformGrid { Rows = 1, Columns = points.Count, Height = height, Margin = new Thickness(0, 12, 0, 6) };
        for (int i = 0; i < points.Count; i++)
        {
            var (label, value) = points[i];
            var cell = new Grid { Margin = new Thickness(points.Count > 40 ? 0.5 : 2, 0, points.Count > 40 ? 0.5 : 2, 0), ToolTip = label + ": " + format(value), Background = Brushes.Transparent };
            var bar = new Border { CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(2, height * value / max), Opacity = i == points.Count - 1 ? 1 : 0.65 };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var grow = new ScaleTransform(1, 0);
            bar.RenderTransformOrigin = new Point(0.5, 1);
            bar.RenderTransform = grow;
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(Math.Min(i * 12, 400)), EasingFunction = new CubicEase() });
            cell.Children.Add(bar);
            bars.Children.Add(cell);
        }
        stack.Children.Add(bars);
        var labels = new Grid();
        labels.Children.Add(Ui.Text(points[0].Label, 13, "SubtleBrush"));
        var end = Ui.Text(points[^1].Label, 13, "SubtleBrush");
        end.HorizontalAlignment = HorizontalAlignment.Right;
        labels.Children.Add(end);
        stack.Children.Add(labels);
        Page.Children.Add(Card(stack));
    }

    /// <summary>A big card across the page: a picture behind (dimmed), with words and buttons over it.</summary>
    protected Button Feature(string uid, ImageSource? pic, string? url, string kicker, string title, string line, Action click, object? tag = null, double height = 220, UIElement? extra = null)
    {
        var grid = new Grid { Height = height };
        var back = Picture(640, height, pic, url, "\uE7FC", 0);
        if (back is Grid bg) { bg.Width = double.NaN; bg.Height = double.NaN; bg.Clip = null; }
        grid.Children.Add(back);
        var shade = new Border { CornerRadius = new CornerRadius(12) };
        var bgc = Switch2Settings.Background;
        shade.Background = new LinearGradientBrush(Color.FromArgb(0xF2, bgc.R, bgc.G, bgc.B), Color.FromArgb(0x40, bgc.R, bgc.G, bgc.B), 0);
        grid.Children.Add(shade);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 26, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var k = Ui.Text(kicker, 14, "AccentBrush");
        words.Children.Add(k);
        var t = Ui.Text(title, 30, "FgBrush");
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Margin = new Thickness(0, 4, 0, 4);
        words.Children.Add(t);
        var l = Ui.Text(line, 16, "SubtleBrush");
        l.TextWrapping = TextWrapping.Wrap;
        l.MaxHeight = 70;
        words.Children.Add(l);
        if (extra != null) words.Children.Add(extra);
        grid.Children.Add(words);
        grid.Clip = null;
        var holder = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = grid };
        holder.Loaded += (_, _) => holder.Clip = new RectangleGeometry(new Rect(0, 0, holder.ActualWidth, holder.ActualHeight), 12, 12);
        holder.SizeChanged += (_, e) => holder.Clip = new RectangleGeometry(new Rect(e.NewSize), 12, 12);
        var tile = new Button { Style = Switch2Pages.TileStyle, Content = holder, Uid = uid, Tag = tag, Margin = new Thickness(0, 4, 0, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        tile.Click += (_, _) => click();
        Page.Children.Add(tile);
        return tile;
    }

    /// <summary>"3 h 20 min" style hours.</summary>
    protected static string H(double hours) => Labs.Hours(hours);
}

// ====================================================================================================== passport

/// <summary>My Gamer Passport, Switch 2 style: your passport page, the stamps closest to earn, then every kind of stamp.</summary>
public sealed class S2PassportScreen : S2PlayScreen
{
    private List<Labs.StampPlus> _stamps = new();
    private HashSet<string> _new = new();

    public S2PassportScreen() : base("My Gamer Passport", "\uE8D4", "{Select}  Open      LT / RT  Section      {Back}  Back", "id") { }

    public override void OnShown()
    {
        Load();
        base.OnShown();
    }

    private void Load()
    {
        _stamps = Labs.PassportPlus(Host.AllGames, Host.Playtime, Host.Achievements, Host.Library);
        // Stamps earned since the last visit get a "New" badge and stamp themselves in.
        var key = (Labs.StampPlus s) => s.Group + "/" + s.Name;
        var before = Labs.Get("passport.earned").Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var now = _stamps.Where(s => s.Earned).Select(key).ToHashSet();
        _new = before.Count == 0 ? new() : now.Where(k => !before.Contains(k)).ToHashSet();
        Labs.Put("passport.earned", string.Join("|", now));
    }

    private static readonly string[] Order = { "Genres", "Decades", "Stores", "Firsts", "Moments", "Hours", "Finisher", "Achievements", "Streaks", "Variety", "Collector" };

    protected override IEnumerable<(string Id, string Name, string? Count)> Sections()
    {
        yield return ("id", "My passport", null);
        yield return ("next", "Next stamps", null);
        if (_new.Count > 0) yield return ("new", "New stamps", _new.Count.ToString());
        yield return ("-", "", null);
        foreach (var g in Order)
        {
            var these = _stamps.Where(s => s.Group == g).ToList();
            if (these.Count > 0) yield return (g, g, $"{these.Count(s => s.Earned)}/{these.Count}");
        }
    }

    protected override void BuildUnderNav()
    {
        int earned = _stamps.Count(s => s.Earned);
        var (rank, next) = Labs.PassportRank(earned);
        UnderNav.Children.Add(Ui.Text(rank, 18, "FgBrush"));
        UnderNav.Children.Add(Bar(next == 0 ? 1 : earned / (double)next, 6));
        UnderNav.Children.Add(Ui.Text(next == 0 ? $"{earned} stamps, the top rank" : $"{earned} of {next} stamps to the next rank", 13, "SubtleBrush"));
    }

    protected override void BuildPage()
    {
        switch (Section)
        {
            case "id": Identity(); break;
            case "next":
            {
                Title("Next stamps", "Closest first");
                var next = _stamps.Where(s => !s.Earned).OrderByDescending(s => s.Progress).ThenBy(s => s.Group).Take(24).ToList();
                if (next.Count == 0) { Note("You've earned every stamp there is. Impressive."); break; }
                Note("The stamps you're nearest to. Keep playing and they'll stamp themselves in.");
                foreach (var s in next)
                    Row("n:" + s.Group + s.Name, s.Name, s.Group + "  ·  " + s.Detail, $"{s.Progress * 100:0}%", () => Open(s), glyph: s.Glyph, progress: s.Progress);
                break;
            }
            case "new":
                Title("New stamps", "Since your last visit");
                StampGrid(_stamps.Where(s => _new.Contains(s.Group + "/" + s.Name)));
                break;
            default:
            {
                var these = _stamps.Where(s => s.Group == Section).ToList();
                Title(Section, $"{these.Count(s => s.Earned)} of {these.Count}");
                Note(Section switch
                {
                    "Genres" => "A stamp for every genre in your library you've played (genres come from the store's details).",
                    "Decades" => "When the games you've played came out.",
                    "Stores" => "Every store you've played games from.",
                    "Moments" => "When you play: early, late, at lunch, on holidays.",
                    "Firsts" => "Firsts and big moments.",
                    _ => "Stamps that come in levels: each one up is a new stamp.",
                });
                StampGrid(these.OrderByDescending(s => s.Earned).ThenByDescending(s => s.Progress));
                break;
            }
        }
    }

    private void Identity()
    {
        var games = Host.AllGames.Where(g => g.IsGame).ToList();
        int earned = _stamps.Count(s => s.Earned);
        var (rank, _) = Labs.PassportRank(earned);
        string name = SteamLibrary.GetSteamNames().Persona ?? Environment.UserName;
        var first = Host.Playtime.Sessions.Select(s => s.Start).DefaultIfEmpty(DateTime.Today).Min();
        double hours = Host.Playtime.Seconds.Values.Sum(d => d.Values.Sum()) / 3600;
        var byGenre = games.Where(g => !string.IsNullOrEmpty(GenreCache.Of(g))).GroupBy(g => GenreCache.Of(g)!)
            .Select(x => (x.Key, Hours: x.Sum(g => Labs.HoursOf(g, Host.Playtime)))).OrderByDescending(x => x.Hours).FirstOrDefault();
        var byStore = games.GroupBy(g => g.SourceName).Select(x => (x.Key, Hours: x.Sum(g => Labs.HoursOf(g, Host.Playtime)))).OrderByDescending(x => x.Hours).FirstOrDefault();
        var top = games.OrderByDescending(g => Labs.HoursOf(g, Host.Playtime)).FirstOrDefault();

        // The passport's photo page: initials in a circle, the fields beside it, a machine-readable line under it.
        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var photo = new Grid { Width = 150, Height = 180, Margin = new Thickness(0, 0, 26, 0), VerticalAlignment = VerticalAlignment.Top };
        photo.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = OffBrush });
        if (top?.Cover != null) photo.Children.Add(new Border { CornerRadius = new CornerRadius(14), Opacity = 0.35, Background = new ImageBrush(top.Cover) { Stretch = Stretch.UniformToFill } });
        var initials = Ui.Text(string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0]))), 54, "FgBrush");
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        photo.Children.Add(initials);
        page.Children.Add(photo);
        var fields = new UniformGrid { Columns = 2 };
        void Field(string label, string value)
        {
            var f = new StackPanel { Margin = new Thickness(0, 0, 18, 14) };
            f.Children.Add(Ui.Text(label.ToUpperInvariant(), 12, "SubtleBrush"));
            var v = Ui.Text(value, 20, "FgBrush");
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            f.Children.Add(v);
            fields.Children.Add(f);
        }
        Field("Name", name);
        Field("Rank", rank);
        Field("Issued", first.ToString("d MMM yyyy"));
        Field("Stamps", $"{earned} of {_stamps.Count}");
        Field("Home genre", byGenre.Key ?? "Unknown");
        Field("Home store", byStore.Key ?? "Unknown");
        Field("Hours with SquareDeck", H(hours));
        Field("Most played", top?.Name ?? "Nothing yet");
        Grid.SetColumn(fields, 1);
        page.Children.Add(fields);
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var icon = PassportArt.Icon(26, Res("AccentBrush"));
        icon.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(icon);
        head.Children.Add(Ui.Text("GAMER PASSPORT  ·  SQUAREDECK", 15, "AccentBrush"));
        stack.Children.Add(head);
        stack.Children.Add(page);
        var mrz = new TextBlock
        {
            Text = ("P<SQD" + new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()) + "<<" + rank.ToUpperInvariant().Replace(' ', '<')).PadRight(44, '<')[..44],
            FontFamily = new FontFamily("Consolas"), FontSize = 16, Margin = new Thickness(0, 14, 0, 0), Opacity = 0.6
        };
        mrz.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        stack.Children.Add(mrz);
        Page.Children.Add(Card(stack, new Thickness(26, 22, 26, 22)));

        Header("Your stamps");
        foreach (var g in Order)
        {
            var these = _stamps.Where(s => s.Group == g).ToList();
            if (these.Count == 0) continue;
            int have = these.Count(s => s.Earned);
            var which = g;
            Row("c:" + g, g, have == these.Count ? "All earned" : $"{these.Count - have} still to earn", $"{have}/{these.Count}", () => Show(which),
                glyph: these[0].Glyph, progress: have / (double)these.Count);
        }
    }

    private void StampGrid(IEnumerable<Labs.StampPlus> stamps)
    {
        var wrap = Tiles();
        foreach (var s in stamps) wrap.Children.Add(StampTile(s));
    }

    private Button StampTile(Labs.StampPlus s)
    {
        bool fresh = _new.Contains(s.Group + "/" + s.Name);
        var stack = new StackPanel { Width = 160 };
        var face = new Grid { Width = 130, Height = 130, HorizontalAlignment = HorizontalAlignment.Center, Opacity = s.Earned ? 1 : 0.45 };
        // A round stamp: a ring with notches, the game (or an icon) inside, tilted a little each its own way.
        var ring = new Ellipse { StrokeThickness = 4, StrokeDashArray = s.Earned ? null : new DoubleCollection { 2, 2 } };
        ring.SetResourceReference(Shape.StrokeProperty, s.Earned ? "AccentBrush" : "SubtleBrush");
        face.Children.Add(ring);
        var inner = new Ellipse { Margin = new Thickness(10) };
        if (s.Earned && s.Art?.Cover != null) inner.Fill = new ImageBrush(s.Art.Cover) { Stretch = Stretch.UniformToFill };
        else inner.Fill = OffBrush;
        face.Children.Add(inner);
        if (!(s.Earned && s.Art?.Cover != null))
        {
            var g = Ui.Glyph(s.Glyph, 40, s.Earned ? "AccentBrush" : "SubtleBrush");
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.VerticalAlignment = VerticalAlignment.Center;
            face.Children.Add(g);
        }
        int tilt = (Math.Abs(s.Name.GetHashCode()) % 15) - 7;
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        var spin = new RotateTransform(s.Earned ? tilt : 0);
        var size = new ScaleTransform(1, 1);
        face.RenderTransform = new TransformGroup { Children = { size, spin } };
        if (fresh)
        {
            // Stamped in: it lands from bigger, with a little bounce.
            var land = new DoubleAnimation(1.8, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(250) };
            size.BeginAnimation(ScaleTransform.ScaleXProperty, land);
            size.BeginAnimation(ScaleTransform.ScaleYProperty, land);
        }
        stack.Children.Add(face);
        var name = Ui.Text(s.Name, 16, "FgBrush");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(name);
        var d = Ui.Text(s.Earned ? s.Detail : $"{s.Progress * 100:0}%", 13, "SubtleBrush");
        d.HorizontalAlignment = HorizontalAlignment.Center;
        d.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(d);
        if (!s.Earned && s.Progress > 0) stack.Children.Add(Bar(s.Progress, 5, 120));
        if (fresh) { var p = Pill("New"); p.HorizontalAlignment = HorizontalAlignment.Center; p.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(p); }
        var tile = new Button { Style = Switch2Pages.TileStyle, Content = stack, Uid = "s:" + s.Group + s.Name, Tag = s.Art, Margin = new Thickness(0, 0, 8, 10) };
        tile.Click += (_, _) => Open(s);
        return tile;
    }

    private async void Open(Labs.StampPlus s)
    {
        var text = (s.Earned ? "Earned.  " : $"{s.Progress * 100:0}% of the way.  ") + s.Detail;
        if (s.Art != null)
        {
            int c = await Host.AskAsync(s.Name, text, "Open " + s.Art.Name, "Close");
            if (c == 0) Host.Push(new DetailsScreen(s.Art));
        }
        else await Host.AskAsync(s.Name, text, "OK");
    }
}

// ====================================================================================================== backlog boss

/// <summary>
/// The Backlog Boss, Switch 2 style: the battle (its health, what you dealt since last time, how long it'll take at your
/// pace, tonight's target), then its weak points, quick wins, long hauls, the whole army, your trophies and the battle log.
/// </summary>
public sealed class S2BossScreen : S2PlayScreen
{
    private Labs.Boss? _boss;
    private double _hit, _seen;

    public S2BossScreen() : base("Backlog Boss", "\uE7EF", "{Select}  Choose      {Details}  Game page      LT / RT  Section      {Back}  Back", "battle") { }

    public override void OnShown()
    {
        Load();
        base.OnShown();
    }

    public override void OnResumed()
    {
        Load();
        base.OnResumed();
    }

    private void Load()
    {
        _boss = Labs.BacklogBoss(Host.AllGames, Host.Playtime, Host.Library);
        _seen = double.TryParse(Labs.Get("boss.seen"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : _boss.HoursLeft;
        _hit = Math.Max(0, _seen - _boss.HoursLeft);
        Labs.Put("boss.seen", _boss.HoursLeft.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_boss.Games > 0 || _boss.Finished.Count > 0) Labs.LogToday("boss.log", _boss.HoursLeft, _boss.Games);
    }

    private List<Labs.Minion> Minions => _boss?.Minions ?? new();

    protected override IEnumerable<(string Id, string Name, string? Count)> Sections()
    {
        var m = Minions;
        yield return ("battle", "The battle", null);
        yield return ("weak", "Weak points", m.Count > 0 ? Math.Min(5, m.Count).ToString() : null);
        yield return ("quick", "Quick wins", m.Count(x => x.Left <= 5) is > 0 and var q ? q.ToString() : null);
        yield return ("long", "Long hauls", m.Count(x => x.Left >= 30) is > 0 and var l ? l.ToString() : null);
        yield return ("army", "The whole army", m.Count > 0 ? m.Count.ToString() : null);
        yield return ("-", "", null);
        yield return ("trophies", "Trophies", _boss?.Finished.Count > 0 ? _boss.Finished.Count.ToString() : null);
        yield return ("log", "Battle log", null);
    }

    protected override void BuildUnderNav()
    {
        if (_boss == null || _boss.MaxHours <= 0) return;
        double hp = Math.Clamp(_boss.HoursLeft / _boss.MaxHours, 0, 1);
        UnderNav.Children.Add(Ui.Text($"Boss health  {hp * 100:0}%", 15, "SubtleBrush"));
        UnderNav.Children.Add(Bar(hp, 8));
    }

    protected override void BuildPage()
    {
        var b = _boss;
        if (b == null || (b.Games == 0 && b.Finished.Count == 0))
        {
            Title("No boss yet");
            Note("The boss is made from installed games that aren't marked Finished or Dropped and have a How Long To Beat time. " +
                 "Open a few games' pages so their times are looked up, then come back to fight.");
            return;
        }
        switch (Section)
        {
            case "battle": Battle(b); break;
            case "weak":
                Title("Weak points", "Closest to done");
                Note("Finish one of these to land a critical hit.");
                foreach (var m in Minions.Take(5)) MinionRow(m);
                break;
            case "quick":
                Title("Quick wins", "5 hours or less left");
                if (!Minions.Any(x => x.Left <= 5)) Note("Nothing that short right now.");
                foreach (var m in Minions.Where(x => x.Left <= 5)) MinionRow(m);
                break;
            case "long":
                Title("Long hauls", "30 hours or more left");
                if (!Minions.Any(x => x.Left >= 30)) Note("No long hauls: nothing with 30 hours or more to go.");
                foreach (var m in Minions.Where(x => x.Left >= 30).OrderByDescending(x => x.Left)) MinionRow(m);
                break;
            case "army":
                Title("The whole army", $"{Minions.Count} games  ·  {H(b.HoursLeft)} left");
                var wrap = Tiles();
                foreach (var m in Minions)
                {
                    var mm = m;
                    wrap.Children.Add(Tile("a:" + m.Game.Key, 150, 150, m.Game.Cover, null, m.Game.Name, H(m.Left) + " left", () => Choose(mm), m.Game,
                        progress: m.Main <= 0 ? 0 : 1 - m.Left / m.Main));
                }
                break;
            case "trophies":
                Title("Trophies", $"{b.Finished.Count} beaten");
                if (b.Finished.Count == 0) Note("Mark a game Finished in its notebook (or from a weak point here) and it lands on this shelf.");
                var shelf = Tiles();
                foreach (var g in b.Finished)
                {
                    var game = g;
                    shelf.Children.Add(Tile("t:" + g.Key, 150, 150, g.Cover, null, g.Name, "Beaten", () => Host.Push(new DetailsScreen(game)), g, new[] { Pill("\U0001F3C6") }));
                }
                break;
            case "log": Log(b); break;
        }
    }

    private void Battle(Labs.Boss b)
    {
        double hp = b.MaxHours <= 0 ? 0 : Math.Clamp(b.HoursLeft / b.MaxHours, 0, 1);
        int level = Math.Max(1, b.Games / 4 + 1);
        Title(hp <= 0.001 ? "Boss defeated!" : $"Backlog Boss  ·  Level {level}", $"{H(b.HoursLeft)} of health left");

        // The boss card: a big health bar that drains from where it was last time to now.
        var stack = new StackPanel();
        var top = new Grid();
        var face = new Grid { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Left };
        var ring = new Ellipse { StrokeThickness = 6 };
        ring.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        face.Children.Add(ring);
        var eye = Ui.Glyph(hp <= 0.001 ? "\uE73E" : "\uE7EF", 40, "AccentBrush");
        eye.HorizontalAlignment = HorizontalAlignment.Center;
        eye.VerticalAlignment = VerticalAlignment.Center;
        face.Children.Add(eye);
        if (_hit > 0.05)
        {
            // Took a hit since the last visit: it shakes.
            var shake = new TranslateTransform();
            face.RenderTransform = shake;
            var anim = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(400) };
            foreach (var (ms, x) in new[] { (0, 0.0), (60, -8.0), (120, 8.0), (180, -6.0), (240, 5.0), (300, -2.0), (360, 0.0) })
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            shake.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        top.Children.Add(face);
        var words = new StackPanel { Margin = new Thickness(116, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(hp <= 0.001 ? "Every game in it is beaten" : $"Made of {b.Games} unfinished games", 22, "FgBrush"));
        words.Children.Add(Ui.Text(_hit > 0.05 ? $"You dealt {H(_hit)} of damage since your last visit!" : "Play its games to deal damage, an hour at a time.", 16, _hit > 0.05 ? "AccentBrush" : "SubtleBrush"));
        top.Children.Add(words);
        stack.Children.Add(top);
        var bar = new Grid { Height = 22, Margin = new Thickness(0, 20, 0, 6) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = OffBrush });
        var was = new Border { CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.35 };
        was.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        var now = new Border { CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left };
        now.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(was);
        bar.Children.Add(now);
        double from = b.MaxHours <= 0 ? 0 : Math.Clamp(_seen / b.MaxHours, 0, 1);
        bar.SizeChanged += (_, e) =>
        {
            double w = e.NewSize.Width;
            was.Width = Math.Max(0, w * Math.Max(from, hp));
            now.BeginAnimation(WidthProperty, new DoubleAnimation(w * Math.Max(from, hp), Math.Max(hp > 0 ? 22 : 0, w * hp), TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        stack.Children.Add(bar);
        var legend = new Grid();
        legend.Children.Add(Ui.Text($"{hp * 100:0}% health", 14, "SubtleBrush"));
        var dealt = Ui.Text($"{H(Math.Max(0, b.MaxHours - b.HoursLeft))} dealt in all", 14, "SubtleBrush");
        dealt.HorizontalAlignment = HorizontalAlignment.Right;
        legend.Children.Add(dealt);
        stack.Children.Add(legend);
        Page.Children.Add(Card(stack, new Thickness(24, 20, 24, 18)));

        double pace = Labs.HoursPerWeek(Host.Playtime);
        Stats((H(b.HoursLeft), "Health left", "\uE95E"), (b.Games.ToString(), "Games in it", "\uE7FC"), (b.Finished.Count.ToString(), "Defeated", "\uE73E"),
              (pace > 0.1 ? H(pace) : "-", "Your hours a week", "\uE916"));

        // How long at your pace.
        if (b.HoursLeft > 0)
        {
            string eta = pace < 0.2 ? "Play a little this week and it'll work out when you'd beat it."
                : $"At {H(pace)} a week, you'd beat it in about {Math.Ceiling(b.HoursLeft / pace):0} weeks (around {DateTime.Today.AddDays(b.HoursLeft / pace * 7):MMMM yyyy}). Only playing its games, of course.";
            Note(eta);
        }

        // Tonight's target: the closest weak point.
        if (Minions.Count > 0)
        {
            Header("Tonight's target");
            var m = Minions[0];
            Feature("target", m.Game.Cover, null, "CRITICAL HIT", m.Game.Name, $"{H(m.Left)} left of {H(m.Main)}  ·  You've played {H(m.Played)}", () => _ = Host.ActivateAsync(m.Game), m.Game, 190,
                Bar(m.Main <= 0 ? 0 : 1 - m.Left / m.Main, 8, 360));
            var pick = Minions.Skip(1).Where(x => x.Left <= 10).OrderBy(_ => Guid.NewGuid()).FirstOrDefault();
            if (pick != null)
            {
                Header("Or try");
                MinionRow(pick);
            }
        }
    }

    private void Log(Labs.Boss b)
    {
        Title("Battle log", "Day by day");
        var log = Labs.DailyLog("boss.log");
        Chart("Boss health", log.TakeLast(60).Select(x => (x.Day.ToString("d MMM"), x.Value)).ToList(), H);
        // Damage by week: the drop in health from the start to the end of each week.
        var weeks = log.GroupBy(x => PlaytimeStore.WeekStart(x.Day)).Select(w => (w.Key, Drop: Math.Max(0, w.First().Value - w.Last().Value))).TakeLast(16).ToList();
        Chart("Damage each week", weeks.Select(w => ("Week of " + w.Key.ToString("d MMM"), w.Drop)).ToList(), H, 110);
        var recent = Host.AllGames.Where(g => g.IsGame && Host.Library.Find(g.Key)?.Status == "Finished").Take(5).ToList();
        if (recent.Count > 0)
        {
            Header("Beaten");
            foreach (var g in recent) { var game = g; Row("lb:" + g.Key, g.Name, "Finished", null, () => Host.Push(new DetailsScreen(game)), g.Cover, tag: g); }
        }
    }

    private void MinionRow(Labs.Minion m)
    {
        var genre = GenreCache.Of(m.Game);
        Row("m:" + m.Game.Key, m.Game.Name, $"{H(m.Played)} played of {H(m.Main)}" + (genre != null ? "  ·  " + genre : ""), H(m.Left) + " left", () => Choose(m), m.Game.Cover,
            tag: m.Game, progress: m.Main <= 0 ? 0 : 1 - m.Left / m.Main,
            badges: m.Left <= 2 ? new[] { Pill("Almost there") } : null);
    }

    private async void Choose(Labs.Minion m)
    {
        int c = await Host.AskAsync(m.Game.Name, $"{H(m.Left)} left of How Long To Beat's {H(m.Main)} main story.", "Attack (play)", "Game page", "Mark as finished", "Drop it from the boss", "Cancel");
        switch (c)
        {
            case 0: _ = Host.ActivateAsync(m.Game); break;
            case 1: Host.Push(new DetailsScreen(m.Game)); break;
            case 2 or 3:
                Host.Library.For(m.Game.Key).Status = c == 2 ? "Finished" : "Dropped";
                Host.Library.Save();
                if (c == 2) Host.ShowToast("CRITICAL HIT", m.Game.Name + " defeated!", $"The boss lost {H(m.Left)} of health", "", "achievement", m.Game.Cover, m.Game, remember: false);
                Load();
                Rebuild(FocusTo.Keep);
                break;
        }
    }
}

// ====================================================================================================== mood

/// <summary>
/// The Mood picker, Switch 2 style: moods down the left, and for the one chosen its card, how long you've got, and the
/// games that fit, three big ones first. Y picks again.
/// </summary>
public sealed class S2MoodScreen : S2PlayScreen
{
    private static readonly Dictionary<string, (Color From, Color To)> Colors = new()
    {
        ["chill"] = (Color.FromRgb(0x2E, 0xB8, 0x9A), Color.FromRgb(0x1C, 0x6E, 0x8C)),
        ["adrenaline"] = (Color.FromRgb(0xF0, 0x4E, 0x3E), Color.FromRgb(0xB0, 0x1E, 0x5A)),
        ["story"] = (Color.FromRgb(0x8E, 0x5C, 0xF0), Color.FromRgb(0x46, 0x2E, 0xA8)),
        ["laugh"] = (Color.FromRgb(0xF7, 0xB5, 0x2C), Color.FromRgb(0xE0, 0x6A, 0x1E)),
        ["brain"] = (Color.FromRgb(0x3E, 0x8E, 0xF0), Color.FromRgb(0x24, 0x4C, 0xB8)),
        ["spooky"] = (Color.FromRgb(0x5A, 0x3E, 0x6E), Color.FromRgb(0x1A, 0x10, 0x22)),
        ["unfinished"] = (Color.FromRgb(0xE8, 0x7A, 0x3A), Color.FromRgb(0x9A, 0x3A, 0x22)),
        ["rediscover"] = (Color.FromRgb(0x4E, 0xB8, 0xD8), Color.FromRgb(0x2A, 0x5E, 0x8E)),
        ["nostalgia"] = (Color.FromRgb(0xC8, 0x8E, 0x5E), Color.FromRgb(0x6E, 0x44, 0x2A)),
        ["new"] = (Color.FromRgb(0x6E, 0xD8, 0x6E), Color.FromRgb(0x2A, 0x8E, 0x5A)),
        ["surprise"] = (Color.FromRgb(0xF0, 0x6E, 0xC8), Color.FromRgb(0x7A, 0x3E, 0xE0)),
    };

    private static readonly (string Name, double Hours)[] Times = { ("Any time", 0), ("Under an hour", 1), ("An hour or two", 2), ("All evening", 5) };
    private readonly Dictionary<string, List<Game>> _picks = new();

    public S2MoodScreen() : base("How are you feeling?", "\uE76E", "{Select}  Play      {Details}  Game page      {Options}  Pick again      LT / RT  Mood      {Back}  Back",
        Labs.Get("mood.last", "chill")) { }

    private double MaxHours => Times[Math.Clamp(int.TryParse(Labs.Get("mood.time", "0"), out var t) ? t : 0, 0, Times.Length - 1)].Hours;
    private bool InstalledOnly => Labs.Get("mood.installed", "1") == "1";

    protected override IEnumerable<(string Id, string Name, string? Count)> Sections()
    {
        foreach (var m in Labs.MoodsPlus)
        {
            if (m.Id == "spooky" || m.Id == "unfinished") yield return ("-", "", null);
            yield return (m.Id, m.Name, null);
        }
    }

    private List<Game> Picks()
    {
        if (!_picks.TryGetValue(Section, out var list))
            _picks[Section] = list = Labs.PickForMoodPlus(Section, Host.AllGames, Host.Playtime, Host.Library, MaxHours, InstalledOnly);
        return list;
    }

    protected override bool OnY()
    {
        _picks.Remove(Section);
        UiSound.Page();
        Rebuild(FocusTo.Content);
        return true;
    }

    protected override void BuildPage()
    {
        Labs.Put("mood.last", Section, save: false);
        var m = Labs.MoodsPlus.FirstOrDefault(x => x.Id == Section) ?? Labs.MoodsPlus[0];
        var (from, to) = Colors.TryGetValue(m.Id, out var c) ? c : (Color.FromRgb(0x55, 0x55, 0x66), Color.FromRgb(0x33, 0x33, 0x44));

        // The mood's own card, in its colors.
        var card = new Grid { Height = 120, Margin = new Thickness(6, 14, 6, 6) };
        card.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = new LinearGradientBrush(from, to, 20) });
        var big = new TextBlock { Text = m.Glyph, FontFamily = Ui.IconFont, FontSize = 84, Foreground = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 30, 0) };
        card.Children.Add(big);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
        words.Children.Add(new TextBlock { Text = m.Name, FontSize = 32, Foreground = Brushes.White });
        words.Children.Add(new TextBlock { Text = m.Line, FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)) });
        card.Children.Add(words);
        var slide = new TranslateTransform(30, 0);
        card.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Page.Children.Add(card);

        int ti = Array.FindIndex(Times, t => t.Hours == MaxHours);
        Page.Children.Add(Switch2Pages.OptionLine(
            Option("o:time", "Time I have", Times[ti].Name, () => { Labs.Put("mood.time", ((ti + 1) % Times.Length).ToString()); _picks.Clear(); Rebuild(FocusTo.Keep); }),
            Option("o:inst", "Installed only", InstalledOnly ? "On" : "Off", () => { Labs.Put("mood.installed", InstalledOnly ? "0" : "1"); _picks.Clear(); Rebuild(FocusTo.Keep); })));

        var picks = Picks();
        if (picks.Count == 0)
        {
            Note(m.Genres.Length > 0 ? "Nothing in your library fits this one yet: genres come from the store's details, so open a few game pages and try again."
                                     : "Nothing fits this one right now. Try another mood, or turn off Installed only.");
            return;
        }
        Header("Top picks");
        var wrap = Tiles();
        foreach (var g in picks.Take(3)) wrap.Children.Add(PickTile(g));
        if (picks.Count > 3)
        {
            Header("More that fit");
            foreach (var g in picks.Skip(3))
            {
                var game = g;
                Row("r:" + g.Key, g.Name, Why(g), Labs.HoursOf(g, Host.Playtime) is > 0.1 and var h ? H(h) + " played" : "New to you", () => _ = Host.ActivateAsync(game), g.Cover, tag: g);
            }
        }
        Note("Press Y to pick again. The picks change each time.");
    }

    private Button PickTile(Game g)
    {
        var badges = new List<UIElement>();
        if (!g.IsInstalled) badges.Add(Pill("Not installed", false));
        if (Host.Library.Find(g.Key)?.Status == "Playing") badges.Add(Pill("Playing"));
        return Tile("p:" + g.Key, 250, 250, g.Cover, null, g.Name, Why(g), () => _ = Host.ActivateAsync(g), g, badges);
    }

    private string Why(Game g)
    {
        var bits = new List<string>();
        if (GenreCache.Of(g) is { } genre) bits.Add(genre);
        if (HowLongToBeat.Cached(g).Times is { Main: > 0 } t) bits.Add(H(t.Main) + " to beat");
        double played = Labs.HoursOf(g, Host.Playtime);
        var last = g.LastPlayed ?? g.RecentAt;
        if (Section == "rediscover" && last > DateTime.MinValue.AddYears(1)) bits.Add($"Last played {last:MMM yyyy}");
        else if (Section == "nostalgia" && g.ReleaseYear is int y) bits.Add("From " + y);
        else if (played > 0.1) bits.Add(H(played) + " played");
        return string.Join("  ·  ", bits);
    }
}

// ====================================================================================================== leaderboard

/// <summary>
/// The Friends leaderboard, Switch 2 style and reworked: the ranking with a podium, what your friends are playing,
/// the games you play together, awards, your place over time, and a page for each friend comparing them with you.
/// </summary>
public sealed class S2LeaderboardScreen : S2PlayScreen
{
    private List<Labs.PlayerWeek>? _players;
    private string? _error;
    private DateTime _at;
    private string _sort = "hours";
    private string? _player;

    public S2LeaderboardScreen() : base("Friends leaderboard", "\uE716", "{Select}  Open      {Details}  Sort      {Options}  Refresh      LT / RT  Section      {Back}  Back", "board") { }

    public override void OnShown()
    {
        base.OnShown();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _error = null;
        var key = Host.Settings.SteamWebApiKey;
        try { (_players, _error) = await Task.Run(() => Labs.LeaderboardAsync(key, Cts.Token)); }
        catch (OperationCanceledException) { return; }
        _at = DateTime.Now;
        if (_players != null && _players.FindIndex(p => p.IsYou) is var r and >= 0)
            Labs.LogToday("lb.log", r + 1, _players[r].Hours);
        if (Host.IsTop(this)) Rebuild(FocusTo.Keep);
    }

    protected override bool OnY()
    {
        _players = null;
        Rebuild(FocusTo.Keep);
        _ = LoadAsync();
        return true;
    }

    protected override bool OnX()
    {
        if (Section != "board") return base.OnX();
        _sort = _sort switch { "hours" => "games", "games" => "shared", _ => "hours" };
        Rebuild(FocusTo.Keep);
        return true;
    }

    private Labs.PlayerWeek? Me => _players?.FirstOrDefault(p => p.IsYou);
    private HashSet<string> MyGames => Me?.Games.Select(g => g.Game).ToHashSet() ?? new();
    private int Shared(Labs.PlayerWeek p) => p.IsYou ? 0 : p.Games.Count(g => MyGames.Contains(g.Game));

    protected override IEnumerable<(string Id, string Name, string? Count)> Sections()
    {
        yield return ("board", "Ranking", _players != null ? _players.Count.ToString() : null);
        yield return ("games", "What friends play", null);
        yield return ("together", "Played together", _players != null && _players.Sum(Shared) > 0 ? _players.Count(p => Shared(p) > 0).ToString() : null);
        yield return ("awards", "Awards", null);
        yield return ("you", "Your place", null);
        if (_player != null)
        {
            yield return ("-", "", null);
            yield return ("p", _player, null);
        }
    }

    protected override void BuildUnderNav()
    {
        UnderNav.Children.Add(Ui.Text("The last two weeks", 14, "SubtleBrush"));
        if (_players != null) UnderNav.Children.Add(Ui.Text("Updated " + _at.ToString("t"), 13, "SubtleBrush"));
    }

    protected override void BuildPage()
    {
        if (_error != null) { Title("Friends leaderboard"); Note(_error); return; }
        if (_players == null) { Title("Friends leaderboard"); Note("Asking Steam…"); return; }
        if (_players.Count <= 1) { Title("Friends leaderboard"); Note("None of your friends have played lately, or their game details are private."); }
        switch (Section)
        {
            case "board": Board(); break;
            case "games": Games(); break;
            case "together": Together(); break;
            case "awards": Awards(); break;
            case "you": You(); break;
            case "p": Player(); break;
        }
    }

    private List<Labs.PlayerWeek> Ranked() => _sort switch
    {
        "games" => _players!.OrderByDescending(p => p.Games.Count).ThenByDescending(p => p.Hours).ToList(),
        "shared" => _players!.OrderByDescending(p => p.IsYou ? int.MaxValue : Shared(p)).ThenByDescending(p => p.Hours).ToList(),
        _ => _players!,
    };

    private void Board()
    {
        var list = Ranked();
        Title("Ranking", _sort switch { "games" => "By games played", "shared" => "By games shared with you", _ => "By hours played" });
        // The podium: the top three on steps, first in the middle.
        if (list.Count >= 2)
        {
            var podium = new Grid { Height = 200, Margin = new Thickness(6, 10, 6, 6) };
            for (int i = 0; i < 3; i++) podium.ColumnDefinitions.Add(new ColumnDefinition());
            int[] place = { 1, 0, 2 };
            double[] height = { 90, 130, 64 };
            for (int col = 0; col < 3; col++)
            {
                int idx = place[col];
                if (idx >= list.Count) continue;
                var p = list[idx];
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 0) };
                var name = Ui.Text(p.Name + (p.IsYou ? " (you)" : ""), 18, p.IsYou ? "AccentBrush" : "FgBrush");
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                stack.Children.Add(name);
                var hrs = Ui.Text(H(p.Hours), 14, "SubtleBrush");
                hrs.HorizontalAlignment = HorizontalAlignment.Center;
                hrs.Margin = new Thickness(0, 0, 0, 6);
                stack.Children.Add(hrs);
                var step = new Border { Height = height[col], CornerRadius = new CornerRadius(12, 12, 0, 0), BorderThickness = new Thickness(1, 1, 1, 0) };
                step.SetResourceReference(Border.BackgroundProperty, idx == 0 ? "AccentBrush" : "S2Card");
                step.SetResourceReference(Border.BorderBrushProperty, "S2Line");
                step.Child = new TextBlock { Text = (idx + 1).ToString(), FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = idx == 0 ? Brushes.White : Res("FgBrush") };
                var rise = new ScaleTransform(1, 0);
                step.RenderTransformOrigin = new Point(0.5, 1);
                step.RenderTransform = rise;
                rise.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(480)) { BeginTime = TimeSpan.FromMilliseconds(120 * (2 - idx)), EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
                stack.Children.Add(step);
                Grid.SetColumn(stack, col);
                podium.Children.Add(stack);
            }
            var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            line.SetResourceReference(Shape.FillProperty, "S2Line");
            Grid.SetColumnSpan(line, 3);
            podium.Children.Add(line);
            Page.Children.Add(podium);
        }
        Page.Children.Add(Switch2Pages.OptionLine(Option("o:sort", "Sort by", _sort switch { "games" => "Games played", "shared" => "Shared with you", _ => "Hours" }, () => OnX()),
            Option("o:refresh", "Refresh", "Now", () => OnY())));
        double max = Math.Max(0.1, list.Max(p => p.Hours));
        int rank = 1;
        foreach (var p in list)
        {
            var player = p;
            int shared = Shared(p);
            string top = p.Games.Count > 0 ? "Mostly " + p.Games[0].Game : "Nothing lately";
            var badges = new List<UIElement>();
            if (p.IsYou) badges.Add(Pill("You"));
            if (shared > 0) badges.Add(Pill($"{shared} in common", false));
            Row("pl:" + p.Name, $"{rank++}.  {p.Name}", $"{top}  ·  {p.Games.Count} {(p.Games.Count == 1 ? "game" : "games")}", H(p.Hours), () => OpenPlayer(player.Name),
                glyph: p.IsYou ? "\uE77B" : "\uE716", badges: badges, progress: p.Hours / max);
        }
        if (Me is { } me && list.IndexOf(me) is var mine and > 0)
        {
            var ahead = list[mine - 1];
            Note(_sort == "hours" ? $"{H(Math.Max(0, ahead.Hours - me.Hours))} more and you'd pass {ahead.Name}." : "");
        }
        Note("Hours from the last two weeks (the period Steam reports). Friends whose game details are private don't appear.");
    }

    private void OpenPlayer(string name)
    {
        _player = name;
        Show("p");
    }

    private void Games()
    {
        var all = _players!.Where(p => !p.IsYou).SelectMany(p => p.Games.Select(g => (Player: p.Name, g.Game, g.Hours)))
            .GroupBy(x => x.Game).Select(g => (Game: g.Key, Players: g.Select(x => x.Player).Distinct().ToList(), Hours: g.Sum(x => x.Hours)))
            .OrderByDescending(x => x.Players.Count).ThenByDescending(x => x.Hours).Take(40).ToList();
        Title("What friends play", $"{all.Count} games");
        if (all.Count == 0) { Note("Your friends haven't played anything lately."); return; }
        foreach (var x in all)
        {
            var owned = Labs.Owned(x.Game, Host.AllGames);
            var badges = new List<UIElement>();
            if (MyGames.Contains(x.Game)) badges.Add(Pill("You play it"));
            else if (owned != null) badges.Add(Pill("In your library", false));
            var game = x.Game;
            Row("g:" + x.Game, x.Game, string.Join(", ", x.Players.Take(4)) + (x.Players.Count > 4 ? $" and {x.Players.Count - 4} more" : ""),
                $"{x.Players.Count} {(x.Players.Count == 1 ? "friend" : "friends")}  ·  {H(x.Hours)}",
                () => { if (owned != null) Host.Push(new DetailsScreen(owned)); else ProcessUtil.Open("https://store.steampowered.com/search/?term=" + Uri.EscapeDataString(game)); },
                owned?.Cover, glyph: "\uE7FC", tag: owned, badges: badges);
        }
    }

    private void Together()
    {
        Title("Played together", "Games you and a friend both played");
        var mine = Me?.Games.ToDictionary(g => g.Game, g => g.Hours) ?? new();
        var pairs = _players!.Where(p => !p.IsYou).SelectMany(p => p.Games.Where(g => mine.ContainsKey(g.Game)).Select(g => (p.Name, g.Game, Theirs: g.Hours, Yours: mine[g.Game])))
            .OrderByDescending(x => x.Theirs + x.Yours).ToList();
        if (pairs.Count == 0) { Note("You haven't played the same games as your friends lately. The What friends play page shows what they're into."); return; }
        foreach (var game in pairs.GroupBy(x => x.Game))
        {
            Header(game.Key);
            var owned = Labs.Owned(game.Key, Host.AllGames);
            foreach (var x in game)
            {
                bool ahead = x.Yours >= x.Theirs;
                var friend = x.Name;
                Row("t:" + x.Game + x.Name, x.Name, $"You {H(x.Yours)}  ·  {friend} {H(x.Theirs)}", ahead ? "You lead" : $"{H(x.Theirs - x.Yours)} behind",
                    () => OpenPlayer(friend), owned?.Cover, glyph: "\uE716", tag: owned, progress: x.Yours / Math.Max(0.01, x.Yours + x.Theirs));
            }
        }
    }

    private void Awards()
    {
        Title("Awards", "This fortnight");
        var friends = _players!.ToList();
        if (friends.Count == 0) return;
        void Award(string glyph, string title, Labs.PlayerWeek? who, string why)
        {
            if (who == null) return;
            var name = who.Name;
            Row("aw:" + title, title, why, name + (who.IsYou ? " (you)" : ""), () => OpenPlayer(name), glyph: glyph, badges: who.IsYou ? new[] { Pill("You!") } : null);
        }
        var most = friends.OrderByDescending(p => p.Hours).First();
        Award("\uE7EF", "Most hours", most, H(most.Hours) + " in two weeks");
        var devoted = friends.Where(p => p.Games.Count > 0).OrderByDescending(p => p.Games[0].Hours).FirstOrDefault();
        if (devoted != null) Award("\uE734", "Most devoted", devoted, $"{H(devoted.Games[0].Hours)} in {devoted.Games[0].Game} alone");
        var explorer = friends.OrderByDescending(p => p.Games.Count).First();
        Award("\uE8B9", "Explorer", explorer, $"{explorer.Games.Count} different games");
        var twin = friends.Where(p => !p.IsYou).OrderByDescending(Shared).FirstOrDefault();
        if (twin != null && Shared(twin) > 0) Award("\uE8FA", "Your gaming twin", twin, $"{Shared(twin)} games in common with you");
        var solo = friends.Where(p => p.Games.Count > 0).FirstOrDefault(p => friends.Count(o => o.Games.Any(g => g.Game == p.Games[0].Game)) == 1);
        if (solo != null) Award("\uE81D", "Lone adventurer", solo, $"The only one playing {solo.Games[0].Game}");
        var hot = friends.SelectMany(p => p.Games).GroupBy(g => g.Game).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(x => x.Hours)).FirstOrDefault();
        if (hot != null)
        {
            Header("Game of the fortnight");
            var owned = Labs.Owned(hot.Key, Host.AllGames);
            var game = hot.Key;
            Feature("hot", owned?.Cover, null, $"{hot.Count()} PLAYING", hot.Key, $"{H(hot.Sum(x => x.Hours))} played by you and your friends in two weeks",
                () => { if (owned != null) Host.Push(new DetailsScreen(owned)); else ProcessUtil.Open("https://store.steampowered.com/search/?term=" + Uri.EscapeDataString(game)); }, owned, 170);
        }
    }

    private void You()
    {
        var log = Labs.DailyLog("lb.log");
        var me = Me;
        int rank = me == null ? 0 : _players!.IndexOf(me) + 1;
        Title("Your place", rank > 0 ? $"{rank} of {_players!.Count}" : null);
        if (me != null)
            Stats((rank > 0 ? "#" + rank : "-", "Your place", "\uE7EF"), (H(me.Hours), "Your hours", "\uE916"), (me.Games.Count.ToString(), "Your games", "\uE7FC"),
                  (_players!.Where(p => !p.IsYou).Sum(Shared).ToString(), "Shared with friends", "\uE8FA"));
        Chart("Your hours (last two weeks, each day you looked)", log.TakeLast(45).Select(x => (x.Day.ToString("d MMM"), x.Extra)).ToList(), H);
        Chart("Your place (higher bar is better)", log.TakeLast(45).Select(x => (x.Day.ToString("d MMM"), Math.Max(0, (_players?.Count ?? 10) + 1 - x.Value))).ToList(),
              v => "#" + Math.Max(1, (_players?.Count ?? 10) + 1 - v).ToString("0"), 110);
        if (me != null && me.Games.Count > 0)
        {
            Header("What you played");
            double max = me.Games.Max(g => g.Hours);
            foreach (var (game, hours) in me.Games)
            {
                var owned = Labs.Owned(game, Host.AllGames);
                Row("y:" + game, game, null, H(hours), owned != null ? () => Host.Push(new DetailsScreen(owned)) : null, owned?.Cover, glyph: "\uE7FC", tag: owned, progress: hours / max);
            }
        }
    }

    private void Player()
    {
        var p = _players!.FirstOrDefault(x => x.Name == _player);
        if (p == null) { Title(_player ?? ""); Note("They haven't played anything lately."); return; }
        int rank = _players!.IndexOf(p) + 1;
        Title(p.Name + (p.IsYou ? " (you)" : ""), $"#{rank}  ·  {H(p.Hours)}");
        var me = Me;
        Stats(("#" + rank, "Place", "\uE7EF"), (H(p.Hours), "Hours", "\uE916"), (p.Games.Count.ToString(), "Games", "\uE7FC"), (p.IsYou ? "-" : Shared(p).ToString(), "In common with you", "\uE8FA"));
        if (!p.IsYou && me != null)
            Note(me.Hours >= p.Hours ? $"You played {H(me.Hours - p.Hours)} more than {p.Name} in the last two weeks." : $"{p.Name} played {H(p.Hours - me.Hours)} more than you in the last two weeks.");
        Header("Their games");
        double max = Math.Max(0.01, p.Games.Select(g => g.Hours).DefaultIfEmpty(0).Max());
        var mine = me?.Games.ToDictionary(g => g.Game, g => g.Hours) ?? new();
        foreach (var (game, hours) in p.Games)
        {
            var owned = Labs.Owned(game, Host.AllGames);
            var badges = new List<UIElement>();
            if (!p.IsYou && mine.TryGetValue(game, out var yours)) badges.Add(Pill("You: " + H(yours)));
            else if (owned != null) badges.Add(Pill("In your library", false));
            var g = game;
            Row("pg:" + game, game, null, H(hours),
                () => { if (owned != null) Host.Push(new DetailsScreen(owned)); else ProcessUtil.Open("https://store.steampowered.com/search/?term=" + Uri.EscapeDataString(g)); },
                owned?.Cover, glyph: "\uE7FC", tag: owned, badges: badges, progress: hours / max);
        }
    }
}

// ====================================================================================================== free games

/// <summary>
/// Free games, Switch 2 style and grown: everything free right now on Epic, Steam, Amazon Prime Gaming, GOG, Ubisoft,
/// EA, Battle.net, itch.io and DRM-free stores; what's ending soon; Epic's next free games; free loot and DLC; deals
/// from many stores (best, under $5, biggest discounts, big games, your wishlist, by store); and what you've claimed.
/// New ones since your last visit are marked, games you already have say so, and the total worth is added up.
/// </summary>
public sealed class S2FreeGamesScreen : S2PlayScreen
{
    private List<Labs.Offer>? _offers;
    private bool _failed;
    private HashSet<string> _new = new();
    private readonly Dictionary<string, List<Labs.Deal>?> _deals = new();
    private readonly HashSet<string> _loading = new();
    private List<Labs.DealStore> _stores = new();
    private string _sort = Labs.Get("free.sort", "ends");
    private bool HideClaimed => Labs.Get("free.hideClaimed", "0") == "1";
    private string _dealStore = Labs.Get("free.dealStore", "1");

    public S2FreeGamesScreen() : base("Free games", "\uE719", "{Select}  Open      {Details}  Sort      {Options}  Refresh      LT / RT  Section      {Back}  Back", "now") { }

    public override void OnShown()
    {
        base.OnShown();
        _ = LoadAsync();
        _ = Task.Run(() => Labs.DealStoresAsync(Cts.Token)).ContinueWith(t => Dispatcher.BeginInvoke(() => { _stores = t.Result; if (Section.StartsWith("d")) Rebuild(FocusTo.Keep); }));
    }

    private async Task LoadAsync()
    {
        try { _offers = await Task.Run(() => Labs.AllFreeAsync(Cts.Token)); }
        catch (OperationCanceledException) { return; }
        _failed = _offers == null;
        if (_offers != null) _new = Labs.TakeNewOffers(_offers.Where(o => o.Starts == null));
        if (Host.IsTop(this)) Rebuild(FocusTo.Keep);
    }

    protected override bool OnY()
    {
        _offers = null;
        _failed = false;
        _deals.Clear();
        Rebuild(FocusTo.Keep);
        _ = LoadAsync();
        return true;
    }

    protected override bool OnX()
    {
        _sort = _sort switch { "ends" => "worth", "worth" => "new", "new" => "name", _ => "ends" };
        Labs.Put("free.sort", _sort);
        Rebuild(FocusTo.Keep);
        return true;
    }

    private IEnumerable<Labs.Offer> Now => (_offers ?? new()).Where(o => o.Starts == null && (!HideClaimed || !Labs.IsClaimed(o)));
    private IEnumerable<Labs.Offer> NowGames => Now.Where(o => o.Kind == "game");

    protected override IEnumerable<(string Id, string Name, string? Count)> Sections()
    {
        string? N(int n) => _offers == null || n == 0 ? null : n.ToString();
        yield return ("now", "Free right now", N(NowGames.Count()));
        yield return ("soon", "Ending soon", N(NowGames.Count(o => o.Ends is DateTime e && e - DateTime.Now < TimeSpan.FromDays(2))));
        yield return ("next", "Coming next", N((_offers ?? new()).Count(o => o.Starts != null)));
        if (_new.Count > 0) yield return ("new", "New since last time", _new.Count.ToString());
        yield return ("-", "", null);
        foreach (var (store, shortName, _) in Labs.OfferStores)
        {
            int n = NowGames.Count(o => o.Store == store);
            if (n > 0 || store is "Epic Games Store" or "Steam" or "Amazon Prime Gaming" or "GOG") yield return ("s:" + store, shortName, N(n));
        }
        yield return ("loot", "Free loot and DLC", N(Now.Count(o => o.Kind == "loot")));
        yield return ("-", "", null);
        yield return ("dbest", "Best deals", null);
        yield return ("dcheap", "Under $5", null);
        yield return ("dbig", "Biggest discounts", null);
        yield return ("daaa", "Big games on sale", null);
        if (Host.WishlistGames.Count > 0) yield return ("dwish", "Your wishlist on sale", null);
        yield return ("dstore", "Deals by store", null);
        yield return ("-", "", null);
        yield return ("claimed", "Claimed", (_offers ?? new()).Count(Labs.IsClaimed) is > 0 and var c ? c.ToString() : null);
    }

    protected override void BuildUnderNav()
    {
        if (_offers == null) return;
        double worth = Labs.WorthOf(NowGames);
        if (worth > 0)
        {
            UnderNav.Children.Add(Ui.Text("$" + worth.ToString("N0"), 24, "AccentBrush"));
            UnderNav.Children.Add(Ui.Text("of games free right now", 13, "SubtleBrush"));
        }
    }

    protected override void BuildPage()
    {
        if (Section.StartsWith('d')) { Deals(); return; }
        if (_failed) { Title("Free games"); Note("The giveaways couldn't be loaded. Check the internet connection, then press Y to try again."); return; }
        if (_offers == null) { Title("Free games"); Note("Looking for free games on every store…"); return; }
        switch (Section)
        {
            case "now":
            {
                var list = Sort(NowGames).ToList();
                Title("Free right now", $"{list.Count} games");
                SortLine();
                // The best one first, big.
                var top = list.Where(o => !Labs.IsClaimed(o)).OrderByDescending(o => Labs.WorthOf(new[] { o })).FirstOrDefault();
                if (top != null)
                    Feature("feat:" + top.Id, null, top.Image, $"FREE ON {top.Store.ToUpperInvariant()}  ·  {Labs.EndsIn(top.Ends).ToUpperInvariant()}", top.Title,
                        (top.Worth.Length > 0 ? "Usually " + top.Worth + ".  " : "") + Clean(top.Description), () => Open(top), height: 230);
                Grid(list.Where(o => o != top));
                Note("Free to keep if you claim them before they end. Giveaways come from GamerPower; Epic's from Epic's own list.");
                break;
            }
            case "soon":
            {
                var list = NowGames.Where(o => o.Ends is DateTime e && e - DateTime.Now < TimeSpan.FromDays(2)).OrderBy(o => o.Ends).ToList();
                Title("Ending soon", "Within two days");
                if (list.Count == 0) Note("Nothing ends in the next two days.");
                Grid(list);
                break;
            }
            case "next":
            {
                var list = _offers.Where(o => o.Starts != null).OrderBy(o => o.Starts).ToList();
                Title("Coming next", "Epic's next free games");
                if (list.Count == 0) { Note("Epic hasn't said what's next yet. It usually does a week ahead."); break; }
                foreach (var o in list)
                    Feature("next:" + o.Id, null, o.Image, Labs.WhenStarts(o.Starts!.Value).ToUpperInvariant(), o.Title, Clean(o.Description), () => ProcessUtil.Open(o.Url), height: 200);
                break;
            }
            case "new":
                Title("New since last time", $"{_new.Count}");
                Grid(Sort((_offers ?? new()).Where(o => _new.Contains(o.Id))));
                break;
            case "loot":
            {
                var list = Sort(Now.Where(o => o.Kind == "loot")).ToList();
                Title("Free loot and DLC", $"{list.Count}");
                Note("In-game items, skins, currency and DLC to claim. Many of Amazon Prime Gaming's are here (they need Prime).");
                SortLine();
                Grid(list);
                break;
            }
            case "claimed":
            {
                var list = (_offers ?? new()).Where(Labs.IsClaimed).ToList();
                Title("Claimed", $"{list.Count}");
                Note(list.Count == 0 ? "Mark a giveaway as claimed when you've got it and it moves here." : "Giveaways you've marked as claimed (while they're still running).");
                Grid(list);
                break;
            }
            default:
            {
                var store = Section[2..];
                var list = Sort(NowGames.Where(o => o.Store == store)).ToList();
                Title(store, $"{list.Count} free");
                Note(store switch
                {
                    "Epic Games Store" => "Epic gives a game away every week (and more over the holidays). See Coming next for the next one.",
                    "Amazon Prime Gaming" => "Free games with Amazon Prime, claimed on Amazon's site and played through the Amazon Games app, GOG, Epic or another store it names.",
                    "Steam" => "Free-to-keep games on Steam: add them to your account before they end.",
                    "GOG" => "GOG's giveaways are DRM-free: yours to keep, offline too.",
                    _ => "Free to keep if you claim them before they end.",
                });
                if (list.Count == 0) Note("Nothing free there right now.");
                SortLine();
                Grid(list);
                break;
            }
        }
    }

    private void SortLine() =>
        Page.Children.Add(Switch2Pages.OptionLine(
            Option("o:sort", "Sort by", _sort switch { "worth" => "Worth", "new" => "Newest", "name" => "Name", _ => "Ending soon" }, () => OnX()),
            Option("o:hide", "Hide claimed", HideClaimed ? "On" : "Off", () => { Labs.Put("free.hideClaimed", HideClaimed ? "0" : "1"); Rebuild(FocusTo.Keep); })));

    private IEnumerable<Labs.Offer> Sort(IEnumerable<Labs.Offer> list) => _sort switch
    {
        "worth" => list.OrderByDescending(o => Labs.WorthOf(new[] { o })),
        "new" => list.OrderByDescending(o => o.Added ?? DateTime.MinValue),
        "name" => list.OrderBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase),
        _ => list.OrderBy(o => o.Ends ?? DateTime.MaxValue),
    };

    private static string Clean(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<.*?>", "").Trim();

    private void Grid(IEnumerable<Labs.Offer> offers)
    {
        var wrap = Tiles();
        foreach (var o in offers)
        {
            var offer = o;
            var badges = new List<UIElement>();
            if (_new.Contains(o.Id)) badges.Add(Pill("New"));
            if (Labs.IsClaimed(o)) badges.Add(Pill("Claimed", false));
            else if (o.Kind == "game" && Labs.Owned(o.Title, Host.AllGames) != null) badges.Add(Pill("In your library", false));
            if (o.Ends is DateTime e && e - DateTime.Now < TimeSpan.FromHours(24)) badges.Add(Pill("Last day"));
            string sub = (o.Starts != null ? Labs.WhenStarts(o.Starts.Value) : Labs.EndsIn(o.Ends)) + (o.Worth.Length > 0 ? "  ·  " + o.Worth : "");
            if (!Section.StartsWith("s:")) sub = ShortStore(o.Store) + "  ·  " + sub;
            wrap.Children.Add(Tile("o:" + o.Id, 300, 140, null, o.Image, o.Title, sub, () => Open(offer), null, badges, "\uE719"));
        }
    }

    private static string ShortStore(string store) => Labs.OfferStores.FirstOrDefault(s => s.Store == store).Short ?? store;

    private async void Open(Labs.Offer o)
    {
        bool claimed = Labs.IsClaimed(o);
        var owned = o.Kind == "game" ? Labs.Owned(o.Title, Host.AllGames) : null;
        var text = string.Join("\n\n", new[]
        {
            $"{o.Store}  ·  {(o.Starts != null ? Labs.WhenStarts(o.Starts.Value) : Labs.EndsIn(o.Ends))}" + (o.Worth.Length > 0 ? "  ·  Usually " + o.Worth : "") +
            (o.Claimers > 0 ? $"  ·  {o.Claimers:N0} people claimed it" : ""),
            owned != null ? "You already have a game called that in your library." : "",
            Clean(o.Description),
        }.Where(s => s.Length > 0));
        var options = new List<string> { "Claim it in the store", claimed ? "Not claimed after all" : "Mark as claimed" };
        if (owned != null) options.Add("Open " + owned.Name);
        options.Add("Cancel");
        int c = await Host.AskAsync(o.Title, text, options.ToArray());
        if (c == 0)
        {
            ProcessUtil.Open(o.Url);
            if (!claimed && await Host.AskAsync("Did you claim it?", "Mark " + o.Title + " as claimed so it moves to Claimed.", "Mark as claimed", "Not yet") == 0)
                Labs.SetClaimed(o, true);
        }
        else if (c == 1) Labs.SetClaimed(o, !claimed);
        else if (c == 2 && owned != null) { Host.Push(new DetailsScreen(owned)); return; }
        else return;
        Rebuild(FocusTo.Keep);
    }

    // ---------------------------------------------------------------- deals

    private string DealQuery => Section switch
    {
        "dcheap" => "sortBy=Deal%20Rating&upperPrice=5",
        "dbig" => "sortBy=Savings&steamRating=70",
        "daaa" => "sortBy=Deal%20Rating&AAA=1",
        "dwish" => "steamAppID=" + string.Join(",", Host.WishlistGames.Where(g => g.AppId > 0).Select(g => g.AppId).Take(60)),
        "dstore" => "sortBy=Deal%20Rating&storeID=" + Uri.EscapeDataString(_dealStore),
        _ => "sortBy=Deal%20Rating",
    };

    private void Deals()
    {
        var query = DealQuery;
        Title(Section switch
        {
            "dcheap" => "Under $5", "dbig" => "Biggest discounts", "daaa" => "Big games on sale", "dwish" => "Your wishlist on sale",
            "dstore" => "Deals by store", _ => "Best deals",
        }, "Prices in US dollars");
        if (Section == "dstore")
        {
            var name = _stores.FirstOrDefault(s => s.Id == _dealStore)?.Name ?? "Steam";
            Page.Children.Add(Option("o:store", "Store", name, () => _ = PickStoreAsync()));
        }
        if (!_deals.TryGetValue(query, out var deals))
        {
            Note("Looking for deals…");
            if (_loading.Add(query))
                _ = Task.Run(() => Labs.DealsAsync(query, Cts.Token)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
                {
                    _loading.Remove(query);
                    _deals[query] = t.IsCompletedSuccessfully ? t.Result : null;
                    if (Host.IsTop(this) && DealQuery == query) Rebuild(FocusTo.Keep);
                }));
            return;
        }
        if (deals == null) { Note("The deals couldn't be loaded. Press Y to try again."); return; }
        if (deals.Count == 0) { Note(Section == "dwish" ? "Nothing on your wishlist is on sale right now." : "No deals found."); return; }
        Note(Section switch
        {
            "dwish" => "The cheapest price for each game on your Steam wishlist, on whichever store has it lowest.",
            "dbig" => "The biggest price cuts on games Steam players rate well.",
            _ => "Deals on Steam, GOG, Humble, Fanatical, Green Man Gaming, Epic and more, from CheapShark. The same game on several stores shows the cheapest.",
        });
        var wrap = Tiles();
        foreach (var d in deals)
        {
            var deal = d;
            var badges = new List<UIElement> { Pill($"-{d.Savings}%") };
            if (d.Sale <= 0) badges.Insert(0, Pill("Free"));
            if (Labs.Owned(d.Title, Host.AllGames) != null) badges.Add(Pill("In your library", false));
            string store = _stores.FirstOrDefault(s => s.Id == d.StoreId)?.Name ?? "";
            wrap.Children.Add(Tile("d:" + d.Id, 280, 106, null, d.Thumb, d.Title,
                $"{Labs.Money(d.Sale)}  (was {Labs.Money(d.Normal)})" + (store.Length > 0 ? "  ·  " + store : ""), () => OpenDeal(deal), null, badges, "\uE719"));
        }
    }

    private async Task PickStoreAsync()
    {
        if (_stores.Count == 0) _stores = await Labs.DealStoresAsync(Cts.Token);
        if (_stores.Count == 0) return;
        var menu = new MenuScreen("Store", null, _stores.Select(s => s.Name).ToArray());
        Host.Push(menu);
        int c = await menu.Result;
        if (c < 0) return;
        _dealStore = _stores[c].Id;
        Labs.Put("free.dealStore", _dealStore);
        Rebuild(FocusTo.Keep);
    }

    private async void OpenDeal(Labs.Deal d)
    {
        string store = _stores.FirstOrDefault(s => s.Id == d.StoreId)?.Name ?? "the store";
        var lines = new List<string> { $"{Labs.Money(d.Sale)} on {store}, down {d.Savings}% from {Labs.Money(d.Normal)}." };
        if (d.SteamRating.Length > 0) lines.Add($"Steam reviews: {d.SteamRating} ({d.SteamPercent}% positive).");
        if (d.Metacritic > 0) lines.Add($"Metacritic: {d.Metacritic}.");
        if (d.Released is DateTime r) lines.Add("Released " + r.ToString("d MMM yyyy") + ".");
        var owned = Labs.Owned(d.Title, Host.AllGames);
        if (owned != null) lines.Add("You already have a game called that.");
        var options = new List<string> { "Open the deal on " + store };
        if (d.SteamAppId > 0) options.Add("See it on Steam");
        options.Add("Cancel");
        int c = await Host.AskAsync(d.Title, string.Join("\n", lines), options.ToArray());
        if (c == 0) ProcessUtil.Open(d.Url);
        else if (c == 1 && d.SteamAppId > 0) ProcessUtil.Open("https://store.steampowered.com/app/" + d.SteamAppId);
    }
}
