using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;
using Path = System.IO.Path;

namespace SquareDeck;

/// <summary>
/// The experimental features that live on the home screen or run in the background: the style schedule,
/// overnight downloads, the refund reminder, screenshots on achievements, milestones, tilting covers, genre
/// music, dusty tiles and the companion. Each does nothing unless it's turned on (Settings > Experimental).
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _labsTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _tiltTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _genreTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private readonly DispatcherTimer _companionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Windows.Devices.Sensors.Accelerometer? _accel;
    private double _tiltX, _tiltY;
    private FrameworkElement? _tiltedCover, _tiltedFace;
    private string? _genreTrack;
    private DateTime? _celebratedAt;
    private readonly Dictionary<int, HashSet<string>> _achievementsSeen = new();
    private readonly HashSet<string> _refundWarned = new();
    private int _overnightIdle;
    private bool _labsBusy;

    /// <summary>Adds an element over the whole window (across all of the layout's rows).</summary>
    private void AddOverlay(UIElement element)
    {
        Grid.SetRowSpan(element, Math.Max(1, RootGrid.RowDefinitions.Count));
        Grid.SetColumnSpan(element, Math.Max(1, RootGrid.ColumnDefinitions.Count));
        RootGrid.Children.Add(element);
    }

    private void InitLabs()
    {
        Labs.Init(Settings);
        _ = Task.Run(Updater.CleanUpDownloads); // installers left from an update that's finished
        if (int.TryParse(Labs.Get("sticks.zone"), out var zone) && zone is >= 10 and <= 90) Gamepad.StickThreshold = (short)(32767 * zone / 100);
        Labs.Changed += OnLabChanged;
        _labsTimer.Tick += (_, _) => _ = LabsTickAsync();
        _labsTimer.Start();
        _tiltTimer.Tick += (_, _) => TiltTick();
        _genreTimer.Tick += (_, _) => GenreTick();
        _companionTimer.Tick += (_, _) => CompanionTick();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            foreach (var f in Labs.Features) if (f.Id is not ("coverdim" or "covereffect")) OnLabChanged(f.Id);
            ApplyCoverLabs();
            NoticeCrash();
            _ = LabsTickAsync();
        }));
    }

    /// <summary>Minutes of the current session of a running game (0 when it isn't running).</summary>
    public double RunningMinutes(Game game) => _sessionStart.TryGetValue(game.Key, out var started) ? (DateTime.Now - started).TotalMinutes : 0;

    private void OnLabChanged(string id)
    {
        switch (id)
        {
            case "tilt": SetTilt(Labs.On("tilt")); break;
            case "coverdim": case "covereffect": ApplyCoverLabs(); break;
            case "dust": RefreshDust(); break;
            case "companion": SetCompanion(Labs.On("companion")); break;
            case "genresound":
                if (!Labs.On("genresound") && _genreTrack != null) { _genreTrack = null; UpdateMenuMusic(); }
                break;
            case "overnight":
                if (!Labs.On("overnight")) { Labs.KeepAwake(false); Labs.Put("overnight.active", "0", save: false); }
                break;
        }
    }

    /// <summary>Once a minute: the background parts of the experimental features.</summary>
    private async Task LabsTickAsync()
    {
        if (_labsBusy) return;
        _labsBusy = true;
        try
        {
            var running = AllGames.FirstOrDefault(g => g.IsRunning);
            if (Labs.On("refund"))
            {
                Labs.NoteSeen(AllGames);
                if (running is { IsSteam: true } && !_refundWarned.Contains(running.Key) &&
                    Labs.RefundWindow(new[] { running }, RunningMinutes).FirstOrDefault() is { MinutesLeft: <= 20 } r)
                {
                    _refundWarned.Add(running.Key);
                    string detail = $"{r.MinutesPlayed:0} min played. Steam's 2-hour refund limit is {r.MinutesLeft:0} min of play away.";
                    if (IsActive) ShowToast("REFUND WINDOW", running.Name, detail, "", "info", game: running);
                    else GameNotice.Show("Refund window closing: " + running.Name, detail, "");
                }
            }
            if (Labs.On("styleschedule") && running == null && Labs.ScheduledStyle(DateTime.Now) is { } wanted && wanted != Labs.Get("schedule.applied"))
            {
                Labs.Put("schedule.applied", wanted);
                if (!string.Equals(Settings.UiStyle, wanted, StringComparison.OrdinalIgnoreCase)) UseStyle(UiStyles.Find(wanted));
            }
            if (Labs.On("overnight")) OvernightTick(running);
            if (Labs.On("milestones") && running == null && IsActive && _screens.Count == 0)
            {
                var fresh = Labs.NewMilestones(AllGames, Playtime, Achievements, Library);
                if (fresh.Count > 0) Celebrate(fresh[0].Title, fresh.Count > 1 ? fresh[0].Detail + $"  (+{fresh.Count - 1} more)" : fresh[0].Detail);
            }
            if (Labs.On("achshot") && running is { IsSteam: true } && !IsActive) await AchievementShotAsync(running);
            if (Labs.On("dust")) RefreshDust();
        }
        catch (Exception ex) { CrashLog.Write("Experimental features", ex); }
        finally { _labsBusy = false; }
    }

    // ---------------------------------------------------------------- darker covers, cover effect

    /// <summary>Applies "Darken covers that aren't highlighted" and "Cover art effect" to the home row.</summary>
    public void ApplyCoverLabs()
    {
        bool dim = Labs.On("coverdim");
        int darkness = int.TryParse(Labs.Get("coverdim.dark", "40"), out var d) ? Math.Clamp(d, 0, 90) : 40;
        int soften = int.TryParse(Labs.Get("coverdim.soft", "0"), out var s) ? Math.Clamp(s, 0, 80) : 0;
        Application.Current.Resources["CoverDimBrush"] = dim && darkness > 0 ? Frozen(Color.FromArgb((byte)(darkness * 255 / 100), 0, 0, 0)) : Brushes.Transparent;
        Application.Current.Resources["CoverFadeBrush"] = dim && soften > 0 ? Frozen(Color.FromArgb((byte)(soften * 255 / 100), 128, 128, 128)) : Brushes.Transparent;

        string effect = Labs.On("covereffect") ? Labs.Get("covereffect.kind", "retro") : "";
        CoverEffects.Kind = effect;
        CoverEffects.Detail = int.TryParse(Labs.Get("covereffect.pixels", "48"), out var px) ? Math.Clamp(px, 24, 128) : 48;
        CoverEffects.Duotone = Labs.Get("covereffect.duotone", "accent");
        CoverEffects.Reset();
        Game.CoverEffect = effect.Length > 0 ? CoverEffects.Apply : null;
        Application.Current.Resources["CoverScaling"] = CoverEffects.IsPixelArt(effect) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        foreach (var g in _view) g.RefreshShape();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------------------------------------------------------------- close store launchers after playing

    /// <summary>Each store's launcher, by the store name games carry.</summary>
    private static readonly Dictionary<string, string[]> LauncherProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Epic"] = new[] { "EpicGamesLauncher", "EpicWebHelper" }, ["Epic Games"] = new[] { "EpicGamesLauncher", "EpicWebHelper" },
        ["EA"] = new[] { "EADesktop", "EABackgroundService" }, ["EA app"] = new[] { "EADesktop", "EABackgroundService" },
        ["Ubisoft"] = new[] { "upc", "UbisoftConnect" }, ["Ubisoft Connect"] = new[] { "upc", "UbisoftConnect" },
        ["GOG"] = new[] { "GalaxyClient", "GalaxyClientService" }, ["GOG Galaxy"] = new[] { "GalaxyClient" },
        ["Battle.net"] = new[] { "Battle.net" }, ["Amazon"] = new[] { "Amazon Games" }, ["Amazon Games"] = new[] { "Amazon Games" },
        ["itch"] = new[] { "itch" }, ["Rockstar"] = new[] { "Launcher", "RockstarService" },
    };

    /// <summary>
    /// After a game ends: closes its store's launcher, unless another game from the same store is still running.
    /// Steam is only closed when chosen (it also handles downloads and friends).
    /// </summary>
    private async Task CloseLauncherAfterAsync(Game game)
    {
        await Task.Delay(TimeSpan.FromSeconds(8)); // let the store finish syncing saves and playtime
        if (AllGames.Any(g => g.IsRunning && g.SourceName == game.SourceName)) return;
        if (game.IsSteam)
        {
            if (Labs.Get("closelaunchers.steam") == "1" && !_steam.Any(g => g.UpdateText.StartsWith("Updating", StringComparison.Ordinal))) ProcessUtil.OpenOutside("steam://exit");
            return;
        }
        if (!LauncherProcesses.TryGetValue(game.SourceName, out var names)) return;
        await Task.Run(() =>
        {
            foreach (var name in names)
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try { if (!p.CloseMainWindow() || !p.WaitForExit(3000)) p.Kill(true); }
                    catch (Exception) { }
                    finally { p.Dispose(); }
                }
        });
    }

    // ---------------------------------------------------------------- install estimate

    /// <summary>"Download: about 45 GB, about 1 h 10 min…" and the space left on each drive, for the install question.</summary>
    private async Task<string> InstallEstimateAsync(Game game)
    {
        long? size = null;
        double? speed = null;
        await RunBusyAsync("Working out the download", async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(14));
            var sizeTask = Labs.RequiredSpaceAsync(game.AppId, cts.Token);
            var speedTask = Labs.MeasureDownloadSpeedAsync(cts.Token);
            size = await sizeTask;
            speed = await speedTask;
        });
        var b = new System.Text.StringBuilder("\n\n");
        if (size is long s)
        {
            b.Append("Download: about ").Append(Labs.Size(s));
            if (speed is double bps && bps > 0)
            {
                double minutes = s / bps / 60;
                b.Append(", about ").Append(minutes < 60 ? $"{Math.Ceiling(minutes):0} min" : $"{minutes / 60:0.#} h").Append($" at {bps * 8 / 1_000_000:0} Mbps");
            }
            b.Append(" (the size its store page lists; the actual download is often smaller).\n");
        }
        else b.Append("Its store page doesn't list a size.").Append(speed is double bps2 ? $" Your connection: {bps2 * 8 / 1_000_000:0} Mbps." : "").Append('\n');
        var drives = Labs.Drives();
        if (drives.Count > 0)
            b.Append("Free after installing: ").Append(string.Join(",  ", drives.Select(d =>
                $"{d.Drive}{(d.Removable ? " (microSD)" : "")} {(size is long n ? (d.Free - n > 0 ? Labs.Size(d.Free - n) : "not enough space") : Labs.Size(d.Free) + " now")}")));
        return b.ToString();
    }

    // ---------------------------------------------------------------- overnight downloads

    private void OvernightTick(Game? running)
    {
        if (Labs.Get("overnight.active") != "1") return;
        bool plugged = Battery.IsPluggedIn();
        bool downloading = _steam.Any(g => g.UpdateText.StartsWith("Updating", StringComparison.Ordinal)) || InstallQueue.Count > 0 || _installing != null;
        if (!plugged)
        {
            Labs.KeepAwake(false);
            Labs.Put("overnight.status", "Waiting for the charger", save: false);
            return;
        }
        if (downloading)
        {
            _overnightIdle = 0;
            Labs.KeepAwake(true);
            Labs.Put("overnight.status", "Downloading: staying awake", save: false);
            return;
        }
        // Nothing downloading for three checks in a row: done.
        if (++_overnightIdle < 3) return;
        _overnightIdle = 0;
        Labs.KeepAwake(false);
        Labs.Put("overnight.active", "0");
        Labs.Put("overnight.status", "Finished at " + DateTime.Now.ToString("h:mm tt"));
        Notifications.Add("download", "Overnight downloads finished", "Everything finished downloading at " + DateTime.Now.ToString("h:mm tt") + ".");
        if (Labs.Get("overnight.sleep", "1") == "1" && running == null) Power.Sleep();
    }

    // ---------------------------------------------------------------- screenshots on achievements

    private async Task AchievementShotAsync(Game game)
    {
        var list = await Labs.AchievementsAsync(game.AppId, CancellationToken.None);
        if (list == null) return;
        var unlocked = list.Where(a => a.Unlocked).Select(a => a.Api).ToHashSet();
        if (!_achievementsSeen.TryGetValue(game.AppId, out var before))
        {
            _achievementsSeen[game.AppId] = unlocked; // the first look: what was already unlocked
            return;
        }
        var fresh = list.Where(a => a.Unlocked && !before.Contains(a.Api)).ToList();
        _achievementsSeen[game.AppId] = unlocked;
        if (fresh.Count == 0 || !game.IsRunning) return;
        Labs.GameBarScreenshot();
        await Labs.NameNewestCaptureAsync(CapturesScreen.Folder, fresh[0].Name);
    }

    // ---------------------------------------------------------------- crash notice (bug report)

    private void NoticeCrash()
    {
        if (!Labs.On("bugreport") || !File.Exists(CrashLog.Path)) return;
        var when = File.GetLastWriteTime(CrashLog.Path).ToString("s");
        if (when == Labs.Get("crash.seen")) return;
        bool first = !Labs.Has("crash.seen");
        Labs.Put("crash.seen", when);
        if (!first) Notifications.Add("info", "SquareDeck ran into a problem", "Report it in one tap from Settings > Experimental > One-tap bug report.");
    }

    // ---------------------------------------------------------------- milestones: confetti

    /// <summary>A burst of confetti with a banner. Used by milestones and the backlog boss.</summary>
    public void Celebrate(string title, string detail)
    {
        _celebratedAt = DateTime.Now;
        UiSound.Toast();
        var layer = new Canvas { IsHitTestVisible = false };
        Panel.SetZIndex(layer, 9000);
        var rand = new Random();
        var colors = new[] { "#FF5A5F", "#FFC93C", "#3EC1D3", "#7B61FF", "#4CD964", "#FF8C42" };
        int pieces = Theme.ReduceMotion ? 0 : 90;
        for (int i = 0; i < pieces; i++)
        {
            var piece = new Rectangle { Width = 6 + rand.Next(6), Height = 10 + rand.Next(8), Fill = (Brush)new BrushConverter().ConvertFromString(colors[rand.Next(colors.Length)])!,
                RenderTransformOrigin = new Point(0.5, 0.5) };
            var spin = new RotateTransform(rand.Next(360));
            piece.RenderTransform = spin;
            double x = rand.NextDouble() * 1280;
            Canvas.SetLeft(piece, x);
            layer.Children.Add(piece);
            var time = TimeSpan.FromSeconds(2.2 + rand.NextDouble() * 1.6);
            var delay = TimeSpan.FromSeconds(rand.NextDouble() * 0.6);
            piece.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-30, 760, time) { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            piece.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + rand.Next(-120, 120), time) { BeginTime = delay });
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(spin.Angle, spin.Angle + rand.Next(-720, 720), time) { BeginTime = delay });
        }
        var banner = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(36, 22, 36, 22), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
        banner.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        Panel.SetZIndex(banner, 9001);
        var words = new StackPanel();
        var t1 = Ui.Text(title, 30, "FgBrush", bold: true);
        t1.HorizontalAlignment = HorizontalAlignment.Center;
        var t2 = Ui.Text(detail, 17, "SubtleBrush");
        t2.HorizontalAlignment = HorizontalAlignment.Center;
        t2.Margin = new Thickness(0, 6, 0, 0);
        words.Children.Add(t1);
        words.Children.Add(t2);
        banner.Child = words;
        AddOverlay(layer);
        AddOverlay(banner);
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.8))));
        banner.BeginAnimation(OpacityProperty, fade);
        var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.2) };
        done.Tick += (_, _) => { done.Stop(); RootGrid.Children.Remove(layer); RootGrid.Children.Remove(banner); };
        done.Start();
        CompanionReact("spin");
    }

    // ---------------------------------------------------------------- dusty tiles

    private void RefreshDust()
    {
        bool on = Labs.On("dust");
        foreach (var g in _view) g.DustImage = on ? Labs.DustFor(g.Actual) : null;
    }

    // ---------------------------------------------------------------- sound that follows the genre

    /// <summary>The music track the genre feature wants on Home now, or null for the chosen music.</summary>
    private string? GenreMusic => Labs.On("genresound") ? _genreTrack : null;

    private int _labsLastIndex = -1;

    private void OnHomeSelectionForLabs()
    {
        int index = GameList.SelectedIndex;
        if (_labsLastIndex >= 0 && index >= 0) CompanionLook(Math.Sign(index - _labsLastIndex));
        _labsLastIndex = index;
        if (!Labs.On("genresound")) return;
        _genreTimer.Stop();
        _genreTimer.Start(); // waits a moment, so scrolling past tiles doesn't flip the music each step
    }

    private void GenreTick()
    {
        _genreTimer.Stop();
        if (!Labs.On("genresound") || Settings.MenuMusic.Length == 0) return;
        var game = (GameList.SelectedItem as Game)?.Actual;
        var track = game != null && game.IsGame ? Labs.TrackForGenre(GenreCache.Of(game)) : null;
        if (track == _genreTrack) return;
        // Fade out, change, fade back in.
        var player = _menuMusic;
        if (player == null || !_menuMusicPlaying) { _genreTrack = track; UpdateMenuMusic(); return; }
        double target = Math.Clamp(Settings.MenuMusicVolume, 0, 100) / 100.0;
        int step = 0;
        var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        fade.Tick += (_, _) =>
        {
            step++;
            if (step <= 10) { player.Volume = target * (1 - step / 10.0); return; }
            if (step == 11)
            {
                _genreTrack = track;
                UpdateMenuMusic();
                if (_menuMusic != null) _menuMusic.Volume = 0;
                return;
            }
            if (_menuMusic != null) _menuMusic.Volume = target * Math.Min(1, (step - 11) / 12.0);
            if (step >= 23) fade.Stop();
        };
        fade.Start();
    }

    // ---------------------------------------------------------------- covers that tilt

    private void SetTilt(bool on)
    {
        if (on)
        {
            try
            {
                _accel ??= Windows.Devices.Sensors.Accelerometer.GetDefault();
                if (_accel != null)
                {
                    _accel.ReportInterval = Math.Max(_accel.MinimumReportInterval, 33u);
                    _accel.ReadingChanged -= OnAcceleration;
                    _accel.ReadingChanged += OnAcceleration;
                }
            }
            catch (Exception) { _accel = null; }
            _tiltTimer.Start();
        }
        else
        {
            _tiltTimer.Stop();
            if (_accel != null) _accel.ReadingChanged -= OnAcceleration;
            ResetTilt();
        }
    }

    /// <summary>Whether this PC has a motion sensor for the tilting covers.</summary>
    public bool HasMotionSensor
    {
        get
        {
            try { return (_accel ??= Windows.Devices.Sensors.Accelerometer.GetDefault()) != null; }
            catch (Exception) { return false; }
        }
    }

    private void OnAcceleration(Windows.Devices.Sensors.Accelerometer sender, Windows.Devices.Sensors.AccelerometerReadingChangedEventArgs args)
    {
        // Smoothed, so the covers drift rather than jitter.
        _tiltX = _tiltX * 0.8 + Math.Clamp(args.Reading.AccelerationX / 0.5, -1, 1) * 0.2;
        _tiltY = _tiltY * 0.8 + Math.Clamp(args.Reading.AccelerationY / 0.5, -1, 1) * 0.2;
    }

    private void TiltTick()
    {
        if (_accel == null || _screens.Count > 0 || !IsActive || Theme.ReduceMotion) { ResetTilt(); return; }
        var container = GameList.SelectedItem == null ? null : GameList.ItemContainerGenerator.ContainerFromItem(GameList.SelectedItem) as FrameworkElement;
        var cover = container == null ? null : Named(container, "CoverImage");
        var face = container == null ? null : Named(container, "ContinueFace");
        if (!ReferenceEquals(cover, _tiltedCover)) { ResetTilt(); _tiltedCover = cover; _tiltedFace = face; }
        if (cover == null) return;
        // The cover moves one way, the "Continue" logo and name the other, at a smaller depth.
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        cover.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.08, 1.08), new TranslateTransform(-_tiltX * 9, _tiltY * 9) } };
        if (face != null) face.RenderTransform = new TranslateTransform(_tiltX * 5, -_tiltY * 5);
    }

    private void ResetTilt()
    {
        if (_tiltedCover != null) _tiltedCover.RenderTransform = Transform.Identity;
        if (_tiltedFace != null) _tiltedFace.RenderTransform = Transform.Identity;
        _tiltedCover = _tiltedFace = null;
    }

    private static FrameworkElement? Named(DependencyObject root, string name)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (Named(child, name) is { } found) return found;
        }
        return null;
    }

    // ---------------------------------------------------------------- companion

    private Grid? _companion;
    private Canvas? _companionSprite, _companionFx;
    private Border? _companionBubble;
    private readonly ScaleTransform _companionScale = new(1, 1);
    private readonly RotateTransform _companionTurn = new(0);
    private readonly TranslateTransform _companionMove = new();
    private string _companionDrawn = "";
    private int _companionTicks;
    private string? _companionAction;
    private DateTime _companionActionUntil;
    private int _companionLook;
    private DateTime _companionLookUntil;
    private bool _companionWaveFrame;
    private readonly DispatcherTimer _companionFrames = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private bool _companionWasPlaying;

    private void SetCompanion(bool on)
    {
        if (on && _companion == null)
        {
            _companionSprite = new Canvas { Width = 72, Height = 72, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            _companionFx = new Canvas { IsHitTestVisible = false };
            _companion = new Grid { Width = 120, Height = 130, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 74), IsHitTestVisible = false };
            _companionSprite.RenderTransformOrigin = new Point(0.5, 1);
            _companionSprite.RenderTransform = new TransformGroup { Children = { _companionScale, _companionTurn, _companionMove } };
            _companion.Children.Add(_companionSprite);
            _companion.Children.Add(_companionFx);
            Panel.SetZIndex(_companion, 40);
            _companionBubble = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5), HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 206), Opacity = 0, IsHitTestVisible = false };
            _companionBubble.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            Panel.SetZIndex(_companionBubble, 40);
            AddOverlay(_companion);
            AddOverlay(_companionBubble);
            _companionDrawn = "";
            _companionFrames.Tick -= CompanionFrame;
            _companionFrames.Tick += CompanionFrame;
            _companionTimer.Start();
            CompanionTick();
            CompanionReact("wave");
        }
        else if (!on && _companion != null)
        {
            _companionTimer.Stop();
            _companionFrames.Stop();
            RootGrid.Children.Remove(_companion);
            if (_companionBubble != null) RootGrid.Children.Remove(_companionBubble);
            _companion = null;
            _companionBubble = null;
        }
    }

    /// <summary>The companion's look changed (character, color, name or outfit): draw it again.</summary>
    public void RefreshCompanion()
    {
        _companionDrawn = "";
        CompanionTick();
    }

    /// <summary>
    /// Plays one of the companion's reactions: wave (hello), bye (a game starts), jump (an achievement), spin (a
    /// milestone), hearts (a new favorite), worried (low battery), dance, yawn, sleep, peek. Also used by its page to try them.
    /// </summary>
    public void CompanionReact(string action)
    {
        if (_companion == null || _companionSprite == null) return;
        _companionAction = action;
        double seconds = action switch { "spin" => 1.6, "dance" => 3.2, "sleep" => 4, "wave" or "bye" => 2.2, _ => 1.8 };
        _companionActionUntil = DateTime.Now.AddSeconds(seconds);
        _companionFrames.Start();
        var reduce = Theme.ReduceMotion;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation Anim(double from, double to, double secs, bool back = false, int repeat = 1) =>
            new(from, to, TimeSpan.FromSeconds(secs)) { AutoReverse = back, RepeatBehavior = new RepeatBehavior(repeat), EasingFunction = ease };
        if (!reduce)
            switch (action)
            {
                case "jump":
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -26, 0.22, true, 2));
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.82, 1, 0.18));
                    Sparkles("✦", 7, "#FFD84D");
                    break;
                case "spin":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = new RepeatBehavior(2) });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -14, 0.4, true, 2));
                    Sparkles("★", 9, "#FFC93C");
                    break;
                case "hearts":
                    _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 1.12, 0.2, true, 2));
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 1.12, 0.2, true, 2));
                    Sparkles("♥", 6, "#FF5A7A", rise: true);
                    break;
                case "worried":
                    _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-4, 4, TimeSpan.FromMilliseconds(60)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(8) });
                    Sparkles("💧", 1, "#7FB8FF", rise: false);
                    break;
                case "dance":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, 12, TimeSpan.FromSeconds(0.4)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -6, 0.2, true, 8));
                    Sparkles("♪", 4, "#C3A6FF", rise: true);
                    break;
                case "yawn":
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 1.1, 0.6, true));
                    break;
                case "sleep":
                    Sparkles("z", 3, "#AAB4C8", rise: true);
                    break;
                case "peek":
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(40, 0, 0.5));
                    break;
                case "wave":
                case "bye":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                    break;
            }
        string? say = action switch
        {
            "wave" => new[] { "Hi!", "Welcome back!", "Hello!" }[Random.Shared.Next(3)],
            "bye" => "Have fun!",
            "jump" => "Achievement!",
            "spin" => "Woohoo!",
            "hearts" => "Ooh, a favorite!",
            "worried" => "Battery's low…",
            "dance" => "♪ ♫",
            "yawn" => "*yawn*",
            "sleep" => "Zzz…",
            _ => null
        };
        if (say != null) CompanionSay(say);
        _companionDrawn = "";
        CompanionTick();
    }

    private void CompanionSay(string text)
    {
        if (_companionBubble == null) return;
        var line = new TextBlock();
        line.Inlines.Add(new System.Windows.Documents.Run(CompanionArt.Name + ": ") { FontWeight = FontWeights.Normal });
        line.Inlines.Add(new System.Windows.Documents.Run(text) { FontWeight = FontWeights.Bold });
        line.FontSize = 14;
        line.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        _companionBubble.Child = line;
        var show = new DoubleAnimationUsingKeyFrames();
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
        _companionBubble.BeginAnimation(OpacityProperty, show);
    }

    /// <summary>Little symbols that burst out (or float up) around the companion.</summary>
    private void Sparkles(string symbol, int count, string hex, bool rise = false)
    {
        if (_companionFx == null) return;
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        for (int i = 0; i < count; i++)
        {
            var t = new TextBlock { Text = symbol, Foreground = brush, FontSize = 14 + Random.Shared.Next(8), FontWeight = FontWeights.Bold };
            double x = 50 + Random.Shared.Next(-36, 36), y = 70 + Random.Shared.Next(-20, 10);
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            _companionFx.Children.Add(t);
            var time = TimeSpan.FromSeconds(rise ? 1.6 : 0.9);
            var delay = TimeSpan.FromSeconds(i * (rise ? 0.25 : 0.04));
            t.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, rise ? y - 70 : y - 30 - Random.Shared.Next(30), time) { BeginTime = delay });
            if (!rise) t.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + Random.Shared.Next(-40, 40), time) { BeginTime = delay });
            var fade = new DoubleAnimation(1, 0, time) { BeginTime = delay };
            fade.Completed += (_, _) => _companionFx?.Children.Remove(t);
            t.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Eyes follow the highlight as you move along the home row.</summary>
    private void CompanionLook(int direction)
    {
        if (_companion == null || direction == 0) return;
        _companionLook = direction;
        _companionLookUntil = DateTime.Now.AddSeconds(1.4);
        CompanionTick();
    }

    /// <summary>Game started (bye) or ended (welcome back).</summary>
    private void CompanionGameState(bool playing)
    {
        if (playing == _companionWasPlaying) return;
        _companionWasPlaying = playing;
        if (!playing) CompanionReact("wave");
    }

    private void CompanionFrame(object? sender, EventArgs e)
    {
        if (_companionAction == null || DateTime.Now > _companionActionUntil)
        {
            _companionFrames.Stop();
            _companionAction = null;
            _companionDrawn = "";
            CompanionTick();
            return;
        }
        if (_companionAction is "wave" or "bye") { _companionWaveFrame = !_companionWaveFrame; CompanionTick(); }
    }

    private void CompanionTick()
    {
        if (_companion == null || _companionSprite == null) return;
        // On Home, and on its own page so its reactions can be tried there.
        bool home = (_screens.Count == 0 || _screens[^1] is CompanionScreen) && IntroLayer.Children.Count == 0;
        _companion.Visibility = _companionBubble!.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        if (!home) return;
        _companionTicks++;
        string mood = Labs.CompanionMood(Playtime, Achievements, _celebratedAt);
        mood = _companionAction switch
        {
            "jump" or "spin" => "cheer",
            "hearts" => "love",
            "worried" => "worried",
            "sleep" => "sleepy",
            "yawn" => "bored",
            "dance" => "dance",
            _ => mood
        };
        int look = DateTime.Now < _companionLookUntil ? _companionLook : 0;
        bool wave = _companionAction is "wave" or "bye" && _companionWaveFrame;
        bool blink = mood is not ("sleepy" or "cheer" or "love") && _companionAction == null && _companionTicks % 9 == 0;
        string key = $"{CompanionArt.SpeciesId}|{CompanionArt.ColorHex}|{mood}|{blink}|{Labs.Outfit}|{look}|{wave}";
        if (key != _companionDrawn)
        {
            _companionDrawn = key;
            CompanionArt.Draw(_companionSprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, blink, Labs.Outfit, look, wave);
        }
        if (_companionAction != null || Theme.ReduceMotion) return;
        // Idle: a gentle bob; now and then something on its own.
        _companionMove.Y = _companionTicks % 4 < 2 ? -2 : 0;
        if (_companionTicks % 40 == 0)
        {
            if (mood == "sleepy") CompanionReact("sleep");
            else if (mood == "bored") CompanionReact("yawn");
            else if (_menuMusicPlaying || LocalMusic.IsPlaying) CompanionReact("dance");
            else CompanionSay(new[] { "Ready when you are.", "What are we playing?", "♪", "Pick something fun!" }[_companionTicks / 40 % 4]);
        }
    }
}
