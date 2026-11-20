using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// My Device: the handheld at a glance, live. What it is (model, processor, graphics, memory, Windows), and how it's
/// doing right now: battery and power draw, battery health, temperatures, fans, processor and graphics load, the power
/// limit, storage, the display, how fast the battery is going down, and how long it's been on. Small graphs show the
/// last two minutes.
/// </summary>
public sealed class MyDeviceScreen : ListScreen
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private HardwareMonitor? _monitor;
    private readonly Dictionary<string, (TextBlock Value, TextBlock Detail, Polyline? Graph)> _cards = new();
    private readonly Dictionary<string, List<double>> _history = new();
    private static readonly DateTime OpenedAt = DateTime.Now;

    public MyDeviceScreen() : base("My Device")
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public override void OnShown()
    {
        _monitor ??= new HardwareMonitor();
        base.OnShown();
        Refresh();
        _timer.Start();
    }

    public override void OnResumed() { base.OnResumed(); _timer.Start(); }
    public override void OnCovered() { base.OnCovered(); _timer.Stop(); }

    public override void OnClosed()
    {
        _timer.Stop();
        _monitor?.Dispose();
        _monitor = null;
        base.OnClosed();
    }

    protected override void Build()
    {
        _cards.Clear();
        Rows.Children.Add(Hero());

        var grid = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        Card(grid, "battery", "\uE83F", "Battery", graph: true);
        Card(grid, "health", "\uEBB5", "Battery health");
        Card(grid, "temp", "\uE9CA", "Temperature", graph: true);
        Card(grid, "fans", "\uE9F5", "Fans", graph: true);
        Card(grid, "cpu", "\uE950", "Processor", graph: true);
        Card(grid, "gpu", "\uE7F4", "Graphics", graph: true);
        Card(grid, "memory", "\uE964", "Memory");
        Card(grid, "tdp", "\uE945", "Power limit");
        Card(grid, "storage", "\uEDA2", "Storage");
        Card(grid, "display", "\uE7F4", "Display");
        Card(grid, "drain", "\uE8EF", "Battery pace");
        Card(grid, "uptime", "\uE823", "On for");
        Rows.Children.Add(grid);

        AddHeader("More");
        AddRow(Handheld.DisplayName + " Specific Settings", null, Ui.HandheldGlyph, click: () => Host.Push(new SettingsScreen("handheld")),
            subtitle: "Lights, power modes, fans, charge limit and more for this device");
        AddRow("Power and performance", null, "\uEC4A", click: () => Host.Push(new SettingsScreen("power")),
            subtitle: "TDP limit, CPU boost, Windows power mode");
        AddRow("Battery", null, "\uE83F", click: () => Host.Push(new BatteryScreen()), subtitle: "Battery saver, charging and usage");
        AddRow("My Device on the home screen", Host.Settings.ShowMyDeviceButton ? "On" : "Off", "\uE8A9", click: () =>
        {
            Host.Settings.ShowMyDeviceButton = !Host.Settings.ShowMyDeviceButton;
            Host.Settings.Save();
            Host.RefreshHomeButtonsPublic();
            Rebuild();
        });
        Refresh();
    }

    private UIElement Hero()
    {
        var a = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var panel = new Border
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush(Color.FromArgb(0x50, a.R, a.G, a.B), Color.FromArgb(0x08, a.R, a.G, a.B), 0)
        };
        var row = new DockPanel();
        var badge = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(26), Margin = new Thickness(0, 0, 22, 0), Background = new SolidColorBrush(a) };
        var icon = Ui.Glyph(Ui.HandheldGlyph, 48, "BgBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        badge.Child = icon;
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(Handheld.DisplayName, 32, "FgBrush", bold: true));
        var info = Hardware.Info;
        var line1 = new List<string>();
        if (Handheld.Brand.Length > 0) line1.Add(Handheld.Brand);
        if (info.CpuName.Length > 0) line1.Add(info.CpuName);
        if (info.Cores > 0) line1.Add($"{info.Cores} cores / {info.Threads} threads");
        text.Children.Add(Wrap(Ui.Text(string.Join("   \u00B7   ", line1), 15, "SubtleBrush")));
        var line2 = new List<string>();
        if (info.Gpus.Count > 0) line2.Add(info.Gpus[0].Name);
        if (info.MemoryBytes > 0) line2.Add($"{info.MemoryBytes / 1073741824.0:0} GB {info.MemoryType}".Trim());
        line2.Add(WindowsVersion());
        string bios = Bios("BIOSVersion");
        if (bios.Length > 0) line2.Add("BIOS " + bios);
        text.Children.Add(Wrap(Ui.Text(string.Join("   \u00B7   ", line2), 14, "SubtleBrush")));
        row.Children.Add(text);
        panel.Child = row;
        return panel;
    }

    private static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }

    private static string Bios(string name)
    {
        try { return (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", name, "") as string ?? "").Trim(); }
        catch (Exception) { return ""; }
    }

    private static string WindowsVersion()
    {
        try
        {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string display = Microsoft.Win32.Registry.GetValue(key, "DisplayVersion", "") as string ?? "";
            int build = Environment.OSVersion.Version.Build;
            return (build >= 22000 ? "Windows 11" : "Windows 10") + (display.Length > 0 ? " " + display : "");
        }
        catch (Exception) { return "Windows"; }
    }

    private void Card(Panel into, string id, string glyph, string title, bool graph = false)
    {
        var card = new Border { Width = 300, Height = 150, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 12), Margin = new Thickness(0, 0, 10, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(glyph, 15, "AccentBrush");
        g.Margin = new Thickness(0, 2, 8, 0);
        head.Children.Add(g);
        head.Children.Add(Ui.Text(title, 14, "SubtleBrush"));
        stack.Children.Add(head);
        var value = Ui.Text("…", 28, "FgBrush", bold: true);
        value.Margin = new Thickness(0, 4, 0, 0);
        Grid.SetRow(value, 1);
        stack.Children.Add(value);
        var detail = Ui.Text("", 13, "SubtleBrush");
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(detail, 2);
        stack.Children.Add(detail);
        Polyline? line = null;
        if (graph)
        {
            line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Bottom };
            line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            var canvas = new Canvas { Height = 26, ClipToBounds = true, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            canvas.Children.Add(line);
            Grid.SetRow(canvas, 3);
            stack.Children.Add(canvas);
        }
        card.Child = stack;
        into.Children.Add(card);
        _cards[id] = (value, detail, line);
    }

    private void Set(string id, string value, string detail, double? graphValue = null)
    {
        if (!_cards.TryGetValue(id, out var c)) return;
        c.Value.Text = value;
        c.Detail.Text = detail;
        if (c.Graph == null || graphValue == null) return;
        if (!_history.TryGetValue(id, out var list)) _history[id] = list = new List<double>();
        list.Add(graphValue.Value);
        if (list.Count > 60) list.RemoveAt(0);
        double min = list.Min(), max = list.Max();
        if (max - min < 1) { min -= 1; max += 1; }
        const double width = 268, height = 24;
        var points = new PointCollection();
        for (int i = 0; i < list.Count; i++)
            points.Add(new Point(width - (list.Count - 1 - i) * (width / 59), height - (list[i] - min) / (max - min) * height));
        c.Graph.Points = points;
    }

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{Math.Max(1, t.Minutes)} min";

    private void Refresh()
    {
        if (_cards.Count == 0) return;
        try
        {
            var r = DeviceSensors.Read();

            // Battery and power draw.
            if (r.Percent is int p)
            {
                string detail;
                double draw = Math.Abs(r.DrawWatts ?? 0);
                if (r.Plugged) detail = draw > 0.2 ? $"Charging at {draw:0.0} W" : "Plugged in";
                else
                {
                    var left = Battery.GetTimeRemaining();
                    detail = (draw > 0.2 ? $"Using {draw:0.0} W" : "On battery") + (left is { } l ? "  \u00B7  " + Duration(l) + " left" : "");
                }
                Set("battery", p + "%", detail, draw > 0 ? draw : null);
            }
            else Set("battery", "No battery", "Running on power");

            if (r.HealthPercent is double h)
                Set("health", $"{h:0}%", $"Holds {r.FullWh:0.0} of {r.DesignWh:0.0} Wh when new");
            else Set("health", "—", "Windows doesn't report it");

            // Temperatures and fans.
            if (r.CpuTemp is int t)
                Set("temp", t + " °C", (r.GpuTemp is int gt ? $"Graphics {gt} °C  \u00B7  " : "") + r.TempSource, t);
            else Set("temp", "—", "This device doesn't report it" + (Handheld.Vendor == HandheldVendor.Lenovo && !DeviceHelper.Installed ? " (set up the device helper)" : ""));
            if (r.CpuFanRpm is int f)
                Set("fans", f.ToString("N0") + " RPM", r.GpuFanRpm is int gf ? $"Second fan {gf:N0} RPM" : (f == 0 ? "Not spinning" : "Fan speed"), f);
            else Set("fans", "—", "This device doesn't report it");

            // Processor and graphics.
            if (_monitor != null)
            {
                var (cpu, mhz, gpu) = _monitor.Sample();
                var info = Hardware.Info;
                Set("cpu", cpu is double c ? $"{c:0}%" : "—", (mhz is int m ? $"{m / 1000.0:0.0} GHz  \u00B7  " : "") + $"{info.Cores} cores", cpu);
                Set("gpu", gpu is double g ? $"{g:0}%" : "—", info.Gpus.Count > 0 ? info.Gpus[0].Name : "Graphics", gpu);
            }

            // Memory.
            if (Hardware.MemoryStatus() is { } mem)
            {
                double used = (mem.Total - mem.Available) / 1073741824.0, total = mem.Total / 1073741824.0;
                var info = Hardware.Info;
                Set("memory", $"{used:0.0} / {total:0} GB", info.MemorySpeed > 0 ? $"{info.MemoryType} {info.MemorySpeed} MT/s".Trim() : $"{used / total * 100:0}% in use");
            }

            // Power limit.
            if (TdpControl.Ready) Set("tdp", TdpControl.EffectiveWatts + " W", TdpControl.MethodName(TdpControl.Method) + (TdpControl.Chip is { } chip ? $"  \u00B7  {chip.Min}–{chip.Max} W" : ""));
            else Set("tdp", TdpControl.CanApply ? "Off" : "—", TdpControl.CanApply ? "TDP control is off" : "Set up in Power and performance");

            // Storage.
            try
            {
                var drive = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                Set("storage", $"{drive.AvailableFreeSpace / 1073741824.0:0} GB free", $"of {drive.TotalSize / 1073741824.0:0} GB ({drive.Name.TrimEnd('\\')})");
            }
            catch (Exception) { Set("storage", "—", ""); }

            // Display.
            if (DisplaySettings.Current() is { } mode) Set("display", $"{mode.Width} × {mode.Height}", $"{mode.RefreshRate} Hz");

            // Battery pace.
            if (r.Plugged) Set("drain", "Charging", "Shows how fast the battery goes down once unplugged");
            else if (DeviceSensors.DrainPerHour() is double pace)
                Set("drain", $"{pace:0.0}% / hour", pace > 0 ? $"About {Duration(TimeSpan.FromHours(100 / pace))} from a full charge at this pace" : "");
            else Set("drain", "Measuring…", "Needs a few minutes on battery");

            // On for.
            Set("uptime", Duration(TimeSpan.FromMilliseconds(Environment.TickCount64)), "SquareDeck open " + Duration(DateTime.Now - OpenedAt));
        }
        catch (Exception) { }
    }
}

/// <summary>The handhelds of one maker that SquareDeck recognizes, with each one's power range and what it can change.</summary>
public sealed class SupportedHandheldsScreen : ListScreen
{
    private readonly string _brand;

    public SupportedHandheldsScreen(string brand) : base(brand + " handhelds")
    {
        _brand = brand;
    }

    protected override void Build()
    {
        var current = Handheld.Current;
        AddNote("Recognized from the motherboard's maker and product name, as HandheldCompanion (github.com/Valkirie/HandheldCompanion) does. " +
                "The power range is the device's own; SquareDeck keeps the TDP inside it.");
        foreach (var m in Handheld.Models.Where(x => x.Brand == _brand).GroupBy(x => x.Name).Select(g => g.First()))
        {
            bool mine = ReferenceEquals(m, current) || (current != null && current.Name == m.Name);
            AddRow(m.Name, $"{m.TdpMin}–{m.TdpMax} W", Ui.HandheldGlyph, click: () => { },
                subtitle: (mine ? "This device  \u00B7  " : "") + (m.Intel ? "Intel  \u00B7  " : "AMD  \u00B7  ") + Handheld.ControlsText(m));
        }
    }
}
