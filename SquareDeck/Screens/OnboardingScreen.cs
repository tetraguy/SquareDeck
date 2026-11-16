using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The first time SquareDeck opens. A list of steps down the left shows where you are; the right side holds one
/// simple choice per step. Every choice applies straight away, so you see it happen, and "Use recommended" on the
/// first step skips the lot. All of it can be changed later in Settings, and it can be run again from
/// Settings > Updates, backup and reset.
/// </summary>
public sealed class OnboardingScreen : Screen
{
    private static readonly (string Name, string Glyph, string About)[] Steps =
    {
        ("Welcome", "", "Say hello"),
        ("Look", "", "Style and theme"),
        ("Background", "", "Still or moving"),
        ("Controller", "", "Buttons and sounds"),
        ("Games", "", "Where they come from"),
        ("Ready", "", "Tips and go")
    };
    private static readonly string[] SuggestedStyles = { "switch", "frosted-dark", "waves", "stage", "cinema", "pixel-city" };
    private static readonly string[] SuggestedBackgrounds = { "", "Blue Wave", "Black Wave", "Green Blades", "Lofi City", "Night River" };

    private readonly TaskCompletionSource _done;
    private readonly StackPanel _rail = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _progress = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Grid _progressTrack = new() { Height = 4, Margin = new Thickness(0, 0, 0, 18) };
    private readonly ContentControl _page = new() { Focusable = false };
    private readonly Button _back, _next, _skip;
    private int _step;
    private bool _startOtherStores;

    public OnboardingScreen(TaskCompletionSource done)
    {
        _done = done;
        _back = Ui.PanelButton("‹  Back");
        _back.Click += (_, _) => Go(_step - 1);
        _skip = Ui.PanelButton("Skip setup");
        _skip.Click += (_, _) => Finish();
        _next = new Button { Style = Ui.StyleOf("AccentButton"), Content = "Next  ›", MinWidth = 200 };
        _next.Click += (_, _) => Advance();

        // Progress along the top.
        var track = new Border { CornerRadius = new CornerRadius(2), Opacity = 0.18 };
        track.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        _progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _progressTrack.Children.Add(track);
        _progressTrack.Children.Add(_progress);
        _progressTrack.SizeChanged += (_, _) => ShowProgress(animate: false);

        // Left: the steps. Right: the current one, in a soft panel.
        var railBox = new Border { Width = 236, Padding = new Thickness(0, 0, 22, 0), Child = _rail };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.VerticalOnly, Content = _page, Padding = new Thickness(34, 26, 34, 22)
        };
        var panel = new Border { CornerRadius = new CornerRadius(24), Child = scroll };
        panel.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        panel.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");

        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.Children.Add(railBox);
        Grid.SetColumn(panel, 1);
        middle.Children.Add(panel);

        var bottom = new DockPanel { Margin = new Thickness(258, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_back, Dock.Left);
        bottom.Children.Add(_back);
        DockPanel.SetDock(_next, Dock.Right);
        bottom.Children.Add(_next);
        _skip.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(_skip, Dock.Right);
        bottom.Children.Add(_skip);

        var body = new Grid { MaxWidth = 1180 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(_progressTrack);
        Grid.SetRow(middle, 1);
        body.Children.Add(middle);
        Grid.SetRow(bottom, 2);
        body.Children.Add(bottom);

        SetPage("Set up SquareDeck", body, "{Select}  Choose      {RB}  Next      {LB}  Back", wide: true);
        CompactHeader();
    }

    public override void OnShown()
    {
        _startOtherStores = Host.Settings.OtherStores;
        Go(0);
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Back:
                if (_step > 0) Go(_step - 1);
                return true; // never leaves by accident; Skip setup is a button
            case NavAction.Start:
            case NavAction.RB:
                Advance();
                return true;
            case NavAction.LB:
                if (_step > 0) Go(_step - 1);
                return true;
        }
        return false;
    }

    private void Advance()
    {
        if (_step == Steps.Length - 1) Finish(); else Go(_step + 1);
    }

    private void Finish()
    {
        var s = Host.Settings;
        s.SetupDone = true;
        s.Save();
        Host.ApplySettings();
        Host.Pop(this);
        if (s.OtherStores != _startOtherStores) _ = Host.ReloadAsync(waitForArt: false); // the other launchers' games come or go
        _done.TrySetResult();
    }

    private void ShowProgress(bool animate)
    {
        double width = _progressTrack.ActualWidth * (_step + 1) / Steps.Length;
        if (animate && !Theme.ReduceMotion && _progress.ActualWidth > 0)
            _progress.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else
        {
            _progress.BeginAnimation(WidthProperty, null);
            _progress.Width = width;
        }
    }

    /// <param name="keep">Redrawing the same step after a choice: no slide, and the highlight stays on what was picked.</param>
    private void Go(int step, bool keep = false)
    {
        _step = Math.Clamp(step, 0, Steps.Length - 1);
        BuildRail();
        ShowProgress(animate: true);
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _skip.Visibility = _step == Steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = _step == 0 ? "Customize  ›" : _step == Steps.Length - 1 ? "Start playing" : "Next  ›";

        UIElement page = _step switch
        {
            0 => WelcomePage(),
            1 => LookPage(),
            2 => BackgroundPage(),
            3 => ControllerPage(),
            4 => GamesPage(),
            _ => ReadyPage()
        };
        _page.Content = page;
        if (!keep && !Theme.ReduceMotion && page is FrameworkElement fe)
        {
            var shift = new TranslateTransform(0, 18);
            fe.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }
        // The current choice on the page, else its first, else Next.
        var buttons = FindButtons(page).Where(b => b.Focusable).ToList();
        // (On the last step that's "Start playing", so one press finishes.)
        FocusLater(_step == Steps.Length - 1 ? _next : buttons.FirstOrDefault(b => b.Tag as string == "current") ?? buttons.FirstOrDefault() ?? _next);
        if (!keep) UiSound.Move();
    }

    private static IEnumerable<Button> FindButtons(UIElement root) => root is DependencyObject d ? d.FindDescendants<Button>() : Enumerable.Empty<Button>();

    /// <summary>The steps down the left: done ones ticked, the current one lit. Touch or click one to jump to it.</summary>
    private void BuildRail()
    {
        _rail.Children.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            int index = i;
            bool current = i == _step, done = i < _step;
            var badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BorderBrushProperty, current || done ? "AccentBrush" : "SubtleBrush");
            if (current || done) badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var icon = new TextBlock { Text = done ? "" : Steps[i].Glyph, FontFamily = Ui.IconFont, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, current || done ? "BgBrush" : "SubtleBrush");
            badge.Child = icon;

            var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Ui.Text(Steps[i].Name, 17, current ? "FgBrush" : "SubtleBrush", bold: current));
            if (current) words.Children.Add(Ui.Text(Steps[i].About, 13, "SubtleBrush"));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Opacity = current || done ? 1 : 0.6, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.Children.Add(badge);
            row.Children.Add(words);
            row.MouseLeftButtonUp += (_, _) => Go(index);
            _rail.Children.Add(row);

            if (i < Steps.Length - 1)
            {
                // The line joining one step to the next.
                var line = new Border { Width = 2, Height = 22, Margin = new Thickness(18, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = done ? 1 : 0.3 };
                line.SetResourceReference(Border.BackgroundProperty, done ? "AccentBrush" : "SubtleBrush");
                _rail.Children.Add(line);
            }
        }
    }

    // ================================================================ building blocks

    private static StackPanel Page(string title, string lead)
    {
        var p = new StackPanel();
        var t = Ui.Text(title, 34, "FgBrush", bold: true);
        t.Margin = new Thickness(0, 0, 0, 6);
        p.Children.Add(t);
        var l = Ui.Text(lead, 16, "SubtleBrush");
        l.TextWrapping = TextWrapping.Wrap;
        l.MaxWidth = 760;
        l.HorizontalAlignment = HorizontalAlignment.Left;
        l.Margin = new Thickness(0, 0, 0, 20);
        p.Children.Add(l);
        return p;
    }

    private static TextBlock Section(string text)
    {
        var t = Ui.Text(text.ToUpperInvariant(), 13, "SubtleBrush", bold: true);
        t.Margin = new Thickness(2, 16, 0, 8);
        return t;
    }

    /// <summary>A picture card with its name under it. The chosen one carries a tick in the highlight color.</summary>
    private Button Card(UIElement picture, double width, double height, string name, bool chosen, Action pick)
    {
        var face = new Grid { Width = width, Height = height, Clip = new RectangleGeometry(new Rect(0, 0, width, height), 12, 12) };
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        face.Children.Add(back);
        face.Children.Add(picture);
        if (chosen)
        {
            var edge = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12) };
            edge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            face.Children.Add(edge);
            var tick = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
            tick.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var mark = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            mark.SetResourceReference(TextBlock.ForegroundProperty, "BgBrush");
            tick.Child = mark;
            face.Children.Add(tick);
        }
        var card = new StackPanel { Width = width };
        card.Children.Add(face);
        var label = Ui.Text(name, 15, chosen ? "AccentBrush" : "FgBrush", bold: true);
        label.Margin = new Thickness(2, 8, 0, 0);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        card.Children.Add(label);
        var b = Ui.Tile(card);
        b.Margin = new Thickness(0, 0, 10, 10);
        if (chosen) b.Tag = "current";
        b.Click += (_, _) => pick();
        return b;
    }

    /// <summary>A row of pill choices; the chosen one is ticked.</summary>
    private WrapPanel Choices((string Id, string Text)[] options, string current, Action<string> pick)
    {
        var w = new WrapPanel();
        foreach (var (id, text) in options)
        {
            string o = id;
            bool chosen = current == o;
            var b = chosen ? new Button { Style = Ui.StyleOf("AccentButton"), Content = "✓  " + text } : Ui.PanelButton(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            if (chosen) b.Tag = "current";
            b.Click += (_, _) => { pick(o); Host.Settings.Save(); Go(_step, keep: true); };
            w.Children.Add(b);
        }
        return w;
    }

    private static Grid Logo(double size)
    {
        // The SquareDeck mark: a rounded square with a frame and a small filled square in its corner.
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Rectangle { RadiusX = size * 0.2, RadiusY = size * 0.2, Fill = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)) });
        var frame = new Rectangle { Width = size * 0.45, Height = size * 0.45, RadiusX = size * 0.06, RadiusY = size * 0.06, StrokeThickness = size * 0.0625 };
        frame.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        g.Children.Add(frame);
        var dot = new Rectangle { Width = size * 0.166, Height = size * 0.166, RadiusX = size * 0.04, RadiusY = size * 0.04, Margin = new Thickness(size * 0.33, size * 0.33, 0, 0) };
        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        g.Children.Add(dot);
        g.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.4 };
        return g;
    }

    /// <summary>An icon in a soft square, a heading and a line of text (the welcome page's highlights, the tips).</summary>
    private static UIElement Feature(string glyph, string title, string text, double width)
    {
        var chip = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top };
        chip.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        chip.Child = icon;
        var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Width = width - 62 };
        words.Children.Add(Ui.Text(title, 16, "FgBrush", bold: true));
        var body = Ui.Text(text, 14, "SubtleBrush");
        body.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Width = width, Margin = new Thickness(0, 0, 0, 16) };
        row.Children.Add(chip);
        row.Children.Add(words);
        return row;
    }

    // ================================================================ pages

    private UIElement WelcomePage()
    {
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 22) };
        head.Children.Add(Logo(96));
        var words = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text("Welcome to SquareDeck", 38, "FgBrush", bold: true));
        var lead = Ui.Text("Your games, apps and music in one place, made for a controller.", 17, "SubtleBrush");
        lead.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(lead);
        head.Children.Add(words);
        p.Children.Add(head);

        var games = Host.AllGames.Count(g => g.IsGame);
        var features = new WrapPanel();
        features.Children.Add(Feature("", games > 0 ? $"{games} game{(games == 1 ? "" : "s")} found" : "Your library, gathered",
            "Steam and your other launchers, with covers downloaded for you.", 380));
        features.Children.Add(Feature("", "Make it yours", "Dozens of styles, moving backgrounds, sounds and music.", 380));
        features.Children.Add(Feature("", "Controller first", "Everything works from the couch or in your hands.", 380));
        features.Children.Add(Feature("", "Tuned for handhelds", "Power limits, quick settings and an in-game menu.", 380));
        p.Children.Add(features);

        p.Children.Add(Section("How would you like to start?"));
        var choices = new StackPanel();
        var quick = Ui.Row("Use recommended settings", "About 5 seconds", "", subtitle: "Skip the choices and start playing. Everything can be changed later in Settings.");
        quick.Click += (_, _) => Go(Steps.Length - 1);
        choices.Children.Add(quick);
        var custom = Ui.Row("Customize", "About a minute", "", subtitle: "Pick a look, a background and your controller's buttons, one step at a time.");
        custom.Tag = "current";
        custom.Click += (_, _) => Go(1);
        choices.Children.Add(custom);
        Ui.GroupRows(choices);
        p.Children.Add(choices);
        return p;
    }

    private UIElement LookPage()
    {
        var s = Host.Settings;
        var p = Page("Pick a look", "A style changes the home screen and every page, and brings its own colors, wallpaper, sounds and music. " +
                                    "You'll see it change behind this page.");
        var styles = new WrapPanel();
        string current = UiStyles.Upgrade(s.UiStyle);
        var shown = SuggestedStyles.ToList();
        if (!shown.Contains(current)) shown[^1] = current; // the one in use is always among them
        foreach (var id in shown)
        {
            var st = UiStyles.Find(id);
            const double w = 262, h = w * 9 / 16;
            var preview = UiStyleScreen.Preview(st);
            preview.RenderTransform = new ScaleTransform(w / 340, w / 340);
            // The preview is drawn at its full size and scaled down, so it's held at the card's top left corner.
            var holder = new Canvas { Width = w, Height = h, ClipToBounds = true };
            holder.Children.Add(preview);
            styles.Children.Add(Card(holder, w, h, st.Name, st.Id == current, () =>
            {
                Host.UseStyle(st); // with its matching wallpaper, sounds and music
                Go(_step, keep: true);
            }));
        }
        p.Children.Add(styles);
        var more = Ui.Text($"{UiStyles.All.Length} styles in all: Settings > Appearance > UI style.", 13, "SubtleBrush");
        p.Children.Add(more);

        // Styles with their own fixed colors choose the theme themselves.
        if (UiStyles.Find(current).ThemeMode == null)
        {
            p.Children.Add(Section("Light or dark"));
            p.Children.Add(Choices(new[] { ("system", "Follow Windows"), ("light", "Light"), ("dark", "Dark"), ("oled", "Black (OLED)") }, s.ThemeMode, m =>
            {
                s.ThemeMode = m;
                s.DarkTheme = Theme.ShouldBeDark(s);
                Host.ApplyLook();
            }));
        }
        return p;
    }

    private UIElement BackgroundPage()
    {
        var s = Host.Settings;
        var p = Page("A moving background?", "A slow, silent video behind everything. It stops while you play, so it costs nothing in games.");
        var row = new WrapPanel();
        var names = SuggestedBackgrounds.ToList();
        if (s.DynamicBackground.Length > 0 && !names.Contains(s.DynamicBackground, StringComparer.OrdinalIgnoreCase)) names.Insert(1, s.DynamicBackground);
        foreach (var name in names)
        {
            string n = name;
            if (n.Length > 0 && MainWindow.DynamicPathFor(n) == null) continue;
            string still = System.IO.Path.Combine(MainWindow.BackgroundsFolder, "previews", n + ".jpg");
            UIElement picture;
            if (n.Length > 0 && File.Exists(still) && ImageLoader.FromFile(still, 480) is { } pic)
                picture = new Image { Source = pic, Stretch = Stretch.UniformToFill };
            else
            {
                var off = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
                off.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush");
                picture = off;
            }
            bool chosen = string.Equals(s.DynamicBackground, n, StringComparison.OrdinalIgnoreCase);
            row.Children.Add(Card(picture, 262, 147, n.Length == 0 ? "None" : n, chosen, () =>
            {
                if (n.Length > 0 && s.DynamicBlur == 0) s.DynamicBlur = 8; // a little softness keeps text readable
                Host.UseDynamicBackground(n);
                Go(_step, keep: true);
            }));
        }
        p.Children.Add(row);
        p.Children.Add(Ui.Text("More backgrounds and a blur level: Settings > Appearance > Moving background.", 13, "SubtleBrush"));
        return p;
    }

    private UIElement ControllerPage()
    {
        var s = Host.Settings;
        var p = Page("Your controller", "So the buttons on screen match the ones in your hands. The sample below changes as you choose.");

        p.Children.Add(Section("Button names"));
        p.Children.Add(Choices(Hints.Styles, s.GlyphStyle, id => { s.GlyphStyle = id; Hints.SetStyle(id); Host.ApplySettings(); }));
        p.Children.Add(Section("How buttons are drawn"));
        p.Children.Add(Choices(Hints.Looks, s.GlyphLook, id => { s.GlyphLook = id; Hints.SetLook(id); }));
        p.Children.Add(Section("Which button selects"));
        p.Children.Add(Choices(new[] { ("ABXY", "Bottom button selects"), ("BAYX", "Right button selects (Nintendo)") }, Hints.Map, id => Host.SetButtonMap(id)));
        p.Children.Add(Section("Interface sounds"));
        p.Children.Add(Choices(new[] { ("on", "On"), ("off", "Off") }, s.UiSounds ? "on" : "off", id => { s.UiSounds = id == "on"; UiSound.Configure(s); }));

        // A live sample of the hint bar, as it will look everywhere.
        p.Children.Add(Section("Preview"));
        var sample = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 12, 20, 12), HorizontalAlignment = HorizontalAlignment.Left };
        sample.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        sample.Child = new HintBar("{Select}  Select      {Back}  Back      {Details}  Details      {Options}  Options");
        p.Children.Add(sample);
        return p;
    }

    private UIElement GamesPage()
    {
        var s = Host.Settings;
        int steam = Host.AllGames.Count(g => g.IsGame && g.IsSteam), others = Host.AllGames.Count(g => g.IsGame && !g.IsSteam);
        var p = Page("Your games", steam + others > 0
            ? $"Found so far: {steam} from Steam" + (others > 0 ? $" and {others} from elsewhere" : "") + ". Nothing to do here unless you want more."
            : "Steam games show up by themselves. Nothing to do here unless you want more.");
        var rows = new StackPanel();
        var other = Ui.Row("Include games from other launchers", s.OtherStores ? "On" : "Off", "",
            subtitle: "Epic, GOG, Ubisoft, EA, Xbox / Game Pass, Battle.net and Steam's non-Steam shortcuts");
        other.Tag = "current";
        other.Click += (_, _) => { s.OtherStores = !s.OtherStores; s.Save(); Go(_step, keep: true); };
        rows.Children.Add(other);
        var key = Ui.Row("Steam Web API key (optional)", string.IsNullOrWhiteSpace(s.SteamWebApiKey) ? "Not set" : "Saved", "",
            subtitle: "For Friends, and for games you own but haven't installed. Free from steamcommunity.com/dev/apikey");
        key.Click += async (_, _) => { await SettingsScreen.EditSteamKeyAsync(Host, () => { }); Go(_step, keep: true); };
        rows.Children.Add(key);
        Ui.GroupRows(rows);
        p.Children.Add(rows);
        p.Children.Add(Section("Done for you"));
        var auto = new WrapPanel();
        auto.Children.Add(Feature("", "Cover art", "Square covers, artwork and logos download by themselves.", 380));
        auto.Children.Add(Feature("", "New installs", "Games you install later appear on their own.", 380));
        p.Children.Add(auto);
        return p;
    }

    private UIElement ReadyPage()
    {
        var s = Host.Settings;
        var p = Page("You're all set", "Here's what you chose, and four buttons worth remembering.");

        var summary = new StackPanel();
        void Chosen(string glyph, string label, string value, int step)
        {
            var row = Ui.Row(label, value, glyph);
            row.Click += (_, _) => Go(step);
            summary.Children.Add(row);
        }
        Chosen("", "Look", UiStyles.Find(s.UiStyle).Name, 1);
        Chosen("", "Background", s.DynamicBackground.Length > 0 ? s.DynamicBackground : "None", 2);
        Chosen("", "Controller", Hints.Styles.FirstOrDefault(x => x.Item1 == s.GlyphStyle).Item2 ?? "Xbox", 3);
        Chosen("", "Games", s.OtherStores ? "Steam and other launchers" : "Steam only", 4);
        Ui.GroupRows(summary);
        p.Children.Add(summary);

        p.Children.Add(Section("Good to know"));
        var tips = new WrapPanel();
        tips.Children.Add(Feature("", "Search everything", "Menu (Start) on the home screen finds games, apps, settings and files.", 380));
        tips.Children.Add(Feature("", "Quick settings", "View opens volume, brightness, Wi-Fi and more, from anywhere.", 380));
        tips.Children.Add(Feature("", "Options for a game", Hints.KeyLabel("Options") + " on a game: details, artwork, collections and more.", 380));
        tips.Children.Add(Feature("", "While you play", "The Guide button brings SquareDeck back or opens the quick menu.", 380));
        p.Children.Add(tips);
        return p;
    }
}
