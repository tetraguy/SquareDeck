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
    private readonly DispatcherTimer _discordTimer = new() { Interval = TimeSpan.FromSeconds(15) };
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
        _discordTimer.Tick += (_, _) => _ = DiscordTickAsync();
        InitEight();
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

    /// <summary>
    /// Experimental "Your handheld in the corner": the controller symbol at the bottom left of Home becomes a drawing
    /// of the handheld SquareDeck detected (the same one the controller tester shows), in the text color.
    /// </summary>
    /// <summary>The icon in the bottom-left corner changed (the ROG logo switched on or off): draw it again.</summary>
    public void RefreshFooterIcon() => UpdateFooterPad();

    private void UpdateFooterPad()
    {
        foreach (var old in FooterLeft.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "devart").ToList()) FooterLeft.Children.Remove(old);
        if (Ui.DeviceArt(32) is { } art)
        {
            FooterLeft.Children.Insert(FooterLeft.Children.IndexOf(FooterPad), art);
            FooterPad.Visibility = Visibility.Collapsed;
        }
        else FooterPad.Visibility = Visibility.Visible;
    }

    private void OnLabChanged(string id)
    {
        switch (id)
        {
            case "oemfooter": UpdateFooterPad(); break;
            case "tilt": SetTilt(Labs.On("tilt")); break;
            case "plxcontinue": SetContinueParallax(Labs.On("plxcontinue") && !_plxGamePaused); break;
            case "coverdim": case "covereffect": ApplyCoverLabs(); break;
            case "dust": RefreshDust(); break;
            case "companion": SetCompanion(Labs.On("companion")); break;
            case "discord":
                if (Labs.On("discord") && Labs.Get("discord.presence", "1") == "1") { _discordTimer.Start(); _ = DiscordTickAsync(); }
                else { _discordTimer.Stop(); _ = DiscordPresence.ClearAsync(); }
                break;
            case "genresound":
                if (!Labs.On("genresound") && _genreTrack != null) { _genreTrack = null; UpdateMenuMusic(); }
                break;
            case "overnight":
                if (!Labs.On("overnight")) { Labs.KeepAwake(false); Labs.Put("overnight.active", "0", save: false); }
                break;
        }
    }

    /// <summary>Every 15 seconds while Discord status is on: the game that's running (or browsing) as your Discord status.</summary>
    public async Task DiscordTickAsync()
    {
        string id = Labs.Get("discord.app", "");
        if (id.Length == 0) return;
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        if (running != null && Labs.Get("discord.hide:" + running.Key) == "1") running = null;
        DateTime? started = running != null && _sessionStart.TryGetValue(running.Key, out var s) ? s : null;
        string? image = running is { IsSteam: true } ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{running.AppId}/header.jpg" : null;
        await DiscordPresence.UpdateAsync(id, running?.Name, started, image, Labs.Get("discord.browsing", "1") == "1");
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
            // Unlockable styles: one whose goal has just been reached is unlocked and celebrated.
            if (running == null && IsActive && _screens.Count == 0 &&
                StyleUnlocks.Update(AllGames, Playtime, Achievements, Library) is { Count: > 0 } unlocked)
            {
                Celebrate("New style unlocked: " + unlocked[0].Name, "Find it in Settings > Themes > Unlockable." + (unlocked.Count > 1 ? $"  (+{unlocked.Count - 1} more)" : ""));
                CompanionReact("cheer");
            }
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
        if (!first) Notifications.Add("info", "SquareDeck ran into a problem", "Report it in one tap from Settings > Updates and data > One-tap bug report.");
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
        if (index != _labsLastIndex) CompanionNoticeSelection();
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
        double target = MusicLevel;
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
        if (ParallaxOwns(GameList.SelectedItem as Game)) { ResetTilt(); return; } // the parallax moves the "Continue" tile
        if (!ReferenceEquals(cover, _tiltedCover)) { ResetTilt(); _tiltedCover = cover; _tiltedFace = face; }
        if (cover == null) return;
        // How far, how, zoomed or not, and which way (Settings > Home screen > Covers that tilt).
        double k = double.TryParse(Labs.Get("tilt.strength", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var st) ? st : 1;
        if (Labs.Get("tilt.invert") == "1") k = -k;
        double x = _tiltX * k, y = _tiltY * k;
        double zoom = Labs.Get("tilt.zoom", "1") == "1" ? 1.08 + 0.02 * Math.Min(Math.Abs(k), 2) : 1;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(zoom, zoom));
        switch (Labs.Get("tilt.style", "shift"))
        {
            case "swing": // turns around its middle, as if hung from a pin
                group.Children.Add(new RotateTransform(x * 5));
                group.Children.Add(new TranslateTransform(-x * 4, y * 4));
                break;
            case "skew": // leans like a card held in the hand
                group.Children.Add(new SkewTransform(-x * 6, y * 4));
                group.Children.Add(new TranslateTransform(-x * 5, y * 5));
                break;
            case "float": // rises toward the tilt, with the shadow moving the other way
                group.Children.Add(new TranslateTransform(-x * 7, y * 7 - 4 * Math.Min(1, Math.Abs(x) + Math.Abs(y))));
                break;
            default: // parallax: the art slides inside its frame
                group.Children.Add(new TranslateTransform(-x * 9, y * 9));
                break;
        }
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        cover.RenderTransform = group;
        if (Labs.Get("tilt.style", "shift") == "float" && !Perf.Active)
        {
            if (cover.Effect is not System.Windows.Media.Effects.DropShadowEffect shadow)
                cover.Effect = shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, Opacity = 0.55, ShadowDepth = 10 };
            shadow.Direction = 270 + Math.Atan2(y, x) * 180 / Math.PI;
        }
        else if (cover.Effect is System.Windows.Media.Effects.DropShadowEffect) cover.Effect = null;
        // The cover moves one way, the "Continue" logo and name the other, at a smaller depth.
        if (face != null) face.RenderTransform = new TranslateTransform(x * 5, -y * 5);
    }

    private void ResetTilt()
    {
        if (_tiltedCover != null)
        {
            _tiltedCover.RenderTransform = Transform.Identity;
            if (_tiltedCover.Effect is System.Windows.Media.Effects.DropShadowEffect) _tiltedCover.Effect = null;
        }
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
    private Border? _companionBubbleText;
    private readonly ScaleTransform _companionScale = new(1, 1);
    private readonly RotateTransform _companionTurn = new(0);
    private readonly TranslateTransform _companionMove = new();
    // Where it stands: shared by the companion and its speech bubble, so the bubble always goes with it. It glides
    // from place to place (between pages, and when it roams), never jumps.
    private readonly TranslateTransform _companionPos = new();
    // What it does next: an animation that comes up while another is playing waits its turn instead of cutting it off.
    private readonly Queue<string> _companionQueue = new();
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
        // Turned back on while it was saying goodbye: it stays, and says hello again instead.
        if (on && _companion != null && _companionLeaving)
        {
            _companionLeaving = false;
            _companionByeId++;
            CompanionResetPose();
            _companionTimer.Start();
            CompanionHello();
            return;
        }
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
            // The speech bubble sits just above its head (the sprite is 72 tall at the bottom of its 74-up box), with a
            // little tail pointing down at it, so the words clearly come from the companion.
            _companionBubbleText = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5) };
            _companionBubbleText.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            var tail = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0,0 L 14,0 L 4,9 Z"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, -1, 22, 0)
            };
            tail.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "DialogBrush");
            var bubble = new StackPanel();
            bubble.Children.Add(_companionBubbleText);
            bubble.Children.Add(tail);
            _companionBubble = new Border { Child = bubble, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 150), Opacity = 0, IsHitTestVisible = false };
            Panel.SetZIndex(_companionBubble, 40);
            _companion.RenderTransform = _companionPos;
            _companionBubble.RenderTransform = _companionPos;
            AddOverlay(_companion);
            AddOverlay(_companionBubble);
            _companionDrawn = "";
            _companionFrames.Tick -= CompanionFrame;
            _companionFrames.Tick += CompanionFrame;
            _companionTimer.Start();
            CompanionTick();
            CompanionHello(); // a different arrival each time
            HookCompanionPet();
        }
        else if (!on && _companion != null && !_companionLeaving)
            CompanionGoodbye(RemoveCompanion); // a different goodbye each time, then off the screen
    }

    private void RemoveCompanion()
    {
        if (_companion != null)
        {
            _companionTimer.Stop();
            _companionFrames.Stop();
            RootGrid.Children.Remove(_companion);
            if (_companionBubble != null) RootGrid.Children.Remove(_companionBubble);
            _companion = null;
            _companionBubble = null;
            _companionBubbleText = null;
            _companionPetHooked = false;
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
        double seconds = CompanionExtraSeconds(action) ?? action switch
        {
            "spin" => 1.6, "dance" => 3.2, "sleep" => 4, "wave" or "bye" => 2.2, "walk" => 4.2, "juggle" => 3, "lookaround" => 2.6, "flip" => 1.4,
            "trip" => 2.4, "peekaboo" => 2.6, "balloon" => 3.2, "read" or "tinker" or "search" or "think" or "game" => 3, "zoom" => 1.6,
            "hop" => 1.5, "cartwheel" => 1.4, "slide" => 1.2, "sneeze" => 1.8, "ball" => 2.4, "bubbles" => 2.6, "magic" => 2.4, "meditate" => 3.6,
            "moonwalk" => 2.6, "wobble" => 1.6, "cheer" => 2, "nod" => 1.4, _ => 1.8
        };
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
                case "walk":
                {
                    // A little stroll to the left and back, hopping with each step, facing the way it goes.
                    var stroll = new DoubleAnimationUsingKeyFrames();
                    stroll.KeyFrames.Add(new EasingDoubleKeyFrame(-150, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                    stroll.KeyFrames.Add(new LinearDoubleKeyFrame(-150, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
                    stroll.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                    _companionMove.BeginAnimation(TranslateTransform.XProperty, stroll);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -5, 0.16, true, 12));
                    _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2);
                    Dispatcher.BeginInvoke(async () => { await Task.Delay(2200); _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(1.8); _companionDrawn = ""; CompanionTick(); });
                    break;
                }
                case "juggle":
                    for (int i = 0; i < 3; i++) Juggle(i);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -3, 0.25, true, 6));
                    break;
                case "lookaround":
                    _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(0.9);
                    Dispatcher.BeginInvoke(async () => { await Task.Delay(900); _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(0.9); _companionDrawn = ""; CompanionTick(); });
                    Sparkles("?", 1, "#AAB4C8", rise: true);
                    break;
                case "stretch":
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 1.22, 0.5, true));
                    _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 0.88, 0.5, true));
                    break;
                case "flip":
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -46, 0.35, true));
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -360, TimeSpan.FromSeconds(0.7)) { EasingFunction = ease });
                    Sparkles("✦", 5, "#FFFFFF");
                    break;
                case "trip":
                {
                    // Topples over, lies there a moment, and pops back up.
                    var fall = new DoubleAnimationUsingKeyFrames();
                    fall.KeyFrames.Add(new EasingDoubleKeyFrame(85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.35)), new BounceEase { Bounces = 2, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
                    fall.KeyFrames.Add(new LinearDoubleKeyFrame(85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
                    fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8)), new BackEase { EasingMode = EasingMode.EaseOut }));
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, fall);
                    Sparkles("✶", 3, "#FFD84D");
                    break;
                }
                case "peekaboo":
                {
                    var hide = new DoubleAnimationUsingKeyFrames();
                    hide.KeyFrames.Add(new EasingDoubleKeyFrame(90, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4)), ease));
                    hide.KeyFrames.Add(new LinearDoubleKeyFrame(90, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
                    hide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.9)), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, hide);
                    break;
                }
                case "balloon":
                    Sparkles("◯", 1, "#FF6B8A", rise: true);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -18, 1.2, true));
                    break;
                case "confetti":
                    foreach (var hex in new[] { "#FF6B8A", "#FFD84D", "#7FD8BE", "#7FB8FF", "#C3A6FF" }) Sparkles("■", 3, hex);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -20, 0.2, true, 2));
                    break;
                case "read":
                    Sparkles("❐", 1, "#E8D8B0", rise: false);
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -5, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
                    break;
                case "tinker":
                    Sparkles("⚙", 3, "#AAB4C8", rise: true);
                    _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-2, 2, TimeSpan.FromMilliseconds(90)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(12) });
                    break;
                case "search":
                    _companionLook = 1; _companionLookUntil = DateTime.Now.AddSeconds(1.2);
                    Dispatcher.BeginInvoke(async () => { await Task.Delay(1200); _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(1.2); _companionDrawn = ""; CompanionTick(); });
                    Sparkles("○", 2, "#7FB8FF", rise: true);
                    break;
                case "think":
                    Sparkles("…", 1, "#AAB4C8", rise: true);
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.6)) { AutoReverse = true });
                    break;
                case "game":
                    Sparkles("▶", 2, "#7FD8BE", rise: true);
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
                    break;
                case "shop":
                    Sparkles("$", 4, "#FFD84D", rise: true);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -10, 0.2, true, 3));
                    break;
                case "camera":
                    Sparkles("✦", 8, "#FFFFFF");
                    _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.1, 1, 0.3));
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.1, 1, 0.3));
                    break;
                case "trophy":
                    Sparkles("★", 6, "#FFC93C", rise: true);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -24, 0.25, true, 2));
                    break;
                case "roam":
                {
                    // Walks to another spot on the page, facing the way it goes, bobbing with each step.
                    var (from, to) = CompanionRoamTarget(160);
                    double secs = Math.Clamp(Math.Abs(to - from) / 140, 1.2, 5);
                    _companionActionUntil = DateTime.Now.AddSeconds(secs + 0.2);
                    CompanionGlideX(to, secs, new SineEase { EasingMode = EasingMode.EaseInOut });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -5, 0.16, true, Math.Max(2, (int)(secs / 0.32))));
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(0.16)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(Math.Max(2, (int)(secs / 0.32))) });
                    _companionLook = to < from ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(secs);
                    break;
                }
                case "hop":
                {
                    // Three bunny hops to a new spot.
                    var (from, to) = CompanionRoamTarget(120);
                    CompanionGlideX(to, 1.3, new QuadraticEase { EasingMode = EasingMode.EaseInOut });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -28, TimeSpan.FromSeconds(0.21)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1.05, TimeSpan.FromSeconds(0.21)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                    _companionLook = to < from ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(1.3);
                    break;
                }
                case "cartwheel":
                {
                    var (from, to) = CompanionRoamTarget(140);
                    CompanionGlideX(to, 1.2, new SineEase { EasingMode = EasingMode.EaseInOut });
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, to < from ? -360 : 360, TimeSpan.FromSeconds(1.2)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -18, 0.6, true));
                    Sparkles("✦", 4, "#FFFFFF");
                    break;
                }
                case "slide":
                {
                    // A run-up and a slide, as if on ice, leaning back.
                    var (from, to) = CompanionRoamTarget(180);
                    CompanionGlideX(to, 1.0, new CubicEase { EasingMode = EasingMode.EaseOut });
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, to < from ? 12 : -12, TimeSpan.FromSeconds(0.5)) { AutoReverse = true });
                    _companionLook = to < from ? -1 : 1; _companionLookUntil = DateTime.Now.AddSeconds(1);
                    Sparkles("≈", 3, "#BFE6FF");
                    break;
                }
                case "moonwalk":
                {
                    // Glides backwards, looking the other way.
                    var (from, to) = CompanionRoamTarget(120);
                    CompanionGlideX(to, 2.4, null);
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -2, 0.2, true, 6));
                    _companionLook = to < from ? 1 : -1; _companionLookUntil = DateTime.Now.AddSeconds(2.4);
                    Sparkles("♪", 3, "#C3A6FF", rise: true);
                    break;
                }
                case "sneeze":
                {
                    var build = new DoubleAnimationUsingKeyFrames();
                    build.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9)), new SineEase()));
                    build.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.05)), new BackEase()));
                    build.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)), new ElasticEase { Oscillations = 2 }));
                    _companionScale.BeginAnimation(ScaleTransform.ScaleYProperty, build);
                    Dispatcher.BeginInvoke(async () => { await Task.Delay(1000); Sparkles("*", 6, "#DDE6F0"); });
                    break;
                }
                case "ball":
                    KickBall();
                    _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -8, TimeSpan.FromSeconds(0.12)) { AutoReverse = true });
                    _companionLook = -1; _companionLookUntil = DateTime.Now.AddSeconds(2.2);
                    break;
                case "bubbles":
                    Sparkles("○", 8, "#9FD8FF", rise: true);
                    Sparkles("◦", 6, "#CDEBFF", rise: true);
                    _companionScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 1.06, 0.3, true, 3));
                    break;
                case "magic":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(0.3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                    Sparkles("✦", 10, "#C7A4FF");
                    Dispatcher.BeginInvoke(async () => { await Task.Delay(700); Sparkles("★", 6, "#FFD84D", rise: true); });
                    break;
                case "meditate":
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -24, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    Sparkles("◌", 3, "#BFA8FF", rise: true);
                    break;
                case "wobble":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, 14, TimeSpan.FromSeconds(0.2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = new SineEase() });
                    break;
                case "cheer":
                    _companionMove.BeginAnimation(TranslateTransform.YProperty, Anim(0, -16, 0.18, true, 4));
                    foreach (var hex in new[] { "#FFD84D", "#FF6B8A", "#7FD8BE" }) Sparkles("✦", 3, hex);
                    break;
                case "nod":
                    _companionTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 6, TimeSpan.FromSeconds(0.18)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                    break;
                case "zoom":
                    _companionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -60, TimeSpan.FromSeconds(0.25)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = ease });
                    Sparkles("≡", 4, "#AAB4C8");
                    break;
            }
        if (!reduce) CompanionExtraAnimate(action);
        string? say = CompanionTakeLine(action) ?? CompanionExtraSay(action) ?? action switch
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
            "trip" => "Oops!",
            "flip" => "Ta-da!",
            "juggle" => "Look, juggling!",
            "balloon" => "Wheee!",
            "sneeze" => "Achoo!",
            "ball" => "Goal!",
            "magic" => "Abracadabra!",
            "meditate" => "Ommm…",
            "cartwheel" => "Wheee!",
            "moonwalk" => "Hee-hee!",
            "cheer" => "Yay!",
            _ => null
        };
        if (say != null) CompanionSay(say);
        _companionDrawn = "";
        CompanionTick();
    }

    /// <summary>
    /// Plays an animation when the current one has finished (straight away if nothing is playing). Page arrivals and
    /// the things it does on its own come through here, so they never cut an animation off halfway.
    /// </summary>
    private void CompanionQueue(string action)
    {
        if (_companion == null) return;
        if (_companionAction == null || DateTime.Now > _companionActionUntil) { CompanionReact(action); return; }
        // Only the newest few, and not the same thing twice in a row.
        if (_companionQueue.Count > 0 && _companionQueue.Last() == action) return;
        while (_companionQueue.Count >= 2) _companionQueue.Dequeue();
        _companionQueue.Enqueue(action);
    }

    /// <summary>The spot it stands on by default: Home's corner, or a little further in on pages (footer and scroll bar).</summary>
    private (double X, double Y) CompanionBase() => _screens.Count == 0 ? (0, 0) : (-20, 10);

    /// <summary>How far left of its spot it may wander: most of the width on Home, the right side of other pages (clear of what's on them).</summary>
    private double CompanionRange()
    {
        double width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : 1280;
        return Math.Max(0, (_screens.Count == 0 ? width * 0.72 : width * 0.32) - 140);
    }

    /// <summary>Somewhere else along the bottom to go to, at least <paramref name="minStep"/> away.</summary>
    private (double From, double To) CompanionRoamTarget(double minStep)
    {
        double from = _companionPos.X, bx = CompanionBase().X, range = CompanionRange();
        double low = bx - range, high = bx;
        double to = low + Random.Shared.NextDouble() * (high - low);
        if (Math.Abs(to - from) < minStep) to = from - minStep >= low ? from - minStep : Math.Min(high, from + minStep);
        // Now and then it heads back home to its corner.
        if (Random.Shared.Next(4) == 0) to = bx;
        return (from, Math.Clamp(to, low, high));
    }

    private void CompanionGlideX(double x, double seconds, IEasingFunction? ease) =>
        _companionPos.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease });

    /// <summary>Glides to a spot (and its bubble with it); straight there with reduced motion.</summary>
    private void CompanionGlide(double x, double y, double seconds)
    {
        if (Theme.ReduceMotion || seconds <= 0)
        {
            _companionPos.BeginAnimation(TranslateTransform.XProperty, null);
            _companionPos.BeginAnimation(TranslateTransform.YProperty, null);
            _companionPos.X = x; _companionPos.Y = y;
            return;
        }
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _companionPos.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease });
        _companionPos.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease });
    }

    /// <summary>Kicks a ball that rolls off and bounces, then fades.</summary>
    private void KickBall()
    {
        if (_companionFx == null) return;
        var ball = new System.Windows.Shapes.Ellipse
        {
            Width = 16, Height = 16, Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), StrokeThickness = 1.5,
            Fill = new RadialGradientBrush(Colors.White, Color.FromRgb(0xC8, 0xCE, 0xD8)) { GradientOrigin = new Point(0.35, 0.3) },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform()
        };
        Canvas.SetLeft(ball, 38);
        Canvas.SetTop(ball, 100);
        _companionFx.Children.Add(ball);
        var roll = TimeSpan.FromSeconds(2);
        ball.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(38, -240, roll) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var bounce = new DoubleAnimationUsingKeyFrames();
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(70, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.55)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(88, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.75)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.95)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        ball.BeginAnimation(Canvas.TopProperty, bounce);
        ((RotateTransform)ball.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -720, roll) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(1.8) };
        fade.Completed += (_, _) => _companionFx?.Children.Remove(ball);
        ball.BeginAnimation(OpacityProperty, fade);
    }

    private void CompanionSay(string text)
    {
        if (_companionBubble == null || _companionBubbleText == null) return;
        var line = new TextBlock();
        line.Inlines.Add(new System.Windows.Documents.Run(CompanionArt.Name + ": ") { FontWeight = FontWeights.Normal });
        line.Inlines.Add(new System.Windows.Documents.Run(text) { FontWeight = FontWeights.Bold });
        line.FontSize = 14;
        line.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        _companionBubbleText.Child = line;
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

    /// <summary>One star thrown up and caught, round and round (the juggling act).</summary>
    private void Juggle(int index)
    {
        if (_companionFx == null) return;
        var star = new TextBlock { Text = "★", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(index switch { 0 => Color.FromRgb(0xFF, 0xD8, 0x4D), 1 => Color.FromRgb(0xFF, 0x6B, 0x8A), _ => Color.FromRgb(0x7F, 0xB8, 0xFF) }) };
        _companionFx.Children.Add(star);
        var delay = TimeSpan.FromSeconds(index * 0.33);
        var x = new DoubleAnimationUsingKeyFrames { BeginTime = delay, RepeatBehavior = new RepeatBehavior(3) };
        x.KeyFrames.Add(new LinearDoubleKeyFrame(36, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        x.KeyFrames.Add(new LinearDoubleKeyFrame(70, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        var y = new DoubleAnimationUsingKeyFrames { BeginTime = delay, RepeatBehavior = new RepeatBehavior(3) };
        y.KeyFrames.Add(new LinearDoubleKeyFrame(64, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        y.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        y.KeyFrames.Add(new EasingDoubleKeyFrame(64, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        y.Completed += (_, _) => _companionFx?.Children.Remove(star);
        star.BeginAnimation(Canvas.LeftProperty, x);
        star.BeginAnimation(Canvas.TopProperty, y);
    }

    // ---------------------------------------------------------------- the companion around the launcher

    /// <summary>
    /// Where the companion is, by page, and what it does there: its first move when it arrives, the things it does
    /// now and then, and what it says. Null: pages it stays off (full-screen video, web pages, the game's start, pop-ups).
    /// </summary>
    private static (string Arrive, string[] Idle, string[] Lines)? CompanionPlace(Screen? screen)
    {
        if (screen is { IsDialog: true }) return null;
        if (CompanionPlaceExtra(screen) is { } extra) return extra;
        var core = CompanionPlaceCore(screen);
        if (core is { } c && screen == null) return (c.Arrive, c.Idle.Concat(HomeMoreActs).ToArray(), c.Lines.Concat(HomeMore).ToArray());
        return core;
    }

    private static (string Arrive, string[] Idle, string[] Lines)? CompanionPlaceCore(Screen? screen) => screen switch
    {
        null => ("wave", new[] { "walk", "juggle", "lookaround", "stretch", "flip", "trip", "peekaboo", "balloon", "confetti", "dance", "game", "think",
                                 "ball", "bubbles", "magic", "meditate", "wobble", "sneeze", "roam", "hop" },
            new[] { "Ready when you are.", "What are we playing?", "♪", "Pick something fun!", "I could play all day.", "Psst… that one looks good." }),
        { IsDialog: true } => null,
        StreamingScreen or CloudGameScreen or BrowserScreen or MediaViewerScreen or ViewerScreen or TrailerScreen or LaunchSplashScreen
            or CaptureViewerScreen or TextEditorScreen or CountdownScreen or LockScreenScreen or OnboardingScreen or BusyScreen or GameBusyScreen => null,
        CompanionScreen => ("wave", new[] { "flip", "juggle", "dance", "stretch", "magic", "cartwheel", "cheer", "bubbles" }, new[] { "That's me!", "Do I look good?" }),
        SettingsScreen or AccessibilityScreen or PcSettingsScreen or ThemeScreen or UiStyleScreen or AccentScreen or HighlightEffectsScreen or SoundsScreen
            or WallpaperScreen or ColorThemeScreen or StyleBuilderScreen or CoverLabsScreen
            => ("tinker", new[] { "tinker", "think", "lookaround", "stretch", "nod", "magic" }, new[] { "Fiddling with settings?", "Ooh, new look?", "Careful with that one.", "I like this color." }),
        LibraryScreen or SearchScreen or StoreSearchScreen or AllAppsScreen => ("search", new[] { "search", "read", "think", "lookaround", "roam", "nod" },
            new[] { "So many games…", "Looking for something?", "This one!", "Have you finished that one?" }),
        DetailsScreen or GameProfileScreen or GameNewsScreen or GameNotesScreen or GameDiaryScreen => ("read", new[] { "read", "think", "game" },
            new[] { "Looks fun!", "Let's play it!", "Ooh, tell me more.", "I've heard good things." }),
        GameAchievementsScreen or PlaytimeScreen or UnlocksScreen or UnlockablesScreen or StickerBookScreen or PassportScreen or LeaderboardScreen or BossScreen
            => ("trophy", new[] { "trophy", "confetti", "flip", "cheer", "cartwheel" }, new[] { "Look at all that!", "You're on a roll.", "So proud of you!" }),
        MediaScreen or MusicSearchScreen or MusicPlayersScreen => ("dance", new[] { "dance", "dance", "juggle", "moonwalk", "wobble" }, new[] { "♪ ♫", "Turn it up!", "This one's a bop." }),
        StoresScreen or WishlistScreen or FreeGamesScreen or ReleaseCalendarScreen or AddStoresScreen or RecompZoneScreen or RecompGameScreen
            => ("shop", new[] { "shop", "lookaround", "think" }, new[] { "Ooh, sales!", "Free games? Yes!", "Just one more…", "Add it to the wishlist!" }),
        FriendsScreen or DiscordScreen or ShareScreen => ("wave", new[] { "wave", "hearts", "dance" }, new[] { "Say hi for me!", "Who's online?", "Play together?" }),
        CapturesScreen => ("camera", new[] { "camera", "flip" }, new[] { "Say cheese!", "Great shot!", "Frame it!" }),
        ExplorerScreen or StorageScreen or StorageAdvisorScreen or CleanupScreen or DiskCleanupScreen or MoveGameScreen or BackupScreen
            => ("search", new[] { "search", "tinker", "read" }, new[] { "Digging through files…", "Tidy up time!", "Where did that go?" }),
        BoostScreen or TaskManagerScreen or SpeedTestScreen or DriveSpeedScreen or StorageSpeedScreen or NetworkToolsScreen
            => ("zoom", new[] { "zoom", "tinker" }, new[] { "Zoom zoom!", "Faster!", "Let's go fast." }),
        BatteryScreen or BatteryHealthScreen or PowerMenuScreen or QuickSettingsScreen => ("stretch", new[] { "stretch", "yawn", "lookaround" },
            new[] { "Need a charge?", "Keep me powered!", "Time for a nap?" }),
        _ => ("lookaround", new[] { "lookaround", "think", "stretch", "read" }, new[] { "What's this page?", "Hmm…", "I'm right here." }),
    };

    private Screen? _companionPage;
    private bool _companionPageSeen;

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
        if (!playing)
        {
            if (CompanionWelcomeBack() is { } line) _companionLines["wave"] = line;
            CompanionReact("wave");
        }
    }

    private void CompanionFrame(object? sender, EventArgs e)
    {
        if (_companionAction == null || DateTime.Now > _companionActionUntil)
        {
            // Finished: whatever was waiting plays now, flowing straight on from this one.
            if (_companionQueue.Count > 0 && _companion?.Visibility == Visibility.Visible)
            {
                _companionAction = null;
                CompanionReact(_companionQueue.Dequeue());
                return;
            }
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
        // Everywhere in the launcher (doing what suits each page), except full-screen pages, pop-ups and the intro.
        var top = _screens.Count == 0 ? null : _screens[^1];
        var place = IntroLayer.Children.Count == 0 ? CompanionPlace(top) : null;
        var shown = place != null ? Visibility.Visible : Visibility.Collapsed;
        if (_companion.Visibility != shown) _companion.Visibility = _companionBubble!.Visibility = shown;
        if (place == null) { _companionQueue.Clear(); return; }
        bool home = top == null;
        _companionSprite.Width = _companionSprite.Height = 72;
        if (!ReferenceEquals(top, _companionPage) || !_companionPageSeen)
        {
            bool first = !_companionPageSeen;
            _companionPage = top;
            _companionPageSeen = true;
            // Arriving on a page: it glides to its spot there (pages have a footer and a scroll bar on the right, so it
            // stands a little further in), whatever it's doing carries on, and the page's own move comes after it.
            var (bx, by) = CompanionBase();
            CompanionGlide(bx, by, first ? 0 : 0.7);
            if (!Theme.ReduceMotion && (top != null || _companionTicks > 4))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (CompanionArriveFor(top) is { } special)
                    {
                        if (special.Line != null) CompanionQueue(special.Act, special.Line); else CompanionQueue(special.Act);
                        return;
                    }
                    CompanionQueue(place.Value.Arrive);
                    if (Random.Shared.Next(3) == 0) CompanionSay(place.Value.Lines[Random.Shared.Next(place.Value.Lines.Length)]);
                });
            }
        }
        _companionTicks++;
        string mood = Labs.CompanionMood(Playtime, Achievements, _celebratedAt);
        mood = CompanionExtraMood(_companionAction) ?? _companionAction switch
        {
            "jump" or "spin" => "cheer",
            "hearts" => "love",
            "worried" => "worried",
            "sleep" => "sleepy",
            "yawn" => "bored",
            "dance" or "game" or "juggle" => "dance",
            "flip" or "confetti" or "trophy" or "shop" or "camera" or "cartwheel" or "cheer" or "magic" => "cheer",
            "roam" or "hop" or "slide" or "moonwalk" or "ball" or "wobble" => "dance",
            "bubbles" or "meditate" or "nod" => "happy",
            "sneeze" => "worried",
            "trip" or "zoom" => "worried",
            "think" or "read" or "tinker" or "search" => "happy",
            "balloon" => "love",
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
        // Now and then (a little more often on Home), something on its own: what suits the page, its mood, or the music.
        int every = home ? 24 : 36;
        if (_companionTicks % every == 0)
        {
            if (mood == "sleepy") CompanionQueue("sleep");
            else if (mood == "bored" && Random.Shared.Next(2) == 0) CompanionQueue(Random.Shared.Next(2) == 0 ? "yawn" : "sneeze");
            else if ((_menuMusicPlaying || LocalMusic.IsPlaying) && Random.Shared.Next(2) == 0) CompanionQueue(Random.Shared.Next(3) == 0 ? "moonwalk" : "dance");
            else if (Random.Shared.Next(3) == 0) CompanionQueue(new[] { "roam", "roam", "hop", "slide", "cartwheel" }[Random.Shared.Next(5)]);
            else
            {
                var idle = place.Value.Idle;
                CompanionQueue(idle[Random.Shared.Next(idle.Length)]);
                if (Random.Shared.Next(3) == 0) CompanionSay(place.Value.Lines[Random.Shared.Next(place.Value.Lines.Length)]);
            }
        }
    }
}
