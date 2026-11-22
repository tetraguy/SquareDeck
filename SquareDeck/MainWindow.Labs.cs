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

    private void InitLabs()
    {
        Labs.Init(Settings);
        if (int.TryParse(Labs.Get("sticks.zone"), out var zone) && zone is >= 10 and <= 90) Gamepad.StickThreshold = (short)(32767 * zone / 100);
        Labs.Changed += OnLabChanged;
        _labsTimer.Tick += (_, _) => _ = LabsTickAsync();
        _labsTimer.Start();
        _tiltTimer.Tick += (_, _) => TiltTick();
        _genreTimer.Tick += (_, _) => GenreTick();
        _companionTimer.Tick += (_, _) => CompanionTick();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            foreach (var f in Labs.Features) OnLabChanged(f.Id);
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
        RootGrid.Children.Add(layer);
        RootGrid.Children.Add(banner);
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.8))));
        banner.BeginAnimation(OpacityProperty, fade);
        var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.2) };
        done.Tick += (_, _) => { done.Stop(); RootGrid.Children.Remove(layer); RootGrid.Children.Remove(banner); };
        done.Start();
        CompanionTick();
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

    private void OnHomeSelectionForLabs()
    {
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

    private Canvas? _companion;
    private Border? _companionBubble;
    private string _companionDrawn = "";
    private int _companionTicks;

    private void SetCompanion(bool on)
    {
        if (on && _companion == null)
        {
            _companion = new Canvas { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 30, 78), IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
            Panel.SetZIndex(_companion, 40);
            _companionBubble = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 156), Opacity = 0, IsHitTestVisible = false };
            _companionBubble.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            Panel.SetZIndex(_companionBubble, 40);
            RootGrid.Children.Add(_companion);
            RootGrid.Children.Add(_companionBubble);
            _companionDrawn = "";
            _companionTimer.Start();
            CompanionTick();
        }
        else if (!on && _companion != null)
        {
            _companionTimer.Stop();
            RootGrid.Children.Remove(_companion);
            if (_companionBubble != null) RootGrid.Children.Remove(_companionBubble);
            _companion = null;
            _companionBubble = null;
        }
    }

    private void CompanionTick()
    {
        if (_companion == null) return;
        bool home = _screens.Count == 0 && IntroLayer.Children.Count == 0;
        _companion.Visibility = _companionBubble!.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        if (!home) return;
        _companionTicks++;
        string mood = Labs.CompanionMood(Playtime, Achievements, _celebratedAt);
        bool blink = mood != "sleepy" && _companionTicks % 9 == 0;
        string key = mood + (blink ? "-blink" : "") + "|" + Labs.Outfit;
        if (key != _companionDrawn)
        {
            _companionDrawn = key;
            DrawCompanion(_companion, mood, blink, Labs.Outfit);
        }
        // A little bob; a jump when cheering.
        if (_companion.RenderTransform is TranslateTransform move && !Theme.ReduceMotion)
            move.Y = mood == "cheer" ? (_companionTicks % 2 == 0 ? -8 : 0) : (_companionTicks % 4 < 2 ? -2 : 0);
        // Now and then it says something.
        if (_companionTicks % 40 == 1)
        {
            string say = mood switch
            {
                "sleepy" => "Zzz…",
                "cheer" => "Yay! Nice one!",
                "bored" => "Play something?",
                _ => new[] { "Hi!", "Ready when you are.", "What are we playing?", "♪" }[_companionTicks / 40 % 4]
            };
            _companionBubble.Child = Ui.Text(say, 14, "FgBrush", bold: true);
            var show = new DoubleAnimationUsingKeyFrames();
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
            _companionBubble.BeginAnimation(OpacityProperty, show);
        }
    }

    /// <summary>The companion: an original round pixel creature, 12 × 12, six screen pixels to each.</summary>
    private static void DrawCompanion(Canvas canvas, string mood, bool blink, string outfit)
    {
        canvas.Children.Clear();
        string[] body =
        {
            "............",
            "............",
            "....BBBB....",
            "..BBBBBBBB..",
            ".BBBBBBBBBB.",
            ".BBBBBBBBBB.",
            "BBBBBBBBBBBB",
            "BBBBBBBBBBBB",
            "BBBBBBBBBBBB",
            ".BBBBBBBBBB.",
            "..BBBBBBBB..",
            "...F....F...",
        };
        var grid = body.Select(r => r.ToCharArray()).ToArray();
        void Put(int x, int y, char c) { if (y >= 0 && y < 12 && x >= 0 && x < 12) grid[y][x] = c; }
        // Eyes.
        if (mood == "sleepy" || blink) { Put(3, 6, 'K'); Put(4, 6, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); }
        else if (mood == "bored") { Put(3, 6, 'K'); Put(4, 6, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); Put(4, 5, 'B'); Put(8, 5, 'B'); }
        else { Put(4, 5, 'W'); Put(4, 6, 'K'); Put(7, 5, 'W'); Put(7, 6, 'K'); }
        // Mouth and cheeks.
        if (mood == "cheer") { Put(5, 8, 'K'); Put(6, 8, 'K'); Put(5, 9, 'R'); Put(6, 9, 'R'); }
        else if (mood == "bored") { Put(5, 8, 'K'); Put(6, 8, 'K'); }
        else if (mood != "sleepy") { Put(4, 8, 'K'); Put(7, 8, 'K'); Put(5, 9, 'K'); Put(6, 9, 'K'); }
        if (mood != "bored") { Put(2, 7, 'P'); Put(9, 7, 'P'); }
        // Arms up when cheering.
        if (mood == "cheer") { Put(0, 4, 'B'); Put(11, 4, 'B'); Put(0, 5, 'B'); Put(11, 5, 'B'); }
        // Outfits.
        switch (outfit)
        {
            case "cap": for (int x = 3; x <= 8; x++) Put(x, 2, 'C'); for (int x = 2; x <= 9; x++) Put(x, 3, 'C'); Put(10, 3, 'C'); Put(11, 3, 'C'); break;
            case "crown": Put(3, 0, 'G'); Put(6, 0, 'G'); Put(8, 0, 'G'); for (int x = 3; x <= 8; x++) { Put(x, 1, 'G'); Put(x, 2, 'G'); } break;
            case "scarf": for (int x = 1; x <= 10; x++) Put(x, 10, 'S'); Put(9, 11, 'S'); break;
            case "shades": for (int x = 2; x <= 9; x++) Put(x, 5, 'K'); Put(3, 6, 'K'); Put(4, 6, 'K'); Put(7, 6, 'K'); Put(8, 6, 'K'); break;
        }
        var colors = new Dictionary<char, Color>
        {
            ['B'] = Color.FromRgb(0x7F, 0xD8, 0xBE), ['F'] = Color.FromRgb(0x4F, 0xA8, 0x8E), ['K'] = Color.FromRgb(0x1E, 0x22, 0x2B), ['W'] = Colors.White,
            ['R'] = Color.FromRgb(0xE0, 0x4F, 0x5F), ['P'] = Color.FromRgb(0xF5, 0xA3, 0xB7), ['C'] = Color.FromRgb(0x3E, 0x6F, 0xD9), ['G'] = Color.FromRgb(0xF2, 0xC1, 0x4E),
            ['S'] = Color.FromRgb(0xD9, 0x48, 0x48),
        };
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                if (!colors.TryGetValue(grid[y][x], out var c)) continue;
                var r = new Rectangle { Width = 6, Height = 6, Fill = new SolidColorBrush(c) };
                Canvas.SetLeft(r, x * 6);
                Canvas.SetTop(r, y * 6);
                canvas.Children.Add(r);
            }
        if (mood == "sleepy")
        {
            var z = Ui.Text("z", 16, "SubtleBrush", bold: true);
            Canvas.SetLeft(z, 66);
            Canvas.SetTop(z, -6);
            canvas.Children.Add(z);
        }
    }
}
