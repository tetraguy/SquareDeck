using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Task manager laid out like Handheld Task Manager: Apps (programs with a window), Processes (everything,
/// grouped by program, so 30 browser processes are one row) and Performance (CPU and memory with a minute of
/// history, battery, network, storage, system). CPU, RAM and battery stay in the top bar.
///
/// A opens the actions for a row, X ends it (asks first), Y changes the sort, LB / RB switch tabs,
/// LT / RT page, View refreshes now, Menu is the main menu, B clears the filter or closes.
/// </summary>
public sealed class TaskManagerScreen : Screen
{
    private enum Tab { Apps, Processes, Performance }

    /// <summary>Windows components that would take the system down with them.</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Memory Compression", "Secure System", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm"
    };

    private sealed class Row
    {
        public TaskGroupData Data = new();
        public Button Button = null!;
        public Image Icon = null!;
        public TextBlock Primary = null!, Secondary = null!, Cpu = null!, Memory = null!;
        public bool HasWindow => Data.Windows.Count > 0;
        public string Title => HasWindow && Data.Title.Length > 0 ? Data.Title : Data.Name;
        public bool IsCritical => Critical.Contains(Data.Name);
    }

    private readonly TaskSampler _sampler = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<double> _cpuHistory = new(), _memHistory = new();
    private TaskSample? _last;
    private bool _sampling;
    private Tab _tab = Tab.Apps;
    private string _sort = "memory"; // memory, cpu, name
    private string _filter = "";
    private string? _focusKey;
    private DateTime _lastMove = DateTime.MinValue;

    // Layout.
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };

    /// <summary>Experimental: Switch 2 style Recomp Zone, Retro Lounge, Explorer and Task Manager.</summary>
    private readonly bool _s2 = Switch2Pages.ToolsOn;
    private readonly StackPanel _s2Nav = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly TextBlock _chipCpu = Ui.Text("--", 15, "FgBrush", bold: true), _chipRam = Ui.Text("--", 15, "FgBrush", bold: true), _chipBattery = Ui.Text("", 15, "FgBrush", bold: true);
    private readonly StackPanel _chipBatteryPanel = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
    private readonly Grid _listPanel = new();
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _listScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
    private readonly TextBlock _empty = Ui.Text("", 18, "SubtleBrush");
    private readonly Grid _headers = new() { Margin = new Thickness(20, 0, 34, 6) };
    private readonly ScrollViewer _perf = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = Ui.Text("", 14, "SubtleBrush");

    // Performance cards.
    private readonly TextBlock _cpuValue = Big(), _cpuSub = Sub(), _memValue = Big(), _memSub = Sub(), _batValue = Big(), _batSub = Sub(),
        _netDown = Ui.Text("--", 22, "FgBrush", bold: true), _netUp = Ui.Text("--", 22, "FgBrush", bold: true), _sysInfo = Ui.Text("", 15, "FgBrush");
    private readonly Polyline _cpuLine = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polyline _memLine = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
    private readonly Grid _cpuChart = new() { Height = 70, Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
    private readonly Grid _memChart = new() { Height = 70, Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
    private readonly Border _batFill = new() { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _batBar = new() { Height = 8, Margin = new Thickness(0, 14, 0, 0) };
    private readonly StackPanel _drives = new();

    private static TextBlock Big() => Ui.Text("--", 34, "FgBrush", bold: true);
    private static TextBlock Sub() => Ui.Text(" ", 13, "SubtleBrush");

    public TaskManagerScreen()
    {
        // Top bar: tabs, live summary, filter, menu.
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (tab, label) in new[] { (Tab.Apps, "Apps"), (Tab.Processes, "Processes"), (Tab.Performance, "Performance") })
        {
            var t = tab;
            var b = new Button { Style = Ui.StyleOf("PillButton"), Content = label, Margin = new Thickness(0, 0, 8, 0), Focusable = false, Tag = t, FontSize = 17 };
            b.Click += (_, _) => SetTab(t);
            _tabs.Children.Add(b);
            // Switch 2 style: the tabs as sections down the left (LB / RB or a tap change them).
            var item = Switch2Pages.NavItem(label, null, false, () => SetTab(t), focusable: false);
            item.DataContext = t;
            _s2Nav.Children.Add(item);
        }
        DockPanel.SetDock(_tabs, Dock.Left);
        if (!_s2) top.Children.Add(_tabs);
        var menu = IconButton("", "Menu", () => _ = MainMenuAsync());
        DockPanel.SetDock(menu, Dock.Right);
        top.Children.Add(menu);
        var filter = IconButton("", "Filter by name", () => _ = PromptFilterAsync());
        DockPanel.SetDock(filter, Dock.Right);
        top.Children.Add(filter);
        var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 0, 16, 0), Height = 46, Margin = new Thickness(0, 0, 8, 0) };
        chip.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chipRow.Children.Add(Ui.Text("CPU", 13, "SubtleBrush"));
        _chipCpu.Margin = new Thickness(7, 0, 16, 0);
        _chipCpu.MinWidth = 38;
        chipRow.Children.Add(_chipCpu);
        chipRow.Children.Add(Ui.Text("RAM", 13, "SubtleBrush"));
        _chipRam.Margin = new Thickness(7, 0, 0, 0);
        _chipRam.MinWidth = 38;
        chipRow.Children.Add(_chipRam);
        var batIcon = Ui.Text("", 15, "SubtleBrush");
        batIcon.FontFamily = Ui.IconFont;
        batIcon.VerticalAlignment = VerticalAlignment.Center;
        _chipBatteryPanel.Children.Add(batIcon);
        _chipBattery.Margin = new Thickness(7, 0, 0, 0);
        _chipBatteryPanel.Children.Add(_chipBattery);
        chipRow.Children.Add(_chipBatteryPanel);
        chip.Child = chipRow;
        DockPanel.SetDock(chip, Dock.Right);
        top.Children.Add(chip);
        top.Children.Add(new Border()); // fill

        // Column headers (click to sort) and the rows.
        _headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        _headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        AddHeader("Name", "name", 0, HorizontalAlignment.Left);
        AddHeader("CPU", "cpu", 1, HorizontalAlignment.Right);
        AddHeader("Memory", "memory", 2, HorizontalAlignment.Right);
        _listScroll.Content = _list;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.IsHitTestVisible = false;
        _listPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _listPanel.Children.Add(_headers);
        Grid.SetRow(_listScroll, 1);
        _listPanel.Children.Add(_listScroll);
        Grid.SetRow(_empty, 1);
        _listPanel.Children.Add(_empty);

        BuildPerformance();

        _status.Margin = new Thickness(6, 8, 0, 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(top);
        Grid.SetRow(_listPanel, 1);
        body.Children.Add(_listPanel);
        Grid.SetRow(_perf, 1);
        body.Children.Add(_perf);
        Grid.SetRow(_status, 2);
        body.Children.Add(_status);

        SetPage("Task Manager", body,
            "{Select}  Actions      {Details}  End task      {Options}  Sort      LB RB  Tabs      {Back}  Back", wide: true);
        if (_s2)
        {
            (body.Parent as Panel)?.Children.Remove(body);
            ReplaceBody(Switch2Pages.Shell(_s2Nav, body));
            UseSwitch2Chrome("Task Manager", "\uE9D9");
            _headers.Margin = new Thickness(28, 0, 44, 6); // over the flat rows' wider padding
            Switch2Pages.AutoRestyle(_list, 56);
        }
        else CompactHeader();

        _timer.Tick += (_, _) => _ = SampleAsync();
    }

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Style = Ui.StyleOf("PillButton"), Width = 54, Height = 46, Margin = new Thickness(0, 0, 8, 0), ToolTip = tip, Padding = new Thickness(0), Focusable = false,
            Content = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center }
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void AddHeader(string label, string sort, int column, HorizontalAlignment align)
    {
        var b = new Button { Style = Ui.StyleOf("BareButton"), Focusable = false, HorizontalAlignment = align, Tag = sort, Content = Ui.Text(label, 14, "SubtleBrush", bold: true) };
        b.Click += (_, _) => { _sort = sort; SaveState(); Arrange(force: true); UpdateHeaders(); };
        Grid.SetColumn(b, column);
        _headers.Children.Add(b);
    }

    public override void OnResumed()
    {
        if (_s2) RefreshSwitch2Colors();
        base.OnResumed();
    }

    public override void OnShown()
    {
        var s = Host.Settings;
        _tab = Enum.TryParse<Tab>(s.TaskManagerTab, out var t) ? t : Tab.Apps;
        _sort = s.TaskManagerSort is "cpu" or "name" ? s.TaskManagerSort : "memory";
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(s.TaskManagerRefreshMs, 500, 10000));
        SetTab(_tab);
        UpdateHeaders();
        _ = SampleAsync();
        if (s.TaskManagerRefreshMs > 0) _timer.Start();
    }

    public override void OnClosed() => _timer.Stop();

    public override void FocusDefault()
    {
        if (_tab != Tab.Performance && FocusRow()) return;
        base.FocusDefault();
    }

    // ================================================================ input

    public override bool OnAction(NavAction action)
    {
        var row = FocusedRow();
        switch (action)
        {
            case NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right:
                _lastMove = DateTime.UtcNow; // hold the order still while moving through it
                return false;
            case NavAction.X:
                if (row != null) _ = EndTaskAsync(row);
                return true;
            case NavAction.Y:
                _sort = _sort switch { "memory" => "cpu", "cpu" => "name", _ => "memory" };
                SaveState();
                Arrange(force: true);
                UpdateHeaders();
                Host.ShowToast("Sorted", SortLabel(), "", "", "info");
                return true;
            case NavAction.LB:
                SetTab((Tab)(((int)_tab + 2) % 3));
                return true;
            case NavAction.RB:
                SetTab((Tab)(((int)_tab + 1) % 3));
                return true;
            case NavAction.LT:
                Page(-1);
                return true;
            case NavAction.RT:
                Page(+1);
                return true;
            case NavAction.View:
                _ = SampleAsync();
                return true;
            case NavAction.Start:
                _ = MainMenuAsync();
                return true;
            case NavAction.Back:
                if (_filter.Length > 0) { _filter = ""; Arrange(force: true); return true; }
                return false;
        }
        return false;
    }

    private Row? FocusedRow() => _rows.Values.FirstOrDefault(r => r.Button.IsKeyboardFocused);

    private void Page(int direction)
    {
        var buttons = _list.Children.OfType<Button>().ToList();
        if (buttons.Count == 0) return;
        int index = Math.Max(0, buttons.FindIndex(b => b.IsKeyboardFocused));
        int step = Math.Max(1, (int)(_listScroll.ViewportHeight / 64) - 1);
        var target = buttons[Math.Clamp(index + direction * step, 0, buttons.Count - 1)];
        target.Focus();
        target.BringIntoView();
    }

    private void SetTab(Tab tab)
    {
        _tab = tab;
        foreach (var item in _s2Nav.Children.OfType<Button>())
            item.Tag = item.DataContext is Tab it && it == tab ? "selected" : null;
        foreach (var b in _tabs.Children.OfType<Button>())
        {
            if (b.Tag is Tab t && t == tab) b.SetResourceReference(ForegroundProperty, "AccentBrush");
            else b.SetResourceReference(ForegroundProperty, "FgBrush");
        }
        bool perf = tab == Tab.Performance;
        _listPanel.Visibility = perf ? Visibility.Collapsed : Visibility.Visible;
        _perf.Visibility = perf ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
        _focusKey = null;
        Arrange(force: true);
        if (!perf) FocusRow();
        UiSound.Move();
    }

    private void SaveState()
    {
        Host.Settings.TaskManagerTab = _tab.ToString();
        Host.Settings.TaskManagerSort = _sort;
        Host.Settings.Save();
    }

    private string SortLabel() => _sort switch { "cpu" => "By CPU", "name" => "By name", _ => "By memory" };

    private void UpdateHeaders()
    {
        foreach (var b in _headers.Children.OfType<Button>())
            if (b.Content is TextBlock t)
            {
                bool on = (string)b.Tag == _sort;
                t.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentBrush" : "SubtleBrush");
                t.Text = t.Text.TrimEnd(' ', '▾') + (on ? "  ▾" : "");
            }
    }

    // ================================================================ sampling

    private async Task SampleAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var sample = await Task.Run(() => _sampler.Collect());
            if (!Host.IsTop(this) && !IsVisible) return;
            Apply(sample);
        }
        catch (Exception) { }
        finally { _sampling = false; }
    }

    private void Apply(TaskSample sample)
    {
        _last = sample;
        Push(_cpuHistory, sample.CpuTotal);
        double memUsed = sample.MemTotal > 0 ? (sample.MemTotal - sample.MemAvailable) * 100.0 / sample.MemTotal : 0;
        Push(_memHistory, memUsed);

        _chipCpu.Text = $"{sample.CpuTotal:0}%";
        _chipRam.Text = $"{memUsed:0}%";
        _chipBatteryPanel.Visibility = sample.HasBattery && sample.BatteryPercent >= 0 ? Visibility.Visible : Visibility.Collapsed;
        _chipBattery.Text = sample.BatteryPercent >= 0 ? $"{sample.BatteryPercent}%{(sample.Charging ? " ⚡" : "")}" : "";

        // Rows: update the ones that are still there, add new ones, drop the ones that ended.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in sample.Groups)
        {
            seen.Add(g.Key);
            if (!_rows.TryGetValue(g.Key, out var row)) _rows[g.Key] = row = NewRow(g);
            row.Data = g;
            row.Primary.Text = row.Title;
            string count = g.Pids.Count == 1 ? $"PID {g.Pids[0]}" : $"{g.Pids.Count} processes";
            row.Secondary.Text = row.HasWindow && g.Title.Length > 0 ? $"{g.Name}  ·  {count}" : count;
            row.Cpu.Text = g.Cpu < 0.05 ? "0%" : g.Cpu < 10 ? $"{g.Cpu:0.0}%" : $"{g.Cpu:0}%";
            row.Memory.Text = SystemStats.Bytes(g.Memory);
            if (row.Icon.Source == null && g.Path != null) LoadIcon(row, g.Path);
        }
        foreach (var gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList()) _rows.Remove(gone);

        Arrange(force: false);
        UpdatePerformance(sample, memUsed);
        UpdateStatus();
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > 60) history.RemoveAt(0);
    }

    private Row NewRow(TaskGroupData g)
    {
        var row = new Row();
        var grid = new Grid { Height = 46 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        var placeholder = Ui.Text("", 22, "SubtleBrush");
        placeholder.FontFamily = Ui.IconFont;
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(placeholder);
        row.Icon = new Image { Width = 34, Height = 34, Stretch = Stretch.Uniform, Tag = placeholder };
        RenderOptions.SetBitmapScalingMode(row.Icon, BitmapScalingMode.HighQuality);
        grid.Children.Add(row.Icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        row.Primary = Ui.Text("", 17, "FgBrush");
        row.Primary.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Secondary = Ui.Text("", 12, "SubtleBrush");
        row.Secondary.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(row.Primary);
        names.Children.Add(row.Secondary);
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        row.Cpu = Ui.Text("", 16, "FgBrush");
        row.Cpu.HorizontalAlignment = HorizontalAlignment.Right;
        row.Cpu.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(row.Cpu, 2);
        grid.Children.Add(row.Cpu);
        row.Memory = Ui.Text("", 16, "FgBrush");
        row.Memory.HorizontalAlignment = HorizontalAlignment.Right;
        row.Memory.VerticalAlignment = VerticalAlignment.Center;
        row.Memory.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(row.Memory, 3);
        grid.Children.Add(row.Memory);

        row.Button = new Button { Style = Ui.StyleOf("RowButton"), Content = grid, Padding = new Thickness(2), Margin = new Thickness(0, 2, 0, 2), Tag = g.Key };
        string key = g.Key;
        row.Button.Click += (_, _) => { if (_rows.TryGetValue(key, out var r)) _ = ActionsAsync(r); };
        row.Button.GotKeyboardFocus += (_, _) => { _focusKey = key; UpdateStatus(); };
        row.Button.MouseRightButtonUp += (_, e) => { e.Handled = true; row.Button.Focus(); if (_rows.TryGetValue(key, out var r)) _ = ActionsAsync(r); };
        return row;
    }

    private void LoadIcon(Row row, string path)
    {
        if (_icons.TryGetValue(path, out var cached))
        {
            if (cached != null) SetIcon(row, cached);
            return;
        }
        _icons[path] = null;
        _ = Task.Run(() =>
        {
            var image = ShellThumb.Get(path, 64);
            if (image != null && !image.IsFrozen && image.CanFreeze) image.Freeze();
            return image;
        }).ContinueWith(t =>
        {
            if (t.Result == null) return;
            _icons[path] = t.Result;
            foreach (var r in _rows.Values.Where(r => string.Equals(r.Data.Path, path, StringComparison.OrdinalIgnoreCase))) SetIcon(r, t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void SetIcon(Row row, ImageSource image)
    {
        row.Icon.Source = image;
        if (row.Icon.Tag is UIElement placeholder) placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>Puts the rows in order for the tab, filter and sort. Holds still for a moment after you move.</summary>
    private void Arrange(bool force)
    {
        if (_tab == Tab.Performance) return;
        IEnumerable<Row> rows = _rows.Values;
        if (_tab == Tab.Apps) rows = rows.Where(r => r.HasWindow);
        if (_filter.Length > 0)
            rows = rows.Where(r => r.Data.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || r.Data.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        rows = _sort switch
        {
            "cpu" => rows.OrderByDescending(r => r.Data.Cpu).ThenByDescending(r => r.Data.Memory),
            "name" => rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderByDescending(r => r.Data.Memory)
        };
        var wanted = rows.Select(r => r.Button).ToList();
        var current = _list.Children.OfType<Button>().ToList();
        bool sameSet = wanted.Count == current.Count && !wanted.Except(current).Any();
        bool moving = DateTime.UtcNow - _lastMove < TimeSpan.FromSeconds(3);
        if (!force && sameSet && (moving || wanted.SequenceEqual(current))) { UpdateEmpty(wanted.Count); return; }

        bool rowHadFocus = FocusedRow() != null;
        string? focus = FocusedRow()?.Data.Key ?? _focusKey;
        _list.Children.Clear();
        foreach (var b in wanted) _list.Children.Add(b);
        UpdateEmpty(wanted.Count);
        // Keep the highlight on the same program wherever it moved to.
        if (!rowHadFocus) return;
        if (focus != null && _rows.TryGetValue(focus, out var row) && wanted.Contains(row.Button)) row.Button.Focus();
        else if (wanted.Count > 0) wanted[0].Focus();
    }

    private void UpdateEmpty(int count)
    {
        _empty.Text = _last == null ? "Reading what's running…" : _filter.Length > 0 ? $"Nothing matches \"{_filter}\"." : "Nothing to show.";
        _empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool FocusRow()
    {
        var buttons = _list.Children.OfType<Button>().ToList();
        var target = _focusKey != null && _rows.TryGetValue(_focusKey, out var r) && buttons.Contains(r.Button) ? r.Button : buttons.FirstOrDefault();
        if (target == null) return false;
        FocusLater(target);
        return true;
    }

    private void UpdateStatus()
    {
        if (_last == null) { _status.Text = ""; return; }
        int apps = _rows.Values.Count(r => r.HasWindow);
        var parts = new List<string> { $"{apps} apps", $"{_last.ProcessCount} processes", SortLabel().ToLowerInvariant() };
        if (_filter.Length > 0) parts.Add($"filtered by \"{_filter}\"");
        if (!IsElevated) parts.Add("some system processes need administrator rights to end");
        _status.Text = string.Join("     ·     ", parts);
    }

    private static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }
    }

    // ================================================================ performance

    private void BuildPerformance()
    {
        var grid = new UniformGrid { Columns = 2 };
        _cpuLine.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        _memLine.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        _cpuChart.Children.Add(_cpuLine);
        _memChart.Children.Add(_memLine);
        _cpuChart.SizeChanged += (_, _) => Plot(_cpuLine, _cpuChart, _cpuHistory);
        _memChart.SizeChanged += (_, _) => Plot(_memLine, _memChart, _memHistory);

        grid.Children.Add(Card("CPU", _cpuValue, _cpuSub, _cpuChart));
        grid.Children.Add(Card("MEMORY", _memValue, _memSub, _memChart));

        var track = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.18 };
        track.SetResourceReference(Border.BackgroundProperty, "FgBrush");
        _batBar.Children.Add(track);
        _batFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _batBar.Children.Add(_batFill);
        grid.Children.Add(Card("BATTERY", _batValue, _batSub, _batBar));

        var net = new StackPanel();
        net.Children.Add(NetLine("", _netDown));
        net.Children.Add(NetLine("", _netUp));
        net.Children.Add(Sub("Download and upload, all adapters"));
        grid.Children.Add(Card("NETWORK", net));
        grid.Children.Add(Card("STORAGE", _drives));
        _sysInfo.TextWrapping = TextWrapping.Wrap;
        _sysInfo.LineHeight = 26;
        _sysInfo.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(Card("SYSTEM", _sysInfo));
        _perf.Content = grid;
    }

    private static TextBlock Sub(string text)
    {
        var t = Ui.Text(text, 13, "SubtleBrush");
        t.Margin = new Thickness(0, 8, 0, 0);
        return t;
    }

    private static StackPanel NetLine(string glyph, TextBlock value)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var icon = Ui.Text(glyph, 17, "SubtleBrush");
        icon.FontFamily = Ui.IconFont;
        icon.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(icon);
        value.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(value);
        return line;
    }

    private static Border Card(string title, params UIElement[] content)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, 13, "SubtleBrush", bold: true));
        foreach (var c in content) stack.Children.Add(c);
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 12, 12), Child = stack, MinHeight = 190 };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        card.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        return card;
    }

    private void UpdatePerformance(TaskSample s, double memUsed)
    {
        _cpuValue.Text = $"{s.CpuTotal:0}%";
        _cpuSub.Text = $"{Environment.ProcessorCount} logical processors  ·  {s.ProcessCount} processes";
        _memValue.Text = $"{memUsed:0}%";
        _memSub.Text = $"{SystemStats.Bytes(s.MemTotal - s.MemAvailable)} used of {SystemStats.Bytes(s.MemTotal)}";
        if (s.HasBattery && s.BatteryPercent >= 0)
        {
            _batValue.Text = $"{s.BatteryPercent}%";
            _batSub.Text = s.Charging ? "Charging" : s.OnAcPower ? "Plugged in" :
                s.BatterySecondsLeft > 0 ? $"About {TimeSpan.FromSeconds(s.BatterySecondsLeft):h\\:mm} left" : "On battery";
            _batFill.Width = Math.Max(8, (_batBar.ActualWidth > 0 ? _batBar.ActualWidth : 480) * s.BatteryPercent / 100.0);
            if (s.BatteryPercent <= 15 && !s.Charging) _batFill.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            else _batFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        }
        else
        {
            _batValue.Text = "None";
            _batSub.Text = "This PC runs on mains power";
            _batFill.Width = 0;
        }
        _netDown.Text = Speed(s.NetDownBytesPerSec);
        _netUp.Text = Speed(s.NetUpBytesPerSec);

        _drives.Children.Clear();
        foreach (var d in s.Drives)
        {
            var item = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            item.Children.Add(Ui.Text(d.Label, 15, "FgBrush"));
            var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 4) };
            var tr = new Border { CornerRadius = new CornerRadius(3), Opacity = 0.18 };
            tr.SetResourceReference(Border.BackgroundProperty, "FgBrush");
            bar.Children.Add(tr);
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(6, 440 * d.Fraction) };
            if (d.Fraction > 0.9) fill.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.Children.Add(fill);
            item.Children.Add(bar);
            item.Children.Add(Ui.Text(d.Sub, 12, "SubtleBrush"));
            _drives.Children.Add(item);
        }

        _sysInfo.Text = $"{Environment.MachineName}\nWindows {Environment.OSVersion.Version.Major} (build {Environment.OSVersion.Version.Build})\n" +
                        $"Up for {Uptime(s.Uptime)}\n{(IsElevated ? "Running as administrator" : "Running as a standard user")}";
        Plot(_cpuLine, _cpuChart, _cpuHistory);
        Plot(_memLine, _memChart, _memHistory);
    }

    private static string Speed(double bytesPerSecond)
    {
        double bits = bytesPerSecond * 8;
        return bits >= 1_000_000 ? $"{bits / 1_000_000:0.0} Mbps" : bits >= 1000 ? $"{bits / 1000:0} Kbps" : $"{bits:0} bps";
    }

    private static string Uptime(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m" : span.TotalHours >= 1 ? $"{span.Hours}h {span.Minutes}m" : $"{span.Minutes}m";

    /// <summary>The last minute of samples as a line, 0% at the bottom and 100% at the top.</summary>
    private static void Plot(Polyline line, FrameworkElement host, List<double> values)
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        var points = new PointCollection();
        if (w > 0 && h > 0 && values.Count > 1)
        {
            double step = w / 59;
            double x = w - (values.Count - 1) * step;
            foreach (var v in values)
            {
                points.Add(new Point(x, h - Math.Clamp(v, 0, 100) / 100 * (h - 2) - 1));
                x += step;
            }
        }
        line.Points = points;
    }

    // ================================================================ actions

    private async Task ActionsAsync(Row row)
    {
        var g = row.Data;
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        void Add(string label, string glyph, Action run) => actions.Add((label, glyph, () => { run(); return Task.CompletedTask; }));
        if (row.HasWindow)
        {
            Add("Switch to", "", () => SwitchTo(row));
            Add("Close (lets the app save first)", "", () => CloseWindows(row));
        }
        actions.Add(("End task (force)", "", () => EndTaskAsync(row)));
        actions.Add(("Set priority", "", () => PriorityAsync(row)));
        if (g.Path != null) Add("Open file location", "", () => ProcessUtil.Open("explorer.exe", $"/select,\"{g.Path}\""));
        actions.Add(("Details", "", () => DetailsAsync(row)));
        int choice = await Host.AskWithIconsAsync(row.Title, $"{g.Name}  ·  CPU {row.Cpu.Text}  ·  {row.Memory.Text}", actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private void SwitchTo(Row row)
    {
        if (!row.HasWindow) return;
        IntPtr hwnd = row.Data.Windows[0];
        if (TaskNative.IsIconic(hwnd)) TaskNative.ShowWindow(hwnd, TaskNative.SW_RESTORE);
        if (!TaskNative.SetForegroundWindow(hwnd)) Host.ShowToast("Task Manager", "Windows wouldn't switch to that app", "", "", "info");
    }

    /// <summary>Asks the app to close, the same as pressing its X button, so it can save first.</summary>
    private void CloseWindows(Row row)
    {
        foreach (var hwnd in row.Data.Windows) TaskNative.PostMessage(hwnd, TaskNative.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Host.ShowToast("Task Manager", $"Asked {row.Title} to close", "", "", "info");
        _ = RefreshSoonAsync();
    }

    private async Task EndTaskAsync(Row row)
    {
        var g = row.Data;
        if (row.IsCritical)
        {
            await Host.AskAsync($"{g.Name} can't be ended", "It's part of Windows. Ending it would crash or sign out the device.", "OK");
            return;
        }
        if (g.Pids.Contains(Environment.ProcessId))
        {
            await Host.AskAsync("That's SquareDeck", "Use Power > Exit SquareDeck to close the launcher.", "OK");
            return;
        }
        string what = g.Pids.Count == 1 ? row.Title : $"{row.Title} ({g.Pids.Count} processes)";
        if (await Host.AskAsync($"End {what}?", "It closes immediately. Anything unsaved in it is lost.", "Cancel", "End task") != 1) return;

        int failed = 0;
        bool denied = false;
        foreach (int pid in g.Pids.ToList())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception ex) { failed++; if (ex.NativeErrorCode == 5) denied = true; }
            catch (Exception) { failed++; }
        }
        string message = failed == 0 ? $"Ended {g.Name}"
            : denied && !IsElevated ? $"Windows blocked ending {g.Name}. It needs administrator rights."
            : $"Couldn't end {failed} of {g.Pids.Count} processes in {g.Name}";
        Host.ShowToast("Task Manager", message, "", "", failed == 0 ? "info" : "warning");
        _ = RefreshSoonAsync();
    }

    private async Task PriorityAsync(Row row)
    {
        ProcessPriorityClass? current = null;
        try
        {
            using var process = Process.GetProcessById(row.Data.Pids[0]);
            current = process.PriorityClass;
        }
        catch (Exception) { }
        var options = new[] { ProcessPriorityClass.High, ProcessPriorityClass.AboveNormal, ProcessPriorityClass.Normal, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Idle };
        int pick = await Host.AskWithIconsAsync("Priority for " + row.Data.Name,
            "Higher priority gives a game more CPU time; lower keeps background apps out of the way.",
            options.Select(o => (PriorityName(o), current == o ? "" : "")).ToArray());
        if (pick < 0) return;
        int failed = 0;
        foreach (int pid in row.Data.Pids.ToList())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.PriorityClass = options[pick];
            }
            catch (ArgumentException) { }
            catch (Exception) { failed++; }
        }
        Host.ShowToast("Task Manager", failed == 0 ? $"{row.Data.Name} set to {PriorityName(options[pick])}"
            : $"Couldn't change {failed} of {row.Data.Pids.Count} processes" + (IsElevated ? "" : " (needs administrator rights)"), "", "", "info");
    }

    private static string PriorityName(ProcessPriorityClass priority) => priority switch
    {
        ProcessPriorityClass.Idle => "Low",
        ProcessPriorityClass.BelowNormal => "Below normal",
        ProcessPriorityClass.AboveNormal => "Above normal",
        ProcessPriorityClass.High => "High",
        ProcessPriorityClass.RealTime => "Realtime",
        _ => "Normal"
    };

    private async Task DetailsAsync(Row row)
    {
        var g = row.Data;
        var lines = new List<string> { $"Name: {g.Name}" };
        if (g.Title.Length > 0) lines.Add($"Window: {g.Title}");
        lines.Add($"Processes: {g.Pids.Count}  (IDs {string.Join(", ", g.Pids.Take(16))}{(g.Pids.Count > 16 ? ", …" : "")})");
        lines.Add($"CPU: {row.Cpu.Text}     Memory: {row.Memory.Text}");
        lines.Add($"Location: {g.Path ?? "not available (system process)"}");
        try
        {
            using var process = Process.GetProcessById(g.Pids[0]);
            lines.Add($"Priority: {PriorityName(process.PriorityClass)}");
            lines.Add($"Started: {process.StartTime:g}");
            lines.Add($"Threads: {process.Threads.Count}");
        }
        catch (Exception) { }
        await Host.AskAsync(row.Title, string.Join("\n", lines), "OK");
    }

    private async Task RefreshSoonAsync()
    {
        await Task.Delay(500);
        await SampleAsync();
    }

    private async Task MainMenuAsync()
    {
        var s = Host.Settings;
        string speed = s.TaskManagerRefreshMs <= 0 ? "paused" : s.TaskManagerRefreshMs <= 1000 ? "fast" : s.TaskManagerRefreshMs <= 2000 ? "normal" : "slow";
        var actions = new List<(string Label, string Glyph, Func<Task> Run)>();
        void Add(string label, string glyph, Action run) => actions.Add((label, glyph, () => { run(); return Task.CompletedTask; }));
        actions.Add(("Filter by name", "", PromptFilterAsync));
        if (_filter.Length > 0) Add("Clear the filter", "", () => { _filter = ""; Arrange(force: true); });
        Add($"Sort: {SortLabel()}", "", () => OnAction(NavAction.Y));
        actions.Add(($"Update speed: {speed}", "", SpeedAsync));
        Add("Refresh now", "", () => _ = SampleAsync());
        actions.Add(("Run a new task", "", RunAsync));
        Add("Open Windows Task Manager", "", () => ProcessUtil.Open("taskmgr.exe"));
        actions.Add(("Controls", "", () => Host.AskAsync("Task Manager controls",
            "A  Actions for the row (switch to, close, end, priority, location, details)\nX  End task (asks first)\nY  Change the sort\n" +
            "LB / RB  Previous / next tab\nLT / RT  Page up / down\nView  Refresh now\nMenu  This menu\nB  Clear the filter, then back\n\n" +
            "Windows system processes (csrss, lsass, svchost, dwm…) can't be ended.", "OK")));
        int choice = await Host.AskWithIconsAsync("Task Manager", null, actions.Select(a => (a.Label, a.Glyph)).ToArray());
        if (choice >= 0) await actions[choice].Run();
    }

    private async Task SpeedAsync()
    {
        var options = new[] { (1000, "Fast (every second)"), (1500, "Normal"), (3000, "Slow (every 3 seconds)"), (0, "Paused") };
        int pick = await Host.AskAsync("Update speed", null, options.Select(o => o.Item2).ToArray());
        if (pick < 0) return;
        var s = Host.Settings;
        s.TaskManagerRefreshMs = options[pick].Item1;
        s.Save();
        _timer.Stop();
        if (s.TaskManagerRefreshMs > 0)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(s.TaskManagerRefreshMs);
            _timer.Start();
        }
    }

    private async Task RunAsync()
    {
        var text = await Host.PromptAsync("Run a new task", "A program, folder or web address", "", "Run");
        if (string.IsNullOrWhiteSpace(text)) return;
        try { ProcessUtil.Open(text.Trim()); }
        catch (Exception ex) { await Host.AskAsync("Couldn't run it", ex.Message, "OK"); }
    }

    private async Task PromptFilterAsync()
    {
        var text = await Host.PromptAsync("Filter by name", "Show only rows containing:", _filter, "Filter");
        if (text == null) return;
        _filter = text.Trim();
        if (_tab == Tab.Performance) SetTab(Tab.Processes);
        Arrange(force: true);
        FocusRow();
    }
}
