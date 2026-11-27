using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The performance overlay: a small, click-through panel that floats over games (or everything) showing frames per
/// second, processor and graphics use, the processor's speed, memory, the chip's power limit and the battery.
/// It never takes focus or clicks, so games keep the controller, keyboard and mouse.
/// </summary>
public sealed class PerfOverlay : Window
{
    public static readonly (string Id, string Name)[] Positions =
    {
        ("top-left", "Top left"), ("top-center", "Top middle"), ("top-right", "Top right"),
        ("middle-left", "Middle left"), ("middle-right", "Middle right"),
        ("bottom-left", "Bottom left"), ("bottom-center", "Bottom middle"), ("bottom-right", "Bottom right"),
    };
    public static readonly (string Id, string Name, string About)[] Layouts =
    {
        ("row", "Bar", "Everything on one line, like a status bar"),
        ("column", "Column", "One reading per line, labels on the left"),
        ("tiles", "Tiles", "A small card for each reading"),
        ("fps", "Big FPS", "Just the frame rate, large, with the other readings small underneath"),
    };
    public static readonly (string Id, string Name, string About)[] Details =
    {
        ("minimal", "Minimal", "FPS, CPU, GPU and battery"),
        ("standard", "Standard", "Adds frame time, CPU speed, memory and TDP"),
        ("full", "Full", "Adds a frame-time graph, memory in GB, battery time left and the clock"),
        ("custom", "Custom", "Pick each reading yourself"),
    };

    /// <summary>The readings, for Details > Custom (in the order they're shown).</summary>
    public static readonly (string Id, string Name)[] Readings =
    {
        ("fps", "FPS"), ("frametime", "Frame time"), ("cpu", "CPU use"), ("cpuclock", "CPU speed"), ("gpu", "GPU use"),
        ("ram", "Memory"), ("ramgb", "Memory in GB"), ("tdp", "TDP"), ("battery", "Battery"), ("batterytime", "Battery time left"),
        ("clock", "Clock"), ("graph", "Frame-time graph"),
    };

    /// <summary>How each reading is drawn: the number, a meter under it, or a ring around it.</summary>
    public static readonly (string Id, string Name, string About)[] Meters =
    {
        ("numbers", "Numbers", "Just the values"),
        ("bars", "Bars", "A small meter under each value that fills as it works harder"),
        ("rings", "Rings", "A ring gauge for each reading, like a dashboard"),
        ("blocks", "Blocks", "Five segments that light up, like a signal meter"),
    };

    public static readonly (string Id, string Name)[] Shapes = { ("rounded", "Rounded rectangle"), ("square", "Rectangle"), ("pill", "Pill") };
    public static readonly (string Id, string Name)[] Looks =
    {
        ("dark", "Dark panel"), ("glass", "Frosted glass"), ("text", "Text only"), ("neon", "Neon"), ("accent", "Highlight color"), ("retro", "Retro terminal"),
    };
    public static readonly (string Id, string Name, double Scale)[] Sizes = { ("small", "Small", 0.8), ("medium", "Medium", 1.0), ("large", "Large", 1.3) };

    private static PerfOverlay? _current;

    /// <summary>Shows, restyles or closes the overlay to match the settings.</summary>
    public static void Apply(Settings s)
    {
        if (!s.OsdEnabled)
        {
            _current?.Close();
            _current = null;
            return;
        }
        if (_current == null)
        {
            _current = new PerfOverlay(s);
            _current.Show();
        }
        _current.Restyle();
    }

    public static void CloseAll()
    {
        _current?.Close();
        _current = null;
    }

    private readonly Settings _s;
    private readonly HardwareMonitor _monitor = new();
    private readonly DispatcherTimer _sensors = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Border _panel = new();
    private readonly List<double> _frameHistory = new();
    private double? _cpu, _gpu, _ram, _ramUsedGb, _ramTotalGb;
    private int? _mhz;
    private bool _sampling;

    private PerfOverlay(Settings s)
    {
        _s = s;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "SquareDeck performance overlay";
        Content = _panel;
        SourceInitialized += (_, _) => MakeClickThrough();
        _sensors.Tick += (_, _) => { SampleSensors(); FrameCounter.Retry(); };
        _fast.Tick += (_, _) => Refresh();
        SizeChanged += (_, _) => Place();
        Closed += (_, _) =>
        {
            _sensors.Stop();
            _fast.Stop();
            Task.Run(FrameCounter.Stop);
            _monitor.Dispose();
        };
        _sensors.Start();
        _fast.Start();
        Task.Run(FrameCounter.Start); // starting PresentMon can take a moment: keep it off the interface thread
    }

    // ---------------------------------------------------------------- the window

    private const int GwlExStyle = -20, Transparent = 0x20, Layered = 0x80000, ToolWindow = 0x80, NoActivate = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public int Flags; }

    /// <summary>Clicks and taps go straight through to whatever is underneath; it never becomes the active window.</summary>
    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | Transparent | Layered | ToolWindow | NoActivate);
    }

    /// <summary>The process of the window in front, and whether it fills its whole screen (a game, usually).</summary>
    internal static (int Pid, bool FullScreen) ForegroundApp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return (0, false);
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!GetWindowRect(hwnd, out var r)) return ((int)pid, false);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return ((int)pid, false);
            var screen = info.Monitor;
            bool full = r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
            return ((int)pid, full);
        }
        catch (Exception) { return (0, false); }
    }

    private void Place()
    {
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight, m = 14;
        double w = ActualWidth, h = ActualHeight;
        (Left, Top) = _s.OsdPosition switch
        {
            "top-left" => (m, m),
            "top-center" => ((sw - w) / 2, m),
            "middle-left" => (m, (sh - h) / 2),
            "middle-right" => (sw - w - m, (sh - h) / 2),
            "bottom-center" => ((sw - w) / 2, sh - h - m),
            "bottom-left" => (m, sh - h - m),
            "bottom-right" => (sw - w - m, sh - h - m),
            _ => (sw - w - m, m),
        };
    }

    // ---------------------------------------------------------------- readings

    /// <summary>Processor, graphics and memory, once a second, read off the interface thread.</summary>
    private async void SampleSensors()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var (cpu, mhz, gpu) = await Task.Run(_monitor.Sample);
            if (cpu != null) _cpu = cpu;
            if (mhz != null) _mhz = mhz;
            if (gpu != null) _gpu = gpu;
            if (Hardware.MemoryStatus() is var (total, free) && total > 0)
            {
                _ram = 100.0 * (total - free) / total;
                _ramUsedGb = (total - free) / 1073741824.0;
                _ramTotalGb = total / 1073741824.0;
            }
        }
        catch (Exception) { }
        finally { _sampling = false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, Flag, Percent, Saver; public uint LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private static (int? Percent, bool Charging, TimeSpan? Left) BatteryNow()
    {
        if (!GetSystemPowerStatus(out var p) || p.Percent > 100 || (p.Flag & 128) != 0) return (null, false, null);
        TimeSpan? left = p.AcLine == 1 || p.LifeTime is 0 or uint.MaxValue ? null : TimeSpan.FromSeconds(p.LifeTime);
        return (p.Percent, p.AcLine == 1, left);
    }

    // ---------------------------------------------------------------- drawing

    private string Look => Looks.Any(l => l.Id == _s.OsdStyle) ? _s.OsdStyle : "dark";
    private double Scale => Sizes.FirstOrDefault(z => z.Id == _s.OsdSize).Scale is > 0 and var k ? k : 1;

    private Brush Label => Look switch
    {
        "retro" => new SolidColorBrush(Color.FromRgb(0x3C, 0xB8, 0x4E)),
        "accent" => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        _ => new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
    };

    private Brush Value => Look switch
    {
        "retro" => new SolidColorBrush(Color.FromRgb(0x7C, 0xFF, 0x8C)),
        "neon" => new SolidColorBrush(AccentColor),
        _ => Brushes.White,
    };

    private static Color AccentColor => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    /// <summary>Green, amber, red: how hard something is working.</summary>
    private Brush Load(double? percent)
    {
        if (Look is "retro" or "accent" || percent == null || !_s.OsdColors) return Value;
        return percent switch { >= 90 => new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)), >= 70 => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x4D)), _ => Value };
    }

    private Brush FpsBrush(double? fps)
    {
        if (Look is "retro" or "accent" || fps == null || !_s.OsdColors) return Value;
        return fps switch { < 30 => new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)), < 50 => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x4D)), _ => new SolidColorBrush(Color.FromRgb(0x6D, 0xF0, 0x8A)) };
    }

    private void Restyle()
    {
        Opacity = Math.Clamp(_s.OsdOpacity, 30, 100) / 100.0;
        double k = Scale;
        _panel.Padding = Look == "text" ? new Thickness(0) : new Thickness(12 * k, 8 * k, 12 * k, 8 * k);
        _panel.CornerRadius = new CornerRadius(Look == "retro" || _s.OsdShape == "square" ? 2 : _s.OsdShape == "pill" ? 40 * k : 12 * k);
        if (_s.OsdShape == "pill" && Look != "text") _panel.Padding = new Thickness(20 * k, 8 * k, 20 * k, 8 * k);
        _panel.BorderThickness = new Thickness(Look is "neon" or "retro" ? 1.5 : Look == "glass" ? 1 : 0);
        _panel.Effect = null;
        switch (Look)
        {
            case "glass":
                _panel.Background = new LinearGradientBrush(Color.FromArgb(0x70, 0x60, 0x70, 0x88), Color.FromArgb(0x50, 0x20, 0x28, 0x38), 90);
                _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                break;
            case "text":
                _panel.Background = Brushes.Transparent;
                _panel.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 1, Color = Colors.Black };
                break;
            case "neon":
                _panel.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x06, 0x04, 0x10));
                _panel.BorderBrush = new SolidColorBrush(AccentColor);
                _panel.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9, Color = AccentColor };
                break;
            case "accent":
                var a = AccentColor;
                _panel.Background = new SolidColorBrush(Color.FromArgb(0xD8, a.R, a.G, a.B));
                break;
            case "retro":
                _panel.Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x02, 0x0A, 0x04));
                _panel.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0xB8, 0x4E));
                break;
            default:
                _panel.Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x0A, 0x0C, 0x10));
                break;
        }
        Refresh();
        Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded); // a new position with the same size
    }

    private void Refresh()
    {
        // When to show: always, or only while a full-screen program other than SquareDeck is in front.
        var (pid, full) = ForegroundApp();
        bool ours = pid == Environment.ProcessId;
        bool show = _s.OsdWhen == "always" || (full && !ours && pid != 0);
        FrameCounter.TargetPid = ours ? 0 : pid;
        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) Show();

        double? fps = FrameCounter.Fps, frameMs = FrameCounter.FrameMs;
        if (fps is double f)
        {
            _frameHistory.Add(frameMs ?? 1000 / Math.Max(f, 1));
            if (_frameHistory.Count > 40) _frameHistory.RemoveAt(0);
        }
        var battery = BatteryNow();
        string detail = _s.OsdDetail;
        bool standard = detail is "standard" or "full", fullDetail = detail == "full";
        var custom = new HashSet<string>((_s.OsdItems ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (detail == "custom" && custom.Count == 0) custom = new() { "fps", "cpu", "gpu", "battery" };
        bool Want(string id) => detail == "custom" ? custom.Contains(id) : id switch
        {
            "fps" or "cpu" or "gpu" or "battery" => true,
            "frametime" or "cpuclock" or "ram" or "tdp" => standard,
            _ => fullDetail,
        };

        var items = new List<Reading>();
        if (Want("fps")) items.Add(new("FPS", fps is double v ? $"{v:0}" : FrameCounter.Blank, FpsBrush(fps), fps is double fv ? Math.Min(100, fv / 60 * 100) : null));
        if (Want("frametime")) items.Add(new("FRAME", frameMs is double ms ? $"{ms:0.0} ms" : "\u2014", Value, null));
        if (Want("cpu") || Want("cpuclock"))
            items.Add(new("CPU", (Want("cpu") ? (_cpu is double c ? $"{c:0}%" : "\u2014") : "") + (Want("cpuclock") && _mhz is int mhz ? $" {mhz / 1000.0:0.0} GHz" : ""), Load(_cpu), _cpu));
        if (Want("gpu")) items.Add(new("GPU", _gpu is double g ? $"{g:0}%" : "\u2014", Load(_gpu), _gpu));
        if (Want("ram") || Want("ramgb"))
            items.Add(new("RAM", (Want("ram") ? (_ram is double r ? $"{r:0}%" : "\u2014") : "") + (Want("ramgb") && _ramUsedGb is double u && _ramTotalGb is double t ? $" {u:0.0}/{t:0} GB" : ""), Load(_ram), _ram));
        if (Want("tdp") && TdpControl.Ready)
        {
            int watts = TdpControl.EffectiveWatts;
            double? share = TdpControl.Chip is { } chip && chip.Max > 0 ? 100.0 * watts / chip.Max : null;
            items.Add(new("TDP", $"{watts} W", Value, share));
        }
        if ((Want("battery") || Want("batterytime")) && battery.Percent is int b)
            items.Add(new("BAT", (Want("battery") ? $"{b}%" + (battery.Charging ? " \u26A1" : "") : "") + (Want("batterytime") && battery.Left is TimeSpan left ? $" {(int)left.TotalHours}:{left.Minutes:00}" : ""),
                b <= 15 && !battery.Charging && _s.OsdColors && Look is not ("retro" or "accent") ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)) : Value, b));
        if (Want("clock")) items.Add(new("TIME", DateTime.Now.ToString("t"), Value, null));
        foreach (var item in items) item.Text = item.Text.Trim();

        double k = Scale;
        UIElement body = _s.OsdLayout switch
        {
            "column" => Column(items, k),
            "tiles" => Tiles(items, k),
            "fps" => BigFps(items, k),
            _ => Row(items, k),
        };
        if (Want("graph") && _frameHistory.Count > 1)
        {
            var stack = new StackPanel();
            stack.Children.Add(body);
            stack.Children.Add(Graph(k, _s.OsdLayout == "row" ? Math.Max(160, items.Count * 70) * k : 170 * k));
            body = stack;
        }
        _panel.Child = body;
        TextOptions.SetTextFormattingMode(_panel, TextFormattingMode.Display);
        WindowActivator.KeepOnTop(this);
    }

    private FontFamily Font => Look == "retro" ? new FontFamily("Consolas") : new FontFamily("Segoe UI");

    private TextBlock Text(string text, double size, Brush brush, bool bold) =>
        new() { Text = text, FontSize = size, Foreground = brush, FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, FontFamily = Font, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>One reading: its label, the text shown, its color, and how full its meter is (0 to 100, or none).</summary>
    private sealed class Reading
    {
        public Reading(string label, string text, Brush brush, double? percent) { Label = label; Text = text; Brush = brush; Percent = percent; }
        public string Label;
        public string Text;
        public Brush Brush;
        public double? Percent;
    }

    private string MeterStyle => Meters.Any(m => m.Id == _s.OsdMeters) ? _s.OsdMeters : "numbers";

    private Brush Track => new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

    /// <summary>The meter under a value: a thin bar, or five blocks.</summary>
    private UIElement? Meter(Reading item, double width, double k)
    {
        if (item.Percent is not double p) return null;
        p = Math.Clamp(p, 0, 100);
        if (MeterStyle == "blocks")
        {
            var blocks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3 * k, 0, 0) };
            int lit = (int)Math.Ceiling(p / 20);
            double each = (width - 4 * 2 * k) / 5;
            for (int i = 0; i < 5; i++)
                blocks.Children.Add(new Border { Width = Math.Max(3, each), Height = 4 * k, Margin = new Thickness(0, 0, i < 4 ? 2 * k : 0, 0), CornerRadius = new CornerRadius(1), Background = i < lit ? item.Brush : Track });
            return blocks;
        }
        var track = new Grid { Width = width, Height = 3.5 * k, Margin = new Thickness(0, 3 * k, 0, 0) };
        track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2 * k) });
        track.Children.Add(new Border { Background = item.Brush, CornerRadius = new CornerRadius(2 * k), Width = width * p / 100, HorizontalAlignment = HorizontalAlignment.Left });
        return track;
    }

    /// <summary>A ring gauge with the value in the middle.</summary>
    private UIElement Ring(Reading item, double size, double k, double fontSize)
    {
        var grid = new Grid { Width = size, Height = size };
        double thick = Math.Max(2.5, size * 0.1);
        grid.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = thick });
        if (item.Percent is double p && p > 0)
        {
            double r = (size - thick) / 2, a = Math.Clamp(p, 0, 99.9) / 100 * 2 * Math.PI;
            var c = new Point(size / 2, size / 2);
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            grid.Children.Add(new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { fig }), Stroke = item.Brush, StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        var value = Text(item.Text.Split(' ')[0], fontSize, item.Brush, true);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(value);
        return grid;
    }

    /// <summary>A value with its meter (or ring) in the chosen style.</summary>
    private UIElement Value_(Reading item, double size, double k, double meterWidth)
    {
        if (MeterStyle == "rings" && item.Percent != null)
            return Ring(item, size * 2.6, k, size * 0.78);
        var value = Text(item.Text, size, item.Brush, true);
        if (MeterStyle == "numbers" || Meter(item, meterWidth, k) is not { } meter) return value;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(value);
        stack.Children.Add(meter);
        return stack;
    }

    private UIElement Row(List<Reading> items, double k)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) row.Children.Add(new Border { Width = 1, Height = 14 * k, Margin = new Thickness(9 * k, 0, 9 * k, 0), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
            row.Children.Add(Text(items[i].Label + " ", 11 * k, Label, false));
            row.Children.Add(Value_(items[i], 14 * k, k, 34 * k));
        }
        return row;
    }

    private UIElement Column(List<Reading> items, double k)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Text(items[i].Label, 11 * k, Label, false);
            label.Margin = new Thickness(0, 2 * k, 14 * k, 2 * k);
            var value = Value_(items[i], (i == 0 ? 18 : 14) * k, k, 70 * k);
            if (value is FrameworkElement fe) fe.Margin = new Thickness(0, 2 * k, 0, 2 * k);
            Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
            grid.Children.Add(label); grid.Children.Add(value);
        }
        return grid;
    }

    private UIElement Tiles(List<Reading> items, double k)
    {
        var wrap = new WrapPanel { MaxWidth = 300 * k };
        foreach (var item in items)
        {
            var card = new StackPanel();
            card.Children.Add(Text(item.Label, 10 * k, Label, false));
            card.Children.Add(Value_(item, 16 * k, k, 48 * k));
            wrap.Children.Add(new Border
            {
                Child = card, MinWidth = 64 * k, Margin = new Thickness(3 * k), Padding = new Thickness(8 * k, 4 * k, 8 * k, 5 * k),
                CornerRadius = new CornerRadius(Look == "retro" || _s.OsdShape == "square" ? 1 : 8 * k),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
            });
        }
        return wrap;
    }

    /// <summary>The frame rate large, the other readings in a small line underneath.</summary>
    private UIElement BigFps(List<Reading> items, double k)
    {
        var stack = new StackPanel();
        var fps = items.FirstOrDefault(i => i.Label == "FPS");
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (fps != null && MeterStyle == "rings" && fps.Percent != null) top.Children.Add(Ring(fps, 64 * k, k, 24 * k));
        else
        {
            top.Children.Add(Text(fps?.Text ?? FrameCounter.Blank, 34 * k, fps?.Brush ?? Value, true));
            var unit = Text(" FPS", 12 * k, Label, false);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(0, 0, 0, 7 * k);
            top.Children.Add(unit);
        }
        stack.Children.Add(top);
        if (fps != null && MeterStyle is "bars" or "blocks" && Meter(fps, 90 * k, k) is FrameworkElement m) { m.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(m); }
        var rest = items.Where(i => i != fps).ToList();
        if (rest.Count > 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4 * k, 0, 0) };
            foreach (var item in rest)
            {
                line.Children.Add(Text(item.Label + " ", 9 * k, Label, false));
                var v = Text(item.Text, 11 * k, item.Brush, true);
                v.Margin = new Thickness(0, 0, 9 * k, 0);
                line.Children.Add(v);
            }
            stack.Children.Add(line);
        }
        return stack;
    }

    /// <summary>The last twenty seconds of frame times: flat is smooth, spikes are stutters.</summary>
    private UIElement Graph(double k, double width)
    {
        double height = 30 * k, top = Math.Max(34, _frameHistory.Max() * 1.15);
        var line = new Polyline { Stroke = Look == "retro" ? Value : new SolidColorBrush(AccentColor), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        for (int i = 0; i < _frameHistory.Count; i++)
            line.Points.Add(new Point(width * i / 39.0, height - height * Math.Min(1, _frameHistory[i] / top)));
        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 6 * k, 0, 0), ClipToBounds = true };
        // A guide at 16.7 ms (60 FPS).
        double guide = height - height * (16.7 / top);
        canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = guide, Y2 = guide, Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), StrokeDashArray = new DoubleCollection { 3, 3 } });
        canvas.Children.Add(line);
        return canvas;
    }
}

/// <summary>
/// Frames per second for the program in front, from Intel's PresentMon (an open-source tool that reads the same
/// Windows events as the Xbox Game Bar's counter). SquareDeck uses it when it's in Tools\PresentMon; without it
/// the overlay shows "—" for FPS and everything else still works. PresentMon needs administrator rights, or the
/// account in the "Performance Log Users" group.
/// </summary>
public static class FrameCounter
{
    private static Process? _process;
    private static readonly object Gate = new();
    private static readonly Queue<(DateTime At, double Ms)> Frames = new();
    private static int _pidColumn = -1, _msColumn = -1;
    private static int _users;

    /// <summary>Only this process's frames count (0: none).</summary>
    public static int TargetPid { get; set; }

    public static string? ToolPath
    {
        get
        {
            try
            {
                string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon");
                if (!Directory.Exists(dir)) return null;
                return Directory.EnumerateFiles(dir, "PresentMon*.exe", SearchOption.AllDirectories).OrderByDescending(f => f).FirstOrDefault();
            }
            catch (Exception) { return null; }
        }
    }

    public static bool Available => ToolPath != null;

    /// <summary>Why there's no frame rate, for Settings: null while it's counting (or hasn't been asked to).</summary>
    public static string? Problem { get; private set; }

    /// <summary>What the overlay shows for FPS when there's no number: a dash, or "n/a" when PresentMon isn't permitted.</summary>
    public static string Blank => Problem == null ? "\u2014" : _denied ? "n/a" : "\u2014";

    private static bool _denied;
    private static DateTime _lastTry;

    /// <summary>
    /// PresentMon may read the frame rate: SquareDeck runs as administrator, or this account is in Windows'
    /// "Performance Log Users" group (S-1-5-32-559; it takes effect after signing out and back in).
    /// </summary>
    public static bool Permitted
    {
        get
        {
            try
            {
                using var me = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(me);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)
                    || p.IsInRole(new System.Security.Principal.SecurityIdentifier("S-1-5-32-559"));
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Adds this account to "Performance Log Users" (Windows asks for administrator permission once). Null when it
    /// worked; the frame rate shows after signing out and back in (or restarting).
    /// </summary>
    public static async Task<string?> AllowAsync()
    {
        string user = Environment.UserDomainName + "\\" + Environment.UserName;
        string output = await Maintenance.RunElevatedAsync(
            $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"try {{ Add-LocalGroupMember -SID 'S-1-5-32-559' -Member '{user.Replace("'", "''")}' -ErrorAction Stop; 'OK' }} catch {{ if ($_.FullyQualifiedErrorId -like '*MemberExists*') {{ 'OK' }} else {{ $_.Exception.Message }} }}\"");
        return output.Contains("OK") ? null : output.Length > 0 ? output : "Windows didn't say why.";
    }

    /// <summary>Starts PresentMon again if it isn't running (it quit, or wasn't allowed at first), at most every 15 seconds.</summary>
    public static void Retry()
    {
        if (_users <= 0 || ToolPath is not { } tool) return;
        if (_process is { HasExited: false }) return;
        if (DateTime.UtcNow - _lastTry < TimeSpan.FromSeconds(15)) return;
        _ = Task.Run(() => LaunchAny(tool));
    }

    /// <summary>Frames a second over the last two seconds; null when there's nothing to count.</summary>
    public static double? Fps
    {
        get
        {
            lock (Gate)
            {
                Trim();
                if (Frames.Count < 2) return null;
                double totalMs = Frames.Sum(f => f.Ms);
                return totalMs > 0 ? Frames.Count * 1000.0 / totalMs : null;
            }
        }
    }

    /// <summary>The average time between frames over the last second, in milliseconds.</summary>
    public static double? FrameMs
    {
        get
        {
            lock (Gate)
            {
                Trim();
                return Frames.Count < 2 ? null : Frames.Average(f => f.Ms);
            }
        }
    }

    private static void Trim()
    {
        // Two seconds: piped output arrives in bursts, so a one-second window could come up empty at low frame rates.
        var cutoff = DateTime.UtcNow.AddSeconds(-2);
        while (Frames.Count > 0 && Frames.Peek().At < cutoff) Frames.Dequeue();
    }

    public static void Start()
    {
        if (Interlocked.Increment(ref _users) > 1) return;
        if (ToolPath is not { } tool) { Problem = "PresentMon isn't in SquareDeck's Tools folder"; return; }
        LaunchAny(tool);
    }

    private static readonly object LaunchGate = new();

    private static void LaunchAny(string tool)
    {
        lock (LaunchGate)
        {
            if (_process is { HasExited: false }) return;
            _lastTry = DateTime.UtcNow;
            _stderr = "";
            // PresentMon 2.x takes "--" options; 1.x took "-". Try the newer form and fall back if it quits straight away.
            bool ok = Launch(tool, "--output_stdout --stop_existing_session --session_name SquareDeckOSD --no_console_stats --exclude dwm.exe")
                   || Launch(tool, "-output_stdout -stop_existing_session -session_name SquareDeckOSD -no_top -exclude dwm.exe");
            _denied = !ok && (!Permitted || _stderr.Contains("access", StringComparison.OrdinalIgnoreCase) || _stderr.Contains("privilege", StringComparison.OrdinalIgnoreCase)
                              || _stderr.Contains("elevat", StringComparison.OrdinalIgnoreCase));
            Problem = ok ? null : _denied
                ? "Windows doesn't let PresentMon read frame rates for this account"
                : "PresentMon stopped straight away" + (_stderr.Length > 0 ? ": " + _stderr.Trim().Split('\n')[0] : "");
        }
    }

    private static string _stderr = "";

    private static bool Launch(string tool, string args)
    {
        try
        {
            _pidColumn = _msColumn = -1;
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(tool, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(tool)!
                },
                EnableRaisingEvents = true
            };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Parse(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line && _stderr.Length < 2000) _stderr += line + "\n"; };
            if (!p.Start()) return false;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (p.WaitForExit(1500)) return false; // it quit: wrong options or no permission
            _process = p;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void Parse(string line)
    {
        var cells = line.Split(',');
        if (_pidColumn < 0)
        {
            // The first line names the columns.
            _pidColumn = Array.FindIndex(cells, c => c.Trim() == "ProcessID");
            _msColumn = Array.FindIndex(cells, c => c.Trim() == "MsBetweenPresents");
            if (_msColumn < 0) _msColumn = Array.FindIndex(cells, c => c.Trim() == "FrameTime");
            if (_msColumn < 0) _msColumn = Array.FindIndex(cells, c => c.Trim() == "MsBetweenDisplayChange");
            if (_pidColumn < 0 || _msColumn < 0) _pidColumn = -1;
            return;
        }
        if (cells.Length <= Math.Max(_pidColumn, _msColumn)) return;
        if (!int.TryParse(cells[_pidColumn], out int pid) || pid != TargetPid) return;
        if (!double.TryParse(cells[_msColumn], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms) || ms <= 0 || ms > 1000) return;
        lock (Gate) Frames.Enqueue((DateTime.UtcNow, ms));
    }

    public static void Stop()
    {
        if (Interlocked.Decrement(ref _users) > 0) return;
        _users = 0;
        try { if (_process is { HasExited: false }) _process.Kill(); }
        catch (Exception) { }
        _process = null;
        lock (Gate) Frames.Clear();
    }
}
