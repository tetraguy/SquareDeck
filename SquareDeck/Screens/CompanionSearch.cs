using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// With the companion on, it does Game Genie's searching itself (in the Genie's place): a big version of your
/// companion trying one way after another to find the answers on the web. It types furiously on a laptop (code and
/// question marks fly off the keys), sweeps a magnifying glass over floating web pages, opens tab after tab while a
/// loading ring spins, reads a thick guide with its eyes darting, holds up an antenna that catches the signal (Wi-Fi
/// arcs pulse above it), sorts answer cards into a neat stack, surfs a wave of links, and has the idea at last
/// (question marks, then a bulb and an exclamation mark). It bobs, blinks and looks around between acts, with a
/// line about what it's up to and three bouncing dots under it.
/// </summary>
public sealed class CompanionSearch : Grid
{
    private const double W = 360, H = 300, Size = 144, Cell = Size / 12;
    private const double SpriteX = (W - Size) / 2, SpriteY = 110;
    private const double ActLength = 3.4;
    private static readonly string[] Acts = { "type", "magnify", "tabs", "read", "antenna", "cards", "surf", "idea" };

    private readonly Canvas _stage = new() { Width = W, Height = H, ClipToBounds = false };
    private readonly Canvas _back = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly Canvas _fx = new() { Width = W, Height = H, IsHitTestVisible = false };
    private readonly Canvas _sprite = new() { Width = Size, Height = Size, RenderTransformOrigin = new Point(0.5, 1) };
    private readonly TranslateTransform _move = new();
    private readonly RotateTransform _lean = new();
    private readonly ScaleTransform _squash = new(1, 1);
    private readonly TextBlock _prop = Emoji(40);
    private readonly TextBlock _line;
    private readonly List<TextBlock> _dots = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly List<(FrameworkElement E, double X, double Y, double VX, double VY, DateTime Born, double Life, bool Fall)> _bits = new();
    private readonly string _name = CompanionArt.Name;
    private int _act = -1, _beat = -1;
    private string _lastFrame = "";

    /// <summary>How many are on screen: while one is, the companion's usual small self in the corner steps away.</summary>
    public static int Showing { get; private set; }
    private bool _counted;

    public CompanionSearch(string text)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var art = new Grid { Width = W, Height = H };
        art.Children.Add(_back);
        art.Children.Add(_stage);
        art.Children.Add(_fx);
        stack.Children.Add(new Viewbox { Width = W * 0.95, Height = H * 0.95, Child = art });
        var title = Ui.Text(text, 18, "FgBrush", bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxWidth = 620;
        title.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(title);
        _line = Ui.Text("", 15, "SubtleBrush");
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        lineRow.Children.Add(_line);
        for (int i = 0; i < 3; i++)
        {
            var dot = Ui.Text(".", 15, "SubtleBrush");
            dot.FontWeight = FontWeights.Bold;
            dot.RenderTransform = new TranslateTransform();
            _dots.Add(dot);
            lineRow.Children.Add(dot);
        }
        stack.Children.Add(lineRow);
        Children.Add(stack);

        // A soft glow under it, and the companion itself.
        var glow = new Ellipse { Width = 220, Height = 40, Fill = new RadialGradientBrush(Color.FromArgb(0x50, 0x7F, 0xB8, 0xFF), Color.FromArgb(0, 0x7F, 0xB8, 0xFF)) };
        Canvas.SetLeft(glow, (W - 220) / 2); Canvas.SetTop(glow, SpriteY + Size - 18);
        _back.Children.Add(glow);
        _sprite.RenderTransform = new TransformGroup { Children = { _squash, _lean, _move } };
        Canvas.SetLeft(_sprite, SpriteX); Canvas.SetTop(_sprite, SpriteY);
        _stage.Children.Add(_sprite);
        _stage.Children.Add(_prop);

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { if (!_counted) { _counted = true; Showing++; Refresh(); } };
        Unloaded += (_, _) => { if (_counted) { _counted = false; Showing--; Refresh(); } };
        Loaded += (_, _) => { if (Theme.ReduceMotion) { Draw("happy", false, 0, false); _line.Text = _name + " is searching"; } else _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Has the small companion step away (or come back) at once.</summary>
    private static void Refresh() => (Application.Current?.MainWindow as MainWindow)?.RefreshCompanion();

    private static TextBlock Emoji(double size) => new()
    {
        FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji"), RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(), IsHitTestVisible = false
    };

    private static SolidColorBrush B(byte r, byte g, byte b, byte a = 255) => new(Color.FromArgb(a, r, g, b));

    private string LineFor(string act) => act switch
    {
        "type" => _name + " is typing really fast",
        "magnify" => "Searching every corner of the web",
        "tabs" => "Opening way too many tabs",
        "read" => "Reading the walkthroughs",
        "antenna" => "Picking up the signal",
        "cards" => "Sorting through the answers",
        "surf" => "Surfing the web",
        _ => "Connecting the dots",
    };

    private void Draw(string mood, bool blink, int look, bool wave)
    {
        string frame = $"{mood}|{blink}|{look}|{wave}";
        if (frame == _lastFrame) return;
        _lastFrame = frame;
        CompanionArt.Draw(_sprite, CompanionArt.SpeciesId, CompanionArt.ColorHex, mood, blink && mood != "cheer", Labs.Outfit, look, wave, Cell);
    }

    /// <summary>A flying piece of the act (a character, a card, a spark) that moves, maybe falls, and fades.</summary>
    private void Bit(FrameworkElement e, double x, double y, double vx, double vy, double life, bool fall = false)
    {
        _fx.Children.Add(e);
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        _bits.Add((e, x, y, vx, vy, DateTime.UtcNow, life, fall));
    }

    private static TextBlock Glyph(string text, double size, Brush brush) => new() { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = brush, FontFamily = new FontFamily("Consolas") };

    /// <summary>A little floating web page: a title bar and a few lines.</summary>
    private static Border Page(double w, double h, Color accent)
    {
        var lines = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        lines.Children.Add(new Border { Height = 4, Width = w * 0.5, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left });
        for (int i = 0; i < 3; i++)
            lines.Children.Add(new Border { Height = 3, Width = w * (0.75 - i * 0.12), CornerRadius = new CornerRadius(1.5), Background = B(0xFF, 0xFF, 0xFF, 0x70), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
        return new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(6), Child = lines, Background = B(0x2A, 0x30, 0x40, 0xE8),
            BorderBrush = B(0xFF, 0xFF, 0xFF, 0x40), BorderThickness = new Thickness(1)
        };
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double t = (now - _start).TotalSeconds;
        int act = (int)(t / ActLength) % Acts.Length;
        double at = t % ActLength;
        string name = Acts[act];
        if (act != _act)
        {
            _act = act;
            _beat = -1;
            _back.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "act").ToList().ForEach(e => _back.Children.Remove(e));
            _line.Text = LineFor(name);
            // A little hop into each new act, with a puff of sparkles.
            for (int i = 0; i < 6; i++)
                Bit(Glyph("✦", 12, B(0xFF, 0xE2, 0x7A)), W / 2, SpriteY + 40, Random.Shared.Next(-80, 80), Random.Shared.Next(-90, -30), 0.7);
        }

        string mood = "happy";
        bool blink = (int)(t * 10) % 41 == 0, wave = false;
        int look = 0;
        double y = 0, x = 0, lean = 0, sx = 1, sy = 1;
        string prop = "";
        double px = 0, py = 0, pspin = 0, psize = 40;
        int beat;

        // Every act starts with a little hop.
        if (at < 0.3) { y = -Math.Sin(at / 0.3 * Math.PI) * 18; sy = 1 + 0.08 * Math.Sin(at / 0.3 * Math.PI); }

        switch (name)
        {
            case "type":
            {
                // A laptop in front, keys clacking (arms up and down), code and question marks flying off.
                prop = "💻"; psize = 54; px = W / 2 - 30; py = SpriteY + Size - 62;
                wave = (int)(at * 12) % 2 == 0;
                y += -Math.Abs(Math.Sin(at * 18)) * 2;
                look = 0;
                beat = (int)(at * 6);
                if (beat != _beat)
                {
                    _beat = beat;
                    string[] chars = { "</>", "?", "{ }", "www", "#", "?!", "01" };
                    Bit(Glyph(chars[Random.Shared.Next(chars.Length)], 14, Random.Shared.Next(2) == 0 ? B(0x7F, 0xD8, 0xBE) : B(0x7F, 0xB8, 0xFF)),
                        W / 2 + Random.Shared.Next(-30, 30), SpriteY + Size - 60, Random.Shared.Next(-60, 60), -90, 1.2);
                }
                mood = at > 2.6 ? "cheer" : "happy";
                break;
            }
            case "magnify":
            {
                // Web pages float around; a magnifying glass sweeps over them; it leans after it.
                if (_back.Children.OfType<FrameworkElement>().All(e => e.Tag as string != "act"))
                    for (int i = 0; i < 3; i++)
                    {
                        var p = Page(78, 52, i == 1 ? Color.FromRgb(0xFF, 0x8F, 0xB1) : Color.FromRgb(0x7F, 0xB8, 0xFF));
                        p.Tag = "act";
                        p.Opacity = 0.9;
                        Canvas.SetLeft(p, 20 + i * 110); Canvas.SetTop(p, 22 + (i % 2) * 22);
                        _back.Children.Add(p);
                    }
                double sweep = Math.Sin(at * 1.8);
                prop = "🔍"; psize = 42; px = W / 2 - 20 + sweep * 130; py = 40 + Math.Cos(at * 3.6) * 12;
                look = sweep > 0.3 ? 1 : sweep < -0.3 ? -1 : 0;
                lean = sweep * 6;
                wave = true;
                mood = at > 2.8 ? "cheer" : "happy";
                break;
            }
            case "tabs":
            {
                // Tabs pop up one after another across the top while a loading ring spins.
                beat = (int)(at / 0.4);
                if (beat != _beat && beat < 7)
                {
                    _beat = beat;
                    var tab = new Border { Width = 46, Height = 18, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = B(0x3A, 0x42, 0x58), BorderBrush = B(0xFF, 0xFF, 0xFF, 0x50), BorderThickness = new Thickness(1, 1, 1, 0), Tag = "act",
                        Child = new Border { Width = 26, Height = 3, CornerRadius = new CornerRadius(1.5), Background = B(0x7F, 0xD8, 0xBE), VerticalAlignment = VerticalAlignment.Center } };
                    Canvas.SetLeft(tab, 14 + beat * 48); Canvas.SetTop(tab, 34);
                    _back.Children.Add(tab);
                    y -= 6;
                }
                prop = "⟳"; psize = 30; px = W / 2 + 70; py = SpriteY + 10; pspin = at * 360;
                _prop.FontFamily = new FontFamily("Segoe UI Symbol");
                look = (int)(at * 2.5) % 3 - 1;
                wave = (int)(at * 2.5) % 2 == 0;
                mood = "happy";
                break;
            }
            case "read":
            {
                // A thick guide, pages turning, eyes darting along the lines.
                prop = "📖"; psize = 50; px = W / 2 - 26; py = SpriteY + 48;
                look = (int)(at * 3) % 2 == 0 ? -1 : 1;
                beat = (int)(at / 0.8);
                if (beat != _beat) { _beat = beat; Bit(Glyph("~", 18, B(0xFF, 0xFF, 0xFF, 0xB0)), W / 2 + 6, SpriteY + 52, 30, -40, 0.6); }
                y += Math.Sin(at * 2) * 2;
                mood = at > 2.9 ? "cheer" : "happy";
                break;
            }
            case "antenna":
            {
                // An antenna held up, Wi-Fi arcs pulsing out above it.
                prop = "📡"; psize = 40; px = W / 2 + 34; py = SpriteY - 6;
                wave = true;
                beat = (int)(at / 0.45);
                if (beat != _beat)
                {
                    _beat = beat;
                    var arc = new Path { Data = Geometry.Parse("M 0,14 A 20,20 0 0 1 40,14"), Stroke = B(0x7F, 0xD8, 0xBE), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    Bit(arc, W / 2 + 34, SpriteY - 20, 0, -40, 0.9);
                }
                lean = Math.Sin(at * 3) * 4;
                mood = at > 2.6 ? "cheer" : "happy";
                look = 1;
                break;
            }
            case "cards":
            {
                // Answer cards fly in from both sides and land in a neat stack it pats down.
                beat = (int)(at / 0.35);
                if (beat != _beat && beat < 8)
                {
                    _beat = beat;
                    bool fromLeft = beat % 2 == 0;
                    var card = Page(54, 36, fromLeft ? Color.FromRgb(0xFF, 0xD8, 0x5C) : Color.FromRgb(0xC3, 0xA6, 0xFF));
                    Bit(card, fromLeft ? -40 : W, 60 + Random.Shared.Next(0, 30), fromLeft ? 420 : -420, 60, 0.45);
                }
                wave = (int)(at * 4) % 2 == 0;
                look = (int)(at / 0.35) % 2 == 0 ? -1 : 1;
                if (at > 2.8) { mood = "cheer"; sy = 1 - 0.06 * Math.Sin((at - 2.8) * 12); }
                break;
            }
            case "surf":
            {
                // Riding a wave of links: side to side, leaning into it, "www" spray behind.
                x = Math.Sin(at * 2.2) * 70;
                lean = Math.Cos(at * 2.2) * 10;
                y += -Math.Abs(Math.Sin(at * 4.4)) * 8;
                look = Math.Cos(at * 2.2) > 0 ? 1 : -1;
                mood = "cheer";
                prop = "🌊"; psize = 46; px = W / 2 - 23 + x; py = SpriteY + Size - 46;
                beat = (int)(at * 5);
                if (beat != _beat) { _beat = beat; Bit(Glyph("www", 11, B(0x7F, 0xB8, 0xFF)), W / 2 + x - look * 60, SpriteY + Size - 40, -look * 50, -30, 0.8); }
                break;
            }
            default: // idea
            {
                // Question marks rise while it thinks, then the bulb flashes and it jumps: got it!
                if (at < 2.0)
                {
                    mood = "happy";
                    look = (int)(at * 1.5) % 2 == 0 ? -1 : 1;
                    beat = (int)(at / 0.4);
                    if (beat != _beat) { _beat = beat; Bit(Glyph("?", 22 + Random.Shared.Next(8), B(0xFF, 0xD1, 0x6A)), W / 2 + Random.Shared.Next(-60, 60), SpriteY - 10, Random.Shared.Next(-20, 20), -60, 1.0); }
                    lean = Math.Sin(at * 2) * 5;
                }
                else
                {
                    prop = "💡"; psize = 44; px = W / 2 - 22; py = SpriteY - 64;
                    mood = "cheer"; wave = true;
                    double k = at - 2.0;
                    y = -Math.Abs(Math.Sin(k * 6)) * 20;
                    if (_beat != 99)
                    {
                        _beat = 99;
                        Bit(Glyph("!", 34, B(0xFF, 0xE2, 0x7A)), W / 2 + 40, SpriteY - 40, 20, -50, 1.0);
                        for (int i = 0; i < 10; i++)
                        {
                            double a = i / 10.0 * Math.PI * 2;
                            Bit(Glyph("✦", 12, B(0xFF, 0xE2, 0x7A)), W / 2, SpriteY - 40, Math.Cos(a) * 110, Math.Sin(a) * 110, 0.8);
                        }
                    }
                }
                break;
            }
        }
        if (name != "tabs") _prop.FontFamily = new FontFamily("Segoe UI Emoji");

        // Flying pieces.
        for (int i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            double age = (now - b.Born).TotalSeconds;
            if (age > b.Life) { _fx.Children.Remove(b.E); _bits.RemoveAt(i); continue; }
            Canvas.SetLeft(b.E, b.X + b.VX * age);
            Canvas.SetTop(b.E, b.Y + b.VY * age + (b.Fall ? 300 * age * age : 0));
            b.E.Opacity = Math.Min(1, (b.Life - age) / 0.3);
        }

        Draw(mood, blink, look, wave);
        _move.X = x;
        _move.Y = y;
        _lean.Angle = lean;
        _squash.ScaleX = sx;
        _squash.ScaleY = sy;
        _prop.Text = prop;
        _prop.FontSize = psize;
        Canvas.SetLeft(_prop, px);
        Canvas.SetTop(_prop, py);
        ((RotateTransform)_prop.RenderTransform).Angle = pspin;
        _prop.RenderTransformOrigin = new Point(0.5, 0.5);

        // The three dots bounce in a wave.
        for (int i = 0; i < _dots.Count; i++)
            ((TranslateTransform)_dots[i].RenderTransform).Y = -Math.Max(0, Math.Sin(t * 6 - i * 0.7)) * 4;
    }
}
