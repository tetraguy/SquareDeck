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
        UnregisterSummonKey();
        UnregisterBackKeys();
        AllyControl.Shutdown(); // built-in controller on, game mode, M1/M2 as ASUS ships them
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
            if (Environment.ProcessPath is string exe)
                Process.Start(new ProcessStartInfo(exe, App.RestartArgument) { UseShellExecute = true })?.Dispose(); // waits for this copy to close
        }
        catch (Exception) { }
        Close();
    }

    // ================================================================ pickers

    /// <summary>Lets the user choose a file with the controller. Null if they backed out.</summary>
    public async Task<string?> PickFileAsync(string title, string[] extensions, bool orFolder = false)
    {
        var picker = new FilePickerScreen(title, extensions, orFolder);
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

    // ================================================================ keyboard shortcut (the widget in a game, the launcher otherwise)

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
        if (message == App.ShowExistingMessage)
        {
            // SquareDeck was started again: come forward instead of opening a second copy (and, from Game Bar's Home or
            // Library button in Xbox mode, go there).
            handled = true;
            WindowActivator.BringToFront(this);
            int where = wParam.ToInt32();
            if (where is 1 or 2) GameBarGo(where == 2 ? "library" : "home", fresh: false);
            return IntPtr.Zero;
        }
        if (message == App.WidgetWakeMessage)
        {
            handled = true;
            if (!Settings.GameBarWidget) { Settings.GameBarWidget = true; Settings.Save(); }
            WidgetBridge.Restart();
            return IntPtr.Zero;
        }
        if (message == WmHotkey && wParam.ToInt32() == SummonHotkeyId)
        {
            handled = true;
            Summon();
        }
        else if (message == WmHotkey && (wParam.ToInt32() == M1HotkeyId || wParam.ToInt32() == M2HotkeyId))
        {
            handled = true;
            OnAllyButton(wParam.ToInt32() == M1HotkeyId ? "m1" : "m2");
        }
        else if (message == AllyButtons.WmInput && _allyListening && AllyButtons.Read(lParam) is string press)
        {
            OnAllyButton(press, AllyButtons.LastPressKey);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Game Bar's Home and Library buttons, with SquareDeck as Xbox mode's home app: Home closes every open page and
    /// shows the games row; Library does the same, then opens the Library. A question that can't be dismissed (a
    /// required update) stays. Just after start, it waits for the opening to finish.
    /// </summary>
    public async void GameBarGo(string where, bool fresh)
    {
        if (fresh) await Task.Delay(TimeSpan.FromSeconds(2.5));
        // Not over the first-time setup.
        if (_screens.Any(s => s is SquareDeck.Screens.OnboardingScreen)) return;
        while (_screens.Count > 0 && !(_screens[^1] is SquareDeck.Screens.MenuScreen { Mandatory: true }) && _screens[^1] is not SquareDeck.Screens.BusyScreen)
            Pop(_screens[^1]);
        if (_screens.Count > 0) return; // something that has to stay is open
        SetMode(HomeView.Games);
        if (where == "library") Push(new SquareDeck.Screens.LibraryScreen());
    }

    // ================================================================ ROG Ally buttons

    private bool _allyListening;

    /// <summary>Starts or stops listening to the Ally's extra buttons, as Settings says.</summary>
    public void ApplyAllyButtons()
    {
        AllyButtons.ModelOverride = Settings.AllyModel ?? "";
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (_hotkeySource == null) ApplySummonKey(); // sets up the message hook
        bool want = Settings.AllyRemap || Settings.ExperimentAllyQuickMenu.Length > 0;
        if (want) _ = Task.Run(AllyHid.EnsureAwake); // without ASUS Optimization the extra buttons only send codes after it
        ApplyBackKeys(handle);
        if (want == _allyListening) return;
        _allyListening = AllyButtons.Listen(handle, want) && want;
    }

    // Adrenaline Hook: a game profile can show AMD Software's metrics overlay while that game runs. Its shortcut
    // toggles it, so it's pressed once when the game has started and once after it closes.
    private string? _amdMetricsFor;
    private DateTime _amdMetricsSince;

    private void UpdateAmdMetrics(Game? running)
    {
        if (!Settings.ExperimentAdrenalineHook) { _amdMetricsFor = null; return; }
        if (running == null)
        {
            if (_amdMetricsFor != null && _amdMetricsFor != "pending") AdrenalineHook.Send("amdmetrics"); // hide it again
            _amdMetricsFor = null;
            return;
        }
        if (_amdMetricsFor == running.Key || Library.Find(running.Key)?.Profile is not { AmdMetricsOverlay: true }) return;
        // Wait for the game's window (about ten seconds after it was first seen running), then show it.
        if (_amdMetricsFor != "pending") { _amdMetricsFor = "pending"; _amdMetricsSince = DateTime.UtcNow; return; }
        if (DateTime.UtcNow - _amdMetricsSince < TimeSpan.FromSeconds(10)) return;
        AdrenalineHook.Send("amdmetrics");
        _amdMetricsFor = running.Key;
    }

    // M1 / M2 send F18 / F17 once "M1 and M2 back buttons" is on (Settings > ROG Ally controls). They're only taken
    // from other apps while one of them has an action, so games can still bind them otherwise.
    private const int M1HotkeyId = 0x5145, M2HotkeyId = 0x5146;
    private bool _backKeysRegistered;

    private void ApplyBackKeys(IntPtr handle)
    {
        UnregisterBackKeys();
        if (!Settings.AllyBackKeys) return;
        bool wantM1 = Settings.AllyRemap && Settings.AllyBindings.TryGetValue("m1", out var m1) && !string.IsNullOrEmpty(m1)
                      || Settings.ExperimentAllyQuickMenu == "m1";
        bool wantM2 = Settings.AllyRemap && Settings.AllyBindings.TryGetValue("m2", out var m2) && !string.IsNullOrEmpty(m2)
                      || Settings.ExperimentAllyQuickMenu == "m2";
        const uint NoRepeat = 0x4000, F17 = 0x80, F18 = 0x81;
        try
        {
            if (wantM1) _backKeysRegistered |= RegisterHotKey(handle, M1HotkeyId, NoRepeat, F18);
            if (wantM2) _backKeysRegistered |= RegisterHotKey(handle, M2HotkeyId, NoRepeat, F17);
        }
        catch (Exception) { }
    }

    private void UnregisterBackKeys()
    {
        if (!_backKeysRegistered || _hotkeySource == null) return;
        try
        {
            UnregisterHotKey(_hotkeySource.Handle, M1HotkeyId);
            UnregisterHotKey(_hotkeySource.Handle, M2HotkeyId);
        }
        catch (Exception) { }
        _backKeysRegistered = false;
    }

    private void OnAllyButton(string press, string? codeKey = null)
    {
        // A button set up by pressing it (by its raw code) comes first: it works whatever the model calls the button.
        if (Settings.AllyRemap && codeKey != null && (Settings.AllyCodeBindings ??= new()).TryGetValue(codeKey, out var learned) && !string.IsNullOrEmpty(learned)
            && !(Settings.ExperimentAllyQuickMenu.Length > 0 && press == Settings.ExperimentAllyQuickMenu && WidgetWanted()))
        {
            RunAllyAction(learned);
            return;
        }
        if (press == "code") return; // a code with no name and nothing set up for it
        // One Ally button opens SquareDeck's Game Bar widget while a game is in front.
        if (Settings.ExperimentAllyQuickMenu.Length > 0 && press == Settings.ExperimentAllyQuickMenu)
        {
            if (WidgetWanted()) { OpenWidget(); return; }
            // No game in front: the button does what it's set to do (when the remapping is on).
            if (!Settings.AllyRemap) return;
        }
        else if (!Settings.AllyRemap) return;
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
            case "widget" or "quickmenu": OpenWidget(); break;
            case "taskswitcher": ProcessUtil.Open("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}"); break;
            case "taskview": AllyButtons.SendKeys(Win, Tab); break;
            case "desktop": AllyButtons.SendKeys(Win, D); break;
            case "gamebar": ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_GAME_GUIDE_OVERLAY&source=GbEm"); break;
            case "commandcenter": ProcessUtil.Open("ms-gamebar://launchForeground/activate/B9ECED6F.ASUSCommandCenter_qmba6cd70vzyy_App_Widget"); break;
            case "armourycrate": ProcessUtil.Open("asusac://"); break;
            case "screenshot": CaptureGame("IHKID_CAPTURE_GAME_SCREENSHOT"); break;
            case "record": CaptureGame("IHKID_TOGGLE_GAME_RECORDING"); break;
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
                if (NightLight.IsOn is bool on) { NightLight.Set(!on); Note(on ? "Night light off" : "Night light on"); }
                else Note("Night light isn't available here");
                break;
            case "cpuboost":
            {
                // The running game's own choice when it has one, otherwise the usual setting.
                bool boost = !TdpControl.EffectiveBoost;
                if (TdpControl.GameProfile is { CpuBoost: not null } gp) { gp.CpuBoost = boost; Library.Save(); }
                else { Settings.CpuBoost = boost; Settings.Save(); }
                TdpControl.UpdateReapplyTimer();
                _ = TdpControl.ApplyAsync(quiet: true);
                Note(boost ? "CPU boost on" : "CPU boost off");
                break;
            }
            case "ris":
            {
                var ris = AmdAdlx.ImageSharpening;
                if (ris is bool r && AmdAdlx.SetImageSharpening(!r)) Note(r ? "Image sharpening off" : "Image sharpening on");
                else Note("Radeon Image Sharpening isn't available here");
                _widgetSlowAt = DateTime.MinValue;
                break;
            }
            case "chill":
            {
                var chill = AmdAdlx.Chill;
                if (chill is bool c && AmdAdlx.SetChill(!c)) Note(c ? "Radeon Chill off" : "Radeon Chill on");
                else Note("Radeon Chill isn't available here");
                _widgetSlowAt = DateTime.MinValue;
                break;
            }
            case "ledlevel" when AllyControl.IsAlly:
            {
                // Off, 5, 30, 60, 100, off… (5% when they come back on).
                var s = Settings;
                int now = s.AllyLights is "" or "off" ? 0 : AllyControl.BrightnessPercent(s);
                int next = now switch { 0 => 5, < 30 => 30, < 60 => 60, < 100 => 100, _ => 0 };
                if (next == 0) { s.AllyLightsLastOn = s.AllyLights; s.AllyLights = "off"; }
                else
                {
                    if (s.AllyLights is "" or "off") s.AllyLights = s.AllyLightsLastOn is { Length: > 0 } last && last != "off" ? last : "solid";
                    s.AllyLightPercent = next;
                    AllyControl.WakeLights(s);
                }
                s.Save();
                AllyControl.ApplyLights(force: true);
                Note(next == 0 ? "Lights off" : $"Lights {next}%");
                break;
            }
            // ---- the quick menu's extra buttons
            case "perf":
                Settings.OsdEnabled = !Settings.OsdEnabled;
                Settings.Save();
                PerfOverlay.Apply(Settings);
                Note(Settings.OsdEnabled ? "Performance overlay on" : "Performance overlay off");
                break;
            case "refresh":
                CycleRefreshRate();
                break;
            case "wifi":
            case "bluetooth":
                _ = ToggleRadioAsync(action == "wifi" ? Windows.Devices.Radios.RadioKind.WiFi : Windows.Devices.Radios.RadioKind.Bluetooth, action == "wifi" ? "Wi-Fi" : "Bluetooth");
                break;
            case "airplane":
                _ = ToggleAirplaneAsync();
                break;
            case "dnd":
                // For now (not the setting): Windows' notifications off or on; put back when SquareDeck closes.
                bool quiet = !Settings.ToastsSuppressed;
                if (DoNotDisturb.Set(quiet)) { Settings.ToastsSuppressed = quiet; Settings.Save(); }
                Note(quiet ? "Do not disturb on" : "Do not disturb off");
                break;
            case "playpause":
                _ = MediaService.PlayPauseAsync();
                Note("Play / pause");
                break;
            case "hdr":
                if (Hdr.Get() is bool hdr) { Hdr.Set(!hdr); Note(hdr ? "HDR off" : "HDR on"); }
                else Note("This screen has no HDR");
                break;
            case "screenoff":
                // Any button or touch wakes it again. Handy for a long download.
                _ = Task.Delay(600).ContinueWith(_ => ScreenPower.Off());
                break;
            // ---- ROG Ally (Settings > ROG Ally controls)
            case "lights":
                Note(AllyControl.ToggleLights() ? "Lights on" : "Lights off");
                break;
            case "asusmode":
                Note(AsusAcpi.Available ? "ASUS mode: " + AllyControl.NextAsusMode() : "ASUS modes aren't available here");
                break;
            case "fan":
                Note(AsusAcpi.Available ? "Fans: " + AllyControl.NextFan() : "Fan control isn't available here");
                break;
            case "amdoverlay":
            case "amdmetrics":
                AdrenalineHook.Send(action); // AMD Software's own shortcut, to the game in front
                break;
            case "padmouse":
                Note(AllyControl.ToggleMouseMode() ? (AllyControl.MouseMode ? "Mouse mode" : "Controller mode") : "Couldn't change the controller mode");
                break;
        }
    }

    /// <summary>A line in the Game Bar widget (if it's open) saying what a button just did.</summary>
    private void Note(string text) => WidgetBridge.Note(text);

    private async Task ToggleRadioAsync(Windows.Devices.Radios.RadioKind kind, string name)
    {
        var on = await NetworkService.GetRadioStateAsync(kind);
        if (on == null) { Note(name + " isn't available"); return; }
        bool ok = await NetworkService.SetRadioAsync(kind, !on.Value);
        Note(ok ? $"{name} {(on.Value ? "off" : "on")}" : $"Couldn't change {name}");
    }

    /// <summary>The next refresh rate the screen offers at its current size (60 → 120 → 144 → 60…).</summary>
    private void CycleRefreshRate()
    {
        try
        {
            if (DisplaySettings.Current() is not { } now) return;
            var rates = DisplaySettings.All().Where(m => m.Width == now.Width && m.Height == now.Height).Select(m => m.RefreshRate).Distinct().OrderBy(r => r).ToList();
            if (rates.Count < 2) { Note($"{now.RefreshRate} Hz (the only rate)"); return; }
            int next = rates.FirstOrDefault(r => r > now.RefreshRate);
            if (next == 0) next = rates[0];
            Note(DisplaySettings.Apply(now with { RefreshRate = next }, keep: true) ? $"{next} Hz" : "Couldn't change the refresh rate");
        }
        catch (Exception) { Note("Couldn't change the refresh rate"); }
    }

    /// <summary>
    /// Asks Game Bar for a screenshot or recording of the game in front.
    /// </summary>
    private void CaptureGame(string hotkey)
    {
        ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=" + hotkey + "&source=GbEm");
    }

    /// <summary>What the summon buttons do: SquareDeck's Game Bar widget over a running game, otherwise the launcher itself.</summary>
    private void Summon()
    {
        if (WidgetWanted() && (!IsActive || AllGames.Any(g => g.IsRunning))) OpenWidget();
        else WindowActivator.BringToFront(this);
    }

    // ================================================================ explorer

    private void Explorer_Click(object sender, RoutedEventArgs e) => Push(new ExplorerScreen());

    private void News_Click(object sender, RoutedEventArgs e) => Push(new GameNewsScreen());

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
                    if (Settings.FriendAlerts == "off" ||
                        (Settings.FriendAlerts == "pinned" && !Settings.PinnedFriends.Contains(f.Id))) continue;
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
        if (_screens.Count == 0) return;
        var top = _screens[^1];
        if (top.OnRightStick(_gamepad.RightStickX, y, _gamepad.RightStickPressed)) return; // the browser uses it as a mouse
        if (y == 0) return;

        ScrollViewer? viewer = null;
        if (Keyboard.FocusedElement is DependencyObject focused && top.IsAncestorOf(focused as Visual ?? top))
        {
            for (var d = focused; d != null && d != top; d = VisualTreeHelper.GetParent(d))
                if (d is ScrollViewer { ScrollableHeight: > 0 } sv) { viewer = sv; break; }
        }
        viewer ??= FindScrollable(top);
        // Faster the further the stick is pushed.
        if (viewer == null) return;
        double before = viewer.VerticalOffset;
        viewer.ScrollToVerticalOffset(before - y * Math.Abs(y) * 26);
        // On a game's page, a soft tick every so often as the page scrolls past (like notches), and none at the ends.
        if (top is DetailsScreen && Settings.RumbleInSettings && Math.Abs(viewer.VerticalOffset - before) > 0.5)
        {
            _scrollTravel += Math.Abs(y * y * 26);
            if (_scrollTravel >= 160) { _scrollTravel = 0; Rumble(12); }
        }
    }
    private double _scrollTravel;

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
        StepPage(page, direction);
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
        AllyControl.ShowGame(game); // the Ally's lights, when they follow the selected game
        try
        {
            await ApplyGameAccentCoreAsync(game);
        }
        finally { AllyControl.AccentChanged(); }
    }

    private async Task ApplyGameAccentCoreAsync(Game? game)
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
            // SquareDeck's own Spotify player: it knows the song, its artist and its cover itself (Windows may only
            // see a page called "SquareDeck player").
            var ours = SpotifyPlayer.Track;
            _nowPlayingIsOurs = ours != null && SpotifyPlayer.IsRunning &&
                                (now == null || now.Title == "SquareDeck player" || now.Title == ours.Title || SpotifyPlayer.IsPlaying);
            if (_nowPlayingIsOurs && ours != null)
                now = new NowPlaying(ours.Title, ours.Artist, "SquareDeck", SpotifyPlayer.IsPlaying, TimeSpan.Zero, TimeSpan.Zero, null);
            bool playing = now?.IsPlaying == true;
            if (playing != _mediaPlaying)
            {
                // Background music makes way for the music you chose, and comes back when it stops.
                _mediaPlaying = playing;
                UpdateMenuMusic();
            }
            if (now == null)
            {
                MusicButton.Visibility = Visibility.Collapsed;
                if (_footerIndex >= 0) SetFooterIndex(Math.Min(_footerIndex, FooterButtons().Count - 1));
                return;
            }
            MusicButton.Visibility = Visibility.Visible;
            MusicGlyph.Text = now.IsPlaying ? "\uE769" : "\uE768";
            MusicText.Text = now.Artist.Length > 0 ? now.Title + "  \u2013  " + now.Artist : now.Title;
            EnsureMusicMarquee();

            // The cover art of the song, fetched again only when the song changes.
            string key = now.App + "|" + now.Title + "|" + now.Artist;
            if (key != _musicArtKey)
            {
                _musicArtKey = key;
                if (_nowPlayingIsOurs && ours?.ArtUrl is { Length: > 0 } artUrl)
                {
                    try { _musicArt = await Task.Run(() => ImageLoader.FromUrlAsync(artUrl, 96)); }
                    catch (Exception) { _musicArt = null; }
                }
                else
                {
                    var withArt = await MediaService.GetAsync(includeThumbnail: true);
                    _musicArt = withArt?.Thumbnail is { Length: > 0 } bytes ? ImageLoader.FromBytes(bytes, 96) : null;
                }
            }
            bool art = _musicArt != null;
            MusicArt.Background = art ? new ImageBrush(_musicArt) { Stretch = Stretch.UniformToFill } : null;
            MusicArt.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
            MusicArt.Opacity = now.IsPlaying ? 1 : 0.45; // paused: the cover dims
            MusicGlyph.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception) { }
    }

    private string? _musicArtKey;
    private bool _nowPlayingIsOurs;
    private ImageSource? _musicArt;
    private MarqueeText? _musicMarquee;

    /// <summary>Puts the song name in a box that scrolls when the name is too long for the banner.</summary>
    private void EnsureMusicMarquee()
    {
        if (_musicMarquee != null || MusicText.Parent is not Panel panel) return;
        int index = panel.Children.IndexOf(MusicText);
        panel.Children.Remove(MusicText);
        MusicText.MaxWidth = double.PositiveInfinity;
        _musicMarquee = new MarqueeText(MusicText) { MaxWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Insert(index, _musicMarquee);
    }

    /// <summary>Next or previous song in whatever is playing (Spotify, a browser, Media Player…).</summary>
    private async Task SkipSongAsync(bool next)
    {
        UiSound.Page();
        if (next) await MediaService.NextAsync(); else await MediaService.PreviousAsync();
        // A little nudge in the banner so it's clear something happened.
        if (!Theme.ReduceMotion)
        {
            var shift = new TranslateTransform();
            MusicButton.RenderTransform = shift;
            var nudge = new DoubleAnimation(next ? 14 : -14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            shift.BeginAnimation(TranslateTransform.XProperty, nudge);
        }
        await Task.Delay(400); // the player takes a moment to report the new song
        UpdateNowPlaying();
    }

    /// <summary>The now-playing pill: back to the player (the Multimedia page, on its Spotify tab for SquareDeck's own player).</summary>
    private void MusicOpen_Click(object sender, RoutedEventArgs e) => OpenPlayer();

    public void OpenPlayer()
    {
        if (_screens.OfType<MediaScreen>().FirstOrDefault() is { } open)
        {
            // Already open underneath: go back to it rather than opening a second one.
            while (_screens.Count > 0 && !ReferenceEquals(_screens[^1], open)) Pop(_screens[^1]);
            return;
        }
        if (_nowPlayingIsOurs) Settings.MusicTab = "spotify";
        else if (Settings.MusicTab == "streaming") Settings.MusicTab = "now";
        Push(new MediaScreen());
    }

    private async void MusicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_nowPlayingIsOurs) SpotifyPlayer.TogglePlay();
        else await MediaService.PlayPauseAsync();
        await Task.Delay(250);
        UpdateNowPlaying();
    }

    // ================================================================ footer focus (music, Stop)

    private int _footerIndex = -1;
    private bool _stopFocused => _footerIndex >= 0;

    private List<Button> FooterButtons() =>
        new[] { MusicButton, StreamButton, StreamCloseButton, StopButton }
            .Where(b => b == StopButton ? RunningPanel.Visibility == Visibility.Visible : b.Visibility == Visibility.Visible)
            .ToList();

    private void SetFooterIndex(int index)
    {
        var buttons = FooterButtons();
        if (index >= buttons.Count) index = buttons.Count - 1;
        if (index >= 0 && index != _footerIndex) Rumble();
        _footerIndex = index;

        MusicButton.Tag = StopButton.Tag = StreamButton.Tag = StreamCloseButton.Tag = null;
        UpdateHomeRing();
        if (index < 0) return;
        buttons[index].Tag = "selected";
        if (buttons[index] == StopButton)
        {
            var running = AllGames.FirstOrDefault(g => g.IsRunning);
            ButtonLabel.Text = running == null ? "Stop" : "Stop " + running.Name;
        }
        else if (buttons[index] == StreamButton) ButtonLabel.Text = "Back to " + (StreamingScreen.Current?.Service.Name ?? "the service");
        else if (buttons[index] == StreamCloseButton) ButtonLabel.Text = "Close " + (StreamingScreen.Current?.Service.Name ?? "the service");
        else ButtonLabel.Text = Hints.Format("{Select}  Open player      {Details}  Play / pause");
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
            case NavAction.X when buttons[Math.Clamp(_footerIndex, 0, buttons.Count - 1)] == MusicButton:
                MusicToggle_Click(MusicButton, new RoutedEventArgs());
                break;
            case NavAction.Up:
            case NavAction.Back:
                SetFooterIndex(-1);
                SetButtonIndex(_stopReturnIndex);
                break;
            case NavAction.Accept:
                var target = buttons[Math.Clamp(_footerIndex, 0, buttons.Count - 1)];
                Motion.PressThen(target, () =>
                {
                    if (target == StopButton) SetFooterIndex(-1);
                    target.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                });
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
        LocalMusic.Pause(); // your music stops for the game
        // The game's own TDP, boost and power mode (or the defaults, if it has none).
        TdpControl.SetGame(profile is { HasPower: true } ? profile : null);

        if (Settings.DoNotDisturb && DoNotDisturb.Set(true))
        {
            launch.Quiet = true;
            Settings.ToastsSuppressed = true;
            Settings.Save();
        }

        if (launch.Previous != null || launch.Quiet) _activeLaunch = launch;
        await OnGameStartingEightAsync(game); // Experimental 8.0: frame cap, script before, session timer
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
            SessionLog.Finish(key, name, started, seconds); // battery and performance for this game
            if (game != null && Labs.On("closelaunchers")) _ = CloseLauncherAfterAsync(game);
            OnGameEndedEight(key, game); // Experimental 8.0: frame cap back, script after
            if (_activeLaunch != null && _activeLaunch.Game.Key == key) _ = RestoreLaunchAsync();

            // Steam games wait a moment so the summary can include achievements; the rest report straight away.
            if (game is { IsSteam: true } && Settings.AchievementToasts)
            {
                _pendingSummary[game.AppId] = seconds;
                // If the achievement check never answers (offline, Steam slow), the summary still comes, without a count.
                var g = game;
                _ = Task.Delay(TimeSpan.FromSeconds(25)).ContinueWith(_ => FinishPendingSummary(g, null), TaskScheduler.FromCurrentSynchronizationContext());
            }
            else ShowSessionSummary(name, seconds, null);
        }

        SessionLog.Sample(running.Keys);

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
        if (!Settings.SessionSummary || seconds < 20) return;
        // The game has only just closed: SquareDeck may still be hidden behind it (or coming back to the front).
        // Wait until it's on screen, so the summary is seen instead of playing out unseen.
        for (int i = 0; i < 240 && (_backgroundMode || !IsVisible || WindowState == WindowState.Minimized); i++) await Task.Delay(500);
        await Task.Delay(700); // after the return animation
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
        // Each call adds to what's being fetched instead of replacing it. A new call used to cancel the one before,
        // so when two pages asked at once (Home putting covers back after a game, the library or a refresh asking
        // for its own few), the covers the first call was still fetching never arrived and those tiles stayed blank.
        var cts = _coverCts ??= new CancellationTokenSource();
        var todo = games.Where(g => g.Cover == null && g.IsSteam && !g.Parked && _coverFetching.Add(g)).ToList();
        if (todo.Count == 0) return;
        _ = FetchCoversAsync(todo, cts.Token);
    }

    /// <summary>Games whose cover is being fetched right now (so they aren't fetched twice at once).</summary>
    private readonly HashSet<Game> _coverFetching = new();

    /// <summary>Stops the cover fetches under way (going into lean mode behind a game); the next ask starts afresh.</summary>
    private void CancelCoverFetches()
    {
        _coverCts?.Cancel();
        _coverCts = null;
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
                catch (OperationCanceledException) { _coverFetching.Remove(game); return; }
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
                finally { gate.Release(); _coverFetching.Remove(game); }
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
        string estimate = Labs.On("installestimate") ? await InstallEstimateAsync(game) : "";
        int choice = await AskAsync(game.Name,
            "This game is in your Steam library but isn't installed on this PC. Steam asks you to confirm where to install it." + estimate,
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
            AllyControl.React("download");
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

    // ================================================================ in-game: the Game Bar widget

    /// <summary>
    /// Opens SquareDeck's widget in Xbox Game Bar (it replaced the in-game quick menu): the widget straight away when
    /// it's installed, as ASUS's Command Center button does it; otherwise Game Bar itself.
    /// </summary>
    public void OpenWidget()
    {
        if (WidgetInstaller.FamilyName() is { } family)
            ProcessUtil.Open($"ms-gamebar://launchForeground/activate/{family}_App_SquareDeckWidget");
        else
        {
            ProcessUtil.Open("ms-gamebar://hotkey/?ihkid=IHKID_GAME_GUIDE_OVERLAY&source=GbEm");
            if (Settings.GameBarWidget && WidgetInstaller.Find() != null) _ = InstallWidgetQuietlyAsync();
        }
    }

    private async Task InstallWidgetQuietlyAsync()
    {
        var (ok, message) = await WidgetInstaller.InstallAsync();
        ShowToast("Xbox Game Bar widget", ok ? "Widget ready" : "Widget not installed", ok ? "The SquareDeck widget is in Game Bar now." : message, "", ok ? "success" : "warning");
    }

    /// <summary>
    /// Whether the summon buttons should open the widget (over a game) rather than bring the launcher forward:
    /// a game SquareDeck started is running, or another app fills the screen in front.
    /// </summary>
    private bool WidgetWanted() =>
        // Any app in front counts, not only exactly full-screen ones: borderless games are often a pixel short of the
        // screen, and games from the Xbox app aren't always recognised as running.
        Settings.InGameQuickMenu && (AllGames.Any(g => g.IsRunning) || WindowActivator.FrontApp() != null);

    // ================================================================ stores

    private void Stores_Click(object sender, RoutedEventArgs e) => Push(new StoresScreen());

    // ================================================================ first-run setup

    /// <summary>A short series of choices on first launch. Each can be changed later in Settings.</summary>
    /// <summary>The welcome pages (first start, or Settings > Run first-time setup again). Finishes when they close.</summary>
    public async Task RunSetupAsync()
    {
        var done = new TaskCompletionSource();
        Push(new OnboardingScreen(done));
        await done.Task;
    }
}
