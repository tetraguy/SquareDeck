using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// My Device: the handheld at a glance, live, laid out as a dashboard. At the top, what it is (model, processor,
/// graphics, memory, Windows, BIOS) beside a ring showing the battery. Then round gauges for the processor, graphics,
/// temperature and fan, and cards for power (draw, battery health, pace, power limit, controller battery) and the
/// system (memory, each drive, display, network, sound, Windows, time on). Every card can be pressed to open the
/// settings it belongs to. Small graphs show the last two minutes.
/// </summary>
public sealed class MyDeviceScreen : ListScreen
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private HardwareMonitor? _monitor;
    private readonly Dictionary<string, Card> _cards = new();
    private readonly Dictionary<string, Gauge> _gauges = new();
    private readonly Dictionary<string, List<double>> _history = new();
    private static readonly DateTime OpenedAt = DateTime.Now;
    private Gauge? _batteryRing;
    private TextBlock? _batteryState;
    private int _ticks;
    private (string Name, string Detail, int? Bars)? _network;

    private const double CardWidth = 219;

    private sealed class Card
    {
        public TextBlock Value = null!, Detail = null!;
        public Polyline? Graph;
        public Border? Bar, BarTrack;
    }

    private sealed class Gauge
    {
        public Path Arc = null!;
        public TextBlock Value = null!, Detail = null!;
        public double Size;
    }

    public MyDeviceScreen() : base("My Device")
    {
        _timer.Tick += (_, _) => Refresh();
    }

    private bool _opened;

    public override void OnShown()
    {
        _monitor ??= new HardwareMonitor();
        // The first time it opens, start at the top: the list would otherwise put the highlight on its first row,
        // which is under "More" at the very bottom (the tiles above aren't rows), and scroll all the way down to it.
        bool first = !_opened;
        _opened = true;
        if (first) SuppressAutoFocus = true;
        base.OnShown();
        if (first)
        {
            SuppressAutoFocus = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                RowsScroll.ScrollToTop();
                FocusDefault(); // the first tile (Processor), just under the device card
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RowsScroll.ScrollToTop()));
            }));
        }
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

    private static Color Accent => (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

    protected override void Build()
    {
        _cards.Clear();
        _gauges.Clear();
        Rows.Children.Add(Hero());

        Section("Live");
        var live = new WrapPanel();
        GaugeTile(live, "cpu", "", "Processor", () => Host.Push(new TaskManagerScreen()));
        GaugeTile(live, "gpu", "", "Graphics", () => Host.Push(new TaskManagerScreen()));
        GaugeTile(live, "temp", "", "Temperature", () => Host.Push(new SettingsScreen(SettingsScreen.TdpSection)));
        GaugeTile(live, "fans", "", "Fan", () => Host.Push(new SettingsScreen(SettingsScreen.TdpSection)));
        Rows.Children.Add(live);

        Section("Power");
        var power = new WrapPanel();
        CardTile(power, "draw", "", "Power draw", () => Host.Push(new BatteryScreen()), graph: true);
        CardTile(power, "health", "", "Battery health", () => Host.Push(new BatteryScreen()), bar: true);
        CardTile(power, "drain", "", "Battery pace", () => Host.Push(new BatteryScreen()));
        CardTile(power, "tdp", "", "Power limit", () => Host.Push(new SettingsScreen(SettingsScreen.TdpSection)));
        CardTile(power, "pad", "", "Controller", () => Host.Push(new SettingsScreen("controller")));
        CardTile(power, "brightness", "", "Brightness", () => Host.Push(new QuickSettingsScreen()), bar: true);
        Rows.Children.Add(power);

        Section("System");
        var system = new WrapPanel();
        CardTile(system, "memory", "", "Memory", () => Host.Push(new TaskManagerScreen()), bar: true, graph: true);
        foreach (var drive in Drives())
        {
            var root = drive.RootDirectory.FullName;
            CardTile(system, "drive:" + drive.Name, "", "Storage  " + drive.Name.TrimEnd('\\'), () => Host.Push(new ExplorerScreen(root)), bar: true);
        }
        CardTile(system, "display", "", "Display", () => Host.Push(new SettingsScreen("look")));
        CardTile(system, "network", "", "Network", () => Host.Push(new SettingsScreen("network")));
        CardTile(system, "sound", "", "Sound", () => Host.Push(new SettingsScreen("sound")), bar: true);
        CardTile(system, "windows", "", "Windows", () => Host.Push(new SettingsScreen("system")));
        CardTile(system, "uptime", "", "On for", () => Host.Push(new SettingsScreen("system")));
        Rows.Children.Add(system);

        AddHeader("More");
        AddRow(Handheld.DisplayName + " Specific Settings", null, Ui.HandheldGlyph, click: () => Host.Push(new SettingsScreen("handheld")),
            subtitle: "TDP limit, lights, power modes, fans, charge limit and more for this device");
        AddRow("Performance Boost and the overlay", null, "", click: () => Host.Push(new SettingsScreen("gaming")),
            subtitle: "Performance Boost, SquareDeck performance mode and the performance overlay, in Gaming");
        AddRow("Battery", null, "", click: () => Host.Push(new BatteryScreen()), subtitle: "Battery saver, charging and usage");
        AddRow("My Device on the home screen", Host.Settings.ShowMyDeviceButton ? "On" : "Off", "", click: () =>
        {
            Host.Settings.ShowMyDeviceButton = !Host.Settings.ShowMyDeviceButton;
            Host.Settings.Save();
            Host.RefreshHomeButtonsPublic();
            Rebuild();
        });
        _ticks = 0;
        Refresh();
    }

    private static IEnumerable<DriveInfo> Drives()
    {
        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); } catch (Exception) { yield break; }
        foreach (var d in all)
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0; } catch (Exception) { ok = false; }
            if (ok) yield return d;
        }
    }

    private void Section(string title)
    {
        var t = Ui.Text(title.ToUpperInvariant(), 13, "SubtleBrush", bold: true);
        t.Margin = new Thickness(10, 22, 0, 4);
        Rows.Children.Add(t);
    }

    // ---------------------------------------------------------------- the top

    private UIElement Hero()
    {
        var a = Accent;
        var panel = new Border { CornerRadius = new CornerRadius(26), Padding = new Thickness(28, 24, 28, 24), Margin = new Thickness(8, 0, 8, 4), ClipToBounds = true };
        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, a.R, a.G, a.B), 0));
        bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, a.R, a.G, a.B), 0.6));
        bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, a.R, a.G, a.B), 1));
        panel.Background = bg;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var photo = DevicePhoto();
        if (photo != null) grid.Children.Add(photo);
        // A soft circle of light behind the battery ring.
        var halo = new Ellipse { Width = 360, Height = 360, Margin = new Thickness(0, -120, -140, -120), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x40, a.R, a.G, a.B), Color.FromArgb(0, a.R, a.G, a.B)) };
        Grid.SetColumnSpan(halo, 3);
        grid.Children.Add(halo);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        // The device's own picture stands in for the icon when there is one.
        if (photo == null)
        {
            var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 16, 0), Background = new SolidColorBrush(a) };
            var icon = Ui.Glyph(Ui.HandheldGlyph, 32, "BgBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = icon;
            top.Children.Add(badge);
        }
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(Handheld.DisplayName, 34, "FgBrush", bold: true));
        if (Handheld.Brand.Length > 0) names.Children.Add(Ui.Text(Handheld.Brand, 15, "SubtleBrush"));
        top.Children.Add(names);
        left.Children.Add(top);

        // What's inside, as chips.
        var chips = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        var info = Hardware.Info;
        void Chip(string glyph, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 13, "AccentBrush");
            g.Margin = new Thickness(0, 1, 7, 0);
            row.Children.Add(g);
            row.Children.Add(Ui.Text(text, 13, "FgBrush"));
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 8, 8), Child = row };
            chip.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            chips.Children.Add(chip);
        }
        Chip("", info.CpuName + (info.Cores > 0 ? $"  ·  {info.Cores}C / {info.Threads}T" : ""));
        if (info.Gpus.Count > 0) Chip("", info.Gpus[0].Name);
        if (info.MemoryBytes > 0) Chip("", $"{info.MemoryBytes / 1073741824.0:0} GB {info.MemoryType}".Trim());
        Chip("", WindowsVersion());
        string bios = Bios("BIOSVersion");
        if (bios.Length > 0) Chip("", "BIOS " + bios);
        left.Children.Add(chips);
        Grid.SetColumn(left, 1);
        grid.Children.Add(left);

        // The battery ring.
        var ringBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        _batteryRing = MakeGauge(170, 14, 34);
        ringBox.Children.Add(_batteryRing.Arc.Parent as UIElement);
        _batteryState = Ui.Text("", 14, "SubtleBrush");
        _batteryState.HorizontalAlignment = HorizontalAlignment.Center;
        _batteryState.Margin = new Thickness(0, 8, 0, 0);
        ringBox.Children.Add(_batteryState);
        Grid.SetColumn(ringBox, 2);
        grid.Children.Add(ringBox);

        panel.Child = grid;
        return panel;
    }

    /// <summary>
    /// The handheld itself (the right picture for the model), floating gently over a soft shadow, sliding in when the
    /// page opens. Null when SquareDeck has no picture of this device.
    /// </summary>
    internal static UIElement? DevicePhoto(double width = 380)
    {
        string? path = Handheld.ImagePath;
        if (path == null) return null;
        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 760;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
        }
        catch (Exception) { return null; }

        double height = width * bmp.PixelHeight / Math.Max(1, bmp.PixelWidth);
        var box = new Grid { Width = width, Margin = new Thickness(-6, -8, 26, -8), VerticalAlignment = VerticalAlignment.Center };
        var shadow = new Ellipse
        {
            Width = width * 0.78, Height = 26, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -6), IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0))
        };
        box.Children.Add(shadow);
        var image = new Image { Source = bmp, Width = width, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 14) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var lift = new TranslateTransform();
        image.RenderTransform = lift;
        box.Children.Add(image);

        // Slides up and fades in, then floats.
        box.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        box.Loaded += (_, _) =>
        {
            box.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
            var rise = new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease };
            rise.Completed += (_, _) =>
            {
                var bob = new DoubleAnimation(0, -7, TimeSpan.FromSeconds(2.6))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                lift.BeginAnimation(TranslateTransform.YProperty, bob);
                var breathe = new DoubleAnimation(1, 0.82, TimeSpan.FromSeconds(2.6))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                shadow.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(width * 0.78, width * 0.66, TimeSpan.FromSeconds(2.6))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                shadow.BeginAnimation(UIElement.OpacityProperty, breathe);
            };
            lift.BeginAnimation(TranslateTransform.YProperty, rise);
        };
        return box;
    }

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

    private static string WindowsBuild()
    {
        try
        {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var ubr = Microsoft.Win32.Registry.GetValue(key, "UBR", null);
            return "Build " + Environment.OSVersion.Version.Build + (ubr is int u ? "." + u : "");
        }
        catch (Exception) { return ""; }
    }

    // ---------------------------------------------------------------- gauges and cards

    /// <summary>A ring gauge: a faint track, an arc for the value (three quarters of a circle at full), and the value inside.</summary>
    private static Gauge MakeGauge(double size, double thickness, double fontSize)
    {
        var a = Accent;
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        var track = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Data = ArcData(size, thickness, 1) };
        var arc = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var stroke = new LinearGradientBrush(Color.FromArgb(0xFF, (byte)Math.Min(255, a.R + 60), (byte)Math.Min(255, a.G + 60), (byte)Math.Min(255, a.B + 60)), a, 45);
        arc.Stroke = stroke;
        grid.Children.Add(track);
        grid.Children.Add(arc);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var value = Ui.Text("…", fontSize, "FgBrush", bold: true);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        var detail = Ui.Text("", Math.Max(11, fontSize * 0.36), "SubtleBrush");
        detail.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(value);
        center.Children.Add(detail);
        grid.Children.Add(center);
        return new Gauge { Arc = arc, Value = value, Detail = detail, Size = size };
    }

    private static Geometry ArcData(double size, double thickness, double part)
    {
        part = Math.Clamp(part, 0.0001, 1);
        double r = (size - thickness) / 2, cx = size / 2, cy = size / 2;
        double start = 135, sweep = 270 * part;
        Point At(double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(start), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(start + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    private static void SetGauge(Gauge? g, double? part, string value, string detail)
    {
        if (g == null) return;
        g.Arc.Data = part is double p ? ArcData(g.Size, g.Arc.StrokeThickness, p) : null;
        g.Value.Text = value;
        g.Detail.Text = detail;
    }

    private static Border Face(UIElement content, double height)
    {
        var card = new Border { Width = CardWidth, Height = height, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 12), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
        return card;
    }

    private Button Pressable(Panel into, Border face, Action open)
    {
        var tile = Ui.Tile(face);
        tile.Margin = new Thickness(0);
        tile.Click += (_, _) => open();
        into.Children.Add(tile);
        return tile;
    }

    private void GaugeTile(Panel into, string id, string glyph, string title, Action open)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(glyph, 15, "AccentBrush");
        g.Margin = new Thickness(0, 2, 8, 0);
        head.Children.Add(g);
        head.Children.Add(Ui.Text(title, 14, "SubtleBrush"));
        stack.Children.Add(head);
        var gauge = MakeGauge(150, 12, 30);
        var host = (FrameworkElement)gauge.Arc.Parent;
        host.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(host);
        _gauges[id] = gauge;
        Pressable(into, Face(stack, 214), open);
    }

    private void CardTile(Panel into, string id, string glyph, string title, Action open, bool graph = false, bool bar = false)
    {
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(glyph, 15, "AccentBrush");
        g.Margin = new Thickness(0, 2, 8, 0);
        head.Children.Add(g);
        var titleText = Ui.Text(title, 14, "SubtleBrush");
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(titleText);
        grid.Children.Add(head);
        var value = Ui.Text("…", 24, "FgBrush", bold: true);
        value.Margin = new Thickness(0, 6, 0, 0);
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(value, 1);
        grid.Children.Add(value);
        var detail = Ui.Text("", 12.5, "SubtleBrush");
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(detail, 2);
        grid.Children.Add(detail);
        var card = new Card { Value = value, Detail = detail };
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (graph)
        {
            var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.85 };
            line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            var canvas = new Canvas { Height = 24, ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0) };
            canvas.Children.Add(line);
            bottom.Children.Add(canvas);
            card.Graph = line;
        }
        if (bar)
        {
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 8, 0, 0) };
            track.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var holder = new Grid();
            holder.Children.Add(track);
            holder.Children.Add(fill);
            bottom.Children.Add(holder);
            card.Bar = fill;
            card.BarTrack = track;
        }
        Grid.SetRow(bottom, 3);
        grid.Children.Add(bottom);
        _cards[id] = card;
        Pressable(into, Face(grid, 150), open);
    }

    private void Set(string id, string value, string detail, double? graphValue = null, double? barPart = null)
    {
        if (!_cards.TryGetValue(id, out var c)) return;
        c.Value.Text = value;
        c.Detail.Text = detail;
        if (c.Bar != null) c.Bar.Width = Math.Max(barPart is double b && b > 0 ? 6 : 0, (CardWidth - 32) * Math.Clamp(barPart ?? 0, 0, 1));
        if (c.Graph == null || graphValue == null) return;
        if (!_history.TryGetValue(id, out var list)) _history[id] = list = new List<double>();
        list.Add(graphValue.Value);
        if (list.Count > 60) list.RemoveAt(0);
        double min = list.Min(), max = list.Max();
        if (max - min < 1) { min -= 1; max += 1; }
        double width = CardWidth - 32, height = 22;
        var points = new PointCollection();
        for (int i = 0; i < list.Count; i++)
            points.Add(new Point(width - (list.Count - 1 - i) * (width / 59), height - (list[i] - min) / (max - min) * height));
        c.Graph.Points = points;
    }

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{Math.Max(1, t.Minutes)} min";

    private static string Gb(double bytes) => bytes >= 1099511627776 ? $"{bytes / 1099511627776.0:0.0} TB" : $"{bytes / 1073741824.0:0} GB";

    // ---------------------------------------------------------------- live values

    private void Refresh()
    {
        if (_cards.Count == 0) return;
        _ticks++;
        try
        {
            var r = DeviceSensors.Read();
            var info = Hardware.Info;

            // Battery ring and power draw.
            double draw = Math.Abs(r.DrawWatts ?? 0);
            if (r.Percent is int p)
            {
                string state;
                if (r.Plugged) state = p >= 100 ? "Fully charged" : draw > 0.2 ? $"Charging  ·  {draw:0.0} W" : "Plugged in";
                else state = Battery.GetTimeRemaining() is { } left ? Duration(left) + " left" : "On battery";
                SetGauge(_batteryRing, p / 100.0, p + "%", r.Plugged ? "⚡ plugged in" : "battery");
                if (_batteryState != null) _batteryState.Text = state;
                Set("draw", draw > 0.05 ? $"{draw:0.0} W" : "—", r.Plugged ? (draw > 0.2 ? "Going into the battery" : "From the charger") : "From the battery", draw > 0 ? draw : null);
            }
            else
            {
                SetGauge(_batteryRing, null, "AC", "no battery");
                if (_batteryState != null) _batteryState.Text = "Running on power";
                Set("draw", "—", "No battery to measure");
            }

            if (r.HealthPercent is double h)
                Set("health", $"{h:0}%", $"{r.FullWh:0.0} of {r.DesignWh:0.0} Wh when new", barPart: h / 100);
            else Set("health", "—", "Windows doesn't report it");

            if (r.Plugged) Set("drain", "Charging", "Measured once unplugged");
            else if (DeviceSensors.DrainPerHour() is double pace)
                Set("drain", $"{pace:0.0}% / h", pace > 0 ? $"≈ {Duration(TimeSpan.FromHours(100 / pace))} from full" : "");
            else Set("drain", "Measuring…", "Needs a few minutes on battery");

            // Gauges: processor, graphics, temperature, fan.
            if (_monitor != null)
            {
                var (cpu, mhz, gpu) = _monitor.Sample();
                SetGauge(_gauges.GetValueOrDefault("cpu"), cpu / 100, cpu is double c ? $"{c:0}%" : "—", mhz is int m ? $"{m / 1000.0:0.00} GHz" : $"{info.Cores} cores");
                SetGauge(_gauges.GetValueOrDefault("gpu"), gpu / 100, gpu is double g ? $"{g:0}%" : "—", info.Gpus.Count > 0 ? ShortGpu(info.Gpus[0].Name) : "");
            }
            if (r.CpuTemp is int t)
                SetGauge(_gauges.GetValueOrDefault("temp"), Math.Clamp((t - 30) / 70.0, 0, 1), t + "°", r.GpuTemp is int gt ? $"GPU {gt}°  ·  {r.TempSource}" : r.TempSource);
            else SetGauge(_gauges.GetValueOrDefault("temp"), null, "—", Handheld.Vendor == HandheldVendor.Lenovo && !DeviceHelper.Installed ? "set up the device helper" : "not reported");
            if (r.CpuFanRpm is int f)
                SetGauge(_gauges.GetValueOrDefault("fans"), Math.Clamp(f / 6500.0, 0, 1), f == 0 ? "Off" : $"{f / 1000.0:0.0}k", f == 0 ? "not spinning" : r.GpuFanRpm is int gf ? $"RPM  ·  2nd {gf:N0}" : "RPM");
            else SetGauge(_gauges.GetValueOrDefault("fans"), null, "—", "not reported");

            // Power limit.
            if (TdpControl.Ready)
                Set("tdp", TdpControl.EffectiveWatts + " W", TdpControl.MethodName(TdpControl.Method) + (TdpControl.Chip is { } chip ? $"  ·  {chip.Min}–{chip.Max} W" : ""));
            else Set("tdp", TdpControl.CanApply ? "Off" : "—", TdpControl.CanApply ? "TDP control is off" : "Set up in Settings");

            // Controller battery (a wireless pad connected), and brightness.
            string? pad = Host.ControllerBattery;
            Set("pad", pad ?? "—", pad == null ? "No controller battery to read" : pad == "Wired" ? "Connected by cable or built in" : "Wireless controller battery");
            if (Brightness.Last is int light) Set("brightness", light + "%", "Built-in screen", barPart: light / 100.0);
            else Set("brightness", "—", Brightness.Supported == false ? "This screen doesn't allow it" : "Reading…");

            // Memory.
            if (Hardware.MemoryStatus() is { } mem)
            {
                double used = mem.Total - mem.Available;
                Set("memory", $"{used / 1073741824.0:0.0} / {mem.Total / 1073741824.0:0} GB",
                    (info.MemorySpeed > 0 ? $"{info.MemoryType} {info.MemorySpeed} MT/s  ·  " : "") + $"{used / mem.Total * 100:0}% used", used / mem.Total * 100, used / mem.Total);
            }

            // Drives (every few seconds is plenty).
            if (_ticks % 5 == 1)
                foreach (var d in Drives())
                {
                    try
                    {
                        double used = d.TotalSize - d.AvailableFreeSpace;
                        string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveFormat : d.VolumeLabel;
                        Set("drive:" + d.Name, Gb(d.AvailableFreeSpace) + " free", $"of {Gb(d.TotalSize)}  ·  {label}", barPart: used / d.TotalSize);
                    }
                    catch (Exception) { }
                }

            // Display.
            if (DisplaySettings.Current() is { } mode)
            {
                double scale = 100;
                try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX * 100; } catch (Exception) { }
                Set("display", $"{mode.Width} × {mode.Height}", $"{mode.RefreshRate} Hz  ·  {scale:0}% scale");
            }

            // Network (looked up in the background every ten seconds).
            if (_ticks % 5 == 1) _ = ReadNetworkAsync();
            if (_network is { } n) Set("network", n.Name, n.Detail, barPart: n.Bars is int b ? b / 5.0 : null);
            else Set("network", "…", "");

            // Sound.
            var volume = Audio.GetVolume();
            bool muted = Audio.GetMute();
            Set("sound", muted ? "Muted" : volume is int v ? v + "%" : "—", "Volume", barPart: muted ? 0 : (volume ?? 0) / 100.0);

            // Windows and time on.
            if (_ticks == 1) Set("windows", WindowsVersion().Replace("Windows ", "Win "), WindowsBuild());
            Set("uptime", Duration(TimeSpan.FromMilliseconds(Environment.TickCount64)), "SquareDeck open " + Duration(DateTime.Now - OpenedAt));
        }
        catch (Exception) { }
    }

    private static string ShortGpu(string name) =>
        name.Replace("(TM)", "").Replace("(R)", "").Replace("Graphics", "").Replace("AMD ", "").Replace("Intel ", "").Replace("NVIDIA ", "").Trim();

    /// <summary>The connection in use: Wi-Fi network name and signal, or the wired adapter; its address and speed.</summary>
    private async Task ReadNetworkAsync()
    {
        try
        {
            var found = await Task.Run(() =>
            {
                string name = "Offline", detail = "Not connected";
                int? bars = null;
                try
                {
                    var profile = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                    if (profile != null)
                    {
                        name = profile.ProfileName;
                        if (profile.IsWlanConnectionProfile) bars = profile.GetSignalBars();
                    }
                }
                catch (Exception) { }
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                    var props = nic.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    var v4 = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
                    bool wifi = nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
                    string speed = nic.Speed > 0 ? (nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1e9:0.#} Gbps" : $"{nic.Speed / 1e6:0} Mbps") : "";
                    if (name == "Offline") name = wifi ? "Wi-Fi" : "Ethernet";
                    detail = string.Join("  ·  ", new[] { wifi ? "Wi-Fi" : "Wired", v4 ?? "", speed }.Where(x => x.Length > 0));
                    break;
                }
                return (name, detail, bars);
            });
            _network = found;
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
