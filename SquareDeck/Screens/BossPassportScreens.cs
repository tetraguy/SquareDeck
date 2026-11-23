using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

// ====================================================================================================== backlog boss

/// <summary>
/// The Backlog Boss: your unfinished games as a monster whose body is a mosaic of their covers. Its health is the
/// hours left to finish them (How Long To Beat's main story, less what you've played). Coming back after playing,
/// the damage you dealt lands: the boss flinches, the bar drains and the hours fly off. Weak points (the games
/// closest to done) are a press away from playing; finishing one is a critical hit. The rest of the backlog is its
/// army, and finished games are your trophies.
/// </summary>
public sealed class BossScreen : Screen
{
    private static readonly Color Ember = Color.FromRgb(0xFF, 0x8A, 0x3D), Blood = Color.FromRgb(0xE0, 0x2E, 0x4A);
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 0, 30) };
    private readonly Random _rand = new(7);
    private Button? _first;

    public BossScreen()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = _page };
        SetPage("Backlog Boss", scroll, "{Select}  Attack (play)      {Back}  Back", wide: true);
    }

    public override void OnShown() => Build();

    public override void FocusDefault()
    {
        if (_first != null) _first.Focus();
        else base.FocusDefault();
    }

    private void Build()
    {
        _page.Children.Clear();
        var b = Labs.BacklogBoss(Host.AllGames, Host.Playtime, Host.Library);
        if (b.Games == 0 && b.Finished.Count == 0)
        {
            var none = Ui.Text("No boss yet. It's made from installed games that aren't marked Finished or Dropped and have a How Long To Beat time. " +
                               "Open a few games' pages so their times are looked up, then come back to fight.", 18, "SubtleBrush");
            none.TextWrapping = TextWrapping.Wrap;
            none.MaxWidth = 820;
            none.Margin = new Thickness(0, 60, 0, 0);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.TextAlignment = TextAlignment.Center;
            _page.Children.Add(none);
            return;
        }

        double hp = b.MaxHours <= 0 ? 0 : Math.Clamp(b.HoursLeft / b.MaxHours, 0, 1);
        double seen = double.TryParse(Labs.Get("boss.seen"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : b.HoursLeft;
        double hit = Math.Max(0, seen - b.HoursLeft);
        Labs.Put("boss.seen", b.HoursLeft.ToString(System.Globalization.CultureInfo.InvariantCulture));
        double seenHp = b.MaxHours <= 0 ? 0 : Math.Clamp(seen / b.MaxHours, 0, 1);

        _page.Children.Add(Arena(b, hp, Math.Max(hp, seenHp), hit));
        _page.Children.Add(Stats(b));

        if (b.Minions.Count > 0)
        {
            _page.Children.Add(Heading("Weak points", "Finish one of these to land a critical hit. Press to play"));
            var weak = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var m in b.Minions.Take(3)) weak.Children.Add(WeakPoint(m));
            _page.Children.Add(weak);
        }
        if (b.Minions.Count > 3)
        {
            _page.Children.Add(Heading($"Its army  ·  {b.Minions.Count - 3}", "The rest of your unfinished games, closest to done first"));
            var army = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var m in b.Minions.Skip(3).Take(60)) army.Children.Add(Soldier(m));
            _page.Children.Add(army);
        }
        if (b.Finished.Count > 0)
        {
            _page.Children.Add(Heading($"Trophies  ·  {b.Finished.Count}", "Games you've beaten"));
            var trophies = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var g in b.Finished.Take(40)) trophies.Children.Add(Trophy(g));
            _page.Children.Add(trophies);
        }
        FocusLater(_first);
    }

    // ---------------------------------------------------------------- the arena

    private FrameworkElement Arena(Labs.Boss b, double hp, double fromHp, double hit)
    {
        bool defeated = hp <= 0.001;
        var arena = new Grid { Height = 470, Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true };
        arena.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(26),
            Background = new RadialGradientBrush(Color.FromRgb(0x4A, 0x10, 0x2A), Color.FromRgb(0x0C, 0x05, 0x12)) { RadiusX = 0.75, RadiusY = 0.9, Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.65) }
        });
        // The floor: a glowing ellipse the boss hovers over.
        var floor = new Ellipse { Width = 520, Height = 70, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 36),
            Fill = new RadialGradientBrush(Color.FromArgb(0x90, Blood.R, Blood.G, Blood.B), Color.FromArgb(0, 0, 0, 0)) };
        arena.Children.Add(floor);
        arena.Children.Add(Embers());
        arena.Clip = new RectangleGeometry(new Rect(0, 0, 4000, 470), 26, 26);

        // The name plate and the health bar.
        var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 22, 0, 0) };
        string title = defeated ? "Defeated" : hp > 0.75 ? "The Pile of Shame" : hp > 0.4 ? "The Wounded Pile" : hp > 0.1 ? "The Crumbling Pile" : "On its last legs";
        var lv = Label($"LV {b.Games}  ·  BACKLOG BOSS", 13, Color.FromRgb(0xFF, 0xB3, 0x8A), bold: true);
        lv.HorizontalAlignment = HorizontalAlignment.Center;
        top.Children.Add(lv);
        var name = Label(title.ToUpperInvariant(), 34, Colors.White, bold: true);
        name.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        if (!Perf.Active) name.Effect = new DropShadowEffect { Color = Blood, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
        top.Children.Add(name);
        top.Children.Add(HealthBar(hp, fromHp, b));
        arena.Children.Add(top);

        // The boss itself.
        var boss = Monster(b, hp);
        boss.VerticalAlignment = VerticalAlignment.Bottom;
        boss.Margin = new Thickness(0, 0, 0, 56);
        arena.Children.Add(boss);

        if (defeated)
        {
            var win = Label("BOSS DEFEATED", 52, Colors.White, bold: true);
            win.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
            win.HorizontalAlignment = HorizontalAlignment.Center;
            win.VerticalAlignment = VerticalAlignment.Center;
            if (!Perf.Active) win.Effect = new DropShadowEffect { Color = Ember, BlurRadius = 30, ShadowDepth = 0 };
            arena.Children.Add(win);
            arena.Children.Add(Confetti());
            Host.Rumble(260);
        }
        else if (hit > 0.05 && !Theme.ReduceMotion)
        {
            // The damage since last time: a number flies off and the boss flinches.
            var dmg = Label("-" + Labs.Hours(hit), 40, Color.FromRgb(0xFF, 0xE0, 0x6A), bold: true);
            dmg.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
            dmg.HorizontalAlignment = HorizontalAlignment.Center;
            dmg.VerticalAlignment = VerticalAlignment.Center;
            dmg.Margin = new Thickness(260, 0, 0, 40);
            dmg.Opacity = 0;
            if (!Perf.Active) dmg.Effect = new DropShadowEffect { Color = Blood, BlurRadius = 12, ShadowDepth = 0 };
            var lift = new TranslateTransform();
            dmg.RenderTransform = lift;
            arena.Children.Add(dmg);
            var begin = TimeSpan.FromMilliseconds(650);
            dmg.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = begin,
                KeyFrames = { new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))), new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))) }
            });
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -90, TimeSpan.FromMilliseconds(1400)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var shake = new TranslateTransform();
            boss.RenderTransform = new TransformGroup { Children = { shake } };
            shake.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = begin,
                KeyFrames = { new LinearDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))), new LinearDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))),
                              new LinearDoubleKeyFrame(-9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))), new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))) }
            });
            _ = RumbleHitAsync();
        }
        return arena;
    }

    private async Task RumbleHitAsync()
    {
        await Task.Delay(650);
        Host.Rumble(90);
        await Task.Delay(140);
        Host.Rumble(50);
    }

    private FrameworkElement HealthBar(double hp, double fromHp, Labs.Boss b)
    {
        const double W = 760;
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var bar = new Grid { Width = W, Height = 22 };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(0xA0, 0x10, 0x04, 0x0A)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1.5) });
        // The white "chip" shows the damage just dealt, then drains down to the red.
        var chip = new Border { CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Width = W * fromHp, Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xF1, 0xD6)) };
        var fill = new Border { CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(0, W * hp), Background = new LinearGradientBrush(Blood, Ember, 0) };
        if (!Perf.Active) fill.Effect = new DropShadowEffect { Color = Blood, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 };
        bar.Children.Add(chip);
        bar.Children.Add(fill);
        for (int i = 1; i < 10; i++) bar.Children.Add(new Rectangle { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(W * i / 10, 4, 0, 4), Fill = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)) });
        if (fromHp > hp && !Theme.ReduceMotion)
            chip.BeginAnimation(WidthProperty, new DoubleAnimation(W * fromHp, W * hp, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(1100), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        else chip.Width = W * hp;
        box.Children.Add(bar);
        var text = Label($"{Labs.Hours(b.HoursLeft)} of {Labs.Hours(b.MaxHours)} health  ·  {b.Games} {(b.Games == 1 ? "game" : "games")}", 14, Color.FromRgb(0xFF, 0xD9, 0xC7), bold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 6, 0, 0);
        box.Children.Add(text);
        return box;
    }

    /// <summary>The boss: a horned silhouette filled with a mosaic of your unfinished games' covers, with glowing eyes.</summary>
    private FrameworkElement Monster(Labs.Boss b, double hp)
    {
        const double W = 320, H = 270;
        var root = new Grid { Width = W, Height = H, RenderTransformOrigin = new Point(0.5, 1) };
        var shape = Geometry.Parse("M 40,262 C 4,190 12,96 78,62 L 58,0 L 122,46 C 146,38 174,38 198,46 L 262,0 L 242,62 C 308,96 316,190 280,262 " +
                                   "Q 262,240 244,262 Q 226,240 208,262 Q 190,240 172,262 Q 154,240 136,262 Q 118,240 100,262 Q 82,240 64,262 Q 52,246 40,262 Z");

        // The mosaic of covers inside the silhouette.
        var mosaic = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Rows = 6, Width = W, Height = H };
        var covers = b.Minions.Select(m => m.Game.Cover).Where(c => c != null).ToList();
        for (int i = 0; i < 42; i++)
        {
            var c = covers.Count > 0 ? covers[i % covers.Count] : null;
            mosaic.Children.Add(c != null ? new Image { Source = c, Stretch = Stretch.UniformToFill } : new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x40)) });
        }
        var body = new Grid { Clip = shape };
        body.Children.Add(mosaic);
        // Tinted by its health: violet and angry, then burnt orange, then ash grey.
        var tint = hp > 0.6 ? Color.FromRgb(0x7A, 0x22, 0xC9) : hp > 0.25 ? Color.FromRgb(0xC2, 0x4B, 0x1E) : Color.FromRgb(0x50, 0x50, 0x58);
        body.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0x90, tint.R, tint.G, tint.B), Color.FromArgb(0xE8, 0x10, 0x04, 0x14), 90) });
        root.Children.Add(body);
        // Cracks as it weakens.
        if (hp < 0.66) root.Children.Add(new Path { Data = Geometry.Parse("M 120,90 L 138,128 L 126,150 L 146,196"), Stroke = new SolidColorBrush(Ember), StrokeThickness = 3, Opacity = 0.85 });
        if (hp < 0.33) root.Children.Add(new Path { Data = Geometry.Parse("M 222,110 L 204,142 L 218,170 L 196,214 M 84,170 L 104,186"), Stroke = new SolidColorBrush(Ember), StrokeThickness = 3, Opacity = 0.85 });
        var outline = new Path { Data = shape, Stroke = new SolidColorBrush(Color.FromArgb(0xD0, Blood.R, Blood.G, Blood.B)), StrokeThickness = 4 };
        if (!Perf.Active) outline.Effect = new DropShadowEffect { Color = Blood, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 };
        root.Children.Add(outline);

        // Eyes (they blink) and a jagged grin.
        var eyes = new Canvas { Width = W, Height = H, RenderTransformOrigin = new Point(0.5, 0.42) };
        var blink = new ScaleTransform(1, 1, W / 2, 112);
        eyes.RenderTransform = blink;
        foreach (double x in new[] { 112.0, 208.0 })
        {
            var glow = new Ellipse { Width = 46, Height = 30, Fill = new RadialGradientBrush(Color.FromRgb(0xFF, 0xF2, 0x8A), Color.FromRgb(0xFF, 0x7A, 0x1A)) };
            if (!Perf.Active) glow.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xA0, 0x30), BlurRadius = 22, ShadowDepth = 0 };
            Canvas.SetLeft(glow, x - 23);
            Canvas.SetTop(glow, 98);
            eyes.Children.Add(glow);
            var pupil = new Ellipse { Width = 10, Height = 22, Fill = Brushes.Black };
            Canvas.SetLeft(pupil, x - 5);
            Canvas.SetTop(pupil, 102);
            eyes.Children.Add(pupil);
        }
        root.Children.Add(eyes);
        root.Children.Add(new Path { Data = Geometry.Parse("M 110,176 L 126,192 L 142,178 L 160,194 L 178,178 L 194,192 L 210,176 Q 160,214 110,176 Z"), Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x02, 0x08)), Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xE7, 0xC2)), StrokeThickness = 2 });

        if (!Theme.ReduceMotion && hp > 0.001)
        {
            var breathe = new ScaleTransform(1, 1);
            var bob = new TranslateTransform();
            var group = new TransformGroup { Children = { breathe, bob } };
            var wrap = new Grid { RenderTransform = group, RenderTransformOrigin = new Point(0.5, 1) };
            wrap.Children.Add(root);
            breathe.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.035, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            breathe.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.985, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromSeconds(2.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            blink.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                KeyFrames = { new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))), new LinearDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.48))), new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.58))) }
            });
            return wrap;
        }
        if (hp <= 0.001) { root.Opacity = 0.35; root.RenderTransform = new RotateTransform(-8); }
        return root;
    }

    private FrameworkElement Embers()
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        if (Theme.ReduceMotion) return canvas;
        for (int i = 0; i < 26; i++)
        {
            double size = 3 + _rand.NextDouble() * 5;
            var e = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(i % 3 == 0 ? Color.FromRgb(0xFF, 0xD1, 0x6A) : Ember), Opacity = 0 };
            Canvas.SetLeft(e, 60 + _rand.NextDouble() * 1100);
            Canvas.SetTop(e, 470);
            canvas.Children.Add(e);
            var rise = new TranslateTransform();
            e.RenderTransform = rise;
            var d = TimeSpan.FromSeconds(4 + _rand.NextDouble() * 4);
            var start = TimeSpan.FromSeconds(_rand.NextDouble() * 6);
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -480, d) { BeginTime = start, RepeatBehavior = RepeatBehavior.Forever });
            rise.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-20, 20, TimeSpan.FromSeconds(1.5 + _rand.NextDouble())) { BeginTime = start, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            e.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = start, RepeatBehavior = RepeatBehavior.Forever, Duration = d,
                KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)), new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.15)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
            });
        }
        return canvas;
    }

    private FrameworkElement Confetti()
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        if (Theme.ReduceMotion) return canvas;
        var colors = new[] { Ember, Blood, Color.FromRgb(0xFF, 0xE0, 0x6A), Color.FromRgb(0x5E, 0xD6, 0xFF), Colors.White };
        for (int i = 0; i < 60; i++)
        {
            var r = new Rectangle { Width = 8, Height = 14, Fill = new SolidColorBrush(colors[i % colors.Length]), RenderTransformOrigin = new Point(0.5, 0.5) };
            Canvas.SetLeft(r, _rand.NextDouble() * 1200);
            Canvas.SetTop(r, -20);
            var spin = new RotateTransform();
            var fall = new TranslateTransform();
            r.RenderTransform = new TransformGroup { Children = { spin, fall } };
            var d = TimeSpan.FromSeconds(2.5 + _rand.NextDouble() * 2);
            var start = TimeSpan.FromSeconds(_rand.NextDouble() * 2);
            fall.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 520, d) { BeginTime = start, RepeatBehavior = RepeatBehavior.Forever });
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 720, d) { BeginTime = start, RepeatBehavior = RepeatBehavior.Forever });
            canvas.Children.Add(r);
        }
        return canvas;
    }

    // ---------------------------------------------------------------- around the arena

    private FrameworkElement Stats(Labs.Boss b)
    {
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
        void Stat(string glyph, string value, string label, Color color)
        {
            var card = new Border { CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(18, 14, 18, 14) };
            card.SetResourceReference(Border.BackgroundProperty, "TileBrush");
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 16, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            head.Children.Add(Ui.Text(label, 13, "SubtleBrush", bold: true));
            stack.Children.Add(head);
            var v = Ui.Text(value, 26, "FgBrush", bold: true);
            v.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(v);
            card.Child = stack;
            row.Children.Add(card);
        }
        Stat("", Labs.Hours(Math.Max(0, b.MaxHours - b.HoursLeft)), "Damage dealt", Ember);
        Stat("", b.Games.ToString(), "Games in the boss", Blood);
        Stat("", b.Finished.Count.ToString(), "Games defeated", Color.FromRgb(0xFF, 0xD1, 0x6A));
        Stat("", b.Minions.Count > 0 ? Labs.Hours(b.Minions[0].Left) : "-", "Next critical hit in", Color.FromRgb(0x5E, 0xD6, 0xFF));
        return row;
    }

    private static FrameworkElement Heading(string title, string sub)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 22, 0, 12) };
        stack.Children.Add(Ui.Text(title, 22, "FgBrush", bold: true));
        stack.Children.Add(Ui.Text(sub, 14, "SubtleBrush"));
        return stack;
    }

    private Button WeakPoint(Labs.Minion m)
    {
        const double W = 390;
        var card = new Grid { Width = W, Height = 168 };
        if (m.Game.Cover != null) card.Children.Add(new Image { Source = m.Game.Cover, Stretch = Stretch.UniformToFill, Opacity = 0.35 });
        card.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0xE8, 0x14, 0x06, 0x10), 0) });
        var row = new DockPanel { Margin = new Thickness(16) };
        var cover = new Border { Width = 136, Height = 136, CornerRadius = new CornerRadius(14), ClipToBounds = true, Margin = new Thickness(0, 0, 16, 0),
            Background = m.Game.Cover != null ? new ImageBrush(m.Game.Cover) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x33, 0x22, 0x33)),
            BorderBrush = new SolidColorBrush(Ember), BorderThickness = new Thickness(2) };
        DockPanel.SetDock(cover, Dock.Left);
        row.Children.Add(cover);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var crit = Label("⚔  WEAK POINT", 12, Ember, bold: true);
        info.Children.Add(crit);
        var name = Label(m.Game.Name, 19, Colors.White, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 4, 0, 8);
        info.Children.Add(name);
        info.Children.Add(MiniBar(m, 200));
        info.Children.Add(Label($"{Labs.Hours(m.Left)} left of {Labs.Hours(m.Main)}", 13, Color.FromRgb(0xFF, 0xD9, 0xC7)));
        var play = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 14, 6), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(Blood, Ember, 0) };
        play.Child = Label("▶  Attack", 13, Colors.White, bold: true);
        info.Children.Add(play);
        row.Children.Add(info);
        card.Children.Add(row);
        card.Clip = new RectangleGeometry(new Rect(0, 0, W, 168), 18, 18);
        var tile = Ui.Tile(card);
        tile.Margin = new Thickness(0, 0, 14, 14);
        tile.Click += (_, _) => _ = Host.ActivateAsync(m.Game);
        _first ??= tile;
        return tile;
    }

    private Button Soldier(Labs.Minion m)
    {
        var stack = new StackPanel { Width = 128 };
        var box = GridScreen.ImageBox(128, 128, out var img);
        img.Source = m.Game.Cover;
        stack.Children.Add(box);
        var bar = MiniBar(m, 128);
        bar.Margin = new Thickness(0, 8, 0, 2);
        stack.Children.Add(bar);
        var t = Ui.Text(Labs.Hours(m.Left) + " left", 12, "SubtleBrush", bold: true);
        stack.Children.Add(t);
        var tile = Ui.Tile(stack);
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.ToolTip = m.Game.Name;
        tile.Click += (_, _) => Host.Push(new DetailsScreen(m.Game));
        _first ??= tile;
        return tile;
    }

    private static FrameworkElement MiniBar(Labs.Minion m, double width)
    {
        double left = m.Main <= 0 ? 0 : Math.Clamp(m.Left / m.Main, 0, 1);
        var g = new Grid { Width = width, Height = 7, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, width * left), Background = new LinearGradientBrush(Blood, Ember, 0) });
        return g;
    }

    private Button Trophy(Game g)
    {
        var box = GridScreen.ImageBox(110, 110, out var img);
        img.Source = g.Cover;
        var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 3), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x6A)) };
        badge.Child = Label("🏆 Beaten", 11, Color.FromRgb(0x30, 0x20, 0x00), bold: true);
        ((Grid)box.Child).Children.Add(badge);
        var tile = Ui.Tile(box);
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.ToolTip = g.Name;
        tile.Click += (_, _) => Host.Push(new DetailsScreen(g));
        return tile;
    }

    private static TextBlock Label(string text, double size, Color color, bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
}

// ====================================================================================================== passport

/// <summary>The passport icon (a booklet with a globe), drawn to match the text around it.</summary>
public static class PassportArt
{
    /// <summary>
    /// A small passport: <paramref name="color"/> when given, otherwise it follows the color of the button it sits in.
    /// </summary>
    public static FrameworkElement Icon(double size, Brush? color = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        Shape Add(Shape s) { canvas.Children.Add(s); return s; }
        var book = Add(new Path { Data = Geometry.Parse("M 5,2.5 H 18 A 1.5,1.5 0 0 1 19.5,4 V 20 A 1.5,1.5 0 0 1 18,21.5 H 5 A 1.5,1.5 0 0 1 3.5,20 V 4 A 1.5,1.5 0 0 1 5,2.5 Z"), StrokeThickness = 1.6 });
        var globe = Add(new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.4 });
        Canvas.SetLeft(globe, 7);
        Canvas.SetTop(globe, 6);
        var meridian = Add(new Path { Data = Geometry.Parse("M 11.5,6 C 9.3,8.2 9.3,12.8 11.5,15 M 11.5,6 C 13.7,8.2 13.7,12.8 11.5,15 M 7,10.5 H 16"), StrokeThickness = 1.1 });
        var line = Add(new Path { Data = Geometry.Parse("M 8,18 H 15"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        foreach (var s in new[] { book, globe, meridian, line })
        {
            if (color != null && color != Brushes.Transparent) s.Stroke = color;
            else s.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    }
}

/// <summary>
/// My Gamer Passport: an identity page (your picture with a moving hologram, name, passport number, hours, favorite
/// genre and a machine-readable line), then visa pages of ink stamps for each genre, decade and store you've played
/// and the milestones you've reached. Each stamp carries the artwork of a game that earned it, and they thump onto
/// the page one after another, with a light tap on the controller. A on a stamp tells its story.
/// </summary>
public sealed class PassportScreen : Screen
{
    private static readonly string[] Inks = { "#E0565B", "#3E7BD9", "#2EA36F", "#D99A2B", "#9461E0", "#E04E97", "#1FA9B8" };
    private readonly StackPanel _page = new() { Margin = new Thickness(0, 0, 0, 30) };
    private readonly List<FrameworkElement> _toStamp = new();
    private Button? _first;

    public PassportScreen()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = _page };
        SetPage("My Gamer Passport", scroll, "{Select}  About this stamp      {Back}  Back", wide: true);
    }

    public override void OnShown()
    {
        Build();
        _ = StampInAsync();
    }

    public override void FocusDefault()
    {
        if (_first != null) _first.Focus();
        else base.FocusDefault();
    }

    private void Build()
    {
        _page.Children.Clear();
        _toStamp.Clear();
        var stamps = Labs.Passport(Host.AllGames, Host.Playtime, Host.Achievements, Host.Library);
        _page.Children.Add(IdentityPage(stamps));
        foreach (var group in stamps.GroupBy(s => s.Group))
        {
            int earned = group.Count(s => s.Earned);
            var head = new DockPanel { Margin = new Thickness(6, 26, 6, 12) };
            var count = Ui.Text($"{earned} of {group.Count()}", 15, "SubtleBrush", bold: true);
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            var title = Ui.Text("VISAS  ·  " + group.Key.ToUpperInvariant(), 18, "FgBrush", bold: true);
            title.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
            head.Children.Add(title);
            _page.Children.Add(head);
            var sheet = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 18, 6, 8), Background = PaperBrush() };
            var wrap = new WrapPanel();
            int i = 0;
            foreach (var s in group.OrderByDescending(s => s.Earned)) wrap.Children.Add(StampTile(s, i++));
            sheet.Child = wrap;
            _page.Children.Add(sheet);
        }
        FocusLater(_first);
    }

    /// <summary>Deep navy paper with faint security lines.</summary>
    private static Brush PaperBrush()
    {
        var lines = new GeometryGroup();
        for (int i = 0; i < 6; i++)
            lines.Children.Add(Geometry.Parse($"M 0,{20 + i * 12} C 40,{5 + i * 12} 80,{35 + i * 12} 120,{20 + i * 12} S 200,{5 + i * 12} 240,{20 + i * 12}"));
        var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0x9E, 0xC7, 0xFF)), 1), lines);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(0x12, 0x1C, 0x33), Color.FromRgb(0x0B, 0x12, 0x24), 90), null, new RectangleGeometry(new Rect(0, 0, 240, 96))));
        group.Children.Add(drawing);
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 240, 96), ViewportUnits = BrushMappingMode.Absolute };
    }

    // ---------------------------------------------------------------- the identity page

    private FrameworkElement IdentityPage(List<Labs.Stamp> stamps)
    {
        var page = new Border { CornerRadius = new CornerRadius(26), Background = PaperBrush(), Padding = new Thickness(30, 26, 30, 22), BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x9E, 0xC7, 0xFF)) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var gold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC7, 0x7A));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(PassportArt.Icon(30, gold));
        var t = new TextBlock { Text = "GAMER PASSPORT", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = gold, FontFamily = new FontFamily("Bahnschrift, Segoe UI"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        head.Children.Add(t);
        head.Children.Add(new TextBlock { Text = "  ·  SQUAREDECK", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xE8, 0xC7, 0x7A)), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumnSpan(head, 3);
        grid.Children.Add(head);

        // Your picture, with a hologram sweeping across it.
        var photo = new Grid { Width = 170, Height = 210, Margin = new Thickness(0, 0, 28, 0) };
        var frame = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x48)) };
        var face = new Viewbox { Stretch = Stretch.UniformToFill, Child = Avatars.Build(Host.Settings.Avatar, 170) };
        frame.Child = face;
        photo.Children.Add(frame);
        var holo = new Rectangle { RadiusX = 14, RadiusY = 14, IsHitTestVisible = false, Opacity = 0.55 };
        var sweep = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x7F, 0xFF, 0xE9), 0.3));
        sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0x7F, 0xFF, 0xE9), 0.42));
        sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0xD9, 0x7F, 0xFF), 0.5));
        sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0xFF, 0xD9, 0x7F), 0.58));
        sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xD9, 0x7F), 0.7));
        var move = new TranslateTransform();
        sweep.RelativeTransform = move;
        holo.Fill = sweep;
        photo.Children.Add(holo);
        if (!Theme.ReduceMotion)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        photo.Children.Add(new Border { CornerRadius = new CornerRadius(14), BorderBrush = gold, BorderThickness = new Thickness(2) });
        Grid.SetRow(photo, 1);
        grid.Children.Add(photo);

        // The details.
        var games = Host.AllGames.Where(g => g.IsGame).ToList();
        double hours = games.Sum(g => Math.Max(Host.Playtime.TotalSeconds(g.Key) / 3600, (g.IsSteam ? SteamLibrary.GetPlaytimeMinutes(g.AppId) ?? 0 : 0) / 60.0));
        var favorite = stamps.Where(s => s.Group == "Genres" && s.Earned).Select(s => s.Name).FirstOrDefault() ?? "Still exploring";
        var issued = Host.Playtime.Sessions.Count > 0 ? Host.Playtime.Sessions.Min(s => s.Start) : DateTime.Today;
        string name = UserName.Display;
        uint hash = 2166136261;
        foreach (char ch in name) hash = (hash ^ ch) * 16777619;
        string number = $"SD{hash % 10000:0000}{(hash / 10000) % 10000:0000}";
        var fields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Top };
        void Field(string label, string value)
        {
            var f = new StackPanel { Margin = new Thickness(0, 0, 20, 14) };
            f.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x9B, 0xC9)) });
            f.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            fields.Children.Add(f);
        }
        Field("Name", name);
        Field("Passport no.", number);
        Field("Nationality", "Gamer");
        Field("Issued", issued.ToString("dd MMM yyyy"));
        Field("Hours played", hours >= 1 ? $"{hours:N0} h" : "Just starting");
        Field("Favorite genre", favorite);
        Field("Games", games.Count.ToString("N0"));
        Field("Stamps", $"{stamps.Count(s => s.Earned)} of {stamps.Count}");
        Grid.SetRow(fields, 1);
        Grid.SetColumn(fields, 1);
        grid.Children.Add(fields);

        // A ring for how complete the passport is.
        double done = stamps.Count == 0 ? 0 : stamps.Count(s => s.Earned) / (double)stamps.Count;
        var ring = new Grid { Width = 150, Height = 150, VerticalAlignment = VerticalAlignment.Top };
        ring.Children.Add(new Ellipse { StrokeThickness = 10, Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0x9E, 0xC7, 0xFF)) });
        double around = Math.PI * (150 - 10) / 10;
        var arc = new Ellipse { StrokeThickness = 10, Stroke = new LinearGradientBrush(Color.FromRgb(0x7F, 0xFF, 0xE9), Color.FromRgb(0xD9, 0x7F, 0xFF), 45), StrokeDashCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { around * done, around * 2 }, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90) };
        ring.Children.Add(arc);
        var pct = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pct.Children.Add(new TextBlock { Text = $"{done * 100:0}%", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        pct.Children.Add(new TextBlock { Text = "complete", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x9B, 0xC9)), HorizontalAlignment = HorizontalAlignment.Center });
        ring.Children.Add(pct);
        Grid.SetRow(ring, 1);
        Grid.SetColumn(ring, 2);
        grid.Children.Add(ring);

        // The machine-readable line along the bottom.
        string mrzName = new string(name.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '<').ToArray());
        string line1 = ("P<SQD" + mrzName + "<<GAMER").PadRight(44, '<')[..44];
        string line2 = (number + "<" + issued.ToString("yyMMdd") + "<" + Math.Round(hours).ToString("0")).PadRight(44, '<')[..44];
        var mrz = new TextBlock { Text = line1 + "\n" + line2, FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"), FontSize = 17, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xD8, 0xE4, 0xFF)), Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(mrz, 2);
        Grid.SetColumnSpan(mrz, 3);
        grid.Children.Add(mrz);
        page.Child = grid;
        return page;
    }

    // ---------------------------------------------------------------- stamps

    private Button StampTile(Labs.Stamp s, int i)
    {
        const double S = 176;
        var color = (Color)ColorConverter.ConvertFromString(Inks[Math.Abs(StableHash(s.Name)) % Inks.Length]);
        var ink = new SolidColorBrush(s.Earned ? color : Color.FromArgb(0x70, 0x8C, 0x9A, 0xB8));
        var root = new Grid { Width = S, Height = S };

        // Each kind of visa has its own shape.
        Geometry shape = s.Group switch
        {
            "Genres" => new EllipseGeometry(new Point(S / 2, S / 2), S / 2 - 6, S / 2 - 6),
            "Decades" => new RectangleGeometry(new Rect(10, 26, S - 20, S - 52), 16, 16),
            "Stores" => Polygon(S, 8, 0),
            _ => Polygon(S, 12, 0.13),
        };
        // The artwork of a game that earned it, faded into the ink.
        if (s.Earned && s.Art?.Cover != null)
            root.Children.Add(new Rectangle { Fill = new ImageBrush(s.Art.Cover) { Stretch = Stretch.UniformToFill, Opacity = 0.38 }, Clip = shape });
        root.Children.Add(new Path { Data = shape, Fill = new SolidColorBrush(Color.FromArgb(s.Earned ? (byte)0x30 : (byte)0x00, color.R, color.G, color.B)) });
        var outer = new Path { Data = shape, Stroke = ink, StrokeThickness = 4 };
        if (!s.Earned) outer.StrokeDashArray = new DoubleCollection { 3, 3 };
        root.Children.Add(outer);
        var inner = new Path { Data = shape, Stroke = ink, StrokeThickness = 1.5, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.88, 0.88), Opacity = s.Earned ? 0.9 : 0.4 };
        root.Children.Add(inner);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = S - 44 };
        string kind = s.Group switch { "Genres" => "GENRE", "Decades" => "ERA", "Stores" => "STORE", _ => "MILESTONE" };
        words.Children.Add(new TextBlock { Text = s.Earned ? kind : "NOT YET", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center });
        words.Children.Add(new TextBlock { Text = s.Name.ToUpperInvariant(), FontSize = s.Name.Length > 14 ? 14 : 17, FontWeight = FontWeights.Black, Foreground = s.Earned ? Brushes.White : ink, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), FontFamily = new FontFamily("Bahnschrift, Segoe UI") });
        if (s.Earned) words.Children.Add(new TextBlock { Text = "★ APPROVED ★", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(words);
        if (s.Earned && !Perf.Active) root.Effect = new DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.45 };

        double angle = s.Earned ? (StableHash(s.Name + "r") % 15) - 7 : 0;
        var scale = new ScaleTransform(1, 1);
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = new TransformGroup { Children = { scale, new RotateTransform(angle) } };
        if (s.Earned && !Theme.ReduceMotion) { root.Opacity = 0; _toStamp.Add(root); }

        var tile = new Button { Style = Ui.StyleOf("TileButton"), Content = root, Margin = new Thickness(0, 0, 12, 12), ToolTip = s.Detail };
        tile.Click += (_, _) => _ = Host.AskAsync(s.Name, s.Earned ? s.Detail + (s.Art != null ? "\n\nStamped by " + s.Art.Name + "." : "") : "Not stamped yet. " + s.Detail, "OK");
        _first ??= tile;
        return tile;
    }

    /// <summary>The earned stamps thump onto the page one after another, each with a light tap on the controller.</summary>
    private async Task StampInAsync()
    {
        await Task.Delay(250);
        int n = 0;
        foreach (var stamp in _toStamp.ToList())
        {
            if (!IsLoaded) return;
            if (stamp.RenderTransform is TransformGroup { Children: [ScaleTransform scale, ..] })
            {
                var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.9, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.9, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            }
            stamp.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            if (n++ < 14) Host.Rumble(14);
            await Task.Delay(n < 24 ? 110 : 30);
        }
    }

    private static Geometry Polygon(double size, int points, double notch)
    {
        var fig = new PathFigure { IsClosed = true };
        double c = size / 2, r = size / 2 - 8;
        int steps = notch > 0 ? points * 2 : points;
        for (int i = 0; i < steps; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / steps;
            double rr = notch > 0 && i % 2 == 1 ? r * (1 - notch) : r;
            var p = new Point(c + rr * Math.Cos(a), c + rr * Math.Sin(a));
            if (i == 0) fig.StartPoint = p;
            else fig.Segments.Add(new LineSegment(p, true));
        }
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char ch in s) h = h * 31 + ch;
            return h & 0x7FFFFFFF;
        }
    }
}
