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
        ("bottom-left", "Bottom left"), ("bottom-right", "Bottom right"),
    };
    public static readonly (string Id, string Name, string About)[] Layouts =
    {
        ("row", "Bar", "Everything on one line, like a status bar"),
        ("column", "Column", "One reading per line, labels on the left"),
        ("tiles", "Tiles", "A small card for each reading"),
    };
    public static readonly (string Id, string Name, string About)[] Details =
    {
        ("minimal", "Minimal", "FPS, CPU, GPU and battery"),
        ("standard", "Standard", "Adds frame time, CPU speed, memory and TDP"),
        ("full", "Full", "Adds a frame-time graph, memory in GB, battery time left and the clock"),
    };
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
        _sensors.Tick += (_, _) => SampleSensors();
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
    internal static (int Pid, bool FullScreen) Foreground()
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
        if (Look is "retro" or "accent" || percent == null) return Value;
        return percent switch { >= 90 => new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)), >= 70 => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x4D)), _ => Value };
    }

    private Brush FpsBrush(double? fps)
    {
        if (Look is "retro" or "accent" || fps == null) return Value;
        return fps switch { < 30 => new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)), < 50 => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x4D)), _ => new SolidColorBrush(Color.FromRgb(0x6D, 0xF0, 0x8A)) };
    }

    private void Restyle()
    {
        Opacity = Math.Clamp(_s.OsdOpacity, 30, 100) / 100.0;
        double k = Scale;
        _panel.Padding = Look == "text" ? new Thickness(0) : new Thickness(12 * k, 8 * k, 12 * k, 8 * k);
        _panel.CornerRadius = new CornerRadius(Look == "retro" ? 2 : 12 * k);
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
        var (pid, full) = Foreground();
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

        var items = new List<(string Label, string Value, Brush Brush)>();
        items.Add(("FPS", fps is double v ? $"{v:0}" : "—", FpsBrush(fps)));
        if (standard) items.Add(("FRAME", frameMs is double ms ? $"{ms:0.0} ms" : "—", Value));
        items.Add(("CPU", (_cpu is double c ? $"{c:0}%" : "—") + (standard && _mhz is int mhz ? $" {mhz / 1000.0:0.0} GHz" : ""), Load(_cpu)));
        items.Add(("GPU", _gpu is double g ? $"{g:0}%" : "—", Load(_gpu)));
        if (standard) items.Add(("RAM", (_ram is double r ? $"{r:0}%" : "—") + (fullDetail && _ramUsedGb is double u && _ramTotalGb is double t ? $" {u:0.0}/{t:0} GB" : ""), Load(_ram)));
        if (standard && TdpControl.Ready) items.Add(("TDP", $"{TdpControl.EffectiveWatts} W", Value));
        if (battery.Percent is int b)
            items.Add(("BAT", $"{b}%" + (battery.Charging ? " ⚡" : "") + (fullDetail && battery.Left is TimeSpan left ? $" {(int)left.TotalHours}:{left.Minutes:00}" : ""),
                b <= 15 && !battery.Charging && Look is not ("retro" or "accent") ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)) : Value));
        if (fullDetail) items.Add(("TIME", DateTime.Now.ToString("t"), Value));

        double k = Scale;
        UIElement body = _s.OsdLayout switch
        {
            "column" => Column(items, k),
            "tiles" => Tiles(items, k),
            _ => Row(items, k),
        };
        if (fullDetail && _frameHistory.Count > 1)
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

    private UIElement Row(List<(string Label, string Value, Brush Brush)> items, double k)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) row.Children.Add(new Border { Width = 1, Height = 14 * k, Margin = new Thickness(9 * k, 0, 9 * k, 0), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
            row.Children.Add(Text(items[i].Label + " ", 11 * k, Label, false));
            row.Children.Add(Text(items[i].Value, 14 * k, items[i].Brush, true));
        }
        return row;
    }

    private UIElement Column(List<(string Label, string Value, Brush Brush)> items, double k)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Text(items[i].Label, 11 * k, Label, false);
            label.Margin = new Thickness(0, 1 * k, 14 * k, 1 * k);
            var value = Text(items[i].Value, (i == 0 ? 18 : 14) * k, items[i].Brush, true);
            Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
            grid.Children.Add(label); grid.Children.Add(value);
        }
        return grid;
    }

    private UIElement Tiles(List<(string Label, string Value, Brush Brush)> items, double k)
    {
        var wrap = new WrapPanel { MaxWidth = 300 * k };
        foreach (var item in items)
        {
            var card = new StackPanel();
            card.Children.Add(Text(item.Label, 10 * k, Label, false));
            card.Children.Add(Text(item.Value, 16 * k, item.Brush, true));
            wrap.Children.Add(new Border
            {
                Child = card, MinWidth = 64 * k, Margin = new Thickness(3 * k), Padding = new Thickness(8 * k, 4 * k, 8 * k, 5 * k), CornerRadius = new CornerRadius(Look == "retro" ? 1 : 8 * k),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
            });
        }
        return wrap;
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

    /// <summary>Frames a second over the last second; null when there's nothing to count.</summary>
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
        var cutoff = DateTime.UtcNow.AddSeconds(-1);
        while (Frames.Count > 0 && Frames.Peek().At < cutoff) Frames.Dequeue();
    }

    public static void Start()
    {
        if (Interlocked.Increment(ref _users) > 1 || ToolPath is not { } tool) return;
        // PresentMon 2.x takes "--" options; 1.x took "-". Try the newer form and fall back if it quits straight away.
        if (!Launch(tool, "--output_stdout --stop_existing_session --session_name SquareDeckOSD --no_console_stats --exclude dwm.exe"))
            Launch(tool, "-output_stdout -stop_existing_session -session_name SquareDeckOSD -no_top -exclude dwm.exe");
    }

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
            p.ErrorDataReceived += (_, _) => { };
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
