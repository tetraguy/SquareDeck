using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Power and performance: three live cards for the processor, memory and graphics. Each shows what the
/// part is, how busy it is right now (a big number and a small graph of the last minute), and its key facts:
/// the processor's speed now and at most, the memory's capacity, type and speed, the graphics chip and its memory.
/// Updates once a second while it's on screen.
/// </summary>
public sealed class HardwarePanel : Border
{
    private const int History = 60;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private HardwareMonitor? _monitor;
    private readonly Card _cpu, _memory, _gpu, _disk, _net;
    private readonly Card? _npu; // only when the device has one
    private readonly NetworkMonitor _network = new();
    private double _netPeak = 1_000_000; // the graph's top: the busiest second seen, at least 1 Mb/s
    private readonly string _drive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    public HardwarePanel()
    {
        var info = Hardware.Info;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };

        _cpu = new Card("", "Processor", Short(info.CpuName, "Processor"));
        _cpu.SetFacts($"{info.Cores} cores  ·  {info.Threads} threads",
                      info.CpuMaxMhz > 0 ? $"Up to {Ghz(info.CpuMaxMhz)}" : "");

        string type = info.MemoryType.Length > 0 ? info.MemoryType + " " : "";
        _memory = new Card("", "Memory", $"{Gb(info.MemoryBytes)} {type}".Trim());
        _memory.SetFacts(info.MemorySpeed > 0 ? $"{info.MemorySpeed:N0} MT/s" + (info.MemoryModules > 1 ? $"  ·  {info.MemoryModules} modules" : "") : "",
                         "");

        var gpu = info.Gpus.OrderByDescending(g => g.Memory).FirstOrDefault();
        _gpu = new Card("", "Graphics", gpu.Name != null ? Short(gpu.Name, "Graphics") : "Graphics");
        _gpu.SetFacts(gpu.Memory > 0 ? $"{Gb(gpu.Memory)} dedicated memory" : "Shares the system memory",
                      info.Gpus.Count > 1 ? $"+ {string.Join(", ", info.Gpus.Where(g => g.Name != gpu.Name).Select(g => Short(g.Name, "")))}" : "");

        // The system drive (C:): its name, size and free space, and how busy it is.
        _disk = new Card("\uEDA2", "Storage", $"Local disk ({_drive.TrimEnd('\\')})");
        ShowDriveSpace();
        // The network in use: Wi-Fi or Ethernet, its name and speed, and the traffic right now.
        _net = new Card("\uE701", "Network", "Not connected");

        grid.Children.Add(_cpu);
        grid.Children.Add(_memory);
        grid.Children.Add(_gpu);
        // The NPU (AI accelerator), only if there is one.
        if (info.Npus.FirstOrDefault() is { Name: not null } npu)
        {
            _npu = new Card("\uE9D9", "NPU", Short(npu.Name, "Neural processor"));
            _npu.SetFacts(npu.Maker.Length > 0 ? $"{npu.Maker}  ·  driver {npu.Driver}" : npu.Driver.Length > 0 ? "Driver " + npu.Driver : "Neural processor",
                          info.Npus.Count > 1 ? "+ " + string.Join(", ", info.Npus.Skip(1).Select(n => Short(n.Name, ""))) : "For AI features in Windows and apps");
            grid.Children.Add(_npu);
        }
        grid.Children.Add(_disk);
        grid.Children.Add(_net);
        Child = grid;
        Margin = new Thickness(0, 4, 0, 10);

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            _monitor ??= new HardwareMonitor();
            Tick();
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _monitor?.Dispose();
            _monitor = null;
        };
    }

    private async void Tick()
    {
        var monitor = _monitor;
        if (monitor == null) return;
        var (cpu, mhz, gpu) = await Task.Run(monitor.Sample);
        var memory = Hardware.MemoryStatus();

        if (cpu is double c) _cpu.Push(c, mhz is int m ? $"{Ghz(m)} now" : null);
        else _cpu.Waiting();
        if (memory is { } mem && mem.Total > 0)
        {
            ulong used = mem.Total - mem.Available;
            _memory.Push(100.0 * used / mem.Total, $"{Gb(used, 1)} of {Gb(mem.Total, 1)} in use");
        }
        if (gpu is double g) _gpu.Push(g, null);
        else _gpu.Waiting();

        if (_npu != null)
        {
            if (monitor.NpuBusy is double npuLoad) _npu.Push(npuLoad, null);
            else _npu.Push(0, "Use isn't reported on this device", big: "Ready");
        }
        if (monitor.DiskBusy is double busy)
            _disk.Push(busy, $"Read {Rate(monitor.DiskRead ?? 0, bytes: true)}  ·  Write {Rate(monitor.DiskWrite ?? 0, bytes: true)}");
        else _disk.Waiting();
        if (DateTime.Now.Second % 15 == 0) ShowDriveSpace();

        var (down, up) = await Task.Run(_network.Sample);
        ShowNetwork();
        if (down is double d && up is double u)
        {
            _netPeak = Math.Max(_netPeak, Math.Max(d, u));
            _net.Push(100 * d / _netPeak, $"↓ {Rate(d)}  ·  ↑ {Rate(u)}", big: Rate(d));
        }
        else _net.Waiting();
    }

    private void ShowDriveSpace()
    {
        try
        {
            var drive = new System.IO.DriveInfo(_drive);
            string label = drive.VolumeLabel.Length > 0 ? drive.VolumeLabel : "Local disk";
            _disk.SetName($"{label} ({_drive.TrimEnd('\\')})");
            long used = drive.TotalSize - drive.AvailableFreeSpace;
            _disk.SetFacts($"{Gb((ulong)drive.AvailableFreeSpace)} free of {Gb((ulong)drive.TotalSize)}  ·  {drive.DriveFormat}",
                           $"{100.0 * used / drive.TotalSize:0}% full");
        }
        catch (Exception) { }
    }

    private void ShowNetwork()
    {
        if (_network.Name.Length == 0)
        {
            _net.SetName("Not connected");
            _net.SetFacts("No network with internet access", "");
            return;
        }
        string title = _network.Kind == "Wi-Fi" && _network.Network.Length > 0 ? $"Wi-Fi: {_network.Network}" : _network.Kind;
        _net.SetName(title);
        string signal = _network.Bars is int bars ? $"  ·  signal {bars}/5" : "";
        string link = _network.LinkBitsPerSecond > 0 ? $"Link {Rate(_network.LinkBitsPerSecond)}" : "";
        _net.SetFacts(link + signal + (_network.Address.Length > 0 ? $"  ·  {_network.Address}" : ""), Short(_network.Name, ""));
    }

    /// <summary>"12.4 Mb/s", or "85 MB/s" for bytes.</summary>
    private static string Rate(double perSecond, bool bytes = false)
    {
        string unit = bytes ? "B/s" : "b/s";
        if (perSecond >= 1e9) return $"{perSecond / 1e9:0.0} G{unit}";
        if (perSecond >= 1e6) return $"{perSecond / 1e6:0.#} M{unit}";
        if (perSecond >= 1e3) return $"{perSecond / 1e3:0} K{unit}";
        return $"{perSecond:0} {unit}";
    }

    private static string Ghz(int mhz) => $"{mhz / 1000.0:0.00} GHz";

    private static string Gb(ulong bytes, int decimals = 0)
    {
        double gb = bytes / 1073741824.0;
        return decimals == 0 || gb >= 100 ? $"{Math.Round(gb):0} GB" : $"{gb.ToString("0." + new string('#', decimals))} GB";
    }

    /// <summary>Drops the trademark clutter from hardware names ("AMD Ryzen Z1 Extreme w/ Radeon 780M Graphics").</summary>
    private static string Short(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        foreach (var junk in new[] { "(R)", "(TM)", "(tm)", "®", "™", " CPU", " Processor" })
            name = name.Replace(junk, "");
        int at = name.IndexOf('@');
        if (at > 0) name = name[..at];
        return System.Text.RegularExpressions.Regex.Replace(name, @"\s{2,}", " ").Trim();
    }

    /// <summary>One part: icon and title, its name, the big usage figure with a graph, and two lines of facts.</summary>
    private sealed class Card : Border
    {
        private readonly TextBlock _percent = Ui.Text("—", 38, "FgBrush", bold: true);
        private readonly TextBlock _name = Ui.Text("", 17, "FgBrush", bold: true);
        private readonly TextBlock _live = Ui.Text("", 14, "AccentBrush", bold: true);
        private readonly TextBlock _fact1 = Ui.Text("", 14, "SubtleBrush"), _fact2 = Ui.Text("", 14, "SubtleBrush");
        private readonly Polyline _line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        private readonly Polygon _fill = new() { Opacity = 0.18 };
        private readonly Border _barFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        private readonly Grid _bar = new() { Height = 6, Margin = new Thickness(0, 10, 0, 0) };
        private readonly List<double> _history = new();
        private const double GraphWidth = 200, GraphHeight = 46;

        public Card(string glyph, string title, string name)
        {
            CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner, 8));
            Padding = new Thickness(18, 16, 18, 16);
            Margin = new Thickness(0, 0, 10, 10);
            SetResourceReference(BackgroundProperty, "CircleBrush");
            SetResourceReference(BorderBrushProperty, "PanelStroke");
            SetResourceReference(BorderThicknessProperty, "PanelStrokeThickness");

            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Ui.Glyph(glyph, 18, "AccentBrush");
            icon.Margin = new Thickness(0, 1, 10, 0);
            head.Children.Add(icon);
            head.Children.Add(Ui.Text(title, 16, "SubtleBrush", bold: true));
            stack.Children.Add(head);

            var nameText = _name;
            nameText.Text = name;
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            nameText.ToolTip = name;
            nameText.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(nameText);

            // The big figure on the left, the last minute as a little graph on the right.
            var middle = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            middle.ColumnDefinitions.Add(new ColumnDefinition());
            var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            numbers.Children.Add(_percent);
            numbers.Children.Add(_live);
            middle.Children.Add(numbers);
            var graph = new Canvas { Height = GraphHeight, Width = GraphWidth, ClipToBounds = true };
            _line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            _fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
            graph.Children.Add(_fill);
            graph.Children.Add(_line);
            var viewbox = new Viewbox { Child = graph, Stretch = Stretch.Fill, Height = GraphHeight, Margin = new Thickness(12, 0, 0, 4) };
            Grid.SetColumn(viewbox, 1);
            middle.Children.Add(viewbox);
            stack.Children.Add(middle);

            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Opacity = 0.25 };
            track.SetResourceReference(BackgroundProperty, "SubtleBrush");
            _barFill.SetResourceReference(BackgroundProperty, "AccentBrush");
            _bar.Children.Add(track);
            _bar.Children.Add(_barFill);
            stack.Children.Add(_bar);

            _fact1.Margin = new Thickness(0, 10, 0, 0);
            _fact1.TextTrimming = _fact2.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(_fact1);
            stack.Children.Add(_fact2);
            Child = stack;
        }

        public void SetName(string name)
        {
            _name.Text = name;
            _name.ToolTip = name;
        }

        public void SetFacts(string first, string second)
        {
            _fact1.Text = first;
            _fact2.Text = second;
            _fact2.Visibility = second.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Waiting()
        {
            if (_history.Count == 0) _percent.Text = "—";
        }

        public void Push(double percent, string? live, string? big = null)
        {
            percent = Math.Clamp(percent, 0, 100);
            _percent.Text = big ?? $"{Math.Round(percent):0}%";
            _percent.FontSize = big != null ? 30 : 38;
            if (live != null) _live.Text = live;
            _barFill.Width = Math.Max(0, _bar.ActualWidth * percent / 100);
            _history.Add(percent);
            if (_history.Count > History) _history.RemoveAt(0);
            var points = new PointCollection();
            double step = GraphWidth / (History - 1);
            double x0 = GraphWidth - (_history.Count - 1) * step;
            for (int k = 0; k < _history.Count; k++)
                points.Add(new Point(x0 + k * step, GraphHeight - 2 - (GraphHeight - 4) * _history[k] / 100));
            _line.Points = points;
            var area = new PointCollection(points) { new Point(GraphWidth, GraphHeight), new Point(x0, GraphHeight) };
            _fill.Points = area;
        }
    }
}
