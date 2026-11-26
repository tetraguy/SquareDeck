using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Staying out of the way. While SquareDeck can't be seen (behind a game, behind another app covering the screen, or
/// minimized), the launcher stops drawing altogether, checks the controller less often, gives up its CPU priority and
/// hands memory back to Windows. Everything comes back the moment you return to it.
/// </summary>
public partial class MainWindow
{
    private bool _backgroundMode;
    private Grid? _heroHost, _wallpaperHost;

    private void InitPower()
    {
        Activated += (_, _) => { UpdateBackgroundMode(); UpdateFocusPause(); };
        Deactivated += (_, _) => { UpdateBackgroundMode(); UpdateFocusPause(); };
        StateChanged += (_, _) => UpdateBackgroundMode();
        Perf.Update(Settings);
        TdpControl.Init(Settings);
        AllyControl.Init(Settings);
        _ = HandheldVendors.ApplyAllAsync(Settings); // Legion Go / MSI Claw: mode, fan boost, charge limit
        Brightness.Prefetch(); // so the quick settings show the brightness slider the moment they open
        if (Settings.ExperimentAdrenalineHook && Settings.AdrenalineAutoHook)
        {
            // Adrenaline Hook: new Xbox games go into AMD Software's list, a minute after starting (it scans the Store apps).
            var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            later.Tick += async (_, _) =>
            {
                later.Stop();
                if (AllGames.Any(g => g.IsRunning)) return; // not while playing: AMD Software is closed to save
                int added = await AdrenalineHook.AutoHookAsync();
                if (added > 0) ShowToast("ADRENALINE HOOK", added + (added == 1 ? " new game added to AMD Software" : " new games added to AMD Software"), "", "", "info");
            };
            later.Start();
        }
        LocalMusic.Init(Settings);
        GameGenie.Init(Settings);
        // Genie answers for new games, written in the background (asks the window about games on its own thread).
        GeniePrep.Start(Settings,
            () => Dispatcher.Invoke(() => AllGames.Any(g => g.IsRunning)),
            key => Dispatcher.Invoke(() => AllGames.FirstOrDefault(g => g.Key == key)));
        StyleUnlocks.Init(Settings);
        StartWidgetBridge(); // the Xbox Game Bar widget
        // Background music makes way for Spotify playing inside the launcher.
        SpotifyPlayer.Changed += () => Dispatcher.BeginInvoke(new Action(() => { UpdateMenuMusic(); UpdateNowPlaying(); }));

        // Checked once a minute, also while a game is in front (the clock timer stops then).
        var batteryWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        batteryWatch.Tick += (_, _) => CheckLowBattery();
        batteryWatch.Start();

        // The blurred artwork and wallpaper are kept as ready-made pictures, so the blur isn't worked out again every
        // time something nearby moves (the pulsing highlight used to make it recalculate it every frame).
        WatchCaptures();
        // Xbox mode home app: registered again from this folder if SquareDeck has moved since.
        if (Settings.HomeAppOn)
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Run(() => HomeApp.KeepRegisteredAsync(Settings));
                Settings.Save();
            });
        _heroHost = WrapInCache(HeroImage);
        _wallpaperHost = WrapInCache(WallpaperLayer);
    }

    private Grid? WrapInCache(FrameworkElement element)
    {
        if (element.Parent is not Grid parent) return null;
        int index = parent.Children.IndexOf(element);
        parent.Children.Remove(element);
        var host = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetRowSpan(host, Grid.GetRowSpan(element));
        Grid.SetRow(host, Grid.GetRow(element));
        Grid.SetRowSpan(element, 1);
        Grid.SetRow(element, 0);
        host.Children.Add(element);
        parent.Children.Insert(index, host);
        return host;
    }

    /// <summary>Caches a blurred layer at a reduced size (it's blurry anyway), or not at all when it isn't blurred.</summary>
    private void UpdateLayerCaches()
    {
        if (_heroHost != null) _heroHost.CacheMode = HeroImage.Effect != null ? new BitmapCache(0.5) : null;
        if (_wallpaperHost != null) _wallpaperHost.CacheMode = WallpaperLayer.Effect != null ? new BitmapCache(0.5) : null;
    }

    /// <summary>Settings > Performance mode changed, or the power cable was plugged in or out.</summary>
    public void ApplyPerformance()
    {
        Perf.Update(Settings);
        ApplyLook(); // shadows, glows, blurs and the highlight follow it
        ApplySettings();
    }

    private void CheckPerformancePower()
    {
        TdpControl.CheckPower();
        if (Settings.PerformanceMode == "battery" && Perf.Update(Settings)) ApplyPerformance();
    }

    private int _holdVisible;

    /// <summary>
    /// Keeps the launcher drawn and on top while something must be seen over a game: the launch splash screen and the
    /// "Closing software" pop-up. (Without this, stepping aside for the game hid them, and in Windows' Xbox full
    /// screen experience they ended up behind the shell.)
    /// </summary>
    public void HoldVisible(bool on)
    {
        _holdVisible = Math.Max(0, _holdVisible + (on ? 1 : -1));
        if (on && _holdVisible == 1)
        {
            Topmost = true;
            WindowActivator.ShowOnTop(this);
        }
        else if (!on && _holdVisible == 0)
        {
            Topmost = false;
            WindowActivator.ClearOnTop(this);
        }
        UpdateBackgroundMode();
    }

    private bool _comingBack;

    /// <summary>
    /// After a game closes: SquareDeck in front with the keyboard focus. The controller only drives SquareDeck while its
    /// window is the active one, and Windows often refuses a plain request to come forward (the closing game, its store
    /// or the desktop keeps the focus), so it uses the same firm method as the launch splash and tries a few times while
    /// Windows settles. Then the highlight goes back where it was.
    /// </summary>
    private async void ComeBackAfterGame()
    {
        if (_comingBack) return;
        _comingBack = true;
        try
        {
            int[] waits = { 0, 250, 600, 1200, 2000 };
            foreach (var wait in waits)
            {
                if (wait > 0) await Task.Delay(wait);
                if (AllGames.Any(g => g.IsRunning)) return; // another game started meanwhile
                if (IsActive && WindowActivator.IsInFront(this)) break;
                WindowActivator.ShowOnTop(this);
                if (_holdVisible == 0) WindowActivator.ClearOnTop(this); // in front, not stuck on top of what comes next
            }
            if (!IsActive) return;
            _gamepad.Reset(); // no buttons left "held" from the game
            UpdateBackgroundMode();
            if (_screens.Count > 0) _screens[^1].FocusDefault();
            else Keyboard.Focus(this);
            if (_screens.Count == 0) ReturnAnimationEight(); // Experimental: back on the game you launched
        }
        catch (Exception) { }
        finally { _comingBack = false; }
    }

    private DateTime _lastFrontGrab;

    /// <summary>
    /// While the launch splash is held in front: back on top of every other window, and, until the game itself is
    /// running, the focus taken back from a store that grabbed it (at most about once a second).
    /// </summary>
    public void KeepInFront(bool takeFocus)
    {
        if (_holdVisible == 0) return;
        WindowActivator.KeepOnTop(this);
        if (takeFocus && !WindowActivator.IsInFront(this) && DateTime.UtcNow - _lastFrontGrab > TimeSpan.FromSeconds(1.2))
        {
            _lastFrontGrab = DateTime.UtcNow;
            WindowActivator.ShowOnTop(this);
        }
    }

    // ================================================================ captures named after the game

    private FileSystemWatcher? _capturesWatcher;

    /// <summary>
    /// Windows names each Game Bar capture after the app in front. If SquareDeck was in front for a moment (its quick
    /// menu, a notice), a screenshot of the game came out as "SquareDeck 2026-11-16 08-30-12.png". New captures named
    /// that way while a game is running are renamed after the game, so they land in its album and on its page.
    /// </summary>
    private void WatchCaptures()
    {
        try
        {
            var folder = Screens.CapturesScreen.Folder;
            Directory.CreateDirectory(folder);
            _capturesWatcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            _capturesWatcher.Created += (_, e) =>
            {
                if (!Path.GetFileName(e.Name ?? "").StartsWith("SquareDeck ", StringComparison.OrdinalIgnoreCase)) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var game = AllGames.FirstOrDefault(g => g.IsRunning);
                    if (game != null) _ = RenameCaptureAsync(e.FullPath, game.Name);
                }));
            };
        }
        catch (Exception) { } // no Captures folder access: captures keep Windows' names
    }

    private static async Task RenameCaptureAsync(string path, string gameName)
    {
        var bad = Path.GetInvalidFileNameChars();
        string safe = new string(gameName.Select(c => bad.Contains(c) ? ' ' : c).ToArray()).Trim();
        if (safe.Length == 0) return;
        string dir = Path.GetDirectoryName(path)!, file = Path.GetFileName(path);
        string target = Path.Combine(dir, safe + file["SquareDeck".Length..]);
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(dir, safe + Path.GetFileNameWithoutExtension(file)["SquareDeck".Length..] + $" ({n})" + Path.GetExtension(file));

        // Windows is still writing it (a recording, for as long as it lasts): try again until it lets go.
        bool video = Screens.CapturesScreen.IsVideo(path);
        for (int attempt = 0; attempt < (video ? 1200 : 40); attempt++)
        {
            await Task.Delay(video ? 3000 : 500);
            if (!File.Exists(path)) return;
            try { File.Move(path, target); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    private bool _gameMemoryMode;

    /// <summary>
    /// While a game runs, SquareDeck gives back as much memory as it can, every time (not only with "Free graphics
    /// memory for the game" on):
    ///  - the moving background is unloaded completely (paused, a video still keeps its decoder and frames in memory);
    ///  - the menu music player is closed (and later carries on from where it was);
    ///  - a browser kept open in the background is closed (its web pages can use hundreds of megabytes);
    ///  - the game artwork and animated cover are let go, and the memory is handed back to Windows.
    /// Everything comes back when the game closes.
    /// </summary>
    private void UpdateGameMemory()
    {
        bool game = AllGames.Any(g => g.IsRunning);
        if (game == _gameMemoryMode) return;
        _gameMemoryMode = game;
        if (game)
        {
            StopDynamicBackground(); // the setting stays; only the video is unloaded
            SuspendMenuMusic(); // closed to save memory; it picks up from the same spot afterwards
            if (!_screens.OfType<Screens.BrowserScreen>().Any()) Screens.BrowserScreen.DisposeKept();
            // Video pages use a lot of memory; a music service keeps playing (music while you play is the point of it).
            if (!_screens.OfType<Screens.StreamingScreen>().Any() && Screens.StreamingScreen.Current?.Service.Music != true) Screens.StreamingScreen.CloseKept();
            if (_animatedGame != null) _animatedGame.AnimatedCover = null;
            ClearHero();
            Motion.StopPulse(RingOf(GameList.SelectedItem));
            // Compact the memory (large pictures included) and hand it back to Windows.
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ProcessUtil.TrimWorkingSet();
        }
        else
        {
            RestoreDynamicBackground();
            UpdateMenuMusic();
            // The game closed: once SquareDeck has settled, any tile left without its art gets it (lean mode does this
            // itself when it ends; this catches a game closed before lean mode began, or while SquareDeck stayed behind).
            _ = Dispatcher.InvokeAsync(async () => { await Task.Delay(2500); RestoreMissingArt(); });
            _heroKey = null;
            if (Settings.HeroBackgrounds) _ = ShowHeroAsync((GameList.SelectedItem as Game)?.Actual);
        }
    }

    private int _lowBatteryWarnedAt = 101;

    /// <summary>Low battery: one notification at the chosen level and one more at 5%, again after the next charge.</summary>
    private void CheckLowBattery()
    {
        var percent = Battery.GetPercent();
        if (percent == null || Battery.IsPluggedIn()) { _lowBatteryWarnedAt = 101; return; }
        if (!Settings.LowBatteryAlert) return;
        int p = percent.Value;
        int level = p <= 5 ? 5 : p <= Settings.LowBatteryLevel ? Settings.LowBatteryLevel : 101;
        if (level == 101 || level >= _lowBatteryWarnedAt) return;
        _lowBatteryWarnedAt = level;
        string title = p <= 5 ? "Battery nearly empty" : "Battery low";
        string detail = $"{p}% left. Plug in the charger" + (p <= 5 ? " now, or save your game." : ".");
        if (AllGames.Any(g => g.IsRunning) && !IsActive) GameNotice.Show(title, detail, "\uEBA0"); // over the game
        else ShowToast("Battery", title, detail, "", "info");
        UiSound.Battery(critical: p <= 5);
        CompanionReact("worried");
    }

    private bool _focusPaused;

    /// <summary>
    /// With another app in front, SquareDeck rests: the menu music and moving background pause (see
    /// UpdateMenuMusic and UpdateDynamicPlayback), an animated cover stops, and the highlight stops pulsing.
    /// It all carries on when SquareDeck is back in front. The screen itself stays drawn, so a pop-up from another
    /// app over the launcher doesn't leave a blank screen behind it.
    /// </summary>
    private void UpdateFocusPause()
    {
        UpdateMenuMusic();
        bool pause = !IsActive;
        if (pause == _focusPaused) return;
        _focusPaused = pause;
        if (pause)
        {
            Motion.StopPulse(RingOf(GameList.SelectedItem));
            if (_animatedGame != null)
            {
                _animatedGame.AnimatedCover = null; // the video is let go; it comes back when the game is highlighted again
                _animatedGame = null;
            }
        }
        else if (!_backgroundMode)
        {
            if (!Theme.ReduceMotion) Motion.StartPulse(RingOf(GameList.SelectedItem));
            _ = ShowAnimatedCoverAsync((GameList.SelectedItem as Game)?.Actual);
        }
    }

    private void UpdateBackgroundMode()
    {
        bool gameRunning = AllGames.Any(g => g.IsRunning);
        // Out of sight: minimized, behind a game, or behind another app covering the screen (see MainWindow.Rest).
        bool background = _holdVisible == 0 && (WindowState == WindowState.Minimized || (!IsActive && (gameRunning || _coveredByApp)));
        if (background == _backgroundMode) { UpdateLean(background); return; }
        _backgroundMode = background;
        UpdateLean(background);
        PauseAmbient(background);

        if (background)
        {
            // Nothing on screen to draw, so nothing for the graphics chip to do.
            RootGrid.Visibility = Visibility.Hidden;
            _padTimer.Interval = TimeSpan.FromMilliseconds(50); // still quick enough for the Guide button
            _clockTimer.Stop();
            _heroTimer.Stop();
            // Housekeeping slows right down: everything goes to the game. (The widget and the Guide button still answer.)
            _idleTimer.Interval = TimeSpan.FromSeconds(10);
            _updateTimer.Interval = TimeSpan.FromMinutes(2);
            _connectionTimer.Interval = TimeSpan.FromSeconds(30);
            _musicTimer.Interval = TimeSpan.FromSeconds(10);
            if (gameRunning) foreach (var screen in _screens) screen.PauseTimers(); // every open page, the one on top too
            SquareDeck.Screens.BrowserScreen.SleepKept();
            SquareDeck.Screens.StreamingScreen.SleepKept();
            Motion.StopPulse(RingOf(GameList.SelectedItem));
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
            ProcessUtil.TrimWorkingSet();
        }
        else
        {
            RootGrid.Visibility = Visibility.Visible;
            _padTimer.Interval = TimeSpan.FromMilliseconds(_resting ? 50 : 16);
            _idleTimer.Interval = TimeSpan.FromSeconds(2);
            _updateTimer.Interval = TimeSpan.FromSeconds(30);
            _connectionTimer.Interval = TimeSpan.FromSeconds(10);
            _musicTimer.Interval = TimeSpan.FromSeconds(3);
            if (_screens.Count > 0) _screens[^1].ResumeTimers(); // the pages underneath wait until they're back on top
            _clockTimer.Start();
            UpdateClock();
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch (Exception) { }
            if (!Theme.ReduceMotion) Motion.StartPulse(RingOf(GameList.SelectedItem));
        }
    }
}

/// <summary>
/// A small notice in the corner of the screen over a running game (the launcher itself is in the background then).
/// It doesn't take the keyboard or controller away from the game.
/// </summary>
public sealed class GameNotice : Window
{
    private static GameNotice? _current;

    public static void Show(string title, string detail, string glyph)
    {
        _current?.Close();
        _current = new GameNotice(title, detail, glyph);
        _current.Show();
    }

    private GameNotice(string title, string detail, string glyph)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        var area = SystemParameters.WorkArea;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD4, 0xDE)), MaxWidth = 320, TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 28, Margin = new Thickness(0, 0, 16, 0),
                                         Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5B)), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(text);
        Content = new Border
        {
            Child = row, Padding = new Thickness(18, 14, 22, 14), Margin = new Thickness(12), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x16, 0x19, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
        };
        Loaded += (_, _) => { Left = area.Right - ActualWidth - 24; Top = area.Top + 24; };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
        Closed += (_, _) => { if (_current == this) _current = null; };
    }
}
