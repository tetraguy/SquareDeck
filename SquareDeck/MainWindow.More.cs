using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

// The third part of the main window: home buttons and hints, the screen saver, closing animations,
// screen shape, updates, reset, collections and menu music.
public partial class MainWindow
{
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _statusClear = new() { Interval = TimeSpan.FromSeconds(8) };
    private DateTime _lastInput = DateTime.UtcNow;
    private Point _lastMouse;

    // ================================================================ main display

    /// <summary>Moves the launcher to the display chosen in Settings, if it is connected and we aren't there already.</summary>
    public void ApplyMainDisplay()
    {
        var target = Monitors.Find(Settings.MainDisplay);
        if (target == null) return;
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            // Where the window's middle is now, in screen pixels.
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var middle = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
                if (middle.X >= target.Left && middle.X < target.Left + target.Width &&
                    middle.Y >= target.Top && middle.Y < target.Top + target.Height) return; // already on it
            }
            Monitors.MoveTo(this, target);
        }
        catch (Exception) { }
    }

    // ================================================================ Windows notifications

    private readonly DispatcherTimer _windowsToastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _checkingWindowsToasts;

    /// <summary>Copies new notifications from other apps into the launcher's pop-ups and notification panel.</summary>
    private async Task CheckWindowsToastsAsync()
    {
        if (!Settings.WindowsNotifications || _checkingWindowsToasts) return;
        _checkingWindowsToasts = true;
        try
        {
            var fresh = await WindowsNotifications.TakeNewAsync();
            // Not supported or not allowed: stop asking every few seconds (Settings can switch it back on).
            if (WindowsNotifications.Status is "unsupported" or "denied") _windowsToastTimer.Interval = TimeSpan.FromMinutes(2);
            else _windowsToastTimer.Interval = TimeSpan.FromSeconds(6);
            foreach (var toast in fresh.Take(4)) // a burst of them shouldn't queue pop-ups for a minute
                ShowToast(toast.App.Length > 0 ? toast.App.ToUpperInvariant() : "WINDOWS", toast.Title, toast.Text, toast.IconPath, "windows",
                    toast.IconPath.Length > 0 ? ImageLoader.FromFile(toast.IconPath, 112) : null);
        }
        catch (Exception) { }
        finally { _checkingWindowsToasts = false; }
    }

    // ================================================================ one highlight at a time

    private DateTime _mouseMovedAt;

    /// <summary>
    /// The selected game's ring shows only while the games row itself has the focus. When a round button,
    /// a top-bar icon or the footer has it, that is the only thing highlighted.
    /// </summary>
    /// <summary>The selected game's ring picks up a new ring brush (the highlight color or gradient changed).</summary>
    public void RefreshHomeRing() => UpdateHomeRing();

    private void UpdateHomeRing()
    {
        bool onGames = _buttonIndex < 0 && _topIndex < 0 && _footerIndex < 0 && _actionIndex < 0;
        Resources["RingActiveBrush"] = onGames ? (TryFindResource("RingBrush") as Brush ?? TryFindResource("AccentBrush") as Brush ?? Brushes.White) : Brushes.Transparent;
    }

    /// <summary>
    /// A real mouse moving onto a button moves the focus there, so the pointer and the controller never
    /// highlight two different things. Touch and buttons sliding under a still pointer don't count.
    /// </summary>
    private void OnButtonHover(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null || sender is not Button button || !button.IsEnabled) return;
        if (DateTime.UtcNow - _mouseMovedAt > TimeSpan.FromMilliseconds(200)) return;
        if (!ReferenceEquals(Window.GetWindow(button), this)) return;

        if (_screens.Count > 0)
        {
            if (_screens[^1].IsAncestorOf(button) && button.Focusable && !button.IsKeyboardFocused) button.Focus();
            return;
        }
        int round = Array.IndexOf(_buttons, button);
        if (round >= 0)
        {
            SetTopIndex(-1);
            SetFooterIndex(-1);
            if (_actionIndex >= 0) SetActionIndex(-1);
            SetButtonIndex(round);
            return;
        }
        int top = TopButtons().IndexOf(button);
        if (top >= 0)
        {
            SetButtonIndex(-1);
            SetFooterIndex(-1);
            SetTopIndex(top);
        }
    }

    /// <summary>Tapping or clicking a game hands the highlight back to the games row.</summary>
    private void OnGameListPointer(object sender, MouseButtonEventArgs e)
    {
        if (_buttonIndex >= 0) SetButtonIndex(-1);
        if (_topIndex >= 0) SetTopIndex(-1);
        if (_footerIndex >= 0) SetFooterIndex(-1);
    }

    // ================================================================ favorite heart

    /// <summary>A heart that pops up over the game for a moment when it becomes a favorite.</summary>
    private void ShowHeartBurst(Game item, FrameworkElement? from)
    {
        try
        {
            // Over the game's tile when it is on the home row, otherwise over whatever was used to add it.
            FrameworkElement? anchor = _screens.Count == 0 && GameList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem tile
                ? tile : from ?? Keyboard.FocusedElement as FrameworkElement;
            var center = anchor != null && anchor.IsVisible
                ? anchor.TranslatePoint(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2), IntroLayer)
                : new Point(IntroLayer.ActualWidth / 2, IntroLayer.ActualHeight / 2);

            var scale = new ScaleTransform(0.2, 0.2);
            var rise = new TranslateTransform();
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(rise);
            var heart = new TextBlock
            {
                Text = "\uEB52", FontFamily = Ui.IconFont, FontSize = 64, IsHitTestVisible = false,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D)),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = transforms,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
            };
            heart.Margin = new Thickness(center.X - 32, center.Y - 40, 0, 0);
            IntroLayer.Children.Add(heart);

            if (Theme.ReduceMotion)
            {
                scale.ScaleX = scale.ScaleY = 1;
                var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                hold.Tick += (_, _) => { hold.Stop(); IntroLayer.Children.Remove(heart); };
                hold.Start();
                return;
            }

            var pop = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0.2, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.3, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            rise.BeginAnimation(TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, -36, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(550) });
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(600) };
            fade.Completed += (_, _) => IntroLayer.Children.Remove(heart);
            heart.BeginAnimation(OpacityProperty, fade);
            Rumble();
        }
        catch (Exception) { }
    }

    /// <summary>Where the heart shows: over the game's tile on the home row, otherwise over what was used to change it.</summary>
    private Point HeartCenter(Game item, FrameworkElement? from)
    {
        FrameworkElement? anchor = _screens.Count == 0 && GameList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem tile
            ? tile : from ?? Keyboard.FocusedElement as FrameworkElement;
        return anchor != null && anchor.IsVisible
            ? anchor.TranslatePoint(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2), IntroLayer)
            : new Point(IntroLayer.ActualWidth / 2, IntroLayer.ActualHeight / 2);
    }

    /// <summary>
    /// The favorite heart in reverse, when a game leaves favorites: the heart pops up the same way, a jagged crack runs
    /// down it, and its two halves fall apart, tipping away from each other as they fade.
    /// </summary>
    private void ShowHeartBreak(Game item, FrameworkElement? from)
    {
        try
        {
            var center = HeartCenter(item, from);
            // The heart drawn as a shape (64 x 60), so it can be cut along the crack.
            var heart = Geometry.Parse("M32,58 C10,40 2,28 2,18 C2,8 10,2 18,2 C25,2 30,7 32,11 C34,7 39,2 46,2 C54,2 62,8 62,18 C62,28 54,40 32,58 Z");
            var crack = new[] { new Point(32, 6), new Point(27, 17), new Point(36, 27), new Point(28, 38), new Point(35, 48), new Point(32, 64) };
            Geometry Side(bool left)
            {
                var figure = new PathFigure { StartPoint = new Point(left ? 0 : 64, 0), IsClosed = true };
                figure.Segments.Add(new LineSegment(new Point(32, 0), true));
                foreach (var p in crack) figure.Segments.Add(new LineSegment(p, true));
                figure.Segments.Add(new LineSegment(new Point(left ? 0 : 64, 64), true));
                return new PathGeometry(new[] { figure });
            }
            var color = new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x68));
            var halves = new List<(System.Windows.Shapes.Path Path, TranslateTransform Move, RotateTransform Tip)>();
            var holder = new Grid
            {
                Width = 64, Height = 64, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(center.X - 32, center.Y - 38, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 }
            };
            foreach (bool left in new[] { true, false })
            {
                var move = new TranslateTransform();
                var tip = new RotateTransform(0, left ? 22 : 42, 40);
                var group = new TransformGroup();
                group.Children.Add(tip);
                group.Children.Add(move);
                var half = new System.Windows.Shapes.Path { Data = heart, Fill = color, Clip = Side(left), RenderTransform = group };
                holder.Children.Add(half);
                halves.Add((half, move, tip));
            }
            var scale = new ScaleTransform(0.2, 0.2);
            holder.RenderTransform = scale;
            IntroLayer.Children.Add(holder);

            if (Theme.ReduceMotion)
            {
                scale.ScaleX = scale.ScaleY = 1;
                halves[0].Move.X = -6; halves[1].Move.X = 6;
                var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                hold.Tick += (_, _) => { hold.Stop(); IntroLayer.Children.Remove(holder); };
                hold.Start();
                return;
            }

            // Pop up, as the favorite heart does.
            var pop = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0.2, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.25, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
            pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            // A little shudder, then the halves come apart: each slides out and down, tipping away from the other.
            var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var (_, move, tip) = halves[i];
                var shake = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(380) };
                shake.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(side * 2, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
                shake.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(side * -1, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
                shake.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(side * 1, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
                shake.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(side * 18, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), ease));
                move.BeginAnimation(TranslateTransform.XProperty, shake);
                move.BeginAnimation(TranslateTransform.YProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(480)) { BeginTime = TimeSpan.FromMilliseconds(500), EasingFunction = ease });
                tip.BeginAnimation(RotateTransform.AngleProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, side * 24, TimeSpan.FromMilliseconds(480)) { BeginTime = TimeSpan.FromMilliseconds(500), EasingFunction = ease });
            }
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(660) };
            fade.Completed += (_, _) => IntroLayer.Children.Remove(holder);
            holder.BeginAnimation(OpacityProperty, fade);
            Rumble();
        }
        catch (Exception) { }
    }

    // ================================================================ graphics memory

    private bool _gpuSaving;

    /// <summary>
    /// While a game runs, the launcher stops using the graphics card: it switches to software drawing (which
    /// releases its video memory), drops the blurred background and animated cover, and frees cached pictures.
    /// Everything comes back when the game closes.
    /// </summary>
    private void UpdateGpuSaver()
    {
        UpdateDynamicPlayback();
        UpdateBackgroundMode();
        UpdateGameMemory();
        // Software drawing only once SquareDeck is actually behind the game. Switching while the boost or launch
        // splash was still animating (the game is "running" a moment before its window shows) drew those animations,
        // with their blur and glow, without the graphics card: that's what made them stutter.
        bool covering = _screens.Count > 0 && _screens[^1] is LaunchSplashScreen or BoostScreen;
        bool save = Settings.FreeGpuDuringGames && AllGames.Any(g => g.IsRunning) && !covering && (_backgroundMode || !IsActive);
        if (save == _gpuSaving) return;
        _gpuSaving = save;
        try
        {
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource { CompositionTarget: { } target })
                target.RenderMode = save ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
        }
        catch (Exception) { }

        if (save)
        {
            if (_animatedGame != null) _animatedGame.AnimatedCover = null;
            ClearHero();
            Motion.StopPulse(RingOf(GameList.SelectedItem));
            // Give the memory back to Windows now rather than whenever the runtime gets round to it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ProcessUtil.TrimWorkingSet();
        }
        else
        {
            _heroKey = null;
            _ = ShowHeroAsync(GameList.SelectedItem as Game);
            if (!Theme.ReduceMotion) Motion.StartPulse(RingOf(GameList.SelectedItem));
        }
    }

    /// <summary>Called once from the constructor.</summary>
    private void InitMore()
    {
        EventManager.RegisterClassHandler(typeof(Button), MouseEnterEvent, new MouseEventHandler(OnButtonHover));
        GameList.PreviewMouseDown += OnGameListPointer;
        Motion.ButtonAnimations = Settings.ButtonAnimations;
        Motion.ButtonStyle = Settings.ButtonAnimationStyle;
        Motion.PageStyle = Settings.PageTransition;
        Motion.TileStyle = Settings.TileHighlight;
        Motion.PunchIn = Settings.PunchIn;
        // Mouse and touch: home buttons squash while held and spring back when let go.
        foreach (var button in _buttons.Concat(AllTopButtons).Append(MusicButton).Append(StopButton))
        {
            var b = button;
            b.PreviewMouseLeftButtonDown += (_, _) => Motion.HoldDown(b);
            b.PreviewMouseLeftButtonUp += (_, _) => Motion.Release(b);
            b.MouseLeave += (_, _) => Motion.Release(b);
            b.PreviewTouchDown += (_, _) => Motion.HoldDown(b);
            b.PreviewTouchUp += (_, _) => Motion.Release(b);
        }
        PreviewMouseMove += (_, e) => { if (e.StylusDevice == null) _mouseMovedAt = DateTime.UtcNow; };
        UpdateHomeRing();
        _windowsToastTimer.Tick += (_, _) => _ = CheckWindowsToastsAsync();
        _windowsToastTimer.Start();

        // Messages in the footer ("X is no longer running", "Couldn't start…") clear themselves after a while.
        _statusClear.Tick += (_, _) => { _statusClear.Stop(); StatusText.Text = ""; };
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))?.AddValueChanged(StatusText, (_, _) =>
        {
            _statusClear.Stop();
            if (StatusText.Text.Length > 0) _statusClear.Start();
        });

        // Mouse and touch count as activity; a real mouse also switches the hints to keyboard keys.
        PreviewMouseDown += (_, e) =>
        {
            if (e.StylusDevice == null) Hints.SetKeyboard(true);
            NoteActivity();
            // The mouse's own Back button (on its side) goes back, like B.
            if (e.ChangedButton == System.Windows.Input.MouseButton.XButton1 && _screens.Count > 0)
            {
                e.Handled = true;
                Handle(NavAction.Back);
            }
        };
        // Tapping or clicking "Back" in a page's hints goes back too.
        HintBar.BackRequested = () => { if (_screens.Count > 0) Handle(NavAction.Back); };
        PreviewMouseMove += (_, e) =>
        {
            var at = e.GetPosition(this);
            if (Math.Abs(at.X - _lastMouse.X) + Math.Abs(at.Y - _lastMouse.Y) < 6) return; // ignore jitter
            _lastMouse = at;
            NoteActivity();
        };
        PreviewTouchDown += (_, _) => NoteActivity();
        PreviewKeyDown += (_, _) => NoteActivity();

        _idleTimer.Tick += (_, _) =>
        {
            CheckScreensaver();
            UpdateMenuMusic();
        };
        _idleTimer.Start();

        SizeChanged += (_, _) => { if (Settings.AspectRatio == "auto") ApplyAspect(); };
        ApplyAspect();
    }

    // ================================================================ home buttons and hints

    /// <summary>Shows or hides the optional round buttons and rebuilds the list the thumbstick moves through.</summary>
    private void RefreshHomeButtons()
    {
        static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        BrowserBtn.Visibility = Shown(Settings.ShowBrowserButton);
        MusicBtn.Visibility = Shown(Settings.ShowMusicButton);
        FriendsBtn.Visibility = Shown(Settings.ShowFriendsButton);
        ExplorerBtn.Visibility = Shown(Settings.ShowExplorerButton);
        NewsBtn.Visibility = Shown(Settings.ShowNewsButton);
        MyDeviceBtn.Visibility = Shown(Settings.ShowMyDeviceButton && Handheld.IsHandheld);
        DrawMyDeviceIcon();

        _buttons = ButtonRow.Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible).ToArray();
        if (_buttonIndex >= _buttons.Length) _buttonIndex = -1;
        foreach (var hidden in ButtonRow.Children.OfType<Button>().Where(b => b.Visibility != Visibility.Visible)) hidden.Tag = null;
        UpdateButtonHighlight();
    }

    /// <summary>For Settings: shows or hides the home screen buttons after a change.</summary>
    public void RefreshHomeButtonsPublic()
    {
        RefreshHomeButtons();
        Dispatcher.BeginInvoke(new Action(FitButtonRow), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MyDevice_Click(object sender, RoutedEventArgs e) => Push(new Screens.MyDeviceScreen());

    /// <summary>My Device's button shows a drawn handheld in place of the font glyph the round buttons use.</summary>
    private void DrawMyDeviceIcon()
    {
        void Draw()
        {
            MyDeviceBtn.ApplyTemplate();
            var glyph = FindChild<TextBlock>(MyDeviceBtn, t => Equals(t.FontFamily?.Source, "Segoe MDL2 Assets") && t.Name != "Label");
            if (glyph?.Parent is not Panel holder || holder.Children.OfType<System.Windows.Shapes.Path>().Any()) return;
            double size = glyph.FontSize > 0 ? glyph.FontSize : 34;
            var path = new System.Windows.Shapes.Path
            {
                Data = Ui.HandheldShape,
                Stretch = Stretch.Uniform, Width = size * 1.35, Height = size * 0.95,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = MyDeviceBtn });
            glyph.Visibility = Visibility.Collapsed;
            holder.Children.Insert(holder.Children.IndexOf(glyph), path);
        }
        if (MyDeviceBtn.IsLoaded) Draw();
        else MyDeviceBtn.Loaded += (_, _) => Draw();
    }

    private static T? FindChild<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && match(t)) return t;
            if (FindChild(child, match) is { } found) return found;
        }
        return null;
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        SetButtonIndex(-1);
        SetTopIndex(-1);
        if (_mode != HomeView.Games) SetMode(HomeView.Games);
        else if (_view.Count > 0) GameList.SelectedIndex = 0; // already home: jump back to the first tile
    }

    private void Battery_Click(object sender, RoutedEventArgs e) => Push(new BatteryScreen());

    private void Browser_Click(object sender, RoutedEventArgs e) => Push(BrowserScreen.Open(this));

    // ================================================================ footer: pills and hints side by side

    private bool _fittingFooter, _footerHooked;

    /// <summary>
    /// What's playing, the running game and the button hints share the footer. With music and a game at once they ran
    /// into each other, so the pills' text gives way first (song title, then the game's name, then the status line),
    /// down to just the cover and icon, keeping a gap before the hints.
    /// </summary>
    private void FitFooter()
    {
        if (!_footerHooked)
        {
            _footerHooked = true;
            SizeChangedEventHandler again = (_, _) => QueueFitFooter();
            FooterGrid.SizeChanged += again;
            HomeHints.SizeChanged += again;
            FooterLeft.SizeChanged += again;
        }
        if (_fittingFooter || FooterGrid.ActualWidth <= 0) return;
        _fittingFooter = true;
        try
        {
            const double MusicMax = 170, GameMax = 190, StatusMax = 170, MusicMin = 70, GameMin = 90;
            MusicText.MaxWidth = MusicMax;
            StatusText.MaxWidth = StatusMax;
            if (_runningMarquee != null) _runningMarquee.MaxWidth = GameMax;

            double room = FooterGrid.ActualWidth - FooterLeft.Margin.Left - HomeHints.ActualWidth - HomeHints.Margin.Right - 28;
            FooterLeft.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double over = FooterLeft.DesiredSize.Width - room;
            if (over <= 0) return;

            double Natural(FrameworkElement e) { e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return e.DesiredSize.Width; }
            double Take(double have, double floor)
            {
                double cut = Math.Min(over, Math.Max(0, have - floor));
                over -= cut;
                return have - cut;
            }

            if (MusicButton.Visibility == Visibility.Visible) MusicText.MaxWidth = Take(Natural(MusicText), MusicMin);
            if (_runningMarquee != null && RunningPanel.Visibility == Visibility.Visible)
                _runningMarquee.MaxWidth = Take(Natural(_runningMarquee), GameMin); // longer names scroll
            if (StatusText.Text.Length > 0) StatusText.MaxWidth = Take(Natural(StatusText), 0);
            // Still too wide: the song title goes, its cover and play / pause stay.
            if (over > 0 && MusicButton.Visibility == Visibility.Visible) MusicText.MaxWidth = Take(MusicText.MaxWidth, 0);
        }
        finally { _fittingFooter = false; }
    }

    private bool _footerQueued;

    private void QueueFitFooter()
    {
        if (_footerQueued || _fittingFooter) return;
        _footerQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { _footerQueued = false; FitFooter(); }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The button hints in the home screen's footer: the same bar every other page uses.</summary>
    private void UpdateHomeHints()
    {
        bool running = RunningPanel.Visibility == Visibility.Visible;
        string template = (running ? "{Back}  Back to game      " : "") +
                          "{Details}  Details      {Options}  Options      {Select}  Start";
        // Checked every second or two with the running game: only redone (and the footer refitted) when it changed.
        if (HomeHints.Content is HintBar bar)
        {
            if (bar.Template == template) return;
            bar.Template = template;
        }
        else HomeHints.Content = new HintBar(template);
        QueueFitFooter();
    }

    // ================================================================ screen shape

    /// <summary>Lays the interface out at 16:9 (1280 x 720) or 16:10 (1280 x 800), so a 16:10 screen is filled edge to edge.</summary>
    private void ApplyAspect()
    {
        bool tall = Settings.AspectRatio switch
        {
            "16:10" => true,
            "auto" => ActualWidth > 0 && ActualHeight > 0 && ActualWidth / ActualHeight < 1.68,
            _ => false
        };
        double height = tall ? 800 : 720;
        if (Math.Abs(RootGrid.Height - height) < 0.5) return;

        RootGrid.Height = height;
        // The extra height is shared out so the home screen doesn't just gain a gap at the bottom.
        _tallLayout = tall;
        ApplyRowHeights();
    }

    // ================================================================ screen saver

    public static readonly (string Id, string Name)[] Screensavers =
    {
        ("off", "Off"), ("gradient", "Moving gradient"), ("heroes", "Game artwork slideshow")
    };

    private bool _saverOn;
    private CancellationTokenSource? _saverCts;
    private DispatcherTimer? _saverClock;

    private void NoteActivity()
    {
        _lastInput = DateTime.UtcNow;
        if (_saverOn) StopScreensaver();
    }

    /// <summary>Ends the screen saver if it is showing. True means the input that did it should be swallowed.</summary>
    private bool WakeFromScreensaver()
    {
        _lastInput = DateTime.UtcNow;
        if (!_saverOn) return false;
        StopScreensaver();
        return true;
    }

    private void CheckScreensaver()
    {
        bool allowed = Settings.Screensaver != "off" && IsActive && _screens.Count == 0 &&
                       IntroLayer.Children.Count == 0 && !AllGames.Any(g => g.IsRunning);
        if (_saverOn)
        {
            if (!allowed) StopScreensaver();
            return;
        }
        if (allowed && DateTime.UtcNow - _lastInput >= TimeSpan.FromMinutes(Math.Max(1, Settings.ScreensaverMinutes)))
            StartScreensaver();
    }

    /// <summary>Shows the screen saver now (also used by "Preview" in Settings).</summary>
    public void StartScreensaver(string? style = null)
    {
        if (_saverOn) return;
        style ??= Settings.Screensaver;
        if (style == "off") style = "gradient";
        _saverOn = true;
        _saverCts = new CancellationTokenSource();

        var root = new Grid { Background = Brushes.Black };
        SaverLayer.Children.Clear();
        SaverLayer.Children.Add(root);

        if (style == "heroes") BuildHeroSaver(root, _saverCts.Token);
        else BuildGradientSaver(root);

        // A clock that moves to a different corner every minute, so nothing sits still on an OLED screen.
        var time = new TextBlock { FontSize = 64, FontWeight = FontWeights.Light, Foreground = Brushes.White };
        var date = new TextBlock { FontSize = 22, Foreground = Brushes.White, Opacity = 0.8, Margin = new Thickness(4, 0, 0, 0) };
        var clock = new StackPanel { Margin = new Thickness(60), Opacity = 0.9 };
        clock.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 };
        clock.Children.Add(time);
        clock.Children.Add(date);
        root.Children.Add(clock);
        int corner = 0;
        void Tick()
        {
            time.Text = DateTime.Now.ToString(Settings.Clock24Hour ? "H:mm" : "h:mm");
            date.Text = DateTime.Now.ToString("dddd, MMMM d");
            int now = DateTime.Now.Minute % 4;
            if (now == corner && clock.Tag != null) return;
            corner = now;
            clock.Tag = "placed";
            // Bottom-left belongs to the game logo in the slideshow, so the clock skips it there.
            clock.HorizontalAlignment = corner is 0 or 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            clock.VerticalAlignment = corner is 0 or 1 || style == "heroes" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }
        Tick();
        _saverClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _saverClock.Tick += (_, _) => Tick();
        _saverClock.Start();

        SaverLayer.Visibility = Visibility.Visible;
        SaverLayer.BeginAnimation(OpacityProperty, Theme.ReduceMotion ? null : new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
        if (Theme.ReduceMotion) SaverLayer.Opacity = 1;
    }

    private void StopScreensaver()
    {
        if (!_saverOn) return;
        _saverOn = false;
        _saverCts?.Cancel();
        _saverCts = null;
        _saverClock?.Stop();
        _saverClock = null;
        SaverLayer.BeginAnimation(OpacityProperty, null);
        SaverLayer.Children.Clear();
        SaverLayer.Visibility = Visibility.Collapsed;
        _lastInput = DateTime.UtcNow;
    }

    /// <summary>Slow-moving bands of color built from the highlight color.</summary>
    private static void BuildGradientSaver(Grid root)
    {
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0xE0, 0xD0);
        static Color Shift(Color c, double r, double g, double b) => Color.FromRgb(
            (byte)Math.Clamp(c.R * r, 0, 255), (byte)Math.Clamp(c.G * g, 0, 255), (byte)Math.Clamp(c.B * b, 0, 255));

        var palette = new[]
        {
            Shift(accent, 0.55, 0.55, 0.55), Color.FromRgb(0x2B, 0x1B, 0x5E), Color.FromRgb(0x0B, 0x3D, 0x5C),
            Shift(accent, 0.8, 0.5, 0.9), Color.FromRgb(0x5A, 0x16, 0x3C), Color.FromRgb(0x08, 0x12, 0x30)
        };
        var spin = new RotateTransform(0, 0.5, 0.5);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), RelativeTransform = spin };
        for (int i = 0; i < 3; i++)
        {
            var stop = new GradientStop(palette[i], i / 2.0);
            brush.GradientStops.Add(stop);
            if (Theme.ReduceMotion) continue;
            stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette[i], palette[i + 3], TimeSpan.FromSeconds(9 + i * 4))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
            });
        }
        if (!Theme.ReduceMotion)
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(80)) { RepeatBehavior = RepeatBehavior.Forever });
        root.Children.Add(new System.Windows.Shapes.Rectangle { Fill = brush });
    }

    /// <summary>A slideshow of your games' wide artwork, each with its logo (or name) in the corner.</summary>
    private void BuildHeroSaver(Grid root, CancellationToken ct)
    {
        Image Layer()
        {
            var image = new Image
            {
                Stretch = Stretch.UniformToFill, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            root.Children.Add(image);
            return image;
        }
        var first = Layer();
        var second = Layer();

        // Darkens the bottom so the logo reads on any picture.
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.45));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1));
        root.Children.Add(new System.Windows.Shapes.Rectangle { Fill = shade });

        var logo = new Image { MaxWidth = 430, MaxHeight = 170, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var name = new TextBlock { FontSize = 40, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 700 };
        var caption = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(64, 0, 0, 60), Opacity = 0,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 }
        };
        caption.Children.Add(logo);
        caption.Children.Add(name);
        root.Children.Add(caption);

        _ = RunHeroSaverAsync(root, first, second, caption, logo, name, ct);
    }

    private async Task RunHeroSaverAsync(Grid root, Image first, Image second, StackPanel caption, Image logo, TextBlock name, CancellationToken ct)
    {
        try
        {
            var games = AllGames.Where(g => g.IsGame).OrderBy(_ => Random.Shared.Next()).ToList();
            string key = Settings.SteamGridDbApiKey;
            int index = 0, misses = 0;
            bool useFirst = true;
            var fade = TimeSpan.FromMilliseconds(Theme.ReduceMotion ? 1 : 1400);

            while (!ct.IsCancellationRequested)
            {
                if (games.Count == 0 || misses > games.Count)
                {
                    // No artwork to show (offline with nothing cached, or no games): fall back to the gradient.
                    BuildGradientSaver(root);
                    return;
                }
                var game = games[index++ % games.Count];
                var (hero, mark) = await Task.Run(async () =>
                {
                    using var client = new SteamGridDbClient(key);
                    var heroPath = await client.GetHeroAsync(game, ct);
                    if (heroPath == null) return ((ImageSource?)null, (ImageSource?)null);
                    string? logoPath = null;
                    try { logoPath = await client.GetLogoAsync(game, ct); } catch (Exception) when (!ct.IsCancellationRequested) { }
                    return (ImageLoader.FromFile(heroPath, 1920), logoPath == null ? null : ImageLoader.FromFile(logoPath, 860));
                }, ct);
                if (ct.IsCancellationRequested) return;
                if (hero == null) { misses++; continue; }
                misses = 0;

                var incoming = useFirst ? first : second;
                var outgoing = useFirst ? second : first;
                useFirst = !useFirst;

                incoming.Source = hero;
                incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(1, fade));
                outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fade));
                if (!Theme.ReduceMotion && incoming.RenderTransform is ScaleTransform zoom)
                {
                    // A slow push-in while the picture is up.
                    var drift = new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(13));
                    zoom.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
                    zoom.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
                }

                caption.BeginAnimation(OpacityProperty, null);
                caption.Opacity = 0;
                logo.Source = mark;
                logo.Visibility = mark != null ? Visibility.Visible : Visibility.Collapsed;
                name.Text = game.Name;
                name.Visibility = mark == null ? Visibility.Visible : Visibility.Collapsed;
                caption.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade) { BeginTime = TimeSpan.FromMilliseconds(Theme.ReduceMotion ? 0 : 700) });

                await Task.Delay(TimeSpan.FromSeconds(11), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested) BuildGradientSaver(root);
        }
    }

    // ================================================================ closing animations

    public static readonly (string Id, string Name)[] ExitStyles =
    {
        ("logo", "Logo goodbye"), ("fade", "Fade to black"), ("shrink", "Shrink away"),
        ("curtain", "Curtain"), ("tv", "Old TV switch-off"), ("random", "Surprise me")
    };

    /// <summary>
    /// A closing animation played for a look (the style builder), then everything put back: SquareDeck stays open.
    /// </summary>
    public async Task PreviewExitAsync(string style)
    {
        var background = Background;
        try { await PlayExitAnimationAsync(style); } catch (Exception) { }
        await Task.Delay(350);
        IntroLayer.Children.Clear();
        RootGrid.BeginAnimation(OpacityProperty, null);
        RootGrid.Opacity = 1;
        RootGrid.RenderTransform = null;
        Background = background;
        UiSound.Select();
    }

    /// <summary>Plays the chosen closing animation. The window closes when it has finished.</summary>
    private async Task PlayExitAnimationAsync(string? choice = null)
    {
        string style = choice ?? Settings.ExitStyle;
        if (style == "random")
        {
            var choices = ExitStyles.Where(s => s.Id != "random").ToArray();
            style = choices[Random.Shared.Next(choices.Length)].Id;
        }
        var quick = TimeSpan.FromMilliseconds(320);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        UiSound.Back();

        var black = new Grid { Background = Brushes.Black, Opacity = 0 };
        switch (style)
        {
            case "fade":
                IntroLayer.Children.Add(black);
                black.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
                await Task.Delay(640);
                break;

            case "shrink":
            {
                // The whole screen pulls back and fades out.
                Background = Brushes.Black;
                var scale = new ScaleTransform(1, 1);
                RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
                RootGrid.RenderTransform = scale;
                var pull = new DoubleAnimation(1, 0.82, TimeSpan.FromMilliseconds(520)) { EasingFunction = easeIn };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pull);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pull);
                RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(520)));
                await Task.Delay(560);
                break;
            }

            case "curtain":
            {
                // Two panels close over the screen from the top and the bottom.
                double half = RootGrid.Height / 2;
                foreach (bool top in new[] { true, false })
                {
                    var move = new TranslateTransform(0, top ? -half : half);
                    var panel = new Border
                    {
                        Height = half, Background = Brushes.Black, RenderTransform = move,
                        VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom
                    };
                    IntroLayer.Children.Add(panel);
                    move.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                }
                await Task.Delay(600);
                break;
            }

            case "tv":
            {
                // Like an old television: the picture collapses to a line, then to a dot.
                Background = Brushes.Black;
                var scale = new ScaleTransform(1, 1);
                RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
                RootGrid.RenderTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.006, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn });
                await Task.Delay(280);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeIn });
                await Task.Delay(300);
                break;
            }

            default:
            {
                // The page fades behind the logo, the logo shrinks away, then everything goes to black.
                var bye = new TextBlock
                {
                    Text = "See you next time", FontSize = 26, Margin = new Thickness(0, 22, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                bye.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                var scale = new ScaleTransform(1, 1);
                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, Opacity = 0
                };
                stack.Children.Add(BuildLogoMark(96));
                stack.Children.Add(bye);
                var cover = new Grid { Opacity = 0 };
                cover.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
                IntroLayer.Children.Add(cover);
                IntroLayer.Children.Add(stack);
                IntroLayer.Children.Add(black);

                cover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, quick));
                stack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, quick));
                await Task.Delay(760);
                var shrink = new DoubleAnimation(1, 0.7, quick) { EasingFunction = easeIn };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
                black.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, quick));
                await Task.Delay(360);
                break;
            }
        }
    }

    // ================================================================ updates

    private bool _checkingUpdates;

    /// <summary>
    /// Looks at the project's GitHub releases. Run automatically at startup (quiet unless there is something new)
    /// and from Settings (always says what it found).
    /// </summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            ReleaseInfo? release;
            try
            {
                release = await Task.Run(() => Updater.GetLatestAsync(CancellationToken.None));
            }
            catch (Exception)
            {
                if (manual) await AskAsync("Couldn't check for updates", "GitHub couldn't be reached. Check the internet connection and try again.", "OK");
                return;
            }

            if (release == null || !Updater.IsNewer(release))
            {
                if (manual)
                    await AskAsync("You're up to date",
                        $"This is SquareDeck {Updater.Current}." + (release == null ? " No releases have been published yet." : $" The newest release is {release.Tag}."), "OK");
                return;
            }
            if (!manual && release.Tag == Settings.SkippedVersion) return;
            // Experimental: download it first, so Install starts straight away.
            bool ready = false;
            if (Labs.On("autoupdate"))
            {
                if (manual) await RunBusyAsync("Downloading SquareDeck " + release.Tag, async () => ready = await Updater.PrefetchAsync(release, CancellationToken.None) != null);
                else ready = await Task.Run(() => Updater.PrefetchAsync(release, CancellationToken.None)) != null;
            }

            if (!manual)
            {
                // Wait for a quiet moment on the home screen rather than interrupting setup, a page or a game.
                for (int i = 0; i < 600 && (_screens.Count > 0 || IntroLayer.Children.Count > 0 || !IsActive || AllGames.Any(g => g.IsRunning)); i++)
                    await Task.Delay(1000);
                Notifications.Add("info", "Update available: SquareDeck " + release.Tag, "Install it from Settings > About.");
                UpdateBell();
            }

            int choice = await AskWithIconsAsync("SquareDeck " + release.Tag + " is available",
                $"You have version {Updater.Current}. " + (ready ? "It's downloaded and ready to install. " : "") + "See what's new and install it, or ignore this version.",
                ("Install", ""), ("Ignore this version", ""), ("Remind me later", ""));
            if (choice == 1)
            {
                Settings.SkippedVersion = release.Tag;
                Settings.Save();
            }
            else if (choice == 0)
            {
                Push(new UpdateScreen(release));
            }
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    /// <summary>Downloads the release and hands over to its installer (or the copy script), then closes the launcher.</summary>
    public async Task InstallUpdateAsync(ReleaseInfo release)
    {
        if (AllGames.FirstOrDefault(g => g.IsRunning) is { } running &&
            await AskAsync(running.Name + " is running", "The launcher restarts to update. The game keeps running, but finish or save first to be safe.",
                "Update anyway", "Cancel") != 0) return;

        // Already downloaded in the background (experimental), or a progress card with the bar, speed and time left; B cancels.
        string? file = Labs.On("autoupdate") ? Updater.Prefetched(release) : null;
        string? problem = null;
        if (file == null)
        {
            var download = new UpdateDownloadScreen(release);
            Push(download);
            file = await download.Result;
            problem = download.Problem;
            if (file == null && problem == null) return; // cancelled
        }

        if (file != null)
        {
            try
            {
                SaveEverything();
                if (file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    // Installed silently: a full-screen progress window takes over, then the new version opens.
                    string Hex(string key, string fallback) => (Application.Current.Resources[key] as SolidColorBrush)?.Color.ToString() ?? fallback;
                    string ready = SilentUpdate.Start(file, release.Tag, Hex("BgBrush", "#FF0B0E14"), Hex("FgBrush", "#FFFFFFFF"), Hex("AccentBrush", "#FF2E9BFF"),
                        Settings.RumbleStrength > 0 ? Settings.RumbleStrength / 100.0 : 0, Settings.UpdaterLook, Settings.UpdaterHaptic);
                    // Stay on screen until the update window shows, so there's no flash of the desktop in between.
                    await RunBusyAsync("Starting the update", async () =>
                    {
                        for (int i = 0; i < 100 && !File.Exists(ready); i++) await Task.Delay(100);
                    });
                    Close();
                    return;
                }
                Updater.Install(file);
                Close();
                return;
            }
            catch (Exception ex) { problem = ex.Message; }
        }
        if (await AskAsync("The update couldn't be installed", problem ?? "Something went wrong.", "Open the release page", "Close") == 0)
            ProcessUtil.Open(release.PageUrl);
    }

    /// <summary>The update screen with a given animation and vibration (the style builder), nothing installed.</summary>
    public void PreviewUpdateScreen(string? look, string? haptic)
    {
        string Hex(string key, string fallback) => (Application.Current.Resources[key] as SolidColorBrush)?.Color.ToString() ?? fallback;
        try
        {
            SilentUpdate.Preview(Hex("BgBrush", "#FF0B0E14"), Hex("FgBrush", "#FFFFFFFF"), Hex("AccentBrush", "#FF2E9BFF"),
                Settings.RumbleStrength > 0 ? Settings.RumbleStrength / 100.0 : 0, string.IsNullOrEmpty(look) ? Settings.UpdaterLook : look,
                string.IsNullOrEmpty(haptic) ? Settings.UpdaterHaptic : haptic);
        }
        catch (Exception ex) { ShowToast("UPDATES", "Couldn't show the update screen", ex.Message, "", "info"); }
    }

    private MediaPlayer? _musicTaste;
    private DispatcherTimer? _musicTasteTimer;

    /// <summary>
    /// A taste of a menu music choice (the style builder): about twelve seconds, with the menu music hushed meanwhile.
    /// </summary>
    public void PreviewMusic(string id)
    {
        try
        {
            _musicTaste?.Stop();
            _musicTaste?.Close();
            _musicTasteTimer?.Stop();
            string? file = id.StartsWith("file:", StringComparison.Ordinal) ? id[5..] : BackgroundMusic.Resolve(id);
            if (file == null || !System.IO.File.Exists(file)) return;
            double volume = Math.Clamp(Settings.MenuMusicVolume, 0, 100) / 100.0;
            if (volume <= 0) volume = 0.4;
            if (_menuMusic != null) _menuMusic.Volume = 0;
            var player = new MediaPlayer { Volume = volume };
            player.Open(new Uri(file));
            player.Play();
            _musicTaste = player;
            _musicTasteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _musicTasteTimer.Tick += (_, _) =>
            {
                _musicTasteTimer!.Stop();
                player.Stop();
                player.Close();
                if (ReferenceEquals(_musicTaste, player)) _musicTaste = null;
                if (_menuMusic != null) _menuMusic.Volume = MusicLevel;
            };
            _musicTasteTimer.Start();
        }
        catch (Exception) { }
    }

    /// <summary>Settings > Updates > Try the update screen: the real window, with nothing installed.</summary>
    public void PreviewUpdateScreen()
    {
        string Hex(string key, string fallback) => (Application.Current.Resources[key] as SolidColorBrush)?.Color.ToString() ?? fallback;
        try
        {
            SilentUpdate.Preview(Hex("BgBrush", "#FF0B0E14"), Hex("FgBrush", "#FFFFFFFF"), Hex("AccentBrush", "#FF2E9BFF"),
                Settings.RumbleStrength > 0 ? Settings.RumbleStrength / 100.0 : 0, Settings.UpdaterLook, Settings.UpdaterHaptic);
        }
        catch (Exception ex) { ShowToast("UPDATES", "Couldn't show the update screen", ex.Message, "", "info"); }
    }

    // ================================================================ reset

    /// <summary>Puts the launcher back to how it was when first installed, after asking how far to go.</summary>
    public async Task ResetLauncherAsync()
    {
        int choice = await AskWithIconsAsync("Reset the launcher",
            "“Settings only” puts every option back to its default and keeps your library changes, play time and artwork.\n\n" +
            "“Everything” also removes favorites, edited titles, custom games, added apps, collections, play time, notifications " +
            "and downloaded artwork. Your games themselves are never touched.",
            ("Reset settings only", ""), ("Reset everything", ""), ("Cancel", ""));
        if (choice is not (0 or 1)) return;
        bool everything = choice == 1;
        if (await AskAsync(everything ? "Erase all launcher data?" : "Reset all settings?",
                "The launcher restarts and first-time setup runs again. This can't be undone" +
                (everything ? "" : " (a backup from Settings > Your data can be restored later)") + ".",
                "Cancel", everything ? "Erase and restart" : "Reset and restart") != 1) return;

        // Undo the things that live outside the launcher's own folder.
        try { if (Startup.ReplacesDesktop) Startup.ReplacesDesktop = false; } catch (Exception) { }
        try { if (Startup.RunAtSignIn) Startup.RunAtSignIn = false; } catch (Exception) { }
        try { if (Settings.ToastsSuppressed) DoNotDisturb.Set(false); } catch (Exception) { }
        UnregisterSummonKey();

        await RunBusyAsync("Resetting", () => Task.Run(() =>
        {
            try
            {
                if (!everything)
                {
                    File.Delete(Path.Combine(Settings.DataDir, "settings.json"));
                    return;
                }
                foreach (var file in Directory.EnumerateFiles(Settings.DataDir))
                {
                    try { File.Delete(file); } catch (Exception) { }
                }
                foreach (var folder in Directory.EnumerateDirectories(Settings.DataDir))
                {
                    // Sound packs you added yourself are kept.
                    if (string.Equals(Path.GetFileName(folder), "SoundPacks", StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(folder, true); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }));
        RestartWithoutSaving();
    }

    // ================================================================ restart to UEFI

    /// <summary>Restarts the PC into its firmware setup screen, after confirming.</summary>
    public async Task RestartToFirmwareAsync()
    {
        var running = AllGames.FirstOrDefault(g => g.IsRunning);
        string message = "The PC restarts and opens its UEFI / BIOS setup screen instead of Windows. " +
                         "Windows asks for administrator permission first.";
        if (running != null) message += "\n\n" + running.Name + " is still running. Unsaved progress will be lost.";
        if (await AskAsync("Restart to UEFI setup?", message, "Cancel", "Restart to UEFI") != 1) return;

        SaveEverything();
        bool ok = await Task.Run(Power.RestartToFirmware);
        if (!ok)
            await AskAsync("Couldn't restart to UEFI",
                "Either the permission prompt was declined, or this PC's firmware doesn't support it " +
                "(older BIOS-mode installs don't). You can also get there from Windows Settings > System > Recovery > Advanced startup.",
                "OK");
    }

    // ================================================================ collections

    public GameCollection CreateCollection(string name)
    {
        var collection = new GameCollection { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        Library.Collections.Add(collection);
        Library.Save();
        return collection;
    }

    /// <summary>A checklist of collections for one game: pick one to put the game in it or take it out.</summary>
    public async Task ChooseCollectionsAsync(Game game)
    {
        game = game.Actual;
        while (true)
        {
            var collections = Library.Collections;
            var options = collections
                .Select(c => ((c.Games.Contains(game.Key) ? "✓  " : "") + c.Name, c.Icon))
                .ToList();
            options.Add(("New collection…", ""));
            options.Add(("Done", ""));

            int choice = await AskWithIconsAsync("Collections for " + game.Name,
                collections.Count == 0 ? "You have no collections yet. Make one to group games your own way."
                    : "Pick a collection to add the game to it, or to take it out again.",
                options.ToArray());
            if (choice < 0 || choice == collections.Count + 1) break;

            if (choice == collections.Count)
            {
                var name = await PromptAsync("New collection", "For example: Co-op night, Backlog, Cozy games.", "", "Create");
                if (string.IsNullOrWhiteSpace(name)) continue;
                CreateCollection(name).Games.Add(game.Key);
            }
            else
            {
                var picked = collections[choice];
                if (!picked.Games.Remove(game.Key)) picked.Games.Add(game.Key);
            }
            Library.Save();
            NotifyLibraryChanged();
        }
    }

    // ================================================================ menu music

    private MediaPlayer? _menuMusic;
    private string _menuMusicFile = "", _menuMusicChoice = "";

    // Where the menu music was when a game started (the player is closed to save memory), so it carries on from there.
    private string _resumeFile = "", _resumeChoice = "";
    private TimeSpan _resumeAt;

    /// <summary>Notes the track and the spot, then closes the player.</summary>
    private void SuspendMenuMusic()
    {
        if (_menuMusic != null && _menuMusicFile.Length > 0)
        {
            _resumeFile = _menuMusicFile;
            _resumeChoice = _menuMusicChoice;
            try { _resumeAt = _menuMusic.Position; } catch (Exception) { _resumeAt = TimeSpan.Zero; }
        }
        StopMenuMusic();
    }
    private bool _menuMusicPlaying;

    /// <summary>Something is playing through Windows' media controls (Spotify, the Music page, a browser…).</summary>
    private bool _mediaPlaying;

    /// <summary>
    /// Background music: plays quietly while the launcher is in front and nothing else needs the speakers
    /// (never while a game runs, nor with a video, the Music page or the browser open).
    /// A single track loops; "Shuffle all" moves on to another track at the end of each one.
    /// </summary>
    private DateTime _musicAfterStartup;

    /// <summary>Holds the menu music until the startup sound has finished, then starts it.</summary>
    private void HoldMusicForStartup()
    {
        var length = UiSound.StartupLength;
        if (length <= TimeSpan.Zero) return;
        _musicAfterStartup = DateTime.Now + length + TimeSpan.FromMilliseconds(250);
        if (_menuMusicPlaying && _menuMusic != null) { _menuMusic.Pause(); _menuMusicPlaying = false; }
        var later = new System.Windows.Threading.DispatcherTimer { Interval = length + TimeSpan.FromMilliseconds(300) };
        later.Tick += (_, _) => { later.Stop(); UpdateMenuMusic(); };
        later.Start();
    }

    /// <summary>Recomp Zone's own music (RecompZone\music.mp3 next to SquareDeck).</summary>
    private static string RecompMusicFile => Path.Combine(AppContext.BaseDirectory, "RecompZone", "music.mp3");

    /// <summary>Retro Lounge's own music (RetroLounge\music.mp3 next to SquareDeck).</summary>
    private static string RetroMusicFile => Path.Combine(AppContext.BaseDirectory, "RetroLounge", "music.mp3");

    /// <summary>
    /// A page's own music while it's open, once its opening has finished (Settings > Sound): Recomp Zone's or Retro
    /// Lounge's, whichever of the two was opened last.
    /// </summary>
    private string? PageMusic()
    {
        for (int i = _screens.Count - 1; i >= 0; i--)
        {
            if (_screens[i] is Screens.RecompZoneScreen { MusicReady: true } && Settings.RecompZoneMusic && File.Exists(RecompMusicFile)) return RecompMusicFile;
            if (_screens[i] is Screens.RetroLoungeScreen { MusicReady: true } && Settings.RetroLoungeMusic && File.Exists(RetroMusicFile)) return RetroMusicFile;
        }
        return null;
    }

    private bool _musicFading, _fadeInNext;
    private System.Windows.Threading.DispatcherTimer? _musicFade;

    private int _musicHolds;

    // ---- the music lowered while a question is asked (leaving Recomp Zone or Retro Lounge)

    private double _duck = 1;
    private System.Windows.Threading.DispatcherTimer? _duckTimer;

    /// <summary>How loud the menu music plays now: its setting, lowered while a question is being asked.</summary>
    private double MusicLevel => Math.Clamp(Settings.MenuMusicVolume, 0, 100) / 100.0 * _duck;

    /// <summary>
    /// Fades the music down to a third while a question is asked ("Leave Recomp Zone?"), and back up to where it was
    /// if you stay, so the moment feels like the zone holding its breath.
    /// </summary>
    public void DuckMusic(bool down)
    {
        double target = down ? 0.3 : 1;
        _duckTimer?.Stop();
        if (Theme.ReduceMotion) { _duck = target; ApplyDuck(); return; }
        double start = _duck;
        var begun = DateTime.Now;
        double ms = down ? 650 : 900; // a little slower coming back up
        _duckTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _duckTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.Now - begun).TotalMilliseconds / ms);
            double eased = t * t * (3 - 2 * t); // smooth at both ends
            _duck = start + (target - start) * eased;
            ApplyDuck();
            if (t >= 1) _duckTimer?.Stop();
        };
        _duckTimer.Start();
    }

    /// <summary>
    /// Back to full straight away, without touching the track that's playing: used when leaving, so the zone's music
    /// fades out from where it is and the launcher's own comes in at its usual level.
    /// </summary>
    public void ResetMusicDuck()
    {
        _duckTimer?.Stop();
        _duck = 1;
    }

    private void ApplyDuck()
    {
        try { if (_menuMusic != null && !_musicFading && _pauseFade?.IsEnabled != true && _fadeInTimer?.IsEnabled != true) _menuMusic.Volume = MusicLevel; } catch (Exception) { }
    }

    /// <summary>
    /// Pauses the menu music while something needs quiet (the style builder, so its sounds and music can be heard on
    /// their own), and lets it carry on from the same spot afterwards.
    /// </summary>
    public void HoldMenuMusic(bool hold)
    {
        _musicHolds = Math.Max(0, _musicHolds + (hold ? 1 : -1));
        try
        {
            if (_musicHolds > 0) { _menuMusic?.Pause(); return; }
            if (_menuMusic != null && _menuMusicPlaying) _menuMusic.Play();
            UpdateMenuMusic();
        }
        catch (Exception) { }
    }

    private void UpdateMenuMusic()
    {
        if (_musicHolds > 0) { try { _menuMusic?.Pause(); } catch (Exception) { } return; } // held quiet (the style builder)
        string choice = Settings.MenuMusic;
        if (choice.Length > 0 && GenreMusic is { } genre) choice = genre; // experimental: music that follows the genre
        if (PageMusic() is { } page) choice = "file:" + page; // Recomp Zone's or Retro Lounge's own, at the same loudness
        if (_musicFading) return; // a crossfade is under way: it calls back when the old track is quiet
        if (choice.Length == 0)
        {
            // Leaving Recomp Zone with the launcher's music off: its music fades away rather than stopping dead.
            if (_menuMusic != null && _menuMusicPlaying && _menuMusicChoice.StartsWith("file:", StringComparison.Ordinal) && !Theme.ReduceMotion)
                CrossfadeOut(StopMenuMusic);
            else StopMenuMusic();
            return;
        }
        if (_musicFading) return; // a crossfade is under way: it calls back when the old track is quiet

        // Changing between the launcher's music and a page's: the old one fades out, the new one fades in.
        if (_menuMusic != null && _menuMusicPlaying && choice != _menuMusicChoice && !Theme.ReduceMotion)
        {
            CrossfadeOut(() =>
            {
                // The launcher's track carries on from the same spot when you come back.
                if (!_menuMusicChoice.StartsWith("file:", StringComparison.Ordinal) && _menuMusic != null)
                {
                    _resumeFile = _menuMusicFile;
                    _resumeChoice = _menuMusicChoice;
                    try { _resumeAt = _menuMusic.Position; } catch (Exception) { _resumeAt = TimeSpan.Zero; }
                }
                StopMenuMusic();
                _fadeInNext = true;
                UpdateMenuMusic();
            });
            return;
        }

        try
        {
            if (_menuMusic == null || choice != _menuMusicChoice)
            {
                // Back from a game: the same track, from where it stopped (shuffle too).
                bool resuming = _resumeFile.Length > 0 && choice == _resumeChoice && File.Exists(_resumeFile);
                var file = resuming ? _resumeFile : choice.StartsWith("file:", StringComparison.Ordinal) ? choice[5..] : BackgroundMusic.Resolve(choice);
                var at = resuming ? _resumeAt : TimeSpan.Zero;
                _resumeFile = _resumeChoice = "";
                if (file == null) { StopMenuMusic(); return; }
                OpenMenuMusic(file, at);
                _menuMusicChoice = choice;
                if (choice.StartsWith("file:", StringComparison.Ordinal) && !Theme.ReduceMotion) _fadeInNext = true; // Recomp Zone's music rises in
            }
            // (Left alone while it's fading in or out: the fade sets it.)
            if (_pauseFade?.IsEnabled != true && _fadeInTimer?.IsEnabled != true) _menuMusic!.Volume = MusicLevel;

            bool busy = (_screens.Count > 0 && _screens[^1] is ViewerScreen or BrowserScreen or MediaScreen or MusicSearchScreen or StreamingScreen) || CloudOpen || LocalMusic.IsPlaying
                        || (Settings.MenuMusicPausesForMedia && (_mediaPlaying || SpotifyPlayer.IsPlaying));
            bool play = IsActive && !busy && _bootVideo == null && !AllGames.Any(g => g.IsRunning) && DateTime.Now >= _musicAfterStartup;
            if (play == _menuMusicPlaying) return;
            _menuMusicPlaying = play;
            if (play)
            {
                // Back (a game closed, a video or page closed, SquareDeck in front again): rises in from silence, or
                // from wherever a fade-out had got to, instead of starting at full volume.
                bool wasFadingOut = _pauseFade?.IsEnabled == true;
                _pauseFade?.Stop();
                if (!Theme.ReduceMotion) FadeIn(_menuMusic, MusicLevel, from: wasFadingOut ? _menuMusic.Volume : 0);
                _menuMusic.Play();
            }
            else FadeOutThenPause(_menuMusic); // a game starting, a video, SquareDeck behind something: fades away, not cut off
            _fadeInNext = false;
        }
        catch (Exception)
        {
            // No codec for the file, or no sound device: go without music.
        }
    }

    private void OpenMenuMusic(string file, TimeSpan startAt = default)
    {
        _menuMusic?.Close();
        var player = new MediaPlayer();
        if (startAt > TimeSpan.Zero)
        {
            // The position can only be set once the file is open.
            void Seek(object? sender, EventArgs e)
            {
                player.MediaOpened -= Seek;
                try { if (!player.NaturalDuration.HasTimeSpan || startAt < player.NaturalDuration.TimeSpan) player.Position = startAt; }
                catch (Exception) { }
            }
            player.MediaOpened += Seek;
        }
        player.MediaEnded += (_, _) =>
        {
            if (Settings.MenuMusic == "shuffle" && !_menuMusicChoice.StartsWith("file:", StringComparison.Ordinal)
                && BackgroundMusic.Resolve("shuffle", _menuMusicFile) is string next && next != _menuMusicFile)
            {
                bool wasPlaying = _menuMusicPlaying;
                OpenMenuMusic(next);
                _menuMusic!.Volume = MusicLevel;
                _menuMusicPlaying = wasPlaying;
                if (wasPlaying) _menuMusic.Play();
                return;
            }
            player.Position = TimeSpan.Zero; // a single track loops
            player.Play();
        };
        player.Open(new Uri(file));
        _menuMusic = player;
        _menuMusicFile = file;
        _menuMusicPlaying = false;
    }

    /// <summary>Fades the playing track out over half a second, then does <paramref name="then"/>.</summary>
    private void CrossfadeOut(Action then)
    {
        var player = _menuMusic!;
        double from = player.Volume;
        int step = 0;
        _musicFading = true;
        _musicFade?.Stop();
        _musicFade = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _musicFade.Tick += (_, _) =>
        {
            step++;
            try { player.Volume = from * Math.Max(0, 1 - step / 12.0); } catch (Exception) { }
            if (step < 12) return;
            _musicFade!.Stop();
            _musicFading = false;
            then();
        };
        _musicFade.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _pauseFade, _fadeInTimer;

    /// <summary>
    /// Fades the music down over about two thirds of a second, then pauses it (where it is, so it carries on from the
    /// same spot). Coming back before it's quiet cancels it and fades back up from there.
    /// </summary>
    private void FadeOutThenPause(MediaPlayer player)
    {
        _pauseFade?.Stop();
        if (Theme.ReduceMotion) { try { player.Pause(); } catch (Exception) { } return; }
        double from = player.Volume;
        int step = 0;
        _pauseFade = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _pauseFade.Tick += (_, _) =>
        {
            step++;
            if (!ReferenceEquals(player, _menuMusic) || _menuMusicPlaying) { _pauseFade?.Stop(); return; } // closed, or playing again
            try { player.Volume = from * Math.Max(0, 1 - step / 16.0); } catch (Exception) { }
            if (step < 16) return;
            _pauseFade?.Stop();
            try { player.Pause(); player.Volume = MusicLevel; } catch (Exception) { }
        };
        _pauseFade.Start();
    }

    /// <summary>Brings a track up from silence (or from <paramref name="from"/>) over about a second.</summary>
    private void FadeIn(MediaPlayer player, double target, double from = 0)
    {
        player.Volume = from;
        int step = 0;
        _fadeInTimer?.Stop();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _fadeInTimer = timer;
        timer.Tick += (_, _) =>
        {
            step++;
            if (!ReferenceEquals(player, _menuMusic)) { timer.Stop(); return; }
            if (!_menuMusicPlaying) { timer.Stop(); return; } // paused again meanwhile: the fade-out has it
            player.Volume = from + (target - from) * Math.Min(1, step / 25.0);
            if (step >= 25) timer.Stop();
        };
        timer.Start();
    }

    private void StopMenuMusic()
    {
        _pauseFade?.Stop();
        if (_menuMusic == null) return;
        _menuMusic.Close();
        _menuMusic = null;
        _menuMusicFile = _menuMusicChoice = "";
        _menuMusicPlaying = false;
    }

    /// <summary>Plays the chosen track from the start, so a new choice can be heard straight away.</summary>
    public void RestartMenuMusic()
    {
        _resumeFile = _resumeChoice = "";
        StopMenuMusic();
        UpdateMenuMusic();
    }

    // ================================================================ Steam profile picture

    /// <summary>The signed-in Steam account's profile picture, from Steam's own files on this PC. Null if not found.</summary>
    public static string? FindSteamAvatarFile()
    {
        try
        {
            var steam = SteamLibrary.FindSteamPath();
            if (steam == null || SteamLibrary.GetSteamId64() is not long id) return null;
            var file = Path.Combine(steam, "config", "avatarcache", id + ".png");
            return File.Exists(file) ? file : null;
        }
        catch (Exception) { return null; }
    }
}
