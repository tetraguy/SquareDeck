using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.21: SquareDeck's own in-game quick menu, the choice beside the Xbox Game Bar widget (Settings > Controller and
/// Game Bar widget > In-game menu). A window of its own, always on top, that slides in over whatever is in front (a
/// game, a cloud game, the desktop, any app) and slides away again, giving the focus back to it.
///
/// Four tabs (LB / RB): Game (resume, SquareDeck, screenshot, record, keyboard, close the game, power), Performance (live
/// readings, the TDP with presets and a per-game profile, the ASUS mode and fans, the frame cap and refresh rate),
/// Quick settings (volume, brightness, what's playing and your quick tiles) and Device (battery, the lights, the
/// controller's mouse mode and the menu's own choices). It reads and changes everything through the same calls as the
/// Game Bar widget, so both always agree and do exactly the same things.
///
/// Controls: the d-pad (or left stick) moves, left / right also adjust sliders, A chooses, B or the summon buttons go
/// back to the game, X takes a screenshot, Y goes to SquareDeck. Touch, the mouse and the keyboard work too; tapping
/// outside the panel goes back to the game.
/// </summary>
public sealed class QuickMenuWindow : Window
{
    // ================================================================ the pieces a tab is made of

    private abstract class Item
    {
        public FrameworkElement View = null!;
        public virtual bool CanFocus => true;
        public virtual void SetFocus(bool on) { }
        public virtual bool Left() => false;
        public virtual bool Right() => false;
        public virtual void Accept() { }
        public virtual void Refresh() { }
        public virtual double FocusX => 0.5;
        public virtual void FocusNear(double x) { }
    }

    /// <summary>A heading, a note or a card that can't be chosen.</summary>
    private sealed class Static : Item
    {
        private readonly Action? _refresh;
        public Static(FrameworkElement view, Action? refresh = null) { View = view; _refresh = refresh; }
        public override bool CanFocus => false;
        public override void Refresh() => _refresh?.Invoke();
    }

    private sealed class Cell
    {
        public required Border Box;
        public required TextBlock Glyph, Label, Value;
        public required Action Run;
        public Func<bool?>? On;
        public Func<string?>? Text;
        public string? Confirm;
        public bool Danger;
        public string BaseLabel = "";
    }

    /// <summary>One or more buttons side by side; left and right move between them.</summary>
    private sealed class CellRow : Item
    {
        private readonly QuickMenuWindow _menu;
        public readonly List<Cell> Cells = new();
        private int _col;
        private bool _focused;

        public CellRow(QuickMenuWindow menu, IEnumerable<Cell> cells, double height)
        {
            _menu = menu;
            Cells.AddRange(cells);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            for (int i = 0; i < Cells.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var c = Cells[i];
                c.Box.Height = height;
                c.Box.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == Cells.Count - 1 ? 0 : 5, 0);
                Grid.SetColumn(c.Box, i);
                int index = i;
                c.Box.MouseLeftButtonUp += (_, e) => { e.Handled = true; _menu.FocusItem(this); _col = index; Paint(); Accept(); };
                grid.Children.Add(c.Box);
            }
            View = grid;
            Paint();
        }

        public override void SetFocus(bool on) { _focused = on; Paint(); }
        public override bool Left() { if (_col == 0) return false; _col--; Paint(); return true; }
        public override bool Right() { if (_col >= Cells.Count - 1) return false; _col++; Paint(); return true; }
        public override double FocusX => Cells.Count <= 1 ? 0.5 : (_col + 0.5) / Cells.Count;
        public override void FocusNear(double x) { _col = Math.Clamp((int)(x * Cells.Count), 0, Cells.Count - 1); Paint(); }

        public override void Accept()
        {
            var c = Cells[_col];
            if (c.Confirm != null && !ReferenceEquals(_menu._armed, c))
            {
                _menu.Arm(c);
                return;
            }
            _menu.Disarm();
            UiSound.Select();
            c.Run();
        }

        public override void Refresh() => Paint();

        public void Paint()
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                var c = Cells[i];
                bool on = c.On?.Invoke() == true;
                bool focus = _focused && i == _col;
                bool armed = ReferenceEquals(_menu._armed, c);
                var accent = c.Danger ? Color.FromRgb(0xE5, 0x48, 0x4D) : _menu._accent;
                c.Box.Background = new SolidColorBrush(on ? Color.FromArgb(0x55, accent.R, accent.G, accent.B) : armed ? Color.FromArgb(0x66, 0xE5, 0x48, 0x4D) : _menu._card);
                c.Box.BorderBrush = new SolidColorBrush(focus ? accent : _menu._line);
                c.Box.BorderThickness = new Thickness(focus ? 2.5 : 1);
                c.Glyph.Foreground = new SolidColorBrush(on || (focus && c.Danger) ? accent : _menu._text);
                c.Label.Text = armed ? c.Confirm : c.BaseLabel;
                string? value = c.Text?.Invoke() ?? (c.On == null ? null : c.On() switch { true => "On", false => "Off", _ => null });
                c.Value.Text = value ?? "";
                c.Value.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
                c.Box.RenderTransform = new ScaleTransform(focus ? 1.03 : 1, focus ? 1.03 : 1);
            }
        }
    }

    /// <summary>A slider: left and right change it, A does the slider's own extra (mute, for the volume).</summary>
    private sealed class SliderRow : Item
    {
        private readonly QuickMenuWindow _menu;
        private readonly Func<int?> _get;
        private readonly Action<int> _set;
        private readonly Func<int, string> _label;
        private readonly int _min, _max, _step;
        private readonly Action? _accept;
        private readonly Border _box, _fill, _track;
        private readonly TextBlock _value, _glyph;
        private bool _focused;
        private int? _pending;
        private DateTime _pendingAt;

        public SliderRow(QuickMenuWindow menu, string glyph, string name, int min, int max, int step, Func<int?> get, Action<int> set, Func<int, string> label, Action? accept = null)
        {
            _menu = menu; _get = get; _set = set; _label = label; _min = min; _max = Math.Max(min + 1, max); _step = step; _accept = accept;
            _glyph = Glyph(glyph, 22);
            var title = Label(name, 17);
            _value = Label("", 17, bold: true);
            _value.HorizontalAlignment = HorizontalAlignment.Right;
            var top = new Grid();
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            _glyph.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(_glyph);
            left.Children.Add(title);
            top.Children.Add(left);
            top.Children.Add(_value);
            _track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 12, 0, 0) };
            _fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var trackHolder = new Grid();
            trackHolder.Children.Add(_track);
            var fillHolder = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            fillHolder.Children.Add(_fill);
            trackHolder.Children.Add(fillHolder);
            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(trackHolder);
            _box = new Border { Child = stack, CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            _box.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _menu.FocusItem(this);
                double w = _track.ActualWidth;
                if (w <= 0) return;
                double x = e.GetPosition(_track).X / w;
                Commit((int)Math.Round(_min + Math.Clamp(x, 0, 1) * (_max - _min)));
            };
            _track.SizeChanged += (_, _) => Paint();
            View = _box;
            Paint();
        }

        private int Current => _pending is int p && DateTime.Now - _pendingAt < TimeSpan.FromSeconds(2) ? p : _get() ?? _min;

        private void Commit(int value)
        {
            value = Math.Clamp(value, _min, _max);
            _pending = value;
            _pendingAt = DateTime.Now;
            _set(value);
            UiSound.Move();
            Paint();
        }

        public override void SetFocus(bool on) { _focused = on; Paint(); }
        public override bool Left() { Commit(Current - _step); return true; }
        public override bool Right() { Commit(Current + _step); return true; }
        public override void Accept() { if (_accept != null) { UiSound.Select(); _accept(); } }
        public override void Refresh() => Paint();

        private void Paint()
        {
            int v = Current;
            _value.Text = _label(v);
            _box.Background = new SolidColorBrush(_focused ? _menu._card : Colors.Transparent);
            _box.BorderBrush = new SolidColorBrush(_focused ? _menu._accent : Colors.Transparent);
            _box.BorderThickness = new Thickness(_focused ? 2.5 : 0);
            _track.Background = new SolidColorBrush(_menu._off);
            _fill.Background = new SolidColorBrush(_menu._accent);
            double w = _track.ActualWidth;
            _fill.Width = w <= 0 ? 0 : Math.Max(8, w * (v - _min) / (double)(_max - _min));
            _glyph.Foreground = new SolidColorBrush(_focused ? _menu._accent : _menu._text);
        }
    }

    // ================================================================ the window

    private readonly MainWindow _host;
    private readonly Grid _root = new();
    private readonly Border _scrim = new() { Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)) };
    private readonly Border _panel = new() { ClipToBounds = true };
    private readonly TranslateTransform _slide = new();
    private readonly StackPanel _items = new() { Margin = new Thickness(26, 6, 26, 24) };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _clock = Label("", 34, bold: true);
    private readonly TextBlock _date = Label("", 15);
    private readonly TextBlock _battery = Label("", 17, bold: true);
    private readonly TextBlock _batteryGlyph = Glyph("\uE83F", 22);
    private readonly TextBlock _note = Label("", 16, bold: true);
    private readonly Border _noteBox = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 8, 18, 9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 74), Opacity = 0, IsHitTestVisible = false };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _disarm = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<Item> _list = new();
    private readonly List<(string Name, string Glyph)> _tabs = new() { ("Game", "\uE7FC"), ("Performance", "\uEC4A"), ("Quick settings", "\uE9E9"), ("Device", "\uE7F8") };
    private int _tab, _focus = -1;
    private JsonObject _state = new();
    private string _shape = "";
    private IntPtr _previous;
    private DateTime _openedAt;
    private bool _closing;
    private Cell? _armed;
    private Color _accent, _card, _line, _text, _subtle, _off, _bg;
    private readonly CpuMeter _cpu = new();

    public bool IsOpen { get; private set; }

    public QuickMenuWindow(MainWindow host)
    {
        _host = host;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Title = "SquareDeck quick menu";
        FontFamily = host.FontFamily;

        _scrim.MouseLeftButtonUp += (_, _) => CloseMenu();
        _root.Children.Add(_scrim);

        _scroll = new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.VerticalOnly, Focusable = false };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top: the time and the battery.
        var header = new Grid { Margin = new Thickness(28, 24, 28, 10) };
        var time = new StackPanel();
        time.Children.Add(_clock);
        time.Children.Add(_date);
        header.Children.Add(time);
        var battery = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _batteryGlyph.Margin = new Thickness(0, 0, 8, 0);
        battery.Children.Add(_batteryGlyph);
        battery.Children.Add(_battery);
        header.Children.Add(battery);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // The tabs, with LB and RB either side.
        var tabs = new Grid { Margin = new Thickness(20, 4, 20, 12) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lb = new HintBar("{LB}") { VerticalAlignment = VerticalAlignment.Center };
        var rb = new HintBar("{RB}") { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lb, 0);
        Grid.SetColumn(_tabStrip, 1);
        Grid.SetColumn(rb, 2);
        tabs.Children.Add(lb);
        tabs.Children.Add(_tabStrip);
        tabs.Children.Add(rb);
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);

        Grid.SetRow(_scroll, 2);
        layout.Children.Add(_scroll);

        var hints = new HintBar("{Select}  Choose      ◀▶  Adjust      {Details}  Screenshot      {Options}  SquareDeck      {Back}  Back to the game")
        { Margin = new Thickness(26, 10, 26, 18), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(hints, 3);
        layout.Children.Add(hints);

        var inner = new Grid();
        inner.Children.Add(layout);
        _noteBox.Child = _note;
        inner.Children.Add(_noteBox);
        _panel.Child = inner;
        _panel.RenderTransform = _slide;
        _panel.MouseLeftButtonUp += (_, e) => e.Handled = true; // taps on the panel don't fall through to the scrim
        _root.Children.Add(_panel);
        Content = _root;

        _tick.Tick += (_, _) => RefreshState();
        _disarm.Tick += (_, _) => Disarm();
        SourceInitialized += (_, _) => SetToolWindow();
        WidgetBridge.Noted += text => Dispatcher.BeginInvoke(() => ShowNote(text));
        KeyDown += OnKey;
    }

    // ---------------------------------------------------------------- opening and closing

    public void OpenMenu()
    {
        if (IsOpen && !_closing) return;
        _closing = false;
        _previous = WindowActivator.Foreground();
        _openedAt = DateTime.Now;
        Colorize();
        // The screen the game is on, whole.
        var area = ScreenOf(_previous);
        Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
        double width = Math.Min(560, Math.Max(400, area.Width * 0.4));
        _panel.Width = width;
        bool right = _host.Settings.QuickMenuSide == "right";
        _panel.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _panel.CornerRadius = right ? new CornerRadius(24, 0, 0, 24) : new CornerRadius(0, 24, 24, 0);
        _panel.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        _state = SafeState();
        _shape = "";
        BuildTab(keepFocus: false);
        RefreshClock();
        bool takes = _host.Settings.QuickMenuFocus;
        SetNoActivate(!takes);
        ShowActivated = takes;
        if (!IsVisible) Show();
        if (takes) WindowActivator.ShowOnTop(this); else WindowActivator.KeepOnTop(this);
        IsOpen = true;
        _tick.Start();
        // Slides in from its side with a little settle; the scrim fades in behind.
        double from = right ? width : -width;
        if (Theme.ReduceMotion) { _slide.BeginAnimation(TranslateTransform.XProperty, null); _slide.X = 0; _scrim.Opacity = 1; }
        else
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } });
            _scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        UiSound.Select();
    }

    /// <summary>Slides away; the game (or whatever was in front) gets the focus back unless <paramref name="restore"/> is false.</summary>
    public void CloseMenu(bool restore = true, Action? after = null)
    {
        if (!IsOpen || _closing) return;
        _closing = true;
        _tick.Stop();
        Disarm();
        void Done()
        {
            if (!_closing) return;
            _closing = false;
            IsOpen = false;
            Hide();
            if (restore && _host.Settings.QuickMenuFocus && _previous != IntPtr.Zero) WindowActivator.Focus(_previous);
            after?.Invoke();
        }
        if (Theme.ReduceMotion) { Done(); return; }
        bool right = _host.Settings.QuickMenuSide == "right";
        var slide = new DoubleAnimation(0, right ? _panel.Width : -_panel.Width, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        slide.Completed += (_, _) => Done();
        _slide.BeginAnimation(TranslateTransform.XProperty, slide);
        _scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)));
        UiSound.Back();
    }

    /// <summary>Closes the menu, then does something once it's out of the way (a screenshot shouldn't show the menu).</summary>
    private void Then(Action what, int delayMs = 250, bool restore = true) =>
        CloseMenu(restore, () => Dispatcher.BeginInvoke(async () => { await Task.Delay(delayMs); what(); }));

    // ---------------------------------------------------------------- input

    /// <summary>A controller button (from SquareDeck's controller loop while the menu is open).</summary>
    public void OnAction(NavAction action)
    {
        if (!IsOpen || _closing) return;
        if (DateTime.Now - _openedAt < TimeSpan.FromMilliseconds(250)) return; // the buttons that opened it don't count
        switch (action)
        {
            case NavAction.Up: Move(-1); break;
            case NavAction.Down: Move(1); break;
            case NavAction.Left: if (Current?.Left() != true) Feedback(); break;
            case NavAction.Right: if (Current?.Right() != true) Feedback(); break;
            case NavAction.Accept: Current?.Accept(); break;
            case NavAction.LB: SwitchTab(-1); break;
            case NavAction.RB: SwitchTab(1); break;
            case NavAction.X: Then(() => _host.QuickMenuAction("screenshot"), 350); break;
            case NavAction.Y: Then(() => _host.QuickMenuAction("home"), 120, restore: false); break;
            case NavAction.LT: _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - 240); break;
            case NavAction.RT: _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + 240); break;
            case NavAction.Back or NavAction.Start or NavAction.View: CloseMenu(); break;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        NavAction? action = e.Key switch
        {
            Key.Up => NavAction.Up, Key.Down => NavAction.Down, Key.Left => NavAction.Left, Key.Right => NavAction.Right,
            Key.Enter or Key.Space => NavAction.Accept, Key.Escape or Key.Back => NavAction.Back,
            Key.PageUp or Key.Q => NavAction.LB, Key.PageDown or Key.E => NavAction.RB, _ => null,
        };
        if (action == null) return;
        e.Handled = true;
        OnAction(action.Value);
    }

    private static void Feedback() { }

    private Item? Current => _focus >= 0 && _focus < _list.Count ? _list[_focus] : null;

    private void Move(int delta)
    {
        if (_list.Count == 0) return;
        double x = Current?.FocusX ?? 0.5;
        int i = _focus;
        do { i += delta; } while (i >= 0 && i < _list.Count && !_list[i].CanFocus);
        if (i < 0 || i >= _list.Count) { if (delta < 0) _scroll.ScrollToTop(); else _scroll.ScrollToBottom(); return; }
        SetFocusIndex(i, x);
        UiSound.Move();
    }

    private void SetFocusIndex(int index, double x = -1)
    {
        Disarm();
        Current?.SetFocus(false);
        _focus = index;
        if (Current is not { } item) return;
        if (x >= 0) item.FocusNear(x);
        item.SetFocus(true);
        item.View.BringIntoView(new Rect(0, -60, item.View.ActualWidth, item.View.ActualHeight + 120));
    }

    private void FocusItem(Item item)
    {
        int i = _list.IndexOf(item);
        if (i >= 0 && i != _focus) SetFocusIndex(i);
    }

    private void SwitchTab(int delta)
    {
        _tab = (_tab + delta + _tabs.Count) % _tabs.Count;
        UiSound.Move();
        _shape = "";
        BuildTab(keepFocus: false);
        _scroll.ScrollToTop();
    }

    private void Arm(Cell cell)
    {
        _armed = cell;
        _disarm.Stop();
        _disarm.Start();
        UiSound.Move();
        foreach (var row in _list.OfType<CellRow>()) row.Paint();
    }

    private void Disarm()
    {
        _disarm.Stop();
        if (_armed == null) return;
        _armed = null;
        foreach (var row in _list.OfType<CellRow>()) row.Paint();
    }

    // ---------------------------------------------------------------- state

    private JsonObject SafeState()
    {
        try { return _host.QuickMenuState(); } catch (Exception) { return new JsonObject(); }
    }

    private void RefreshState()
    {
        if (!IsOpen) return;
        _state = SafeState();
        _cpu.Sample();
        RefreshClock();
        // Something appeared or went (a game, music, a slider): the tab is laid out again, on the same spot.
        if (Shape() != _shape) { BuildTab(keepFocus: true); return; }
        foreach (var item in _list) item.Refresh();
    }

    private void RefreshClock()
    {
        _clock.Text = DateTime.Now.ToString("t");
        _date.Text = DateTime.Now.ToString("dddd, MMMM d");
        int? pct = Int("battery");
        bool plugged = Bool("plugged") == true;
        _battery.Text = pct is int p ? p + "%" + (Int("left") is int left && !plugged ? $"  ·  {left / 60}h {left % 60:00}m" : plugged ? "  ·  Charging" : "") : "";
        _batteryGlyph.Text = plugged ? "\uE83E" : pct is int b ? b switch { < 10 => "\uE850", < 30 => "\uE852", < 60 => "\uE855", < 90 => "\uE858", _ => "\uE83F" } : "\uE83F";
        _batteryGlyph.Foreground = new SolidColorBrush(pct is < 20 && !plugged ? Color.FromRgb(0xE5, 0x48, 0x4D) : _text);
    }

    private JsonNode? Node(string path)
    {
        JsonNode? n = _state;
        foreach (var part in path.Split('.')) { n = n is JsonObject o ? o[part] : null; if (n == null) return null; }
        return n;
    }

    private int? Int(string path) { try { return Node(path) is JsonValue v ? (v.TryGetValue<int>(out var i) ? i : v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null) : null; } catch (Exception) { return null; } }
    private bool? Bool(string path) { try { return Node(path) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null; } catch (Exception) { return null; } }
    private string? Str(string path) { try { return Node(path) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; } catch (Exception) { return null; } }

    private void Request(string op, JsonObject? args = null)
    {
        try { _host.QuickMenuRequest(op, args); } catch (Exception) { }
        // Read back soon, so the change shows.
        Dispatcher.BeginInvoke(async () => { await Task.Delay(450); RefreshState(); });
    }

    /// <summary>What decides a tab's layout; when it changes the tab is laid out again.</summary>
    private string Shape() => string.Join("|", _tab, Node("game") != null, Str("game.key"), Bool("tdp.ready"), Bool("tdp.profile"), Node("media") != null,
        Node("brightness") != null, Node("ally") != null, Node("fpscap") != null, (Node("rates") as JsonArray)?.Count, (Node("quick") as JsonArray)?.Count, Node("toggles.cpuboost") != null);

    // ---------------------------------------------------------------- the tabs

    private void BuildTab(bool keepFocus)
    {
        int keep = keepFocus ? _focus : -1;
        double x = Current?.FocusX ?? 0.5;
        Disarm();
        _list.Clear();
        _items.Children.Clear();
        _shape = Shape();
        switch (_tab)
        {
            case 0: BuildGame(); break;
            case 1: BuildPerformance(); break;
            case 2: BuildQuick(); break;
            default: BuildDevice(); break;
        }
        foreach (var item in _list) _items.Children.Add(item.View);
        PaintTabs();
        _focus = -1;
        int first = keep >= 0 && keep < _list.Count && _list[keep].CanFocus ? keep : _list.FindIndex(i => i.CanFocus);
        if (first >= 0) SetFocusIndex(first, keepFocus ? x : 0);
    }

    private void PaintTabs()
    {
        _tabStrip.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool on = i == _tab;
            var glyph = Glyph(_tabs[i].Glyph, 18);
            glyph.Foreground = new SolidColorBrush(on ? _bg : _subtle);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(glyph);
            if (on)
            {
                var name = Label(_tabs[i].Name, 15, bold: true);
                name.Foreground = new SolidColorBrush(_bg);
                name.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(name);
            }
            var pill = new Border
            {
                Child = row, CornerRadius = new CornerRadius(16), Padding = new Thickness(on ? 14 : 11, 7, on ? 16 : 11, 7), Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(on ? _accent : Colors.Transparent), Cursor = Cursors.Hand,
            };
            int index = i;
            pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (index != _tab) SwitchTab(index - _tab); };
            _tabStrip.Children.Add(pill);
        }
    }

    private void Header(string text)
    {
        var t = Label(text.ToUpperInvariant(), 13, bold: true);
        t.Foreground = new SolidColorBrush(_subtle);
        t.Margin = new Thickness(4, 14, 0, 10);
        _list.Add(new Static(t));
    }

    private void NoteText(string text)
    {
        var t = Label(text, 14);
        t.Foreground = new SolidColorBrush(_subtle);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 0, 0, 12);
        _list.Add(new Static(t));
    }

    private Cell MakeCell(string glyph, string label, Action run, Func<bool?>? on = null, Func<string?>? text = null, string? confirm = null, bool danger = false, bool tall = false)
    {
        var g = Glyph(glyph, tall ? 28 : 22);
        var l = Label(label, tall ? 16 : 15, bold: true);
        l.TextTrimming = TextTrimming.CharacterEllipsis;
        l.TextAlignment = TextAlignment.Center;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        var v = Label("", 13);
        v.Foreground = new SolidColorBrush(_subtle);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        g.HorizontalAlignment = HorizontalAlignment.Center;
        g.Margin = new Thickness(0, 0, 0, 6);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        stack.Children.Add(g);
        stack.Children.Add(l);
        stack.Children.Add(v);
        var box = new Border { Child = stack, CornerRadius = new CornerRadius(16), RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand, SnapsToDevicePixels = true };
        return new Cell { Box = box, Glyph = g, Label = l, Value = v, Run = run, On = on, Text = text, Confirm = confirm, Danger = danger, BaseLabel = label };
    }

    private void Row(double height, params Cell[] cells) { if (cells.Length > 0) _list.Add(new CellRow(this, cells, height)); }

    /// <summary>Lays cells out in rows of <paramref name="per"/>.</summary>
    private void Rows(IEnumerable<Cell> cells, int per, double height)
    {
        var all = cells.ToList();
        for (int i = 0; i < all.Count; i += per) Row(height, all.Skip(i).Take(per).ToArray());
    }

    // ---- Game

    private void BuildGame()
    {
        // The game: its cover, name, and time played.
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14), Background = new SolidColorBrush(_card) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cover = new Border { Width = 86, Height = 86, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0), Background = new SolidColorBrush(_off), ClipToBounds = true };
        string? key = Str("game.key");
        if (!string.IsNullOrEmpty(key) && _host.QuickMenuCover(key) is { } picture)
            cover.Child = new Image { Source = picture, Stretch = Stretch.UniformToFill };
        else cover.Child = new TextBlock { Text = "\uE7FC", FontFamily = IconFont, FontSize = 34, Foreground = new SolidColorBrush(_subtle), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(cover, 0);
        grid.Children.Add(cover);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Label(Str("game.name") ?? "No game running", 21, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Label("", 15);
        detail.Foreground = new SolidColorBrush(_subtle);
        detail.TextWrapping = TextWrapping.Wrap;
        info.Children.Add(name);
        info.Children.Add(detail);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        card.Child = grid;
        void Detail()
        {
            var parts = new List<string>();
            if (Str("game.cloud") is { } cloud) parts.Add(cloud);
            if (Int("game.minutes") is int m) parts.Add(m < 60 ? $"Playing for {m} min" : $"Playing for {m / 60} h {m % 60:00} min");
            if (Node("game.hours") is JsonValue h && h.TryGetValue<double>(out var hours) && hours > 0) parts.Add($"{hours:0.#} h in all");
            if (Bool("game.other") == true) parts.Add("In front now");
            detail.Text = parts.Count > 0 ? string.Join("  ·  ", parts) : Node("game") == null ? "The menu works over any app" : "";
        }
        Detail();
        _list.Add(new Static(card, Detail));

        Row(96, MakeCell("\uE768", "Resume", () => CloseMenu(), tall: true), MakeCell("\uE80F", "SquareDeck", () => Then(() => _host.QuickMenuAction("home"), 120, restore: false), tall: true));
        Row(88, MakeCell("\uE722", "Screenshot", () => Then(() => _host.QuickMenuAction("screenshot"), 350)),
                MakeCell("\uE7C8", "Record", () => Then(() => _host.QuickMenuAction("record"), 350)),
                MakeCell("\uE765", "Keyboard", () => Then(() => _host.QuickMenuAction("keyboard"), 200)));
        Row(88, MakeCell("\uE9F5", "Task Manager", () => Then(() => _host.QuickMenuAction("taskmanager"), 150, restore: false)),
                MakeCell("\uE7C4", "Show desktop", () => Then(() => _host.QuickMenuAction("desktop"), 150, restore: false)),
                MakeCell("\uE8A9", "Game Bar", () => Then(() => _host.QuickMenuAction("gamebar"), 200)));
        if (Node("game") != null && Bool("game.close") != false)
            Row(70, MakeCell("\uE711", Bool("game.other") == true ? "Close " + Trim(Str("game.name"), 22) : "Close game", () => Then(() => Request("closegame", new JsonObject { ["bar"] = false }), 150, restore: false),
                confirm: "Press A again to close", danger: true));

        Header("Power");
        Row(80, MakeCell("\uE708", "Sleep", () => Then(() => Request("power", new JsonObject { ["id"] = "sleep" }), 100, restore: false), confirm: "Again to sleep"),
                MakeCell("\uE72E", "Lock", () => Then(() => Request("power", new JsonObject { ["id"] = "lock" }), 100, restore: false), confirm: "Again to lock"),
                MakeCell("\uE777", "Restart", () => Then(() => Request("power", new JsonObject { ["id"] = "restart" }), 100, restore: false), confirm: "Again to restart", danger: true),
                MakeCell("\uE7E8", "Shut down", () => Then(() => Request("power", new JsonObject { ["id"] = "shutdown" }), 100, restore: false), confirm: "Again to shut down", danger: true));
    }

    // ---- Performance

    private void BuildPerformance()
    {
        // Live readings, as chips.
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Chips()
        {
            chips.Children.Clear();
            void Chip(string glyph, string text)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Glyph(glyph, 15);
                g.Foreground = new SolidColorBrush(_accent);
                g.Margin = new Thickness(0, 0, 7, 0);
                row.Children.Add(g);
                row.Children.Add(Label(text, 15, bold: true));
                chips.Children.Add(new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 13, 7), Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush(_card) });
            }
            if (Int("fps") is int fps) Chip("\uE7F4", fps + " FPS");
            if (_cpu.Percent is double cpu) Chip("\uE950", $"CPU {cpu:0}%");
            if (_cpu.MemoryPercent is double mem) Chip("\uE964", $"RAM {mem:0}%");
            if (Int("tdp.watts") is int w && Bool("tdp.ready") == true) Chip("\uE945", w + " W");
            if ((Int("ally.rpm") ?? Int("fan")) is int rpm) Chip("\uE9CA", rpm + " RPM");
            if (Int("battery") is int bat) Chip("\uE83F", bat + "%");
        }
        _cpu.Sample();
        Chips();
        _list.Add(new Static(chips, Chips));

        Header("Power limit (TDP)");
        if (Bool("tdp.ready") == true && Int("tdp.min") is int min && Int("tdp.max") is int max)
        {
            _list.Add(new SliderRow(this, "\uE945", "TDP", min, max, 1, () => Int("tdp.watts"), w => Request("tdp", new JsonObject { ["watts"] = w }),
                w => w + " W" + (Bool("tdp.profile") == true ? "  ·  game profile" : Bool("tdp.session") == true ? "  ·  this session" : "")));
            var presets = (Node("tdp.presets") as JsonArray)?.Select(n => { try { return n?.GetValue<int>() ?? 0; } catch (Exception) { return 0; } }).Where(n => n > 0).ToList() ?? new();
            string[] names = { "Quiet", "Balanced", "Performance", "Max" };
            Row(76, presets.Select((w, i) => MakeCell(i switch { 0 => "\uEC0A", 1 => "\uE9D9", 2 => "\uEC4A", _ => "\uE945" }, i < names.Length ? names[i] : w + " W",
                () => Request("tdp", new JsonObject { ["watts"] = w }), on: () => Int("tdp.watts") == w, text: () => w + " W")).ToArray());
            var extra = new List<Cell>();
            if (Node("game") != null && Str("game.key") is { Length: > 0 })
                extra.Add(MakeCell("\uE74E", "Keep for this game", () => Request("gameprofile", new JsonObject { ["on"] = Bool("tdp.profile") != true }), on: () => Bool("tdp.profile")));
            extra.Add(MakeCell("\uE777", "Back to usual", () => Request("tdpreset"), text: () => Bool("tdp.session") == true ? "Changed" : null));
            Row(76, extra.ToArray());
        }
        else NoteText("TDP control isn't set up yet. It's in SquareDeck's Settings, under your device.");

        if (Bool("ally.asus") == true)
        {
            Header("Operating mode");
            Row(72, AllyControl.AsusModes.Select(m => MakeCell(m.Id switch { "silent" => "\uEC0A", "turbo" => "\uE945", _ => "\uEC4A" }, m.Name,
                () => Request("asusmode", new JsonObject { ["id"] = m.Id }), on: () => Str("ally.mode") == m.Id)).ToArray());
            Header("Fans");
            Rows(AllyControl.FanCurves.Select(f => MakeCell("\uE9CA", f.Name, () => Request("fan", new JsonObject { ["id"] = f.Id }), on: () => Str("ally.fan") == f.Id)), 4, 64);
        }

        if (Node("fpscap") != null && Bool("capok") != false)
        {
            Header("Frame rate cap");
            int[] caps = { 0, 30, 40, 45, 60, 90, 120 };
            Rows(caps.Select(c => MakeCell(c == 0 ? "\uE711" : "\uE916", c == 0 ? "Off" : c.ToString(), () => Request("fpscap", new JsonObject { ["value"] = c }), on: () => (Int("fpscap") ?? 0) == c)), 4, 60);
        }

        if (Node("rates") is JsonArray { Count: > 1 } rates)
        {
            Header("Refresh rate");
            var hz = rates.Select(n => { try { return n?.GetValue<int>() ?? 0; } catch (Exception) { return 0; } }).Where(n => n > 0).ToList();
            Rows(hz.Select(r => MakeCell("\uE7F4", r + " Hz", () => Request("refresh", new JsonObject { ["hz"] = r }), on: () => Int("refresh") == r)), 4, 60);
        }

        Header("More");
        var toggles = new List<Cell> { MakeCell("\uE9D9", "Performance overlay", () => Request("action", new JsonObject { ["id"] = "perf" }), on: () => Bool("toggles.perf")) };
        if (Node("toggles.cpuboost") != null) toggles.Add(MakeCell("\uE945", "CPU boost", () => Request("action", new JsonObject { ["id"] = "cpuboost" }), on: () => Bool("toggles.cpuboost")));
        if (Node("toggles.chill") != null) toggles.Add(MakeCell("\uE9CA", "Radeon Chill", () => Request("action", new JsonObject { ["id"] = "chill" }), on: () => Bool("toggles.chill")));
        if (Node("toggles.ris") != null) toggles.Add(MakeCell("\uE890", "Sharpening", () => Request("action", new JsonObject { ["id"] = "ris" }), on: () => Bool("toggles.ris")));
        Rows(toggles, 2, 76);
    }

    // ---- Quick settings

    private static readonly Dictionary<string, string> TileGlyphs = new()
    {
        ["perf"] = "\uE9D9", ["refresh"] = "\uE7F4", ["nightlight"] = "\uE706", ["hdr"] = "\uE91B", ["dnd"] = "\uE708", ["playpause"] = "\uE768",
        ["wifi"] = "\uE701", ["bluetooth"] = "\uE702", ["keyboard"] = "\uE765", ["taskmanager"] = "\uE9F5", ["amdoverlay"] = "\uEC4A", ["screenoff"] = "\uE7E8",
        ["mic"] = "\uE720", ["desktop"] = "\uE7C4", ["amdmetrics"] = "\uE9D2", ["cpuboost"] = "\uE945", ["ris"] = "\uE890", ["chill"] = "\uE9CA",
        ["ledlevel"] = "\uE781", ["airplane"] = "\uE709", ["lights"] = "\uE781", ["padmouse"] = "\uE962",
    };

    private void BuildQuick()
    {
        _list.Add(new SliderRow(this, "\uE767", "Volume", 0, 100, 5, () => Int("volume"), v => Request("volume", new JsonObject { ["value"] = v }),
            v => Bool("muted") == true ? "Muted" : v + "%", accept: () => Request("mute")));
        if (Node("brightness") != null)
            _list.Add(new SliderRow(this, "\uE706", "Brightness", 0, 100, 5, () => Int("brightness"), v => Request("brightness", new JsonObject { ["value"] = v }), v => v + "%"));
        NoteText("A on the volume mutes or unmutes it.");

        if (Node("media") is JsonObject)
        {
            Header("Now playing");
            var title = Label("", 17, bold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var artist = Label("", 14);
            artist.Foreground = new SolidColorBrush(_subtle);
            var stack = new StackPanel { Margin = new Thickness(4, 0, 0, 10) };
            stack.Children.Add(title);
            stack.Children.Add(artist);
            void Song() { title.Text = Str("media.title") ?? ""; artist.Text = Str("media.artist") ?? ""; }
            Song();
            _list.Add(new Static(stack, Song));
            Row(66, MakeCell("\uE892", "Previous", () => Request("media", new JsonObject { ["cmd"] = "previous" })),
                    MakeCell("\uE768", "Play / pause", () => Request("media", new JsonObject { ["cmd"] = "toggle" }), text: () => Bool("media.playing") == true ? "Playing" : "Paused"),
                    MakeCell("\uE893", "Next", () => Request("media", new JsonObject { ["cmd"] = "next" })));
        }

        Header("Quick settings");
        var ids = (Node("quick") as JsonArray)?.Select(n => { try { return n?.GetValue<string>(); } catch (Exception) { return null; } }).Where(s => s != null).Select(s => s!).ToList()
                  ?? Settings.WidgetQuickTiles.Select(t => t.Id).ToList();
        var names = Settings.WidgetQuickTiles.ToDictionary(t => t.Id, t => t.Name);
        Rows(ids.Where(names.ContainsKey).Select(id => MakeCell(TileGlyphs.GetValueOrDefault(id, "\uE713"), names[id],
            () => Request("action", new JsonObject { ["id"] = id }), on: () => TileOn(id), text: () => TileText(id))), 3, 84);
        NoteText("These are the same tiles as the widget's Quick card: choose them in Settings > Controller and Game Bar widget > Widget quick settings.");
    }

    private bool? TileOn(string id) => id switch
    {
        "refresh" or "keyboard" or "taskmanager" or "screenoff" or "desktop" or "playpause" or "amdoverlay" or "amdmetrics" => null,
        "ledlevel" => Int("toggles.ledlevel") is int l ? l > 0 : null,
        _ => Bool("toggles." + id),
    };

    private string? TileText(string id) => id switch
    {
        "refresh" => Int("refresh") is int hz ? hz + " Hz" : null,
        "ledlevel" => Int("toggles.ledlevel") is int l ? (l > 0 ? l + "%" : "Off") : null,
        _ => null,
    };

    // ---- Device

    private void BuildDevice()
    {
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 0, 0, 12), Background = new SolidColorBrush(_card) };
        var stack = new StackPanel();
        var device = Label(Str("device") ?? Handheld.DisplayName, 19, bold: true);
        var line = Label("", 15);
        line.Foreground = new SolidColorBrush(_subtle);
        line.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(device);
        stack.Children.Add(line);
        card.Child = stack;
        void Line()
        {
            var parts = new List<string>();
            if (Int("battery") is int b) parts.Add(b + "% battery");
            if (Bool("plugged") == true) parts.Add("plugged in");
            else if (Int("left") is int left) parts.Add($"about {left / 60} h {left % 60:00} min left");
            if (Bool("saver") == true) parts.Add("battery saver on");
            line.Text = string.Join("  ·  ", parts);
        }
        Line();
        _list.Add(new Static(card, Line));

        if (Node("ally") != null)
        {
            Header(Handheld.DisplayName);
            Row(80, MakeCell("\uE781", "Lights", () => Request("action", new JsonObject { ["id"] = "lights" }), on: () => Bool("ally.lights")),
                    MakeCell("\uE962", "Mouse mode", () => Request("action", new JsonObject { ["id"] = "padmouse" }), on: () => Bool("ally.mouse")),
                    MakeCell("\uE7FC", "Command Center", () => Then(() => _host.QuickMenuAction("commandcenter"), 200)));
        }

        Header("Connections");
        Row(80, MakeCell("\uE701", "Wi-Fi", () => Request("action", new JsonObject { ["id"] = "wifi" }), on: () => Bool("toggles.wifi")),
                MakeCell("\uE702", "Bluetooth", () => Request("action", new JsonObject { ["id"] = "bluetooth" }), on: () => Bool("toggles.bluetooth")),
                MakeCell("\uE709", "Airplane", () => Request("action", new JsonObject { ["id"] = "airplane" }), on: () => Bool("toggles.airplane")));
        Row(80, MakeCell("\uE708", "Do not disturb", () => Request("action", new JsonObject { ["id"] = "dnd" }), on: () => Bool("toggles.dnd")),
                MakeCell("\uE706", "Night light", () => Request("action", new JsonObject { ["id"] = "nightlight" }), on: () => Bool("toggles.nightlight")),
                MakeCell("\uE91B", "HDR", () => Request("action", new JsonObject { ["id"] = "hdr" }), on: () => Bool("toggles.hdr")));

        Header("This menu");
        var s = _host.Settings;
        Row(76, MakeCell(s.QuickMenuSide == "right" ? "\uE76B" : "\uE76C", "Side", () =>
            {
                s.QuickMenuSide = s.QuickMenuSide == "right" ? "left" : "right";
                s.Save();
                CloseMenu(restore: false, () => Dispatcher.BeginInvoke(() => { _tab = 3; OpenMenu(); }));
            }, text: () => s.QuickMenuSide == "right" ? "Right" : "Left"),
            MakeCell("\uE8A7", "Use the widget", () =>
            {
                s.InGameMenu = "widget";
                s.Save();
                Then(() => _host.OpenGameBarWidget(), 300);
            }, text: () => "Game Bar"));
        NoteText("Settings > Controller and Game Bar widget > In-game menu chooses between this menu and the Game Bar widget.");
    }

    // ---------------------------------------------------------------- looks

    private void Colorize()
    {
        Switch2Settings.Refresh();
        _bg = Switch2Settings.Background;
        _card = Switch2Settings.Card;
        _line = Switch2Settings.Line;
        _text = Switch2Settings.Text;
        _subtle = Switch2Settings.Subtle;
        _off = Switch2Settings.Off;
        _accent = Switch2Settings.Blue;
        Switch2Settings.ApplyColors(_root);
        _panel.Background = new SolidColorBrush(Color.FromArgb(0xF4, _bg.R, _bg.G, _bg.B));
        _panel.BorderBrush = new SolidColorBrush(_line);
        _panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.55 };
        foreach (var t in new[] { _clock, _battery }) t.Foreground = new SolidColorBrush(_text);
        _date.Foreground = new SolidColorBrush(_subtle);
        _noteBox.Background = new SolidColorBrush(_accent);
        _note.Foreground = new SolidColorBrush(_bg);
        Resources["FgBrush"] = new SolidColorBrush(_text);
        Resources["SubtleBrush"] = new SolidColorBrush(_subtle);
    }

    private void ShowNote(string text)
    {
        if (!IsOpen || string.IsNullOrWhiteSpace(text)) return;
        _note.Text = text;
        var show = new DoubleAnimationUsingKeyFrames();
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
        _noteBox.BeginAnimation(OpacityProperty, show);
    }

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static TextBlock Glyph(string glyph, double size) => new() { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Label(string text, double size, bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        return t;
    }

    private static string Trim(string? text, int max) => text == null ? "" : text.Length > max ? text[..(max - 1)] + "…" : text;

    // ---------------------------------------------------------------- Windows plumbing

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }

    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Kept out of Alt+Tab and the taskbar.</summary>
    private void SetToolWindow()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }

    /// <summary>With "takes the controller" off, the menu floats over the game without taking the focus.</summary>
    private void SetNoActivate(bool on)
    {
        var h = new WindowInteropHelper(this).EnsureHandle();
        int ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        SetWindowLong(h, GWL_EXSTYLE, on ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE);
    }

    /// <summary>The screen the window in front is on (in WPF units), or the main screen.</summary>
    private Rect ScreenOf(IntPtr window)
    {
        try
        {
            var monitor = MonitorFromWindow(window != IntPtr.Zero ? window : new WindowInteropHelper(_host).Handle, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var dpi = VisualTreeHelper.GetDpi(_host);
                return new Rect(info.Monitor.Left / dpi.DpiScaleX, info.Monitor.Top / dpi.DpiScaleY,
                    (info.Monitor.Right - info.Monitor.Left) / dpi.DpiScaleX, (info.Monitor.Bottom - info.Monitor.Top) / dpi.DpiScaleY);
            }
        }
        catch (Exception) { }
        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    /// <summary>Processor and memory use, read cheaply once a second while the menu is open.</summary>
    private sealed class CpuMeter
    {
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }

        private long _idle, _kernel, _user;
        public double? Percent { get; private set; }
        public double? MemoryPercent { get; private set; }

        public void Sample()
        {
            try
            {
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    long total = (kernel - _kernel) + (user - _user), idleDelta = idle - _idle;
                    if (_kernel != 0 && total > 0) Percent = Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100);
                    (_idle, _kernel, _user) = (idle, kernel, user);
                }
                var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref mem)) MemoryPercent = mem.MemoryLoad;
            }
            catch (Exception) { }
        }
    }
}
