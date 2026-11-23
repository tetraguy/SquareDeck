using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// A game's trailers, played inside SquareDeck in a pop-up drawn in the current UI style (its dialog background,
/// corners and buttons, or an Advanced style's pop-up artwork): the trailer's name over the video, a progress bar
/// with the time, and real buttons under it (previous, back 10 seconds, play / pause, forward 10 seconds, next,
/// sound, expand) that the controller moves between like any other row. Expand fills the screen and back.
/// LB / RB change trailer, LT / RT skip 10 seconds, Y expands, X mutes, B closes (or leaves expanded first).
/// </summary>
public sealed class TrailerScreen : Screen
{
    private readonly List<Trailer> _trailers;
    private readonly string _game;
    private int _index;
    private readonly Border _sheet, _frame, _barFill;
    private readonly Grid _barTrack = new() { Height = 6 };
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Black };
    private readonly TextBlock _eyebrow, _title, _count, _time, _status;
    private readonly StackPanel _head, _hints;
    private readonly Button _play, _mute, _expand, _prev, _next;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _expanded, _paused, _muted, _ready;
    private static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _environment;

    public override bool IsDialog => true;

    public TrailerScreen(string game, List<Trailer> trailers)
    {
        _game = game;
        _trailers = trailers;

        // ---- the name of the game and the trailer, and which one of how many
        _head = new StackPanel { Margin = new Thickness(2, 0, 2, 14) };
        var top = new DockPanel();
        _count = Ui.Text("", 14, "SubtleBrush", bold: true);
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(_count, Dock.Right);
        top.Children.Add(_count);
        _eyebrow = Ui.Text(game.ToUpperInvariant(), 13, "AccentBrush", bold: true);
        top.Children.Add(_eyebrow);
        _head.Children.Add(top);
        _title = Ui.Text("", 24, "FgBrush", bold: true);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Margin = new Thickness(0, 2, 0, 0);
        _head.Children.Add(_title);

        // ---- the video
        _frame = new Border { CornerRadius = new CornerRadius(Corner(14)), ClipToBounds = true, Background = Brushes.Black, Child = _web, Width = 960, Height = 540 };
        _status = Ui.Text("Loading the trailer…", 16, "SubtleBrush");

        // ---- progress and time
        var progress = new DockPanel { Margin = new Thickness(0, 14, 0, 4) };
        _time = Ui.Text("0:00 / 0:00", 14, "SubtleBrush", bold: true);
        _time.Margin = new Thickness(14, 0, 0, 0);
        _time.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_time, Dock.Right);
        progress.Children.Add(_time);
        var track = new Border { CornerRadius = new CornerRadius(3), Opacity = 0.25 };
        track.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        _barFill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _barTrack.Children.Add(track);
        _barTrack.Children.Add(_barFill);
        _barTrack.VerticalAlignment = VerticalAlignment.Center;
        _barTrack.Cursor = System.Windows.Input.Cursors.Hand;
        _barTrack.Background = Brushes.Transparent;
        _barTrack.MouseLeftButtonDown += (_, e) =>
        {
            double f = Math.Clamp(e.GetPosition(_barTrack).X / Math.Max(1, _barTrack.ActualWidth), 0, 1);
            Script($"var v=document.getElementById('v');if(v&&v.duration)v.currentTime=v.duration*{f.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        };
        progress.Children.Add(_barTrack);

        // ---- the buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        Button Icon(string glyph, string tip, Action click, string? label = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            if (label != null) row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            var b = new Button { Style = Ui.StyleOf("PanelButton"), Content = row, ToolTip = tip, Margin = new Thickness(4, 0, 4, 0), MinWidth = 64, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }
        _prev = Icon("", "Previous trailer (LB)", () => Step(-1));
        Icon("", "Back 10 seconds (LT)", () => Seek(-10));
        _play = Icon("", "Play or pause", TogglePlay, "Pause");
        _play.MinWidth = 150;
        Icon("", "Forward 10 seconds (RT)", () => Seek(10));
        _next = Icon("", "Next trailer (RB)", () => Step(1));
        _mute = Icon("", "Sound on or off (X)", ToggleMute);
        _expand = Icon("", "Expand (Y)", Expand, "Expand");
        if (trailers.Count < 2) { _prev.Visibility = Visibility.Collapsed; _next.Visibility = Visibility.Collapsed; }

        // ---- the controls, in the style's own button names
        _hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        void Hint(string key, string what, string? key2 = null)
        {
            _hints.Children.Add(HintBar.Pill(key, _hints.Children.Count == 0 ? 0 : 22));
            if (key2 != null) _hints.Children.Add(HintBar.Pill(key2, 4));
            var t = Ui.Text(what, 13, "SubtleBrush");
            t.Margin = new Thickness(7, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            _hints.Children.Add(t);
        }
        // Keys while the keyboard is in use, the controller's buttons while it is (redrawn when that changes).
        void BuildHints()
        {
            _hints.Children.Clear();
            if (trailers.Count > 1) Hint(Hints.KeyLabel("LB"), "Other trailers", Hints.KeyLabel("RB"));
            Hint(Hints.KeyLabel("LT"), "Skip 10 seconds", Hints.KeyLabel("RT"));
            Hint(Hints.KeyLabel("Options"), "Expand");
            Hint(Hints.KeyLabel("Details"), "Sound");
            Hint(Hints.KeyLabel("Back"), "Close");
        }
        BuildHints();
        Action redraw = () => Dispatcher.BeginInvoke(new Action(BuildHints));
        Loaded += (_, _) => { Hints.Changed -= redraw; Hints.Changed += redraw; };
        Unloaded += (_, _) => Hints.Changed -= redraw;

        var stack = new StackPanel();
        stack.Children.Add(_head);
        var videoHost = new Grid();
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        videoHost.Children.Add(_status);
        videoHost.Children.Add(_frame);
        stack.Children.Add(videoHost);
        stack.Children.Add(progress);
        stack.Children.Add(buttons);
        stack.Children.Add(_hints);

        // ---- the pop-up itself, drawn like the style's other pop-ups
        _sheet = new Border
        {
            Padding = new Thickness(26, 22, 26, 20), CornerRadius = new CornerRadius(Corner(24)), Child = stack,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };
        _sheet.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        _sheet.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _sheet.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        LookChrome.StyleSheet(_sheet);
        QuickSettingsScreen.SkinSheet(_sheet);

        var dim = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)) };
        dim.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == dim) Host.Pop(this); };
        dim.Children.Add(_sheet);
        dim.SizeChanged += (_, _) => Fit();
        Content = dim;
        _poll.Tick += async (_, _) => await PollAsync();
    }

    private static double Corner(double usual) => Theme.TileCorner <= 4 ? Math.Min(usual, Theme.TileCorner + 2) : usual;

    /// <summary>The video as large as fits: about four fifths of the screen in the pop-up, all of it when expanded.</summary>
    private void Fit()
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 0) return;
        double chrome = _expanded ? 120 : 270; // the title, progress, buttons and hints around it
        double maxW = root.ActualWidth * (_expanded ? 0.98 : 0.8) - (_expanded ? 0 : 52);
        double maxH = root.ActualHeight - chrome;
        double w = Math.Min(maxW, maxH * 16 / 9);
        _frame.Width = Math.Max(320, w);
        _frame.Height = Math.Max(180, w * 9 / 16);
        _barTrack.Width = double.NaN;
    }

    public override void FocusDefault() => _play.Focus();

    public override async void OnShown()
    {
        if (!Theme.ReduceMotion && _sheet.RenderTransform is ScaleTransform pop)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
        FocusLater(_play);
        try
        {
            _environment ??= Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "Trailers"),
                new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await _web.EnsureCoreWebView2Async(await _environment);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            // The page never keeps the controller's highlight: the buttons do.
            _web.GotFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _play.Focus()));
            _ready = true;
            Play();
            _poll.Start();
        }
        catch (Exception)
        {
            _status.Text = "Trailers need Microsoft Edge WebView2, which isn't available.";
            _frame.Visibility = Visibility.Hidden;
        }
    }

    public override void OnClosed()
    {
        _poll.Stop();
        try { _web.Dispose(); } catch (Exception) { }
    }

    // The video is its own window, which would sit on top of a pop-up opened over this one.
    public override void OnCovered() => _web.Visibility = Visibility.Hidden;
    public override void OnResumed() => _web.Visibility = Visibility.Visible;

    private void Play()
    {
        var t = _trailers[_index];
        _title.Text = t.Name.Length > 0 ? t.Name : "Trailer";
        _count.Text = _trailers.Count > 1 ? $"{_index + 1} of {_trailers.Count}" : "";
        _barFill.Width = 0;
        _time.Text = "0:00 / 0:00";
        if (!_ready) return;
        string src = t.Url ?? t.Stream!;
        bool hls = t.Url == null;
        // One video, no browser controls (SquareDeck's buttons drive it). Streams play through hls.js where needed.
        string page = "<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;height:100%;background:#000;overflow:hidden;cursor:pointer}" +
                      "video{width:100%;height:100%;object-fit:contain;background:#000}</style>" +
                      (hls ? "<script src='https://cdn.jsdelivr.net/npm/hls.js@1/dist/hls.min.js'></script>" : "") +
                      "</head><body><video id='v' autoplay playsinline></video><script>" +
                      $"var v=document.getElementById('v'),s={JsonSerializer.Serialize(src)};" +
                      (hls ? "if(v.canPlayType('application/vnd.apple.mpegurl')){v.src=s}else if(window.Hls&&Hls.isSupported()){var h=new Hls();h.loadSource(s);h.attachMedia(v)}else{v.src=s}"
                           : "v.src=s;") +
                      (_muted ? "v.muted=true;" : "") +
                      "v.addEventListener('click',function(){v.paused?v.play():v.pause()});" +
                      "v.play().catch(function(){});</script></body></html>";
        _web.CoreWebView2?.NavigateToString(page);
    }

    private void Step(int delta)
    {
        if (_trailers.Count < 2) return;
        _index = (_index + delta + _trailers.Count) % _trailers.Count;
        UiSound.Move();
        Play();
    }

    private void Seek(int seconds) =>
        Script($"var v=document.getElementById('v');if(v)v.currentTime=Math.max(0,Math.min((v.duration||1e9)-0.5,v.currentTime+({seconds})))");

    private void TogglePlay() => Script("var v=document.getElementById('v');if(v){v.paused?v.play():v.pause()}");

    private void ToggleMute()
    {
        _muted = !_muted;
        Script($"var v=document.getElementById('v');if(v)v.muted={(_muted ? "true" : "false")}");
        SetLabel(_mute, _muted ? "" : "", null);
    }

    private void Expand()
    {
        _expanded = !_expanded;
        _head.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        _hints.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        _sheet.Padding = _expanded ? new Thickness(12, 10, 12, 10) : new Thickness(26, 22, 26, 20);
        SetLabel(_expand, _expanded ? "" : "", _expanded ? "Shrink" : "Expand");
        Fit();
        FocusLater(_play);
    }

    private static void SetLabel(Button b, string glyph, string? label)
    {
        if (b.Content is not StackPanel row) return;
        if (row.Children[0] is TextBlock g) g.Text = glyph;
        if (label != null && row.Children.Count > 1 && row.Children[1] is TextBlock t) t.Text = label;
    }

    /// <summary>Reads where the video is, so the bar, the time and the play button stay true.</summary>
    private async Task PollAsync()
    {
        if (_web.CoreWebView2 == null) return;
        string json;
        try { json = await _web.CoreWebView2.ExecuteScriptAsync("(function(){var v=document.getElementById('v');return v?[v.currentTime,v.duration||0,v.paused?1:0]:null})()"); }
        catch (Exception) { return; }
        try
        {
            var a = JsonSerializer.Deserialize<double[]>(json);
            if (a == null || a.Length < 3) return;
            double now = a[0], total = double.IsFinite(a[1]) ? a[1] : 0;
            _status.Text = "";
            _barFill.Width = total > 0 ? _barTrack.ActualWidth * Math.Clamp(now / total, 0, 1) : 0;
            _time.Text = $"{Clock(now)} / {Clock(total)}";
            bool paused = a[2] > 0.5;
            if (paused != _paused)
            {
                _paused = paused;
                SetLabel(_play, paused ? "" : "", paused ? "Play" : "Pause");
            }
        }
        catch (Exception) { }
    }

    private static string Clock(double seconds)
    {
        if (seconds <= 0 || !double.IsFinite(seconds)) return "0:00";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private void Script(string js)
    {
        try { _ = _web.CoreWebView2?.ExecuteScriptAsync(js); } catch (Exception) { }
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y: Expand(); return true;
            case NavAction.X: ToggleMute(); return true;
            case NavAction.LT: Seek(-10); return true;
            case NavAction.RT: Seek(10); return true;
            case NavAction.LB when _trailers.Count > 1: Step(-1); return true;
            case NavAction.RB when _trailers.Count > 1: Step(1); return true;
            case NavAction.LB or NavAction.RB: return true; // one trailer: don't flip pages behind it
            case NavAction.Start: TogglePlay(); return true;
            case NavAction.Back when _expanded: Expand(); return true;
        }
        return false; // A presses the highlighted button; left and right move between them; B closes
    }
}
