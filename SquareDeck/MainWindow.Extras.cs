using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

// The second half of the main window: file pickers, notifications, sessions and profiles,
// the not-installed list and install queue, touch gestures, the now-playing bar, and first-run setup.
public partial class MainWindow
{
    private readonly DispatcherTimer _musicTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _friendTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private bool _skipSaveOnClose;

    /// <summary>Called once from the constructor, after the window's own parts exist.</summary>
    private void InitExtras()
    {
        // A crash while a game was running could have left Windows notifications switched off.
        if (Settings.ToastsSuppressed)
        {
            DoNotDisturb.Set(false);
            Settings.ToastsSuppressed = false;
            Settings.Save();
        }

        UpdateBell();
        _clockTimer.Interval = TimeSpan.FromSeconds(Settings.ClockSeconds ? 1 : 5);

        _musicTimer.Tick += (_, _) => UpdateNowPlaying();
        _musicTimer.Start();
        UpdateNowPlaying();

        _friendTimer.Tick += (_, _) => _ = CheckFriendsAsync();
        _friendTimer.Start();
        _ = CheckFriendsAsync();

        _updateReminder.Tick += (_, _) => _ = CheckWindowsUpdateAsync(atLaunch: false);
        _updateReminder.Start();
        SourceInitialized += (_, _) => { ApplySummonKey(); ApplyMainDisplay(); ApplyAllyButtons(); };
        Loaded += (_, _) => ApplyMainDisplay(); // again once shown, in case Windows placed it elsewhere

        PreviewTouchDown += OnTouchDown;
        PreviewTouchUp += OnTouchUp;

        if (Settings.CacheLimitMb > 0)
        {
            long limit = Settings.CacheLimitMb * 1024L * 1024L;
            Task.Run(() => CacheControl.Trim(limit));
        }
    }

    private void OnClosedExtras()
    {
        _overlay?.Close();
        UnregisterSummonKey();
        if (_activeLaunch != null) _ = RestoreLaunchAsync(); // put display / sound / notifications back
        if (_skipSaveOnClose) return;

        SaveEverything();
        if (Settings.BackupFolder.Length > 0)
        {
            try { Backup.Create(Settings.BackupFolder, dated: false); }
            catch (Exception) { /* the folder may be offline; the next close tries again */ }
        }
    }

    /// <summary>
    /// After restoring a backup made on another PC, the profile picture and wallpaper paths point at
    /// that PC's folders. The files themselves came across, so look for them in our own data folder.
    /// </summary>
    private void RelocateSavedPaths()
    {
        if (!Settings.Avatar.StartsWith("preset:", StringComparison.Ordinal))
            Settings.Avatar = Backup.Relocate(Settings.Avatar, Settings.DataDir) ?? "preset:0";
        if (Settings.Wallpaper.Length > 0 && !Settings.Wallpaper.StartsWith("preset:", StringComparison.Ordinal))
            Settings.Wallpaper = Backup.Relocate(Settings.Wallpaper, Settings.DataDir) ?? "";
    }

    public void SaveEverything()
    {
        Settings.Save();
        Library.Save();
        Playtime.Save();
        Achievements.Save();
        Notifications.Save();
    }

    /// <summary>Starts a fresh copy of the launcher and closes this one, leaving the data files as they are on disk.</summary>
    public void RestartWithoutSaving()
    {
        _skipSaveOnClose = true;
        try
        {
            if (Environment.ProcessPath is string exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception) { }
        Close();
    }

    // ================================================================ pickers

    /// <summary>Lets the user choose a file with the controller. Null if they backed out.</summary>
    public async Task<string?> PickFileAsync(string title, string[] extensions)
    {
        var picker = new FilePickerScreen(title, extensions);
        Push(picker);
        return await picker.Result;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var picker = new FilePickerScreen(title, null);
        Push(picker);
        return await picker.Result;
    }

    // ================================================================ notifications

    public NotificationStore Notifications { get; } = NotificationStore.Load();

    public void UpdateBell()
    {
        int unread = Notifications.Unread;
        BellBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellCount.Text = unread > 9 ? "9+" : unread.ToString();
    }

    private void Bell_Click(object sender, RoutedEventArgs e) => Push(new NotificationsScreen());

    private bool _lowWarned, _criticalWarned;

    /// <summary>Warns once at 15% and once at 5% each time the battery runs down (bottom-right pop-up).</summary>
    private void CheckBattery(int percent, bool pluggedIn)
    {
        if (pluggedIn || percent > 20)
        {
            _lowWarned = _criticalWarned = false;
            return;
        }
        if (percent <= 5 && !_criticalWarned)
        {
            _criticalWarned = _lowWarned = true;
            ShowToast("BATTERY", $"Battery at {percent}%", "Plug in now to avoid losing progress.", "", "battery");
        }
        else if (percent <= 15 && !_lowWarned)
        {
            _lowWarned = true;
            ShowToast("LOW BATTERY", $"Battery at {percent}%", "Plug in soon to keep playing.", "", "battery");
        }
    }

    // ================================================================ Windows Update reminder

    // First look half a minute after starting, then every half hour until it has been said once.
    private readonly DispatcherTimer _updateReminder = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _updateWarned, _updateWarnedAtLaunch;

    /// <summary>Says so when Windows is waiting to restart for an update, which could interrupt a game.</summary>
    private async Task CheckWindowsUpdateAsync(bool atLaunch)
    {
        if (!atLaunch) _updateReminder.Interval = TimeSpan.FromMinutes(30);
        if (atLaunch ? _updateWarnedAtLaunch : _updateWarned) return;

        bool pending;
        try { pending = await Task.Run(WindowsUpdate.RestartPending); }
        catch (Exception) { return; }
        if (!pending) return;

        if (atLaunch) _updateWarnedAtLaunch = true;
        else { _updateWarned = true; _updateReminder.Stop(); }
        ShowToast("WINDOWS UPDATE", "A restart is pending",
            atLaunch ? "Windows may restart during this session to finish updating."
                     : "Restart from the Power menu before a long session.", "", "info");
    }

    // ================================================================ keyboard shortcut for the quick menu

    public static readonly (string Id, string Name)[] SummonKeys =
    {
        ("none", "None"), ("f9", "F9"), ("f10", "F10"), ("ctrl+alt+q", "Ctrl + Alt + Q"), ("ctrl+shift+space", "Ctrl + Shift + Space")
    };

    private const int SummonHotkeyId = 0x5144;
    private System.Windows.Interop.HwndSource? _hotkeySource;
    private bool _hotkeyRegistered;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void UnregisterSummonKey()
    {
        if (!_hotkeyRegistered || _hotkeySource == null) return;
        try { UnregisterHotKey(_hotkeySource.Handle, SummonHotkeyId); } catch (Exception) { }
        _hotkeyRegistered = false;
    }

    /// <summary>Registers (or removes) the system-wide key that works like the controller's summon buttons.</summary>
    private void ApplySummonKey()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return; // too early; called again once the window exists
        if (_hotkeySource == null)
        {
            _hotkeySource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            _hotkeySource?.AddHook(HotkeyHook);
        }
        UnregisterSummonKey();

        const uint Alt = 1, Control = 2, Shift = 4, NoRepeat = 0x4000;
        (uint Modifiers, uint Key)? combo = Settings.SummonKey switch
        {
            "f9" => (0, 0x78),
            "f10" => (0, 0x79),
            "ctrl+alt+q" => (Control | Alt, 0x51),
            "ctrl+shift+space" => (Control | Shift, 0x20),
            _ => null
        };
        if (combo == null) return;
        try { _hotkeyRegistered = RegisterHotKey(handle, SummonHotkeyId, combo.Value.Modifiers | NoRepeat, combo.Value.Key); }
        catch (Exception) { _hotkeyRegistered = false; }
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (message == WmHotkey && wParam.ToInt32() == SummonHotkeyId)
        {
            handled = true;
            Summon();
        }
        else if (message == AllyButtons.WmInput && _allyListening && AllyButtons.Read(lParam) is string press)
        {
            OnAllyButton(press);
        }
        return IntPtr.Zero;
    }

    // ================================================================ ROG Ally buttons

    private bool _allyListening;

    /// <summary>Starts or stops listening to the Ally's extra buttons, as Settings says.</summary>
    public void ApplyAllyButtons()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (_hotkeySource == null) ApplySummonKey(); // sets up the message hook
        bool want = Settings.AllyRemap;
        if (want == _allyListening) return;
        _allyListening = AllyButtons.Listen(handle, want) && want;
    }

    private void OnAllyButton(string press)
    {
        string? action;
        if (press is "mic" or "screenshot" or "keyboard" or "record")
        {
            if (!Settings.AllyExtraKeys) return;
            action = press;
        }
        else if (!Settings.AllyBindings.TryGetValue(press, out action) || string.IsNullOrEmpty(action)) return;
        RunAllyAction(action);
    }

    private void RunAllyAction(string action)
    {
        const ushort Win = 0x5B, Tab = 0x09, D = 0x44;
        switch (action)
        {
            case "home":
                WindowActivator.BringToFront(this);
                if (_screens.Count == 0) SetMode(HomeView.Games);
                break;
            case "quickmenu": Summon(); break;
            case "taskswitcher": ProcessUtil.Open("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}"); break;
            case "taskview": AllyButtons.SendKeys(Win, Tab); break;
            case "desktop": AllyButtons.SendKeys(Win, D); break;
            case "gamebar": ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_GAME_GUIDE_OVERLAY&source=GbEm"); break;
            case "commandcenter": ProcessUtil.Open("ms-gamebar://launchForeground/activate/B9ECED6F.ASUSCommandCenter_qmba6cd70vzyy_App_Widget"); break;
            case "armourycrate": ProcessUtil.Open("asusac://"); break;
            case "screenshot": ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_CAPTURE_GAME_SCREENSHOT&source=GbEm"); break;
            case "record": ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_TOGGLE_GAME_RECORDING&source=GbEm"); break;
            case "mic": ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_ALT_TOGGLE_MICROPHONE_CAPTURE&source=GbEm"); break;
            case "keyboard":
                var tabTip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");
                if (!File.Exists(tabTip) || !ProcessUtil.Open(tabTip)) ProcessUtil.Open("osk.exe");
                break;
            case "taskmanager":
                WindowActivator.BringToFront(this);
                Push(new TaskManagerScreen());
                break;
            case "nightlight":
                if (NightLight.IsOn is bool on) NightLight.Set(!on);
                break;
        }
    }

    /// <summary>What the summon buttons do: the quick menu over a running game, otherwise the launcher itself.</summary>
    private void Summon()
    {
        if (_overlay is { IsVisible: true } open) { open.Dismiss(); return; }
        if (Settings.InGameQuickMenu && AllGames.Any(g => g.IsRunning) && !IsActive) ToggleOverlay();
        else WindowActivator.BringToFront(this);
    }

    // ================================================================ explorer

    private void Explorer_Click(object sender, RoutedEventArgs e) => Push(new ExplorerScreen());

    private Dictionary<string, (bool Online, string? Game)>? _friendStates;
    private readonly Dictionary<string, ImageSource> _friendPictures = new();

    private async Task<ImageSource?> FriendPictureAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_friendPictures.TryGetValue(url, out var cached)) return cached;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // The larger version of the picture where Steam has one.
                string address = attempt == 0 ? url.Replace("_medium.", "_full.") : url;
                var picture = await Task.Run(() => ImageLoader.FromUrlAsync(address, 128));
                if (picture != null) return _friendPictures[url] = picture;
            }
            catch (Exception) { }
        }
        return null;
    }

    /// <summary>Notes friends who came online or started a game since the last look.</summary>
    private async Task CheckFriendsAsync()
    {
        var key = Settings.SteamWebApiKey;
        if (key.Length == 0) return;
        try
        {
            var (friends, _) = await Task.Run(() => SteamFriends.GetAsync(key, CancellationToken.None));
            if (friends == null) return;

            var now = friends.ToDictionary(f => f.Id, f => (f.IsOnline, f.Game));
            if (_friendStates != null) // the first look only sets the baseline
            {
                foreach (var f in friends)
                {
                    _friendStates.TryGetValue(f.Id, out var before);
                    string? news = f.InGame && before.Game != f.Game ? "is playing " + f.Game
                        : f.IsOnline && !before.Online ? "is now online" : null;
                    if (news == null) continue;
                    // Their profile picture is fetched first, so it is already there when the pop-up appears.
                    ShowToast("FRIEND", f.Name, news, f.Avatar, "friend", await FriendPictureAsync(f.Avatar));
                }
            }
            _friendStates = now;
        }
        catch (Exception) { }
    }

    // ================================================================ controller extras

    public Gamepad.RawState PadState => _gamepad.GetRaw();

    /// <summary>A longer buzz at a given strength, for the test page and the strength slider.</summary>
    public void RumbleTest(int percent) => _gamepad.Pulse(160, percent);

    /// <summary>Right stick: scroll the page under the highlight without moving the highlight.</summary>
    private void ScrollWithRightStick()
    {
        double y = _gamepad.RightStickY;
        if (y == 0 || _screens.Count == 0) return;
        var top = _screens[^1];
        if (top.OnRightStick(y)) return; // the browser scrolls its own page

        ScrollViewer? viewer = null;
        if (Keyboard.FocusedElement is DependencyObject focused && top.IsAncestorOf(focused as Visual ?? top))
        {
            for (var d = focused; d != null && d != top; d = VisualTreeHelper.GetParent(d))
                if (d is ScrollViewer { ScrollableHeight: > 0 } sv) { viewer = sv; break; }
        }
        viewer ??= FindScrollable(top);
        // Faster the further the stick is pushed.
        viewer?.ScrollToVerticalOffset(viewer.VerticalOffset - y * Math.Abs(y) * 26);
    }

    private static ScrollViewer? FindScrollable(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer { ScrollableHeight: > 0 } sv) return sv;
            if (FindScrollable(child) is { } nested) return nested;
        }
        return null;
    }

    // ================================================================ touch gestures

    private Point _touchStart;
    private DateTime _touchStartTime;

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        _touchStart = e.GetTouchPoint(this).Position;
        _touchStartTime = DateTime.UtcNow;
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        var end = e.GetTouchPoint(this).Position;
        double dx = end.X - _touchStart.X, dy = end.Y - _touchStart.Y;
        if (DateTime.UtcNow - _touchStartTime > TimeSpan.FromMilliseconds(700)) return; // a drag, not a flick

        // Pull down from the top edge: quick settings.
        if (_touchStart.Y < ActualHeight * 0.12 && dy > ActualHeight * 0.15 && Math.Abs(dy) > Math.Abs(dx) * 1.5)
        {
            if (_screens.Count == 0 || !_screens[^1].IsDialog) Push(new QuickSettingsScreen());
            return;
        }

        bool sideways = Math.Abs(dx) > ActualWidth * 0.10 && Math.Abs(dx) > Math.Abs(dy) * 2;
        if (!sideways) return;
        int direction = dx < 0 ? +1 : -1; // swipe left = go forward

        // On the home row a swipe scrolls through the tiles; anywhere else it turns the page.
        if (_screens.Count == 0 && IsOverGameRow(_touchStart))
        {
            MoveSelection(direction * 3);
            return;
        }
        int page = CurrentPage();
        if (page < 0) return;
        UiSound.Page();
        GoToPage(page + direction, direction);
    }

    private bool IsOverGameRow(Point windowPoint)
    {
        try
        {
            var topLeft = GameList.TransformToAncestor(this).Transform(new Point(0, 0));
            var bottomRight = GameList.TransformToAncestor(this).Transform(new Point(GameList.ActualWidth, GameList.ActualHeight));
            return windowPoint.Y >= topLeft.Y && windowPoint.Y <= bottomRight.Y;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ================================================================ page transitions

    /// <summary>Slides something in from the side it was reached from (+1 from the right, -1 from the left).</summary>
    private static void SlideIn(UIElement element, int direction)
    {
        if (direction == 0 || Theme.ReduceMotion) return;
        var shift = new TranslateTransform(direction * 70, 0);
        element.RenderTransform = shift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(230);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 70, 0, time) { EasingFunction = ease });
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, time) { EasingFunction = ease });
    }

    // ================================================================ per-game accent

    private async Task ApplyGameAccentAsync(Game? game)
    {
        if (!Settings.GameAccent || game?.Cover is not BitmapSource cover)
        {
            Theme.ApplyAccent(Settings);
            return;
        }
        var color = await Task.Run(() => ImageLoader.DominantColor(cover));
        if (!ReferenceEquals((GameList.SelectedItem as Game)?.Actual, game)) return; // the highlight moved on
        if (color is Color c) Theme.TintAccent(c);
        else Theme.ApplyAccent(Settings);
    }

    // ================================================================ now-playing bar

    private async void UpdateNowPlaying()
    {
        try
        {
            var now = await MediaService.GetAsync(includeThumbnail: false);
            if (now == null)
            {
                MusicButton.Visibility = Visibility.Collapsed;
                if (_footerIndex >= 0) SetFooterIndex(Math.Min(_footerIndex, FooterButtons().Count - 1));
                return;
            }
            MusicButton.Visibility = Visibility.Visible;
            MusicGlyph.Text = now.IsPlaying ? "" : "";
            MusicText.Text = now.Artist.Length > 0 ? now.Title + "  –  " + now.Artist : now.Title;
        }
        catch (Exception) { }
    }

    private async void MusicToggle_Click(object sender, RoutedEventArgs e)
    {
        await MediaService.PlayPauseAsync();
        await Task.Delay(250);
        UpdateNowPlaying();
    }

    // ================================================================ footer focus (music, Stop)

    private int _footerIndex = -1;
    private bool _stopFocused => _footerIndex >= 0;

    private List<Button> FooterButtons() =>
        new[] { MusicButton, StopButton }
            .Where(b => b == StopButton ? RunningPanel.Visibility == Visibility.Visible : b.Visibility == Visibility.Visible)
            .ToList();

    private void SetFooterIndex(int index)
    {
        var buttons = FooterButtons();
        if (index >= buttons.Count) index = buttons.Count - 1;
        if (index >= 0 && index != _footerIndex) Rumble();
        _footerIndex = index;

        MusicButton.Tag = StopButton.Tag = null;
        UpdateHomeRing();
        if (index < 0) return;
        buttons[index].Tag = "selected";
        if (buttons[index] == StopButton)
        {
            var running = AllGames.FirstOrDefault(g => g.IsRunning);
            ButtonLabel.Text = running == null ? "Stop" : "Stop " + running.Name;
        }
        else ButtonLabel.Text = "Play / pause";
    }

    /// <summary>Kept for the callers that only care about Stop: true highlights it, false leaves the footer.</summary>
    private void SetStopFocus(bool focused)
    {
        if (!focused) { SetFooterIndex(-1); return; }
        int stop = FooterButtons().IndexOf(StopButton);
        SetFooterIndex(stop >= 0 ? stop : 0);
    }

    private void HandleFooter(NavAction action)
    {
        var buttons = FooterButtons();
        if (buttons.Count == 0) { SetFooterIndex(-1); SetButtonIndex(_stopReturnIndex); return; }
        switch (action)
        {
            case NavAction.Left: SetFooterIndex(Math.Max(0, _footerIndex - 1)); break;
            case NavAction.Right: SetFooterIndex(Math.Min(buttons.Count - 1, _footerIndex + 1)); break;
            case NavAction.Up:
            case NavAction.Back:
                SetFooterIndex(-1);
                SetButtonIndex(_stopReturnIndex);
                break;
            case NavAction.Accept:
                var target = buttons[Math.Clamp(_footerIndex, 0, buttons.Count - 1)];
                if (target == StopButton) SetFooterIndex(-1);
                target.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                break;
        }
    }

    // ================================================================ sessions, profiles, do-not-disturb

    private readonly Dictionary<string, DateTime> _sessionStart = new();
    private readonly Dictionary<int, double> _pendingSummary = new();

    private sealed class ActiveLaunch
    {
        public required Game Game;
        public GameProfiles.Previous? Previous;
        public bool Quiet;
        public DateTime At = DateTime.UtcNow;
        public bool SeenRunning;
    }

    private ActiveLaunch? _activeLaunch;

    /// <summary>Just before a game starts: switch to its profile and silence notifications, if asked to.</summary>
    private async Task PrepareLaunchAsync(Game game)
    {
        if (_activeLaunch != null) await RestoreLaunchAsync();
        var launch = new ActiveLaunch { Game = game };

        var profile = Library.Find(game.Key)?.Profile;
        if (profile is { IsEmpty: false })
        {
            try { launch.Previous = await GameProfiles.ApplyAsync(profile); }
            catch (Exception) { }
        }

        if (Settings.DoNotDisturb && DoNotDisturb.Set(true))
        {
            launch.Quiet = true;
            Settings.ToastsSuppressed = true;
            Settings.Save();
        }

        if (launch.Previous != null || launch.Quiet) _activeLaunch = launch;
    }

    /// <summary>Puts back whatever <see cref="PrepareLaunchAsync"/> changed.</summary>
    private async Task RestoreLaunchAsync()
    {
        var launch = _activeLaunch;
        _activeLaunch = null;
        if (launch == null) return;

        if (launch.Quiet)
        {
            DoNotDisturb.Set(false);
            Settings.ToastsSuppressed = false;
            Settings.Save();
        }
        if (launch.Previous != null)
        {
            try { await GameProfiles.RestoreAsync(launch.Previous); }
            catch (Exception) { }
        }
    }

    /// <summary>Notices games starting and stopping: session records, summaries and profile clean-up.</summary>
    private void TrackSessions()
    {
        var now = DateTime.Now;
        var running = AllGames.Where(g => g.IsRunning).ToDictionary(g => g.Key);

        foreach (var (key, game) in running)
        {
            if (_sessionStart.ContainsKey(key)) continue;
            _sessionStart[key] = now;
            if (_activeLaunch != null && ReferenceEquals(_activeLaunch.Game, game)) _activeLaunch.SeenRunning = true;
        }

        foreach (var key in _sessionStart.Keys.Where(k => !running.ContainsKey(k)).ToList())
        {
            var started = _sessionStart[key];
            _sessionStart.Remove(key);
            double seconds = (now - started).TotalSeconds;
            var game = AllGames.FirstOrDefault(g => g.Key == key);
            string name = game?.Name ?? Playtime.Names.GetValueOrDefault(key, "Game");

            Playtime.AddSession(key, name, started, seconds);
            Playtime.Save();
            if (_activeLaunch != null && _activeLaunch.Game.Key == key) _ = RestoreLaunchAsync();

            // Steam games wait a moment so the summary can include achievements; the rest report straight away.
            if (game is { IsSteam: true } && Settings.AchievementToasts) _pendingSummary[game.AppId] = seconds;
            else ShowSessionSummary(name, seconds, null);
        }

        // A launch that never turned into a running game (cancelled in the store's own window, say).
        if (_activeLaunch is { SeenRunning: false } stale && DateTime.UtcNow - stale.At > TimeSpan.FromMinutes(3))
            _ = RestoreLaunchAsync();
    }

    /// <summary>Called by the achievement check once it knows how many were earned (null: couldn't tell).</summary>
    private void FinishPendingSummary(Game game, int? achievements)
    {
        if (!_pendingSummary.Remove(game.AppId, out var seconds)) return;
        ShowSessionSummary(game.Name, seconds, achievements);
    }

    private async void ShowSessionSummary(string name, double seconds, int? achievements)
    {
        if (!Settings.SessionSummary || seconds < 60) return;
        string text = PlaytimeStore.Format(seconds) + " played";
        if (achievements is > 0) text += $"  \u00B7  {achievements} {(achievements == 1 ? "achievement" : "achievements")} earned";

        var game = AllGames.FirstOrDefault(g => g.Name == name);
        var picture = game == null ? null : await GameIconAsync(game);
        ShowToast("SESSION SUMMARY", name, text, "", "session", picture, game);
    }

    /// <summary>
    /// A game's icon: SteamGridDB's icon artwork, else the icon of its program, else its cover.
    /// Never fails and never waits long, so a pop-up isn't held up by a slow connection.
    /// </summary>
    private async Task<ImageSource?> GameIconAsync(Game game)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var key = Settings.SteamGridDbApiKey;
            var icon = await Task.Run(async () =>
            {
                try
                {
                    using var client = new SteamGridDbClient(key);
                    var path = await client.GetIconAsync(game, limit.Token);
                    if (path != null && ImageLoader.FromFile(path, 128) is { } art) return art;
                }
                catch (Exception) { }

                // The program's own icon, for games SteamGridDB has no icon for (or when offline).
                string? exe = ExactProgram(game);
                if (exe == null && !string.IsNullOrEmpty(game.InstallDir) && Directory.Exists(game.InstallDir))
                {
                    try
                    {
                        exe = Directory.EnumerateFiles(game.InstallDir, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                                        !Path.GetFileName(f).Contains("crash", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                    }
                    catch (Exception) { }
                }
                return exe != null && File.Exists(exe) ? ShellThumb.Get(exe, 64) : null;
            });
            return icon ?? game.Cover;
        }
        catch (Exception)
        {
            return game.Cover;
        }
    }

    // ================================================================ not-installed Steam games, install queue

    /// <summary>Steam games on the account that aren't installed on this PC.</summary>
    public List<Game> NotInstalledGames { get; } = new();

    /// <summary>"nokey", "loading", "ready" or "failed".</summary>
    public string OwnedGamesState { get; private set; } = "nokey";

    public List<Game> InstallQueue { get; } = new();
    private Game? _installing;
    private CancellationTokenSource? _coverCts;
    private bool _progressChecked;

    // Saved copies of the owned-games list and the wishlist, so they show (with counts) without waiting for Steam.
    private readonly GameListCache _lists = GameListCache.Load();
    private bool _listsApplied, _ownedRefreshing, _wishRefreshing;

    /// <summary>Games on the Steam wishlist.</summary>
    public List<Game> WishlistGames { get; } = new();

    /// <summary>"nokey", "loading", "ready" or "failed".</summary>
    public string WishlistState { get; private set; } = "idle";
    public int WishlistTotal => _lists.WishlistTotal;
    public bool OwnedRefreshing => _ownedRefreshing;
    public bool WishlistRefreshing => _wishRefreshing;
    public DateTime OwnedUpdatedAt => _lists.OwnedAt;
    public DateTime WishlistUpdatedAt => _lists.WishlistAt;

    /// <summary>Fills the not-installed list and the wishlist from the saved copies (once).</summary>
    public void ApplyCachedLists()
    {
        if (_listsApplied) return;
        _listsApplied = true;
        if (_lists.Owned.Count > 0)
        {
            BuildNotInstalled(_lists.Owned.Select(o => (o.AppId, o.Name)));
            OwnedGamesState = "ready";
        }
        if (_lists.Wishlist.Count > 0 || _lists.WishlistAt > DateTime.MinValue)
        {
            BuildWishlist();
            WishlistState = "ready";
        }
    }

    /// <summary>
    /// Gets the owned-games list from Steam. With <paramref name="refresh"/> it asks again even when a saved
    /// copy is showing; the saved copy stays on screen until the new one arrives.
    /// </summary>
    public async Task LoadOwnedGamesAsync(bool refresh = false)
    {
        ApplyCachedLists();
        var key = Settings.SteamWebApiKey;
        if (key.Length == 0)
        {
            if (OwnedGamesState != "ready") OwnedGamesState = "nokey";
            return;
        }
        if (_ownedRefreshing || (OwnedGamesState == "ready" && !refresh)) return;

        _ownedRefreshing = true;
        if (OwnedGamesState != "ready") OwnedGamesState = "loading";
        LibraryChanged?.Invoke();
        List<(int AppId, string Name)>? owned = null;
        try { owned = await Task.Run(() => SteamStoreClient.GetOwnedGamesAsync(key, CancellationToken.None)); }
        catch (Exception) { }
        _ownedRefreshing = false;

        if (owned == null)
        {
            if (OwnedGamesState != "ready") OwnedGamesState = "failed"; // keep showing the saved copy if there is one
            LibraryChanged?.Invoke();
            return;
        }

        _lists.Owned = owned.Select(o => new GameListCache.OwnedEntry { AppId = o.AppId, Name = o.Name }).ToList();
        _lists.OwnedAt = DateTime.Now;
        _lists.Save();
        BuildNotInstalled(owned);
        OwnedGamesState = "ready";
        LibraryChanged?.Invoke();
    }

    /// <summary>Rebuilds the not-installed list, keeping the tiles (and covers) of games that were already in it.</summary>
    private void BuildNotInstalled(IEnumerable<(int AppId, string Name)> owned)
    {
        var installed = _steam.Select(g => g.AppId).ToHashSet();
        var before = NotInstalledGames.GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First());
        NotInstalledGames.Clear();
        foreach (var (appId, name) in owned.Where(o => !installed.Contains(o.AppId)))
        {
            if (!before.TryGetValue(appId, out var game))
                game = new Game { Kind = ItemKind.SteamGame, AppId = appId, Name = name, OriginalName = name, IsInstalled = false };
            var o = Library.Find(game.Key);
            game.Name = !string.IsNullOrWhiteSpace(o?.Title) ? o.Title : name;
            game.IsFavorite = o?.Favorite == true;
            NotInstalledGames.Add(game);
        }
    }

    /// <summary>Gets the wishlist from Steam (names and prices only for games it hasn't seen, plus a few to keep prices fresh).</summary>
    public async Task LoadWishlistAsync(bool refresh = false)
    {
        ApplyCachedLists();
        if (_wishRefreshing || (WishlistState == "ready" && !refresh)) return;
        if (SteamLibrary.GetSteamId64() == null)
        {
            WishlistState = "failed";
            LibraryChanged?.Invoke();
            return;
        }

        _wishRefreshing = true;
        if (WishlistState != "ready") WishlistState = "loading";
        LibraryChanged?.Invoke();
        try
        {
            var key = Settings.SteamWebApiKey;
            var ids = await Task.Run(() => SteamWishlist.GetIdsAsync(key, CancellationToken.None));
            if (ids == null)
            {
                if (WishlistState != "ready") WishlistState = "failed";
                return;
            }

            var known = _lists.Wishlist.ToDictionary(w => w.AppId);
            var fresh = new List<GameListCache.WishEntry>();
            foreach (var (appId, added) in ids)
                fresh.Add(known.TryGetValue(appId, out var k) ? k : new GameListCache.WishEntry { AppId = appId, Name = "", Added = added });

            // New games first, then the ones whose price is oldest. Kept small: the store limits how fast it may be asked.
            var toFetch = fresh.Where(w => w.Name.Length == 0).Take(60)
                .Concat(fresh.Where(w => w.Name.Length > 0).OrderBy(w => w.PriceAt).Take(15)).ToList();
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(toFetch.Select(async w =>
            {
                await gate.WaitAsync();
                try
                {
                    var item = await Task.Run(() => SteamWishlist.GetDetailsAsync(w.AppId, w.Added, CancellationToken.None));
                    if (item == null) return;
                    w.Name = item.Name;
                    w.Price = item.Price;
                    w.Discount = item.DiscountPercent;
                    w.PriceAt = DateTime.Now;
                }
                finally { gate.Release(); }
            }));

            _lists.Wishlist = fresh;
            _lists.WishlistTotal = fresh.Count;
            _lists.WishlistAt = DateTime.Now;
            _lists.Save();
            BuildWishlist();
            WishlistState = "ready";
        }
        catch (Exception)
        {
            if (WishlistState != "ready") WishlistState = "failed";
        }
        finally
        {
            _wishRefreshing = false;
            LibraryChanged?.Invoke();
        }
    }

    private void BuildWishlist()
    {
        var owned = AllGames.Where(g => g.IsSteam).Select(g => g.AppId).Concat(NotInstalledGames.Select(g => g.AppId)).ToHashSet();
        var before = WishlistGames.GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First());
        WishlistGames.Clear();
        foreach (var w in _lists.Wishlist.OrderByDescending(w => w.Added))
        {
            if (owned.Contains(w.AppId)) continue; // bought since it was wished for
            string name = w.Name.Length > 0 ? w.Name : "App " + w.AppId;
            if (!before.TryGetValue(w.AppId, out var game))
                game = new Game
                {
                    Kind = ItemKind.SteamGame, AppId = w.AppId, Name = name, OriginalName = name, IsInstalled = false,
                    OnWishlist = true, RecentAt = w.Added
                };
            game.Name = name;
            game.Price = w.Price;
            game.DiscountPercent = w.Discount;
            WishlistGames.Add(game);
        }
    }

    /// <summary>Drops games from the not-installed list once Steam has them installed.</summary>
    private void PruneNotInstalled()
    {
        if (NotInstalledGames.Count == 0) return;
        var installed = _steam.Select(g => g.AppId).ToHashSet();
        NotInstalledGames.RemoveAll(g => installed.Contains(g.AppId));
    }

    /// <summary>Cover size for library tiles: sharp on a 4K screen without holding full-size pictures in memory.</summary>
    private const int LibraryCoverDecode = 320;

    /// <summary>
    /// Fetches covers, in the background, for games that are on screen but have none yet. Saved covers load
    /// straight from disk; games SteamGridDB has no square art for show their icon instead.
    /// </summary>
    public void EnsureCovers(IReadOnlyList<Game> games)
    {
        _coverCts?.Cancel();
        var cts = _coverCts = new CancellationTokenSource();
        var todo = games.Where(g => g.Cover == null && g.IsSteam).ToList();
        if (todo.Count == 0) return;
        _ = FetchCoversAsync(todo, cts.Token);
    }

    private async Task FetchCoversAsync(List<Game> todo, CancellationToken ct)
    {
        try
        {
            using var client = new SteamGridDbClient(Settings.SteamGridDbApiKey);
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(todo.Select(async game =>
            {
                try { await gate.WaitAsync(ct); }
                catch (OperationCanceledException) { return; }
                try
                {
                    string? custom = CustomCoverPath(game);
                    if (SteamGridDbClient.IsIconFile(custom)) { await ShowIconFileAsync(game, custom!); return; }
                    var image = await Task.Run(async () =>
                    {
                        string? path = custom ?? SteamGridDbClient.FindCachedCover(game.AppId);
                        if (path == null)
                        {
                            try { path = await client.GetCoverAsync(game.AppId, ct); }
                            catch (Exception e) when (e is not OperationCanceledException) { }
                        }
                        return path == null ? null : ImageLoader.FromFile(path, LibraryCoverDecode);
                    }, ct);
                    if (ct.IsCancellationRequested) return;
                    if (image != null)
                    {
                        game.Cover = image;
                        game.UsesIcon = false;
                    }
                    else
                    {
                        await UseIconAsync(game, client, ct); // no square cover exists: the game's icon instead
                    }
                }
                catch (Exception)
                {
                    // Offline, a rejected key, or the page changed: the tile keeps its title.
                }
                finally { gate.Release(); }
            }));
        }
        catch (Exception) { }
    }

    /// <summary>What A does on a wishlist game: it isn't owned, so offer the store.</summary>
    private async Task OfferWishlistAsync(Game game)
    {
        string price = game.Price.Length == 0 ? "" : game.DiscountPercent > 0 ? $"{game.Price} ({game.DiscountPercent}% off)" : game.Price;
        int choice = await AskWithIconsAsync(game.Name, price.Length > 0 ? "On your wishlist  \u00B7  " + price : "On your wishlist",
            ("Open the store page in Steam", "\uE7BF"), ("Game details", "\uE946"), ("Cancel", "\uE711"));
        if (choice == 0) ProcessUtil.Open("steam://store/" + game.AppId);
        else if (choice == 1) Push(new DetailsScreen(game));
    }

    private async Task OfferInstallAsync(Game game)
    {
        bool queued = InstallQueue.Contains(game);
        int choice = await AskAsync(game.Name,
            "This game is in your Steam library but isn't installed on this PC. Steam asks you to confirm where to install it.",
            "Install now", queued ? "Remove from the install queue" : "Add to the install queue", "View the store page", "Cancel");
        switch (choice)
        {
            case 0:
                ProcessUtil.Open($"steam://install/{game.AppId}");
                break;
            case 1:
                if (queued) InstallQueue.Remove(game); else InstallQueue.Add(game);
                ProcessInstallQueue();
                LibraryChanged?.Invoke();
                break;
            case 2:
                ProcessUtil.Open($"steam://store/{game.AppId}");
                break;
        }
    }

    public void ClearInstallQueue()
    {
        InstallQueue.Clear();
        _installing = null;
        LibraryChanged?.Invoke();
    }

    /// <summary>Starts the next queued install once the one before it has finished downloading.</summary>
    private void ProcessInstallQueue()
    {
        if (_installing != null)
        {
            if (!SteamLibrary.IsFullyInstalled(_installing.AppId)) return;
            ShowToast("DOWNLOAD", _installing.Name, "has finished installing", "", "download", game: _installing);
            _installing = null;
            _ = ReloadAsync(waitForArt: false);
        }
        if (InstallQueue.Count == 0) return;

        _installing = InstallQueue[0];
        InstallQueue.RemoveAt(0);
        ProcessUtil.Open($"steam://install/{_installing.AppId}");
        LibraryChanged?.Invoke();
    }

    /// <summary>Fills in achievement progress for the library's progress bars, once per run of the launcher.</summary>
    public async Task RefreshAchievementProgressAsync()
    {
        if (_progressChecked) return;
        _progressChecked = true;
        bool changed = false;
        try
        {
            foreach (var game in _steam.Where(g => Achievements.ProgressOf(g.AppId) == null).Take(80).ToList())
            {
                var all = await Task.Run(() => SteamStoreClient.GetAchievementsAsync(game.AppId, CancellationToken.None));
                if (all == null) continue; // no achievements, or a private profile
                changed |= Achievements.NoteProgress(game.AppId, all);
                await Task.Delay(250); // be gentle with Steam's site
            }
        }
        catch (Exception) { }
        if (!changed) return;
        Achievements.Save();
        LibraryChanged?.Invoke();
    }

    // ================================================================ boot video

    private MediaElement? _bootVideo;

    /// <summary>Plays the user's own video over the home screen at startup. Any input skips it.</summary>
    private bool TryPlayBootVideo()
    {
        var path = Settings.BootVideo;
        if (path.Length == 0 || !File.Exists(path)) return false;

        var video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close,
            Stretch = Stretch.Uniform, Source = new Uri(path)
        };
        video.MediaEnded += (_, _) => EndBootVideo();
        video.MediaFailed += (_, _) => EndBootVideo();
        var cover = new Grid { Background = Brushes.Black };
        cover.Children.Add(video);
        IntroLayer.Children.Add(cover);
        _bootVideo = video;
        video.Play();

        // Never let a long or stuck video hold the launcher hostage.
        var limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        limit.Tick += (_, _) => { limit.Stop(); EndBootVideo(); };
        limit.Start();
        return true;
    }

    private void EndBootVideo()
    {
        if (_bootVideo == null) return;
        try { _bootVideo.Stop(); _bootVideo.Close(); } catch (Exception) { }
        _bootVideo = null;
        IntroLayer.Children.Clear();
    }

    // ================================================================ in-game quick menu

    private QuickOverlay? _overlay;

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Dismiss();
            return;
        }
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        Color? tint = null;
        try { if (running?.Cover is BitmapSource art) tint = ImageLoader.DominantColor(art); } catch (Exception) { }
        _overlay = new QuickOverlay(running?.Name, Battery.GetPercent(), Settings.Clock24Hour, Settings.OverlaySide, running?.Cover, tint);
        _overlay.OpenLauncher += () => WindowActivator.BringToFront(this);
        _overlay.ExitGame += async () =>
        {
            if (running != null) await StopGameAsync(running, confirm: false);
            WindowActivator.BringToFront(this);
        };
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        _overlay.Activate();
    }

    // ================================================================ stores

    private void Stores_Click(object sender, RoutedEventArgs e) => Push(new StoresScreen());

    // ================================================================ first-run setup

    /// <summary>A short series of choices on first launch. Each can be changed later in Settings.</summary>
    public async Task RunSetupAsync()
    {
        int start = await AskAsync("Welcome to SquareDeck",
            "A few quick choices to set things up. Everything here can be changed later in Settings.",
            "Start setup", "Skip and use the defaults");
        if (start == 0)
        {
            int look = await AskAsync("Choose a look", null, "Follow Windows", "Light", "Dark", "OLED black");
            if (look >= 0)
            {
                Settings.ThemeMode = look switch { 1 => "light", 2 => "dark", 3 => "oled", _ => "system" };
                Settings.DarkTheme = Theme.ShouldBeDark(Settings);
                ApplyLook();
            }

            int stores = await AskAsync("Which games should appear?",
                "Steam games are always shown. Games from Epic, GOG, Xbox / Game Pass, Battle.net and Steam's non-Steam shortcuts can be included too.",
                "Steam and the other stores", "Steam only");
            if (stores >= 0) Settings.OtherStores = stores == 0;

            int layout = await AskAsync("Controller button layout", "Which button should select things?",
                "Xbox style: A selects, B goes back", "Nintendo style: A/B and X/Y swapped");
            if (layout >= 0) SetButtonMap(layout == 1 ? "BAYX" : "ABXY");

            int art = await AskAsync("Cover art",
                "Covers, backgrounds and logos come from SteamGridDB. A key is already built in; you can use your own instead " +
                "(steamgriddb.com, under Preferences > API).",
                "Use the built-in key", "Enter my own key");
            if (art == 1)
            {
                var key = await PromptAsync("SteamGridDB API key", null, Settings.SteamGridDbApiKey);
                if (!string.IsNullOrWhiteSpace(key)) Settings.SteamGridDbApiKey = key.Trim();
            }

            int steam = await AskAsync("Steam extras (optional)",
                "The Friends page and the list of games you own but haven't installed need your own free Steam Web API key " +
                "(steamcommunity.com/dev/apikey).",
                "Skip for now", "Enter my key");
            if (steam == 1) await SettingsScreen.EditSteamKeyAsync(this, () => { });
        }

        Settings.SetupDone = true;
        Settings.Save();
        ApplySettings();
    }
}

/// <summary>
/// A small panel shown over a running game: clock, battery, volume, brightness, and a way out.
/// It is a separate always-on-top window so the launcher itself can stay in the background.
/// </summary>
public sealed class QuickOverlay : Window
{
    private readonly List<FrameworkElement> _items = new();
    private readonly Slider _volume, _brightness;
    private readonly Border _brightnessRow;
    private readonly Button _exit;
    private readonly DispatcherTimer _brightnessApply = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _index;
    private bool _syncing, _exitArmed;

    public event Action? OpenLauncher;
    public event Action? ExitGame;

    private readonly string _side;
    private readonly TranslateTransform _slide = new();
    private const double PanelWidth = 500;
    private bool _closing;

    /// <param name="side">"left" or "right" for a panel that slides in from that edge; "center" for a pop-up in the middle.</param>
    public QuickOverlay(string? gameName, int? battery, bool clock24, string side = "right", ImageSource? cover = null, Color? tint = null)
    {
        _side = side is "left" or "center" ? side : "right";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // A side panel leaves the game visible; the centered pop-up dims it more.
        Background = new SolidColorBrush(Color.FromArgb(_side == "center" ? (byte)0x90 : (byte)0x48, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        WindowState = WindowState.Maximized;

        var panel = new StackPanel();
        if (cover != null)
        {
            // The running game's cover heads the menu.
            var art = new Border
            {
                Width = 190, Height = 190, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 20), Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill },
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 }
            };
            panel.Children.Add(art);
        }
        string time = DateTime.Now.ToString(clock24 ? "H:mm" : "h:mm tt");
        panel.Children.Add(Ui.Text(time + (battery != null ? $"      Battery {battery}%" : ""), 26));
        if (gameName != null)
        {
            var playing = Ui.Text("Playing " + gameName, 17, "SubtleBrush");
            playing.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(playing);
        }

        var rows = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        rows.Children.Add(Ui.SliderRow("Volume", "", out _volume, out var volumeText));
        _brightnessRow = Ui.SliderRow("Brightness", "", out _brightness, out var brightnessText);
        _brightnessRow.Visibility = Visibility.Collapsed;
        rows.Children.Add(_brightnessRow);

        var back = Ui.Row(gameName != null ? "Back to game" : "Close", glyph: "");
        back.Click += (_, _) => Dismiss();
        rows.Children.Add(back);
        var launcher = Ui.Row("Open the launcher", glyph: "");
        launcher.Click += (_, _) => { Close(); OpenLauncher?.Invoke(); };
        rows.Children.Add(launcher);
        _exit = Ui.Row("Exit game", glyph: "");
        _exit.Click += (_, _) =>
        {
            // Two presses, so a slip of the thumb can't end a session.
            if (!_exitArmed)
            {
                _exitArmed = true;
                _exit.Content = Ui.Text("Press again to exit the game (unsaved progress is lost)", 20);
                return;
            }
            Close();
            ExitGame?.Invoke();
        };
        if (gameName != null) rows.Children.Add(_exit);
        panel.Children.Add(rows);

        var hint = Ui.Text("Up / Down to move      Left / Right to adjust      " + Hints.KeyLabel("Back") + " to close", 15, "SubtleBrush");
        hint.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(hint);

        Border box;
        if (_side == "center")
        {
            box = new Border
            {
                Width = 620, Padding = new Thickness(30), CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
            };
        }
        else
        {
            // A full-height panel against one edge of the screen, like a console's quick menu.
            bool left = _side == "left";
            panel.VerticalAlignment = VerticalAlignment.Center;
            box = new Border
            {
                Width = PanelWidth, Padding = new Thickness(30, 40, 30, 40),
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch, Child = panel, RenderTransform = _slide,
                BorderThickness = left ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 }
            };
            box.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            hint.TextWrapping = TextWrapping.Wrap;
            _slide.X = left ? -PanelWidth : PanelWidth;
        }
        box.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        if (tint is Color gameColor) box.Background = CoverGradient(gameColor);
        Content = box;
        // Click or tap outside the panel to close it.
        MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) Dismiss(); };

        _items.Add(_volume);
        _items.Add(back);
        _items.Add(launcher);
        if (gameName != null) _items.Add(_exit);

        _syncing = true;
        var volume = Audio.GetVolume();
        _volume.IsEnabled = volume != null;
        _volume.Value = volume ?? 0;
        volumeText.Text = volume == null ? "No device" : volume + "%";
        _syncing = false;

        _volume.ValueChanged += (_, e) =>
        {
            volumeText.Text = (int)e.NewValue + "%";
            if (!_syncing) Audio.SetVolume((int)e.NewValue);
        };
        _brightness.ValueChanged += (_, e) =>
        {
            brightnessText.Text = (int)e.NewValue + "%";
            if (_syncing) return;
            _brightnessApply.Stop();
            _brightnessApply.Start();
        };
        _brightnessApply.Tick += async (_, _) =>
        {
            _brightnessApply.Stop();
            await Brightness.SetAsync((int)_brightness.Value);
        };

        Loaded += async (_, _) =>
        {
            SlideTo(0, null);
            _volume.Focus();
            var level = await Brightness.GetAsync();
            if (level == null) return;
            _syncing = true;
            _brightness.Value = level.Value;
            _syncing = false;
            _brightnessRow.Visibility = Visibility.Visible;
            _items.Insert(1, _brightness);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Dismiss(); };
        Closed += (_, _) => _brightnessApply.Stop();
    }

    /// <summary>
    /// The same wash of the cover's color that the game's details page uses: strongest at the top,
    /// fading into the normal background so the text stays readable.
    /// </summary>
    private static Brush CoverGradient(Color color)
    {
        var bg = (Application.Current.Resources["BgBrush"] as SolidColorBrush)?.Color ?? Colors.Black;
        Color Mix(double amount) => Color.FromRgb(
            (byte)(bg.R + (color.R - bg.R) * amount),
            (byte)(bg.G + (color.G - bg.G) * amount),
            (byte)(bg.B + (color.B - bg.B) * amount));
        double strength = Theme.CurrentIsDark ? 0.62 : 0.4;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
        brush.GradientStops.Add(new GradientStop(Mix(strength), 0));
        brush.GradientStops.Add(new GradientStop(Mix(strength * 0.55), 0.4));
        brush.GradientStops.Add(new GradientStop(Mix(strength * 0.2), 0.75));
        brush.GradientStops.Add(new GradientStop(bg, 1));
        brush.Freeze();
        return brush;
    }

    private void SlideTo(double x, Action? then)
    {
        if (_side == "center" || Theme.ReduceMotion)
        {
            _slide.X = x;
            then?.Invoke();
            return;
        }
        var move = new DoubleAnimation(x, TimeSpan.FromMilliseconds(then == null ? 240 : 170))
        {
            EasingFunction = new CubicEase { EasingMode = then == null ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        if (then != null) move.Completed += (_, _) => then();
        _slide.BeginAnimation(TranslateTransform.XProperty, move);
    }

    /// <summary>Closes the menu, sliding the panel back off its edge first.</summary>
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        SlideTo(_side == "left" ? -PanelWidth : PanelWidth, Close);
    }

    /// <summary>Controller input, passed on by the launcher while this window is showing.</summary>
    public void HandleAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Up: Move(-1); break;
            case NavAction.Down: Move(+1); break;
            case NavAction.Left or NavAction.Right:
                if (_items[_index] is Slider slider)
                    slider.Value = Math.Clamp(slider.Value + (action == NavAction.Right ? 5 : -5), 0, 100);
                break;
            case NavAction.Accept:
                if (_items[_index] is Button button)
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                break;
            case NavAction.Back or NavAction.View:
                Dismiss();
                break;
        }
    }

    private void Move(int delta)
    {
        _index = Math.Clamp(_index + delta, 0, _items.Count - 1);
        _items[_index].Focus();
    }
}
