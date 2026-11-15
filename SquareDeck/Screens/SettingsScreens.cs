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
        ("games", "Games and library", "", "Stores, notifications while playing, achievements, keys"),
        ("access", "Accessibility", "", "Text size, high contrast, reduced motion"),
        ("network", "Network and browser", "", "Wi-Fi, Bluetooth, speed test, web browser"),
        ("system", "System and tools", "", "PC settings, Task Manager, files, AnyFSE, startup"),
        ("data", "Updates, backup and reset", "", "New versions, your data, start over")
    };

    private readonly string? _category;

    public SettingsScreen() : this(null) { }

    private SettingsScreen(string? category)
        : base(category == null ? "Settings" : Categories.First(c => c.Id == category).Name) => _category = category;

    protected override void Build()
    {
        var s = Host.Settings;
        switch (_category)
        {
            case null:
                BuildHome();
                break;

            case "look": BuildLook(s); break;
            case "home": BuildHome(s); break;
            case "sound": BuildSound(s); break;
            case "controller": BuildController(s); break;
            case "games": BuildGames(s); break;
            case "network": BuildNetwork(s); break;
            case "system": BuildSystem(s); break;
            case "data": BuildData(s); break;
        }
    }

    private static string _lastCategory = "look";

    /// <summary>The first page: who and what this is, then the categories as a grid of cards.</summary>
    private void BuildHome()
    {
        // A summary card: profile picture, name, version, and a way to the profile page.
        var top = new DockPanel { LastChildFill = true };
        var face = Avatars.Build(Host.Settings.Avatar, 64);
        face.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(face, Dock.Left);
        top.Children.Add(face);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // The Steam account signed in on this PC, rather than the Windows user name.
        var steamId = SteamLibrary.GetSteamId64();
        var (persona, account) = SteamLibrary.GetSteamNames();
        who.Children.Add(Ui.Text(persona ?? account ?? (steamId != null ? "Steam account" : "Not signed in to Steam"), 22, "FgBrush", bold: true));
        who.Children.Add(Ui.Text((steamId != null ? "Steam ID " + steamId : "Sign in to Steam to see your account") +
                                 "  \u00B7  SquareDeck " + Updater.Current, 15, "SubtleBrush"));
        top.Children.Add(who);
        var summary = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 16, 22, 16), Child = top };
        summary.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var profile = Ui.Tile(summary);
        profile.Margin = new Thickness(-8, 0, -8, 6);
        profile.DataContext = "profile";
        profile.Click += (_, _) => Host.Push(new ProfileScreen());
        // Wrapped, so the list's own "keep the highlight in place" logic leaves the focus to this page.
        Rows.Children.Add(new Grid { Children = { profile } });

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-8, 0, -8, 0) };
        Button? focus = null;
        foreach (var c in Categories)
        {
            string id = c.Id;
            var stack = new StackPanel { Height = 140 };
            var badge = Ui.IconBadge(c.Glyph, 52);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.Margin = new Thickness(0, 0, 0, 14);
            stack.Children.Add(badge);
            stack.Children.Add(Ui.Text(c.Name, 18, "FgBrush", bold: true));
            var about = Ui.Text(c.About, 14, "SubtleBrush");
            about.TextWrapping = TextWrapping.Wrap;
            about.TextTrimming = TextTrimming.CharacterEllipsis;
            about.MaxHeight = 40;
            about.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(about);
            var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 18, 20, 16), Child = stack };
            card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            var tile = Ui.Tile(card);
            tile.DataContext = id;
            tile.GotKeyboardFocus += (_, _) => _lastCategory = id;
            tile.Click += (_, _) =>
            {
                if (id == "access") Host.Push(new AccessibilityScreen());
                else Host.Push(new SettingsScreen(id));
            };
            if (id == _lastCategory) focus = tile;
            grid.Children.Add(tile);
        }
        Rows.Children.Add(grid);
        FocusLater(focus ?? grid.Children.OfType<Button>().First());
    }

    private void BuildLook(Settings s)
    {
        AddHeader("Theme");
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
            Toggle(() => s.HeroBackgrounds = !s.HeroBackgrounds));
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

    private void BuildHome(Settings s)
    {
        AddHeader("Home row");
        AddRow("Continue where you left off", OnOff(s.ContinueTile), "\uE768", click: () =>
            Toggle(() => s.ContinueTile = !s.ContinueTile),
            subtitle: "Your last game as a large tile with its artwork, first on the home row");

        AddHeader("Buttons under the games");
        AddRow("Music button", OnOff(s.ShowMusicButton), "", click: () => Toggle(() => s.ShowMusicButton = !s.ShowMusicButton));
        AddRow("Friends button", OnOff(s.ShowFriendsButton), "", click: () => Toggle(() => s.ShowFriendsButton = !s.ShowFriendsButton));
        AddRow("Web browser button", OnOff(s.ShowBrowserButton), "", click: () => Toggle(() => s.ShowBrowserButton = !s.ShowBrowserButton));
        AddRow("Explorer (files) button", OnOff(s.ShowExplorerButton), "", click: () => Toggle(() => s.ShowExplorerButton = !s.ShowExplorerButton));
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
        AddRow("Closing animation on Exit to desktop", OnOff(s.ExitAnimation), "", click: () =>
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

    private void BuildGames(Settings s)
    {
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
        AddRow("Include Epic, GOG, Xbox, Battle.net and Steam shortcuts", OnOff(s.OtherStores), "", click: () =>
        {
            Toggle(() => s.OtherStores = !s.OtherStores);
            _ = Host.ReloadAsync(waitForArt: false);
        });

        AddHeader("While playing");
        AddRow("Free graphics memory for the game", OnOff(s.FreeGpuDuringGames), "\uE7F4", click: () =>
            Toggle(() => s.FreeGpuDuringGames = !s.FreeGpuDuringGames));
        AddNote("While a game runs, the launcher stops using the graphics card and lets go of its pictures, so the game gets the " +
                "video memory. Recommended; turn off only if switching back to the launcher feels slow.");
        AddRow("3-second countdown before a game starts", OnOff(s.LaunchCountdown), "", click: () =>
            Toggle(() => s.LaunchCountdown = !s.LaunchCountdown));
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
        AddRow("Open the browser", glyph: "", click: () => Host.Push(new BrowserScreen()));
        AddRow("Show its button on the home screen", OnOff(s.ShowBrowserButton), "", click: () =>
            Toggle(() => s.ShowBrowserButton = !s.ShowBrowserButton));
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
        AddRow("Device information", glyph: "", click: () => Host.Push(new DeviceInfoScreen()));
        AddRow("Restart to UEFI / BIOS setup", glyph: "\uE950", click: () => _ = Host.RestartToFirmwareAsync());
        AddRow("Format a USB drive or memory card", glyph: "", click: () => Host.Push(new StorageScreen()));

        AddHeader("Tools");
        AddRow("Task Manager", "Apps, processes, performance", "", click: () => Host.Push(new TaskManagerScreen()));
        AddRow("File explorer", glyph: "", click: () => Host.Push(new ExplorerScreen()));
        AddRow("Xbox mode home app", AnyFse.IsInstalled() && AnyFse.LaunchesUs() && AnyFse.IsHomeApp() ? "SquareDeck" : "Set up", "\uE7FC",
            click: () => Host.Push(new AnyFseScreen()), subtitle: "Open SquareDeck as the full screen experience's home, through AnyFSE");
        if (AllyButtons.Model.Length > 0 || AllyButtons.IsSupported)
            AddRow("ROG Ally buttons", s.AllyRemap ? "Remapped" : "Off", "\uE765", click: () => Host.Push(new AllyButtonsScreen()),
                subtitle: "Armoury Crate, Command Center and Mode+ buttons");

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
        int choice = await Host.AskAsync("Closing animation", "Plays when you choose Exit to desktop from the Power menu.",
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
    public static async Task EditSteamKeyAsync(MainWindow host, Action done)
    {
        var key = await host.PromptAsync("Steam Web API key",
            "Get your own free key at steamcommunity.com/dev/apikey (any domain name works), then paste it here. " +
            "It is stored only on this PC. Leave it empty to remove it.",
            host.Settings.SteamWebApiKey);
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
                "until you choose Power \u2192 Exit to desktop. Programs that normally start with Windows will not start " +
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

    public override void OnResumed() => Rebuild(); // values may have changed on a sub-page

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
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");
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
