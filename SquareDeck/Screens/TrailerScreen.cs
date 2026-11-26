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
/// A game's trailers, played inside SquareDeck in a Switch 2 style pop-up (a floating card, round buttons with the
/// rimmed highlight, the UI style's colors): the trailer's name over the video, a progress bar
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
    private readonly TrailerPal? _pal = TrailerPal.Enabled ? new TrailerPal() : null; // the companion, with popcorn
    private static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _environment;

    public override bool IsDialog => true;

    public TrailerScreen(string game, List<Trailer> trailers)
    {
        _game = game;
        _trailers = trailers;
        // The Switch 2 look, like the game's page it opens from: a floating card in the card color, the title in
        // regular type, round buttons that take the rimmed, glowing highlight, and the bar's track in the line color.
        Switch2Settings.ApplyColors(this);

        // ---- the name of the game and the trailer, and which one of how many
        _head = new StackPanel { Margin = new Thickness(2, 0, 2, 14) };
        var top = new DockPanel();
        _count = Ui.Text("", 15, "SubtleBrush");
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(_count, Dock.Right);
        top.Children.Add(_count);
        _eyebrow = Ui.Text(game, 15, "SubtleBrush");
        top.Children.Add(_eyebrow);
        _head.Children.Add(top);
        _title = Ui.Text("", 26, "FgBrush");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Margin = new Thickness(0, 2, 0, 0);
        _head.Children.Add(_title);

        // ---- the video
        _frame = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Brushes.Black, Child = _web, Width = 960, Height = 540 };
        _status = Ui.Text("Loading the trailer…", 16, "SubtleBrush");

        // ---- progress and time
        var progress = new DockPanel { Margin = new Thickness(0, 14, 0, 4) };
        _time = Ui.Text("0:00 / 0:00", 15, "SubtleBrush");
        _time.Margin = new Thickness(14, 0, 0, 0);
        _time.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_time, Dock.Right);
        progress.Children.Add(_time);
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "S2Line");
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
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        Button Icon(string glyph, string tip, Action click, string? label = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            if (label != null) row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            var b = new Button { Style = RoundButton, Content = row, ToolTip = tip, Margin = new Thickness(7, 0, 7, 0) };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }
        _prev = Icon("", "Previous trailer (LB)", () => Step(-1));
        Icon("", "Back 10 seconds (LT)", () => Seek(-10));
        _play = Icon("", "Play or pause", TogglePlay, "Pause");
        _play.MinWidth = 150;
        _play.Tag = "primary"; // filled in the highlight color, as the Switch 2 marks the main button
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
        // The buttons and hints, with the companion in its cinema seat at the left when it's on. (Nothing can be drawn
        // over the video itself: it's a web view.)
        var controls = new StackPanel();
        controls.Children.Add(buttons);
        controls.Children.Add(_hints);
        if (_pal != null)
        {
            var row = new Grid();
            row.Children.Add(controls);
            _pal.View.HorizontalAlignment = HorizontalAlignment.Left;
            _pal.View.VerticalAlignment = VerticalAlignment.Bottom;
            _pal.View.Margin = new Thickness(-14, 0, 0, -10);
            row.Children.Add(_pal.View);
            stack.Children.Add(row);
        }
        else stack.Children.Add(controls);

        // ---- the pop-up itself, drawn like the style's other pop-ups
        _sheet = new Border
        {
            Padding = new Thickness(26, 22, 26, 20), CornerRadius = new CornerRadius(20), Child = stack,
            Background = new SolidColorBrush(Switch2Settings.Card),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };

        var dim = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)) };
        dim.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == dim) Host.Pop(this); };
        dim.Children.Add(_sheet);
        dim.SizeChanged += (_, _) => Fit();
        Content = dim;
        _poll.Tick += async (_, _) => await PollAsync();
    }

    private static Style? _round;

    /// <summary>A round button (a pill when it has a label), on the line color; highlighted, the rim and its glow.</summary>
    private static Style RoundButton => _round ??= (Style)System.Windows.Markup.XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource FgBrush}"/>
          <Setter Property="FontSize" Value="17"/>
          <Setter Property="Height" Value="54"/>
          <Setter Property="MinWidth" Value="54"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="Card" CornerRadius="30" BorderThickness="3" BorderBrush="Transparent" Background="{DynamicResource S2Line}" Margin="-3"/>
                  <Border Padding="18,0,18,0">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="Tag" Value="primary">
                    <Setter TargetName="Card" Property="Background" Value="{DynamicResource AccentBrush}"/>
                    <Setter Property="Foreground" Value="White"/>
                  </Trigger>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Card" Property="Opacity" Value="0.85"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource S2Rim}"/>
                    <Setter TargetName="Card" Property="Effect" Value="{DynamicResource S2Glow}"/>
                    <Setter TargetName="Card" Property="Opacity" Value="1"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    /// <summary>The video as large as fits: about four fifths of the screen in the pop-up, all of it when expanded.</summary>
    private void Fit()
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 0) return;
        double chrome = _expanded ? 130 : 285; // the title, progress, buttons and hints around it
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
        // The player is a window of its own (nothing SquareDeck draws can sit on top of it), so the loading animation is
        // part of the page: a spinning ring in the highlight color with a film strip running under it, "Loading trailer…"
        // until it plays, and "Buffering…" whenever it stops to wait for more.
        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0xE0, 0xD0);
        string ac = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        string loader =
            "<div id='ld'><div class='ring'><i></i><b>\u25B6</b></div>" +
            "<div class='strip'><div class='holes'></div></div>" +
            "<div id='lt'>Loading trailer<span class='d'>.</span><span class='d'>.</span><span class='d'>.</span></div></div>";
        string loaderCss =
            "#ld{position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:22px;" +
            "background:radial-gradient(circle at 50% 45%,#1a1d26 0%,#000 70%);transition:opacity .35s;pointer-events:none;font-family:'Segoe UI',sans-serif}" +
            "#ld.off{opacity:0}#ld.soft{background:rgba(0,0,0,.45)}" +
            ".ring{position:relative;width:96px;height:96px}" +
            ".ring i{position:absolute;inset:0;border-radius:50%;border:7px solid rgba(255,255,255,.12);border-top-color:" + ac + ";border-right-color:" + ac + ";animation:spin .9s linear infinite;box-shadow:0 0 24px " + ac + "55}" +
            ".ring b{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:#fff;font-size:30px;padding-left:6px;animation:pulse 1.2s ease-in-out infinite}" +
            ".strip{width:240px;height:26px;border-radius:5px;background:#111;overflow:hidden;border:2px solid #2a2a30}" +
            ".holes{width:200%;height:100%;background:repeating-linear-gradient(90deg,transparent 0 8px,#fff2 8px 18px,transparent 18px 26px)," +
            "linear-gradient(90deg," + ac + "33," + ac + "aa," + ac + "33);background-size:26px 8px,100% 100%;background-repeat:repeat-x,no-repeat;background-position:0 3px,0 0;animation:roll 1s linear infinite}" +
            "#lt{color:#fff;font-size:22px;font-weight:600;letter-spacing:.5px}" +
            ".d{animation:blink 1.2s infinite}.d:nth-child(2){animation-delay:.2s}.d:nth-child(3){animation-delay:.4s}" +
            "@keyframes spin{to{transform:rotate(360deg)}}@keyframes pulse{50%{transform:scale(.85);opacity:.7}}" +
            "@keyframes roll{to{transform:translateX(-26px)}}@keyframes blink{0%,100%{opacity:.2}50%{opacity:1}}";
        string loaderJs =
            "var ld=document.getElementById('ld'),lt=document.getElementById('lt'),started=false;" +
            "function busy(t){lt.firstChild.nodeValue=t;ld.className=started?'soft':''}" +
            "function idle(){started=true;ld.className='off'}" +
            "v.addEventListener('playing',idle);" +
            "v.addEventListener('waiting',function(){busy('Buffering')});" +
            "v.addEventListener('stalled',function(){if(v.readyState<3)busy('Buffering')});" +
            "v.addEventListener('seeking',function(){if(v.readyState<3)busy('Buffering')});" +
            "v.addEventListener('canplay',function(){if(v.paused&&started)ld.className='off'});" +
            "v.addEventListener('error',function(){busy(\"Couldn't load the trailer\");ld.querySelector('.ring').style.display='none'});";
        string page = "<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;height:100%;background:#000;overflow:hidden;cursor:pointer}" +
                      "video{width:100%;height:100%;object-fit:contain;background:#000}" + loaderCss + "</style>" +
                      (hls ? "<script src='https://cdn.jsdelivr.net/npm/hls.js@1/dist/hls.min.js'></script>" : "") +
                      "</head><body><video id='v' autoplay playsinline></video>" + loader + "<script>" +
                      $"var v=document.getElementById('v'),s={JsonSerializer.Serialize(src)};" +
                      (hls ? "if(v.canPlayType('application/vnd.apple.mpegurl')){v.src=s}else if(window.Hls&&Hls.isSupported()){var h=new Hls();h.loadSource(s);h.attachMedia(v)}else{v.src=s}"
                           : "v.src=s;") +
                      loaderJs +
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
        _pal?.NewTrailer();
    }

    private void Seek(int seconds)
    {
        _pal?.Skipped();
        Script($"var v=document.getElementById('v');if(v)v.currentTime=Math.max(0,Math.min((v.duration||1e9)-0.5,v.currentTime+({seconds})))");
    }

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
                _pal?.Paused(paused);
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
