using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SquareDeck.Services;
using Windows.Devices.Radios;

namespace SquareDeck.Screens;

/// <summary>
/// Settings. The first page is a short list of categories; each opens a page of its own, so no list is
/// longer than a screen or two.
/// </summary>
public sealed class SettingsScreen : ListScreen
{
    private static readonly (string Id, string Name, string Glyph, string About)[] Categories =
    {
        ("look", "Appearance", "", "Theme, colors, wallpaper, clock, screen shape"),
        ("home", "Home screen", "", "Buttons, screen saver, startup and closing animations"),
        ("sound", "Sound", "", "Interface sounds, sound packs, chimes, menu music"),
        ("controller", "Controller and quick menu", "", "Vibration, button names, in-game menu"),
        ("power", "Power and performance", "\uEC4A", "TDP limit, CPU boost, power mode, Performance mode"),
        ("games", "Games and library", "", "Stores, notifications while playing, achievements, keys"),
        ("access", "Accessibility", "", "Text size, high contrast, reduced motion"),
        ("network", "Network and browser", "", "Wi-Fi, Bluetooth, speed test, web browser"),
        ("system", "System and tools", "", "PC settings, Task Manager, files, AnyFSE, startup"),
        ("data", "Updates, backup and reset", "", "New versions, your data, start over")
    };

    private string _category;
    private readonly StackPanel _nav = new();
    private Border _navPanel = null!;
    private readonly System.Windows.Threading.DispatcherTimer _switchSoon = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private string? _pendingCategory;

    /// <summary>Every settings page and row, for Search: its name, subtitle, page, and how to open it there.</summary>
    internal static List<SearchSetting> BuildIndex(MainWindow host)
    {
        var list = new List<SearchSetting>();
        foreach (var c in Categories)
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
        : base("Settings", "{Select}  Select      {Back}  Back", null)
    {
        _category = category ?? _lastCategory;
        _switchSoon.Tick += (_, _) =>
        {
            _switchSoon.Stop();
            if (_pendingCategory != null && _pendingCategory != _category) Show(_pendingCategory, focusContent: false);
        };
        BuildLayout();
    }

    private void BuildLayout()
    {
        // Replace the plain list page with two panes.
        var navPanel = new Border { Width = 300, CornerRadius = new CornerRadius(18), Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(0, 0, 22, 0), ClipToBounds = true };
        _navPanel = navPanel;
        navPanel.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        navPanel.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        navPanel.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        navPanel.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _nav
        };
        _nav.PreviewGotKeyboardFocus += (_, _) => _focusInNav = true;
        Rows.PreviewGotKeyboardFocus += (_, _) => _focusInNav = false;
        Rows.Width = double.NaN;
        Rows.HorizontalAlignment = HorizontalAlignment.Stretch;
        Rows.Margin = new Thickness(0, 0, 14, 20);
        if (RowsScroll.Parent is Panel old) old.Children.Remove(RowsScroll);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(navPanel);
        Grid.SetColumn(RowsScroll, 1);
        grid.Children.Add(RowsScroll);
        ReplaceBody(grid);
    }

    public override void OnShown()
    {
        SizeNav(animate: false);
        BuildNav();
        if (FocusLabel != null) { Rebuild(); return; } // opened from Search: land on that row
        SuppressAutoFocus = true;
        Rebuild();
        SuppressAutoFocus = false;
        FocusLater(NavRow(_category));
    }

    private bool _focusInNav = true;

    public override void OnResumed()
    {
        BuildNav();
        if (_focusInNav)
        {
            // Back from a page opened from the list of sections (profile, search): the highlight stays in the list.
            SuppressAutoFocus = true;
            Rebuild();
            SuppressAutoFocus = false;
            FocusLater(NavRow(_category));
            return;
        }
        Rebuild(); // back on the setting that opened the page
    }

    public override void OnClosed() => _switchSoon.Stop();

    public override void FocusDefault() => (NavRow(_category) ?? (UIElement?)Rows.Children.OfType<Button>().FirstOrDefault())?.Focus();

    public override bool OnAction(NavAction action)
    {
        // B in the settings goes back to the list of sections first.
        if (action == NavAction.Back && Rows.IsKeyboardFocusWithin && NavRow(_category) is { } row)
        {
            row.Focus();
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

        foreach (var c in Categories)
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
        foreach (var c in Categories)
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
        _category = id;
        _lastCategory = id;
        if (Collapsed)
        {
            // The blade shows where you are with a stronger tint, so it's redrawn (keeping the focus on the same icon).
            var focused = _nav.Children.OfType<Button>().FirstOrDefault(b => b.IsKeyboardFocused)?.DataContext as string;
            BuildNav();
            if (focused != null) _nav.Children.OfType<Button>().FirstOrDefault(b => b.DataContext as string == focused)?.Focus();
        }
        else
            foreach (var b in _nav.Children.OfType<Button>())
                if (b.DataContext is string tag && tag.StartsWith("nav:") && tag is not ("nav:profile" or "nav:search" or "nav:toggle"))
                    b.Tag = tag == "nav:" + id ? "selected" : null;
        SuppressAutoFocus = true;
        ForgetRow(); // a different section starts at its top
        Rebuild();
        SuppressAutoFocus = false;
        RowsScroll.ScrollToTop();
        if (focusContent) FocusLater(Rows.Children.OfType<Button>().FirstOrDefault());
        // No sound here: moving onto the section in the list already made one.
    }

    protected override void Build()
    {
        var s = Host.Settings;
        var cat = Categories.FirstOrDefault(c => c.Id == _category);
        if (cat.Id == null) { _category = "look"; cat = Categories[0]; }

        // The section's name and what's in it, at the top of the right pane.
        var title = Ui.Text(cat.Name, 30, "FgBrush", bold: true);
        title.Margin = new Thickness(6, 0, 0, 2);
        Rows.Children.Add(title);
        var about = Ui.Text(cat.About, 15, "SubtleBrush");
        about.Margin = new Thickness(6, 0, 0, 0);
        Rows.Children.Add(about);

        switch (_category)
        {
            case "look": BuildLook(s); break;
            case "home": BuildHome(s); break;
            case "sound": BuildSound(s); break;
            case "controller": BuildController(s); break;
            case "power": BuildPower(s); break;
            case "games": BuildGames(s); break;
            case "access": BuildAccess(s); break;
            case "network": BuildNetwork(s); break;
            case "system": BuildSystem(s); break;
            case "data": BuildData(s); break;
        }
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

        AddHeader("Motion");
        AddRow("Reduce motion", OnOff(s.ReduceMotion), "\uE916", click: () =>
        {
            s.ReduceMotion = !s.ReduceMotion;
            Apply();
        });
        AddNote("Stops the pulsing highlight, page slides, fades, animated covers and the startup animation.");
    }

    private static string _lastCategory = "look";


    private void BuildLook(Settings s)
    {
        AddHeader("Theme");
        AddRow("UI style", UiStyles.Find(s.UiStyle).Name, "\uE771", click: () => Host.Push(new UiStyleScreen()),
            subtitle: "Switch, Xbox, PlayStation 5, Steam Big Picture, Neon, Minimal, Retro or Glass");
        AddRow("Moving background", s.DynamicBackground.Length == 0 ? "Off" : System.IO.Path.GetFileNameWithoutExtension(s.DynamicBackground),
            "\uE714", click: () => Host.Push(new DynamicBackgroundScreen()), subtitle: "Waves or blades in motion, with a blur level");
        AddRow("Theme", ThemeLabel(s.ThemeMode), "", click: () =>
        {
            // Light -> Dark -> OLED black -> Follow system -> Light
            s.ThemeMode = s.ThemeMode switch { "light" => "dark", "dark" => "oled", "oled" => "system", _ => "light" };
            s.DarkTheme = Theme.ShouldBeDark(s);
            Theme.Apply(s);
            s.Save();
            Rebuild();
        });
        AddRow("Looks, wallpaper and tile corners", glyph: "", click: () => Host.Push(new ThemeScreen()));
        AddRow("Highlight color", AccentLabel(s.AccentColor), "", click: () => Host.Push(new AccentScreen()));
        AddRow("Tint the highlight with the game's colors", OnOff(s.GameAccent), "", click: () =>
            Toggle(() => s.GameAccent = !s.GameAccent));
        AddRow("Profile picture", glyph: "", click: () => Host.Push(new AvatarScreen()));

        AddHeader("Artwork");
        AddRow("Hero background behind the home row", OnOff(s.HeroBackgrounds), "", click: () =>
            Toggle(() => s.HeroBackgrounds = !s.HeroBackgrounds),
            subtitle: s.DynamicBackground.Length > 0 ? "Turned off while a moving background is on" : null);
        AddRow("Animated cover on the highlighted game", OnOff(s.AnimatedCovers), "", click: () =>
            Toggle(() => s.AnimatedCovers = !s.AnimatedCovers));
        AddRow("Color the game details page from the cover art", OnOff(s.DetailsGradient), "", click: () =>
            Toggle(() => s.DetailsGradient = !s.DetailsGradient));

        AddHeader("Top bar");
        AddRow("Clock", (s.Clock24Hour ? "24-hour" : "12-hour") + (s.ClockSeconds ? ", seconds" : "") + (s.ClockDate ? ", date" : ""),
            "", click: ClockOptions);
        AddRow("Show battery percentage", OnOff(s.ShowBatteryPercent), "", click: () =>
            Toggle(() => s.ShowBatteryPercent = !s.ShowBatteryPercent));

        AddHeader("Screen");
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
    }

    private static readonly (string Id, string Name)[] ButtonStyles =
        { ("grow", "Grow and glow"), ("lift", "Lift"), ("bounce", "Bounce"), ("pulse", "Pulse"), ("wiggle", "Wiggle") };
    private static readonly (string Id, string Name)[] PageStyles =
        {
            ("slide", "Slide up"), ("slide-left", "Slide in from the side"), ("slide-down", "Drop down"), ("rise", "Rise"),
            ("fade", "Fade"), ("zoom", "Zoom"), ("pop", "Pop"), ("spring", "Spring"), ("drop", "Bounce"), ("flip", "Card flip"),
            ("swing", "Swing"), ("focus", "Come into focus"), ("none", "None")
        };
    private static readonly (string Id, string Name)[] TileStyles =
        { ("pulse", "Pulsing ring"), ("steady", "Steady ring"), ("grow", "Tile grows") };

    private async Task ChooseAsync((string Id, string Name)[] options, string title, string? message, Action<string> set)
    {
        int choice = await Host.AskAsync(title, message, options.Select(o => o.Name).ToArray());
        if (choice >= 0) Toggle(() => set(options[choice].Id));
    }

    private void BuildHome(Settings s)
    {
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
            click: () => _ = ChooseAsync(TileStyles, "Selected game", "How the highlighted game on the home row stands out.", v => s.TileHighlight = v));
        AddNote("Startup and closing animations are under Appearance. Reduce motion (Accessibility) turns all of these off.");

        AddHeader("Home row");
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
        AddNote("Home, Favorites, Apps, Captures, Stores, Settings and Power are always shown. Hidden pages can still be opened from Settings.");

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
        AddRow("Boot video", s.BootVideo.Length > 0 ? Path.GetFileName(s.BootVideo) : "Built-in animation", "", click: ChooseBootVideo);
        AddRow("Closing animation on Exit SquareDeck", OnOff(s.ExitAnimation), "", click: () =>
            Toggle(() => s.ExitAnimation = !s.ExitAnimation));
        AddRow("Closing animation style", MainWindow.ExitStyles.FirstOrDefault(x => x.Id == s.ExitStyle).Name ?? "Logo goodbye", "",
            click: ChooseExitStyle);
    }

    private void BuildSound(Settings s)
    {
        AddRow("Interface sounds", !s.UiSounds ? "Off" : UiSound.SetName(s.SoundSet) + ", " + s.SoundVolume + "%",
            "", click: () => Host.Push(new SoundsScreen()));
        AddNote("Choose a built-in set or one of the sound packs, set the loudness, and pick which kinds of sound play.");
        AddHeader("Background music");
        AddRow("Music on the launcher's screens", BackgroundMusic.NameOf(s.MenuMusic), "\uE8D6", click: () => _ = ChooseBackgroundMusicAsync(Host, Rebuild));
        if (s.MenuMusic.Length > 0)
            AddRow("Music loudness", s.MenuMusicVolume + "%", "\uE995", click: () =>
            {
                int[] levels = { 10, 20, 30, 50, 75, 100 };
                Toggle(() => s.MenuMusicVolume = levels[(Array.IndexOf(levels, s.MenuMusicVolume) + 1) % levels.Length]);
            });
        AddNote("Plays quietly only while no game is running, and pauses for videos, the Music page and the browser.");
        AddRow("Add your own music (MP3 files)", glyph: "\uE8B7", click: () =>
        {
            try { Directory.CreateDirectory(BackgroundMusic.UserDir); } catch (Exception) { }
            ProcessUtil.Open(BackgroundMusic.UserDir);
        });

        AddHeader("Music page");
        AddRow("Search Spotify and YouTube Music", OnOff(s.MusicServices), "\uE721", click: () => Toggle(() => s.MusicServices = !s.MusicServices));
        if (s.MusicServices)
        {
            var spotify = new SpotifyClient(s);
            AddRow("Spotify Client ID", s.SpotifyClientId.Length > 0 ? Mask(s.SpotifyClientId) : "Not set", "\uE8D7", click: () => _ = EditSpotifyIdAsync());
            if (spotify.HasClientId)
                AddRow(spotify.IsSignedIn ? "Signed in to Spotify (sign out)" : "Sign in to Spotify", glyph: "\uE77B", click: () => _ = SpotifySignInAsync(spotify));
            AddNote("YouTube Music opens inside the launcher; sign in there once to see your playlists. Spotify needs a free app of your own: " +
                    "create one at developer.spotify.com, add the redirect URI " + SpotifyClient.RedirectUri + ", and paste its Client ID here. " +
                    "Playing on a device from the launcher needs Spotify Premium; without it, results open in the Spotify app.");
        }

        AddHeader("Interface");
        AddRow("Game start chime", UiSound.LaunchChimes.FirstOrDefault(c => c.Id == s.LaunchChime).Name ?? "Rise", "",
            click: ChooseLaunchChime);
        AddRow("Startup chime and animation", OnOff(s.StartupIntro), "", click: () =>
            Toggle(() => s.StartupIntro = !s.StartupIntro));
        AddRow("Volume, brightness and radios", glyph: "", click: () => Host.Push(new QuickSettingsScreen()));
        AddRow("Sound output device", glyph: "", click: () => Host.Push(new PcSettingsScreen()));
    }

    private void BuildController(Settings s)
    {
        AddHeader("Typing");
        AddRow("Keyboard for typing", s.TouchKeyboard == "windows" ? "Windows touch keyboard" : "SquareDeck keyboard", "\uE765",
            click: () => Toggle(() => s.TouchKeyboard = s.TouchKeyboard == "windows" ? "squaredeck" : "windows"),
            subtitle: s.TouchKeyboard == "windows" ? "Best on a touchscreen; Windows' keyboard opens when you type"
                                                   : "Built in, works with the controller");

        if (AllyButtons.Model.Length > 0 || AllyButtons.IsSupported)
        {
            AddHeader(AllyButtons.Model.Length > 0 ? AllyButtons.Model : "ROG Ally");
            AddRow("Remap the Armoury Crate and Command Center buttons", s.AllyRemap ? "On" : "Off", "\uE765",
                click: () => Host.Push(new AllyButtonsScreen()));
        }

        AddHeader("Controller");
        AddRow("Button names shown on screen", Hints.Styles.FirstOrDefault(x => x.Id == s.GlyphStyle).Name ?? "Xbox", "", click: () =>
        {
            int index = Array.FindIndex(Hints.Styles, x => x.Id == s.GlyphStyle);
            Toggle(() => s.GlyphStyle = Hints.Styles[(index + 1) % Hints.Styles.Length].Id);
        });
        AddNote("Xbox shows A B X Y, PlayStation shows ✕ ○ □ △, Switch shows B A Y X for the same button positions. " +
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
        AddRow("Vibration strength", s.RumbleStrength <= 0 ? "Off" : s.RumbleStrength + "%", "", click: RumbleStrength);
        AddRow("Controller test", glyph: "", click: () => Host.Push(new ControllerTestScreen()));

        AddHeader("Summon and quick menu");
        AddRow("Summon buttons bring the launcher forward", OnOff(s.GuideSummon), "", click: () =>
            Toggle(() => s.GuideSummon = !s.GuideSummon));
        AddRow("In-game quick menu", OnOff(s.InGameQuickMenu), "", click: () =>
            Toggle(() => s.InGameQuickMenu = !s.InGameQuickMenu));
        AddRow("Controller buttons", SummonLabel(s.SummonCombo), "", click: ChooseSummonButtons);
        AddRow("Keyboard shortcut", MainWindow.SummonKeys.FirstOrDefault(k => k.Id == s.SummonKey).Name ?? "None", "",
            click: ChooseSummonKey);
        AddRow("Where the quick menu appears", s.OverlaySide switch { "left" => "Slides in from the left", "center" => "Pop-up in the middle", _ => "Slides in from the right" },
            "", click: () => Toggle(() => s.OverlaySide = s.OverlaySide switch { "right" => "left", "left" => "center", _ => "right" }));
    }

    /// <summary>Power and performance: TDP control for AMD handhelds, and the launcher's own Performance mode.</summary>
    private void BuildPower(Settings s)
    {
        var chip = TdpControl.Chip;
        AddHeader("Processor power (TDP)");
        if (chip == null)
        {
            AddNote("TDP control works with AMD processors (Ryzen Z2 Extreme, Z1 Extreme and similar). This PC has " +
                    (TdpControl.CpuName.Length > 0 ? TdpControl.CpuName : "a processor SquareDeck couldn't identify") + ".");
        }
        else if (!TdpControl.Installed)
        {
            AddNote(chip.Name + ". " + chip.Note);
            AddRow("Set up TDP control", "Not set up", "\uE896", click: SetUpTdp,
                subtitle: TdpControl.HasBundled ? "Installs RyzenAdj, which comes with SquareDeck (one Windows permission prompt)"
                                                : "Downloads RyzenAdj from GitHub and installs it (one Windows permission prompt)");
            AddNote("Setting the power limit needs administrator rights. Setup installs RyzenAdj (an open-source tool, " +
                    "github.com/FlyGoat/RyzenAdj) into Program Files and adds a background task, \"SquareDeck\\ApplyTdp\", " +
                    "that applies your limits without asking each time. You can remove both here later.");
        }
        else
        {
            AddRow("TDP control", OnOff(s.TdpEnabled), "\uEC4A", click: () =>
            {
                s.TdpEnabled = !s.TdpEnabled;
                SaveTdp();
            }, subtitle: s.TdpEnabled ? (TdpControl.LastResult.Length > 0 ? TdpControl.LastResult : chip.Name) : chip.Name);
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
            }
        }

        AddHeader("Processor");
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

        if (TdpControl.Installed)
        {
            AddHeader("TDP control setup");
            AddRow("Keep re-applying", OnOff(s.TdpReapply), "\uE72C", click: () => { s.TdpReapply = !s.TdpReapply; SaveTdp(); },
                subtitle: "Every 15 seconds, for devices whose own software puts its limit back (Armoury Crate, Legion Space)");
            AddRow("Apply now", TdpControl.LastResult, "\uE768", click: async () => { await TdpControl.ApplyAsync(); Rebuild(); });
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

        AddHeader("Launcher performance");
        AddRow("Performance mode", Perf.Label(s.PerformanceMode), "\uEC4A", click: () =>
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
        Host.ShowToast("TDP control", "Setting up…", TdpControl.HasBundled ? "Installing RyzenAdj" : "Downloading RyzenAdj", "", "info");
        var error = await TdpControl.InstallAsync();
        if (error != null)
        {
            await Host.AskAsync("TDP control isn't set up", error, "OK");
        }
        else
        {
            Host.Settings.TdpEnabled = true;
            Host.Settings.TdpWatts = TdpControl.Chip?.Default ?? 15;
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
        AddRow("Splash screen while a game starts", OnOff(s.LaunchSplash), "\uE7F4", click: () => Toggle(() => s.LaunchSplash = !s.LaunchSplash),
            subtitle: "The game's cover fills the screen until the game is running");
        AddRow("Start Steam games directly", OnOff(s.SteamDirectLaunch), "\uE756", click: () => Toggle(() => s.SteamDirectLaunch = !s.SteamDirectLaunch),
            subtitle: "From the game's own program instead of a Steam link, so Steam's launch pop-up stays away");
        AddNote("With this on, SquareDeck starts the game's .exe itself and tells it which Steam game it is. Steam must already be " +
                "running; if it isn't, or the program can't be found, the game starts through Steam as usual. SquareDeck remembers each " +
                "game's program the first time you play it, and you can pick it yourself in the game's options (Program to start " +
                "directly). Launch options set in Steam aren't used, the Steam overlay may not appear, and a few games (with their own " +
                "launcher or copy protection) still restart themselves through Steam.");

        AddHeader("Captures");
        AddRow("Open Captures as albums (one per game)", OnOff(s.CaptureAlbums), "\uE8B9",
            click: () => Toggle(() => s.CaptureAlbums = !s.CaptureAlbums),
            subtitle: "Albums are made from the game name in each capture's file name");

        AddHeader("Library");
        AddRow("Rescan Steam library", glyph: "", click: Rescan);
        AddRow("Re-download cover art", glyph: "", click: Redownload);
        int hidden = Host.HiddenCount;
        if (hidden > 0)
            AddRow("Restore removed games", hidden.ToString(), "", click: () => { Host.RestoreHidden(); Rebuild(); });
        AddHeader("Other game stores");
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

        AddHeader("While playing");
        AddRow("Free graphics memory for the game", OnOff(s.FreeGpuDuringGames), "\uE7F4", click: () =>
            Toggle(() => s.FreeGpuDuringGames = !s.FreeGpuDuringGames));
        AddNote("While a game runs, the launcher stops using the graphics card and lets go of its pictures, so the game gets the " +
                "video memory. Recommended; turn off only if switching back to the launcher feels slow.");
        AddRow("Do not disturb (hide Windows notifications)", OnOff(s.DoNotDisturb), "", click: () =>
            Toggle(() => s.DoNotDisturb = !s.DoNotDisturb));
        AddRow("Return to the launcher when a game closes", OnOff(s.ReturnToLauncher), "", click: () =>
            Toggle(() => s.ReturnToLauncher = !s.ReturnToLauncher));
        AddRow("Session summary when a game closes", OnOff(s.SessionSummary), "", click: () =>
            Toggle(() => s.SessionSummary = !s.SessionSummary));

        AddHeader("Achievements and play time");
        AddRow("Achievement pop-ups", OnOff(s.AchievementToasts), "", click: () =>
            Toggle(() => s.AchievementToasts = !s.AchievementToasts));
        AddRow("Recent achievements", glyph: "", click: () => Host.Push(new UnlocksScreen()));
        AddRow("Your play time", glyph: "", click: () => Host.Push(new PlaytimeScreen()));

        AddHeader("Service keys");
        AddRow("Steam Web API key (friends, not-installed games)", s.SteamWebApiKey.Length > 0 ? Mask(s.SteamWebApiKey) : "Not set", "",
            click: () => _ = EditSteamKeyAsync(Host, Rebuild));
        AddRow("SteamGridDB API key (cover art)", Mask(s.SteamGridDbApiKey), "", click: EditApiKey);
    }

    private void BuildNetwork(Settings s)
    {
        AddHeader("Connections");
        AddRow("Wi-Fi", glyph: "", click: () => Host.Push(new WifiScreen()));
        AddRow("Bluetooth", glyph: "", click: () => Host.Push(new BluetoothScreen()));
        AddRow("Quick settings (volume, brightness, radios)", glyph: "", click: () => Host.Push(new QuickSettingsScreen()));
        AddRow("Network speed test", glyph: "", click: () => Host.Push(new SpeedTestScreen()));

        AddHeader("Web browser");
        AddRow("Open the browser", glyph: "", click: () => Host.Push(BrowserScreen.Open(Host)));
        AddRow("Show its button on the home screen", OnOff(s.ShowBrowserButton), "", click: () =>
            Toggle(() => s.ShowBrowserButton = !s.ShowBrowserButton));
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
        AddRow("Restart to UEFI / BIOS setup", glyph: "\uE950", click: () => _ = Host.RestartToFirmwareAsync());
        AddRow("Format a USB drive or memory card", glyph: "", click: () => Host.Push(new StorageScreen()));

        AddHeader("Tools");
        AddRow("Task Manager", "Apps, processes, performance", "", click: () => Host.Push(new TaskManagerScreen()));
        AddRow("File explorer", glyph: "", click: () => Host.Push(new ExplorerScreen()));
        bool homeApp = HomeApp.IsHomeApp || (AnyFse.IsInstalled() && AnyFse.LaunchesUs() && AnyFse.IsHomeApp());
        AddRow("Xbox mode home app", homeApp ? "SquareDeck" : "Set up", "\uE7FC",
            click: () => Host.Push(new HomeAppScreen()), subtitle: "Start Xbox mode (the full screen experience) straight into SquareDeck");
        if (AllyButtons.Model.Length > 0 || AllyButtons.IsSupported)
            AddRow("ROG Ally buttons", s.AllyRemap ? "Remapped" : "Off", "\uE765", click: () => Host.Push(new AllyButtonsScreen()),
                subtitle: "Armoury Crate, Command Center and Mode+ buttons");

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
    }

    private void BuildData(Settings s)
    {
        AddHeader("Updates");
        AddRow("This version", "SquareDeck " + Updater.Current, "");
        AddRow("Check for updates automatically", OnOff(s.AutoUpdateCheck), "", click: () =>
            Toggle(() => s.AutoUpdateCheck = !s.AutoUpdateCheck));
        AddRow("Check for updates now", glyph: "", click: () => _ = Host.CheckForUpdatesAsync(manual: true));
        if (s.SkippedVersion.Length > 0)
            AddRow("Stop ignoring " + s.SkippedVersion, glyph: "", click: () => Toggle(() => s.SkippedVersion = ""));
        AddRow("Open the releases page", glyph: "", click: () => ProcessUtil.Open(Updater.ReleasesPage));

        AddHeader("Your data");
        AddRow("Backup and restore", glyph: "", click: () => Host.Push(new BackupScreen()));
        AddRow("Downloaded art and video", CacheLabel(s), "", click: CacheOptions);
        AddRow("Show Windows notifications in the launcher", !s.WindowsNotifications ? "Off" : WindowsNotifications.Status switch
            {
                "allowed" => "On", "denied" => "On, but Windows hasn't allowed it", "unsupported" => "Not supported on this PC", _ => "On"
            }, "\uEA8F", click: ToggleWindowsNotifications);
        if (s.WindowsNotifications && WindowsNotifications.Status == "denied")
            AddRow("Allow it in Windows (Privacy > Notifications)", glyph: "\uE713", click: () => ProcessUtil.Open("ms-settings:privacy-notifications"));
        AddRow("Notifications", Host.Notifications.Unread > 0 ? Host.Notifications.Unread + " new" : null, "",
            click: () => Host.Push(new NotificationsScreen()));

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
            (copied.Length > 0 ? "Use the key I copied" : "Enter my key", copied.Length > 0 ? "\uE77F" : "\uE765"),
            ("Get a key from Steam (opens in the browser)", "\uE774")
        };
        if (has) options.Add(("Remove my key", "\uE74D"));
        int choice = await host.AskWithIconsAsync("Steam Web API key",
            "A free key from Steam lets SquareDeck list your friends and the games you own but haven't installed. " +
            "On Steam's page, sign in, type any domain name (for example \"localhost\"), agree, and copy the key. " +
            "It's stored only on this PC.", options.ToArray());
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
        int choice = await Host.AskAsync("Buttons that bring up the quick menu",
            "Hold them together while a game is running. Pick a combination your games don't use. " +
            "Windows may keep the Guide button for the Game Bar; if it does, use one of the others.",
            SummonCombos.Select(c => c.Name).ToArray());
        if (choice >= 0) Toggle(() => Host.Settings.SummonCombo = SummonCombos[choice].Id);
    }

    private async void ChooseSummonKey()
    {
        int choice = await Host.AskAsync("Keyboard shortcut for the quick menu",
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
            await Host.RunBusyAsync("Finish signing in to Spotify in your browser\u2026", () => spotify.SignInAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't sign in to Spotify", ex.Message, "OK");
        }
        Rebuild();
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

public sealed class AvatarScreen : GridScreen
{
    public AvatarScreen() : base("Profile picture")
    {
        for (int i = 0; i < Avatars.Presets.Length; i++)
        {
            string setting = "preset:" + i;
            AddTile(Avatars.Build(setting, 120), null, 120, () => Pick(setting));
        }
        AddTile(GlyphBox(120, 120, ""), "Choose image…", 120, Browse);

        // Your Steam profile picture, read from Steam's own files on this PC.
        if (MainWindow.FindSteamAvatarFile() is string steam && ImageLoader.FromFile(steam, 240) is { } picture)
        {
            var face = new System.Windows.Shapes.Ellipse
            {
                Width = 120, Height = 120,
                Fill = new System.Windows.Media.ImageBrush(picture) { Stretch = System.Windows.Media.Stretch.UniformToFill }
            };
            AddTile(face, "Steam avatar", 120, () => Import(steam));
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

/// <summary>Volume, brightness and radios in one small panel (View button, Q, or the top-bar icon).</summary>
public sealed class QuickSettingsScreen : Screen
{
    private readonly Slider _volume, _brightness;
    private readonly TextBlock _volumeText, _brightnessText, _muteText, _wifiText, _bluetoothText, _airplaneText;
    private readonly Border _brightnessRow;
    private readonly System.Windows.Threading.DispatcherTimer _brightnessApply = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool? _wifi, _bluetooth;
    private bool _syncing;           // true while we set a slider ourselves, so that isn't treated as the user
    private bool _brightnessReady;

    public override bool IsDialog => true;

    public QuickSettingsScreen()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Quick settings", 26));

        var rows = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        rows.Children.Add(Ui.SliderRow("Volume", "", out _volume, out _volumeText));
        var mute = Ui.ValueRow("Mute", "", out _muteText);
        mute.Click += (_, _) => { Audio.SetMute(!Audio.GetMute()); ShowAudio(); };
        rows.Children.Add(mute);

        _brightnessRow = Ui.SliderRow("Brightness", "", out _brightness, out _brightnessText);
        _brightnessRow.Visibility = Visibility.Collapsed; // shown once we know the screen supports it
        rows.Children.Add(_brightnessRow);

        // TDP, when TDP control is set up and on: for now only (until the game closes, or the launcher restarts).
        if (TdpControl.Ready && TdpControl.Chip is { } chip)
        {
            rows.Children.Add(Ui.SliderRow("TDP", "\uEC4A", out var tdp, out var tdpText));
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
        }

        var wifi = Ui.ValueRow("Wi-Fi", "", out _wifiText);
        wifi.Click += async (_, _) => { if (_wifi != null) await SetRadiosAsync(!_wifi.Value, null); };
        rows.Children.Add(wifi);

        var bluetooth = Ui.ValueRow("Bluetooth", "", out _bluetoothText);
        bluetooth.Click += async (_, _) => { if (_bluetooth != null) await SetRadiosAsync(null, !_bluetooth.Value); };
        rows.Children.Add(bluetooth);

        var airplane = Ui.ValueRow("Airplane mode", "", out _airplaneText);
        airplane.Click += async (_, _) =>
        {
            // Windows has no public switch for its own airplane mode; this turns both radios off (or back on).
            bool anyOn = _wifi == true || _bluetooth == true;
            await SetRadiosAsync(!anyOn, !anyOn);
        };
        rows.Children.Add(airplane);

        // Night light: Windows' warmer evening colors.
        var night = Ui.ValueRow("Night light", "\uE708", out var nightText);
        void ShowNight() => nightText.Text = NightLight.IsOn switch { true => "On", false => "Off", _ => "Open settings" };
        ShowNight();
        night.Click += async (_, _) =>
        {
            if (NightLight.IsOn is bool on && NightLight.Set(!on))
            {
                await Task.Delay(300);
                ShowNight();
            }
            else ProcessUtil.Open("ms-settings:nightlight");
        };
        rows.Children.Add(night);
        Ui.GroupRows(rows);
        panel.Children.Add(rows);

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

        var box = new Border
        {
            Width = 640,
            Padding = new Thickness(32),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel
        };
        box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0, 0, 0))
        };
        backdrop.Children.Add(box);
        Content = backdrop;
    }

    public override void OnShown()
    {
        ShowAudio();
        _ = LoadAsync();
    }

    public override void OnClosed() => _brightnessApply.Stop();

    public override void FocusDefault() => _volume.Focus();

    private void ShowAudio()
    {
        _syncing = true;
        var volume = Audio.GetVolume();
        _volume.IsEnabled = volume != null;
        _volume.Value = volume ?? 0;
        _volumeText.Text = volume == null ? "No device" : volume + "%";
        _muteText.Text = Audio.GetMute() ? "On" : "Off";
        _syncing = false;
    }

    private async Task LoadAsync()
    {
        await ShowRadiosAsync();
        var brightness = await Brightness.GetAsync();
        if (brightness != null)
        {
            _syncing = true;
            _brightness.Value = brightness.Value;
            _syncing = false;
            _brightnessRow.Visibility = Visibility.Visible;
            _brightnessReady = true;
        }
    }

    private async Task ShowRadiosAsync()
    {
        _wifi = await NetworkService.GetRadioStateAsync(RadioKind.WiFi);
        _bluetooth = await NetworkService.GetRadioStateAsync(RadioKind.Bluetooth);
        _wifiText.Text = _wifi == null ? "Not available" : _wifi.Value ? "On" : "Off";
        _bluetoothText.Text = _bluetooth == null ? "Not available" : _bluetooth.Value ? "On" : "Off";
        bool hasRadio = _wifi != null || _bluetooth != null;
        _airplaneText.Text = !hasRadio ? "Not available" : _wifi != true && _bluetooth != true ? "On" : "Off";
    }

    private async Task SetRadiosAsync(bool? wifi, bool? bluetooth)
    {
        if (wifi != null && _wifi != null) await NetworkService.SetRadioAsync(RadioKind.WiFi, wifi.Value);
        if (bluetooth != null && _bluetooth != null) await NetworkService.SetRadioAsync(RadioKind.Bluetooth, bluetooth.Value);
        await ShowRadiosAsync();
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
        Fact("SquareDeck version",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3), "");
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
