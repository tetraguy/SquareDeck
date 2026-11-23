using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A capture opened from the Captures page, full screen. The picture floats on a blurred, dimmed copy of itself,
/// with a filmstrip of the captures around it and a floating toolbar (previous, play / pause, zoom, share, delete,
/// back). The header, toolbar and filmstrip fade away after a few seconds and come back with any input.
/// LB / RB move through the captures of the view it was opened from (newest first, as on the page), LT / RT zoom a
/// screenshot and the right stick moves around it, Y shares and X deletes.
/// </summary>
public sealed class CaptureViewerScreen : Screen
{
    private static readonly double[] Zooms = { 1, 1.5, 2, 3, 4 };

    private readonly List<string> _paths;
    private readonly Dictionary<string, ImageSource?> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid _stage = new() { ClipToBounds = true };
    private readonly Image _ambient = new() { Stretch = Stretch.UniformToFill, Opacity = 0.55 };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { Foreground = Brushes.White, Opacity = 0.72, FontSize = 14, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _counter = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _zoomText = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly Border _counterPill, _zoomPill;
    private readonly TextBlock _caption = new() { Foreground = Brushes.White, FontSize = 13, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Grid _chrome = new();
    private readonly Button _previous, _next, _play, _zoom, _share, _delete, _back;
    private readonly TextBlock _playGlyph;
    private readonly Grid _progressRow = new() { Margin = new Thickness(18, 4, 18, 10), Visibility = Visibility.Collapsed };
    private readonly Border _progressFill = new() { HorizontalAlignment = HorizontalAlignment.Left, Height = 4, CornerRadius = new CornerRadius(2), Width = 0 };
    private readonly Border _progressTrack = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _elapsed = new() { Foreground = Brushes.White, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _length = new() { Foreground = Brushes.White, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _videoTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _pan = new();
    private readonly TranslateTransform _slide = new();

    /// <summary>
    /// Room around the picture for the header and the toolbar, measured from them (so the toolbar never covers the
    /// picture, whatever the screen's size and scale); a zoomed-in picture grows past it, full screen drops it.
    /// </summary>
    private Thickness _frame = new(56, 96, 56, 236);
    private bool _fullScreen;
    private readonly Button _fullButton;
    private readonly FrameworkElement _head, _bottom;
    private Image? _picture;
    private MediaElement? _video;
    private bool _playing, _chromeShown = true;
    private bool _padInput; // the controller (or keyboard) was used last: the controls stay up instead of fading
    private int _index, _zoomLevel, _direction;

    /// <summary>Set when something was deleted, so the page behind reloads.</summary>
    public bool Changed { get; private set; }

    private string Current => _paths[_index];

    public CaptureViewerScreen(IReadOnlyList<string> paths, int index)
    {
        _paths = paths.ToList();
        _index = Math.Clamp(index, 0, Math.Max(0, _paths.Count - 1));

        var root = new Grid { Background = Brushes.Black, ClipToBounds = true };

        // Behind: the capture itself, blurred and dimmed, so the colors of the picture fill the screen.
        if (!Perf.Active) _ambient.Effect = new BlurEffect { Radius = 60, KernelType = KernelType.Gaussian };
        _ambient.Margin = new Thickness(-80); // the blur's soft edges stay off screen
        root.Children.Add(_ambient);
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)) });

        // The picture or clip, centered with room for the header and toolbar.
        _stage.RenderTransform = _slide;
        root.Children.Add(_stage);
        root.MouseWheel += (_, e) => { Zoom(e.Delta > 0 ? +1 : -1); e.Handled = true; };
        root.MouseMove += (_, _) => { _padInput = false; Wake(); };

        // Header: game and date on the left, zoom and "3 / 12" on the right, over a soft shade.
        var head = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = 110 };
        head.Background = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        var words = new StackPanel { Margin = new Thickness(48, 24, 280, 0) };
        words.Children.Add(_title);
        words.Children.Add(_detail);
        head.Children.Add(words);
        var pills = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 28, 48, 0) };
        _zoomPill = Glass(_zoomText, new Thickness(14, 6, 14, 7), 16);
        _zoomPill.Margin = new Thickness(0, 0, 10, 0);
        _zoomPill.Visibility = Visibility.Collapsed;
        _counterPill = Glass(_counter, new Thickness(14, 6, 14, 7), 16);
        pills.Children.Add(_zoomPill);
        pills.Children.Add(_counterPill);
        head.Children.Add(pills);
        _chrome.Children.Add(head);
        _head = head;

        // Bottom: the floating toolbar, what the highlighted button does, and the filmstrip.
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 22) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 10) };
        _previous = Tool("", "Previous", () => Step(-1));
        _play = Tool("", "Pause", TogglePlay);
        _playGlyph = (TextBlock)_play.Content;
        _zoom = Tool("", "Zoom", () => Zoom(_zoomLevel + 1 < Zooms.Length ? +1 : -_zoomLevel));
        _fullButton = Tool("\uE740", "Full screen", ToggleFullScreen);
        _share = Tool("", "Share", Share);
        _delete = Tool("", "Delete", () => _ = DeleteAsync());
        _back = Tool("", "Close", () => Host.Pop(this));
        _next = Tool("", "Next", () => Step(+1));
        var tools1 = new List<Button> { _previous, _play, _zoom, _fullButton, _share };
        if (Labs.On("watermark")) tools1.Add(Tool("\uE8B9", "Add the game's logo", () => _ = WatermarkAsync()));
        tools1.AddRange(new[] { _delete, _back, _next });
        foreach (var b in tools1) tools.Children.Add(b);
        var toolbarStack = new StackPanel();
        toolbarStack.Children.Add(tools);
        // A clip's progress, inside the toolbar under its buttons.
        _progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _progressRow.ColumnDefinitions.Add(new ColumnDefinition());
        _progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var track = new Grid { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(_progressTrack);
        track.Children.Add(_progressFill);
        Grid.SetColumn(track, 1);
        Grid.SetColumn(_length, 2);
        _progressRow.Children.Add(_elapsed);
        _progressRow.Children.Add(track);
        _progressRow.Children.Add(_length);
        toolbarStack.Children.Add(_progressRow);
        var toolbar = Glass(toolbarStack, new Thickness(0), 34);
        toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Children.Add(toolbar);
        bottom.Children.Add(_caption);
        _strip.Margin = new Thickness(0, 14, 0, 0);
        bottom.Children.Add(_strip);
        bottom.Children.Add(new HintBar("LB RB  Previous / next      LT RT  Zoom      Start  Full screen      {Options}  Share      {Details}  Delete      {Back}  Back")
            { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Opacity = 0.7 });
        _chrome.Children.Add(bottom);
        _bottom = bottom;
        head.SizeChanged += (_, _) => UpdateFrame();
        bottom.SizeChanged += (_, _) => UpdateFrame();
        root.Children.Add(_chrome);

        Content = root;
        _hideTimer.Tick += (_, _) => HideChrome();
        _videoTimer.Tick += (_, _) => UpdateProgress();
        Show();
    }

    // ---------------------------------------------------------------- building blocks

    /// <summary>A dark, see-through rounded panel with a faint edge, for the toolbar and the pills.</summary>
    private static Border Glass(UIElement child, Thickness padding, double corner) => new()
    {
        Child = child, Padding = padding, CornerRadius = new CornerRadius(Math.Min(corner, Math.Max(Theme.TileCorner * 2, 4))),
        Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x16, 0x16, 0x1C)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A round icon button for the toolbar; its name shows under the toolbar while it's highlighted.</summary>
    private Button Tool(string glyph, string name, Action click)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Button { Style = Ui.StyleOf("PillButton"), Content = icon, Width = 52, Height = 52, Padding = new Thickness(0), Margin = new Thickness(4, 0, 4, 0), Tag = name, ToolTip = name };
        b.Click += (_, _) => { Wake(); click(); };
        b.GotKeyboardFocus += (_, _) => _caption.Text = b.Tag as string ?? "";
        b.MouseEnter += (_, _) => _caption.Text = b.Tag as string ?? "";
        return b;
    }

    public override void FocusDefault() => (_paths.Count > 0 && CapturesScreen.IsVideo(Current) ? _play : _share).Focus();

    public override void OnShown() => Wake();

    public override void OnResumed()
    {
        // Back from the share page or a question: carry on playing.
        if (_video != null && _playing) _video.Play();
        Wake();
    }

    public override void OnClosed()
    {
        _hideTimer.Stop();
        CloseVideo();
    }

    public override bool OnAction(NavAction action)
    {
        // With the controller the controls stay up (Menu switches to full screen for a clean look).
        _padInput = true;
        // The first press after the controls were hidden only brings them back (B still closes).
        bool wasHidden = !_chromeShown;
        Wake();
        if (wasHidden && action is NavAction.Accept or NavAction.Left or NavAction.Right or NavAction.Up or NavAction.Down) return true;
        switch (action)
        {
            case NavAction.RB: Step(+1); return true;
            case NavAction.LB: Step(-1); return true;
            case NavAction.RT: Zoom(+1); return true;
            case NavAction.LT: Zoom(-1); return true;
            case NavAction.Y: Share(); return true;
            case NavAction.X: _ = DeleteAsync(); return true;
            case NavAction.Back when _zoomLevel > 0: Zoom(-_zoomLevel); return true; // B zooms out first
            case NavAction.Back when _fullScreen: ToggleFullScreen(); return true;  // then leaves full screen
            case NavAction.Start: ToggleFullScreen(); return true;
        }
        return false;
    }

    /// <summary>The right stick moves around a zoomed-in screenshot.</summary>
    public override bool OnRightStick(double x, double y, bool pressed)
    {
        if (_zoomLevel == 0 || _picture == null) return x != 0 || y != 0;
        if (x == 0 && y == 0) return true;
        Wake();
        _pan.X -= x * Math.Abs(x) * 24;
        _pan.Y += y * Math.Abs(y) * 24;
        ClampPan();
        return true;
    }

    // ---------------------------------------------------------------- showing a capture

    /// <summary>The next (+1) or previous (-1) capture, wrapping around at the ends.</summary>
    private void Step(int delta)
    {
        if (_paths.Count < 2) return;
        _index = ((_index + delta) % _paths.Count + _paths.Count) % _paths.Count;
        _direction = delta;
        UiSound.Move();
        Show();
    }

    private async void Show()
    {
        CloseVideo();
        _stage.Children.Clear();
        _picture = null;
        ResetZoom();
        if (_paths.Count == 0) return;
        int showing = _index;
        string path = Current;
        bool video = CapturesScreen.IsVideo(path);
        bool many = _paths.Count > 1;
        _previous.Visibility = _next.Visibility = _counterPill.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        _play.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        _zoom.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        _progressRow.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        _counter.Text = $"{showing + 1} / {_paths.Count}";
        _title.Text = CapturesScreen.GameOf(path);
        _detail.Text = Describe(path, video);
        BuildStrip();
        if (Keyboard.FocusedElement is Button { IsVisible: false } || (video && Keyboard.FocusedElement == _zoom) || (!video && Keyboard.FocusedElement == _play))
            Host.FocusSoon(video ? _play : _share);

        // The backdrop: a small copy is enough once it's blurred.
        _ambient.Source = _thumbs.TryGetValue(path, out var known) ? known : null;
        _ = Task.Run(() => Thumb(path)).ContinueWith(t =>
        {
            _thumbs[path] = t.Result;
            if (showing == _index) _ambient.Source = t.Result;
            UpdateStripThumb(path, t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());

        FrameworkElement shown;
        if (video)
        {
            var element = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, Source = new Uri(path) };
            element.MediaEnded += (_, _) => { element.Stop(); SetPlaying(false); UpdateProgress(); };
            element.MediaOpened += (_, _) => UpdateProgress();
            Round(element);
            _video = element;
            shown = element;
            _stage.Children.Add(element);
            element.Play();
            SetPlaying(true);
            _videoTimer.Start();
        }
        else
        {
            var image = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_pan);
            image.RenderTransform = group;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Round(image);
            _picture = image;
            shown = image;
            _stage.Children.Add(image);
            var source = await Task.Run(() => (ImageSource?)ImageLoader.FromFile(path, 2560));
            if (showing != _index) return; // ignore a slow load we have moved past
            image.Source = source;
        }
        Arrive(shown);
    }

    /// <summary>The space the header and the toolbar take, kept clear of the picture.</summary>
    private void UpdateFrame()
    {
        double top = Math.Max(72, _head.ActualHeight - 6);
        double bottom = _bottom.ActualHeight + _bottom.Margin.Bottom + 16;
        _frame = new Thickness(56, top, 56, bottom);
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        var margin = _fullScreen ? new Thickness(0) : _frame;
        if (_picture != null) _picture.Margin = margin;
        if (_video != null) _video.Margin = margin;
        ClampPan();
    }

    /// <summary>
    /// Full screen: the picture fills the whole screen with nothing over it (Start, or the toolbar's button). Any
    /// button brings the toolbar back over it for a moment; Start or B leaves full screen.
    /// </summary>
    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        ((TextBlock)_fullButton.Content).Text = _fullScreen ? "\uE73F" : "\uE740";
        _fullButton.Tag = _fullButton.ToolTip = _fullScreen ? "Exit full screen" : "Full screen";
        if (Keyboard.FocusedElement == _fullButton) _caption.Text = (string)_fullButton.Tag;
        ApplyFrame();
        if (_fullScreen) HideChrome(force: true);
        else Wake();
        UiSound.Move();
    }

    /// <summary>Rounded corners and a soft shadow that follow the picture's own size (not the whole stage).</summary>
    private void Round(FrameworkElement element)
    {
        element.Margin = _fullScreen ? new Thickness(0) : _frame;
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        double radius = Math.Max(Theme.TileCorner, 4);
        element.SizeChanged += (_, _) => element.Clip = new RectangleGeometry(new Rect(element.RenderSize), radius, radius);
    }

    /// <summary>The new capture slides in from the side it came from and fades up.</summary>
    private void Arrive(FrameworkElement shown)
    {
        int from = _direction;
        _direction = 0;
        if (Theme.ReduceMotion) return;
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        if (from != 0)
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from * 70, 0, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static string Describe(string path, bool video)
    {
        var parts = new List<string> { video ? "Clip" : "Screenshot" };
        try
        {
            var info = new FileInfo(path);
            parts.Add(info.LastWriteTime.ToString("dddd, MMMM d, yyyy  ·  h:mm tt"));
            long bytes = info.Length;
            parts.Add(bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB");
        }
        catch (Exception) { }
        return string.Join("  ·  ", parts);
    }

    /// <summary>A small picture of a capture: the file itself for screenshots, Windows' thumbnail for clips.</summary>
    private static ImageSource? Thumb(string path)
    {
        try
        {
            if (!CapturesScreen.IsVideo(path)) return ImageLoader.FromFile(path, 320);
            var shell = ShellThumb.Get(path, 256);
            shell?.Freeze();
            return shell;
        }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------- filmstrip

    private readonly Dictionary<string, Border> _stripFaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Up to nine captures around this one; the current one is larger, with a ring in the highlight color.</summary>
    private void BuildStrip()
    {
        _strip.Children.Clear();
        _stripFaces.Clear();
        if (_paths.Count < 2) return;
        int span = Math.Min(4, (_paths.Count - 1) / 2);
        int start = _paths.Count <= 9 ? 0 : _index - span;
        int count = Math.Min(9, _paths.Count);
        for (int k = 0; k < count; k++)
        {
            int i = ((start + k) % _paths.Count + _paths.Count) % _paths.Count;
            string path = _paths[i];
            bool here = i == _index;
            double w = here ? 112 : 88, h = w * 9 / 16;
            var face = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner * 0.6, 3)), Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x50, 0x30, 0x30, 0x38)), BorderThickness = new Thickness(here ? 3 : 0),
                Opacity = here ? 1 : 0.55, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true
            };
            if (here) face.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (CapturesScreen.IsVideo(path))
                face.Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 14, Foreground = Brushes.White, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _stripFaces[path] = face;
            if (_thumbs.TryGetValue(path, out var known)) SetFace(face, known);
            else
            {
                _thumbs[path] = null; // loading
                _ = Task.Run(() => Thumb(path)).ContinueWith(t => { _thumbs[path] = t.Result; UpdateStripThumb(path, t.Result); },
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            var target = i;
            var button = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, Content = face, Cursor = Cursors.Hand };
            button.Click += (_, _) =>
            {
                Wake();
                if (target == _index) return;
                _direction = target > _index ? 1 : -1;
                _index = target;
                UiSound.Move();
                Show();
            };
            _strip.Children.Add(button);
        }
    }

    private void UpdateStripThumb(string path, ImageSource? source)
    {
        if (_stripFaces.TryGetValue(path, out var face)) SetFace(face, source);
    }

    private static void SetFace(Border face, ImageSource? source)
    {
        if (source != null) face.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
    }

    // ---------------------------------------------------------------- toolbar fading

    /// <summary>Brings the header, toolbar and filmstrip back, and starts the clock to fade them again.</summary>
    private void Wake()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
        if (_chromeShown) return;
        _chromeShown = true;
        _chrome.IsHitTestVisible = true;
        Fade(1);
    }

    private void HideChrome(bool force = false)
    {
        _hideTimer.Stop();
        // Stay up while a clip is paused or nothing is showing, and while the controller is in use (outside full screen).
        if (!force && (_paths.Count == 0 || (_video != null && !_playing) || (_padInput && !_fullScreen))) return;
        _chromeShown = false;
        _chrome.IsHitTestVisible = false;
        Fade(0);
    }

    private void Fade(double to)
    {
        if (Theme.ReduceMotion) { _chrome.BeginAnimation(OpacityProperty, null); _chrome.Opacity = to; return; }
        _chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 160 : 450)));
    }

    // ---------------------------------------------------------------- zoom

    private void Zoom(int steps)
    {
        if (_picture == null) return;
        int level = Math.Clamp(_zoomLevel + steps, 0, Zooms.Length - 1);
        if (level == _zoomLevel) return;
        _zoomLevel = level;
        double z = Zooms[level];
        if (Theme.ReduceMotion)
        {
            _scale.ScaleX = _scale.ScaleY = z;
        }
        else
        {
            var anim = new DoubleAnimation(z, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            anim.Completed += (_, _) => ClampPan();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        if (level == 0) { _pan.X = _pan.Y = 0; }
        ClampPan(z);
        _zoomText.Text = $"{z:0.#}×";
        _zoomPill.Visibility = level > 0 ? Visibility.Visible : Visibility.Collapsed;
        UiSound.Move();
    }

    private void ResetZoom()
    {
        _zoomLevel = 0;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = _scale.ScaleY = 1;
        _pan.X = _pan.Y = 0;
        _zoomPill.Visibility = Visibility.Collapsed;
    }

    /// <summary>Keeps a zoomed-in picture covering the screen: no panning past its edges.</summary>
    private void ClampPan(double? zoom = null)
    {
        if (_picture == null) return;
        double z = zoom ?? Zooms[_zoomLevel];
        double maxX = Math.Max(0, (_picture.ActualWidth * z - _stage.ActualWidth) / 2);
        double maxY = Math.Max(0, (_picture.ActualHeight * z - _stage.ActualHeight) / 2);
        _pan.X = Math.Clamp(_pan.X, -maxX, maxX);
        _pan.Y = Math.Clamp(_pan.Y, -maxY, maxY);
    }

    // ---------------------------------------------------------------- clips

    private void TogglePlay()
    {
        if (_video == null) return;
        if (_playing) _video.Pause(); else _video.Play();
        SetPlaying(!_playing);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _playGlyph.Text = playing ? "" : "";
        _play.Tag = _play.ToolTip = playing ? "Pause" : "Play";
        if (Keyboard.FocusedElement == _play) _caption.Text = (string)_play.Tag;
        if (!playing) Wake(); // keep the controls up while paused
    }

    private void UpdateProgress()
    {
        if (_video == null || !_video.NaturalDuration.HasTimeSpan) { _elapsed.Text = _length.Text = ""; _progressFill.Width = 0; return; }
        var total = _video.NaturalDuration.TimeSpan;
        var now = _video.Position;
        _elapsed.Text = Time(now);
        _length.Text = Time(total);
        double fraction = total.TotalSeconds > 0 ? Math.Clamp(now.TotalSeconds / total.TotalSeconds, 0, 1) : 0;
        _progressFill.Width = Math.Max(0, _progressTrack.ActualWidth * fraction);
    }

    private static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void CloseVideo()
    {
        _videoTimer.Stop();
        if (_video == null) return;
        try { _video.Stop(); _video.Close(); } catch (Exception) { }
        _video = null;
        _playing = false;
    }

    // ---------------------------------------------------------------- share and delete

    private void Share()
    {
        if (_paths.Count == 0) return;
        if (_video != null && _playing) _video.Pause();
        Host.Push(new ShareScreen(Current));
    }

    /// <summary>
    /// Experimental "Watermark screenshots": a copy of the screenshot with the game's logo in a corner (the size and
    /// corner you choose), saved next to it as "… (logo).png". The original is kept.
    /// </summary>
    private async Task WatermarkAsync()
    {
        if (_paths.Count == 0 || CapturesScreen.IsVideo(Current)) { await Host.AskAsync("Screenshots only", "The logo can be added to screenshots, not videos.", "OK"); return; }
        string path = Current;
        var game = GameForCapture(path);
        if (game == null) { await Host.AskAsync("Which game is this?", "The screenshot's name doesn't match a game in your library, so its logo can't be found.", "OK"); return; }
        int corner = await Host.AskAsync("Where should the logo go?", game.Name, "Top left", "Top right", "Bottom left", "Bottom right");
        if (corner < 0) return;
        int size = await Host.AskAsync("Logo size", null, "Small", "Normal", "Large");
        if (size < 0) return;
        string? saved = null, problem = null;
        await Host.RunBusyAsync("Adding the logo", async () =>
        {
            try
            {
                string? logoPath = null;
                using (var client = new SteamGridDbClient(Host.Settings.SteamGridDbApiKey))
                    try { logoPath = await client.GetLogoAsync(game, CancellationToken.None); } catch (Exception) { }
                BitmapSource? logo = null;
                if (logoPath != null) logo = await Task.Run(() => ImageLoader.FromFile(logoPath, 1600) as BitmapSource);
                if (logo == null && game.IsSteam)
                    logo = await ImageLoader.FromUrlAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.AppId}/logo.png", 1600) as BitmapSource;
                if (logo == null) { problem = "No logo was found for " + game.Name + "."; return; }
                saved = await Task.Run(() => Compose(path, logo, corner, size));
            }
            catch (Exception ex) { problem = ex.Message; }
        });
        if (saved == null) { await Host.AskAsync("Couldn't add the logo", problem ?? "Something went wrong.", "OK"); return; }
        Changed = true;
        _paths.Insert(_index + 1, saved);
        _index++;
        BuildStrip();
        Show();
        Host.ShowToast("SAVED", "Logo added", Path.GetFileName(saved), "", remember: false);
    }

    private Game? GameForCapture(string path)
    {
        static string Simple(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        // Steam's own screenshots sit in a folder named after the game's app id.
        var parts = path.Split(Path.DirectorySeparatorChar);
        int i = Array.FindIndex(parts, p => p.Equals("remote", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < parts.Length && int.TryParse(parts[i + 1], out var appId))
            return Host.AllGames.FirstOrDefault(g => g.IsSteam && g.AppId == appId);
        string of = Simple(CapturesScreen.GameOf(path));
        if (of.Length == 0) return null;
        return Host.AllGames.Where(g => g.IsGame).FirstOrDefault(g => Simple(g.Name) == of || Simple(g.OriginalName) == of)
            ?? Host.AllGames.Where(g => g.IsGame).FirstOrDefault(g => of.Length > 5 && (Simple(g.Name).StartsWith(of) || of.StartsWith(Simple(g.Name))));
    }

    /// <summary>Draws the logo onto the screenshot at full size and saves the copy. Returns its path.</summary>
    private static string Compose(string path, BitmapSource logo, int corner, int size)
    {
        var shot = new BitmapImage();
        shot.BeginInit();
        shot.CacheOption = BitmapCacheOption.OnLoad;
        shot.UriSource = new Uri(path);
        shot.EndInit();
        shot.Freeze();
        int w = shot.PixelWidth, h = shot.PixelHeight;
        double logoW = w * new[] { 0.14, 0.2, 0.28 }[size];
        double logoH = logoW * logo.PixelHeight / Math.Max(1, logo.PixelWidth);
        if (logoH > h * 0.3) { logoH = h * 0.3; logoW = logoH * logo.PixelWidth / Math.Max(1, logo.PixelHeight); }
        double margin = w * 0.025;
        double x = corner is 0 or 2 ? margin : w - margin - logoW;
        double y = corner is 0 or 1 ? margin : h - margin - logoH;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(shot, new Rect(0, 0, w, h));
            dc.DrawImage(logo, new Rect(x, y, logoW, logoH));
        }
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path);
        string output = Path.Combine(dir, name + " (logo).png");
        for (int n = 2; File.Exists(output); n++) output = Path.Combine(dir, $"{name} (logo {n}).png");
        using (var file = File.Create(output)) encoder.Save(file);
        return output;
    }

    private async Task DeleteAsync()
    {
        if (_paths.Count == 0) return;
        string path = Current;
        if (await Host.AskAsync("Delete this capture?", Path.GetFileName(path) + "\nIt will be moved to the Recycle Bin.", "Delete", "Cancel") != 0) return;
        CloseVideo();
        _stage.Children.Clear();
        try { FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
        catch (Exception ex)
        {
            await Host.AskAsync("Couldn't delete the file", ex.Message, "OK");
            Show();
            return;
        }
        Changed = true;
        _thumbs.Remove(path);
        _paths.RemoveAt(_index);
        if (_paths.Count == 0) { Host.Pop(this); return; }
        _index = Math.Min(_index, _paths.Count - 1); // the one that followed it
        Show();
    }
}

/// <summary>
/// SquareDeck's own share sheet for a capture: the file at the top, then tiles for everything that can be done with it
/// from here (copy it, open or edit it, save a copy to a folder, OneDrive or a USB drive).
/// </summary>
public sealed class ShareScreen : Screen
{
    private readonly string _path;
    private readonly TextBlock _status = Ui.Text("", 15, "AccentBrush", bold: true);
    private Button? _first;

    public override bool IsDialog => true;

    public ShareScreen(string path)
    {
        _path = path;
        bool video = CapturesScreen.IsVideo(path);
        var panel = new StackPanel();

        // Header: "Share" and a close button.
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = new Button { Style = Ui.StyleOf("PillButton"), Content = Icon("", 14), Padding = new Thickness(0), Width = 40, Height = 40, Focusable = false };
        close.Click += (_, _) => Host.Pop(this);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var shareIcon = Icon("", 20);
        shareIcon.Margin = new Thickness(0, 0, 12, 0);
        title.Children.Add(shareIcon);
        title.Children.Add(Ui.Text("Share", 22, "FgBrush", bold: true));
        head.Children.Add(title);
        panel.Children.Add(head);

        // The file: picture, name, size.
        var file = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6) };
        file.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        var fileRow = new DockPanel();
        var thumb = new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), Background = Brushes.Black };
        if (!video && ImageLoader.FromFile(path, 240) is { } small) thumb.Background = new ImageBrush(small) { Stretch = Stretch.UniformToFill };
        else thumb.Child = Icon("", 22);
        fileRow.Children.Add(thumb);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(Path.GetFileName(path), 16, "FgBrush", bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(name);
        words.Children.Add(Ui.Text(Describe(path, video), 13, "SubtleBrush"));
        fileRow.Children.Add(words);
        file.Child = fileRow;
        panel.Children.Add(file);

        // Copy: for pasting into a chat, an email or a document.
        panel.Children.Add(Section("Copy"));
        var copy = new WrapPanel();
        if (!video) copy.Children.Add(Tile("", "#3B82F6", "Copy picture", () => Do("Picture copied. Paste it anywhere.", CopyPicture)));
        copy.Children.Add(Tile("", "#6366F1", "Copy file", () => Do("File copied. Paste it into a chat, email or folder.", CopyFile)));
        copy.Children.Add(Tile("", "#64748B", "Copy path", () => Do("Path copied.", () => Clipboard.SetText(_path))));
        panel.Children.Add(copy);

        panel.Children.Add(Section("Share using"));
        var apps = new WrapPanel();
        apps.Children.Add(Tile(video ? "" : "", "#0EA5E9", video ? "Play in Windows" : "Photos", () => Leave(() => ProcessUtil.Open(_path))));
        if (!video) apps.Children.Add(Tile("", "#F59E0B", "Edit in Paint", () => Leave(() => ProcessUtil.Open("mspaint.exe", "\"" + _path + "\""))));
        apps.Children.Add(Tile("", "#EAB308", "Show in folder", () => Leave(() => ProcessUtil.Open("explorer.exe", "/select,\"" + _path + "\""))));
        string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        if (oneDrive.Length > 0 && Directory.Exists(oneDrive))
            apps.Children.Add(Tile("", "#2563EB", "OneDrive", () => SaveTo(Path.Combine(oneDrive, "Pictures", "SquareDeck"), "OneDrive > Pictures > SquareDeck. It uploads by itself.")));
        apps.Children.Add(Tile("", "#10B981", "Desktop", () => SaveTo(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "your desktop")));
        apps.Children.Add(Tile("", "#EC4899", video ? "Videos" : "Pictures",
            () => SaveTo(Environment.GetFolderPath(video ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures), video ? "Videos" : "Pictures")));
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Take(3))
            {
                string root = drive.RootDirectory.FullName, label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "USB drive" : drive.VolumeLabel;
                apps.Children.Add(Tile("", "#8B5CF6", $"{label} ({root.TrimEnd('\\')})", () => SaveTo(Path.Combine(root, "SquareDeck"), label)));
            }
        }
        catch (Exception) { }
        panel.Children.Add(apps);

        _status.Margin = new Thickness(4, 12, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_status);

        var card = new Border
        {
            Width = 640, Padding = new Thickness(28, 24, 28, 24), CornerRadius = new CornerRadius(20), Child = panel,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        card.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        backdrop.Children.Add(card);
        Content = backdrop;
    }

    public override void FocusDefault() => _first?.Focus();

    private static TextBlock Icon(string glyph, double size)
    {
        var t = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        return t;
    }

    private static TextBlock Section(string text)
    {
        var t = Ui.Text(text, 15, "FgBrush", bold: true);
        t.Margin = new Thickness(2, 14, 0, 8);
        return t;
    }

    private static string Describe(string path, bool video)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            string size = bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
            return (video ? "Clip" : "Screenshot") + "  ·  " + size;
        }
        catch (Exception) { return video ? "Clip" : "Screenshot"; }
    }

    /// <summary>One target: an icon on a colored square with its name underneath.</summary>
    private Button Tile(string glyph, string color, string label, Action click)
    {
        var square = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        };
        square.Child = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(label, 13);
        name.TextAlignment = TextAlignment.Center;
        name.TextWrapping = TextWrapping.Wrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxHeight = 36;
        name.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel { Width = 104, Height = 100 };
        stack.Children.Add(square);
        stack.Children.Add(name);
        var b = Ui.Tile(stack);
        b.Margin = new Thickness(0, 0, 4, 4);
        b.Click += (_, _) => click();
        _first ??= b;
        return b;
    }

    /// <summary>Does something that stays on this page, and says how it went.</summary>
    private void Do(string done, Action action)
    {
        try
        {
            action();
            _status.Text = "✓  " + done;
            UiSound.Select();
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't do that: " + ex.Message;
            UiSound.Error();
        }
    }

    /// <summary>Opens another program with the file, and closes the sheet.</summary>
    private void Leave(Func<bool> open)
    {
        if (open()) Host.Pop(this);
        else { _status.Text = "That couldn't be opened on this PC."; UiSound.Error(); }
    }

    private void CopyPicture()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // the file isn't kept open
        image.UriSource = new Uri(_path);
        image.EndInit();
        image.Freeze();
        Clipboard.SetImage(image);
    }

    private void CopyFile() => Clipboard.SetFileDropList(new StringCollection { _path });

    private void SaveTo(string folder, string where) => Do("Saved a copy to " + where + (where.EndsWith('.') ? "" : "."), () =>
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(_path));
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, Path.GetFileNameWithoutExtension(_path) + $" ({n})" + Path.GetExtension(_path));
        File.Copy(_path, target);
    });
}
