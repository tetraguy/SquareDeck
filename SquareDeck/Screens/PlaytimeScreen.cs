using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Your play time as a dashboard: headline numbers, the week as a bar chart (LB / RB for other weeks), a twelve-week
/// trend, a calendar heat map, most-played games with their covers and a share ring, genres, achievements by month,
/// when you play (time of day and day of week), and personal records. Every card can be landed on with the controller,
/// so the page scrolls as you move down it.
/// </summary>
public sealed class PlaytimeScreen : ListScreen
{
    private int _weeksBack; // 0 = this week
    private Dictionary<string, string>? _genres; // game key -> its first genre, filled in from the Steam store
    private const double CardWidth = 940;

    public PlaytimeScreen() : base("Your play time", "{Select}  Details      {LB}{RB}  Week      {Back}  Back")
    {
        // Switch 2 style: the cards, bars and tracks take the Switch 2 card color.
        if (Switch2Pages.ListPage(this))
        {
            var card = new SolidColorBrush(Switch2Settings.Card);
            card.Freeze();
            Resources["TileBrush"] = card;
        }
    }

    public override bool OnAction(NavAction action)
    {
        if (action is NavAction.LB or NavAction.RB)
        {
            _weeksBack = Math.Clamp(_weeksBack + (action == NavAction.LB ? 1 : -1), 0, 52);
            Rebuild();
            return true;
        }
        return base.OnAction(action);
    }

    // ---------------------------------------------------------------- building blocks

    private static Brush Accent => (Brush)Application.Current.Resources["AccentBrush"];
    private static Color AccentColor => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    /// <summary>A card: a focusable panel so the controller can land on it (and the page scrolls to it).</summary>
    private Button Card(UIElement content, Action? press = null, double? width = null)
    {
        var button = new Button
        {
            Style = Ui.StyleOf("RowButton"), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Width = width ?? double.NaN, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(22, 18, 22, 18)
        };
        if (press != null) button.Click += (_, _) => press();
        return button;
    }

    private static TextBlock Label(string text, double size = 13, string brush = "SubtleBrush", bool bold = false)
    {
        var t = Ui.Text(text, size, brush, bold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static StackPanel Titled(string title, string? subtitle = null)
    {
        var s = new StackPanel();
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var t = Label(title.ToUpperInvariant(), 13, "AccentBrush", true);
        row.Children.Add(t);
        if (subtitle != null)
        {
            var sub = Label(subtitle, 13);
            sub.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(sub, Dock.Right);
            row.Children.Insert(0, sub);
        }
        s.Children.Add(row);
        return s;
    }

    private static string Hours(double seconds)
    {
        double h = seconds / 3600;
        return h >= 100 ? $"{h:0}h" : h >= 1 ? $"{h:0.#}h" : $"{seconds / 60:0}m";
    }

    private static void Grow(FrameworkElement bar, double to, int delay, bool vertical)
    {
        if (Theme.ReduceMotion) { if (vertical) bar.Height = to; else bar.Width = to; return; }
        var a = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        bar.BeginAnimation(vertical ? HeightProperty : WidthProperty, a);
    }

    // ---------------------------------------------------------------- the page

    protected override void Build()
    {
        var store = Host.Playtime;
        var now = DateTime.Now;
        var allTime = store.AllTime();
        double total = allTime.Sum(g => g.Seconds);
        var weekStart = PlaytimeStore.WeekStart(now).AddDays(-7 * _weeksBack);
        double thisWeek = Enumerable.Range(0, 7).Sum(i => store.SecondsOn(weekStart.AddDays(i)));
        double lastWeek = Enumerable.Range(0, 7).Sum(i => store.SecondsOn(weekStart.AddDays(i - 7)));
        var sessions = store.Sessions;
        var unlocks = Host.Achievements.Feed;

        if (total < 1 && sessions.Count == 0)
        {
            AddNote("Your play time shows up here after your first session. SquareDeck measures it while it's open, for every game it can see running.");
            return;
        }

        Rows.Children.Add(Headline(total, thisWeek, lastWeek, allTime.Count, sessions, unlocks.Count));
        Rows.Children.Add(Card(WeekChart(store, weekStart, thisWeek), () => _ = PickWeekAsync()));
        Rows.Children.Add(Card(Trend(store, now)));
        Rows.Children.Add(Card(HeatMap(store, now)));
        if (allTime.Count > 0) Rows.Children.Add(Card(MostPlayed(allTime, total)));
        Rows.Children.Add(Card(Genres(allTime)));
        Rows.Children.Add(Card(Habits(sessions)));
        Rows.Children.Add(Card(Achievements(unlocks, now)));
        Rows.Children.Add(Card(Records(store, sessions)));
        AddNote("Measured by SquareDeck while it's open, for every game it can see running. Steam's own lifetime totals are on each game's details page.");
        if (_genres == null) _ = LoadGenresAsync(allTime);
    }

    private async Task PickWeekAsync()
    {
        int choice = await Host.AskAsync("Which week?", "LB and RB also step through the weeks.", "This week", "Last week", "2 weeks ago", "3 weeks ago", "4 weeks ago");
        if (choice < 0) return;
        _weeksBack = choice;
        Rebuild();
    }

    /// <summary>Four big numbers across the top.</summary>
    private UIElement Headline(double total, double week, double lastWeek, int games, List<SessionEntry> sessions, int unlocks)
    {
        var grid = new UniformGridPanel(4);
        string delta = lastWeek < 60 ? (week > 0 ? "new this week" : "nothing yet") :
            week >= lastWeek ? $"▲ {(week - lastWeek) / lastWeek:P0} vs last week" : $"▼ {(lastWeek - week) / lastWeek:P0} vs last week";
        double average = sessions.Count > 0 ? sessions.Average(s => s.Seconds) : 0;
        grid.Add(Stat("", Hours(total), "played in all", $"{sessions.Count} sessions", true));
        grid.Add(Stat("", Hours(week), _weeksBack == 0 ? "this week" : "that week", delta));
        grid.Add(Stat("", games.ToString(), games == 1 ? "game played" : "games played", average > 0 ? $"{Hours(average)} average session" : ""));
        grid.Add(Stat("", unlocks.ToString(), "achievements", "unlocked since SquareDeck started tracking"));
        return grid.Panel;
    }

    private Button Stat(string glyph, string value, string label, string detail, bool hero = false)
    {
        var s = new StackPanel();
        var icon = Ui.Glyph(glyph, 22, "AccentBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(icon);
        var v = Ui.Text(value, hero ? 40 : 34, "FgBrush", true);
        v.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(v);
        s.Children.Add(Label(label, 15, "FgBrush"));
        if (detail.Length > 0)
        {
            var d = Label(detail, 12);
            d.Margin = new Thickness(0, 4, 0, 0);
            d.TextWrapping = TextWrapping.Wrap;
            d.TextTrimming = TextTrimming.None;
            d.MaxHeight = 34;
            s.Children.Add(d);
        }
        var card = Card(s);
        card.Margin = new Thickness(0, 0, 10, 12);
        card.MinHeight = 170;
        card.VerticalContentAlignment = VerticalAlignment.Top;
        if (hero)
        {
            var a = AccentColor;
            card.Background = new LinearGradientBrush(Color.FromArgb(0x55, a.R, a.G, a.B), Color.FromArgb(0x10, a.R, a.G, a.B), 45);
        }
        return card;
    }

    /// <summary>Seven tall bars, Monday to Sunday, with today marked.</summary>
    private UIElement WeekChart(PlaytimeStore store, DateTime start, double total)
    {
        string label = _weeksBack switch { 0 => "This week", 1 => "Last week", _ => _weeksBack + " weeks ago" };
        var s = Titled(label, $"{start:MMM d} – {start.AddDays(6):MMM d}  ·  {PlaytimeStore.Format(total)}");
        var perDay = Enumerable.Range(0, 7).Select(i => store.SecondsOn(start.AddDays(i))).ToList();
        double top = Math.Max(perDay.Max(), 1800);
        var bars = new Grid { Height = 190 };
        for (int i = 0; i < 7; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var day = start.AddDays(i);
            var col = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
            var name = Label(day.ToString("ddd"), 13, day.Date == DateTime.Today ? "AccentBrush" : "SubtleBrush", day.Date == DateTime.Today);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(name, Dock.Bottom);
            col.Children.Add(name);
            var value = Label(perDay[i] >= 60 ? Hours(perDay[i]) : "", 12, "FgBrush");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(value, Dock.Top);
            col.Children.Add(value);
            var holder = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            var track = new Border { CornerRadius = new CornerRadius(8), Opacity = 0.5 };
            track.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            holder.Children.Add(track);
            var bar = new Border { CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            var a = AccentColor;
            bar.Background = new LinearGradientBrush(a, Color.FromArgb(0xA0, a.R, a.G, a.B), 90);
            holder.Children.Add(bar);
            col.Children.Add(holder);
            Grid.SetColumn(col, i);
            bars.Children.Add(col);
            double h = 140 * perDay[i] / top;
            Grow(bar, Math.Max(perDay[i] > 0 ? 6 : 0, h), i * 50, vertical: true);
        }
        s.Children.Add(bars);
        return s;
    }

    /// <summary>The last twelve weeks as a smooth area chart.</summary>
    private UIElement Trend(PlaytimeStore store, DateTime now)
    {
        var start = PlaytimeStore.WeekStart(now).AddDays(-7 * 11);
        var weeks = Enumerable.Range(0, 12).Select(w => Enumerable.Range(0, 7).Sum(d => store.SecondsOn(start.AddDays(w * 7 + d)))).ToList();
        double top = Math.Max(weeks.Max(), 3600);
        var s = Titled("Last 12 weeks", $"best week {Hours(weeks.Max())}  ·  average {Hours(weeks.Average())}");
        const double w = CardWidth - 44, h = 130;
        var canvas = new Canvas { Width = w, Height = h + 22, ClipToBounds = true };
        var pts = weeks.Select((v, i) => new Point(i * w / 11, h - h * v / top * 0.92)).ToList();
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(0, h), true, true);
            foreach (var p in pts) g.LineTo(p, true, true);
            g.LineTo(new Point(w, h), true, false);
        }
        var a = AccentColor;
        canvas.Children.Add(new Path { Data = area, Fill = new LinearGradientBrush(Color.FromArgb(0x70, a.R, a.G, a.B), Color.FromArgb(0x05, a.R, a.G, a.B), 90) });
        var line = new Polyline { Stroke = Accent, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, Points = new PointCollection(pts) };
        canvas.Children.Add(line);
        for (int i = 0; i < pts.Count; i++)
        {
            var dot = new Ellipse { Width = 9, Height = 9, Fill = i == pts.Count - 1 ? Brushes.White : Accent };
            Canvas.SetLeft(dot, Math.Clamp(pts[i].X - 4.5, 0, w - 9)); Canvas.SetTop(dot, pts[i].Y - 4.5);
            canvas.Children.Add(dot);
            if (i % 2 == 1 || i == 11)
            {
                var t = Label(start.AddDays(i * 7).ToString("MMM d"), 11);
                Canvas.SetLeft(t, Math.Clamp(pts[i].X - 20, 0, w - 44)); Canvas.SetTop(t, h + 6);
                canvas.Children.Add(t);
            }
        }
        if (!Theme.ReduceMotion)
        {
            canvas.OpacityMask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, 0) } };
            var mask = (LinearGradientBrush)canvas.OpacityMask;
            mask.GradientStops[1].BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
            mask.GradientStops[2].BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.02, 1.02, TimeSpan.FromMilliseconds(900)));
        }
        s.Children.Add(canvas);
        return s;
    }

    /// <summary>Every day of the last 26 weeks as a square, darker the more you played.</summary>
    private UIElement HeatMap(PlaytimeStore store, DateTime now)
    {
        const int weeks = 26;
        var first = PlaytimeStore.WeekStart(now).AddDays(-7 * (weeks - 1));
        var values = new double[weeks, 7];
        double top = 1;
        int active = 0;
        for (int w = 0; w < weeks; w++)
            for (int d = 0; d < 7; d++)
            {
                var day = first.AddDays(w * 7 + d);
                values[w, d] = day > now ? -1 : store.SecondsOn(day);
                top = Math.Max(top, values[w, d]);
                if (values[w, d] >= 60) active++;
            }
        var s = Titled("Activity", $"{active} days played in the last six months");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dayNames = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        foreach (var n in new[] { "Mon", "", "Wed", "", "Fri", "", "Sun" })
        {
            var t = Label(n, 11);
            t.Height = 26;
            dayNames.Children.Add(t);
        }
        row.Children.Add(dayNames);
        var a = AccentColor;
        for (int w = 0; w < weeks; w++)
        {
            var col = new StackPanel();
            for (int d = 0; d < 7; d++)
            {
                double v = values[w, d];
                var cell = new Border { Width = 22, Height = 22, Margin = new Thickness(2), CornerRadius = new CornerRadius(5) };
                if (v < 0) cell.Background = Brushes.Transparent;
                else if (v < 60) cell.SetResourceReference(Border.BackgroundProperty, "TileBrush");
                else
                {
                    double k = 0.25 + 0.75 * Math.Sqrt(v / top);
                    cell.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * k), a.R, a.G, a.B));
                }
                if (v >= 0) cell.ToolTip = $"{first.AddDays(w * 7 + d):ddd MMM d}: {PlaytimeStore.Format(Math.Max(0, v))}";
                col.Children.Add(cell);
            }
            row.Children.Add(col);
        }
        s.Children.Add(row);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        legend.Children.Add(Label("Less ", 11));
        foreach (var k in new[] { 0.0, 0.35, 0.6, 0.8, 1.0 })
        {
            var c = new Border { Width = 14, Height = 14, Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(3) };
            if (k == 0) c.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            else c.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * k), a.R, a.G, a.B));
            legend.Children.Add(c);
        }
        legend.Children.Add(Label(" More", 11));
        s.Children.Add(legend);
        return s;
    }

    /// <summary>Top games: covers, bars and hours, beside a ring showing each one's share.</summary>
    private UIElement MostPlayed(List<(string Key, string Name, double Seconds)> allTime, double total)
    {
        var s = Titled("Most played", "all time");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var list = new StackPanel();
        var top = allTime.Take(6).ToList();
        var palette = SharePalette();
        for (int i = 0; i < top.Count; i++)
        {
            var (key, name, seconds) = top[i];
            var game = Host.AllGames.FirstOrDefault(g => g.Key == key);
            var row = new Grid { Margin = new Thickness(0, 0, 18, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var rank = Label((i + 1).ToString(), 20, i == 0 ? "AccentBrush" : "SubtleBrush", true);
            rank.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(rank);
            var cover = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0, 0, 0, 4), BorderBrush = new SolidColorBrush(palette[i % palette.Length]) };
            if (game?.TileImage is ImageSource art) cover.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            else cover.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            Grid.SetColumn(cover, 1);
            row.Children.Add(cover);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel();
            var hrs = Label($"{Hours(seconds)}  ·  {seconds / Math.Max(1, total):P0}", 14, "SubtleBrush");
            DockPanel.SetDock(hrs, Dock.Right);
            head.Children.Add(hrs);
            head.Children.Add(Label(name, 17, "FgBrush", true));
            info.Children.Add(head);
            var track = new Grid { Height = 8, Margin = new Thickness(0, 7, 0, 0) };
            var bg = new Border { CornerRadius = new CornerRadius(4) };
            bg.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            track.Children.Add(bg);
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(palette[i % palette.Length]) };
            track.Children.Add(fill);
            info.Children.Add(track);
            Grid.SetColumn(info, 2);
            row.Children.Add(info);
            list.Children.Add(row);
            Grow(fill, 430 * seconds / top[0].Seconds, i * 60, vertical: false);
        }
        grid.Children.Add(list);
        var ring = Donut(top.Select(t => t.Seconds).Append(Math.Max(0, total - top.Sum(t => t.Seconds))).ToList(), Hours(total), "total", palette);
        Grid.SetColumn(ring, 1);
        grid.Children.Add(ring);
        s.Children.Add(grid);
        return s;
    }

    private static Color[] SharePalette()
    {
        var a = AccentColor;
        return new[] { a, Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromArgb(0x50, 0x80, 0x80, 0x80) };
    }

    /// <summary>A ring chart: each value a slice, the last one in grey ("everything else").</summary>
    private static UIElement Donut(List<double> values, string center, string caption, Color[] palette)
    {
        const double size = 210, thick = 30;
        var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        double sum = Math.Max(values.Sum(), 1), angle = -90;
        var c = new Point(size / 2, size / 2);
        double r = size / 2 - thick / 2;
        for (int i = 0; i < values.Count; i++)
        {
            double sweep = 360 * values[i] / sum;
            if (sweep < 0.5) continue;
            if (sweep >= 359.9) sweep = 359.9;
            Point P(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            var fig = new PathFigure { StartPoint = P(angle) };
            fig.Segments.Add(new ArcSegment(P(angle + sweep - 1.2), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
            var color = i == values.Count - 1 ? palette[^1] : palette[i % (palette.Length - 1)];
            canvas.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Stroke = new SolidColorBrush(color), StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Flat });
            angle += sweep;
        }
        var label = new StackPanel { Width = size, VerticalAlignment = VerticalAlignment.Center };
        var big = Ui.Text(center, 30, "FgBrush", true); big.HorizontalAlignment = HorizontalAlignment.Center;
        var small = Label(caption, 13); small.HorizontalAlignment = HorizontalAlignment.Center;
        label.Children.Add(big); label.Children.Add(small);
        Canvas.SetTop(label, size / 2 - 28);
        canvas.Children.Add(label);
        if (!Theme.ReduceMotion)
        {
            var spin = new RotateTransform(-40, size / 2, size / 2);
            canvas.RenderTransform = spin;
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-40, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
        }
        return canvas;
    }

    /// <summary>Play time by genre (from the Steam store, for the games played most).</summary>
    private UIElement Genres(List<(string Key, string Name, double Seconds)> allTime)
    {
        var s = Titled("Genres", "by play time");
        if (_genres == null) { s.Children.Add(Label("Looking up genres…", 15)); return s; }
        var byGenre = allTime.Where(g => _genres.ContainsKey(g.Key))
            .GroupBy(g => _genres[g.Key]).Select(x => (Genre: x.Key, Seconds: x.Sum(g => g.Seconds)))
            .OrderByDescending(x => x.Seconds).Take(6).ToList();
        if (byGenre.Count == 0) { s.Children.Add(Label("Genres show up for Steam games once they've been played.", 15)); return s; }
        double sum = byGenre.Sum(g => g.Seconds);
        var palette = SharePalette();
        // One stacked bar across the card, then a legend of chips.
        var bar = new Grid { Height = 26, ClipToBounds = true };
        bar.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth - 44, 26), 13, 13);
        for (int i = 0; i < byGenre.Count; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(byGenre[i].Seconds, GridUnitType.Star) });
            var part = new Border { Background = new SolidColorBrush(palette[i % (palette.Length - 1)]), Margin = new Thickness(0, 0, i < byGenre.Count - 1 ? 2 : 0, 0) };
            Grid.SetColumn(part, i);
            bar.Children.Add(part);
        }
        s.Children.Add(bar);
        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        for (int i = 0; i < byGenre.Count; i++)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 6) };
            chip.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(palette[i % (palette.Length - 1)]), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(Label(byGenre[i].Genre, 15, "FgBrush", true));
            chip.Children.Add(Label($"  {byGenre[i].Seconds / sum:P0}", 14));
            chips.Children.Add(chip);
        }
        s.Children.Add(chips);
        return s;
    }

    private async Task LoadGenresAsync(List<(string Key, string Name, double Seconds)> allTime)
    {
        var found = new Dictionary<string, string>();
        foreach (var (key, _, _) in allTime.Take(20))
        {
            var game = Host.AllGames.FirstOrDefault(g => g.Key == key);
            if (game is not { AppId: > 0 }) continue;
            try
            {
                var details = await SteamStoreClient.GetDetailsAsync(game.AppId, CancellationToken.None);
                var first = details?.Genres.Split(',').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
                if (first != null) found[key] = first;
            }
            catch (Exception) { }
        }
        _genres = found;
        if (Host.IsTop(this)) Rebuild();
    }

    /// <summary>When you play: time of day (24 little bars) and favourite days.</summary>
    private UIElement Habits(List<SessionEntry> sessions)
    {
        var s = Titled("When you play", sessions.Count > 0 ? $"{sessions.Count} sessions" : null);
        if (sessions.Count == 0) { s.Children.Add(Label("Habits show up after a few sessions.", 15)); return s; }
        var hours = new double[24];
        var days = new double[7];
        foreach (var e in sessions)
        {
            hours[e.Start.Hour] += e.Seconds;
            days[((int)e.Start.DayOfWeek + 6) % 7] += e.Seconds;
        }
        int peakHour = Array.IndexOf(hours, hours.Max());
        string part = peakHour switch { >= 5 and < 12 => "a morning player", >= 12 and < 17 => "an afternoon player", >= 17 and < 22 => "an evening player", _ => "a night owl" };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(Label($"You're {part}: most play starts around {DateTime.Today.AddHours(peakHour):h tt}.", 16, "FgBrush", true));
        var chart = new Grid { Height = 100, Margin = new Thickness(0, 12, 0, 0) };
        double top = Math.Max(hours.Max(), 1);
        for (int h = 0; h < 24; h++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom, Background = h == peakHour ? Accent : null };
            if (h != peakHour) { bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); bar.Opacity = 0.45; }
            Grid.SetColumn(bar, h);
            chart.Children.Add(bar);
            Grow(bar, Math.Max(3, 100 * hours[h] / top), h * 15, vertical: true);
        }
        left.Children.Add(chart);
        var axis = new Grid();
        foreach (var (h, text) in new[] { (0, "12am"), (6, "6am"), (12, "12pm"), (18, "6pm") })
        {
            var t = Label(text, 11);
            t.Margin = new Thickness(h / 24.0 * 540, 4, 0, 0);
            axis.Children.Add(t);
        }
        left.Children.Add(axis);
        grid.Children.Add(left);

        var right = new StackPanel { Margin = new Thickness(30, 0, 0, 0) };
        string[] names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        int bestDay = Array.IndexOf(days, days.Max());
        right.Children.Add(Label($"Favourite day: {DateTime.Today.AddDays(bestDay - ((int)DateTime.Today.DayOfWeek + 6) % 7):dddd}", 16, "FgBrush", true));
        double topDay = Math.Max(days.Max(), 1);
        for (int d = 0; d < 7; d++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var n = Label(names[d], 12, d == bestDay ? "AccentBrush" : "SubtleBrush", d == bestDay);
            n.Width = 36;
            row.Children.Add(n);
            var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.Opacity = d == bestDay ? 1 : 0.5;
            row.Children.Add(bar);
            right.Children.Add(row);
            Grow(bar, 220 * days[d] / topDay, d * 40, vertical: false);
        }
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        s.Children.Add(grid);
        return s;
    }

    /// <summary>Achievements unlocked per month for six months, and the latest few.</summary>
    private UIElement Achievements(List<UnlockEntry> unlocks, DateTime now)
    {
        var s = Titled("Achievements", unlocks.Count > 0 ? $"{unlocks.Count} unlocked" : null);
        if (unlocks.Count == 0) { s.Children.Add(Label("Achievements you unlock in Steam games show up here.", 15)); return s; }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var months = Enumerable.Range(0, 6).Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(i - 5)).ToList();
        var counts = months.Select(m => unlocks.Count(u => u.When.Year == m.Year && u.When.Month == m.Month)).ToList();
        int top = Math.Max(1, counts.Max());
        var chart = new Grid { Height = 140 };
        for (int i = 0; i < 6; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
            var name = Label(months[i].ToString("MMM"), 12);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(name, Dock.Bottom);
            col.Children.Add(name);
            var count = Label(counts[i] > 0 ? counts[i].ToString() : "", 12, "FgBrush", true);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(count, Dock.Top);
            col.Children.Add(count);
            var bar = new Border { CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, 6), Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)) };
            col.Children.Add(bar);
            Grid.SetColumn(col, i);
            chart.Children.Add(col);
            Grow(bar, Math.Max(counts[i] > 0 ? 4 : 0, 90.0 * counts[i] / top), i * 60, vertical: true);
        }
        grid.Children.Add(chart);
        var latest = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
        latest.Children.Add(Label("Latest", 12, "SubtleBrush", true));
        foreach (var u in unlocks.OrderByDescending(u => u.When).Take(4))
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0) };
            if (u.Icon.StartsWith("http") && Uri.TryCreate(u.Icon, UriKind.Absolute, out var uri))
                icon.Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(uri)) { Stretch = Stretch.UniformToFill };
            else icon.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(u.Name, 15, "FgBrush", true));
            text.Children.Add(Label($"{u.Game}  ·  {u.When:MMM d}", 12));
            row.Children.Add(text);
            latest.Children.Add(row);
        }
        Grid.SetColumn(latest, 1);
        grid.Children.Add(latest);
        s.Children.Add(grid);
        return s;
    }

    /// <summary>Records as trophy tiles.</summary>
    private UIElement Records(PlaytimeStore store, List<SessionEntry> sessions)
    {
        var s = Titled("Personal records");
        var longest = store.LongestSession();
        var streak = store.LongestStreak();
        var month = store.BestMonth();
        // Current streak: days in a row up to today (or up to yesterday, if today hasn't been played yet).
        int current = 0;
        var day = store.SecondsOn(DateTime.Today) >= 60 ? DateTime.Today : DateTime.Today.AddDays(-1);
        while (store.SecondsOn(day) >= 60 && current < 1000) { current++; day = day.AddDays(-1); }
        var tiles = new UniformGridPanel(4);
        tiles.Add(Trophy("", longest != null ? Hours(longest.Seconds) : "—", "Longest session", longest != null ? $"{longest.Name}, {longest.Start:MMM d}" : ""));
        tiles.Add(Trophy("", streak.Days > 0 ? $"{streak.Days} days" : "—", "Longest streak", streak.Days > 0 ? $"ended {streak.End:MMM d}" : ""));
        tiles.Add(Trophy("", current > 0 ? $"{current} days" : "0", "Current streak", current > 0 ? "keep it going" : "play today to start one"));
        tiles.Add(Trophy("", month is { } m ? Hours(m.Seconds) : "—", "Best month", month?.Month ?? ""));
        s.Children.Add(tiles.Panel);
        return s;
    }

    private static UIElement Trophy(string glyph, string value, string label, string detail)
    {
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 10, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var s = new StackPanel();
        var icon = Ui.Glyph(glyph, 20, "AccentBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(icon);
        var v = Ui.Text(value, 24, "FgBrush", true);
        v.Margin = new Thickness(0, 6, 0, 0);
        s.Children.Add(v);
        s.Children.Add(Label(label, 13, "FgBrush"));
        s.Children.Add(Label(detail, 11));
        b.Child = s;
        return b;
    }

    /// <summary>A row of equal columns.</summary>
    private sealed class UniformGridPanel
    {
        public readonly Grid Panel = new();
        private readonly int _columns;
        public UniformGridPanel(int columns)
        {
            _columns = columns;
            for (int i = 0; i < columns; i++) Panel.ColumnDefinitions.Add(new ColumnDefinition());
        }
        public void Add(UIElement e)
        {
            Grid.SetColumn(e, Panel.Children.Count % _columns);
            Panel.Children.Add(e);
        }
    }
}
