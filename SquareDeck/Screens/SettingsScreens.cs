using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SquareDeck.Services;
using Windows.Devices.Radios;

namespace SquareDeck.Screens;

/// <summary>
/// Settings. The first page is a short list of categories; each opens a page of its own, so no list is
/// longer than a screen or two.
/// </summary>
public sealed partial class SettingsScreen : ListScreen
{
    private static readonly (string Id, string Name, string Glyph, string About)[] Categories =
    {
        ("overview", "Home", "\uE80F", "Your profile, your device at a glance, and the settings people change most"),
        ("looks", "Themes", "\uE771", "Pick one of {styles} ready-made styles or create your own"),
        ("look", "Look and feel", "\uE790", "Colors, wallpaper and themes, the top bar and home screen buttons"),
        ("home", "Home screen", "\uE8A9", "The games row, animations, screen saver, starting and closing"),
        ("sound", "Sound and music", "\uE767", "Interface sounds, menu music, the Multimedia page and sound output"),
        ("gaming", "Gaming", "\uE768", "Launching games, while you play, Performance Boost, the overlay, cloud gaming"),
        ("library", "Library and stores", "\uE8F1", "Library layout and stores, achievements, play time, service keys"),
        ("controller", "Controller and Game Bar widget", "\uE7FC", "Buttons, vibration, typing, the Game Bar widget, handheld buttons"),
        ("handheld", "{device}", Ui.HandheldGlyph, "TDP limit and processor power, lights, fans, battery care and buttons for this device"),
        ("network", "Network and browser", "\uE774", "Wi-Fi, Bluetooth, troubleshooting, the web browser"),
        ("access", "Accessibility", "\uE776", "Seeing, motion, hearing, controls and timing, reading aloud"),
        ("apps", "Apps", "\uE71D", "Installed apps: uninstall, repair, reset; startup and default apps"),
        ("system", "System and tools", "\uE7F8", "Notifications, disk cleanup, tools, Xbox mode, starting with Windows"),
        ("data", "Updates and data", "\uE777", "New versions, backup, downloaded art, start over"),
        ("experimental", "Experimental", "\uE9D9", "Features still being tried out. They may change or go away")
    };

    /// <summary>
    /// The sections shown. On a recognized handheld it has a section of its own, named after the model, which is the one
    /// place for its TDP; on any other PC the TDP stays in Power and performance and that section isn't shown.
    /// </summary>
    private static (string Id, string Name, string Glyph, string About)[] VisibleCategories
    {
        get
        {
            // "{styles}" is the number of ready-made styles, counted when shown (see UiStyles.ReadyMadeCount).
            var all = Categories.Select(c => (c.Id, c.Name, c.Glyph, c.About.Replace("{styles}", UiStyles.ReadyMadeCount.ToString())));
            if (!Handheld.IsHandheld) return all.Where(c => c.Id != "handheld").ToArray();
            return all.Select(c => c.Id switch
            {
                "handheld" => (c.Id, Handheld.DisplayName, c.Glyph, c.Item4),
                _ => c
            }).ToArray();
        }
    }

    /// <summary>
    /// Which groups of settings each section shows, from which part of the settings code. Null groups = all of that part.
    /// Sections can borrow groups from each other's parts, so settings sit where you'd look for them.
    /// </summary>
    private static readonly Dictionary<string, (string Part, string[]? Groups)[]> Sections = new()
    {
        ["looks"] = new[] { ("looks", (string[]?)null) },
        ["look"] = new[] { ("look", (string[]?)new[] { "Background", "Colors", "Font", "Your looks", "Profile", "Artwork" }), ("look", new[] { "Top bar", "Display", "Settings view and unlockables" }) },
        ["home"] = new[] { ("home", (string[]?)new[] { "Home row and buttons", "Buttons under the games", "Animations", "Covers and companion", "Moving background", "Screen saver", "Starting and closing" }) },
        ["sound"] = new[] { ("sound", (string[]?)null) },
        ["gaming"] = new[] { ("games", (string[]?)new[] { "Launching games", "While playing" }), ("power", new[] { "Performance Boost", "Performance overlay" }), ("games", new[] { "Cloud gaming" }),
                             // On a PC that isn't a recognized handheld, the TDP (on a handheld it's in the handheld's own section).
                             ("power", new[] { "Processor power (TDP)", "Processor (Ryzen Z1 / Z2)", "TDP helper" }) },
        ["library"] = new[] { ("games", (string[]?)new[] { "Library and stores", "Achievements, play time and captures", "Library and store features", "Pages and services", "Service keys" }) },
        ["controller"] = new[] { ("controller", (string[]?)null) },
        ["network"] = new[] { ("network", (string[]?)null) },
        ["access"] = new[] { ("access", (string[]?)null) },
        ["apps"] = new[] { ("apps", (string[]?)null) },
        ["system"] = new[] { ("system", (string[]?)null) },
        ["data"] = new[] { ("data", (string[]?)null) },
        ["experimental"] = new[] { ("experimental", (string[]?)null) },
        ["handheld"] = new[] { ("handheld", (string[]?)null) },
    };

    /// <summary>Section ids from earlier versions.</summary>
    private static string Upgrade(string id) => id switch
    {
        "games" => "gaming",
        // Power and performance is gone: its TDP is in the handheld's section (or Gaming elsewhere), the rest in Gaming.
        "power" => Handheld.IsHandheld ? "handheld" : "gaming",
        _ => id
    };

    private string _category;
    private readonly StackPanel _nav = new();
    private Border _navPanel = null!;
    // How long the highlight rests on a category before its settings are shown (short, but moving through several
    // categories quickly doesn't build each one on the way).
    private readonly System.Windows.Threading.DispatcherTimer _switchSoon = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private string? _pendingCategory;

    /// <summary>Every settings page and row, for Search: its name, subtitle, page, and how to open it there.</summary>
    internal static List<SearchSetting> BuildIndex(MainWindow host)
    {
        var list = new List<SearchSetting>();
        foreach (var c in VisibleCategories)
        {
            var cat = c;
            list.Add(new SearchSetting(cat.Name, cat.About, "Settings", cat.Glyph, () => new SettingsScreen(cat.Id)));
            try
            {
                var page = new SettingsScreen(cat.Id) { Host = host };
                foreach (var (label, sub) in page.RowLabels())
                {
                    string l = label;
                    list.Add(new SearchSetting(l, sub, cat.Name, cat.Glyph, () => new SettingsScreen(cat.Id) { FocusLabel = l }));
                }
            }
            catch (Exception) { }
        }
        return list;
    }

    public SettingsScreen() : this(null) { }

    /// <summary>
    /// Two panes: the sections on the left (moving through them shows each one straight away), their settings on
    /// the right. Your profile and a search box sit at the top of the left side.
    /// </summary>
    internal SettingsScreen(string? category)
        : base("Settings", "{Select}  Select      {Back}  Back", PanesLayout())
    {
        _panes = _panesHost != null;
        if (_panes) BuildPanes(_panesHost!);
        _panesHost = null;
        _category = Upgrade(category ?? "overview"); // Settings opens on its home page
        // A light tick on the controller each time the highlight moves to another row (if vibration is on).
        AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (Host?.Settings.RumbleInSettings == true && e.NewFocus is Button && e.OldFocus is Button && !ReferenceEquals(e.NewFocus, e.OldFocus))
                Host.Rumble(16);
        }), true);
        _switchSoon.Tick += (_, _) =>
        {
            _switchSoon.Stop();
            if (_pendingCategory != null && _pendingCategory != _category) Show(_pendingCategory, focusContent: false);
        };
        BuildLayout();
    }

    /// <summary>
    /// The look decides the layout (PageDesign.Settings): one centered column (Home lists the sections; each opens
    /// on its own), a column on the left over the artwork (drawer), a wider column, or two panes with the sections
    /// on the left and their settings on the right (panes).
    /// </summary>
    private void BuildLayout()
    {
        switch (LayoutId)
        {
            case "drawer":
                Rows.Width = 880;
                Rows.HorizontalAlignment = HorizontalAlignment.Left;
                Rows.Margin = new Thickness(70, 0, 0, 24);
                break;
            case "wide":
                Rows.Width = 1180;
                Rows.HorizontalAlignment = HorizontalAlignment.Center;
                Rows.Margin = new Thickness(0, 0, 0, 24);
                break;
            case "panes":
                Rows.Width = double.NaN;
                Rows.MaxWidth = 1100;
                Rows.HorizontalAlignment = HorizontalAlignment.Stretch;
                Rows.Margin = new Thickness(0, 0, 36, 24);
                break;
            default:
                Rows.Width = 1040;
                Rows.HorizontalAlignment = HorizontalAlignment.Center;
                Rows.Margin = new Thickness(0, 0, 0, 24);
                break;
        }
    }

    /// <summary>
    /// The page's layout: the look's own, unless Experimental > Settings view style picks one. "column" (one
    /// centered column), "wide", "drawer" (on the left over the artwork), "panes" (sections on the left) or "tiles"
    /// (Settings home shows the sections as a grid of tiles).
    /// </summary>
    internal static string LayoutId
    {
        get
        {
            var chosen = Labs.On("settingsview") ? Labs.Get("settings.layout", "auto") : "auto";
            return chosen == "auto" ? LookChrome.Design.Settings : chosen;
        }
    }

    public static readonly (string Id, string Name, string About)[] Layouts =
    {
        ("auto", "Follow the style", "Each UI style's own layout"),
        ("column", "Single column", "One centered list; Settings home lists the sections"),
        ("wide", "Wide column", "A wider list that shows more on each row"),
        ("drawer", "Side drawer", "A list on the left, over the wallpaper or artwork"),
        ("panes", "Two panes", "The sections down the left, their settings on the right"),
        ("tiles", "Tiles", "Settings home shows the sections as big tiles"),
    };

    private async void ChooseLayout()
    {
        int c = await Host.AskAsync("Settings view style", "How the Settings page is laid out. Settings reopens in the new layout.",
            Layouts.Select(l => l.Name).ToArray());
        if (c < 0) return;
        Labs.Put("settings.layout", Layouts[c].Id);
        // The layout is set up when the page opens, so open it again.
        Host.Pop(this);
        Host.Push(new SettingsScreen("look"));
    }

    // ---------------------------------------------------------------- two panes (console-style looks)

    [ThreadStatic] private static Grid? _panesHost;
    private readonly bool _panes;

    /// <summary>For looks with two panes: a grid with room on the left for the sections and the settings on the right.</summary>
    private static Func<ScrollViewer, UIElement>? PanesLayout()
    {
        if (LayoutId != "panes") return null;
        return scroll =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            _panesHost = grid;
            return grid;
        };
    }

    private void BuildPanes(Grid host)
    {
        var navScroll = new ScrollViewer
        {
            Content = _nav, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly
        };
        _navPanel = new Border { Child = navScroll, Margin = new Thickness(0, 0, 30, 12), Width = 300 };
        // Each console draws its list of sections its own way.
        switch (Looks.Current.Id)
        {
            case "deck":
                _navPanel.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1B));
                _navPanel.CornerRadius = new CornerRadius(4);
                break;
            case "xdash":
                _navPanel.Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x10, 0x12, 0x14));
                _navPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                _navPanel.BorderThickness = new Thickness(0, 0, 1, 0);
                break;
            default:
                _navPanel.Background = Brushes.Transparent;
                break;
        }
        host.Children.Add(_navPanel);
    }

    /// <summary>Marks the section you're in on the left pane.</summary>
    private void MarkNav()
    {
        if (!_panes) return;
        foreach (var b in _nav.Children.OfType<Button>())
            if (b.DataContext is string tag && tag.StartsWith("nav:") && tag is not ("nav:toggle" or "nav:profile" or "nav:search"))
                b.Tag = tag == "nav:" + _category ? "selected" : null;
    }

    public override void OnShown()
    {
        if (_panes) { SizeNav(animate: false); BuildNav(); }
        if (FocusLabel != null) { Rebuild(); return; } // opened from Search: land on that row
        ForgetRow();
        Rebuild();
    }

    public override void OnResumed() => Rebuild(); // back on the setting that opened the page

    public override void OnClosed() => _switchSoon.Stop();

    public override void FocusDefault()
    {
        if (_panes && NavRow(_category) is { } nav) { nav.Focus(); return; }
        Rows.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    public override bool OnAction(NavAction action)
    {
        if (action != NavAction.Back) return false;
        // B steps back: a group to its section, a section to Settings home, then out of Settings.
        if (_group != null) { LeaveGroup(); return true; }
        // Two panes: from the settings back to the list of sections, then out.
        if (_panes)
        {
            if (Rows.IsKeyboardFocusWithin && NavRow(_category) is { } nav) { nav.Focus(); return true; }
            return false;
        }
        if (_category != "overview")
        {
            var from = _category;
            Show("overview", focusContent: false);
            ReturnScroll(Rows.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == "section:" + from));
            return true;
        }
        return false;
    }

    private Button? NavRow(string id) => _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == "nav:" + id);

    private bool Collapsed => Host.Settings.SettingsNavCollapsed;

    /// <summary>Shrinks the list of sections to icons only, or opens it back up (like the library's sidebar).</summary>
    private void ToggleNav()
    {
        Host.Settings.SettingsNavCollapsed = !Collapsed;
        Host.Settings.Save();
        SizeNav(animate: true);
        BuildNav();
        FocusLater(_nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == "nav:toggle"));
    }

    private void SizeNav(bool animate)
    {
        double target = Collapsed ? 96 : 300;
        _navPanel.Padding = Collapsed ? new Thickness(8, 12, 8, 12) : new Thickness(10, 12, 10, 12);
        if (!animate || Theme.ReduceMotion)
        {
            _navPanel.BeginAnimation(WidthProperty, null);
            _navPanel.Width = target;
            return;
        }
        var slide = new System.Windows.Media.Animation.DoubleAnimation(_navPanel.ActualWidth > 0 ? _navPanel.ActualWidth : target, target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        slide.Completed += (_, _) => { _navPanel.BeginAnimation(WidthProperty, null); _navPanel.Width = target; };
        _navPanel.BeginAnimation(WidthProperty, slide);
    }

    private void BuildNav()
    {
        _nav.Children.Clear();
        if (Collapsed) { BuildBlade(); return; }

        var shrink = Ui.Row("Show icons only", null, "\uE76B");
        shrink.FontSize = 15;
        shrink.DataContext = "nav:toggle";
        shrink.Margin = new Thickness(0, 0, 0, 6);
        shrink.Click += (_, _) => ToggleNav();
        _nav.Children.Add(shrink);
        _nav.Children.Add(new Border { Height = 2 });

        // You: profile picture, name and version; opens the profile page.
        var me = new DockPanel();
        var face = Avatars.Build(Host.Settings.Avatar, 44);
        face.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(face, Dock.Left);
        me.Children.Add(face);
        var (persona, account) = SteamLibrary.GetSteamNames();
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(persona ?? account ?? "Your profile", 17, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        who.Children.Add(name);
        who.Children.Add(Ui.Text("SquareDeck " + Updater.Current, 13, "SubtleBrush"));
        me.Children.Add(who);
        var profile = new Button { Style = Ui.StyleOf("RowButton"), Content = me, Margin = new Thickness(0, 0, 0, 8), DataContext = "nav:profile" };
        profile.Click += (_, _) => Host.Push(new ProfileScreen());
        _nav.Children.Add(profile);
        _nav.Children.Add(new Border { Height = 2 }); // keeps it out of the group below

        // Find a setting by name.
        var search = Ui.Row("Find a setting", null, "\uE721");
        search.FontSize = 16;
        search.DataContext = "nav:search";
        search.Margin = new Thickness(0, 0, 0, 12);
        search.Click += (_, _) => Host.Push(new SearchScreen(settingsOnly: true));
        _nav.Children.Add(search);
        _nav.Children.Add(new Border { Height = 2 });

        foreach (var c in VisibleCategories)
        {
            string id = c.Id;
            var row = Ui.Row(c.Name, null, c.Glyph);
            row.FontSize = 16;
            row.Margin = new Thickness(0, 1, 0, 1);
            row.DataContext = "nav:" + id;
            if (id == _category) row.Tag = "selected";
            // Moving onto a section shows it; selecting it goes into its settings.
            row.GotKeyboardFocus += (_, _) =>
            {
                _pendingCategory = id;
                _switchSoon.Stop();
                _switchSoon.Start();
            };
            row.Click += (_, _) => Show(id, focusContent: true);
            _nav.Children.Add(row);
        }
        Ui.GroupRows(_nav);
    }

    /// <summary>The sections as a blade of icons: open it up, you, find a setting, then each section.</summary>
    private void BuildBlade()
    {
        Button Add(Button b, string tag, Thickness margin)
        {
            b.DataContext = tag;
            b.Margin = margin;
            _nav.Children.Add(b);
            return b;
        }
        Add(Ui.BladeButton("\uE76C", "Show the names"), "nav:toggle", new Thickness(0, 0, 0, 4)).Click += (_, _) => ToggleNav();
        var face = Avatars.Build(Host.Settings.Avatar, 40);
        Add(Ui.BladeButton(null, "Your profile", picture: face), "nav:profile", new Thickness(0, 4, 0, 0)).Click += (_, _) => Host.Push(new ProfileScreen());
        Add(Ui.BladeButton("\uE721", "Find a setting"), "nav:search", new Thickness(0, 0, 0, 8)).Click += (_, _) => Host.Push(new SearchScreen(settingsOnly: true));
        foreach (var c in VisibleCategories)
        {
            string id = c.Id;
            var row = Add(Ui.BladeButton(c.Glyph, c.Name, selected: id == _category), "nav:" + id, new Thickness(0));
            row.GotKeyboardFocus += (_, _) =>
            {
                _pendingCategory = id;
                _switchSoon.Stop();
                _switchSoon.Start();
            };
            row.Click += (_, _) => Show(id, focusContent: true);
        }
    }

    /// <summary>Shows a section on the right, keeping the focus where it is unless asked to move into it.</summary>
    private void Show(string id, bool focusContent)
    {
        _switchSoon.Stop();
        RememberScroll();
        if (_category != id) _group = null;
        _category = id;
        _lastCategory = id;
        SuppressAutoFocus = true;
        ForgetRow(); // a different section starts at its top
        Rebuild();
        SuppressAutoFocus = false;
        RowsScroll.ScrollToTop();
        MarkNav();
        if (_panes && !focusContent) return; // moving through the sections: the highlight stays on the left
        // The first setting (past the way back), or the first card on Settings home.
        FocusLater(Rows.Children.OfType<Button>().Skip(id == "overview" || _panes ? 0 : 1).FirstOrDefault() ?? Rows.Children.OfType<Button>().FirstOrDefault());
    }

    protected override void Build()
    {
        var s = Host.Settings;
        // The handheld's section only exists on a handheld; elsewhere its TDP settings are in Power and performance.
        if (_category == "handheld" && !Handheld.IsHandheld) _category = "gaming";
        var cat = VisibleCategories.FirstOrDefault(c => c.Id == _category);
        if (cat.Id == null) { _category = "overview"; cat = Categories[0]; }

        // The section's banner: its icon, name and what's in it (and where you are, inside a group).
        // Settings home has no banner: the page is already titled "Settings".
        if (_category == "overview") { Rows.Children.Add(new Border { Height = 6 }); AddUndoRow(); BuildOverview(s); return; }
        Rows.Children.Add(Banner(cat.Glyph, cat.Name, _group == null ? cat.About : null));

        if (_panes) goto sections; // the sections are on the left; no way back needed
        var back = Ui.Row("Settings", null, "\uE72B", subtitle: "Back to all settings");
        back.Click += (_, _) => OnAction(NavAction.Back);
        back.Margin = new Thickness(0, 8, 0, 4);
        back.Tag = "back";
        Rows.Children.Add(back);
        Rows.Children.Add(new Border { Height = 4 });
        sections:
        foreach (var (part, groups) in Sections[_category])
        {
            int start = Rows.Children.Count;
            switch (part)
            {
                case "looks": BuildLooks(s); break;
                case "look": BuildLook(s); break;
                case "home": BuildHome(s); break;
                case "sound": BuildSound(s); break;
                case "controller": BuildController(s); break;
                case "power": BuildPower(s); break;
                case "apps": BuildApps(s); break;
                case "games": BuildGames(s); break;
                case "access": BuildAccess(s); break;
                case "network": BuildNetwork(s); break;
                case "system": BuildSystem(s); break;
                case "data": BuildData(s); break;
                case "experimental": BuildExperimental(s); break;
                case "handheld": BuildHandheld(s); break;
            }
            if (groups != null) KeepGroups(start, groups);
        }
        Organize(cat.Name);
        if (!_indexing) { AddUndoRow(at: 1); AddResetRow(cat.Name); }
    }

    /// <summary>This version's changes, from the changelog that comes with SquareDeck (GitHub's if it's missing).</summary>
    private async Task ShowThisVersionAsync()
    {
        string? log = null;
        try
        {
            string local = System.IO.Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            log = System.IO.File.Exists(local) ? await System.IO.File.ReadAllTextAsync(local) : await Updater.ChangelogAsync(CancellationToken.None);
        }
        catch (Exception) { }
        var current = Updater.Current;
        var previous = new Version(current.Major, current.Minor, Math.Max(0, current.Build - 1));
        string notes = log == null ? "" : Updater.ChangelogSince(log, previous, current);
        if (notes.Length == 0) { Host.Push(new ChangelogScreen()); return; }
        Host.Push(new WhatsNewScreen(previous, current, notes));
    }

    // ---------------------------------------------------------------- changed from the default, reset, undo

    /// <summary>The settings this section (or the group open in it) changes, from SettingsSections' map.</summary>
    private List<string> SectionProps(string? group) =>
        SettingsSections.Properties(Sections[_category].Select(p => (p.Part,
            group == null ? p.Groups : (p.Groups == null || p.Groups.Contains(group) ? new[] { group } : Array.Empty<string>()))));

    /// <summary>"Undo …" for a minute after a big change (a new theme, a section reset).</summary>
    private void AddUndoRow(int at = -1)
    {
        if (Host.Undoable is not { } undo) return;
        var row = Ui.Row("Undo", null, "\uE7A7", subtitle: undo.What);
        row.Click += (_, _) => { Host.RunUndo(); Rebuild(); };
        row.Margin = new Thickness(0, 6, 0, 4);
        if (at >= 0 && at <= Rows.Children.Count) Rows.Children.Insert(at, row);
        else Rows.Children.Add(row);
    }

    /// <summary>At the bottom: how many settings here differ from the default, and a way to put them back.</summary>
    private void AddResetRow(string sectionName)
    {
        var props = SectionProps(_group);
        var changed = SettingsSections.Changed(Host.Settings, props);
        if (changed.Count == 0) return;
        string where = _group ?? sectionName;
        var row = Ui.Row("Reset " + where, $"{changed.Count} changed", "\uE777",
            subtitle: $"Puts the {changed.Count} {(changed.Count == 1 ? "setting" : "settings")} you changed here back to the default. You can undo it for a minute");
        row.Click += async (_, _) =>
        {
            if (await Host.AskAsync("Reset " + where + "?", $"{changed.Count} {(changed.Count == 1 ? "setting goes" : "settings go")} back to the default. Undo is at the top of Settings for a minute afterwards.", "Reset", "Cancel") != 0) return;
            var s = Host.Settings;
            var snapshot = SettingsSections.Reset(s, changed);
            Host.ApplyAfterReset();
            Host.OfferUndo("Reset " + where, () => { SettingsSections.Undo(s, snapshot); Host.ApplyAfterReset(); });
            Rebuild();
        };
        row.Margin = new Thickness(0, 14, 0, 4);
        Rows.Children.Add(row);
    }

    /// <summary>Of the rows added since <paramref name="start"/>, keeps only the named groups (in the order they were built).</summary>
    private void KeepGroups(int start, string[] groups)
    {
        var added = Rows.Children.Cast<UIElement>().Skip(start).ToList();
        bool keep = false;
        foreach (var e in added)
        {
            if (e is TextBlock { Tag: "group" } h) keep = groups.Contains(h.Text);
            if (!keep) Rows.Children.Remove(e);
        }
    }

    // ---------------------------------------------------------------- the Home section: a dashboard

    private void BuildOverview(Settings s)
    {
        // You and your device.
        var me = new DockPanel();
        var face = Avatars.Build(s.Avatar, 64);
        face.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(face, Dock.Left);
        me.Children.Add(face);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Ui.Text(UserName.Display, 24, "FgBrush", bold: true));
        var facts = new List<string> { "SquareDeck " + Updater.Current };
        if (Battery.GetPercent() is int battery) facts.Add(battery + "% battery");
        if (TdpControl.Ready) facts.Add(TdpControl.EffectiveWatts + " W TDP");
        who.Children.Add(Ui.Text(string.Join("   \u00B7   ", facts), 14, "SubtleBrush"));
        me.Children.Add(who);
        var profile = new Button { Style = Ui.StyleOf("RowButton"), Content = me, Padding = new Thickness(18, 14, 18, 14) };
        profile.Click += (_, _) => Host.Push(new ProfileScreen());
        Rows.Children.Add(profile);
        Rows.Children.Add(new Border { Height = 4 });

        var search = Ui.Row("Find a setting", null, "\uE721", subtitle: "Search every setting by name");
        search.Click += (_, _) => Host.Push(new SearchScreen(settingsOnly: true));
        search.Margin = new Thickness(0, 8, 0, 0);
        Rows.Children.Add(search);
        var health = Ui.Row("Device health", null, "\uE95E", subtitle: "Who controls the lights, the TDP and the buttons, anything in the way, and Collect a report");
        health.Click += (_, _) => Host.Push(new DeviceHealthScreen());
        health.Margin = new Thickness(0, 4, 0, 0);
        Rows.Children.Add(health);
        Rows.Children.Add(new Border { Height = 4 });

        // Every section, as a card (two panes list them on the left already).
        if (_panes) return;
        if (LayoutId == "tiles")
        {
            AddHeader("All settings");
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 8) };
            foreach (var c in VisibleCategories.Skip(1))
            {
                var id = c.Id;
                var cardFace = new StackPanel { Width = 220, Height = 132, Margin = new Thickness(12) };
                var glyph = Ui.Glyph(c.Glyph, 40, "AccentBrush");
                glyph.HorizontalAlignment = HorizontalAlignment.Left;
                cardFace.Children.Add(glyph);
                var title = Ui.Text(c.Name, 19, "FgBrush", bold: true);
                title.Margin = new Thickness(0, 12, 0, 2);
                cardFace.Children.Add(title);
                var about = Ui.Text(c.About, 12, "SubtleBrush");
                about.TextWrapping = TextWrapping.Wrap;
                about.TextTrimming = TextTrimming.CharacterEllipsis;
                about.MaxHeight = 34;
                cardFace.Children.Add(about);
                var card = new Border { Child = cardFace, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 8)) };
                card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
                var tile = Ui.Tile(card);
                tile.Margin = new Thickness(6);
                tile.DataContext = "section:" + id;
                tile.Click += (_, _) => Show(id, focusContent: true);
                wrap.Children.Add(tile);
            }
            Rows.Children.Add(wrap);
            return;
        }
        AddHeader("All settings");
        foreach (var c in VisibleCategories.Skip(1))
        {
            var id = c.Id;
            var row = AddRow(c.Name, null, c.Glyph, click: () => Show(id, focusContent: true), subtitle: c.About);
            row.DataContext = "section:" + id;
        }
    }

    /// <summary>Opens a section, a group in it, and puts the highlight on one row.</summary>
    private void Jump(string section, string group, string? row)
    {
        Show(section, focusContent: false);
        _group = group;
        ForgetRow();
        if (row != null) FocusLabel = row;
        Rebuild();
        if (row == null) FocusLater(Rows.Children.OfType<Button>().Skip(1).FirstOrDefault());
    }

    // ---------------------------------------------------------------- groups: a tidy overview, one group at a time

    private string? _group;       // the group open inside the section, or null for the overview
    private bool _indexing;        // building for Search: keep every row

    /// <summary>Headers in Settings mark the start of a group.</summary>
    private new void AddHeader(string text)
    {
        var header = Ui.Header(text);
        header.Tag = "group";
        Rows.Children.Add(header);
    }

    internal new List<(string Label, string? Subtitle)> RowLabels()
    {
        _indexing = true;
        try { return base.RowLabels(); }
        finally { _indexing = false; }
    }

    /// <summary>
    /// Sections with several groups open as a short list of group cards (name, icon, what's inside); choosing one
    /// shows just that group's settings, with a way back. Small sections stay as one list.
    /// </summary>
    private void Organize(string section)
    {
        if (_indexing) return;
        // The way back (to Settings home) stays at the top; it isn't part of any group.
        var backRow = Rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "back");
        var all = Rows.Children.Cast<UIElement>().Skip(1).ToList();
        int backAt = backRow != null ? all.IndexOf(backRow) : -1;
        var body = backAt < 0 ? all : all.Where((e, i) => i != backAt && i != backAt + 1).ToList();
        // Pinned parts (a section's own header card, like the handheld's picture) stay at the top of the hub, in no group.
        var pinned = body.Where(e => e is FrameworkElement { Tag: "pinned" }).ToList();
        body = body.Except(pinned).ToList();
        var groups = new List<(string Name, List<UIElement> Items)>();
        foreach (var e in body)
        {
            if (e is TextBlock { Tag: "group" } h) groups.Add((h.Text, new List<UIElement>()));
            else
            {
                if (groups.Count == 0) groups.Add(("General", new List<UIElement>()));
                groups[^1].Items.Add(e);
            }
        }
        groups.RemoveAll(g => g.Items.Count == 0);
        int rows = body.Count(e => e is Button);
        if (groups.Count <= 1 || rows <= 8) { _group = null; return; }

        // Opened from Search: go straight to the group holding that row.
        if (FocusLabel != null)
            foreach (var g in groups)
                if (g.Items.OfType<Button>().Any(b => LabelOf(b)?.Label == FocusLabel)) { _group = g.Name; break; }

        if (_group != null && groups.FirstOrDefault(g => g.Name == _group) is { Name: not null } open)
        {
            var banner = Rows.Children[0];
            Rows.Children.Clear();
            Rows.Children.Add(banner);
            var back = Ui.Row(section, null, "\uE72B", subtitle: "Back to all " + section.ToLowerInvariant() + " settings");
            back.Click += (_, _) => LeaveGroup();
            back.Margin = new Thickness(0, 6, 0, 4);
            Rows.Children.Add(back);
            Rows.Children.Add(new Border { Height = 6 }); // keeps it out of the group below
            var title = Ui.Text(open.Name, 24, "FgBrush", bold: true);
            title.Margin = new Thickness(6, 14, 0, 8);
            Rows.Children.Add(title);
            foreach (var e in open.Items) Rows.Children.Add(e);
            return;
        }
        _group = null;

        var top = Rows.Children[0];
        Rows.Children.Clear();
        Rows.Children.Add(top);
        if (backRow != null) { Rows.Children.Add(backRow); Rows.Children.Add(new Border { Height = 6 }); }
        foreach (var p in pinned) Rows.Children.Add(p);
        Rows.Children.Add(new Border { Height = 4 });
        foreach (var g in groups)
        {
            var name = g.Name;
            var buttons = g.Items.OfType<Button>().ToList();
            string glyph = buttons.Select(GlyphOf).FirstOrDefault(x => x != null) ?? "\uE713";
            var labels = buttons.Select(b => LabelOf(b)?.Label).Where(l => !string.IsNullOrEmpty(l)).Take(3).ToList();
            var card = new Grid();
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.ColumnDefinitions.Add(new ColumnDefinition());
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 18, 0) };
            var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
            badge.Background = new LinearGradientBrush(Color.FromArgb(0xFF, a.R, a.G, a.B), Color.FromArgb(0x90, a.R, a.G, a.B), 45);
            var icon = Ui.Glyph(glyph, 22, "BgBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = icon;
            card.Children.Add(badge);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(name, 20, "FgBrush", bold: true));
            int changedHere = SettingsSections.Changed(Host.Settings, SectionProps(name)).Count;
            var sub = Ui.Text((changedHere > 0 ? $"\u25CF {changedHere} changed  \u00B7  " : "") + $"{buttons.Count} {(buttons.Count == 1 ? "setting" : "settings")}  \u00B7  {string.Join(", ", labels)}", 14, "SubtleBrush");
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(sub);
            Grid.SetColumn(text, 1);
            card.Children.Add(text);
            var chevron = Ui.Glyph("\uE76C", 16, "SubtleBrush");
            chevron.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chevron, 2);
            card.Children.Add(chevron);
            var button = new Button { Style = Ui.StyleOf("RowButton"), Content = card, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10) };
            button.Click += (_, _) => EnterGroup(name);
            Rows.Children.Add(button);
            Rows.Children.Add(new Border { Height = 0 }); // each card stands on its own (not merged into one group)
        }
    }

    private static string? GlyphOf(Button b)
    {
        string? found = null;
        void Walk(object node)
        {
            if (found != null) return;
            if (node is TextBlock t && Equals(t.FontFamily, Ui.IconFont) && !string.IsNullOrEmpty(t.Text)) { found = t.Text; return; }
            if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
        }
        Walk(b);
        return found;
    }

    /// <summary>How far each section (and group) was scrolled, so stepping back returns to the same place.</summary>
    private readonly Dictionary<string, double> _scrolled = new();
    private string ViewKey => _category + "|" + (_group ?? "");
    private void RememberScroll() => _scrolled[ViewKey] = RowsScroll.VerticalOffset;

    /// <summary>After stepping back: the page scrolled as it was, then the highlight on the row you came from.</summary>
    private void ReturnScroll(Button? row)
    {
        double at = _scrolled.TryGetValue(ViewKey, out var v) ? v : 0;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            RowsScroll.ScrollToVerticalOffset(at);
            if (row != null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { row.Focus(); row.BringIntoView(); }));
        }));
    }

    private void EnterGroup(string name)
    {
        RememberScroll();
        _group = name;
        ForgetRow();
        Rebuild();
        RowsScroll.ScrollToTop();
        // The first setting of the group, not the way back.
        FocusLater(Rows.Children.OfType<Button>().Skip(1).FirstOrDefault() ?? Rows.Children.OfType<Button>().FirstOrDefault());
    }

    private void LeaveGroup()
    {
        var name = _group;
        _group = null;
        ForgetRow();
        Rebuild();
        ReturnScroll(Rows.Children.OfType<Button>().FirstOrDefault(b => LabelOf(b)?.Label == name));
    }

    /// <summary>The section's banner: a big icon in a highlight-colored badge, its name, and what it covers.</summary>
    private static UIElement Banner(string glyph, string name, string? about)
    {
        if (LookChrome.Design.Banner != "badge") return LookChrome.Banner(glyph, name, about);
        var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var panel = new Border
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 4),
            Background = new LinearGradientBrush(Color.FromArgb(0x48, a.R, a.G, a.B), Color.FromArgb(0x08, a.R, a.G, a.B), 0)
        };
        var row = new DockPanel();
        var badge = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 18, 0), Background = new SolidColorBrush(a) };
        var icon = Ui.Glyph(glyph, 28, "BgBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        badge.Child = icon;
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(name, 30, "FgBrush", bold: true));
        if (about != null)
        {
            var sub = Ui.Text(about, 15, "SubtleBrush");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.TextTrimming = TextTrimming.None;
            text.Children.Add(sub);
        }
        row.Children.Add(text);
        panel.Child = row;
        return panel;
    }

    private static readonly int[] TextSizes = { 100, 115, 130, 150 };

    /// <summary>Accessibility, in its own section.</summary>
    private void BuildAccess(Settings s)
    {
        void Apply()
        {
            s.Save();
            Host.ApplyLook();
            Host.ApplySettings();
            Rebuild();
        }

        AddHeader("Seeing");
        AddRow("Text and control size", s.TextScale + "%", "\uE8D3", click: () =>
        {
            s.TextScale = TextSizes[(Array.IndexOf(TextSizes, s.TextScale) + 1) % TextSizes.Length];
            s.Save();
            Host.ApplyLook();
            Host.ApplySettings();
            Host.Pop(this);                          // pages take the new size when they open,
            Host.Push(new SettingsScreen("access")); // so reopen this one to show it straight away
        });
        AddNote("Makes text and buttons larger on every page. The home screen keeps its layout; its tiles are already large.");
        string contrast = s.ThemeMode switch { "hc-dark" => "White on black", "hc-light" => "Black on white", _ => "Off" };
        AddRow("High contrast", contrast, "\uE793", click: () =>
        {
            s.ThemeMode = s.ThemeMode switch { "hc-dark" => "hc-light", "hc-light" => "system", _ => "hc-dark" };
            s.DarkTheme = Theme.ShouldBeDark(s);
            Apply();
        });
        AddRow("Color filters (Windows)", glyph: "\uE790", click: () => ProcessUtil.Open("ms-settings:easeofaccess-colorfilter"));
        AddNote("Grayscale, inverted and color-blindness filters for the whole screen, games included. Win+Ctrl+C switches them on and off.");

        AddRow("Thicker highlight", OnOff(s.ThickFocusRing), "", click: () =>
        {
            s.ThickFocusRing = !s.ThickFocusRing;
            Apply();
        }, subtitle: "A bolder ring around whatever is selected, so it's easier to find");
        AddRow("Magnifier (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-magnifier"),
            subtitle: "Zoom into any part of the screen. Win+Plus starts it, Win+Esc stops it");
        AddRow("Mouse pointer size and color (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-mousepointer"));
        AddRow("Font", glyph: "", click: () => Show("look", focusContent: true), subtitle: "Pick an easier-to-read font in Look and feel");

        AddHeader("Motion");
        AddRow("Reduce motion", OnOff(s.ReduceMotion), "", click: () =>
        {
            s.ReduceMotion = !s.ReduceMotion;
            Apply();
        });
        AddNote("Stops the pulsing highlight, page slides, fades, animated covers and the startup animation.");
        AddRow("Covers that tilt", Labs.On("tilt") ? "On" : "Off", "", click: () => { Labs.Set("tilt", !Labs.On("tilt")); Rebuild(); },
            subtitle: "Turn off if movement on screen makes you uncomfortable");

        AddHeader("Hearing");
        AddRow("Flash the screen for alerts", OnOff(s.VisualSounds), "", click: () =>
        {
            s.VisualSounds = !s.VisualSounds;
            s.Save();
            Rebuild();
            if (s.VisualSounds) UiSound.Toast(); // shows what it looks like
        }, subtitle: "The edge of the screen glows with notifications, and red for errors and low battery, even with sounds off");
        AddRow("Live captions (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-hearing"),
            subtitle: "Captions for any sound on the PC, games and videos included. Win+Ctrl+L turns them on");
        AddRow("Mono audio (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-audio"),
            subtitle: "Both ears hear everything: for one earbud or hearing in one ear");
        AddRow("Interface sounds", glyph: "", click: () => Show("sound", focusContent: true), subtitle: "Turn them up, down or off in Sound and music");

        AddHeader("Controls and timing");
        string pace = s.NavRepeat switch { "slow" => "Slower", "off" => "Off (one step per press)", _ => "Normal" };
        AddRow("Holding a direction", pace, "", click: () =>
        {
            s.NavRepeat = s.NavRepeat switch { "normal" => "slow", "slow" => "off", _ => "normal" };
            Apply();
        }, subtitle: "How fast the highlight keeps moving while you hold the d-pad or a stick. Slower is easier to stop where you want");
        string toast = s.ToastSeconds switch { >= 15 => "15 seconds", >= 9 => "9 seconds", _ => "4½ seconds (usual)" };
        AddRow("Notifications stay on screen", toast, "", click: () =>
        {
            s.ToastSeconds = s.ToastSeconds switch { >= 15 => 0, >= 9 => 15, _ => 9 };
            s.Save();
            Rebuild();
        }, subtitle: "Longer gives more time to read them");
        AddRow("Swap A and B, or other buttons", glyph: "", click: () => Show("controller", focusContent: true),
            subtitle: "Which button selects and which goes back, in Controller and Game Bar widget");
        AddRow("Vibration", s.RumbleStrength > 0 ? s.RumbleStrength + "%" : "Off", "", click: () => Show("controller", focusContent: true),
            subtitle: "How strongly the controller buzzes, or off, in Controller and Game Bar widget");
        AddRow("On-screen keyboard and typing (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-keyboard"),
            subtitle: "Sticky keys, filter keys and the on-screen keyboard");

        AddHeader("Reading aloud");
        AddRow("Narrator (Windows)", glyph: "", click: () => ProcessUtil.Open("ms-settings:easeofaccess-narrator"),
            subtitle: "Reads out what's on screen. Win+Ctrl+Enter turns it on and off");
    }

    private static string _lastCategory = "overview";


    /// <summary>
    /// Themes: the looks to choose from, each a complete design for SquareDeck (its home screen and every
    /// page). Switch is the only one for now; the theme engine is being built up a step at a time.
    /// </summary>
    private void BuildLooks(Settings s)
    {
        // Switch is SquareDeck's look; these are its ready-made styles (UiStyles.ReadyMadeCount counts them) (all with the Switch layout) and yours, each with a little picture of its home screen.
        AddRow("Match theme, wallpaper, sounds and music to the style", OnOff(s.StyleMatching), "\uE790",
            click: () => Toggle(() => s.StyleMatching = !s.StyleMatching),
            subtitle: "Off keeps your own theme, wallpaper, sounds, music and highlight color when you change style");
        // Basic: the styles drawn by SquareDeck (and yours). Advanced: styles made from UI kits, with their own
        // panels, buttons, switches, pointer and icons.
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 2) };
        foreach (var (id, label, glyph) in new[] { ("basic", "Basic", "\uE771"), ("advanced", "Advanced", "\uE7F4"), ("legendary", "Legendary", "\uE734"), ("mythical", "Mythical", "\uE945"), ("unlockable", "Unlockable", "\uE72E") })
        {
            var chip = new Button
            {
                Style = Ui.StyleOf("ChipButton"), Tag = _stylesTab == id ? "active" : null,
                Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) },
                        new TextBlock { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            string tab = id;
            chip.Click += (_, _) =>
            {
                if (_stylesTab == tab) return;
                _stylesTab = tab;
                UiSound.Select();
                Rebuild();
                FocusLater(Rows.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).FirstOrDefault(b => b.Tag as string == "active"));
            };
            tabs.Children.Add(chip);
        }
        Rows.Children.Add(tabs);
        const double W = 296;
        if (_stylesTab == "advanced") { BuildAdvancedStyles(s, W, legendary: false); return; }
        if (_stylesTab == "legendary") { BuildAdvancedStyles(s, W, legendary: true); return; }
        if (_stylesTab == "mythical") { BuildAdvancedStyles(s, W, legendary: false, mythical: true); return; }
        if (_stylesTab == "unlockable") { BuildAdvancedStyles(s, W, legendary: false, unlockable: true); return; }

        AddRow("Surprise me", null, "\uE8B1", subtitle: "Switch to a random style you're not using now", click: () =>
        {
            var others = UiStyles.All.Where(x => x.Id != s.UiStyle).ToList();
            if (others.Count > 0) UseSwitchStyle(others[Random.Shared.Next(others.Count)]);
        });

        // Your own styles: make one, import one, and the ones you've made.
        AddHeader("Your styles");
        var mine = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        mine.Children.Add(ActionCard("\uE710", "Create a style", "Pick a background, music, sounds, colors, font, buttons, an effect and animations", W, () => _ = CreateStyleAsync()));
        mine.Children.Add(ActionCard("\uE8B5", "Import a style", "Add a style someone shared with you (a " + MyStyles.Extension + " file)", W, () => _ = ImportStyleAsync()));
        foreach (var custom in s.CustomStyles.ToList())
        {
            var c = custom;
            var st = UiStyles.Find(c.Id);
            if (st.Id != c.Id) continue;
            var tile = StyleCard(st, st.Id == UiStyles.Current.Id || s.UiStyle == st.Id, W);
            tile.Click += (_, _) => _ = MyStyleMenuAsync(c);
            mine.Children.Add(tile);
        }
        Rows.Children.Add(mine);

        AddHeader("Ready-made styles  ·  " + UiStyles.All.Length);
        // Three to a row: the column is 1040 wide, and each card adds the tile's padding and ring plus a 10 gap.
        var shelf = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var style in UiStyles.All)
        {
            var st = style;
            bool inUse = st.Id == UiStyles.Current.Id;
            var tile = StyleCard(st, inUse, W);
            tile.Click += async (_, _) =>
            {
                // A style that comes in several colors and asks first (Spectrum): pick the color, then it's applied.
                if (st.Skin is { AskColor: true, Variants.Length: > 1 } colors)
                {
                    int pick = await Host.AskAsync(st.Name, "Which color would you like?", colors.Variants.Select(v => v.Name).ToArray());
                    if (pick < 0) return;
                    s.SkinVariants[st.Id] = colors.Variants[pick].Id;
                    s.Save();
                    if (inUse) { Host.ApplyLook(); Rebuild(); }
                    else UseSwitchStyle(st);
                    return;
                }
                if (inUse) { Host.ShowToast("Themes", st.Name + " is in use", "It's the style SquareDeck has now.", "", "info", remember: false); return; }
                UseSwitchStyle(st);
            };
            shelf.Children.Add(tile);
        }
        Rows.Children.Add(shelf);
        AddNote("Each style keeps the Switch layout and changes its colors, font, corners, buttons and artwork. With matching " +
                "on, it also brings its own theme (light, dark or OLED), wallpaper or moving background, interface sounds and music.");

        AddHeader("More");
        AddFeature("styleschedule");
        AddRow("Colors, wallpaper and more", null, "\uE790", click: () => Show("look", focusContent: true),
            subtitle: "Light or dark, color themes, the highlight color, wallpaper and the moving background");
        AddRow("Sounds and music", null, "\uE767", click: () => Show("sound", focusContent: true),
            subtitle: "Interface sounds and menu music");
    }

    /// <summary>Which tab of Themes is showing: the Basic styles, the Advanced ones or the Legendary ones.</summary>
    private static string _stylesTab = UiStyles.Current.Unlock != null ? "unlockable" : UiStyles.IsMythical(UiStyles.Current.Id) ? "mythical" : UiStyles.IsLegendary(UiStyles.Current.Id) ? "legendary" : UiStyles.IsAdvanced(UiStyles.Current.Id) ? "advanced" : "basic";

    /// <summary>
    /// The Advanced tab (styles made from UI kits, each with a color choice where the kit has several) or the Legendary
    /// tab (Advanced styles whose artwork was drawn from scratch by Claude). Both are UiStyles.Advanced, split by Legendary.
    /// </summary>
    private void BuildAdvancedStyles(Settings s, double W, bool legendary, bool unlockable = false, bool mythical = false)
    {
        var current = UiStyles.Current;
        var list = unlockable ? UiStyles.Advanced.Where(x => x.Unlock != null).ToList()
                              : mythical ? UiStyles.Advanced.Where(x => x.Mythical).ToList()
                              : UiStyles.Advanced.Where(x => x.Legendary == legendary && !x.Mythical && x.Unlock == null).ToList();
        if (unlockable)
        {
            foreach (var st in StyleUnlocks.Update(Host.AllGames, Host.Playtime, Host.Achievements, Host.Library))
                Host.ShowToast("Themes", "New style unlocked: " + st.Name, "It's yours to use now.", "", "info");
        }
        if (list.Any(x => x.Id == current.Id) && current.Skin is { Variants.Length: > 1 } skin)
        {
            AddHeader(current.Name);
            string chosen = skin.Variants.FirstOrDefault(v => v.Id == current.SkinVariant).Name ?? skin.Variants[0].Name;
            AddRow("Color", chosen, "\uE790", subtitle: "The kit's own color versions of its panels and buttons", click: async () =>
            {
                int pick = await Host.AskAsync("Color", null, skin.Variants.Select(v => v.Name).ToArray());
                if (pick < 0) return;
                s.SkinVariants[current.Id] = skin.Variants[pick].Id;
                s.Save();
                Host.ApplyLook();
                Rebuild();
            });
        }

        AddHeader((unlockable ? $"Unlockable styles  ·  {list.Count(x => !StyleUnlocks.IsLocked(x))} of {list.Count} unlocked"
                   : mythical ? "Mythical styles  ·  " + list.Count
                   : legendary ? "Legendary styles  ·  " + list.Count : "Advanced styles  ·  " + list.Count));
        var shelf = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var style in list)
        {
            var st = style;
            bool inUse = st.Id == current.Id;
            s.SkinVariants.TryGetValue(st.Id, out var variant);
            StyleUnlocks.Progress? progress = st.Unlock != null && StyleUnlocks.IsLocked(st)
                ? StyleUnlocks.Check(st, Host.AllGames, Host.Playtime, Host.Achievements, Host.Library) : null;
            var tile = AdvancedCard(st, variant, inUse, W, progress);
            tile.Click += (_, _) =>
            {
                if (progress is { } p) { UiSound.Back(); Host.ShowToast("Themes", st.Name + " is locked", p.Goal + "  (" + p.Have + ")", "", "info", remember: false); return; }
                if (inUse) { Host.ShowToast("Themes", st.Name + " is in use", "It's the style SquareDeck has now.", "", "info", remember: false); return; }
                UseSwitchStyle(st);
            };
            shelf.Children.Add(tile);
        }
        Rows.Children.Add(shelf);
        if (unlockable)
            AddNote("Unlockable styles are earned by playing: each one opens up when you reach its goal, and SquareDeck lets you know. " +
                    "They're Advanced styles with their own artwork, font, sounds and music.");
        else if (mythical)
            AddNote("Mythical styles are the top tier: each one is a world of its own, drawn from scratch down to the last detail. Their " +
                    "cards, buttons, pop-ups, switches, check boxes, pointer, icons and wallpaper share one idea, with a font, a highlight in its own " +
                    "colors, interface sounds, music and a moving touch on the home screen to match.");
        else if (legendary)
            AddNote("Legendary styles are drawn from scratch: every panel, button, pop-up, " +
                    "switch, check box, pointer, icon and wallpaper. They keep the Switch layout, and with matching on each brings its own " +
                    "theme, font, interface sounds and music.");
        else
            AddNote("Advanced styles keep the Switch layout and dress it in a UI kit's own artwork: panels, buttons, dialogs, " +
                    "switches, the mouse pointer and, for some, the round buttons' icons. With matching on, each brings its own theme, " +
                    "background, interface sounds and music.");
        AddRow("Credits", null, "\uE946", subtitle: "The artists behind these kits, sounds and fonts", click: () => Host.Push(new CreditsScreen()));
    }

    /// <summary>An Advanced style's card: a picture of its artwork, its name in its own font, and its colors.</summary>
    internal static Button AdvancedCard(UiStyle st, string? variant, bool inUse, double W, StyleUnlocks.Progress? locked = null)
    {
        var card = new StackPanel { Width = W };
        var frame = new Grid { Width = W, Height = W * 191 / 340, ClipToBounds = true };
        var back = new Border { CornerRadius = new CornerRadius(10) };
        back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        frame.Children.Add(back);
        if (st.Skin is { } skin)
        {
            string path = System.IO.Path.Combine(Services.Skins.FolderFor(skin, variant), "preview.jpg");
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var picture = new System.Windows.Media.Imaging.BitmapImage();
                    picture.BeginInit();
                    picture.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    picture.DecodePixelWidth = 680;
                    picture.UriSource = new Uri(path);
                    picture.EndInit();
                    picture.Freeze();
                    frame.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill }
                    });
                }
            }
            catch (Exception) { }
        }
        if (st.Mythical)
        {
            // Mythical: a frame of the style's own highlight colors with a light sweeping round it, and a gem badge.
            var colors = (st.RingColors ?? new[] { "#F2C760", "#B36BFF", "#5CF2B0", "#F2C760" })
                .Select(c => (Color)ColorConverter.ConvertFromString(c)).ToArray();
            var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            for (int i = 0; i < colors.Length; i++) rim.GradientStops.Add(new GradientStop(colors[i], i / (double)Math.Max(1, colors.Length - 1)));
            var spin = new RotateTransform(0, 0.5, 0.5);
            rim.RelativeTransform = spin;
            if (!Theme.ReduceMotion)
                spin.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), BorderBrush = rim });
            var gem = new StackPanel { Orientation = Orientation.Horizontal };
            gem.Children.Add(new TextBlock { Text = "\u25C6", FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            gem.Children.Add(new TextBlock { Text = "MYTHICAL", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            var badgeBack = new LinearGradientBrush(colors.First(), colors[colors.Length / 2], 0);
            frame.Children.Add(new Border
            {
                Child = gem, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(10, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = badgeBack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6, Color = colors.First() }
            });
        }
        if (inUse)
        {
            var badgeText = new StackPanel { Orientation = Orientation.Horizontal };
            var tick = Ui.Glyph("\uE73E", 13, "BgBrush");
            tick.Margin = new Thickness(0, 1, 6, 0);
            badgeText.Children.Add(tick);
            badgeText.Children.Add(Ui.Text("In use", 13, "BgBrush", bold: true));
            var badge = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 5), Margin = new Thickness(0, 10, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = badgeText };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            frame.Children.Add(badge);
        }
        if (locked is { } lp)
        {
            // Locked: the picture dimmed, a lock, and how close it is.
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0C, 0x0E, 0x14)) });
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var lockIcon = Ui.Glyph("\uE72E", 34, "FgBrush");
            lockIcon.Foreground = Brushes.White;
            lockIcon.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(lockIcon);
            var have = Ui.Text(lp.Have, 14, "FgBrush", bold: true);
            have.Foreground = Brushes.White;
            have.HorizontalAlignment = HorizontalAlignment.Center;
            have.Margin = new Thickness(0, 8, 0, 6);
            center.Children.Add(have);
            var track = new Border { Width = W * 0.6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)) };
            var fill = new Border { Width = Math.Max(6, W * 0.6 * lp.Fraction), Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            center.Children.Add(new Grid { Children = { track, fill }, Width = W * 0.6 });
            frame.Children.Add(center);
        }
        card.Children.Add(frame);
        var name = Ui.Text(st.Name, 18, "FgBrush", bold: true);
        name.Margin = new Thickness(4, 10, 0, 2);
        try { name.FontFamily = Services.AppFonts.Family(st.Font); } catch (Exception) { }
        card.Children.Add(name);
        string about = locked is { } goal ? "To unlock: " + goal.Goal + ". " + st.Description : st.Description;
        if (st.Skin is { Variants.Length: > 1 } many) about += " · " + many.Variants.Length + " colors";
        var text = Ui.Text(about, 13, "SubtleBrush");
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(4, 0, 4, 4);
        card.Children.Add(text);
        var tile = Ui.Tile(card);
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.VerticalAlignment = VerticalAlignment.Top;
        return tile;
    }

    /// <summary>A style's card: a little picture of its home screen, its name in its own font, and what it's like.</summary>
    internal static Button StyleCard(UiStyle st, bool inUse, double W)
    {
        var card = new StackPanel { Width = W };
        var frame = new Grid();
        frame.Children.Add(new Viewbox { Width = W, Stretch = Stretch.Uniform, Child = UiStyleScreen.Preview(st) });
        if (inUse)
        {
            // "In use" badge in the corner.
            var badgeText = new StackPanel { Orientation = Orientation.Horizontal };
            var tick = Ui.Glyph("\uE73E", 13, "BgBrush");
            tick.Margin = new Thickness(0, 1, 6, 0);
            badgeText.Children.Add(tick);
            badgeText.Children.Add(Ui.Text("In use", 13, "BgBrush", bold: true));
            var badge = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 5), Margin = new Thickness(0, 10, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = badgeText };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            frame.Children.Add(badge);
        }
        card.Children.Add(frame);
        var name = Ui.Text(st.Name, 18, "FgBrush", bold: true);
        name.Margin = new Thickness(4, 10, 0, 2);
        // The name is set in the style's own font, so the type is part of the preview.
        try { name.FontFamily = Services.AppFonts.Family(st.Font); } catch (Exception) { }
        card.Children.Add(name);
        var about = Ui.Text(st.Description, 13, "SubtleBrush");
        about.TextWrapping = TextWrapping.Wrap;
        about.TextTrimming = TextTrimming.None;
        about.Margin = new Thickness(4, 0, 4, 4);
        card.Children.Add(about);
        var tile = Ui.Tile(card);
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.VerticalAlignment = VerticalAlignment.Top;
        return tile;
    }

    /// <summary>A card that does something (Create a style, Import a style): an icon where the picture would be.</summary>
    private static Button ActionCard(string glyph, string title, string about, double W, Action click)
    {
        var card = new StackPanel { Width = W };
        var face = new Grid { Width = W, Height = W * 9 / 16 };
        var back = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2) };
        back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        back.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        face.Children.Add(back);
        var icon = Ui.Glyph(glyph, 46, "AccentBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        face.Children.Add(icon);
        card.Children.Add(face);
        var name = Ui.Text(title, 18, "FgBrush", bold: true);
        name.Margin = new Thickness(4, 10, 0, 2);
        card.Children.Add(name);
        var text = Ui.Text(about, 13, "SubtleBrush");
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(4, 0, 4, 4);
        card.Children.Add(text);
        var tile = Ui.Tile(card);
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Click += (_, _) => click();
        return tile;
    }

    /// <summary>Create a style: start from Switch, the style in use, or any ready-made style, then the builder opens.</summary>
    private async Task CreateStyleAsync()
    {
        var s = Host.Settings;
        var current = UiStyles.Find(s.UiStyle);
        // A style of yours in use: start from what it was built on, with its choices.
        var currentMine = s.CustomStyles.FirstOrDefault(c => c.Id == current.Id);
        // With Switch in use, "Start from Switch" and "the style in use" would be the same choice: offered once.
        bool switchInUse = current.Id == UiStyles.All[0].Id;
        var options = new List<string> { switchInUse ? "Start from Switch (in use now)" : "Start from Switch" };
        if (!switchInUse) options.Add("Start from " + current.Name + " (in use now)");
        options.Add("Choose a ready-made style to start from…");
        options.Add("Cancel");
        int choice = await Host.AskAsync("Create a style", "Start from a ready-made style, then change anything you like. The picture of the home screen follows every change.",
            options.ToArray());
        if (choice < 0) return;
        if (switchInUse && choice >= 1) choice++; // the same numbering as with both offered
        string name = MyStyles.UniqueName(s, "My style");
        void Open(CustomStyle draft)
        {
            try { Host.Push(new StyleBuilderScreen(draft, isNew: true, saved: Rebuild)); }
            catch (Exception ex) { _ = Host.AskAsync("Couldn't open the style builder", ex.Message, "OK"); }
        }
        switch (choice)
        {
            case 0: Open(CustomStyle.From(UiStyles.All[0], s, name)); break;
            case 1:
                if (currentMine != null) { var copy = MyStyles.Duplicate(currentMine, name); Open(copy); }
                else Open(CustomStyle.From(current, s, name));
                break;
            case 2:
                var styles = UiStyles.All.Select(x => (x.Id, x.Name, x.Description)).ToArray();
                PreviewPickerScreen? picker = null;
                picker = new PreviewPickerScreen("Start from", "Choose the ready-made style to build yours on.", styles, () => "",
                    id => new Viewbox { Width = PreviewPickerScreen.W, Height = PreviewPickerScreen.H, Stretch = Stretch.UniformToFill, Child = UiStyleScreen.Preview(UiStyles.Find(id)) },
                    id =>
                    {
                        Host.Pop(picker!);
                        Open(CustomStyle.From(UiStyles.Find(id), s, name));
                    });
                Host.Push(picker);
                break;
        }
    }

    private async Task ImportStyleAsync()
    {
        var file = await Host.PickFileAsync("Choose a SquareDeck style", new[] { MyStyles.Extension });
        if (file == null) return;
        try
        {
            var s = Host.Settings;
            var style = MyStyles.Import(file, s);
            s.CustomStyles.Insert(0, style);
            s.Save();
            MyStyles.Load(s);
            Rebuild();
            if (await Host.AskAsync(style.Name + " imported", "It's in Your styles now.", "Use it now", "Edit it first", "Later") is int c && c == 0)
                UseSwitchStyle(UiStyles.Find(style.Id));
            else if (c == 1) Host.Push(new StyleBuilderScreen(style.Copy(), isNew: false, saved: Rebuild));
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't import the style", ex.Message, "OK");
        }
    }

    private async Task MyStyleMenuAsync(CustomStyle c)
    {
        var s = Host.Settings;
        bool inUse = s.UiStyle == c.Id;
        int choice = await Host.AskAsync(c.Name, "Your style, saved " + c.Saved.ToString("MMMM d, yyyy"),
            inUse ? "Use it again (reapply)" : "Use this style", "Edit", "Duplicate", "Export", "Rename", "Delete", "Cancel");
        switch (choice)
        {
            case 0: UseSwitchStyle(UiStyles.Find(c.Id)); break;
            case 1:
                try { Host.Push(new StyleBuilderScreen(c.Copy(), isNew: false, saved: Rebuild)); }
                catch (Exception ex) { await Host.AskAsync("Couldn't open the style builder", ex.Message, "OK"); }
                break;
            case 2:
                var copy = MyStyles.Duplicate(c, MyStyles.UniqueName(s, c.Name + " copy"));
                s.CustomStyles.Insert(s.CustomStyles.IndexOf(c) + 1, copy);
                s.Save();
                MyStyles.Load(s);
                Rebuild();
                break;
            case 3: await StyleBuilderScreen.ExportStyleAsync(Host, c); break;
            case 4:
                var name = await Host.PromptAsync("Rename", null, c.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                c.Name = MyStyles.UniqueName(s, name, c.Id);
                s.Save();
                MyStyles.Load(s);
                if (inUse) Host.ApplyLook();
                Rebuild();
                break;
            case 5:
                if (await Host.AskAsync("Delete " + c.Name + "?", "This can't be undone. Export it first if you might want it again.", "Delete", "Cancel") != 0) return;
                MyStyles.Delete(s, c);
                s.Save();
                MyStyles.Load(s);
                if (inUse) UseSwitchStyle(UiStyles.All[0]);
                else Rebuild();
                break;
        }
    }

    /// <summary>Uses a style and redraws this page in it.</summary>
    private void UseSwitchStyle(UiStyle style)
    {
        Host.UseStyle(style);
        UiSound.Select();
        Host.ShowToast("Themes", style.Name + " applied", Host.Settings.StyleMatching ? "With its colors, wallpaper, sounds and music" : "Your colors, wallpaper and sounds are kept", "", "info", remember: false);
        var fresh = new SettingsScreen("looks");
        Host.Push(fresh);
        Host.RemoveBelow(this);
    }

    private void BuildLook(Settings s)
    {
        AddHeader("Background");
        AddRow("Moving background", s.DynamicBackground.Length == 0 ? "Off" : System.IO.Path.GetFileNameWithoutExtension(s.DynamicBackground),
            "\uE714", click: () => Host.Push(new DynamicBackgroundScreen()), subtitle: "Waves or blades in motion, with a blur level");
        AddRow("Windows lock screen", glyph: "\uEE3F", click: () => Host.Push(new LockScreenScreen()),
            subtitle: "Use one of SquareDeck's wallpapers, or a picture of your own, when the device is locked");
        AddHeader("Colors");
        // Light, dark, OLED or following Windows: only the Switch style lets you choose; the others bring their own.
        if (UiStyles.Current.Id == "switch")
        AddRow("Theme", ThemeLabel(s.ThemeMode), "", click: () =>
        {
            // Light -> Dark -> OLED black -> Follow system -> Light
            s.ThemeMode = s.ThemeMode switch { "light" => "dark", "dark" => "oled", "oled" => "system", _ => "light" };
            s.DarkTheme = Theme.ShouldBeDark(s);
            Theme.Apply(s);
            s.Save();
            Rebuild();
        });
        AddRow("Highlight color", AccentLabel(s.AccentColor), "", click: () => Host.Push(new AccentScreen()));
        AddRow("Tint the highlight with the game's colors", OnOff(s.GameAccent), "", click: () =>
            Toggle(() => s.GameAccent = !s.GameAccent));

        // Your own themes, ready-made looks and wallpaper: only for the Switch style (the others bring their own).
        if (UiStyles.Current.Id == "switch")
        {
            AddHeader("Your looks");
            AddRow("My themes, looks and wallpaper", glyph: "", click: () => Host.Push(new ThemeScreen()));
        }
        else AddHeader("Profile"); // just your picture with the other styles
        AddRow("Profile picture", glyph: "", click: () => Host.Push(new AvatarScreen()));

        AddHeader("Artwork");
        AddRow("Hero background behind the home row", OnOff(s.HeroBackgrounds), "", click: () =>
            Toggle(() => s.HeroBackgrounds = !s.HeroBackgrounds),
            subtitle: s.DynamicBackground.Length > 0 ? "Turned off while a moving background is on" : null);
        AddRow("Animated cover on the highlighted game", OnOff(s.AnimatedCovers), "", click: () =>
            Toggle(() => s.AnimatedCovers = !s.AnimatedCovers));
        AddRow("Color the game details page from the cover art", OnOff(s.DetailsGradient), "", click: () =>
            Toggle(() => s.DetailsGradient = !s.DetailsGradient));

        AddHeader("Font");
        AddRow("Font", UiFonts.FirstOrDefault(f => f.Id == s.UiFont).Name ?? "Match the style", "\uE8D2",
            click: () => Preview("Font", "Used everywhere in SquareDeck. Each is shown in its own type; choose one to use it.", UiFonts, () => s.UiFont, PreviewPickerScreen.Font, id => s.UiFont = id),
            subtitle: $"Every UI style comes with its own font. Or pick one of {UiFonts.Length - 1} to use with every style");
        // A sample of the font in use, right here.
        Rows.Children.Add(PreviewPickerScreen.Font(s.UiFont));

        AddHeader("Top bar");
        AddRow("Show the clock", OnOff(s.ShowClock), "\uE823", click: () => Toggle(() => s.ShowClock = !s.ShowClock));
        AddRow("Clock", (s.Clock24Hour ? "24-hour" : "12-hour") + (s.ClockSeconds ? ", seconds" : "") + (s.ClockDate ? ", date" : ""),
            "", click: ClockOptions);
        AddRow("Show battery percentage", OnOff(s.ShowBatteryPercent), "", click: () =>
            Toggle(() => s.ShowBatteryPercent = !s.ShowBatteryPercent));
        AddRow("Headphone battery", OnOff(s.ShowHeadphoneBattery), "\uE7F6", click: () =>
            Toggle(() => s.ShowHeadphoneBattery = !s.ShowHeadphoneBattery), subtitle: "Beside the Bluetooth icon, when Bluetooth headphones are connected");

        AddHeader("Display");
        var displays = Monitors.List();
        var chosen = Monitors.Find(s.MainDisplay);
        AddRow("Main display for the launcher", chosen?.Label ?? (s.MainDisplay.Length > 0 ? "Not connected (using Windows' choice)" : "Windows decides"),
            "\uE7F4", click: ChooseDisplay);
        if (displays.Count < 2) AddNote("Only one display is connected right now. The choice is remembered for when another is plugged in (a TV or dock).");
        var night = NightLight.IsOn;
        AddRow("Night light (warmer colors, easier on the eyes)", night == null ? "Set in Windows" : OnOff(night.Value), "\uE708",
            click: ToggleNightLight);
        AddRow("Night light strength and schedule", glyph: "\uE713", click: () => ProcessUtil.Open("ms-settings:nightlight"));
        AddRow("Screen shape", s.AspectRatio switch { "16:10" => "16:10", "auto" => "Automatic (match the screen)", _ => "16:9" }, "",
            click: () => Toggle(() => s.AspectRatio = s.AspectRatio switch { "16:9" => "16:10", "16:10" => "auto", _ => "16:9" }));
        AddNote("16:10 fills handhelds and laptops with taller screens (1280 x 800, 1920 x 1200, 2560 x 1600) edge to edge " +
                "instead of leaving bars at the top and bottom.");

        AddHeader("Settings view and unlockables");
        AddFeature("settingsview");
        AddFeaturePage("unlockables", "Unlockables", "Highlight colors and companion outfits you earn by playing, and the ones still locked");
    }

    internal static readonly (string Id, string Name)[] ButtonStyles =
        { ("grow", "Grow and glow"), ("lift", "Lift"), ("bounce", "Bounce"), ("pulse", "Pulse"), ("wiggle", "Wiggle") };
    internal static readonly (string Id, string Name)[] PageStyles =
        {
            ("slide", "Slide up"), ("slide-left", "Slide in from the side"), ("slide-down", "Drop down"), ("rise", "Rise"),
            ("fade", "Fade"), ("zoom", "Zoom"), ("pop", "Pop"), ("spring", "Spring"), ("drop", "Bounce"), ("flip", "Card flip"),
            ("swing", "Swing"), ("focus", "Come into focus"), ("none", "None")
        };
    private static readonly (string Id, string Name)[] TileStyles = Motion.TileHighlights;

    private async Task ChooseAsync((string Id, string Name)[] options, string title, string? message, Action<string> set)
    {
        int choice = await Host.AskAsync(title, message, options.Select(o => o.Name).ToArray());
        if (choice >= 0) Toggle(() => set(options[choice].Id));
    }

    private void BuildHome(Settings s)
    {
        AddHeader("Home row and buttons");
        AddRow("Game art", UiStyles.TileArts.FirstOrDefault(x => x.Id == s.TileArt).Name ?? "Default", "\uE91B",
            click: () => Preview("Game art", "How games and apps look on the home row, in Favorites and in Apps. Choose one to use it.", UiStyles.TileArts,
                () => s.TileArt, id => PreviewPickerScreen.Art(id, s.TileSize), id => s.TileArt = id),
            subtitle: "Square, tall grid, or rectangle (header capsule)");
        AddRow("Game art size", UiStyles.TileSizes.FirstOrDefault(x => x.Id == s.TileSize).Name ?? "Default", "\uE740",
            click: () => Preview("Game art size", "How big the tiles are on the home row. Choose one to use it.", UiStyles.TileSizes,
                () => s.TileSize, id => PreviewPickerScreen.Art(s.TileArt == "style" ? "square" : s.TileArt, id), id => s.TileSize = id));
        AddRow("Continue where you left off", OnOff(s.ContinueTile), "\uE768", click: () =>
            Toggle(() => s.ContinueTile = !s.ContinueTile),
            subtitle: "Your last game as a large tile with its artwork, first on the home row");

        AddHeader("Buttons under the games");
        AddRow("Music button", OnOff(s.ShowMusicButton), "", click: () => Toggle(() => s.ShowMusicButton = !s.ShowMusicButton));
        AddRow("Friends button", OnOff(s.ShowFriendsButton), "", click: () => Toggle(() => s.ShowFriendsButton = !s.ShowFriendsButton));
        AddRow("Web browser button", OnOff(s.ShowBrowserButton), "", click: () => Toggle(() => s.ShowBrowserButton = !s.ShowBrowserButton));
        AddRow("Explorer (files) button", OnOff(s.ShowExplorerButton), "", click: () => Toggle(() => s.ShowExplorerButton = !s.ShowExplorerButton));
        AddRow("News button", OnOff(s.ShowNewsButton), "\uE789", click: () => Toggle(() => s.ShowNewsButton = !s.ShowNewsButton),
            subtitle: "Game news, patches, announcements and sales");
        if (Handheld.IsHandheld)
            AddRow("My Device button", OnOff(s.ShowMyDeviceButton), Ui.HandheldGlyph, click: () =>
            {
                s.ShowMyDeviceButton = !s.ShowMyDeviceButton;
                s.Save();
                Host.RefreshHomeButtonsPublic();
                Rebuild();
            }, subtitle: "Your " + Handheld.DisplayName + "'s temperatures, fans, battery and more");
        AddNote("Home, Favorites, Apps, Captures, Stores, Settings and Power are always shown. Hidden pages can still be opened from Settings.");

        AddHeader("Animations");
        AddRow("Home buttons", s.ButtonAnimations ? ButtonStyles.FirstOrDefault(b => b.Id == s.ButtonAnimationStyle).Name ?? "Grow and glow" : "Off",
            "\uE916", click: async () =>
            {
                var options = ButtonStyles.Append(("off", "Off")).ToArray();
                int choice = await Host.AskAsync("Home button animation", "How the round buttons and top-bar icons react when highlighted. " +
                                                 "All of them spring when pressed.", options.Select(o => o.Item2).ToArray());
                if (choice < 0) return;
                Toggle(() =>
                {
                    s.ButtonAnimations = options[choice].Item1 != "off";
                    if (s.ButtonAnimations) s.ButtonAnimationStyle = options[choice].Item1;
                });
            }, subtitle: "Grow, lift, bounce, pulse or wiggle when highlighted");
        AddRow("Opening pages", PageStyles.FirstOrDefault(p => p.Id == s.PageTransition).Name ?? "Slide up", "\uE8A7",
            click: () => _ = ChooseAsync(PageStyles, "Opening pages", "How pages and pop-ups appear.", v => s.PageTransition = v));
        AddRow("Selected game", TileStyles.FirstOrDefault(t => t.Id == s.TileHighlight).Name ?? "Pulsing ring", "\uE7FC",
            click: () => _ = ChooseAsync(TileStyles, "Selected game", "How the highlighted game or app on the home row stands out. Move along the row to see it.", v => s.TileHighlight = v),
            subtitle: "Ten highlight animations: pulse, grow, bounce, lift, wobble, breathe, tilt, glow, shake…");
        AddRow("Punch-in when opening", OnOff(s.PunchIn), "\uE8E1", click: () => Toggle(() => s.PunchIn = !s.PunchIn),
            subtitle: "A game or app is pressed in and punches forward as you open it");
        AddNote("Startup and closing animations are further down. Reduce motion (Accessibility) turns all of these off.");

        AddHeader("Screen saver");
        AddRow("Screen saver", MainWindow.Screensavers.FirstOrDefault(x => x.Id == s.Screensaver).Name ?? "Off", "", click: () =>
        {
            int index = Array.FindIndex(MainWindow.Screensavers, x => x.Id == s.Screensaver);
            Toggle(() => s.Screensaver = MainWindow.Screensavers[(index + 1) % MainWindow.Screensavers.Length].Id);
        });
        AddRow("Start after", s.ScreensaverMinutes == 1 ? "1 minute" : s.ScreensaverMinutes + " minutes", "", click: () =>
        {
            int[] minutes = { 1, 2, 5, 10, 15, 30 };
            Toggle(() => s.ScreensaverMinutes = minutes[(Array.IndexOf(minutes, s.ScreensaverMinutes) + 1) % minutes.Length]);
        });
        if (s.Screensaver != "off")
            AddRow("Preview it now", glyph: "", click: () =>
            {
                // Shown over the home screen: go back there first. Any button ends it.
                Host.Pop(Host.RootScreen ?? this);
                Host.StartScreensaver();
            });
        AddNote("It appears when the home screen has been left alone, never over a game. The slideshow shows wide artwork and " +
                "the logo of games in your library.");

        AddHeader("Starting and closing");
        AddRow("Startup chime and animation", OnOff(s.StartupIntro), "", click: () =>
            Toggle(() => s.StartupIntro = !s.StartupIntro));
        AddRow("Startup animation", MainWindow.BootAnimations.FirstOrDefault(b => b.Id == s.BootAnimation).Name ?? "Logo pop", "",
            click: ChooseBootAnimation);
        AddRow("Welcome message", OnOff(s.WelcomeMessage), "\uE8BD",
            click: () => Toggle(() => s.WelcomeMessage = !s.WelcomeMessage),
            subtitle: $"\"{MainWindow.Greeting(DateTime.Now)}, {MainWindow.FirstName()}\" slides out next to your profile picture at startup");
        if (s.WelcomeMessage) AddRow("Show the welcome message now", glyph: "\uE768", click: () => { if (Host.RootScreen is { } root) Host.Pop(root); Host.ShowWelcome(); },
            subtitle: "Goes back to the home screen and plays it");
        AddRow("Boot video", s.BootVideo.Length > 0 ? Path.GetFileName(s.BootVideo) : "Built-in animation", "", click: ChooseBootVideo);
        AddRow("Closing animation on Exit SquareDeck", OnOff(s.ExitAnimation), "", click: () =>
            Toggle(() => s.ExitAnimation = !s.ExitAnimation));
        AddRow("Closing animation style", MainWindow.ExitStyles.FirstOrDefault(x => x.Id == s.ExitStyle).Name ?? "Logo goodbye", "",
            click: ChooseExitStyle);

        AddHeader("Covers and companion");
        AddFeature("coverdim");
        AddFeature("covereffect");
        AddFeature("tilt");
        if (Labs.On("tilt")) BuildTiltOptions();
        AddFeature("dust");
        AddFeature("companion");

        AddHeader("Moving background");
        AddFeature("wallpaperengine");
    }

    private void BuildSound(Settings s)
    {
        AddHeader("Interface sounds");
        AddRow("Interface sounds", !s.UiSounds ? "Off" : UiSound.SetName(s.SoundSet) + ", " + s.SoundVolume + "%",
            "", click: () => Host.Push(new SoundsScreen()));
        AddNote("Choose a built-in set or one of the sound packs, set the loudness, and pick which kinds of sound play.");
        AddRow("Game start chime", UiSound.LaunchChimes.FirstOrDefault(c => c.Id == s.LaunchChime).Name ?? "Rise", "",
            click: ChooseLaunchChime);

        AddHeader("Background music");
        AddRow("Music on the launcher's screens", BackgroundMusic.NameOf(s.MenuMusic), "\uE8D6", click: () => _ = ChooseBackgroundMusicAsync(Host, Rebuild));
        if (Labs.On("recomp"))
            AddRow("Recomp Zone music", OnOff(s.RecompZoneMusic), "\uE8D6", click: () => Toggle(() => s.RecompZoneMusic = !s.RecompZoneMusic),
                subtitle: "Recomp Zone plays its own music while it's open, at the same loudness as the launcher's music");
        AddRow("Retro Lounge music", OnOff(s.RetroLoungeMusic), "\uE8D6", click: () => Toggle(() => s.RetroLoungeMusic = !s.RetroLoungeMusic),
            subtitle: "Retro Lounge plays its own music while it's open, at the same loudness as the launcher's music");
        if (s.MenuMusic.Length > 0 || s.RecompZoneMusic || s.RetroLoungeMusic)
            AddRow("Music loudness", s.MenuMusicVolume + "%", "\uE995", click: () =>
            {
                int[] levels = { 10, 20, 30, 50, 75, 100 };
                Toggle(() => s.MenuMusicVolume = levels[(Array.IndexOf(levels, s.MenuMusicVolume) + 1) % levels.Length]);
            });
        if (s.MenuMusic.Length > 0)
            AddRow("Pause it while your music plays", OnOff(s.MenuMusicPausesForMedia), "\uE8D6", click: () =>
                Toggle(() => s.MenuMusicPausesForMedia = !s.MenuMusicPausesForMedia),
                subtitle: "Music from the Multimedia page, Spotify or any other player takes over, and it comes back when that stops");
        AddNote("Plays quietly only while SquareDeck is in front and no game is running, and pauses for videos, streaming and the browser.");
        AddRow("Add your own music (MP3 files)", glyph: "\uE8B7", click: () =>
        {
            try { Directory.CreateDirectory(BackgroundMusic.UserDir); } catch (Exception) { }
            ProcessUtil.Open(BackgroundMusic.UserDir);
        });

        AddHeader("Multimedia page");
        AddRow("Streaming opening animation", StreamingIntro.Styles.FirstOrDefault(c => c.Id == s.StreamingIntro).Name ?? "Surprise me", "\uE714",
            click: () => Pick("Streaming opening animation", StreamingIntro.Styles, id => s.StreamingIntro = id),
            subtitle: "Plays while Netflix, Hulu and the other streaming services open. YouTube Music and Apple Music have their own music ones (None turns those off too)");
        AddRow("Search Spotify and YouTube Music", OnOff(s.MusicServices), "\uE721", click: () => Toggle(() => s.MusicServices = !s.MusicServices));
        if (s.MusicServices)
        {
            var spotify = new SpotifyClient(s);
            AddRow("Spotify Client ID", s.SpotifyClientId.Length > 0 ? Mask(s.SpotifyClientId) : "SquareDeck's (built in)", "\uE8D7", click: () => _ = EditSpotifyIdAsync(),
                subtitle: "Only needed to use a Spotify app of your own; leave it empty to use SquareDeck's");
            if (spotify.HasClientId)
                AddRow(spotify.IsSignedIn ? "Signed in to Spotify (sign out)" : "Sign in to Spotify", glyph: "\uE77B", click: () => _ = SpotifySignInAsync(spotify));
            if (spotify.HasClientId)
            {
                AddRow("Play Spotify inside SquareDeck", OnOff(s.SpotifyInLauncher), "\uE7FC", click: () =>
                {
                    s.SpotifyInLauncher = !s.SpotifyInLauncher;
                    if (!s.SpotifyInLauncher) SpotifyPlayer.Stop();
                    Toggle(() => { });
                }, subtitle: spotify.NeedsNewSignIn
                    ? "Sign in to Spotify again first: it needs one more permission"
                    : "Music plays from the launcher itself, no Spotify app needed (Spotify Premium)");
                if (SpotifyPlayer.IsRunning)
                    AddRow("Stop the built-in Spotify player", glyph: "\uE71A", click: () => { SpotifyPlayer.Stop(); Rebuild(); },
                        subtitle: "SquareDeck stops being a Spotify device until you play something here again");
            }
            AddNote("YouTube Music opens inside the launcher; sign in there once to see your playlists. Spotify works with SquareDeck's " +
                    "built-in app: just sign in. To use an app of your own instead, create one at developer.spotify.com, add the redirect URI " +
                    SpotifyClient.RedirectUri + ", and enter its Client ID above. Playing from the launcher needs Spotify Premium; " +
                    "without it, results open in the Spotify app.");
        }

        AddHeader("Sound output");
        AddRow("Volume, brightness and radios", glyph: "", click: () => Host.Push(new QuickSettingsScreen()));
        AddRow("Sound output device", glyph: "", click: () => Host.Push(new PcSettingsScreen()));

        AddHeader("Music that follows the game");
        AddFeature("genresound");
    }

    private void BuildController(Settings s)
    {
        AddHeader("Buttons");
        AddRow("Button names shown on screen", Hints.Styles.FirstOrDefault(x => x.Id == s.GlyphStyle).Name ?? "Xbox", "", click: () =>
        {
            int index = Array.FindIndex(Hints.Styles, x => x.Id == s.GlyphStyle);
            Toggle(() => s.GlyphStyle = Hints.Styles[(index + 1) % Hints.Styles.Length].Id);
        });
        AddNote("Match the controller picks PlayStation or Switch names when one of those controllers is connected, Xbox otherwise. Xbox shows A B X Y, PlayStation shows ✕ ○ □ △, Switch shows B A Y X for the same button positions. " +
                "When you use a keyboard or mouse the hints switch to keys (Enter, Esc, X, Y) by themselves.");
        AddRow("How buttons are drawn", Hints.Looks.FirstOrDefault(x => x.Id == s.GlyphLook).Name ?? "Simple", "\uE7FC", click: () =>
        {
            int index = Array.FindIndex(Hints.Looks, x => x.Id == s.GlyphLook);
            Toggle(() => s.GlyphLook = Hints.Looks[(index + 1) % Hints.Looks.Length].Id);
        });
        AddNote("Simple draws the button's name on a pill. Controller icons and Pixel art show pictures of the buttons, " +
                "for the controller chosen above, and of the keys when you use a keyboard.");
        int pagesOn = MainWindow.CyclePages.Count(p => !s.PagesOff.Contains(p.Id));
        AddRow("Pages LB / RB switch between", $"{pagesOn} of {MainWindow.CyclePages.Length}", "",
            click: () => Host.Push(new PageOrderScreen()), subtitle: "Choose which pages, and their order");
        AddRow("Which button does what", string.Join(" ", Hints.Map.Select(Hints.ButtonName)), "",
            click: () => Host.Push(new ControlsScreen()));

        AddHeader("Vibration and testing");
        AddRow("Vibration strength", s.RumbleStrength <= 0 ? "Off" : s.RumbleStrength + "%", "", click: RumbleStrength);
        AddRow("Vibrate when a notification pops up", OnOff(s.RumbleOnNotifications), "\uEA8F",
            click: () => Toggle(() => s.RumbleOnNotifications = !s.RumbleOnNotifications));
        AddRow("Vibrate while moving through menus", OnOff(s.RumbleInSettings), "\uE7FC",
            click: () => Toggle(() => s.RumbleInSettings = !s.RumbleInSettings), subtitle: "A light tick from row to row in Settings, quick settings and the power menu");
        AddRow("Controller test", glyph: "", click: () => Host.Push(new ControllerTestScreen()));
        AddFeaturePage("sticks", "Stick check and dead zone");

        AddHeader("Typing");
        AddRow("Keyboard for typing", s.TouchKeyboard == "windows" ? "Windows touch keyboard" : "SquareDeck keyboard", "\uE765",
            click: () => Toggle(() => { s.TouchKeyboard = s.TouchKeyboard == "windows" ? "squaredeck" : "windows"; s.KeyboardChosen = true; }),
            subtitle: s.TouchKeyboard == "windows" ? "Best on a touchscreen; Windows' keyboard opens when you type"
                                                   : "Built in, works with the controller");

        AddHeader("Summon");
        AddRow("Summon buttons bring the launcher forward", OnOff(s.GuideSummon), "\uE7FC", click: () =>
            Toggle(() => s.GuideSummon = !s.GuideSummon));
        AddRow("Over a game, summon opens the Game Bar widget", OnOff(s.InGameQuickMenu), "\uE7FC", click: () =>
            Toggle(() => s.InGameQuickMenu = !s.InGameQuickMenu), subtitle: "SquareDeck's widget replaced the in-game quick menu");
        AddRow("Controller buttons", SummonLabel(s.SummonCombo), "\uE7FC", click: ChooseSummonButtons);
        AddRow("Keyboard shortcut", MainWindow.SummonKeys.FirstOrDefault(k => k.Id == s.SummonKey).Name ?? "None", "\uE765",
            click: ChooseSummonKey);

        AddHeader("Xbox Game Bar widget");
        AddRow("Widget connection", OnOff(s.GameBarWidget), "\uE7FC", click: () => Toggle(() =>
            {
                s.GameBarWidget = !s.GameBarWidget;
                if (s.GameBarWidget) WidgetBridge.Start(); else WidgetBridge.Stop();
            }),
            subtitle: (s.GameBarWidget && WidgetBridge.Connected > 0 ? "Connected now. " : "") +
                      "Lets the SquareDeck widget in Game Bar (Win + G) change the TDP, sound, brightness and quick toggles while you play");
        {
            var installedWidget = WidgetInstaller.InstalledVersion();
            var bundled = WidgetInstaller.Find();
            string value = WidgetInstaller.Busy ? "Installing…"
                : bundled != null && (installedWidget == null || installedWidget < bundled.Version) ? (installedWidget == null ? "Install" : "Update to " + bundled.Version)
                : installedWidget != null ? "Installed (" + installedWidget + ")" : "Not included";
            AddRow("Install the widget", value, "\uE896", click: () => _ = InstallWidgetAsync(),
                subtitle: bundled != null ? "From the Widget folder where SquareDeck is installed. The first time, Windows asks an administrator to trust its certificate"
                    : "Put the widget package (from Visual Studio's Create App Packages) in a Widget folder next to SquareDeck");
        }
        AddRow("Widget colors", WidgetThemeName(s.WidgetTheme), "\uE790", click: () => _ = ChooseWidgetThemeAsync(),
            subtitle: "SquareDeck's teal, your UI style's highlight color, or the colors of the game you're playing");
        AddRow("Widget background", WidgetBackgrounds.FirstOrDefault(b => b.Id == s.WidgetBackground).Name ?? "SquareDeck", "\uE771", click: () => _ = ChooseWidgetBackgroundAsync(),
            subtitle: "Behind the widget's cards: SquareDeck's dark, frosted glass, true black, your highlight color, two gradients or the game's art");
        AddRow("Widget quick settings", s.WidgetQuick == null ? "All" : $"{s.WidgetQuick.Count} of {Settings.WidgetQuickTiles.Length}", "\uE762", click: () => _ = ChooseWidgetQuickAsync(),
            subtitle: "Which tiles the widget's Quick card shows. The Edit button on the card changes them too");
        AddRow("Widget cards", WidgetCardsLabel(s), "\uE8A9", click: () => _ = ChooseWidgetCardsAsync(),
            subtitle: "Which cards the widget shows below Power: the game, live stats, the ROG Ally and quick toggles");
        AddRow("Close game in the widget", OnOff(s.WidgetCloseGame), "\uE711", click: () => Toggle(() => s.WidgetCloseGame = !s.WidgetCloseGame),
            subtitle: "A Close game button on the widget's game card (press twice)");
        AddRow("Open the widget now", null, "\uE8A7", click: () => Host.RunWidgetAction(),
            subtitle: "Opens Game Bar with SquareDeck's widget");

        if (Handheld.IsHandheld)
        {
            AddHeader("Handheld buttons");
            AddRow(Handheld.DisplayName + " Specific Settings", null, Ui.HandheldGlyph, click: () => Show("handheld", focusContent: true),
                subtitle: "The device's own buttons, lights, power and controls");
        }
    }

    /// <summary>Power and performance: TDP control for AMD handhelds, and the launcher's own Performance mode.</summary>
    private void BuildApps(Settings s)
    {
        AddHeader("Apps on this device");
        AddRow("Installed apps", glyph: "\uE71D", click: () => Host.Push(new InstalledAppsScreen()),
            subtitle: "See every app, and uninstall, modify, repair or reset them");
        AddRow("Apps that start with Windows", glyph: "\uE7E8", click: () => ProcessUtil.Open("ms-settings:startupapps"),
            subtitle: "Opens Windows Settings");
        AddRow("Default apps", glyph: "\uE7AC", click: () => ProcessUtil.Open("ms-settings:defaultapps"),
            subtitle: "Which app opens each kind of file and link (opens Windows Settings)");
        AddRow("Optional features", glyph: "\uE8F1", click: () => ProcessUtil.Open("ms-settings:optionalfeatures"),
            subtitle: "Opens Windows Settings");
    }

    private void BuildPower(Settings s)
    {
        var chip = TdpControl.Chip;
        // What's inside and how busy it is, live.
        AddHeader("This device");
        Rows.Children.Add(new HardwarePanel());

        AddHeader("Performance Boost");
        AddRow("Performance Boost", OnOff(s.PerformanceBoost), "\uE945",
            click: () => Toggle(() => s.PerformanceBoost = !s.PerformanceBoost),
            subtitle: "Frees memory used by other programs just before a game starts, with an animation");
        string animation = s.BoostAnimation == "random" ? "Surprise me" : BoostScreen.Styles.FirstOrDefault(x => x.Id == s.BoostAnimation).Name ?? "Pulse rings";
        AddRow("Animation", animation, "\uE768", click: ChooseBoostAnimation);
        AddRow("Vibration with the animation", OnOff(s.BoostRumble), "\uE7FC",
            click: () => Toggle(() => s.BoostRumble = !s.BoostRumble),
            subtitle: s.RumbleStrength > 0 ? "Each animation has its own pattern, timed to it" : "Turn on vibration in Controller settings to feel it");
        AddRow("Try it", glyph: "\uE768", click: () => Host.Push(new BoostScreen(null, s.BoostAnimation, s.BoostRumble, preview: true)),
            subtitle: "Plays the animation without freeing anything");
        // SquareDeck's own lighter mode (it was in Power and performance, now gone).
        AddRow("SquareDeck performance mode", Perf.Label(s.PerformanceMode), "\uEC4A", click: () =>
        {
            // Off -> on battery only -> always -> off
            s.PerformanceMode = s.PerformanceMode switch { "off" => "battery", "battery" => "on", _ => "off" };
            s.Save();
            Host.ApplyPerformance();
            Rebuild();
        }, subtitle: Perf.Active ? "On now: lighter effects to save battery and graphics power" : "Uses less graphics power and battery");
        AddNote("Turns off drop shadows, glows and live blurs (artwork is softened a cheaper way), the pulsing highlight and " +
                "animated covers, and draws animations at 30 frames a second (from the next start). While a game runs and " +
                "SquareDeck is in the background, it always stops drawing and gives the game its CPU and graphics time.");
        AddHeader("Performance overlay");
        AddRow("Performance overlay", OnOff(s.OsdEnabled), "\uE9D9",
            click: () => Toggle(() => s.OsdEnabled = !s.OsdEnabled),
            subtitle: "FPS, CPU and GPU use, CPU speed, memory, TDP and battery, floating over your games");
        if (s.OsdEnabled)
        {
            string Name<T>(IEnumerable<T> list, Func<T, string> id, Func<T, string> name, string current) => list.Where(x => id(x) == current).Select(name).FirstOrDefault() ?? name(list.First());
            AddRow("Show it", s.OsdWhen == "always" ? "All the time" : "In full-screen games", "\uE7FC",
                click: () => Toggle(() => s.OsdWhen = s.OsdWhen == "always" ? "games" : "always"),
                subtitle: "In games means whenever a program other than SquareDeck fills the screen");
            AddRow("Details", Name(PerfOverlay.Details, d => d.Id, d => d.Name, s.OsdDetail), "\uE8FD",
                click: () => Pick("Overlay details", PerfOverlay.Details.Select(d => (d.Id, d.Name, d.About)).ToArray(), id => s.OsdDetail = id),
                subtitle: PerfOverlay.Details.FirstOrDefault(d => d.Id == s.OsdDetail).About ?? "");
            AddRow("Layout", Name(PerfOverlay.Layouts, d => d.Id, d => d.Name, s.OsdLayout), "\uE8A9",
                click: () => Pick("Overlay layout", PerfOverlay.Layouts.Select(d => (d.Id, d.Name, d.About)).ToArray(), id => s.OsdLayout = id));
            AddRow("Style", Name(PerfOverlay.Looks, d => d.Id, d => d.Name, s.OsdStyle), "\uE790",
                click: () => Pick("Overlay style", PerfOverlay.Looks.Select(d => (d.Id, d.Name, "")).ToArray(), id => s.OsdStyle = id));
            AddRow("Position", Name(PerfOverlay.Positions, d => d.Id, d => d.Name, s.OsdPosition), "\uE8A0",
                click: () => Pick("Overlay position", PerfOverlay.Positions.Select(d => (d.Id, d.Name, "")).ToArray(), id => s.OsdPosition = id));
            AddRow("Size", Name(PerfOverlay.Sizes, d => d.Id, d => d.Name, s.OsdSize), "\uE8E8",
                click: () => Toggle(() => s.OsdSize = s.OsdSize switch { "small" => "medium", "medium" => "large", _ => "small" }));
            AddRow("Opacity", $"{Math.Clamp(s.OsdOpacity, 30, 100)}%", "\uE706",
                click: () => Toggle(() => s.OsdOpacity = s.OsdOpacity >= 100 ? 40 : Math.Min(100, s.OsdOpacity + 10)));
            AddNote(FrameCounter.Available
                ? "FPS comes from PresentMon in SquareDeck's Tools folder. It needs SquareDeck to run as administrator, or your account in Windows' \"Performance Log Users\" group."
                : "To show FPS, put Intel's PresentMon (free, github.com/GameTechDev/PresentMon) in SquareDeck's Tools\\PresentMon folder. Everything else works without it.");
            AddNote("The overlay shows over borderless and windowed games. Games in exclusive full-screen mode draw over everything, so switch them to borderless to see it.");
        }
        // TDP and processor tuning: on a handheld they live in its own section of Settings, so there is one place for them.
        if (!Handheld.IsHandheld) BuildTdp(s);


    }

    /// <summary>
    /// Processor power: the TDP limit (and its helper) and the Ryzen tuning. In the handheld's own section on a handheld,
    /// in Power and performance on any other PC; never in both.
    /// </summary>
    private void BuildTdp(Settings s, bool handheld = false, Action? first = null)
    {
        var chip = TdpControl.Chip;
        // In the handheld's own section the groups read as Performance, Processor tuning and Advanced; the device's own
        // power mode (from <paramref name="first"/>) leads the Performance group.
        AddHeader(handheld ? "Performance" : "Processor power (TDP)");
        first?.Invoke();
        if (chip == null)
        {
            AddNote("TDP control works with AMD and Intel processors. This PC has " +
                    (TdpControl.CpuName.Length > 0 ? TdpControl.CpuName : "a processor SquareDeck couldn't identify") + ".");
        }
        else if (!TdpControl.CanApply)
        {
            AddNote(chip.Name + ". " + chip.Note);
            bool vendorFirmware = Handheld.Vendor is HandheldVendor.Lenovo or HandheldVendor.Msi;
            if (vendorFirmware || TdpControl.IsIntel)
            {
                AddRow("Set up TDP control", "Not set up", "\uE896", click: () => _ = SetUpDeviceHelperAsync(),
                    subtitle: vendorFirmware
                        ? "Sets the limit through the " + Handheld.DisplayName + "'s own firmware (one Windows permission prompt)"
                        : "Sets Intel's power limits with KX.exe from your HandheldCompanion install (one Windows permission prompt)");
                AddNote("Setting the power limit needs administrator rights. Setup adds the device helper: a small script in Program Files " +
                        "and a background task, \"SquareDeck\\DeviceHelper\", that changes only the settings on its list, without asking each time." +
                        (TdpControl.IsIntel && !vendorFirmware
                            ? " Intel power limits need KX.exe, which comes with HandheldCompanion (github.com/Valkirie/HandheldCompanion): install HandheldCompanion first, or point SquareDeck to its KX folder."
                            : ""));
            }
            if (!TdpControl.IsIntel)
            {
                AddRow(vendorFirmware ? "Set up with RyzenAdj instead" : "Set up TDP control", "Not set up", "\uE896", click: SetUpTdp,
                    subtitle: TdpControl.HasBundled ? "Installs RyzenAdj, which comes with SquareDeck (one Windows permission prompt)"
                                                    : "Downloads RyzenAdj from GitHub and installs it (one Windows permission prompt)");
                if (!vendorFirmware)
                    AddNote("Setting the power limit needs administrator rights. Setup installs RyzenAdj (an open-source tool, " +
                            "github.com/FlyGoat/RyzenAdj) into Program Files and adds a background task, \"SquareDeck\\ApplyTdp\", " +
                            "that applies your limits without asking each time. You can remove both here later.");
            }
        }
        else
        {
            AddRow("TDP control", OnOff(s.TdpEnabled), "\uEC4A", click: () =>
            {
                s.TdpEnabled = !s.TdpEnabled;
                SaveTdp();
            }, subtitle: s.TdpEnabled ? (TdpControl.LastResult.Length > 0 ? TdpControl.LastResult : chip.Name) : chip.Name);
            AddTdpMethodRow(s);
            if (s.TdpEnabled)
            {
                AddRow("TDP limit", TdpControl.Clamp(s.TdpWatts) + " W", "\uE945", click: () => _ = PickWattsAsync("TDP limit",
                    "The processor's power limit. Lower lasts longer on battery and runs cooler; higher runs games faster.",
                    s.TdpWatts, w => { s.TdpWatts = w; SaveTdp(); }));
                AddRow("Quick presets", null, "\uE9E9", click: () => _ = TdpPresetAsync(s));
                AddRow("Different limit when plugged in", s.TdpPluggedWatts > 0 ? TdpControl.Clamp(s.TdpPluggedWatts) + " W" : "Same", "\uEBB5", click: async () =>
                {
                    int choice = await Host.AskAsync("When plugged in", "Use a different limit while the charger is connected?", "Same as on battery", "Choose a limit…");
                    if (choice == 0) { s.TdpPluggedWatts = 0; SaveTdp(); }
                    else if (choice == 1)
                        await PickWattsAsync("TDP when plugged in", "Used while the charger is connected.",
                            s.TdpPluggedWatts > 0 ? s.TdpPluggedWatts : chip.Max, w => { s.TdpPluggedWatts = w; SaveTdp(); });
                });
                AddRow("Temperature limit", s.TdpTempLimit > 0 ? s.TdpTempLimit + " °C" : "Device default", "\uE9CA", click: async () =>
                {
                    int[] temps = { 0, 80, 85, 90, 95 };
                    int choice = await Host.AskAsync("Temperature limit", "The processor slows down before it gets hotter than this. Lower is cooler and quieter.",
                        temps.Select(t => t == 0 ? "Device default" : t + " °C").ToArray());
                    if (choice < 0) return;
                    s.TdpTempLimit = temps[choice];
                    SaveTdp();
                });
                AddRow("Boost window", s.TdpBoostWindow == "short" ? "Short bursts" : "Off", "\uE9F5", click: () =>
                {
                    s.TdpBoostWindow = s.TdpBoostWindow == "short" ? "off" : "short";
                    SaveTdp();
                }, subtitle: s.TdpBoostWindow == "short"
                    ? $"Up to {Math.Min(chip.Max, (int)Math.Round(TdpControl.Clamp(s.TdpWatts) * 1.25))} W for a few seconds, then back to the limit"
                    : "The limit holds all the time. Short bursts help loading screens and busy moments");
                AddRow("Power profile", s.RyzenProfile switch { "saving" => "Power saving", "performance" => "Maximum performance", _ => "Device default" }, "\uE83F",
                    click: async () =>
                    {
                        string[] ids = { "", "saving", "performance" };
                        int choice = await Host.AskAsync("Power profile",
                            "AMD's own tuning for Ryzen Z1 and Z2 chips, on top of the limit. Power saving keeps clocks lower when there's little to do; " +
                            "maximum performance holds them high (more heat and fan).",
                            "Device default", "Power saving", "Maximum performance");
                        if (choice < 0) return;
                        s.RyzenProfile = ids[choice];
                        SaveTdp();
                    }, subtitle: TdpControl.HelperNeedsUpdate ? "Needs the TDP helper updated (below)" : "AMD's power saving or maximum performance tuning");
            }
        }

        AddHeader(handheld ? "Processor tuning" : "Processor (Ryzen Z1 / Z2)");
        AddRow("CPU boost", OnOff(s.CpuBoost), "\uE9F5", click: () => { s.CpuBoost = !s.CpuBoost; SaveTdp(); },
            subtitle: "Short bursts above the base speed. Off saves battery and heat; most games barely notice");
        AddRow("Maximum CPU speed", s.MaxCpu >= 100 ? "No limit" : s.MaxCpu + "%", "\uEC48", click: async () =>
        {
            int[] caps = { 100, 90, 80, 70, 60, 50 };
            int choice = await Host.AskAsync("Maximum CPU speed", "Caps how fast the processor may run. Leaves more of the power limit for graphics.",
                caps.Select(c => c >= 100 ? "No limit" : c + "%").ToArray());
            if (choice < 0) return;
            s.MaxCpu = caps[choice];
            SaveTdp();
        });
        AddRow("Energy preference", s.CpuEpp < 0 ? "Windows default" : s.CpuEpp switch { <= 10 => "Performance", <= 40 => "Balanced performance", <= 70 => "Balanced saving", _ => "Power saving" }, "\uEBB4",
            click: async () =>
            {
                int[] values = { -1, 0, 33, 60, 85 };
                int choice = await Host.AskAsync("Energy preference",
                    "How eagerly the processor speeds up (EPP). Saving waits a little longer before clocking up, which helps battery in lighter games.",
                    "Windows default", "Performance", "Balanced performance", "Balanced saving", "Power saving");
                if (choice < 0) return;
                s.CpuEpp = values[choice];
                SaveTdp();
            });
        AddRow("CPU cores in use", s.CpuCores >= 100 ? "All" : s.CpuCores + "%", "\uE964", click: async () =>
        {
            int[] shares = { 100, 75, 50, 25 };
            int choice = await Host.AskAsync("CPU cores in use",
                "Windows parks the rest. On a Z1 Extreme or Z2 Extreme (8 cores), half is plenty for older and indie games and saves battery. " +
                "Demanding games want all of them.",
                shares.Select(c => c >= 100 ? "All cores" : c + "% of the cores").ToArray());
            if (choice < 0) return;
            s.CpuCores = shares[choice];
            SaveTdp();
        });
        AddRow("Windows power mode", TdpControl.PowerModeName(s.WindowsPowerMode), "\uE83F", click: async () =>
        {
            var modes = TdpControl.WindowsPowerModes;
            int choice = await Host.AskAsync("Windows power mode", "The same as Settings > System > Power in Windows (used with the Balanced power plan).",
                modes.Select(m => m.Name).ToArray());
            if (choice < 0) return;
            s.WindowsPowerMode = modes[choice].Id;
            SaveTdp();
        });
        AddNote("Games can have their own TDP, CPU boost, speed and power mode: open a game's options (Y) > Game profile. " +
                "The TDP can also be changed while playing from the Guide menu or Quick settings, until the game closes.");

        if (!handheld) BuildTdpHelper(s, "TDP helper");
    }

    /// <summary>The TDP helper (RyzenAdj): set it up too, keep re-applying, update or remove it. Under <paramref name="header"/>, if given.</summary>
    private void BuildTdpHelper(Settings s, string? header)
    {
        var chip = TdpControl.Chip;
        if (!TdpControl.Installed && TdpControl.Method is "asus" or "lenovo" or "msi" && !TdpControl.IsIntel && chip != null)
        {
            if (header != null) AddHeader(header);
            AddRow("Set up RyzenAdj too", "Optional", "\uE896", click: SetUpTdp,
                subtitle: "Not needed on this device. Adds the temperature limit and power profile, which the firmware can't set");
        }
        if (TdpControl.Installed)
        {
            if (header != null) AddHeader(header);
            AddRow("Keep re-applying", OnOff(s.TdpReapply), "\uE72C", click: () => { s.TdpReapply = !s.TdpReapply; SaveTdp(); },
                subtitle: "Every 15 seconds, for devices whose own software puts its limit back (Armoury Crate, Legion Space)");
            AddRow("Apply now", TdpControl.LastResult, "\uE768", click: async () => { await TdpControl.ApplyAsync(); Rebuild(); });
            if (TdpControl.HelperNeedsUpdate)
                AddRow("Update the TDP helper", "Needed for the power profile", "\uE895", click: SetUpTdp,
                    subtitle: "Reinstalls RyzenAdj and its task with this version's script (one Windows permission prompt)");
            AddRow("Remove TDP control", null, "\uE74D", click: async () =>
            {
                if (await Host.AskAsync("Remove TDP control?", "This removes RyzenAdj and the background task. The current limit stays until the device restarts or its own software changes it.", "Remove", "Cancel") != 0) return;
                var error = await TdpControl.UninstallAsync();
                if (error != null) await Host.AskAsync("Couldn't remove everything", error, "OK");
                s.TdpEnabled = false;
                s.Save();
                Rebuild();
            });
        }
    }

    /// <summary>"How the limit is set": a choice when there's more than one way on this PC.</summary>
    private void AddTdpMethodRow(Settings s)
    {
        var methods = TdpControl.AvailableMethods;
        if (methods.Count == 0) return;
        string now = TdpControl.Method;
        string about = now switch
        {
            "asus" => "Through the Ally's firmware, like Armoury Crate (no helper needed)",
            "lenovo" => "Through Lenovo's firmware, like Legion Space (device helper)",
            "msi" => "Through MSI's firmware, like MSI Center M (device helper)",
            "kx" => "Intel's PL1 / PL2 limits with KX.exe (device helper)",
            _ => "Through the RyzenAdj helper"
        };
        AddRow("How the limit is set", TdpControl.MethodName(now), "\uE950", click: async () =>
        {
            if (methods.Count < 2)
            {
                await Host.AskAsync("How the limit is set", about + ". It's the only way set up on this device.", "OK");
                return;
            }
            int choice = await Host.AskAsync("How the limit is set", null, methods.Select(TdpControl.MethodName).ToArray());
            if (choice < 0) return;
            s.TdpMethod = methods[choice];
            SaveTdp();
        }, subtitle: about);
    }

    /// <summary>Sets up the device helper (Legion Go, MSI Claw, Intel TDP), finding KX.exe for Intel.</summary>
    private async Task SetUpDeviceHelperAsync()
    {
        bool vendor = Handheld.Vendor is HandheldVendor.Lenovo or HandheldVendor.Msi;
        string? kx = DeviceHelper.FindKx();
        if (TdpControl.IsIntel && kx == null)
        {
            int pick = await Host.AskAsync("KX.exe wasn't found",
                "Intel power limits are set with KX.exe, which comes with HandheldCompanion (github.com/Valkirie/HandheldCompanion). " +
                "SquareDeck looked in Program Files and didn't find it. " +
                (vendor ? "You can set up without it: the " + Handheld.DisplayName + "'s own firmware sets the limit." : "Install HandheldCompanion, or choose its KX folder."),
                vendor ? new[] { "Set up without it", "Choose the KX folder…", "Cancel" } : new[] { "Choose the KX folder…", "Cancel" });
            string chosen = vendor ? (pick switch { 0 => "skip", 1 => "browse", _ => "" }) : (pick == 0 ? "browse" : "");
            if (chosen == "") return;
            if (chosen == "browse")
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Find KX.exe", Filter = "KX.exe|KX.exe", CheckFileExists = true };
                if (dialog.ShowDialog() != true) return;
                kx = System.IO.Path.GetDirectoryName(dialog.FileName);
            }
        }
        if (await Host.AskAsync("Set up the device helper?",
                "Windows will ask for permission once. SquareDeck adds a small script to Program Files and a background task, " +
                "\"SquareDeck\\DeviceHelper\", that changes only the settings on its list (power modes, fan boost, charge limit, power limits). " +
                "Changing power limits is at your own risk; stay within your device's range.",
                "Set up", "Cancel") != 0) return;
        Host.ShowToast("DEVICE HELPER", "Setting up…", "", "", "info");
        bool hadTdp = TdpControl.CanApply;
        var error = await DeviceHelper.InstallAsync(kx);
        if (error != null)
        {
            await Host.AskAsync("The device helper isn't set up", error, "OK");
            Rebuild();
            return;
        }
        Host.ShowToast("DEVICE HELPER", "Set up", "", "", "info");
        if (!hadTdp && TdpControl.CanApply)
        {
            Host.Settings.TdpEnabled = true;
            Host.Settings.TdpWatts = TdpControl.Chip?.Default ?? 15;
        }
        _ = HandheldVendors.ApplyAllAsync(Host.Settings);
        SaveTdp();
    }

    // ---------------------------------------------------------------- <model> Specific Settings

    /// <summary>Where the TDP settings are: the handheld's own section on a handheld, Power and performance elsewhere.</summary>
    public static string TdpPlace => Handheld.IsHandheld ? Handheld.DisplayName : "Gaming";

    /// <summary>The section of Settings with the TDP settings.</summary>
    public static string TdpSection => Handheld.IsHandheld ? "handheld" : "gaming";

    // ---------------------------------------------------------------- the handheld's own section
    //
    // One place for everything about this device, as a hub of groups (each opens on its own): Performance, Processor
    // tuning, Fans and temperature, Lights, Controller, Buttons, Battery, Screen, This device and Advanced. On the ROG
    // Ally family it covers what HandheldCompanion offers for it: TDP and the ASUS modes, fan curves, the RGB rings,
    // the controller's firmware settings (rumble strength, stick and trigger dead zones, M1 / M2, mouse mode), the
    // charge limit, and the screen's refresh rate and resolution.

    private void BuildHandheld(Settings s)
    {
        bool ally = Handheld.Vendor == HandheldVendor.Asus;
        Rows.Children.Add(HandheldHero(s, ally));
        var health = Ui.Row("Device health", null, "\uE95E", subtitle: "What's in control of the lights, TDP and buttons, and one-press fixes");
        health.Click += (_, _) => Host.Push(new DeviceHealthScreen());
        health.Tag = "pinned";
        Rows.Children.Add(health);
        var batteryByGame = Ui.Row("Battery and performance by game", null, "\uE859", subtitle: "How long a full battery lasts in each game");
        batteryByGame.Click += (_, _) => Host.Push(new BatteryInsightScreen());
        batteryByGame.Tag = "pinned";
        Rows.Children.Add(batteryByGame);

        // ---- Performance: the device's own power mode first, then the TDP; Processor tuning; Advanced (the helper).
        BuildTdp(s, handheld: true, first: ally ? () =>
        {
            AddRow("Operating mode", AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name ?? "Leave as it is", "",
                click: () => AllyPick("Operating mode", AllyControl.AsusModes, m => m.Name, m => s.AllyAsusMode = m.Id, () => Task.Run(AllyControl.ApplyPower)),
                subtitle: "Silent, Performance or Turbo, as in Armoury Crate. The TDP limit below, when on, is applied on top");
        } : null);

        if (ally) BuildAllyHardware(s);
        // Legion Go / MSI Claw: through the device helper.
        if (HandheldVendors.Supported)
        {
            bool lenovo = Handheld.Vendor == HandheldVendor.Lenovo;
            AddHeader(lenovo ? "Legion modes and fans" : "MSI Claw modes and fans");
            if (!DeviceHelper.Installed)
            {
                AddRow("Set up the device helper", "Not set up", "\uE896", click: () => _ = SetUpDeviceHelperAsync(),
                    subtitle: (lenovo ? "Legion modes" : "User scenarios") + ", fan boost and the charge limit need it (one Windows permission prompt)");
            }
            else
            {
                var modes = HandheldVendors.Modes;
                AddRow(lenovo ? "Legion mode" : "User scenario", modes.FirstOrDefault(m => m.Id == s.HandheldPowerMode).Name ?? "Leave as it is", "\uEC4A",
                    click: async () =>
                    {
                        int choice = await Host.AskAsync(lenovo ? "Legion mode" : "User scenario", null, modes.Select(m => m.Name).ToArray());
                        if (choice < 0) return;
                        s.HandheldPowerMode = modes[choice].Id;
                        s.Save();
                        bool ok = await HandheldVendors.SetModeAsync(s.HandheldPowerMode);
                        Host.ShowToast(Handheld.DisplayName.ToUpperInvariant(), ok ? modes[choice].Name : "Couldn't change it", ok ? "" : DeviceHelper.LastResult, "", "info");
                        Rebuild();
                    }, subtitle: modes.FirstOrDefault(m => m.Id == s.HandheldPowerMode).About);
                AddRow("Fan boost", OnOff(s.HandheldFanBoost), "\uE9CA", click: async () =>
                {
                    s.HandheldFanBoost = !s.HandheldFanBoost;
                    s.Save();
                    if (!await HandheldVendors.SetFanBoostAsync(s.HandheldFanBoost))
                        Host.ShowToast(Handheld.DisplayName.ToUpperInvariant(), "Couldn't change the fans", DeviceHelper.LastResult, "", "info");
                    Rebuild();
                }, subtitle: "The fans at full speed: loud, but the coolest");
                if (lenovo)
                    AddRow("Stop charging at 80%", OnOff(s.HandheldChargeLimit is > 0 and < 100), "\uEBB5", click: async () =>
                    {
                        s.HandheldChargeLimit = s.HandheldChargeLimit is > 0 and < 100 ? 100 : 80;
                        s.Save();
                        if (!await HandheldVendors.SetChargeLimitAsync(s.HandheldChargeLimit))
                            Host.ShowToast("LEGION", "Couldn't change the charge limit", DeviceHelper.LastResult, "", "info");
                        Rebuild();
                    }, subtitle: "Kinder to the battery when the Legion Go is mostly plugged in");
                else
                    AddRow("Stop charging at", s.HandheldChargeLimit is > 0 ? s.HandheldChargeLimit + "%" : "Leave as it is", "\uEBB5", click: async () =>
                    {
                        var limits = HandheldVendors.ClawChargeLimits;
                        int choice = await Host.AskAsync("Stop charging at", "80% is kinder to the battery when the Claw is mostly plugged in.",
                            limits.Select(l => l == 0 ? "Leave as it is" : l + "%").ToArray());
                        if (choice < 0) return;
                        s.HandheldChargeLimit = limits[choice];
                        s.Save();
                        if (s.HandheldChargeLimit > 0 && !await HandheldVendors.SetChargeLimitAsync(s.HandheldChargeLimit))
                            Host.ShowToast("MSI CLAW", "Couldn't change the charge limit", DeviceHelper.LastResult, "", "info");
                        Rebuild();
                    });
            }
        }


        // ---- Screen (every handheld): refresh rate and resolution of the built-in display.
        BuildHandheldScreen();

        // ---- This device
        AddHeader("This device");
        AddRow("My Device", null, "", click: () => Host.Push(new MyDeviceScreen()),
            subtitle: "Temperatures, fan speed, battery health, power draw and more, live");
        AddRow("My Device on the home screen", OnOff(s.ShowMyDeviceButton), "", click: () =>
        {
            s.ShowMyDeviceButton = !s.ShowMyDeviceButton;
            s.Save();
            Host.RefreshHomeButtonsPublic();
            Rebuild();
        }, subtitle: "A button beside the others under the games row");
        AddRow("Battery", null, "", click: () => Host.Push(new BatteryScreen()), subtitle: "Battery saver, health, charging and usage");
        if (Handheld.Current is { } device) AddNote("SquareDeck can change: " + Handheld.ControlsText(device) + ".");

        // ---- Advanced: the helpers (the TDP helper's rows were added to it by BuildTdp)
        AddHeader("Advanced");
        BuildTdpHelper(s, null);
        if (HandheldVendors.Supported || TdpControl.IsIntel)
        {
            if (DeviceHelper.Installed)
            {
                AddRow("Device helper", "Set up", "\uE90F", click: async () =>
                {
                    int choice = await Host.AskAsync("Device helper", "Changes handheld settings that need administrator rights.", "Set it up again", "Remove it", "Cancel");
                    if (choice == 0) await SetUpDeviceHelperAsync();
                    else if (choice == 1)
                    {
                        var error = await DeviceHelper.UninstallAsync();
                        if (error != null) await Host.AskAsync("Device helper", error, "OK");
                        Rebuild();
                    }
                }, subtitle: TdpControl.IsIntel ? (DeviceHelper.HasKx ? "With KX.exe for Intel power limits" : "Without KX.exe: Intel power limits through the firmware only") : "Power modes, fans, charge limit, power limits");
            }
            else if (!HandheldVendors.Supported)
            {
                AddRow("Set up Intel TDP", "Not set up", "\uE896", click: () => _ = SetUpDeviceHelperAsync(),
                    subtitle: "Uses KX.exe from HandheldCompanion (one Windows permission prompt)");
            }
        }

    }

    /// <summary>The top of the handheld's section: its picture, name, processor and power range, and what it's set to now.</summary>
    private UIElement HandheldHero(Settings s, bool ally)
    {
        var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var panel = new Border
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 16, 22, 18), Margin = new Thickness(0, 8, 0, 14), Tag = "pinned",
            Background = new LinearGradientBrush(Color.FromArgb(0x55, a.R, a.G, a.B), Color.FromArgb(0x12, a.R, a.G, a.B), 20)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (MyDeviceScreen.DevicePhoto(260) is { } photo) grid.Children.Add(photo);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(Handheld.DisplayName, 28, "FgBrush", bold: true));
        var facts = new List<string>();
        if (Handheld.Brand.Length > 0) facts.Add(Handheld.Brand);
        if (TdpControl.CpuName.Length > 0) facts.Add(TdpControl.CpuName);
        if (Handheld.Current is { } device) facts.Add($"{device.TdpMin}–{device.TdpMax} W");
        var line = Ui.Text(string.Join("   ·   ", facts), 14, "SubtleBrush");
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 4, 0, 12);
        text.Children.Add(line);

        // What it's set to right now, as chips.
        var chips = new WrapPanel();
        void Chip(string glyph, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 13, "AccentBrush");
            g.Margin = new Thickness(0, 1, 7, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(label, 13, "FgBrush"));
            chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 8, 8), Child = row,
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))
            });
        }
        Chip("", TdpControl.Ready ? TdpControl.EffectiveWatts + " W TDP" : "TDP not set");
        if (ally)
        {
            Chip("", AllyControl.AsusModes.FirstOrDefault(m => m.Id == s.AllyAsusMode).Name is { } mode && s.AllyAsusMode.Length > 0 ? mode : "ASUS mode as it is");
            var fan = AllyControl.FanCurves.FirstOrDefault(f => f.Id == s.AllyFan);
            Chip("", s.AllyFan.Length > 0 ? fan.Name + " fans" : "Fans as they are");
            Chip("", "Lights: " + (AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights).Name ?? "as they are"));
            Chip("", s.AllyChargeLimit is > 0 and < 100 ? "Charges to " + s.AllyChargeLimit + "%" : "Charges to 100%");
        }
        if (DisplaySettings.Current() is { } screen) Chip("", $"{screen.RefreshRate} Hz");
        text.Children.Add(chips);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        panel.Child = grid;
        return panel;
    }

    /// <summary>Saves a setting, applies it, and redraws.</summary>
    private void AllySave(Action change, Action? apply = null)
    {
        change();
        Host.Settings.Save();
        apply?.Invoke();
        Rebuild();
    }

    private async void AllyPick<T>(string title, T[] options, Func<T, string> name, Action<T> store, Action? apply = null)
    {
        int choice = await Host.AskAsync(title, null, options.Select(name).ToArray());
        if (choice < 0 || choice >= options.Length) return;
        AllySave(() => store(options[choice]), apply);
    }

    /// <summary>The ROG Ally family's own hardware: fans, lights, the controller's firmware, buttons and battery care.</summary>
    private void BuildAllyHardware(Settings s)
    {
        bool acpi = AsusAcpi.Available;
        bool lights = AllyHid.HasLights;
        bool pad = AllyHid.HasController;

        // ---- Fans and temperature
        AddHeader("Fans and temperature");
        var fan = AllyControl.FanCurves.FirstOrDefault(f => f.Id == s.AllyFan);
        AddRow("Fan curve", fan.Name ?? "Leave as it is", "",
            click: () => AllyPick("Fan curve", AllyControl.FanChoices, f => f.Name + (f.About.Length > 0 ? "  ·  " + f.About : ""), f => s.AllyFan = f.Id, () => Task.Run(AllyControl.ApplyPower)),
            subtitle: string.IsNullOrEmpty(fan.About) ? "How fast the fans spin as it warms up" : fan.About);
        if (acpi)
        {
            var now = new List<string>();
            if (AsusAcpi.FanRpm(AsusAcpi.CpuFan) is int rpm) now.Add(rpm.ToString("N0") + " RPM");
            if (AsusAcpi.FanPercent() is int pct) now.Add(pct + "%");
            if (now.Count > 0) AddRow("Fans now", string.Join("  ·  ", now), "", click: Rebuild, subtitle: "Select to refresh. My Device shows them live, with the temperature");
        }
        else AddNote("SquareDeck couldn't reach ASUS's firmware driver (ATKACPI, installed with Armoury Crate SE or the MyASUS drivers), so the operating mode, fans and charge limit can't be changed.");

        // ---- Graphics memory (VRAM): shown here, changed in Armoury Crate SE or the UEFI setup
        AddHeader("Graphics memory (VRAM)");
        AddRow("Assigned to the graphics now", AllyVram.CurrentText(), "", click: Rebuild,
            subtitle: "Auto, 4, 6, 8, 12 or 16 GB depending on the model. More helps texture-heavy games and leaves less for Windows");
        AddRow("Change it in Armoury Crate SE", null, "", click: () => ProcessUtil.Open("asusac://"),
            subtitle: "Settings > Operating mode > GPU settings > Memory assigned to GPU. It takes effect after a restart");
        AddRow("Restart into the UEFI setup", null, "", click: async () =>
            {
                if (await Host.AskAsync("Restart into the UEFI setup?", "Save what you're doing first. In the setup, look under Advanced for \"UMA frame buffer size\" (Auto, or a size in GB), then save and exit.", "Cancel", "Restart") != 1) return;
                var result = await AllyVram.RestartToSetupAsync();
                if (result.Contains("declined")) Host.ShowToast("ROG ALLY", "Not restarted", result, "", "info");
            },
            subtitle: "The same setting, before Windows starts. Windows asks first");
        AddNote("SquareDeck doesn't write this one to the firmware itself: on the Ally X and Xbox Allys ASUS hasn't published the values, and a wrong one can stop the Ally from starting.");

        // ---- Lights
        AddHeader("Lights");
        if (!lights) AddNote("SquareDeck couldn't find the Ally's light controller. These settings apply once it's found.");
        if (AsusOptimization.Installed && !AsusOptimization.Enabled)
            AddRow("ASUS Optimization is off", "Switch on", "", click: async () =>
            {
                var error = await AsusOptimization.SetEnabledAsync(true);
                if (error != null) await Host.AskAsync("ASUS Optimization", error, "OK");
                else { AllyControl.ApplyLights(force: true); AllyControl.ApplyXboxLight(force: true); }
                Rebuild();
            }, subtitle: "While ASUS's service is off, the lights don't take changes. Windows asks for permission");
        if (s.AllyLights is not ("" or "off") && AllyControl.AwakeOff(s))
            AddNote("The lights are dark: \"Awake\" is off under When the lights are on (" + (Battery.IsPluggedIn() ? "plugged in" : "on battery") + "). Turn it on there, or choose the effect again.");
        if (AllyControl.DynamicLightingOn)
            AddRow("Windows' Dynamic Lighting is on", "Open", "", click: () => ProcessUtil.Open("ms-settings:personalization-lighting"),
                subtitle: "It takes over the Ally's lights, so SquareDeck's changes don't show. Turn off \"Use Dynamic Lighting on my devices\"");
        if (s.AllyLights.Length == 0) AddNote("The lights are left as they are. Choose an effect and SquareDeck takes them over.");
        var mode = AllyControl.LightModes.FirstOrDefault(m => m.Id == s.AllyLights);
        AddRow("Effect", mode.Name ?? "Leave as it is", "", click: () => _ = ChooseLightEffectAsync(),
            subtitle: string.IsNullOrEmpty(mode.About) ? "The rings around the sticks" : mode.About);
        if (s.AllyLights is "solid" or "breathing" or "sticks")
        {
            bool sticks = s.AllyLights == "sticks";
            AddRow(sticks ? "Left stick color" : "Color", AllyControlScreen.ColorName(s.AllyLightColor), "",
                click: () => Host.Push(new LedColorScreen(sticks ? "Left stick color" : "Light color", s.AllyLightColor,
                    hex => { s.AllyLightColor = hex; s.Save(); AllyControl.ApplyLights(force: true); })));
            if (s.AllyLights is "breathing" or "sticks")
                AddRow(sticks ? "Right stick color" : "Second color", AllyControlScreen.ColorName(s.AllyLightColor2), "",
                    click: () => Host.Push(new LedColorScreen(sticks ? "Right stick color" : "Second light color", s.AllyLightColor2,
                        hex => { s.AllyLightColor2 = hex; s.Save(); AllyControl.ApplyLights(force: true); })));
        }
        if (s.AllyLights is "breathing" or "rainbow" or "wheel")
            AddRow("Speed", AllyControl.Speeds.FirstOrDefault(x => x.Id == s.AllyLightSpeed).Name ?? "Medium", "",
                click: () => AllySave(() => s.AllyLightSpeed = s.AllyLightSpeed switch { "slow" => "medium", "medium" => "fast", _ => "slow" }, () => AllyControl.ApplyLights(force: true)));
        if (s.AllyLights is not ("" or "off"))
        {
            AddRow("Brightness", AllyControl.BrightnessPercent(s) + "%", "",
                click: () => AllyPick("Brightness", AllyControl.Brightness, b => b + "%", b => s.AllyLightPercent = b, () => AllyControl.ApplyLights(force: true)));
            AddRow("Off while playing", OnOff(s.AllyLightsOffPlaying), "",
                click: () => AllySave(() => s.AllyLightsOffPlaying = !s.AllyLightsOffPlaying, () => AllyControl.ApplyLights(force: true)),
                subtitle: s.AllyGameLights ? "Game lights are on, so they win: turn those off to use this" : "Saves a little battery; they come back when the game closes");
        }
        AddRow("Game lights while playing", OnOff(s.AllyGameLights), "",
            click: () => AllySave(() => s.AllyGameLights = !s.AllyGameLights, () => AllyControl.ApplyLights(force: true)),
            subtitle: "The game's colors from its cover, or the effect in its game profile");
        AddRow("Pulse red when the battery is low", OnOff(s.AllyLowBatteryFlash), "",
            click: () => AllySave(() => s.AllyLowBatteryFlash = !s.AllyLowBatteryFlash, () => AllyControl.ApplyLights(force: true)),
            subtitle: "At 15% or less, until you plug in");

        // ---- When the lights may light (Armoury Crate SE's power states)
        AddHeader("When the lights are on");
        void Power(string label, bool on, Action flip, string subtitle) =>
            AddRow(label, OnOff(on), "", subtitle: subtitle,
                click: () => AllySave(() => { flip(); s.AllyLightPowerSet = true; }, () => { AllyControl.ApplyLightPower(force: true); AllyControl.ApplyLights(force: true); }));
        if (!s.AllyLightPowerSet) AddNote("Armoury Crate SE's settings apply until you change one here.");
        Power("Boot up and shut down", s.AllyLightBootShutdown, () => s.AllyLightBootShutdown = !s.AllyLightBootShutdown, "While it starts and while it turns off");
        Power("Awake, on battery", s.AllyLightAwakeBattery, () => s.AllyLightAwakeBattery = !s.AllyLightAwakeBattery, "While you use it unplugged");
        if (s.AllyLightAwakeBattery)
            AddRow("Off after a minute without input", OnOff(s.AllyLightBatteryIdleOff), "",
                click: () => AllySave(() => s.AllyLightBatteryIdleOff = !s.AllyLightBatteryIdleOff, () => AllyControl.ApplyLights(force: true)),
                subtitle: s.AllyLights is "" or "off" ? "On battery. Works with an effect chosen above" : "On battery: they come back the moment you touch it");
        Power("Awake, plugged in", s.AllyLightAwakePlugged, () => s.AllyLightAwakePlugged = !s.AllyLightAwakePlugged, "While it's charging or on the charger");
        Power("Sleep", s.AllyLightSleep, () => s.AllyLightSleep = !s.AllyLightSleep, "While it sleeps");

        // ---- The Xbox button's light (ROG Xbox Ally)
        if (AllyControl.HasXboxLight)
        {
            AddHeader("Xbox button light");
            var states = new[] { ("", "Leave as it is"), ("on", "On"), ("off", "Off") };
            AddRow("Light", states.FirstOrDefault(x => x.Item1 == s.AllyXboxLight).Item2 ?? "Leave as it is", "",
                click: () => AllyPick("Xbox button light", states, x => x.Item2, x => { s.AllyXboxLight = x.Item1; if (x.Item1 == "on") AllyControl.WakeLights(s); }, () => AllyControl.ApplyXboxLight(force: true)),
                subtitle: s.AllyXboxLight.Length == 0 ? "Armoury Crate SE (or the last setting) decides" : "The green glow in the Xbox button");
            if (s.AllyXboxLight == "on")
            {
                AddRow("Brightness", AllyControl.XboxLightLevelName(s.AllyXboxLightLevel), "",
                    click: () => AllyPick("Xbox button brightness", AllyControl.XboxLightLevels, l => l.Name, l => s.AllyXboxLightLevel = l.Level, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Bright is how ASUS ships it");
                AddRow("Effect", s.AllyXboxLightBreathe ? "Breathing" : "Steady", "",
                    click: () => AllySave(() => s.AllyXboxLightBreathe = !s.AllyXboxLightBreathe, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Steady, or a slow fade in and out");
                AddRow("Off while playing", OnOff(s.AllyXboxLightOffPlaying), "",
                    click: () => AllySave(() => s.AllyXboxLightOffPlaying = !s.AllyXboxLightOffPlaying, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "Keeps it out of the corner of your eye; it comes back when the game closes");
                AddRow("Breathe when the battery is low", OnOff(s.AllyXboxLightLowBattery), "",
                    click: () => AllySave(() => s.AllyXboxLightLowBattery = !s.AllyXboxLightLowBattery, () => AllyControl.ApplyXboxLight(force: true)),
                    subtitle: "At 15% or less, bright and breathing, until you plug in");
            }
        }

        // ---- Controller: the built-in controller's own firmware settings (they work in every game)
        AddHeader("Controller");
        if (!pad) AddNote("SquareDeck couldn't find the Ally's controller settings device. These settings apply once it's found.");
        AddRow("Rumble strength", s.AllyVibration == 0 ? "Off" : s.AllyVibration + "%", "", click: async () =>
        {
            var dialog = new SliderDialog("Rumble strength", "How strongly the built-in controller vibrates, in every game. ASUS's default is 100%.",
                "Strength", "", s.AllyVibration, null, 0, 100, v => v == 0 ? "Off" : v + "%");
            Host.Push(dialog);
            int v = await dialog.Result;
            AllySave(() => s.AllyVibration = Math.Clamp(v, 0, 100), () => Task.Run(AllyControl.ApplyTuning));
        }, subtitle: "The controller's own strength, for every game. SquareDeck's menu buzz is in Controller and Game Bar widget");
        AddRow("Stick dead zone", s.AllyStickInner + "%", "", click: () => _ = PickPercentAsync("Stick dead zone",
            "How far the sticks move before anything happens. Raise it if a worn stick drifts on its own.", s.AllyStickInner, 0, 30,
            v => s.AllyStickInner = v), subtitle: "Both sticks, in the controller itself. 0% is ASUS's default");
        AddRow("Stick reaches full at", s.AllyStickOuter + "%", "", click: () => _ = PickPercentAsync("Stick reaches full at",
            "How far the sticks travel to count as pushed all the way. Lower it if a stick never quite reaches full.", s.AllyStickOuter, 70, 100,
            v => s.AllyStickOuter = v), subtitle: "100% is ASUS's default");
        AddRow("Trigger dead zone", s.AllyTriggerInner + "%", "", click: () => _ = PickPercentAsync("Trigger dead zone",
            "How far the triggers press before they register. Raise it for hair triggers you brush by accident.", s.AllyTriggerInner, 0, 40,
            v => s.AllyTriggerInner = v), subtitle: "Both triggers. 0% is ASUS's default");
        AddRow("Trigger reaches full at", s.AllyTriggerOuter + "%", "", click: () => _ = PickPercentAsync("Trigger reaches full at",
            "How far the triggers press to count as fully pressed. Lower it for faster full throttle or a quicker shot.", s.AllyTriggerOuter, 60, 100,
            v => s.AllyTriggerOuter = v), subtitle: "100% is ASUS's default");
        if (s.AllyVibration != 100 || s.AllyStickInner != 0 || s.AllyStickOuter != 100 || s.AllyTriggerInner != 0 || s.AllyTriggerOuter != 100)
            AddRow("Put the controller back to ASUS's defaults", null, "", click: () => AllySave(() =>
            {
                s.AllyVibration = 100; s.AllyStickInner = 0; s.AllyStickOuter = 100; s.AllyTriggerInner = 0; s.AllyTriggerOuter = 100;
            }, () => Task.Run(AllyControl.ApplyTuning)));
        if (Labs.Feature("sticks") is not null && LabPage("sticks") is { } sticksPage)
            AddRow("Stick check", null, "", click: sticksPage, subtitle: "See exactly where each stick and trigger is, to set the dead zones");
        AddRow("Mouse mode", OnOff(AllyControl.MouseMode), "",
            click: () => { if (!AllyControl.ToggleMouseMode()) Host.ShowToast("ROG ALLY", "Couldn't change the mode", "", "", "info"); Rebuild(); },
            subtitle: "The right stick moves the pointer and the triggers click, for the desktop. Game mode comes back when SquareDeck closes");
        AddRow("Switch off the built-in controller", null, "", click: async () =>
        {
            if (await Host.AskAsync("Switch off the built-in controller?",
                    "For when you play with another controller, so the Ally's own doesn't also count as a player. " +
                    "Make sure the other controller is connected: the touch screen still works, and the built-in controller comes back when SquareDeck closes or the Ally restarts.",
                    "Switch off", "Switch back on", "Cancel") is int pick and (0 or 1))
            {
                bool ok = AllyHid.SetBuiltInController(pick == 1);
                Host.ShowToast("ROG ALLY", ok ? (pick == 1 ? "Built-in controller on" : "Built-in controller off") : "Couldn't change the controller", "", "", "info");
            }
        }, subtitle: "When another controller is player one");

        // ---- Buttons
        AddHeader("Buttons");
        AddRow("ROG Ally buttons", s.AllyRemap ? "On" : "Off", "", click: () => Host.Push(new AllyButtonsScreen()),
            subtitle: "What Armoury Crate, Command Center, the Xbox Library button" + (s.AllyBackKeys ? ", M1 and M2" : "") + " do; a button test");
        AddRow("M1 and M2 back buttons", s.AllyBackKeys ? "Usable" : "As ASUS set them", "", click: async () =>
        {
            if (!s.AllyBackKeys && await Host.AskAsync("Use the back buttons?",
                    "M1 and M2 will send the F18 and F17 keys, so you can give them actions in ROG Ally buttons (and games can bind them). " +
                    "This puts the controller's other buttons back to ASUS's defaults, undoing mappings made in Armoury Crate SE.",
                    "Use them", "Cancel") != 0) return;
            AllySave(() => s.AllyBackKeys = !s.AllyBackKeys, () => { Task.Run(AllyControl.ApplyController); Host.ApplyAllyButtons(); });
        }, subtitle: s.AllyBackKeys ? "Give them actions in ROG Ally buttons. Put back as ASUS ships them when SquareDeck closes" : "Turn on to give them actions");

        // ---- Battery care
        AddHeader("Battery care");
        AddRow("Stop charging at", s.AllyChargeLimit is > 0 and < 100 ? s.AllyChargeLimit + "%" : s.AllyChargeLimit == 100 ? "100%" : "Leave as it is", "",
            click: () => AllyPick("Stop charging at", AllyControl.ChargeLimits, l => l == 0 ? "Leave as it is" : l + "%", l => s.AllyChargeLimit = l, () => Task.Run(AllyControl.ApplyBattery)),
            subtitle: "80% is kinder to the battery when the Ally is mostly plugged in");
        if (s.AllyChargeLimit is > 0 and < 100)
            AddRow("Charge to 100% this once", AllyControl.ChargingFullOnce ? "Until SquareDeck restarts" : null, "",
                click: () => { AllyControl.ChargeFullOnce(); Rebuild(); },
                subtitle: "For a long trip: charges fully now; the limit comes back next time SquareDeck starts");
        AddRow("Battery health", null, "", click: () => Host.Push(new BatteryScreen()), subtitle: "Wear, charge cycles and how long a charge lasts");

        // ---- ASUS software: Armoury Crate SE (install or remove it with ASUS's own tools), and the ASUS Optimization service
        AddHeader("ASUS software");
        if (_armouryInstalled == null)
            _ = Task.Run(ArmouryCrate.IsInstalled).ContinueWith(r => Dispatcher.Invoke(() => { _armouryInstalled = r.Result; Rebuild(); }));
        AddRow(_armouryInstalled == true ? "Uninstall Armoury Crate SE" : "Install Armoury Crate SE",
            _armouryInstalled == null ? "Checking…" : _armouryInstalled == true ? "Installed" : "Not installed", _armouryInstalled == true ? "\uE74D" : "\uE896",
            click: () => _ = RunArmouryCrateToolAsync(uninstall: _armouryInstalled == true),
            subtitle: _armouryInstalled == true
                ? "Downloads ASUS's Armoury Crate Uninstall Tool and runs it (Windows asks for administrator permission)"
                : "Downloads ASUS's Armoury Crate installer and runs it (Windows asks for administrator permission)");
        if (_armouryInstalled != null)
            AddRow(_armouryInstalled == true ? "Reinstall Armoury Crate SE" : "Run ASUS's uninstall tool anyway", null, _armouryInstalled == true ? "\uE896" : "\uE74D",
                click: () => _ = RunArmouryCrateToolAsync(uninstall: _armouryInstalled != true),
                subtitle: _armouryInstalled == true ? "Runs ASUS's installer again, to repair or update it" : "Clears what's left of an earlier install (services, drivers)");
        if (AsusOptimization.Installed)
        {
            bool on = AsusOptimization.Enabled;
            AddRow("ASUS Optimization service", on ? (AsusOptimization.Running ? "On (running)" : "On") : "Off", "\uE912", click: async () =>
            {
                if (on && await Host.AskAsync("Switch off ASUS Optimization?",
                        "ASUS's background service handles the Ally's special buttons and puts back ASUS's own power limits, so it can take over " +
                        "the buttons SquareDeck maps and the TDP SquareDeck sets. Switching it off stops it and keeps it from starting with Windows. " +
                        "The Ally's lights also stop taking changes (the stick rings and the Xbox button), from SquareDeck and Armoury Crate SE alike. " +
                        "Armoury Crate SE's own button features need it; you can switch it back on here at any time. Windows will ask for permission.",
                        "Switch it off", "Cancel") != 0) return;
                Host.ShowToast("ROG ALLY", on ? "Switching off ASUS Optimization…" : "Switching on ASUS Optimization…", "", "", "info", remember: false);
                var error = await AsusOptimization.SetEnabledAsync(!on);
                if (error != null) await Host.AskAsync("ASUS Optimization", error, "OK");
                else
                {
                    Host.ShowToast("ROG ALLY", on ? "ASUS Optimization is off" : "ASUS Optimization is on",
                        on ? "SquareDeck's buttons and TDP won't be overridden" : "ASUS's own button features are back", "", "info", remember: false);
                    if (on && TdpControl.Ready) _ = TdpControl.ApplyAsync(quiet: true); // SquareDeck's limit, now that nothing puts ASUS's back
                    Host.ApplyAllyButtons();
                }
                Rebuild();
            }, subtitle: on ? "Switch off to stop it overriding SquareDeck's button mapping and TDP (the lights stop taking changes while it's off)" : "Off: SquareDeck's buttons and TDP aren't overridden, but the lights don't take changes. Select to switch it back on");
        }

        AddNote("The firmware and controller messages come from HandheldCompanion by Valkirie (github.com/Valkirie/HandheldCompanion). " +
                "Armoury Crate SE can change the same settings; if it's running, the last one to change something wins.");
    }

    private async Task PickPercentAsync(string title, string message, int value, int min, int max, Action<int> store)
    {
        var dialog = new SliderDialog(title, message, "Percent", "", Math.Clamp(value, min, max), null, min, max, v => v + "%");
        Host.Push(dialog);
        int v = await dialog.Result;
        AllySave(() => store(Math.Clamp(v, min, max)), () => Task.Run(AllyControl.ApplyTuning));
    }

    /// <summary>Picks a light effect and applies it straight away, saying whether the lights took it.</summary>
    private static readonly (string Id, string Name)[] WidgetThemes =
        { ("squaredeck", "SquareDeck teal"), ("style", "Match the UI style"), ("game", "Match the game") };

    public static readonly (string Id, string Name, string About)[] WidgetBackgrounds =
    {
        ("squaredeck", "SquareDeck", "The dark slate gradient"), ("glass", "Frosted glass", "See-through, so the game shows behind it"),
        ("oled", "True black", "Pure black: easiest on the battery with an OLED screen"), ("accent", "Highlight color", "A deep wash of the widget's color"),
        ("aurora", "Aurora", "Teal into violet"), ("sunset", "Sunset", "Magenta into amber"), ("game", "The game's art", "The running game's artwork, dimmed (SquareDeck's dark otherwise)"),
    };

    /// <summary>Adds and removes the widget's quick settings, one press each, until B.</summary>
    private async Task ChooseWidgetQuickAsync()
    {
        var s = Host.Settings;
        while (true)
        {
            var shown = s.WidgetQuick ?? Settings.WidgetQuickTiles.Select(t => t.Id).ToList();
            var options = Settings.WidgetQuickTiles.Select(t => (t.Name + (shown.Contains(t.Id) ? "  ·  shown" : ""), shown.Contains(t.Id) ? "\uE73E" : "\uE710")).ToArray();
            int pick = await Host.AskWithIconsAsync("Widget quick settings", "Select one to add it to the widget or take it off. B when you're done.", options);
            if (pick < 0 || pick >= Settings.WidgetQuickTiles.Length) break;
            var id = Settings.WidgetQuickTiles[pick].Id;
            var list = new List<string>(shown);
            if (!list.Remove(id)) list.Add(id);
            s.WidgetQuick = list;
            s.Save();
            WidgetBridge.Nudge();
        }
        Rebuild();
    }

    private async Task ChooseWidgetBackgroundAsync()
    {
        int pick = await Host.AskAsync("Widget background", "What's behind the widget's cards. It changes as soon as you choose.",
            WidgetBackgrounds.Select(b => b.Name + "  ·  " + b.About).ToArray());
        if (pick >= 0) Toggle(() => Host.Settings.WidgetBackground = WidgetBackgrounds[pick].Id);
        WidgetBridge.Nudge();
    }

    private static string WidgetThemeName(string? id) => WidgetThemes.FirstOrDefault(t => t.Id == id).Name ?? WidgetThemes[0].Name;

    private async Task ChooseWidgetThemeAsync()
    {
        int pick = await Host.AskAsync("Widget colors", "The widget's highlight color. With Match the game, it takes the colors of the game's artwork while one runs (your UI style's color otherwise).",
            WidgetThemes.Select(t => t.Name).ToArray());
        if (pick >= 0) Toggle(() => Host.Settings.WidgetTheme = WidgetThemes[pick].Id);
        WidgetBridge.Nudge();
    }

    private static readonly (string Id, string Name)[] WidgetCards =
        { ("game", "Game"), ("live", "Live"), ("ally", "ROG Ally"), ("quick", "Quick") };

    private static string WidgetCardsLabel(Settings s)
    {
        var on = WidgetCards.Where(c => s.WidgetSections?.Contains(c.Id) != false).Select(c => c.Name).ToList();
        return on.Count == 0 ? "Power only" : string.Join(", ", on);
    }

    private async Task ChooseWidgetCardsAsync()
    {
        var s = Host.Settings;
        s.WidgetSections ??= new List<string>(Settings.WidgetSectionIds);
        while (true)
        {
            var names = WidgetCards.Select(c => (s.WidgetSections.Contains(c.Id) ? "\u2713  " : "      ") + c.Name).ToArray();
            int pick = await Host.AskAsync("Widget cards", "Press a card to show or hide it. B when you're done.", names);
            if (pick < 0) break;
            string id = WidgetCards[pick].Id;
            if (!s.WidgetSections.Remove(id)) s.WidgetSections.Add(id);
            s.Save();
            WidgetBridge.Nudge();
        }
        Rebuild();
    }

    private static bool? _armouryInstalled;

    /// <summary>Downloads ASUS's Armoury Crate tool, unzips it and runs it, with a pop-up showing each step.</summary>
    private async Task RunArmouryCrateToolAsync(bool uninstall)
    {
        if (await Host.AskAsync(uninstall ? "Uninstall Armoury Crate SE?" : "Install Armoury Crate SE?",
                uninstall
                    ? "SquareDeck downloads ASUS's Armoury Crate Uninstall Tool from ASUS and runs it. It removes Armoury Crate SE and its services; SquareDeck's own controls keep working. Windows asks for administrator permission, and ASUS may ask you to restart."
                    : "SquareDeck downloads ASUS's Armoury Crate installer from ASUS and runs it. Windows asks for administrator permission; the installer then downloads and sets up Armoury Crate SE in its own window.",
                uninstall ? "Uninstall" : "Install", "Cancel") != 0) return;
        using var cts = new CancellationTokenSource();
        var screen = new ToolProgressScreen(uninstall ? "Uninstalling Armoury Crate SE" : "Installing Armoury Crate SE", () => { try { cts.Cancel(); } catch (ObjectDisposedException) { } });
        Host.Push(screen);
        string? error;
        try
        {
            var progress = new Progress<(ArmouryCrate.Stage Stage, string Detail, double? Fraction)>(p => screen.Report(p.Stage, p.Detail, p.Fraction));
            error = await ArmouryCrate.RunAsync(uninstall, progress, cts.Token);
        }
        catch (OperationCanceledException) { error = "Cancelled. Nothing was changed."; }
        catch (Exception ex) { error = ex.Message; }
        finally { Host.Pop(screen); }
        _armouryInstalled = null; // looked at again
        await Host.AskAsync(error == null ? (uninstall ? "ASUS's uninstall tool finished" : "ASUS's installer finished") : "Armoury Crate SE", error ??
            (uninstall ? "If ASUS asked for a restart, restart before setting things up again. SquareDeck's buttons, TDP and lights don't need Armoury Crate."
                       : "Armoury Crate SE should be in your Start menu. If its buttons fight with SquareDeck's, set them to do nothing in Armoury Crate SE."), "OK");
        Rebuild();
    }

    /// <summary>Installs (or updates) the Game Bar widget from the Widget folder next to SquareDeck.</summary>
    private async Task InstallWidgetAsync()
    {
        if (WidgetInstaller.Busy) return;
        if (WidgetInstaller.Find() is not { } package)
        {
            await Host.AskAsync("Xbox Game Bar widget", "There's no widget package in " + WidgetInstaller.Folder + ". The installer puts it there; for a build of your own, copy Visual Studio's AppPackages output into that folder.", "OK");
            return;
        }
        await Host.InstallWidgetWithScreenAsync(package, update: WidgetInstaller.InstalledVersion() != null);
        Rebuild();
    }

    private async Task ChooseLightEffectAsync()
    {
        var s = Host.Settings;
        var modes = AllyControl.LightModes;
        int choice = await Host.AskAsync("Light effect", null, modes.Select(m => m.Name).ToArray());
        if (choice < 0 || choice >= modes.Length) return;
        s.AllyLights = modes[choice].Id;
        bool woke = s.AllyLights is not ("" or "off") && AllyControl.WakeLights(s);
        s.Save();
        if (woke) Host.ShowToast("ROG ALLY", "\"Awake\" is back on", "It was off in When the lights are on, which kept the lights dark whatever the effect.", "", "info");
        Rebuild();
        if (s.AllyLights.Length == 0) return;
        if (!await AllyControl.ApplyLightsAsync(force: true))
            Host.ShowToast("ROG ALLY", "Couldn't reach the lights", "SquareDeck will keep trying. Armoury Crate SE may be holding them.", "", "info");
        else if (AllyControl.DynamicLightingOn)
            Host.ShowToast("ROG ALLY", "Windows' Dynamic Lighting is on", "It holds the Ally's lights. Turn it off in Settings > Personalization > Dynamic Lighting.", "", "info");
    }

    /// <summary>The built-in screen: refresh rate and resolution, kept after a restart.</summary>
    private void BuildHandheldScreen()
    {
        var now = DisplaySettings.Current();
        if (now == null) return;
        var modes = DisplaySettings.All();
        AddHeader("Screen");
        var rates = modes.Where(m => m.Width == now.Width && m.Height == now.Height).Select(m => m.RefreshRate).Distinct().OrderByDescending(r => r).ToArray();
        AddRow("Refresh rate", now.RefreshRate + " Hz", "", click: async () =>
        {
            if (rates.Length < 2) { await Host.AskAsync("Refresh rate", "This screen only offers " + now.RefreshRate + " Hz at this resolution.", "OK"); return; }
            int choice = await Host.AskAsync("Refresh rate", "Higher is smoother; lower saves battery.", rates.Select(r => r + " Hz").ToArray());
            if (choice < 0) return;
            if (!DisplaySettings.Apply(new DisplayMode(now.Width, now.Height, rates[choice]), keep: true))
                Host.ShowToast("SCREEN", "Couldn't change the refresh rate", "", "", "info");
            Rebuild();
        }, subtitle: rates.Length > 1 ? string.Join(" or ", rates.Select(r => r + " Hz")) + " on this screen" : null);
        var sizes = modes.Select(m => (m.Width, m.Height)).Distinct().Take(8).ToArray();
        AddRow("Resolution", now.Resolution, "", click: async () =>
        {
            int choice = await Host.AskAsync("Resolution", "Lower resolutions run games faster on battery. Windows keeps the change.",
                sizes.Select(z => $"{z.Width} × {z.Height}" + (z.Width == now.Width && z.Height == now.Height ? "  (now)" : "")).ToArray());
            if (choice < 0) return;
            var size = sizes[choice];
            int rate = modes.Where(m => m.Width == size.Width && m.Height == size.Height).Select(m => m.RefreshRate)
                            .OrderBy(r => Math.Abs(r - now.RefreshRate)).FirstOrDefault(now.RefreshRate);
            if (!DisplaySettings.Apply(new DisplayMode(size.Width, size.Height, rate), keep: true))
                Host.ShowToast("SCREEN", "Couldn't change the resolution", "", "", "info");
            Rebuild();
        });
        AddRow("Brightness, night light and more", null, "", click: () => Host.Push(new QuickSettingsScreen()), subtitle: "In quick settings");
    }

    private void SaveTdp()
    {
        Host.Settings.Save();
        TdpControl.UpdateReapplyTimer();
        _ = ApplyTdpAndRefreshAsync();
        Rebuild();
    }

    private async Task ApplyTdpAndRefreshAsync()
    {
        await TdpControl.ApplyAsync();
        if (IsLoaded) Rebuild();
    }

    private async Task PickWattsAsync(string title, string message, int watts, Action<int> chosen)
    {
        var chip = TdpControl.Chip;
        if (chip == null) return;
        var dialog = new SliderDialog(title, message, "Watts", "\uE945", TdpControl.Clamp(watts), null, chip.Min, chip.Max, v => v + " W");
        Host.Push(dialog);
        chosen(TdpControl.Clamp(await dialog.Result));
    }

    private async Task TdpPresetAsync(Settings s)
    {
        var chip = TdpControl.Chip!;
        var presets = new[] { ("Silent", 8), ("Battery saver", 12), ("Balanced", 18), ("Performance", 25), ("Turbo", chip.Max) }
            .Where(p => p.Item2 >= chip.Min && p.Item2 <= chip.Max).ToArray();
        int choice = await Host.AskAsync("TDP preset", null, presets.Select(p => $"{p.Item1}  ({p.Item2} W)").ToArray());
        if (choice < 0) return;
        s.TdpWatts = presets[choice].Item2;
        SaveTdp();
    }

    private async void SetUpTdp()
    {
        if (await Host.AskAsync("Set up TDP control?",
                (TdpControl.HasBundled ? "SquareDeck will install the copy of RyzenAdj that comes with it. " : "SquareDeck will download RyzenAdj from GitHub. ") +
                "Windows will ask for permission to install it with a " +
                "background task that applies your limits. Changing power limits is at your own risk; stay within your device's range.",
                "Set up", "Cancel") != 0) return;
        bool updating = TdpControl.Installed; // an update keeps the limit you chose
        Host.ShowToast("TDP control", "Setting up…", TdpControl.HasBundled ? "Installing RyzenAdj" : "Downloading RyzenAdj", "", "info");
        var error = await TdpControl.InstallAsync();
        if (error != null)
        {
            await Host.AskAsync("TDP control isn't set up", error, "OK");
        }
        else
        {
            Host.Settings.TdpEnabled = true;
            if (!updating) Host.Settings.TdpWatts = TdpControl.Chip?.Default ?? 15;
            Host.Settings.Save();
            await TdpControl.ApplyAsync();
            Host.ShowToast("TDP control", "Ready", TdpControl.LastResult, "", "info");
        }
        Rebuild();
    }

    private void BuildGames(Settings s)
    {
        AddHeader("Launching games");
        AddRow("Countdown before a game starts", s.LaunchCountdown ? s.LaunchCountdownSeconds + " seconds" : "Off", "\uE916", click: () =>
        {
            // Off -> 3 -> 5 -> 10 -> off
            Toggle(() =>
            {
                if (!s.LaunchCountdown) { s.LaunchCountdown = true; s.LaunchCountdownSeconds = 3; }
                else if (s.LaunchCountdownSeconds < 5) s.LaunchCountdownSeconds = 5;
                else if (s.LaunchCountdownSeconds < 10) s.LaunchCountdownSeconds = 10;
                else s.LaunchCountdown = false;
            });
        }, subtitle: "A moment to change your mind, with Start now and Cancel");
        AddRow("Ask about updates before playing", OnOff(s.AskAboutUpdates), "\uE896", click: () => Toggle(() => s.AskAboutUpdates = !s.AskAboutUpdates),
            subtitle: "When a Steam game has an update waiting: update it first, or play anyway");
        AddRow("Recompiling animation for Recomp Zone games", OnOff(s.RecompLaunchAnimation), "\uE943", click: () => Toggle(() => s.RecompLaunchAnimation = !s.RecompLaunchAnimation),
            subtitle: "The console's machine code streams in and native PC code streams out as the cover is rebuilt");
        AddRow("Splash screen while a game starts", OnOff(s.LaunchSplash), "\uE7F4", click: () => Toggle(() => s.LaunchSplash = !s.LaunchSplash),
            subtitle: "The game's cover fills the screen until the game is running");
        if (s.LaunchSplash)
        {
            string launchName = s.LaunchAnimation == "random" ? "Surprise me" : LaunchSplashScreen.Styles.FirstOrDefault(x => x.Id == s.LaunchAnimation).Name ?? "Classic";
            AddRow("Splash animation", launchName, "\uE768", click: ChooseLaunchAnimation,
                subtitle: "Six to choose from: classic, spotlight, flip, ripples, loading ring and cinematic");
        }
        AddRow("Start Steam games directly", OnOff(s.SteamDirectLaunch), "\uE756", click: () => Toggle(() => s.SteamDirectLaunch = !s.SteamDirectLaunch),
            subtitle: "From the game's own program instead of a Steam link, so Steam's launch pop-up stays away");
        AddNote("With this on, SquareDeck starts the game's .exe itself and tells it which Steam game it is. Steam must already be " +
                "running; if it isn't, or the program can't be found, the game starts through Steam as usual. SquareDeck remembers each " +
                "game's program the first time you play it, and you can pick it yourself in the game's options (Program to start " +
                "directly). Launch options set in Steam aren't used, the Steam overlay may not appear, and a few games (with their own " +
                "launcher or copy protection) still restart themselves through Steam.");

        AddHeader("While playing");
        AddRow("Free graphics memory for the game", OnOff(s.FreeGpuDuringGames), "\uE7F4", click: () =>
            Toggle(() => s.FreeGpuDuringGames = !s.FreeGpuDuringGames));
        AddRow("Use as little memory as possible while playing", OnOff(s.LeanWhilePlaying), "\uE964", click: () =>
            Toggle(() => s.LeanWhilePlaying = !s.LeanWhilePlaying),
            subtitle: "Behind a game, SquareDeck lets go of covers and pictures, stops an idle Spotify player and lets Windows reclaim its memory first");
        if (s.LeanWhilePlaying)
            AddRow("Close open pages while playing", OnOff(s.ClosePagesWhilePlaying), "\uE8BB", click: () =>
                Toggle(() => s.ClosePagesWhilePlaying = !s.ClosePagesWhilePlaying),
                subtitle: "Frees the most memory. After the game you come back to Home instead of the page you left");
        AddRow("Free memory when SquareDeck is out of sight", OnOff(s.LeanInBackground), "\uE964", click: () =>
            Toggle(() => s.LeanInBackground = !s.LeanInBackground),
            subtitle: "After a minute behind another app or minimized, SquareDeck lets go of covers, the moving background and the " +
                      "menu music. They come back when you return; music you're listening to keeps playing");
        AddNote("While a game runs, the launcher stops using the graphics card and lets go of its pictures, so the game gets the " +
                "video memory. Recommended; turn off only if switching back to the launcher feels slow.");
        AddRow("Do not disturb (hide Windows notifications)", OnOff(s.DoNotDisturb), "", click: () =>
            Toggle(() => s.DoNotDisturb = !s.DoNotDisturb));
        AddRow("Return to the launcher when a game closes", OnOff(s.ReturnToLauncher), "", click: () =>
            Toggle(() => s.ReturnToLauncher = !s.ReturnToLauncher));
        AddRow("Session summary when a game closes", OnOff(s.SessionSummary), "", click: () =>
            Toggle(() => s.SessionSummary = !s.SessionSummary));

        AddHeader("Cloud gaming");
        AddNote("Turned-on services appear on the Stores page. They open full screen, and the controller goes straight to the game. " +
                "To leave, hold the leave buttons, press the leave shortcut (both below), or use the small button in a corner.");
        foreach (var service in CloudService.All)
        {
            var cs = service;
            bool on = s.CloudServices.Contains(cs.Id);
            AddRow(cs.Name, OnOff(on), "\uE753", click: () => Toggle(() =>
            {
                if (s.CloudServices.Contains(cs.Id)) s.CloudServices.Remove(cs.Id); else s.CloudServices.Add(cs.Id);
            }), subtitle: cs.About);
        }
        AddRow("Leave with these buttons", CloudGameScreen.LeaveButtonsName(s.CloudLeaveButtons), "\uE7FC",
            click: () => Pick("Buttons that leave a cloud game", CloudGameScreen.LeaveButtons, id => s.CloudLeaveButtons = id),
            subtitle: "Hold them together to close the stream and go back to SquareDeck. Pick ones your games don't need");
        AddRow("How long to hold them", CloudGameScreen.LeaveHolds.FirstOrDefault(h => double.Parse(h.Id, System.Globalization.CultureInfo.InvariantCulture) == s.CloudLeaveHold).Name ?? $"{s.CloudLeaveHold:0.#} seconds", "\uE916",
            click: () => Pick("How long to hold the leave buttons", CloudGameScreen.LeaveHolds, id => s.CloudLeaveHold = double.Parse(id, System.Globalization.CultureInfo.InvariantCulture)),
            subtitle: "Longer means you won't leave by accident; the corner button fills up while you hold");
        AddRow("Leave with this shortcut", CloudGameScreen.LeaveKeyName(s.CloudLeaveKey), "\uE765",
            click: () => Pick("Keyboard shortcut that leaves a cloud game", CloudGameScreen.LeaveKeys, id => s.CloudLeaveKey = id),
            subtitle: "For a keyboard, or handheld buttons set to send keys");
        AddRow("Exit button corner", s.CloudCorner switch { "top-left" => "Top left", "bottom-left" => "Bottom left", "bottom-right" => "Bottom right", _ => "Top right" }, "\uE8A0",
            click: () => Toggle(() => s.CloudCorner = s.CloudCorner switch { "top-right" => "bottom-right", "bottom-right" => "bottom-left", "bottom-left" => "top-left", _ => "top-right" }),
            subtitle: "Where the nearly invisible exit button waits during a cloud game");
        AddRow("Launch animation", CloudIntro.Styles.FirstOrDefault(c => c.Id == s.CloudIntro).Name ?? "Surprise me", "\uE753",
            click: () => Pick("Cloud launch animation", CloudIntro.Styles, id => s.CloudIntro = id),
            subtitle: "Plays while a cloud gaming service opens");
        AddHeader("Library and stores");
        AddRow("Wishlist sale alerts", s.SaleAlerts ? (s.SaleAlertMin <= 1 ? "Any discount" : $"{s.SaleAlertMin}% off or more") : "Off", "\uE8EC",
            click: () => Toggle(() =>
            {
                // Off → any → 25% → 50% → 75% → off
                if (!s.SaleAlerts) { s.SaleAlerts = true; s.SaleAlertMin = 1; }
                else if (s.SaleAlertMin < 25) s.SaleAlertMin = 25;
                else if (s.SaleAlertMin < 50) s.SaleAlertMin = 50;
                else if (s.SaleAlertMin < 75) s.SaleAlertMin = 75;
                else s.SaleAlerts = false;
            }),
            subtitle: "A notification when a game on your Steam wishlist (or one you watch) goes on sale");
        AddRow("Release-day alerts", OnOff(s.ReleaseAlerts), "\uE787", click: () => Toggle(() => s.ReleaseAlerts = !s.ReleaseAlerts),
            subtitle: "A notification the day a wishlist game comes out");
        AddRow("Open the wishlist and release calendar", glyph: "\uE8A7", click: () => Host.Push(new WishlistScreen()));
        AddRow("How long to beat on game pages", OnOff(s.ShowHowLongToBeat), "\uE916",
            click: () => Toggle(() => s.ShowHowLongToBeat = !s.ShowHowLongToBeat),
            subtitle: "Main story, main + extras and completionist times from HowLongToBeat.com, next to your own play time. Each game is looked up once and kept");
        if (s.ShowHowLongToBeat)
            AddRow("Forget the kept HowLongToBeat times", glyph: "\uE74D", click: () =>
            {
                HowLongToBeat.Clear();
                Host.ShowToast("HowLongToBeat", "Times forgotten", "Each game is looked up again when its page opens.", "", "info");
            });
        AddRow("Player stats on game pages", OnOff(s.ShowPlayerStats), "\uE9D9",
            click: () => Toggle(() => s.ShowPlayerStats = !s.ShowPlayerStats),
            subtitle: "Players now and peaks, owner estimate, store data, monthly players and review history, with SteamDB's full page a press away");
        AddRow("Library layout", LibraryScreen.Layouts.First(l => l.Id == s.LibraryLayout || (l.Id == "sidebar" && LibraryScreen.Layouts.All(x => x.Id != s.LibraryLayout))).Name, "\uE8A9",
            click: ChooseLibraryLayout, subtitle: "How the Game library page is laid out: eleven to choose from");
        AddRow("Library view", s.LibraryView switch { "grid" => "Grid", "header" => "Rectangle", _ => "Square" }, "\uE8B9",
            click: () => { s.LibraryView = s.LibraryView switch { "square" => "grid", "grid" => "header", _ => "square" }; s.Save(); Rebuild(); },
            subtitle: "Square covers, tall grid art or wide store headers");
        AddRow("Rescan Steam library", glyph: "", click: Rescan);
        AddRow("Re-download cover art", glyph: "", click: Redownload);
        int hidden = Host.HiddenCount;
        if (hidden > 0)
            AddRow("Restore removed games", hidden.ToString(), "", click: () => { Host.RestoreHidden(); Rebuild(); });
        AddRow("Show games from other stores", OnOff(s.OtherStores), "\uE7BF", click: () =>
        {
            Toggle(() => s.OtherStores = !s.OtherStores);
            _ = Host.ReloadAsync(waitForArt: false);
        });
        if (s.OtherStores)
        {
            // Each store on its own, so one can be left out without losing the others.
            foreach (var (id, name) in ExternalStores.Stores)
            {
                string store = id;
                AddRow(name, OnOff(!s.DisabledStores.Contains(id)), "\uE719", click: () =>
                {
                    Toggle(() =>
                    {
                        if (!s.DisabledStores.Remove(store)) s.DisabledStores.Add(store);
                    });
                    _ = Host.ReloadAsync(waitForArt: false);
                });
            }
        }

        AddHeader("Achievements, play time and captures");
        AddRow("Battery and performance by game", null, "\uE859", click: () => Host.Push(new BatteryInsightScreen()),
            subtitle: "How long a full battery lasts in each game, with its power, frame rate and temperature");
        AddRow("Achievement pop-ups", OnOff(s.AchievementToasts), "", click: () =>
            Toggle(() => s.AchievementToasts = !s.AchievementToasts));
        AddRow("Recent achievements", glyph: "", click: () => Host.Push(new UnlocksScreen()));
        AddRow("Your play time", glyph: "", click: () => Host.Push(new PlaytimeScreen()));

        AddRow("Open Captures as albums (one per game)", OnOff(s.CaptureAlbums), "\uE8B9",
            click: () => Toggle(() => s.CaptureAlbums = !s.CaptureAlbums),
            subtitle: "Albums are made from the game name in each capture's file name");
        AddRow("Show the game's picture next to album names", OnOff(s.CaptureAlbumIcons), "\uE8B9",
            click: () => Toggle(() => s.CaptureAlbumIcons = !s.CaptureAlbumIcons),
            subtitle: "In the Captures albums, for games in your library");

        AddHeader("Library and store features");
        foreach (var id in new[] { "installestimate", "overnight", "closelaunchers", "steamfullscreen", "refund", "trailers", "diary", "guides",
                                   "achshot", "watermark", "milestones", "stickers", "discord" })
            AddFeature(id);
        AddHeader("Pages and services");
        foreach (var id in new[] { "recomp", "freegames", "vacuumtube", "musicstreams", "leaderboard", "boss", "mood", "passport" })
            AddFeature(id);

        AddHeader("Service keys");
        AddRow("Steam Web API key (friends, not-installed games)", s.SteamWebApiKey.Length > 0 ? Mask(s.SteamWebApiKey) : "Not set", "",
            click: () => _ = EditSteamKeyAsync(Host, Rebuild));
        AddRow("SteamGridDB API key (cover art)", Mask(s.SteamGridDbApiKey), "", click: EditApiKey);
        // Game Genie's key lives only here. The key itself is never shown (the built-in one or yours): only which one is in use.
        AddRow("Gemini API key (Game Genie)", GameGenie.UsingOwnKey ? "Yours" : GameGenie.HasKey ? "Built in" : "Not set", "\uE8D7", click: async () =>
        {
            string now = GameGenie.UsingOwnKey
                ? "You're using your own key. Enter a new one to replace it, or leave it empty to go back to the built-in key."
                : "You're using the key built into this copy of SquareDeck. Enter your own (from aistudio.google.com) to use it instead.";
            string? key = await Host.PromptAsync("Gemini API key", now, "", "Save");
            if (key == null) return;
            key = key.Trim();
            // Typing the built-in key again is the same as using the built-in key: it isn't stored as yours.
            if (key == ApiKeys.Gemini) key = "";
            s.GeminiApiKey = key;
            s.Save();
            Rebuild();
        }, subtitle: "Your own Google Gemini key for Game Genie's answers, instead of the one built in");
        AddRow("Prepare Genie answers for new games", s.GeniePrepare ? "On" : "Off", "\uE945", click: () =>
        {
            s.GeniePrepare = !s.GeniePrepare;
            s.Save();
            Rebuild();
        }, subtitle: $"Games added from now on get their answers in the background (up to {GeniePrep.DailyLimit} a day), so Ask Genie opens with them ready. " +
                     $"{GeniePrep.Ready} {(GeniePrep.Ready == 1 ? "game" : "games")} ready" + (GeniePrep.Waiting > 0 ? $", {GeniePrep.Waiting} waiting" : ""));
    }

    private void BuildNetwork(Settings s)
    {
        AddHeader("Connections");
        AddRow("Wi-Fi", glyph: "", click: () => Host.Push(new WifiScreen()));
        AddRow("Bluetooth", glyph: "", click: () => Host.Push(new BluetoothScreen()));
        AddRow("Quick settings (volume, brightness, radios)", glyph: "", click: () => Host.Push(new QuickSettingsScreen()));

        AddHeader("Troubleshooting");
        AddRow("Network speed test", glyph: "", click: () => Host.Push(new SpeedTestScreen()));
        AddRow("Network troubleshooting", glyph: "\uE9D9", click: () => Host.Push(new NetworkToolsScreen()),
            subtitle: "Diagnose, flush DNS, renew the IP address, reset Winsock and TCP/IP");

        AddHeader("Web browser");
        AddRow("Open the browser", glyph: "", click: () => Host.Push(BrowserScreen.Open(Host)));
        AddRow("Show its button on the home screen", OnOff(s.ShowBrowserButton), "", click: () =>
            Toggle(() => s.ShowBrowserButton = !s.ShowBrowserButton));
        AddRow("Open web links in SquareDeck", OnOff(s.OpenLinksInSquareDeck), "\uE71B", click: () =>
            Toggle(() => s.OpenLinksInSquareDeck = !s.OpenLinksInSquareDeck),
            subtitle: s.OpenLinksInSquareDeck ? "Links from news, game pages, updates and settings open here, not in Windows' browser"
                : "Links open in Windows' default browser");
        AddRow("Keep the browser open when you leave it", OnOff(s.BrowserKeepOpen), "\uE8A7", click: () =>
        {
            Toggle(() => s.BrowserKeepOpen = !s.BrowserKeepOpen);
            if (!s.BrowserKeepOpen) BrowserScreen.DisposeKept();
        }, subtitle: "Tabs and pages stay as they were when you come back");
        AddRow("Most tabs open at once", s.BrowserMaxTabs + " tabs", "\uE737", click: async () =>
        {
            int[] counts = { 2, 3, 4, 6, 8, 10, 15 };
            int choice = await Host.AskAsync("Most tabs open at once",
                "Every open tab uses memory and battery, even in the background. When the limit is reached, open a new tab by closing one.",
                counts.Select(c => c + " tabs" + (c == 6 ? " (recommended)" : "")).ToArray());
            if (choice >= 0) Toggle(() => s.BrowserMaxTabs = counts[choice]);
        }, subtitle: "Fewer tabs keep the browser quick on a handheld");
        AddRow("Right stick in the browser", s.BrowserStickMouse ? "Moves a pointer" : "Scrolls the page", "\uE962", click: () =>
            Toggle(() => s.BrowserStickMouse = !s.BrowserStickMouse),
            subtitle: "As a pointer, press the stick in (or A just after moving it) to click");
        AddRow("Home page", s.BrowserHome, "", click: async () =>
        {
            var text = await Host.PromptAsync("Browser home page", "The page the browser opens with.", s.BrowserHome);
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (!text.Contains("://")) text = "https://" + text;
            Toggle(() => s.BrowserHome = text);
        });
        AddRow("Search engine", BrowserScreen.Engines.FirstOrDefault(e => e.Id == s.BrowserSearch).Name ?? "Google", "", click: () =>
        {
            int index = Array.FindIndex(BrowserScreen.Engines, e => e.Id == s.BrowserSearch);
            Toggle(() => s.BrowserSearch = BrowserScreen.Engines[(index + 1) % BrowserScreen.Engines.Length].Id);
        });
        AddRow("Page zoom", s.BrowserZoom + "%", "", click: () =>
        {
            int[] zooms = { 100, 110, 125, 150, 175, 200 };
            int index = Array.IndexOf(zooms, s.BrowserZoom);
            Toggle(() => s.BrowserZoom = zooms[(index + 1) % zooms.Length]);
        });
        AddNote("More (clear cookies and history, set the current page as home) is in the browser's own menu.");
    }

    private void BuildSystem(Settings s)
    {
        AddHeader("This PC");
        AddRow("Display, sound output, sleep, power plan", glyph: "", click: () => Host.Push(new PcSettingsScreen()));
        AddRow("Battery", glyph: "", click: () => Host.Push(new BatteryScreen()));
        AddRow("Low battery notification", s.LowBatteryAlert ? "At " + s.LowBatteryLevel + "%" : "Off", "\uEBA0", click: async () =>
        {
            int[] levels = { 0, 30, 25, 20, 15, 10 };
            int choice = await Host.AskAsync("Low battery notification",
                "A notification when the battery gets down to this level, even over a game, and again at 5%.",
                levels.Select(l => l == 0 ? "Off" : "At " + l + "%").ToArray());
            if (choice < 0) return;
            Toggle(() => { s.LowBatteryAlert = levels[choice] > 0; if (levels[choice] > 0) s.LowBatteryLevel = levels[choice]; });
        }, subtitle: "Shown over games too");
        AddRow("Device information", glyph: "", click: () => Host.Push(new DeviceInfoScreen()));

        AddHeader("Notifications");
        AddRow("Show Windows notifications in the launcher", !s.WindowsNotifications ? "Off" : WindowsNotifications.Status switch
            {
                "allowed" => "On", "denied" => "On, but Windows hasn't allowed it", "unsupported" => "Not supported on this PC", _ => "On"
            }, "\uEA8F", click: ToggleWindowsNotifications);
        if (s.WindowsNotifications && WindowsNotifications.Status == "denied")
            AddRow("Allow it in Windows (Privacy > Notifications)", glyph: "\uE713", click: () => ProcessUtil.Open("ms-settings:privacy-notifications"));
        AddRow("Notifications", Host.Notifications.Unread > 0 ? Host.Notifications.Unread + " new" : null, "",
            click: () => Host.Push(new NotificationsScreen()));

        AddHeader("Maintenance");
        AddRow("Disk cleanup", glyph: "\uE74D", click: () => Host.Push(new DiskCleanupScreen()),
            subtitle: "Temporary files, crash reports, shader and browser caches, the Recycle Bin");
        AddRow("Storage speed test", glyph: "\uEC4A", click: () => Host.Push(new StorageSpeedScreen()),
            subtitle: "How fast the internal drive, a memory card or a USB drive reads and writes");
        AddRow("Format a USB drive or memory card", glyph: "", click: () => Host.Push(new StorageScreen()));
        AddRow("Restart to UEFI / BIOS setup", glyph: "\uE950", click: () => _ = Host.RestartToFirmwareAsync());

        AddHeader("Tools");
        AddRow("Task Manager", "Apps, processes, performance", "", click: () => Host.Push(new TaskManagerScreen()));
        AddRow("File explorer", glyph: "", click: () => Host.Push(new ExplorerScreen()));
        AddFeaturePage("storage", "Storage advisor");
        AddFeaturePage("cleanup", "Library cleanup");
        BuildAdrenalineHook(s);
        AddRow("Open Terminal", "Command Prompt or PowerShell", "\uE756", click: async () =>
        {
            int shell = await Host.AskWithIconsAsync("Open Terminal", "Runs inside SquareDeck. Type with the keyboard, or press A for the touch keyboard.",
                ("Command Prompt", "\uE756"), ("PowerShell", "\uE756"));
            if (shell >= 0) Host.Push(new TerminalScreen(powerShell: shell == 1));
        }, subtitle: "Type and run commands here; Y has handy ones (network, disk space, battery report)");

        AddHeader("Windows");
        void Tweak(string label, Func<bool> get, Action<bool> set, string glyph, string about) =>
            AddRow(label, OnOff(get()), glyph, click: () =>
            {
                try { set(!get()); }
                catch (Exception ex) { _ = Host.AskAsync("Couldn't change that", ex.Message, "OK"); }
                Rebuild();
            }, subtitle: about);
        Tweak("Dark mode for Windows", () => WindowsTweaks.DarkMode, v => WindowsTweaks.DarkMode = v, "\uE793", "Windows' own apps, menus and taskbar");
        Tweak("Game Mode", () => WindowsTweaks.GameMode, v => WindowsTweaks.GameMode = v, "\uE7FC", "Gives games priority and keeps Windows Update quiet while you play");
        Tweak("Background recording", () => WindowsTweaks.BackgroundRecording, v => WindowsTweaks.BackgroundRecording = v, "\uE714",
            "Game Bar keeps the last minutes of play to save as a clip. Off saves battery and performance");
        Tweak("Xbox button opens Game Bar", () => WindowsTweaks.GameBarOnGuide, v => WindowsTweaks.GameBarOnGuide = v, "\uE990",
            "Turn off so the Guide button only brings up SquareDeck");
        Tweak("Transparency effects", () => WindowsTweaks.Transparency, v => WindowsTweaks.Transparency = v, "\uE727", "See-through Windows menus (off saves a little battery)");
        Tweak("Sticky Keys shortcut", () => WindowsTweaks.StickyKeysShortcut, v => WindowsTweaks.StickyKeysShortcut = v, "\uE765",
            "The pop-up after pressing Shift five times, often set off by handheld buttons");
        AddRow("More Windows settings", null, "\uE713", click: async () =>
        {
            var pages = WindowsTweaks.Pages;
            int choice = await Host.AskWithIconsAsync("Windows settings", "Opens the page in Windows Settings.", pages.Select(p => (p.Name, p.Glyph)).ToArray());
            if (choice >= 0) ProcessUtil.Open(pages[choice].Uri);
        }, subtitle: "Windows Update, night light, HDR and VRR, battery saver, storage and more");

        AddHeader("Starting with Windows");
        AddRow("Start with Windows", OnOff(Startup.RunAtSignIn), "", click: () =>
        {
            try { Startup.RunAtSignIn = !Startup.RunAtSignIn; }
            catch (Exception ex) { _ = Host.AskAsync("Couldn't change startup", ex.Message, "OK"); }
            Rebuild();
        });
        AddRow("Replace the Windows desktop", OnOff(Startup.ReplacesDesktop), "", click: ToggleDesktop);
        bool homeApp = HomeApp.IsHomeApp || (AnyFse.IsInstalled() && AnyFse.LaunchesUs() && AnyFse.IsHomeApp());
        AddRow("Xbox mode home app", homeApp ? "SquareDeck" : "Set up", "\uE7FC",
            click: () => Host.Push(new HomeAppScreen()), subtitle: "Start Xbox mode (the full screen experience) straight into SquareDeck");
    }

    /// <summary>Features still being tried out. Each one is off until you turn it on here.</summary>
    private void BuildExperimental(Settings s)
    {
        AddHeader("Experimental features");
        AddNote("Features that are still being tried out live here first. They may change, move or go away, and " +
                "they might not work everywhere yet. Nothing here is turned on unless you turn it on. Features that have " +
                "settled in have moved to their own places in Settings.");

        BuildLabs();
    }

    /// <summary>Adrenaline Hook (off until turned on): in System and tools.</summary>
    private void BuildAdrenalineHook(Settings s)
    {
        bool amd = AdrenalineHook.HasAmdGraphics;
        AddRow("Adrenaline Hook", amd ? OnOff(s.ExperimentAdrenalineHook) : "Needs AMD graphics", "\uE7F4", click: () =>
        {
            if (!amd)
            {
                _ = Host.AskAsync("Adrenaline Hook", "Adrenaline Hook adds games to AMD Software: Adrenalin Edition, so it needs AMD Radeon graphics. " +
                    "This PC has " + (AdrenalineHook.GraphicsName.Length > 0 ? AdrenalineHook.GraphicsName : "no AMD graphics") + ".", "OK");
                return;
            }
            s.ExperimentAdrenalineHook = !s.ExperimentAdrenalineHook;
            s.Save();
            Rebuild();
        }, subtitle: "Adds Xbox, Microsoft Store and other games to AMD Software, so its per-game settings (frame generation, sharpening and more) work for them");
        if (amd && s.ExperimentAdrenalineHook)
            AddRow("Open Adrenaline Hook", null, "\uE768", click: () => Host.Push(new AdrenalineHookScreen()),
                subtitle: "Hook games, AMD per-game settings, the Radeon overlays, backups");
    }

    /// <summary>The newer experimental features, by group: a switch for each, and its page once it's on.</summary>
    private void BuildLabs()
    {
        foreach (var group in Labs.Features.Where(f => !Labs.Graduated.Contains(f.Id)).GroupBy(f => f.Group))
        {
            AddHeader(group.Key);
            foreach (var f in group)
            {
                var feature = f;
                bool on = Labs.On(f.Id);
                string? blocked = f.Id == "tilt" && Host?.HasMotionSensor == false ? "No motion sensor" : null;
                AddRow(f.Name, blocked ?? OnOff(on), f.Glyph, click: () =>
                {
                    if (blocked != null && !on) { _ = Host.AskAsync(feature.Name, "This PC doesn't report a motion sensor, so the covers can't follow its tilt.", "OK"); return; }
                    Labs.Set(feature.Id, !on);
                    Rebuild();
                }, subtitle: f.About);
                if (on && LabPage(f.Id) is { } open) AddRow("Open " + f.Name.ToLowerInvariant(), null, "\uE768", click: open);
            }
        }
    }

    private Action? LabPage(string id) => id switch
    {
        "storage" => () => Host.Push(new StorageAdvisorScreen()),
        "cleanup" => () => Host.Push(new CleanupScreen()),
        "overnight" => () => Host.Push(new OvernightScreen()),
        "bugreport" => () => Host.Push(new BugReportScreen()),
        "refund" => () => Host.Push(new RefundScreen()),
        "sticks" => () => Host.Push(new StickCheckScreen()),
        "battery" => () => Host.Push(new BatteryHealthScreen()),
        "recomp" => () => Host.Push(new RecompZoneScreen()),
        "discord" => () => Host.Push(new DiscordScreen()),
        "musicstreams" => () => { Host.Settings.MusicTab = "streaming"; Host.Push(new MediaScreen()); },
        "freegames" => () => Host.Push(new FreeGamesScreen()),
        "leaderboard" => () => Host.Push(new LeaderboardScreen()),
        "styleschedule" => () => Host.Push(new StyleScheduleScreen()),
        "wallpaperengine" => () => Host.Push(new WallpaperEngineScreen()),
        "boss" => () => Host.Push(new BossScreen()),
        "unlockables" => () => Host.Push(new UnlockablesScreen()),
        "passport" => () => Host.Push(new PassportScreen()),
        "mood" => () => Host.Push(new MoodScreen()),
        "companion" => () => Host.Push(new CompanionScreen()),
        "settingsview" => ChooseLayout,
        "coverdim" => () => Host.Push(new CoverLabsScreen(effect: false)),
        "covereffect" => () => Host.Push(new CoverLabsScreen(effect: true)),
        "closelaunchers" => async () =>
        {
            bool steam = Labs.Get("closelaunchers.steam") == "1";
            int c = await Host.AskAsync("Close Steam too?", "Steam also handles downloads, friends and chat, so it's left open unless you choose otherwise. " +
                "It's never closed while it's downloading.", steam ? "Keep closing Steam" : "Close Steam too", steam ? "Leave Steam open" : "Leave Steam open");
            if (c == 0) Labs.Put("closelaunchers.steam", "1");
            else if (c == 1) Labs.Put("closelaunchers.steam", "0");
        },
        _ => EightPage(id)
    };

    /// <summary>A feature that has left Experimental: its switch, and a row to open its page while it's on.</summary>
    private void AddFeature(string id, string? name = null)
    {
        if (Labs.Feature(id) is not { } f) return;
        bool on = Labs.On(id);
        string label = name ?? f.Name;
        string? blocked = id == "tilt" && Host?.HasMotionSensor == false ? "No motion sensor" : null;
        AddRow(label, blocked ?? OnOff(on), f.Glyph, click: () =>
        {
            if (blocked != null && !on) { _ = Host.AskAsync(label, "This PC doesn't report a motion sensor, so the covers can't follow its tilt.", "OK"); return; }
            Labs.Set(id, !on);
            Rebuild();
        }, subtitle: f.About);
        if (on && LabPage(id) is { } open) AddRow("Open " + label.ToLowerInvariant(), null, "", click: open);
    }

    /// <summary>A feature that's always there now: a row that opens its page.</summary>
    private void AddFeaturePage(string id, string label, string? subtitle = null)
    {
        if (Labs.Feature(id) is not { } f || LabPage(id) is not { } open) return;
        AddRow(label, null, f.Glyph, click: open, subtitle: subtitle ?? f.About);
    }

    /// <summary>Covers that tilt: how far they move, how they move, the zoom, and which way.</summary>
    private void BuildTiltOptions()
    {
        string strength = Labs.Get("tilt.strength", "1");
        AddRow("Tilt strength", strength switch { "0.5" => "Subtle", "1.6" => "Strong", "2.4" => "Extreme", _ => "Normal" }, "",
            click: () => { Labs.Put("tilt.strength", strength switch { "0.5" => "1", "1" => "1.6", "1.6" => "2.4", _ => "0.5" }); Rebuild(); });
        string style = Labs.Get("tilt.style", "shift");
        AddRow("Tilt effect", style switch { "swing" => "Swing", "skew" => "3D lean", "float" => "Float with shadow", _ => "Parallax shift" }, "",
            click: () => { Labs.Put("tilt.style", style switch { "shift" => "swing", "swing" => "skew", "skew" => "float", _ => "shift" }); Rebuild(); },
            subtitle: "Parallax shift slides the art, Swing turns it, 3D lean leans it like a card, Float lifts it with a moving shadow");
        bool zoom = Labs.Get("tilt.zoom", "1") == "1";
        AddRow("Zoom the tilted cover", OnOff(zoom), "", click: () => { Labs.Put("tilt.zoom", zoom ? "0" : "1"); Rebuild(); },
            subtitle: "A little larger, so its edges never show while it moves");
        bool invert = Labs.Get("tilt.invert") == "1";
        AddRow("Reverse the direction", OnOff(invert), "", click: () => { Labs.Put("tilt.invert", invert ? "0" : "1"); Rebuild(); });
    }

    private void BuildData(Settings s)
    {
        AddHeader("Updates");
        // Press it five times for a surprise.
        AddRow("This version", "SquareDeck " + Updater.Current, "", click: () => EasterEgg.Tap(Host));
        AddRow("Check for updates automatically", OnOff(s.AutoUpdateCheck), "", click: () =>
            Toggle(() => s.AutoUpdateCheck = !s.AutoUpdateCheck));
        AddRow("Check for updates now", glyph: "", click: () => _ = Host.CheckForUpdatesAsync(manual: true));
        AddRow("What's new after an update", OnOff(s.WhatsNewAfterUpdate), "\uE789", click: () =>
            Toggle(() => s.WhatsNewAfterUpdate = !s.WhatsNewAfterUpdate), subtitle: "The new version's changes, once, the first time SquareDeck starts after an update");
        AddRow("What's new in this version", null, "\uE81C", click: () => _ = ShowThisVersionAsync());
        AddFeature("autoupdate");
        AddFeature("bugreport");
        if (s.SkippedVersion.Length > 0)
            AddRow("Stop ignoring " + s.SkippedVersion, glyph: "", click: () => Toggle(() => s.SkippedVersion = ""));
        AddRow("View changelogs", null, "\uE81C", subtitle: "What changed in each version", click: () => Host.Push(new ChangelogScreen()));
        AddRow("Credits", null, "\uE946", subtitle: "The artists behind the Advanced styles' kits, sounds and fonts", click: () => Host.Push(new CreditsScreen()));
        AddRow("Update screen animation", SilentUpdate.Looks.FirstOrDefault(l => l.Id == s.UpdaterLook).Name ?? "A different one each time", "\uE768",
            click: () => Pick("Update screen animation", SilentUpdate.Looks.Select(l => (l.Id, l.Name, "")).ToArray(), id => s.UpdaterLook = id),
            subtitle: "What moves behind the progress while SquareDeck updates itself");
        AddRow("Update screen vibration", SilentUpdate.Haptics.FirstOrDefault(h => h.Id == s.UpdaterHaptic).Name ?? "A different one each time", "\uE7FC",
            click: () => Pick("Update screen vibration", SilentUpdate.Haptics.Select(h => (h.Id, h.Name, "")).ToArray(), id => s.UpdaterHaptic = id),
            subtitle: s.RumbleStrength > 0 ? "How the controller vibrates during an update" : "Turn on vibration in Controller settings to feel it");
        AddRow("Try the update screen", glyph: "\uE7B3", click: () => Host.PreviewUpdateScreen(),
            subtitle: "Plays it without installing anything. Press any button to close it");

        AddHeader("Your data");
        AddRow("Backup and restore", glyph: "", click: () => Host.Push(new BackupScreen()));
        AddRow("Downloaded art and video", CacheLabel(s), "", click: CacheOptions);
        AddHeader("Start over");
        AddRow("Run first-time setup again", glyph: "", click: () => _ = Host.RunSetupAsync());
        AddRow("Reset the launcher", "Settings only, or everything", "", click: () => _ = Host.ResetLauncherAsync());
    }

    private async void ChooseExitStyle()
    {
        int choice = await Host.AskAsync("Closing animation", "Plays when you choose Exit SquareDeck from the Power menu.",
            MainWindow.ExitStyles.Select(x => x.Name).ToArray());
        if (choice >= 0) Toggle(() => { Host.Settings.ExitStyle = MainWindow.ExitStyles[choice].Id; Host.Settings.ExitAnimation = true; });
    }

    private static string OnOff(bool on) => on ? "On" : "Off";

    private static readonly int[] CacheLimits = { 0, 250, 500, 1000, 2000, 5000 };

    private static string LimitName(int mb) => mb <= 0 ? "No limit" : mb >= 1000 ? mb / 1000 + " GB" : mb + " MB";

    private static string CacheLabel(Settings s) =>
        $"{CacheControl.SizeBytes() / 1024d / 1024d:0} MB used, limit: {LimitName(s.CacheLimitMb).ToLowerInvariant()}";

    private async void CacheOptions()
    {
        var s = Host.Settings;
        var labels = CacheLimits.Select(m => "Limit: " + LimitName(m)).Append("Clear downloaded art now").ToArray();
        int choice = await Host.AskAsync("Downloaded art and video",
            "Covers, backgrounds, logos, animated covers and store details saved on this PC. When the limit is reached the " +
            "items you haven't seen for longest are removed; they download again when needed. Images you chose yourself are never removed.",
            labels);
        if (choice < 0) return;
        if (choice == CacheLimits.Length)
        {
            _ = Host.RedownloadArtAsync();
        }
        else
        {
            s.CacheLimitMb = CacheLimits[choice];
            s.Save();
            if (s.CacheLimitMb > 0) await Task.Run(() => CacheControl.Trim(s.CacheLimitMb * 1024L * 1024L));
        }
        Rebuild();
    }

    private async void ClockOptions()
    {
        var s = Host.Settings;
        int choice = await Host.AskAsync("Clock", null,
            s.Clock24Hour ? "Use 12-hour time" : "Use 24-hour time",
            s.ClockSeconds ? "Hide seconds" : "Show seconds",
            s.ClockDate ? "Hide the date" : "Show the date");
        if (choice < 0) return;
        if (choice == 0) s.Clock24Hour = !s.Clock24Hour;
        else if (choice == 1) s.ClockSeconds = !s.ClockSeconds;
        else s.ClockDate = !s.ClockDate;
        s.Save();
        Host.ApplySettings();
        Rebuild();
    }

    private async void ChooseBootVideo()
    {
        var s = Host.Settings;
        int choice = await Host.AskAsync("Boot video",
            "Played when the launcher opens, instead of the logo animation. Any button skips it. " +
            "\u201CStartup chime and intro\u201D must be on.",
            "Choose a video\u2026", "Use the built-in animation", "Cancel");
        if (choice == 1) s.BootVideo = "";
        else if (choice == 0)
        {
            var file = await Host.PickFileAsync("Choose a boot video", new[] { ".mp4", ".wmv", ".avi", ".mov", ".mkv", ".webm" });
            if (file == null) return;
            s.BootVideo = file;
        }
        else return;
        s.Save();
        Rebuild();
    }

    private async void ChooseLibraryLayout()
    {
        var s = Host.Settings;
        var layouts = LibraryScreen.Layouts;
        int choice = await Host.AskAsync("Library layout", null, layouts.Select(l => l.Name).ToArray());
        if (choice < 0) return;
        s.LibraryLayout = layouts[choice].Id;
        if (s.LibraryLayout == "showcase") s.LibraryView = "header"; // Showcase is rectangles
        s.Save();
        Rebuild();
    }

    /// <summary>Pick one of the boost animations (or a different one each time); each plays as it's chosen.</summary>
    private async void ChooseLaunchAnimation()
    {
        var s = Host.Settings;
        var names = LaunchSplashScreen.Styles.Select(x => x.Name + "  —  " + x.About).Append("Surprise me  —  a different one each time").ToArray();
        int choice = await Host.AskAsync("Splash animation", "What plays while a game starts. Pick one to see it.", names);
        if (choice < 0) return;
        s.LaunchAnimation = choice < LaunchSplashScreen.Styles.Length ? LaunchSplashScreen.Styles[choice].Id : "random";
        s.Save();
        Rebuild();
        Host.Push(LaunchSplashScreen.Preview(s.LaunchAnimation));
    }

    private async void ChooseBoostAnimation()
    {
        var s = Host.Settings;
        var names = BoostScreen.Styles.Select(x => x.Name).Append("Surprise me").ToArray();
        int current = Array.FindIndex(BoostScreen.Styles, x => x.Id == s.BoostAnimation);
        int choice = await Host.AskAsync("Boost animation", null, names);
        if (choice < 0) return;
        s.BoostAnimation = choice < BoostScreen.Styles.Length ? BoostScreen.Styles[choice].Id : "random";
        s.Save();
        Rebuild();
        Host.Push(new BoostScreen(null, s.BoostAnimation, s.BoostRumble, preview: true));
    }

    private async void RumbleStrength()
    {
        var s = Host.Settings;
        var dialog = new SliderDialog("Vibration strength",
            "How strongly the controller buzzes as you move around. Slide to the far left to turn it off.",
            "Strength", "\uE7FC", s.RumbleStrength, v => Host.RumbleTest(v));
        Host.Push(dialog);
        s.RumbleStrength = await dialog.Result;
        s.Vibration = s.RumbleStrength > 0;
        s.Save();
        Rebuild();
    }

    private async void ChooseLaunchChime()
    {
        var s = Host.Settings;
        int choice = await Host.AskAsync("Game start chime", "Played each time you start a game.",
            UiSound.LaunchChimes.Select(c => c.Name).ToArray());
        if (choice < 0) return;
        var id = UiSound.LaunchChimes[choice].Id;

        if (id == "custom")
        {
            var picked = await Host.PickFileAsync("Choose a sound", new[] { ".wav" });
        if (picked == null) return;
            s.LaunchChimeFile = picked;
        }
        s.LaunchChime = id;
        s.Save();
        UiSound.SetLaunchChime(s.LaunchChime, s.LaunchChimeFile);
        UiSound.Launch(); // preview
        Rebuild();
    }

    /// <summary>Asks for the user's Steam Web API key and saves it. Shared with the Friends page.</summary>
    public const string SteamKeyPage = "https://steamcommunity.com/dev/apikey";

    public static async Task EditSteamKeyAsync(MainWindow host, Action done)
    {
        // A key copied from Steam's page is offered straight away.
        string copied = "";
        try
        {
            var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText().Trim() : "";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9A-Fa-f]{32}$") && text != host.Settings.SteamWebApiKey) copied = text;
        }
        catch (Exception) { }

        bool has = host.Settings.SteamWebApiKey.Length > 0;
        var options = new List<(string, string)>
        {
            ("Sign in with Steam (scan a QR code with the Steam app)", "\uE8D7"),
            (copied.Length > 0 ? "Use the key I copied" : "Enter my key", copied.Length > 0 ? "\uE77F" : "\uE765"),
            ("Get a key from Steam (opens in the browser)", "\uE774")
        };
        if (has) options.Add(("Remove my key", "\uE74D"));
        int choice = await host.AskWithIconsAsync("Steam Web API key",
            "A free key from Steam lets SquareDeck list your friends and the games you own but haven't installed. " +
            "On Steam's page, sign in, type any domain name (for example \"localhost\"), agree, and copy the key. " +
            "It's stored only on this PC.", options.ToArray());
        if (choice == 0)
        {
            // Steam's own sign-in (a QR code for the Steam app), then SquareDeck reads or registers the key itself.
            var signIn = new SteamSignInScreen();
            host.Push(signIn);
            if (await signIn.Result) done();
            return;
        }
        choice--; // the rest as before
        if (choice == 1)
        {
            host.Push(BrowserScreen.Open(host, SteamKeyPage));
            host.ShowToast("Steam Web API key", "Copy your key, then come back", "Press B to leave the browser, then choose Steam Web API key again.", "", "info");
            return;
        }
        if (choice == 2)
        {
            host.Settings.SteamWebApiKey = "";
            host.Settings.Save();
            done();
            return;
        }
        if (choice != 0) return;
        var key = await host.PromptAsync("Steam Web API key", "Paste or type the 32-character key from " + SteamKeyPage + ".",
            copied.Length > 0 ? copied : host.Settings.SteamWebApiKey);
        if (key == null) return;
        host.Settings.SteamWebApiKey = key.Trim();
        host.Settings.Save();
        done();
    }

    private static readonly (string Id, string Name)[] SummonCombos =
    {
        ("default", "Guide, or View + Menu"), ("guide", "Guide only"), ("view+menu", "View + Menu"),
        ("l3+r3", "Both sticks pressed in"), ("lb+rb+view", "LB + RB + View")
    };

    private static string SummonLabel(string id) => SummonCombos.FirstOrDefault(c => c.Id == id).Name ?? SummonCombos[0].Name;

    private async void ChooseSummonButtons()
    {
        int choice = await Host.AskAsync("Buttons that open the Game Bar widget",
            "Hold them together while a game is running to open SquareDeck's widget in Game Bar (or, without a game, bring SquareDeck forward). Pick a combination your games don't use. " +
            "Windows may keep the Guide button for the Game Bar; if it does, use one of the others.",
            SummonCombos.Select(c => c.Name).ToArray());
        if (choice >= 0) Toggle(() => Host.Settings.SummonCombo = SummonCombos[choice].Id);
    }

    private async void ChooseSummonKey()
    {
        int choice = await Host.AskAsync("Keyboard shortcut for the Game Bar widget",
            "Works anywhere in Windows while the launcher is open, including inside a game. Handy for handhelds whose extra " +
            "buttons can be set to send a key.", MainWindow.SummonKeys.Select(k => k.Name).ToArray());
        if (choice >= 0) Toggle(() => Host.Settings.SummonKey = MainWindow.SummonKeys[choice].Id);
    }

    private async void ChooseBootAnimation()
    {
        int choice = await Host.AskAsync("Startup animation", "Plays when the launcher opens. A boot video, if one is set, replaces it.",
            MainWindow.BootAnimations.Select(b => b.Name).ToArray());
        if (choice < 0) return;
        Toggle(() => Host.Settings.BootAnimation = MainWindow.BootAnimations[choice].Id);
        if (Host.Settings.StartupIntro) Host.PreviewIntro();
    }

    private async void ChooseDisplay()
    {
        var displays = Monitors.List();
        var labels = new List<string> { "Windows decides" };
        labels.AddRange(displays.Select(d => d.Label));
        int choice = await Host.AskAsync("Main display for the launcher",
            "The launcher opens on this display and moves there now. If it isn't connected, Windows' own choice is used.",
            labels.ToArray());
        if (choice < 0) return;
        Host.Settings.MainDisplay = choice == 0 ? "" : displays[choice - 1].Device;
        Host.Settings.Save();
        Host.ApplyMainDisplay();
        Rebuild();
    }

    private async void ToggleWindowsNotifications()
    {
        var s = Host.Settings;
        s.WindowsNotifications = !s.WindowsNotifications;
        s.Save();
        if (s.WindowsNotifications) await WindowsNotifications.RequestAccessAsync();
        if (Host.IsTop(this)) Rebuild();
    }

    /// <summary>The background music picker, shared with the Interface sounds page.</summary>
    public static async Task ChooseBackgroundMusicAsync(MainWindow host, Action done)
    {
        var tracks = BackgroundMusic.List();
        var labels = new List<string> { "Off", "Shuffle all" };
        labels.AddRange(tracks.Select(t => t.Name));
        int choice = await host.AskAsync("Background music", "Plays only while no game is running.", labels.ToArray());
        if (choice < 0) return;
        host.Settings.MenuMusic = choice == 0 ? "" : choice == 1 ? "shuffle" : tracks[choice - 2].Id;
        host.Settings.Save();
        host.RestartMenuMusic();
        done();
    }

    private async void ToggleNightLight()
    {
        var on = NightLight.IsOn;
        if (on == null || !NightLight.Set(!on.Value))
        {
            if (await Host.AskAsync("Night light", "Windows doesn't let the launcher switch Night light on this PC. Its settings page can.",
                    "Open Night light settings", "Cancel") == 0)
                ProcessUtil.Open("ms-settings:nightlight");
            return;
        }
        await Task.Delay(400); // Windows picks the change up a moment later
        Rebuild();
    }

    private async Task EditSpotifyIdAsync()
    {
        var id = await Host.PromptAsync("Spotify Client ID",
            "From your app's page at developer.spotify.com (Dashboard > your app > Settings). Its redirect URI must be " +
            SpotifyClient.RedirectUri + ".", Host.Settings.SpotifyClientId, "Save");
        if (id == null) return;
        if (id.Trim() != Host.Settings.SpotifyClientId) Host.Settings.SpotifyToken = ""; // a sign-in belongs to one app
        Host.Settings.SpotifyClientId = id.Trim();
        Host.Settings.Save();
        Rebuild();
    }

    private async Task SpotifySignInAsync(SpotifyClient spotify)
    {
        if (spotify.IsSignedIn)
        {
            if (await Host.AskAsync("Sign out of Spotify?", null, "Cancel", "Sign out") == 1) spotify.SignOut();
            Rebuild();
            return;
        }
        try
        {
            await Host.RunCancellableBusyAsync("Finish signing in to Spotify in your browser\u2026", token => spotify.SignInAsync(token));
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't sign in to Spotify", ex.Message, "OK");
        }
        Rebuild();
    }

    /// <summary>A page of pictures of each choice; choosing one stores it and applies the look straight away.</summary>
    private void Preview(string title, string note, (string Id, string Name, string About)[] options, Func<string> current,
        Func<string, FrameworkElement> draw, Action<string> store)
    {
        Host.Push(new PreviewPickerScreen(title, note, options, current, draw, id =>
        {
            store(id);
            Host.Settings.Save();
            Host.ApplyLook();
            Host.ApplySettings();
        }));
    }

    public static readonly (string Id, string Name, string About)[] UiFonts =
    {
        ("style", "Match the style", "Each UI style comes with a font picked to go with it"),
        ("Segoe UI", "Segoe UI", "Windows' clean everyday font"),
        ("Segoe UI Variable Display", "Segoe UI Variable", "Windows 11's rounder, modern take"),
        ("Bahnschrift", "Bahnschrift", "Condensed and technical, like signage"),
        ("Trebuchet MS", "Trebuchet", "Friendly and a little playful"),
        ("Franklin Gothic Medium", "Franklin Gothic", "Bold, newsy headlines"),
        ("Georgia", "Georgia", "A readable serif with character"),
        ("Palatino Linotype", "Palatino", "Elegant, classic book type"),
        ("Cascadia Mono, Consolas", "Cascadia Mono", "Monospaced, like a terminal"),
        ("Ink Free", "Ink Free", "Handwritten marker"),
        ("Comic Sans MS", "Comic Sans", "Casual and cartoony"),
        // Fonts that come with SquareDeck (its Fonts folder)
        ("file:Modesans-FreeDemo.otf#Modesans#Modesans Free Demo", "Modesans", "Clean geometric sans (this demo has no numbers)"),
        ("file:Vislar-Thin.otf#Vislar#Vislar Thin", "Vislar", "Thin, airy and futuristic"),
        ("file:Synth2-Thin.ttf#Synth#Synth Thin", "Synth", "Thin retro-future lines, very 80s"),
        ("file:ESSENTIALO.ttf#ESSENTIALO", "Essentialo", "Scratchy, hand-drawn and a little eerie"),
        ("file:Glafort-Regular.ttf#Glafort Demo#Glafort", "Glafort", "Light, stylish display type (this demo has no numbers)"),
        ("file:Against_Perfection_Serif_Personal_Use.ttf#Against Perfection Personal Use#Against Perfection Personal Use Serif", "Against Perfection", "Rough, imperfect serif with charm"),
        ("file:Amazing_Midnight_Personal_Use.ttf#Amazing Midnight Personal Use", "Amazing Midnight", "Tall, condensed poster lettering"),
        ("file:SoulSeashell-Regular.ttf#Soul Seashell", "Soul Seashell", "Soft, flowing beach-day script"),
        ("file:Dreamink_Cream_Ginger.ttf#Dreamink Cream Ginger", "Dreamink", "Sweet, rounded hand lettering"),
        ("file:Jotbrisk_Demo.otf#Jotbrisk Demo", "Jotbrisk", "Quick, brisk handwriting (this demo has no numbers)"),
        ("file:Always_be_honest.otf#Always be honest", "Always Be Honest", "Neat, friendly handwriting"),
        ("file:ChildnotesHandwriting-Demo.otf#Childnotes Handwriting#Childnotes Handwriting Demo", "Childnotes", "Notes from a school notebook (demo: numbers show as DEMO)"),
        ("file:Aboli-DemoVersion-Regular.ttf#Aboli - Demo Version", "Aboli", "Chunky, playful and bouncy (demo: numbers show as DEMO)"),
        ("file:Happy_Chicken.ttf#Happy Chicken", "Happy Chicken", "Goofy, cartoony and fun"),
        ("file:HomeVideo-Regular.ttf#Home Video", "Home Video", "A VHS camcorder date stamp"),
        ("file:pixel-it-5x4.ttf#Pixel It! 5x4", "Pixel It", "Tiny blocky pixels, like an old handheld"),
        ("file:Cyber_Gaming.ttf#Cyber Gaming", "Cyber Gaming", "Wide, techy gamer lettering"),
        ("file:ForbiddenLove.otf#Forbidden Love", "Forbidden Love", "Thin, tall and elegant"),
        ("file:Sixrow-Thin.ttf#Sixrow#Sixrow Thin", "Sixrow", "Thin, square capitals like a dot-matrix display"),
        ("file:Tindamare.ttf#Tindamare", "Tindamare", "A clean, round geometric sans"),
        ("file:BarlowCondensed-SemiBold.ttf#Barlow Condensed SemiBold#Barlow Condensed", "Barlow Condensed", "Tall, condensed and sporty"),
        ("file:AfacadFlux-Regular.ttf#Afacad Flux", "Afacad Flux", "Friendly, rounded and easy to read"),
        ("file:Windows2000XP.otf#windows 2000 and xp", "Windows 2000", "The crisp system type of old Windows"),
        ("file:WindowsXPLike.otf#WindowsXPlike A", "Windows XP", "Friendly early-2000s desktop type"),
        ("file:OpenDyslexic-Regular.otf#OpenDyslexic", "OpenDyslexic", "Weighted letters that some people with dyslexia find easier to read"),
        ("file:Ubuntu-Regular.ttf#Ubuntu", "Ubuntu", "Ubuntu Linux's friendly, rounded sans-serif"),
        ("file:Marcellus-Regular.ttf#Marcellus", "Marcellus", "Classic carved Roman letters with a gentle flare"),
        ("file:Philosopher-Regular.ttf#Philosopher", "Philosopher", "Flowing Art Nouveau letters"),
        ("file:Play-Regular.ttf#Play", "Play", "Rounded and techy"),
        ("file:CrimsonText-Regular.ttf#Crimson Text", "Crimson Text", "A bookish old-style serif"),
        ("file:VarelaRound-Regular.ttf#Varela Round", "Varela Round", "Soft and round"),
        ("file:Spectral-Regular.ttf#Spectral", "Spectral", "An elegant, quiet serif"),
        ("file:SairaCondensed-Regular.ttf#Saira Condensed", "Saira Condensed", "Tall, narrow and strong"),
        ("file:DidactGothic-Regular.ttf#Didact Gothic", "Didact Gothic", "Clean and open, easy to read"),
        ("file:IMFeENrm28P.ttf#IM FELL English", "IM FELL English", "Old printed letters from the 1600s"),
        ("file:Sniglet-Regular.ttf#Sniglet", "Sniglet", "Chunky and playful"),
        ("file:PoiretOne-Regular.ttf#Poiret One", "Poiret One", "Thin Art Deco lines"),
        ("file:Cinzel-Regular.ttf#Cinzel", "Cinzel", "Stately capitals carved like an old inscription"),
        ("file:Comfortaa-Regular.ttf#Comfortaa", "Comfortaa", "Round and soft, built from circles"),
        ("file:ShareTechMono-Regular.ttf#Share Tech Mono", "Share Tech Mono", "A technical monospace for drawings and readouts"),
        ("file:JosefinSans-Regular.ttf#Josefin Sans", "Josefin Sans", "Geometric and elegant, from the 1920s"),
        ("file:TenorSans-Regular.ttf#Tenor Sans", "Tenor Sans", "Calm, open and classical"),
        ("file:ElMessiri-Regular.ttf#El Messiri", "El Messiri", "Gently curved, with an Arabic calligraphic air"),
        ("file:ChakraPetch-Regular.ttf#Chakra Petch", "Chakra Petch", "Squared-off and techy"),
        ("file:Almendra-Regular.ttf#Almendra", "Almendra", "A calligraphic book hand with a medieval feel"),
        ("file:Merienda-Regular.ttf#Merienda", "Merienda", "Warm, flowing casual script"),
        ("file:Bangers-Regular.ttf#Bangers", "Bangers", "Loud comic-book capitals"),
        ("file:VT323-Regular.ttf#VT323", "VT323", "An old computer terminal's pixel type"),
        ("file:Jersey10-Regular.ttf#Jersey 10", "Jersey 10", "Tall, condensed pixel type like a team jersey"),
        ("file:PixelOperator16-Regular.ttf#Pixel Operator", "Pixel Operator", "A tidy, readable pixel font"),
        ("file:orange_juice_2.0.ttf#orange juice", "Orange Juice", "Bold, bubbly hand lettering"),
        ("file:Aurely_Roliste_Demo.ttf#Aurely Roliste Demo", "Aurely Roliste", "Bold brush script (this demo has no numbers)"),
        ("file:Marching_Brush.otf#Marching Brush", "Marching Brush", "Rough brushed capitals (this demo's numbers are scribbles)"),
        ("file:Foliager_Warmth.otf#Foliager Warmth", "Foliager Warmth", "Curly, warm handwriting (demo: numbers show a logo)"),
        ("file:Frostpop.otf#Frostpop", "Frostpop", "Tall, narrow and playful (demo: numbers show a logo)"),
        ("file:Jhendela.otf#Jhendela", "Jhendela", "Bold, flowing marker script (demo: numbers show a logo)"),
        ("file:Prixma-Regular.otf#Prixma", "Prixma", "Light and modern (numbers are pictures in this version)"),
    };

    /// <summary>A list of choices for a setting; the chosen id is stored and applied straight away.</summary>
    private async void Pick(string title, (string Id, string Name, string About)[] options, Action<string> store)
    {
        // Just the names: a line of description for each made long lists too tall to use.
        int choice = await Host.AskAsync(title, null, options.Select(o => o.Name).ToArray());
        if (choice < 0) return;
        Toggle(() => store(options[choice].Id));
    }

    private void Toggle(Action change)
    {
        change();
        Host.Settings.Save();
        Host.ApplySettings();
        Rebuild();
    }

    private async void ToggleDesktop()
    {
        try
        {
            if (Startup.ReplacesDesktop)
            {
                Startup.ReplacesDesktop = false;
                Rebuild();
                await Host.AskAsync("Windows desktop restored",
                    "The normal desktop will load again the next time you sign in.", "OK");
                return;
            }

            int choice = await Host.AskAsync("Replace the Windows desktop?",
                "After you next sign in, Windows opens this launcher instead of the desktop: no taskbar or Start menu " +
                "until you choose Power \u2192 Exit SquareDeck. Programs that normally start with Windows will not start " +
                "until then. If the launcher ever fails to open, press Ctrl+Shift+Esc, choose Run new task and type " +
                "explorer.exe. Only your user account is affected.",
                "Replace the desktop", "Cancel");
            if (choice != 0) return;
            Startup.ReplacesDesktop = true;
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't change the setting", ex.Message, "OK");
        }
        Rebuild();
    }

    private static string ThemeLabel(string? mode) => mode switch
    {
        "dark" => "Dark",
        "oled" => "OLED (black)",
        "light" => "Light",
        _ => "Follow system (" + (Theme.SystemIsDark() ? "dark" : "light") + ")"
    };

    private static string AccentLabel(string hex)
    {
        if (hex == "system") return "Follow Windows";
        if (!Theme.TryParseColor(hex, out _)) return "Default";
        var known = Theme.AccentChoices.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));
        return known.Name ?? "Custom (" + hex.ToUpperInvariant() + ")";
    }


    private static string Mask(string key) => key.Length <= 4 ? "****" : "•••• " + key[^4..];

    private async void Rescan()
    {
        await Host.ReloadAsync(waitForArt: false);
        await Host.AskAsync("Library rescanned", $"Found {Host.GameCount} installed Steam games.", "OK");
    }

    private async void Redownload()
    {
        int choice = await Host.AskAsync("Re-download cover art?",
            "All downloaded covers are fetched again from SteamGridDB. Covers you picked yourself are kept.",
            "Re-download", "Cancel");
        if (choice != 0) return;
        _ = Host.RedownloadArtAsync();
        await Host.AskAsync("Downloading covers", "New covers will appear on the home screen as they arrive.", "OK");
    }

    private async void EditApiKey()
    {
        var key = await Host.PromptAsync("SteamGridDB API key",
            "Find your key at steamgriddb.com under Preferences → API.", Host.Settings.SteamGridDbApiKey);
        if (string.IsNullOrWhiteSpace(key) || key.Trim() == Host.Settings.SteamGridDbApiKey) return;
        Host.Settings.SteamGridDbApiKey = key.Trim();
        Host.Settings.Save();
        Rebuild();
        _ = Host.ReloadAsync();
    }
}

/// <summary>Color of the selection ring and the highlighted game's title.</summary>
public sealed class AccentScreen : GridScreen
{
    public AccentScreen() : base("Highlight color")
    {
        AddTile(GlyphBox(120, 120, "\uE777"), "Default", 120, () => Pick(""));

        // Highlight effects: the ring in your own colors, moving.
        var effects = new System.Windows.Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (hex, at) in new[] { ("#FF2BD6", 0.0), ("#7A2BFF", 0.5), ("#22D3EE", 1.0) })
            effects.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex), at));
        var effectsTile = new Border
        {
            Width = 120, Height = 120, BorderThickness = new Thickness(6), BorderBrush = effects, CornerRadius = new CornerRadius(14),
            Child = Ui.Glyph("\uE945", 40, "FgBrush")
        };
        ((TextBlock)effectsTile.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)effectsTile.Child).VerticalAlignment = VerticalAlignment.Center;
        AddTile(effectsTile, HighlightEffects.Active ? "Effects (on)" : "Effects\u2026", 120, () => Host.Push(new HighlightEffectsScreen()));

        // Whatever accent color is set in Windows' own Personalization settings.
        var system = Theme.SystemAccent();
        var windows = new Border
        {
            Width = 120, Height = 120, BorderThickness = new Thickness(1),
            Background = system is System.Windows.Media.Color c ? new System.Windows.Media.SolidColorBrush(c) : null,
            Child = Ui.Glyph("\uE770", 40, "BgBrush")
        };
        windows.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
        ((TextBlock)windows.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)windows.Child).VerticalAlignment = VerticalAlignment.Center;
        AddTile(windows, "Follow Windows", 120, () => Pick("system"));

        foreach (var (name, hex) in Theme.AccentChoices)
        {
            Theme.TryParseColor(hex, out var color);
            var swatch = new Border
            {
                Width = 120,
                Height = 120,
                Background = new System.Windows.Media.SolidColorBrush(color),
                BorderThickness = new Thickness(1)
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "SubtleBrush");
            AddTile(swatch, name, 120, () => Pick(hex));
        }

        // A rainbow tile for "any color you like".
        var rainbow = new System.Windows.Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var stops = new[] { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#00C7BE", "#007AFF", "#AF52DE" };
        for (int i = 0; i < stops.Length; i++)
            rainbow.GradientStops.Add(new System.Windows.Media.GradientStop(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(stops[i]), i / 6.0));
        AddTile(new Border { Width = 120, Height = 120, Background = rainbow }, "Custom\u2026", 120, Custom);
    }

    private void Pick(string hex)
    {
        Host.Settings.AccentColor = hex;
        Host.Settings.Save();
        Theme.Apply(Host.Settings);
        Host.Pop(this);
    }

    private void Custom()
    {
        // Start the picker on the color in use now.
        var start = (Application.Current.Resources["AccentBrush"] as System.Windows.Media.SolidColorBrush)?.Color
                    ?? System.Windows.Media.Color.FromRgb(0x20, 0xE0, 0xD0);
        Host.Push(new ColorPickerScreen(start, Pick)); // Pick closes this page, and the picker with it
    }
}

/// <summary>
/// Highlight effects: the selection ring drawn in your own colors, still or moving (spin, flow, stripes, shimmer,
/// comet, color cycle, breathe, heartbeat, neon flicker), with speed and glow.
/// </summary>
public sealed class HighlightEffectsScreen : ListScreen
{
    public HighlightEffectsScreen() : base("Highlight effects") { }

    private static System.Windows.Media.Color Accent =>
        (Application.Current.Resources["AccentBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Color.FromRgb(0x20, 0xE0, 0xD0);

    protected override void Build()
    {
        var s = Host.Settings;
        string effect = HighlightEffects.Effect;
        AddPreview(effect);

        AddRow("Effect", HighlightEffects.All.FirstOrDefault(e => e.Id == effect).Name ?? "None", "\uE945",
            click: () => Host.Push(new HighlightChoiceScreen("Effect", HighlightEffects.All, effect, id => { s.HighlightEffect = id; Apply(); })),
            subtitle: HighlightEffects.All.FirstOrDefault(e => e.Id == effect).About);
        if (effect.Length == 0)
        {
            AddNote("Choose an effect to draw the highlight in your own colors, standing still or moving.");
            return;
        }

        AddHeader("Colors");
        string palette = HighlightEffects.PaletteId;
        var sets = HighlightEffects.Palettes.Select(p => (p.Id, p.Name, p.Id == "accent" ? "Shades of the highlight color, so it follows the color you pick" : string.Join("  ", p.Colors))).ToArray();
        AddRow("Color set", palette.Length == 0 ? "Your own" : HighlightEffects.Palettes.FirstOrDefault(p => p.Id == palette).Name ?? "Highlight color", "\uE790",
            click: () => Host.Push(new HighlightChoiceScreen("Color set", sets, palette, id => { s.HighlightColors = "palette:" + id; Apply(); })),
            subtitle: "A ready-made set. Changing any color below makes it your own");
        var hexes = HighlightEffects.Hexes(Accent);
        for (int i = 0; i < hexes.Count; i++)
        {
            int index = i;
            AddRow($"Color {i + 1}", hexes[i], icon: Swatch(hexes[i]), click: () => EditColor(hexes, index));
        }
        if (hexes.Count < 8)
            AddRow("Add a color", null, "\uE710", click: () => { hexes.Add(hexes[^1]); EditColor(hexes, hexes.Count - 1); });
        if (hexes.Count > 2)
            AddRow("Remove the last color", null, "\uE738", click: () => { hexes.RemoveAt(hexes.Count - 1); Save(hexes); });
        AddRow("Reverse the colors", null, "\uE8AB", click: () => { hexes.Reverse(); Save(hexes); });

        AddHeader("Movement and glow");
        AddRow("Speed", HighlightEffects.Speeds.First(x => x.Id == HighlightEffects.Speed).Name, "\uEC4A",
            click: () => _ = PickAsync("Speed", HighlightEffects.Speeds, v => s.HighlightSpeed = v));
        AddRow("Glow", HighlightEffects.Glows.First(x => x.Id == HighlightEffects.Glow).Name, "\uE706",
            click: () => _ = PickAsync("Glow", HighlightEffects.Glows, v => s.HighlightGlow = v),
            subtitle: "A soft light around the highlight, in its first color");
        AddNote(Theme.ReduceMotion || Perf.Active
            ? "Reduce motion or Performance mode is on, so the colors are shown standing still."
            : "Shown on the highlighted game on Home and on every highlighted row and button. A high-contrast theme uses its own highlight instead.");
    }

    /// <summary>A sample ring with the effect, its glow and the word Preview.</summary>
    private void AddPreview(string effect)
    {
        var ring = new Border
        {
            Width = 520, Height = 110, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(5), Margin = new Thickness(0, 6, 0, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderBrush = effect.Length > 0 ? HighlightEffects.Build(effect, Accent) : new System.Windows.Media.SolidColorBrush(Accent),
            Child = Ui.Text("Preview", 22, "FgBrush", bold: true)
        };
        ((TextBlock)ring.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)ring.Child).VerticalAlignment = VerticalAlignment.Center;
        if (HighlightEffects.GlowFor(Accent) is (true, var glow)) ring.Effect = glow;
        else ring.SetResourceReference(UIElement.EffectProperty, "RingGlow");
        Rows.Children.Add(ring);
    }

    private static Image Swatch(string hex)
    {
        Theme.TryParseColor(hex, out var color);
        var drawing = new System.Windows.Media.GeometryDrawing(new System.Windows.Media.SolidColorBrush(color),
            new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1.5), new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 28, 28), 7, 7));
        return new Image { Source = new System.Windows.Media.DrawingImage(drawing), Width = 28, Height = 28 };
    }

    private void EditColor(List<string> hexes, int index)
    {
        Theme.TryParseColor(hexes[index], out var start);
        ColorPickerScreen? picker = null;
        picker = new ColorPickerScreen(start, hex =>
        {
            hexes[index] = hex;
            if (picker != null) Host.Pop(picker);
            Save(hexes);
        });
        Host.Push(picker);
    }

    private void Save(List<string> hexes)
    {
        Host.Settings.HighlightColors = string.Join(",", hexes);
        Apply();
    }

    private async Task PickAsync(string title, (string Id, string Name)[] options, Action<string> set)
    {
        int choice = await Host.AskAsync(title, null, options.Select(o => o.Name).ToArray());
        if (choice < 0) return;
        set(options[choice].Id);
        Apply();
    }

    private void Apply()
    {
        var s = Host.Settings;
        s.Save();
        HighlightEffects.Load(s);
        UiStyles.ApplyResources();
        UiStyles.RefreshRing(gradient: true);
        Rebuild();
    }
}

/// <summary>A list to choose one thing from, each with a line about it; choosing goes back.</summary>
public sealed class HighlightChoiceScreen : ListScreen
{
    private readonly (string Id, string Name, string About)[] _items;
    private readonly string _current;
    private readonly Action<string> _chosen;

    public HighlightChoiceScreen(string title, (string Id, string Name, string About)[] items, string current, Action<string> chosen) : base(title)
    {
        _items = items; _current = current; _chosen = chosen;
    }

    protected override void Build()
    {
        foreach (var (id, name, about) in _items)
            AddRow(name, id == _current ? "In use" : null, id == _current ? "\uE73E" : "", subtitle: about, click: () =>
            {
                Host.Pop(this);
                _chosen(id);
            });
    }
}

public sealed class AvatarScreen : GridScreen
{
    public AvatarScreen() : base("Profile picture")
    {
        // Your own first: a picture from this PC, or your Steam avatar.
        AddTile(GlyphBox(104, 104, "\uE8B9"), "Choose image…", 104, Browse);
        if (MainWindow.FindSteamAvatarFile() is string steam && ImageLoader.FromFile(steam, 240) is { } picture)
        {
            var face = new System.Windows.Shapes.Ellipse
            {
                Width = 104, Height = 104,
                Fill = new System.Windows.Media.ImageBrush(picture) { Stretch = System.Windows.Media.Stretch.UniformToFill }
            };
            AddTile(face, "Steam avatar", 104, () => Import(steam));
        }
        // Then the built-in pictures, a row per group.
        foreach (var (group, from, count) in Avatars.Groups)
        {
            var heading = Ui.Text(group, 18, "FgBrush", bold: true);
            heading.Margin = new Thickness(4, 14, 0, 10);
            Body.Children.Add(heading);
            var row = new WrapPanel();
            Body.Children.Add(row);
            for (int i = from; i < from + count && i < Avatars.Presets.Length; i++)
            {
                string setting = "preset:" + i;
                var tile = AddTile(Avatars.Build(setting, 104), null, 104, () => Pick(setting), into: row);
            }
        }

    }

    /// <summary>Copies a picture into the launcher's folder and uses it.</summary>
    private void Import(string file)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            foreach (var old in Directory.EnumerateFiles(Settings.DataDir, "avatar_*"))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            var dest = Path.Combine(Settings.DataDir, "avatar_" + DateTime.Now.Ticks + Path.GetExtension(file));
            File.Copy(file, dest, overwrite: true);
            Pick(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }

    private void Pick(string setting)
    {
        Host.Settings.Avatar = setting;
        Host.Settings.Save();
        Host.ApplyAvatar();
        Host.Pop(this);
    }

    private async void Browse()
    {
        var picked = await Host.PickFileAsync("Choose a profile picture", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" });
        if (picked == null) return;
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            // New file name each time so the old picture is never served from a cache.
            foreach (var old in Directory.EnumerateFiles(Settings.DataDir, "avatar_*"))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            var dest = Path.Combine(Settings.DataDir,
                "avatar_" + DateTime.Now.Ticks + Path.GetExtension(picked));
            File.Copy(picked, dest, overwrite: true);
            Pick(dest);
        }
        catch (Exception ex)
        {
            _ = Host.AskAsync("Couldn't use that image", ex.Message, "OK");
        }
    }
}

public sealed class WifiScreen : ListScreen
{
    private bool? _on;
    private bool _busy = true;
    private string? _error;
    private List<WifiNetwork> _networks = new();

    public WifiScreen() : base("Wi-Fi") { }

    public override void OnShown()
    {
        Rebuild();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        Rebuild();
        _on = await NetworkService.GetRadioStateAsync(RadioKind.WiFi);
        if (_on == true) (_networks, _error) = await NetworkService.ScanWifiAsync();
        else { _networks = new(); _error = null; }
        _busy = false;
        Rebuild();
    }

    protected override void Build()
    {
        if (_on == null && !_busy)
        {
            AddNote("No Wi-Fi adapter was found on this PC.");
        }
        else
        {
            AddRow("Wi-Fi", _on == true ? "On" : _on == false ? "Off" : "…", "", click: Toggle);
            if (_on == true) AddRow("Scan again", glyph: "", click: () => _ = RefreshAsync());
        }
        AddRow("Open Windows Wi-Fi settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:network-wifi"));

        if (_busy) { AddNote("Looking for networks…"); return; }
        if (_error != null) { AddNote(_error); return; }
        if (_on != true) return;

        AddHeader("Networks");
        if (_networks.Count == 0) AddNote("No networks found.");
        foreach (var n in _networks)
        {
            var net = n;
            string bars = new string('●', Math.Clamp(n.Bars, 1, 4)) + new string('○', 4 - Math.Clamp(n.Bars, 1, 4));
            string value = n.Connected ? "Connected" : (n.Secured ? "Secured   " : "Open   ") + bars;
            AddRow(n.Ssid, value, n.Secured ? "" : "", click: () => Select(net));
        }
    }

    private async void Toggle()
    {
        if (_on == null) return;
        bool target = _on != true;
        if (!await NetworkService.SetRadioAsync(RadioKind.WiFi, target))
            await Host.AskAsync("Couldn't change Wi-Fi", "Windows did not allow this app to switch the radio.", "OK");
        await RefreshAsync();
    }

    private async void Select(WifiNetwork network)
    {
        if (network.Connected)
        {
            if (await Host.AskAsync(network.Ssid, "You are connected to this network.", "Disconnect", "Cancel") == 0)
            {
                await NetworkService.DisconnectWifiAsync();
                await RefreshAsync();
            }
            return;
        }

        string? password = null;
        if (network.Secured)
        {
            password = await Host.PromptAsync("Connect to " + network.Ssid,
                "Enter the network password. Leave it empty to use a password Windows already saved.", "", "Connect");
            if (password == null) return;
        }

        _busy = true;
        Rebuild();
        var error = await NetworkService.ConnectWifiAsync(network, password);
        if (error != null) await Host.AskAsync("Couldn't connect", error, "OK");
        await RefreshAsync();
    }
}

public sealed class BluetoothScreen : ListScreen
{
    private bool? _on;
    private bool _busy = true;
    private bool _searching;
    private List<BluetoothItem> _paired = new();
    private List<BluetoothItem>? _nearby;

    public BluetoothScreen() : base("Bluetooth") { }

    public override void OnShown()
    {
        Rebuild();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        Rebuild();
        _on = await NetworkService.GetRadioStateAsync(RadioKind.Bluetooth);
        _paired = _on == true ? await NetworkService.ListBluetoothAsync(paired: true) : new();
        _busy = false;
        Rebuild();
    }

    protected override void Build()
    {
        if (_on == null && !_busy)
            AddNote("No Bluetooth adapter was found on this PC.");
        else
            AddRow("Bluetooth", _on == true ? "On" : _on == false ? "Off" : "…", "", click: Toggle);
        AddRow("Open Windows Bluetooth settings", glyph: "", click: () => ProcessUtil.Open("ms-settings:bluetooth"));

        if (_busy) { AddNote("Checking Bluetooth…"); return; }
        if (_on != true) return;

        AddHeader("Paired devices");
        if (_paired.Count == 0) AddNote("Nothing paired yet.");
        foreach (var d in _paired)
        {
            var device = d;
            AddRow(d.Name, d.Connected ? "Connected" : "Paired", "", click: () => SelectPaired(device));
        }

        AddHeader("Add a device");
        AddRow(_searching ? "Searching…" : "Search for nearby devices", glyph: "", click: Search);
        if (_nearby != null)
        {
            if (_nearby.Count == 0 && !_searching)
                AddNote("No devices found. Put the device in pairing mode and search again.");
            foreach (var d in _nearby)
            {
                var device = d;
                AddRow(d.Name, "Pair", "", click: () => Pair(device));
            }
        }
    }

    private async void Toggle()
    {
        if (_on == null) return;
        bool target = _on != true;
        if (!await NetworkService.SetRadioAsync(RadioKind.Bluetooth, target))
            await Host.AskAsync("Couldn't change Bluetooth", "Windows did not allow this app to switch the radio.", "OK");
        _nearby = null;
        await RefreshAsync();
    }

    private async void Search()
    {
        if (_searching) return;
        _searching = true;
        _nearby ??= new();
        Rebuild();
        _nearby = await NetworkService.ListBluetoothAsync(paired: false);
        _searching = false;
        Rebuild();
    }

    private async void Pair(BluetoothItem device)
    {
        _busy = true;
        Rebuild();
        var error = await NetworkService.PairAsync(device.Id);
        if (error != null) await Host.AskAsync("Couldn't pair " + device.Name, error, "OK");
        _nearby = null;
        await RefreshAsync();
    }

    private async void SelectPaired(BluetoothItem device)
    {
        if (await Host.AskAsync(device.Name, device.Connected ? "Connected" : "Paired", "Remove device", "Cancel") != 0)
            return;
        var error = await NetworkService.UnpairAsync(device.Id);
        if (error != null) await Host.AskAsync("Couldn't remove " + device.Name, error, "OK");
        await RefreshAsync();
    }
}

/// <summary>
/// Quick settings: a panel that slides in from the left (View button, Q, or the top-bar icon). The time and battery
/// at the top, a grid of tiles that light up in the highlight color when on (Wi-Fi, Bluetooth, airplane mode, night
/// light, mute, settings), sliders for volume, brightness and TDP, and the power menu at the bottom.
/// It follows the UI style's colors and corners.
/// </summary>
public sealed class QuickSettingsScreen : Screen
{
    private readonly Slider _volume, _brightness;
    private readonly TextBlock _volumeText, _brightnessText, _clock, _date, _battery, _batteryGlyph;
    private readonly Border _brightnessRow;
    private readonly System.Windows.Threading.DispatcherTimer _brightnessApply = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly System.Windows.Threading.DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly QuickTile _wifiTile, _bluetoothTile, _airplaneTile, _nightTile, _muteTile;
    private readonly Border _sheet;
    private readonly string _side = "center";
    private bool? _wifi, _bluetooth;
    private bool _syncing;           // true while we set a slider ourselves, so that isn't treated as the user
    private bool _brightnessReady;

    public override bool IsDialog => true;

    /// <summary>Corners that suit the UI style: round, or nearly square for the sharp-cornered styles.</summary>
    internal static double Round(double usual) => Theme.TileCorner <= 4 ? Math.Min(usual, Theme.TileCorner + 2) : usual;

    public QuickSettingsScreen()
    {
        RumbleOnMove();
        var panel = new StackPanel();

        // ---- the time, the date, and the battery
        var top = new DockPanel { Margin = new Thickness(4, 0, 4, 18) };
        var batteryRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _batteryGlyph = Ui.Glyph("", 20, "FgBrush");
        _batteryGlyph.Margin = new Thickness(0, 0, 8, 0);
        _batteryGlyph.VerticalAlignment = VerticalAlignment.Center;
        _battery = Ui.Text("", 18, "FgBrush", bold: true);
        _battery.VerticalAlignment = VerticalAlignment.Center;
        batteryRow.Children.Add(_batteryGlyph);
        batteryRow.Children.Add(_battery);
        DockPanel.SetDock(batteryRow, Dock.Right);
        top.Children.Add(batteryRow);
        var when = new StackPanel();
        _clock = Ui.Text("", 40, "FgBrush", bold: true);
        _date = Ui.Text("", 15, "SubtleBrush");
        when.Children.Add(_clock);
        when.Children.Add(_date);
        top.Children.Add(when);
        panel.Children.Add(top);

        // ---- toggles
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
        _wifiTile = new QuickTile("", "Wi-Fi");
        _wifiTile.Click += async (_, _) => { if (_wifi != null) await SetRadiosAsync(!_wifi.Value, null); };
        _wifiTile.Hold += () => Host.Push(new WifiScreen());
        _bluetoothTile = new QuickTile("", "Bluetooth");
        _bluetoothTile.Click += async (_, _) => { if (_bluetooth != null) await SetRadiosAsync(null, !_bluetooth.Value); };
        _bluetoothTile.Hold += () => Host.Push(new BluetoothScreen());
        _airplaneTile = new QuickTile("", "Airplane");
        _airplaneTile.Click += async (_, _) =>
        {
            // Windows has no public switch for its own airplane mode; this turns both radios off (or back on).
            bool anyOn = _wifi == true || _bluetooth == true;
            await SetRadiosAsync(!anyOn, !anyOn);
        };
        _nightTile = new QuickTile("", "Night light");
        _nightTile.Click += async (_, _) =>
        {
            if (NightLight.IsOn is bool on && NightLight.Set(!on))
            {
                await Task.Delay(300);
                ShowNight();
            }
            else ProcessUtil.Open("ms-settings:nightlight");
        };
        _muteTile = new QuickTile("", "Mute");
        _muteTile.Click += (_, _) => { Audio.SetMute(!Audio.GetMute()); ShowAudio(); };
        var settings = new QuickTile("", "Settings");
        settings.Set(false, "All settings");
        settings.Click += (_, _) => { Host.Pop(this); Host.Push(new SettingsScreen()); };
        foreach (var t in new[] { _wifiTile, _bluetoothTile, _airplaneTile, _nightTile, _muteTile, settings }) grid.Children.Add(t);
        panel.Children.Add(grid);

        // ---- sliders
        var sliders = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var volumeRow = Ui.SliderRow("Volume", "", out _volume, out _volumeText);
        sliders.Children.Add(Slim(volumeRow));
        _brightnessRow = Slim(Ui.SliderRow("Brightness", "", out _brightness, out _brightnessText));
        _brightnessRow.Visibility = Visibility.Collapsed; // shown once we know the screen supports it
        sliders.Children.Add(_brightnessRow);
        // The last known level shows at once; LoadAsync corrects it once the current level is read.
        if (Brightness.Last is int lastLevel && Brightness.Supported != false)
        {
            _syncing = true;
            _brightness.Value = lastLevel;
            _syncing = false;
            _brightnessRow.Visibility = Visibility.Visible;
            _brightnessReady = true;
        }

        // TDP, when TDP control is set up and on: for now only (until the game closes, or the launcher restarts).
        if (TdpControl.Ready && TdpControl.Chip is { } chip)
        {
            var tdpRow = Ui.SliderRow("TDP", "", out var tdp, out var tdpText);
            tdp.Minimum = chip.Min;
            tdp.Maximum = chip.Max;
            tdp.SmallChange = 1;
            tdp.LargeChange = 5;
            tdp.IsSnapToTickEnabled = true;
            tdp.TickFrequency = 1;
            tdp.Value = TdpControl.EffectiveWatts;
            tdpText.Text = TdpControl.EffectiveWatts + " W";
            tdp.ValueChanged += (_, e) =>
            {
                int w = (int)Math.Round(e.NewValue);
                tdpText.Text = w + " W";
                TdpControl.SetSessionWatts(w);
            };
            sliders.Children.Add(Slim(tdpRow));
        }
        panel.Children.Add(sliders);

        // ---- power, along the bottom
        var power = new Button { Style = Ui.StyleOf("PanelButton"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 16, 0, 0) };
        var powerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        powerRow.Children.Add(new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
        powerRow.Children.Add(new TextBlock { Text = "Power", VerticalAlignment = VerticalAlignment.Center });
        power.Content = powerRow;
        power.Click += (_, _) => { Host.Pop(this); _ = Host.PowerMenuAsync(); };
        panel.Children.Add(power);

        _volume.ValueChanged += (_, e) =>
        {
            _volumeText.Text = (int)e.NewValue + "%";
            if (!_syncing) Audio.SetVolume((int)e.NewValue);
        };
        _brightness.ValueChanged += (_, e) =>
        {
            _brightnessText.Text = (int)e.NewValue + "%";
            if (_syncing || !_brightnessReady) return;
            // Setting brightness is slow, so wait until the slider stops moving.
            _brightnessApply.Stop();
            _brightnessApply.Start();
        };
        _brightnessApply.Tick += async (_, _) =>
        {
            _brightnessApply.Stop();
            await Brightness.SetAsync((int)_brightness.Value);
        };
        _tick.Tick += (_, _) => ShowClock();

        // A sheet on the left, over the dimmed screen.
        _sheet = new Border
        {
            Width = 470, Padding = new Thickness(26, 24, 26, 24), Margin = new Thickness(18, 18, 0, 18),
            CornerRadius = new CornerRadius(Round(24)),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = panel, RenderTransform = new System.Windows.Media.TranslateTransform(),
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.45 }
        };
        _sheet.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        _sheet.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _sheet.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        // Where the sheet sits and how it's drawn follow the look: a drawer from the left (ubiquity), a panel down the
        // right like a console's guide (SquareDeck, X Dash), a sheet along the bottom (Orbit), or floating (the rest).
        _side = LookChrome.SheetSide(power: false);
        switch (_side)
        {
            case "bottom":
                break;
            default:
                // A full-height blade the same size as the Wi-Fi / Bluetooth blade on the home screen (520 wide), on
                // the side the look puts it (floating looks: the left).
                if (_side is not ("left" or "right")) _side = "left";
                _sheet.Width = 520;
                _sheet.Margin = new Thickness(0);
                _sheet.VerticalAlignment = VerticalAlignment.Stretch;
                _sheet.HorizontalAlignment = _side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                _sheet.Padding = new Thickness(30, 34, 30, 26);
                _sheet.CornerRadius = _side == "left" ? new CornerRadius(0, Round(26), Round(26), 0) : new CornerRadius(Round(26), 0, 0, Round(26));
                break;
        }
        switch (_side)
        {
            case "left":
            case "right":
                break;
            case "bottom":
                _sheet.Margin = new Thickness(0);
                _sheet.Width = 620;
                _sheet.HorizontalAlignment = HorizontalAlignment.Center;
                _sheet.VerticalAlignment = VerticalAlignment.Bottom;
                _sheet.Padding = new Thickness(36, 30, 36, 30);
                break;
        }
        LookChrome.StyleSheet(_sheet);
        SkinSheet(_sheet);
        var backdrop = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x70, 0, 0, 0)) };
        backdrop.Children.Add(_sheet);
        Content = backdrop;
    }

    /// <summary>The slider rows, a little tighter and rounder than on the settings pages.</summary>
    private static Border Slim(Border row)
    {
        row.CornerRadius = new CornerRadius(Round(16));
        row.Padding = new Thickness(14, 8, 14, 8);
        row.Margin = new Thickness(0, 4, 0, 4);
        if (row.Child is Grid g && g.ColumnDefinitions.Count > 1) g.ColumnDefinitions[1].Width = new GridLength(112);
        LookChrome.PaintFace(row, Array.Empty<TextBlock>(), on: false, focused: false);
        if (Ui.SkinBrush(row, "panel") is { } art) { row.Background = art; row.CornerRadius = new CornerRadius(0); } // an Advanced style's row artwork (it draws its own corners)
        return row;
    }

    /// <summary>
    /// An Advanced style's pop-up artwork behind a sheet or card (quick settings, the power menu), with room under
    /// a title bar when the artwork has one. Basic styles keep their plain background.
    /// </summary>
    internal static void SkinSheet(Border sheet)
    {
        if (Ui.SkinBrush(sheet, "dialog") is not { } art) return;
        sheet.Background = art;
        sheet.CornerRadius = new CornerRadius(0);
        sheet.BorderThickness = new Thickness(0);
        if (Skins.Current is { DialogPad: > 0 } titled)
            sheet.Padding = new Thickness(sheet.Padding.Left, sheet.Padding.Top + titled.DialogPad, sheet.Padding.Right, sheet.Padding.Bottom);
    }

    public override void OnShown()
    {
        ShowClock();
        ShowAudio();
        ShowNight();
        _tick.Start();
        _ = LoadAsync();
        if (!Theme.ReduceMotion && _sheet.RenderTransform is System.Windows.Media.TranslateTransform slide)
        {
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            bool edge = _side is "left" or "right" or "bottom";
            double from = _side switch { "left" => -(_sheet.Width + 20), "right" => _sheet.Width + 20, "bottom" => 420, _ => -80 };
            slide.BeginAnimation(_side == "bottom" ? System.Windows.Media.TranslateTransform.YProperty : System.Windows.Media.TranslateTransform.XProperty,
                new System.Windows.Media.Animation.DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(edge ? 260 : 220)) { EasingFunction = ease });
            _sheet.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    public override void OnResumed()
    {
        _ = ShowRadiosAsync(); // back from the Wi-Fi or Bluetooth page
    }

    public override void OnClosed()
    {
        _brightnessApply.Stop();
        _tick.Stop();
    }

    public override void FocusDefault() => _wifiTile.Focus();

    private void ShowClock()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString("t");
        _date.Text = now.ToString("dddd, MMMM d");
        var battery = Battery.GetPercent();
        _battery.Text = battery == null ? "" : battery + "%";
        _batteryGlyph.Visibility = battery == null ? Visibility.Collapsed : Visibility.Visible;
        // Battery glyphs, from empty (E850) to nine tenths (E859), and full (E83F).
        if (battery is int b) _batteryGlyph.Text = b >= 100 ? "\uE83F" : ((char)(0xE850 + Math.Clamp(b / 10, 0, 9))).ToString();
        if (battery is <= 15) _battery.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
    }

    private void ShowNight()
    {
        bool? on = NightLight.IsOn;
        _nightTile.Set(on == true, on switch { true => "On", false => "Off", _ => "Settings" });
    }

    private void ShowAudio()
    {
        _syncing = true;
        var volume = Audio.GetVolume();
        _volume.IsEnabled = volume != null;
        _volume.Value = volume ?? 0;
        _volumeText.Text = volume == null ? "—" : volume + "%";
        bool muted = Audio.GetMute();
        _muteTile.Set(muted, muted ? "Sound off" : "Sound on");
        _syncing = false;
    }

    private async Task LoadAsync()
    {
        // Brightness first: the slider is what you see; the radios take longer to read.
        var brightness = await Brightness.GetAsync();
        if (brightness != null)
        {
            _syncing = true;
            _brightness.Value = brightness.Value;
            _syncing = false;
            _brightnessRow.Visibility = Visibility.Visible;
            _brightnessReady = true;
        }
        else
        {
            _brightnessRow.Visibility = Visibility.Collapsed;
            _brightnessReady = false;
        }
        await ShowRadiosAsync();
    }

    private async Task ShowRadiosAsync()
    {
        _wifi = await NetworkService.GetRadioStateAsync(RadioKind.WiFi);
        _bluetooth = await NetworkService.GetRadioStateAsync(RadioKind.Bluetooth);
        string wifiName = "On";
        if (_wifi == true)
        {
            try { if ((await NetworkService.GetStatusAsync()).WifiBars != null) wifiName = "Connected"; } catch (Exception) { }
        }
        _wifiTile.Set(_wifi == true, _wifi == null ? "Not available" : _wifi.Value ? wifiName : "Off");
        _bluetoothTile.Set(_bluetooth == true, _bluetooth == null ? "Not available" : _bluetooth.Value ? "On" : "Off");
        bool hasRadio = _wifi != null || _bluetooth != null;
        bool airplane = hasRadio && _wifi != true && _bluetooth != true;
        _airplaneTile.Set(airplane, !hasRadio ? "Not available" : airplane ? "On" : "Off");
    }

    private async Task SetRadiosAsync(bool? wifi, bool? bluetooth)
    {
        if (wifi != null && _wifi != null) await NetworkService.SetRadioAsync(RadioKind.WiFi, wifi.Value);
        if (bluetooth != null && _bluetooth != null) await NetworkService.SetRadioAsync(RadioKind.Bluetooth, bluetooth.Value);
        await ShowRadiosAsync();
    }

    public override bool OnAction(NavAction action)
    {
        // Y on a tile: its full page (Wi-Fi networks, Bluetooth devices).
        if (action == NavAction.Y && System.Windows.Input.Keyboard.FocusedElement is QuickTile { } tile) { tile.RaiseHold(); return true; }
        return false;
    }
}

/// <summary>
/// A quick-settings tile: an icon, a name and its state. Filled with the highlight color when on, quiet glass when
/// off, and it grows a little and gets a ring when highlighted.
/// </summary>
public sealed class QuickTile : Button
{
    private readonly Border _face;
    private readonly TextBlock _glyph, _label, _state;
    private readonly System.Windows.Media.ScaleTransform _scale = new(1, 1);
    private bool _on;

    /// <summary>Y on the tile (or a right click): its full settings page.</summary>
    public event Action? Hold;

    private readonly System.Windows.Media.Brush? _art;

    public QuickTile(string glyph, string label)
    {
        Style = Ui.StyleOf("BareButton");
        Focusable = true; // the bare style can't take the highlight by itself; the controller needs it to
        Margin = new Thickness(5);
        FocusVisualStyle = null;
        _glyph = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 24, Margin = new Thickness(0, 0, 0, 10) };
        _label = Ui.Text(label, 16, bold: true);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _state = Ui.Text("", 13);
        _state.TextTrimming = TextTrimming.CharacterEllipsis;
        var stack = new StackPanel();
        stack.Children.Add(_glyph);
        stack.Children.Add(_label);
        stack.Children.Add(_state);
        _face = new Border
        {
            Height = 112, Padding = new Thickness(16, 14, 12, 12), CornerRadius = new CornerRadius(QuickSettingsScreen.Round(18)),
            BorderThickness = new Thickness(3), BorderBrush = System.Windows.Media.Brushes.Transparent, Child = stack,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale
        };
        Content = _face;
        _art = Ui.SkinBrush(_face, "button");
        Paint();
        GotKeyboardFocus += (_, _) => Highlight(true);
        LostKeyboardFocus += (_, _) => Highlight(false);
        MouseRightButtonUp += (_, e) => { e.Handled = true; RaiseHold(); };
    }

    internal void RaiseHold() => Hold?.Invoke();

    public void Set(bool on, string state)
    {
        _on = on;
        _state.Text = state;
        Paint();
    }

    private void Paint()
    {
        if (LookChrome.PaintFace(_face, new[] { _glyph, _label, _state }, _on, IsKeyboardFocused))
        {
            _state.Opacity = 0.8;
            return;
        }
        if (_on) { Ui.SkinCorners(_face, false); _face.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); }
        else Ui.SkinFill(_face, _art, "CircleBrush"); // an Advanced style's button artwork when off
        string fore = _on ? "BgBrush" : "FgBrush";
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, fore);
        _label.SetResourceReference(TextBlock.ForegroundProperty, fore);
        _state.SetResourceReference(TextBlock.ForegroundProperty, _on ? "BgBrush" : "SubtleBrush");
        _state.Opacity = _on ? 0.8 : 1;
    }

    private void Highlight(bool on)
    {
        if (LookChrome.Design.Rows != "card") Paint();
        else if (on) _face.SetResourceReference(Border.BorderBrushProperty, _on ? "FgBrush" : "AccentBrush");
        else _face.BorderBrush = System.Windows.Media.Brushes.Transparent;
        double to = on && !Theme.ReduceMotion ? 1.04 : 1;
        _scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
        _scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
    }
}

/// <summary>Name, serial number, hardware, display and storage of this PC.</summary>
public sealed class DeviceInfoScreen : ListScreen
{
    private DeviceInfo? _info;
    private bool _loaded;

    public DeviceInfoScreen() : base("Device information", "{Back}  Back") { }

    public override void OnShown()
    {
        Rebuild();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _info = await Task.Run(DeviceInfo.LoadAsync);
        _loaded = true;
        Rebuild();
    }

    private void Fact(string label, string? value, string glyph)
    {
        if (!string.IsNullOrWhiteSpace(value)) AddRow(label, value.Trim(), glyph);
    }

    private static string Gigabytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.#} GB";

    protected override void Build()
    {
        AddHeader("Device");
        Fact("Device name", Environment.MachineName, "");
        if (!_loaded) AddNote("Reading hardware details…");
        if (_info != null)
        {
            Fact("Manufacturer", _info.Manufacturer, "");
            Fact("Model", _info.Model, "");
            Fact("Serial number", _info.Serial, "");

            AddHeader("Hardware");
            Fact("Processor", _info.Cpu, "");
            if (_info.Cores > 0) Fact("Cores / threads", $"{_info.Cores} / {_info.Threads}", "");
            if (_info.MemoryBytes > 0) Fact("Memory", Gigabytes(_info.MemoryBytes), "");
            Fact("Graphics", _info.Gpu, "");
        }
        else if (_loaded)
        {
            AddNote("Windows didn't return the hardware details.");
        }

        AddHeader("Display");
        if (_info is { Width: > 0, Height: > 0 })
        {
            Fact("Resolution", $"{_info.Width} × {_info.Height}", "");
            if (_info.RefreshRate > 0) Fact("Refresh rate", _info.RefreshRate + " Hz", "");
        }
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Host);
        Fact("Scaling", $"{dpi.DpiScaleX * 100:0}%", "");

        AddHeader("Storage");
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                Fact("Drive " + drive.Name.TrimEnd('\\'),
                    $"{Gigabytes(drive.AvailableFreeSpace)} free of {Gigabytes(drive.TotalSize)}", "");
            }
        }
        catch (Exception) { }

        AddHeader("System");
        if (_info != null)
        {
            Fact("Windows", (_info.Os + " (" + _info.OsVersion + ")").Trim(), "");
            Fact("BIOS version", _info.Bios, "");
        }
        Fact("Signed in as", Environment.UserName, "");
        foreach (var ip in DeviceInfo.GetIpAddresses()) Fact("IP address", ip, "");

        AddHeader("Power");
        var battery = Battery.GetPercent();
        Fact("Battery", battery == null ? "No battery" : battery + "%", "");
        Fact("Controller", Host.ControllerBattery ?? "Not connected", "");

        AddHeader("Launcher");
        // Press it five times for a surprise.
        AddRow("SquareDeck version", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "", "",
            click: () => EasterEgg.Tap(Host));
    }
}

/// <summary>Choose which controller button does what in the launcher.</summary>
/// <summary>Settings > Controller > Pages LB / RB switch between: which pages, and in what order.</summary>
public sealed class PageOrderScreen : ListScreen
{
    public PageOrderScreen() : base("Pages LB / RB switch between",
        "{Select}  On / off      {Details}  Move up      {Options}  Move down      {Back}  Back") { }

    protected override void Build()
    {
        var s = Host.Settings;
        AddNote("LB goes left and RB goes right through these pages, in this order, and wraps around at the ends. " +
                "Pages that are off are skipped, but you can still open them from their buttons.");
        var order = MainWindow.PageOrderOf(s);
        for (int n = 0; n < order.Count; n++)
        {
            var page = MainWindow.CyclePages[order[n]];
            bool on = !s.PagesOff.Contains(page.Id);
            string? note = page.Id switch
            {
                "music" when !s.ShowMusicButton => "Its button is hidden, so it's skipped",
                "friends" when !s.ShowFriendsButton => "Its button is hidden, so it's skipped",
                _ => null
            };
            string id = page.Id;
            var row = AddRow($"{n + 1}.  {page.Name}", on ? "On" : "Off", page.Glyph, click: () => Toggle(id), subtitle: note);
            row.Tag = id;
        }
        AddHeader("Order");
        AddNote($"Highlight a page and press {Hints.KeyLabel("Details")} to move it up or {Hints.KeyLabel("Options")} to move it down.");
        AddRow("Back to the original order, all on", null, "", click: Reset);
    }

    public override bool OnAction(NavAction action)
    {
        if (action is NavAction.X or NavAction.Y && System.Windows.Input.Keyboard.FocusedElement is Button { Tag: string id })
        {
            Move(id, action == NavAction.X ? -1 : 1);
            return true;
        }
        return base.OnAction(action);
    }

    private void Toggle(string id)
    {
        var s = Host.Settings;
        if (!s.PagesOff.Remove(id))
        {
            // At least two pages stay on, or LB / RB would have nowhere to go.
            if (MainWindow.CyclePages.Count(p => !s.PagesOff.Contains(p.Id)) <= 2)
            {
                _ = Host.AskAsync("Keep at least two pages", "LB and RB need somewhere to go. Switch another page on first.", "OK");
                return;
            }
            s.PagesOff.Add(id);
        }
        Save();
    }

    private void Move(string id, int step)
    {
        var s = Host.Settings;
        var ids = MainWindow.PageOrderOf(s).Select(i => MainWindow.CyclePages[i].Id).ToList();
        int at = ids.IndexOf(id), to = at + step;
        if (at < 0 || to < 0 || to >= ids.Count) return;
        (ids[at], ids[to]) = (ids[to], ids[at]);
        s.PageOrder = ids;
        UiSound.Move();
        Save(focusId: id);
    }

    private void Reset()
    {
        Host.Settings.PageOrder = new();
        Host.Settings.PagesOff = new();
        Save();
    }

    private void Save(string? focusId = null)
    {
        Host.Settings.Save();
        Rebuild();
        // The moved page keeps the highlight (its row's name changes with its number).
        if (focusId != null && Rows.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == focusId) is { } row)
            FocusLater(row);
    }
}

public sealed class ControlsScreen : ListScreen
{
    private static readonly string[] Roles = { "Select / start", "Back", "Details", "Options" };

    public ControlsScreen() : base("Controller buttons") { }

    protected override void Build()
    {
        AddNote("These apply to the launcher only, not to games.");
        for (int i = 0; i < Roles.Length; i++)
        {
            int role = i;
            AddRow(Roles[i], Hints.ButtonName(Hints.Map[i]) + " button", "\uE7FC", click: () => Change(role));
        }
        AddHeader("Presets");
        AddRow("Bottom button selects, right button goes back", "Xbox and PlayStation habit", "\uE7FC", click: () => Apply("ABXY"));
        AddRow("Right button selects, bottom button goes back", "Nintendo habit", "\uE7FC", click: () => Apply("BAYX"));
        AddNote("The names shown for the buttons (Xbox, PlayStation or Switch) are chosen under Settings > Controller.");
    }

    private async void Change(int role)
    {
        int choice = await Host.AskAsync("Button for “" + Roles[role] + "”",
            "The action that used that button gets this one's old button.",
            "ABXY".Select(c => Hints.ButtonName(c)).ToArray());
        if (choice < 0) return;

        char wanted = "ABXY"[choice];
        var map = Hints.Map.ToCharArray();
        int other = Array.IndexOf(map, wanted);
        (map[role], map[other]) = (map[other], map[role]);
        Apply(new string(map));
    }

    private void Apply(string map)
    {
        Host.SetButtonMap(map);
        Rebuild();
    }
}

/// <summary>
/// The power menu: big round buttons in a row (lock, sleep, hibernate, restart, shut down, exit to the desktop) under
/// your profile picture, name and the battery. Restart and shut down ask on the button itself: the first press turns
/// it red and says "Press again", the second does it. A running game is named as a warning first.
/// </summary>
public sealed class PowerMenuScreen : Screen
{
    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(Button Button, Border Circle, TextBlock Label, string Name, bool Confirm)> _items = new();
    private readonly List<TextBlock> _glyphs = new();
    private readonly List<System.Windows.Media.Brush?> _arts = new();
    /// <summary>How the look lays the options out: in a row (the usual) or as a list (drawers and console menus).</summary>
    private readonly bool _list = Looks.Current.Id is "ubiquity" or "deck" or "xdash";
    private readonly string _side = LookChrome.SheetSide(power: true);
    private readonly TextBlock _hint = Ui.Text("", 15, "SubtleBrush");
    private int _armed = -1;
    private int _choice = -1;

    public override bool IsDialog => true;

    /// <summary>Index of the chosen action (0 lock … 5 exit), or -1 for cancelled.</summary>
    public Task<int> Result => _done.Task;

    public PowerMenuScreen(string? warning)
    {
        RumbleOnMove();
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Who and how much battery, above the buttons.
        var who = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
        var name = Ui.Text(UserName.Display, 26, "FgBrush", bold: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        if (Skins.Current?.TitleText is { } titleText) name.Foreground = titleText; // it sits on the pop-up art's title bar
        who.Children.Add(name);
        if (Battery.GetPercent() is int battery)
        {
            var pill = new Border { CornerRadius = new CornerRadius(QuickSettingsScreen.Round(14)), Padding = new Thickness(12, 4, 14, 5), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            pill.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(battery >= 100 ? "" : ((char)(0xE850 + Math.Clamp(battery / 10, 0, 9))).ToString(), 15, "FgBrush");
            g.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(battery + "%", 15, "FgBrush", bold: true));
            pill.Child = row;
            who.Children.Add(pill);
        }
        panel.Children.Add(who);

        // A heading in the look's way for the drawers and console menus.
        if (_list)
        {
            var heading = Looks.Current.Id == "ubiquity" ? Ui.Text(LookChrome.Spaced("Power"), 26, "FgBrush") : Ui.Text("Power", 30, "FgBrush");
            heading.FontWeight = FontWeights.Light;
            heading.Margin = new Thickness(4, 0, 0, 22);
            panel.Children.Insert(0, heading);
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            who.HorizontalAlignment = HorizontalAlignment.Left;
            who.Margin = new Thickness(4, 0, 0, 22);
        }
        var buttons = new StackPanel { Orientation = _list ? Orientation.Vertical : Orientation.Horizontal, HorizontalAlignment = _list ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
        Add(buttons, "", "Lock", false);
        Add(buttons, "", "Sleep", false);
        Add(buttons, "", "Hibernate", false);
        Add(buttons, "", "Restart", true);
        Add(buttons, "", "Shut down", true);
        Add(buttons, "", "Exit", false);
        panel.Children.Add(buttons);

        _hint.HorizontalAlignment = _list ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _hint.Margin = new Thickness(_list ? 4 : 0, 26, 0, 0);
        _hint.TextAlignment = _list ? TextAlignment.Left : TextAlignment.Center;
        if (_list) _hint.MaxWidth = 420;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.MaxWidth = 760;
        panel.Children.Add(_hint);
        if (warning != null)
        {
            var warn = new Border { CornerRadius = new CornerRadius(QuickSettingsScreen.Round(14)), Padding = new Thickness(18, 10, 18, 11), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            warn.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph("", 16, "AccentBrush");
            g.Margin = new Thickness(0, 1, 10, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(warning, 15, "FgBrush"));
            warn.Child = row;
            panel.Children.Add(warn);
        }

        var card = new Border
        {
            Padding = new Thickness(48, 40, 48, 38), CornerRadius = new CornerRadius(QuickSettingsScreen.Round(30)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 50, ShadowDepth = 0, Opacity = 0.5 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        card.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        LookChrome.StyleSheet(card);
        QuickSettingsScreen.SkinSheet(card);
        switch (_side)
        {
            case "left":
            case "right":
                // A drawer the full height of the screen, in from its edge.
                card.HorizontalAlignment = _side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                card.VerticalAlignment = VerticalAlignment.Stretch;
                card.Width = 520;
                card.Padding = new Thickness(40, 70, 36, 40);
                if (!Theme.ReduceMotion) SlideIn(card, _side == "left" ? -540 : 540, vertical: false);
                break;
            case "bottom":
                // A sheet along the bottom with the options in a row.
                card.HorizontalAlignment = HorizontalAlignment.Stretch;
                card.VerticalAlignment = VerticalAlignment.Bottom;
                card.Margin = new Thickness(60, 0, 60, 0);
                card.Padding = new Thickness(48, 34, 48, 40);
                if (!Theme.ReduceMotion) SlideIn(card, 460, vertical: true);
                break;
        }
        if (_list && _side == "center") card.Width = 560;
        if (!Theme.ReduceMotion && _side == "center")
        {
            var scale = new System.Windows.Media.ScaleTransform(0.94, 0.94);
            card.RenderTransform = scale;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
        var backdrop = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0, 0, 0)) };
        backdrop.Children.Add(card);
        Content = backdrop;
    }

    private static void SlideIn(Border card, double from, bool vertical)
    {
        var move = new System.Windows.Media.TranslateTransform();
        card.RenderTransform = move;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        move.BeginAnimation(vertical ? System.Windows.Media.TranslateTransform.YProperty : System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void Add(Panel row, string glyph, string name, bool confirm)
    {
        int index = _items.Count;
        string look = Looks.Current.Id;
        var glyphText = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = _list ? 24 : 34, VerticalAlignment = VerticalAlignment.Center };
        glyphText.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        TextBlock label;
        Border circle;
        FrameworkElement content;
        if (_list)
        {
            // A row: the symbol, then the name.
            label = Ui.Text(name, 20, "FgBrush");
            label.VerticalAlignment = VerticalAlignment.Center;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            glyphText.Width = 34;
            glyphText.Margin = new Thickness(0, 0, 18, 0);
            line.Children.Add(glyphText);
            line.Children.Add(label);
            circle = new Border { Height = 62, Padding = new Thickness(20, 0, 20, 0), BorderThickness = new Thickness(3), BorderBrush = System.Windows.Media.Brushes.Transparent, Child = line };
            content = circle;
        }
        else
        {
            // A round button (the usual), or a square tile in the looks with square shapes.
            double size = look is "orbit" ? 88 : 96;
            glyphText.HorizontalAlignment = HorizontalAlignment.Center;
            circle = new Border
            {
                Width = look is "aniki" or "solaris" ? 108 : size, Height = look is "aniki" or "solaris" ? 108 : size,
                CornerRadius = new CornerRadius(QuickSettingsScreen.Round(size / 2)),
                BorderThickness = new Thickness(3), BorderBrush = System.Windows.Media.Brushes.Transparent, Child = glyphText
            };
            circle.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            label = Ui.Text(name, 15, "FgBrush", bold: look is not ("orbit" or "aniki"));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 12, 0, 0);
            if (look == "solaris") label.Text = name.ToUpperInvariant();
            var stack = new StackPanel { Width = look is "aniki" or "solaris" ? 132 : 128 };
            stack.Children.Add(circle);
            stack.Children.Add(label);
            content = stack;
        }
        double grow = _list ? 1.02 : 1.08;
        var scale = new System.Windows.Media.ScaleTransform(1, 1);
        var button = new Button
        {
            Style = Ui.StyleOf("BareButton"), Focusable = true, Content = content, FocusVisualStyle = null,
            Margin = _list ? new Thickness(0, 4, 0, 4) : new Thickness(6, 0, 6, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = _list ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.4), RenderTransform = scale
        };
        button.GotKeyboardFocus += (_, _) =>
        {
            if (_armed >= 0 && _armed != index) Disarm();
            Paint(index, focused: true);
            _hint.Text = Describe(index);
            if (!Theme.ReduceMotion)
            {
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(grow, TimeSpan.FromMilliseconds(120)));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(grow, TimeSpan.FromMilliseconds(120)));
            }
        };
        button.LostKeyboardFocus += (_, _) =>
        {
            Paint(index, focused: false);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        button.Click += (_, _) => Choose(index);
        row.Children.Add(button);
        _arts.Add(Ui.SkinBrush(circle, _list ? "panel" : "button")); // an Advanced style's artwork for the tile
        _items.Add((button, circle, label, name, confirm));
        _glyphs.Add(glyphText);
        Paint(index, focused: false);
    }

    private static string Describe(int index) => index switch
    {
        0 => "Lock the PC. Everything stays open.",
        1 => "Low power, back in a moment. Games stay where they are.",
        2 => "Saves everything to the drive and turns off. Uses no battery.",
        3 => "Restart the PC.",
        4 => "Turn the PC off.",
        _ => "Close SquareDeck and go to the Windows desktop."
    };

    private void Paint(int index, bool focused)
    {
        var (_, circle, label, name, _) = _items[index];
        var glyph = _glyphs[index];
        bool armed = index == _armed;
        string shown = Looks.Current.Id == "solaris" && !_list ? name.ToUpperInvariant() : name;
        if (armed)
        {
            Ui.SkinCorners(circle, false);
            circle.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0x3B, 0x3B));
            glyph.Foreground = System.Windows.Media.Brushes.White;
            label.Text = "Press again";
            if (_list) label.Foreground = System.Windows.Media.Brushes.White;
            return;
        }
        label.Text = shown;
        bool round = !_list && Looks.Current.Id is not ("aniki" or "solaris");
        // The look's own way: in a list the highlight paints the whole row; in a row of buttons it lights the button.
        var texts = _list ? new[] { glyph, label } : new[] { glyph };
        if (LookChrome.PaintFace(circle, texts, on: focused && Looks.Current.Id is "orbit" or "solaris" && !_list, focused, round))
        {
            if (!_list) label.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            return;
        }
        if (focused) { Ui.SkinCorners(circle, false); circle.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); }
        else Ui.SkinFill(circle, index < _arts.Count ? _arts[index] : null, "CircleBrush");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, focused ? "BgBrush" : "FgBrush");
        if (focused) circle.SetResourceReference(Border.BorderBrushProperty, "FgBrush");
        else circle.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Disarm()
    {
        int was = _armed;
        _armed = -1;
        if (was >= 0) Paint(was, _items[was].Button.IsKeyboardFocused);
    }

    private void Choose(int index)
    {
        if (_items[index].Confirm && _armed != index)
        {
            // Restart and shut down: one more press to be sure.
            _armed = index;
            Paint(index, focused: true);
            _hint.Text = $"Press A again to {_items[index].Name.ToLowerInvariant()}. Move away or press B to cancel.";
            UiSound.Error();
            return;
        }
        _choice = index;
        Host.Pop(this);
    }

    public override bool OnAction(NavAction action)
    {
        if (action == NavAction.Back && _armed >= 0)
        {
            Disarm();
            _hint.Text = "";
            return true;
        }
        return false;
    }

    public override void FocusDefault() => _items[1].Button.Focus(); // Sleep, the one used most on a handheld

    public override void OnClosed() => _done.TrySetResult(_choice);
}


