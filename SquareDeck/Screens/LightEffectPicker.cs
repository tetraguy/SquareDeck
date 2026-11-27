using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Choosing the ROG Ally's light effect: the effects on the left, and on the right the two stick rings drawn with the
/// one that's highlighted playing, in your colors and at your speed, so you see how it will look before choosing it.
/// The preview is drawn from the same frames SquareDeck plays on the lights (LightFx).
/// </summary>
public sealed class LightEffectPicker : Screen
{
    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly (string? Id, string Preview, string Name, string About)[] _options;
    private readonly int _current;
    private readonly string _title;
    private readonly string? _message;
    private readonly LightFx.Inputs _inputs;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly List<Button> _rows = new();
    private readonly Path[] _zones = new Path[4];
    private readonly Path[] _glows = new Path[4];
    private TextBlock? _name, _about;
    private string _preview = "";
    private bool _s2;

    public override bool IsDialog => true;

    /// <summary>The chosen option's index, or -1 when it was closed without choosing.</summary>
    public Task<int> Result => _done.Task;

    /// <param name="options">Each option: what's saved (Id), which effect its preview plays, its name and a line about it.</param>
    public LightEffectPicker(string title, string? message, (string? Id, string Preview, string Name, string About)[] options, int current, LightFx.Inputs inputs)
    {
        _title = title;
        _message = message;
        _options = options;
        _current = Math.Clamp(current, 0, Math.Max(0, options.Length - 1));
        _inputs = inputs;
        _timer.Tick += (_, _) => Draw();
        Build();
    }

    /// <summary>Opens the picker over <paramref name="owner"/> (in the Switch 2 look where that page has it) and waits for a choice.</summary>
    public static async Task<int> AskAsync(Screen owner, string title, string? message,
        (string? Id, string Preview, string Name, string About)[] options, int current, LightFx.Inputs inputs)
    {
        var picker = new LightEffectPicker(title, message, options, current, inputs);
        if (Switch2Pages.Applies(owner)) picker.UseSwitch2Look();
        owner.Host.Push(picker);
        return await picker.Result;
    }

    /// <summary>Every effect, for Settings' own choice (its id is both what's saved and what the preview plays).</summary>
    public static (string? Id, string Preview, string Name, string About)[] AllEffects() =>
        AllyControl.LightModes.Select(m => ((string?)m.Id, m.Id, m.Name, m.About)).ToArray();

    /// <summary>Drawn like the Switch 2's pop-ups: a light card, flat rows on thin lines, a round mark on the one in use.</summary>
    public void UseSwitch2Look()
    {
        _s2 = true;
        Build();
    }

    private void Build()
    {
        if (_s2) Switch2Settings.ApplyColors(this);
        _rows.Clear();
        Brush Fg() => _s2 ? new SolidColorBrush(Switch2Settings.Text) : (Brush)FindResource("FgBrush");
        Brush Subtle() => _s2 ? new SolidColorBrush(Switch2Settings.Subtle) : (Brush)FindResource("SubtleBrush");

        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        // ---- the effects
        var left = new DockPanel { Margin = new Thickness(0, 0, 24, 0) };
        var heading = new StackPanel { Margin = new Thickness(4, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = _title, FontSize = _s2 ? 22 : 26, Foreground = Fg(), FontWeight = _s2 ? FontWeights.Normal : FontWeights.SemiBold });
        if (!string.IsNullOrEmpty(_message))
            heading.Children.Add(new TextBlock { Text = _message, FontSize = 15, Foreground = Subtle(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top);
        left.Children.Add(heading);

        var list = new StackPanel();
        for (int i = 0; i < _options.Length; i++)
        {
            int index = i;
            var o = _options[i];
            var row = Ui.Row(o.Name, null, "", subtitle: null);
            if (_s2) { row.Style = Switch2Settings.RowStyle; row.Margin = new Thickness(0); }
            if (i == _current && row.Content is Grid g)
            {
                // The one in use: a round blue mark at the right.
                var ring = new Grid { Width = 26, Height = 26, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var mark = new Ellipse();
                mark.SetResourceReference(Shape.FillProperty, "AccentBrush");
                ring.Children.Add(mark);
                ring.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Brushes.White });
                Grid.SetColumn(ring, Math.Max(0, g.ColumnDefinitions.Count - 1));
                g.Children.Add(ring);
            }
            row.GotKeyboardFocus += (_, _) => Show(index);
            row.MouseEnter += (_, _) => Show(index);
            row.Click += (_, _) => Choose(index);
            _rows.Add(row);
            list.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 470 };
        left.Children.Add(scroll);
        outer.Children.Add(left);

        // ---- the preview: the front of the Ally with its two stick rings
        var preview = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var device = new Border
        {
            Height = 230, CornerRadius = new CornerRadius(60),
            Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2D, 0x34), Color.FromRgb(0x14, 0x16, 0x1B), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
        };
        var canvas = new Canvas { Width = 380, Height = 230, ClipToBounds = false };
        // The screen between the sticks, dark.
        var screen = new Border { Width = 170, Height = 150, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0C)) };
        Canvas.SetLeft(screen, 105); Canvas.SetTop(screen, 40);
        canvas.Children.Add(screen);
        AddStick(canvas, 52, 82, 0);   // left stick: zones 0 (outer) and 1 (inner)
        AddStick(canvas, 328, 148, 2); // right stick (lower, as on the Ally): zones 2 (inner) and 3 (outer)
        device.Child = canvas;
        preview.Children.Add(device);
        _name = new TextBlock { FontSize = 20, Foreground = Fg(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 4) };
        _about = new TextBlock { FontSize = 14, Foreground = Subtle(), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 360 };
        preview.Children.Add(_name);
        preview.Children.Add(_about);
        Grid.SetColumn(preview, 1);
        outer.Children.Add(preview);

        var box = new Border
        {
            Width = 1040, Padding = new Thickness(30, 26, 30, 24), Child = outer,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        if (_s2)
        {
            box.Background = new SolidColorBrush(Switch2Settings.Card);
            box.CornerRadius = new CornerRadius(16);
        }
        else
        {
            box.CornerRadius = new CornerRadius(8);
            box.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
            box.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
            box.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        }
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(_s2 ? (byte)0x55 : (byte)0xA0, 0, 0, 0)) };
        backdrop.Children.Add(box);
        Content = backdrop;
        Show(_current);
    }

    /// <summary>A stick with its ring split in two halves (the outer and inner zone), with a soft glow behind each.</summary>
    private void AddStick(Canvas canvas, double cx, double cy, int firstZone)
    {
        const double R = 34, Thick = 9;
        var cap = new Ellipse { Width = 50, Height = 50, Fill = new RadialGradientBrush(Color.FromRgb(0x3A, 0x3E, 0x47), Color.FromRgb(0x18, 0x1A, 0x1F)) };
        Canvas.SetLeft(cap, cx - 25); Canvas.SetTop(cap, cy - 25);
        // The outer half faces away from the screen: left on the left stick, right on the right stick.
        bool leftStick = firstZone == 0;
        for (int half = 0; half < 2; half++)
        {
            bool outerHalf = half == 0;
            bool leftHalf = leftStick ? outerHalf : !outerHalf;
            var arc = Arc(cx, cy, R, leftHalf);
            int zone = firstZone + (leftStick ? half : 1 - half); // left stick: 0 outer, 1 inner; right stick: 2 inner, 3 outer
            var glow = new Path { Data = arc, StrokeThickness = Thick + 10, Opacity = 0.55, Effect = new BlurEffect { Radius = 14 }, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var line = new Path { Data = arc, StrokeThickness = Thick, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat };
            canvas.Children.Add(glow);
            canvas.Children.Add(line);
            _zones[zone] = line;
            _glows[zone] = glow;
        }
        canvas.Children.Add(cap);
    }

    /// <summary>Half a ring: the left half or the right half, with a small gap at the top and bottom.</summary>
    private static Geometry Arc(double cx, double cy, double r, bool leftHalf)
    {
        double gap = 6 * Math.PI / 180;
        double a0 = leftHalf ? Math.PI / 2 + gap : -Math.PI / 2 + gap, a1 = leftHalf ? 3 * Math.PI / 2 - gap : Math.PI / 2 - gap;
        var start = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var end = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        g.Freeze();
        return g;
    }

    private void Show(int index)
    {
        if (index < 0 || index >= _options.Length) return;
        _preview = _options[index].Preview;
        if (_name != null) _name.Text = _options[index].Name;
        if (_about != null) _about.Text = _options[index].About;
        Draw();
    }

    private void Draw()
    {
        double t = (DateTime.UtcNow - _started).TotalSeconds;
        var frame = LightFx.Frame(_preview, t, _inputs);
        for (int z = 0; z < 4; z++)
        {
            if (_zones[z] == null) continue;
            var c = frame[z];
            // A dark zone still shows the ring itself, faintly.
            var shown = c.R + c.G + c.B < 30 ? Color.FromRgb(0x30, 0x33, 0x3A) : c;
            _zones[z].Stroke = new SolidColorBrush(shown);
            _glows[z].Stroke = new SolidColorBrush(c);
        }
    }

    private void Choose(int index)
    {
        _done.TrySetResult(index);
        Host.Pop(this);
    }

    public override void OnShown() => _timer.Start();

    public override void FocusDefault()
    {
        if (_rows.Count > 0) _rows[Math.Clamp(_current, 0, _rows.Count - 1)].Focus();
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _done.TrySetResult(-1);
    }
}
