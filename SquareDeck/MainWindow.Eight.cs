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

/// <summary>
/// The experimental features from 8.0 that run in the background or live on Home: frame caps, scripts and the
/// session timer around a game, docked / TV mode, the charger check, the return animation, achievement pop-ups,
/// friends playing, the daily pick, trailer previews, microSD cards, the Windows Update guard and crash recovery.
/// Each does nothing unless it's turned on (Settings > Experimental).
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _eightTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private int _eightTicks;
    private bool _eightBusy;

    private void InitEight()
    {
        StartCrashRecovery();
        _eightTimer.Tick += (_, _) => _ = EightTickAsync();
        _eightTimer.Start();
        Labs.Changed += id =>
        {
            switch (id)
            {
                case "docked": if (!Labs.On("docked") && _docked) SetDocked(false); break;
                case "microsd": Eight.ForgetShelves(); break;
            }
        };
        // Visual modes: the chosen GameVisual mode is put back at start (Windows or Armoury Crate may have reset it).
        if (Labs.On("visualmodes") && (VisualModes.SavedMode != VisualModes.DefaultMode || VisualModes.SavedTemperature != VisualModes.Neutral))
            _ = Task.Run(() => VisualModes.ApplyAsync(VisualModes.SavedMode, VisualModes.SavedTemperature));
        // TV mode's bigger text is put back if SquareDeck closed while docked and is now on the built-in screen.
        if (Labs.Get("docked.prevtext") is { Length: > 0 } prev && int.TryParse(prev, out var text) && !Eight.ExternalDisplay())
        {
            Settings.TextScale = text;
            Settings.LabValues.Remove("docked.prevtext");
            Settings.Save();
        }
    }

    /// <summary>Every 5 seconds: the background parts of the 8.0 experiments.</summary>
    private async Task EightTickAsync()
    {
        if (_eightBusy) return;
        _eightBusy = true;
        _eightTicks++;
        try
        {
            var running = AllGames.FirstOrDefault(g => g.IsRunning);
            NoteScreenForRecovery();

            if (Labs.On("docked"))
            {
                bool external = Eight.ExternalDisplay();
                if (external != _docked) SetDocked(external);
            }
            if (Labs.On("sessiontimer")) SessionTimerTick(running);
            if (Labs.On("charger") && _eightTicks % 6 == 0) ChargerTick(running);
            if (Labs.On("microsd") && _eightTicks % 2 == 0) CardsTick();
            if (Labs.On("drivenotice")) DrivesTick(); else _knownDrives = null;
            if (Labs.On("achtoasts") && _eightTicks % 12 == 0) await AchievementToastsTickAsync(running);
            if (Labs.On("updateguard") && running != null && _eightTicks % 120 == 0) await UpdateGuardTickAsync(running);
        }
        catch (Exception ex) { CrashLog.Write("Experimental features (8.0)", ex); }
        finally { _eightBusy = false; }
    }

    // ================================================================ around a game: frame cap, scripts, timer

    private AmdAdlx.CapState? _capBeforeValue;
    private string? _capGame;

    /// <summary>
    /// What AMD's frame limiter was set to before SquareDeck capped it. Kept on disk too, so the cap is lifted even
    /// after SquareDeck closes or crashes mid-game (the driver itself would keep it for every game, indefinitely).
    /// </summary>
    private AmdAdlx.CapState? _capBefore
    {
        get => _capBeforeValue;
        set
        {
            if (value != null && _capBeforeValue == null) _capAppliedAt = DateTime.UtcNow;
            _capBeforeValue = value;
            Labs.Put("capbefore", value is { } c ? $"{c.Method}|{(c.Enabled ? 1 : 0)}|{c.First}|{c.Second}" : "");
        }
    }

    /// <summary>The lowest cap SquareDeck ever applies: under that, games crawl.</summary>
    private const int LowestCap = 30;

    /// <summary>
    /// No game running any more, but a cap SquareDeck set is still in the driver (it was set with no game, or the game
    /// closed in a way that wasn't noticed): lifted, back to how AMD Software had it.
    /// </summary>
    private DateTime _capAppliedAt;

    private void LiftStrayCap()
    {
        if (_capBefore is not { } before) return;
        // A game's cap goes on just before it starts: give it time to show up, and leave a launch that's under way.
        if (DateTime.UtcNow - _capAppliedAt < TimeSpan.FromSeconds(90)) return;
        if (_activeLaunch is { SeenRunning: false } && DateTime.UtcNow - _capAppliedAt < TimeSpan.FromMinutes(10)) return;
        _capBefore = null;
        _capGame = null;
        _widgetCap = 0;
        _ = Task.Run(() => AmdAdlx.RestoreCap(before));
    }

    /// <summary>At start: a cap left behind by a SquareDeck that closed mid-game (saved on disk) is lifted.</summary>
    private void LiftCapFromLastTime()
    {
        string saved = Labs.Get("capbefore");
        if (saved.Length == 0 || AllGames.Any(g => g.IsRunning)) return;
        var parts = saved.Split('|');
        Labs.Put("capbefore", "");
        if (parts.Length == 4 && int.TryParse(parts[2], out int first) && int.TryParse(parts[3], out int second))
        {
            var before = new AmdAdlx.CapState(parts[0], parts[1] == "1", first, second);
            _ = Task.Run(() => AmdAdlx.RestoreCap(before));
        }
    }
    private Game? _lastLaunched;

    /// <summary>Just before a game starts (after its profile): its script, its frame cap, and the session timer.</summary>
    private async Task OnGameStartingEightAsync(Game game)
    {
        _lastLaunched = game;
        try
        {
            if (Labs.On("scripts") && Eight.PreScript(game.Key) is { Length: > 0 } pre)
                await Eight.RunScriptAsync(pre, Eight.WaitForPre(game.Key) ? 60 : 0);
            if (Labs.On("fpscap") && Eight.FpsCap(game.Key) is int choice and > 0)
            {
                int fps = Math.Max(LowestCap, choice);
                if (_capBefore != null) await Task.Run(() => AmdAdlx.RestoreCap(_capBefore));
                _capBefore = await Task.Run(() => AmdAdlx.ApplyCap(fps));
                _capGame = _capBefore != null ? game.Key : null;
            }
            if (Labs.On("sessiontimer")) StartSessionTimer(game.Key);
            if (_docked && Labs.Get("docked.power", "1") == "1") DockedPower(true);
        }
        catch (Exception ex) { CrashLog.Write("Before a game (8.0)", ex); }
    }

    /// <summary>A game has closed: its frame cap is lifted and its script after runs.</summary>
    private void OnGameEndedEight(string key, Game? game)
    {
        try
        {
            if (_capGame == key && _capBefore is { } before)
            {
                _capBefore = null;
                _capGame = null;
                _ = Task.Run(() => AmdAdlx.RestoreCap(before));
            }
            if (_capGame == null) _widgetCap = 0;
            if (Labs.On("scripts") && Eight.PostScript(key) is { Length: > 0 } post) _ = Eight.RunScriptAsync(post, 0);
            if (_timerGame == key) { _breakAt = null; _timerGame = null; }
            if (_docked && Labs.Get("docked.power", "1") == "1")
                _ = Task.Delay(1500).ContinueWith(_ => DockedPower(true), TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex) { CrashLog.Write("After a game (8.0)", ex); }
    }

    // ---- session timer and break reminder

    private DateTime? _breakAt;
    private string? _timerGame;
    private bool _batteryNudged;

    private void StartSessionTimer(string key)
    {
        _timerGame = key;
        _batteryNudged = false;
        int minutes = int.TryParse(Labs.Get("timer.minutes", "120"), out var m) ? m : 120;
        _breakAt = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
    }

    private void SessionTimerTick(Game? running)
    {
        if (running == null) return;
        if (_timerGame != running.Key) StartSessionTimer(running.Key);
        if (_breakAt is DateTime due && DateTime.Now >= due)
        {
            int minutes = int.TryParse(Labs.Get("timer.minutes", "120"), out var m) ? m : 120;
            _breakAt = Labs.Get("timer.repeat", "1") == "1" && minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
            string played = PlaytimeStore.Format(RunningMinutes(running) * 60);
            GameNotice.Show("Time for a break?", $"{played} of {running.Name}. Stretch, blink, grab some water.", "");
        }
        int level = int.TryParse(Labs.Get("timer.battery", "20"), out var l) ? l : 20;
        if (level > 0 && !_batteryNudged && !Battery.IsPluggedIn() && Battery.GetPercent() is int percent && percent <= level)
        {
            _batteryNudged = true;
            GameNotice.Show($"Battery at {percent}%", "A good moment to save your game, or plug in.", "");
        }
    }

    // ---- smooth return

    /// <summary>When a game closes: back on the game you launched, the Switch 2 way (when that's on).</summary>
    private void ReturnAnimationEight()
    {
        if (!GameCard.S2 || _lastLaunched == null) return;
        var key = _lastLaunched.Key;
        var item = GameList.Items.OfType<Game>().FirstOrDefault(g => g.Actual.Key == key);
        if (item != null)
        {
            GameList.SelectedItem = item;
            GameList.ScrollIntoView(item);
        }
        if (Theme.ReduceMotion) return;
        SwitchReturn(item);
    }

    /// <summary>
    /// Experimental "Switch 2 style launch and return": the game closing the Switch way. The screen is the UI style's
    /// page color with the game's cover large in the middle; a moment later the cover flies back down into its tile on
    /// Home, shrinking as it goes, while the page color fades away to Home and the tile settles with a small bounce.
    /// </summary>
    private void SwitchReturn(Game? item)
    {
        var game = item?.Actual ?? _lastLaunched;
        if (game == null) return;
        Switch2Settings.Refresh();
        const double size = 260;
        var veil = new Grid { Background = new SolidColorBrush(Switch2Settings.Background), IsHitTestVisible = false };
        var cover = GameCard.Cover(game, size);
        cover.HorizontalAlignment = HorizontalAlignment.Left;
        cover.VerticalAlignment = VerticalAlignment.Top;
        var move = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        cover.RenderTransform = group;
        var layer = new Grid { IsHitTestVisible = false };
        layer.Children.Add(veil);
        layer.Children.Add(cover);
        IntroLayer.Children.Add(layer);
        double w = IntroLayer.ActualWidth > 0 ? IntroLayer.ActualWidth : ActualWidth, h = IntroLayer.ActualHeight > 0 ? IntroLayer.ActualHeight : ActualHeight;
        move.X = w / 2 - size / 2;
        move.Y = h / 2 - size / 2;
        cover.Opacity = 0;
        cover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            // Where the game's tile is on Home (or the middle of the row if it can't be seen).
            var tile = item != null ? GameList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement : null;
            Point target = new(w / 2, h / 2);
            double toScale = 0.5;
            try
            {
                if (tile != null && tile.IsVisible)
                {
                    target = tile.TranslatePoint(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2), IntroLayer);
                    toScale = Math.Max(0.2, Math.Min(tile.ActualWidth, tile.ActualHeight) / size * 0.85);
                }
            }
            catch (Exception) { }
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var begin = TimeSpan.FromMilliseconds(420);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target.X - size / 2, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target.Y - size / 2, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(toScale, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(toScale, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
            veil.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(500) });
            var done = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { BeginTime = TimeSpan.FromMilliseconds(880) };
            done.Completed += (_, _) =>
            {
                IntroLayer.Children.Remove(layer);
                if (tile == null) return;
                // The tile takes the cover's place with a small settle.
                var pop = new ScaleTransform(1.08, 1.08);
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
                tile.RenderTransform = pop;
                var settle = new DoubleAnimation(1.08, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
                settle.Completed += (_, _) => tile.RenderTransform = Transform.Identity;
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            };
            cover.BeginAnimation(OpacityProperty, done);
        }));
    }

    // ================================================================ docked / TV mode

    private bool _docked;
    private string? _dockAudioBefore;
    private bool _dockCursor;

    private void SetDocked(bool docked)
    {
        _docked = docked;
        if (docked)
        {
            if (Labs.Get("docked.text", "1") == "1" && Settings.TextScale < 130)
            {
                Labs.Put("docked.prevtext", Settings.TextScale.ToString());
                Settings.TextScale = 130;
                ApplyLook();
                ApplySettings();
            }
            if (Labs.Get("docked.power", "1") == "1") DockedPower(true);
            if (Labs.Get("docked.cursor", "1") == "1") { Mouse.OverrideCursor = Cursors.None; _dockCursor = true; }
            if (Labs.Get("docked.audio", "1") == "1" && Eight.TvSoundOutput() is { } tv)
            {
                var current = Audio.GetOutputs().FirstOrDefault(o => o.IsDefault);
                if (current.Id != tv.Id)
                {
                    _dockAudioBefore = current.Id;
                    Audio.SetDefaultOutput(tv.Id);
                }
            }
            ShowToast("TV MODE", "Docked", "Bigger text, more power and the sound on the TV. Unplug to go back.", "", "info", remember: false);
        }
        else
        {
            if (Labs.Get("docked.prevtext") is { Length: > 0 } prev && int.TryParse(prev, out var text))
            {
                Settings.TextScale = text;
                Settings.LabValues.Remove("docked.prevtext");
                Settings.Save();
                ApplyLook();
                ApplySettings();
            }
            DockedPower(false);
            if (_dockCursor) { Mouse.OverrideCursor = null; _dockCursor = false; }
            if (_dockAudioBefore != null) { Audio.SetDefaultOutput(_dockAudioBefore); _dockAudioBefore = null; }
            ShowToast("TV MODE", "Undocked", "Back to the handheld's own screen and settings.", "", "info", remember: false);
        }
    }

    private void DockedPower(bool on)
    {
        if (!TdpControl.Ready || TdpControl.Chip is not { } chip) return;
        if (on) TdpControl.SetSessionWatts(chip.Max);
        else TdpControl.ClearSession();
    }

    // ================================================================ charger check

    private int _weakChargerReads;
    private bool _chargerWarned;

    private void ChargerTick(Game? running)
    {
        if (!Battery.IsPluggedIn()) { _weakChargerReads = 0; _chargerWarned = false; return; }
        if (_chargerWarned) return;
        double? rate = null;
        try { if (Windows.Devices.Power.Battery.AggregateBattery.GetReport().ChargeRateInMilliwatts is int mw) rate = mw / 1000.0; }
        catch (Exception) { }
        if (rate is not double w || w > -1.5 || Battery.GetPercent() is >= 99) { _weakChargerReads = 0; return; }
        if (++_weakChargerReads < 3) return; // about a minute and a half of draining while plugged in
        _chargerWarned = true;
        string detail = $"Plugged in, but the battery is still going down by about {Math.Abs(w):0} W" +
                        (running != null ? " with " + running.Name + " running." : ".") + " A stronger USB-C charger (65 W or more) keeps up.";
        if (running != null && !IsActive) GameNotice.Show("The charger can't keep up", detail, "");
        else ShowToast("CHARGER", "The charger can't keep up", detail, "", "info");
    }

    // ================================================================ microSD cards

    private void CardsTick()
    {
        var (_, arrived, left) = Eight.UpdateCards(AllGames);
        // With "Drive connected notice" on, that notice (which opens the card) says it instead.
        foreach (var c in Labs.On("drivenotice") ? new List<Eight.Card>() : arrived)
            ShowToast("MICROSD", c.Label + " is in", c.Games.Count == 0 ? "No games on it yet." : $"{c.Games.Count} {(c.Games.Count == 1 ? "game" : "games")} on it, ready to play.", "", "info", remember: false);
        foreach (var c in left)
            ShowToast("MICROSD", c.Label + " was removed", $"Its {c.Games.Count} {(c.Games.Count == 1 ? "game stays" : "games stay")} in the Library, grayed out, until it's back.", "", "info", remember: false);
        if (arrived.Count + left.Count > 0) Eight.ForgetShelves();
    }

    // ================================================================ drive connected notice

    private HashSet<string>? _knownDrives;

    /// <summary>
    /// Every 5 seconds: a drive that wasn't there before (a USB stick, an external SSD or hard drive, a microSD card)
    /// gets a notification; selecting it opens the drive in SquareDeck's File Explorer. Drives present when
    /// SquareDeck starts (or when the feature is turned on) don't.
    /// </summary>
    private void DrivesTick()
    {
        List<DriveInfo> drives;
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Removable or DriveType.Fixed or DriveType.CDRom && d.IsReady).ToList(); }
        catch (Exception) { return; }
        var now = drives.Select(d => d.Name.ToUpperInvariant()).ToHashSet();
        if (_knownDrives == null) { _knownDrives = now; return; }
        foreach (var d in drives.Where(d => !_knownDrives.Contains(d.Name.ToUpperInvariant())))
        {
            string letter = d.Name.TrimEnd('\\');
            string kind = d.DriveType switch
            {
                DriveType.Removable => d.TotalSize < 300L * 1024 * 1024 * 1024 && IsCardReader(d) ? "microSD card" : "USB drive",
                DriveType.CDRom => "Disc",
                _ => "External drive"
            };
            string name = d.VolumeLabel.Length > 0 ? d.VolumeLabel : kind;
            string size = "";
            try { size = $"{Labs.Size(d.TotalSize)}, {Labs.Size(d.AvailableFreeSpace)} free  \u00B7  "; } catch (Exception) { }
            ShowToast(kind.ToUpperInvariant() + " CONNECTED", $"{name} ({letter})", size + "Select to open it in File Explorer", "", "drive");
        }
        _knownDrives = now;
    }

    /// <summary>A removable drive that's probably a memory card (the Ally's card reader shows as an SD device).</summary>
    private static bool IsCardReader(DriveInfo d)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices");
            if (key?.GetValue(@"\DosDevices\" + d.Name.TrimEnd('\\')) is byte[] data)
            {
                string id = System.Text.Encoding.Unicode.GetString(data);
                return id.Contains("SD", StringComparison.OrdinalIgnoreCase) && !id.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>A drive notification was selected: its drive in File Explorer, if it's still connected.</summary>
    private void OpenDriveNotification(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\(([A-Za-z]):\)\s*$");
        string root = m.Success ? m.Groups[1].Value.ToUpperInvariant() + ":\\" : "";
        if (root.Length > 0 && Directory.Exists(root)) Push(new ExplorerScreen(root));
        else _ = AskAsync(title, "That drive isn't connected any more.", "OK");
    }

    // ================================================================ achievement pop-ups

    private readonly Dictionary<int, HashSet<string>> _toastSeen = new();
    private readonly Dictionary<string, HashSet<string>> _raSeen = new();

    private async Task AchievementToastsTickAsync(Game? running)
    {
        if (running is { IsSteam: true })
        {
            var list = await Labs.AchievementsAsync(running.AppId, CancellationToken.None);
            if (list != null)
            {
                var unlocked = list.Where(a => a.Unlocked).Select(a => a.Api).ToHashSet();
                if (_toastSeen.TryGetValue(running.AppId, out var before))
                    foreach (var a in list.Where(a => a.Unlocked && !before.Contains(a.Api)).Take(3))
                        AchievementToast.Show(a.Name, a.Description, a.Icon, running.Name, "Steam");
                        AllyControl.React("achievement");
                _toastSeen[running.AppId] = unlocked;
            }
        }
        if (RetroEight.Running is { } retro && Settings.RaUser.Length > 0 && Settings.RaApiKey.Length > 0)
        {
            var progress = await RetroExtras.ProgressAsync(retro, Settings);
            if (progress != null)
            {
                var earned = progress.Achievements.Where(a => a.Earned != null).Select(a => a.Title).ToHashSet();
                if (_raSeen.TryGetValue(retro.Key, out var before))
                    foreach (var a in progress.Achievements.Where(a => a.Earned != null && !before.Contains(a.Title)).Take(3))
                        AchievementToast.Show(a.Title, a.Description + $"  ·  {a.Points} points", a.Badge, retro.Title, "RetroAchievements");
                        AllyControl.React("achievement");
                _raSeen[retro.Key] = earned;
            }
        }
    }

    // ================================================================ Windows Update guard (while playing)

    private bool _guardWarned;

    private async Task UpdateGuardTickAsync(Game running)
    {
        if (_guardWarned) return;
        bool pending;
        try { pending = await Task.Run(WindowsUpdate.RestartPending); } catch (Exception) { return; }
        if (!pending) return;
        _guardWarned = true;
        var hours = Eight.ActiveHours();
        bool inHours = hours is (int start, int end) && (start <= end ? DateTime.Now.Hour >= start && DateTime.Now.Hour < end : DateTime.Now.Hour >= start || DateTime.Now.Hour < end);
        GameNotice.Show("Windows wants to restart", inHours
            ? "It won't restart by itself during your active hours, but save " + running.Name + " when you can."
            : "Save " + running.Name + " soon: it's outside your active hours, so Windows may restart.", "");
    }

    // ================================================================ crash recovery

    private static string RunningMarker => Path.Combine(Settings.DataDir, "running.flag");
    private string _recoveryNoted = "";

    private void StartCrashRecovery()
    {
        bool unclean = File.Exists(RunningMarker);
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(RunningMarker, DateTime.Now.ToString("s"));
            Application.Current.Exit += (_, _) => { try { File.Delete(RunningMarker); } catch (Exception) { } };
        }
        catch (Exception) { }
        if (!unclean || !Labs.On("crashrecovery")) return;
        string where = Labs.Get("recovery.screen");
        _ = Task.Delay(TimeSpan.FromSeconds(4)).ContinueWith(_ => OfferRecoveryAsync(where), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task OfferRecoveryAsync(string where)
    {
        var parts = where.Split('|', 3);
        string label = parts.Length >= 3 ? parts[2] : "";
        bool canGoBack = parts.Length >= 3 && parts[0].Length > 0 && RecreateScreen(parts[0], parts[1], test: true) != null;
        var options = new List<string>();
        if (canGoBack) options.Add("Go back to " + label);
        options.Add("Send the log");
        options.Add("Not now");
        int choice = await AskAsync("SquareDeck closed unexpectedly", "Sorry about that. " +
            (canGoBack ? "You were on " + label + "." : "") + " The log of what went wrong can be sent as a bug report, with personal details removed.", options.ToArray());
        if (choice < 0) return;
        string picked = options[choice];
        if (picked.StartsWith("Go back") && RecreateScreen(parts[0], parts[1], test: false) is { } screen) Push(screen);
        else if (picked == "Send the log") Push(new BugReportScreen());
    }

    /// <summary>Remembers the page on top, so it can be offered again after a crash.</summary>
    private void NoteScreenForRecovery()
    {
        if (!Labs.On("crashrecovery")) return;
        var top = _screens.LastOrDefault(s => !s.IsDialog);
        string note = top == null ? "" : top is SettingsScreen settings ? $"{top.GetType().FullName}|{settings.CurrentCategory}|Settings"
            : top.GetType().GetConstructor(Type.EmptyTypes) != null ? $"{top.GetType().FullName}||{ScreenLabel(top)}" : "";
        if (note == _recoveryNoted) return;
        _recoveryNoted = note;
        Labs.Put("recovery.screen", note);
    }

    private static string ScreenLabel(Screen s)
    {
        string name = s.GetType().Name;
        if (name.EndsWith("Screen")) name = name[..^6];
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private static Screen? RecreateScreen(string type, string arg, bool test)
    {
        try
        {
            var t = typeof(MainWindow).Assembly.GetType(type);
            if (t == null || !typeof(Screen).IsAssignableFrom(t)) return null;
            if (t == typeof(SettingsScreen)) return test ? new DummyScreen() : new SettingsScreen(arg.Length > 0 ? arg : null);
            if (t.GetConstructor(Type.EmptyTypes) == null) return null;
            return test ? new DummyScreen() : Activator.CreateInstance(t) as Screen;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Stands in for a page when only checking that it could be made.</summary>
    private sealed class DummyScreen : Screen { }
}

/// <summary>
/// A pop-up for an unlocked achievement, in the current style's colors, shown over the game (it never takes the
/// focus): the badge, its name and what it was for, then it slides away.
/// </summary>
public sealed class AchievementToast : Window
{
    private static AchievementToast? _current;

    public static void Show(string title, string detail, string iconUrl, string game, string source)
    {
        _current?.Close();
        _current = new AchievementToast(title, detail, iconUrl, game, source);
        _current.Show();
        UiSound.Toast();
    }

    private AchievementToast(string title, string detail, string iconUrl, string game, string source)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        Brush Res(string key, Brush fallback) => Application.Current.TryFindResource(key) as Brush ?? fallback;
        var accent = Res("AccentBrush", new SolidColorBrush(Color.FromRgb(0x3E, 0xC1, 0xD3)));
        var card = Res("CardBrush", new SolidColorBrush(Color.FromArgb(0xF0, 0x16, 0x19, 0x22)));
        var fg = Res("FgBrush", Brushes.White);
        var subtle = Res("SubtleBrush", new SolidColorBrush(Color.FromRgb(0xCF, 0xD4, 0xDE)));

        var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = accent, Margin = new Thickness(0, 0, 16, 0) };
        badge.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = 128;
                bmp.EndInit();
                badge.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch (Exception) { }
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340 };
        text.Children.Add(new TextBlock { Text = ("Achievement unlocked  ·  " + source).ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = accent });
        text.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        if (detail.Length > 0) text.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = subtle, TextWrapping = TextWrapping.Wrap, MaxHeight = 38 });
        text.Children.Add(new TextBlock { Text = game, FontSize = 12, Foreground = subtle, Opacity = 0.8, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(badge);
        row.Children.Add(text);
        var border = new Border
        {
            Child = row, Padding = new Thickness(14, 12, 22, 12), Margin = new Thickness(14), CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 14)),
            Background = card, BorderBrush = accent, BorderThickness = new Thickness(2),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 }
        };
        Content = border;

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            double top = area.Top + 18;
            Top = top - 30;
            Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(TopProperty, new DoubleAnimation(top - 30, top, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
            var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
            close.Tick += (_, _) =>
            {
                close.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (_, _) => { Close(); if (ReferenceEquals(_current, this)) _current = null; };
                BeginAnimation(OpacityProperty, fade);
            };
            close.Start();
        };
    }
}
